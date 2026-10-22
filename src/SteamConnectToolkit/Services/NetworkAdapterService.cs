using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;
using SteamConnectToolkit.Models;

namespace SteamConnectToolkit.Services
{
    public class AdapterInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public NetworkInterfaceType Type { get; set; }
        public bool IsUp { get; set; }
        public bool HasGateway { get; set; }
        public bool HasGlobalIpv6 { get; set; }
        public bool Recommended { get; set; }
        public bool Ipv4Static { get; set; }
        public bool Ipv6Static { get; set; }
        public List<string> EffectiveIpv4Dns { get; set; }
        public List<string> EffectiveIpv6Dns { get; set; }

        public AdapterInfo()
        {
            EffectiveIpv4Dns = new List<string>();
            EffectiveIpv6Dns = new List<string>();
        }

        public string TypeText()
        {
            switch (Type)
            {
                case NetworkInterfaceType.Wireless80211: return "Wi-Fi";
                case NetworkInterfaceType.Ethernet:
                case NetworkInterfaceType.GigabitEthernet:
                case NetworkInterfaceType.FastEthernetT:
                case NetworkInterfaceType.FastEthernetFx: return "Ethernet";
                case NetworkInterfaceType.Ppp: return "PPP/VPN";
                case NetworkInterfaceType.Wwanpp:
                case NetworkInterfaceType.Wwanpp2: return "Di động";
                default:
                    {
                        string t = Type.ToString();
                        int n;
                        return int.TryParse(t, out n) ? "Ảo/khác" : t;
                    }
            }
        }

        public string Ipv4DnsText() { return EffectiveIpv4Dns.Count > 0 ? string.Join(", ", EffectiveIpv4Dns) : "(không có)"; }
        public string Ipv6DnsText() { return EffectiveIpv6Dns.Count > 0 ? string.Join(", ", EffectiveIpv6Dns) : "(không có)"; }

        public string SourceText()
        {
            return "IPv4 " + (Ipv4Static ? "tĩnh" : "tự động") + " / IPv6 " + (Ipv6Static ? "tĩnh" : "tự động");
        }

        public override string ToString()
        {
            string d = Description ?? "";
            if (d.Length > 40) d = d.Substring(0, 38) + "…";
            return Name + " (" + TypeText() + (IsUp ? "" : ", tắt") + (HasGateway ? "" : ", không gateway") + ") – " + d;
        }
    }

    /// <summary>Liệt kê card mạng và đọc trạng thái DNS (tĩnh/tự động) từ registry.</summary>
    public static class NetworkAdapterService
    {
        private const string Tcpip4Key = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\";
        private const string Tcpip6Key = @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters\Interfaces\";

        private static readonly string[] VirtualHints =
        {
            "virtual", "vmware", "virtualbox", "hyper-v", "vethernet", "tap-", "tap ", "wintun", "wireguard",
            "tailscale", "zerotier", "hamachi", "radmin", "bluetooth", "wan miniport", "npcap", "loopback", "docker", "wsl"
        };

        public static List<AdapterInfo> GetAdapters(bool includeDown)
        {
            var list = new List<AdapterInfo>();
            NetworkInterface[] all;
            try { all = NetworkInterface.GetAllNetworkInterfaces(); }
            catch (Exception) { return list; }

            foreach (var ni in all)
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                bool up = ni.OperationalStatus == OperationalStatus.Up;
                if (!up && !includeDown) continue;

                var a = new AdapterInfo
                {
                    Id = ni.Id,
                    Name = ni.Name,
                    Description = ni.Description,
                    Type = ni.NetworkInterfaceType,
                    IsUp = up
                };
                try
                {
                    var props = ni.GetIPProperties();
                    a.HasGateway = props.GatewayAddresses.Any(g => g.Address != null
                        && !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any));
                    foreach (var d in props.DnsAddresses)
                    {
                        if (d.AddressFamily == AddressFamily.InterNetwork) a.EffectiveIpv4Dns.Add(d.ToString());
                        else if (d.AddressFamily == AddressFamily.InterNetworkV6 && !d.IsIPv6SiteLocal) a.EffectiveIpv6Dns.Add(d.ToString());
                    }
                    a.HasGlobalIpv6 = props.UnicastAddresses.Any(u => u.Address.AddressFamily == AddressFamily.InterNetworkV6
                        && !u.Address.IsIPv6LinkLocal && !u.Address.IsIPv6SiteLocal && !IsUniqueLocal(u.Address));
                }
                catch (Exception) { }

                var st = ReadRegistryState(a.Id);
                a.Ipv4Static = st.Ipv4Static;
                a.Ipv6Static = st.Ipv6Static;

                string desc = (ni.Description ?? "") + " " + (ni.Name ?? "");
                string lower = desc.ToLowerInvariant();
                bool looksVirtual = VirtualHints.Any(h => lower.Contains(h));
                a.Recommended = up && a.HasGateway && !looksVirtual;
                list.Add(a);
            }
            return list.OrderByDescending(x => x.Recommended).ThenByDescending(x => x.IsUp).ThenBy(x => x.Name).ToList();
        }

        public static bool AnyGlobalIpv6()
        {
            try { return GetAdapters(false).Any(a => a.HasGlobalIpv6); }
            catch (Exception) { return false; }
        }

        /// <summary>Chụp cấu hình DNS hiện tại của card để có thể thu hồi sau này.</summary>
        public static AdapterDnsState CaptureState(AdapterInfo a)
        {
            var s = ReadRegistryState(a.Id);
            s.AdapterName = a.Name;
            s.AdapterDescription = a.Description;
            s.EffectiveDns = a.EffectiveIpv4Dns.Concat(a.EffectiveIpv6Dns).ToList();
            s.CapturedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            return s;
        }

        /// <summary>Hook kiểm thử: thay việc đọc registry thật (adapterId → trạng thái).</summary>
        public static Func<string, AdapterDnsState> StateReaderOverride;

        public static AdapterDnsState ReadRegistryState(string adapterId)
        {
            var reader = StateReaderOverride;
            if (reader != null)
            {
                var r = reader(adapterId);
                if (r != null) return r;
            }
            var s = new AdapterDnsState { AdapterId = adapterId };
            var v4 = ReadNameServers(Tcpip4Key + adapterId);
            s.Ipv4Static = v4.Count > 0;
            s.Ipv4Servers = v4;
            var v6 = ReadNameServers(Tcpip6Key + adapterId);
            s.Ipv6Static = v6.Count > 0;
            s.Ipv6Servers = v6;
            return s;
        }

        private static List<string> ReadNameServers(string subKey)
        {
            var list = new List<string>();
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = hklm.OpenSubKey(subKey, false))
                {
                    if (key == null) return list;
                    var val = key.GetValue("NameServer") as string;
                    if (string.IsNullOrWhiteSpace(val)) return list;
                    foreach (var part in val.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        IPAddress ip;
                        if (IPAddress.TryParse(part.Trim(), out ip)) list.Add(ip.ToString());
                    }
                }
            }
            catch (Exception) { }
            return list;
        }

        private static bool IsUniqueLocal(IPAddress ip)
        {
            var b = ip.GetAddressBytes();
            return b.Length == 16 && (b[0] & 0xFE) == 0xFC; // fc00::/7
        }
    }
}
