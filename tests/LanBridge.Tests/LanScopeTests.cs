using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using LanBridge.Core;
using Xunit;

namespace LanBridge.Tests
{
    public sealed class LanScopeTests
    {
        [Fact]
        public void OnlySamePhysicalSubnetIsEligible()
        {
            var scope = new LanScope(new[]
            {
                new LanInterface(
                    "Wi-Fi",
                    IPAddress.Parse("192.168.10.5"),
                    IPAddress.Parse("255.255.255.0"),
                    IPAddress.Parse("192.168.10.255"))
            });

            Assert.True(scope.IsEligiblePeer(IPAddress.Parse("192.168.10.99")));
            Assert.False(scope.IsEligiblePeer(IPAddress.Parse("192.168.11.5")));
            Assert.False(scope.IsEligiblePeer(IPAddress.Parse("8.8.8.8")));
            Assert.False(scope.IsEligiblePeer(IPAddress.Parse("172.20.128.10")));
            Assert.False(scope.IsEligiblePeer(IPAddress.Parse("127.0.0.1")));
        }

        [Theory]
        [InlineData("Hyper-V Virtual Ethernet Adapter", NetworkInterfaceType.Ethernet, true)]
        [InlineData("Bluetooth Network Connection", NetworkInterfaceType.Ethernet, true)]
        [InlineData("Tailscale Tunnel", NetworkInterfaceType.Ethernet, true)]
        [InlineData("Intel(R) Wi-Fi 6E AX211", NetworkInterfaceType.Wireless80211, false)]
        [InlineData("Realtek PCIe GbE Family Controller", NetworkInterfaceType.Ethernet, false)]
        public void VirtualInterfaceDetection_IsCaseInsensitive(
            string description,
            NetworkInterfaceType type,
            bool expected)
        {
            Assert.Equal(expected, LanScope.IsVirtualInterface(description, type));
        }

        [Fact]
        public void Capture_ReturnsOnlyPhysicalPrivateInterfaces()
        {
            var scope = LanScope.Capture();
            Assert.NotNull(scope.Interfaces);
            Assert.All(scope.Interfaces, i =>
            {
                Assert.Equal(System.Net.Sockets.AddressFamily.InterNetwork, i.Address.AddressFamily);
                Assert.False(LanScope.IsVirtualInterface(i.Name, NetworkInterfaceType.Ethernet));
                var bytes = i.Address.GetAddressBytes();
                Assert.True(
                    bytes[0] == 10 ||
                    (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                    (bytes[0] == 192 && bytes[1] == 168));
            });
        }
    }
}
