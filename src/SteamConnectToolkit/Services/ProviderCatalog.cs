using System;
using System.Collections.Generic;
using System.Linq;
using SteamConnectToolkit.Models;

namespace SteamConnectToolkit.Services
{
    /// <summary>Danh mục DNS công cộng có sẵn và đọc/ghi cấu hình người dùng.</summary>
    public static class ProviderCatalog
    {
        public static List<DnsProvider> BuiltIn()
        {
            var l = new List<DnsProvider>();

            // ===== Danh sách chính (được bật sẵn) =====
            l.Add(Make("google", "Google Public DNS", "8.8.8.8", "8.8.4.4",
                "2001:4860:4860::8888", "2001:4860:4860::8844", "core", "Phổ biến, nhanh, không lọc nội dung."));
            l.Add(Make("cloudflare", "Cloudflare DNS", "1.1.1.1", "1.0.0.1",
                "2606:4700:4700::1111", "2606:4700:4700::1001", "core", "Rất nhanh, chú trọng riêng tư, không lọc."));
            l.Add(Make("quad9", "Quad9", "9.9.9.9", "149.112.112.112",
                "2620:fe::fe", "2620:fe::9", "core", "Chặn tên miền mã độc, DNSSEC."));
            l.Add(Make("quad9-unfiltered", "Quad9 Unfiltered", "9.9.9.10", "149.112.112.10",
                "2620:fe::10", "2620:fe::fe:10", "core", "Quad9 không lọc, không DNSSEC."));
            l.Add(Make("adguard", "AdGuard DNS", "94.140.14.14", "94.140.15.15",
                "2a10:50c0::ad1:ff", "2a10:50c0::ad2:ff", "core", "Chặn quảng cáo và theo dõi."));
            l.Add(Make("verisign", "Verisign Public DNS", "64.6.64.6", "64.6.65.6",
                "2620:74:1b::1:1", "2620:74:1c::2:2", "core", "Nay do UltraDNS (Neustar/Vercara) vận hành, không lọc."));
            l.Add(Make("opendns", "OpenDNS", "208.67.222.222", "208.67.220.220",
                "2620:119:35::35", "2620:119:53::53", "core", "Cisco OpenDNS Home, chặn phishing."));
            l.Add(Make("cleanbrowsing-security", "CleanBrowsing Security Filter", "185.228.168.9", "185.228.169.9",
                "2a0d:2a00:1::2", "2a0d:2a00:2::2", "core", "Chặn mã độc, phishing, spam."));
            l.Add(Make("controld-malware", "Control D Malware Blocking", "76.76.2.1", "76.76.10.1",
                "2606:1a40::1", "2606:1a40:1::1", "core", "Control D free resolver p1: chặn mã độc."));
            l.Add(Make("dns4eu-protective", "DNS4EU Protective", "86.54.11.1", "86.54.11.201",
                "2a13:1001::86:54:11:1", "2a13:1001::86:54:11:201", "core", "Dự án DNS công cộng của EU, chặn mã độc."));
            l.Add(Make("ultradns", "UltraDNS Public (Neustar)", "156.154.70.1", "156.154.71.1",
                "2610:a1:1018::1", "2610:a1:1019::1", "core", "UltraDNS Public 'Reliability & Performance', không lọc."));

            // ===== Danh sách bổ sung (tắt sẵn, bật khi kiểm tra thấy tốt) =====
            l.Add(Make("cloudflare-malware", "Cloudflare for Families (chặn mã độc)", "1.1.1.2", "1.0.0.2",
                "2606:4700:4700::1112", "2606:4700:4700::1002", "extra", ""));
            l.Add(Make("cloudflare-family", "Cloudflare for Families (mã độc + người lớn)", "1.1.1.3", "1.0.0.3",
                "2606:4700:4700::1113", "2606:4700:4700::1003", "extra", ""));
            l.Add(Make("quad9-ecs", "Quad9 ECS", "9.9.9.11", "149.112.112.11",
                "2620:fe::11", "2620:fe::fe:11", "extra", "Chặn mã độc, hỗ trợ ECS (định vị CDN tốt hơn)."));
            l.Add(Make("adguard-unfiltered", "AdGuard DNS Unfiltered", "94.140.14.140", "94.140.14.141",
                "2a10:50c0::1:ff", "2a10:50c0::2:ff", "extra", ""));
            l.Add(Make("adguard-family", "AdGuard DNS Family", "94.140.14.15", "94.140.15.16",
                "2a10:50c0::bad1:ff", "2a10:50c0::bad2:ff", "extra", "Chặn quảng cáo + nội dung người lớn."));
            l.Add(Make("opendns-familyshield", "OpenDNS FamilyShield", "208.67.222.123", "208.67.220.123",
                "2620:119:35::123", "2620:119:53::123", "extra", "Chặn nội dung người lớn."));
            l.Add(Make("cleanbrowsing-adult", "CleanBrowsing Adult Filter", "185.228.168.10", "185.228.169.11",
                "2a0d:2a00:1::1", "2a0d:2a00:2::1", "extra", ""));
            l.Add(Make("cleanbrowsing-family", "CleanBrowsing Family Filter", "185.228.168.168", "185.228.169.168",
                "2a0d:2a00:1::", "2a0d:2a00:2::", "extra", ""));
            l.Add(Make("controld-unfiltered", "Control D Unfiltered", "76.76.2.0", "76.76.10.0",
                "2606:1a40::", "2606:1a40:1::", "extra", "Control D free resolver p0."));
            l.Add(Make("controld-ads", "Control D Ads + Malware", "76.76.2.2", "76.76.10.2",
                "2606:1a40::2", "2606:1a40:1::2", "extra", "Control D free resolver p2."));
            l.Add(Make("dns4eu-unfiltered", "DNS4EU Unfiltered", "86.54.11.100", "86.54.11.200",
                "2a13:1001::86:54:11:100", "2a13:1001::86:54:11:200", "extra", ""));
            l.Add(Make("dns4eu-ad", "DNS4EU Protective + chặn quảng cáo", "86.54.11.13", "86.54.11.213",
                "2a13:1001::86:54:11:13", "2a13:1001::86:54:11:213", "extra", ""));
            l.Add(Make("ultradns-threat", "UltraDNS Threat Protection", "156.154.70.2", "156.154.71.2",
                "2610:a1:1018::2", "2610:a1:1019::2", "extra", ""));
            l.Add(Make("comodo", "Comodo Secure DNS", "8.26.56.26", "8.20.247.20",
                null, null, "extra", "Chỉ IPv4."));
            l.Add(Make("yandex", "Yandex DNS Basic", "77.88.8.8", "77.88.8.1",
                "2a02:6b8::feed:0ff", "2a02:6b8:0:1::feed:0ff", "extra", ""));
            l.Add(Make("mullvad", "Mullvad DNS", "194.242.2.2", null,
                "2a07:e340::2", null, "extra", "Không lọc, chú trọng riêng tư."));
            l.Add(Make("dnssb", "DNS.SB", "185.222.222.222", "45.11.45.11",
                "2a09::", "2a11::", "extra", ""));
            l.Add(Make("alternate", "Alternate DNS", "76.76.19.19", "76.223.122.150",
                "2602:fcbc::ad", "2602:fcbc:2::ad", "extra", "Chặn quảng cáo."));
            l.Add(Make("lumen", "Lumen / Level3", "4.2.2.1", "4.2.2.2",
                null, null, "extra", "Chỉ IPv4, không chính thức công bố."));
            l.Add(Make("he", "Hurricane Electric", "74.82.42.42", null,
                "2001:470:20::2", null, "extra", ""));
            l.Add(Make("cira", "CIRA Canadian Shield (Protected)", "149.112.121.20", "149.112.122.20",
                "2620:10a:80bb::20", "2620:10a:80bc::20", "extra", "Có thể chỉ phục vụ tốt ở Canada."));
            l.Add(Make("safedns", "SafeDNS", "195.46.39.39", "195.46.39.40",
                null, null, "extra", "Chỉ IPv4."));
            return l;
        }

