using System.Globalization;
using System.Linq;
using ERP.Models;

namespace ERP.Infrastructure
{
    /// <summary>
    /// فلتر أعمدة رقمية بنمط قائمة الأصناف (filterCol_*Expr): مطابقة، &lt;= &gt;= &lt; &gt;، نطاق : أو -.
    /// </summary>
    public static class SalesInvoiceListNumericExpr
    {
        public static IQueryable<SalesInvoice> ApplySiIdExpr(IQueryable<SalesInvoice> q, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return q;
            var expr = raw.Trim();
            if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                return q.Where(si => si.SIId <= smax);
            if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                return q.Where(si => si.SIId >= smin);
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                return q.Where(si => si.SIId < smax2);
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                return q.Where(si => si.SIId > smin2);
            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var separator = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    return q.Where(si => si.SIId >= a && si.SIId <= b);
                }
            }
            if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                return q.Where(si => si.SIId == ex);
            return q;
        }

        public static IQueryable<SalesInvoice> ApplyNetExpr(IQueryable<SalesInvoice> q, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return q;
            var expr = raw.Trim();
            if (expr.StartsWith("<=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                return q.Where(si => si.NetTotal <= smax);
            if (expr.StartsWith(">=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                return q.Where(si => si.NetTotal >= smin);
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                return q.Where(si => si.NetTotal < smax2);
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                return q.Where(si => si.NetTotal > smin2);
            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var separator = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && decimal.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && decimal.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    return q.Where(si => si.NetTotal >= a && si.NetTotal <= b);
                }
            }
            if (decimal.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                return q.Where(si => si.NetTotal == ex);
            return q;
        }
    }

    public static class SalesReturnListNumericExpr
    {
        public static IQueryable<SalesReturn> ApplySrIdExpr(IQueryable<SalesReturn> q, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return q;
            var expr = raw.Trim();
            if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                return q.Where(sr => sr.SRId <= smax);
            if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                return q.Where(sr => sr.SRId >= smin);
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                return q.Where(sr => sr.SRId < smax2);
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                return q.Where(sr => sr.SRId > smin2);
            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var separator = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    return q.Where(sr => sr.SRId >= a && sr.SRId <= b);
                }
            }
            if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                return q.Where(sr => sr.SRId == ex);
            return q;
        }

        public static IQueryable<SalesReturn> ApplyNetExpr(IQueryable<SalesReturn> q, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return q;
            var expr = raw.Trim();
            if (expr.StartsWith("<=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                return q.Where(sr => sr.NetTotal <= smax);
            if (expr.StartsWith(">=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                return q.Where(sr => sr.NetTotal >= smin);
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                return q.Where(sr => sr.NetTotal < smax2);
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                return q.Where(sr => sr.NetTotal > smin2);
            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var separator = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && decimal.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && decimal.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    return q.Where(sr => sr.NetTotal >= a && sr.NetTotal <= b);
                }
            }
            if (decimal.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                return q.Where(sr => sr.NetTotal == ex);
            return q;
        }
    }

    /// <summary>فلتر أعمدة رقمية لقائمة أوامر البيع (رقم الأمر، إجمالي القيمة المتوقعة).</summary>
    public static class SalesOrderListNumericExpr
    {
        public static IQueryable<SalesOrder> ApplySoIdExpr(IQueryable<SalesOrder> q, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return q;
            var expr = raw.Trim();
            if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                return q.Where(o => o.SOId <= smax);
            if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                return q.Where(o => o.SOId >= smin);
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                return q.Where(o => o.SOId < smax2);
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                return q.Where(o => o.SOId > smin2);
            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var separator = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    return q.Where(o => o.SOId >= a && o.SOId <= b);
                }
            }
            if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                return q.Where(o => o.SOId == ex);
            return q;
        }

        public static IQueryable<SalesOrder> ApplyExpectedTotalExpr(IQueryable<SalesOrder> q, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return q;
            var expr = raw.Trim();
            if (expr.StartsWith("<=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                return q.Where(o => o.ExpectedItemsTotal <= smax);
            if (expr.StartsWith(">=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                return q.Where(o => o.ExpectedItemsTotal >= smin);
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                return q.Where(o => o.ExpectedItemsTotal < smax2);
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                return q.Where(o => o.ExpectedItemsTotal > smin2);
            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var separator = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && decimal.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && decimal.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    return q.Where(o => o.ExpectedItemsTotal >= a && o.ExpectedItemsTotal <= b);
                }
            }
            if (decimal.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                return q.Where(o => o.ExpectedItemsTotal == ex);
            return q;
        }
    }

    /// <summary>تعبيرات رقمية لسطور فاتورة المبيعات — مفاتيح الأعمدة كما في data-col.</summary>
    public static class SalesInvoiceLineListNumericExpr
    {
        public static IQueryable<SalesInvoiceLine> ApplyForColumn(IQueryable<SalesInvoiceLine> q, string column, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return q;
            column = (column ?? "").Trim().ToLowerInvariant();
            return column switch
            {
                "siid" => ApplyInt(q, raw.Trim(), x => x.SIId),
                "lineno" => ApplyInt(q, raw.Trim(), x => x.LineNo),
                "prodid" => ApplyInt(q, raw.Trim(), x => x.ProdId),
                "qty" => ApplyInt(q, raw.Trim(), x => x.Qty),
                "price" => ApplyDecimal(q, raw.Trim(), x => x.PriceRetail),
                "disc1" => ApplyDecimal(q, raw.Trim(), x => x.Disc1Percent),
                "disc2" => ApplyDecimal(q, raw.Trim(), x => x.Disc2Percent),
                "disc3" => ApplyDecimal(q, raw.Trim(), x => x.Disc3Percent),
                "discval" => ApplyDecimal(q, raw.Trim(), x => x.DiscountValue),
                "total" => ApplyDecimal(q, raw.Trim(), x => x.LineTotalAfterDiscount),
                "net" => ApplyDecimal(q, raw.Trim(), x => x.LineNetTotal),
                _ => q
            };
        }

        private static IQueryable<SalesInvoiceLine> ApplyInt(IQueryable<SalesInvoiceLine> q, string expr, System.Linq.Expressions.Expression<Func<SalesInvoiceLine, int>> prop)
        {
            // EF Core يترجم استدعاءات Where مع نفس الشكل — نستخدم Switch على الأسماء لكل خاصية
            if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var le))
                return WhereInt(q, prop, le, IntCmp.Le);
            if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var ge))
                return WhereInt(q, prop, ge, IntCmp.Ge);
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var lt))
                return WhereInt(q, prop, lt, IntCmp.Lt);
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var gt))
                return WhereInt(q, prop, gt, IntCmp.Gt);
            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var sep = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    return WhereIntRange(q, prop, a, b);
                }
            }
            if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                return WhereIntEq(q, prop, ex);
            return q;
        }

        private enum IntCmp { Le, Ge, Lt, Gt }

        private static IQueryable<SalesInvoiceLine> WhereInt(IQueryable<SalesInvoiceLine> q, System.Linq.Expressions.Expression<Func<SalesInvoiceLine, int>> prop, int v, IntCmp cmp)
        {
            if (IsMember(prop, nameof(SalesInvoiceLine.SIId)))
                return cmp switch { IntCmp.Le => q.Where(x => x.SIId <= v), IntCmp.Ge => q.Where(x => x.SIId >= v), IntCmp.Lt => q.Where(x => x.SIId < v), _ => q.Where(x => x.SIId > v) };
            if (IsMember(prop, nameof(SalesInvoiceLine.LineNo)))
                return cmp switch { IntCmp.Le => q.Where(x => x.LineNo <= v), IntCmp.Ge => q.Where(x => x.LineNo >= v), IntCmp.Lt => q.Where(x => x.LineNo < v), _ => q.Where(x => x.LineNo > v) };
            if (IsMember(prop, nameof(SalesInvoiceLine.ProdId)))
                return cmp switch { IntCmp.Le => q.Where(x => x.ProdId <= v), IntCmp.Ge => q.Where(x => x.ProdId >= v), IntCmp.Lt => q.Where(x => x.ProdId < v), _ => q.Where(x => x.ProdId > v) };
            return cmp switch { IntCmp.Le => q.Where(x => x.Qty <= v), IntCmp.Ge => q.Where(x => x.Qty >= v), IntCmp.Lt => q.Where(x => x.Qty < v), _ => q.Where(x => x.Qty > v) };
        }

        private static IQueryable<SalesInvoiceLine> WhereIntRange(IQueryable<SalesInvoiceLine> q, System.Linq.Expressions.Expression<Func<SalesInvoiceLine, int>> prop, int a, int b)
        {
            if (IsMember(prop, nameof(SalesInvoiceLine.SIId)))
                return q.Where(x => x.SIId >= a && x.SIId <= b);
            if (IsMember(prop, nameof(SalesInvoiceLine.LineNo)))
                return q.Where(x => x.LineNo >= a && x.LineNo <= b);
            if (IsMember(prop, nameof(SalesInvoiceLine.ProdId)))
                return q.Where(x => x.ProdId >= a && x.ProdId <= b);
            return q.Where(x => x.Qty >= a && x.Qty <= b);
        }

        private static IQueryable<SalesInvoiceLine> WhereIntEq(IQueryable<SalesInvoiceLine> q, System.Linq.Expressions.Expression<Func<SalesInvoiceLine, int>> prop, int v)
        {
            if (IsMember(prop, nameof(SalesInvoiceLine.SIId)))
                return q.Where(x => x.SIId == v);
            if (IsMember(prop, nameof(SalesInvoiceLine.LineNo)))
                return q.Where(x => x.LineNo == v);
            if (IsMember(prop, nameof(SalesInvoiceLine.ProdId)))
                return q.Where(x => x.ProdId == v);
            return q.Where(x => x.Qty == v);
        }

        private static bool IsMember(System.Linq.Expressions.Expression<Func<SalesInvoiceLine, int>> prop, string name) =>
            prop.Body is System.Linq.Expressions.MemberExpression m && m.Member.Name == name;

        private static IQueryable<SalesInvoiceLine> ApplyDecimal(IQueryable<SalesInvoiceLine> q, string expr, System.Linq.Expressions.Expression<Func<SalesInvoiceLine, decimal>> prop)
        {
            if (expr.StartsWith("<=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                return WhereDec(q, prop, smax, DecCmp.Le);
            if (expr.StartsWith(">=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                return WhereDec(q, prop, smin, DecCmp.Ge);
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                return WhereDec(q, prop, smax2, DecCmp.Lt);
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                return WhereDec(q, prop, smin2, DecCmp.Gt);
            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var sep = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && decimal.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && decimal.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    return WhereDecRange(q, prop, a, b);
                }
            }
            if (decimal.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                return WhereDecEq(q, prop, ex);
            return q;
        }

        private enum DecCmp { Le, Ge, Lt, Gt }

        private static IQueryable<SalesInvoiceLine> WhereDec(IQueryable<SalesInvoiceLine> q, System.Linq.Expressions.Expression<Func<SalesInvoiceLine, decimal>> prop, decimal v, DecCmp cmp)
        {
            if (IsMemberDec(prop, nameof(SalesInvoiceLine.PriceRetail)))
                return cmp switch { DecCmp.Le => q.Where(x => x.PriceRetail <= v), DecCmp.Ge => q.Where(x => x.PriceRetail >= v), DecCmp.Lt => q.Where(x => x.PriceRetail < v), _ => q.Where(x => x.PriceRetail > v) };
            if (IsMemberDec(prop, nameof(SalesInvoiceLine.Disc1Percent)))
                return cmp switch { DecCmp.Le => q.Where(x => x.Disc1Percent <= v), DecCmp.Ge => q.Where(x => x.Disc1Percent >= v), DecCmp.Lt => q.Where(x => x.Disc1Percent < v), _ => q.Where(x => x.Disc1Percent > v) };
            if (IsMemberDec(prop, nameof(SalesInvoiceLine.Disc2Percent)))
                return cmp switch { DecCmp.Le => q.Where(x => x.Disc2Percent <= v), DecCmp.Ge => q.Where(x => x.Disc2Percent >= v), DecCmp.Lt => q.Where(x => x.Disc2Percent < v), _ => q.Where(x => x.Disc2Percent > v) };
            if (IsMemberDec(prop, nameof(SalesInvoiceLine.Disc3Percent)))
                return cmp switch { DecCmp.Le => q.Where(x => x.Disc3Percent <= v), DecCmp.Ge => q.Where(x => x.Disc3Percent >= v), DecCmp.Lt => q.Where(x => x.Disc3Percent < v), _ => q.Where(x => x.Disc3Percent > v) };
            if (IsMemberDec(prop, nameof(SalesInvoiceLine.DiscountValue)))
                return cmp switch { DecCmp.Le => q.Where(x => x.DiscountValue <= v), DecCmp.Ge => q.Where(x => x.DiscountValue >= v), DecCmp.Lt => q.Where(x => x.DiscountValue < v), _ => q.Where(x => x.DiscountValue > v) };
            if (IsMemberDec(prop, nameof(SalesInvoiceLine.LineTotalAfterDiscount)))
                return cmp switch { DecCmp.Le => q.Where(x => x.LineTotalAfterDiscount <= v), DecCmp.Ge => q.Where(x => x.LineTotalAfterDiscount >= v), DecCmp.Lt => q.Where(x => x.LineTotalAfterDiscount < v), _ => q.Where(x => x.LineTotalAfterDiscount > v) };
            return cmp switch { DecCmp.Le => q.Where(x => x.LineNetTotal <= v), DecCmp.Ge => q.Where(x => x.LineNetTotal >= v), DecCmp.Lt => q.Where(x => x.LineNetTotal < v), _ => q.Where(x => x.LineNetTotal > v) };
        }

        private static IQueryable<SalesInvoiceLine> WhereDecRange(IQueryable<SalesInvoiceLine> q, System.Linq.Expressions.Expression<Func<SalesInvoiceLine, decimal>> prop, decimal a, decimal b)
        {
            if (IsMemberDec(prop, nameof(SalesInvoiceLine.PriceRetail)))
                return q.Where(x => x.PriceRetail >= a && x.PriceRetail <= b);
            if (IsMemberDec(prop, nameof(SalesInvoiceLine.Disc1Percent)))
                return q.Where(x => x.Disc1Percent >= a && x.Disc1Percent <= b);
            if (IsMemberDec(prop, nameof(SalesInvoiceLine.Disc2Percent)))
                return q.Where(x => x.Disc2Percent >= a && x.Disc2Percent <= b);
            if (IsMemberDec(prop, nameof(SalesInvoiceLine.Disc3Percent)))
                return q.Where(x => x.Disc3Percent >= a && x.Disc3Percent <= b);
            if (IsMemberDec(prop, nameof(SalesInvoiceLine.DiscountValue)))
                return q.Where(x => x.DiscountValue >= a && x.DiscountValue <= b);
            if (IsMemberDec(prop, nameof(SalesInvoiceLine.LineTotalAfterDiscount)))
                return q.Where(x => x.LineTotalAfterDiscount >= a && x.LineTotalAfterDiscount <= b);
            return q.Where(x => x.LineNetTotal >= a && x.LineNetTotal <= b);
        }

        private static IQueryable<SalesInvoiceLine> WhereDecEq(IQueryable<SalesInvoiceLine> q, System.Linq.Expressions.Expression<Func<SalesInvoiceLine, decimal>> prop, decimal v)
        {
            if (IsMemberDec(prop, nameof(SalesInvoiceLine.PriceRetail)))
                return q.Where(x => x.PriceRetail == v);
            if (IsMemberDec(prop, nameof(SalesInvoiceLine.Disc1Percent)))
                return q.Where(x => x.Disc1Percent == v);
            if (IsMemberDec(prop, nameof(SalesInvoiceLine.Disc2Percent)))
                return q.Where(x => x.Disc2Percent == v);
            if (IsMemberDec(prop, nameof(SalesInvoiceLine.Disc3Percent)))
                return q.Where(x => x.Disc3Percent == v);
            if (IsMemberDec(prop, nameof(SalesInvoiceLine.DiscountValue)))
                return q.Where(x => x.DiscountValue == v);
            if (IsMemberDec(prop, nameof(SalesInvoiceLine.LineTotalAfterDiscount)))
                return q.Where(x => x.LineTotalAfterDiscount == v);
            return q.Where(x => x.LineNetTotal == v);
        }

        private static bool IsMemberDec(System.Linq.Expressions.Expression<Func<SalesInvoiceLine, decimal>> prop, string name) =>
            prop.Body is System.Linq.Expressions.MemberExpression m && m.Member.Name == name;
    }

    /// <summary>تعبيرات رقمية لقائمة أصناف أوامر البيع — مفاتيح الأعمدة كما في data-col.</summary>
    public static class SOLineListNumericExpr
    {
        public static IQueryable<SOLine> ApplyForColumn(IQueryable<SOLine> q, string column, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return q;
            var expr = raw.Trim();
            column = (column ?? "").Trim().ToLowerInvariant();
            return column switch
            {
                "soid" => ApplyInt(q, expr, x => x.SOId),
                "lineno" => ApplyInt(q, expr, x => x.LineNo),
                "prod" => ApplyInt(q, expr, x => x.ProdId),
                "qty" => ApplyInt(q, expr, x => x.QtyRequested),
                "reqretail" => ApplyDecimal(q, expr, x => x.RequestedRetailPrice),
                "disc" => ApplyDecimal(q, expr, x => x.SalesDiscountPct),
                "linetotal" => ApplyLineTotal(q, expr),
                _ => q
            };
        }

        private static IQueryable<SOLine> ApplyInt(IQueryable<SOLine> q, string expr, System.Linq.Expressions.Expression<Func<SOLine, int>> prop)
        {
            if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var le))
                return WhereInt(q, prop, le, IntCmp.Le);
            if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var ge))
                return WhereInt(q, prop, ge, IntCmp.Ge);
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var lt))
                return WhereInt(q, prop, lt, IntCmp.Lt);
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var gt))
                return WhereInt(q, prop, gt, IntCmp.Gt);
            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var sep = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    return WhereIntRange(q, prop, a, b);
                }
            }
            if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                return WhereIntEq(q, prop, ex);
            return q;
        }

        private enum IntCmp { Le, Ge, Lt, Gt }

        private static IQueryable<SOLine> WhereInt(IQueryable<SOLine> q, System.Linq.Expressions.Expression<Func<SOLine, int>> prop, int v, IntCmp cmp)
        {
            if (IsIntMember(prop, nameof(SOLine.SOId)))
                return cmp switch { IntCmp.Le => q.Where(x => x.SOId <= v), IntCmp.Ge => q.Where(x => x.SOId >= v), IntCmp.Lt => q.Where(x => x.SOId < v), _ => q.Where(x => x.SOId > v) };
            if (IsIntMember(prop, nameof(SOLine.LineNo)))
                return cmp switch { IntCmp.Le => q.Where(x => x.LineNo <= v), IntCmp.Ge => q.Where(x => x.LineNo >= v), IntCmp.Lt => q.Where(x => x.LineNo < v), _ => q.Where(x => x.LineNo > v) };
            if (IsIntMember(prop, nameof(SOLine.ProdId)))
                return cmp switch { IntCmp.Le => q.Where(x => x.ProdId <= v), IntCmp.Ge => q.Where(x => x.ProdId >= v), IntCmp.Lt => q.Where(x => x.ProdId < v), _ => q.Where(x => x.ProdId > v) };
            return cmp switch { IntCmp.Le => q.Where(x => x.QtyRequested <= v), IntCmp.Ge => q.Where(x => x.QtyRequested >= v), IntCmp.Lt => q.Where(x => x.QtyRequested < v), _ => q.Where(x => x.QtyRequested > v) };
        }

        private static IQueryable<SOLine> WhereIntRange(IQueryable<SOLine> q, System.Linq.Expressions.Expression<Func<SOLine, int>> prop, int a, int b)
        {
            if (IsIntMember(prop, nameof(SOLine.SOId)))
                return q.Where(x => x.SOId >= a && x.SOId <= b);
            if (IsIntMember(prop, nameof(SOLine.LineNo)))
                return q.Where(x => x.LineNo >= a && x.LineNo <= b);
            if (IsIntMember(prop, nameof(SOLine.ProdId)))
                return q.Where(x => x.ProdId >= a && x.ProdId <= b);
            return q.Where(x => x.QtyRequested >= a && x.QtyRequested <= b);
        }

        private static IQueryable<SOLine> WhereIntEq(IQueryable<SOLine> q, System.Linq.Expressions.Expression<Func<SOLine, int>> prop, int v)
        {
            if (IsIntMember(prop, nameof(SOLine.SOId)))
                return q.Where(x => x.SOId == v);
            if (IsIntMember(prop, nameof(SOLine.LineNo)))
                return q.Where(x => x.LineNo == v);
            if (IsIntMember(prop, nameof(SOLine.ProdId)))
                return q.Where(x => x.ProdId == v);
            return q.Where(x => x.QtyRequested == v);
        }

        private static bool IsIntMember(System.Linq.Expressions.Expression<Func<SOLine, int>> prop, string name) =>
            prop.Body is System.Linq.Expressions.MemberExpression m && m.Member.Name == name;

        private static IQueryable<SOLine> ApplyDecimal(IQueryable<SOLine> q, string expr, System.Linq.Expressions.Expression<Func<SOLine, decimal>> prop)
        {
            if (expr.StartsWith("<=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var le))
                return WhereDec(q, prop, le, DecCmp.Le);
            if (expr.StartsWith(">=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var ge))
                return WhereDec(q, prop, ge, DecCmp.Ge);
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var lt))
                return WhereDec(q, prop, lt, DecCmp.Lt);
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var gt))
                return WhereDec(q, prop, gt, DecCmp.Gt);
            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var sep = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && decimal.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && decimal.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    return WhereDecRange(q, prop, a, b);
                }
            }
            if (decimal.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                return WhereDecEq(q, prop, ex);
            return q;
        }

        private enum DecCmp { Le, Ge, Lt, Gt }

        private static IQueryable<SOLine> WhereDec(IQueryable<SOLine> q, System.Linq.Expressions.Expression<Func<SOLine, decimal>> prop, decimal v, DecCmp cmp)
        {
            if (IsDecMember(prop, nameof(SOLine.RequestedRetailPrice)))
                return cmp switch { DecCmp.Le => q.Where(x => x.RequestedRetailPrice <= v), DecCmp.Ge => q.Where(x => x.RequestedRetailPrice >= v), DecCmp.Lt => q.Where(x => x.RequestedRetailPrice < v), _ => q.Where(x => x.RequestedRetailPrice > v) };
            return cmp switch { DecCmp.Le => q.Where(x => x.SalesDiscountPct <= v), DecCmp.Ge => q.Where(x => x.SalesDiscountPct >= v), DecCmp.Lt => q.Where(x => x.SalesDiscountPct < v), _ => q.Where(x => x.SalesDiscountPct > v) };
        }

        private static IQueryable<SOLine> WhereDecRange(IQueryable<SOLine> q, System.Linq.Expressions.Expression<Func<SOLine, decimal>> prop, decimal a, decimal b)
        {
            if (IsDecMember(prop, nameof(SOLine.RequestedRetailPrice)))
                return q.Where(x => x.RequestedRetailPrice >= a && x.RequestedRetailPrice <= b);
            return q.Where(x => x.SalesDiscountPct >= a && x.SalesDiscountPct <= b);
        }

        private static IQueryable<SOLine> WhereDecEq(IQueryable<SOLine> q, System.Linq.Expressions.Expression<Func<SOLine, decimal>> prop, decimal v)
        {
            if (IsDecMember(prop, nameof(SOLine.RequestedRetailPrice)))
                return q.Where(x => x.RequestedRetailPrice == v);
            return q.Where(x => x.SalesDiscountPct == v);
        }

        private static bool IsDecMember(System.Linq.Expressions.Expression<Func<SOLine, decimal>> prop, string name) =>
            prop.Body is System.Linq.Expressions.MemberExpression m && m.Member.Name == name;

        private static IQueryable<SOLine> ApplyLineTotal(IQueryable<SOLine> q, string expr)
        {
            if (expr.StartsWith("<=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var le))
                return q.Where(x => (x.ExpectedUnitPrice * x.QtyRequested) <= le);
            if (expr.StartsWith(">=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var ge))
                return q.Where(x => (x.ExpectedUnitPrice * x.QtyRequested) >= ge);
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var lt))
                return q.Where(x => (x.ExpectedUnitPrice * x.QtyRequested) < lt);
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var gt))
                return q.Where(x => (x.ExpectedUnitPrice * x.QtyRequested) > gt);
            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var sep = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && decimal.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && decimal.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    return q.Where(x => (x.ExpectedUnitPrice * x.QtyRequested) >= a && (x.ExpectedUnitPrice * x.QtyRequested) <= b);
                }
            }
            if (decimal.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                return q.Where(x => (x.ExpectedUnitPrice * x.QtyRequested) == ex);
            return q;
        }
    }
}
