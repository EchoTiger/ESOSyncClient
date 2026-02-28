using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using static RedfurSync.FissalTheme;

namespace RedfurSync
{
    public sealed class DisplayNameForm : Form
    {
        // ── Native magic to allow dragging borderless forms ──
        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        private const int BaseW       = 360;
        private const int BaseHeaderH = 69;
        private const int BasePad     = 10;

        private readonly float _scale;
        private readonly int   _pad;
        private readonly int   _headerH;

        private readonly TextBox _input;
        private readonly Button  _saveBtn;
        private readonly Button  _cancelBtn;

        // ── Pulse & Tuning Variables ──
        private readonly System.Windows.Forms.Timer _pulseTimer;
        private int _glowAlpha = 40;
        private int _glowStep = 24;
        private Color _lightColor = Color.FromArgb(60, 180, 220); 
        
        private bool _isTuning = true;
        private int _tuningTicks = 0;
        private string _lockedFrequency = "0.00";

        // ── Advanced Organic Tuning Dynamics ──
        private double _currentFreqValue = 100.00;
        private string _currentFreqString = "100.00";
        private Color _currentFreqColor = Color.Gold;

        // Controls for the natural stutter and pacing
        private int _nextScrambleTick = 0;
        private int _minScrambleDelay = 2; // The fastest jump (frantic)
        private int _maxScrambleDelay = 8; // The longest pause (listening closely)

        // Controls for the visual static
        private int _glitchChance = 1; // Percentage chance (0-100) to show visual static (#, ?, %, !)
        
        private readonly List<(string Text, Color Color)> _consoleLines = new List<(string, Color)>();
        
        // ── The Mechanical Camera Scroll ──
        private int _cameraY = 0;
        private int _targetCameraY = 0;

        // ── The Spark of Chaos ──
        private readonly Random _rnd = new Random();

        public string DisplayName => _input.Text.Trim();

        public DisplayNameForm(string currentName)
        {
            AutoScaleMode   = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar   = false;
            TopMost         = true;
            BackColor       = CBg;
            DoubleBuffered  = true;
            StartPosition   = FormStartPosition.CenterScreen;

            var _h = Handle;
            _scale   = GetScale(Handle);
            _pad     = S(BasePad);
            _headerH = S(BaseHeaderH);

            Width = S(BaseW);

            // Seed the initial terminal thought 
            _consoleLines.Add(("# [FIS-DBG] Tuning frequency: ", Color.GreenYellow));

            // ── The Mechanical Housing (Clipping Mask) ──
            var inputHousing = new Panel
            {
                Location  = new Point(_pad, _headerH + S(65)), 
                Width     = S(BaseW) - _pad * 2,
                Height    = S(28), 
                BackColor = CGoldDark, 
            };

            inputHousing.Paint += (s, e) => 
            {
                using var p = new Pen(CGoldDim, S(3));
                e.Graphics.DrawRectangle(p, 5, 5, inputHousing.Width - 1, inputHousing.Height - 1);
            };

            // ── The Trapped Text ──
            _input = new TextBox
            {
                Text            = currentName is "Redfur Trader" or "Unknown" or "" ? "" : currentName,
                PlaceholderText = "How shall this one address you?",
                BackColor       = inputHousing.BackColor, 
                ForeColor       = CText,
                BorderStyle     = BorderStyle.None,
                AutoSize        = false,
                Font            = Body(12.5f, _scale),
                MaxLength       = 32,
                Location        = new Point(S(5), -S(3)), 
                Width           = inputHousing.Width - S(8),
                Height          = S(40), 
            };

            inputHousing.Controls.Add(_input);
            _input.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)  { e.SuppressKeyPress = true; TrySave(); }
                if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            };

            int btnY = inputHousing.Bottom + S(16);
            _saveBtn   = MakeBtn("Transmit",  CGreen,                     new Point(_pad,                       btnY));
            _cancelBtn = MakeBtn("Not now",  Color.FromArgb(90, 72, 44), new Point(S(BaseW) - _pad - S(130),  btnY));
            _saveBtn.Click   += (_, _) => TrySave();
            _cancelBtn.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

