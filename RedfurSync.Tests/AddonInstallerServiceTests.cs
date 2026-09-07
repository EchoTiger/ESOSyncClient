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
        // Create mock live directory structure
        var liveDir = Path.Combine(tempDir.Path, "live");
        Directory.CreateDirectory(Path.Combine(liveDir, "AddOns"));

        var shouldRelocate = AddonInstallerService.ShouldRelocate(out var targetPath, out var foundLive, () => liveDir);

        Assert.True(shouldRelocate);
        Assert.Equal(liveDir, foundLive);
        Assert.Equal(Path.Combine(liveDir, "AddOns", "FissalRelay", "Client", "RedfurSync.exe"), targetPath);
    }
}
