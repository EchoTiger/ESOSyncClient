using System;
using System.Threading;
using System.Windows.Forms;

namespace RedfurSync
{
    internal static class Program
    {
        private static Mutex? _mutex;

        [STAThread]
        static void Main()
        {
            // ── DPI awareness ─────────────────────────────────────────────────
            // Must be called before anything else to prevent blurry text on
            // high-DPI / 4K displays. PerMonitorV2 lets each monitor use its
            // own scaling factor and re-scales when windows move between them.
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                string oldExe = exePath + ".old";
                if (File.Exists(oldExe))
                {
                    try { File.Delete(oldExe); } catch { /* It will be deleted next time */ }
                }
            }

            _mutex = new Mutex(true, "FissalCogworkCourier_SingleInstance", out bool isNew);

            if (!isNew)
            {
                MessageBox.Show(
                    "Fissal Relay is already active!\n\nCheck your system tray where your computer's time is!",
                    "Fissal Relay",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using var app = new TrayApp();
            Application.Run();

            _mutex.ReleaseMutex();
        }
    }
}
