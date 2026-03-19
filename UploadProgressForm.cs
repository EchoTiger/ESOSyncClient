using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using static RedfurSync.FissalTheme;

namespace RedfurSync
{
    public sealed class UploadProgressForm : Form
    {
        // ── Native magic to allow dragging borderless forms ──
        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        // ── Centralized Visual Configuration (Easy Maintenance) ──
        public static class AppConfig
        {
            public static int AnimIntervalMs = 51; 
            
            public const int MaxLogsKept = 8;
            public const int GlitchChancePer1k = 8;  
            public const int HeavyGlitchPct = 18;    
            public const float ScanlineSpeed = 0.56f;
            public const int MarqueePause = 48;     
            public const float ShimmerSpeed = 0.85f; 
            public const float ScrollFriction = 0.75f;
            public const float ScrollWheelSpeedMain = 0.90f;
            public const float ScrollWheelSpeedDiag = 0.90f;
            public const float CopyBubbleFloatSpeed = 1.45f; 
            public const float CopyBubbleFadeSpeed = 4.8f;   

            // --- Fissal Additions: UI Tweaks & Adjustments ---
            public const int BaseGroupHeaderH = 36;     // Reduced padding for Log/Update headers (Was 40)
            public const int BaseGroupHeaderPad = 1;    // Padding between header and first job child
            
            public const int DiagScrollWidth = 10;      // Width of the Diag scrollbar
            public const int DiagScrollRightOffset = 5; // Moves Diag scrollbar left away from edge
            public const int DiagScrollTopPad = 5;      // Stretches scrollbar upward
            public const int DiagScrollBottomPad = 5;   // Stretches scrollbar downward
            // -------------------------------------------------
            
            public enum FidelityMode { Low, Medium, High }
            public static FidelityMode CurrentMode = FidelityMode.Medium;

            public static void SetMode(FidelityMode mode)
            {
                CurrentMode = mode;
                
                if (mode == FidelityMode.Low)
                {
                    AnimIntervalMs = 68;
                    SetAll(false);
                }
                else if (mode == FidelityMode.High)
                {
                    AnimIntervalMs = 45;
                    SetAll(true);
                }
                else 
                {
                    AnimIntervalMs = 51;
                    FX.ScreenScanlines   = false;
                    FX.ScreenVignette    = false;
                    FX.ScreenGlassShine  = false;
                    FX.ScreenHeavyGlitch = false;
                    FX.BezelShadow       = false;
                    FX.EtchedRivets      = true;
                    FX.HeaderSocketGlow  = true;
                    FX.HeaderGlassGlint  = true;
                    FX.HeaderNeonText    = true;
                    FX.MCLightsGlow      = true;
                    FX.MCScanlines       = false;
                    FX.MCGloss           = true;
                    FX.CloseBtnDome      = true;
                    FX.LogHeaderPulse    = true;
                    FX.UpdateReadyPulse  = true;
                    FX.GroupSepScanlines = false;
                    FX.RowBadgeGlow      = false;
                    FX.RowBadgeScanlines = false;
                    FX.DrawerShadow      = false;
                    FX.ConnectionGradients=true;
                    FX.ButtonGlows       = true;
                    FX.BarPulseQueued    = true;
                    FX.BarEnergyUpload   = true;
                    FX.BarStaticFailed   = true;
                    FX.BarCoolGlowDone   = false;
                    FX.TextHazyGlitch    = false;
                    FX.MarqueeTextAnim   = true;
                }
            }

            private static void SetAll(bool state)
            {
                FX.ScreenScanlines   = state;
                FX.ScreenVignette    = state;
                FX.ScreenGlassShine  = state;
                FX.ScreenHeavyGlitch = state;
                FX.BezelShadow       = state;
                FX.EtchedRivets      = state;
                FX.HeaderSocketGlow  = true;
                FX.HeaderGlassGlint  = state;
                FX.HeaderNeonText    = state;
                FX.MCLightsGlow      = true;
                FX.MCScanlines       = state;
                FX.MCGloss           = state;
                FX.CloseBtnDome      = state;
                FX.LogHeaderPulse    = state;
                FX.UpdateReadyPulse  = state;
                FX.GroupSepScanlines = state;
                FX.RowBadgeGlow      = state;
                FX.RowBadgeScanlines = state;
                FX.DrawerShadow      = state;
                FX.ConnectionGradients=state;
                FX.ButtonGlows       = state;
                FX.BarPulseQueued    = state;
                FX.BarEnergyUpload   = state;
                FX.BarStaticFailed   = state;
                FX.BarCoolGlowDone   = state;
                FX.TextHazyGlitch    = state;
                FX.MarqueeTextAnim   = true;
            }

            public static class FX
            {
                public static bool ScreenScanlines   = false; 
                public static bool ScreenVignette    = false; 
                public static bool ScreenGlassShine  = false; 
                public static bool ScreenHeavyGlitch = false; 
                public static bool BezelShadow       = false; 
                public static bool EtchedRivets      = true; 
                public static bool HeaderSocketGlow  = true;  
                public static bool HeaderGlassGlint  = true;  
                public static bool HeaderNeonText    = true;  
                public static bool MCLightsGlow      = true;  
                public static bool MCScanlines       = false; 
                public static bool MCGloss           = true; 
                public static bool CloseBtnDome      = true;  
                public static bool LogHeaderPulse    = true; 
                public static bool UpdateReadyPulse  = true;  
                public static bool GroupSepScanlines = false; 
                public static bool RowBadgeGlow      = false; 
                public static bool RowBadgeScanlines = false; 
                public static bool DrawerShadow      = false; 
                public static bool ConnectionGradients=true; 
                public static bool ButtonGlows       = true;  
                public static bool BarPulseQueued    = true;  
                public static bool BarEnergyUpload   = true;  
                public static bool BarStaticFailed   = true;  
                public static bool BarCoolGlowDone   = false; 
                public static bool TextHazyGlitch    = false;  
                public static bool MarqueeTextAnim   = true;  
            }
        }

        private const int BaseW       = 340; 
        private const int BaseHeaderH = 60; 
        private const int BaseRowH    = 75;  
        private const int BaseExpandH = 125;  
        private const int BaseBarH    = 7;  
        private const int BasePad     = 12; 
        private const int BaseBtnW    = 50;   
        private const int BaseBtnH    = 20;   
        private const int BaseDiagH   = 20;   
        private const int BaseEmptyH  = 90;   
        private const int MaxRows     = 4;
        private float _emptyStateAlpha = 0f;
        private readonly float _scale;
        private int FormW, HeaderH, RowH, ExpandH, BarH, Pad, BtnW, BtnH, DiagH, EmptyH;

        private readonly ObservableCollection<UploadJob> _jobs;
        private readonly Action<UploadJob> _onRetry;
        private readonly Action<UploadJob> _onCancel;
        private readonly System.Windows.Forms.Timer _anim;

        private readonly HashSet<string> _expandedLogs = new();
        private readonly Dictionary<int, float> _diagScrolls = new(); 
        private readonly Dictionary<int, float> _diagMaxScrolls = new(); 
        private readonly Dictionary<string, string> _truncCache = new(); 
        private readonly Dictionary<DateTime, string> _phantomHeaders = new();

        private SolidBrush _bgBrush;
        private Font _fTitle28Bold, _fTitle125Bold, _fLogFont, _fTitle10Bold, _fTitle95;
        private Font _fBody95Italic, _fBody9Bold, _fBody8Bold, _fBody8Italic, _fBody8Reg, _fBody75Bold, _fBody75Italic, _fBody75Reg, _fBody7Bold;
        private Font _fMono9Bold, _fMono9, _fMono8Bold, _fMono8, _fMono75Bold;
        private StringFormat _sfCenter, _sfLeft;

        private int   _hoverLayoutIdx      = -1;   
        private int   _hoverDeleteGroupIdx = -1;   
        private bool  _hoverClose;
        private int   _hoverCopyJobIdx     = -1;
        private int   _hoverDiagJobIdx     = -1;
        private int   _hoverTrashJobIdx    = -1;
        
        private bool  _isDraggingMainScroll;
        private float _dragStartY;
        private float _dragStartScrollY;
        private int   _isDraggingDiagIdx = -1;

        private float _shimmer, _scanPhase, _globalGlitchX, _globalGlitchY, _marqueeX;
        private bool  _heavyGlitch;
        private int   _marqueeWait   = AppConfig.MarqueePause;

        private string? _purgingGroupText = null;
        private UploadJob? _purgingJobRef = null; 
        
        private int _purgeAnimFrames = 10;
        private const int MaxPurgeFrames = 10; 
        private const int MaxJobPurgeFrames = 4;
        
        private float _slideOffset = 0f;
        private int _slideStartY = 0;

        private enum DisplayState { Glitching, HoldStart, Scrolling, HoldEnd }
        private DisplayState _dispState = DisplayState.HoldStart;
        private int _dispWait = AppConfig.MarqueePause, _dispStatusIdx = 0;
        private float _spinPhase = 0f;

        private float _scrollY = 0f, _targetScrollY = 0f;
        private int   _contentHeight = 0;
        private readonly List<RowLayout> _layout = new();
        private bool _layoutNeedsUpdate = true;
        private bool _userHasResized = false;
        private bool _isResizing = false;
        private readonly Random _rand = new();
        private System.Windows.Forms.Timer? _phantomTimer;

        private struct RowLayout
        {
            public bool   IsSeparator;
            public string SepText;
            public string GroupText; 
            public int    JobIndex;
            public int    Y, Height;
            public bool   IsLastChild, GroupIsExpanded, IsUpdateGroup;
            public int    GroupTotalHeight; 
            public Color  GroupColor; 
            public int    ExpandedHeight;
            public bool   GroupHasResend; 
        }

        private struct CopyBubble { public float X, Y, Alpha; public Color ThemeColor; }
        private readonly List<CopyBubble> _copyBubbles = new();

        private int   _glowAlpha = 80, _glowStep = 4;
        private Color _coreColor = CGreen, _auraColor = Color.FromArgb(240, 150, 40);
        private readonly Action<UploadJob> _onApply;
        
        private Rectangle CloseBtnRect => new Rectangle(Width - Pad - S(20), (HeaderH - S(20)) / 2, S(25), S(25));
        
        private int RightGutterW => S(3);
        private int WorkingAreaW => Width - RightGutterW;
        private int RightBtnX => WorkingAreaW - Pad - BtnW;

