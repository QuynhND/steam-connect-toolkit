using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using SteamConnectToolkit.Models;

namespace SteamConnectToolkit.Services
{
    public class TestOptions
    {
        public int DnsTimeoutMs { get; set; }
        public int HttpsTimeoutMs { get; set; }
        public int Concurrency { get; set; }
        public bool TestIpv6 { get; set; }

        public TestOptions()
        {
            DnsTimeoutMs = 2500;
            HttpsTimeoutMs = 6000;
            Concurrency = 4;
            TestIpv6 = true;
        }

        public static TestOptions FromConfig(AppConfig cfg)
        {
            return new TestOptions
            {
                DnsTimeoutMs = cfg.DnsTimeoutMs,
                HttpsTimeoutMs = cfg.HttpsTimeoutMs,
                Concurrency = cfg.Concurrency,
                TestIpv6 = true
            };
        }
    }

    /// <summary>Kiểm tra từng nhà cung cấp DNS với toàn bộ tên miền Steam và xếp hạng.</summary>
    public static class ProviderTester
    {
        /// <summary>Kiểm tra một nhà cung cấp; lỗi bất ngờ được ghi vào Error thay vì làm hỏng cả đợt kiểm tra.</summary>
        public static ProviderTestResult Test(DnsProvider p, TestOptions opt, CancellationToken ct)
        {
            try
            {
                return TestCore(p, opt, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new ProviderTestResult { Provider = p, TotalDomains = SteamChecker.Domains.Length, Error = ex.GetType().Name + ": " + ex.Message };
            }
        }

        private static ProviderTestResult TestCore(DnsProvider p, TestOptions opt, CancellationToken ct)
        {
            var result = new ProviderTestResult { Provider = p, TotalDomains = SteamChecker.Domains.Length };
            var v4 = ParseAll(p.GetIpv4Servers());
            var v6 = ParseAll(p.GetIpv6Servers());
            var servers = v4.Count > 0 ? v4 : v6;
            if (servers.Count == 0)
            {
                result.Error = "Không có địa chỉ hợp lệ";
                return result;
            }

            var tasks = new List<Task<DomainCheckResult>>();
            foreach (var host in SteamChecker.Domains)
            {
                string h = host;
                tasks.Add(Task.Run(() => CheckDomainWithFallback(servers, h, opt, ct), ct));
            }
            try
            {
                Task.WaitAll(tasks.ToArray());
            }
            catch (AggregateException)
            {
                if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
            }
            foreach (var t in tasks)
            {
                if (t.Status == TaskStatus.RanToCompletion && t.Result != null) result.Domains.Add(t.Result);
            }

            var dnsOk = result.Domains.Where(d => d.DnsOk).ToList();
            var httpsOk = result.Domains.Where(d => d.HttpsOk).ToList();
            result.DnsOkCount = dnsOk.Count;
            result.HttpsOkCount = httpsOk.Count;
            result.AvgDnsMs = dnsOk.Count > 0 ? dnsOk.Average(d => d.DnsMs) : 0;
            result.AvgHttpsMs = httpsOk.Count > 0 ? httpsOk.Average(d => d.HttpsMs) : 0;

            // Kiểm tra máy chủ IPv6 có trả lời không (chỉ khi có cả IPv4 và IPv6).
            if (opt.TestIpv6 && v4.Count > 0 && v6.Count > 0 && !ct.IsCancellationRequested)
            {
                result.Ipv6Tested = true;
                DnsQueryResult q = null;
                foreach (var s in v6)
                {
                    q = DnsResolver.Query(s, SteamChecker.Domains[0], DnsRecordType.A, opt.DnsTimeoutMs);
                    if (q.Success) break;
                }
                result.Ipv6Reachable = q != null && q.Success;
                result.Ipv6Error = q != null ? q.Error : null;
            }
            return result;
        }

        private static DomainCheckResult CheckDomainWithFallback(List<IPAddress> servers, string host, TestOptions opt, CancellationToken ct)
        {
            DomainCheckResult last = null;
            foreach (var s in servers)
            {
                ct.ThrowIfCancellationRequested();
                var r = SteamChecker.CheckViaServer(s, host, opt.DnsTimeoutMs, opt.HttpsTimeoutMs);
                if (r.DnsOk) return r; // DNS trả lời được: dùng kết quả này (kể cả HTTPS lỗi)
                last = r;
            }
            return last;
        }

        public static async Task<List<ProviderTestResult>> TestManyAsync(
            IList<DnsProvider> providers, TestOptions opt, Action<ProviderTestResult> onResult, CancellationToken ct)
        {
            var sem = new SemaphoreSlim(Math.Max(1, opt.Concurrency));
            var tasks = providers.Select(async p =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var r = await Task.Run(() => Test(p, opt, ct), ct);
                    if (onResult != null) onResult(r);
                    return r;
                }
                finally
                {
                    sem.Release();
                }
            }).ToList();
            var arr = await Task.WhenAll(tasks);
            return arr.ToList();
        }

        /// <summary>Xếp hạng: nhiều HTTPS OK nhất, rồi DNS OK, rồi độ trễ thấp nhất.</summary>
        public static List<ProviderTestResult> Rank(IEnumerable<ProviderTestResult> results)
        {
            return results
                .OrderByDescending(r => r.HttpsOkCount)
                .ThenByDescending(r => r.DnsOkCount)
                .ThenBy(r => r.AvgTotalMs > 0 ? r.AvgTotalMs : double.MaxValue)
                .ToList();
        }

        /// <summary>Ghi kết quả vào đối tượng provider để lưu/hiển thị.</summary>
        public static void ApplyToProvider(ProviderTestResult r)
        {
            if (r == null || r.Provider == null) return;
            r.Provider.LastTestSummary = r.StatusText() + " (HTTPS " + r.HttpsOkCount + "/" + r.TotalDomains + ")";
            r.Provider.LastScore = r.Score;
            r.Provider.LastLatencyMs = Math.Round(r.AvgTotalMs);
            r.Provider.LastTestedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        }

        private static List<IPAddress> ParseAll(IEnumerable<string> values)
        {
            var list = new List<IPAddress>();
            foreach (var v in values)
            {
                IPAddress ip;
                if (IPAddress.TryParse(v, out ip)) list.Add(ip);
            }
            return list;
        }
    }
}
