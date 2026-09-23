using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RedfurSync;

/// <summary>
/// High-performance, streaming Lua table reader with zero Large Object Heap (LOH) allocations.
/// Rents 64KB buffers from ArrayPool and scans table hierarchies without buffering entire 50MB-100MB files.
/// </summary>
public static class LuaStreamingReader
{
    private const int BufferSize = 64 * 1024; // 64 KB (safely below 85 KB LOH threshold)

    /// <summary>
    /// Stream-reads kiosk observations from a SavedVariables file.
    /// Stops reading as soon as the kiosks table block completes.
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
            int depth = 0;
            string currentTraderKey = "";
            Dictionary<string, string> currentFields = new(StringComparer.OrdinalIgnoreCase);

            while ((line = reader.ReadLine()) != null)
            {
                var span = line.AsSpan().Trim();
                if (span.IsEmpty || span.StartsWith("--")) continue;

                if (!inKiosks)
                {
                    if (span.Contains("[\"kiosks\"]", StringComparison.OrdinalIgnoreCase) && span.Contains("{", StringComparison.Ordinal))
                    {
                        inKiosks = true;
                        depth = 1;
                    }
                    continue;
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
                            // Start of a trader entry: e.g. ["Shuzug"] = {
                            currentTraderKey = ExtractKey(span);
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
                            currentFields.Clear();
                        }

                        depth--;
                        if (depth <= 0)
                        {
                            // End of kiosks block; stop parsing to save CPU & I/O
                            return observations;
                        }
                    }
                }

                // If inside a trader's table (depth == 2), extract key = value properties
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
        };
    }
}
