using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ERP.Filters;
using ERP.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Controllers
{
    public partial class ReportsController
    {
        /// <param name="omitTextColumnFilter">عند جلب قيم عمود للفلتر: تجاهل فلتر هذا العمود النصي حتى تظهر كل القيم المتاحة بعد بقية الفلاتر.</param>
        private async Task<List<ProductProfitReportDto>> BuildProductProfitsReportRowsAsync(
            string? mainSearch,
            int? categoryId,
            int? warehouseId,
            DateTime? fromDate,
            DateTime? toDate,
            bool includeZeroQty,
            string? filterCol_code,
            string? filterCol_name,
            string? filterCol_category,
            string? filterCol_salesrevenueExpr,
            string? filterCol_salescostExpr,
            string? filterCol_salesprofitExpr,
            string? filterCol_salesprofitpctExpr,
            string? filterCol_returnprofitExpr,
            string? filterCol_adjustmentprofitExpr,
            string? filterCol_transferprofitExpr,
            string? filterCol_netprofitExpr,
            string? filterCol_salesqtyExpr,
            string? sort,
            string? dir,
            string? omitTextColumnFilter)
        {
            var productsQuery = _context.Products
                .AsNoTracking()
                .Include(p => p.Category)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(mainSearch))
            {
                var s = mainSearch.Trim();
                productsQuery = productsQuery.Where(p =>
                    (p.ProdName != null && p.ProdName.Contains(s)) ||
                    (p.Barcode != null && p.Barcode.Contains(s)) ||
                    (p.ProdId.ToString() == s));
            }

            if (categoryId.HasValue && categoryId.Value > 0)
                productsQuery = productsQuery.Where(p => p.CategoryId == categoryId.Value);

            productsQuery = productsQuery.Where(p => p.IsActive == true);

            var productIds = await productsQuery.Select(p => p.ProdId).ToListAsync();
            if (productIds.Count == 0)
                return new List<ProductProfitReportDto>();

            var salesProfitQuery = _context.SalesInvoiceLines
                .AsNoTracking()
                .Include(sil => sil.SalesInvoice)
                .Where(sil =>
                    productIds.Contains(sil.ProdId) &&
                    sil.SalesInvoice.IsPosted);

            if (warehouseId.HasValue && warehouseId.Value > 0)
                salesProfitQuery = salesProfitQuery.Where(sil => sil.SalesInvoice.WarehouseId == warehouseId.Value);

            if (fromDate.HasValue)
            {
                var from = fromDate.Value.Date;
                salesProfitQuery = salesProfitQuery.Where(sil => sil.SalesInvoice.SIDate >= from);
            }

            if (toDate.HasValue)
            {
                var to = toDate.Value.Date.AddDays(1);
                salesProfitQuery = salesProfitQuery.Where(sil => sil.SalesInvoice.SIDate < to);
            }

            var salesProfitData = await salesProfitQuery
                .GroupBy(sil => sil.ProdId)
                .Select(g => new
                {
                    ProdId = g.Key,
                    SalesRevenue = g.Sum(sil => sil.LineNetTotal),
                    SalesQty = g.Sum(sil => (decimal?)sil.Qty) ?? 0m,
                    LineCostTotal = g.Sum(sil => sil.CostTotal)
                })
                .ToDictionaryAsync(x => x.ProdId);

            var siIdsInRange = await salesProfitQuery.Select(sil => sil.SIId).Distinct().ToListAsync();
            var salesCostFromLedger = new Dictionary<int, decimal>();
            if (siIdsInRange.Any())
            {
                var ledgerCostQuery = _context.StockLedger
                    .AsNoTracking()
                    .Where(sl => sl.SourceType == "Sales" && sl.QtyOut > 0 && siIdsInRange.Contains(sl.SourceId) && productIds.Contains(sl.ProdId));
                if (warehouseId.HasValue && warehouseId.Value > 0)
                    ledgerCostQuery = ledgerCostQuery.Where(sl => sl.WarehouseId == warehouseId.Value);
                salesCostFromLedger = await ledgerCostQuery
                    .GroupBy(sl => sl.ProdId)
                    .Select(g => new { ProdId = g.Key, SalesCost = g.Sum(sl => sl.TotalCost ?? (sl.UnitCost * sl.QtyOut)) })
                    .ToDictionaryAsync(x => x.ProdId, x => x.SalesCost);
            }

            Dictionary<int, decimal> salesCostFromFifoRecalc = new Dictionary<int, decimal>();
            if (siIdsInRange.Any())
            {
                var fifoRows = await (
                    from m in _context.StockFifoMap.AsNoTracking()
                    join slOut in _context.StockLedger.AsNoTracking() on m.OutEntryId equals slOut.EntryId
                    join slIn in _context.StockLedger.AsNoTracking() on m.InEntryId equals slIn.EntryId
                    where slOut.SourceType == "Sales" && slOut.QtyOut > 0 && siIdsInRange.Contains(slOut.SourceId) && productIds.Contains(slOut.ProdId)
                    select new { slOut.ProdId, slOut.WarehouseId, m.Qty, slIn.UnitCost, slIn.TotalCost, slIn.QtyIn }
                ).ToListAsync();
                var q = fifoRows.AsEnumerable();
                if (warehouseId.HasValue && warehouseId.Value > 0)
                    q = q.Where(x => x.WarehouseId == warehouseId.Value);
                salesCostFromFifoRecalc = q
                    .GroupBy(x => x.ProdId)
                    .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty * EffectiveStockInflowUnitCost(x.UnitCost, x.QtyIn, x.TotalCost)));
            }

            var salesReturnProfitQuery = _context.SalesReturnLines
                .AsNoTracking()
                .Include(srl => srl.SalesReturn)
                .Where(srl => productIds.Contains(srl.ProdId) && srl.SalesReturn != null && srl.SalesReturn.IsPosted);

            if (warehouseId.HasValue && warehouseId.Value > 0)
                salesReturnProfitQuery = salesReturnProfitQuery.Where(srl => srl.SalesReturn!.WarehouseId == warehouseId.Value);
            if (fromDate.HasValue)
                salesReturnProfitQuery = salesReturnProfitQuery.Where(srl => srl.SalesReturn!.SRDate >= fromDate.Value.Date);
            if (toDate.HasValue)
                salesReturnProfitQuery = salesReturnProfitQuery.Where(srl => srl.SalesReturn!.SRDate < toDate.Value.Date.AddDays(1));

            var returnRevenueQty = await salesReturnProfitQuery
                .GroupBy(srl => srl.ProdId)
                .Select(g => new { ProdId = g.Key, ReturnRevenue = g.Sum(srl => srl.LineNetTotal), ReturnQty = g.Sum(srl => (decimal?)srl.Qty) ?? 0m })
                .ToDictionaryAsync(x => x.ProdId);

            var srIdsInRange = await salesReturnProfitQuery
                .Select(srl => srl.SalesReturn!.SRId)
                .Distinct()
                .ToListAsync();
            Dictionary<int, decimal> returnCostData = new Dictionary<int, decimal>();
            if (srIdsInRange.Any())
            {
                returnCostData = await _context.StockLedger
                    .AsNoTracking()
                    .Where(sl => sl.SourceType == "SalesReturn" && srIdsInRange.Contains(sl.SourceId) && productIds.Contains(sl.ProdId))
                    .GroupBy(sl => sl.ProdId)
                    .Select(g => new { ProdId = g.Key, ReturnCost = g.Sum(sl => sl.UnitCost * sl.QtyIn) })
                    .ToDictionaryAsync(x => x.ProdId, x => x.ReturnCost);
            }

            var adjustmentProfitQuery = from sal in _context.StockAdjustmentLines.AsNoTracking()
                                        join sa in _context.StockAdjustments.AsNoTracking() on sal.StockAdjustmentId equals sa.Id
                                        where sa.IsPosted &&
                                              productIds.Contains(sal.ProductId) &&
                                              sal.CostDiff.HasValue && sal.CostDiff.Value != 0
                                        select new { sal.ProductId, sal.CostDiff, sa.WarehouseId, sa.AdjustmentDate };

            if (warehouseId.HasValue && warehouseId.Value > 0)
                adjustmentProfitQuery = adjustmentProfitQuery.Where(x => x.WarehouseId == warehouseId.Value);

            if (fromDate.HasValue)
            {
                var from = fromDate.Value.Date;
                adjustmentProfitQuery = adjustmentProfitQuery.Where(x => x.AdjustmentDate >= from);
            }

            if (toDate.HasValue)
            {
                var to = toDate.Value.Date.AddDays(1);
                adjustmentProfitQuery = adjustmentProfitQuery.Where(x => x.AdjustmentDate < to);
            }

            var adjustmentProfitData = await adjustmentProfitQuery
                .GroupBy(x => x.ProductId)
                .Select(g => new { ProdId = g.Key, AdjustmentProfit = g.Sum(x => x.CostDiff!.Value) })
                .ToDictionaryAsync(x => x.ProdId, x => x.AdjustmentProfit);

            var transferProfitQuery = from stl in _context.StockTransferLines.AsNoTracking()
                                      join st in _context.StockTransfers.AsNoTracking() on stl.StockTransferId equals st.Id
                                      where st.IsPosted &&
                                            productIds.Contains(stl.ProductId) &&
                                            stl.PriceRetail.HasValue && stl.PriceRetail.Value > 0 &&
                                            stl.DiscountPct.HasValue && stl.UnitCost > 0
                                      select new
                                      {
                                          stl.ProductId,
                                          st.FromWarehouseId,
                                          st.TransferDate,
                                          LineProfit = (stl.PriceRetail!.Value * (1m - stl.DiscountPct!.Value / 100m) - stl.UnitCost) * stl.Qty
                                      };

            transferProfitQuery = transferProfitQuery.Where(x => x.LineProfit > 0);

            if (warehouseId.HasValue && warehouseId.Value > 0)
                transferProfitQuery = transferProfitQuery.Where(x => x.FromWarehouseId == warehouseId.Value);

            if (fromDate.HasValue)
            {
                var from = fromDate.Value.Date;
                transferProfitQuery = transferProfitQuery.Where(x => x.TransferDate >= from);
            }

            if (toDate.HasValue)
            {
                var to = toDate.Value.Date.AddDays(1);
                transferProfitQuery = transferProfitQuery.Where(x => x.TransferDate < to);
            }

            var transferProfitData = await transferProfitQuery
                .GroupBy(x => x.ProductId)
                .Select(g => new { ProdId = g.Key, TransferProfit = g.Sum(x => x.LineProfit) })
                .ToDictionaryAsync(x => x.ProdId, x => x.TransferProfit);

            var productsDict = await productsQuery
                .Select(p => new
                {
                    p.ProdId,
                    p.ProdName,
                    CategoryName = p.Category != null ? p.Category.CategoryName : ""
                })
                .ToDictionaryAsync(p => p.ProdId);

            var reportData = new List<ProductProfitReportDto>();

            foreach (var prodId in productIds)
            {
                if (!productsDict.TryGetValue(prodId, out var product)) continue;

                salesProfitData.TryGetValue(prodId, out var salesRow);
                decimal salesRevenue = salesRow?.SalesRevenue ?? 0m;
                decimal ledgerSalesCost = salesCostFromLedger.TryGetValue(prodId, out var costVal) ? costVal : 0m;
                decimal fifoRecalcCost = salesCostFromFifoRecalc.TryGetValue(prodId, out var fifoC) ? fifoC : 0m;
                decimal lineCostFallback = salesRow?.LineCostTotal ?? 0m;
                decimal salesCost = ledgerSalesCost > 0m ? ledgerSalesCost : (fifoRecalcCost > 0m ? fifoRecalcCost : lineCostFallback);
                decimal salesQtyGross = salesRow?.SalesQty ?? 0m;
                decimal salesProfit = salesRevenue - salesCost;
                decimal salesProfitPercent = salesRevenue != 0 ? (salesProfit / salesRevenue) * 100m : 0m;

                decimal returnRevenue = returnRevenueQty.TryGetValue(prodId, out var ret) ? ret.ReturnRevenue : 0m;
                decimal returnQty = returnRevenueQty.TryGetValue(prodId, out var retQty) ? retQty.ReturnQty : 0m;
                decimal returnCost = returnCostData.TryGetValue(prodId, out var retCost) ? retCost : 0m;
                decimal returnProfit = returnRevenue - returnCost;

                decimal salesQty = salesQtyGross - returnQty;

                decimal adjustmentProfit = adjustmentProfitData.TryGetValue(prodId, out var adjProfit) ? adjProfit : 0m;

                decimal transferProfit = transferProfitData.TryGetValue(prodId, out var trfProfit) ? trfProfit : 0m;

                decimal ledgerRevenue = 0m;
                decimal ledgerCost = 0m;
                decimal ledgerProfit = 0m;
                decimal ledgerProfitPercent = 0m;
                decimal accountBalanceRevenue = 0m;
                decimal accountBalanceCost = 0m;
                decimal accountBalanceProfit = 0m;
                decimal accountBalanceProfitPercent = 0m;

                decimal netProfit = (salesProfit - returnProfit) + adjustmentProfit + transferProfit;

                if (!includeZeroQty && salesRevenue == 0m && returnRevenue == 0m && adjustmentProfit == 0m && transferProfit == 0m)
                    continue;

                reportData.Add(new ProductProfitReportDto
                {
                    ProdId = prodId,
                    ProdCode = prodId.ToString(),
                    ProdName = product.ProdName ?? "",
                    CategoryName = product.CategoryName ?? "",
                    SalesRevenue = salesRevenue,
                    SalesCost = salesCost,
                    SalesProfit = salesProfit,
                    SalesProfitPercent = salesProfitPercent,
                    ReturnProfit = returnProfit,
                    LedgerRevenue = ledgerRevenue,
                    LedgerCost = ledgerCost,
                    LedgerProfit = ledgerProfit,
                    LedgerProfitPercent = ledgerProfitPercent,
                    AccountBalanceRevenue = accountBalanceRevenue,
                    AccountBalanceCost = accountBalanceCost,
                    AccountBalanceProfit = accountBalanceProfit,
                    AccountBalanceProfitPercent = accountBalanceProfitPercent,
                    AdjustmentProfit = adjustmentProfit,
                    TransferProfit = transferProfit,
                    NetProfit = netProfit,
                    SalesQty = salesQty
                });
            }

            var omit = (omitTextColumnFilter ?? "").Trim().ToLowerInvariant();

            if (omit != "code" && !string.IsNullOrWhiteSpace(filterCol_code))
            {
                var codeFilter = filterCol_code.Trim();
                if (codeFilter.Contains('|'))
                {
                    var parts = codeFilter.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    reportData = reportData
                        .Where(r => parts.Contains(r.ProdCode ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                        .ToList();
                }
                else
                {
                    reportData = reportData.Where(r => (r.ProdCode ?? "").Contains(codeFilter, StringComparison.OrdinalIgnoreCase)).ToList();
                }
            }
            if (omit != "name" && !string.IsNullOrWhiteSpace(filterCol_name))
            {
                var nameFilter = filterCol_name.Trim();
                if (nameFilter.Contains('|'))
                {
                    var parts = nameFilter.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    reportData = reportData
                        .Where(r => parts.Contains(r.ProdName ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                        .ToList();
                }
                else
                {
                    reportData = reportData.Where(r => (r.ProdName ?? "").Contains(nameFilter, StringComparison.OrdinalIgnoreCase)).ToList();
                }
            }
            if (omit != "category" && !string.IsNullOrWhiteSpace(filterCol_category))
            {
                var catFilter = filterCol_category.Trim();
                if (catFilter.Contains('|'))
                {
                    var parts = catFilter.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    reportData = reportData
                        .Where(r => parts.Contains(r.CategoryName ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                        .ToList();
                }
                else
                {
                    reportData = reportData.Where(r => (r.CategoryName ?? "").Contains(catFilter, StringComparison.OrdinalIgnoreCase)).ToList();
                }
            }

            var inv = CultureInfo.InvariantCulture;
            bool ApplyDecimalExpr(string? expr, Func<ProductProfitReportDto, decimal> selector)
            {
                if (string.IsNullOrWhiteSpace(expr)) return false;
                var e = expr.Trim();
                if (e.StartsWith("<=") && e.Length > 2 && decimal.TryParse(e.Substring(2), NumberStyles.Any, inv, out var max))
                {
                    reportData = reportData.Where(r => selector(r) <= max).ToList();
                    return true;
                }
                if (e.StartsWith(">=") && e.Length > 2 && decimal.TryParse(e.Substring(2), NumberStyles.Any, inv, out var min))
                {
                    reportData = reportData.Where(r => selector(r) >= min).ToList();
                    return true;
                }
                if (e.StartsWith("<") && !e.StartsWith("<=") && e.Length > 1 && decimal.TryParse(e.Substring(1), NumberStyles.Any, inv, out var max2))
                {
                    reportData = reportData.Where(r => selector(r) < max2).ToList();
                    return true;
                }
                if (e.StartsWith(">") && !e.StartsWith(">=") && e.Length > 1 && decimal.TryParse(e.Substring(1), NumberStyles.Any, inv, out var min2))
                {
                    reportData = reportData.Where(r => selector(r) > min2).ToList();
                    return true;
                }
                if ((e.Contains(':') || e.Contains('-')) && !e.StartsWith("-"))
                {
                    var sep = e.Contains(':') ? ':' : '-';
                    var parts = e.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2 &&
                        decimal.TryParse(parts[0].Trim(), NumberStyles.Any, inv, out var fromR) &&
                        decimal.TryParse(parts[1].Trim(), NumberStyles.Any, inv, out var toR))
                    {
                        if (fromR > toR) (fromR, toR) = (toR, fromR);
                        reportData = reportData.Where(r => selector(r) >= fromR && selector(r) <= toR).ToList();
                        return true;
                    }
                }
                if (decimal.TryParse(e, NumberStyles.Any, inv, out var exact))
                {
                    reportData = reportData.Where(r => selector(r) == exact).ToList();
                    return true;
                }
                return false;
            }
            ApplyDecimalExpr(filterCol_salesrevenueExpr, r => r.SalesRevenue);
            ApplyDecimalExpr(filterCol_salescostExpr, r => r.SalesCost);
            ApplyDecimalExpr(filterCol_salesprofitExpr, r => r.SalesProfit);
            ApplyDecimalExpr(filterCol_salesprofitpctExpr, r => r.SalesProfitPercent);
            ApplyDecimalExpr(filterCol_returnprofitExpr, r => r.ReturnProfit);
            ApplyDecimalExpr(filterCol_adjustmentprofitExpr, r => r.AdjustmentProfit);
            ApplyDecimalExpr(filterCol_transferprofitExpr, r => r.TransferProfit);
            ApplyDecimalExpr(filterCol_netprofitExpr, r => r.NetProfit);
            ApplyDecimalExpr(filterCol_salesqtyExpr, r => r.SalesQty);

            var sortKey = sort ?? "name";
            var sortDir = dir ?? "asc";
            bool isDesc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
            switch (sortKey.ToLowerInvariant())
            {
                case "code":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.ProdCode).ToList()
                        : reportData.OrderBy(r => r.ProdCode).ToList();
                    break;
                case "salesprofit":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.SalesProfit).ToList()
                        : reportData.OrderBy(r => r.SalesProfit).ToList();
                    break;
                case "adjustmentprofit":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.AdjustmentProfit).ToList()
                        : reportData.OrderBy(r => r.AdjustmentProfit).ToList();
                    break;
                case "transferprofit":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.TransferProfit).ToList()
                        : reportData.OrderBy(r => r.TransferProfit).ToList();
                    break;
                case "ledgerprofit":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.LedgerProfit).ToList()
                        : reportData.OrderBy(r => r.LedgerProfit).ToList();
                    break;
                case "salesrevenue":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.SalesRevenue).ToList()
                        : reportData.OrderBy(r => r.SalesRevenue).ToList();
                    break;
                case "salescost":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.SalesCost).ToList()
                        : reportData.OrderBy(r => r.SalesCost).ToList();
                    break;
                case "salesprofitpct":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.SalesProfitPercent).ToList()
                        : reportData.OrderBy(r => r.SalesProfitPercent).ToList();
                    break;
                case "salesqty":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.SalesQty).ToList()
                        : reportData.OrderBy(r => r.SalesQty).ToList();
                    break;
                case "returnprofit":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.ReturnProfit).ToList()
                        : reportData.OrderBy(r => r.ReturnProfit).ToList();
                    break;
                case "netprofit":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.NetProfit).ToList()
                        : reportData.OrderBy(r => r.NetProfit).ToList();
                    break;
                case "category":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.CategoryName).ToList()
                        : reportData.OrderBy(r => r.CategoryName).ToList();
                    break;
                default:
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.ProdName).ToList()
                        : reportData.OrderBy(r => r.ProdName).ToList();
                    break;
            }

            return reportData;
        }

        /// <summary>قيم مميزة لأعمدة نصية من كامل النتائج المفلترة (للفلترة الموحّدة).</summary>
        [HttpGet]
        [RequirePermission("Reports.ProductProfits")]
        public async Task<IActionResult> GetProductProfitsColumnValues(
            string column,
            string? search,
            string? mainSearch,
            string? sort,
            string? dir,
            int? categoryId,
            int? warehouseId,
            DateTime? fromDate,
            DateTime? toDate,
            bool includeZeroQty = false,
            string? filterCol_code = null,
            string? filterCol_name = null,
            string? filterCol_category = null,
            string? filterCol_salesrevenueExpr = null,
            string? filterCol_salescostExpr = null,
            string? filterCol_salesprofitExpr = null,
            string? filterCol_salesprofitpctExpr = null,
            string? filterCol_returnprofitExpr = null,
            string? filterCol_adjustmentprofitExpr = null,
            string? filterCol_transferprofitExpr = null,
            string? filterCol_netprofitExpr = null,
            string? filterCol_salesqtyExpr = null)
        {
            var col = (column ?? "").Trim().ToLowerInvariant();
            if (col != "code" && col != "name" && col != "category")
                return Json(Array.Empty<object>());

            var rows = await BuildProductProfitsReportRowsAsync(
                mainSearch,
                categoryId,
                warehouseId,
                fromDate,
                toDate,
                includeZeroQty,
                filterCol_code,
                filterCol_name,
                filterCol_category,
                filterCol_salesrevenueExpr,
                filterCol_salescostExpr,
                filterCol_salesprofitExpr,
                filterCol_salesprofitpctExpr,
                filterCol_returnprofitExpr,
                filterCol_adjustmentprofitExpr,
                filterCol_transferprofitExpr,
                filterCol_netprofitExpr,
                filterCol_salesqtyExpr,
                sort,
                dir,
                omitTextColumnFilter: col);

            var term = (search ?? "").Trim();
            if (col == "code")
            {
                var list = rows.Select(r => r.ProdCode ?? "").Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(2000).ToList();
                if (!string.IsNullOrEmpty(term))
                {
                    var t = term.ToLowerInvariant();
                    list = list.Where(x => (x ?? "").ToLowerInvariant().Contains(t)).ToList();
                }
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (col == "name")
            {
                var list = rows.Select(r => r.ProdName ?? "").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(2000).ToList();
                if (!string.IsNullOrEmpty(term))
                {
                    var t = term.ToLowerInvariant();
                    list = list.Where(x => (x ?? "").ToLowerInvariant().Contains(t)).ToList();
                }
                return Json(list.Select(v => new { value = v, display = v }));
            }
            var catList = rows.Select(r => r.CategoryName ?? "").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(2000).ToList();
            if (!string.IsNullOrEmpty(term))
            {
                var t = term.ToLowerInvariant();
                catList = catList.Where(x => (x ?? "").ToLowerInvariant().Contains(t)).ToList();
            }
            return Json(catList.Select(v => new { value = v, display = string.IsNullOrEmpty(v) ? "—" : v }));
        }
    }
}