            Height = _saveBtn.Bottom + _pad;

            Controls.AddRange(new Control[] { inputHousing, _saveBtn, _cancelBtn });
            Shown += (_, _) => { _input.Focus(); _input.SelectAll(); };

            // ── The Drag Snare ──
            MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ReleaseCapture();
                    SendMessage(Handle, 0xA1, 0x2, 0); 
                }
            };

            // ── The Waking Sequence ──
            _pulseTimer = new System.Windows.Forms.Timer { Interval = 40 }; 
            _pulseTimer.Tick += (_, _) =>
            {
                // ── Advance the Camera Motor ──
                if (_cameraY < _targetCameraY)
                {
                    // A heavy, instantaneous shift, like a mechanical CRT buffer
                    //_cameraY = _targetCameraY; 

                    // A fast, mechanical zip (3 pixels per tick) rather than a smooth float
                    _cameraY += Math.Max(1, S(3)); 
                    if (_cameraY > _targetCameraY) _cameraY = _targetCameraY;
                }

                if (_isTuning)
                {
                    _tuningTicks++;
                    
                    // Only generate new numbers when we reach the randomized target tick
                    if (_tuningTicks >= _nextScrambleTick && _tuningTicks < 80)
                    {
                        // Calculate the next time the dial should "jump"
                        _nextScrambleTick = _tuningTicks + _rnd.Next(_minScrambleDelay, _maxScrambleDelay + 1);

                        _currentFreqValue = _rnd.NextDouble() * 899.99 + 100.00;
                        _currentFreqString = _currentFreqValue.ToString("0.00");

                        // Apply organic glitching based on your adjustable percentage
                        if (_rnd.Next(100) < _glitchChance) 
                        {
                            if (_rnd.Next(2) == 0) _currentFreqString = _currentFreqString.Replace('4', '#').Replace('7', '?');
                            else _currentFreqString = _currentFreqString.Replace('2', '%').Replace('9', '!');
                        }
                        
                        // Natural Color Shift: Blend the color smoothly based on how high the frequency is
                        float blendRatio = (float)((_currentFreqValue - 100.0) / 899.99);
                        _currentFreqColor = BlendColor(Color.Gold, Color.DarkOrange, blendRatio);
                    }

                    if (_tuningTicks == 80) // ~1.2s
                    {
                        _lockedFrequency = (_rnd.NextDouble() * 899.99 + 100.00).ToString("0.00");
                        _consoleLines[0] = ($"# [FIS-DBG] Frequency Tuned! @ {_lockedFrequency}hz", CGreen);
                    }
                    else if (_tuningTicks == 100)
                    {
                        AddConsoleLine("# [FIS-DBG] Stabilizing connection...", Color.YellowGreen);
                    }
                    else if (_tuningTicks == 120) // ~2.2s
                    {
                        AddConsoleLine("# [FIS-DBG] Synchronizing...", Color.YellowGreen);
                    }
                    else if (_tuningTicks == 160) // ~3.2s
                    {
                        AddConsoleLine("# [FIS-DBG] Connection stabilized!", CGreen);
                    }
                    else if (_tuningTicks == 200) // ~4.0s
                    {
                        AddConsoleLine("# [FIS-DBG] Data stream ready!", Color.FromArgb(120, 210, 255));
                        _isTuning = false; 
                        _lightColor = CGreen; 
                    }
                }

                int targetStep = _isTuning ? 24 : 4; 
                _glowStep = _glowStep > 0 ? targetStep : -targetStep;
                _glowAlpha += _glowStep;

                if (_glowAlpha >= 240) { _glowAlpha = 240; _glowStep = -targetStep; }
                if (_glowAlpha <= 40)  { _glowAlpha = 40;  _glowStep = targetStep; }

                Invalidate(); 
            };
            _pulseTimer.Start();
        }

        private void AddConsoleLine(string text, Color color)
        {
            _consoleLines.Add((text, color));
            // If we have more than 2 lines, instruct the camera to instantly slide down by one line height
            if (_consoleLines.Count > 2)
            {
                _targetCameraY += S(12);
            }
        }

        private Color BlendColor(Color c1, Color c2, float ratio)
        {
            // Ensure the ratio stays strictly between 0 and 1
            ratio = Math.Max(0f, Math.Min(1f, ratio));
            
            int r = (int)(c1.R + (c2.R - c1.R) * ratio);
            int g = (int)(c1.G + (c2.G - c1.G) * ratio);
            int b = (int)(c1.B + (c2.B - c1.B) * ratio);
            
            return Color.FromArgb(r, g, b);
        }

        private int S(int v) => (int)Math.Round(v * _scale);

        private void TrySave()
        {
            if (string.IsNullOrWhiteSpace(_input.Text))
            {
                _input.BackColor = Color.FromArgb(55, 18, 12);
                var t = new System.Windows.Forms.Timer { Interval = 500 };
                t.Tick += (_, _) => { _input.BackColor = Color.FromArgb(10, 8, 5); t.Stop(); t.Dispose(); }; 
                t.Start();
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

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

            DrawCornerRivets(g, Width, Height, S(7), CGoldDim);

            // Header
            using var hg = new LinearGradientBrush(
                new Point(0, 0), new Point(0, _headerH),
                Color.FromArgb(44, 34, 17), Color.FromArgb(20, 15, 8));
            g.FillRectangle(hg, 0, 0, Width, _headerH);
            DrawDivider(g, S(10), Width - S(10), _headerH - 1, CGoldDim, CGoldMid);

            // ── The Mechanical Indicator Light ──
            int rimSize = S(16);
            int dy = _headerH / 3 - rimSize / 3 - S(5); 
            int dx = _pad;

            using var glowPen = new Pen(Color.FromArgb(Math.Min(255, _glowAlpha), _lightColor), S(4));
            g.DrawEllipse(glowPen, dx - S(1), dy - S(1), rimSize + S(2), rimSize + S(2));

            using var rimBrush = new LinearGradientBrush(
                new Rectangle(dx, dy, rimSize, rimSize),
                CBtnDark, CBtnLight, LinearGradientMode.ForwardDiagonal);
            g.FillEllipse(rimBrush, dx, dy, rimSize, rimSize);

            using var rimBorder = new Pen(Color.FromArgb(30, 20, 10), S(1));
            g.DrawEllipse(rimBorder, dx, dy, rimSize, rimSize);

            int glassPad = S(3);
            int gSize = rimSize - glassPad * 2;
            int gx = dx + glassPad;
            int gy = dy + glassPad;

            using var shadowBrush = new SolidBrush(Color.FromArgb(180, 5, 5, 5));
            g.FillEllipse(shadowBrush, gx, gy, gSize, gSize);

            int coreAlpha = Math.Max(60, _glowAlpha);
            using var coreBrush = new SolidBrush(Color.FromArgb(coreAlpha, _lightColor));
            g.FillEllipse(coreBrush, gx, gy, gSize, gSize);

            using var glintBrush = new SolidBrush(Color.FromArgb(160, Color.White));
            g.FillEllipse(glintBrush, gx + gSize / 4, gy + gSize / 6, gSize / 3, gSize / 3);

            // ── The Digital Screen Cutout ──
            int screenX = _pad + rimSize + S(8);
            int screenY = S(8);
            int screenW = Width - screenX - S(12);
            int screenH = _headerH - S(16);

            using var screenBg = new SolidBrush(Color.FromArgb(6, 6, 8));
            g.FillRectangle(screenBg, screenX, screenY, screenW, screenH);

            using var screenBorder = new Pen(Color.FromArgb(80, 37, 252, 0), S(1));
            g.DrawRectangle(screenBorder, screenX, screenY, screenW, screenH);

            using var scanlinePen = new Pen(Color.FromArgb(12, CGreen), 2);
            for (int i = screenY; i < screenY + screenH; i += 3)
            {
                g.DrawLine(scanlinePen, screenX, i, screenX + screenW, i);
            }

            Action<string, Font, Color, PointF> DrawGlowingText = (text, font, color, pt) =>
            {
                int dynamicAlpha = Math.Max(10, _glowAlpha / 3); 
                using var glowBrush = new SolidBrush(Color.FromArgb(dynamicAlpha, color));
                
                int spread = 1; 
                int offset = Math.Max(1, S(1)); 
                
                for (int hx = -spread; hx <= spread; hx++)
                {
                    for (int hy = -spread; hy <= spread; hy++)
                    {
                        if (hx == 0 && hy == 0) continue;
                        g.DrawString(text, font, glowBrush, new PointF(pt.X + (hx * offset), pt.Y + (hy * offset)));
                    }
                }
                
                using var coreBrush = new SolidBrush(color);
                g.DrawString(text, font, coreBrush, pt);
            };

            // ── Static Header (Drawn ABOVE the scrolling mask) ──
            using var tf = Title(12f, _scale, FontStyle.Bold);
            DrawGlowingText("> TONAL_RECOGNITION.4md", tf, CGoldBrt, new PointF(screenX + S(1), screenY + S(-1)));

            // ── Apply Clipping Mask so old text slides cleanly under the header ──
            g.SetClip(new Rectangle(screenX, screenY + S(15), screenW, screenH - S(15)));

            using var sf = Body(8f, _scale, FontStyle.Regular);
            
            // Decouple the blink from the tuner state so it never freezes
            bool showCursor = (Environment.TickCount / 500) % 2 == 0;
            
            for (int i = 0; i < _consoleLines.Count; i++)
            {
                var line = _consoleLines[i];
                string lineText = line.Text;
                Color lineColor = line.Color;

                if (i == 0 && _tuningTicks < 80)
                {
                    // Use the stored, slower-updating values
                    lineText = $"# [FIS-DBG] Tuning frequency: {_currentFreqString}hz";
                    lineColor = _currentFreqColor;
                }

                // Append the blinking block cursor ONLY to the final line
                if (i == _consoleLines.Count - 1 && showCursor)
                {
                    lineText += "_";
                }

                // ── The Retro Camera Offset Formula ──
                int yPos = screenY + S(27) + (i * S(12)) - _cameraY; 
                
                // Only render if it's currently inside the visible screen bounds
                if (yPos > screenY - S(12) && yPos < screenY + screenH)
                {
                    DrawGlowingText(lineText, sf, lineColor, new PointF(screenX + S(12), yPos));
                }
            }

            // Remove the mask so the rest of the form draws normally
            g.ResetClip();

            // ── Smoothly Spaced Form Text ──
            using var sf3 = Body(9.5f, _scale, FontStyle.Regular);
            using var subBrush3 = new SolidBrush(CText);
            g.DrawString("Set a name to be credited for the sync contribution\n", sf3, subBrush3, new PointF(S(8), _headerH + S(5))); 

            using var lf = Body(10f, _scale, FontStyle.Regular);
            using var labelBrush = new SolidBrush(Color.Silver);
            g.DrawString("Input Name", lf, labelBrush, new PointF(_pad-5, _headerH + S(43))); 
        
            using var inputBgBrush = new SolidBrush(Color.FromArgb(10, 5, 5));
            using var inputBorderPen = new Pen(CGoldDim, S(1));
            
            var inputRect = new Rectangle(_pad, _headerH + S(63), Width - _pad * 2, S(32)); 
            
            g.FillRectangle(inputBgBrush, inputRect);
            g.DrawRectangle(inputBorderPen, inputRect.X, inputRect.Y, inputRect.Width, inputRect.Height);
        }

        private Button MakeBtn(string label, Color accent, Point loc)
        {
            var b = new Button
            {
                Text      = label,
                Location  = loc,
                Width     = S(130),
                Height    = S(34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(28, accent),
                ForeColor = accent,
                Font      = Title(9f, _scale, FontStyle.Bold), 
                Cursor    = Cursors.Hand,
            };
            b.FlatAppearance.BorderColor = accent;
            b.FlatAppearance.BorderSize  = 1;
            b.MouseEnter += (_, _) => { b.BackColor = Color.FromArgb(60, accent); b.ForeColor = Color.White; };
            b.MouseLeave += (_, _) => { b.BackColor = Color.FromArgb(28, accent); b.ForeColor = accent; };
            return b;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _pulseTimer?.Stop();
                _pulseTimer?.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; }
        }
    }
}