using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace RedfurSync
{
    internal static class MasterMerchantSaleScanner
    {
        private static readonly Regex SaleIdPattern = new(
            "^\\s*\\[\"id\"\\]\\s*=\\s*\"(?<id>\\d{1,20})\",?\\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static IReadOnlyList<string> ReadSaleIds(string filePath, int maxIds = 200_000)
        {
            var saleIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(filePath))
            {
                var match = SaleIdPattern.Match(line);
                if (!match.Success) continue;
                saleIds.Add(match.Groups["id"].Value);
                if (saleIds.Count > maxIds)
                    throw new InvalidDataException($"Master Merchant file contains more than {maxIds:N0} sale IDs.");
            }
            return new List<string>(saleIds);
        }

        public static bool IsSalesFile(string fileName)
        {
            if (!fileName.StartsWith("GS", StringComparison.OrdinalIgnoreCase)
                || !fileName.EndsWith("Data.lua", StringComparison.OrdinalIgnoreCase)
                || fileName.Length != 12)
                return false;
            return int.TryParse(fileName.AsSpan(2, 2), out var index) && index >= 0 && index <= 15;
        }
    }
}