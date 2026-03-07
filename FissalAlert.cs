using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using static RedfurSync.FissalTheme;

namespace RedfurSync
{
    public sealed class FissalAlert : Form
    {
        public enum AlertLevel { Low, Normal, Success, TotalError }

        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private readonly string _alertTitle;
        private readonly string _alertText;
        private readonly float _scale;
        
        private readonly Color _themeColor;
        private readonly int _flashSpeed;
        
        private readonly System.Windows.Forms.Timer _animTimer;
        private int _ticks;
        private int _maxTicks;
        private float _glow = 0f;
        private bool _fadingOut = false;

        private int _lightAlpha = 40;
        private int _lightStep;

        private int _targetY;
        private int _startY;
        private int _slideFrame = 0;
        private const int SlideDuration = 18; // The length of the bounce

        public static void Show(string title, string text, AlertLevel level = AlertLevel.Normal, int timeoutMs = 7000, Action? onClick = null)
        {
            var alert = new FissalAlert(title, text, level, null, null, timeoutMs, onClick);
            alert.Show();
        }

        public static void ShowCustom(string title, string text, Color lightColor, int flashSpeed, int timeoutMs = 7000, Action? onClick = null)
        {
            var alert = new FissalAlert(title, text, null, lightColor, flashSpeed, timeoutMs, onClick);
            alert.Show();
        }

        private FissalAlert(string title, string text, AlertLevel? level, Color? customColor, int? customSpeed, int timeoutMs, Action? onClick)
        {
            _alertTitle = title;
            _alertText = text;
            
            if (customColor.HasValue && customSpeed.HasValue)
            {
                _themeColor = customColor.Value;
                _flashSpeed = customSpeed.Value;
            }
            else
            {
                switch (level)
                {
                    case AlertLevel.TotalError:
                        _themeColor = CBarFail;
                        _flashSpeed = 24;
                        break;
                    case AlertLevel.Success:
                        _themeColor = Color.FromArgb(60, 180, 220);
                        _flashSpeed = 24;
                        break;
                    case AlertLevel.Normal:
                        _themeColor = CGreen;
                        _flashSpeed = 2;
                        break;
                    case AlertLevel.Low:
                    default:
                        _themeColor = CGoldDim;
                        _flashSpeed = 4;
                        break;
                }
            }

            _lightStep = _flashSpeed;


            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = CBg;
            DoubleBuffered = true;
            StartPosition = FormStartPosition.Manual;
            
            _scale = GetScale(Handle);

            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
            using (var tf = Title(14f, _scale, FontStyle.Bold))
            using (var bf = Body(10.5f, _scale))
            {
                string titleDisplay = $"> {_alertTitle.ToUpper()}";
                float titleW = g.MeasureString(titleDisplay, tf).Width;
                float textRawW = g.MeasureString(_alertText, bf).Width;

                int minWidth = S(100);
                int maxWidth = S(300); 
                
                int desiredWidth = (int)Math.Max(titleW + S(35), textRawW + S(15));
                Width = Math.Max(minWidth, Math.Min(desiredWidth, maxWidth));

                float wrapWidth = Width - S(10);
                float wrappedTextH = g.MeasureString(_alertText, bf, new SizeF(wrapWidth, 9999)).Height;

                int minHeight = S(100);
                int desiredHeight = (int)Math.Ceiling(wrappedTextH + S(50));
                Height = Math.Max(minHeight, desiredHeight);
            }

            var wa = Screen.PrimaryScreen?.WorkingArea ?? Screen.AllScreens[0].WorkingArea;
            
            int targetX = wa.Right - Width - S(10);
            _targetY = wa.Bottom - Height - S(10);
            _startY = wa.Bottom + S(10); // Hidden just below the screen edge

            Location = new Point(targetX, _startY);
            Opacity = 0;

            // Increased interval from 16ms to 33ms (~30fps) to drastically reduce CPU load and prevent UI thread freezing.
            _maxTicks = timeoutMs / 25;
            _animTimer = new System.Windows.Forms.Timer { Interval = 25 };
            _animTimer.Tick += OnTick;
            _animTimer.Start();

            MouseClick += (_, _) => {
                onClick?.Invoke();
                BeginFadeOut();
            };
        }

        private int S(int v) => (int)Math.Round(v * _scale);

        private void BeginFadeOut()
        {
            if (_fadingOut) return;
            _fadingOut = true;
            _ticks = Math.Max(_ticks, _maxTicks - SlideDuration);
        }

