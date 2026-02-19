using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using static RedfurSync.FissalTheme;

namespace RedfurSync
{
    public sealed class TrayApp : IDisposable
    {
        private readonly NotifyIcon        _trayIcon;
        private readonly ContextMenuStrip  _menu;
        private readonly FileWatcherService _watcher;
        private ToolStripMenuItem _statusItem  = null!;
        private ToolStripMenuItem _startupItem = null!;
        private UploadProgressForm? _progressForm;

        private const string Checked   = "✔️  ";
        private const string Unchecked = "     ";

        public TrayApp()
        {
            _menu     = BuildMenu();
            _trayIcon = new NotifyIcon
            {
                // We changed the name of the method here!
                Icon             = BuildFissalIcon(),
                ContextMenuStrip = _menu,
                Text             = "Fissal's Tonal Relay",
                Visible          = true
            };

            _trayIcon.MouseClick += OnTrayClick;

            _watcher = new FileWatcherService(UpdateStatus);
            _watcher.JobsChanged       += OnJobsChanged;
            _watcher.ConnectionChecked += OnConnectionChecked;

            CheckFirstRun();
        }

        // ── Startup ───────────────────────────────────────────────────────────

        private void CheckFirstRun()
        {
            var config = AppConfig.Load();
            if (!config.IsConfigured())
            {
                ShowAlert("Fissal needs to tune her vocal coils!",
                    "This one cannot bounce purrs without a server address and key.\n" +
                    "Please fill in config.json and restart the matrix.",
                    FissalAlert.AlertLevel.Low);
                OpenConfigFile();
                return;
            }

            bool on = StartupHelper.IsStartupEnabled();
            if (config.RunOnStartup && !on) StartupHelper.SetStartup(true);
            UpdateStartupText(config.RunOnStartup || on);
            _ = _watcher.StartAsync();
        }

        private void OnConnectionChecked(bool ok, string msg)
        {
            if (ok)
                ShowAlert("📡 Syncronized",
                    "This one is now actively monitoring sales!\n" +
                    "Masser hums with harmonic frequency!",
                    FissalAlert.AlertLevel.Success, 9000);
            else
                ShowAlert("🌃 Fissal's meow was lost in the void…",
                    $"The signal to the moons could not be established:\n{msg}\n\n" +
                    "Fissal will clear her mechanical throat. Do not panic.",
                    FissalAlert.AlertLevel.TotalError, 9000);
        }

        // ── Tray click ────────────────────────────────────────────────────────

        private void OnTrayClick(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            if (_progressForm != null && !_progressForm.IsDisposed)
            {
                _progressForm.Close();
                _progressForm = null;
                return;
            }

            _progressForm = new UploadProgressForm(
                _watcher.Jobs,
                j => _watcher.RetryJob(j),
                j => _watcher.CancelJob(j));

            _progressForm.FormClosed += (_, _) => _progressForm = null;
            _progressForm.PositionAboveTray();
            _progressForm.Show();
            _progressForm.Activate();
        }

        private void OnJobsChanged()
        {
            if (_progressForm == null || _progressForm.IsDisposed) return;
            if (_progressForm.InvokeRequired)
                _progressForm.BeginInvoke(_progressForm.Invalidate);
            else
                _progressForm.Invalidate();
        }

        // ── Menu ──────────────────────────────────────────────────────────────

        private ContextMenuStrip BuildMenu()
        {
            float sysScale = GetSystemScale();

            var menu = new ContextMenuStrip
            {
                Font     = Title(11f, sysScale), 
                Renderer = new FissalMenuRenderer()
            };

            _statusItem = new ToolStripMenuItem("   ⚙  Fissal is tuning her mechanical purr…")
            {
                Enabled = false,
                Font    = Body(9.5f, sysScale, FontStyle.Italic)
            };
            // Mark this item so the renderer knows to add the light
            _statusItem.Tag = "status_light";

            menu.Items.Add(_statusItem);
            menu.Items.Add(new ToolStripSeparator());

            _startupItem = new ToolStripMenuItem(Unchecked + "Run Fissal on startup");
            _startupItem.Click += (_, _) =>
            {
                bool nowOn = _startupItem?.Text != null && !_startupItem.Text.StartsWith(Checked);
                StartupHelper.SetStartup(nowOn);
                var cfg = AppConfig.Load();
                cfg.RunOnStartup = nowOn;
                AppConfig.Save(cfg);
                UpdateStartupText(nowOn);
            };
            menu.Items.Add(_startupItem);
            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add("📖  Register Your Name",       null, OnSetDisplayName);
            menu.Items.Add("📡  Network Config",  null, (_, _) => OpenConfigFile());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("⚡  Cut Connection",             null, OnShutdown);

            return menu;
        }

