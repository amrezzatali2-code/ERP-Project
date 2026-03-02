using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ERP.Data;
using ERP.Filters;
using ERP.Models;
using ERP.Security;

namespace ERP.Controllers
{
    /// <summary>
    /// الكنترولر الخاص بالخزينة الرئيسية
    /// يعرض رصيد الخزينة وأذون الاستلام والدفع
    /// </summary>
    [RequirePermission("Treasury.Index")]
    public class TreasuryController : Controller
    {
        private readonly AppDbContext _context;

        public TreasuryController(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// عرض الخزينة الرئيسية: الرصيد + أذون الاستلام والدفع
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Index(
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int pageReceipts = 1,
            int pagePayments = 1,
            int pageSize = 20
        )
        {
            // =========================================================
            // 1) حساب الرصيد الإجمالي للخزينة (من جميع حسابات الخزينة والبنوك)
            // =========================================================
            // جلب جميع حسابات الخزينة والبنوك (Asset)
            var cashAccounts = await _context.Accounts
                .AsNoTracking()
                .Where(a => a.AccountType == AccountType.Asset &&
                           (a.AccountName.Contains("خزينة") || 
                            a.AccountName.Contains("بنك") ||
                            a.AccountName.Contains("صندوق") ||
                            a.AccountCode.StartsWith("1101") || // كود الخزينة
                            a.AccountCode.StartsWith("1102")))  // كود البنوك
                .Select(a => a.AccountId)
                .ToListAsync();

            // حساب الرصيد الإجمالي من LedgerEntries (القيود المحاسبية)
            // ✅ ملاحظة مهمة: الرصيد يُحسب من القيود المحاسبية المرحّلة فقط
            // يجب ترحيل أذون الاستلام والدفع إلى LedgerEntries لتظهر في الرصيد
            decimal totalTreasuryBalance = 0m;
            
            if (cashAccounts.Any())
            {
                totalTreasuryBalance = await _context.LedgerEntries
                    .AsNoTracking()
                    .Where(e => cashAccounts.Contains(e.AccountId))
                    .SumAsync(e => (decimal?)(e.Debit - e.Credit)) ?? 0m;
            }

            // =========================================================
            // 2) جلب أذون الاستلام (CashReceipts)
            // =========================================================
            var receiptsQuery = _context.CashReceipts
                .AsNoTracking()
                .Include(r => r.CashAccount)
                .Include(r => r.CounterAccount)
                .Include(r => r.Customer)
                .OrderByDescending(r => r.ReceiptDate)
                .ThenByDescending(r => r.CashReceiptId)
                .AsQueryable();

            // فلترة بالتاريخ
            if (fromDate.HasValue)
            {
                receiptsQuery = receiptsQuery.Where(r => r.ReceiptDate >= fromDate.Value);
            }
            if (toDate.HasValue)
            {
                receiptsQuery = receiptsQuery.Where(r => r.ReceiptDate <= toDate.Value);
            }

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
            // 3) جلب أذون الدفع (CashPayments)
            // =========================================================
            var paymentsQuery = _context.CashPayments
                .AsNoTracking()
                .Include(p => p.CashAccount)
                .Include(p => p.CounterAccount)
                .Include(p => p.Customer)
                .OrderByDescending(p => p.PaymentDate)
                .ThenByDescending(p => p.CashPaymentId)
                .AsQueryable();

            // فلترة بالتاريخ
            if (fromDate.HasValue)
            {
                paymentsQuery = paymentsQuery.Where(p => p.PaymentDate >= fromDate.Value);
            }
            if (toDate.HasValue)
            {
                paymentsQuery = paymentsQuery.Where(p => p.PaymentDate <= toDate.Value);
            }

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
            // 4) تمرير البيانات للفيو
            // =========================================================
            ViewBag.TotalTreasuryBalance = totalTreasuryBalance;
            ViewBag.Receipts = receipts;
            ViewBag.Payments = payments;
            ViewBag.TotalReceiptsAmount = totalReceiptsAmount;
            ViewBag.TotalPaymentsAmount = totalPaymentsAmount;
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;
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
            var cashAccounts = await _context.Accounts
                .AsNoTracking()
                .Where(a => a.AccountType == AccountType.Asset &&
                           (a.AccountName.Contains("خزينة") ||
                            a.AccountName.Contains("بنك") ||
                            a.AccountName.Contains("صندوق") ||
                            a.AccountCode.StartsWith("1101") ||
                            a.AccountCode.StartsWith("1102")))
                .Select(a => a.AccountId)
                .ToListAsync();

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
