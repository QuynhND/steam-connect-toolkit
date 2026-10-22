using System;
using System.Collections.Generic;
using System.Linq;
using SteamConnectToolkit.Models;

namespace SteamConnectToolkit.Services
{
    public class DnsRestoreItem
    {
        public AdapterDnsState State { get; set; }
        public string CurrentName { get; set; }
        public bool Missing { get; set; }
        public bool Ok { get; set; }
        public string Error { get; set; }
    }

    public class RecoveryReport
    {
        public int DnsRestored { get; set; }
        public int DnsFailed { get; set; }
        public int DnsMissing { get; set; }
        public int VpnDisconnected { get; set; }
        public int VpnFailed { get; set; }
        public int ProfilesRemoved { get; set; }
        public int ProfilesFailed { get; set; }
        public List<DnsRestoreItem> DnsItems { get; set; }

        public RecoveryReport()
        {
            DnsItems = new List<DnsRestoreItem>();
        }

        public bool AllOk { get { return DnsFailed == 0 && VpnFailed == 0 && ProfilesFailed == 0; } }
        public bool Nothing { get { return DnsRestored + DnsFailed + DnsMissing + VpnDisconnected + VpnFailed + ProfilesRemoved + ProfilesFailed == 0; } }

        public string Summary()
        {
            var parts = new List<string>();
            if (DnsRestored + DnsFailed + DnsMissing > 0)
                parts.Add("DNS: " + DnsRestored + " card khôi phục" + (DnsFailed > 0 ? ", " + DnsFailed + " lỗi" : "") + (DnsMissing > 0 ? ", " + DnsMissing + " card không có" : ""));
            if (VpnDisconnected + VpnFailed > 0)
                parts.Add("VPN: " + VpnDisconnected + " ngắt" + (VpnFailed > 0 ? ", " + VpnFailed + " lỗi" : ""));
            if (ProfilesRemoved + ProfilesFailed > 0)
                parts.Add("cấu hình VPN Gate: " + ProfilesRemoved + " xoá" + (ProfilesFailed > 0 ? ", " + ProfilesFailed + " lỗi" : ""));
            return parts.Count > 0 ? string.Join("; ", parts) : "không có gì để khôi phục";
        }
    }

