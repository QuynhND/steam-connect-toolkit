using System;
using System.Linq;
using SteamConnectToolkit.Models;

namespace SteamConnectToolkit.Services
{
    /// <summary>Lưu cấu hình DNS gốc của từng card mạng (chỉ chụp một lần, trước thay đổi đầu tiên).</summary>
    public static class BackupService
    {
        private static readonly object Sync = new object();

        public static BackupData Load()
        {
            lock (Sync) { return JsonStore.Load<BackupData>(AppPaths.BackupFile); }
        }

        public static void Save(BackupData data)
        {
            lock (Sync) { JsonStore.Save(AppPaths.BackupFile, data); }
        }

        public static bool HasAny()
        {
            var d = Load();
            return d.Adapters != null && d.Adapters.Count > 0;
        }

        /// <summary>Nếu card chưa có bản sao lưu thì chụp ngay; trả về trạng thái gốc.</summary>
        public static AdapterDnsState EnsureCaptured(AdapterInfo adapter, out bool capturedNow)
        {
            lock (Sync)
            {
                var d = Load();
                var existing = d.Adapters.FirstOrDefault(x => string.Equals(x.AdapterId, adapter.Id, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    capturedNow = false;
                    return existing;
                }
                var s = NetworkAdapterService.CaptureState(adapter);
                d.Adapters.Add(s);
                if (string.IsNullOrEmpty(d.CreatedAt)) d.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                Save(d);
                capturedNow = true;
                return s;
            }
        }

        public static void Remove(string adapterId)
        {
            lock (Sync)
            {
                var d = Load();
                d.Adapters.RemoveAll(x => string.Equals(x.AdapterId, adapterId, StringComparison.OrdinalIgnoreCase));
                if (d.Adapters.Count == 0) d.CreatedAt = null;
                Save(d);
            }
        }
    }
}
