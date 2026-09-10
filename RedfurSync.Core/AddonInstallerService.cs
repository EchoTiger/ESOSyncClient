using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RedfurSync
{
    public static class AddonInstallerService
    {
        public const string AddonDirectoryName = "FissalRelay";
        public const string ClientDirectoryName = "Client";
        public const string TargetExeName = "RedfurSync.exe";
        public const string TtcPriceTableUrl = "https://us.tamrieltradecentre.com/download/PriceTable";

        public static string? FindEsoLiveDirectory(Func<string>? customProvider = null)
        {
            if (customProvider != null)
            {
                try
                {
                    var custom = customProvider();
                    if (Directory.Exists(custom)) return custom;
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

        public static void InstallAddonFiles(string esoLiveDir)
        {
            var addonDir = Path.Combine(esoLiveDir, "AddOns", AddonDirectoryName);
            Directory.CreateDirectory(addonDir);

            var manifestPath = Path.Combine(addonDir, "FissalRelay.txt");
            if (!File.Exists(manifestPath))
            {
                File.WriteAllText(manifestPath, AddonManifestTemplate);
            }

            var luaPath = Path.Combine(addonDir, "FissalRelay.lua");
            if (!File.Exists(luaPath))
            {
                File.WriteAllText(luaPath, AddonLuaTemplate);
            }

            var uiPath = Path.Combine(addonDir, "FissalRelay_UI.lua");
            if (!File.Exists(uiPath))
            {
                File.WriteAllText(uiPath, AddonUiTemplate);
            }
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
## Version: 1.2.5
## AddOnVersion: 10205
## APIVersion: 101048 101049
## SavedVariables: FissalRelay_SavedVariables
## DependsOn: LibHistoire>=1062 LibAddonMenu-2.0>=41
## OptionalDependsOn: LibCustomMenu

FissalRelay.lua
FissalRelay_UI.lua
";

        private const string AddonLuaTemplate = @"--[[ Fissal's Cogwork Relay ]]--
FissalRelay = FissalRelay or {}
local FR = FissalRelay
FR.name = ""FissalRelay""
FR.version = ""1.2.5""
FR.processors = {}

local DEFAULT_SAVED_VARS = {
    version = 2,
    historyDepthDays = 30,
    lastSeenEventId = {},
    kiosks = {},
    sales = {},
    staff = { bankDeposits = {}, rosterSnapshots = {}, inactivityAudits = {} },
    settings = { chatAnnouncements = true, soundEffects = false, trackAllGuilds = true, autoRosterSnapshotOnLogin = true, announceKioskRecon = true }
}

function FR:AddSale(event, guildId)
    local info = event:GetEventInfo()
    if not info then return false end
    local eventIdStr = tostring(event:GetEventId())
    if self.savedVars.sales[eventIdStr] then return false end

    local wasKiosk = true
    if info.buyerDisplayName and info.buyerDisplayName ~= """" and GetGuildMemberIndexFromDisplayName(guildId, info.buyerDisplayName) then
        wasKiosk = false
    end

    self.savedVars.sales[eventIdStr] = {
        id = eventIdStr,
        timestamp = event:GetEventTimestampS() or GetTimeStamp(),
        guildId = guildId,
        guildName = GetGuildName(guildId),
        seller = info.sellerDisplayName or """",
        buyer = info.buyerDisplayName or """",
        itemLink = info.itemLink or """",
        price = info.price or 0,
        quant = info.quantity or 1,
        wasKiosk = wasKiosk,
    }
    self.savedVars.lastSeenEventId[guildId] = eventIdStr
    return true
end

function FR:SetupProcessors()
    if not LibHistoire or not LibHistoire.IsReady or not LibHistoire:IsReady() then return end
    for i = 1, GetNumGuilds() do
        local guildId = GetGuildId(i)
        if not self.processors[""trader_"" .. guildId] then
            local p = LibHistoire:CreateGuildHistoryProcessor(guildId, GUILD_HISTORY_EVENT_CATEGORY_TRADER, ""FissalRelay"")
            if p then
                local lastId = tonumber(self.savedVars.lastSeenEventId[guildId])
                if lastId then p:SetAfterEventId(lastId)
                else p:SetAfterEventTime(GetTimeStamp() - (self.savedVars.historyDepthDays * 86400)) end
                p:SetEventCallback(function(e)
                    if e:GetEventType() == GUILD_HISTORY_TRADER_EVENT_ITEM_SOLD then FR:AddSale(e, guildId) end
                end)
                p:Start()
                self.processors[""trader_"" .. guildId] = p
            end
        end
    end
end

EVENT_MANAGER:RegisterForEvent(FR.name, EVENT_ADD_ON_LOADED, function(_, name)
    if name ~= FR.name then return end
    EVENT_MANAGER:UnregisterForEvent(FR.name, EVENT_ADD_ON_LOADED)
    FR.savedVars = ZO_SavedVars:NewAccountWide(""FissalRelay_SavedVariables"", 1, nil, DEFAULT_SAVED_VARS)
    SLASH_COMMANDS[""/fissal""] = function() d(""|cFF9900[Fissal]|r Clockwork Courier active. Recorded sales: "" .. NonContiguousCount(FR.savedVars.sales)) end
    if LibHistoire and LibHistoire.OnReady then
        LibHistoire:OnReady(function() FR:SetupProcessors() end)
    end
end)
";

        private const string AddonUiTemplate = @"--[[ FissalRelay_UI.lua ]]--
FissalRelay = FissalRelay or {}
";
    }
}
