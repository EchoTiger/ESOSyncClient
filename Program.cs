using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace RedfurSync
{
    internal static class Program
    {
        private static Mutex? _mutex;
        public const string MutexName = "FissalCogworkCourier_SingleInstance";
        public const string WakeEventName = "FissalRelay_ActivateEvent";

        [STAThread]
        static void Main()
        {
            // ── DPI awareness ─────────────────────────────────────────────────
            // Must be called before anything else to prevent blurry text on
            // high-DPI / 4K displays. PerMonitorV2 lets each monitor use its
            // own scaling factor and re-scales when windows move between them.
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            AppConfig.FaultReporter = (title, message) => FissalBox.Show(message, title);

            _mutex = new Mutex(true, MutexName, out bool isNew);

            if (!isNew)
            {
                // Signal the running instance to smoothly reveal and bring the Terminal forward!
                try
                {
                    if (EventWaitHandle.TryOpenExisting(WakeEventName, out var wakeEvent))
                    {
                        using (wakeEvent)
                        {
                            wakeEvent.Set();
                        }
                    }
                }
                catch
                {
                    // Fall back quietly; do not pop up an unstyled generic MessageBox
                }
                return;
            }

            // Only the primary instance may clean a stale ".old" — a second instance
            // must never delete the backup while the first is mid-update.
            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                string oldExe = exePath + ".old";
                if (File.Exists(oldExe))
                {
                    try { File.Delete(oldExe); } catch { /* It will be deleted next time */ }
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using var app = new TrayApp();
            Application.Run();

            _mutex.ReleaseMutex();
        }
    }
}
