using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using static RedfurSync.FissalTheme;

namespace RedfurSync
{
    internal sealed class RelayAssistantForm : Form
    {
        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int message, int wParam, int lParam);
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        private readonly Func<string, Task<(bool ok, string message, string model)>> _ask;
        private readonly Func<string> _getHarnessContext;
        private readonly FissalHarnessService _harnessService;
        private readonly FlowLayoutPanel _transcript;
        private readonly TextBox _prompt;
        private readonly Label _status;
        private Label _model = null!;
        private readonly Button _send;
        private CheckBox _harness = null!;
        private CheckBox _writePermissions = null!;
        private readonly float _scale;
        private readonly List<(string role, string text)> _history = new();

        public RelayAssistantForm(
            Func<string, Task<(bool ok, string message, string model)>> ask,
            Func<string> getHarnessContext)
        {
            _ask = ask;
            _getHarnessContext = getHarnessContext;
            _harnessService = new FissalHarnessService(AppConfig.Instance);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.None;
            MinimumSize = new Size(680, 560);
            Size = new Size(880, 720);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = CBg;
            ForeColor = CText;
            Text = "Ask Fissal - Relay Assistant";
            KeyPreview = true;
            _scale = IsHandleCreated ? GetScale(Handle) : GetSystemScale();

            var shell = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20),
                ColumnCount = 1,
                RowCount = 6,
                BackColor = CBg,
            };
            shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            shell.Controls.Add(BuildWindowBar());
            shell.Controls.Add(BuildHeader());
            shell.Controls.Add(BuildQuickActions());

            _transcript = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.FromArgb(11, 9, 5),
                Padding = new Padding(12),
                Margin = new Padding(0, 10, 0, 10),
                AccessibleName = "Conversation with Fissal",
            };
            _transcript.Resize += (_, _) => ResizeMessageCards();
            shell.Controls.Add(_transcript);

            var composer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = CPanelBg,
                Padding = new Padding(10),
                Margin = new Padding(0),
            };
            composer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            composer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _prompt = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                AcceptsReturn = true,
                MaxLength = 1200,
                ScrollBars = ScrollBars.Vertical,
                MinimumSize = new Size(0, 72),
                BackColor = Color.FromArgb(20, 16, 9),
                ForeColor = CText,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Body(9f, _scale),
                AccessibleName = "Message Fissal",
            };
            _prompt.KeyDown += PromptKeyDown;
            composer.Controls.Add(_prompt, 0, 0);

            _send = MakeButton("Send  >", CGreen);
            _send.MinimumSize = new Size(104, 72);
            _send.Margin = new Padding(10, 0, 0, 0);
            _send.AccessibleName = "Send message to Fissal";
            _send.Click += async (_, _) => await SendAsync();
            composer.Controls.Add(_send, 1, 0);
            shell.Controls.Add(composer);

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 8, 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _status = new Label
            {
                AutoSize = true,
                ForeColor = CTextSub,
                Font = Body(8f, _scale),
                Text = "Enter sends. Shift+Enter adds a new line.",
                AccessibleName = "Assistant status",
                Anchor = AnchorStyles.Left,
            };
            footer.Controls.Add(_status, 0, 0);
            var close = MakeButton("Close", CTextSub);
            close.DialogResult = DialogResult.Cancel;
            footer.Controls.Add(close, 1, 0);
            shell.Controls.Add(footer);

            Controls.Add(shell);
            CancelButton = close;
            Paint += (_, args) =>
            {
                using var border = new Pen(CBorder, 1);
                args.Graphics.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            };
            Shown += (_, _) =>
            {
                AddMessage(false, "Hello! I can help verify Relay setup, explain sync activity, and troubleshoot files. The **Fissal Harness** is an optional bridge that gives me local Relay diagnostics for the current conversation. It starts read-only; you can separately allow carefully limited Relay settings changes.");
                _prompt.Focus();
            };
        }

        protected override void WndProc(ref Message message)
        {
            const int WmNcHitTest = 0x0084;
            const int resizeGrip = 8;
            base.WndProc(ref message);
            if (message.Msg != WmNcHitTest || WindowState == FormWindowState.Maximized) return;

            var cursor = PointToClient(new Point(message.LParam.ToInt32()));
            var left = cursor.X <= resizeGrip;
            var right = cursor.X >= ClientSize.Width - resizeGrip;
            var top = cursor.Y <= resizeGrip;
            var bottom = cursor.Y >= ClientSize.Height - resizeGrip;
            if (left && top) message.Result = (IntPtr)13;
            else if (right && top) message.Result = (IntPtr)14;
            else if (left && bottom) message.Result = (IntPtr)16;
            else if (right && bottom) message.Result = (IntPtr)17;
            else if (left) message.Result = (IntPtr)10;
            else if (right) message.Result = (IntPtr)11;
            else if (top) message.Result = (IntPtr)12;
            else if (bottom) message.Result = (IntPtr)15;
        }

        private Control BuildWindowBar()
        {
            var bar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Height = 34,
                ColumnCount = 3,
                Margin = new Padding(0, 0, 0, 12),
                BackColor = Color.FromArgb(18, 14, 8),
            };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
            var title = new Label
            {
                Dock = DockStyle.Fill,
                Text = "  FISSAL // LOCAL RELAY TERMINAL",
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = CGoldMid,
                Font = Mono(8f, _scale, FontStyle.Bold),
            };
            title.MouseDown += DragWindow;
            bar.MouseDown += DragWindow;
            bar.Controls.Add(title, 0, 0);

            var minimize = MakeWindowButton("_", "Minimize Ask Fissal");
            minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
            bar.Controls.Add(minimize, 1, 0);
            var close = MakeWindowButton("X", "Close Ask Fissal");
            close.ForeColor = CBarFail;
            close.Click += (_, _) => Close();
            bar.Controls.Add(close, 2, 0);
            return bar;
        }

        private Button MakeWindowButton(string text, string accessibleName)
        {
            var button = new Button
            {
                Dock = DockStyle.Fill,
                Text = text,
                AccessibleName = accessibleName,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(18, 14, 8),
                ForeColor = CTextSub,
                Font = Mono(9f, _scale, FontStyle.Bold),
                TabStop = true,
                Margin = new Padding(0),
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = CBarBg;
            return button;
        }

        private void DragWindow(object? sender, MouseEventArgs eventArgs)
        {
            if (eventArgs.Button != MouseButtons.Left || WindowState == FormWindowState.Maximized) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, 0x2, 0);
        }

        private Control BuildHeader()
        {
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var brand = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
            brand.Controls.Add(new Label
            {
                AutoSize = true,
                Text = "ASK FISSAL",
                ForeColor = CGoldBrt,
                Font = Title(15f, _scale, FontStyle.Bold),
                Margin = new Padding(0),
                AccessibleName = "Ask Fissal header",
            });
            brand.Controls.Add(new Label
            {
                AutoSize = true,
                Text = "Relay support, diagnostics, and guided configuration",
                ForeColor = CTextSub,
                Font = Body(8.5f, _scale),
                Margin = new Padding(0, 3, 0, 0),
            });
            header.Controls.Add(brand, 0, 0);

            var controls = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Anchor = AnchorStyles.Right };
            _model = new Label
            {
                AutoSize = true,
                Text = "● READY",
                ForeColor = CGreen,
                Font = Mono(8f, _scale, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 4),
                AccessibleName = "Assistant model status",
            };
            _harness = new CheckBox
            {
                AutoSize = true,
                Text = "Enable Fissal Harness",
                ForeColor = CText,
                Font = Body(8f, _scale),
                FlatStyle = FlatStyle.Flat,
                Checked = AppConfig.Instance.FissalHarnessEnabled,
                AccessibleName = "Enable Fissal Harness diagnostics",
            };
            _harness.CheckedChanged += (_, _) =>
            {
                AppConfig.Instance.FissalHarnessEnabled = _harness.Checked;
                if (!_harness.Checked) _writePermissions.Checked = false;
                AppConfig.Instance.Save();
                _writePermissions.Enabled = _harness.Checked;
                _status.Text = _harness.Checked
                    ? "Harness enabled. Read-only Relay diagnostics will accompany requests."
                    : "Harness disabled. No local diagnostics will be shared.";
            };
            _writePermissions = new CheckBox
            {
                AutoSize = true,
                Text = "Allow Relay settings changes",
                ForeColor = CWarn,
                Font = Body(8f, _scale),
                FlatStyle = FlatStyle.Flat,
                Checked = AppConfig.Instance.FissalHarnessEnabled && AppConfig.Instance.FissalWritePermissions,
                Enabled = AppConfig.Instance.FissalHarnessEnabled,
                AccessibleName = "Allow Fissal to change approved Relay settings",
            };
            _writePermissions.CheckedChanged += (_, _) =>
            {
                AppConfig.Instance.FissalWritePermissions = _writePermissions.Checked;
                AppConfig.Instance.Save();
                _status.Text = _writePermissions.Checked
                    ? "Write permission enabled for approved Relay settings only. Changes still require confirmation."
                    : "Write permission disabled. Fissal cannot change local settings.";
            };
            controls.Controls.Add(_model);
            controls.Controls.Add(_harness);
            controls.Controls.Add(_writePermissions);
            header.Controls.Add(controls, 1, 0);

            var explanation = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(390, 0),
                Text = "Harness means a controlled connection to this Relay. Read mode shares file names, sizes, dates, sync state, and errors, never file contents. Write mode can only change approved Relay settings and creates a recovery backup.",
                ForeColor = CTextSub,
                Font = Body(7.5f, _scale),
                Margin = new Padding(0, 8, 0, 0),
            };
            controls.Controls.Add(explanation);
            return header;
        }

        private Control BuildQuickActions()
        {
            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = true,
                Margin = new Padding(0, 12, 0, 0),
            };
            AddQuickAction(actions, "Check my data files", "Check whether the Relay can see my ESO data files and explain anything missing.");
            AddQuickAction(actions, "Why is sync idle?", "Review my Relay state and tell me why no files may be syncing.");
            AddQuickAction(actions, "Explain recent activity", "Summarize my recent Relay sync activity and call out failures or stale data.");

            var clear = MakeButton("Clear chat", CTextSub);
            clear.Click += (_, _) =>
            {
                _transcript.Controls.Clear();
                _history.Clear();
                AddMessage(false, "Fresh page. What should we inspect?");
            };
            actions.Controls.Add(clear);
            return actions;
        }

        private void AddQuickAction(Control parent, string label, string prompt)
        {
            var button = MakeButton(label, CGoldMid);
            button.Click += (_, _) =>
            {
                _prompt.Text = prompt;
                _prompt.Focus();
                _prompt.SelectionStart = _prompt.TextLength;
            };
            parent.Controls.Add(button);
        }

        private Button MakeButton(string text, Color accent)
        {
            var button = new Button
            {
                AutoSize = true,
                Text = text,
                ForeColor = accent,
                BackColor = CBtnBg,
                FlatStyle = FlatStyle.Flat,
                Font = Body(8.5f, _scale, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Padding = new Padding(8, 4, 8, 4),
                Margin = new Padding(0, 0, 8, 0),
            };
            button.FlatAppearance.BorderColor = CBtnBorder;
            button.FlatAppearance.MouseOverBackColor = CBarBg;
            return button;
        }

        private void PromptKeyDown(object? sender, KeyEventArgs eventArgs)
        {
            if (eventArgs.KeyCode != Keys.Enter || eventArgs.Shift) return;
            eventArgs.SuppressKeyPress = true;
            eventArgs.Handled = true;
            _ = SendAsync();
        }

        private async Task SendAsync()
        {
            var prompt = _prompt.Text.Trim();
            if (string.IsNullOrWhiteSpace(prompt) || !_send.Enabled) return;

            _prompt.Clear();
            AddMessage(true, prompt);
            _history.Add(("User", prompt));
            SetBusy(true);
            try
            {
                var request = BuildConversationRequest();
                if (_harness.Checked)
                {
                    request += "\n\n[LOCAL RELAY HARNESS - diagnostics supplied with explicit user consent]\n"
                        + _harnessService.DescribePermissions(_writePermissions.Checked) + "\n"
                        + _getHarnessContext();
                    if (_writePermissions.Checked) request += "\n" + _harnessService.GetCommandContract();
                }

                var result = await _ask(request);
                var responseText = result.message;
                if (result.ok) responseText = ProcessHarnessAction(responseText);
                AddMessage(false, responseText, !result.ok);
                if (result.ok) _history.Add(("Fissal", responseText));
                _model.Text = result.ok && !string.IsNullOrWhiteSpace(result.model)
                    ? $"● {result.model.ToUpperInvariant()}"
                    : result.ok ? "● CONNECTED" : "● ATTENTION";
                _model.ForeColor = result.ok ? CGreen : CWarn;
                _status.Text = result.ok ? "Response complete." : "Fissal could not complete that request.";
            }
            catch (Exception ex)
            {
                AddMessage(false, $"I could not complete that request. `{ex.Message}`", true);
                _status.Text = "Request failed. You can edit your question and try again.";
            }
            finally
            {
                SetBusy(false);
                _prompt.Focus();
            }
        }

        private string ProcessHarnessAction(string response)
        {
            const string pattern = @"<fissal-action>(.*?)</fissal-action>";
            var match = Regex.Match(response, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!match.Success) return response;

            var visibleResponse = Regex.Replace(response, pattern, string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();
            if (!_harness.Checked || !_writePermissions.Checked)
                return visibleResponse + "\n\n**Local action blocked:** Write permission is disabled.";

            var confirmation = FissalBox.Show(
                "Fissal requested a change to an approved Relay setting. The Relay will validate it, create a recovery backup, and restore the previous configuration if anything fails. Apply this change?",
                "Confirm Local Change",
                MessageBoxButtons.YesNo);
            if (confirmation != DialogResult.Yes)
                return visibleResponse + "\n\n**Local action cancelled:** No settings were changed.";

            var execution = _harnessService.Execute(match.Groups[1].Value);
            return visibleResponse + $"\n\n**Local action {(execution.ok ? "complete" : "failed")}:** {execution.message}";
        }

        private string BuildConversationRequest()
        {
            var builder = new StringBuilder("Continue this Relay support conversation. Reply to the latest user message.\n");
            var start = Math.Max(0, _history.Count - 8);
            for (var index = start; index < _history.Count; index++)
            {
                var turn = _history[index];
                builder.Append(turn.role).Append(": ").AppendLine(turn.text);
            }
            return builder.ToString();
        }

        private void SetBusy(bool busy)
        {
            _send.Enabled = !busy;
            _send.Text = busy ? "Thinking..." : "Send  >";
            _status.Text = busy ? "Fissal is reviewing the Relay signal..." : _status.Text;
            UseWaitCursor = busy;
        }

        private void AddMessage(bool fromUser, string text, bool isError = false)
        {
            var card = new Panel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = fromUser ? Color.FromArgb(43, 33, 16) : isError ? CErrBg : CPanelBg,
                Padding = new Padding(12),
                Margin = new Padding(fromUser ? 64 : 0, 0, fromUser ? 0 : 64, 10),
                Tag = "message-card",
                AccessibleName = fromUser ? "Your message" : "Fissal response",
            };
            var body = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
            body.Controls.Add(new Label
            {
                AutoSize = true,
                Text = fromUser ? "YOU" : isError ? "FISSAL // NEEDS ATTENTION" : "FISSAL",
                ForeColor = fromUser ? CGoldBrt : isError ? CBarFail : CGreen,
                Font = Mono(8f, _scale, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 6),
            });

            var content = new RichTextBox
            {
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = card.BackColor,
                ForeColor = CText,
                Font = Body(9f, _scale),
                DetectUrls = true,
                ScrollBars = RichTextBoxScrollBars.None,
                TabStop = true,
                AccessibleName = fromUser ? "Message text" : "Fissal message text",
            };
            ApplyMarkdown(content, text);
            content.Width = Math.Max(360, _transcript.ClientSize.Width - 128);
            content.Height = MeasureRichTextHeight(content, content.Width);
            content.LinkClicked += (_, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.LinkText)) return;
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(args.LinkText) { UseShellExecute = true }); } catch { }
            };
            body.Controls.Add(content);

            var copy = new LinkLabel
            {
                AutoSize = true,
                Text = "Copy response",
                LinkColor = CTextSub,
                ActiveLinkColor = CGoldBrt,
                VisitedLinkColor = CTextSub,
                Font = Body(7.5f, _scale),
                Margin = new Padding(0, 8, 0, 0),
                Visible = !fromUser,
                AccessibleName = "Copy Fissal response",
            };
            copy.LinkClicked += (_, _) =>
            {
                try { Clipboard.SetText(text); _status.Text = "Response copied to clipboard."; } catch { _status.Text = "Could not access the clipboard."; }
            };
            body.Controls.Add(copy);
            card.Controls.Add(body);
            _transcript.Controls.Add(card);
            ResizeMessageCards();
            _transcript.ScrollControlIntoView(card);
        }

        private void ResizeMessageCards()
        {
            var width = Math.Max(400, _transcript.ClientSize.Width - 32);
            foreach (Control control in _transcript.Controls)
            {
                if (!Equals(control.Tag, "message-card")) continue;
                control.Width = width - control.Margin.Horizontal;
                foreach (Control child in control.Controls)
                {
                    child.Width = control.ClientSize.Width - control.Padding.Horizontal;
                    foreach (Control nested in child.Controls)
                    {
                        if (nested is RichTextBox rich)
                        {
                            rich.Width = child.ClientSize.Width;
                            rich.Height = MeasureRichTextHeight(rich, rich.Width);
                        }
                    }
                }
            }
        }

        private int MeasureRichTextHeight(RichTextBox rich, int width)
        {
            var proposed = new Size(Math.Max(100, width - 8), int.MaxValue);
            var measured = TextRenderer.MeasureText(rich.Text + "\n", rich.Font, proposed, TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            return Math.Max(28, measured.Height + 8);
        }

        private void ApplyMarkdown(RichTextBox box, string raw)
        {
            var text = Regex.Replace(raw ?? string.Empty, "(?m)^#{1,6}\\s+", string.Empty);
            text = Regex.Replace(text, "(?m)^[-*]\\s+", "• ");
            box.Text = text;
            FormatMatches(box, @"\*\*(.+?)\*\*", FontStyle.Bold, CGoldBrt, true);
            FormatMatches(box, @"(?<!\*)\*([^*\r\n]+)\*(?!\*)", FontStyle.Italic, CText, true);
            FormatMatches(box, @"`([^`]+)`", FontStyle.Regular, CWarn, true, true);
        }

        private void FormatMatches(RichTextBox box, string pattern, FontStyle style, Color color, bool removeMarkers, bool monospace = false)
        {
            var matches = Regex.Matches(box.Text, pattern);
            for (var index = matches.Count - 1; index >= 0; index--)
            {
                var match = matches[index];
                if (removeMarkers)
                {
                    box.Select(match.Index, match.Length);
                    box.SelectedText = match.Groups[1].Value;
                }
                box.Select(match.Index, match.Groups[1].Value.Length);
                box.SelectionFont = monospace ? Mono(8.5f, _scale, style) : Body(9f, _scale, style);
                box.SelectionColor = color;
            }
            box.Select(0, 0);
        }
    }
}