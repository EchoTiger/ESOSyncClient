using System;
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

            // ── The Mechanical Housing (Clipping Mask) ──
            var inputHousing = new Panel
            {
                Location  = new Point(_pad, _headerH + S(20)),
                Width     = S(BaseW) - _pad * 2-8,
                Height    = S(28), // The exact height you want the box to visually be
                BackColor = CGoldDark, // Your contrasting background color!
            };

            // Draw the custom border directly onto the housing
            inputHousing.Paint += (s, e) => 
            {
                using var p = new Pen(CGoldDim, S(3));
                e.Graphics.DrawRectangle(p, 0, 0, inputHousing.Width - 3, inputHousing.Height - 1);
            };

            // ── The Trapped Text ──
            _input = new TextBox
            {
                Text            = currentName is "Redfur Trader" or "Unknown" or "" ? "" : currentName,
                PlaceholderText = "How shall this one address you?",
                BackColor       = inputHousing.BackColor, // Match the housing perfectly
                ForeColor       = CText,
                BorderStyle     = BorderStyle.None,
                AutoSize        = false,
                Font            = Body(12.5f, _scale),
                MaxLength       = 32,
                
                // We shove the ghost gap UP into the ceiling of the panel so it is hidden!
                Location        = new Point(S(4), -S(3)), 
                
                Width           = inputHousing.Width - S(8),
                Height          = S(44), // Give it plenty of room so the bottom doesn't clip
            };

            // Lock the text box inside the housing
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
                    SendMessage(Handle, 0xA1, 0x2, 0); // Tricks Windows into dragging the form
                }
            };
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

            // Goggle dot
            int dot = S(14);
            int dy  = _headerH / 2 - dot - 5;
            using var goggleBrush = new SolidBrush(CGreen);
            g.FillEllipse(goggleBrush, _pad, dy, dot, dot);
            
            using var gogglePen = new Pen(Color.FromArgb(155, CGreen), S(1));
            g.DrawEllipse(gogglePen, _pad, dy, dot, dot);
            
            using var glintBrush = new SolidBrush(Color.FromArgb(120, Color.White));
            g.FillEllipse(glintBrush, _pad + dot / 4, dy + dot / 6, dot / 4, dot / 4);

            using var tf = Title(12f, _scale, FontStyle.Bold);
            using var titleBrush = new SolidBrush(CGoldBrt);
            g.DrawString("TONAL_RECOGNITION.4md", tf, titleBrush, new PointF(_pad + dot + S(10), S(3)));

            using var sf = Body(8.5f, _scale, FontStyle.Bold);
            using var subBrush = new SolidBrush(CGreenDim);
            g.DrawString("[FIS-DBG] Tuning frequency...", sf, subBrush, new PointF(_pad + dot + S(10), S(24)));

            using var sf2 = Body(8.5f, _scale, FontStyle.Bold);
            using var subBrush2 = new SolidBrush(CGreen);
            g.DrawString("[FIS-DBG] Data stream ready!", sf2, subBrush2, new PointF(_pad + dot + S(10), S(36)));

            using var sf3 = Body(9f, _scale, FontStyle.Bold);
            using var subBrush3 = new SolidBrush(CText);
            g.DrawString("Set a name to be credited for the sync contribution\n", sf3, subBrush3, new PointF(S(10), S(50)));

            using var lf = Body(9f, _scale, FontStyle.Regular);
            using var labelBrush = new SolidBrush(CTextSub);
            g.DrawString("Input Name", lf, labelBrush, new PointF(_pad, _headerH + S(0)));
        
            // ── Draw the custom housing for the borderless TextBox ──
            using var inputBgBrush = new SolidBrush(Color.FromArgb(10, 8, 5));
            using var inputBorderPen = new Pen(CGoldDim, S(1));
            
            // This rectangle acts as our visually perfect text box
            var inputRect = new Rectangle(_pad, _headerH + S(18), Width - _pad * 2, S(32));
            
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

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; }
        }
    }
}