        private void UpdateStartupText(bool on)
        {
            if (_menu.InvokeRequired) { _menu.BeginInvoke(() => UpdateStartupText(on)); return; }
            _startupItem.Text = (on ? Checked : Unchecked) + "Run Fissal on Windows startup";
        }

        // ── Status ────────────────────────────────────────────────────────────

        private void UpdateStatus(string msg)
        {
            if (_menu.InvokeRequired) { _menu.BeginInvoke(() => UpdateStatus(msg)); return; }
            _statusItem.Text = "⚙  " + msg;
            var full = "" + msg;
            _trayIcon.Text = full.Length > 63 ? full[..63] : full;
        }

        // ── Display name ──────────────────────────────────────────────────────

        private void OnSetDisplayName(object? sender, EventArgs e)
        {
            var config = AppConfig.Load();
            using var form = new DisplayNameForm(config.DisplayName);
            if (form.ShowDialog() == DialogResult.OK)
            {
                config.DisplayName = form.DisplayName;
                AppConfig.Save(config);
                ShowAlert("Registry Updated!",
                    $"This one will address you as \"{config.DisplayName}\"!",
                    FissalAlert.AlertLevel.Success);
            }
        }

        // ── Shutdown with goodbye ─────────────────────────────────────────────

        private void OnShutdown(object? sender, EventArgs e)
        {
            using var goodbye = new GoodbyeForm();
            if (goodbye.ShowDialog() == DialogResult.OK)
            {
                _trayIcon.Visible = false;
                Application.Exit();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void ShowBalloon(string title, string text, ToolTipIcon icon)
        {
            _trayIcon.BalloonTipTitle = title;
            _trayIcon.BalloonTipText  = text;
            _trayIcon.BalloonTipIcon  = icon;
            _trayIcon.ShowBalloonTip(7000);
        }

        private void ShowAlert(string title, string text, FissalAlert.AlertLevel level = FissalAlert.AlertLevel.Normal, int timeoutMs = 7000)
        {
            if (_menu.InvokeRequired) 
            { 
                _menu.BeginInvoke(() => ShowAlert(title, text, level, timeoutMs)); 
                return; 
            }
            
            FissalAlert.Show(title, text, level, timeoutMs);
        }

        private static void OpenConfigFolder() =>
            Process.Start("explorer.exe", AppConfig.ConfigDirectory);

        private static void OpenConfigFile()
        {
            // Combines the directory path with the actual file name
            // (Make sure "config.json" matches the exact name of your file)
            string configFilePath = System.IO.Path.Combine(AppConfig.ConfigDirectory, "config.json");

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = configFilePath,
                UseShellExecute = true
            };
            
            System.Diagnostics.Process.Start(psi);
        }

        // ── High-Resolution Fissal Icon ───────────────────────────────────────
private static Icon BuildFissalIcon()
        {
            using var bmp = new Bitmap(64, 64);
            using var g   = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                string? actualResourceName = null;
                
                foreach (string name in assembly.GetManifestResourceNames())
                {
                    if (name.EndsWith("fiss.png", StringComparison.OrdinalIgnoreCase))
                    {
                        actualResourceName = name;
                        break;
                    }
                }

                if (actualResourceName != null)
                {
                    using var stream = assembly.GetManifestResourceStream(actualResourceName);
                    using var img = Image.FromStream(stream!);
                    
                    using var path = new GraphicsPath();
                    path.AddEllipse(2, 2, 60, 60);
                    g.SetClip(path);
                    
                    g.DrawImage(img, 2, 2, 60, 60);
                    g.ResetClip();
                }
                else
                {
                    g.FillEllipse(new SolidBrush(Color.FromArgb(22, 17, 10)), 2, 2, 60, 60);
                }
            }
            catch { /* Silent fallback */ }

            using var ring = new Pen(Color.FromArgb(160, 128, 48), 2.5f);
            g.DrawEllipse(ring, 2, 2, 60, 60);

            return Icon.FromHandle(bmp.GetHicon());
        }
        public void Dispose()
        {
            _progressForm?.Dispose();
            _watcher.Dispose();
            _trayIcon.Dispose();
            _menu.Dispose();
        }
    }

