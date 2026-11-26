using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace LanBridge.Core
{
    public sealed class TransferClient
    {
        private readonly string _deviceName;
        private readonly DeviceIdentity _identity;
        private readonly PeerStore _peers;
        private readonly IClientPrompter _prompter;
        private readonly ILogger _log;
        private readonly TransferOptions _options;
        private readonly int _localPort;

        public TransferClient(
            string deviceName,
            DeviceIdentity identity,
            PeerStore peers,
            IClientPrompter prompter,
            ILogger log,
            TransferOptions options = null,
            int localPort = 45873)
        {
            if (localPort <= 0 || localPort > 65535) throw new ArgumentOutOfRangeException(nameof(localPort));
            _deviceName = Sanitizer.SanitizeDeviceName(deviceName);
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));
            _peers = peers ?? throw new ArgumentNullException(nameof(peers));
            _prompter = prompter ?? throw new ArgumentNullException(nameof(prompter));
            _log = log ?? NullLogger.Instance;
            _options = options ?? new TransferOptions();
            _localPort = localPort;
        }

        public async Task<bool> PingAsync(PeerEndpoint peer, CancellationToken cancellationToken = default)
        {
            if (peer == null) throw new ArgumentNullException(nameof(peer));
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using (var session = await OpenSessionAsync(peer, "ping", cancellationToken).ConfigureAwait(false))
                {
                    await TimeoutIo.WriteFrameAsync(
                        session.Stream,
                        FrameType.Ping,
                        Array.Empty<byte>(),
                        _options.ControlTimeout,
                        cancellationToken).ConfigureAwait(false);

                    var frame = await TimeoutIo.ReadFrameAsync(
                        session.Stream,
                        _options.ControlTimeout,
                        cancellationToken).ConfigureAwait(false);

                    return frame.Type == FrameType.Pong;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is AuthenticationException ||
                ex is ProtocolException ||
                ex is SocketException ||
                ex is TimeoutException)
            {
                _log.Info("Ping to " + peer.Name + " failed: " + ex.GetType().Name + ": " + Sanitizer.SanitizeLogValue(ex.Message));
                return false;
            }
        }

        public async Task<PairingAttemptResult> PairAsync(
            PeerEndpoint peer,
            CancellationToken cancellationToken = default)
        {
            if (peer == null) throw new ArgumentNullException(nameof(peer));
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using (var session = await OpenSessionAsync(peer, "pair", cancellationToken).ConfigureAwait(false))
                {
                    var alreadyPaired = _peers.ContainsFingerprint(peer.Fingerprint);
                    if (session.Ack.Paired && alreadyPaired)
                    {
                        var existing = _peers.FindByFingerprint(peer.Fingerprint);
                        if (existing != null)
                        {
                            _peers.UpdateEndpoint(peer.Fingerprint, peer.Address.ToString(), peer.Port);
                            _peers.Save();
                            return PairingAttemptResult.Ok(existing);
                        }
                    }

                    Frame promptFrame;
                    try
                    {
                        promptFrame = await TimeoutIo.ReadFrameAsync(
                            session.Stream,
                            _options.PairApprovalTimeout,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                        return PairingAttemptResult.Fail("timeout");
                    }

                    if (promptFrame.Type == FrameType.PairDeny)
                    {
                        var deny = ProtocolJson.Deserialize<PairDenyDto>(promptFrame.Payload);
                        return PairingAttemptResult.Fail(deny.Reason);
                    }

                    if (promptFrame.Type != FrameType.PairPrompt)
                        return PairingAttemptResult.Fail("invalid");

                    byte[] serverNonce;
                    try
                    {
                        serverNonce = Convert.FromBase64String(session.Ack.Nonce ?? string.Empty);
                    }
                    catch (FormatException)
                    {
                        return PairingAttemptResult.Fail("invalid");
                    }

                    var code = PairingCode.Compute(
                        _identity.Fingerprint,
                        peer.Fingerprint,
                        session.Nonce,
                        serverNonce);

                    using (var codeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        codeCts.CancelAfter(_options.PairCodeTimeout);
                        var previousFailed = false;

                        for (var attempt = 1; attempt <= 3; attempt++)
                        {
                            string typed;
                            try
                            {
                                typed = await _prompter.RequestPairingCodeAsync(
                                    new PairingCodePrompt(peer, attempt, previousFailed),
                                    codeCts.Token).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                            {
                                await SendAbortAsync(session.Stream, cancellationToken).ConfigureAwait(false);
                                return PairingAttemptResult.Fail("timeout");
                            }

                            if (string.IsNullOrWhiteSpace(typed))
                            {
                                await SendAbortAsync(session.Stream, cancellationToken).ConfigureAwait(false);
                                return PairingAttemptResult.Fail("timeout");
                            }

                            var normalized = new string(typed.Where(char.IsDigit).ToArray());
                            if (!string.Equals(normalized, code, StringComparison.Ordinal))
                            {
                                previousFailed = true;
                                continue;
                            }

                            var proof = PairingCode.ComputeProof(
                                code,
                                session.Nonce,
                                serverNonce,
                                _identity.Fingerprint,
                                peer.Fingerprint);

                            await TimeoutIo.WriteFrameAsync(
                                session.Stream,
                                FrameType.PairProof,
                                ProtocolJson.Serialize(new PairProofDto
                                {
                                    Proof = Convert.ToBase64String(proof)
                                }),
                                _options.ControlTimeout,
                                codeCts.Token).ConfigureAwait(false);

                            Frame resultFrame;
                            try
                            {
                                resultFrame = await TimeoutIo.ReadFrameAsync(
                                    session.Stream,
                                    _options.PairCodeTimeout,
                                    codeCts.Token).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                            {
                                await SendAbortAsync(session.Stream, cancellationToken).ConfigureAwait(false);
                                return PairingAttemptResult.Fail("timeout");
                            }

                            if (resultFrame.Type == FrameType.PairOk)
                            {
                                var record = UpsertServerPeer(peer, session.Ack);
                                return PairingAttemptResult.Ok(record);
                            }

                            if (resultFrame.Type == FrameType.PairDeny)
                            {
                                var deny = ProtocolJson.Deserialize<PairDenyDto>(resultFrame.Payload);
                                return PairingAttemptResult.Fail(deny.Reason);
                            }

                            return PairingAttemptResult.Fail("invalid");
                        }

                        await SendAbortAsync(session.Stream, cancellationToken).ConfigureAwait(false);
                        return PairingAttemptResult.Fail("wrong_code");
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (ProtocolException ex)
            {
                _log.Info("Pairing with " + peer.Name + " failed: " + Sanitizer.SanitizeLogValue(ex.Code));
                return PairingAttemptResult.Fail(ex.Code);
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is AuthenticationException ||
                ex is SocketException ||
                ex is TimeoutException)
            {
                _log.Info("Pairing with " + peer.Name + " failed: " + ex.GetType().Name + ": " + Sanitizer.SanitizeLogValue(ex.Message));
                return PairingAttemptResult.Fail(ex is TimeoutException ? "timeout" : "pairing_failed");
            }
        }

        public Task<TransferResult> SendAsync(
            PeerEndpoint peer,
            IReadOnlyList<string> filePaths,
            CancellationToken cancellationToken = default)
        {
            if (peer == null) throw new ArgumentNullException(nameof(peer));
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(TransferResult.Fail("not_implemented"));
        }

        private PeerRecord UpsertServerPeer(PeerEndpoint peer, HelloAckDto ack)
        {
            var record = new PeerRecord
            {
                Fingerprint = peer.Fingerprint,
                DeviceId = ack.Id ?? string.Empty,
                Name = Sanitizer.SanitizeDeviceName(ack.Name),
                LastIp = peer.Address.ToString(),
                LastPort = peer.Port,
                PairedAtUtc = DateTime.UtcNow,
                LastSeenUtc = DateTime.UtcNow
            };
            _peers.Upsert(record);
            _peers.Save();
            return record;
        }

        private static async Task SendAbortAsync(SslStream stream, CancellationToken cancellationToken)
        {
            try
            {
                await TimeoutIo.WriteFrameAsync(
                    stream,
                    FrameType.PairAbort,
                    Array.Empty<byte>(),
                    TimeSpan.FromSeconds(2),
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        private async Task<ClientSession> OpenSessionAsync(
            PeerEndpoint peer,
            string intent,
            CancellationToken cancellationToken)
        {
            var tcp = new TcpClient(AddressFamily.InterNetwork);
            SslStream ssl = null;

            try
            {
                await TimeoutIo.WithTimeout(
                    tcp.ConnectAsync(peer.Address, peer.Port),
                    _options.ConnectTimeout,
                    cancellationToken).ConfigureAwait(false);
                tcp.NoDelay = true;

                ssl = Tls.CreateClient(tcp.GetStream(), _identity.Certificate, peer.Fingerprint);
                await TimeoutIo.WithTimeout(
                    ssl.AuthenticateAsClientAsync(
                        "lanbridge",
                        new X509CertificateCollection { _identity.Certificate },
                        SslProtocols.Tls12,
                        false),
                    _options.ControlTimeout,
                    cancellationToken).ConfigureAwait(false);

                var clientNonce = NewNonceBytes();
                var hello = new HelloDto
                {
                    V = 1,
                    Id = _identity.DeviceId,
                    Name = _deviceName,
                    Fp = _identity.Fingerprint,
                    Nonce = Convert.ToBase64String(clientNonce),
                    Intent = intent,
                    Paired = _peers.ContainsFingerprint(peer.Fingerprint),
                    Port = _localPort
                };

                await TimeoutIo.WriteFrameAsync(
                    ssl,
                    FrameType.Hello,
                    ProtocolJson.Serialize(hello),
                    _options.ControlTimeout,
                    cancellationToken).ConfigureAwait(false);

                var frame = await TimeoutIo.ReadFrameAsync(
                    ssl,
                    _options.ControlTimeout,
                    cancellationToken).ConfigureAwait(false);

                if (frame.Type == FrameType.Error)
                {
                    var error = ProtocolJson.Deserialize<ErrorDto>(frame.Payload);
                    throw new ProtocolException(error.Code, error.Message);
                }

                if (frame.Type != FrameType.HelloAck)
                    throw new ProtocolException("hello_ack_required", "Expected HELLO_ACK from the other laptop.");

                var ack = ProtocolJson.Deserialize<HelloAckDto>(frame.Payload);
                if (ack.V != 1)
                    throw new ProtocolException("version", "The other laptop is running an incompatible version.");
                if (!string.Equals(ack.Fp, peer.Fingerprint, StringComparison.OrdinalIgnoreCase))
                    throw new ProtocolException("identity_mismatch", "The server certificate did not match the pinned fingerprint.");

                return new ClientSession(tcp, ssl, ack, clientNonce);
            }
            catch
            {
                try { ssl?.Dispose(); } catch { }
                try { tcp.Close(); } catch { }
                throw;
            }
        }

        private static byte[] NewNonceBytes()
        {
            var nonce = new byte[16];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(nonce);
            return nonce;
        }

        private sealed class ClientSession : IDisposable
        {
            public TcpClient Tcp { get; }
            public SslStream Stream { get; }
            public HelloAckDto Ack { get; }
            public byte[] Nonce { get; }

            public ClientSession(TcpClient tcp, SslStream stream, HelloAckDto ack, byte[] nonce)
            {
                Tcp = tcp;
                Stream = stream;
                Ack = ack;
                Nonce = nonce;
            }

            public void Dispose()
            {
                try { Stream?.Dispose(); } catch { }
                try { Tcp?.Close(); } catch { }
            }
        }
    }
}