        private static DnsProvider Make(string id, string name, string v4a, string v4b, string v6a, string v6b, string category, string notes)
        {
            return new DnsProvider
            {
                Id = id,
                Name = name,
                Ipv4Primary = v4a,
                Ipv4Secondary = v4b,
                Ipv6Primary = v6a,
                Ipv6Secondary = v6b,
                Category = category,
                Notes = notes,
                Enabled = category == "core",
                BuiltIn = true
            };
        }

        /// <summary>Đọc cấu hình, hợp nhất danh sách có sẵn với trạng thái đã lưu và DNS tự thêm.</summary>
        public static AppConfig LoadConfig()
        {
            var cfg = JsonStore.Load<AppConfig>(AppPaths.ConfigFile);
            cfg.Normalize();
            Migrate(cfg);
            var stored = cfg.Providers;
            var merged = new List<DnsProvider>();

            foreach (var b in BuiltIn())
            {
                var s = stored.FirstOrDefault(x => x.Id == b.Id);
                if (s != null)
                {
                    b.Enabled = s.Enabled;
                    b.LastTestSummary = s.LastTestSummary;
                    b.LastScore = s.LastScore;
                    b.LastLatencyMs = s.LastLatencyMs;
                    b.LastTestedAt = s.LastTestedAt;
                }
                merged.Add(b);
            }
            foreach (var s in stored)
            {
                if (s.BuiltIn) continue;
                if (string.IsNullOrEmpty(s.Id)) s.Id = NewId("custom");
                if (merged.Any(m => m.Id == s.Id)) continue;
                if (string.IsNullOrEmpty(s.Category)) s.Category = "custom";
                merged.Add(s);
            }
            cfg.Providers = merged;
            return cfg;
        }

        public static void SaveConfig(AppConfig cfg)
        {
            JsonStore.Save(AppPaths.ConfigFile, cfg);
        }

        /// <summary>Nâng cấp cấu hình của phiên bản cũ lên giá trị mặc định mới (chỉ các tuỳ chọn hiệu năng).</summary>
        public static void Migrate(AppConfig cfg)
        {
            if (cfg.Version < 2)
            {
                // v1: 6 luồng / HTTPS 5 s gây timeout giả khi nhiều kết nối song song.
                cfg.Concurrency = 4;
                if (cfg.HttpsTimeoutMs < 6000) cfg.HttpsTimeoutMs = 6000;
                cfg.Version = 2;
            }
        }

        public static string NewId(string prefix)
        {
            return prefix + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        /// <summary>Bật lại toàn bộ DNS chính, tắt DNS bổ sung, giữ nguyên DNS tự thêm.</summary>
        public static void ResetBuiltIns(AppConfig cfg)
        {
            foreach (var p in cfg.Providers.Where(x => x.BuiltIn))
            {
                p.Enabled = p.Category == "core";
            }
        }
    }
}
