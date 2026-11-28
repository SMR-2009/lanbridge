using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace LanBridge.Core
{
    public sealed class TransferServer : IDisposable
    {
        private readonly int _port;
        private readonly string _deviceName;
        private readonly DeviceIdentity _identity;
        private readonly PeerStore _peers;
        private readonly Func<string> _receiveFolder;
        private readonly ITransferPrompter _prompter;
        private readonly ILogger _log;
        private readonly TransferOptions _options;
        private readonly bool _allowLoopback;
        private readonly Func<LanScope> _scopeFactory;
        private readonly Func<string, string, long, FileReceiver> _receiverFactory;
        private const int DefaultMaxConcurrentClients = 12;
        private readonly ConcurrentDictionary<TcpClient, byte> _clients = new ConcurrentDictionary<TcpClient, byte>();
        private readonly ConcurrentDictionary<IPAddress, int> _clientsPerIp = new ConcurrentDictionary<IPAddress, int>();
        private readonly SemaphoreSlim _clientSlots;
        private const int MaxClientsPerIp = 4;
        private long _pairingCooldownUntilTicks;
        private int _rejectedClientCount;

        private readonly SemaphoreSlim _pairingLock = new SemaphoreSlim(1, 1);
        private int _cancelRequested;
        private int _stopping;
        private TcpListener _listener;
        private CancellationTokenSource _cancellation;
        private Task _acceptLoop;
        private int _receiving;
        private bool _disposed;

        private volatile bool _discoverable = true;
        public bool Discoverable
        {
            get => _discoverable;
            set => _discoverable = value;
        }

        public volatile bool AutoAcceptFromPaired;
        public bool IsReceiving => Volatile.Read(ref _receiving) != 0;
        public long MaxReceiveBytes
        {
            get => _options.MaxReceiveBytes;
            set => _options.MaxReceiveBytes = value > 0 ? value : _options.MaxReceiveBytes;
        }
        public int RejectedClientCount => Volatile.Read(ref _rejectedClientCount);
        public int ActiveClientCount => _clients.Count;
        public event Action<PeerRecord> DevicePaired;

        public TransferServer(
            int port,
            string deviceName,
            DeviceIdentity identity,
            PeerStore peers,
            Func<string> receiveFolder,
            ITransferPrompter prompter,
            ILogger log,
            TransferOptions options = null,
            bool allowLoopback = false,
            Func<LanScope> scopeFactory = null,
            Func<string, string, long, FileReceiver> receiverFactory = null,
            int maxConcurrentClients = DefaultMaxConcurrentClients)
        {
            if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            if (maxConcurrentClients <= 0 || maxConcurrentClients > 1024)
                throw new ArgumentOutOfRangeException(nameof(maxConcurrentClients));
            _port = port;
            _deviceName = Sanitizer.SanitizeDeviceName(deviceName);
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));
            _peers = peers ?? throw new ArgumentNullException(nameof(peers));
            _receiveFolder = receiveFolder ?? throw new ArgumentNullException(nameof(receiveFolder));
            _prompter = prompter ?? throw new ArgumentNullException(nameof(prompter));
            _log = log ?? NullLogger.Instance;
            _options = options ?? new TransferOptions();
            _allowLoopback = allowLoopback;
            _scopeFactory = scopeFactory ?? LanScope.Capture;
            _receiverFactory = receiverFactory ?? ((folder, name, size) => new FileReceiver(folder, name, size));
            _clientSlots = new SemaphoreSlim(maxConcurrentClients, maxConcurrentClients);
        }

        public void Start()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TransferServer));
            if (_listener != null) throw new InvalidOperationException("The server is already started.");

            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            Interlocked.Exchange(ref _stopping, 0);
            _cancellation = new CancellationTokenSource();
            _acceptLoop = Task.Run(() => AcceptLoopAsync(_cancellation.Token));
            _log.Info("Transfer server listening on port " + _port);
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = null;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    _log.Warn("Accept failed: " + Sanitizer.SanitizeLogValue(ex.Message));
                    continue;
                }

                if (client == null) continue;

                if (!_clientSlots.Wait(0))
                {
                    var rejected = Interlocked.Increment(ref _rejectedClientCount);
                    if (rejected == 1 || rejected % 25 == 0)
                    {
                        _log.Warn("Rejected inbound connection: concurrent client limit reached (" + rejected + " rejected).");
                    }

                    try { client.Close(); } catch { }
                    continue;
                }

                IPAddress remote;
                try
                {
                    remote = ((IPEndPoint)client.Client.RemoteEndPoint).Address;
                }
                catch
                {
                    try { client.Close(); } catch { }
                    _clientSlots.Release();
                    continue;
                }

                var perIp = _clientsPerIp.AddOrUpdate(remote, 1, (key, current) => current + 1);
                if (perIp > MaxClientsPerIp)
                {
                    DecrementIp(remote);
                    var rejected = Interlocked.Increment(ref _rejectedClientCount);
                    if (rejected == 1 || rejected % 25 == 0)
                    {
                        _log.Warn("Rejected inbound connection: per-IP limit reached for " + remote + " (" + rejected + " rejected).");
                    }

                    try { client.Close(); } catch { }
                    _clientSlots.Release();
                    continue;
                }

                if (!IsRemoteAllowed(remote))
                {
                    DecrementIp(remote);
                    _log.Warn("Rejected non-LAN connection from " + remote);
                    try { client.Close(); } catch { }
                    _clientSlots.Release();
                    continue;
                }

                _clients[client] = 0;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await HandleClientAsync(client, remote, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        _clients.TryRemove(client, out _);
                        DecrementIp(remote);
                        try { client.Close(); } catch { }
                        _clientSlots.Release();
                    }
                });
            }
        }

        private async Task HandleClientAsync(TcpClient client, IPAddress remote, CancellationToken cancellationToken)
        {
            try
            {
                client.NoDelay = true;

                using (var ssl = Tls.CreateServer(client.GetStream(), _identity.Certificate))
                {
                    await TimeoutIo.WithTimeout(
                        ssl.AuthenticateAsServerAsync(_identity.Certificate, true, Tls.Protocol, false),
                        _options.ControlTimeout,
                        cancellationToken).ConfigureAwait(false);

                    _log.Info("TLS negotiated (server): " + ssl.SslProtocol
                        + "; cipher=" + ssl.CipherAlgorithm + "-" + ssl.CipherStrength
                        + "; hash=" + ssl.HashAlgorithm);

                    var remoteCertificate = ssl.RemoteCertificate;
                    if (remoteCertificate == null)
                    {
                        await SendErrorAsync(ssl, "identity_required", cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    string clientFingerprint;
                    using (var clientCertificate = new X509Certificate2(remoteCertificate))
                    {
                        clientFingerprint = Tls.Fingerprint(clientCertificate);
                    }

                    var helloFrame = await TimeoutIo.ReadFrameAsync(ssl, _options.ControlTimeout, cancellationToken).ConfigureAwait(false);
                    if (helloFrame.Type != FrameType.Hello)
                    {
                        await SendErrorAsync(ssl, "hello_required", cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    HelloDto hello;
                    try
                    {
                        hello = ProtocolJson.Deserialize<HelloDto>(helloFrame.Payload);
                    }
                    catch (ProtocolException)
                    {
                        await SendErrorAsync(ssl, "invalid_hello", cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    if (hello.V != 1)
                    {
                        await SendErrorAsync(ssl, "version", cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    if (!string.Equals(hello.Fp, clientFingerprint, StringComparison.OrdinalIgnoreCase))
                    {
                        await SendErrorAsync(ssl, "identity_mismatch", cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    if (hello.Intent != "ping" && hello.Intent != "pair" && hello.Intent != "send")
                    {
                        await SendErrorAsync(ssl, "bad_intent", cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    var serverNonce = NewNonceBytes();
                    var ack = new HelloAckDto
                    {
                        V = 1,
                        Id = _identity.DeviceId,
                        Name = _deviceName,
                        Fp = _identity.Fingerprint,
                        Nonce = Convert.ToBase64String(serverNonce),
                        Paired = _peers.ContainsFingerprint(clientFingerprint),
                        Busy = IsReceiving,
                        Port = _port
                    };
                    await WriteJsonAsync(ssl, FrameType.HelloAck, ack, cancellationToken).ConfigureAwait(false);

                    if (hello.Intent == "ping")
                    {
                        var ping = await TimeoutIo.ReadFrameAsync(ssl, _options.ControlTimeout, cancellationToken).ConfigureAwait(false);
                        if (ping.Type != FrameType.Ping)
                        {
                            await SendErrorAsync(ssl, "ping_required", cancellationToken).ConfigureAwait(false);
                            return;
                        }

                        await TimeoutIo.WriteFrameAsync(
                            ssl,
                            FrameType.Pong,
                            Array.Empty<byte>(),
                            _options.ControlTimeout,
                            cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    if (hello.Intent == "pair")
                    {
                        await HandlePairAsync(ssl, hello, clientFingerprint, remote, serverNonce, cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    if (hello.Intent == "send")
                    {
                        await HandleSendAsync(ssl, client, hello, ack, clientFingerprint, remote, cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    await SendErrorAsync(ssl, "bad_intent", cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _log.Warn("Connection failed: " + ex.GetType().Name + ": " + Sanitizer.SanitizeLogValue(ex.Message));
            }
        }

        private void DecrementIp(IPAddress remote)
        {
            if (remote == null) return;
            _clientsPerIp.AddOrUpdate(remote, 0, (key, current) => Math.Max(0, current - 1));
        }

        public void CancelActiveTransfer()
        {
            Interlocked.Exchange(ref _cancelRequested, 1);
        }

        private async Task HandlePairAsync(
            SslStream ssl,
            HelloDto hello,
            string clientFingerprint,
            IPAddress remote,
            byte[] serverNonce,
            CancellationToken cancellationToken)
        {
            if (DateTime.UtcNow.Ticks < Interlocked.Read(ref _pairingCooldownUntilTicks))
            {
                await SendPairDenyAsync(ssl, "busy", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!Discoverable && !_peers.ContainsFingerprint(clientFingerprint))
            {
                await SendPairDenyAsync(ssl, "hidden", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!await _pairingLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                await SendPairDenyAsync(ssl, "busy", cancellationToken).ConfigureAwait(false);
                return;
            }

            var paired = false;
            try
            {
                var peer = new PeerEndpoint(
                    remote,
                    hello.Port > 0 ? hello.Port : _port,
                    clientFingerprint,
                    hello.Id,
                    hello.Name);

                bool approved;
                using (var approvalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    approvalCts.CancelAfter(_options.PairApprovalTimeout);
                    try
                    {
                        approved = await _prompter.RequestPairingApprovalAsync(
                            new PairingRequest(peer),
                            approvalCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        await SendPairDenyAsync(ssl, "timeout", cancellationToken).ConfigureAwait(false);
                        return;
                    }
                }

                if (!approved)
                {
                    await SendPairDenyAsync(ssl, "denied", cancellationToken).ConfigureAwait(false);
                    return;
                }

                byte[] clientNonce;
                try
                {
                    clientNonce = Convert.FromBase64String(hello.Nonce ?? string.Empty);
                }
                catch (FormatException)
                {
                    await SendPairDenyAsync(ssl, "invalid", cancellationToken).ConfigureAwait(false);
                    return;
                }

                var code = PairingCode.Compute(
                    clientFingerprint,
                    _identity.Fingerprint,
                    clientNonce,
                    serverNonce);
                _prompter.ShowPairingCode(new PairingCodeDisplay(peer, code, _options.PairCodeTimeout));

                await WriteJsonAsync(
                    ssl,
                    FrameType.PairPrompt,
                    new PairPromptDto
                    {
                        ExpiresSeconds = Math.Max(1, (int)_options.PairCodeTimeout.TotalSeconds)
                    },
                    cancellationToken).ConfigureAwait(false);

                Frame proofFrame;
                using (var codeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    codeCts.CancelAfter(_options.PairCodeTimeout);
                    try
                    {
                        proofFrame = await TimeoutIo.ReadFrameAsync(
                            ssl,
                            _options.PairCodeTimeout,
                            codeCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        await SendPairDenyAsync(ssl, "timeout", cancellationToken).ConfigureAwait(false);
                        return;
                    }
                }

                if (proofFrame.Type == FrameType.PairAbort)
                {
                    return;
                }

                if (proofFrame.Type != FrameType.PairProof)
                {
                    await SendPairDenyAsync(ssl, "invalid", cancellationToken).ConfigureAwait(false);
                    return;
                }

                PairProofDto proofDto;
                try
                {
                    proofDto = ProtocolJson.Deserialize<PairProofDto>(proofFrame.Payload);
                }
                catch (ProtocolException)
                {
                    await SendPairDenyAsync(ssl, "invalid", cancellationToken).ConfigureAwait(false);
                    return;
                }

                byte[] proof;
                try
                {
                    proof = Convert.FromBase64String(proofDto.Proof ?? string.Empty);
                }
                catch (FormatException)
                {
                    await SendPairDenyAsync(ssl, "invalid", cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (!PairingCode.VerifyProof(
                    code,
                    clientNonce,
                    serverNonce,
                    clientFingerprint,
                    _identity.Fingerprint,
                    proof))
                {
                    await SendPairDenyAsync(ssl, "invalid", cancellationToken).ConfigureAwait(false);
                    return;
                }

                var record = new PeerRecord
                {
                    Fingerprint = clientFingerprint,
                    DeviceId = hello.Id ?? string.Empty,
                    Name = Sanitizer.SanitizeDeviceName(hello.Name),
                    LastIp = remote.ToString(),
                    LastPort = hello.Port > 0 ? hello.Port : _port,
                    PairedAtUtc = DateTime.UtcNow,
                    LastSeenUtc = DateTime.UtcNow
                };
                _peers.Upsert(record);
                _peers.Save();
                paired = true;
                DevicePaired?.Invoke(record);

                await TimeoutIo.WriteFrameAsync(
                    ssl,
                    FrameType.PairOk,
                    Array.Empty<byte>(),
                    _options.ControlTimeout,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (!paired && _options.PairingCooldown > TimeSpan.Zero)
                {
                    Interlocked.Exchange(
                        ref _pairingCooldownUntilTicks,
                        DateTime.UtcNow.Add(_options.PairingCooldown).Ticks);
                }

                // Close the current dialog before allowing the next pairing to
                // install its own form. Releasing first let a new task inherit a
                // form that was already closing.
                _prompter.ClosePairingCode();
                _pairingLock.Release();
            }
        }

        private async Task SendPairDenyAsync(SslStream ssl, string reason, CancellationToken cancellationToken)
        {
            await WriteJsonAsync(
                ssl,
                FrameType.PairDeny,
                new PairDenyDto { Reason = reason },
                cancellationToken).ConfigureAwait(false);
        }

        private async Task HandleSendAsync(
            SslStream ssl,
            TcpClient client,
            HelloDto hello,
            HelloAckDto ack,
            string clientFingerprint,
            IPAddress remote,
            CancellationToken cancellationToken)
        {
            if (!hello.Paired || !ack.Paired)
            {
                await SendErrorAsync(ssl, "not_paired", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (ack.Busy || Interlocked.CompareExchange(ref _receiving, 1, 0) != 0)
            {
                await SendErrorAsync(ssl, "busy_inbound", cancellationToken).ConfigureAwait(false);
                return;
            }

            Interlocked.Exchange(ref _cancelRequested, 0);
            var completed = 0;
            IProgress<TransferProgress> progress = null;
            var transferCompleted = false;
            var failureCode = "cancelled";
            try
            {
                Frame offerFrame;
                try
                {
                    offerFrame = await TimeoutIo.ReadFrameAsync(ssl, _options.OfferTimeout, cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    await SendErrorAsync(ssl, "timeout", cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (offerFrame.Type == FrameType.Cancel) return;
                if (offerFrame.Type != FrameType.Offer)
                {
                    await SendErrorAsync(ssl, "offer_required", cancellationToken).ConfigureAwait(false);
                    return;
                }

                OfferDto offerDto;
                try
                {
                    offerDto = ProtocolJson.Deserialize<OfferDto>(offerFrame.Payload);
                }
                catch (ProtocolException)
                {
                    await SendErrorAsync(ssl, "invalid_offer", cancellationToken).ConfigureAwait(false);
                    return;
                }

                var validation = ValidateOffer(offerDto, _options.MaxReceiveBytes, out var files, out var total);
                if (validation != null)
                {
                    await SendErrorAsync(ssl, validation, cancellationToken).ConfigureAwait(false);
                    return;
                }

                _log.Info("Incoming offer from " + Sanitizer.SanitizeLogValue(hello.Name)
                    + ": " + files.Count + " file(s), " + total + " bytes.");

                var folder = _receiveFolder();
                Directory.CreateDirectory(folder);

                var resumePoints = new List<ResumePoint>(files.Count);
                long alreadyPresent = 0;
                for (var resumeIndex = 0; resumeIndex < files.Count; resumeIndex++)
                {
                    var point = FileReceiver.FindResume(folder, files[resumeIndex].Name, files[resumeIndex].Size);
                    resumePoints.Add(point);
                    if (point != null) alreadyPresent += point.Offset;
                }

                var requiredBytes = alreadyPresent >= total ? 0 : total - alreadyPresent;
                if (!HasFreeSpace(folder, requiredBytes))
                {
                    await WriteJsonAsync(ssl, FrameType.Reject, new RejectDto { Reason = "disk_full" }, cancellationToken).ConfigureAwait(false);
                    return;
                }

                var peer = new PeerEndpoint(
                    remote,
                    hello.Port > 0 ? hello.Port : _port,
                    clientFingerprint,
                    hello.Id,
                    hello.Name);
                var offer = new TransferOffer(peer, offerDto.TransferId, files);

                var accepted = AutoAcceptFromPaired && hello.Paired;
                if (!accepted)
                {
                    using (var offerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        offerCts.CancelAfter(_options.OfferTimeout);
                        try
                        {
                            accepted = await _prompter.ApproveOfferAsync(offer, offerCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            await WriteJsonAsync(ssl, FrameType.Reject, new RejectDto { Reason = "timeout" }, cancellationToken).ConfigureAwait(false);
                            return;
                        }
                    }
                }

                if (!accepted)
                {
                    await WriteJsonAsync(ssl, FrameType.Reject, new RejectDto { Reason = "declined" }, cancellationToken).ConfigureAwait(false);
                    return;
                }

                var accept = new AcceptDto
                {
                    TransferId = offer.TransferId,
                    Resume = new List<ResumeFileDto>()
                };
                for (var resumeIndex = 0; resumeIndex < resumePoints.Count; resumeIndex++)
                {
                    var point = resumePoints[resumeIndex];
                    accept.Resume.Add(new ResumeFileDto
                    {
                        Index = resumeIndex,
                        Offset = point == null ? 0 : point.Offset,
                        PrefixSha256 = point == null ? string.Empty : point.PrefixSha256
                    });
                }

                await WriteJsonAsync(ssl, FrameType.Accept, accept, cancellationToken).ConfigureAwait(false);
                _log.Info("Incoming transfer accepted from " + Sanitizer.SanitizeLogValue(hello.Name) + ".");
                progress = _prompter.CreateReceiveProgress(offer, cancellationToken);

                var fileBuffer = new byte[FrameCodec.MaxFileDataPayload];

                for (var index = 0; index < files.Count; index++)
                {
                    var beginFrame = await TimeoutIo.ReadFrameAsync(ssl, _options.FileIdleTimeout, cancellationToken).ConfigureAwait(false);
                    if (beginFrame.Type == FrameType.Cancel)
                    {
                        RemoveResumePointsFrom(resumePoints, index);
                        return;
                    }
                    if (beginFrame.Type != FrameType.FileBegin)
                    {
                        await SendErrorAsync(ssl, "file_begin_required", cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    FileBeginDto begin;
                    try
                    {
                        begin = ProtocolJson.Deserialize<FileBeginDto>(beginFrame.Payload);
                    }
                    catch (ProtocolException)
                    {
                        await SendErrorAsync(ssl, "invalid_file", cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    var resumePoint = resumePoints[index];
                    var resumeOffset = resumePoint == null ? 0 : resumePoint.Offset;
                    if (begin.Index != index ||
                        begin.Size != files[index].Size ||
                        !string.Equals(begin.Name, files[index].Name, StringComparison.OrdinalIgnoreCase) ||
                        begin.ResumeOffset < 0 ||
                        begin.ResumeOffset > begin.Size ||
                        (begin.ResumeOffset != 0 && begin.ResumeOffset != resumeOffset))
                    {
                        await SendErrorAsync(ssl, "invalid_file", cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    FileReceiver receiver;
                    try
                    {
                        receiver = begin.ResumeOffset > 0 && resumePoint != null
                            ? FileReceiver.OpenExisting(
                                folder,
                                begin.Name,
                                begin.Size,
                                resumePoint.PartPath,
                                resumePoint.Offset)
                            : _receiverFactory(folder, begin.Name, begin.Size);
                        if (receiver == null) throw new IOException("No receiver was created.");
                    }
                    catch (Exception ex)
                    {
                        _log.Warn("Receiver creation failed: " + Sanitizer.SanitizeLogValue(ex.Message));
                        await SendFileErrorAsync(ssl, index, "write_failed", cancellationToken).ConfigureAwait(false);
                        await SendErrorAsync(ssl, "transfer_failed", cancellationToken, index, completed).ConfigureAwait(false);
                        return;
                    }

                    try
                    {
                        progress.Report(new TransferProgress
                        {
                            FileName = files[index].Name,
                            FileIndex = index,
                            FileCount = files.Count,
                            BytesTransferred = 0,
                            TotalBytes = files[index].Size
                        });

                        long received = 0;
                        while (true)
                        {
                            if (Volatile.Read(ref _cancelRequested) != 0)
                            {
                                _log.Info("Receiver cancelled the transfer.");
                                await SendCancelAsync(ssl, "cancelled").ConfigureAwait(false);
                                await DrainAfterCancelAsync(ssl, fileBuffer).ConfigureAwait(false);
                                return;
                            }

                            var dataFrame = await TimeoutIo.ReadFileDataAsync(
                                ssl,
                                fileBuffer,
                                _options.FileIdleTimeout,
                                cancellationToken).ConfigureAwait(false);

                            if (dataFrame.Type == FrameType.Cancel)
                            {
                                _log.Info("Sender cancelled the current file; removing its partial file.");
                                receiver.Abort();
                                RemoveResumePointsFrom(resumePoints, index + 1);
                                _log.Info("Partial cleanup after cancel: part=" + File.Exists(receiver.PartPath)
                                    + ", metadata=" + File.Exists(receiver.PartPath + ".resume"));
                                return;
                            }

                            if (dataFrame.Type == FrameType.FileEnd)
                            {
                                FileEndDto end;
                                try
                                {
                                    end = ProtocolJson.Deserialize<FileEndDto>(dataFrame.Payload);
                                }
                                catch (ProtocolException)
                                {
                                    throw new InvalidDataException("Invalid FILE_END.");
                                }

                                if (end.Index != index) throw new InvalidDataException("Unexpected file index.");
                                receiver.Complete(end.Sha256);
                                break;
                            }

                            if (dataFrame.Type != FrameType.FileData)
                                throw new InvalidDataException("Expected FILE_DATA.");
                            if (dataFrame.Index != index)
                                throw new InvalidDataException("Unexpected file index.");

                            receiver.Write(dataFrame.DataBuffer, dataFrame.DataOffset, dataFrame.DataLength);
                            received += dataFrame.DataLength;
                            progress.Report(new TransferProgress
                            {
                                FileName = files[index].Name,
                                FileIndex = index,
                                FileCount = files.Count,
                                BytesTransferred = received,
                                TotalBytes = files[index].Size
                            });
                        }

                        await WriteJsonAsync(ssl, FrameType.FileOk, new FileOkDto { Index = index }, cancellationToken).ConfigureAwait(false);
                        completed++;
                        progress.Report(new TransferProgress
                        {
                            FileName = files[index].Name,
                            FileIndex = index,
                            FileCount = files.Count,
                            BytesTransferred = files[index].Size,
                            TotalBytes = files[index].Size,
                            IsComplete = true
                        });
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException) || Volatile.Read(ref _stopping) != 0)
                    {
                        if (Volatile.Read(ref _cancelRequested) != 0)
                            receiver.Abort();
                        else
                            receiver.PreservePartial();
                        failureCode = "transfer_failed";
                        _log.Warn("File receive failed: " + Sanitizer.SanitizeLogValue(ex.Message));
                        await SendFileErrorAsync(ssl, index, "write_failed", cancellationToken).ConfigureAwait(false);
                        await SendErrorAsync(ssl, "transfer_failed", cancellationToken, index, completed).ConfigureAwait(false);
                        return;
                    }
                    finally
                    {
                        if (!receiver.IsPreserved) receiver.Abort();
                    }
                }

                var doneFrame = await TimeoutIo.ReadFrameAsync(ssl, _options.ControlTimeout, cancellationToken).ConfigureAwait(false);
                if (doneFrame.Type != FrameType.Done)
                {
                    await SendErrorAsync(ssl, "done_required", cancellationToken).ConfigureAwait(false);
                    return;
                }

                DoneDto done;
                try
                {
                    done = ProtocolJson.Deserialize<DoneDto>(doneFrame.Payload);
                }
                catch (ProtocolException)
                {
                    await SendErrorAsync(ssl, "done_failed", cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (!string.Equals(done.TransferId, offer.TransferId, StringComparison.Ordinal))
                {
                    await SendErrorAsync(ssl, "done_failed", cancellationToken).ConfigureAwait(false);
                    return;
                }

                await WriteJsonAsync(ssl, FrameType.DoneAck, new DoneAckDto { TransferId = offer.TransferId }, cancellationToken).ConfigureAwait(false);
                transferCompleted = true;
                if (progress != null)
                {
                    progress.Report(new TransferProgress
                    {
                        FileName = files[files.Count - 1].Name,
                        FileIndex = files.Count - 1,
                        FileCount = files.Count,
                        BytesTransferred = offer.TotalSize,
                        TotalBytes = offer.TotalSize,
                        IsComplete = true,
                        OverallComplete = true
                    });
                }
                _log.Info("Incoming transfer completed from " + Sanitizer.SanitizeLogValue(hello.Name) + ".");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                failureCode = "cancelled";
            }
            catch (Exception ex)
            {
                failureCode = Volatile.Read(ref _cancelRequested) != 0
                    ? "cancelled"
                    : "transfer_failed";
                _log.Warn("Send failed: " + ex.GetType().Name + ": " + Sanitizer.SanitizeLogValue(ex.Message));
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

                if (!transferCompleted)
                {
                    _log.Info("Incoming transfer ended from " + Sanitizer.SanitizeLogValue(hello.Name)
                        + ": " + Sanitizer.SanitizeLogValue(failureCode));
                }

                Interlocked.Exchange(ref _receiving, 0);

            }
        }

        private static void RemoveResumePointsFrom(List<ResumePoint> points, int startIndex)
        {
            if (points == null) return;
            for (var index = Math.Max(0, startIndex); index < points.Count; index++)
                FileReceiver.DeleteResumePoint(points[index]);
        }

        private async Task SendFileErrorAsync(SslStream ssl, int index, string code, CancellationToken cancellationToken)
        {
            await WriteJsonAsync(
                ssl,
                FrameType.FileError,
                new FileErrorDto { Index = index, Code = code, Message = Sanitizer.MapError(code) },
                cancellationToken).ConfigureAwait(false);
        }

        private static string ValidateOffer(OfferDto offer, long maxReceiveBytes, out List<TransferFile> files, out long total)
        {
            files = new List<TransferFile>();
            total = 0;

            if (offer?.Files == null || offer.Files.Count == 0 || offer.Files.Count > 5000)
                return "invalid_offer";
            if (maxReceiveBytes <= 0) return "invalid_offer";

            foreach (var file in offer.Files)
            {
                if (file == null || file.Size < 0) return "invalid_offer";
                if (file.Size > maxReceiveBytes || total > maxReceiveBytes - file.Size)
                    return "invalid_offer";
                if (total > long.MaxValue - file.Size) return "invalid_offer";
                total += file.Size;
                files.Add(new TransferFile(PathGuard.SanitizeFileName(file.Name), file.Size));
            }

            return offer.TotalSize == total ? null : "invalid_offer";
        }

        public static bool HasFreeSpace(string folder, long total)
        {
            if (string.IsNullOrWhiteSpace(folder) || total < 0) return false;

            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(folder));
                if (string.IsNullOrEmpty(root)) return false;
                var drive = new DriveInfo(root);
                const long reserve = 1024L * 1024 * 1024;

                // Compare before adding the reserve. The old expression could
                // overflow for a hostile declared total and bypass the check.
                return drive.AvailableFreeSpace > reserve &&
                       total <= drive.AvailableFreeSpace - reserve;
            }
            catch
            {
                return false;
            }
        }


        private bool IsRemoteAllowed(IPAddress remote)
        {
            if (IPAddress.IsLoopback(remote)) return _allowLoopback;
            return _scopeFactory().IsEligiblePeer(remote);
        }

        private static async Task DrainAfterCancelAsync(SslStream stream, byte[] buffer)
        {
            using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
            {
                try
                {
                    while (true)
                    {
                        var frame = await TimeoutIo.ReadFileDataAsync(
                            stream,
                            buffer,
                            TimeSpan.FromSeconds(2),
                            cancel.Token).ConfigureAwait(false);
                        if (frame.Type == FrameType.Cancel ||
                            frame.Type == FrameType.FileEnd ||
                            frame.Type == FrameType.Error)
                        {
                            return;
                        }
                    }
                }
                catch
                {
                }
            }
        }

        private static async Task SendCancelAsync(SslStream stream, string reason)
        {
            try
            {
                await TimeoutIo.WriteFrameAsync(
                    stream,
                    FrameType.Cancel,
                    ProtocolJson.Serialize(new CancelDto { Reason = reason }),
                    TimeSpan.FromSeconds(2),
                    CancellationToken.None).ConfigureAwait(false);

                await Task.Delay(150).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        private async Task SendErrorAsync(
            SslStream ssl,
            string code,
            CancellationToken cancellationToken,
            int fileIndex = -1,
            int completed = 0)
        {
            var error = new ErrorDto
            {
                Code = code,
                Message = Sanitizer.MapError(code),
                FileIndex = fileIndex,
                Completed = completed
            };
            await WriteJsonAsync(ssl, FrameType.Error, error, cancellationToken).ConfigureAwait(false);
        }

        private async Task WriteJsonAsync<T>(SslStream ssl, FrameType type, T value, CancellationToken cancellationToken)
        {
            await TimeoutIo.WriteFrameAsync(
                ssl,
                type,
                ProtocolJson.Serialize(value),
                _options.ControlTimeout,
                cancellationToken).ConfigureAwait(false);
        }

        private static byte[] NewNonceBytes()
        {
            var nonce = new byte[16];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(nonce);
            return nonce;
        }

        public void Stop()
        {
            if (_listener == null && _cancellation == null) return;

            Interlocked.Exchange(ref _stopping, 1);
            try { _cancellation?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }

            foreach (var client in _clients.Keys)
            {
                try { client.Close(); } catch { }
            }

            try { _acceptLoop?.Wait(TimeSpan.FromSeconds(2)); } catch { }

            _listener = null;
            _cancellation?.Dispose();
            _cancellation = null;
            _acceptLoop = null;
            _log.Info("Transfer server stopped.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _disposed = true;
        }
    }
}
