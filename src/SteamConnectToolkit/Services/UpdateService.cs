using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace SteamConnectToolkit.Services
{
    /// <summary>Một gói trong manifest cập nhật.</summary>
    public class UpdatePackage
    {
        public string Url { get; set; }
        public string Sha256 { get; set; }
        public long Size { get; set; }
    }

    /// <summary>Nội dung latest.json do release.ps1 tạo.</summary>
    public class UpdateManifest
    {
        public string Version { get; set; }
        public string Published { get; set; }
        /// <summary>Phiên bản tối thiểu còn được hỗ trợ; thấp hơn → cập nhật bắt buộc.</summary>
        public string MinVersion { get; set; }
        public bool Mandatory { get; set; }
        public string Notes { get; set; }
        public string NotesUrl { get; set; }
        public UpdatePackage Portable { get; set; }
        public UpdatePackage Installer { get; set; }
    }

    public class UpdateCheckResult
    {
        public bool Ok { get; set; }
        public string Error { get; set; }
        public UpdateManifest Manifest { get; set; }
        public Version Current { get; set; }
        public Version Latest { get; set; }
        public bool UpdateAvailable { get; set; }
        public bool Mandatory { get; set; }
        public bool UsesInstaller { get; set; }
        public UpdatePackage Package { get; set; }
        /// <summary>"manifest" (latest.json) hoặc "GitHub API (…)" khi Release không còn latest.json.</summary>
        public string Source { get; set; }

        public string Describe()
        {
            if (!Ok) return "Không kiểm tra được: " + Error;
            if (!UpdateAvailable) return "Đang dùng bản mới nhất (" + Current.ToString(3) + ").";
            return "Có bản mới " + Manifest.Version + (Mandatory ? " (bắt buộc)" : "") + (string.IsNullOrEmpty(Manifest.Published) ? "" : ", phát hành " + Manifest.Published)
                 + (UsesInstaller ? " – gói cài đặt" : " – gói portable");
        }
    }

    /// <summary>
    /// Nâng cấp từ xa: tải manifest (HTTPS) → so sánh phiên bản → tải gói → xác minh SHA-256 (+ chữ ký số nếu cấu hình)
    /// → đóng app (khôi phục DNS/VPN như thoát bình thường) → thay file (portable) hoặc chạy bộ cài im lặng → mở lại.
    /// </summary>
    public static class UpdateService
    {
        /// <summary>Địa chỉ manifest mặc định: file latest.json đính kèm GitHub Release mới nhất của kho (tools/publish-release.ps1 tải lên; GitHub chuyển hướng tới bản mới nhất).</summary>
        public const string DefaultManifestUrl = AppInfo.ReleasesUrl + "/latest/download/latest.json";
        /// <summary>API GitHub cho Release mới nhất – dự phòng khi latest.json bị xoá khỏi Release; mã băm lấy từ trường digest của từng asset.</summary>
        public const string GitHubLatestApiUrl = "https://api.github.com/repos/" + AppInfo.RepositorySlug + "/releases/latest";
        public const string ApplyArgument = "--apply-update";
        public const string AfterUpdateArgument = "--after-update";
        public const int CheckIntervalHours = 24;

        public static string UpdatesDir { get { return Path.Combine(AppPaths.DataDir, "updates"); } }

        // ---------- Phiên bản ----------

        public static Version ParseVersion(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
            int cut = s.IndexOfAny(new[] { '-', '+', ' ' });
            if (cut > 0) s = s.Substring(0, cut);
            var parts = s.Split('.');
            var n = new int[4];
            int count = 0;
            foreach (var p in parts)
            {
                int v;
                if (!int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) || v < 0) break;
                if (count < 4) n[count++] = v;
            }
            if (count == 0) return null;
            return new Version(n[0], n[1], n[2], n[3]);
        }

        public static Version CurrentVersion()
        {
            return ParseVersion(AppInfo.Version) ?? new Version(0, 0, 0, 0);
        }

        /// <summary>Bản cài bằng Inno Setup có unins000.exe cạnh file chạy.</summary>
        public static bool IsInstalledCopy()
        {
            try { return File.Exists(Path.Combine(AppInfo.ExeDirectory, "unins000.exe")); }
            catch (Exception) { return false; }
        }

        public static bool IsCheckDue(string lastCheck)
        {
            DateTime t;
            if (string.IsNullOrWhiteSpace(lastCheck) || !DateTime.TryParseExact(lastCheck, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) return true;
            return (DateTime.Now - t).TotalHours >= CheckIntervalHours;
        }

        // ---------- Manifest ----------

        /// <summary>Chỉ cho phép https:// (và file:// để kiểm thử/nội bộ). Không bao giờ dùng http:// cho file chạy.</summary>
        public static bool IsAllowedUrl(string url)
        {
            Uri u;
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out u)) return false;
            return u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeFile;
        }

        public static string FetchText(string url, int timeoutMs)
        {
            return FetchText(url, timeoutMs, null);
        }

        public static string FetchText(string url, int timeoutMs, string accept)
        {
            if (!IsAllowedUrl(url)) throw new InvalidOperationException("Địa chỉ phải dùng https:// (hoặc file:// khi kiểm thử): " + url);
            var req = WebRequest.Create(url.Trim());
            req.Timeout = timeoutMs;
            var http = req as HttpWebRequest;
            if (http != null)
            {
                http.UserAgent = AppInfo.Name + "/" + AppInfo.Version;
                http.ReadWriteTimeout = timeoutMs;
                http.AllowAutoRedirect = true;
                http.Headers["Cache-Control"] = "no-cache";
                if (!string.IsNullOrEmpty(accept)) http.Accept = accept;
            }
            using (var resp = req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                return sr.ReadToEnd();
            }
        }

        public static UpdateManifest ParseManifest(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("Manifest rỗng.");
            UpdateManifest m;
            try { m = new JavaScriptSerializer().Deserialize<UpdateManifest>(json); }
            catch (Exception ex) { throw new InvalidOperationException("Manifest không phải JSON hợp lệ: " + ex.Message); }
            if (m == null || ParseVersion(m.Version) == null) throw new InvalidOperationException("Manifest thiếu trường Version hợp lệ.");
            if ((m.Portable == null || string.IsNullOrEmpty(m.Portable.Url)) && (m.Installer == null || string.IsNullOrEmpty(m.Installer.Url)))
                throw new InvalidOperationException("Manifest không có gói Portable/Installer.");
            return m;
        }

        /// <summary>Địa chỉ đang dùng có phải manifest mặc định trên GitHub Releases của kho không.</summary>
        public static bool IsDefaultGitHubManifest(string url)
        {
            return url != null && string.Equals(url.Trim(), DefaultManifestUrl, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Chỉ chuyển sang GitHub API khi máy chủ có trả lời (404/403…) hoặc nội dung hỏng; mất mạng thì không thử thêm cho khỏi chờ lâu.</summary>
        public static bool ShouldFallbackToApi(Exception ex)
        {
            var web = ex as WebException;
            if (web != null) return web.Status == WebExceptionStatus.ProtocolError;
            return ex is InvalidOperationException;
        }

        /// <summary>Dựng manifest từ phản hồi GET /repos/{owner}/{repo}/releases/latest của GitHub (asset kèm digest sha256).</summary>
        public static UpdateManifest ManifestFromGitHubRelease(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("Phản hồi GitHub API rỗng.");
            Dictionary<string, object> root;
            try { root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>; }
            catch (Exception ex) { throw new InvalidOperationException("Phản hồi GitHub API không phải JSON hợp lệ: " + ex.Message); }
            if (root == null) throw new InvalidOperationException("Phản hồi GitHub API không đúng dạng.");
            string tag = JsonString(root, "tag_name");
            if (ParseVersion(tag) == null) throw new InvalidOperationException("Release không có tag phiên bản hợp lệ: " + tag);
            if (JsonBool(root, "draft") || JsonBool(root, "prerelease")) throw new InvalidOperationException("Release mới nhất là bản nháp/thử nghiệm.");
            var m = new UpdateManifest
            {
                Version = tag.Trim().TrimStart('v', 'V'),
                Published = TrimDate(JsonString(root, "published_at")),
                Notes = TrimNotes(JsonString(root, "body")),
                NotesUrl = JsonString(root, "html_url")
            };
            object assetsObj;
            var assets = root.TryGetValue("assets", out assetsObj) ? assetsObj as object[] : null;
            if (assets != null)
            {
                foreach (var a in assets)
                {
                    var asset = a as Dictionary<string, object>;
                    if (asset == null) continue;
                    string name = JsonString(asset, "name") ?? "";
                    string url = JsonString(asset, "browser_download_url");
                    string digest = JsonString(asset, "digest") ?? "";
                    string sha = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest.Substring(7).Trim() : null;
                    // Không có mã băm → không dùng: Download() từ chối gói thiếu SHA-256
                    if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(sha)) continue;
                    var pkg = new UpdatePackage { Url = url, Sha256 = sha, Size = JsonLong(asset, "size") };
                    if (string.Equals(name, "SteamConnectToolkit.exe", StringComparison.OrdinalIgnoreCase)) m.Portable = pkg;
                    else if (name.StartsWith("SteamConnectToolkit-Setup-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) m.Installer = pkg;
                }
            }
            if (m.Portable == null && m.Installer == null)
                throw new InvalidOperationException("Release " + tag + " không có SteamConnectToolkit.exe hay bộ cài kèm mã băm SHA-256.");
            return m;
        }

        /// <summary>Hỏi GitHub API về Release mới nhất; null kèm lỗi nếu không được.</summary>
        public static UpdateManifest ManifestFromGitHubApi(int timeoutMs, out string error)
        {
            error = null;
            try { return ManifestFromGitHubRelease(FetchText(GitHubLatestApiUrl, timeoutMs, "application/vnd.github+json")); }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        private static string JsonString(Dictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return null;
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        private static bool JsonBool(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) && v is bool && (bool)v;
        }

        private static long JsonLong(Dictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return 0;
            try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); }
            catch (Exception) { return 0; }
        }

        private static string TrimDate(string iso)
        {
            return iso != null && iso.Length >= 10 ? iso.Substring(0, 10) : iso;
        }

        /// <summary>Ghi chú Release: bỏ phần chân "---" do script phát hành thêm, giới hạn độ dài cho hộp thoại.</summary>
        private static string TrimNotes(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            string s = body.Replace("\r\n", "\n").Trim();
            int cut = s.IndexOf("\n---", StringComparison.Ordinal);
            if (cut > 0) s = s.Substring(0, cut).Trim();
            if (s.Length > 1500) s = s.Substring(0, 1500) + "…";
            return s;
        }

        public static UpdateCheckResult Check(string manifestUrl, Version current, bool installedCopy, int timeoutMs)
        {
            var r = new UpdateCheckResult { Current = current ?? new Version(0, 0, 0, 0) };
            try
            {
                UpdateManifest m;
                try
                {
                    m = ParseManifest(FetchText(manifestUrl, timeoutMs));
                    r.Source = "manifest";
                }
                catch (Exception ex)
                {
                    if (!IsDefaultGitHubManifest(manifestUrl) || !ShouldFallbackToApi(ex)) throw;
                    // latest.json không còn trên Release (bị xoá/đổi tên) → hỏi GitHub API, mã băm lấy từ digest của asset
                    string apiErr;
                    m = ManifestFromGitHubApi(timeoutMs, out apiErr);
                    if (m == null) throw new InvalidOperationException(ex.Message + " (GitHub API dự phòng: " + apiErr + ")");
                    r.Source = "GitHub API, Release không có latest.json";
                }
                r.Manifest = m;
                r.Latest = ParseVersion(m.Version);
                bool hasInstaller = m.Installer != null && !string.IsNullOrEmpty(m.Installer.Url);
                bool hasPortable = m.Portable != null && !string.IsNullOrEmpty(m.Portable.Url);
                if (installedCopy && hasInstaller) { r.Package = m.Installer; r.UsesInstaller = true; }
                else if (hasPortable) { r.Package = m.Portable; r.UsesInstaller = false; }
                else { r.Package = m.Installer; r.UsesInstaller = true; }
                r.UpdateAvailable = r.Latest > r.Current && r.Package != null;
                var min = ParseVersion(m.MinVersion);
                r.Mandatory = r.UpdateAvailable && (m.Mandatory || (min != null && r.Current < min));
                r.Ok = true;
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
            }
            return r;
        }

        // ---------- Tải và xác minh ----------

        public static string Sha256File(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>Tải gói về destPath (qua file .part), kiểm tra kích thước và SHA-256; sai → xoá và ném lỗi.</summary>
        public static string Download(UpdatePackage pkg, string destPath, Action<long, long> progress, CancellationToken ct)
        {
            if (pkg == null || string.IsNullOrEmpty(pkg.Url)) throw new InvalidOperationException("Gói cập nhật không có URL.");
            if (!IsAllowedUrl(pkg.Url)) throw new InvalidOperationException("URL gói phải dùng https://: " + pkg.Url);
            if (string.IsNullOrWhiteSpace(pkg.Sha256)) throw new InvalidOperationException("Gói cập nhật không có SHA-256 – từ chối tải.");
            string expected = pkg.Sha256.Trim().ToLowerInvariant();

            // Đã tải trước đó và còn đúng → dùng lại
            if (File.Exists(destPath))
            {
                try { if (string.Equals(Sha256File(destPath), expected, StringComparison.OrdinalIgnoreCase)) return destPath; }
                catch (Exception) { }
                try { File.Delete(destPath); } catch (Exception) { }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destPath));
            string tmp = destPath + ".part";
            try
            {
                var req = WebRequest.Create(pkg.Url.Trim());
                req.Timeout = 60000;
                var http = req as HttpWebRequest;
                if (http != null)
                {
                    http.UserAgent = AppInfo.Name + "/" + AppInfo.Version;
                    http.ReadWriteTimeout = 60000;
                    http.AllowAutoRedirect = true;
                }
                long done = 0;
                using (var resp = req.GetResponse())
                {
                    long total = resp.ContentLength;
                    using (var src = resp.GetResponseStream())
                    using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                    {
                        var buf = new byte[81920];
                        int n;
                        while ((n = src.Read(buf, 0, buf.Length)) > 0)
                        {
                            ct.ThrowIfCancellationRequested();
                            dst.Write(buf, 0, n);
                            done += n;
                            if (progress != null) progress(done, total);
                        }
                    }
                }
                if (pkg.Size > 0 && new FileInfo(tmp).Length != pkg.Size)
                    throw new InvalidOperationException("Kích thước file tải về (" + new FileInfo(tmp).Length + ") không khớp manifest (" + pkg.Size + ").");
                string hash = Sha256File(tmp);
                if (!string.Equals(hash, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("SHA-256 không khớp (tải về " + hash + ", manifest " + expected + ") – file có thể đã bị thay đổi, huỷ cập nhật.");
                File.Move(tmp, destPath);
                return destPath;
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
            }
        }

        /// <summary>Vân tay (thumbprint) chứng chỉ ký file; null nếu file không ký.</summary>
        public static string SignerThumbprint(string path)
        {
            try
            {
                var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
                return cert.Thumbprint;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Nếu có cấu hình vân tay nhà phát hành thì file phải được ký đúng chứng chỉ đó.</summary>
        public static bool VerifySigner(string path, string expectedThumbprint, out string actual)
        {
            actual = SignerThumbprint(path);
            if (string.IsNullOrWhiteSpace(expectedThumbprint)) return true;
            return actual != null && string.Equals(actual.Replace(" ", ""), expectedThumbprint.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
        }

        // ---------- Áp dụng ----------

        /// <summary>Chạy bộ cài Inno Setup im lặng; /UPDATE=1 để bộ cài tự mở lại app sau khi xong.</summary>
        public static Process LaunchInstaller(string setupPath)
        {
            return Process.Start(new ProcessStartInfo(setupPath, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /UPDATE=1") { UseShellExecute = false });
        }

        /// <summary>Chạy chính exe hiện tại ở chế độ --apply-update để thay file sau khi app thoát.</summary>
        public static Process LaunchPortableUpdater(string newExePath)
        {
            string exe = AppInfo.ExePath;
            return Process.Start(new ProcessStartInfo(exe, ApplyArgument + " \"" + newExePath + "\" " + Process.GetCurrentProcess().Id)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }

        /// <summary>
        /// Thay file chương trình: chờ tiến trình cũ thoát, đổi tên bản cũ thành .bak, chép bản mới vào, mở lại.
        /// Thử lại tối đa 20 lần (file có thể còn bị giữ); thất bại thì trả bản cũ về chỗ.
        /// </summary>
        public static int ApplyPortableUpdate(string newExePath, int waitPid, string targetExe, bool relaunch, Action<string> log)
        {
            if (waitPid > 0)
            {
                try { Process.GetProcessById(waitPid).WaitForExit(); }
                catch (ArgumentException) { }
                catch (Exception) { }
                Thread.Sleep(1000);
            }
            if (!File.Exists(newExePath) || new FileInfo(newExePath).Length == 0)
            {
                if (log != null) log("Cập nhật: file mới không tồn tại: " + newExePath);
                return 2;
            }
            string bak = targetExe + ".bak";
            Exception last = null;
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    if (File.Exists(bak)) File.Delete(bak);
                    if (File.Exists(targetExe)) File.Move(targetExe, bak);
                    File.Copy(newExePath, targetExe, true);
                    if (log != null) log("Cập nhật: đã thay " + targetExe + " (bản cũ giữ ở " + bak + ")");
                    try { File.Delete(newExePath); } catch (Exception) { }
                    if (relaunch)
                    {
                        Process.Start(new ProcessStartInfo(targetExe, AfterUpdateArgument) { UseShellExecute = false });
                        if (log != null) log("Cập nhật: đã mở lại app.");
                    }
                    return 0;
                }
                catch (Exception ex)
                {
                    last = ex;
                    Thread.Sleep(500);
                }
            }
            try { if (!File.Exists(targetExe) && File.Exists(bak)) File.Move(bak, targetExe); }
            catch (Exception) { }
            if (log != null) log("Cập nhật: không thay được file sau 20 lần – " + (last != null ? last.Message : "?") + "; đã trả lại bản cũ.");
            return 1;
        }

        /// <summary>Sau khi nâng cấp: xoá .bak và gói tải cũ hơn 7 ngày.</summary>
        public static void Cleanup()
        {
            try
            {
                string bak = AppInfo.ExePath + ".bak";
                if (File.Exists(bak)) File.Delete(bak);
            }
            catch (Exception) { }
            try
            {
                if (!Directory.Exists(UpdatesDir)) return;
                foreach (var f in Directory.GetFiles(UpdatesDir))
                {
                    try { if ((DateTime.Now - File.GetLastWriteTime(f)).TotalDays > 7) File.Delete(f); }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
        }
    }
}
