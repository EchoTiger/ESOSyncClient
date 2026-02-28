using System;
using System.IO;
using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Reflection;

namespace RedfurSync
{
    /// <summary>
    /// Shared theme constants, DPI helpers, and font factories for all Fissal windows.
    /// </summary>
    internal static class FissalTheme
    {
        // ── Palette ───────────────────────────────────────────────────────────
        public static readonly Color CBg        = Color.FromArgb(12, 9, 5);
        public static readonly Color CPanelBg   = Color.FromArgb(22, 17, 10);
        public static readonly Color CBorder    = Color.FromArgb(90, 72, 36);
        public static readonly Color CGoldBrt   = Color.FromArgb(218, 182, 88);
        public static readonly Color CGoldMid   = Color.FromArgb(160, 128, 55);
        public static readonly Color CGoldDim   = Color.FromArgb(95,  74, 30);
        public static readonly Color CGoldDark   = Color.FromArgb(64, 50, 20);
        public static readonly Color CGreen     = Color.FromArgb(55,  200, 100);
        public static readonly Color CGreenDim  = Color.FromArgb(28,  100,  50);
        public static readonly Color CText      = Color.FromArgb(232, 215, 185);
        public static readonly Color CTextSub   = Color.FromArgb(130, 112, 82);
        public static readonly Color CBarBg     = Color.FromArgb(30,  24, 14);
        public static readonly Color CBarDone   = Color.FromArgb(55,  185, 82);
        public static readonly Color CBarFail   = Color.FromArgb(170,  55, 42);
        public static readonly Color CBarActive = Color.FromArgb(210, 175, 80);
        public static readonly Color CBarCancel = Color.FromArgb(72,  65, 52);
        public static readonly Color CBtnBg     = Color.FromArgb(32,  26, 14);
        public static readonly Color CBtnBorder = Color.FromArgb(88,  70, 34);
        public static readonly Color CErrBg     = Color.FromArgb(38,  16, 10);
        public static readonly Color CErrBorder = Color.FromArgb(110,  40, 28);
        public static readonly Color CSep       = Color.FromArgb(55,  44, 22);
        public static readonly Color CBtnDark  = Color.FromArgb(85, 85, 85); 
        public static readonly Color CBtnLight  = Color.FromArgb(150, 150, 150);

        // ── P/Invoke — the only reliable way to get per-monitor DPI ──────────
        [DllImport("user32.dll")] private static extern int GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsProcessDPIAware();

        /// <summary>
        /// Returns the true DPI scale factor for the monitor a window is on.
        /// Call AFTER the form's Handle has been created.
        /// Falls back gracefully on older Windows versions.
        /// </summary>
        public static float GetScale(IntPtr hwnd)
        {
            try
            {
                int dpi = GetDpiForWindow(hwnd);
                if (dpi > 0) return dpi / 96f;
            }
            catch { /* GetDpiForWindow requires Win10 1607+ */ }

            // Fallback: read from a Graphics context on the window itself
            try
            {
                using var g = Graphics.FromHwnd(hwnd);
                return g.DpiX / 96f;
            }
            catch { }

            return 1f;
        }

        // ── Font stack ────────────────────────────────────────────────────────
                
        // P/Invoke to register the font globally for native controls (TextBox, Button)
        [DllImport("gdi32.dll")]
        private static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, [In] ref uint pcFonts);

        private static readonly PrivateFontCollection _pfc = new PrivateFontCollection();
        private static readonly FontFamily? _customFont;
        static FissalTheme()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                string? actualResourceName = null;

                // The bulletproof hunt: we let Fissal sniff out the font dynamically
                foreach (string name in assembly.GetManifestResourceNames())
                {
                    if (name.EndsWith("RetroFontMain.ttf", StringComparison.OrdinalIgnoreCase))
                    {
                        actualResourceName = name;
                        break; // Caught it!
                    }
                }

