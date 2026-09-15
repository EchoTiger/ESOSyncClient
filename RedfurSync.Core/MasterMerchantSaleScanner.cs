using System;
using System.Collections.Generic;
using System.IO;

namespace RedfurSync
{
    internal static class MasterMerchantSaleScanner
    {
        // Span-based parse: avoids 200 k+ Regex Match allocations on large sales files.
        // Lines matched:   ["id"] = "123456789",
        public static IReadOnlyList<string> ReadSaleIds(string filePath, int maxIds = 200_000)
        {
            var saleIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(filePath))
            {
                var span = line.AsSpan().Trim();

                // Must start with ["id"]
                if (!span.StartsWith("[\"id\"]", StringComparison.Ordinal)) continue;
                span = span.Slice(6).TrimStart();   // skip ["id"] + whitespace

                // Expect '='
                if (span.IsEmpty || span[0] != '=') continue;
                span = span.Slice(1).TrimStart();

                // Expect opening quote
                if (span.IsEmpty || span[0] != '\"') continue;
                span = span.Slice(1);

                // Find closing quote — value must be all digits, 1-20 chars
                int closeQuote = span.IndexOf('\"');
                if (closeQuote <= 0) continue;

                var idSpan = span.Slice(0, closeQuote);
                if (idSpan.Length > 20) continue;

                bool allDigits = true;
                for (int i = 0; i < idSpan.Length; i++)
                    if (!char.IsAsciiDigit(idSpan[i])) { allDigits = false; break; }
                if (!allDigits) continue;

                saleIds.Add(idSpan.ToString());
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
