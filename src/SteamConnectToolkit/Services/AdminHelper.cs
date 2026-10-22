using System;
using System.Diagnostics;
using System.Security.Principal;

namespace SteamConnectToolkit.Services
{
    public static class AdminHelper
    {
        public static bool IsAdministrator()
        {
            try
            {
                using (var id = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(id);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Khởi động lại chính ứng dụng với quyền Administrator (UAC). Trả về true nếu đã gửi yêu cầu.</summary>
        public static bool TryRelaunchElevated()
        {
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                var psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = Environment.CurrentDirectory
                };
                Process.Start(psi);
                return true;
            }
            catch (Exception)
            {
                // Người dùng bấm "No" ở UAC hoặc lỗi khác.
                return false;
            }
        }

        public static bool IsWindows10OrNewer()
        {
            try { return Environment.OSVersion.Version.Major >= 10; }
            catch (Exception) { return true; }
        }
    }
}
