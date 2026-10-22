using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using SteamConnectToolkit.Models;

namespace SteamConnectToolkit.Services
{
    /// <summary>Một máy chủ trong danh sách VPN Gate (vpngate.net, Đại học Tsukuba, Nhật Bản).</summary>
    public class VpnGateServer
    {
        public string HostName { get; set; }
        public string Ip { get; set; }
        public string CountryCode { get; set; }
        public string CountryName { get; set; }
        public int PingMs { get; set; }
        public double SpeedMbps { get; set; }
        public int Sessions { get; set; }
        public long Score { get; set; }
        public string Uptime { get; set; }
        public string LogPolicy { get; set; }
        public bool HasL2tp { get; set; }
        public bool HasSstp { get; set; }
        /// <summary>Hostname SSTP (*.opengw.net), có thể kèm ":cổng" nếu khác 443.</summary>
        public string SstpHost { get; set; }
        public int SstpPort { get; set; }
        /// <summary>Thời gian TCP connect tới cổng SSTP/SSL (ms); -1 = chưa đo; -2 = không tới được.</summary>
        public double ProbeMs { get; set; }

        public VpnGateServer()
        {
            PingMs = -1;
            ProbeMs = -1;
            SstpPort = 443;
        }

        public bool IsOfficial { get { return (HostName ?? "").StartsWith("public-vpn-", StringComparison.OrdinalIgnoreCase); } }
        public bool Reachable { get { return ProbeMs >= 0; } }

        /// <summary>Giao thức dùng được với VPN client Windows: SSTP (ưu tiên) hoặc L2TP/IPsec.</summary>
        public string PreferredProtocol(bool preferSstp)
        {
            bool sstp = HasSstp && !string.IsNullOrEmpty(SstpHost);
            if (preferSstp && sstp) return "Sstp";
            if (HasL2tp) return "L2tp";
            if (sstp) return "Sstp";
            return null;
        }

        public string ProtocolsText()
        {
            var l = new List<string>();
            if (HasSstp) l.Add("SSTP" + (SstpPort != 443 ? ":" + SstpPort : ""));
            if (HasL2tp) l.Add("L2TP");
            return l.Count > 0 ? string.Join(" + ", l) : "(OpenVPN/SoftEther)";
        }

        public string ProfileName()
        {
            string h = HostName ?? Ip;
            int dot = h.IndexOf('.');
            if (dot > 0) h = h.Substring(0, dot);
            return "VPN Gate " + CountryCode + " " + h;
        }

        /// <summary>Địa chỉ máy chủ ghi vào cấu hình: SSTP bắt buộc dùng hostname *.opengw.net (chứng chỉ), L2TP dùng IP.</summary>
        public string ServerAddressFor(string protocol)
        {
            return protocol == "Sstp" ? SstpHost : Ip;
        }

        public override string ToString()
        {
            return CountryCode + " " + HostName + " (" + Ip + ")";
        }
    }

    /// <summary>
    /// Tìm và xếp hạng máy chủ VPN công cộng miễn phí của dự án VPN Gate để dùng với VPN client có sẵn của Windows.
    /// Tài khoản chung: vpn / vpn; L2TP pre-shared key: vpn. Máy chủ ghi log theo chính sách của dự án (thường 2 tuần).
    /// </summary>
    public static class VpnGateService
    {
        public const string SourceName = "VPN Gate (vpngate.net)";
        public const string ListUrl = "https://www.vpngate.net/en/";
        public const string Username = "vpn";
        public const string Password = "vpn";
        public const string L2tpPsk = "vpn";

        /// <summary>Quốc gia gần Việt Nam (độ trễ thấp) được ưu tiên.</summary>
        private static readonly string[] NearCountries = { "VN", "TH", "SG", "HK", "TW", "JP", "KR", "MY", "ID", "PH" };

        /// <summary>Hook kiểm thử: thay việc tải HTML thật.</summary>
        public static Func<string> HtmlFetcher;