    /// <summary>
    /// Khôi phục toàn bộ thay đổi do app gây ra: ngắt VPN app đã kết nối → trả DNS gốc → xoá cấu hình VPN Gate do app tạo.
    /// Dùng chung cho nút Thu hồi, lúc thoát app, lúc khởi động (phiên trước lỗi) và tiến trình guardian.
    /// Mọi bước đều idempotent: chạy lại không gây hại.
    /// </summary>
    public static class RecoveryService
    {
        public static RecoveryReport RestoreAll(SessionState session, AppConfig cfg, bool removeAutoProfiles, Action<string> log)
        {
            var rep = new RecoveryReport();

            // 1. Ngắt VPN do app kết nối (trước khi đổi DNS để card VPN biến mất)
            if (session != null)
            {
                foreach (var name in session.VpnConnectedByApp.ToList())
                {
                    if (!VpnService.IsInterfaceUp(name))
                    {
                        if (log != null) log("VPN " + name + " đã ngắt sẵn.");
                        SessionService.RemoveVpnConnected(name);
                        continue;
                    }
                    var r = VpnService.DisconnectReliable(name, log);
                    HistoryService.Add("Ngắt VPN", name, "Khôi phục cấu hình", r.Ok ? "Thành công" : "Lỗi: " + r.Output);
                    if (r.Ok)
                    {
                        rep.VpnDisconnected++;
                        SessionService.RemoveVpnConnected(name);
                        if (log != null) log("✔ Đã ngắt VPN " + name);
                    }
                    else
                    {
                        rep.VpnFailed++;
                        if (log != null) log("✖ Không ngắt được VPN " + name + ": " + r.Output);
                    }
                }
            }

            // 2. DNS gốc
            var items = RestoreDnsBackups(log);
            rep.DnsItems = items;
            rep.DnsRestored = items.Count(i => i.Ok);
            rep.DnsFailed = items.Count(i => !i.Ok && !i.Missing);
            rep.DnsMissing = items.Count(i => i.Missing);

            // 3. Cấu hình VPN Gate do app tạo
            if (removeAutoProfiles && cfg != null && cfg.VpnProfilesCreatedByApp != null)
            {
                foreach (var p in cfg.VpnProfilesCreatedByApp.Where(x => x.Auto).ToList())
                {
                    if (VpnService.IsInterfaceUp(p.Name))
                    {
                        var d = VpnService.DisconnectReliable(p.Name, log);
                        if (!d.Ok) { rep.ProfilesFailed++; if (log != null) log("✖ Không ngắt được VPN " + p.Name + " để xoá cấu hình"); continue; }
                    }
                    var r = VpnService.RemoveProfile(p.Name, false, log);
                    bool gone = r.Ok || r.Output.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0 || r.Output.IndexOf("không tìm thấy", StringComparison.OrdinalIgnoreCase) >= 0;
                    HistoryService.Add("Xoá VPN", p.Name, "Khôi phục cấu hình (cấu hình do app tạo)", gone ? "Thành công" : "Lỗi: " + r.Output);
                    if (gone)
                    {
                        rep.ProfilesRemoved++;
                        cfg.VpnProfilesCreatedByApp.Remove(p);
                        CredentialStore.Remove(cfg, p.Name);
                        if (log != null) log("✔ Đã xoá cấu hình VPN " + p.Name);
                    }
                    else
                    {
                        rep.ProfilesFailed++;
                        if (log != null) log("✖ Không xoá được cấu hình VPN " + p.Name + ": " + r.Output);
                    }
                }
                try { ProviderCatalog.SaveConfig(cfg); } catch (Exception) { }
            }

            if (log != null) log("Khôi phục: " + rep.Summary());
            return rep;
        }

        /// <summary>Trả DNS gốc cho mọi card có trong bản sao lưu (tìm card theo GUID; card không có thì giữ bản sao lưu).</summary>
        public static List<DnsRestoreItem> RestoreDnsBackups(Action<string> log)
        {
            var items = new List<DnsRestoreItem>();
            var backup = BackupService.Load();
            if (backup.Adapters.Count == 0) return items;
            var present = NetworkAdapterService.GetAdapters(true);
            foreach (var s in backup.Adapters.ToList())
            {
                var item = new DnsRestoreItem { State = s };
                var cur = present.FirstOrDefault(a => string.Equals(a.Id, s.AdapterId, StringComparison.OrdinalIgnoreCase));
                if (cur == null)
                {
                    item.Missing = true;
                    if (log != null) log("⚠ " + s.AdapterName + ": card không có trong hệ thống (đã tắt hoặc đã tháo) – bỏ qua, giữ bản sao lưu.");
                    items.Add(item);
                    continue;
                }
                item.CurrentName = cur.Name;
                if (!string.Equals(cur.Name, s.AdapterName, StringComparison.Ordinal) && log != null)
                    log("Card đã đổi tên: \"" + s.AdapterName + "\" → \"" + cur.Name + "\", dùng tên mới.");
                if (log != null) log("Khôi phục " + cur.Name + " về: " + s.Describe());
                var results = DnsApplier.Restore(s, cur.Name, log);
                item.Ok = results.All(r => r.Ok);
                if (!item.Ok) item.Error = DnsApplier.FirstError(results);
                HistoryService.Add("Thu hồi DNS", cur.Name, "Khôi phục: " + s.Describe(), item.Ok ? "Thành công" : "Lỗi: " + item.Error);
                if (item.Ok)
                {
                    BackupService.Remove(s.AdapterId);
                    if (log != null) log("✔ " + cur.Name + ": đã khôi phục DNS gốc");
                }
                else if (log != null) log("✖ " + cur.Name + ": không khôi phục được – " + item.Error + " (giữ lại bản sao lưu)");
                items.Add(item);
            }
            if (items.Any(i => i.Ok)) DnsApplier.FlushDns(log);
            return items;
        }
    }
}
