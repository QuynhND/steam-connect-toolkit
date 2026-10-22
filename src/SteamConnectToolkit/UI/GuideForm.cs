using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using SteamConnectToolkit.Services;

namespace SteamConnectToolkit.UI
{
    /// <summary>Hướng dẫn từng bước cho người dùng lần đầu. Mỗi trang có thể yêu cầu cửa sổ chính chuyển sang tab liên quan.</summary>
    public class GuideForm : Form
    {
        private readonly List<GuidePage> _pages = GuideContent.Pages();
        private readonly Action<int> _selectTab;
        private int _index;

        private readonly Label _lblTitle = new Label();
        private readonly Label _lblStep = new Label();
        private readonly FlowLayoutPanel _body = new FlowLayoutPanel();
        private const int BodyWidth = 630;
        private readonly CheckBox _chkShowOnStartup = new CheckBox();
        private readonly Button _btnPrev = new Button();
        private readonly Button _btnNext = new Button();
        private readonly Button _btnClose = new Button();

        /// <summary>Trạng thái ô "Hiện hướng dẫn này mỗi khi mở app" khi đóng.</summary>
        public bool ShowOnStartup { get { return _chkShowOnStartup.Checked; } }
        public int PageCount { get { return _pages.Count; } }
        public int CurrentIndex { get { return _index; } }

        public GuideForm(bool showOnStartup, Action<int> selectTab)
        {
            _selectTab = selectTab;
            Text = "Hướng dẫn sử dụng – Steam Connect Toolkit";
            Font = new Font("Segoe UI", 9.5f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(680, 470);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (Exception) { }

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(14) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

            var head = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            _lblTitle.Dock = DockStyle.Fill;
            _lblTitle.Font = new Font("Segoe UI", 14f, FontStyle.Bold);
            _lblTitle.ForeColor = Color.FromArgb(20, 60, 110);
            _lblTitle.TextAlign = ContentAlignment.MiddleLeft;
            _lblTitle.AutoEllipsis = true;
            _lblStep.Dock = DockStyle.Fill;
            _lblStep.TextAlign = ContentAlignment.MiddleRight;
            _lblStep.ForeColor = Color.DimGray;
            head.Controls.Add(_lblTitle, 0, 0);
            head.Controls.Add(_lblStep, 1, 0);

            _body.Dock = DockStyle.Fill;
            _body.FlowDirection = FlowDirection.TopDown;
            _body.WrapContents = false;
            _body.AutoScroll = true;
            _body.BackColor = Color.White;
            _body.Font = new Font("Segoe UI", 10f);
            _body.Padding = new Padding(8, 6, 8, 6);
            _body.Margin = new Padding(0, 6, 0, 6);
            _body.BorderStyle = BorderStyle.FixedSingle;

            var foot = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            foot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foot.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _chkShowOnStartup.Text = "Hiện hướng dẫn này mỗi khi mở app";
            _chkShowOnStartup.AutoSize = true;
            _chkShowOnStartup.Checked = showOnStartup;
            _chkShowOnStartup.Margin = new Padding(0, 10, 0, 0);
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0), WrapContents = false };
            _btnPrev.Text = "‹ Trước";
            _btnNext.Text = "Tiếp ›";
            _btnClose.Text = "Đóng";
            foreach (var b in new[] { _btnPrev, _btnNext, _btnClose })
            {
                b.Width = 100;
                b.Height = 32;
                b.Margin = new Padding(6, 2, 0, 2);
                buttons.Controls.Add(b);
            }
            _btnNext.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            _btnPrev.Click += (s, e) => GoTo(_index - 1);
            _btnNext.Click += (s, e) => { if (_index >= _pages.Count - 1) Close(); else GoTo(_index + 1); };
            _btnClose.Click += (s, e) => Close();
            foot.Controls.Add(_chkShowOnStartup, 0, 0);
            foot.Controls.Add(buttons, 1, 0);

            root.Controls.Add(head, 0, 0);
            root.Controls.Add(_body, 0, 1);
            root.Controls.Add(foot, 0, 2);
            Controls.Add(root);
            AcceptButton = _btnNext;
            CancelButton = _btnClose;
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Left) { GoTo(_index - 1); e.Handled = true; }
                else if (e.KeyCode == Keys.Right) { GoTo(_index + 1); e.Handled = true; }
            };
            GoTo(0);
        }

        public void GoTo(int index)
        {
            if (_pages.Count == 0) return;
            if (index < 0) index = 0;
            if (index > _pages.Count - 1) index = _pages.Count - 1;
            _index = index;
            var page = _pages[_index];
            _lblTitle.Text = page.Title;
            _lblStep.Text = "Bước " + (_index + 1) + "/" + _pages.Count;
            RenderBody(page.Body);
            _btnPrev.Enabled = _index > 0;
            _btnNext.Text = _index >= _pages.Count - 1 ? "Hoàn tất" : "Tiếp ›";
            if (_selectTab != null)
            {
                try { _selectTab(page.TabIndex); }
                catch (Exception) { }
            }
        }

        /// <summary>Định dạng nhẹ bằng các Label: dòng kết thúc bằng ':' (không phải gạch đầu dòng) in đậm; gạch đầu dòng thụt vào; dòng trống tạo khoảng cách.</summary>
        private void RenderBody(string text)
        {
            _body.SuspendLayout();
            _body.Controls.Clear();
            var normal = _body.Font;
            var bold = new Font(normal, FontStyle.Bold);
            int width = Math.Max(300, _body.ClientSize.Width - 36);
            if (width > BodyWidth + 40) width = BodyWidth + 40;
            foreach (var raw in (text ?? "").Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0)
                {
                    _body.Controls.Add(new Label { AutoSize = true, Text = " ", Height = 6, Margin = new Padding(0) });
                    continue;
                }
                bool bullet = line.StartsWith("•");
                bool heading = !bullet && line.EndsWith(":");
                var lbl = new Label
                {
                    AutoSize = true,
                    Text = line,
                    Font = heading ? bold : normal,
                    MaximumSize = new Size(width - (bullet ? 16 : 0), 0),
                    Margin = bullet ? new Padding(16, 1, 0, 3) : new Padding(0, 3, 0, 3),
                    ForeColor = heading ? Color.FromArgb(20, 60, 110) : Color.Black
                };
                _body.Controls.Add(lbl);
            }
            _body.ResumeLayout();
            _body.AutoScrollPosition = new Point(0, 0);
        }
    }
}
