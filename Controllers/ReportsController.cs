using ERP.Data;
using ERP.Filters;
using ERP.Models;
using ERP.Security;
using ERP.Services;
using ERP.ViewModels;
using ERP.Infrastructure;
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
        private readonly ILedgerPostingService _ledgerPostingService;
        private readonly IUserActivityLogger _activityLogger;

        public ReportsController(AppDbContext context, StockAnalysisService stockAnalysisService, ILedgerPostingService ledgerPostingService, IUserActivityLogger activityLogger)
        {
            _context = context;
            _stockAnalysisService = stockAnalysisService;
            _ledgerPostingService = ledgerPostingService;
            _activityLogger = activityLogger;
        }

        /// <summary>
        /// إصلاح القيود المتبقية من مستندات محذوفة (أذون استلام/دفع، إشعارات) دون عكس، ثم إعادة حساب أرصدة العملاء.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> FixOrphanedPaymentEntries()
        {
            var postedBy = User?.Identity?.Name ?? "SYSTEM";
            int fixedCount = 0;

            // إذون الدفع
            var existingPaymentIds = await _context.CashPayments.Select(p => p.CashPaymentId).ToListAsync();
            var orphanedPayments = await _context.LedgerEntries
                .Where(e => e.SourceType == LedgerSourceType.Payment && e.SourceId.HasValue && e.LineNo < 9000)
                .Select(e => e.SourceId!.Value).Distinct()
                .Where(id => !existingPaymentIds.Contains(id)).ToListAsync();

            foreach (var id in orphanedPayments)
            {
                try
                {
                    await _ledgerPostingService.ReverseForHeaderDeleteAsync(LedgerSourceType.Payment, id, postedBy, "إصلاح إذن دفع محذوف");
                    fixedCount++;
                }
                catch { }
            }

            // إذون الاستلام
            var existingReceiptIds = await _context.CashReceipts.Select(r => r.CashReceiptId).ToListAsync();
            var orphanedReceipts = await _context.LedgerEntries
                .Where(e => e.SourceType == LedgerSourceType.Receipt && e.SourceId.HasValue && e.LineNo < 9000)
                .Select(e => e.SourceId!.Value).Distinct()
                .Where(id => !existingReceiptIds.Contains(id)).ToListAsync();

            foreach (var id in orphanedReceipts)
            {
                try
                {
                    await _ledgerPostingService.ReverseForHeaderDeleteAsync(LedgerSourceType.Receipt, id, postedBy, "إصلاح إذن استلام محذوف");
                    fixedCount++;
                }
                catch { }
            }

            // إشعارات الخصم والإضافة
            var existingDebitIds = await _context.DebitNotes.Select(d => d.DebitNoteId).ToListAsync();
            var existingCreditIds = await _context.CreditNotes.Select(c => c.CreditNoteId).ToListAsync();

            var orphanedDebits = await _context.LedgerEntries
                .Where(e => e.SourceType == LedgerSourceType.DebitNote && e.SourceId.HasValue && e.LineNo < 9000)
                .Select(e => e.SourceId!.Value).Distinct()
                .Where(id => !existingDebitIds.Contains(id)).ToListAsync();

            var orphanedCredits = await _context.LedgerEntries
                .Where(e => e.SourceType == LedgerSourceType.CreditNote && e.SourceId.HasValue && e.LineNo < 9000)
                .Select(e => e.SourceId!.Value).Distinct()
                .Where(id => !existingCreditIds.Contains(id)).ToListAsync();

            foreach (var id in orphanedDebits)
            {
                try
                {
                    await _ledgerPostingService.ReverseForHeaderDeleteAsync(LedgerSourceType.DebitNote, id, postedBy, "إصلاح إشعار خصم محذوف");
                    fixedCount++;
                }
                catch { }
            }
            foreach (var id in orphanedCredits)
            {
                try
                {
                    await _ledgerPostingService.ReverseForHeaderDeleteAsync(LedgerSourceType.CreditNote, id, postedBy, "إصلاح إشعار إضافة محذوف");
                    fixedCount++;
                }
                catch { }
            }

            await _ledgerPostingService.RecalcAllCustomerBalancesAsync();
            TempData["SuccessMessage"] = fixedCount > 0 ? $"تم إصلاح {fixedCount} قيد وإعادة حساب أرصدة العملاء." : "تم إعادة حساب أرصدة العملاء حسب القيود الحالية في دفتر الأستاذ.";
            return RedirectToAction(nameof(CustomerBalances), new { loadReport = true });
        }

        /// <summary>
        /// تصفير رصيد عميل محدد (حذف كل قيود LedgerEntries المرتبطة به + تصفير Customer.CurrentBalance).
        /// استخدم عند وجود رصيد شاذ أو لبدء حساب من جديد.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ZeroCustomerBalance(int customerId)
        {
            if (customerId <= 0)
            {
                TempData["ErrorMessage"] = "معرّف العميل غير صالح.";
                return RedirectToAction(nameof(CustomerBalances), new { loadReport = true });
            }

            var customer = await _context.Customers.FindAsync(customerId);
            if (customer == null)
            {
                TempData["ErrorMessage"] = "العميل غير موجود.";
                return RedirectToAction(nameof(CustomerBalances), new { loadReport = true });
            }

            var entries = await _context.LedgerEntries
                .Where(e => e.CustomerId == customerId)
                .ToListAsync();

            _context.LedgerEntries.RemoveRange(entries);
            customer.CurrentBalance = 0;
            customer.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"تم تصفير رصيد العميل «{customer.CustomerName ?? customerId.ToString()}» ({entries.Count} قيد). لا يمكن التراجع.";
            return RedirectToAction(nameof(CustomerBalances), new { loadReport = true });
        }

        /// <summary>
        /// مزامنة StockBatches من StockLedger (مصدر الحقيقة).
        /// يُصلح عدم التزامن عند حذف مشتريات/تسويات دون تحديث StockBatches.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> SyncStockBatchesFromLedger()
        {
            var ledgerBalances = await _context.StockLedger
                .AsNoTracking()
                .GroupBy(sl => new { sl.WarehouseId, sl.ProdId, BatchNo = sl.BatchNo ?? "", Expiry = sl.Expiry.HasValue ? sl.Expiry.Value.Date : (DateTime?)null })
                .Select(g => new
                {
                    g.Key.WarehouseId,
                    g.Key.ProdId,
                    g.Key.BatchNo,
                    g.Key.Expiry,
                    Qty = g.Sum(sl => sl.QtyIn - sl.QtyOut)
                })
                .ToListAsync();

            int updated = 0, created = 0;
            foreach (var lb in ledgerBalances)
            {
                int qtyToSet = Math.Max(0, lb.Qty);
                var sb = await _context.StockBatches.FirstOrDefaultAsync(x =>
                    x.WarehouseId == lb.WarehouseId && x.ProdId == lb.ProdId &&
                    (x.BatchNo ?? "").Trim() == (lb.BatchNo ?? "").Trim() &&
                    ((x.Expiry.HasValue ? x.Expiry.Value.Date : (DateTime?)null) == lb.Expiry));
                if (sb != null)
                {
                    if (sb.QtyOnHand != qtyToSet)
                    {
                        sb.QtyOnHand = qtyToSet;
                        sb.UpdatedAt = DateTime.UtcNow;
                        sb.Note = $"مزامنة من StockLedger {DateTime.UtcNow:yyyy-MM-dd}";
                        updated++;
                    }
                }
                else if (qtyToSet > 0)
                {
                    _context.StockBatches.Add(new StockBatch
                    {
                        WarehouseId = lb.WarehouseId,
                        ProdId = lb.ProdId,
                        BatchNo = lb.BatchNo ?? "",
                        Expiry = lb.Expiry,
                        QtyOnHand = qtyToSet,
                        UpdatedAt = DateTime.UtcNow,
                        Note = $"مزامنة من StockLedger {DateTime.UtcNow:yyyy-MM-dd}"
                    });
                    created++;
                }
            }
            // تصفير StockBatches التي ليس لها رصيد في Ledger (أرصدة يتيمة)
            var allBatches = await _context.StockBatches.Where(sb => sb.QtyOnHand > 0).ToListAsync();
            foreach (var sb in allBatches)
            {
                var key = (sb.WarehouseId, sb.ProdId, BatchNo: (sb.BatchNo ?? "").Trim(), Expiry: sb.Expiry.HasValue ? sb.Expiry.Value.Date : (DateTime?)null);
                if (!ledgerBalances.Any(lb => lb.WarehouseId == key.WarehouseId && lb.ProdId == key.ProdId &&
                    (lb.BatchNo ?? "").Trim() == key.BatchNo && ((lb.Expiry.HasValue ? lb.Expiry.Value.Date : (DateTime?)null) == key.Expiry)))
                {
                    sb.QtyOnHand = 0;
                    sb.UpdatedAt = DateTime.UtcNow;
                    sb.Note = $"مزامنة: تصفير (لا رصيد في Ledger) {DateTime.UtcNow:yyyy-MM-dd}";
                    updated++;
                }
            }
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"تمت المزامنة: {updated} تحديث، {created} إنشاء. StockBatches الآن مطابق لـ StockLedger.";
            return RedirectToAction(nameof(ProductBalances), new { loadReport = true });
        }

        /// <summary>
        /// حذف قيود StockLedger اليتيمة (مصدرها محذوف: مشتريات، مرتجعات، تسويات، تحويلات).
        /// يُصلح ظهور كمية بدون تكلفة عند حذف المستند الأصلي دون تنظيف StockLedger.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CleanOrphanedStockLedger()
        {
            int removed = 0;

            // Purchase: SourceId = PIId
            var existingPIIds = await _context.PurchaseInvoices.Select(p => p.PIId).ToListAsync();
            var orphanPurchase = await _context.StockLedger
                .Where(sl => sl.SourceType == "Purchase" && !existingPIIds.Contains(sl.SourceId))
                .ToListAsync();
            if (orphanPurchase.Any()) { _context.StockLedger.RemoveRange(orphanPurchase); removed += orphanPurchase.Count; }

            // Sales: SourceId = SIId
            var existingSIIds = await _context.SalesInvoices.Select(s => s.SIId).ToListAsync();
            var orphanSales = await _context.StockLedger
                .Where(sl => sl.SourceType == "Sales" && !existingSIIds.Contains(sl.SourceId))
                .ToListAsync();
            if (orphanSales.Any()) { _context.StockLedger.RemoveRange(orphanSales); removed += orphanSales.Count; }

            // SalesReturn: SourceId = SRId
            var existingSRIds = await _context.SalesReturns.Select(s => s.SRId).ToListAsync();
            var orphanSR = await _context.StockLedger
                .Where(sl => sl.SourceType == "SalesReturn" && !existingSRIds.Contains(sl.SourceId))
                .ToListAsync();
            if (orphanSR.Any()) { _context.StockLedger.RemoveRange(orphanSR); removed += orphanSR.Count; }

            // PurchaseReturn: SourceId = PRetId
            var existingPRetIds = await _context.PurchaseReturns.Select(p => p.PRetId).ToListAsync();
            var orphanPRet = await _context.StockLedger
                .Where(sl => sl.SourceType == "PurchaseReturn" && !existingPRetIds.Contains(sl.SourceId))
                .ToListAsync();
            if (orphanPRet.Any()) { _context.StockLedger.RemoveRange(orphanPRet); removed += orphanPRet.Count; }

            // Adjustment: SourceId = StockAdjustment Id
            var existingAdjIds = await _context.StockAdjustments.Select(a => a.Id).ToListAsync();
            var orphanAdj = await _context.StockLedger
                .Where(sl => sl.SourceType == "Adjustment" && !existingAdjIds.Contains(sl.SourceId))
                .ToListAsync();
            if (orphanAdj.Any()) { _context.StockLedger.RemoveRange(orphanAdj); removed += orphanAdj.Count; }

            // TransferIn / TransferOut: SourceId = StockTransfer Id
            var existingTransferIds = await _context.StockTransfers.Select(t => t.Id).ToListAsync();
            var orphanTransfer = await _context.StockLedger
                .Where(sl => (sl.SourceType == "TransferIn" || sl.SourceType == "TransferOut") && !existingTransferIds.Contains(sl.SourceId))
                .ToListAsync();
            if (orphanTransfer.Any()) { _context.StockLedger.RemoveRange(orphanTransfer); removed += orphanTransfer.Count; }

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = removed > 0
                ? $"تم حذف {removed} قيد يتيم من StockLedger. نفّذ «مزامنة الأرصدة» لتحديث StockBatches."
                : "لا توجد قيود يتيمة في StockLedger.";
            return RedirectToAction(nameof(ProductBalances), new { loadReport = true });
        }

        /// <summary>
        /// تصفير رصيد صنف محدد (حذف كل قيود StockLedger + تصفير StockBatches).
        /// استخدم عند وجود رصيد شاذ من مصدر غير معروف بعد التنظيف.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ZeroProductBalance(int prodId, int? warehouseId = null)
        {
            if (prodId <= 0)
            {
                TempData["ErrorMessage"] = "معرّف الصنف غير صالح.";
                return RedirectToAction(nameof(ProductBalances), new { loadReport = true });
            }

            var ledgers = await _context.StockLedger
                .Where(sl => sl.ProdId == prodId && (!warehouseId.HasValue || sl.WarehouseId == warehouseId.Value))
                .ToListAsync();

            var entryIds = ledgers.Select(l => l.EntryId).ToList();
            if (entryIds.Any())
            {
                var fifoMaps = await _context.Set<StockFifoMap>()
                    .Where(f => entryIds.Contains(f.OutEntryId) || entryIds.Contains(f.InEntryId))
                    .ToListAsync();
                if (fifoMaps.Any())
                    _context.Set<StockFifoMap>().RemoveRange(fifoMaps);
                _context.StockLedger.RemoveRange(ledgers);
            }

            var batches = await _context.StockBatches
                .Where(sb => sb.ProdId == prodId && (!warehouseId.HasValue || sb.WarehouseId == warehouseId.Value))
                .ToListAsync();
            foreach (var sb in batches)
            {
                sb.QtyOnHand = 0;
                sb.UpdatedAt = DateTime.UtcNow;
                sb.Note = $"تصفير يدوي {DateTime.UtcNow:yyyy-MM-dd}";
            }

            await _context.SaveChangesAsync();
            var prod = await _context.Products.FindAsync(prodId);
            TempData["SuccessMessage"] = $"تم تصفير رصيد الصنف «{prod?.ProdName ?? prodId.ToString()}» ({ledgers.Count} قيد، {batches.Count} تشغيلة).";
            return RedirectToAction(nameof(ProductBalances), new { loadReport = true });
        }

        // =========================================================
        // تقرير: أرصدة الأصناف
        // يعرض الصنف، الكمية الحالية، الخصم المرجح، المبيعات بين تاريخين، 
        // سعر الجمهور، تكلفة العلبة، والتكلفة الإجمالية
        // =========================================================
        [HttpGet]
        [RequirePermission("Reports.ProductBalances")]
        public async Task<IActionResult> ProductBalances(
            string? search,
            int? categoryId,
            int? productGroupId,
            bool? hasBonus,
            int? warehouseId,
            DateTime? fromDate,
            DateTime? toDate,
            bool includeZeroQty = false,
            bool includeBatches = true,
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

            var productGroups = await _context.ProductGroups
                .AsNoTracking()
                .Where(g => g.IsActive)
                .OrderBy(g => g.Name)
                .Select(g => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                {
                    Value = g.ProductGroupId.ToString(),
                    Text = g.Name ?? ""
                })
                .ToListAsync();

            ViewBag.Categories = categories;
            ViewBag.Warehouses = warehouses;
            ViewBag.ProductGroups = productGroups;

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
            // 2) تجهيز الفلاتر — المخزن: نقرأه دائماً من الطلب لضمان بقاء الاختيار بعد التجميع
            // =========================================================
            var whFromQuery = Request.Query["warehouseId"].FirstOrDefault();
            if (!string.IsNullOrEmpty(whFromQuery) && int.TryParse(whFromQuery, out var whIdParsed))
                warehouseId = whIdParsed;

            ViewBag.Search = search ?? "";
            ViewBag.CategoryId = categoryId;
            ViewBag.ProductGroupId = productGroupId;
            ViewBag.HasBonus = hasBonus;
            ViewBag.WarehouseId = warehouseId;
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;
            ViewBag.IncludeZeroQty = includeZeroQty;
            ViewBag.IncludeBatches = includeBatches;
            ViewBag.SortBy = sortBy;
            ViewBag.SortDir = sortDir;

            // =========================================================
            // 3) تحميل البيانات فقط عند الضغط على "تجميع التقرير"
            // =========================================================
            if (!loadReport)
            {
                // الصفحة تفتح بدون بيانات - فقط الفلاتر (عرض التشغيلات مفعّل افتراضياً)
                ViewBag.IncludeZeroQty = false;
                ViewBag.IncludeBatches = true;
                ViewBag.ReportData = new List<ProductBalanceReportDto>();
                ViewBag.TotalCost = 0m;
                return View();
            }

            // عند تحميل التقرير: إذا لم يتم تحديد includeZeroQty في الـ query، اجعله false افتراضياً
            // (لأن checkbox غير المُفعّل لا يُرسل في الـ form GET)
            string? includeZeroQtyStr = Request.Query["includeZeroQty"].FirstOrDefault();
            if (string.IsNullOrEmpty(includeZeroQtyStr))
            {
                includeZeroQty = false; // الافتراضي: عدم عرض الصفر
                ViewBag.IncludeZeroQty = false;
            }

            // عند عدم تحديد تاريخ: لا نطبّق فلتر "أصناف تم شراؤها في الفترة" (نعرض الكل حسب الفلاتر الأخرى)
            // عند التحديد: نقتصر على أصناف لها حركة شراء (StockLedger SourceType=Purchase) في المدى [fromDate, toDate]
            DateTime? fromDateUtc = fromDate.HasValue ? (DateTime?)DateTime.SpecifyKind(fromDate.Value, DateTimeKind.Local) : null;
            DateTime? toDateUtc = toDate.HasValue ? (DateTime?)DateTime.SpecifyKind(toDate.Value, DateTimeKind.Local) : null;

            // =========================================================
            // 4) بناء الاستعلام الأساسي للأصناف (عند طلب التقرير)
            // =========================================================
            var productsQuery = _context.Products
                .AsNoTracking()
                .Include(p => p.Category)
                .Include(p => p.ProductGroup)
                .Include(p => p.ProductBonusGroup)
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

            // فلتر مجموعة الصنف
            if (productGroupId.HasValue && productGroupId.Value > 0)
            {
                productsQuery = productsQuery.Where(p => p.ProductGroupId == productGroupId.Value);
            }

            // فلتر أصناف عليها بونص
            if (hasBonus == true)
            {
                productsQuery = productsQuery.Where(p => p.ProductBonusGroupId != null);
            }

            // فلتر الأصناف النشطة فقط (افتراضي)
            productsQuery = productsQuery.Where(p => p.IsActive == true);

            // =========================================================
            // 5) تحميل قائمة الأصناف المرشحة
            // =========================================================
            var productIds = await productsQuery.Select(p => p.ProdId).ToListAsync();

            // 5.1) عند تحديد من/إلى تاريخ: نقتصر على أصناف تم شراؤها في هذه الفترة (StockLedger Purchase + TranDate)
            if (productIds.Count > 0 && (fromDateUtc.HasValue || toDateUtc.HasValue))
            {
                var purchaseInRangeQuery = _context.StockLedger
                    .AsNoTracking()
                    .Where(sl => sl.SourceType == "Purchase" && productIds.Contains(sl.ProdId));
                if (fromDateUtc.HasValue)
                    purchaseInRangeQuery = purchaseInRangeQuery.Where(sl => sl.TranDate >= fromDateUtc.Value);
                if (toDateUtc.HasValue)
                    purchaseInRangeQuery = purchaseInRangeQuery.Where(sl => sl.TranDate <= toDateUtc.Value);
                var purchasedIds = await purchaseInRangeQuery.Select(sl => sl.ProdId).Distinct().ToListAsync();
                productIds = productIds.Intersect(purchasedIds).ToList();
            }

            if (productIds.Count == 0)
            {
                ViewBag.ReportData = new List<ProductBalanceReportDto>();
                ViewBag.TotalCost = 0m;
                return View();
            }

            // عند اختيار مخزن: نضمّن أصنافاً لها أي حركة (تحويل أو شراء أو غيره) في هذا المخزن حتى تظهر الأصناف المحوّلة (أصناف نشطة فقط)
            if (warehouseId.HasValue && warehouseId.Value > 0)
            {
                var inWarehouseIds = await _context.StockLedger
                    .AsNoTracking()
                    .Where(sl => sl.WarehouseId == warehouseId.Value)
                    .Select(sl => sl.ProdId)
                    .Distinct()
                    .ToListAsync();
                var activeProductIds = await _context.Products.Where(p => p.IsActive).Select(p => p.ProdId).ToListAsync();
                productIds = productIds.Union(inWarehouseIds.Intersect(activeProductIds)).Distinct().ToList();
            }

            // =========================================================
            // 6) تحميل البيانات بشكل مجمع (Bulk Loading) - تحسين الأداء
            // =========================================================
            
            // 6.1) تحميل جميع Products دفعة واحدة (لجميع productIds بعد دمج أصناف المخزن)
            var productsDict = await _context.Products
                .AsNoTracking()
                .Where(p => productIds.Contains(p.ProdId))
                .Include(p => p.Category)
                .Include(p => p.ProductGroup)
                .Include(p => p.ProductBonusGroup)
                .Select(p => new
                {
                    p.ProdId,
                    p.ProdName,
                    CategoryName = p.Category != null ? p.Category.CategoryName : "",
                    ProductGroupName = p.ProductGroup != null ? p.ProductGroup.Name : "",
                    ProductBonusGroupName = p.ProductBonusGroup != null ? p.ProductBonusGroup.Name : "",
                    p.PriceRetail
                })
                .ToDictionaryAsync(p => p.ProdId);

            // 6.2) الكمية من StockLedger (مصدر الحقيقة) = Sum(QtyIn - QtyOut) لتجنب عدم التزامن مع StockBatches
            var ledgerQtyQuery = _context.StockLedger
                .AsNoTracking()
                .Where(sl => productIds.Contains(sl.ProdId));
            if (warehouseId.HasValue && warehouseId.Value > 0)
                ledgerQtyQuery = ledgerQtyQuery.Where(sl => sl.WarehouseId == warehouseId.Value);
            var stockQuantities = await ledgerQtyQuery
                .GroupBy(sl => sl.ProdId)
                .Select(g => new { ProdId = g.Key, TotalQty = g.Sum(sl => sl.QtyIn - sl.QtyOut) })
                .ToDictionaryAsync(x => x.ProdId, x => Math.Max(0, x.TotalQty));

            // 6.3) تحميل StockLedger للخصم المرجح والتكلفة (Purchase مع RemainingQty > 0)
            var stockLedgerCostQuery = _context.StockLedger
                .AsNoTracking()
                .Where(x =>
                    productIds.Contains(x.ProdId) &&
                    x.SourceType == "Purchase" &&
                    (x.RemainingQty ?? 0) > 0);
            if (warehouseId.HasValue && warehouseId.Value > 0)
                stockLedgerCostQuery = stockLedgerCostQuery.Where(x => x.WarehouseId == warehouseId.Value);
            var stockLedgerDiscount = await stockLedgerCostQuery
                .GroupBy(x => x.ProdId)
                .Select(g => new
                {
                    ProdId = g.Key,
                    TotalRemaining = g.Sum(x => (decimal)(x.RemainingQty ?? 0)),
                    WeightedDiscount = g.Sum(x => (decimal)(x.RemainingQty ?? 0) * ((decimal?)(x.PurchaseDiscount) ?? 0m)),
                    WeightedCost = g.Sum(x => (decimal)(x.RemainingQty ?? 0) * x.UnitCost)
                })
                .ToDictionaryAsync(x => x.ProdId);

            // 6.3.1) تحميل بيانات التشغيلات (كل ProdId + BatchNo + Expiry) لعرض صنف بتشغيلتين أو أكثر
            var batchLedgerQuery = _context.StockLedger
                .AsNoTracking()
                .Where(x =>
                    productIds.Contains(x.ProdId) &&
                    x.SourceType == "Purchase" &&
                    (x.RemainingQty ?? 0) > 0);
            if (warehouseId.HasValue && warehouseId.Value > 0)
                batchLedgerQuery = batchLedgerQuery.Where(x => x.WarehouseId == warehouseId.Value);
            var batchRowsRaw = await batchLedgerQuery
                .GroupBy(x => new { x.ProdId, x.BatchNo, x.Expiry })
                .Select(g => new
                {
                    g.Key.ProdId,
                    g.Key.BatchNo,
                    g.Key.Expiry,
                    TotalRemaining = g.Sum(x => x.RemainingQty ?? 0),
                    WeightedDiscount = g.Sum(x => (decimal)(x.RemainingQty ?? 0) * ((decimal?)(x.PurchaseDiscount) ?? 0m)),
                    WeightedCost = g.Sum(x => (decimal)(x.RemainingQty ?? 0) * x.UnitCost)
                })
                .ToListAsync();
            // تجميع التشغيلات حسب الصنف مع التأكد أن كل عنصر يخص نفس الصنف فقط
            var batchesByProdId = productIds.Distinct().ToDictionary(
                pid => pid,
                pid => batchRowsRaw.Where(b => b.ProdId == pid).ToList());

            var batchMasterList = await _context.Batches
                .AsNoTracking()
                .Where(b => productIds.Contains(b.ProdId))
                .Select(b => new { b.BatchId, b.ProdId, b.BatchNo, b.Expiry, b.PriceRetailBatch })
                .ToListAsync();
            var batchLookup = batchMasterList
                .GroupBy(b => b.ProdId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // 6.3.2) تحميل أحدث الخصم اليدوي (ProductDiscountOverrides) لكل صنف/مخزن/تشغيلة
            var overridesList = await _context.ProductDiscountOverrides
                .AsNoTracking()
                .Where(x => productIds.Contains(x.ProductId) && (x.WarehouseId == null || (warehouseId.HasValue && x.WarehouseId == warehouseId.Value)))
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new { x.ProductId, x.WarehouseId, x.BatchId, x.OverrideDiscountPct, x.CreatedAt })
                .ToListAsync();

            decimal? GetLatestOverride(int p, int? w, int? b)
            {
                var match = overridesList
                    .Where(o => o.ProductId == p && (!w.HasValue || o.WarehouseId == null || o.WarehouseId == w) && (o.BatchId == null || (b.HasValue && o.BatchId == b)))
                    .OrderByDescending(o => o.BatchId.HasValue ? 1 : 0)
                    .ThenByDescending(o => o.WarehouseId.HasValue ? 1 : 0)
                    .ThenByDescending(o => o.CreatedAt)
                    .FirstOrDefault();
                return match?.OverrideDiscountPct;
            }

            // 6.4) تحميل المبيعات: إذا وُجد تاريخ محدد نقتصر على المدى، وإلا نعرض كل المبيعات
            var salesQuery = _context.SalesInvoiceLines
                .AsNoTracking()
                .Include(sil => sil.SalesInvoice)
                .Where(sil =>
                    productIds.Contains(sil.ProdId) &&
                    sil.SalesInvoice.IsPosted);

            if (warehouseId.HasValue && warehouseId.Value > 0)
                salesQuery = salesQuery.Where(sil => sil.SalesInvoice.WarehouseId == warehouseId.Value);

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

            var salesQuantities = await salesQuery
                .GroupBy(sil => sil.ProdId)
                .Select(g => new { ProdId = g.Key, TotalQty = g.Sum(sil => (decimal?)sil.Qty) ?? 0m })
                .ToDictionaryAsync(x => x.ProdId, x => x.TotalQty);

            // 6.4.0) مبيعات حسب التشغيلة (ProdId + BatchNo + Expiry) لعرض المبيعات أمام كل تشغيلة
            var salesLinesByBatch = await salesQuery
                .Select(sil => new { sil.ProdId, BatchNo = (sil.BatchNo ?? "").Trim(), sil.Expiry, Qty = (decimal)sil.Qty })
                .ToListAsync();
            var salesByBatchKey = salesLinesByBatch
                .GroupBy(x => new { x.ProdId, x.BatchNo, ExpiryDate = x.Expiry.HasValue ? x.Expiry!.Value.Date : (DateTime?)null })
                .ToDictionary(g => (g.Key.ProdId, g.Key.BatchNo, g.Key.ExpiryDate), g => g.Sum(x => x.Qty));

            // 6.4.1) مرتجعات البيع (خصم من كمية المبيعات): نفس منطق التاريخ (محدد = في المدى، غير محدد = الكل)
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
            var returnQuantities = await returnsQuery
                .GroupBy(srl => srl.ProdId)
                .Select(g => new { ProdId = g.Key, TotalQty = g.Sum(srl => (decimal?)srl.Qty) ?? 0m })
                .ToDictionaryAsync(x => x.ProdId, x => x.TotalQty);

            // مرتجعات حسب التشغيلة (لخصمها من مبيعات كل تشغيلة)
            var returnsLinesByBatch = await returnsQuery
                .Select(srl => new { srl.ProdId, BatchNo = (srl.BatchNo ?? "").Trim(), srl.Expiry, Qty = (decimal)srl.Qty })
                .ToListAsync();
            var returnsByBatchKey = returnsLinesByBatch
                .GroupBy(x => new { x.ProdId, x.BatchNo, ExpiryDate = x.Expiry.HasValue ? x.Expiry!.Value.Date : (DateTime?)null })
                .ToDictionary(g => (g.Key.ProdId, g.Key.BatchNo, g.Key.ExpiryDate), g => g.Sum(x => x.Qty));

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

                // الخصم المرجح (محسوب من StockLedger: متوسط موزون لخصم الشراء حسب الكمية المتبقية)
                decimal weightedDiscount = 0m;
                if (stockLedgerDiscount.TryGetValue(prodId, out var discountData) && discountData.TotalRemaining > 0)
                    weightedDiscount = discountData.WeightedDiscount / discountData.TotalRemaining;

                // صافي المبيعات (المبيعات − مرتجعات البيع)
                decimal salesQty = salesQuantities.TryGetValue(prodId, out var sales) ? sales : 0m;
                if (returnQuantities.TryGetValue(prodId, out var retQty))
                    salesQty = Math.Max(0m, salesQty - retQty);

                // تكلفة العلبة (محسوبة من StockLedger: متوسط موزون للتكلفة حسب الكمية المتبقية)
                decimal unitCost = 0m;
                if (stockLedgerDiscount.TryGetValue(prodId, out var costData) && costData.TotalRemaining > 0)
                    unitCost = costData.WeightedCost / costData.TotalRemaining;

                // التكلفة الإجمالية
                decimal totalCost = currentQty * unitCost;

                // الخصم اليدوي والفعّال (الجدول الجديد هو مصدر الخصم في البيع)
                var manualPct = GetLatestOverride(prodId, warehouseId, null);
                var effectivePct = manualPct ?? weightedDiscount;
                var profitDelta = (effectivePct - weightedDiscount) * product.PriceRetail * currentQty / 100m;

                var dto = new ProductBalanceReportDto
                {
                    ProdId = prodId,
                    ProdCode = prodId.ToString(),
                    ProdName = product.ProdName ?? "",
                    CategoryName = product.CategoryName ?? "",
                    ProductGroupName = product.ProductGroupName ?? "",
                    ProductBonusGroupName = product.ProductBonusGroupName ?? "",
                    CurrentQty = currentQty,
                    WeightedDiscount = weightedDiscount,
                    ManualDiscountPct = manualPct,
                    EffectiveDiscountPct = effectivePct,
                    ProfitDeltaExpected = profitDelta,
                    SalesQty = salesQty,
                    PriceRetail = product.PriceRetail,
                    UnitCost = unitCost,
                    TotalCost = totalCost
                };
                // التشغيلات: نعرض فقط التشغيلات التي لها كميات فعلية في StockLedger، والصنف نعرض له قسم التشغيلات فقط إذا كان له أكثر من تشغيلة واحدة (٢+)
                if (includeBatches && batchLookup.TryGetValue(prodId, out var prodBatches) && prodBatches != null)
                {
                    var ledgerList = batchesByProdId.TryGetValue(prodId, out var blist) ? blist : null;
                    var batchRows = new List<ProductBalanceBatchRow>();
                    foreach (var m in prodBatches.OrderBy(m => m.Expiry).ThenBy(m => m.BatchNo ?? ""))
                    {
                        var bExp = m.Expiry.Date;
                        var match = ledgerList?.FirstOrDefault(b => (b.BatchNo ?? "").Trim() == (m.BatchNo ?? "").Trim() && (b.Expiry?.Date ?? DateTime.MinValue) == bExp);
                        decimal brQty = match != null ? match.TotalRemaining : 0m;
                        if (brQty <= 0) continue; // نعرض فقط التشغيلات التي لها كمية فعلية
                        decimal calcDisc = match != null ? match.WeightedDiscount / brQty : 0m;
                        decimal calcCost = match != null ? match.WeightedCost / brQty : 0m;
                        var manualBatch = GetLatestOverride(prodId, warehouseId, m.BatchId);
                        var effectiveBatch = manualBatch ?? calcDisc;
                        var batchKey = (prodId, BatchNo: (m.BatchNo ?? "").Trim(), ExpiryDate: m.Expiry.Date);
                        decimal batchSalesQty = salesByBatchKey.TryGetValue(batchKey, out var bs) ? bs : 0m;
                        if (returnsByBatchKey.TryGetValue(batchKey, out var br))
                            batchSalesQty = Math.Max(0m, batchSalesQty - br);
                        decimal batchPriceRetail = (m.PriceRetailBatch ?? 0m) > 0m ? (m.PriceRetailBatch ?? 0m) : product.PriceRetail;
                        batchRows.Add(new ProductBalanceBatchRow
                        {
                            BatchId = m.BatchId,
                            BatchNo = m.BatchNo,
                            Expiry = m.Expiry,
                            CurrentQty = (int)brQty,
                            WeightedDiscount = calcDisc,
                            ManualDiscountPct = manualBatch,
                            EffectiveDiscountPct = effectiveBatch,
                            UnitCost = calcCost,
                            TotalCost = brQty * calcCost,
                            SalesQty = batchSalesQty,
                            PriceRetail = batchPriceRetail
                        });
                    }
                    if (batchRows.Count >= 2) // الصنف له أكثر من تشغيلة واحدة فقط نعرض قسم التشغيلات
                    {
                        dto.Batches = batchRows;
                        // تكلفة الصف الرئيسي = إجمالي تكلفات التشغيلات المعروضة (حتى يتطابق الرقم مع مجموع الثلاث تشغيلات)
                        decimal batchesTotalCost = batchRows.Sum(b => b.TotalCost);
                        int batchesTotalQty = batchRows.Sum(b => b.CurrentQty);
                        dto.TotalCost = batchesTotalCost;
                        dto.UnitCost = batchesTotalQty > 0 ? batchesTotalCost / batchesTotalQty : unitCost;
                    }
                    else if (batchRows.Count == 1)
                    {
                        // تشغيلة واحدة: عرض رقم التشغيلة والتاريخ في الصف الرئيسي
                        dto.FirstBatchNo = batchRows[0].BatchNo;
                        dto.FirstBatchExpiry = batchRows[0].Expiry;
                    }
                }
                // إذا لم يُملأ التشغيلة/التاريخ من جدول Batches، استخدم أول تشغيلة من دفتر الحركة (StockLedger) إن وُجدت
                if (includeBatches && dto.FirstBatchNo == null && dto.Batches == null && batchesByProdId.TryGetValue(prodId, out var ledgerOnly) && ledgerOnly != null && ledgerOnly.Count > 0)
                {
                    var first = ledgerOnly.OrderBy(b => b.Expiry).ThenBy(b => b.BatchNo ?? "").First();
                    dto.FirstBatchNo = string.IsNullOrWhiteSpace(first.BatchNo) ? null : (first.BatchNo ?? "").Trim();
                    dto.FirstBatchExpiry = first.Expiry;
                }
                reportData.Add(dto);
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

        /// <summary>
        /// حفظ الخصم اليدوي للبيع من تقرير أرصدة الأصناف (AJAX).
        /// إذا القيمة فارغة: حذف أحدث override مطابق. وإلا: إدراج سجل جديد.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveManualDiscount(
            [FromForm] int productId,
            [FromForm] int? warehouseId,
            [FromForm] int? batchId,
            [FromForm] decimal? manualDiscountPct,
            [FromForm] string? reason)
        {
            if (productId <= 0)
            {
                return Json(new { success = false, message = "معرف الصنف غير صالح." });
            }

            var userName = User?.Identity?.Name ?? "SYSTEM";
            int? wh = (warehouseId.HasValue && warehouseId.Value > 0) ? warehouseId : null;
            int? bat = (batchId.HasValue && batchId.Value > 0) ? batchId : null;

            if (!manualDiscountPct.HasValue || (manualDiscountPct.HasValue && manualDiscountPct.Value < 0))
            {
                // إلغاء الخصم اليدوي: حذف أحدث سجل مطابق (خيار A)
                var toDelete = await _context.ProductDiscountOverrides
                    .Where(x => x.ProductId == productId
                        && (wh == null ? x.WarehouseId == null : x.WarehouseId == wh)
                        && (bat == null ? x.BatchId == null : x.BatchId == bat))
                    .OrderByDescending(x => x.CreatedAt)
                    .FirstOrDefaultAsync();

                if (toDelete != null)
                {
                    _context.ProductDiscountOverrides.Remove(toDelete);
                    await _context.SaveChangesAsync();
                    await _activityLogger.LogAsync(UserActionType.Edit, "ProductDiscountOverride", toDelete.Id, "إلغاء الخصم اليدوي للبيع (من تقرير أرصدة الأصناف)", newValues: $"{{\"ProductId\":{productId},\"WarehouseId\":{warehouseId},\"BatchId\":{batchId}}}");
                }
                return Json(new { success = true, message = "تم إلغاء الخصم اليدوي." });
            }

            decimal value = Math.Min(100m, Math.Max(0m, manualDiscountPct.Value));

            // عدم التكرار: إدراج سجل جديد فقط عند تغيّر القيمة (أحدث override لنفس المفتاح له نفس النسبة => لا إدراج)
            var latest = await _context.ProductDiscountOverrides
                .AsNoTracking()
                .Where(x => x.ProductId == productId
                    && (wh == null ? x.WarehouseId == null : x.WarehouseId == wh)
                    && (bat == null ? x.BatchId == null : x.BatchId == bat))
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new { x.OverrideDiscountPct })
                .FirstOrDefaultAsync();

            if (latest != null && latest.OverrideDiscountPct == value)
            {
                return Json(new { success = true, message = "القيمة نفسها مسجلة مسبقاً، لم يُضف سجل جديد." });
            }

            _context.ProductDiscountOverrides.Add(new ProductDiscountOverride
            {
                ProductId = productId,
                WarehouseId = wh,
                BatchId = bat,
                OverrideDiscountPct = value,
                Reason = reason?.Length > 200 ? reason.Substring(0, 200) : reason,
                CreatedBy = userName,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            await _activityLogger.LogAsync(UserActionType.Create, "ProductDiscountOverride", null, "تعيين خصم يدوي للبيع (من تقرير أرصدة الأصناف)", newValues: $"{{\"ProductId\":{productId},\"WarehouseId\":{warehouseId},\"BatchId\":{batchId},\"OverrideDiscountPct\":{value}}}");

            return Json(new { success = true, message = "تم حفظ الخصم اليدوي." });
        }

        // =========================================================
        // تقرير: مبيعات أصناف البونص لكل مستخدم
        // يجمع مبيعات كل صنف من أصناف البونص لكل مستخدم ويعرض إجمالي المبيعات وقيمة البونص
        // =========================================================
        [HttpGet]
        [RequirePermission("Reports.BonusReport")]
        public async Task<IActionResult> BonusReport(
            DateTime? fromDate,
            DateTime? toDate,
            int? warehouseId,
            bool loadReport = false)
        {
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;
            ViewBag.WarehouseId = warehouseId;

            var warehouses = await _context.Warehouses
                .AsNoTracking()
                .OrderBy(w => w.WarehouseName)
                .Select(w => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                {
                    Value = w.WarehouseId.ToString(),
                    Text = w.WarehouseName
                })
                .ToListAsync();
            ViewBag.Warehouses = warehouses;

            if (!loadReport)
            {
                ViewBag.ReportData = new List<BonusReportDto>();
                return View();
            }

            var today = DateTime.Today;
            if (!fromDate.HasValue && !toDate.HasValue)
            {
                fromDate = new DateTime(today.Year, today.Month, 1);
                toDate = today;
                ViewBag.FromDate = fromDate;
                ViewBag.ToDate = toDate;
            }

            var from = fromDate?.Date ?? DateTime.MinValue;
            var to = (toDate?.Date ?? DateTime.MaxValue).AddDays(1);

            var query = _context.SalesInvoiceLines
                .AsNoTracking()
                .Include(sil => sil.SalesInvoice)
                .Include(sil => sil.Product)
                    .ThenInclude(p => p!.ProductBonusGroup)
                .Where(sil =>
                    sil.SalesInvoice != null &&
                    sil.SalesInvoice.IsPosted &&
                    sil.Product != null &&
                    sil.Product.ProductBonusGroupId != null);

            if (warehouseId.HasValue && warehouseId.Value > 0)
            {
                query = query.Where(sil => sil.SalesInvoice!.WarehouseId == warehouseId.Value);
            }

            query = query.Where(sil => sil.SalesInvoice!.SIDate >= from && sil.SalesInvoice.SIDate < to);

            var grouped = await query
                .GroupBy(sil => new { UserName = sil.SalesInvoice!.CreatedBy ?? "", ProdId = sil.ProdId })
                .Select(g => new BonusReportDto
                {
                    UserName = g.Key.UserName,
                    ProdName = g.Max(sil => sil.Product != null ? (sil.Product.ProdName ?? "") : ""),
                    ProductBonusGroupName = g.Max(sil => sil.Product != null && sil.Product.ProductBonusGroup != null ? sil.Product.ProductBonusGroup.Name : ""),
                    BonusAmountPerUnit = g.Max(sil => sil.Product != null && sil.Product.ProductBonusGroup != null ? sil.Product.ProductBonusGroup.BonusAmount : 0m),
                    TotalQty = g.Sum(sil => sil.Qty),
                    TotalSalesValue = g.Sum(sil => sil.LineNetTotal),
                    TotalBonusAmount = g.Sum(sil => sil.Qty * (sil.Product != null && sil.Product.ProductBonusGroup != null ? sil.Product.ProductBonusGroup.BonusAmount : 0m))
                })
                .OrderBy(x => x.UserName)
                .ThenByDescending(x => x.TotalSalesValue)
                .ToListAsync();

            ViewBag.ReportData = grouped;
            ViewBag.TotalSalesValue = grouped.Sum(x => x.TotalSalesValue);
            ViewBag.TotalBonusAmount = grouped.Sum(x => x.TotalBonusAmount);
            ViewBag.TotalQty = grouped.Sum(x => x.TotalQty);

            return View();
        }

        // =========================================================
        // تقرير: أرصدة العملاء
        // يعرض العميل، الرصيد الحالي، الحد الائتماني، المبيعات والمشتريات بين تاريخين
        // =========================================================
        [HttpGet]
        [RequirePermission("Reports.CustomerBalances")]
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
                ViewBag.IncludeZeroBalance = false;
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

            // عند تحميل التقرير: إذا لم يتم تحديد includeZeroBalance في الـ query، اجعله false افتراضياً
            string? includeZeroBalanceStr = Request.Query["includeZeroBalance"].FirstOrDefault();
            if (string.IsNullOrEmpty(includeZeroBalanceStr))
            {
                includeZeroBalance = false;
                ViewBag.IncludeZeroBalance = false;
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
                    c.CreditLimit
                })
                .ToDictionaryAsync(c => c.CustomerId);

            // 6.1.1) حساب الرصيد الحالي من LedgerEntries (مصدر الحقيقة)
            var balanceByCustomer = await _context.LedgerEntries
                .AsNoTracking()
                .Where(e => e.CustomerId.HasValue && customerIds.Contains(e.CustomerId.Value))
                .GroupBy(e => e.CustomerId!.Value)
                .Select(g => new { CustomerId = g.Key, Balance = g.Sum(e => (decimal?)(e.Debit - e.Credit)) ?? 0m })
                .ToDictionaryAsync(x => x.CustomerId, x => x.Balance);

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

                decimal currentBalance = balanceByCustomer.TryGetValue(customerId, out var bal) ? bal : 0m;
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
                case "category":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.PartyCategory ?? "").ToList()
                        : reportData.OrderBy(r => r.PartyCategory ?? "").ToList();
                    break;
                case "phone":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.Phone1 ?? "").ToList()
                        : reportData.OrderBy(r => r.Phone1 ?? "").ToList();
                    break;
                case "balance":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.CurrentBalance).ToList()
                        : reportData.OrderBy(r => r.CurrentBalance).ToList();
                    break;
                case "creditlimit":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.CreditLimit).ToList()
                        : reportData.OrderBy(r => r.CreditLimit).ToList();
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
                case "availablecredit":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.AvailableCredit).ToList()
                        : reportData.OrderBy(r => r.AvailableCredit).ToList();
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
            // عند التصدير: إذا لم يتم تحديد includeZeroBalance، اجعله false افتراضياً
            string? includeZeroBalanceStr = Request.Query["includeZeroBalance"].FirstOrDefault();
            if (string.IsNullOrEmpty(includeZeroBalanceStr))
            {
                includeZeroBalance = false;
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
                    c.CreditLimit
                })
                .ToDictionaryAsync(c => c.CustomerId);

            var balanceByCustomer = await _context.LedgerEntries
                .AsNoTracking()
                .Where(e => e.CustomerId.HasValue && customerIds.Contains(e.CustomerId.Value))
                .GroupBy(e => e.CustomerId!.Value)
                .Select(g => new { CustomerId = g.Key, Balance = g.Sum(e => (decimal?)(e.Debit - e.Credit)) ?? 0m })
                .ToDictionaryAsync(x => x.CustomerId, x => x.Balance);

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

                decimal currentBalance = balanceByCustomer.TryGetValue(customerId, out var bal) ? bal : 0m;
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
                case "category":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.PartyCategory ?? "").ToList()
                        : reportData.OrderBy(r => r.PartyCategory ?? "").ToList();
                    break;
                case "phone":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.Phone1 ?? "").ToList()
                        : reportData.OrderBy(r => r.Phone1 ?? "").ToList();
                    break;
                case "balance":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.CurrentBalance).ToList()
                        : reportData.OrderBy(r => r.CurrentBalance).ToList();
                    break;
                case "creditlimit":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.CreditLimit).ToList()
                        : reportData.OrderBy(r => r.CreditLimit).ToList();
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
                case "availablecredit":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.AvailableCredit).ToList()
                        : reportData.OrderBy(r => r.AvailableCredit).ToList();
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
            int? productGroupId,
            bool? hasBonus,
            int? warehouseId,
            DateTime? fromDate,
            DateTime? toDate,
            bool includeZeroQty = false,
            bool includeBatches = true,
            string? sortBy = "name",
            string? sortDir = "asc")
        {
            // عند التصدير: إذا لم يتم تحديد includeZeroQty، اجعله false افتراضياً
            string? includeZeroQtyStr = Request.Query["includeZeroQty"].FirstOrDefault();
            if (string.IsNullOrEmpty(includeZeroQtyStr))
            {
                includeZeroQty = false;
            }

            // عند عدم تحديد تاريخ: لا نطبّق فلتر "أصناف تم شراؤها في الفترة"

            // بناء الاستعلام (نفس منطق ProductBalances)
            var productsQuery = _context.Products
                .AsNoTracking()
                .Include(p => p.Category)
                .Include(p => p.ProductGroup)
                .Include(p => p.ProductBonusGroup)
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

            if (productGroupId.HasValue && productGroupId.Value > 0)
            {
                productsQuery = productsQuery.Where(p => p.ProductGroupId == productGroupId.Value);
            }

            if (hasBonus == true)
            {
                productsQuery = productsQuery.Where(p => p.ProductBonusGroupId != null);
            }

            productsQuery = productsQuery.Where(p => p.IsActive == true);

            var productIds = await productsQuery.Select(p => p.ProdId).ToListAsync();

            // أصناف تم شراؤها في الفترة (من إلى مع وقت) — نفس منطق ProductBalances
            if (productIds.Count > 0 && (fromDate.HasValue || toDate.HasValue))
            {
                var fromDateUtc = fromDate.HasValue ? (DateTime?)DateTime.SpecifyKind(fromDate.Value, DateTimeKind.Local) : null;
                var toDateUtc = toDate.HasValue ? (DateTime?)DateTime.SpecifyKind(toDate.Value, DateTimeKind.Local) : null;
                var purchaseInRangeQuery = _context.StockLedger
                    .AsNoTracking()
                    .Where(sl => sl.SourceType == "Purchase" && productIds.Contains(sl.ProdId));
                if (fromDateUtc.HasValue)
                    purchaseInRangeQuery = purchaseInRangeQuery.Where(sl => sl.TranDate >= fromDateUtc.Value);
                if (toDateUtc.HasValue)
                    purchaseInRangeQuery = purchaseInRangeQuery.Where(sl => sl.TranDate <= toDateUtc.Value);
                var purchasedIds = await purchaseInRangeQuery.Select(sl => sl.ProdId).Distinct().ToListAsync();
                productIds = productIds.Intersect(purchasedIds).ToList();
            }

            if (productIds.Count == 0)
            {
                return BadRequest("لا توجد بيانات للتصدير");
            }

            // تحميل البيانات بشكل مجمع (نفس منطق ProductBalances، مع الخصم/التكلفة المعدّلة)
            var productsDict = await productsQuery
                .Select(p => new
                {
                    p.ProdId,
                    p.ProdName,
                    CategoryName = p.Category != null ? p.Category.CategoryName : "",
                    ProductGroupName = p.ProductGroup != null ? p.ProductGroup.Name : "",
                    ProductBonusGroupName = p.ProductBonusGroup != null ? p.ProductBonusGroup.Name : "",
                    p.PriceRetail
                })
                .ToDictionaryAsync(p => p.ProdId);

            // الكمية من StockLedger (مصدر الحقيقة) - نفس منطق ProductBalances
            var ledgerQtyQueryExp = _context.StockLedger
                .AsNoTracking()
                .Where(sl => productIds.Contains(sl.ProdId));
            if (warehouseId.HasValue && warehouseId.Value > 0)
                ledgerQtyQueryExp = ledgerQtyQueryExp.Where(sl => sl.WarehouseId == warehouseId.Value);
            var stockQuantities = await ledgerQtyQueryExp
                .GroupBy(sl => sl.ProdId)
                .Select(g => new { ProdId = g.Key, TotalQty = g.Sum(sl => sl.QtyIn - sl.QtyOut) })
                .ToDictionaryAsync(x => x.ProdId, x => Math.Max(0, x.TotalQty));

            var stockLedgerCostQueryExp = _context.StockLedger
                .AsNoTracking()
                .Where(x =>
                    productIds.Contains(x.ProdId) &&
                    x.SourceType == "Purchase" &&
                    (x.RemainingQty ?? 0) > 0);
            if (warehouseId.HasValue && warehouseId.Value > 0)
                stockLedgerCostQueryExp = stockLedgerCostQueryExp.Where(x => x.WarehouseId == warehouseId.Value);
            var stockLedgerDiscount = await stockLedgerCostQueryExp
                .GroupBy(x => x.ProdId)
                .Select(g => new
                {
                    ProdId = g.Key,
                    TotalRemaining = g.Sum(x => (decimal)(x.RemainingQty ?? 0)),
                    WeightedDiscount = g.Sum(x => (decimal)(x.RemainingQty ?? 0) * ((decimal?)(x.PurchaseDiscount) ?? 0m)),
                    WeightedCost = g.Sum(x => (decimal)(x.RemainingQty ?? 0) * x.UnitCost)
                })
                .ToDictionaryAsync(x => x.ProdId);

            var batchLedgerQueryExp = _context.StockLedger.AsNoTracking()
                .Where(x => productIds.Contains(x.ProdId) && x.SourceType == "Purchase" && (x.RemainingQty ?? 0) > 0);
            if (warehouseId.HasValue && warehouseId.Value > 0)
                batchLedgerQueryExp = batchLedgerQueryExp.Where(x => x.WarehouseId == warehouseId.Value);
            var batchRowsRawExp = await batchLedgerQueryExp
                .GroupBy(x => new { x.ProdId, x.BatchNo, x.Expiry })
                .Select(g => new { g.Key.ProdId, g.Key.BatchNo, g.Key.Expiry, TotalRemaining = g.Sum(x => x.RemainingQty ?? 0), WeightedDiscount = g.Sum(x => (decimal)(x.RemainingQty ?? 0) * ((decimal?)(x.PurchaseDiscount) ?? 0m)), WeightedCost = g.Sum(x => (decimal)(x.RemainingQty ?? 0) * x.UnitCost) })
                .ToListAsync();
            var batchesByProdIdExp = productIds.Distinct().ToDictionary(pid => pid, pid => batchRowsRawExp.Where(b => b.ProdId == pid).ToList());
            var batchMasterListExp = await _context.Batches.AsNoTracking().Where(b => productIds.Contains(b.ProdId)).Select(b => new { b.BatchId, b.ProdId, b.BatchNo, b.Expiry }).ToListAsync();
            var batchLookupExp = batchMasterListExp.GroupBy(b => b.ProdId).ToDictionary(g => g.Key, g => g.ToList());

            // تحميل الخصم اليدوي للتصدير (نفس منطق ProductBalances)
            var overridesListExp = await _context.ProductDiscountOverrides
                .AsNoTracking()
                .Where(x => productIds.Contains(x.ProductId) && (x.WarehouseId == null || (warehouseId.HasValue && x.WarehouseId == warehouseId.Value)))
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new { x.ProductId, x.WarehouseId, x.BatchId, x.OverrideDiscountPct, x.CreatedAt })
                .ToListAsync();
            decimal? GetLatestOverrideExp(int p, int? w, int? b)
            {
                var match = overridesListExp
                    .Where(o => o.ProductId == p && (!w.HasValue || o.WarehouseId == null || o.WarehouseId == w) && (o.BatchId == null || (b.HasValue && o.BatchId == b)))
                    .OrderByDescending(o => o.BatchId.HasValue ? 1 : 0)
                    .ThenByDescending(o => o.WarehouseId.HasValue ? 1 : 0)
                    .ThenByDescending(o => o.CreatedAt)
                    .FirstOrDefault();
                return match?.OverrideDiscountPct;
            }

            // المبيعات ومرتجعات البيع: إذا وُجد تاريخ محدد نقتصر على المدى، وإلا كل المبيعات/المرتجعات
            var salesQueryExp = _context.SalesInvoiceLines
                .AsNoTracking()
                .Include(sil => sil.SalesInvoice)
                .Where(sil =>
                    productIds.Contains(sil.ProdId) &&
                    sil.SalesInvoice.IsPosted);
            if (warehouseId.HasValue && warehouseId.Value > 0)
                salesQueryExp = salesQueryExp.Where(sil => sil.SalesInvoice.WarehouseId == warehouseId.Value);
            if (fromDate.HasValue)
            {
                var from = fromDate.Value.Date;
                salesQueryExp = salesQueryExp.Where(sil => sil.SalesInvoice.SIDate >= from);
            }
            if (toDate.HasValue)
            {
                var to = toDate.Value.Date.AddDays(1);
                salesQueryExp = salesQueryExp.Where(sil => sil.SalesInvoice.SIDate < to);
            }
            var salesQuantities = await salesQueryExp
                .GroupBy(sil => sil.ProdId)
                .Select(g => new { ProdId = g.Key, TotalQty = g.Sum(sil => (decimal?)sil.Qty) ?? 0m })
                .ToDictionaryAsync(x => x.ProdId, x => x.TotalQty);

            var returnsQueryExp = _context.SalesReturnLines
                .AsNoTracking()
                .Include(srl => srl.SalesReturn)
                .Where(srl => productIds.Contains(srl.ProdId) && srl.SalesReturn != null && srl.SalesReturn.IsPosted);
            if (warehouseId.HasValue && warehouseId.Value > 0)
                returnsQueryExp = returnsQueryExp.Where(srl => srl.SalesReturn!.WarehouseId == warehouseId.Value);
            if (fromDate.HasValue)
                returnsQueryExp = returnsQueryExp.Where(srl => srl.SalesReturn!.SRDate >= fromDate.Value.Date);
            if (toDate.HasValue)
                returnsQueryExp = returnsQueryExp.Where(srl => srl.SalesReturn!.SRDate < toDate.Value.Date.AddDays(1));
            var returnQuantities = await returnsQueryExp
                .GroupBy(srl => srl.ProdId)
                .Select(g => new { ProdId = g.Key, TotalQty = g.Sum(srl => (decimal?)srl.Qty) ?? 0m })
                .ToDictionaryAsync(x => x.ProdId, x => x.TotalQty);

            var reportData = new List<ProductBalanceReportDto>();

            foreach (var prodId in productIds)
            {
                if (!productsDict.TryGetValue(prodId, out var product)) continue;

                int currentQty = stockQuantities.TryGetValue(prodId, out var qty) ? qty : 0;

                if (!includeZeroQty && currentQty == 0)
                    continue;

                decimal weightedDiscount = 0m;
                if (stockLedgerDiscount.TryGetValue(prodId, out var discountDataExp) && discountDataExp.TotalRemaining > 0)
                    weightedDiscount = discountDataExp.WeightedDiscount / discountDataExp.TotalRemaining;

                decimal salesQty = salesQuantities.TryGetValue(prodId, out var sales) ? sales : 0m;
                if (returnQuantities.TryGetValue(prodId, out var retQty))
                    salesQty = Math.Max(0m, salesQty - retQty);

                decimal unitCost = 0m;
                if (stockLedgerDiscount.TryGetValue(prodId, out var costDataExp) && costDataExp.TotalRemaining > 0)
                    unitCost = costDataExp.WeightedCost / costDataExp.TotalRemaining;

                decimal totalCost = currentQty * unitCost;
                var manualPctExp = GetLatestOverrideExp(prodId, warehouseId, null);
                var effectivePctExp = manualPctExp ?? weightedDiscount;
                var profitDeltaExp = (effectivePctExp - weightedDiscount) * product.PriceRetail * currentQty / 100m;

                var dtoExp = new ProductBalanceReportDto
                {
                    ProdId = prodId,
                    ProdCode = prodId.ToString(),
                    ProdName = product.ProdName ?? "",
                    CategoryName = product.CategoryName ?? "",
                    ProductGroupName = product.ProductGroupName ?? "",
                    ProductBonusGroupName = product.ProductBonusGroupName ?? "",
                    CurrentQty = currentQty,
                    WeightedDiscount = weightedDiscount,
                    ManualDiscountPct = manualPctExp,
                    EffectiveDiscountPct = effectivePctExp,
                    ProfitDeltaExpected = profitDeltaExp,
                    SalesQty = salesQty,
                    PriceRetail = product.PriceRetail,
                    UnitCost = unitCost,
                    TotalCost = totalCost
                };
                if (includeBatches && batchLookupExp.TryGetValue(prodId, out var prodBatchesExp) && prodBatchesExp != null)
                {
                    var ledgerListExp = batchesByProdIdExp.TryGetValue(prodId, out var blistExp) ? blistExp : null;
                    var batchRowsExp = new List<ProductBalanceBatchRow>();
                    foreach (var m in prodBatchesExp.OrderBy(m => m.Expiry).ThenBy(m => m.BatchNo ?? ""))
                    {
                        var bExp = m.Expiry.Date;
                        var matchExp = ledgerListExp?.FirstOrDefault(b => (b.BatchNo ?? "").Trim() == (m.BatchNo ?? "").Trim() && (b.Expiry?.Date ?? DateTime.MinValue) == bExp);
                        decimal brQty = matchExp != null ? matchExp.TotalRemaining : 0m;
                        if (brQty <= 0) continue;
                        decimal brDisc = matchExp != null ? matchExp.WeightedDiscount / brQty : 0m;
                        decimal brCost = matchExp != null ? matchExp.WeightedCost / brQty : 0m;
                        var manualBatchExp = GetLatestOverrideExp(prodId, warehouseId, m.BatchId);
                        var effectiveBatchExp = manualBatchExp ?? brDisc;
                        batchRowsExp.Add(new ProductBalanceBatchRow { BatchNo = m.BatchNo, Expiry = m.Expiry, CurrentQty = (int)brQty, WeightedDiscount = brDisc, ManualDiscountPct = manualBatchExp, EffectiveDiscountPct = effectiveBatchExp, UnitCost = brCost, TotalCost = brQty * brCost });
                    }
                    if (batchRowsExp.Count >= 2)
                    {
                        dtoExp.Batches = batchRowsExp;
                        decimal batchesTotalCostExp = batchRowsExp.Sum(b => b.TotalCost);
                        int batchesTotalQtyExp = batchRowsExp.Sum(b => b.CurrentQty);
                        dtoExp.TotalCost = batchesTotalCostExp;
                        dtoExp.UnitCost = batchesTotalQtyExp > 0 ? batchesTotalCostExp / batchesTotalQtyExp : unitCost;
                    }
                }
                reportData.Add(dtoExp);
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
            worksheet.Cell(row, 4).Value = "مجموعة الصنف";
            worksheet.Cell(row, 5).Value = "مجموعة البونص";
            worksheet.Cell(row, 6).Value = "الكمية الحالية";
            worksheet.Cell(row, 7).Value = "الخصم المرجح %";
            worksheet.Cell(row, 8).Value = "الخصم اليدوي للبيع %";
            worksheet.Cell(row, 9).Value = "الخصم الفعّال %";
            worksheet.Cell(row, 10).Value = "المبيعات";
            worksheet.Cell(row, 11).Value = "سعر الجمهور";
            worksheet.Cell(row, 12).Value = "تكلفة العلبة";
            worksheet.Cell(row, 13).Value = "التكلفة الإجمالية";

            worksheet.Range(row, 1, row, 13).Style.Font.Bold = true;

            // البيانات (صف الصنف ثم صفوف التشغيلات إن وُجدت)
            row = 2;
            foreach (var item in reportData)
            {
                worksheet.Cell(row, 1).Value = item.ProdCode;
                worksheet.Cell(row, 2).Value = item.ProdName;
                worksheet.Cell(row, 3).Value = item.CategoryName;
                worksheet.Cell(row, 4).Value = item.ProductGroupName;
                worksheet.Cell(row, 5).Value = item.ProductBonusGroupName;
                worksheet.Cell(row, 6).Value = item.CurrentQty;
                worksheet.Cell(row, 7).Value = item.WeightedDiscount;
                if (item.ManualDiscountPct.HasValue)
                    worksheet.Cell(row, 8).Value = item.ManualDiscountPct.Value;
                else
                    worksheet.Cell(row, 8).Value = string.Empty;
                worksheet.Cell(row, 9).Value = item.EffectiveDiscountPct;
                worksheet.Cell(row, 10).Value = item.SalesQty;
                worksheet.Cell(row, 11).Value = item.PriceRetail;
                worksheet.Cell(row, 12).Value = item.UnitCost;
                worksheet.Cell(row, 13).Value = item.TotalCost;
                row++;
                if (item.Batches != null && item.Batches.Count >= 2)
                {
                    foreach (var batch in item.Batches)
                    {
                        worksheet.Cell(row, 1).Value = "";
                        worksheet.Cell(row, 2).Value = "  └ تشغيلة: " + (batch.BatchNo ?? "-") + (batch.Expiry.HasValue ? " | " + batch.Expiry.Value.ToString("yyyy-MM-dd") : "");
                        worksheet.Cell(row, 3).Value = "";
                        worksheet.Cell(row, 4).Value = "";
                        worksheet.Cell(row, 5).Value = "";
                        worksheet.Cell(row, 6).Value = batch.CurrentQty;
                        worksheet.Cell(row, 7).Value = batch.WeightedDiscount;
                        if (batch.ManualDiscountPct.HasValue)
                            worksheet.Cell(row, 8).Value = batch.ManualDiscountPct.Value;
                        else
                            worksheet.Cell(row, 8).Value = string.Empty;
                        worksheet.Cell(row, 9).Value = batch.EffectiveDiscountPct;
                        worksheet.Cell(row, 10).Value = "";
                        worksheet.Cell(row, 11).Value = "";
                        worksheet.Cell(row, 12).Value = batch.UnitCost;
                        worksheet.Cell(row, 13).Value = batch.TotalCost;
                        row++;
                    }
                }
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
        [RequirePermission("Reports.ProductProfits")]
        public async Task<IActionResult> ProductProfits(
            string? search,
            int? categoryId,
            int? warehouseId,
            DateTime? fromDate,
            DateTime? toDate,
            bool includeZeroQty = false,
            string? profitMethod = "both", // "sales" | "ledger" | "both"
            string? sortBy = "name",
            string? sortDir = "asc",
            string? filterCol_code = null,
            string? filterCol_name = null,
            string? filterCol_category = null,
            string? filterCol_salesrevenueExpr = null,
            string? filterCol_salescostExpr = null,
            string? filterCol_salesprofitExpr = null,
            string? filterCol_salesprofitpctExpr = null,
            string? filterCol_adjustmentprofitExpr = null,
            string? filterCol_transferprofitExpr = null,
            string? filterCol_salesqtyExpr = null,
            string? filterCol_avgunitpriceExpr = null,
            string? filterCol_avgunitcostExpr = null,
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
            ViewBag.IncludeZeroQty = includeZeroQty;
            ViewBag.ProfitMethod = profitMethod;
            ViewBag.SortBy = sortBy;
            ViewBag.SortDir = sortDir;
            ViewBag.FilterCol_Code = filterCol_code;
            ViewBag.FilterCol_Name = filterCol_name;
            ViewBag.FilterCol_Category = filterCol_category;
            ViewBag.FilterCol_SalesrevenueExpr = filterCol_salesrevenueExpr;
            ViewBag.FilterCol_SalescostExpr = filterCol_salescostExpr;
            ViewBag.FilterCol_SalesprofitExpr = filterCol_salesprofitExpr;
            ViewBag.FilterCol_SalesprofitpctExpr = filterCol_salesprofitpctExpr;
            ViewBag.FilterCol_AdjustmentprofitExpr = filterCol_adjustmentprofitExpr;
            ViewBag.FilterCol_TransferprofitExpr = filterCol_transferprofitExpr;
            ViewBag.FilterCol_SalesqtyExpr = filterCol_salesqtyExpr;
            ViewBag.FilterCol_AvgunitpriceExpr = filterCol_avgunitpriceExpr;
            ViewBag.FilterCol_AvgunitcostExpr = filterCol_avgunitcostExpr;

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

            // الإيرادات والكمية من SalesInvoiceLines
            var salesProfitData = await salesProfitQuery
                .GroupBy(sil => sil.ProdId)
                .Select(g => new
                {
                    ProdId = g.Key,
                    SalesRevenue = g.Sum(sil => sil.LineNetTotal),
                    SalesQty = g.Sum(sil => (decimal?)sil.Qty) ?? 0m
                })
                .ToDictionaryAsync(x => x.ProdId);

            // التكلفة من StockLedger (مصدر الحقيقة - تكلفة FIFO من فواتير الشراء)
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
            // 5.2) أرباح التسويات (من StockAdjustmentLines المترحلة)
            // CostDiff موجب = فائض جرد (ربح)، CostDiff سالب = عجز جرد (خسارة)
            // =========================================================
            var adjustmentProfitQuery = from sal in _context.StockAdjustmentLines.AsNoTracking()
                                       join sa in _context.StockAdjustments.AsNoTracking() on sal.StockAdjustmentId equals sa.Id
                                       where sa.IsPosted &&
                                             productIds.Contains(sal.ProductId) &&
                                             sal.CostDiff.HasValue && sal.CostDiff.Value != 0
                                       select new { sal.ProductId, sal.CostDiff, sa.WarehouseId, sa.AdjustmentDate };

            if (warehouseId.HasValue && warehouseId.Value > 0)
            {
                adjustmentProfitQuery = adjustmentProfitQuery.Where(x => x.WarehouseId == warehouseId.Value);
            }

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

            // =========================================================
            // 5.3) أرباح التحويلات (من StockTransferLines المترحلة — خصم أقل من المرجح)
            // ربح السطر = (سعر التحويل - التكلفة) × الكمية، حيث سعر التحويل = PriceRetail × (1 - DiscountPct/100)
            // =========================================================
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
            {
                transferProfitQuery = transferProfitQuery.Where(x => x.FromWarehouseId == warehouseId.Value);
            }

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

            // =========================================================
            // 6) حساب ربح الميزانية (طريقة جديدة):
            // الربح = مجموع العملاء المدينين + رصيد الخزنة + تكلفة البضاعة في المخزن - مجموع العملاء الدائنين
            // =========================================================
            decimal customersDebitSum = 0m;   // مجموع العملاء المدينين (رصيد مدين)
            decimal customersCreditSum = 0m;   // مجموع العملاء الدائنين (رصيد دائن)
            decimal treasuryBalance = 0m;     // رصيد الخزنة
            decimal inventoryCostTotal = 0m;  // تكلفة البضاعة في المخزن

            // 6.1) مجموع العملاء المدينين والدائنين من LedgerEntries (مصدر الحقيقة - يتوافق مع حذف القيود)
            var activeCustomerIds = await _context.Customers
                .AsNoTracking()
                .Where(c => c.IsActive == true)
                .Select(c => c.CustomerId)
                .ToListAsync();

            var customerLedgerQuery = _context.LedgerEntries
                .AsNoTracking()
                .Where(e => e.CustomerId.HasValue && activeCustomerIds.Contains(e.CustomerId.Value));
            if (toDate.HasValue)
                customerLedgerQuery = customerLedgerQuery.Where(e => e.EntryDate < toDate.Value.Date.AddDays(1));
            var customerBalancesFromLedger = await customerLedgerQuery
                .GroupBy(e => e.CustomerId!.Value)
                .Select(g => new { CustomerId = g.Key, Balance = g.Sum(e => (decimal?)(e.Debit - e.Credit)) ?? 0m })
                .ToListAsync();

            foreach (var c in customerBalancesFromLedger)
            {
                if (c.Balance > 0)
                    customersDebitSum += c.Balance;
                else if (c.Balance < 0)
                    customersCreditSum += Math.Abs(c.Balance);
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
                // رصيد الخزنة = الرصيد التراكمي حتى نهاية الفترة (مثل العملاء والمخزون)
                // وليس التغير في الفترة - ليتوافق ربح الميزانية مع صافي الأصول
                var treasuryQuery = _context.LedgerEntries
                    .AsNoTracking()
                    .Where(e => cashAccountIds.Contains(e.AccountId) && e.PostVersion > 0);

                if (toDate.HasValue)
                    treasuryQuery = treasuryQuery.Where(e => e.EntryDate < toDate.Value.Date.AddDays(1));
                // لا نستخدم fromDate - نأخذ كل القيود حتى toDate للحصول على الرصيد التراكمي

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
                // التكلفة من StockLedger (FIFO) لتطابق فواتير الشراء
                decimal salesRevenue = salesProfitData.TryGetValue(prodId, out var salesData) ? salesData.SalesRevenue : 0m;
                decimal salesCost = salesCostFromLedger.TryGetValue(prodId, out var costVal) ? costVal : 0m;
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

                // ربح التسويات (فائض جرد - عجز جرد)
                decimal adjustmentProfit = adjustmentProfitData.TryGetValue(prodId, out var adjProfit) ? adjProfit : 0m;

                // ربح التحويلات (خصم أقل من المرجح)
                decimal transferProfit = transferProfitData.TryGetValue(prodId, out var trfProfit) ? trfProfit : 0m;

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

                // عرض الصفر: عند عدم التفعيل نستبعد الأصناف التي ليس لها مبيعات ولا تسويات ولا تحويلات
                if (!includeZeroQty && salesRevenue == 0m && adjustmentProfit == 0m && transferProfit == 0m)
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
                    SalesQty = salesQty,
                    AvgUnitPrice = avgUnitPrice,
                    AvgUnitCost = avgUnitCost
                });
            }

            // =========================================================
            // 7.5) فلترة أعمدة (بنمط Excel)
            // =========================================================
            if (!string.IsNullOrWhiteSpace(filterCol_code))
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
            if (!string.IsNullOrWhiteSpace(filterCol_name))
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
            if (!string.IsNullOrWhiteSpace(filterCol_category))
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

            // =========================================================
            // 7.6) فلاتر رقمية متقدمة (أكبر من، أصغر من، يساوي، من:إلى)
            // =========================================================
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            bool ApplyDecimalExpr(string? expr, Func<ProductProfitReportDto, decimal> selector)
            {
                if (string.IsNullOrWhiteSpace(expr)) return false;
                var e = expr.Trim();
                if (e.StartsWith("<=") && e.Length > 2 && decimal.TryParse(e.Substring(2), System.Globalization.NumberStyles.Any, inv, out var max))
                {
                    reportData = reportData.Where(r => selector(r) <= max).ToList();
                    return true;
                }
                if (e.StartsWith(">=") && e.Length > 2 && decimal.TryParse(e.Substring(2), System.Globalization.NumberStyles.Any, inv, out var min))
                {
                    reportData = reportData.Where(r => selector(r) >= min).ToList();
                    return true;
                }
                if (e.StartsWith("<") && !e.StartsWith("<=") && e.Length > 1 && decimal.TryParse(e.Substring(1), System.Globalization.NumberStyles.Any, inv, out var max2))
                {
                    reportData = reportData.Where(r => selector(r) < max2).ToList();
                    return true;
                }
                if (e.StartsWith(">") && !e.StartsWith(">=") && e.Length > 1 && decimal.TryParse(e.Substring(1), System.Globalization.NumberStyles.Any, inv, out var min2))
                {
                    reportData = reportData.Where(r => selector(r) > min2).ToList();
                    return true;
                }
                if ((e.Contains(':') || e.Contains('-')) && !e.StartsWith("-"))
                {
                    var sep = e.Contains(':') ? ':' : '-';
                    var parts = e.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2 &&
                        decimal.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Any, inv, out var from) &&
                        decimal.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Any, inv, out var to))
                    {
                        if (from > to) (from, to) = (to, from);
                        reportData = reportData.Where(r => selector(r) >= from && selector(r) <= to).ToList();
                        return true;
                    }
                }
                if (decimal.TryParse(e, System.Globalization.NumberStyles.Any, inv, out var exact))
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
            ApplyDecimalExpr(filterCol_adjustmentprofitExpr, r => r.AdjustmentProfit);
            ApplyDecimalExpr(filterCol_transferprofitExpr, r => r.TransferProfit);
            ApplyDecimalExpr(filterCol_salesqtyExpr, r => r.SalesQty);
            ApplyDecimalExpr(filterCol_avgunitpriceExpr, r => r.AvgUnitPrice);
            ApplyDecimalExpr(filterCol_avgunitcostExpr, r => r.AvgUnitCost);

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
                case "avgunitprice":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.AvgUnitPrice).ToList()
                        : reportData.OrderBy(r => r.AvgUnitPrice).ToList();
                    break;
                case "avgunitcost":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.AvgUnitCost).ToList()
                        : reportData.OrderBy(r => r.AvgUnitCost).ToList();
                    break;
                case "category":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.CategoryName).ToList()
                        : reportData.OrderBy(r => r.CategoryName).ToList();
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
            decimal totalAdjustmentProfit = reportData.Sum(r => r.AdjustmentProfit);
            decimal totalTransferProfit = reportData.Sum(r => r.TransferProfit);
            decimal totalProfit = totalSalesProfit + totalAdjustmentProfit + totalTransferProfit;

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
            ViewBag.TotalAdjustmentProfit = totalAdjustmentProfit;
            ViewBag.TotalTransferProfit = totalTransferProfit;
            ViewBag.TotalProfit = totalProfit;

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
        [RequirePermission("Reports.CustomerProfits")]
        public async Task<IActionResult> CustomerProfits(
            string? search,
            string? partyCategory,
            int? governorateId,
            DateTime? fromDate,
            DateTime? toDate,
            bool includeZeroQty = false,
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
            ViewBag.IncludeZeroQty = includeZeroQty;
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

                // عرض الصفر: عند عدم التفعيل نستبعد العملاء الذين ليس لهم مبيعات في الفترة
                if (!includeZeroQty && salesRevenue == 0m)
                    continue;

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

            // تقرير أرباح العملاء: الجدول فقط (أرباح من مبيعات كل عميل)، بدون قسم الميزانية تحته
            ViewBag.BalanceSheetData = null;

            return View();
        }
    }
}
