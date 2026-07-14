using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using static RedfurSync.FissalTheme;

namespace RedfurSync
{
    internal sealed class RelayAssistantForm : Form
    {
        private readonly Func<string, Task<(bool ok, string message, string model)>> _ask;
        private readonly TextBox _prompt;
        private readonly TextBox _response;
        private readonly Label _status;
        private readonly Button _send;

        public RelayAssistantForm(Func<string, Task<(bool ok, string message, string model)>> ask)
        {
            _ask = ask;
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(520, 430);
            Size = new Size(620, 520);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = CBg;
            ForeColor = CText;
            Text = "Ask Fissal";

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18),
                ColumnCount = 1,
                RowCount = 7,
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            layout.Controls.Add(new Label
            {
                AutoSize = true,
                Text = "Fissal Relay Assistant",
                ForeColor = CGoldBrt,
                Font = Title(16f, 1f, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 6),
            });
            layout.Controls.Add(new Label
            {
                AutoSize = true,
                Text = "Ask about setup, connection state, sync progress, or Relay errors.",
                ForeColor = CTextSub,
                Font = SystemFonts.MessageBoxFont,
                Margin = new Padding(0, 0, 0, 8),
            });

            _prompt = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                MaxLength = 1200,
                ScrollBars = ScrollBars.Vertical,
                BackColor = CPanelBg,
                ForeColor = CText,
                Font = SystemFonts.MessageBoxFont,
                AccessibleName = "Question for Fissal",
            };
            layout.Controls.Add(_prompt);

            _send = new Button
            {
                AutoSize = true,
                Text = "Send",
                ForeColor = CGreen,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 8, 0, 8),
            };
            _send.FlatAppearance.BorderColor = CGreen;
            _send.Click += async (_, _) => await SendAsync();
            layout.Controls.Add(_send);

            _response = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(8, 8, 10),
                ForeColor = CText,
                Font = SystemFonts.MessageBoxFont,
                AccessibleName = "Fissal response",
            };
            layout.Controls.Add(_response);

            _status = new Label
            {
                AutoSize = true,
                ForeColor = CTextSub,
                Text = "Paired requests use the Relay Assistant route configured in Fissal Control.",
                Margin = new Padding(0, 8, 0, 4),
            };
            layout.Controls.Add(_status);

            var close = new Button { AutoSize = true, Text = "Close", DialogResult = DialogResult.Cancel };
            layout.Controls.Add(close);
            Controls.Add(layout);
            AcceptButton = _send;
            CancelButton = close;
            Shown += (_, _) => _prompt.Focus();
        }

        private async Task SendAsync()
        {
            if (string.IsNullOrWhiteSpace(_prompt.Text)) return;
            _send.Enabled = false;
            _prompt.Enabled = false;
            _status.Text = "Fissal is thinking...";
            try
            {
                var result = await _ask(_prompt.Text);
                _response.Text = result.message;
                _status.Text = result.ok
                    ? string.IsNullOrWhiteSpace(result.model) ? "Response received." : $"Response received from {result.model}."
                    : "Fissal could not answer this request.";
            }
            finally
            {
                _prompt.Enabled = true;
                _send.Enabled = true;
                _prompt.Focus();
            }
        }
    }
}