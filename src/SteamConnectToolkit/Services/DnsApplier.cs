using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SteamConnectToolkit.Models;

namespace SteamConnectToolkit.Services
{
    public class CommandResult
    {
        public string Command { get; set; }
        public int ExitCode { get; set; }
        public string Output { get; set; }
        public bool Ok { get { return ExitCode == 0; } }
    }

    /// <summary>Thay đổi DNS bằng netsh (IPv4 và IPv6), cần quyền Administrator.</summary>
    public static class DnsApplier
    {
        /// <summary>
        /// Hook kiểm thử: (đường dẫn exe, tham số) → kết quả, thay cho việc chạy tiến trình thật.
        /// null = chạy netsh/ipconfig thật.
        /// </summary>
        public static Func<string, string, CommandResult> CommandRunner;

        /// <summary>Đặt DNS tĩnh IPv4 + IPv6 theo nhà cung cấp. Nếu nhà cung cấp không có IPv6 thì xoá DNS IPv6 (address=none)
        /// để Windows không dùng DNS IPv6 của ISP nữa.</summary>
        public static List<CommandResult> ApplyProvider(AdapterInfo adapter, DnsProvider p, Action<string> log)
        {
            var results = new List<CommandResult>();
            results.AddRange(SetStatic(adapter.Name, "ipv4", p.GetIpv4Servers(), log));
            results.AddRange(SetStatic(adapter.Name, "ipv6", p.GetIpv6Servers(), log));
            return results;
        }

        public class ApplyOutcome
        {
            public bool Ok { get; set; }
            public bool RolledBack { get; set; }
            public bool RollbackOk { get; set; }
            public string Error { get; set; }
            public List<CommandResult> Commands { get; set; }

            public ApplyOutcome()
            {
                Commands = new List<CommandResult>();
            }
        }

        /// <summary>
        /// Áp dụng DNS "có giao dịch": chạy netsh → đọc lại registry để xác minh → nếu lệnh lỗi hoặc không khớp thì hoàn tác
        /// ngay về cấu hình gốc, để card không bao giờ bị bỏ ở trạng thái dở dang.
        /// </summary>
        public static ApplyOutcome ApplyProviderSafely(AdapterInfo adapter, DnsProvider p, AdapterDnsState original, Action<string> log)
        {
            var o = new ApplyOutcome();
            var results = ApplyProvider(adapter, p, log);
            o.Commands.AddRange(results);
            var bad = results.FirstOrDefault(r => !r.Ok);
            if (bad == null)
            {
                string mismatch = VerifyApplied(NetworkAdapterService.ReadRegistryState(adapter.Id), p);
                if (mismatch == null)
                {
                    o.Ok = true;
                    return o;
                }
                o.Error = "xác minh sau khi đặt không khớp (" + mismatch + ")";
            }
            else
            {
                o.Error = string.IsNullOrEmpty(bad.Output) ? bad.Command : bad.Output;
            }

            if (log != null) log("⟲ Hoàn tác " + adapter.Name + " về cấu hình gốc vì lỗi: " + o.Error);
            var rb = Restore(original, adapter.Name, log);
            o.Commands.AddRange(rb);
            o.RolledBack = true;
            o.RollbackOk = rb.All(r => r.Ok);
            if (log != null)
            {
                if (o.RollbackOk) log("⟲ " + adapter.Name + ": đã trả về cấu hình gốc (" + original.Describe() + ")");
                else log("✖ " + adapter.Name + ": hoàn tác cũng lỗi – " + FirstError(rb) + "; bản sao lưu được giữ lại");
            }
            return o;
        }

        /// <summary>So sánh trạng thái đọc lại từ registry với nhà cung cấp; trả về mô tả sai khác hoặc null nếu khớp.</summary>
        public static string VerifyApplied(AdapterDnsState state, DnsProvider p)
        {
            var want4 = Normalize(p.GetIpv4Servers());
            var want6 = Normalize(p.GetIpv6Servers());
            var have4 = Normalize(state.Ipv4Servers);
            var have6 = Normalize(state.Ipv6Servers);
            if (!have4.SequenceEqual(want4)) return "IPv4 hiện \"" + string.Join(", ", have4) + "\" thay vì \"" + string.Join(", ", want4) + "\"";
            if (!have6.SequenceEqual(want6))
                return want6.Count == 0 ? "DNS IPv6 chưa được xoá (" + string.Join(", ", have6) + ")" : "IPv6 hiện \"" + string.Join(", ", have6) + "\" thay vì \"" + string.Join(", ", want6) + "\"";
            return null;
        }

        private static List<string> Normalize(IEnumerable<string> addrs)
        {
            var l = new List<string>();
            if (addrs == null) return l;
            foreach (var a in addrs)
            {
                System.Net.IPAddress ip;
                if (System.Net.IPAddress.TryParse((a ?? "").Trim(), out ip)) l.Add(ip.ToString());
            }
            return l;
        }

