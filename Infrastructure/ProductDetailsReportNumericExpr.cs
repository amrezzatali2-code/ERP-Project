using System.Linq;
using ERP.Models;

namespace ERP.Infrastructure
{
    /// <summary>
    /// فلتر أعمدة رقمية (filterCol_*Expr) لتقرير الأصناف المفصّلة.
    /// </summary>
    public static class ProductDetailsReportNumericExpr
    {
        public static IQueryable<SalesInvoiceLine> ApplySalesInvoiceLine(
            IQueryable<SalesInvoiceLine> q,
            string? qtyExpr, string? unitPriceExpr, string? lineTotalExpr)
        {
            if (NumericColumnExprParser.TryParseInt(qtyExpr, out var qr))
                q = ApplyIntSalesQty(q, qr);
            if (NumericColumnExprParser.TryParseDecimal(unitPriceExpr, out var pr))
                q = ApplyDecimalSalesPrice(q, pr);
            if (NumericColumnExprParser.TryParseDecimal(lineTotalExpr, out var tr))
                q = ApplyDecimalSalesNet(q, tr);
            return q;
        }

        private static IQueryable<SalesInvoiceLine> ApplyIntSalesQty(IQueryable<SalesInvoiceLine> q, NumericFilterResult tr)
        {
            var a = (int)tr.Min;
            var b = (int)tr.Max;
            return tr.Kind switch
            {
                NumericFilterKind.Exact => q.Where(l => l.Qty == a),
                NumericFilterKind.GreaterOrEqual => q.Where(l => l.Qty >= a),
                NumericFilterKind.LessOrEqual => q.Where(l => l.Qty <= a),
                NumericFilterKind.Greater => q.Where(l => l.Qty > a),
                NumericFilterKind.Less => q.Where(l => l.Qty < a),
                NumericFilterKind.Between => q.Where(l => l.Qty >= a && l.Qty <= b),
                _ => q
            };
        }

        private static IQueryable<SalesInvoiceLine> ApplyDecimalSalesPrice(IQueryable<SalesInvoiceLine> q, NumericFilterResult tr)
        {
            var a = tr.Min;
            var b = tr.Max;
            return tr.Kind switch
            {
                NumericFilterKind.Exact => q.Where(l => l.PriceRetail == a),
                NumericFilterKind.GreaterOrEqual => q.Where(l => l.PriceRetail >= a),
                NumericFilterKind.LessOrEqual => q.Where(l => l.PriceRetail <= a),
                NumericFilterKind.Greater => q.Where(l => l.PriceRetail > a),
                NumericFilterKind.Less => q.Where(l => l.PriceRetail < a),
                NumericFilterKind.Between => q.Where(l => l.PriceRetail >= a && l.PriceRetail <= b),
                _ => q
            };
        }

        private static IQueryable<SalesInvoiceLine> ApplyDecimalSalesNet(IQueryable<SalesInvoiceLine> q, NumericFilterResult tr)
        {
            var a = tr.Min;
            var b = tr.Max;
            return tr.Kind switch
            {
                NumericFilterKind.Exact => q.Where(l => l.LineNetTotal == a),
                NumericFilterKind.GreaterOrEqual => q.Where(l => l.LineNetTotal >= a),
                NumericFilterKind.LessOrEqual => q.Where(l => l.LineNetTotal <= a),
                NumericFilterKind.Greater => q.Where(l => l.LineNetTotal > a),
                NumericFilterKind.Less => q.Where(l => l.LineNetTotal < a),
                NumericFilterKind.Between => q.Where(l => l.LineNetTotal >= a && l.LineNetTotal <= b),
                _ => q
            };
        }

