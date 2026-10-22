using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using SteamConnectToolkit.Models;

namespace SteamConnectToolkit.Services
{
    /// <summary>
    /// Quản lý VPN tích hợp của Windows 10/11 (IKEv2, L2TP/IPsec, SSTP, PPTP):
    /// - danh sách / thêm / xoá cấu hình: PowerShell module VpnClient (Get/Add/Remove-VpnConnection)
    /// - kết nối: RAS API (RasDial) để mật khẩu không xuất hiện trên dòng lệnh
    /// - ngắt: rasdial.exe /disconnect
    /// Không cần driver bên thứ ba.
    /// </summary>
    public static class VpnService
    {
        /// <summary>Hook kiểm thử: thay việc chạy PowerShell thật (script → kết quả). null = chạy thật.</summary>
        public static Func<string, CommandResult> PowerShellRunner;

        public static readonly string[] TunnelTypes = { "Ikev2", "L2tp", "Sstp", "Pptp", "Automatic" };

        public const int PowerShellTimeoutMs = 90000;

        // =====================================================================
        //  Danh sách cấu hình
        // =====================================================================

        private const string ListScript =
            "[Console]::OutputEncoding=[Text.Encoding]::UTF8; $ErrorActionPreference='SilentlyContinue'; " +
            "$u=@(Get-VpnConnection) | ForEach-Object { $_ | Add-Member -NotePropertyName AllUser -NotePropertyValue $false -PassThru }; " +
            "$a=@(Get-VpnConnection -AllUserConnection) | ForEach-Object { $_ | Add-Member -NotePropertyName AllUser -NotePropertyValue $true -PassThru }; " +
            "@($u)+@($a) | Where-Object { $_ -and $_.Name } | " +
            "Select-Object Name,ServerAddress,TunnelType,ConnectionStatus,EncryptionLevel,SplitTunneling,RememberCredential,AllUser,@{n='Auth';e={(@($_.AuthenticationMethod) -join '+')}} | " +
            "ConvertTo-Csv -NoTypeInformation";

        public static List<VpnProfile> ListProfiles(Action<string> log)
        {
            var r = RunPowerShell(ListScript, log);
            if (!r.Ok) throw new InvalidOperationException("Không đọc được danh sách VPN của Windows: " + Short(r.Output));
            var list = ParseProfiles(r.Output);
            MarkConnected(list);
            return list;
        }

        /// <summary>Đọc CSV của Get-VpnConnection (hàm thuần, dùng cho kiểm thử).</summary>
        public static List<VpnProfile> ParseProfiles(string csv)
        {
            var list = new List<VpnProfile>();
            if (string.IsNullOrWhiteSpace(csv)) return list;
            var rows = DiscoveryService.ParseCsv(csv.Trim());
            if (rows.Count < 2) return list;
            var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
            int iName = header.IndexOf("name");
            int iServer = header.IndexOf("serveraddress");
            int iType = header.IndexOf("tunneltype");
            int iStatus = header.IndexOf("connectionstatus");
            int iEnc = header.IndexOf("encryptionlevel");
            int iSplit = header.IndexOf("splittunneling");
            int iRem = header.IndexOf("remembercredential");
            int iAll = header.IndexOf("alluser");
            int iAuth = header.IndexOf("auth");
            if (iName < 0) return list;
            for (int i = 1; i < rows.Count; i++)
            {
                var row = rows[i];
                string name = Get(row, iName);
                if (name.Length == 0) continue;
                list.Add(new VpnProfile
                {
                    Name = name,
                    Server = Get(row, iServer),
                    TunnelType = Get(row, iType),
                    Status = Get(row, iStatus),
                    Encryption = Get(row, iEnc),
                    SplitTunneling = IsTrue(Get(row, iSplit)),
                    RememberCredential = IsTrue(Get(row, iRem)),
                    AllUsers = IsTrue(Get(row, iAll)),
                    Authentication = Get(row, iAuth)
                });
            }
            return list;
        }

