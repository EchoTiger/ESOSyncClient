using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using static RedfurSync.FissalTheme;

namespace RedfurSync
{
    /// <summary>
    /// Fissal's farewell dialog — shown before the app shuts down.
    /// Returns DialogResult.OK to confirm shutdown, Cancel to abort.
    /// </summary>
    public sealed class GoodbyeForm : Form
    {
        private const int BaseW = 380;
        
        private readonly float _scale;
        private readonly Button _shutdownBtn;
        private readonly Button _stayBtn;
        
        // Holding the chosen farewell safely so it doesn't shift beneath our paws
        private readonly string _chosenFarewell; 
        private readonly int _attrY;

        // Fissal's farewell lines — one is chosen at random
        private static readonly string[] Farewells =
        {
            "Safe travels! Fissal's ears will be listening for your return!",
            "Harmonic tunneling prepared to shut down!\nThis one will rest his chassis until you return.",
            "Powering down the tonal matrix!\nUntil next time friend!\nMay your coffers overflow.",
            "Until next time, friend! The moons grow quiet.\nRemember to log your sales!",
            "The Lunar Relay closes for the eve.\nYour sale data is safely stored in the vault!",
        };

        public GoodbyeForm()
        {
            AutoScaleMode   = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar   = false;
            TopMost         = true;
            BackColor       = CBg;
            DoubleBuffered  = true;
            StartPosition   = FormStartPosition.CenterScreen;

            var _h = Handle;
            _scale = GetScale(Handle);

            Width  = S(BaseW);
            
            // Fissal chooses her words once, when the window awakens
            _chosenFarewell = Farewells[Math.Abs(Environment.TickCount % Farewells.Length)];

            // We let our digital senses test the space required for her voice
            using var dummyG = Graphics.FromHwnd(IntPtr.Zero);
            using var ff = Body(12f, _scale, FontStyle.Regular);
            
            int textMarginX = S(48); // Left and right padding combined
            var sz = dummyG.MeasureString(_chosenFarewell, ff, Width - textMarginX);
            
            // Give her a comfortable minimum height, but stretch if she is feeling talkative
            int measuredTextH = (int)Math.Ceiling(sz.Height);
            int textH = Math.Max(S(45), measuredTextH);

            int hh = S(54); // Header height
            _attrY = hh + S(18) + textH + S(10); // Position the signature just below the text
            int btnY = _attrY + S(28);           // Position the buttons below the signature
            
            // The form dynamically snaps its height to contain everything perfectly
            Height = btnY + S(54);

            // Shutdown button
            _shutdownBtn = MakeBtn("Stop Sync", CBarFail, new Point(S(20), btnY));
            _shutdownBtn.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };

            // Stay button
            _stayBtn = MakeBtn("Stay Connected", CGreen, new Point(Width - S(20) - S(140), btnY));
            _stayBtn.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(_shutdownBtn);
            Controls.Add(_stayBtn);

            // Allow Escape to cancel
            KeyPreview = true;
            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
                if (e.KeyCode == Keys.Enter)  { DialogResult = DialogResult.OK;     Close(); }
            };
        }

        private int S(int v) => (int)Math.Round(v * _scale);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode   = PixelOffsetMode.HighQuality;

            // ── Background ───────────────────────────────────────────────────
            using var bgBrush = new SolidBrush(CBg);
            g.FillRectangle(bgBrush, 0, 0, Width, Height);

            using var borderPen = new Pen(CBorder, S(1));
            g.DrawRectangle(borderPen, 1, 1, Width - 2, Height - 2);
            
            using var dimPen = new Pen(Color.FromArgb(38, CGoldDim), S(1));
            g.DrawRectangle(dimPen, S(4), S(4), Width - S(8), Height - S(8));

            DrawCornerRivets(g, Width, Height, S(8), CGoldDim);

            // ── Header band ──────────────────────────────────────────────────
            int hh = S(54);
            using var hg = new LinearGradientBrush(
                new Point(0, 0), new Point(0, hh),
                Color.FromArgb(42, 33, 18), Color.FromArgb(20, 15, 8));
            g.FillRectangle(hg, 0, 0, Width, hh);

            DrawDivider(g, S(12), Width - S(12), hh, CGoldDim, CGoldMid);

            // Gear glyphs flanking the title
            using var gearFont = Title(18f, _scale);
            using var gearBrush = new SolidBrush(Color.FromArgb(60, CGoldDim));
            g.DrawString("⚙", gearFont, gearBrush, new PointF(S(12), S(10)));
            g.DrawString("⚙", gearFont, gearBrush, new PointF(Width - S(12) - g.MeasureString("⚙", gearFont).Width, S(10)));

            // Title
            using var tf = Title(13f, _scale);
            var tsz = g.MeasureString("FISSAL RELAY", tf);
            using var titleBrush = new SolidBrush(CGoldBrt);
            g.DrawString("FISSAL RELAY", tf, titleBrush, new PointF((Width - tsz.Width) / 2f, S(13)));

            // ── Farewell message ─────────────────────────────────────────────
            using var ff = Body(12f, _scale, FontStyle.Regular);
            
            // The bounds organically expand based on the height calculated during awakening
            var fRect = new RectangleF(S(24), hh + S(18), Width - S(48), _attrY - (hh + S(18)));
            using var sf = new StringFormat
            {
                Alignment     = StringAlignment.Center,
                LineAlignment = StringAlignment.Near,
            };
            
            // Shadow pass
            using var shadowBrush = new SolidBrush(Color.FromArgb(60, Color.Black));
            g.DrawString(_chosenFarewell, ff, shadowBrush, new RectangleF(fRect.X + 1, fRect.Y + 1, fRect.Width, fRect.Height), sf);
            
            // Main text
            using var textBrush = new SolidBrush(CText);
            g.DrawString(_chosenFarewell, ff, textBrush, fRect, sf);

            // Small "— Fissal" attribution resting softly beneath the text
            using var atf = Body(10f, _scale, FontStyle.Italic);
            var attr = "- Fissal";
            var asz  = g.MeasureString(attr, atf);
            using var attrBrush = new SolidBrush(CTextSub);
            g.DrawString(attr, atf, attrBrush, new PointF((Width - asz.Width) / 2f, _attrY));
        }

        private Button MakeBtn(string label, Color accent, Point loc)
        {
            var b = new Button
            {
                Text      = label,
                Location  = loc,
                Width     = S(140),
                Height    = S(34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(30, accent),
                ForeColor = accent,
                Font      = Title(9f, _scale, FontStyle.Bold), 
                Cursor    = Cursors.Hand,
            };
            b.FlatAppearance.BorderColor = accent;
            b.FlatAppearance.BorderSize  = 1;
            b.MouseEnter += (_, _) => { b.BackColor = Color.FromArgb(65, accent); b.ForeColor = Color.White; };
            b.MouseLeave += (_, _) => { b.BackColor = Color.FromArgb(30, accent); b.ForeColor = accent; };
            return b;
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; }
        }
    }
}