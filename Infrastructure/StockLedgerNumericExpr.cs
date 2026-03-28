using System.Globalization;
using System.Linq;
using ERP.Models;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure
{
    /// <summary>
    /// فلاتر أعمدة رقمية بنمط قائمة الأصناف (filterCol_*Expr): مطابقة أو &lt; &gt; نطاق.
    /// </summary>
    public static class StockLedgerNumericExpr
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static IQueryable<StockLedger> ApplyEntryIdExpr(IQueryable<StockLedger> q, string? expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return q;
            var s = expr.Trim();
            if (s.StartsWith("<=") && s.Length > 2 && int.TryParse(s.AsSpan(2), out var max))
                return q.Where(x => x.EntryId <= max);
            if (s.StartsWith(">=") && s.Length > 2 && int.TryParse(s.AsSpan(2), out var min))
                return q.Where(x => x.EntryId >= min);
            if (s.StartsWith("<") && !s.StartsWith("<=") && s.Length > 1 && int.TryParse(s.AsSpan(1), out var max2))
                return q.Where(x => x.EntryId < max2);
            if (s.StartsWith(">") && !s.StartsWith(">=") && s.Length > 1 && int.TryParse(s.AsSpan(1), out var min2))
                return q.Where(x => x.EntryId > min2);
            if ((s.Contains(':') || (s.Contains('-') && !s.StartsWith("-"))) && !s.StartsWith("-"))
            {
                var sep = s.Contains(':') ? ':' : '-';
                var parts = s.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out var from) && int.TryParse(parts[1].Trim(), out var to))
                {
                    if (from > to) (from, to) = (to, from);
                    return q.Where(x => x.EntryId >= from && x.EntryId <= to);
                }
            }
            if (int.TryParse(s, out var exact))
                return q.Where(x => x.EntryId == exact);
            return q;
        }

        public static IQueryable<StockLedger> ApplyWarehouseIdExpr(IQueryable<StockLedger> q, string? expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return q;
            var s = expr.Trim();
            if (s.StartsWith("<=") && s.Length > 2 && int.TryParse(s.AsSpan(2), out var max))
                return q.Where(x => x.WarehouseId <= max);
            if (s.StartsWith(">=") && s.Length > 2 && int.TryParse(s.AsSpan(2), out var min))
                return q.Where(x => x.WarehouseId >= min);
            if (s.StartsWith("<") && !s.StartsWith("<=") && s.Length > 1 && int.TryParse(s.AsSpan(1), out var max2))
                return q.Where(x => x.WarehouseId < max2);
            if (s.StartsWith(">") && !s.StartsWith(">=") && s.Length > 1 && int.TryParse(s.AsSpan(1), out var min2))
                return q.Where(x => x.WarehouseId > min2);
            if ((s.Contains(':') || (s.Contains('-') && !s.StartsWith("-"))) && !s.StartsWith("-"))
            {
                var sep = s.Contains(':') ? ':' : '-';
                var parts = s.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out var from) && int.TryParse(parts[1].Trim(), out var to))
                {
                    if (from > to) (from, to) = (to, from);
                    return q.Where(x => x.WarehouseId >= from && x.WarehouseId <= to);
                }
            }
            if (int.TryParse(s, out var exact))
                return q.Where(x => x.WarehouseId == exact);
            return q;
        }

        public static IQueryable<StockLedger> ApplyProdIdExpr(IQueryable<StockLedger> q, string? expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return q;
            var s = expr.Trim();
            if (s.StartsWith("<=") && s.Length > 2 && int.TryParse(s.AsSpan(2), out var max))
                return q.Where(x => x.ProdId <= max);
            if (s.StartsWith(">=") && s.Length > 2 && int.TryParse(s.AsSpan(2), out var min))
                return q.Where(x => x.ProdId >= min);
            if (s.StartsWith("<") && !s.StartsWith("<=") && s.Length > 1 && int.TryParse(s.AsSpan(1), out var max2))
                return q.Where(x => x.ProdId < max2);
            if (s.StartsWith(">") && !s.StartsWith(">=") && s.Length > 1 && int.TryParse(s.AsSpan(1), out var min2))
                return q.Where(x => x.ProdId > min2);
            if ((s.Contains(':') || (s.Contains('-') && !s.StartsWith("-"))) && !s.StartsWith("-"))
            {
                var sep = s.Contains(':') ? ':' : '-';
                var parts = s.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out var from) && int.TryParse(parts[1].Trim(), out var to))
                {
                    if (from > to) (from, to) = (to, from);
                    return q.Where(x => x.ProdId >= from && x.ProdId <= to);
                }
            }
            if (int.TryParse(s, out var exact))
                return q.Where(x => x.ProdId == exact);
            return q;
        }

        public static IQueryable<StockLedger> ApplySourceIdExpr(IQueryable<StockLedger> q, string? expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return q;
            var s = expr.Trim();
            if (s.StartsWith("<=") && s.Length > 2 && int.TryParse(s.AsSpan(2), out var max))
                return q.Where(x => x.SourceId <= max);
            if (s.StartsWith(">=") && s.Length > 2 && int.TryParse(s.AsSpan(2), out var min))
                return q.Where(x => x.SourceId >= min);
            if (s.StartsWith("<") && !s.StartsWith("<=") && s.Length > 1 && int.TryParse(s.AsSpan(1), out var max2))
                return q.Where(x => x.SourceId < max2);
            if (s.StartsWith(">") && !s.StartsWith(">=") && s.Length > 1 && int.TryParse(s.AsSpan(1), out var min2))
                return q.Where(x => x.SourceId > min2);
            if ((s.Contains(':') || (s.Contains('-') && !s.StartsWith("-"))) && !s.StartsWith("-"))
            {
                var sep = s.Contains(':') ? ':' : '-';
                var parts = s.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out var from) && int.TryParse(parts[1].Trim(), out var to))
                {
                    if (from > to) (from, to) = (to, from);
                    return q.Where(x => x.SourceId >= from && x.SourceId <= to);
                }
            }
            if (int.TryParse(s, out var exact))
                return q.Where(x => x.SourceId == exact);
            return q;
        }

        public static IQueryable<StockLedger> ApplySourceLineExpr(IQueryable<StockLedger> q, string? expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return q;
            var s = expr.Trim();
            if (s.StartsWith("<=") && s.Length > 2 && int.TryParse(s.AsSpan(2), out var max))
                return q.Where(x => x.SourceLine <= max);
            if (s.StartsWith(">=") && s.Length > 2 && int.TryParse(s.AsSpan(2), out var min))
                return q.Where(x => x.SourceLine >= min);
            if (s.StartsWith("<") && !s.StartsWith("<=") && s.Length > 1 && int.TryParse(s.AsSpan(1), out var max2))
                return q.Where(x => x.SourceLine < max2);
            if (s.StartsWith(">") && !s.StartsWith(">=") && s.Length > 1 && int.TryParse(s.AsSpan(1), out var min2))
                return q.Where(x => x.SourceLine > min2);
            if ((s.Contains(':') || (s.Contains('-') && !s.StartsWith("-"))) && !s.StartsWith("-"))
            {
                var sep = s.Contains(':') ? ':' : '-';
                var parts = s.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out var from) && int.TryParse(parts[1].Trim(), out var to))
                {
                    if (from > to) (from, to) = (to, from);
                    return q.Where(x => x.SourceLine >= from && x.SourceLine <= to);
                }
            }
            if (int.TryParse(s, out var exact))
                return q.Where(x => x.SourceLine == exact);
            return q;
        }

        public static IQueryable<StockLedger> ApplyQtyInExpr(IQueryable<StockLedger> q, string? expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return q;
            var s = expr.Trim();
            if (s.StartsWith("<=") && s.Length > 2 && int.TryParse(s.AsSpan(2), out var max))
                return q.Where(x => x.QtyIn <= max);
            if (s.StartsWith(">=") && s.Length > 2 && int.TryParse(s.AsSpan(2), out var min))
                return q.Where(x => x.QtyIn >= min);
            if (s.StartsWith("<") && !s.StartsWith("<=") && s.Length > 1 && int.TryParse(s.AsSpan(1), out var max2))
                return q.Where(x => x.QtyIn < max2);
            if (s.StartsWith(">") && !s.StartsWith(">=") && s.Length > 1 && int.TryParse(s.AsSpan(1), out var min2))
                return q.Where(x => x.QtyIn > min2);
            if ((s.Contains(':') || (s.Contains('-') && !s.StartsWith("-"))) && !s.StartsWith("-"))
            {
                var sep = s.Contains(':') ? ':' : '-';
                var parts = s.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out var from) && int.TryParse(parts[1].Trim(), out var to))
                {
                    if (from > to) (from, to) = (to, from);
                    return q.Where(x => x.QtyIn >= from && x.QtyIn <= to);
                }
            }
            if (int.TryParse(s, out var exact))
                return q.Where(x => x.QtyIn == exact);
            return q;
        }

        public static IQueryable<StockLedger> ApplyQtyOutExpr(IQueryable<StockLedger> q, string? expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return q;
            var s = expr.Trim();
            if (s.StartsWith("<=") && s.Length > 2 && int.TryParse(s.AsSpan(2), out var max))
                return q.Where(x => x.QtyOut <= max);
            if (s.StartsWith(">=") && s.Length > 2 && int.TryParse(s.AsSpan(2), out var min))
                return q.Where(x => x.QtyOut >= min);
            if (s.StartsWith("<") && !s.StartsWith("<=") && s.Length > 1 && int.TryParse(s.AsSpan(1), out var max2))
                return q.Where(x => x.QtyOut < max2);
            if (s.StartsWith(">") && !s.StartsWith(">=") && s.Length > 1 && int.TryParse(s.AsSpan(1), out var min2))
                return q.Where(x => x.QtyOut > min2);
            if ((s.Contains(':') || (s.Contains('-') && !s.StartsWith("-"))) && !s.StartsWith("-"))
            {
                var sep = s.Contains(':') ? ':' : '-';
                var parts = s.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out var from) && int.TryParse(parts[1].Trim(), out var to))
                {
                    if (from > to) (from, to) = (to, from);
                    return q.Where(x => x.QtyOut >= from && x.QtyOut <= to);
                }
            }
            if (int.TryParse(s, out var exact))
                return q.Where(x => x.QtyOut == exact);
            return q;
        }

        public static IQueryable<StockLedger> ApplyRemainingQtyExpr(IQueryable<StockLedger> q, string? expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return q;
            var s = expr.Trim();
            if (s.StartsWith("<=") && s.Length > 2 && int.TryParse(s.AsSpan(2), out var max))
                return q.Where(x => x.RemainingQty.HasValue && x.RemainingQty.Value <= max);
            if (s.StartsWith(">=") && s.Length > 2 && int.TryParse(s.AsSpan(2), out var min))
                return q.Where(x => x.RemainingQty.HasValue && x.RemainingQty.Value >= min);
            if (s.StartsWith("<") && !s.StartsWith("<=") && s.Length > 1 && int.TryParse(s.AsSpan(1), out var max2))
                return q.Where(x => x.RemainingQty.HasValue && x.RemainingQty.Value < max2);
            if (s.StartsWith(">") && !s.StartsWith(">=") && s.Length > 1 && int.TryParse(s.AsSpan(1), out var min2))
                return q.Where(x => x.RemainingQty.HasValue && x.RemainingQty.Value > min2);
            if ((s.Contains(':') || (s.Contains('-') && !s.StartsWith("-"))) && !s.StartsWith("-"))
            {
                var sep = s.Contains(':') ? ':' : '-';
                var parts = s.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out var from) && int.TryParse(parts[1].Trim(), out var to))
                {
                    if (from > to) (from, to) = (to, from);
                    return q.Where(x => x.RemainingQty.HasValue && x.RemainingQty.Value >= from && x.RemainingQty.Value <= to);
                }
            }
            if (int.TryParse(s, out var exact))
                return q.Where(x => x.RemainingQty.HasValue && x.RemainingQty.Value == exact);
            return q;
        }

        public static IQueryable<StockLedger> ApplyUnitCostExpr(IQueryable<StockLedger> q, string? expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return q;
            var s = expr.Trim();
            if (s.StartsWith("<=") && s.Length > 2 && decimal.TryParse(s.AsSpan(2), NumberStyles.Any, Inv, out var max))
                return q.Where(x => x.UnitCost <= max);
            if (s.StartsWith(">=") && s.Length > 2 && decimal.TryParse(s.AsSpan(2), NumberStyles.Any, Inv, out var min))
                return q.Where(x => x.UnitCost >= min);
            if (s.StartsWith("<") && !s.StartsWith("<=") && s.Length > 1 && decimal.TryParse(s.AsSpan(1), NumberStyles.Any, Inv, out var max2))
                return q.Where(x => x.UnitCost < max2);
            if (s.StartsWith(">") && !s.StartsWith(">=") && s.Length > 1 && decimal.TryParse(s.AsSpan(1), NumberStyles.Any, Inv, out var min2))
                return q.Where(x => x.UnitCost > min2);
            if ((s.Contains(':') || (s.Contains('-') && !s.StartsWith("-"))) && !s.StartsWith("-"))
            {
                var sep = s.Contains(':') ? ':' : '-';
                var parts = s.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 &&
                    decimal.TryParse(parts[0].Trim(), NumberStyles.Any, Inv, out var from) &&
                    decimal.TryParse(parts[1].Trim(), NumberStyles.Any, Inv, out var to))
                {
                    if (from > to) (from, to) = (to, from);
                    return q.Where(x => x.UnitCost >= from && x.UnitCost <= to);
                }
            }
            if (decimal.TryParse(s, NumberStyles.Any, Inv, out var exact))
                return q.Where(x => x.UnitCost == exact);
            return q;
        }

        public static IQueryable<StockLedger> ApplyPriceRetailExpr(IQueryable<StockLedger> q, string? expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return q;
            var s = expr.Trim();
            if (s.StartsWith("<=") && s.Length > 2 && decimal.TryParse(s.AsSpan(2), NumberStyles.Any, Inv, out var max))
                return q.Where(x => x.PriceRetailBatch.HasValue && x.PriceRetailBatch.Value <= max);
            if (s.StartsWith(">=") && s.Length > 2 && decimal.TryParse(s.AsSpan(2), NumberStyles.Any, Inv, out var min))
                return q.Where(x => x.PriceRetailBatch.HasValue && x.PriceRetailBatch.Value >= min);
            if (s.StartsWith("<") && !s.StartsWith("<=") && s.Length > 1 && decimal.TryParse(s.AsSpan(1), NumberStyles.Any, Inv, out var max2))
                return q.Where(x => x.PriceRetailBatch.HasValue && x.PriceRetailBatch.Value < max2);
            if (s.StartsWith(">") && !s.StartsWith(">=") && s.Length > 1 && decimal.TryParse(s.AsSpan(1), NumberStyles.Any, Inv, out var min2))
                return q.Where(x => x.PriceRetailBatch.HasValue && x.PriceRetailBatch.Value > min2);
            if ((s.Contains(':') || (s.Contains('-') && !s.StartsWith("-"))) && !s.StartsWith("-"))
            {
                var sep = s.Contains(':') ? ':' : '-';
                var parts = s.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 &&
                    decimal.TryParse(parts[0].Trim(), NumberStyles.Any, Inv, out var from) &&
                    decimal.TryParse(parts[1].Trim(), NumberStyles.Any, Inv, out var to))
                {
                    if (from > to) (from, to) = (to, from);
                    return q.Where(x => x.PriceRetailBatch.HasValue && x.PriceRetailBatch.Value >= from && x.PriceRetailBatch.Value <= to);
                }
            }
            if (decimal.TryParse(s, NumberStyles.Any, Inv, out var exact))
                return q.Where(x => x.PriceRetailBatch.HasValue && x.PriceRetailBatch.Value == exact);
            return q;
        }

        public static IQueryable<StockLedger> ApplyPurchaseDiscountExpr(IQueryable<StockLedger> q, string? expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return q;
            var s = expr.Trim();
            if (s.StartsWith("<=") && s.Length > 2 && decimal.TryParse(s.AsSpan(2), NumberStyles.Any, Inv, out var max))
                return q.Where(x => x.PurchaseDiscount.HasValue && x.PurchaseDiscount.Value <= max);
            if (s.StartsWith(">=") && s.Length > 2 && decimal.TryParse(s.AsSpan(2), NumberStyles.Any, Inv, out var min))
                return q.Where(x => x.PurchaseDiscount.HasValue && x.PurchaseDiscount.Value >= min);
            if (s.StartsWith("<") && !s.StartsWith("<=") && s.Length > 1 && decimal.TryParse(s.AsSpan(1), NumberStyles.Any, Inv, out var max2))
                return q.Where(x => x.PurchaseDiscount.HasValue && x.PurchaseDiscount.Value < max2);
            if (s.StartsWith(">") && !s.StartsWith(">=") && s.Length > 1 && decimal.TryParse(s.AsSpan(1), NumberStyles.Any, Inv, out var min2))
                return q.Where(x => x.PurchaseDiscount.HasValue && x.PurchaseDiscount.Value > min2);
            if ((s.Contains(':') || (s.Contains('-') && !s.StartsWith("-"))) && !s.StartsWith("-"))
            {
                var sep = s.Contains(':') ? ':' : '-';
                var parts = s.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 &&
                    decimal.TryParse(parts[0].Trim(), NumberStyles.Any, Inv, out var from) &&
                    decimal.TryParse(parts[1].Trim(), NumberStyles.Any, Inv, out var to))
                {
                    if (from > to) (from, to) = (to, from);
                    return q.Where(x => x.PurchaseDiscount.HasValue && x.PurchaseDiscount.Value >= from && x.PurchaseDiscount.Value <= to);
                }
            }
            if (decimal.TryParse(s, NumberStyles.Any, Inv, out var exact))
                return q.Where(x => x.PurchaseDiscount.HasValue && x.PurchaseDiscount.Value == exact);
            return q;
        }

        /// <summary>إجمالي التكلفة كما في الشاشة: TotalCost أو كمية×تكلفة.</summary>
        public static IQueryable<StockLedger> ApplyLineTotalCostExpr(IQueryable<StockLedger> q, string? expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return q;
            var s = expr.Trim();
            if (s.StartsWith("<=") && s.Length > 2 && decimal.TryParse(s.AsSpan(2), NumberStyles.Any, Inv, out var max))
                return q.Where(x => ((x.TotalCost ?? 0) > 0 ? x.TotalCost!.Value : (x.QtyIn * x.UnitCost)) <= max);
            if (s.StartsWith(">=") && s.Length > 2 && decimal.TryParse(s.AsSpan(2), NumberStyles.Any, Inv, out var min))
                return q.Where(x => ((x.TotalCost ?? 0) > 0 ? x.TotalCost!.Value : (x.QtyIn * x.UnitCost)) >= min);
            if (s.StartsWith("<") && !s.StartsWith("<=") && s.Length > 1 && decimal.TryParse(s.AsSpan(1), NumberStyles.Any, Inv, out var max2))
                return q.Where(x => ((x.TotalCost ?? 0) > 0 ? x.TotalCost!.Value : (x.QtyIn * x.UnitCost)) < max2);
            if (s.StartsWith(">") && !s.StartsWith(">=") && s.Length > 1 && decimal.TryParse(s.AsSpan(1), NumberStyles.Any, Inv, out var min2))
                return q.Where(x => ((x.TotalCost ?? 0) > 0 ? x.TotalCost!.Value : (x.QtyIn * x.UnitCost)) > min2);
            if ((s.Contains(':') || (s.Contains('-') && !s.StartsWith("-"))) && !s.StartsWith("-"))
            {
                var sep = s.Contains(':') ? ':' : '-';
                var parts = s.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 &&
                    decimal.TryParse(parts[0].Trim(), NumberStyles.Any, Inv, out var from) &&
                    decimal.TryParse(parts[1].Trim(), NumberStyles.Any, Inv, out var to))
                {
                    if (from > to) (from, to) = (to, from);
                    return q.Where(x =>
                        ((x.TotalCost ?? 0) > 0 ? x.TotalCost!.Value : (x.QtyIn * x.UnitCost)) >= from &&
                        ((x.TotalCost ?? 0) > 0 ? x.TotalCost!.Value : (x.QtyIn * x.UnitCost)) <= to);
                }
            }
            if (decimal.TryParse(s, NumberStyles.Any, Inv, out var exact))
                return q.Where(x => ((x.TotalCost ?? 0) > 0 ? x.TotalCost!.Value : (x.QtyIn * x.UnitCost)) == exact);
            return q;
        }
    }
}