        /// <summary>Đánh dấu đã kết nối: Windows báo Connected hoặc có card mạng cùng tên đang Up (độc lập ngôn ngữ).</summary>
        public static void MarkConnected(List<VpnProfile> list)
        {
            foreach (var p in list)
                p.IsConnected = string.Equals(p.Status, "Connected", StringComparison.OrdinalIgnoreCase) || IsInterfaceUp(p.Name);
        }

        public static bool IsInterfaceUp(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces().Any(ni =>
                    string.Equals(ni.Name, name, StringComparison.OrdinalIgnoreCase) && ni.OperationalStatus == OperationalStatus.Up);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // =====================================================================
        //  Thêm / xoá cấu hình
        // =====================================================================

        public static string BuildAddScript(VpnProfileSpec s)
        {
            var sb = new StringBuilder();
            sb.Append("[Console]::OutputEncoding=[Text.Encoding]::UTF8; $ErrorActionPreference='Stop'; ");
            sb.Append("Add-VpnConnection -Name '").Append(Q(s.Name.Trim())).Append("' -ServerAddress '").Append(Q(s.Server.Trim()))
              .Append("' -TunnelType ").Append(s.TunnelType).Append(" -Force");
            string t = (s.TunnelType ?? "").ToLowerInvariant();
            if (t == "l2tp")
            {
                sb.Append(" -AuthenticationMethod MSChapv2");
                if (!string.IsNullOrEmpty(s.L2tpPsk)) sb.Append(" -L2tpPsk '").Append(Q(s.L2tpPsk)).Append("'");
            }
            else if (t == "pptp" || t == "sstp")
            {
                sb.Append(" -AuthenticationMethod MSChapv2");
            }
            // IKEv2 / Automatic: mặc định EAP (MSCHAPv2) = tên đăng nhập + mật khẩu.
            if (s.RememberCredential) sb.Append(" -RememberCredential");
            if (s.AllUsers) sb.Append(" -AllUserConnection");
            if (s.SplitTunneling) sb.Append(" -SplitTunneling");
            sb.Append("; Write-Output 'OK'");
            return sb.ToString();
        }

        public static CommandResult AddProfile(VpnProfileSpec s, Action<string> log)
        {
            string err = s.Validate();
            if (err != null) return new CommandResult { Command = "Add-VpnConnection", ExitCode = -1, Output = err };
            return RunPowerShell(BuildAddScript(s), log);
        }

        public static string BuildRemoveScript(string name, bool allUsers)
        {
            return "$ErrorActionPreference='Stop'; Remove-VpnConnection -Name '" + Q(name) + "' -Force" + (allUsers ? " -AllUserConnection" : "") + "; Write-Output 'OK'";
        }

        public static CommandResult RemoveProfile(string name, bool allUsers, Action<string> log)
        {
            return RunPowerShell(BuildRemoveScript(name, allUsers), log);
        }

        // =====================================================================
        //  Kết nối / ngắt
        // =====================================================================

        public static VpnResult Connect(VpnProfile profile, string userName, string password, Action<string> log)
        {
            return Connect(profile.Name, profile.AllUsers, userName, password, log);
        }

        /// <summary>Quay số đồng bộ bằng RasDial; trả về khi đã kết nối hoặc thất bại (có thể mất tới ~1 phút).
        /// Không có tên đăng nhập → dùng tài khoản Windows đã nhớ cho cấu hình (qua rasdial.exe).</summary>
        public static VpnResult Connect(string entryName, bool allUsers, string userName, string password, Action<string> log)
        {
            if (string.IsNullOrEmpty(userName)) return ConnectWithSavedCredentials(entryName, log);
            var result = new VpnResult();
            var sw = Stopwatch.StartNew();
            var p = new RASDIALPARAMS();
            p.dwSize = Marshal.SizeOf(typeof(RASDIALPARAMS));
            p.szEntryName = entryName ?? "";
            p.szPhoneNumber = "";
            p.szCallbackNumber = "";
            p.szUserName = userName ?? "";
            p.szPassword = password ?? "";
            p.szDomain = "";
            string phonebook = PhonebookPath(allUsers);
            if (log != null)
                log("> RasDial \"" + entryName + "\" (" + (allUsers ? "mọi người dùng" : "người dùng này") + ", tài khoản: "
                    + (string.IsNullOrEmpty(userName) ? "(đã lưu trong Windows)" : userName) + ")");

            IntPtr hConn = IntPtr.Zero;
            int rc;
            try
            {
                rc = RasDialW(IntPtr.Zero, phonebook, ref p, 0, IntPtr.Zero, ref hConn);
            }
            catch (Exception ex)
            {
                result.ErrorCode = -1;
                result.Message = "Không gọi được RAS API: " + ex.Message;
                result.ElapsedMs = sw.Elapsed.TotalMilliseconds;
                return result;
            }
            result.ElapsedMs = sw.Elapsed.TotalMilliseconds;
            if (rc == 0)
            {
                result.Ok = true;
                result.Message = "Đã kết nối sau " + Math.Round(result.ElapsedMs / 1000.0, 1) + " giây";
                return result;
            }
            if (hConn != IntPtr.Zero)
            {
                try { RasHangUpW(hConn); } catch (Exception) { }
            }
            result.ErrorCode = rc;
            result.Message = DescribeRasError(rc);
            return result;
        }

        /// <summary>rasdial.exe "tên" không kèm tài khoản → Windows dùng tài khoản đã nhớ. Mã thoát khác 0 = mã lỗi RAS.</summary>
        private static VpnResult ConnectWithSavedCredentials(string entryName, Action<string> log)
        {
            var sw = Stopwatch.StartNew();
            string name = (entryName ?? "").Replace("\"", "");
            // Quay số có thể mất tới vài chục giây → cho rasdial tối đa 2 phút.
            var r = DnsApplier.Run(Path.Combine(Environment.SystemDirectory, "rasdial.exe"), "\"" + name + "\"", log, 120000);
            var result = new VpnResult { ElapsedMs = sw.Elapsed.TotalMilliseconds };
            if (r.Ok && IsInterfaceUp(name))
            {
                result.Ok = true;
                result.Message = "Đã kết nối (tài khoản Windows đã nhớ) sau " + Math.Round(result.ElapsedMs / 1000.0, 1) + " giây";
                return result;
            }
            result.ErrorCode = r.ExitCode;
            result.Message = r.ExitCode > 0 ? DescribeRasError(r.ExitCode) : "rasdial: " + Short(r.Output);
            if (r.Ok) result.Message = "rasdial báo thành công nhưng card VPN chưa lên: " + Short(r.Output);
            return result;
        }

        public static CommandResult Disconnect(string entryName, Action<string> log)
        {
            string name = (entryName ?? "").Replace("\"", "");
            return DnsApplier.Run(Path.Combine(Environment.SystemDirectory, "rasdial.exe"), "\"" + name + "\" /disconnect", log);
        }

        /// <summary>Ngắt và xác minh card VPN đã biến mất; thử lại tối đa 3 lần.</summary>
        public static CommandResult DisconnectReliable(string entryName, Action<string> log)
        {
            CommandResult last = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                last = Disconnect(entryName, log);
                for (int w = 0; w < 10; w++)
                {
                    if (!IsInterfaceUp(entryName))
                        return new CommandResult { Command = last.Command, ExitCode = 0, Output = "đã ngắt" };
                    System.Threading.Thread.Sleep(300);
                }
                if (log != null) log("VPN " + entryName + " vẫn còn kết nối, thử ngắt lại (" + attempt + "/3)…");
            }
            if (!IsInterfaceUp(entryName)) return new CommandResult { Command = last.Command, ExitCode = 0, Output = "đã ngắt" };
            return new CommandResult
            {
                Command = last.Command,
                ExitCode = last.ExitCode == 0 ? 1 : last.ExitCode,
                Output = "card VPN vẫn còn sau 3 lần ngắt" + (string.IsNullOrEmpty(last.Output) ? "" : ": " + last.Output)
            };
        }

