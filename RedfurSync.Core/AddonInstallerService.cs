using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace RedfurSync
{
    public enum AddonInstallState
    {
        EsoNotFound,
        NotInstalled,
        UpdateAvailable,
        UpToDate
    }

    public sealed class AddonStatusResult
    {
        public AddonInstallState State { get; init; }
        public bool EsoLiveFound => State != AddonInstallState.EsoNotFound;
        public string? EsoLiveDirectory { get; init; }
        public string? AddonDirectory { get; init; }
        public bool AddonInstalled => State == AddonInstallState.UpToDate || State == AddonInstallState.UpdateAvailable;
        public string? InstalledVersion { get; init; }
        public int? InstalledVersionCode { get; init; }
        public string LatestVersion { get; init; } = AddonInstallerService.LatestAddonVersion;
        public bool NeedsUpdate => State == AddonInstallState.UpdateAvailable || State == AddonInstallState.NotInstalled;
        public bool LibHistoireInstalled { get; init; }
        public bool LibAddonMenuInstalled { get; init; }
        public string StatusMessage { get; init; } = string.Empty;
    }

    public static class AddonInstallerService
    {
        public const string AddonDirectoryName = "FissalRelay";
        public const string ClientDirectoryName = "Client";
        public const string TargetExeName = "RedfurSync.exe";
        public const string LatestAddonVersion = "1.2.8";
        public const int LatestAddonVersionCode = 10208;
        public const string TtcPriceTableUrl = "https://us.tamrieltradecentre.com/download/PriceTable";

        public static string ActiveLatestAddonVersion { get; set; } = LatestAddonVersion;
        public static string? RemoteAddonDownloadUrl { get; set; } = null;

        public static async Task<bool> CheckRemoteAddonVersionAsync(string? serverUrl = null)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                string baseUri = !string.IsNullOrWhiteSpace(serverUrl) ? serverUrl.TrimEnd('/') : "https://redfur.ech-o.net";
                string url = $"{baseUri}/api/relay/v1/download";
                var json = await http.GetStringAsync(url);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("addonVersion", out var verProp))
                {
                    var remoteVer = verProp.GetString();
                    if (!string.IsNullOrWhiteSpace(remoteVer))
                    {
                        ActiveLatestAddonVersion = remoteVer;
                    }
                }
                if (doc.RootElement.TryGetProperty("addonUrl", out var urlProp))
                {
                    RemoteAddonDownloadUrl = urlProp.GetString();
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string? FindEsoLiveDirectory(Func<string>? customProvider = null)
        {
            if (customProvider != null)
            {
                try
                {
                    var custom = customProvider();
                    if (!string.IsNullOrWhiteSpace(custom) && Directory.Exists(custom)) return custom;
                }
                catch { }
            }

            var candidates = new List<string>
            {
                @"E:\Files\Documents\Elder Scrolls Online\live",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Elder Scrolls Online", "live"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "Elder Scrolls Online", "live"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive", "Documents", "Elder Scrolls Online", "live"),
            };

            // Check standard drives
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                candidates.Add(Path.Combine(drive.RootDirectory.FullName, "Files", "Documents", "Elder Scrolls Online", "live"));
                candidates.Add(Path.Combine(drive.RootDirectory.FullName, "Documents", "Elder Scrolls Online", "live"));
            }

            foreach (var path in candidates)
            {
                if (Directory.Exists(path) && Directory.Exists(Path.Combine(path, "AddOns")))
                {
                    return path;
                }
            }

            // Secondary check if just live directory exists
            foreach (var path in candidates)
            {
                if (Directory.Exists(path)) return path;
            }

            return null;
        }

        public static string GetTargetExePath(string esoLiveDir)
        {
            return Path.Combine(esoLiveDir, "AddOns", AddonDirectoryName, ClientDirectoryName, TargetExeName);
        }

        public static bool ShouldRelocate(out string targetExePath, out string esoLiveDir, Func<string>? customProvider = null)
        {
            targetExePath = string.Empty;
            esoLiveDir = string.Empty;

            var foundLive = FindEsoLiveDirectory(customProvider);
            if (string.IsNullOrWhiteSpace(foundLive)) return false;

            esoLiveDir = foundLive;
            targetExePath = GetTargetExePath(foundLive);

            var currentExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe)) return false;

            var currentDir = Path.GetDirectoryName(currentExe);
            var targetDir = Path.GetDirectoryName(targetExePath);

            if (string.IsNullOrWhiteSpace(currentDir) || string.IsNullOrWhiteSpace(targetDir)) return false;

            // If already in target directory or in AddOns\FissalRelay, no relocation needed
            if (string.Equals(Path.GetFullPath(currentDir), Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase))
                return false;

            var parentTargetDir = Path.GetDirectoryName(targetDir);
            if (parentTargetDir != null && string.Equals(Path.GetFullPath(currentDir), Path.GetFullPath(parentTargetDir), StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        public static (string? version, int? versionCode) ParseManifestVersion(string manifestContent)
        {
            if (string.IsNullOrWhiteSpace(manifestContent)) return (null, null);

            string? version = null;
            int? versionCode = null;

            using var reader = new StringReader(manifestContent);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("## Version:", StringComparison.OrdinalIgnoreCase))
                {
                    version = trimmed.Substring("## Version:".Length).Trim();
                }
                else if (trimmed.StartsWith("## AddOnVersion:", StringComparison.OrdinalIgnoreCase))
                {
                    var codeStr = trimmed.Substring("## AddOnVersion:".Length).Trim();
                    if (int.TryParse(codeStr, out var code))
                    {
                        versionCode = code;
                    }
                }
            }

            return (version, versionCode);
        }

        public static AddonStatusResult CheckAddonInstallStatus(string? explicitEsoLiveDir = null, Func<string>? customProvider = null)
        {
            string? liveDir = !string.IsNullOrWhiteSpace(explicitEsoLiveDir) && Directory.Exists(explicitEsoLiveDir)
                ? explicitEsoLiveDir
                : FindEsoLiveDirectory(customProvider);

            if (string.IsNullOrWhiteSpace(liveDir) || !Directory.Exists(liveDir))
            {
                return new AddonStatusResult
                {
                    State = AddonInstallState.EsoNotFound,
                    StatusMessage = "Elder Scrolls Online live directory could not be located. Browse or enter your live folder path.",
                };
            }

            string addonsRoot = Path.Combine(liveDir, "AddOns");
            string addonDir = Path.Combine(addonsRoot, AddonDirectoryName);
            string manifestPath = Path.Combine(addonDir, "FissalRelay.txt");

            bool libHistoire = Directory.Exists(Path.Combine(addonsRoot, "LibHistoire"));
            bool libAddonMenu = Directory.Exists(Path.Combine(addonsRoot, "LibAddonMenu-2.0"));

            string effectiveLatest = ActiveLatestAddonVersion;

            if (!Directory.Exists(addonDir) || !File.Exists(manifestPath))
            {
                return new AddonStatusResult
                {
                    State = AddonInstallState.NotInstalled,
                    EsoLiveDirectory = liveDir,
                    AddonDirectory = addonDir,
                    LatestVersion = effectiveLatest,
                    LibHistoireInstalled = libHistoire,
                    LibAddonMenuInstalled = libAddonMenu,
                    StatusMessage = $"Fissal Relay addon is not installed in {addonDir}.",
                };
            }

            try
            {
                string manifestText = File.ReadAllText(manifestPath);
                var (installedVer, installedCode) = ParseManifestVersion(manifestText);

                bool updateNeeded = false;
                if (!string.IsNullOrWhiteSpace(installedVer))
                {
                    updateNeeded = RelayVersion.IsServerNewer(effectiveLatest, installedVer);
                }
                else if (installedCode.HasValue)
                {
                    updateNeeded = LatestAddonVersionCode > installedCode.Value;
                }
                else
                {
                    updateNeeded = true;
                }

                var state = updateNeeded ? AddonInstallState.UpdateAvailable : AddonInstallState.UpToDate;
                string msg = updateNeeded
                    ? $"Update available: v{installedVer ?? "unknown"} is installed, latest is v{effectiveLatest}."
                    : $"Addon is up to date (v{installedVer ?? effectiveLatest}).";

                return new AddonStatusResult
                {
                    State = state,
                    EsoLiveDirectory = liveDir,
                    AddonDirectory = addonDir,
                    InstalledVersion = installedVer,
                    InstalledVersionCode = installedCode,
                    LatestVersion = effectiveLatest,
                    LibHistoireInstalled = libHistoire,
                    LibAddonMenuInstalled = libAddonMenu,
                    StatusMessage = msg,
                };
            }
            catch (Exception ex)
            {
                return new AddonStatusResult
                {
                    State = AddonInstallState.UpdateAvailable,
                    EsoLiveDirectory = liveDir,
                    AddonDirectory = addonDir,
                    LatestVersion = effectiveLatest,
                    LibHistoireInstalled = libHistoire,
                    LibAddonMenuInstalled = libAddonMenu,
                    StatusMessage = $"Could not read addon manifest: {ex.Message}",
                };
            }
        }

        public static string? GetAddonFileContent(string filename)
        {
            var assembly = typeof(AddonInstallerService).Assembly;
            var resourceNames = assembly.GetManifestResourceNames();
            var match = resourceNames.FirstOrDefault(r => r.EndsWith(filename, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                using var s = assembly.GetManifestResourceStream(match);
                if (s != null)
                {
                    using var reader = new StreamReader(s);
                    return reader.ReadToEnd();
                }
            }

            // Fallback: check relative path on disk if available
            try
            {
                var candidatePaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AddOn", AddonDirectoryName, filename),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "AddOn", AddonDirectoryName, filename),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "AddOn", AddonDirectoryName, filename),
                };
                foreach (var path in candidatePaths)
                {
                    if (File.Exists(path)) return File.ReadAllText(path);
                }
            }
            catch { }

            // Final fallback to static templates
            return filename.ToLowerInvariant() switch
            {
                "fissalrelay.txt" => AddonManifestTemplate,
                "fissalrelay.lua" => AddonLuaTemplate,
                "fissalrelay_ui.lua" => AddonUiTemplate,
                _ => null,
            };
        }

        public static bool InstallOrUpdateAddon(string esoLiveDir, out string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(esoLiveDir) || !Directory.Exists(esoLiveDir))
                {
                    message = "Invalid Elder Scrolls Online live directory path.";
                    return false;
                }

                var addonDir = Path.Combine(esoLiveDir, "AddOns", AddonDirectoryName);
                Directory.CreateDirectory(addonDir);

                var clientDir = Path.Combine(addonDir, ClientDirectoryName);
                Directory.CreateDirectory(clientDir);

                var manifest = GetAddonFileContent("FissalRelay.txt") ?? AddonManifestTemplate;
                var lua = GetAddonFileContent("FissalRelay.lua") ?? AddonLuaTemplate;
                var ui = GetAddonFileContent("FissalRelay_UI.lua") ?? AddonUiTemplate;

                File.WriteAllText(Path.Combine(addonDir, "FissalRelay.txt"), manifest);
                File.WriteAllText(Path.Combine(addonDir, "FissalRelay.lua"), lua);
                File.WriteAllText(Path.Combine(addonDir, "FissalRelay_UI.lua"), ui);

                message = $"Fissal Relay addon v{LatestAddonVersion} installed successfully!";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Failed to install addon files: {ex.Message}";
                return false;
            }
        }

        public static void InstallAddonFiles(string esoLiveDir)
        {
            InstallOrUpdateAddon(esoLiveDir, out _);
        }

        public static void RelocateSelfAndCreateShortcut(string esoLiveDir, string targetExePath)
        {
            var targetDir = Path.GetDirectoryName(targetExePath);
            if (!string.IsNullOrWhiteSpace(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            var currentExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe)) return;

            // Install or ensure addon files
            InstallAddonFiles(esoLiveDir);

            // Copy executable
            File.Copy(currentExe, targetExePath, overwrite: true);

            // Copy config.json or app.ico if in the same folder
            var currentDir = Path.GetDirectoryName(currentExe);
            if (!string.IsNullOrWhiteSpace(currentDir) && !string.IsNullOrWhiteSpace(targetDir))
            {
                var icoPath = Path.Combine(currentDir, "app.ico");
                if (File.Exists(icoPath))
                {
                    File.Copy(icoPath, Path.Combine(targetDir, "app.ico"), overwrite: true);
                }
            }

            // Create Desktop Shortcut
            CreateDesktopShortcut(targetExePath);
        }

        public static void CreateDesktopShortcut(string targetExePath)
        {
            try
            {
                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var shortcutPath = Path.Combine(desktopPath, "Fissal Relay.lnk");

                // Use PowerShell to generate the shortcut safely without external COM dependencies
                var psScript = $"$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('{shortcutPath.Replace("'", "''")}'); $s.TargetPath = '{targetExePath.Replace("'", "''")}'; $s.WorkingDirectory = '{Path.GetDirectoryName(targetExePath)!.Replace("'", "''")}'; $s.Description = 'Fissal Cogwork Relay for Castle Echo'; $s.Save()";
                
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(3000);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AddonInstaller] Desktop shortcut creation skipped: {ex.Message}");
            }
        }

        public static void OpenFolderInExplorer(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{path}\"",
                        UseShellExecute = true,
                    });
                }
                else
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "xdg-open",
                        Arguments = $"\"{path}\"",
                        UseShellExecute = true,
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AddonInstaller] OpenFolder failed: {ex.Message}");
            }
        }

        public static async Task<bool> DownloadAndInstallTtcPriceTableAsync(string esoLiveDir, HttpClient? httpClient = null, CancellationToken cancellationToken = default)
        {
            try
            {
                var ttcDir = Path.Combine(esoLiveDir, "AddOns", "TamrielTradeCentre");
                Directory.CreateDirectory(ttcDir);

                var client = httpClient ?? new HttpClient();
                using var request = new HttpRequestMessage(HttpMethod.Get, TtcPriceTableUrl);
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) FissalRelay/1.0");

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode) return false;

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

                foreach (var entry in archive.Entries)
                {
                    if (entry.Name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                    {
                        var destFile = Path.Combine(ttcDir, entry.Name);
                        entry.ExtractToFile(destFile, overwrite: true);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AddonInstaller] TTC price table download failed: {ex.Message}");
                return false;
            }
        }

        private const string AddonManifestTemplate = @"## Title: |cFF9900Fissal's|r Cogwork Relay
