using System.IO;
using RedfurSync;
using Xunit;

namespace RedfurSync.Tests;

public sealed class KioskReconScannerTests
{
    [Fact]
    public void ReadKiosks_ParsesKioskObservations()
    {
        using var tempDir = new TemporaryDirectory();
        var sampleLua = @"
FissalRelay_SavedVariables = {
    [""kiosks""] = {
        [""Shuzug""] = {
            [""trader""] = ""Shuzug"",
            [""guildId""] = 2,
            [""guildName""] = ""Redfur Trading Post"",
            [""zone""] = ""Reaper's March"",
            [""city""] = ""Rawl'kha"",
            [""x""] = ""0.4567"",
            [""y""] = ""0.8912"",
            [""timestamp""] = 1725330000,
            [""observedBy""] = ""@EchoTiger""
        },
        [""Gals North-Wind""] = {
            [""trader""] = ""Gals North-Wind"",
            [""guildId""] = 3,
            [""guildName""] = ""Rawl'kha Traders"",
            [""zone""] = ""Deshaan"",
            [""city""] = ""Mournhold"",
            [""timestamp""] = 1725331000,
            [""observedBy""] = ""@Fissal""
        }
    },
    [""staff""] = {}
}
";
        var filePath = tempDir.WriteFile("FissalRelay.lua", sampleLua);
        var kiosks = KioskReconScanner.ReadKiosks(filePath);

        Assert.Equal(2, kiosks.Count);
        var k1 = kiosks.Find(k => k.Trader == "Shuzug");
        Assert.NotNull(k1);
        Assert.Equal("Redfur Trading Post", k1.GuildName);
        Assert.Equal("Rawl'kha", k1.City);
        Assert.Equal("@EchoTiger", k1.ObservedBy);

        var k2 = kiosks.Find(k => k.Trader == "Gals North-Wind");
        Assert.NotNull(k2);
        Assert.Equal("Rawl'kha Traders", k2.GuildName);
        Assert.Equal("Mournhold", k2.City);
    }

    [Fact]
    public void ExportKiosksMarkdown_ProducesMarkdownTable()
    {
        var kiosks = new List<KioskObservation>
        {
            new KioskObservation
            {
                Trader = "Shuzug",
                City = "Rawl'kha",
                Zone = "Reaper's March",
                GuildName = "Redfur Trading Post",
                ObservedBy = "@EchoTiger",
                Timestamp = 1725330000
            }
        };

        var md = KioskReconScanner.ExportKiosksMarkdown(kiosks);
        Assert.Contains("| Kiosk Trader | Location / Zone | Holding Guild | Observed By | Time (UTC) |", md);
        Assert.Contains("**Shuzug**", md);
        Assert.Contains("Rawl'kha, Reaper's March", md);
        Assert.Contains("**Redfur Trading Post**", md);
        Assert.Contains("`@EchoTiger`", md);
    }

    [Fact]
    public void ExportInactivesCsv_ProducesValidCsv()
    {
        var audit = new InactivityAudit
        {
            GuildName = "Redfur Trading Post",
            GuildId = 2,
            MinDays = 14,
            Members = new List<InactiveMember>
            {
                new InactiveMember { Name = "@OldPlayer", Days = 35, Rank = "Merchant", Note = "Vacation" },
                new InactiveMember { Name = "@Sleepy", Days = 18, Rank = "Member", Note = "" }
            }
        };

        var csv = KioskReconScanner.ExportInactivesCsv(audit);
        Assert.Contains("DisplayName,DaysInactive,Rank,Note", csv);
        Assert.Contains("\"@OldPlayer\",35,\"Merchant\",\"Vacation\"", csv);
        Assert.Contains("\"@Sleepy\",18,\"Member\",\"\"", csv);
    }
}
