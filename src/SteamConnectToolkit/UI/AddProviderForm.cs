using System;
using System.Drawing;
using System.Windows.Forms;
using SteamConnectToolkit.Models;

namespace SteamConnectToolkit.UI
{
    /// <summary>Hộp thoại thêm DNS thủ công.</summary>
    public class AddProviderForm : Form
    {
        private readonly TextBox _name = new TextBox();
        private readonly TextBox _v4a = new TextBox();
        private readonly TextBox _v4b = new TextBox();
        private readonly TextBox _v6a = new TextBox();
        private readonly TextBox _v6b = new TextBox();
        private readonly TextBox _notes = new TextBox();

        public DnsProvider Result { get; private set; }

        public AddProviderForm()
        {
            Text = "Thêm DNS thủ công";
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(460, 250);
            AutoScaleMode = AutoScaleMode.Font;

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 7, Padding = new Padding(12) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 6; i++) t.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            AddRow(t, 0, "Tên hiển thị:", _name);
            AddRow(t, 1, "IPv4 chính:", _v4a);
            AddRow(t, 2, "IPv4 phụ (tuỳ chọn):", _v4b);
            AddRow(t, 3, "IPv6 chính (tuỳ chọn):", _v6a);
            AddRow(t, 4, "IPv6 phụ (tuỳ chọn):", _v6b);
            AddRow(t, 5, "Ghi chú:", _notes);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var btnCancel = new Button { Text = "Huỷ", Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
            var btnOk = new Button { Text = "Thêm", Width = 90, Height = 30 };
            btnOk.Click += OkClick;
            buttons.Controls.Add(btnCancel);
            buttons.Controls.Add(btnOk);
            t.Controls.Add(buttons, 0, 6);
            t.SetColumnSpan(buttons, 2);

            Controls.Add(t);
            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        private static void AddRow(TableLayoutPanel t, int row, string label, TextBox box)
        {
            var l = new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            box.Dock = DockStyle.Fill;
            t.Controls.Add(l, 0, row);
            t.Controls.Add(box, 1, row);
        }

        private void OkClick(object sender, EventArgs e)
        {
            var p = new DnsProvider
            {
                Id = Services.ProviderCatalog.NewId("custom"),
                Name = _name.Text.Trim(),
                Ipv4Primary = _v4a.Text.Trim(),
                Ipv4Secondary = _v4b.Text.Trim(),
                Ipv6Primary = _v6a.Text.Trim(),
                Ipv6Secondary = _v6b.Text.Trim(),
                Notes = _notes.Text.Trim(),
                Category = "custom",
                Enabled = true,
                BuiltIn = false
            };
            string err = p.Validate();
            if (err != null)
            {
                MessageBox.Show(this, err, "Thiếu thông tin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Result = p;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
