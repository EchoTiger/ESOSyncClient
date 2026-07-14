using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using static RedfurSync.FissalTheme;

namespace RedfurSync
{
    internal sealed class RelayPairingForm : Form
    {
        private readonly TextBox _code;
        private readonly float _scale;

        public string PairingCode => _code.Text.Trim();

        public RelayPairingForm(string currentCode)
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = CBg;
            ForeColor = CText;
            Text = "Fissal Relay Pairing";
            Width = 460;
            Height = 290;

            var handle = Handle;
            _scale = GetScale(handle);
            Font = Body(10f, _scale);

            var title = new Label
            {
                AutoSize = true,
                Location = new Point(28, 24),
                ForeColor = CGoldBrt,
                Font = Title(15f, _scale, FontStyle.Bold),
                Text = "Connect Fissal Relay",
            };
            Controls.Add(title);

            var copy = new Label
            {
                AutoSize = false,
                Location = new Point(30, 68),
                Size = new Size(385, 48),
                ForeColor = CTextSub,
                Font = Body(10f, _scale),
                Text = "Open the Redfur dashboard, create a pairing code, then enter it below.\nNo API key is needed. The code expires after ten minutes.",
            };
            Controls.Add(copy);

            _code = new TextBox
            {
                Location = new Point(30, 132),
                Width = 185,
                Height = 36,
                MaxLength = 6,
                Text = currentCode,
                TextAlign = HorizontalAlignment.Center,
                Font = Title(16f, _scale, FontStyle.Bold),
                BackColor = CPanelBg,
                ForeColor = CGoldBrt,
                BorderStyle = BorderStyle.FixedSingle,
            };
            _code.KeyPress += (_, e) =>
            {
                if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true;
            };
            Controls.Add(_code);

            var dashboard = new LinkLabel
            {
                AutoSize = true,
                Location = new Point(235, 143),
                LinkColor = CGoldBrt,
                ActiveLinkColor = Color.White,
                VisitedLinkColor = CGoldMid,
                Font = Body(9.5f, _scale),
                Text = "Open Redfur dashboard",
                TabStop = true,
            };
            dashboard.Click += (_, _) => OpenDashboard();
            Controls.Add(dashboard);

            var cancel = MakeButton("Later", CTextSub, new Point(228, 205), DialogResult.Cancel);
            var pair = MakeButton("Pair Relay", CGreen, new Point(328, 205), DialogResult.OK);
            Controls.Add(cancel);
            Controls.Add(pair);
            AcceptButton = pair;
            CancelButton = cancel;
            Shown += (_, _) => _code.Focus();
        }

        public static bool ShowFor(AppConfig config)
        {
            using var form = new RelayPairingForm(config.PairingCode);
            if (form.ShowDialog() != DialogResult.OK || form.PairingCode.Length != 6) return false;
            config.PairingCode = form.PairingCode;
            config.Save();
            return true;
        }

        private Button MakeButton(string text, Color color, Point location, DialogResult result)
        {
            var button = new Button
            {
                Text = text,
                Location = location,
                Width = 92,
                Height = 34,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(28, color),
                ForeColor = color,
                Font = Body(9f, _scale, FontStyle.Bold),
                DialogResult = result,
                Cursor = Cursors.Hand,
            };
            button.FlatAppearance.BorderColor = color;
            button.FlatAppearance.BorderSize = 1;
            return button;
        }

        private static void OpenDashboard()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://redfur.ech-o.net/dev/?relay=1",
                UseShellExecute = true,
            });
        }
    }
}