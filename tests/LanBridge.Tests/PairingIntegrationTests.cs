using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using LanBridge.Core;
using Xunit;

namespace LanBridge.Tests
{
    public sealed class PairingIntegrationTests
    {
        [Fact]
        public async Task Pair_HappyPath()
        {
            using var harness = new PairHarness();
            PeerRecord pairedEvent = null;
            harness.Server.DevicePaired += record => pairedEvent = record;

            var result = await harness.Client.PairAsync(harness.Peer, default);

            Assert.True(result.Success, result.ErrorCode);
            Assert.Equal(6, harness.Prompter.LastCode.Length);
            Assert.Single(harness.ServerStore.Peers);
            Assert.Single(harness.ClientStore.Peers);
            Assert.Equal(harness.ClientIdentity.Fingerprint, harness.ServerStore.Peers[0].Fingerprint);
            Assert.Equal(harness.ServerIdentity.Fingerprint, harness.ClientStore.Peers[0].Fingerprint);
            Assert.Equal("Client", harness.ServerStore.Peers[0].Name);
            Assert.Equal("Server", harness.ClientStore.Peers[0].Name);
            Assert.NotNull(pairedEvent);
        }

        [Fact]
        public async Task Pair_WrongCode_ThreeAttempts_NoPairing()
        {
            using var harness = new PairHarness();
            harness.Prompter.PairingCode = "000000";

            var result = await harness.Client.PairAsync(harness.Peer, default);

            Assert.False(result.Success);
            Assert.Equal("wrong_code", result.ErrorCode);
            Assert.Empty(harness.ServerStore.Peers);
            Assert.Empty(harness.ClientStore.Peers);
            Assert.Equal(3, harness.Prompter.CodePrompts.Count);
            await WaitUntilAsync(() => harness.Prompter.CloseCount > 0);
            Assert.True(harness.Prompter.CloseCount > 0);
        }

        [Fact]
        public async Task Pair_Denied_NoPairing()
        {
            using var harness = new PairHarness();
            harness.Prompter.ApprovePairing = false;

            var result = await harness.Client.PairAsync(harness.Peer, default);

            Assert.False(result.Success);
            Assert.Equal("denied", result.ErrorCode);
            Assert.Empty(harness.ServerStore.Peers);
            Assert.Empty(harness.ClientStore.Peers);
        }

        [Fact]
        public async Task Pair_ApprovalTimeout()
        {
            using var harness = new PairHarness(new TransferOptions
            {
                PairApprovalTimeout = TimeSpan.FromMilliseconds(100),
                PairCodeTimeout = TimeSpan.FromMilliseconds(100)
            });
            harness.Prompter.BlockPairingApproval = true;

            var result = await harness.Client.PairAsync(harness.Peer, default);

            Assert.False(result.Success);
            Assert.Equal("timeout", result.ErrorCode);
            Assert.Empty(harness.ServerStore.Peers);
            Assert.Empty(harness.ClientStore.Peers);
            await WaitUntilAsync(() => harness.Prompter.CloseCount > 0);
            Assert.True(harness.Prompter.CloseCount > 0);
        }

        [Fact]
        public async Task Pair_CodeTimeout()
        {
            using var harness = new PairHarness(new TransferOptions
            {
                PairApprovalTimeout = TimeSpan.FromMilliseconds(100),
                PairCodeTimeout = TimeSpan.FromMilliseconds(100)
            });
            harness.Prompter.BlockPairingCode = true;

            var result = await harness.Client.PairAsync(harness.Peer, default);

            Assert.False(result.Success);
            Assert.Equal("timeout", result.ErrorCode);
            Assert.Empty(harness.ServerStore.Peers);
            Assert.Empty(harness.ClientStore.Peers);
            await WaitUntilAsync(() => harness.Prompter.CloseCount > 0);
            Assert.True(harness.Prompter.CloseCount > 0);
        }

        [Fact]
        public async Task Pair_Hidden_UnpairedRejected()
        {
            using var harness = new PairHarness();
            harness.Server.Discoverable = false;

            var result = await harness.Client.PairAsync(harness.Peer, default);

            Assert.False(result.Success);
            Assert.Equal("hidden", result.ErrorCode);
            Assert.Empty(harness.ServerStore.Peers);
            Assert.Empty(harness.ClientStore.Peers);
        }

        [Fact]
        public async Task Pair_IdentityChanged_KeepsOldPin()
        {
            using var harness = new PairHarness(clientDeviceId: "d1");
            harness.ServerStore.Upsert(TestData.Record("f1", "d1", "Old", "10.0.0.1", 45873));

            var result = await harness.Client.PairAsync(harness.Peer, default);

            Assert.True(result.Success, result.ErrorCode);
            Assert.True(harness.ServerStore.ContainsFingerprint("f1"));
            Assert.True(harness.ServerStore.ContainsFingerprint(harness.ClientIdentity.Fingerprint));
            Assert.Equal(2, harness.ServerStore.FindByDeviceId("d1").Count);
        }

        private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!condition() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }
        }

        private sealed class PairHarness : IDisposable
        {
            public TempProfile Profile { get; }
            public DeviceIdentity ServerIdentity { get; }
            public DeviceIdentity ClientIdentity { get; }
            public PeerStore ServerStore { get; }
            public PeerStore ClientStore { get; }
            public FakePrompter Prompter { get; }
            public TransferServer Server { get; }
            public TransferClient Client { get; }
            public PeerEndpoint Peer { get; }

            public PairHarness(TransferOptions options = null, string clientDeviceId = "client")
            {
                options = options ?? new TransferOptions();
                Profile = new TempProfile();
                ServerIdentity = TestData.Identity(Profile, "server");
                ClientIdentity = DeviceIdentity.LoadOrCreate(
                    Path.Combine(Profile.Root, "client"),
                    clientDeviceId,
                    NullLogger.Instance);
                ServerStore = PeerStore.Load(Path.Combine(Profile.Root, "server-peers.json"), NullLogger.Instance);
                ClientStore = PeerStore.Load(Path.Combine(Profile.Root, "client-peers.json"), NullLogger.Instance);
                Prompter = new FakePrompter();

                var port = TestData.FreePort();
                Server = new TransferServer(
                    port,
                    "Server",
                    ServerIdentity,
                    ServerStore,
                    () => Profile.ReceiveDir,
                    Prompter,
                    NullLogger.Instance,
                    options,
                    allowLoopback: true);
                Server.Start();

                Client = new TransferClient(
                    "Client",
                    ClientIdentity,
                    ClientStore,
                    Prompter,
                    NullLogger.Instance,
                    options);
                Peer = new PeerEndpoint(IPAddress.Loopback, port, ServerIdentity.Fingerprint, "server", "Server");
            }

            public void Dispose()
            {
                Server.Dispose();
                ClientIdentity.Dispose();
                ServerIdentity.Dispose();
                Profile.Dispose();
            }
        }
    }
}
