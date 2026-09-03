using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
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
        
        // Performance menu items
        private ToolStripMenuItem _perfLowItem  = null!;
        private ToolStripMenuItem _perfMedItem  = null!;
        private ToolStripMenuItem _perfHighItem = null!;

        private UploadProgressForm? _progressForm;
        private RelayMainWindow?    _mainWindow;
        private EventWaitHandle?    _wakeEvent;
        private RegisteredWaitHandle? _wakeRegistration;

        // Mono glyphs derived from v2 tokens.css --theme-mark '◈' / '◆' — not emoji.
        private const string Checked   = "◆  ";
        private const string Unchecked = "   ";

        private int  _prevActiveCount  = 0;
        private bool _batchHadError    = false;
        private bool _batchHadSuccess  = false;

        public TrayApp()
        {
            _menu     = BuildMenu();
            
            // Force the menu handle to exist immediately so we can safely invoke on it
            var _ = _menu.Handle;

            _trayIcon = new NotifyIcon
            {
                Icon             = BuildFissalIcon(),
                ContextMenuStrip = _menu,
                Text             = "Fissal's Tonal Relay",
                Visible          = true
            };

            _trayIcon.MouseClick += OnTrayClick;

            _watcher = new FileWatcherService(UpdateStatus);
            _watcher.JobsChanged       += OnJobsChanged;
            _watcher.ConnectionChecked += OnConnectionChecked;

            // ── Listen for second-instance launches to smoothly wake and show terminal ──
            try
            {
                _wakeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.WakeEventName);
                _wakeRegistration = ThreadPool.RegisterWaitForSingleObject(
                    _wakeEvent,
                    (_, _) =>
                    {
                        if (_disposed || _menu.IsDisposed) return;
                        _menu.BeginInvoke(() => OpenMainWindow("sync"));
                    },
                    null,
                    -1,
                    false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TrayApp] Single-instance wake event registration skipped: {ex.Message}");
            }

            // ── Read her unified mind and restore the visual fidelity right as she wakes ──
            var config = AppConfig.Instance;
            SetPerformanceMode(config.VisualFidelity, saveConfig: false);

            CheckFirstRun();
        }
        private System.Windows.Forms.Timer? _batchAlertTimer;
        private bool _disposed;
        private void CheckFirstRun()
        {
            var config = AppConfig.Instance;

            bool needsName = string.IsNullOrWhiteSpace(config.DisplayName) || config.DisplayName == "Redfur Trader";
            bool needsPairing = string.IsNullOrWhiteSpace(config.DeviceToken) && string.IsNullOrWhiteSpace(config.ApiKey);

            if (needsName || needsPairing)
            {
                using var form = new DisplayNameForm(config.DisplayName);
                form.ShowDialog();
                
                // If they still didn't pair or enter a pairing code, we log it
                if (string.IsNullOrWhiteSpace(config.DeviceToken) && string.IsNullOrWhiteSpace(config.ApiKey) && string.IsNullOrWhiteSpace(config.PairingCode))
                {
                    UpdateStatus("Waiting for a Relay pairing code");
                    return;
                }
            }

            bool on = StartupHelper.IsStartupEnabled();
            if (config.RunOnStartup && !on) StartupHelper.SetStartup(true);
            UpdateStartupText(config.RunOnStartup || on);
            var _ = _watcher.StartAsync();
        }

        private void OnConnectionChecked(bool ok, string msg)
        {
            if (ok)
                ShowCustomAlert("Frequencies Synced!",
                    "Fissal is carefully monitoring your tracked sales!",
                    Color.FromArgb(60, 180, 220),
                    10,
                    5000,
                    () => OpenMainWindow("sync"));
            else
                ShowAlert("Fissal's meow was lost in the void…",
                    $"The signal to the moons could not be established:\n{msg}\n\n" +
                    "Fissal will clear her mechanical throat. Do not panic.",
                    FissalAlert.AlertLevel.TotalError,
                    9000,
                    () => OpenMainWindow("diagnostics"));
        }

        private void OnTrayClick(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            if (_mainWindow != null && !_mainWindow.IsDisposed && _mainWindow.Visible)
            {
                _mainWindow.Hide();
                return;
            }

            OpenMainWindow("sync");
        }

        private void OpenMainWindow(string tabId = "sync")
        {
            if (_menu.InvokeRequired)
            {
                _menu.BeginInvoke(() => OpenMainWindow(tabId));
                return;
            }

            if (_mainWindow == null || _mainWindow.IsDisposed)
            {
                _mainWindow = new RelayMainWindow(_watcher, ApplyUpdate);
            }

            _mainWindow.NavigateToTab(tabId);
        }
        
        private void OnJobsChanged()
        {
            if (_progressForm != null && !_progressForm.IsDisposed)
            {
                if (_progressForm.InvokeRequired)
                    _progressForm.BeginInvoke(_progressForm.Invalidate);
                else
                    _progressForm.Invalidate();
            }

            // [Req 1 & Req 5] Delay slightly to group alerts together and prevent spam
            if (_batchAlertTimer == null)
            {
                _batchAlertTimer = new System.Windows.Forms.Timer { Interval = 2000 };
                _batchAlertTimer.Tick += (_, _) => 
                { 
                    _batchAlertTimer.Stop(); 
                    if (_menu.InvokeRequired) _menu.BeginInvoke(CheckBatchCompletion);
                    else CheckBatchCompletion();
                };
            }
            _batchAlertTimer.Stop();
            _batchAlertTimer.Start();
        }

