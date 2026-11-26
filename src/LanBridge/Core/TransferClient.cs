using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
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

        public async Task<TransferResult> SendAsync(
            PeerEndpoint peer,
            IReadOnlyList<string> filePaths,
            CancellationToken cancellationToken = default)
        {
            if (peer == null) throw new ArgumentNullException(nameof(peer));
            if (filePaths == null) throw new ArgumentNullException(nameof(filePaths));
            cancellationToken.ThrowIfCancellationRequested();

            var files = BuildFiles(filePaths);
            ClientSession session = null;
            var completed = 0;
            IProgress<TransferProgress> progress = null;
            var transferCompleted = false;
            var failureCode = "cancelled";

            try
            {
                session = await OpenSessionAsync(peer, "send", cancellationToken).ConfigureAwait(false);
                if (!session.Ack.Paired || !_peers.ContainsFingerprint(peer.Fingerprint))
                    return TransferResult.Fail("not_paired", 0, files.Count);
                if (session.Ack.Busy)
                    return TransferResult.Fail("busy_inbound", 0, files.Count);

                var offer = new TransferOffer(peer, Guid.NewGuid().ToString("N"), files);
                var offerDto = new OfferDto
                {
                    TransferId = offer.TransferId,
                    TotalSize = offer.TotalSize,
                    Files = files.Select(file => new OfferFileDto { Name = file.Name, Size = file.Size }).ToList()
                };

                await WriteJsonAsync(session.Stream, FrameType.Offer, offerDto, cancellationToken).ConfigureAwait(false);

                Frame decision;
                try
                {
                    decision = await TimeoutIo.ReadFrameAsync(
                        session.Stream,
                        _options.OfferTimeout,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    return TransferResult.Fail("timeout", 0, files.Count);
                }

                if (decision.Type == FrameType.Error)
                {
                    var error = ProtocolJson.Deserialize<ErrorDto>(decision.Payload);
                    return TransferResult.Fail(error.Code, 0, files.Count);
                }

                if (decision.Type == FrameType.Reject)
                {
                    var reject = ProtocolJson.Deserialize<RejectDto>(decision.Payload);
                    return TransferResult.Fail(
                        string.Equals(reject.Reason, "disk_full", StringComparison.OrdinalIgnoreCase)
                            ? "disk_full"
                            : "offer_rejected",
                        0,
                        files.Count);
                }

                if (decision.Type != FrameType.Accept)
                    return TransferResult.Fail("offer_rejected", 0, files.Count);

                progress = _prompter.CreateSendProgress(offer, cancellationToken);

                for (var index = 0; index < files.Count; index++)
                {
                    var file = files[index];
                    await WriteJsonAsync(
                        session.Stream,
                        FrameType.FileBegin,
                        new FileBeginDto { Index = index, Name = file.Name, Size = file.Size },
                        cancellationToken).ConfigureAwait(false);

                    using (var stream = new FileStream(
                        file.LocalPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        1,
                        FileOptions.SequentialScan))
                    using (var sha = new SHA256Cng())
                    {
                        // One reusable frame buffer per file. The data is read
                        // directly after the 9-byte frame header, so bulk transfer
                        // does not allocate a new array for every 64 KiB chunk.
                        var frameBuffer = new byte[FrameCodec.FileDataHeaderSize + FrameCodec.MaxFileChunk];
                        var hashScratch = new byte[FrameCodec.MaxFileChunk];
                        long sent = 0;

                        while (true)
                        {
                            var read = stream.Read(
                                frameBuffer,
                                FrameCodec.FileDataHeaderSize,
                                FrameCodec.MaxFileChunk);
                            if (read <= 0) break;

                            sha.TransformBlock(frameBuffer, FrameCodec.FileDataHeaderSize, read, hashScratch, 0);
                            FrameCodec.WriteFileDataHeader(frameBuffer, index, read);
                            await TimeoutIo.WriteFileDataAsync(
                                session.Stream,
                                frameBuffer,
                                FrameCodec.FileDataHeaderSize + read,
                                _options.FileIdleTimeout,
                                cancellationToken).ConfigureAwait(false);

                            sent += read;
                            progress.Report(new TransferProgress
                            {
                                FileName = file.Name,
                                FileIndex = index,
                                FileCount = files.Count,
                                BytesTransferred = sent,
                                TotalBytes = file.Size
                            });
                        }

                        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                        await WriteJsonAsync(
                            session.Stream,
                            FrameType.FileEnd,
                            new FileEndDto { Index = index, Sha256 = Hex(sha.Hash) },
                            cancellationToken).ConfigureAwait(false);
                    }

                    Frame resultFrame;
                    try
                    {
                        resultFrame = await TimeoutIo.ReadFrameAsync(
                            session.Stream,
                            _options.ControlTimeout,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                        failureCode = "timeout";
                        return TransferResult.Fail("timeout", completed, files.Count);
                    }

                    if (resultFrame.Type == FrameType.FileOk)
                    {
                        completed++;
                        progress.Report(new TransferProgress
                        {
                            FileName = file.Name,
                            FileIndex = index,
                            FileCount = files.Count,
                            BytesTransferred = file.Size,
                            TotalBytes = file.Size,
                            IsComplete = true
                        });
                        continue;
                    }

                    if (resultFrame.Type == FrameType.FileError)
                    {
                        var error = ProtocolJson.Deserialize<FileErrorDto>(resultFrame.Payload);
                        _log.Info("Receiver rejected " + file.Name + ": " + Sanitizer.SanitizeLogValue(error.Code));
                        failureCode = "transfer_failed";
                        return TransferResult.Fail("transfer_failed", completed, files.Count);
                    }

                    if (resultFrame.Type == FrameType.Error)
                    {
                        var error = ProtocolJson.Deserialize<ErrorDto>(resultFrame.Payload);
                        failureCode = error.Code;
                        return TransferResult.Fail(
                            error.Code,
                            error.Completed > 0 ? error.Completed : completed,
                            files.Count);
                    }

                    failureCode = "transfer_failed";
                    return TransferResult.Fail("transfer_failed", completed, files.Count);
                }

                await WriteJsonAsync(
                    session.Stream,
                    FrameType.Done,
                    new DoneDto { TransferId = offer.TransferId },
                    cancellationToken).ConfigureAwait(false);
                var done = await TimeoutIo.ReadFrameAsync(
                    session.Stream,
                    _options.ControlTimeout,
                    cancellationToken).ConfigureAwait(false);
                if (done.Type != FrameType.DoneAck)
                {
                    failureCode = "transfer_failed";
                    return TransferResult.Fail("done_failed", completed, files.Count);
                }

                transferCompleted = true;
                return TransferResult.Ok(completed, files.Count);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (session != null) await SendCancelAsync(session.Stream).ConfigureAwait(false);
                failureCode = "cancelled";
                return TransferResult.Fail("cancelled", completed, files.Count);
            }
            catch (ProtocolException ex)
            {
                _log.Info("Send to " + peer.Name + " failed: " + Sanitizer.SanitizeLogValue(ex.Code));
                failureCode = ex.Code;
                return TransferResult.Fail(ex.Code, completed, files.Count);
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is AuthenticationException ||
                ex is SocketException ||
                ex is TimeoutException)
            {
                _log.Info("Send to " + peer.Name + " failed: " + ex.GetType().Name + ": " + Sanitizer.SanitizeLogValue(ex.Message));
                failureCode = ex is TimeoutException ? "timeout" : "transfer_failed";
                return TransferResult.Fail(failureCode, completed, files.Count);
            }
            finally
            {
                if (progress != null && !transferCompleted)
                {
                    progress.Report(new TransferProgress
                    {
                        Failed = true,
                        ErrorCode = failureCode
                    });
                }

                session?.Dispose();
            }
        }

        private Task WriteJsonAsync<T>(SslStream ssl, FrameType type, T value, CancellationToken cancellationToken)
        {
            return TimeoutIo.WriteFrameAsync(
                ssl,
                type,
                ProtocolJson.Serialize(value),
                _options.ControlTimeout,
                cancellationToken);
        }

        private static List<TransferFile> BuildFiles(IReadOnlyList<string> filePaths)
        {
            if (filePaths.Count == 0 || filePaths.Count > 5000)
                throw new ArgumentException("Select between 1 and 5,000 files.", nameof(filePaths));

            var files = new List<TransferFile>();
            foreach (var path in filePaths)
            {
                var info = new FileInfo(path);
                if (!info.Exists) throw new FileNotFoundException("File not found.", path);
                files.Add(new TransferFile(info.Name, info.Length, info.FullName));
            }
            return files;
        }

        private static async Task SendCancelAsync(SslStream stream)
        {
            try
            {
                await TimeoutIo.WriteFrameAsync(
                    stream,
                    FrameType.Cancel,
                    ProtocolJson.Serialize(new CancelDto { Reason = "cancelled" }),
                    TimeSpan.FromSeconds(2),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        private static string Hex(byte[] bytes)
        {
            if (bytes == null) return string.Empty;
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes) builder.Append(value.ToString("x2"));
            return builder.ToString();
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