        public static IQueryable<PILine> ApplyPiLine(
            IQueryable<PILine> q,
            string? qtyExpr, string? unitPriceExpr, string? lineTotalExpr)
        {
            if (NumericColumnExprParser.TryParseInt(qtyExpr, out var qr))
                q = ApplyIntPiQty(q, qr);
            if (NumericColumnExprParser.TryParseDecimal(unitPriceExpr, out var pr))
                q = ApplyDecimalPiCost(q, pr);
            if (NumericColumnExprParser.TryParseDecimal(lineTotalExpr, out var tr))
                q = ApplyDecimalPiLineTotal(q, tr);
            return q;
        }

        private static IQueryable<PILine> ApplyIntPiQty(IQueryable<PILine> q, NumericFilterResult tr)
        {
            var a = (int)tr.Min;
            var b = (int)tr.Max;
            return tr.Kind switch
            {
                NumericFilterKind.Exact => q.Where(l => l.Qty == a),
                NumericFilterKind.GreaterOrEqual => q.Where(l => l.Qty >= a),
                NumericFilterKind.LessOrEqual => q.Where(l => l.Qty <= a),
                NumericFilterKind.Greater => q.Where(l => l.Qty > a),
                NumericFilterKind.Less => q.Where(l => l.Qty < a),
                NumericFilterKind.Between => q.Where(l => l.Qty >= a && l.Qty <= b),
                _ => q
            };
        }

        private static IQueryable<PILine> ApplyDecimalPiCost(IQueryable<PILine> q, NumericFilterResult tr)
        {
            var a = tr.Min;
            var b = tr.Max;
            return tr.Kind switch
            {
                NumericFilterKind.Exact => q.Where(l => l.UnitCost == a),
                NumericFilterKind.GreaterOrEqual => q.Where(l => l.UnitCost >= a),
                NumericFilterKind.LessOrEqual => q.Where(l => l.UnitCost <= a),
                NumericFilterKind.Greater => q.Where(l => l.UnitCost > a),
                NumericFilterKind.Less => q.Where(l => l.UnitCost < a),
                NumericFilterKind.Between => q.Where(l => l.UnitCost >= a && l.UnitCost <= b),
                _ => q
            };
        }

        private static IQueryable<PILine> ApplyDecimalPiLineTotal(IQueryable<PILine> q, NumericFilterResult tr)
        {
            var a = tr.Min;
            var b = tr.Max;
            return tr.Kind switch
            {
                NumericFilterKind.Exact => q.Where(l => l.Qty * l.UnitCost == a),
                NumericFilterKind.GreaterOrEqual => q.Where(l => l.Qty * l.UnitCost >= a),
                NumericFilterKind.LessOrEqual => q.Where(l => l.Qty * l.UnitCost <= a),
                NumericFilterKind.Greater => q.Where(l => l.Qty * l.UnitCost > a),
                NumericFilterKind.Less => q.Where(l => l.Qty * l.UnitCost < a),
                NumericFilterKind.Between => q.Where(l => l.Qty * l.UnitCost >= a && l.Qty * l.UnitCost <= b),
                _ => q
            };
        }

        public static IQueryable<PurchaseReturnLine> ApplyPurchaseReturnLine(
            IQueryable<PurchaseReturnLine> q,
            string? qtyExpr, string? unitPriceExpr, string? lineTotalExpr)
        {
            if (NumericColumnExprParser.TryParseInt(qtyExpr, out var qr))
                q = ApplyIntPrQty(q, qr);
            if (NumericColumnExprParser.TryParseDecimal(unitPriceExpr, out var pr))
                q = ApplyDecimalPrCost(q, pr);
            if (NumericColumnExprParser.TryParseDecimal(lineTotalExpr, out var tr))
                q = ApplyDecimalPrLineTotal(q, tr);
            return q;
        }

