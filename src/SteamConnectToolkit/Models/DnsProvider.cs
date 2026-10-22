using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace SteamConnectToolkit.Models
{
    /// <summary>Một nhà cung cấp DNS công cộng (IPv4 + IPv6).</summary>
    public class DnsProvider
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Ipv4Primary { get; set; }
        public string Ipv4Secondary { get; set; }
        public string Ipv6Primary { get; set; }
        public string Ipv6Secondary { get; set; }
        /// <summary>core | extra | custom | discovered</summary>
        public string Category { get; set; }
        public string Notes { get; set; }
        public bool Enabled { get; set; }
        public bool BuiltIn { get; set; }

        // Kết quả lần kiểm tra gần nhất (lưu lại để hiển thị)
        public string LastTestSummary { get; set; }
        public double LastScore { get; set; }
        public double LastLatencyMs { get; set; }
        public string LastTestedAt { get; set; }

        public DnsProvider()
        {
            Category = "custom";
            Enabled = true;
        }

        public List<string> GetIpv4Servers() { return NonEmpty(Ipv4Primary, Ipv4Secondary); }
        public List<string> GetIpv6Servers() { return NonEmpty(Ipv6Primary, Ipv6Secondary); }
        public bool HasIpv4() { return GetIpv4Servers().Count > 0; }
        public bool HasIpv6() { return GetIpv6Servers().Count > 0; }
        public string Ipv4Text() { return HasIpv4() ? string.Join(", ", GetIpv4Servers()) : "(không có)"; }
        public string Ipv6Text() { return HasIpv6() ? string.Join(", ", GetIpv6Servers()) : "(không có)"; }

        public string DisplayName()
        {
            string ip = HasIpv4() ? Ipv4Primary : Ipv6Primary;
            return Name + "  [" + ip + "]";
        }

        public string CategoryText()
        {
            switch (Category)
            {
                case "core": return "Chính";
                case "extra": return "Bổ sung";
                case "discovered": return "Tìm thấy";
                default: return "Tự thêm";
            }
        }

        public override string ToString() { return DisplayName(); }

        private static List<string> NonEmpty(params string[] values)
        {
            var list = new List<string>();
            foreach (var v in values)
            {
                if (!string.IsNullOrWhiteSpace(v)) list.Add(v.Trim());
            }
            return list;
        }

        public static bool IsValidIp(string s, AddressFamily family)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            IPAddress ip;
            if (!IPAddress.TryParse(s, out ip)) return false;
            if (ip.AddressFamily != family) return false;
            if (family == AddressFamily.InterNetwork)
            {
                // IPAddress.TryParse chấp nhận dạng rút gọn kiểu inet_aton ("8.8.8" → 8.8.0.8, "1" → 0.0.0.1).
                // Bắt buộc đúng 4 octet thập phân và dạng chuẩn để người dùng không nhập nhầm.
                var parts = s.Split('.');
                if (parts.Length != 4) return false;
                foreach (var part in parts)
                {
                    int n;
                    if (part.Length == 0 || part.Length > 3 || !int.TryParse(part, out n) || n < 0 || n > 255) return false;
                }
                return ip.ToString() == s;
            }
            return true;
        }

        /// <summary>Trả về thông báo lỗi, hoặc null nếu hợp lệ.</summary>
        public string Validate()
        {
            if (string.IsNullOrWhiteSpace(Name)) return "Tên không được để trống.";
            if (!HasIpv4() && !HasIpv6()) return "Cần ít nhất một địa chỉ IPv4 hoặc IPv6.";
            foreach (var v in GetIpv4Servers())
                if (!IsValidIp(v, AddressFamily.InterNetwork)) return "Địa chỉ IPv4 không hợp lệ: " + v;
            foreach (var v in GetIpv6Servers())
                if (!IsValidIp(v, AddressFamily.InterNetworkV6)) return "Địa chỉ IPv6 không hợp lệ: " + v;
            return null;
        }

        public DnsProvider Clone()
        {
            return (DnsProvider)MemberwiseClone();
        }
    }
}
