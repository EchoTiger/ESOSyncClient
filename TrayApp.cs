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
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private readonly NotifyIcon        _trayIcon;
        private readonly ContextMenuStrip  _menu;
        private readonly FileWatcherService _watcher;
        private ToolStripMenuItem _statusItem  = null!;
        private ToolStripMenuItem _startupItem = null!;
        private ToolStripMenuItem _silentSyncItem = null!;
        
        // Performance menu items
        private ToolStripMenuItem _perfLowItem  = null!;
        private ToolStripMenuItem _perfMedItem  = null!;
        private ToolStripMenuItem _perfHighItem = null!;

        private readonly SynchronizationContext? _uiContext;
        private RelayMainWindow?    _mainWindow;
        private UploadProgressForm? _compactWindow;
        private EventWaitHandle?    _wakeEvent;
        private RegisteredWaitHandle? _wakeRegistration;

        // Mono glyphs derived from v2 tokens.css --theme-mark '◈' / '◆' — not emoji.
        private const string Checked   = "◆  ";
        private const string Unchecked = "   ";

        private int  _prevActiveCount  = 0;
        private bool _batchHadError    = false;
        private bool _batchHadSuccess  = false;

        public TrayApp(bool startMinimized = false)
        {
            _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
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
                        if (_disposed) return;
                        if (_uiContext != null)
                            _uiContext.Post(_ => OpenMainWindow("sync"), null);
                        else if (!_menu.IsDisposed && _menu.IsHandleCreated)
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

            if (!startMinimized)
            {
                // If the watcher never started (user canceled first-run), show setup tab
                // so the app visibly launches instead of sitting invisibly in the tray
                bool needsSetup = string.IsNullOrWhiteSpace(AppConfig.Instance.DeviceToken)
                    && string.IsNullOrWhiteSpace(AppConfig.Instance.ApiKey);
                Action showInitial = needsSetup
                    ? () => OpenMainWindow("setup", forceFull: true)
                    : AppConfig.Instance.CompactMode
                        ? () => OpenCompactWindow()
                        : () => OpenMainWindow("sync");
                if (_uiContext != null)
                    _uiContext.Post(_ => showInitial(), null);
                else
                    showInitial();
            }
        }
        private System.Windows.Forms.Timer? _batchAlertTimer;
        private bool _disposed;
        private void CheckFirstRun()
        {
            var config = AppConfig.Instance;

            bool isPaired = !string.IsNullOrWhiteSpace(config.DeviceToken) || !string.IsNullOrWhiteSpace(config.ApiKey);
            bool needsPairing = !isPaired;

            if (needsPairing)
            {
                using var form = new DisplayNameForm(config.DisplayName);
                var dr = form.ShowDialog();
                
                // If they canceled or still haven't paired, stand by quietly without raising alarming error popups
                if (dr != DialogResult.OK && string.IsNullOrWhiteSpace(config.DeviceToken) && string.IsNullOrWhiteSpace(config.ApiKey) && string.IsNullOrWhiteSpace(config.PairingCode))
                {
                    UpdateStatus("Waiting for a Relay pairing code");
                }
            }

            bool hasConfiguredIdentity = !string.IsNullOrWhiteSpace(config.DeviceToken) || !string.IsNullOrWhiteSpace(config.ApiKey);
            bool on = StartupHelper.IsStartupEnabled();
            if (config.RunOnStartup && !on && hasConfiguredIdentity)
            {
                StartupHelper.SetStartup(true);
                on = true;
            }
            UpdateStartupText(on);
            UpdateSilentSyncText(config.SilentSync);
            var _ = _watcher.StartAsync();
        }

        private void OnConnectionChecked(bool ok, string msg)
        {
            if (_disposed) return;
            if (ok)
            {
                if (!AppConfig.Instance.SilentSync)
                {
                    ShowCustomAlert("Frequencies Synced!",
                        "Fissal is carefully monitoring your tracked sales!",
                        Color.FromArgb(60, 180, 220),
                        10,
                        5000,
                        () => OpenMainWindow("sync"));
                }
            }
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

            if (_compactWindow != null && !_compactWindow.IsDisposed && _compactWindow.Visible)
            {
                _compactWindow.Hide();
                return;
            }

            if (_mainWindow != null && !_mainWindow.IsDisposed && _mainWindow.Visible)
            {
                _mainWindow.Hide();
                return;
            }

            if (AppConfig.Instance.CompactMode)
                OpenCompactWindow();
            else
                OpenMainWindow("sync");
        }

        public void ActivateMainWindow()
        {
            if (_mainWindow == null || _mainWindow.IsDisposed) return;
            if (!_mainWindow.Visible) _mainWindow.Show();
            if (_mainWindow.WindowState == FormWindowState.Minimized)
                _mainWindow.WindowState = FormWindowState.Normal;
            _mainWindow.TopMost = true;
            _mainWindow.TopMost = false;
            _mainWindow.BringToFront();
            _mainWindow.Activate();
            _mainWindow.Focus();
        }

        private void OpenCompactWindow(Point? anchorTopRight = null)
        {
            if (_disposed) return;
            if (_menu.InvokeRequired)
            {
                if (_disposed || _menu.IsDisposed || !_menu.IsHandleCreated) return;
                _menu.BeginInvoke(() => OpenCompactWindow(anchorTopRight));
                return;
            }

            if (_mainWindow != null && !_mainWindow.IsDisposed && _mainWindow.Visible)
                _mainWindow.Hide();

            bool newlyCreated = false;
            if (_compactWindow == null || _compactWindow.IsDisposed)
            {
                _compactWindow = new UploadProgressForm(_watcher, ApplyUpdate, SwapToFullWindow);
                newlyCreated = true;
            }

            if (anchorTopRight.HasValue)
            {
                var scr = Screen.FromPoint(anchorTopRight.Value) ?? Screen.PrimaryScreen;
                var wa = scr?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
                int x = Math.Clamp(anchorTopRight.Value.X - _compactWindow.Width, wa.Left + 8, Math.Max(wa.Left + 8, wa.Right - _compactWindow.Width - 8));
                int y = Math.Clamp(anchorTopRight.Value.Y, wa.Top + 8, Math.Max(wa.Top + 8, wa.Bottom - _compactWindow.Height - 8));
                _compactWindow.StartPosition = FormStartPosition.Manual;
                _compactWindow.Location = new Point(x, y);
            }
            else if (newlyCreated)
            {
                _compactWindow.PositionAboveTray();
            }

            if (!_compactWindow.Visible) _compactWindow.Show();
            if (_compactWindow.WindowState == FormWindowState.Minimized)
                _compactWindow.WindowState = FormWindowState.Normal;
            _compactWindow.TopMost = true;
            _compactWindow.TopMost = false;
            _compactWindow.BringToFront();
            _compactWindow.Activate();
            _compactWindow.Focus();
        }

        private void SwapToCompactWindow()
        {
            if (_disposed) return;
            if (_menu.InvokeRequired)
            {
                if (_disposed || _menu.IsDisposed || !_menu.IsHandleCreated) return;
                _menu.BeginInvoke(SwapToCompactWindow);
                return;
            }

            AppConfig.Instance.CompactMode = true;
            AppConfig.Instance.Save();

            Point? anchor = null;
            if (_mainWindow != null && !_mainWindow.IsDisposed && _mainWindow.Visible && _mainWindow.WindowState != FormWindowState.Minimized)
            {
                anchor = new Point(_mainWindow.Right, _mainWindow.Top);
                _mainWindow.Hide();
            }

            OpenCompactWindow(anchor);
        }

        private void SwapToFullWindow()
        {
            if (_disposed) return;
            if (_menu.InvokeRequired)
            {
                if (_disposed || _menu.IsDisposed || !_menu.IsHandleCreated) return;
                _menu.BeginInvoke(SwapToFullWindow);
                return;
            }

            AppConfig.Instance.CompactMode = false;
            AppConfig.Instance.Save();

            if (_compactWindow != null && !_compactWindow.IsDisposed && _compactWindow.Visible)
                _compactWindow.Hide();

            OpenMainWindow("sync", forceFull: true);
        }

        private void OpenMainWindow(string tabId = "sync", bool forceFull = false)
        {
            if (_disposed) return;
            if (_menu.InvokeRequired)
            {
                if (_disposed || _menu.IsDisposed || !_menu.IsHandleCreated) return;
                _menu.BeginInvoke(() => OpenMainWindow(tabId, forceFull));
                return;
            }

            if (!forceFull && tabId == "sync" && AppConfig.Instance.CompactMode)
            {
                OpenCompactWindow();
                return;
            }

            if (_compactWindow != null && !_compactWindow.IsDisposed && _compactWindow.Visible)
            {
                _compactWindow.Hide();
                if (AppConfig.Instance.CompactMode)
                {
                    AppConfig.Instance.CompactMode = false;
                    AppConfig.Instance.Save();
                }
            }

            if (_mainWindow == null || _mainWindow.IsDisposed)
            {
                _mainWindow = new RelayMainWindow(_watcher, ApplyUpdate, SwapToCompactWindow, UpdateStartupText, UpdateSilentSyncText);
            }

            _mainWindow.NavigateToTab(tabId);
            ActivateMainWindow();
        }

        private void OpenAssistantDialog()
        {
            if (_disposed) return;
            if (_menu.InvokeRequired)
            {
                if (_disposed || _menu.IsDisposed || !_menu.IsHandleCreated) return;
                _menu.BeginInvoke(OpenAssistantDialog);
                return;
            }

            using var dlg = new RelayAssistantForm(_watcher);
            if (_mainWindow != null && !_mainWindow.IsDisposed && _mainWindow.Visible)
                dlg.ShowDialog(_mainWindow);
            else if (_compactWindow != null && !_compactWindow.IsDisposed && _compactWindow.Visible)
                dlg.ShowDialog(_compactWindow);
            else
                dlg.ShowDialog();
        }
        
        private void OnJobsChanged()
        {
            if (_disposed) return;
            if (_uiContext != null)
            {
                _uiContext.Post(_ => HandleJobsChangedOnUI(), null);
            }
            else if (_menu != null && !_menu.IsDisposed && _menu.IsHandleCreated && _menu.InvokeRequired)
            {
                _menu.BeginInvoke(new Action(HandleJobsChangedOnUI));
            }
            else if (!_disposed)
            {
                HandleJobsChangedOnUI();
            }
        }

        private void HandleJobsChangedOnUI()
        {
            if (_disposed) return;
            // [Req 1 & Req 5] Delay slightly to group alerts together and prevent spam
            if (_batchAlertTimer == null)
            {
                _batchAlertTimer = new System.Windows.Forms.Timer { Interval = 2000 };
                _batchAlertTimer.Tick += (_, _) => 
                { 
                    _batchAlertTimer.Stop(); 
                    CheckBatchCompletion();
                };
            }
            _batchAlertTimer.Stop();
            _batchAlertTimer.Start();
        }

        private void CheckBatchCompletion()
        {
            if (_disposed) return;
            try
            {
                var jobs = _watcher.GetJobsSnapshot();
                if (jobs.Length == 0) { _prevActiveCount = 0; return; }

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
                    if (!AppConfig.Instance.SilentSync)
                    {
                        ShowCustomAlert("Transmission Initiated", $"Fissal is syncing {names} to the Redfur lattice!", Color.FromArgb(200, 160, 60), 6, 4000, () => OpenMainWindow("sync"));
                    }
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
                            Color.FromArgb(180, 100, 220), 4, 10000, () => OpenMainWindow("sync"));
                    }
                    else if (hasFailedUpdate)
                    {
                        ShowAlert("Update Interrupted", 
                            "Fissal's claws slipped while pulling the new module!\n\nCheck diagnostics for details!", 
                            FissalAlert.AlertLevel.TotalError, 9000, () => OpenMainWindow("diagnostics"));
                    }
                    else if (_batchHadSuccess || _batchHadError)
                    {
                        if (_batchHadError)
                        {
                            string msg = $"[SYNC SUMMARY]\n\n" +
                                         $"✦ Verified: {totalSynced} files\n" +
                                         $"✖ Errors: {totalErrors} files\n\n" +
                                         $"Interference detected! Open diagnostics to review log anomalies.";
                            if (!AppConfig.Instance.SilentSync)
                            {
                                ShowAlert("Sync Completed With Errors", msg, FissalAlert.AlertLevel.TotalError, 10000, () => OpenMainWindow("diagnostics"));
                            }
                        }
                        else if (_batchHadSuccess)
                        {
                            string msg = $"[SYNC SUMMARY]\n\n" +
                                         $"✦ Verified: {totalSynced} files\n" +
                                         $"✦ Errors: 0\n\n" +
                                         $"All data securely delivered to the lattice!";
                            if (!AppConfig.Instance.SilentSync)
                            {
                                ShowCustomAlert("Sync Complete!", msg, Color.FromArgb(60, 180, 220), 6, 6000, () => OpenMainWindow("sync"));
                            }
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

            _silentSyncItem = new ToolStripMenuItem((AppConfig.Instance.SilentSync ? Checked : Unchecked) + "Silent Background Sync")
            {
                ToolTipText = "Mute routine popups and chimes during automatic background file syncs."
            };
            _silentSyncItem.Click += (_, _) =>
            {
                var cfg = AppConfig.Instance;
                bool nowSilent = !cfg.SilentSync;
                cfg.SilentSync = nowSilent;
                cfg.Save();
                UpdateSilentSyncText(nowSilent);
            };
            menu.Items.Add(_silentSyncItem);
            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add("⚡  Open Relay Terminal",     null, (_, _) => OpenMainWindow("sync", forceFull: true));
            menu.Items.Add("💬  Ask Fissal...",          null, (_, _) => OpenAssistantDialog());
            menu.Items.Add("◱  Toggle Compact Mode",     null, (_, _) => {
                if (AppConfig.Instance.CompactMode)
                    SwapToFullWindow();
                else
                    SwapToCompactWindow();
            });
            menu.Items.Add("🛠️  Setup & Pairing",         null, (_, _) => OpenMainWindow("setup", forceFull: true));
            menu.Items.Add("🎨  Terminal Themes",         null, (_, _) => OpenMainWindow("themes", forceFull: true));
            menu.Items.Add("⚙️  Diagnostics",             null, (_, _) => OpenMainWindow("diagnostics", forceFull: true));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("🔌  Cut Connection",          null, OnShutdown);

            return menu;
        }

        private void SetPerformanceMode(FidelityMode mode, bool saveConfig)
        {
            UploadProgressForm.AppConfig.SetMode(mode);
            _compactWindow?.ApplyAnimationInterval();
            _mainWindow?.UpdateIdleTimerInterval();

            _perfLowItem.Text  = (mode == FidelityMode.Low ? Checked : Unchecked) + "Minimal (Low FX)";
            _perfMedItem.Text  = (mode == FidelityMode.Medium ? Checked : Unchecked) + "Balanced";
            _perfHighItem.Text = (mode == FidelityMode.High ? Checked : Unchecked) + "Full Glow (Max FX)";

            if (saveConfig)
            {
                var config = AppConfig.Instance;
                config.VisualFidelity = mode;
                config.Save();
            }
        }

        private void UpdateStartupText(bool on)
        {
            if (_disposed) return;
            if (_menu.InvokeRequired)
            {
                if (_disposed || _menu.IsDisposed || !_menu.IsHandleCreated) return;
                _menu.BeginInvoke(() => UpdateStartupText(on));
                return;
            }
            _startupItem.Text = (on ? Checked : Unchecked) + "Run Fissal on startup";
            _mainWindow?.RefreshPreferencesControls();
        }

        private void UpdateSilentSyncText(bool silent)
        {
            if (_disposed) return;
            if (_menu.InvokeRequired)
            {
                if (_disposed || _menu.IsDisposed || !_menu.IsHandleCreated) return;
                _menu.BeginInvoke(() => UpdateSilentSyncText(silent));
                return;
            }
            if (_silentSyncItem != null)
                _silentSyncItem.Text = (silent ? Checked : Unchecked) + "Silent Background Sync";
            _mainWindow?.RefreshPreferencesControls();
        }

        private void UpdateStatus(string msg)
        {
            if (_disposed) return;
            if (_menu.InvokeRequired)
            {
                if (_disposed || _menu.IsDisposed || !_menu.IsHandleCreated) return;
                _menu.BeginInvoke(() => UpdateStatus(msg));
                return;
            }
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
            if (_disposed) return;
            if (_menu.InvokeRequired)
            {
                if (_disposed || _menu.IsDisposed || !_menu.IsHandleCreated) return;
                _menu.BeginInvoke(() => ShowAlert(title, text, level, timeoutMs, onClick));
                return;
            }
            FissalAlert.Show(title, text, level, timeoutMs, onClick);
        }

        private void ShowCustomAlert(string title, string text, Color lightColor, int flashSpeed, int timeoutMs = 7000, Action? onClick = null)
        {
            if (_disposed) return;
            if (_menu.InvokeRequired)
            {
                if (_disposed || _menu.IsDisposed || !_menu.IsHandleCreated) return;
                _menu.BeginInvoke(() => ShowCustomAlert(title, text, lightColor, flashSpeed, timeoutMs, onClick));
                return;
            }
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

        private bool _applyingUpdate;
        private async void ApplyUpdate(UploadJob job)
        {
            if (_applyingUpdate) return;

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

            _applyingUpdate = true;
            UpdateStatus("Applying update & verifying health...");

            try
            {
                long seq = AppConfig.Instance.LastVerifiedSequence;
                string fromVer = RelayVersion.Current;
                string toVer = job.UpdateVersion;
                string stagedPath = job.FilePath;

                // Run handshake off the UI thread so the message pump stays responsive during the 30s health check
                var outcome = await System.Threading.Tasks.Task.Run(() =>
                {
                    var installer = new UpdateInstaller(new PhysicalUpdateFileSystem(), path =>
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = path,
                            Arguments = "--post-update-verify",
                            UseShellExecute = true,
                            WorkingDirectory = Path.GetDirectoryName(path)
                        };
                        Process.Start(psi);
                    });
                    return installer.ApplyWithHandshake(
                        exePath,
                        stagedPath,
                        sequence: seq,
                        fromVersion: fromVer,
                        toVersion: toVer,
                        handshakeTimeout: TimeSpan.FromSeconds(30));
                });

                if (!outcome.Ok)
                {
                    _applyingUpdate = false;
                    ShowAlert("Update Failed!", $"Fissal's claws slipped: {outcome.Message}", FissalAlert.AlertLevel.TotalError);
                    return;
                }

                _trayIcon.Visible = false;
                Program.SafeReleaseMutex();
                Application.Exit();
            }
            catch (Exception ex)
            {
                _applyingUpdate = false;
                ShowAlert("Update Failed!", $"Fissal's claws slipped: {ex.Message}", FissalAlert.AlertLevel.TotalError);
            }
        }

        private void OpenProgressForm()
        {
            if (_disposed) return;
            if (_menu.InvokeRequired)
            {
                if (_disposed || _menu.IsDisposed || !_menu.IsHandleCreated) return;
                _menu.BeginInvoke(OpenProgressForm);
                return;
            }
            OpenMainWindow("sync");
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
                    using var fillBrush = new SolidBrush(Color.FromArgb(22, 17, 10));
                    g.FillEllipse(fillBrush, 2, 2, 60, 60);
                }
            }
            catch { }

            using var ring = new Pen(Color.FromArgb(160, 128, 48), 2.5f);
            g.DrawEllipse(ring, 2, 2, 60, 60);

            IntPtr hIcon = bmp.GetHicon();
            try
            {
                using var tempIcon = Icon.FromHandle(hIcon);
                return (Icon)tempIcon.Clone();
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _watcher.JobsChanged       -= OnJobsChanged;
            _watcher.ConnectionChecked -= OnConnectionChecked;
            _watcher.Dispose();

            _wakeRegistration?.Unregister(null);
            _wakeEvent?.Dispose();

            _batchAlertTimer?.Stop();
            _batchAlertTimer?.Dispose();
            _compactWindow?.Dispose();
            _mainWindow?.Dispose();
            _trayIcon.Dispose();
            _menu.Dispose();
        }
    }

    internal sealed class FissalMenuRenderer : ToolStripRenderer
    {
        // Derived from FissalTheme — single source of truth (tokens.css parity).
        private static Color CBg    => FissalTheme.CPanelBg;
        private static Color CHover => FissalTheme.CBarBg;
        private static Color CText  => FissalTheme.CText;
        private static Color CDim   => FissalTheme.CTextSub;
        private static Color CGold  => FissalTheme.CGoldBrt;
        private static Color CSep   => FissalTheme.CSep;
        private static Color CBord  => FissalTheme.CBorder;
        private static Color CDmnd  => FissalTheme.CGoldDim;

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