                if (actualResourceName != null)
                {
                    using Stream? stream = assembly.GetManifestResourceStream(actualResourceName);
                    if (stream != null)
                    {
                        // Read the embedded stream into a byte array
                        byte[] fontData = new byte[stream.Length];
                        stream.ReadExactly(fontData, 0, fontData.Length);

                        // Allocate native memory for the font
                        IntPtr dataPtr = Marshal.AllocCoTaskMem(fontData.Length);
                        try
                        {
                            Marshal.Copy(fontData, 0, dataPtr, fontData.Length);

                            // 1. Load into GDI+ (For Graphics.DrawString in your OnPaint methods)
                            _pfc.AddMemoryFont(dataPtr, fontData.Length);
                            if (_pfc.Families.Length > 0)
                            {
                                _customFont = _pfc.Families[0];
                            }

                            // 2. Load into native GDI memory (For your TextBox and Button controls)
                            uint cFonts = 0;
                            AddFontMemResourceEx(dataPtr, (uint)fontData.Length, IntPtr.Zero, ref cFonts);
                        }
                        finally
                        {
                            // Safely free the allocated memory
                            Marshal.FreeCoTaskMem(dataPtr);
                        }
                    }
                }
                else
                {
                    // A soft warning growl if the compiler truly failed to pack it
                    MessageBox.Show("Fissal's hunt failed: 'RetroFont.ttf' was not found inside the compiled assembly.\nDouble check that the .csproj has the <EmbeddedResource> tag.", "Missing Font");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fissal encountered an error loading the embedded font:\n{ex.Message}", "Font Error");
            }
        }

        public static string TitleFontName => _customFont?.Name ?? "Courier New";
        public static string BodyFontName  => _customFont?.Name ?? "Courier New";
        public static string MonoFontName  => _customFont?.Name ?? "Courier New";

        // ── A quick sniff to find the main monitor's scale for the Tray Menu
        public static float GetSystemScale()
        {
            try
            {
                using var g = Graphics.FromHwnd(IntPtr.Zero);
                return g.DpiX / 96f;
            }
            catch { return 1f; }
        }

        // ── The Translation Magic
        // Converts your original "Points" to "Pixels" (1 Point = 1.333 Pixels)
        // This ensures the text stays exactly the size you designed it to be,
        // while snapping to whole pixels so the 8-bit aesthetic stays perfectly sharp!
// ── The Translation Magic
        // Tweak this number to grow or shrink all text across the entire app!
        // 1.0f is default. 1.15f makes everything 15% larger.
        public static float GlobalTextScale = 1.25f; 
        private static float PtToPx(float pt, float scale)
        {
            // Fissal now multiplies the final size by your global preference
            float px = pt * (96f / 72f) * scale * GlobalTextScale;
            return (float)Math.Round(px); 
        }

        public static Font Title(float pt, float scale, FontStyle style = FontStyle.Regular)
        {
            float pxSize = PtToPx(pt, scale);
            if (_customFont != null) 
                return new Font(_customFont, pxSize, style, GraphicsUnit.Pixel);
                
            return new Font("Courier New", pxSize, style, GraphicsUnit.Pixel);
        }

        public static Font Body(float pt, float scale, FontStyle style = FontStyle.Regular)
        {
            float pxSize = PtToPx(pt, scale);
            if (_customFont != null) 
                return new Font(_customFont, pxSize, style, GraphicsUnit.Pixel);
                
            return new Font("Courier New", pxSize, style, GraphicsUnit.Pixel);
        }

        public static Font Mono(float pt, float scale, FontStyle style = FontStyle.Regular)
        {
            float pxSize = PtToPx(pt, scale);
            if (_customFont != null) 
                return new Font(_customFont, pxSize, style, GraphicsUnit.Pixel);
                
            return new Font("Courier New", pxSize, style, GraphicsUnit.Pixel);
        }

        // ── Decorative helpers ────────────────────────────────────────────────

        public static void DrawDivider(Graphics g, int x1, int x2, int y, Color lineColor, Color diamondColor)
        {
            using var linePen = new Pen(lineColor, 1f);
            g.DrawLine(linePen, x1, y, x2, y);
            
            int cx = (x1 + x2) / 2;
            using var diamondBrush = new SolidBrush(diamondColor);
            g.FillPolygon(diamondBrush, new[]
            {
                new Point(cx,     y - 3),
                new Point(cx + 4, y),
                new Point(cx,     y + 3),
                new Point(cx - 4, y),
            });
        }

        public static void DrawCornerRivets(Graphics g, int w, int h, int margin, Color rivetColor)
        {
            int r = 8;
            
            using var rivetBrush = new SolidBrush(rivetColor);
            using var highlightPen = new Pen(Color.FromArgb(80, Color.White), 0.5f);
            
            foreach (var (rx, ry) in new[] {
                (margin, margin), (w - margin - r, margin),
                (margin, h - margin - r), (w - margin - r, h - margin - r) })
            {
                g.FillEllipse(rivetBrush, rx, ry, r, r);
                g.DrawEllipse(highlightPen, rx, ry, r, r);
            }
        }

        public static System.Drawing.Drawing2D.GraphicsPath RoundRect(float x, float y, float w, float h, float r)
        {
            var p = new System.Drawing.Drawing2D.GraphicsPath();
            p.AddArc(x,       y,       r*2, r*2, 180, 90);
            p.AddArc(x+w-r*2, y,       r*2, r*2, 270, 90);
            p.AddArc(x+w-r*2, y+h-r*2, r*2, r*2,   0, 90);
            p.AddArc(x,       y+h-r*2, r*2, r*2,  90, 90);
            p.CloseFigure();
            return p;
        }
    }
}