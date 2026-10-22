using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using SteamConnectToolkit.Models;

namespace SteamConnectToolkit.Services
{
    /// <summary>
    /// Tiến trình "guardian": bản sao của app chạy ẩn với tham số --guard &lt;pid&gt;, chờ app chính kết thúc.
    /// Nếu app chính không đánh dấu thoát sạch (crash, bị End task) mà vẫn còn thay đổi DNS/VPN thì guardian khôi phục.
    /// </summary>
    public static class GuardianRunner
    {
        public const string Argument = "--guard";
        public const int SettleDelayMs = 2000;

        /// <summary>Khởi động guardian cho tiến trình hiện tại. Chỉ chạy khi app được chạy từ file exe chính.</summary>
        public static bool Launch(Action<string> log)
        {
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                if (!exe.EndsWith(AppPaths.AppName + ".exe", StringComparison.OrdinalIgnoreCase))
                {
                    if (log != null) log("Guardian: bỏ qua (không chạy từ " + AppPaths.AppName + ".exe).");
                    return false;
                }
                var psi = new ProcessStartInfo(exe, Argument + " " + Process.GetCurrentProcess().Id)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);
                if (log != null) log("Guardian đã chạy kèm: nếu app dừng đột ngột, DNS/VPN sẽ được khôi phục tự động.");
                return true;
            }
            catch (Exception ex)
            {
                if (log != null) log("Guardian: không khởi động được – " + ex.Message);
                return false;
            }
        }

        /// <summary>Chờ tiến trình cha kết thúc rồi khôi phục nếu cần. Trả về mã thoát.</summary>
        public static int Run(int parentPid, Action<string> log)
        {
            try
            {
                var parent = Process.GetProcessById(parentPid);
                parent.WaitForExit();
            }
            catch (ArgumentException) { /* đã kết thúc */ }
            catch (Exception ex)
            {
                if (log != null) log("Guardian: không theo dõi được tiến trình " + parentPid + " – " + ex.Message);
            }
            Thread.Sleep(SettleDelayMs); // cho app chính kịp ghi session.json

            var s = SessionService.Load();
            if (s.Pid != parentPid)
            {
                if (log != null) log("Guardian: session.json thuộc phiên khác (pid " + s.Pid + "), bỏ qua.");
                return 0;
            }
            if (s.CleanExit)
            {
                if (log != null) log("Guardian: app đã thoát sạch" + (s.KeepOnExit ? " (giữ thay đổi theo lựa chọn người dùng)" : "") + ", không cần làm gì.");
                return 0;
            }
            AppConfig cfg = null;
            try { cfg = ProviderCatalog.LoadConfig(); } catch (Exception) { }
            if (!SessionService.HasPending(s, cfg))
            {
                SessionService.MarkClean(false, "guardian: không có thay đổi nào");
                if (log != null) log("Guardian: app dừng đột ngột nhưng không có thay đổi nào cần khôi phục.");
                return 0;
            }
            if (log != null) log("Guardian: app dừng đột ngột (pid " + parentPid + "), đang khôi phục cấu hình…");
            var rep = RecoveryService.RestoreAll(s, cfg, true, log);
            SessionService.Update(x =>
            {
                x.CleanExit = rep.AllOk;
                x.EndedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                x.Note = "guardian: " + rep.Summary();
            });
            HistoryService.Add("Khôi phục tự động", "(guardian)", "App dừng đột ngột; " + rep.Summary(), rep.AllOk ? "Thành công" : "Một phần lỗi");
            return rep.AllOk ? 0 : 1;
        }

        /// <summary>Ghi nhật ký vào app.log (guardian không có giao diện).</summary>
        public static void FileLog(string msg)
        {
            try
            {
                File.AppendAllText(AppPaths.LogFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [guardian] " + msg + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception) { }
        }
    }
}
