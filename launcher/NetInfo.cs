using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CoopLauncher
{
    /// <summary>The host's own addresses (LAN and VPNs) and UPnP port forwarding on the router.</summary>
    public static class NetInfo
    {
        public sealed class Address
        {
            public string Kind, Ip;
            public override string ToString() => $"{Kind} {Ip}";
        }

        public static List<Address> LocalAddresses()
        {
            var list = new List<Address>();
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var name = (ni.Name + " " + ni.Description).ToLowerInvariant();
                string kind =
                    name.Contains("radmin") ? "Radmin VPN" :
                    name.Contains("zerotier") ? "ZeroTier" :
                    name.Contains("tailscale") ? "Tailscale" :
                    name.Contains("hamachi") ? "Hamachi" :
                    name.Contains("virtual") || name.Contains("vmware") || name.Contains("hyper-v") || name.Contains("vethernet") || name.Contains("wsl") ? null :
                    "LAN";
                if (kind == null) continue;
                foreach (var a in ni.GetIPProperties().UnicastAddresses)
                {
                    if (a.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var ip = a.Address.ToString();
                    if (ip.StartsWith("169.254.")) continue; // no DHCP lease
                    list.Add(new Address { Kind = kind, Ip = ip });
                }
            }
            // VPN addresses first: those are the ones friends over the internet need
            return list.OrderBy(a => a.Kind == "LAN" ? 1 : 0).ToList();
        }

        const string UpnpDescription = "Anomaly Co-op";

        /// <summary>Ask the router to forward TCP port to this PC. Returns the external IP or null.</summary>
        public static string UpnpMap(int port, string localIp, out string message)
        {
            try
            {
                dynamic mappings = UpnpCollection();
                if (mappings == null)
                {
                    message = "роутер не поддерживает UPnP или он выключен";
                    return null;
                }
                try { mappings.Remove(port, "TCP"); } catch { }
                dynamic m = mappings.Add(port, "TCP", port, localIp, true, UpnpDescription);
                string external = m?.ExternalIPAddress;
                message = "порт открыт через UPnP";
                return external;
            }
            catch (Exception e)
            {
                message = "UPnP не сработал: " + e.Message;
                return null;
            }
        }

        public static void UpnpUnmap(int port)
        {
            try
            {
                dynamic mappings = UpnpCollection();
                if (mappings == null) return;
                dynamic m = mappings[port, "TCP"];
                if (m != null && (string)m.Description == UpnpDescription) mappings.Remove(port, "TCP");
            }
            catch { }
        }

        static object UpnpCollection()
        {
            var t = Type.GetTypeFromProgID("HNetCfg.NATUPnP");
            if (t == null) return null;
            dynamic nat = Activator.CreateInstance(t);
            return nat.StaticPortMappingCollection;
        }

        /// <summary>Best LAN address to forward the port to.</summary>
        public static string PrimaryLanIp()
        {
            try
            {
                using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    s.Connect("8.8.8.8", 53); // no packet is sent for UDP connect; it only picks the route
                    return ((IPEndPoint)s.LocalEndPoint).Address.ToString();
                }
            }
            catch { return LocalAddresses().FirstOrDefault(a => a.Kind == "LAN")?.Ip; }
        }
    }
}