private void CheckBatchCompletion()
        {
            try
            {
                var jobs = _watcher.Jobs;
                if (jobs.Count == 0) { _prevActiveCount = 0; return; }

                DateTime newestTime = jobs.Max(j => j.QueuedAt);
                var recentGroup = jobs
                    .Where(j => Math.Abs((j.QueuedAt - newestTime).TotalMinutes) <= 15) // Expanded to catch larger batches
                    .ToList();

                int  activeNow       = recentGroup.Count(j => j.Status is UploadStatus.Uploading or UploadStatus.Queued);
                bool anyFailed       = recentGroup.Any(j => j.Status is UploadStatus.Failed or UploadStatus.Cancelled);
                bool anySucceeded    = recentGroup.Any(j => j.Status == UploadStatus.Done);
                bool hasReadyUpdate  = recentGroup.Any(j => j.Status == UploadStatus.UpdateReady);
                bool hasFailedUpdate = recentGroup.Any(j => j.IsUpdate && (j.Status == UploadStatus.Failed || j.Status == UploadStatus.Cancelled));
                
                int totalPending     = recentGroup.Count(j => j.Status == UploadStatus.Queued);
                int totalSynced      = recentGroup.Count(j => j.Status == UploadStatus.Done);
                int totalErrors      = recentGroup.Count(j => j.Status is UploadStatus.Failed or UploadStatus.Cancelled);

                // [Req 5] Summarized notification logic
                if (_prevActiveCount == 0 && activeNow > 0)
                {
                    var activeJobs = recentGroup.Where(j => j.Status is UploadStatus.Uploading or UploadStatus.Queued).ToList();
                    string names = activeJobs.Count == 1 ? activeJobs[0].FileName : $"{activeJobs.Count} files";
                    ShowCustomAlert("Transmission Initiated", $"Fissal is syncing {names} to the Redfur lattice!", Color.FromArgb(200, 160, 60), 6, 4000, OpenProgressForm);
                }

                if (_prevActiveCount > 0)
                {
                    if (anyFailed)    _batchHadError   = true;
                    if (anySucceeded) _batchHadSuccess = true;
                }

                if (_prevActiveCount > 0 && activeNow == 0)
                {
                    if (hasReadyUpdate)
                    {
                        ShowCustomAlert("Update Prepared!", 
                            "A new module has been received from Redfur!\n\nOpen the terminal to apply the upgrade.", 
                            Color.FromArgb(180, 100, 220), 4, 10000, OpenProgressForm);
                    }
                    else if (hasFailedUpdate)
                    {
                        ShowAlert("Update Interrupted", 
                            "Fissal's claws slipped while pulling the new module!\n\nCheck diagnostics for details!", 
                            FissalAlert.AlertLevel.TotalError, 9000, OpenProgressForm);
                    }
                    else if (_batchHadSuccess || _batchHadError)
                    {
                        if (_batchHadError)
                        {
                            string msg = $"[SYNC SUMMARY]\n\n" +
                                         $"✦ Verified: {totalSynced} files\n" +
                                         $"✖ Errors: {totalErrors} files\n\n" +
                                         $"Interference detected! Open diagnostics to review log anomalies.";
                            ShowAlert("Sync Completed With Errors", msg, FissalAlert.AlertLevel.TotalError, 10000, OpenProgressForm);
                        }
                        else if (_batchHadSuccess)
                        {
                            string msg = $"[SYNC SUMMARY]\n\n" +
                                         $"✦ Verified: {totalSynced} files\n" +
                                         $"✦ Errors: 0\n\n" +
                                         $"All data securely delivered to the lattice!";
                            ShowCustomAlert("Sync Complete!", msg, Color.FromArgb(60, 180, 220), 6, 6000, OpenProgressForm);
                        }
                    }
                    _batchHadError   = false;
                    _batchHadSuccess = false;
                }

                _prevActiveCount = activeNow;
            }
            catch { }
        }
        
        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip
            {
                Renderer = new FissalMenuRenderer(),
                ShowImageMargin = false
            };
            // DPI: prefer handle-aware per-monitor scale; fallback to system scale
            // if handle not yet created (BuildMenu runs before _menu assigned).
            float sysScale;
            try { var _ = menu.Handle; sysScale = GetScale(menu.Handle); }
            catch { sysScale = GetSystemScale(); }
            menu.Font = Title(11f, sysScale);

            _statusItem = new ToolStripMenuItem("⚙  Fissal is harmonizing her mechanical purr…")
            {
                Enabled = false,
                Font    = Body(9.5f, sysScale, FontStyle.Italic)
            };
            _statusItem.Tag = "status_light";

            menu.Items.Add(_statusItem);
            menu.Items.Add(new ToolStripSeparator());

            // ── Themed Visual Fidelity Menu ──
            var perfMenu = new ToolStripMenuItem("⚡  Visual Fidelity");
            ((ToolStripDropDownMenu)perfMenu.DropDown).ShowImageMargin = false;

            _perfLowItem  = new ToolStripMenuItem(Unchecked + "Minimal (Low FX)");
            _perfMedItem  = new ToolStripMenuItem(Checked   + "Balanced");
            _perfHighItem = new ToolStripMenuItem(Unchecked + "Full Glow (Max FX)");

            _perfLowItem.Click  += (_, _) => SetPerformanceMode(FidelityMode.Low, true);
            _perfMedItem.Click  += (_, _) => SetPerformanceMode(FidelityMode.Medium, true);
            _perfHighItem.Click += (_, _) => SetPerformanceMode(FidelityMode.High, true);

            perfMenu.DropDownItems.Add(_perfLowItem);
            perfMenu.DropDownItems.Add(_perfMedItem);
            perfMenu.DropDownItems.Add(_perfHighItem);

            menu.Items.Add(perfMenu);
            menu.Items.Add(new ToolStripSeparator());

            _startupItem = new ToolStripMenuItem(Unchecked + "Run Fissal on startup");
            _startupItem.Click += (_, _) =>
            {
                bool nowOn = _startupItem?.Text != null && !_startupItem.Text.StartsWith(Checked);
                StartupHelper.SetStartup(nowOn);
                
                var cfg = AppConfig.Instance;
                cfg.RunOnStartup = nowOn;
                cfg.Save();
                
                UpdateStartupText(nowOn);
            };
            menu.Items.Add(_startupItem);
            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add("⚡  Open Relay Terminal",     null, (_, _) => OpenMainWindow("sync"));
            menu.Items.Add("💬  Ask Fissal",              null, (_, _) => OpenMainWindow("assistant"));
            menu.Items.Add("🛠️  Setup & Pairing",         null, (_, _) => OpenMainWindow("setup"));
            menu.Items.Add("🎨  Terminal Themes",         null, (_, _) => OpenMainWindow("themes"));
            menu.Items.Add("⚙️  Diagnostics",             null, (_, _) => OpenMainWindow("diagnostics"));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("🔌  Cut Connection",          null, OnShutdown);

            return menu;
        }

        private void SetPerformanceMode(FidelityMode mode, bool saveConfig)
        {
            UploadProgressForm.AppConfig.SetMode(mode);

            _perfLowItem.Text  = (mode == FidelityMode.Low ? Checked : Unchecked) + "Minimal (Low FX)";
            _perfMedItem.Text  = (mode == FidelityMode.Medium ? Checked : Unchecked) + "Balanced";
            _perfHighItem.Text = (mode == FidelityMode.High ? Checked : Unchecked) + "Full Glow (Max FX)";

            if (saveConfig)
            {
                var config = AppConfig.Instance;
                config.VisualFidelity = mode;
                config.Save();
            }

            if (_progressForm != null && !_progressForm.IsDisposed)
            {
                _progressForm.ApplyAnimationInterval();
                _progressForm.Invalidate();
            }
        }

        private void UpdateStartupText(bool on)
        {
            if (_menu.InvokeRequired) { _menu.BeginInvoke(() => UpdateStartupText(on)); return; }
            _startupItem.Text = (on ? Checked : Unchecked) + "Run Fissal on startup";
        }

        private void UpdateStatus(string msg)
        {
            if (_menu.InvokeRequired) { _menu.BeginInvoke(() => UpdateStatus(msg)); return; }
            _statusItem.Text = "⚙  " + msg;
            var full = "" + msg;
            _trayIcon.Text = full.Length > 63 ? full[..63] : full;
        }

        private void OnShutdown(object? sender, EventArgs e)
        {
            using var goodbye = new GoodbyeForm();
            if (goodbye.ShowDialog() == DialogResult.OK)
            {
                _trayIcon.Visible = false;
                Application.Exit();
            }
        }

        private void ShowBalloon(string title, string text, ToolTipIcon icon)
        {
            _trayIcon.BalloonTipTitle = title;
            _trayIcon.BalloonTipText  = text;
            _trayIcon.BalloonTipIcon  = icon;
            _trayIcon.ShowBalloonTip(7000);
        }

        private void ShowAlert(string title, string text, FissalAlert.AlertLevel level = FissalAlert.AlertLevel.Normal, int timeoutMs = 7000, Action? onClick = null)
        {
            if (_menu.InvokeRequired) { _menu.BeginInvoke(() => ShowAlert(title, text, level, timeoutMs, onClick)); return; }
            FissalAlert.Show(title, text, level, timeoutMs, onClick);
        }

        private void ShowCustomAlert(string title, string text, Color lightColor, int flashSpeed, int timeoutMs = 7000, Action? onClick = null)
        {
            if (_menu.InvokeRequired) { _menu.BeginInvoke(() => ShowCustomAlert(title, text, lightColor, flashSpeed, timeoutMs, onClick)); return; }
            FissalAlert.ShowCustom(title, text, lightColor, flashSpeed, timeoutMs, onClick);
        }
        
        private static void OpenConfigFolder() => Process.Start("explorer.exe", AppConfig.ConfigDirectory);

        private static void OpenConfigFile()
        {
            string configFilePath = AppConfig.ConfigPath; 
            
            if (!System.IO.File.Exists(configFilePath))
            {
                AppConfig.Instance.Save();
            }

            var psi = new System.Diagnostics.ProcessStartInfo 
            { 
                FileName = "notepad.exe", 
                Arguments = $"\"{configFilePath}\"",
                UseShellExecute = true 
            };
            System.Diagnostics.Process.Start(psi);
        }

        private void ApplyUpdate(UploadJob job)
        {
            var result = FissalBox.Show(
                "A new module has been prepared for Fissal.\n\nDo you want to restart the program to apply the upgrade?", 
                "Update Ready!", 
                MessageBoxButtons.YesNo);

            if (result != DialogResult.Yes) return;

            string exePath;
            try
            {
                exePath = Environment.ProcessPath ?? throw new InvalidOperationException("Could not determine the executable path.");
            }
            catch (Exception ex)
            {
                ShowAlert("Update Failed!", $"Fissal's claws slipped: {ex.Message}", FissalAlert.AlertLevel.TotalError);
                return;
            }

            // Backup → replace → launch lives in Core so failure injection is testable on Linux.
            var installer = new UpdateInstaller(new PhysicalUpdateFileSystem(), path => Process.Start(path));
            var outcome = installer.Apply(exePath, job.FilePath);
            if (!outcome.Ok)
            {
                ShowAlert("Update Failed!", $"Fissal's claws slipped: {outcome.Message}", FissalAlert.AlertLevel.TotalError);
                return;
            }

            Application.Exit();
        }

        private void OpenProgressForm()
        {
            if (_menu.InvokeRequired) { _menu.BeginInvoke(OpenProgressForm); return; }

            // If it's already open, just bring it to the front so your eyes can lock onto it
            if (_progressForm != null && !_progressForm.IsDisposed)
            {
                _progressForm.Activate();
                return;
            }

            _progressForm = new UploadProgressForm(
                _watcher.Jobs,
                j => _watcher.RetryJob(j),
                j => _watcher.CancelJob(j),
                j => ApplyUpdate(j)); 
            _progressForm.FormClosed += (_, _) => _progressForm = null;
            _progressForm.PositionAboveTray();
            _progressForm.Show();
            _progressForm.Activate();
        }
        
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
                    using var img    = Image.FromStream(stream!);

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
            catch { }

            using var ring = new Pen(Color.FromArgb(160, 128, 48), 2.5f);
            g.DrawEllipse(ring, 2, 2, 60, 60);

            return Icon.FromHandle(bmp.GetHicon());
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _wakeRegistration?.Unregister(null);
            _wakeEvent?.Dispose();

            _batchAlertTimer?.Stop();
            _batchAlertTimer?.Dispose();
            _progressForm?.Dispose();
            _mainWindow?.Dispose();
            _watcher.Dispose();
            _trayIcon.Dispose();
            _menu.Dispose();
        }
    }

    internal sealed class FissalMenuRenderer : ToolStripRenderer
    {
        // Derived from FissalTheme — single source of truth (tokens.css parity).
        // CBg kept at 18,14,8 (slightly raised vs FissalTheme.CBg 15,12,6) so
        // the context menu lifts off the main panel; all other values follow theme.
        private static readonly Color CBg    = Color.FromArgb(18, 14, 8); // raised menu surface — intentional vs FissalTheme.CBg
        private static readonly Color CHover = Color.FromArgb(50, 40, 20);
        private static readonly Color CText  = FissalTheme.CText;
        private static readonly Color CDim   = FissalTheme.CTextSub;
        private static readonly Color CGold  = FissalTheme.CGoldBrt; // was 218,182,88 -> 212,162,78 (#d4a24e)
        private static readonly Color CSep   = FissalTheme.CSep;
        private static readonly Color CBord  = FissalTheme.CBorder;
        private static readonly Color CDmnd  = FissalTheme.CGoldDim;

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
                using var hg = new LinearGradientBrush(rc,
                    Color.FromArgb(22, 58, 29), Color.FromArgb(15, 40, 14),
                    LinearGradientMode.Horizontal);
                g.FillRectangle(hg, rc);
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

            var rect = e.TextRectangle;
            rect.Y -= 4;
            e.TextRectangle = rect;

            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var g  = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int y  = e.Item.Height / 3;
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