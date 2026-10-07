using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static RedfurSync.FissalTheme;

namespace RedfurSync
{
    internal sealed class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            UpdateStyles();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (BackColor != Color.Transparent)
            {
                using var brush = new SolidBrush(BackColor);
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }
            else
            {
                base.OnPaintBackground(e);
            }
        }
    }

    internal sealed class DoubleBufferedTableLayoutPanel : TableLayoutPanel
    {
        public DoubleBufferedTableLayoutPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            UpdateStyles();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (BackColor != Color.Transparent)
            {
                using var brush = new SolidBrush(BackColor);
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }
            else
            {
                base.OnPaintBackground(e);
            }
        }
    }

    internal sealed class DoubleBufferedFlowLayoutPanel : FlowLayoutPanel
    {
        public DoubleBufferedFlowLayoutPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            UpdateStyles();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (BackColor != Color.Transparent)
            {
                using var brush = new SolidBrush(BackColor);
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }
            else
            {
                base.OnPaintBackground(e);
            }
        }
    }

    public sealed class RelayMainWindow : Form
    {
        private static void TraceLog(string msg)
        {
            try {
                var p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");
                File.AppendAllText(p, $"[{DateTime.Now:HH:mm:ss.fff}] [MainWindow] {msg}\n");
            } catch {}
        }

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        private const int WM_VSCROLL = 0x0115;
        private const int SB_BOTTOM = 7;
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

        private readonly FileWatcherService _watcher;
        private readonly Action<UploadJob> _applyUpdateAction;
        private float _scale = 1f;

        // Top-level layout
        private DoubleBufferedTableLayoutPanel _rootLayout = null!;
        private DoubleBufferedTableLayoutPanel _headerConsole = null!;
        private Label _titleMarkLabel = new();
        private Label _titleTextLabel = new();
        private Label _titleThemeBadge = new();
        private Label _titleStatusLabel = new();
        private Label _messageBoardLabel = new();
        private string _messageBoardText = "All guild trader lines & bank deposits verified • Watching ESO";
        private Color _messageBoardColor = CGreen;
        private Panel _navRail = null!;
        private Panel _contentHost = null!;

        // Vacuum tube & micro-display CRT marquee state
        private readonly Random _rand = new();
        private int _glowAlpha = 140;
        private int _glowStep = 2;
        private Color _coreColor = Color.FromArgb(255, 200, 120);
        private Color _auraColor = Color.FromArgb(240, 150, 40);
        private float _crtFlicker = 1.0f;
        private float _scanPhase = 0f;
        private float _shimmer = 0f;
        private int _dispStatusIdx = 0;
        private int _dispWait = 35;
        private DoubleBufferedPanel? _titlePlatePanel;
        private DoubleBufferedPanel? _microDisplayPanel;

        // Nav buttons
        private readonly List<(string id, Panel panel, Button btn, Panel viewPanel)> _navItems = new();
        private string _activeTabId = "sync";

        // View Panels
        private Panel _syncView = null!;
        private Panel _addonView = null!;
        private Panel _setupView = null!;
        private Panel _themesView = null!;
        private Panel _diagnosticsView = null!;

        // ── 1. Sync View Controls ──
        private DoubleBufferedFlowLayoutPanel _syncJobsList = null!;
        private Label _syncSummaryLabel = null!;
        private Label _syncStateBadge = null!;
        private Button _btnRefreshJobs = null!;
        private Button _btnClearCompleted = null!;
        private RichTextBox _syncLogBox = null!;
        private DoubleBufferedPanel? _oscilloscopePanel;
        private readonly Dictionary<UploadJob, UploadStatus> _loggedJobStates = new();
        private bool _isRefreshingSyncView = false;
        private bool _isResizingSyncCards = false;
        private readonly Dictionary<UploadJob, JobCardControls> _jobCards = new();
        private readonly HashSet<string> _userCollapsedSessions = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _userExpandedSessions = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SessionCardControls> _sessionCards = new(StringComparer.OrdinalIgnoreCase);
        private System.Windows.Forms.Timer? _animTimer;
        private int _animFrame = 0;
        private Label _tickerLabel = null!;
        private Panel _tickerPanel = null!;
        private bool _batchInProgress = false;
        private bool _isConnected = true;
        private readonly SynchronizationContext? _syncContext;
        private readonly System.Threading.Timer? _syncCoalesceTimer;
        private long _lastRenderedJobVersion = -1;

        private sealed class JobCardControls
        {
            public Panel Card { get; init; } = null!;
            public Label StatusLabel { get; init; } = null!;
            public Label DetailLabel { get; init; } = null!;
            public FlowLayoutPanel ActionFlow { get; init; } = null!;
        }

        private sealed class SessionCardControls
        {
            public Panel Card { get; init; } = null!;
            public Label ChevronLabel { get; init; } = null!;
            public Label TitleLabel { get; init; } = null!;
            public Label SubtitleLabel { get; init; } = null!;
            public Label StatusLabel { get; init; } = null!;
            public Label DetailLabel { get; init; } = null!;
            public FlowLayoutPanel ActionFlow { get; init; } = null!;
            public Panel FilesContainer { get; init; } = null!;
            public Dictionary<UploadJob, CompactFileRowControls> FileRows { get; } = new();
            public SyncSessionModel Session { get; set; } = null!;
        }

        private sealed class CompactFileRowControls
        {
            public Panel Row { get; init; } = null!;
            public Label NameLabel { get; init; } = null!;
            public Label MetaLabel { get; init; } = null!;
            public Label StatusLabel { get; init; } = null!;
            public FlowLayoutPanel ActionFlow { get; init; } = null!;
        }

        private sealed class SyncSessionModel
        {
            public string Id { get; init; } = string.Empty;
            public DateTime Timestamp { get; init; }
            public string Title { get; init; } = string.Empty;
            public List<UploadJob> Jobs { get; } = new();

            public int TotalCount => Jobs.Count;
            public int DoneCount => Jobs.Count(j => j.Status == UploadStatus.Done);
            public int UploadingCount => Jobs.Count(j => j.Status == UploadStatus.Uploading);
            public int QueuedCount => Jobs.Count(j => j.Status == UploadStatus.Queued);
            public int FailedCount => Jobs.Count(j => j.Status is UploadStatus.Failed or UploadStatus.Cancelled);
            public long TotalBytes => Jobs.Sum(j => j.FileSizeBytes);

            public string TotalSizeDisplay
            {
                get
                {
                    long b = TotalBytes;
                    if (b < 1024) return $"{b} B";
                    if (b < 1024 * 1024) return $"{b / 1024.0:0.0} KB";
                    return $"{b / (1024.0 * 1024):0.0} MB";
                }
            }

            public float AggregateProgress
            {
                get
                {
                    if (Jobs.Count == 0) return 0f;
                    float sum = 0f;
                    foreach (var j in Jobs)
                    {
                        if (j.Status == UploadStatus.Done) sum += 1f;
                        else if (j.Status == UploadStatus.Uploading) sum += Math.Clamp(j.Progress, 0f, 1f);
                    }
                    return Math.Clamp(sum / Jobs.Count, 0f, 1f);
                }
            }

            public UploadStatus AggregateStatus
            {
                get
                {
                    if (UploadingCount > 0) return UploadStatus.Uploading;
                    if (QueuedCount > 0) return UploadStatus.Queued;
                    if (FailedCount > 0) return UploadStatus.Failed;
                    if (DoneCount == TotalCount && TotalCount > 0) return UploadStatus.Done;
                    return UploadStatus.Queued;
                }
            }
        }

        // ── Compact Mode State ──
        private bool _isCompactMode = false;
        private Size _normalSize;
        private Control? _compactToggleBtn;

        // ── 2. ESO Addon Controls ──
        private Label _lblAddonStatusBadge = null!;
        private Label _lblAddonStatusDetail = null!;
        private Label _lblAddonInstalledVer = null!;
        private Label _lblAddonLatestVer = null!;
        private TextBox _txtAddonEsoPath = null!;
        private Label _lblLibHistoireStatus = null!;
        private Button _btnInstallLibHistoire = null!;
        private Label _lblLibAddonMenuStatus = null!;
        private Button _btnInstallLibAddonMenu = null!;
        private Label _lblTtcStatus = null!;
        private Button _btnInstallOrUpdateAddon = null!;
        private Button _btnBrowseEsoPath = null!;
        private Button _btnRefreshAddon = null!;
        private Button _btnOpenAddonFolder = null!;
        private Button _btnOpenSavedVars = null!;
        private Button _btnUpdateTtcPriceTable = null!;
        private Label _lblSetupAddonStatus = null!;
        private TableLayoutPanel? _addonLayout;
        private TableLayoutPanel? _setupLayout;

        // ── 3. Setup Controls ──
        private TextBox _txtDisplayName = null!;
        private TextBox _txtPairingCode = null!;
        private TextBox _txtServerUrl = null!;
        private Label _lblPairingStatus = null!;
        private Label _lblDeviceInfo = null!;
        private Button _btnSilentSync = null!;
        private Button _btnSyncMm = null!;
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
            _syncContext = SynchronizationContext.Current;
            _syncCoalesceTimer = new System.Threading.Timer(OnSyncCoalesceTick, null, 200, 200);

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = CBg;
            ForeColor = CText;
            ShowInTaskbar = true;
            Text = "Fissal Relay // Dwemer Tonal Terminal";

            // Determine true monitor DPI scaling before computing sizes
            _scale = FissalTheme.GetSystemScale();
            if (IsHandleCreated)
            {
                float winScale = GetScale(Handle);
                if (winScale > 0) _scale = winScale;
            }
            else
            {
                try
                {
                    var scr = Screen.FromPoint(Cursor.Position) ?? Screen.PrimaryScreen;
                    if (scr != null)
                    {
                        using var g = Graphics.FromHwnd(IntPtr.Zero);
                        _scale = g.DpiX / 96f;
                    }
                }
                catch { }
            }

            int baseW = 1060;
            int baseH = 690;
            int initialW = (int)(baseW * _scale);
            int initialH = (int)(baseH * _scale);

            var activeScreen = Screen.FromPoint(Cursor.Position) ?? Screen.PrimaryScreen;
            if (activeScreen != null)
            {
                var work = activeScreen.WorkingArea;
                if (initialW > work.Width * 0.94f) initialW = (int)(work.Width * 0.94f);
                if (initialH > work.Height * 0.94f) initialH = (int)(work.Height * 0.94f);
            }

            MinimumSize = new Size((int)(920 * _scale), (int)(580 * _scale));
            Size = new Size(initialW, initialH);

            FissalTheme.SetTheme(AppConfig.Instance.Theme);

            SuspendLayout();
            try
            {
                TraceLog("BuildShell starting");
                BuildShell();
                TraceLog("BuildViewPanels starting");
                BuildViewPanels();
                TraceLog("SwitchTab starting");
                SwitchTab("sync");
            }
            finally
            {
                ResumeLayout(true);
            }
            TraceLog("ctor finished");

            _watcher.JobsChanged += OnWatcherJobsChanged;
            _watcher.ConnectionChecked += OnWatcherConnectionChecked;
            FissalTheme.ThemeChanged += OnGlobalThemeChanged;

            Shown += (_, _) =>
            {
                TraceLog("Shown: RefreshAllViews");
                RefreshAllViews();
                TraceLog("Shown: SeedInitialTelemetry");
                SeedInitialTelemetry();
                TraceLog("Shown: ApplyDarkModeScrollbars");
                ApplyDarkModeScrollbars();
                TraceLog("Shown: all done");
                if (AppConfig.Instance.CompactMode)
                {
                    ToggleCompactMode();
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
                _syncCoalesceTimer?.Dispose();
                _animTimer?.Stop();
                _animTimer?.Dispose();
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
            TraceLog("NavigateToTab: SwitchTab before Show()");
            SwitchTab(tabId);
            if (!Visible)
            {
                TraceLog("NavigateToTab: Show()");
                Show();
                TraceLog("NavigateToTab: Show() returned");
            }
            if (tabId == "sync" && _lastRenderedJobVersion != _watcher.CurrentJobVersion)
            {
                RefreshSyncView();
            }
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }
            TopMost = true;
            Activate();
            BringToFront();
            TopMost = false;
            try { SetForegroundWindow(Handle); } catch { }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED: Paints all descendants using double-buffering
                return cp;
            }
        }

        // ── Window Resizing & Frame Handling ─────────────────────────────────
        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int RESIZE_GRIP = 8;
            base.WndProc(ref m);

            if (m.Msg == WM_NCHITTEST && WindowState != FormWindowState.Maximized)
            {
                long lp = m.LParam.ToInt64();
                int x = unchecked((short)(lp & 0xFFFF));
                int y = unchecked((short)((lp >> 16) & 0xFFFF));
                var pt = PointToClient(new Point(x, y));
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
            _rootLayout = new DoubleBufferedTableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                BackColor = CBg,
                Padding = new Padding((int)(12 * _scale), (int)(10 * _scale), (int)(12 * _scale), (int)(10 * _scale)),
            };
            _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(204 * _scale))); // Nav rail
            _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));                   // Content area
            _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(80 * _scale)));        // Authentic Dwemer header console
            _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));                        // Body

            _rootLayout.Paint += (s, e) =>
            {
                DrawTerminalChassis(e.Graphics, _rootLayout.Width, _rootLayout.Height, _scale);
            };

            // ── 1. Unified Master Header Console (Row 0) ──
            _headerConsole = new DoubleBufferedTableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.FromArgb(20, 16, 9),
                Margin = new Padding((int)(4 * _scale), (int)(4 * _scale), (int)(4 * _scale), (int)(4 * _scale)),
                Padding = new Padding((int)(8 * _scale), (int)(4 * _scale), (int)(8 * _scale), (int)(4 * _scale)),
            };
            _headerConsole.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(270 * _scale))); // Left Identity Plate
            _headerConsole.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));                   // Center Micro-Display CRT Console
            _headerConsole.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(115 * _scale))); // Right Window Controls
            _headerConsole.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _headerConsole.MouseDown += OnTitleBarMouseDown;
            _headerConsole.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int w = _headerConsole.Width;
                int h = _headerConsole.Height;
                if (w <= 10 || h <= 10) return;

                // 1. Dwemer copper/brass gradient background
                using (var hg = new LinearGradientBrush(new Point(0, 0), new Point(0, h), Color.FromArgb(46, 36, 18), Color.FromArgb(20, 16, 9)))
                {
                    g.FillRectangle(hg, 0, 0, w, h);
                }

                // 2. Multi-layer bottom bezel lines
                using (var bezelEdgePen = new Pen(Color.FromArgb(50, 255, 255, 255), Math.Max(2f, 3f * _scale)))
                {
                    g.DrawLine(bezelEdgePen, 0, h - (int)(3 * _scale), w, h - (int)(3 * _scale));
                }
                using (var bezelInnerPen = new Pen(Color.FromArgb(240, 0, 0, 0), Math.Max(1.5f, 2f * _scale)))
                {
                    g.DrawLine(bezelInnerPen, 0, h - 1, w, h - 1);
                }

                // 3. Top edge highlight
                using (var topPen = new Pen(Color.FromArgb(45, 255, 255, 255), 1f))
                {
                    g.DrawLine(topPen, 0, 0, w, 0);
                }

                // 4. Dwemer corner rivets
                DrawCornerRivets(g, w, h, (int)(5 * _scale), Color.FromArgb(100, CGoldMid));
            };

            // 1A. Left Identity Slate: Stamped Dark Metallic Plate with Vacuum Tube & Neon Title
            _titlePlatePanel = new DoubleBufferedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Margin = new Padding((int)(2 * _scale), (int)(6 * _scale), (int)(4 * _scale), (int)(6 * _scale)),
                Cursor = Cursors.Default,
            };
            _titlePlatePanel.MouseDown += OnTitleBarMouseDown;
            _titlePlatePanel.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                int w = _titlePlatePanel.Width;
                int h = _titlePlatePanel.Height;
                if (w <= 10 || h <= 10) return;

                // 1. Inset stamped dark metallic plate
                using var platePath = FissalTheme.RoundRect(0, 0, w - 1, h - 1, (int)(4 * _scale));
                using (var plateBg = new LinearGradientBrush(
                    new Rectangle(0, 0, w, h),
                    Color.FromArgb(12, 14, 16), Color.FromArgb(35, 40, 45), LinearGradientMode.Vertical))
                {
                    g.FillPath(plateBg, platePath);
                }

                using (var plateShadow = new Pen(Color.FromArgb(200, 0, 0, 0), Math.Max(1.5f, 2f * _scale)))
                    g.DrawPath(plateShadow, platePath);

                using (var plateHi = new Pen(Color.FromArgb(40, 255, 255, 255), 1f))
                    g.DrawLine(plateHi, (int)(4 * _scale), h - 1, w - (int)(4 * _scale), h - 1);

                // 2. Physical Vacuum Tube / Nixie Lamp
                int tubeSize = Math.Min(h - (int)(10 * _scale), (int)(38 * _scale));
                int tubeX = (int)(10 * _scale);
                int tubeY = (h - tubeSize) / 2;
                var tubeBounds = new Rectangle(tubeX, tubeY, tubeSize, tubeSize);
                FissalTheme.DrawDwemerVacuumTube(g, tubeBounds, _coreColor, _auraColor, _glowAlpha, _scale);

                // 3. Dusky Sunset Neon Title ("Fissal Relay")
                float titleX = tubeBounds.Right + (int)(10 * _scale);
                float titleY = (int)(6 * _scale);
                Color neonColor = FissalTheme.BrightenColor(Color.FromArgb(255, 200, 100, 40), 0.35f, LightFilter.Dusky);

                using (var outerGlow = new SolidBrush(Color.FromArgb((int)(Math.Sin(_shimmer * 0.4f) * 35 + 65), neonColor)))
                {
                    using var fTitle = Title(12f, _scale, FontStyle.Bold);
                    g.DrawString("Fissal Relay", fTitle, outerGlow, titleX - 1, titleY - 1);
                    g.DrawString("Fissal Relay", fTitle, outerGlow, titleX + 1, titleY + 1);
                    g.DrawString("Fissal Relay", fTitle, outerGlow, titleX + 1, titleY - 1);
                    g.DrawString("Fissal Relay", fTitle, outerGlow, titleX - 1, titleY + 1);

                    // Cream glowing neon core
                    using var coreNeon = new SolidBrush(Color.FromArgb(240, 255, 255, 210));
                    g.DrawString("Fissal Relay", fTitle, coreNeon, titleX, titleY);
                }

                // 4. 3D Embossed Subtitle & Active Palette Badge
                float subX = titleX;
                float subY = titleY + (int)(22 * _scale);
                string subText = $"Masser Matrix v{RelayVersion.Current} • [{Current.DisplayName.ToUpperInvariant()}]";
                using var fSub = Mono(7.2f, _scale, FontStyle.Bold);

                // 3D Indent shadow
                using (var subShadow = new SolidBrush(Color.FromArgb(255, 20, 10, 0)))
                    g.DrawString(subText, fSub, subShadow, subX - 1, subY - 1);

                // 3D Indent highlight
                using (var subHi = new SolidBrush(Color.FromArgb(140, 190, 190, 190)))
                    g.DrawString(subText, fSub, subHi, subX, subY + 1);

                // Subtitle core
                using (var subCore = new SolidBrush(Color.FromArgb(200, CGoldMid)))
                    g.DrawString(subText, fSub, subCore, subX, subY);
            };
            _headerConsole.Controls.Add(_titlePlatePanel, 0, 0);

            // 1B. Center Live Status Banner: Recessed Cathode Micro-Display Console with LED lamps
            _microDisplayPanel = new DoubleBufferedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Margin = new Padding((int)(6 * _scale), (int)(6 * _scale), (int)(6 * _scale), (int)(6 * _scale)),
                Cursor = Cursors.Default,
            };
            _microDisplayPanel.MouseDown += OnTitleBarMouseDown;
            _microDisplayPanel.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                int w = _microDisplayPanel.Width;
                int h = _microDisplayPanel.Height;
                if (w <= 10 || h <= 10) return;

                // 1. Recessed CRT screen plate bound dynamically to active theme
                using var mcPath = FissalTheme.RoundRect(0, 0, w - 1, h - 1, (int)(4 * _scale));
                using (var mcBg = new SolidBrush(Color.FromArgb(245, CPanelBgAlt)))
                    g.FillPath(mcBg, mcPath);

                // 2. Inner shadow gradient to push screen backward
                using (var mcInnerShadow = new LinearGradientBrush(
                    new Rectangle(0, 0, w, h),
                    Color.FromArgb(160, CBg), Color.Transparent, LinearGradientMode.Vertical))
                {
                    mcInnerShadow.SetBlendTriangularShape(0.25f);
                    g.FillPath(mcInnerShadow, mcPath);
                }

                // 3. CRT scanlines dynamically bound to active theme accent
                using (var mcScanPen = new Pen(Color.FromArgb(16, CAccent), 1f))
                {
                    for (int sy = 2; sy < h - 1; sy += 3)
                        g.DrawLine(mcScanPen, 1, sy, w - 2, sy);
                }

                // 4. Heavy metallic bezel & bottom specular highlight
                using (var mcBorder = new Pen(Color.FromArgb(255, 12, 14, 16), Math.Max(1.5f, 2f * _scale)))
                    g.DrawPath(mcBorder, mcPath);

                using (var mcHighlight = new Pen(Color.FromArgb(50, 255, 255, 255), 1f))
                    g.DrawLine(mcHighlight, (int)(3 * _scale), h - 1, w - (int)(3 * _scale), h - 1);

                // 5. Gather dynamic statuses
                var statuses = new List<(string text, Color color, int type)>();
                int active = 0, pending = 0;
                string errorFile = "";
                bool hasError = false, hasReadyUpdate = false;

                var safeJobs = _watcher.GetJobsSnapshot();

                foreach (var j in safeJobs)
                {
                    if (j.Status == UploadStatus.UpdateReady) hasReadyUpdate = true;
                    if (j.Status == UploadStatus.Failed || j.Status == UploadStatus.Cancelled)
                    {
                        hasError = true;
                        if (string.IsNullOrEmpty(errorFile)) errorFile = j.FileName;
                    }
                    if (j.Status == UploadStatus.Uploading) active++;
                    if (j.Status == UploadStatus.Queued) pending++;
                }

                if (!_isConnected)
                {
                    statuses.Add(("⚠ DISCONNECTED FROM REDFUR RELAY // RETRYING TONAL LINK", CBarFail, 2));
                }
                if (hasError)
                {
                    statuses.Add(($"!! ERROR IN {errorFile.ToUpper()} !!", Color.FromArgb(255, 255, 45, 45), 2));
                }
                if (hasReadyUpdate)
                {
                    statuses.Add(("!! UPDATE AVAILABLE! CHECK LOGS TO APPLY", Color.FromArgb(255, 190, 160, 255), 3));
                }
                if (active > 0)
                {
                    var activeJobs = safeJobs.Where(j => j.Status == UploadStatus.Uploading).ToList();
                    string txt = active == 1 ? $"> {activeJobs[0].FileName.ToUpper()} SYNCING" : $"> {active} FILES TRANSMITTING";
                    statuses.Add((txt, Color.FromArgb(255, 255, 200, 0), 1));
                }
                else if (pending > 0)
                {
                    statuses.Add(($"# {pending} FILES PENDING TRANSMISSION", Color.FromArgb(255, 180, 255, 50), 1));
                }

                if (statuses.Count == 0)
                {
                    string dispName = AppConfig.Instance.DisplayName;
                    string userStatus = string.IsNullOrWhiteSpace(dispName) ? "" : $"> OPERATOR: {dispName.ToUpper()}";

                    statuses.Add(("> STAND BY... MONITORING ESO LIVE", CAccent, 0));
                    statuses.Add(("● TONAL TRANSCEIVER RESONANT • REDFUR RELAY", CGoldBrt, 0));
                    if (!string.IsNullOrEmpty(userStatus)) statuses.Add((userStatus, CText, 0));
                    if (safeJobs.Length > 0)
                    {
                        int doneCount = safeJobs.Count(j => j.Status == UploadStatus.Done);
                        statuses.Add(($"> {doneCount} TRANSMISSIONS ARCHIVED", CGreen, 0));
                    }
                }

                if (_dispStatusIdx >= statuses.Count) _dispStatusIdx = 0;
                var cur = statuses[_dispStatusIdx];
                string displayBadge = cur.text;
                Color badgeColor = cur.color;

                // 6. Draw 4 Status Indicator Lamps on the right side
                int lightDia = Math.Max(7, (int)(8 * _scale));
                int lightSpacing = Math.Max(14, (int)(16 * _scale));
                int totalLampsW = 3 * lightSpacing + lightDia + (int)(12 * _scale);
                int lampsStartX = w - totalLampsW - (int)(10 * _scale);
                int lampsY = (h - lightDia) / 2;

                Color[] lightColors = {
                    Color.FromArgb(50, 255, 50),   // 0: Standby / Normal (Green)
                    Color.FromArgb(255, 200, 0),   // 1: Transmitting / Active (Amber)
                    Color.FromArgb(255, 45, 45),   // 2: Anomaly / Error (Red)
                    Color.FromArgb(195, 120, 255)  // 3: Upgrade Matrix (Purple)
                };

                for (int i = 0; i < 4; i++)
                {
                    bool isLampActive = statuses.Exists(s => s.type == i);
                    bool isCurrent = cur.type == i;
                    int lx = lampsStartX + (i * lightSpacing);

                    // Off state / dark lens
                    using (var offBrush = new LinearGradientBrush(
                        new Rectangle(lx, lampsY, lightDia, lightDia),
                        Color.FromArgb(255, lightColors[i].R / 4, lightColors[i].G / 4, lightColors[i].B / 4),
                        Color.FromArgb(255, 12, 14, 18), LinearGradientMode.ForwardDiagonal))
                    {
                        g.FillEllipse(offBrush, lx, lampsY, lightDia, lightDia);
                    }

                    using (var rimPen = new Pen(Color.FromArgb(80, lightColors[i]), 1f))
                        g.DrawEllipse(rimPen, lx, lampsY, lightDia, lightDia);

                    if (isLampActive)
                    {
                        int alpha = (int)((isCurrent ? ((Math.Sin(_shimmer * 1.2) + 1.0) / 2.0) : 0.85) * 200 + 55);
                        alpha = Math.Clamp(alpha, 60, 255);

                        using (var litBrush = new SolidBrush(Color.FromArgb(alpha, lightColors[i])))
                            g.FillEllipse(litBrush, lx + 1, lampsY + 1, lightDia - 2, lightDia - 2);

                        // White hot core
                        using (var whiteCore = new SolidBrush(Color.FromArgb(alpha, Color.White)))
                            g.FillEllipse(whiteCore, lx + lightDia / 2 - 1, lampsY + lightDia / 2 - 1, 2, 2);

                        // Horizontal glow flare
                        using (var glowH = new SolidBrush(Color.FromArgb(alpha / 4, lightColors[i])))
                            g.FillEllipse(glowH, lx - (int)(3 * _scale), lampsY + (lightDia / 4), lightDia + (int)(6 * _scale), lightDia / 2);
                    }

                    // Specular glint
                    using (var glintBrush = new SolidBrush(Color.FromArgb(isLampActive ? 220 : 120, Color.White)))
                        g.FillEllipse(glintBrush, lx + 2, lampsY + 1, Math.Max(2, lightDia / 3), Math.Max(1, lightDia / 4));
                }

                // 7. Draw Marquee Phosphor Text on the left
                int textMaxW = Math.Max(60, lampsStartX - (int)(16 * _scale));
                var textClip = new Rectangle((int)(12 * _scale), 0, textMaxW, h);
                var gState = g.Save();
                g.SetClip(textClip);

                using var textFont = Mono(8.2f, _scale, FontStyle.Bold);
                var textSz = g.MeasureString(displayBadge, textFont);
                float drawX = (int)(12 * _scale);
                float drawY = (h - textSz.Height) / 2f;

                // Smooth marquee scroll if text overflows the CRT viewable boundary
                if (textSz.Width > textMaxW)
                {
                    float overflow = textSz.Width - textMaxW;
                    float cycle = (_animFrame * 1.5f) % (overflow + (80 * _scale));
                    if (cycle > overflow) cycle = 0; // pause at start before scrolling
                    drawX -= cycle;
                }

                // Subtle phosphor glow
                using (var glowTextBrush = new SolidBrush(Color.FromArgb(50, badgeColor)))
                {
                    g.DrawString(displayBadge, textFont, glowTextBrush, drawX - 1, drawY);
                    g.DrawString(displayBadge, textFont, glowTextBrush, drawX + 1, drawY);
                    g.DrawString(displayBadge, textFont, glowTextBrush, drawX, drawY - 1);
                    g.DrawString(displayBadge, textFont, glowTextBrush, drawX, drawY + 1);
                }

                // Core crisp text
                using (var coreTextBrush = new SolidBrush(badgeColor))
                {
                    g.DrawString(displayBadge, textFont, coreTextBrush, drawX, drawY);
                }

                g.Restore(gState);
            };
            _headerConsole.Controls.Add(_microDisplayPanel, 1, 0);

            // 1C. Right Window Controls Deck (Tactile circular dome industrial buttons)
            var rightDeck = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0, (int)(16 * _scale), (int)(4 * _scale), 0),
            };
            rightDeck.MouseDown += OnTitleBarMouseDown;

            Control MakeCircularWinBtn(Func<string> getSymbol, Color capColor, Action onClick)
            {
                int btnSize = (int)(26 * _scale);
                bool isHover = false;
                bool isPressed = false;
                var btn = new DoubleBufferedPanel
                {
                    Width = btnSize,
                    Height = btnSize,
                    Cursor = Cursors.Hand,
                    Margin = new Padding((int)(5 * _scale), 0, 0, 0),
                    BackColor = Color.Transparent,
                };
                btn.MouseEnter += (_, _) => { isHover = true; btn.Invalidate(); };
                btn.MouseLeave += (_, _) => { isHover = false; isPressed = false; btn.Invalidate(); };
                btn.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { isPressed = true; btn.Invalidate(); } };
                btn.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left) { isPressed = false; btn.Invalidate(); } };
                btn.Click += (_, _) => onClick();
                btn.Paint += (s, e) =>
                {
                    FissalTheme.DrawDwemerCircularButton(e.Graphics, new Rectangle(0, 0, btn.Width, btn.Height), getSymbol(), capColor, isHover, isPressed, _scale);
                };
                return btn;
            }

            rightDeck.Controls.Add(MakeCircularWinBtn(() => "✕", Color.FromArgb(195, 38, 38), () => Hide()));
            rightDeck.Controls.Add(MakeCircularWinBtn(() => "□", Color.FromArgb(68, 56, 40), () => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized));
            rightDeck.Controls.Add(MakeCircularWinBtn(() => "—", Color.FromArgb(68, 56, 40), () => WindowState = FormWindowState.Minimized));
            _compactToggleBtn = MakeCircularWinBtn(() => _isCompactMode ? "◰" : "◱", Color.FromArgb(68, 56, 40), ToggleCompactMode);
            rightDeck.Controls.Add(_compactToggleBtn);

            _headerConsole.Controls.Add(rightDeck, 2, 0);

            _rootLayout.Controls.Add(_headerConsole, 0, 0);
            _rootLayout.SetColumnSpan(_headerConsole, 2);

            // ── 2. Content Area Host (Row 1, Col 1) - Must be created BEFORE AddNavButton! ──
            _contentHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CBg,
                Margin = new Padding(0, 0, (int)(6 * _scale), (int)(6 * _scale)),
                Padding = new Padding((int)(4 * _scale)),
            };
            _rootLayout.Controls.Add(_contentHost, 1, 1);

            // ── 3. Navigation Rail (Row 1, Col 0) ──
            _navRail = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CPanelBg,
                Margin = new Padding((int)(6 * _scale), 0, (int)(4 * _scale), (int)(6 * _scale)),
                Padding = new Padding(0, (int)(4 * _scale), 0, (int)(4 * _scale)),
            };
            _navRail.Paint += (s, e) =>
            {
                var g = e.Graphics;
                DrawTerminalMesh(g, new Rectangle(0, 0, _navRail.Width, _navRail.Height), _scale, 5);
                using var p = new Pen(Color.FromArgb(40, CBorderSub), 1f);
                g.DrawRectangle(p, 0, 0, _navRail.Width - 1, _navRail.Height - 1);
            };

            var navStack = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                BackColor = Color.Transparent,
            };

            AddNavButton(navStack, "sync",        "⚡ Live Sync & Logs", "Live file sync monitor and batch history");
            AddNavButton(navStack, "addon",       "📜 ESO Addon",        "Addon install, version sync & game path");
            AddNavButton(navStack, "setup",       "🛠️ Setup & Pairing",  "Device token, display name, and pairing code");
            AddNavButton(navStack, "themes",      "🎨 Themes & Display", "11 Terminal color palettes and UI scaling");
            AddNavButton(navStack, "diagnostics", "⚙️ Diagnostics",      "Watcher status, log viewers, and debug controls");

            _navRail.Controls.Add(navStack);

            var navBadge = new DoubleBufferedPanel
            {
                Dock = DockStyle.Bottom,
                Height = (int)(62 * _scale),
                BackColor = Color.Transparent,
                Padding = new Padding((int)(8 * _scale), (int)(4 * _scale), (int)(8 * _scale), (int)(4 * _scale)),
            };
            navBadge.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // Stamped brass plaque background
                int w = navBadge.Width;
                int h = navBadge.Height;
                using (var plateBg = new LinearGradientBrush(
                    new Rectangle(2, 2, w - 4, h - 4),
                    Color.FromArgb(20, 17, 13), Color.FromArgb(12, 10, 8), LinearGradientMode.Vertical))
                {
                    g.FillRectangle(plateBg, 2, 2, w - 4, h - 4);
                }

                using (var plateBorder = new Pen(Color.FromArgb(60, CGoldDark), 1f))
                {
                    g.DrawRectangle(plateBorder, 2, 2, w - 5, h - 5);
                }

                DrawDivider(g, (int)(6 * _scale), w - (int)(6 * _scale), 2, CBorderSub, CGoldBrt);

                using var f1 = Mono(8f, _scale, FontStyle.Bold);
                using var b1 = new SolidBrush(CGoldBrt);
                g.DrawString("REDFUR SYNC", f1, b1, (int)(8 * _scale), (int)(8 * _scale));

                using var f2 = Mono(7f, _scale, FontStyle.Regular);
                using var b2 = new SolidBrush(CTextSub);
                g.DrawString($"v{RelayVersion.Current} • WIN-X64", f2, b2, (int)(8 * _scale), (int)(24 * _scale));

                using var f3 = Mono(7f, _scale, FontStyle.Bold);
                using var b3 = new SolidBrush(CGreen);
                g.DrawString("● ONLINE", f3, b3, (int)(8 * _scale), (int)(40 * _scale));

                // Decorative corner micro-rivets on badge
                DrawCornerRivets(g, w - 4, h - 4, (int)(2 * _scale), Color.FromArgb(100, CGoldMid));
            };
            _navRail.Controls.Add(navBadge);
            _rootLayout.Controls.Add(_navRail, 0, 1);

            Controls.Add(_rootLayout);
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
            int btnHeight = (int)(52 * _scale);  // D1: was 42px, bigger visual weight
            int btnWidth = (int)(188 * _scale);

            var itemPanel = new DoubleBufferedPanel
            {
                Width = btnWidth,
                Height = btnHeight,
                Margin = new Padding((int)(4 * _scale), (int)(3 * _scale), (int)(4 * _scale), (int)(3 * _scale)),
                BackColor = Color.Transparent,
            };

            itemPanel.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                bool active = _activeTabId == id;

                int w = itemPanel.Width;
                int h = itemPanel.Height;

                if (active)
                {
                    // Engaged mechanical lever bay: recessed metallic bed
                    using var bgBrush = new LinearGradientBrush(
                        new Rectangle(0, 0, w, h),
                        Color.FromArgb(24, 28, 34), Color.FromArgb(14, 17, 21), LinearGradientMode.Vertical);
                    g.FillRectangle(bgBrush, 0, 0, w - 1, h - 1);

                    // Dual metallic border with brass gleam
                    using var borderPen = new Pen(CGoldMid, 1.25f);
                    g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);

                    // Tactile mechanical lever / luminous jewel prism on the left edge
                    int barW = (int)(6 * _scale);
                    using var jewelBrush = new SolidBrush(CGreen);
                    g.FillRectangle(jewelBrush, 1, 1, barW, h - 2);

                    // Glowing jewel halo
                    using var glowBrush = new SolidBrush(Color.FromArgb(110, CGreen));
                    g.FillRectangle(glowBrush, barW + 1, 1, (int)(3 * _scale), h - 2);

                    // Top inner shadow for depth
                    using var shadowPen = new Pen(Color.FromArgb(90, 0, 0, 0), 1f);
                    g.DrawLine(shadowPen, barW, 1, w - 2, 1);
                }
                else
                {
                    // Unengaged slot with subtle etched border
                    using var slotBrush = new SolidBrush(Color.FromArgb(13, 11, 9));
                    g.FillRectangle(slotBrush, 0, 0, w - 1, h - 1);

                    using var borderPen = new Pen(Color.FromArgb(35, CBorderSub), 1f);
                    g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);

                    // Subtle cold lever pip
                    int pipW = (int)(3 * _scale);
                    using var pipBrush = new SolidBrush(Color.FromArgb(40, CGoldDark));
                    g.FillRectangle(pipBrush, 1, 1, pipW, h - 2);
                }
            };

            var btn = new Button
            {
                Dock = DockStyle.Fill,
                Text = title,
                TextAlign = ContentAlignment.MiddleLeft,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = CTextSub,
                Font = Body(8.5f, _scale, FontStyle.Regular),
                Cursor = Cursors.Hand,
                Margin = new Padding(0),
                Padding = new Padding((int)(12 * _scale), 0, 0, 0),
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(35, 180, 140, 50);

            var viewPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CBg,
                Visible = false,
                Tag = "view-root",
            };

            btn.Click += (_, _) => SwitchTab(id);

            itemPanel.Controls.Add(btn);
            container.Controls.Add(itemPanel);

            _navItems.Add((id, itemPanel, btn, viewPanel));
            _contentHost.Controls.Add(viewPanel);
        }

        private void SwitchTab(string id)
        {
            _activeTabId = id;
            foreach (var item in _navItems)
            {
                bool active = item.id == id;
                item.btn.ForeColor = active ? CGoldBrt : CTextSub;
                item.btn.Font = Body(8.5f, _scale, active ? FontStyle.Bold : FontStyle.Regular);
                item.panel.Invalidate();
                item.viewPanel.Visible = active;
                if (active) item.viewPanel.BringToFront();
            }

            if (id == "sync") RefreshSyncView();
            else if (id == "addon")
            {
                RefreshAddonView();
                _ = Task.Run(async () =>
                {
                    bool found = await AddonInstallerService.CheckRemoteAddonVersionAsync(AppConfig.Instance.ServerUrl);
                    if (found && _addonView != null && !_addonView.IsDisposed)
                    {
                        try { BeginInvoke(() => RefreshAddonView()); } catch { }
                    }
                });
            }
            else if (id == "diagnostics") RefreshDiagnosticsView();
            else if (id == "setup") RefreshSetupView();
        }

        private void BuildViewPanels()
        {
            _syncView = _navItems.First(x => x.id == "sync").viewPanel;
            _addonView = _navItems.First(x => x.id == "addon").viewPanel;
            _setupView = _navItems.First(x => x.id == "setup").viewPanel;
            _themesView = _navItems.First(x => x.id == "themes").viewPanel;
            _diagnosticsView = _navItems.First(x => x.id == "diagnostics").viewPanel;

            InitSyncView();
            InitAddonView();
            InitSetupView();
            InitThemesView();
            InitDiagnosticsView();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 1. LIVE SYNC & LOGS VIEW
        // ═════════════════════════════════════════════════════════════════════
        private void InitSyncView()
        {
            var layout = new DoubleBufferedTableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Color.Transparent,
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(44 * _scale))); // Top status ribbon (comfortably padded)
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));                  // Active transmissions deck
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));                  // Live Tonal Telemetry Slate

            // Top Header Bar
            var topBar = new DoubleBufferedTableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = CPanelBg,
                Padding = new Padding((int)(10 * _scale), (int)(4 * _scale), (int)(10 * _scale), (int)(4 * _scale)),
            };
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // Badge
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // Summary
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // Refresh
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // Clear
            topBar.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int w = topBar.Width;
                int h = topBar.Height;
                if (w <= 5 || h <= 5) return;

                // Deep dark CRT ribbon bed
                using (var ribbonBg = new SolidBrush(Color.FromArgb(16, 14, 11)))
                    g.FillRectangle(ribbonBg, 0, 0, w, h);

                DrawTerminalMesh(g, new Rectangle(0, 0, w, h), _scale, 5);

                // Dual metallic border with brass gleam
                using (var p = new Pen(Color.FromArgb(70, CGoldDark), 1f))
                    g.DrawRectangle(p, 0, 0, w - 1, h - 1);

                using (var hiPen = new Pen(Color.FromArgb(40, 255, 255, 255), 1f))
                    g.DrawLine(hiPen, (int)(4 * _scale), 1, w - (int)(4 * _scale), 1);
            };

            _syncStateBadge = new Label
            {
                Text = "⚡ SYNC STREAM",
                ForeColor = CGreen,
                Font = Title(9f, _scale, FontStyle.Bold),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
            };
            topBar.Controls.Add(_syncStateBadge, 0, 0);

            _syncSummaryLabel = new Label
            {
                Text = "Ready — Monitoring ESO Sales Data",
                ForeColor = CText,
                Font = Body(8f, _scale),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding((int)(10 * _scale), 0, 0, 0),
            };
            topBar.Controls.Add(_syncSummaryLabel, 1, 0);

            _btnRefreshJobs = MakeStyledButton("Refresh", CGreen);
            _btnRefreshJobs.Height = (int)(28 * _scale);
            _btnRefreshJobs.Margin = new Padding((int)(4 * _scale), 0, (int)(4 * _scale), 0);
            _btnRefreshJobs.Click += (_, _) =>
            {
                RefreshSyncView();
                LogTelemetry("REFRESH", $"Audited {_watcher.GetJobsSnapshot().Length} live transmission cassettes.", CGoldBrt);
            };
            topBar.Controls.Add(_btnRefreshJobs, 2, 0);

            _btnClearCompleted = MakeStyledButton("Clear Done", CTextSub);
            _btnClearCompleted.Height = (int)(28 * _scale);
            _btnClearCompleted.Margin = new Padding((int)(4 * _scale), 0, (int)(4 * _scale), 0);
            _btnClearCompleted.Click += (_, _) =>
            {
                int removed = _watcher.RemoveCompletedJobs();
                RefreshSyncView();
                LogTelemetry("CLEARED", $"Cleared {removed} completed sync records from live deck.", CTextSub);
            };
            topBar.Controls.Add(_btnClearCompleted, 3, 0);

            layout.Controls.Add(topBar, 0, 0);

            // Center Scrollable Jobs List (Upper Deck)
            _syncJobsList = new DoubleBufferedFlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.FromArgb(10, 9, 7),
                Padding = new Padding((int)(6 * _scale)),
            };
            _syncJobsList.HandleCreated += (_, _) => FissalTheme.ApplyDarkScrollbars(_syncJobsList.Handle);
            _syncJobsList.Resize += (_, _) => ResizeSyncJobCards();
            _syncJobsList.Paint += (s, e) =>
            {
                var g = e.Graphics;
                int w = _syncJobsList.Width;
                int h = _syncJobsList.Height;
                if (w <= 5 || h <= 5) return;

                DrawTerminalMesh(g, new Rectangle(0, 0, w, h), _scale, 5);

                // Authentic CRT Raster Scanlines
                if (UploadProgressForm.AppConfig.FX.ScreenScanlines || UploadProgressForm.AppConfig.FX.ScreenHeavyGlitch)
                {
                    int scanAlpha = UploadProgressForm.AppConfig.FX.ScreenScanlines ? 28 : 14;
                    using var scanPen = new Pen(Color.FromArgb(scanAlpha, 12, 18, 14), 1.2f);
                    for (float sy = _scanPhase; sy < h; sy += 3.5f)
                        g.DrawLine(scanPen, 0, sy, w, sy);

                    // Reactive cathode ray phosphor sweep
                    if (UploadProgressForm.AppConfig.CurrentMode != FidelityMode.Low)
                    {
                        float sweepY = ((_animFrame * 2.2f) % Math.Max(20, h));
                        using var sweepBrush = new LinearGradientBrush(
                            new RectangleF(0, sweepY - 15, w, 30),
                            Color.Transparent,
                            Color.FromArgb((int)(16 * _crtFlicker), 80, 240, 180),
                            LinearGradientMode.Vertical);
                        g.FillRectangle(sweepBrush, 0, sweepY - 15, w, 30);
                    }
                }
            };
            layout.Controls.Add(_syncJobsList, 0, 1);

            // Lower Live Tonal Telemetry Slate
            var telemetrySlate = new DoubleBufferedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = CPanelBg,
                Margin = new Padding(0, (int)(4 * _scale), 0, 0),
                Padding = new Padding((int)(6 * _scale)),
            };
            telemetrySlate.Paint += (s, e) =>
            {
                var g = e.Graphics;
                DrawTerminalMesh(g, new Rectangle(0, 0, telemetrySlate.Width, telemetrySlate.Height), _scale, 5);
                using var pen = new Pen(CBorderSub, 1f);
                g.DrawRectangle(pen, 0, 0, telemetrySlate.Width - 1, telemetrySlate.Height - 1);
            };

            var telTable = new DoubleBufferedTableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Color.Transparent,
            };
            telTable.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(22 * _scale))); // Header
            telTable.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(40 * _scale))); // Dual monitor: Oscilloscope & Ticker
            telTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100));                 // Log Box

            var telHeader = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
            };
            telHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            telHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var telTitle = new Label
            {
                Text = "📡 TONAL TRANSMISSION TELEMETRY & SYSTEM LOG",
                ForeColor = CGoldBrt,
                Font = Mono(8f, _scale, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            var btnClearLog = new Label
            {
                Text = "[Clear]",
                ForeColor = CTextSub,
                Font = Mono(7.5f, _scale),
                Cursor = Cursors.Hand,
                AutoSize = true,
                Anchor = AnchorStyles.Right,
            };
            btnClearLog.Click += (_, _) => _syncLogBox?.Clear();
            telHeader.Controls.Add(telTitle, 0, 0);
            telHeader.Controls.Add(btnClearLog, 1, 0);
            telTable.Controls.Add(telHeader, 0, 0);

            // Dual Monitor Strip: Left Cathode Oscilloscope + Right Telemetry Ticker
            var dualMonitorStrip = new DoubleBufferedTableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, (int)(2 * _scale)),
            };
            dualMonitorStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(180 * _scale))); // Oscilloscope CRT
            dualMonitorStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));                  // Ticker
            dualMonitorStrip.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // D8: label the oscilloscope so users know what it shows
            var oscLabel = new Label
            {
                Text = "UPLOAD ACTIVITY",
                ForeColor = Color.FromArgb(120, CGoldBrt),
                Font = Mono(7f, _scale, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, (int)(4 * _scale), 0, 0),
            };
            _oscilloscopePanel = new DoubleBufferedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(4, 9, 6),
                Margin = new Padding(0, 0, (int)(4 * _scale), 0),
            };
            _oscilloscopePanel.Paint += (s, e) =>
            {
                var g = e.Graphics;
                var activeJob = _watcher.GetJobsSnapshot().FirstOrDefault(j => j.Status == UploadStatus.Uploading);
                bool isTx = activeJob != null;
                Color waveCol = isTx ? CGoldBrt : CGreen;
                DrawTonalWaveform(g, new Rectangle(0, 0, _oscilloscopePanel.Width, _oscilloscopePanel.Height), _animFrame * 0.22f, waveCol, isTx, _scale);
            };
            dualMonitorStrip.Controls.Add(_oscilloscopePanel, 0, 0);

            _tickerPanel = new DoubleBufferedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(14, 11, 8),
                Padding = new Padding((int)(8 * _scale), 0, (int)(8 * _scale), 0),
            };
            _tickerPanel.Paint += (s, e) =>
            {
                var g = e.Graphics;
                using var pen = new Pen(Color.FromArgb(40, CGoldMid), 1f);
                g.DrawRectangle(pen, 0, 0, _tickerPanel.Width - 1, _tickerPanel.Height - 1);
            };

            _tickerLabel = new Label
            {
                Text = "● TONAL TRANSCEIVER RESONANT • READY FOR ESO TELEMETRY",
                ForeColor = CGreen,
                Font = Mono(7.5f, _scale, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
            };
            _tickerPanel.Controls.Add(_tickerLabel);
            dualMonitorStrip.Controls.Add(_tickerPanel, 1, 0);

            telTable.Controls.Add(dualMonitorStrip, 0, 1);

            _animTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _animTimer.Tick += (_, _) => OnAnimTimerTick();
            _animTimer.Start();

            _syncLogBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(6, 6, 5),
                ForeColor = CText,
                Font = Mono(8f, _scale),
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.Vertical,
            };
            _syncLogBox.HandleCreated += (_, _) => FissalTheme.ApplyDarkScrollbars(_syncLogBox.Handle);
            telTable.Controls.Add(_syncLogBox, 0, 2);
            telemetrySlate.Controls.Add(telTable);
            layout.Controls.Add(telemetrySlate, 0, 2);

            _syncView.Controls.Add(layout);
        }

        private void SeedInitialTelemetry()
        {
            LogTelemetry("SYSTEM", $"Fissal Relay client online (v{RelayVersion.Current}) • Connected to homelab lattice.", CGreen);
            LogTelemetry("HARVEST", "Continuous sales and bank deposit ingestion engine active.", CGreen);
            LogTelemetry("RECON", "In-person guild kiosk observer active on EVENT_OPEN_TRADING_HOUSE.", CGoldBrt);
            LogTelemetry("WATCH", "Monitoring ESO SavedVariables directory for live trade and raffle data.", CGreen);
        }

        private void OnAnimTimerTick()
        {
            if (IsDisposed || !Visible || WindowState == FormWindowState.Minimized) return;
            _animFrame++;

            _scanPhase = (_scanPhase + 0.35f) % 4f;
            _shimmer += 0.1f;

            // CRT phosphor voltage & luminescence ripple
            if (UploadProgressForm.AppConfig.CurrentMode != FidelityMode.Low)
            {
                float baseNoise = ((float)_rand.NextDouble() - 0.5f) * (_batchInProgress ? 0.08f : 0.04f);
                float sinRipple = (float)Math.Sin(_shimmer * 0.8f) * 0.02f;
                _crtFlicker = Math.Clamp(1.0f + baseNoise + sinRipple, 0.88f, 1.12f);
            }
            else
            {
                _crtFlicker = 1.0f;
            }

            // Vacuum tube filament and gas glow pulse
            var jobsSnapshot = _watcher.GetJobsSnapshot();
            bool isUploading = jobsSnapshot.Any(j => j.Status == UploadStatus.Uploading);
            bool hasError = jobsSnapshot.Any(j => j.Status == UploadStatus.Failed || j.Status == UploadStatus.Cancelled);
            bool hasUpdate = jobsSnapshot.Any(j => j.Status == UploadStatus.UpdateReady);

            int targetStep = hasError ? 2 : isUploading ? 3 : hasUpdate ? 1 : 1;
            _glowStep = _glowStep > 0 ? targetStep : -targetStep;
            _glowAlpha += _glowStep;
            if (_glowAlpha >= 240) { _glowAlpha = 240; _glowStep = -targetStep; }
            if (_glowAlpha <= 40) { _glowAlpha = 40; _glowStep = targetStep; }

            _coreColor = !_isConnected ? CBarFail
                       : hasError ? CBarFail
                       : isUploading ? Color.FromArgb(60, 180, 220)
                       : hasUpdate ? Color.FromArgb(180, 100, 220)
                       : Color.FromArgb(255, 200, 120);

            _auraColor = !_isConnected ? CBarFail
                       : hasError ? CBarFail
                       : isUploading ? Color.FromArgb(60, 180, 220)
                       : hasUpdate ? Color.FromArgb(200, 120, 240)
                       : Color.FromArgb(240, 150, 40);

            // Cycle marquee status message every 35 frames (~3.5 seconds)
            _dispWait--;
            if (_dispWait <= 0)
            {
                _dispWait = 35;
                _dispStatusIdx++;
            }

            _titlePlatePanel?.Invalidate();
            _microDisplayPanel?.Invalidate();
            _oscilloscopePanel?.Invalidate();

            var activeJob = jobsSnapshot.FirstOrDefault(j => j.Status == UploadStatus.Uploading);
            if (activeJob == null)
            {
                int queued = jobsSnapshot.Count(j => j.Status == UploadStatus.Queued);
                if (queued > 0)
                {
                    if (_tickerLabel != null && !_tickerLabel.IsDisposed)
                    {
                        _tickerLabel.Text = $"⏳ [QUEUED] {queued} file{(queued == 1 ? "" : "s")} awaiting tonal frequency window...";
                        _tickerLabel.ForeColor = CWarn;
                    }
                }
                else
                {
                    if (_tickerLabel != null && !_tickerLabel.IsDisposed)
                    {
                        _tickerLabel.Text = "● TONAL TRANSCEIVER RESONANT • READY FOR ESO TELEMETRY";
                        _tickerLabel.ForeColor = CGreen;
                    }
                }
                return;
            }

            if (_tickerLabel != null && !_tickerLabel.IsDisposed)
            {
                string[] spinners = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
                string spin = spinners[_animFrame % spinners.Length];

                string[] waves = { " ▃▅▆", "▃▅▆▇", "▅▆▇▆", "▆▇▆▅", "▇▆▅▃", "▆▅▃ ", "▅▃  ", "▃   " };
                string wave = waves[_animFrame % waves.Length];

                int pct = (int)(activeJob.Progress * 100);
                string bar = BuildAsciiBar(activeJob.Progress, 8);

                _tickerLabel.Text = $"{spin} [TRANSMITTING] {activeJob.FileName} • {pct}% {bar} {wave} REDFUR RELAY";
                _tickerLabel.ForeColor = CGoldBrt;
            }

            // Smooth live animation of active transmitting rows (plasma gauge & carrier waves)
            if (isUploading)
            {
                int animCycle = (_animFrame / 2) % 4;
                string waveGlyph = animCycle switch { 0 => "▪▫▫▫", 1 => "▫▪▫▫", 2 => "▫▫▪▫", _ => "▫▫▫▪" };

                foreach (var sc in _sessionCards.Values)
                {
                    if (sc.Session.AggregateStatus == UploadStatus.Uploading)
                    {
                        sc.ChevronLabel?.Parent?.Invalidate(); // Refresh session header plasma gauge
                        if (sc.StatusLabel != null && !sc.StatusLabel.IsDisposed)
                        {
                            sc.StatusLabel.Text = $"⚡ TRANSMITTING {waveGlyph} ({sc.Session.DoneCount}/{sc.Session.TotalCount})";
                        }
                    }

                    foreach (var kvp in sc.FileRows)
                    {
                        if (kvp.Key.Status == UploadStatus.Uploading)
                        {
                            kvp.Value.Row.Invalidate();
                            if (kvp.Value.StatusLabel != null && !kvp.Value.StatusLabel.IsDisposed)
                            {
                                kvp.Value.StatusLabel.Text = $"⚡ TRANSMITTING {waveGlyph} {(int)(kvp.Key.Progress * 100)}%";
                            }
                        }
                    }
                }

                foreach (var kvp in _jobCards)
                {
                    if (kvp.Key.Status == UploadStatus.Uploading)
                    {
                        kvp.Value.Card.Invalidate();
                        if (kvp.Value.StatusLabel != null && !kvp.Value.StatusLabel.IsDisposed)
                        {
                            kvp.Value.StatusLabel.Text = $"⚡ TRANSMITTING {waveGlyph} {(int)(kvp.Key.Progress * 100)}%";
                        }
                    }
                }
            }
        }

        private static string BuildAsciiBar(float progress, int width)
        {
            int filled = Math.Clamp((int)(progress * width), 0, width);
            return $"[{new string('■', filled)}{new string('□', width - filled)}]";
        }

        public void LogTelemetry(string tag, string message, Color color)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => LogTelemetry(tag, message, color));
                return;
            }
            if (_syncLogBox == null || _syncLogBox.IsDisposed) return;

            string ts = DateTime.Now.ToString("HH:mm:ss");
            int lineCount = _syncLogBox.GetLineFromCharIndex(_syncLogBox.TextLength);
            if (lineCount > 300)
            {
                int charIdx = _syncLogBox.GetFirstCharIndexFromLine(50);
                if (charIdx > 0)
                {
                    _syncLogBox.Select(0, charIdx);
                    _syncLogBox.SelectedText = "";
                }
            }

            string tagPrefix = tag switch
            {
                "TRANSMIT" => "⚡ TRANSMIT",
                "VERIFIED" => "✓ VERIFIED",
                "BATCH" => "📦 BATCH",
                "ALERT" => "⚠ ALERT",
                "UPGRADE" => "📦 UPGRADE",
                "RECON" => "✦ RECON",
                "BANK" => "⚖ BANK",
                "ONLINE" => "● ONLINE",
                "SYSTEM" => "⚙ SYSTEM",
                "HARVEST" => "🌾 HARVEST",
                "WATCH" => "👁 WATCH",
                "REFRESH" => "🔄 REFRESH",
                "CLEARED" => "🧹 CLEARED",
                _ => tag
            };

            _syncLogBox.SelectionStart = _syncLogBox.TextLength;
            _syncLogBox.SelectionLength = 0;

            _syncLogBox.SelectionColor = CTextSub;
            _syncLogBox.AppendText($"[{ts}] ");

            _syncLogBox.SelectionColor = color;
            _syncLogBox.AppendText($"[{tagPrefix}] ");

            _syncLogBox.SelectionColor = CText;
            _syncLogBox.AppendText($"{message}\n");

            _syncLogBox.SelectionStart = _syncLogBox.TextLength;
            if (_syncLogBox.IsHandleCreated)
            {
                SendMessage(_syncLogBox.Handle, WM_VSCROLL, SB_BOTTOM, 0);
            }
            else
            {
                _syncLogBox.ScrollToCaret();
            }
        }

        private bool IsSessionExpanded(SyncSessionModel session)
        {
            if (_userCollapsedSessions.Contains(session.Id)) return false;
            if (_userExpandedSessions.Contains(session.Id)) return true;
            return session.AggregateStatus is UploadStatus.Uploading or UploadStatus.Failed;
        }

        private void RefreshSyncView()
        {
            if (InvokeRequired)
            {
                BeginInvoke(RefreshSyncView);
                return;
            }

            if (IsDisposed || !IsHandleCreated || !Visible || WindowState == FormWindowState.Minimized) return;
            if (_isRefreshingSyncView) return;
            _isRefreshingSyncView = true;

            try
            {
                _lastRenderedJobVersion = _watcher.CurrentJobVersion;
                var jobs = _watcher.GetJobsSnapshot();
                int queued = jobs.Count(j => j.Status == UploadStatus.Queued);
                int uploading = jobs.Count(j => j.Status == UploadStatus.Uploading);
                int done = jobs.Count(j => j.Status == UploadStatus.Done);
                int failed = jobs.Count(j => j.Status is UploadStatus.Failed or UploadStatus.Cancelled);

                if (uploading > 0)
                {
                    _syncStateBadge.Text = "⚡ TRANSMITTING...";
                    _syncStateBadge.ForeColor = CGoldBrt;
                    _messageBoardText = $"Transmitting {uploading} active file{(uploading == 1 ? "" : "s")} ({queued} queued)...";
                    _messageBoardColor = CGoldBrt;
                    _titleStatusLabel.Text = "⚡ TRANSMITTING TELEMETRY";
                    _titleStatusLabel.ForeColor = CGoldBrt;

                    if (_animTimer != null && !_animTimer.Enabled)
                    {
                        _animTimer.Start();
                    }
                }
                else if (queued > 0)
                {
                    _syncStateBadge.Text = "⏳ QUEUED";
                    _syncStateBadge.ForeColor = CWarn;
                    _messageBoardText = $"Sync Queued: {queued} file{(queued == 1 ? "" : "s")} ready to upload";
                    _messageBoardColor = CWarn;
                    _titleStatusLabel.Text = "⏳ QUEUED FOR TRANSMISSION";
                    _titleStatusLabel.ForeColor = CWarn;
                }
                else
                {
                    _syncStateBadge.Text = "● SYNC IDLE";
                    _syncStateBadge.ForeColor = CGreen;
                    _messageBoardText = failed > 0 ? $"Notice: {failed} file{(failed == 1 ? "" : "s")} failed to upload" : "All guild data synchronized • Watching for ESO updates";
                    _messageBoardColor = failed > 0 ? CBarFail : CGreen;
                    _titleStatusLabel.Text = failed > 0 ? "⚠ ATTENTION NEEDED" : "● CONNECTED TO REDFUR RELAY";
                    _titleStatusLabel.ForeColor = failed > 0 ? CBarFail : CGreen;
                }

                if (_messageBoardLabel != null)
                {
                    _messageBoardLabel.Text = _messageBoardText;
                    _messageBoardLabel.ForeColor = _messageBoardColor;
                }

                // Stream batch logging boundary markers
                if (uploading > 0 && !_batchInProgress)
                {
                    _batchInProgress = true;
                    LogTelemetry("BATCH", $"┌─── ⚙ INGESTION STREAM ENGAGED: {uploading + queued} file(s) queued for transmission ───", CGoldBrt);
                }
                else if (uploading == 0 && queued == 0 && _batchInProgress)
                {
                    _batchInProgress = false;
                    LogTelemetry("BATCH", $"└─── ✓ BATCH COMPLETE: All telemetry cassettes synchronized to Redfur Relay ───", CGreen);
                }

                // Telemetry tracking for individual state transitions
                foreach (var job in jobs)
                {
                    if (!_loggedJobStates.TryGetValue(job, out var lastStatus) || lastStatus != job.Status)
                    {
                        _loggedJobStates[job] = job.Status;
                        if (job.Status == UploadStatus.Done)
                            LogTelemetry("VERIFIED", $"{job.FileName} synchronized to Redfur Relay ({job.FileSizeDisplay})", CGreen);
                        else if (job.Status == UploadStatus.Failed)
                            LogTelemetry("ALERT", $"{job.FileName} failed: {job.ErrorMessage}", CBarFail);
                        else if (job.Status == UploadStatus.UpdateReady)
                            LogTelemetry("UPGRADE", $"{job.FileName} ready for deployment ({job.FileSizeDisplay})", Color.FromArgb(196, 137, 255));
                        else if (job.Status == UploadStatus.Uploading)
                            LogTelemetry("TRANSMIT", $"{job.FileName} uploading ({job.FileSizeDisplay})...", CGoldBrt);
                    }
                }

                if (_loggedJobStates.Count > jobs.Length + 64)
                {
                    var activeSet = new HashSet<UploadJob>(jobs);
                    var staleJobs = _loggedJobStates.Keys.Where(k => !activeSet.Contains(k)).ToList();
                    foreach (var stale in staleJobs)
                        _loggedJobStates.Remove(stale);
                }

                // Separate standalone update jobs and cluster regular files into sessions
                var updateJobs = jobs.Where(j => j.IsUpdate).ToList();
                var normalJobs = jobs.Where(j => !j.IsUpdate).OrderBy(j => j.QueuedAt).ToList();

                var sessions = new List<SyncSessionModel>();
                SyncSessionModel? currentSession = null;
                foreach (var job in normalJobs)
                {
                    if (currentSession == null || Math.Abs((currentSession.Timestamp - job.QueuedAt).TotalSeconds) > 60)
                    {
                        string sessionId = $"session_{job.QueuedAt:yyyyMMdd_HHmmss}";
                        currentSession = new SyncSessionModel
                        {
                            Id = sessionId,
                            Timestamp = job.QueuedAt,
                            Title = $"ESO DATA BATCH • {job.QueuedAt:HH:mm:ss}",
                        };
                        sessions.Add(currentSession);
                    }
                    currentSession.Jobs.Add(job);
                }

                // Prioritize sessions: batches with errors first, then in-progress/uploading, then queued, then done (newest first within each tier)
                var orderedSessions = sessions
                    .OrderBy(s => GetJobStatusPriority(s.AggregateStatus))
                    .ThenByDescending(s => s.Timestamp)
                    .ToList();

                _syncSummaryLabel.Text = $"Sessions: {sessions.Count}  |  Active: {uploading}  |  Queued: {queued}  |  Synced: {done}  |  Errors: {failed}  |  Total: {jobs.Length}";

                _syncJobsList.SuspendLayout();

                // 1. Remove empty placeholder if we have content
                if (jobs.Length > 0)
                {
                    for (int i = _syncJobsList.Controls.Count - 1; i >= 0; i--)
                    {
                        if (Equals(_syncJobsList.Controls[i].Tag, "empty-card"))
                        {
                            var c = _syncJobsList.Controls[i];
                            _syncJobsList.Controls.RemoveAt(i);
                            c.Dispose();
                        }
                    }
                }

                // 2. Reconcile standalone update cards
                var existingUpdateJobs = _jobCards.Keys.ToList();
                foreach (var ej in existingUpdateJobs)
                {
                    if (!updateJobs.Contains(ej))
                    {
                        var card = _jobCards[ej];
                        _syncJobsList.Controls.Remove(card.Card);
                        card.Card.Dispose();
                        _jobCards.Remove(ej);
                    }
                }

                foreach (var uj in updateJobs)
                {
                    if (_jobCards.TryGetValue(uj, out var cardControls))
                    {
                        UpdateJobCard(uj, cardControls);
                    }
                    else
                    {
                        var newControls = BuildJobCard(uj);
                        _jobCards.Add(uj, newControls);
                        _syncJobsList.Controls.Add(newControls.Card);
                    }
                }

                // 3. Reconcile session cards
                var currentSessionIds = orderedSessions.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var existingSessionIds = _sessionCards.Keys.ToList();
                foreach (var oldId in existingSessionIds)
                {
                    if (!currentSessionIds.Contains(oldId))
                    {
                        var sc = _sessionCards[oldId];
                        _syncJobsList.Controls.Remove(sc.Card);
                        sc.Card.Dispose();
                        _sessionCards.Remove(oldId);
                    }
                }

                foreach (var session in orderedSessions)
                {
                    if (_sessionCards.TryGetValue(session.Id, out var cardControls))
                    {
                        UpdateSessionCard(session, cardControls);
                    }
                    else
                    {
                        var newControls = BuildSessionCard(session);
                        _sessionCards.Add(session.Id, newControls);
                        _syncJobsList.Controls.Add(newControls.Card);
                    }
                }

                // 4. Ensure correct visual ordering: update cards first, then sessions
                int controlIndex = 0;
                foreach (var uj in updateJobs)
                {
                    if (_jobCards.TryGetValue(uj, out var uc))
                    {
                        if (_syncJobsList.Controls.GetChildIndex(uc.Card) != controlIndex)
                            _syncJobsList.Controls.SetChildIndex(uc.Card, controlIndex);
                        controlIndex++;
                    }
                }
                foreach (var s in orderedSessions)
                {
                    if (_sessionCards.TryGetValue(s.Id, out var sc))
                    {
                        if (_syncJobsList.Controls.GetChildIndex(sc.Card) != controlIndex)
                            _syncJobsList.Controls.SetChildIndex(sc.Card, controlIndex);
                        controlIndex++;
                    }
                }

                // 5. Empty placeholder when no jobs exist
                if (jobs.Length == 0 && _syncJobsList.Controls.Count == 0)
                {
                    var emptyCard = new DoubleBufferedPanel
                    {
                        Width = Math.Max(300, _syncJobsList.ClientSize.Width - 16),
                        Height = (int)(44 * _scale),
                        BackColor = Color.FromArgb(14, 12, 10),
                        Margin = new Padding(0, 4, 0, 4),
                        Padding = new Padding((int)(12 * _scale), 0, (int)(12 * _scale), 0),
                        Tag = "empty-card",
                    };
                    emptyCard.Paint += (s, e) =>
                    {
                        var g = e.Graphics;
                        using var pen = new Pen(Color.FromArgb(40, CBorderSub), 1f);
                        g.DrawRectangle(pen, 0, 0, emptyCard.Width - 1, emptyCard.Height - 1);
                    };
                    var emptyLabel = new Label
                    {
                        Text = "✓ All guild data synchronized with Redfur Relay. Monitoring for changes.",
                        ForeColor = CTextSub,
                        Font = Body(8.5f, _scale, FontStyle.Italic),
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                    };
                    emptyCard.Controls.Add(emptyLabel);
                    _syncJobsList.Controls.Add(emptyCard);
                }

                ResizeSyncJobCards();
                _syncJobsList.ResumeLayout(true);
            }
            finally
            {
                _isRefreshingSyncView = false;
            }
        }

        private SessionCardControls BuildSessionCard(SyncSessionModel session)
        {
            var card = new DoubleBufferedPanel
            {
                BackColor = Color.FromArgb(13, 11, 9),
                Margin = new Padding(0, 0, 0, (int)(8 * _scale)),
                Padding = new Padding(0),
                Tag = "session-card",
                AutoSize = false,
                Height = (int)(48 * _scale),
            };

            card.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                var bounds = new Rectangle(0, 0, card.Width - 1, card.Height - 1);

                // 1. Tactile metallic casing & CRT phosphor texture
                using (var bgBrush = new SolidBrush(Color.FromArgb(14, 12, 10)))
                    g.FillRectangle(bgBrush, bounds);
                DrawTerminalMesh(g, bounds, _scale, 7);

                // 2. Status perimeter border
                Color borderCol = session.AggregateStatus switch
                {
                    UploadStatus.Done => Color.FromArgb(100, CGreen),
                    UploadStatus.Uploading => CGoldBrt,
                    UploadStatus.Queued => Color.FromArgb(110, CWarn),
                    UploadStatus.Failed => Color.FromArgb(160, CBarFail),
                    _ => Color.FromArgb(70, CBorderSub)
                };

                using (var pen = new Pen(borderCol, 1.25f))
                    g.DrawRectangle(pen, bounds);

                // 3. Left status crystal pillar & energy flare
                Color accentCol = session.AggregateStatus switch
                {
                    UploadStatus.Done => CGreen,
                    UploadStatus.Uploading => Color.FromArgb(60, 200, 240),
                    UploadStatus.Failed => CBarFail,
                    _ => CWarn
                };

                int pillarW = (int)(5 * _scale);
                using (var pillarBrush = new SolidBrush(accentCol))
                    g.FillRectangle(pillarBrush, 1, 1, pillarW, card.Height - 2);

                int flareAlpha = session.AggregateStatus == UploadStatus.Uploading ? (int)(50 + 20 * Math.Sin(_animFrame * 0.3f)) : 30;
                using (var flareBrush = new SolidBrush(Color.FromArgb(Math.Clamp(flareAlpha, 15, 80), accentCol)))
                    g.FillRectangle(flareBrush, 1 + pillarW, 1, (int)(8 * _scale), card.Height - 2);

                // 4. Dwemer brass chassis corner rivets
                int rivetOffset = (int)(6 * _scale);
                int rivetR = Math.Max(2, (int)(1.75f * _scale));
                DrawCornerRivets(g, card.Width, card.Height, rivetOffset, Color.FromArgb(90, CGoldMid));
            };

            // Files container (nested scrollable cassette deck for batch contents)
            var filesContainer = new DoubleBufferedFlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.FromArgb(10, 8, 7),
                Padding = new Padding((int)(10 * _scale), (int)(4 * _scale), (int)(10 * _scale), (int)(6 * _scale)),
                Margin = new Padding(0),
                Visible = false,
            };
            filesContainer.HandleCreated += (_, _) => FissalTheme.ApplyDarkScrollbars(filesContainer.Handle);

            filesContainer.Paint += (s, e) =>
            {
                var g = e.Graphics;
                // Subtle phosphor glow inside the open cassette deck
                DrawTerminalMesh(g, new Rectangle(0, 0, filesContainer.Width, filesContainer.Height), _scale, 4);
            };

            // Header panel (clickable top strip, always hovering at the top of the batch)
            var headerPanel = new DoubleBufferedPanel
            {
                Dock = DockStyle.Top,
                Height = (int)(48 * _scale),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand,
                Padding = new Padding((int)(14 * _scale), (int)(4 * _scale), (int)(12 * _scale), (int)(4 * _scale)),
            };

            headerPanel.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                if (IsSessionExpanded(session))
                {
                    using var divPen = new Pen(Color.FromArgb(70, CGoldMid), 1f);
                    g.DrawLine(divPen, (int)(8 * _scale), headerPanel.Height - 1, headerPanel.Width - (int)(8 * _scale), headerPanel.Height - 1);
                }

                if (session.AggregateStatus == UploadStatus.Uploading)
                {
                    int barH = Math.Max(3, (int)(3 * _scale));
                    int barPadX = (int)(14 * _scale);
                    int barY = headerPanel.Height - barH - (int)(2 * _scale);
                    int barW = headerPanel.Width - barPadX * 2;

                    if (barW > 20)
                    {
                        using var grooveBrush = new SolidBrush(Color.FromArgb(10, 8, 6));
                        g.FillRectangle(grooveBrush, barPadX, barY, barW, barH);
                        using var groovePen = new Pen(Color.FromArgb(40, CGoldMid), 1f);
                        g.DrawRectangle(groovePen, barPadX, barY, barW, barH);

                        float prog = Math.Clamp(session.AggregateProgress, 0.05f, 1f);
                        int fillW = (int)(barW * prog);

                        if (fillW > 0)
                        {
                            var fillRect = new Rectangle(barPadX, barY, fillW, barH);
                            using var plasmaBrush = new LinearGradientBrush(
                                fillRect,
                                Color.FromArgb(40, 180, 240),
                                CGoldBrt,
                                LinearGradientMode.Horizontal);
                            g.FillRectangle(plasmaBrush, fillRect);

                            int sparkW = Math.Min(fillW, (int)(6 * _scale));
                            using var sparkBrush = new SolidBrush(Color.FromArgb(255, 255, 230));
                            g.FillRectangle(sparkBrush, barPadX + fillW - sparkW, barY, sparkW, barH);
                        }
                    }
                }
            };

            var headerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 2,
                BackColor = Color.Transparent,
            };
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(28 * _scale))); // Chevron
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));                   // Title & Subtitle
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));                   // Status Badge
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));                   // Progress / Detail
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(115 * _scale))); // Action Button

            var chevronLabel = new Label
            {
                Text = "▶",
                ForeColor = CGoldBrt,
                Font = Title(10f, _scale, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
            };
            headerLayout.Controls.Add(chevronLabel, 0, 0);
            headerLayout.SetRowSpan(chevronLabel, 2);

            var titleLabel = new Label
            {
                Text = session.Title,
                ForeColor = CGoldBrt,
                Font = Mono(8.5f, _scale, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
            };
            headerLayout.Controls.Add(titleLabel, 1, 0);

            var subtitleLabel = new Label
            {
                Text = $"{session.TotalCount} files • {session.TotalSizeDisplay}",
                ForeColor = CTextSub,
                Font = Mono(7.5f, _scale),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
            };
            headerLayout.Controls.Add(subtitleLabel, 1, 1);

            var statusLabel = new Label
            {
                Font = Mono(8f, _scale, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
            };
            headerLayout.Controls.Add(statusLabel, 2, 0);
            headerLayout.SetRowSpan(statusLabel, 2);

            var detailLabel = new Label
            {
                Font = Body(8f, _scale),
                ForeColor = CTextSub,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
            };
            headerLayout.Controls.Add(detailLabel, 3, 0);
            headerLayout.SetRowSpan(detailLabel, 2);

            var actionFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                BackColor = Color.Transparent,
            };
            headerLayout.Controls.Add(actionFlow, 4, 0);
            headerLayout.SetRowSpan(actionFlow, 2);

            headerPanel.Controls.Add(headerLayout);

            void ToggleSession()
            {
                bool isExp = IsSessionExpanded(session);
                if (isExp)
                {
                    _userExpandedSessions.Remove(session.Id);
                    _userCollapsedSessions.Add(session.Id);
                }
                else
                {
                    _userCollapsedSessions.Remove(session.Id);
                    _userExpandedSessions.Add(session.Id);
                }
                RefreshSyncView();
            }

            headerPanel.Click += (_, _) => ToggleSession();
            chevronLabel.Click += (_, _) => ToggleSession();
            titleLabel.Click += (_, _) => ToggleSession();
            subtitleLabel.Click += (_, _) => ToggleSession();
            statusLabel.Click += (_, _) => ToggleSession();
            detailLabel.Click += (_, _) => ToggleSession();

            // Docking order: headerPanel docked Top at y=0, filesContainer docked Fill below it
            card.Controls.Add(filesContainer);
            card.Controls.Add(headerPanel);
            card.Controls.SetChildIndex(headerPanel, 1);
            card.Controls.SetChildIndex(filesContainer, 0);

            var controls = new SessionCardControls
            {
                Card = card,
                ChevronLabel = chevronLabel,
                TitleLabel = titleLabel,
                SubtitleLabel = subtitleLabel,
                StatusLabel = statusLabel,
                DetailLabel = detailLabel,
                ActionFlow = actionFlow,
                FilesContainer = filesContainer,
                Session = session,
            };

            UpdateSessionCard(session, controls);
            return controls;
        }

        private static int GetJobStatusPriority(UploadStatus status) => status switch
        {
            UploadStatus.Failed => 0,
            UploadStatus.Uploading => 1,
            UploadStatus.Queued or UploadStatus.Cancelled => 2,
            UploadStatus.Done => 3,
            _ => 4
        };

        private static bool IsGsFile(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;
            return fileName.StartsWith("GS", StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateSessionCard(SyncSessionModel session, SessionCardControls controls)
        {
            controls.Session = session;
            bool isExpanded = IsSessionExpanded(session);

            controls.ChevronLabel.Text = isExpanded ? "▼" : "▶";
            // D4b: detail column hidden in collapsed state to reduce info density
            if (controls.DetailLabel != null) controls.DetailLabel.Visible = isExpanded;
            controls.TitleLabel.Text = session.Title;
            controls.SubtitleLabel.Text = $"{session.TotalCount} files • {session.TotalSizeDisplay}";
            controls.SubtitleLabel.ForeColor = isExpanded ? CTextSub : Color.FromArgb(100, CTextSub);  // D4: ghost when collapsed

            int animCycle = (_animFrame / 2) % 4;
            string wave = animCycle switch { 0 => "▪▫▫▫", 1 => "▫▪▫▫", 2 => "▫▫▪▫", _ => "▫▫▫▪" };

            controls.StatusLabel.Text = session.AggregateStatus switch
            {
                UploadStatus.Uploading => $"⚡ TRANSMITTING {wave} ({session.DoneCount}/{session.TotalCount})",
                UploadStatus.Queued => $"⏳ QUEUED ({session.TotalCount} files)",
                UploadStatus.Done => $"✓ ALL SYNCHRONIZED ({session.TotalCount} files)",
                UploadStatus.Failed => $"⚠ {session.FailedCount} FAILED",
                _ => session.AggregateStatus.ToString().ToUpperInvariant()
            };
            controls.StatusLabel.ForeColor = session.AggregateStatus switch
            {
                UploadStatus.Done => CGreen,
                UploadStatus.Uploading => Color.FromArgb(60, 200, 240),
                UploadStatus.Queued => CWarn,
                UploadStatus.Failed => CBarFail,
                _ => CTextSub
            };

            // Action buttons
            bool hasRetryBtn = controls.ActionFlow.Controls.Count > 0;
            bool needsRetryBtn = session.FailedCount > 0;
            if (hasRetryBtn != needsRetryBtn)
            {
                controls.ActionFlow.SuspendLayout();
                controls.ActionFlow.Controls.Clear();
                if (needsRetryBtn)
                {
                    var btnRetryAll = MakeStyledButton("Retry Failed", CGoldBrt);
                    btnRetryAll.Click += (_, _) =>
                    {
                        foreach (var f in session.Jobs.Where(j => j.Status == UploadStatus.Failed))
                            _watcher.RetryJob(f);
                    };
                    controls.ActionFlow.Controls.Add(btnRetryAll);
                }
                controls.ActionFlow.ResumeLayout(true);
            }

            // Reconcile nested file rows if expanded
            int headerH = (int)(48 * _scale);
            if (isExpanded)
            {
                controls.FilesContainer.SuspendLayout();

                var currentJobs = session.Jobs
                    .OrderBy(j => GetJobStatusPriority(j.Status))
                    .ThenBy(j => IsGsFile(j.FileName) ? 1 : 0)
                    .ThenBy(j => j.FileName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var existingJobKeys = controls.FileRows.Keys.ToList();

                foreach (var oldJob in existingJobKeys)
                {
                    if (!currentJobs.Contains(oldJob))
                    {
                        var rowCtrl = controls.FileRows[oldJob];
                        controls.FilesContainer.Controls.Remove(rowCtrl.Row);
                        rowCtrl.Row.Dispose();
                        controls.FileRows.Remove(oldJob);
                    }
                }

                int rowH = (int)(32 * _scale);
                int rowGap = (int)(3 * _scale);
                int containerInnerPad = (int)(14 * _scale);

                for (int i = 0; i < currentJobs.Count; i++)
                {
                    var job = currentJobs[i];
                    if (controls.FileRows.TryGetValue(job, out var rowControls))
                    {
                        UpdateCompactFileRow(job, rowControls);
                        if (controls.FilesContainer.Controls.GetChildIndex(rowControls.Row) != i)
                        {
                            controls.FilesContainer.Controls.SetChildIndex(rowControls.Row, i);
                        }
                    }
                    else
                    {
                        var newRow = BuildCompactFileRow(job);
                        controls.FileRows.Add(job, newRow);
                        controls.FilesContainer.Controls.Add(newRow.Row);
                        controls.FilesContainer.Controls.SetChildIndex(newRow.Row, i);
                    }
                }

                int totalFileH = currentJobs.Count * (rowH + rowGap) + containerInnerPad;
                int maxDeckH = (int)(250 * _scale);
                int deckH = Math.Min(totalFileH, maxDeckH);
                bool needsScroll = totalFileH > maxDeckH;
                if (controls.FilesContainer.AutoScroll != needsScroll)
                    controls.FilesContainer.AutoScroll = needsScroll;

                if (!controls.FilesContainer.Visible)
                    controls.FilesContainer.Visible = true;
                controls.FilesContainer.ResumeLayout(true);

                if (controls.Card.Height != headerH + deckH)
                    controls.Card.Height = headerH + deckH;
            }
            else
            {
                if (controls.FilesContainer.Visible)
                    controls.FilesContainer.Visible = false;
                if (controls.Card.Height != headerH)
                    controls.Card.Height = headerH;
            }

            controls.Card.Invalidate();
        }

        private CompactFileRowControls BuildCompactFileRow(UploadJob job)
        {
            var row = new DoubleBufferedPanel
            {
                BackColor = Color.FromArgb(16, 14, 12),
                Margin = new Padding(0, 1, 0, (int)(3 * _scale)),
                Tag = "file-row",
            };

            row.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                var bounds = new Rectangle(0, 0, row.Width - 1, row.Height - 1);

                // 1. Dark Dwemer cassette chassis background
                Color rowBg = job.Status switch
                {
                    UploadStatus.Uploading => Color.FromArgb(22, 18, 14),
                    UploadStatus.Failed => Color.FromArgb(24, 14, 14),
                    _ => Color.FromArgb(16, 14, 12)
                };
                using (var bgBrush = new SolidBrush(rowBg))
                    g.FillRectangle(bgBrush, bounds);

                // 2. Subtle status rim border
                Color borderCol = job.Status switch
                {
                    UploadStatus.Uploading => Color.FromArgb(90, CGoldMid),
                    UploadStatus.Done => Color.FromArgb(40, CGreen),
                    UploadStatus.Failed => Color.FromArgb(100, CBarFail),
                    UploadStatus.Queued => Color.FromArgb(40, CWarn),
                    _ => Color.FromArgb(30, CBorderSub)
                };
                using (var borderPen = new Pen(borderCol, 1f))
                    g.DrawRectangle(borderPen, bounds);

                // 3. Left indicator capsule (Dwemer soul gem pillar)
                Color pillarCol = job.Status switch
                {
                    UploadStatus.Uploading => Color.FromArgb(60, 200, 240), // Cyan tonal frequency
                    UploadStatus.Done => CGreen,                             // Emerald verified
                    UploadStatus.Failed => CBarFail,                         // Crimson hazard
                    UploadStatus.Queued => CWarn,                            // Amber queued
                    _ => CTextSub
                };

                int pillarW = (int)(4 * _scale);
                using (var pillarBrush = new SolidBrush(pillarCol))
                    g.FillRectangle(pillarBrush, 1, 1, pillarW, row.Height - 2);

                // Glowing aura next to pillar
                int flareAlpha = job.Status == UploadStatus.Uploading ? (int)(50 + 25 * Math.Sin(_animFrame * 0.3f)) : 25;
                using (var flareBrush = new SolidBrush(Color.FromArgb(Math.Clamp(flareAlpha, 15, 80), pillarCol)))
                    g.FillRectangle(flareBrush, 1 + pillarW, 1, (int)(6 * _scale), row.Height - 2);

                // 4. Progress Gauge: Illuminated Dwemer Tonal Plasma Bar
                if (job.Status == UploadStatus.Uploading)
                {
                    int barH = Math.Max(3, (int)(3 * _scale));
                    int barPadX = (int)(12 * _scale);
                    int barY = row.Height - barH - (int)(2 * _scale);
                    int barW = row.Width - barPadX * 2;

                    if (barW > 20)
                    {
                        // Recessed dark groove
                        using var grooveBrush = new SolidBrush(Color.FromArgb(10, 8, 6));
                        g.FillRectangle(grooveBrush, barPadX, barY, barW, barH);
                        using var groovePen = new Pen(Color.FromArgb(40, CGoldMid), 1f);
                        g.DrawRectangle(groovePen, barPadX, barY, barW, barH);

                        // Dual gradient fluid fill (cyan -> bright amber)
                        float prog = Math.Clamp(job.Progress, 0.05f, 1f);
                        int fillW = (int)(barW * prog);

                        if (fillW > 0)
                        {
                            var fillRect = new Rectangle(barPadX, barY, fillW, barH);
                            using var plasmaBrush = new LinearGradientBrush(
                                fillRect,
                                Color.FromArgb(40, 180, 240),
                                CGoldBrt,
                                LinearGradientMode.Horizontal);
                            g.FillRectangle(plasmaBrush, fillRect);

                            // Leading energetic spark / head beacon
                            int sparkW = Math.Min(fillW, (int)(6 * _scale));
                            using var sparkBrush = new SolidBrush(Color.FromArgb(255, 255, 230));
                            g.FillRectangle(sparkBrush, barPadX + fillW - sparkW, barY, sparkW, barH);
                        }
                    }
                }
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = Color.Transparent,
                Padding = new Padding((int)(6 * _scale), 0, (int)(6 * _scale), 0),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40)); // Name
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25)); // Size & Time
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35)); // Status
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(85 * _scale))); // Action

            var nameLabel = new Label
            {
                Text = "📄 " + job.FileName,
                ForeColor = CGoldBrt,
                Font = Mono(8f, _scale, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
            };
            layout.Controls.Add(nameLabel, 0, 0);

            var metaLabel = new Label
            {
                Text = $"{job.QueuedAt:HH:mm:ss} • {job.FileSizeDisplay}",
                ForeColor = CTextSub,
                Font = Mono(7.5f, _scale),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
            };
            layout.Controls.Add(metaLabel, 1, 0);

            var statusLabel = new Label
            {
                Font = Mono(7.5f, _scale, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
            };
            layout.Controls.Add(statusLabel, 2, 0);

            var actionFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                BackColor = Color.Transparent,
            };
            layout.Controls.Add(actionFlow, 3, 0);

            row.Controls.Add(layout);
            row.Height = (int)(32 * _scale);

            var controls = new CompactFileRowControls
            {
                Row = row,
                NameLabel = nameLabel,
                MetaLabel = metaLabel,
                StatusLabel = statusLabel,
                ActionFlow = actionFlow
            };
            UpdateCompactFileRow(job, controls);
            return controls;
        }

        private void UpdateCompactFileRow(UploadJob job, CompactFileRowControls controls)
        {
            controls.NameLabel.Text = "📄 " + job.FileName;
            controls.MetaLabel.Text = $"{job.QueuedAt:HH:mm:ss} • {job.FileSizeDisplay}";

            int animCycle = (_animFrame / 2) % 4;
            string wave = animCycle switch { 0 => "▪▫▫▫", 1 => "▫▪▫▫", 2 => "▫▫▪▫", _ => "▫▫▫▪" };

            controls.StatusLabel.Text = job.Status switch
            {
                UploadStatus.Queued => "⏳ QUEUED",
                UploadStatus.Uploading => $"⚡ TRANSMITTING {wave} {(int)(job.Progress * 100)}%",
                UploadStatus.Done => "✦ SYNCHRONIZED",
                UploadStatus.Failed => string.IsNullOrWhiteSpace(job.ErrorMessage) ? "⚠ FAILED" : $"⚠ {job.ErrorMessage}",
                UploadStatus.Cancelled => "CANCELLED",
                _ => job.Status.ToString().ToUpperInvariant()
            };
            controls.StatusLabel.ForeColor = job.Status switch
            {
                UploadStatus.Done => CGreen,
                UploadStatus.Uploading => Color.FromArgb(60, 200, 240),
                UploadStatus.Queued => CWarn,
                UploadStatus.Failed => CBarFail,
                _ => CTextSub
            };

            string neededAction = job.Status == UploadStatus.Failed ? "retry"
                : (job.Status is UploadStatus.Uploading or UploadStatus.Queued ? "cancel" : "none");
            string currentAction = controls.ActionFlow.Tag as string ?? "none";
            if (neededAction != currentAction)
            {
                controls.ActionFlow.Tag = neededAction;
                controls.ActionFlow.SuspendLayout();
                controls.ActionFlow.Controls.Clear();
                if (neededAction == "retry")
                {
                    var btnRetry = MakeStyledButton("Retry", CGoldBrt);
                    btnRetry.Height = (int)(22 * _scale);
                    btnRetry.Font = Mono(7f, _scale);
                    btnRetry.Click += (_, _) => _watcher.RetryJob(job);
                    controls.ActionFlow.Controls.Add(btnRetry);
                }
                else if (neededAction == "cancel")
                {
                    var btnCancel = MakeStyledButton("Cancel", CBarFail);
                    btnCancel.Height = (int)(22 * _scale);
                    btnCancel.Font = Mono(7f, _scale);
                    btnCancel.Click += (_, _) => _watcher.CancelJob(job);
                    controls.ActionFlow.Controls.Add(btnCancel);
                }
                controls.ActionFlow.ResumeLayout(true);
            }

            controls.Row.Invalidate();
        }

        private JobCardControls BuildJobCard(UploadJob job)
        {
            var card = new DoubleBufferedPanel
            {
                BackColor = CPanelBg,
                Padding = new Padding(12, 6, 12, 6),
                Margin = new Padding(0, 0, 0, (int)(4 * _scale)),
                Tag = "sync-card",
            };

            card.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                var bounds = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                using (var bgBrush = new SolidBrush(Color.FromArgb(14, 12, 10)))
                {
                    g.FillRectangle(bgBrush, bounds);
                }
                DrawTerminalMesh(g, bounds, _scale, 7);

                Color borderCol = job.Status switch
                {
                    UploadStatus.Done => Color.FromArgb(80, CGreen),
                    UploadStatus.Uploading => CGoldBrt,
                    UploadStatus.Queued => Color.FromArgb(100, CWarn),
                    UploadStatus.UpdateReady => Color.FromArgb(180, 137, 255),
                    UploadStatus.Failed => Color.FromArgb(150, CBarFail),
                    _ => CBorderSub
                };

                using (var pen = new Pen(borderCol, 1.25f))
                {
                    g.DrawRectangle(pen, bounds);
                }

                Color accentCol = job.Status switch
                {
                    UploadStatus.Done => CGreen,
                    UploadStatus.Uploading => Color.FromArgb(60, 200, 240),
                    UploadStatus.UpdateReady => Color.FromArgb(180, 137, 255),
                    UploadStatus.Failed => CBarFail,
                    _ => CWarn
                };

                int pillarW = (int)(4 * _scale);
                using (var dotBrush = new SolidBrush(accentCol))
                {
                    g.FillRectangle(dotBrush, 1, 1, pillarW, card.Height - 2);
                }
                int flareAlpha = job.Status == UploadStatus.Uploading ? (int)(50 + 25 * Math.Sin(_animFrame * 0.3f)) : 30;
                using (var flareBrush = new SolidBrush(Color.FromArgb(Math.Clamp(flareAlpha, 15, 80), accentCol)))
                {
                    g.FillRectangle(flareBrush, 1 + pillarW, 1, (int)(6 * _scale), card.Height - 2);
                }

                if (job.Status == UploadStatus.Uploading)
                {
                    int barH = Math.Max(3, (int)(3 * _scale));
                    int barPadX = (int)(14 * _scale);
                    int barY = card.Height - barH - (int)(3 * _scale);
                    int barW = card.Width - barPadX * 2;

                    if (barW > 20)
                    {
                        using var grooveBrush = new SolidBrush(Color.FromArgb(10, 8, 6));
                        g.FillRectangle(grooveBrush, barPadX, barY, barW, barH);
                        using var groovePen = new Pen(Color.FromArgb(40, CGoldMid), 1f);
                        g.DrawRectangle(groovePen, barPadX, barY, barW, barH);

                        float prog = Math.Clamp(job.Progress, 0.05f, 1f);
                        int fillW = (int)(barW * prog);

                        if (fillW > 0)
                        {
                            var fillRect = new Rectangle(barPadX, barY, fillW, barH);
                            using var plasmaBrush = new LinearGradientBrush(
                                fillRect,
                                Color.FromArgb(40, 180, 240),
                                CGoldBrt,
                                LinearGradientMode.Horizontal);
                            g.FillRectangle(plasmaBrush, fillRect);

                            int sparkW = Math.Min(fillW, (int)(6 * _scale));
                            using var sparkBrush = new SolidBrush(Color.FromArgb(255, 255, 230));
                            g.FillRectangle(sparkBrush, barPadX + fillW - sparkW, barY, sparkW, barH);
                        }
                    }
                }

                int rivetOffset = (int)(5 * _scale);
                DrawCornerRivets(g, card.Width, card.Height, rivetOffset, Color.FromArgb(80, CGoldMid));
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 2,
                BackColor = CPanelBg,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35)); // File name & time
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25)); // Status
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40)); // Details
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(110 * _scale))); // Fixed action button width

            // File Name
            var nameLabel = new Label
            {
                Text = (job.IsUpdate ? "📦 " : "📄 ") + job.FileName,
                ForeColor = job.IsUpdate ? Color.FromArgb(196, 137, 255) : CGoldBrt,
                Font = Mono(8.5f, _scale, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = CPanelBg,
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
                BackColor = CPanelBg,
            };
            layout.Controls.Add(timeLabel, 0, 1);

            // Status Badge
            var statusLabel = new Label
            {
                Font = Mono(8f, _scale, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = CPanelBg,
            };
            layout.Controls.Add(statusLabel, 1, 0);

            // Status progress or error detail
            var detailLabel = new Label
            {
                Font = Body(8f, _scale),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = CPanelBg,
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
                BackColor = CPanelBg,
            };

            layout.Controls.Add(actionFlow, 3, 0);
            layout.SetRowSpan(actionFlow, 2);

            card.Controls.Add(layout);
            card.Height = (int)(52 * _scale);
            var controls = new JobCardControls { Card = card, StatusLabel = statusLabel, DetailLabel = detailLabel, ActionFlow = actionFlow };
            UpdateJobCard(job, controls);
            return controls;
        }

        private void UpdateJobCard(UploadJob job, JobCardControls controls)
        {
            controls.StatusLabel.Text = job.Status switch
            {
                UploadStatus.Queued => "⏳ QUEUED",
                UploadStatus.Uploading => $"⚡ UPLOADING {(int)(job.Progress * 100)}%",
                UploadStatus.Done => "✓ SYNCHRONIZED",
                UploadStatus.UpdateReady => "📦 UPDATE READY",
                UploadStatus.Failed => "⚠ FAILED",
                UploadStatus.Cancelled => "CANCELLED",
                _ => job.Status.ToString().ToUpperInvariant()
            };
            controls.StatusLabel.ForeColor = job.Status switch
            {
                UploadStatus.Done => CGreen,
                UploadStatus.Uploading => CGoldBrt,
                UploadStatus.Queued => CWarn,
                UploadStatus.UpdateReady => Color.FromArgb(196, 137, 255),
                UploadStatus.Failed => CBarFail,
                _ => CTextSub
            };
            controls.DetailLabel.Text = string.IsNullOrWhiteSpace(job.ErrorMessage)
                ? (job.Status == UploadStatus.Done ? "Verified • Synchronized to Redfur Relay" : "")
                : job.ErrorMessage;
            controls.DetailLabel.ForeColor = string.IsNullOrWhiteSpace(job.ErrorMessage) ? CTextSub : CBarFail;

            // Reconcile action buttons dynamically
            string neededJobAction = job.Status == UploadStatus.UpdateReady ? "upgrade"
                : (job.Status == UploadStatus.Failed ? "retry"
                : (job.Status is UploadStatus.Uploading or UploadStatus.Queued ? "cancel" : "none"));
            string currentJobAction = controls.ActionFlow.Tag as string ?? "none";
            if (neededJobAction != currentJobAction)
            {
                controls.ActionFlow.Tag = neededJobAction;
                controls.ActionFlow.SuspendLayout();
                controls.ActionFlow.Controls.Clear();
                if (neededJobAction == "upgrade")
                {
                    var btnApply = MakeStyledButton("Apply Upgrade", Color.FromArgb(196, 137, 255));
                    btnApply.Click += (_, _) => _applyUpdateAction(job);
                    controls.ActionFlow.Controls.Add(btnApply);
                }
                else if (neededJobAction == "retry")
                {
                    var btnRetry = MakeStyledButton("Retry", CGoldBrt);
                    btnRetry.Click += (_, _) => _watcher.RetryJob(job);
                    controls.ActionFlow.Controls.Add(btnRetry);
                }
                else if (neededJobAction == "cancel")
                {
                    var btnCancel = MakeStyledButton("Cancel", CBarFail);
                    btnCancel.Click += (_, _) => _watcher.CancelJob(job);
                    controls.ActionFlow.Controls.Add(btnCancel);
                }
                controls.ActionFlow.ResumeLayout(true);
            }

            controls.Card.Invalidate();
        }

        private void ResizeSyncJobCards()
        {
            if (_isResizingSyncCards) return;
            _isResizingSyncCards = true;
            try
            {
                int targetWidth = Math.Max(200, _syncJobsList.ClientSize.Width - (int)(20 * _scale));
                foreach (Control ctrl in _syncJobsList.Controls)
                {
                    if (Equals(ctrl.Tag, "session-card") || Equals(ctrl.Tag, "sync-card") || Equals(ctrl.Tag, "empty-card"))
                    {
                        if (ctrl.Width != targetWidth)
                            ctrl.Width = targetWidth;
                    }
                }

                foreach (var sc in _sessionCards.Values)
                {
                    int rowWidth = Math.Max(180, sc.FilesContainer.ClientSize.Width - (int)(16 * _scale));
                    foreach (var rowCtrl in sc.FileRows.Values)
                    {
                        if (rowCtrl.Row.Width != rowWidth)
                            rowCtrl.Row.Width = rowWidth;
                    }

                    // Refresh explicit card height
                    int headerH = (int)(48 * _scale);
                    if (IsSessionExpanded(sc.Session))
                    {
                        int rowH = (int)(32 * _scale);
                        int rowGap = (int)(3 * _scale);
                        int containerInnerPad = (int)(14 * _scale);
                        int totalFileH = sc.Session.Jobs.Count * (rowH + rowGap) + containerInnerPad;
                        int maxDeckH = (int)(250 * _scale);
                        int deckH = Math.Min(totalFileH, maxDeckH);
                        bool needsScroll = totalFileH > maxDeckH;
                        if (sc.FilesContainer.AutoScroll != needsScroll)
                            sc.FilesContainer.AutoScroll = needsScroll;

                        if (sc.Card.Height != headerH + deckH)
                            sc.Card.Height = headerH + deckH;
                    }
                    else
                    {
                        if (sc.Card.Height != headerH)
                            sc.Card.Height = headerH;
                    }
                }
            }
            finally
            {
                _isResizingSyncCards = false;
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // 2. WINDOWING & COMPACT MODE ENGINE
        // ═════════════════════════════════════════════════════════════════════
        public void ToggleCompactMode()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(ToggleCompactMode));
                return;
            }

            _isCompactMode = !_isCompactMode;
            AppConfig.Instance.CompactMode = _isCompactMode;
            AppConfig.Instance.Save();

            SuspendLayout();
            try
            {
                if (_isCompactMode)
                {
                    _normalSize = Size;
                    MinimumSize = new Size((int)(400 * _scale), (int)(260 * _scale));
                    _navRail.Visible = false;
                    _rootLayout.ColumnStyles[0].Width = 0;
                    SwitchTab("sync");

                    int compactW = (int)(480 * _scale);
                    int compactH = (int)(340 * _scale);
                    var scr = Screen.FromControl(this) ?? Screen.PrimaryScreen;
                    if (scr != null)
                    {
                        var wa = scr.WorkingArea;
                        if (compactW > wa.Width) compactW = wa.Width;
                        if (compactH > wa.Height) compactH = wa.Height;
                    }
                    Size = new Size(compactW, compactH);
                }
                else
                {
                    MinimumSize = new Size((int)(920 * _scale), (int)(580 * _scale));
                    _navRail.Visible = true;
                    _rootLayout.ColumnStyles[0].Width = (int)(220 * _scale);
                    Size = _normalSize.Width > 0 ? _normalSize : new Size((int)(1060 * _scale), (int)(690 * _scale));
                }
            }
            finally
            {
                ResumeLayout(true);
            }

            _compactToggleBtn?.Invalidate();
            Invalidate(true);
        }

        // ═════════════════════════════════════════════════════════════════════
        // RECURSIVE THEME VISITOR ENGINE
        // ═════════════════════════════════════════════════════════════════════
        private void ApplyThemeToHierarchy(Control root)
        {
            foreach (Control c in root.Controls)
            {
                // Preserve semantic controls (e.g. green status indicators, warning badges)
                if (c.Tag is string tag && tag.StartsWith("semantic-", StringComparison.OrdinalIgnoreCase))
                {
                    if (c.HasChildren) ApplyThemeToHierarchy(c);
                    continue;
                }

                if (c is Panel or TableLayoutPanel or FlowLayoutPanel)
                {
                    if (c.Tag as string == "card")
                    {
                        c.BackColor = CPanelBg;
                        c.ForeColor = CText;
                    }
                    else if (c.Tag as string == "card-alt")
                    {
                        c.BackColor = CPanelBgAlt;
                        c.ForeColor = CText;
                    }
                    else if (c.Tag as string == "view-root" || c.Parent == _contentHost)
                    {
                        c.BackColor = CBg;
                        c.ForeColor = CText;
                    }
                }
                else if (c is Button btn)
                {
                    btn.BackColor = CBtnBg;
                    Color accent = CGoldBrt;
                    if (btn.Tag is string btag)
                    {
                        if (btag == "btn-green") accent = CGreen;
                        else if (btag == "btn-warn") accent = CWarn;
                        else if (btag == "btn-fail") accent = CBarFail;
                        else if (btag == "btn-sub") accent = CTextSub;
                        else if (btag == "btn-text") accent = CText;
                        else accent = CGoldBrt;
                    }
                    else
                    {
                        if (btn.Text.StartsWith("Install", StringComparison.OrdinalIgnoreCase) ||
                            btn.Text.StartsWith("Restart", StringComparison.OrdinalIgnoreCase) ||
                            btn.Text.StartsWith("Apply", StringComparison.OrdinalIgnoreCase) ||
                            btn.Text.StartsWith("✓", StringComparison.OrdinalIgnoreCase))
                        {
                            accent = CGreen;
                        }
                        else if (btn.Text.Contains("Clear", StringComparison.OrdinalIgnoreCase) ||
                                 btn.Text.Contains("Open", StringComparison.OrdinalIgnoreCase) ||
                                 btn.Text.Contains("Visit", StringComparison.OrdinalIgnoreCase))
                        {
                            accent = CTextSub;
                        }
                        else
                        {
                            accent = CGoldBrt;
                        }
                    }
                    btn.ForeColor = accent;
                    btn.FlatAppearance.BorderColor = accent;
                    btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(45, accent.R, accent.G, accent.B);
                }
                else if (c is Label lbl)
                {
                    if (lbl.Tag is string ltag)
                    {
                        if (ltag == "header") lbl.ForeColor = CGoldBrt;
                        else if (ltag == "field") lbl.ForeColor = CGoldBrt;
                        else if (ltag == "sub") lbl.ForeColor = CTextSub;
                        else if (ltag == "value") lbl.ForeColor = CText;
                        else lbl.ForeColor = CText;
                    }
                    else
                    {
                        if (lbl.Font.Bold || lbl.Font.Size >= 10f) lbl.ForeColor = CGoldBrt;
                        else if (lbl.Font.Italic || lbl.Font.Size <= 8f) lbl.ForeColor = CTextSub;
                        else lbl.ForeColor = CText;
                    }
                }
                else if (c is TextBox tb)
                {
                    tb.BackColor = CBg;
                    tb.ForeColor = CText;
                }
                else if (c is RichTextBox rtb)
                {
                    rtb.BackColor = CPanelBgAlt;
                    rtb.ForeColor = CText;
                }
                else if (c is ComboBox cmb)
                {
                    cmb.BackColor = CPanelBgAlt;
                    cmb.ForeColor = CText;
                }
                else if (c is CheckBox cb)
                {
                    cb.ForeColor = CText;
                }
                else if (c is TrackBar trk)
                {
                    trk.BackColor = CPanelBg;
                }

                if (c.HasChildren) ApplyThemeToHierarchy(c);
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // 2b. ESO ADDON INSTALL & UPDATE MANAGEMENT VIEW
        // ═════════════════════════════════════════════════════════════════════
        private void InitAddonView()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                AutoScroll = true,
                Padding = new Padding((int)(16 * _scale)),
                BackColor = Color.Transparent,
            };
            layout.HorizontalScroll.Enabled = false;
            layout.HorizontalScroll.Visible = false;
            layout.HandleCreated += (_, _) => FissalTheme.ApplyDarkScrollbars(layout.Handle);
            layout.Resize += (_, _) =>
            {
                layout.HorizontalScroll.Maximum = 0;
                layout.HorizontalScroll.Visible = false;
            };
            _addonLayout = layout;

            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // Card 1: Addon Status & Install/Update Actions
            var formPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                RowCount = 7,
                BackColor = CPanelBg,
                Padding = new Padding((int)(16 * _scale)),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, (int)(12 * _scale)),
            };
            formPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(160 * _scale)));
            formPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            formPanel.Tag = "card";

            var sectionLabel = new Label
            {
                Text = "FISSAL'S COGWORK RELAY — ESO ADDON INSTALL & SYNC",
                UseMnemonic = false,
                ForeColor = CGoldBrt,
                Font = Title(11f, _scale, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, (int)(14 * _scale)),
                Tag = "header",
            };
            formPanel.Controls.Add(sectionLabel, 0, 0);
            formPanel.SetColumnSpan(sectionLabel, 2);

            // Addon Status Badge & Detail
            formPanel.Controls.Add(MakeFieldLabel("Addon State:"), 0, 1);
            var statusFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                Margin = new Padding(0),
            };
            _lblAddonStatusBadge = new Label
            {
                Text = "Checking addon status...",
                UseMnemonic = false,
                ForeColor = CGoldBrt,
                Font = Mono(9.5f, _scale, FontStyle.Bold),
                AutoSize = true,
                Tag = "semantic-badge",
            };
            _lblAddonStatusDetail = new Label
            {
                Text = "Scanning Elder Scrolls Online directories...",
                UseMnemonic = false,
                ForeColor = CTextSub,
                Font = Mono(8f, _scale),
                AutoSize = true,
                Margin = new Padding(0, (int)(3 * _scale), 0, 0),
                Tag = "sub",
            };
            statusFlow.Controls.Add(_lblAddonStatusBadge);
            statusFlow.Controls.Add(_lblAddonStatusDetail);
            formPanel.Controls.Add(statusFlow, 1, 1);

            // Installed Version
            formPanel.Controls.Add(MakeFieldLabel("Installed Version:"), 0, 2);
            _lblAddonInstalledVer = new Label
            {
                Text = "Detecting...",
                ForeColor = CText,
                Font = Mono(9f, _scale, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Tag = "value",
            };
            formPanel.Controls.Add(_lblAddonInstalledVer, 1, 2);

            // Latest Version
            formPanel.Controls.Add(MakeFieldLabel("Latest Available:"), 0, 3);
            _lblAddonLatestVer = new Label
            {
                Text = $"v{AddonInstallerService.ActiveLatestAddonVersion} (Latest Available)",
                ForeColor = CGoldBrt,
                Font = Mono(9f, _scale, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Tag = "header",
            };
            formPanel.Controls.Add(_lblAddonLatestVer, 1, 3);

            // ESO Live Path
            formPanel.Controls.Add(MakeFieldLabel("ESO Live Directory:"), 0, 4);
            var pathLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                AutoSize = true,
                Margin = new Padding(0),
            };
            pathLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pathLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pathLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            string initialLivePath = AppConfig.Instance.CustomEsoLiveDirectory ?? AddonInstallerService.FindEsoLiveDirectory() ?? "";
            _txtAddonEsoPath = MakeStyledTextBox(initialLivePath);
            _txtAddonEsoPath.Leave += (_, _) =>
            {
                var text = _txtAddonEsoPath.Text.Trim();
                if (!string.IsNullOrWhiteSpace(text) && Directory.Exists(text))
                {
                    var cfg = AppConfig.Instance;
                    if (!string.Equals(cfg.CustomEsoLiveDirectory, text, StringComparison.OrdinalIgnoreCase))
                    {
                        cfg.CustomEsoLiveDirectory = text;
                        cfg.Save();
                        RefreshAddonView();
                        RefreshSetupView();
                    }
                }
            };
            pathLayout.Controls.Add(_txtAddonEsoPath, 0, 0);

            _btnBrowseEsoPath = MakeStyledButton("Browse...", CText);
            _btnBrowseEsoPath.Click += (_, _) =>
            {
                using var fbd = new FolderBrowserDialog
                {
                    Description = "Select your Elder Scrolls Online 'live' folder",
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = false,
                };
                if (!string.IsNullOrWhiteSpace(_txtAddonEsoPath.Text) && Directory.Exists(_txtAddonEsoPath.Text))
                {
                    fbd.SelectedPath = _txtAddonEsoPath.Text;
                }
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    _txtAddonEsoPath.Text = fbd.SelectedPath;
                    var cfg = AppConfig.Instance;
                    cfg.CustomEsoLiveDirectory = fbd.SelectedPath;
                    cfg.Save();
                    RefreshAddonView();
                    RefreshSetupView();
                }
            };
            pathLayout.Controls.Add(_btnBrowseEsoPath, 1, 0);

            _btnOpenAddonFolder = MakeStyledButton("Open AddOns", CText);
            _btnOpenAddonFolder.Click += (_, _) =>
            {
                string live = _txtAddonEsoPath.Text.Trim();
                if (string.IsNullOrWhiteSpace(live)) live = AddonInstallerService.FindEsoLiveDirectory() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(live) && Directory.Exists(live))
                {
                    string target = Path.Combine(live, "AddOns", AddonInstallerService.AddonDirectoryName);
                    if (!Directory.Exists(target)) target = Path.Combine(live, "AddOns");
                    AddonInstallerService.OpenFolderInExplorer(target);
                }
                else
                {
                    FissalBox.Show("Elder Scrolls Online live folder could not be found.", "Folder Error");
                }
            };
            pathLayout.Controls.Add(_btnOpenAddonFolder, 2, 0);
            formPanel.Controls.Add(pathLayout, 1, 4);

            // Action Buttons Row
            formPanel.Controls.Add(MakeFieldLabel("Addon Actions:"), 0, 5);
            var actionFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                Margin = new Padding(0, (int)(6 * _scale), 0, 0),
            };

            _btnInstallOrUpdateAddon = MakeStyledButton("Install / Update Addon", CGreen);
            _btnInstallOrUpdateAddon.Margin = new Padding(0, 0, (int)(8 * _scale), (int)(6 * _scale));
            _btnInstallOrUpdateAddon.Click += (_, _) =>
            {
                string? path = _txtAddonEsoPath.Text.Trim();
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                {
                    path = AddonInstallerService.FindEsoLiveDirectory();
                }

                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                {
                    FissalBox.Show("Please select or enter a valid Elder Scrolls Online 'live' directory first.", "ESO Directory Required");
                    return;
                }

                var cfg = AppConfig.Instance;
                if (!string.Equals(cfg.CustomEsoLiveDirectory, path, StringComparison.OrdinalIgnoreCase))
                {
                    cfg.CustomEsoLiveDirectory = path;
                    cfg.Save();
                }

                bool ok = AddonInstallerService.InstallOrUpdateAddon(path, out string msg);
                RefreshAddonView();
                RefreshSetupView();

                string fullMsg = ok
                    ? $"{msg}\n\nNotice: If Elder Scrolls Online is currently running, remember to type /reloadui in-game chat to load the new scripts into memory!"
                    : msg;
                FissalBox.Show(fullMsg, ok ? "Addon Installation Successful" : "Installation Notice");
                return;
            };
            actionFlow.Controls.Add(_btnInstallOrUpdateAddon);

            _btnRefreshAddon = MakeStyledButton("Check Status", CGoldBrt);
            _btnRefreshAddon.Margin = new Padding(0, 0, (int)(8 * _scale), (int)(6 * _scale));
            _btnRefreshAddon.Click += async (_, _) =>
            {
                _btnRefreshAddon.Enabled = false;
                _btnRefreshAddon.Text = "Checking...";
                await AddonInstallerService.CheckRemoteAddonVersionAsync(AppConfig.Instance.ServerUrl);
                RefreshAddonView();
                _btnRefreshAddon.Enabled = true;
                _btnRefreshAddon.Text = "Check Status";
                FissalBox.Show("Addon installation status refreshed from server & disk.", "Status Refreshed");
            };
            actionFlow.Controls.Add(_btnRefreshAddon);

            _btnOpenSavedVars = MakeStyledButton("Open SavedVariables", CTextSub);
            _btnOpenSavedVars.Margin = new Padding(0, 0, (int)(8 * _scale), (int)(6 * _scale));
            _btnOpenSavedVars.Click += (_, _) =>
            {
                string live = _txtAddonEsoPath.Text.Trim();
                if (string.IsNullOrWhiteSpace(live)) live = AddonInstallerService.FindEsoLiveDirectory() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(live) && Directory.Exists(live))
                {
                    string target = Path.Combine(live, "SavedVariables");
                    if (!Directory.Exists(target)) Directory.CreateDirectory(target);
                    AddonInstallerService.OpenFolderInExplorer(target);
                }
                else
                {
                    FissalBox.Show("Elder Scrolls Online live folder not found.", "Folder Error");
                }
            };
            actionFlow.Controls.Add(_btnOpenSavedVars);

            formPanel.Controls.Add(actionFlow, 1, 5);

            // In-Game Notice Callout
            formPanel.Controls.Add(MakeFieldLabel("In-Game Notice:"), 0, 6);
            var noticeLabel = new Label
            {
                Text = "⚡ Note: If ESO is running when installing or updating, type /reloadui in-game to load new scripts.",
                ForeColor = CGoldBrt,
                Font = Mono(8f, _scale, FontStyle.Italic),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Tag = "sub",
            };
            formPanel.Controls.Add(noticeLabel, 1, 6);

            layout.Controls.Add(formPanel, 0, 0);

            // Card 2: Required Dependencies & Data Utilities (LibHistoire, LibAddonMenu, TamrielTradeCentre)
            var depsPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 3,
                RowCount = 4,
                BackColor = CPanelBg,
                Padding = new Padding((int)(16 * _scale)),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, (int)(12 * _scale)),
                Tag = "card"
            };
            depsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(160 * _scale)));
            depsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            depsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(290 * _scale)));

            var depsHeader = new Label
            {
                Text = "ESO ADDON DEPENDENCIES & DATA UTILITIES",
                UseMnemonic = false,
                ForeColor = CGoldBrt,
                Font = Title(11f, _scale, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, (int)(14 * _scale)),
                Tag = "header"
            };
            depsPanel.Controls.Add(depsHeader, 0, 0);
            depsPanel.SetColumnSpan(depsHeader, 3);

            // 1. LibHistoire
            depsPanel.Controls.Add(MakeFieldLabel("LibHistoire:"), 0, 1);
            _lblLibHistoireStatus = new Label
            {
                Text = "Checking...",
                ForeColor = CTextSub,
                Font = Mono(8.5f, _scale, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Tag = "semantic-status",
            };
            depsPanel.Controls.Add(_lblLibHistoireStatus, 1, 1);

            _btnInstallLibHistoire = MakeStyledButton("⚡ Install LibHistoire", CGoldBrt);
            _btnInstallLibHistoire.AutoSize = true;
            _btnInstallLibHistoire.Click += async (_, _) =>
            {
                string live = _txtAddonEsoPath.Text.Trim();
                if (string.IsNullOrWhiteSpace(live)) live = AddonInstallerService.FindEsoLiveDirectory() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(live) || !Directory.Exists(live))
                {
                    FissalBox.Show("Elder Scrolls Online live folder not found.", "Folder Error");
                    return;
                }
                _btnInstallLibHistoire.Enabled = false;
                _btnInstallLibHistoire.Text = "Installing...";
                bool ok = await AddonInstallerService.DownloadAndInstallDependencyAsync(live, "LibHistoire", AddonInstallerService.LibHistoireDownloadUrl);
                _btnInstallLibHistoire.Enabled = true;
                _btnInstallLibHistoire.Text = "⚡ Install LibHistoire";
                RefreshAddonView();
                FissalBox.Show(ok ? "LibHistoire installed successfully! If ESO is running, type /reloadui in-game." : "Failed to install LibHistoire from repository mirror.", "LibHistoire");
            };
            depsPanel.Controls.Add(_btnInstallLibHistoire, 2, 1);

            // 2. LibAddonMenu-2.0
            depsPanel.Controls.Add(MakeFieldLabel("LibAddonMenu-2.0:"), 0, 2);
            _lblLibAddonMenuStatus = new Label
            {
                Text = "Checking...",
                ForeColor = CTextSub,
                Font = Mono(8.5f, _scale, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Tag = "semantic-status",
            };
            depsPanel.Controls.Add(_lblLibAddonMenuStatus, 1, 2);

            _btnInstallLibAddonMenu = MakeStyledButton("⚡ Install LibAddonMenu", CGoldBrt);
            _btnInstallLibAddonMenu.AutoSize = true;
            _btnInstallLibAddonMenu.Click += async (_, _) =>
            {
                string live = _txtAddonEsoPath.Text.Trim();
                if (string.IsNullOrWhiteSpace(live)) live = AddonInstallerService.FindEsoLiveDirectory() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(live) || !Directory.Exists(live))
                {
                    FissalBox.Show("Elder Scrolls Online live folder not found.", "Folder Error");
                    return;
                }
                _btnInstallLibAddonMenu.Enabled = false;
                _btnInstallLibAddonMenu.Text = "Installing...";
                bool ok = await AddonInstallerService.DownloadAndInstallDependencyAsync(live, "LibAddonMenu-2.0", AddonInstallerService.LibAddonMenuDownloadUrl);
                _btnInstallLibAddonMenu.Enabled = true;
                _btnInstallLibAddonMenu.Text = "⚡ Install LibAddonMenu";
                RefreshAddonView();
                FissalBox.Show(ok ? "LibAddonMenu-2.0 installed successfully! If ESO is running, type /reloadui in-game." : "Failed to install LibAddonMenu-2.0 from repository mirror.", "LibAddonMenu-2.0");
            };
            depsPanel.Controls.Add(_btnInstallLibAddonMenu, 2, 2);

            // 3. TTC Price Table
            depsPanel.Controls.Add(MakeFieldLabel("TamrielTradeCentre:"), 0, 3);
            _lblTtcStatus = new Label
            {
                Text = "ℹ Optional — Required for in-game store bumping & price lookups",
                ForeColor = CTextSub,
                Font = Mono(8f, _scale),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Tag = "sub"
            };
            depsPanel.Controls.Add(_lblTtcStatus, 1, 3);

            var ttcFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0)
            };
            _btnUpdateTtcPriceTable = MakeStyledButton("⚡ Update Price Table", CGoldBrt);
            _btnUpdateTtcPriceTable.AutoSize = true;
            _btnUpdateTtcPriceTable.Margin = new Padding(0, 0, (int)(6 * _scale), 0);
            _btnUpdateTtcPriceTable.Click += async (_, _) =>
            {
                string live = _txtAddonEsoPath.Text.Trim();
                if (string.IsNullOrWhiteSpace(live)) live = AddonInstallerService.FindEsoLiveDirectory() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(live) || !Directory.Exists(live))
                {
                    FissalBox.Show("Elder Scrolls Online live folder not found.", "Folder Error");
                    return;
                }
                _btnUpdateTtcPriceTable.Enabled = false;
                _btnUpdateTtcPriceTable.Text = "Downloading...";
                bool ok = await AddonInstallerService.DownloadAndInstallTtcPriceTableAsync(live);
                _btnUpdateTtcPriceTable.Enabled = true;
                _btnUpdateTtcPriceTable.Text = "⚡ Update Price Table";
                FissalBox.Show(ok ? "TTC PriceTable downloaded and updated successfully! If ESO is running, type /reloadui in-game." : "Failed to download TTC PriceTable from server.", "TTC PriceTable");
            };
            ttcFlow.Controls.Add(_btnUpdateTtcPriceTable);

            var btnVisitTtc = MakeStyledButton("Visit TTC Web", CTextSub);
            btnVisitTtc.AutoSize = true;
            btnVisitTtc.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo("https://tamrieltradecentre.com") { UseShellExecute = true }); } catch { }
            };
            ttcFlow.Controls.Add(btnVisitTtc);

            depsPanel.Controls.Add(ttcFlow, 2, 3);

            layout.Controls.Add(depsPanel, 0, 1);

            // Card 3: In-Game Guide / Overview
            var guidePanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = CPanelBg,
                Padding = new Padding((int)(16 * _scale)),
                AutoSize = true,
                Tag = "card",
            };
            var guideHeader = new Label
            {
                Text = "IN-GAME COMMANDS & COURIER PROTOCOL",
                UseMnemonic = false,
                ForeColor = CGoldBrt,
                Font = Title(11f, _scale, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, (int)(12 * _scale)),
                Tag = "header",
            };
            guidePanel.Tag = "card";
            guidePanel.Controls.Add(guideHeader, 0, 0);

            var guideText = new Label
            {
                Text = "• /fissal — Displays recorded sales count and courier telemetry in chat.\n" +
                       "• /reloadui — Flushes in-game history and SavedVariables to disk so Fissal Relay can sync immediately.\n" +
                       "• In-Person Kiosk Ground Recon: Automatically captures trader ownership, locations, and guild bids.\n" +
                       "• All-Guild Staff Management: Automatically snapshots roster states, bank deposits, and inactivity audits.",
                ForeColor = CTextSub,
                Font = Mono(8f, _scale),
                AutoSize = true,
                Tag = "sub",
            };
            guidePanel.Controls.Add(guideText, 0, 1);

            layout.Controls.Add(guidePanel, 0, 2);

            _addonView.Controls.Add(layout);
        }

        private void RefreshAddonView()
        {
            string? customPath = _txtAddonEsoPath?.Text.Trim();
            if (string.IsNullOrWhiteSpace(customPath)) customPath = null;

            var status = AddonInstallerService.CheckAddonInstallStatus(customPath);

            if (_txtAddonEsoPath != null && string.IsNullOrWhiteSpace(_txtAddonEsoPath.Text) && !string.IsNullOrWhiteSpace(status.EsoLiveDirectory))
            {
                _txtAddonEsoPath.Text = status.EsoLiveDirectory;
            }

            _lblAddonInstalledVer.Text = status.InstalledVersion != null ? $"v{status.InstalledVersion}" : "Not Installed";
            _lblAddonInstalledVer.ForeColor = status.InstalledVersion != null ? CText : CBarFail;

            _lblAddonLatestVer.Text = $"v{status.LatestVersion} (Latest Available)";
            _lblAddonLatestVer.ForeColor = CGoldBrt;

            switch (status.State)
            {
                case AddonInstallState.UpToDate:
                    _lblAddonStatusBadge.Text = "● ADDON IS INSTALLED & UP TO DATE";
                    _lblAddonStatusBadge.ForeColor = CGreen;
                    _lblAddonStatusDetail.Text = $"Version {status.InstalledVersion} is active in ESO live directory.";
                    _lblAddonStatusDetail.ForeColor = CTextSub;
                    _btnInstallOrUpdateAddon.Text = "Repair / Reinstall Addon (v" + status.LatestVersion + ")";
                    _btnInstallOrUpdateAddon.ForeColor = CGoldBrt;
                    _btnInstallOrUpdateAddon.FlatAppearance.BorderColor = CGoldBrt;
                    _btnInstallOrUpdateAddon.BackColor = CBtnBg;
                    _btnInstallOrUpdateAddon.Tag = "btn-gold";
                    break;

                case AddonInstallState.UpdateAvailable:
                    _lblAddonStatusBadge.Text = "▲ ADDON UPDATE REQUIRED";
                    _lblAddonStatusBadge.ForeColor = CWarn;
                    _lblAddonStatusDetail.Text = status.StatusMessage;
                    _lblAddonStatusDetail.ForeColor = CTextSub;
                    _btnInstallOrUpdateAddon.Text = "Update Addon to v" + status.LatestVersion + " Now";
                    _btnInstallOrUpdateAddon.ForeColor = CGreen;
                    _btnInstallOrUpdateAddon.FlatAppearance.BorderColor = CGreen;
                    _btnInstallOrUpdateAddon.BackColor = CBtnBg;
                    _btnInstallOrUpdateAddon.Tag = "btn-green";
                    break;

                case AddonInstallState.NotInstalled:
                    _lblAddonStatusBadge.Text = "✖ ADDON NOT INSTALLED";
                    _lblAddonStatusBadge.ForeColor = CBarFail;
                    _lblAddonStatusDetail.Text = "FissalRelay folder is missing from ESO live AddOns directory.";
                    _lblAddonStatusDetail.ForeColor = CTextSub;
                    _btnInstallOrUpdateAddon.Text = "Install Fissal Relay Addon (v" + status.LatestVersion + ")";
                    _btnInstallOrUpdateAddon.ForeColor = CGreen;
                    _btnInstallOrUpdateAddon.FlatAppearance.BorderColor = CGreen;
                    _btnInstallOrUpdateAddon.BackColor = CBtnBg;
                    _btnInstallOrUpdateAddon.Tag = "btn-green";
                    break;

                case AddonInstallState.EsoNotFound:
                default:
                    _lblAddonStatusBadge.Text = "? ESO LIVE DIRECTORY NOT FOUND";
                    _lblAddonStatusBadge.ForeColor = CWarn;
                    _lblAddonStatusDetail.Text = "Could not locate Elder Scrolls Online live folder. Use 'Browse...' to select it.";
                    _lblAddonStatusDetail.ForeColor = CTextSub;
                    _btnInstallOrUpdateAddon.Text = "Install Addon";
                    _btnInstallOrUpdateAddon.ForeColor = CTextSub;
                    _btnInstallOrUpdateAddon.FlatAppearance.BorderColor = CTextSub;
                    _btnInstallOrUpdateAddon.BackColor = CBtnBg;
                    _btnInstallOrUpdateAddon.Tag = "btn-sub";
                    break;
            }

            // Dependencies
            if (status.EsoLiveFound)
            {
                if (status.LibHistoireInstalled)
                {
                    _lblLibHistoireStatus.Text = "● LibHistoire: Installed";
                    _lblLibHistoireStatus.ForeColor = CGreen;
                    _btnInstallLibHistoire.Text = "✓ Reinstall / Update";
                    _btnInstallLibHistoire.ForeColor = CTextSub;
                    _btnInstallLibHistoire.FlatAppearance.BorderColor = CTextSub;
                    _btnInstallLibHistoire.BackColor = CBtnBg;
                    _btnInstallLibHistoire.Tag = "btn-sub";
                }
                else
                {
                    _lblLibHistoireStatus.Text = "▲ LibHistoire: Missing (Required for Sales Sync)";
                    _lblLibHistoireStatus.ForeColor = CBarFail;
                    _btnInstallLibHistoire.Text = "⚡ Install LibHistoire";
                    _btnInstallLibHistoire.ForeColor = CGoldBrt;
                    _btnInstallLibHistoire.FlatAppearance.BorderColor = CGoldBrt;
                    _btnInstallLibHistoire.BackColor = CBtnBg;
                    _btnInstallLibHistoire.Tag = "btn-gold";
                }

                if (status.LibAddonMenuInstalled)
                {
                    _lblLibAddonMenuStatus.Text = "● LibAddonMenu-2.0: Installed";
                    _lblLibAddonMenuStatus.ForeColor = CGreen;
                    _btnInstallLibAddonMenu.Text = "✓ Reinstall / Update";
                    _btnInstallLibAddonMenu.ForeColor = CTextSub;
                    _btnInstallLibAddonMenu.FlatAppearance.BorderColor = CTextSub;
                    _btnInstallLibAddonMenu.BackColor = CBtnBg;
                    _btnInstallLibAddonMenu.Tag = "btn-sub";
                }
                else
                {
                    _lblLibAddonMenuStatus.Text = "▲ LibAddonMenu-2.0: Missing (Required for Settings)";
                    _lblLibAddonMenuStatus.ForeColor = CWarn;
                    _btnInstallLibAddonMenu.Text = "⚡ Install LibAddonMenu";
                    _btnInstallLibAddonMenu.ForeColor = CGoldBrt;
                    _btnInstallLibAddonMenu.FlatAppearance.BorderColor = CGoldBrt;
                    _btnInstallLibAddonMenu.BackColor = CBtnBg;
                    _btnInstallLibAddonMenu.Tag = "btn-gold";
                }
            }
            else
            {
                _lblLibHistoireStatus.Text = "● LibHistoire: Unknown (ESO Path needed)";
                _lblLibHistoireStatus.ForeColor = CTextSub;
                _lblLibAddonMenuStatus.Text = "● LibAddonMenu-2.0: Unknown (ESO Path needed)";
                _lblLibAddonMenuStatus.ForeColor = CTextSub;
            }
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
                RowCount = 4,
                AutoScroll = true,
                Padding = new Padding((int)(16 * _scale)),
            };
            layout.HorizontalScroll.Enabled = false;
            layout.HorizontalScroll.Visible = false;
            layout.HandleCreated += (_, _) => FissalTheme.ApplyDarkScrollbars(layout.Handle);
            layout.Resize += (_, _) =>
            {
                layout.HorizontalScroll.Maximum = 0;
                layout.HorizontalScroll.Visible = false;
            };
            _setupLayout = layout;

            layout.AutoScrollMargin = new Size(0, (int)(24 * _scale));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            int labelColWidth = (int)(175 * _scale);

            // ── Card 1: Device Authentication & Pairing ──
            var authPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                RowCount = 5,
                BackColor = CPanelBg,
                Padding = new Padding((int)(16 * _scale)),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, (int)(12 * _scale)),
            };
            authPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelColWidth));
            authPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            authPanel.Tag = "card";

            var authHeader = new Label
            {
                Text = "DEVICE AUTHENTICATION & PAIRING",
                UseMnemonic = false,
                ForeColor = CGoldBrt,
                Font = Title(11f, _scale, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, (int)(14 * _scale)),
            };
            authPanel.Controls.Add(authHeader, 0, 0);
            authPanel.SetColumnSpan(authHeader, 2);

            // Display Name
            authPanel.Controls.Add(MakeFieldLabel("Trader Display Name:"), 0, 1);
            _txtDisplayName = MakeStyledTextBox(AppConfig.Instance.DisplayName);
            _txtDisplayName.Margin = new Padding(0, (int)(3 * _scale), 0, (int)(6 * _scale));
            authPanel.Controls.Add(_txtDisplayName, 1, 1);

            // Pairing Code
            authPanel.Controls.Add(MakeFieldLabel("Relay Pairing Code:"), 0, 2);
            var pairLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0, (int)(3 * _scale), 0, (int)(6 * _scale)) };
            pairLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pairLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(140 * _scale)));

            _txtPairingCode = MakeStyledTextBox(AppConfig.Instance.PairingCode);
            pairLayout.Controls.Add(_txtPairingCode, 0, 0);

            _btnPairDevice = MakeStyledButton("Pair Device Now", CGreen);
            _btnPairDevice.Margin = new Padding((int)(8 * _scale), 0, 0, 0);
            _btnPairDevice.Click += async (_, _) => await RunDevicePairingAsync();
            pairLayout.Controls.Add(_btnPairDevice, 1, 0);
            authPanel.Controls.Add(pairLayout, 1, 2);

            // Pairing Status
            authPanel.Controls.Add(MakeFieldLabel("Pairing Status:"), 0, 3);
            _lblPairingStatus = new Label
            {
                Text = "Inspecting...",
                ForeColor = CText,
                Font = Mono(9f, _scale, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, (int)(4 * _scale), 0, (int)(6 * _scale)),
            };
            authPanel.Controls.Add(_lblPairingStatus, 1, 3);

            // Device Info
            authPanel.Controls.Add(MakeFieldLabel("Device Details:"), 0, 4);
            _lblDeviceInfo = new Label
            {
                Text = "Loading...",
                ForeColor = CTextSub,
                Font = Mono(8f, _scale),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, (int)(3 * _scale), 0, 0),
            };
            authPanel.Controls.Add(_lblDeviceInfo, 1, 4);

            layout.Controls.Add(authPanel, 0, 0);

            // ── Card 2: Lightweight 1-line ESO Addon Status Banner ──
            var addonBanner = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = CPanelBg,
                Padding = new Padding((int)(14 * _scale), (int)(10 * _scale), (int)(14 * _scale), (int)(10 * _scale)),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, (int)(12 * _scale)),
                Tag = "card"
            };
            addonBanner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            addonBanner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(220 * _scale)));

            _lblSetupAddonStatus = new Label
            {
                Text = "● Checking addon...",
                ForeColor = CGoldBrt,
                Font = Mono(8.5f, _scale, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            addonBanner.Controls.Add(_lblSetupAddonStatus, 0, 0);

            var btnGoToAddon = MakeStyledButton("Go to ESO Addon Manager →", CText);
            btnGoToAddon.AutoSize = true;
            btnGoToAddon.Click += (_, _) => SwitchTab("addon");
            addonBanner.Controls.Add(btnGoToAddon, 1, 0);

            layout.Controls.Add(addonBanner, 0, 1);

            // ── Card 3: Relay Preferences & Network Endpoints ──
            var prefsPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                RowCount = 5,
                BackColor = CPanelBg,
                Padding = new Padding((int)(16 * _scale)),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, (int)(12 * _scale)),
            };
            prefsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelColWidth));
            prefsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            prefsPanel.Tag = "card";

            var prefsHeader = new Label
            {
                Text = "RELAY PREFERENCES & ENDPOINTS",
                UseMnemonic = false,
                ForeColor = CGoldBrt,
                Font = Title(11f, _scale, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, (int)(14 * _scale)),
            };
            prefsPanel.Controls.Add(prefsHeader, 0, 0);
            prefsPanel.SetColumnSpan(prefsHeader, 2);

            // Server URL
            prefsPanel.Controls.Add(MakeFieldLabel("Sync Server URL:"), 0, 1);
            _txtServerUrl = MakeStyledTextBox(AppConfig.Instance.ServerUrl);
            _txtServerUrl.Margin = new Padding(0, (int)(3 * _scale), 0, (int)(6 * _scale));
            prefsPanel.Controls.Add(_txtServerUrl, 1, 1);

            // Silent Background Sync
            prefsPanel.Controls.Add(MakeFieldLabel("Background Alerts:"), 0, 2);
            _btnSilentSync = MakeStyledButton("", CGreen);
            _btnSilentSync.AutoSize = true;
            _btnSilentSync.Margin = new Padding(0, (int)(3 * _scale), 0, (int)(6 * _scale));
            _btnSilentSync.Click += (_, _) =>
            {
                var cfg = AppConfig.Instance;
                cfg.SilentSync = !cfg.SilentSync;
                cfg.Save();
                UpdateSilentSyncButton();
            };
            _btnSilentSync.MinimumSize = new Size((int)(180 * _scale), (int)(28 * _scale));
            UpdateSilentSyncButton();
            prefsPanel.Controls.Add(_btnSilentSync, 1, 2);

            // MasterMerchant Sync Toggle
            prefsPanel.Controls.Add(MakeFieldLabel("MasterMerchant Sync:"), 0, 3);
            _btnSyncMm = MakeStyledButton("", CGoldBrt);
            _btnSyncMm.AutoSize = true;
            _btnSyncMm.MinimumSize = new Size((int)(180 * _scale), (int)(28 * _scale));
            _btnSyncMm.Margin = new Padding(0, (int)(3 * _scale), 0, (int)(6 * _scale));
            _btnSyncMm.Click += (_, _) =>
            {
                var cfg = AppConfig.Instance;
                cfg.SyncMasterMerchantFiles = !cfg.SyncMasterMerchantFiles;
                cfg.Save();
                UpdateSyncMmButton();
            };
            UpdateSyncMmButton();
            prefsPanel.Controls.Add(_btnSyncMm, 1, 3);

            // Action Buttons
            prefsPanel.Controls.Add(MakeFieldLabel("Preferences Actions:"), 0, 4);
            var btnRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                Margin = new Padding(0, (int)(4 * _scale), 0, 0),
            };

            _btnSaveSetup = MakeStyledButton("Save Settings", CGoldBrt);
            _btnSaveSetup.Margin = new Padding(0, 0, (int)(8 * _scale), (int)(4 * _scale));
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
            _btnTestConnection.Margin = new Padding(0, 0, (int)(8 * _scale), (int)(4 * _scale));
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

            prefsPanel.Controls.Add(btnRow, 1, 4);

            layout.Controls.Add(prefsPanel, 0, 2);

            _setupView.Controls.Add(layout);
        }

        private async Task RunDevicePairingAsync()
        {
            string code = _txtPairingCode.Text.Trim();
            var cfg = AppConfig.Instance;
            if (string.IsNullOrWhiteSpace(code))
            {
                if (!string.IsNullOrWhiteSpace(cfg.ApiKey))
                {
                    code = cfg.ApiKey.Trim();
                }
                else
                {
                    FissalBox.Show("Please enter a pairing code from the Redfur web interface, or configure an API key.", "Pairing Code Missing");
                    return;
                }
            }

            _btnPairDevice.Enabled = false;
            _btnPairDevice.Text = "Pairing...";

            try
            {
                cfg.PairingCode = code;
                cfg.DisplayName = _txtDisplayName.Text.Trim();
                cfg.Save();

                var (paired, message) = await _watcher.PairDeviceAsync();

                if (paired)
                {
                    RefreshSetupView();
                    FissalBox.Show(message, "Pairing Complete");
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

            bool hasToken = !string.IsNullOrWhiteSpace(cfg.DeviceToken);
            bool hasKey = !string.IsNullOrWhiteSpace(cfg.ApiKey);

            if (hasToken)
            {
                _lblPairingStatus.Text = "✔ PAIRED WITH LATTICE (Device Token Active)";
                _lblPairingStatus.ForeColor = CGreen;
            }
            else if (hasKey)
            {
                _lblPairingStatus.Text = "◆ MASTER / DEDICATED API KEY (Owner Mode Active)";
                _lblPairingStatus.ForeColor = CWarn;
            }
            else
            {
                _lblPairingStatus.Text = "✖ UNPAIRED / CODE REQUIRED";
                _lblPairingStatus.ForeColor = CBarFail;
            }

            string authMode = hasToken ? "Device Token (rfr_... / DPAPI Encrypted)" : hasKey ? "Master API Key (Direct Owner Access)" : "Unlinked";
            _lblDeviceInfo.Text = $"Auth Mode: {authMode}\nToken Storage: DPAPI Encrypted (CurrentUser)\nUpdate Endpoint: {cfg.UpdateUrl}";
            UpdateSilentSyncButton();
            UpdateSyncMmButton();

            if (_lblSetupAddonStatus != null && !_lblSetupAddonStatus.IsDisposed)
            {
                var addonStatus = AddonInstallerService.CheckAddonInstallStatus();
                switch (addonStatus.State)
                {
                    case AddonInstallState.UpToDate:
                        _lblSetupAddonStatus.Text = $"● Addon v{addonStatus.InstalledVersion} is Installed & Up to Date";
                        _lblSetupAddonStatus.ForeColor = CGreen;
                        break;
                    case AddonInstallState.UpdateAvailable:
                        _lblSetupAddonStatus.Text = $"▲ Update Available: v{addonStatus.InstalledVersion ?? "?"} installed → v{addonStatus.LatestVersion} available";
                        _lblSetupAddonStatus.ForeColor = CWarn;
                        break;
                    case AddonInstallState.NotInstalled:
                        _lblSetupAddonStatus.Text = "✖ Addon Not Installed";
                        _lblSetupAddonStatus.ForeColor = CBarFail;
                        break;
                    case AddonInstallState.EsoNotFound:
                    default:
                        _lblSetupAddonStatus.Text = "⚠ ESO Directory Not Found";
                        _lblSetupAddonStatus.ForeColor = CWarn;
                        break;
                }
            }
        }

        private void UpdateSilentSyncButton()
        {
            if (_btnSilentSync == null || _btnSilentSync.IsDisposed) return;
            bool silent = AppConfig.Instance.SilentSync;
            _btnSilentSync.Text = silent ? "◆  SILENT BACKGROUND SYNC (MUTED)" : "◇  SILENT BACKGROUND SYNC (ALERTS ACTIVE)";
            _btnSilentSync.ForeColor = silent ? CGreen : CWarn;
        }

        private void UpdateSyncMmButton()
        {
            if (_btnSyncMm == null || _btnSyncMm.IsDisposed) return;
            bool syncMm = AppConfig.Instance.SyncMasterMerchantFiles;
            _btnSyncMm.Text = syncMm
                ? "◆  LEGACY MM SYNC ACTIVE (Uploading all 18 GSxx files)"
                : "◇  AUTO-OPTIMIZED (Fissal Relay handles sales; GS files skipped)";
            _btnSyncMm.ForeColor = syncMm ? CWarn : CGreen;
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
                Padding = new Padding((int)(12 * _scale)),
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(40 * _scale)));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(92 * _scale)));

            // Header
            var header = new Label
            {
                Text = "SELECT TERMINAL THEME & VISUAL FIDELITY",
                UseMnemonic = false,
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
                Padding = new Padding((int)(8 * _scale)),
            };
            _themeCardsHost.HandleCreated += (_, _) => FissalTheme.ApplyDarkScrollbars(_themeCardsHost.Handle);
            PopulateThemeCards();
            layout.Controls.Add(_themeCardsHost, 0, 1);

            // Performance & Scale Settings
            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = CPanelBg,
                Padding = new Padding((int)(14 * _scale), (int)(10 * _scale), (int)(14 * _scale), (int)(10 * _scale)),
                Margin = new Padding(0, (int)(8 * _scale), 0, 0),
            };
            footer.Paint += (s, e) =>
            {
                var g = e.Graphics;
                using var pen = new Pen(CBorderSub, 1f);
                g.DrawRectangle(pen, 0, 0, footer.Width - 1, footer.Height - 1);
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
                    var g = e.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    Color borderCol = isSelected ? p.Green : p.BorderSub;
                    float borderW = isSelected ? 2f : 1f;
                    using var pen = new Pen(borderCol, borderW);
                    g.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);

                    if (isSelected)
                    {
                        using var jewelBrush = new SolidBrush(p.Green);
                        g.FillPolygon(jewelBrush, new[] {
                            new PointF(card.Width - (int)(14 * _scale), 0),
                            new PointF(card.Width, 0),
                            new PointF(card.Width, (int)(14 * _scale))
                        });
                    }
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
                Width = (int)(22 * _scale),
                Height = (int)(14 * _scale),
                BackColor = color,
                Margin = new Padding(0, 0, (int)(4 * _scale), 0),
            };
            swatch.Paint += (s, e) =>
            {
                using var p = new Pen(Color.FromArgb(60, 255, 255, 255), 1f);
                e.Graphics.DrawRectangle(p, 0, 0, swatch.Width - 1, swatch.Height - 1);
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
                Padding = new Padding((int)(12 * _scale)),
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
                Padding = new Padding((int)(10 * _scale), (int)(6 * _scale), (int)(10 * _scale), (int)(6 * _scale)),
            };
            topCard.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(140 * _scale)));
            topCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            topCard.Tag = "card";
            topCard.Controls.Add(MakeFieldLabel("ESO Directory:"), 0, 0);
            _esoPathLabel = new Label { Text = "Detecting...", ForeColor = CTextSub, Font = Mono(7.5f, _scale), Dock = DockStyle.Fill, Tag = "sub" };
            topCard.Controls.Add(_esoPathLabel, 1, 0);

            topCard.Controls.Add(MakeFieldLabel("Watcher Telemetry:"), 0, 1);
            _watcherStatusLabel = new Label { Text = "Active", ForeColor = CGreen, Font = Mono(8.5f, _scale, FontStyle.Bold), Dock = DockStyle.Fill };
            topCard.Controls.Add(_watcherStatusLabel, 1, 1);

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
                WrapContents = true,
                AutoSize = true,
                BackColor = CPanelBg,
                Padding = new Padding((int)(8 * _scale), (int)(6 * _scale), (int)(8 * _scale), (int)(6 * _scale)),
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
            string resolvedEsoPath = AppConfig.Instance.CustomEsoLiveDirectory ?? AddonInstallerService.FindEsoLiveDirectory() ?? "";
            bool hasValidEsoDir = !string.IsNullOrWhiteSpace(resolvedEsoPath) && Directory.Exists(resolvedEsoPath);

            if (hasValidEsoDir)
            {
                _esoPathLabel.Text = resolvedEsoPath;
                _esoPathLabel.ForeColor = CText;
            }
            else
            {
                _esoPathLabel.Text = "Not Detected — Configure in Setup or Addon Tab";
                _esoPathLabel.ForeColor = CWarn;
            }

            if (!hasValidEsoDir)
            {
                _watcherStatusLabel.Text = "⚠ Inactive (ESO Directory inaccessible)";
                _watcherStatusLabel.ForeColor = CWarn;
            }
            else if (_watcher.GetJobsSnapshot().Any(j => j.Status == UploadStatus.Uploading))
            {
                _watcherStatusLabel.Text = "● Transmitting File Data...";
                _watcherStatusLabel.ForeColor = CGoldBrt;
            }
            else
            {
                _watcherStatusLabel.Text = "● Actively Monitoring (Standing by for live game events)";
                _watcherStatusLabel.ForeColor = CGreen;
            }

            string context = _watcher.GetAssistantContext();
            _diagLogBox.Text = $"[FISSAL TONAL RELAY DIAGNOSTICS SNAPSHOT — {DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n\n" + context;
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
            _headerConsole.BackColor = CPanelBgAlt;
            _titleMarkLabel.Text = ThemeMark + " ";
            _titleMarkLabel.ForeColor = CGoldBrt;
            _titleTextLabel.ForeColor = CGoldBrt;
            _titleThemeBadge.Text = $"[{Current.DisplayName.ToUpperInvariant()}]";
            _titleThemeBadge.ForeColor = CGreen;

            _navRail.BackColor = CPanelBg;
            _contentHost.BackColor = CBg;

            foreach (var item in _navItems)
            {
                item.viewPanel.BackColor = CBg;
                item.viewPanel.ForeColor = CText;
                item.panel.BackColor = CPanelBg;
            }

            // Hierarchical semantic re-theming across all views
            foreach (var item in _navItems)
            {
                ApplyThemeToHierarchy(item.viewPanel);
            }
            ApplyThemeToHierarchy(_contentHost);
            ApplyThemeToHierarchy(_headerConsole);
            ApplyThemeToHierarchy(_navRail);

            PopulateThemeCards();
            SwitchTab(_activeTabId);
            RefreshAllViews();
            Invalidate(true);
        }

        private void RefreshAllViews()
        {
            RefreshSyncView();
            RefreshAddonView();
            RefreshSetupView();
            RefreshDiagnosticsView();
        }

        private void OnSyncCoalesceTick(object? state)
        {
            if (IsDisposed || !IsHandleCreated || !Visible || WindowState == FormWindowState.Minimized) return;
            if (_isRefreshingSyncView) return;
            long currentVer = _watcher.CurrentJobVersion;
            if (currentVer != _lastRenderedJobVersion)
            {
                _lastRenderedJobVersion = currentVer;
                _syncContext?.Post(_ =>
                {
                    if (!IsDisposed && IsHandleCreated && Visible && WindowState != FormWindowState.Minimized)
                    {
                        RefreshSyncView();
                    }
                }, null);
            }
        }

        private void OnWatcherJobsChanged()
        {
            // Coalesced 150ms timer handles rendering dispatching to prevent UI thrashing during file bursts
        }

        private void OnWatcherConnectionChecked(bool ok, string msg)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => OnWatcherConnectionChecked(ok, msg));
                return;
            }
            _isConnected = ok;
            _titleStatusLabel.Text = ok ? "● CONNECTED TO REDFUR RELAY" : "⚠ DISCONNECTED";
            _titleStatusLabel.ForeColor = ok ? CGreen : CBarFail;
            _messageBoardText = ok ? "All guild trader lines & bank deposits verified • Watching ESO" : "Connection Degraded — Check server URL in Setup";
            _messageBoardColor = ok ? CGreen : CBarFail;
            if (_messageBoardLabel != null)
            {
                _messageBoardLabel.Text = _messageBoardText;
                _messageBoardLabel.ForeColor = _messageBoardColor;
            }

            LogTelemetry(ok ? "ONLINE" : "ALERT", ok ? $"Server connection verified: {msg}" : $"Connection issue: {msg}", ok ? CGreen : CBarFail);
        }

        private void OnTitleBarMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || WindowState == FormWindowState.Maximized) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, 0x2, 0);
        }

        // ── Helper UI Controls ────────────────────────────────────────────────
        private Button MakeStyledButton(string text, Color accent, string? role = null)
        {
            var btn = new Button
            {
                Text = text,
                ForeColor = accent,
                BackColor = CBtnBg,
                FlatStyle = FlatStyle.Flat,
                Font = Title(8.5f, _scale, FontStyle.Bold),
                Cursor = Cursors.Hand,
                AutoSize = true,
                Padding = new Padding((int)(12 * _scale), (int)(5 * _scale), (int)(12 * _scale), (int)(5 * _scale)),
                Margin = new Padding(0, 0, (int)(8 * _scale), 0),
                Tag = role ?? (accent == CGreen ? "btn-green" : (accent == CWarn ? "btn-warn" : (accent == CBarFail ? "btn-fail" : (accent == CTextSub ? "btn-sub" : (accent == CText ? "btn-text" : "btn-gold"))))),
            };
            btn.AutoEllipsis = false;
            btn.TextAlign = ContentAlignment.MiddleCenter;
            btn.MinimumSize = new Size((int)(80 * _scale), (int)(26 * _scale));
            btn.FlatAppearance.BorderColor = accent;
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(45, accent.R, accent.G, accent.B);
            return btn;
        }

        private TextBox MakeStyledTextBox(string initialText)
        {
            return new TextBox
            {
                Text = initialText,
                BackColor = CBg,  // D9: was hardcoded #0C0E12, now theme-aware
                ForeColor = CText,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Mono(9.5f, _scale),
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
                Tag = "field",
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyDarkModeScrollbars();
        }

        private void ApplyDarkModeScrollbars()
        {
            try
            {
                if (IsHandleCreated)
                    FissalTheme.ApplyWindowDarkMode(Handle);
                if (_syncJobsList != null && _syncJobsList.IsHandleCreated)
                    FissalTheme.ApplyDarkScrollbars(_syncJobsList.Handle);
                if (_syncLogBox != null && _syncLogBox.IsHandleCreated)
                    FissalTheme.ApplyDarkScrollbars(_syncLogBox.Handle);
                if (_diagLogBox != null && _diagLogBox.IsHandleCreated)
                    FissalTheme.ApplyDarkScrollbars(_diagLogBox.Handle);
                if (_addonLayout != null && _addonLayout.IsHandleCreated)
                    FissalTheme.ApplyDarkScrollbars(_addonLayout.Handle);
                if (_setupLayout != null && _setupLayout.IsHandleCreated)
                    FissalTheme.ApplyDarkScrollbars(_setupLayout.Handle);
                if (_themeCardsHost != null && _themeCardsHost.IsHandleCreated)
                    FissalTheme.ApplyDarkScrollbars(_themeCardsHost.Handle);
            }
            catch { }
        }

        private sealed class DwemerToggleControl : Control
        {
            private bool _checked;
            private readonly string _label;
            private readonly Color _activeColor;
            private readonly float _scale;

            public event EventHandler? CheckedChanged;

            [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
            public bool Checked
            {
                get => _checked;
                set
                {
                    if (_checked != value)
                    {
                        _checked = value;
                        Invalidate();
                        CheckedChanged?.Invoke(this, EventArgs.Empty);
                    }
                }
            }

            public DwemerToggleControl(string label, Color activeColor, float scale, bool initialChecked)
            {
                _label = label;
                _activeColor = activeColor;
                _scale = scale;
                _checked = initialChecked;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                Cursor = Cursors.Hand;
                Height = (int)(28 * _scale);
                Width = (int)(155 * _scale);
            }

            protected override void OnClick(EventArgs e)
            {
                base.OnClick(e);
                if (Enabled) Checked = !Checked;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                FissalTheme.DrawDwemerToggle(e.Graphics, ClientRectangle, _label, _checked && Enabled, Enabled ? _activeColor : Color.FromArgb(80, CBorderSub), _scale);
            }
        }
    }
}
