using System;
using System.Collections.Generic;
using System.IO;

namespace RedfurSync
{
    public class FissalSaleRecord
    {
        public string Id { get; set; } = string.Empty;
        public long Timestamp { get; set; }
        public int GuildId { get; set; }
        public string GuildName { get; set; } = string.Empty;
        public string Seller { get; set; } = string.Empty;
        public string Buyer { get; set; } = string.Empty;
        public string ItemLink { get; set; } = string.Empty;
        public long Price { get; set; }
        public int Quant { get; set; } = 1;
        public bool WasKiosk { get; set; }
    }

    public static class FissalRelayScanner
    {
        public static bool IsFissalRelayFile(string fileName)
        {
            return string.Equals(fileName, "FissalRelay.lua", StringComparison.OrdinalIgnoreCase);
        }

        public static IReadOnlyList<string> ReadSaleIds(string filePath, int maxIds = 500_000)
        {
            var saleIds = new HashSet<string>(StringComparer.Ordinal);
            if (!File.Exists(filePath)) return new List<string>();

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            bool inSales = false;
            bool awaitingSalesBrace = false;
            int depth = 0;
            string? line;

            while ((line = reader.ReadLine()) != null)
            {
                var span = line.AsSpan().Trim();
                if (span.IsEmpty || span.StartsWith("--")) continue;

                if (!inSales)
                {
                    if (awaitingSalesBrace)
                    {
                        int openIdx = span.IndexOf('{');
                        if (openIdx >= 0)
                        {
                            awaitingSalesBrace = false;
                            inSales = true;
                            depth = 0;
                            CountBraces(span.Slice(openIdx), ref depth);
                            if (depth <= 0)
                            {
                                inSales = false;
                                depth = 0;
                            }
                        }
                        else if (!span.StartsWith("--"))
                        {
                            awaitingSalesBrace = false;
                        }
                        continue;
                    }

                    if (span.Contains("[\"sales\"]", StringComparison.OrdinalIgnoreCase))
                    {
                        int openIdx = span.IndexOf('{');
                        if (openIdx >= 0)
                        {
                            inSales = true;
                            depth = 0;
                            CountBraces(span.Slice(openIdx), ref depth);
                            if (depth <= 0)
                            {
                                inSales = false;
                                depth = 0;
                            }
                        }
                        else
                        {
                            awaitingSalesBrace = true;
                        }
                    }
                    continue;
                }

                if (span.StartsWith("[\"id\"]"))
                {
                    int startQuote = span.Slice(6).IndexOf('"');
                    if (startQuote != -1)
                    {
                        startQuote += 6;
                        int endQuote = span.Slice(startQuote + 1).IndexOf('"');
                        if (endQuote != -1)
                        {
                            endQuote += startQuote + 1;
                            saleIds.Add(span.Slice(startQuote + 1, endQuote - startQuote - 1).ToString());
                        }
                    }
                }
                else if (depth == 1 && span.StartsWith("[\""))
                {
                    int endQuote = span.Slice(2).IndexOf('"');
                    if (endQuote != -1)
                    {
                        endQuote += 2;
                        var idPart = span.Slice(2, endQuote - 2);
                        if (idPart.Length > 0 && char.IsDigit(idPart[0]))
                        {
                            bool allDigits = true;
                            for (int d = 0; d < idPart.Length; d++)
                            {
                                if (!char.IsAsciiDigit(idPart[d])) { allDigits = false; break; }
                            }
                            if (allDigits)
                            {
                                saleIds.Add(idPart.ToString());
                            }
                        }
                    }
                }

                CountBraces(span, ref depth);
                if (depth <= 0)
                {
                    inSales = false;
                    depth = 0;
                }

                if (saleIds.Count > maxIds)
                    throw new InvalidDataException($"FissalRelay file contains more than {maxIds:N0} sale IDs.");
            }

            return new List<string>(saleIds);
        }

        private static void CountBraces(ReadOnlySpan<char> span, ref int depth)
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
                else if (c == '}')
                {
                    depth--;
                    if (depth <= 0) return;
                }
            }
        }
    }
}