        public static string FirstError(IEnumerable<CommandResult> results)
        {
            var bad = results.FirstOrDefault(r => !r.Ok);
            return bad != null ? (string.IsNullOrEmpty(bad.Output) ? bad.Command : bad.Output) : "?";
        }

        /// <summary>Khôi phục cấu hình DNS đã sao lưu (dùng tên card lúc sao lưu).</summary>
        public static List<CommandResult> Restore(AdapterDnsState s, Action<string> log)
        {
            return Restore(s, s.AdapterName, log);
        }

        /// <summary>Khôi phục cấu hình DNS đã sao lưu lên card có tên hiện tại (card có thể đã bị đổi tên).</summary>
        public static List<CommandResult> Restore(AdapterDnsState s, string currentInterfaceName, Action<string> log)
        {
            string name = string.IsNullOrWhiteSpace(currentInterfaceName) ? s.AdapterName : currentInterfaceName;
            var results = new List<CommandResult>();
            if (s.Ipv4Static) results.AddRange(SetStatic(name, "ipv4", s.Ipv4Servers, log));
            else results.Add(SetDhcp(name, "ipv4", log));

            if (s.Ipv6Static) results.AddRange(SetStatic(name, "ipv6", s.Ipv6Servers, log));
            else results.Add(SetDhcp(name, "ipv6", log));
            return results;
        }

        public static List<CommandResult> SetStatic(string ifaceName, string family, IList<string> servers, Action<string> log)
        {
            var results = new List<CommandResult>();
            string q = Quote(ifaceName);
            if (servers == null || servers.Count == 0)
            {
                results.Add(RunNetsh("interface " + family + " set dnsservers name=" + q + " source=static address=none validate=no", log));
                return results;
            }
            results.Add(RunNetsh("interface " + family + " set dnsservers name=" + q + " source=static address=" + servers[0] + " validate=no", log));
            for (int i = 1; i < servers.Count; i++)
            {
                results.Add(RunNetsh("interface " + family + " add dnsservers name=" + q + " address=" + servers[i] + " index=" + (i + 1) + " validate=no", log));
            }
            return results;
        }

        public static CommandResult SetDhcp(string ifaceName, string family, Action<string> log)
        {
            return RunNetsh("interface " + family + " set dnsservers name=" + Quote(ifaceName) + " source=dhcp", log);
        }

        public static CommandResult FlushDns(Action<string> log)
        {
            return Run(Path.Combine(Environment.SystemDirectory, "ipconfig.exe"), "/flushdns", log);
        }

        public static CommandResult RunNetsh(string args, Action<string> log)
        {
            return Run(Path.Combine(Environment.SystemDirectory, "netsh.exe"), args, log);
        }

        private static string Quote(string s)
        {
            return "\"" + (s ?? "").Replace("\"", "") + "\"";
        }

        public static CommandResult Run(string exe, string args, Action<string> log)
        {
            return Run(exe, args, log, 30000);
        }

        public static CommandResult Run(string exe, string args, Action<string> log, int timeoutMs)
        {
            var r = new CommandResult { Command = Path.GetFileName(exe) + " " + args, Output = "" };
            if (log != null) log("> " + r.Command);
            var runner = CommandRunner;
            if (runner != null)
            {
                try
                {
                    var sim = runner(exe, args);
                    r.ExitCode = sim != null ? sim.ExitCode : -1;
                    r.Output = sim != null && sim.Output != null ? sim.Output : "";
                }
                catch (Exception ex)
                {
                    r.ExitCode = -1;
                    r.Output = ex.Message;
                }
                if (log != null && !r.Ok) log("  => lỗi (mã " + r.ExitCode + ")");
                return r;
            }
            try
            {
                var enc = OemEncoding();
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = enc,
                    StandardErrorEncoding = enc
                };
                using (var proc = Process.Start(psi))
                {
                    var errTask = Task.Run(() => proc.StandardError.ReadToEnd());
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = errTask.Wait(timeoutMs) ? errTask.Result : "";
                    if (!proc.WaitForExit(timeoutMs))
                    {
                        try { proc.Kill(); } catch (Exception) { }
                        r.ExitCode = -1;
                        r.Output = "Quá thời gian chờ lệnh";
                        return r;
                    }
                    r.ExitCode = proc.ExitCode;
                    r.Output = (stdout + Environment.NewLine + stderr).Trim();
                }
            }
            catch (Exception ex)
            {
                r.ExitCode = -1;
                r.Output = ex.Message;
            }
            if (log != null)
            {
                if (!string.IsNullOrEmpty(r.Output)) log("  " + r.Output.Replace("\r\n", " / ").Replace("\n", " / "));
                if (!r.Ok) log("  => lỗi (mã " + r.ExitCode + ")");
            }
            return r;
        }

        private static Encoding OemEncoding()
        {
            try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
            catch (Exception) { return Encoding.Default; }
        }
    }
}
