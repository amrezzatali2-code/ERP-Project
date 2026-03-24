using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ERP.Data;
using ERP.Filters;
using ERP.Models;
using ERP.Security;
using ERP.Services;

namespace ERP.Controllers
{
    /// <summary>
    /// الكنترولر الخاص بالخزينة الرئيسية
    /// يعرض رصيد الخزينة وأذون الاستلام والدفع (تفاصيل الأذون تُصفّى حسب الحسابات المسموح رؤيتها للمستخدم).
    /// </summary>
    [RequirePermission("Treasury.Index")]
    public class TreasuryController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IUserAccountVisibilityService _userAccountVisibility;

        public TreasuryController(AppDbContext context, IUserAccountVisibilityService userAccountVisibility)
        {
            _context = context;
            _userAccountVisibility = userAccountVisibility;
        }

        /// <summary>
        /// عرض الخزينة الرئيسية: الرصيد + أذون الاستلام والدفع
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Index(
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? createdBy = null,
            int? cashAccountId = null,
            int pageReceipts = 1,
            int pagePayments = 1,
            int pageSize = 20
        )
        {
            createdBy = string.IsNullOrWhiteSpace(createdBy) ? null : createdBy.Trim();
            string? createdByLower = createdBy?.ToLowerInvariant();

            var hiddenAccountIds = await _userAccountVisibility.GetHiddenAccountIdsForCurrentUserAsync();
            var allTreasuryBoxIds = await TreasuryCashAccounts.GetTreasuryCashBoxAccountIdsAsync(_context);
            var visibleTreasuryBoxIds = allTreasuryBoxIds.Where(id => !hiddenAccountIds.Contains(id)).ToList();
            var visibleSet = new HashSet<int>(visibleTreasuryBoxIds);

            // خزينة واحدة للفلتر (اختياري) — يجب أن تكون ضمن الخزائن المرئية
            int? effectiveCashBoxId = null;
            if (cashAccountId.HasValue && cashAccountId.Value > 0 && visibleSet.Contains(cashAccountId.Value))
                effectiveCashBoxId = cashAccountId.Value;

            var balanceAccountIds = effectiveCashBoxId.HasValue
                ? new List<int> { effectiveCashBoxId.Value }
                : visibleTreasuryBoxIds;

            // =========================================================
            // 1) حساب الرصيد الإجمالي للخزينة (من LedgerEntries لحسابات الخزائن المرئية أو خزينة واحدة)
            // — عند اختيار مستخدم: نعرض صافي حركته (استلام − دفع) في الفترة بدل رصيد الدفتر الكامل
            // =========================================================
            decimal totalTreasuryBalance = 0m;

            if (balanceAccountIds.Count > 0)
            {
                totalTreasuryBalance = await _context.LedgerEntries
                    .AsNoTracking()
                    .Where(e => balanceAccountIds.Contains(e.AccountId))
                    .SumAsync(e => (decimal?)(e.Debit - e.Credit)) ?? 0m;
            }

            // =========================================================
            // 2) جلب أذون الاستلام (CashReceipts) — إخفاء أذون الطرف + فقط خزائن معرّفة كصندوق
            // =========================================================
            var receiptsQuery = _context.CashReceipts
                .AsNoTracking()
                .Include(r => r.CashAccount)
                .Include(r => r.CounterAccount)
                .Include(r => r.Customer)
                .Where(r => !hiddenAccountIds.Contains(r.CounterAccountId))
                .OrderByDescending(r => r.ReceiptDate)
                .ThenByDescending(r => r.CashReceiptId)
                .AsQueryable();

            if (visibleTreasuryBoxIds.Count > 0)
                receiptsQuery = receiptsQuery.Where(r => visibleTreasuryBoxIds.Contains(r.CashAccountId));
            else
                receiptsQuery = receiptsQuery.Where(_ => false);

            if (effectiveCashBoxId.HasValue)
                receiptsQuery = receiptsQuery.Where(r => r.CashAccountId == effectiveCashBoxId.Value);

            // فلترة بالتاريخ
            if (fromDate.HasValue)
            {
                receiptsQuery = receiptsQuery.Where(r => r.ReceiptDate >= fromDate.Value);
            }
            if (toDate.HasValue)
            {
                receiptsQuery = receiptsQuery.Where(r => r.ReceiptDate <= toDate.Value);
            }

            if (createdByLower != null)
                receiptsQuery = receiptsQuery.Where(r => r.CreatedBy != null && r.CreatedBy.Trim().ToLower() == createdByLower);

            // إجمالي أذون الاستلام (قبل الترقيم)
            decimal totalReceiptsAmount = await receiptsQuery.SumAsync(r => (decimal?)r.Amount) ?? 0m;

            // الترقيم
            int totalReceipts = await receiptsQuery.CountAsync();
            int totalPagesReceipts = (int)Math.Ceiling(totalReceipts / (double)pageSize);
            if (pageReceipts < 1) pageReceipts = 1;
            if (pageReceipts > totalPagesReceipts && totalPagesReceipts > 0) pageReceipts = totalPagesReceipts;

            var receipts = await receiptsQuery
                .Skip((pageReceipts - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // =========================================================
            // 3) جلب أذون الدفع (CashPayments) — إخفاء أذون الطرف الذي حسابه غير مسموح للمستخدم
            // =========================================================
            var paymentsQuery = _context.CashPayments
                .AsNoTracking()
                .Include(p => p.CashAccount)
                .Include(p => p.CounterAccount)
                .Include(p => p.Customer)
                .Where(p => !hiddenAccountIds.Contains(p.CounterAccountId))
                .OrderByDescending(p => p.PaymentDate)
                .ThenByDescending(p => p.CashPaymentId)
                .AsQueryable();

            if (visibleTreasuryBoxIds.Count > 0)
                paymentsQuery = paymentsQuery.Where(p => visibleTreasuryBoxIds.Contains(p.CashAccountId));
            else
                paymentsQuery = paymentsQuery.Where(_ => false);

            if (effectiveCashBoxId.HasValue)
                paymentsQuery = paymentsQuery.Where(p => p.CashAccountId == effectiveCashBoxId.Value);

            // فلترة بالتاريخ
            if (fromDate.HasValue)
            {
                paymentsQuery = paymentsQuery.Where(p => p.PaymentDate >= fromDate.Value);
            }
            if (toDate.HasValue)
            {
                paymentsQuery = paymentsQuery.Where(p => p.PaymentDate <= toDate.Value);
            }

            if (createdByLower != null)
                paymentsQuery = paymentsQuery.Where(p => p.CreatedBy != null && p.CreatedBy.Trim().ToLower() == createdByLower);

            // إجمالي أذون الدفع (قبل الترقيم)
            decimal totalPaymentsAmount = await paymentsQuery.SumAsync(p => (decimal?)p.Amount) ?? 0m;

            // الترقيم
            int totalPayments = await paymentsQuery.CountAsync();
            int totalPagesPayments = (int)Math.Ceiling(totalPayments / (double)pageSize);
            if (pagePayments < 1) pagePayments = 1;
            if (pagePayments > totalPagesPayments && totalPagesPayments > 0) pagePayments = totalPagesPayments;

            var payments = await paymentsQuery
                .Skip((pagePayments - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // =========================================================
            // قائمة من أنشأوا أذوناً (ضمن ما يحق للمستخدم رؤيته) — لقائمة الاختيار
            // =========================================================
            var creatorsReceiptsQuery = _context.CashReceipts.AsNoTracking()
                .Where(r => !hiddenAccountIds.Contains(r.CounterAccountId) && r.CreatedBy != null && r.CreatedBy.Trim() != "");
            var creatorsPaymentsQuery = _context.CashPayments.AsNoTracking()
                .Where(p => !hiddenAccountIds.Contains(p.CounterAccountId) && p.CreatedBy != null && p.CreatedBy.Trim() != "");
            if (visibleTreasuryBoxIds.Count > 0)
            {
                creatorsReceiptsQuery = creatorsReceiptsQuery.Where(r => visibleTreasuryBoxIds.Contains(r.CashAccountId));
                creatorsPaymentsQuery = creatorsPaymentsQuery.Where(p => visibleTreasuryBoxIds.Contains(p.CashAccountId));
            }
            if (effectiveCashBoxId.HasValue)
            {
                creatorsReceiptsQuery = creatorsReceiptsQuery.Where(r => r.CashAccountId == effectiveCashBoxId.Value);
                creatorsPaymentsQuery = creatorsPaymentsQuery.Where(p => p.CashAccountId == effectiveCashBoxId.Value);
            }

            var creatorsReceipts = await creatorsReceiptsQuery.Select(r => r.CreatedBy!.Trim()).Distinct().ToListAsync();
            var creatorsPayments = await creatorsPaymentsQuery.Select(p => p.CreatedBy!.Trim()).Distinct().ToListAsync();
            var treasuryCreators = creatorsReceipts
                .Concat(creatorsPayments)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).First())
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var balanceForDisplay = createdByLower != null
                ? totalReceiptsAmount - totalPaymentsAmount
                : totalTreasuryBalance;

            var treasuryBoxRows = await TreasuryCashAccounts.QueryTreasuryCashBoxes(_context.Accounts.AsNoTracking())
                .Where(a => visibleSet.Contains(a.AccountId))
                .OrderBy(a => a.AccountCode == "1101" ? 0 : 1)
                .ThenBy(a => a.AccountCode)
                .ThenBy(a => a.AccountName)
                .Select(a => new { a.AccountId, a.AccountName, a.AccountCode })
                .ToListAsync();

            var treasuryCashBoxItems = new List<SelectListItem>
            {
                new() { Value = "", Text = "— كل الخزائن المرئية —", Selected = !effectiveCashBoxId.HasValue }
            };
            foreach (var b in treasuryBoxRows)
            {
                var code = b.AccountCode ?? "";
                var label = (string.IsNullOrEmpty(code) ? "" : code + " — ") + (b.AccountName ?? "");
                treasuryCashBoxItems.Add(new SelectListItem
                {
                    Value = b.AccountId.ToString(),
                    Text = label,
                    Selected = effectiveCashBoxId == b.AccountId
                });
            }

            // =========================================================
            // 4) تمرير البيانات للفيو
            // =========================================================
            ViewBag.TotalTreasuryBalance = balanceForDisplay;
            ViewBag.TreasuryBalanceIsLedgerTotal = createdByLower == null;
            ViewBag.TreasuryCashAccountId = effectiveCashBoxId;
            ViewBag.TreasuryCashBoxSelect = new SelectList(treasuryCashBoxItems, "Value", "Text", effectiveCashBoxId?.ToString() ?? "");
            ViewBag.Receipts = receipts;
            ViewBag.Payments = payments;
            ViewBag.TotalReceiptsAmount = totalReceiptsAmount;
            ViewBag.TotalPaymentsAmount = totalPaymentsAmount;
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;
            ViewBag.CreatedBy = createdBy;
            ViewBag.TreasuryCreators = treasuryCreators;
            ViewBag.PageReceipts = pageReceipts;
            ViewBag.PagePayments = pagePayments;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalReceipts = totalReceipts;
            ViewBag.TotalPayments = totalPayments;
            ViewBag.TotalPagesReceipts = totalPagesReceipts;
            ViewBag.TotalPagesPayments = totalPagesPayments;

            return View();
        }

        /// <summary>
        /// تصفير رصيد الخزينة (حذف كل قيود LedgerEntries لحسابات الخزينة والبنوك).
        /// استخدم عند البدء من جديد. لا يمكن التراجع.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ZeroTreasuryBalance()
        {
            var cashAccounts = await TreasuryCashAccounts.GetTreasuryCashBoxAccountIdsAsync(_context);

            if (cashAccounts.Count == 0)
            {
                TempData["ErrorMessage"] = "لم يتم العثور على حسابات الخزينة.";
                return RedirectToAction(nameof(Index));
            }

            var entries = await _context.LedgerEntries
                .Where(e => cashAccounts.Contains(e.AccountId))
                .ToListAsync();

            _context.LedgerEntries.RemoveRange(entries);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"تم تصفير رصيد الخزينة ({entries.Count} قيد). لا يمكن التراجع.";
            return RedirectToAction(nameof(Index));
        }
    }
}
