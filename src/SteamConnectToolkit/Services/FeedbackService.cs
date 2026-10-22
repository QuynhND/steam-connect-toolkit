using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SteamConnectToolkit.Services
{
    /// <summary>
    /// Gửi phản hồi / báo lỗi: mở trang tạo issue trên GitHub với nội dung điền sẵn.
    /// Không dùng token (không có bí mật trong app); nội dung được lược bỏ IP, tên máy, tên người dùng, đường dẫn cá nhân.
    /// </summary>
    public static class FeedbackService
    {
        public const string IssuesUrl = AppInfo.RepositoryUrl + "/issues";
        public const string NewIssueUrl = AppInfo.RepositoryUrl + "/issues/new";
        /// <summary>Trình duyệt/GitHub giới hạn URL ~8 KB; giữ nội dung dưới mức này.</summary>
        public const int MaxUrlChars = 7800;
        public const int MaxBodyChars = 3500;

        private static readonly Regex Ipv4 = new Regex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b");
        // Ứng viên IPv6 (kể cả dạng rút gọn ::); chỉ coi là IPv6 khi có "::" hoặc ≥ 3 dấu ":" để không nhầm với giờ 15:00:01.
        private static readonly Regex Ipv6 = new Regex(@"(?<![\w:.])(?:[0-9a-fA-F]{0,4}:){2,7}[0-9a-fA-F]{0,4}(?![\w:.])");
        private static readonly Regex UserPath = new Regex(@"[A-Za-z]:\\Users\\[^\\\s]+", RegexOptions.IgnoreCase);

        private static string Canonical(string ip)
        {
            System.Net.IPAddress a;
            return System.Net.IPAddress.TryParse(ip, out a) ? a.ToString() : ip;
        }

        /// <summary>Địa chỉ DNS công cộng trong danh mục được giữ nguyên (không phải thông tin cá nhân).</summary>
        private static HashSet<string> KnownIps()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in ProviderCatalog.BuiltIn())
            {
                foreach (var a in p.GetIpv4Servers()) set.Add(a);
                foreach (var a in p.GetIpv6Servers()) set.Add(a);
            }
            return set;
        }

        /// <summary>Ẩn IP lạ, tên máy, tên người dùng, đường dẫn hồ sơ người dùng.</summary>
        public static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var known = new HashSet<string>(KnownIps().Select(Canonical), StringComparer.OrdinalIgnoreCase);
            string s = Ipv4.Replace(text, m => known.Contains(Canonical(m.Value)) ? m.Value : "x.x.x.x");
            s = Ipv6.Replace(s, m =>
            {
                string v = m.Value;
                bool looksIpv6 = v.Contains("::") || v.Count(c => c == ':') >= 3;
                if (!looksIpv6) return v;
                System.Net.IPAddress a;
                if (!System.Net.IPAddress.TryParse(v, out a)) return v;
                return known.Contains(a.ToString()) ? v : "x:x::x";
            });
            s = UserPath.Replace(s, "C:\\Users\\<user>");
            try
            {
                string user = Environment.UserName;
                string machine = Environment.MachineName;
                if (!string.IsNullOrEmpty(user) && user.Length > 2) s = Regex.Replace(s, @"\b" + Regex.Escape(user) + @"\b", "<user>", RegexOptions.IgnoreCase);
                if (!string.IsNullOrEmpty(machine) && machine.Length > 2) s = Regex.Replace(s, @"\b" + Regex.Escape(machine) + @"\b", "<máy>", RegexOptions.IgnoreCase);
            }
            catch (Exception) { }
            return s;
        }

        /// <summary>Thông tin hệ thống an toàn để đưa vào issue (không tên máy, không đường dẫn).</summary>
        public static string PublicSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("App: " + AppInfo.Name + " " + AppInfo.Version + (UpdateService.IsInstalledCopy() ? " (bản cài đặt)" : " (portable)"));
            sb.AppendLine("Windows: " + AppInfo.WindowsVersion() + " (" + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit") + ")");
            sb.AppendLine("Runtime: " + AppInfo.DotNetVersion());
            sb.AppendLine("Quyền Administrator: " + (AdminHelper.IsAdministrator() ? "có" : "không"));
            var nat = VpnService.GetL2tpNatRule();
            sb.AppendLine("Khoá L2TP sau NAT: " + (nat.HasValue ? nat.Value.ToString() : "chưa đặt"));
            return sb.ToString().TrimEnd();
        }

        public static string BuildBody(string description, string steps, bool includeSystem, string recentLog)
        {
            var sb = new StringBuilder();
            sb.AppendLine("### Mô tả");
            sb.AppendLine(string.IsNullOrWhiteSpace(description) ? "(chưa ghi)" : Sanitize(description.Trim()));
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(steps))
            {
                sb.AppendLine("### Các bước tái hiện / bối cảnh");
                sb.AppendLine(Sanitize(steps.Trim()));
                sb.AppendLine();
            }
            if (includeSystem)
            {
                sb.AppendLine("### Thông tin hệ thống");
                sb.AppendLine("```");
                sb.AppendLine(PublicSummary());
                sb.AppendLine("```");
                sb.AppendLine();
            }
            if (!string.IsNullOrWhiteSpace(recentLog))
            {
                sb.AppendLine("### Nhật ký gần nhất (đã ẩn IP, tên máy)");
                sb.AppendLine("```");
                sb.AppendLine(Sanitize(recentLog.Trim()));
                sb.AppendLine("```");
                sb.AppendLine();
            }
            sb.AppendLine("_Gửi từ " + AppInfo.Name + " " + AppInfo.Version + ". Nếu cần, đính kèm gói chẩn đoán (Cài đặt → Xuất gói chẩn đoán, không chứa mật khẩu)._");
            string body = sb.ToString();
            if (body.Length > MaxBodyChars) body = body.Substring(0, MaxBodyChars) + Environment.NewLine + "…(đã cắt bớt)";
            return body;
        }

        /// <summary>URL mở trang tạo issue với tiêu đề, nội dung, nhãn điền sẵn; tự rút ngắn để không vượt giới hạn URL.</summary>
        public static string BuildIssueUrl(string title, string body, string labels)
        {
            string t = string.IsNullOrWhiteSpace(title) ? "Phản hồi từ " + AppInfo.Name + " " + AppInfo.Version : title.Trim();
            if (t.Length > 120) t = t.Substring(0, 120);
            string b = body ?? "";
            string url = Compose(t, b, labels);
            while (url.Length > MaxUrlChars && b.Length > 200)
            {
                b = b.Substring(0, b.Length - 300) + Environment.NewLine + "…(đã cắt bớt)";
                url = Compose(t, b, labels);
            }
            return url;
        }

        private static string Compose(string title, string body, string labels)
        {
            var sb = new StringBuilder(NewIssueUrl);
            sb.Append("?title=").Append(Uri.EscapeDataString(title));
            sb.Append("&body=").Append(Uri.EscapeDataString(body));
            if (!string.IsNullOrWhiteSpace(labels)) sb.Append("&labels=").Append(Uri.EscapeDataString(labels));
            return sb.ToString();
        }
    }
}
