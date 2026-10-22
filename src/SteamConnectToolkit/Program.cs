using System;
using System.Threading;
using System.Windows.Forms;
using SteamConnectToolkit.Services;
using SteamConnectToolkit.UI;

namespace SteamConnectToolkit
{
    internal static class Program
    {
        /// <summary>true khi app được mở lại ngay sau khi nâng cấp (--after-update).</summary>
        internal static bool AfterUpdate;

        [STAThread]
        private static int Main(string[] args)
        {
            // Phải gọi trước mọi thao tác mạng.
            HttpsProbe.ConfigureTls();

            // Chế độ guardian: chạy ẩn, chờ app chính kết thúc, khôi phục nếu app dừng đột ngột.
            if (args.Length >= 2 && args[0] == GuardianRunner.Argument)
            {
                int pid;
                if (!int.TryParse(args[1], out pid)) return 2;
                AppPaths.EnsureDataDir();
                return GuardianRunner.Run(pid, GuardianRunner.FileLog);
            }

            // Chế độ thay file sau khi tải bản mới (portable): --apply-update <file mới> <pid app cũ>
            if (args.Length >= 3 && args[0] == UpdateService.ApplyArgument)
            {
                int pid;
                int.TryParse(args[2], out pid);
                AppPaths.EnsureDataDir();
                string target = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                return UpdateService.ApplyPortableUpdate(args[1], pid, target, true, GuardianRunner.FileLog);
            }
            AfterUpdate = args.Length >= 1 && args[0] == UpdateService.AfterUpdateArgument;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (!AdminHelper.IsWindows10OrNewer())
            {
                MessageBox.Show("Ứng dụng được thiết kế cho Windows 10 và Windows 11. Trên phiên bản cũ hơn có thể hoạt động không ổn định.",
                    "Steam Connect Toolkit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            // Manifest đã yêu cầu requireAdministrator; kiểm tra lại phòng trường hợp manifest bị bỏ qua.
            if (!AdminHelper.IsAdministrator())
            {
                var r = MessageBox.Show(
                    "Ứng dụng cần quyền Administrator để thay đổi DNS của card mạng.\n\n" +
                    "Bấm OK để khởi động lại với quyền Administrator (Windows sẽ hỏi xác nhận UAC).",
                    "Cần quyền Administrator", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                if (r == DialogResult.OK)
                {
                    if (!AdminHelper.TryRelaunchElevated())
                    {
                        MessageBox.Show("Không thể khởi động lại với quyền Administrator. Hãy chuột phải vào file .exe và chọn \"Run as administrator\".",
                            "Steam Connect Toolkit", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                return 1;
            }

            bool createdNew;
            using (var mutex = new Mutex(true, "Global\\SteamConnectToolkit.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("Steam Connect Toolkit đang chạy.", "Steam Connect Toolkit", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }

                Application.ThreadException += (s, e) =>
                {
                    MessageBox.Show("Lỗi không mong muốn:\n" + e.Exception.Message, "Steam Connect Toolkit", MessageBoxButtons.OK, MessageBoxIcon.Error);
                };
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    var ex = e.ExceptionObject as Exception;
                    MessageBox.Show("Lỗi nghiêm trọng:\n" + (ex != null ? ex.ToString() : e.ExceptionObject.ToString()),
                        "Steam Connect Toolkit", MessageBoxButtons.OK, MessageBoxIcon.Error);
                };

                Application.Run(new MainForm());
            }
            return 0;
        }
    }
}