        private static string PhonebookPath(bool allUsers)
        {
            try
            {
                string root = allUsers
                    ? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
                    : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string path = Path.Combine(root, @"Microsoft\Network\Connections\Pbk\rasphone.pbk");
                return File.Exists(path) ? path : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // =====================================================================
        //  L2TP/IPsec sau NAT: AssumeUDPEncapsulationContextOnSendRule
        // =====================================================================

        private const string PolicyAgentKey = @"SYSTEM\CurrentControlSet\Services\PolicyAgent";
        private const string NatTValueName = "AssumeUDPEncapsulationContextOnSendRule";

        /// <summary>Giá trị hiện tại (null = chưa đặt). 2 = cho phép cả máy khách và máy chủ sau NAT.</summary>
        public static int? GetL2tpNatRule()
        {
            try
            {
                using (var hklm = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64))
                using (var key = hklm.OpenSubKey(PolicyAgentKey, false))
                {
                    if (key == null) return null;
                    var v = key.GetValue(NatTValueName);
                    return v is int ? (int?)(int)v : null;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Đặt giá trị 2 (cần quyền admin) rồi khởi động lại dịch vụ IPsec; Windows khuyến nghị khởi động lại máy để chắc chắn có hiệu lực.</summary>
        public static CommandResult SetL2tpNatRule(Action<string> log)
        {
            try
            {
                using (var hklm = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64))
                using (var key = hklm.CreateSubKey(PolicyAgentKey))
                {
                    key.SetValue(NatTValueName, 2, Microsoft.Win32.RegistryValueKind.DWord);
                }
                if (log != null) log("Đã đặt HKLM\\" + PolicyAgentKey + "\\" + NatTValueName + " = 2");
            }
            catch (Exception ex)
            {
                return new CommandResult { Command = "registry " + NatTValueName, ExitCode = -1, Output = ex.Message };
            }
            return RunPowerShell("$ErrorActionPreference='Continue'; Restart-Service PolicyAgent -Force; Restart-Service IKEEXT -Force; Write-Output 'OK'", log);
        }

        // =====================================================================
        //  IP công cộng (để thấy lưu lượng đã đi qua VPN chưa)
        // =====================================================================

        // Chỉ dùng máy chủ IPv4 (api4 / ipv4.*): máy chủ dual-stack sẽ trả IPv6 của ISP ngay cả khi đang nối VPN IPv4.
        public static readonly string[] IpEndpoints = { "https://api4.ipify.org", "https://ipv4.icanhazip.com", "https://checkip.amazonaws.com", "https://ifconfig.me/ip" };
        public static readonly string[] Ipv6Endpoints = { "https://api6.ipify.org", "https://ipv6.icanhazip.com" };

        /// <summary>IPv4 công cộng (null nếu không lấy được).</summary>
        public static string GetPublicIp(int timeoutMs, out string error)
        {
            error = null;
            int perEndpoint = Math.Max(2000, Math.Min(timeoutMs, 6000));
            foreach (var url in IpEndpoints)
            {
                try
                {
                    string s = HttpGet(url, perEndpoint).Trim();
                    IPAddress ip;
                    if (IPAddress.TryParse(s, out ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) return ip.ToString();
                    error = "phản hồi không phải IPv4 từ " + url;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            }
            return null;
        }

        /// <summary>IPv6 công cộng; null nếu máy không có IPv6 (hoặc không hỏi được). Dùng để phát hiện rò rỉ IPv6 khi nối VPN.</summary>
        public static string GetPublicIpv6(int timeoutMs)
        {
            int perEndpoint = Math.Max(1500, Math.Min(timeoutMs, 5000));
            foreach (var url in Ipv6Endpoints)
            {
                try
                {
                    string s = HttpGet(url, perEndpoint).Trim();
                    IPAddress ip;
                    if (IPAddress.TryParse(s, out ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6) return ip.ToString();
                }
                catch (Exception) { }
            }
            return null;
        }

        /// <summary>Mã quốc gia (2 chữ) của IP; null nếu không tra được.</summary>
        public static string GetIpCountry(string ip, int timeoutMs)
        {
            try
            {
                string s = HttpGet("https://ipinfo.io/" + ip + "/country", timeoutMs).Trim();
                return s.Length == 2 && s.All(char.IsLetter) ? s.ToUpperInvariant() : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string HttpGet(string url, int timeoutMs)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.UserAgent = "curl/8.0 (SteamConnectToolkit/1.0)";
            req.Accept = "text/plain";
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream()))
            {
                return sr.ReadToEnd();
            }
        }

        // =====================================================================
        //  Mã lỗi RAS
        // =====================================================================

        public static string DescribeRasError(int code)
        {
            string sys = RasErrorString(code);
            string hint = RasHint(code);
            var sb = new StringBuilder("Lỗi " + code);
            if (!string.IsNullOrEmpty(sys)) sb.Append(": ").Append(sys);
            if (!string.IsNullOrEmpty(hint)) sb.Append(" → ").Append(hint);
            return sb.ToString();
        }

        private static string RasErrorString(int code)
        {
            try
            {
                var sb = new StringBuilder(1024);
                if (RasGetErrorStringW(code, sb, sb.Capacity) == 0) return sb.ToString().Trim();
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>Gợi ý tiếng Việt cho các mã lỗi VPN hay gặp.</summary>
        public static string RasHint(int code)
        {
            switch (code)
            {
                case 623: return "Không tìm thấy cấu hình VPN này trong Windows (bấm Làm mới).";
                case 691: return "Sai tên đăng nhập/mật khẩu, hoặc máy chủ không chấp nhận phương thức xác thực.";
                case 800: return "Không tới được máy chủ VPN: kiểm tra địa chỉ, loại giao thức và firewall (IKEv2: UDP 500/4500; L2TP: UDP 500/4500/1701; SSTP: TCP 443; PPTP: TCP 1723 + GRE).";
                case 806: return "GRE (PPTP) bị router/ISP chặn – hãy dùng IKEv2 hoặc SSTP.";
                case 807: return "Kết nối bị ngắt giữa chừng – mạng không ổn định hoặc máy chủ đóng kết nối.";
                case 809: return "Không thiết lập được phiên: cổng bị chặn hoặc máy nằm sau NAT. Với L2TP/IPsec sau NAT cần đặt AssumeUDPEncapsulationContextOnSendRule=2 trong registry rồi khởi động lại.";
                case 812: return "Máy chủ từ chối theo chính sách (phương thức xác thực/EAP không khớp).";
                case 868: return "Không phân giải được tên máy chủ VPN – kiểm tra DNS hoặc dùng địa chỉ IP.";
                case 789: return "L2TP: lỗi thương lượng bảo mật – thường do sai Pre-shared key.";
                case 835: return "L2TP: không xác thực được chứng chỉ máy chủ.";
                case 13801: return "IKEv2: chứng chỉ máy chủ không hợp lệ hoặc chưa được tin cậy (cần cài chứng chỉ CA của nhà cung cấp).";
                case 13806: return "IKEv2: máy chủ yêu cầu chứng chỉ máy tính mà máy này không có.";
                case 13868: return "IKEv2: không khớp chính sách IPsec (thuật toán) với máy chủ.";
                case 720: return "Lỗi giao thức PPP – thử xoá rồi tạo lại cấu hình VPN hoặc đổi loại giao thức.";
                case 619: return "Cổng bị ngắt – máy chủ/firewall đóng kết nối; kiểm tra lại loại giao thức.";
                case 678: return "Máy chủ không trả lời.";
                case 676: return "Máy chủ bận.";
                case 633: return "Cổng đang được kết nối khác dùng – ngắt VPN khác trước.";
                case 632: return "Cấu trúc tham số không đúng (lỗi chương trình – vui lòng báo cho tác giả).";
                default: return null;
            }
        }

        // =====================================================================
        //  PowerShell
        // =====================================================================

        public static CommandResult RunPowerShell(string script, Action<string> log)
        {
            var runner = PowerShellRunner;
            if (runner != null)
            {
                var sim = runner(script);
                return sim ?? new CommandResult { Command = "powershell", ExitCode = -1, Output = "" };
            }

            string exe = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            string args = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -OutputFormat Text -Command \"" + script.Replace("\"", "\\\"") + "\"";
            var r = new CommandResult { Command = "powershell.exe " + ShortLabel(script), Output = "" };
            if (log != null) log("> " + r.Command);
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                using (var proc = Process.Start(psi))
                {
                    var errTask = Task.Run(() => proc.StandardError.ReadToEnd());
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = errTask.Wait(PowerShellTimeoutMs) ? errTask.Result : "";
                    if (!proc.WaitForExit(PowerShellTimeoutMs))
                    {
                        try { proc.Kill(); } catch (Exception) { }
                        r.ExitCode = -1;
                        r.Output = "PowerShell quá thời gian chờ";
                        return r;
                    }
                    r.ExitCode = proc.ExitCode;
                    r.Output = stdout.Trim();
                    if (!string.IsNullOrWhiteSpace(stderr))
                    {
                        r.Output = (r.Output + Environment.NewLine + stderr.Trim()).Trim();
                        // Script 'Stop' không in OK mà có lỗi → coi là thất bại dù exit code 0.
                        if (r.ExitCode == 0 && script.Contains("$ErrorActionPreference='Stop'") && !stdout.Contains("OK")) r.ExitCode = 1;
                    }
                }
            }
            catch (Exception ex)
            {
                r.ExitCode = -1;
                r.Output = ex.Message;
            }
            if (log != null && !r.Ok) log("  => lỗi: " + Short(r.Output));
            return r;
        }

        // =====================================================================
        //  Tiện ích
        // =====================================================================

        private static string Q(string s)
        {
            return (s ?? "").Replace("'", "''").Replace("\r", "").Replace("\n", "");
        }

        private static string Get(List<string> row, int idx)
        {
            return idx >= 0 && idx < row.Count ? row[idx].Trim() : "";
        }

        private static bool IsTrue(string s)
        {
            return string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static string Short(string s)
        {
            if (string.IsNullOrEmpty(s)) return "(không có thông tin)";
            s = s.Replace("\r\n", " / ").Replace("\n", " / ");
            return s.Length > 300 ? s.Substring(0, 300) + "…" : s;
        }

        private static string ShortLabel(string script)
        {
            int i = script.IndexOf("-VpnConnection", StringComparison.Ordinal);
            if (i > 0)
            {
                int start = script.LastIndexOf(' ', i) + 1;
                int end = script.IndexOf(';', i);
                string cmd = end > start ? script.Substring(start, end - start) : script.Substring(start);
                // Ẩn PSK nếu có
                int psk = cmd.IndexOf("-L2tpPsk", StringComparison.OrdinalIgnoreCase);
                if (psk >= 0) cmd = cmd.Substring(0, psk) + "-L2tpPsk '***'";
                return cmd.Length > 200 ? cmd.Substring(0, 200) + "…" : cmd;
            }
            return "Get-VpnConnection";
        }

        // =====================================================================
        //  P/Invoke rasapi32
        // =====================================================================

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RASDIALPARAMS
        {
            public int dwSize;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string szEntryName;      // RAS_MaxEntryName + 1
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 129)] public string szPhoneNumber;    // RAS_MaxPhoneNumber + 1
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 129)] public string szCallbackNumber; // RAS_MaxCallbackNumber + 1
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string szUserName;       // UNLEN + 1
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string szPassword;       // PWLEN + 1
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string szDomain;          // DNLEN + 1
            public int dwSubEntry;
            public IntPtr dwCallbackId;
            public int dwIfIndex;
        }

        [DllImport("rasapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RasDialW(IntPtr lpRasDialExtensions, string lpszPhonebook, ref RASDIALPARAMS lpRasDialParams,
            uint dwNotifierType, IntPtr lpvNotifier, ref IntPtr lphRasConn);

        [DllImport("rasapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int RasHangUpW(IntPtr hrasconn);

        [DllImport("rasapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int RasGetErrorStringW(int uErrorValue, StringBuilder lpszErrorString, int cBufSize);
    }
}
