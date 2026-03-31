using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ERP.Data;
using ERP.Filters;
using ERP.Infrastructure;
using ERP.Models;
using ERP.Security;
using ERP.Services;
using ERP.ViewModels;

namespace ERP.Controllers
{
    /// <summary>
    /// كشف حساب عميل — يعرض كل عمليات العميل في مدة معينة بالتاريخ
    /// </summary>
    [RequirePermission("CustomerLedger.Index")]
    public class CustomerLedgerController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IPermissionService _permissionService;
        private readonly IUserAccountVisibilityService _accountVisibilityService;

        public CustomerLedgerController(
            AppDbContext context,
            IPermissionService permissionService,
            IUserAccountVisibilityService accountVisibilityService)
        {
            _context = context;
            _permissionService = permissionService;
            _accountVisibilityService = accountVisibilityService;
        }

        private static string SourceTypeDisplayAr(LedgerSourceType t)
        {
            return t switch
            {
                LedgerSourceType.Opening => "رصيد افتتاحي",
                LedgerSourceType.SalesInvoice => "فاتورة مبيعات",
                LedgerSourceType.SalesReturn => "مرتجع بيع",
                LedgerSourceType.PurchaseInvoice => "فاتورة مشتريات",
                LedgerSourceType.PurchaseReturn => "مرتجع شراء",
                LedgerSourceType.Receipt => "إذن استلام",
                LedgerSourceType.Payment => "إذن دفع",
                LedgerSourceType.Journal => "قيد يومية",
                LedgerSourceType.Adjustment => "تسوية",
                LedgerSourceType.StockTransfer => "تحويل مخزني",
                LedgerSourceType.StockAdjustment => "تسوية جرد",
                LedgerSourceType.DebitNote => "إشعار خصم",
                LedgerSourceType.CreditNote => "إشعار إضافة",
                _ => t.ToString()
            };
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
            string? sort = "entryDate",
            string? dir = "asc",
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_entryDate = null,
            string? filterCol_sourceType = null,
            string? filterCol_voucherNo = null,
            string? filterCol_account = null,
            string? filterCol_debit = null,
            string? filterCol_credit = null,
            string? filterCol_description = null,
            int page = 1,
            int pageSize = 50)
        {
            var sep = new[] { '|', ',' };
            var custQueryLedger = _context.Customers.AsNoTracking();
            custQueryLedger = await _accountVisibilityService.ApplyCustomerVisibilityFilterAsync(custQueryLedger);
            // تجهيز قائمة العملاء — نفس مصدر حجم التعامل (datalist + بحث متعدد الكلمات)
            var customersVolume = await custQueryLedger
                .OrderBy(c => c.CustomerName)
                .Select(c => new CustomerVolumeDropdownItem
                {
                    Id = c.CustomerId,
                    Name = c.CustomerName ?? "",
                    Phone = c.Phone1 ?? "",
                    IsActive = c.IsActive
                })
                .ToListAsync();
            ViewBag.CustomersVolumeDropdown = customersVolume;

            if (!customerId.HasValue || customerId.Value <= 0)
            {
                ViewBag.Customer = (Customer?)null;
                ViewBag.FromDate = fromDate;
                ViewBag.ToDate = toDate;
                ViewBag.TotalDebit = 0m;
                ViewBag.TotalCredit = 0m;
                ViewBag.NetBalance = 0m;
                ViewBag.Sort = (sort ?? "entryDate").Trim().ToLowerInvariant();
                ViewBag.Dir = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc";
                ViewBag.FilterCol_Id = filterCol_id;
                ViewBag.FilterCol_IdExpr = filterCol_idExpr;
                ViewBag.FilterCol_EntryDate = filterCol_entryDate;
                ViewBag.FilterCol_SourceType = filterCol_sourceType;
                ViewBag.FilterCol_VoucherNo = filterCol_voucherNo;
                ViewBag.FilterCol_Account = filterCol_account;
                ViewBag.FilterCol_Debit = filterCol_debit;
                ViewBag.FilterCol_Credit = filterCol_credit;
                ViewBag.FilterCol_Description = filterCol_description;
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

            // لو العميل غير ظاهر للمستخدم (حسب صلاحيات الحسابات) → لا نسمح بعرض دفتر حسابه
            var custVisibilityQuery = _context.Customers.AsNoTracking().Where(c => c.CustomerId == customerId.Value);
            custVisibilityQuery = await _accountVisibilityService.ApplyCustomerVisibilityFilterAsync(custVisibilityQuery);
            if (!await custVisibilityQuery.AnyAsync())
            {
                return NotFound();
            }

            IQueryable<LedgerEntry> q = _context.LedgerEntries
                .AsNoTracking()
                .Include(e => e.Account)
                .Where(e => e.CustomerId == customerId.Value);

            // استبعاد قيود "عكس ترحيل" — نعرض القيد النهائي فقط دون ظهور عكس الترحيل
            q = q.Where(e => e.Description == null || !e.Description.Contains("عكس ترحيل"));

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

            // لكل مستند (نوع + رقم) نأخذ آخر مرحلة ترحيل فقط — حتى لا يتكرر رقم المستند
            q = q.Where(e => !e.SourceId.HasValue ||
                e.PostVersion == (_context.LedgerEntries
                    .Where(x => x.CustomerId == customerId.Value && x.SourceType == e.SourceType && x.SourceId == e.SourceId
                        && (x.Description == null || !x.Description.Contains("عكس ترحيل"))
                        && (!x.SourceId.HasValue
                            || x.SourceType == LedgerSourceType.Opening
                            || x.SourceType == LedgerSourceType.Journal
                            || x.SourceType == LedgerSourceType.Adjustment
                            || x.SourceType == LedgerSourceType.StockTransfer
                            || x.SourceType == LedgerSourceType.StockAdjustment
                            || (x.SourceType == LedgerSourceType.SalesInvoice && _context.SalesInvoices.Any(s => s.SIId == x.SourceId))
                            || (x.SourceType == LedgerSourceType.SalesReturn && _context.SalesReturns.Any(s => s.SRId == x.SourceId))
                            || (x.SourceType == LedgerSourceType.PurchaseInvoice && _context.PurchaseInvoices.Any(p => p.PIId == x.SourceId))
                            || (x.SourceType == LedgerSourceType.PurchaseReturn && _context.PurchaseReturns.Any(p => p.PRetId == x.SourceId))
                            || (x.SourceType == LedgerSourceType.Receipt && _context.CashReceipts.Any(r => r.CashReceiptId == x.SourceId))
                            || (x.SourceType == LedgerSourceType.Payment && _context.CashPayments.Any(p => p.CashPaymentId == x.SourceId))
                            || (x.SourceType == LedgerSourceType.DebitNote && _context.DebitNotes.Any(d => d.DebitNoteId == x.SourceId))
                            || (x.SourceType == LedgerSourceType.CreditNote && _context.CreditNotes.Any(c => c.CreditNoteId == x.SourceId))))
                    .Max(x => (int?)x.PostVersion) ?? 0));

            if (fromDate.HasValue)
                q = q.Where(e => e.EntryDate >= fromDate.Value.Date);

            if (toDate.HasValue)
                q = q.Where(e => e.EntryDate <= toDate.Value.Date.AddDays(1));

            // فلاتر أعمدة بنمط Excel
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var id) ? id : (int?)null).Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    q = q.Where(e => ids.Contains(e.Id));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_idExpr))
            {
                var expr = filterCol_idExpr.Trim();
                if (expr.StartsWith("<") && int.TryParse(expr.AsSpan(1).Trim(), out var maxId))
                    q = q.Where(e => e.Id < maxId);
                else if (expr.StartsWith(">") && int.TryParse(expr.AsSpan(1).Trim(), out var minId))
                    q = q.Where(e => e.Id > minId);
                else if (expr.Contains(":") && int.TryParse(expr.Split(':')[0].Trim(), out var fromId) && int.TryParse(expr.Split(':')[1].Trim(), out var toId))
                    q = q.Where(e => e.Id >= fromId && e.Id <= toId);
                else if (int.TryParse(expr, out var exactId))
                    q = q.Where(e => e.Id == exactId);
            }
            if (!string.IsNullOrWhiteSpace(filterCol_entryDate))
            {
                var dateParts = filterCol_entryDate.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length >= 7).ToList();
                foreach (var part in dateParts)
                {
                    if (part.Length >= 7 && part.Contains("-") && int.TryParse(part.AsSpan(0, 4), out var y) && int.TryParse(part.AsSpan(5, 2), out var m))
                    {
                        var from = new DateTime(y, m, 1, 0, 0, 0);
                        var to = from.AddMonths(1).AddTicks(-1);
                        q = q.Where(e => e.EntryDate >= from && e.EntryDate <= to);
                        break;
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_sourceType))
            {
                var vals = filterCol_sourceType.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                var types = new List<LedgerSourceType>();
                foreach (var v in vals)
                {
                    if (Enum.TryParse<LedgerSourceType>(v, ignoreCase: true, out var t))
                        types.Add(t);
                }
                if (types.Count > 0)
                    q = q.Where(e => types.Contains(e.SourceType));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_voucherNo))
            {
                var vals = filterCol_voucherNo.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    q = q.Where(e => e.VoucherNo != null && vals.Any(v => e.VoucherNo.Contains(v)));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_account))
            {
                var vals = filterCol_account.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    q = q.Where(e => e.Account != null && vals.Any(v => (e.Account.AccountCode != null && e.Account.AccountCode.Contains(v)) || (e.Account.AccountName != null && e.Account.AccountName.Contains(v))));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_debit))
            {
                var vals = filterCol_debit.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                var decimals = vals.Select(x => decimal.TryParse(x, out var d) ? d : (decimal?)null).Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (decimals.Count > 0)
                    q = q.Where(e => e.Debit > 0 && decimals.Contains(e.Debit));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_credit))
            {
                var vals = filterCol_credit.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                var decimals = vals.Select(x => decimal.TryParse(x, out var d) ? d : (decimal?)null).Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (decimals.Count > 0)
                    q = q.Where(e => e.Credit > 0 && decimals.Contains(e.Credit));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_description))
            {
                var vals = filterCol_description.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    q = q.Where(e => e.Description != null && vals.Any(v => e.Description.Contains(v)));
            }

            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            var so = (sort ?? "entryDate").Trim().ToLowerInvariant();
            q = so switch
            {
                "id" => desc ? q.OrderByDescending(e => e.Id) : q.OrderBy(e => e.Id),
                "entrydate" => desc ? q.OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.Id) : q.OrderBy(e => e.EntryDate).ThenBy(e => e.Id),
                "sourcetype" => desc ? q.OrderByDescending(e => e.SourceType) : q.OrderBy(e => e.SourceType),
                "voucherno" => desc ? q.OrderByDescending(e => e.VoucherNo ?? "") : q.OrderBy(e => e.VoucherNo ?? ""),
                "account" => desc ? q.OrderByDescending(e => e.Account != null ? e.Account.AccountCode : "") : q.OrderBy(e => e.Account != null ? e.Account.AccountCode : ""),
                "debit" => desc ? q.OrderByDescending(e => e.Debit) : q.OrderBy(e => e.Debit),
                "credit" => desc ? q.OrderByDescending(e => e.Credit) : q.OrderBy(e => e.Credit),
                "description" => desc ? q.OrderByDescending(e => e.Description ?? "") : q.OrderBy(e => e.Description ?? ""),
                _ => desc ? q.OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.Id) : q.OrderBy(e => e.EntryDate).ThenBy(e => e.Id)
            };

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
            ViewBag.Sort = so;
            ViewBag.Dir = desc ? "desc" : "asc";
            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_IdExpr = filterCol_idExpr;
            ViewBag.FilterCol_EntryDate = filterCol_entryDate;
            ViewBag.FilterCol_SourceType = filterCol_sourceType;
            ViewBag.FilterCol_VoucherNo = filterCol_voucherNo;
            ViewBag.FilterCol_Account = filterCol_account;
            ViewBag.FilterCol_Debit = filterCol_debit;
            ViewBag.FilterCol_Credit = filterCol_credit;
            ViewBag.FilterCol_Description = filterCol_description;

            return View(model);
        }

        /// <summary>جلب القيم المميزة لعمود في كشف حساب عميل (للفلترة بنمط Excel)</summary>
        [HttpGet]
        public async Task<IActionResult> GetColumnValues(int? customerId, string column, string? search = null)
        {
            if (!customerId.HasValue || customerId.Value <= 0)
                return Json(Array.Empty<object>());

            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var q = _context.LedgerEntries
                .AsNoTracking()
                .Include(e => e.Account)
                .Where(e => e.CustomerId == customerId.Value)
                .Where(e => e.Description == null || !e.Description.Contains("عكس ترحيل"));

            List<(string Value, string Display)> items = column?.ToLowerInvariant() switch
            {
                "id" => (await q.Select(e => e.Id).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "entrydate" => (await q.Select(e => new { e.EntryDate.Year, Month = e.EntryDate.Month }).Distinct().OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).Take(100).ToListAsync())
                    .Select(x => ($"{x.Year}-{x.Month:D2}", $"{x.Year}/{x.Month:D2}")).ToList(),
                "sourcetype" => Enum.GetValues<LedgerSourceType>().Select(t => (t.ToString(), SourceTypeDisplayAr(t))).OrderBy(x => x.Item2).ToList(),
                "voucherno" => string.IsNullOrEmpty(searchTerm)
                    ? (await q.Where(e => e.VoucherNo != null).Select(e => e.VoucherNo!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v, v)).ToList()
                    : (await q.Where(e => e.VoucherNo != null && EF.Functions.Like(e.VoucherNo, "%" + searchTerm + "%")).Select(e => e.VoucherNo!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v!, v)).ToList(),
                "account" => string.IsNullOrEmpty(searchTerm)
                    ? (await q.Where(e => e.Account != null).Select(e => e.Account!.AccountCode + " - " + e.Account!.AccountName).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v, v)).ToList()
                    : (await q.Where(e => e.Account != null && (EF.Functions.Like(e.Account.AccountCode, "%" + searchTerm + "%") || EF.Functions.Like(e.Account.AccountName, "%" + searchTerm + "%"))).Select(e => e.Account!.AccountCode + " - " + e.Account!.AccountName).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v!, v)).ToList(),
                "debit" => (await q.Where(e => e.Debit > 0).Select(e => e.Debit).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v.ToString("N2"), v.ToString("N2"))).ToList(),
                "credit" => (await q.Where(e => e.Credit > 0).Select(e => e.Credit).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v.ToString("N2"), v.ToString("N2"))).ToList(),
                "description" => string.IsNullOrEmpty(searchTerm)
                    ? (await q.Where(e => e.Description != null).Select(e => e.Description!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v ?? "", v ?? "")).ToList()
                    : (await q.Where(e => e.Description != null && EF.Functions.Like(e.Description, "%" + searchTerm + "%")).Select(e => e.Description!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v ?? "", v ?? "")).ToList(),
                _ => new List<(string, string)>()
            };

            return Json(items.Select(x => new { value = x.Value, display = x.Display }));
        }
    }
}
