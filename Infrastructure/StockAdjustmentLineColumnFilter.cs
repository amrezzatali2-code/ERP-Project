using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ERP.Models;

namespace ERP.Infrastructure
{
    /// <summary>
    /// فلاتر أعمدة سطور التسوية: قائمة قيم | أو بحث رقمي filterCol_*Expr (نمط قائمة الأصناف).
    /// </summary>
    public static class StockAdjustmentLineColumnFilter
    {
        private static readonly char[] _sep = new[] { '|', ',', ';' };

        private static IQueryable<StockAdjustmentLine> ApplyIntExpr(IQueryable<StockAdjustmentLine> q, string? raw, string column)
        {
            if (string.IsNullOrWhiteSpace(raw)) return q;
            var expr = raw.Trim();
            var c = column.ToLowerInvariant();

            if (c == "id")
            {
                if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                    return q.Where(l => l.Id <= smax);
                if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                    return q.Where(l => l.Id >= smin);
                if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                    return q.Where(l => l.Id < smax2);
                if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
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

            if (c == "stock")
            {
                if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                    return q.Where(l => l.StockAdjustmentId <= smax);
                if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                    return q.Where(l => l.StockAdjustmentId >= smin);
                if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                    return q.Where(l => l.StockAdjustmentId < smax2);
                if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                    return q.Where(l => l.StockAdjustmentId > smin2);
                if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
                {
                    var separator = expr.Contains(':') ? ':' : '-';
                    var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2
                        && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                        && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                    {
                        if (a > b) (a, b) = (b, a);
                        return q.Where(l => l.StockAdjustmentId >= a && l.StockAdjustmentId <= b);
                    }
                }
                if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                    return q.Where(l => l.StockAdjustmentId == ex);
                return q;
            }

            if (c == "product")
            {
                if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                    return q.Where(l => l.ProductId <= smax);
                if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                    return q.Where(l => l.ProductId >= smin);
                if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                    return q.Where(l => l.ProductId < smax2);
                if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
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

            if (c == "batch")
            {
                if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                    return q.Where(l => l.BatchId.HasValue && l.BatchId.Value <= smax);
                if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                    return q.Where(l => l.BatchId.HasValue && l.BatchId.Value >= smin);
                if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                    return q.Where(l => l.BatchId.HasValue && l.BatchId.Value < smax2);
                if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                    return q.Where(l => l.BatchId.HasValue && l.BatchId.Value > smin2);
                if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
                {
                    var separator = expr.Contains(':') ? ':' : '-';
                    var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2
                        && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                        && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                    {
                        if (a > b) (a, b) = (b, a);
                        return q.Where(l => l.BatchId.HasValue && l.BatchId.Value >= a && l.BatchId.Value <= b);
                    }
                }
                if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                    return q.Where(l => l.BatchId == ex);
                return q;
            }

            if (c == "qtybefore")
            {
                if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                    return q.Where(l => l.QtyBefore <= smax);
                if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                    return q.Where(l => l.QtyBefore >= smin);
                if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                    return q.Where(l => l.QtyBefore < smax2);
                if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                    return q.Where(l => l.QtyBefore > smin2);
                if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
                {
                    var separator = expr.Contains(':') ? ':' : '-';
                    var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2
                        && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                        && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                    {
                        if (a > b) (a, b) = (b, a);
                        return q.Where(l => l.QtyBefore >= a && l.QtyBefore <= b);
                    }
                }
                if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                    return q.Where(l => l.QtyBefore == ex);
                return q;
            }

            if (c == "qtyafter")
            {
                if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                    return q.Where(l => l.QtyAfter <= smax);
                if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                    return q.Where(l => l.QtyAfter >= smin);
                if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                    return q.Where(l => l.QtyAfter < smax2);
                if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                    return q.Where(l => l.QtyAfter > smin2);
                if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
                {
                    var separator = expr.Contains(':') ? ':' : '-';
                    var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2
                        && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                        && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                    {
                        if (a > b) (a, b) = (b, a);
                        return q.Where(l => l.QtyAfter >= a && l.QtyAfter <= b);
                    }
                }
                if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                    return q.Where(l => l.QtyAfter == ex);
                return q;
            }

            if (c == "qtydiff")
            {
                if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                    return q.Where(l => l.QtyDiff <= smax);
                if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                    return q.Where(l => l.QtyDiff >= smin);
                if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                    return q.Where(l => l.QtyDiff < smax2);
                if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                    return q.Where(l => l.QtyDiff > smin2);
                if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
                {
                    var separator = expr.Contains(':') ? ':' : '-';
                    var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2
                        && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                        && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                    {
                        if (a > b) (a, b) = (b, a);
                        return q.Where(l => l.QtyDiff >= a && l.QtyDiff <= b);
                    }
                }
                if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                    return q.Where(l => l.QtyDiff == ex);
                return q;
            }

            return q;
        }

        private static IQueryable<StockAdjustmentLine> ApplyDecimalExpr(IQueryable<StockAdjustmentLine> q, string? raw, string column)
        {
            if (string.IsNullOrWhiteSpace(raw)) return q;
            var expr = raw.Trim();
            var c = column.ToLowerInvariant();

            if (c == "costunit")
            {
                if (expr.StartsWith("<=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                    return q.Where(l => l.CostPerUnit.HasValue && l.CostPerUnit.Value <= smax);
                if (expr.StartsWith(">=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                    return q.Where(l => l.CostPerUnit.HasValue && l.CostPerUnit.Value >= smin);
                if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                    return q.Where(l => l.CostPerUnit.HasValue && l.CostPerUnit.Value < smax2);
                if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                    return q.Where(l => l.CostPerUnit.HasValue && l.CostPerUnit.Value > smin2);
                if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
                {
                    var separator = expr.Contains(':') ? ':' : '-';
                    var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2
                        && decimal.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                        && decimal.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                    {
                        if (a > b) (a, b) = (b, a);
                        return q.Where(l => l.CostPerUnit.HasValue && l.CostPerUnit.Value >= a && l.CostPerUnit.Value <= b);
                    }
                }
                if (decimal.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                    return q.Where(l => l.CostPerUnit.HasValue && l.CostPerUnit.Value == ex);
                return q;
            }

            if (c == "costdiff")
            {
                if (expr.StartsWith("<=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                    return q.Where(l => l.CostDiff.HasValue && l.CostDiff.Value <= smax);
                if (expr.StartsWith(">=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                    return q.Where(l => l.CostDiff.HasValue && l.CostDiff.Value >= smin);
                if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                    return q.Where(l => l.CostDiff.HasValue && l.CostDiff.Value < smax2);
                if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                    return q.Where(l => l.CostDiff.HasValue && l.CostDiff.Value > smin2);
                if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
                {
                    var separator = expr.Contains(':') ? ':' : '-';
                    var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2
                        && decimal.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                        && decimal.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                    {
                        if (a > b) (a, b) = (b, a);
                        return q.Where(l => l.CostDiff.HasValue && l.CostDiff.Value >= a && l.CostDiff.Value <= b);
                    }
                }
                if (decimal.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                    return q.Where(l => l.CostDiff.HasValue && l.CostDiff.Value == ex);
                return q;
            }

            return q;
        }

        public static IQueryable<StockAdjustmentLine> ApplyColumnFilters(
            IQueryable<StockAdjustmentLine> qBase,
            string? filterCol_id, string? filterCol_idExpr,
            string? filterCol_stock, string? filterCol_stockExpr,
            string? filterCol_product, string? filterCol_productExpr,
            string? filterCol_batch, string? filterCol_batchExpr,
            string? filterCol_qtyBefore, string? filterCol_qtyBeforeExpr,
            string? filterCol_qtyAfter, string? filterCol_qtyAfterExpr,
            string? filterCol_qtyDiff, string? filterCol_qtyDiffExpr,
            string? filterCol_costUnit, string? filterCol_costUnitExpr,
            string? filterCol_costDiff, string? filterCol_costDiffExpr,
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

            if (!string.IsNullOrWhiteSpace(filterCol_stockExpr))
                qBase = ApplyIntExpr(qBase, filterCol_stockExpr, "stock");
            else if (!string.IsNullOrWhiteSpace(filterCol_stock))
            {
                var ids = filterCol_stock.Split(_sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(l => ids.Contains(l.StockAdjustmentId));
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

            if (!string.IsNullOrWhiteSpace(filterCol_batchExpr))
                qBase = ApplyIntExpr(qBase, filterCol_batchExpr, "batch");
            else if (!string.IsNullOrWhiteSpace(filterCol_batch))
            {
                var ids = filterCol_batch.Split(_sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(l => l.BatchId.HasValue && ids.Contains(l.BatchId.Value));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_qtyBeforeExpr))
                qBase = ApplyIntExpr(qBase, filterCol_qtyBeforeExpr, "qtybefore");
            else if (!string.IsNullOrWhiteSpace(filterCol_qtyBefore))
            {
                var ids = filterCol_qtyBefore.Split(_sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(l => ids.Contains(l.QtyBefore));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_qtyAfterExpr))
                qBase = ApplyIntExpr(qBase, filterCol_qtyAfterExpr, "qtyafter");
            else if (!string.IsNullOrWhiteSpace(filterCol_qtyAfter))
            {
                var ids = filterCol_qtyAfter.Split(_sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(l => ids.Contains(l.QtyAfter));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_qtyDiffExpr))
                qBase = ApplyIntExpr(qBase, filterCol_qtyDiffExpr, "qtydiff");
            else if (!string.IsNullOrWhiteSpace(filterCol_qtyDiff))
            {
                var ids = filterCol_qtyDiff.Split(_sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(l => ids.Contains(l.QtyDiff));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_costUnitExpr))
                qBase = ApplyDecimalExpr(qBase, filterCol_costUnitExpr, "costunit");
            else if (!string.IsNullOrWhiteSpace(filterCol_costUnit))
            {
                var vals = filterCol_costUnit.Split(_sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0)
                    qBase = qBase.Where(l => l.CostPerUnit.HasValue && vals.Contains(l.CostPerUnit.Value));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_costDiffExpr))
                qBase = ApplyDecimalExpr(qBase, filterCol_costDiffExpr, "costdiff");
            else if (!string.IsNullOrWhiteSpace(filterCol_costDiff))
            {
                var vals = filterCol_costDiff.Split(_sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0)
                    qBase = qBase.Where(l => l.CostDiff.HasValue && vals.Contains(l.CostDiff.Value));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_note))
            {
                var vals = filterCol_note.Split(_sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    qBase = qBase.Where(l => l.Note != null && vals.Contains(l.Note));
            }

            return qBase;
        }
    }
}
