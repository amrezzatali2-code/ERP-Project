using System;
using System.Globalization;
using System.Linq;
using ERP.Models;

namespace ERP.Infrastructure
{
    /// <summary>
    /// فلاتر أعمدة سطور التحويل: قائمة قيم | للنصوص؛ بحث رقمي filterCol_*Expr للأعمدة الرقمية فقط (ليس من/إلى مخزن).
    /// </summary>
    public static class StockTransferLineColumnFilter
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

        private static IQueryable<StockTransferLine> ApplyIntExpr(IQueryable<StockTransferLine> q, string? raw, string column)
        {
            raw = NormalizeNumericExpr(raw);
            if (string.IsNullOrWhiteSpace(raw)) return q;
            var expr = raw.Trim();
            var c = column.ToLowerInvariant();

            if (c == "id")
            {
                if (expr.StartsWith("<=", StringComparison.Ordinal) && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                    return q.Where(l => l.Id <= smax);
                if (expr.StartsWith(">=", StringComparison.Ordinal) && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                    return q.Where(l => l.Id >= smin);
                if (expr.StartsWith("<", StringComparison.Ordinal) && !expr.StartsWith("<=", StringComparison.Ordinal) && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                    return q.Where(l => l.Id < smax2);
                if (expr.StartsWith(">", StringComparison.Ordinal) && !expr.StartsWith(">=", StringComparison.Ordinal) && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                    return q.Where(l => l.Id > smin2);
                if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
                {
                    var separator = expr.Contains(':') ? ':' : '-';
                    var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2
                        && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                        && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                    {
                        if (a > b) (a, b) = (b, a);
                        return q.Where(l => l.Id >= a && l.Id <= b);
                    }
                }
                if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                    return q.Where(l => l.Id == ex);
                return q;
            }

            if (c == "transfer")
            {
                if (expr.StartsWith("<=", StringComparison.Ordinal) && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                    return q.Where(l => l.StockTransferId <= smax);
                if (expr.StartsWith(">=", StringComparison.Ordinal) && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                    return q.Where(l => l.StockTransferId >= smin);
                if (expr.StartsWith("<", StringComparison.Ordinal) && !expr.StartsWith("<=", StringComparison.Ordinal) && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                    return q.Where(l => l.StockTransferId < smax2);
                if (expr.StartsWith(">", StringComparison.Ordinal) && !expr.StartsWith(">=", StringComparison.Ordinal) && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                    return q.Where(l => l.StockTransferId > smin2);
                if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
                {
                    var separator = expr.Contains(':') ? ':' : '-';
                    var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2
                        && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                        && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                    {
                        if (a > b) (a, b) = (b, a);
                        return q.Where(l => l.StockTransferId >= a && l.StockTransferId <= b);
                    }
                }
                if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                    return q.Where(l => l.StockTransferId == ex);
                return q;
            }

            if (c == "product")
            {
                if (expr.StartsWith("<=", StringComparison.Ordinal) && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                    return q.Where(l => l.ProductId <= smax);
                if (expr.StartsWith(">=", StringComparison.Ordinal) && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                    return q.Where(l => l.ProductId >= smin);
                if (expr.StartsWith("<", StringComparison.Ordinal) && !expr.StartsWith("<=", StringComparison.Ordinal) && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                    return q.Where(l => l.ProductId < smax2);
                if (expr.StartsWith(">", StringComparison.Ordinal) && !expr.StartsWith(">=", StringComparison.Ordinal) && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                    return q.Where(l => l.ProductId > smin2);
                if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
                {
                    var separator = expr.Contains(':') ? ':' : '-';
                    var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2
                        && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                        && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                    {
                        if (a > b) (a, b) = (b, a);
                        return q.Where(l => l.ProductId >= a && l.ProductId <= b);
                    }
                }
                if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                    return q.Where(l => l.ProductId == ex);
                return q;
            }

            if (c == "qty")
            {
                if (expr.StartsWith("<=", StringComparison.Ordinal) && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                    return q.Where(l => l.Qty <= smax);
                if (expr.StartsWith(">=", StringComparison.Ordinal) && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                    return q.Where(l => l.Qty >= smin);
                if (expr.StartsWith("<", StringComparison.Ordinal) && !expr.StartsWith("<=", StringComparison.Ordinal) && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                    return q.Where(l => l.Qty < smax2);
                if (expr.StartsWith(">", StringComparison.Ordinal) && !expr.StartsWith(">=", StringComparison.Ordinal) && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                    return q.Where(l => l.Qty > smin2);
                if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
                {
                    var separator = expr.Contains(':') ? ':' : '-';
                    var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2
                        && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                        && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                    {
                        if (a > b) (a, b) = (b, a);
                        return q.Where(l => l.Qty >= a && l.Qty <= b);
                    }
                }
                if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                    return q.Where(l => l.Qty == ex);
                return q;
            }

            return q;
        }

        public static IQueryable<StockTransferLine> ApplyColumnFilters(
            IQueryable<StockTransferLine> qBase,
            string? filterCol_id,
            string? filterCol_idExpr,
            string? filterCol_transfer,
            string? filterCol_transferExpr,
            string? filterCol_date,
            string? filterCol_fromwh,
            string? filterCol_fromwhExpr,
            string? filterCol_towh,
            string? filterCol_towhExpr,
            string? filterCol_product,
            string? filterCol_productExpr,
            string? filterCol_qty,
            string? filterCol_qtyExpr,
            string? filterCol_note)
        {
            if (!string.IsNullOrWhiteSpace(filterCol_idExpr))
                qBase = ApplyIntExpr(qBase, filterCol_idExpr, "id");
            else if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(l => ids.Contains(l.Id));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_transferExpr))
                qBase = ApplyIntExpr(qBase, filterCol_transferExpr, "transfer");
            else if (!string.IsNullOrWhiteSpace(filterCol_transfer))
            {
                var ids = filterCol_transfer.Split(_sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(l => ids.Contains(l.StockTransferId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_productExpr))
                qBase = ApplyIntExpr(qBase, filterCol_productExpr, "product");
            else if (!string.IsNullOrWhiteSpace(filterCol_product))
            {
                var ids = filterCol_product.Split(_sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(l => ids.Contains(l.ProductId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_qtyExpr))
                qBase = ApplyIntExpr(qBase, filterCol_qtyExpr, "qty");
            else if (!string.IsNullOrWhiteSpace(filterCol_qty))
            {
                var ids = filterCol_qty.Split(_sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(l => ids.Contains(l.Qty));
            }

            // من مخزن / إلى مخزن: فلتر نصي (قائمة قيم |) وليس Expr رقمي
            if (!string.IsNullOrWhiteSpace(filterCol_fromwh))
            {
                var ids = filterCol_fromwh.Split(_sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(l => l.StockTransfer != null && ids.Contains(l.StockTransfer.FromWarehouseId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_towh))
            {
                var ids = filterCol_towh.Split(_sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(l => l.StockTransfer != null && ids.Contains(l.StockTransfer.ToWarehouseId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_note))
            {
                var vals = filterCol_note.Split(_sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    qBase = qBase.Where(l => l.Note != null && vals.Contains(l.Note));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_date))
            {
                var dates = filterCol_date.Split(_sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => DateTime.TryParse(x.Trim(), out var d) ? d.Date : (DateTime?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (dates.Count > 0)
                    qBase = qBase.Where(l => l.StockTransfer != null && dates.Contains(l.StockTransfer.TransferDate.Date));
            }

            return qBase;
        }
    }
}
