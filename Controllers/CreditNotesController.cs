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
using ERP.Models;                                 // CreditNote, UserActionType...
using ERP.Security;
using ERP.Services;                               // ILedgerPostingService

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إشعارات الإضافة (CreditNotes)
    /// - زر حفظ = حفظ + ترحيل محاسبي (LedgerEntries + تحديث حساب العميل + الأرباح).
    /// بالنظام الثابت:
    /// - Index: بحث + ترتيب + فلترة بالتاريخ والكود + اختيار أعمدة + طباعة + تصدير + حذف جماعي/حذف الكل.
    /// - Show: عرض إشعار واحد.
    /// - Export: تصدير إلى CSV/Excel.
    /// - BulkDelete: حذف المحدد.
    /// - DeleteAll: حذف كل الإشعارات (لبيئة TEST).
    /// - بالإضافة إلى CRUD الأساسي: Create / Edit / Details / Delete.
    /// </summary>
    [RequirePermission("CreditNotes.Index")]
    public class CreditNotesController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ILedgerPostingService _ledgerPostingService;
        private readonly IUserActivityLogger _activityLogger;
        private readonly IPermissionService _permissionService;
        private readonly IUserAccountVisibilityService _accountVisibilityService;

        private const string InvestorAccountCode = "3101";

        public CreditNotesController(
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
            // قائمة العملاء/الأطراف
            ViewData["CustomerId"] = new SelectList(
                _context.Customers
                        .AsNoTracking()
                        .Where(c => c.IsActive == true)
                        .OrderBy(c => c.CustomerName),
                "CustomerId",
                "CustomerName",
                customerId
            );

            // قائمة الحسابات (حساب الطرف)
            var accountsQ = _context.Accounts
                        .AsNoTracking()
                        .OrderBy(a => a.AccountName)
                        .AsQueryable();
            if (!canViewInvestors)
                accountsQ = accountsQ.Where(a => a.AccountCode != InvestorAccountCode);

            ViewData["AccountId"] = new SelectList(accountsQ, "AccountId", "AccountName", accountId);

            // قائمة حساب مقابل (اختياري)
            ViewData["OffsetAccountId"] = new SelectList(accountsQ, "AccountId", "AccountName", offsetAccountId);
        }

        // =========================================================
        // دالة خاصة: تجهيز الاستعلام الأساسي + الفلاتر + البحث + الترتيب
        // نستخدمها في Index و Export حتى لا نكرر الكود.
        // =========================================================
        private IQueryable<CreditNote> BuildQuery(
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
            // (1) الاستعلام الأساسي من جدول إشعارات الإضافة مع تحميل الطرف والحسابات (بدون تتبّع لتحسين الأداء)
            IQueryable<CreditNote> q = _context.CreditNotes
                .AsNoTracking()
                .Include(c => c.Customer)
                .Include(c => c.Account)
                .Include(c => c.OffsetAccount);

            if (!canViewInvestors)
                q = q.Where(c => c.Account != null && c.Account.AccountCode != InvestorAccountCode
                                 && (c.OffsetAccount == null || c.OffsetAccount.AccountCode != InvestorAccountCode));

            // إخفاء أي إشعار مرتبط بعميل غير ظاهر (حساب رئيسي أو قيود بحساب مسموح)
            if (hiddenCustomerAccountIds != null && hiddenCustomerAccountIds.Count > 0)
            {
                q = restrictedToAllowedOnly
                    ? q.Where(c => (c.Customer != null && c.Customer.AccountId != null && !hiddenCustomerAccountIds.Contains(c.Customer.AccountId.Value))
                        || (c.Customer != null && (c.Customer.PartyCategory == "Customer" || c.Customer.PartyCategory == "Supplier")
                            && c.CustomerId != null && _context.LedgerEntries.Any(e => e.CustomerId == c.CustomerId && !hiddenCustomerAccountIds.Contains(e.AccountId))))
                    : q.Where(c => c.Customer == null || c.Customer.AccountId == null || !hiddenCustomerAccountIds.Contains(c.Customer.AccountId.Value)
                        || (c.Customer != null && (c.Customer.PartyCategory == "Customer" || c.Customer.PartyCategory == "Supplier")
                            && c.CustomerId != null && _context.LedgerEntries.Any(e => e.CustomerId == c.CustomerId && !hiddenCustomerAccountIds.Contains(e.AccountId))));
            }

            // (2) فلتر كود من/إلى (نعتمد هنا على CreditNoteId كرقم الإشعار)
            if (fromCode.HasValue)
                q = q.Where(c => c.CreditNoteId >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(c => c.CreditNoteId <= toCode.Value);

            // (3) فلتر التاريخ: نفلتر حسب تاريخ الإشعار NoteDate
            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                DateTime from = fromDate.Value;
                DateTime to = toDate.Value;

                q = q.Where(c => c.NoteDate >= from && c.NoteDate <= to);
            }

            // (4) خرائط البحث: نحدد الأعمدة النصية والرقمية للبحث الموحد

            // الحقول النصية (string) التى يمكن البحث فيها
            var stringFields =
                new Dictionary<string, Expression<Func<CreditNote, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["reason"] = c => c.Reason ?? "",                                   // سبب الإشعار
                    ["desc"] = c => c.Description ?? "",                              // البيان
                    ["customer"] = c => c.Customer != null ? c.Customer.CustomerName : "",// اسم الطرف
                    ["account"] = c => c.Account != null ? c.Account.AccountName : "",   // حساب الطرف
                    ["offset"] = c => c.OffsetAccount != null ? c.OffsetAccount.AccountName : "", // الحساب المقابل
                    ["createdBy"] = c => c.CreatedBy ?? "",                                // أنشئ بواسطة
                    ["postedBy"] = c => c.PostedBy ?? ""                                  // رحّله بواسطة
                };

            // الحقول الرقمية (int) التى يمكن البحث فيها (نفس إشعارات الخصم)
            var intFields =
                new Dictionary<string, Expression<Func<CreditNote, int>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = c => c.CreditNoteId,       // البحث برقم الإشعار
                    ["number"] = c => c.CreditNoteId   // رقم المستند
                };

            // الحقول المسموح الترتيب عليها
            var orderFields =
                new Dictionary<string, Expression<Func<CreditNote, object>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["CreditNoteId"] = c => c.CreditNoteId,                               // رقم الإشعار / رقم المستند
                    ["NoteDate"] = c => c.NoteDate,                                   // تاريخ الإشعار
                    ["Amount"] = c => c.Amount,                                     // المبلغ
                    ["CustomerName"] = c => c.Customer != null ? c.Customer.CustomerName : "",
                    ["AccountName"] = c => c.Account != null ? c.Account.AccountName : "",
                    ["OffsetAccountName"] = c => c.OffsetAccount != null ? c.OffsetAccount.AccountName : "",
                    ["Reason"] = c => c.Reason ?? "",
                    ["Description"] = c => c.Description ?? "",
                    ["IsPosted"] = c => c.IsPosted,                                   // حالة الترحيل
                    ["CreatedAt"] = c => c.CreatedAt,                                  // تاريخ الإنشاء
                    ["UpdatedAt"] = c => c.UpdatedAt ?? DateTime.MinValue              // آخر تعديل
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
                defaultSearchBy: "all",         // لو المستخدم لم يحدد نوع البحث
                defaultSortBy: "NoteDate"     // الترتيب الافتراضي بتاريخ الإشعار
            );

            return q;
        }

        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        private static IQueryable<CreditNote> ApplyColumnFilters(
            IQueryable<CreditNote> query,
            string? filterCol_id,
            string? filterCol_number,
            string? filterCol_date,
            string? filterCol_customer,
            string? filterCol_account,
            string? filterCol_offset,
            string? filterCol_amount,
            string? filterCol_reason,
            string? filterCol_posted,
            string? filterCol_created,
            string? filterCol_updated,
            string? filterCol_desc)
        {
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) query = query.Where(c => ids.Contains(c.CreditNoteId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_number))
            {
                var ids = filterCol_number.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) query = query.Where(c => ids.Contains(c.CreditNoteId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_date))
            {
                var parts = filterCol_date.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length >= 8).ToList();
                if (parts.Count > 0)
                {
                    var dates = new List<DateTime>();
                    foreach (var p in parts)
                        if (DateTime.TryParse(p, out var d)) dates.Add(d.Date);
                    if (dates.Count > 0) query = query.Where(c => dates.Contains(c.NoteDate.Date));
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_customer))
            {
                var vals = filterCol_customer.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    query = query.Where(c => c.Customer != null && vals.Contains(c.Customer.CustomerName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_account))
            {
                var vals = filterCol_account.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    query = query.Where(c => c.Account != null && vals.Contains(c.Account.AccountName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_offset))
            {
                var vals = filterCol_offset.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    query = query.Where(c => c.OffsetAccount != null && vals.Contains(c.OffsetAccount.AccountName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_amount))
            {
                var vals = filterCol_amount.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0) query = query.Where(c => vals.Contains(c.Amount));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_reason))
            {
                var vals = filterCol_reason.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    query = query.Where(c => c.Reason != null && vals.Contains(c.Reason));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_posted))
            {
                var vals = filterCol_posted.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim().ToLowerInvariant()).Where(x => x == "true" || x == "1" || x == "مرحّل" || x == "false" || x == "0" || x == "مسودة").ToList();
                if (vals.Count > 0)
                {
                    var postTrue = vals.Any(v => v == "true" || v == "1" || v == "مرحّل");
                    var postFalse = vals.Any(v => v == "false" || v == "0" || v == "مسودة");
                    if (postTrue && !postFalse) query = query.Where(c => c.IsPosted);
                    else if (postFalse && !postTrue) query = query.Where(c => !c.IsPosted);
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_created))
            {
                var parts = filterCol_created.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length >= 8).ToList();
                if (parts.Count > 0)
                {
                    var dates = new List<DateTime>();
                    foreach (var p in parts)
                        if (DateTime.TryParse(p, out var d)) dates.Add(d);
                    if (dates.Count > 0)
                        query = query.Where(c => dates.Any(d => c.CreatedAt >= d && c.CreatedAt < d.AddMinutes(1)));
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_updated))
            {
                var parts = filterCol_updated.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length >= 8).ToList();
                if (parts.Count > 0)
                {
                    var dates = new List<DateTime>();
                    foreach (var p in parts)
                        if (DateTime.TryParse(p, out var d)) dates.Add(d);
                    if (dates.Count > 0)
                        query = query.Where(c => c.UpdatedAt.HasValue && dates.Any(d => c.UpdatedAt.Value >= d && c.UpdatedAt.Value < d.AddMinutes(1)));
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_desc))
            {
                var vals = filterCol_desc.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    query = query.Where(c => c.Description != null && vals.Any(v => c.Description.Contains(v)));
            }
            return query;
        }

        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var columnLower = (column ?? "").Trim().ToLowerInvariant();
            var q = _context.CreditNotes.AsNoTracking()
                .Include(c => c.Customer)
                .Include(c => c.Account)
                .Include(c => c.OffsetAccount);

            if (columnLower == "id" || columnLower == "number")
            {
                var ids = await q.Select(c => c.CreditNoteId).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "date")
            {
                var dates = await q.Select(c => c.NoteDate.Date).Distinct().OrderByDescending(x => x).Take(500).ToListAsync();
                return Json(dates.Select(d => new { value = d.ToString("yyyy-MM-dd"), display = d.ToString("yyyy-MM-dd") }));
            }
            if (columnLower == "customer" || columnLower == "customername")
            {
                var list = await q.Where(c => c.Customer != null).Select(c => c.Customer!.CustomerName).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "account" || columnLower == "accountname")
            {
                var list = await q.Where(c => c.Account != null).Select(c => c.Account!.AccountName).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "offset" || columnLower == "offsetaccountname")
            {
                var list = await q.Where(c => c.OffsetAccount != null).Select(c => c.OffsetAccount!.AccountName).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "amount")
            {
                var list = await q.Select(c => c.Amount).Distinct().OrderBy(x => x).Take(300).ToListAsync();
                return Json(list.Select(v => new { value = v.ToString(CultureInfo.InvariantCulture), display = v.ToString("0.00") }));
            }
            if (columnLower == "reason")
            {
                var list = await q.Where(c => c.Reason != null && c.Reason != "").Select(c => c.Reason!).Distinct().OrderBy(x => x).Take(300).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "posted" || columnLower == "isposted")
            {
                return Json(new[] { new { value = "true", display = "مرحّل" }, new { value = "false", display = "مسودة" } });
            }
            if (columnLower == "created" || columnLower == "createdat")
            {
                var list = await q.Where(c => c.CreatedAt != default).Select(c => c.CreatedAt).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd HH:mm"), display = d.ToString("yyyy-MM-dd HH:mm") }));
            }
            if (columnLower == "updated" || columnLower == "updatedat")
            {
                var list = await q.Where(c => c.UpdatedAt.HasValue).Select(c => c.UpdatedAt!.Value).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd HH:mm"), display = d.ToString("yyyy-MM-dd HH:mm") }));
            }
            if (columnLower == "desc" || columnLower == "description")
            {
                var list = await q.Where(c => c.Description != null && c.Description != "").Select(c => c.Description!).Distinct().OrderBy(x => x).Take(300).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v.Length > 50 ? v.Substring(0, 50) + "…" : v }));
            }
            return Json(Array.Empty<object>());
        }

        // =========================================================
        // Index — عرض قائمة إشعارات الإضافة (نظام القوائم الموحد)
        // =========================================================
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",
            string? sort = "NoteDate",
            string? dir = "desc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,   // من كود (CreditNoteId)
            int? toCode = null,     // إلى كود
            string? filterCol_id = null,
            string? filterCol_number = null,
            string? filterCol_date = null,
            string? filterCol_customer = null,
            string? filterCol_account = null,
            string? filterCol_offset = null,
            string? filterCol_amount = null,
            string? filterCol_reason = null,
            string? filterCol_posted = null,
            string? filterCol_created = null,
            string? filterCol_updated = null,
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

            q = ApplyColumnFilters(q, filterCol_id, filterCol_number, filterCol_date, filterCol_customer, filterCol_account, filterCol_offset, filterCol_amount, filterCol_reason, filterCol_posted, filterCol_created, filterCol_updated, filterCol_desc);

            var totalAmount = await q.Select(c => (decimal?)c.Amount).SumAsync() ?? 0m;
            var model = await PagedResult<CreditNote>.CreateAsync(q, page, pageSize);

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
            ViewBag.FilterCol_Reason = filterCol_reason;
            ViewBag.FilterCol_Posted = filterCol_posted;
            ViewBag.FilterCol_Created = filterCol_created;
            ViewBag.FilterCol_Updated = filterCol_updated;
            ViewBag.FilterCol_Desc = filterCol_desc;
            ViewBag.TotalAmount = totalAmount;
            ViewBag.DateField = "NoteDate";
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalCount = model.TotalCount;

            return View(model);
        }

        // =========================================================
        // Show — عرض تفاصيل إشعار إضافة واحد (قراءة فقط)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Show(int id)
        {
            if (id <= 0)
                return BadRequest(); // رقم غير صحيح

            // قراءة الإشعار مع الطرف والحسابات (للعرض فقط)
            var note = await _context.CreditNotes
                                     .AsNoTracking()
                                     .Include(c => c.Customer)
                                     .Include(c => c.Account)
                                     .Include(c => c.OffsetAccount)
                                     .FirstOrDefaultAsync(c => c.CreditNoteId == id);

            if (note == null)
                return NotFound();

            if (!await CanViewInvestorsAsync() &&
                note.Account != null &&
                note.Account.AccountCode == InvestorAccountCode)
                return NotFound();

            if (!await CanViewInvestorsAsync() &&
                note.OffsetAccount != null &&
                note.OffsetAccount.AccountCode == InvestorAccountCode)
                return NotFound();

            return View(note); // Views/CreditNotes/Show.cshtml (نعمله لاحقاً بنفس نمط Show الثابت)
        }

        // =========================================================
        // Export — تصدير قائمة الإشعارات إلى CSV (يفتح في Excel)
        // زر التصدير في الواجهة لونه أخضر (زر إكسل).
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
            string? filterCol_reason = null,
            string? filterCol_posted = null,
            string? filterCol_created = null,
            string? filterCol_updated = null,
            string? filterCol_desc = null,
            string format = "excel")   // excel | csv (الاتنين حالياً يخرجوا CSV)
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

            q = ApplyColumnFilters(q, filterCol_id, filterCol_number, filterCol_date, filterCol_customer, filterCol_account, filterCol_offset, filterCol_amount, filterCol_reason, filterCol_posted, filterCol_created, filterCol_updated, filterCol_desc);

            var list = await q.ToListAsync();

            var sb = new StringBuilder();

            // عناوين الأعمدة في ملف CSV (نفس إشعارات الخصم)
            sb.AppendLine("CreditNoteId,NoteDate,CustomerId,CustomerName,AccountId,AccountName,OffsetAccountId,OffsetAccountName,Amount,Reason,Description,IsPosted,CreatedAt,UpdatedAt,CreatedBy,PostedAt,PostedBy");

            // كل صف إشعار في سطر CSV
            foreach (var c in list)
            {
                string customerName = c.Customer?.CustomerName ?? "";
                string accountName = c.Account?.AccountName ?? "";
                string offsetName = c.OffsetAccount?.AccountName ?? "";

                string line = string.Join(",",
                    c.CreditNoteId,
                    c.NoteDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    c.CustomerId?.ToString() ?? "",
                    customerName.Replace(",", " "),
                    c.AccountId,
                    accountName.Replace(",", " "),
                    c.OffsetAccountId?.ToString() ?? "",
                    offsetName.Replace(",", " "),
                    c.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                    (c.Reason ?? "").Replace(",", " "),
                    (c.Description ?? "").Replace(",", " "),
                    c.IsPosted ? "1" : "0",
                    c.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    c.UpdatedAt.HasValue
                        ? c.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                        : "",
                    (c.CreatedBy ?? "").Replace(",", " "),
                    c.PostedAt.HasValue
                        ? c.PostedAt.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                        : "",
                    (c.PostedBy ?? "").Replace(",", " ")
                );

                sb.AppendLine(line);
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = "CreditNotes.csv";
            const string contentType = "text/csv";

            return File(bytes, contentType, fileName);
        }

        // =========================================================
        // BulkDelete — حذف مجموعة من إشعارات الإضافة المحددة
        // (يفضل استخدامها بحذر).
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(int[] ids)
        {
            // لو المستخدم لم يحدد أى إشعار
            if (ids == null || ids.Length == 0)
            {
                TempData["Error"] = "لم يتم اختيار أى إشعار للحذف.";
                return RedirectToAction(nameof(Index));
            }

            // نحضر الإشعارات المطابقة للأرقام المختارة
            var notes = await _context.CreditNotes
                                      .Where(c => ids.Contains(c.CreditNoteId))
                                      .ToListAsync();

            if (notes.Count == 0)
            {
                TempData["Error"] = "لم يتم العثور على الإشعارات المحددة.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                string? postedBy = User?.Identity?.Name ?? "System";
                foreach (var note in notes.Where(n => n.IsPosted))
                {
                    await _ledgerPostingService.ReverseForHeaderDeleteAsync(Models.LedgerSourceType.CreditNote, note.CreditNoteId, postedBy, "حذف جماعي إشعار إضافة");
                }
                _context.CreditNotes.RemoveRange(notes);
                await _context.SaveChangesAsync();

                TempData["Success"] = $"تم حذف {notes.Count} من إشعارات الإضافة المحددة.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"لا يمكن حذف بعض الإشعارات: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // DeleteAll — حذف جميع إشعارات الإضافة
        // تنبيه: يُفضّل استخدامه في بيئة TEST فقط.
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _context.CreditNotes.ToListAsync();

            if (all.Count == 0)
            {
                TempData["Error"] = "لا توجد إشعارات لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                string? postedBy = User?.Identity?.Name ?? "System";
                foreach (var note in all.Where(n => n.IsPosted))
                {
                    await _ledgerPostingService.ReverseForHeaderDeleteAsync(Models.LedgerSourceType.CreditNote, note.CreditNoteId, postedBy, "حذف جميع إشعارات الإضافة");
                }
                _context.CreditNotes.RemoveRange(all);
                await _context.SaveChangesAsync();

                TempData["Success"] = "تم حذف جميع إشعارات الإضافة.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"لا يمكن حذف جميع الإشعارات: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // جلب حساب الطرف تلقائياً عند اختيار العميل (نفس نمط إشعار الخصم)
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
        // CRUD الأساسي — Create / Details / Edit / Delete
        // =========================================================

        // GET: CreditNotes/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
                return NotFound();

            var creditNote = await _context.CreditNotes
                .Include(c => c.Customer)
                .Include(c => c.Account)
                .Include(c => c.OffsetAccount)
                .FirstOrDefaultAsync(m => m.CreditNoteId == id);

            if (creditNote == null)
                return NotFound();

            return View(creditNote);
        }

        // GET: CreditNotes/Create
        public async Task<IActionResult> Create(int? customerId = null)
        {
            var model = new CreditNote
            {
                NoteDate = DateTime.Now,
                CreatedAt = DateTime.Now
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

        // POST: CreditNotes/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreditNote creditNote)
        {
            // إن لم يُرسل حساب الطرف وتم اختيار عميل، نملأه من حساب العميل (نفس نمط إذن الاستلام)
            if (creditNote.AccountId <= 0 && creditNote.CustomerId.HasValue)
            {
                var cust = await _context.Customers
                    .AsNoTracking()
                    .Where(c => c.CustomerId == creditNote.CustomerId.Value)
                    .Select(c => new { c.AccountId })
                    .FirstOrDefaultAsync();
                if (cust?.AccountId != null)
                {
                    creditNote.AccountId = cust.AccountId.Value;
                    ModelState.Remove("AccountId");
                }
            }

            if (creditNote.AccountId <= 0)
                ModelState.AddModelError(nameof(CreditNote.AccountId), "حساب الطرف مطلوب.");

            if (ModelState.IsValid)
            {
                // تعبئة بيانات التتبع
                creditNote.CreatedAt = DateTime.Now;
                creditNote.UpdatedAt = creditNote.CreatedAt;
                creditNote.CreatedBy = User?.Identity?.Name ?? "System";
                creditNote.IsPosted = false;
                creditNote.PostedAt = null;
                creditNote.PostedBy = null;
                creditNote.IsLocked = true; // غلق الإشعار بعد الحفظ

                _context.Add(creditNote);
                await _context.SaveChangesAsync();

                await _activityLogger.LogAsync(UserActionType.Create, "CreditNote", creditNote.CreditNoteId, $"إنشاء إشعار إضافة رقم {creditNote.CreditNoteId}");

                try
                {
                    await _ledgerPostingService.PostCreditNoteAsync(creditNote.CreditNoteId, User?.Identity?.Name ?? "System");
                    TempData["Success"] = "تم حفظ وترحيل إشعار الإضافة بنجاح.";
                }
                catch (Exception ex)
                {
                    TempData["Error"] = TempData["ErrorMessage"] = $"تم الحفظ، لكن فشل الترحيل: {ex.Message}";
                }
                return RedirectToAction(nameof(Edit), new { id = creditNote.CreditNoteId });
            }

            var canViewInvestors = await CanViewInvestorsAsync();
            PopulateLookups(creditNote.CustomerId, creditNote.AccountId, creditNote.OffsetAccountId, canViewInvestors);
            return View(creditNote);
        }

        // GET: CreditNotes/Unlock/5 — فتح الإشعار للتعديل (سيُضاف التحقق من الصلاحية لاحقاً)
        public async Task<IActionResult> Unlock(int? id)
        {
            if (id == null)
                return NotFound();

            var creditNote = await _context.CreditNotes.FindAsync(id);
            if (creditNote == null)
                return NotFound();

            creditNote.IsLocked = false;
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Edit), new { id });
        }

        // GET: CreditNotes/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
                return NotFound();

            var creditNote = await _context.CreditNotes.FindAsync(id);
            if (creditNote == null)
                return NotFound();

            var canViewInvestors = await CanViewInvestorsAsync();
            PopulateLookups(creditNote.CustomerId, creditNote.AccountId, creditNote.OffsetAccountId, canViewInvestors);
            return View(creditNote);
        }

        // POST: CreditNotes/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, CreditNote input)
        {
            if (id != input.CreditNoteId)
                return NotFound();

            // ملء حساب الطرف من العميل إن كان فارغاً (نفس نمط Create)
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
                ModelState.AddModelError(nameof(CreditNote.AccountId), "حساب الطرف مطلوب.");

            if (!ModelState.IsValid)
            {
                var canViewInvestors = await CanViewInvestorsAsync();
                PopulateLookups(input.CustomerId, input.AccountId, null, canViewInvestors);
                return View(input);
            }

            var existing = await _context.CreditNotes.FindAsync(id);
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
                    await _ledgerPostingService.ReverseForHeaderDeleteAsync(Models.LedgerSourceType.CreditNote, id, postedBy, "تعديل إشعار إضافة وإعادة ترحيله");
                    existing.IsPosted = false;
                    existing.PostedAt = null;
                    existing.PostedBy = null;
                }

                await _context.SaveChangesAsync();

                var newValues = System.Text.Json.JsonSerializer.Serialize(new { existing.NoteDate, existing.CustomerId, existing.AccountId, existing.Amount });
                await _activityLogger.LogAsync(UserActionType.Edit, "CreditNote", id, $"تعديل إشعار إضافة رقم {id}", oldValues, newValues);

                try
                {
                    await _ledgerPostingService.PostCreditNoteAsync(id, postedBy);
                    TempData["Success"] = "تم حفظ وترحيل إشعار الإضافة بنجاح.";
                }
                catch (Exception ex)
                {
                    TempData["Error"] = $"تم الحفظ وعكس الترحيل القديم، لكن فشل الترحيل الجديد: {ex.Message}";
                }
                return RedirectToAction(nameof(Edit), new { id });
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!CreditNoteExists(existing.CreditNoteId))
                    return NotFound();

                throw;
            }
        }

        // GET: CreditNotes/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
                return NotFound();

            var creditNote = await _context.CreditNotes
                .Include(c => c.Customer)
                .Include(c => c.Account)
                .Include(c => c.OffsetAccount)
                .FirstOrDefaultAsync(m => m.CreditNoteId == id);

            if (creditNote == null)
                return NotFound();

            return View(creditNote);
        }

        // POST: CreditNotes/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var creditNote = await _context.CreditNotes.FindAsync(id);
            if (creditNote == null)
                return RedirectToAction(nameof(Index));

            try
            {
                var oldValues = System.Text.Json.JsonSerializer.Serialize(new { creditNote.NoteDate, creditNote.CustomerId, creditNote.AccountId, creditNote.Amount });
                if (creditNote.IsPosted)
                    await _ledgerPostingService.ReverseForHeaderDeleteAsync(Models.LedgerSourceType.CreditNote, id, User?.Identity?.Name ?? "System", "حذف إشعار إضافة");

                _context.CreditNotes.Remove(creditNote);
                await _context.SaveChangesAsync();

                await _activityLogger.LogAsync(UserActionType.Delete, "CreditNote", id, $"حذف إشعار إضافة رقم {id}", oldValues: oldValues);

                TempData["Success"] = "تم حذف إشعار الإضافة بنجاح.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = TempData["ErrorMessage"] = $"لا يمكن حذف الإشعار: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        private bool CreditNoteExists(int id)
        {
            return _context.CreditNotes.Any(e => e.CreditNoteId == id);
        }
    }
}
