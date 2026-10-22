using System;
using System.Collections.Generic;

namespace SteamConnectToolkit.Models
{
    /// <summary>Ảnh chụp cấu hình DNS của một card mạng (để thu hồi).</summary>
    public class AdapterDnsState
    {
        public string AdapterId { get; set; }
        public string AdapterName { get; set; }
        public string AdapterDescription { get; set; }

        /// <summary>true = DNS IPv4 đặt tĩnh; false = nhận tự động (DHCP).</summary>
        public bool Ipv4Static { get; set; }
        public List<string> Ipv4Servers { get; set; }
        /// <summary>true = DNS IPv6 đặt tĩnh; false = tự động.</summary>
        public bool Ipv6Static { get; set; }
        public List<string> Ipv6Servers { get; set; }

        /// <summary>DNS đang có hiệu lực tại thời điểm chụp (tham khảo).</summary>
        public List<string> EffectiveDns { get; set; }
        public string CapturedAt { get; set; }

        public AdapterDnsState()
        {
            Ipv4Servers = new List<string>();
            Ipv6Servers = new List<string>();
            EffectiveDns = new List<string>();
        }

        public string Describe()
        {
            string v4 = Ipv4Static ? "tĩnh " + (Ipv4Servers.Count > 0 ? string.Join(", ", Ipv4Servers) : "(trống)") : "tự động (DHCP)";
            string v6 = Ipv6Static ? "tĩnh " + (Ipv6Servers.Count > 0 ? string.Join(", ", Ipv6Servers) : "(trống)") : "tự động";
            return "IPv4: " + v4 + " | IPv6: " + v6;
        }
    }

    public class BackupData
    {
        public List<AdapterDnsState> Adapters { get; set; }
        public string CreatedAt { get; set; }

        public BackupData()
        {
            Adapters = new List<AdapterDnsState>();
        }
    }
}
