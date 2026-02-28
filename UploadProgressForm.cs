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
            public static int AnimIntervalMs = 45; 
            
            public const int MaxLogsKept = 8;
            public const int GlitchChancePer1k = 5;  
            public const int HeavyGlitchPct = 15;    
            public const float ScanlineSpeed = 0.56f;
            public const int MarqueePause = 150;     
            public const float ShimmerSpeed = 0.35f; 
            public const float ScrollFriction = 0.35f;
            public const float ScrollWheelSpeedMain = 0.9f;
            public const float ScrollWheelSpeedDiag = 0.85f;
            public const float CopyBubbleFloatSpeed = 1.45f; 
            public const float CopyBubbleFadeSpeed = 4.8f;   
            
            public enum FidelityMode { Low, Medium, High }
            public static FidelityMode CurrentMode = FidelityMode.Medium;

            public static void SetMode(FidelityMode mode)
            {
                CurrentMode = mode;
                
                if (mode == FidelityMode.Low)
                {
                    AnimIntervalMs = 60;
                    SetAll(false);
                }
                else if (mode == FidelityMode.High)
                {
                    AnimIntervalMs = 33;
                    SetAll(true);
                }
                else 
                {
                    AnimIntervalMs = 45;
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

        private const int BaseW       = 400;
        private const int BaseHeaderH = 68; 
        private const int BaseRowH    = 106;  
        private const int BaseExpandH = 125; 
        private const int BaseBarH    = 10;
        private const int BasePad     = 18;
        private const int BaseBtnW    = 68;   
        private const int BaseBtnH    = 24;   
        private const int BaseDiagH   = 20;   
        private const int BaseEmptyH  = 90;
        private const int MaxRows     = 5;
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

        private SolidBrush _bgBrush;
        private Font _fTitle28Bold, _fTitle125Bold, _fTitle10Bold, _fTitle95;
        private Font _fBody95Italic, _fBody8Bold, _fBody8Italic, _fBody75Bold, _fBody75Italic, _fBody75Reg, _fBody7Bold;
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
        private int _purgingJobIdx = -1;
        private int _purgeAnimFrames = 1;
        private const int MaxPurgeFrames = 4; 
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
        }

        private struct CopyBubble { public float X, Y, Alpha; public Color ThemeColor; }
        private readonly List<CopyBubble> _copyBubbles = new();

        private int   _glowAlpha = 80, _glowStep = 4;
        private Color _coreColor = CGreen, _auraColor = Color.FromArgb(240, 150, 40);
        private readonly Action<UploadJob> _onApply;
        
        private Rectangle CloseBtnRect => new Rectangle(Width - Pad - S(28), (HeaderH - S(28)) / 2, S(28), S(28));
        
        private int RightGutterW => S(26);
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
            _fBody95Italic = Body(9.5f, _scale, FontStyle.Italic); _fBody8Bold = Body(8f, _scale, FontStyle.Bold);
            _fBody8Italic = Body(8f, _scale, FontStyle.Italic); _fBody75Bold = Body(7.5f, _scale, FontStyle.Bold);
            _fBody75Italic = Body(7.5f, _scale, FontStyle.Italic); _fBody75Reg = Body(7.5f, _scale, FontStyle.Regular);
            _fBody7Bold = Body(7f, _scale, FontStyle.Bold);
            
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
        }

        public void PositionAboveTray()
        {
            EnsureLayoutUpdated();
            var wa = Screen.PrimaryScreen?.WorkingArea ?? Screen.AllScreens[0].WorkingArea;
            Location = new Point(wa.Right - FormW - S(10), wa.Bottom - Height - S(10));
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
                    if (_purgingJobIdx != -1)
                    {
                        var job = _jobs[_purgingJobIdx];
                        int startY = -1;
                        int purgedHeight = 0;
                        foreach(var r in _layout) {
                            if (!r.IsSeparator && r.JobIndex == _purgingJobIdx) {
                                startY = r.Y; purgedHeight = r.Height + S(12); break;
                            }
                        }
                        _slideStartY = startY; _slideOffset = purgedHeight;
                        if (job.CanCancel && (job.Status == UploadStatus.Uploading || job.Status == UploadStatus.Queued)) _onCancel(job);
                        _jobs.RemoveAt(_purgingJobIdx);
                        _purgingJobIdx = -1;
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
                                || Math.Abs((thisJob.QueuedAt - currentGroupAnchor.Value).TotalSeconds) > 5
                                || (lastWasUpdate.HasValue && lastWasUpdate.Value != thisJob.IsUpdate);
                
                if (isNewGroup) 
                {
                    currentGroupAnchor = thisJob.QueuedAt;
                    currentGroupText = thisJob.QueuedAt.ToString("MMM dd, h:mm:ss tt");
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

            _spinPhase += 0.15f; 

            for (int i = _copyBubbles.Count - 1; i >= 0; i--)
            {
                var b = _copyBubbles[i];
                float velocityMulti = (b.Alpha / 255f) * 0.8f + 0.2f; 
                b.Y -= AppConfig.CopyBubbleFloatSpeed * velocityMulti;
                b.Alpha = Math.Max(0f, b.Alpha - AppConfig.CopyBubbleFadeSpeed);
                if (b.Alpha <= 0) _copyBubbles.RemoveAt(i);
                else _copyBubbles[i] = b;
            }

            if (_jobs.Count == 0) {
                _emptyStateAlpha = Math.Min(255f, _emptyStateAlpha + 8f);
            } else {
                _emptyStateAlpha = 0f;
            }

            return true; 
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
            
            _coreColor = hasError ? CBarFail : isUploading ? Color.FromArgb(60, 180, 220) : hasUpdate ? Color.FromArgb(180, 100, 220) : Color.FromArgb(80, 200, 120);
            _auraColor = hasError ? CBarFail : isUploading ? Color.FromArgb(60, 180, 220) : hasUpdate ? Color.FromArgb(200, 120, 240) : Color.FromArgb(240, 150, 40);

            _glowStep = _glowStep > 0 ? targetStep : -targetStep;
            _glowAlpha += _glowStep;
            if (_glowAlpha >= 240) { _glowAlpha = 240; _glowStep = -targetStep; }
            if (_glowAlpha <= 40) { _glowAlpha = 40; _glowStep = targetStep; }

            return prevAlpha != _glowAlpha;
        }

        private string GetDiagContent(UploadJob job)
        {
            return job.Status switch {
                UploadStatus.Failed or UploadStatus.Cancelled => string.IsNullOrWhiteSpace(job.ErrorMessage) ? "No error detail was captured in this transmission." : job.ErrorMessage,
                UploadStatus.Done => "[ OK ] Signal verified. No anomalies in the transmission log.", 
                UploadStatus.Uploading => $"[ >> ] Active transmission in progress -- {job.Progress * 100:0}% complete.",
                UploadStatus.Queued => "[ -- ] Awaiting open transmission slot. Standing by.", 
                UploadStatus.UpdateReady => $"[ OK ] Matrix downloaded and verified.\n • Current Build : v{job.CurrentVersion}\n • Target Build  : v{job.UpdateVersion}\n • Payload Size  : {job.FileSizeDisplay}\n[ CHANGELOG ]\n{job.Changelog}\n\nReady for integration sequence.",                _ => "[ ?? ] Signal state unknown."
            };
        }

        private void EnsureLayoutUpdated()
        {
            if (!_layoutNeedsUpdate) return;

            _layout.Clear();
            int currentY = S(20); 

            var groupedJobs = new List<List<int>>();
            List<int>? currentGroup = null; 
            DateTime? groupStartTime = null; 
            bool? lastWasUpdate = null;

            for (int i = 0; i < _jobs.Count; i++)
            {
                var job = _jobs[i];
                bool isNewGroup = groupStartTime == null || Math.Abs((job.QueuedAt - groupStartTime.Value).TotalSeconds) > 10 || (lastWasUpdate.HasValue && lastWasUpdate.Value != job.IsUpdate);
                
                if (isNewGroup || currentGroup == null) 
                { 
                    currentGroup = new List<int>(); 
                    groupedJobs.Add(currentGroup); 
                    groupStartTime = job.QueuedAt; 
                }
                currentGroup.Add(i);
                lastWasUpdate = job.IsUpdate;
                currentGroup.Sort((a, b) => {
                    string nameA = _jobs[a].FileName ?? "";
                    string nameB = _jobs[b].FileName ?? "";
                    
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

                var firstJob = _jobs[group[0]];
                string currentGroupText = firstJob.QueuedAt.ToString("MMM dd, h:mm:ss tt");
                bool isExpanded = _expandedLogs.Contains(currentGroupText);
                
                bool isUpdateGrp = false;
                Color grpCol = GetJobStatusColor(firstJob); 
                
                foreach (int jIdx in group) 
                {
                    if (_jobs[jIdx].IsUpdate) isUpdateGrp = true;
                }

                if (g > 0) currentY += S(24); 

                int headerHeight = S(40); 
                _layout.Add(new RowLayout
                {
                    IsSeparator = true, SepText = currentGroupText, GroupText = currentGroupText,
                    Y = currentY, Height = headerHeight, GroupIsExpanded = isExpanded, IsUpdateGroup = isUpdateGrp,
                    GroupColor = grpCol
                });
                int sepIndex = _layout.Count - 1;
                currentY += headerHeight + S(2); 
                int groupStartY = currentY;

                if (isExpanded)
                {
                    for (int j = 0; j < group.Count; j++)
                    {
                        int jobIndex = group[j];
                        var job = _jobs[jobIndex];
                        int childW = WorkingAreaW - (Pad + S(18)) - Pad;
                        
                        int calculatedExpandH = ExpandH;
                        if (job.IsExpanded)
                        {
                            // We let our digital senses test the width first to see if a scrollbar will form
                            float textWNoScroll = childW - S(48);
                            var sz = dummyG.MeasureString(GetDiagContent(job), _fMono9, (int)textWNoScroll);
                            int neededH = (int)sz.Height + S(38);
                            calculatedExpandH = Math.Clamp(neededH, S(50), ExpandH);
                        }

                        int h = RowH + (job.IsExpanded ? calculatedExpandH : 0);
                        bool isLast = (j == group.Count - 1);

                        _layout.Add(new RowLayout
                        {
                            IsSeparator = false, GroupText = currentGroupText, JobIndex = jobIndex,
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
            _contentHeight = currentY + S(20); 
            CalculateFormBounds(); _layoutNeedsUpdate = false;
        }

        private void CalculateFormBounds()
        {
            int maxContentH = MaxRows * RowH;
            
            int effectiveContentH = _contentHeight + (int)_slideOffset;
            if (_jobs.Count == 0) effectiveContentH = Math.Max(EmptyH, effectiveContentH);
            int visibleContentH = Math.Min(effectiveContentH, maxContentH);
            
            int nh = HeaderH + visibleContentH + S(6);
            if (Height != nh || Width != FormW) { Height = nh; Width = FormW; }
            
            float maxScroll = Math.Max(0, effectiveContentH - visibleContentH);
            if (_targetScrollY > maxScroll) _targetScrollY = maxScroll;
            if (_scrollY > maxScroll) _scrollY = maxScroll;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Shift | Keys.P)) { InjectPhantomData(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void InjectPhantomData()
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

            DateTime activeLogTime = DateTime.Now.AddMinutes(-2);
            DateTime updateLogTime = DateTime.Now.AddMinutes(-1); 
            
            var activeUploadJob = new UploadJob { FileName = "GS01Store.lua", Status = UploadStatus.Uploading, Progress = 0.15f, QueuedAt = activeLogTime.AddSeconds(5) };

            _jobs.Add(new UploadJob { FileName = "TexturePack_Vol1.zip", Status = UploadStatus.Queued, Progress = 0f, QueuedAt = activeLogTime });
            _jobs.Add(activeUploadJob);
            _jobs.Add(new UploadJob { FileName = "GS02Store.lua", Status = UploadStatus.Failed, Progress = 0.6f, QueuedAt = activeLogTime.AddSeconds(15), ErrorMessage = "Connection severed by lunar interference. Database returned mismatch logic sequence out of bounds.", IsExpanded = true });
            _jobs.Add(new UploadJob { FileName = "GS03Store.lua", Status = UploadStatus.Done, Progress = 1f, QueuedAt = activeLogTime.AddSeconds(15), ErrorMessage = "Connection severed by lunar interference. Database returned mismatch logic sequence out of bounds.", IsExpanded = false });
            _jobs.Add(new UploadJob { FileName = "GS04Store.lua", Status = UploadStatus.Cancelled, Progress = 1f, QueuedAt = activeLogTime.AddSeconds(15), ErrorMessage = "Connection severed by lunar interference. Database returned mismatch logic sequence out of bounds.", IsExpanded = false });
            _jobs.Add(new UploadJob { FileName = "GS05Store.lua", Status = UploadStatus.UpdateReady, Progress = 1f, QueuedAt = activeLogTime.AddSeconds(15), ErrorMessage = "Connection severed by lunar interference. Database returned mismatch logic sequence out of bounds.", IsExpanded = false });
            _jobs.Add(new UploadJob { FileName = "Fissal Matrix v1.2.0", IsUpdate = true, UpdateVersion = "1.2.0", Status = UploadStatus.UpdateReady, Progress = 1f, FileSizeBytes = 148500000, QueuedAt = updateLogTime, IsExpanded = true });

            _expandedLogs.Add(activeLogTime.ToString("MMM dd, h:mm:ss tt"));
            _expandedLogs.Add(updateLogTime.ToString("MMM dd, h:mm:ss tt"));

            _layoutNeedsUpdate = true; 
            EnsureLayoutUpdated(); 

            _phantomTimer = new System.Windows.Forms.Timer { Interval = 150 };
            _phantomTimer.Tick += (s, e) => {
                if (_jobs.Contains(activeUploadJob) && activeUploadJob.Status == UploadStatus.Uploading) 
                {
                    activeUploadJob.Progress += 0.015f;
                    if (activeUploadJob.Progress >= 1f) 
                    { 
                        activeUploadJob.Progress = 1f; 
                        activeUploadJob.Status = UploadStatus.Done; 
                        _phantomTimer.Stop(); 
                    }
                    Invalidate();
                }
                else
                {
                    _phantomTimer.Stop();
                }
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
            DrawHeader(g);

            if (_jobs.Count == 0 && _slideOffset <= 0) { DrawEmpty(g); }
            else
            {
                int viewHeight = Height - HeaderH - S(6);
                var clipRect = new Rectangle(0, HeaderH, Width, viewHeight);
                g.SetClip(clipRect); 
                g.TranslateTransform(_globalGlitchX, HeaderH - _scrollY + _globalGlitchY);

                for (int i = 0; i < _layout.Count; i++)
                {
                    var row = _layout[i];
                    float effectiveHeight = row.IsSeparator && row.GroupIsExpanded ? row.Height + row.GroupTotalHeight : row.Height;
                    
                    if (row.Y + effectiveHeight < _scrollY - _slideOffset) continue;
                    if (row.Y > _scrollY + viewHeight) break;

                    var gState = g.Save();

                    bool isPurgingGroup = _purgingGroupText != null && row.GroupText == _purgingGroupText;
                    bool isPurgingJob = !row.IsSeparator && _purgingJobIdx == row.JobIndex;
                    
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

                    if (row.IsSeparator) DrawSeparator(g, row, i, i == _hoverLayoutIdx);
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
                    using var screenScanPen = new Pen(Color.FromArgb(15, 0, 0, 15), 10);
                    for (float sy = HeaderH + _scanPhase; sy < Height; sy += 4)
                        g.DrawLine(screenScanPen, 0, sy, Width, sy);
                }

                if (AppConfig.FX.ScreenVignette)
                {
                    using var vignettePath = new GraphicsPath();
                    vignettePath.AddRectangle(clipRect);
                    using var pthGrBrush = new PathGradientBrush(vignettePath)
                    {
                        CenterColor = Color.Transparent, SurroundColors = new[] { Color.FromArgb(160, 0, 0, 0) }, FocusScales = new PointF(0.80f, 0.90f)
                    };
                    g.FillRectangle(pthGrBrush, clipRect);
                }

                if (AppConfig.FX.ScreenGlassShine)
                {
                    using var glassHighlight = new LinearGradientBrush(clipRect, Color.FromArgb(12, 255, 255, 255), Color.Transparent, LinearGradientMode.ForwardDiagonal);
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
            int offset = S(6), rSize = S(5);
            Point[] corners = { new Point(offset, offset), new Point(Width - offset - rSize, offset), new Point(offset, Height - offset - rSize), new Point(Width - offset - rSize, Height - offset - rSize) };
            using var shadowBrush = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
            using var hlBrush = new SolidBrush(Color.FromArgb(60, 255, 255, 255));
            using var coreBrush = new SolidBrush(Color.FromArgb(10, 8, 6)); 
            foreach (var pt in corners) {
                g.FillEllipse(hlBrush, pt.X + 1, pt.Y + 1, rSize, rSize); g.FillEllipse(shadowBrush, pt.X - 1, pt.Y - 1, rSize, rSize); g.FillEllipse(coreBrush, pt.X, pt.Y, rSize, rSize);
            }
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
            int boxH = row.GroupIsExpanded ? row.Height - S(4) : row.Height - S(8);
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

        private void DrawSeparator(Graphics g, RowLayout row, int layoutIdx, bool isHovered)
        {
            bool isExpanded = row.GroupIsExpanded, delHover = _hoverDeleteGroupIdx == layoutIdx, textHover = isHovered && !delHover; 

            if (layoutIdx > 0)
            {
                int lineY = row.Y - S(10);
                if (AppConfig.FX.LogHeaderPulse) {
                    using var groupSepGlow = new Pen(Color.FromArgb(40, row.GroupColor), S(5)); 
                    g.DrawLine(groupSepGlow, Pad + S(20), lineY, WorkingAreaW - Pad - S(20), lineY);
                }
                using var groupSepLine = new Pen(Color.FromArgb(60, CGoldBrt), S(2)) { DashStyle = DashStyle.Dot };
                g.DrawLine(groupSepLine, Pad + S(50), lineY, WorkingAreaW - Pad - S(50), lineY);
            }

            int boxX = Pad, boxY = row.Y + S(4), boxW = WorkingAreaW - Pad * 2, boxH = isExpanded ? row.Height - S(8) : row.Height - S(8);

            if (isExpanded && row.GroupTotalHeight > 0)
            {
                int bgX = boxX + S(10), bgY = boxY + boxH, bgW = boxW - S(16), bgH = row.GroupTotalHeight;
                Color treeColor = row.GroupColor;
                using var groupBgBrush = new SolidBrush(Color.FromArgb(30, treeColor.R, treeColor.G, treeColor.B));
                g.FillRectangle(groupBgBrush, bgX, bgY, bgW, bgH);
                using var groupBorderPen = new Pen(Color.FromArgb(100, treeColor.R, treeColor.G, treeColor.B), 2);
                g.DrawLine(groupBorderPen, bgX, bgY, bgX, bgY + bgH); g.DrawLine(groupBorderPen, bgX + bgW, bgY, bgX + bgW, bgY + bgH); g.DrawLine(groupBorderPen, bgX, bgY + bgH, bgX + bgW, bgY + bgH); 
            }

            if (row.IsUpdateGroup && AppConfig.FX.UpdateReadyPulse)
            {
                int pulseAlpha = (int)(Math.Sin(_shimmer * 0.20f) * 100 + 100); 
                for (int i = 1; i <= 3; i++) DrawFadingGlow(g, boxX - S(1), boxY - S(1), boxW + S(2), boxH + S(2), Color.FromArgb(pulseAlpha / i, 180, 100, 240), S(i * 3), isExpanded);
            }
            else if (!row.IsUpdateGroup && AppConfig.FX.LogHeaderPulse)
            {
                int pulseAlpha = (int)(Math.Sin(_shimmer * 0.04f) * 20 + 50); 
                DrawFadingGlow(g, boxX - S(1), boxY - S(1), boxW + S(2), boxH + S(2), Color.FromArgb(textHover ? 90 : pulseAlpha, row.GroupColor), S(4), isExpanded);
            }
            
            using var bgBrush = new SolidBrush(Color.FromArgb(6, 6, 8)); g.FillRectangle(bgBrush, boxX, boxY, boxW, boxH);

            Color borderColor = row.IsUpdateGroup ? Color.FromArgb(textHover ? 220 : 140, 160, 80, 220) : Color.FromArgb(textHover ? 220 : 140, row.GroupColor);
            using var borderPen = new Pen(borderColor, S(1));
            
            g.DrawRectangle(borderPen, boxX, boxY, boxW, boxH); 

            var sf = _fTitle10Bold;
            string text = row.IsUpdateGroup ? $"!! UPDATE AWAITING INTEGRATION   {(isExpanded ? "[-]" : "[+]")}" : $"> LOG: {row.SepText.ToUpper()}   {(isExpanded ? "[-]" : "[+]")}";
            Color mainColor = row.IsUpdateGroup ? (textHover ? Color.FromArgb(240, 180, 255) : Color.FromArgb(200, 130, 240)) : (textHover ? Color.White : CGoldBrt);
            
            var sz = g.MeasureString(text, sf);
            DrawGlowingText(g, text, sf, mainColor, boxX + S(8), boxY + (boxH - sz.Height) / 2f + S(1), AppConfig.FX.LogHeaderPulse || AppConfig.FX.UpdateReadyPulse ? (textHover ? 90 : 40) : 0);

            if (AppConfig.FX.GroupSepScanlines)
            {
                using var scanlinePen = new Pen(row.IsUpdateGroup ? Color.FromArgb(20, 180, 100, 220) : Color.FromArgb(15, CGreen), 1);
                for (int i = boxY + 2; i < boxY + boxH; i += 3) g.DrawLine(scanlinePen, boxX + 1, i, boxX + boxW - 1, i);
            }

            var delRect = DeleteBtnRect(row);
            using var delPath = RoundRect(delRect.X, delRect.Y, delRect.Width, delRect.Height, S(2));
            using var delBg = new SolidBrush(delHover ? Color.FromArgb(40, CBarFail) : Color.Transparent); g.FillPath(delBg, delPath);
            using var delPen = new Pen(delHover ? Color.FromArgb(255, 120, 120) : Color.FromArgb(100, CBarFail), 1); g.DrawPath(delPen, delPath);
            using var delTextBrush = new SolidBrush(delHover ? Color.White : Color.FromArgb(200, CBarFail)); g.DrawString("[ PURGE ]", _fBody7Bold, delTextBrush, delRect, _sfCenter);
        }

        private Color GetJobStatusColor(UploadJob j) => j.Status switch {
            UploadStatus.Queued => CTextSub, UploadStatus.Uploading => j.IsUpdate ? Color.FromArgb(180, 100, 220) : CGoldBrt,
            UploadStatus.Done => CGreen, UploadStatus.Failed => CBarFail, UploadStatus.Cancelled => CBarCancel,
            UploadStatus.UpdateReady => Color.FromArgb(180, 100, 220), _ => CTextSub
        };

        private void DrawRow(Graphics g, RowLayout rowInfo, int layoutIdx, UploadJob job, int idx, int y)
        {
            int childPad = Pad + S(18), childW = WorkingAreaW - childPad - Pad, totalH = RowH + (job.IsExpanded ? rowInfo.ExpandedHeight : 0);

            (string glyph, Color gc) = job.Status switch {
                UploadStatus.Queued => ("[--]", CTextSub), UploadStatus.Uploading => (job.IsUpdate ? "[ v]" : "[>>]", job.IsUpdate ? Color.FromArgb(180, 100, 220) : CGoldBrt),
                UploadStatus.Done => ("[✓]", CGreen), UploadStatus.Failed => ("[!!]", CBarFail), UploadStatus.Cancelled => ("[//]", CBarCancel), UploadStatus.UpdateReady => ("[^]", Color.FromArgb(180, 100, 220)), _ => ("[??]", CTextSub)
            };

            int lineX = Pad + S(2), midY = y + S(18); 
            
            int prevMidY = y - S(20); 
            Color prevColor = rowInfo.GroupColor;
            for(int k = layoutIdx - 1; k >= 0; k--) {
                if (_layout[k].IsSeparator) { prevMidY = _layout[k].Y + _layout[k].Height; break; }
                prevMidY = _layout[k].Y + S(18); 
                prevColor = GetJobStatusColor(_jobs[_layout[k].JobIndex]); 
                break;
            }

            int renderTop = (int)(_scrollY - _slideOffset);
            int clampedPrevMidY = Math.Max(prevMidY, renderTop);

            if (AppConfig.FX.ConnectionGradients) { 
                float distance = midY - clampedPrevMidY;
                if (distance > 0) {
                    using var vertBrush = new LinearGradientBrush(new PointF(lineX, clampedPrevMidY), new PointF(lineX, midY), prevColor, gc);
                    using var vertPen = new Pen(vertBrush, S(1)); 
                    g.DrawLine(vertPen, lineX, clampedPrevMidY, lineX, midY); 
                }
            }
            else { 
                using var solidPen = new Pen(gc, S(1)); 
                g.DrawLine(solidPen, lineX, clampedPrevMidY, lineX, midY); 
            }

            using var horizPen = new Pen(gc, S(1)); g.DrawLine(horizPen, lineX, midY, childPad - S(6), midY);

            if (job.IsExpanded)
            {
                int ey = y + RowH; 
                Color drawerBg = job.Status switch { UploadStatus.Failed or UploadStatus.Cancelled => Color.FromArgb(16, 4, 28), UploadStatus.Done or UploadStatus.Uploading => Color.FromArgb(4, 16, 28), UploadStatus.Queued => Color.FromArgb(18, 16, 28), UploadStatus.UpdateReady => Color.FromArgb(24, 12, 36), _ => CErrBg };
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
                    using var drawerShadow = new LinearGradientBrush(new Rectangle(childPad + 1, ey, childW - 2, S(10)), Color.FromArgb(110, 0, 0, 0), Color.Transparent, LinearGradientMode.Vertical);
                    g.FillRectangle(drawerShadow, childPad + 1, ey, childW - 2, S(10));
                }

                using var diagTitleBrush = new SolidBrush(diagTitleColor); g.DrawString("// DIAGNOSTICS:", _fBody75Italic, diagTitleBrush, new PointF(childPad + S(10), ey + S(8)));

                string diagContent = GetDiagContent(job);

                using var errMsgBrush = new SolidBrush(diagTextColor);
                
                // We let our digital senses test the width first to see if a scrollbar will form
                float textWNoScroll = childW - S(48);
                float textWWithScroll = childW - S(66); 
                
                var testSz = g.MeasureString(diagContent, _fMono9Bold, (int)textWNoScroll);
                bool willScroll = testSz.Height > (rowInfo.ExpandedHeight - S(38));
                
                float actualTextW = willScroll ? textWWithScroll : textWNoScroll;
                var diagTextRect = new RectangleF(childPad + S(10), ey + S(24), actualTextW, rowInfo.ExpandedHeight - S(38));
                var sz = g.MeasureString(diagContent, _fMono9Bold, (int)actualTextW);
                
                float maxScroll = Math.Max(0, sz.Height - diagTextRect.Height); _diagMaxScrolls[idx] = maxScroll; 
                float currentScroll = _diagScrolls.TryGetValue(idx, out float ds) ? ds : 0f;
                if (currentScroll > maxScroll) currentScroll = maxScroll; _diagScrolls[idx] = currentScroll; 

                var prevClip = g.Clip; g.SetClip(diagTextRect, CombineMode.Intersect); 
                g.DrawString(diagContent, _fMono9, errMsgBrush, new RectangleF(diagTextRect.X, diagTextRect.Y - currentScroll, diagTextRect.Width, sz.Height), StringFormat.GenericDefault); g.Clip = prevClip;

                bool hasScroll = maxScroll > 0;
                if (hasScroll)
                {
                    float sbW = S(10), sbX = childPad + childW - sbW - S(1);
                    float topBoxY = ey + S(8);
                    float botBoxY = ey + rowInfo.ExpandedHeight - S(8) - sbW;
                    float trackY = topBoxY + sbW + S(2);
                    float trackBottom = botBoxY - S(2);
                    float trackH = trackBottom - trackY;

                    float thumbH = Math.Max(S(12), trackH * (diagTextRect.Height / sz.Height)), thumbY = trackY + (trackH - thumbH) * (currentScroll / maxScroll);

                    if (currentScroll < maxScroll - 1)
                    {
                        using var fadeBrush = new LinearGradientBrush(new RectangleF(diagTextRect.X, diagTextRect.Bottom - S(16), diagTextRect.Width, S(16)), Color.FromArgb(0, drawerBg), drawerBg, LinearGradientMode.Vertical);
                        g.FillRectangle(fadeBrush, diagTextRect.X, diagTextRect.Bottom - S(16), diagTextRect.Width, S(16));
                        int arrowAlpha = (int)(Math.Sin(_shimmer * 0.2f) * 100 + 100);
                        using var indBrush = new SolidBrush(Color.FromArgb(arrowAlpha, diagTextColor));
                        g.DrawString("▼", _fMono8Bold, indBrush, new PointF(diagTextRect.X + diagTextRect.Width / 2f - S(5), diagTextRect.Bottom - S(14)));
                    }

                    using var sbTrackBrush = new SolidBrush(Color.FromArgb(30, drawerBorder)); g.FillRectangle(sbTrackBrush, sbX, trackY, sbW, trackH);
                    using var arrowPen = new Pen(diagTextColor, 1.5f * _scale); arrowPen.LineJoin = LineJoin.Round; 

                    g.DrawRectangle(arrowPen, sbX, topBoxY, sbW, sbW); 
                    g.DrawLines(arrowPen, new PointF[] { new PointF(sbX + S(3), topBoxY + S(7)), new PointF(sbX + sbW/2, topBoxY + S(4)), new PointF(sbX + sbW - S(3), topBoxY + S(7)) }); 
                    
                    g.DrawRectangle(arrowPen, sbX, botBoxY, sbW, sbW); 
                    g.DrawLines(arrowPen, new PointF[] { new PointF(sbX + S(3), botBoxY + S(4)), new PointF(sbX + sbW/2, botBoxY + S(7)), new PointF(sbX + sbW - S(3), botBoxY + S(4)) }); 

                    using var sbThumbBrush = new SolidBrush(diagTextColor); g.FillRectangle(sbThumbBrush, sbX + S(1), thumbY, sbW - S(2), thumbH);
                    using var ridgePen = new Pen(drawerBg, 1); float midT = thumbY + thumbH / 2;
                    g.DrawLine(ridgePen, sbX + S(2), midT - S(2), sbX + sbW - S(3), midT - S(2)); g.DrawLine(ridgePen, sbX + S(2), midT, sbX + sbW - S(3), midT); g.DrawLine(ridgePen, sbX + S(2), midT + S(2), sbX + sbW - S(3), midT + S(2));
                }

                DrawIconBtn(g, CopyBtnRect(rowInfo, hasScroll), gc, diagTextColor, _hoverCopyJobIdx == idx, AppConfig.FX.ButtonGlows, drawerBg, false);
            }

            Color baseCardBg = job.IsUpdate ? Color.FromArgb(20, 10, 30) : (idx % 2 == 1 ? Color.FromArgb(20, 15, 5) : Color.FromArgb(10, 8, 2));
            using var altBrush = new SolidBrush(baseCardBg); g.FillRectangle(altBrush, childPad, y, childW, RowH);

            using var blockBorderPen = new Pen(Color.FromArgb(60, CGoldDim), 1);
            if (job.IsExpanded) { g.DrawLine(blockBorderPen, childPad, y, childPad + childW - 1, y); g.DrawLine(blockBorderPen, childPad, y, childPad, y + RowH); g.DrawLine(blockBorderPen, childPad + childW - 1, y, childPad + childW - 1, y + RowH); }
            else { g.DrawRectangle(blockBorderPen, childPad, y, childW - 1, RowH - 1); }

            int badgeX = childPad + S(4), badgeY = y + S(8);
            
            if (AppConfig.FX.RowBadgeGlow) { using var badgeBgBrush = new SolidBrush(Color.FromArgb(28, gc)); g.FillRectangle(badgeBgBrush, badgeX, badgeY, S(26), S(21)); }
            using var badgeBorderPen = new Pen(Color.FromArgb(99, gc), 3); g.DrawRectangle(badgeBorderPen, badgeX, badgeY, S(26), S(20));
            
            if (AppConfig.FX.RowBadgeScanlines) {
                using var badgeScanPen = new Pen(Color.FromArgb(20, gc), 1);
                for (int si = badgeY + 2; si < badgeY + S(20); si += 3) g.DrawLine(badgeScanPen, badgeX + 1, si, badgeX + S(24), si);
            }

            var glyphSz = g.MeasureString(glyph, _fMono75Bold);
            DrawGlowingText(g, glyph, _fMono75Bold, gc, badgeX + (S(26) - glyphSz.Width) / 2f, badgeY + (S(21) - glyphSz.Height) / 2f, AppConfig.FX.RowBadgeGlow ? 50 : 0);

            var btn = BtnRect(job, y);
            if (btn.HasValue)
            {
                bool hoverAction = _hoverLayoutIdx >= 0 && _layout.Count > _hoverLayoutIdx && !_layout[_hoverLayoutIdx].IsSeparator && _layout[_hoverLayoutIdx].JobIndex == idx;
                bool canResend = job.CanRetry || job.Status == UploadStatus.Done, canApply = job.IsUpdate && job.Status == UploadStatus.UpdateReady;
                Color btnColor = canApply ? Color.FromArgb(180, 100, 220) : (canResend ? CGreen : CBarFail);

                if (canApply)
                {
                    float arrowBaseX = btn.Value.X - S(5), arrowY = btn.Value.Y + btn.Value.Height / 2, ah = S(14) * Math.Abs((float)Math.Cos(_spinPhase));
                    if (ah > 0.5f) { using var arrowBrush = new SolidBrush(CGreen); g.FillPolygon(arrowBrush, new PointF[] { new PointF(arrowBaseX - S(10), arrowY - ah/2), new PointF(arrowBaseX, arrowY), new PointF(arrowBaseX - S(10), arrowY + ah/2) }); }
                }
                DrawBtn(g, btn.Value, canApply ? "Apply" : (canResend ? "Re-send" : "Abort"), btnColor, btnColor, hoverAction, glow: (canApply && AppConfig.FX.ButtonGlows));
            }

            var trashBtn = TrashBtnRect(job, y);
            if (trashBtn.HasValue)
            {
                bool trashHover = _hoverTrashJobIdx == idx;
                DrawIconBtn(g, trashBtn.Value, gc, gc, trashHover, AppConfig.FX.ButtonGlows, baseCardBg, true);
            }

            DrawBtn(g, DiagToggleBtnRect(job, y), job.IsExpanded ? "[X]  DIAG" : "[-]  DIAG", job.IsExpanded ? gc : CGoldDim, job.IsExpanded ? gc : CGoldDim, _hoverDiagJobIdx == idx, glow: (job.IsExpanded && AppConfig.FX.ButtonGlows));

            DrawHazyText(g, "> " + Trunc(job.FileName, g, _fTitle95, RightBtnX - (childPad + S(36)) - S(40)), _fTitle95, job.Status == UploadStatus.UpdateReady ? Color.FromArgb(210, 160, 240) : CText, childPad + S(34), y + S(10));
            
            string detailText = job.IsUpdate ? $"> Payload: {job.FileSizeDisplay}\n> Target Build: v{job.UpdateVersion}" : $"> {job.FileSizeDisplay}\n> synced at {job.QueuedAt:h:mm tt}";
            var detailSz = g.MeasureString(detailText, _fBody8Italic);
            var detailRect = new RectangleF(childPad + S(5), y + S(32), detailSz.Width + S(50), detailSz.Height + S(6));
            
            using var infoBgPath = RoundRect(detailRect.X, detailRect.Y, detailRect.Width, detailRect.Height, S(1));
            using var infoBgBrush = new SolidBrush(Color.FromArgb(8, 12, 10));
            g.FillPath(infoBgBrush, infoBgPath);
            using var infoBorder = new Pen(Color.FromArgb(80, 75, 90), 1);
            g.DrawPath(infoBorder, infoBgPath);
            using var detailTextBrush = new SolidBrush(Color.FromArgb(150, 150, 100));
            g.DrawString(detailText, _fBody8Italic, detailTextBrush, new PointF(detailRect.X + S(6), detailRect.Y + S(3)));

            float bx = childPad + S(6), bw = childW - S(16), by = y + RowH - BarH - S(8);
            string statusText = job.Status switch { UploadStatus.UpdateReady => "[RDY!] CLICK APPLY TO UPDATE", UploadStatus.Done => "[DONE] DATA VERIFIED", UploadStatus.Failed => "[ERR!] LUNAR STATIC", UploadStatus.Cancelled => "[VOID] SYNC ABORTED", UploadStatus.Queued => "[PEND] AWAITING ALIGNMENT", UploadStatus.Uploading => job.IsUpdate ? "[XMIT] PULLING NEW MATRIX..." : "[XMIT] TRANSMITTING...", _ => "[ ?? ] UNKNOWN SIGNAL" };

            using var statBrush = new SolidBrush(gc); g.DrawString(statusText, job.Status switch { UploadStatus.Done or UploadStatus.UpdateReady => _fBody75Bold, UploadStatus.Failed or UploadStatus.Cancelled => _fBody75Italic, _ => _fBody75Reg }, statBrush, new PointF(bx, by - S(14)));
            string pct = job.Status switch { UploadStatus.Done or UploadStatus.UpdateReady => "100%", UploadStatus.Failed => "ERR", UploadStatus.Cancelled => "N/A", UploadStatus.Queued => "0%", _ => $"{job.Progress * 100:0}%" };
            g.DrawString(pct, _fBody8Bold, statBrush, new PointF(bx + bw - g.MeasureString(pct, _fBody8Bold).Width, by - S(14)));

            using var track = RoundRect(bx, by, bw, BarH, S(3));
            using var trackBrush = new SolidBrush(Color.FromArgb(20, 15, 10)); g.FillPath(trackBrush, track);

            if (job.Status == UploadStatus.Queued && AppConfig.FX.BarPulseQueued)
            {
                using var pulseBrush = new SolidBrush(Color.FromArgb((int)(30 + ((float)(Math.Sin(_shimmer * 0.03f) + 1f) / 2f) * 100), Color.White)); g.FillPath(pulseBrush, track);
            }

            float fill = job.Status == UploadStatus.Done || job.Status == UploadStatus.UpdateReady ? bw : bw * job.Progress;
            if (fill > 1f && job.Status != UploadStatus.Queued)
            {
                Color fc = job.Status switch { UploadStatus.Done or UploadStatus.UpdateReady => CBarDone, UploadStatus.Failed => CBarFail, UploadStatus.Cancelled => CBarCancel, _ => CBarActive };
                using var fp = RoundRect(bx, by, fill, BarH, S(3));
                
                if (job.Status == UploadStatus.UpdateReady) { using var updateFill = new SolidBrush(Color.FromArgb(180, 100, 220)); g.FillPath(updateFill, fp); }
                else { using var fillBrush = new SolidBrush(fc); g.FillPath(fillBrush, fp); }

                var prevClip = g.Clip; g.SetClip(fp, CombineMode.Intersect); 

                if (job.Status == UploadStatus.Uploading && AppConfig.FX.BarEnergyUpload)
                {
                    using var energyPen = new Pen(Color.FromArgb(70, 255, 255, 255), S(3)); float offset = (_shimmer * 2.0f) % S(50);
                    for (float ex = bx - S(16) + offset; ex < bx + fill + S(16); ex += S(10)) g.DrawLine(energyPen, ex, by + BarH + S(2), ex + S(8), by - S(2));
                    using var leadGlow = new LinearGradientBrush(new RectangleF(bx + fill - S(20), by, S(20), BarH), Color.Transparent, Color.FromArgb(255, Color.Orange), LinearGradientMode.Horizontal);
                    if (fill > S(20)) g.FillRectangle(leadGlow, bx + fill - S(20), by, S(20), BarH);
                }
                else if ((job.Status == UploadStatus.Failed || job.Status == UploadStatus.Cancelled) && AppConfig.FX.BarStaticFailed)
                {
                    using var staticPen = new Pen(Color.FromArgb(40, 0, 0, 0), 2);
                    for (float ex = bx; ex < bx + fill; ex += S(2)) if (_rand.Next(10) > 4) g.DrawLine(staticPen, ex, by, ex, by + BarH);
                }
                else if ((job.Status == UploadStatus.Done || job.Status == UploadStatus.UpdateReady) && AppConfig.FX.BarCoolGlowDone)
                {
                    float offset = (_shimmer * 0.5f) % (bw + S(100));
                    if (offset < bw) { using var coolGlow = new LinearGradientBrush(new RectangleF(bx + offset, by, S(30), BarH), Color.Transparent, Color.FromArgb(60, 255, 255, 255), LinearGradientMode.Horizontal); g.FillRectangle(coolGlow, bx + offset, by, S(30), BarH); }
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

            using var btnPen = new Pen(hov || (glow && AppConfig.FX.ButtonGlows) ? Color.FromArgb(255, accent) : Color.FromArgb(120, accent), S(1));
            g.DrawPath(btnPen, path);
            
            using var textBrush = new SolidBrush(hov ? Color.White : txtColor); 
            g.DrawString(label, _fBody8Bold, textBrush, new RectangleF(r.X, r.Y, r.Width, r.Height), _sfCenter);
        }

        private void DrawIconBtn(Graphics g, Rectangle r, Color accent, Color txtColor, bool hov, bool glow, Color containerBg, bool isTrash)
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

            using var btnPen = new Pen(hov || (glow && AppConfig.FX.ButtonGlows) ? Color.FromArgb(255, accent) : Color.FromArgb(120, accent), S(1));
            g.DrawPath(btnPen, path);

            using var iconPen = new Pen(hov ? Color.White : txtColor, 1.5f * _scale);
            int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;

            if (isTrash)
            {
                int tW = S(8), tH = S(10), tX = cx - tW / 2, tY = cy - tH / 2 + S(1);
                g.DrawLine(iconPen, tX - S(2), tY, tX + tW + S(2), tY); 
                g.DrawLine(iconPen, tX + S(2), tY, tX + S(2), tY - S(2)); 
                g.DrawLine(iconPen, tX + tW - S(2), tY, tX + tW - S(2), tY - S(2)); 
                g.DrawLine(iconPen, tX + S(2), tY - S(2), tX + tW - S(2), tY - S(2)); 
                g.DrawRectangle(iconPen, tX, tY, tW, tH); 
                g.DrawLine(iconPen, tX + S(2), tY + S(2), tX + S(2), tY + tH - S(2)); 
                g.DrawLine(iconPen, tX + tW - S(2), tY + S(2), tX + tW - S(2), tY + tH - S(2)); 
            }
            else
            {
                int docW = Math.Max(S(8), r.Width / 2 - S(2)); 
                int docH = Math.Max(S(10), r.Height / 2);
                int docX = cx - docW / 2 + S(1);
                int docY = cy - docH / 2 + S(1);

                g.DrawRectangle(iconPen, docX - S(2), docY - S(2), docW, docH);
                using var maskBrush = new SolidBrush(containerBg); g.FillRectangle(maskBrush, docX, docY, docW, docH);
                
                if (glow && AppConfig.FX.ButtonGlows) { using var ig = new SolidBrush(Color.FromArgb((int)(Math.Sin(_shimmer * 0.1f) * 100 + 100) / 3, accent)); g.FillRectangle(ig, docX, docY, docW, docH); }
                if (hov) { using var hv = new SolidBrush(Color.FromArgb(45, accent)); g.FillRectangle(hv, docX, docY, docW, docH); }

                g.DrawRectangle(iconPen, docX, docY, docW, docH); 
                g.DrawLine(iconPen, docX + S(2), docY + S(2), docX + docW - S(2), docY + S(2)); 
                g.DrawLine(iconPen, docX + S(2), docY + S(5), docX + docW - S(2), docY + S(5)); 
                g.DrawLine(iconPen, docX + S(2), docY + S(8), docX + docW - S(3), docY + S(8));
            }
        }

        private Rectangle? BtnRect(UploadJob job, int y)
        {
            if (!job.CanRetry && !job.CanCancel && job.Status != UploadStatus.Done && job.Status != UploadStatus.UpdateReady) return null;
            return new Rectangle(RightBtnX, y + S(9), BtnW, BtnH);
        }

        private Rectangle? TrashBtnRect(UploadJob job, int y)
        {
            if (job.IsUpdate) return null;
            var b = BtnRect(job, y);
            int refX = b.HasValue ? b.Value.X : RightBtnX;
            
            int shortH = BtnH - S(4);
            return new Rectangle(refX - S(30), y + S(9) + S(2), S(25), shortH);
        }

        private Rectangle DiagToggleBtnRect(UploadJob job, int y) => new Rectangle(RightBtnX, (job.CanRetry || job.CanCancel || job.Status == UploadStatus.Done || job.Status == UploadStatus.UpdateReady) ? y + S(9) + BtnH + S(5) : y + S(9), BtnW, DiagH);

        private Rectangle CopyBtnRect(RowLayout row, bool hasScroll) 
        {
            int childPad = Pad + S(18);
            int childW = WorkingAreaW - childPad - Pad;
            int btnW = S(26), btnH = S(24);
            int rightOffset = hasScroll ? S(26) : S(8);
            return new Rectangle(childPad + childW - btnW - rightOffset, row.Y + RowH + S(6), btnW, btnH);
        }

        private void DrawHeader(Graphics g)
        {
            using var hg = new LinearGradientBrush(new Point(0, 0), new Point(0, HeaderH), Color.FromArgb(46, 36, 18), Color.FromArgb(20, 16, 9)); g.FillRectangle(hg, 0, 0, Width, HeaderH);
            using var bezelEdgePen = new Pen(Color.FromArgb(90, 255, 255, 255), 1); g.DrawLine(bezelEdgePen, 0, HeaderH - 2, Width, HeaderH - 2);
            using var bezelInnerPen = new Pen(Color.FromArgb(200, 0, 0, 0), 2); g.DrawLine(bezelInnerPen, 0, HeaderH - 1, Width, HeaderH - 1);

            int plateH = S(45), plateW = S(165), plateX = Pad-15, plateY = (HeaderH - plateH) / 2;
            using var platePath = RoundRect(plateX, plateY, plateW, plateH, S(4));
            using var plateBg = new LinearGradientBrush(new Rectangle(plateX, plateY, plateW, plateH), Color.FromArgb(12, 14, 16), Color.FromArgb(35, 40, 45), LinearGradientMode.Vertical); g.FillPath(plateBg, platePath);
            using var plateShadow = new Pen(Color.FromArgb(200, 0, 0, 0), S(2)); g.DrawPath(plateShadow, platePath);
            using var plateHi = new Pen(Color.FromArgb(40, 255, 255, 255), 1); g.DrawLine(plateHi, plateX + S(4), plateY + plateH, plateX + plateW - S(4), plateY + plateH);

            g.TranslateTransform(plateX, plateY);

            int rimSize = S(18), dx = S(10), dy = (plateH - rimSize) / 2;
            using var socketShadow = new SolidBrush(Color.FromArgb(220, 0, 0, 0)); g.FillEllipse(socketShadow, dx - S(1), dy - S(1), rimSize + S(2), rimSize + S(2));
            using var rimBrush = new LinearGradientBrush(new Rectangle(dx, dy, rimSize, rimSize), CGoldDim, CGoldBrt, LinearGradientMode.BackwardDiagonal); g.FillEllipse(rimBrush, dx, dy, rimSize, rimSize);

            if (AppConfig.FX.HeaderSocketGlow)
            {
                using var glowPen = new Pen(Color.FromArgb(Math.Min(255, _glowAlpha), _auraColor), S(3)); g.DrawEllipse(glowPen, dx - S(1), dy - S(1), rimSize + S(2), rimSize + S(2));
                using var reflectionBrush = new SolidBrush(Color.FromArgb(Math.Max(0, (int)(_glowAlpha * 0.45f)), _auraColor)); g.FillEllipse(reflectionBrush, dx, dy, rimSize, rimSize);
            }

            using var rimBorder = new Pen(Color.FromArgb(30, 20, 10), S(1)); g.DrawEllipse(rimBorder, dx, dy, rimSize, rimSize);

            int glassPad = S(2), gSize = rimSize - glassPad * 2, gx = dx + glassPad, gy = dy + glassPad;
            using var shadowBrush = new SolidBrush(Color.FromArgb(180, 5, 5, 5)); g.FillEllipse(shadowBrush, gx, gy, gSize, gSize);
            using var coreBrush = new SolidBrush(Color.FromArgb(Math.Max(50, _glowAlpha), _coreColor)); g.FillEllipse(coreBrush, gx, gy, gSize, gSize);
            
            using var innerShadowPath = new GraphicsPath(); innerShadowPath.AddEllipse(gx, gy, gSize, gSize);
            using var innerShadow = new PathGradientBrush(innerShadowPath) { CenterColor = Color.Transparent, SurroundColors = new[] { Color.FromArgb(180, 0, 0, 0) }, FocusScales = new PointF(0.8f, 0.8f) }; g.FillEllipse(innerShadow, gx, gy, gSize, gSize);

            if (AppConfig.FX.HeaderGlassGlint)
            {
                using var glintPath = new GraphicsPath(); glintPath.AddEllipse(gx + S(2), gy + S(2), gSize - S(4), gSize - S(4));
                using var glassGlintBrush = new LinearGradientBrush(new Rectangle(gx, gy, gSize, gSize), Color.FromArgb(180, 255, 255, 255), Color.Transparent, LinearGradientMode.ForwardDiagonal); g.FillPath(glassGlintBrush, glintPath);
            }

            float titleX = dx + rimSize + S(6), titleY = S(4); Color neonColor = Color.FromArgb(255, 255, 170, 80); 
            
            if (AppConfig.FX.HeaderNeonText)
            {
                using var outerGlow = new SolidBrush(Color.FromArgb((int)(Math.Sin(_shimmer * 0.15f) * 40 + 60), neonColor));
                g.DrawString("Fissal Relay", _fTitle125Bold, outerGlow, new PointF(titleX - S(2), titleY - S(2))); g.DrawString("Fissal Relay", _fTitle125Bold, outerGlow, new PointF(titleX + S(2), titleY + S(2))); g.DrawString("Fissal Relay", _fTitle125Bold, outerGlow, new PointF(titleX + S(2), titleY - S(2))); g.DrawString("Fissal Relay", _fTitle125Bold, outerGlow, new PointF(titleX - S(2), titleY + S(2)));
                DrawGlowingText(g, "Fissal Relay", _fTitle125Bold, neonColor, titleX, titleY, 180);
            }

            using var coreNeon = new SolidBrush(Color.FromArgb(255, 255, 255, 200)); g.DrawString("Fissal Relay", _fTitle125Bold, coreNeon, new PointF(titleX, titleY));

            float subX = dx + rimSize + S(5), subY = S(26); string subText = $"Masser Matrix v{(System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(2) ?? "1.00")}";
            using var subIndentShadow = new SolidBrush(Color.FromArgb(255, 20, 10, 0)); g.DrawString(subText, _fBody95Italic, subIndentShadow, new PointF(subX-2, subY - 2));
            using var subIndentHi = new SolidBrush(Color.FromArgb(170, 190, 190, 190)); g.DrawString(subText, _fBody95Italic, subIndentHi, new PointF(subX, subY + 2));
            using var subCore = new SolidBrush(Color.FromArgb(100, CGoldMid.R, CGoldMid.G, CGoldMid.B)); g.DrawString(subText, _fBody95Italic, subCore, new PointF(subX, subY));
            g.ResetTransform(); 

            int mcW = S(155), mcH = S(34), mcX = CloseBtnRect.Left - mcW - S(15);
            int mcY = plateY + plateH - mcH; 
            using var mcPath = RoundRect(mcX, mcY, mcW, mcH, S(4));
            using var mcBg = new SolidBrush(Color.FromArgb(255, 4, 12, 6)); g.FillPath(mcBg, mcPath);

            using var mcInnerShadow = new LinearGradientBrush(new Rectangle(mcX, mcY, mcW, mcH), Color.FromArgb(220, 5, 10, 15), Color.Transparent, LinearGradientMode.Vertical); mcInnerShadow.SetBlendTriangularShape(0.2f); g.FillPath(mcInnerShadow, mcPath);

            if (AppConfig.FX.MCScanlines) { using var mcScanPen = new Pen(Color.FromArgb(20, CGreen), 1); for (int sy = mcY + 2; sy < mcY + mcH; sy += 3) g.DrawLine(mcScanPen, mcX + 1, sy, mcX + mcW - 2, sy); }

            using var mcBorder = new Pen(Color.FromArgb(255, 12, 14, 16), S(3)); g.DrawPath(mcBorder, mcPath);
            using var mcHighlight = new Pen(Color.FromArgb(50, 255, 255, 255), 1); g.DrawLine(mcHighlight, mcX + S(3), mcY + mcH + S(1), mcX + mcW - S(3), mcY + mcH + S(1));

            var statuses = new List<(string text, Color color, int type)>(); 
            bool hasReadyUpdate = false, hasError = false; string errorFile = ""; int active = 0, pending = 0;

            foreach (var j in _jobs) 
            {
                if (j.Status == UploadStatus.UpdateReady) hasReadyUpdate = true;
                if (j.Status == UploadStatus.Failed || j.Status == UploadStatus.Cancelled) { hasError = true; if (string.IsNullOrEmpty(errorFile)) errorFile = j.FileName; }
                if (j.Status == UploadStatus.Uploading) active++;
                if (j.Status == UploadStatus.Queued) pending++;
            }

            if (hasError) statuses.Add(($"!! ERROR IN {errorFile.ToUpper()} !!", Color.FromArgb(255, 255, 40, 40), 2));
            if (hasReadyUpdate) statuses.Add(("!! UPDATE AVAILABLE! CHECK LOGS TO APPLY", Color.FromArgb(255, 200, 60, 255), 3));
            if (active > 0) statuses.Add(($"> {active} FILES SYNCING", Color.FromArgb(255, 255, 200, 0), 1));
            else if (pending > 0) statuses.Add(($"# {pending} FILES PENDING", Color.FromArgb(255, 180, 255, 50), 1));
            
            if (statuses.Count == 0) statuses.Add((_jobs.Count == 0 ? "> STAND BY..." : $"> {_jobs.Count} FILES SYNCED", Color.FromArgb(255, 50, 255, 50), 0));

            if (_dispStatusIdx >= statuses.Count) _dispStatusIdx = 0;
            var currentStatus = statuses[_dispStatusIdx];
            string displayBadge = currentStatus.text; Color badgeColor = currentStatus.color;

            int lightDia = S(10), lightSpacing = S(20), lightsY = (mcY - lightDia) / 2, leftLightsStartX = mcX + S(6); 
            Color[] lightColors = { Color.FromArgb(255, 20, 255, 20), Color.FromArgb(255, 255, 220, 0), Color.FromArgb(255, 255, 30, 30), Color.FromArgb(255, 220, 50, 255) };

            for (int i = 0; i < 4; i++)
            {
                bool isActive = statuses.Exists(s => s.type == i), isCurrent = currentStatus.type == i;
                int cx = leftLightsStartX + (i * lightSpacing);
                if (i == 3) cx += S(12); // Extra gap for the update light
                
                using var offBrush = new LinearGradientBrush(new Rectangle(cx, lightsY, lightDia, lightDia), 
                    Color.FromArgb(70, lightColors[i].R/3, lightColors[i].G/3, lightColors[i].B/3), 
                    Color.FromArgb(40, 15, 15, 15), LinearGradientMode.Vertical); 
                g.FillEllipse(offBrush, cx, lightsY, lightDia, lightDia);
                
                using var rim = new Pen(Color.FromArgb(90, lightColors[i].R/2, lightColors[i].G/2, lightColors[i].B/2), 1); 
                g.DrawEllipse(rim, cx, lightsY, lightDia, lightDia);

                if (isActive && AppConfig.FX.MCLightsGlow)
                {
                    int alpha = (int)((isCurrent ? (float)((Math.Sin(_shimmer * 0.45f) + 1.0) / 2.0) : 1f) * 200 + 55); 
                    using var litBrush = new SolidBrush(Color.FromArgb(alpha, lightColors[i])); 
                    g.FillEllipse(litBrush, cx + 1, lightsY + 1, lightDia - 2, lightDia - 2);
                    
                    using var whiteCore = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255));
                    g.FillEllipse(whiteCore, cx + S(2), lightsY + S(2), lightDia - S(4), lightDia - S(4));

                    using var glowH = new SolidBrush(Color.FromArgb(alpha / 4, lightColors[i])); g.FillEllipse(glowH, cx - S(3), lightsY, lightDia + S(6), lightDia); 
                    using var glowV = new SolidBrush(Color.FromArgb(alpha / 5, lightColors[i])); g.FillEllipse(glowV, cx, lightsY - S(2), lightDia, lightDia + S(4)); 
                    using var glowCore = new SolidBrush(Color.FromArgb(alpha / 3, lightColors[i])); g.FillEllipse(glowCore, cx - S(1), lightsY - S(1), lightDia + S(2), lightDia + S(2));
                }

                using var glintBrush = new SolidBrush(Color.FromArgb(isActive ? 255 : 180, 255, 255, 255)); g.FillEllipse(glintBrush, cx + S(2), lightsY + S(1), S(3), S(2)); 
            }

            var textSz = g.MeasureString(displayBadge, _fMono9Bold);
            float maxScroll = Math.Max(0, textSz.Width - (mcW - S(12)));

            if (AppConfig.FX.MarqueeTextAnim)
            {
                if (_dispState == DisplayState.Glitching)
                {
                    char[] chars = displayBadge.ToCharArray();
                    for(int i = 0; i < chars.Length; i++) if (_rand.Next(100) < 12) chars[i] = "░▒_-"[_rand.Next(4)];
                    displayBadge = new string(chars);

                    _dispWait--;
                    if (_dispWait <= 0) { _dispStatusIdx = (_dispStatusIdx + 1) % statuses.Count; _dispState = DisplayState.HoldStart; _marqueeWait = AppConfig.MarqueePause; _marqueeX = 0; }
                }
                else if (_dispState == DisplayState.HoldStart)
                {
                    _marqueeX = 0; _marqueeWait--;
                    if (_marqueeWait <= 0) { if (maxScroll > 0) _dispState = DisplayState.Scrolling; else { _dispState = DisplayState.Glitching; _dispWait = 25; } }
                }
                else if (_dispState == DisplayState.Scrolling)
                {
                    _marqueeX -= _scale * 0.7f;
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
            DrawGlowingText(g, displayBadge, _fMono9Bold, badgeColor, mcX + S(6) + _marqueeX, mcY + S(10) - 3, AppConfig.FX.HeaderNeonText ? 90 : 0); 
            g.Restore(clipState);
            
            if (AppConfig.FX.MCGloss)
            {
                using var mcGloss = new LinearGradientBrush(new Rectangle(mcX, mcY, mcW, mcH / 2), Color.FromArgb(25, 255, 255, 255), Color.Transparent, LinearGradientMode.Vertical); g.FillPath(mcGloss, mcPath);
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
            int sbWidth = S(8); 
            
            int sbPaneW = RightGutterW;
            int sbPaneX = Width - sbPaneW;
            using var sbPaneBg = new SolidBrush(Color.FromArgb(16, 14, 18));
            g.FillRectangle(sbPaneBg, sbPaneX, HeaderH, sbPaneW, clipRect.Height);
            using var sbPaneBorder = new Pen(Color.FromArgb(40, CGoldDim), 1);
            g.DrawLine(sbPaneBorder, sbPaneX, HeaderH, sbPaneX, HeaderH + clipRect.Height);

            int sbX = Width - S(17);
            float trackTop = HeaderH + S(22);
            float trackBot = HeaderH + clipRect.Height - S(22);
            float trackHeight = trackBot - trackTop;

            float thumbHeight = Math.Max(S(24), trackHeight * (clipRect.Height / (float)(_contentHeight + (int)_slideOffset)));
            float thumbY = trackTop + (trackHeight - thumbHeight) * (_scrollY / maxScroll);

            using var trackBrush = new SolidBrush(Color.FromArgb(40, 5, 5, 5)); g.FillRectangle(trackBrush, sbX, trackTop, sbWidth, trackHeight);
            using var trackShadow = new Pen(Color.FromArgb(100, 0, 0, 0), 1); g.DrawRectangle(trackShadow, sbX, trackTop, sbWidth, trackHeight);

            using var arrowPen = new Pen(Color.FromArgb(150, 150, 150), 1.5f * _scale); arrowPen.LineJoin = LineJoin.Round; 
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

        private void OnMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || CloseBtnRect.Contains(e.Location)) return;

            int viewHeight = Height - HeaderH - S(6); float maxMainScroll = Math.Max(0, _contentHeight + (int)_slideOffset - viewHeight);
            if (maxMainScroll > 0)
            {
                int sbWidth = S(8), sbX = Width - S(17);
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

                foreach (var row in _layout)
                {
                    if (row.IsSeparator) continue;
                    var job = _jobs[row.JobIndex];
                    if (job.IsExpanded && contentY >= row.Y + RowH && contentY <= row.Y + RowH + row.ExpandedHeight)
                    {
                        float diagMaxScroll = _diagMaxScrolls.TryGetValue(row.JobIndex, out float ms) ? ms : 0f;
                        if (diagMaxScroll > 0) 
                        {
                            float trackY = (row.Y + RowH) + S(4) + S(10) + S(2), trackH = row.ExpandedHeight - S(8) - (S(10) * 2) - S(4);
                            float currentScroll = _diagScrolls.TryGetValue(row.JobIndex, out float ds) ? ds : 0f;
                            float thumbH = Math.Max(S(12), trackH * ((row.ExpandedHeight - S(38)) / (row.ExpandedHeight - S(38) + diagMaxScroll))), screenThumbY = HeaderH - _scrollY + trackY + (trackH - thumbH) * (currentScroll / diagMaxScroll);

                            if (_slideOffset > 0 && row.Y >= _slideStartY) screenThumbY += _slideOffset;

                            if (new RectangleF(WorkingAreaW - Pad - S(10) - S(14), screenThumbY, S(10), thumbH).Contains(e.Location))
                            {
                                _isDraggingDiagIdx = row.JobIndex; _dragStartY = e.Y; _dragStartScrollY = currentScroll; return; 
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
                    
                    float trackH = expH - S(8) - (S(10) * 2) - S(4);
                    float scrollableTrack = trackH - Math.Max(S(12), trackH * ((expH - S(38)) / (expH - S(38) + diagMaxScroll)));
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

                for (int li = 0; li < _layout.Count; li++)
                {
                    var row = _layout[li];
                    if (contentY < row.Y || contentY >= row.Y + row.Height) continue;

                    if (row.IsSeparator) { if (DeleteBtnRect(row).Contains(e.X, (int)contentY)) _hoverDeleteGroupIdx = li; else _hoverLayoutIdx = li; break; }

                    if (row.JobIndex < 0 || row.JobIndex >= _jobs.Count) continue;

                    var job = _jobs[row.JobIndex];
                    bool hasScroll = _diagMaxScrolls.TryGetValue(row.JobIndex, out float ms) && ms > 0;
                    if (job.IsExpanded && CopyBtnRect(row, hasScroll).Contains(e.X, (int)contentY)) { _hoverCopyJobIdx = row.JobIndex; break; }
                    if (DiagToggleBtnRect(job, row.Y).Contains(e.X, (int)contentY)) { _hoverDiagJobIdx = row.JobIndex; break; }
                    
                    var trashBtn = TrashBtnRect(job, row.Y);
                    if (trashBtn.HasValue && trashBtn.Value.Contains(e.X, (int)contentY)) { _hoverTrashJobIdx = row.JobIndex; break; }
                    
                    var b = BtnRect(job, row.Y); if (b.HasValue && b.Value.Contains(e.X, (int)contentY)) { _hoverLayoutIdx = li; break; }
                    break;
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

                foreach (var row in _layout)
                {
                    if (contentY < row.Y || contentY >= row.Y + row.Height) continue;

                    if (row.IsSeparator)
                    {
                        if (DeleteBtnRect(row).Contains(e.X, (int)contentY))
                        {
                            _purgingGroupText = row.GroupText;
                            _purgeAnimFrames = MaxPurgeFrames;
                            Invalidate(); 
                            return;
                        }

                        if (_expandedLogs.Contains(row.SepText)) _expandedLogs.Remove(row.SepText); else _expandedLogs.Add(row.SepText);
                        _layoutNeedsUpdate = true; EnsureLayoutUpdated(); Invalidate(); return;
                    }

                    if (row.JobIndex < 0 || row.JobIndex >= _jobs.Count) continue;

                    var job = _jobs[row.JobIndex];

                    bool hasScroll = _diagMaxScrolls.TryGetValue(row.JobIndex, out float ms) && ms > 0;
                    if (job.IsExpanded && CopyBtnRect(row, hasScroll).Contains(e.X, (int)contentY))
                    {
                        string diagContent = GetDiagContent(job);
                        try { Clipboard.SetText(diagContent); } catch { /* silent */ }
                        Color thC = job.Status switch { UploadStatus.Failed or UploadStatus.Cancelled => CBarFail, UploadStatus.Done => CGreen, UploadStatus.UpdateReady => Color.FromArgb(180, 100, 220), _ => CGoldBrt };
                        _copyBubbles.Add(new CopyBubble { X = e.X, Y = e.Y, Alpha = 255f, ThemeColor = thC }); Invalidate(); return;
                    }

                    if (DiagToggleBtnRect(job, row.Y).Contains(e.X, (int)contentY)) { job.IsExpanded = !job.IsExpanded; _layoutNeedsUpdate = true; EnsureLayoutUpdated(); Invalidate(); return; }

                    var trashBtn = TrashBtnRect(job, row.Y);
                    if (trashBtn.HasValue && trashBtn.Value.Contains(e.X, (int)contentY))
                    {
                        _purgingJobIdx = row.JobIndex;
                        _purgeAnimFrames = MaxJobPurgeFrames;
                        Invalidate();
                        return;
                    }

                    var b = BtnRect(job, row.Y);
                    if (b.HasValue && b.Value.Contains(e.X, (int)contentY))
                    {
                        if (job.IsUpdate && job.Status == UploadStatus.UpdateReady) _onApply(job);
                        else { if (job.CanRetry || job.Status == UploadStatus.Done) _onRetry(job); else if (job.CanCancel) _onCancel(job); }
                        Invalidate(); return;
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

        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; } }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _anim.Stop(); _anim.Dispose(); _bgBrush?.Dispose(); _fTitle28Bold?.Dispose(); _fTitle125Bold?.Dispose(); _fTitle10Bold?.Dispose(); _fTitle95?.Dispose(); _fBody95Italic?.Dispose(); _fBody8Bold?.Dispose(); _fBody8Italic?.Dispose(); _fBody75Bold?.Dispose(); _fBody75Italic?.Dispose(); _fBody75Reg?.Dispose(); _fBody7Bold?.Dispose(); _fMono9Bold?.Dispose(); _fMono9?.Dispose(); _fMono8Bold?.Dispose(); _fMono8?.Dispose(); _fMono75Bold?.Dispose(); _sfCenter?.Dispose(); _sfLeft?.Dispose(); }
            base.Dispose(disposing);
        }
    }
}