        private static IQueryable<PurchaseReturnLine> ApplyIntPrQty(IQueryable<PurchaseReturnLine> q, NumericFilterResult tr)
        {
            var a = (int)tr.Min;
            var b = (int)tr.Max;
            return tr.Kind switch
            {
                NumericFilterKind.Exact => q.Where(l => l.Qty == a),
                NumericFilterKind.GreaterOrEqual => q.Where(l => l.Qty >= a),
                NumericFilterKind.LessOrEqual => q.Where(l => l.Qty <= a),
                NumericFilterKind.Greater => q.Where(l => l.Qty > a),
                NumericFilterKind.Less => q.Where(l => l.Qty < a),
                NumericFilterKind.Between => q.Where(l => l.Qty >= a && l.Qty <= b),
                _ => q
            };
        }

        private static IQueryable<PurchaseReturnLine> ApplyDecimalPrCost(IQueryable<PurchaseReturnLine> q, NumericFilterResult tr)
        {
            var a = tr.Min;
            var b = tr.Max;
            return tr.Kind switch
            {
                NumericFilterKind.Exact => q.Where(l => l.UnitCost == a),
                NumericFilterKind.GreaterOrEqual => q.Where(l => l.UnitCost >= a),
                NumericFilterKind.LessOrEqual => q.Where(l => l.UnitCost <= a),
                NumericFilterKind.Greater => q.Where(l => l.UnitCost > a),
                NumericFilterKind.Less => q.Where(l => l.UnitCost < a),
                NumericFilterKind.Between => q.Where(l => l.UnitCost >= a && l.UnitCost <= b),
                _ => q
            };
        }

        private static IQueryable<PurchaseReturnLine> ApplyDecimalPrLineTotal(IQueryable<PurchaseReturnLine> q, NumericFilterResult tr)
        {
            var a = tr.Min;
            var b = tr.Max;
            return tr.Kind switch
            {
                NumericFilterKind.Exact => q.Where(l => l.Qty * l.UnitCost == a),
                NumericFilterKind.GreaterOrEqual => q.Where(l => l.Qty * l.UnitCost >= a),
                NumericFilterKind.LessOrEqual => q.Where(l => l.Qty * l.UnitCost <= a),
                NumericFilterKind.Greater => q.Where(l => l.Qty * l.UnitCost > a),
                NumericFilterKind.Less => q.Where(l => l.Qty * l.UnitCost < a),
                NumericFilterKind.Between => q.Where(l => l.Qty * l.UnitCost >= a && l.Qty * l.UnitCost <= b),
                _ => q
            };
        }

        public static IQueryable<StockAdjustmentLine> ApplyStockAdjustmentLine(
            IQueryable<StockAdjustmentLine> q,
            string? qtyExpr, string? unitPriceExpr, string? lineTotalExpr)
        {
            if (NumericColumnExprParser.TryParseInt(qtyExpr, out var qr))
                q = ApplyIntAdjQty(q, qr);
            if (NumericColumnExprParser.TryParseDecimal(unitPriceExpr, out var pr))
                q = ApplyDecimalAdjCpu(q, pr);
            if (NumericColumnExprParser.TryParseDecimal(lineTotalExpr, out var tr))
                q = ApplyDecimalAdjCostDiff(q, tr);
            return q;
        }

        private static IQueryable<StockAdjustmentLine> ApplyIntAdjQty(IQueryable<StockAdjustmentLine> q, NumericFilterResult tr)
        {
            var a = (int)tr.Min;
            var b = (int)tr.Max;
            return tr.Kind switch
            {
                NumericFilterKind.Exact => q.Where(l => l.QtyDiff == a),
                NumericFilterKind.GreaterOrEqual => q.Where(l => l.QtyDiff >= a),
                NumericFilterKind.LessOrEqual => q.Where(l => l.QtyDiff <= a),
                NumericFilterKind.Greater => q.Where(l => l.QtyDiff > a),
                NumericFilterKind.Less => q.Where(l => l.QtyDiff < a),
                NumericFilterKind.Between => q.Where(l => l.QtyDiff >= a && l.QtyDiff <= b),
                _ => q
            };
        }

