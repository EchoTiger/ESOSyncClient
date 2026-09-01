using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace RedfurSync
{
    internal sealed class FissalHarnessService
    {
        private readonly AppConfig _config;

        public FissalHarnessService(AppConfig config)
        {
            _config = config;
        }

        public string DescribePermissions(bool writeEnabled)
        {
            return writeEnabled
                ? "Write access: Relay settings only (display name, startup, debounce, log retention, scale, and visual fidelity). Every change is validated, backed up, and restored if saving fails. ESO files and arbitrary paths remain read-only."
                : "Read access: Relay status, recognized ESO file names, sizes, timestamps, and recent sync errors. File contents and arbitrary paths are never shared.";
        }

        public string GetCommandContract()
        {
            return "If a local settings change is needed, append exactly one line in this format: "
                + "<fissal-action>{\"action\":\"set_setting\",\"setting\":\"DebounceMs\",\"value\":\"5000\",\"reason\":\"brief reason\"}</fissal-action>. "
                + "Allowed settings: DisplayName, RunOnStartup, DebounceMs, MaxLogsKept, AppScale, VisualFidelity. "
                + "Do not claim the change succeeded; the Relay executes, validates, and reports the result locally.";
        }

        public (bool ok, string message) Execute(string actionJson)
        {
            string? backupPath = null;
            string? setting = null;
            string? previousValue = null;
            try
            {
                using var document = JsonDocument.Parse(actionJson);
                var root = document.RootElement;
                if (!root.TryGetProperty("action", out var action)
                    || !string.Equals(action.GetString(), "set_setting", StringComparison.OrdinalIgnoreCase))
                    return (false, "The requested local action is not supported.");

                setting = root.TryGetProperty("setting", out var settingValue) ? settingValue.GetString() : null;
                var value = root.TryGetProperty("value", out var valueElement) ? valueElement.ToString() : null;
                if (string.IsNullOrWhiteSpace(setting) || value == null)
                    return (false, "The local action did not include a setting and value.");

                backupPath = BackupConfiguration();
                previousValue = ApplySetting(setting, value);
                _config.Save();
                ValidateSavedConfiguration();
                return (true, $"Updated {setting} from {previousValue} to {value}. A recovery backup was created.");
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrWhiteSpace(backupPath)) RestoreConfiguration(backupPath);
                if (!string.IsNullOrWhiteSpace(setting) && previousValue != null)
                {
                    try { ApplySetting(setting, previousValue); } catch { }
                }
                return (false, $"The change was rejected and the previous configuration was restored: {ex.Message}");
            }
        }

        private string ApplySetting(string setting, string value)
        {
            switch (setting.ToLowerInvariant())
            {
                case "displayname":
                    var normalized = UploadService.NormalizeLabel(value)
                        ?? throw new InvalidOperationException("Display name contains unsupported characters.");
                    var previousName = _config.DisplayName;
                    _config.DisplayName = normalized;
                    return previousName;
                case "runonstartup":
                    var previousStartup = _config.RunOnStartup;
                    _config.RunOnStartup = ParseBool(value);
                    return previousStartup.ToString();
                case "debouncems":
                    var previousDebounce = _config.DebounceMs;
                    _config.DebounceMs = ParseInt(value, 500, 60000, "DebounceMs");
                    return previousDebounce.ToString(CultureInfo.InvariantCulture);
                case "maxlogskept":
                    var previousLogs = _config.MaxLogsKept;
                    _config.MaxLogsKept = ParseInt(value, 1, 100, "MaxLogsKept");
                    return previousLogs.ToString(CultureInfo.InvariantCulture);
                case "appscale":
                    var previousScale = _config.AppScale;
                    if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) || scale < 0.75f || scale > 2f)
                        throw new InvalidOperationException("AppScale must be between 0.75 and 2.0.");
                    _config.AppScale = scale;
                    return previousScale.ToString(CultureInfo.InvariantCulture);
                case "visualfidelity":
                    var previousFidelity = _config.VisualFidelity;
                    if (!Enum.TryParse<FidelityMode>(value, true, out var fidelity))
                        throw new InvalidOperationException("VisualFidelity must be Low, Medium, or High.");
                    _config.VisualFidelity = fidelity;
                    return previousFidelity.ToString();
                default:
                    throw new InvalidOperationException($"{setting} is outside the Relay settings sandbox.");
            }
        }

        private static bool ParseBool(string value)
        {
            if (bool.TryParse(value, out var parsed)) return parsed;
            throw new InvalidOperationException("The value must be true or false.");
        }

        private static int ParseInt(string value, int minimum, int maximum, string setting)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                && parsed >= minimum && parsed <= maximum) return parsed;
            throw new InvalidOperationException($"{setting} must be between {minimum} and {maximum}.");
        }

        private static string BackupConfiguration()
        {
            Directory.CreateDirectory(AppConfig.ConfigDirectory);
            var backupPath = Path.Combine(AppConfig.ConfigDirectory, "config.fissal-recovery.json");
            if (File.Exists(AppConfig.ConfigPath)) File.Copy(AppConfig.ConfigPath, backupPath, true);
            return backupPath;
        }

        private static void ValidateSavedConfiguration()
        {
            using var document = JsonDocument.Parse(File.ReadAllText(AppConfig.ConfigPath));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Saved configuration did not pass validation.");
        }

        private static void RestoreConfiguration(string backupPath)
        {
            try
            {
                if (File.Exists(backupPath)) File.Copy(backupPath, AppConfig.ConfigPath, true);
            }
            catch
            {
            }
        }
    }
}