using System.IO;
using RedfurSync;
using Xunit;

namespace RedfurSync.Tests;

public sealed class AddonInstallerServiceTests
{
    [Fact]
    public void GetTargetExePath_BuildsCanonicalAddonPath()
    {
        var esoLive = @"C:\Games\Elder Scrolls Online\live";
        var target = AddonInstallerService.GetTargetExePath(esoLive);

        Assert.Equal(Path.Combine(esoLive, "AddOns", "FissalRelay", "Client", "RedfurSync.exe"), target);
    }

    [Fact]
    public void InstallAddonFiles_CreatesTemplatesIfMissing()
    {
        using var tempDir = new TemporaryDirectory();
        AddonInstallerService.InstallAddonFiles(tempDir.Path);

        var addonDir = Path.Combine(tempDir.Path, "AddOns", "FissalRelay");
        Assert.True(File.Exists(Path.Combine(addonDir, "FissalRelay.txt")));
        Assert.True(File.Exists(Path.Combine(addonDir, "FissalRelay.lua")));
        Assert.True(File.Exists(Path.Combine(addonDir, "FissalRelay_UI.lua")));
    }

    [Fact]
    public void ShouldRelocate_ReturnsTrueWhenOutsideAddonDir()
    {
        using var tempDir = new TemporaryDirectory();
        var liveDir = Path.Combine(tempDir.Path, "live");
        Directory.CreateDirectory(Path.Combine(liveDir, "AddOns"));

        var shouldRelocate = AddonInstallerService.ShouldRelocate(out var targetPath, out var foundLive, () => liveDir);

        Assert.True(shouldRelocate);
        Assert.Equal(liveDir, foundLive);
        Assert.Equal(Path.Combine(liveDir, "AddOns", "FissalRelay", "Client", "RedfurSync.exe"), targetPath);
    }

    [Fact]
    public void ParseManifestVersion_ExtractsVersionAndCode()
    {
        var manifest = @"## Title: Fissal's Relay
## Version: 1.2.6
## AddOnVersion: 10206
## Author: Echo";

        var (version, versionCode) = AddonInstallerService.ParseManifestVersion(manifest);

        Assert.Equal("1.2.6", version);
        Assert.Equal(10206, versionCode);
    }

    [Fact]
    public void CheckAddonInstallStatus_DetectsNotFound_WhenNoLiveDir()
    {
        var status = AddonInstallerService.CheckAddonInstallStatus("/nonexistent/eso/path");

        Assert.Equal(AddonInstallState.EsoNotFound, status.State);
        Assert.False(status.EsoLiveFound);
        Assert.False(status.AddonInstalled);
    }

    [Fact]
    public void CheckAddonInstallStatus_DetectsNotInstalled_WhenAddonMissing()
    {
        using var temp = new TemporaryDirectory();
        var liveDir = Path.Combine(temp.Path, "live");
        Directory.CreateDirectory(Path.Combine(liveDir, "AddOns"));

        var status = AddonInstallerService.CheckAddonInstallStatus(liveDir);

        Assert.Equal(AddonInstallState.NotInstalled, status.State);
        Assert.True(status.EsoLiveFound);
        Assert.False(status.AddonInstalled);
        Assert.True(status.NeedsUpdate);
    }

    [Fact]
    public void CheckAddonInstallStatus_DetectsUpdateAvailable_WhenOlderVersion()
    {
        using var temp = new TemporaryDirectory();
        var liveDir = Path.Combine(temp.Path, "live");
        var addonDir = Path.Combine(liveDir, "AddOns", "FissalRelay");
        Directory.CreateDirectory(addonDir);

        File.WriteAllText(Path.Combine(addonDir, "FissalRelay.txt"), "## Title: Fissal\n## Version: 1.2.5\n## AddOnVersion: 10205\n");

        var status = AddonInstallerService.CheckAddonInstallStatus(liveDir);

        Assert.Equal(AddonInstallState.UpdateAvailable, status.State);
        Assert.True(status.AddonInstalled);
        Assert.True(status.NeedsUpdate);
        Assert.Equal("1.2.5", status.InstalledVersion);
        Assert.Equal(AddonInstallerService.LatestAddonVersion, status.LatestVersion);
    }

    [Fact]
    public void CheckAddonInstallStatus_DetectsUpToDate_WhenLatestVersion()
    {
        using var temp = new TemporaryDirectory();
        var liveDir = Path.Combine(temp.Path, "live");
        var addonDir = Path.Combine(liveDir, "AddOns", "FissalRelay");
        Directory.CreateDirectory(addonDir);

        File.WriteAllText(Path.Combine(addonDir, "FissalRelay.txt"),
            $"## Title: Fissal\n## Version: {AddonInstallerService.LatestAddonVersion}\n## AddOnVersion: {AddonInstallerService.LatestAddonVersionCode}\n");

        var status = AddonInstallerService.CheckAddonInstallStatus(liveDir);

        Assert.Equal(AddonInstallState.UpToDate, status.State);
        Assert.True(status.AddonInstalled);
        Assert.False(status.NeedsUpdate);
        Assert.Equal(AddonInstallerService.LatestAddonVersion, status.InstalledVersion);
    }

    [Fact]
    public void CheckAddonInstallStatus_DetectsDependencies()
    {
        using var temp = new TemporaryDirectory();
        var liveDir = Path.Combine(temp.Path, "live");
        var addonsRoot = Path.Combine(liveDir, "AddOns");
        Directory.CreateDirectory(Path.Combine(addonsRoot, "LibHistoire"));
        Directory.CreateDirectory(Path.Combine(addonsRoot, "LibAddonMenu-2.0"));

        var status = AddonInstallerService.CheckAddonInstallStatus(liveDir);

        Assert.True(status.LibHistoireInstalled);
        Assert.True(status.LibAddonMenuInstalled);
    }

    [Fact]
    public void InstallOrUpdateAddon_WritesLatestVersionFiles()
    {
        using var temp = new TemporaryDirectory();
        var liveDir = Path.Combine(temp.Path, "live");
        Directory.CreateDirectory(liveDir);

        var success = AddonInstallerService.InstallOrUpdateAddon(liveDir, out var msg);

        Assert.True(success);
        Assert.Contains("installed successfully", msg);

        var manifestPath = Path.Combine(liveDir, "AddOns", "FissalRelay", "FissalRelay.txt");
        Assert.True(File.Exists(manifestPath));

        var (ver, code) = AddonInstallerService.ParseManifestVersion(File.ReadAllText(manifestPath));
        Assert.Equal(AddonInstallerService.LatestAddonVersion, ver);
        Assert.Equal(AddonInstallerService.LatestAddonVersionCode, code);
    }

    [Fact]
    public void GetAddonFileContent_ReturnsContentForAddonFiles()
    {
        var txt = AddonInstallerService.GetAddonFileContent("FissalRelay.txt");
        var lua = AddonInstallerService.GetAddonFileContent("FissalRelay.lua");
        var ui = AddonInstallerService.GetAddonFileContent("FissalRelay_UI.lua");

        Assert.NotNull(txt);
        Assert.NotNull(lua);
        Assert.NotNull(ui);
        Assert.Contains("FissalRelay", txt);
        Assert.Contains("FissalRelay", lua);
    }
}