    // ── Menu renderer ─────────────────────────────────────────────────────────

    internal sealed class FissalMenuRenderer : ToolStripRenderer
    {
        private static readonly Color CBg    = Color.FromArgb(18, 14, 8); // Menu background
        private static readonly Color CHover = Color.FromArgb(50, 40, 20); // Hover background
        private static readonly Color CText  = Color.FromArgb(218, 195, 148); // Main text
        private static readonly Color CDim   = Color.FromArgb(95,  82, 58); // Disabled text
        private static readonly Color CGold  = Color.FromArgb(218, 182, 88); // Gold accent
        private static readonly Color CSep   = Color.FromArgb(60,  48, 24); // Separator
        private static readonly Color CBord  = Color.FromArgb(88,  70, 34); // Border
        private static readonly Color CDmnd  = Color.FromArgb(120, 95, 42); // Diamond accent

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            var g = e.Graphics;
            using var bgBrush = new SolidBrush(CBg);
            g.FillRectangle(bgBrush, new Rectangle(0, 0, e.ToolStrip.Width, e.ToolStrip.Height));
            
            using var borderPen = new Pen(CBord);
            g.DrawRectangle(borderPen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var g  = e.Graphics;
            var rc = new Rectangle(1, 1, e.Item.Width - 2, e.Item.Height - 2);

            if (!e.Item.Enabled)
            {
                using var disabledBrush = new SolidBrush(Color.FromArgb(24, 19, 11));
                g.FillRectangle(disabledBrush, rc);
                return;
            }

            if (e.Item.Selected)
            {
                Color highlightLeft = Color.FromArgb(22, 58, 29);
                Color highlightRight = Color.FromArgb(15, 40, 14);

                using var hg = new LinearGradientBrush(rc,
                    highlightLeft, highlightRight,
                    LinearGradientMode.Horizontal);
                g.FillRectangle(hg, rc);
                
                // This draws the little gold accent bar on the left edge
                using var goldBrush = new SolidBrush(CGold);
                g.FillRectangle(goldBrush, new Rectangle(1, rc.Y + 2, 3, rc.Height - 4));
            }
            else
            {
                using var bgBrush = new SolidBrush(CBg);
                g.FillRectangle(bgBrush, rc);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            e.TextColor = !e.Item.Enabled ? CDim
                        : e.Item.Selected ? CGold
                        : CText;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var g  = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int y  = e.Item.Height / 2;
            int cx = e.Item.Width  / 2;

            using var sepPen = new Pen(CSep);
            g.DrawLine(sepPen, 8, y, e.Item.Width - 8, y);

            using var dmndBrush = new SolidBrush(CDmnd);
            g.FillPolygon(dmndBrush, new[]
            {
                new Point(cx,     y - 4),
                new Point(cx + 4, y),
                new Point(cx,     y + 4),
                new Point(cx - 4, y),
            });
            
            using var dmndPen = new Pen(Color.FromArgb(80, CGold), 0.5f);
            g.DrawPolygon(dmndPen, new[]
            {
                new Point(cx,     y - 4),
                new Point(cx + 4, y),
                new Point(cx,     y + 4),
                new Point(cx - 4, y),
            });
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = CDmnd;
            base.OnRenderArrow(e);
        }
    }
}