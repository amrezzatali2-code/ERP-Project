using System;                                     // متغيرات التاريخ DateTime
using System.Collections.Generic;                 // Dictionary, List
using System.Globalization;                       // تنسيق التواريخ عند التصدير
using System.Linq;                                // LINQ: Where / OrderBy
using System.Linq.Expressions;                    // Expression<Func<...>>
using System.Text;                                // StringBuilder للتصدير
using System.Threading.Tasks;                     // async / await
using Microsoft.AspNetCore.Mvc;                   // Controller, IActionResult
using Microsoft.AspNetCore.Mvc.Rendering;         // SelectList للقوائم المنسدلة
using Microsoft.EntityFrameworkCore;              // AsNoTracking, Include, ToListAsync
using ERP.Data;                                   // AppDbContext الاتصال بقاعدة البيانات
using ERP.Filters;
using ERP.Infrastructure;                         // PagedResult + ApplySearchSort + UserActivityLogger
using ERP.Models;                                 // DebitNote, UserActionType...
using ERP.Security;
using ERP.Services;                               // ILedgerPostingService

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إشعارات الخصم (DebitNotes)
    /// - CRUD (إنشاء/تعديل/تفاصيل/حذف).
    /// - زر حفظ وترحيل = حفظ + ترحيل محاسبي (LedgerEntries + تحديث حساب العميل + الأرباح).
    /// </summary>
    [RequirePermission("DebitNotes.Index")]
    public class DebitNotesController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ILedgerPostingService _ledgerPostingService;
        private readonly IUserActivityLogger _activityLogger;
        private readonly IPermissionService _permissionService;
        private readonly IUserAccountVisibilityService _accountVisibilityService;

        private const string InvestorAccountCode = "3101";

        public DebitNotesController(
            AppDbContext context,
            ILedgerPostingService ledgerPostingService,
            IUserActivityLogger activityLogger,
            IPermissionService permissionService,
            IUserAccountVisibilityService accountVisibilityService)
        {
            _context = context;
            _ledgerPostingService = ledgerPostingService;
            _activityLogger = activityLogger;
            _permissionService = permissionService;
            _accountVisibilityService = accountVisibilityService;
        }

        private static Task<bool> CanViewInvestorsAsync() => Task.FromResult(true); // إظهار/إخفاء 3101 يعتمد على «الحسابات المسموح رؤيتها» فقط

        // =========================================================
        // دالة مساعدة: تحميل القوائم المنسدلة (الطرف + الحسابات)
        // =========================================================
        private void PopulateLookups(
            int? customerId = null,
            int? accountId = null,
            int? offsetAccountId = null,
            bool canViewInvestors = true)
        {
            ViewData["CustomerId"] = new SelectList(
                _context.Customers.AsNoTracking().Where(c => c.IsActive == true).OrderBy(c => c.CustomerName),
                "CustomerId", "CustomerName", customerId);

            var accountsQ = _context.Accounts.AsNoTracking().OrderBy(a => a.AccountName).AsQueryable();
            if (!canViewInvestors)
                accountsQ = accountsQ.Where(a => a.AccountCode != InvestorAccountCode);

            ViewData["AccountId"] = new SelectList(accountsQ, "AccountId", "AccountName", accountId);
            ViewData["OffsetAccountId"] = new SelectList(accountsQ, "AccountId", "AccountName", offsetAccountId);
        }

        // =========================================================
        // دالة خاصة: تجهيز الاستعلام الأساسي + الفلاتر + البحث + الترتيب
        // تُستخدم في Index و Export حتى لا نكرر الكود.
        // =========================================================
        private IQueryable<DebitNote> BuildQuery(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            int? fromCode,
            int? toCode,
            bool canViewInvestors,
            List<int> hiddenCustomerAccountIds,
            bool restrictedToAllowedOnly)
        {
            // (1) الاستعلام الأساسي من جدول إشعارات الخصم مع تحميل العميل والحسابات
            IQueryable<DebitNote> q = _context.DebitNotes
                .AsNoTracking()
                .Include(d => d.Customer)
                .Include(d => d.Account)
                .Include(d => d.OffsetAccount);

            if (!canViewInvestors)
                q = q.Where(d => d.Account != null && d.Account.AccountCode != InvestorAccountCode
                                 && (d.OffsetAccount == null || d.OffsetAccount.AccountCode != InvestorAccountCode));

            // إخفاء أي إشعار مرتبط بعميل غير ظاهر (حساب رئيسي أو قيود بحساب مسموح)
            if (hiddenCustomerAccountIds != null && hiddenCustomerAccountIds.Count > 0)
            {
                q = restrictedToAllowedOnly
                    ? q.Where(d => (d.Customer != null && d.Customer.AccountId != null && !hiddenCustomerAccountIds.Contains(d.Customer.AccountId.Value))
                        || (d.Customer != null && (d.Customer.PartyCategory == "Customer" || d.Customer.PartyCategory == "Supplier")
                            && d.CustomerId != null && _context.LedgerEntries.Any(e => e.CustomerId == d.CustomerId && !hiddenCustomerAccountIds.Contains(e.AccountId))))
                    : q.Where(d => d.Customer == null || d.Customer.AccountId == null || !hiddenCustomerAccountIds.Contains(d.Customer.AccountId.Value)
                        || (d.Customer != null && (d.Customer.PartyCategory == "Customer" || d.Customer.PartyCategory == "Supplier")
                            && d.CustomerId != null && _context.LedgerEntries.Any(e => e.CustomerId == d.CustomerId && !hiddenCustomerAccountIds.Contains(e.AccountId))));
            }

            // (2) فلتر كود من/إلى (نعتمد هنا على DebitNoteId كرقم الإشعار)
            if (fromCode.HasValue)
                q = q.Where(d => d.DebitNoteId >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(d => d.DebitNoteId <= toCode.Value);

            // (3) فلتر التاريخ: نفلتر حسب تاريخ الإشعار NoteDate
            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                DateTime from = fromDate.Value;
                DateTime to = toDate.Value;

                q = q.Where(d => d.NoteDate >= from && d.NoteDate <= to);
            }

            // (4) خرائط البحث: نحدد الأعمدة النصية والرقمية للبحث الموحد

            // الحقول النصية (string) التي يمكن البحث فيها
            var stringFields =
                new Dictionary<string, Expression<Func<DebitNote, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["reason"] = d => d.Reason ?? "",                                  // سبب الإشعار
                    ["desc"] = d => d.Description ?? "",                             // البيان
                    ["customer"] = d => d.Customer != null ? d.Customer.CustomerName : "", // اسم العميل/الطرف
                    ["account"] = d => d.Account != null ? d.Account.AccountName : "",   // اسم حساب الطرف
                    ["offset"] = d => d.OffsetAccount != null ? d.OffsetAccount.AccountName : "" // الحساب المقابل
                };

            // الحقول الرقمية (int) التي يمكن البحث فيها
            var intFields =
                new Dictionary<string, Expression<Func<DebitNote, int>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = d => d.DebitNoteId,
                    ["number"] = d => d.DebitNoteId   // رقم المستند = رقم الإشعار
                };

            // الحقول المسموح الترتيب عليها
            var orderFields =
                new Dictionary<string, Expression<Func<DebitNote, object>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["DebitNoteId"] = d => d.DebitNoteId,
                    ["NoteDate"] = d => d.NoteDate,
                    ["Amount"] = d => d.Amount,
                    ["CustomerName"] = d => d.Customer != null ? d.Customer.CustomerName : "",
                    ["AccountName"] = d => d.Account != null ? d.Account.AccountName : "",
                    ["OffsetAccountName"] = d => d.OffsetAccount != null ? d.OffsetAccount.AccountName : "",
                    ["IsPosted"] = d => d.IsPosted,
                    ["CreatedAt"] = d => d.CreatedAt,
                    ["UpdatedAt"] = d => d.UpdatedAt ?? DateTime.MinValue,
                    ["Reason"] = d => d.Reason ?? "",
                    ["Description"] = d => d.Description ?? ""
                };

            // (5) تطبيق منظومة البحث/الترتيب الموحدة
            q = q.ApplySearchSort(
                search: search,
                searchBy: searchBy,
                sort: sort,
                dir: dir,
                stringFields: stringFields,
                intFields: intFields,
                orderFields: orderFields,
                defaultSearchBy: "all",          // لو المستخدم لم يحدد نوع البحث
                defaultSortBy: "NoteDate"      // الترتيب الافتراضي بتاريخ الإشعار (الأحدث أولاً)
            );

            return q;
        }

        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        private static IQueryable<DebitNote> ApplyColumnFilters(
            IQueryable<DebitNote> query,
            string? filterCol_id,
            string? filterCol_number,
            string? filterCol_date,
            string? filterCol_customer,
            string? filterCol_account,
            string? filterCol_offset,
            string? filterCol_amount,
            string? filterCol_posted,
            string? filterCol_reason,
            string? filterCol_desc)
        {
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) query = query.Where(d => ids.Contains(d.DebitNoteId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_number))
            {
                var ids = filterCol_number.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) query = query.Where(d => ids.Contains(d.DebitNoteId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_date))
            {
                var parts = filterCol_date.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length >= 8).ToList();
                if (parts.Count > 0)
                {
                    var dates = new List<DateTime>();
                    foreach (var p in parts)
                        if (DateTime.TryParse(p, out var dt)) dates.Add(dt.Date);
                    if (dates.Count > 0) query = query.Where(d => dates.Contains(d.NoteDate.Date));
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_customer))
            {
                var vals = filterCol_customer.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    query = query.Where(d => d.Customer != null && vals.Contains(d.Customer.CustomerName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_account))
            {
                var vals = filterCol_account.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    query = query.Where(d => d.Account != null && vals.Contains(d.Account.AccountName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_offset))
            {
                var vals = filterCol_offset.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    query = query.Where(d => d.OffsetAccount != null && vals.Contains(d.OffsetAccount.AccountName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_amount))
            {
                var vals = filterCol_amount.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0) query = query.Where(d => vals.Contains(d.Amount));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_posted))
            {
                var vals = filterCol_posted.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim().ToLowerInvariant()).Where(x => x == "true" || x == "1" || x == "مرحّل" || x == "false" || x == "0" || x == "مسودة").ToList();
                if (vals.Count > 0)
                {
                    var postTrue = vals.Any(v => v == "true" || v == "1" || v == "مرحّل");
                    var postFalse = vals.Any(v => v == "false" || v == "0" || v == "مسودة");
                    if (postTrue && !postFalse) query = query.Where(d => d.IsPosted);
                    else if (postFalse && !postTrue) query = query.Where(d => !d.IsPosted);
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_reason))
            {
                var vals = filterCol_reason.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    query = query.Where(d => d.Reason != null && vals.Any(v => d.Reason.Contains(v)));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_desc))
            {
                var vals = filterCol_desc.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    query = query.Where(d => d.Description != null && vals.Any(v => d.Description.Contains(v)));
            }
            return query;
        }

        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var columnLower = (column ?? "").Trim().ToLowerInvariant();
            var q = _context.DebitNotes.AsNoTracking()
                .Include(d => d.Customer)
                .Include(d => d.Account)
                .Include(d => d.OffsetAccount);

            if (columnLower == "id" || columnLower == "number")
            {
                var ids = await q.Select(d => d.DebitNoteId).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "date")
            {
                var dates = await q.Select(d => d.NoteDate.Date).Distinct().OrderByDescending(x => x).Take(500).ToListAsync();
                return Json(dates.Select(d => new { value = d.ToString("yyyy-MM-dd"), display = d.ToString("yyyy-MM-dd") }));
            }
            if (columnLower == "customer" || columnLower == "customername")
            {
                var list = await q.Where(d => d.Customer != null).Select(d => d.Customer!.CustomerName).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "account" || columnLower == "accountname")
            {
                var list = await q.Where(d => d.Account != null).Select(d => d.Account!.AccountName).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "offset" || columnLower == "offsetaccountname")
            {
                var list = await q.Where(d => d.OffsetAccount != null).Select(d => d.OffsetAccount!.AccountName).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "amount")
            {
                var list = await q.Select(d => d.Amount).Distinct().OrderBy(x => x).Take(300).ToListAsync();
                return Json(list.Select(v => new { value = v.ToString(CultureInfo.InvariantCulture), display = v.ToString("0.00") }));
            }
            if (columnLower == "posted" || columnLower == "isposted")
            {
                return Json(new[] { new { value = "true", display = "مرحّل" }, new { value = "false", display = "مسودة" } });
            }
            if (columnLower == "created" || columnLower == "createdat")
            {
                var list = await q.Select(d => d.CreatedAt).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd HH:mm"), display = d.ToString("yyyy-MM-dd HH:mm") }));
            }
            if (columnLower == "updated" || columnLower == "updatedat")
            {
                var list = await q.Where(d => d.UpdatedAt.HasValue).Select(d => d.UpdatedAt!.Value).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd HH:mm"), display = d.ToString("yyyy-MM-dd HH:mm") }));
            }
            if (columnLower == "reason")
            {
                var list = await q.Where(d => d.Reason != null && d.Reason != "").Select(d => d.Reason!).Distinct().OrderBy(x => x).Take(300).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v.Length > 50 ? v.Substring(0, 50) + "…" : v }));
            }
            if (columnLower == "desc" || columnLower == "description")
            {
                var list = await q.Where(d => d.Description != null && d.Description != "").Select(d => d.Description!).Distinct().OrderBy(x => x).Take(300).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v.Length > 50 ? v.Substring(0, 50) + "…" : v }));
            }
            return Json(Array.Empty<object>());
        }

        // =========================================================
        // Index — عرض قائمة إشعارات الخصم (نظام القوائم الموحد)
        // =========================================================
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",
            string? sort = "NoteDate",
            string? dir = "desc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_number = null,
            string? filterCol_date = null,
            string? filterCol_customer = null,
            string? filterCol_account = null,
            string? filterCol_offset = null,
            string? filterCol_amount = null,
            string? filterCol_posted = null,
            string? filterCol_reason = null,
            string? filterCol_desc = null,
            int page = 1,
            int pageSize = 50)
        {
            var canViewInvestors = await CanViewInvestorsAsync();
            var hiddenAccountIds = await _accountVisibilityService.GetHiddenAccountIdsForCurrentUserAsync();
            var hiddenList = hiddenAccountIds.ToList();
            var restrictedOnly = await _accountVisibilityService.IsRestrictedToAllowedAccountsOnlyAsync();
            var q = BuildQuery(
                search,
                searchBy,
                sort,
                dir,
                useDateRange,
                fromDate,
                toDate,
                fromCode,
                toCode,
                canViewInvestors,
                hiddenList,
                restrictedOnly);

            q = ApplyColumnFilters(q, filterCol_id, filterCol_number, filterCol_date, filterCol_customer, filterCol_account, filterCol_offset, filterCol_amount, filterCol_posted, filterCol_reason, filterCol_desc);

            var totalAmount = await q.Select(d => (decimal?)d.Amount).SumAsync() ?? 0m;
            var model = await PagedResult<DebitNote>.CreateAsync(q, page, pageSize);

            model.UseDateRange = useDateRange;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            ViewBag.Search = search ?? "";
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.Sort = sort ?? "NoteDate";
            ViewBag.Dir = (dir?.ToLower() == "asc") ? "asc" : "desc";
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_Number = filterCol_number;
            ViewBag.FilterCol_Date = filterCol_date;
            ViewBag.FilterCol_Customer = filterCol_customer;
            ViewBag.FilterCol_Account = filterCol_account;
            ViewBag.FilterCol_Offset = filterCol_offset;
            ViewBag.FilterCol_Amount = filterCol_amount;
            ViewBag.FilterCol_Posted = filterCol_posted;
            ViewBag.FilterCol_Reason = filterCol_reason;
            ViewBag.FilterCol_Desc = filterCol_desc;
            ViewBag.DateField = "NoteDate";
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalCount = model.TotalCount;
            ViewBag.TotalAmount = totalAmount;

            return View(model);
        }

        // =========================================================
        // Show — عرض تفاصيل إشعار خصم واحد (قراءة فقط)
        // نستخدمه في زر "عرض" في الجدول، ونعتمد على نفس View بتاع Details لو حابب.
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Show(int id)
        {
            if (id <= 0)
                return BadRequest(); // رقم غير صحيح

            var debitNote = await _context.DebitNotes
                                          .AsNoTracking()
                                          .Include(d => d.Customer)
                                          .Include(d => d.Account)
                                          .Include(d => d.OffsetAccount)
                                          .FirstOrDefaultAsync(d => d.DebitNoteId == id);

            if (debitNote == null)
                return NotFound();

            if (!await CanViewInvestorsAsync() &&
                debitNote.Account != null &&
                debitNote.Account.AccountCode == InvestorAccountCode)
                return NotFound();

            if (!await CanViewInvestorsAsync() &&
                debitNote.OffsetAccount != null &&
                debitNote.OffsetAccount.AccountCode == InvestorAccountCode)
                return NotFound();

            return View(debitNote); // Views/DebitNotes/Show.cshtml (نعمله لاحقاً لو حابب)
        }

        // =========================================================
        // Export — تصدير قائمة إشعارات الخصم إلى CSV (يفتح في Excel)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy = "all",
            string? sort = "NoteDate",
            string? dir = "desc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_number = null,
            string? filterCol_date = null,
            string? filterCol_customer = null,
            string? filterCol_account = null,
            string? filterCol_offset = null,
            string? filterCol_amount = null,
            string? filterCol_posted = null,
            string? filterCol_reason = null,
            string? filterCol_desc = null,
            string format = "excel")
        {
            var canViewInvestors = await CanViewInvestorsAsync();
            var hiddenAccountIds = await _accountVisibilityService.GetHiddenAccountIdsForCurrentUserAsync();
            var hiddenList = hiddenAccountIds.ToList();
            var restrictedOnly = await _accountVisibilityService.IsRestrictedToAllowedAccountsOnlyAsync();
            var q = BuildQuery(
                search,
                searchBy,
                sort,
                dir,
                useDateRange,
                fromDate,
                toDate,
                fromCode,
                toCode,
                canViewInvestors,
                hiddenList,
                restrictedOnly);
            q = ApplyColumnFilters(q, filterCol_id, filterCol_number, filterCol_date, filterCol_customer, filterCol_account, filterCol_offset, filterCol_amount, filterCol_posted, filterCol_reason, filterCol_desc);

            var list = await q.ToListAsync();

            var sb = new StringBuilder();

            // عناوين الأعمدة في ملف CSV
            sb.AppendLine("DebitNoteId,NoteDate,CustomerId,CustomerName,AccountId,AccountName,OffsetAccountId,OffsetAccountName,Amount,Reason,Description,IsPosted,CreatedAt,UpdatedAt,CreatedBy,PostedAt,PostedBy");

            // كل إشعار في سطر CSV
            foreach (var d in list)
            {
                string customerName = d.Customer?.CustomerName ?? "";
                string accountName = d.Account?.AccountName ?? "";
                string offsetAccountName = d.OffsetAccount?.AccountName ?? "";

                string line = string.Join(",",
                    d.DebitNoteId,
                    d.NoteDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    d.CustomerId?.ToString() ?? "",
                    customerName.Replace(",", " "),
                    d.AccountId,
                    accountName.Replace(",", " "),
                    d.OffsetAccountId?.ToString() ?? "",
                    offsetAccountName.Replace(",", " "),
                    d.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                    (d.Reason ?? "").Replace(",", " "),
                    (d.Description ?? "").Replace(",", " "),
                    d.IsPosted ? "1" : "0",
                    d.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    d.UpdatedAt.HasValue
                        ? d.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                        : "",
                    (d.CreatedBy ?? "").Replace(",", " "),
                    d.PostedAt.HasValue
                        ? d.PostedAt.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                        : "",
                    (d.PostedBy ?? "").Replace(",", " ")
                );

                sb.AppendLine(line);
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = "DebitNotes.csv";
            const string contentType = "text/csv";

            return File(bytes, contentType, fileName);
        }

        // =========================================================
        // BulkDelete — حذف مجموعة من إشعارات الخصم المحددة
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(int[] ids)
        {
            // لو المستخدم لم يحدد أى إشعار
            if (ids == null || ids.Length == 0)
            {
                TempData["DebitNoteError"] = "لم يتم اختيار أى إشعار للحذف.";
                return RedirectToAction(nameof(Index));
            }

            // نحضر الإشعارات المطابقة للأرقام المختارة
            var notes = await _context.DebitNotes
                                      .Where(d => ids.Contains(d.DebitNoteId))
                                      .ToListAsync();

            if (notes.Count == 0)
            {
                TempData["DebitNoteError"] = "لم يتم العثور على الإشعارات المحددة.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                string? postedBy = User?.Identity?.Name ?? "System";
                foreach (var note in notes.Where(n => n.IsPosted))
                {
                    await _ledgerPostingService.ReverseForHeaderDeleteAsync(LedgerSourceType.DebitNote, note.DebitNoteId, postedBy, "حذف جماعي إشعار خصم");
                }
                _context.DebitNotes.RemoveRange(notes);
                await _context.SaveChangesAsync();

                TempData["DebitNoteSuccess"] = $"تم حذف {notes.Count} من إشعارات الخصم المحددة.";
            }
            catch (Exception ex)
            {
                TempData["DebitNoteError"] = $"لا يمكن حذف بعض الإشعارات: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // DeleteAll — حذف جميع إشعارات الخصم
        // يُفضل استخدامه في بيئة تجريبية فقط.
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _context.DebitNotes.ToListAsync();

            if (all.Count == 0)
            {
                TempData["DebitNoteError"] = "لا توجد إشعارات خصم لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                string? postedBy = User?.Identity?.Name ?? "System";
                foreach (var note in all.Where(n => n.IsPosted))
                {
                    await _ledgerPostingService.ReverseForHeaderDeleteAsync(LedgerSourceType.DebitNote, note.DebitNoteId, postedBy, "حذف جميع إشعارات الخصم");
                }
                _context.DebitNotes.RemoveRange(all);
                await _context.SaveChangesAsync();

                TempData["DebitNoteSuccess"] = "تم حذف جميع إشعارات الخصم.";
            }
            catch (Exception ex)
            {
                TempData["DebitNoteError"] = $"لا يمكن حذف جميع الإشعارات: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // جلب حساب الطرف تلقائياً عند اختيار العميل (للواجهة)
        // =========================================================
        public async Task<IActionResult> GetCustomerAccount(int customerId)
        {
            var customer = await _context.Customers
                .AsNoTracking()
                .Where(c => c.CustomerId == customerId)
                .Select(c => new { c.AccountId })
                .FirstOrDefaultAsync();

            if (customer == null || !customer.AccountId.HasValue)
            {
                return Json(new { success = false, message = "العميل غير موجود أو غير مربوط بحساب محاسبي." });
            }

            return Json(new { success = true, accountId = customer.AccountId.Value });
        }

        // =========================================================
        // ===== CRUD القياسي: Details / Create / Edit / Delete =====
        // =========================================================

        // GET: DebitNotes/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
                return NotFound();

            var debitNote = await _context.DebitNotes
                                          .Include(d => d.Customer)
                                          .Include(d => d.Account)
                                          .Include(d => d.OffsetAccount)
                                          .FirstOrDefaultAsync(m => m.DebitNoteId == id);
            if (debitNote == null)
                return NotFound();

            return View(debitNote);
        }

        // GET: DebitNotes/Create
        public async Task<IActionResult> Create(int? customerId = null)
        {
            var model = new DebitNote
            {
                NoteDate = DateTime.Now,
                IsPosted = false
            };
            if (customerId.HasValue && customerId.Value > 0)
            {
                var customer = await _context.Customers
                    .AsNoTracking()
                    .Where(c => c.CustomerId == customerId.Value)
                    .Select(c => new { c.CustomerId, c.AccountId })
                    .FirstOrDefaultAsync();
                if (customer != null)
                {
                    model.CustomerId = customer.CustomerId;
                    model.AccountId = customer.AccountId ?? 0;
                    ViewBag.LockCustomer = true;
                }
            }
            var canViewInvestors = await CanViewInvestorsAsync();
            PopulateLookups(model.CustomerId, model.AccountId > 0 ? model.AccountId : null, null, canViewInvestors);
            return View(model);
        }

        // POST: DebitNotes/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(DebitNote debitNote)
        {
            // إن لم يُرسل حساب الطرف وتم اختيار عميل، نملأه من حساب العميل (نفس نمط إذن الاستلام)
            if (debitNote.AccountId <= 0 && debitNote.CustomerId.HasValue)
            {
                var cust = await _context.Customers
                    .AsNoTracking()
                    .Where(c => c.CustomerId == debitNote.CustomerId.Value)
                    .Select(c => new { c.AccountId })
                    .FirstOrDefaultAsync();
                if (cust?.AccountId != null)
                {
                    debitNote.AccountId = cust.AccountId.Value;
                    ModelState.Remove("AccountId");
                }
            }

            if (debitNote.AccountId <= 0)
                ModelState.AddModelError(nameof(DebitNote.AccountId), "حساب الطرف مطلوب.");

            if (ModelState.IsValid)
            {
                debitNote.CreatedAt = DateTime.Now;
                debitNote.UpdatedAt = null;
                debitNote.IsPosted = false;
                debitNote.PostedAt = null;
                debitNote.IsLocked = true; // غلق الإشعار بعد الحفظ
                if (string.IsNullOrEmpty(debitNote.CreatedBy))
                    debitNote.CreatedBy = User?.Identity?.Name ?? "System";

                _context.Add(debitNote);
                await _context.SaveChangesAsync();

                await _activityLogger.LogAsync(UserActionType.Create, "DebitNote", debitNote.DebitNoteId, $"إنشاء إشعار خصم رقم {debitNote.DebitNoteId}");

                try
                {
                    await _ledgerPostingService.PostDebitNoteAsync(debitNote.DebitNoteId, User?.Identity?.Name ?? "System");
                    TempData["DebitNoteSuccess"] = "تم حفظ وترحيل إشعار الخصم بنجاح.";
                }
                catch (Exception ex)
                {
                    TempData["DebitNoteError"] = TempData["ErrorMessage"] = $"تم الحفظ، لكن فشل الترحيل: {ex.Message}";
                }
                return RedirectToAction(nameof(Edit), new { id = debitNote.DebitNoteId });
            }

            var canViewInvestors = await CanViewInvestorsAsync();
            PopulateLookups(debitNote.CustomerId, debitNote.AccountId, debitNote.OffsetAccountId, canViewInvestors);
            return View(debitNote);
        }

        // GET: DebitNotes/Unlock/5 — فتح الإشعار للتعديل (سيُضاف التحقق من الصلاحية لاحقاً)
        public async Task<IActionResult> Unlock(int? id)
        {
            if (id == null)
                return NotFound();

            var debitNote = await _context.DebitNotes.FindAsync(id);
            if (debitNote == null)
                return NotFound();

            debitNote.IsLocked = false;
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Edit), new { id });
        }

        // GET: DebitNotes/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
                return NotFound();

            var debitNote = await _context.DebitNotes.FindAsync(id);
            if (debitNote == null)
                return NotFound();

            var canViewInvestors = await CanViewInvestorsAsync();
            PopulateLookups(debitNote.CustomerId, debitNote.AccountId, debitNote.OffsetAccountId, canViewInvestors);
            return View(debitNote);
        }

        // POST: DebitNotes/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, DebitNote input)
        {
            if (id != input.DebitNoteId)
                return NotFound();

            if (input.AccountId <= 0 && input.CustomerId.HasValue)
            {
                var cust = await _context.Customers
                    .AsNoTracking()
                    .Where(c => c.CustomerId == input.CustomerId.Value)
                    .Select(c => new { c.AccountId })
                    .FirstOrDefaultAsync();
                if (cust?.AccountId != null)
                {
                    input.AccountId = cust.AccountId.Value;
                    ModelState.Remove("AccountId");
                }
            }
            if (input.AccountId <= 0)
                ModelState.AddModelError(nameof(DebitNote.AccountId), "حساب الطرف مطلوب.");

            if (!ModelState.IsValid)
            {
                var canViewInvestors = await CanViewInvestorsAsync();
                PopulateLookups(input.CustomerId, input.AccountId, null, canViewInvestors);
                return View(input);
            }

            var existing = await _context.DebitNotes.FindAsync(id);
            if (existing == null)
                return NotFound();

            var oldValues = System.Text.Json.JsonSerializer.Serialize(new { existing.NoteDate, existing.CustomerId, existing.AccountId, existing.Amount });
            existing.NoteDate = input.NoteDate;
            existing.CustomerId = input.CustomerId;
            existing.AccountId = input.AccountId;
            existing.Amount = input.Amount;
            existing.Reason = input.Reason;
            existing.Description = input.Description;
            existing.UpdatedAt = DateTime.Now;
            existing.IsLocked = true;

            try
            {
                string? postedBy = User?.Identity?.Name ?? "System";

                if (existing.IsPosted)
                {
                    // إشعار مرحّل: نعكس القيود القديمة ثم نرحّل بالمبلغ الجديد
                    await _ledgerPostingService.ReverseForHeaderDeleteAsync(Models.LedgerSourceType.DebitNote, id, postedBy, "تعديل إشعار خصم وإعادة ترحيله");
                    existing.IsPosted = false;
                    existing.PostedAt = null;
                    existing.PostedBy = null;
                }

                await _context.SaveChangesAsync();

                var newValues = System.Text.Json.JsonSerializer.Serialize(new { existing.NoteDate, existing.CustomerId, existing.AccountId, existing.Amount });
                await _activityLogger.LogAsync(UserActionType.Edit, "DebitNote", id, $"تعديل إشعار خصم رقم {id}", oldValues, newValues);

                try
                {
                    await _ledgerPostingService.PostDebitNoteAsync(id, postedBy);
                    TempData["DebitNoteSuccess"] = "تم حفظ وترحيل إشعار الخصم بنجاح.";
                }
                catch (Exception ex)
                {
                    TempData["DebitNoteError"] = $"تم الحفظ وعكس الترحيل القديم، لكن فشل الترحيل الجديد: {ex.Message}";
                }
                return RedirectToAction(nameof(Edit), new { id });
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!DebitNoteExists(id))
                    return NotFound();
                throw;
            }
        }

        // GET: DebitNotes/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
                return NotFound();

            var debitNote = await _context.DebitNotes
                                          .Include(d => d.Customer)
                                          .Include(d => d.Account)
                                          .Include(d => d.OffsetAccount)
                                          .FirstOrDefaultAsync(m => m.DebitNoteId == id);
            if (debitNote == null)
                return NotFound();

            return View(debitNote);
        }

        // POST: DebitNotes/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var debitNote = await _context.DebitNotes.FindAsync(id);
            if (debitNote == null)
                return RedirectToAction(nameof(Index));

            try
            {
                var oldValues = System.Text.Json.JsonSerializer.Serialize(new { debitNote.NoteDate, debitNote.CustomerId, debitNote.AccountId, debitNote.Amount });
                if (debitNote.IsPosted)
                    await _ledgerPostingService.ReverseForHeaderDeleteAsync(Models.LedgerSourceType.DebitNote, id, User?.Identity?.Name ?? "System", "حذف إشعار خصم");

                _context.DebitNotes.Remove(debitNote);
                await _context.SaveChangesAsync();

                await _activityLogger.LogAsync(UserActionType.Delete, "DebitNote", id, $"حذف إشعار خصم رقم {id}", oldValues: oldValues);

                TempData["DebitNoteSuccess"] = "تم حذف إشعار الخصم.";
            }
            catch (Exception ex)
            {
                TempData["DebitNoteError"] = TempData["ErrorMessage"] = $"لا يمكن حذف الإشعار: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        private bool DebitNoteExists(int id)
        {
            return _context.DebitNotes.Any(e => e.DebitNoteId == id);
        }
    }
}
