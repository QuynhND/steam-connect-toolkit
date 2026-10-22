using System;
using System.IO;
using System.Linq;
using SteamConnectToolkit.Models;

namespace SteamConnectToolkit.Services
{
    /// <summary>Theo dõi phiên chạy (session.json) để bảo đảm khôi phục cấu hình khi thoát hoặc khi app dừng đột ngột.</summary>
    public static class SessionService
    {
        private static readonly object Sync = new object();

        public static string FilePath { get { return Path.Combine(AppPaths.DataDir, "session.json"); } }

        public static SessionState Load()
        {
            lock (Sync) { return JsonStore.Load<SessionState>(FilePath); }
        }

        public static void Save(SessionState s)
        {
            lock (Sync) { JsonStore.Save(FilePath, s); }
        }

        /// <summary>Bắt đầu phiên mới cho tiến trình hiện tại (ghi đè phiên trước).</summary>
        public static SessionState Begin(int pid)
        {
            var s = new SessionState
            {
                Pid = pid,
                StartedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                CleanExit = false
            };
            Save(s);
            return s;
        }

        public static void Update(Action<SessionState> mutate)
        {
            lock (Sync)
            {
                var s = Load();
                mutate(s);
                Save(s);
            }
        }

        public static void MarkClean(bool keepOnExit, string note)
        {
            Update(s =>
            {
                s.CleanExit = true;
                s.KeepOnExit = keepOnExit;
                s.EndedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                s.Note = note;
            });
        }

        public static void AddVpnConnected(string name)
        {
            Update(s =>
            {
                if (!s.VpnConnectedByApp.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase))) s.VpnConnectedByApp.Add(name);
            });
        }

        public static void RemoveVpnConnected(string name)
        {
            Update(s => s.VpnConnectedByApp.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>Có thay đổi nào cần khôi phục không (kiểm tra trạng thái thật của hệ thống).</summary>
        public static bool HasPending(SessionState s, AppConfig cfg)
        {
            if (BackupService.HasAny()) return true;
            if (s != null && s.VpnConnectedByApp.Any(VpnService.IsInterfaceUp)) return true;
            if (cfg != null && cfg.VpnProfilesCreatedByApp != null && cfg.VpnProfilesCreatedByApp.Any(p => p.Auto)) return true;
            return false;
        }

        /// <summary>Mô tả ngắn các thay đổi đang có hiệu lực, mỗi dòng một mục.</summary>
        public static string DescribePending(SessionState s, AppConfig cfg)
        {
            var lines = new System.Collections.Generic.List<string>();
            var backup = BackupService.Load();
            foreach (var a in backup.Adapters) lines.Add("DNS đã đổi trên \"" + a.AdapterName + "\" (gốc: " + a.Describe() + ")");
            if (s != null)
                foreach (var n in s.VpnConnectedByApp.Where(VpnService.IsInterfaceUp)) lines.Add("VPN \"" + n + "\" đang kết nối (do app kết nối)");
            if (cfg != null && cfg.VpnProfilesCreatedByApp != null)
                foreach (var p in cfg.VpnProfilesCreatedByApp.Where(x => x.Auto)) lines.Add("Cấu hình VPN Gate do app tạo: \"" + p.Name + "\"");
            return string.Join(Environment.NewLine, lines);
        }
    }
}
