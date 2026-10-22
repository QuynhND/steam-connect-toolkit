using System;
using System.Collections.Generic;
using System.Globalization;

namespace SteamConnectToolkit.Models
{
    public class AppConfig
    {
        public int Version { get; set; }
        public List<DnsProvider> Providers { get; set; }
        public string LastProviderId { get; set; }

        public int DnsTimeoutMs { get; set; }
        public int HttpsTimeoutMs { get; set; }
        public int Concurrency { get; set; }

        public bool AutoDisableFailed { get; set; }
        public string DiscoveryCountry { get; set; }
        public int DiscoveryMaxServers { get; set; }
        public double DiscoveryMinReliability { get; set; }

        /// <summary>Tài khoản VPN đã lưu (mật khẩu mã hoá DPAPI theo tài khoản Windows).</summary>
        public List<VpnCredential> VpnCredentials { get; set; }
        public string LastVpnProfile { get; set; }

        /// <summary>Cấu hình VPN do app tạo (để dọn khi khôi phục).</summary>
        public List<VpnProfileRecord> VpnProfilesCreatedByApp { get; set; }
        /// <summary>true = khi thoát app KHÔNG tự khôi phục DNS/VPN (người dùng chủ động chọn).</summary>
        public bool KeepChangesOnExit { get; set; }

        /// <summary>Hiện hướng dẫn cho người dùng lần đầu khi mở app (tắt được trong Cài đặt hoặc ngay trong hướng dẫn).</summary>
        public bool ShowGuideOnStartup { get; set; }
        /// <summary>Ưu tiên SSTP khi dùng VPN Gate.</summary>
        public bool VpnGatePreferSstp { get; set; }
        /// <summary>Tự động kiểm tra kết nối Steam ngay khi mở app.</summary>
        public bool AutoCheckOnStartup { get; set; }

        // ---- Cập nhật từ xa ----
        /// <summary>Địa chỉ https của latest.json; rỗng = tắt cập nhật.</summary>
        public string UpdateManifestUrl { get; set; }
        public bool UpdateCheckOnStartup { get; set; }
        public string LastUpdateCheck { get; set; }
        /// <summary>Vân tay chứng chỉ ký file của nhà phát hành (tuỳ chọn); có đặt thì gói tải về phải được ký đúng.</summary>
        public string UpdateSignerThumbprint { get; set; }
        public string SkippedUpdateVersion { get; set; }

        public AppConfig()
        {
            Version = 2; // tăng khi đổi mặc định cần di trú (xem ProviderCatalog.Migrate)
            Providers = new List<DnsProvider>();
            VpnCredentials = new List<VpnCredential>();
            VpnProfilesCreatedByApp = new List<VpnProfileRecord>();
            KeepChangesOnExit = false;
            ShowGuideOnStartup = true;
            VpnGatePreferSstp = true;
            AutoCheckOnStartup = false;
            UpdateManifestUrl = "";
            UpdateCheckOnStartup = true;
            UpdateSignerThumbprint = "";
            DnsTimeoutMs = 2500;
            HttpsTimeoutMs = 6000;
            Concurrency = 4; // 4 DNS x 5 tên miền = 20 kết nối song song, tránh timeout giả do nghẽn
            AutoDisableFailed = true;
            DiscoveryMaxServers = 40;
            DiscoveryMinReliability = 0.95;
            try { DiscoveryCountry = RegionInfo.CurrentRegion.TwoLetterISORegionName; }
            catch (Exception) { DiscoveryCountry = "VN"; }
        }

        /// <summary>Đưa các tuỳ chọn (không đụng danh sách DNS, tài khoản VPN, lịch sử) về mặc định.</summary>
        public void ResetOptions()
        {
            var d = new AppConfig();
            DnsTimeoutMs = d.DnsTimeoutMs;
            HttpsTimeoutMs = d.HttpsTimeoutMs;
            Concurrency = d.Concurrency;
            AutoDisableFailed = d.AutoDisableFailed;
            DiscoveryCountry = d.DiscoveryCountry;
            DiscoveryMaxServers = d.DiscoveryMaxServers;
            DiscoveryMinReliability = d.DiscoveryMinReliability;
            KeepChangesOnExit = d.KeepChangesOnExit;
            ShowGuideOnStartup = d.ShowGuideOnStartup;
            VpnGatePreferSstp = d.VpnGatePreferSstp;
            AutoCheckOnStartup = d.AutoCheckOnStartup;
            UpdateCheckOnStartup = d.UpdateCheckOnStartup;
            SkippedUpdateVersion = null;
        }

        /// <summary>Đảm bảo các giá trị hợp lệ sau khi đọc từ file.</summary>
        public void Normalize()
        {
            if (Providers == null) Providers = new List<DnsProvider>();
            if (VpnCredentials == null) VpnCredentials = new List<VpnCredential>();
            if (VpnProfilesCreatedByApp == null) VpnProfilesCreatedByApp = new List<VpnProfileRecord>();
            if (UpdateManifestUrl == null) UpdateManifestUrl = "";
            if (UpdateSignerThumbprint == null) UpdateSignerThumbprint = "";
            if (DnsTimeoutMs < 500) DnsTimeoutMs = 2500;
            if (HttpsTimeoutMs < 1000) HttpsTimeoutMs = 6000;
            if (Concurrency < 1 || Concurrency > 32) Concurrency = 4;
            if (DiscoveryMaxServers < 1) DiscoveryMaxServers = 40;
            if (DiscoveryMinReliability <= 0 || DiscoveryMinReliability > 1) DiscoveryMinReliability = 0.95;
            if (string.IsNullOrWhiteSpace(DiscoveryCountry)) DiscoveryCountry = "VN";
        }
    }
}
