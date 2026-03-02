using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ERP.Data;
using ERP.Filters;
using ERP.Infrastructure;
using ERP.Models;
using ERP.Security;

namespace ERP.Controllers
{
    /// <summary>
    /// كشف حساب عميل — يعرض كل عمليات العميل في مدة معينة بالتاريخ
    /// </summary>
    [RequirePermission("CustomerLedger.Index")]
    public class CustomerLedgerController : Controller
    {
        private readonly AppDbContext _context;

        public CustomerLedgerController(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// كشف حساب عميل — القيود المحاسبية للعميل مع فلتر التاريخ
        /// عند عدم اختيار عميل: يعرض نموذج اختيار العميل
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Index(
            int? customerId,
            DateTime? fromDate,
            DateTime? toDate,
            int page = 1,
            int pageSize = 50)
        {
            // تجهيز قائمة العملاء للأوتوكومبليت (مثل فاتورة البيع / حجم التعامل)
            var customersList = await _context.Customers
                .AsNoTracking()
                .OrderBy(c => c.CustomerName)
                .Select(c => new { id = c.CustomerId, code = c.CustomerId.ToString(), name = c.CustomerName })
                .ToListAsync();
            ViewBag.CustomersJson = JsonSerializer.Serialize(customersList);

            if (!customerId.HasValue || customerId.Value <= 0)
            {
                ViewBag.Customer = (Customer?)null;
                ViewBag.FromDate = fromDate;
                ViewBag.ToDate = toDate;
                ViewBag.TotalDebit = 0m;
                ViewBag.TotalCredit = 0m;
                ViewBag.NetBalance = 0m;
                var emptyModel = await PagedResult<LedgerEntry>.CreateAsync(
                    _context.LedgerEntries.Where(e => false), 1, pageSize);
                return View(emptyModel);
            }

            var customer = await _context.Customers
                .AsNoTracking()
                .Include(c => c.Account)
                .FirstOrDefaultAsync(c => c.CustomerId == customerId.Value);

            if (customer == null)
            {
                TempData["Error"] = "العميل غير موجود.";
                return RedirectToAction("Show", "Customers");
            }

            IQueryable<LedgerEntry> q = _context.LedgerEntries
                .AsNoTracking()
                .Include(e => e.Account)
                .Where(e => e.CustomerId == customerId.Value);

            // استبعاد القيود المرتبطة بمستندات محذوفة (مثل فاتورة تم حذفها ثم عُكست قيودها)
            q = q.Where(e =>
                !e.SourceId.HasValue
                || e.SourceType == LedgerSourceType.Opening
                || e.SourceType == LedgerSourceType.Journal
                || e.SourceType == LedgerSourceType.Adjustment
                || e.SourceType == LedgerSourceType.StockTransfer
                || e.SourceType == LedgerSourceType.StockAdjustment
                || (e.SourceType == LedgerSourceType.SalesInvoice && _context.SalesInvoices.Any(s => s.SIId == e.SourceId))
                || (e.SourceType == LedgerSourceType.SalesReturn && _context.SalesReturns.Any(s => s.SRId == e.SourceId))
                || (e.SourceType == LedgerSourceType.PurchaseInvoice && _context.PurchaseInvoices.Any(p => p.PIId == e.SourceId))
                || (e.SourceType == LedgerSourceType.PurchaseReturn && _context.PurchaseReturns.Any(p => p.PRetId == e.SourceId))
                || (e.SourceType == LedgerSourceType.Receipt && _context.CashReceipts.Any(r => r.CashReceiptId == e.SourceId))
                || (e.SourceType == LedgerSourceType.Payment && _context.CashPayments.Any(p => p.CashPaymentId == e.SourceId))
                || (e.SourceType == LedgerSourceType.DebitNote && _context.DebitNotes.Any(d => d.DebitNoteId == e.SourceId))
                || (e.SourceType == LedgerSourceType.CreditNote && _context.CreditNotes.Any(c => c.CreditNoteId == e.SourceId)));

            if (fromDate.HasValue)
                q = q.Where(e => e.EntryDate >= fromDate.Value.Date);

            if (toDate.HasValue)
                q = q.Where(e => e.EntryDate <= toDate.Value.Date.AddDays(1));

            q = q.OrderBy(e => e.EntryDate).ThenBy(e => e.Id);

            decimal totalDebit = await q.SumAsync(e => (decimal?)e.Debit) ?? 0m;
            decimal totalCredit = await q.SumAsync(e => (decimal?)e.Credit) ?? 0m;
            decimal netBalance = totalDebit - totalCredit;

            var model = await PagedResult<LedgerEntry>.CreateAsync(q, page, pageSize);

            ViewBag.Customer = customer;
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;
            ViewBag.TotalDebit = totalDebit;
            ViewBag.TotalCredit = totalCredit;
            ViewBag.NetBalance = netBalance;

            return View(model);
        }
    }
}
