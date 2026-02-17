using ERP.Data;
using ERP.Services;
using ERP.Models;
using ERP.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر التقارير Reports Controller
    /// يحتوي على جميع التقارير المالية والمخزنية
    /// </summary>
    public class ReportsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly StockAnalysisService _stockAnalysisService;

        public ReportsController(AppDbContext context, StockAnalysisService stockAnalysisService)
        {
            _context = context;
            _stockAnalysisService = stockAnalysisService;
        }

        // =========================================================
        // تقرير: أرصدة الأصناف
        // يعرض الصنف، الكمية الحالية، الخصم المرجح، المبيعات بين تاريخين، 
        // سعر الجمهور، تكلفة العلبة، والتكلفة الإجمالية
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> ProductBalances(
            string? search,
            int? categoryId,
            int? warehouseId,
            DateTime? fromDate,
            DateTime? toDate,
            bool includeZeroQty = false,
            string? sortBy = "name",
            string? sortDir = "asc",
            bool loadReport = false,
            int page = 1,
            int pageSize = 200)
        {
            // =========================================================
            // 1) تجهيز القوائم المنسدلة (Categories, Warehouses)
            // =========================================================
            var categories = await _context.Categories
                .AsNoTracking()
                .OrderBy(c => c.CategoryName)
                .Select(c => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                {
                    Value = c.CategoryId.ToString(),
                    Text = c.CategoryName
                })
                .ToListAsync();

            var warehouses = await _context.Warehouses
                .AsNoTracking()
                .OrderBy(w => w.WarehouseName)
                .Select(w => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                {
                    Value = w.WarehouseId.ToString(),
                    Text = w.WarehouseName
                })
                .ToListAsync();

            ViewBag.Categories = categories;
            ViewBag.Warehouses = warehouses;

            // =========================================================
            // 1.1) تحميل قائمة الأصناف للأوتوكومبليت (datalist)
            // =========================================================
            var productsAuto = await _context.Products
                .AsNoTracking()
                .OrderBy(p => p.ProdName)
                .Select(p => new
                {
                    Id = p.ProdId,
                    Name = p.ProdName ?? string.Empty,
                    GenericName = p.GenericName ?? string.Empty,
                    Company = p.Company ?? string.Empty,
                    PriceRetail = p.PriceRetail,
                    HasQuota = p.HasQuota
                })
                .ToListAsync();
            ViewBag.ProductsAuto = productsAuto;

            // =========================================================
            // 2) تجهيز الفلاتر
            // =========================================================
            ViewBag.Search = search ?? "";
            ViewBag.CategoryId = categoryId;
            ViewBag.WarehouseId = warehouseId;
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;
            ViewBag.IncludeZeroQty = includeZeroQty;
            ViewBag.SortBy = sortBy;
            ViewBag.SortDir = sortDir;

            // =========================================================
            // 3) تحميل البيانات فقط عند الضغط على "تجميع التقرير"
            // =========================================================
            if (!loadReport)
            {
                // الصفحة تفتح بدون بيانات - فقط الفلاتر
                // الافتراضي: عرض الصفر = true (عند أول فتح)
                ViewBag.IncludeZeroQty = true;
                ViewBag.ReportData = new List<ProductBalanceReportDto>();
                ViewBag.TotalCost = 0m;
                return View();
            }

            // عند تحميل التقرير: إذا لم يتم تحديد includeZeroQty في الـ query، اجعله true افتراضياً
            // (لأن checkbox غير المُفعّل لا يُرسل في الـ form GET)
            string? includeZeroQtyStr = Request.Query["includeZeroQty"].FirstOrDefault();
            if (string.IsNullOrEmpty(includeZeroQtyStr))
            {
                includeZeroQty = true; // الافتراضي: عرض الصفر
                ViewBag.IncludeZeroQty = true;
            }

            // عند عدم تحديد تاريخ: استخدام الشهر الحالي افتراضياً لعرض المبيعات
            var today = DateTime.Today;
            if (!fromDate.HasValue && !toDate.HasValue)
            {
                fromDate = new DateTime(today.Year, today.Month, 1);
                toDate = today;
                ViewBag.FromDate = fromDate;
                ViewBag.ToDate = toDate;
            }

            // =========================================================
            // 4) بناء الاستعلام الأساسي للأصناف (عند طلب التقرير)
            // =========================================================
            var productsQuery = _context.Products
                .AsNoTracking()
                .Include(p => p.Category)
                .AsQueryable();

            // فلتر البحث (اسم الصنف أو الكود)
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                productsQuery = productsQuery.Where(p =>
                    p.ProdName.Contains(s) ||
                    (p.Barcode != null && p.Barcode.Contains(s)) ||
                    (p.ProdId.ToString() == s));
            }

            // فلتر الفئة
            if (categoryId.HasValue && categoryId.Value > 0)
            {
                productsQuery = productsQuery.Where(p => p.CategoryId == categoryId.Value);
            }

            // فلتر الأصناف النشطة فقط (افتراضي)
            productsQuery = productsQuery.Where(p => p.IsActive == true);

            // =========================================================
            // 5) تحميل قائمة الأصناف المرشحة
            // =========================================================
            var productIds = await productsQuery.Select(p => p.ProdId).ToListAsync();

            if (productIds.Count == 0)
            {
                ViewBag.ReportData = new List<ProductBalanceReportDto>();
                ViewBag.TotalCost = 0m;
                return View();
            }

            // =========================================================
            // 6) تحميل البيانات بشكل مجمع (Bulk Loading) - تحسين الأداء
            // =========================================================
            
            // 6.1) تحميل جميع Products دفعة واحدة
            var productsDict = await productsQuery
                .Select(p => new
                {
                    p.ProdId,
                    p.ProdName,
                    CategoryName = p.Category != null ? p.Category.CategoryName : "",
                    p.PriceRetail
                })
                .ToDictionaryAsync(p => p.ProdId);

            // 6.2) تحميل جميع StockBatches دفعة واحدة (GroupBy ProdId)
            var stockBatchesQ = _context.StockBatches
                .AsNoTracking()
                .Where(sb => productIds.Contains(sb.ProdId));

            if (warehouseId.HasValue && warehouseId.Value > 0)
            {
                stockBatchesQ = stockBatchesQ.Where(sb => sb.WarehouseId == warehouseId.Value);
            }

            var stockQuantities = await stockBatchesQ
                .GroupBy(sb => sb.ProdId)
                .Select(g => new { ProdId = g.Key, TotalQty = g.Sum(sb => sb.QtyOnHand) })
                .ToDictionaryAsync(x => x.ProdId, x => x.TotalQty);

            // 6.3) تحميل جميع StockLedger للخصم المرجح والتكلفة دفعة واحدة
            var stockLedgerDiscount = await _context.StockLedger
                .AsNoTracking()
                .Where(x =>
                    productIds.Contains(x.ProdId) &&
                    x.SourceType == "Purchase" &&
                    (x.RemainingQty ?? 0) > 0)
                .GroupBy(x => x.ProdId)
                .Select(g => new
                {
                    ProdId = g.Key,
                    TotalRemaining = g.Sum(x => (decimal)(x.RemainingQty ?? 0)),
                    WeightedDiscount = g.Sum(x => (decimal)(x.RemainingQty ?? 0) * ((decimal?)(x.PurchaseDiscount) ?? 0m)),
                    WeightedCost = g.Sum(x => (decimal)(x.RemainingQty ?? 0) * x.UnitCost)
                })
                .ToDictionaryAsync(x => x.ProdId);

            // 6.4) تحميل جميع المبيعات دفعة واحدة (إن وُجدت فلاتر تاريخ)
            Dictionary<int, decimal> salesQuantities = new Dictionary<int, decimal>();
            if (fromDate.HasValue || toDate.HasValue)
            {
                var salesQuery = _context.SalesInvoiceLines
                    .AsNoTracking()
                    .Include(sil => sil.SalesInvoice)
                    .Where(sil =>
                        productIds.Contains(sil.ProdId) &&
                        sil.SalesInvoice.IsPosted);

                if (warehouseId.HasValue && warehouseId.Value > 0)
                {
                    salesQuery = salesQuery.Where(sil => sil.SalesInvoice.WarehouseId == warehouseId.Value);
                }

                if (fromDate.HasValue)
                {
                    var from = fromDate.Value.Date;
                    salesQuery = salesQuery.Where(sil => sil.SalesInvoice.SIDate >= from);
                }

                if (toDate.HasValue)
                {
                    var to = toDate.Value.Date.AddDays(1);
                    salesQuery = salesQuery.Where(sil => sil.SalesInvoice.SIDate < to);
                }

                salesQuantities = await salesQuery
                    .GroupBy(sil => sil.ProdId)
                    .Select(g => new { ProdId = g.Key, TotalQty = g.Sum(sil => (decimal?)sil.Qty) ?? 0m })
                    .ToDictionaryAsync(x => x.ProdId, x => x.TotalQty);
            }

            // 6.4.1) تحميل مرتجعات البيع (خصم من كمية المبيعات) عند وجود فلاتر تاريخ
            Dictionary<int, decimal> returnQuantities = new Dictionary<int, decimal>();
            if (fromDate.HasValue || toDate.HasValue)
            {
                var returnsQuery = _context.SalesReturnLines
                    .AsNoTracking()
                    .Include(srl => srl.SalesReturn)
                    .Where(srl => productIds.Contains(srl.ProdId) && srl.SalesReturn != null && srl.SalesReturn.IsPosted);
                if (warehouseId.HasValue && warehouseId.Value > 0)
                    returnsQuery = returnsQuery.Where(srl => srl.SalesReturn!.WarehouseId == warehouseId.Value);
                if (fromDate.HasValue)
                    returnsQuery = returnsQuery.Where(srl => srl.SalesReturn!.SRDate >= fromDate.Value.Date);
                if (toDate.HasValue)
                    returnsQuery = returnsQuery.Where(srl => srl.SalesReturn!.SRDate < toDate.Value.Date.AddDays(1));
                returnQuantities = await returnsQuery
                    .GroupBy(srl => srl.ProdId)
                    .Select(g => new { ProdId = g.Key, TotalQty = g.Sum(srl => (decimal?)srl.Qty) ?? 0m })
                    .ToDictionaryAsync(x => x.ProdId, x => x.TotalQty);
            }

            // 6.5) بناء reportData من البيانات المحملة
            var reportData = new List<ProductBalanceReportDto>();

            foreach (var prodId in productIds)
            {
                if (!productsDict.TryGetValue(prodId, out var product)) continue;

                // الكمية الحالية
                int currentQty = stockQuantities.TryGetValue(prodId, out var qty) ? qty : 0;

                // فلتر الكميات الصفرية
                if (!includeZeroQty && currentQty == 0)
                    continue;

                // الخصم المرجح
                decimal weightedDiscount = 0m;
                if (stockLedgerDiscount.TryGetValue(prodId, out var discountData))
                {
                    if (discountData.TotalRemaining > 0)
                    {
                        weightedDiscount = discountData.WeightedDiscount / discountData.TotalRemaining;
                    }
                }

                // صافي المبيعات (المبيعات − مرتجعات البيع)
                decimal salesQty = salesQuantities.TryGetValue(prodId, out var sales) ? sales : 0m;
                if (returnQuantities.TryGetValue(prodId, out var retQty))
                    salesQty = Math.Max(0m, salesQty - retQty);

                // تكلفة العلبة
                decimal unitCost = 0m;
                if (stockLedgerDiscount.TryGetValue(prodId, out var costData))
                {
                    if (costData.TotalRemaining > 0)
                    {
                        unitCost = costData.WeightedCost / costData.TotalRemaining;
                    }
                }

                // التكلفة الإجمالية
                decimal totalCost = currentQty * unitCost;

                reportData.Add(new ProductBalanceReportDto
                {
                    ProdId = prodId,
                    ProdCode = prodId.ToString(),
                    ProdName = product.ProdName ?? "",
                    CategoryName = product.CategoryName ?? "",
                    CurrentQty = currentQty,
                    WeightedDiscount = weightedDiscount,
                    SalesQty = salesQty,
                    PriceRetail = product.PriceRetail,
                    UnitCost = unitCost,
                    TotalCost = totalCost
                });
            }

            // =========================================================
            // 7) الترتيب
            // =========================================================
            bool isDesc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
            switch (sortBy?.ToLowerInvariant())
            {
                case "code":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.ProdCode).ToList()
                        : reportData.OrderBy(r => r.ProdCode).ToList();
                    break;
                case "qty":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.CurrentQty).ToList()
                        : reportData.OrderBy(r => r.CurrentQty).ToList();
                    break;
                case "sales":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.SalesQty).ToList()
                        : reportData.OrderBy(r => r.SalesQty).ToList();
                    break;
                case "cost":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.TotalCost).ToList()
                        : reportData.OrderBy(r => r.TotalCost).ToList();
                    break;
                default: // "name"
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.ProdName).ToList()
                        : reportData.OrderBy(r => r.ProdName).ToList();
                    break;
            }

            // =========================================================
            // 8) حساب المجاميع الإجمالية (من كل البيانات - قبل Pagination)
            // =========================================================
            int totalQty = reportData.Sum(r => r.CurrentQty);
            decimal totalPriceRetail = reportData.Sum(r => r.PriceRetail);
            decimal totalSalesQty = reportData.Sum(r => r.SalesQty);
            decimal totalUnitCost = reportData.Sum(r => r.UnitCost);
            decimal totalCostSum = reportData.Sum(r => r.TotalCost);

            int totalCount = reportData.Count; // إجمالي عدد الأصناف (قبل Pagination)

            // =========================================================
            // 9) Pagination (اختياري: 200, 500, 1000, 5000, أو الكل)
            // =========================================================
            if (pageSize > 0 && pageSize < totalCount)
            {
                // تطبيق Pagination
                if (page < 1) page = 1;
                int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
                if (page > totalPages) page = totalPages;

                int skip = (page - 1) * pageSize;
                reportData = reportData.Skip(skip).Take(pageSize).ToList();

                ViewBag.Page = page;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalPages = totalPages;
                ViewBag.TotalCount = totalCount;
            }
            else
            {
                // عرض الكل (لا Pagination)
                ViewBag.Page = 1;
                ViewBag.PageSize = totalCount;
                ViewBag.TotalPages = 1;
                ViewBag.TotalCount = totalCount;
            }

            ViewBag.ReportData = reportData;
            ViewBag.TotalQty = totalQty;
            ViewBag.TotalPriceRetail = totalPriceRetail;
            ViewBag.TotalSalesQty = totalSalesQty;
            ViewBag.TotalUnitCost = totalUnitCost;
            ViewBag.TotalCost = totalCostSum;

            return View();
        }

        // =========================================================
        // تقرير: أرصدة العملاء
        // يعرض العميل، الرصيد الحالي، الحد الائتماني، المبيعات والمشتريات بين تاريخين
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> CustomerBalances(
            string? search,
            string? partyCategory,
            int? governorateId,
            DateTime? fromDate,
            DateTime? toDate,
            bool includeZeroBalance = false,
            string? sortBy = "name",
            string? sortDir = "asc",
            bool loadReport = false,
            int page = 1,
            int pageSize = 200)
        {
            // =========================================================
            // 1) تجهيز القوائم المنسدلة (Governorates)
            // =========================================================
            var governorates = await _context.Governorates
                .AsNoTracking()
                .OrderBy(g => g.GovernorateName)
                .Select(g => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                {
                    Value = g.GovernorateId.ToString(),
                    Text = g.GovernorateName
                })
                .ToListAsync();

            ViewBag.Governorates = governorates;

            // =========================================================
            // 1.1) تحميل قائمة العملاء للأوتوكومبليت (datalist)
            // =========================================================
            var customersAuto = await _context.Customers
                .AsNoTracking()
                .OrderBy(c => c.CustomerName)
                .Select(c => new
                {
                    Id = c.CustomerId,
                    Name = c.CustomerName ?? string.Empty,
                    Phone = c.Phone1 ?? string.Empty,
                    Category = c.PartyCategory ?? string.Empty
                })
                .ToListAsync();
            ViewBag.CustomersAuto = customersAuto;

            // =========================================================
            // 2) تجهيز الفلاتر
            // =========================================================
            ViewBag.Search = search ?? "";
            ViewBag.PartyCategory = partyCategory;
            ViewBag.GovernorateId = governorateId;
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;
            ViewBag.IncludeZeroBalance = includeZeroBalance;
            ViewBag.SortBy = sortBy;
            ViewBag.SortDir = sortDir;

            // =========================================================
            // 3) تحميل البيانات فقط عند الضغط على "تجميع التقرير"
            // =========================================================
            if (!loadReport)
            {
                ViewBag.IncludeZeroBalance = true;
                ViewBag.ReportData = new List<CustomerBalanceReportDto>();
                ViewBag.TotalBalance = 0m;
                ViewBag.TotalDebit = 0m;
                ViewBag.TotalCredit = 0m;
                ViewBag.TotalSales = 0m;
                ViewBag.TotalPurchases = 0m;
                ViewBag.TotalReturns = 0m;
                ViewBag.Page = 1;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalPages = 1;
                ViewBag.TotalCount = 0;
                return View();
            }

            // عند تحميل التقرير: إذا لم يتم تحديد includeZeroBalance في الـ query، اجعله true افتراضياً
            string? includeZeroBalanceStr = Request.Query["includeZeroBalance"].FirstOrDefault();
            if (string.IsNullOrEmpty(includeZeroBalanceStr))
            {
                includeZeroBalance = true;
                ViewBag.IncludeZeroBalance = true;
            }

            // =========================================================
            // 4) بناء الاستعلام الأساسي للعملاء (عند طلب التقرير)
            // =========================================================
            var customersQuery = _context.Customers
                .AsNoTracking()
                .AsQueryable();

            // فلتر البحث (اسم العميل أو الكود)
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                customersQuery = customersQuery.Where(c =>
                    (c.CustomerName != null && c.CustomerName.Contains(s)) ||
                    (c.Phone1 != null && c.Phone1.Contains(s)) ||
                    (c.CustomerId.ToString() == s));
            }

            // فلتر فئة العميل
            if (!string.IsNullOrWhiteSpace(partyCategory))
            {
                customersQuery = customersQuery.Where(c => c.PartyCategory == partyCategory);
            }

            // فلتر المحافظة
            if (governorateId.HasValue && governorateId.Value > 0)
            {
                customersQuery = customersQuery.Where(c => c.GovernorateId == governorateId.Value);
            }

            // فلتر العملاء النشطين فقط (افتراضي)
            customersQuery = customersQuery.Where(c => c.IsActive == true);

            // =========================================================
            // 5) تحميل قائمة العملاء المرشحة
            // =========================================================
            var customerIds = await customersQuery.Select(c => c.CustomerId).ToListAsync();

            if (customerIds.Count == 0)
            {
                ViewBag.ReportData = new List<CustomerBalanceReportDto>();
                ViewBag.TotalBalance = 0m;
                ViewBag.TotalSales = 0m;
                ViewBag.TotalPurchases = 0m;
                ViewBag.TotalReturns = 0m;
                ViewBag.Page = 1;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalPages = 1;
                ViewBag.TotalCount = 0;
                return View();
            }

            // =========================================================
            // 6) تحميل البيانات بشكل مجمع (Bulk Loading) - تحسين الأداء
            // =========================================================

            // 6.1) تحميل جميع Customers دفعة واحدة
            var customersDict = await customersQuery
                .Select(c => new
                {
                    c.CustomerId,
                    c.CustomerName,
                    c.PartyCategory,
                    c.Phone1,
                    c.CurrentBalance,
                    c.CreditLimit
                })
                .ToDictionaryAsync(c => c.CustomerId);

            // 6.2) تحميل المبيعات (دائماً - إذا وُجدت فلاتر تاريخ نطبقها)
            Dictionary<int, decimal> salesTotals = new Dictionary<int, decimal>();
            {
                var salesQuery = _context.LedgerEntries
                    .AsNoTracking()
                    .Where(e =>
                        e.CustomerId.HasValue &&
                        customerIds.Contains(e.CustomerId.Value) &&
                        e.SourceType == LedgerSourceType.SalesInvoice &&
                        e.LineNo == 1 &&
                        e.PostVersion > 0 &&
                        !_context.LedgerEntries.Any(rev =>
                            rev.CustomerId == e.CustomerId &&
                            rev.SourceType == LedgerSourceType.SalesInvoice &&
                            rev.SourceId == e.SourceId &&
                            rev.LineNo == 9001)); // استثناء الفواتير المحذوفة

                if (fromDate.HasValue)
                {
                    var from = fromDate.Value.Date;
                    salesQuery = salesQuery.Where(e => e.EntryDate >= from);
                }

                if (toDate.HasValue)
                {
                    var to = toDate.Value.Date.AddDays(1);
                    salesQuery = salesQuery.Where(e => e.EntryDate < to);
                }

                salesTotals = await salesQuery
                    .GroupBy(e => e.CustomerId!.Value)
                    .Select(g => new { CustomerId = g.Key, TotalSales = g.Sum(e => e.Debit) })
                    .ToDictionaryAsync(x => x.CustomerId, x => x.TotalSales);
            }

            // 6.3) تحميل المشتريات (دائماً - إذا وُجدت فلاتر تاريخ نطبقها)
            Dictionary<int, decimal> purchasesTotals = new Dictionary<int, decimal>();
            {
                // نحسب آخر PostVersion لكل فاتورة مشتريات
                var maxPostVersions = await _context.LedgerEntries
                    .AsNoTracking()
                    .Where(e =>
                        e.CustomerId.HasValue &&
                        customerIds.Contains(e.CustomerId.Value) &&
                        e.SourceType == LedgerSourceType.PurchaseInvoice &&
                        e.LineNo == 2 &&
                        e.LineNo < 9000 &&
                        e.PostVersion > 0 &&
                        e.Description != null &&
                        !e.Description.Contains("عكس"))
                    .GroupBy(e => e.SourceId)
                    .Select(g => new { SourceId = g.Key, MaxPostVersion = g.Max(e => e.PostVersion) })
                    .ToDictionaryAsync(x => x.SourceId, x => x.MaxPostVersion);

                var sourceIds = maxPostVersions.Keys.ToList();
                if (sourceIds.Count > 0)
                {
                    var purchasesQuery = _context.LedgerEntries
                        .AsNoTracking()
                        .Where(e =>
                            e.CustomerId.HasValue &&
                            customerIds.Contains(e.CustomerId.Value) &&
                            e.SourceType == LedgerSourceType.PurchaseInvoice &&
                            e.LineNo == 2 &&
                            e.LineNo < 9000 &&
                            e.PostVersion > 0 &&
                            e.Description != null &&
                            !e.Description.Contains("عكس") &&
                            sourceIds.Contains(e.SourceId!.Value));

                    if (fromDate.HasValue)
                    {
                        var from = fromDate.Value.Date;
                        purchasesQuery = purchasesQuery.Where(e => e.EntryDate >= from);
                    }

                    if (toDate.HasValue)
                    {
                        var to = toDate.Value.Date.AddDays(1);
                        purchasesQuery = purchasesQuery.Where(e => e.EntryDate < to);
                    }

                    var allPurchasesEntries = await purchasesQuery.ToListAsync();

                    // تصفية في الذاكرة: فقط القيود التي تطابق آخر PostVersion
                    var filteredEntries = allPurchasesEntries
                        .Where(e =>
                            e.SourceId.HasValue &&
                            maxPostVersions.ContainsKey(e.SourceId.Value) &&
                            maxPostVersions[e.SourceId.Value] == e.PostVersion)
                        .GroupBy(e => e.CustomerId!.Value)
                        .Select(g => new { CustomerId = g.Key, TotalPurchases = g.Sum(e => e.Credit) })
                        .ToList();

                    purchasesTotals = filteredEntries.ToDictionary(x => x.CustomerId, x => x.TotalPurchases);
                }
            }

            // 6.3.1) تحميل المرتجعات (مرتجعات البيع + مرتجعات الشراء) من LedgerEntries
            Dictionary<int, decimal> returnsTotals = new Dictionary<int, decimal>();
            {
                var salesReturnQ = _context.LedgerEntries
                    .AsNoTracking()
                    .Where(e =>
                        e.CustomerId.HasValue &&
                        customerIds.Contains(e.CustomerId.Value) &&
                        e.SourceType == LedgerSourceType.SalesReturn &&
                        e.LineNo == 2 &&
                        e.PostVersion > 0);
                if (fromDate.HasValue) salesReturnQ = salesReturnQ.Where(e => e.EntryDate >= fromDate.Value.Date);
                if (toDate.HasValue) salesReturnQ = salesReturnQ.Where(e => e.EntryDate < toDate.Value.Date.AddDays(1));
                var srByCustomer = await salesReturnQ
                    .GroupBy(e => e.CustomerId!.Value)
                    .Select(g => new { CustomerId = g.Key, Sum = g.Sum(e => e.Credit) })
                    .ToDictionaryAsync(x => x.CustomerId, x => x.Sum);

                var purchaseReturnQ = _context.LedgerEntries
                    .AsNoTracking()
                    .Where(e =>
                        e.CustomerId.HasValue &&
                        customerIds.Contains(e.CustomerId.Value) &&
                        e.SourceType == LedgerSourceType.PurchaseReturn &&
                        e.LineNo == 1 &&
                        e.PostVersion > 0);
                if (fromDate.HasValue) purchaseReturnQ = purchaseReturnQ.Where(e => e.EntryDate >= fromDate.Value.Date);
                if (toDate.HasValue) purchaseReturnQ = purchaseReturnQ.Where(e => e.EntryDate < toDate.Value.Date.AddDays(1));
                var prByCustomer = await purchaseReturnQ
                    .GroupBy(e => e.CustomerId!.Value)
                    .Select(g => new { CustomerId = g.Key, Sum = g.Sum(e => e.Debit) })
                    .ToDictionaryAsync(x => x.CustomerId, x => x.Sum);

                foreach (var cid in customerIds)
                {
                    decimal sr = srByCustomer.TryGetValue(cid, out var s) ? s : 0m;
                    decimal pr = prByCustomer.TryGetValue(cid, out var p) ? p : 0m;
                    returnsTotals[cid] = sr + pr;
                }
            }

            // 6.4) بناء reportData من البيانات المحملة
            var reportData = new List<CustomerBalanceReportDto>();

            foreach (var customerId in customerIds)
            {
                if (!customersDict.TryGetValue(customerId, out var customer)) continue;

                decimal currentBalance = customer.CurrentBalance;
                decimal creditLimit = customer.CreditLimit;

                // فلتر الأرصدة الصفرية
                if (!includeZeroBalance && currentBalance == 0)
                    continue;

                decimal totalSales = salesTotals.TryGetValue(customerId, out var sales) ? sales : 0m;
                decimal totalPurchases = purchasesTotals.TryGetValue(customerId, out var purchases) ? purchases : 0m;
                decimal totalReturns = returnsTotals.TryGetValue(customerId, out var ret) ? ret : 0m;
                decimal availableCredit = creditLimit - currentBalance;

                reportData.Add(new CustomerBalanceReportDto
                {
                    CustomerId = customerId,
                    CustomerCode = customerId.ToString(),
                    CustomerName = customer.CustomerName ?? "",
                    PartyCategory = customer.PartyCategory ?? "",
                    Phone1 = customer.Phone1 ?? "",
                    CurrentBalance = currentBalance,
                    CreditLimit = creditLimit,
                    TotalSales = totalSales,
                    TotalPurchases = totalPurchases,
                    TotalReturns = totalReturns,
                    AvailableCredit = availableCredit
                });
            }

            // =========================================================
            // 7) الترتيب
            // =========================================================
            bool isDesc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
            switch (sortBy?.ToLowerInvariant())
            {
                case "code":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.CustomerCode).ToList()
                        : reportData.OrderBy(r => r.CustomerCode).ToList();
                    break;
                case "balance":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.CurrentBalance).ToList()
                        : reportData.OrderBy(r => r.CurrentBalance).ToList();
                    break;
                case "sales":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.TotalSales).ToList()
                        : reportData.OrderBy(r => r.TotalSales).ToList();
                    break;
                case "purchases":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.TotalPurchases).ToList()
                        : reportData.OrderBy(r => r.TotalPurchases).ToList();
                    break;
                case "returns":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.TotalReturns).ToList()
                        : reportData.OrderBy(r => r.TotalReturns).ToList();
                    break;
                default: // "name"
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.CustomerName).ToList()
                        : reportData.OrderBy(r => r.CustomerName).ToList();
                    break;
            }

            // =========================================================
            // 8) حساب المجاميع الإجمالية (من كل البيانات - قبل Pagination)
            // =========================================================
            decimal totalBalance = reportData.Sum(r => r.CurrentBalance);
            decimal totalDebitSum = reportData.Sum(r => r.CurrentBalance > 0 ? r.CurrentBalance : 0m);
            decimal totalCreditSum = reportData.Sum(r => r.CurrentBalance < 0 ? Math.Abs(r.CurrentBalance) : 0m);
            decimal totalSalesSum = reportData.Sum(r => r.TotalSales);
            decimal totalPurchasesSum = reportData.Sum(r => r.TotalPurchases);
            decimal totalReturnsSum = reportData.Sum(r => r.TotalReturns);
            decimal totalCreditLimit = reportData.Sum(r => r.CreditLimit);
            decimal totalAvailableCredit = reportData.Sum(r => r.AvailableCredit);

            int totalCount = reportData.Count; // إجمالي عدد العملاء (قبل Pagination)

            // =========================================================
            // 9) Pagination (اختياري: 200, 500, 1000, 5000, أو الكل)
            // =========================================================
            if (pageSize > 0 && pageSize < totalCount)
            {
                if (page < 1) page = 1;
                int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
                if (page > totalPages) page = totalPages;

                int skip = (page - 1) * pageSize;
                reportData = reportData.Skip(skip).Take(pageSize).ToList();

                ViewBag.Page = page;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalPages = totalPages;
                ViewBag.TotalCount = totalCount;
            }
            else
            {
                ViewBag.Page = 1;
                ViewBag.PageSize = totalCount;
                ViewBag.TotalPages = 1;
                ViewBag.TotalCount = totalCount;
            }

            ViewBag.ReportData = reportData;
            ViewBag.TotalBalance = totalBalance;
            ViewBag.TotalDebit = totalDebitSum;
            ViewBag.TotalCredit = totalCreditSum;
            ViewBag.TotalSales = totalSalesSum;
            ViewBag.TotalPurchases = totalPurchasesSum;
            ViewBag.TotalReturns = totalReturnsSum;
            ViewBag.TotalCreditLimit = totalCreditLimit;
            ViewBag.TotalAvailableCredit = totalAvailableCredit;

            return View();
        }

        // =========================================================
        // تصدير Excel: أرصدة العملاء (نفس فلاتر CustomerBalances)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> ExportCustomerBalances(
            string? search,
            string? partyCategory,
            int? governorateId,
            DateTime? fromDate,
            DateTime? toDate,
            bool includeZeroBalance = false,
            string? sortBy = "name",
            string? sortDir = "asc")
        {
            // عند التصدير: إذا لم يتم تحديد includeZeroBalance، اجعله true افتراضياً
            string? includeZeroBalanceStr = Request.Query["includeZeroBalance"].FirstOrDefault();
            if (string.IsNullOrEmpty(includeZeroBalanceStr))
            {
                includeZeroBalance = true;
            }

            // بناء الاستعلام (نفس منطق CustomerBalances)
            var customersQuery = _context.Customers
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                customersQuery = customersQuery.Where(c =>
                    (c.CustomerName != null && c.CustomerName.Contains(s)) ||
                    (c.Phone1 != null && c.Phone1.Contains(s)) ||
                    (c.CustomerId.ToString() == s));
            }

            if (!string.IsNullOrWhiteSpace(partyCategory))
            {
                customersQuery = customersQuery.Where(c => c.PartyCategory == partyCategory);
            }

            if (governorateId.HasValue && governorateId.Value > 0)
            {
                customersQuery = customersQuery.Where(c => c.GovernorateId == governorateId.Value);
            }

            customersQuery = customersQuery.Where(c => c.IsActive == true);

            var customerIds = await customersQuery.Select(c => c.CustomerId).ToListAsync();
            if (customerIds.Count == 0)
            {
                return BadRequest("لا توجد بيانات للتصدير");
            }

            // تحميل البيانات بشكل مجمع (نفس منطق CustomerBalances)
            var customersDict = await customersQuery
                .Select(c => new
                {
                    c.CustomerId,
                    c.CustomerName,
                    c.PartyCategory,
                    c.Phone1,
                    c.CurrentBalance,
                    c.CreditLimit
                })
                .ToDictionaryAsync(c => c.CustomerId);

            Dictionary<int, decimal> salesTotals = new Dictionary<int, decimal>();
            if (fromDate.HasValue || toDate.HasValue)
            {
                var salesQuery = _context.LedgerEntries
                    .AsNoTracking()
                    .Where(e =>
                        e.CustomerId.HasValue &&
                        customerIds.Contains(e.CustomerId.Value) &&
                        e.SourceType == LedgerSourceType.SalesInvoice &&
                        e.LineNo == 1 &&
                        e.PostVersion > 0 &&
                        !_context.LedgerEntries.Any(rev =>
                            rev.CustomerId == e.CustomerId &&
                            rev.SourceType == LedgerSourceType.SalesInvoice &&
                            rev.SourceId == e.SourceId &&
                            rev.LineNo == 9001));

                if (fromDate.HasValue)
                {
                    var from = fromDate.Value.Date;
                    salesQuery = salesQuery.Where(e => e.EntryDate >= from);
                }

                if (toDate.HasValue)
                {
                    var to = toDate.Value.Date.AddDays(1);
                    salesQuery = salesQuery.Where(e => e.EntryDate < to);
                }

                salesTotals = await salesQuery
                    .GroupBy(e => e.CustomerId!.Value)
                    .Select(g => new { CustomerId = g.Key, TotalSales = g.Sum(e => e.Debit) })
                    .ToDictionaryAsync(x => x.CustomerId, x => x.TotalSales);
            }

            Dictionary<int, decimal> purchasesTotals = new Dictionary<int, decimal>();
            if (fromDate.HasValue || toDate.HasValue)
            {
                var maxPostVersions = await _context.LedgerEntries
                    .AsNoTracking()
                    .Where(e =>
                        e.CustomerId.HasValue &&
                        customerIds.Contains(e.CustomerId.Value) &&
                        e.SourceType == LedgerSourceType.PurchaseInvoice &&
                        e.LineNo == 2 &&
                        e.LineNo < 9000 &&
                        e.PostVersion > 0 &&
                        e.Description != null &&
                        !e.Description.Contains("عكس"))
                    .GroupBy(e => e.SourceId)
                    .Select(g => new { SourceId = g.Key, MaxPostVersion = g.Max(e => e.PostVersion) })
                    .ToDictionaryAsync(x => x.SourceId, x => x.MaxPostVersion);

                var sourceIds = maxPostVersions.Keys.ToList();
                if (sourceIds.Count > 0)
                {
                    var purchasesQuery = _context.LedgerEntries
                        .AsNoTracking()
                        .Where(e =>
                            e.CustomerId.HasValue &&
                            customerIds.Contains(e.CustomerId.Value) &&
                            e.SourceType == LedgerSourceType.PurchaseInvoice &&
                            e.LineNo == 2 &&
                            e.LineNo < 9000 &&
                            e.PostVersion > 0 &&
                            e.Description != null &&
                            !e.Description.Contains("عكس") &&
                            sourceIds.Contains(e.SourceId!.Value));

                    if (fromDate.HasValue)
                    {
                        var from = fromDate.Value.Date;
                        purchasesQuery = purchasesQuery.Where(e => e.EntryDate >= from);
                    }

                    if (toDate.HasValue)
                    {
                        var to = toDate.Value.Date.AddDays(1);
                        purchasesQuery = purchasesQuery.Where(e => e.EntryDate < to);
                    }

                    var allPurchasesEntries = await purchasesQuery.ToListAsync();

                    var filteredEntries = allPurchasesEntries
                        .Where(e =>
                            e.SourceId.HasValue &&
                            maxPostVersions.ContainsKey(e.SourceId.Value) &&
                            maxPostVersions[e.SourceId.Value] == e.PostVersion)
                        .GroupBy(e => e.CustomerId!.Value)
                        .Select(g => new { CustomerId = g.Key, TotalPurchases = g.Sum(e => e.Credit) })
                        .ToList();

                    purchasesTotals = filteredEntries.ToDictionary(x => x.CustomerId, x => x.TotalPurchases);
                }
            }

            Dictionary<int, decimal> returnsTotals = new Dictionary<int, decimal>();
            {
                var salesReturnQ = _context.LedgerEntries.AsNoTracking()
                    .Where(e => e.CustomerId.HasValue && customerIds.Contains(e.CustomerId.Value) &&
                        e.SourceType == LedgerSourceType.SalesReturn && e.LineNo == 2 && e.PostVersion > 0);
                if (fromDate.HasValue) salesReturnQ = salesReturnQ.Where(e => e.EntryDate >= fromDate.Value.Date);
                if (toDate.HasValue) salesReturnQ = salesReturnQ.Where(e => e.EntryDate < toDate.Value.Date.AddDays(1));
                var srByCustomer = await salesReturnQ
                    .GroupBy(e => e.CustomerId!.Value)
                    .Select(g => new { CustomerId = g.Key, Sum = g.Sum(e => e.Credit) })
                    .ToDictionaryAsync(x => x.CustomerId, x => x.Sum);
                var purchaseReturnQ = _context.LedgerEntries.AsNoTracking()
                    .Where(e => e.CustomerId.HasValue && customerIds.Contains(e.CustomerId.Value) &&
                        e.SourceType == LedgerSourceType.PurchaseReturn && e.LineNo == 1 && e.PostVersion > 0);
                if (fromDate.HasValue) purchaseReturnQ = purchaseReturnQ.Where(e => e.EntryDate >= fromDate.Value.Date);
                if (toDate.HasValue) purchaseReturnQ = purchaseReturnQ.Where(e => e.EntryDate < toDate.Value.Date.AddDays(1));
                var prByCustomer = await purchaseReturnQ
                    .GroupBy(e => e.CustomerId!.Value)
                    .Select(g => new { CustomerId = g.Key, Sum = g.Sum(e => e.Debit) })
                    .ToDictionaryAsync(x => x.CustomerId, x => x.Sum);
                foreach (var cid in customerIds)
                {
                    decimal sr = srByCustomer.TryGetValue(cid, out var s) ? s : 0m;
                    decimal pr = prByCustomer.TryGetValue(cid, out var p) ? p : 0m;
                    returnsTotals[cid] = sr + pr;
                }
            }

            var reportData = new List<CustomerBalanceReportDto>();

            foreach (var customerId in customerIds)
            {
                if (!customersDict.TryGetValue(customerId, out var customer)) continue;

                decimal currentBalance = customer.CurrentBalance;
                decimal creditLimit = customer.CreditLimit;

                if (!includeZeroBalance && currentBalance == 0)
                    continue;

                decimal totalSales = salesTotals.TryGetValue(customerId, out var sales) ? sales : 0m;
                decimal totalPurchases = purchasesTotals.TryGetValue(customerId, out var purchases) ? purchases : 0m;
                decimal totalReturns = returnsTotals.TryGetValue(customerId, out var ret) ? ret : 0m;
                decimal availableCredit = creditLimit - currentBalance;

                reportData.Add(new CustomerBalanceReportDto
                {
                    CustomerId = customerId,
                    CustomerCode = customerId.ToString(),
                    CustomerName = customer.CustomerName ?? "",
                    PartyCategory = customer.PartyCategory ?? "",
                    Phone1 = customer.Phone1 ?? "",
                    CurrentBalance = currentBalance,
                    CreditLimit = creditLimit,
                    TotalSales = totalSales,
                    TotalPurchases = totalPurchases,
                    TotalReturns = totalReturns,
                    AvailableCredit = availableCredit
                });
            }

            // الترتيب (نفس منطق CustomerBalances)
            bool isDesc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
            switch (sortBy?.ToLowerInvariant())
            {
                case "code":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.CustomerCode).ToList()
                        : reportData.OrderBy(r => r.CustomerCode).ToList();
                    break;
                case "balance":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.CurrentBalance).ToList()
                        : reportData.OrderBy(r => r.CurrentBalance).ToList();
                    break;
                case "sales":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.TotalSales).ToList()
                        : reportData.OrderBy(r => r.TotalSales).ToList();
                    break;
                case "purchases":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.TotalPurchases).ToList()
                        : reportData.OrderBy(r => r.TotalPurchases).ToList();
                    break;
                case "returns":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.TotalReturns).ToList()
                        : reportData.OrderBy(r => r.TotalReturns).ToList();
                    break;
                default: // "name"
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.CustomerName).ToList()
                        : reportData.OrderBy(r => r.CustomerName).ToList();
                    break;
            }

            // تصدير Excel (كل البيانات بدون Pagination)
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("أرصدة العملاء");

            int row = 1;

            // عناوين الأعمدة
            worksheet.Cell(row, 1).Value = "الكود";
            worksheet.Cell(row, 2).Value = "اسم العميل";
            worksheet.Cell(row, 3).Value = "فئة العميل";
            worksheet.Cell(row, 4).Value = "الهاتف";
            worksheet.Cell(row, 5).Value = "مدين";
            worksheet.Cell(row, 6).Value = "دائن";
            worksheet.Cell(row, 7).Value = "الحد الائتماني";
            worksheet.Cell(row, 8).Value = "المبيعات";
            worksheet.Cell(row, 9).Value = "المشتريات";
            worksheet.Cell(row, 10).Value = "المرتجعات";
            worksheet.Cell(row, 11).Value = "الائتمان المتاح";

            worksheet.Range(row, 1, row, 11).Style.Font.Bold = true;

            // البيانات
            row = 2;
            foreach (var item in reportData)
            {
                decimal debitVal = item.CurrentBalance > 0 ? item.CurrentBalance : 0m;
                decimal creditVal = item.CurrentBalance < 0 ? Math.Abs(item.CurrentBalance) : 0m;
                worksheet.Cell(row, 1).Value = item.CustomerCode;
                worksheet.Cell(row, 2).Value = item.CustomerName;
                worksheet.Cell(row, 3).Value = item.PartyCategory;
                worksheet.Cell(row, 4).Value = item.Phone1;
                worksheet.Cell(row, 5).Value = debitVal;
                worksheet.Cell(row, 6).Value = creditVal;
                worksheet.Cell(row, 7).Value = item.CreditLimit;
                worksheet.Cell(row, 8).Value = item.TotalSales;
                worksheet.Cell(row, 9).Value = item.TotalPurchases;
                worksheet.Cell(row, 10).Value = item.TotalReturns;
                worksheet.Cell(row, 11).Value = item.AvailableCredit;
                row++;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new System.IO.MemoryStream();
            workbook.SaveAs(stream);
            var fileName = $"CustomerBalances_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
            return File(stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        // =========================================================
        // تصدير Excel: أرصدة الأصناف (نفس فلاتر ProductBalances)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> ExportProductBalances(
            string? search,
            int? categoryId,
            int? warehouseId,
            DateTime? fromDate,
            DateTime? toDate,
            bool includeZeroQty = false,
            string? sortBy = "name",
            string? sortDir = "asc")
        {
            // عند التصدير: إذا لم يتم تحديد includeZeroQty، اجعله true افتراضياً
            string? includeZeroQtyStr = Request.Query["includeZeroQty"].FirstOrDefault();
            if (string.IsNullOrEmpty(includeZeroQtyStr))
            {
                includeZeroQty = true;
            }

            // عند عدم تحديد تاريخ: استخدام الشهر الحالي افتراضياً (نفس ProductBalances)
            var todayExport = DateTime.Today;
            if (!fromDate.HasValue && !toDate.HasValue)
            {
                fromDate = new DateTime(todayExport.Year, todayExport.Month, 1);
                toDate = todayExport;
            }

            // بناء الاستعلام (نفس منطق ProductBalances)
            var productsQuery = _context.Products
                .AsNoTracking()
                .Include(p => p.Category)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                productsQuery = productsQuery.Where(p =>
                    p.ProdName.Contains(s) ||
                    (p.Barcode != null && p.Barcode.Contains(s)) ||
                    (p.ProdId.ToString() == s));
            }

            if (categoryId.HasValue && categoryId.Value > 0)
            {
                productsQuery = productsQuery.Where(p => p.CategoryId == categoryId.Value);
            }

            productsQuery = productsQuery.Where(p => p.IsActive == true);

            var productIds = await productsQuery.Select(p => p.ProdId).ToListAsync();
            if (productIds.Count == 0)
            {
                return BadRequest("لا توجد بيانات للتصدير");
            }

            // تحميل البيانات بشكل مجمع (نفس منطق ProductBalances)
            var productsDict = await productsQuery
                .Select(p => new
                {
                    p.ProdId,
                    p.ProdName,
                    CategoryName = p.Category != null ? p.Category.CategoryName : "",
                    p.PriceRetail
                })
                .ToDictionaryAsync(p => p.ProdId);

            var stockBatchesQ = _context.StockBatches
                .AsNoTracking()
                .Where(sb => productIds.Contains(sb.ProdId));

            if (warehouseId.HasValue && warehouseId.Value > 0)
            {
                stockBatchesQ = stockBatchesQ.Where(sb => sb.WarehouseId == warehouseId.Value);
            }

            var stockQuantities = await stockBatchesQ
                .GroupBy(sb => sb.ProdId)
                .Select(g => new { ProdId = g.Key, TotalQty = g.Sum(sb => sb.QtyOnHand) })
                .ToDictionaryAsync(x => x.ProdId, x => x.TotalQty);

            var stockLedgerDiscount = await _context.StockLedger
                .AsNoTracking()
                .Where(x =>
                    productIds.Contains(x.ProdId) &&
                    x.SourceType == "Purchase" &&
                    (x.RemainingQty ?? 0) > 0)
                .GroupBy(x => x.ProdId)
                .Select(g => new
                {
                    ProdId = g.Key,
                    TotalRemaining = g.Sum(x => (decimal)(x.RemainingQty ?? 0)),
                    WeightedDiscount = g.Sum(x => (decimal)(x.RemainingQty ?? 0) * ((decimal?)(x.PurchaseDiscount) ?? 0m)),
                    WeightedCost = g.Sum(x => (decimal)(x.RemainingQty ?? 0) * x.UnitCost)
                })
                .ToDictionaryAsync(x => x.ProdId);

            Dictionary<int, decimal> salesQuantities = new Dictionary<int, decimal>();
            if (fromDate.HasValue || toDate.HasValue)
            {
                var salesQuery = _context.SalesInvoiceLines
                    .AsNoTracking()
                    .Include(sil => sil.SalesInvoice)
                    .Where(sil =>
                        productIds.Contains(sil.ProdId) &&
                        sil.SalesInvoice.IsPosted);

                if (warehouseId.HasValue && warehouseId.Value > 0)
                {
                    salesQuery = salesQuery.Where(sil => sil.SalesInvoice.WarehouseId == warehouseId.Value);
                }

                if (fromDate.HasValue)
                {
                    var from = fromDate.Value.Date;
                    salesQuery = salesQuery.Where(sil => sil.SalesInvoice.SIDate >= from);
                }

                if (toDate.HasValue)
                {
                    var to = toDate.Value.Date.AddDays(1);
                    salesQuery = salesQuery.Where(sil => sil.SalesInvoice.SIDate < to);
                }

                salesQuantities = await salesQuery
                    .GroupBy(sil => sil.ProdId)
                    .Select(g => new { ProdId = g.Key, TotalQty = g.Sum(sil => (decimal?)sil.Qty) ?? 0m })
                    .ToDictionaryAsync(x => x.ProdId, x => x.TotalQty);
            }

            Dictionary<int, decimal> returnQuantities = new Dictionary<int, decimal>();
            if (fromDate.HasValue || toDate.HasValue)
            {
                var returnsQuery = _context.SalesReturnLines
                    .AsNoTracking()
                    .Include(srl => srl.SalesReturn)
                    .Where(srl => productIds.Contains(srl.ProdId) && srl.SalesReturn != null && srl.SalesReturn.IsPosted);
                if (warehouseId.HasValue && warehouseId.Value > 0)
                    returnsQuery = returnsQuery.Where(srl => srl.SalesReturn!.WarehouseId == warehouseId.Value);
                if (fromDate.HasValue)
                    returnsQuery = returnsQuery.Where(srl => srl.SalesReturn!.SRDate >= fromDate.Value.Date);
                if (toDate.HasValue)
                    returnsQuery = returnsQuery.Where(srl => srl.SalesReturn!.SRDate < toDate.Value.Date.AddDays(1));
                returnQuantities = await returnsQuery
                    .GroupBy(srl => srl.ProdId)
                    .Select(g => new { ProdId = g.Key, TotalQty = g.Sum(srl => (decimal?)srl.Qty) ?? 0m })
                    .ToDictionaryAsync(x => x.ProdId, x => x.TotalQty);
            }

            var reportData = new List<ProductBalanceReportDto>();

            foreach (var prodId in productIds)
            {
                if (!productsDict.TryGetValue(prodId, out var product)) continue;

                int currentQty = stockQuantities.TryGetValue(prodId, out var qty) ? qty : 0;

                if (!includeZeroQty && currentQty == 0)
                    continue;

                decimal weightedDiscount = 0m;
                if (stockLedgerDiscount.TryGetValue(prodId, out var discountData))
                {
                    if (discountData.TotalRemaining > 0)
                    {
                        weightedDiscount = discountData.WeightedDiscount / discountData.TotalRemaining;
                    }
                }

                decimal salesQty = salesQuantities.TryGetValue(prodId, out var sales) ? sales : 0m;
                if (returnQuantities.TryGetValue(prodId, out var retQty))
                    salesQty = Math.Max(0m, salesQty - retQty);

                decimal unitCost = 0m;
                if (stockLedgerDiscount.TryGetValue(prodId, out var costData))
                {
                    if (costData.TotalRemaining > 0)
                    {
                        unitCost = costData.WeightedCost / costData.TotalRemaining;
                    }
                }

                decimal totalCost = currentQty * unitCost;

                reportData.Add(new ProductBalanceReportDto
                {
                    ProdId = prodId,
                    ProdCode = prodId.ToString(),
                    ProdName = product.ProdName ?? "",
                    CategoryName = product.CategoryName ?? "",
                    CurrentQty = currentQty,
                    WeightedDiscount = weightedDiscount,
                    SalesQty = salesQty,
                    PriceRetail = product.PriceRetail,
                    UnitCost = unitCost,
                    TotalCost = totalCost
                });
            }

            // الترتيب (نفس منطق ProductBalances)
            bool isDesc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
            switch (sortBy?.ToLowerInvariant())
            {
                case "code":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.ProdCode).ToList()
                        : reportData.OrderBy(r => r.ProdCode).ToList();
                    break;
                case "qty":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.CurrentQty).ToList()
                        : reportData.OrderBy(r => r.CurrentQty).ToList();
                    break;
                case "sales":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.SalesQty).ToList()
                        : reportData.OrderBy(r => r.SalesQty).ToList();
                    break;
                case "cost":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.TotalCost).ToList()
                        : reportData.OrderBy(r => r.TotalCost).ToList();
                    break;
                default: // "name"
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.ProdName).ToList()
                        : reportData.OrderBy(r => r.ProdName).ToList();
                    break;
            }

            // تصدير Excel (كل البيانات بدون Pagination)
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("أرصدة الأصناف");

            int row = 1;

            // عناوين الأعمدة
            worksheet.Cell(row, 1).Value = "الكود";
            worksheet.Cell(row, 2).Value = "اسم الصنف";
            worksheet.Cell(row, 3).Value = "الفئة";
            worksheet.Cell(row, 4).Value = "الكمية الحالية";
            worksheet.Cell(row, 5).Value = "الخصم المرجح %";
            worksheet.Cell(row, 6).Value = "المبيعات";
            worksheet.Cell(row, 7).Value = "سعر الجمهور";
            worksheet.Cell(row, 8).Value = "تكلفة العلبة";
            worksheet.Cell(row, 9).Value = "التكلفة الإجمالية";

            worksheet.Range(row, 1, row, 9).Style.Font.Bold = true;

            // البيانات
            row = 2;
            foreach (var item in reportData)
            {
                worksheet.Cell(row, 1).Value = item.ProdCode;
                worksheet.Cell(row, 2).Value = item.ProdName;
                worksheet.Cell(row, 3).Value = item.CategoryName;
                worksheet.Cell(row, 4).Value = item.CurrentQty;
                worksheet.Cell(row, 5).Value = item.WeightedDiscount;
                worksheet.Cell(row, 6).Value = item.SalesQty;
                worksheet.Cell(row, 7).Value = item.PriceRetail;
                worksheet.Cell(row, 8).Value = item.UnitCost;
                worksheet.Cell(row, 9).Value = item.TotalCost;
                row++;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new System.IO.MemoryStream();
            workbook.SaveAs(stream);
            var fileName = $"ProductBalances_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
            return File(stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        // =========================================================
        // تقرير: أرباح الأصناف
        // يحسب الربح بطريقتين:
        // 1) ربح البيع: من فواتير المبيعات (Revenue - Cost)
        // 2) ربح الميزانية: مجموع العملاء المدينين + رصيد الخزنة + تكلفة البضاعة في المخزن - مجموع العملاء الدائنين
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> ProductProfits(
            string? search,
            int? categoryId,
            int? warehouseId,
            DateTime? fromDate,
            DateTime? toDate,
            string? profitMethod = "both", // "sales" | "ledger" | "both"
            string? sortBy = "name",
            string? sortDir = "asc",
            bool loadReport = false,
            int page = 1,
            int pageSize = 20)
        {
            // =========================================================
            // 1) تجهيز القوائم المنسدلة
            // =========================================================
            var categories = await _context.Categories
                .AsNoTracking()
                .OrderBy(c => c.CategoryName)
                .Select(c => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                {
                    Value = c.CategoryId.ToString(),
                    Text = c.CategoryName
                })
                .ToListAsync();

            var warehouses = await _context.Warehouses
                .AsNoTracking()
                .OrderBy(w => w.WarehouseName)
                .Select(w => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                {
                    Value = w.WarehouseId.ToString(),
                    Text = w.WarehouseName
                })
                .ToListAsync();

            ViewBag.Categories = categories;
            ViewBag.Warehouses = warehouses;

            // =========================================================
            // 2) تجهيز الفلاتر
            // =========================================================
            ViewBag.Search = search ?? "";
            ViewBag.CategoryId = categoryId;
            ViewBag.WarehouseId = warehouseId;
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;
            ViewBag.ProfitMethod = profitMethod;
            ViewBag.SortBy = sortBy;
            ViewBag.SortDir = sortDir;

            // =========================================================
            // 3) تحميل البيانات فقط عند الضغط على "تجميع التقرير"
            // =========================================================
            if (!loadReport)
            {
                ViewBag.ReportData = new List<ProductProfitReportDto>();
                ViewBag.TotalSalesRevenue = 0m;
                ViewBag.TotalSalesCost = 0m;
                ViewBag.TotalSalesProfit = 0m;
                ViewBag.BalanceSheetData = null;
                return View();
            }

            // =========================================================
            // 4) بناء الاستعلام الأساسي للأصناف
            // =========================================================
            var productsQuery = _context.Products
                .AsNoTracking()
                .Include(p => p.Category)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                productsQuery = productsQuery.Where(p =>
                    p.ProdName.Contains(s) ||
                    (p.Barcode != null && p.Barcode.Contains(s)) ||
                    (p.ProdId.ToString() == s));
            }

            if (categoryId.HasValue && categoryId.Value > 0)
            {
                productsQuery = productsQuery.Where(p => p.CategoryId == categoryId.Value);
            }

            productsQuery = productsQuery.Where(p => p.IsActive == true);

            var productIds = await productsQuery.Select(p => p.ProdId).ToListAsync();

            if (productIds.Count == 0)
            {
                ViewBag.ReportData = new List<ProductProfitReportDto>();
                return View();
            }

            // =========================================================
            // 5) حساب الربح من البيع (SalesInvoiceLines)
            // =========================================================
            var salesProfitQuery = _context.SalesInvoiceLines
                .AsNoTracking()
                .Include(sil => sil.SalesInvoice)
                .Where(sil =>
                    productIds.Contains(sil.ProdId) &&
                    sil.SalesInvoice.IsPosted);

            if (warehouseId.HasValue && warehouseId.Value > 0)
            {
                salesProfitQuery = salesProfitQuery.Where(sil => sil.SalesInvoice.WarehouseId == warehouseId.Value);
            }

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
                    SalesCost = g.Sum(sil => 
                        sil.CostTotal > 0 
                            ? sil.CostTotal 
                            : (sil.ProfitValue > 0 
                                ? (sil.LineNetTotal - sil.ProfitValue) 
                                : 0m)),
                    SalesQty = g.Sum(sil => (decimal?)sil.Qty) ?? 0m
                })
                .ToDictionaryAsync(x => x.ProdId);

            // =========================================================
            // 5.1) مرتجعات البيع – خصم من الإيرادات والتكلفة والكمية
            // =========================================================
            var salesReturnProfitQuery = _context.SalesReturnLines
                .AsNoTracking()
                .Include(srl => srl.SalesReturn)
                .Where(srl => productIds.Contains(srl.ProdId) && srl.SalesReturn != null);

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

            // =========================================================
            // 6) حساب ربح الميزانية (طريقة جديدة):
            // الربح = مجموع العملاء المدينين + رصيد الخزنة + تكلفة البضاعة في المخزن - مجموع العملاء الدائنين
            // =========================================================
            decimal customersDebitSum = 0m;   // مجموع العملاء المدينين (رصيد مدين)
            decimal customersCreditSum = 0m;   // مجموع العملاء الدائنين (رصيد دائن)
            decimal treasuryBalance = 0m;     // رصيد الخزنة
            decimal inventoryCostTotal = 0m;  // تكلفة البضاعة في المخزن

            // 6.1) مجموع العملاء المدينين والدائنين (نفس مصدر تقرير أرصدة العملاء: Customer.CurrentBalance)
            var customerBalancesData = await _context.Customers
                .AsNoTracking()
                .Where(c => c.IsActive == true)
                .Select(c => new { c.CustomerId, c.CurrentBalance })
                .ToListAsync();

            foreach (var c in customerBalancesData)
            {
                if (c.CurrentBalance > 0)
                    customersDebitSum += c.CurrentBalance;
                else if (c.CurrentBalance < 0)
                    customersCreditSum += Math.Abs(c.CurrentBalance);
            }

            // 6.2) رصيد الخزنة (حسابات الخزينة والبنوك)
            var cashAccountIds = await _context.Accounts
                .AsNoTracking()
                .Where(a => a.AccountType == AccountType.Asset &&
                    (a.AccountName.Contains("خزينة") || a.AccountName.Contains("بنك") ||
                     a.AccountName.Contains("صندوق") || a.AccountCode.StartsWith("1101") ||
                     a.AccountCode.StartsWith("1102")))
                .Select(a => a.AccountId)
                .ToListAsync();

            if (cashAccountIds.Any())
            {
                var treasuryQuery = _context.LedgerEntries
                    .AsNoTracking()
                    .Where(e => cashAccountIds.Contains(e.AccountId) && e.PostVersion > 0);

                if (fromDate.HasValue)
                    treasuryQuery = treasuryQuery.Where(e => e.EntryDate >= fromDate.Value.Date);
                if (toDate.HasValue)
                    treasuryQuery = treasuryQuery.Where(e => e.EntryDate < toDate.Value.Date.AddDays(1));

                treasuryBalance = await treasuryQuery
                    .SumAsync(e => (decimal?)(e.Debit - e.Credit)) ?? 0m;
            }

            // 6.3) تكلفة البضاعة في المخزن (نفس منطق تقرير أرصدة الأصناف)
            // = StockBatches.QtyOnHand × متوسط التكلفة المرجح من StockLedger (Purchase) لكل صنف
            var inventoryCostStockBatches = _context.StockBatches.AsNoTracking();
            var inventoryCostStockLedger = _context.StockLedger.AsNoTracking()
                .Where(sl => (sl.RemainingQty ?? 0) > 0 && sl.SourceType == "Purchase");

            if (warehouseId.HasValue && warehouseId.Value > 0)
            {
                inventoryCostStockBatches = inventoryCostStockBatches.Where(sb => sb.WarehouseId == warehouseId.Value);
                inventoryCostStockLedger = inventoryCostStockLedger.Where(sl => sl.WarehouseId == warehouseId.Value);
            }

            // متوسط التكلفة المرجح لكل صنف: Sum(RemainingQty*UnitCost) / Sum(RemainingQty)
            var weightedCostByProd = await inventoryCostStockLedger
                .GroupBy(sl => sl.ProdId)
                .Select(g => new
                {
                    ProdId = g.Key,
                    TotalRemaining = g.Sum(sl => (decimal)(sl.RemainingQty ?? 0)),
                    WeightedCost = g.Sum(sl => (decimal)(sl.RemainingQty ?? 0) * sl.UnitCost)
                })
                .ToDictionaryAsync(x => x.ProdId);

            // الكمية الحالية لكل صنف من StockBatches
            var qtyByProd = await inventoryCostStockBatches
                .GroupBy(sb => sb.ProdId)
                .Select(g => new { ProdId = g.Key, TotalQty = g.Sum(sb => sb.QtyOnHand) })
                .ToDictionaryAsync(x => x.ProdId, x => x.TotalQty);

            foreach (var kvp in qtyByProd)
            {
                int prodId = kvp.Key;
                int currentQty = kvp.Value;
                decimal unitCost = 0m;
                if (weightedCostByProd.TryGetValue(prodId, out var costData) && costData.TotalRemaining > 0)
                    unitCost = costData.WeightedCost / costData.TotalRemaining;
                inventoryCostTotal += currentQty * unitCost;
            }

            decimal balanceSheetProfit = customersDebitSum + treasuryBalance + inventoryCostTotal - customersCreditSum;

            // =========================================================
            // 7) بناء reportData
            // =========================================================
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

                // الربح من البيع (بعد خصم مرتجعات البيع)
                decimal salesRevenue = salesProfitData.TryGetValue(prodId, out var salesData) ? salesData.SalesRevenue : 0m;
                decimal salesCost = salesProfitData.TryGetValue(prodId, out var salesData2) ? salesData2.SalesCost : 0m;
                decimal salesQty = salesProfitData.TryGetValue(prodId, out var salesData3) ? salesData3.SalesQty : 0m;
                if (returnRevenueQty.TryGetValue(prodId, out var ret))
                {
                    salesRevenue = Math.Max(0m, salesRevenue - ret.ReturnRevenue);
                    salesQty = Math.Max(0m, salesQty - ret.ReturnQty);
                }
                if (returnCostData.TryGetValue(prodId, out var retCost))
                    salesCost = Math.Max(0m, salesCost - retCost);
                decimal salesProfit = salesRevenue - salesCost;
                decimal salesProfitPercent = salesRevenue > 0 ? (salesProfit / salesRevenue) * 100m : 0m;

                // ربح الميزانية: رقم إجمالي على مستوى الشركة (لا يوزع على الأصناف)
                decimal ledgerRevenue = 0m;
                decimal ledgerCost = 0m;
                decimal ledgerProfit = 0m;
                decimal ledgerProfitPercent = 0m;
                decimal accountBalanceRevenue = 0m;
                decimal accountBalanceCost = 0m;
                decimal accountBalanceProfit = 0m;
                decimal accountBalanceProfitPercent = 0m;

                // متوسطات (بعد خصم المرتجعات)
                decimal avgUnitPrice = salesQty > 0 ? salesRevenue / salesQty : 0m;
                decimal avgUnitCost = salesQty > 0 ? salesCost / salesQty : 0m;

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
                    LedgerRevenue = ledgerRevenue,
                    LedgerCost = ledgerCost,
                    LedgerProfit = ledgerProfit,
                    LedgerProfitPercent = ledgerProfitPercent,
                    AccountBalanceRevenue = accountBalanceRevenue,
                    AccountBalanceCost = accountBalanceCost,
                    AccountBalanceProfit = accountBalanceProfit,
                    AccountBalanceProfitPercent = accountBalanceProfitPercent,
                    SalesQty = salesQty,
                    AvgUnitPrice = avgUnitPrice,
                    AvgUnitCost = avgUnitCost
                });
            }

            // =========================================================
            // 8) الترتيب
            // =========================================================
            bool isDesc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
            switch (sortBy?.ToLowerInvariant())
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
                default: // "name"
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.ProdName).ToList()
                        : reportData.OrderBy(r => r.ProdName).ToList();
                    break;
            }

            // =========================================================
            // 9) حساب المجاميع
            // =========================================================
            decimal totalSalesRevenue = reportData.Sum(r => r.SalesRevenue);
            decimal totalSalesCost = reportData.Sum(r => r.SalesCost);
            decimal totalSalesProfit = totalSalesRevenue - totalSalesCost;

            int totalCount = reportData.Count;

            // =========================================================
            // 10) Pagination
            // =========================================================
            if (pageSize > 0 && pageSize < totalCount)
            {
                if (page < 1) page = 1;
                int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
                if (page > totalPages) page = totalPages;

                int skip = (page - 1) * pageSize;
                reportData = reportData.Skip(skip).Take(pageSize).ToList();

                ViewBag.Page = page;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalPages = totalPages;
                ViewBag.TotalCount = totalCount;
            }
            else
            {
                ViewBag.Page = 1;
                ViewBag.PageSize = totalCount;
                ViewBag.TotalPages = 1;
                ViewBag.TotalCount = totalCount;
            }

            ViewBag.ReportData = reportData;
            ViewBag.TotalSalesRevenue = totalSalesRevenue;
            ViewBag.TotalSalesCost = totalSalesCost;
            ViewBag.TotalSalesProfit = totalSalesProfit;

            // =========================================================
            // 11) بيانات ربح الميزانية (للعرض تحت الجدول) - ProductProfits
            // الربح = مجموع العملاء المدينين + رصيد الخزنة + تكلفة البضاعة - مجموع العملاء الدائنين
            // =========================================================
            ViewBag.BalanceSheetData = new
            {
                CustomersDebitSum = customersDebitSum,
                CustomersCreditSum = customersCreditSum,
                TreasuryBalance = treasuryBalance,
                InventoryCostTotal = inventoryCostTotal,
                BalanceSheetProfit = balanceSheetProfit
            };

            return View();
        }

        // =========================================================
        // تقرير: أرباح العملاء
        // يحسب الربح بطريقتين:
        // 1) من البيع (SalesInvoiceLines): Revenue - Cost
        // 2) من الميزانية (LedgerEntries): Revenue Account - COGS Account
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> CustomerProfits(
            string? search,
            string? partyCategory,
            int? governorateId,
            DateTime? fromDate,
            DateTime? toDate,
            string? profitMethod = "both", // "sales" | "ledger" | "both"
            string? sortBy = "name",
            string? sortDir = "asc",
            bool loadReport = false,
            int page = 1,
            int pageSize = 200)
        {
            // =========================================================
            // 1) تجهيز القوائم المنسدلة
            // =========================================================
            var governorates = await _context.Governorates
                .AsNoTracking()
                .OrderBy(g => g.GovernorateName)
                .Select(g => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                {
                    Value = g.GovernorateId.ToString(),
                    Text = g.GovernorateName
                })
                .ToListAsync();

            ViewBag.Governorates = governorates;

            // =========================================================
            // 2) تجهيز الفلاتر
            // =========================================================
            ViewBag.Search = search ?? "";
            ViewBag.PartyCategory = partyCategory;
            ViewBag.GovernorateId = governorateId;
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;
            ViewBag.ProfitMethod = profitMethod;
            ViewBag.SortBy = sortBy;
            ViewBag.SortDir = sortDir;

            // =========================================================
            // 3) تحميل البيانات فقط عند الضغط على "تجميع التقرير"
            // =========================================================
            if (!loadReport)
            {
                ViewBag.ReportData = new List<CustomerProfitReportDto>();
                ViewBag.TotalSalesRevenue = 0m;
                ViewBag.TotalSalesCost = 0m;
                ViewBag.TotalSalesProfit = 0m;
                ViewBag.TotalLedgerRevenue = 0m;
                ViewBag.TotalLedgerCost = 0m;
                ViewBag.TotalLedgerProfit = 0m;
                return View();
            }

            // =========================================================
            // 4) بناء الاستعلام الأساسي للعملاء
            // =========================================================
            var customersQuery = _context.Customers
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                customersQuery = customersQuery.Where(c =>
                    (c.CustomerName != null && c.CustomerName.Contains(s)) ||
                    (c.Phone1 != null && c.Phone1.Contains(s)) ||
                    (c.CustomerId.ToString() == s));
            }

            if (!string.IsNullOrWhiteSpace(partyCategory))
            {
                customersQuery = customersQuery.Where(c => c.PartyCategory == partyCategory);
            }

            if (governorateId.HasValue && governorateId.Value > 0)
            {
                customersQuery = customersQuery.Where(c => c.GovernorateId == governorateId.Value);
            }

            customersQuery = customersQuery.Where(c => c.IsActive == true);

            var customerIds = await customersQuery.Select(c => c.CustomerId).ToListAsync();

            if (customerIds.Count == 0)
            {
                ViewBag.ReportData = new List<CustomerProfitReportDto>();
                return View();
            }

            // =========================================================
            // 5) حساب الربح من البيع (SalesInvoiceLines)
            // نفس منطق ProductProfits بالضبط
            // =========================================================
            var salesProfitQuery = _context.SalesInvoiceLines
                .AsNoTracking()
                .Include(sil => sil.SalesInvoice)
                .Where(sil =>
                    customerIds.Contains(sil.SalesInvoice.CustomerId) &&
                    sil.SalesInvoice.IsPosted);

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
                .GroupBy(sil => sil.SalesInvoice.CustomerId)
                .Select(g => new
                {
                    CustomerId = g.Key,
                    SalesRevenue = g.Sum(sil => sil.LineNetTotal),
                    SalesCost = g.Sum(sil => 
                        sil.CostTotal > 0 
                            ? sil.CostTotal 
                            : (sil.ProfitValue > 0 
                                ? (sil.LineNetTotal - sil.ProfitValue) 
                                : 0m)),
                    InvoiceCount = g.Select(sil => sil.SIId).Distinct().Count()
                })
                .ToDictionaryAsync(x => x.CustomerId);

            // =========================================================
            // 6) حساب الربح من الميزانية (LedgerEntries)
            // =========================================================
            var salesRevenueAccount = await _context.Accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.AccountCode == "4100");

            var cogsAccount = await _context.Accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.AccountCode == "5100");

            Dictionary<int, decimal> ledgerRevenueData = new Dictionary<int, decimal>();
            Dictionary<int, decimal> ledgerCostData = new Dictionary<int, decimal>();

            if (salesRevenueAccount != null && cogsAccount != null)
            {
                // الحصول على فواتير المبيعات المرتبطة بالعملاء المحددين
                var salesInvoiceIds = await salesProfitQuery
                    .Select(sil => sil.SalesInvoice.SIId)
                    .Distinct()
                    .ToListAsync();

                if (salesInvoiceIds.Any())
                {
                    // تحميل الفواتير دفعة واحدة
                    var invoicesDict = await _context.SalesInvoices
                        .AsNoTracking()
                        .Where(si => salesInvoiceIds.Contains(si.SIId))
                        .Select(si => new { si.SIId, si.CustomerId })
                        .ToDictionaryAsync(x => x.SIId, x => x.CustomerId);

                    // الإيرادات من الميزانية - نستخدم SourceId (رقم الفاتورة) ثم نربطه بالعميل
                    var revenueQuery = _context.LedgerEntries
                        .AsNoTracking()
                        .Where(e =>
                            e.AccountId == salesRevenueAccount.AccountId &&
                            e.SourceType == LedgerSourceType.SalesInvoice &&
                            e.LineNo == 2 &&
                            e.PostVersion > 0 &&
                            e.SourceId.HasValue &&
                            salesInvoiceIds.Contains(e.SourceId.Value) &&
                            !_context.LedgerEntries.Any(rev =>
                                rev.SourceType == LedgerSourceType.SalesInvoice &&
                                rev.SourceId == e.SourceId &&
                                rev.LineNo == 9001));

                    if (fromDate.HasValue)
                    {
                        var from = fromDate.Value.Date;
                        revenueQuery = revenueQuery.Where(e => e.EntryDate >= from);
                    }

                    if (toDate.HasValue)
                    {
                        var to = toDate.Value.Date.AddDays(1);
                        revenueQuery = revenueQuery.Where(e => e.EntryDate < to);
                    }

                    var revenueEntries = await revenueQuery.ToListAsync();

                    // ربط الإيرادات بالعملاء عبر SalesInvoices
                    foreach (var entry in revenueEntries)
                    {
                        if (!entry.SourceId.HasValue) continue;
                        if (!invoicesDict.TryGetValue(entry.SourceId.Value, out int customerId)) continue;
                        if (!customerIds.Contains(customerId)) continue;

                        if (ledgerRevenueData.ContainsKey(customerId))
                            ledgerRevenueData[customerId] += entry.Credit;
                        else
                            ledgerRevenueData[customerId] = entry.Credit;
                    }

                    // COGS من الميزانية - نستخدم SourceId (رقم الفاتورة) ثم نربطه بالعميل
                    var cogsQuery = _context.LedgerEntries
                        .AsNoTracking()
                        .Where(e =>
                            e.AccountId == cogsAccount.AccountId &&
                            e.SourceType == LedgerSourceType.SalesInvoice &&
                            e.LineNo == 3 &&
                            e.PostVersion > 0 &&
                            e.SourceId.HasValue &&
                            salesInvoiceIds.Contains(e.SourceId.Value) &&
                            !_context.LedgerEntries.Any(rev =>
                                rev.SourceType == LedgerSourceType.SalesInvoice &&
                                rev.SourceId == e.SourceId &&
                                rev.LineNo == 9001));

                    if (fromDate.HasValue)
                    {
                        var from = fromDate.Value.Date;
                        cogsQuery = cogsQuery.Where(e => e.EntryDate >= from);
                    }

                    if (toDate.HasValue)
                    {
                        var to = toDate.Value.Date.AddDays(1);
                        cogsQuery = cogsQuery.Where(e => e.EntryDate < to);
                    }

                    var cogsEntries = await cogsQuery.ToListAsync();

                    // ربط COGS بالعملاء عبر SalesInvoices
                    foreach (var entry in cogsEntries)
                    {
                        if (!entry.SourceId.HasValue) continue;
                        if (!invoicesDict.TryGetValue(entry.SourceId.Value, out int customerId)) continue;
                        if (!customerIds.Contains(customerId)) continue;

                        if (ledgerCostData.ContainsKey(customerId))
                            ledgerCostData[customerId] += entry.Debit;
                        else
                            ledgerCostData[customerId] = entry.Debit;
                    }
                }
            }

            // =========================================================
            // 6.5) حساب إشعارات الخصم والإضافة من LedgerEntries
            // ✅ إشعارات الخصم (DebitNote): تكلفة/مصروف (يقلل الربح)
            // ✅ إشعارات الإضافة (CreditNote): إيراد (يزيد الربح)
            // 
            // المنطق: عند ترحيل إشعار الخصم/الإضافة، يُنشأ قيدان:
            // - DebitNote: مدين حساب العميل، دائن حساب OffsetAccount
            // - CreditNote: مدين حساب OffsetAccount، دائن حساب العميل
            // 
            // لذلك: نحسب التأثير من القيد الذي يحتوي على OffsetAccount:
            // - إذا كان AccountType = Expense → مصروف (يقلل الربح)
            // - إذا كان AccountType = Revenue → إيراد (يزيد الربح)
            // =========================================================
            var notesQuery = _context.LedgerEntries
                .AsNoTracking()
                .Include(e => e.Account)
                .Where(e =>
                    e.CustomerId.HasValue &&
                    customerIds.Contains(e.CustomerId.Value) &&
                    (e.SourceType == LedgerSourceType.DebitNote || 
                     e.SourceType == LedgerSourceType.CreditNote) &&
                    e.PostVersion > 0);

            if (fromDate.HasValue)
            {
                var from = fromDate.Value.Date;
                notesQuery = notesQuery.Where(e => e.EntryDate >= from);
            }

            if (toDate.HasValue)
            {
                var to = toDate.Value.Date.AddDays(1);
                notesQuery = notesQuery.Where(e => e.EntryDate < to);
            }

            var notesEntries = await notesQuery.ToListAsync();

            Dictionary<int, decimal> debitNotesAmount = new Dictionary<int, decimal>();
            Dictionary<int, decimal> creditNotesAmount = new Dictionary<int, decimal>();

            // جلب إشعارات الخصم والإضافة للحصول على OffsetAccountId
            var debitNoteIds = notesEntries
                .Where(e => e.SourceType == LedgerSourceType.DebitNote && e.SourceId.HasValue)
                .Select(e => e.SourceId!.Value)
                .Distinct()
                .ToList();

            var creditNoteIds = notesEntries
                .Where(e => e.SourceType == LedgerSourceType.CreditNote && e.SourceId.HasValue)
                .Select(e => e.SourceId!.Value)
                .Distinct()
                .ToList();

            var debitNotesDict = await _context.DebitNotes
                .AsNoTracking()
                .Where(dn => debitNoteIds.Contains(dn.DebitNoteId))
                .Select(dn => new { dn.DebitNoteId, dn.OffsetAccountId })
                .ToDictionaryAsync(x => x.DebitNoteId);

            var creditNotesDict = await _context.CreditNotes
                .AsNoTracking()
                .Where(cn => creditNoteIds.Contains(cn.CreditNoteId))
                .Select(cn => new { cn.CreditNoteId, cn.OffsetAccountId })
                .ToDictionaryAsync(x => x.CreditNoteId);

            foreach (var entry in notesEntries)
            {
                if (!entry.CustomerId.HasValue || !entry.SourceId.HasValue) continue;

                int custId = entry.CustomerId.Value;
                int? offsetAccountId = null;

                // تحديد OffsetAccountId
                if (entry.SourceType == LedgerSourceType.DebitNote)
                {
                    if (debitNotesDict.TryGetValue(entry.SourceId.Value, out var dn))
                        offsetAccountId = dn.OffsetAccountId;
                }
                else if (entry.SourceType == LedgerSourceType.CreditNote)
                {
                    if (creditNotesDict.TryGetValue(entry.SourceId.Value, out var cn))
                        offsetAccountId = cn.OffsetAccountId;
                }

                // إذا كان AccountId في القيد = OffsetAccountId → هذا هو القيد الذي يؤثر على الربح
                if (!offsetAccountId.HasValue || entry.AccountId != offsetAccountId.Value)
                    continue;

                decimal amount = 0m;

                // تحديد المبلغ حسب نوع الإشعار ونوع القيد
                if (entry.SourceType == LedgerSourceType.DebitNote)
                {
                    // DebitNote: دائن OffsetAccount (Credit)
                    amount = entry.Credit;
                }
                else if (entry.SourceType == LedgerSourceType.CreditNote)
                {
                    // CreditNote: مدين OffsetAccount (Debit)
                    amount = entry.Debit;
                }

                if (amount <= 0m) continue;

                // تحديد التأثير حسب نوع الحساب
                if (entry.Account.AccountType == AccountType.Expense)
                {
                    // مصروف → يقلل الربح
                    if (debitNotesAmount.ContainsKey(custId))
                        debitNotesAmount[custId] += amount;
                    else
                        debitNotesAmount[custId] = amount;
                }
                else if (entry.Account.AccountType == AccountType.Revenue)
                {
                    // إيراد → يزيد الربح
                    if (creditNotesAmount.ContainsKey(custId))
                        creditNotesAmount[custId] += amount;
                    else
                        creditNotesAmount[custId] = amount;
                }
            }

            // =========================================================
            // 7) بناء reportData
            // =========================================================
            var customersDict = await customersQuery
                .Select(c => new
                {
                    c.CustomerId,
                    c.CustomerName,
                    c.PartyCategory,
                    c.Phone1
                })
                .ToDictionaryAsync(c => c.CustomerId);

            var reportData = new List<CustomerProfitReportDto>();

            foreach (var customerId in customerIds)
            {
                if (!customersDict.TryGetValue(customerId, out var customer)) continue;

                // الربح من البيع (نفس منطق ProductProfits)
                decimal salesRevenue = salesProfitData.TryGetValue(customerId, out var salesData) ? salesData.SalesRevenue : 0m;
                decimal salesCost = salesProfitData.TryGetValue(customerId, out var salesData2) ? salesData2.SalesCost : 0m;
                decimal salesProfit = salesRevenue - salesCost;
                decimal salesProfitPercent = salesRevenue > 0 ? (salesProfit / salesRevenue) * 100m : 0m;
                int invoiceCount = salesProfitData.TryGetValue(customerId, out var salesData3) ? salesData3.InvoiceCount : 0;
                decimal avgInvoiceValue = invoiceCount > 0 ? salesRevenue / invoiceCount : 0m;

                // الربح من الميزانية
                decimal ledgerRevenue = ledgerRevenueData.TryGetValue(customerId, out var rev) ? rev : 0m;
                decimal ledgerCost = ledgerCostData.TryGetValue(customerId, out var cost) ? cost : 0m;
                decimal ledgerProfit = ledgerRevenue - ledgerCost;
                decimal ledgerProfitPercent = ledgerRevenue > 0 ? (ledgerProfit / ledgerRevenue) * 100m : 0m;

                // إشعارات الخصم والإضافة
                decimal debitNotes = debitNotesAmount.TryGetValue(customerId, out var dn) ? dn : 0m;
                decimal creditNotes = creditNotesAmount.TryGetValue(customerId, out var cn) ? cn : 0m;
                decimal netNotesAdjustment = creditNotes - debitNotes; // صافي الإشعارات (الإضافة - الخصم)
                decimal adjustedProfit = salesProfit + netNotesAdjustment; // الربح المعدل
                decimal adjustedProfitPercent = salesRevenue > 0 ? (adjustedProfit / salesRevenue) * 100m : 0m;

                // الربح من أرصدة الحسابات (Account Balance) - سيتم حسابه لاحقاً لكل عميل
                decimal accountBalanceRevenue = 0m;
                decimal accountBalanceCost = 0m;
                decimal accountBalanceProfit = 0m;
                decimal accountBalanceProfitPercent = 0m;

                reportData.Add(new CustomerProfitReportDto
                {
                    CustomerId = customerId,
                    CustomerCode = customerId.ToString(),
                    CustomerName = customer.CustomerName ?? "",
                    PartyCategory = customer.PartyCategory ?? "",
                    Phone1 = customer.Phone1 ?? "",
                    SalesRevenue = salesRevenue,
                    SalesCost = salesCost,
                    SalesProfit = salesProfit,
                    SalesProfitPercent = salesProfitPercent,
                    LedgerRevenue = ledgerRevenue,
                    LedgerCost = ledgerCost,
                    LedgerProfit = ledgerProfit,
                    LedgerProfitPercent = ledgerProfitPercent,
                    DebitNotesAmount = debitNotes,
                    CreditNotesAmount = creditNotes,
                    NetNotesAdjustment = netNotesAdjustment,
                    AdjustedProfit = adjustedProfit,
                    AdjustedProfitPercent = adjustedProfitPercent,
                    AccountBalanceRevenue = accountBalanceRevenue,
                    AccountBalanceCost = accountBalanceCost,
                    AccountBalanceProfit = accountBalanceProfit,
                    AccountBalanceProfitPercent = accountBalanceProfitPercent,
                    InvoiceCount = invoiceCount,
                    AvgInvoiceValue = avgInvoiceValue
                });
            }

            // =========================================================
            // 7.1) حساب الربح من أرصدة الحسابات (Account Balance) للعملاء
            // =========================================================
            if (salesRevenueAccount != null && cogsAccount != null)
            {
                // الحصول على فواتير المبيعات المرتبطة بالعملاء المحددين
                var salesInvoiceIdsQuery = _context.SalesInvoiceLines
                    .AsNoTracking()
                    .Where(sil =>
                        customerIds.Contains(sil.SalesInvoice.CustomerId) &&
                        sil.SalesInvoice.IsPosted);

                if (fromDate.HasValue)
                {
                    var from = fromDate.Value.Date;
                    salesInvoiceIdsQuery = salesInvoiceIdsQuery.Where(sil => sil.SalesInvoice.SIDate >= from);
                }

                if (toDate.HasValue)
                {
                    var to = toDate.Value.Date.AddDays(1);
                    salesInvoiceIdsQuery = salesInvoiceIdsQuery.Where(sil => sil.SalesInvoice.SIDate < to);
                }

                var salesInvoiceIds = await salesInvoiceIdsQuery
                    .Select(sil => sil.SalesInvoice.SIId)
                    .Distinct()
                    .ToListAsync();

                if (salesInvoiceIds.Any())
                {
                    // تحميل الفواتير دفعة واحدة
                    var invoicesDict = await _context.SalesInvoices
                        .AsNoTracking()
                        .Where(si => salesInvoiceIds.Contains(si.SIId))
                        .Select(si => new { si.SIId, si.CustomerId })
                        .ToDictionaryAsync(x => x.SIId, x => x.CustomerId);

                    // حساب رصيد حساب الإيرادات (4100) = Credit - Debit
                    var revenueBalanceEntries = await _context.LedgerEntries
                        .AsNoTracking()
                        .Where(e =>
                            e.AccountId == salesRevenueAccount.AccountId &&
                            e.SourceType == LedgerSourceType.SalesInvoice &&
                            e.LineNo == 2 &&
                            e.PostVersion > 0 &&
                            e.SourceId.HasValue &&
                            salesInvoiceIds.Contains(e.SourceId.Value) &&
                            !_context.LedgerEntries.Any(rev =>
                                rev.SourceType == LedgerSourceType.SalesInvoice &&
                                rev.SourceId == e.SourceId &&
                                rev.LineNo == 9001))
                        .ToListAsync();

                    // تطبيق فلتر التاريخ
                    if (fromDate.HasValue || toDate.HasValue)
                    {
                        var from = fromDate?.Date ?? DateTime.MinValue;
                        var to = toDate?.Date.AddDays(1) ?? DateTime.MaxValue;
                        revenueBalanceEntries = revenueBalanceEntries
                            .Where(e => e.EntryDate >= from && e.EntryDate < to)
                            .ToList();
                    }

                    // ربط الإيرادات بالعملاء عبر SalesInvoices
                    Dictionary<int, decimal> revenueBalanceByCustomer = new Dictionary<int, decimal>();
                    foreach (var entry in revenueBalanceEntries)
                    {
                        if (!entry.SourceId.HasValue) continue;
                        if (!invoicesDict.TryGetValue(entry.SourceId.Value, out int customerId)) continue;
                        if (!customerIds.Contains(customerId)) continue;

                        if (revenueBalanceByCustomer.ContainsKey(customerId))
                        {
                            revenueBalanceByCustomer[customerId] += (entry.Credit - entry.Debit);
                        }
                        else
                        {
                            revenueBalanceByCustomer[customerId] = entry.Credit - entry.Debit;
                        }
                    }

                    // حساب رصيد حساب COGS (5100) = Debit - Credit
                    var cogsBalanceEntries = await _context.LedgerEntries
                        .AsNoTracking()
                        .Where(e =>
                            e.AccountId == cogsAccount.AccountId &&
                            e.SourceType == LedgerSourceType.SalesInvoice &&
                            e.LineNo == 3 &&
                            e.PostVersion > 0 &&
                            e.SourceId.HasValue &&
                            salesInvoiceIds.Contains(e.SourceId.Value) &&
                            !_context.LedgerEntries.Any(rev =>
                                rev.SourceType == LedgerSourceType.SalesInvoice &&
                                rev.SourceId == e.SourceId &&
                                rev.LineNo == 9001))
                        .ToListAsync();

                    // تطبيق فلتر التاريخ
                    if (fromDate.HasValue || toDate.HasValue)
                    {
                        var from = fromDate?.Date ?? DateTime.MinValue;
                        var to = toDate?.Date.AddDays(1) ?? DateTime.MaxValue;
                        cogsBalanceEntries = cogsBalanceEntries
                            .Where(e => e.EntryDate >= from && e.EntryDate < to)
                            .ToList();
                    }

                    // ربط COGS بالعملاء عبر SalesInvoices
                    Dictionary<int, decimal> cogsBalanceByCustomer = new Dictionary<int, decimal>();
                    foreach (var entry in cogsBalanceEntries)
                    {
                        if (!entry.SourceId.HasValue) continue;
                        if (!invoicesDict.TryGetValue(entry.SourceId.Value, out int customerId)) continue;
                        if (!customerIds.Contains(customerId)) continue;

                        if (cogsBalanceByCustomer.ContainsKey(customerId))
                        {
                            cogsBalanceByCustomer[customerId] += (entry.Debit - entry.Credit);
                        }
                        else
                        {
                            cogsBalanceByCustomer[customerId] = entry.Debit - entry.Credit;
                        }
                    }

                    // تحديث بيانات كل عميل
                    foreach (var item in reportData)
                    {
                        decimal revenueBalance = revenueBalanceByCustomer.TryGetValue(item.CustomerId, out var revBal) ? revBal : 0m;
                        decimal cogsBalance = cogsBalanceByCustomer.TryGetValue(item.CustomerId, out var cogsBal) ? cogsBal : 0m;
                        
                        item.AccountBalanceRevenue = revenueBalance;
                        item.AccountBalanceCost = cogsBalance;
                        item.AccountBalanceProfit = revenueBalance - cogsBalance;
                        item.AccountBalanceProfitPercent = revenueBalance > 0 
                            ? (item.AccountBalanceProfit / revenueBalance) * 100m 
                            : 0m;
                    }
                }
            }

            // =========================================================
            // 8) الترتيب
            // =========================================================
            bool isDesc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
            switch (sortBy?.ToLowerInvariant())
            {
                case "code":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.CustomerCode).ToList()
                        : reportData.OrderBy(r => r.CustomerCode).ToList();
                    break;
                case "salesprofit":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.SalesProfit).ToList()
                        : reportData.OrderBy(r => r.SalesProfit).ToList();
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
                default: // "name"
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.CustomerName).ToList()
                        : reportData.OrderBy(r => r.CustomerName).ToList();
                    break;
            }

            // =========================================================
            // 9) حساب المجاميع
            // =========================================================
            decimal totalSalesRevenue = reportData.Sum(r => r.SalesRevenue);
            decimal totalSalesCost = reportData.Sum(r => r.SalesCost);
            decimal totalSalesProfit = totalSalesRevenue - totalSalesCost;
            decimal totalLedgerRevenue = reportData.Sum(r => r.LedgerRevenue);
            decimal totalLedgerCost = reportData.Sum(r => r.LedgerCost);
            decimal totalLedgerProfit = totalLedgerRevenue - totalLedgerCost;
            
            // حساب المجاميع للطريقة الثالثة (من الأرصدة)
            decimal totalAccountBalanceRevenue = reportData.Sum(r => r.AccountBalanceRevenue);
            decimal totalAccountBalanceCost = reportData.Sum(r => r.AccountBalanceCost);
            decimal totalAccountBalanceProfit = totalAccountBalanceRevenue - totalAccountBalanceCost;

            // إجمالي إشعارات الخصم والإضافة (CustomerProfits)
            decimal totalDebitNotes = reportData.Sum(r => r.DebitNotesAmount);
            decimal totalCreditNotes = reportData.Sum(r => r.CreditNotesAmount);
            decimal totalNetNotesAdjustment = totalCreditNotes - totalDebitNotes;
            decimal totalAdjustedProfit = totalSalesProfit + totalNetNotesAdjustment;

            int totalCount = reportData.Count;

            // =========================================================
            // 10) Pagination
            // =========================================================
            if (pageSize > 0 && pageSize < totalCount)
            {
                if (page < 1) page = 1;
                int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
                if (page > totalPages) page = totalPages;

                int skip = (page - 1) * pageSize;
                reportData = reportData.Skip(skip).Take(pageSize).ToList();

                ViewBag.Page = page;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalPages = totalPages;
                ViewBag.TotalCount = totalCount;
            }
            else
            {
                ViewBag.Page = 1;
                ViewBag.PageSize = totalCount;
                ViewBag.TotalPages = 1;
                ViewBag.TotalCount = totalCount;
            }

            ViewBag.ReportData = reportData;
            ViewBag.TotalSalesRevenue = totalSalesRevenue;
            ViewBag.TotalSalesCost = totalSalesCost;
            ViewBag.TotalSalesProfit = totalSalesProfit;
            ViewBag.TotalLedgerRevenue = totalLedgerRevenue;
            ViewBag.TotalLedgerCost = totalLedgerCost;
            ViewBag.TotalLedgerProfit = totalLedgerProfit;
            ViewBag.TotalAccountBalanceRevenue = totalAccountBalanceRevenue;
            ViewBag.TotalAccountBalanceCost = totalAccountBalanceCost;
            ViewBag.TotalAccountBalanceProfit = totalAccountBalanceProfit;
            ViewBag.TotalDebitNotes = totalDebitNotes;
            ViewBag.TotalCreditNotes = totalCreditNotes;
            ViewBag.TotalNetNotesAdjustment = totalNetNotesAdjustment;
            ViewBag.TotalAdjustedProfit = totalAdjustedProfit;

            // =========================================================
            // 11) بيانات الميزانية (للعرض تحت الجدول) - CustomerProfits
            // =========================================================
            if (salesRevenueAccount != null && cogsAccount != null)
            {
                // حساب إجمالي الإيرادات من LedgerEntries (حساب 4100)
                var revenueQuery = _context.LedgerEntries
                    .AsNoTracking()
                    .Where(e =>
                        e.AccountId == salesRevenueAccount.AccountId &&
                        e.SourceType == LedgerSourceType.SalesInvoice &&
                        e.LineNo == 2 &&
                        e.PostVersion > 0 &&
                        e.CustomerId.HasValue &&
                        customerIds.Contains(e.CustomerId.Value) &&
                        !_context.LedgerEntries.Any(rev =>
                            rev.SourceType == LedgerSourceType.SalesInvoice &&
                            rev.SourceId == e.SourceId &&
                            rev.LineNo == 9001));

                if (fromDate.HasValue)
                {
                    var from = fromDate.Value.Date;
                    revenueQuery = revenueQuery.Where(e => e.EntryDate >= from);
                }

                if (toDate.HasValue)
                {
                    var to = toDate.Value.Date.AddDays(1);
                    revenueQuery = revenueQuery.Where(e => e.EntryDate < to);
                }

                var totalRevenueFromLedger = await revenueQuery.SumAsync(e => (decimal?)e.Credit) ?? 0m;

                // حساب إجمالي COGS من LedgerEntries (حساب 5100)
                var cogsQuery = _context.LedgerEntries
                    .AsNoTracking()
                    .Where(e =>
                        e.AccountId == cogsAccount.AccountId &&
                        e.SourceType == LedgerSourceType.SalesInvoice &&
                        e.LineNo == 3 &&
                        e.PostVersion > 0 &&
                        e.CustomerId.HasValue &&
                        customerIds.Contains(e.CustomerId.Value) &&
                        !_context.LedgerEntries.Any(rev =>
                            rev.SourceType == LedgerSourceType.SalesInvoice &&
                            rev.SourceId == e.SourceId &&
                            rev.LineNo == 9001));

                if (fromDate.HasValue)
                {
                    var from = fromDate.Value.Date;
                    cogsQuery = cogsQuery.Where(e => e.EntryDate >= from);
                }

                if (toDate.HasValue)
                {
                    var to = toDate.Value.Date.AddDays(1);
                    cogsQuery = cogsQuery.Where(e => e.EntryDate < to);
                }

                var totalCogsFromLedger = await cogsQuery.SumAsync(e => (decimal?)e.Debit) ?? 0m;

                ViewBag.BalanceSheetData = new
                {
                    RevenueAccount = new
                    {
                        AccountId = salesRevenueAccount.AccountId,
                        AccountCode = salesRevenueAccount.AccountCode,
                        AccountName = salesRevenueAccount.AccountName,
                        TotalCredit = totalRevenueFromLedger
                    },
                    CogsAccount = new
                    {
                        AccountId = cogsAccount.AccountId,
                        AccountCode = cogsAccount.AccountCode,
                        AccountName = cogsAccount.AccountName,
                        TotalDebit = totalCogsFromLedger
                    },
                    TotalProfit = totalRevenueFromLedger - totalCogsFromLedger
                };
            }
            else
            {
                ViewBag.BalanceSheetData = null;
            }

            return View();
        }
    }
}