        private void OnTick(object? sender, EventArgs e)
        {
            _ticks++;
            
            _glow = (float)(Math.Sin(_ticks * 0.1) * 0.5 + 0.5); 

            _lightStep = _lightStep > 0 ? _flashSpeed : -_flashSpeed;
            _lightAlpha += _lightStep;

            if (_lightAlpha >= 240) { _lightAlpha = 240; _lightStep = -_flashSpeed; }
            if (_lightAlpha <= 40)  { _lightAlpha = 40;  _lightStep = _flashSpeed; }

            if (_fadingOut || _ticks > _maxTicks - SlideDuration)
            {
                if (!_fadingOut) BeginFadeOut();
                
                _slideFrame--;
                Opacity = Math.Min(1.0, (double)_slideFrame / (SlideDuration - 5)); 
                
                if (_slideFrame <= 0)
                {
                    _animTimer.Stop();
                    Close();
                    return;
                }
            }
            else
            {
                if (_slideFrame < SlideDuration) _slideFrame++;
                if (Opacity < 1.0) Opacity += 0.15;
            }

            // The mathematical arc of her pounce (Ease-Out-Back)
            double t = (double)_slideFrame / SlideDuration;
            double c1 = 1.70158;
            double c3 = c1 + 1.0;
            double easeOutBack = 1.0 + c3 * Math.Pow(t - 1.0, 3.0) + c1 * Math.Pow(t - 1.0, 2.0);

            // Apply her new vertical position
            Top = _startY + (int)((_targetY - _startY) * easeOutBack);

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using var bgBrush = new SolidBrush(CBg);
            g.FillRectangle(bgBrush, 0, 0, Width, Height);
            
            using var dimPen = new Pen(Color.FromArgb(35, CGoldDim), S(1));
            g.DrawRectangle(dimPen, S(3), S(3), Width - S(6), Height - S(6));
            
            DrawCornerRivets(g, Width, Height, S(3), CGoldDim);

            int rimSize = S(13);
            int dx = S(12);
            int dy = S(10);

            using var lightGlowPen = new Pen(Color.FromArgb(Math.Min(255, _lightAlpha), _themeColor), S(4));
            g.DrawEllipse(lightGlowPen, dx - S(1), dy - S(1), rimSize + S(2), rimSize + S(2));

            using var rimBrush = new LinearGradientBrush(
                new Rectangle(dx, dy, rimSize, rimSize),
                CGoldBrt, CGoldDim, LinearGradientMode.ForwardDiagonal);
            g.FillEllipse(rimBrush, dx, dy, rimSize, rimSize);

            int reflectionAlpha = Math.Max(0, (int)(_lightAlpha * 0.45f)); 
            using var reflectionBrush = new SolidBrush(Color.FromArgb(reflectionAlpha, _themeColor));
            g.FillEllipse(reflectionBrush, dx, dy, rimSize, rimSize);

            using var rimBorder = new Pen(Color.FromArgb(30, 20, 10), S(1));
            g.DrawEllipse(rimBorder, dx, dy, rimSize, rimSize);

            int glassPad = S(2);
            int gSize = rimSize - glassPad * 2;
            int gx = dx + glassPad;
            int gy = dy + glassPad;

            using var shadowBrush = new SolidBrush(Color.FromArgb(180, 5, 5, 5));
            g.FillEllipse(shadowBrush, gx, gy, gSize, gSize);

            int coreAlpha = Math.Max(30, _lightAlpha); 
            using var coreBrush = new SolidBrush(Color.FromArgb(coreAlpha, _themeColor));
            g.FillEllipse(coreBrush, gx, gy, gSize, gSize);

            using var glintBrush = new SolidBrush(Color.FromArgb(160, Color.White));
            g.FillEllipse(glintBrush, gx + gSize / 4, gy + gSize / 6, gSize / 3, gSize / 3);

            using var tf = Title(12f, _scale, FontStyle.Bold);
            string titleDisplay = $"> {_alertTitle.ToUpper()}";
            
            int baseGlow = (int)(30 + (50 * _glow)); 
            using var titleGlowBrush = new SolidBrush(Color.FromArgb(baseGlow, _themeColor));
            
            float tx = dx + rimSize + S(5); 
            float ty = S(6);
            
            for (int hx = -1; hx <= 1; hx++)
            {
                for (int hy = -1; hy <= 1; hy++)
                {
                    if (hx == 0 && hy == 0) continue;
                    g.DrawString(titleDisplay, tf, titleGlowBrush, new PointF(tx + (hx * S(1)), ty + (hy * S(1))));
                }
            }
            
            using var textCoreBrush = new SolidBrush(_themeColor);
            g.DrawString(titleDisplay, tf, textCoreBrush, new PointF(tx, ty));

            int boxX = S(10);
            int boxY = S(30);
            int boxW = Width - S(20);
            int boxH = Height - S(40);

            using var crtBg = new SolidBrush(Color.FromArgb(8, 8, 10)); 
            g.FillRectangle(crtBg, boxX, boxY, boxW, boxH);

            using var crtBorder = new Pen(Color.FromArgb(50, _themeColor), S(1));
            g.DrawRectangle(crtBorder, boxX, boxY, boxW, boxH);

            using var scanPen = new Pen(Color.FromArgb(20, _themeColor), 1);
            for (int i = boxY + 2; i < boxY + boxH; i += 3)
            {
                g.DrawLine(scanPen, boxX + 1, i, boxX + boxW - 1, i);
            }

            using var bf = Body(9.5f, _scale);
            using var bodyTextBrush = new SolidBrush(CText);
            var textRect = new RectangleF(boxX + S(8), boxY + S(8), boxW - S(16), boxH - S(16));
            g.DrawString(_alertText, bf, bodyTextBrush, textRect);
            
            using var borderPen = new Pen(CBorder, S(1));
            g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animTimer?.Stop();
                _animTimer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}