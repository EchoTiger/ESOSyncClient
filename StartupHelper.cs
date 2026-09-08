using Microsoft.Win32;
using System;

namespace RedfurSync
{
    internal static class StartupHelper
    {
        private const string AppName      = "FissalCogworkCourier";
        private const string RegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        public static void SetStartup(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true);
                if (key == null) return;

                if (enable)
                {
                    // Environment.ProcessPath is the correct way to get the exe path
                    // in a single-file published app — Assembly.Location returns empty string
                    var exePath = Environment.ProcessPath ?? AppContext.BaseDirectory;
                    key.SetValue(AppName, $"\"{exePath}\" --startup");
                }
                else
                {
                    key.DeleteValue(AppName, throwOnMissingValue: false);
                }

                // Clean up any stale legacy run keys
                key.DeleteValue("ESOSyncClient", throwOnMissingValue: false);
                key.DeleteValue("RedfurSync", throwOnMissingValue: false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Fissal] Could not update startup registry: {ex.Message}");
            }
        }

        public static bool IsStartupEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true);
                if (key == null) return false;

                var val = key.GetValue(AppName) as string;
                if (string.IsNullOrWhiteSpace(val)) return false;

                var currentExe = Environment.ProcessPath ?? AppContext.BaseDirectory;
                string expected = $"\"{currentExe}\" --startup";
                if (!string.Equals(val, expected, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(val, $"\"{currentExe}\"", StringComparison.OrdinalIgnoreCase))
                {
                    // Self-heal stale path from previous relocation or drive
                    key.SetValue(AppName, expected);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
