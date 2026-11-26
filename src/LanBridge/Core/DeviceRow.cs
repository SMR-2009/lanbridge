using System;
using System.Collections.Generic;
using System.Linq;

namespace LanBridge.Core
{
    public enum DeviceState
    {
        Paired,
        Available,
        Offline,
        IdentityChanged
    }

    public sealed class DeviceRow
    {
        public string Fingerprint { get; set; }
        public string DeviceId { get; set; }
        public string Name { get; set; }
        public string IpAddress { get; set; }
        public int Port { get; set; }
        public DeviceState State { get; set; }
        public DateTime LastSeenUtc { get; set; }

        public bool HasEndpoint => !string.IsNullOrEmpty(IpAddress) && Port > 0;
    }

    public static class DeviceRowMapper
    {
        public static IReadOnlyList<DeviceRow> Build(
            IReadOnlyList<PeerRecord> peers,
            IReadOnlyList<DiscoveredDevice> discovered)
        {
            var peerList = peers ?? Array.Empty<PeerRecord>();
            var discoveredList = discovered ?? Array.Empty<DiscoveredDevice>();

            var discoveredByFingerprint = discoveredList
                .Where(device => device != null && !string.IsNullOrEmpty(device.Fingerprint))
                .GroupBy(device => device.Fingerprint, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(device => device.LastSeenUtc).First(),
                    StringComparer.Ordinal);

            var rows = new List<DeviceRow>();
            var added = new HashSet<string>(StringComparer.Ordinal);

            foreach (var peer in peerList)
            {
                if (peer == null || string.IsNullOrEmpty(peer.Fingerprint)) continue;
                if (!added.Add(peer.Fingerprint)) continue;

                if (discoveredByFingerprint.TryGetValue(peer.Fingerprint, out var device))
                {
                    rows.Add(new DeviceRow
                    {
                        Fingerprint = peer.Fingerprint,
                        DeviceId = !string.IsNullOrEmpty(device.DeviceId) ? device.DeviceId : peer.DeviceId,
                        Name = Sanitizer.SanitizeDeviceName(device.Name ?? peer.Name),
                        IpAddress = device.IPAddress != null ? device.IPAddress.ToString() : peer.LastIp,
                        Port = device.Port > 0 ? device.Port : peer.LastPort,
                        State = DeviceState.Paired,
                        LastSeenUtc = device.LastSeenUtc
                    });
                }
                else
                {
                    rows.Add(new DeviceRow
                    {
                        Fingerprint = peer.Fingerprint,
                        DeviceId = peer.DeviceId,
                        Name = Sanitizer.SanitizeDeviceName(peer.Name),
                        IpAddress = peer.LastIp,
                        Port = peer.LastPort,
                        State = DeviceState.Offline,
                        LastSeenUtc = peer.LastSeenUtc
                    });
                }
            }

            foreach (var device in discoveredByFingerprint.Values)
            {
                if (added.Contains(device.Fingerprint)) continue;

                var identityChanged = peerList.Any(peer =>
                    peer != null &&
                    !string.IsNullOrEmpty(peer.DeviceId) &&
                    string.Equals(peer.DeviceId, device.DeviceId, StringComparison.Ordinal));

                rows.Add(new DeviceRow
                {
                    Fingerprint = device.Fingerprint,
                    DeviceId = device.DeviceId,
                    Name = Sanitizer.SanitizeDeviceName(device.Name),
                    IpAddress = device.IPAddress != null ? device.IPAddress.ToString() : null,
                    Port = device.Port,
                    State = identityChanged ? DeviceState.IdentityChanged : DeviceState.Available,
                    LastSeenUtc = device.LastSeenUtc
                });
            }

            return rows
                .OrderBy(row => row.State)
                .ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }
}
