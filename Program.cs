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
        static void Main(string[] args)
        {
            // ── DPI awareness ─────────────────────────────────────────────────
            // Must be called before anything else to prevent blurry text on
            // high-DPI / 4K displays. PerMonitorV2 lets each monitor use its
            // own scaling factor and re-scales when windows move between them.
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            FissalTheme.EnableDarkModeSupport();

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) =>
            {
                LogCrash(e.Exception);
                FissalBox.Show(
                    "Fissal encountered an unexpected apparatus disturbance:\n\n" +
                    e.Exception.Message + "\n\n" +
                    "A trace has been recorded to crash.log.",
                    "Fissal Relay // Disturbance");
            };
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    LogCrash(ex);
                    MessageBox.Show(
                        "Fissal encountered a fatal core disturbance:\n\n" +
                        ex.Message + "\n\n" +
                        "A trace has been recorded to crash.log.",
                        "Fissal Relay Fatal Disturbance",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            };

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

            // ── Self-Relocation into ESO AddOns Chamber ─────────────────────────────
            if (AddonInstallerService.ShouldRelocate(out var targetExePath, out var esoLiveDir))
            {
                try
                {
                    AddonInstallerService.RelocateSelfAndCreateShortcut(esoLiveDir, targetExePath);
                    FissalBox.Show(
                        "Fissal has relocated our Relay into your ESO AddOns chamber:\n\n" +
                        targetExePath + "\n\n" +
                        "A desktop shortcut 'Fissal Relay' has been forged, and the FissalRelay in-game addon is installed!\n\n" +
                        "Launching from your new brass chamber now...",
                        "Fissal's Cogwork Relocation");

                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = targetExePath,
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(targetExePath)
                    };
                    System.Diagnostics.Process.Start(psi);
                    _mutex.ReleaseMutex();
                    return;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Program] Relocation failed: {ex.Message}");
                }
            }
            else
            {
                var liveDir = AddonInstallerService.FindEsoLiveDirectory();
                if (!string.IsNullOrWhiteSpace(liveDir))
                {
                    try { AddonInstallerService.InstallAddonFiles(liveDir); } catch { }
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool startMinimized = false;
            foreach (var arg in args)
            {
                if (arg.Equals("--startup", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("--tray", StringComparison.OrdinalIgnoreCase))
                {
                    startMinimized = true;
                    break;
                }
            }

            using var app = new TrayApp(startMinimized);
            Application.Run();

            _mutex.ReleaseMutex();
        }

        private static void LogCrash(Exception ex)
        {
            try
            {
                var crashFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                File.AppendAllText(crashFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
            }
            catch { }
        }
    }
}
