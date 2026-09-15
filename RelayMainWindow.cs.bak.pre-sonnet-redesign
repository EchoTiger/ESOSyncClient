using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
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
        private Panel _assistantView = null!;
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
        private readonly Dictionary<string, UploadStatus> _loggedJobStates = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<UploadJob, JobCardControls> _jobCards = new();
        private readonly HashSet<string> _userCollapsedSessions = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _userExpandedSessions = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SessionCardControls> _sessionCards = new(StringComparer.OrdinalIgnoreCase);
        private System.Windows.Forms.Timer? _animTimer;
        private int _animFrame = 0;
        private Label _tickerLabel = null!;
        private Panel _tickerPanel = null!;
        private bool _batchInProgress = false;
        private bool _syncRefreshPending;
        private bool _isConnected = true;

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

        // ── 2. Ask Fissal Controls ──
        private FlowLayoutPanel _transcript = null!;
        private TextBox _prompt = null!;
        private Button _send = null!;
        private Label _assistantStatus = null!;
        private Label _assistantModelLabel = null!;
        private DwemerToggleControl _harnessToggle = null!;
        private DwemerToggleControl _writePermsToggle = null!;
        private readonly List<(string role, string text)> _chatHistory = new();
        private readonly FissalHarnessService _harnessService = new(AppConfig.Instance);

        // ── 3. Setup Controls ──
        private TextBox _txtDisplayName = null!;
        private TextBox _txtPairingCode = null!;
        private TextBox _txtServerUrl = null!;
        private Label _lblPairingStatus = null!;
        private Label _lblDeviceInfo = null!;
        private Button _btnSilentSync = null!;
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
            MinimumSize = new Size(880, 560);
            Size = new Size(980, 620);
            BackColor = CBg;
            ForeColor = CText;
            ShowInTaskbar = true;
            Text = "Fissal Relay // Dwemer Tonal Terminal";

            _scale = GetScale(Handle);

            TraceLog("BuildShell starting");
            BuildShell();
            TraceLog("BuildViewPanels starting");
            BuildViewPanels();
            TraceLog("SwitchTab starting");
            SwitchTab("sync");
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
                if (_transcript.Controls.Count == 0)
                {
                    AddAssistantMessage(false, "*purrs warmly* Welcome back to the bench! Fissal's tonal lattice is humming smoothly. I can inspect our live sync cassettes, check your ESO data scrolls, explain anomaly logs, or tune our apparatus settings. What shall we look into together?");
                    // Keep greeting at top
                    _transcript.AutoScrollPosition = new Point(0, 0);
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
            TraceLog("NavigateToTab: Show()");
            Show();
            TraceLog("NavigateToTab: Show() returned");
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }
            TopMost = true;
            Activate();
            BringToFront();
            TopMost = false;
            try { SetForegroundWindow(Handle); } catch { }
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
            _headerConsole.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(320 * _scale))); // Left Identity Plate
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

                // 1. Recessed CRT screen plate
                using var mcPath = FissalTheme.RoundRect(0, 0, w - 1, h - 1, (int)(4 * _scale));
                using (var mcBg = new SolidBrush(Color.FromArgb(255, 4, 12, 6)))
                    g.FillPath(mcBg, mcPath);

                // 2. Inner shadow gradient to push screen backward
                using (var mcInnerShadow = new LinearGradientBrush(
                    new Rectangle(0, 0, w, h),
                    Color.FromArgb(220, 5, 10, 15), Color.Transparent, LinearGradientMode.Vertical))
                {
                    mcInnerShadow.SetBlendTriangularShape(0.25f);
                    g.FillPath(mcInnerShadow, mcPath);
                }

                // 3. CRT green scanlines
                using (var mcScanPen = new Pen(Color.FromArgb(22, CGreen), 1f))
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

                var safeJobs = new List<UploadJob>();
                lock (_watcher.Jobs)
                {
                    for (int k = 0; k < _watcher.Jobs.Count; k++)
                        try { safeJobs.Add(_watcher.Jobs[k]); } catch { break; }
                }

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
                    statuses.Add(("⚠ DISCONNECTED FROM CASTLE ECHO // RETRYING TONAL LINK", CBarFail, 2));
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

                    statuses.Add(("> STAND BY... MONITORING ESO LIVE", Color.FromArgb(255, 50, 255, 50), 0));
                    statuses.Add(("● TONAL TRANSCEIVER RESONANT • CASTLE ECHO", Color.FromArgb(255, 50, 255, 50), 0));
                    if (!string.IsNullOrEmpty(userStatus)) statuses.Add((userStatus, Color.FromArgb(255, 50, 255, 50), 0));
                    if (safeJobs.Count > 0)
                    {
                        int doneCount = safeJobs.Count(j => j.Status == UploadStatus.Done);
                        statuses.Add(($"> {doneCount} TRANSMISSIONS ARCHIVED", Color.FromArgb(255, 50, 255, 50), 0));
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

            Control MakeCircularWinBtn(string symbol, Color capColor, Action onClick)
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
                    FissalTheme.DrawDwemerCircularButton(e.Graphics, new Rectangle(0, 0, btn.Width, btn.Height), symbol, capColor, isHover, isPressed, _scale);
                };
                return btn;
            }

            rightDeck.Controls.Add(MakeCircularWinBtn("✕", Color.FromArgb(195, 38, 38), () => Hide()));
            rightDeck.Controls.Add(MakeCircularWinBtn("□", Color.FromArgb(68, 56, 40), () => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized));
            rightDeck.Controls.Add(MakeCircularWinBtn("—", Color.FromArgb(68, 56, 40), () => WindowState = FormWindowState.Minimized));

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
            AddNavButton(navStack, "assistant",   "🐾 Transceiver",      "Commune with Fissal for relay diagnostics");
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
            int btnHeight = (int)(42 * _scale);
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
            else if (id == "diagnostics") RefreshDiagnosticsView();
            else if (id == "setup") RefreshSetupView();
            else if (id == "assistant")
            {
                BeginInvoke(() =>
                {
                    ApplyDarkModeScrollbars();
                    ResizeAssistantCards();
                    _prompt?.Focus();
                });
            }
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
                LogTelemetry("REFRESH", $"Audited {_watcher.Jobs.Count} live transmission cassettes.", CGoldBrt);
            };
            topBar.Controls.Add(_btnRefreshJobs, 2, 0);

            _btnClearCompleted = MakeStyledButton("Clear Done", CTextSub);
            _btnClearCompleted.Height = (int)(28 * _scale);
            _btnClearCompleted.Margin = new Padding((int)(4 * _scale), 0, (int)(4 * _scale), 0);
            _btnClearCompleted.Click += (_, _) =>
            {
                var doneJobs = _watcher.Jobs.Where(j => j.Status is UploadStatus.Done or UploadStatus.Cancelled).ToList();
                foreach (var j in doneJobs) _watcher.Jobs.Remove(j);
                RefreshSyncView();
                LogTelemetry("CLEARED", $"Cleared {doneJobs.Count} completed sync records from live deck.", CTextSub);
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

            _oscilloscopePanel = new DoubleBufferedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(4, 9, 6),
                Margin = new Padding(0, 0, (int)(4 * _scale), 0),
            };
            _oscilloscopePanel.Paint += (s, e) =>
            {
                var g = e.Graphics;
                var activeJob = _watcher.Jobs.FirstOrDefault(j => j.Status == UploadStatus.Uploading);
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
            if (IsDisposed) return;
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
            bool isUploading = _watcher.Jobs.Any(j => j.Status == UploadStatus.Uploading);
            bool hasError = _watcher.Jobs.Any(j => j.Status == UploadStatus.Failed || j.Status == UploadStatus.Cancelled);
            bool hasUpdate = _watcher.Jobs.Any(j => j.Status == UploadStatus.UpdateReady);

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
            _syncJobsList?.Invalidate();

            var activeJob = _watcher.Jobs.FirstOrDefault(j => j.Status == UploadStatus.Uploading);
            if (activeJob == null)
            {
                int queued = _watcher.Jobs.Count(j => j.Status == UploadStatus.Queued);
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

                _tickerLabel.Text = $"{spin} [TRANSMITTING] {activeJob.FileName} • {pct}% {bar} {wave} CASTLE ECHO";
                _tickerLabel.ForeColor = CGoldBrt;
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
            if (_syncLogBox.Lines.Length > 300)
            {
                _syncLogBox.Select(0, _syncLogBox.GetFirstCharIndexFromLine(50));
                _syncLogBox.SelectedText = "";
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
            _syncLogBox.ScrollToCaret();
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

            var jobs = _watcher.Jobs.ToList();
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
                _titleStatusLabel.Text = failed > 0 ? "⚠ ATTENTION NEEDED" : "● CONNECTED TO CASTLE ECHO";
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
                LogTelemetry("BATCH", $"└─── ✓ BATCH COMPLETE: All telemetry cassettes synchronized to Castle Echo ───", CGreen);
            }

            // Telemetry tracking for individual state transitions
            foreach (var job in jobs)
            {
                if (!_loggedJobStates.TryGetValue(job.FileName, out var lastStatus) || lastStatus != job.Status)
                {
                    _loggedJobStates[job.FileName] = job.Status;
                    if (job.Status == UploadStatus.Done)
                        LogTelemetry("VERIFIED", $"{job.FileName} synchronized to Castle Echo ({job.FileSizeDisplay})", CGreen);
                    else if (job.Status == UploadStatus.Failed)
                        LogTelemetry("ALERT", $"{job.FileName} failed: {job.ErrorMessage}", CBarFail);
                    else if (job.Status == UploadStatus.UpdateReady)
                        LogTelemetry("UPGRADE", $"{job.FileName} ready for deployment ({job.FileSizeDisplay})", Color.FromArgb(196, 137, 255));
                    else if (job.Status == UploadStatus.Uploading)
                        LogTelemetry("TRANSMIT", $"{job.FileName} uploading ({job.FileSizeDisplay})...", CGoldBrt);
                }
            }

            // Separate standalone update jobs and cluster regular files into sessions
            var updateJobs = jobs.Where(j => j.IsUpdate).ToList();
            var normalJobs = jobs.Where(j => !j.IsUpdate).OrderByDescending(j => j.QueuedAt).ToList();

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

            _syncSummaryLabel.Text = $"Sessions: {sessions.Count}  |  Active: {uploading}  |  Queued: {queued}  |  Synced: {done}  |  Errors: {failed}  |  Total: {jobs.Count}";

            _syncJobsList.SuspendLayout();

            // 1. Remove empty placeholder if we have content
            if (jobs.Count > 0)
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
            var currentSessionIds = sessions.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
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

            foreach (var session in sessions)
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
                    _syncJobsList.Controls.SetChildIndex(uc.Card, controlIndex++);
            }
            foreach (var s in sessions)
            {
                if (_sessionCards.TryGetValue(s.Id, out var sc))
                    _syncJobsList.Controls.SetChildIndex(sc.Card, controlIndex++);
            }

            // 5. Empty placeholder when no jobs exist
            if (jobs.Count == 0 && _syncJobsList.Controls.Count == 0)
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
                    Text = "✓ All guild data synchronized with Castle Echo. Monitoring for changes.",
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
                    UploadStatus.Uploading => CGoldBrt,
                    UploadStatus.Failed => CBarFail,
                    _ => CWarn
                };

                int pillarW = (int)(5 * _scale);
                using (var pillarBrush = new SolidBrush(accentCol))
                    g.FillRectangle(pillarBrush, 1, 1, pillarW, card.Height - 2);

                using (var flareBrush = new SolidBrush(Color.FromArgb(35, accentCol)))
                    g.FillRectangle(flareBrush, 1 + pillarW, 1, (int)(8 * _scale), card.Height - 2);

                // 4. Dwemer brass chassis corner rivets
                int rivetOffset = (int)(6 * _scale);
                int rivetR = Math.Max(2, (int)(1.75f * _scale));
                DrawCornerRivets(g, card.Width, card.Height, rivetOffset, Color.FromArgb(90, CGoldMid));
            };

            // Files container (nested compact rows for batch contents)
            var filesContainer = new DoubleBufferedFlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.FromArgb(10, 8, 7),
                Padding = new Padding((int)(12 * _scale), (int)(6 * _scale), (int)(12 * _scale), (int)(8 * _scale)),
                Margin = new Padding(0),
                Visible = false,
            };

            filesContainer.Paint += (s, e) =>
            {
                var g = e.Graphics;
                // Subtle phosphor glow inside the open cassette deck
                DrawTerminalMesh(g, new Rectangle(0, 0, filesContainer.Width, filesContainer.Height), _scale, 4);
            };

            // Header panel (clickable top strip)
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
                if (IsSessionExpanded(session))
                {
                    using var divPen = new Pen(Color.FromArgb(50, CGoldMid), 1f) { DashStyle = DashStyle.Dash };
                    g.DrawLine(divPen, (int)(10 * _scale), headerPanel.Height - 1, headerPanel.Width - (int)(10 * _scale), headerPanel.Height - 1);
                }

                if (session.AggregateStatus == UploadStatus.Uploading)
                {
                    int barH = Math.Max(2, (int)(3 * _scale));
                    int barY = headerPanel.Height - barH;
                    float prog = Math.Clamp(session.AggregateProgress, 0.05f, 1f);
                    int fillW = (int)(headerPanel.Width * prog);

                    using var barBg = new SolidBrush(Color.FromArgb(40, CGoldMid));
                    g.FillRectangle(barBg, 0, barY, headerPanel.Width, barH);

                    using var barBrush = new SolidBrush(CGoldBrt);
                    g.FillRectangle(barBrush, 0, barY, fillW, barH);
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

            // Order: filesContainer below headerPanel
            card.Controls.Add(filesContainer);
            card.Controls.Add(headerPanel);
            card.Controls.SetChildIndex(headerPanel, 0);
            card.Controls.SetChildIndex(filesContainer, 1);

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

        private void UpdateSessionCard(SyncSessionModel session, SessionCardControls controls)
        {
            controls.Session = session;
            bool isExpanded = IsSessionExpanded(session);

            controls.ChevronLabel.Text = isExpanded ? "▼" : "▶";
            controls.TitleLabel.Text = session.Title;
            controls.SubtitleLabel.Text = $"{session.TotalCount} files • {session.TotalSizeDisplay}";

            controls.StatusLabel.Text = session.AggregateStatus switch
            {
                UploadStatus.Uploading => $"⚡ TRANSMITTING ({session.DoneCount}/{session.TotalCount})",
                UploadStatus.Queued => $"⏳ QUEUED ({session.TotalCount} files)",
                UploadStatus.Done => $"✓ ALL SYNCHRONIZED ({session.TotalCount} files)",
                UploadStatus.Failed => $"⚠ {session.FailedCount} FAILED",
                _ => session.AggregateStatus.ToString().ToUpperInvariant()
            };
            controls.StatusLabel.ForeColor = session.AggregateStatus switch
            {
                UploadStatus.Done => CGreen,
                UploadStatus.Uploading => CGoldBrt,
                UploadStatus.Queued => CWarn,
                UploadStatus.Failed => CBarFail,
                _ => CTextSub
            };

            // Action buttons
            controls.ActionFlow.SuspendLayout();
            controls.ActionFlow.Controls.Clear();
            if (session.FailedCount > 0)
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

            // Reconcile nested file rows if expanded
            int headerH = (int)(48 * _scale);
            if (isExpanded)
            {
                controls.FilesContainer.SuspendLayout();

                var currentJobs = session.Jobs;
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

                foreach (var job in currentJobs.OrderByDescending(j => j.QueuedAt))
                {
                    if (controls.FileRows.TryGetValue(job, out var rowControls))
                    {
                        UpdateCompactFileRow(job, rowControls);
                    }
                    else
                    {
                        var newRow = BuildCompactFileRow(job);
                        controls.FileRows.Add(job, newRow);
                        controls.FilesContainer.Controls.Add(newRow.Row);
                    }
                }

                int totalFileH = currentJobs.Count * (rowH + rowGap) + containerInnerPad;
                controls.FilesContainer.Height = totalFileH;
                controls.FilesContainer.Visible = true;
                controls.FilesContainer.ResumeLayout(true);

                controls.Card.Height = headerH + totalFileH;
            }
            else
            {
                controls.FilesContainer.Visible = false;
                controls.Card.Height = headerH;
            }

            controls.Card.Invalidate(true);
        }

        private CompactFileRowControls BuildCompactFileRow(UploadJob job)
        {
            var row = new DoubleBufferedPanel
            {
                BackColor = Color.FromArgb(18, 15, 12),
                Margin = new Padding(0, 1, 0, (int)(2 * _scale)),
                Tag = "file-row",
            };

            row.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                using (var bg = new SolidBrush(Color.FromArgb(18, 15, 12)))
                    g.FillRectangle(bg, row.ClientRectangle);

                using var pen = new Pen(Color.FromArgb(35, CBorderSub), 1f);
                g.DrawRectangle(pen, 0, 0, row.Width - 1, row.Height - 1);

                if (job.Status == UploadStatus.Uploading)
                {
                    int barH = Math.Max(2, (int)(2 * _scale));
                    int barY = row.Height - barH;
                    float prog = Math.Clamp(job.Progress, 0.05f, 1f);
                    int fillW = (int)(row.Width * prog);

                    using var barBg = new SolidBrush(Color.FromArgb(30, CGoldMid));
                    g.FillRectangle(barBg, 0, barY, row.Width, barH);

                    using var barBrush = new SolidBrush(CGoldBrt);
                    g.FillRectangle(barBrush, 0, barY, fillW, barH);
                }
                else if (job.Status == UploadStatus.Done)
                {
                    using var dot = new SolidBrush(CGreen);
                    g.FillRectangle(dot, 1, 1, (int)(2 * _scale), row.Height - 2);
                }
                else if (job.Status is UploadStatus.Failed or UploadStatus.Cancelled)
                {
                    using var dot = new SolidBrush(CBarFail);
                    g.FillRectangle(dot, 1, 1, (int)(2 * _scale), row.Height - 2);
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

            controls.StatusLabel.Text = job.Status switch
            {
                UploadStatus.Queued => "⏳ QUEUED",
                UploadStatus.Uploading => $"⚡ UPLOADING {(int)(job.Progress * 100)}%",
                UploadStatus.Done => "✓ SYNCHRONIZED",
                UploadStatus.Failed => string.IsNullOrWhiteSpace(job.ErrorMessage) ? "⚠ FAILED" : $"⚠ {job.ErrorMessage}",
                UploadStatus.Cancelled => "CANCELLED",
                _ => job.Status.ToString().ToUpperInvariant()
            };
            controls.StatusLabel.ForeColor = job.Status switch
            {
                UploadStatus.Done => CGreen,
                UploadStatus.Uploading => CGoldBrt,
                UploadStatus.Queued => CWarn,
                UploadStatus.Failed => CBarFail,
                _ => CTextSub
            };

            controls.ActionFlow.SuspendLayout();
            controls.ActionFlow.Controls.Clear();
            if (job.Status == UploadStatus.Failed)
            {
                var btnRetry = MakeStyledButton("Retry", CGoldBrt);
                btnRetry.Height = (int)(22 * _scale);
                btnRetry.Font = Mono(7f, _scale);
                btnRetry.Click += (_, _) => _watcher.RetryJob(job);
                controls.ActionFlow.Controls.Add(btnRetry);
            }
            else if (job.Status is UploadStatus.Uploading or UploadStatus.Queued)
            {
                var btnCancel = MakeStyledButton("Cancel", CBarFail);
                btnCancel.Height = (int)(22 * _scale);
                btnCancel.Font = Mono(7f, _scale);
                btnCancel.Click += (_, _) => _watcher.CancelJob(job);
                controls.ActionFlow.Controls.Add(btnCancel);
            }
            controls.ActionFlow.ResumeLayout(true);

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
                    UploadStatus.Uploading => CGoldBrt,
                    UploadStatus.UpdateReady => Color.FromArgb(180, 137, 255),
                    UploadStatus.Failed => CBarFail,
                    _ => CWarn
                };

                int pillarW = (int)(4 * _scale);
                using (var dotBrush = new SolidBrush(accentCol))
                {
                    g.FillRectangle(dotBrush, 1, 1, pillarW, card.Height - 2);
                }
                using (var flareBrush = new SolidBrush(Color.FromArgb(30, accentCol)))
                {
                    g.FillRectangle(flareBrush, 1 + pillarW, 1, (int)(6 * _scale), card.Height - 2);
                }

                if (job.Status == UploadStatus.Uploading)
                {
                    int barH = Math.Max(2, (int)(3 * _scale));
                    int barY = card.Height - barH;
                    float prog = Math.Clamp(job.Progress, 0.05f, 1f);
                    int fillW = (int)(card.Width * prog);

                    using var barBg = new SolidBrush(Color.FromArgb(40, CGoldMid));
                    g.FillRectangle(barBg, 0, barY, card.Width, barH);

                    using var barBrush = new SolidBrush(CGoldBrt);
                    g.FillRectangle(barBrush, 0, barY, fillW, barH);
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
                ? (job.Status == UploadStatus.Done ? "Verified • Synchronized to Castle Echo" : "")
                : job.ErrorMessage;
            controls.DetailLabel.ForeColor = string.IsNullOrWhiteSpace(job.ErrorMessage) ? CTextSub : CBarFail;

            // Reconcile action buttons dynamically
            controls.ActionFlow.SuspendLayout();
            controls.ActionFlow.Controls.Clear();
            if (job.Status == UploadStatus.UpdateReady)
            {
                var btnApply = MakeStyledButton("Apply Upgrade", Color.FromArgb(196, 137, 255));
                btnApply.Click += (_, _) => _applyUpdateAction(job);
                controls.ActionFlow.Controls.Add(btnApply);
            }
            else if (job.Status == UploadStatus.Failed)
            {
                var btnRetry = MakeStyledButton("Retry", CGoldBrt);
                btnRetry.Click += (_, _) => _watcher.RetryJob(job);
                controls.ActionFlow.Controls.Add(btnRetry);
            }
            else if (job.Status is UploadStatus.Uploading or UploadStatus.Queued)
            {
                var btnCancel = MakeStyledButton("Cancel", CBarFail);
                btnCancel.Click += (_, _) => _watcher.CancelJob(job);
                controls.ActionFlow.Controls.Add(btnCancel);
            }
            controls.ActionFlow.ResumeLayout(true);

            controls.Card.Invalidate();
        }

        private void ResizeSyncJobCards()
        {
            int targetWidth = Math.Max(200, _syncJobsList.ClientSize.Width - (int)(20 * _scale));
            foreach (Control ctrl in _syncJobsList.Controls)
            {
                if (Equals(ctrl.Tag, "session-card") || Equals(ctrl.Tag, "sync-card") || Equals(ctrl.Tag, "empty-card"))
                {
                    ctrl.Width = targetWidth;
                }
            }

            foreach (var sc in _sessionCards.Values)
            {
                int rowWidth = Math.Max(180, sc.Card.ClientSize.Width - (int)(24 * _scale));
                sc.FilesContainer.Width = sc.Card.ClientSize.Width;
                foreach (var rowCtrl in sc.FileRows.Values)
                {
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
                    sc.FilesContainer.Height = totalFileH;
                    sc.Card.Height = headerH + totalFileH;
                }
                else
                {
                    sc.Card.Height = headerH;
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
                BackColor = CBg,
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(46 * _scale))); // Header & Toggles
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(40 * _scale))); // Quick Action chips
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));                 // Chat transcript
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(76 * _scale))); // Input composer
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(28 * _scale))); // Status / shortcuts

            // Header
            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = CPanelBg,
                Padding = new Padding((int)(12 * _scale), (int)(6 * _scale), (int)(12 * _scale), (int)(6 * _scale)),
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var brandLabel = new Label
            {
                Text = "🐾 TONAL TRANSCEIVER // FISSAL",
                ForeColor = CGoldBrt,
                Font = Title(10f, _scale, FontStyle.Bold),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
            };
            header.Controls.Add(brandLabel, 0, 0);

            _assistantModelLabel = new Label
            {
                Text = "● HARMONICS SYNCHRONIZED",
                ForeColor = CGreen,
                Font = Mono(7.5f, _scale, FontStyle.Bold),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding((int)(12 * _scale), 0, 0, 0),
            };
            header.Controls.Add(_assistantModelLabel, 1, 0);

            _harnessToggle = new DwemerToggleControl("Tonal Attunement", CGreen, _scale, AppConfig.Instance.FissalHarnessEnabled);
            _harnessToggle.Anchor = AnchorStyles.Right;
            _harnessToggle.Margin = new Padding(0, 0, (int)(8 * _scale), 0);
            _harnessToggle.CheckedChanged += (_, _) =>
            {
                AppConfig.Instance.FissalHarnessEnabled = _harnessToggle.Checked;
                if (!_harnessToggle.Checked) _writePermsToggle.Checked = false;
                AppConfig.Instance.Save();
                _writePermsToggle.Enabled = _harnessToggle.Checked;
                _assistantStatus.Text = _harnessToggle.Checked ? "Attunement active. Tonal diagnostics accompany transmissions." : "Attunement idle.";
            };
            header.Controls.Add(_harnessToggle, 2, 0);

            _writePermsToggle = new DwemerToggleControl("Allow Tuning", CWarn, _scale, AppConfig.Instance.FissalHarnessEnabled && AppConfig.Instance.FissalWritePermissions);
            _writePermsToggle.Enabled = AppConfig.Instance.FissalHarnessEnabled;
            _writePermsToggle.Anchor = AnchorStyles.Right;
            _writePermsToggle.Margin = new Padding(0);
            _writePermsToggle.CheckedChanged += (_, _) =>
            {
                AppConfig.Instance.FissalWritePermissions = _writePermsToggle.Checked;
                AppConfig.Instance.Save();
            };
            header.Controls.Add(_writePermsToggle, 3, 0);

            layout.Controls.Add(header, 0, 0);

            // Quick Actions
            var quickActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.FromArgb(16, 13, 8),
                Padding = new Padding((int)(8 * _scale), (int)(5 * _scale), (int)(8 * _scale), (int)(5 * _scale)),
            };
            AddChatChip(quickActions, "📜", "Check Sales Files", "Check whether the Relay can see my ESO data files and explain anything missing.");
            AddChatChip(quickActions, "⏳", "Why is sync idle?", "Review my Relay state and tell me why no files may be syncing.");
            AddChatChip(quickActions, "🔍", "Explain recent logs", "Summarize my recent Relay sync activity and call out failures or stale data.");
            AddChatChip(quickActions, "🧹", "Clear chat", () =>
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
                Padding = new Padding((int)(12 * _scale)),
            };
            _transcript.Resize += (_, _) => ResizeAssistantCards();
            _transcript.HandleCreated += (_, _) => ApplyDarkModeScrollbars();
            layout.Controls.Add(_transcript, 0, 2);

            // Composer
            var composer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = CPanelBg,
                Padding = new Padding((int)(8 * _scale)),
            };
            composer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(28 * _scale))); // Prompt glyph
            composer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));                  // Input text box
            composer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(135 * _scale))); // Transmit button

            var promptMarker = new Label
            {
                Text = "❯",
                ForeColor = CGreen,
                Font = Title(13f, _scale, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
            };
            composer.Controls.Add(promptMarker, 0, 0);

            _prompt = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                AcceptsReturn = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(16, 14, 9),
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
            _prompt.HandleCreated += (_, _) => FissalTheme.ApplyDarkScrollbars(_prompt.Handle);
            composer.Controls.Add(_prompt, 1, 0);

            _send = MakeStyledButton("⚡ TRANSMIT", CGreen);
            _send.Dock = DockStyle.Fill;
            _send.Font = Title(10f, _scale, FontStyle.Bold);
            _send.Click += async (_, _) => await SendAssistantPromptAsync();
            composer.Controls.Add(_send, 2, 0);

            layout.Controls.Add(composer, 0, 3);

            // Status Bar
            var statusPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CPanelBg,
                Padding = new Padding((int)(8 * _scale), (int)(4 * _scale), (int)(8 * _scale), (int)(4 * _scale)),
            };
            _assistantStatus = new Label
            {
                Text = "● Attuned to 115.2 kHz • Press Enter to transmit (Shift+Enter for newline)",
                ForeColor = CTextSub,
                Font = Body(8f, _scale),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            statusPanel.Controls.Add(_assistantStatus);
            layout.Controls.Add(statusPanel, 0, 4);

            _assistantView.Controls.Add(layout);
        }

        private void AddChatChip(FlowLayoutPanel panel, string icon, string label, string prompt)
        {
            var btn = MakeStyledButton($"{icon} {label}", CGoldMid);
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

        private void AddChatChip(FlowLayoutPanel panel, string icon, string label, Action onClick)
        {
            var btn = MakeStyledButton($"{icon} {label}", CTextSub);
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
            _send.Text = "TUNING...";
            _assistantStatus.Text = "Fissal is analyzing the tonal harmonics...";

            try
            {
                var sb = new StringBuilder("Continue this Relay support conversation. Reply to the latest user message.\n");
                int start = Math.Max(0, _chatHistory.Count - 8);
                for (int i = start; i < _chatHistory.Count; i++)
                {
                    sb.Append(_chatHistory[i].role).Append(": ").AppendLine(_chatHistory[i].text);
                }

                if (_harnessToggle.Checked)
                {
                    sb.Append("\n\n[LOCAL RELAY HARNESS - diagnostics supplied with explicit user consent]\n")
                      .Append(_harnessService.DescribePermissions(_writePermsToggle.Checked)).Append("\n")
                      .Append(_watcher.GetAssistantContext());
                    if (_writePermsToggle.Checked)
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
                _send.Text = "⚡ TRANSMIT";
                _prompt.Focus();
            }
        }

        private string ProcessHarnessActionInReply(string response)
        {
            const string pattern = @"<fissal-action>(.*?)</fissal-action>";
            var match = Regex.Match(response, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!match.Success) return response;

            var visibleResponse = Regex.Replace(response, pattern, string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();
            if (!_harnessToggle.Checked || !_writePermsToggle.Checked)
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
            int availW = _transcript.ClientSize.Width > 100 ? _transcript.ClientSize.Width : (int)(680 * _scale);
            int indent = (int)(40 * _scale);
            int totalW = Math.Max(380, availW - (int)(28 * _scale));
            int cardW = totalW - indent;

            var card = new Panel
            {
                AutoSize = false,
                Width = cardW,
                BackColor = fromUser ? Color.FromArgb(28, 22, 14) : isError ? CErrBg : Color.FromArgb(14, 18, 14),
                Padding = new Padding((int)(12 * _scale)),
                Margin = new Padding(fromUser ? indent : 0, 0, fromUser ? 0 : indent, (int)(12 * _scale)),
                Tag = "chat-card",
            };

            card.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Color borderCol = fromUser ? CGoldDim : isError ? CErrBorder : CBorderSub;
                using var pen = new Pen(borderCol, 1f);
                g.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);

                // Small decorative corner jewel on Fissal cards
                if (!fromUser)
                {
                    using var jewelBrush = new SolidBrush(isError ? CBarFail : CGreen);
                    g.FillPolygon(jewelBrush, new[] {
                        new PointF(1, 1),
                        new PointF(12 * _scale, 1),
                        new PointF(1, 12 * _scale)
                    });
                }
                else
                {
                    using var jewelBrush = new SolidBrush(CGoldBrt);
                    g.FillPolygon(jewelBrush, new[] {
                        new PointF(card.Width - 1, 1),
                        new PointF(card.Width - (12 * _scale), 1),
                        new PointF(card.Width - 1, 12 * _scale)
                    });
                }
            };

            int innerW = cardW - card.Padding.Horizontal;

            // 1. Header (Sender label & Timestamp)
            var headerPanel = new TableLayoutPanel
            {
                Location = new Point(card.Padding.Left, card.Padding.Top),
                Width = innerW,
                Height = (int)(22 * _scale),
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Tag = "card-header",
            };
            headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var senderLabel = new Label
            {
                Text = fromUser ? "👤 YOU // TRADER CONSOLE" : isError ? "⚠️ FISSAL // SIGNAL ANOMALY" : "🐾 FISSAL // TONAL HARMONICS",
                ForeColor = fromUser ? CGoldBrt : isError ? CBarFail : CGreen,
                Font = Mono(8f, _scale, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            headerPanel.Controls.Add(senderLabel, 0, 0);

            var timeLabel = new Label
            {
                Text = DateTime.Now.ToString("h:mm tt"),
                ForeColor = CTextSub,
                Font = Mono(7.5f, _scale),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
            };
            headerPanel.Controls.Add(timeLabel, 1, 0);
            card.Controls.Add(headerPanel);

            // 2. Rich Text Box
            int textW = innerW - (int)(4 * _scale);
            var rtb = new RichTextBox
            {
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = card.BackColor,
                ForeColor = CText,
                Font = Body(9.5f, _scale),
                DetectUrls = true,
                ScrollBars = RichTextBoxScrollBars.None,
                TabStop = false,
                Location = new Point(card.Padding.Left, headerPanel.Bottom + (int)(5 * _scale)),
                Width = textW,
                Tag = "card-rtb",
            };
            FormatAssistantRichText(rtb, text);
            int textH = CalculateRichTextHeight(rtb, textW);
            rtb.Height = textH;

            rtb.LinkClicked += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.LinkText))
                    try { Process.Start(new ProcessStartInfo(e.LinkText) { UseShellExecute = true }); } catch { }
            };
            card.Controls.Add(rtb);

            // 3. Optional Copy Link
            LinkLabel? copyLink = null;
            if (!fromUser)
            {
                copyLink = new LinkLabel
                {
                    Text = "📋 Copy Transmission",
                    LinkColor = CTextSub,
                    ActiveLinkColor = CGoldBrt,
                    Font = Body(7.8f, _scale),
                    Location = new Point(card.Padding.Left, rtb.Bottom + (int)(6 * _scale)),
                    Width = innerW,
                    Height = (int)(20 * _scale),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Tag = "card-copy",
                };
                copyLink.LinkClicked += (_, _) =>
                {
                    try
                    {
                        Clipboard.SetText(text);
                        copyLink.Text = "✓ Transmission copied!";
                        copyLink.LinkColor = CGreen;
                        _assistantStatus.Text = "Transmission copied to clipboard.";
                    }
                    catch { _assistantStatus.Text = "Failed to copy transmission."; }
                };
                card.Controls.Add(copyLink);
            }

            int cardH = (copyLink != null ? copyLink.Bottom : rtb.Bottom) + card.Padding.Bottom + (int)(4 * _scale);
            card.Height = cardH;

            _transcript.Controls.Add(card);
            ResizeAssistantCards();
            _transcript.ScrollControlIntoView(card);
        }

        private void ResizeAssistantCards()
        {
            if (_transcript == null || _transcript.IsDisposed) return;
            int availW = _transcript.ClientSize.Width;
            if (availW <= 100) return;

            int indent = (int)(40 * _scale);
            int totalW = Math.Max(380, availW - (int)(28 * _scale));
            int cardW = totalW - indent;

            _transcript.SuspendLayout();

            foreach (Control ctrl in _transcript.Controls)
            {
                if (!Equals(ctrl.Tag, "chat-card") || ctrl is not Panel card) continue;

                card.Width = cardW;
                int innerW = cardW - card.Padding.Horizontal;

                TableLayoutPanel? header = null;
                RichTextBox? rtb = null;
                LinkLabel? copy = null;

                foreach (Control child in card.Controls)
                {
                    if (Equals(child.Tag, "card-header") && child is TableLayoutPanel t) header = t;
                    else if (Equals(child.Tag, "card-rtb") && child is RichTextBox r) rtb = r;
                    else if (Equals(child.Tag, "card-copy") && child is LinkLabel l) copy = l;
                }

                if (header != null)
                {
                    header.Width = innerW;
                }

                if (rtb != null)
                {
                    int textW = innerW - (int)(4 * _scale);
                    rtb.Width = textW;
                    int newH = CalculateRichTextHeight(rtb, textW);
                    rtb.Height = newH;
                    rtb.Location = new Point(card.Padding.Left, (header != null ? header.Bottom : card.Padding.Top) + (int)(5 * _scale));

                    if (copy != null)
                    {
                        copy.Width = innerW;
                        copy.Location = new Point(card.Padding.Left, rtb.Bottom + (int)(6 * _scale));
                    }

                    card.Height = (copy != null ? copy.Bottom : rtb.Bottom) + card.Padding.Bottom + (int)(4 * _scale);
                }
            }

            _transcript.ResumeLayout(true);
        }

        private int CalculateRichTextHeight(RichTextBox rtb, int width)
        {
            if (string.IsNullOrEmpty(rtb.Text)) return (int)(24 * _scale);

            // 1. GDI text measurement with ample line headroom
            var size = TextRenderer.MeasureText(
                rtb.Text + "\n\n ",
                rtb.Font,
                new Size(Math.Max(100, width - (int)(12 * _scale)), int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

            int gdiH = size.Height + (int)(18 * _scale);

            // 2. RichEdit native position check if handle is created
            int rtbH = 0;
            if (rtb.IsHandleCreated && rtb.TextLength > 0)
            {
                var pt = rtb.GetPositionFromCharIndex(rtb.TextLength - 1);
                rtbH = (int)(pt.Y + rtb.Font.GetHeight() * 1.8f + (14 * _scale));
            }

            return Math.Max(Math.Max((int)(24 * _scale), gdiH), rtbH);
        }

        private void FormatAssistantRichText(RichTextBox box, string raw)
        {
            string clean = Regex.Replace(raw ?? string.Empty, "(?m)^#{1,6}\\s+", string.Empty);
            clean = Regex.Replace(clean, "(?m)^[-*]\\s+", "• ");
            box.Text = clean;

            // Apply style passes
            ApplyStylePattern(box, @"\*\*(.+?)\*\*", FontStyle.Bold, CGoldBrt, removeMarker: true);
            ApplyStylePattern(box, @"\*([^*]+?)\*", FontStyle.Italic, Color.FromArgb(170, 210, 180), removeMarker: true);
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
                RowCount = 8,
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

            // Silent Background Sync
            formPanel.Controls.Add(MakeFieldLabel("Background Alerts:"), 0, 6);
            _btnSilentSync = MakeStyledButton("", CGreen);
            _btnSilentSync.AutoSize = true;
            _btnSilentSync.Click += (_, _) =>
            {
                var cfg = AppConfig.Instance;
                cfg.SilentSync = !cfg.SilentSync;
                cfg.Save();
                UpdateSilentSyncButton();
            };
            UpdateSilentSyncButton();
            formPanel.Controls.Add(_btnSilentSync, 1, 6);

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

            formPanel.Controls.Add(btnRow, 1, 7);

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
            UpdateSilentSyncButton();
        }

        private void UpdateSilentSyncButton()
        {
            if (_btnSilentSync == null || _btnSilentSync.IsDisposed) return;
            bool silent = AppConfig.Instance.SilentSync;
            _btnSilentSync.Text = silent ? "◆  SILENT BACKGROUND SYNC (MUTED)" : "◇  SILENT BACKGROUND SYNC (ALERTS ACTIVE)";
            _btnSilentSync.ForeColor = silent ? CGreen : CWarn;
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
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(92 * _scale)));

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
            _headerConsole.BackColor = CPanelBgAlt;
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
            _isConnected = ok;
            _titleStatusLabel.Text = ok ? "● CONNECTED TO CASTLE ECHO" : "⚠ DISCONNECTED";
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
        private Button MakeStyledButton(string text, Color accent)
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
            };
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
                BackColor = Color.FromArgb(12, 14, 18),
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
                if (_transcript != null && _transcript.IsHandleCreated)
                    FissalTheme.ApplyDarkScrollbars(_transcript.Handle);
                if (_prompt != null && _prompt.IsHandleCreated)
                    FissalTheme.ApplyDarkScrollbars(_prompt.Handle);
                if (_diagLogBox != null && _diagLogBox.IsHandleCreated)
                    FissalTheme.ApplyDarkScrollbars(_diagLogBox.Handle);
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
