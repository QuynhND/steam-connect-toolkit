using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using SteamConnectToolkit.Models;
using SteamConnectToolkit.Services;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

/// <summary>
/// Kiểm thử phần lõi. KHÔNG thay đổi DNS thật, KHÔNG cần quyền admin.
///   SmokeTest.exe selftest [--no-network]      bộ tự kiểm thử (mô phỏng netsh, tình huống lỗi, JSON, CSV, xếp hạng...)
///   SmokeTest.exe [all | id1 id2 ...] [--no-discovery]   kiểm tra mạng thật với các DNS chỉ định
/// Mã thoát: 0 = tất cả đạt, 2 = có kiểm thử thất bại.
/// </summary>
internal static class Program
{
    private static int _pass;
    private static int _fail;
    private static readonly List<string> Failures = new List<string>();

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        HttpsProbe.ConfigureTls();
        if (args.Length > 0 && args[0] == "selftest") return SelfTest(!args.Contains("--no-network"));
        return NetworkCheck(args);
    }

    // =====================================================================
    //  Tự kiểm thử
    // =====================================================================

    private static void Check(bool cond, string name, string detail)
    {
        if (cond)
        {
            _pass++;
            Console.WriteLine("  PASS  " + name);
        }
        else
        {
            _fail++;
            Failures.Add(name);
            Console.WriteLine("  FAIL  " + name + (string.IsNullOrEmpty(detail) ? "" : "  –  " + detail));
        }
    }

    private static void Check(bool cond, string name)
    {
        Check(cond, name, null);
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine("== " + title + " ==");
    }

    private static int SelfTest(bool network)
    {
        var builtIn = ProviderCatalog.BuiltIn();
        string tmp = Path.Combine(Path.GetTempPath(), "SteamConnectToolkit-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        AppPaths.DataDirOverride = tmp;
        Console.WriteLine("Thư mục dữ liệu tạm: " + tmp);
        try
        {
            TestProviderModel();
            TestCatalog(builtIn);
            TestConfigAndJson(builtIn);
            TestCsvAndDiscoveryModel();
            var adapters = TestAdapters();
            TestSimulatedApplyRestore(builtIn, adapters);
            TestRankingAndTester(builtIn);
            TestLegacyMigration(tmp);
            TestVpn(network);
            TestVpnGate(network);
            TestSessionAndRecovery(builtIn, adapters);
            TestGuideAndSettings();
            TestAboutAndDiagnostics(builtIn);
            TestUpdates(tmp);
            TestFeedback();
            if (network) TestNetworkEdgeCases();
            else Console.WriteLine("\n(bỏ qua kiểm thử mạng: --no-network)");
        }
        catch (Exception ex)
        {
            _fail++;
            Failures.Add("Ngoại lệ không mong muốn: " + ex.GetType().Name);
            Console.WriteLine("  FAIL  Ngoại lệ không mong muốn: " + ex);
        }
        finally
        {
            DnsApplier.CommandRunner = null;
            AppPaths.DataDirOverride = null;
            try { Directory.Delete(tmp, true); } catch (Exception) { }
        }

        Console.WriteLine();
        Console.WriteLine("======================================");
        Console.WriteLine("KẾT QUẢ: PASS " + _pass + " / FAIL " + _fail);
        foreach (var f in Failures) Console.WriteLine("  ✖ " + f);
        return _fail == 0 ? 0 : 2;
    }

    private static void TestProviderModel()
    {
        Section("Mô hình DNS provider");
        Check(new DnsProvider { Name = "X", Ipv4Primary = "8.8.8.8" }.Validate() == null, "Validate: IPv4 hợp lệ");
        Check(new DnsProvider { Name = "X", Ipv4Primary = "8.8.8" }.Validate() != null, "Validate: IPv4 thiếu octet (8.8.8) bị từ chối");
        Check(new DnsProvider { Name = "X", Ipv4Primary = "1" }.Validate() != null, "Validate: IPv4 '1' bị từ chối");
        Check(new DnsProvider { Name = "X", Ipv4Primary = "256.1.1.1" }.Validate() != null, "Validate: octet > 255 bị từ chối");
        Check(new DnsProvider { Name = "X", Ipv4Primary = "8.8.8.8.8" }.Validate() != null, "Validate: 5 octet bị từ chối");
        Check(new DnsProvider { Name = "X", Ipv4Primary = "abc" }.Validate() != null, "Validate: chữ bị từ chối");
        Check(new DnsProvider { Name = "X", Ipv6Primary = "2001:4860:4860:0:0:0:0:8888" }.Validate() == null, "Validate: IPv6 dạng đầy đủ hợp lệ");
        Check(new DnsProvider { Name = "X", Ipv4Primary = "2001:4860:4860::8888" }.Validate() != null, "Validate: IPv6 trong ô IPv4 bị từ chối");
        Check(new DnsProvider { Name = "X", Ipv6Primary = "8.8.8.8" }.Validate() != null, "Validate: IPv4 trong ô IPv6 bị từ chối");
        Check(new DnsProvider { Name = "  ", Ipv4Primary = "8.8.8.8" }.Validate() != null, "Validate: thiếu tên");
        Check(new DnsProvider { Name = "X" }.Validate() != null, "Validate: không có địa chỉ");
        Check(new DnsProvider { Name = "X", Ipv4Primary = " 1.1.1.1 " }.Validate() == null, "Validate: khoảng trắng thừa được cắt");
        var v6 = new DnsProvider { Name = "V6", Ipv6Primary = "2a07:e340::2" };
        Check(v6.Validate() == null && !v6.HasIpv4() && v6.HasIpv6() && v6.DisplayName().Contains("2a07:e340::2") && v6.Ipv4Text() == "(không có)",
            "Provider chỉ IPv6: hợp lệ, DisplayName dùng IPv6");
        var c = new DnsProvider { Id = "a", Name = "A", Ipv4Primary = "1.1.1.1", Enabled = false };
        var clone = c.Clone();
        clone.Enabled = true;
        Check(!c.Enabled && clone.Enabled && clone.Id == "a", "Clone: bản sao độc lập");
    }

    private static void TestCatalog(List<DnsProvider> builtIn)
    {
        Section("Danh mục DNS có sẵn");
        Check(builtIn.Select(p => p.Id).Distinct().Count() == builtIn.Count, "Id duy nhất");
        var invalid = builtIn.Where(p => p.Validate() != null).Select(p => p.Name).ToList();
        Check(invalid.Count == 0, "Mọi địa chỉ hợp lệ", string.Join(", ", invalid));
        var core = builtIn.Where(p => p.Category == "core").ToList();
        Check(core.Count == 11 && core.All(p => p.Enabled && p.BuiltIn), "11 DNS chính được bật sẵn");
        Check(builtIn.Where(p => p.Category == "extra").All(p => !p.Enabled), "DNS bổ sung tắt sẵn");
        string[] required = { "google", "cloudflare", "quad9", "quad9-unfiltered", "adguard", "verisign", "opendns", "cleanbrowsing-security", "controld-malware", "dns4eu-protective", "ultradns" };
        Check(required.All(id => builtIn.Any(p => p.Id == id && p.Category == "core")), "Đủ 11 DNS theo yêu cầu");
        Check(core.All(p => p.HasIpv4() && p.HasIpv6()), "11 DNS chính đều có IPv4 và IPv6");
        var allIps = builtIn.SelectMany(p => p.GetIpv4Servers().Concat(p.GetIpv6Servers())).ToList();
        Check(allIps.Distinct().Count() == allIps.Count, "Không trùng địa chỉ giữa các DNS", string.Join(", ", allIps.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key)));
    }

    private static void TestConfigAndJson(List<DnsProvider> builtIn)
    {
        Section("Cấu hình, JSON, lịch sử");
        var cfg = ProviderCatalog.LoadConfig();
        Check(cfg.Providers.Count == builtIn.Count && !File.Exists(AppPaths.ConfigFile), "Nạp mặc định khi chưa có file");

        cfg.Providers.First(p => p.Id == "google").Enabled = false;
        cfg.Providers.First(p => p.Id == "comodo").Enabled = true;
        cfg.Providers.Add(new DnsProvider { Id = "custom-1", Name = "Tự thêm", Ipv4Primary = "10.0.0.53", Category = "custom", BuiltIn = false });
        cfg.Providers.Add(new DnsProvider { Name = "Không Id", Ipv4Primary = "10.0.0.54", Category = "custom", BuiltIn = false });
        cfg.LastProviderId = "quad9";
        cfg.DiscoveryCountry = "VN";
        ProviderCatalog.SaveConfig(cfg);
        Check(File.Exists(AppPaths.ConfigFile) && !File.Exists(AppPaths.ConfigFile + ".tmp"), "Ghi file (atomic, không để lại .tmp)");

        var cfg2 = ProviderCatalog.LoadConfig();
        Check(!cfg2.Providers.First(p => p.Id == "google").Enabled && cfg2.Providers.First(p => p.Id == "comodo").Enabled, "Giữ trạng thái bật/tắt của DNS có sẵn");
        Check(cfg2.Providers.Any(p => p.Id == "custom-1" && p.Ipv4Primary == "10.0.0.53"), "Giữ DNS tự thêm");
        Check(cfg2.Providers.Any(p => p.Name == "Không Id" && !string.IsNullOrEmpty(p.Id)), "DNS tự thêm thiếu Id được cấp Id");
        Check(cfg2.LastProviderId == "quad9" && cfg2.DiscoveryCountry == "VN", "Giữ tuỳ chọn");

        // Địa chỉ DNS có sẵn bị sửa trong file → danh mục trong mã phải thắng (tránh cấu hình cũ/sai)
        string json = File.ReadAllText(AppPaths.ConfigFile);
        File.WriteAllText(AppPaths.ConfigFile, json.Replace("\"8.8.8.8\"", "\"9.9.9.9\""));
        var cfg4 = ProviderCatalog.LoadConfig();
        Check(cfg4.Providers.First(p => p.Id == "google").Ipv4Primary == "8.8.8.8", "Địa chỉ DNS có sẵn luôn lấy từ danh mục trong mã");

        File.WriteAllText(AppPaths.ConfigFile, "{ this is not json");
        var cfg3 = ProviderCatalog.LoadConfig();
        Check(cfg3.Providers.Count == builtIn.Count && File.Exists(AppPaths.ConfigFile + ".corrupt"), "File hỏng → dùng mặc định, giữ bản .corrupt");

        File.WriteAllText(AppPaths.ConfigFile, "");
        Check(ProviderCatalog.LoadConfig().Providers.Count == builtIn.Count, "File rỗng → mặc định");

        var bad = new AppConfig { DnsTimeoutMs = 1, HttpsTimeoutMs = 0, Concurrency = 999, DiscoveryMinReliability = 5, DiscoveryMaxServers = -3, DiscoveryCountry = "" };
        bad.Normalize();
        Check(bad.DnsTimeoutMs >= 500 && bad.HttpsTimeoutMs >= 1000 && bad.Concurrency <= 32 && bad.DiscoveryMinReliability <= 1 && bad.DiscoveryMaxServers > 0 && bad.DiscoveryCountry == "VN",
            "Normalize sửa giá trị sai");

        var h = new HistoryData();
        h.Entries.Add(new HistoryEntry { Time = "t", Action = "a{}[]\",:", Adapter = "Wi-Fi \"x\"", Detail = "đường dẫn C:\\x\ty", Result = "ok" });
        JsonStore.Save(AppPaths.HistoryFile, h);
        var h2 = JsonStore.Load<HistoryData>(AppPaths.HistoryFile);
        Check(h2.Entries.Count == 1 && h2.Entries[0].Action == "a{}[]\",:" && h2.Entries[0].Adapter == "Wi-Fi \"x\"" && h2.Entries[0].Detail == "đường dẫn C:\\x\ty",
            "JSON: ký tự đặc biệt / Unicode round-trip");
        string pretty = File.ReadAllText(AppPaths.HistoryFile);
        Check(pretty.Contains("\n") && pretty.Contains("\"Entries\": ["), "JSON: file được định dạng dễ đọc");

        HistoryService.Clear();
        HistoryService.Add("A", "Eth", "d", "Thành công");
        HistoryService.Add("B", "Eth", "d2", "Lỗi: x");
        var entries = HistoryService.Load();
        Check(entries.Count == 2 && entries[1].Result == "Lỗi: x" && !string.IsNullOrEmpty(entries[0].Time), "Lịch sử: thêm / đọc");
        string csv = Path.Combine(AppPaths.DataDir, "h.csv");
        HistoryService.ExportCsv(csv);
        Check(File.ReadAllText(csv).Contains("\"Lỗi: x\""), "Lịch sử: xuất CSV");
        HistoryService.Clear();
        Check(HistoryService.Load().Count == 0, "Lịch sử: xoá");

        BackupService.Save(new BackupData());
        Check(!BackupService.HasAny(), "Sao lưu: trống ban đầu");

        // Ghi đè file đang tồn tại nhiều lần: không để sót .tmp
        for (int i = 0; i < 3; i++) JsonStore.Save(AppPaths.HistoryFile, new HistoryData());
        Check(File.Exists(AppPaths.HistoryFile) && !File.Exists(AppPaths.HistoryFile + ".tmp"), "Ghi đè lặp lại: không sót file .tmp");

        // File đích bị khoá bởi tiến trình khác: báo lỗi cho nơi gọi, nhưng vẫn dọn .tmp và không hỏng file cũ
        string locked = Path.Combine(AppPaths.DataDir, "locked.json");
        File.WriteAllText(locked, "{\"Entries\":[]}");
        bool threwLocked = false;
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            try { JsonStore.Save(locked, new HistoryData()); }
            catch (Exception) { threwLocked = true; }
        }
        Check(threwLocked && !File.Exists(locked + ".tmp") && File.ReadAllText(locked) == "{\"Entries\":[]}", "File đích bị khoá → ném lỗi, dọn .tmp, giữ nguyên file cũ");

        // Di trú cấu hình phiên bản 1 → 2
        var old = new AppConfig { Version = 1, Concurrency = 6, HttpsTimeoutMs = 5000 };
        ProviderCatalog.Migrate(old);
        Check(old.Version == 2 && old.Concurrency == 4 && old.HttpsTimeoutMs == 6000, "Di trú cấu hình v1 → v2 (4 luồng, HTTPS 6 s)");
        var fresh = new AppConfig();
        ProviderCatalog.Migrate(fresh);
        Check(fresh.Version == 2 && fresh.Concurrency == 4, "Cấu hình mới không bị di trú lại");
    }

    private static void TestCsvAndDiscoveryModel()
    {
        Section("CSV & mô hình DNS tìm thấy");
        var rows = DiscoveryService.ParseCsv("ip_address,name,as_org\r\n1.2.3.4,\"a, b\",\"Org \"\"X\"\"\"\n5.6.7.8,,Plain\n");
        Check(rows.Count == 3 && rows[1][1] == "a, b" && rows[1][2] == "Org \"X\"" && rows[2][1] == "" && rows[2][2] == "Plain",
            "CSV: ngoặc kép, dấu phẩy trong ô, CRLF, dòng trống cuối");
        Check(DiscoveryService.ParseCsv("").Count == 0, "CSV: chuỗi rỗng");
        var d6 = DiscoveryService.ToProvider(new DiscoveredServer { Ip = "2001:db8::1", AsOrg = "Org", Country = "VN", Reliability = 1, IsIpv6 = true });
        Check(d6.Ipv6Primary == "2001:db8::1" && !d6.HasIpv4() && d6.Category == "discovered" && d6.Validate() == null && !d6.BuiltIn, "ToProvider IPv6");
        var d4 = DiscoveryService.ToProvider(new DiscoveredServer { Ip = "203.0.113.5", AsOrg = new string('A', 80), Country = "VN", Reliability = 0.99 });
        Check(d4.Ipv4Primary == "203.0.113.5" && d4.Name.Length < 60 && d4.Id == "disc-203_0_113_5" && d4.Notes.Contains("0.99"), "ToProvider IPv4, tên dài bị cắt, Id theo IP");
        Check(DiscoveryService.CountryUrl(" Vn ") == "https://public-dns.info/nameserver/vn.csv", "URL quốc gia chuẩn hoá");
    }

    private static List<AdapterInfo> TestAdapters()
    {
        Section("Card mạng");
        var adapters = NetworkAdapterService.GetAdapters(true);
        Check(adapters.Count > 0, "Liệt kê được card mạng");
        Check(adapters.Where(a => a.Recommended).All(a => a.IsUp && a.HasGateway), "Card đề xuất đều đang kết nối và có gateway");
        Check(adapters.All(a => !string.IsNullOrEmpty(a.Id) && a.Id.StartsWith("{")), "Mỗi card có GUID");
        var bogus = NetworkAdapterService.ReadRegistryState("{00000000-0000-0000-0000-000000000000}");
        Check(!bogus.Ipv4Static && !bogus.Ipv6Static && bogus.Ipv4Servers.Count == 0 && bogus.Ipv6Servers.Count == 0, "GUID không tồn tại → 'tự động', không ném ngoại lệ");
        foreach (var a in adapters.Where(x => x.IsUp)) Console.WriteLine("    " + (a.Recommended ? "[*] " : "[ ] ") + a + " | " + a.SourceText());
        return adapters;
    }

    private static void TestSimulatedApplyRestore(List<DnsProvider> builtIn, List<AdapterInfo> adapters)
    {
        Section("Mô phỏng áp dụng / thu hồi DNS (netsh giả lập, không đổi gì thật)");
        var target = adapters.FirstOrDefault(a => a.Recommended) ?? adapters.FirstOrDefault();
        if (target == null)
        {
            Console.WriteLine("  (bỏ qua: không có card mạng)");
            return;
        }
        var calls = new List<string>();
        bool failIpv6 = false;
        DnsApplier.CommandRunner = (exe, a2) =>
        {
            calls.Add(Path.GetFileName(exe) + " " + a2);
            bool fail = failIpv6 && a2.Contains(" ipv6 ");
            return new CommandResult { ExitCode = fail ? 1 : 0, Output = fail ? "Simulated failure" : "Ok." };
        };
        var logLines = new List<string>();
        Action<string> log = s => logLines.Add(s);
        try
        {
            var google = builtIn.First(p => p.Id == "google");
            var comodo = builtIn.First(p => p.Id == "comodo");
            string q = "name=\"" + target.Name + "\"";

            BackupService.Save(new BackupData());
            bool capturedNow;
            var orig = BackupService.EnsureCaptured(target, out capturedNow);
            var reg = NetworkAdapterService.ReadRegistryState(target.Id);
            Check(capturedNow && orig.Ipv4Static == reg.Ipv4Static && orig.Ipv4Servers.SequenceEqual(reg.Ipv4Servers)
                  && orig.Ipv6Static == reg.Ipv6Static && orig.Ipv6Servers.SequenceEqual(reg.Ipv6Servers) && !string.IsNullOrEmpty(orig.CapturedAt),
                "Sao lưu: chụp đúng trạng thái registry của " + target.Name, orig.Describe());
            Check(BackupService.HasAny() && File.Exists(AppPaths.BackupFile), "Sao lưu: đã ghi file original-dns.json");
            bool again;
            var orig2 = BackupService.EnsureCaptured(target, out again);
            Check(!again && orig2.CapturedAt == orig.CapturedAt, "Sao lưu: lần hai không chụp đè (giữ bản gốc thật)");

            calls.Clear();
            var res = DnsApplier.ApplyProvider(target, google, log);
            string[] expected =
            {
                "netsh.exe interface ipv4 set dnsservers " + q + " source=static address=8.8.8.8 validate=no",
                "netsh.exe interface ipv4 add dnsservers " + q + " address=8.8.4.4 index=2 validate=no",
                "netsh.exe interface ipv6 set dnsservers " + q + " source=static address=2001:4860:4860::8888 validate=no",
                "netsh.exe interface ipv6 add dnsservers " + q + " address=2001:4860:4860::8844 index=2 validate=no"
            };
            Check(res.Count == 4 && res.All(r => r.Ok) && calls.SequenceEqual(expected), "Áp dụng Google: đúng 4 lệnh netsh IPv4 + IPv6", string.Join(" | ", calls));

            calls.Clear();
            res = DnsApplier.ApplyProvider(target, comodo, log);
            Check(res.Count == 3 && res.All(r => r.Ok) && calls[2] == "netsh.exe interface ipv6 set dnsservers " + q + " source=static address=none validate=no",
                "Áp dụng DNS không có IPv6: xoá DNS IPv6 (address=none)", string.Join(" | ", calls));

            calls.Clear();
            var fl = DnsApplier.FlushDns(log);
            Check(fl.Ok && calls.Count == 1 && calls[0] == "ipconfig.exe /flushdns", "Flush DNS: ipconfig /flushdns");

            calls.Clear();
            res = DnsApplier.Restore(orig, log);
            var exp2 = new List<string>();
            AddExpectedRestore(exp2, q, "ipv4", orig.Ipv4Static, orig.Ipv4Servers);
            AddExpectedRestore(exp2, q, "ipv6", orig.Ipv6Static, orig.Ipv6Servers);
            Check(res.All(r => r.Ok) && calls.SequenceEqual(exp2), "Thu hồi: lệnh khớp trạng thái gốc (" + orig.Describe() + ")", string.Join(" | ", calls));

            var dhcpState = new AdapterDnsState { AdapterId = target.Id, AdapterName = target.Name, Ipv4Static = false, Ipv6Static = false };
            calls.Clear();
            DnsApplier.Restore(dhcpState, log);
            Check(calls.Count == 2 && calls[0] == "netsh.exe interface ipv4 set dnsservers " + q + " source=dhcp" && calls[1] == "netsh.exe interface ipv6 set dnsservers " + q + " source=dhcp",
                "Thu hồi về DHCP: source=dhcp cho cả IPv4 và IPv6", string.Join(" | ", calls));

            var staticEmpty = new AdapterDnsState { AdapterId = target.Id, AdapterName = target.Name, Ipv4Static = true, Ipv4Servers = new List<string> { "192.168.1.1", "192.168.1.2", "192.168.1.3" }, Ipv6Static = false };
            calls.Clear();
            DnsApplier.Restore(staticEmpty, log);
            Check(calls.Count == 4 && calls[2] == "netsh.exe interface ipv4 add dnsservers " + q + " address=192.168.1.3 index=3 validate=no",
                "Thu hồi tĩnh 3 địa chỉ: index tăng dần", string.Join(" | ", calls));

            calls.Clear();
            DnsApplier.Restore(orig, "Card Đổi Tên", log);
            Check(calls.Count > 0 && calls.All(c => c.Contains("name=\"Card Đổi Tên\"")), "Thu hồi: dùng tên card hiện tại khi card đã bị đổi tên");

            calls.Clear();
            DnsApplier.SetDhcp("Weird \"Name\"", "ipv4", log);
            Check(calls[0] == "netsh.exe interface ipv4 set dnsservers name=\"Weird Name\" source=dhcp", "netsh: dấu ngoặc kép trong tên card được loại bỏ");

            failIpv6 = true;
            calls.Clear();
            logLines.Clear();
            res = DnsApplier.ApplyProvider(target, google, log);
            Check(res.Count(r => !r.Ok) == 2 && res.Count(r => r.Ok) == 2 && res.First(r => !r.Ok).Output == "Simulated failure",
                "Áp dụng khi IPv6 lỗi: IPv4 vẫn OK, lỗi IPv6 được báo riêng");
            Check(logLines.Any(l => l.Contains("lỗi (mã 1)")), "Nhật ký ghi mã lỗi lệnh");
            failIpv6 = false;

            DnsApplier.CommandRunner = (exe, a2) => { throw new InvalidOperationException("boom"); };
            var ex = DnsApplier.FlushDns(log);
            Check(!ex.Ok && ex.Output == "boom", "Ngoại lệ khi chạy lệnh → kết quả lỗi, không crash");

            BackupService.Remove(target.Id);
            Check(!BackupService.HasAny(), "Sao lưu: xoá sau khi thu hồi thành công");
            BackupService.Remove("{khong-ton-tai}");
            Check(!BackupService.HasAny(), "Sao lưu: xoá Id không tồn tại không gây lỗi");
        }
        finally
        {
            DnsApplier.CommandRunner = null;
        }
    }

    private static void AddExpectedRestore(List<string> exp, string q, string family, bool isStatic, List<string> servers)
    {
        if (!isStatic)
        {
            exp.Add("netsh.exe interface " + family + " set dnsservers " + q + " source=dhcp");
            return;
        }
        if (servers == null || servers.Count == 0)
        {
            exp.Add("netsh.exe interface " + family + " set dnsservers " + q + " source=static address=none validate=no");
            return;
        }
        exp.Add("netsh.exe interface " + family + " set dnsservers " + q + " source=static address=" + servers[0] + " validate=no");
        for (int i = 1; i < servers.Count; i++)
            exp.Add("netsh.exe interface " + family + " add dnsservers " + q + " address=" + servers[i] + " index=" + (i + 1) + " validate=no");
    }

    private static ProviderTestResult Mk(string name, int dns, int https, double ms)
    {
        return new ProviderTestResult
        {
            Provider = new DnsProvider { Id = name, Name = name, Ipv4Primary = "1.1.1.1" },
            TotalDomains = 5,
            DnsOkCount = dns,
            HttpsOkCount = https,
            AvgDnsMs = ms / 2,
            AvgHttpsMs = ms / 2
        };
    }

    private static void TestRankingAndTester(List<DnsProvider> builtIn)
    {
        Section("Xếp hạng & bộ kiểm tra");
        var ranked = ProviderTester.Rank(new[] { Mk("slowGood", 5, 5, 900), Mk("fastPartial", 5, 3, 100), Mk("fastGood", 5, 5, 200), Mk("dead", 0, 0, 0), Mk("dnsOnly", 5, 0, 50) });
        string order = string.Join(",", ranked.Select(r => r.Provider.Name));
        Check(order == "fastGood,slowGood,fastPartial,dnsOnly,dead", "Xếp hạng: HTTPS OK → DNS OK → độ trễ", order);
        Check(Mk("x", 5, 5, 10).Score == 25 && Mk("x", 5, 0, 10).Score == 10 && Mk("x", 0, 0, 0).Score == 0, "Điểm: HTTPS 3 + DNS 2 mỗi tên miền");
        Check(Mk("x", 5, 5, 10).StatusText() == "Tốt" && Mk("x", 5, 2, 10).StatusText() == "Một phần"
              && Mk("x", 5, 0, 10).StatusText() == "DNS OK, HTTPS lỗi" && Mk("x", 0, 0, 0).StatusText() == "Không hoạt động", "Trạng thái theo kết quả");
        var applied = Mk("y", 5, 5, 100);
        ProviderTester.ApplyToProvider(applied);
        Check(applied.Provider.LastScore == 25 && applied.Provider.LastLatencyMs == 100 && applied.Provider.LastTestSummary.StartsWith("Tốt") && !string.IsNullOrEmpty(applied.Provider.LastTestedAt),
            "ApplyToProvider ghi kết quả vào provider");

        var errRes = ProviderTester.Test(new DnsProvider { Name = "Rỗng" }, new TestOptions(), CancellationToken.None);
        Check(!string.IsNullOrEmpty(errRes.Error) && errRes.Score == 0 && errRes.StatusText().StartsWith("LỖI"), "Provider không có địa chỉ → Error, không ném ngoại lệ");

        var cts = new CancellationTokenSource();
        cts.Cancel();
        bool cancelled = false;
        try { ProviderTester.Test(builtIn[0], new TestOptions(), cts.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "Token đã huỷ → OperationCanceledException (dừng đúng cách)");

        var opt = TestOptions.FromConfig(new AppConfig { Concurrency = 3, DnsTimeoutMs = 1234, HttpsTimeoutMs = 2345 });
        Check(opt.Concurrency == 3 && opt.DnsTimeoutMs == 1234 && opt.HttpsTimeoutMs == 2345, "TestOptions.FromConfig");

        var dc = new DomainCheckResult { Host = "h", DnsOk = false, DnsError = "x" };
        Check(dc.HttpsText() == "(bỏ qua)" && dc.DnsText().StartsWith("LỖI") && !dc.Ok, "DomainCheckResult: DNS lỗi → HTTPS bỏ qua");
        Check(SteamChecker.Domains.Length == 5 && SteamChecker.Domains.Contains("login.steampowered.com") && SteamChecker.Domains.Contains("help.steampowered.com"), "Đủ 5 tên miền Steam");
    }

    private static void TestNetworkEdgeCases()
    {
        Section("Tình huống mạng bất thường (cần internet)");
        string nx = "khong-ton-tai-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".example";
        var r1 = DnsResolver.Query(IPAddress.Parse("8.8.8.8"), nx, DnsRecordType.A, 3000);
        Check(!r1.Success && r1.Error != null && r1.Error.Contains("NXDOMAIN"), "DNS: tên miền không tồn tại → NXDOMAIN", r1.Error);

        var sw = Stopwatch.StartNew();
        var r2 = DnsResolver.Query(IPAddress.Parse("192.0.2.1"), "store.steampowered.com", DnsRecordType.A, 1000);
        sw.Stop();
        Check(!r2.Success && r2.Error != null && r2.Error.Contains("Hết thời gian") && sw.ElapsedMilliseconds < 2500, "DNS: máy chủ không phản hồi → timeout đúng hạn", r2.Error + " sau " + sw.ElapsedMilliseconds + " ms");

        var r3 = DnsResolver.Query(IPAddress.Parse("8.8.8.8"), "", DnsRecordType.A, 1000);
        Check(!r3.Success && r3.Error != null, "DNS: tên miền rỗng → lỗi, không ném ngoại lệ");

        var r4 = DnsResolver.Query(IPAddress.Parse("1.1.1.1"), "www.cloudflare.com", DnsRecordType.AAAA, 3000);
        Check(r4.Success && r4.Addresses.All(a => a.AddressFamily == AddressFamily.InterNetworkV6), "DNS: AAAA trả địa chỉ IPv6", r4.Error);

        var r5 = DnsResolver.Query(IPAddress.Parse("8.8.8.8"), "store.steampowered.com", DnsRecordType.A, 3000);
        Check(r5.Success && r5.Addresses.Count > 0 && r5.ElapsedMs > 0, "DNS: A của Steam qua 8.8.8.8" + (r5.Cnames.Count > 0 ? " (có CNAME)" : ""), r5.Error);

        // IP giả: dùng IP thật của Steam (Akamai) nhưng bắt tay với SNI của một tên miền khác → chứng chỉ không khớp.
        // (Google/Cloudflare im lặng với SNI lạ nên không dùng làm máy chủ thử được.)
        var sq = DnsResolver.Query(IPAddress.Parse("8.8.8.8"), "store.steampowered.com", DnsRecordType.A, 3000);
        if (sq.Success)
        {
            var p1 = HttpsProbe.Probe(sq.Addresses[0], "www.google.com", 6000);
            bool detected = !(p1.TlsOk && p1.HttpOk);
            Check(detected, "HTTPS: IP không phục vụ tên miền (chứng chỉ sai tên) bị phát hiện"
                + (!string.IsNullOrEmpty(p1.PolicyErrors) ? " [" + p1.PolicyErrors + "]" : ""), p1.Error);
        }
        else Check(false, "HTTPS: IP giả – không phân giải được store.steampowered.com để thử", sq.Error);

        sw.Restart();
        var p2 = HttpsProbe.Probe(IPAddress.Parse("192.0.2.1"), "store.steampowered.com", 1000);
        sw.Stop();
        Check(!p2.TcpOk && p2.Error != null && p2.Error.Contains("hết thời gian") && sw.ElapsedMilliseconds < 2500, "HTTPS: cổng 443 không phản hồi → timeout đúng hạn", p2.Error);

        var p3 = HttpsProbe.Probe(IPAddress.Loopback, "store.steampowered.com", 2000);
        Check(!p3.TlsOk && p3.Error != null, "HTTPS: localhost không thể giả làm Steam", p3.Error);

        var sys = SteamChecker.CheckViaSystem(nx, 2000, 2000);
        Check(!sys.DnsOk && !sys.HttpsOk && sys.HttpsText() == "(bỏ qua)", "Steam check: tên miền không tồn tại → DNS lỗi, bỏ qua HTTPS", sys.DnsError);

        var via = SteamChecker.CheckViaServer(IPAddress.Parse("8.8.8.8"), "store.steampowered.com", 3000, 6000);
        Check(via.DnsOk && via.Ips.Count > 0, "Steam check qua máy chủ chỉ định: DNS OK", via.DnsError);
        Console.WriteLine("    " + via.Summary());

        var dead = ProviderTester.Test(new DnsProvider { Name = "TEST-NET", Ipv4Primary = "192.0.2.1" }, new TestOptions { DnsTimeoutMs = 800, HttpsTimeoutMs = 800 }, CancellationToken.None);
        Check(dead.DnsOkCount == 0 && dead.HttpsOkCount == 0 && dead.Domains.Count == 5 && dead.StatusText() == "Không hoạt động", "Provider chết → 0/5, trạng thái 'Không hoạt động'");

        var fallback = ProviderTester.Test(new DnsProvider { Name = "Dự phòng", Ipv4Primary = "192.0.2.1", Ipv4Secondary = "8.8.8.8" }, new TestOptions { DnsTimeoutMs = 800 }, CancellationToken.None);
        Check(fallback.DnsOkCount == 5 && fallback.Domains.All(d => d.Resolver == "8.8.8.8"), "DNS chính chết → tự dùng DNS phụ", fallback.Summary());

        bool threw = false;
        string msg = null;
        try { DiscoveryService.FetchCountry("zz", 0.9, 5, false, null); }
        catch (Exception ex) { threw = true; msg = ex.Message; }
        Check(threw, "Discovery: mã quốc gia không có → báo lỗi, không treo", msg);

        string ipErr;
        string pub4 = VpnService.GetPublicIp(6000, out ipErr);
        IPAddress pub4Addr;
        Check(pub4 != null && IPAddress.TryParse(pub4, out pub4Addr) && pub4Addr.AddressFamily == AddressFamily.InterNetwork, "IP công cộng: luôn là IPv4 (máy có IPv6 vẫn trả IPv4)", pub4 ?? ipErr);
        string pub6 = VpnService.GetPublicIpv6(4000);
        IPAddress pub6Addr;
        Check(pub6 == null || (IPAddress.TryParse(pub6, out pub6Addr) && pub6Addr.AddressFamily == AddressFamily.InterNetworkV6), "IPv6 công cộng: null hoặc IPv6 hợp lệ", pub6 ?? "(không có IPv6)");

        var live = UpdateService.Check(UpdateService.DefaultManifestUrl, new Version(0, 0, 0, 1), false, 20000);
        Check(live.Ok ? (live.Latest >= new Version(1, 0, 0, 0) && live.Package != null && live.Package.Url.StartsWith(AppInfo.ReleasesUrl + "/download/", StringComparison.Ordinal))
                      : live.Error.Contains("404"),
            "Manifest GitHub Releases (…/releases/latest/download/latest.json): có bản mới hoặc 404 khi chưa có Release nào", live.Ok ? live.Describe() : live.Error);

        string ghErr;
        var ghLive = UpdateService.ManifestFromGitHubApi(20000, out ghErr);
        Check(ghLive != null && UpdateService.ParseVersion(ghLive.Version) >= new Version(1, 0, 3, 0) && ghLive.Portable != null && ghLive.Portable.Sha256.Length == 64,
            "GitHub API dự phòng: Release mới nhất có SteamConnectToolkit.exe kèm SHA-256", ghLive != null ? ghLive.Version + " " + ghLive.Portable.Url : ghErr);
    }

    private static void TestLegacyMigration(string tmp)
    {
        Section("Di trú thư mục dữ liệu phiên bản cũ (SteamDnsSwitcher → SteamConnectToolkit)");
        string legacy = Path.Combine(tmp, "legacy-SteamDnsSwitcher");
        string fresh = Path.Combine(tmp, "new-SteamConnectToolkit");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "original-dns.json"), "{\"Adapters\":[]}");
        File.WriteAllText(Path.Combine(legacy, "history.json"), "{\"Entries\":[]}");
        bool moved = AppPaths.MigrateLegacy(legacy, fresh);
        Check(moved && File.Exists(Path.Combine(fresh, "original-dns.json")) && File.Exists(Path.Combine(fresh, "history.json")) && !Directory.Exists(legacy),
            "Di trú: chuyển toàn bộ file sang thư mục mới");
        Check(!AppPaths.MigrateLegacy(legacy, fresh), "Di trú: không có thư mục cũ → không làm gì");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "config.json"), "{}");
        Check(!AppPaths.MigrateLegacy(legacy, fresh) && !File.Exists(Path.Combine(fresh, "config.json")), "Di trú: thư mục mới đã có dữ liệu → giữ nguyên, không ghi đè");
        Check(!AppPaths.MigrateLegacy(Path.Combine(tmp, "khong-ton-tai"), Path.Combine(tmp, "x")), "Di trú: đường dẫn không tồn tại → an toàn");
    }

    private static void TestVpn(bool network)
    {
        Section("VPN (Windows tích hợp)");
        // Phân tích CSV của Get-VpnConnection
        string csv = "\"Name\",\"ServerAddress\",\"TunnelType\",\"ConnectionStatus\",\"EncryptionLevel\",\"SplitTunneling\",\"RememberCredential\",\"AllUser\",\"Auth\"\r\n"
                   + "\"VPN Công ty\",\"vpn.example.com\",\"Ikev2\",\"Disconnected\",\"Required\",\"False\",\"True\",\"False\",\"Eap\"\r\n"
                   + "\"L2TP, nhà\",\"203.0.113.9\",\"L2tp\",\"Connected\",\"Optional\",\"True\",\"False\",\"True\",\"MSChapv2+Chap\"\r\n";
        var profiles = VpnService.ParseProfiles(csv);
        Check(profiles.Count == 2 && profiles[0].Name == "VPN Công ty" && profiles[0].TunnelType == "Ikev2" && profiles[0].RememberCredential && !profiles[0].AllUsers,
            "ParseProfiles: dòng 1 (Unicode, IKEv2)");
        Check(profiles[1].Name == "L2TP, nhà" && profiles[1].Server == "203.0.113.9" && profiles[1].SplitTunneling && profiles[1].AllUsers
              && profiles[1].Authentication == "MSChapv2+Chap" && profiles[1].Status == "Connected", "ParseProfiles: dòng 2 (dấu phẩy trong tên, cờ bool)");
        VpnService.MarkConnected(profiles);
        Check(profiles[1].IsConnected && !profiles[0].IsConnected, "MarkConnected: theo ConnectionStatus của Windows");
        Check(VpnService.ParseProfiles("").Count == 0 && VpnService.ParseProfiles("\"Name\"\r\n").Count == 0, "ParseProfiles: rỗng / chỉ có header");
        Check(profiles[0].TunnelTypeText() == "IKEv2" && profiles[1].TunnelTypeText() == "L2TP/IPsec" && new VpnProfile { TunnelType = "Sstp" }.TunnelTypeText() == "SSTP",
            "TunnelTypeText");

        // Script thêm/xoá: quoting, cờ
        var spec = new VpnProfileSpec { Name = "Steam's VPN", Server = "vpn.example.com", TunnelType = "L2tp", L2tpPsk = "p'sk", RememberCredential = true, AllUsers = true };
        string add = VpnService.BuildAddScript(spec);
        Check(add.Contains("-Name 'Steam''s VPN'") && add.Contains("-ServerAddress 'vpn.example.com'") && add.Contains("-TunnelType L2tp")
              && add.Contains("-L2tpPsk 'p''sk'") && add.Contains("-AuthenticationMethod MSChapv2") && add.Contains("-RememberCredential")
              && add.Contains("-AllUserConnection") && !add.Contains("-SplitTunneling") && add.Contains("$ErrorActionPreference='Stop'"),
            "BuildAddScript L2TP: nhân đôi dấu nháy đơn, đủ cờ", add);
        string addIke = VpnService.BuildAddScript(new VpnProfileSpec { Name = "A", Server = "b", TunnelType = "Ikev2", SplitTunneling = true, RememberCredential = false });
        Check(!addIke.Contains("-AuthenticationMethod") && addIke.Contains("-SplitTunneling") && !addIke.Contains("-RememberCredential") && !addIke.Contains("-L2tpPsk"),
            "BuildAddScript IKEv2: dùng EAP mặc định, split tunneling", addIke);
        string rem = VpnService.BuildRemoveScript("X'Y", true);
        Check(rem.Contains("-Name 'X''Y'") && rem.Contains("-AllUserConnection") && rem.Contains("-Force"), "BuildRemoveScript");
        Check(new VpnProfileSpec { Name = "", Server = "x" }.Validate() != null && new VpnProfileSpec { Name = "a", Server = "" }.Validate() != null
              && new VpnProfileSpec { Name = "a", Server = "x y" }.Validate() != null && new VpnProfileSpec { Name = "a/b", Server = "x" }.Validate() != null
              && new VpnProfileSpec { Name = "a", Server = "x", TunnelType = "Foo" }.Validate() != null
              && new VpnProfileSpec { Name = "VPN Nhà", Server = "vpn.example.com" }.Validate() == null, "VpnProfileSpec.Validate");

        // Hook PowerShell: AddProfile không hợp lệ không gọi PowerShell; hợp lệ thì gọi
        int calls = 0;
        VpnService.PowerShellRunner = s => { calls++; return new CommandResult { ExitCode = 0, Output = "OK", Command = "ps" }; };
        try
        {
            var bad = VpnService.AddProfile(new VpnProfileSpec { Name = "", Server = "x" }, null);
            int callsAfterBad = calls;
            var good = VpnService.AddProfile(new VpnProfileSpec { Name = "T", Server = "x" }, null);
            Check(!bad.Ok && callsAfterBad == 0 && good.Ok && calls == 1, "AddProfile: kiểm tra hợp lệ trước khi gọi PowerShell", "calls=" + calls);
            VpnService.PowerShellRunner = s => new CommandResult { ExitCode = 0, Output = csv, Command = "ps" };
            var listed = VpnService.ListProfiles(null);
            Check(listed.Count == 2, "ListProfiles qua hook");
            VpnService.PowerShellRunner = s => new CommandResult { ExitCode = 1, Output = "Get-VpnConnection : lỗi giả lập", Command = "ps" };
            bool threw = false;
            try { VpnService.ListProfiles(null); } catch (InvalidOperationException) { threw = true; }
            Check(threw, "ListProfiles: PowerShell lỗi → ngoại lệ có thông điệp");
        }
        finally
        {
            VpnService.PowerShellRunner = null;
        }

        // Mã lỗi RAS
        Check(!string.IsNullOrEmpty(VpnService.RasHint(691)) && !string.IsNullOrEmpty(VpnService.RasHint(809)) && VpnService.RasHint(12345) == null, "RasHint: có gợi ý cho mã phổ biến");
        string d = VpnService.DescribeRasError(691);
        Check(d.StartsWith("Lỗi 691") && d.Contains("→"), "DescribeRasError gộp thông báo hệ thống + gợi ý", d);

        // RasDial thật với cấu hình không tồn tại → 623 (chứng tỏ P/Invoke đúng cấu trúc; 632 = sai kích thước)
        var dial = VpnService.Connect("SCT-khong-ton-tai-" + Guid.NewGuid().ToString("N").Substring(0, 6), false, "user", "pass", null);
        Check(!dial.Ok && dial.ErrorCode == 623, "RasDial: cấu hình không tồn tại → lỗi 623 (cấu trúc RASDIALPARAMS đúng)", dial.ErrorCode + " " + dial.Message);

        // DPAPI
        string prot = CredentialStore.Protect("mật khẩu bí mật");
        Check(!string.IsNullOrEmpty(prot) && prot != "mật khẩu bí mật" && CredentialStore.Unprotect(prot) == "mật khẩu bí mật", "DPAPI: mã hoá/giải mã mật khẩu");
        Check(CredentialStore.Unprotect("khong-phai-base64!") == null && CredentialStore.Protect("") == "" && CredentialStore.Unprotect("") == "", "DPAPI: dữ liệu hỏng → null, rỗng → rỗng");
        var cfg = new AppConfig();
        CredentialStore.Save(cfg, "VPN A", "user1", "pw1", true);
        CredentialStore.Save(cfg, "VPN B", "user2", "pw2", false);
        var a = CredentialStore.Find(cfg, "vpn a");
        var b = CredentialStore.Find(cfg, "VPN B");
        Check(a != null && a.UserName == "user1" && CredentialStore.Unprotect(a.PasswordProtected) == "pw1" && b != null && b.PasswordProtected == "", "CredentialStore: lưu có/không mật khẩu, tìm không phân biệt hoa thường");
        CredentialStore.Save(cfg, "VPN A", "", "", false);
        Check(CredentialStore.Find(cfg, "VPN A") == null, "CredentialStore: xoá khi không còn gì để lưu");

        // Danh sách VPN thật của máy (PowerShell) – chỉ đọc
        try
        {
            var real = VpnService.ListProfiles(null);
            Check(true, "Get-VpnConnection thật: " + real.Count + " cấu hình" + (real.Count > 0 ? " (" + string.Join(", ", real.Select(p => p.Name + "=" + p.StatusText())) + ")" : ""));
        }
        catch (Exception ex)
        {
            Check(false, "Get-VpnConnection thật", ex.Message);
        }

        if (network)
        {
            // Tạo cấu hình tạm tới địa chỉ TEST-NET, quay số phải thất bại có mã lỗi, rồi xoá.
            string name = "SCT-SelfTest-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            var tmpSpec = new VpnProfileSpec { Name = name, Server = "192.0.2.1", TunnelType = "Ikev2", RememberCredential = false };
            var add2 = VpnService.AddProfile(tmpSpec, null);
            Check(add2.Ok, "Add-VpnConnection thật (cấu hình tạm, người dùng này)", add2.Output);
            if (add2.Ok)
            {
                try
                {
                    var listed = VpnService.ListProfiles(null);
                    Check(listed.Any(p => p.Name == name && p.Server == "192.0.2.1" && p.TunnelType == "Ikev2"), "Cấu hình tạm xuất hiện trong danh sách");
                    Console.WriteLine("    Đang quay số tới 192.0.2.1 (sẽ thất bại, chờ tối đa ~1 phút)…");
                    var r2 = VpnService.Connect(name, false, "user", "pass", null);
                    Check(!r2.Ok && r2.ErrorCode > 0 && r2.ErrorCode != 623 && r2.ErrorCode != 632, "RasDial tới máy chủ chết → lỗi RAS có mã (" + r2.ErrorCode + ")", r2.Message);
                    Console.WriteLine("    " + r2.Message + " (" + Math.Round(r2.ElapsedMs / 1000) + " s)");
                    var r3 = VpnService.Connect(name, false, "", "", null);
                    Check(!r3.Ok, "rasdial với tài khoản đã nhớ (không có) → báo lỗi, không treo", r3.Message);
                }
                finally
                {
                    var rm = VpnService.RemoveProfile(name, false, null);
                    Check(rm.Ok && !VpnService.ListProfiles(null).Any(p => p.Name == name), "Remove-VpnConnection thật: xoá cấu hình tạm", rm.Output);
                }
            }
        }
    }

    private static string GateRow(string cc, string country, string hostName, string ip, int sessions, string speed, int ping, string log, bool l2tp, bool sstp, string sstpHost, string score)
    {
        var sb = new StringBuilder();
        sb.Append("<tr class='vg_table_row'>");
        sb.Append("<td><img src='../images/flags/" + cc + ".png' /><br />" + country + "</td>");
        sb.Append("<td><b>" + hostName + "</b><br /><span>" + ip + "</span><br />(ddns)</td>");
        sb.Append("<td>" + sessions + " sessions<br />3 days<br />Total 1,234 users</td>");
        sb.Append("<td>" + speed + " Mbps<br />Ping: " + ping + " ms<br /><br />12.34 GB<br />Logging policy:<br />" + log + "</td>");
        sb.Append("<td>SSL-VPN<br />TCP: 443</td>");
        sb.Append("<td>" + (l2tp ? "<b>L2TP/IPsec</b><br />Connect guide" : "") + "</td>");
        sb.Append("<td>OpenVPN<br />Config file</td>");
        sb.Append("<td>" + (sstp ? "<b>MS-SSTP</b><br />Connect guide<br />SSTP Hostname :<br /><b>" + sstpHost + "</b>" : "") + "</td>");
        sb.Append("<td>By someone</td>");
        sb.Append("<td>" + score + "</td>");
        sb.Append("</tr>");
        return sb.ToString();
    }

    private static void TestVpnGate(bool network)
    {
        Section("VPN Gate (VPN miễn phí)");
        string html = "<html><body><table><tr><td>header L2TP/IPsec MS-SSTP</td></tr>"
            + GateRow("JP", "Japan", "public-vpn-120.opengw.net", "219.100.37.101", 97, "411.52", 8, "2 Weeks", true, true, "public-vpn-120.opengw.net", "2,920,261")
            + GateRow("US", "United States", "vpn988046136.opengw.net", "156.47.87.249", 138, "117.00", 10, "2 Weeks", false, true, "vpn988046136.opengw.net:1575", "2,283,881")
            + GateRow("KR", "Korea Republic of", "vpn111222333.opengw.net", "1.2.3.4", 5, "20.5", 40, "Permanent", false, false, "", "900,000")
            + GateRow("TH", "Thailand", "vpn444555666.opengw.net", "5.6.7.8", 9, "50", 30, "2 Weeks", true, false, "", "1,500,000")
            + "</table></body></html>";
        var list = VpnGateService.ParseHtml(html);
        Check(list.Count == 4, "ParseHtml: đọc đủ 4 dòng", list.Count.ToString());
        var jp = list.FirstOrDefault(s => s.Ip == "219.100.37.101");
        Check(jp != null && jp.CountryCode == "JP" && jp.HostName == "public-vpn-120.opengw.net" && jp.IsOfficial && jp.HasL2tp && jp.HasSstp
              && jp.SstpHost == "public-vpn-120.opengw.net" && jp.SstpPort == 443 && jp.PingMs == 8 && Math.Abs(jp.SpeedMbps - 411.52) < 0.01
              && jp.Sessions == 97 && jp.Score == 2920261 && jp.LogPolicy == "2 Weeks",
            "ParseHtml: máy chủ chính thức JP (SSTP + L2TP, điểm, ping, tốc độ, log)", jp != null ? jp.ToString() + " " + jp.ProtocolsText() + " " + jp.Score + " " + jp.LogPolicy : "null");
        var us = list.FirstOrDefault(s => s.Ip == "156.47.87.249");
        Check(us != null && us.HasSstp && !us.HasL2tp && us.SstpHost == "vpn988046136.opengw.net:1575" && us.SstpPort == 1575 && !us.IsOfficial,
            "ParseHtml: SSTP cổng khác 443", us != null ? us.SstpHost + " " + us.SstpPort : "null");
        var kr = list.FirstOrDefault(s => s.Ip == "1.2.3.4");
        Check(kr != null && !kr.HasSstp && !kr.HasL2tp && kr.PreferredProtocol(true) == null && kr.ProtocolsText().Contains("OpenVPN"), "ParseHtml: máy chủ chỉ OpenVPN → không dùng được");
        Check(VpnGateService.ParseHtml("").Count == 0 && VpnGateService.ParseHtml("<html><table><tr><td>x</td></tr></table></html>").Count == 0, "ParseHtml: HTML rỗng / không có bảng");

        var ranked = VpnGateService.Rank(list, true);
        Check(ranked.Count == 3 && ranked[0].Ip == "219.100.37.101" && ranked[1].Ip == "156.47.87.249" && ranked[2].Ip == "5.6.7.8",
            "Rank (ưu tiên SSTP): JP chính thức (SSTP) → US (SSTP, xa) → TH (gần nhưng chỉ L2TP)", string.Join(",", ranked.Select(r => r.CountryCode)));
        var rankedL = VpnGateService.Rank(list, false);
        Check(rankedL.Count == 3 && rankedL[0].Ip == "219.100.37.101" && rankedL[1].Ip == "5.6.7.8" && rankedL[2].Ip == "156.47.87.249",
            "Rank (không ưu tiên SSTP): JP chính thức → TH (gần) → US (xa)", string.Join(",", rankedL.Select(r => r.CountryCode)));
        us.ProbeMs = 120; jp.ProbeMs = -2; list.First(s => s.Ip == "5.6.7.8").ProbeMs = 80;
        var ranked2 = VpnGateService.Rank(list, true);
        Check(ranked2[0].Ip == "156.47.87.249" || ranked2[0].Ip == "5.6.7.8", "Rank: máy chủ không tới được bị đẩy xuống cuối", string.Join(",", ranked2.Select(r => r.Ip)));
        Check(ranked2.Last().Ip == "219.100.37.101", "Rank: máy chủ không tới được ở cuối");

        Check(jp.PreferredProtocol(true) == "Sstp" && jp.PreferredProtocol(false) == "L2tp" && us.PreferredProtocol(false) == "Sstp", "PreferredProtocol theo tuỳ chọn");
        var spec = VpnGateService.ToProfileSpec(jp, true);
        Check(spec != null && spec.TunnelType == "Sstp" && spec.Server == "public-vpn-120.opengw.net" && spec.Name == "VPN Gate JP public-vpn-120" && spec.L2tpPsk == "" && !spec.SplitTunneling && spec.Validate() == null,
            "ToProfileSpec SSTP: dùng hostname opengw.net, không split tunnel", spec != null ? spec.Name + " " + spec.Server : "null");
        var specL = VpnGateService.ToProfileSpec(jp, false);
        Check(specL != null && specL.TunnelType == "L2tp" && specL.Server == "219.100.37.101" && specL.L2tpPsk == "vpn" && specL.Validate() == null, "ToProfileSpec L2TP: dùng IP + PSK vpn");
        var specP = VpnGateService.ToProfileSpec(us, true);
        Check(specP != null && specP.Server == "vpn988046136.opengw.net:1575" && specP.Validate() == null, "ToProfileSpec SSTP cổng riêng: host:port hợp lệ");
        Check(VpnGateService.ToProfileSpec(kr, true) == null, "ToProfileSpec: máy chủ không hỗ trợ → null");
        Check(VpnService.GetL2tpNatRule() == null || VpnService.GetL2tpNatRule().HasValue, "GetL2tpNatRule: đọc registry không lỗi (" + (VpnService.GetL2tpNatRule().HasValue ? VpnService.GetL2tpNatRule().Value.ToString() : "chưa đặt") + ")");

        VpnGateService.HtmlFetcher = () => html;
        try
        {
            var fetched = VpnGateService.Fetch(1000, null);
            Check(fetched.Count == 4, "Fetch qua hook");
        }
        finally
        {
            VpnGateService.HtmlFetcher = null;
        }

        if (network)
        {
            try
            {
                var live = VpnGateService.Fetch(20000, null);
                int sstp = live.Count(s => s.HasSstp && !string.IsNullOrEmpty(s.SstpHost));
                Check(live.Count > 10 && sstp > 0, "VPN Gate thật: tải và phân tích danh sách (" + live.Count + " máy chủ, " + sstp + " SSTP, " + live.Count(s => s.HasL2tp) + " L2TP)");
                var top = VpnGateService.Rank(live, true).Take(3).ToList();
                foreach (var s in top) VpnGateService.Probe(s, 3000);
                Check(top.Any(s => s.Reachable), "VPN Gate thật: ít nhất 1 trong 3 máy chủ đầu tới được qua TCP",
                    string.Join(" | ", top.Select(s => s.ToString() + " " + Math.Round(s.ProbeMs) + "ms")));
                foreach (var s in top) Console.WriteLine("    " + s.CountryCode + " " + s.HostName + " " + s.ProtocolsText() + " tới được: " + (s.Reachable ? Math.Round(s.ProbeMs) + " ms" : "không") + ", điểm " + s.Score.ToString("N0"));
            }
            catch (Exception ex)
            {
                Check(false, "VPN Gate thật: tải danh sách", ex.Message);
            }
        }
    }

    private static BackupData OneBackup(AdapterInfo target, bool v4Static, string v4)
    {
        var st = new AdapterDnsState { AdapterId = target.Id, AdapterName = target.Name, Ipv4Static = v4Static, Ipv6Static = false };
        if (v4Static) st.Ipv4Servers = new List<string> { v4 };
        return new BackupData { Adapters = new List<AdapterDnsState> { st } };
    }

    private static void TestSessionAndRecovery(List<DnsProvider> builtIn, List<AdapterInfo> adapters)
    {
        Section("Phiên làm việc, hoàn tác khi lỗi, khôi phục toàn bộ, guardian");
        SessionService.Begin(12345);
        Check(SessionService.Load().Pid == 12345 && !SessionService.Load().CleanExit && !string.IsNullOrEmpty(SessionService.Load().StartedAt), "Session: Begin ghi pid, CleanExit=false");
        SessionService.AddVpnConnected("VPN A");
        SessionService.AddVpnConnected("vpn a");
        Check(SessionService.Load().VpnConnectedByApp.Count == 1, "Session: AddVpnConnected không trùng (không phân biệt hoa thường)");
        SessionService.RemoveVpnConnected("VPN A");
        Check(SessionService.Load().VpnConnectedByApp.Count == 0, "Session: RemoveVpnConnected");
        SessionService.MarkClean(true, "test");
        var s1 = SessionService.Load();
        Check(s1.CleanExit && s1.KeepOnExit && s1.Note == "test" && !string.IsNullOrEmpty(s1.EndedAt), "Session: MarkClean");

        var cfg = new AppConfig();
        BackupService.Save(new BackupData());
        Check(!SessionService.HasPending(s1, cfg), "HasPending: không có gì");
        cfg.VpnProfilesCreatedByApp.Add(new VpnProfileRecord { Name = "VPN Gate X", Auto = true });
        Check(SessionService.HasPending(s1, cfg) && SessionService.DescribePending(s1, cfg).Contains("VPN Gate X"), "HasPending/DescribePending: cấu hình VPN Gate do app tạo");
        cfg.VpnProfilesCreatedByApp[0].Auto = false;
        Check(!SessionService.HasPending(s1, cfg), "HasPending: cấu hình tự nhập không tính là thay đổi cần khôi phục");

        var target = adapters.FirstOrDefault(a => a.Recommended) ?? adapters.FirstOrDefault();
        if (target == null)
        {
            Console.WriteLine("  (bỏ qua: không có card mạng)");
            return;
        }
        var google = builtIn.First(p => p.Id == "google");
        var comodo = builtIn.First(p => p.Id == "comodo");
        var calls = new List<string>();
        var psCalls = new List<string>();
        bool failIpv6 = false;
        AdapterDnsState fakeState = null;
        DnsApplier.CommandRunner = (exe, a2) =>
        {
            calls.Add(Path.GetFileName(exe) + " " + a2);
            bool fail = failIpv6 && a2.Contains(" ipv6 ") && a2.Contains("source=static address=2001");
            return new CommandResult { ExitCode = fail ? 1 : 0, Output = fail ? "Simulated failure" : "Ok." };
        };
        NetworkAdapterService.StateReaderOverride = id => fakeState;
        VpnService.PowerShellRunner = script => { psCalls.Add(script); return new CommandResult { ExitCode = 0, Output = "OK" }; };
        try
        {
            var original = new AdapterDnsState { AdapterId = target.Id, AdapterName = target.Name, Ipv4Static = false, Ipv6Static = false };

            fakeState = new AdapterDnsState
            {
                AdapterId = target.Id, Ipv4Static = true, Ipv4Servers = new List<string> { "8.8.8.8", "8.8.4.4" },
                Ipv6Static = true, Ipv6Servers = new List<string> { "2001:4860:4860::8888", "2001:4860:4860::8844" }
            };
            calls.Clear();
            var o1 = DnsApplier.ApplyProviderSafely(target, google, original, null);
            Check(o1.Ok && !o1.RolledBack && calls.Count == 4, "ApplySafely: thành công khi xác minh registry khớp", o1.Error);

            fakeState = new AdapterDnsState { AdapterId = target.Id, Ipv4Static = true, Ipv4Servers = new List<string> { "1.1.1.1" }, Ipv6Static = false };
            calls.Clear();
            var o2 = DnsApplier.ApplyProviderSafely(target, google, original, null);
            Check(!o2.Ok && o2.RolledBack && o2.RollbackOk && o2.Error.Contains("xác minh") && calls.Count == 6
                  && calls[4].Contains("ipv4") && calls[4].Contains("source=dhcp") && calls[5].Contains("ipv6") && calls[5].Contains("source=dhcp"),
                "ApplySafely: xác minh lệch → hoàn tác về DHCP gốc", string.Join(" | ", calls));

            failIpv6 = true;
            calls.Clear();
            var o3 = DnsApplier.ApplyProviderSafely(target, google, original, null);
            Check(!o3.Ok && o3.RolledBack && o3.RollbackOk && o3.Error == "Simulated failure" && calls.Count(c => c.Contains("source=dhcp")) == 2,
                "ApplySafely: lệnh netsh lỗi → hoàn tác", string.Join(" | ", calls));
            failIpv6 = false;

            var st = new AdapterDnsState { Ipv4Servers = new List<string> { "8.8.8.8", "8.8.4.4" }, Ipv6Servers = new List<string> { "2001:4860:4860:0:0:0:0:8888", "2001:4860:4860::8844" } };
            Check(DnsApplier.VerifyApplied(st, google) == null, "VerifyApplied: chuẩn hoá cách viết IPv6");
            Check(DnsApplier.VerifyApplied(new AdapterDnsState { Ipv4Servers = new List<string> { "8.26.56.26", "8.20.247.20" }, Ipv6Servers = new List<string> { "2001:db8::1" } }, comodo) != null,
                "VerifyApplied: DNS không có IPv6 mà IPv6 chưa xoá → không khớp");
            Check(DnsApplier.VerifyApplied(new AdapterDnsState { Ipv4Servers = new List<string> { "8.8.4.4", "8.8.8.8" } }, google) != null, "VerifyApplied: sai thứ tự → không khớp");

            BackupService.Save(OneBackup(target, true, "192.168.1.1"));
            SessionService.Begin(777);
            SessionService.AddVpnConnected("SCT-Fake-VPN");
            var sess = SessionService.Load();
            var cfg2 = new AppConfig();
            cfg2.VpnProfilesCreatedByApp.Add(new VpnProfileRecord { Name = "VPN Gate JP test", Auto = true });
            cfg2.VpnProfilesCreatedByApp.Add(new VpnProfileRecord { Name = "Manual VPN", Auto = false });
            Check(SessionService.HasPending(sess, cfg2) && SessionService.DescribePending(sess, cfg2).Contains(target.Name), "HasPending: có DNS sao lưu");
            calls.Clear();
            psCalls.Clear();
            var rep = RecoveryService.RestoreAll(sess, cfg2, true, null);
            Check(rep.AllOk && rep.DnsRestored == 1 && rep.ProfilesRemoved == 1 && rep.VpnDisconnected == 0 && rep.VpnFailed == 0 && !BackupService.HasAny(),
                "RestoreAll: DNS về gốc, xoá cấu hình VPN Gate, giữ cấu hình tự nhập", rep.Summary());
            Check(calls.Any(c => c.Contains("source=static address=192.168.1.1")) && calls.Any(c => c.Contains("ipconfig.exe /flushdns"))
                  && psCalls.Count == 1 && psCalls[0].Contains("Remove-VpnConnection -Name 'VPN Gate JP test'"),
                "RestoreAll: đúng lệnh (netsh gốc, flush DNS, Remove-VpnConnection)", string.Join(" | ", calls) + " || " + string.Join(" | ", psCalls));
            Check(cfg2.VpnProfilesCreatedByApp.Count == 1 && cfg2.VpnProfilesCreatedByApp[0].Name == "Manual VPN", "RestoreAll: cập nhật danh sách cấu hình do app tạo");
            Check(SessionService.Load().VpnConnectedByApp.Count == 0, "RestoreAll: VPN không còn up → gỡ khỏi phiên");
            Check(RecoveryService.RestoreAll(SessionService.Load(), cfg2, true, null).Nothing, "RestoreAll: chạy lại không còn gì (idempotent)");
            Check(rep.Summary().Contains("DNS: 1 card") && rep.Summary().Contains("cấu hình VPN Gate: 1 xoá"), "RecoveryReport.Summary", rep.Summary());

            BackupService.Save(OneBackup(target, false, null));
            var child = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 2 127.0.0.1 >nul") { UseShellExecute = false, CreateNoWindow = true });
            SessionService.Begin(child.Id);
            calls.Clear();
            var sw = Stopwatch.StartNew();
            int rc = GuardianRunner.Run(child.Id, null);
            var after = SessionService.Load();
            Check(rc == 0 && !BackupService.HasAny() && after.CleanExit && (after.Note ?? "").StartsWith("guardian") && calls.Count(c => c.Contains("source=dhcp")) == 2,
                "Guardian: chờ tiến trình kết thúc rồi khôi phục DNS, đánh dấu phiên sạch (" + sw.ElapsedMilliseconds + " ms)", after.Note + " | " + string.Join(" | ", calls));
            Check(HistoryService.Load().Any(h => h.Action == "Khôi phục tự động"), "Guardian: ghi lịch sử");

            BackupService.Save(OneBackup(target, false, null));
            SessionService.Begin(99999);
            SessionService.MarkClean(true, "kept");
            calls.Clear();
            rc = GuardianRunner.Run(99999, null);
            Check(rc == 0 && BackupService.HasAny() && calls.Count == 0, "Guardian: phiên thoát sạch (giữ thay đổi) → không đụng vào");

            SessionService.Begin(1);
            rc = GuardianRunner.Run(int.MaxValue - 7, null);
            Check(rc == 0 && BackupService.HasAny() && calls.Count == 0, "Guardian: session của pid khác → bỏ qua");

            SessionService.Begin(int.MaxValue - 8);
            BackupService.Save(new BackupData());
            rc = GuardianRunner.Run(int.MaxValue - 8, null);
            Check(rc == 0 && SessionService.Load().CleanExit && calls.Count == 0, "Guardian: dừng đột ngột nhưng không có thay đổi → đánh dấu sạch");

            var dr = VpnService.DisconnectReliable("SCT-Fake-VPN", null);
            Check(dr.Ok && calls.Any(c => c.Contains("rasdial.exe \"SCT-Fake-VPN\" /disconnect")), "DisconnectReliable: card không còn → thành công sau 1 lần");
        }
        finally
        {
            DnsApplier.CommandRunner = null;
            NetworkAdapterService.StateReaderOverride = null;
            VpnService.PowerShellRunner = null;
            BackupService.Save(new BackupData());
        }
    }

    private static void TestGuideAndSettings()
    {
        Section("Hướng dẫn lần đầu & cài đặt");
        var pages = GuideContent.Pages();
        Check(pages.Count == 7, "Hướng dẫn: 7 trang", pages.Count.ToString());
        Check(pages.All(p => !string.IsNullOrWhiteSpace(p.Title) && !string.IsNullOrWhiteSpace(p.Body) && p.TabIndex >= 0 && p.TabIndex <= 4), "Hướng dẫn: mỗi trang có tiêu đề, nội dung, tab hợp lệ");
        string all = string.Join("\n", pages.Select(p => p.Title + "\n" + p.Body));
        Check(all.Contains("Thu hồi") && all.Contains("VPN Gate") && all.Contains("Chọn nhanh") && all.Contains("guardian") && all.Contains("Cài đặt") && all.Contains("khởi động lại Steam"),
            "Hướng dẫn: đề cập các chức năng chính (Thu hồi, VPN Gate, Chọn nhanh, guardian, Cài đặt, khởi động lại Steam)");
        Check(pages.Select(p => p.TabIndex).Distinct().Count() >= 3, "Hướng dẫn: dẫn người dùng qua ít nhất 3 tab khác nhau");
        Check(pages.All(p => p.Body.Split('\n').All(l => l.Length <= 260)), "Hướng dẫn: không có dòng quá dài");

        var cfg = new AppConfig();
        Check(cfg.ShowGuideOnStartup && cfg.VpnGatePreferSstp && !cfg.KeepChangesOnExit, "Cài đặt mặc định: hiện hướng dẫn lần đầu, ưu tiên SSTP, khôi phục khi thoát");
        cfg.ShowGuideOnStartup = false;
        cfg.DnsTimeoutMs = 4000;
        cfg.Concurrency = 9;
        cfg.KeepChangesOnExit = true;
        cfg.VpnGatePreferSstp = false;
        ProviderCatalog.SaveConfig(cfg);
        var back = ProviderCatalog.LoadConfig();
        Check(!back.ShowGuideOnStartup && back.DnsTimeoutMs == 4000 && back.Concurrency == 9 && back.KeepChangesOnExit && !back.VpnGatePreferSstp, "Cài đặt: lưu và đọc lại đầy đủ");
        back.VpnProfilesCreatedByApp.Add(new VpnProfileRecord { Name = "X", Auto = true });
        back.VpnCredentials.Add(new VpnCredential { Profile = "X", UserName = "u" });
        back.Providers[0].Enabled = false;
        back.ResetOptions();
        Check(back.ShowGuideOnStartup && back.DnsTimeoutMs == 2500 && back.Concurrency == 4 && !back.KeepChangesOnExit && back.VpnGatePreferSstp
              && back.VpnProfilesCreatedByApp.Count == 1 && back.VpnCredentials.Count == 1 && !back.Providers[0].Enabled,
            "ResetOptions: chỉ đặt lại tuỳ chọn, giữ DNS/tài khoản/cấu hình VPN");
        // Tương thích file cũ không có trường mới → mặc định
        File.WriteAllText(AppPaths.ConfigFile, "{\"Version\":2,\"Providers\":[],\"DnsTimeoutMs\":2500,\"HttpsTimeoutMs\":6000,\"Concurrency\":4}");
        var old = ProviderCatalog.LoadConfig();
        Check(old.ShowGuideOnStartup && old.VpnGatePreferSstp && old.VpnProfilesCreatedByApp != null, "Cài đặt: file cũ thiếu trường mới → dùng mặc định");
    }

    private static void TestAboutAndDiagnostics(List<DnsProvider> builtIn)
    {
        Section("Giới thiệu, xuất/nhập cấu hình, gói chẩn đoán");
        Check(AppInfo.WindowsVersion().StartsWith("Windows") && AppInfo.DotNetVersion().Contains("4."), "AppInfo: phiên bản Windows / .NET", AppInfo.WindowsVersion() + " | " + AppInfo.DotNetVersion());
        Console.WriteLine("    " + AppInfo.WindowsVersion() + " | " + AppInfo.DotNetVersion() + " | v" + AppInfo.Version);
        string sys = AppInfo.SystemSummary();
        Check(sys.Contains(AppInfo.Name) && sys.Contains("Card mạng") && sys.Contains("Quyền Administrator") && sys.Contains("Sao lưu DNS gốc"), "AppInfo.SystemSummary: đủ mục");
        Check(AppInfo.SystemSummaryShort().Split('\n').Length >= 3 && AppInfo.SystemSummaryShort().Contains("Dữ liệu:"), "AppInfo.SystemSummaryShort: 3 dòng");
        Check(!string.IsNullOrEmpty(AppInfo.Author) && AppInfo.Author == "Quynh Nguyen" && AppInfo.License.Contains("MIT"), "AppInfo: tác giả, giấy phép");

        var cfg = new AppConfig();
        cfg.Providers = builtIn;
        cfg.DnsTimeoutMs = 3333;
        CredentialStore.Save(cfg, "VPN X", "user", "secret-pass", true);
        string secret = cfg.VpnCredentials[0].PasswordProtected;
        var san = DiagnosticsService.Sanitize(cfg);
        Check(san.VpnCredentials.Count == 1 && san.VpnCredentials[0].PasswordProtected == "" && san.VpnCredentials[0].UserName == "user" && cfg.VpnCredentials[0].PasswordProtected == secret,
            "Sanitize: bỏ mật khẩu, giữ tên đăng nhập, không đụng bản gốc");

        string exp = Path.Combine(AppPaths.DataDir, "export.json");
        DiagnosticsService.ExportConfig(exp, cfg);
        string expText = File.ReadAllText(exp);
        Check(File.Exists(exp) && !expText.Contains(secret) && expText.Contains("3333") && expText.Contains("\"google\""), "ExportConfig: có tuỳ chọn và DNS, không có mật khẩu");

        var cur = new AppConfig();
        CredentialStore.Save(cur, "VPN Y", "u2", "p2", true);
        var imp = DiagnosticsService.ImportConfig(exp, cur);
        Check(imp.DnsTimeoutMs == 3333 && imp.Providers.Count == builtIn.Count && imp.VpnCredentials.Any(c => c.Profile == "VPN X"), "ImportConfig: hợp nhất, giữ tài khoản trong file", imp.DnsTimeoutMs + " " + imp.Providers.Count);
        File.WriteAllText(exp, "{\"DnsTimeoutMs\":4444}");
        var imp2 = DiagnosticsService.ImportConfig(exp, cur);
        Check(imp2.VpnCredentials.Any(c => c.Profile == "VPN Y"), "ImportConfig: file không có tài khoản → giữ tài khoản hiện có");
        File.WriteAllText(exp, "{\"foo\":1,\"Version\":0}");
        bool threw = false;
        try { DiagnosticsService.ImportConfig(exp, cur); } catch (InvalidOperationException) { threw = true; }
        Check(threw, "ImportConfig: file không đúng cấu trúc → báo lỗi rõ");
        File.WriteAllText(exp, "not json at all");
        threw = false;
        try { DiagnosticsService.ImportConfig(exp, cur); } catch (InvalidOperationException) { threw = true; }
        Check(threw, "ImportConfig: không phải JSON → báo lỗi");
        File.WriteAllText(exp, "{\"Version\":2,\"Providers\":[{\"Id\":\"bad\",\"Name\":\"Bad\",\"Ipv4Primary\":\"8.8.8\"}]}");
        threw = false;
        try { DiagnosticsService.ImportConfig(exp, cur); } catch (InvalidOperationException) { threw = true; }
        Check(threw, "ImportConfig: DNS không hợp lệ trong file → báo lỗi");

        string zipPath = Path.Combine(AppPaths.DataDir, "diag.zip");
        var entries = DiagnosticsService.ExportDiagnostics(zipPath, cfg, null);
        Check(File.Exists(zipPath) && entries.Contains("system-info.txt") && entries.Contains("config.json") && entries.Contains("vpn-profiles.txt"), "ExportDiagnostics: tạo zip đủ mục", string.Join(",", entries));
        using (var fs = File.OpenRead(zipPath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
        {
            string cfgText;
            using (var sr = new StreamReader(zip.GetEntry("config.json").Open())) cfgText = sr.ReadToEnd();
            Check(!cfgText.Contains(secret) && cfgText.Contains("\"UserName\": \"user\""), "ExportDiagnostics: config trong zip không có mật khẩu");
            string si;
            using (var sr2 = new StreamReader(zip.GetEntry("system-info.txt").Open())) si = sr2.ReadToEnd();
            Check(si.Contains("netsh interface ipv4 show dnsservers") && si.Contains("Windows"), "ExportDiagnostics: có thông tin hệ thống và đầu ra netsh");
        }
    }

    private static void TestUpdates(string tmp)
    {
        Section("Nâng cấp từ xa (manifest, tải, xác minh, thay file)");
        Check(UpdateService.ParseVersion("1.2.3") == new Version(1, 2, 3, 0) && UpdateService.ParseVersion("v1.2") == new Version(1, 2, 0, 0)
              && UpdateService.ParseVersion("1.2.3-beta+5") == new Version(1, 2, 3, 0) && UpdateService.ParseVersion("abc") == null && UpdateService.ParseVersion("") == null,
            "ParseVersion: các dạng 1.2.3 / v1.2 / 1.2.3-beta / lỗi");
        Check(!UpdateService.IsAllowedUrl("http://x/latest.json") && UpdateService.IsAllowedUrl("https://x/latest.json")
              && UpdateService.IsAllowedUrl("file:///C:/x.json") && !UpdateService.IsAllowedUrl("ftp://x/y") && !UpdateService.IsAllowedUrl("latest.json"),
            "IsAllowedUrl: chỉ https:// và file://");
        Check(UpdateService.IsCheckDue(null) && UpdateService.IsCheckDue("2000-01-01 00:00:00") && !UpdateService.IsCheckDue(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
            "IsCheckDue: mỗi ngày một lần");
        Check(UpdateService.DefaultManifestUrl.StartsWith(AppInfo.ReleasesUrl + "/latest/download/", StringComparison.Ordinal) && UpdateService.IsAllowedUrl(UpdateService.DefaultManifestUrl),
            "DefaultManifestUrl: trỏ tới GitHub Releases (…/releases/latest/download/latest.json) qua https");

        // Dự phòng khi Release không còn latest.json: dựng manifest từ GitHub API (trường digest = sha256 của asset)
        string gh = "{\"tag_name\":\"v2.0.0\",\"draft\":false,\"prerelease\":false,\"published_at\":\"2026-11-01T10:00:00Z\",\"html_url\":\"https://github.com/x/y/releases/tag/v2.0.0\",\"body\":\"- Sửa lỗi A\\r\\n- Thêm B\\r\\n\\r\\n---\\r\\nSHA-256: xem SHA256SUMS.txt\",\"assets\":[{\"name\":\"SteamConnectToolkit-2.0.0-portable.zip\",\"browser_download_url\":\"https://github.com/x/y/releases/download/v2.0.0/z.zip\",\"size\":10,\"digest\":\"sha256:aa\"},{\"name\":\"SteamConnectToolkit.exe\",\"browser_download_url\":\"https://github.com/x/y/releases/download/v2.0.0/SteamConnectToolkit.exe\",\"size\":360448,\"digest\":\"sha256:ABCDEF0123\"},{\"name\":\"SteamConnectToolkit-Setup-2.0.0.exe\",\"browser_download_url\":\"https://github.com/x/y/releases/download/v2.0.0/SteamConnectToolkit-Setup-2.0.0.exe\",\"size\":1234567,\"digest\":\"sha256:0011\"}]}";
        var gm = UpdateService.ManifestFromGitHubRelease(gh);
        Check(gm.Version == "2.0.0" && gm.Published == "2026-11-01" && gm.Portable != null && gm.Portable.Sha256 == "ABCDEF0123" && gm.Portable.Size == 360448
              && gm.Installer != null && gm.Installer.Sha256 == "0011" && gm.Notes == "- Sửa lỗi A\n- Thêm B" && gm.NotesUrl.EndsWith("v2.0.0"),
            "GitHub API → manifest: phiên bản, ngày, exe/bộ cài kèm SHA-256 từ digest, ghi chú bỏ phần chân");
        bool ghThrew = false;
        try { UpdateService.ManifestFromGitHubRelease(gh.Replace("\"digest\":\"sha256:ABCDEF0123\"", "\"digest\":\"\"").Replace("\"digest\":\"sha256:0011\"", "\"digest\":\"\"")); }
        catch (InvalidOperationException) { ghThrew = true; }
        Check(ghThrew, "GitHub API → manifest: asset không có digest → từ chối (không tải file thiếu mã băm)");
        ghThrew = false;
        try { UpdateService.ManifestFromGitHubRelease(gh.Replace("\"draft\":false", "\"draft\":true")); } catch (InvalidOperationException) { ghThrew = true; }
        Check(ghThrew, "GitHub API → manifest: bản nháp → từ chối");
        ghThrew = false;
        try { UpdateService.ManifestFromGitHubRelease("{\"tag_name\":\"abc\"}"); } catch (InvalidOperationException) { ghThrew = true; }
        Check(ghThrew && UpdateService.IsDefaultGitHubManifest(" " + UpdateService.DefaultManifestUrl.ToUpperInvariant() + " ") && !UpdateService.IsDefaultGitHubManifest("https://x/latest.json")
              && UpdateService.ShouldFallbackToApi(new InvalidOperationException("x")) && UpdateService.ShouldFallbackToApi(new WebException("404", WebExceptionStatus.ProtocolError))
              && !UpdateService.ShouldFallbackToApi(new WebException("timeout", WebExceptionStatus.Timeout)),
            "GitHub API: tag không hợp lệ → lỗi; chỉ dự phòng cho địa chỉ mặc định khi máy chủ trả lỗi hoặc nội dung hỏng");

        var rnd = new Random(7);
        var payload = new byte[5000];
        rnd.NextBytes(payload);
        string pkgPath = Path.Combine(tmp, "new-app.exe");
        File.WriteAllBytes(pkgPath, payload);
        string sha = UpdateService.Sha256File(pkgPath);
        string pkgUri = new Uri(pkgPath).AbsoluteUri;
        string manifestPath = Path.Combine(tmp, "latest.json");
        string manifestUri = new Uri(manifestPath).AbsoluteUri;
        File.WriteAllText(manifestPath, "{\"Version\":\"9.9.9\",\"Published\":\"2026-10-21\",\"Notes\":\"test\",\"Portable\":{\"Url\":\"" + pkgUri + "\",\"Sha256\":\"" + sha + "\",\"Size\":5000}}");

        var r = UpdateService.Check(manifestUri, new Version(1, 0, 0, 0), false, 5000);
        Check(r.Ok && r.UpdateAvailable && r.Latest == new Version(9, 9, 9, 0) && !r.UsesInstaller && r.Package.Sha256 == sha && !r.Mandatory && r.Describe().Contains("9.9.9"),
            "Check: manifest qua file:// → có bản mới (portable)", r.Error);
        Check(UpdateService.Check(manifestUri, new Version(9, 9, 9, 0), false, 5000).UpdateAvailable == false, "Check: cùng phiên bản → không có bản mới");
        Check(UpdateService.Check(manifestUri, new Version(10, 0, 0, 0), false, 5000).UpdateAvailable == false, "Check: đang dùng bản mới hơn → không hạ cấp");

        File.WriteAllText(manifestPath, "{\"Version\":\"2.0.0\",\"MinVersion\":\"2.0.0\",\"Installer\":{\"Url\":\"https://example.com/setup.exe\",\"Sha256\":\"ab\",\"Size\":0},\"Portable\":{\"Url\":\"" + pkgUri + "\",\"Sha256\":\"" + sha + "\",\"Size\":5000}}");
        var r4 = UpdateService.Check(manifestUri, new Version(1, 0, 0, 0), true, 5000);
        Check(r4.Ok && r4.UpdateAvailable && r4.UsesInstaller && r4.Mandatory, "Check: bản đã cài dùng gói Installer; thấp hơn MinVersion → bắt buộc", r4.Error);
        var r5 = UpdateService.Check(manifestUri, new Version(1, 0, 0, 0), false, 5000);
        Check(r5.Ok && !r5.UsesInstaller && r5.Package.Url == pkgUri, "Check: bản portable dùng gói Portable");
        File.WriteAllText(manifestPath, "{\"Version\":\"3.0.0\",\"Mandatory\":true,\"Portable\":{\"Url\":\"" + pkgUri + "\",\"Sha256\":\"" + sha + "\"}}");
        Check(UpdateService.Check(manifestUri, new Version(1, 0, 0, 0), false, 5000).Mandatory, "Check: Mandatory=true");
        File.WriteAllText(manifestPath, "{\"Version\":\"x.y\"}");
        var r6 = UpdateService.Check(manifestUri, new Version(1, 0, 0, 0), false, 5000);
        Check(!r6.Ok && r6.Error.Contains("Version"), "Check: manifest thiếu Version hợp lệ → lỗi rõ", r6.Error);
        File.WriteAllText(manifestPath, "{\"Version\":\"1.5.0\"}");
        Check(!UpdateService.Check(manifestUri, new Version(1, 0, 0, 0), false, 5000).Ok, "Check: manifest không có gói → lỗi");
        File.WriteAllText(manifestPath, "garbage{{{");
        Check(!UpdateService.Check(manifestUri, new Version(1, 0, 0, 0), false, 5000).Ok, "Check: manifest không phải JSON → lỗi");
        var r8 = UpdateService.Check("http://127.0.0.1/latest.json", new Version(1, 0, 0, 0), false, 2000);
        Check(!r8.Ok && r8.Error.Contains("https"), "Check: http:// bị từ chối", r8.Error);
        Check(!UpdateService.Check(new Uri(Path.Combine(tmp, "khong-co.json")).AbsoluteUri, new Version(1, 0, 0, 0), false, 2000).Ok, "Check: manifest không tồn tại → lỗi, không crash");

        string dest = Path.Combine(tmp, "updates", "dl.exe");
        long seen = 0;
        string got = UpdateService.Download(new UpdatePackage { Url = pkgUri, Sha256 = sha.ToUpperInvariant(), Size = 5000 }, dest, (d, t) => { seen = d; }, CancellationToken.None);
        Check(File.Exists(got) && new FileInfo(got).Length == 5000 && seen == 5000 && !File.Exists(dest + ".part"), "Download: tải, kích thước và SHA-256 khớp (không phân biệt hoa thường)");
        Check(UpdateService.Download(new UpdatePackage { Url = pkgUri, Sha256 = sha, Size = 5000 }, dest, null, CancellationToken.None) == dest, "Download: file đã có và đúng hash → dùng lại");
        string dest2 = Path.Combine(tmp, "updates", "dl2.exe");
        bool threw = false;
        try { UpdateService.Download(new UpdatePackage { Url = pkgUri, Sha256 = "00", Size = 5000 }, dest2, null, CancellationToken.None); }
        catch (InvalidOperationException ex) { threw = ex.Message.Contains("SHA-256"); }
        Check(threw && !File.Exists(dest2) && !File.Exists(dest2 + ".part"), "Download: SHA-256 sai → từ chối, dọn file tạm");
        threw = false;
        try { UpdateService.Download(new UpdatePackage { Url = pkgUri, Sha256 = sha, Size = 4999 }, dest2, null, CancellationToken.None); }
        catch (InvalidOperationException ex) { threw = ex.Message.Contains("Kích thước"); }
        Check(threw && !File.Exists(dest2), "Download: kích thước sai → từ chối");
        threw = false;
        try { UpdateService.Download(new UpdatePackage { Url = pkgUri, Sha256 = "", Size = 0 }, dest2, null, CancellationToken.None); }
        catch (InvalidOperationException) { threw = true; }
        Check(threw, "Download: manifest không có SHA-256 → từ chối tải");
        threw = false;
        try { UpdateService.Download(new UpdatePackage { Url = "http://example.com/x.exe", Sha256 = sha }, dest2, null, CancellationToken.None); }
        catch (InvalidOperationException) { threw = true; }
        Check(threw, "Download: URL http:// → từ chối");

        string appDir = Path.Combine(tmp, "app");
        Directory.CreateDirectory(appDir);
        string target = Path.Combine(appDir, "SteamConnectToolkit.exe");
        File.WriteAllText(target, "OLD");
        int rc = UpdateService.ApplyPortableUpdate(got, 0, target, false, null);
        Check(rc == 0 && File.ReadAllBytes(target).Length == 5000 && File.Exists(target + ".bak") && File.ReadAllText(target + ".bak") == "OLD" && !File.Exists(got),
            "ApplyPortableUpdate: thay file, giữ bản cũ .bak, xoá gói đã tải");
        int rc2 = UpdateService.ApplyPortableUpdate(Path.Combine(tmp, "missing.exe"), 0, target, false, null);
        Check(rc2 != 0 && File.ReadAllBytes(target).Length == 5000, "ApplyPortableUpdate: thiếu file mới → không đụng file hiện tại");
        File.WriteAllBytes(got, payload);
        int rc3 = UpdateService.ApplyPortableUpdate(got, int.MaxValue - 9, target, false, null);
        Check(rc3 == 0 && File.ReadAllBytes(target + ".bak").Length == 5000, "ApplyPortableUpdate: pid không tồn tại → không treo, thay file lần 2 (bak = bản trước)");

        string thumb;
        Check(UpdateService.VerifySigner(target, "", out thumb) && thumb == null, "VerifySigner: không yêu cầu chữ ký → chấp nhận file không ký");
        Check(!UpdateService.VerifySigner(target, "ABCDEF0123", out thumb), "VerifySigner: yêu cầu chữ ký mà file không ký → từ chối");
    }

    private static void TestFeedback()
    {
        Section("Phản hồi / tạo issue GitHub (không lộ thông tin cá nhân)");
        string user = Environment.UserName;
        string machine = Environment.MachineName;
        string raw = "IP công cộng: 203.0.113.45, DNS 8.8.8.8 và 2001:4860:4860::8888, máy " + machine + " người dùng " + user + " tại C:\\Users\\" + user + "\\Desktop, VPN 10.239.81.84";
        string san = FeedbackService.Sanitize(raw);
        Check(!san.Contains("203.0.113.45") && san.Contains("8.8.8.8") && san.Contains("2001:4860:4860::8888") && !san.Contains("10.239.81.84")
              && !san.Contains(machine) && !san.Contains("C:\\Users\\" + user) && san.Contains("<user>") && san.Contains("<máy>"),
            "Sanitize: ẩn IP lạ, tên máy, tên người dùng, đường dẫn; giữ DNS công cộng", san);
        string pub = FeedbackService.PublicSummary();
        Check(pub.Contains("App:") && pub.Contains("Windows") && !pub.Contains(machine) && !pub.Contains("C:\\")
              && !System.Text.RegularExpressions.Regex.IsMatch(pub, "\\b" + System.Text.RegularExpressions.Regex.Escape(user) + "\\b"),
            "PublicSummary: không có tên máy / người dùng / đường dẫn", pub);
        string timeLog = FeedbackService.Sanitize("15:00:01  Steam OK, Quyền Administrator: có, IPv6 fe80::1 và 2a0d:2a00:1::2");
        Check(timeLog.Contains("15:00:01") && timeLog.Contains("Administrator") && !timeLog.Contains("fe80::1") && timeLog.Contains("2a0d:2a00:1::2"),
            "Sanitize: giữ giờ và chữ Administrator, ẩn IPv6 lạ, giữ IPv6 DNS công cộng", timeLog);
        string body = FeedbackService.BuildBody("Không vào được Steam ở 203.0.113.45", "", true, "15:00:01  Card " + machine + " ok");
        Check(body.Contains("### Mô tả") && body.Contains("x.x.x.x") && !body.Contains("203.0.113.45") && body.Contains("### Thông tin hệ thống") && body.Contains("### Nhật ký") && !body.Contains(machine) && body.Contains(AppInfo.Version),
            "BuildBody: đủ mục, đã ẩn thông tin");
        string longBody = FeedbackService.BuildBody(new string('a', 20000), "", false, "");
        Check(longBody.Length <= FeedbackService.MaxBodyChars + 40 && longBody.EndsWith("…(đã cắt bớt)"), "BuildBody: cắt bớt khi quá dài", longBody.Length.ToString());
        string url = FeedbackService.BuildIssueUrl("Tiêu đề thử", body, "bug");
        Check(url.StartsWith(FeedbackService.NewIssueUrl + "?title=") && url.Contains("&body=") && url.EndsWith("&labels=bug") && url.Length <= FeedbackService.MaxUrlChars, "BuildIssueUrl: đúng dạng, có nhãn", url.Length.ToString());
        int b = url.IndexOf("&body=") + 6;
        int e = url.IndexOf("&labels=");
        string decoded = Uri.UnescapeDataString(url.Substring(b, e - b));
        Check(decoded.Contains("### Mô tả") && decoded.Contains("x.x.x.x"), "BuildIssueUrl: nội dung giải mã đúng");
        string hugeUrl = FeedbackService.BuildIssueUrl(new string('t', 300), new string('ư', 9000), "");
        Check(hugeUrl.Length <= FeedbackService.MaxUrlChars && Uri.UnescapeDataString(hugeUrl).Contains("…(đã cắt bớt)"), "BuildIssueUrl: rút ngắn nội dung để không vượt giới hạn URL", hugeUrl.Length.ToString());
        Check(FeedbackService.BuildIssueUrl("", "x", "").Contains(Uri.EscapeDataString("Phản hồi từ " + AppInfo.Name)), "BuildIssueUrl: tiêu đề mặc định khi trống");
    }

    // =====================================================================
    //  Kiểm tra mạng thật (chế độ cũ)
    // =====================================================================

    private static int NetworkCheck(string[] args)
    {
        bool discovery = !args.Contains("--no-discovery");
        var ids = args.Where(a => !a.StartsWith("--")).ToArray();
        if (ids.Length == 0) ids = new[] { "google", "cloudflare", "quad9" };

        Console.WriteLine("== Card mạng ==");
        foreach (var a in NetworkAdapterService.GetAdapters(true))
        {
            Console.WriteLine((a.Recommended ? "[*] " : "[ ] ") + a + " | " + a.SourceText()
                + " | v4: " + a.Ipv4DnsText() + " | v6: " + a.Ipv6DnsText() + " | GlobalIPv6: " + a.HasGlobalIpv6);
        }

        Console.WriteLine();
        Console.WriteLine("== Steam qua DNS hệ thống ==");
        foreach (var h in SteamChecker.Domains)
            Console.WriteLine(SteamChecker.CheckViaSystem(h, 2500, 6000).Summary());

        Console.WriteLine();
        Console.WriteLine("== Kiểm tra nhà cung cấp DNS ==");
        var provs = ProviderCatalog.BuiltIn().Where(p => ids.Contains("all") || ids.Contains(p.Id)).ToList();
        var opt = new TestOptions();
        var results = ProviderTester.TestManyAsync(provs, opt, r => Console.WriteLine("  " + r.Summary()), CancellationToken.None).Result;
        Console.WriteLine("-- Xếp hạng --");
        foreach (var r in ProviderTester.Rank(results))
        {
            Console.WriteLine("  " + r.StatusText().PadRight(18) + " | điểm " + r.Score + " | " + r.Summary());
            foreach (var d in r.Domains.Where(x => !x.Ok)) Console.WriteLine("      ! " + d.Summary());
        }

        if (discovery)
        {
            Console.WriteLine();
            Console.WriteLine("== Tìm DNS công cộng (public-dns.info, VN) ==");
            try
            {
                var list = DiscoveryService.FetchCountry("vn", 0.9, 5, false, Console.WriteLine);
                foreach (var s in list)
                    Console.WriteLine("  " + s.Ip.PadRight(16) + " " + s.Reliability + " " + s.AsOrg + " -> " + DiscoveryService.ToProvider(s).DisplayName());
            }
            catch (Exception ex)
            {
                Console.WriteLine("  Lỗi: " + ex.Message);
            }
        }
        return 0;
    }
}
