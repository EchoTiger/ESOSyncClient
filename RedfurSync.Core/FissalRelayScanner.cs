using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

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
        private static readonly Regex SaleIdPattern = new(
            @"^\s*\[""id""\]\s*=\s*""(?<id>\d{1,20})"",?\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex KeySaleIdPattern = new(
            @"^\s*\[""(?<id>\d{1,20})""\]\s*=\s*\{",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
                var match = SaleIdPattern.Match(line);
                if (match.Success)
                {
                    saleIds.Add(match.Groups["id"].Value);
                }
                else
                {
                    var matchKey = KeySaleIdPattern.Match(line);
                    if (matchKey.Success)
                    {
                        saleIds.Add(matchKey.Groups["id"].Value);
                    }
                }

                if (saleIds.Count > maxIds)
                    throw new InvalidDataException($"FissalRelay file contains more than {maxIds:N0} sale IDs.");
            }

            return new List<string>(saleIds);
        }
    }
}
