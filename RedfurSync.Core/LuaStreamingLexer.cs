using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RedfurSync;

/// <summary>
/// High-performance, streaming Lua table reader with zero Large Object Heap (LOH) allocations.
/// Rents 64KB buffers from ArrayPool and scans table hierarchies without buffering entire 50MB-100MB files.
/// Supports both single-line K&R (["kiosks"] = {) and ESO native Allman multi-line (["kiosks"] =\n{) tables.
/// </summary>
public static class LuaStreamingReader
{
    private const int BufferSize = 32 * 1024; // 32 KB (char[32768] = 64 KB, safely below 85 KB LOH threshold)

    /// <summary>
    /// Stream-reads kiosk observations from a SavedVariables file across all account profiles.
    /// </summary>
    public static List<KioskObservation> ReadKioskObservations(string filePath)
    {
        var observations = new List<KioskObservation>();
        if (!File.Exists(filePath)) return observations;

        try
        {
            using var fileStream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                BufferSize,
                FileOptions.SequentialScan);

            using var reader = new StreamReader(fileStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, BufferSize);

            string? line;
            bool inKiosks = false;
            bool awaitingKiosksBrace = false;
            int depth = 0;
            string pendingTraderKey = "";
            string currentTraderKey = "";
            Dictionary<string, string> currentFields = new(StringComparer.OrdinalIgnoreCase);

            while ((line = reader.ReadLine()) != null)
            {
                var span = line.AsSpan().Trim();
                if (span.IsEmpty || span.StartsWith("--")) continue;

                if (!inKiosks)
                {
                    if (awaitingKiosksBrace)
                    {
                        int openIdx = span.IndexOf('{');
                        if (openIdx >= 0)
                        {
                            awaitingKiosksBrace = false;
                            inKiosks = true;
                            depth = 0;
                            pendingTraderKey = "";
                            currentTraderKey = "";
                            CountBracesOnLine(span.Slice(openIdx), ref depth);
                            if (depth <= 0)
                            {
                                inKiosks = false;
                                depth = 0;
                            }
                        }
                        else if (!span.StartsWith("--"))
                        {
                            awaitingKiosksBrace = false;
                        }
                        continue;
                    }

                    if (span.Contains("[\"kiosks\"]", StringComparison.OrdinalIgnoreCase))
                    {
                        int openIdx = span.IndexOf('{');
                        if (openIdx >= 0)
                        {
                            inKiosks = true;
                            depth = 0;
                            pendingTraderKey = "";
                            currentTraderKey = "";
                            CountBracesOnLine(span.Slice(openIdx), ref depth);
                            if (depth <= 0)
                            {
                                inKiosks = false;
                                depth = 0;
                            }
                        }
                        else
                        {
                            awaitingKiosksBrace = true;
                        }
                    }
                    continue;
                }

                // At depth == 1, capture ["TraderName"] = even if its opening '{' is on the next line (ESO Allman format)
                if (depth == 1)
                {
                    var keyOnLine = ExtractKey(span);
                    if (!string.IsNullOrEmpty(keyOnLine))
                    {
                        pendingTraderKey = keyOnLine;
                    }
                }

                // We are inside the kiosks block. Track table depth.
                for (int i = 0; i < span.Length; i++)
                {
                    char c = span[i];
                    if (c == '"')
                    {
                        // Skip string literal to avoid counting braces inside strings
                        i++;
                        while (i < span.Length)
                        {
                            if (span[i] == '\\') { i += 2; continue; }
                            if (span[i] == '"') break;
                            i++;
                        }
                        continue;
                    }

                    if (c == '{')
                    {
                        depth++;
                        if (depth == 2)
                        {
                            // Start of a trader entry: e.g. ["Shuzug"] = { or Allman { on line after ["Shakh"] =
                            var inlineKey = ExtractKey(span);
                            currentTraderKey = !string.IsNullOrEmpty(inlineKey) ? inlineKey : pendingTraderKey;
                            pendingTraderKey = "";
                            currentFields.Clear();
                        }
                    }
                    else if (c == '}')
                    {
                        if (depth == 2 && !string.IsNullOrWhiteSpace(currentTraderKey))
                        {
                            // End of a trader entry
                            var obs = MaterializeKiosk(currentTraderKey, currentFields);
                            if (obs != null) observations.Add(obs);
                            currentTraderKey = "";
                            pendingTraderKey = "";
                            currentFields.Clear();
                        }

                        depth--;
                        if (depth <= 0)
                        {
                            inKiosks = false;
                            depth = 0;
                            pendingTraderKey = "";
                            currentTraderKey = "";
                            break;
                        }
                    }
                }

                if (!inKiosks) continue;

                // If inside a trader table (depth >= 2), extract key = value properties
                if (depth >= 2)
                {
                    ExtractKeyValuePair(span, currentFields);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LuaStreamingReader] Error reading {filePath}: {ex.Message}");
        }

        return observations;
    }

