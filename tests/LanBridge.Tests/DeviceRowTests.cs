using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using LanBridge.Core;
using Xunit;

namespace LanBridge.Tests
{
    public sealed class DeviceRowTests
    {
        [Fact]
        public void DiscoveredWithoutPeer_IsAvailable()
        {
            var rows = DeviceRowMapper.Build(
                Array.Empty<PeerRecord>(),
                new[] { Discovered("fp-1", "d1", "Rohan-Laptop", "192.168.10.7") });

            var row = Assert.Single(rows);
            Assert.Equal(DeviceState.Available, row.State);
            Assert.Equal("fp-1", row.Fingerprint);
            Assert.Equal("192.168.10.7", row.IpAddress);
            Assert.Equal(45873, row.Port);
        }

        [Fact]
        public void DiscoveredWithPeer_IsPaired()
        {
            var peer = TestData.Record("fp-1", "d1", "Stored", "192.168.10.2", 45873);
            var rows = DeviceRowMapper.Build(
                new[] { peer },
                new[] { Discovered("fp-1", "d1", "Fresh", "192.168.10.7") });

            var row = Assert.Single(rows);
            Assert.Equal(DeviceState.Paired, row.State);
            Assert.Equal("Fresh", row.Name);
            Assert.Equal("192.168.10.7", row.IpAddress);
        }

        [Fact]
        public void PairedWithoutDiscovery_IsOfflineAndKeepsLastSeen()
        {
            var lastSeen = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var peer = TestData.Record("fp-1", "d1", "Rohan-Laptop", "192.168.10.2", 45873);
            peer.LastSeenUtc = lastSeen;

            var rows = DeviceRowMapper.Build(new[] { peer }, Array.Empty<DiscoveredDevice>());

            var row = Assert.Single(rows);
            Assert.Equal(DeviceState.Offline, row.State);
            Assert.Equal(lastSeen, row.LastSeenUtc);
            Assert.Equal("192.168.10.2", row.IpAddress);
        }

        [Fact]
        public void NewFingerprintForKnownDevice_IsIdentityChanged_AndOldPeerStaysOffline()
        {
            var peer = TestData.Record("fp-old", "d1", "Old name", "192.168.10.2", 45873);
            var discovered = Discovered("fp-new", "d1", "Rohan-Laptop", "192.168.10.7");

            var rows = DeviceRowMapper.Build(new[] { peer }, new[] { discovered });

            Assert.Equal(2, rows.Count);
            var oldRow = rows.Single(row => row.Fingerprint == "fp-old");
            var newRow = rows.Single(row => row.Fingerprint == "fp-new");
            Assert.Equal(DeviceState.Offline, oldRow.State);
            Assert.Equal(DeviceState.IdentityChanged, newRow.State);
        }

        [Fact]
        public void DuplicateDiscovered_CollapsesToNewest()
        {
            var older = Discovered("fp-1", "d1", "Old", "192.168.10.2");
            older.LastSeenUtc = DateTime.UtcNow.AddMinutes(-2);
            var newer = Discovered("fp-1", "d1", "New", "192.168.10.7");
            newer.LastSeenUtc = DateTime.UtcNow;

            var rows = DeviceRowMapper.Build(Array.Empty<PeerRecord>(), new[] { older, newer });

            var row = Assert.Single(rows);
            Assert.Equal("New", row.Name);
            Assert.Equal("192.168.10.7", row.IpAddress);
        }

        [Fact]
        public void Rows_AreSortedByStateThenName()
        {
            var peers = new[]
            {
                TestData.Record("fp-paired", "d1", "Zed", "192.168.10.2", 45873),
                TestData.Record("fp-paired-a", "d2", "Alpha", "192.168.10.3", 45873),
                TestData.Record("fp-offline", "d3", "Offline", "192.168.10.4", 45873),
                TestData.Record("fp-old", "d4", "Old", "192.168.10.5", 45873)
            };
            var discovered = new[]
            {
                Discovered("fp-paired", "d1", "Zed", "192.168.10.2"),
                Discovered("fp-paired-a", "d2", "Alpha", "192.168.10.3"),
                Discovered("fp-available", "d5", "Beta", "192.168.10.6"),
                Discovered("fp-new", "d4", "New", "192.168.10.7")
            };

            var rows = DeviceRowMapper.Build(peers, discovered);

            Assert.Equal(
                new[]
                {
                    DeviceState.Paired,
                    DeviceState.Paired,
                    DeviceState.Available,
                    DeviceState.Offline,
                    DeviceState.Offline,
                    DeviceState.IdentityChanged
                },
                rows.Select(row => row.State).ToArray());
            Assert.Equal("Alpha", rows[0].Name);
            Assert.Equal("Zed", rows[1].Name);
            Assert.Equal("Beta", rows[2].Name);
            Assert.Equal("New", rows.Last().Name);
        }

        [Fact]
        public void EmptyInputs_ReturnEmpty()
        {
            Assert.Empty(DeviceRowMapper.Build(Array.Empty<PeerRecord>(), Array.Empty<DiscoveredDevice>()));
        }

        [Fact]
        public void HostileDeviceName_IsSanitized()
        {
            var rows = DeviceRowMapper.Build(
                Array.Empty<PeerRecord>(),
                new[] { Discovered("fp-1", "d1", "Rohan\r\nFAKE", "192.168.10.7") });

            var row = Assert.Single(rows);
            Assert.DoesNotContain("\r", row.Name);
            Assert.DoesNotContain("\n", row.Name);
        }

        private static DiscoveredDevice Discovered(string fingerprint, string deviceId, string name, string ip)
        {
            return new DiscoveredDevice
            {
                Fingerprint = fingerprint,
                DeviceId = deviceId,
                Name = name,
                IPAddress = IPAddress.Parse(ip),
                Port = 45873,
                LastSeenUtc = DateTime.UtcNow
            };
        }
    }
}
