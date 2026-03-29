using System;
using System.Globalization;
using System.Linq;
using ERP.Models;

namespace ERP.Infrastructure
{
    /// <summary>
    /// فلاتر أعمدة رؤوس التحويل: قائمة قيم | أو بحث رقمي filterCol_*Expr.
    /// </summary>
    public static class StockTransferColumnFilter
    {
        private static readonly char[] _sep = new[] { '|', ',', ';' };

        private static string? NormalizeNumericExpr(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var chars = raw.Trim().ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (chars[i] >= '٠' && chars[i] <= '٩')
                    chars[i] = (char)('0' + (chars[i] - '٠'));
            }
            var s = new string(chars);
            if (s.Count(c => c == ',') == 1 && s.IndexOf('.') < 0)
                s = s.Replace(',', '.');
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        private static IQueryable<StockTransfer> ApplyIntExpr(IQueryable<StockTransfer> q, string? raw, string column)
        {
            raw = NormalizeNumericExpr(raw);
            if (string.IsNullOrWhiteSpace(raw)) return q;
            var expr = raw.Trim();
            var c = column.ToLowerInvariant();

            if (c == "id")
            {
                if (expr.StartsWith("<=", StringComparison.Ordinal) && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                    return q.Where(t => t.Id <= smax);
                if (expr.StartsWith(">=", StringComparison.Ordinal) && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                    return q.Where(t => t.Id >= smin);
                if (expr.StartsWith("<", StringComparison.Ordinal) && !expr.StartsWith("<=", StringComparison.Ordinal) && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                    return q.Where(t => t.Id < smax2);
                if (expr.StartsWith(">", StringComparison.Ordinal) && !expr.StartsWith(">=", StringComparison.Ordinal) && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                    return q.Where(t => t.Id > smin2);
                if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
                {
                    var separator = expr.Contains(':') ? ':' : '-';
                    var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2
                        && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                        && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                    {
                        if (a > b) (a, b) = (b, a);
                        return q.Where(t => t.Id >= a && t.Id <= b);
                    }
                }
                if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                    return q.Where(t => t.Id == ex);
                return q;
            }

            if (c == "fromwarehouse")
            {
                if (expr.StartsWith("<=", StringComparison.Ordinal) && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                    return q.Where(t => t.FromWarehouseId <= smax);
                if (expr.StartsWith(">=", StringComparison.Ordinal) && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                    return q.Where(t => t.FromWarehouseId >= smin);
                if (expr.StartsWith("<", StringComparison.Ordinal) && !expr.StartsWith("<=", StringComparison.Ordinal) && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                    return q.Where(t => t.FromWarehouseId < smax2);
                if (expr.StartsWith(">", StringComparison.Ordinal) && !expr.StartsWith(">=", StringComparison.Ordinal) && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                    return q.Where(t => t.FromWarehouseId > smin2);
                if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
                {
                    var separator = expr.Contains(':') ? ':' : '-';
                    var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2
                        && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                        && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                    {
                        if (a > b) (a, b) = (b, a);
                        return q.Where(t => t.FromWarehouseId >= a && t.FromWarehouseId <= b);
                    }
                }
                if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                    return q.Where(t => t.FromWarehouseId == ex);
                return q;
            }

            if (c == "towarehouse")
            {
                if (expr.StartsWith("<=", StringComparison.Ordinal) && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                    return q.Where(t => t.ToWarehouseId <= smax);
                if (expr.StartsWith(">=", StringComparison.Ordinal) && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                    return q.Where(t => t.ToWarehouseId >= smin);
                if (expr.StartsWith("<", StringComparison.Ordinal) && !expr.StartsWith("<=", StringComparison.Ordinal) && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                    return q.Where(t => t.ToWarehouseId < smax2);
                if (expr.StartsWith(">", StringComparison.Ordinal) && !expr.StartsWith(">=", StringComparison.Ordinal) && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                    return q.Where(t => t.ToWarehouseId > smin2);
                if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
                {
                    var separator = expr.Contains(':') ? ':' : '-';
                    var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2
                        && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                        && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                    {
                        if (a > b) (a, b) = (b, a);
                        return q.Where(t => t.ToWarehouseId >= a && t.ToWarehouseId <= b);
                    }
                }
                if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                    return q.Where(t => t.ToWarehouseId == ex);
                return q;
            }

            return q;
        }

        public static IQueryable<StockTransfer> ApplyColumnFilters(
            IQueryable<StockTransfer> qBase,
            string? filterCol_id,
            string? filterCol_idExpr,
            string? filterCol_fromwarehouse,
            string? filterCol_fromwarehouseExpr,
            string? filterCol_towarehouse,
            string? filterCol_towarehouseExpr,
            string? filterCol_note,
            string? filterCol_date,
            string? filterCol_created,
            string? filterCol_updated)
        {
            if (!string.IsNullOrWhiteSpace(filterCol_idExpr))
                qBase = ApplyIntExpr(qBase, filterCol_idExpr, "id");
            else if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(t => ids.Contains(t.Id));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_fromwarehouseExpr))
                qBase = ApplyIntExpr(qBase, filterCol_fromwarehouseExpr, "fromwarehouse");
            else if (!string.IsNullOrWhiteSpace(filterCol_fromwarehouse))
            {
                var ids = filterCol_fromwarehouse.Split(_sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(t => ids.Contains(t.FromWarehouseId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_towarehouseExpr))
                qBase = ApplyIntExpr(qBase, filterCol_towarehouseExpr, "towarehouse");
            else if (!string.IsNullOrWhiteSpace(filterCol_towarehouse))
            {
                var ids = filterCol_towarehouse.Split(_sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(t => ids.Contains(t.ToWarehouseId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_note))
            {
                var vals = filterCol_note.Split(_sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    qBase = qBase.Where(t => t.Note != null && vals.Contains(t.Note));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_date))
            {
                var dates = filterCol_date.Split(_sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => DateTime.TryParse(x.Trim(), out var d) ? d.Date : (DateTime?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (dates.Count > 0)
                    qBase = qBase.Where(t => dates.Contains(t.TransferDate.Date));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_created))
            {
                var dates = filterCol_created.Split(_sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => DateTime.TryParse(x.Trim(), out var d) ? d.Date : (DateTime?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (dates.Count > 0)
                    qBase = qBase.Where(t => dates.Contains(t.CreatedAt.Date));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_updated))
            {
                var dates = filterCol_updated.Split(_sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => DateTime.TryParse(x.Trim(), out var d) ? d.Date : (DateTime?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (dates.Count > 0)
                    qBase = qBase.Where(t => t.UpdatedAt.HasValue && dates.Contains(t.UpdatedAt.Value.Date));
            }

            return qBase;
        }
    }
}
