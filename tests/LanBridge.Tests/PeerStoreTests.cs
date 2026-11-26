using System;
using System.IO;
using System.Linq;
using LanBridge.Core;
using Xunit;

namespace LanBridge.Tests
{
    public sealed class PeerStoreTests
    {
        [Fact]
        public void SaveAndLoad_PreservesRecord()
        {
            using var profile = new TempProfile();
            var store = PeerStore.Load(profile.PeerPath, NullLogger.Instance);
            store.Upsert(Record("fp-1", "device-1", "Laptop-A", "192.168.10.7", 45873));
            store.Save();

            var loaded = PeerStore.Load(profile.PeerPath, NullLogger.Instance);
            var peer = Assert.Single(loaded.Peers);
            Assert.Equal("fp-1", peer.Fingerprint);
            Assert.Equal("device-1", peer.DeviceId);
            Assert.Equal("Laptop-A", peer.Name);
            Assert.Equal("192.168.10.7", peer.LastIp);
            Assert.Equal(45873, peer.LastPort);
        }

        [Fact]
        public void Upsert_ReplacesSameFingerprint()
        {
            using var profile = new TempProfile();
            var store = PeerStore.Load(profile.PeerPath, NullLogger.Instance);
            store.Upsert(Record("fp-1", "d1", "Old", "10.0.0.1", 1));
            store.Upsert(Record("fp-1", "d1", "New", "10.0.0.2", 2));

            var peer = Assert.Single(store.Peers);
            Assert.Equal("New", peer.Name);
            Assert.Equal("10.0.0.2", peer.LastIp);
        }

        [Fact]
        public void SameDeviceIdWithDifferentFingerprints_KeepsBoth()
        {
            using var profile = new TempProfile();
            var store = PeerStore.Load(profile.PeerPath, NullLogger.Instance);
            store.Upsert(Record("fp-old", "d1", "Old", "10.0.0.1", 1));
            store.Upsert(Record("fp-new", "d1", "New", "10.0.0.2", 2));

            Assert.Equal(2, store.Peers.Count);
            Assert.Equal(2, store.FindByDeviceId("d1").Count);
        }

        [Fact]
        public void UpdateEndpoint_ChangesOnlyTheEndpoint()
        {
            using var profile = new TempProfile();
            var store = PeerStore.Load(profile.PeerPath, NullLogger.Instance);
            store.Upsert(Record("fp-1", "d1", "Name", "10.0.0.1", 1));

            Assert.True(store.UpdateEndpoint("fp-1", "10.0.0.9", 999));
            var peer = Assert.Single(store.Peers);
            Assert.Equal("10.0.0.9", peer.LastIp);
            Assert.Equal(999, peer.LastPort);
            Assert.Equal("Name", peer.Name);
        }

        [Fact]
        public void Remove_DeletesByFingerprint()
        {
            using var profile = new TempProfile();
            var store = PeerStore.Load(profile.PeerPath, NullLogger.Instance);
            store.Upsert(Record("fp-1", "d1", "Name", "10.0.0.1", 1));

            Assert.True(store.Remove("fp-1"));
            Assert.Empty(store.Peers);
            Assert.False(store.Remove("fp-1"));
        }

        [Fact]
        public void CorruptFile_IsQuarantined()
        {
            using var profile = new TempProfile();
            File.WriteAllText(profile.PeerPath, "{not json");
            var store = PeerStore.Load(profile.PeerPath, NullLogger.Instance);

            Assert.Empty(store.Peers);
            Assert.Contains(Directory.GetFiles(profile.Root), f => f.Contains("peers.corrupt-"));
        }

        [Fact]
        public void ContainsFingerprint_HandlesNullAndEmpty()
        {
            using var profile = new TempProfile();
            var store = PeerStore.Load(profile.PeerPath, NullLogger.Instance);
            store.Upsert(Record("FP-1", "d1", "Name", "10.0.0.1", 1));

            Assert.True(store.ContainsFingerprint("FP-1"));
            Assert.False(store.ContainsFingerprint("fp-1"));
            Assert.False(store.ContainsFingerprint(null));
            Assert.False(store.ContainsFingerprint(""));
        }

        private static PeerRecord Record(string fingerprint, string deviceId, string name, string ip, int port)
        {
            return new PeerRecord
            {
                Fingerprint = fingerprint,
                DeviceId = deviceId,
                Name = name,
                LastIp = ip,
                LastPort = port,
                PairedAtUtc = DateTime.UtcNow,
                LastSeenUtc = DateTime.UtcNow
            };
        }
    }
}
