using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace SteamConnectToolkit.Services
{
    public class HttpsProbeResult
    {
        public string Host { get; set; }
        public string Ip { get; set; }
        public bool TcpOk { get; set; }
        public bool TlsOk { get; set; }
        public bool HttpOk { get; set; }
        public double TcpMs { get; set; }
        public double TlsMs { get; set; }
        public double TotalMs { get; set; }
        public int StatusCode { get; set; }
        public string Error { get; set; }
        public string CertSubject { get; set; }
        public string CertIssuer { get; set; }
        public string Protocol { get; set; }
        public string PolicyErrors { get; set; }
        public bool ProtocolUnsupported { get; set; }
    }

    /// <summary>
    /// Kiểm tra HTTPS tới một IP cụ thể với SNI = tên miền và xác thực chứng chỉ đầy đủ.
    /// Phát hiện được: chặn TCP 443, reset khi bắt tay TLS, DNS trả IP giả (chứng chỉ sai), v.v.
    /// </summary>
    public static class HttpsProbe
    {
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) SteamConnectToolkit/1.0";

        // SslProtocols.None = để Windows chọn (TLS 1.2/1.3). Nếu runtime không chấp nhận None thì dùng TLS 1.2 cố định.
        private static volatile bool _forceTls12;

        /// <summary>Gọi một lần ở đầu chương trình để .NET dùng TLS mặc định của hệ điều hành thay vì TLS 1.0.</summary>
        public static void ConfigureTls()
        {
            try
            {
                AppContext.SetSwitch("Switch.System.Net.DontEnableSystemDefaultTlsVersions", false);
                AppContext.SetSwitch("Switch.System.Net.DontEnableSchUseStrongCrypto", false);
            }
            catch (Exception) { }
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch (Exception) { }
        }

        public static HttpsProbeResult Probe(IPAddress ip, string host, int timeoutMs)
        {
            var r = ProbeOnce(ip, host, timeoutMs, _forceTls12 ? SslProtocols.Tls12 : SslProtocols.None);
            if (r.ProtocolUnsupported && !_forceTls12)
            {
                _forceTls12 = true;
                r = ProbeOnce(ip, host, timeoutMs, SslProtocols.Tls12);
            }
            return r;
        }

        private static HttpsProbeResult ProbeOnce(IPAddress ip, string host, int timeoutMs, SslProtocols protocols)
        {
            var r = new HttpsProbeResult { Host = host, Ip = ip.ToString() };
            var sw = Stopwatch.StartNew();
            TcpClient tcp = null;
            try
            {
                tcp = new TcpClient(ip.AddressFamily);
                var ar = tcp.Client.BeginConnect(new IPEndPoint(ip, 443), null, null);
                if (!ar.AsyncWaitHandle.WaitOne(timeoutMs, true))
                {
                    r.Error = "TCP 443: hết thời gian chờ (" + timeoutMs + " ms)";
                    return r;
                }
                tcp.Client.EndConnect(ar);
                r.TcpOk = true;
                r.TcpMs = sw.Elapsed.TotalMilliseconds;
                tcp.ReceiveTimeout = timeoutMs;
                tcp.SendTimeout = timeoutMs;

                SslPolicyErrors policy = SslPolicyErrors.None;
                using (var ssl = new SslStream(tcp.GetStream(), false,
                    (sender, cert, chain, errors) => { policy = errors; return errors == SslPolicyErrors.None; }))
                {
                    try
                    {
                        ssl.AuthenticateAsClient(host, null, protocols, false);
                    }
                    catch (AuthenticationException ex)
                    {
                        r.PolicyErrors = policy.ToString();
                        r.Error = policy != SslPolicyErrors.None
                            ? "TLS: chứng chỉ không hợp lệ (" + policy + ")"
                            : "TLS: " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message);
                        return r;
                    }
                    catch (NotSupportedException ex)
                    {
                        r.ProtocolUnsupported = true;
                        r.Error = "TLS: giao thức không được hỗ trợ (" + ex.Message + ")";
                        return r;
                    }
                    catch (ArgumentException ex)
                    {
                        r.ProtocolUnsupported = true;
                        r.Error = "TLS: giao thức không được hỗ trợ (" + ex.Message + ")";
                        return r;
                    }
                    r.TlsOk = true;
                    r.TlsMs = sw.Elapsed.TotalMilliseconds;
                    r.Protocol = ssl.SslProtocol.ToString();
                    try
                    {
                        var raw = ssl.RemoteCertificate;
                        if (raw != null)
                        {
                            var cert2 = raw as X509Certificate2 ?? new X509Certificate2(raw);
                            r.CertSubject = cert2.GetNameInfo(X509NameType.SimpleName, false);
                            r.CertIssuer = cert2.GetNameInfo(X509NameType.SimpleName, true);
                        }
                    }
                    catch (Exception) { }

                    string request = "HEAD / HTTP/1.1\r\nHost: " + host + "\r\nUser-Agent: " + UserAgent +
                                     "\r\nAccept: */*\r\nConnection: close\r\n\r\n";
                    var bytes = Encoding.ASCII.GetBytes(request);
                    ssl.Write(bytes, 0, bytes.Length);
                    ssl.Flush();

                    var buf = new byte[4096];
                    int total = 0;
                    while (total < buf.Length)
                    {
                        int n = ssl.Read(buf, total, buf.Length - total);
                        if (n <= 0) break;
                        total += n;
                        string text = Encoding.ASCII.GetString(buf, 0, total);
                        int eol = text.IndexOf("\r\n", StringComparison.Ordinal);
                        if (eol >= 0)
                        {
                            r.StatusCode = ParseStatus(text.Substring(0, eol));
                            break;
                        }
                    }
                    r.HttpOk = r.StatusCode > 0;
                    if (!r.HttpOk) r.Error = "Không nhận được phản hồi HTTP hợp lệ";
                }
            }
            catch (SocketException ex)
            {
                r.Error = "TCP 443: " + DescribeSocketError(ex);
            }
            catch (IOException ex)
            {
                var inner = ex.InnerException as SocketException;
                r.Error = inner != null ? "Kết nối bị ngắt: " + DescribeSocketError(inner) : "I/O: " + ex.Message;
            }
            catch (Exception ex)
            {
                r.Error = ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                r.TotalMs = sw.Elapsed.TotalMilliseconds;
                if (tcp != null)
                {
                    try { tcp.Close(); } catch (Exception) { }
                }
            }
            return r;
        }

        private static int ParseStatus(string statusLine)
        {
            // "HTTP/1.1 200 OK"
            var parts = statusLine.Split(' ');
            int code;
            if (parts.Length >= 2 && parts[0].StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase) && int.TryParse(parts[1], out code))
                return code;
            return 0;
        }

        public static string DescribeSocketError(SocketException ex)
        {
            switch (ex.SocketErrorCode)
            {
                case SocketError.TimedOut: return "hết thời gian chờ";
                case SocketError.ConnectionRefused: return "bị từ chối kết nối";
                case SocketError.ConnectionReset: return "bị reset (có thể do chặn)";
                case SocketError.HostUnreachable: return "không tới được máy chủ";
                case SocketError.NetworkUnreachable: return "không có đường mạng";
                case SocketError.HostNotFound: return "không tìm thấy tên miền";
                case SocketError.NoData: return "không có dữ liệu DNS";
                case SocketError.TryAgain: return "DNS tạm thời lỗi";
                default: return ex.SocketErrorCode.ToString();
            }
        }
    }
}