        public UploadProgressForm(
            ObservableCollection<UploadJob> jobs, Action<UploadJob> onRetry, Action<UploadJob> onCancel, Action<UploadJob> onApply)
        {
            _jobs = jobs; _onRetry = onRetry; _onCancel = onCancel; _onApply = onApply;
            _jobs.CollectionChanged += (s, e) => { _layoutNeedsUpdate = true; };

            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; TopMost = true; BackColor = CBg; DoubleBuffered = true;
            StartPosition = FormStartPosition.Manual;

            _scale = GetScale(Handle); ApplyScale(); InitializeGdiCache();
            Width = FormW;

            _anim = new System.Windows.Forms.Timer { Interval = AppConfig.AnimIntervalMs };
            _anim.Tick += OnAnimationTick; _anim.Start();

            MouseDown += OnMouseDown; MouseClick += OnClick;
            MouseMove += OnMove; MouseLeave += (_, _) => ResetHoverStates();
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int WM_NCLBUTTONDOWN = 0x00A1;
            const int WM_ENTERSIZEMOVE = 0x0231;
            const int WM_EXITSIZEMOVE = 0x0232;

            if (m.Msg == WM_ENTERSIZEMOVE) _isResizing = true;
            if (m.Msg == WM_EXITSIZEMOVE) { _isResizing = false; CalculateFormBounds(); Invalidate(); }
            const int HTBOTTOMRIGHT = 17;
            const int HTBOTTOM = 15;
            const int HTRIGHT = 11;
            const int RESIZE_HANDLE_SIZE = 12;

            base.WndProc(ref m);

            if (m.Msg == WM_NCHITTEST)
            {
                Point screenPoint = new Point(m.LParam.ToInt32());
                Point clientPoint = this.PointToClient(screenPoint);

                if (clientPoint.X >= this.ClientSize.Width - RESIZE_HANDLE_SIZE && clientPoint.Y >= this.ClientSize.Height - RESIZE_HANDLE_SIZE)
                    m.Result = (IntPtr)HTBOTTOMRIGHT;
                else if (clientPoint.X >= this.ClientSize.Width - RESIZE_HANDLE_SIZE)
                    m.Result = (IntPtr)HTRIGHT;
                else if (clientPoint.Y >= this.ClientSize.Height - RESIZE_HANDLE_SIZE)
                    m.Result = (IntPtr)HTBOTTOM;
            }
            else if (m.Msg == WM_NCLBUTTONDOWN)
            {
                // The moment you grab the edge to resize, Fissal submits to your manual sizing
                int hitTest = m.WParam.ToInt32();
                if (hitTest == HTBOTTOMRIGHT || hitTest == HTRIGHT || hitTest == HTBOTTOM)
                {
                    _userHasResized = true;
                }
            }
        }

        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; } }

        public void ApplyAnimationInterval()
        {
            if (_anim != null)
                _anim.Interval = AppConfig.AnimIntervalMs;
        }

        private void InitializeGdiCache()
        {
            _bgBrush = new SolidBrush(CBg);
            _fTitle28Bold = Title(28f, _scale, FontStyle.Bold); _fTitle125Bold = Title(12.5f, _scale, FontStyle.Bold);
            _fTitle10Bold = Title(10f, _scale, FontStyle.Bold); _fTitle95 = Title(9.5f, _scale);
        
            _fBody95Italic = Body(9.5f, _scale, FontStyle.Italic); _fBody9Bold = Body(9f, _scale, FontStyle.Bold);
            _fBody8Bold = Body(8f, _scale, FontStyle.Bold); _fBody8Reg = Body(8f, _scale, FontStyle.Regular);
            _fBody8Italic = Body(8f, _scale, FontStyle.Italic); _fBody75Bold = Body(7.5f, _scale, FontStyle.Bold);
            _fBody75Italic = Body(7.5f, _scale, FontStyle.Italic); _fBody75Reg = Body(7.5f, _scale, FontStyle.Regular);
            _fBody7Bold = Body(7f, _scale, FontStyle.Bold);
            
            _fLogFont = Mono(10f, _scale, FontStyle.Regular);
            _fMono9Bold = Mono(9f, _scale, FontStyle.Bold); _fMono9 = Mono(9f, _scale);
            _fMono8Bold = Mono(8f, _scale, FontStyle.Bold); _fMono8 = Mono(8f, _scale); _fMono75Bold = Mono(7.5f, _scale, FontStyle.Bold);

            _sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.None, FormatFlags = StringFormatFlags.NoClip | StringFormatFlags.NoWrap };
            _sfLeft = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
        }

        private void ResetHoverStates()
        {
            _hoverLayoutIdx = -1; _hoverDeleteGroupIdx = -1; _hoverClose = false;
            _hoverCopyJobIdx = -1; _hoverDiagJobIdx = -1; _hoverTrashJobIdx = -1; Invalidate();
        }

        private int S(int v) => (int)Math.Round(v * _scale);

        private void ApplyScale()
        {
            FormW = S(BaseW); HeaderH = S(BaseHeaderH); RowH = S(BaseRowH); ExpandH = S(BaseExpandH);
            BarH = S(BaseBarH); Pad = S(BasePad); BtnW = S(BaseBtnW); BtnH = S(BaseBtnH);
            DiagH = S(BaseDiagH); EmptyH = S(BaseEmptyH);
            MinimumSize = new Size(S(340), HeaderH + S(AppConfig.BaseGroupHeaderH));
        }

        public void PositionAboveTray()
        {
            EnsureLayoutUpdated();
            var wa = Screen.PrimaryScreen?.WorkingArea ?? Screen.AllScreens[0].WorkingArea;
            Location = new Point(wa.Right - FormW - S(10), wa.Bottom - Height - S(10));
        }

        private void SafeRemoveJob(UploadJob jobToRemove)
        {
            string targetGroupText = null;
            var peerJobs = new List<UploadJob>();
            
            DateTime? currentGroupAnchor = null;
            string currentGroupText = "";
            bool? lastWasUpdate = null;
            
            foreach(var job in _jobs) {
                bool isNewGroup = currentGroupAnchor == null
                                || job.QueuedAt.Date != currentGroupAnchor.Value.Date // [Req 4]
                                || (lastWasUpdate.HasValue && lastWasUpdate.Value != job.IsUpdate);
                
                if (isNewGroup) {
                    currentGroupAnchor = job.QueuedAt;
                    currentGroupText = job.QueuedAt.ToString("MMM dd, yyyy").ToUpper(); // [Req 4]
                }
                if (job == jobToRemove) targetGroupText = currentGroupText;
                lastWasUpdate = job.IsUpdate;
            }

            bool wasExpanded = targetGroupText != null && _expandedLogs.Contains(targetGroupText);

            currentGroupAnchor = null;
            foreach(var job in _jobs) {
                bool isNewGroup = currentGroupAnchor == null
                                || job.QueuedAt.Date != currentGroupAnchor.Value.Date
                                || (lastWasUpdate.HasValue && lastWasUpdate.Value != job.IsUpdate);
                if (isNewGroup) {
                    currentGroupAnchor = job.QueuedAt;
                    currentGroupText = job.QueuedAt.ToString("MMM dd, yyyy").ToUpper();
                }
                if (currentGroupText == targetGroupText && job != jobToRemove) {
                    peerJobs.Add(job);
                }
                lastWasUpdate = job.IsUpdate;
            }

            _jobs.Remove(jobToRemove);

            if (wasExpanded && peerJobs.Count > 0) {
                currentGroupAnchor = null;
                string newGroupText = null;
                foreach(var job in _jobs) {
                    bool isNewGroup = currentGroupAnchor == null
                                    || job.QueuedAt.Date != currentGroupAnchor.Value.Date
                                    || (lastWasUpdate.HasValue && lastWasUpdate.Value != job.IsUpdate);
                    if (isNewGroup) {
                        currentGroupAnchor = job.QueuedAt;
                        currentGroupText = job.QueuedAt.ToString("MMM dd, yyyy").ToUpper();
                    }
                    if (job == peerJobs[0]) {
                        newGroupText = currentGroupText;
                        break;
                    }
                    lastWasUpdate = job.IsUpdate;
                }
                if (newGroupText != null) _expandedLogs.Add(newGroupText);
            }
            
            _layoutNeedsUpdate = true;
            EnsureLayoutUpdated();
        }

        private void OnAnimationTick(object? sender, EventArgs e)
        {
            bool visualChanged = ProcessVisualMath();
            bool scrollChanged = ProcessScrollPhysics();
            bool colorChanged = ProcessColorPulse();

            if (_slideOffset > 0)
            {
                _slideOffset = Math.Max(0, _slideOffset - Math.Max(S(15), _slideOffset * 0.15f));
                CalculateFormBounds(); 
                visualChanged = true;
            }

            if (_purgeAnimFrames > 0)
            {
                _purgeAnimFrames--;
                if (_purgeAnimFrames == 0)
                {
                    if (_purgingJobRef != null)
                    {
                        var job = _purgingJobRef;
                        int startY = -1;
                        int purgedHeight = 0;
                        int foundIndex = _jobs.IndexOf(job);
                        
                        foreach(var r in _layout) {
                            if (!r.IsSeparator && r.JobIndex == foundIndex) {
                                startY = r.Y; purgedHeight = r.Height + S(12); break;
                            }
                        }
                        _slideStartY = startY; _slideOffset = purgedHeight;
                        
                        // Purge the entire sync batch, not just the anchor file
                        var batchJobs = _jobs.Where(j => Math.Abs((j.QueuedAt - job.QueuedAt).TotalSeconds) <= 60 && j.IsUpdate == job.IsUpdate).ToList();
                        foreach(var j in batchJobs) {
                            if (j.CanCancel && (j.Status == UploadStatus.Uploading || j.Status == UploadStatus.Queued)) 
                                _onCancel(j);
                            _jobs.Remove(j);
                        }
                        
                        _purgingJobRef = null;
                        _layoutNeedsUpdate = true;
                        EnsureLayoutUpdated();
                    }
                    else if (_purgingGroupText != null)
                    {
                        int totalPurgedHeight = 0;
                        int startY = -1;
                        foreach(var r in _layout) {
                            if (r.GroupText == _purgingGroupText) {
                                if (startY == -1) startY = r.Y;
                                totalPurgedHeight += r.Height + (r.IsSeparator ? S(2) : S(12));
                            }
                        }
                        _slideStartY = startY;
                        _slideOffset = totalPurgedHeight;
                        ExecutePurge(_purgingGroupText);
                        _purgingGroupText = null;
                    }
                }
                visualChanged = true;
            }
            
            if (visualChanged || scrollChanged || colorChanged)
            {
                Invalidate();
            }
        }

        private void ExecutePurge(string groupText)
        {
            var jobsToDelete = new List<UploadJob>();
            DateTime? currentGroupAnchor = null;
            string currentGroupText = "";
            bool? lastWasUpdate = null;
            
            foreach(var thisJob in _jobs) 
            {
                bool isNewGroup = currentGroupAnchor == null
                                || thisJob.QueuedAt.Date != currentGroupAnchor.Value.Date
                                || (lastWasUpdate.HasValue && lastWasUpdate.Value != thisJob.IsUpdate);
                
                if (isNewGroup) 
                {
                    currentGroupAnchor = thisJob.QueuedAt;
                    currentGroupText = thisJob.QueuedAt.ToString("MMM dd, yyyy").ToUpper(); // Fixed format to match the Day headers
                }
                
                if (currentGroupText == groupText) jobsToDelete.Add(thisJob);
                lastWasUpdate = thisJob.IsUpdate;
            }
            
            foreach(var j in jobsToDelete) 
            {
                if (j.Status == UploadStatus.Uploading || j.Status == UploadStatus.Queued)
                {
                    if (j.CanCancel) _onCancel(j);
                }
                _jobs.Remove(j);
            }

            _expandedLogs.Remove(groupText);
            _layoutNeedsUpdate = true; EnsureLayoutUpdated();
        }
        
        private bool ProcessVisualMath()
        {
            _shimmer = (_shimmer + AppConfig.ShimmerSpeed) % 500f;
            
            if (AppConfig.FX.ScreenScanlines || AppConfig.FX.MCScanlines || AppConfig.FX.GroupSepScanlines || AppConfig.FX.RowBadgeScanlines)
                _scanPhase = (_scanPhase + AppConfig.ScanlineSpeed) % 4f;

            if (AppConfig.FX.ScreenHeavyGlitch && _rand.Next(1000) < AppConfig.GlitchChancePer1k)
            {
                _globalGlitchX = _rand.Next(-4, 5) * _scale;
                _globalGlitchY = _rand.Next(-2, 3) * _scale;
                _heavyGlitch   = _rand.Next(100) < AppConfig.HeavyGlitchPct;
            }
            else
            {
                _globalGlitchX = 0; _globalGlitchY = 0; _heavyGlitch = false;
            }

            if (AppConfig.FX.MCLightsGlow || AppConfig.FX.MarqueeTextAnim)
                _spinPhase += 0.15f; 

            bool visualChanged = _heavyGlitch; // Base invalidation trigger

            for (int i = _copyBubbles.Count - 1; i >= 0; i--)
            {
                var b = _copyBubbles[i];
                float velocityMulti = (b.Alpha / 255f) * 0.8f + 0.2f; 
                b.Y -= AppConfig.CopyBubbleFloatSpeed * velocityMulti;
                b.Alpha = Math.Max(0f, b.Alpha - AppConfig.CopyBubbleFadeSpeed);
                if (b.Alpha <= 0) _copyBubbles.RemoveAt(i);
                else _copyBubbles[i] = b;
                visualChanged = true;
            }

            if (_jobs.Count == 0) {
                if (_emptyStateAlpha < 255f) {
                    _emptyStateAlpha = Math.Min(255f, _emptyStateAlpha + 8f);
                    visualChanged = true;
                }
            } else {
                if (_emptyStateAlpha > 0f) {
                    _emptyStateAlpha = 0f;
                    visualChanged = true;
                }
            }

            // In High fidelity, always invalidate to maintain smooth continuous glows/scrolling.
            // In Low fidelity, only flag a visual change if dynamic physical animations are active.
            if (AppConfig.CurrentMode == AppConfig.FidelityMode.High) return true;
            return visualChanged;
        }

        private bool ProcessScrollPhysics()
        {
            float diff = _targetScrollY - _scrollY;
            if (Math.Abs(diff) > 0.5f) { _scrollY += diff * AppConfig.ScrollFriction; return true; }
            else if (Math.Abs(diff) > 0) { _scrollY = _targetScrollY; return true; }
            return false;
        }

        private bool ProcessColorPulse()
        {
            int prevAlpha = _glowAlpha;
            bool isUploading = false, hasError = false, hasUpdate = false;
            foreach (var j in _jobs)
            {
                if (j.Status == UploadStatus.Uploading) isUploading = true;
                if (j.Status == UploadStatus.Failed || j.Status == UploadStatus.Cancelled) hasError = true;
                if (j.Status == UploadStatus.UpdateReady) hasUpdate = true;
            }

            int targetStep = hasError ? 2 : isUploading ? 3 : hasUpdate ? 1 : 1;
            
// --- FISSAL's MOOD RING ---
            // _coreColor dictates the ambient gas cloud inside the tube.
            // _auraColor dictates the sharp, burning filament and outer flare.

            _coreColor = hasError    ? CBarFail                              // RED: An error has occurred
                       : isUploading ? Color.FromArgb(60, 180, 220)          // BLUE: Currently transmitting data
                       : hasUpdate   ? Color.FromArgb(180, 100, 220)         // PURPLE: A new update is ready
                       : Color.FromArgb(255, 200, 120);                      // WARM GOLD: Standby / All Jobs Done

            _auraColor = hasError    ? CBarFail                              // RED: An error has occurred
                       : isUploading ? Color.FromArgb(60, 180, 220)          // BLUE: Currently transmitting data
                       : hasUpdate   ? Color.FromArgb(200, 120, 240)         // PURPLE: A new update is ready
                       : Color.FromArgb(240, 150, 40);                       // BRIGHT ORANGE: Standby / All Jobs Done            _glowStep = _glowStep > 0 ? targetStep : -targetStep;
            
            _glowAlpha += _glowStep;
            if (_glowAlpha >= 240) { _glowAlpha = 240; _glowStep = -targetStep; }
            if (_glowAlpha <= 40) { _glowAlpha = 40; _glowStep = targetStep; }

            return prevAlpha != _glowAlpha;
        }

        private string GetDiagContent(UploadJob job, List<UploadJob> syncBatch = null)
        {
            if (syncBatch == null || syncBatch.Count <= 1)
            {
                return job.Status switch {
                    UploadStatus.Failed or UploadStatus.Cancelled => string.IsNullOrWhiteSpace(job.ErrorMessage) ? "No error detail was captured in this transmission." : job.ErrorMessage,
                    UploadStatus.Done => "[ OK ] Signal verified. No anomalies in the transmission log.", 
                    UploadStatus.Uploading => $"[ >> ] Active transmission in progress -- {job.Progress * 100:0}% complete.",
                    UploadStatus.Queued => "[ -- ] Awaiting open transmission slot. Standing by.", 
                    UploadStatus.UpdateReady => $"[ OK ] Matrix downloaded and verified. Fissal is ready to deplay this new module at your will!\n • Current Build : v{job.CurrentVersion}\n • Target Build  : v{job.UpdateVersion}\n • Payload Size  : {job.FileSizeDisplay}\n\n[ CHANGELOG ] :\n{job.Changelog}\n\n[ OK ] Ready for integration!",
                    _ => "[ ?? ] Signal state unknown."
                };
            }

            // Aggregate errors for copy-paste
            string output = $"--- SYNC DIAGNOSTICS ({syncBatch.Count} FILES) ---\n";
            foreach (var j in syncBatch)
            {
                if (j.Status is UploadStatus.Failed or UploadStatus.Cancelled)
                {
                    output += $"\n[X] {j.FileName}:\n    {(string.IsNullOrWhiteSpace(j.ErrorMessage) ? "Unknown Error" : j.ErrorMessage)}";
                }
            }
            if (!syncBatch.Any(j => j.Status is UploadStatus.Failed or UploadStatus.Cancelled))
            {
                output += "\n[ OK ] All signals in this batch verified. No anomalies.";
            }

            return output;
        }

