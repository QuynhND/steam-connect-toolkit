using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace SteamConnectToolkit.Services
{
    /// <summary>Thông tin ứng dụng và hệ thống cho mục Giới thiệu / gói chẩn đoán.</summary>
    public static class AppInfo
    {
        public const string Name = "Steam Connect Toolkit";
        public const string Author = "Quynh Nguyen";
        public const string License = "MIT License";
        public const string Copyright = "© 2026 Quynh Nguyen";
        public const string RepositoryUrl = "https://github.com/QuynhND/steam-connect-toolkit";
        public const string ReleasesUrl = RepositoryUrl + "/releases";
        public const string Description =
            "Kiểm tra kết nối Steam (DNS + HTTPS), đổi DNS IPv4/IPv6 sang DNS công cộng có sao lưu và thu hồi, " +
            "kết nối VPN tích hợp Windows (IKEv2, L2TP/IPsec, SSTP, PPTP) và VPN Gate miễn phí. Dành cho Windows 10/11, không cần cài runtime.";
        public const string Credits =
            "Nguồn dữ liệu: VPN Gate (Đại học Tsukuba, Nhật Bản), public-dns.info, các nhà cung cấp DNS công cộng. " +
            "Mã nguồn và icon phát hành theo giấy phép MIT.";

        public static string Version
        {
            get
            {
                try
                {
                    var asm = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;
                    var info = asm.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false) as AssemblyInformationalVersionAttribute[];
                    if (info != null && info.Length > 0 && !string.IsNullOrEmpty(info[0].InformationalVersion)) return info[0].InformationalVersion;
                    return asm.GetName().Version.ToString(3);
                }
                catch (Exception)
                {
                    return "?";
                }
            }
        }

        public static string ExePath
        {
            get
            {
                try { return Process.GetCurrentProcess().MainModule.FileName; }
                catch (Exception) { return ""; }
            }
        }

        public static string ExeDirectory
        {
            get
            {
                try { return Path.GetDirectoryName(ExePath) ?? ""; }
                catch (Exception) { return ""; }
            }
        }

        public static string BuildDate
        {
            get
            {
                try { return File.GetLastWriteTime(ExePath).ToString("yyyy-MM-dd HH:mm"); }
                catch (Exception) { return "?"; }
            }
        }

        /// <summary>Tên Windows thật (Windows 11 vẫn ghi ProductName là "Windows 10" trong registry → sửa theo số build).</summary>
        public static string WindowsVersion()
        {
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false))
                {
                    if (key == null) return Environment.OSVersion.ToString();
                    string product = (key.GetValue("ProductName") as string) ?? "Windows";
                    string display = (key.GetValue("DisplayVersion") as string) ?? (key.GetValue("ReleaseId") as string) ?? "";
                    string build = (key.GetValue("CurrentBuild") as string) ?? "";
                    object ubr = key.GetValue("UBR");
                    int buildNum;
                    if (int.TryParse(build, out buildNum) && buildNum >= 22000 && product.StartsWith("Windows 10")) product = "Windows 11" + product.Substring("Windows 10".Length);
                    return product + (display.Length > 0 ? " " + display : "") + " (build " + build + (ubr != null ? "." + ubr : "") + ")";
                }
            }
            catch (Exception)
            {
                return Environment.OSVersion.ToString();
            }
        }

        public static string DotNetVersion()
        {
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", false))
                {
                    object v = key != null ? key.GetValue("Release") : null;
                    if (v is int)
                    {
                        int r = (int)v;
                        string name = r >= 533320 ? "4.8.1" : r >= 528040 ? "4.8" : r >= 461808 ? "4.7.2" : r >= 461308 ? "4.7.1" : r >= 460798 ? "4.7"
                            : r >= 394802 ? "4.6.2" : r >= 394254 ? "4.6.1" : r >= 393295 ? "4.6" : r >= 379893 ? "4.5.2" : "4.5";
                        return ".NET Framework " + name + " (release " + r + ")";
                    }
                }
            }
            catch (Exception) { }
            return "CLR " + Environment.Version;
        }

        /// <summary>Tóm tắt ngắn cho mục Giới thiệu (3 dòng).</summary>
        public static string SystemSummaryShort()
        {
            var nat = VpnService.GetL2tpNatRule();
            return WindowsVersion() + " · " + DotNetVersion() + " · Quyền Administrator: " + (AdminHelper.IsAdministrator() ? "có" : "không") + Environment.NewLine
                 + "File chạy: " + ExePath + " (build " + BuildDate + ")" + Environment.NewLine
                 + "Dữ liệu: " + AppPaths.DataDir + " · Khoá L2TP sau NAT: " + (nat.HasValue ? nat.Value.ToString() : "chưa đặt");
        }

        /// <summary>Thông tin đầy đủ cho gói chẩn đoán / sao chép.</summary>
        public static string SystemSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine(Name + " " + Version + " – " + Author + " – " + License);
            sb.AppendLine("Trang dự án: " + RepositoryUrl);
            sb.AppendLine("Thời điểm: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("File chạy: " + ExePath);
            sb.AppendLine("Build: " + BuildDate);
            sb.AppendLine("Thư mục dữ liệu: " + AppPaths.DataDir);
            sb.AppendLine("Windows: " + WindowsVersion() + " (" + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit") + ")");
            sb.AppendLine("Runtime: " + DotNetVersion());
            sb.AppendLine("Quyền Administrator: " + (AdminHelper.IsAdministrator() ? "có" : "không"));
            sb.AppendLine("Máy / người dùng: " + Environment.MachineName + " / " + Environment.UserName);
            var nat = VpnService.GetL2tpNatRule();
            sb.AppendLine("AssumeUDPEncapsulationContextOnSendRule: " + (nat.HasValue ? nat.Value.ToString() : "chưa đặt"));
            try
            {
                var s = SessionService.Load();
                sb.AppendLine("Phiên: pid " + s.Pid + ", bắt đầu " + s.StartedAt + ", sạch=" + s.CleanExit + ", giữ=" + s.KeepOnExit
                              + ", VPN do app kết nối: " + (s.VpnConnectedByApp.Count > 0 ? string.Join(", ", s.VpnConnectedByApp) : "(không)"));
            }
            catch (Exception) { }
            try
            {
                var backup = BackupService.Load();
                sb.AppendLine("Sao lưu DNS gốc: " + (backup.Adapters.Count == 0 ? "(không)" : string.Join("; ", backup.Adapters.Select(a => a.AdapterName + ": " + a.Describe()))));
            }
            catch (Exception) { }
            try
            {
                sb.AppendLine("Card mạng:");
                foreach (var a in NetworkAdapterService.GetAdapters(true))
                    sb.AppendLine("  " + (a.Recommended ? "[*] " : "[ ] ") + a + " | " + a.SourceText() + " | IPv4: " + a.Ipv4DnsText() + " | IPv6: " + a.Ipv6DnsText());
            }
            catch (Exception) { }
            return sb.ToString();
        }
    }
}
