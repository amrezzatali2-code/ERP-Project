using System;
using System.Globalization;
using System.Text;

namespace ERP.Services
{
    /// <summary>
    /// Parser تمهيدي لكود GS1 DataMatrix يعتمد على أشهر AIs:
    /// 01 = GTIN, 17 = Expiry, 10 = Batch, 21 = Serial.
    /// </summary>
    public class Gs1DataMatrixParser : IGs1DataMatrixParser
    {
        private const char GroupSeparator = (char)29;

        public Gs1ScanData Parse(string rawScan)
        {
            var result = new Gs1ScanData { Raw = rawScan ?? string.Empty };
            var cleaned = Normalize(rawScan);
            result.Cleaned = cleaned;

            var i = 0;
            while (i < cleaned.Length)
            {
                if (TryReadFixed(cleaned, ref i, "01", 14, out var gtin))
                {
                    result.Gtin = gtin;
                    continue;
                }

                if (TryReadFixed(cleaned, ref i, "17", 6, out var expiry))
                {
                    result.Expiry = ParseExpiry(expiry);
                    continue;
                }

                if (TryReadVariable(cleaned, ref i, "10", out var batchNo))
                {
                    result.BatchNo = batchNo;
                    continue;
                }

                if (TryReadVariable(cleaned, ref i, "21", out var serialNo))
                {
                    result.SerialNo = serialNo;
                    continue;
                }

                i++;
            }

            return result;
        }

        private static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Replace("(", string.Empty).Replace(")", string.Empty).Trim();
            var sb = new StringBuilder(normalized.Length);
            foreach (var ch in normalized)
            {
                if (ch != '\r' && ch != '\n')
                {
                    sb.Append(ch);
                }
            }

            return sb.ToString();
        }

        private static bool TryReadFixed(string input, ref int index, string ai, int dataLength, out string data)
        {
            data = string.Empty;
            if (!StartsWithAi(input, index, ai))
            {
                return false;
            }

            var start = index + ai.Length;
            if (start + dataLength > input.Length)
            {
                return false;
            }

            data = input.Substring(start, dataLength);
            index = start + dataLength;
            return true;
        }

        private static bool TryReadVariable(string input, ref int index, string ai, out string data)
        {
            data = string.Empty;
            if (!StartsWithAi(input, index, ai))
            {
                return false;
            }

            var start = index + ai.Length;
            var end = input.IndexOf(GroupSeparator, start);
            if (end < 0)
            {
                end = input.Length;
            }

            data = input.Substring(start, end - start);
            index = end == input.Length ? end : end + 1;
            return true;
        }

        private static bool StartsWithAi(string input, int index, string ai)
        {
            return index + ai.Length <= input.Length
                   && string.CompareOrdinal(input, index, ai, 0, ai.Length) == 0;
        }

        private static DateTime? ParseExpiry(string value)
        {
            if (value.Length != 6)
            {
                return null;
            }

            return DateTime.TryParseExact(value, "yyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry)
                ? expiry
                : null;
        }
    }
}