private void EnsureLayoutUpdated()
        {
            if (!_layoutNeedsUpdate) return;

            _layout.Clear();
            int currentY = S(0); 

            var groupedJobs = new List<List<int>>();
            List<int>? currentGroup = null; 
            DateTime? groupStartTime = null; 
            bool? lastWasUpdate = null;

            for (int i = 0; i < _jobs.Count; i++)
            {
                var job = _jobs[i];
                bool isNewGroup = groupStartTime == null 
                        || job.QueuedAt.Date != groupStartTime.Value.Date // [Req 4] Group by Day
                        || (lastWasUpdate.HasValue && lastWasUpdate.Value != job.IsUpdate);
        
                if (isNewGroup || currentGroup == null) 
                { 
                    currentGroup = new List<int>(); 
                    groupedJobs.Add(currentGroup); 
                    groupStartTime = job.QueuedAt; 
                }
                currentGroup.Add(i);
                lastWasUpdate = job.IsUpdate;
                currentGroup.Sort((a, b) => {
                    var jobA = _jobs[a];
                    var jobB = _jobs[b];
                    
                    int timeCmp = jobB.QueuedAt.CompareTo(jobA.QueuedAt);
                    if (timeCmp != 0) return timeCmp;

                    string nameA = jobA.FileName ?? "";
                    string nameB = jobB.FileName ?? "";
                    
                    bool isGsA = nameA.StartsWith("GS") && nameA.Contains("Data");
                    bool isGsB = nameB.StartsWith("GS") && nameB.Contains("Data");
                    
                    if (!isGsA && isGsB) return -1;
                    if (isGsA && !isGsB) return 1;
                    
                    return string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
                });
            }

            groupedJobs.Reverse();

            var updateGroups = new List<List<int>>();
            var normalGroups = new List<List<int>>();

            foreach (var group in groupedJobs)
            {
                bool isUpdateGrp = false;
                foreach (int jIdx in group) if (_jobs[jIdx].IsUpdate) isUpdateGrp = true;
                if (isUpdateGrp) updateGroups.Add(group);
                else normalGroups.Add(group);
            }

            groupedJobs.Clear();
            groupedJobs.AddRange(updateGroups); 
            groupedJobs.AddRange(normalGroups);

            using var dummyG = Graphics.FromHwnd(IntPtr.Zero);

            for (int g = 0; g < groupedJobs.Count; g++)
            {
                var group = groupedJobs[g];
                if (group.Count == 0) continue;

                var groupAnchorJob = group.Select(idx => _jobs[idx]).OrderByDescending(j => j.QueuedAt).First();
                string currentGroupText = groupAnchorJob.QueuedAt.ToString("MMM dd, yyyy").ToUpper(); 
                bool isExpanded = _expandedLogs.Contains(currentGroupText);
                
                // Allow our Phantom Data to override the display text
                string displaySepText = _phantomHeaders.TryGetValue(groupAnchorJob.QueuedAt.Date, out string custom) ? custom : currentGroupText;

                bool isUpdateGrp = false;
                bool hasResend = false;
                Color grpCol = GetJobStatusColor(groupAnchorJob); 
                
                foreach (int jIdx in group) 
                {
                    if (_jobs[jIdx].IsUpdate) isUpdateGrp = true;
                    if (_jobs[jIdx].CanRetry) hasResend = true;
                }

                if (g > 0) currentY += S(20); 

                int headerHeight = S(AppConfig.BaseGroupHeaderH); 
                _layout.Add(new RowLayout
                {
                    IsSeparator = true, SepText = displaySepText, GroupText = currentGroupText, // Map the display text to SepText
                    Y = currentY, Height = headerHeight, GroupIsExpanded = isExpanded, IsUpdateGroup = isUpdateGrp,
                    GroupColor = grpCol, GroupHasResend = hasResend
                });
                int sepIndex = _layout.Count - 1;
                currentY += headerHeight + S(AppConfig.BaseGroupHeaderPad); 
                int groupStartY = currentY;

                if (isExpanded)
                {
                    // Sub-group files into "Syncs" (Files queued within 60 seconds of each other)
                    var syncBatches = new List<List<int>>();
                    List<int> currentSync = null;
                    DateTime? syncAnchor = null;

                    foreach (int jIdx in group)
                    {
                        var job = _jobs[jIdx];
                        if (syncAnchor == null || Math.Abs((job.QueuedAt - syncAnchor.Value).TotalSeconds) > 60 || job.IsUpdate)
                        {
                            currentSync = new List<int>();
                            syncBatches.Add(currentSync);
                            syncAnchor = job.QueuedAt;
                        }
                        currentSync.Add(jIdx);
                    }

                    for (int s = 0; s < syncBatches.Count; s++)
                    {
                        var syncBatch = syncBatches[s];
                        int firstJobIdx = syncBatch[0];
                        var anchorJob = _jobs[firstJobIdx];

                        int childW = WorkingAreaW - (Pad + S(18)) - Pad;
                        int calculatedExpandH = ExpandH;

                        if (anchorJob.IsExpanded)
                        {
                            // Calculate height needed for DIAG panel to fit all files in this sync
                            int filesHeight = syncBatch.Count * S(15); // Matched to actual drawing height
                            int errorsHeight = 0;
                            
                            foreach(var idx in syncBatch) {
                                if (!string.IsNullOrWhiteSpace(_jobs[idx].ErrorMessage)) {
                                    float textWNoScroll = childW - S(48);
                                    var sz = dummyG.MeasureString(_jobs[idx].ErrorMessage, _fBody75Reg, (int)textWNoScroll);
                                    errorsHeight += (int)sz.Height + S(10); // Matched to actual drawing padding
                                }
                            }

                            int neededH = filesHeight + errorsHeight + S(45);
                            calculatedExpandH = Math.Clamp(neededH, S(50), S(350)); // Allow larger diag panel
                        }

                        int h = RowH + (anchorJob.IsExpanded ? calculatedExpandH : 0);
                        bool isLast = (s == syncBatches.Count - 1);

                        // We store the FIRST job of the sync as the representative index for the row, 
                        // but we will draw all of them inside the DIAG panel.
                        _layout.Add(new RowLayout
                        {
                            IsSeparator = false, GroupText = currentGroupText, JobIndex = firstJobIdx,
                            Y = currentY, Height = h, IsLastChild = isLast,
                            ExpandedHeight = calculatedExpandH
                        });
                        currentY += h + S(12); 
                    }
                    var sepLayout = _layout[sepIndex];
                    sepLayout.GroupTotalHeight = currentY - groupStartY + S(4); 
                    _layout[sepIndex] = sepLayout;
                }
            }
            _contentHeight = currentY + S(10); 
            CalculateFormBounds(); _layoutNeedsUpdate = false;
        }

        private void CalculateFormBounds()
        {
            int effectiveContentH = _contentHeight + (int)_slideOffset;
            if (_jobs.Count == 0) effectiveContentH = Math.Max(EmptyH, effectiveContentH);
            
            if (!_userHasResized && !_isResizing)
            {
                // [Req 2] Added extra padding for group headers so the maximum size accommodates logs properly
                int maxContentH = (MaxRows * RowH) + S(AppConfig.BaseGroupHeaderH * 3); 
                int visibleContentH = Math.Min(effectiveContentH, maxContentH);
                int nh = HeaderH + visibleContentH + S(6);
                
                if (Height != nh) Height = nh; 
                if (Width != FormW) Width = FormW; 
            }

            // Always recalculate scroll bounds based on the *current* height
            int viewHeight = Height - HeaderH - S(6);
            float maxScroll = Math.Max(0, effectiveContentH - viewHeight);
            
            if (_targetScrollY > maxScroll) _targetScrollY = maxScroll;
            if (_scrollY > maxScroll) _scrollY = maxScroll;
        }

        // [Req 2] Ensure layout updates and wraps properly if UI is opened after logs were added invisibly
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) {
                _layoutNeedsUpdate = true;
                EnsureLayoutUpdated();
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Shift | Keys.P)) { InjectPhantomData(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private async void InjectPhantomData()
        {
            if (_phantomTimer != null)
            {
                _phantomTimer.Stop();
                _phantomTimer.Dispose();
                _phantomTimer = null;
            }

            _jobs.Clear(); 
            _expandedLogs.Clear(); 
            _truncCache.Clear(); 
            _phantomHeaders.Clear();

            DateTime baseDate = DateTime.Today;

            // 1. UPDATE (testver, simulated network fetch + 10s download)
            DateTime dUpdate = baseDate;
            _phantomHeaders[dUpdate.Date] = "[UPDATE SIMULATION]";
            var updateJob = new UploadJob { 
                FileName = "Fissal Matrix", IsUpdate = true, UpdateVersion = "testver", CurrentVersion = "1.0.0",
                Status = UploadStatus.Uploading, Progress = 0f, FileSizeBytes = 148500000, 
                QueuedAt = dUpdate.AddSeconds(-5), IsExpanded = true, Changelog = "Establishing secure link to server..." 
            };
            _jobs.Add(updateJob);
            _expandedLogs.Add(dUpdate.ToString("MMM dd, yyyy").ToUpper()); 

            // 2. LOG THAT'S DONE
            DateTime dDone = baseDate.AddDays(-1);
            _phantomHeaders[dDone.Date] = "[DONE]";
            _jobs.Add(new UploadJob { FileName = "GS01Data.lua", Status = UploadStatus.Done, Progress = 1f, QueuedAt = dDone.AddHours(-1) });
            _jobs.Add(new UploadJob { FileName = "GS02Data.lua", Status = UploadStatus.Done, Progress = 1f, QueuedAt = dDone.AddHours(-1).AddSeconds(5) });

            // 3. LOG THAT'S DONE AND ERRORED
            DateTime dDoneErr = baseDate.AddDays(-2);
            _phantomHeaders[dDoneErr.Date] = "[DONE] & [ERROR]";
            _jobs.Add(new UploadJob { FileName = "GS03Data.lua", Status = UploadStatus.Done, Progress = 1f, QueuedAt = dDoneErr.AddHours(-2) });
            _jobs.Add(new UploadJob { FileName = "GS04Data.lua", Status = UploadStatus.Failed, Progress = 0.8f, QueuedAt = dDoneErr.AddHours(-2).AddSeconds(5), ErrorMessage = "Lunar interference detected. Sequence out of bounds.", IsExpanded = true });
            _expandedLogs.Add(dDoneErr.ToString("MMM dd, yyyy").ToUpper());

            // 4. LOG THAT'S ERRORED
            DateTime dErr = baseDate.AddDays(-3);
            _phantomHeaders[dErr.Date] = "[ERROR]";
            _jobs.Add(new UploadJob { FileName = "GS05Data.lua", Status = UploadStatus.Failed, Progress = 0.2f, QueuedAt = dErr.AddHours(-3), ErrorMessage = "File locked by another process." });
            _jobs.Add(new UploadJob { FileName = "GS06Data.lua", Status = UploadStatus.Failed, Progress = 0.0f, QueuedAt = dErr.AddHours(-3).AddSeconds(5), ErrorMessage = "Network timeout." });

            // 5. LOG WITH DONE AND PENDING
            DateTime dDonePend = baseDate.AddDays(-4);
            _phantomHeaders[dDonePend.Date] = "[DONE] & [PENDING]";
            _jobs.Add(new UploadJob { FileName = "GS07Data.lua", Status = UploadStatus.Done, Progress = 1f, QueuedAt = dDonePend.AddHours(-4) });
            _jobs.Add(new UploadJob { FileName = "LargeTexturePack.zip", Status = UploadStatus.Queued, Progress = 0f, QueuedAt = dDonePend.AddHours(-4).AddSeconds(5) });

            // 6. LOG WITH VERY SLOW UPLOADS
            DateTime dSlow = baseDate.AddDays(-5);
            _phantomHeaders[dSlow.Date] = "[SLOW UPLOADS]";
            var slowJob1 = new UploadJob { FileName = "GS08Data.lua", Status = UploadStatus.Uploading, Progress = 0.1f, QueuedAt = dSlow.AddHours(-5) };
            var slowJob2 = new UploadJob { FileName = "GS09Data.lua", Status = UploadStatus.Uploading, Progress = 0.05f, QueuedAt = dSlow.AddHours(-5).AddSeconds(5) };
            _jobs.Add(slowJob1); _jobs.Add(slowJob2);

            // 7. LOG WITH VERY SLOW UPLOAD AND ONE IS ERRORED
            DateTime dSlowErr = baseDate.AddDays(-6);
            _phantomHeaders[dSlowErr.Date] = "[SLOW] & [ERROR]";
            var slowJob3 = new UploadJob { FileName = "GS10Data.lua", Status = UploadStatus.Uploading, Progress = 0.4f, QueuedAt = dSlowErr.AddHours(-6) };
            _jobs.Add(slowJob3);
            _jobs.Add(new UploadJob { FileName = "GS11Data.lua", Status = UploadStatus.Failed, Progress = 0.99f, QueuedAt = dSlowErr.AddHours(-6).AddSeconds(5), ErrorMessage = "Packet loss exceeded threshold." });
            _expandedLogs.Add(dSlowErr.ToString("MMM dd, yyyy").ToUpper());

            // 8. LOG THAT HAS INTERRUPTED JOBS
            DateTime dInt = baseDate.AddDays(-7);
            _phantomHeaders[dInt.Date] = "[INTERRUPTED]";
            _jobs.Add(new UploadJob { FileName = "GS12Data.lua", Status = UploadStatus.Cancelled, Progress = 0.5f, QueuedAt = dInt.AddHours(-7), ErrorMessage = "Sync manually aborted by user." });
            _jobs.Add(new UploadJob { FileName = "GS13Data.lua", Status = UploadStatus.Queued, Progress = 0f, QueuedAt = dInt.AddHours(-7).AddSeconds(5) });

            _layoutNeedsUpdate = true; 
            EnsureLayoutUpdated(); 

            // Asynchronous Network Simulation for Patch Notes
            try {
                using var client = new System.Net.Http.HttpClient();
                // To pull real data, swap the URL below with your actual API endpoint:
                // string jsonResponse = await client.GetStringAsync("https://your-api.com/updates/testver");
                
                await System.Threading.Tasks.Task.Delay(1000); // Prove the UI doesn't freeze during network wait
                updateJob.Changelog = "[ SERVER RESPONSE RECEIVED FOR TESTVER ]\n\n- Improved matrix synchronization stability.\n- Upgraded visual rendering engine.\n- Resolved packet fragmentation on large files.";
            } catch {
                updateJob.Changelog = "Failed to connect to server.\nUsing offline [testver] notes:\n- Adjusted orbital pathways.\n- Purred softly.";
            }

            _phantomTimer = new System.Windows.Forms.Timer { Interval = 150 };
            _phantomTimer.Tick += (s, e) => {
                bool redraw = false;
                
                // 10 second download at 150ms intervals (~66 ticks)
                if (updateJob.Status == UploadStatus.Uploading) {
                    updateJob.Progress += 0.015f;
                    if (updateJob.Progress >= 1f) { 
                        updateJob.Progress = 1f; 
                        updateJob.Status = UploadStatus.UpdateReady; 
                    }
                    redraw = true;
                }

                // Agonizingly slow uploads for UI testing
                if (slowJob1.Status == UploadStatus.Uploading) { slowJob1.Progress += 0.002f; if (slowJob1.Progress > 1f) slowJob1.Status = UploadStatus.Done; redraw = true; }
                if (slowJob2.Status == UploadStatus.Uploading) { slowJob2.Progress += 0.001f; if (slowJob2.Progress > 1f) slowJob2.Status = UploadStatus.Done; redraw = true; }
                if (slowJob3.Status == UploadStatus.Uploading) { slowJob3.Progress += 0.0015f; if (slowJob3.Progress > 1f) slowJob3.Status = UploadStatus.Done; redraw = true; }

                if (redraw) Invalidate();
            };
            
            _phantomTimer.Start();
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            float contentY = e.Y - HeaderH + _scrollY;
            bool handledInDiag = false;

            foreach (var row in _layout)
            {
                if (row.IsSeparator) continue;
                var job = _jobs[row.JobIndex];
                if (job.IsExpanded)
                {
                    int ey = row.Y + RowH;
                    if (contentY >= ey && contentY <= ey + row.ExpandedHeight)
                    {
                        float diagMaxScroll = _diagMaxScrolls.TryGetValue(row.JobIndex, out float ms) ? ms : 0f;
                        if (diagMaxScroll > 0)
                        {
                            float currentScroll = _diagScrolls.TryGetValue(row.JobIndex, out float s) ? s : 0f;
                            float newScroll = Math.Clamp(currentScroll - e.Delta * AppConfig.ScrollWheelSpeedDiag, 0, diagMaxScroll);
                            if (Math.Abs(newScroll - currentScroll) > 0.01f) { _diagScrolls[row.JobIndex] = newScroll; handledInDiag = true; Invalidate(); }
                        }
                        break; 
                    }
                }
            }

            if (!handledInDiag)
            {
                float maxScroll = Math.Max(0, _contentHeight + (int)_slideOffset - (Height - HeaderH - S(6)));
                if (maxScroll > 0)
                {
                    _targetScrollY -= e.Delta * AppConfig.ScrollWheelSpeedMain;
                    _targetScrollY = Math.Clamp(_targetScrollY, 0, maxScroll);
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            EnsureLayoutUpdated();
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit; g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            g.FillRectangle(_bgBrush, 0, 0, Width, Height);
            
            // [Req 6] Subtle retro terminal mesh texture
            using var meshPen = new Pen(Color.FromArgb(8, 255, 255, 255), 1f);
            for (int i = 0; i < Width; i += S(3)) g.DrawLine(meshPen, i, 0, i, Height);
            for (int j = 0; j < Height; j += S(3)) g.DrawLine(meshPen, 0, j, Width, j);

            // Screen scratches and smudges based on Fidelity ---
            if (AppConfig.CurrentMode != AppConfig.FidelityMode.Low)
            {
                int sAlpha = AppConfig.CurrentMode == AppConfig.FidelityMode.High ? 8 : 3;
                using var screenScratch = new Pen(Color.FromArgb(sAlpha, 255, 255, 255), 1f);
                g.DrawCurve(screenScratch, new PointF[] { new PointF(S(50), S(120)), new PointF(S(80), S(125)), new PointF(S(90), S(110)) });
                g.DrawLine(screenScratch, Width - S(60), Height - S(100), Width - S(40), Height - S(80));
                g.DrawLine(screenScratch, S(15), Height - S(50), S(35), Height - S(45));
                
                // Increased alpha slightly because the radial gradient softens it dramatically
                int sSmudgeAlpha = AppConfig.CurrentMode == AppConfig.FidelityMode.High ? 12 : 5;
                
                void DrawSoftSmudge(float sx, float sy, float sw, float sh) {
                    using var p = new GraphicsPath(); p.AddEllipse(sx, sy, sw, sh);
                    using var pgb = new PathGradientBrush(p) { 
                        CenterColor = Color.FromArgb(sSmudgeAlpha, 255, 255, 255), 
                        SurroundColors = new[] { Color.Transparent } 
                    };
                    g.FillPath(pgb, p);
                }

                DrawSoftSmudge(Width / 2 + S(40), HeaderH + S(50), S(120), S(80));
                DrawSoftSmudge(S(20), Height / 2, S(90), S(140));
            }

            DrawHeader(g);

            if (_jobs.Count == 0 && _slideOffset <= 0) { DrawEmpty(g); }
            else
            {
                int viewHeight = Height - HeaderH - S(6);
                var clipRect = new Rectangle(0, HeaderH, Width, viewHeight);
                g.SetClip(clipRect); 
                g.TranslateTransform(_globalGlitchX, HeaderH - _scrollY + _globalGlitchY);

                // PASS 1: Draw regular rows and the separator's background layer
                for (int i = 0; i < _layout.Count; i++)
                {
                    var row = _layout[i];
                    float effectiveHeight = row.IsSeparator && row.GroupIsExpanded ? row.Height + row.GroupTotalHeight : row.Height;
                    
                    if (row.Y + effectiveHeight < _scrollY - _slideOffset && !row.IsSeparator) continue;
                    if (row.Y > _scrollY + viewHeight) break;

                    var gState = g.Save();

                    bool isPurgingGroup = _purgingGroupText != null && row.GroupText == _purgingGroupText;
                    bool isPurgingJob = !row.IsSeparator && _jobs.Count > row.JobIndex && _jobs[row.JobIndex] == _purgingJobRef;
                    float purgeIntensity = 0f;
                    
                    if (isPurgingGroup || isPurgingJob)
                    {
                        int maxF = isPurgingGroup ? MaxPurgeFrames : MaxJobPurgeFrames;
                        float purgeRatio = _purgeAnimFrames / (float)maxF; 
                        purgeIntensity = 1f - purgeRatio; 
                        
                        float pGlitchX = _rand.Next(-S(3), S(3)) * purgeIntensity; 
                        float pGlitchY = _rand.Next(-S(1), S(1)) * purgeIntensity; 
                        
                        float scaleY = Math.Max(0.15f, purgeRatio);
                        float centerY = row.Y + (row.Height / 2f);
                        
                        g.TranslateTransform(pGlitchX, centerY + pGlitchY);
                        g.ScaleTransform(1f, scaleY);
                        g.TranslateTransform(0, -centerY);
                    }
                    else if (row.Y >= _slideStartY && _slideOffset > 0)
                    {
                        g.TranslateTransform(0, _slideOffset);
                    }

                    if (row.IsSeparator) DrawSeparatorBg(g, row, i, i == _hoverLayoutIdx);
                    else DrawRow(g, row, i, _jobs[row.JobIndex], row.JobIndex, row.Y);
                        
                    if (isPurgingGroup || isPurgingJob)
                    {
                        int itemX = row.IsSeparator ? Pad : Pad + S(18);
                        int itemW = row.IsSeparator ? WorkingAreaW - Pad * 2 : WorkingAreaW - (Pad + S(18)) - Pad;

                        int flashAlpha = Math.Clamp((int)(120 * purgeIntensity), 0, 255);
                        using var flashBrush = new SolidBrush(Color.FromArgb(flashAlpha, 200, 255, 255));
                        
                        g.FillRectangle(flashBrush, itemX - S(5), row.Y - S(5), itemW + S(10), row.Height + S(10));

                        using var cutPen = new Pen(Color.Cyan, S(1));
                        using var cutPen2 = new Pen(Color.Magenta, S(1));
                            
                        int linesToDraw = (int)(10 * purgeIntensity); 
                        for(int l = 0; l < linesToDraw; l++) {
                            int cy = row.Y + _rand.Next(row.Height);
                            int startX = itemX + _rand.Next(itemW - S(20)); 
                            int maxSafeLen = (itemX + itemW) - startX;
                            int lineLen = _rand.Next(S(10), Math.Min(maxSafeLen, itemW / 3)); 
                            g.DrawLine(l % 2 == 0 ? cutPen : cutPen2, startX, cy, startX + lineLen, cy);
                        }
                    }
                    g.Restore(gState);
                }

                // PASS 2: Draw the Sticky Headers over top of the rows
                for (int i = 0; i < _layout.Count; i++)
                {
                    var row = _layout[i];
                    if (!row.IsSeparator) continue;

                    float effectiveHeight = row.Height + (row.GroupIsExpanded ? row.GroupTotalHeight : 0);
                    if (row.Y + effectiveHeight < _scrollY - _slideOffset) continue;
                    if (row.Y > _scrollY + viewHeight) break;

                    var gState = g.Save();

                    int drawY = row.Y;
                    bool isSticky = false;
                    if (row.GroupIsExpanded)
                    {
                        int scrollTop = (int)_scrollY;
                        if (drawY < scrollTop && drawY + row.GroupTotalHeight - row.Height > scrollTop)
                        {
                            drawY = scrollTop;
                            isSticky = true;
                        }
                    }

                    // [Req 1] Faint background mask if sticky
                    if (isSticky)
                    {
                        using var stickyBg = new SolidBrush(Color.FromArgb(240, CBg));
                        g.FillRectangle(stickyBg, 0, drawY, Width, row.Height);
                        using var stickyShadow = new LinearGradientBrush(new Rectangle(0, drawY + row.Height, Width, S(6)), Color.FromArgb(100, 0, 0, 0), Color.Transparent, LinearGradientMode.Vertical);
                        g.FillRectangle(stickyShadow, 0, drawY + row.Height, Width, S(6));
                    }

                    bool isPurgingGroup = _purgingGroupText != null && row.GroupText == _purgingGroupText;
                    if (isPurgingGroup)
                    {
                        float purgeRatio = _purgeAnimFrames / (float)MaxPurgeFrames; 
                        float purgeIntensity = 1f - purgeRatio; 
                        
                        float pGlitchX = _rand.Next(-S(3), S(3)) * purgeIntensity; 
                        float pGlitchY = _rand.Next(-S(1), S(1)) * purgeIntensity; 
                        
                        float scaleY = Math.Max(0.15f, purgeRatio);
                        float centerY = drawY + (row.Height / 2f);
                        
                        g.TranslateTransform(pGlitchX, centerY + pGlitchY);
                        g.ScaleTransform(1f, scaleY);
                        g.TranslateTransform(0, -centerY);
                    }
                    else if (drawY >= _slideStartY && _slideOffset > 0)
                    {
                        g.TranslateTransform(0, _slideOffset);
                    }

                    var stickyRow = row;
                    stickyRow.Y = drawY;
                    DrawSeparatorHeader(g, stickyRow, i, i == _hoverLayoutIdx);
                    g.Restore(gState);
                }

                if (_heavyGlitch && AppConfig.FX.ScreenHeavyGlitch)
                {
                    int tearY = (int)(_scrollY + _rand.Next(viewHeight)); 
                    int tearH = _rand.Next(S(4), S(18));
                    using var tearBrush = new SolidBrush(Color.FromArgb(40, CText));
                    g.FillRectangle(tearBrush, 0, tearY, Width, tearH);
                }

                g.ResetTransform(); 
                
                if (AppConfig.FX.ScreenScanlines)
                {
                    using var screenScanPen = new Pen(Color.FromArgb(90, 10, 5, 2), 1.5f);
                    for (float sy = HeaderH + _scanPhase; sy < Height; sy += 4)
                        g.DrawLine(screenScanPen, 0, sy, Width, sy);
                }

                if (AppConfig.FX.ScreenVignette)
                {
                    using var vignettePath = new GraphicsPath();
                    vignettePath.AddRectangle(clipRect);
                    using var pthGrBrush = new PathGradientBrush(vignettePath)
                    {
                        CenterColor = Color.Transparent, SurroundColors = new[] { Color.FromArgb(190, 0, 0, 0) }, FocusScales = new PointF(0.65f, 0.80f)
                    };
                    g.FillRectangle(pthGrBrush, clipRect);
                }

                if (AppConfig.FX.ScreenGlassShine)
                {
                    using var glassHighlight = new LinearGradientBrush(clipRect, Color.FromArgb(32, 180, 215, 190), Color.Transparent, LinearGradientMode.ForwardDiagonal);
                    g.FillRectangle(glassHighlight, clipRect);
                }

                if (AppConfig.FX.BezelShadow)
                {
                    using var bezelShadowBrush = new LinearGradientBrush(new Rectangle(0, HeaderH, Width, S(16)), Color.FromArgb(200, 0, 0, 0), Color.Transparent, LinearGradientMode.Vertical);
                    g.FillRectangle(bezelShadowBrush, 0, HeaderH, Width, S(16));
                }

                g.ResetClip();
                if (_contentHeight + (int)_slideOffset > viewHeight) DrawScrollbar(g, clipRect);
            }
            DrawChrome(g); DrawCopyBubbles(g);
        }

        private void DrawChrome(Graphics g)
        {
            using var borderPen = new Pen(CBorder, S(1));
            g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            using var dimPen = new Pen(Color.FromArgb(35, CGoldDim), S(1));
            g.DrawRectangle(dimPen, S(3), S(3), Width - S(6), Height - S(6));
            if (AppConfig.FX.EtchedRivets) DrawEtchedRivets(g);
        }

        private void DrawEtchedRivets(Graphics g)
        {
            return; 
        }

        private void DrawGlowingText(Graphics g, string text, Font font, Color color, float x, float y, int glowAlpha = 40)
        {
            if (glowAlpha > 0)
            {
                using var glowBrush = new SolidBrush(Color.FromArgb(glowAlpha, color));
                g.DrawString(text, font, glowBrush, new PointF(x, y - S(1))); g.DrawString(text, font, glowBrush, new PointF(x, y + S(1)));
                g.DrawString(text, font, glowBrush, new PointF(x - S(1), y)); g.DrawString(text, font, glowBrush, new PointF(x + S(1), y));
            }
            using var coreBrush = new SolidBrush(color);
            g.DrawString(text, font, coreBrush, new PointF(x, y));
        }

        private void DrawGlowingTextRect(Graphics g, string text, Font font, Color color, RectangleF rect, StringFormat sf, int glowAlpha = 40)
        {
            if (glowAlpha > 0)
            {
                using var glowBrush = new SolidBrush(Color.FromArgb(glowAlpha, color));
                g.DrawString(text, font, glowBrush, new RectangleF(rect.X, rect.Y - S(1), rect.Width, rect.Height), sf); g.DrawString(text, font, glowBrush, new RectangleF(rect.X, rect.Y + S(1), rect.Width, rect.Height), sf);
                g.DrawString(text, font, glowBrush, new RectangleF(rect.X - S(1), rect.Y, rect.Width, rect.Height), sf); g.DrawString(text, font, glowBrush, new RectangleF(rect.X + S(1), rect.Y, rect.Width, rect.Height), sf);
            }
            using var coreBrush = new SolidBrush(color);
            g.DrawString(text, font, coreBrush, rect, sf);
        }

        private void DrawCopyBubbles(Graphics g)
        {
            if (_copyBubbles.Count == 0) return;
            const string gearPart = "⚙ ", textPart = "Copied!"; 
            var bf = _fBody8Bold; var totalSz = g.MeasureString(gearPart + textPart, bf);
            int bw = (int)totalSz.Width + S(14), bh = (int)totalSz.Height + S(6);

            foreach (var bubble in _copyBubbles)
            {
                int alpha = (int)bubble.Alpha;
                float bx = bubble.X - bw / 2f, by = bubble.Y - bh, innerX = bx + S(7);  
                using var bgPath = RoundRect(bx, by, bw, bh, S(5));
                using var bgBrush = new SolidBrush(Color.FromArgb(alpha, Color.FromArgb(16, 4, 28)));
                g.FillPath(bgBrush, bgPath);
                using var borderPen = new Pen(Color.FromArgb(alpha, bubble.ThemeColor), S(1));
                g.DrawPath(borderPen, bgPath);

                int glowAlpha = Math.Max(0, (int)(alpha * 0.32f));
                float gearWidth = g.MeasureString(gearPart, bf).Width;
                DrawGlowingTextRect(g, gearPart, bf, Color.FromArgb(alpha, CGoldBrt), new RectangleF(innerX, by, gearWidth, bh), _sfLeft, glowAlpha);
                DrawGlowingTextRect(g, textPart, bf, Color.FromArgb(alpha, bubble.ThemeColor), new RectangleF(innerX + gearWidth, by, bw, bh), _sfLeft, glowAlpha);
            }
        }

        private Rectangle DeleteBtnRect(RowLayout row)
        {
            int boxX = Pad, boxY = row.Y + S(4), boxW = WorkingAreaW - Pad * 2;
            int boxH = row.GroupIsExpanded ? row.Height - S(4) : row.Height - S(4);
            return new Rectangle(boxX + boxW - S(50) - S(8), boxY + (boxH - S(16)) / 2 - S(2), S(50), S(16));
        }

        private void DrawFadingGlow(Graphics g, float x, float y, float w, float h, Color baseColor, float penW, bool fadeBottom)
        {
            if (fadeBottom)
            {
                using var path = new GraphicsPath();
                path.AddLine(x, y + h, x, y);
                path.AddLine(x, y, x + w, y);
                path.AddLine(x + w, y, x + w, y + h);
                using var brush = new LinearGradientBrush(new RectangleF(x, y, Math.Max(1, w), Math.Max(1, h)), baseColor, Color.Transparent, LinearGradientMode.Vertical);
                using var pen = new Pen(brush, penW) { LineJoin = LineJoin.Miter };
                g.DrawPath(pen, path);
            }
            else
            {
                using var pen = new Pen(baseColor, penW); g.DrawRectangle(pen, x, y, w, h);
            }
        }

        private void DrawSeparatorBg(Graphics g, RowLayout row, int layoutIdx, bool isHovered)
        {
            bool isExpanded = row.GroupIsExpanded; 
            if (layoutIdx > 0)
            {
                int lineY = row.Y - S(10);
                if (AppConfig.FX.LogHeaderPulse) {
                    using var groupSepGlow = new Pen(Color.FromArgb(20, row.GroupColor), S(5)); 
                    g.DrawLine(groupSepGlow, Pad - S(15), lineY, WorkingAreaW - Pad + S(15), lineY);
                }
                using var groupSepLine = new Pen(Color.FromArgb(50, row.GroupColor.R/1, row.GroupColor.G/1, row.GroupColor.B/1), S(2)) { DashStyle = DashStyle.DashDotDot };
                g.DrawLine(groupSepLine, Pad + S(90), lineY, WorkingAreaW - Pad - S(90), lineY);
            }

            int boxX = Pad, boxY = row.Y + S(5), boxW = WorkingAreaW - Pad * 2, boxH = isExpanded ? row.Height - S(8) : row.Height - S(8);
            if (isExpanded && row.GroupTotalHeight > 0)
            {
                int bgX = boxX + S(10), bgY = boxY + boxH, bgW = boxW - S(16), bgH = row.GroupTotalHeight;
                Color treeColor = row.GroupColor;
                using var groupBgBrush = new SolidBrush(Color.FromArgb(30, treeColor.R, treeColor.G, treeColor.B));
                g.FillRectangle(groupBgBrush, bgX, bgY, bgW, bgH);
                using var groupBorderPen = new Pen(Color.FromArgb(100, treeColor.R, treeColor.G, treeColor.B), 2);
                g.DrawLine(groupBorderPen, bgX, bgY, bgX, bgY + bgH); g.DrawLine(groupBorderPen, bgX + bgW, bgY, bgX + bgW, bgY + bgH); g.DrawLine(groupBorderPen, bgX, bgY + bgH, bgX + bgW, bgY + bgH); 
            }
        }

        private void DrawSeparatorHeader(Graphics g, RowLayout row, int layoutIdx, bool isHovered)
        {
            bool isExpanded = row.GroupIsExpanded, delHover = _hoverDeleteGroupIdx == layoutIdx, textHover = isHovered && !delHover; 
            int boxX = Pad, boxY = row.Y + S(5), boxW = WorkingAreaW - Pad * 2, boxH = isExpanded ? row.Height - S(8) : row.Height - S(8);

            if (row.IsUpdateGroup && AppConfig.FX.UpdateReadyPulse) {
                int pulseAlpha = (int)(Math.Sin(_shimmer * 0.20f) * 100 + 100); 
                for (int i = 1; i <= 3; i++) DrawFadingGlow(g, boxX - S(1), boxY - S(1), boxW + S(2), boxH + S(2), Color.FromArgb(pulseAlpha / i, 180, 100, 240), S(i * 3), isExpanded);
            }
            else if (!row.IsUpdateGroup && AppConfig.FX.LogHeaderPulse) {
                int pulseAlpha = (int)(Math.Sin(_shimmer * 0.04f) * 20 + 50); 
                DrawFadingGlow(g, boxX - S(1), boxY - S(1), boxW + S(2), boxH + S(2), Color.FromArgb(textHover ? 90 : pulseAlpha, row.GroupColor), S(4), isExpanded);
            }
            
            // Sink the header background into a rich, deep shadow when hovered
            Color headerBg = textHover ? DarkenColor(row.GroupColor, 0.7f, LightFilter.Natural, 255) : Color.FromArgb(6, 6, 8);
            using var bgBrush = new SolidBrush(headerBg); g.FillRectangle(bgBrush, boxX, boxY, boxW, boxH);

            // [Req 1] Dynamic Header Progress Bar Priorities (Current Sync Only)
            float totalProg = 0f; int count = 0;
            bool hasError = false; bool hasPending = false; bool hasUpdateDownloading = false;
            bool hasUpdateReady = false; bool hasUploading = false; bool allDone = true;

            DateTime newestTime = DateTime.MinValue;
            bool newestIsUpdate = false;
            bool foundAny = false;

            // Safe index-based enumeration to avoid InvalidOperationException on background updates
            for (int k = 0; k < _jobs.Count; k++) {
                try {
                    var j = _jobs[k];
                    if (j.QueuedAt.ToString("MMM dd, yyyy").ToUpper() == row.GroupText) {
                        if (!foundAny || j.QueuedAt >= newestTime) {
                            newestTime = j.QueuedAt;
                            newestIsUpdate = j.IsUpdate;
                            foundAny = true;
                        }
                    }
                } catch { break; } // Failsafe if collection shrinks during iteration
            }

            if (foundAny)
            {
                for (int k = 0; k < _jobs.Count; k++) {
                    try {
                        var j = _jobs[k];
                        if (j.QueuedAt.ToString("MMM dd, yyyy").ToUpper() == row.GroupText) {
                            if (Math.Abs((j.QueuedAt - newestTime).TotalSeconds) <= 60 && j.IsUpdate == newestIsUpdate) {
                                totalProg += (j.Status == UploadStatus.Done || j.Status == UploadStatus.UpdateReady) ? 1f : j.Progress;
                                count++;
                                
                                if (j.Status == UploadStatus.Failed || j.Status == UploadStatus.Cancelled) hasError = true;
                                else if (j.Status == UploadStatus.Queued) hasPending = true;
                                else if (j.Status == UploadStatus.Uploading && j.IsUpdate) hasUpdateDownloading = true;
                                else if (j.Status == UploadStatus.Uploading && !j.IsUpdate) hasUploading = true;
                                else if (j.Status == UploadStatus.UpdateReady) hasUpdateReady = true;

                                if (j.Status != UploadStatus.Done && j.Status != UploadStatus.UpdateReady) allDone = false;
                            }
                        }
                    } catch { break; } 
                }
            }
            
            if (count > 0) {
                float avgProg = totalProg / count;
                Color progColor;
                
                // Priority: Error > Pending/Aborted > Update Ready > Update Downloading > Job Uploading > All Done
                if (hasError) progColor = Color.FromArgb(95, 200, 0, 0);
                else if (hasPending) progColor = Color.FromArgb(20, 180, 180, 180);
                else if (hasUpdateReady) progColor = Color.FromArgb(20, 180, 100, 220);
                else if (hasUpdateDownloading) progColor = Color.FromArgb(20, 180, 100, 220);
                else if (hasUploading) progColor = Color.FromArgb(20, CGoldBrt.R, CGoldBrt.G, CGoldBrt.B);
                else progColor = Color.FromArgb(20, CGreen.R, CGreen.G, CGreen.B);

                using var avgProgBrush = new SolidBrush(progColor);
                g.FillRectangle(avgProgBrush, boxX, boxY, boxW * Math.Max(0.05f, avgProg), boxH);
            }

            Color borderColor = row.IsUpdateGroup ? Color.FromArgb(textHover ? 220 : 140, 160, 80, 220) : Color.FromArgb(textHover ? 220 : 140, row.GroupColor);
            using var borderPen = new Pen(borderColor, 1.5f);
            g.DrawRectangle(borderPen, boxX, boxY, boxW, boxH); 

            var sf = _fLogFont;
            string text = row.IsUpdateGroup ? $"!! UPDATE READY" : $"{(isExpanded ? " -" : " + ")} LOG: {row.SepText}";
            Color mainColor = row.IsUpdateGroup ? (textHover ? Color.FromArgb(240, 180, 255) : Color.FromArgb(200, 130, 240)) : (textHover ? Color.White : CGoldBrt);
            
            var sz = g.MeasureString(text, sf);
            DrawGlowingText(g, text, sf, mainColor, boxX + S(3), boxY + (boxH - sz.Height) / 2f + S(1), AppConfig.FX.LogHeaderPulse || AppConfig.FX.UpdateReadyPulse ? (textHover ? 90 : 40) : 0);

            if (AppConfig.FX.GroupSepScanlines) {
                using var scanlinePen = new Pen(row.IsUpdateGroup ? Color.FromArgb(20, 180, 100, 220) : Color.FromArgb(15, CGreen), 1);
                for (int i = boxY + 2; i < boxY + boxH; i += 3) g.DrawLine(scanlinePen, boxX + 1, i, boxX + boxW - 1, i);
            }

            bool showResend = row.GroupHasResend;
            string btnText = showResend ? "[RE-SEND]" : "[PURGE]";
            Color btnBaseColor = showResend ? CGreen : CBarFail;

            var delRect = DeleteBtnRect(row);
            using var delPath = RoundRect(delRect.X, delRect.Y, delRect.Width, delRect.Height, S(2));
            using var delBg = new SolidBrush(delHover ? Color.FromArgb(40, btnBaseColor) : Color.Transparent); g.FillPath(delBg, delPath);
            using var delPen = new Pen(delHover ? Color.FromArgb(200, btnBaseColor) : Color.FromArgb(40, btnBaseColor), 1); g.DrawPath(delPen, delPath);
            using var delTextBrush = new SolidBrush(delHover ? Color.White : Color.FromArgb(200, btnBaseColor)); 
            g.DrawString(btnText, _fMono8, delTextBrush, delRect, _sfCenter);
        }
        private Color GetJobStatusColor(UploadJob j) => j.Status switch {
            UploadStatus.Queued => CTextSub, UploadStatus.Uploading => j.IsUpdate ? Color.FromArgb(180, 100, 220) : CGoldBrt,
            UploadStatus.Done => CGreen, UploadStatus.Failed => CBarFail, UploadStatus.Cancelled => CBarCancel,
            UploadStatus.UpdateReady => Color.FromArgb(180, 100, 220), _ => CTextSub
        };

        private void DrawRow(Graphics g, RowLayout rowInfo, int layoutIdx, UploadJob job, int idx, int y)
        {
            var syncBatch = new List<UploadJob>();
            for (int k = 0; k < _jobs.Count; k++) {
                try { var j = _jobs[k]; if (Math.Abs((j.QueuedAt - job.QueuedAt).TotalSeconds) <= 60 && j.IsUpdate == job.IsUpdate) syncBatch.Add(j); } catch { break; }
            }
            syncBatch.Sort((a, b) => {
                int GetStatusWeight(UploadStatus s) => s switch {
                    UploadStatus.Uploading => 0, UploadStatus.Failed => 1, UploadStatus.Cancelled => 1,
                    UploadStatus.Queued => 2, UploadStatus.Done => 3, UploadStatus.UpdateReady => 3, _ => 4
                };

                int statusA = GetStatusWeight(a.Status);
                int statusB = GetStatusWeight(b.Status);
                if (statusA != statusB) return statusA.CompareTo(statusB);

                string nameA = a.FileName ?? "", nameB = b.FileName ?? "";
                bool isGsA = nameA.StartsWith("GS", StringComparison.OrdinalIgnoreCase) && nameA.IndexOf("Data", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isGsB = nameB.StartsWith("GS", StringComparison.OrdinalIgnoreCase) && nameB.IndexOf("Data", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!isGsA && isGsB) return -1;
                if (isGsA && !isGsB) return 1;

                return string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
            });

            long totalBytes = syncBatch.Sum(j => j.FileSizeBytes);
            string totalSizeDisplay = totalBytes >= 1048576 ? $"{totalBytes / 1048576.0:0.0} MB" : totalBytes >= 1024 ? $"{totalBytes / 1024.0:0} KB" : $"{totalBytes} B";
            string syncTimeName = job.IsUpdate ? "UPDATE FISSAL" : $"SYNC> {job.QueuedAt:h:mm:ss tt}";
            
            int childPad = Pad + S(15), childW = WorkingAreaW - childPad - Pad, totalH = RowH + (job.IsExpanded ? rowInfo.ExpandedHeight : 0);

            bool batchUploading = syncBatch.Any(j => j.Status == UploadStatus.Uploading || j.Status == UploadStatus.Queued);
            bool batchHasError = syncBatch.Any(j => j.Status == UploadStatus.Failed || j.Status == UploadStatus.Cancelled);
            bool batchDone = syncBatch.Count > 0 && syncBatch.All(j => j.Status == UploadStatus.Done || j.Status == UploadStatus.UpdateReady);
            float batchAvgProgress = syncBatch.Count > 0 ? syncBatch.Average(j => (j.Status == UploadStatus.Done || j.Status == UploadStatus.UpdateReady) ? 1f : j.Progress) : 0f;

            (string glyph, Color gc) = ("??", CTextSub);
            if (job.IsUpdate) { glyph = job.Status == UploadStatus.UpdateReady ? "^" : "v"; gc = Color.FromArgb(180, 100, 220); }
            else if (batchHasError && batchUploading) { glyph = "!!"; gc = Color.FromArgb(255, 140, 40); }
            else if (batchHasError && !batchUploading) { glyph = "!!"; gc = CBarFail; }
            else if (batchDone) { glyph = "✓"; gc = CGreen; }
            else if (batchUploading) { glyph = syncBatch.Any(j => j.Status == UploadStatus.Uploading) ? ">>" : "--"; gc = CGoldBrt; }

            int lineX = Pad, midY = y + S(14), prevMidY = y - S(20); 
            Color prevColor = rowInfo.GroupColor;
            for(int k = layoutIdx - 1; k >= 0; k--) {
                if (_layout[k].IsSeparator) { prevMidY = _layout[k].Y + _layout[k].Height; break; }
                prevMidY = _layout[k].Y + S(12); prevColor = GetJobStatusColor(_jobs[_layout[k].JobIndex]); break;
            }

            int renderTop = (int)(_scrollY - _slideOffset), clampedPrevMidY = Math.Max(prevMidY, renderTop);

            if (AppConfig.FX.ConnectionGradients) { 
                float distance = midY - clampedPrevMidY;
                if (distance > 0) {
                    using var vertBrush = new LinearGradientBrush(new PointF(lineX, clampedPrevMidY), new PointF(lineX, midY), prevColor, gc);
                    using var vertPen = new Pen(vertBrush, S(1)); g.DrawLine(vertPen, lineX, clampedPrevMidY, lineX, midY); 
                }
            }
            else { using var solidPen = new Pen(gc, S(1)); g.DrawLine(solidPen, lineX, clampedPrevMidY, lineX, midY); }

            using var horizPen = new Pen(gc, S(1)); g.DrawLine(horizPen, lineX, midY, childPad - S(4), midY);

            if (!rowInfo.IsLastChild) {
                int viewBottom = (int)(_scrollY - _slideOffset) + (Height - HeaderH - S(6));
                int nextY = y + totalH + S(12);
                if (nextY > viewBottom) {
                    using var dropPen = new Pen(gc, S(1));
                    g.DrawLine(dropPen, lineX, midY, lineX, viewBottom);
                }
            }
            
            if (job.IsExpanded)
            {
                int ey = y + RowH - S(1); 
                Color drawerBg = job.Status switch { UploadStatus.Failed or UploadStatus.Cancelled => Color.FromArgb(6, 2, 16), UploadStatus.Done or UploadStatus.Uploading => Color.FromArgb(6, 10, 24), UploadStatus.Queued => Color.FromArgb(18, 16, 28), UploadStatus.UpdateReady => Color.FromArgb(24, 12, 36), _ => CErrBg };
                Color drawerBorder = job.Status switch { UploadStatus.Failed or UploadStatus.Cancelled => Color.FromArgb(100, 44, 28), UploadStatus.Done => Color.FromArgb(38, 95, 52), UploadStatus.Uploading => CGoldMid, UploadStatus.Queued => Color.FromArgb(90, 72, 22), UploadStatus.UpdateReady => Color.FromArgb(140, 80, 180), _ => CErrBorder };
                Color diagTitleColor = job.Status switch { UploadStatus.Failed or UploadStatus.Cancelled => Color.FromArgb(140, 82, 58), UploadStatus.Done => Color.FromArgb(68, 130, 80), UploadStatus.Uploading => CGoldMid, UploadStatus.Queued => Color.FromArgb(120, 100, 42), UploadStatus.UpdateReady => Color.FromArgb(190, 120, 230), _ => Color.FromArgb(140, 82, 58) };
                Color diagTextColor = job.Status switch { UploadStatus.Failed or UploadStatus.Cancelled => Color.FromArgb(215, 118, 100), UploadStatus.Done => Color.FromArgb(130, 200, 140), UploadStatus.Uploading => CGoldBrt, UploadStatus.Queued => Color.FromArgb(185, 160, 80), UploadStatus.UpdateReady => Color.FromArgb(210, 160, 240), _ => Color.FromArgb(170, 190, 160) };

                using var errBgBrush = new SolidBrush(drawerBg); g.FillRectangle(errBgBrush, childPad, ey, childW, rowInfo.ExpandedHeight);
                using var errPen = new Pen(drawerBorder, 1);
                
                g.DrawLine(errPen, childPad, ey, childPad + childW - 1, ey); 
                g.DrawLine(errPen, childPad, ey, childPad, ey + rowInfo.ExpandedHeight - 1); 
                g.DrawLine(errPen, childPad + childW - 1, ey, childPad + childW - 1, ey + rowInfo.ExpandedHeight - 1); 
                g.DrawLine(errPen, childPad, ey + rowInfo.ExpandedHeight - 1, childPad + childW - 1, ey + rowInfo.ExpandedHeight - 1); 

                if (AppConfig.FX.DrawerShadow)
                {
                    using var drawerShadow = new LinearGradientBrush(new Rectangle(childPad + 1, ey, childW - 2, S(10)), Color.FromArgb(50, 0, 0, 0), Color.Transparent, LinearGradientMode.Vertical);
                    g.FillRectangle(drawerShadow, childPad + 1, ey, childW - 2, S(10));
                }

                using var diagTitleBrush = new SolidBrush(diagTitleColor); g.DrawString("// DIAGNOSTICS:", _fBody75Italic, diagTitleBrush, new PointF(childPad + S(10), ey + S(6)));

                string diagContent = GetDiagContent(job, syncBatch);
                using var errMsgBrush = new SolidBrush(diagTextColor);
                
                float textWNoScroll = childW - S(10);
                float textWWithScroll = childW - S(35);
                
                int requiredDrawH = 0; 
                foreach (var sj in syncBatch) {
                    requiredDrawH += S(15); 
                    if (sj.Status is UploadStatus.Failed or UploadStatus.Cancelled && !string.IsNullOrWhiteSpace(sj.ErrorMessage)) {
                        requiredDrawH += (int)g.MeasureString(sj.ErrorMessage, _fBody75Reg, (int)textWWithScroll).Height + S(10);
                    }
                }

                bool willScroll = requiredDrawH > (rowInfo.ExpandedHeight - S(38));
                float actualTextW = willScroll ? textWWithScroll : textWNoScroll;
                
                var diagTextRect = new RectangleF(childPad + S(5), ey + S(28), actualTextW, rowInfo.ExpandedHeight - S(33));

                float maxScroll = Math.Max(0, requiredDrawH - diagTextRect.Height); _diagMaxScrolls[idx] = maxScroll; 
                float currentScroll = _diagScrolls.TryGetValue(idx, out float ds) ? ds : 0f;
                if (currentScroll > maxScroll) currentScroll = maxScroll; _diagScrolls[idx] = currentScroll; 

                var prevClip = g.Clip; g.SetClip(diagTextRect, CombineMode.Intersect); 

                float currentFileY = diagTextRect.Y - currentScroll;
                
                if (job.IsUpdate) {
                     g.DrawString(diagContent, _fBody75Reg, errMsgBrush, new RectangleF(diagTextRect.X, currentFileY, diagTextRect.Width, 9999), StringFormat.GenericDefault); 
                }
                else {
                    foreach (var sj in syncBatch)
                    {
                        if (currentFileY > ey + rowInfo.ExpandedHeight) break; 

                        string checkGlyph = sj.Status switch { UploadStatus.Done => "✓", UploadStatus.Failed or UploadStatus.Cancelled => "X", _ => "-" };
                        Color sCol = sj.Status switch { UploadStatus.Done => CGreen, UploadStatus.Failed or UploadStatus.Cancelled => CBarFail, UploadStatus.Uploading => CGoldBrt, _ => CTextSub };
                        
                        using var sBrush = new SolidBrush(sCol);

                        double totalMB = sj.FileSizeBytes / 1048576.0;
                        double totalKB = sj.FileSizeBytes / 1024.0;
                        string sSizeStr = sj.FileSizeBytes >= 1048576 ? $"{totalMB:0.0}MB" : $"{totalKB:0}KB";
                        string uploadedStr = sj.FileSizeBytes >= 1048576 ? $"{totalMB * sj.Progress:0.0}MB" : $"{totalKB * sj.Progress:0}KB";

                        if (sj.Status == UploadStatus.Queued) uploadedStr = sj.FileSizeBytes >= 1048576 ? "0.0MB" : "0KB";
                        if (sj.Status == UploadStatus.Done) uploadedStr = sSizeStr;

                        string sizeProgressTxt = $"{uploadedStr} / {sSizeStr}";
                        var szFont = _fBody75Reg; 

                        float fixedSizeTextW = S(90); 
                        float miniBarW = S(65);
                        
                        float rightEdge = diagTextRect.X + actualTextW;
                        float textX = rightEdge - fixedSizeTextW;
                        float barX = textX - miniBarW - S(10); 

                        string fullFileName = $"{checkGlyph} {sj.FileName}";
                        float maxNameW = barX - diagTextRect.X - S(15); 
                        string truncName = Trunc(fullFileName, g, _fBody75Bold, maxNameW);

                        g.DrawString(truncName, _fBody75Bold, sBrush, diagTextRect.X, currentFileY);

                        using var miniBg = new SolidBrush(Color.FromArgb(40, sCol)); g.FillRectangle(miniBg, barX, currentFileY + S(6), miniBarW, S(3));
                        using var miniFg = new SolidBrush(sCol); g.FillRectangle(miniFg, barX, currentFileY + S(6), miniBarW * sj.Progress, S(3));

                        using var sizeBrush = new SolidBrush(sCol);
                        
                        using var sfRight = new StringFormat { Alignment = StringAlignment.Far, FormatFlags = StringFormatFlags.NoClip | StringFormatFlags.NoWrap };
                        g.DrawString(sizeProgressTxt, szFont, sizeBrush, new RectangleF(textX, currentFileY, fixedSizeTextW, S(15)), sfRight);
                        
                        currentFileY += S(15);

                        if (sj.Status is UploadStatus.Failed or UploadStatus.Cancelled && !string.IsNullOrWhiteSpace(sj.ErrorMessage))
                        {
                            var errSz = g.MeasureString(sj.ErrorMessage, _fBody75Reg, (int)actualTextW - S(15));
                            using var errTxtBrush = new SolidBrush(Color.FromArgb(215, 118, 100));
                            g.DrawString(sj.ErrorMessage, _fBody75Reg, errTxtBrush, new RectangleF(diagTextRect.X + S(15), currentFileY, actualTextW - S(15), errSz.Height));
                            currentFileY += errSz.Height + S(10);
                        }
                    }
                }
                g.Clip = prevClip;

                bool hasScroll = maxScroll > 0;
                if (hasScroll)
                {
                    float sbW = S(AppConfig.DiagScrollWidth);
                    float sbX = childPad + childW - sbW - S(AppConfig.DiagScrollRightOffset);
                    float topBoxY = ey + S(AppConfig.DiagScrollTopPad);
                    float botBoxY = ey + rowInfo.ExpandedHeight - S(AppConfig.DiagScrollBottomPad) - sbW;
                    float trackY = topBoxY + sbW + S(3);
                    float trackBottom = botBoxY - S(3);
                    float trackH = trackBottom - trackY;

                    float thumbH = Math.Max(S(15), trackH * (diagTextRect.Height / requiredDrawH)), thumbY = trackY + (trackH - thumbH) * (currentScroll / maxScroll);

                    if (currentScroll < maxScroll - 1)
                    {
                        using var fadeBrush = new LinearGradientBrush(new RectangleF(diagTextRect.X, diagTextRect.Bottom - S(16), diagTextRect.Width, S(16)), Color.FromArgb(0, drawerBg), drawerBg, LinearGradientMode.Vertical);
                        g.FillRectangle(fadeBrush, diagTextRect.X, diagTextRect.Bottom - S(16), diagTextRect.Width, S(16));
                        int arrowAlpha = (int)(Math.Sin(_shimmer * 0.25f) * 100 + 100);
                        using var indBrush = new SolidBrush(Color.FromArgb(arrowAlpha, diagTextColor));
                        g.DrawString("ↆ", _fBody9Bold, indBrush, new PointF(diagTextRect.X + diagTextRect.Width / 1.8f, diagTextRect.Bottom - S(15)));
                    }

                    using var sbTrackBrush = new SolidBrush(Color.FromArgb(25, drawerBorder)); g.FillRectangle(sbTrackBrush, sbX, trackY, sbW, trackH);
                    using var arrowPen = new Pen(diagTextColor, 1.1f * _scale); arrowPen.LineJoin = LineJoin.Round; 

                    g.DrawRectangle(arrowPen, sbX, topBoxY, sbW, sbW); 
                    g.DrawLines(arrowPen, new PointF[] { new PointF(sbX + S(3), topBoxY + S(7)), new PointF(sbX + sbW/2, topBoxY + S(4)), new PointF(sbX + sbW - S(3), topBoxY + S(7)) }); 
                    
                    g.DrawRectangle(arrowPen, sbX, botBoxY, sbW, sbW); 
                    g.DrawLines(arrowPen, new PointF[] { new PointF(sbX + S(3), botBoxY + S(4)), new PointF(sbX + sbW/2, botBoxY + S(7)), new PointF(sbX + sbW - S(3), botBoxY + S(4)) }); 

                    using var sbThumbBrush = new SolidBrush(diagTextColor); g.FillRectangle(sbThumbBrush, sbX + S(1), thumbY, sbW - S(2), thumbH);
                    using var ridgePen = new Pen(drawerBg, 2); float midT = thumbY + thumbH / 2;
                    g.DrawLine(ridgePen, sbX + S(2), midT - S(2), sbX + sbW - S(3), midT - S(2)); g.DrawLine(ridgePen, sbX + S(2), midT, sbX + sbW - S(3), midT); g.DrawLine(ridgePen, sbX + S(2), midT + S(2), sbX + sbW - S(3), midT + S(2));
                }
            }

            Color baseCardBg = job.IsUpdate ? Color.FromArgb(13, 10, 30) : (idx % 2 == 2 ? Color.FromArgb(13, 13, 13) : Color.FromArgb(8, 6, 1));
            using var altBrush = new SolidBrush(baseCardBg); g.FillRectangle(altBrush, childPad, y, childW, RowH);
            using var blockBorderPen = new Pen(Color.FromArgb(190, CGoldDim), 1.53f);
            if (job.IsExpanded) { g.DrawLine(blockBorderPen, childPad, y, childPad + childW - 1, y); g.DrawLine(blockBorderPen, childPad, y, childPad, y + RowH); g.DrawLine(blockBorderPen, childPad + childW - 1, y, childPad + childW - 1, y + RowH); }
            else { g.DrawRectangle(blockBorderPen, childPad, y, childW - 1, RowH - 1); }

            int badgeSize = S(20), badgeX = childPad + S(4), badgeY = y + S(4);
            if (AppConfig.FX.RowBadgeGlow) { using var badgeBgBrush = new SolidBrush(Color.FromArgb(25, gc)); g.FillRectangle(badgeBgBrush, badgeX, badgeY, badgeSize, badgeSize); }
            using var badgeBorderPen = new Pen(Color.FromArgb(255, gc), 1.5f); g.DrawRectangle(badgeBorderPen, badgeX, badgeY, badgeSize, badgeSize);
            if (AppConfig.FX.RowBadgeScanlines) {
                using var badgeScanPen = new Pen(Color.FromArgb(20, gc), 1);
                for (int si = badgeY + 2; si < badgeY + badgeSize; si += 3) g.DrawLine(badgeScanPen, badgeX + 1, si, badgeX + badgeSize - 1, si);
            }
            
            DrawGlowingTextRect(g, glyph, _fMono75Bold, gc, new RectangleF(badgeX, badgeY + S(1), badgeSize, badgeSize), _sfCenter, AppConfig.FX.RowBadgeGlow ? 50 : 0);

            var actionBtn = ActionBtnRect(job, y, childPad, childW);
            var diagBtn = DiagBtnRect(job, y, childPad, childW);
            var trashBtn = TrashBtnRect(job, y, childPad, childW);
            var copyBtn = CopyBtnRect(job, y, childPad, childW);

            string titleStr = "|" + Trunc(syncTimeName, g, _fTitle95, childW - S(100));
            var titleSz = g.MeasureString(titleStr, _fTitle95);

            int finishedCount = syncBatch.Count(j => j.Status == UploadStatus.Done || j.Status == UploadStatus.UpdateReady || j.Status == UploadStatus.Failed || j.Status == UploadStatus.Cancelled);
            string detailText = job.IsUpdate ? $"size      {totalSizeDisplay}\nversion   v{job.UpdateVersion}" : $"size: {totalSizeDisplay}\n{(batchDone ? $"total synced: {syncBatch.Count}" : $"total files: {finishedCount} / {syncBatch.Count}")}";
            var detailSz = g.MeasureString(detailText, _fBody8Italic);
            float bx = childPad + S(13); 

            // --- Alert Priority Logic ---
            bool isDone = job.Status == UploadStatus.Done;
            bool isUpdate = job.IsUpdate && job.Status == UploadStatus.UpdateReady;
            bool isError = batchHasError && !batchUploading; // Failed
            bool isWarn = batchHasError && batchUploading; // Warning
            bool isInterrupted = job.Status == UploadStatus.Cancelled;
            bool isPending = syncBatch.Any(j => j.Status == UploadStatus.Queued || j.Status == UploadStatus.Uploading);

            int actionAlert = 0, diagAlert = 0, trashAlert = 0, copyAlert = 0;
            string iconName = isUpdate ? "APPLY" : ((job.CanRetry || isDone) ? "RESEND" : "ABORT");

            if (isDone) {
                // All 0 (Dim) by default
            } else if (isUpdate) {
                actionAlert = 3; // High alert to apply
                diagAlert = 2;   // Medium alert to read patch notes
            } else if (isError || isWarn) {
                diagAlert = 3;   // High alert to check error
                actionAlert = 2; // Medium alert to retry
                trashAlert = 2;  // Medium alert to trash
                copyAlert = 1;   // Low alert (Lit to copy text)
            } else if (isInterrupted) {
                actionAlert = 3; // High alert to resend
                diagAlert = 2;   // Medium alert
                trashAlert = 2;  // Medium alert
                copyAlert = 1;
            } else if (isPending) {
                if (iconName == "RESEND") actionAlert = 3; // High alert if able to resend
                else actionAlert = 1; // Low alert (Lit solid color) for typical aborting
                diagAlert = 1; 
            }

            if (actionBtn.HasValue) {
                bool hoverAction = _hoverLayoutIdx >= 0 && _layout.Count > _hoverLayoutIdx && !_layout[_hoverLayoutIdx].IsSeparator && _layout[_hoverLayoutIdx].JobIndex == idx;
                Color baseActionCol = isUpdate ? Color.FromArgb(0, 255, 150) : ((job.CanRetry || isDone) ? CGreen : CBarFail); 
                DrawIconBtn(g, actionBtn.Value, baseActionCol, hoverAction, actionAlert, baseCardBg, iconName, (actionAlert == 3 && isUpdate) ? 1.5f : 1f);
            }

            bool hoverDiag = _hoverDiagJobIdx == idx;
            DrawIconBtn(g, diagBtn, gc, hoverDiag, diagAlert, baseCardBg, job.IsExpanded ? "DIAG_OPEN" : "DIAG");

            if (trashBtn.HasValue) {
                DrawIconBtn(g, trashBtn.Value, gc, _hoverTrashJobIdx == idx, trashAlert, baseCardBg, "TRASH");
            }
            
            if (copyBtn.HasValue) {
                DrawIconBtn(g, copyBtn.Value, gc, _hoverCopyJobIdx == idx, copyAlert, baseCardBg, "COPY");
            }

            DrawHazyText(g, titleStr, _fTitle95, job.Status == UploadStatus.UpdateReady ? Color.FromArgb(210, 160, 240) : CText, childPad + S(25), y + S(5));
            
            float rightAlignX = childPad + childW - S(8); 
            
            var detailRect = new RectangleF(bx, y + S(30), detailSz.Width + S(35), detailSz.Height - S(1));
            using var infoBgPath = RoundRect(detailRect.X, detailRect.Y, detailRect.Width, detailRect.Height, S(1));
            using var infoBgBrush = new SolidBrush(BrightenColor(baseCardBg, 0.1f, LightFilter.Dusky));
            g.FillPath(infoBgBrush, infoBgPath);
            using var infoBorder = new Pen(BrightenColor(baseCardBg, 0.20f, LightFilter.None), 2f);
            g.DrawPath(infoBorder, infoBgPath);
            using var detailTextBrush = new SolidBrush(BrightenColor(baseCardBg, 0.65f, LightFilter.Cool)); g.DrawString(detailText, _fMono8, detailTextBrush, new PointF(detailRect.X + S(3), detailRect.Y));

            string pct = batchDone ? "100%" : (batchHasError && !batchUploading ? "ERR!" : (syncBatch.All(j => j.Status == UploadStatus.Queued) ? "PEND" : $"{batchAvgProgress * 100:0}%"));
            if (batchHasError && batchUploading) pct = $"WARN {pct}";

            using var pctBrush = new SolidBrush(Color.FromArgb(220, gc));
            var pctSz = g.MeasureString(pct, _fTitle125Bold);
            
            float pctY = y + RowH - pctSz.Height + S(3); 
            float pctX = rightAlignX - pctSz.Width;
            g.DrawString(pct, _fTitle125Bold, pctBrush, new PointF(pctX, pctY)); 

            float bw = pctX - bx - S(10);
            float by = pctY + pctSz.Height - S(16); 
            using var track = RoundRect(bx, by, bw, BarH, S(1));

            if (syncBatch.All(j => j.Status == UploadStatus.Queued) && AppConfig.FX.BarPulseQueued) {
                using var pulseBrush = new SolidBrush(Color.FromArgb((int)(30 + ((float)(Math.Sin(_shimmer * 0.15f) + 1f) / 2f) * 100), Color.White)); g.FillPath(pulseBrush, track);
            }

            float fill = batchDone ? bw : bw * batchAvgProgress;
            if (fill > 1f && !syncBatch.All(j => j.Status == UploadStatus.Queued))
            {
                using var fp = RoundRect(bx, by, fill, BarH, S(3));
                if (job.Status == UploadStatus.UpdateReady) { using var updateFill = new SolidBrush(Color.FromArgb(180, 100, 220)); g.FillPath(updateFill, fp); }
                else { using var fillBrush = new SolidBrush(gc); g.FillPath(fillBrush, fp); }

                var prevClip = g.Clip; g.SetClip(fp, CombineMode.Intersect); 

                if (batchUploading && AppConfig.FX.BarEnergyUpload) {
                    using var energyPen = new Pen(Color.FromArgb(180, 255, 255, 255), S(3)); float offset = (_shimmer * 2.0f) % S(50);
                    for (float ex = bx - S(70) + offset; ex < bx + fill + S(100); ex += S(10)) g.DrawLine(energyPen, ex, by + BarH + S(2), ex + S(2), by - S(25));
                    using var leadGlow = new LinearGradientBrush(new RectangleF(bx + fill - S(20), by, S(20), BarH), Color.Transparent, Color.FromArgb(255, Color.Orange), LinearGradientMode.Horizontal);
                    if (fill > S(20)) g.FillRectangle(leadGlow, bx + fill - S(20), by, S(20), BarH);
                }
                else if (batchHasError && !batchUploading && AppConfig.FX.BarStaticFailed) {
                    using var staticPen = new Pen(Color.FromArgb(30, 0, 0, 0), 4f);
                    for (float ex = bx; ex < bx + fill; ex += S(1)) if (_rand.Next(3) > 1) g.DrawLine(staticPen, ex, by, ex, by + BarH);
                }
                else if (batchDone && AppConfig.FX.BarCoolGlowDone) {
                    float offset = (_shimmer * 4.0f) % (bw + S(150));
                    if (offset < bw) { using var coolGlow = new LinearGradientBrush(new RectangleF(bx + offset, by, S(7), BarH), Color.Transparent, Color.FromArgb(255, 255, 255), LinearGradientMode.Horizontal); g.FillRectangle(coolGlow, bx + offset, by, S(10), BarH); }
                }
                g.Clip = prevClip; 
            }
        }

        private void DrawHazyText(Graphics g, string text, Font f, Color color, float x, float y)
        {
            string displayText = text;
            if (AppConfig.FX.TextHazyGlitch && text.Length > 0 && _rand.Next(1000) < 10) 
            {
                char[] chars = displayText.ToCharArray();
                chars[_rand.Next(chars.Length)] = "01░▒▓■_!*ØX?"[_rand.Next(12)];
                displayText = new string(chars);
            }
            DrawGlowingText(g, displayText, f, color, x, y, AppConfig.FX.HeaderNeonText ? 20 : 0);
        }

        private void DrawBtn(Graphics g, Rectangle r, string label, Color accent, Color txtColor, bool hov, bool glow = false)
        {
            using var path = RoundRect(r.X, r.Y, r.Width, r.Height, S(4));
            
            if (glow && AppConfig.FX.ButtonGlows)
            {
                int pulse = (int)(Math.Sin(_shimmer * 0.15f) * 80 + 80); 
                using var pOuter = new Pen(Color.FromArgb(pulse / 3, accent), S(4));
                g.DrawPath(pOuter, path);
                
                var innerPath = RoundRect(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2, S(3));
                using var pInner = new Pen(Color.FromArgb(pulse / 4, accent), S(2));
                g.DrawPath(pInner, innerPath);
                innerPath.Dispose();
            }

            Color fillC = hov ? Color.FromArgb(40, accent) : Color.FromArgb(10, accent);
            using var fillBrush = new SolidBrush(fillC);
            g.FillPath(fillBrush, path);

            using var btnPen = new Pen(hov || (glow && AppConfig.FX.ButtonGlows) ? Color.FromArgb(255, accent) : Color.FromArgb(130, accent), S(1));
            g.DrawPath(btnPen, path);
            
            using var textBrush = new SolidBrush(hov ? Color.White : txtColor); 
            g.DrawString(label, _fBody8Bold, textBrush, new RectangleF(r.X, r.Y, r.Width, r.Height), _sfCenter);
        }

        private void DrawIconBtn(Graphics g, Rectangle r, Color baseJobColor, bool hov, int alertLevel, Color containerBg, string iconType, float borderThickness = 1f)
        {
            // 0: Dim (No Alert), 1: Lit (Low Alert), 2: Slow Pulse (Medium Alert), 3: Fast Flash (High Alert)
            float pulseAmt = 0f;
            if (alertLevel == 3) pulseAmt = (float)(Math.Sin(_shimmer * 0.40f) + 1f) / 2f; 
            else if (alertLevel == 2) pulseAmt = (float)(Math.Sin(_shimmer * 0.12f) + 1f) / 2f; 
            
            Color activeColor = baseJobColor;
            
            if (alertLevel == 0) {
                // Dim state: Lower alpha to fade it into the panel without greying it out completely
                activeColor = Color.FromArgb(90, baseJobColor.R, baseJobColor.G, baseJobColor.B); 
            } 
            else if (alertLevel == 2) {
                // Medium Alert: A soft, breathing ember. 
                int alpha = (int)(130 + (100 * pulseAmt));
                
                float softGlow = pulseAmt * 0.20f;
                int rVal = (int)(baseJobColor.R + (255 - baseJobColor.R) * softGlow);
                int gVal = (int)(baseJobColor.G + (255 - baseJobColor.G) * softGlow);
                int bVal = (int)(baseJobColor.B + (255 - baseJobColor.B) * softGlow);
                
                activeColor = Color.FromArgb(alpha, rVal, gVal, bVal);
            }
            else if (alertLevel == 3 && pulseAmt > 0f) {
                // High Alert: Sharp, aggressive strobe to pure white
                int rVal = (int)(baseJobColor.R + (255 - baseJobColor.R) * pulseAmt);
                int gVal = (int)(baseJobColor.G + (255 - baseJobColor.G) * pulseAmt);
                int bVal = (int)(baseJobColor.B + (255 - baseJobColor.B) * pulseAmt);
                activeColor = Color.FromArgb(255, rVal, gVal, bVal);
            }
            
            // Hover safely overrides to a reddish-orange for tactile interaction
            if (hov) activeColor = Color.FromArgb(255, 100, 40);

            using var path = RoundRect(r.X, r.Y, r.Width, r.Height, S(2));
            
            // Increased the hover fill alpha to 60 to make it more visible on dark backgrounds
            Color fillC = hov ? Color.FromArgb(60, activeColor) : Color.Transparent;
            if (iconType == "DIAG_OPEN") fillC = hov ? Color.FromArgb(80, activeColor) : Color.FromArgb(20, activeColor);
            
            using var fillBrush = new SolidBrush(fillC);
            g.FillPath(fillBrush, path);

            using var btnPen = new Pen(activeColor, borderThickness * _scale);
            g.DrawPath(btnPen, path);

            using var iconPen = new Pen(activeColor, 0.76f * _scale);
            int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;

            if (iconType == "TRASH")
            {
                int tW = S(7), tH = S(11), tX = cx - tW / 2, tY = cy - tH / 2 + S(1);
                g.DrawLine(iconPen, tX - S(2), tY, tX + tW + S(2), tY); 
                g.DrawLine(iconPen, tX + S(2), tY, tX + S(2), tY - S(2)); 
                g.DrawLine(iconPen, tX + tW - S(2), tY, tX + tW - S(2), tY - S(2)); 
                g.DrawLine(iconPen, tX + S(2), tY - S(2), tX + tW - S(2), tY - S(2)); 
                g.DrawRectangle(iconPen, tX, tY, tW, tH); 
                g.DrawLine(iconPen, tX + S(2), tY + S(2), tX + S(2), tY + tH - S(2)); 
                g.DrawLine(iconPen, tX + tW - S(2), tY + S(2), tX + tW - S(2), tY + tH - S(2)); 
            }
            else if (iconType == "COPY")
            {
                int docW = Math.Max(S(8), r.Width / 2 - S(5)), docH = Math.Max(S(11), r.Height / 2);
                int docX = cx - docW / 2 + S(1), docY = cy - docH / 2 + S(1);
                g.DrawRectangle(iconPen, docX - S(2), docY - S(2), docW, docH);
                using var maskBrush = new SolidBrush(containerBg); g.FillRectangle(maskBrush, docX, docY, docW, docH);
                if (hov) { using var hv = new SolidBrush(Color.FromArgb(60, activeColor)); g.FillRectangle(hv, docX, docY, docW, docH); }
                g.DrawRectangle(iconPen, docX, docY, docW, docH); 
                g.DrawLine(iconPen, docX + S(2), docY + S(2), docX + docW - S(2), docY + S(2)); 
                g.DrawLine(iconPen, docX + S(2), docY + S(5), docX + docW - S(2), docY + S(5)); 
                g.DrawLine(iconPen, docX + S(2), docY + S(8), docX + docW - S(3), docY + S(8));
            }
            else if (iconType == "DIAG" || iconType == "DIAG_OPEN")
            {
                int pw = S(10), ph = S(8);
                int px = cx - pw / 2, py = cy;
                using var diagIconPen = new Pen(activeColor, 1.2f * _scale) { LineJoin = LineJoin.Round };
                g.DrawLines(diagIconPen, new PointF[] {
                    new PointF(px, py),
                    new PointF(px + S(2), py),
                    new PointF(px + S(4), py - ph/2),
                    new PointF(px + S(6), py + ph/2),
                    new PointF(px + S(8), py),
                    new PointF(px + pw, py)
                });
            }
            else
            {
                string iconChar = iconType switch {
                "RESEND" => "↻", "ABORT" => "✖", "APPLY" => "⇪", _ => "?"
                };
                using var textBrush = new SolidBrush(activeColor);
                
                Font iconFont = _fMono9Bold; 
                float yOffset = S(1);

                if (iconType == "APPLY") 
                {
                    iconFont = _fTitle125Bold; 
                    yOffset = S(2); 
                }

                g.DrawString(iconChar, iconFont, textBrush, new RectangleF(r.X, r.Y + yOffset, r.Width, r.Height), _sfCenter);            
            }
        }

        private Rectangle? ActionBtnRect(UploadJob job, int y, int childPad, int childW)
        {
            if (!job.CanRetry && !job.CanCancel && job.Status != UploadStatus.Done && job.Status != UploadStatus.UpdateReady) return null;
            
            if (job.IsUpdate)
            {
                // Custom position for the Apply button so the hover and click hitboxes perfectly align with the visual rendering
                return new Rectangle(childPad + S(154), y + S(30), S(28), S(28));
            }

            int btnSize = S(20), sp = S(5);
            int gridX = childPad + childW - (btnSize * 2 + sp) - S(8);
            
            return new Rectangle(gridX, y + S(8), btnSize, btnSize);
        }

        private Rectangle DiagBtnRect(UploadJob job, int y, int childPad, int childW)
        {
            int btnSize = S(20), sp = S(5);
            int gridX = childPad + childW - (btnSize * 2 + sp) - S(8);
            return new Rectangle(gridX + btnSize + sp, y + S(8), btnSize, btnSize);
        }

        private Rectangle? TrashBtnRect(UploadJob job, int y, int childPad, int childW)
        {
            if (job.IsUpdate) return null; 
            int btnSize = S(20), sp = S(5);
            int gridX = childPad + childW - (btnSize * 2 + sp) - S(8);
            return new Rectangle(gridX, y + S(8) + btnSize + sp, btnSize, btnSize);
        }

        private Rectangle? CopyBtnRect(UploadJob job, int y, int childPad, int childW)
        {
            int btnSize = S(20), sp = S(5);
            int gridX = childPad + childW - (btnSize * 2 + sp) - S(8);
            return new Rectangle(gridX + btnSize + sp, y + S(8) + btnSize + sp, btnSize, btnSize);
        }

        private void DrawHeader(Graphics g)
        {
            using var hg = new LinearGradientBrush(new Point(0, 0), new Point(0, HeaderH), Color.FromArgb(46, 36, 18), Color.FromArgb(20, 16, 9)); g.FillRectangle(hg, 0, 0, Width, HeaderH);
            using var bezelEdgePen = new Pen(Color.FromArgb(50, 255, 255, 255), 8); g.DrawLine(bezelEdgePen, 0, HeaderH - 2, Width, HeaderH - 2);
            using var bezelInnerPen = new Pen(Color.FromArgb(250, 0, 0, 0), 4); g.DrawLine(bezelInnerPen, 0, HeaderH - 1, Width, HeaderH - 1);

            int plateH = S(45), plateW = S(165), plateX = Pad-10, plateY = (HeaderH - plateH) / 2;
            using var platePath = RoundRect(plateX, plateY, plateW, plateH, S(4));
            using var plateBg = new LinearGradientBrush(new Rectangle(plateX, plateY, plateW, plateH), Color.FromArgb(12, 14, 16), Color.FromArgb(35, 40, 45), LinearGradientMode.Vertical); g.FillPath(plateBg, platePath);
            using var plateShadow = new Pen(Color.FromArgb(200, 0, 0, 0), S(2)); g.DrawPath(plateShadow, platePath);
            using var plateHi = new Pen(Color.FromArgb(40, 255, 255, 255), 1); g.DrawLine(plateHi, plateX + S(4), plateY + plateH, plateX + plateW - S(4), plateY + plateH);

            g.TranslateTransform(plateX, plateY);

            // --- VACUUM TUBE / NIXIE BULB START ---
            int rimSize = S(24), dx = S(8), dy = (plateH - rimSize) / 2;
            int glassPad = S(2), gSize = rimSize - glassPad * 2, gx = dx + glassPad, gy = dy + glassPad;

            // Outer industrial socket housing
            using var housingShadow = new SolidBrush(Color.FromArgb(180, 0, 0, 0)); 
            g.FillEllipse(housingShadow, dx + S(1), dy + S(1), rimSize, rimSize);
            using var housingBrush = new LinearGradientBrush(new Rectangle(dx, dy, rimSize, rimSize), Color.FromArgb(45, 45, 50), Color.FromArgb(10, 10, 12), LinearGradientMode.ForwardDiagonal);
            g.FillEllipse(housingBrush, dx, dy, rimSize, rimSize);
            using var housingRing = new Pen(Color.FromArgb(120, 160, 160, 160), 1);
            g.DrawEllipse(housingRing, dx, dy, rimSize, rimSize);

            // Deep void of the bulb cavity
            using var cavityBrush = new SolidBrush(Color.FromArgb(255, 2, 2, 3));
            g.FillEllipse(cavityBrush, gx, gy, gSize, gSize);

            // Extreme inner shadow to pull the void backward
            using var voidPath = new GraphicsPath(); voidPath.AddEllipse(gx, gy, gSize, gSize);
            using var voidDepth = new PathGradientBrush(voidPath) {
                CenterColor = Color.Transparent, 
                SurroundColors = new[] { Color.FromArgb(255, 0, 0, 0) }, 
                FocusScales = new PointF(0.3f, 0.3f)
            };
            g.FillEllipse(voidDepth, gx, gy, gSize, gSize);

            // Glowing filament and trapped gas
            if (AppConfig.FX.HeaderSocketGlow)
            {
                int pulseAlpha = Math.Max(50, _glowAlpha);
                
                // Background gas glow inside the tube
                using var gasPath = new GraphicsPath(); gasPath.AddEllipse(gx + S(3), gy + S(2), gSize - S(5), gSize - S(5));
                using var gasGlow = new PathGradientBrush(gasPath) {
                    // Warming the core gas with a Dusky filter for that rich, vintage heat
                    CenterColor = BrightenColor(_coreColor, 0.11f, LightFilter.None, (int)(pulseAlpha * 1.8f)),
                    SurroundColors = new[] { Color.Transparent },
                    FocusScales = new PointF(0.12f, 0.77f)
                };
                g.FillPath(gasGlow, gasPath);

                // Physical wire/filament running up the center
                int cx = gx + gSize / 2;
                using var wirePen = new Pen(Color.FromArgb(85, 0, 0, 0), S(10));
                g.DrawLine(wirePen, cx, gy + S(4), cx, gy + gSize - S(4));
                using var hotWirePen = new Pen(Color.FromArgb(pulseAlpha, _auraColor), S(1));
                g.DrawLine(hotWirePen, cx, gy + S(1), cx, gy + gSize - S(5));

                // Bright burning core on the filament
                using var burnCore = new SolidBrush(BrightenColor(_auraColor, 0.85f, LightFilter.Natural));
                g.FillEllipse(burnCore, cx - S(1), gy + gSize / 2 - S(2), S(2), S(4));
                using var burnHalo = new SolidBrush(BrightenColor(_auraColor, 0.45f, LightFilter.Dusky, pulseAlpha));
                g.FillEllipse(burnHalo, cx - S(2), gy + gSize / 2 - S(3), S(4), S(6));
            }

            // Heavy glass dome reflections to seal the bulb
            if (AppConfig.FX.HeaderGlassGlint)
            {
                // Dark edge thickness of the glass
                using var glassEdge = new Pen(Color.FromArgb(255, 0, 0, 0), S(3));
                g.DrawEllipse(glassEdge, gx + S(1), gy + S(1), gSize - S(2), gSize - S(2));

                // Sharp crescent reflection on the top curve
                using var topCrescent = new GraphicsPath();
                topCrescent.AddArc(gx + S(2), gy + S(2), gSize - S(4), gSize - S(4), 180, 180);
                topCrescent.AddArc(gx + S(2), gy + S(4), gSize - S(4), gSize - S(9), 0, -180);
                using var crescentBrush = new SolidBrush(Color.FromArgb(120, 255, 255, 255));
                g.FillPath(crescentBrush, topCrescent);

                // Subtle ambient bounce on the bottom lip
                using var botLip = new GraphicsPath();
                botLip.AddArc(gx + S(4), gy + S(4), gSize - S(8), gSize - S(8), 20, 140);
                using var botLipPen = new Pen(Color.FromArgb(25, 255, 255, 255), S(1));
                g.DrawPath(botLipPen, botLip);

                // Subtle Crack & Smudge for ruggedness ---
                if (AppConfig.CurrentMode != AppConfig.FidelityMode.Low)
                {
                    int smudgeAlpha = AppConfig.CurrentMode == AppConfig.FidelityMode.High ? 15 : 6;
                    using var smudgeBrush = new SolidBrush(Color.FromArgb(smudgeAlpha, 255, 255, 255));
                    g.FillEllipse(smudgeBrush, gx + S(6), gy + S(8), gSize - S(16), gSize - S(12));

                    int crackAlpha = AppConfig.CurrentMode == AppConfig.FidelityMode.High ? 80 : 35;
                    using var crackPen = new Pen(Color.FromArgb(crackAlpha, 255, 255, 255), 1f);
                    var crackPts = new PointF[] {
                        new PointF(gx + S(5), gy + S(16)),
                        new PointF(gx + S(9), gy + S(13)),
                        new PointF(gx + S(11), gy + S(15)),
                        new PointF(gx + S(17), gy + S(10))
                    };
                    g.DrawLines(crackPen, crackPts);
                    
                    int refractAlpha = AppConfig.CurrentMode == AppConfig.FidelityMode.High ? 40 : 15;
                    using var refractionPen = new Pen(Color.FromArgb(refractAlpha, _coreColor), 1f);
                    g.DrawLine(refractionPen, gx + S(10), gy + S(14), gx + S(14), gy + S(11));
                }
            }
            // --- VACUUM TUBE / NIXIE BULB END ---
            
            float titleX = dx + rimSize + S(8), titleY = S(5); 
            // Cast a warm, dusky sunset hue over the main neon title
            Color neonColor = BrightenColor(Color.FromArgb(255, 200, 100, 40), 0.35f, LightFilter.Dusky);


            if (AppConfig.FX.HeaderNeonText)
            {
                using var outerGlow = new SolidBrush(Color.FromArgb((int)(Math.Sin(_shimmer * 0.15f) * 40 + 60), neonColor));
                g.DrawString("Fissal Relay", _fTitle125Bold, outerGlow, new PointF(titleX - S(2), titleY - S(2))); g.DrawString("Fissal Relay", _fTitle125Bold, outerGlow, new PointF(titleX + S(2), titleY + S(2))); g.DrawString("Fissal Relay", _fTitle125Bold, outerGlow, new PointF(titleX + S(2), titleY - S(2))); g.DrawString("Fissal Relay", _fTitle125Bold, outerGlow, new PointF(titleX - S(2), titleY + S(2)));
                DrawGlowingText(g, "Fissal Relay", _fTitle125Bold, neonColor, titleX, titleY, 180);
            }

            using var coreNeon = new SolidBrush(Color.FromArgb(150, 255, 255, 200)); g.DrawString("Fissal Relay", _fTitle125Bold, coreNeon, new PointF(titleX, titleY));

            float subX = dx + rimSize + S(5), subY = S(25); string subText = $"Masser Matrix v{(System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(2) ?? "1.00")}";
            using var subIndentShadow = new SolidBrush(Color.FromArgb(255, 20, 10, 0)); g.DrawString(subText, _fBody95Italic, subIndentShadow, new PointF(subX-2, subY - 2));
            using var subIndentHi = new SolidBrush(Color.FromArgb(170, 190, 190, 190)); g.DrawString(subText, _fBody95Italic, subIndentHi, new PointF(subX, subY + 2));
            using var subCore = new SolidBrush(Color.FromArgb(100, CGoldMid.R, CGoldMid.G, CGoldMid.B)); g.DrawString(subText, _fBody95Italic, subCore, new PointF(subX, subY));
            g.ResetTransform(); 

            int mcW = S(115), mcH = S(25), mcX = CloseBtnRect.Left - mcW - S(8);
            int mcY = plateY + plateH - mcH; 
            using var mcPath = RoundRect(mcX, mcY, mcW, mcH, S(4));
            using var mcBg = new SolidBrush(Color.FromArgb(255, 4, 12, 6)); g.FillPath(mcBg, mcPath);

            using var mcInnerShadow = new LinearGradientBrush(new Rectangle(mcX, mcY, mcW, mcH), Color.FromArgb(220, 5, 10, 15), Color.Transparent, LinearGradientMode.Vertical); mcInnerShadow.SetBlendTriangularShape(0.2f); g.FillPath(mcInnerShadow, mcPath);

            if (AppConfig.FX.MCScanlines) { using var mcScanPen = new Pen(Color.FromArgb(20, CGreen), 1); for (int sy = mcY + 2; sy < mcY + mcH; sy += 3) g.DrawLine(mcScanPen, mcX + 1, sy, mcX + mcW - 2, sy); }

            using var mcBorder = new Pen(Color.FromArgb(255, 12, 14, 16), S(3)); g.DrawPath(mcBorder, mcPath);
            using var mcHighlight = new Pen(Color.FromArgb(50, 255, 255, 255), 1); g.DrawLine(mcHighlight, mcX + S(3), mcY + mcH + S(1), mcX + mcW - S(3), mcY + mcH + S(1));

            var statuses = new List<(string text, Color color, int type)>(); 
            bool hasReadyUpdate = false, hasError = false; string errorFile = ""; int active = 0, pending = 0;

            // Take a safe snapshot of the jobs list to prevent background syncs from crashing the paint thread
            var safeJobs = new List<UploadJob>();
            for (int k = 0; k < _jobs.Count; k++) {
                try { safeJobs.Add(_jobs[k]); } catch { break; }
            }

            foreach (var j in safeJobs) 
            {
                if (j.Status == UploadStatus.UpdateReady) hasReadyUpdate = true;
                if (j.Status == UploadStatus.Failed || j.Status == UploadStatus.Cancelled) { hasError = true; if (string.IsNullOrEmpty(errorFile)) errorFile = j.FileName; }
                if (j.Status == UploadStatus.Uploading) active++;
                if (j.Status == UploadStatus.Queued) pending++;
            }

            if (hasError) statuses.Add(($"!! ERROR IN {errorFile.ToUpper()} !!", Color.FromArgb(255, 255, 40, 40), 2));
            if (hasReadyUpdate) statuses.Add(("!! UPDATE AVAILABLE! CHECK LOGS TO APPLY", Color.FromArgb(255, 190, 160, 255), 3));

            // [Req 8] Dynamic files syncing text
            if (active > 0) 
            {
                var activeJobs = safeJobs.Where(j => j.Status == UploadStatus.Uploading).ToList();
                string text = active == 1 ? $"> {activeJobs[0].FileName.ToUpper()} SYNCING" : $"> {active} FILES SYNCING";
                statuses.Add((text, Color.FromArgb(255, 255, 200, 0), 1));
            }
            else if (pending > 0) statuses.Add(($"# {pending} FILES PENDING", Color.FromArgb(255, 180, 255, 50), 1));
            
            if (statuses.Count == 0)
            {
                string dispName = RedfurSync.AppConfig.Instance.DisplayName;
                string userStatus = string.IsNullOrWhiteSpace(dispName) ? "" : $"> LOGGED IN AS {dispName.ToUpper()}";

                if (safeJobs.Count == 0)
                {
                    statuses.Add(("> STAND BY... AWAITING FILE CHANGES", Color.FromArgb(255, 50, 255, 50), 0));
                    if (userStatus != "") statuses.Add((userStatus, Color.FromArgb(255, 50, 255, 50), 0));
                }
                else
                {
                    int totalSynced = safeJobs.Count;
                    int lastLogCount = 0;

                    var newest = safeJobs.OrderByDescending(j => j.QueuedAt).FirstOrDefault();
                    if (newest != null)
                    {
                        lastLogCount = safeJobs.Count(j => Math.Abs((j.QueuedAt - newest.QueuedAt).TotalSeconds) <= 60 && j.IsUpdate == newest.IsUpdate);
                    }

                    statuses.Add(($"> {lastLogCount} IN LAST SYNC", Color.FromArgb(255, 50, 255, 50), 0));
                    statuses.Add(($"> {totalSynced} TOTAL SYNCED", Color.FromArgb(255, 50, 255, 50), 0));
                    if (userStatus != "") statuses.Add((userStatus, Color.FromArgb(255, 50, 255, 50), 0));
                }
            }

            if (_dispStatusIdx >= statuses.Count) _dispStatusIdx = 0;
            var currentStatus = statuses[_dispStatusIdx];
            string displayBadge = currentStatus.text; Color badgeColor = currentStatus.color;

            int lightDia = S(9), lightSpacing = S(20), lightsY = (mcY - lightDia+15) / 2, leftLightsStartX = mcX + S(25); 
            Color[] lightColors = { Color.FromArgb(60, 20, 220, 20), Color.FromArgb(50, 250, 190, 0), Color.FromArgb(60, 255, 30, 30), Color.FromArgb(60, 190, 50, 240) };

            for (int i = 0; i < 4; i++)
            {
                bool isActive = statuses.Exists(s => s.type == i) && !(_dispState == DisplayState.Glitching && _dispWait < 10);
                bool isCurrent = currentStatus.type == i;
                int cx = leftLightsStartX + (i * lightSpacing);
                if (i == 3) cx += S(18); 
                
                using var offBrush = new LinearGradientBrush(new Rectangle(cx, lightsY, lightDia, lightDia), 
                    Color.FromArgb(255, lightColors[i].R/3, lightColors[i].G/100, lightColors[i].B/45), 
                    Color.FromArgb(255, 40, 40, 70), LinearGradientMode.ForwardDiagonal); 
                g.FillEllipse(offBrush, cx, lightsY, lightDia, lightDia);
                
                using var rim = new Pen(Color.FromArgb(50, lightColors[i].R, lightColors[i].G, lightColors[i].B), 2f); 
                g.DrawEllipse(rim, cx, lightsY, lightDia, lightDia);

                if (isActive && AppConfig.FX.MCLightsGlow)
                {
                    int alpha = (int)((isCurrent ? (float)((Math.Sin(_shimmer * 0.50) + 1) / 2.0) : 1f) * 245 + 1); 
                    using var litBrush = new SolidBrush(Color.FromArgb(alpha, lightColors[i])); 
                    g.FillEllipse(litBrush, cx + 5, lightsY + 5, lightDia - 8, lightDia - 8);
                    
                    using var whiteCore = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255));
                    g.FillEllipse(whiteCore, cx + S(2), lightsY + S(2), lightDia - S(2), lightDia - S(2));

                    using var glowH = new SolidBrush(Color.FromArgb(alpha / 13+33, lightColors[i])); g.FillEllipse(glowH, cx - S(3), lightsY, lightDia + S(6), lightDia); 
                    using var glowV = new SolidBrush(Color.FromArgb(alpha / 13+13, lightColors[i])); g.FillEllipse(glowV, cx, lightsY - S(3), lightDia, lightDia + S(2)); 
                    using var glowCore = new SolidBrush(Color.FromArgb(alpha / 5, lightColors[i])); g.FillEllipse(glowCore, cx - S(1), lightsY - S(1), lightDia + S(2), lightDia + S(2));
                }

                using var glintBrush = new SolidBrush(Color.FromArgb(isActive ? 255 : 180, 255, 255, 255)); g.FillEllipse(glintBrush, cx + S(2), lightsY + S(1), S(3), S(2)); 
            }

            var textSz = g.MeasureString(displayBadge, _fBody8Reg);
            float maxScroll = Math.Max(0, textSz.Width - (mcW - S(12)));

            if (AppConfig.FX.MarqueeTextAnim)
            {
                if (_dispState == DisplayState.Glitching)
                {
                    if (_dispWait < 10) { 
                        displayBadge = ""; 
                    } else {
                        char[] chars = displayBadge.ToCharArray();
                        for(int i = 0; i < chars.Length; i++) if (_rand.Next(100) < 12) chars[i] = "░▒_-"[_rand.Next(4)];
                        displayBadge = new string(chars);
                    }

                    _dispWait--;
                    if (_dispWait <= 0) { _dispStatusIdx = (_dispStatusIdx + 1) % statuses.Count; _dispState = DisplayState.HoldStart; _marqueeWait = AppConfig.MarqueePause; _marqueeX = 0; }
                }
                else if (_dispState == DisplayState.HoldStart)
                {
                    _marqueeX = 0; _marqueeWait--;
                    if (_marqueeWait <= 0) 
                    { 
                        if (maxScroll > 0) 
                        {
                            _dispState = DisplayState.Scrolling; 
                        } 
                        else 
                        { 
                            // Skip scrolling and pause without glitching
                            _dispState = DisplayState.HoldEnd; 
                            _marqueeWait = 150; 
                        } 
                    }
                }
                else if (_dispState == DisplayState.Scrolling)
                {
                    _marqueeX -= _scale * 2f;
                    if (_marqueeX <= -maxScroll) { _marqueeX = -maxScroll; _dispState = DisplayState.HoldEnd; _marqueeWait = AppConfig.MarqueePause; }
                }
                else if (_dispState == DisplayState.HoldEnd)
                {
                    _marqueeX = -maxScroll; _marqueeWait--;
                    if (_marqueeWait <= 0) { _dispState = DisplayState.Glitching; _dispWait = 25; }
                }
            }
            else { _marqueeX = 0; }

            if (_dispState != DisplayState.Glitching && displayBadge.Length > 0 && AppConfig.FX.TextHazyGlitch && _rand.Next(1000) < 5)
            {
                char[] chars = displayBadge.ToCharArray(); chars[_rand.Next(chars.Length)] = "01░▒▓"[ _rand.Next(5) ]; displayBadge = new string(chars);
            }

            var clipState = g.Save(); g.SetClip(new Rectangle(mcX + S(2), mcY + S(2), mcW - S(4), mcH - S(4)), CombineMode.Intersect);
            DrawGlowingText(g, displayBadge, _fBody8Reg, badgeColor, mcX + S(6) + _marqueeX, mcY + S(5), AppConfig.FX.HeaderNeonText ? 90 : 0); 
            g.Restore(clipState);
            
            if (AppConfig.FX.MCGloss)
            {
                using var mcGloss = new LinearGradientBrush(new Rectangle(mcX, mcY, mcW, mcH / 1), Color.FromArgb(35, 110, 90, 110), Color.Transparent, LinearGradientMode.Vertical); g.FillPath(mcGloss, mcPath);
            }

            // Micro Display Screen Scratches ---
            if (AppConfig.CurrentMode != AppConfig.FidelityMode.Low)
            {
                int smudgeAlpha = AppConfig.CurrentMode == AppConfig.FidelityMode.High ? 20 : 8;
                
                void DrawSoftMCSmudge(float sx, float sy, float sw, float sh) {
                    using var p = new GraphicsPath(); p.AddEllipse(sx, sy, sw, sh);
                    using var pgb = new PathGradientBrush(p) { 
                        CenterColor = Color.FromArgb(smudgeAlpha, 255, 255, 255), 
                        SurroundColors = new[] { Color.Transparent } 
                    };
                    g.FillPath(pgb, p);
                }

                DrawSoftMCSmudge(mcX + S(10), mcY + S(5), S(40), S(15));
                DrawSoftMCSmudge(mcX + mcW - S(30), mcY + S(2), S(20), S(10));
                
                int scratchAlpha = AppConfig.CurrentMode == AppConfig.FidelityMode.High ? 30 : 15;
                using var mcScratchPen = new Pen(Color.FromArgb(scratchAlpha, 255, 255, 255), 1f);
                g.DrawLine(mcScratchPen, mcX + S(15), mcY + S(8), mcX + S(22), mcY + S(14));
                g.DrawLine(mcScratchPen, mcX + mcW - S(10), mcY + S(18), mcX + mcW - S(5), mcY + S(12));
            }

            var cxRect = CloseBtnRect;
            using var gasketBrush = new SolidBrush(Color.FromArgb(20, 20, 22)); g.FillEllipse(gasketBrush, cxRect);
            using var gasketRim = new Pen(Color.FromArgb(50, 50, 50), 2); g.DrawEllipse(gasketRim, cxRect);

            var btnRect = new Rectangle(cxRect.X + S(3), cxRect.Y + S(3) + (_hoverClose ? S(2) : 0), cxRect.Width - S(6), cxRect.Height - S(6));

            if (!_hoverClose) { using var shadow = new SolidBrush(Color.FromArgb(150, 0, 0, 0)); g.FillEllipse(shadow, new Rectangle(btnRect.X, btnRect.Y + S(2), btnRect.Width, btnRect.Height)); }

            using var capBrush = new LinearGradientBrush(btnRect, _hoverClose ? Color.FromArgb(180, 30, 30) : Color.FromArgb(200, 40, 40), _hoverClose ? Color.FromArgb(80, 10, 10) : Color.FromArgb(120, 20, 20), LinearGradientMode.Vertical); g.FillEllipse(capBrush, btnRect);

            float symW = S(8), symH = S(8), symX = btnRect.X + (btnRect.Width - symW) / 2f, symY = btnRect.Y + (btnRect.Height - symH) / 2f + S(1), center = symX + symW / 2f;
            using var symCorePen = new Pen(Color.FromArgb(220, 255, 255, 255), 1.5f * _scale); symCorePen.StartCap = LineCap.Round; symCorePen.EndCap = LineCap.Round;
            g.DrawArc(symCorePen, symX, symY, symW, symH, -60, 300); g.DrawLine(symCorePen, center, symY - S(2), center, symY + symH / 2.5f);

            if (AppConfig.FX.CloseBtnDome)
            {
                using var domeBrush = new LinearGradientBrush(new Rectangle(cxRect.X, cxRect.Y, cxRect.Width, cxRect.Height / 2), Color.FromArgb(90, 255, 255, 255), Color.Transparent, LinearGradientMode.Vertical); g.FillPie(domeBrush, cxRect.X, cxRect.Y, cxRect.Width, cxRect.Height, 180, 180);
                using var domeHighlight = new Pen(Color.FromArgb(120, 255, 255, 255), 1); g.DrawArc(domeHighlight, cxRect.X + 2, cxRect.Y + 2, cxRect.Width - 4, cxRect.Height - 4, 200, 140);
            }
        }

        private void DrawEmpty(Graphics g)
        {
            int a = (int)_emptyStateAlpha;
            var gsz = g.MeasureString("⚙", _fTitle28Bold);
            using var gearBrush = new SolidBrush(Color.FromArgb((int)(a * 0.08f), CGoldMid)); 
            g.DrawString("⚙", _fTitle28Bold, gearBrush, new PointF((Width - gsz.Width) / 2f, HeaderH + S(8)));
            
            var t1 = "Fissal's ears are calibrated!"; var s1 = g.MeasureString(t1, _fTitle10Bold);
            using var text1Brush = new SolidBrush(Color.FromArgb(a, CTextSub)); 
            g.DrawString(t1, _fTitle10Bold, text1Brush, new PointF((Width - s1.Width) / 2f, HeaderH + EmptyH - S(40)));
            
            var t2 = "*radio static hums softly, resembling a faint purr*"; var s2 = g.MeasureString(t2, _fBody8Italic);
            using var text2Brush = new SolidBrush(Color.FromArgb((int)(a * 0.27f), CTextSub)); 
            g.DrawString(t2, _fBody8Italic, text2Brush, new PointF((Width - s2.Width) / 2f, HeaderH + EmptyH - S(22)));
        }

        private void DrawScrollbar(Graphics g, Rectangle clipRect)
        {
            float maxScroll = _contentHeight + (int)_slideOffset - clipRect.Height; if (maxScroll <= 0) return;
            int sbWidth = S(9); 
            
            int sbPaneW = RightGutterW;
            int sbPaneX = Width - sbPaneW;
            using var sbPaneBg = new SolidBrush(Color.FromArgb(28, 24, 20));
            g.FillRectangle(sbPaneBg, sbPaneX, HeaderH, sbPaneW, clipRect.Height);
            using var sbPaneBorder = new Pen(Color.FromArgb(40, CGoldDim), 1);
            g.DrawLine(sbPaneBorder, sbPaneX, HeaderH, sbPaneX, HeaderH + clipRect.Height);

            int sbX = Width - S(12);
            float trackTop = HeaderH + S(20);
            float trackBot = HeaderH + clipRect.Height - S(20);
            float trackHeight = trackBot - trackTop;

            float thumbHeight = Math.Max(S(24), trackHeight * (clipRect.Height / (float)(_contentHeight + (int)_slideOffset)));
            float thumbY = trackTop + (trackHeight - thumbHeight) * (_scrollY / maxScroll);

            using var trackBrush = new SolidBrush(Color.FromArgb(150, 5, 5, 5)); g.FillRectangle(trackBrush, sbX, trackTop, sbWidth, trackHeight);
            using var trackShadow = new Pen(Color.FromArgb(100, 0, 0, 0), 1); g.DrawRectangle(trackShadow, sbX, trackTop, sbWidth, trackHeight);

            using var arrowPen = new Pen(Color.FromArgb(180, 140, 50), 1.8f * _scale); arrowPen.LineJoin = LineJoin.Round; 
            float arrX = sbX;
            g.DrawLines(arrowPen, new PointF[] { new PointF(arrX + S(1), HeaderH + S(12)), new PointF(arrX + sbWidth/2, HeaderH + S(7)), new PointF(arrX + sbWidth - S(1), HeaderH + S(12)) });
            g.DrawLines(arrowPen, new PointF[] { new PointF(arrX + S(1), HeaderH + clipRect.Height - S(12)), new PointF(arrX + sbWidth/2, HeaderH + clipRect.Height - S(7)), new PointF(arrX + sbWidth - S(1), HeaderH + clipRect.Height - S(12)) });

            using var thumbPath = RoundRect(sbX + 1, thumbY, sbWidth - 2, thumbHeight, S(2));
            using var thumbBrush = new SolidBrush(Color.FromArgb(255, 50, 45, 40)); g.FillPath(thumbBrush, thumbPath);

            using var thumbHi = new Pen(Color.FromArgb(80, 255, 255, 255), 1); using var thumbLo = new Pen(Color.FromArgb(150, 0, 0, 0), 1);
            g.DrawLine(thumbHi, sbX + 1, thumbY, sbX + sbWidth - 2, thumbY); g.DrawLine(thumbHi, sbX + 1, thumbY, sbX + 1, thumbY + thumbHeight);
            g.DrawLine(thumbLo, sbX + sbWidth - 2, thumbY, sbX + sbWidth - 2, thumbY + thumbHeight); g.DrawLine(thumbLo, sbX + 1, thumbY + thumbHeight, sbX + sbWidth - 2, thumbY + thumbHeight);

            using var gripDark = new Pen(Color.FromArgb(200, 10, 8, 5), 1); using var gripLight = new Pen(Color.FromArgb(50, 255, 255, 255), 1);
            float midY = thumbY + thumbHeight / 2;
            for (int i = -1; i <= 1; i++) { float yPos = midY + (i * S(4)); g.DrawLine(gripDark, sbX + S(2), yPos, sbX + sbWidth - S(3), yPos); g.DrawLine(gripLight, sbX + S(2), yPos + 1, sbX + sbWidth - S(3), yPos + 1); }
        }

private bool GetHitRow(float contentY, out int layoutIdx, out RowLayout hitRow)
        {
            layoutIdx = -1;
            hitRow = default;

            // Pass 1: Headers (Sticky and Normal)
            for (int i = 0; i < _layout.Count; i++)
            {
                var row = _layout[i];
                if (!row.IsSeparator) continue;
                
                int drawY = row.Y;
                if (row.GroupIsExpanded) {
                    int scrollTop = (int)_scrollY;
                    if (drawY < scrollTop && drawY + row.GroupTotalHeight - row.Height > scrollTop) {
                        drawY = scrollTop;
                    }
                }
                
                if (contentY >= drawY && contentY < drawY + row.Height) {
                    layoutIdx = i;
                    hitRow = row;
                    hitRow.Y = drawY; // Map the virtual Y so rect bounds check works
                    return true;
                }
            }

            // Pass 2: Jobs
            for (int i = 0; i < _layout.Count; i++)
            {
                var row = _layout[i];
                if (row.IsSeparator) continue;
                if (contentY >= row.Y && contentY < row.Y + row.Height) {
                    layoutIdx = i;
                    hitRow = row;
                    return true;
                }
            }

            return false;
        }

        private void OnMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || CloseBtnRect.Contains(e.Location)) return;

            int viewHeight = Height - HeaderH - S(6); float maxMainScroll = Math.Max(0, _contentHeight + (int)_slideOffset - viewHeight);
            if (maxMainScroll > 0)
            {
                int sbWidth = S(9), sbX = Width - S(12);
                float trackTop = HeaderH + S(22), trackBot = HeaderH + viewHeight - S(22), trackHeight = trackBot - trackTop;
                float thumbHeight = Math.Max(S(24), trackHeight * (viewHeight / (float)(_contentHeight + (int)_slideOffset))), thumbY = trackTop + (trackHeight - thumbHeight) * (_scrollY / maxMainScroll);

                if (new RectangleF(sbX, thumbY, sbWidth, thumbHeight).Contains(e.Location)) { _isDraggingMainScroll = true; _dragStartY = e.Y; _dragStartScrollY = _scrollY; return; }
                
                if (new RectangleF(Width - RightGutterW, HeaderH, RightGutterW, S(22)).Contains(e.Location)) { _targetScrollY = Math.Clamp(_targetScrollY - RowH, 0, maxMainScroll); return; }
                if (new RectangleF(Width - RightGutterW, trackBot, RightGutterW, S(22)).Contains(e.Location)) { _targetScrollY = Math.Clamp(_targetScrollY + RowH, 0, maxMainScroll); return; }
                
                if (new RectangleF(Width - RightGutterW, trackTop, RightGutterW, trackHeight).Contains(e.Location)) { _targetScrollY = Math.Clamp(_targetScrollY + (e.Y < thumbY ? -viewHeight * 0.8f : viewHeight * 0.8f), 0, maxMainScroll); return; }
            }

            if (e.Y > HeaderH)
            {
                float contentY = e.Y - HeaderH + _scrollY;
                if (_slideOffset > 0 && contentY >= _slideStartY) contentY -= _slideOffset;

                if (GetHitRow(contentY, out int li, out RowLayout row))
                {
                    if (!row.IsSeparator && row.JobIndex >= 0 && row.JobIndex < _jobs.Count)
                    {
                        var job = _jobs[row.JobIndex];
                        if (job.IsExpanded && contentY >= row.Y + RowH && contentY <= row.Y + RowH + row.ExpandedHeight)
                        {
                            float diagMaxScroll = _diagMaxScrolls.TryGetValue(row.JobIndex, out float ms) ? ms : 0f;
                            if (diagMaxScroll > 0) 
                            {
                                float sbW = S(AppConfig.DiagScrollWidth);
                                float trackY = (row.Y + RowH) + S(AppConfig.DiagScrollTopPad) + sbW + S(2); 
                                float trackH = row.ExpandedHeight - S(AppConfig.DiagScrollTopPad) - S(AppConfig.DiagScrollBottomPad) - (sbW * 2) - S(4);
                                float currentScroll = _diagScrolls.TryGetValue(row.JobIndex, out float ds) ? ds : 0f;
                                float thumbH = Math.Max(S(12), trackH * ((row.ExpandedHeight - S(38)) / (row.ExpandedHeight - S(38) + diagMaxScroll))), screenThumbY = HeaderH - _scrollY + trackY + (trackH - thumbH) * (currentScroll / diagMaxScroll);

                                if (_slideOffset > 0 && row.Y >= _slideStartY) screenThumbY += _slideOffset;

                                if (new RectangleF(WorkingAreaW - Pad - sbW - S(AppConfig.DiagScrollRightOffset), screenThumbY, sbW, thumbH).Contains(e.Location))
                                {
                                    _isDraggingDiagIdx = row.JobIndex; _dragStartY = e.Y; _dragStartScrollY = currentScroll; return; 
                                }
                            }
                        }
                    }
                }
            }
            if (e.Y <= HeaderH && !CloseBtnRect.Contains(e.Location)) { ReleaseCapture(); SendMessage(Handle, 0xA1, 0x2, 0); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _isDraggingMainScroll = false; _isDraggingDiagIdx = -1; }
            base.OnMouseUp(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            CalculateFormBounds(); // Recalculate scroll maxes based on your new drag height
            Invalidate(); // Purr-fectly redraw the elements so they anchor in real-time
        }

        private void OnMove(object? sender, MouseEventArgs e)
        {
            EnsureLayoutUpdated();

            if (_isDraggingMainScroll)
            {
                int viewHeight = Height - HeaderH - S(6); float maxScroll = Math.Max(0, _contentHeight + (int)_slideOffset - viewHeight);
                float scrollableTrack = (viewHeight - S(44)) - Math.Max(S(24), (viewHeight - S(44)) * (viewHeight / (float)(_contentHeight + (int)_slideOffset)));
                if (scrollableTrack > 0) { _targetScrollY = Math.Clamp(_dragStartScrollY + ((e.Y - _dragStartY) / scrollableTrack) * maxScroll, 0, maxScroll); _scrollY = _targetScrollY; Invalidate(); }
                return;
            }

            if (_isDraggingDiagIdx != -1)
            {
                float diagMaxScroll = _diagMaxScrolls.TryGetValue(_isDraggingDiagIdx, out float ms) ? ms : 0f;
                if (diagMaxScroll > 0)
                {
                    int expH = ExpandH;
                    foreach(var r in _layout) { if(!r.IsSeparator && r.JobIndex == _isDraggingDiagIdx) { expH = r.ExpandedHeight; break; } }
                    
                    float trackH = expH - S(AppConfig.DiagScrollTopPad) - S(AppConfig.DiagScrollBottomPad) - (S(AppConfig.DiagScrollWidth) * 2) - S(4);
                    float scrollableTrack = trackH - Math.Max(S(12), trackH * ((expH - S(35)) / (expH - S(35) + diagMaxScroll)));
                    if (scrollableTrack > 0) { _diagScrolls[_isDraggingDiagIdx] = Math.Clamp(_dragStartScrollY + ((e.Y - _dragStartY) / scrollableTrack) * diagMaxScroll, 0, diagMaxScroll); Invalidate(); }
                }
                return;
            }

            bool newHoverClose = CloseBtnRect.Contains(e.Location);
            if (_hoverClose != newHoverClose) { _hoverClose = newHoverClose; Invalidate(); }

            int prev = _hoverLayoutIdx, prevCopy = _hoverCopyJobIdx, prevDiag = _hoverDiagJobIdx, prevDelGroup = _hoverDeleteGroupIdx, prevTrash = _hoverTrashJobIdx;
            _hoverLayoutIdx = -1; _hoverCopyJobIdx = -1; _hoverDiagJobIdx = -1; _hoverDeleteGroupIdx = -1; _hoverTrashJobIdx = -1;

            if (e.Y > HeaderH)
            {
                float contentY = e.Y - HeaderH + _scrollY;
                if (_slideOffset > 0 && contentY >= _slideStartY) contentY -= _slideOffset;

                if (GetHitRow(contentY, out int li, out RowLayout row))
                {
                    if (row.IsSeparator) { 
                        if (DeleteBtnRect(row).Contains(e.X, (int)contentY)) _hoverDeleteGroupIdx = li; 
                        else _hoverLayoutIdx = li; 
                    }
                    else if (row.JobIndex >= 0 && row.JobIndex < _jobs.Count)
                    {
                        var job = _jobs[row.JobIndex];
                        int childPad = Pad + S(18);
                        int childW = WorkingAreaW - childPad - Pad;

                        var copyBtn = CopyBtnRect(job, row.Y, childPad, childW);
                        if (copyBtn.HasValue && copyBtn.Value.Contains(e.X, (int)contentY)) { _hoverCopyJobIdx = row.JobIndex; }
                        else
                        {
                            var trashBtn = TrashBtnRect(job, row.Y, childPad, childW);
                            if (trashBtn.HasValue && trashBtn.Value.Contains(e.X, (int)contentY)) { _hoverTrashJobIdx = row.JobIndex; }
                            else if (DiagBtnRect(job, row.Y, childPad, childW).Contains(e.X, (int)contentY)) { _hoverDiagJobIdx = row.JobIndex; }
                            else
                            {
                                var b = ActionBtnRect(job, row.Y, childPad, childW); 
                                if (b.HasValue && b.Value.Contains(e.X, (int)contentY)) { _hoverLayoutIdx = li; }
                            }
                        }
                    }
                }
            }

            if (_hoverLayoutIdx != prev || _hoverCopyJobIdx != prevCopy || _hoverDiagJobIdx != prevDiag || _hoverDeleteGroupIdx != prevDelGroup || _hoverTrashJobIdx != prevTrash) Invalidate();
        }

        private void OnClick(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (CloseBtnRect.Contains(e.Location)) { Close(); return; }

            if (_purgeAnimFrames > 0) return; 

            EnsureLayoutUpdated();

            if (e.Y > HeaderH)
            {
                float contentY = e.Y - HeaderH + _scrollY;
                if (_slideOffset > 0 && contentY >= _slideStartY) contentY -= _slideOffset;

                if (GetHitRow(contentY, out int li, out RowLayout row))
                {
                    if (row.IsSeparator)
                    {
                        if (DeleteBtnRect(row).Contains(e.X, (int)contentY))
                        {
                            if (row.GroupHasResend)
                            {
                                DateTime? currentGroupAnchor = null; string currentGroupText = ""; bool? lastWasUpdate = null;
                                foreach(var thisJob in _jobs) 
                                {
                                    bool isNewGroup = currentGroupAnchor == null || thisJob.QueuedAt.Date != currentGroupAnchor.Value.Date || (lastWasUpdate.HasValue && lastWasUpdate.Value != thisJob.IsUpdate);
                                    if (isNewGroup) { currentGroupAnchor = thisJob.QueuedAt; currentGroupText = thisJob.QueuedAt.ToString("MMM dd, yyyy").ToUpper(); }
                                    if (currentGroupText == row.GroupText && thisJob.CanRetry) _onRetry(thisJob);
                                    lastWasUpdate = thisJob.IsUpdate;
                                }
                            }
                            else { _purgingGroupText = row.GroupText; _purgeAnimFrames = MaxPurgeFrames; Invalidate(); }
                        }
                        else
                        {
                            if (_expandedLogs.Contains(row.GroupText)) {
                                _expandedLogs.Remove(row.GroupText);
                                foreach(var childJob in _jobs) {
                                    if (childJob.QueuedAt.ToString("MMM dd, yyyy").ToUpper() == row.GroupText && childJob.Status != UploadStatus.UpdateReady) childJob.IsExpanded = false;
                                }
                            } 
                            else { 
                                _expandedLogs.Add(row.GroupText); 
                                // Ensure all jobs in this newly opened group are collapsed by default, except Updates
                                foreach(var childJob in _jobs) {
                                    if (childJob.QueuedAt.ToString("MMM dd, yyyy").ToUpper() == row.GroupText) {
                                        childJob.IsExpanded = childJob.IsUpdate;
                                    }
                                }
                            }
                            _layoutNeedsUpdate = true; EnsureLayoutUpdated(); Invalidate(); return;
                        }
                    }
                    else if (row.JobIndex >= 0 && row.JobIndex < _jobs.Count)
                    {
                        var job = _jobs[row.JobIndex];
                        int childPad = Pad + S(18);
                        int childW = WorkingAreaW - childPad - Pad;

                        var copyBtn = CopyBtnRect(job, row.Y, childPad, childW);
                        if (copyBtn.HasValue && copyBtn.Value.Contains(e.X, (int)contentY))
                        {
                            string diagContent = GetDiagContent(job);
                            try { Clipboard.SetText(diagContent); } catch { /* silent */ }
                            Color thC = job.Status switch { UploadStatus.Failed or UploadStatus.Cancelled => CBarFail, UploadStatus.Done => CGreen, UploadStatus.UpdateReady => Color.FromArgb(180, 100, 220), _ => CGoldBrt };
                            _copyBubbles.Add(new CopyBubble { X = e.X, Y = e.Y, Alpha = 255f, ThemeColor = thC }); Invalidate(); return;
                        }

                        var trashBtn = TrashBtnRect(job, row.Y, childPad, childW);
                        if (trashBtn.HasValue && trashBtn.Value.Contains(e.X, (int)contentY))
                        {
                            _purgingJobRef = job; _purgeAnimFrames = MaxJobPurgeFrames; Invalidate(); return;
                        }

                        if (DiagBtnRect(job, row.Y, childPad, childW).Contains(e.X, (int)contentY)) { job.IsExpanded = !job.IsExpanded; _layoutNeedsUpdate = true; EnsureLayoutUpdated(); Invalidate(); return; }

                        var b = ActionBtnRect(job, row.Y, childPad, childW);
                        if (b.HasValue && b.Value.Contains(e.X, (int)contentY))
                        {
                            if (job.IsUpdate && job.Status == UploadStatus.UpdateReady) _onApply(job);
                            else { if (job.CanRetry || job.Status == UploadStatus.Done) _onRetry(job); else if (job.CanCancel) _onCancel(job); }
                            Invalidate(); return;
                        }
                    }
                }
            }
        }

        private string Trunc(string text, Graphics g, Font f, float maxW)
        {
            if (_truncCache.Count > 200) _truncCache.Clear(); 
            string cacheKey = text + "_" + maxW; if (_truncCache.TryGetValue(cacheKey, out string cachedVal)) return cachedVal;
            if (g.MeasureString(text, f).Width <= maxW) { _truncCache[cacheKey] = text; return text; }
            var ext = System.IO.Path.GetExtension(text); var noExt = System.IO.Path.GetFileNameWithoutExtension(text);
            while (noExt.Length > 3 && g.MeasureString(noExt + "…" + ext, f).Width > maxW) noExt = noExt[..^1];
            string result = noExt + "…" + ext; _truncCache[cacheKey] = result; return result;
        }

        public enum LightFilter { None, Natural, Dusky, Cool }
        /// <summary>
        /// Brightens a color by smoothly interpolating it towards a specific light source.
        /// <param name="amount">0.0f (no change) to 1.0f (maximum highlight)</param>
        /// </summary>
        private Color BrightenColor(Color color, float amount, LightFilter filter = LightFilter.None, int? overrideAlpha = null)
        {
            amount = Math.Clamp(amount, 0f, 1f);
            
            // Define what color "light" is hitting the surface
            Color target = filter switch {
                LightFilter.Natural => Color.FromArgb(255, 250, 235), // Warm ambient sunlight
                LightFilter.Dusky   => Color.FromArgb(255, 190, 130), // Sunset orange/gold
                LightFilter.Cool    => Color.FromArgb(220, 240, 255), // Crisp bright sky
                _                   => Color.FromArgb(255, 255, 255)  // Pure white (Default)
            };

            int r = (int)(color.R + ((target.R - color.R) * amount));
            int g = (int)(color.G + ((target.G - color.G) * amount));
            int b = (int)(color.B + ((target.B - color.B) * amount));
            int a = overrideAlpha ?? color.A;
            
            return Color.FromArgb(Math.Clamp(a, 0, 255), r, g, b);
        }

        /// <summary>
        /// Darkens a color by smoothly interpolating it towards a specific shadow tone.
        /// <param name="amount">0.0f (no change) to 1.0f (maximum shadow)</param>
        /// </summary>
        private Color DarkenColor(Color color, float amount, LightFilter filter = LightFilter.None, int? overrideAlpha = null)
        {
            amount = Math.Clamp(amount, 0f, 1f);
            
            // Define what color the "shadows" are in this environment
            Color target = filter switch {
                LightFilter.Natural => Color.FromArgb(15, 20, 35),    // Deep twilight blue
                LightFilter.Dusky   => Color.FromArgb(35, 15, 20),    // Rich purplish-brown
                LightFilter.Cool    => Color.FromArgb(10, 15, 25),    // Deep icy navy
                _                   => Color.FromArgb(0, 0, 0)        // Pure black (Default)
            };

            int r = (int)(color.R + ((target.R - color.R) * amount));
            int g = (int)(color.G + ((target.G - color.G) * amount));
            int b = (int)(color.B + ((target.B - color.B) * amount));
            int a = overrideAlpha ?? color.A;
            
            return Color.FromArgb(Math.Clamp(a, 0, 255), r, g, b);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _anim.Stop(); _anim.Dispose(); _bgBrush?.Dispose(); _fTitle28Bold?.Dispose(); _fTitle125Bold?.Dispose(); _fTitle10Bold?.Dispose(); _fTitle95?.Dispose(); _fBody95Italic?.Dispose(); _fBody8Bold?.Dispose(); _fBody8Italic?.Dispose(); _fBody75Bold?.Dispose(); _fBody75Italic?.Dispose(); _fBody75Reg?.Dispose(); _fBody7Bold?.Dispose(); _fMono9Bold?.Dispose(); _fMono9?.Dispose(); _fMono8Bold?.Dispose(); _fMono8?.Dispose(); _fMono75Bold?.Dispose(); _sfCenter?.Dispose(); _sfLeft?.Dispose(); _fBody8Bold?.Dispose(); _fBody8Reg?.Dispose(); }
            base.Dispose(disposing);
        }
    }
}