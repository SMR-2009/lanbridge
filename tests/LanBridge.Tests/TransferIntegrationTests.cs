using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LanBridge.Core;
using Xunit;

namespace LanBridge.Tests
{
    public sealed class TransferIntegrationTests
    {
        [Fact]
        public async Task Send_TwoFiles_WritesThem()
        {
            using var harness = new TransferHarness();
            Assert.True((await harness.PairAsync()).Success);

            var first = harness.SourceFile("a.txt", Encoding.UTF8.GetBytes("alpha"));
            var second = harness.SourceFile("b.bin", new byte[] { 1, 2, 3, 4 });

            var result = await harness.Client.SendAsync(harness.Peer, new[] { first, second }, default);

            Assert.True(result.Success, result.ErrorCode);
            Assert.Equal(2, result.FilesCompleted);
            Assert.Equal("alpha", File.ReadAllText(harness.ReceivePath("a.txt")));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(harness.ReceivePath("b.bin")));
            Assert.Empty(Directory.GetFiles(harness.Profile.ReceiveDir, "*.part"));
        }

        [Fact]
        public async Task Send_DuplicateName_GetsSuffix()
        {
            using var harness = new TransferHarness();
            Assert.True((await harness.PairAsync()).Success);
            File.WriteAllText(harness.ReceivePath("a.txt"), "old");
            var source = harness.SourceFile("a.txt", Encoding.UTF8.GetBytes("new"));

            var result = await harness.Client.SendAsync(harness.Peer, new[] { source }, default);

            Assert.True(result.Success, result.ErrorCode);
            Assert.Equal("old", File.ReadAllText(harness.ReceivePath("a.txt")));
            Assert.Equal("new", File.ReadAllText(harness.ReceivePath("a (1).txt")));
        }

        [Fact]
        public async Task Send_Unpaired_Rejected()
        {
            using var harness = new TransferHarness();
            var source = harness.SourceFile("a.txt", Encoding.UTF8.GetBytes("no"));

            var result = await harness.Client.SendAsync(harness.Peer, new[] { source }, default);

            Assert.False(result.Success);
            Assert.Equal("not_paired", result.ErrorCode);
            Assert.Empty(Directory.GetFiles(harness.Profile.ReceiveDir));
            Assert.Empty(harness.ServerStore.Peers);
        }

        [Fact]
        public async Task Send_OneSidedPairing_Rejected()
        {
            using var harness = new TransferHarness();
            Assert.True((await harness.PairAsync()).Success);
            harness.ClientStore.Remove(harness.ServerIdentity.Fingerprint);
            harness.ClientStore.Save();
            var source = harness.SourceFile("a.txt", Encoding.UTF8.GetBytes("one-sided"));

            var result = await harness.Client.SendAsync(harness.Peer, new[] { source }, default);

            Assert.False(result.Success);
            Assert.Equal("not_paired", result.ErrorCode);
            Assert.True(harness.ServerStore.ContainsFingerprint(harness.ClientIdentity.Fingerprint));
            Assert.Empty(harness.ClientStore.Peers);
            Assert.Empty(Directory.GetFiles(harness.Profile.ReceiveDir));
        }

        [Fact]
        public async Task Send_Declined_NoFile()
        {
            using var harness = new TransferHarness();
            harness.Prompter.ApproveOffers = false;
            Assert.True((await harness.PairAsync()).Success);
            var source = harness.SourceFile("a.txt", Encoding.UTF8.GetBytes("declined"));

            var result = await harness.Client.SendAsync(harness.Peer, new[] { source }, default);

            Assert.False(result.Success);
            Assert.Equal("offer_rejected", result.ErrorCode);
            Assert.Empty(Directory.GetFiles(harness.Profile.ReceiveDir));
            Assert.Single(harness.Prompter.Offers);
        }

