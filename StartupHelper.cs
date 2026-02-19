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
                    key.SetValue(AppName, $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue(AppName, throwOnMissingValue: false);
                }
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
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
                return key?.GetValue(AppName) != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