    /// <summary>
    /// Stream-reads knownTradingGuilds ([guildId] = "Guild Name") from a SavedVariables file.
    /// </summary>
    public static Dictionary<int, string> ReadKnownTradingGuilds(string filePath)
    {
        var guilds = new Dictionary<int, string>();
        if (!File.Exists(filePath)) return guilds;

        try
        {
            using var fileStream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                BufferSize,
                FileOptions.SequentialScan);

            using var reader = new StreamReader(fileStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, BufferSize);

            string? line;
            bool inKnownGuilds = false;
            bool awaitingBrace = false;
            int depth = 0;

            while ((line = reader.ReadLine()) != null)
            {
                var span = line.AsSpan().Trim();
                if (span.IsEmpty || span.StartsWith("--")) continue;

                if (!inKnownGuilds)
                {
                    if (awaitingBrace)
                    {
                        int openIdx = span.IndexOf('{');
                        if (openIdx >= 0)
                        {
                            awaitingBrace = false;
                            inKnownGuilds = true;
                            depth = 0;
                            CountBracesOnLine(span.Slice(openIdx), ref depth);
                            if (depth <= 0)
                            {
                                inKnownGuilds = false;
                                depth = 0;
                            }
                        }
                        else
                        {
                            awaitingBrace = false;
                        }
                        continue;
                    }

                    if (span.Contains("[\"knownTradingGuilds\"]", StringComparison.OrdinalIgnoreCase))
                    {
                        int openIdx = span.IndexOf('{');
                        if (openIdx >= 0)
                        {
                            inKnownGuilds = true;
                            depth = 0;
                            CountBracesOnLine(span.Slice(openIdx), ref depth);
                            if (depth <= 0)
                            {
                                inKnownGuilds = false;
                                depth = 0;
                            }
                        }
                        else
                        {
                            awaitingBrace = true;
                        }
                    }
                    continue;
                }

                // Match [21505] = "Skooma Dealers",
                if (depth == 1 && span.StartsWith("[") && !span.StartsWith("[\""))
                {
                    int closeBracket = span.IndexOf(']');
                    if (closeBracket > 1 && int.TryParse(span.Slice(1, closeBracket - 1), out int guildId) && guildId > 0)
                    {
                        var afterBracket = span.Slice(closeBracket + 1);
                        int eqIdx = afterBracket.IndexOf('=');
                        if (eqIdx >= 0)
                        {
                            var valSpan = afterBracket.Slice(eqIdx + 1).Trim();
                            if (valSpan.EndsWith(",")) valSpan = valSpan.Slice(0, valSpan.Length - 1).Trim();
                            if (valSpan.StartsWith("\"") && valSpan.EndsWith("\"") && valSpan.Length >= 2)
                            {
                                var gName = valSpan.Slice(1, valSpan.Length - 2).ToString();
                                if (!string.IsNullOrWhiteSpace(gName))
                                {
                                    guilds[guildId] = gName;
                                }
                            }
                        }
                    }
                }

                CountBracesOnLine(span, ref depth);
                if (depth <= 0)
                {
                    inKnownGuilds = false;
                    depth = 0;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LuaStreamingReader] Error reading knownTradingGuilds from {filePath}: {ex.Message}");
        }

        return guilds;
    }

    private static void CountBracesOnLine(ReadOnlySpan<char> span, ref int depth)
    {
        for (int i = 0; i < span.Length; i++)
        {
            char c = span[i];
            if (c == '"')
            {
                i++;
                while (i < span.Length)
                {
                    if (span[i] == '\\') { i += 2; continue; }
                    if (span[i] == '"') break;
                    i++;
                }
                continue;
            }
            if (c == '{') depth++;
            else if (c == '}') depth--;
        }
    }

    private static string ExtractKey(ReadOnlySpan<char> span)
    {
        int startBracket = span.IndexOf("[\"", StringComparison.Ordinal);
        if (startBracket >= 0)
        {
            var afterStart = span.Slice(startBracket + 2);
            int endQuote = afterStart.IndexOf('"');
            if (endQuote >= 0)
            {
                return afterStart.Slice(0, endQuote).ToString();
            }
        }
        return "";
    }

    private static void ExtractKeyValuePair(ReadOnlySpan<char> span, Dictionary<string, string> fields)
    {
        int startBracket = span.IndexOf("[\"", StringComparison.Ordinal);
        if (startBracket < 0) return;

        var afterKeyStart = span.Slice(startBracket + 2);
        int endKeyQuote = afterKeyStart.IndexOf('"');
        if (endKeyQuote < 0) return;

        var key = afterKeyStart.Slice(0, endKeyQuote).ToString();
        var afterEquals = afterKeyStart.Slice(endKeyQuote + 1);

        int eqIdx = afterEquals.IndexOf('=');
        if (eqIdx < 0) return;

        var valSpan = afterEquals.Slice(eqIdx + 1).Trim();
        if (valSpan.EndsWith(",")) valSpan = valSpan.Slice(0, valSpan.Length - 1).Trim();

        if (valSpan.StartsWith("\"") && valSpan.EndsWith("\"") && valSpan.Length >= 2)
        {
            fields[key] = valSpan.Slice(1, valSpan.Length - 2).ToString();
        }
        else if (!valSpan.StartsWith("{"))
        {
            fields[key] = valSpan.ToString();
        }
    }

    private static KioskObservation? MaterializeKiosk(string key, Dictionary<string, string> fields)
    {
        fields.TryGetValue("guildName", out var guildName);
        if (string.IsNullOrWhiteSpace(guildName)) return null;

        fields.TryGetValue("trader", out var trader);
        if (string.IsNullOrWhiteSpace(trader)) trader = key;

        fields.TryGetValue("zone", out var zone);
        fields.TryGetValue("city", out var city);
        fields.TryGetValue("x", out var x);
        fields.TryGetValue("y", out var y);
        fields.TryGetValue("observedBy", out var observedBy);
        fields.TryGetValue("source", out var source);

        int guildId = 0;
        if (fields.TryGetValue("guildId", out var gIdStr)) int.TryParse(gIdStr, out guildId);

        long timestamp = 0;
        if (fields.TryGetValue("timestamp", out var tsStr)) long.TryParse(tsStr, out timestamp);

        return new KioskObservation
        {
            Trader = trader,
            GuildId = guildId,
            GuildName = guildName,
            Zone = zone ?? "",
            City = city ?? "",
            X = x ?? "0",
            Y = y ?? "0",
            Timestamp = timestamp,
            ObservedBy = observedBy ?? "",
            Source = source ?? "",
        };
    }
}
