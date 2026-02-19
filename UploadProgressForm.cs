using System;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
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

        // Base layout at 96 DPI — everything multiplied by _scale at runtime
        private const int BaseW       = 375;
        private const int BaseHeaderH = 58;
        private const int BaseRowH    = 82;
        private const int BaseExpandH = 62;
        private const int BaseBarH    = 7;
        private const int BasePad     = 18;
        private const int BaseBtnW    = 68;
        private const int BaseBtnH    = 28;
        private const int BaseEmptyH  = 90;
        private const int MaxRows     = 7;

        private readonly float _scale;
        private int FormW, HeaderH, RowH, ExpandH, BarH, Pad, BtnW, BtnH, EmptyH;

        private readonly ObservableCollection<UploadJob> _jobs;
        private readonly Action<UploadJob> _onRetry;
        private readonly Action<UploadJob> _onCancel;
        private readonly System.Windows.Forms.Timer _anim;
        
        private int   _hoverRow = -1;
        private bool  _hoverClose; // Tracks hover over the new 'X' button
        private float _shimmer;

        // The hit-box for our new mechanical Close button
        private Rectangle CloseBtnRect => new Rectangle(Width - Pad - S(8), (HeaderH - S(8)) / 2, S(14), S(14));

        public UploadProgressForm(
            ObservableCollection<UploadJob> jobs,
            Action<UploadJob> onRetry,
            Action<UploadJob> onCancel)
        {
            _jobs     = jobs;
            _onRetry  = onRetry;
            _onCancel = onCancel;

            AutoScaleMode   = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar   = false;
            TopMost         = true;
            BackColor       = CBg;
            DoubleBuffered  = true;
            StartPosition   = FormStartPosition.Manual;

            var _h = Handle;
            _scale = GetScale(Handle);
            ApplyScale();

            Width  = FormW;
            Height = HeaderH + EmptyH + S(6);

            _anim = new System.Windows.Forms.Timer { Interval = 40 };
            _anim.Tick += (_, _) => { _shimmer = (_shimmer + 2.5f) % 500f; Invalidate(); };
            _anim.Start();

            // Auto-close removed! It is now a free-floating mechanical panel.
            MouseDown  += OnMouseDown; // For dragging
            MouseClick += OnClick;
            MouseMove  += OnMove;
            MouseLeave += (_, _) => { _hoverRow = -1; _hoverClose = false; Invalidate(); };
        }

        private int S(int v)    => (int)Math.Round(v * _scale);
        private float SF(float v) => v * _scale;

        private void ApplyScale()
        {
            FormW   = S(BaseW);
            HeaderH = S(BaseHeaderH);
            RowH    = S(BaseRowH);
            ExpandH = S(BaseExpandH);
            BarH    = S(BaseBarH);
            Pad     = S(BasePad);
            BtnW    = S(BaseBtnW);
            BtnH    = S(BaseBtnH);
            EmptyH  = S(BaseEmptyH);
        }

        // ── Positioning ───────────────────────────────────────────────────────

        public void PositionAboveTray()
        {
            SyncSize();
            var wa = Screen.PrimaryScreen?.WorkingArea ?? Screen.AllScreens[0].WorkingArea;
            Location = new Point(wa.Right - FormW - S(10), wa.Bottom - Height - S(10));
        }

        private void SyncSize()
        {
            int h = HeaderH;
            if (_jobs.Count == 0) h += EmptyH;
            else
            {
                int vis = Math.Min(_jobs.Count, MaxRows);
                for (int i = 0; i < vis; i++)
                    h += RowH + (_jobs[i].IsExpanded ? ExpandH : 0);
            }
            int nh = h + S(6);
            if (Height != nh || Width != FormW) { Height = nh; Width = FormW; }
        }

        // ── Paint ─────────────────────────────────────────────────────────────

        protected override void OnPaint(PaintEventArgs e)
        {
            SyncSize();
            var g = e.Graphics;
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode   = PixelOffsetMode.HighQuality;

            // Background
            using var bgBrush = new SolidBrush(CBg);
            g.FillRectangle(bgBrush, 0, 0, Width, Height);

            // Outer gold border + inner dim border (panel bolted effect)
            using var borderPen = new Pen(CBorder, S(1));
            g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            
            using var dimPen = new Pen(Color.FromArgb(35, CGoldDim), S(1));
            g.DrawRectangle(dimPen, S(3), S(3), Width - S(6), Height - S(6));

            // Corner rivets
            DrawCornerRivets(g, Width, Height, S(7), CGoldDim);

            DrawHeader(g);

            if (_jobs.Count == 0) DrawEmpty(g);
            else
            {
                int y = HeaderH, vis = Math.Min(_jobs.Count, MaxRows);
                for (int i = 0; i < vis; i++)
                {
                    DrawRow(g, _jobs[i], i, y);
                    y += RowH + (_jobs[i].IsExpanded ? ExpandH : 0);
                }
            }
        }

        // ── Header ────────────────────────────────────────────────────────────

        private void DrawHeader(Graphics g)
        {
            using var hg = new LinearGradientBrush(
                new Point(0, 0), new Point(0, HeaderH),
                Color.FromArgb(46, 36, 18), Color.FromArgb(20, 16, 9));
            g.FillRectangle(hg, 0, 0, Width, HeaderH);

            DrawDivider(g, S(10), Width - S(10), HeaderH - 1, CGoldDim, CGoldMid);

            // Green goggle dot (Fissal's eye)
            int dot = S(11);
            int dy  = HeaderH / 2 - dot / 2 - S(2);
            using var goggleBrush = new SolidBrush(CGreen);
            g.FillEllipse(goggleBrush, Pad, dy, dot, dot);
            
            using var gogglePen = new Pen(Color.FromArgb(160, CGreen), S(1));
            g.DrawEllipse(gogglePen, Pad, dy, dot, dot);
            
            using var glintBrush = new SolidBrush(Color.FromArgb(130, Color.White));
            g.FillEllipse(glintBrush, Pad + dot / 4, dy + dot / 6, dot / 4, dot / 4);

            // Title
            using var tf = Title(12f, _scale, FontStyle.Bold);
            using var titleBrush = new SolidBrush(CGoldBrt);
            g.DrawString("Fissal's Tonal Relay", tf, titleBrush, new PointF(Pad + dot + S(8), S(10)));

            // Subtitle
            using var sf = Body(9.5f, _scale, FontStyle.Bold);
            using var subBrush = new SolidBrush(CGoldDim);
            g.DrawString("Masser Matrix v1.00", sf, subBrush, new PointF(Pad + dot + S(8), S(30)));

            // The new Mechanical Close Button (X)
            var cRect = CloseBtnRect;
            // It glows red (CBarFail) when you hover over it!
            using var cPen = new Pen(_hoverClose ? CBarFail : CGoldDim, S(2));
            g.DrawLine(cPen, cRect.X, cRect.Y, cRect.Right, cRect.Bottom);
            g.DrawLine(cPen, cRect.Right, cRect.Y, cRect.X, cRect.Bottom);

            // Badge - Shifted slightly left to make room for the new close button
            int active = 0;
            foreach (var j in _jobs)
                if (j.Status is UploadStatus.Uploading or UploadStatus.Queued) active++;
            string badge = active > 0 ? $"⚙ {active} purrs syncing"
                : _jobs.Count == 0 ? "standing by" : $"{_jobs.Count} in queue";
                
            using var bf = Body(8f, _scale);
            var bsz = g.MeasureString(badge, bf);
            using var badgeBrush = new SolidBrush(active > 0 ? CGreen : CTextSub);
            // Notice the extra Pad + S(16) subtraction here to move it left
            g.DrawString(badge, bf, badgeBrush, new PointF(Width - bsz.Width - Pad - S(24), (HeaderH - bsz.Height) / 1f));
        }

        // ── Empty ─────────────────────────────────────────────────────────────

        private void DrawEmpty(Graphics g)
        {
            using var gf = Title(28f, _scale, FontStyle.Bold);
            var gsz = g.MeasureString("⚙", gf);
            using var gearBrush = new SolidBrush(Color.FromArgb(22, CGoldDim));
            g.DrawString("⚙", gf, gearBrush, new PointF((Width - gsz.Width) / 2f, HeaderH + S(8)));

            using var f1 = Title(10f, _scale, FontStyle.Bold);
            var t1 = "Purr frequency tuned!";
            var s1 = g.MeasureString(t1, f1);
            using var text1Brush = new SolidBrush(CTextSub);
            g.DrawString(t1, f1, text1Brush, new PointF((Width - s1.Width) / 2f, HeaderH + EmptyH - S(40)));

            using var f2 = Body(8f, _scale, FontStyle.Italic);
            var t2 = "awaiting data to transmit!";
            var s2 = g.MeasureString(t2, f2);
            using var text2Brush = new SolidBrush(Color.FromArgb(70, CTextSub));
            g.DrawString(t2, f2, text2Brush, new PointF((Width - s2.Width) / 2f, HeaderH + EmptyH - S(22)));
        }

        // ── Row ───────────────────────────────────────────────────────────────

        private void DrawRow(Graphics g, UploadJob job, int idx, int y)
        {
            if (idx % 2 == 1)
            {
                using var altBrush = new SolidBrush(Color.FromArgb(10, 255, 245, 200));
                g.FillRectangle(altBrush, 0, y, Width, RowH);
            }

            // Status glyph
            (string glyph, Color gc) = job.Status switch
            {
                UploadStatus.Queued    => ("◷", CTextSub),
                UploadStatus.Uploading => ("⚙", CGoldBrt),
                UploadStatus.Done      => ("✦", CGreen),
                UploadStatus.Failed    => ("✖", CBarFail),
                UploadStatus.Cancelled => ("—", CBarCancel),
                _                      => ("?", CTextSub)
            };
            using var gf = Body(13f, _scale);
            using var glyphBrush = new SolidBrush(gc);
            g.DrawString(glyph, gf, glyphBrush, new PointF(Pad, y + S(10)));

            var btn = BtnRect(job, y);
            if (btn.HasValue)
                DrawBtn(g, btn.Value, job.CanRetry ? "Re-send" : "Abort",
                    job.CanRetry ? CGreen : CBarFail, _hoverRow == idx);

            float nw = Width - Pad * 3 - S(28) - (btn.HasValue ? BtnW + S(8) : 0);
            using var nf = Title(9.5f, _scale);
            using var nameBrush = new SolidBrush(CText);
            g.DrawString(Trunc(job.FileName, g, nf, nw), nf, nameBrush, new PointF(Pad + S(26), y + S(8)));

            using var sf = Body(7.5f, _scale, FontStyle.Italic);
            using var detailBrush = new SolidBrush(CTextSub);
            g.DrawString($"{job.FileSizeDisplay}  ·  dispatched {job.QueuedAt:HH:mm:ss}", sf, detailBrush, new PointF(Pad + S(26), y + S(30)));

            float bx = Pad, by = y + RowH - BarH - S(16), bw = Width - Pad * 2;
            using var track = RoundRect(bx, by, bw, BarH, S(3));
            using var trackBrush = new SolidBrush(CBarBg);
            g.FillPath(trackBrush, track);

            float fill = job.Status == UploadStatus.Done ? bw : bw * job.Progress;
            if (fill > 1f)
            {
                Color fc = job.Status switch
                {
                    UploadStatus.Done      => CBarDone,
                    UploadStatus.Failed    => CBarFail,
                    UploadStatus.Cancelled => CBarCancel,
                    _                      => CBarActive
                };
                using var fp = RoundRect(bx, by, fill, BarH, S(3));
                using var fillBrush = new SolidBrush(fc);
                g.FillPath(fillBrush, fp);

                if (job.Status == UploadStatus.Uploading && fill > S(20))
                {
                    float sx = bx + (_shimmer % (fill + S(60))) - S(28);
                    if (sx > bx && sx < bx + fill)
                    {
                        float sw = Math.Min(S(36), bx + fill - sx);
                        using var sh = new LinearGradientBrush(
                            new PointF(sx, by), new PointF(sx + sw, by),
                            Color.FromArgb(0, Color.White), Color.FromArgb(90, Color.White));
                        g.FillRectangle(sh, sx, by, sw, BarH);
                    }
                }
            }

            string pct = job.Status switch
            {
                UploadStatus.Done      => "bounced to vault  ✦",
                UploadStatus.Failed    => "lunar static",
                UploadStatus.Cancelled => "purr aborted",
                UploadStatus.Queued    => "awaiting ear-rotation alignment",
                _                      => $"{job.Progress * 100:0}%"
            };
            using var pf = Body(7.5f, _scale, job.Status == UploadStatus.Done ? FontStyle.Bold : FontStyle.Regular);
            var psz = g.MeasureString(pct, pf);
            Color pc = job.Status switch
            {
                UploadStatus.Done   => CGreen,
                UploadStatus.Failed => CBarFail,
                _                   => CTextSub
            };
            using var pctBrush = new SolidBrush(pc);
            g.DrawString(pct, pf, pctBrush, new PointF(Width - Pad - psz.Width, by - S(14)));

            bool hasErr = job.Status is UploadStatus.Failed or UploadStatus.Cancelled
                          && !string.IsNullOrWhiteSpace(job.ErrorMessage);
            if (hasErr)
            {
                using var cf = Body(7.5f, _scale, FontStyle.Italic);
                using var expandBrush = new SolidBrush(CGoldDim);
                g.DrawString(job.IsExpanded ? "▲ conceal" : "▼ diagnostics",
                    cf, expandBrush, new PointF(Pad + S(26), by - S(14)));
            }

            DrawDivider(g, 0, Width, y + RowH - 1,
                Color.FromArgb(18, 255, 245, 200), Color.FromArgb(0, Color.Black));

            if (job.IsExpanded && !string.IsNullOrWhiteSpace(job.ErrorMessage))
            {
                int ey = y + RowH;
                using var errBgBrush = new SolidBrush(CErrBg);
                g.FillRectangle(errBgBrush, 1, ey, Width - 2, ExpandH);
                
                using var errPen = new Pen(CErrBorder, S(1));
                g.DrawRectangle(errPen, 1, ey, Width - 3, ExpandH - 1);

                using var el = Body(7.5f, _scale, FontStyle.Italic);
                using var errTitleBrush = new SolidBrush(Color.FromArgb(140, 95, 72));
                g.DrawString("⚙ Diagnostics:", el, errTitleBrush, new PointF(Pad, ey + S(8)));

                using var ef = Mono(8f, _scale);
                using var errMsgBrush = new SolidBrush(Color.FromArgb(215, 118, 100));
                g.DrawString(job.ErrorMessage, ef, errMsgBrush,
                    new RectangleF(Pad, ey + S(24), Width - Pad * 2, ExpandH - S(30)),
                    new StringFormat { Trimming = StringTrimming.EllipsisCharacter });
            }
        }

        private void DrawBtn(Graphics g, Rectangle r, string label, Color accent, bool hov)
        {
            using var path = RoundRect(r.X, r.Y, r.Width, r.Height, S(4));
            
            using var bgBrush = new SolidBrush(hov ? Color.FromArgb(65, accent) : CBtnBg);
            g.FillPath(bgBrush, path);
            
            using var btnPen = new Pen(hov ? accent : CBtnBorder, S(1));
            g.DrawPath(btnPen, path);
            
            using var f  = Body(8f, _scale, FontStyle.Bold);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var textBrush = new SolidBrush(hov ? Color.White : accent);
            
            g.DrawString(label, f, textBrush, new RectangleF(r.X, r.Y, r.Width, r.Height), sf);
        }

        private Rectangle? BtnRect(UploadJob job, int ry)
        {
            if (!job.CanRetry && !job.CanCancel) return null;
            return new Rectangle(Width - Pad - BtnW, ry + (RowH - BtnH) / 2, BtnW, BtnH);
        }

        // ── Mouse & Dragging ───────────────────────────────────────────────────

        private void OnMouseDown(object? sender, MouseEventArgs e)
        {
            // If we click the header (but NOT the close button), drag the window!
            if (e.Button == MouseButtons.Left && e.Y <= HeaderH && !CloseBtnRect.Contains(e.Location))
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, 0x2, 0);
            }
        }

        private void OnMove(object? sender, MouseEventArgs e)
        {
            // Check hover for the 'X' Close button
            bool newHoverClose = CloseBtnRect.Contains(e.Location);
            if (_hoverClose != newHoverClose)
            {
                _hoverClose = newHoverClose;
                Invalidate();
            }

            int prev = _hoverRow; _hoverRow = -1;
            int y = HeaderH, vis = Math.Min(_jobs.Count, MaxRows);
            for (int i = 0; i < vis; i++)
            {
                var b = BtnRect(_jobs[i], y);
                if (b.HasValue && b.Value.Contains(e.Location)) { _hoverRow = i; break; }
                y += RowH + (_jobs[i].IsExpanded ? ExpandH : 0);
            }
            if (_hoverRow != prev) Invalidate();
        }

        private void OnClick(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            // Did we click the new 'X' Close button?
            if (CloseBtnRect.Contains(e.Location))
            {
                Close();
                return;
            }

            int y = HeaderH, vis = Math.Min(_jobs.Count, MaxRows);
            for (int i = 0; i < vis; i++)
            {
                var job = _jobs[i];
                var b   = BtnRect(job, y);
                if (b.HasValue && b.Value.Contains(e.Location))
                {
                    if (job.CanRetry)  _onRetry(job);
                    if (job.CanCancel) _onCancel(job);
                    Invalidate(); return;
                }
                bool hasErr = job.Status is UploadStatus.Failed or UploadStatus.Cancelled
                              && !string.IsNullOrWhiteSpace(job.ErrorMessage);
                if (hasErr && e.Y >= y && e.Y < y + RowH)
                {
                    job.IsExpanded = !job.IsExpanded;
                    SyncSize(); Invalidate(); return;
                }
                y += RowH + (job.IsExpanded ? ExpandH : 0);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string Trunc(string text, Graphics g, Font f, float maxW)
        {
            if (g.MeasureString(text, f).Width <= maxW) return text;
            var ext   = System.IO.Path.GetExtension(text);
            var noExt = System.IO.Path.GetFileNameWithoutExtension(text);
            while (noExt.Length > 3 && g.MeasureString(noExt + "…" + ext, f).Width > maxW)
                noExt = noExt[..^1];
            return noExt + "…" + ext;
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) 
            {
                _anim.Stop();
                _anim.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}