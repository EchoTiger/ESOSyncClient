using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using static RedfurSync.FissalTheme;

namespace RedfurSync
{
    /// <summary>
    /// A custom, fully themed replacement for the standard Windows MessageBox.
    /// </summary>
    public sealed class FissalBox : Form
    {
        // ── Native magic to allow dragging borderless forms ──
        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        private const int BaseW       = 420;
        private const int BaseHeaderH = 45;
        private const int BasePad     = 20;

        private readonly float _scale;
        private readonly string _message;
        private readonly string _title;
        private readonly MessageBoxButtons _buttons;
        
        // Layout calculations
        private readonly int _headerH;
        private readonly int _pad;
        private Rectangle _crtRect;

        /// <summary>
        /// Summons the FissalBox. Use this exactly like MessageBox.Show().
        /// </summary>
        public static DialogResult Show(string text, string title = "Tonal Matrix Alert", MessageBoxButtons buttons = MessageBoxButtons.OK)
        {
            using var box = new FissalBox(text, title, buttons);
            return box.ShowDialog();
        }

        private FissalBox(string text, string title, MessageBoxButtons buttons)
        {
            _message = text;
            _title   = title;
            _buttons = buttons;

            AutoScaleMode   = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar   = false;
            TopMost         = true;
            BackColor       = CBg;
            DoubleBuffered  = true;
            StartPosition   = FormStartPosition.CenterScreen;

            var _h = Handle; // Force handle creation
            _scale   = GetScale(Handle);
            _pad     = S(BasePad);
            _headerH = S(BaseHeaderH);

            Width = S(BaseW);

            // ── Dynamically measure the height of the text ──
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            using var f = Body(11f, _scale); // The font used for the message
            
            int boxX = _pad;
            int boxY = _headerH + S(15);
            int boxW = Width - (_pad * 2);
            int textMaxW = boxW - S(24); // S(12) padding on each side for the text
            
            var textSize = g.MeasureString(_message, f, textMaxW);

            int boxH = (int)textSize.Height + S(24);
            _crtRect = new Rectangle(boxX, boxY, boxW, boxH);

            // ── Set dynamic form height based on the text ──
            int btnY = _crtRect.Bottom + S(20);
            Height = btnY + S(45) + _pad;

            BuildButtons(btnY);

            // ── The Drag Snare ──
            MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ReleaseCapture();
                    SendMessage(Handle, 0xA1, 0x2, 0); // Trick Windows into dragging
                }
            };
        }

        private void BuildButtons(int btnY)
        {
            if (_buttons == MessageBoxButtons.OK)
            {
                var btn = MakeBtn("Acknowledge", CGreen, new Point((Width - S(130)) / 2, btnY), DialogResult.OK);
                Controls.Add(btn);
            }
            else if (_buttons == MessageBoxButtons.YesNo)
            {
                var yesBtn = MakeBtn("Yes", CGreen, new Point(Width / 2 - S(140), btnY), DialogResult.Yes);
                var noBtn  = MakeBtn("No", CBarFail, new Point(Width / 2 + S(10), btnY), DialogResult.No);
                Controls.Add(yesBtn);
                Controls.Add(noBtn);
            }
            else if (_buttons == MessageBoxButtons.OKCancel)
            {
                var okBtn     = MakeBtn("Confirm", CGreen, new Point(Width / 2 - S(140), btnY), DialogResult.OK);
                var cancelBtn = MakeBtn("Abort", CBarFail, new Point(Width / 2 + S(10), btnY), DialogResult.Cancel);
                Controls.Add(okBtn);
                Controls.Add(cancelBtn);
            }
        }

        private Button MakeBtn(string label, Color accent, Point loc, DialogResult result)
        {
            var b = new Button
            {
                Text         = label,
                Location     = loc,
                Width        = S(130),
                Height       = S(34),
                FlatStyle    = FlatStyle.Flat,
                BackColor    = Color.FromArgb(28, accent),
                ForeColor    = accent,
                Font         = Title(9f, _scale, FontStyle.Bold), 
                Cursor       = Cursors.Hand,
                DialogResult = result
            };
            b.FlatAppearance.BorderColor = accent;
            b.FlatAppearance.BorderSize  = 1;
            b.MouseEnter += (_, _) => { b.BackColor = Color.FromArgb(60, accent); b.ForeColor = Color.White; };
            b.MouseLeave += (_, _) => { b.BackColor = Color.FromArgb(28, accent); b.ForeColor = accent; };
            return b;
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

            DrawCornerRivets(g, Width, Height, S(7), CGoldDim);

            // Header
            using var hg = new LinearGradientBrush(
                new Point(0, 0), new Point(0, _headerH),
                Color.FromArgb(44, 34, 17), Color.FromArgb(20, 15, 8));
            g.FillRectangle(hg, 0, 0, Width, _headerH);
            DrawDivider(g, S(10), Width - S(10), _headerH - 1, CGoldDim, CGoldMid);

            // ── The Hazy, Glowing Title ──
            using var tf = Title(12f, _scale, FontStyle.Bold);
            string titleText = $"> {_title.ToUpper()}";
            
            using var glowBrush = new SolidBrush(Color.FromArgb(60, CGoldMid));
            float tx = _pad;
            float ty = S(14);
            
            for (int hx = -1; hx <= 1; hx++)
            {
                for (int hy = -1; hy <= 1; hy++)
                {
                    if (hx == 0 && hy == 0) continue;
                    g.DrawString(titleText, tf, glowBrush, new PointF(tx + (hx * S(1)), ty + (hy * S(1))));
                }
            }
            
            using var titleBrush = new SolidBrush(CGoldBrt);
            g.DrawString(titleText, tf, titleBrush, new PointF(tx, ty));

            // ── The CRT Screen Body ──
            using var crtBg = new SolidBrush(Color.FromArgb(8, 8, 10)); // Deep screen void
            g.FillRectangle(crtBg, _crtRect.X, _crtRect.Y, _crtRect.Width, _crtRect.Height);

            using var crtBorder = new Pen(Color.FromArgb(40, CGoldDim), S(1));
            g.DrawRectangle(crtBorder, _crtRect.X, _crtRect.Y, _crtRect.Width, _crtRect.Height);

            // ── Terminal Scanlines ──
            using var scanPen = new Pen(Color.FromArgb(15, CGreen), 1);
            for (int i = _crtRect.Y + 2; i < _crtRect.Y + _crtRect.Height; i += 3)
            {
                g.DrawLine(scanPen, _crtRect.X + 1, i, _crtRect.X + _crtRect.Width - 1, i);
            }

            // ── Message Text ──
            using var ff = Body(11f, _scale);
            using var textBrush = new SolidBrush(CText);
            var textRenderRect = new RectangleF(_crtRect.X + S(12), _crtRect.Y + S(12), _crtRect.Width - S(24), _crtRect.Height - S(24));
            g.DrawString(_message, ff, textBrush, textRenderRect);
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; }
        }
    }
}