using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SteamConnectToolkit.Models;
using SteamConnectToolkit.Services;

namespace SteamConnectToolkit.UI
{
    public class MainForm : Form
    {
        private enum NoticeKind { Info, Success, Warning, Error }

        private static readonly Color ColorOk = Color.FromArgb(0, 120, 0);
        private static readonly Color ColorWarn = Color.FromArgb(190, 110, 0);
        private static readonly Color ColorErr = Color.FromArgb(190, 0, 0);

        private AppConfig _config = new AppConfig();
        private List<AdapterInfo> _adapters = new List<AdapterInfo>();
        private CancellationTokenSource _cts;
        private bool _busy;
        private bool _adaptersLoadedOnce;
        private float _scale = 1f; // hệ số DPI (1.0 = 96 DPI)
        private readonly object _fileLogLock = new object();

        // Bảng điều khiển
        private TabControl _tabs;
        private Label _lblNotice, _lblCommand, _lblProgress, _lblBackup;
        private ProgressBar _progress;
        private Button _btnCheck, _btnApply, _btnRecheck, _btnRevert, _btnBest, _btnStop, _btnRefreshAdapters, _btnOpenData;
        private CheckBox _chkKeepOnExit;
        private ComboBox _cmbProviders;
        private ListView _lvResults, _lvAdapters;
        private RichTextBox _rtbLog;

        // Lịch sử
        private DataGridView _dgvHistory;

        // Danh sách DNS
        private DataGridView _dgvProviders, _dgvDiscovered;
        private TextBox _txtCountry;
        private CheckBox _chkAutoDisable;
        private RichTextBox _rtbReport;
        private Button _btnTestAll, _btnDiscover, _btnAddCustom, _btnRemove, _btnToggle, _btnResetCatalog, _btnAddDiscovered, _btnStop2;

        // VPN
        private TabPage _vpnPage;
        private DataGridView _dgvVpn;
        private TextBox _txtVpnUser, _txtVpnPass;
        private CheckBox _chkVpnRemember;
        private Label _lblVpnIp, _lblVpnStatus;
        private Button _btnVpnRefresh, _btnVpnAdd, _btnVpnRemove, _btnVpnConnect, _btnVpnConnectCheck, _btnVpnDisconnect, _btnVpnIp, _btnStop3;
        private List<VpnProfile> _vpnProfiles = new List<VpnProfile>();
        private bool _vpnLoadedOnce;
        private bool _fillingVpn;
        private System.Windows.Forms.Timer _vpnTimer;

        // VPN Gate (VPN miễn phí)
        private DataGridView _dgvVpnGate;
        private Button _btnVpnGateFind, _btnVpnGateUse;
        private CheckBox _chkVpnGatePreferSstp;
        private List<VpnGateServer> _vpnGateServers = new List<VpnGateServer>();

        // Cài đặt, giới thiệu & hướng dẫn
        private TabPage _settingsPage;
        private Button _btnGuideDash, _btnShowGuide, _btnResetSettings, _btnOpenData2;
        private Button _btnOpenLog, _btnExportConfig, _btnImportConfig, _btnDiag, _btnClearVpnCreds, _btnNatT, _btnReadme, _btnLicense, _btnCopySys;
        private CheckBox _chkGuideOnStartup, _chkKeepOnExit2, _chkAutoDisable2, _chkPreferSstp2, _chkAutoCheck;
        private NumericUpDown _numDnsTimeout, _numHttpsTimeout, _numConcurrency, _numDiscoveryMax;
        private TextBox _txtCountry2;
        private Label _lblDataDir, _lblNatT, _lblVpnCreds, _lblSystemInfo;
        private bool _guideOpen;

        // Cập nhật từ xa
        private TextBox _txtUpdateUrl;
        private CheckBox _chkUpdateAuto;
        private Button _btnCheckUpdate, _btnInstallUpdate;
        private Label _lblUpdateStatus;
        private UpdateCheckResult _lastUpdate;
        private string _pendingUpdatePath;
        private bool _pendingUpdateInstaller;

        public MainForm()
        {
            BuildUi();
            Load += MainForm_Load;
            FormClosing += MainForm_FormClosing;
        }

        // =====================================================================
        //  Dựng giao diện
        // =====================================================================

        private void BuildUi()
        {
            Text = "Steam Connect Toolkit – kiểm tra Steam, đổi DNS, kết nối VPN (Windows 10/11)";
            Font = new Font("Segoe UI", 9f);
            // Thiết kế ở 96 DPI; WinForms tự phóng to control theo DPI màn hình. Cột ListView/DataGridView phóng thủ công bằng _scale.
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            try { _scale = Math.Max(1f, DeviceDpi / 96f); }
            catch (Exception) { _scale = 1f; }
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1200, 780);
            MinimumSize = new Size(1020, 680);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (Exception) { }

            _tabs = new TabControl { Dock = DockStyle.Fill };
            _tabs.TabPages.Add(BuildDashboardTab());
            _tabs.TabPages.Add(BuildHistoryTab());
            _tabs.TabPages.Add(BuildProvidersTab());
            _vpnPage = BuildVpnTab();
            _tabs.TabPages.Add(_vpnPage);
            _settingsPage = BuildSettingsTab();
            _tabs.TabPages.Add(_settingsPage);
            _tabs.SelectedIndexChanged += (s, e) =>
            {
                // Lần đầu mở tab VPN: tự đọc danh sách cấu hình (PowerShell mất 1-2 giây).
                if (_tabs.SelectedTab == _vpnPage && !_vpnLoadedOnce && !_busy && !_guideOpen) BtnVpnRefresh_Click(s, e);
            };
            Controls.Add(_tabs);
            Shown += (s, e) =>
            {
                if (_config.ShowGuideOnStartup) BeginInvoke(new Action(() => ShowGuide(true)));
            };
        }

