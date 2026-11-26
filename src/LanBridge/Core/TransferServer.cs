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

                    var ack = new HelloAckDto
                    {
                        V = 1,
                        Id = _identity.DeviceId,
                        Name = _deviceName,
                        Fp = _identity.Fingerprint,
                        Nonce = NewNonce(),
                        Paired = _peers.ContainsFingerprint(clientFingerprint),
                        Busy = IsReceiving
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

        private static string NewNonce()
        {
            var nonce = new byte[16];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(nonce);
            return Convert.ToBase64String(nonce);
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