        private static IQueryable<StockAdjustmentLine> ApplyDecimalAdjCpu(IQueryable<StockAdjustmentLine> q, NumericFilterResult tr)
        {
            var a = tr.Min;
            var b = tr.Max;
            return tr.Kind switch
            {
                NumericFilterKind.Exact => q.Where(l => l.CostPerUnit != null && l.CostPerUnit == a),
                NumericFilterKind.GreaterOrEqual => q.Where(l => l.CostPerUnit != null && l.CostPerUnit >= a),
                NumericFilterKind.LessOrEqual => q.Where(l => l.CostPerUnit != null && l.CostPerUnit <= a),
                NumericFilterKind.Greater => q.Where(l => l.CostPerUnit != null && l.CostPerUnit > a),
                NumericFilterKind.Less => q.Where(l => l.CostPerUnit != null && l.CostPerUnit < a),
                NumericFilterKind.Between => q.Where(l => l.CostPerUnit != null && l.CostPerUnit >= a && l.CostPerUnit <= b),
                _ => q
            };
        }

        private static IQueryable<StockAdjustmentLine> ApplyDecimalAdjCostDiff(IQueryable<StockAdjustmentLine> q, NumericFilterResult tr)
        {
            var a = tr.Min;
            var b = tr.Max;
            return tr.Kind switch
            {
                NumericFilterKind.Exact => q.Where(l => l.CostDiff != null && l.CostDiff == a),
                NumericFilterKind.GreaterOrEqual => q.Where(l => l.CostDiff != null && l.CostDiff >= a),
                NumericFilterKind.LessOrEqual => q.Where(l => l.CostDiff != null && l.CostDiff <= a),
                NumericFilterKind.Greater => q.Where(l => l.CostDiff != null && l.CostDiff > a),
                NumericFilterKind.Less => q.Where(l => l.CostDiff != null && l.CostDiff < a),
                NumericFilterKind.Between => q.Where(l => l.CostDiff != null && l.CostDiff >= a && l.CostDiff <= b),
                _ => q
            };
        }

        public static IQueryable<StockTransferLine> ApplyStockTransferLine(
            IQueryable<StockTransferLine> q,
            string? qtyExpr, string? unitPriceExpr, string? lineTotalExpr)
        {
            if (NumericColumnExprParser.TryParseInt(qtyExpr, out var qr))
                q = ApplyIntStQty(q, qr);
            if (NumericColumnExprParser.TryParseDecimal(unitPriceExpr, out var pr))
                q = ApplyDecimalStCost(q, pr);
            if (NumericColumnExprParser.TryParseDecimal(lineTotalExpr, out var tr))
                q = ApplyDecimalStLineTotal(q, tr);
            return q;
        }

        private static IQueryable<StockTransferLine> ApplyIntStQty(IQueryable<StockTransferLine> q, NumericFilterResult tr)
        {
            var a = (int)tr.Min;
            var b = (int)tr.Max;
            return tr.Kind switch
            {
                NumericFilterKind.Exact => q.Where(l => l.Qty == a),
                NumericFilterKind.GreaterOrEqual => q.Where(l => l.Qty >= a),
                NumericFilterKind.LessOrEqual => q.Where(l => l.Qty <= a),
                NumericFilterKind.Greater => q.Where(l => l.Qty > a),
                NumericFilterKind.Less => q.Where(l => l.Qty < a),
                NumericFilterKind.Between => q.Where(l => l.Qty >= a && l.Qty <= b),
                _ => q
            };
        }

        private static IQueryable<StockTransferLine> ApplyDecimalStCost(IQueryable<StockTransferLine> q, NumericFilterResult tr)
        {
            var a = tr.Min;
            var b = tr.Max;
            return tr.Kind switch
            {
                NumericFilterKind.Exact => q.Where(l => l.UnitCost == a),
                NumericFilterKind.GreaterOrEqual => q.Where(l => l.UnitCost >= a),
                NumericFilterKind.LessOrEqual => q.Where(l => l.UnitCost <= a),
                NumericFilterKind.Greater => q.Where(l => l.UnitCost > a),
                NumericFilterKind.Less => q.Where(l => l.UnitCost < a),
                NumericFilterKind.Between => q.Where(l => l.UnitCost >= a && l.UnitCost <= b),
                _ => q
            };
        }

