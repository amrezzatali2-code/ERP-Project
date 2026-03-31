using System;
using System.Globalization;
using System.Linq;

namespace ERP.Infrastructure
{
    /// <summary>
    /// فلترة أعمدة رقمية في تقرير أرصدة العملاء: قيمة واحدة أو عدة قيم بـ | أو تعبيرات &gt; &lt; &gt;= &lt;= نطاق (نفس نمط قوائم ERP).
    /// </summary>
    public static class CustomerBalancesNumericFilter
    {
        private static string NormalizeExpr(string raw)
        {
            var s = raw.Trim();
            // أشكال شائعة من RTL: "2000000<" يعني أقل من 2000000
            if (s.Length > 1 && (s[^1] == '<' || s[^1] == '>'))
            {
                var prefix = s[^1];
                var numPart = s[..^1].Trim();
                if (numPart.Length > 0 && char.IsDigit(numPart[0]))
                {
                    if (prefix == '<') return "<" + numPart;
                    if (prefix == '>') return ">" + numPart;
                }
            }
            return s;
        }

        private static string StripThousands(string s)
        {
            return s.Replace(",", "").Replace("،", "").Trim();
        }

        private static bool TryParseDec(string s, out decimal d)
        {
            d = 0;
            s = StripThousands(s);
            return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out d);
        }

        /// <summary>
        /// يتحقق من تطابق قيمة عمود رقمي مع النص القادم من لوحة الفلتر (قائمة | أو تعبير).
        /// </summary>
        public static bool MatchesDecimal(decimal value, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return true;
            var expr = NormalizeExpr(raw);

            // عدة قيم مطابقة تامة (مفصولة بـ | كما في لوحة الفلتر)
            if (expr.Contains('|'))
            {
                var parts = expr.Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (parts.Count == 0) return true;
                var decimals = parts.Select(p => TryParseDec(p, out var d) ? (decimal?)d : null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                return decimals.Count > 0 && decimals.Contains(value);
            }

            // تعبير واحد: مقارنة أو مطابقة تامة
            if (expr.StartsWith("<=", StringComparison.Ordinal) && expr.Length > 2 && TryParseDec(expr.Substring(2), out var le))
                return value <= le;
            if (expr.StartsWith(">=", StringComparison.Ordinal) && expr.Length > 2 && TryParseDec(expr.Substring(2), out var ge))
                return value >= ge;
            if (expr.StartsWith("<") && !expr.StartsWith("<=", StringComparison.Ordinal) && expr.Length > 1 && TryParseDec(expr.Substring(1), out var lt))
                return value < lt;
            if (expr.StartsWith(">") && !expr.StartsWith(">=", StringComparison.Ordinal) && expr.Length > 1 && TryParseDec(expr.Substring(1), out var gt))
                return value > gt;

            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0 && char.IsDigit(expr[0]))) &&
                !expr.StartsWith("-"))
            {
                var separator = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && TryParseDec(parts[0].Trim(), out var a)
                    && TryParseDec(parts[1].Trim(), out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    return value >= a && value <= b;
                }
            }

            if (TryParseDec(expr, out var exact))
                return value == exact;

            return false;
        }
    }
}
