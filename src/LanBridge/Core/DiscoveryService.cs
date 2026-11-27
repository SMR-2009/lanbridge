using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace LanBridge.Core
{
    [DataContract]
    public sealed class DiscoveryPacketData
    {
        [DataMember(Name = "t")] public string T { get; set; }
        [DataMember(Name = "v")] public int V { get; set; }
        [DataMember(Name = "id")] public string Id { get; set; }
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "port")] public int Port { get; set; }
        [DataMember(Name = "fp")] public string Fp { get; set; }
        [DataMember(Name = "discoverable")] public bool Discoverable { get; set; }
    }

    public static class DiscoveryPacket
    {
        public const int MaxBytes = 1400;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("LNB1");

        public static byte[] Encode(DiscoveryPacketData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            var json = ProtocolJson.Serialize(data);
            if (json.Length + Magic.Length > MaxBytes)
                throw new ProtocolException("discovery_too_large", "Discovery packet exceeds 1,400 bytes.");

            var buffer = new byte[Magic.Length + json.Length];
            Buffer.BlockCopy(Magic, 0, buffer, 0, Magic.Length);
            Buffer.BlockCopy(json, 0, buffer, Magic.Length, json.Length);
            return buffer;
        }

        public static bool TryDecode(byte[] buffer, out DiscoveryPacketData data, out string error)
        {
            data = null;
            error = null;

            if (buffer == null || buffer.Length < Magic.Length + 1 || buffer.Length > MaxBytes)
            {
                error = "bad_length";
                return false;
            }

            for (var i = 0; i < Magic.Length; i++)
            {
                if (buffer[i] != Magic[i])
                {
                    error = "bad_magic";
                    return false;
                }
            }

            var json = new byte[buffer.Length - Magic.Length];
            Buffer.BlockCopy(buffer, Magic.Length, json, 0, json.Length);

            try
            {
                data = ProtocolJson.Deserialize<DiscoveryPacketData>(json);
            }
            catch (ProtocolException)
            {
                error = "bad_json";
                return false;
            }

            if (data == null || data.V != 1 || string.IsNullOrEmpty(data.Id) || string.IsNullOrEmpty(data.T))
            {
                error = "bad_fields";
                return false;
            }

            return true;
        }
    }

    public sealed class DiscoveredDevice
    {
        public string DeviceId { get; set; }
        public string Name { get; set; }
        public string Fingerprint { get; set; }
        public IPAddress IPAddress { get; set; }
        public int Port { get; set; }
        public DateTime LastSeenUtc { get; set; }
    }

    public sealed class DiscoveryService : IDisposable
    {
        private readonly int _port;
        private readonly string _deviceName;
        private readonly DeviceIdentity _identity;
        private readonly PeerStore _peers;
        private readonly Func<PeerEndpoint, CancellationToken, Task<bool>> _confirm;
        private readonly ILogger _log;
        private readonly bool _allowLoopback;
        private readonly object _gate = new object();
        private readonly Dictionary<string, DiscoveredDevice> _devices =
            new Dictionary<string, DiscoveredDevice>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _confirmCache =
            new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _rateLimits =
            new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly SemaphoreSlim _packetSlots = new SemaphoreSlim(8, 8);
        private const int MaxDevices = 256;
        private const int MaxConfirmEntries = 256;
        private const int MaxRateEntries = 256;

        private UdpClient _udp;
        private int _rejectedPacketCount;
        private LanScope _scope;
        private CancellationTokenSource _cancellation;
        private Task _receiveLoop;
        private Task _announceLoop;
        private Task _expireLoop;
        private long _packetsSent;
        private bool _disposed;

        public string DeviceName => _deviceName;
        public bool Discoverable { get; set; } = true;
        public long PacketsSent => Interlocked.Read(ref _packetsSent);
        public int RejectedPacketCount => Volatile.Read(ref _rejectedPacketCount);

        public event Action<DiscoveredDevice> DeviceConfirmed;
        public event Action<DiscoveredDevice> DeviceLost;

        public DiscoveryService(
            int port,
            string deviceName,
            DeviceIdentity identity,
            PeerStore peers,
            Func<PeerEndpoint, CancellationToken, Task<bool>> confirm,
            ILogger log,
            bool allowLoopback = false)
        {
            if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            _port = port;
            _deviceName = Sanitizer.SanitizeDeviceName(deviceName);
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));
            _peers = peers ?? throw new ArgumentNullException(nameof(peers));
            _confirm = confirm ?? throw new ArgumentNullException(nameof(confirm));
            _log = log ?? NullLogger.Instance;
            _allowLoopback = allowLoopback;
        }

        public IReadOnlyList<DiscoveredDevice> Devices
        {
            get
            {
                lock (_gate)
                {
                    return _devices.Values
                        .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                }
            }
        }

        public void Start()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DiscoveryService));
            if (_udp != null) throw new InvalidOperationException("Discovery is already started.");

            _scope = LanScope.Capture();
            _udp = new UdpClient(new IPEndPoint(IPAddress.Any, _port));
            _udp.EnableBroadcast = true;
            _cancellation = new CancellationTokenSource();

            _receiveLoop = Task.Run(() => ReceiveLoopAsync(_cancellation.Token));
            _announceLoop = Task.Run(() => AnnounceLoopAsync(_cancellation.Token));
            _expireLoop = Task.Run(() => ExpireLoopAsync(_cancellation.Token));

            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;

            if (Discoverable) _ = SendQueryAsync();
            _log.Info("Discovery listening on UDP " + _port);
        }

        public void Refresh()
        {
            if (!Discoverable) return;
            _ = SendQueryAsync();
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await _udp.ReceiveAsync().ConfigureAwait(false);
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
                    _log.Info("Discovery receive failed: " + Sanitizer.SanitizeLogValue(ex.Message));
                    continue;
                }

                if (result.Buffer == null || result.Buffer.Length == 0) continue;
                var buffer = result.Buffer;
                var remote = result.RemoteEndPoint;

                if (!_packetSlots.Wait(0))
                {
                    Interlocked.Increment(ref _rejectedPacketCount);
                    continue;
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await HandlePacketAsync(buffer, remote, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        _packetSlots.Release();
                    }
                });
            }
        }

        private async Task HandlePacketAsync(byte[] buffer, IPEndPoint remote, CancellationToken cancellationToken)
        {
            try
            {
                if (remote == null || remote.Address == null) return;
                if (!IsAllowedSource(remote.Address)) return;
                if (!AllowRate(remote.Address.ToString())) return;
                if (!DiscoveryPacket.TryDecode(buffer, out var data, out var error))
                {
                    _log.Info("Ignored discovery packet: " + error);
                    return;
                }

                if (data.T == "q")
                {
                    if (Discoverable) await SendAnnouncementToAsync(remote, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (data.T != "a" || !data.Discoverable) return;
                if (string.Equals(data.Id, _identity.DeviceId, StringComparison.Ordinal)) return;
                if (string.Equals(data.Fp, _identity.Fingerprint, StringComparison.OrdinalIgnoreCase)) return;
                if (data.Port <= 0 || data.Port > 65535) return;
                if (string.IsNullOrEmpty(data.Fp) || data.Fp.Length != 64) return;

                var now = DateTime.UtcNow;
                var confirmKey = data.Fp + "|" + remote.Address + "|" + data.Port;
                var needsConfirmation = true;
                lock (_gate)
                {
                    if (_confirmCache.TryGetValue(confirmKey, out var confirmedAt) &&
                        now - confirmedAt < TimeSpan.FromSeconds(10))
                    {
                        needsConfirmation = false;
                    }
                }

                if (needsConfirmation)
                {
                    var endpoint = new PeerEndpoint(
                        remote.Address,
                        data.Port,
                        data.Fp,
                        data.Id,
                        data.Name);
                    bool confirmed;
                    try
                    {
                        confirmed = await _confirm(endpoint, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _log.Info("Discovery confirmation failed: " + Sanitizer.SanitizeLogValue(ex.Message));
                        confirmed = false;
                    }

                    if (!confirmed) return;
                    lock (_gate)
                    {
                        _confirmCache[confirmKey] = now;
                        TrimOldest(_confirmCache, MaxConfirmEntries);
                    }
                }

                DiscoveredDevice device;
                var isNew = false;
                lock (_gate)
                {
                    if (!_devices.TryGetValue(data.Fp, out device))
                    {
                        device = new DiscoveredDevice
                        {
                            Fingerprint = data.Fp,
                            DeviceId = data.Id,
                            Name = Sanitizer.SanitizeDeviceName(data.Name),
                            IPAddress = remote.Address,
                            Port = data.Port,
                            LastSeenUtc = now
                        };
                        _devices[data.Fp] = device;
                        isNew = true;
                        TrimOldestDevices();
                    }
                    else
                    {
                        device.DeviceId = data.Id;
                        device.Name = Sanitizer.SanitizeDeviceName(data.Name);
                        device.IPAddress = remote.Address;
                        device.Port = data.Port;
                        device.LastSeenUtc = now;
                    }
                }

                if (_peers.ContainsFingerprint(data.Fp))
                {
                    if (_peers.UpdateEndpoint(data.Fp, remote.Address.ToString(), data.Port))
                    {
                        try { _peers.Save(); }
                        catch (Exception ex)
                        {
                            _log.Warn("Could not persist the peer endpoint: " + Sanitizer.SanitizeLogValue(ex.Message));
                        }
                    }
                }

                if (isNew) DeviceConfirmed?.Invoke(device);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _log.Info("Discovery packet handling failed: " + Sanitizer.SanitizeLogValue(ex.Message));
            }
        }

        private async Task AnnounceLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                    await SendAnnouncementAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _log.Info("Discovery announce failed: " + Sanitizer.SanitizeLogValue(ex.Message));
                }
            }
        }

        private async Task ExpireLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var lost = new List<DiscoveredDevice>();
                lock (_gate)
                {
                    var now = DateTime.UtcNow;
                    var cutoff = now - TimeSpan.FromSeconds(15);
                    foreach (var device in _devices.Values.Where(device => device.LastSeenUtc < cutoff).ToList())
                    {
                        lost.Add(device);
                        _devices.Remove(device.Fingerprint);
                    }

                    var rateCutoff = now - TimeSpan.FromMinutes(1);
                    foreach (var key in _rateLimits.Where(pair => pair.Value < rateCutoff).Select(pair => pair.Key).ToList())
                    {
                        _rateLimits.Remove(key);
                    }

                    var confirmCutoff = now - TimeSpan.FromMinutes(10);
                    foreach (var key in _confirmCache.Where(pair => pair.Value < confirmCutoff).Select(pair => pair.Key).ToList())
                    {
                        _confirmCache.Remove(key);
                    }
                }

                foreach (var device in lost) DeviceLost?.Invoke(device);
            }
        }

        private async Task SendAnnouncementAsync(CancellationToken cancellationToken)
        {
            if (!Discoverable) return;

            var data = new DiscoveryPacketData
            {
                T = "a",
                V = 1,
                Id = _identity.DeviceId,
                Name = _deviceName,
                Port = _port,
                Fp = _identity.Fingerprint,
                Discoverable = true
            };
            await SendToBroadcastsAsync(DiscoveryPacket.Encode(data), cancellationToken).ConfigureAwait(false);
        }

        private async Task SendQueryAsync()
        {
            if (!Discoverable) return;

            var data = new DiscoveryPacketData
            {
                T = "q",
                V = 1,
                Id = _identity.DeviceId
            };
            await SendToBroadcastsAsync(DiscoveryPacket.Encode(data), CancellationToken.None).ConfigureAwait(false);
        }

        private async Task SendAnnouncementToAsync(IPEndPoint remote, CancellationToken cancellationToken)
        {
            if (!Discoverable) return;

            var data = new DiscoveryPacketData
            {
                T = "a",
                V = 1,
                Id = _identity.DeviceId,
                Name = _deviceName,
                Port = _port,
                Fp = _identity.Fingerprint,
                Discoverable = true
            };

            try
            {
                var bytes = DiscoveryPacket.Encode(data);
                await _udp.SendAsync(bytes, bytes.Length, remote).ConfigureAwait(false);
                Interlocked.Increment(ref _packetsSent);
            }
            catch (Exception ex)
            {
                _log.Info("Discovery reply failed: " + Sanitizer.SanitizeLogValue(ex.Message));
            }
        }

        private async Task SendToBroadcastsAsync(byte[] bytes, CancellationToken cancellationToken)
        {
            if (_udp == null) return;

            var endpoints = new List<IPEndPoint>();
            try
            {
                var scope = _scope ?? LanScope.Capture();
                foreach (var iface in scope.Interfaces)
                {
                    endpoints.Add(new IPEndPoint(iface.Broadcast, _port));
                }
            }
            catch (Exception ex)
            {
                _log.Info("LAN scope capture failed: " + Sanitizer.SanitizeLogValue(ex.Message));
            }
            endpoints.Add(new IPEndPoint(IPAddress.Broadcast, _port));

            foreach (var endpoint in endpoints
                .GroupBy(e => e.Address + ":" + e.Port)
                .Select(group => group.First()))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await _udp.SendAsync(bytes, bytes.Length, endpoint).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.Info("Discovery send failed: " + Sanitizer.SanitizeLogValue(ex.Message));
                }
                Interlocked.Increment(ref _packetsSent);
            }
        }

        private bool IsAllowedSource(IPAddress remote)
        {
            if (remote == null || remote.AddressFamily != AddressFamily.InterNetwork) return false;
            if (IPAddress.IsLoopback(remote)) return _allowLoopback;
            var scope = _scope ?? LanScope.Capture();
            return scope.IsEligiblePeer(remote);
        }

        private bool AllowRate(string key)
        {
            var now = DateTime.UtcNow;
            lock (_gate)
            {
                if (_rateLimits.TryGetValue(key, out var last) && now - last < TimeSpan.FromMilliseconds(250))
                    return false;
                _rateLimits[key] = now;
                TrimOldest(_rateLimits, MaxRateEntries);
                return true;
            }
        }

        private void TrimOldest(Dictionary<string, DateTime> values, int limit)
        {
            if (values.Count <= limit) return;
            foreach (var key in values.OrderBy(pair => pair.Value).Take(values.Count - limit).Select(pair => pair.Key).ToList())
                values.Remove(key);
        }

        private void TrimOldestDevices()
        {
            if (_devices.Count <= MaxDevices) return;
            foreach (var key in _devices.OrderBy(pair => pair.Value.LastSeenUtc)
                .Take(_devices.Count - MaxDevices)
                .Select(pair => pair.Key)
                .ToList())
            {
                _devices.Remove(key);
            }
        }

        private void OnNetworkAddressChanged(object sender, EventArgs e)
        {
            try
            {
                _scope = LanScope.Capture();
                Refresh();
            }
            catch
            {
            }
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                try
                {
                    _scope = LanScope.Capture();
                    Refresh();
                }
                catch
                {
                }
            }
        }

        public void Stop()
        {
            if (_udp == null && _cancellation == null) return;

            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;

            try { _cancellation?.Cancel(); } catch { }
            try { _udp?.Close(); } catch { }

            var tasks = new[] { _receiveLoop, _announceLoop, _expireLoop }
                .Where(task => task != null)
                .ToArray();
            try { Task.WaitAll(tasks, TimeSpan.FromSeconds(2)); } catch { }

            _udp = null;
            _scope = null;
            _cancellation?.Dispose();
            _cancellation = null;
            _receiveLoop = null;
            _announceLoop = null;
            _expireLoop = null;
            _log.Info("Discovery stopped.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _disposed = true;
        }
    }
}