        private static IQueryable<StockTransferLine> ApplyDecimalStLineTotal(IQueryable<StockTransferLine> q, NumericFilterResult tr)
        {
            var a = tr.Min;
            var b = tr.Max;
            return tr.Kind switch
            {
                NumericFilterKind.Exact => q.Where(l => l.Qty * l.UnitCost == a),
                NumericFilterKind.GreaterOrEqual => q.Where(l => l.Qty * l.UnitCost >= a),
                NumericFilterKind.LessOrEqual => q.Where(l => l.Qty * l.UnitCost <= a),
                NumericFilterKind.Greater => q.Where(l => l.Qty * l.UnitCost > a),
                NumericFilterKind.Less => q.Where(l => l.Qty * l.UnitCost < a),
                NumericFilterKind.Between => q.Where(l => l.Qty * l.UnitCost >= a && l.Qty * l.UnitCost <= b),
                _ => q
            };
        }

        public static IQueryable<PRLine> ApplyPrLine(
            IQueryable<PRLine> q,
            string? qtyExpr, string? unitPriceExpr, string? lineTotalExpr)
        {
            if (NumericColumnExprParser.TryParseDecimal(qtyExpr, out var qr))
                q = ApplyDecimalPrReqQty(q, qr);
            if (NumericColumnExprParser.TryParseDecimal(unitPriceExpr, out var pr))
                q = ApplyDecimalPrExpCost(q, pr);
            if (NumericColumnExprParser.TryParseDecimal(lineTotalExpr, out var tr))
                q = ApplyDecimalPrReqLineTotal(q, tr);
            return q;
        }

        private static IQueryable<PRLine> ApplyDecimalPrReqQty(IQueryable<PRLine> q, NumericFilterResult tr)
        {
            var a = tr.Min;
            var b = tr.Max;
            return tr.Kind switch
            {
                NumericFilterKind.Exact => q.Where(l => l.QtyRequested == a),
                NumericFilterKind.GreaterOrEqual => q.Where(l => l.QtyRequested >= a),
                NumericFilterKind.LessOrEqual => q.Where(l => l.QtyRequested <= a),
                NumericFilterKind.Greater => q.Where(l => l.QtyRequested > a),
                NumericFilterKind.Less => q.Where(l => l.QtyRequested < a),
                NumericFilterKind.Between => q.Where(l => l.QtyRequested >= a && l.QtyRequested <= b),
                _ => q
            };
        }

        private static IQueryable<PRLine> ApplyDecimalPrExpCost(IQueryable<PRLine> q, NumericFilterResult tr)
        {
            var a = tr.Min;
            var b = tr.Max;
            return tr.Kind switch
            {
                NumericFilterKind.Exact => q.Where(l => l.ExpectedCost == a),
                NumericFilterKind.GreaterOrEqual => q.Where(l => l.ExpectedCost >= a),
                NumericFilterKind.LessOrEqual => q.Where(l => l.ExpectedCost <= a),
                NumericFilterKind.Greater => q.Where(l => l.ExpectedCost > a),
                NumericFilterKind.Less => q.Where(l => l.ExpectedCost < a),
                NumericFilterKind.Between => q.Where(l => l.ExpectedCost >= a && l.ExpectedCost <= b),
                _ => q
            };
        }

