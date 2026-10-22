using System;
using System.Drawing;
using System.Windows.Forms;
using SteamConnectToolkit.Models;

namespace SteamConnectToolkit.UI
{
    /// <summary>Hộp thoại tạo cấu hình VPN tích hợp Windows.</summary>
    public class AddVpnForm : Form
    {
        private readonly TextBox _name = new TextBox();
        private readonly TextBox _server = new TextBox();
        private readonly ComboBox _type = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox _psk = new TextBox { UseSystemPasswordChar = true };
        private readonly CheckBox _remember = new CheckBox { Text = "Cho Windows nhớ tài khoản sau lần kết nối đầu", Checked = true };
        private readonly CheckBox _allUsers = new CheckBox { Text = "Dùng chung cho mọi người dùng trên máy" };
        private readonly CheckBox _split = new CheckBox { Text = "Split tunneling (chỉ lưu lượng tới mạng VPN đi qua VPN – KHÔNG dùng cho Steam)" };
        private readonly Label _hint = new Label { ForeColor = Color.DimGray };

        private static readonly string[] TypeLabels = { "IKEv2 (khuyên dùng)", "L2TP/IPsec (Pre-shared key)", "SSTP (TCP 443)", "PPTP (cũ, kém an toàn)", "Tự động" };
        private static readonly string[] TypeValues = { "Ikev2", "L2tp", "Sstp", "Pptp", "Automatic" };

        public VpnProfileSpec Result { get; private set; }

        public AddVpnForm()
        {
            Text = "Thêm cấu hình VPN (Windows tích hợp)";
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 330);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 9, Padding = new Padding(12) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++) t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _type.Items.AddRange(TypeLabels);
            _type.SelectedIndex = 0;
            _type.SelectedIndexChanged += (s, e) => UpdateState();

            AddRow(t, 0, "Tên cấu hình:", _name);
            AddRow(t, 1, "Máy chủ VPN (tên miền/IP):", _server);
            AddRow(t, 2, "Giao thức:", _type);
            AddRow(t, 3, "Pre-shared key (L2TP):", _psk);
            t.Controls.Add(_remember, 0, 4); t.SetColumnSpan(_remember, 2);
            t.Controls.Add(_allUsers, 0, 5); t.SetColumnSpan(_allUsers, 2);
            t.Controls.Add(_split, 0, 6); t.SetColumnSpan(_split, 2);
            _hint.Dock = DockStyle.Fill;
            _hint.Text = "Tên đăng nhập/mật khẩu nhập ở tab VPN khi kết nối. IKEv2 dùng EAP-MSCHAPv2; nếu nhà cung cấp yêu cầu chứng chỉ CA, hãy cài chứng chỉ đó trước.";
            t.Controls.Add(_hint, 0, 7); t.SetColumnSpan(_hint, 2);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var btnCancel = new Button { Text = "Huỷ", Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
            var btnOk = new Button { Text = "Tạo", Width = 90, Height = 30 };
            btnOk.Click += OkClick;
            buttons.Controls.Add(btnCancel);
            buttons.Controls.Add(btnOk);
            t.Controls.Add(buttons, 0, 8);
            t.SetColumnSpan(buttons, 2);

            Controls.Add(t);
            AcceptButton = btnOk;
            CancelButton = btnCancel;
            foreach (var c in new CheckBox[] { _remember, _allUsers, _split }) c.AutoSize = true;
            UpdateState();
        }

        private void UpdateState()
        {
            _psk.Enabled = _type.SelectedIndex == 1;
        }

        private static void AddRow(TableLayoutPanel t, int row, string label, Control box)
        {
            var l = new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            box.Dock = DockStyle.Fill;
            t.Controls.Add(l, 0, row);
            t.Controls.Add(box, 1, row);
        }

        private void OkClick(object sender, EventArgs e)
        {
            var spec = new VpnProfileSpec
            {
                Name = _name.Text.Trim(),
                Server = _server.Text.Trim(),
                TunnelType = TypeValues[Math.Max(0, _type.SelectedIndex)],
                L2tpPsk = _type.SelectedIndex == 1 ? _psk.Text : "",
                RememberCredential = _remember.Checked,
                AllUsers = _allUsers.Checked,
                SplitTunneling = _split.Checked
            };
            string err = spec.Validate();
            if (err != null)
            {
                MessageBox.Show(this, err, "Thiếu thông tin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (spec.TunnelType == "L2tp" && string.IsNullOrEmpty(spec.L2tpPsk))
            {
                if (MessageBox.Show(this, "L2TP/IPsec thường cần Pre-shared key. Tạo cấu hình không có PSK (dùng chứng chỉ)?", "Thiếu PSK",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            }
            if (spec.SplitTunneling)
            {
                if (MessageBox.Show(this, "Bật split tunneling thì lưu lượng Steam sẽ KHÔNG đi qua VPN. Vẫn tiếp tục?", "Split tunneling",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }
            Result = spec;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
