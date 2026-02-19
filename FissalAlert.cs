using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using static RedfurSync.FissalTheme;

namespace RedfurSync
{
    public sealed class FissalAlert : Form
    {
        public enum AlertLevel
        {
            Normal,     // Solid Green
            Low,        // Solid Yellow
            High,       // Solid Red
            TotalError, // Flashing Red
            Success     // Flashing Light Blue
        }

        private const int BaseW       = 340;
        private const int BaseHeaderH = 40;
        private const int BasePad     = 15;

        private readonly float _scale;
        private readonly string _message;
        private readonly string _title;
        private readonly AlertLevel _level;
        
        private readonly System.Windows.Forms.Timer _timer;       // Controls the fade out
        private readonly System.Windows.Forms.Timer? _pulseTimer; // Controls the flashing lights
        private bool _isLightOn = true;

        private readonly int _headerH;
        private readonly int _pad;
        private RectangleF _textRect;

        private static FissalAlert? _currentAlert;

        public static void Show(string title, string text, AlertLevel level = AlertLevel.Normal, int durationMs = 7000)
        {
            _currentAlert?.Close();
            _currentAlert = new FissalAlert(title, text, level, durationMs);
            _currentAlert.Show();
        }

        private FissalAlert(string title, string text, AlertLevel level, int durationMs)
        {
            _title   = title;
            _message = text;
            _level   = level;

            AutoScaleMode   = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar   = false;
            TopMost         = true;
            BackColor       = CBg;
            DoubleBuffered  = true;
            StartPosition   = FormStartPosition.Manual;

            var _h = Handle;
            _scale   = GetScale(Handle);
            _pad     = S(BasePad);
            _headerH = S(BaseHeaderH);

            Width = S(BaseW);

            using var g = Graphics.FromHwnd(IntPtr.Zero);
            using var f = Body(10f, _scale, FontStyle.Italic);
            int maxTextW = Width - (_pad * 2);
            var textSize = g.MeasureString(_message, f, maxTextW);

            _textRect = new RectangleF(_pad, _headerH + S(15), maxTextW, textSize.Height);
            Height = (int)_textRect.Bottom + S(20);

            PositionInCorner();

            _timer = new System.Windows.Forms.Timer { Interval = durationMs };
            _timer.Tick += (_, _) => Close();
            _timer.Start();

            // ── The Flashing Light Magic ──
            if (_level == AlertLevel.TotalError || _level == AlertLevel.Success)
            {
                _pulseTimer = new System.Windows.Forms.Timer { Interval = 400 }; // Flash speed
                _pulseTimer.Tick += (_, _) => 
                { 
                    _isLightOn = !_isLightOn; 
                    Invalidate(); 
                };
                _pulseTimer.Start();
            }

            MouseClick += (_, _) => Close();
        }

        protected override bool ShowWithoutActivation => true;

        private void PositionInCorner()
        {
            var wa = Screen.PrimaryScreen?.WorkingArea ?? Screen.AllScreens[0].WorkingArea;
            Location = new Point(wa.Right - Width - S(15), wa.Bottom - Height - S(15));
        }

        private int S(int v) => (int)Math.Round(v * _scale);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode   = PixelOffsetMode.HighQuality;

            using var bgBrush = new SolidBrush(CBg);
            g.FillRectangle(bgBrush, 0, 0, Width, Height);
            
            using var borderPen = new Pen(CBorder, S(1));
            g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            
            using var dimPen = new Pen(Color.FromArgb(32, CGoldDim), S(1));
            g.DrawRectangle(dimPen, S(3), S(3), Width - S(6), Height - S(6));

            DrawCornerRivets(g, Width, Height, S(6), CGoldDim);

            using var hg = new LinearGradientBrush(
                new Point(0, 0), new Point(0, _headerH),
                Color.FromArgb(44, 34, 17), Color.FromArgb(20, 15, 8));
            g.FillRectangle(hg, 0, 0, Width, _headerH);
            DrawDivider(g, S(10), Width - S(10), _headerH - 1, CGoldDim, CGoldMid);

            // ── Determine the Indicator Light Color ──
            Color dotColor = _level switch
            {
                AlertLevel.Normal     => CGreen,
                AlertLevel.Low        => Color.FromArgb(240, 190, 60), // Vibrant Yellow
                AlertLevel.High       => CBarFail,                     // Solid Red
                AlertLevel.TotalError => _isLightOn ? CBarFail : Color.FromArgb(60, 20, 15), // Flashing Red / Dim Glass
                AlertLevel.Success    => _isLightOn ? Color.FromArgb(120, 210, 255) : Color.FromArgb(20, 45, 60), // Flashing Light Blue / Dim Glass
                _                     => CGreen
            };

            int dot = S(8);
            int dy  = _headerH / 2 - dot / 2;
            using var goggleBrush = new SolidBrush(dotColor);
            g.FillEllipse(goggleBrush, _pad, dy, dot, dot);
            
            // Add a soft glow if the light is on!
            if (_isLightOn)
            {
                using var glowPen = new Pen(Color.FromArgb(100, dotColor), S(2));
                g.DrawEllipse(glowPen, _pad - S(1), dy - S(1), dot + S(2), dot + S(2));
            }

            using var tf = Title(14f, _scale, FontStyle.Bold);
            using var titleBrush = new SolidBrush(CGoldBrt);
            g.DrawString(_title, tf, titleBrush, new PointF(_pad + dot + S(0), S(9)));

            using var ff = Body(10f, _scale, FontStyle.Regular);
            using var textBrush = new SolidBrush(CText);
            g.DrawString("- " + _message, ff, textBrush, _textRect);
        }

        protected override CreateParams CreateParams
        {
            get 
            { 
                var cp = base.CreateParams; 
                cp.ExStyle |= 0x80 | 0x08000000; 
                return cp; 
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Stop();
                _timer.Dispose();
                
                if (_pulseTimer != null)
                {
                    _pulseTimer.Stop();
                    _pulseTimer.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}