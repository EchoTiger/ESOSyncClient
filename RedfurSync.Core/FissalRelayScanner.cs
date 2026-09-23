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

            foreach (var line in File.ReadLines(filePath))
            {
                var span = line.AsSpan().TrimStart();

                if (span.StartsWith("[\"id\"]"))
                {
                    // Fix: ReadOnlySpan<char>.IndexOf(char, int) removed in .NET 8+.
                    // Use Slice(offset).IndexOf('"') and add offset back.
                    int startQuote = span.Slice(6).IndexOf('"');
                    if (startQuote != -1)
                    {
                        startQuote += 6; // absolute index
                        int endQuote = span.Slice(startQuote + 1).IndexOf('"');
                        if (endQuote != -1)
                        {
                            endQuote += startQuote + 1; // absolute index
                            saleIds.Add(span.Slice(startQuote + 1, endQuote - startQuote - 1).ToString());
                        }
                    }
                }
                else if (span.StartsWith("[\"") && span.EndsWith("{"))
                {
                    int endQuote = span.Slice(2).IndexOf('"');
                    if (endQuote != -1)
                    {
                        endQuote += 2; // absolute index
                        var idPart = span.Slice(2, endQuote - 2);
                        if (idPart.Length > 0 && char.IsDigit(idPart[0]))
                        {
                            saleIds.Add(idPart.ToString());
                        }
                    }
                }

                if (saleIds.Count > maxIds)
                    throw new InvalidDataException($"FissalRelay file contains more than {maxIds:N0} sale IDs.");
            }

            return new List<string>(saleIds);
        }
    }
}