        private static IQueryable<PRLine> ApplyDecimalPrReqLineTotal(IQueryable<PRLine> q, NumericFilterResult tr)
        {
            var a = tr.Min;
            var b = tr.Max;
            return tr.Kind switch
            {
                NumericFilterKind.Exact => q.Where(l => l.QtyRequested * l.ExpectedCost == a),
                NumericFilterKind.GreaterOrEqual => q.Where(l => l.QtyRequested * l.ExpectedCost >= a),
                NumericFilterKind.LessOrEqual => q.Where(l => l.QtyRequested * l.ExpectedCost <= a),
                NumericFilterKind.Greater => q.Where(l => l.QtyRequested * l.ExpectedCost > a),
                NumericFilterKind.Less => q.Where(l => l.QtyRequested * l.ExpectedCost < a),
                NumericFilterKind.Between => q.Where(l => l.QtyRequested * l.ExpectedCost >= a && l.QtyRequested * l.ExpectedCost <= b),
                _ => q
            };
        }

        public static IQueryable<SOLine> ApplySoLine(
            IQueryable<SOLine> q,
            string? qtyExpr, string? unitPriceExpr, string? lineTotalExpr)
        {
            if (NumericColumnExprParser.TryParseDecimal(qtyExpr, out var qr))
                q = ApplyDecimalSoQty(q, qr);
            if (NumericColumnExprParser.TryParseDecimal(unitPriceExpr, out var pr))
                q = ApplyDecimalSoRetail(q, pr);
            if (NumericColumnExprParser.TryParseDecimal(lineTotalExpr, out var tr))
                q = ApplyDecimalSoLineTotal(q, tr);
            return q;
        }

        private static IQueryable<SOLine> ApplyDecimalSoQty(IQueryable<SOLine> q, NumericFilterResult tr)
        {
            var a = tr.Min;
            var b = tr.Max;
            return tr.Kind switch
            {
                NumericFilterKind.Exact => q.Where(l => l.QtyRequested == a),
                NumericFilterKind.GreaterOrEqual => q.Where(l => l.QtyRequested >= a),
                NumericFilterKind.LessOrEqual => q.Where(l => l.QtyRequested <= a),
                NumericFilterKind.Greater => q.Where(l => l.QtyRequested > a),
                NumericFilterKind.Less => q.Where(l => l.QtyRequested < a),
                NumericFilterKind.Between => q.Where(l => l.QtyRequested >= a && l.QtyRequested <= b),
                _ => q
            };
        }

        private static IQueryable<SOLine> ApplyDecimalSoRetail(IQueryable<SOLine> q, NumericFilterResult tr)
        {
            var a = tr.Min;
            var b = tr.Max;
            return tr.Kind switch
            {
                NumericFilterKind.Exact => q.Where(l => l.RequestedRetailPrice == a),
                NumericFilterKind.GreaterOrEqual => q.Where(l => l.RequestedRetailPrice >= a),
                NumericFilterKind.LessOrEqual => q.Where(l => l.RequestedRetailPrice <= a),
                NumericFilterKind.Greater => q.Where(l => l.RequestedRetailPrice > a),
                NumericFilterKind.Less => q.Where(l => l.RequestedRetailPrice < a),
                NumericFilterKind.Between => q.Where(l => l.RequestedRetailPrice >= a && l.RequestedRetailPrice <= b),
                _ => q
            };
        }

        private static IQueryable<SOLine> ApplyDecimalSoLineTotal(IQueryable<SOLine> q, NumericFilterResult tr)
        {
            var a = tr.Min;
            var b = tr.Max;
            return tr.Kind switch
            {
                NumericFilterKind.Exact => q.Where(l => l.QtyRequested * l.RequestedRetailPrice * (1 - l.SalesDiscountPct / 100m) == a),
                NumericFilterKind.GreaterOrEqual => q.Where(l => l.QtyRequested * l.RequestedRetailPrice * (1 - l.SalesDiscountPct / 100m) >= a),
                NumericFilterKind.LessOrEqual => q.Where(l => l.QtyRequested * l.RequestedRetailPrice * (1 - l.SalesDiscountPct / 100m) <= a),
                NumericFilterKind.Greater => q.Where(l => l.QtyRequested * l.RequestedRetailPrice * (1 - l.SalesDiscountPct / 100m) > a),
                NumericFilterKind.Less => q.Where(l => l.QtyRequested * l.RequestedRetailPrice * (1 - l.SalesDiscountPct / 100m) < a),
                NumericFilterKind.Between => q.Where(l =>
                    l.QtyRequested * l.RequestedRetailPrice * (1 - l.SalesDiscountPct / 100m) >= a &&
                    l.QtyRequested * l.RequestedRetailPrice * (1 - l.SalesDiscountPct / 100m) <= b),
                _ => q
            };
        }