        [Fact]
        public async Task Send_BusyInbound()
        {
            using var harness = new TransferHarness();
            Assert.True((await harness.PairAsync()).Success);
            harness.Prompter.OfferGate = new TaskCompletionSource<bool>();
            var firstSource = harness.SourceFile("first.bin", new byte[1024 * 1024]);
            var firstTask = harness.Client.SendAsync(harness.Peer, new[] { firstSource }, default);
            await WaitUntilAsync(() => harness.Server.IsReceiving, 2000);

            var secondSource = harness.SourceFile("second.bin", new byte[16]);
            var second = await harness.Client.SendAsync(harness.Peer, new[] { secondSource }, default);

            Assert.False(second.Success);
            Assert.Equal("busy_inbound", second.ErrorCode);

            harness.Prompter.OfferGate.SetResult(true);
            var first = await firstTask;
            Assert.True(first.Success, first.ErrorCode);
        }

        [Fact]
        public async Task Send_Cancel_DeletesPart()
        {
            using var harness = new TransferHarness();
            Assert.True((await harness.PairAsync()).Success);
            var source = harness.SourceFile("big.bin", new byte[64 * 1024 * 1024]);

            using var cts = new CancellationTokenSource();
            var task = harness.Client.SendAsync(harness.Peer, new[] { source }, cts.Token);
            await Task.Delay(20);
            cts.Cancel();
            var result = await task;

            Assert.False(result.Success);
            Assert.Equal("cancelled", result.ErrorCode);
            await WaitUntilAsync(() => !harness.Server.IsReceiving, 3000);
            Assert.Empty(Directory.GetFiles(harness.Profile.ReceiveDir, "*.part"));
        }

        [Fact]
        public async Task Send_FileError_AbortsAndKeepsCompleted()
        {
            using var harness = new TransferHarness(receiverFactory: (folder, name, size) =>
            {
                if (name == "b.bin") throw new IOException("injected receiver failure");
                return new FileReceiver(folder, name, size);
            });
            Assert.True((await harness.PairAsync()).Success);
            var first = harness.SourceFile("a.bin", new byte[1024]);
            var second = harness.SourceFile("b.bin", new byte[1024]);

            var result = await harness.Client.SendAsync(harness.Peer, new[] { first, second }, default);

            Assert.False(result.Success);
            Assert.Equal("transfer_failed", result.ErrorCode);
            Assert.Equal(1, result.FilesCompleted);
            Assert.True(File.Exists(harness.ReceivePath("a.bin")));
            Assert.False(File.Exists(harness.ReceivePath("b.bin")));
            Assert.Empty(Directory.GetFiles(harness.Profile.ReceiveDir, "*.part"));
        }

        [Fact]
        public async Task Send_And_Receive_AreIndependent()
        {
            using var firstHarness = new TransferHarness();
            using var secondHarness = new TransferHarness();
            Assert.True((await firstHarness.PairAsync()).Success);
            Assert.True((await secondHarness.PairAsync()).Success);

            firstHarness.Prompter.OfferGate = new TaskCompletionSource<bool>();
            var firstSource = firstHarness.SourceFile("first.bin", new byte[1024]);
            var firstTask = firstHarness.Client.SendAsync(firstHarness.Peer, new[] { firstSource }, default);
            await WaitUntilAsync(() => firstHarness.Server.IsReceiving, 2000);

            var secondSource = secondHarness.SourceFile("second.bin", new byte[2048]);
            var secondResult = await secondHarness.Client.SendAsync(secondHarness.Peer, new[] { secondSource }, default);
            Assert.True(secondResult.Success, secondResult.ErrorCode);

            firstHarness.Prompter.OfferGate.SetResult(true);
            var firstResult = await firstTask;
            Assert.True(firstResult.Success, firstResult.ErrorCode);
        }

        private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!condition() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }
        }

        private sealed class TransferHarness : IDisposable
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

            public TransferHarness(
                Func<string, string, long, FileReceiver> receiverFactory = null,
                TransferOptions options = null)
            {
                options = options ?? new TransferOptions();
                Profile = new TempProfile();
                ServerIdentity = TestData.Identity(Profile, "server");
                ClientIdentity = TestData.Identity(Profile, "client");
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
                    allowLoopback: true,
                    receiverFactory: receiverFactory);
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

            public Task<PairingAttemptResult> PairAsync()
            {
                return Client.PairAsync(Peer, default);
            }

            public string SourceFile(string name, byte[] content)
            {
                var directory = Path.Combine(Profile.Root, "source");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, name);
                File.WriteAllBytes(path, content);
                return path;
            }

            public string ReceivePath(string name)
            {
                return Path.Combine(Profile.ReceiveDir, name);
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
