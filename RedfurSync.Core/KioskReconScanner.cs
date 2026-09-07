using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

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
    private static readonly Regex KioskBlockRegex = new(
        @"\[""([^""]+)""\]\s*=\s*\{([^}]+)\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static List<KioskObservation> ReadKiosks(string filePath)
    {
        var observations = new List<KioskObservation>();
        if (!File.Exists(filePath)) return observations;

        string content;
        try
        {
            content = File.ReadAllText(filePath, Encoding.UTF8);
        }
        catch
        {
            return observations;
        }

        // Find the kiosks table block
        int kiosksIdx = content.IndexOf("[\"kiosks\"]", StringComparison.OrdinalIgnoreCase);
        if (kiosksIdx < 0) return observations;

        // Substring from kiosks forward up to next major top-level key
        int staffIdx = content.IndexOf("[\"staff\"]", kiosksIdx, StringComparison.OrdinalIgnoreCase);
        string section = staffIdx > kiosksIdx
            ? content.Substring(kiosksIdx, staffIdx - kiosksIdx)
            : content.Substring(kiosksIdx);

        var matches = KioskBlockRegex.Matches(section);
        foreach (Match match in matches)
        {
            var traderKey = match.Groups[1].Value.Trim();
            var block = match.Groups[2].Value;

            string GetString(string key)
            {
                var m = Regex.Match(block, $@"\[""{key}""\]\s*=\s*""([^""]*)""", RegexOptions.CultureInvariant);
                return m.Success ? m.Groups[1].Value.Trim() : "";
            }

            long GetLong(string key)
            {
                var m = Regex.Match(block, $@"\[""{key}""\]\s*=\s*(\d+)", RegexOptions.CultureInvariant);
                return m.Success && long.TryParse(m.Groups[1].Value, out var val) ? val : 0;
            }

            int GetInt(string key)
            {
                var m = Regex.Match(block, $@"\[""{key}""\]\s*=\s*(\d+)", RegexOptions.CultureInvariant);
                return m.Success && int.TryParse(m.Groups[1].Value, out var val) ? val : 0;
            }

            var trader = GetString("trader");
            if (string.IsNullOrEmpty(trader)) trader = traderKey;

            var guildName = GetString("guildName");
            if (string.IsNullOrEmpty(guildName)) continue;

            observations.Add(new KioskObservation
            {
                Trader = trader,
                GuildId = GetInt("guildId"),
                GuildName = guildName,
                Zone = GetString("zone"),
                City = GetString("city"),
                X = GetString("x"),
                Y = GetString("y"),
                Timestamp = GetLong("timestamp"),
                ObservedBy = GetString("observedBy"),
            });
        }

        return observations;
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
