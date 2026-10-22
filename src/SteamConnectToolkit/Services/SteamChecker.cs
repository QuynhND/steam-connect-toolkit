using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using SteamConnectToolkit.Models;

namespace SteamConnectToolkit.Services
{
    /// <summary>Kiểm tra các tên miền Steam: phân giải DNS + bắt tay HTTPS.</summary>
    public static class SteamChecker
    {
        public static readonly string[] Domains =
        {
            "store.steampowered.com",
            "steamcommunity.com",
            "api.steampowered.com",
            "login.steampowered.com",
            "help.steampowered.com"
        };

        /// <summary>Kiểm tra bằng DNS hiện tại của hệ thống (resolver Windows).</summary>
        public static DomainCheckResult CheckViaSystem(string host, int dnsTimeoutMs, int httpsTimeoutMs)
        {
            var r = new DomainCheckResult { Host = host, Resolver = "system" };
            var sw = Stopwatch.StartNew();
            IPAddress[] addrs = null;
            try
            {
                var t = Task.Run(() => Dns.GetHostAddresses(host));
                // Resolver hệ thống có thể thử nhiều máy chủ, cho thời gian gấp đôi.
                if (!t.Wait(dnsTimeoutMs * 2)) r.DnsError = "Hết thời gian chờ";
                else addrs = t.Result;
            }
            catch (AggregateException ex)
            {
                var inner = ex.InnerException ?? ex;
                var se = inner as SocketException;
                r.DnsError = se != null ? HttpsProbe.DescribeSocketError(se) : inner.Message;
            }
            catch (Exception ex)
            {
                r.DnsError = ex.Message;
            }
            r.DnsMs = sw.Elapsed.TotalMilliseconds;

            if (addrs != null && addrs.Length > 0)
            {
                r.DnsOk = true;
                r.DnsError = null;
                r.Ips = addrs.Select(a => a.ToString()).ToList();
            }
            else if (string.IsNullOrEmpty(r.DnsError))
            {
                r.DnsError = "Không phân giải được";
            }
            if (!r.DnsOk) return r;

            ProbeHttps(r, addrs, host, httpsTimeoutMs);
            return r;
        }

        /// <summary>Kiểm tra bằng một máy chủ DNS cụ thể (không đụng tới cấu hình hệ thống).</summary>
        public static DomainCheckResult CheckViaServer(IPAddress dnsServer, string host, int dnsTimeoutMs, int httpsTimeoutMs)
        {
            var r = new DomainCheckResult { Host = host, Resolver = dnsServer.ToString() };
            var q = DnsResolver.Query(dnsServer, host, DnsRecordType.A, dnsTimeoutMs);
            r.DnsMs = q.ElapsedMs;
            if (!q.Success)
            {
                r.DnsError = q.Error;
                return r;
            }
            r.DnsOk = true;
            r.Ips = q.Addresses.Select(a => a.ToString()).ToList();
            ProbeHttps(r, q.Addresses.ToArray(), host, httpsTimeoutMs);
            return r;
        }

        private static void ProbeHttps(DomainCheckResult r, IPAddress[] addrs, string host, int timeoutMs)
        {
            // Ưu tiên IPv4 để so sánh công bằng giữa các DNS.
            var v4 = addrs.Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToList();
            var candidates = v4.Count > 0 ? v4 : addrs.ToList();
            // Tối đa 2 lần thử: IP thứ nhất, rồi IP thứ hai (nếu có) hoặc thử lại IP thứ nhất.
            // Không thử lại khi lỗi chứng chỉ (kết quả không đổi).
            var attempts = new List<IPAddress> { candidates[0], candidates.Count > 1 ? candidates[1] : candidates[0] };
            HttpsProbeResult p = null;
            double spent = 0;
            foreach (var ip in attempts)
            {
                p = HttpsProbe.Probe(ip, host, timeoutMs);
                spent += p.TotalMs;
                if (p.TlsOk && p.HttpOk) break;
                if (!string.IsNullOrEmpty(p.PolicyErrors)) break;
            }
            r.HttpsMs = p.TlsOk && p.HttpOk ? p.TotalMs : spent;
            r.HttpStatus = p.StatusCode;
            r.CertSubject = p.CertSubject;
            r.TlsProtocol = p.Protocol;
            r.HttpsOk = p.TlsOk && p.HttpOk;
            if (!r.HttpsOk) r.HttpsError = p.Error ?? "lỗi không xác định";
        }
    }
}
