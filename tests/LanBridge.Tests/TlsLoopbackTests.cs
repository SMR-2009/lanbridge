using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using LanBridge.Core;
using Xunit;

namespace LanBridge.Tests
{
    public sealed class TlsLoopbackTests
    {
        [Fact]
        public async Task PinnedServer_Handshake_Succeeds()
        {
            using var profile = new TempProfile();
            using var server = DeviceIdentity.LoadOrCreate(Path.Combine(profile.Root, "server"), "server", NullLogger.Instance);
            using var client = DeviceIdentity.LoadOrCreate(Path.Combine(profile.Root, "client"), "client", NullLogger.Instance);
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var accept = Task.Run(async () =>
            {
                using var tcp = await listener.AcceptTcpClientAsync();
                using var ssl = Tls.CreateServer(tcp.GetStream(), server.Certificate);
                await ssl.AuthenticateAsServerAsync(server.Certificate, true, SslProtocols.Tls12, false);
                var buffer = new byte[4];
                await ReadExactAsync(ssl, buffer, 4);
                await ssl.WriteAsync(buffer, 0, 4);
            });

            using var clientTcp = new TcpClient();
            await clientTcp.ConnectAsync(IPAddress.Loopback, port);
            using var clientSsl = Tls.CreateClient(clientTcp.GetStream(), client.Certificate, server.Fingerprint);
            try
            {
                await clientSsl.AuthenticateAsClientAsync("lanbridge", new X509CertificateCollection { client.Certificate }, SslProtocols.Tls12, false);
            }
            catch (Exception ex)
            {
                await Task.WhenAny(accept, Task.Delay(2000));
                var serverError = accept.IsFaulted
                    ? accept.Exception?.GetBaseException().Message
                    : "server task did not fault";
                throw new InvalidOperationException("Client handshake failed: " + ex.Message + "; server: " + serverError, ex);
            }

            var remote = new X509Certificate2(clientSsl.RemoteCertificate);
            Assert.Equal(server.Fingerprint, Tls.Fingerprint(remote));

            var payload = new byte[] { 1, 2, 3, 4 };
            await clientSsl.WriteAsync(payload, 0, payload.Length);
            var response = new byte[4];
            await ReadExactAsync(clientSsl, response, 4);
            Assert.Equal(payload, response);
            await accept;
            listener.Stop();
        }

        [Fact]
        public async Task WrongPin_HandshakeFails()
        {
            using var profile = new TempProfile();
            using var server = DeviceIdentity.LoadOrCreate(Path.Combine(profile.Root, "server"), "server", NullLogger.Instance);
            using var client = DeviceIdentity.LoadOrCreate(Path.Combine(profile.Root, "client"), "client", NullLogger.Instance);
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var accept = Task.Run(async () =>
            {
                try
                {
                    using var tcp = await listener.AcceptTcpClientAsync();
                    using var ssl = Tls.CreateServer(tcp.GetStream(), server.Certificate);
                    await ssl.AuthenticateAsServerAsync(server.Certificate, true, SslProtocols.Tls12, false);
                }
                catch
                {
                }
            });

            using var clientTcp = new TcpClient();
            await clientTcp.ConnectAsync(IPAddress.Loopback, port);
            using var clientSsl = Tls.CreateClient(clientTcp.GetStream(), client.Certificate, new string('0', 64));
            var ex = await Record.ExceptionAsync(() => clientSsl.AuthenticateAsClientAsync(
                "lanbridge",
                new X509CertificateCollection { client.Certificate },
                SslProtocols.Tls12,
                false));

            Assert.NotNull(ex);
            Assert.True(ex is AuthenticationException || ex is IOException);
            await Task.WhenAny(accept, Task.Delay(2000));
            listener.Stop();
        }

        private static async Task ReadExactAsync(Stream stream, byte[] buffer, int count)
        {
            var offset = 0;
            while (offset < count)
            {
                var read = await stream.ReadAsync(buffer, offset, count - offset);
                if (read <= 0) throw new EndOfStreamException();
                offset += read;
            }
        }
    }
}
