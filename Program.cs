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
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);
        private const int ASFW_ANY = -1;


        [STAThread]
        static void Main(string[] args)
        {
            try { AllowSetForegroundWindow(ASFW_ANY); } catch { }
            TraceLog("Main started. Args: " + string.Join(" ", args));

            // ── DPI & Visual Styles (must precede any WinForms handle or dialog) ──
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            FissalTheme.EnableDarkModeSupport();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool isPostUpdateVerify = false;
            bool startMinimized = false;
            foreach (var arg in args)
            {
                if (arg.Equals("--post-update-verify", StringComparison.OrdinalIgnoreCase))
                    isPostUpdateVerify = true;
                else if (arg.Equals("--startup", StringComparison.OrdinalIgnoreCase) ||
                         arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                         arg.Equals("--tray", StringComparison.OrdinalIgnoreCase))
                    startMinimized = true;
            }

            // ── Post-update verification handshake (MUST run before RecoverPendingUpdate) ──
            if (isPostUpdateVerify)
            {
                TraceLog("Executing post-update verification...");
                try
                {
                    _ = AppConfig.Instance;
                    var installDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory;
                    var healthyPath = Path.Combine(installDir, "HEALTHY");
                    File.WriteAllText(healthyPath, $"HEALTHY {DateTimeOffset.UtcNow:O}");
                    TraceLog("HEALTHY sentinel written successfully.");
                }
                catch (Exception ex)
                {
                    TraceLog($"Post-update verification failed: {ex.Message}");
                    return;
                }
            }
            else
            {
                // ── Crash-during-commit recovery (Fable 5.1 Ruling 3.5) ──────────────
                var installDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory;
                UpdateInstaller.RecoverPendingUpdate(installDir);
            }

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
            bool ownsMutex = isNew;
            TraceLog("Mutex checked. isNew: " + isNew);

            if (!isNew)
            {
                try { AllowSetForegroundWindow(ASFW_ANY); } catch { }

                if (!isPostUpdateVerify)
                {
                    // Try to signal the running instance to wake
                    bool signaled = false;
                    try
                    {
                        if (EventWaitHandle.TryOpenExisting(WakeEventName, out var wakeEvent))
                        {
                            using (wakeEvent) { wakeEvent.Set(); }
                            signaled = true;
                        }
                    }
                    catch { }

                    if (signaled)
                    {
                        return;
                    }
                }

                // During --post-update-verify, give the previous instance up to 25s to observe HEALTHY and exit.
                var mutexWait = isPostUpdateVerify ? TimeSpan.FromSeconds(25) : TimeSpan.FromSeconds(5);
                TraceLog(isPostUpdateVerify
                    ? "Post-update handover — waiting up to 25s for previous instance to release mutex..."
                    : "Wake event not found — previous instance may be dead. Attempting mutex takeover...");
                try
                {
                    bool acquired = _mutex.WaitOne(mutexWait, false);
                    if (!acquired)
                    {
                        TraceLog("Could not acquire mutex. Another instance may still be running.");
                        return;
                    }
                    ownsMutex = true;
                    TraceLog("Mutex acquired — proceeding as primary.");
                }
                catch (AbandonedMutexException)
                {
                    ownsMutex = true;
                    TraceLog("Abandoned mutex recovered. Proceeding as primary.");
                }
            }

            // Only the primary instance may clean stale update backups
            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                foreach (var suffix in new[] { ".old", ".prev", ".xvol.tmp" })
                {
                    string stale = exePath + suffix;
                    if (File.Exists(stale))
                    {
                        try { File.Delete(stale); } catch { }
                    }
                }
                try
                {
                    var baseDir = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;
                    foreach (var p in Directory.EnumerateFiles(baseDir, "*.prev"))
                    {
                        try { File.Delete(p); } catch { }
                    }
                }
                catch { }
            }

            // ── Self-Relocation into %LOCALAPPDATA%\Programs\FissalRelay ──────────
            if (!isPostUpdateVerify && AddonInstallerService.ShouldRelocate(out var targetExePath, out var esoLiveDir))
            {
                try
                {
                    bool hadStartup = StartupHelper.IsStartupEnabled();
                    AddonInstallerService.RelocateSelfAndCreateShortcut(esoLiveDir, targetExePath);

                    if (hadStartup || (AppConfig.Instance.RunOnStartup &&
                        (!string.IsNullOrWhiteSpace(AppConfig.Instance.DeviceToken) || !string.IsNullOrWhiteSpace(AppConfig.Instance.ApiKey))))
                    {
                        StartupHelper.SetStartupForPath(targetExePath, true);
                    }

                    if (!startMinimized)
                    {
                        string addonNote = !string.IsNullOrWhiteSpace(esoLiveDir)
                            ? "A desktop shortcut 'Fissal Relay' has been forged, and the FissalRelay in-game addon is installed!\n\n"
                            : "A desktop shortcut 'Fissal Relay' has been forged! You can link your ESO folder anytime in the Addon tab.\n\n";

                        FissalBox.Show(
                            "Fissal has anchored our Relay in your local programs chamber:\n\n" +
                            targetExePath + "\n\n" +
                            addonNote +
                            "Launching from your brass chamber now...",
                            "Fissal's Cogwork Relocation");
                    }

                    // Release mutex BEFORE spawning relocated instance so child acquires ownership immediately
                    if (ownsMutex)
                    {
                        SafeReleaseMutex();
                        ownsMutex = false;
                    }

                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = targetExePath,
                        Arguments = startMinimized ? "--startup" : string.Empty,
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(targetExePath)
                    };
                    System.Diagnostics.Process.Start(psi);
                    return;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Program] Relocation failed: {ex.Message}");
                    TraceLog($"Relocation FAILED: {ex.Message}\n{ex.StackTrace}");
                }
            }
            else
            {
                var liveDir = AddonInstallerService.FindEsoLiveDirectory();
                if (!string.IsNullOrWhiteSpace(liveDir))
                {
                    try
                    {
                        var status = AddonInstallerService.CheckAddonInstallStatus(liveDir);
                        if (status.NeedsUpdate)
                        {
                            AddonInstallerService.InstallAddonFiles(liveDir);
                        }
                    }
                    catch { }
                }
            }

            FissalTheme.SetTheme(AppConfig.Instance.Theme);
            TraceLog("Creating TrayApp...");
            using var app = new TrayApp(startMinimized);
            TraceLog("Starting Application.Run...");
            Application.Run();
            TraceLog("Application.Run returned!");

            if (ownsMutex)
            {
                SafeReleaseMutex();
            }
        }

        internal static void SafeReleaseMutex()
        {
            var m = _mutex;
            if (m == null) return;
            try { m.ReleaseMutex(); }
            catch (ApplicationException) { }
            catch (ObjectDisposedException) { }
        }

        private static string GetLogFilePath(string fileName)
        {
            var logDir = Path.Combine(AppConfig.ConfigDirectory, "logs");
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, fileName);
            try
            {
                var fi = new FileInfo(logPath);
                if (fi.Exists && fi.Length > 2 * 1024 * 1024)
                {
                    var rotated = logPath + ".1";
                    File.Move(logPath, rotated, overwrite: true);
                }
            }
            catch { }
            return logPath;
        }

        private static void TraceLog(string msg)
        {
            try
            {
                var p = GetLogFilePath("debug.log");
                File.AppendAllText(p, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
            }
            catch { }
        }

        private static void LogCrash(Exception ex)
        {
            try
            {
                var crashFile = GetLogFilePath("crash.log");
                File.AppendAllText(crashFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
            }
            catch { }
        }
    }
}
