using System.IO;
using RedfurSync;
using Xunit;

namespace RedfurSync.Tests;

public sealed class FissalRelayScannerTests
{
    [Fact]
    public void IsFissalRelayFile_IdentifiesFissalFile()
    {
        Assert.True(FissalRelayScanner.IsFissalRelayFile("FissalRelay.lua"));
        Assert.True(FissalRelayScanner.IsFissalRelayFile("fissalrelay.lua"));
        Assert.False(FissalRelayScanner.IsFissalRelayFile("GS01Data.lua"));
        Assert.False(FissalRelayScanner.IsFissalRelayFile("PriceTableNA.lua"));
    }

    [Fact]
    public void ReadSaleIds_ParsesFormattedIds()
    {
        using var tempDir = new TemporaryDirectory();
        var sampleLua = @"
FissalRelay_SavedVariables = {
    [""sales""] = {
        [""10001""] = {
            [""id""] = ""10001"",
            [""price""] = 500,
            [""seller""] = ""@EchoTiger""
        },
        [""10002""] = {
            [""id""] = ""10002"",
            [""price""] = 1200,
            [""seller""] = ""@Fissal""
        }
    }
}
";
        var filePath = tempDir.WriteFile("FissalRelay.lua", sampleLua);
        var saleIds = FissalRelayScanner.ReadSaleIds(filePath);

        Assert.Equal(2, saleIds.Count);
        Assert.Contains("10001", saleIds);
        Assert.Contains("10002", saleIds);
    }
}
