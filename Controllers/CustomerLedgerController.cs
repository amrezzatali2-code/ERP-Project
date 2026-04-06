using System;
using System.Collections.Generic;
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

            if (fromDate.HasValue)
                q = q.Where(e => e.EntryDate >= fromDate.Value.Date);

            if (toDate.HasValue)
                q = q.Where(e => e.EntryDate <= toDate.Value.Date.AddDays(1));

            var rawEntries = await q.ToListAsync();

            bool IsReverse(LedgerEntry e) => (e.Description ?? "").Contains("عكس ترحيل", StringComparison.Ordinal);

            bool IsAlwaysVisibleType(LedgerSourceType t) =>
                t == LedgerSourceType.Opening
                || t == LedgerSourceType.Journal
                || t == LedgerSourceType.Adjustment
                || t == LedgerSourceType.StockTransfer
                || t == LedgerSourceType.StockAdjustment;

            var salesInvoiceIds = rawEntries.Where(e => e.SourceType == LedgerSourceType.SalesInvoice && e.SourceId.HasValue).Select(e => e.SourceId!.Value).Distinct().ToList();
            var salesReturnIds = rawEntries.Where(e => e.SourceType == LedgerSourceType.SalesReturn && e.SourceId.HasValue).Select(e => e.SourceId!.Value).Distinct().ToList();
            var purchaseInvoiceIds = rawEntries.Where(e => e.SourceType == LedgerSourceType.PurchaseInvoice && e.SourceId.HasValue).Select(e => e.SourceId!.Value).Distinct().ToList();
            var purchaseReturnIds = rawEntries.Where(e => e.SourceType == LedgerSourceType.PurchaseReturn && e.SourceId.HasValue).Select(e => e.SourceId!.Value).Distinct().ToList();
            var receiptIds = rawEntries.Where(e => e.SourceType == LedgerSourceType.Receipt && e.SourceId.HasValue).Select(e => e.SourceId!.Value).Distinct().ToList();
            var paymentIds = rawEntries.Where(e => e.SourceType == LedgerSourceType.Payment && e.SourceId.HasValue).Select(e => e.SourceId!.Value).Distinct().ToList();
            var debitNoteIds = rawEntries.Where(e => e.SourceType == LedgerSourceType.DebitNote && e.SourceId.HasValue).Select(e => e.SourceId!.Value).Distinct().ToList();
            var creditNoteIds = rawEntries.Where(e => e.SourceType == LedgerSourceType.CreditNote && e.SourceId.HasValue).Select(e => e.SourceId!.Value).Distinct().ToList();

            var existingSalesInvoices = new HashSet<int>(await _context.SalesInvoices.AsNoTracking().Where(x => salesInvoiceIds.Contains(x.SIId)).Select(x => x.SIId).ToListAsync());
            var existingSalesReturns = new HashSet<int>(await _context.SalesReturns.AsNoTracking().Where(x => salesReturnIds.Contains(x.SRId)).Select(x => x.SRId).ToListAsync());
            var existingPurchaseInvoices = new HashSet<int>(await _context.PurchaseInvoices.AsNoTracking().Where(x => purchaseInvoiceIds.Contains(x.PIId)).Select(x => x.PIId).ToListAsync());
            var existingPurchaseReturns = new HashSet<int>(await _context.PurchaseReturns.AsNoTracking().Where(x => purchaseReturnIds.Contains(x.PRetId)).Select(x => x.PRetId).ToListAsync());
            var existingReceipts = new HashSet<int>(await _context.CashReceipts.AsNoTracking().Where(x => receiptIds.Contains(x.CashReceiptId)).Select(x => x.CashReceiptId).ToListAsync());
            var existingPayments = new HashSet<int>(await _context.CashPayments.AsNoTracking().Where(x => paymentIds.Contains(x.CashPaymentId)).Select(x => x.CashPaymentId).ToListAsync());
            var existingDebitNotes = new HashSet<int>(await _context.DebitNotes.AsNoTracking().Where(x => debitNoteIds.Contains(x.DebitNoteId)).Select(x => x.DebitNoteId).ToListAsync());
            var existingCreditNotes = new HashSet<int>(await _context.CreditNotes.AsNoTracking().Where(x => creditNoteIds.Contains(x.CreditNoteId)).Select(x => x.CreditNoteId).ToListAsync());

            bool SourceExists(LedgerEntry e)
            {
                if (!e.SourceId.HasValue) return true;
                if (IsAlwaysVisibleType(e.SourceType)) return true;

                return e.SourceType switch
                {
                    LedgerSourceType.SalesInvoice => existingSalesInvoices.Contains(e.SourceId.Value),
                    LedgerSourceType.SalesReturn => existingSalesReturns.Contains(e.SourceId.Value),
                    LedgerSourceType.PurchaseInvoice => existingPurchaseInvoices.Contains(e.SourceId.Value),
                    LedgerSourceType.PurchaseReturn => existingPurchaseReturns.Contains(e.SourceId.Value),
                    LedgerSourceType.Receipt => existingReceipts.Contains(e.SourceId.Value),
                    LedgerSourceType.Payment => existingPayments.Contains(e.SourceId.Value),
                    LedgerSourceType.DebitNote => existingDebitNotes.Contains(e.SourceId.Value),
                    LedgerSourceType.CreditNote => existingCreditNotes.Contains(e.SourceId.Value),
                    _ => false
                };
            }

            var entriesBySource = rawEntries
                .Where(e => e.SourceId.HasValue && !IsAlwaysVisibleType(e.SourceType))
                .GroupBy(e => (e.SourceType, SourceId: e.SourceId!.Value))
                .ToDictionary(g => g.Key, g => g.ToList());

            var filteredEntriesList = new List<LedgerEntry>();
            foreach (var entry in rawEntries)
            {
                if (!entry.SourceId.HasValue || IsAlwaysVisibleType(entry.SourceType))
                {
                    filteredEntriesList.Add(entry);
                    continue;
                }

                var key = (entry.SourceType, SourceId: entry.SourceId.Value);
                if (!entriesBySource.TryGetValue(key, out var sourceEntries))
                    continue;

                var sourceExists = SourceExists(entry);
                if (!sourceExists)
                {
                    if (IsReverse(entry))
                        filteredEntriesList.Add(entry);

                    continue;
                }

                if (IsReverse(entry))
                    continue;

                var lastStage = sourceEntries
                    .Where(x => !IsReverse(x))
                    .Select(x => x.PostVersion)
                    .DefaultIfEmpty(0)
                    .Max();

                if (entry.PostVersion == lastStage)
                    filteredEntriesList.Add(entry);
            }

            var filteredEntries = filteredEntriesList.AsQueryable();

            // فلاتر أعمدة بنمط Excel
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var id) ? id : (int?)null).Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    filteredEntries = filteredEntries.Where(e => ids.Contains(e.Id));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_idExpr))
            {
                var expr = filterCol_idExpr.Trim();
                if (expr.StartsWith("<") && int.TryParse(expr.AsSpan(1).Trim(), out var maxId))
                    filteredEntries = filteredEntries.Where(e => e.Id < maxId);
                else if (expr.StartsWith(">") && int.TryParse(expr.AsSpan(1).Trim(), out var minId))
                    filteredEntries = filteredEntries.Where(e => e.Id > minId);
                else if (expr.Contains(":") && int.TryParse(expr.Split(':')[0].Trim(), out var fromId) && int.TryParse(expr.Split(':')[1].Trim(), out var toId))
                    filteredEntries = filteredEntries.Where(e => e.Id >= fromId && e.Id <= toId);
                else if (int.TryParse(expr, out var exactId))
                    filteredEntries = filteredEntries.Where(e => e.Id == exactId);
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
                        filteredEntries = filteredEntries.Where(e => e.EntryDate >= from && e.EntryDate <= to);
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
                    filteredEntries = filteredEntries.Where(e => types.Contains(e.SourceType));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_voucherNo))
            {
                var vals = filterCol_voucherNo.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    filteredEntries = filteredEntries.Where(e => e.VoucherNo != null && vals.Any(v => e.VoucherNo.Contains(v)));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_account))
            {
                var vals = filterCol_account.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    filteredEntries = filteredEntries.Where(e => e.Account != null && vals.Any(v => (e.Account.AccountCode != null && e.Account.AccountCode.Contains(v)) || (e.Account.AccountName != null && e.Account.AccountName.Contains(v))));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_debit))
            {
                var vals = filterCol_debit.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                var decimals = vals.Select(x => decimal.TryParse(x, out var d) ? d : (decimal?)null).Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (decimals.Count > 0)
                    filteredEntries = filteredEntries.Where(e => e.Debit > 0 && decimals.Contains(e.Debit));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_credit))
            {
                var vals = filterCol_credit.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                var decimals = vals.Select(x => decimal.TryParse(x, out var d) ? d : (decimal?)null).Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (decimals.Count > 0)
                    filteredEntries = filteredEntries.Where(e => e.Credit > 0 && decimals.Contains(e.Credit));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_description))
            {
                var vals = filterCol_description.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    filteredEntries = filteredEntries.Where(e => e.Description != null && vals.Any(v => e.Description.Contains(v)));
            }

            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            var so = (sort ?? "entryDate").Trim().ToLowerInvariant();
            filteredEntries = so switch
            {
                "id" => desc ? filteredEntries.OrderByDescending(e => e.Id) : filteredEntries.OrderBy(e => e.Id),
                "entrydate" => desc ? filteredEntries.OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.Id) : filteredEntries.OrderBy(e => e.EntryDate).ThenBy(e => e.Id),
                "sourcetype" => desc ? filteredEntries.OrderByDescending(e => e.SourceType) : filteredEntries.OrderBy(e => e.SourceType),
                "voucherno" => desc ? filteredEntries.OrderByDescending(e => e.VoucherNo ?? "") : filteredEntries.OrderBy(e => e.VoucherNo ?? ""),
                "account" => desc ? filteredEntries.OrderByDescending(e => e.Account != null ? e.Account.AccountCode : "") : filteredEntries.OrderBy(e => e.Account != null ? e.Account.AccountCode : ""),
                "debit" => desc ? filteredEntries.OrderByDescending(e => e.Debit) : filteredEntries.OrderBy(e => e.Debit),
                "credit" => desc ? filteredEntries.OrderByDescending(e => e.Credit) : filteredEntries.OrderBy(e => e.Credit),
                "description" => desc ? filteredEntries.OrderByDescending(e => e.Description ?? "") : filteredEntries.OrderBy(e => e.Description ?? ""),
                _ => desc ? filteredEntries.OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.Id) : filteredEntries.OrderBy(e => e.EntryDate).ThenBy(e => e.Id)
            };

            var filteredList = filteredEntries.ToList();
            decimal totalDebit = filteredList.Sum(e => e.Debit);
            decimal totalCredit = filteredList.Sum(e => e.Credit);
            decimal netBalance = totalDebit - totalCredit;

            var pageNumber = page < 1 ? 1 : page;
            var safePageSize = pageSize <= 0 ? 50 : pageSize;
            var pageItems = filteredList.Skip((pageNumber - 1) * safePageSize).Take(safePageSize).ToList();
            var model = new PagedResult<LedgerEntry>(pageItems, pageNumber, safePageSize, filteredList.Count);

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
