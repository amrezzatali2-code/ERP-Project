using System.Globalization;
using System.Linq;
using ERP.Models;

namespace ERP.Infrastructure
{
    /// <summary>
    /// فلتر أعمدة رقمية لدفتر الأستاذ (filterCol_*Expr).
    /// </summary>
    public static class LedgerEntryListNumericExpr
    {
        public static IQueryable<LedgerEntry> ApplyIdExpr(IQueryable<LedgerEntry> q, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return q;
            var expr = raw.Trim();
            if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                return q.Where(e => e.Id <= smax);
            if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                return q.Where(e => e.Id >= smin);
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                return q.Where(e => e.Id < smax2);
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                return q.Where(e => e.Id > smin2);
            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', System.StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var separator = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(separator, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    return q.Where(e => e.Id >= a && e.Id <= b);
                }
            }
            if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                return q.Where(e => e.Id == ex);
            return q;
        }

        public static IQueryable<LedgerEntry> ApplyPostVersionExpr(IQueryable<LedgerEntry> q, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return q;
            var expr = raw.Trim();
            if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                return q.Where(e => e.PostVersion <= smax);
            if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                return q.Where(e => e.PostVersion >= smin);
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                return q.Where(e => e.PostVersion < smax2);
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                return q.Where(e => e.PostVersion > smin2);
            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', System.StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var separator = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(separator, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    return q.Where(e => e.PostVersion >= a && e.PostVersion <= b);
                }
            }
            if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                return q.Where(e => e.PostVersion == ex);
            return q;
        }

        public static IQueryable<LedgerEntry> ApplySourceIdExpr(IQueryable<LedgerEntry> q, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return q;
            var expr = raw.Trim();
            if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                return q.Where(e => e.SourceId.HasValue && e.SourceId.Value <= smax);
            if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                return q.Where(e => e.SourceId.HasValue && e.SourceId.Value >= smin);
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                return q.Where(e => e.SourceId.HasValue && e.SourceId.Value < smax2);
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                return q.Where(e => e.SourceId.HasValue && e.SourceId.Value > smin2);
            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', System.StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var separator = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(separator, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    return q.Where(e => e.SourceId.HasValue && e.SourceId.Value >= a && e.SourceId.Value <= b);
                }
            }
            if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                return q.Where(e => e.SourceId.HasValue && e.SourceId.Value == ex);
            return q;
        }

        public static IQueryable<LedgerEntry> ApplyAccountIdExpr(IQueryable<LedgerEntry> q, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return q;
            var expr = raw.Trim();
            if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                return q.Where(e => e.AccountId <= smax);
            if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                return q.Where(e => e.AccountId >= smin);
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                return q.Where(e => e.AccountId < smax2);
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                return q.Where(e => e.AccountId > smin2);
            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', System.StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var separator = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(separator, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    return q.Where(e => e.AccountId >= a && e.AccountId <= b);
                }
            }
            if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                return q.Where(e => e.AccountId == ex);
            return q;
        }

        public static IQueryable<LedgerEntry> ApplyDebitExpr(IQueryable<LedgerEntry> q, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return q;
            var expr = raw.Trim();
            if (expr.StartsWith("<=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                return q.Where(e => e.Debit <= smax);
            if (expr.StartsWith(">=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                return q.Where(e => e.Debit >= smin);
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                return q.Where(e => e.Debit < smax2);
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                return q.Where(e => e.Debit > smin2);
            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', System.StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var separator = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(separator, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && decimal.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && decimal.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    return q.Where(e => e.Debit >= a && e.Debit <= b);
                }
            }
            if (decimal.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                return q.Where(e => e.Debit == ex);
            return q;
        }

        public static IQueryable<LedgerEntry> ApplyCreditExpr(IQueryable<LedgerEntry> q, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return q;
            var expr = raw.Trim();
            if (expr.StartsWith("<=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                return q.Where(e => e.Credit <= smax);
            if (expr.StartsWith(">=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                return q.Where(e => e.Credit >= smin);
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                return q.Where(e => e.Credit < smax2);
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                return q.Where(e => e.Credit > smin2);
            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', System.StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var separator = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(separator, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && decimal.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && decimal.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    return q.Where(e => e.Credit >= a && e.Credit <= b);
                }
            }
            if (decimal.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                return q.Where(e => e.Credit == ex);
            return q;
        }
    }
}
