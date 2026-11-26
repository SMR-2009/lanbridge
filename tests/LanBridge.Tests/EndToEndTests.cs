using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LanBridge.Core;
using Xunit;

namespace LanBridge.Tests
{
    public sealed class EndToEndTests
    {
        [Fact]
        public async Task Pair_DisconnectWhileApprovalPending_ClosesDialogAndFreesLock()
        {
            using var harness = new EndToEndHarness(new TransferOptions
            {
                PairApprovalTimeout = TimeSpan.FromMilliseconds(100),
                PairCodeTimeout = TimeSpan.FromMilliseconds(100)
            });
            harness.Prompter.BlockPairingApproval = true;

            using (var cts = new CancellationTokenSource(80))
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => harness.Client.PairAsync(harness.Peer, cts.Token));
            }

            await WaitUntilAsync(() => harness.Prompter.CloseCount > 0, 2000);
            Assert.True(harness.Prompter.CloseCount > 0);

            harness.Prompter.BlockPairingApproval = false;
            harness.Prompter.PairingCode = null;
            var second = await harness.Client.PairAsync(harness.Peer, default);
            Assert.True(second.Success, second.ErrorCode);
        }

        [Fact]
        public async Task Pair_OneSided_DoesNotAutoTrustOrDelete()
        {
            using var harness = new EndToEndHarness();
            var paired = await harness.Client.PairAsync(harness.Peer, default);
            Assert.True(paired.Success, paired.ErrorCode);

            harness.ServerStore.Remove(harness.ClientIdentity.Fingerprint);
            harness.ServerStore.Save();

            var source = harness.SourceFile("one-sided.txt", Encoding.UTF8.GetBytes("data"));
            var result = await harness.Client.SendAsync(harness.Peer, new[] { source }, default);

            Assert.False(result.Success);
            Assert.Equal("not_paired", result.ErrorCode);
            Assert.Empty(harness.ServerStore.Peers);
            Assert.True(harness.ClientStore.ContainsFingerprint(harness.ServerIdentity.Fingerprint));
            Assert.Empty(Directory.GetFiles(harness.Profile.ReceiveDir));
        }

        [Fact]
        public async Task Send_HiddenPaired_UsesLastEndpoint()
        {
            using var harness = new EndToEndHarness();
            Assert.True((await harness.Client.PairAsync(harness.Peer, default)).Success);
            harness.Server.Discoverable = false;
            var source = harness.SourceFile("hidden.txt", Encoding.UTF8.GetBytes("still works"));

            var result = await harness.Client.SendAsync(harness.Peer, new[] { source }, default);

            Assert.True(result.Success, result.ErrorCode);
            Assert.Equal("still works", File.ReadAllText(harness.ReceivePath("hidden.txt")));
        }

        [Fact]
        public async Task Send_HiddenUnpaired_RejectedWithoutFile()
        {
            using var harness = new EndToEndHarness();
            harness.Server.Discoverable = false;
            var source = harness.SourceFile("hidden-unpaired.txt", Encoding.UTF8.GetBytes("no"));

            var result = await harness.Client.SendAsync(harness.Peer, new[] { source }, default);

            Assert.False(result.Success);
            Assert.Equal("not_paired", result.ErrorCode);
            Assert.Empty(Directory.GetFiles(harness.Profile.ReceiveDir));
        }

        [Fact]
        public async Task Send_OtherSubnet_RejectedBeforeOffer()
        {
            var scope = new LanScope(new[]
            {
                new LanInterface(
                    "VPN",
                    IPAddress.Parse("10.0.0.5"),
                    IPAddress.Parse("255.0.0.0"),
                    IPAddress.Parse("10.255.255.255"))
            });
            using var harness = new EndToEndHarness(scopeFactory: () => scope, allowLoopback: false);
            var source = harness.SourceFile("vpn.txt", Encoding.UTF8.GetBytes("no"));

            var result = await harness.Client.SendAsync(harness.Peer, new[] { source }, default);

            Assert.False(result.Success);
            Assert.Equal("not_lan", result.ErrorCode);
            Assert.Empty(harness.Prompter.Offers);
            Assert.Empty(Directory.GetFiles(harness.Profile.ReceiveDir));
        }

        [Fact]
        public async Task Send_ReceiverCancel_StopsTransferAndRemovesPart()
        {
            using var harness = new EndToEndHarness();
            Assert.True((await harness.Client.PairAsync(harness.Peer, default)).Success);
            var source = harness.SourceFile("cancel.bin", new byte[32 * 1024 * 1024]);

            var task = harness.Client.SendAsync(harness.Peer, new[] { source }, default);
            await WaitUntilAsync(() => harness.Server.IsReceiving, 2000);
            await Task.Delay(30);
            harness.Server.CancelActiveTransfer();

            var result = await task;
            Assert.False(result.Success);
            await WaitUntilAsync(() => !harness.Server.IsReceiving, 3000);
            Assert.Empty(Directory.GetFiles(harness.Profile.ReceiveDir, "*.part"));
        }

        [Fact]
        public async Task PairingCode_AttemptsResetForANewSession()
        {
            using var harness = new EndToEndHarness();
            harness.Prompter.PairingCode = "000000";

            var first = await harness.Client.PairAsync(harness.Peer, default);
            Assert.False(first.Success);
            Assert.Equal("wrong_code", first.ErrorCode);
            Assert.Equal(3, harness.Prompter.CodePrompts.Count);

            harness.Prompter.PairingCode = null;
            var second = await harness.Client.PairAsync(harness.Peer, default);
            Assert.True(second.Success, second.ErrorCode);
            Assert.Equal(4, harness.Prompter.CodePrompts.Count);
        }

        [Fact]
        public void DeviceNameAndLogValues_DoNotInjectLines()
        {
            var name = Sanitizer.SanitizeDeviceName("Rohan\r\nFAKE LOG");
            var log = Sanitizer.SanitizeLogValue("a\r\nb", 20);

            Assert.DoesNotContain("\r", name);
            Assert.DoesNotContain("\n", name);
            Assert.DoesNotContain("\r", log);
            Assert.DoesNotContain("\n", log);
        }

        private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!condition() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }
        }

        private sealed class EndToEndHarness : IDisposable
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

            public EndToEndHarness(
                TransferOptions options = null,
                Func<LanScope> scopeFactory = null,
                bool allowLoopback = true)
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
                    allowLoopback,
                    scopeFactory);
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