## Author: Echo & Fissal
## Version: 1.2.8
## AddOnVersion: 10208
## APIVersion: 101048 101049
## SavedVariables: FissalRelay_SavedVariables
## DependsOn: LibHistoire>=1062 LibAddonMenu-2.0>=41
## OptionalDependsOn: LibCustomMenu

FissalRelay.lua
FissalRelay_UI.lua
";

        // NOTE: This template is a last-resort fallback used only if the embedded
        // resource lookup AND all disk-path searches in GetAddonFileContent() fail.
        // It installs a minimal stub that displays an error in-game so the user
        // knows to reinstall. It deliberately does NOT attempt to replicate the
        // full addon logic, which would quickly become stale and dangerous.
        private const string AddonLuaTemplate = @"--[[ FissalRelay -- EMERGENCY STUB ]]--
-- The full FissalRelay.lua could not be located during installation.
-- Please reinstall the Fissal Relay application to restore the addon.
FissalRelay = FissalRelay or {}
local FR = FissalRelay
FR.name = ""FissalRelay""
FR.version = ""0.0.0-stub""
FR.processors = {}
EVENT_MANAGER:RegisterForEvent(FR.name, EVENT_ADD_ON_LOADED, function(_, name)
    if name ~= FR.name then return end
    EVENT_MANAGER:UnregisterForEvent(FR.name, EVENT_ADD_ON_LOADED)
    df(""|cFF0000[FissalRelay]|r STUB LOADED — full addon missing. Please reinstall Fissal Relay."")
end)
";

        private const string AddonUiTemplate = @"--[[ FissalRelay_UI.lua ]]--
FissalRelay = FissalRelay or {}
";
    }
}
