using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using static RedfurSync.FissalTheme;

namespace RedfurSync
{
    public sealed class RelayMainWindow : Form
    {
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private readonly FileWatcherService _watcher;
        private readonly Action<UploadJob> _applyUpdateAction;
        private float _scale = 1f;

        // Top-level layout
        private TableLayoutPanel _rootLayout = null!;
        private Panel _titleBar = null!;
        private Label _titleMarkLabel = null!;
        private Label _titleTextLabel = null!;
        private Label _titleThemeBadge = null!;
        private Label _titleStatusLabel = null!;
        private Label _messageBoardLabel = null!;
        private Label _powerLampLabel = null!;
        private Label _signalLampLabel = null!;
        private Label _transferLampLabel = null!;
        private Panel _navRail = null!;
        private Panel _contentHost = null!;

        // Nav buttons
        private readonly List<(string id, Button btn, Panel indicator, Panel viewPanel)> _navItems = new();
        private string _activeTabId = "sync";

        // View Panels
        private Panel _syncView = null!;
        private Panel _assistantView = null!;
        private Panel _setupView = null!;
        private Panel _themesView = null!;
        private Panel _diagnosticsView = null!;

        // ── 1. Sync View Controls ──
        private FlowLayoutPanel _syncJobsList = null!;
        private Label _syncSummaryLabel = null!;
        private Label _syncStateBadge = null!;
        private Button _btnRefreshJobs = null!;
        private Button _btnClearCompleted = null!;
        private readonly Dictionary<UploadJob, JobCardControls> _jobCards = new();
        private bool _syncRefreshPending;

        private sealed class JobCardControls
        {
            public Panel Card { get; init; } = null!;
            public Label StatusLabel { get; init; } = null!;
            public Label DetailLabel { get; init; } = null!;
        }

        // ── 2. Ask Fissal Controls ──
        private FlowLayoutPanel _transcript = null!;
        private TextBox _prompt = null!;
        private Button _send = null!;
        private Label _assistantStatus = null!;
        private Label _assistantModelLabel = null!;
        private CheckBox _harnessCheckBox = null!;
        private CheckBox _writePermsCheckBox = null!;
        private readonly List<(string role, string text)> _chatHistory = new();
        private readonly FissalHarnessService _harnessService = new(AppConfig.Instance);

        // ── 3. Setup Controls ──
        private TextBox _txtDisplayName = null!;
        private TextBox _txtPairingCode = null!;
        private TextBox _txtServerUrl = null!;
        private Label _lblPairingStatus = null!;
        private Label _lblDeviceInfo = null!;
        private Button _btnPairDevice = null!;
        private Button _btnSaveSetup = null!;
        private Button _btnTestConnection = null!;

        // ── 4. Themes View Controls ──
        private FlowLayoutPanel _themeCardsHost = null!;
        private ComboBox _fidelityCombo = null!;
        private TrackBar _scaleTrackBar = null!;
        private Label _scaleValueLabel = null!;

        // ── 5. Diagnostics Controls ──
        private RichTextBox _diagLogBox = null!;
        private Label _watcherStatusLabel = null!;
        private Label _esoPathLabel = null!;
        private Label _configPathLabel = null!;
        private Button _btnOpenConfigDir = null!;
        private Button _btnOpenConfigFile = null!;
        private Button _btnRestartWatcher = null!;

