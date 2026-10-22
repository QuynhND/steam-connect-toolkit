using System;
using System.IO;

namespace SteamConnectToolkit.Services
{
    /// <summary>Đường dẫn dữ liệu: %ProgramData%\SteamConnectToolkit (app chạy với quyền admin nên ghi được).</summary>
    public static class AppPaths
    {
        public const string AppName = "SteamConnectToolkit";
        /// <summary>Tên thư mục dữ liệu của phiên bản cũ (Steam DNS Switcher); được di trú tự động khi khởi động.</summary>
        public const string LegacyAppName = "SteamDnsSwitcher";

        /// <summary>Dùng cho kiểm thử: chuyển hướng thư mục dữ liệu sang nơi khác. null = mặc định.</summary>
        public static string DataDirOverride;

        private static string CommonAppData
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData); }
        }

        public static string DataDir
        {
            get
            {
                if (!string.IsNullOrEmpty(DataDirOverride)) return DataDirOverride;
                return Path.Combine(CommonAppData, AppName);
            }
        }

        public static string LegacyDataDir { get { return Path.Combine(CommonAppData, LegacyAppName); } }

        public static string ConfigFile { get { return Path.Combine(DataDir, "config.json"); } }
        public static string HistoryFile { get { return Path.Combine(DataDir, "history.json"); } }
        public static string BackupFile { get { return Path.Combine(DataDir, "original-dns.json"); } }
        public static string LogFile { get { return Path.Combine(DataDir, "app.log"); } }

        public static void EnsureDataDir()
        {
            try
            {
                if (string.IsNullOrEmpty(DataDirOverride)) MigrateLegacy(LegacyDataDir, DataDir);
                Directory.CreateDirectory(DataDir);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Chuyển dữ liệu (cấu hình, lịch sử, sao lưu DNS gốc) từ thư mục của phiên bản cũ sang thư mục mới
        /// nếu thư mục mới chưa có dữ liệu. Trả về true nếu đã di trú.
        /// </summary>
        public static bool MigrateLegacy(string legacyDir, string newDir)
        {
            if (string.IsNullOrEmpty(legacyDir) || string.IsNullOrEmpty(newDir)) return false;
            if (!Directory.Exists(legacyDir)) return false;
            if (Directory.Exists(newDir) && Directory.GetFiles(newDir).Length > 0) return false;
            try
            {
                if (!Directory.Exists(newDir))
                {
                    Directory.Move(legacyDir, newDir);
                    return true;
                }
            }
            catch (Exception) { }

            // Dự phòng: sao chép từng file, giữ lại thư mục cũ.
            bool any = false;
            try
            {
                Directory.CreateDirectory(newDir);
                foreach (var f in Directory.GetFiles(legacyDir))
                {
                    string dest = Path.Combine(newDir, Path.GetFileName(f));
                    if (File.Exists(dest)) continue;
                    File.Copy(f, dest);
                    any = true;
                }
            }
            catch (Exception) { }
            return any;
        }
    }
}
