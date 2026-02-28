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

        private const string Checked   = "👍  ";
        private const string Unchecked = "👎  ";

        private const string Selected = "👍  ";
        private const string Unselected = "   ";

        private int  _prevActiveCount  = 0;
        private bool _batchHadError    = false;
        private bool _batchHadSuccess  = false;

        public TrayApp()
        {
            _menu     = BuildMenu();
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

            // ── Read her memory and restore the visual fidelity right as she wakes ──
            var config = AppConfig.Load();
            SetPerformanceMode(config.VisualFidelity, saveConfig: false);

            CheckFirstRun();
        }

        private void CheckFirstRun()
        {
            var config = AppConfig.Load();
            if (!config.IsConfigured())
            {
                ShowAlert("Fissal needs to tune her vocal coils!",
                    "This one cannot hear without a server address and key.\n" +
                    "Please fill in config.json and restart the matrix.",
                    FissalAlert.AlertLevel.TotalError);
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
                ShowCustomAlert("Frequencies Synced!",
                    "Fissal is now carefully monitoring your tracked sales!\n" +
                    "This ones chassis hums with harmonic frequency!",
                    Color.FromArgb(60, 180, 220),
                    10,
                    7000);
            else
                ShowAlert("Fissal's meow was lost in the void…",
                    $"The signal to the moons could not be established:\n{msg}\n\n" +
                    "Fissal will clear her mechanical throat. Do not panic.",
                    FissalAlert.AlertLevel.TotalError, 7000);
        }

        private void OnTrayClick(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            // If you click the tray while it's open, it neatly folds away
            if (_progressForm != null && !_progressForm.IsDisposed)
            {
                _progressForm.Close();
                _progressForm = null;
                return;
            }

            OpenProgressForm();
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

            if (_menu.InvokeRequired)
                _menu.BeginInvoke(CheckBatchCompletion);
            else
                CheckBatchCompletion();
        }

        private void CheckBatchCompletion()
        {
            try
            {
                var jobs = _watcher.Jobs;
                if (jobs.Count == 0) { _prevActiveCount = 0; return; }

                DateTime newestTime = jobs.Max(j => j.QueuedAt);
                var recentGroup = jobs
                    .Where(j => Math.Abs((j.QueuedAt - newestTime).TotalMinutes) <= 10)
                    .ToList();

                int  activeNow       = recentGroup.Count(j => j.Status is UploadStatus.Uploading or UploadStatus.Queued);
                bool anyFailed       = recentGroup.Any(j => j.Status is UploadStatus.Failed or UploadStatus.Cancelled);
                bool anySucceeded    = recentGroup.Any(j => j.Status == UploadStatus.Done);
                bool hasReadyUpdate  = recentGroup.Any(j => j.Status == UploadStatus.UpdateReady);
                bool hasFailedUpdate = recentGroup.Any(j => j.IsUpdate && (j.Status == UploadStatus.Failed || j.Status == UploadStatus.Cancelled));
                
                if (_prevActiveCount > 0)
                {
                    if (anyFailed)    _batchHadError   = true;
                    if (anySucceeded) _batchHadSuccess = true;
                }

                if (_prevActiveCount > 0 && activeNow == 0)
                {
                    var failJobs = recentGroup.Where(j => (j.Status == UploadStatus.Failed || j.Status == UploadStatus.Cancelled) && !j.IsUpdate).ToList();
                    var doneJobs = recentGroup.Where(j => j.Status == UploadStatus.Done && !j.IsUpdate).ToList();
                    
                    if (hasReadyUpdate)
                    {
                        ShowCustomAlert("Update Prepared!", 
                            "A new module has been received from Redfur!\nOpen the terminal to apply the upgrade.", 
                            Color.FromArgb(180, 100, 220), 
                            4, 10000, OpenProgressForm); // <-- Added callback
                    }
                    else if (hasFailedUpdate)
                    {
                        ShowAlert("Update Interrupted", 
                            "Fissal's claws slipped while pulling the new module.\nCheck diagnostics for details!", 
                            FissalAlert.AlertLevel.TotalError, 9000, OpenProgressForm); // <-- Added callback
                    }
                    else if (_batchHadSuccess || _batchHadError)
                    {
                        if (failJobs.Count > 0)
                        {
                            string failNames = failJobs.Count <= 2 
                                ? string.Join(" and ", failJobs.Select(j => j.FileName))
                                : $"{failJobs.Count} files";
                                
                            string msg = $"Interference detected! {failNames} failed to reach the matrix.\nExpand the diagnostics panel for details.";
                            
                            if (doneJobs.Count > 0)
                            {
                                string doneNames = doneJobs.Count <= 2
                                    ? string.Join(" & ", doneJobs.Select(j => j.FileName))
                                    : $"{doneJobs.Count} files";
                                msg += $"\n\n✦ However, {doneNames} transmitted successfully.";
                            }

                            ShowAlert("Sync Encountered Errors", msg, FissalAlert.AlertLevel.TotalError, 9000, OpenProgressForm); // <-- Added callback
                        }
                        else if (doneJobs.Count > 0)
                        {
                            string doneNames = doneJobs.Count <= 2 
                                ? string.Join(" and ", doneJobs.Select(j => j.FileName))
                                : $"{doneJobs.Count} files";
                                
                            string msg = $"{doneNames} successfully delivered to the matrix.\nAll transmissions verified and sealed.";

                            ShowCustomAlert("Sync Complete!", msg, Color.FromArgb(60, 180, 220), 6, 7000, OpenProgressForm); // <-- Added callback
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
            float sysScale = GetSystemScale();

            var menu = new ContextMenuStrip
            {
                Font     = Title(11f, sysScale),
                Renderer = new FissalMenuRenderer()
            };

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
            
            _perfLowItem  = new ToolStripMenuItem(Unselected + "Super Clear (Low CPU)");
            _perfMedItem  = new ToolStripMenuItem(Selected   + "Clear (Balanced)");
            _perfHighItem = new ToolStripMenuItem(Unselected + "Terminal (Max Visuals)");

            _perfLowItem.Click  += (_, _) => SetPerformanceMode(UploadProgressForm.AppConfig.FidelityMode.Low, true);
            _perfMedItem.Click  += (_, _) => SetPerformanceMode(UploadProgressForm.AppConfig.FidelityMode.Medium, true);
            _perfHighItem.Click += (_, _) => SetPerformanceMode(UploadProgressForm.AppConfig.FidelityMode.High, true);

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
                var cfg = AppConfig.Load();
                cfg.RunOnStartup = nowOn;
                AppConfig.Save(cfg);
                UpdateStartupText(nowOn);
            };
            menu.Items.Add(_startupItem);
            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add("📝  Register Your Name",      null, OnSetDisplayName);
            menu.Items.Add("📡  Network Config",          null, (_, _) => OpenConfigFile());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("🔌  Cut Connection",          null, OnShutdown);

            return menu;
        }

        private void SetPerformanceMode(UploadProgressForm.AppConfig.FidelityMode mode, bool saveConfig)
        {
            UploadProgressForm.AppConfig.SetMode(mode);

            _perfLowItem.Text  = (mode == UploadProgressForm.AppConfig.FidelityMode.Low ? Selected : Unselected) + "Super Clear (Low CPU)";
            _perfMedItem.Text  = (mode == UploadProgressForm.AppConfig.FidelityMode.Medium ? Selected : Unselected) + "Clear (Balanced)";
            _perfHighItem.Text = (mode == UploadProgressForm.AppConfig.FidelityMode.High ? Selected : Unselected) +"Terminal (Max Visuals)";

            if (saveConfig)
            {
                var config = AppConfig.Load();
                config.VisualFidelity = mode;
                AppConfig.Save(config);
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

        private void OnSetDisplayName(object? sender, EventArgs e)
        {
            var config = AppConfig.Load();
            using var form = new DisplayNameForm(config.DisplayName);
            if (form.ShowDialog() == DialogResult.OK)
            {
                config.DisplayName = form.DisplayName;
                AppConfig.Save(config);
                ShowCustomAlert("Registry Updated!",
                    $"Confirmed!\n\nFissal will address you as \"{config.DisplayName}\" in the logs!",
                    CGreen,
                    5);
            }
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
            string configFilePath = System.IO.Path.Combine(AppConfig.ConfigDirectory, "config.json");
            var psi = new System.Diagnostics.ProcessStartInfo { FileName = configFilePath, UseShellExecute = true };
            System.Diagnostics.Process.Start(psi);
        }

        private void ApplyUpdate(UploadJob job)
        {
            var result = FissalBox.Show(
                "A new module has been prepared for Fissal.\n\nDo you want to restart the program to apply the upgrade?", 
                "Update Ready!", 
                MessageBoxButtons.YesNo);

            if (result == DialogResult.Yes)
            {
                try 
                {
                    string exePath = Environment.ProcessPath ?? AppContext.BaseDirectory;
                    string oldPath = exePath + ".old";
                    
                    if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
                    System.IO.File.Move(exePath, oldPath);
                    System.IO.File.Move(job.FilePath, exePath);
                    
                    Process.Start(exePath);
                    Application.Exit();
                }
                catch (Exception ex) 
                {
                    ShowAlert("Update Failed!", $"Fissal's claws slipped: {ex.Message}", FissalAlert.AlertLevel.TotalError);
                }
            }
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
            _progressForm?.Dispose();
            _watcher.Dispose();
            _trayIcon.Dispose();
            _menu.Dispose();
        }
    }

    internal sealed class FissalMenuRenderer : ToolStripRenderer
    {
        private static readonly Color CBg    = Color.FromArgb(18, 14, 8);
        private static readonly Color CHover = Color.FromArgb(50, 40, 20);
        private static readonly Color CText  = Color.FromArgb(218, 195, 148);
        private static readonly Color CDim   = Color.FromArgb(95,  82, 58);
        private static readonly Color CGold  = Color.FromArgb(218, 182, 88);
        private static readonly Color CSep   = Color.FromArgb(60,  48, 24);
        private static readonly Color CBord  = Color.FromArgb(88,  70, 34);
        private static readonly Color CDmnd  = Color.FromArgb(120, 95, 42);

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