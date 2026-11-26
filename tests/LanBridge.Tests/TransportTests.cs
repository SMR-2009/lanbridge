using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using LanBridge.Core;
using Xunit;

namespace LanBridge.Tests
{
    public sealed class TransportTests
    {
        [Fact]
        public async Task Ping_Succeeds()
        {
            using var profile = new TempProfile();
            using var serverIdentity = TestData.Identity(profile, "server");
            using var clientIdentity = TestData.Identity(profile, "client");
            var serverStore = PeerStore.Load(Path.Combine(profile.Root, "server-peers.json"), NullLogger.Instance);
            var clientStore = PeerStore.Load(Path.Combine(profile.Root, "client-peers.json"), NullLogger.Instance);
            var port = TestData.FreePort();
            var logger = new CollectingLogger();

            using var server = new TransferServer(
                port, "Server", serverIdentity, serverStore,
                () => profile.ReceiveDir, new FakePrompter(), logger,
                allowLoopback: true);
            server.Start();

            var client = new TransferClient("Client", clientIdentity, clientStore, new FakePrompter(), logger);
            var peer = new PeerEndpoint(IPAddress.Loopback, port, serverIdentity.Fingerprint, "srv", "Server");

            var result = await client.PingAsync(peer, default);
            Assert.True(result, string.Join(Environment.NewLine, logger.Lines));
        }

        [Fact]
        public async Task Ping_WrongFingerprint_Fails()
        {
            using var profile = new TempProfile();
            using var serverIdentity = TestData.Identity(profile, "server");
            using var clientIdentity = TestData.Identity(profile, "client");
            var serverStore = PeerStore.Load(Path.Combine(profile.Root, "server-peers.json"), NullLogger.Instance);
            var clientStore = PeerStore.Load(Path.Combine(profile.Root, "client-peers.json"), NullLogger.Instance);
            var port = TestData.FreePort();

            using var server = new TransferServer(
                port, "Server", serverIdentity, serverStore,
                () => profile.ReceiveDir, new FakePrompter(), NullLogger.Instance,
                allowLoopback: true);
            server.Start();

            var client = new TransferClient("Client", clientIdentity, clientStore, new FakePrompter(), NullLogger.Instance);
            var peer = new PeerEndpoint(IPAddress.Loopback, port, new string('0', 64), "srv", "Server");

            Assert.False(await client.PingAsync(peer, default));
        }

        [Fact]
        public async Task Hello_RejectsPublicPeer()
        {
            using var profile = new TempProfile();
            using var serverIdentity = TestData.Identity(profile, "server");
            using var clientIdentity = TestData.Identity(profile, "client");
            var serverStore = PeerStore.Load(Path.Combine(profile.Root, "server-peers.json"), NullLogger.Instance);
            var clientStore = PeerStore.Load(Path.Combine(profile.Root, "client-peers.json"), NullLogger.Instance);
            var port = TestData.FreePort();
            var scope = new LanScope(new[]
            {
                new LanInterface(
                    "LAN",
                    IPAddress.Parse("10.0.0.5"),
                    IPAddress.Parse("255.0.0.0"),
                    IPAddress.Parse("10.255.255.255"))
            });

            using var server = new TransferServer(
                port, "Server", serverIdentity, serverStore,
                () => profile.ReceiveDir, new FakePrompter(), NullLogger.Instance,
                allowLoopback: false,
                scopeFactory: () => scope);
            server.Start();

            var client = new TransferClient("Client", clientIdentity, clientStore, new FakePrompter(), NullLogger.Instance);
            var peer = new PeerEndpoint(IPAddress.Loopback, port, serverIdentity.Fingerprint, "srv", "Server");

            Assert.False(await client.PingAsync(peer, default));
        }

        [Fact]
        public async Task Hello_VersionMismatch_ReturnsVersionError()
        {
            using var profile = new TempProfile();
            using var serverIdentity = TestData.Identity(profile, "server");
            using var clientIdentity = TestData.Identity(profile, "client");
            var serverStore = PeerStore.Load(Path.Combine(profile.Root, "server-peers.json"), NullLogger.Instance);
            var port = TestData.FreePort();

            using var server = new TransferServer(
                port, "Server", serverIdentity, serverStore,
                () => profile.ReceiveDir, new FakePrompter(), NullLogger.Instance,
                allowLoopback: true);
            server.Start();

            using var tcp = new TcpClient(AddressFamily.InterNetwork);
            await tcp.ConnectAsync(IPAddress.Loopback, port);
            using var ssl = Tls.CreateClient(tcp.GetStream(), clientIdentity.Certificate, serverIdentity.Fingerprint);
            await ssl.AuthenticateAsClientAsync(
                "lanbridge",
                new X509CertificateCollection { clientIdentity.Certificate },
                SslProtocols.Tls12,
                false);

            var hello = new HelloDto
            {
                V = 99,
                Id = clientIdentity.DeviceId,
                Name = "Client",
                Fp = clientIdentity.Fingerprint,
                Nonce = Convert.ToBase64String(new byte[16]),
                Intent = "ping",
                Paired = false
            };
            await FrameCodec.WriteAsync(ssl, FrameType.Hello, ProtocolJson.Serialize(hello), default);

            var frame = await FrameCodec.ReadAsync(ssl, default);
            Assert.Equal(FrameType.Error, frame.Type);
            var error = ProtocolJson.Deserialize<ErrorDto>(frame.Payload);
            Assert.Equal("version", error.Code);
        }
    }
}
