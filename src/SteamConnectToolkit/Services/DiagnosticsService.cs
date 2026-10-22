using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using SteamConnectToolkit.Models;

namespace SteamConnectToolkit.Services
{
    /// <summary>Xuất/nhập cấu hình và gói chẩn đoán (zip) – luôn lược bỏ mật khẩu VPN.</summary>
    public static class DiagnosticsService
    {
        /// <summary>Bản sao cấu hình không chứa mật khẩu (giữ tên đăng nhập).</summary>
        public static AppConfig Sanitize(AppConfig cfg)
        {
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var copy = ser.Deserialize<AppConfig>(ser.Serialize(cfg)) ?? new AppConfig();
            copy.Normalize();
            foreach (var c in copy.VpnCredentials) c.PasswordProtected = "";
            return copy;
        }

        public static void ExportConfig(string path, AppConfig cfg)
        {
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            File.WriteAllText(path, JsonStore.Pretty(ser.Serialize(Sanitize(cfg))), new UTF8Encoding(false));
        }

        /// <summary>
        /// Nhập cấu hình từ file: kiểm tra hợp lệ, hợp nhất với danh mục DNS có sẵn; giữ tài khoản VPN hiện có nếu file không có.
        /// Ném ngoại lệ với thông báo rõ nếu file không phải cấu hình của app.
        /// </summary>
        public static AppConfig ImportConfig(string path, AppConfig current)
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("File rỗng.");
            AppConfig imported;
            try
            {
                imported = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<AppConfig>(json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("File không phải JSON cấu hình hợp lệ: " + ex.Message);
            }
            if (imported == null || imported.Version <= 0 || imported.Providers == null)
                throw new InvalidOperationException("File không có cấu trúc cấu hình của " + AppInfo.Name + " (thiếu Version/Providers).");
            imported.Normalize();
            foreach (var p in imported.Providers)
            {
                string err = p.Validate();
                if (!string.IsNullOrEmpty(p.Name) && err != null) throw new InvalidOperationException("DNS \"" + p.Name + "\" trong file không hợp lệ: " + err);
            }
            if (current != null && (imported.VpnCredentials == null || imported.VpnCredentials.Count == 0))
                imported.VpnCredentials = current.VpnCredentials.ToList();
            ProviderCatalog.SaveConfig(imported);
            return ProviderCatalog.LoadConfig();
        }

        /// <summary>Tạo gói chẩn đoán zip: thông tin hệ thống, nhật ký, lịch sử, phiên, sao lưu DNS, cấu hình (không mật khẩu), đầu ra netsh.</summary>
        public static List<string> ExportDiagnostics(string zipPath, AppConfig cfg, Action<string> log)
        {
            var entries = new List<string>();
            using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var sys = new StringBuilder(AppInfo.SystemSummary());
                sys.AppendLine();
                sys.AppendLine("=== netsh interface ipv4 show dnsservers ===");
                sys.AppendLine(DnsApplier.RunNetsh("interface ipv4 show dnsservers", null).Output);
                sys.AppendLine("=== netsh interface ipv6 show dnsservers ===");
                sys.AppendLine(DnsApplier.RunNetsh("interface ipv6 show dnsservers", null).Output);
                sys.AppendLine("=== rasdial (kết nối RAS đang mở) ===");
                sys.AppendLine(DnsApplier.Run(Path.Combine(Environment.SystemDirectory, "rasdial.exe"), "", null).Output);
                AddText(zip, "system-info.txt", sys.ToString(), entries);

                var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                AddText(zip, "config.json", JsonStore.Pretty(ser.Serialize(Sanitize(cfg ?? new AppConfig()))), entries);
                AddFile(zip, AppPaths.LogFile, "app.log", entries);
                AddFile(zip, AppPaths.HistoryFile, "history.json", entries);
                AddFile(zip, SessionService.FilePath, "session.json", entries);
                AddFile(zip, AppPaths.BackupFile, "original-dns.json", entries);
                try
                {
                    var profiles = VpnService.ListProfiles(null);
                    AddText(zip, "vpn-profiles.txt", string.Join(Environment.NewLine, profiles.Select(p => p.Name + " | " + p.Server + " | " + p.TunnelTypeText() + " | " + p.StatusText() + " | " + p.ScopeText())), entries);
                }
                catch (Exception ex)
                {
                    AddText(zip, "vpn-profiles.txt", "Không đọc được: " + ex.Message, entries);
                }
            }
            if (log != null) log("Đã tạo gói chẩn đoán: " + zipPath + " (" + entries.Count + " mục, không chứa mật khẩu)");
            return entries;
        }

        private static void AddText(ZipArchive zip, string name, string text, List<string> entries)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var s = e.Open())
            {
                var bytes = new UTF8Encoding(true).GetBytes(text ?? "");
                s.Write(bytes, 0, bytes.Length);
            }
            entries.Add(name);
        }

        private static void AddFile(ZipArchive zip, string path, string name, List<string> entries)
        {
            try
            {
                if (!File.Exists(path)) return;
                AddText(zip, name, File.ReadAllText(path, Encoding.UTF8), entries);
            }
            catch (Exception ex)
            {
                AddText(zip, name + ".error.txt", ex.Message, entries);
            }
        }
    }
}