        private TabPage BuildSettingsTab()
        {
            var page = new TabPage("Cài đặt");
            var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 0,
                Padding = new Padding(10, 10, 24, 10),
                Margin = new Padding(0)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // --- Hướng dẫn ---
            _chkGuideOnStartup = new CheckBox { Text = "Hiện hướng dẫn cho người dùng lần đầu khi mở app", AutoSize = true, Margin = new Padding(0, 6, 20, 0) };
            _btnShowGuide = ToolButton("Xem hướng dẫn ngay", (s, e) => ShowGuide(false), 170);
            var grpGuide = SettingsGroup("Hướng dẫn sử dụng", FlowRow(_chkGuideOnStartup, _btnShowGuide));

            // --- Khi khởi động ---
            _chkAutoCheck = new CheckBox { Text = "Tự động kiểm tra kết nối Steam ngay khi mở app", AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
            var grpStart = SettingsGroup("Khi khởi động", FlowRow(_chkAutoCheck));

            // --- Khi thoát ---
            _chkKeepOnExit2 = new CheckBox { Text = "Giữ thay đổi DNS/VPN sau khi thoát (không tự khôi phục). Mặc định: tự khôi phục cấu hình gốc, có hỏi trước.", AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
            var grpExit = SettingsGroup("Khi thoát app", FlowRow(_chkKeepOnExit2));

            // --- Kiểm tra DNS / Steam ---
            _numDnsTimeout = new NumericUpDown { Minimum = 500, Maximum = 10000, Increment = 500, Width = 80, Margin = new Padding(0, 3, 16, 0) };
            _numHttpsTimeout = new NumericUpDown { Minimum = 1000, Maximum = 20000, Increment = 500, Width = 80, Margin = new Padding(0, 3, 16, 0) };
            _numConcurrency = new NumericUpDown { Minimum = 1, Maximum = 16, Width = 60, Margin = new Padding(0, 3, 16, 0) };
            _chkAutoDisable2 = new CheckBox { Text = "Tự động tắt DNS lỗi sau khi kiểm tra", AutoSize = true, Margin = new Padding(0, 5, 16, 0) };
            _txtCountry2 = new TextBox { Width = 44, MaxLength = 2, CharacterCasing = CharacterCasing.Upper, Margin = new Padding(0, 3, 16, 0) };
            _numDiscoveryMax = new NumericUpDown { Minimum = 5, Maximum = 200, Increment = 5, Width = 70, Margin = new Padding(0, 3, 16, 0) };
            var grpCheck = SettingsGroup("Kiểm tra DNS và Steam",
                FlowRow(SettingsLabel("Thời gian chờ DNS (ms):"), _numDnsTimeout, SettingsLabel("Thời gian chờ HTTPS (ms):"), _numHttpsTimeout, SettingsLabel("Số DNS đo song song:"), _numConcurrency),
                FlowRow(_chkAutoDisable2, SettingsLabel("Mã quốc gia tìm DNS công cộng:"), _txtCountry2, SettingsLabel("Số DNS tải tối đa:"), _numDiscoveryMax));

            // --- VPN ---
            _chkPreferSstp2 = new CheckBox { Text = "VPN Gate: ưu tiên SSTP (TCP 443, không vướng NAT) hơn L2TP/IPsec", AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
            _lblNatT = new Label { AutoSize = true, Margin = new Padding(0, 7, 8, 0), Text = "Khoá L2TP sau NAT: ?" };
            _btnNatT = ToolButton("Đặt khoá L2TP sau NAT (cần khởi động lại)", BtnNatT_Click, 290);
            _lblVpnCreds = new Label { AutoSize = true, Margin = new Padding(16, 7, 8, 0), Text = "Tài khoản VPN đã lưu: 0" };
            _btnClearVpnCreds = ToolButton("Xoá tài khoản VPN đã lưu", BtnClearVpnCreds_Click, 190);
            var grpVpn = SettingsGroup("VPN", FlowRow(_chkPreferSstp2), FlowRow(_lblNatT, _btnNatT, _lblVpnCreds, _btnClearVpnCreds));

            // --- Cập nhật từ xa ---
            _txtUpdateUrl = new TextBox { Width = 430, Margin = new Padding(0, 3, 12, 0) };
            _chkUpdateAuto = new CheckBox { Text = "Tự kiểm tra bản mới khi mở app (mỗi ngày một lần)", AutoSize = true, Margin = new Padding(0, 5, 0, 0) };
            _btnCheckUpdate = ToolButton("Kiểm tra bản mới", BtnCheckUpdate_Click, 140);
            _btnInstallUpdate = ToolButton("⬇ Tải và cài đặt bản mới", BtnInstallUpdate_Click, 190);
            _btnInstallUpdate.Enabled = false;
            _lblUpdateStatus = new Label { AutoSize = true, Margin = new Padding(8, 7, 0, 0), ForeColor = Color.DimGray, Text = "" };
            var grpUpdate = SettingsGroup("Cập nhật từ xa",
                FlowRow(SettingsLabel("Địa chỉ manifest (latest.json, https):"), _txtUpdateUrl, _chkUpdateAuto),
                FlowRow(_btnCheckUpdate, _btnInstallUpdate, _lblUpdateStatus));

            // --- Dữ liệu & nhật ký ---
            _lblDataDir = new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 6, 0, 0), Text = "Thư mục dữ liệu: " + AppPaths.DataDir };
            _btnOpenData2 = ToolButton("Mở thư mục dữ liệu", BtnOpenData_Click, 160);
            _btnOpenLog = ToolButton("Mở nhật ký app.log", BtnOpenLog_Click, 160);
            _btnExportConfig = ToolButton("Xuất cấu hình…", BtnExportConfig_Click, 140);
            _btnImportConfig = ToolButton("Nhập cấu hình…", BtnImportConfig_Click, 140);
            _btnDiag = ToolButton("Xuất gói chẩn đoán (zip)…", BtnDiag_Click, 210);
            _btnResetSettings = ToolButton("Đặt lại cài đặt mặc định", BtnResetSettings_Click, 180);
            var grpData = SettingsGroup("Dữ liệu và nhật ký", FlowRow(_lblDataDir), FlowRow(_btnOpenData2, _btnOpenLog, _btnExportConfig, _btnImportConfig, _btnDiag), FlowRow(_btnResetSettings));

            // --- Giới thiệu ---
            var grpAbout = new GroupBox { Text = "Giới thiệu", Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 4), Margin = new Padding(0, 0, 0, 8) };
            var tAbout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0) };
            tAbout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            tAbout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            tAbout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            tAbout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            var lblName = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(20, 60, 110),
                Text = AppInfo.Name + " " + AppInfo.Version + "  –  tác giả " + AppInfo.Author + "  –  " + AppInfo.License + ", " + AppInfo.Copyright
            };
            var lblDesc = new Label { Dock = DockStyle.Fill, Text = AppInfo.Description + Environment.NewLine + AppInfo.Credits };
            _lblSystemInfo = new Label { Dock = DockStyle.Fill, ForeColor = Color.DimGray, Text = "" };
            _btnReadme = ToolButton("Mở README", (s, e) => OpenDocFile("README.md"), 120);
            _btnLicense = ToolButton("Mở LICENSE", (s, e) => OpenDocFile("LICENSE"), 120);
            _btnCopySys = ToolButton("Sao chép thông tin hệ thống", BtnCopySys_Click, 210);
            var btnRepo = ToolButton("Trang dự án trên GitHub", (s, e) => OpenUrl(AppInfo.RepositoryUrl), 190);
            var btnReleases = ToolButton("Các bản phát hành", (s, e) => OpenUrl(AppInfo.ReleasesUrl), 150);
            tAbout.Controls.Add(lblName, 0, 0);
            tAbout.Controls.Add(lblDesc, 0, 1);
            tAbout.Controls.Add(_lblSystemInfo, 0, 2);
            tAbout.Controls.Add(FlowRow(_btnReadme, _btnLicense, _btnCopySys, btnRepo, btnReleases), 0, 3);
            grpAbout.Controls.Add(tAbout);

            AddSettingsGroup(root, grpGuide, 78);
            AddSettingsGroup(root, grpStart, 78);
            AddSettingsGroup(root, grpExit, 78);
            AddSettingsGroup(root, grpCheck, 122);
            AddSettingsGroup(root, grpVpn, 122);
            AddSettingsGroup(root, grpUpdate, 122);
            AddSettingsGroup(root, grpData, 158);
            AddSettingsGroup(root, grpAbout, 224);
            scroller.Controls.Add(root);
            page.Controls.Add(scroller);
            return page;
        }

        private static void AddSettingsGroup(TableLayoutPanel root, GroupBox g, int height)
        {
            int row = root.RowCount;
            root.RowCount = row + 1;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            root.Controls.Add(g, 0, row);
        }

        private void UpdateNatTLabel()
        {
            var v = VpnService.GetL2tpNatRule();
            _lblNatT.Text = "Khoá L2TP sau NAT (AssumeUDPEncapsulationContextOnSendRule): " + (v.HasValue ? v.Value.ToString() : "chưa đặt") + (v == 2 ? " ✔" : "");
            _btnNatT.Enabled = v != 2 && !_busy;
        }

        private void UpdateVpnCredsLabel()
        {
            int n = _config.VpnCredentials != null ? _config.VpnCredentials.Count : 0;
            _lblVpnCreds.Text = "Tài khoản VPN đã lưu: " + n;
            _btnClearVpnCreds.Enabled = n > 0 && !_busy;
        }

        /// <summary>Gán lại toàn bộ giá trị tuỳ chọn từ config lên giao diện (sau khi nhập cấu hình hoặc đặt lại).</summary>
        private void ReloadSettingsValues()
        {
            _chkGuideOnStartup.Checked = _config.ShowGuideOnStartup;
            _chkAutoCheck.Checked = _config.AutoCheckOnStartup;
            _chkKeepOnExit.Checked = _config.KeepChangesOnExit;
            _numDnsTimeout.Value = Math.Min(_numDnsTimeout.Maximum, Math.Max(_numDnsTimeout.Minimum, _config.DnsTimeoutMs));
            _numHttpsTimeout.Value = Math.Min(_numHttpsTimeout.Maximum, Math.Max(_numHttpsTimeout.Minimum, _config.HttpsTimeoutMs));
            _numConcurrency.Value = Math.Min(_numConcurrency.Maximum, Math.Max(_numConcurrency.Minimum, _config.Concurrency));
            _numDiscoveryMax.Value = Math.Min(_numDiscoveryMax.Maximum, Math.Max(_numDiscoveryMax.Minimum, _config.DiscoveryMaxServers));
            _chkAutoDisable.Checked = _config.AutoDisableFailed;
            _txtCountry.Text = _config.DiscoveryCountry;
            _chkVpnGatePreferSstp.Checked = _config.VpnGatePreferSstp;
            UpdateNatTLabel();
            UpdateVpnCredsLabel();
            _lblDataDir.Text = "Thư mục dữ liệu: " + AppPaths.DataDir;
            try { _lblSystemInfo.Text = AppInfo.SystemSummaryShort(); } catch (Exception) { }
        }

        private void BtnOpenLog_Click(object sender, EventArgs e)
        {
            try
            {
                AppPaths.EnsureDataDir();
                if (!File.Exists(AppPaths.LogFile)) File.WriteAllText(AppPaths.LogFile, "");
                Process.Start("notepad.exe", "\"" + AppPaths.LogFile + "\"");
            }
            catch (Exception ex) { LogError("Không mở được nhật ký: " + ex.Message); }
        }

        private void OpenDocFile(string fileName)
        {
            string path = Path.Combine(AppInfo.ExeDirectory, fileName);
            if (!File.Exists(path))
            {
                Notice("Không tìm thấy " + fileName + " cạnh file chạy (" + AppInfo.ExeDirectory + "). File có trong bộ cài và mã nguồn.", NoticeKind.Warning);
                return;
            }
            try { Process.Start("notepad.exe", "\"" + path + "\""); }
            catch (Exception ex) { LogError("Không mở được " + fileName + ": " + ex.Message); }
        }

        private void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { LogError("Không mở được " + url + ": " + ex.Message); }
        }

        private void BtnCopySys_Click(object sender, EventArgs e)
        {
            try
            {
                Clipboard.SetText(AppInfo.SystemSummary());
                Notice("Đã sao chép thông tin hệ thống vào clipboard (không chứa mật khẩu).", NoticeKind.Info);
            }
            catch (Exception ex) { LogError("Không sao chép được: " + ex.Message); }
        }

        private void BtnExportConfig_Click(object sender, EventArgs e)
        {
            using (var dlg = new SaveFileDialog
            {
                Filter = "JSON (*.json)|*.json",
                FileName = "SteamConnectToolkit-config-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".json",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    DiagnosticsService.ExportConfig(dlg.FileName, _config);
                    Log("Đã xuất cấu hình (không chứa mật khẩu): " + dlg.FileName);
                    Notice("Đã xuất cấu hình ra " + dlg.FileName, NoticeKind.Success);
                }
                catch (Exception ex) { LogError("Xuất cấu hình lỗi: " + ex.Message); }
            }
        }

        private void BtnImportConfig_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog { Filter = "JSON (*.json)|*.json|Tất cả (*.*)|*.*", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (MessageBox.Show(this, "Nhập cấu hình từ \"" + Path.GetFileName(dlg.FileName) + "\"?\nDanh sách DNS và tuỳ chọn hiện tại sẽ được thay bằng nội dung file (tài khoản VPN hiện có được giữ nếu file không có).",
                        "Nhập cấu hình", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                try
                {
                    _config = DiagnosticsService.ImportConfig(dlg.FileName, _config);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Không nhập được: " + ex.Message, "Nhập cấu hình", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                RefreshProvidersCombo();
                RefreshProvidersGrid();
                ReloadSettingsValues();
                UpdateBackupStatus();
                LoadVpnCredentialFields();
                Log("Đã nhập cấu hình từ " + dlg.FileName + " (" + _config.Providers.Count + " DNS).");
                Notice("Đã nhập cấu hình.", NoticeKind.Success);
            }
        }

        private async void BtnDiag_Click(object sender, EventArgs e)
        {
            string path;
            using (var dlg = new SaveFileDialog
            {
                Filter = "ZIP (*.zip)|*.zip",
                FileName = "SteamConnectToolkit-diag-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".zip",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                path = dlg.FileName;
            }
            await RunOperation("Xuất gói chẩn đoán", async ct =>
            {
                SetMarquee("Đang thu thập thông tin hệ thống, netsh, VPN…");
                var entries = await Task.Run(() => DiagnosticsService.ExportDiagnostics(path, _config, Log), ct);
                Notice("Đã tạo gói chẩn đoán (" + entries.Count + " mục, không chứa mật khẩu): " + path, NoticeKind.Success);
            });
        }

        private void BtnClearVpnCreds_Click(object sender, EventArgs e)
        {
            int n = _config.VpnCredentials != null ? _config.VpnCredentials.Count : 0;
            if (n == 0) return;
            if (MessageBox.Show(this, "Xoá " + n + " tài khoản VPN đã lưu trong app? (Không ảnh hưởng tài khoản Windows đã nhớ trong cấu hình VPN.)",
                    "Xoá tài khoản VPN", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _config.VpnCredentials.Clear();
            SaveConfig();
            UpdateVpnCredsLabel();
            _txtVpnUser.Text = "";
            _txtVpnPass.Text = "";
            _chkVpnRemember.Checked = false;
            Log("Đã xoá " + n + " tài khoản VPN đã lưu.");
            Notice("Đã xoá tài khoản VPN đã lưu.", NoticeKind.Info);
        }

        private void BtnNatT_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(this,
                    "Đặt HKLM\\SYSTEM\\CurrentControlSet\\Services\\PolicyAgent\\AssumeUDPEncapsulationContextOnSendRule = 2 để L2TP/IPsec hoạt động khi máy nằm sau router NAT, " +
                    "rồi khởi động lại dịch vụ IPsec. Windows khuyến nghị khởi động lại máy để chắc chắn có hiệu lực.\n\nTiếp tục?",
                    "Khoá L2TP sau NAT", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var r = VpnService.SetL2tpNatRule(Log);
            UpdateNatTLabel();
            if (r.Ok)
            {
                HistoryService.Add("Registry", "(hệ thống)", "AssumeUDPEncapsulationContextOnSendRule = 2", "Thành công");
                RefreshHistory();
                Notice("Đã đặt khoá L2TP sau NAT. Nếu L2TP vẫn lỗi 809, hãy khởi động lại Windows.", NoticeKind.Success);
            }
            else Notice("Không đặt được khoá: " + r.Output, NoticeKind.Error);
        }

        /// <summary>GroupBox cao cố định chứa các hàng điều khiển (mỗi hàng một FlowLayoutPanel không xuống dòng).</summary>
        private static GroupBox SettingsGroup(string title, params FlowLayoutPanel[] rows)
        {
            var g = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 4), Margin = new Padding(0, 0, 0, 8) };
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows.Length, Margin = new Padding(0) };
            for (int i = 0; i < rows.Length; i++)
            {
                t.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
                t.Controls.Add(rows[i], 0, i);
            }
            g.Controls.Add(t);
            return g;
        }

        private static Label SettingsLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, Margin = new Padding(0, 7, 6, 0) };
        }

        private static FlowLayoutPanel FlowRow(params Control[] controls)
        {
            var f = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), AutoScroll = false };
            f.Controls.AddRange(controls);
            return f;
        }

        /// <summary>Đồng bộ hai ô kiểm (cùng một tuỳ chọn hiện ở hai nơi). CheckedChanged chỉ bắn khi giá trị thực sự đổi nên không lặp vô hạn.</summary>
        private static void SyncChecks(CheckBox a, CheckBox b)
        {
            a.CheckedChanged += (s, e) => { if (b.Checked != a.Checked) b.Checked = a.Checked; };
            b.CheckedChanged += (s, e) => { if (a.Checked != b.Checked) a.Checked = b.Checked; };
        }

        private static void SyncTexts(TextBox a, TextBox b)
        {
            a.TextChanged += (s, e) => { if (b.Text != a.Text) b.Text = a.Text; };
            b.TextChanged += (s, e) => { if (a.Text != b.Text) a.Text = b.Text; };
        }

        /// <summary>Gán giá trị từ config lên tab Cài đặt và nối sự kiện (gọi sau khi đã nạp config).</summary>
        private void InitSettingsTab()
        {
            _chkGuideOnStartup.Checked = _config.ShowGuideOnStartup;
            _chkGuideOnStartup.CheckedChanged += (s, e) => { _config.ShowGuideOnStartup = _chkGuideOnStartup.Checked; SaveConfig(); };

            _chkKeepOnExit2.Checked = _config.KeepChangesOnExit;
            SyncChecks(_chkKeepOnExit, _chkKeepOnExit2);

            _numDnsTimeout.Value = Math.Min(_numDnsTimeout.Maximum, Math.Max(_numDnsTimeout.Minimum, _config.DnsTimeoutMs));
            _numHttpsTimeout.Value = Math.Min(_numHttpsTimeout.Maximum, Math.Max(_numHttpsTimeout.Minimum, _config.HttpsTimeoutMs));
            _numConcurrency.Value = Math.Min(_numConcurrency.Maximum, Math.Max(_numConcurrency.Minimum, _config.Concurrency));
            _numDiscoveryMax.Value = Math.Min(_numDiscoveryMax.Maximum, Math.Max(_numDiscoveryMax.Minimum, _config.DiscoveryMaxServers));
            _numDnsTimeout.ValueChanged += (s, e) => { _config.DnsTimeoutMs = (int)_numDnsTimeout.Value; SaveConfig(); };
            _numHttpsTimeout.ValueChanged += (s, e) => { _config.HttpsTimeoutMs = (int)_numHttpsTimeout.Value; SaveConfig(); };
            _numConcurrency.ValueChanged += (s, e) => { _config.Concurrency = (int)_numConcurrency.Value; SaveConfig(); };
            _numDiscoveryMax.ValueChanged += (s, e) => { _config.DiscoveryMaxServers = (int)_numDiscoveryMax.Value; SaveConfig(); };

            _chkAutoDisable2.Checked = _config.AutoDisableFailed;
            SyncChecks(_chkAutoDisable, _chkAutoDisable2);
            _txtCountry2.Text = _config.DiscoveryCountry;
            SyncTexts(_txtCountry, _txtCountry2);

            _chkVpnGatePreferSstp.Checked = _config.VpnGatePreferSstp;
            _chkPreferSstp2.Checked = _config.VpnGatePreferSstp;
            SyncChecks(_chkVpnGatePreferSstp, _chkPreferSstp2);
            _chkVpnGatePreferSstp.CheckedChanged += (s, e) => { _config.VpnGatePreferSstp = _chkVpnGatePreferSstp.Checked; SaveConfig(); };

            _chkAutoCheck.Checked = _config.AutoCheckOnStartup;
            _chkAutoCheck.CheckedChanged += (s, e) => { _config.AutoCheckOnStartup = _chkAutoCheck.Checked; SaveConfig(); };

            _txtUpdateUrl.Text = string.IsNullOrEmpty(_config.UpdateManifestUrl) ? UpdateService.DefaultManifestUrl : _config.UpdateManifestUrl;
            _txtUpdateUrl.TextChanged += (s, e) => { _config.UpdateManifestUrl = _txtUpdateUrl.Text.Trim(); SaveConfig(); UpdateUpdateStatus(); };
            _chkUpdateAuto.Checked = _config.UpdateCheckOnStartup;
            _chkUpdateAuto.CheckedChanged += (s, e) => { _config.UpdateCheckOnStartup = _chkUpdateAuto.Checked; SaveConfig(); };
            UpdateUpdateStatus();

            _lblDataDir.Text = "Thư mục dữ liệu: " + AppPaths.DataDir;
            UpdateNatTLabel();
            UpdateVpnCredsLabel();
            try { _lblSystemInfo.Text = AppInfo.SystemSummaryShort(); }
            catch (Exception ex) { _lblSystemInfo.Text = "Không đọc được thông tin hệ thống: " + ex.Message; }
        }

        private void BtnResetSettings_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, "Đặt lại các tuỳ chọn về mặc định? (Danh sách DNS, tài khoản VPN, lịch sử và bản sao lưu không bị ảnh hưởng.)",
                    "Đặt lại cài đặt", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _config.ResetOptions();
            ReloadSettingsValues();
            SaveConfig();
            Log("Đã đặt lại cài đặt về mặc định.");
            Notice("Đã đặt lại cài đặt về mặc định.", NoticeKind.Info);
        }

        /// <summary>Mở hướng dẫn từng bước; mỗi trang chuyển cửa sổ chính sang tab liên quan.</summary>
        private void ShowGuide(bool startup)
        {
            if (_guideOpen) return;
            _guideOpen = true;
            int tabBefore = _tabs.SelectedIndex;
            try
            {
                using (var dlg = new GuideForm(_config.ShowGuideOnStartup, idx =>
                {
                    if (idx >= 0 && idx < _tabs.TabPages.Count) _tabs.SelectedIndex = idx;
                }))
                {
                    dlg.ShowDialog(this);
                    bool show = dlg.ShowOnStartup;
                    if (show != _config.ShowGuideOnStartup)
                    {
                        _config.ShowGuideOnStartup = show;
                        _chkGuideOnStartup.Checked = show;
                        SaveConfig();
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("Không mở được hướng dẫn: " + ex.Message);
            }
            finally
            {
                _guideOpen = false;
            }
            if (tabBefore >= 0 && tabBefore < _tabs.TabPages.Count) _tabs.SelectedIndex = tabBefore;
            if (startup) Log("Hướng dẫn lần đầu đã " + (_config.ShowGuideOnStartup ? "hiện; sẽ hiện lại ở lần mở sau (tắt trong Cài đặt)." : "hiện; sẽ không hiện lại (bật lại trong Cài đặt hoặc bấm nút Hướng dẫn)."));
        }

        private TabPage BuildVpnTab()
        {
            var page = new TabPage("VPN");
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(8) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var intro = new Label
            {
                Dock = DockStyle.Fill,
                Height = 38,
                ForeColor = Color.DimGray,
                Margin = new Padding(0, 0, 0, 6),
                Text = "Dùng VPN tích hợp sẵn của Windows (IKEv2, L2TP/IPsec, SSTP, PPTP), không cần cài thêm phần mềm; cần tài khoản VPN của nhà cung cấp hoặc máy chủ riêng. "
                     + "Khi đổi DNS không đủ (ISP chặn ở tầng SNI/IP), hãy kết nối VPN rồi kiểm tra lại Steam."
            };

            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0) };
            _btnVpnRefresh = ToolButton("Làm mới", BtnVpnRefresh_Click, 100);
            _btnVpnAdd = ToolButton("Thêm cấu hình VPN…", BtnVpnAdd_Click, 160);
            _btnVpnRemove = ToolButton("Xoá cấu hình", BtnVpnRemove_Click, 120);
            _btnVpnIp = ToolButton("Kiểm tra IP công cộng", BtnVpnIp_Click, 160);
            _lblVpnIp = new Label { AutoSize = true, Margin = new Padding(4, 8, 10, 0), Text = "IP công cộng: (chưa kiểm tra)" };
            _btnStop3 = ToolButton("■ Dừng", BtnStop_Click, 80);
            _btnStop3.Enabled = false;
            bar.Controls.AddRange(new Control[] { _btnVpnRefresh, _btnVpnAdd, _btnVpnRemove, _btnVpnIp, _lblVpnIp, _btnStop3 });

            _dgvVpn = MakeGrid();
            AddTextColumn(_dgvVpn, "Name", "Tên cấu hình", 200);
            AddTextColumn(_dgvVpn, "Server", "Máy chủ", 220);
            AddTextColumn(_dgvVpn, "Type", "Giao thức", 100);
            AddTextColumn(_dgvVpn, "Status", "Trạng thái", 110);
            AddTextColumn(_dgvVpn, "Auth", "Xác thực", 120);
            AddTextColumn(_dgvVpn, "Scope", "Phạm vi", 120);
            AddTextColumn(_dgvVpn, "Split", "Split tunnel", 90);
            AddTextColumn(_dgvVpn, "Remember", "Windows nhớ TK", 110);
            var enc = AddTextColumn(_dgvVpn, "Enc", "Mã hoá", 120);
            enc.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _dgvVpn.SelectionChanged += (s, e) =>
            {
                if (_fillingVpn) return;
                LoadVpnCredentialFields();
                UpdateVpnButtons();
            };

            var grp = new GroupBox { Text = "Kết nối", Dock = DockStyle.Fill, Padding = new Padding(8), Height = 120, Margin = new Padding(0, 6, 0, 0) };
            var rows = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            rows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var row1 = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0) };
            _txtVpnUser = new TextBox { Width = 200, Margin = new Padding(0, 4, 12, 0) };
            _txtVpnPass = new TextBox { Width = 200, UseSystemPasswordChar = true, Margin = new Padding(0, 4, 12, 0) };
            _chkVpnRemember = new CheckBox { Text = "Nhớ mật khẩu (mã hoá theo tài khoản Windows)", AutoSize = true, Margin = new Padding(0, 7, 0, 0) };
            row1.Controls.AddRange(new Control[]
            {
                new Label { Text = "Tên đăng nhập:", AutoSize = true, Margin = new Padding(0, 8, 6, 0) }, _txtVpnUser,
                new Label { Text = "Mật khẩu:", AutoSize = true, Margin = new Padding(0, 8, 6, 0) }, _txtVpnPass,
                _chkVpnRemember
            });
            var row2 = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0) };
            _btnVpnConnect = ToolButton("Kết nối", BtnVpnConnect_Click, 110);
            _btnVpnConnectCheck = ToolButton("⚡ Kết nối VPN và kiểm tra Steam", BtnVpnConnectCheck_Click, 240);
            _btnVpnConnectCheck.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            _btnVpnDisconnect = ToolButton("Ngắt kết nối", BtnVpnDisconnect_Click, 120);
            _lblVpnStatus = new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Margin = new Padding(8, 8, 0, 0),
                Text = "Để trống tên đăng nhập và mật khẩu nếu Windows đã nhớ tài khoản của cấu hình này."
            };
            row2.Controls.AddRange(new Control[] { _btnVpnConnect, _btnVpnConnectCheck, _btnVpnDisconnect, _lblVpnStatus });
            rows.Controls.Add(row1, 0, 0);
            rows.Controls.Add(row2, 0, 1);
            grp.Controls.Add(rows);

            // --- VPN miễn phí: VPN Gate ---
            var bar2 = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 8, 0, 0) };
            var lblGate = new Label
            {
                Text = "VPN miễn phí – VPN Gate (vpngate.net, ĐH Tsukuba, Nhật; tài khoản vpn / vpn; máy chủ tình nguyện, có ghi log ~2 tuần):",
                AutoSize = true,
                Margin = new Padding(0, 8, 8, 0),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            _btnVpnGateFind = ToolButton("Tìm máy chủ VPN Gate", BtnVpnGateFind_Click, 170);
            _chkVpnGatePreferSstp = new CheckBox { Text = "Ưu tiên SSTP (TCP 443, không vướng NAT)", AutoSize = true, Checked = true, Margin = new Padding(0, 7, 10, 0) };
            // (giá trị thật được nạp từ config trong InitSettingsTab)
            _btnVpnGateUse = ToolButton("⚡ Tạo cấu hình, kết nối và kiểm tra Steam", BtnVpnGateUse_Click, 290);
            _btnVpnGateUse.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            bar2.Controls.AddRange(new Control[] { lblGate, _btnVpnGateFind, _chkVpnGatePreferSstp, _btnVpnGateUse });

            _dgvVpnGate = MakeGrid();
            AddTextColumn(_dgvVpnGate, "CC", "Quốc gia", 70);
            AddTextColumn(_dgvVpnGate, "Host", "Máy chủ", 220);
            AddTextColumn(_dgvVpnGate, "Ip", "IP", 125);
            AddTextColumn(_dgvVpnGate, "Proto", "Giao thức Windows", 125);
            AddTextColumn(_dgvVpnGate, "Probe", "Tới được (ms)", 95);
            AddTextColumn(_dgvVpnGate, "Ping", "Ping", 55);
            AddTextColumn(_dgvVpnGate, "Speed", "Mbps", 65);
            AddTextColumn(_dgvVpnGate, "Sessions", "Phiên", 55);
            AddTextColumn(_dgvVpnGate, "Score", "Điểm", 90);
            var gl = AddTextColumn(_dgvVpnGate, "Log", "Log / ghi chú", 150);
            gl.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            root.Controls.Add(intro, 0, 0);
            root.Controls.Add(bar, 0, 1);
            root.Controls.Add(_dgvVpn, 0, 2);
            root.Controls.Add(bar2, 0, 3);
            root.Controls.Add(_dgvVpnGate, 0, 4);
            root.Controls.Add(grp, 0, 5);
            page.Controls.Add(root);

            _vpnTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            _vpnTimer.Tick += (s, e) => RefreshVpnStatusQuick();
            return page;
        }

        private TabPage BuildDashboardTab()
        {
            var page = new TabPage("Bảng điều khiển");
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(8) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 370));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));

            // --- Thanh trạng thái: thông báo, lệnh đang chạy, tiến trình ---
            _lblNotice = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 10, 0),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                AutoEllipsis = true,
                BackColor = Color.FromArgb(225, 238, 250),
                ForeColor = Color.FromArgb(20, 60, 110)
            };
            _lblCommand = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Text = "Lệnh đang chạy: (không có)" };
            _btnGuideDash = new Button { Text = "❔ Hướng dẫn", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 0), UseVisualStyleBackColor = true };
            _btnGuideDash.Click += (s, e) => ShowGuide(false);
            var cmdRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            cmdRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            cmdRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            cmdRow.Controls.Add(_lblCommand, 0, 0);
            cmdRow.Controls.Add(_btnGuideDash, 1, 0);
            _progress = new ProgressBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100, Style = ProgressBarStyle.Continuous, Margin = new Padding(0, 4, 8, 4) };
            _lblProgress = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            var progRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            progRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            progRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
            progRow.Controls.Add(_progress, 0, 0);
            progRow.Controls.Add(_lblProgress, 1, 0);
            var status = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0, 0, 0, 4) };
            status.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            status.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            status.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            status.Controls.Add(_lblNotice, 0, 0);
            status.Controls.Add(cmdRow, 0, 1);
            status.Controls.Add(progRow, 0, 2);
            root.Controls.Add(status, 0, 0);
            root.SetColumnSpan(status, 2);

            // --- Cột trái: thao tác + card mạng ---
            _btnCheck = MakeButton("1. Kiểm tra kết nối Steam hiện tại", BtnCheck_Click, false);
            var lblChoose = new Label { Text = "Chọn DNS muốn áp dụng (đổi cả IPv4 và IPv6):", Height = 22, TextAlign = ContentAlignment.BottomLeft };
            _cmbProviders = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 2, 0, 6) };
            _btnApply = MakeButton("2. Áp dụng DNS đã chọn", BtnApply_Click, false);
            _btnRecheck = MakeButton("3. Kiểm tra lại kết nối sau khi đổi", BtnRecheck_Click, false);
            _lblBackup = new Label { Height = 44, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DimGray, AutoEllipsis = true };
            _btnRevert = MakeButton("4. Thu hồi – khôi phục DNS gốc, ngắt VPN", BtnRevert_Click, false);
            _chkKeepOnExit = new CheckBox { Text = "Giữ thay đổi sau khi thoát app (không tự khôi phục)", Height = 24, Margin = new Padding(2, 0, 0, 4) };
            _btnBest = MakeButton("⚡ Chọn nhanh DNS tốt nhất và áp dụng ngay", BtnBest_Click, true);
            _btnStop = MakeButton("■ Dừng thao tác đang chạy", BtnStop_Click, false);
            _btnStop.Enabled = false;
            _btnOpenData = MakeButton("Mở thư mục dữ liệu (cấu hình, sao lưu, nhật ký)", BtnOpenData_Click, false);
            var actions = VStack(_btnCheck, lblChoose, _cmbProviders, _btnApply, _btnRecheck, _lblBackup, _btnRevert, _chkKeepOnExit, _btnBest, _btnStop, _btnOpenData);
            var grpActions = new GroupBox { Text = "Thao tác", Dock = DockStyle.Fill, Padding = new Padding(8), Margin = new Padding(0, 0, 6, 0) };
            grpActions.Controls.Add(actions);
            root.Controls.Add(grpActions, 0, 1);

            // --- Cột phải: kết quả + card mạng / DNS hiện tại ---
            _lvResults = MakeListView(new[] { "Tên miền", "DNS", "Địa chỉ IP", "HTTPS", "Tổng (ms)", "Chứng chỉ" }, new[] { 180, 110, 215, 185, 70, -2 });
            var grpResults = new GroupBox { Text = "Kết quả kiểm tra Steam (DNS + HTTPS) bằng DNS hiện tại của hệ thống", Dock = DockStyle.Fill, Padding = new Padding(8) };
            grpResults.Controls.Add(_lvResults);

            _lvAdapters = MakeListView(new[] { "Card mạng", "DNS IPv4 hiện tại", "DNS IPv6 hiện tại", "Nguồn", "Trạng thái" }, new[] { 200, 170, 270, 150, -2 });
            _lvAdapters.CheckBoxes = true;
            _lvAdapters.ItemCheck += (s, e) => { if (_busy) e.NewValue = e.CurrentValue; };
            _btnRefreshAdapters = new Button { Text = "Làm mới", Dock = DockStyle.Bottom, Height = 28 };
            _btnRefreshAdapters.Click += (s, e) => RefreshAdapters();
            var grpAdapters = new GroupBox
            {
                Text = "Card mạng – tích chọn card sẽ được đổi DNS (mặc định: card đang kết nối có gateway, in đậm)",
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };
            grpAdapters.Controls.Add(_lvAdapters);
            grpAdapters.Controls.Add(_btnRefreshAdapters);

            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            right.Controls.Add(grpResults, 0, 0);
            right.Controls.Add(grpAdapters, 0, 1);
            root.Controls.Add(right, 1, 1);

            // --- Nhật ký ---
            _rtbLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.White,
                Font = new Font("Consolas", 9f),
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.Both,
                DetectUrls = false
            };
            var grpLog = new GroupBox { Text = "Nhật ký / quá trình xử lý", Dock = DockStyle.Fill, Padding = new Padding(8) };
            grpLog.Controls.Add(_rtbLog);
            root.Controls.Add(grpLog, 0, 2);
            root.SetColumnSpan(grpLog, 2);

            page.Controls.Add(root);
            return page;
        }

        private TabPage BuildHistoryTab()
        {
            var page = new TabPage("Lịch sử thay đổi");
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0) };
            var btnRefresh = ToolButton("Làm mới", (s, e) => RefreshHistory(), 110);
            var btnExport = ToolButton("Xuất CSV…", BtnExportHistory_Click, 110);
            var btnClear = ToolButton("Xoá lịch sử", BtnClearHistory_Click, 110);
            var hint = new Label
            {
                Text = "Ghi lại mọi lần áp dụng / thu hồi DNS và kết quả kiểm tra Steam. Mới nhất ở trên.",
                AutoSize = true,
                Margin = new Padding(10, 8, 0, 0),
                ForeColor = Color.DimGray
            };
            bar.Controls.AddRange(new Control[] { btnRefresh, btnExport, btnClear, hint });

            _dgvHistory = MakeGrid();
            AddTextColumn(_dgvHistory, "Time", "Thời gian", 135);
            AddTextColumn(_dgvHistory, "Action", "Hành động", 140);
            AddTextColumn(_dgvHistory, "Adapter", "Card mạng", 150);
            var det = AddTextColumn(_dgvHistory, "Detail", "Chi tiết", 400);
            det.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            AddTextColumn(_dgvHistory, "Result", "Kết quả", 230);

            root.Controls.Add(bar, 0, 0);
            root.Controls.Add(_dgvHistory, 0, 1);
            page.Controls.Add(root);
            return page;
        }

        private TabPage BuildProvidersTab()
        {
            var page = new TabPage("Danh sách DNS");
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 48));

            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0) };
            _btnTestAll = ToolButton("Kiểm tra tất cả DNS", BtnTestAll_Click, 150);
            _btnDiscover = ToolButton("Tự động tìm, lọc và báo cáo", BtnDiscover_Click, 200);
            var lblCountry = new Label { Text = "Mã quốc gia:", AutoSize = true, Margin = new Padding(6, 8, 2, 0) };
            _txtCountry = new TextBox { Width = 42, MaxLength = 2, CharacterCasing = CharacterCasing.Upper, Margin = new Padding(0, 5, 10, 0) };
            _chkAutoDisable = new CheckBox { Text = "Tự động tắt DNS lỗi", AutoSize = true, Margin = new Padding(0, 7, 10, 0) };
            _btnAddCustom = ToolButton("Thêm DNS…", BtnAddCustom_Click, 110);
            _btnToggle = ToolButton("Bật / Tắt", BtnToggle_Click, 90);
            _btnRemove = ToolButton("Xoá / bỏ khỏi lựa chọn", BtnRemove_Click, 170);
            _btnResetCatalog = ToolButton("Khôi phục mặc định", BtnReset_Click, 150);
            _btnStop2 = ToolButton("■ Dừng", BtnStop_Click, 80);
            _btnStop2.Enabled = false;
            bar.Controls.AddRange(new Control[] { _btnTestAll, _btnDiscover, lblCountry, _txtCountry, _chkAutoDisable, _btnAddCustom, _btnToggle, _btnRemove, _btnResetCatalog, _btnStop2 });

            _dgvProviders = MakeGrid();
            AddCheckColumn(_dgvProviders, "Enabled", "Dùng", 48);
            AddTextColumn(_dgvProviders, "Name", "Tên", 240);
            AddTextColumn(_dgvProviders, "Ipv4", "IPv4", 180);
            AddTextColumn(_dgvProviders, "Ipv6", "IPv6", 270);
            AddTextColumn(_dgvProviders, "Category", "Loại", 70);
            AddTextColumn(_dgvProviders, "Result", "Kết quả gần nhất", 185);
            AddTextColumn(_dgvProviders, "Score", "Điểm", 50);
            AddTextColumn(_dgvProviders, "Latency", "Trễ (ms)", 65);
            AddTextColumn(_dgvProviders, "Tested", "Lúc", 105);
            var notes = AddTextColumn(_dgvProviders, "Notes", "Ghi chú", 200);
            notes.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _dgvProviders.CellContentClick += DgvProviders_CellContentClick;

            var bar2 = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 6, 0, 0) };
            var lblDisc = new Label
            {
                Text = "DNS công cộng tìm thấy trên internet (" + DiscoveryService.SourceName + "), đã kiểm tra với Steam – chỉ nên thêm DNS hoạt động tốt:",
                AutoSize = true,
                Margin = new Padding(0, 8, 10, 0)
            };
            _btnAddDiscovered = ToolButton("Thêm DNS đã chọn vào danh sách", BtnAddDiscovered_Click, 230);
            bar2.Controls.AddRange(new Control[] { lblDisc, _btnAddDiscovered });

            _dgvDiscovered = MakeGrid();
            AddCheckColumn(_dgvDiscovered, "Pick", "Chọn", 48);
            AddTextColumn(_dgvDiscovered, "Name", "Tên / nhà mạng", 230);
            AddTextColumn(_dgvDiscovered, "Ip", "Địa chỉ", 150);
            AddTextColumn(_dgvDiscovered, "Status", "Kết quả", 150);
            AddTextColumn(_dgvDiscovered, "Score", "Điểm", 50);
            AddTextColumn(_dgvDiscovered, "Latency", "Trễ (ms)", 65);
            var n2 = AddTextColumn(_dgvDiscovered, "Notes", "Ghi chú", 200);
            n2.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _dgvDiscovered.CellContentClick += DgvDiscovered_CellContentClick;

            _rtbReport = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White, Font = new Font("Consolas", 9f), WordWrap = true, DetectUrls = false };
            var grpReport = new GroupBox { Text = "Báo cáo", Dock = DockStyle.Fill, Padding = new Padding(6) };
            grpReport.Controls.Add(_rtbReport);

            var lower = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
            lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
            lower.Controls.Add(_dgvDiscovered, 0, 0);
            lower.Controls.Add(grpReport, 1, 0);

            root.Controls.Add(bar, 0, 0);
            root.Controls.Add(_dgvProviders, 0, 1);
            root.Controls.Add(bar2, 0, 2);
            root.Controls.Add(lower, 0, 3);
            page.Controls.Add(root);
            return page;
        }

        private static Button MakeButton(string text, EventHandler onClick, bool bold)
        {
            var b = new Button
            {
                Text = text,
                Height = 34,
                Margin = new Padding(0, 2, 0, 4),
                UseVisualStyleBackColor = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 0, 0)
            };
            if (bold)
            {
                b.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                b.Height = 40;
                b.BackColor = Color.FromArgb(235, 244, 255);
                b.UseVisualStyleBackColor = false;
            }
            b.Click += onClick;
            return b;
        }

        private static Button ToolButton(string text, EventHandler onClick, int width)
        {
            var b = new Button { Text = text, Width = width, Height = 30, Margin = new Padding(0, 0, 6, 6), UseVisualStyleBackColor = true };
            b.Click += onClick;
            return b;
        }

        private static TableLayoutPanel VStack(params Control[] controls)
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = controls.Length + 1, Margin = new Padding(0) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < controls.Length; i++)
            {
                t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                controls[i].Dock = DockStyle.Fill;
                t.Controls.Add(controls[i], 0, i);
            }
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            return t;
        }

        private int Px(int designPixels)
        {
            return (int)Math.Round(designPixels * _scale);
        }

        private ListView MakeListView(string[] headers, int[] widths)
        {
            var lv = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = false
            };
            for (int i = 0; i < headers.Length; i++) lv.Columns.Add(headers[i], widths[i] > 0 ? Px(widths[i]) : widths[i]); // -2 = cột cuối tự giãn
            return lv;
        }

        private DataGridView MakeGrid()
        {
            var g = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoGenerateColumns = false,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
            };
            g.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            g.RowTemplate.Height = Px(24);
            return g;
        }

        private DataGridViewTextBoxColumn AddTextColumn(DataGridView g, string name, string header, int width)
        {
            var c = new DataGridViewTextBoxColumn { Name = name, HeaderText = header, Width = Px(width), SortMode = DataGridViewColumnSortMode.NotSortable };
            g.Columns.Add(c);
            return c;
        }

        private DataGridViewCheckBoxColumn AddCheckColumn(DataGridView g, string name, string header, int width)
        {
            var c = new DataGridViewCheckBoxColumn { Name = name, HeaderText = header, Width = Px(width), SortMode = DataGridViewColumnSortMode.NotSortable };
            g.Columns.Add(c);
            return c;
        }

        // =====================================================================
        //  Khởi động / đóng
        // =====================================================================

        private void MainForm_Load(object sender, EventArgs e)
        {
            AppPaths.EnsureDataDir();
            try
            {
                _config = ProviderCatalog.LoadConfig();
            }
            catch (Exception ex)
            {
                _config = new AppConfig();
                _config.Providers = ProviderCatalog.BuiltIn();
                LogError("Không đọc được cấu hình, dùng mặc định: " + ex.Message);
            }
            _txtCountry.Text = _config.DiscoveryCountry;
            _chkAutoDisable.Checked = _config.AutoDisableFailed;
            _txtCountry.TextChanged += (s, a) => { _config.DiscoveryCountry = _txtCountry.Text.Trim(); };
            _chkAutoDisable.CheckedChanged += (s, a) => { _config.AutoDisableFailed = _chkAutoDisable.Checked; };
            _chkKeepOnExit.Checked = _config.KeepChangesOnExit;
            _chkKeepOnExit.CheckedChanged += (s, a) =>
            {
                _config.KeepChangesOnExit = _chkKeepOnExit.Checked;
                SaveConfig();
                if (_chkKeepOnExit.Checked) LogWarn("Đã chọn GIỮ thay đổi khi thoát: DNS/VPN sẽ không tự khôi phục khi đóng app.");
                else Log("Khi thoát app sẽ tự khôi phục DNS gốc và ngắt VPN do app kết nối.");
                UpdateBackupStatus();
            };
            InitSettingsTab();

            RefreshProvidersCombo();
            RefreshProvidersGrid();
            RefreshHistory();
            RefreshAdapters();
            UpdateBackupStatus();

            Log("Steam Connect Toolkit 1.0 – tác giả Quynh Nguyen – đang chạy với quyền Administrator. Thư mục dữ liệu: " + AppPaths.DataDir);
            Log("Tên miền kiểm tra: " + string.Join(", ", SteamChecker.Domains));
            Log("Card mạng được chọn mặc định: " + string.Join(", ", GetCheckedAdapters().Select(a => a.Name)));
            UpdateVpnButtons();
            _vpnTimer.Start();
            StartSessionWithRecovery();
            Notice("Sẵn sàng. Bấm \"1. Kiểm tra kết nối Steam hiện tại\" hoặc \"Chọn nhanh DNS tốt nhất\" để bắt đầu.", NoticeKind.Info);
            if (_config.AutoCheckOnStartup)
            {
                Log("Tự động kiểm tra Steam khi mở app (tắt trong Cài đặt).");
                BeginInvoke(new Action(() => BtnCheck_Click(this, EventArgs.Empty)));
            }
            UpdateService.Cleanup();
            if (Program.AfterUpdate)
            {
                HistoryService.Add("Nâng cấp", "(app)", "Đã nâng cấp lên phiên bản " + AppInfo.Version, "Thành công");
                RefreshHistory();
                LogOk("✔ Đã nâng cấp lên phiên bản " + AppInfo.Version + ".");
                Notice("Đã nâng cấp lên phiên bản " + AppInfo.Version + ".", NoticeKind.Success);
            }
            if (_config.UpdateCheckOnStartup && !string.IsNullOrWhiteSpace(_config.UpdateManifestUrl) && UpdateService.IsCheckDue(_config.LastUpdateCheck))
                BeginInvoke(new Action(async () => await StartupUpdateCheckAsync()));
        }

        // =====================================================================
        //  Cập nhật từ xa
        // =====================================================================

        private void UpdateUpdateStatus()
        {
            string ver = UpdateService.CurrentVersion().ToString(3);
            bool hasUrl = !string.IsNullOrWhiteSpace(_config.UpdateManifestUrl);
            if (!hasUrl)
            {
                _lblUpdateStatus.Text = "Phiên bản " + ver + " – chưa cấu hình địa chỉ manifest, cập nhật từ xa đang tắt.";
                _lblUpdateStatus.ForeColor = Color.DimGray;
            }
            else if (_lastUpdate == null)
            {
                _lblUpdateStatus.Text = "Phiên bản " + ver + (string.IsNullOrEmpty(_config.LastUpdateCheck) ? " – chưa kiểm tra bản mới." : " – kiểm tra lần cuối " + _config.LastUpdateCheck + ".");
                _lblUpdateStatus.ForeColor = Color.DimGray;
            }
            else
            {
                _lblUpdateStatus.Text = "Phiên bản " + ver + " – " + _lastUpdate.Describe();
                _lblUpdateStatus.ForeColor = !_lastUpdate.Ok ? ColorErr : (_lastUpdate.UpdateAvailable ? ColorWarn : ColorOk);
            }
            _btnInstallUpdate.Enabled = !_busy && _lastUpdate != null && _lastUpdate.Ok && _lastUpdate.UpdateAvailable;
            _btnCheckUpdate.Enabled = !_busy && hasUrl;
        }

        private async Task<UpdateCheckResult> RunUpdateCheckAsync(CancellationToken ct)
        {
            string url = (_config.UpdateManifestUrl ?? "").Trim();
            var cur = UpdateService.CurrentVersion();
            bool installed = UpdateService.IsInstalledCopy();
            var r = await Task.Run(() => UpdateService.Check(url, cur, installed, 15000), ct);
            _lastUpdate = r;
            _config.LastUpdateCheck = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            SaveConfig();
            UI(UpdateUpdateStatus);
            if (!r.Ok) LogWarn("Kiểm tra bản mới: " + r.Error);
            else if (!r.UpdateAvailable) Log("Cập nhật: đang dùng bản mới nhất (" + cur.ToString(3) + ").");
            else LogWarn("Cập nhật: có bản " + r.Manifest.Version + (r.Mandatory ? " (bắt buộc)" : "") + (string.IsNullOrEmpty(r.Manifest.Notes) ? "" : " – " + r.Manifest.Notes));
            return r;
        }

        /// <summary>Kiểm tra ngầm khi khởi động (không chiếm trạng thái bận, không hiện hộp thoại).</summary>
        private async Task StartupUpdateCheckAsync()
        {
            try
            {
                var r = await RunUpdateCheckAsync(CancellationToken.None);
                if (r.Ok && r.UpdateAvailable)
                {
                    bool skipped = !r.Mandatory && string.Equals(_config.SkippedUpdateVersion, r.Manifest.Version, StringComparison.OrdinalIgnoreCase);
                    if (!skipped) Notice("Có bản mới " + r.Manifest.Version + (r.Mandatory ? " (bắt buộc)" : "") + ". Vào Cài đặt → Cập nhật từ xa để tải và cài.", NoticeKind.Warning);
                }
            }
            catch (Exception ex)
            {
                LogWarn("Kiểm tra bản mới lỗi: " + ex.Message);
            }
        }

        private async void BtnCheckUpdate_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_config.UpdateManifestUrl))
            {
                Notice("Hãy nhập địa chỉ manifest (https://…/latest.json) trước.", NoticeKind.Warning);
                return;
            }
            await RunOperation("Kiểm tra bản mới", async ct =>
            {
                SetMarquee("Đang tải manifest…");
                var r = await RunUpdateCheckAsync(ct);
                if (!r.Ok) { Notice("Không kiểm tra được bản mới: " + r.Error, NoticeKind.Warning); return; }
                if (!r.UpdateAvailable) { Notice("Bạn đang dùng bản mới nhất " + r.Current.ToString(3) + ".", NoticeKind.Success); return; }
                Notice("Có bản mới " + r.Manifest.Version + ". Bấm \"Tải và cài đặt bản mới\".", NoticeKind.Warning);
                UI(() => OfferUpdate(r));
            });
        }

        private void OfferUpdate(UpdateCheckResult r)
        {
            string msg = "Có bản mới " + r.Manifest.Version + (string.IsNullOrEmpty(r.Manifest.Published) ? "" : " (phát hành " + r.Manifest.Published + ")")
                + " – bạn đang dùng " + r.Current.ToString(3) + "."
                + (string.IsNullOrEmpty(r.Manifest.Notes) ? "" : "\n\nThay đổi:\n" + r.Manifest.Notes)
                + (r.Mandatory ? "\n\nBản này là bắt buộc (phiên bản hiện tại không còn được hỗ trợ)." : "")
                + "\n\nTải và cài đặt ngay? (No: để sau; Cancel: bỏ qua bản này)";
            var ans = MessageBox.Show(this, msg, "Có bản mới", r.Mandatory ? MessageBoxButtons.YesNo : MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);
            if (ans == DialogResult.Yes) BtnInstallUpdate_Click(this, EventArgs.Empty);
            else if (ans == DialogResult.Cancel)
            {
                _config.SkippedUpdateVersion = r.Manifest.Version;
                SaveConfig();
                Log("Đã bỏ qua bản " + r.Manifest.Version + " (sẽ không nhắc lại khi khởi động).");
            }
        }

        private async void BtnInstallUpdate_Click(object sender, EventArgs e)
        {
            var r = _lastUpdate;
            if (r == null || !r.Ok || !r.UpdateAvailable)
            {
                Notice("Chưa có bản mới để cài. Bấm \"Kiểm tra bản mới\" trước.", NoticeKind.Info);
                return;
            }
            await RunOperation("Tải bản cập nhật " + r.Manifest.Version, ct => DownloadUpdateCoreAsync(r, ct));
        }

        private async Task DownloadUpdateCoreAsync(UpdateCheckResult r, CancellationToken ct)
        {
            string fileName = (r.UsesInstaller ? "SteamConnectToolkit-Setup-" : "SteamConnectToolkit-") + r.Manifest.Version + ".exe";
            string dest = Path.Combine(UpdateService.UpdatesDir, fileName);
            SetProgress(0, 100, "Đang tải…");
            Log("Tải " + r.Package.Url + " → " + dest);
            string path = await Task.Run(() => UpdateService.Download(r.Package, dest, (done, total) =>
            {
                if (total > 0) SetProgress((int)(done * 100 / total), 100, Math.Round(done / 1048576.0, 1) + " / " + Math.Round(total / 1048576.0, 1) + " MB");
                else SetProgress(0, 100, Math.Round(done / 1048576.0, 1) + " MB");
            }, ct), ct);
            string signer;
            bool signOk = UpdateService.VerifySigner(path, _config.UpdateSignerThumbprint, out signer);
            Log("Đã tải xong, SHA-256 khớp manifest" + (signer != null ? ", chữ ký số: " + signer : ", file không có chữ ký số") + ".");
            if (!signOk)
            {
                try { File.Delete(path); } catch (Exception) { }
                throw new InvalidOperationException("Chữ ký số của gói không khớp nhà phát hành mong đợi (" + _config.UpdateSignerThumbprint + ") – huỷ cập nhật.");
            }
            HistoryService.Add("Nâng cấp", "(app)", "Đã tải và xác minh bản " + r.Manifest.Version + ": " + path, "Sẵn sàng cài");
            UI(RefreshHistory);
            Notice("Đã tải và kiểm tra bản " + r.Manifest.Version + ".", NoticeKind.Success);
            string installerPath = path;
            bool usesInstaller = r.UsesInstaller;
            UI(() =>
            {
                string how = usesInstaller ? "chạy bộ cài (im lặng, giữ nguyên dữ liệu)" : "thay file chương trình (bản cũ giữ ở .bak)";
                string exit = _config.KeepChangesOnExit ? "giữ thay đổi DNS/VPN theo cài đặt" : "khôi phục DNS/VPN như khi thoát bình thường";
                var ask = MessageBox.Show(this,
                    "Đã tải và kiểm tra bản " + r.Manifest.Version + ".\n\nApp sẽ " + exit + ", đóng, " + how + ", rồi tự mở lại.\n\nCài đặt ngay bây giờ?",
                    "Cài đặt bản mới", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (ask == DialogResult.Yes)
                {
                    _pendingUpdatePath = installerPath;
                    _pendingUpdateInstaller = usesInstaller;
                    Close();
                }
                else Log("Gói cập nhật đã tải sẵn ở " + installerPath + "; bấm \"Tải và cài đặt bản mới\" khi muốn cài.");
            });
        }

        /// <summary>Gọi ở cuối FormClosing (khi không bị huỷ): chạy bộ cài hoặc tiến trình thay file.</summary>
        private void LaunchPendingUpdate()
        {
            if (string.IsNullOrEmpty(_pendingUpdatePath)) return;
            try
            {
                if (_pendingUpdateInstaller) UpdateService.LaunchInstaller(_pendingUpdatePath);
                else UpdateService.LaunchPortableUpdater(_pendingUpdatePath);
                HistoryService.Add("Nâng cấp", "(app)", (_pendingUpdateInstaller ? "Chạy bộ cài: " : "Thay file từ: ") + _pendingUpdatePath, "Đã khởi chạy");
                GuardianRunner.FileLog("Cập nhật: đã khởi chạy " + (_pendingUpdateInstaller ? "bộ cài " : "tiến trình thay file ") + _pendingUpdatePath);
            }
            catch (Exception ex)
            {
                GuardianRunner.FileLog("Cập nhật: không khởi chạy được – " + ex.Message);
                MessageBox.Show(this, "Không khởi chạy được bản cập nhật: " + ex.Message + "\n\nFile đã tải: " + _pendingUpdatePath, "Cập nhật", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            _pendingUpdatePath = null;
        }

        /// <summary>Kiểm tra phiên trước (dừng đột ngột / giữ thay đổi), đề nghị khôi phục, rồi mở phiên mới và chạy guardian.</summary>
        private void StartSessionWithRecovery()
        {
            SessionState prev = null;
            bool pending = false;
            try
            {
                prev = SessionService.Load();
                pending = SessionService.HasPending(prev, _config);
            }
            catch (Exception ex)
            {
                LogError("Không đọc được trạng thái phiên trước: " + ex.Message);
            }
            if (pending)
            {
                string what = SessionService.DescribePending(prev, _config);
                bool unclean = prev != null && prev.Pid != 0 && !prev.CleanExit;
                bool kept = prev != null && prev.CleanExit && prev.KeepOnExit;
                if (kept)
                {
                    LogWarn("Phiên trước bạn đã chọn giữ thay đổi:" + Environment.NewLine + what + Environment.NewLine + "Bấm \"4. Thu hồi\" khi muốn khôi phục.");
                }
                else
                {
                    var r = MessageBox.Show(this,
                        (unclean ? "Phiên trước kết thúc không bình thường (app dừng đột ngột). " : "") + "Hiện còn thay đổi do app gây ra:\n\n" + what +
                        "\n\nKhôi phục cấu hình gốc ngay bây giờ?\n(No = giữ nguyên, bạn có thể bấm \"4. Thu hồi\" sau.)",
                        "Khôi phục cấu hình", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (r == DialogResult.Yes)
                    {
                        Notice("Đang khôi phục cấu hình gốc…", NoticeKind.Info);
                        RecoveryReport rep = null;
                        try { rep = RecoveryService.RestoreAll(prev, _config, true, Log); }
                        catch (Exception ex) { LogError("Khôi phục lỗi: " + ex.Message); }
                        string summary = rep != null ? rep.Summary() : "lỗi";
                        HistoryService.Add("Khôi phục khi khởi động", "(app)", summary, rep != null && rep.AllOk ? "Thành công" : "Một phần lỗi");
                        RefreshHistory();
                        RefreshAdapters();
                        if (rep != null && rep.AllOk) LogOk("✔ Đã khôi phục: " + summary);
                        else LogError("✖ Khôi phục chưa trọn vẹn: " + summary);
                    }
                    else LogWarn("Giữ nguyên thay đổi từ phiên trước. Bấm \"4. Thu hồi\" khi muốn khôi phục.");
                }
            }
            try
            {
                SessionService.Begin(Process.GetCurrentProcess().Id);
                GuardianRunner.Launch(Log);
            }
            catch (Exception ex)
            {
                LogError("Không mở được phiên theo dõi: " + ex.Message);
            }
            UpdateBackupStatus();
        }

        private void EndSession(bool cleanExit, bool keepOnExit, string note)
        {
            try
            {
                SessionService.Update(s =>
                {
                    s.CleanExit = cleanExit;
                    s.KeepOnExit = keepOnExit;
                    s.EndedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    s.Note = note;
                });
            }
            catch (Exception ex)
            {
                LogError("Không ghi được trạng thái phiên: " + ex.Message);
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            bool shutdown = e.CloseReason == CloseReason.WindowsShutDown;
            if (_busy)
            {
                if (!shutdown)
                {
                    var r0 = MessageBox.Show(this, "Đang có thao tác chạy. Dừng và thoát?", "Steam Connect Toolkit", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (r0 != DialogResult.Yes)
                    {
                        e.Cancel = true;
                        return;
                    }
                }
                if (_cts != null) _cts.Cancel();
                // Chờ thao tác nền dừng hẳn (tối đa 8 giây) để không tranh chấp netsh/rasdial với bước khôi phục bên dưới.
                var waited = Stopwatch.StartNew();
                while (_busy && waited.ElapsedMilliseconds < 8000)
                {
                    Application.DoEvents();
                    Thread.Sleep(50);
                }
                if (_busy) LogWarn("Thao tác nền chưa dừng sau 8 giây, vẫn tiếp tục khôi phục.");
            }
            try { _vpnTimer.Stop(); } catch (Exception) { }

            SessionState session = null;
            bool pending = false;
            try
            {
                session = SessionService.Load();
                pending = SessionService.HasPending(session, _config);
            }
            catch (Exception ex) { LogError("Không đọc được trạng thái phiên: " + ex.Message); }

            if (!pending)
            {
                EndSession(true, false, "thoát bình thường, không có thay đổi");
            }
            else if (shutdown)
            {
                // Windows đang tắt: không hỏi; khôi phục nhanh DNS + ngắt VPN, bỏ qua xoá cấu hình VPN Gate (PowerShell chậm).
                if (_config.KeepChangesOnExit) EndSession(true, true, "Windows tắt; giữ thay đổi theo lựa chọn");
                else
                {
                    RecoveryReport rep = null;
                    try { rep = RecoveryService.RestoreAll(session, _config, false, Log); } catch (Exception) { }
                    bool ok = rep != null && rep.AllOk;
                    HistoryService.Add("Thoát app (Windows tắt)", "(app)", rep != null ? rep.Summary() : "lỗi", ok ? "Đã khôi phục" : "Khôi phục chưa trọn vẹn");
                    EndSession(ok, false, "Windows tắt; " + (rep != null ? rep.Summary() : "lỗi"));
                }
            }
            else if (_config.KeepChangesOnExit)
            {
                string what = SessionService.DescribePending(session, _config);
                HistoryService.Add("Thoát app", "(app)", "Giữ thay đổi theo lựa chọn: " + what.Replace(Environment.NewLine, "; "), "Giữ");
                EndSession(true, true, "giữ thay đổi theo lựa chọn");
            }
            else
            {
                string what = SessionService.DescribePending(session, _config);
                var r = MessageBox.Show(this,
                    "Trước khi thoát, app sẽ khôi phục cấu hình gốc:\n\n" + what + "\n\n" +
                    "Yes: khôi phục rồi thoát (khuyên dùng).\n" +
                    "No: GIỮ thay đổi và thoát (DNS/VPN vẫn như hiện tại cho tới khi bạn bấm Thu hồi).\n" +
                    "Cancel: không thoát.",
                    "Khôi phục cấu hình trước khi thoát", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    try { _vpnTimer.Start(); } catch (Exception) { }
                    return;
                }
                if (r == DialogResult.No)
                {
                    HistoryService.Add("Thoát app", "(app)", "Giữ thay đổi: " + what.Replace(Environment.NewLine, "; "), "Giữ");
                    EndSession(true, true, "người dùng chọn giữ thay đổi khi thoát");
                }
                else
                {
                    Notice("Đang khôi phục cấu hình gốc trước khi thoát…", NoticeKind.Info);
                    SetBusy(true);
                    Refresh();
                    RecoveryReport rep = null;
                    try { rep = RecoveryService.RestoreAll(session, _config, true, Log); }
                    catch (Exception ex) { LogError("Khôi phục lỗi: " + ex.Message); }
                    SetBusy(false);
                    bool ok = rep != null && rep.AllOk;
                    string summary = rep != null ? rep.Summary() : "lỗi";
                    HistoryService.Add("Thoát app", "(app)", summary, ok ? "Đã khôi phục" : "Khôi phục chưa trọn vẹn");
                    if (!ok)
                    {
                        var r2 = MessageBox.Show(this,
                            "Khôi phục chưa trọn vẹn: " + summary + "\n\nVẫn thoát? (Bản sao lưu được giữ lại; guardian sẽ thử lại và lần mở sau app sẽ đề nghị khôi phục tiếp.)",
                            "Khôi phục chưa xong", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        if (r2 != DialogResult.Yes)
                        {
                            e.Cancel = true;
                            try { _vpnTimer.Start(); } catch (Exception) { }
                            return;
                        }
                        EndSession(false, false, "thoát với khôi phục chưa trọn vẹn: " + summary); // CleanExit=false → guardian thử lại
                    }
                    else EndSession(true, false, "đã khôi phục khi thoát: " + summary);
                }
            }
            SaveConfig();
            LaunchPendingUpdate();
        }

        // =====================================================================
        //  Làm mới dữ liệu hiển thị
        // =====================================================================

        private void RefreshAdapters()
        {
            var previously = new HashSet<string>(GetCheckedAdapters().Select(a => a.Id));
            _adapters = NetworkAdapterService.GetAdapters(true);
            _lvAdapters.BeginUpdate();
            _lvAdapters.Items.Clear();
            foreach (var a in _adapters)
            {
                var item = new ListViewItem(a.Name) { Tag = a };
                item.SubItems.Add(a.Ipv4DnsText());
                item.SubItems.Add(a.Ipv6DnsText());
                item.SubItems.Add(a.SourceText());
                item.SubItems.Add(a.TypeText() + (a.IsUp ? ", đang kết nối" : ", tắt") + (a.HasGateway ? "" : ", không gateway"));
                item.Checked = _adaptersLoadedOnce ? previously.Contains(a.Id) : a.Recommended;
                if (a.Recommended) item.Font = new Font(_lvAdapters.Font, FontStyle.Bold);
                else if (!a.IsUp) item.ForeColor = Color.Gray;
                _lvAdapters.Items.Add(item);
            }
            _lvAdapters.EndUpdate();
            _adaptersLoadedOnce = true;
        }

        private List<AdapterInfo> GetCheckedAdapters()
        {
            return _lvAdapters.CheckedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<AdapterInfo>().ToList();
        }

        private void RefreshProvidersCombo()
        {
            var current = _cmbProviders.SelectedItem as DnsProvider;
            string wantId = current != null ? current.Id : _config.LastProviderId;
            _cmbProviders.BeginUpdate();
            _cmbProviders.Items.Clear();
            foreach (var p in _config.Providers.Where(x => x.Enabled)) _cmbProviders.Items.Add(p);
            _cmbProviders.EndUpdate();
            SelectProviderInCombo(wantId);
            if (_cmbProviders.SelectedIndex < 0 && _cmbProviders.Items.Count > 0) _cmbProviders.SelectedIndex = 0;
        }

        private void SelectProviderInCombo(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            for (int i = 0; i < _cmbProviders.Items.Count; i++)
            {
                var p = (DnsProvider)_cmbProviders.Items[i];
                if (p.Id == id)
                {
                    _cmbProviders.SelectedIndex = i;
                    return;
                }
            }
        }

        private void RefreshProvidersGrid()
        {
            _dgvProviders.SuspendLayout();
            _dgvProviders.Rows.Clear();
            foreach (var p in _config.Providers)
            {
                int idx = _dgvProviders.Rows.Add();
                FillProviderRow(_dgvProviders.Rows[idx], p);
            }
            _dgvProviders.ResumeLayout();
        }

        private static void FillProviderRow(DataGridViewRow row, DnsProvider p)
        {
            row.Tag = p;
            row.Cells["Enabled"].Value = p.Enabled;
            row.Cells["Name"].Value = p.Name;
            row.Cells["Ipv4"].Value = p.Ipv4Text();
            row.Cells["Ipv6"].Value = p.Ipv6Text();
            row.Cells["Category"].Value = p.CategoryText();
            row.Cells["Result"].Value = p.LastTestSummary ?? "";
            row.Cells["Score"].Value = p.LastTestedAt != null ? p.LastScore.ToString() : "";
            row.Cells["Latency"].Value = p.LastTestedAt != null && p.LastLatencyMs > 0 ? p.LastLatencyMs.ToString() : "";
            row.Cells["Tested"].Value = p.LastTestedAt ?? "";
            row.Cells["Notes"].Value = p.Notes ?? "";

            Color back = Color.White;
            string s = p.LastTestSummary ?? "";
            if (s.StartsWith("Tốt")) back = Color.FromArgb(225, 245, 225);
            else if (s.StartsWith("Một phần") || s.StartsWith("DNS OK")) back = Color.FromArgb(255, 245, 215);
            else if (s.Length > 0) back = Color.FromArgb(250, 225, 225);
            row.DefaultCellStyle.BackColor = back;
            row.DefaultCellStyle.ForeColor = p.Enabled ? Color.Black : Color.Gray;
        }

        private void UpdateProviderRow(DnsProvider p)
        {
            foreach (DataGridViewRow row in _dgvProviders.Rows)
            {
                var rp = row.Tag as DnsProvider;
                if (rp != null && rp.Id == p.Id)
                {
                    FillProviderRow(row, p);
                    return;
                }
            }
        }

        private DnsProvider SelectedProvider()
        {
            if (_dgvProviders.CurrentRow == null) return null;
            return _dgvProviders.CurrentRow.Tag as DnsProvider;
        }

        private void FillDiscoveredGrid(List<ProviderTestResult> results)
        {
            _dgvDiscovered.SuspendLayout();
            _dgvDiscovered.Rows.Clear();
            foreach (var r in ProviderTester.Rank(results))
            {
                int idx = _dgvDiscovered.Rows.Add();
                var row = _dgvDiscovered.Rows[idx];
                row.Tag = r.Provider;
                row.Cells["Pick"].Value = r.AllOk;
                row.Cells["Name"].Value = r.Provider.Name;
                row.Cells["Ip"].Value = r.Provider.HasIpv4() ? r.Provider.Ipv4Primary : r.Provider.Ipv6Primary;
                row.Cells["Status"].Value = r.StatusText() + " (" + r.HttpsOkCount + "/" + r.TotalDomains + ")";
                row.Cells["Score"].Value = r.Score.ToString();
                row.Cells["Latency"].Value = r.AvgTotalMs > 0 ? Math.Round(r.AvgTotalMs).ToString() : "";
                row.Cells["Notes"].Value = r.Provider.Notes ?? "";
                row.DefaultCellStyle.BackColor = r.AllOk ? Color.FromArgb(225, 245, 225) : (r.AnyOk ? Color.FromArgb(255, 245, 215) : Color.FromArgb(250, 225, 225));
            }
            _dgvDiscovered.ResumeLayout();
        }

        private void RefreshHistory()
        {
            List<HistoryEntry> entries;
            try { entries = HistoryService.Load(); }
            catch (Exception ex)
            {
                LogError("Không đọc được lịch sử: " + ex.Message);
                return;
            }
            _dgvHistory.SuspendLayout();
            _dgvHistory.Rows.Clear();
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i];
                int idx = _dgvHistory.Rows.Add(e.Time, e.Action, e.Adapter, e.Detail, e.Result);
                if (e.Result != null && e.Result.StartsWith("Lỗi")) _dgvHistory.Rows[idx].DefaultCellStyle.ForeColor = ColorErr;
            }
            _dgvHistory.ResumeLayout();
        }

        private void UpdateBackupStatus()
        {
            UI(() =>
            {
                string what = "";
                try
                {
                    var s = SessionService.Load();
                    if (SessionService.HasPending(s, _config)) what = SessionService.DescribePending(s, _config);
                }
                catch (Exception) { }
                if (what.Length == 0)
                {
                    _lblBackup.Text = "Chưa có thay đổi nào cần thu hồi.";
                    _lblBackup.ForeColor = Color.DimGray;
                }
                else
                {
                    _lblBackup.Text = what.Replace(Environment.NewLine, " | ") + (_config.KeepChangesOnExit ? " — GIỮ khi thoát app." : " — sẽ tự khôi phục khi thoát app.");
                    _lblBackup.ForeColor = ColorWarn;
                }
            });
        }

        private void TrackCreatedProfile(string name, bool auto)
        {
            _config.VpnProfilesCreatedByApp.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            _config.VpnProfilesCreatedByApp.Add(new VpnProfileRecord { Name = name, Auto = auto, CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });
            SaveConfig();
        }

        private void UntrackCreatedProfile(string name)
        {
            _config.VpnProfilesCreatedByApp.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            SaveConfig();
        }

        private void SaveConfig()
        {
            try { ProviderCatalog.SaveConfig(_config); }
            catch (Exception ex) { LogError("Không lưu được cấu hình: " + ex.Message); }
        }

        // =====================================================================
        //  Tiện ích giao diện (an toàn luồng)
        // =====================================================================

        private void UI(Action action)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(action); }
                catch (Exception) { }
            }
            else action();
        }

        private void Notice(string text, NoticeKind kind)
        {
            UI(() =>
            {
                _lblNotice.Text = text;
                switch (kind)
                {
                    case NoticeKind.Success:
                        _lblNotice.BackColor = Color.FromArgb(215, 245, 215);
                        _lblNotice.ForeColor = Color.FromArgb(0, 90, 0);
                        break;
                    case NoticeKind.Warning:
                        _lblNotice.BackColor = Color.FromArgb(255, 240, 200);
                        _lblNotice.ForeColor = Color.FromArgb(140, 80, 0);
                        break;
                    case NoticeKind.Error:
                        _lblNotice.BackColor = Color.FromArgb(250, 220, 220);
                        _lblNotice.ForeColor = Color.FromArgb(150, 0, 0);
                        break;
                    default:
                        _lblNotice.BackColor = Color.FromArgb(225, 238, 250);
                        _lblNotice.ForeColor = Color.FromArgb(20, 60, 110);
                        break;
                }
            });
        }

        private void SetCommand(string text)
        {
            UI(() => { _lblCommand.Text = "Lệnh đang chạy: " + text; });
        }

        private void SetProgress(int done, int total, string text)
        {
            UI(() =>
            {
                _progress.Style = ProgressBarStyle.Continuous;
                _progress.Maximum = Math.Max(1, total);
                _progress.Value = Math.Min(_progress.Maximum, Math.Max(0, done));
                _lblProgress.Text = text;
            });
        }

        private void SetMarquee(string text)
        {
            UI(() =>
            {
                _progress.Style = ProgressBarStyle.Marquee;
                _lblProgress.Text = text;
            });
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            UI(() =>
            {
                var buttons = new[]
                {
                    _btnCheck, _btnApply, _btnRecheck, _btnRevert, _btnBest, _btnRefreshAdapters, _btnOpenData,
                    _btnTestAll, _btnDiscover, _btnAddCustom, _btnToggle, _btnRemove, _btnResetCatalog, _btnAddDiscovered,
                    _btnVpnRefresh, _btnVpnAdd, _btnVpnRemove, _btnVpnIp, _btnVpnConnect, _btnVpnConnectCheck, _btnVpnDisconnect,
                    _btnVpnGateFind, _btnVpnGateUse, _btnResetSettings, _btnExportConfig, _btnImportConfig, _btnDiag, _btnCheckUpdate
                };
                foreach (var b in buttons) b.Enabled = !busy;
                if (_lblNatT != null) UpdateNatTLabel();
                if (_lblVpnCreds != null) UpdateVpnCredsLabel();
                if (_lblUpdateStatus != null) UpdateUpdateStatus();
                _cmbProviders.Enabled = !busy;
                _btnStop.Enabled = busy;
                _btnStop2.Enabled = busy;
                _btnStop3.Enabled = busy;
                UseWaitCursor = busy;
                if (!busy) UpdateVpnButtons();
            });
        }

        private void Log(string msg) { LogWith(msg, Color.Black); }
        private void LogOk(string msg) { LogWith(msg, ColorOk); }
        private void LogWarn(string msg) { LogWith(msg, ColorWarn); }
        private void LogError(string msg) { LogWith(msg, ColorErr); }

        private void LogWith(string msg, Color color)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + msg;
            lock (_fileLogLock)
            {
                try { File.AppendAllText(AppPaths.LogFile, DateTime.Now.ToString("yyyy-MM-dd ") + line + Environment.NewLine, Encoding.UTF8); }
                catch (Exception) { }
            }
            UI(() =>
            {
                if (_rtbLog.TextLength > 300000) _rtbLog.Clear();
                _rtbLog.SelectionStart = _rtbLog.TextLength;
                _rtbLog.SelectionLength = 0;
                _rtbLog.SelectionColor = color;
                _rtbLog.AppendText(line + Environment.NewLine);
                _rtbLog.SelectionColor = _rtbLog.ForeColor;
                _rtbLog.ScrollToCaret();
            });
        }

        private async Task RunOperation(string title, Func<CancellationToken, Task> work)
        {
            if (_busy)
            {
                Notice("Đang có thao tác khác chạy. Hãy chờ hoặc bấm Dừng.", NoticeKind.Warning);
                return;
            }
            _cts = new CancellationTokenSource();
            var cts = _cts;
            SetBusy(true);
            SetCommand(title);
            Log("=== " + title + " ===");
            try
            {
                await work(cts.Token);
            }
            catch (OperationCanceledException)
            {
                LogWarn("Đã dừng theo yêu cầu.");
                Notice("Đã dừng thao tác.", NoticeKind.Warning);
            }
            catch (Exception ex)
            {
                LogError("Lỗi: " + ex.Message);
                Notice("Lỗi: " + ex.Message, NoticeKind.Error);
            }
            finally
            {
                SetBusy(false);
                SetCommand("(không có)");
                UI(() =>
                {
                    _progress.Style = ProgressBarStyle.Continuous;
                    _progress.Value = 0;
                    _lblProgress.Text = "";
                });
                _cts = null;
                cts.Dispose();
            }
        }

        private static string FirstError(IEnumerable<CommandResult> results)
        {
            var bad = results.FirstOrDefault(r => !r.Ok);
            return bad != null ? (string.IsNullOrEmpty(bad.Output) ? bad.Command : bad.Output) : "?";
        }

        private static string FirstDomainError(ProviderTestResult r)
        {
            if (!string.IsNullOrEmpty(r.Error)) return r.Error;
            var d = r.Domains.FirstOrDefault(x => !x.Ok);
            if (d == null) return "";
            return d.Host + ": " + (!d.DnsOk ? "DNS " + (d.DnsError ?? "lỗi") : "HTTPS " + (d.HttpsError ?? "lỗi"));
        }

        // =====================================================================
        //  Các thao tác lõi (gọi trong RunOperation)
        // =====================================================================

        private async Task<List<DomainCheckResult>> CheckSteamCoreAsync(string title, CancellationToken ct)
        {
            SetCommand(title);
            Notice("Đang kiểm tra kết nối tới Steam bằng DNS hiện tại của hệ thống…", NoticeKind.Info);
            UI(() => _lvResults.Items.Clear());
            int total = SteamChecker.Domains.Length;
            int done = 0;
            SetProgress(0, total, "0/" + total);

            var tasks = SteamChecker.Domains.Select(host => Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var r = SteamChecker.CheckViaSystem(host, _config.DnsTimeoutMs, _config.HttpsTimeoutMs);
                int d = Interlocked.Increment(ref done);
                UI(() => AddResultRow(r));
                SetProgress(d, total, d + "/" + total + " – " + host);
                if (r.Ok) LogOk(r.Summary()); else LogError(r.Summary());
                return r;
            }, ct)).ToList();

            var results = (await Task.WhenAll(tasks)).ToList();
            int ok = results.Count(r => r.Ok);
            if (ok == total) Notice("Steam: " + ok + "/" + total + " tên miền kết nối tốt (DNS + HTTPS).", NoticeKind.Success);
            else if (ok == 0) Notice("Steam: không kết nối được (0/" + total + "). Hãy thử đổi DNS hoặc dùng \"Chọn nhanh DNS tốt nhất\".", NoticeKind.Error);
            else Notice("Steam: " + ok + "/" + total + " OK – một số tên miền đang lỗi. Hãy thử đổi DNS.", NoticeKind.Warning);

            try
            {
                HistoryService.Add("Kiểm tra Steam", "(DNS hệ thống)",
                    string.Join("; ", results.Select(r => r.Host + "=" + (r.Ok ? "OK" : "LỖI"))), ok + "/" + total + " OK");
                UI(RefreshHistory);
            }
            catch (Exception ex) { LogWarn("Không ghi được lịch sử: " + ex.Message); }
            return results;
        }

        private void AddResultRow(DomainCheckResult r)
        {
            var item = new ListViewItem(r.Host);
            item.SubItems.Add(r.DnsText());
            item.SubItems.Add(string.Join(", ", r.Ips.Take(3)));
            item.SubItems.Add(r.HttpsText());
            item.SubItems.Add(r.DnsOk ? Math.Round(r.TotalMs).ToString() : "-");
            item.SubItems.Add(r.CertSubject ?? "");
            item.ForeColor = r.Ok ? ColorOk : ColorErr;
            _lvResults.Items.Add(item);
        }

        private async Task<bool> ApplyCoreAsync(DnsProvider p, List<AdapterInfo> adapters, CancellationToken ct)
        {
            SetCommand("Áp dụng " + p.Name);
            Notice("Đang áp dụng " + p.Name + " cho " + adapters.Count + " card mạng…", NoticeKind.Info);
            SetMarquee("Đang chạy netsh…");
            bool allOk = true;
            await Task.Run(() =>
            {
                foreach (var a in adapters)
                {
                    ct.ThrowIfCancellationRequested();
                    bool capturedNow;
                    var original = BackupService.EnsureCaptured(a, out capturedNow);
                    Log((capturedNow ? "Đã sao lưu DNS gốc của " : "Dùng bản sao lưu DNS gốc có sẵn của ") + a.Name + ": " + original.Describe());
                    if (capturedNow && original.Ipv4Static && original.Ipv4Servers.Count > 0)
                    {
                        // Máy có thể đã được đặt DNS công cộng từ trước: cảnh báo để người dùng biết "gốc" là gì.
                        var match = _config.Providers.FirstOrDefault(x => x.HasIpv4() && string.Equals(x.Ipv4Primary, original.Ipv4Servers[0], StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                            LogWarn("⚠ DNS gốc của " + a.Name + " đang là " + match.Name + " (đặt tĩnh từ trước). Thu hồi sẽ trả về cấu hình này, không phải DNS của ISP.");
                    }

                    var outcome = DnsApplier.ApplyProviderSafely(a, p, original, Log);
                    string detail = p.Name + " | IPv4: " + p.Ipv4Text() + " | IPv6: "
                        + (p.HasIpv6() ? p.Ipv6Text() : "xoá (không dùng DNS IPv6 của ISP)") + " | Gốc: " + original.Describe();
                    if (outcome.Ok)
                    {
                        HistoryService.Add("Áp dụng DNS", a.Name, detail, "Thành công (đã xác minh)");
                        LogOk("✔ " + a.Name + ": đã đặt DNS " + p.Name + " (IPv4 + IPv6), xác minh khớp");
                    }
                    else
                    {
                        allOk = false;
                        string res = outcome.RollbackOk
                            ? "Lỗi: " + outcome.Error + " – đã hoàn tác về cấu hình gốc"
                            : "Lỗi: " + outcome.Error + " – hoàn tác cũng lỗi, giữ bản sao lưu";
                        HistoryService.Add("Áp dụng DNS", a.Name, detail, res);
                        LogError("✖ " + a.Name + ": " + res);
                        if (outcome.RollbackOk) BackupService.Remove(a.Id); // card đã về đúng cấu hình gốc
                    }
                }
                DnsApplier.FlushDns(Log);
            }, ct);

            _config.LastProviderId = p.Id;
            SaveConfig();
            UI(RefreshAdapters);
            UI(RefreshHistory);
            UpdateBackupStatus();
            if (allOk)
            {
                Notice("Đã áp dụng " + p.Name + ". Đang kiểm tra lại…", NoticeKind.Success);
                LogWarn("Lưu ý: nếu Steam đang mở, hãy khởi động lại Steam để dùng DNS mới.");
            }
            else Notice("Áp dụng " + p.Name + " gặp lỗi ở một số card. Xem nhật ký.", NoticeKind.Warning);
            return allOk;
        }

        /// <summary>Thu hồi toàn bộ: ngắt VPN do app kết nối, trả DNS gốc, xoá cấu hình VPN Gate do app tạo.</summary>
        private async Task RevertCoreAsync(CancellationToken ct)
        {
            SetCommand("Thu hồi thay đổi");
            SetMarquee("Đang khôi phục…");
            var session = SessionService.Load();
            if (!SessionService.HasPending(session, _config))
            {
                Notice("Không có thay đổi nào để thu hồi.", NoticeKind.Info);
                return;
            }
            Notice("Đang khôi phục cấu hình gốc (DNS, VPN)…", NoticeKind.Info);
            RecoveryReport rep = null;
            await Task.Run(() => { rep = RecoveryService.RestoreAll(session, _config, true, Log); }, ct);

            UI(RefreshAdapters);
            UI(RefreshHistory);
            UpdateBackupStatus();
            if (_vpnLoadedOnce)
            {
                try { await VpnRefreshCoreAsync(ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { LogWarn("Không làm mới được danh sách VPN: " + ex.Message); }
            }
            var missing = rep.DnsItems.Where(i => i.Missing).Select(i => i.State).ToList();
            if (missing.Count > 0) HandleMissingBackups(missing);
            if (rep.AllOk && missing.Count == 0) Notice("Đã khôi phục: " + rep.Summary() + ".", NoticeKind.Success);
            else if (rep.AllOk) Notice("Đã khôi phục (" + rep.Summary() + "); " + missing.Count + " card không có trong hệ thống.", NoticeKind.Warning);
            else Notice("Khôi phục chưa trọn vẹn: " + rep.Summary() + ". Xem nhật ký.", NoticeKind.Warning);
        }

        /// <summary>Card trong bản sao lưu không còn hiện diện: hỏi người dùng có bỏ bản sao lưu đó không.</summary>
        private void HandleMissingBackups(List<AdapterDnsState> missing)
        {
            string names = string.Join(", ", missing.Select(m => m.AdapterName));
            var r = MessageBox.Show(this,
                "Các card sau không có trong hệ thống nên chưa khôi phục được: " + names + ".\n\n" +
                "• Nếu card chỉ đang bị TẮT: chọn No, bật card lên rồi bấm Thu hồi lại.\n" +
                "• Nếu card đã bị GỠ khỏi máy: chọn Yes để xoá bản sao lưu của card đó.",
                "Card mạng không tồn tại", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
            foreach (var m in missing)
            {
                BackupService.Remove(m.AdapterId);
                HistoryService.Add("Xoá sao lưu", m.AdapterName, "Card không còn trong hệ thống; bỏ bản sao lưu: " + m.Describe(), "Đã xoá");
                Log("Đã xoá bản sao lưu của " + m.AdapterName);
            }
            UpdateBackupStatus();
            RefreshHistory();
        }

        private async Task<List<ProviderTestResult>> TestProvidersAsync(List<DnsProvider> providers, CancellationToken ct)
        {
            int total = providers.Count;
            int done = 0;
            SetProgress(0, total, "0/" + total);
            var opt = TestOptions.FromConfig(_config);
            var results = await ProviderTester.TestManyAsync(providers, opt, r =>
            {
                int d = Interlocked.Increment(ref done);
                ProviderTester.ApplyToProvider(r);
                SetProgress(d, total, d + "/" + total + " – " + r.Provider.Name);
                if (r.AllOk) LogOk(r.Summary());
                else if (r.AnyOk) LogWarn(r.Summary() + " – " + FirstDomainError(r));
                else LogError(r.Summary() + " – " + FirstDomainError(r));
                UI(() => UpdateProviderRow(r.Provider));
            }, ct);
            return results;
        }

        /// <summary>Lọc: tắt DNS lỗi (nếu bật tuỳ chọn), bật DNS bổ sung có sẵn nếu hoạt động tốt (khi autoEnableGood).</summary>
        private void ApplyFilters(List<ProviderTestResult> results, List<string> disabled, List<string> enabled, bool autoEnableGood)
        {
            foreach (var r in results)
            {
                var p = r.Provider;
                if (!_config.Providers.Contains(p)) continue;
                if (!r.AnyOk && _config.AutoDisableFailed && p.Enabled)
                {
                    p.Enabled = false;
                    disabled.Add(p.Name);
                }
                else if (autoEnableGood && r.AllOk && !p.Enabled && p.BuiltIn && p.Category == "extra")
                {
                    p.Enabled = true;
                    enabled.Add(p.Name);
                }
            }
            if (disabled.Count > 0) LogWarn("Đã tự động tắt " + disabled.Count + " DNS lỗi: " + string.Join(", ", disabled));
            if (enabled.Count > 0) LogOk("Đã tự động bật " + enabled.Count + " DNS bổ sung hoạt động tốt: " + string.Join(", ", enabled));
        }

        private async Task BestCoreAsync(List<AdapterInfo> adapters, CancellationToken ct)
        {
            var providers = _config.Providers.Where(p => p.Enabled).ToList();
            if (providers.Count == 0) throw new InvalidOperationException("Không có DNS nào được bật trong danh sách.");
            SetCommand("Đang đo " + providers.Count + " DNS");
            Notice("Đang kiểm tra " + providers.Count + " DNS với " + SteamChecker.Domains.Length + " tên miền Steam…", NoticeKind.Info);

            var results = await TestProvidersAsync(providers, ct);
            var ranked = ProviderTester.Rank(results);
            Log("-- Xếp hạng (HTTPS OK, DNS OK, độ trễ) --");
            int i = 1;
            foreach (var r in ranked.Take(5)) Log("  " + (i++) + ". " + r.Summary());

            var disabled = new List<string>();
            var enabledList = new List<string>();
            ApplyFilters(results, disabled, enabledList, false);
            SaveConfig();
            UI(() => { RefreshProvidersGrid(); RefreshProvidersCombo(); });
            WriteReport(results, null, disabled, enabledList, null);

            var best = ranked.FirstOrDefault();
            if (best == null || !best.AnyOk)
            {
                Notice("Không có DNS nào kết nối được Steam. Có thể bị chặn ở tầng khác (SNI/IP) – xem nhật ký.", NoticeKind.Error);
                return;
            }
            if (!best.AllOk) LogWarn("DNS tốt nhất chỉ đạt " + best.HttpsOkCount + "/" + best.TotalDomains + " tên miền.");
            Log("Chọn: " + best.Provider.Name + " – HTTPS " + best.HttpsOkCount + "/" + best.TotalDomains + ", trễ TB " + Math.Round(best.AvgTotalMs) + " ms");
            UI(() => SelectProviderInCombo(best.Provider.Id));

            ct.ThrowIfCancellationRequested();
            await ApplyCoreAsync(best.Provider, adapters, ct);
            await CheckSteamCoreAsync("Kiểm tra lại sau khi đổi DNS", ct);
        }

        private void WriteReport(List<ProviderTestResult> catalog, List<ProviderTestResult> discovered, List<string> disabled, List<string> enabled, string fetchError)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== BÁO CÁO KIỂM TRA DNS – " + DateTime.Now.ToString("dd/MM/yyyy HH:mm") + " ===");
            sb.AppendLine("Tên miền Steam: " + string.Join(", ", SteamChecker.Domains));
            sb.AppendLine();

            var ranked = ProviderTester.Rank(catalog);
            var good = ranked.Where(r => r.AllOk).ToList();
            var partial = ranked.Where(r => !r.AllOk && r.AnyOk).ToList();
            var bad = ranked.Where(r => !r.AnyOk).ToList();
            sb.AppendLine("DANH SÁCH DNS: " + catalog.Count + " – tốt " + good.Count + ", một phần " + partial.Count + ", lỗi " + bad.Count);
            int i = 1;
            foreach (var r in good) sb.AppendLine("  " + (i++) + ". " + Line(r));
            if (partial.Count > 0)
            {
                sb.AppendLine("  -- Một phần --");
                foreach (var r in partial) sb.AppendLine("  " + Line(r) + " | " + FirstDomainError(r));
            }
            if (bad.Count > 0)
            {
                sb.AppendLine("  -- Lỗi --");
                foreach (var r in bad) sb.AppendLine("  " + r.Provider.Name + ": " + FirstDomainError(r));
            }
            if (disabled.Count > 0) sb.AppendLine("Đã tự động tắt (bỏ khỏi lựa chọn): " + string.Join(", ", disabled));
            if (enabled.Count > 0) sb.AppendLine("Đã tự động bật thêm (hoạt động tốt): " + string.Join(", ", enabled));

            ProviderTestResult bestDisc = null;
            if (discovered != null)
            {
                sb.AppendLine();
                sb.AppendLine("TÌM TRÊN INTERNET (" + DiscoveryService.SourceName + ", quốc gia " + _config.DiscoveryCountry + "): " + discovered.Count + " máy chủ mới");
                if (fetchError != null) sb.AppendLine("  Không tải được danh sách: " + fetchError);
                var dr = ProviderTester.Rank(discovered);
                var dgood = dr.Where(r => r.AllOk).ToList();
                bestDisc = dgood.FirstOrDefault();
                sb.AppendLine("  Hoạt động tốt với Steam: " + dgood.Count);
                foreach (var r in dgood.Take(15)) sb.AppendLine("  + " + Line(r));
                sb.AppendLine("  Lỗi / một phần: " + (discovered.Count - dgood.Count) + " (đã lọc, không đề xuất)");
                if (dgood.Count > 0) sb.AppendLine("  → Tích chọn trong bảng rồi bấm \"Thêm DNS đã chọn vào danh sách\" để dùng.");
            }

            sb.AppendLine();
            var best = ranked.FirstOrDefault(r => r.AnyOk);
            if (best != null)
            {
                sb.AppendLine("ĐỀ XUẤT: " + best.Provider.Name + " – HTTPS " + best.HttpsOkCount + "/" + best.TotalDomains
                    + ", trễ TB " + Math.Round(best.AvgTotalMs) + " ms" + Ipv6Note(best));
            }
            else sb.AppendLine("ĐỀ XUẤT: không có DNS nào kết nối được Steam – có thể bị chặn ở tầng khác (SNI/IP). Hãy xem nhật ký.");
            if (bestDisc != null && (best == null || bestDisc.AvgTotalMs < best.AvgTotalMs))
                sb.AppendLine("  (DNS tìm thấy nhanh hơn: " + bestDisc.Provider.Name + " " + Math.Round(bestDisc.AvgTotalMs) + " ms – cân nhắc thêm vào danh sách)");

            UI(() => { _rtbReport.Text = sb.ToString(); });
            Log("Đã cập nhật báo cáo ở tab \"Danh sách DNS\".");
        }

        private static string Ipv6Note(ProviderTestResult r)
        {
            if (!r.Ipv6Tested) return "";
            return r.Ipv6Reachable ? ", IPv6 OK" : ", IPv6 không phản hồi";
        }

        private static string Line(ProviderTestResult r)
        {
            string ip = r.Provider.HasIpv4() ? r.Provider.Ipv4Primary : r.Provider.Ipv6Primary;
            return r.Provider.Name + " [" + ip + "] – HTTPS " + r.HttpsOkCount + "/" + r.TotalDomains + ", trễ TB " + Math.Round(r.AvgTotalMs) + " ms" + Ipv6Note(r);
        }

        // =====================================================================
        //  Sự kiện: Bảng điều khiển
        // =====================================================================

        private async void BtnCheck_Click(object sender, EventArgs e)
        {
            await RunOperation("Kiểm tra kết nối Steam", ct => CheckSteamCoreAsync("Kiểm tra kết nối Steam hiện tại", ct));
        }

        private async void BtnRecheck_Click(object sender, EventArgs e)
        {
            await RunOperation("Kiểm tra lại kết nối Steam", ct => CheckSteamCoreAsync("Kiểm tra lại kết nối Steam", ct));
        }

        private async void BtnApply_Click(object sender, EventArgs e)
        {
            var p = _cmbProviders.SelectedItem as DnsProvider;
            if (p == null)
            {
                Notice("Hãy chọn một DNS trong danh sách.", NoticeKind.Warning);
                return;
            }
            var adapters = GetCheckedAdapters();
            if (adapters.Count == 0)
            {
                Notice("Hãy tích chọn ít nhất một card mạng.", NoticeKind.Warning);
                return;
            }
            string msg = "Áp dụng \"" + p.Name + "\" cho:\n  • " + string.Join("\n  • ", adapters.Select(a => a.Name))
                + "\n\nIPv4: " + p.Ipv4Text()
                + "\nIPv6: " + (p.HasIpv6() ? p.Ipv6Text() : "không có → sẽ xoá DNS IPv6 để Windows không dùng DNS IPv6 của ISP")
                + "\n\nCấu hình hiện tại sẽ được sao lưu để có thể thu hồi. Tiếp tục?";
            if (MessageBox.Show(this, msg, "Xác nhận đổi DNS", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            await RunOperation("Áp dụng " + p.Name, async ct =>
            {
                await ApplyCoreAsync(p, adapters, ct);
                await CheckSteamCoreAsync("Kiểm tra lại sau khi đổi DNS", ct);
            });
        }

        private async void BtnRevert_Click(object sender, EventArgs e)
        {
            var session = SessionService.Load();
            if (!SessionService.HasPending(session, _config))
            {
                Notice("Không có thay đổi nào để thu hồi.", NoticeKind.Info);
                return;
            }
            string msg = "Khôi phục cấu hình gốc:\n\n" + SessionService.DescribePending(session, _config) + "\n\nTiếp tục?";
            if (MessageBox.Show(this, msg, "Xác nhận thu hồi", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            await RunOperation("Thu hồi thay đổi", async ct =>
            {
                await RevertCoreAsync(ct);
                await CheckSteamCoreAsync("Kiểm tra sau khi thu hồi", ct);
            });
        }

        private async void BtnBest_Click(object sender, EventArgs e)
        {
            var adapters = GetCheckedAdapters();
            if (adapters.Count == 0)
            {
                Notice("Hãy tích chọn ít nhất một card mạng.", NoticeKind.Warning);
                return;
            }
            int n = _config.Providers.Count(p => p.Enabled);
            if (n == 0)
            {
                Notice("Không có DNS nào được bật. Vào tab \"Danh sách DNS\" để bật.", NoticeKind.Warning);
                return;
            }
            string msg = "Sẽ đo " + n + " DNS đang bật với " + SteamChecker.Domains.Length + " tên miền Steam, chọn DNS tốt nhất và áp dụng ngay cho:\n  • "
                + string.Join("\n  • ", adapters.Select(a => a.Name)) + "\n\nCấu hình hiện tại sẽ được sao lưu để có thể thu hồi. Tiếp tục?";
            if (MessageBox.Show(this, msg, "Chọn nhanh DNS tốt nhất", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            await RunOperation("Chọn nhanh DNS tốt nhất", ct => BestCoreAsync(adapters, ct));
        }

        private void BtnStop_Click(object sender, EventArgs e)
        {
            var cts = _cts;
            if (cts != null)
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
                LogWarn("Đang dừng…");
            }
        }

        private void BtnOpenData_Click(object sender, EventArgs e)
        {
            AppPaths.EnsureDataDir();
            try { Process.Start("explorer.exe", "\"" + AppPaths.DataDir + "\""); }
            catch (Exception ex) { LogError("Không mở được thư mục: " + ex.Message); }
        }

        // =====================================================================
        //  Sự kiện: Lịch sử
        // =====================================================================

        private void BtnExportHistory_Click(object sender, EventArgs e)
        {
            using (var dlg = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = "SteamConnectToolkit-history-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".csv",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    HistoryService.ExportCsv(dlg.FileName);
                    Log("Đã xuất lịch sử: " + dlg.FileName);
                }
                catch (Exception ex) { LogError("Xuất lịch sử lỗi: " + ex.Message); }
            }
        }

        private void BtnClearHistory_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, "Xoá toàn bộ lịch sử? (Bản sao lưu DNS gốc không bị ảnh hưởng)", "Xoá lịch sử", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                HistoryService.Clear();
                RefreshHistory();
                Log("Đã xoá lịch sử.");
            }
            catch (Exception ex) { LogError("Không xoá được lịch sử: " + ex.Message); }
        }

        // =====================================================================
        //  Sự kiện: Danh sách DNS
        // =====================================================================

        private async void BtnTestAll_Click(object sender, EventArgs e)
        {
            await RunOperation("Kiểm tra tất cả DNS", async ct =>
            {
                var all = _config.Providers.ToList();
                Notice("Đang kiểm tra " + all.Count + " DNS với " + SteamChecker.Domains.Length + " tên miền Steam…", NoticeKind.Info);
                var results = await TestProvidersAsync(all, ct);
                var disabled = new List<string>();
                var enabled = new List<string>();
                ApplyFilters(results, disabled, enabled, false);
                SaveConfig();
                UI(() => { RefreshProvidersGrid(); RefreshProvidersCombo(); });
                WriteReport(results, null, disabled, enabled, null);
                var best = ProviderTester.Rank(results).FirstOrDefault();
                if (best != null && best.AnyOk)
                    Notice("Xong. Tốt nhất: " + best.Provider.Name + " (HTTPS " + best.HttpsOkCount + "/" + best.TotalDomains + ", " + Math.Round(best.AvgTotalMs) + " ms). Xem báo cáo bên dưới.", NoticeKind.Success);
                else
                    Notice("Xong. Không có DNS nào kết nối được Steam.", NoticeKind.Error);
            });
        }

        private async void BtnDiscover_Click(object sender, EventArgs e)
        {
            await RunOperation("Tự động tìm, lọc và báo cáo DNS", async ct =>
            {
                // Bước 1: kiểm tra danh sách có sẵn, lọc lỗi, bật DNS bổ sung tốt
                var all = _config.Providers.ToList();
                Notice("Bước 1/3: kiểm tra " + all.Count + " DNS trong danh sách…", NoticeKind.Info);
                var results = await TestProvidersAsync(all, ct);
                var disabled = new List<string>();
                var enabled = new List<string>();
                ApplyFilters(results, disabled, enabled, true);
                SaveConfig();
                UI(() => { RefreshProvidersGrid(); RefreshProvidersCombo(); });

                // Bước 2: tải danh sách DNS công cộng theo quốc gia
                ct.ThrowIfCancellationRequested();
                string cc = string.IsNullOrWhiteSpace(_config.DiscoveryCountry) ? "VN" : _config.DiscoveryCountry.Trim();
                Notice("Bước 2/3: tải danh sách DNS công cộng (" + cc + ") từ " + DiscoveryService.SourceName + "…", NoticeKind.Info);
                SetMarquee("Đang tải danh sách…");
                var candidates = new List<DnsProvider>();
                string fetchError = null;
                bool ipv6 = NetworkAdapterService.AnyGlobalIpv6();
                try
                {
                    var found = await Task.Run(() => DiscoveryService.FetchCountry(cc, _config.DiscoveryMinReliability, _config.DiscoveryMaxServers, ipv6, Log), ct);
                    var known = new HashSet<string>(_config.Providers.SelectMany(p => p.GetIpv4Servers().Concat(p.GetIpv6Servers())));
                    foreach (var s in found)
                    {
                        if (known.Contains(s.Ip)) continue;
                        candidates.Add(DiscoveryService.ToProvider(s));
                    }
                    Log("Tìm thấy " + found.Count + " máy chủ (độ tin cậy ≥ " + _config.DiscoveryMinReliability + "), " + candidates.Count + " máy chủ chưa có trong danh sách.");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    fetchError = ex.Message;
                    LogWarn("Không tải được danh sách từ internet: " + ex.Message);
                }

                // Bước 3: kiểm tra các ứng viên với Steam
                var discResults = new List<ProviderTestResult>();
                if (candidates.Count > 0)
                {
                    Notice("Bước 3/3: kiểm tra " + candidates.Count + " DNS mới tìm thấy với Steam…", NoticeKind.Info);
                    discResults = await TestProvidersAsync(candidates, ct);
                }
                UI(() => FillDiscoveredGrid(discResults));
                WriteReport(results, discResults, disabled, enabled, fetchError);

                int goodNew = discResults.Count(r => r.AllOk);
                var best = ProviderTester.Rank(results).FirstOrDefault();
                string summary = "Xong. Danh sách: " + results.Count(r => r.AllOk) + " tốt / " + results.Count + "; tắt " + disabled.Count + ", bật thêm " + enabled.Count
                    + ". Internet: " + goodNew + " DNS mới hoạt động tốt" + (fetchError != null ? " (không tải được danh sách)" : "") + ".";
                Notice(summary, best != null && best.AnyOk ? NoticeKind.Success : NoticeKind.Warning);
            });
        }

        private void DgvProviders_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (_busy || e.RowIndex < 0 || e.ColumnIndex != _dgvProviders.Columns["Enabled"].Index) return;
            var p = _dgvProviders.Rows[e.RowIndex].Tag as DnsProvider;
            if (p == null) return;
            p.Enabled = !p.Enabled;
            FillProviderRow(_dgvProviders.Rows[e.RowIndex], p);
            SaveConfig();
            RefreshProvidersCombo();
        }

        private void DgvDiscovered_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (_busy || e.RowIndex < 0 || e.ColumnIndex != _dgvDiscovered.Columns["Pick"].Index) return;
            var cell = _dgvDiscovered.Rows[e.RowIndex].Cells["Pick"];
            bool cur = cell.Value is bool && (bool)cell.Value;
            cell.Value = !cur;
        }

        private void BtnAddCustom_Click(object sender, EventArgs e)
        {
            using (var dlg = new AddProviderForm())
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Result == null) return;
                _config.Providers.Add(dlg.Result);
                SaveConfig();
                RefreshProvidersGrid();
                RefreshProvidersCombo();
                Log("Đã thêm DNS: " + dlg.Result.DisplayName());
            }
        }

        private void BtnToggle_Click(object sender, EventArgs e)
        {
            var p = SelectedProvider();
            if (p == null)
            {
                Notice("Hãy chọn một dòng trong danh sách DNS.", NoticeKind.Warning);
                return;
            }
            p.Enabled = !p.Enabled;
            UpdateProviderRow(p);
            SaveConfig();
            RefreshProvidersCombo();
            Log((p.Enabled ? "Đã bật " : "Đã tắt ") + p.Name);
        }

        private void BtnRemove_Click(object sender, EventArgs e)
        {
            var p = SelectedProvider();
            if (p == null)
            {
                Notice("Hãy chọn một dòng trong danh sách DNS.", NoticeKind.Warning);
                return;
            }
            if (p.BuiltIn)
            {
                p.Enabled = false;
                UpdateProviderRow(p);
                SaveConfig();
                RefreshProvidersCombo();
                Log("Đã bỏ " + p.Name + " khỏi lựa chọn (DNS có sẵn chỉ bị tắt, có thể bật lại).");
                return;
            }
            if (MessageBox.Show(this, "Xoá \"" + p.Name + "\" khỏi danh sách?", "Xoá DNS", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _config.Providers.Remove(p);
            SaveConfig();
            RefreshProvidersGrid();
            RefreshProvidersCombo();
            Log("Đã xoá DNS: " + p.Name);
        }

        private void BtnReset_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, "Bật lại toàn bộ DNS chính và tắt DNS bổ sung? DNS tự thêm được giữ nguyên.", "Khôi phục mặc định", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            ProviderCatalog.ResetBuiltIns(_config);
            SaveConfig();
            RefreshProvidersGrid();
            RefreshProvidersCombo();
            Log("Đã khôi phục danh sách DNS mặc định.");
        }

        private void BtnAddDiscovered_Click(object sender, EventArgs e)
        {
            int added = 0;
            foreach (DataGridViewRow row in _dgvDiscovered.Rows)
            {
                var v = row.Cells["Pick"].Value;
                bool pick = v is bool && (bool)v;
                if (!pick) continue;
                var p = row.Tag as DnsProvider;
                if (p == null) continue;
                if (_config.Providers.Any(x => x.Id == p.Id)) continue;
                _config.Providers.Add(p);
                row.Cells["Pick"].Value = false;
                added++;
                Log("Đã thêm DNS tìm thấy: " + p.DisplayName());
            }
            if (added == 0)
            {
                Notice("Chưa tích chọn DNS nào trong bảng tìm thấy.", NoticeKind.Warning);
                return;
            }
            SaveConfig();
            RefreshProvidersGrid();
            RefreshProvidersCombo();
            Notice("Đã thêm " + added + " DNS vào danh sách.", NoticeKind.Success);
        }

        // =====================================================================
        //  VPN
        // =====================================================================

        private VpnProfile SelectedVpn()
        {
            if (_dgvVpn.CurrentRow == null) return null;
            return _dgvVpn.CurrentRow.Tag as VpnProfile;
        }

        private void FillVpnGrid()
        {
            var sel = SelectedVpn();
            string selectedName = sel != null ? sel.Name : _config.LastVpnProfile;
            _fillingVpn = true;
            try
            {
                _dgvVpn.SuspendLayout();
                _dgvVpn.Rows.Clear();
                foreach (var p in _vpnProfiles)
                {
                    int idx = _dgvVpn.Rows.Add();
                    FillVpnRow(_dgvVpn.Rows[idx], p);
                }
                _dgvVpn.ResumeLayout();
                foreach (DataGridViewRow row in _dgvVpn.Rows)
                {
                    var p = row.Tag as VpnProfile;
                    if (p != null && string.Equals(p.Name, selectedName, StringComparison.OrdinalIgnoreCase))
                    {
                        _dgvVpn.CurrentCell = row.Cells[0];
                        row.Selected = true;
                        break;
                    }
                }
            }
            finally
            {
                _fillingVpn = false;
            }
            LoadVpnCredentialFields();
            UpdateVpnButtons();
        }

        private static void FillVpnRow(DataGridViewRow row, VpnProfile p)
        {
            row.Tag = p;
            row.Cells["Name"].Value = p.Name;
            row.Cells["Server"].Value = p.Server ?? "";
            row.Cells["Type"].Value = p.TunnelTypeText();
            row.Cells["Status"].Value = p.StatusText();
            row.Cells["Auth"].Value = p.Authentication ?? "";
            row.Cells["Scope"].Value = p.ScopeText();
            row.Cells["Split"].Value = p.SplitTunneling ? "Có" : "Không";
            row.Cells["Remember"].Value = p.RememberCredential ? "Có" : "Không";
            row.Cells["Enc"].Value = p.Encryption ?? "";
            row.DefaultCellStyle.BackColor = p.IsConnected ? Color.FromArgb(225, 245, 225) : Color.White;
            row.DefaultCellStyle.ForeColor = Color.Black;
        }

        private void UpdateVpnRow(VpnProfile p)
        {
            foreach (DataGridViewRow row in _dgvVpn.Rows)
            {
                var rp = row.Tag as VpnProfile;
                if (rp != null && string.Equals(rp.Name, p.Name, StringComparison.OrdinalIgnoreCase))
                {
                    FillVpnRow(row, p);
                    return;
                }
            }
        }

        private void LoadVpnCredentialFields()
        {
            var p = SelectedVpn();
            if (p == null) return;
            var c = CredentialStore.Find(_config, p.Name);
            _txtVpnUser.Text = c != null ? (c.UserName ?? "") : "";
            string pw = c != null && !string.IsNullOrEmpty(c.PasswordProtected) ? CredentialStore.Unprotect(c.PasswordProtected) : "";
            _txtVpnPass.Text = pw ?? "";
            _chkVpnRemember.Checked = c != null && !string.IsNullOrEmpty(c.PasswordProtected);
        }

        private void UpdateVpnButtons()
        {
            if (_dgvVpn == null) return;
            var p = SelectedVpn();
            bool has = p != null && !_busy;
            _btnVpnConnect.Enabled = has && !p.IsConnected;
            _btnVpnConnectCheck.Enabled = has && !p.IsConnected;
            _btnVpnDisconnect.Enabled = has && p.IsConnected;
            _btnVpnRemove.Enabled = has;
            if (p == null)
                _lblVpnStatus.Text = _vpnProfiles.Count == 0 ? "Chưa có cấu hình VPN. Bấm \"Thêm cấu hình VPN…\"." : "Chọn một cấu hình VPN trong bảng.";
            else if (p.IsConnected)
                _lblVpnStatus.Text = "\"" + p.Name + "\" đang kết nối. Bấm \"Ngắt kết nối\" để thoát VPN.";
            else
                _lblVpnStatus.Text = "Để trống tên đăng nhập và mật khẩu nếu Windows đã nhớ tài khoản của cấu hình này.";
        }

        /// <summary>Cập nhật trạng thái kết nối theo card mạng (nhanh, không gọi PowerShell).</summary>
        private void RefreshVpnStatusQuick()
        {
            if (_busy || _vpnProfiles.Count == 0 || IsDisposed) return;
            bool changed = false;
            foreach (var p in _vpnProfiles)
            {
                bool up = VpnService.IsInterfaceUp(p.Name);
                if (up != p.IsConnected)
                {
                    p.IsConnected = up;
                    UpdateVpnRow(p);
                    changed = true;
                    Log("VPN " + p.Name + ": " + (up ? "đã kết nối" : "đã ngắt") + " (phát hiện qua card mạng).");
                }
            }
            if (changed)
            {
                UpdateVpnButtons();
                RefreshAdapters();
            }
        }

        private async Task VpnRefreshCoreAsync(CancellationToken ct)
        {
            SetCommand("Đọc danh sách VPN của Windows");
            SetMarquee("Đang chạy PowerShell Get-VpnConnection…");
            var list = await Task.Run(() => VpnService.ListProfiles(Log), ct);
            _vpnProfiles = list;
            _vpnLoadedOnce = true;
            UI(FillVpnGrid);
            var connected = list.Where(p => p.IsConnected).Select(p => p.Name).ToList();
            Log("VPN: " + list.Count + " cấu hình trong Windows, " + connected.Count + " đang kết nối.");
            if (list.Count == 0) Notice("Chưa có cấu hình VPN nào trong Windows. Bấm \"Thêm cấu hình VPN…\" để tạo.", NoticeKind.Info);
            else Notice("VPN: " + list.Count + " cấu hình" + (connected.Count > 0 ? ", đang kết nối: " + string.Join(", ", connected) : "") + ".",
                connected.Count > 0 ? NoticeKind.Success : NoticeKind.Info);
        }

        private async Task VpnConnectCoreAsync(VpnProfile p, string user, string pass, bool checkSteam, CancellationToken ct)
        {
            SetCommand("Kết nối VPN " + p.Name);
            Notice("Đang kết nối VPN \"" + p.Name + "\" (" + p.Server + ", " + p.TunnelTypeText() + ")…", NoticeKind.Info);
            SetMarquee("Đang quay số VPN… (có thể mất tới 1 phút)");

            string ipBefore = null;
            await Task.Run(() =>
            {
                string err;
                ipBefore = VpnService.GetPublicIp(5000, out err);
                if (ipBefore != null) Log("IP công cộng trước khi kết nối: " + ipBefore);
                else LogWarn("Không lấy được IP công cộng trước khi kết nối: " + err);
            }, ct);

            var result = await Task.Run(() => VpnService.Connect(p, user, pass, Log), ct);
            HistoryService.Add("Kết nối VPN", p.Name, p.Server + " (" + p.TunnelTypeText() + ")", result.Ok ? "Thành công" : "Lỗi: " + result.Message);
            UI(RefreshHistory);
            if (!result.Ok)
            {
                LogError("✖ VPN " + p.Name + ": " + result.Message);
                Notice("Không kết nối được VPN " + p.Name + " – " + result.Message, NoticeKind.Error);
                return;
            }
            LogOk("✔ VPN " + p.Name + ": " + result.Message);
            _config.LastVpnProfile = p.Name;
            SaveConfig();
            SessionService.AddVpnConnected(p.Name);
            p.IsConnected = true;
            UI(() => { UpdateVpnRow(p); UpdateVpnButtons(); RefreshAdapters(); });
            UpdateBackupStatus();

            string ipAfter = null;
            await Task.Run(() =>
            {
                string err;
                ipAfter = VpnService.GetPublicIp(8000, out err);
                if (ipAfter != null)
                {
                    string cc = VpnService.GetIpCountry(ipAfter, 5000);
                    string text = ipAfter + (cc != null ? " (" + cc + ")" : "");
                    Log("IP công cộng sau khi kết nối: " + text);
                    UI(() => { _lblVpnIp.Text = "IP công cộng: " + text; });
                }
                else LogWarn("Không lấy được IP công cộng sau khi kết nối: " + err);
            }, ct);
            if (ipBefore != null && ipAfter != null && ipBefore == ipAfter)
                LogWarn("⚠ IP công cộng không đổi – lưu lượng có thể chưa đi qua VPN (cấu hình bật split tunneling?).");

            Notice("Đã kết nối VPN " + p.Name + (ipAfter != null ? " – IP công cộng " + ipAfter : "") + ".", NoticeKind.Success);
            if (checkSteam) await CheckSteamCoreAsync("Kiểm tra Steam qua VPN " + p.Name, ct);
        }

        private async Task VpnDisconnectCoreAsync(VpnProfile p, CancellationToken ct)
        {
            SetCommand("Ngắt VPN " + p.Name);
            SetMarquee("Đang ngắt kết nối…");
            var r = await Task.Run(() => VpnService.DisconnectReliable(p.Name, Log), ct);
            bool stillUp = VpnService.IsInterfaceUp(p.Name);
            bool ok = r.Ok && !stillUp;
            HistoryService.Add("Ngắt VPN", p.Name, p.Server ?? "", ok ? "Thành công" : "Lỗi: " + (r.Output ?? ""));
            UI(RefreshHistory);
            if (ok) SessionService.RemoveVpnConnected(p.Name);
            p.IsConnected = stillUp;
            UI(() => { UpdateVpnRow(p); UpdateVpnButtons(); RefreshAdapters(); });
            UpdateBackupStatus();
            if (ok)
            {
                LogOk("✔ Đã ngắt VPN " + p.Name);
                Notice("Đã ngắt VPN " + p.Name + ".", NoticeKind.Success);
            }
            else
            {
                LogError("✖ Không ngắt được VPN " + p.Name + ": " + r.Output);
                Notice("Không ngắt được VPN " + p.Name + ". Xem nhật ký.", NoticeKind.Error);
            }
        }

        private async void BtnVpnRefresh_Click(object sender, EventArgs e)
        {
            await RunOperation("Làm mới danh sách VPN", VpnRefreshCoreAsync);
        }

        private async void BtnVpnIp_Click(object sender, EventArgs e)
        {
            await RunOperation("Kiểm tra IP công cộng", async ct =>
            {
                SetMarquee("Đang hỏi IP công cộng…");
                string text = null;
                await Task.Run(() =>
                {
                    string err;
                    string ip = VpnService.GetPublicIp(8000, out err);
                    if (ip == null) throw new InvalidOperationException("Không lấy được IP công cộng: " + err);
                    string cc = VpnService.GetIpCountry(ip, 5000);
                    text = ip + (cc != null ? " (" + cc + ")" : "");
                }, ct);
                UI(() => { _lblVpnIp.Text = "IP công cộng: " + text; });
                Log("IP công cộng hiện tại: " + text);
                Notice("IP công cộng hiện tại: " + text, NoticeKind.Info);
            });
        }

        private async void BtnVpnConnect_Click(object sender, EventArgs e)
        {
            await StartVpnConnectAsync(false);
        }

        private async void BtnVpnConnectCheck_Click(object sender, EventArgs e)
        {
            await StartVpnConnectAsync(true);
        }

        private async Task StartVpnConnectAsync(bool checkSteam)
        {
            var p = SelectedVpn();
            if (p == null)
            {
                Notice("Hãy chọn một cấu hình VPN trong bảng (hoặc thêm mới).", NoticeKind.Warning);
                return;
            }
            if (p.IsConnected)
            {
                Notice("VPN \"" + p.Name + "\" đang kết nối.", NoticeKind.Info);
                return;
            }
            string user = _txtVpnUser.Text.Trim();
            string pass = _txtVpnPass.Text;
            CredentialStore.Save(_config, p.Name, user, pass, _chkVpnRemember.Checked);
            SaveConfig();
            await RunOperation("Kết nối VPN " + p.Name, ct => VpnConnectCoreAsync(p, user, pass, checkSteam, ct));
        }

        private async void BtnVpnDisconnect_Click(object sender, EventArgs e)
        {
            var p = SelectedVpn();
            if (p == null) return;
            await RunOperation("Ngắt VPN " + p.Name, ct => VpnDisconnectCoreAsync(p, ct));
        }

        private async void BtnVpnAdd_Click(object sender, EventArgs e)
        {
            VpnProfileSpec spec;
            using (var dlg = new AddVpnForm())
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Result == null) return;
                spec = dlg.Result;
            }
            await RunOperation("Thêm cấu hình VPN " + spec.Name, async ct =>
            {
                SetMarquee("Đang chạy Add-VpnConnection…");
                var r = await Task.Run(() => VpnService.AddProfile(spec, Log), ct);
                HistoryService.Add("Thêm VPN", spec.Name, spec.Server + " (" + spec.TunnelType + (spec.AllUsers ? ", mọi người dùng" : "") + ")",
                    r.Ok ? "Thành công" : "Lỗi: " + r.Output);
                UI(RefreshHistory);
                if (!r.Ok)
                {
                    LogError("✖ Không tạo được cấu hình VPN: " + r.Output);
                    Notice("Không tạo được cấu hình VPN \"" + spec.Name + "\". Xem nhật ký.", NoticeKind.Error);
                    return;
                }
                LogOk("✔ Đã tạo cấu hình VPN " + spec.Name + " (" + spec.Server + ", " + spec.TunnelType + ")");
                _config.LastVpnProfile = spec.Name;
                TrackCreatedProfile(spec.Name, false);
                await VpnRefreshCoreAsync(ct);
                Notice("Đã tạo cấu hình VPN \"" + spec.Name + "\". Nhập tên đăng nhập/mật khẩu rồi bấm Kết nối.", NoticeKind.Success);
            });
        }

        private async void BtnVpnRemove_Click(object sender, EventArgs e)
        {
            var p = SelectedVpn();
            if (p == null)
            {
                Notice("Hãy chọn một cấu hình VPN trong bảng.", NoticeKind.Warning);
                return;
            }
            string msg = "Xoá cấu hình VPN \"" + p.Name + "\" (" + p.Server + ") khỏi Windows?" + (p.IsConnected ? "\n\nVPN đang kết nối sẽ bị ngắt trước." : "");
            if (MessageBox.Show(this, msg, "Xoá cấu hình VPN", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            await RunOperation("Xoá cấu hình VPN " + p.Name, async ct =>
            {
                if (p.IsConnected) await VpnDisconnectCoreAsync(p, ct);
                SetMarquee("Đang chạy Remove-VpnConnection…");
                var r = await Task.Run(() => VpnService.RemoveProfile(p.Name, p.AllUsers, Log), ct);
                HistoryService.Add("Xoá VPN", p.Name, p.Server ?? "", r.Ok ? "Thành công" : "Lỗi: " + r.Output);
                UI(RefreshHistory);
                if (!r.Ok)
                {
                    LogError("✖ Không xoá được cấu hình VPN: " + r.Output);
                    Notice("Không xoá được cấu hình VPN \"" + p.Name + "\". Xem nhật ký.", NoticeKind.Error);
                    return;
                }
                CredentialStore.Remove(_config, p.Name);
                if (string.Equals(_config.LastVpnProfile, p.Name, StringComparison.OrdinalIgnoreCase)) _config.LastVpnProfile = null;
                UntrackCreatedProfile(p.Name);
                UpdateBackupStatus();
                LogOk("✔ Đã xoá cấu hình VPN " + p.Name);
                await VpnRefreshCoreAsync(ct);
                Notice("Đã xoá cấu hình VPN \"" + p.Name + "\".", NoticeKind.Success);
            });
        }

        // =====================================================================
        //  VPN Gate (VPN miễn phí)
        // =====================================================================

        private VpnGateServer SelectedVpnGate()
        {
            if (_dgvVpnGate.CurrentRow == null) return null;
            return _dgvVpnGate.CurrentRow.Tag as VpnGateServer;
        }

        private void FillVpnGateGrid()
        {
            _dgvVpnGate.SuspendLayout();
            _dgvVpnGate.Rows.Clear();
            foreach (var s in _vpnGateServers)
            {
                int idx = _dgvVpnGate.Rows.Add();
                var row = _dgvVpnGate.Rows[idx];
                row.Tag = s;
                row.Cells["CC"].Value = s.CountryCode;
                row.Cells["Host"].Value = s.HostName + (s.IsOfficial ? " ★" : "");
                row.Cells["Ip"].Value = s.Ip;
                row.Cells["Proto"].Value = s.ProtocolsText();
                row.Cells["Probe"].Value = s.ProbeMs >= 0 ? Math.Round(s.ProbeMs).ToString() : (s.ProbeMs == -2 ? "không" : "");
                row.Cells["Ping"].Value = s.PingMs >= 0 ? s.PingMs.ToString() : "";
                row.Cells["Speed"].Value = s.SpeedMbps > 0 ? Math.Round(s.SpeedMbps).ToString() : "";
                row.Cells["Sessions"].Value = s.Sessions.ToString();
                row.Cells["Score"].Value = s.Score.ToString("N0");
                row.Cells["Log"].Value = (s.LogPolicy ?? "") + (s.IsOfficial ? " – máy chủ chính thức" : "");
                row.DefaultCellStyle.BackColor = s.ProbeMs >= 0 ? Color.FromArgb(225, 245, 225) : (s.ProbeMs == -2 ? Color.FromArgb(250, 225, 225) : Color.White);
            }
            _dgvVpnGate.ResumeLayout();
            if (_dgvVpnGate.Rows.Count > 0)
            {
                _dgvVpnGate.CurrentCell = _dgvVpnGate.Rows[0].Cells[0];
                _dgvVpnGate.Rows[0].Selected = true;
            }
        }

        private async Task VpnGateFindCoreAsync(CancellationToken ct)
        {
            bool preferSstp = _chkVpnGatePreferSstp.Checked;
            SetCommand("Tải danh sách VPN Gate");
            Notice("Đang tải danh sách máy chủ VPN Gate…", NoticeKind.Info);
            SetMarquee("Đang tải vpngate.net…");
            var servers = await Task.Run(() => VpnGateService.Fetch(20000, Log), ct);
            if (servers.Count == 0) throw new InvalidOperationException("Không đọc được danh sách máy chủ VPN Gate (trang bị chặn hoặc đổi cấu trúc).");
            var usable = VpnGateService.Rank(servers, preferSstp);
            if (usable.Count == 0) throw new InvalidOperationException("Không có máy chủ VPN Gate nào hỗ trợ SSTP/L2TP lúc này.");

            var toProbe = usable.Take(30).ToList();
            int done = 0;
            SetCommand("Đo " + toProbe.Count + " máy chủ VPN Gate");
            SetProgress(0, toProbe.Count, "0/" + toProbe.Count);
            await Task.Run(() =>
            {
                var tasks = toProbe.Select(s => Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();
                    VpnGateService.Probe(s, 3000);
                    int d = Interlocked.Increment(ref done);
                    SetProgress(d, toProbe.Count, d + "/" + toProbe.Count + " – " + s.HostName);
                }, ct)).ToArray();
                try { Task.WaitAll(tasks); }
                catch (AggregateException) { if (ct.IsCancellationRequested) throw new OperationCanceledException(ct); }
            }, ct);

            _vpnGateServers = VpnGateService.Rank(usable, preferSstp);
            UI(FillVpnGateGrid);
            int reach = toProbe.Count(s => s.Reachable);
            Log("VPN Gate: " + usable.Count + " máy chủ dùng được với VPN Windows (SSTP/L2TP); đã đo " + toProbe.Count + ", tới được " + reach + ".");
            var best = _vpnGateServers.FirstOrDefault();
            if (best != null && best.Reachable)
                Notice("VPN Gate: đề xuất " + best.CountryCode + " " + best.HostName + " (" + best.ProtocolsText() + ", " + Math.Round(best.ProbeMs) + " ms). Chọn dòng rồi bấm \"Tạo cấu hình, kết nối và kiểm tra Steam\".", NoticeKind.Success);
            else
                Notice("VPN Gate: không máy chủ nào tới được từ mạng này (cổng 443 bị chặn?).", NoticeKind.Warning);
        }

        private async void BtnVpnGateFind_Click(object sender, EventArgs e)
        {
            await RunOperation("Tìm máy chủ VPN Gate", VpnGateFindCoreAsync);
        }

        private async void BtnVpnGateUse_Click(object sender, EventArgs e)
        {
            var s = SelectedVpnGate();
            if (s == null)
            {
                Notice("Hãy bấm \"Tìm máy chủ VPN Gate\" rồi chọn một máy chủ.", NoticeKind.Warning);
                return;
            }
            bool preferSstp = _chkVpnGatePreferSstp.Checked;
            var spec = VpnGateService.ToProfileSpec(s, preferSstp);
            if (spec == null)
            {
                Notice("Máy chủ này không hỗ trợ SSTP/L2TP (chỉ OpenVPN/SoftEther).", NoticeKind.Warning);
                return;
            }
            string msg = "Tạo cấu hình VPN Windows \"" + spec.Name + "\"\n  " + (spec.TunnelType == "Sstp" ? "SSTP" : "L2TP/IPsec") + " → " + spec.Server
                + "\nvà kết nối bằng tài khoản vpn / vpn, sau đó kiểm tra Steam.\n\n"
                + "Lưu ý: VPN Gate là dịch vụ công cộng miễn phí do tình nguyện viên vận hành, có ghi log; chỉ nên dùng để truy cập Steam, "
                + "không đăng nhập dịch vụ nhạy cảm (ngân hàng...). Tiếp tục?";
            if (MessageBox.Show(this, msg, "VPN Gate", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            if (spec.TunnelType == "L2tp")
            {
                var rule = VpnService.GetL2tpNatRule();
                if (rule != 2)
                {
                    var r = MessageBox.Show(this,
                        "L2TP/IPsec khi máy nằm sau router NAT cần khoá registry AssumeUDPEncapsulationContextOnSendRule = 2 (hiện " + (rule.HasValue ? rule.Value.ToString() : "chưa đặt") + ").\n\n"
                        + "Yes: đặt ngay và khởi động lại dịch vụ IPsec (có thể vẫn cần khởi động lại Windows).\nNo: cứ thử kết nối.\nCancel: huỷ.",
                        "L2TP sau NAT", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (r == DialogResult.Cancel) return;
                    if (r == DialogResult.Yes)
                    {
                        var set = VpnService.SetL2tpNatRule(Log);
                        if (set.Ok) LogOk("✔ Đã đặt khoá NAT-T cho L2TP; nếu vẫn lỗi 809 hãy khởi động lại Windows.");
                        else LogError("✖ Không đặt được khoá NAT-T: " + set.Output);
                    }
                }
            }

            await RunOperation("VPN Gate: " + spec.Name, async ct =>
            {
                var existing = _vpnProfiles.FirstOrDefault(p => string.Equals(p.Name, spec.Name, StringComparison.OrdinalIgnoreCase));
                if (existing != null && existing.IsConnected)
                {
                    Notice("VPN \"" + spec.Name + "\" đang kết nối.", NoticeKind.Info);
                    return;
                }
                if (existing == null || !string.Equals(existing.Server, spec.Server, StringComparison.OrdinalIgnoreCase))
                {
                    SetMarquee("Đang tạo cấu hình VPN…");
                    var r = await Task.Run(() => VpnService.AddProfile(spec, Log), ct);
                    HistoryService.Add("Thêm VPN", spec.Name, spec.Server + " (" + spec.TunnelType + ", VPN Gate)", r.Ok ? "Thành công" : "Lỗi: " + r.Output);
                    UI(RefreshHistory);
                    if (!r.Ok)
                    {
                        LogError("✖ Không tạo được cấu hình VPN: " + r.Output);
                        Notice("Không tạo được cấu hình VPN \"" + spec.Name + "\". Xem nhật ký.", NoticeKind.Error);
                        return;
                    }
                    LogOk("✔ Đã tạo cấu hình VPN " + spec.Name + " (" + spec.TunnelType + " → " + spec.Server + ")");
                    TrackCreatedProfile(spec.Name, true);
                    UpdateBackupStatus();
                    await VpnRefreshCoreAsync(ct);
                    existing = _vpnProfiles.FirstOrDefault(p => string.Equals(p.Name, spec.Name, StringComparison.OrdinalIgnoreCase));
                    if (existing == null) throw new InvalidOperationException("Cấu hình vừa tạo không xuất hiện trong danh sách VPN của Windows.");
                }
                CredentialStore.Save(_config, spec.Name, VpnGateService.Username, VpnGateService.Password, true);
                _config.LastVpnProfile = spec.Name;
                SaveConfig();
                UI(() =>
                {
                    FillVpnGrid();
                    _txtVpnUser.Text = VpnGateService.Username;
                    _txtVpnPass.Text = VpnGateService.Password;
                    _chkVpnRemember.Checked = true;
                });
                await VpnConnectCoreAsync(existing, VpnGateService.Username, VpnGateService.Password, true, ct);
            });
        }
    }
}
