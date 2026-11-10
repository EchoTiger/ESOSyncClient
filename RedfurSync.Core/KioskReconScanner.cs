using System;
using System.Collections.Generic;
using System.Text;

namespace RedfurSync;

public sealed record KioskObservation
{
    public string Trader { get; init; } = "";
    public int GuildId { get; init; }
    public string GuildName { get; init; } = "";
    public string Zone { get; init; } = "";
    public string City { get; init; } = "";
    public string X { get; init; } = "0";
    public string Y { get; init; } = "0";
    public long Timestamp { get; init; }
    public string ObservedBy { get; init; } = "";
    public string Source { get; init; } = "";
}

public sealed record InactiveMember
{
    public string Name { get; init; } = "";
    public int Days { get; init; }
    public string Rank { get; init; } = "";
    public string Note { get; init; } = "";
}

public sealed record InactivityAudit
{
    public int GuildId { get; init; }
    public string GuildName { get; init; } = "";
    public int MinDays { get; init; }
    public long AuditedAt { get; init; }
    public int TotalMembers { get; init; }
    public int InactiveCount { get; init; }
    public List<InactiveMember> Members { get; init; } = new();
}

public static class KioskReconScanner
{
    public static List<KioskObservation> ReadKiosks(string filePath)
    {
        return LuaStreamingReader.ReadKioskObservations(filePath);
    }

    public static Dictionary<int, string> ReadKnownTradingGuilds(string filePath)
    {
        return LuaStreamingReader.ReadKnownTradingGuilds(filePath);
    }

    public static (List<KioskObservation> Kiosks, Dictionary<int, string> KnownGuilds) ReadKiosksAndKnownGuilds(string filePath)
    {
        return LuaStreamingReader.ReadKiosksAndKnownGuilds(filePath);
    }

    public static string ExportInactivesCsv(InactivityAudit audit)
    {
        var sb = new StringBuilder();
        sb.AppendLine("DisplayName,DaysInactive,Rank,Note");

        foreach (var m in audit.Members)
        {
            var safeNote = m.Note.Replace("\"", "\"\"");
            sb.AppendLine($"\"{m.Name}\",{m.Days},\"{m.Rank}\",\"{safeNote}\"");
        }

        return sb.ToString();
    }

    public static string ExportKiosksMarkdown(IEnumerable<KioskObservation> kiosks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("| Kiosk Trader | Location / Zone | Holding Guild | Observed By | Time (UTC) |");
        sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");

        foreach (var k in kiosks)
        {
            var dateStr = k.Timestamp > 0
                ? DateTimeOffset.FromUnixTimeSeconds(k.Timestamp).ToString("yyyy-MM-dd HH:mm")
                : "Recent";
            var loc = string.IsNullOrEmpty(k.City) ? k.Zone : $"{k.City}, {k.Zone}";
            sb.AppendLine($"| **{k.Trader}** | {loc} | **{k.GuildName}** | `{k.ObservedBy}` | {dateStr} |");
        }

        return sb.ToString();
    }
}
