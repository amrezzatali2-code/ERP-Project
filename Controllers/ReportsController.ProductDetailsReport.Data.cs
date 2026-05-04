using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ERP.Filters;
using ERP.Infrastructure;
using ERP.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Controllers
{
    public partial class ReportsController
    {
        private static readonly string[] ProductDetailsReportPrintColumnOrder =
        {
            "date", "time", "docno", "author", "region", "docnamear", "productcode", "productname",
            "qty", "unitprice", "discount", "linetotal", "customercode", "party", "warehouse", "batch", "expiry", "notes"
        };

        private static string GetPdrReportTypeDisplayName(string? reportType) => reportType switch
        {
            "All" => "كل التفاصيل (تجميعة كاملة)",
            "Sales" => "مبيعات الصنف بالتفصيل",
            "Purchases" => "مشتريات الصنف",
            "SalesReturns" => "مرتجعات البيع",
            "PurchaseReturns" => "مرتجعات الشراء",
            "Adjustments" => "تسويات",
            "Transfers" => "تحويلات",
            "PurchaseRequests" => "طلبات شراء",
            "SalesOrders" => "أوامر بيع",
            _ => reportType ?? ""
        };

        private static IOrderedEnumerable<ProductDetailsReportRow> ApplyPdrInMemorySort(IEnumerable<ProductDetailsReportRow> rows, string? sort, string? dir)
        {
            var sortKey = (sort ?? "Date").Trim().ToLowerInvariant();
            var asc = string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase);

            return sortKey switch
            {
                "docno" => asc ? rows.OrderBy(r => r.DocNo) : rows.OrderByDescending(r => r.DocNo),
                "author" => asc ? rows.OrderBy(r => r.Author ?? "") : rows.OrderByDescending(r => r.Author ?? ""),
                "region" => asc ? rows.OrderBy(r => r.Region ?? "") : rows.OrderByDescending(r => r.Region ?? ""),
                "documentnamear" => asc ? rows.OrderBy(r => r.DocumentNameAr ?? "") : rows.OrderByDescending(r => r.DocumentNameAr ?? ""),
                "productcode" => asc ? rows.OrderBy(r => r.ProductCode) : rows.OrderByDescending(r => r.ProductCode),
                "productname" => asc ? rows.OrderBy(r => r.ProductName) : rows.OrderByDescending(r => r.ProductName),
                "partyname" => asc ? rows.OrderBy(r => r.PartyName ?? "") : rows.OrderByDescending(r => r.PartyName ?? ""),
                "warehousename" => asc ? rows.OrderBy(r => r.WarehouseName ?? "") : rows.OrderByDescending(r => r.WarehouseName ?? ""),
                "qty" => asc ? rows.OrderBy(r => r.Qty) : rows.OrderByDescending(r => r.Qty),
                "unitprice" => asc ? rows.OrderBy(r => r.UnitPrice ?? 0m) : rows.OrderByDescending(r => r.UnitPrice ?? 0m),
                "linetotal" => asc ? rows.OrderBy(r => r.Total ?? 0m) : rows.OrderByDescending(r => r.Total ?? 0m),
                "batchno" => asc ? rows.OrderBy(r => r.BatchNo ?? "") : rows.OrderByDescending(r => r.BatchNo ?? ""),
                "expiry" => asc ? rows.OrderBy(r => r.Expiry ?? DateTime.MinValue) : rows.OrderByDescending(r => r.Expiry ?? DateTime.MinValue),
                _ => asc
                    ? rows.OrderBy(r => r.Date).ThenBy(r => r.Time ?? TimeSpan.Zero).ThenBy(r => r.DocNo)
                    : rows.OrderByDescending(r => r.Date).ThenByDescending(r => r.Time ?? TimeSpan.Zero).ThenByDescending(r => r.DocNo)
            };
        }

        private async Task<(List<ProductDetailsReportRow> list, int totalCount, decimal totalQtyFiltered, decimal totalAmountFiltered, int page)> LoadProductDetailsReportDataAsync(
            string reportType,
            DateTime? fromDt,
            DateTime? toDt,
            string searchTrim,
            string? filterCol_date,
            string? filterCol_docNo,
            string? filterCol_productCode,
            string? filterCol_productName,
            string? filterCol_party,
            string? filterCol_warehouse,
            string? filterCol_author,
            string? filterCol_region,
            string? filterCol_docNameAr,
            string? filterCol_qtyExpr,
            string? filterCol_unitpriceExpr,
            string? filterCol_linetotalExpr,
            string? filterCol_batch,
            string? filterCol_expiry,
            string? filterCol_notes,
            string sort,
            string dir,
            int page,
            int pageSize)
        {
            var list = new List<ProductDetailsReportRow>();
            int totalCount = 0;
            decimal totalQtyFiltered = 0m;
            decimal totalAmountFiltered = 0m;
            switch (reportType)
            {
                case "All":
                    var allTypes = new[]
                    {
                        "Sales", "Purchases", "SalesReturns", "PurchaseReturns",
                        "Adjustments", "Transfers", "PurchaseRequests", "SalesOrders"
                    };

                    var allRows = new List<ProductDetailsReportRow>();
                    foreach (var type in allTypes)
                    {
                        var (rows, _, _, _, _) = await LoadProductDetailsReportDataAsync(
                            type, fromDt, toDt, searchTrim,
                            filterCol_date, filterCol_docNo, filterCol_productCode, filterCol_productName,
                            filterCol_party, filterCol_warehouse, filterCol_author, filterCol_region, filterCol_docNameAr,
                            filterCol_qtyExpr, filterCol_unitpriceExpr, filterCol_linetotalExpr,
                            filterCol_batch, filterCol_expiry, filterCol_notes,
                            sort ?? "Date", dir ?? "desc", 1, 0);

                        if (rows.Count > 0)
                            allRows.AddRange(rows);
                    }

                    var docNameVals = ParseProductDetailsFilterStrings(filterCol_docNameAr);
                    if (docNameVals.Count > 0)
                        allRows = allRows.Where(r => r.DocumentNameAr != null && docNameVals.Contains(r.DocumentNameAr)).ToList();

                    totalCount = allRows.Count;
                    totalQtyFiltered = allRows.Sum(r => r.Qty);
                    totalAmountFiltered = allRows.Sum(r => r.Total ?? 0m);

                    var sortedAll = ApplyPdrInMemorySort(allRows, sort, dir);
                    if (pageSize == 0)
                    {
                        page = 1;
                        list = sortedAll.Take(100_000).ToList();
                    }
                    else
                    {
                        var skipAll = Math.Max(0, (page - 1) * pageSize);
                        list = sortedAll.Skip(skipAll).Take(pageSize).ToList();
                    }
                    break;

                case "Sales":
                    var salesQuery = _context.SalesInvoiceLines
                        .AsNoTracking()
                        .Include(l => l.SalesInvoice).ThenInclude(h => h!.Customer)
                        .Include(l => l.SalesInvoice).ThenInclude(h => h!.Warehouse).ThenInclude(w => w!.Branch)
                        .Include(l => l.Product)
                        .Where(l => l.SalesInvoice != null);
                    if (fromDt.HasValue) salesQuery = salesQuery.Where(l => l.SalesInvoice!.SIDate >= fromDt.Value);
                    if (toDt.HasValue) salesQuery = salesQuery.Where(l => l.SalesInvoice!.SIDate <= toDt.Value);
                    if (!string.IsNullOrEmpty(searchTrim))
                        salesQuery = salesQuery.Where(l =>
                            (l.Product != null && (l.Product.ProdName != null && l.Product.ProdName.Contains(searchTrim) || l.Product.ProdId.ToString() == searchTrim)));
                    var authorVals = ParseProductDetailsFilterStrings(filterCol_author);
                    if (authorVals.Count > 0) salesQuery = salesQuery.Where(l => l.SalesInvoice!.CreatedBy != null && authorVals.Contains(l.SalesInvoice.CreatedBy));
                    var regionVals = ParseProductDetailsFilterStrings(filterCol_region);
                    if (regionVals.Count > 0) salesQuery = salesQuery.Where(l => l.SalesInvoice!.Warehouse != null && l.SalesInvoice.Warehouse.Branch != null && regionVals.Contains(l.SalesInvoice.Warehouse.Branch.BranchName));
                    var docNoVals = ParseProductDetailsFilterStrings(filterCol_docNo);
                    if (docNoVals.Count > 0) salesQuery = salesQuery.Where(l => docNoVals.Contains(l.SalesInvoice!.SIId.ToString()));
                    var partyVals = ParseProductDetailsFilterStrings(filterCol_party);
                    if (partyVals.Count > 0) salesQuery = salesQuery.Where(l => l.SalesInvoice!.Customer != null && partyVals.Contains(l.SalesInvoice.Customer.CustomerName));
                    var whVals = ParseProductDetailsFilterStrings(filterCol_warehouse);
                    if (whVals.Count > 0) salesQuery = salesQuery.Where(l => l.SalesInvoice!.Warehouse != null && whVals.Contains(l.SalesInvoice.Warehouse.WarehouseName));
                    var prodCodeVals = ParseProductDetailsFilterStrings(filterCol_productCode);
                    if (prodCodeVals.Count > 0) salesQuery = salesQuery.Where(l => l.Product != null && prodCodeVals.Contains(l.Product.ProdId.ToString()));
                    var prodNameVals = ParseProductDetailsFilterStrings(filterCol_productName);
                    if (prodNameVals.Count > 0) salesQuery = salesQuery.Where(l => l.Product != null && l.Product.ProdName != null && prodNameVals.Any(v => l.Product.ProdName.Contains(v)));
                    var dateVals = ParseProductDetailsFilterDates(filterCol_date);
                    if (dateVals.Count > 0) salesQuery = salesQuery.Where(l => dateVals.Contains(l.SalesInvoice!.SIDate.Date));
                    salesQuery = ProductDetailsReportNumericExpr.ApplySalesInvoiceLine(salesQuery, filterCol_qtyExpr, filterCol_unitpriceExpr, filterCol_linetotalExpr);
                    var batchValsSi = ParseProductDetailsFilterStrings(filterCol_batch);
                    if (batchValsSi.Count > 0) salesQuery = salesQuery.Where(l => batchValsSi.Contains(l.BatchNo));
                    var expValsSi = ParseProductDetailsFilterDates(filterCol_expiry);
                    if (expValsSi.Count > 0) salesQuery = salesQuery.Where(l => l.Expiry != null && expValsSi.Contains(l.Expiry.Value.Date));
                    var noteValsSi = ParseProductDetailsFilterStrings(filterCol_notes);
                    if (noteValsSi.Count > 0) salesQuery = salesQuery.Where(l => l.Notes != null && noteValsSi.Any(v => l.Notes.Contains(v)));
                    salesQuery = ApplyPdrSalesLineSort(salesQuery, sort, dir);
                    totalCount = await salesQuery.CountAsync();
                    totalQtyFiltered = await salesQuery.SumAsync(l => (decimal)l.Qty);
                    totalAmountFiltered = await salesQuery.SumAsync(l => l.LineNetTotal);
                    var skip = Math.Max(0, (page - 1) * pageSize);
                    var take = pageSize;
                    if (pageSize == 0)
                    {
                        skip = 0;
                        take = totalCount == 0 ? 10 : Math.Min(totalCount, 100_000);
                        page = 1;
                    }
                    var salesRows = await salesQuery
                        .Skip(skip)
                        .Take(take)
                        .Select(l => new ProductDetailsReportRow
                        {
                            ReportType = "Sales",
                            Date = l.SalesInvoice!.SIDate,
                            DocNo = l.SalesInvoice!.SIId.ToString(),
                            DocId = l.SalesInvoice!.SIId,
                            ProductId = l.ProdId,
                            ProductCode = l.Product != null ? l.Product.ProdId.ToString() : "",
                            ProductName = l.Product != null ? (l.Product.ProdName ?? "") : "",
                            Qty = l.Qty,
                            UnitPrice = l.PriceRetail,
                            Total = l.LineNetTotal,
                            DiscountPercent = l.Disc1Percent + l.Disc2Percent + l.Disc3Percent,
                            DiscountValue = l.DiscountValue,
                            Time = l.SalesInvoice!.SITime,
                            CustomerCode = l.SalesInvoice!.CustomerId.ToString(),
                            PartyName = l.SalesInvoice!.Customer != null ? l.SalesInvoice.Customer.CustomerName : null,
                            WarehouseName = l.SalesInvoice!.Warehouse != null ? l.SalesInvoice.Warehouse.WarehouseName : null,
                            BatchNo = l.BatchNo,
                            Expiry = l.Expiry,
                            Notes = null,
                            Author = l.SalesInvoice!.CreatedBy,
                            Region = l.SalesInvoice!.Warehouse != null && l.SalesInvoice.Warehouse.Branch != null ? l.SalesInvoice.Warehouse.Branch.BranchName : null,
                            DocumentNameAr = "فاتورة مبيعات"
                        })
                        .ToListAsync();
                    list.AddRange(salesRows);
                    break;

                case "Purchases":
                    var piQuery = _context.PILines
                        .AsNoTracking()
                        .Include(l => l.PurchaseInvoice).ThenInclude(h => h!.Customer)
                        .Include(l => l.PurchaseInvoice).ThenInclude(h => h!.Warehouse).ThenInclude(w => w!.Branch)
                        .Include(l => l.Product)
                        .Where(l => l.PurchaseInvoice != null);
                    if (fromDt.HasValue) piQuery = piQuery.Where(l => l.PurchaseInvoice!.PIDate >= fromDt.Value);
                    if (toDt.HasValue) piQuery = piQuery.Where(l => l.PurchaseInvoice!.PIDate <= toDt.Value);
                    if (!string.IsNullOrEmpty(searchTrim))
                        piQuery = piQuery.Where(l =>
                            (l.Product != null && (l.Product.ProdName != null && l.Product.ProdName.Contains(searchTrim) || l.Product.ProdId.ToString() == searchTrim)));
                    var piAuthorVals = ParseProductDetailsFilterStrings(filterCol_author);
                    if (piAuthorVals.Count > 0) piQuery = piQuery.Where(l => l.PurchaseInvoice!.CreatedBy != null && piAuthorVals.Contains(l.PurchaseInvoice.CreatedBy));
                    var piRegionVals = ParseProductDetailsFilterStrings(filterCol_region);
                    if (piRegionVals.Count > 0) piQuery = piQuery.Where(l => l.PurchaseInvoice!.Warehouse != null && l.PurchaseInvoice.Warehouse.Branch != null && piRegionVals.Contains(l.PurchaseInvoice.Warehouse.Branch.BranchName));
                    var piDocNoVals = ParseProductDetailsFilterStrings(filterCol_docNo);
                    if (piDocNoVals.Count > 0) piQuery = piQuery.Where(l => piDocNoVals.Contains(l.PurchaseInvoice!.PIId.ToString()));
                    var piPartyVals = ParseProductDetailsFilterStrings(filterCol_party);
                    if (piPartyVals.Count > 0) piQuery = piQuery.Where(l => l.PurchaseInvoice!.Customer != null && piPartyVals.Contains(l.PurchaseInvoice.Customer.CustomerName));
                    var piWhVals = ParseProductDetailsFilterStrings(filterCol_warehouse);
                    if (piWhVals.Count > 0) piQuery = piQuery.Where(l => l.PurchaseInvoice!.Warehouse != null && piWhVals.Contains(l.PurchaseInvoice.Warehouse.WarehouseName));
                    var piProdCodeVals = ParseProductDetailsFilterStrings(filterCol_productCode);
                    if (piProdCodeVals.Count > 0) piQuery = piQuery.Where(l => l.Product != null && piProdCodeVals.Contains(l.Product.ProdId.ToString()));
                    var piProdNameVals = ParseProductDetailsFilterStrings(filterCol_productName);
                    if (piProdNameVals.Count > 0) piQuery = piQuery.Where(l => l.Product != null && l.Product.ProdName != null && piProdNameVals.Any(v => l.Product.ProdName.Contains(v)));
                    var piDateVals = ParseProductDetailsFilterDates(filterCol_date);
                    if (piDateVals.Count > 0) piQuery = piQuery.Where(l => piDateVals.Contains(l.PurchaseInvoice!.PIDate.Date));
                    piQuery = ProductDetailsReportNumericExpr.ApplyPiLine(piQuery, filterCol_qtyExpr, filterCol_unitpriceExpr, filterCol_linetotalExpr);
                    var batchValsPi = ParseProductDetailsFilterStrings(filterCol_batch);
                    if (batchValsPi.Count > 0) piQuery = piQuery.Where(l => l.BatchNo != null && batchValsPi.Contains(l.BatchNo));
                    var expValsPi = ParseProductDetailsFilterDates(filterCol_expiry);
                    if (expValsPi.Count > 0) piQuery = piQuery.Where(l => l.Expiry != null && expValsPi.Contains(l.Expiry.Value.Date));
                    piQuery = ApplyPdrPiLineSort(piQuery, sort, dir);
                    totalCount = await piQuery.CountAsync();
                    totalQtyFiltered = await piQuery.SumAsync(l => (decimal)l.Qty);
                    totalAmountFiltered = await piQuery.SumAsync(l => l.Qty * l.UnitCost);
                    var piSkip = Math.Max(0, (page - 1) * pageSize);
                    var piTake = pageSize;
                    if (pageSize == 0)
                    {
                        piSkip = 0;
                        piTake = totalCount == 0 ? 10 : Math.Min(totalCount, 100_000);
                        page = 1;
                    }
                    var piRows = await piQuery
                        .Skip(piSkip)
                        .Take(piTake)
                        .Select(l => new ProductDetailsReportRow
                        {
                            ReportType = "Purchases",
                            Date = l.PurchaseInvoice!.PIDate,
                            DocNo = l.PurchaseInvoice!.PIId.ToString(),
                            DocId = l.PurchaseInvoice!.PIId,
                            ProductId = l.ProdId,
                            ProductCode = l.Product != null ? l.Product.ProdId.ToString() : "",
                            ProductName = l.Product != null ? (l.Product.ProdName ?? "") : "",
                            Qty = l.Qty,
                            UnitPrice = l.UnitCost,
                            Total = l.Qty * l.UnitCost,
                            DiscountPercent = l.PurchaseDiscountPct,
                            DiscountValue = null,
                            Time = null,
                            CustomerCode = l.PurchaseInvoice!.CustomerId.ToString(),
                            PartyName = l.PurchaseInvoice!.Customer != null ? l.PurchaseInvoice.Customer.CustomerName : null,
                            WarehouseName = l.PurchaseInvoice!.Warehouse != null ? l.PurchaseInvoice.Warehouse.WarehouseName : null,
                            BatchNo = l.BatchNo,
                            Expiry = l.Expiry,
                            Notes = null,
                            Author = l.PurchaseInvoice!.CreatedBy,
                            Region = l.PurchaseInvoice!.Warehouse != null && l.PurchaseInvoice.Warehouse.Branch != null ? l.PurchaseInvoice.Warehouse.Branch.BranchName : null,
                            DocumentNameAr = "فاتورة مشتريات"
                        })
                        .ToListAsync();
                    list.AddRange(piRows);
                    break;

                case "SalesReturns":
                    var srQuery = _context.SalesReturnLines.AsNoTracking()
                        .Include(l => l.SalesReturn).ThenInclude(s => s!.Customer)
                        .Include(l => l.SalesReturn).ThenInclude(s => s!.Warehouse).ThenInclude(w => w!.Branch)
                        .Include(l => l.Product)
                        .Where(l => l.SalesReturn != null);
                    if (fromDt.HasValue) srQuery = srQuery.Where(l => l.SalesReturn!.SRDate >= fromDt.Value);
                    if (toDt.HasValue) srQuery = srQuery.Where(l => l.SalesReturn!.SRDate <= toDt.Value);
                    if (!string.IsNullOrEmpty(searchTrim))
                        srQuery = srQuery.Where(l => l.Product != null && ((l.Product.ProdName != null && l.Product.ProdName.Contains(searchTrim)) || l.Product.ProdId.ToString() == searchTrim));
                    var srAuthorVals = ParseProductDetailsFilterStrings(filterCol_author);
                    if (srAuthorVals.Count > 0) srQuery = srQuery.Where(l => l.SalesReturn!.CreatedBy != null && srAuthorVals.Contains(l.SalesReturn.CreatedBy));
                    var srDocNoVals = ParseProductDetailsFilterStrings(filterCol_docNo);
                    if (srDocNoVals.Count > 0) srQuery = srQuery.Where(l => srDocNoVals.Contains(l.SalesReturn!.SRId.ToString()));
                    var srPartyVals = ParseProductDetailsFilterStrings(filterCol_party);
                    if (srPartyVals.Count > 0) srQuery = srQuery.Where(l => l.SalesReturn!.Customer != null && srPartyVals.Contains(l.SalesReturn.Customer.CustomerName));
                    var srProdCodeVals = ParseProductDetailsFilterStrings(filterCol_productCode);
                    if (srProdCodeVals.Count > 0) srQuery = srQuery.Where(l => l.Product != null && srProdCodeVals.Contains(l.Product.ProdId.ToString()));
                    var srProdNameVals = ParseProductDetailsFilterStrings(filterCol_productName);
                    if (srProdNameVals.Count > 0) srQuery = srQuery.Where(l => l.Product != null && l.Product.ProdName != null && srProdNameVals.Any(v => l.Product.ProdName.Contains(v)));
                    var srDateVals = ParseProductDetailsFilterDates(filterCol_date);
                    if (srDateVals.Count > 0) srQuery = srQuery.Where(l => srDateVals.Contains(l.SalesReturn!.SRDate.Date));
                    srQuery = ProductDetailsReportNumericExpr.ApplySalesReturnLine(srQuery, filterCol_qtyExpr, filterCol_unitpriceExpr, filterCol_linetotalExpr);
                    var batchValsSr = ParseProductDetailsFilterStrings(filterCol_batch);
                    if (batchValsSr.Count > 0) srQuery = srQuery.Where(l => l.BatchNo != null && batchValsSr.Contains(l.BatchNo));
                    var expValsSr = ParseProductDetailsFilterDates(filterCol_expiry);
                    if (expValsSr.Count > 0) srQuery = srQuery.Where(l => l.Expiry != null && expValsSr.Contains(l.Expiry.Value.Date));
                    {
                        var sk = (sort ?? "Date").Trim();
                        var asc = string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase);
                        srQuery = sk.ToLowerInvariant() switch
                        {
                            "docno" => asc ? srQuery.OrderBy(l => l.SalesReturn!.SRId).ThenBy(l => l.LineNo) : srQuery.OrderByDescending(l => l.SalesReturn!.SRId).ThenBy(l => l.LineNo),
                            "author" => asc ? srQuery.OrderBy(l => l.SalesReturn!.CreatedBy).ThenByDescending(l => l.SalesReturn!.SRDate) : srQuery.OrderByDescending(l => l.SalesReturn!.CreatedBy).ThenByDescending(l => l.SalesReturn!.SRDate),
                            "documentnamear" => srQuery.OrderByDescending(l => l.SalesReturn!.SRDate).ThenBy(l => l.SalesReturn!.SRId).ThenBy(l => l.LineNo),
                            "productcode" => asc ? srQuery.OrderBy(l => l.Product!.ProdId).ThenByDescending(l => l.SalesReturn!.SRDate) : srQuery.OrderByDescending(l => l.Product!.ProdId).ThenByDescending(l => l.SalesReturn!.SRDate),
                            "productname" => asc ? srQuery.OrderBy(l => l.Product!.ProdName).ThenByDescending(l => l.SalesReturn!.SRDate) : srQuery.OrderByDescending(l => l.Product!.ProdName).ThenByDescending(l => l.SalesReturn!.SRDate),
                            "partyname" => asc ? srQuery.OrderBy(l => l.SalesReturn!.Customer!.CustomerName).ThenByDescending(l => l.SalesReturn!.SRDate) : srQuery.OrderByDescending(l => l.SalesReturn!.Customer!.CustomerName).ThenByDescending(l => l.SalesReturn!.SRDate),
                            "qty" => asc ? srQuery.OrderBy(l => l.Qty).ThenByDescending(l => l.SalesReturn!.SRDate) : srQuery.OrderByDescending(l => l.Qty).ThenByDescending(l => l.SalesReturn!.SRDate),
                            "unitprice" => asc ? srQuery.OrderBy(l => l.UnitSalePrice).ThenByDescending(l => l.SalesReturn!.SRDate) : srQuery.OrderByDescending(l => l.UnitSalePrice).ThenByDescending(l => l.SalesReturn!.SRDate),
                            "linetotal" => asc ? srQuery.OrderBy(l => l.LineNetTotal).ThenByDescending(l => l.SalesReturn!.SRDate) : srQuery.OrderByDescending(l => l.LineNetTotal).ThenByDescending(l => l.SalesReturn!.SRDate),
                            "batchno" => asc ? srQuery.OrderBy(l => l.BatchNo ?? "").ThenByDescending(l => l.SalesReturn!.SRDate) : srQuery.OrderByDescending(l => l.BatchNo ?? "").ThenByDescending(l => l.SalesReturn!.SRDate),
                            "expiry" => asc ? srQuery.OrderBy(l => l.Expiry ?? DateTime.MinValue).ThenByDescending(l => l.SalesReturn!.SRDate) : srQuery.OrderByDescending(l => l.Expiry ?? DateTime.MinValue).ThenByDescending(l => l.SalesReturn!.SRDate),
                            "date" => asc ? srQuery.OrderBy(l => l.SalesReturn!.SRDate).ThenBy(l => l.SalesReturn!.SRId).ThenBy(l => l.LineNo) : srQuery.OrderByDescending(l => l.SalesReturn!.SRDate).ThenBy(l => l.SalesReturn!.SRId).ThenBy(l => l.LineNo),
                            _ => srQuery.OrderByDescending(l => l.SalesReturn!.SRDate).ThenBy(l => l.SalesReturn!.SRId).ThenBy(l => l.LineNo)
                        };
                    }
                    totalCount = await srQuery.CountAsync();
                    totalQtyFiltered = await srQuery.SumAsync(l => (decimal)l.Qty);
                    totalAmountFiltered = await srQuery.SumAsync(l => l.LineNetTotal);
                    var srSkip = Math.Max(0, (page - 1) * pageSize);
                    var srTake = pageSize;
                    if (pageSize == 0)
                    {
                        srSkip = 0;
                        srTake = totalCount == 0 ? 10 : Math.Min(totalCount, 100_000);
                        page = 1;
                    }
                    var srRows = await srQuery
                        .Skip(srSkip)
                        .Take(srTake)
                        .Select(l => new ProductDetailsReportRow
                        {
                            ReportType = "SalesReturns",
                            Date = l.SalesReturn!.SRDate,
                            DocNo = l.SalesReturn!.SRId.ToString(),
                            DocId = l.SalesReturn!.SRId,
                            ProductId = l.ProdId,
                            ProductCode = l.Product!.ProdId.ToString(),
                            ProductName = l.Product!.ProdName ?? "",
                            Qty = l.Qty,
                            UnitPrice = l.UnitSalePrice,
                            Total = l.LineNetTotal,
                            DiscountPercent = l.Disc1Percent + l.Disc2Percent + l.Disc3Percent,
                            DiscountValue = l.DiscountValue,
                            Time = l.SalesReturn!.SRTime,
                            CustomerCode = l.SalesReturn!.CustomerId.ToString(),
                            PartyName = l.SalesReturn!.Customer!.CustomerName,
                            WarehouseName = l.SalesReturn!.Warehouse != null ? l.SalesReturn.Warehouse.WarehouseName : null,
                            BatchNo = l.BatchNo,
                            Expiry = l.Expiry,
                            Notes = null,
                            Author = l.SalesReturn!.CreatedBy,
                            Region = l.SalesReturn!.Warehouse != null && l.SalesReturn.Warehouse.Branch != null ? l.SalesReturn.Warehouse.Branch.BranchName : null,
                            DocumentNameAr = "مرتجع بيع"
                        })
                        .ToListAsync();
                    list.AddRange(srRows);
                    break;

                case "PurchaseReturns":
                    var prQuery = _context.PurchaseReturnLines
                        .AsNoTracking()
                        .Include(l => l.PurchaseReturn).ThenInclude(h => h!.Customer)
                        .Include(l => l.PurchaseReturn).ThenInclude(h => h!.Warehouse).ThenInclude(w => w!.Branch)
                        .Include(l => l.Product)
                        .Where(l => l.PurchaseReturn != null);
                    if (fromDt.HasValue) prQuery = prQuery.Where(l => l.PurchaseReturn!.PRetDate >= fromDt.Value);
                    if (toDt.HasValue) prQuery = prQuery.Where(l => l.PurchaseReturn!.PRetDate <= toDt.Value);
                    if (!string.IsNullOrEmpty(searchTrim))
                        prQuery = prQuery.Where(l =>
                            (l.Product != null && (l.Product.ProdName != null && l.Product.ProdName.Contains(searchTrim) || l.Product.ProdId.ToString() == searchTrim)));
                    var prAuthorVals = ParseProductDetailsFilterStrings(filterCol_author);
                    if (prAuthorVals.Count > 0) prQuery = prQuery.Where(l => l.PurchaseReturn!.CreatedBy != null && prAuthorVals.Contains(l.PurchaseReturn.CreatedBy));
                    var prDocNoVals = ParseProductDetailsFilterStrings(filterCol_docNo);
                    if (prDocNoVals.Count > 0) prQuery = prQuery.Where(l => prDocNoVals.Contains(l.PurchaseReturn!.PRetId.ToString()));
                    var prPartyVals = ParseProductDetailsFilterStrings(filterCol_party);
                    if (prPartyVals.Count > 0) prQuery = prQuery.Where(l => l.PurchaseReturn!.Customer != null && prPartyVals.Contains(l.PurchaseReturn.Customer.CustomerName));
                    var prProdCodeVals = ParseProductDetailsFilterStrings(filterCol_productCode);
                    if (prProdCodeVals.Count > 0) prQuery = prQuery.Where(l => l.Product != null && prProdCodeVals.Contains(l.Product.ProdId.ToString()));
                    var prProdNameVals = ParseProductDetailsFilterStrings(filterCol_productName);
                    if (prProdNameVals.Count > 0) prQuery = prQuery.Where(l => l.Product != null && l.Product.ProdName != null && prProdNameVals.Any(v => l.Product.ProdName.Contains(v)));
                    var prDateVals = ParseProductDetailsFilterDates(filterCol_date);
                    if (prDateVals.Count > 0) prQuery = prQuery.Where(l => prDateVals.Contains(l.PurchaseReturn!.PRetDate.Date));
                    prQuery = ProductDetailsReportNumericExpr.ApplyPurchaseReturnLine(prQuery, filterCol_qtyExpr, filterCol_unitpriceExpr, filterCol_linetotalExpr);
                    var batchValsPr = ParseProductDetailsFilterStrings(filterCol_batch);
                    if (batchValsPr.Count > 0) prQuery = prQuery.Where(l => l.BatchNo != null && batchValsPr.Contains(l.BatchNo));
                    var expValsPr = ParseProductDetailsFilterDates(filterCol_expiry);
                    if (expValsPr.Count > 0) prQuery = prQuery.Where(l => l.Expiry != null && expValsPr.Contains(l.Expiry.Value.Date));
                    prQuery = ApplyPdrPurchaseReturnLineSort(prQuery, sort, dir);
                    totalCount = await prQuery.CountAsync();
                    totalQtyFiltered = await prQuery.SumAsync(l => (decimal)l.Qty);
                    totalAmountFiltered = await prQuery.SumAsync(l => l.Qty * l.UnitCost);
                    var prSkip = Math.Max(0, (page - 1) * pageSize);
                    var prTake = pageSize;
                    if (pageSize == 0)
                    {
                        prSkip = 0;
                        prTake = totalCount == 0 ? 10 : Math.Min(totalCount, 100_000);
                        page = 1;
                    }
                    var prRows = await prQuery
                        .Skip(prSkip)
                        .Take(prTake)
                        .Select(l => new ProductDetailsReportRow
                        {
                            ReportType = "PurchaseReturns",
                            Date = l.PurchaseReturn!.PRetDate,
                            DocNo = l.PurchaseReturn!.PRetId.ToString(),
                            DocId = l.PurchaseReturn!.PRetId,
                            ProductId = l.ProdId,
                            ProductCode = l.Product != null ? l.Product.ProdId.ToString() : "",
                            ProductName = l.Product != null ? (l.Product.ProdName ?? "") : "",
                            Qty = l.Qty,
                            UnitPrice = l.UnitCost,
                            Total = l.Qty * l.UnitCost,
                            DiscountPercent = l.PurchaseDiscountPct,
                            DiscountValue = null,
                            Time = null,
                            CustomerCode = l.PurchaseReturn!.CustomerId.ToString(),
                            PartyName = l.PurchaseReturn!.Customer != null ? l.PurchaseReturn.Customer.CustomerName : null,
                            WarehouseName = l.PurchaseReturn!.Warehouse != null ? l.PurchaseReturn.Warehouse.WarehouseName : null,
                            BatchNo = l.BatchNo,
                            Expiry = l.Expiry,
                            Notes = null,
                            Author = l.PurchaseReturn!.CreatedBy,
                            Region = l.PurchaseReturn!.Warehouse != null && l.PurchaseReturn.Warehouse.Branch != null ? l.PurchaseReturn.Warehouse.Branch.BranchName : null,
                            DocumentNameAr = "مرتجع شراء"
                        })
                        .ToListAsync();
                    list.AddRange(prRows);
                    break;

                case "Adjustments":
                    var adjQuery = _context.StockAdjustmentLines
                        .AsNoTracking()
                        .Include(l => l.StockAdjustment).ThenInclude(h => h!.Warehouse).ThenInclude(w => w!.Branch)
                        .Include(l => l.Product)
                        .Where(l => l.StockAdjustment != null && l.Product != null);
                    if (fromDt.HasValue) adjQuery = adjQuery.Where(l => l.StockAdjustment!.AdjustmentDate >= fromDt.Value);
                    if (toDt.HasValue) adjQuery = adjQuery.Where(l => l.StockAdjustment!.AdjustmentDate <= toDt.Value);
                    if (!string.IsNullOrEmpty(searchTrim))
                        adjQuery = adjQuery.Where(l =>
                            (l.Product!.ProdName != null && l.Product.ProdName.Contains(searchTrim)) || l.Product.ProdId.ToString() == searchTrim);
                    var adjAuthorVals = ParseProductDetailsFilterStrings(filterCol_author);
                    if (adjAuthorVals.Count > 0) adjQuery = adjQuery.Where(l => l.StockAdjustment!.PostedBy != null && adjAuthorVals.Contains(l.StockAdjustment.PostedBy));
                    var adjDocNoVals = ParseProductDetailsFilterStrings(filterCol_docNo);
                    if (adjDocNoVals.Count > 0) adjQuery = adjQuery.Where(l => adjDocNoVals.Contains(l.StockAdjustment!.Id.ToString()));
                    var adjWhVals = ParseProductDetailsFilterStrings(filterCol_warehouse);
                    if (adjWhVals.Count > 0) adjQuery = adjQuery.Where(l => l.StockAdjustment!.Warehouse != null && adjWhVals.Contains(l.StockAdjustment.Warehouse.WarehouseName));
                    var adjRegionVals = ParseProductDetailsFilterStrings(filterCol_region);
                    if (adjRegionVals.Count > 0) adjQuery = adjQuery.Where(l => l.StockAdjustment!.Warehouse != null && l.StockAdjustment.Warehouse.Branch != null && adjRegionVals.Contains(l.StockAdjustment.Warehouse.Branch.BranchName));
                    var adjProdCodeVals = ParseProductDetailsFilterStrings(filterCol_productCode);
                    if (adjProdCodeVals.Count > 0) adjQuery = adjQuery.Where(l => adjProdCodeVals.Contains(l.Product!.ProdId.ToString()));
                    var adjProdNameVals = ParseProductDetailsFilterStrings(filterCol_productName);
                    if (adjProdNameVals.Count > 0) adjQuery = adjQuery.Where(l => l.Product!.ProdName != null && adjProdNameVals.Any(v => l.Product.ProdName.Contains(v)));
                    var adjDateVals = ParseProductDetailsFilterDates(filterCol_date);
                    if (adjDateVals.Count > 0) adjQuery = adjQuery.Where(l => adjDateVals.Contains(l.StockAdjustment!.AdjustmentDate.Date));
                    adjQuery = ProductDetailsReportNumericExpr.ApplyStockAdjustmentLine(adjQuery, filterCol_qtyExpr, filterCol_unitpriceExpr, filterCol_linetotalExpr);
                    var noteValsAdj = ParseProductDetailsFilterStrings(filterCol_notes);
                    if (noteValsAdj.Count > 0) adjQuery = adjQuery.Where(l => l.Note != null && noteValsAdj.Any(v => l.Note.Contains(v)));
                    adjQuery = ApplyPdrStockAdjustmentLineSort(adjQuery, sort, dir);
                    totalCount = await adjQuery.CountAsync();
                    totalQtyFiltered = await adjQuery.SumAsync(l => l.QtyDiff);
                    totalAmountFiltered = await adjQuery.SumAsync(l => l.CostDiff ?? 0m);
                    var adjSkip = Math.Max(0, (page - 1) * pageSize);
                    var adjTake = pageSize;
                    if (pageSize == 0)
                    {
                        adjSkip = 0;
                        adjTake = totalCount == 0 ? 10 : Math.Min(totalCount, 100_000);
                        page = 1;
                    }
                    var adjRows = await adjQuery
                        .Skip(adjSkip)
                        .Take(adjTake)
                        .Select(l => new ProductDetailsReportRow
                        {
                            ReportType = "Adjustments",
                            Date = l.StockAdjustment!.AdjustmentDate,
                            DocNo = l.StockAdjustment!.Id.ToString(),
                            DocId = l.StockAdjustment!.Id,
                            ProductId = l.ProductId,
                            ProductCode = l.Product!.ProdId.ToString(),
                            ProductName = l.Product.ProdName ?? "",
                            Qty = l.QtyDiff,
                            UnitPrice = l.CostPerUnit,
                            Total = l.CostDiff,
                            DiscountPercent = null,
                            DiscountValue = null,
                            Time = null,
                            CustomerCode = null,
                            PartyName = null,
                            WarehouseName = l.StockAdjustment!.Warehouse != null ? l.StockAdjustment.Warehouse.WarehouseName : null,
                            BatchNo = null,
                            Expiry = null,
                            Notes = l.Note,
                            Author = l.StockAdjustment!.PostedBy,
                            Region = l.StockAdjustment!.Warehouse != null && l.StockAdjustment.Warehouse.Branch != null ? l.StockAdjustment.Warehouse.Branch.BranchName : null,
                            DocumentNameAr = "تسوية جرد"
                        })
                        .ToListAsync();
                    list.AddRange(adjRows);
                    break;

                case "Transfers":
                    var stQuery = _context.StockTransferLines
                        .AsNoTracking()
                        .Include(l => l.StockTransfer).ThenInclude(st => st!.FromWarehouse).ThenInclude(w => w!.Branch)
                        .Include(l => l.StockTransfer).ThenInclude(st => st!.ToWarehouse)
                        .Include(l => l.Product)
                        .Where(l => l.StockTransfer != null && l.Product != null);
                    if (fromDt.HasValue) stQuery = stQuery.Where(l => l.StockTransfer!.TransferDate >= fromDt.Value);
                    if (toDt.HasValue) stQuery = stQuery.Where(l => l.StockTransfer!.TransferDate <= toDt.Value);
                    if (!string.IsNullOrEmpty(searchTrim))
                        stQuery = stQuery.Where(l =>
                            (l.Product!.ProdName != null && l.Product.ProdName.Contains(searchTrim)) || l.Product.ProdId.ToString() == searchTrim);
                    var stDocNoVals = ParseProductDetailsFilterStrings(filterCol_docNo);
                    if (stDocNoVals.Count > 0) stQuery = stQuery.Where(l => stDocNoVals.Contains(l.StockTransfer!.Id.ToString()));
                    var stRegionVals = ParseProductDetailsFilterStrings(filterCol_region);
                    if (stRegionVals.Count > 0) stQuery = stQuery.Where(l => l.StockTransfer!.FromWarehouse != null && l.StockTransfer.FromWarehouse.Branch != null && stRegionVals.Contains(l.StockTransfer.FromWarehouse.Branch.BranchName));
                    var stProdCodeVals = ParseProductDetailsFilterStrings(filterCol_productCode);
                    if (stProdCodeVals.Count > 0) stQuery = stQuery.Where(l => stProdCodeVals.Contains(l.Product!.ProdId.ToString()));
                    var stProdNameVals = ParseProductDetailsFilterStrings(filterCol_productName);
                    if (stProdNameVals.Count > 0) stQuery = stQuery.Where(l => l.Product!.ProdName != null && stProdNameVals.Any(v => l.Product.ProdName.Contains(v)));
                    var stDateVals = ParseProductDetailsFilterDates(filterCol_date);
                    if (stDateVals.Count > 0) stQuery = stQuery.Where(l => stDateVals.Contains(l.StockTransfer!.TransferDate.Date));
                    stQuery = ProductDetailsReportNumericExpr.ApplyStockTransferLine(stQuery, filterCol_qtyExpr, filterCol_unitpriceExpr, filterCol_linetotalExpr);
                    var noteValsSt = ParseProductDetailsFilterStrings(filterCol_notes);
                    if (noteValsSt.Count > 0) stQuery = stQuery.Where(l => l.Note != null && noteValsSt.Any(v => l.Note.Contains(v)));
                    stQuery = ApplyPdrStockTransferLineSort(stQuery, sort, dir);
                    totalCount = await stQuery.CountAsync();
                    totalQtyFiltered = await stQuery.SumAsync(l => l.Qty);
                    totalAmountFiltered = await stQuery.SumAsync(l => l.Qty * l.UnitCost);
                    var stSkip = Math.Max(0, (page - 1) * pageSize);
                    var stTake = pageSize;
                    if (pageSize == 0)
                    {
                        stSkip = 0;
                        stTake = totalCount == 0 ? 10 : Math.Min(totalCount, 100_000);
                        page = 1;
                    }
                    var stRows = await stQuery
                        .Skip(stSkip)
                        .Take(stTake)
                        .Select(l => new ProductDetailsReportRow
                        {
                            ReportType = "Transfers",
                            Date = l.StockTransfer!.TransferDate,
                            DocNo = l.StockTransfer!.Id.ToString(),
                            DocId = l.StockTransfer!.Id,
                            ProductId = l.ProductId,
                            ProductCode = l.Product!.ProdId.ToString(),
                            ProductName = l.Product.ProdName ?? "",
                            Qty = l.Qty,
                            UnitPrice = l.UnitCost,
                            Total = l.Qty * l.UnitCost,
                            DiscountPercent = null,
                            DiscountValue = null,
                            Time = null,
                            CustomerCode = null,
                            PartyName = null,
                            WarehouseName =
                                (l.StockTransfer!.FromWarehouse != null ? l.StockTransfer.FromWarehouse.WarehouseName : "—")
                                + " → " +
                                (l.StockTransfer!.ToWarehouse != null ? l.StockTransfer.ToWarehouse.WarehouseName : "—"),
                            BatchNo = null,
                            Expiry = null,
                            Notes = l.Note,
                            Author = l.StockTransfer!.PostedBy,
                            Region = l.StockTransfer!.FromWarehouse != null && l.StockTransfer.FromWarehouse.Branch != null ? l.StockTransfer.FromWarehouse.Branch.BranchName : null,
                            DocumentNameAr = "تحويل مخزني"
                        })
                        .ToListAsync();
                    list.AddRange(stRows);
                    break;

                case "PurchaseRequests":
                    var prReqQuery = _context.PRLines
                        .AsNoTracking()
                        .Include(l => l.PurchaseRequest).ThenInclude(h => h!.Customer)
                        .Include(l => l.PurchaseRequest).ThenInclude(h => h!.Warehouse).ThenInclude(w => w!.Branch)
                        .Include(l => l.Product)
                        .Where(l => l.PurchaseRequest != null);
                    if (fromDt.HasValue) prReqQuery = prReqQuery.Where(l => l.PurchaseRequest!.PRDate >= fromDt.Value);
                    if (toDt.HasValue) prReqQuery = prReqQuery.Where(l => l.PurchaseRequest!.PRDate <= toDt.Value);
                    if (!string.IsNullOrEmpty(searchTrim))
                        prReqQuery = prReqQuery.Where(l =>
                            (l.Product != null && (l.Product.ProdName != null && l.Product.ProdName.Contains(searchTrim) || l.Product.ProdId.ToString() == searchTrim)));
                    var prReqAuthorVals = ParseProductDetailsFilterStrings(filterCol_author);
                    if (prReqAuthorVals.Count > 0) prReqQuery = prReqQuery.Where(l => l.PurchaseRequest!.CreatedBy != null && prReqAuthorVals.Contains(l.PurchaseRequest.CreatedBy));
                    var prReqDocNoVals = ParseProductDetailsFilterStrings(filterCol_docNo);
                    if (prReqDocNoVals.Count > 0) prReqQuery = prReqQuery.Where(l => prReqDocNoVals.Contains(l.PurchaseRequest!.PRId.ToString()));
                    var prReqPartyVals = ParseProductDetailsFilterStrings(filterCol_party);
                    if (prReqPartyVals.Count > 0) prReqQuery = prReqQuery.Where(l => l.PurchaseRequest!.Customer != null && prReqPartyVals.Contains(l.PurchaseRequest.Customer.CustomerName));
                    var prReqWhVals = ParseProductDetailsFilterStrings(filterCol_warehouse);
                    if (prReqWhVals.Count > 0) prReqQuery = prReqQuery.Where(l => l.PurchaseRequest!.Warehouse != null && prReqWhVals.Contains(l.PurchaseRequest.Warehouse.WarehouseName));
                    var prReqRegionVals = ParseProductDetailsFilterStrings(filterCol_region);
                    if (prReqRegionVals.Count > 0) prReqQuery = prReqQuery.Where(l => l.PurchaseRequest!.Warehouse != null && l.PurchaseRequest.Warehouse.Branch != null && prReqRegionVals.Contains(l.PurchaseRequest.Warehouse.Branch.BranchName));
                    var prReqProdCodeVals = ParseProductDetailsFilterStrings(filterCol_productCode);
                    if (prReqProdCodeVals.Count > 0) prReqQuery = prReqQuery.Where(l => l.Product != null && prReqProdCodeVals.Contains(l.Product.ProdId.ToString()));
                    var prReqProdNameVals = ParseProductDetailsFilterStrings(filterCol_productName);
                    if (prReqProdNameVals.Count > 0) prReqQuery = prReqQuery.Where(l => l.Product != null && l.Product.ProdName != null && prReqProdNameVals.Any(v => l.Product.ProdName.Contains(v)));
                    var prReqDateVals = ParseProductDetailsFilterDates(filterCol_date);
                    if (prReqDateVals.Count > 0) prReqQuery = prReqQuery.Where(l => prReqDateVals.Contains(l.PurchaseRequest!.PRDate.Date));
                    prReqQuery = ProductDetailsReportNumericExpr.ApplyPrLine(prReqQuery, filterCol_qtyExpr, filterCol_unitpriceExpr, filterCol_linetotalExpr);
                    var batchValsPrReq = ParseProductDetailsFilterStrings(filterCol_batch);
                    if (batchValsPrReq.Count > 0) prReqQuery = prReqQuery.Where(l => l.PreferredBatchNo != null && batchValsPrReq.Contains(l.PreferredBatchNo));
                    var expValsPrReq = ParseProductDetailsFilterDates(filterCol_expiry);
                    if (expValsPrReq.Count > 0) prReqQuery = prReqQuery.Where(l => l.PreferredExpiry != null && expValsPrReq.Contains(l.PreferredExpiry.Value.Date));
                    prReqQuery = ApplyPdrPrLineSort(prReqQuery, sort, dir);
                    totalCount = await prReqQuery.CountAsync();
                    totalQtyFiltered = await prReqQuery.SumAsync(l => (decimal)l.QtyRequested);
                    totalAmountFiltered = await prReqQuery.SumAsync(l => l.QtyRequested * l.ExpectedCost);
                    var prReqSkip = Math.Max(0, (page - 1) * pageSize);
                    var prReqTake = pageSize;
                    if (pageSize == 0)
                    {
                        prReqSkip = 0;
                        prReqTake = totalCount == 0 ? 10 : Math.Min(totalCount, 100_000);
                        page = 1;
                    }
                    var prReqRows = await prReqQuery
                        .Skip(prReqSkip)
                        .Take(prReqTake)
                        .Select(l => new ProductDetailsReportRow
                        {
                            ReportType = "PurchaseRequests",
                            Date = l.PurchaseRequest!.PRDate,
                            DocNo = l.PurchaseRequest!.PRId.ToString(),
                            DocId = l.PurchaseRequest!.PRId,
                            ProductId = l.ProdId,
                            ProductCode = l.Product != null ? l.Product.ProdId.ToString() : "",
                            ProductName = l.Product != null ? (l.Product.ProdName ?? "") : "",
                            Qty = l.QtyRequested,
                            UnitPrice = l.ExpectedCost,
                            Total = l.QtyRequested * l.ExpectedCost,
                            DiscountPercent = l.PurchaseDiscountPct,
                            DiscountValue = null,
                            Time = null,
                            CustomerCode = l.PurchaseRequest!.CustomerId.ToString(),
                            PartyName = l.PurchaseRequest!.Customer != null ? l.PurchaseRequest.Customer.CustomerName : null,
                            WarehouseName = l.PurchaseRequest!.Warehouse != null ? l.PurchaseRequest.Warehouse.WarehouseName : null,
                            BatchNo = l.PreferredBatchNo,
                            Expiry = l.PreferredExpiry,
                            Notes = null,
                            Author = l.PurchaseRequest!.CreatedBy,
                            Region = l.PurchaseRequest!.Warehouse != null && l.PurchaseRequest.Warehouse.Branch != null ? l.PurchaseRequest.Warehouse.Branch.BranchName : null,
                            DocumentNameAr = "طلب شراء"
                        })
                        .ToListAsync();
                    list.AddRange(prReqRows);
                    break;

                case "SalesOrders":
                    var soQuery = _context.SOLines
                        .AsNoTracking()
                        .Include(l => l.SalesOrder).ThenInclude(h => h!.Customer)
                        .Include(l => l.SalesOrder).ThenInclude(h => h!.Warehouse).ThenInclude(w => w!.Branch)
                        .Include(l => l.Product)
                        .Where(l => l.SalesOrder != null);
                    if (fromDt.HasValue) soQuery = soQuery.Where(l => l.SalesOrder!.SODate >= fromDt.Value);
                    if (toDt.HasValue) soQuery = soQuery.Where(l => l.SalesOrder!.SODate <= toDt.Value);
                    if (!string.IsNullOrEmpty(searchTrim))
                        soQuery = soQuery.Where(l =>
                            (l.Product != null && (l.Product.ProdName != null && l.Product.ProdName.Contains(searchTrim) || l.Product.ProdId.ToString() == searchTrim)));
                    var soAuthorVals = ParseProductDetailsFilterStrings(filterCol_author);
                    if (soAuthorVals.Count > 0) soQuery = soQuery.Where(l => l.SalesOrder!.CreatedBy != null && soAuthorVals.Contains(l.SalesOrder.CreatedBy));
                    var soDocNoVals = ParseProductDetailsFilterStrings(filterCol_docNo);
                    if (soDocNoVals.Count > 0) soQuery = soQuery.Where(l => soDocNoVals.Contains(l.SalesOrder!.SOId.ToString()));
                    var soPartyVals = ParseProductDetailsFilterStrings(filterCol_party);
                    if (soPartyVals.Count > 0) soQuery = soQuery.Where(l => l.SalesOrder!.Customer != null && soPartyVals.Contains(l.SalesOrder.Customer.CustomerName));
                    var soProdCodeVals = ParseProductDetailsFilterStrings(filterCol_productCode);
                    if (soProdCodeVals.Count > 0) soQuery = soQuery.Where(l => l.Product != null && soProdCodeVals.Contains(l.Product.ProdId.ToString()));
                    var soProdNameVals = ParseProductDetailsFilterStrings(filterCol_productName);
                    if (soProdNameVals.Count > 0) soQuery = soQuery.Where(l => l.Product != null && l.Product.ProdName != null && soProdNameVals.Any(v => l.Product.ProdName.Contains(v)));
                    var soDateVals = ParseProductDetailsFilterDates(filterCol_date);
                    if (soDateVals.Count > 0) soQuery = soQuery.Where(l => soDateVals.Contains(l.SalesOrder!.SODate.Date));
                    soQuery = ProductDetailsReportNumericExpr.ApplySoLine(soQuery, filterCol_qtyExpr, filterCol_unitpriceExpr, filterCol_linetotalExpr);
                    var batchValsSo = ParseProductDetailsFilterStrings(filterCol_batch);
                    if (batchValsSo.Count > 0) soQuery = soQuery.Where(l => l.PreferredBatchNo != null && batchValsSo.Contains(l.PreferredBatchNo));
                    var expValsSo = ParseProductDetailsFilterDates(filterCol_expiry);
                    if (expValsSo.Count > 0) soQuery = soQuery.Where(l => l.PreferredExpiry != null && expValsSo.Contains(l.PreferredExpiry.Value.Date));
                    soQuery = ApplyPdrSoLineSort(soQuery, sort, dir);
                    totalCount = await soQuery.CountAsync();
                    totalQtyFiltered = await soQuery.SumAsync(l => (decimal)l.QtyRequested);
                    totalAmountFiltered = await soQuery.SumAsync(l => l.QtyRequested * l.RequestedRetailPrice * (1 - l.SalesDiscountPct / 100m));
                    var soSkip = Math.Max(0, (page - 1) * pageSize);
                    var soTake = pageSize;
                    if (pageSize == 0)
                    {
                        soSkip = 0;
                        soTake = totalCount == 0 ? 10 : Math.Min(totalCount, 100_000);
                        page = 1;
                    }
                    var soRows = await soQuery
                        .Skip(soSkip)
                        .Take(soTake)
                        .Select(l => new ProductDetailsReportRow
                        {
                            ReportType = "SalesOrders",
                            Date = l.SalesOrder!.SODate,
                            DocNo = l.SalesOrder!.SOId.ToString(),
                            DocId = l.SalesOrder!.SOId,
                            ProductId = l.ProdId,
                            ProductCode = l.Product != null ? l.Product.ProdId.ToString() : "",
                            ProductName = l.Product != null ? (l.Product.ProdName ?? "") : "",
                            Qty = l.QtyRequested,
                            UnitPrice = l.RequestedRetailPrice,
                            Total = l.QtyRequested * l.RequestedRetailPrice * (1 - l.SalesDiscountPct / 100m),
                            DiscountPercent = l.SalesDiscountPct,
                            DiscountValue = null,
                            Time = null,
                            CustomerCode = l.SalesOrder!.CustomerId.ToString(),
                            PartyName = l.SalesOrder!.Customer != null ? l.SalesOrder.Customer.CustomerName : null,
                            WarehouseName = l.SalesOrder!.Warehouse != null ? l.SalesOrder.Warehouse.WarehouseName : null,
                            BatchNo = l.PreferredBatchNo,
                            Expiry = l.PreferredExpiry,
                            Notes = null,
                            Author = l.SalesOrder!.CreatedBy,
                            Region = l.SalesOrder!.Warehouse != null && l.SalesOrder.Warehouse.Branch != null ? l.SalesOrder.Warehouse.Branch.BranchName : null,
                            DocumentNameAr = "أمر بيع"
                        })
                        .ToListAsync();
                    list.AddRange(soRows);
                    break;
            }

            return (list, totalCount, totalQtyFiltered, totalAmountFiltered, page);
        }

        [HttpGet]
        [RequirePermission("Reports.ProductDetailsReport")]
        public async Task<IActionResult> ProductDetailsReportPrint(
            string reportType,
            DateTime? fromDate,
            DateTime? toDate,
            string? search,
            string? filterCol_date = null,
            string? filterCol_docNo = null,
            string? filterCol_productCode = null,
            string? filterCol_productName = null,
            string? filterCol_party = null,
            string? filterCol_warehouse = null,
            string? filterCol_author = null,
            string? filterCol_region = null,
            string? filterCol_docNameAr = null,
            string? filterCol_qtyExpr = null,
            string? filterCol_unitpriceExpr = null,
            string? filterCol_linetotalExpr = null,
            string? filterCol_batch = null,
            string? filterCol_expiry = null,
            string? filterCol_notes = null,
            string? sort = "Date",
            string? dir = "desc",
            string? printCols = null)
        {
            reportType = string.IsNullOrWhiteSpace(reportType) ? null : reportType.Trim();
            if (string.IsNullOrWhiteSpace(reportType))
            {
                ViewBag.TotalMatching = 0;
                ViewBag.PrintedCount = 0;
                ViewBag.ReportTypeDisplayName = "";
                ViewBag.PrintColumnKeys = ListPrintColumnParser.ParsePrintColumns(printCols, ProductDetailsReportPrintColumnOrder);
                ViewBag.PrintColumnsFromList = !string.IsNullOrWhiteSpace(printCols);
                return View("ProductDetailsReportPrint", new List<ProductDetailsReportRow>());
            }

            var fromDt = fromDate.HasValue ? DateTime.SpecifyKind(fromDate.Value, DateTimeKind.Local) : (DateTime?)null;
            var toDt = toDate.HasValue ? DateTime.SpecifyKind(toDate.Value, DateTimeKind.Local) : (DateTime?)null;
            var searchTrim = search?.Trim() ?? "";

            if (!string.Equals(reportType, "All", StringComparison.OrdinalIgnoreCase)
                && !PdrDocNameArPasses(reportType!, filterCol_docNameAr))
            {
                ViewBag.TotalMatching = 0;
                ViewBag.PrintedCount = 0;
                ViewBag.ReportTypeDisplayName = GetPdrReportTypeDisplayName(reportType);
                ViewBag.PrintColumnKeys = ListPrintColumnParser.ParsePrintColumns(printCols, ProductDetailsReportPrintColumnOrder);
                ViewBag.PrintColumnsFromList = !string.IsNullOrWhiteSpace(printCols);
                return View("ProductDetailsReportPrint", new List<ProductDetailsReportRow>());
            }

            const int maxRows = 100_000;
            var (list, totalMatching, totalQty, totalAmount, _) = await LoadProductDetailsReportDataAsync(
                reportType!, fromDt, toDt, searchTrim,
                filterCol_date, filterCol_docNo, filterCol_productCode, filterCol_productName,
                filterCol_party, filterCol_warehouse, filterCol_author, filterCol_region, filterCol_docNameAr,
                filterCol_qtyExpr, filterCol_unitpriceExpr, filterCol_linetotalExpr,
                filterCol_batch, filterCol_expiry, filterCol_notes,
                sort ?? "Date", dir ?? "desc", 1, 0);

            ViewBag.TotalMatching = totalMatching;
            ViewBag.PrintedCount = list.Count;
            ViewBag.Capped = totalMatching > maxRows;
            ViewBag.MaxRows = maxRows;
            ViewBag.TotalQtyFiltered = totalQty;
            ViewBag.TotalAmountFiltered = totalAmount;
            ViewBag.Sort = (sort ?? "Date").Trim();
            ViewBag.Dir = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc";
            ViewBag.SearchSummary = searchTrim;
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;
            ViewBag.ReportTypeDisplayName = GetPdrReportTypeDisplayName(reportType);
            ViewBag.PrintColumnKeys = ListPrintColumnParser.ParsePrintColumns(printCols, ProductDetailsReportPrintColumnOrder);
            ViewBag.PrintColumnsFromList = !string.IsNullOrWhiteSpace(printCols);
            return View("ProductDetailsReportPrint", list);
        }
    }
}
