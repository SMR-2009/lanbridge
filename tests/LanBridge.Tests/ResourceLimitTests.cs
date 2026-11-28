using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using LanBridge.Core;
using Xunit;

namespace LanBridge.Tests
{
    public sealed class ResourceLimitTests
    {
        [Fact]
        public void FreeSpaceCheck_RejectsOverflowingDeclaredSize()
        {
            Assert.False(TransferServer.HasFreeSpace(Path.GetTempPath(), long.MaxValue));
            Assert.False(TransferServer.HasFreeSpace(Path.GetTempPath(), long.MaxValue - 10));
            Assert.True(TransferServer.HasFreeSpace(Path.GetTempPath(), 0));
        }

        [Fact]
        public async Task InboundClients_PerIpLimit_RecoversAfterDisconnect()
        {
            using var profile = new TempProfile();
            using var identity = TestData.Identity(profile, "server");
            var port = TestData.FreePort();
            using var server = new TransferServer(
                port,
                "Server",
                identity,
                PeerStore.Load(Path.Combine(profile.Root, "peers.json"), NullLogger.Instance),
                () => profile.ReceiveDir,
                new FakePrompter(),
                NullLogger.Instance,
                allowLoopback: true,
                maxConcurrentClients: 8);
            server.Start();

            var held = new List<TcpClient>();
            TcpClient extra = null;
            TcpClient recovered = null;
            try
            {
                for (var i = 0; i < 4; i++)
                {
                    var client = new TcpClient(AddressFamily.InterNetwork);
                    await client.ConnectAsync(IPAddress.Loopback, port);
                    held.Add(client);
                }

                await WaitUntilAsync(() => server.ActiveClientCount == 4, 3000);

                extra = new TcpClient(AddressFamily.InterNetwork);
                await extra.ConnectAsync(IPAddress.Loopback, port);
                await WaitUntilAsync(() => server.RejectedClientCount > 0, 3000);
                Assert.True(server.RejectedClientCount > 0);

                extra.Dispose();
                extra = null;
                foreach (var client in held) client.Dispose();
                held.Clear();
                await WaitUntilAsync(() => server.ActiveClientCount == 0, 5000);

                recovered = new TcpClient(AddressFamily.InterNetwork);
                await recovered.ConnectAsync(IPAddress.Loopback, port);
                await WaitUntilAsync(() => server.ActiveClientCount == 1, 3000);
                Assert.Equal(1, server.ActiveClientCount);
            }
            finally
            {
                extra?.Dispose();
                recovered?.Dispose();
                foreach (var client in held) client.Dispose();
            }
        }

        [Fact]
        public async Task InboundClients_AboveLimit_AreRejected()
        {
            using var profile = new TempProfile();
            using var identity = TestData.Identity(profile, "server");
            var port = TestData.FreePort();
            using var server = new TransferServer(
                port,
                "Server",
                identity,
                PeerStore.Load(Path.Combine(profile.Root, "peers.json"), NullLogger.Instance),
                () => profile.ReceiveDir,
                new FakePrompter(),
                NullLogger.Instance,
                allowLoopback: true,
                maxConcurrentClients: 2);
            server.Start();

            var clients = new List<TcpClient>();
            try
            {
                for (var i = 0; i < 3; i++)
                {
                    var client = new TcpClient(AddressFamily.InterNetwork);
                    await client.ConnectAsync(IPAddress.Loopback, port);
                    clients.Add(client);
                }

                await WaitUntilAsync(() => server.RejectedClientCount > 0, 3000);
                Assert.True(server.RejectedClientCount > 0);
            }
            finally
            {
                foreach (var client in clients)
                {
                    client.Dispose();
                }
            }
        }

        private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!condition() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }
        }
    }
}