        public static string FetchHtml(int timeoutMs, Action<string> log)
        {
            var fetcher = HtmlFetcher;
            if (fetcher != null) return fetcher();
            if (log != null) log("Tải danh sách máy chủ VPN Gate: " + ListUrl);
            var req = (HttpWebRequest)WebRequest.Create(ListUrl);
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) SteamConnectToolkit/1.0";
            req.Accept = "text/html";
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                return sr.ReadToEnd();
            }
        }

        public static List<VpnGateServer> Fetch(int timeoutMs, Action<string> log)
        {
            string html = FetchHtml(timeoutMs, log);
            var list = ParseHtml(html);
            if (log != null) log("VPN Gate: " + list.Count + " máy chủ, " + list.Count(s => s.HasSstp) + " hỗ trợ SSTP, " + list.Count(s => s.HasL2tp) + " hỗ trợ L2TP/IPsec.");
            return list;
        }

        private static readonly Regex RowRegex = new Regex(@"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        private static readonly Regex CellRegex = new Regex(@"<td[^>]*>(.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        private static readonly Regex TagRegex = new Regex(@"<[^>]+>", RegexOptions.Singleline);
        private static readonly Regex BrRegex = new Regex(@"<br\s*/?>", RegexOptions.IgnoreCase);
        private static readonly Regex IpRegex = new Regex(@"\b(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\b");
        private static readonly Regex FlagRegex = new Regex(@"flags/([A-Za-z]{2})\.png", RegexOptions.IgnoreCase);

        /// <summary>Đọc bảng máy chủ trên trang vpngate.net (hàm thuần, dùng cho kiểm thử).</summary>
        public static List<VpnGateServer> ParseHtml(string html)
        {
            var list = new List<VpnGateServer>();
            if (string.IsNullOrEmpty(html)) return list;
            foreach (Match row in RowRegex.Matches(html))
            {
                string rowHtml = row.Groups[1].Value;
                if (!IpRegex.IsMatch(rowHtml)) continue;
                var cells = CellRegex.Matches(rowHtml).Cast<Match>().Select(m => CleanCell(m.Groups[1].Value)).ToList();
                if (cells.Count < 10) continue;

                var s = new VpnGateServer();
                var flag = FlagRegex.Match(rowHtml);
                s.CountryCode = flag.Success ? flag.Groups[1].Value.ToUpperInvariant() : "";
                s.CountryName = cells[0].Replace("|", "").Trim();

                var ipm = IpRegex.Match(cells[1]);
                if (!ipm.Success) continue;
                s.Ip = ipm.Groups[1].Value;
                var hm = Regex.Match(cells[1], @"^([a-z0-9\-\.]+)\s*\|", RegexOptions.IgnoreCase);
                s.HostName = hm.Success ? hm.Groups[1].Value : s.Ip;

                var sm = Regex.Match(cells[2], @"(\d+)\s*sessions", RegexOptions.IgnoreCase);
                if (sm.Success) s.Sessions = ParseInt(sm.Groups[1].Value);
                var um = Regex.Match(cells[2], @"\|\s*([^|]+?)\s*\|\s*Total", RegexOptions.IgnoreCase);
                s.Uptime = um.Success ? um.Groups[1].Value.Trim() : "";

                var pm = Regex.Match(cells[3], @"Ping:\s*(\d+)\s*ms", RegexOptions.IgnoreCase);
                if (pm.Success) s.PingMs = ParseInt(pm.Groups[1].Value);
                var spm = Regex.Match(cells[3], @"([\d\.,]+)\s*Mbps", RegexOptions.IgnoreCase);
                if (spm.Success) s.SpeedMbps = ParseDouble(spm.Groups[1].Value);
                var lm = Regex.Match(cells[3], @"Logging policy:\s*\|?\s*(.+)$", RegexOptions.IgnoreCase);
                s.LogPolicy = lm.Success ? lm.Groups[1].Value.Trim() : "";

                s.HasL2tp = cells[5].IndexOf("L2TP", StringComparison.OrdinalIgnoreCase) >= 0;
                s.HasSstp = cells[7].IndexOf("MS-SSTP", StringComparison.OrdinalIgnoreCase) >= 0;
                var shm = Regex.Match(cells[7], @"SSTP Hostname\s*:\s*\|?\s*([a-z0-9\-\.]+(?::\d+)?)", RegexOptions.IgnoreCase);
                if (shm.Success)
                {
                    s.SstpHost = shm.Groups[1].Value;
                    int colon = s.SstpHost.LastIndexOf(':');
                    if (colon > 0) s.SstpPort = ParseInt(s.SstpHost.Substring(colon + 1));
                }
                var scm = Regex.Match(cells[9].Replace(",", ""), @"(\d+)");
                if (scm.Success) s.Score = ParseLong(scm.Groups[1].Value);
                list.Add(s);
            }
            return list;
        }

        private static string CleanCell(string html)
        {
            string t = BrRegex.Replace(html, " | ");
            t = TagRegex.Replace(t, "");
            t = t.Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&quot;", "\"").Replace("&#39;", "'");
            t = Regex.Replace(t, @"\s{2,}", " ");
            return t.Trim();
        }

        /// <summary>Đo TCP connect tới cổng SSTP (hoặc 443) để biết máy chủ có tới được từ mạng hiện tại không.</summary>
        public static void Probe(VpnGateServer s, int timeoutMs)
        {
            int port = s.HasSstp && s.SstpPort > 0 ? s.SstpPort : 443;
            var sw = Stopwatch.StartNew();
            TcpClient tcp = null;
            try
            {
                IPAddress ip;
                if (!IPAddress.TryParse(s.Ip, out ip)) { s.ProbeMs = -2; return; }
                tcp = new TcpClient(ip.AddressFamily);
                var ar = tcp.Client.BeginConnect(new IPEndPoint(ip, port), null, null);
                if (!ar.AsyncWaitHandle.WaitOne(timeoutMs, true)) { s.ProbeMs = -2; return; }
                tcp.Client.EndConnect(ar);
                s.ProbeMs = sw.Elapsed.TotalMilliseconds;
            }
            catch (Exception)
            {
                s.ProbeMs = -2;
            }
            finally
            {
                if (tcp != null) { try { tcp.Close(); } catch (Exception) { } }
            }
        }

        /// <summary>
        /// Xếp hạng theo mức tương thích với VPN Windows và chất lượng:
        /// tới được (nếu đã đo) → có SSTP (cổng 443, không vướng NAT) → quốc gia gần → máy chủ chính thức (public-vpn-*) → điểm → độ trễ.
        /// </summary>
        public static List<VpnGateServer> Rank(IEnumerable<VpnGateServer> servers, bool preferSstp)
        {
            return servers
                .Where(s => s.PreferredProtocol(preferSstp) != null)
                .OrderByDescending(s => s.ProbeMs == -2 ? 0 : 1)
                .ThenByDescending(s => preferSstp && s.HasSstp && !string.IsNullOrEmpty(s.SstpHost) ? 1 : 0)
                .ThenByDescending(s => Array.IndexOf(NearCountries, s.CountryCode) >= 0 ? 1 : 0)
                .ThenByDescending(s => s.IsOfficial ? 1 : 0)
                .ThenByDescending(s => s.Score)
                .ThenBy(s => s.ProbeMs >= 0 ? s.ProbeMs : double.MaxValue)
                .ToList();
        }

        /// <summary>Tạo thông tin cấu hình Windows VPN cho máy chủ VPN Gate.</summary>
        public static VpnProfileSpec ToProfileSpec(VpnGateServer s, bool preferSstp)
        {
            string proto = s.PreferredProtocol(preferSstp);
            if (proto == null) return null;
            return new VpnProfileSpec
            {
                Name = s.ProfileName(),
                Server = s.ServerAddressFor(proto),
                TunnelType = proto,
                L2tpPsk = proto == "L2tp" ? L2tpPsk : "",
                RememberCredential = true,
                AllUsers = false,
                SplitTunneling = false
            };
        }

        private static int ParseInt(string s)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        private static long ParseLong(string s)
        {
            long v;
            return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        private static double ParseDouble(string s)
        {
            double v;
            return double.TryParse(s.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0;
        }
    }
}