        public RelayMainWindow(FileWatcherService watcher, Action<UploadJob> applyUpdateAction)
        {
            _watcher = watcher;
            _applyUpdateAction = applyUpdateAction;

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(880, 580);
            Size = new Size(1040, 680);
            BackColor = CBg;
            ForeColor = CText;
            ShowInTaskbar = true;
            Text = "Fissal Relay // Dwemer Tonal Terminal";

            _scale = GetScale(Handle);

            BuildShell();
            BuildViewPanels();
            SwitchTab("sync");

            _watcher.JobsChanged += OnWatcherJobsChanged;
            _watcher.ConnectionChecked += OnWatcherConnectionChecked;
            FissalTheme.ThemeChanged += OnGlobalThemeChanged;

            Shown += (_, _) =>
            {
                RefreshAllViews();
                if (_transcript.Controls.Count == 0)
                {
                    AddAssistantMessage(false, "Purrs! Fissal's Tonal Terminal is active. I can inspect your live sync queue, check ESO data directories, explain log entries, or tune relay settings.");
                }
            };

            FormClosing += (s, e) =>
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Hide(); // Minimize to system tray
                }
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _watcher.JobsChanged -= OnWatcherJobsChanged;
                _watcher.ConnectionChecked -= OnWatcherConnectionChecked;
                FissalTheme.ThemeChanged -= OnGlobalThemeChanged;
            }
            base.Dispose(disposing);
        }

        public void NavigateToTab(string tabId)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => NavigateToTab(tabId));
                return;
            }
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            SwitchTab(tabId);
        }

        // ── Window Resizing & Frame Handling ─────────────────────────────────
        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int RESIZE_GRIP = 8;
            base.WndProc(ref m);

            if (m.Msg == WM_NCHITTEST && WindowState != FormWindowState.Maximized)
            {
                var pt = PointToClient(new Point(m.LParam.ToInt32()));
                bool left = pt.X <= RESIZE_GRIP;
                bool right = pt.X >= ClientSize.Width - RESIZE_GRIP;
                bool top = pt.Y <= RESIZE_GRIP;
                bool bottom = pt.Y >= ClientSize.Height - RESIZE_GRIP;

                if (left && top) m.Result = (IntPtr)13;
                else if (right && top) m.Result = (IntPtr)14;
                else if (left && bottom) m.Result = (IntPtr)16;
                else if (right && bottom) m.Result = (IntPtr)17;
                else if (left) m.Result = (IntPtr)10;
                else if (right) m.Result = (IntPtr)11;
                else if (top) m.Result = (IntPtr)12;
                else if (bottom) m.Result = (IntPtr)15;
            }
        }

        private void BuildShell()
        {
            _rootLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                BackColor = CBg,
                Margin = new Padding(0),
                Padding = new Padding(10),
            };
            _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(210 * _scale)));
            _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(44 * _scale)));
            _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(56 * _scale)));
            _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // ── Titlebar (spans both columns) ──
            _titleBar = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CPanelBg,
                Margin = new Padding(0),
                Padding = new Padding(12, 0, 8, 0),
            };
            _titleBar.MouseDown += OnTitleBarMouseDown;

            var titleLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 7,
                RowCount = 1,
                BackColor = Color.Transparent,
            };
            titleLayout.MouseDown += OnTitleBarMouseDown;
            titleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // Mark
            titleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // Title
            titleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // Theme badge
            titleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // Status spacer
            titleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36)); // Min
            titleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36)); // Max/Restore
            titleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36)); // Close

            _titleMarkLabel = new Label
            {
                Text = ThemeMark + " ",
                ForeColor = CGoldBrt,
                Font = Title(12f, _scale, FontStyle.Bold),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
            };
            _titleMarkLabel.MouseDown += OnTitleBarMouseDown;
            titleLayout.Controls.Add(_titleMarkLabel, 0, 0);

            _titleTextLabel = new Label
            {
                Text = "FISSAL TONAL RELAY",
                ForeColor = CGoldBrt,
                Font = Title(10f, _scale, FontStyle.Bold),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, 10, 0),
            };
            _titleTextLabel.MouseDown += OnTitleBarMouseDown;
            titleLayout.Controls.Add(_titleTextLabel, 1, 0);

            _titleThemeBadge = new Label
            {
                Text = $"[{Current.DisplayName.ToUpperInvariant()}]",
                ForeColor = CGreen,
                Font = Mono(8f, _scale, FontStyle.Bold),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, 10, 0),
            };
            _titleThemeBadge.MouseDown += OnTitleBarMouseDown;
            titleLayout.Controls.Add(_titleThemeBadge, 2, 0);

            _titleStatusLabel = new Label
            {
                Text = "● LATTICE ACTIVE",
                ForeColor = CTextSub,
                Font = Mono(8f, _scale),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
            };
            _titleStatusLabel.MouseDown += OnTitleBarMouseDown;
            titleLayout.Controls.Add(_titleStatusLabel, 3, 0);

            var btnMin = MakeTitleButton("_", "Minimize", (_, _) => WindowState = FormWindowState.Minimized);
            var btnMax = MakeTitleButton("□", "Maximize/Restore", (_, _) =>
            {
                WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            });
            var btnClose = MakeTitleButton("✕", "Hide to Tray", (_, _) => Hide());
            btnClose.ForeColor = CBarFail;

            titleLayout.Controls.Add(btnMin, 4, 0);
            titleLayout.Controls.Add(btnMax, 5, 0);
            titleLayout.Controls.Add(btnClose, 6, 0);

            _titleBar.Controls.Add(titleLayout);
            _rootLayout.Controls.Add(_titleBar, 0, 0);
            _rootLayout.SetColumnSpan(_titleBar, 2);

            var instrumentPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = CPanelBgAlt,
                Margin = new Padding(0, 5, 0, 7),
                Padding = new Padding(12, 7, 12, 7),
            };
            instrumentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            instrumentPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(230 * _scale)));

            _messageBoardLabel = new Label
            {
                Text = "FISSAL // TONAL LATTICE READY",
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(3, 12, 7),
                ForeColor = CGreen,
                Font = Mono(9f, _scale, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 10, 0),
                BorderStyle = BorderStyle.FixedSingle,
            };
            instrumentPanel.Controls.Add(_messageBoardLabel, 0, 0);

            var lamps = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Padding = new Padding(8, 7, 0, 0),
            };
            _powerLampLabel = MakeStatusLamp("POWER", CGreen);
            _signalLampLabel = MakeStatusLamp("SIGNAL", CTextSub);
            _transferLampLabel = MakeStatusLamp("SEND", CTextSub);
            lamps.Controls.Add(_powerLampLabel);
            lamps.Controls.Add(_signalLampLabel);
            lamps.Controls.Add(_transferLampLabel);
            instrumentPanel.Controls.Add(lamps, 1, 0);
            _rootLayout.Controls.Add(instrumentPanel, 0, 1);
            _rootLayout.SetColumnSpan(instrumentPanel, 2);

            // ── Content Area Host ──
            _contentHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CBg,
                Margin = new Padding(0),
                Padding = new Padding(12),
            };
            _rootLayout.Controls.Add(_contentHost, 1, 2);

            // ── Navigation Rail ──
            _navRail = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CPanelBg,
                Margin = new Padding(0),
                Padding = new Padding(0, 8, 0, 8),
            };

            var navStack = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = false,
                BackColor = Color.Transparent,
            };

            AddNavButton(navStack, "sync",        "⚡ Live Sync & Logs", "Live file sync monitor and batch history");
            AddNavButton(navStack, "assistant",   "💬 Ask Fissal",       "AI-powered relay diagnostics and assistance");
            AddNavButton(navStack, "setup",       "🛠️ Setup & Pairing",  "Device token, display name, and pairing code");
            AddNavButton(navStack, "themes",      "🎨 Themes & Display", "11 Terminal color palettes and UI scaling");
            AddNavButton(navStack, "diagnostics", "⚙️ Diagnostics",      "Watcher status, log viewers, and debug controls");

            _navRail.Controls.Add(navStack);
            _rootLayout.Controls.Add(_navRail, 0, 2);

            Controls.Add(_rootLayout);

            Paint += (_, g) =>
            {
                using var borderPen = new Pen(CBorder, 1);
                g.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
                DrawCornerRivets(g.Graphics, Width, Height, 5, CGoldDim);
            };
        }

        private Label MakeStatusLamp(string label, Color color)
        {
            return new Label
            {
                Text = $"● {label}",
                ForeColor = color,
                Font = Mono(7.5f, _scale, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(5, 0, 5, 0),
            };
        }

        private Button MakeTitleButton(string text, string tooltip, EventHandler onClick)
        {
            var btn = new Button
            {
                Text = text,
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = CTextSub,
                Font = Mono(9f, _scale, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(2),
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = CBarBg;
            btn.Click += onClick;
            return btn;
        }

        private void AddNavButton(FlowLayoutPanel container, string id, string title, string description)
        {
            int btnWidth = (int)(210 * _scale);
            int btnHeight = (int)(52 * _scale);

            var itemPanel = new Panel
            {
                Width = btnWidth,
                Height = btnHeight,
                Margin = new Padding(0, 2, 0, 2),
                BackColor = Color.Transparent,
            };

            var indicator = new Panel
            {
                Dock = DockStyle.Left,
                Width = (int)(4 * _scale),
                BackColor = Color.Transparent,
            };

            var btn = new Button
            {
                Dock = DockStyle.Fill,
                Text = "  " + title,
                TextAlign = ContentAlignment.MiddleLeft,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = CTextSub,
                Font = Body(9f, _scale, FontStyle.Regular),
                Cursor = Cursors.Hand,
                Margin = new Padding(0),
                Padding = new Padding(8, 0, 0, 0),
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = CBarBg;

            var viewPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CBg,
                Visible = false,
            };

            btn.Click += (_, _) => SwitchTab(id);

            itemPanel.Controls.Add(btn);
            itemPanel.Controls.Add(indicator);
            container.Controls.Add(itemPanel);

            _navItems.Add((id, btn, indicator, viewPanel));
            _contentHost.Controls.Add(viewPanel);
        }

        private void SwitchTab(string id)
        {
            _activeTabId = id;
            foreach (var item in _navItems)
            {
                bool active = item.id == id;
                item.btn.ForeColor = active ? CGoldBrt : CTextSub;
                item.btn.Font = Body(9.5f, _scale, active ? FontStyle.Bold : FontStyle.Regular);
                item.btn.BackColor = active ? CBarBg : Color.Transparent;
                item.indicator.BackColor = active ? CGreen : Color.Transparent;
                item.viewPanel.Visible = active;
                if (active) item.viewPanel.BringToFront();
            }

            if (id == "sync") RefreshSyncView();
            else if (id == "diagnostics") RefreshDiagnosticsView();
            else if (id == "setup") RefreshSetupView();
        }

        private void BuildViewPanels()
        {
            _syncView = _navItems.First(x => x.id == "sync").viewPanel;
            _assistantView = _navItems.First(x => x.id == "assistant").viewPanel;
            _setupView = _navItems.First(x => x.id == "setup").viewPanel;
            _themesView = _navItems.First(x => x.id == "themes").viewPanel;
            _diagnosticsView = _navItems.First(x => x.id == "diagnostics").viewPanel;

            InitSyncView();
            InitAssistantView();
            InitSetupView();
            InitThemesView();
            InitDiagnosticsView();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 1. LIVE SYNC & LOGS VIEW
        // ═════════════════════════════════════════════════════════════════════
        private void InitSyncView()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(48 * _scale)));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(42 * _scale)));

            // Top Header Bar
            var topBar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = CPanelBg,
                Padding = new Padding(10, 6, 10, 6),
            };
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // Badge
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // Summary
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // Refresh
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // Clear

            _syncStateBadge = new Label
            {
                Text = "⚡ SYNC STREAM",
                ForeColor = CGreen,
                Font = Title(10f, _scale, FontStyle.Bold),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
            };
            topBar.Controls.Add(_syncStateBadge, 0, 0);

            _syncSummaryLabel = new Label
            {
                Text = "Ready — Monitoring ESO Sales Data",
                ForeColor = CText,
                Font = Body(8.5f, _scale),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(12, 0, 0, 0),
            };
            topBar.Controls.Add(_syncSummaryLabel, 1, 0);

            _btnRefreshJobs = MakeStyledButton("Refresh", CGreen);
            _btnRefreshJobs.Click += (_, _) => RefreshSyncView();
            topBar.Controls.Add(_btnRefreshJobs, 2, 0);

            _btnClearCompleted = MakeStyledButton("Clear Done", CTextSub);
            _btnClearCompleted.Click += (_, _) =>
            {
                var doneJobs = _watcher.Jobs.Where(j => j.Status is UploadStatus.Done or UploadStatus.Cancelled).ToList();
                foreach (var j in doneJobs) _watcher.Jobs.Remove(j);
                RefreshSyncView();
            };
            topBar.Controls.Add(_btnClearCompleted, 3, 0);

            layout.Controls.Add(topBar, 0, 0);

            // Center Scrollable Jobs List
            _syncJobsList = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.FromArgb(12, 10, 7),
                Padding = new Padding(8),
            };
            _syncJobsList.Resize += (_, _) => ResizeSyncJobCards();
            layout.Controls.Add(_syncJobsList, 0, 1);

            // Bottom Footer
            var footer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CPanelBg,
                Padding = new Padding(10, 8, 10, 8),
            };
            var hint = new Label
            {
                Text = "Files in your ESO SavedVariables directory are automatically detected, encrypted, and uploaded.",
                ForeColor = CTextSub,
                Font = Body(8f, _scale),
                Dock = DockStyle.Fill,
            };
            footer.Controls.Add(hint);
            layout.Controls.Add(footer, 0, 2);

            _syncView.Controls.Add(layout);
        }

        private void RefreshSyncView()
        {
            if (InvokeRequired)
            {
                BeginInvoke(RefreshSyncView);
                return;
            }

            var jobs = _watcher.Jobs.ToList();
            int queued = jobs.Count(j => j.Status == UploadStatus.Queued);
            int uploading = jobs.Count(j => j.Status == UploadStatus.Uploading);
            int done = jobs.Count(j => j.Status == UploadStatus.Done);
            int failed = jobs.Count(j => j.Status is UploadStatus.Failed or UploadStatus.Cancelled);

            if (uploading > 0)
            {
                _syncStateBadge.Text = "⚡ TRANSMITTING...";
                _syncStateBadge.ForeColor = CGoldBrt;
                _messageBoardLabel.Text = $"TRANSMITTING // {uploading} ACTIVE // {queued} QUEUED";
                _transferLampLabel.ForeColor = CGoldBrt;
            }
            else if (queued > 0)
            {
                _syncStateBadge.Text = "⏳ QUEUED";
                _syncStateBadge.ForeColor = CWarn;
                _messageBoardLabel.Text = $"SIGNAL QUEUED // {queued} FILE{(queued == 1 ? "" : "S")}";
                _transferLampLabel.ForeColor = CWarn;
            }
            else
            {
                _syncStateBadge.Text = "⚡ SYNC IDLE";
                _syncStateBadge.ForeColor = CGreen;
                _messageBoardLabel.Text = failed > 0 ? $"INTERFERENCE // {failed} LOG ALERT{(failed == 1 ? "" : "S")}" : "FISSAL // TONAL LATTICE READY";
                _transferLampLabel.ForeColor = failed > 0 ? CBarFail : CTextSub;
            }

            _syncSummaryLabel.Text = $"Active: {uploading} | Queued: {queued} | Verified: {done} | Errors: {failed} | Total Tracked: {jobs.Count}";

            bool structureChanged = jobs.Count != _jobCards.Count || jobs.Any(job => !_jobCards.ContainsKey(job));
            if (!structureChanged)
            {
                foreach (var job in jobs)
                {
                    UpdateJobCard(job, _jobCards[job]);
                }
                return;
            }

            _syncJobsList.SuspendLayout();
            _syncJobsList.Controls.Clear();
            _jobCards.Clear();

            if (jobs.Count == 0)
            {
                _syncJobsList.Controls.Add(new Label
                {
                    Text = "No active or recent file transmissions.\nNew sales data in SavedVariables will appear here instantly.",
                    ForeColor = CTextSub,
                    Font = Body(9.5f, _scale, FontStyle.Italic),
                    AutoSize = true,
                    Margin = new Padding(16, 24, 16, 16),
                });
            }
            else
            {
                foreach (var job in jobs.OrderByDescending(j => j.QueuedAt))
                {
                    var controls = BuildJobCard(job);
                    _jobCards.Add(job, controls);
                    _syncJobsList.Controls.Add(controls.Card);
                }
            }

            ResizeSyncJobCards();
            _syncJobsList.ResumeLayout(true);
        }

        private JobCardControls BuildJobCard(UploadJob job)
        {
            var card = new Panel
            {
                BackColor = CPanelBg,
                Padding = new Padding(10, 8, 10, 8),
                Margin = new Padding(0, 0, 0, 6),
                Tag = "sync-card",
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 2,
                BackColor = Color.Transparent,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45)); // File name
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25)); // Status & Progress
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30)); // Details
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));   // Action Buttons

            // File Name
            var nameLabel = new Label
            {
                Text = (job.IsUpdate ? "📦 " : "📄 ") + job.FileName,
                ForeColor = job.IsUpdate ? Color.FromArgb(196, 137, 255) : CGoldBrt,
                Font = Mono(9f, _scale, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            layout.Controls.Add(nameLabel, 0, 0);

            // Time & Size
            string sizeStr = job.FileSizeDisplay;
            var timeLabel = new Label
            {
                Text = $"{job.QueuedAt:HH:mm:ss} • {sizeStr}",
                ForeColor = CTextSub,
                Font = Mono(7.5f, _scale),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            layout.Controls.Add(timeLabel, 0, 1);

            // Status Badge
            var statusLabel = new Label
            {
                Font = Mono(8.5f, _scale, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            layout.Controls.Add(statusLabel, 1, 0);

            // Status progress or error detail
            var detailLabel = new Label
            {
                Font = Body(8f, _scale),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            layout.Controls.Add(detailLabel, 2, 0);
            layout.SetRowSpan(detailLabel, 2);

            // Actions (Retry / Cancel / Apply)
            var actionFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                Anchor = AnchorStyles.Right,
            };

            if (job.Status == UploadStatus.UpdateReady)
            {
                var btnApply = MakeStyledButton("Apply Upgrade", Color.FromArgb(196, 137, 255));
                btnApply.Click += (_, _) => _applyUpdateAction(job);
                actionFlow.Controls.Add(btnApply);
            }
            else if (job.Status == UploadStatus.Failed)
            {
                var btnRetry = MakeStyledButton("Retry", CGoldBrt);
                btnRetry.Click += (_, _) => _watcher.RetryJob(job);
                actionFlow.Controls.Add(btnRetry);
            }
            else if (job.Status is UploadStatus.Uploading or UploadStatus.Queued)
            {
                var btnCancel = MakeStyledButton("Cancel", CBarFail);
                btnCancel.Click += (_, _) => _watcher.CancelJob(job);
                actionFlow.Controls.Add(btnCancel);
            }

            layout.Controls.Add(actionFlow, 3, 0);
            layout.SetRowSpan(actionFlow, 2);

            card.Controls.Add(layout);
            card.Height = (int)(54 * _scale);
            var controls = new JobCardControls { Card = card, StatusLabel = statusLabel, DetailLabel = detailLabel };
            UpdateJobCard(job, controls);
            return controls;
        }

        private static void UpdateJobCard(UploadJob job, JobCardControls controls)
        {
            controls.StatusLabel.Text = job.Status switch
            {
                UploadStatus.Uploading => $"UPLOADING ({(int)(job.Progress * 100)}%)",
                UploadStatus.UpdateReady => "UPGRADE READY",
                _ => job.Status.ToString().ToUpperInvariant()
            };
            controls.StatusLabel.ForeColor = job.Status switch
            {
                UploadStatus.Done => CGreen,
                UploadStatus.Uploading => CGoldBrt,
                UploadStatus.Queued => CWarn,
                UploadStatus.UpdateReady => Color.FromArgb(196, 137, 255),
                UploadStatus.Failed => CBarFail,
                UploadStatus.Cancelled => CTextSub,
                _ => CText
            };
            controls.DetailLabel.Text = string.IsNullOrWhiteSpace(job.ErrorMessage)
                ? job.Status == UploadStatus.Done ? "Lattice Verified" : ""
                : job.ErrorMessage;
            controls.DetailLabel.ForeColor = string.IsNullOrWhiteSpace(job.ErrorMessage) ? CTextSub : CBarFail;
        }

        private void ResizeSyncJobCards()
        {
            int targetWidth = Math.Max(400, _syncJobsList.ClientSize.Width - 24);
            foreach (Control ctrl in _syncJobsList.Controls)
            {
                if (Equals(ctrl.Tag, "sync-card"))
                {
                    ctrl.Width = targetWidth;
                }
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // 2. EMBEDDED ASK FISSAL TERMINAL
        // ═════════════════════════════════════════════════════════════════════
        private void InitAssistantView()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(48 * _scale))); // Header & Harness
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(38 * _scale))); // Quick Action chips
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));                 // Chat transcript
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(72 * _scale))); // Input composer
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(28 * _scale))); // Status / shortcuts

            // Header
            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = CPanelBg,
                Padding = new Padding(10, 6, 10, 6),
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var brandLabel = new Label
            {
                Text = "ASK FISSAL AI",
                ForeColor = CGoldBrt,
                Font = Title(11f, _scale, FontStyle.Bold),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
            };
            header.Controls.Add(brandLabel, 0, 0);

            _assistantModelLabel = new Label
            {
                Text = "● READY",
                ForeColor = CGreen,
                Font = Mono(8f, _scale, FontStyle.Bold),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(10, 0, 0, 0),
            };
            header.Controls.Add(_assistantModelLabel, 1, 0);

            _harnessCheckBox = new CheckBox
            {
                Text = "Fissal Harness",
                ForeColor = CText,
                Font = Body(8.5f, _scale),
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                Checked = AppConfig.Instance.FissalHarnessEnabled,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(0, 0, 10, 0),
            };
            _harnessCheckBox.CheckedChanged += (_, _) =>
            {
                AppConfig.Instance.FissalHarnessEnabled = _harnessCheckBox.Checked;
                if (!_harnessCheckBox.Checked) _writePermsCheckBox.Checked = false;
                AppConfig.Instance.Save();
                _writePermsCheckBox.Enabled = _harnessCheckBox.Checked;
                _assistantStatus.Text = _harnessCheckBox.Checked ? "Harness enabled. Read diagnostics will accompany requests." : "Harness disabled.";
            };
            header.Controls.Add(_harnessCheckBox, 2, 0);

            _writePermsCheckBox = new CheckBox
            {
                Text = "Allow Setting Changes",
                ForeColor = CWarn,
                Font = Body(8.5f, _scale),
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                Checked = AppConfig.Instance.FissalHarnessEnabled && AppConfig.Instance.FissalWritePermissions,
                Enabled = AppConfig.Instance.FissalHarnessEnabled,
                Anchor = AnchorStyles.Right,
            };
            _writePermsCheckBox.CheckedChanged += (_, _) =>
            {
                AppConfig.Instance.FissalWritePermissions = _writePermsCheckBox.Checked;
                AppConfig.Instance.Save();
            };
            header.Controls.Add(_writePermsCheckBox, 3, 0);

            layout.Controls.Add(header, 0, 0);

            // Quick Actions
            var quickActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.FromArgb(16, 13, 8),
                Padding = new Padding(6, 4, 6, 4),
            };
            AddChatChip(quickActions, "Check Sales Files", "Check whether the Relay can see my ESO data files and explain anything missing.");
            AddChatChip(quickActions, "Why is sync idle?", "Review my Relay state and tell me why no files may be syncing.");
            AddChatChip(quickActions, "Explain recent logs", "Summarize my recent Relay sync activity and call out failures or stale data.");
            AddChatChip(quickActions, "Clear chat", () =>
            {
                _transcript.Controls.Clear();
                _chatHistory.Clear();
                AddAssistantMessage(false, "Fresh page. What shall we inspect?");
            });
            layout.Controls.Add(quickActions, 0, 1);

            // Transcript
            _transcript = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.FromArgb(10, 9, 6),
                Padding = new Padding(12),
            };
            _transcript.Resize += (_, _) => ResizeAssistantCards();
            layout.Controls.Add(_transcript, 0, 2);

            // Composer
            var composer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = CPanelBg,
                Padding = new Padding(8),
            };
            composer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            composer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(110 * _scale)));

            _prompt = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                AcceptsReturn = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(18, 15, 9),
                ForeColor = CText,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Body(9.5f, _scale),
                MaxLength = 2000,
            };
            _prompt.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && !e.Shift)
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    _ = SendAssistantPromptAsync();
                }
            };
            composer.Controls.Add(_prompt, 0, 0);

            _send = MakeStyledButton("Send  >", CGreen);
            _send.Dock = DockStyle.Fill;
            _send.Font = Title(10f, _scale, FontStyle.Bold);
            _send.Click += async (_, _) => await SendAssistantPromptAsync();
            composer.Controls.Add(_send, 1, 0);

            layout.Controls.Add(composer, 0, 3);

            // Status Bar
            var statusPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CPanelBg,
                Padding = new Padding(8, 4, 8, 4),
            };
            _assistantStatus = new Label
            {
                Text = "Press Enter to send. Shift+Enter creates a new line.",
                ForeColor = CTextSub,
                Font = Body(8f, _scale),
                Dock = DockStyle.Fill,
            };
            statusPanel.Controls.Add(_assistantStatus);
            layout.Controls.Add(statusPanel, 0, 4);

            _assistantView.Controls.Add(layout);
        }

        private void AddChatChip(FlowLayoutPanel panel, string label, string prompt)
        {
            var btn = MakeStyledButton(label, CGoldMid);
            btn.Height = (int)(28 * _scale);
            btn.Font = Body(8f, _scale);
            btn.Click += (_, _) =>
            {
                _prompt.Text = prompt;
                _prompt.Focus();
                _prompt.SelectionStart = _prompt.TextLength;
            };
            panel.Controls.Add(btn);
        }

        private void AddChatChip(FlowLayoutPanel panel, string label, Action onClick)
        {
            var btn = MakeStyledButton(label, CTextSub);
            btn.Height = (int)(28 * _scale);
            btn.Font = Body(8f, _scale);
            btn.Click += (_, _) => onClick();
            panel.Controls.Add(btn);
        }

        private async Task SendAssistantPromptAsync()
        {
            var text = _prompt.Text.Trim();
            if (string.IsNullOrWhiteSpace(text) || !_send.Enabled) return;

            if (string.IsNullOrWhiteSpace(AppConfig.Instance.DeviceToken) && string.IsNullOrWhiteSpace(AppConfig.Instance.ApiKey))
            {
                AddAssistantMessage(false, "Pairing is required before asking Fissal questions. Please switch to the **Setup & Pairing** tab and enter your Relay Pairing Code.", true);
                return;
            }

            _prompt.Clear();
            AddAssistantMessage(true, text);
            _chatHistory.Add(("User", text));

            _send.Enabled = false;
            _send.Text = "...";
            _assistantStatus.Text = "Fissal is analyzing the tonal harmonics...";

            try
            {
                var sb = new StringBuilder("Continue this Relay support conversation. Reply to the latest user message.\n");
                int start = Math.Max(0, _chatHistory.Count - 8);
                for (int i = start; i < _chatHistory.Count; i++)
                {
                    sb.Append(_chatHistory[i].role).Append(": ").AppendLine(_chatHistory[i].text);
                }

                if (_harnessCheckBox.Checked)
                {
                    sb.Append("\n\n[LOCAL RELAY HARNESS - diagnostics supplied with explicit user consent]\n")
                      .Append(_harnessService.DescribePermissions(_writePermsCheckBox.Checked)).Append("\n")
                      .Append(_watcher.GetAssistantContext());
                    if (_writePermsCheckBox.Checked)
                    {
                        sb.Append("\n").Append(_harnessService.GetCommandContract());
                    }
                }

                var result = await _watcher.AskFissalAsync(sb.ToString());
                string reply = result.message;

                if (result.ok)
                {
                    reply = ProcessHarnessActionInReply(reply);
                    _chatHistory.Add(("Fissal", reply));
                }

                AddAssistantMessage(false, reply, !result.ok);
                _assistantModelLabel.Text = result.ok ? "● CONNECTED" : "● ERROR";
                _assistantModelLabel.ForeColor = result.ok ? CGreen : CBarFail;
                _assistantStatus.Text = result.ok ? "Response received." : "Communication interrupted.";
            }
            catch (Exception ex)
            {
                AddAssistantMessage(false, $"Request failed: `{ex.Message}`", true);
                _assistantStatus.Text = "Error during assistant transmission.";
            }
            finally
            {
                _send.Enabled = true;
                _send.Text = "Send  >";
                _prompt.Focus();
            }
        }

        private string ProcessHarnessActionInReply(string response)
        {
            const string pattern = @"<fissal-action>(.*?)</fissal-action>";
            var match = Regex.Match(response, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!match.Success) return response;

            var visibleResponse = Regex.Replace(response, pattern, string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();
            if (!_harnessCheckBox.Checked || !_writePermsCheckBox.Checked)
                return visibleResponse + "\n\n**Local action blocked:** Write permission is disabled.";

            var confirmation = FissalBox.Show(
                "Fissal requested a change to an approved Relay setting. Apply this change?",
                "Confirm Local Change",
                MessageBoxButtons.YesNo);
            if (confirmation != DialogResult.Yes)
                return visibleResponse + "\n\n**Local action cancelled:** No settings were changed.";

            var execution = _harnessService.Execute(match.Groups[1].Value);
            return visibleResponse + $"\n\n**Local action {(execution.ok ? "complete" : "failed")}:** {execution.message}";
        }

        private void AddAssistantMessage(bool fromUser, string text, bool isError = false)
        {
            var card = new Panel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = fromUser ? Color.FromArgb(36, 28, 14) : isError ? CErrBg : CPanelBg,
                Padding = new Padding(12),
                Margin = new Padding(fromUser ? 64 : 0, 0, fromUser ? 0 : 64, 8),
                Tag = "chat-card",
            };

            var body = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 3,
            };

            var senderLabel = new Label
            {
                AutoSize = true,
                Text = fromUser ? "YOU" : isError ? "FISSAL // ANOMALY" : "FISSAL",
                ForeColor = fromUser ? CGoldBrt : isError ? CBarFail : CGreen,
                Font = Mono(8f, _scale, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 4),
            };
            body.Controls.Add(senderLabel);

            var rtb = new RichTextBox
            {
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = card.BackColor,
                ForeColor = CText,
                Font = Body(9.5f, _scale),
                DetectUrls = true,
                ScrollBars = RichTextBoxScrollBars.None,
                TabStop = true,
            };
            FormatAssistantRichText(rtb, text);
            rtb.Width = Math.Max(300, _transcript.ClientSize.Width - 140);
            rtb.Height = CalculateRichTextHeight(rtb, rtb.Width);
            rtb.LinkClicked += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.LinkText))
                    try { Process.Start(new ProcessStartInfo(e.LinkText) { UseShellExecute = true }); } catch { }
            };
            body.Controls.Add(rtb);

            if (!fromUser)
            {
                var copyLink = new LinkLabel
                {
                    Text = "Copy response",
                    LinkColor = CTextSub,
                    ActiveLinkColor = CGoldBrt,
                    Font = Body(7.5f, _scale),
                    AutoSize = true,
                    Margin = new Padding(0, 6, 0, 0),
                };
                copyLink.LinkClicked += (_, _) =>
                {
                    try { Clipboard.SetText(text); _assistantStatus.Text = "Response copied to clipboard."; }
                    catch { _assistantStatus.Text = "Failed to copy."; }
                };
                body.Controls.Add(copyLink);
            }

            card.Controls.Add(body);
            _transcript.Controls.Add(card);
            ResizeAssistantCards();
            _transcript.ScrollControlIntoView(card);
        }

        private void ResizeAssistantCards()
        {
            int width = Math.Max(360, _transcript.ClientSize.Width - 30);
            foreach (Control ctrl in _transcript.Controls)
            {
                if (!Equals(ctrl.Tag, "chat-card")) continue;
                ctrl.Width = width - ctrl.Margin.Horizontal;
                foreach (Control child in ctrl.Controls)
                {
                    child.Width = ctrl.ClientSize.Width - ctrl.Padding.Horizontal;
                    foreach (Control nested in child.Controls)
                    {
                        if (nested is RichTextBox rtb)
                        {
                            rtb.Width = child.ClientSize.Width;
                            rtb.Height = CalculateRichTextHeight(rtb, rtb.Width);
                        }
                    }
                }
            }
        }

        private int CalculateRichTextHeight(RichTextBox rtb, int width)
        {
            var size = TextRenderer.MeasureText(rtb.Text + "\n ", rtb.Font, new Size(Math.Max(100, width - 8), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            return Math.Max(24, size.Height + 10);
        }

        private void FormatAssistantRichText(RichTextBox box, string raw)
        {
            string clean = Regex.Replace(raw ?? string.Empty, "(?m)^#{1,6}\\s+", string.Empty);
            clean = Regex.Replace(clean, "(?m)^[-*]\\s+", "• ");
            box.Text = clean;

            // Apply style passes
            ApplyStylePattern(box, @"\*\*(.+?)\*\*", FontStyle.Bold, CGoldBrt, removeMarker: true);
            ApplyStylePattern(box, @"`([^`]+)`", FontStyle.Regular, CWarn, removeMarker: true, monospace: true);
        }

        private void ApplyStylePattern(RichTextBox box, string pattern, FontStyle style, Color color, bool removeMarker, bool monospace = false)
        {
            var matches = Regex.Matches(box.Text, pattern);
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                var match = matches[i];
                if (removeMarker)
                {
                    box.Select(match.Index, match.Length);
                    box.SelectedText = match.Groups[1].Value;
                }
                box.Select(match.Index, match.Groups[1].Value.Length);
                box.SelectionFont = monospace ? Mono(9f, _scale, style) : Body(9.5f, _scale, style);
                box.SelectionColor = color;
            }
            box.Select(0, 0);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 3. SETUP & PAIRING VIEW
        // ═════════════════════════════════════════════════════════════════════
        private void InitSetupView()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                AutoScroll = true,
                Padding = new Padding(16),
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var formPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                RowCount = 7,
                BackColor = CPanelBg,
                Padding = new Padding(16),
                AutoSize = true,
            };
            formPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(180 * _scale)));
            formPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // Section Header
            var sectionLabel = new Label
            {
                Text = "RELAY CONFIGURATION & DEVICE PAIRING",
                ForeColor = CGoldBrt,
                Font = Title(11f, _scale, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 16),
            };
            formPanel.Controls.Add(sectionLabel, 0, 0);
            formPanel.SetColumnSpan(sectionLabel, 2);

            // Display Name
            formPanel.Controls.Add(MakeFieldLabel("Trader Display Name:"), 0, 1);
            _txtDisplayName = MakeStyledTextBox(AppConfig.Instance.DisplayName);
            formPanel.Controls.Add(_txtDisplayName, 1, 1);

            // Pairing Code
            formPanel.Controls.Add(MakeFieldLabel("Relay Pairing Code:"), 0, 2);
            var pairLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0) };
            pairLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pairLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _txtPairingCode = MakeStyledTextBox(AppConfig.Instance.PairingCode);
            pairLayout.Controls.Add(_txtPairingCode, 0, 0);

            _btnPairDevice = MakeStyledButton("Pair Device Now", CGreen);
            _btnPairDevice.Click += async (_, _) => await RunDevicePairingAsync();
            pairLayout.Controls.Add(_btnPairDevice, 1, 0);
            formPanel.Controls.Add(pairLayout, 1, 2);

            // Server URL
            formPanel.Controls.Add(MakeFieldLabel("Sync Server URL:"), 0, 3);
            _txtServerUrl = MakeStyledTextBox(AppConfig.Instance.ServerUrl);
            formPanel.Controls.Add(_txtServerUrl, 1, 3);

            // Pairing Status
            formPanel.Controls.Add(MakeFieldLabel("Pairing Status:"), 0, 4);
            _lblPairingStatus = new Label
            {
                Text = "Inspecting...",
                ForeColor = CText,
                Font = Mono(9f, _scale, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            formPanel.Controls.Add(_lblPairingStatus, 1, 4);

            // Device Info
            formPanel.Controls.Add(MakeFieldLabel("Device Details:"), 0, 5);
            _lblDeviceInfo = new Label
            {
                Text = "Loading...",
                ForeColor = CTextSub,
                Font = Mono(8f, _scale),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            formPanel.Controls.Add(_lblDeviceInfo, 1, 5);

            // Action Buttons
            var btnRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                Margin = new Padding(0, 12, 0, 0),
            };

            _btnSaveSetup = MakeStyledButton("Save Settings", CGoldBrt);
            _btnSaveSetup.Click += (_, _) =>
            {
                var cfg = AppConfig.Instance;
                cfg.DisplayName = _txtDisplayName.Text.Trim();
                cfg.PairingCode = _txtPairingCode.Text.Trim();
                cfg.ServerUrl = _txtServerUrl.Text.Trim();
                cfg.Save();
                RefreshSetupView();
                FissalBox.Show("Relay settings saved successfully!", "Settings Saved");
            };
            btnRow.Controls.Add(_btnSaveSetup);

            _btnTestConnection = MakeStyledButton("Test Server Ping", CText);
            _btnTestConnection.Click += async (_, _) =>
            {
                _btnTestConnection.Enabled = false;
                _btnTestConnection.Text = "Pinging...";
                var (ok, msg) = await _watcher.PingServerAsync();
                _btnTestConnection.Enabled = true;
                _btnTestConnection.Text = "Test Server Ping";
                FissalBox.Show(ok ? $"Connected to Redfur server lattice! ({msg})" : $"Could not establish signal to the upload endpoint: {msg}", "Connection Test");
            };
            btnRow.Controls.Add(_btnTestConnection);

            formPanel.Controls.Add(btnRow, 1, 6);

            layout.Controls.Add(formPanel, 0, 0);
            _setupView.Controls.Add(layout);
        }

        private async Task RunDevicePairingAsync()
        {
            string code = _txtPairingCode.Text.Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                FissalBox.Show("Please enter a pairing code from the Redfur web interface.", "Pairing Code Missing");
                return;
            }

            _btnPairDevice.Enabled = false;
            _btnPairDevice.Text = "Pairing...";

            try
            {
                var cfg = AppConfig.Instance;
                cfg.PairingCode = code;
                cfg.DisplayName = _txtDisplayName.Text.Trim();
                cfg.Save();

                var (paired, message) = await _watcher.PairDeviceAsync();

                if (paired)
                {
                    RefreshSetupView();
                    FissalBox.Show("Device successfully paired with the Redfur Lattice!", "Pairing Complete");
                    _ = _watcher.StartAsync();
                }
                else
                {
                    FissalBox.Show($"Pairing failed: {message}", "Pairing Error");
                }
            }
            catch (Exception ex)
            {
                FissalBox.Show($"Exception during pairing: {ex.Message}", "Pairing Exception");
            }
            finally
            {
                _btnPairDevice.Enabled = true;
                _btnPairDevice.Text = "Pair Device Now";
                RefreshSetupView();
            }
        }

        private void RefreshSetupView()
        {
            var cfg = AppConfig.Instance;
            _txtDisplayName.Text = cfg.DisplayName;
            _txtPairingCode.Text = cfg.PairingCode;
            _txtServerUrl.Text = cfg.ServerUrl;

            bool paired = !string.IsNullOrWhiteSpace(cfg.DeviceToken) || !string.IsNullOrWhiteSpace(cfg.ApiKey);
            _lblPairingStatus.Text = paired ? "✔ PAIRED WITH LATTICE" : "✖ UNPAIRED / CODE REQUIRED";
            _lblPairingStatus.ForeColor = paired ? CGreen : CBarFail;

            _lblDeviceInfo.Text = $"Token Storage: DPAPI Encrypted (CurrentUser)\nUpdate Endpoint: {cfg.UpdateUrl}";
        }

        // ═════════════════════════════════════════════════════════════════════
        // 4. THEMES & DISPLAY VIEW
        // ═════════════════════════════════════════════════════════════════════
        private void InitThemesView()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(12),
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(40 * _scale)));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(70 * _scale)));

            // Header
            var header = new Label
            {
                Text = "SELECT TERMINAL THEME & VISUAL FIDELITY",
                ForeColor = CGoldBrt,
                Font = Title(11f, _scale, FontStyle.Bold),
                Dock = DockStyle.Fill,
            };
            layout.Controls.Add(header, 0, 0);

            // 11 Themes Grid
            _themeCardsHost = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = Color.FromArgb(10, 9, 7),
                Padding = new Padding(8),
            };
            PopulateThemeCards();
            layout.Controls.Add(_themeCardsHost, 0, 1);

            // Performance & Scale Settings
            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = CPanelBg,
                Padding = new Padding(12, 8, 12, 8),
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            footer.Controls.Add(MakeFieldLabel("Visual Fidelity:"), 0, 0);

            _fidelityCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(20, 16, 10),
                ForeColor = CText,
                FlatStyle = FlatStyle.Flat,
                Font = Body(9f, _scale),
                Dock = DockStyle.Fill,
            };
            _fidelityCombo.Items.AddRange(new object[] { "Low (Minimal FX)", "Medium (Balanced)", "High (Full Glow / FX)" });
            _fidelityCombo.SelectedIndex = (int)AppConfig.Instance.VisualFidelity;
            _fidelityCombo.SelectedIndexChanged += (_, _) =>
            {
                var mode = (FidelityMode)_fidelityCombo.SelectedIndex;
                AppConfig.Instance.VisualFidelity = mode;
                AppConfig.Instance.Save();
                UploadProgressForm.AppConfig.SetMode(mode);
            };
            footer.Controls.Add(_fidelityCombo, 1, 0);

            footer.Controls.Add(MakeFieldLabel("UI Text Scaling:"), 2, 0);

            var scaleLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
            scaleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            scaleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 45));

            _scaleTrackBar = new TrackBar
            {
                Minimum = 75,
                Maximum = 175,
                Value = (int)(AppConfig.Instance.AppScale * 100),
                TickFrequency = 25,
                Dock = DockStyle.Fill,
            };
            _scaleValueLabel = new Label
            {
                Text = $"{_scaleTrackBar.Value}%",
                ForeColor = CGoldBrt,
                Font = Mono(8.5f, _scale, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
            };

            _scaleTrackBar.Scroll += (_, _) =>
            {
                _scaleValueLabel.Text = $"{_scaleTrackBar.Value}%";
                AppConfig.Instance.AppScale = _scaleTrackBar.Value / 100f;
                AppConfig.Instance.Save();
            };

            scaleLayout.Controls.Add(_scaleTrackBar, 0, 0);
            scaleLayout.Controls.Add(_scaleValueLabel, 1, 0);
            footer.Controls.Add(scaleLayout, 3, 0);

            layout.Controls.Add(footer, 0, 2);
            _themesView.Controls.Add(layout);
        }

        private void PopulateThemeCards()
        {
            _themeCardsHost.Controls.Clear();
            int cardW = (int)(210 * _scale);
            int cardH = (int)(110 * _scale);

            foreach (var kvp in FissalTheme.AllPalettes)
            {
                var p = kvp.Value;
                bool isSelected = string.Equals(FissalTheme.Current.Id, p.Id, StringComparison.OrdinalIgnoreCase);

                var card = new Panel
                {
                    Width = cardW,
                    Height = cardH,
                    BackColor = p.PanelBg,
                    Margin = new Padding(6),
                    Cursor = Cursors.Hand,
                    Padding = new Padding(8),
                };

                var layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 3,
                    BackColor = Color.Transparent,
                };
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(24 * _scale)));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(26 * _scale)));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

                // Title + Mark
                var title = new Label
                {
                    Text = $"{p.Mark} {p.DisplayName.ToUpperInvariant()}",
                    ForeColor = p.GoldBrt,
                    Font = Title(9.5f, _scale, FontStyle.Bold),
                    Dock = DockStyle.Fill,
                };
                layout.Controls.Add(title, 0, 0);

                // Color Swatches
                var swatches = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.LeftToRight,
                    Margin = new Padding(0),
                };
                AddSwatch(swatches, p.Bg);
                AddSwatch(swatches, p.PanelBg);
                AddSwatch(swatches, p.Border);
                AddSwatch(swatches, p.GoldBrt);
                AddSwatch(swatches, p.Green);
                AddSwatch(swatches, p.Accent);
                layout.Controls.Add(swatches, 0, 1);

                // Description
                var desc = new Label
                {
                    Text = p.Description,
                    ForeColor = p.TextSub,
                    Font = Body(7.5f, _scale),
                    Dock = DockStyle.Fill,
                };
                layout.Controls.Add(desc, 0, 2);

                card.Controls.Add(layout);

                // Highlight border if active
                card.Paint += (_, e) =>
                {
                    using var pen = new Pen(isSelected ? p.Green : p.Border, isSelected ? 2 : 1);
                    e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
                };

                // Click event on all card child controls
                void WireClick(Control c)
                {
                    c.Click += (_, _) => FissalTheme.SetTheme(p.Id);
                    foreach (Control child in c.Controls) WireClick(child);
                }
                WireClick(card);

                _themeCardsHost.Controls.Add(card);
            }
        }

        private void AddSwatch(FlowLayoutPanel host, Color color)
        {
            var swatch = new Panel
            {
                Width = (int)(20 * _scale),
                Height = (int)(14 * _scale),
                BackColor = color,
                Margin = new Padding(0, 0, 4, 0),
            };
            host.Controls.Add(swatch);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 5. DIAGNOSTICS & CONFIG VIEW
        // ═════════════════════════════════════════════════════════════════════
        private void InitDiagnosticsView()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(12),
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(80 * _scale)));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(44 * _scale)));

            // Top Status & Paths
            var topCard = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                BackColor = CPanelBg,
                Padding = new Padding(10, 6, 10, 6),
            };
            topCard.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(140 * _scale)));
            topCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            topCard.Controls.Add(MakeFieldLabel("Watcher State:"), 0, 0);
            _watcherStatusLabel = new Label { Text = "Active", ForeColor = CGreen, Font = Mono(8.5f, _scale, FontStyle.Bold), Dock = DockStyle.Fill };
            topCard.Controls.Add(_watcherStatusLabel, 1, 0);

            topCard.Controls.Add(MakeFieldLabel("ESO Directory:"), 0, 1);
            _esoPathLabel = new Label { Text = "Sniffing...", ForeColor = CTextSub, Font = Mono(7.5f, _scale), Dock = DockStyle.Fill };
            topCard.Controls.Add(_esoPathLabel, 1, 1);

            topCard.Controls.Add(MakeFieldLabel("Config Path:"), 0, 2);
            _configPathLabel = new Label { Text = AppConfig.ConfigPath, ForeColor = CTextSub, Font = Mono(7.5f, _scale), Dock = DockStyle.Fill };
            topCard.Controls.Add(_configPathLabel, 1, 2);

            layout.Controls.Add(topCard, 0, 0);

            // Monospace Diagnostics Output Box
            _diagLogBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(8, 7, 5),
                ForeColor = CText,
                Font = Mono(8.5f, _scale),
                BorderStyle = BorderStyle.FixedSingle,
            };
            layout.Controls.Add(_diagLogBox, 0, 1);

            // Action Buttons
            var btnBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = CPanelBg,
                Padding = new Padding(8, 6, 8, 6),
            };

            _btnOpenConfigDir = MakeStyledButton("Open AppData Folder", CGoldMid);
            _btnOpenConfigDir.Click += (_, _) => Process.Start("explorer.exe", AppConfig.ConfigDirectory);
            btnBar.Controls.Add(_btnOpenConfigDir);

            _btnOpenConfigFile = MakeStyledButton("Open config.json", CText);
            _btnOpenConfigFile.Click += (_, _) =>
            {
                if (!File.Exists(AppConfig.ConfigPath)) AppConfig.Instance.Save();
                Process.Start(new ProcessStartInfo("notepad.exe", $"\"{AppConfig.ConfigPath}\"") { UseShellExecute = true });
            };
            btnBar.Controls.Add(_btnOpenConfigFile);

            _btnRestartWatcher = MakeStyledButton("Restart File Watcher", CGreen);
            _btnRestartWatcher.Click += async (_, _) =>
            {
                _btnRestartWatcher.Enabled = false;
                await _watcher.StartAsync();
                _btnRestartWatcher.Enabled = true;
                RefreshDiagnosticsView();
            };
            btnBar.Controls.Add(_btnRestartWatcher);

            layout.Controls.Add(btnBar, 0, 2);
            _diagnosticsView.Controls.Add(layout);
        }

        private void RefreshDiagnosticsView()
        {
            _watcherStatusLabel.Text = _watcher.Jobs.Any(j => j.Status == UploadStatus.Uploading) ? "TRANSMITTING" : "ACTIVE MONITORING";
            _watcherStatusLabel.ForeColor = CGreen;

            string context = _watcher.GetAssistantContext();
            _diagLogBox.Text = $"[FISSAL TONAL RELAY DIAGNOSTICS SNAPSHOT — {DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n\n" + context;

            // Find ESO path from diagnostic lines
            var match = Regex.Match(context, @"Tracked directories:\s*(.+)");
            if (match.Success) _esoPathLabel.Text = match.Groups[1].Value.Trim();
        }

        // ═════════════════════════════════════════════════════════════════════
        // THEME & EVENT HANDLERS
        // ═════════════════════════════════════════════════════════════════════
        private void OnGlobalThemeChanged()
        {
            if (InvokeRequired)
            {
                BeginInvoke(OnGlobalThemeChanged);
                return;
            }

            BackColor = CBg;
            ForeColor = CText;

            _rootLayout.BackColor = CBg;
            _titleBar.BackColor = CPanelBg;
            _titleMarkLabel.Text = ThemeMark + " ";
            _titleMarkLabel.ForeColor = CGoldBrt;
            _titleTextLabel.ForeColor = CGoldBrt;
            _titleThemeBadge.Text = $"[{Current.DisplayName.ToUpperInvariant()}]";
            _titleThemeBadge.ForeColor = CGreen;

            _navRail.BackColor = CPanelBg;
            _contentHost.BackColor = CBg;

            PopulateThemeCards();
            SwitchTab(_activeTabId);
            RefreshAllViews();
            Invalidate(true);
        }

        private void RefreshAllViews()
        {
            RefreshSyncView();
            RefreshSetupView();
            RefreshDiagnosticsView();
        }

        private void OnWatcherJobsChanged()
        {
            if (IsDisposed || _syncRefreshPending) return;
            _syncRefreshPending = true;
            BeginInvoke(() =>
            {
                _syncRefreshPending = false;
                RefreshSyncView();
            });
        }

        private void OnWatcherConnectionChecked(bool ok, string msg)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => OnWatcherConnectionChecked(ok, msg));
                return;
            }
            _titleStatusLabel.Text = ok ? "● LATTICE CONNECTED" : "● SIGNAL DEGRADED";
            _titleStatusLabel.ForeColor = ok ? CGreen : CBarFail;
            _signalLampLabel.ForeColor = ok ? CGreen : CBarFail;
            _messageBoardLabel.Text = ok ? "FREQUENCIES LOCKED // MONITORING ESO DATA" : "SIGNAL DEGRADED // OPEN DIAGNOSTICS";
        }

        private void OnTitleBarMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || WindowState == FormWindowState.Maximized) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, 0x2, 0);
        }

        // ── Helper UI Controls ────────────────────────────────────────────────
        private Button MakeStyledButton(string text, Color accent)
        {
            var btn = new Button
            {
                Text = text,
                ForeColor = accent,
                BackColor = CBtnBg,
                FlatStyle = FlatStyle.Flat,
                Font = Body(8.5f, _scale, FontStyle.Bold),
                Cursor = Cursors.Hand,
                AutoSize = true,
                Padding = new Padding(10, 4, 10, 4),
                Margin = new Padding(0, 0, 8, 0),
            };
            btn.FlatAppearance.BorderColor = CBtnBorder;
            btn.FlatAppearance.MouseOverBackColor = CBarBg;
            return btn;
        }

        private TextBox MakeStyledTextBox(string initialText)
        {
            return new TextBox
            {
                Text = initialText,
                BackColor = Color.FromArgb(18, 15, 9),
                ForeColor = CText,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Mono(9f, _scale),
                Dock = DockStyle.Fill,
            };
        }

        private Label MakeFieldLabel(string text)
        {
            return new Label
            {
                Text = text,
                ForeColor = CGoldBrt,
                Font = Body(8.5f, _scale, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
            };
        }
    }
}
