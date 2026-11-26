using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LanBridge.Core
{
    public sealed class LanInterface
    {
        public string Name { get; }
        public IPAddress Address { get; }
        public IPAddress Mask { get; }
        public IPAddress Broadcast { get; }

        public LanInterface(string name, IPAddress address, IPAddress mask, IPAddress broadcast)
        {
            if (address == null || address.AddressFamily != AddressFamily.InterNetwork)
                throw new ArgumentException("An IPv4 address is required.", nameof(address));
            if (mask == null || mask.AddressFamily != AddressFamily.InterNetwork)
                throw new ArgumentException("An IPv4 mask is required.", nameof(mask));
            if (broadcast == null || broadcast.AddressFamily != AddressFamily.InterNetwork)
                throw new ArgumentException("An IPv4 broadcast address is required.", nameof(broadcast));

            Name = name ?? string.Empty;
            Address = address;
            Mask = mask;
            Broadcast = broadcast;
        }
    }

    public sealed class LanScope
    {
        private readonly List<LanInterface> _interfaces;

        public LanScope(IEnumerable<LanInterface> interfaces)
        {
            _interfaces = interfaces == null ? new List<LanInterface>() : interfaces.ToList();
        }

        public IReadOnlyList<LanInterface> Interfaces => _interfaces.AsReadOnly();

        public static LanScope Capture()
        {
            var result = new List<LanInterface>();

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType != NetworkInterfaceType.Wireless80211 &&
                        nic.NetworkInterfaceType != NetworkInterfaceType.Ethernet) continue;
                    if (IsVirtualInterface(nic.Description, nic.NetworkInterfaceType)) continue;

                    var properties = nic.GetIPProperties();
                    var hasGateway = properties.GatewayAddresses.Any(g =>
                        g.Address != null &&
                        g.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !g.Address.Equals(IPAddress.Any));
                    if (!hasGateway) continue;

                    foreach (var unicast in properties.UnicastAddresses)
                    {
                        var address = unicast.Address;
                        if (address == null || address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (!IsPrivate(address)) continue;

                        var mask = unicast.IPv4Mask;
                        if (mask == null || mask.Equals(IPAddress.Any)) continue;

                        result.Add(new LanInterface(
                            nic.Name,
                            address,
                            mask,
                            ComputeBroadcast(address, mask)));
                    }
                }
                catch (NetworkInformationException)
                {
                }
                catch (PlatformNotSupportedException)
                {
                }
            }

            return new LanScope(result);
        }

        public bool IsEligiblePeer(IPAddress peer)
        {
            if (peer == null || peer.AddressFamily != AddressFamily.InterNetwork) return false;
            if (IPAddress.IsLoopback(peer)) return false;
            if (peer.Equals(IPAddress.Any) || peer.Equals(IPAddress.Broadcast)) return false;

            return _interfaces.Any(iface => IsInSameSubnet(peer, iface.Address, iface.Mask));
        }

        public static bool IsVirtualInterface(string description, NetworkInterfaceType type)
        {
            if (type == NetworkInterfaceType.Tunnel ||
                type == NetworkInterfaceType.Ppp ||
                type == NetworkInterfaceType.Loopback)
                return true;

            if (string.IsNullOrEmpty(description)) return false;

            var value = description.ToLowerInvariant();
            return value.Contains("hyper-v") ||
                   value.Contains("virtual") ||
                   value.Contains("vpn") ||
                   value.Contains("tap") ||
                   value.Contains("tailscale") ||
                   value.Contains("wireguard") ||
                   value.Contains("bluetooth") ||
                   value.Contains("loopback") ||
                   value.Contains("pseudo") ||
                   value.Contains("vmware") ||
                   value.Contains("virtualbox") ||
                   value.Contains("vethernet") ||
                   value.Contains("wsl") ||
                   value.Contains("docker");
        }

        private static bool IsPrivate(IPAddress address)
        {
            var bytes = address.GetAddressBytes();
            if (bytes.Length != 4) return false;

            return bytes[0] == 10 ||
                   (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168);
        }

        private static bool IsInSameSubnet(IPAddress peer, IPAddress address, IPAddress mask)
        {
            var peerBytes = peer.GetAddressBytes();
            var addressBytes = address.GetAddressBytes();
            var maskBytes = mask.GetAddressBytes();
            if (peerBytes.Length != 4 || addressBytes.Length != 4 || maskBytes.Length != 4) return false;

            for (var i = 0; i < 4; i++)
            {
                if ((peerBytes[i] & maskBytes[i]) != (addressBytes[i] & maskBytes[i]))
                    return false;
            }
            return true;
        }

        private static IPAddress ComputeBroadcast(IPAddress address, IPAddress mask)
        {
            var addressBytes = address.GetAddressBytes();
            var maskBytes = mask.GetAddressBytes();
            var broadcast = new byte[4];
            for (var i = 0; i < 4; i++)
            {
                broadcast[i] = (byte)(addressBytes[i] | ~maskBytes[i]);
            }
            return new IPAddress(broadcast);
        }
    }
}
