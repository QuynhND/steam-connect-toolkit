using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SteamConnectToolkit.Models;

namespace SteamConnectToolkit.Services
{
    public class DiscoveredServer
    {
        public string Ip { get; set; }
        public string Name { get; set; }
        public string AsOrg { get; set; }
        public string Country { get; set; }
        public string City { get; set; }
        public double Reliability { get; set; }
        public bool Dnssec { get; set; }
        public bool IsIpv6 { get; set; }
    }

    /// <summary>Tìm thêm DNS công cộng từ danh sách mở public-dns.info (theo quốc gia).</summary>
    public static class DiscoveryService
    {
        public const string SourceName = "public-dns.info";
        public const int FetchTimeoutMs = 20000;

        /// <summary>WebClient mặc định không có timeout (100 s); giới hạn lại để UI không treo lâu khi mạng kém.</summary>
        private sealed class TimeoutWebClient : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address)
            {
                var req = base.GetWebRequest(address);
                if (req != null) req.Timeout = FetchTimeoutMs;
                var http = req as HttpWebRequest;
                if (http != null) http.ReadWriteTimeout = FetchTimeoutMs;
                return req;
            }
        }

        public static string CountryUrl(string countryCode)
        {
            return "https://public-dns.info/nameserver/" + countryCode.Trim().ToLowerInvariant() + ".csv";
        }

        public static List<DiscoveredServer> FetchCountry(string countryCode, double minReliability, int max, bool includeIpv6, Action<string> log)
        {
            string url = CountryUrl(countryCode);
            if (log != null) log("Tải danh sách DNS công cộng: " + url);
            string csv;
            using (var wc = new TimeoutWebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "SteamConnectToolkit/1.0";
                wc.Encoding = Encoding.UTF8;
                csv = wc.DownloadString(url);
            }
            var rows = ParseCsv(csv);
            var list = new List<DiscoveredServer>();
            if (rows.Count < 2) return list;

            var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
            int iIp = header.IndexOf("ip_address");
            int iName = header.IndexOf("name");
            int iOrg = header.IndexOf("as_org");
            int iCountry = header.IndexOf("country_code");
            int iCity = header.IndexOf("city");
            int iErr = header.IndexOf("error");
            int iDnssec = header.IndexOf("dnssec");
            int iRel = header.IndexOf("reliability");
            if (iIp < 0) throw new InvalidOperationException("Định dạng CSV không như mong đợi (thiếu cột ip_address)");

            for (int r = 1; r < rows.Count; r++)
            {
                var row = rows[r];
                if (row.Count <= iIp) continue;
                string ipText = row[iIp].Trim();
                IPAddress ip;
                if (!IPAddress.TryParse(ipText, out ip)) continue;
                bool v6 = ip.AddressFamily == AddressFamily.InterNetworkV6;
                if (v6 && !includeIpv6) continue;
                if (iErr >= 0 && iErr < row.Count && !string.IsNullOrWhiteSpace(row[iErr])) continue;

                double rel = 0;
                if (iRel >= 0 && iRel < row.Count) double.TryParse(row[iRel], NumberStyles.Float, CultureInfo.InvariantCulture, out rel);
                if (rel < minReliability) continue;

                list.Add(new DiscoveredServer
                {
                    Ip = ip.ToString(),
                    Name = Get(row, iName),
                    AsOrg = Get(row, iOrg),
                    Country = Get(row, iCountry),
                    City = Get(row, iCity),
                    Reliability = rel,
                    Dnssec = string.Equals(Get(row, iDnssec), "true", StringComparison.OrdinalIgnoreCase),
                    IsIpv6 = v6
                });
            }
            return list.OrderByDescending(x => x.Reliability).ThenBy(x => x.IsIpv6).Take(max).ToList();
        }

        public static DnsProvider ToProvider(DiscoveredServer s)
        {
            string label = !string.IsNullOrWhiteSpace(s.AsOrg) ? s.AsOrg : (!string.IsNullOrWhiteSpace(s.Name) ? s.Name : "DNS công cộng");
            if (label.Length > 40) label = label.Substring(0, 40);
            var p = new DnsProvider
            {
                Id = "disc-" + s.Ip.Replace(":", "_").Replace(".", "_"),
                Name = label + " (" + (s.Country ?? "?") + ")",
                Category = "discovered",
                Enabled = true,
                BuiltIn = false,
                Notes = "Nguồn: " + SourceName + ", độ tin cậy " + s.Reliability.ToString("0.00", CultureInfo.InvariantCulture)
                        + (string.IsNullOrWhiteSpace(s.City) ? "" : ", " + s.City)
            };
            if (s.IsIpv6) p.Ipv6Primary = s.Ip; else p.Ipv4Primary = s.Ip;
            return p;
        }

        private static string Get(List<string> row, int idx)
        {
            return idx >= 0 && idx < row.Count ? row[idx].Trim() : "";
        }

        /// <summary>Bộ phân tích CSV đơn giản (hỗ trợ dấu ngoặc kép và xuống dòng trong ô).</summary>
        public static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else cell.Append(c);
                    continue;
                }
                switch (c)
                {
                    case '"': inQuotes = true; break;
                    case ',': row.Add(cell.ToString()); cell.Length = 0; break;
                    case '\r': break;
                    case '\n':
                        row.Add(cell.ToString()); cell.Length = 0;
                        if (row.Count > 1 || (row.Count == 1 && row[0].Length > 0)) rows.Add(row);
                        row = new List<string>();
                        break;
                    default: cell.Append(c); break;
                }
            }
            if (cell.Length > 0 || row.Count > 0)
            {
                row.Add(cell.ToString());
                rows.Add(row);
            }
            return rows;
        }
    }
}
