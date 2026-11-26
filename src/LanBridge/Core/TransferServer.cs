using System;
using System.Collections.Concurrent;
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
        private readonly ConcurrentDictionary<TcpClient, byte> _clients = new ConcurrentDictionary<TcpClient, byte>();

        private readonly SemaphoreSlim _pairingLock = new SemaphoreSlim(1, 1);
        private TcpListener _listener;
        private CancellationTokenSource _cancellation;
        private Task _acceptLoop;
        private int _receiving;
        private bool _disposed;

        public bool Discoverable { get; set; } = true;
        public bool IsReceiving => Volatile.Read(ref _receiving) != 0;
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
            Func<LanScope> scopeFactory = null)
        {
            if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
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
        }

        public void Start()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TransferServer));
            if (_listener != null) throw new InvalidOperationException("The server is already started.");

            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
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
                _clients[client] = 0;
                _ = Task.Run(() => HandleClientAsync(client, cancellationToken));
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            try
            {
                client.NoDelay = true;
                var remote = ((IPEndPoint)client.Client.RemoteEndPoint).Address;

                using (var ssl = Tls.CreateServer(client.GetStream(), _identity.Certificate))
                {
                    await TimeoutIo.WithTimeout(
                        ssl.AuthenticateAsServerAsync(_identity.Certificate, true, SslProtocols.Tls12, false),
                        _options.ControlTimeout,
                        cancellationToken).ConfigureAwait(false);

                    var remoteCertificate = ssl.RemoteCertificate;
                    if (remoteCertificate == null)
                    {
                        await SendErrorAsync(ssl, "identity_required", cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    var clientCertificate = new X509Certificate2(remoteCertificate);
                    var clientFingerprint = Tls.Fingerprint(clientCertificate);

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

                    if (!IsRemoteAllowed(remote))
                    {
                        await SendErrorAsync(ssl, "not_lan", cancellationToken).ConfigureAwait(false);
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

                    await SendErrorAsync(ssl, "not_implemented", cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _log.Warn("Connection failed: " + ex.GetType().Name + ": " + Sanitizer.SanitizeLogValue(ex.Message));
            }
            finally
            {
                _clients.TryRemove(client, out _);
                try { client.Close(); } catch { }
            }
        }

        private async Task HandlePairAsync(
            SslStream ssl,
            HelloDto hello,
            string clientFingerprint,
            IPAddress remote,
            byte[] serverNonce,
            CancellationToken cancellationToken)
        {
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

                if (proofFrame.Type == FrameType.PairAbort || proofFrame.Type != FrameType.PairProof)
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

        private bool IsRemoteAllowed(IPAddress remote)
        {
            if (IPAddress.IsLoopback(remote)) return _allowLoopback;
            return _scopeFactory().IsEligiblePeer(remote);
        }

        private async Task SendErrorAsync(SslStream ssl, string code, CancellationToken cancellationToken)
        {
            var error = new ErrorDto { Code = code, Message = Sanitizer.MapError(code) };
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
