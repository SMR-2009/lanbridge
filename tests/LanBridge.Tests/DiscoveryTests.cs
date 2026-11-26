using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LanBridge.Core;
using Xunit;

namespace LanBridge.Tests
{
    public sealed class DiscoveryTests
    {
        [Fact]
        public void Announcement_RoundTrips()
        {
            var data = new DiscoveryPacketData
            {
                T = "a",
                V = 1,
                Id = "peer-1",
                Name = "Peer",
                Port = 45873,
                Fp = new string('a', 64),
                Discoverable = true
            };

            var bytes = DiscoveryPacket.Encode(data);

            Assert.Equal("LNB1", Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.True(bytes.Length <= DiscoveryPacket.MaxBytes);
            Assert.True(DiscoveryPacket.TryDecode(bytes, out var loaded, out var error), error);
            Assert.Equal(data.T, loaded.T);
            Assert.Equal(data.Id, loaded.Id);
            Assert.Equal(data.Name, loaded.Name);
            Assert.Equal(data.Port, loaded.Port);
            Assert.Equal(data.Fp, loaded.Fp);
            Assert.True(loaded.Discoverable);
        }

        [Fact]
        public async Task MalformedPacket_IsIgnored()
        {
            using var profile = new TempProfile();
            using var identity = TestData.Identity(profile, "self");
            var peers = PeerStore.Load(Path.Combine(profile.Root, "peers.json"), NullLogger.Instance);
            var port = TestData.FreeUdpPort();
            var confirmed = false;
            using var service = new DiscoveryService(
                port, "Self", identity, peers,
                (peer, ct) => Task.FromResult(true),
                NullLogger.Instance,
                allowLoopback: true);
            service.DeviceConfirmed += _ => confirmed = true;
            service.Start();

            SendRaw(port, Encoding.UTF8.GetBytes("not a discovery packet"));
            await Task.Delay(300);

            Assert.False(confirmed);
        }

        [Fact]
        public async Task ConfirmedDevice_IsRaised()
        {
            using var profile = new TempProfile();
            using var identity = TestData.Identity(profile, "self");
            var peers = PeerStore.Load(Path.Combine(profile.Root, "peers.json"), NullLogger.Instance);
            var port = TestData.FreeUdpPort();
            var result = new TaskCompletionSource<DiscoveredDevice>();
            using var service = new DiscoveryService(
                port, "Self", identity, peers,
                (peer, ct) => Task.FromResult(true),
                NullLogger.Instance,
                allowLoopback: true);
            service.DeviceConfirmed += device => result.TrySetResult(device);
            service.Start();

            SendAnnouncement(port, "peer-1", "Peer", new string('b', 64));

            var completed = await Task.WhenAny(result.Task, Task.Delay(2000));
            Assert.Same(result.Task, completed);
            var discovered = await result.Task;
            Assert.Equal("peer-1", discovered.DeviceId);
            Assert.Equal("Peer", discovered.Name);
            Assert.Equal(45873, discovered.Port);
            Assert.Equal(IPAddress.Loopback, discovered.IPAddress);
        }

        [Fact]
        public async Task ConfirmFalse_DoesNotRaise()
        {
            using var profile = new TempProfile();
            using var identity = TestData.Identity(profile, "self");
            var peers = PeerStore.Load(Path.Combine(profile.Root, "peers.json"), NullLogger.Instance);
            var port = TestData.FreeUdpPort();
            var confirmed = false;
            using var service = new DiscoveryService(
                port, "Self", identity, peers,
                (peer, ct) => Task.FromResult(false),
                NullLogger.Instance,
                allowLoopback: true);
            service.DeviceConfirmed += _ => confirmed = true;
            service.Start();

            SendAnnouncement(port, "peer-1", "Peer", new string('b', 64));
            await Task.Delay(400);

            Assert.False(confirmed);
        }

        [Fact]
        public async Task SelfAnnouncement_IsIgnored()
        {
            using var profile = new TempProfile();
            using var identity = TestData.Identity(profile, "self");
            var peers = PeerStore.Load(Path.Combine(profile.Root, "peers.json"), NullLogger.Instance);
            var port = TestData.FreeUdpPort();
            var confirmed = false;
            using var service = new DiscoveryService(
                port, "Self", identity, peers,
                (peer, ct) => Task.FromResult(true),
                NullLogger.Instance,
                allowLoopback: true);
            service.DeviceConfirmed += _ => confirmed = true;
            service.Start();

            SendAnnouncement(port, identity.DeviceId, "Self", identity.Fingerprint);
            await Task.Delay(400);

            Assert.False(confirmed);
        }

        [Fact]
        public async Task DiscoverableFalse_SuppressesAnnouncements()
        {
            using var profile = new TempProfile();
            using var identity = TestData.Identity(profile, "self");
            var peers = PeerStore.Load(Path.Combine(profile.Root, "peers.json"), NullLogger.Instance);
            var port = TestData.FreeUdpPort();
            using var service = new DiscoveryService(
                port, "Self", identity, peers,
                (peer, ct) => Task.FromResult(true),
                NullLogger.Instance,
                allowLoopback: true);
            service.Discoverable = false;
            service.Start();

            await Task.Delay(150);
            Assert.Equal(0, service.PacketsSent);

            service.Discoverable = true;
            service.Refresh();
            await Task.Delay(200);

            Assert.True(service.PacketsSent > 0);
        }

        private static void SendAnnouncement(int port, string id, string name, string fingerprint)
        {
            var data = new DiscoveryPacketData
            {
                T = "a",
                V = 1,
                Id = id,
                Name = name,
                Port = 45873,
                Fp = fingerprint,
                Discoverable = true
            };
            SendRaw(port, DiscoveryPacket.Encode(data));
        }

        private static void SendRaw(int port, byte[] bytes)
        {
            using var sender = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            sender.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, port));
        }
    }
}