        /// <summary>مرتجع بيع: فلترة على سطور المرتجع.</summary>
        public static IQueryable<SalesReturnLine> ApplySalesReturnLine(
            IQueryable<SalesReturnLine> q,
            string? qtyExpr, string? unitPriceExpr, string? lineTotalExpr)
        {
            if (NumericColumnExprParser.TryParseInt(qtyExpr, out var qr))
                q = ApplyIntSrQty(q, qr);
            if (NumericColumnExprParser.TryParseDecimal(unitPriceExpr, out var pr))
                q = ApplyDecimalSrUnit(q, pr);
            if (NumericColumnExprParser.TryParseDecimal(lineTotalExpr, out var tr))
                q = ApplyDecimalSrNet(q, tr);
            return q;
        }

        private static IQueryable<SalesReturnLine> ApplyIntSrQty(IQueryable<SalesReturnLine> q, NumericFilterResult tr)
        {
            var a = (int)tr.Min;
            var b = (int)tr.Max;
            return tr.Kind switch
            {
                NumericFilterKind.Exact => q.Where(l => l.Qty == a),
                NumericFilterKind.GreaterOrEqual => q.Where(l => l.Qty >= a),
                NumericFilterKind.LessOrEqual => q.Where(l => l.Qty <= a),
                NumericFilterKind.Greater => q.Where(l => l.Qty > a),
                NumericFilterKind.Less => q.Where(l => l.Qty < a),
                NumericFilterKind.Between => q.Where(l => l.Qty >= a && l.Qty <= b),
                _ => q
            };
        }

        private static IQueryable<SalesReturnLine> ApplyDecimalSrUnit(IQueryable<SalesReturnLine> q, NumericFilterResult tr)
        {
            var a = tr.Min;
            var b = tr.Max;
            return tr.Kind switch
            {
                NumericFilterKind.Exact => q.Where(l => l.UnitSalePrice == a),
                NumericFilterKind.GreaterOrEqual => q.Where(l => l.UnitSalePrice >= a),
                NumericFilterKind.LessOrEqual => q.Where(l => l.UnitSalePrice <= a),
                NumericFilterKind.Greater => q.Where(l => l.UnitSalePrice > a),
                NumericFilterKind.Less => q.Where(l => l.UnitSalePrice < a),
                NumericFilterKind.Between => q.Where(l => l.UnitSalePrice >= a && l.UnitSalePrice <= b),
                _ => q
            };
        }

        private static IQueryable<SalesReturnLine> ApplyDecimalSrNet(IQueryable<SalesReturnLine> q, NumericFilterResult tr)
        {
            var a = tr.Min;
            var b = tr.Max;
            return tr.Kind switch
            {
                NumericFilterKind.Exact => q.Where(l => l.LineNetTotal == a),
                NumericFilterKind.GreaterOrEqual => q.Where(l => l.LineNetTotal >= a),
                NumericFilterKind.LessOrEqual => q.Where(l => l.LineNetTotal <= a),
                NumericFilterKind.Greater => q.Where(l => l.LineNetTotal > a),
                NumericFilterKind.Less => q.Where(l => l.LineNetTotal < a),
                NumericFilterKind.Between => q.Where(l => l.LineNetTotal >= a && l.LineNetTotal <= b),
                _ => q
            };
        }
    }
}
