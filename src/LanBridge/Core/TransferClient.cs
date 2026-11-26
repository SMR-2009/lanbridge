using System;
using System.Collections.Generic;
using System.IO;
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

        public TransferClient(
            string deviceName,
            DeviceIdentity identity,
            PeerStore peers,
            IClientPrompter prompter,
            ILogger log,
            TransferOptions options = null)
        {
            _deviceName = Sanitizer.SanitizeDeviceName(deviceName);
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));
            _peers = peers ?? throw new ArgumentNullException(nameof(peers));
            _prompter = prompter ?? throw new ArgumentNullException(nameof(prompter));
            _log = log ?? NullLogger.Instance;
            _options = options ?? new TransferOptions();
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

        public Task<PairingAttemptResult> PairAsync(PeerEndpoint peer, CancellationToken cancellationToken = default)
        {
            if (peer == null) throw new ArgumentNullException(nameof(peer));
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(PairingAttemptResult.Fail("not_implemented"));
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

                var hello = new HelloDto
                {
                    V = 1,
                    Id = _identity.DeviceId,
                    Name = _deviceName,
                    Fp = _identity.Fingerprint,
                    Nonce = NewNonce(),
                    Intent = intent,
                    Paired = _peers.ContainsFingerprint(peer.Fingerprint)
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

                return new ClientSession(tcp, ssl, ack);
            }
            catch
            {
                try { ssl?.Dispose(); } catch { }
                try { tcp.Close(); } catch { }
                throw;
            }
        }

        private static string NewNonce()
        {
            var nonce = new byte[16];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(nonce);
            return Convert.ToBase64String(nonce);
        }

        private sealed class ClientSession : IDisposable
        {
            public TcpClient Tcp { get; }
            public SslStream Stream { get; }
            public HelloAckDto Ack { get; }

            public ClientSession(TcpClient tcp, SslStream stream, HelloAckDto ack)
            {
                Tcp = tcp;
                Stream = stream;
                Ack = ack;
            }

            public void Dispose()
            {
                try { Stream?.Dispose(); } catch { }
                try { Tcp?.Close(); } catch { }
            }
        }
    }
}
