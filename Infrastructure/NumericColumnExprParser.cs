using System.Globalization;

namespace ERP.Infrastructure
{
    public enum NumericFilterKind
    {
        None,
        Exact,
        GreaterOrEqual,
        LessOrEqual,
        Greater,
        Less,
        Between
    }

    public readonly struct NumericFilterResult
    {
        public NumericFilterKind Kind { get; init; }
        public decimal Min { get; init; }
        public decimal Max { get; init; }
    }

    /// <summary>
    /// تحليل صيغة البحث الرقمي الموحّدة (مثل قائمة الأصناف): رقم، &gt;n، &lt;n، &gt;=، &lt;=، نطاق min:max.
    /// </summary>
    public static class NumericColumnExprParser
    {
        public static bool TryParseDecimal(string? raw, out NumericFilterResult r)
        {
            r = default;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var expr = raw.Trim();
            if (expr.StartsWith("<=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
            {
                r = new NumericFilterResult { Kind = NumericFilterKind.LessOrEqual, Min = smax, Max = smax };
                return true;
            }
            if (expr.StartsWith(">=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
            {
                r = new NumericFilterResult { Kind = NumericFilterKind.GreaterOrEqual, Min = smin, Max = smin };
                return true;
            }
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
            {
                r = new NumericFilterResult { Kind = NumericFilterKind.Less, Min = smax2, Max = smax2 };
                return true;
            }
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
            {
                r = new NumericFilterResult { Kind = NumericFilterKind.Greater, Min = smin2, Max = smin2 };
                return true;
            }
            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var separator = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && decimal.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && decimal.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    r = new NumericFilterResult { Kind = NumericFilterKind.Between, Min = a, Max = b };
                    return true;
                }
            }
            if (decimal.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
            {
                r = new NumericFilterResult { Kind = NumericFilterKind.Exact, Min = ex, Max = ex };
                return true;
            }
            return false;
        }

        public static bool TryParseInt(string? raw, out NumericFilterResult r)
        {
            r = default;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var expr = raw.Trim();
            if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
            {
                r = new NumericFilterResult { Kind = NumericFilterKind.LessOrEqual, Min = smax, Max = smax };
                return true;
            }
            if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
            {
                r = new NumericFilterResult { Kind = NumericFilterKind.GreaterOrEqual, Min = smin, Max = smin };
                return true;
            }
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
            {
                r = new NumericFilterResult { Kind = NumericFilterKind.Less, Min = smax2, Max = smax2 };
                return true;
            }
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
            {
                r = new NumericFilterResult { Kind = NumericFilterKind.Greater, Min = smin2, Max = smin2 };
                return true;
            }
            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var separator = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    r = new NumericFilterResult { Kind = NumericFilterKind.Between, Min = a, Max = b };
                    return true;
                }
            }
            if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
            {
                r = new NumericFilterResult { Kind = NumericFilterKind.Exact, Min = ex, Max = ex };
                return true;
            }
            return false;
        }
    }
}
