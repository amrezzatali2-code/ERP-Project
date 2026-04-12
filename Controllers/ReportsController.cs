using ERP.Data;
using ERP.Filters;
using ERP.Models;
using ERP.Security;
using ERP.Services;
using ERP.ViewModels;
using ERP.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ClosedXML.Excel;

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر التقارير Reports Controller
    /// يحتوي على جميع التقارير المالية والمخزنية
    /// </summary>
    public partial class ReportsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly StockAnalysisService _stockAnalysisService;
        private readonly ILedgerPostingService _ledgerPostingService;
        private readonly IUserActivityLogger _activityLogger;
        private readonly IUserAccountVisibilityService _accountVisibilityService;

        public ReportsController(AppDbContext context, StockAnalysisService stockAnalysisService, ILedgerPostingService ledgerPostingService, IUserActivityLogger activityLogger, IUserAccountVisibilityService accountVisibilityService)
        {
            _context = context;
            _stockAnalysisService = stockAnalysisService;
            _ledgerPostingService = ledgerPostingService;
            _activityLogger = activityLogger;
            _accountVisibilityService = accountVisibilityService;
        }

        /// <summary>تكلفة وحدة دخلة (أول مدة/شراء) عندما UnitCost = 0 لكن إجمالي التكلفة أو الكمية متوفرين — لتوافق البيانات المنقولة من أنظمة أخرى.</summary>
        private static decimal EffectiveStockInflowUnitCost(decimal unitCost, int qtyIn, decimal? totalCost)
        {
            if (qtyIn <= 0) return unitCost;
            if (unitCost != 0m) return unitCost;
            if (totalCost.HasValue && totalCost.Value != 0m)
                return totalCost.Value / qtyIn;
            return 0m;
        }

        /// <summary>عملاء نشطون + فلترة ظهور الحسابات — نفس بذرة أرصدة العملاء وربح الميزانية.</summary>
        private async Task<HashSet<int>> GetEligibleActiveCustomerIdsForBalanceSheetAsync()
        {
            var q = _context.Customers.AsNoTracking().AsQueryable();
            q = await _accountVisibilityService.ApplyCustomerVisibilityFilterAsync(q);
            q = q.Where(c => c.IsActive == true);
            var list = await q.Select(c => c.CustomerId).ToListAsync();
            return list.Count > 0 ? list.ToHashSet() : new HashSet<int>();
        }

        /// <summary>
        /// مصدر واحد لمجموع المدين/الدائن للعملاء (نفس القيود + تاريخ اختياري) لربح الميزانية وإجماليات أرصدة العملاء.
        /// </summary>
        private async Task<(decimal DebitSum, decimal CreditSum)> ComputeBalanceSheetCustomerDebitCreditFromCustomerIdsAsync(
            HashSet<int> customerIds,
            DateTime? asOfDate)
        {
            if (customerIds == null || customerIds.Count == 0)
                return (0m, 0m);

            var q = _context.LedgerEntries
                .AsNoTracking()
                .Include(e => e.Account)
                .Include(e => e.Customer)
                .Where(e => e.CustomerId.HasValue && customerIds.Contains(e.CustomerId.Value));
            if (asOfDate.HasValue)
                q = q.Where(e => e.EntryDate < asOfDate.Value.Date.AddDays(1));
            q = await _accountVisibilityService.ApplyLedgerEntryListVisibilityFilterAsync(q);

            var rows = await q
                .GroupBy(e => e.CustomerId!.Value)
                .Select(g => new { Balance = g.Sum(e => (decimal?)(e.Debit - e.Credit)) ?? 0m })
                .ToListAsync();

            decimal debit = 0m, credit = 0m;
            foreach (var r in rows)
            {
                if (r.Balance > 0) debit += r.Balance;
                else if (r.Balance < 0) credit += Math.Abs(r.Balance);
            }
            return (debit, credit);
        }

        /// <summary>فلتر بحث أصناف تقرير أرصدة الأصناف (يبدأ بـ / يحتوي / ينتهي بـ).</summary>
        private static IQueryable<Product> ApplyProductBalancesSearchFilter(
            IQueryable<Product> productsQuery,
            string? search,
            string? searchMode)
        {
            if (string.IsNullOrWhiteSpace(search)) return productsQuery;
            var s = search.Trim();
            var mode = (searchMode ?? "contains").Trim().ToLowerInvariant();
            return mode switch
            {
                "starts" => productsQuery.Where(p =>
                    (p.ProdName != null && p.ProdName.StartsWith(s)) ||
                    (p.Barcode != null && p.Barcode.StartsWith(s)) ||
                    (p.ProdId.ToString() == s)),
                "ends" => productsQuery.Where(p =>
                    (p.ProdName != null && p.ProdName.EndsWith(s)) ||
                    (p.Barcode != null && p.Barcode.EndsWith(s)) ||
                    (p.ProdId.ToString() == s)),
                _ => productsQuery.Where(p =>
                    (p.ProdName != null && p.ProdName.Contains(s)) ||
                    (p.Barcode != null && p.Barcode.Contains(s)) ||
                    (p.ProdId.ToString() == s)),
            };
        }

        /// <summary>فلتر بحث العملاء في تقرير كشف الحساب (يبدأ بـ / يحتوي / ينتهي بـ).</summary>
        private static IQueryable<Customer> ApplyCustomerBalancesSearchFilter(
            IQueryable<Customer> customersQuery,
            string? search,
            string? searchMode)
        {
            if (string.IsNullOrWhiteSpace(search)) return customersQuery;
            var s = search.Trim();
            var mode = (searchMode ?? "contains").Trim().ToLowerInvariant();
            return mode switch
            {
                "starts" => customersQuery.Where(c =>
                    (c.CustomerName != null && c.CustomerName.StartsWith(s)) ||
                    (c.Phone1 != null && c.Phone1.StartsWith(s)) ||
                    (c.CustomerId.ToString() == s)),
                "ends" => customersQuery.Where(c =>
                    (c.CustomerName != null && c.CustomerName.EndsWith(s)) ||
                    (c.Phone1 != null && c.Phone1.EndsWith(s)) ||
                    (c.CustomerId.ToString() == s)),
                _ => customersQuery.Where(c =>
                    (c.CustomerName != null && c.CustomerName.Contains(s)) ||
                    (c.Phone1 != null && c.Phone1.Contains(s)) ||
                    (c.CustomerId.ToString() == s)),
            };
        }

        /// <summary>
        /// يضيّق معرّفات الأصناف إلى من لهم حركة شراء (StockLedger SourceType=Purchase) تطابق التاريخ و/أو نطاق رقم فاتورة الشراء (SourceId = PIId).
        /// عند الجمع بين التاريخ والنطاق يُشترط تحقّق الشروط على نفس سطر دفتر الحركة.
        /// </summary>
        private async Task<List<int>> ApplyProductBalancesPurchaseMovementFilterAsync(
            List<int> productIds,
            DateTime? fromDate,
            DateTime? toDate,
            int? purchaseInvoiceFrom,
            int? purchaseInvoiceTo)
        {
            if (productIds.Count == 0)
                return productIds;

            DateTime? fromDateUtc = null;
            DateTime? toDateUtc = null;
            if (fromDate.HasValue)
            {
                var fromLocal = fromDate.Value.Kind == DateTimeKind.Utc ? fromDate.Value.ToLocalTime() : DateTime.SpecifyKind(fromDate.Value, DateTimeKind.Local);
                fromDateUtc = TimeZoneInfo.ConvertTimeToUtc(fromLocal, TimeZoneInfo.Local);
            }
            if (toDate.HasValue)
            {
                var toLocal = toDate.Value.Kind == DateTimeKind.Utc ? toDate.Value.ToLocalTime() : DateTime.SpecifyKind(toDate.Value, DateTimeKind.Local);
                if (toLocal.TimeOfDay.TotalSeconds < 1)
                    toLocal = toLocal.Date.AddDays(1).AddSeconds(-1);
                toDateUtc = TimeZoneInfo.ConvertTimeToUtc(toLocal, TimeZoneInfo.Local);
            }

            var needFilter = fromDateUtc.HasValue || toDateUtc.HasValue
                || purchaseInvoiceFrom.HasValue || purchaseInvoiceTo.HasValue;
            if (!needFilter)
                return productIds;

            var purchaseQuery = _context.StockLedger
                .AsNoTracking()
                .Where(sl => sl.SourceType == "Purchase" && productIds.Contains(sl.ProdId));
            if (fromDateUtc.HasValue)
                purchaseQuery = purchaseQuery.Where(sl => sl.TranDate >= fromDateUtc.Value);
            if (toDateUtc.HasValue)
                purchaseQuery = purchaseQuery.Where(sl => sl.TranDate <= toDateUtc.Value);
            if (purchaseInvoiceFrom.HasValue && purchaseInvoiceTo.HasValue)
            {
                var lo = Math.Min(purchaseInvoiceFrom.Value, purchaseInvoiceTo.Value);
                var hi = Math.Max(purchaseInvoiceFrom.Value, purchaseInvoiceTo.Value);
                purchaseQuery = purchaseQuery.Where(sl => sl.SourceId >= lo && sl.SourceId <= hi);
            }
            else if (purchaseInvoiceFrom.HasValue)
                purchaseQuery = purchaseQuery.Where(sl => sl.SourceId >= purchaseInvoiceFrom.Value);

            var purchasedIds = await purchaseQuery.Select(sl => sl.ProdId).Distinct().ToListAsync();
            return productIds.Intersect(purchasedIds).ToList();
        }

        /// <summary>تكلفة المخزون من StockLedger — مصدر واحد لربح الميزانية وكارت إجمالي التكلفة في أرصدة الأصناف.</summary>
        private async Task<decimal> ComputeBalanceSheetInventoryCostAsync(int? warehouseId)
        {
            var invBaseQuery = _context.StockLedger.AsNoTracking()
                .Where(sl => sl.QtyIn > 0 || (sl.RemainingQty ?? 0) > 0);
            if (warehouseId.HasValue && warehouseId.Value > 0)
                invBaseQuery = invBaseQuery.Where(sl => sl.WarehouseId == warehouseId.Value);
            decimal invFromTotalCost = await invBaseQuery
                .Where(sl => sl.TotalCost != null && sl.TotalCost.Value != 0)
                .SumAsync(sl => sl.TotalCost!.Value);
            decimal invFromQtyUnit = await invBaseQuery
                .Where(sl => sl.TotalCost == null || sl.TotalCost.Value == 0)
                .SumAsync(sl => (decimal)(sl.RemainingQty ?? (sl.QtyIn > 0 && sl.QtyOut == 0 ? sl.QtyIn : 0)) * sl.UnitCost);
            return invFromTotalCost + invFromQtyUnit;
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
        /// تصفير RemainingQty لمرتجعات البيع المرتبطة ببيع وهمي (البيع الأصلي لم يكن له FIFO).
        /// بعد التشغيل لن يظهر الصنف كمتاح في فاتورة المبيعات.
        /// </summary>
        [HttpPost]
        [RequirePermission("Reports.ZeroPhantomSalesReturnRemaining")]
        public async Task<IActionResult> ZeroPhantomSalesReturnRemaining()
        {
            var srLedgers = await _context.StockLedger
                .Where(sl => sl.SourceType == "SalesReturn" && sl.QtyIn > 0 && (sl.RemainingQty ?? 0) > 0)
                .ToListAsync();
            int fixedCount = 0;
            foreach (var sl in srLedgers)
            {
                var retLine = await _context.SalesReturnLines
                    .AsNoTracking()
                    .FirstOrDefaultAsync(l => l.SRId == sl.SourceId && l.LineNo == sl.SourceLine);
                if (retLine?.SalesInvoiceId == null || retLine.SalesInvoiceLineNo == null)
                    continue;
                int siId = retLine.SalesInvoiceId.Value;
                int lineNo = retLine.SalesInvoiceLineNo.Value;
                var saleOutEntryIds = await _context.StockLedger
                    .Where(x => x.SourceType == "Sales" && x.SourceId == siId && x.SourceLine == lineNo && x.QtyOut > 0)
                    .Select(x => x.EntryId)
                    .ToListAsync();
                if (saleOutEntryIds.Count == 0)
                    continue;
                bool saleHadFifo = await _context.Set<StockFifoMap>().AnyAsync(f => saleOutEntryIds.Contains(f.OutEntryId));
                if (!saleHadFifo)
                {
                    sl.RemainingQty = 0;
                    fixedCount++;
                }
            }
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = fixedCount > 0
                ? $"تم تصفير RemainingQty لـ {fixedCount} قيد مرتجع بيع (كانت مرتبطة ببيع وهمي). الصنف لن يظهر كمتاح في الفاتورة."
                : "لا توجد قيود مرتجع بيع وهمية تحتاج تصفير.";
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

        /// <summary>أسماء السياسات 1–10 من جدول Policies (للقائمة المنسدلة ورؤوس أعمدة التقرير) — بما فيها غير المفعّلة إن وُجدت.</summary>
        private async Task<Dictionary<int, string>> GetProductBalancePolicyNamesFromDbAsync()
        {
            var rows = await _context.Policies
                .AsNoTracking()
                .Where(p => p.PolicyId >= 1 && p.PolicyId <= 10)
                .Select(p => new { p.PolicyId, p.Name })
                .ToListAsync();
            var byId = rows.ToDictionary(r => r.PolicyId, r => r.Name);
            var result = new Dictionary<int, string>();
            for (var id = 1; id <= 10; id++)
            {
                if (byId.TryGetValue(id, out var nm) && !string.IsNullOrWhiteSpace(nm))
                    result[id] = nm.Trim();
                else
                    result[id] = "سياسة " + id;
            }
            return result;
        }

        // =========================================================
        // تقرير: أرصدة الأصناف
        // يعرض الصنف، الكمية الحالية، الخصم المرجح، المبيعات (عند تحديد تاريخ: في المدى)،
        // سعر الجمهور، تكلفة العلبة، والتكلفة الإجمالية.
        // فلتر قائمة الأصناف بالتاريخ/بفاتورة الشراء: حركات StockLedger نوع Purchase (نفس سطر يلتقي التاريخ + رقم PI).
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
            int? purchaseInvoiceFrom,
            int? purchaseInvoiceTo,
            bool includeZeroQty = false,
            bool includeBatches = true,
            string? sortBy = "name",
            string? sortDir = "asc",
            bool loadReport = false,
            int page = 1,
            int pageSize = 10,
            string? searchMode = "contains")
        {
            ViewBag.LoadReport = loadReport;

            // =========================================================
            // 1) تجهيز القوائم المنسدلة (المخزن فقط — الفئة/مجموعة الصنف أُزيلتا من الواجهة)
            // =========================================================
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
            ViewBag.HasBonus = hasBonus;
            ViewBag.WarehouseId = warehouseId;
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;
            ViewBag.PurchaseInvoiceFrom = purchaseInvoiceFrom;
            ViewBag.PurchaseInvoiceTo = purchaseInvoiceTo;
            ViewBag.IncludeZeroQty = includeZeroQty;
            ViewBag.IncludeBatches = includeBatches;
            ViewBag.SortBy = sortBy;
            ViewBag.SortDir = sortDir;
            ViewBag.SearchMode = string.IsNullOrWhiteSpace(searchMode) ? "contains" : searchMode!.Trim();
            ViewBag.PolicyNamesById = await GetProductBalancePolicyNamesFromDbAsync();

            // =========================================================
            // 3) تحميل البيانات فقط عند الضغط على "تجميع التقرير"
            // =========================================================
            if (!loadReport)
            {
                ViewBag.ShowPolicyColumns = false;
                // الصفحة تفتح بدون بيانات - فقط الفلاتر (عرض التشغيلات مفعّل افتراضياً)
                ViewBag.IncludeZeroQty = false;
                ViewBag.IncludeBatches = true;
                ViewBag.ReportData = new List<ProductBalanceReportDto>();
                ViewBag.TotalCost = 0m;
                ViewBag.TotalQty = 0;
                ViewBag.TotalPriceRetail = 0m;
                ViewBag.TotalSalesQty = 0m;
                ViewBag.TotalUnitCost = 0m;
                ViewBag.WeightedAvgDiscount = 0m;
                ViewBag.AveragePriceRetail = 0m;
                ViewBag.AverageUnitCost = 0m;
                ViewBag.Page = 1;
                ViewBag.PageSize = 10;
                ViewBag.TotalPages = 1;
                ViewBag.TotalCount = 0;
                return View();
            }

            // حجم الصفحة (نمط القوائم): آخر قيمة في الـ query، وقيم مسموحة 10/25/50/100/200 أو 0 = الكل
            var pageSizeQuery = Request.Query["pageSize"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageSizeQuery) && int.TryParse(pageSizeQuery, out var psVal))
                pageSize = psVal;
            if (pageSize < 0) pageSize = 10;
            var allowedPageSizes = new[] { 0, 10, 25, 50, 100, 200 };
            if (pageSize > 0 && !allowedPageSizes.Contains(pageSize))
                pageSize = 10;

            var pageQuery = Request.Query["page"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageQuery) && int.TryParse(pageQuery, out var pageParsed))
                page = pageParsed;

            // «الكمية أكبر من صفر» (pbQtyGtZero): عند التفعيل يُستبعد رصيد صفر — يعادل includeZeroQty=false
            // توافق قديم: includeZeroQty=true في الـ query يعني عرض أصناف برصيد صفر
            var pbQtyGtZero = Request.Query["pbQtyGtZero"].FirstOrDefault();
            if (pbQtyGtZero != null)
                includeZeroQty = !string.Equals(pbQtyGtZero, "true", StringComparison.OrdinalIgnoreCase);
            else
            {
                var legacyInc = Request.Query["includeZeroQty"].FirstOrDefault();
                if (!string.IsNullOrEmpty(legacyInc))
                    includeZeroQty = string.Equals(legacyInc, "true", StringComparison.OrdinalIgnoreCase);
                else
                    includeZeroQty = false;
            }
            ViewBag.IncludeZeroQty = includeZeroQty;

            // عند عدم تحديد تاريخ ولا نطاق فواتير شراء: لا نطبّق فلتر حركات الشراء على قائمة الأصناف
            // عند التحديد: نقتصر على أصناف لها حركة شراء تطابق الشروط (TranDate و/أو SourceId=PIId على نفس السطر)

            // =========================================================
            // 4) بناء الاستعلام الأساسي للأصناف (عند طلب التقرير)
            // =========================================================
            var productsQuery = _context.Products
                .AsNoTracking()
                .Include(p => p.Category)
                .Include(p => p.ProductGroup)
                .Include(p => p.ProductBonusGroup)
                .AsQueryable();

            // فلتر البحث (اسم الصنف أو الكود) — يبدأ بـ / يحتوي / ينتهي بـ
            productsQuery = ApplyProductBalancesSearchFilter(productsQuery, search, searchMode);

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

            // 5.1) فلتر حركات الشراء: تاريخ و/أو نطاق رقم فاتورة مشتريات (PIId في SourceId)
            productIds = await ApplyProductBalancesPurchaseMovementFilterAsync(
                productIds, fromDate, toDate, purchaseInvoiceFrom, purchaseInvoiceTo);

            if (productIds.Count == 0)
            {
                ViewBag.LoadReport = true;
                ViewBag.ShowPolicyColumns = warehouseId.HasValue && warehouseId.Value > 0;
                ViewBag.ReportData = new List<ProductBalanceReportDto>();
                ViewBag.TotalCost = 0m;
                ViewBag.TotalQty = 0;
                ViewBag.TotalPriceRetail = 0m;
                ViewBag.TotalSalesQty = 0m;
                ViewBag.TotalUnitCost = 0m;
                ViewBag.WeightedAvgDiscount = 0m;
                ViewBag.AveragePriceRetail = 0m;
                ViewBag.AverageUnitCost = 0m;
                ViewBag.Page = 1;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalPages = 1;
                ViewBag.TotalCount = 0;
                return View();
            }

            // عند اختيار مخزن: نقتصر على أصناف لها رصيد فعلي > 0 في هذا المخزن، أو (عند عرض الصفر) أصناف معيّن لها هذا المخزن فقط — حتى لا يظهر صنف نُقل بالكامل لمخزن آخر
            if (warehouseId.HasValue && warehouseId.Value > 0)
            {
                var whId = warehouseId.Value;
                var balanceInWarehouse = await _context.StockLedger
                    .AsNoTracking()
                    .Where(sl => sl.WarehouseId == whId && productIds.Contains(sl.ProdId))
                    .GroupBy(sl => sl.ProdId)
                    .Select(g => new { ProdId = g.Key, Balance = g.Sum(sl => sl.QtyIn - sl.QtyOut) })
                    .ToListAsync();
                var withPositiveBalance = balanceInWarehouse.Where(b => b.Balance > 0).Select(b => b.ProdId).ToList();
                var inWarehouseIds = new List<int>(withPositiveBalance);
                if (includeZeroQty)
                {
                    var assignedToWarehouseIds = await _context.Products
                        .AsNoTracking()
                        .Where(p => p.WarehouseId == whId && p.IsActive)
                        .Select(p => p.ProdId)
                        .ToListAsync();
                    inWarehouseIds = inWarehouseIds.Union(assignedToWarehouseIds).Distinct().ToList();
                }
                productIds = productIds.Intersect(inWarehouseIds).ToList();
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
                    p.PriceRetail,
                    p.Company,
                    p.Imported,
                    p.Description
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

            // 6.3) تحميل StockLedger للخصم المرجح والتكلفة (شراء، رصيد أول، تحويل دخول، تحويل إلى مخزن الصنف — مع RemainingQty > 0)
            var stockLedgerCostQuery = _context.StockLedger
                .AsNoTracking()
                .Where(x =>
                    productIds.Contains(x.ProdId) &&
                    (x.SourceType == "Purchase" || x.SourceType == "Opening" || x.SourceType == "TransferIn" || x.SourceType == "SyncToProductWarehouse") &&
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
                    (x.SourceType == "Purchase" || x.SourceType == "Opening" || x.SourceType == "TransferIn" || x.SourceType == "SyncToProductWarehouse") &&
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
                    Company = product.Company,
                    Imported = product.Imported,
                    Description = product.Description,
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
                        var batchKey = (ProdId: prodId, BatchNo: (m.BatchNo ?? "").Trim(), ExpiryDate: (DateTime?)m.Expiry.Date);
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

            var whForPolicyRules = warehouseId is int whPb && whPb > 0 ? whPb : 0;
            var ctxPol = await ProductBalancePolicySaleHelper.LoadWarehousePolicyContextAsync(_context, whForPolicyRules);
            ProductBalancePolicySaleHelper.ApplyToReport(reportData, ctxPol.Rules, ctxPol.DefaultProfit);

            ViewBag.ShowPolicyColumns = true;

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
            // إجمالي التكلفة: مصدر واحد مع «ربح الميزانية» (StockLedger) وليس مجموع صفوف التقرير.
            // =========================================================
            int totalQty = reportData.Sum(r => r.CurrentQty);
            decimal totalPriceRetail = reportData.Sum(r => r.PriceRetail);
            decimal totalSalesQty = reportData.Sum(r => r.SalesQty);
            decimal totalUnitCost = reportData.Sum(r => r.UnitCost);
            decimal totalCostSum = await ComputeBalanceSheetInventoryCostAsync(warehouseId);

            int totalCount = reportData.Count; // إجمالي عدد الأصناف (قبل Pagination)
            // متوسط الخصم المرجح (موزون بالكمية)، متوسط سعر الجمهور، متوسط تكلفة العلبة — للكروت
            decimal weightedAvgDiscount = totalQty > 0
                ? reportData.Sum(r => r.WeightedDiscount * r.CurrentQty) / totalQty
                : 0m;
            decimal averagePriceRetail = totalCount > 0 ? totalPriceRetail / totalCount : 0m;
            decimal averageUnitCost = totalCount > 0 ? totalUnitCost / totalCount : 0m;

            // =========================================================
            // 9) Pagination (نمط القوائم: 10، 25، 50، 100، 200، أو الكل)
            // =========================================================
            ViewBag.TotalCount = totalCount;
            if (totalCount == 0)
            {
                ViewBag.Page = 1;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalPages = 1;
            }
            else if (pageSize == 0)
            {
                int effectiveAll = Math.Min(totalCount, 100_000);
                reportData = reportData.Take(effectiveAll).ToList();
                ViewBag.Page = 1;
                ViewBag.PageSize = 0;
                ViewBag.TotalPages = 1;
            }
            else if (pageSize < totalCount)
            {
                if (page < 1) page = 1;
                int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
                if (page > totalPages) page = totalPages;
                int skip = (page - 1) * pageSize;
                reportData = reportData.Skip(skip).Take(pageSize).ToList();
                ViewBag.Page = page;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalPages = totalPages;
            }
            else
            {
                ViewBag.Page = 1;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalPages = 1;
            }

            ViewBag.ReportData = reportData;
            ViewBag.TotalQty = totalQty;
            ViewBag.TotalPriceRetail = totalPriceRetail;
            ViewBag.TotalSalesQty = totalSalesQty;
            ViewBag.TotalUnitCost = totalUnitCost;
            ViewBag.TotalCost = totalCostSum;
            ViewBag.WeightedAvgDiscount = weightedAvgDiscount;
            ViewBag.AveragePriceRetail = averagePriceRetail;
            ViewBag.AverageUnitCost = averageUnitCost;

            return View();
        }

        private static readonly char[] _productDetailsFilterSep = new[] { '|', ',', ';' };

        private static List<string> ParseProductDetailsFilterStrings(string? filterCol)
        {
            if (string.IsNullOrWhiteSpace(filterCol)) return new List<string>();
            return filterCol.Split(_productDetailsFilterSep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
        }

        private static List<DateTime> ParseProductDetailsFilterDates(string? filterCol)
        {
            var list = new List<DateTime>();
            if (string.IsNullOrWhiteSpace(filterCol)) return list;
            foreach (var part in filterCol.Split(_productDetailsFilterSep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length >= 8))
                if (DateTime.TryParse(part, out var d)) list.Add(d.Date);
            return list;
        }

        /// <summary>
        /// تقرير أصناف مفصّلة: مبيعات، مشتريات، مرتجعات بيع/شراء، تسويات، تحويلات، طلبات شراء، أوامر بيع.
        /// </summary>
        [HttpGet]
        [RequirePermission("Reports.ProductDetailsReport")]
        public async Task<IActionResult> ProductDetailsReport(
            string? reportType,
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
            int page = 1,
            int pageSize = 10)
        {
            var pageSizeQuery = Request.Query["pageSize"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageSizeQuery) && int.TryParse(pageSizeQuery, out var psVal))
                pageSize = psVal;
            if (pageSize < 0)
                pageSize = 10;
            if (pageSize > 0 && pageSize != 10 && pageSize != 25 && pageSize != 50 && pageSize != 100 && pageSize != 200)
                pageSize = 10;

            reportType = string.IsNullOrWhiteSpace(reportType) ? null : reportType.Trim();

            var reportTypes = new List<SelectListItem>
            {
                new SelectListItem { Value = "", Text = "— اختر نوع التقرير —", Selected = string.IsNullOrEmpty(reportType) },
                new SelectListItem { Value = "Sales", Text = "مبيعات الصنف بالتفصيل", Selected = reportType == "Sales" },
                new SelectListItem { Value = "Purchases", Text = "مشتريات الصنف", Selected = reportType == "Purchases" },
                new SelectListItem { Value = "SalesReturns", Text = "مرتجعات البيع", Selected = reportType == "SalesReturns" },
                new SelectListItem { Value = "PurchaseReturns", Text = "مرتجعات الشراء", Selected = reportType == "PurchaseReturns" },
                new SelectListItem { Value = "Adjustments", Text = "تسويات", Selected = reportType == "Adjustments" },
                new SelectListItem { Value = "Transfers", Text = "تحويلات", Selected = reportType == "Transfers" },
                new SelectListItem { Value = "PurchaseRequests", Text = "طلبات شراء", Selected = reportType == "PurchaseRequests" },
                new SelectListItem { Value = "SalesOrders", Text = "أوامر بيع", Selected = reportType == "SalesOrders" }
            };
            ViewBag.ReportTypes = reportTypes;
            ViewBag.ReportType = reportType ?? "";
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;
            ViewBag.Search = search ?? "";
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.FilterCol_Date = filterCol_date;
            ViewBag.FilterCol_DocNo = filterCol_docNo;
            ViewBag.FilterCol_ProductCode = filterCol_productCode;
            ViewBag.FilterCol_ProductName = filterCol_productName;
            ViewBag.FilterCol_Party = filterCol_party;
            ViewBag.FilterCol_Warehouse = filterCol_warehouse;
            ViewBag.FilterCol_Author = filterCol_author;
            ViewBag.FilterCol_Region = filterCol_region;
            ViewBag.FilterCol_DocNameAr = filterCol_docNameAr;
            ViewBag.FilterCol_QtyExpr = filterCol_qtyExpr;
            ViewBag.FilterCol_UnitpriceExpr = filterCol_unitpriceExpr;
            ViewBag.FilterCol_LinetotalExpr = filterCol_linetotalExpr;
            ViewBag.FilterCol_Batch = filterCol_batch;
            ViewBag.FilterCol_Expiry = filterCol_expiry;
            ViewBag.FilterCol_Notes = filterCol_notes;
            ViewBag.Sort = sort ?? "Date";
            ViewBag.Dir = dir ?? "desc";

            List<ProductDetailsReportRow> list = new List<ProductDetailsReportRow>();
            int totalCount = 0;
            decimal totalQtyFiltered = 0m;
            decimal totalAmountFiltered = 0m;

            if (string.IsNullOrWhiteSpace(reportType))
            {
                ViewBag.TotalCount = 0;
                ViewBag.TotalPages = 0;
                ViewBag.TotalQtyFiltered = 0m;
                ViewBag.TotalAmountFiltered = 0m;
                ViewBag.ReportData = list;
                return View();
            }

            var fromDt = fromDate.HasValue ? DateTime.SpecifyKind(fromDate.Value, DateTimeKind.Local) : (DateTime?)null;
            var toDt = toDate.HasValue ? DateTime.SpecifyKind(toDate.Value, DateTimeKind.Local) : (DateTime?)null;
            var searchTrim = search?.Trim() ?? "";

            if (!PdrDocNameArPasses(reportType!, filterCol_docNameAr))
            {
                ViewBag.TotalCount = 0;
                ViewBag.TotalPages = 0;
                ViewBag.TotalQtyFiltered = 0m;
                ViewBag.TotalAmountFiltered = 0m;
                ViewBag.ReportData = list;
                return View();
            }

            (list, totalCount, totalQtyFiltered, totalAmountFiltered, page) = await LoadProductDetailsReportDataAsync(
                reportType!, fromDt, toDt, searchTrim,
                filterCol_date, filterCol_docNo, filterCol_productCode, filterCol_productName,
                filterCol_party, filterCol_warehouse, filterCol_author, filterCol_region, filterCol_docNameAr,
                filterCol_qtyExpr, filterCol_unitpriceExpr, filterCol_linetotalExpr,
                filterCol_batch, filterCol_expiry, filterCol_notes,
                sort ?? "Date", dir ?? "desc", page, pageSize);

            int totalPages = pageSize == 0
                ? 1
                : (totalCount > 0 ? (int)Math.Ceiling((double)totalCount / pageSize) : 0);
            ViewBag.TotalCount = totalCount;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalQtyFiltered = totalQtyFiltered;
            ViewBag.TotalAmountFiltered = totalAmountFiltered;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.ReportData = list;
            return View();
        }

        /// <summary>
        /// جلب قيم عمود للتقرير (للوحة فلتر الأعمدة الشبيهة بإكسل).
        /// </summary>
        [HttpGet]
        [RequirePermission("Reports.ProductDetailsReport")]
        public async Task<IActionResult> GetProductDetailsReportColumnValues(
            string reportType,
            string column,
            string? search = null,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? mainSearch = null)
        {
            var col = (column ?? "").Trim().ToLowerInvariant();
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(reportType)) return Json(Array.Empty<object>());
            if (col is "qty" or "unitprice" or "linetotal") return Json(Array.Empty<object>());

            var fromDtCv = fromDate.HasValue ? DateTime.SpecifyKind(fromDate.Value, DateTimeKind.Local) : (DateTime?)null;
            var toDtCv = toDate.HasValue ? DateTime.SpecifyKind(toDate.Value, DateTimeKind.Local) : (DateTime?)null;
            var mainSearchTrim = mainSearch?.Trim() ?? "";

            if (reportType == "Sales")
            {
                var q = _context.SalesInvoiceLines.AsNoTracking()
                    .Include(l => l.SalesInvoice).ThenInclude(h => h!.Customer)
                    .Include(l => l.SalesInvoice).ThenInclude(h => h!.Warehouse).ThenInclude(w => w!.Branch)
                    .Include(l => l.Product)
                    .Where(l => l.SalesInvoice != null);
                if (fromDtCv.HasValue) q = q.Where(l => l.SalesInvoice!.SIDate >= fromDtCv.Value);
                if (toDtCv.HasValue) q = q.Where(l => l.SalesInvoice!.SIDate <= toDtCv.Value);
                if (!string.IsNullOrEmpty(mainSearchTrim))
                    q = q.Where(l => l.Product != null && ((l.Product.ProdName != null && l.Product.ProdName.Contains(mainSearchTrim)) || l.Product.ProdId.ToString() == mainSearchTrim));
                if (col == "author") { var list = await q.Where(l => l.SalesInvoice!.CreatedBy != null).Select(l => l.SalesInvoice!.CreatedBy!).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v })); }
                if (col == "region") { var list = await q.Where(l => l.SalesInvoice!.Warehouse != null && l.SalesInvoice.Warehouse.Branch != null).Select(l => l.SalesInvoice!.Warehouse!.Branch!.BranchName).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s != null && s.ToLower().Contains(searchTerm)).ToList()!; return Json(list.Select(v => new { value = v, display = v })); }
                if (col == "docno") { var list = await q.Select(l => l.SalesInvoice!.SIId).Distinct().OrderBy(x => x).Take(500).ToListAsync(); return Json(list.Select(v => new { value = v.ToString(), display = v.ToString() })); }
                if (col == "party") { var list = await q.Where(l => l.SalesInvoice!.Customer != null).Select(l => l.SalesInvoice!.Customer!.CustomerName).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v })); }
                if (col == "warehouse") { var list = await q.Where(l => l.SalesInvoice!.Warehouse != null).Select(l => l.SalesInvoice!.Warehouse!.WarehouseName).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v })); }
                if (col == "productcode") { var list = await q.Where(l => l.Product != null).Select(l => l.Product!.ProdId).Distinct().OrderBy(x => x).Take(500).ToListAsync(); return Json(list.Select(v => new { value = v.ToString(), display = v.ToString() })); }
                if (col == "productname") { var list = await q.Where(l => l.Product != null && l.Product.ProdName != null).Select(l => l.Product!.ProdName!).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v.Length > 50 ? v.Substring(0, 50) + "…" : v })); }
                if (col == "date") { var list = await q.Select(l => l.SalesInvoice!.SIDate.Date).Distinct().OrderByDescending(x => x).Take(300).ToListAsync(); return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd"), display = d.ToString("yyyy-MM-dd") })); }
                if (col == "batch") { var list = await q.Where(l => l.BatchNo != null && l.BatchNo != "").Select(l => l.BatchNo!).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v })); }
                if (col == "expiry") { var list = await q.Where(l => l.Expiry != null).Select(l => l.Expiry!.Value.Date).Distinct().OrderByDescending(x => x).Take(300).ToListAsync(); return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd"), display = d.ToString("yyyy-MM-dd") })); }
                if (col == "notes") { var list = await q.Where(l => l.Notes != null && l.Notes != "").Select(l => l.Notes!).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v.Length > 80 ? v.Substring(0, 80) + "…" : v })); }
            }

            if (reportType == "Purchases")
            {
                var q = _context.PILines.AsNoTracking()
                    .Include(l => l.PurchaseInvoice).ThenInclude(h => h!.Warehouse).ThenInclude(w => w!.Branch)
                    .Include(l => l.Product)
                    .Where(l => l.PurchaseInvoice != null);
                if (fromDtCv.HasValue) q = q.Where(l => l.PurchaseInvoice!.PIDate >= fromDtCv.Value);
                if (toDtCv.HasValue) q = q.Where(l => l.PurchaseInvoice!.PIDate <= toDtCv.Value);
                if (!string.IsNullOrEmpty(mainSearchTrim))
                    q = q.Where(l => l.Product != null && ((l.Product.ProdName != null && l.Product.ProdName.Contains(mainSearchTrim)) || l.Product.ProdId.ToString() == mainSearchTrim));
                if (col == "author") { var list = await q.Where(l => l.PurchaseInvoice!.CreatedBy != null).Select(l => l.PurchaseInvoice!.CreatedBy!).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v })); }
                if (col == "region") { var list = await q.Where(l => l.PurchaseInvoice!.Warehouse != null && l.PurchaseInvoice.Warehouse.Branch != null).Select(l => l.PurchaseInvoice!.Warehouse!.Branch!.BranchName).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s != null && s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v })); }
                if (col == "docno") { var list = await q.Select(l => l.PurchaseInvoice!.PIId).Distinct().OrderBy(x => x).Take(500).ToListAsync(); return Json(list.Select(v => new { value = v.ToString(), display = v.ToString() })); }
                if (col == "party") { var list = await q.Where(l => l.PurchaseInvoice!.Customer != null).Select(l => l.PurchaseInvoice!.Customer!.CustomerName).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v })); }
                if (col == "warehouse") { var list = await q.Where(l => l.PurchaseInvoice!.Warehouse != null).Select(l => l.PurchaseInvoice!.Warehouse!.WarehouseName).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v })); }
                if (col == "productcode") { var list = await q.Where(l => l.Product != null).Select(l => l.Product!.ProdId).Distinct().OrderBy(x => x).Take(500).ToListAsync(); return Json(list.Select(v => new { value = v.ToString(), display = v.ToString() })); }
                if (col == "productname") { var list = await q.Where(l => l.Product != null && l.Product.ProdName != null).Select(l => l.Product!.ProdName!).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v.Length > 50 ? v.Substring(0, 50) + "…" : v })); }
                if (col == "date") { var list = await q.Select(l => l.PurchaseInvoice!.PIDate.Date).Distinct().OrderByDescending(x => x).Take(300).ToListAsync(); return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd"), display = d.ToString("yyyy-MM-dd") })); }
                if (col == "batch") { var list = await q.Where(l => l.BatchNo != null && l.BatchNo != "").Select(l => l.BatchNo!).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v })); }
                if (col == "expiry") { var list = await q.Where(l => l.Expiry != null).Select(l => l.Expiry!.Value.Date).Distinct().OrderByDescending(x => x).Take(300).ToListAsync(); return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd"), display = d.ToString("yyyy-MM-dd") })); }
            }

            if (reportType == "SalesReturns")
            {
                var q = from line in _context.SalesReturnLines.AsNoTracking() join sr in _context.SalesReturns on line.SRId equals sr.SRId join c in _context.Customers on sr.CustomerId equals c.CustomerId join p in _context.Products on line.ProdId equals p.ProdId select new { line, sr, c, p };
                if (fromDtCv.HasValue) q = q.Where(x => x.sr.SRDate >= fromDtCv.Value);
                if (toDtCv.HasValue) q = q.Where(x => x.sr.SRDate <= toDtCv.Value);
                if (!string.IsNullOrEmpty(mainSearchTrim))
                    q = q.Where(x => (x.p.ProdName != null && x.p.ProdName.Contains(mainSearchTrim)) || x.p.ProdId.ToString() == mainSearchTrim);
                if (col == "author") { var list = await q.Where(x => x.sr.CreatedBy != null).Select(x => x.sr.CreatedBy!).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v })); }
                if (col == "docno") { var list = await q.Select(x => x.sr.SRId).Distinct().OrderBy(x => x).Take(500).ToListAsync(); return Json(list.Select(v => new { value = v.ToString(), display = v.ToString() })); }
                if (col == "party") { var list = await q.Select(x => x.c.CustomerName).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v })); }
                if (col == "productcode") { var list = await q.Select(x => x.p.ProdId).Distinct().OrderBy(x => x).Take(500).ToListAsync(); return Json(list.Select(v => new { value = v.ToString(), display = v.ToString() })); }
                if (col == "productname") { var list = await q.Where(x => x.p.ProdName != null).Select(x => x.p.ProdName!).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v.Length > 50 ? v.Substring(0, 50) + "…" : v })); }
                if (col == "date") { var list = await q.Select(x => x.sr.SRDate.Date).Distinct().OrderByDescending(x => x).Take(300).ToListAsync(); return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd"), display = d.ToString("yyyy-MM-dd") })); }
                if (col == "batch") { var list = await q.Where(x => x.line.BatchNo != null && x.line.BatchNo != "").Select(x => x.line.BatchNo!).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v })); }
                if (col == "expiry") { var list = await q.Where(x => x.line.Expiry != null).Select(x => x.line.Expiry!.Value.Date).Distinct().OrderByDescending(x => x).Take(300).ToListAsync(); return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd"), display = d.ToString("yyyy-MM-dd") })); }
            }

            if (reportType == "PurchaseReturns")
            {
                var q = _context.PurchaseReturnLines.AsNoTracking().Include(l => l.PurchaseReturn).Include(l => l.Product).Where(l => l.PurchaseReturn != null);
                if (fromDtCv.HasValue) q = q.Where(l => l.PurchaseReturn!.PRetDate >= fromDtCv.Value);
                if (toDtCv.HasValue) q = q.Where(l => l.PurchaseReturn!.PRetDate <= toDtCv.Value);
                if (!string.IsNullOrEmpty(mainSearchTrim))
                    q = q.Where(l => l.Product != null && ((l.Product.ProdName != null && l.Product.ProdName.Contains(mainSearchTrim)) || l.Product.ProdId.ToString() == mainSearchTrim));
                if (col == "author") { var list = await q.Where(l => l.PurchaseReturn!.CreatedBy != null).Select(l => l.PurchaseReturn!.CreatedBy!).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v })); }
                if (col == "docno") { var list = await q.Select(l => l.PurchaseReturn!.PRetId).Distinct().OrderBy(x => x).Take(500).ToListAsync(); return Json(list.Select(v => new { value = v.ToString(), display = v.ToString() })); }
                if (col == "party") { var list = await q.Where(l => l.PurchaseReturn!.Customer != null).Select(l => l.PurchaseReturn!.Customer!.CustomerName).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v })); }
                if (col == "productcode") { var list = await q.Where(l => l.Product != null).Select(l => l.Product!.ProdId).Distinct().OrderBy(x => x).Take(500).ToListAsync(); return Json(list.Select(v => new { value = v.ToString(), display = v.ToString() })); }
                if (col == "productname") { var list = await q.Where(l => l.Product != null && l.Product.ProdName != null).Select(l => l.Product!.ProdName!).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v.Length > 50 ? v.Substring(0, 50) + "…" : v })); }
                if (col == "date") { var list = await q.Select(l => l.PurchaseReturn!.PRetDate.Date).Distinct().OrderByDescending(x => x).Take(300).ToListAsync(); return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd"), display = d.ToString("yyyy-MM-dd") })); }
                if (col == "batch") { var list = await q.Where(l => l.BatchNo != null && l.BatchNo != "").Select(l => l.BatchNo!).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v })); }
                if (col == "expiry") { var list = await q.Where(l => l.Expiry != null).Select(l => l.Expiry!.Value.Date).Distinct().OrderByDescending(x => x).Take(300).ToListAsync(); return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd"), display = d.ToString("yyyy-MM-dd") })); }
            }

            if (reportType == "Adjustments")
            {
                var q = _context.StockAdjustmentLines.AsNoTracking().Include(l => l.StockAdjustment).ThenInclude(h => h!.Warehouse).ThenInclude(w => w!.Branch).Include(l => l.Product).Where(l => l.StockAdjustment != null && l.Product != null);
                if (fromDtCv.HasValue) q = q.Where(l => l.StockAdjustment!.AdjustmentDate >= fromDtCv.Value);
                if (toDtCv.HasValue) q = q.Where(l => l.StockAdjustment!.AdjustmentDate <= toDtCv.Value);
                if (!string.IsNullOrEmpty(mainSearchTrim))
                    q = q.Where(l => (l.Product!.ProdName != null && l.Product.ProdName.Contains(mainSearchTrim)) || l.Product.ProdId.ToString() == mainSearchTrim);
                if (col == "author") { var list = await q.Where(l => l.StockAdjustment!.PostedBy != null).Select(l => l.StockAdjustment!.PostedBy!).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v })); }
                if (col == "region") { var list = await q.Where(l => l.StockAdjustment!.Warehouse != null && l.StockAdjustment.Warehouse.Branch != null).Select(l => l.StockAdjustment!.Warehouse!.Branch!.BranchName).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s != null && s.ToLower().Contains(searchTerm)).ToList()!; return Json(list.Select(v => new { value = v, display = v })); }
                if (col == "docno") { var list = await q.Select(l => l.StockAdjustment!.Id).Distinct().OrderBy(x => x).Take(500).ToListAsync(); return Json(list.Select(v => new { value = v.ToString(), display = v.ToString() })); }
                if (col == "warehouse") { var list = await q.Where(l => l.StockAdjustment!.Warehouse != null).Select(l => l.StockAdjustment!.Warehouse!.WarehouseName).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v })); }
                if (col == "productcode") { var list = await q.Select(l => l.Product!.ProdId).Distinct().OrderBy(x => x).Take(500).ToListAsync(); return Json(list.Select(v => new { value = v.ToString(), display = v.ToString() })); }
                if (col == "productname") { var list = await q.Where(l => l.Product!.ProdName != null).Select(l => l.Product!.ProdName!).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v.Length > 50 ? v.Substring(0, 50) + "…" : v })); }
                if (col == "date") { var list = await q.Select(l => l.StockAdjustment!.AdjustmentDate.Date).Distinct().OrderByDescending(x => x).Take(300).ToListAsync(); return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd"), display = d.ToString("yyyy-MM-dd") })); }
                if (col == "notes") { var list = await q.Where(l => l.Note != null && l.Note != "").Select(l => l.Note!).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v.Length > 80 ? v.Substring(0, 80) + "…" : v })); }
            }

            if (reportType == "Transfers")
            {
                var q = _context.StockTransferLines.AsNoTracking().Include(l => l.StockTransfer).ThenInclude(st => st!.FromWarehouse).ThenInclude(w => w!.Branch).Include(l => l.Product).Where(l => l.StockTransfer != null && l.Product != null);
                if (fromDtCv.HasValue) q = q.Where(l => l.StockTransfer!.TransferDate >= fromDtCv.Value);
                if (toDtCv.HasValue) q = q.Where(l => l.StockTransfer!.TransferDate <= toDtCv.Value);
                if (!string.IsNullOrEmpty(mainSearchTrim))
                    q = q.Where(l => (l.Product!.ProdName != null && l.Product.ProdName.Contains(mainSearchTrim)) || l.Product.ProdId.ToString() == mainSearchTrim);
                if (col == "author") { var list = await q.Where(l => l.StockTransfer!.PostedBy != null).Select(l => l.StockTransfer!.PostedBy!).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v })); }
                if (col == "region") { var list = await q.Where(l => l.StockTransfer!.FromWarehouse != null && l.StockTransfer.FromWarehouse.Branch != null).Select(l => l.StockTransfer!.FromWarehouse!.Branch!.BranchName).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s != null && s.ToLower().Contains(searchTerm)).ToList()!; return Json(list.Select(v => new { value = v, display = v })); }
                if (col == "docno") { var list = await q.Select(l => l.StockTransfer!.Id).Distinct().OrderBy(x => x).Take(500).ToListAsync(); return Json(list.Select(v => new { value = v.ToString(), display = v.ToString() })); }
                if (col == "productcode") { var list = await q.Select(l => l.Product!.ProdId).Distinct().OrderBy(x => x).Take(500).ToListAsync(); return Json(list.Select(v => new { value = v.ToString(), display = v.ToString() })); }
                if (col == "productname") { var list = await q.Where(l => l.Product!.ProdName != null).Select(l => l.Product!.ProdName!).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v.Length > 50 ? v.Substring(0, 50) + "…" : v })); }
                if (col == "date") { var list = await q.Select(l => l.StockTransfer!.TransferDate.Date).Distinct().OrderByDescending(x => x).Take(300).ToListAsync(); return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd"), display = d.ToString("yyyy-MM-dd") })); }
                if (col == "notes") { var list = await q.Where(l => l.Note != null && l.Note != "").Select(l => l.Note!).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v.Length > 80 ? v.Substring(0, 80) + "…" : v })); }
            }

            if (reportType == "PurchaseRequests")
            {
                var q = _context.PRLines.AsNoTracking().Include(l => l.PurchaseRequest).ThenInclude(h => h!.Customer).Include(l => l.PurchaseRequest).ThenInclude(h => h!.Warehouse).ThenInclude(w => w!.Branch).Include(l => l.Product).Where(l => l.PurchaseRequest != null);
                if (fromDtCv.HasValue) q = q.Where(l => l.PurchaseRequest!.PRDate >= fromDtCv.Value);
                if (toDtCv.HasValue) q = q.Where(l => l.PurchaseRequest!.PRDate <= toDtCv.Value);
                if (!string.IsNullOrEmpty(mainSearchTrim))
                    q = q.Where(l => l.Product != null && ((l.Product.ProdName != null && l.Product.ProdName.Contains(mainSearchTrim)) || l.Product.ProdId.ToString() == mainSearchTrim));
                if (col == "author") { var list = await q.Where(l => l.PurchaseRequest!.CreatedBy != null).Select(l => l.PurchaseRequest!.CreatedBy!).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v })); }
                if (col == "region") { var list = await q.Where(l => l.PurchaseRequest!.Warehouse != null && l.PurchaseRequest.Warehouse.Branch != null).Select(l => l.PurchaseRequest!.Warehouse!.Branch!.BranchName).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s != null && s.ToLower().Contains(searchTerm)).ToList()!; return Json(list.Select(v => new { value = v, display = v })); }
                if (col == "docno") { var list = await q.Select(l => l.PurchaseRequest!.PRId).Distinct().OrderBy(x => x).Take(500).ToListAsync(); return Json(list.Select(v => new { value = v.ToString(), display = v.ToString() })); }
                if (col == "party") { var list = await q.Where(l => l.PurchaseRequest!.Customer != null).Select(l => l.PurchaseRequest!.Customer!.CustomerName).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v })); }
                if (col == "warehouse") { var list = await q.Where(l => l.PurchaseRequest!.Warehouse != null).Select(l => l.PurchaseRequest!.Warehouse!.WarehouseName).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v })); }
                if (col == "productcode") { var list = await q.Where(l => l.Product != null).Select(l => l.Product!.ProdId).Distinct().OrderBy(x => x).Take(500).ToListAsync(); return Json(list.Select(v => new { value = v.ToString(), display = v.ToString() })); }
                if (col == "productname") { var list = await q.Where(l => l.Product != null && l.Product.ProdName != null).Select(l => l.Product!.ProdName!).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v.Length > 50 ? v.Substring(0, 50) + "…" : v })); }
                if (col == "date") { var list = await q.Select(l => l.PurchaseRequest!.PRDate.Date).Distinct().OrderByDescending(x => x).Take(300).ToListAsync(); return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd"), display = d.ToString("yyyy-MM-dd") })); }
                if (col == "batch") { var list = await q.Where(l => l.PreferredBatchNo != null && l.PreferredBatchNo != "").Select(l => l.PreferredBatchNo!).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v })); }
                if (col == "expiry") { var list = await q.Where(l => l.PreferredExpiry != null).Select(l => l.PreferredExpiry!.Value.Date).Distinct().OrderByDescending(x => x).Take(300).ToListAsync(); return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd"), display = d.ToString("yyyy-MM-dd") })); }
            }

            if (reportType == "SalesOrders")
            {
                var q = _context.SOLines.AsNoTracking().Include(l => l.SalesOrder).ThenInclude(h => h!.Customer).Include(l => l.Product).Where(l => l.SalesOrder != null);
                if (fromDtCv.HasValue) q = q.Where(l => l.SalesOrder!.SODate >= fromDtCv.Value);
                if (toDtCv.HasValue) q = q.Where(l => l.SalesOrder!.SODate <= toDtCv.Value);
                if (!string.IsNullOrEmpty(mainSearchTrim))
                    q = q.Where(l => l.Product != null && ((l.Product.ProdName != null && l.Product.ProdName.Contains(mainSearchTrim)) || l.Product.ProdId.ToString() == mainSearchTrim));
                if (col == "author") { var list = await q.Where(l => l.SalesOrder!.CreatedBy != null).Select(l => l.SalesOrder!.CreatedBy!).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v })); }
                if (col == "docno") { var list = await q.Select(l => l.SalesOrder!.SOId).Distinct().OrderBy(x => x).Take(500).ToListAsync(); return Json(list.Select(v => new { value = v.ToString(), display = v.ToString() })); }
                if (col == "party") { var list = await q.Where(l => l.SalesOrder!.Customer != null).Select(l => l.SalesOrder!.Customer!.CustomerName).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v })); }
                if (col == "productcode") { var list = await q.Where(l => l.Product != null).Select(l => l.Product!.ProdId).Distinct().OrderBy(x => x).Take(500).ToListAsync(); return Json(list.Select(v => new { value = v.ToString(), display = v.ToString() })); }
                if (col == "productname") { var list = await q.Where(l => l.Product != null && l.Product.ProdName != null).Select(l => l.Product!.ProdName!).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v.Length > 50 ? v.Substring(0, 50) + "…" : v })); }
                if (col == "date") { var list = await q.Select(l => l.SalesOrder!.SODate.Date).Distinct().OrderByDescending(x => x).Take(300).ToListAsync(); return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd"), display = d.ToString("yyyy-MM-dd") })); }
                if (col == "batch") { var list = await q.Where(l => l.PreferredBatchNo != null && l.PreferredBatchNo != "").Select(l => l.PreferredBatchNo!).Distinct().OrderBy(x => x).Take(500).ToListAsync(); if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList(); return Json(list.Select(v => new { value = v, display = v })); }
                if (col == "expiry") { var list = await q.Where(l => l.PreferredExpiry != null).Select(l => l.PreferredExpiry!.Value.Date).Distinct().OrderByDescending(x => x).Take(300).ToListAsync(); return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd"), display = d.ToString("yyyy-MM-dd") })); }
            }

            if (col == "docnamear")
            {
                var name = reportType == "PurchaseReturns" ? "مرتجع شراء" : reportType == "Adjustments" ? "تسوية جرد" : reportType == "Transfers" ? "تحويل مخزني" : reportType == "PurchaseRequests" ? "طلب شراء" : reportType == "SalesOrders" ? "أمر بيع" : reportType == "Sales" ? "فاتورة مبيعات" : reportType == "Purchases" ? "فاتورة مشتريات" : reportType == "SalesReturns" ? "مرتجع بيع" : "";
                if (!string.IsNullOrEmpty(name)) return Json(new[] { new { value = name, display = name } });
            }

            return Json(Array.Empty<object>());
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
        // تقرير: أرصدة العملاء
        // يعرض العميل، الرصيد الحالي، الحد الائتماني، المبيعات والمشتريات بين تاريخين
        // =========================================================
        [HttpGet]
        [RequirePermission("Reports.CustomerBalances")]
        public async Task<IActionResult> CustomerBalances(
            string? search,
            string? searchMode,
            string? partyCategory,
            int? governorateId,
            DateTime? fromDate,
            DateTime? toDate,
            bool includeZeroBalance = false,
            string? sortBy = "name",
            string? sortDir = "asc",
            string? filterCol_code = null,
            string? filterCol_name = null,
            string? filterCol_category = null,
            string? filterCol_phone = null,
            string? filterCol_account = null,
            string? filterCol_debit = null,
            string? filterCol_credit = null,
            string? filterCol_creditlimit = null,
            string? filterCol_sales = null,
            string? filterCol_purchases = null,
            string? filterCol_returns = null,
            string? filterCol_availablecredit = null,
            bool loadReport = false,
            int page = 1,
            int pageSize = 10)
        {
            var pageSizeQuery = Request.Query["pageSize"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageSizeQuery) && int.TryParse(pageSizeQuery, out var psVal))
                pageSize = psVal;
            if (pageSize < 0) pageSize = 10;
            int[] allowedPageSizes = { 10, 25, 50, 100, 200, 500, 1000, 5000, 0 };
            if (pageSize > 0 && Array.IndexOf(allowedPageSizes, pageSize) < 0)
                pageSize = 10;

            var sep = new[] { '|', ',' };
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
            // 1.1) تحميل قائمة العملاء للأوتوكومبليت (datalist) — نفس منطق فاتورة المبيعات
            // =========================================================
            var (hiddenAccountIdsAuto, restrictedAuto) = await _accountVisibilityService.GetVisibilityStateForCurrentUserAsync();
            var hiddenListAuto = hiddenAccountIdsAuto.ToList();
            IQueryable<Customer> customersAutoQuery = _context.Customers.AsNoTracking();
            if (hiddenListAuto.Count > 0)
                customersAutoQuery = restrictedAuto
                    ? customersAutoQuery.Where(c => c.AccountId != null && !hiddenListAuto.Contains(c.AccountId.Value))
                    : customersAutoQuery.Where(c => c.AccountId == null || !hiddenListAuto.Contains(c.AccountId.Value));
            var customersAuto = await customersAutoQuery
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
            ViewBag.SearchMode = string.IsNullOrWhiteSpace(searchMode) ? "contains" : searchMode!.Trim();
            ViewBag.PartyCategory = partyCategory;
            ViewBag.GovernorateId = governorateId;
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;
            ViewBag.IncludeZeroBalance = includeZeroBalance;
            ViewBag.SortBy = sortBy;
            ViewBag.SortDir = sortDir;
            ViewBag.FilterCol_Code = filterCol_code;
            ViewBag.FilterCol_Name = filterCol_name;
            ViewBag.FilterCol_Category = filterCol_category;
            ViewBag.FilterCol_Phone = filterCol_phone;
            ViewBag.FilterCol_Account = filterCol_account;
            ViewBag.FilterCol_Debit = filterCol_debit;
            ViewBag.FilterCol_Credit = filterCol_credit;
            ViewBag.FilterCol_CreditLimit = filterCol_creditlimit;
            ViewBag.FilterCol_Sales = filterCol_sales;
            ViewBag.FilterCol_Purchases = filterCol_purchases;
            ViewBag.FilterCol_Returns = filterCol_returns;
            ViewBag.FilterCol_AvailableCredit = filterCol_availablecredit;

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
                ViewBag.TotalCustomersUnfiltered = 0;
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
            // 4) بناء الاستعلام الأساسي للعملاء (عند طلب التقرير) — نفس منطق فاتورة المبيعات
            // =========================================================
            var customersQuery = _context.Customers.AsNoTracking().AsQueryable();
            customersQuery = await _accountVisibilityService.ApplyCustomerVisibilityFilterAsync(customersQuery);

            // فلتر البحث (اسم العميل أو الكود)
            customersQuery = ApplyCustomerBalancesSearchFilter(customersQuery, search, searchMode);

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
                ViewBag.TotalDebit = 0m;
                ViewBag.TotalCredit = 0m;
                ViewBag.TotalSales = 0m;
                ViewBag.TotalPurchases = 0m;
                ViewBag.TotalReturns = 0m;
                ViewBag.TotalCustomersUnfiltered = 0;
                ViewBag.Page = 1;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalPages = 1;
                ViewBag.TotalCount = 0;
                return View();
            }

            // =========================================================
            // 6) تحميل البيانات بشكل مجمع (Bulk Loading) - تحسين الأداء
            // =========================================================

            // 6.1) تحميل جميع Customers دفعة واحدة (مع كود الإكسل للمقارنة)
            var customersDict = await customersQuery
                .Select(c => new
                {
                    c.CustomerId,
                    c.CustomerName,
                    c.PartyCategory,
                    c.Phone1,
                    c.CreditLimit,
                    c.ExternalCode,
                    c.AccountId,
                    AccountCode = c.Account != null ? c.Account.AccountCode : null,
                    AccountName = c.Account != null ? c.Account.AccountName : null
                })
                .ToDictionaryAsync(c => c.CustomerId);

            // 6.1.1) حساب الرصيد الحالي من LedgerEntries (مصدر الحقيقة) — نفس قيود دفتر الأستاذ الظاهرة للمستخدم
            var ledgerForBalance = _context.LedgerEntries
                .AsNoTracking()
                .Include(e => e.Account)
                .Include(e => e.Customer)
                .Where(e => e.CustomerId.HasValue && customerIds.Contains(e.CustomerId.Value));
            ledgerForBalance = await _accountVisibilityService.ApplyLedgerEntryListVisibilityFilterAsync(ledgerForBalance);
            var balanceByCustomer = await ledgerForBalance
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
                decimal availableCredit = creditLimit == 0 ? 0m : (creditLimit - currentBalance);

                reportData.Add(new CustomerBalanceReportDto
                {
                    CustomerId = customerId,
                    CustomerCode = customerId.ToString(),
                    ExternalCode = customer.ExternalCode,
                    CustomerName = customer.CustomerName ?? "",
                    AccountId = customer.AccountId,
                    AccountCode = customer.AccountCode,
                    AccountName = customer.AccountName,
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

            // عدد العملاء قبل فلاتر الأعمدة فقط (ليطابق "عرض 1-200 من X" عندما لا يوجد فلتر أعمدة)
            int totalBaseCount = reportData.Count;

            // =========================================================
            // 6.5) فلاتر أعمدة بنمط Excel (في الذاكرة)
            // =========================================================
            if (!string.IsNullOrWhiteSpace(filterCol_code))
            {
                reportData = reportData.Where(r =>
                    CustomerBalancesNumericFilter.MatchesDecimal(r.CustomerId, filterCol_code)).ToList();
            }
            if (!string.IsNullOrWhiteSpace(filterCol_name))
            {
                var vals = filterCol_name.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    reportData = reportData.Where(r => vals.Any(v => (r.CustomerName ?? "").Contains(v))).ToList();
            }
            if (!string.IsNullOrWhiteSpace(filterCol_category))
            {
                var vals = filterCol_category.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    reportData = reportData.Where(r => vals.Any(v => (r.PartyCategory ?? "").Contains(v))).ToList();
            }
            if (!string.IsNullOrWhiteSpace(filterCol_phone))
            {
                var vals = filterCol_phone.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    reportData = reportData.Where(r => vals.Any(v => (r.Phone1 ?? "").Contains(v))).ToList();
            }
            if (!string.IsNullOrWhiteSpace(filterCol_account))
            {
                var vals = filterCol_account.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    reportData = reportData.Where(r => vals.Any(v => (r.AccountDisplay ?? "").Contains(v))).ToList();
            }
            if (!string.IsNullOrWhiteSpace(filterCol_debit))
            {
                reportData = reportData.Where(r =>
                    CustomerBalancesNumericFilter.MatchesDecimal(
                        r.CurrentBalance > 0 ? r.CurrentBalance : 0m,
                        filterCol_debit)).ToList();
            }
            if (!string.IsNullOrWhiteSpace(filterCol_credit))
            {
                reportData = reportData.Where(r =>
                    CustomerBalancesNumericFilter.MatchesDecimal(
                        r.CurrentBalance < 0 ? Math.Abs(r.CurrentBalance) : 0m,
                        filterCol_credit)).ToList();
            }
            if (!string.IsNullOrWhiteSpace(filterCol_creditlimit))
            {
                reportData = reportData.Where(r =>
                    CustomerBalancesNumericFilter.MatchesDecimal(r.CreditLimit, filterCol_creditlimit)).ToList();
            }
            if (!string.IsNullOrWhiteSpace(filterCol_sales))
            {
                reportData = reportData.Where(r =>
                    CustomerBalancesNumericFilter.MatchesDecimal(r.TotalSales, filterCol_sales)).ToList();
            }
            if (!string.IsNullOrWhiteSpace(filterCol_purchases))
            {
                reportData = reportData.Where(r =>
                    CustomerBalancesNumericFilter.MatchesDecimal(r.TotalPurchases, filterCol_purchases)).ToList();
            }
            if (!string.IsNullOrWhiteSpace(filterCol_returns))
            {
                reportData = reportData.Where(r =>
                    CustomerBalancesNumericFilter.MatchesDecimal(r.TotalReturns, filterCol_returns)).ToList();
            }
            if (!string.IsNullOrWhiteSpace(filterCol_availablecredit))
            {
                reportData = reportData.Where(r =>
                    CustomerBalancesNumericFilter.MatchesDecimal(r.AvailableCredit, filterCol_availablecredit)).ToList();
            }

            // =========================================================
            // 7) الترتيب
            // =========================================================
            bool isDesc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
            switch (sortBy?.ToLowerInvariant())
            {
                case "code":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.CustomerId).ToList()
                        : reportData.OrderBy(r => r.CustomerId).ToList();
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
                case "account":
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => r.AccountDisplay ?? "").ToList()
                        : reportData.OrderBy(r => r.AccountDisplay ?? "").ToList();
                    break;
                case "debit":
                    // عمود المدين: الترتيب حسب قيمة المدين المعروضة (الرصيد إن كان موجباً، وإلا صفر)
                    decimal DebitDisplayValue(CustomerBalanceReportDto r) => r.CurrentBalance > 0 ? r.CurrentBalance : 0m;
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => DebitDisplayValue(r)).ToList()
                        : reportData.OrderBy(r => DebitDisplayValue(r)).ToList();
                    break;
                case "balance":
                case "credit":
                    // عمود الدائن: الترتيب حسب قيمة الدائن المعروضة (صفر إن الرصيد مدين، |الرصيد| إن كان دائن)
                    decimal CreditDisplayValue(CustomerBalanceReportDto r) => r.CurrentBalance < 0 ? Math.Abs(r.CurrentBalance) : 0m;
                    reportData = isDesc
                        ? reportData.OrderByDescending(r => CreditDisplayValue(r)).ToList()
                        : reportData.OrderBy(r => CreditDisplayValue(r)).ToList();
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
            // المدين/الدائن/صافي الرصيد: مصدر واحد مع «ربح الميزانية» (نفس نطاق customerIds + قيود الدفتر).
            // =========================================================
            var customerIdSetForTotals = customerIds.ToHashSet();
            var (totalDebitSum, totalCreditSum) = await ComputeBalanceSheetCustomerDebitCreditFromCustomerIdsAsync(customerIdSetForTotals, null);
            decimal totalBalance = totalDebitSum - totalCreditSum;
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
            ViewBag.TotalCustomersUnfiltered = totalBaseCount; // إجمالي قبل فلاتر الأعمدة فقط (يطابق الترقيم عند عدم الفلترة)
            ViewBag.TotalSales = totalSalesSum;
            ViewBag.TotalPurchases = totalPurchasesSum;
            ViewBag.TotalReturns = totalReturnsSum;
            ViewBag.TotalCreditLimit = totalCreditLimit;
            ViewBag.TotalAvailableCredit = totalAvailableCredit;
            ViewBag.FilterCol_Code = filterCol_code;
            ViewBag.FilterCol_Name = filterCol_name;
            ViewBag.FilterCol_Category = filterCol_category;
            ViewBag.FilterCol_Phone = filterCol_phone;
            ViewBag.FilterCol_Account = filterCol_account;
            ViewBag.FilterCol_Debit = filterCol_debit;
            ViewBag.FilterCol_Credit = filterCol_credit;
            ViewBag.FilterCol_CreditLimit = filterCol_creditlimit;
            ViewBag.FilterCol_Sales = filterCol_sales;
            ViewBag.FilterCol_Purchases = filterCol_purchases;
            ViewBag.FilterCol_Returns = filterCol_returns;
            ViewBag.FilterCol_AvailableCredit = filterCol_availablecredit;

            return View();
        }

        /// <summary>جلب القيم المميزة لعمود في تقرير أرصدة العملاء (للفلترة بنمط Excel)</summary>
        [HttpGet]
        [RequirePermission("Reports.CustomerBalances")]
        public async Task<IActionResult> GetCustomerBalancesColumnValues(
            string? search,
            string? searchMode,
            string? partyCategory,
            int? governorateId,
            string column,
            string? searchTerm = null)
        {
            // نفس منطق تقرير أرصدة العملاء (CustomerBalances) — لا فلترة يدوية مخفّفة تُسبب عكس التجميعة/البحث
            var q = _context.Customers.AsNoTracking().Where(c => c.IsActive == true);
            q = await _accountVisibilityService.ApplyCustomerVisibilityFilterAsync(q);
            q = ApplyCustomerBalancesSearchFilter(q, search, searchMode);
            if (!string.IsNullOrWhiteSpace(partyCategory))
                q = q.Where(c => c.PartyCategory == partyCategory);
            if (governorateId.HasValue && governorateId.Value > 0)
                q = q.Where(c => c.GovernorateId == governorateId.Value);

            var rawTerm = (searchTerm ?? "").Trim();
            var term = rawTerm.ToLowerInvariant();
            List<(string Value, string Display)> items = column?.ToLowerInvariant() switch
            {
                "name" => string.IsNullOrEmpty(term)
                    ? (await q.Where(c => c.CustomerName != null).Select(c => c.CustomerName!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v ?? "", v ?? "")).ToList()
                    : (await q.Where(c => c.CustomerName != null && EF.Functions.Like(c.CustomerName, "%" + term + "%")).Select(c => c.CustomerName!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v ?? "", v ?? "")).ToList(),
                "category" => (await q.Where(c => c.PartyCategory != null).Select(c => c.PartyCategory!).Distinct().OrderBy(v => v).Take(200).ToListAsync()).Select(v => (v ?? "", v ?? "")).ToList(),
                "phone" => string.IsNullOrEmpty(term)
                    ? (await q.Where(c => c.Phone1 != null).Select(c => c.Phone1!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v ?? "", v ?? "")).ToList()
                    : (await q.Where(c => c.Phone1 != null && EF.Functions.Like(c.Phone1, "%" + term + "%")).Select(c => c.Phone1!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v ?? "", v ?? "")).ToList(),
                "account" => string.IsNullOrEmpty(rawTerm)
                    ? (await q.Where(c => c.Account != null).Select(c => (c.Account!.AccountCode ?? "") + " — " + (c.Account.AccountName ?? "")).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v, v)).ToList()
                    : (await q.Where(c => c.Account != null && ((c.Account!.AccountCode != null && c.Account.AccountCode.Contains(rawTerm)) || (c.Account.AccountName != null && c.Account.AccountName.Contains(rawTerm))))
                        .Select(c => (c.Account!.AccountCode ?? "") + " — " + (c.Account.AccountName ?? "")).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v, v)).ToList(),
                _ => new List<(string, string)>()
            };
            return Json(items.Select(x => new { value = x.Value, display = x.Display }));
        }

        // =========================================================
        // تصدير Excel / CSV (وPDF عبر PdfExportMiddleware): أرصدة العملاء — نفس فلاتر CustomerBalances + الأعمدة الظاهرة
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> ExportCustomerBalances(
            string? search,
            string? searchMode,
            string? partyCategory,
            int? governorateId,
            DateTime? fromDate,
            DateTime? toDate,
            bool includeZeroBalance = false,
            string? sortBy = "name",
            string? sortDir = "asc",
            string? filterCol_code = null,
            string? filterCol_name = null,
            string? filterCol_category = null,
            string? filterCol_phone = null,
            string? filterCol_account = null,
            string? filterCol_debit = null,
            string? filterCol_credit = null,
            string? filterCol_creditlimit = null,
            string? filterCol_sales = null,
            string? filterCol_purchases = null,
            string? filterCol_returns = null,
            string? filterCol_availablecredit = null,
            string? visibleCols = null,
            string format = "excel")
        {
            // عند التصدير: إذا لم يتم تحديد includeZeroBalance، اجعله false افتراضياً
            string? includeZeroBalanceStr = Request.Query["includeZeroBalance"].FirstOrDefault();
            if (string.IsNullOrEmpty(includeZeroBalanceStr))
            {
                includeZeroBalance = false;
            }

            var reportData = await BuildCustomerBalancesReportDataForExportAsync(
                search, searchMode, partyCategory, governorateId, fromDate, toDate, includeZeroBalance,
                sortBy, sortDir,
                filterCol_code, filterCol_name, filterCol_category, filterCol_phone, filterCol_account,
                filterCol_debit, filterCol_credit, filterCol_creditlimit, filterCol_sales, filterCol_purchases, filterCol_returns, filterCol_availablecredit);
            if (reportData == null || reportData.Count == 0)
            {
                return BadRequest("لا توجد بيانات للتصدير");
            }

            var requested = (visibleCols ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrEmpty(x))
                .ToList();

            var requestedSet = requested.Count > 0
                ? new HashSet<string>(requested, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(CustomerBalancesPrintColumnOrder, StringComparer.OrdinalIgnoreCase);

            var orderedCols = CustomerBalancesPrintColumnOrder.Where(requestedSet.Contains).ToList();
            if (orderedCols.Count == 0)
                orderedCols = CustomerBalancesPrintColumnOrder.ToList();

            static decimal DebitOf(CustomerBalanceReportDto item) => item.CurrentBalance > 0 ? item.CurrentBalance : 0m;
            static decimal CreditOf(CustomerBalanceReportDto item) => item.CurrentBalance < 0 ? Math.Abs(item.CurrentBalance) : 0m;

            string ColTitle(string k) => k.ToLowerInvariant() switch
            {
                "code" => "الكود",
                "externalcode" => "كود الاكسل",
                "name" => "اسم العميل",
                "account" => "حساب العميل",
                "category" => "فئة العميل",
                "phone" => "الهاتف",
                "debit" => "مدين",
                "credit" => "دائن",
                "creditlimit" => "الحد الائتماني",
                "sales" => "المبيعات",
                "purchases" => "المشتريات",
                "returns" => "المرتجعات",
                "availablecredit" => "الائتمان المتاح",
                _ => k
            };

            void WriteExcelCell(ClosedXML.Excel.IXLWorksheet ws, int r, int c, CustomerBalanceReportDto item, string k)
            {
                switch (k.ToLowerInvariant())
                {
                    case "code":
                        ws.Cell(r, c).Value = item.CustomerCode;
                        break;
                    case "externalcode":
                        ws.Cell(r, c).Value = item.ExternalCode ?? "";
                        break;
                    case "name":
                        ws.Cell(r, c).Value = item.CustomerName ?? "";
                        break;
                    case "account":
                        ws.Cell(r, c).Value = string.IsNullOrEmpty(item.AccountDisplay) ? "—" : item.AccountDisplay;
                        break;
                    case "category":
                        ws.Cell(r, c).Value = string.IsNullOrWhiteSpace(item.PartyCategory)
                            ? "—"
                            : PartyCategoryDisplay.ToArabic(item.PartyCategory);
                        break;
                    case "phone":
                        ws.Cell(r, c).Value = item.Phone1 ?? "";
                        break;
                    case "debit":
                        ws.Cell(r, c).Value = DebitOf(item);
                        break;
                    case "credit":
                        ws.Cell(r, c).Value = CreditOf(item);
                        break;
                    case "creditlimit":
                        ws.Cell(r, c).Value = item.CreditLimit;
                        break;
                    case "sales":
                        ws.Cell(r, c).Value = item.TotalSales;
                        break;
                    case "purchases":
                        ws.Cell(r, c).Value = item.TotalPurchases;
                        break;
                    case "returns":
                        ws.Cell(r, c).Value = item.TotalReturns;
                        break;
                    case "availablecredit":
                        ws.Cell(r, c).Value = item.AvailableCredit;
                        break;
                    default:
                        ws.Cell(r, c).Value = "";
                        break;
                }
            }

            string CsvCellText(CustomerBalanceReportDto item, string k)
            {
                switch (k.ToLowerInvariant())
                {
                    case "code":
                        return item.CustomerCode ?? "";
                    case "externalcode":
                        return item.ExternalCode ?? "";
                    case "name":
                        return item.CustomerName ?? "";
                    case "account":
                        return string.IsNullOrEmpty(item.AccountDisplay) ? "—" : item.AccountDisplay;
                    case "category":
                        return string.IsNullOrWhiteSpace(item.PartyCategory)
                            ? "—"
                            : PartyCategoryDisplay.ToArabic(item.PartyCategory);
                    case "phone":
                        return item.Phone1 ?? "";
                    case "debit":
                        return DebitOf(item).ToString("0.00", CultureInfo.InvariantCulture);
                    case "credit":
                        return CreditOf(item).ToString("0.00", CultureInfo.InvariantCulture);
                    case "creditlimit":
                        return item.CreditLimit.ToString("0.00", CultureInfo.InvariantCulture);
                    case "sales":
                        return item.TotalSales.ToString("0.00", CultureInfo.InvariantCulture);
                    case "purchases":
                        return item.TotalPurchases.ToString("0.00", CultureInfo.InvariantCulture);
                    case "returns":
                        return item.TotalReturns.ToString("0.00", CultureInfo.InvariantCulture);
                    case "availablecredit":
                        return item.AvailableCredit.ToString("0.00", CultureInfo.InvariantCulture);
                    default:
                        return "";
                }
            }

            static string CsvEscape(string? value)
            {
                if (string.IsNullOrEmpty(value)) return "\"\"";
                value = value.Replace("\"", "\"\"");
                return $"\"{value}\"";
            }

            if (string.Equals(format, "excel", StringComparison.OrdinalIgnoreCase))
            {
                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("أرصدة العملاء"));
                int row = 1;
                for (int i = 0; i < orderedCols.Count; i++)
                    worksheet.Cell(row, i + 1).Value = ColTitle(orderedCols[i]);
                worksheet.Range(row, 1, row, orderedCols.Count).Style.Font.Bold = true;

                foreach (var item in reportData)
                {
                    row++;
                    for (int i = 0; i < orderedCols.Count; i++)
                        WriteExcelCell(worksheet, row, i + 1, item, orderedCols[i]);
                }

                worksheet.Columns().AdjustToContents();
                using var stream = new System.IO.MemoryStream();
                workbook.SaveAs(stream);
                var fileName = ExcelExportNaming.ArabicTimestampedFileName("أرصدة العملاء", ".xlsx");
                return File(stream.ToArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileName);
            }

            // CSV — يُحوَّل إلى PDF عند format=pdf عبر PdfExportMiddleware
            var csvBuilder = new StringBuilder();
            csvBuilder.AppendLine(string.Join(",", orderedCols.Select(k => CsvEscape(ColTitle(k)))));
            foreach (var item in reportData)
            {
                csvBuilder.AppendLine(string.Join(",",
                    orderedCols.Select(k => CsvEscape(CsvCellText(item, k)))));
            }

            var csvBytes = Encoding.UTF8.GetPreamble()
                .Concat(Encoding.UTF8.GetBytes(csvBuilder.ToString()))
                .ToArray();
            var csvName = ExcelExportNaming.ArabicTimestampedFileName("أرصدة العملاء", ".csv");
            return File(csvBytes, "text/csv; charset=utf-8", csvName);
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
            int? purchaseInvoiceFrom,
            int? purchaseInvoiceTo,
            bool includeZeroQty = false,
            bool includeBatches = true,
            string? sortBy = "name",
            string? sortDir = "asc",
            string? searchMode = "contains",
            string? visibleCols = null,
            string format = "excel")
        {
            var pbQtyGtZeroExp = Request.Query["pbQtyGtZero"].FirstOrDefault();
            if (pbQtyGtZeroExp != null)
                includeZeroQty = !string.Equals(pbQtyGtZeroExp, "true", StringComparison.OrdinalIgnoreCase);
            else
            {
                var legacyInc = Request.Query["includeZeroQty"].FirstOrDefault();
                if (!string.IsNullOrEmpty(legacyInc))
                    includeZeroQty = string.Equals(legacyInc, "true", StringComparison.OrdinalIgnoreCase);
                else
                    includeZeroQty = false;
            }

            // بناء الاستعلام (نفس منطق ProductBalances)
            var productsQuery = _context.Products
                .AsNoTracking()
                .Include(p => p.Category)
                .Include(p => p.ProductGroup)
                .Include(p => p.ProductBonusGroup)
                .AsQueryable();

            productsQuery = ApplyProductBalancesSearchFilter(productsQuery, search, searchMode);

            if (hasBonus == true)
            {
                productsQuery = productsQuery.Where(p => p.ProductBonusGroupId != null);
            }

            productsQuery = productsQuery.Where(p => p.IsActive == true);

            var productIds = await productsQuery.Select(p => p.ProdId).ToListAsync();

            productIds = await ApplyProductBalancesPurchaseMovementFilterAsync(
                productIds, fromDate, toDate, purchaseInvoiceFrom, purchaseInvoiceTo);

            // عند اختيار مخزن: نقتصر على أصناف لها رصيد > 0 أو (عرض الصفر) معيّنة لهذا المخزن (نفس منطق ProductBalances)
            if (warehouseId.HasValue && warehouseId.Value > 0)
            {
                var whIdExp = warehouseId.Value;
                var balanceInWarehouseExp = await _context.StockLedger
                    .AsNoTracking()
                    .Where(sl => sl.WarehouseId == whIdExp && productIds.Contains(sl.ProdId))
                    .GroupBy(sl => sl.ProdId)
                    .Select(g => new { ProdId = g.Key, Balance = g.Sum(sl => sl.QtyIn - sl.QtyOut) })
                    .ToListAsync();
                var withPositiveBalanceExp = balanceInWarehouseExp.Where(b => b.Balance > 0).Select(b => b.ProdId).ToList();
                var inWarehouseIdsExp = new List<int>(withPositiveBalanceExp);
                if (includeZeroQty)
                {
                    var assignedToWarehouseIdsExp = await _context.Products
                        .AsNoTracking()
                        .Where(p => p.WarehouseId == whIdExp && p.IsActive)
                        .Select(p => p.ProdId)
                        .ToListAsync();
                    inWarehouseIdsExp = inWarehouseIdsExp.Union(assignedToWarehouseIdsExp).Distinct().ToList();
                }
                productIds = productIds.Intersect(inWarehouseIdsExp).ToList();
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
                    p.PriceRetail,
                    p.Company,
                    p.Imported,
                    p.Description
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
                    (x.SourceType == "Purchase" || x.SourceType == "Opening" || x.SourceType == "TransferIn" || x.SourceType == "SyncToProductWarehouse") &&
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
                .Where(x => productIds.Contains(x.ProdId) && (x.SourceType == "Purchase" || x.SourceType == "Opening" || x.SourceType == "TransferIn" || x.SourceType == "SyncToProductWarehouse") && (x.RemainingQty ?? 0) > 0);
            if (warehouseId.HasValue && warehouseId.Value > 0)
                batchLedgerQueryExp = batchLedgerQueryExp.Where(x => x.WarehouseId == warehouseId.Value);
            var batchRowsRawExp = await batchLedgerQueryExp
                .GroupBy(x => new { x.ProdId, x.BatchNo, x.Expiry })
                .Select(g => new { g.Key.ProdId, g.Key.BatchNo, g.Key.Expiry, TotalRemaining = g.Sum(x => x.RemainingQty ?? 0), WeightedDiscount = g.Sum(x => (decimal)(x.RemainingQty ?? 0) * ((decimal?)(x.PurchaseDiscount) ?? 0m)), WeightedCost = g.Sum(x => (decimal)(x.RemainingQty ?? 0) * x.UnitCost) })
                .ToListAsync();
            var batchesByProdIdExp = productIds.Distinct().ToDictionary(pid => pid, pid => batchRowsRawExp.Where(b => b.ProdId == pid).ToList());
            var batchMasterListExp = await _context.Batches.AsNoTracking().Where(b => productIds.Contains(b.ProdId)).Select(b => new { b.BatchId, b.ProdId, b.BatchNo, b.Expiry, b.PriceRetailBatch }).ToListAsync();
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

            var salesLinesByBatchExp = await salesQueryExp
                .Select(sil => new { sil.ProdId, BatchNo = (sil.BatchNo ?? "").Trim(), sil.Expiry, Qty = (decimal)sil.Qty })
                .ToListAsync();
            var salesByBatchKeyExp = salesLinesByBatchExp
                .GroupBy(x => new { x.ProdId, x.BatchNo, ExpiryDate = x.Expiry.HasValue ? x.Expiry!.Value.Date : (DateTime?)null })
                .ToDictionary(g => (g.Key.ProdId, g.Key.BatchNo, g.Key.ExpiryDate), g => g.Sum(x => x.Qty));

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

            var returnsLinesByBatchExp = await returnsQueryExp
                .Select(srl => new { srl.ProdId, BatchNo = (srl.BatchNo ?? "").Trim(), srl.Expiry, Qty = (decimal)srl.Qty })
                .ToListAsync();
            var returnsByBatchKeyExp = returnsLinesByBatchExp
                .GroupBy(x => new { x.ProdId, x.BatchNo, ExpiryDate = x.Expiry.HasValue ? x.Expiry!.Value.Date : (DateTime?)null })
                .ToDictionary(g => (g.Key.ProdId, g.Key.BatchNo, g.Key.ExpiryDate), g => g.Sum(x => x.Qty));

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
                    Company = product.Company,
                    Imported = product.Imported,
                    Description = product.Description,
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
                        var batchKeyExp = (ProdId: prodId, BatchNo: (m.BatchNo ?? "").Trim(), ExpiryDate: (DateTime?)m.Expiry.Date);
                        decimal batchSalesQtyExp = salesByBatchKeyExp.TryGetValue(batchKeyExp, out var bsExp) ? bsExp : 0m;
                        if (returnsByBatchKeyExp.TryGetValue(batchKeyExp, out var brExp))
                            batchSalesQtyExp = Math.Max(0m, batchSalesQtyExp - brExp);
                        decimal batchPriceRetailExp = (m.PriceRetailBatch ?? 0m) > 0m ? (m.PriceRetailBatch ?? 0m) : product.PriceRetail;
                        batchRowsExp.Add(new ProductBalanceBatchRow { BatchNo = m.BatchNo, Expiry = m.Expiry, CurrentQty = (int)brQty, WeightedDiscount = brDisc, ManualDiscountPct = manualBatchExp, EffectiveDiscountPct = effectiveBatchExp, UnitCost = brCost, TotalCost = brQty * brCost, SalesQty = batchSalesQtyExp, PriceRetail = batchPriceRetailExp });
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

            var whForPolicyRulesExp = warehouseId is int whExp && whExp > 0 ? whExp : 0;
            var ctxPolExp = await ProductBalancePolicySaleHelper.LoadWarehousePolicyContextAsync(_context, whForPolicyRulesExp);
            ProductBalancePolicySaleHelper.ApplyToReport(reportData, ctxPolExp.Rules, ctxPolExp.DefaultProfit);

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

            var exportKind = Request.Query["exportKind"].LastOrDefault();
            var delegatePolicyStr = Request.Query["delegatePolicyId"].LastOrDefault();
            if (string.Equals(exportKind, "delegate", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(delegatePolicyStr, out var delPol) && delPol >= 1 && delPol <= 10)
            {
                var rowsDel = reportData
                    .Select(r => (
                        Name: r.ProdName ?? "",
                        Price: r.PriceRetail,
                        Disc: r.PolicySaleDiscountPct[delPol - 1] ?? 0m))
                    .ToList();
                var polNamesDel = await GetProductBalancePolicyNamesFromDbAsync();
                var polName = polNamesDel.TryGetValue(delPol, out var pnDel) ? pnDel : ("سياسة " + delPol);
                if (string.Equals(format, "excel", StringComparison.OrdinalIgnoreCase))
                {
                    var bytesDel = ProductBalancesDelegateListExportHelper.BuildExcel(rowsDel, polName);
                    var fn = ExcelExportNaming.ArabicTimestampedFileName("قائمة أصناف صيدلية", ".xlsx");
                    return File(bytesDel, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fn);
                }
                var csvDel = ProductBalancesDelegateListExportHelper.BuildCsv(rowsDel, polName);
                var csvNm = ExcelExportNaming.ArabicTimestampedFileName("قائمة أصناف صيدلية", ".csv");
                return File(csvDel, "text/csv; charset=utf-8", csvNm);
            }

            var exportKeys = ProductBalancesExportHelper.ResolveExportColumnKeys(visibleCols);

            if (string.Equals(format, "excel", StringComparison.OrdinalIgnoreCase))
            {
                var xlsxBytes = ProductBalancesExportHelper.BuildExcelBytes(reportData, exportKeys);
                var fileName = ExcelExportNaming.ArabicTimestampedFileName("أرصدة الأصناف", ".xlsx");
                return File(xlsxBytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileName);
            }

            // CSV — يُحوَّل إلى PDF عند format=pdf عبر PdfExportMiddleware
            var csvBytesPb = ProductBalancesExportHelper.BuildCsvBytes(reportData, exportKeys);
            var csvNamePb = ExcelExportNaming.ArabicTimestampedFileName("أرصدة الأصناف", ".csv");
            return File(csvBytesPb, "text/csv; charset=utf-8", csvNamePb);
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
            string? sort = "name",
            string? dir = "asc",
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
            string? filterCol_salesqtyExpr = null,
            bool loadReport = false,
            int page = 1,
            int pageSize = 10,
            string? format = null)  // "excel" | "csv" للتصدير
        {
            var pageSizeQuery = Request.Query["pageSize"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageSizeQuery) && int.TryParse(pageSizeQuery, out var psVal))
                pageSize = psVal;
            if (pageSize < 0)
                pageSize = 10;
            if (pageSize > 0 && pageSize != 10 && pageSize != 25 && pageSize != 50 && pageSize != 100 && pageSize != 200)
                pageSize = 10;

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
            ViewBag.Sort = sort;
            ViewBag.Dir = dir;
            ViewBag.FilterCol_Code = filterCol_code;
            ViewBag.FilterCol_Name = filterCol_name;
            ViewBag.FilterCol_Category = filterCol_category;
            ViewBag.FilterCol_SalesrevenueExpr = filterCol_salesrevenueExpr;
            ViewBag.FilterCol_SalescostExpr = filterCol_salescostExpr;
            ViewBag.FilterCol_SalesprofitExpr = filterCol_salesprofitExpr;
            ViewBag.FilterCol_SalesprofitpctExpr = filterCol_salesprofitpctExpr;
            ViewBag.FilterCol_ReturnprofitExpr = filterCol_returnprofitExpr;
            ViewBag.FilterCol_AdjustmentprofitExpr = filterCol_adjustmentprofitExpr;
            ViewBag.FilterCol_TransferprofitExpr = filterCol_transferprofitExpr;
            ViewBag.FilterCol_NetprofitExpr = filterCol_netprofitExpr;
            ViewBag.FilterCol_SalesqtyExpr = filterCol_salesqtyExpr;

            // =========================================================
            // 3) تحميل البيانات عند "تجميع التقرير" أو طلب التصدير
            // =========================================================
            bool wantExport = !string.IsNullOrWhiteSpace(format) && (format.Equals("excel", StringComparison.OrdinalIgnoreCase) || format.Equals("csv", StringComparison.OrdinalIgnoreCase));
            if (!loadReport && !wantExport)
            {
                ViewBag.ReportData = new List<ProductProfitReportDto>();
                ViewBag.TotalSalesRevenue = 0m;
                ViewBag.TotalSalesCost = 0m;
                ViewBag.TotalSalesProfit = 0m;
                ViewBag.BalanceSheetData = null;
                return View();
            }
            if (wantExport) loadReport = true;

            // =========================================================
            // 3.1) حساب ربح الميزانية مرة واحدة (لا يعتمد على قائمة الأصناف) — لضمان ظهور تكلفة المخزون حتى مع فلتر تاريخ يفرغ القائمة
            // عملاء المدين/الدائن + تكلفة المخزون: دوال مشتركة مع تقرير أرصدة العملاء / أرصدة الأصناف (مصدر واحد).
            // =========================================================
            var eligibleSetBs = await GetEligibleActiveCustomerIdsForBalanceSheetAsync();
            var (customersDebitSum, customersCreditSum) = await ComputeBalanceSheetCustomerDebitCreditFromCustomerIdsAsync(eligibleSetBs, toDate);
            decimal treasuryBalance = 0m;
            decimal inventoryCostTotal = await ComputeBalanceSheetInventoryCostAsync(warehouseId);

            var cashAccountIdsBs = await _context.Accounts
                .AsNoTracking()
                .Where(a => a.AccountType == AccountType.Asset &&
                    (a.AccountName.Contains("خزينة") || a.AccountName.Contains("بنك") ||
                     a.AccountName.Contains("صندوق") || a.AccountCode.StartsWith("1101") || a.AccountCode.StartsWith("1102")))
                .Select(a => a.AccountId)
                .ToListAsync();
            if (cashAccountIdsBs.Any())
            {
                var treasuryQueryBs = _context.LedgerEntries
                    .AsNoTracking()
                    .Where(e => cashAccountIdsBs.Contains(e.AccountId) && e.PostVersion > 0);
                if (toDate.HasValue)
                    treasuryQueryBs = treasuryQueryBs.Where(e => e.EntryDate < toDate.Value.Date.AddDays(1));
                treasuryBalance = await treasuryQueryBs.SumAsync(e => (decimal?)(e.Debit - e.Credit)) ?? 0m;
            }

            decimal balanceSheetProfit = customersDebitSum + treasuryBalance + inventoryCostTotal - customersCreditSum;

            // =========================================================
            // 4)–8) بناء صفوف التقرير (فلترة + ترتيب) — منطق موحّد مع GetProductProfitsColumnValues
            // =========================================================
            var reportData = await BuildProductProfitsReportRowsAsync(
                search,
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
                omitTextColumnFilter: null);

            if (reportData.Count == 0)
            {
                ViewBag.ReportData = new List<ProductProfitReportDto>();
                ViewBag.BalanceSheetData = new
                {
                    CustomersDebitSum = customersDebitSum,
                    CustomersCreditSum = customersCreditSum,
                    TreasuryBalance = treasuryBalance,
                    InventoryCostTotal = inventoryCostTotal,
                    BalanceSheetProfit = balanceSheetProfit
                };
                ViewBag.TotalSalesQty = 0m;
                return View();
            }

            // =========================================================
            // 9) حساب المجاميع
            // =========================================================
            decimal totalSalesRevenue = reportData.Sum(r => r.SalesRevenue);
            decimal totalSalesCost = reportData.Sum(r => r.SalesCost);
            decimal totalSalesProfit = totalSalesRevenue - totalSalesCost;
            decimal totalReturnProfit = reportData.Sum(r => r.ReturnProfit);
            decimal totalAdjustmentProfit = reportData.Sum(r => r.AdjustmentProfit);
            decimal totalTransferProfit = reportData.Sum(r => r.TransferProfit);
            decimal totalNetProfit = reportData.Sum(r => r.NetProfit);
            decimal totalSalesQtyAll = reportData.Sum(r => r.SalesQty);

            int totalCount = reportData.Count;

            // =========================================================
            // 9.5) تصدير Excel (.xlsx) أو CSV (بدون باجيناشن)
            // =========================================================
            if (!string.IsNullOrWhiteSpace(format) && (format.Equals("excel", StringComparison.OrdinalIgnoreCase) || format.Equals("csv", StringComparison.OrdinalIgnoreCase)))
            {
                if (format.Equals("excel", StringComparison.OrdinalIgnoreCase))
                {
                    using var workbook = new XLWorkbook();
                    var worksheet = workbook.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("أرباح الأصناف"));
                    int row = 1;
                    worksheet.Cell(row, 1).Value = "الكود";
                    worksheet.Cell(row, 2).Value = "اسم الصنف";
                    worksheet.Cell(row, 3).Value = "الفئة";
                    worksheet.Cell(row, 4).Value = "البيع";
                    worksheet.Cell(row, 5).Value = "التكلفة";
                    worksheet.Cell(row, 6).Value = "الربح (بيع)";
                    worksheet.Cell(row, 7).Value = "نسبة الربح %";
                    worksheet.Cell(row, 8).Value = "ربح المرتجعات";
                    worksheet.Cell(row, 9).Value = "الربح (تسويات)";
                    worksheet.Cell(row, 10).Value = "الربح (تحويلات)";
                    worksheet.Cell(row, 11).Value = "الكمية";
                    worksheet.Cell(row, 12).Value = "صافي الربح";
                    row++;
                    foreach (var r in reportData)
                    {
                        worksheet.Cell(row, 1).Value = r.ProdCode ?? "";
                        worksheet.Cell(row, 2).Value = r.ProdName ?? "";
                        worksheet.Cell(row, 3).Value = r.CategoryName ?? "";
                        worksheet.Cell(row, 4).Value = r.SalesRevenue;
                        worksheet.Cell(row, 5).Value = r.SalesCost;
                        worksheet.Cell(row, 6).Value = r.SalesProfit;
                        worksheet.Cell(row, 7).Value = r.SalesProfitPercent;
                        worksheet.Cell(row, 8).Value = r.ReturnProfit;
                        worksheet.Cell(row, 9).Value = r.AdjustmentProfit;
                        worksheet.Cell(row, 10).Value = r.TransferProfit;
                        worksheet.Cell(row, 11).Value = r.SalesQty;
                        worksheet.Cell(row, 12).Value = r.NetProfit;
                        row++;
                    }
                    worksheet.Columns().AdjustToContents();
                    using var stream = new System.IO.MemoryStream();
                    workbook.SaveAs(stream);
                    var fileName = ExcelExportNaming.ArabicTimestampedFileName("أرباح الأصناف", ".xlsx");
                    return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                }
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("الكود,اسم الصنف,الفئة,البيع,التكلفة,الربح (بيع),نسبة الربح %,ربح المرتجعات,الربح (تسويات),الربح (تحويلات),الكمية,صافي الربح");
                foreach (var r in reportData)
                {
                    sb.AppendLine(string.Join(",",
                        (r.ProdCode ?? "").Replace(",", " "),
                        (r.ProdName ?? "").Replace(",", " "),
                        (r.CategoryName ?? "").Replace(",", " "),
                        r.SalesRevenue.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                        r.SalesCost.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                        r.SalesProfit.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                        r.SalesProfitPercent.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                        r.ReturnProfit.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                        r.AdjustmentProfit.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                        r.TransferProfit.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                        r.SalesQty.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                        r.NetProfit.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)));
                }
                var csvBytes = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString());
                return File(csvBytes, "text/csv; charset=utf-8", ExcelExportNaming.ArabicTimestampedFileName("أرباح الأصناف", ".csv"));
            }

            // =========================================================
            // 10) Pagination (نمط القوائم الموحد: 10/25/…/الكل)
            // =========================================================
            if (pageSize == 0)
            {
                int effectivePageSize = totalCount == 0 ? 10 : Math.Min(totalCount, 100_000);
                page = 1;
                reportData = reportData.Take(effectivePageSize).ToList();
                ViewBag.Page = 1;
                ViewBag.PageSize = 0;
                ViewBag.TotalPages = 1;
                ViewBag.TotalCount = totalCount;
            }
            else if (totalCount > pageSize)
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
                ViewBag.PageSize = pageSize;
                ViewBag.TotalPages = 1;
                ViewBag.TotalCount = totalCount;
            }

            ViewBag.ReportData = reportData;
            ViewBag.TotalSalesRevenue = totalSalesRevenue;
            ViewBag.TotalSalesCost = totalSalesCost;
            ViewBag.TotalSalesProfit = totalSalesProfit;
            ViewBag.TotalReturnProfit = totalReturnProfit;
            ViewBag.TotalAdjustmentProfit = totalAdjustmentProfit;
            ViewBag.TotalTransferProfit = totalTransferProfit;
            ViewBag.TotalNetProfit = totalNetProfit;
            ViewBag.TotalSalesQty = totalSalesQtyAll;

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
            string? profitMethod = "both",
            string? sort = "name",
            string? dir = "asc",
            string? filterCol_code = null,
            string? filterCol_name = null,
            string? filterCol_category = null,
            string? filterCol_phone = null,
            string? filterCol_salesrevenueExpr = null,
            string? filterCol_salescostExpr = null,
            string? filterCol_salesprofitExpr = null,
            string? filterCol_salesprofitpctExpr = null,
            string? filterCol_returnprofitExpr = null,
            string? filterCol_netprofitExpr = null,
            bool loadReport = false,
            int page = 1,
            int pageSize = 10,
            string? format = null)
        {
            var pageSizeQuery = Request.Query["pageSize"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageSizeQuery) && int.TryParse(pageSizeQuery, out var psVal))
                pageSize = psVal;
            if (pageSize < 0)
                pageSize = 10;
            if (pageSize > 0 && pageSize != 10 && pageSize != 25 && pageSize != 50 && pageSize != 100 && pageSize != 200)
                pageSize = 10;

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
            ViewBag.Sort = sort;
            ViewBag.Dir = dir;
            ViewBag.FilterCol_Code = filterCol_code;
            ViewBag.FilterCol_Name = filterCol_name;
            ViewBag.FilterCol_Category = filterCol_category;
            ViewBag.FilterCol_Phone = filterCol_phone;
            ViewBag.FilterCol_SalesrevenueExpr = filterCol_salesrevenueExpr;
            ViewBag.FilterCol_SalescostExpr = filterCol_salescostExpr;
            ViewBag.FilterCol_SalesprofitExpr = filterCol_salesprofitExpr;
            ViewBag.FilterCol_SalesprofitpctExpr = filterCol_salesprofitpctExpr;
            ViewBag.FilterCol_ReturnprofitExpr = filterCol_returnprofitExpr;
            ViewBag.FilterCol_NetprofitExpr = filterCol_netprofitExpr;

            // =========================================================
            // 3) تحميل البيانات عند "تجميع التقرير" أو طلب التصدير
            // =========================================================
            bool wantExport = !string.IsNullOrWhiteSpace(format) && (format.Equals("excel", StringComparison.OrdinalIgnoreCase) || format.Equals("csv", StringComparison.OrdinalIgnoreCase));
            if (!loadReport && !wantExport)
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
            if (wantExport) loadReport = true;

            var reportData = await BuildCustomerProfitsReportRowsAsync(search, partyCategory, governorateId, fromDate, toDate, includeZeroQty);
            if (reportData.Count == 0)
            {
                ViewBag.ReportData = new List<CustomerProfitReportDto>();
                ViewBag.TotalSalesRevenue = 0m;
                ViewBag.TotalSalesCost = 0m;
                ViewBag.TotalSalesProfit = 0m;
                ViewBag.TotalLedgerRevenue = 0m;
                ViewBag.TotalLedgerCost = 0m;
                ViewBag.TotalLedgerProfit = 0m;
                ViewBag.BalanceSheetData = null;
                return View();
            }

            reportData = ApplyCustomerProfitsColumnFilters(reportData, filterCol_code, filterCol_name, filterCol_category, filterCol_phone, filterCol_salesrevenueExpr, filterCol_salescostExpr, filterCol_salesprofitExpr, filterCol_salesprofitpctExpr, filterCol_returnprofitExpr, filterCol_netprofitExpr, omitTextColumnFilter: null);
            reportData = SortCustomerProfitsReportList(reportData, sort, dir);

            // 9) حساب المجاميع
            // =========================================================
            decimal totalSalesRevenue = reportData.Sum(r => r.SalesRevenue);
            decimal totalSalesCost = reportData.Sum(r => r.SalesCost);
            decimal totalSalesProfit = totalSalesRevenue - totalSalesCost;
            decimal totalReturnProfit = reportData.Sum(r => r.ReturnProfit);
            decimal totalNetProfit = reportData.Sum(r => r.NetProfit);
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
            decimal totalAdjustedProfit = totalNetProfit + totalNetNotesAdjustment;

            int totalCount = reportData.Count;

            // =========================================================
            // 9.5) تصدير Excel (.xlsx) أو CSV (بدون باجيناشن، بدون عدد الفواتير/متوسط الفاتورة)
            // =========================================================
            if (wantExport)
            {
                if (format!.Equals("excel", StringComparison.OrdinalIgnoreCase))
                {
                    using var workbook = new XLWorkbook();
                    var worksheet = workbook.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("أرباح العملاء"));
                    int row = 1;
                    worksheet.Cell(row, 1).Value = "الكود";
                    worksheet.Cell(row, 2).Value = "اسم العميل";
                    worksheet.Cell(row, 3).Value = "فئة العميل";
                    worksheet.Cell(row, 4).Value = "الهاتف";
                    worksheet.Cell(row, 5).Value = "الإيرادات";
                    worksheet.Cell(row, 6).Value = "التكلفة";
                    worksheet.Cell(row, 7).Value = "الربح";
                    worksheet.Cell(row, 8).Value = "نسبة الربح %";
                    worksheet.Cell(row, 9).Value = "ربح المرتجعات";
                    worksheet.Cell(row, 10).Value = "صافي الربح";
                    row++;
                    foreach (var r in reportData)
                    {
                        worksheet.Cell(row, 1).Value = r.CustomerCode ?? "";
                        worksheet.Cell(row, 2).Value = r.CustomerName ?? "";
                        worksheet.Cell(row, 3).Value = r.PartyCategory ?? "";
                        worksheet.Cell(row, 4).Value = r.Phone1 ?? "";
                        worksheet.Cell(row, 5).Value = r.SalesRevenue;
                        worksheet.Cell(row, 6).Value = r.SalesCost;
                        worksheet.Cell(row, 7).Value = r.SalesProfit;
                        worksheet.Cell(row, 8).Value = r.SalesProfitPercent;
                        worksheet.Cell(row, 9).Value = r.ReturnProfit;
                        worksheet.Cell(row, 10).Value = r.NetProfit;
                        row++;
                    }
                    worksheet.Columns().AdjustToContents();
                    using var stream = new System.IO.MemoryStream();
                    workbook.SaveAs(stream);
                    var fileName = ExcelExportNaming.ArabicTimestampedFileName("أرباح العملاء", ".xlsx");
                    return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                }
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("الكود,اسم العميل,فئة العميل,الهاتف,الإيرادات,التكلفة,الربح,نسبة الربح %,ربح المرتجعات,صافي الربح");
                foreach (var r in reportData)
                {
                    sb.AppendLine(string.Join(",",
                        (r.CustomerCode ?? "").Replace(",", " "),
                        (r.CustomerName ?? "").Replace(",", " "),
                        (r.PartyCategory ?? "").Replace(",", " "),
                        (r.Phone1 ?? "").Replace(",", " "),
                        r.SalesRevenue.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                        r.SalesCost.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                        r.SalesProfit.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                        r.SalesProfitPercent.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                        r.ReturnProfit.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                        r.NetProfit.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)));
                }
                var csvBytes = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString());
                return File(csvBytes, "text/csv; charset=utf-8", ExcelExportNaming.ArabicTimestampedFileName("أرباح العملاء", ".csv"));
            }

            // =========================================================
            // 10) Pagination (نمط القوائم الموحد: 10/25/…/الكل)
            // =========================================================
            if (pageSize == 0)
            {
                int effectivePageSize = totalCount == 0 ? 10 : Math.Min(totalCount, 100_000);
                page = 1;
                reportData = reportData.Take(effectivePageSize).ToList();
                ViewBag.Page = 1;
                ViewBag.PageSize = 0;
                ViewBag.TotalPages = 1;
                ViewBag.TotalCount = totalCount;
            }
            else if (totalCount > pageSize)
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
                ViewBag.PageSize = pageSize;
                ViewBag.TotalPages = 1;
                ViewBag.TotalCount = totalCount;
            }

            ViewBag.ReportData = reportData;
            ViewBag.TotalSalesRevenue = totalSalesRevenue;
            ViewBag.TotalSalesCost = totalSalesCost;
            ViewBag.TotalSalesProfit = totalSalesProfit;
            ViewBag.TotalReturnProfit = totalReturnProfit;
            ViewBag.TotalNetProfit = totalNetProfit;
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

        /// <summary>
        /// تقرير أداء المبيعات: KPIs + رسم بياني (خطي لعنصر واحد، أعمدة لأكثر من عنصر) مع فلاتر متعددة الاختيار.
        /// </summary>
        [HttpGet]
        [RequirePermission("Reports.SalesPerformanceReport")]
        public async Task<IActionResult> SalesPerformanceReport(
            DateTime? fromDate,
            DateTime? toDate,
            int[]? governorateIds,
            int[]? districtIds,
            int[]? areaIds,
            int[]? accountTypes,
            int[]? customerIds,
            int[]? userIds,
            int[]? productIds)
        {
            var vm = new SalesPerformanceReportViewModel();
            var today = DateTime.Today;
            vm.FromDate = fromDate ?? new DateTime(today.Year, today.Month, 1);
            vm.ToDate = toDate ?? today;
            var selGov = governorateIds?.Where(id => id > 0).Distinct().ToList() ?? new List<int>();
            var selDist = districtIds?.Where(id => id > 0).Distinct().ToList() ?? new List<int>();
            var selArea = areaIds?.Where(id => id > 0).Distinct().ToList() ?? new List<int>();
            var selAcct = accountTypes?.Where(t => t >= 1 && t <= 5).Distinct().ToList() ?? new List<int>();
            var selCust = customerIds?.Where(id => id > 0).Distinct().ToList() ?? new List<int>();
            var selUsers = userIds?.Where(id => id > 0).Distinct().ToList() ?? new List<int>();
            var selProds = productIds?.Where(id => id > 0).Distinct().ToList() ?? new List<int>();
            vm.SelectedGovernorateIds = selGov;
            vm.SelectedDistrictIds = selDist;
            vm.SelectedAreaIds = selArea;
            vm.SelectedAccountTypes = selAcct;
            vm.SelectedCustomerIds = selCust;
            vm.SelectedUserIds = selUsers;
            vm.SelectedProductIds = selProds;

            var from = vm.FromDate.Value.Date;
            var to = vm.ToDate.Value.Date.AddDays(1);

            var customerIdsList = new List<int>();
            var custQ = _context.Customers.AsNoTracking().Where(c => c.IsActive);
            if (selGov.Any())
                custQ = custQ.Where(c => selGov.Contains(c.GovernorateId ?? 0));
            if (selDist.Any())
                custQ = custQ.Where(c => c.DistrictId.HasValue && selDist.Contains(c.DistrictId.Value));
            if (selArea.Any())
                custQ = custQ.Where(c => c.AreaId.HasValue && selArea.Contains(c.AreaId.Value));
            if (selAcct.Any())
                custQ = custQ.Where(c => c.AccountId != null && _context.Accounts.Any(a => a.AccountId == c.AccountId && selAcct.Contains((int)a.AccountType)));
            if (selCust.Any())
                custQ = custQ.Where(c => selCust.Contains(c.CustomerId));
            customerIdsList = await custQ.Select(c => c.CustomerId).ToListAsync();
            if (!customerIdsList.Any())
                customerIdsList = await _context.Customers.Where(c => c.IsActive).Select(c => c.CustomerId).ToListAsync();

            var creatorNames = new List<string>();
            if (selUsers.Any())
            {
                var names = await _context.Users.AsNoTracking()
                    .Where(uu => selUsers.Contains(uu.UserId))
                    .Select(uu => uu.DisplayName ?? uu.UserName ?? "")
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToListAsync();
                creatorNames = names;
            }

            var salesQ = _context.SalesInvoices.AsNoTracking()
                .Where(si => si.IsPosted && si.SIDate >= from && si.SIDate < to && customerIdsList.Contains(si.CustomerId));
            if (creatorNames.Any())
                salesQ = salesQ.Where(si => creatorNames.Contains(si.CreatedBy));

            if (selProds.Any())
            {
                var siIdsWithProduct = await _context.SalesInvoiceLines.AsNoTracking()
                    .Where(l => selProds.Contains(l.ProdId)).Select(l => l.SIId).Distinct().ToListAsync();
                if (siIdsWithProduct.Any())
                    salesQ = salesQ.Where(si => siIdsWithProduct.Contains(si.SIId));
                else
                    salesQ = salesQ.Where(si => false);
            }

            var salesByWriter = await salesQ
                .GroupBy(si => si.CreatedBy)
                .Select(g => new
                {
                    WriterName = g.Key,
                    TotalSales = g.Sum(si => si.NetTotal),
                    InvoiceCount = g.Count(),
                    TotalBeforeDiscount = g.Sum(si => si.TotalBeforeDiscount),
                    TotalAfterDiscountBeforeTax = g.Sum(si => si.TotalAfterDiscountBeforeTax)
                })
                .ToListAsync();

            var siIdsInRange = await salesQ.Select(si => si.SIId).ToListAsync();
            var costBySi = new Dictionary<int, decimal>();
            var qtyBySi = new Dictionary<int, decimal>();
            if (siIdsInRange.Any())
            {
                var lineAgg = await _context.SalesInvoiceLines.AsNoTracking()
                    .Where(l => siIdsInRange.Contains(l.SIId))
                    .GroupBy(l => l.SIId)
                    .Select(g => new { SIId = g.Key, Cost = g.Sum(l => l.CostTotal), Qty = g.Sum(l => l.Qty) })
                    .ToListAsync();
                foreach (var x in lineAgg)
                {
                    costBySi[x.SIId] = x.Cost;
                    qtyBySi[x.SIId] = x.Qty;
                }
            }

            var siToWriter = await salesQ.Select(si => new { si.SIId, si.CreatedBy }).ToDictionaryAsync(x => x.SIId, x => x.CreatedBy);
            var costByWriter = salesByWriter.ToDictionary(x => x.WriterName, _ => 0m);
            var qtyByWriter = salesByWriter.ToDictionary(x => x.WriterName, _ => 0m);
            foreach (var si in siToWriter)
            {
                if (costBySi.TryGetValue(si.Key, out var c)) costByWriter[si.Value] = costByWriter.GetValueOrDefault(si.Value) + c;
                if (qtyBySi.TryGetValue(si.Key, out var q)) qtyByWriter[si.Value] = qtyByWriter.GetValueOrDefault(si.Value) + q;
            }

            var returnsQ = _context.SalesReturns.AsNoTracking()
                .Where(sr => sr.IsPosted && sr.SRDate >= from && sr.SRDate < to && customerIdsList.Contains(sr.CustomerId));
            if (creatorNames.Any())
                returnsQ = returnsQ.Where(sr => creatorNames.Contains(sr.CreatedBy));
            if (selProds.Any())
            {
                var srIdsWithProduct = await _context.SalesReturnLines.AsNoTracking()
                    .Where(l => selProds.Contains(l.ProdId)).Select(l => l.SRId).Distinct().ToListAsync();
                if (srIdsWithProduct.Any())
                    returnsQ = returnsQ.Where(sr => srIdsWithProduct.Contains(sr.SRId));
                else
                    returnsQ = returnsQ.Where(sr => false);
            }

            var returnsByWriter = await returnsQ
                .GroupBy(sr => sr.CreatedBy)
                .Select(g => new { WriterName = g.Key, TotalReturns = g.Sum(sr => sr.NetTotal) })
                .ToDictionaryAsync(x => x.WriterName, x => x.TotalReturns);
            var srIdsInRange = await returnsQ.Select(sr => sr.SRId).ToListAsync();

            decimal totalNetSales = 0m, totalReturns = 0m, totalCost = 0m, totalDiscountWeighted = 0m, totalQtyForDiscount = 0m;
            int totalInvoices = 0;
            foreach (var s in salesByWriter)
            {
                totalNetSales += s.TotalSales;
                totalCost += costByWriter.GetValueOrDefault(s.WriterName);
                totalInvoices += s.InvoiceCount;
                decimal ret = returnsByWriter.GetValueOrDefault(s.WriterName);
                totalReturns += ret;
                if (s.TotalBeforeDiscount > 0)
                {
                    decimal discPct = (s.TotalBeforeDiscount - s.TotalAfterDiscountBeforeTax) / s.TotalBeforeDiscount * 100m;
                    totalDiscountWeighted += discPct * s.TotalSales;
                    totalQtyForDiscount += s.TotalSales;
                }
            }

            vm.TotalReturns = totalReturns;
            decimal totalGrossSales = totalNetSales;
            vm.TotalGrossSales = totalGrossSales;
            vm.NetSales = totalNetSales - totalReturns;
            vm.NetProfit = (totalNetSales - totalCost) - totalReturns;
            vm.InvoiceCount = totalInvoices;
            vm.AvgInvoiceValue = totalInvoices > 0 ? (totalNetSales - totalReturns) / totalInvoices : 0m;
            decimal totalQtySold = qtyByWriter.Values.Sum();
            vm.AvgItemPrice = totalQtySold > 0 ? (totalNetSales - totalReturns) / totalQtySold : 0m;
            vm.DiscountPctAvg = totalQtyForDiscount > 0 ? totalDiscountWeighted / totalQtyForDiscount : 0m;
            if (totalGrossSales > 0)
            {
                vm.NetSalesPct = (vm.NetSales / totalGrossSales) * 100m;
                vm.ReturnsPct = (vm.TotalReturns / totalGrossSales) * 100m;
                vm.NetProfitPct = (vm.NetProfit / totalGrossSales) * 100m;
            }

            foreach (var s in salesByWriter.OrderByDescending(x => x.TotalSales))
            {
                decimal ret = returnsByWriter.GetValueOrDefault(s.WriterName);
                decimal netSalesRow = s.TotalSales - ret;
                decimal cost = costByWriter.GetValueOrDefault(s.WriterName);
                decimal profit = s.TotalSales - cost - ret;
                decimal salesPct = vm.NetSales != 0 ? (netSalesRow / vm.NetSales) * 100m : 0m;
                decimal returnsPct = s.TotalSales != 0 ? (ret / s.TotalSales) * 100m : 0m;
                decimal profitPct = netSalesRow != 0 ? (profit / netSalesRow) * 100m : 0m;
                decimal discPct = 0m;
                if (s.TotalBeforeDiscount > 0)
                    discPct = (s.TotalBeforeDiscount - s.TotalAfterDiscountBeforeTax) / s.TotalBeforeDiscount * 100m;

                vm.Rows.Add(new SalesPerformanceRow
                {
                    WriterName = s.WriterName ?? "—",
                    TotalSales = s.TotalSales,
                    SalesPct = salesPct,
                    TotalReturns = ret,
                    ReturnsPct = returnsPct,
                    NetProfit = profit,
                    NetProfitPct = profitPct,
                    InvoiceCount = s.InvoiceCount,
                    AvgInvoiceValue = s.InvoiceCount > 0 ? netSalesRow / s.InvoiceCount : 0m,
                    QtySold = qtyByWriter.GetValueOrDefault(s.WriterName ?? ""),
                    DiscountPct = discPct
                });
                vm.ChartData.Add(new SalesPerformanceChartPoint { WriterName = s.WriterName ?? "—", NetSales = netSalesRow });
            }

            // عند وجود فلتر: حساب إجمالي البيع والربح بدون فلتر (نفس الفترة) وعرض النسب من الإجمالي فقط عندها
            vm.HasActiveFilters = selGov.Any() || selDist.Any() || selArea.Any() || selAcct.Any() || selCust.Any() || selUsers.Any() || selProds.Any();
            if (vm.HasActiveFilters)
            {
                var salesGrandQ = _context.SalesInvoices.AsNoTracking().Where(si => si.IsPosted && si.SIDate >= from && si.SIDate < to);
                decimal totalSalesGrand = await salesGrandQ.SumAsync(si => si.NetTotal);
                var siIdsGrand = await salesGrandQ.Select(si => si.SIId).ToListAsync();
                decimal costGrand = 0m;
                if (siIdsGrand.Any())
                    costGrand = await _context.SalesInvoiceLines.AsNoTracking().Where(l => siIdsGrand.Contains(l.SIId)).SumAsync(l => l.CostTotal);
                decimal totalReturnsGrand = await _context.SalesReturns.AsNoTracking().Where(sr => sr.IsPosted && sr.SRDate >= from && sr.SRDate < to).SumAsync(sr => sr.NetTotal);
                vm.GrandTotalNetSales = totalSalesGrand - totalReturnsGrand;
                vm.GrandTotalNetProfit = (totalSalesGrand - costGrand) - totalReturnsGrand;
                if (vm.GrandTotalNetSales > 0)
                {
                    vm.NetSalesPctOfTotal = (vm.NetSales / vm.GrandTotalNetSales) * 100m;
                    vm.ReturnsPctOfTotal = (vm.TotalReturns / vm.GrandTotalNetSales) * 100m;
                    vm.NetProfitPctOfTotalSales = (vm.NetProfit / vm.GrandTotalNetSales) * 100m;
                }
                if (vm.GrandTotalNetProfit != 0)
                    vm.NetProfitPctOfTotalProfit = (vm.NetProfit / vm.GrandTotalNetProfit) * 100m;
            }

            if (salesByWriter.Count == 1 && salesByWriter[0].WriterName != null && siIdsInRange.Any())
            {
                var salesByDate = await _context.SalesInvoices.AsNoTracking()
                    .Where(si => siIdsInRange.Contains(si.SIId))
                    .GroupBy(si => si.SIDate)
                    .Select(g => new { Date = g.Key, Total = g.Sum(si => si.NetTotal) })
                    .ToDictionaryAsync(x => x.Date, x => x.Total);
                var returnsByDate = srIdsInRange.Any()
                    ? await _context.SalesReturns.AsNoTracking()
                        .Where(sr => srIdsInRange.Contains(sr.SRId))
                        .GroupBy(sr => sr.SRDate)
                        .Select(g => new { Date = g.Key, Total = g.Sum(sr => sr.NetTotal) })
                        .ToDictionaryAsync(x => x.Date, x => x.Total)
                    : new Dictionary<DateTime, decimal>();
                for (var d = from; d < to; d = d.AddDays(1))
                {
                    vm.ChartTimeSeriesLabels.Add(d.ToString("yyyy-MM-dd"));
                    var sales = salesByDate.TryGetValue(d, out var s) ? s : 0m;
                    var ret = returnsByDate.TryGetValue(d, out var r) ? r : 0m;
                    vm.ChartTimeSeriesValues.Add(sales - ret);
                }
            }

            var allGov = await _context.Governorates.AsNoTracking().OrderBy(g => g.GovernorateName).Select(g => new { g.GovernorateId, g.GovernorateName }).ToListAsync();
            vm.Governorates = allGov.Select(g => new SelectListItem(g.GovernorateName, g.GovernorateId.ToString(), selGov.Contains(g.GovernorateId))).ToList();
            var distFilter = !selGov.Any();
            var allDist = await _context.Districts.AsNoTracking().Where(d => distFilter || selGov.Contains(d.GovernorateId)).OrderBy(d => d.DistrictName).Select(d => new { d.DistrictId, d.DistrictName }).ToListAsync();
            vm.Districts = allDist.Select(d => new SelectListItem(d.DistrictName, d.DistrictId.ToString(), selDist.Contains(d.DistrictId))).ToList();
            var areaFilterGov = !selGov.Any();
            var areaFilterDist = !selDist.Any();
            var allAreas = await _context.Areas.AsNoTracking().Where(a => (areaFilterGov || selGov.Contains(a.GovernorateId)) && (areaFilterDist || (a.DistrictId.HasValue && selDist.Contains(a.DistrictId.Value)))).OrderBy(a => a.AreaName).Select(a => new { a.AreaId, a.AreaName }).ToListAsync();
            vm.Areas = allAreas.Select(a => new SelectListItem(a.AreaName, a.AreaId.ToString(), selArea.Contains(a.AreaId))).ToList();
            vm.AccountTypeOptions = new List<SelectListItem>
            {
                new SelectListItem("أصل", "1", selAcct.Contains(1)),
                new SelectListItem("التزام", "2", selAcct.Contains(2)),
                new SelectListItem("حقوق ملكية", "3", selAcct.Contains(3)),
                new SelectListItem("إيراد", "4", selAcct.Contains(4)),
                new SelectListItem("مصروف", "5", selAcct.Contains(5))
            };
            var allUsers = await _context.Users.AsNoTracking().Where(u => u.IsActive).OrderBy(u => u.DisplayName ?? u.UserName).Select(u => new { u.UserId, u.DisplayName, u.UserName }).ToListAsync();
            vm.Users = allUsers.Select(u => new SelectListItem((u.DisplayName ?? u.UserName) ?? u.UserId.ToString(), u.UserId.ToString(), selUsers.Contains(u.UserId))).ToList();
            var allCust = await _context.Customers.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.CustomerName).Take(500).Select(c => new { c.CustomerId, c.CustomerName }).ToListAsync();
            vm.Customers = allCust.Select(c => new SelectListItem(c.CustomerName ?? c.CustomerId.ToString(), c.CustomerId.ToString(), selCust.Contains(c.CustomerId))).ToList();
            // أصناف لها بيع فقط (في الفترة): مبيعات من فواتير معتمدة في المدى [from, to]
            var siIdsInRangeForProducts = await _context.SalesInvoices.AsNoTracking()
                .Where(si => si.IsPosted && si.SIDate >= from && si.SIDate < to).Select(si => si.SIId).ToListAsync();
            var productIdsWithSales = siIdsInRangeForProducts.Count > 0
                ? await _context.SalesInvoiceLines.AsNoTracking()
                    .Where(l => siIdsInRangeForProducts.Contains(l.SIId)).Select(l => l.ProdId).Distinct().ToListAsync()
                : new List<int>();
            var allProds = await _context.Products.AsNoTracking()
                .Where(p => p.IsActive && productIdsWithSales.Contains(p.ProdId))
                .OrderBy(p => p.ProdName).Select(p => new { p.ProdId, p.ProdName }).ToListAsync();
            vm.Products = allProds.Select(p => new SelectListItem(p.ProdName ?? p.ProdId.ToString(), p.ProdId.ToString(), selProds.Contains(p.ProdId))).ToList();

            return View(vm);
        }

        /// <summary>
        /// تقرير أداء المشتريات: KPIs + رسم بياني (خطي لعنصر واحد، أعمدة لأكثر من عنصر) مع فلاتر متعددة الاختيار.
        /// </summary>
        [HttpGet]
        [RequirePermission("Reports.PurchasePerformanceReport")]
        public async Task<IActionResult> PurchasePerformanceReport(
            DateTime? fromDate,
            DateTime? toDate,
            int[]? governorateIds,
            int[]? districtIds,
            int[]? areaIds,
            int[]? accountTypes,
            int[]? customerIds,
            int[]? userIds,
            int[]? productIds)
        {
            var vm = new PurchasePerformanceReportViewModel();
            var today = DateTime.Today;
            vm.FromDate = fromDate ?? new DateTime(today.Year, today.Month, 1);
            vm.ToDate = toDate ?? today;
            var selGov = governorateIds?.Where(id => id > 0).Distinct().ToList() ?? new List<int>();
            var selDist = districtIds?.Where(id => id > 0).Distinct().ToList() ?? new List<int>();
            var selArea = areaIds?.Where(id => id > 0).Distinct().ToList() ?? new List<int>();
            var selAcct = accountTypes?.Where(t => t >= 1 && t <= 5).Distinct().ToList() ?? new List<int>();
            var selCust = customerIds?.Where(id => id > 0).Distinct().ToList() ?? new List<int>();
            var selUsers = userIds?.Where(id => id > 0).Distinct().ToList() ?? new List<int>();
            var selProds = productIds?.Where(id => id > 0).Distinct().ToList() ?? new List<int>();
            vm.SelectedGovernorateIds = selGov;
            vm.SelectedDistrictIds = selDist;
            vm.SelectedAreaIds = selArea;
            vm.SelectedAccountTypes = selAcct;
            vm.SelectedCustomerIds = selCust;
            vm.SelectedUserIds = selUsers;
            vm.SelectedProductIds = selProds;

            var from = vm.FromDate.Value.Date;
            var to = vm.ToDate.Value.Date.AddDays(1);

            var custQ = _context.Customers.AsNoTracking().Where(c => c.IsActive);
            if (selGov.Any()) custQ = custQ.Where(c => selGov.Contains(c.GovernorateId ?? 0));
            if (selDist.Any()) custQ = custQ.Where(c => c.DistrictId.HasValue && selDist.Contains(c.DistrictId.Value));
            if (selArea.Any()) custQ = custQ.Where(c => c.AreaId.HasValue && selArea.Contains(c.AreaId.Value));
            if (selAcct.Any()) custQ = custQ.Where(c => c.AccountId != null && _context.Accounts.Any(a => a.AccountId == c.AccountId && selAcct.Contains((int)a.AccountType)));
            if (selCust.Any()) custQ = custQ.Where(c => selCust.Contains(c.CustomerId));
            var customerIdsList = await custQ.Select(c => c.CustomerId).ToListAsync();
            if (!customerIdsList.Any())
                customerIdsList = await _context.Customers.Where(c => c.IsActive).Select(c => c.CustomerId).ToListAsync();

            var creatorNames = new List<string>();
            if (selUsers.Any())
            {
                creatorNames = await _context.Users.AsNoTracking()
                    .Where(uu => selUsers.Contains(uu.UserId))
                    .Select(uu => uu.DisplayName ?? uu.UserName ?? "")
                    .Where(n => !string.IsNullOrEmpty(n)).ToListAsync();
            }

            var purchasesQ = _context.PurchaseInvoices.AsNoTracking()
                .Where(pi => pi.IsPosted && pi.PIDate >= from && pi.PIDate < to && customerIdsList.Contains(pi.CustomerId));
            if (creatorNames.Any())
                purchasesQ = purchasesQ.Where(pi => creatorNames.Contains(pi.CreatedBy));
            if (selProds.Any())
            {
                var piIdsWithProduct = await _context.PILines.AsNoTracking()
                    .Where(l => selProds.Contains(l.ProdId)).Select(l => l.PIId).Distinct().ToListAsync();
                if (piIdsWithProduct.Any())
                    purchasesQ = purchasesQ.Where(pi => piIdsWithProduct.Contains(pi.PIId));
                else
                    purchasesQ = purchasesQ.Where(pi => false);
            }

            var purchasesByWriter = await purchasesQ
                .GroupBy(pi => pi.CreatedBy)
                .Select(g => new
                {
                    WriterName = g.Key,
                    TotalPurchases = g.Sum(pi => pi.NetTotal),
                    InvoiceCount = g.Count()
                })
                .ToListAsync();

            var returnsQ = _context.PurchaseReturns.AsNoTracking()
                .Where(pr => pr.IsPosted && pr.PRetDate >= from && pr.PRetDate < to && customerIdsList.Contains(pr.CustomerId));
            if (creatorNames.Any())
                returnsQ = returnsQ.Where(pr => creatorNames.Contains(pr.CreatedBy));
            if (selProds.Any())
            {
                var pretIdsWithProduct = await _context.PurchaseReturnLines.AsNoTracking()
                    .Where(l => selProds.Contains(l.ProdId)).Select(l => l.PRetId).Distinct().ToListAsync();
                if (pretIdsWithProduct.Any())
                    returnsQ = returnsQ.Where(pr => pretIdsWithProduct.Contains(pr.PRetId));
                else
                    returnsQ = returnsQ.Where(pr => false);
            }

            var returnsByWriter = await returnsQ
                .GroupBy(pr => pr.CreatedBy)
                .Select(g => new { WriterName = g.Key, TotalReturns = g.Sum(pr => pr.NetTotal) })
                .ToDictionaryAsync(x => x.WriterName, x => x.TotalReturns);

            decimal totalPurchases = 0m, totalReturns = 0m;
            foreach (var p in purchasesByWriter)
            {
                totalPurchases += p.TotalPurchases;
                totalReturns += returnsByWriter.GetValueOrDefault(p.WriterName);
            }

            vm.TotalReturns = totalReturns;
            vm.TotalGrossPurchases = totalPurchases;
            vm.NetPurchases = totalPurchases - totalReturns;
            vm.InvoiceCount = purchasesByWriter.Sum(p => p.InvoiceCount);
            vm.AvgInvoiceValue = vm.InvoiceCount > 0 ? vm.NetPurchases / vm.InvoiceCount : 0m;
            vm.AvgItemPrice = 0m;
            vm.DiscountPctAvg = 0m;
            vm.NetProfit = 0m;
            if (vm.TotalGrossPurchases > 0)
            {
                vm.NetPurchasesPct = (vm.NetPurchases / vm.TotalGrossPurchases) * 100m;
                vm.ReturnsPct = (vm.TotalReturns / vm.TotalGrossPurchases) * 100m;
                vm.NetProfitPct = 0m;
            }

            foreach (var p in purchasesByWriter.OrderByDescending(x => x.TotalPurchases))
            {
                decimal ret = returnsByWriter.GetValueOrDefault(p.WriterName);
                decimal netRow = p.TotalPurchases - ret;
                decimal purchPct = vm.NetPurchases != 0 ? (netRow / vm.NetPurchases) * 100m : 0m;
                decimal retPct = p.TotalPurchases != 0 ? (ret / p.TotalPurchases) * 100m : 0m;
                vm.Rows.Add(new PurchasePerformanceRow
                {
                    WriterName = p.WriterName ?? "—",
                    TotalPurchases = p.TotalPurchases,
                    PurchasesPct = purchPct,
                    TotalReturns = ret,
                    ReturnsPct = retPct,
                    NetProfit = 0m,
                    NetProfitPct = 0m,
                    InvoiceCount = p.InvoiceCount,
                    AvgInvoiceValue = p.InvoiceCount > 0 ? netRow / p.InvoiceCount : 0m,
                    QtyBought = 0m,
                    DiscountPct = 0m
                });
                vm.ChartData.Add(new PurchasePerformanceChartPoint { WriterName = p.WriterName ?? "—", NetPurchases = netRow });
            }

            var piIdsInRange = await purchasesQ.Select(pi => pi.PIId).ToListAsync();
            if (purchasesByWriter.Count == 1 && purchasesByWriter[0].WriterName != null && piIdsInRange.Any())
            {
                var singleWriterName = purchasesByWriter[0].WriterName;
                var purchasesByDate = await _context.PurchaseInvoices.AsNoTracking()
                    .Where(pi => pi.IsPosted && pi.CreatedBy == singleWriterName && pi.PIDate >= from && pi.PIDate < to && customerIdsList.Contains(pi.CustomerId))
                    .GroupBy(pi => pi.PIDate)
                    .Select(g => new { Date = g.Key, Total = g.Sum(pi => pi.NetTotal) })
                    .ToDictionaryAsync(x => x.Date, x => x.Total);
                var pretIdsInRange = await returnsQ.Select(pr => pr.PRetId).ToListAsync();
                var returnsByDate = pretIdsInRange.Any()
                    ? await _context.PurchaseReturns.AsNoTracking()
                        .Where(pr => pretIdsInRange.Contains(pr.PRetId))
                        .GroupBy(pr => pr.PRetDate)
                        .Select(g => new { Date = g.Key, Total = g.Sum(pr => pr.NetTotal) })
                        .ToDictionaryAsync(x => x.Date, x => x.Total)
                    : new Dictionary<DateTime, decimal>();
                for (var d = from; d < to; d = d.AddDays(1))
                {
                    vm.ChartTimeSeriesLabels.Add(d.ToString("yyyy-MM-dd"));
                    var purch = purchasesByDate.TryGetValue(d, out var pp) ? pp : 0m;
                    var ret = returnsByDate.TryGetValue(d, out var rr) ? rr : 0m;
                    vm.ChartTimeSeriesValues.Add(purch - ret);
                }
            }

            vm.Governorates = (await _context.Governorates.AsNoTracking().OrderBy(g => g.GovernorateName).Select(g => new { g.GovernorateId, g.GovernorateName }).ToListAsync())
                .Select(g => new SelectListItem(g.GovernorateName, g.GovernorateId.ToString(), selGov.Contains(g.GovernorateId))).ToList();
            vm.Districts = (await _context.Districts.AsNoTracking().Where(d => !selGov.Any() || selGov.Contains(d.GovernorateId)).OrderBy(d => d.DistrictName).Select(d => new { d.DistrictId, d.DistrictName }).ToListAsync())
                .Select(d => new SelectListItem(d.DistrictName, d.DistrictId.ToString(), selDist.Contains(d.DistrictId))).ToList();
            vm.Areas = (await _context.Areas.AsNoTracking().Where(a => (!selGov.Any() || selGov.Contains(a.GovernorateId)) && (!selDist.Any() || (a.DistrictId.HasValue && selDist.Contains(a.DistrictId.Value)))).OrderBy(a => a.AreaName).Select(a => new { a.AreaId, a.AreaName }).ToListAsync())
                .Select(a => new SelectListItem(a.AreaName, a.AreaId.ToString(), selArea.Contains(a.AreaId))).ToList();
            vm.AccountTypeOptions = new List<SelectListItem>
            {
                new SelectListItem("أصل", "1", selAcct.Contains(1)),
                new SelectListItem("التزام", "2", selAcct.Contains(2)),
                new SelectListItem("حقوق ملكية", "3", selAcct.Contains(3)),
                new SelectListItem("إيراد", "4", selAcct.Contains(4)),
                new SelectListItem("مصروف", "5", selAcct.Contains(5))
            };
            vm.Users = (await _context.Users.AsNoTracking().Where(u => u.IsActive).OrderBy(u => u.DisplayName ?? u.UserName).Select(u => new { u.UserId, u.DisplayName, u.UserName }).ToListAsync())
                .Select(u => new SelectListItem((u.DisplayName ?? u.UserName) ?? u.UserId.ToString(), u.UserId.ToString(), selUsers.Contains(u.UserId))).ToList();
            vm.Customers = (await _context.Customers.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.CustomerName).Take(500).Select(c => new { c.CustomerId, c.CustomerName }).ToListAsync())
                .Select(c => new SelectListItem(c.CustomerName ?? c.CustomerId.ToString(), c.CustomerId.ToString(), selCust.Contains(c.CustomerId))).ToList();
            vm.Products = (await _context.Products.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.ProdName).Select(p => new { p.ProdId, p.ProdName }).ToListAsync())
                .Select(p => new SelectListItem(p.ProdName ?? p.ProdId.ToString(), p.ProdId.ToString(), selProds.Contains(p.ProdId))).ToList();

            return View(vm);
        }

        // =========================================================
        // تقرير خط السير: فواتير المبيعات مع بيانات خط السير (شنط، بواكي، كراتين، ثلاجة، ملاحظات)
        // =========================================================
        [HttpGet]
        [RequirePermission("Reports.RouteReport")]
        public async Task<IActionResult> RouteReport(
            DateTime? fromDate,
            DateTime? toDate,
            int? fromSIId,
            int? toSIId,
            int? routeId,
            int? warehouseId,
            bool loadReport = false,
            int page = 1,
            int pageSize = 100)
        {
            var routes = await _context.Routes
                .AsNoTracking()
                .Where(r => r.IsActive)
                .OrderBy(r => r.SortOrder).ThenBy(r => r.Name)
                .Select(r => new SelectListItem(r.Name ?? r.Id.ToString(), r.Id.ToString(), routeId == r.Id))
                .ToListAsync();
            var warehouses = await _context.Warehouses
                .AsNoTracking()
                .OrderBy(w => w.WarehouseName)
                .Select(w => new SelectListItem(w.WarehouseName ?? w.WarehouseId.ToString(), w.WarehouseId.ToString(), warehouseId == w.WarehouseId))
                .ToListAsync();

            ViewBag.Routes = routes;
            ViewBag.Warehouses = warehouses;
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;
            ViewBag.FromSIId = fromSIId;
            ViewBag.ToSIId = toSIId;
            ViewBag.RouteId = routeId;
            ViewBag.WarehouseId = warehouseId;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;

            if (!loadReport)
            {
                ViewBag.ReportData = new List<RouteReportRowDto>();
                ViewBag.TotalCount = 0;
                ViewBag.TotalPages = 1;
                return View();
            }

            var q = _context.SalesInvoices
                .AsNoTracking()
                .Include(si => si.Customer).ThenInclude(c => c!.Route)
                .Include(si => si.Warehouse)
                .Include(si => si.Route)
                .AsQueryable();

            if (fromDate.HasValue)
                q = q.Where(si => si.SIDate >= fromDate.Value.Date);
            if (toDate.HasValue)
                q = q.Where(si => si.SIDate <= toDate.Value.Date);
            if (fromSIId.HasValue)
                q = q.Where(si => si.SIId >= fromSIId.Value);
            if (toSIId.HasValue)
                q = q.Where(si => si.SIId <= toSIId.Value);
            if (routeId.HasValue && routeId.Value > 0)
                q = q.Where(si => si.Customer != null && si.Customer.RouteId == routeId.Value);
            if (warehouseId.HasValue && warehouseId.Value > 0)
                q = q.Where(si => si.WarehouseId == warehouseId.Value);

            q = q.OrderByDescending(si => si.SIDate).ThenByDescending(si => si.SIId);

            int total = await q.CountAsync();
            var list = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            var reportData = new List<RouteReportRowDto>();
            foreach (var si in list)
            {
                var routeData = si.Route;
                reportData.Add(new RouteReportRowDto
                {
                    SIId = si.SIId,
                    SIDate = si.SIDate,
                    CustomerName = si.Customer?.CustomerName ?? "",
                    RouteId = si.Customer?.RouteId,
                    RouteName = si.Customer?.Route?.Name ?? "",
                    WarehouseName = si.Warehouse?.WarehouseName ?? "",
                    BagsCount = routeData?.BagsCount ?? 0,
                    PacketsCount = routeData?.PacketsCount ?? 0,
                    CartonsCount = routeData?.CartonsCount ?? 0,
                    FridgeItemsCount = routeData?.FridgeItemsCount ?? 0,
                    FridgeBoxesCount = routeData?.FridgeBoxesCount ?? 0,
                    Notes = routeData?.Notes
                });
            }

            ViewBag.ReportData = reportData;
            ViewBag.TotalCount = total;
            ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
            return View();
        }
    }
}
