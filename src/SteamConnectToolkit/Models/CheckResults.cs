using System;
using System.Collections.Generic;
using System.Linq;

namespace SteamConnectToolkit.Models
{
    /// <summary>Kết quả kiểm tra một tên miền Steam: DNS + HTTPS.</summary>
    public class DomainCheckResult
    {
        public string Host { get; set; }
        /// <summary>"system" hoặc địa chỉ máy chủ DNS đã dùng.</summary>
        public string Resolver { get; set; }

        public bool DnsOk { get; set; }
        public double DnsMs { get; set; }
        public string DnsError { get; set; }
        public List<string> Ips { get; set; }

        public bool HttpsOk { get; set; }
        public double HttpsMs { get; set; }
        public int HttpStatus { get; set; }
        public string HttpsError { get; set; }
        public string CertSubject { get; set; }
        public string TlsProtocol { get; set; }

        public DomainCheckResult()
        {
            Ips = new List<string>();
        }

        public bool Ok { get { return DnsOk && HttpsOk; } }
        public double TotalMs { get { return DnsMs + HttpsMs; } }

        public string DnsText()
        {
            return DnsOk ? "OK " + Math.Round(DnsMs) + " ms" : "LỖI: " + (DnsError ?? "?");
        }

        public string HttpsText()
        {
            if (!DnsOk) return "(bỏ qua)";
            if (HttpsOk) return "OK " + (HttpStatus > 0 ? "HTTP " + HttpStatus + " " : "") + Math.Round(HttpsMs) + " ms";
            return "LỖI: " + (HttpsError ?? "?");
        }

        public string Summary()
        {
            return Host + ": DNS " + DnsText() + (Ips.Count > 0 ? " -> " + string.Join(", ", Ips.Take(2)) : "") + " | HTTPS " + HttpsText();
        }
    }

    /// <summary>Kết quả kiểm tra một nhà cung cấp DNS với toàn bộ tên miền Steam.</summary>
    public class ProviderTestResult
    {
        public DnsProvider Provider { get; set; }
        public List<DomainCheckResult> Domains { get; set; }
        public int DnsOkCount { get; set; }
        public int HttpsOkCount { get; set; }
        public int TotalDomains { get; set; }
        public double AvgDnsMs { get; set; }
        public double AvgHttpsMs { get; set; }
        public bool Ipv6Tested { get; set; }
        public bool Ipv6Reachable { get; set; }
        public string Ipv6Error { get; set; }
        public string Error { get; set; }

        public ProviderTestResult()
        {
            Domains = new List<DomainCheckResult>();
        }

        /// <summary>Điểm: mỗi tên miền HTTPS OK = 3, DNS OK = 2 (tối đa 25 với 5 tên miền).</summary>
        public double Score { get { return HttpsOkCount * 3 + DnsOkCount * 2; } }
        public double AvgTotalMs { get { return AvgDnsMs + AvgHttpsMs; } }
        public bool AllOk { get { return TotalDomains > 0 && HttpsOkCount == TotalDomains; } }
        public bool AnyOk { get { return HttpsOkCount > 0; } }

        public string StatusText()
        {
            if (!string.IsNullOrEmpty(Error)) return "LỖI: " + Error;
            if (AllOk) return "Tốt";
            if (AnyOk) return "Một phần";
            if (DnsOkCount > 0) return "DNS OK, HTTPS lỗi";
            return "Không hoạt động";
        }

        public string Summary()
        {
            string s = Provider.Name + ": DNS " + DnsOkCount + "/" + TotalDomains + ", HTTPS " + HttpsOkCount + "/" + TotalDomains
                + ", trễ TB " + Math.Round(AvgDnsMs) + " + " + Math.Round(AvgHttpsMs) + " ms";
            if (Ipv6Tested) s += ", IPv6 " + (Ipv6Reachable ? "OK" : "lỗi");
            return s;
        }
    }
}
