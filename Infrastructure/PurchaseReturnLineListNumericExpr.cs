using System.Globalization;
using System.Linq;
using ERP.Models;

namespace ERP.Infrastructure
{
    /// <summary>
    /// فلتر أعمدة رقمية لقائمة سطور مرتجع الشراء (filterCol_*Expr).
    /// </summary>
    public static class PurchaseReturnLineListNumericExpr
    {
        public static IQueryable<PurchaseReturnLine> ApplyPretIdExpr(IQueryable<PurchaseReturnLine> q, string? raw)
            => ApplyIntOnPRetId(q, raw);

        public static IQueryable<PurchaseReturnLine> ApplyLineNoExpr(IQueryable<PurchaseReturnLine> q, string? raw)
            => ApplyIntOnLineNo(q, raw);

        public static IQueryable<PurchaseReturnLine> ApplyProdIdExpr(IQueryable<PurchaseReturnLine> q, string? raw)
            => ApplyIntOnProdId(q, raw);

        public static IQueryable<PurchaseReturnLine> ApplyQtyExpr(IQueryable<PurchaseReturnLine> q, string? raw)
            => ApplyIntOnQty(q, raw);

        public static IQueryable<PurchaseReturnLine> ApplyPriceRetailExpr(IQueryable<PurchaseReturnLine> q, string? raw)
            => ApplyDecimalPriceRetail(q, raw);

        public static IQueryable<PurchaseReturnLine> ApplyUnitCostExpr(IQueryable<PurchaseReturnLine> q, string? raw)
            => ApplyDecimalUnitCost(q, raw);

        public static IQueryable<PurchaseReturnLine> ApplyDiscountExpr(IQueryable<PurchaseReturnLine> q, string? raw)
            => ApplyDecimalDiscount(q, raw);

        public static IQueryable<PurchaseReturnLine> ApplyLineTotalExpr(IQueryable<PurchaseReturnLine> q, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return q;
            var expr = raw.Trim();
            if (expr.StartsWith("<=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                return q.Where(l => l.Qty * l.UnitCost <= smax);
            if (expr.StartsWith(">=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                return q.Where(l => l.Qty * l.UnitCost >= smin);
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                return q.Where(l => l.Qty * l.UnitCost < smax2);
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                return q.Where(l => l.Qty * l.UnitCost > smin2);
            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var separator = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && decimal.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && decimal.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    return q.Where(l => l.Qty * l.UnitCost >= a && l.Qty * l.UnitCost <= b);
                }
            }
            if (decimal.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                return q.Where(l => l.Qty * l.UnitCost == ex);
            return q;
        }

        private static IQueryable<PurchaseReturnLine> ApplyIntOnPRetId(IQueryable<PurchaseReturnLine> q, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return q;
            var expr = raw.Trim();
            if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                return q.Where(l => l.PRetId <= smax);
            if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                return q.Where(l => l.PRetId >= smin);
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                return q.Where(l => l.PRetId < smax2);
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                return q.Where(l => l.PRetId > smin2);
            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var separator = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    return q.Where(l => l.PRetId >= a && l.PRetId <= b);
                }
            }
            if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                return q.Where(l => l.PRetId == ex);
            return q;
        }

        private static IQueryable<PurchaseReturnLine> ApplyIntOnLineNo(IQueryable<PurchaseReturnLine> q, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return q;
            var expr = raw.Trim();
            if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                return q.Where(l => l.LineNo <= smax);
            if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                return q.Where(l => l.LineNo >= smin);
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                return q.Where(l => l.LineNo < smax2);
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                return q.Where(l => l.LineNo > smin2);
            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var separator = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    return q.Where(l => l.LineNo >= a && l.LineNo <= b);
                }
            }
            if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                return q.Where(l => l.LineNo == ex);
            return q;
        }

        private static IQueryable<PurchaseReturnLine> ApplyIntOnProdId(IQueryable<PurchaseReturnLine> q, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return q;
            var expr = raw.Trim();
            if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                return q.Where(l => l.ProdId <= smax);
            if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                return q.Where(l => l.ProdId >= smin);
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                return q.Where(l => l.ProdId < smax2);
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                return q.Where(l => l.ProdId > smin2);
            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var separator = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    return q.Where(l => l.ProdId >= a && l.ProdId <= b);
                }
            }
            if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                return q.Where(l => l.ProdId == ex);
            return q;
        }

        private static IQueryable<PurchaseReturnLine> ApplyIntOnQty(IQueryable<PurchaseReturnLine> q, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return q;
            var expr = raw.Trim();
            if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                return q.Where(l => l.Qty <= smax);
            if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                return q.Where(l => l.Qty >= smin);
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                return q.Where(l => l.Qty < smax2);
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
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

        private static IQueryable<PurchaseReturnLine> ApplyDecimalPriceRetail(IQueryable<PurchaseReturnLine> q, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return q;
            var expr = raw.Trim();
            if (expr.StartsWith("<=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                return q.Where(l => l.PriceRetail <= smax);
            if (expr.StartsWith(">=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                return q.Where(l => l.PriceRetail >= smin);
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                return q.Where(l => l.PriceRetail < smax2);
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                return q.Where(l => l.PriceRetail > smin2);
            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var separator = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && decimal.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && decimal.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    return q.Where(l => l.PriceRetail >= a && l.PriceRetail <= b);
                }
            }
            if (decimal.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                return q.Where(l => l.PriceRetail == ex);
            return q;
        }

        private static IQueryable<PurchaseReturnLine> ApplyDecimalUnitCost(IQueryable<PurchaseReturnLine> q, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return q;
            var expr = raw.Trim();
            if (expr.StartsWith("<=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                return q.Where(l => l.UnitCost <= smax);
            if (expr.StartsWith(">=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                return q.Where(l => l.UnitCost >= smin);
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                return q.Where(l => l.UnitCost < smax2);
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                return q.Where(l => l.UnitCost > smin2);
            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var separator = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && decimal.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && decimal.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    return q.Where(l => l.UnitCost >= a && l.UnitCost <= b);
                }
            }
            if (decimal.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                return q.Where(l => l.UnitCost == ex);
            return q;
        }

        private static IQueryable<PurchaseReturnLine> ApplyDecimalDiscount(IQueryable<PurchaseReturnLine> q, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return q;
            var expr = raw.Trim();
            if (expr.StartsWith("<=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                return q.Where(l => l.PurchaseDiscountPct <= smax);
            if (expr.StartsWith(">=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                return q.Where(l => l.PurchaseDiscountPct >= smin);
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                return q.Where(l => l.PurchaseDiscountPct < smax2);
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                return q.Where(l => l.PurchaseDiscountPct > smin2);
            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var separator = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && decimal.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && decimal.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    return q.Where(l => l.PurchaseDiscountPct >= a && l.PurchaseDiscountPct <= b);
                }
            }
            if (decimal.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                return q.Where(l => l.PurchaseDiscountPct == ex);
            return q;
        }
    }
}
