using System;                                     // متغيرات التاريخ DateTime
using System.Collections.Generic;                 // Dictionary, List
using System.Globalization;                       // تنسيق التواريخ عند التصدير
using System.Linq;                                // LINQ: Where / OrderBy
using System.Linq.Expressions;                    // Expression<Func<...>>
using System.Text;                                // StringBuilder للتصدير
using System.Threading.Tasks;                     // async / await
using Microsoft.AspNetCore.Mvc;                   // Controller, IActionResult
using Microsoft.EntityFrameworkCore;              // AsNoTracking, Include, ToListAsync
using ERP.Data;                                   // AppDbContext الاتصال بقاعدة البيانات
using ERP.Filters;
using ERP.Infrastructure;                         // PagedResult + ApplySearchSort
using ERP.Models;                                 // LedgerEntry + Account + Customer
using ERP.Security;
using ERP.Services;                               // ILedgerPostingService

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر دفتر الأستاذ (LedgerEntries)
    /// شاشة قراءة فقط:
    /// - عرض القيود المحاسبية مع بحث/ترتيب/فلترة بالتاريخ والكود.
    /// - تصدير القيود إلى CSV.
    /// - حذف جماعي / حذف الكل (يُفضّل لبيئة تجريبية أو بإذن خاص).
    /// لا يوجد إنشاء/تعديل قيود من هنا؛ القيود تُنشأ من الشاشات الأخرى (فواتير، إيصالات، قيود يدوية).
    /// </summary>
    [RequirePermission("LedgerEntries.Index")]
    public class LedgerEntriesController : Controller
    {
        // كائن الاتصال بقاعدة البيانات
        private readonly AppDbContext _context;   // متغير: السياق الأساسي للتعامل مع الـ DB
        private readonly ILedgerPostingService _ledgerPostingService;
        private readonly IPermissionService _permissionService;
        private readonly IUserAccountVisibilityService _accountVisibilityService;

        private const string InvestorAccountCode = "3101";

        public LedgerEntriesController(
            AppDbContext context,
            ILedgerPostingService ledgerPostingService,
            IPermissionService permissionService,
            IUserAccountVisibilityService accountVisibilityService)
        {
            _context = context;
            _ledgerPostingService = ledgerPostingService;
            _permissionService = permissionService;
            _accountVisibilityService = accountVisibilityService;
        }

        private static Task<bool> CanViewInvestorsAsync() => Task.FromResult(true); // إظهار/إخفاء 3101 يعتمد على «الحسابات المسموح رؤيتها» فقط

        // =========================================================
        // دالة خاصة: تجهيز الاستعلام الأساسي + الفلاتر + البحث + الترتيب
        // نستخدمها في Index و Export حتى لا نكرر الكود.
        // =========================================================
        private IQueryable<LedgerEntry> BuildQuery(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            int? fromCode,
            int? toCode,
            string? searchMode = null)
        {
            // (1) الاستعلام الأساسي من جدول القيود مع الحساب والعميل (بدون تتبّع لتحسين الأداء)
            // فلترة «من يرى أي قيد» تُطبَّق عبر IUserAccountVisibilityService.ApplyLedgerEntryListVisibilityFilterAsync بعد BuildQuery
            IQueryable<LedgerEntry> q = _context.LedgerEntries
                .AsNoTracking()
                .Include(e => e.Account)
                .Include(e => e.Customer);

            // (2) فلتر كود من/إلى (نعتمد هنا على Id كرقم القيد)
            if (fromCode.HasValue)
                q = q.Where(e => e.Id >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(e => e.Id <= toCode.Value);

            // (3) فلتر التاريخ: نفلتر حسب تاريخ القيد EntryDate
            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                DateTime from = fromDate.Value;
                DateTime to = toDate.Value;

                q = q.Where(e => e.EntryDate >= from && e.EntryDate <= to);
            }

            // (4) خرائط البحث: نحدد الأعمدة النصية والرقمية للبحث الموحد

            // الحقول النصية (string) التى يمكن البحث فيها
            var stringFields =
                new Dictionary<string, Expression<Func<LedgerEntry, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["voucher"] = e => e.VoucherNo ?? "",                                         // رقم المستند
                    ["desc"] = e => e.Description ?? "",                                      // البيان
                    ["source"] = e => e.SourceType.ToString(),                                  // نوع المستند
                    ["account"] = e => e.Account != null ? e.Account.AccountName : "",           // اسم الحساب
                    ["customer"] = e => e.Customer != null ? e.Customer.CustomerName : ""         // اسم العميل/الطرف
                };

            // الحقول الرقمية (int) التى يمكن البحث فيها
            var intFields =
                new Dictionary<string, Expression<Func<LedgerEntry, int>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = e => e.Id                       // البحث برقم القيد
                };

            // الحقول المسموح الترتيب عليها
            var orderFields =
                new Dictionary<string, Expression<Func<LedgerEntry, object>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Id"] = e => e.Id,                                       // رقم القيد
                    ["EntryDate"] = e => e.EntryDate,                                // تاريخ القيد
                    ["SourceType"] = e => e.SourceType,                               // نوع المستند
                    ["PostVersion"] = e => e.PostVersion,                             // مرحلة
                    ["VoucherNo"] = e => e.VoucherNo ?? "",                          // رقم المستند
                    ["SourceId"] = e => e.SourceId ?? 0,                            // معرّف المصدر
                    ["AccountId"] = e => e.AccountId,                                // رقم الحساب
                    ["AccountName"] = e => e.Account != null ? e.Account.AccountName : "",
                    ["CustomerName"] = e => e.Customer != null ? e.Customer.CustomerName : "",
                    ["Debit"] = e => e.Debit,                                    // مدين
                    ["Credit"] = e => e.Credit,                                   // دائن
                    ["Description"] = e => e.Description ?? ""                         // البيان
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
                defaultSortBy: "EntryDate",       // الترتيب الافتراضي بتاريخ القيد (الأحدث أولاً)
                searchMode: searchMode);

            return q;
        }

        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        private static IQueryable<LedgerEntry> ApplyColumnFilters(
            IQueryable<LedgerEntry> query,
            string? filterCol_id,
            string? filterCol_idExpr,
            string? filterCol_date,
            string? filterCol_source,
            string? filterCol_postVersion,
            string? filterCol_postVersionExpr,
            string? filterCol_sourceId,
            string? filterCol_sourceIdExpr,
            string? filterCol_accId,
            string? filterCol_accIdExpr,
            string? filterCol_accName,
            string? filterCol_customer,
            string? filterCol_debit,
            string? filterCol_debitExpr,
            string? filterCol_credit,
            string? filterCol_creditExpr,
            string? filterCol_desc)
        {
            if (!string.IsNullOrWhiteSpace(filterCol_idExpr))
                query = LedgerEntryListNumericExpr.ApplyIdExpr(query, filterCol_idExpr);
            else if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) query = query.Where(e => ids.Contains(e.Id));
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
                    if (dates.Count > 0) query = query.Where(e => dates.Contains(e.EntryDate.Date));
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_source))
            {
                var vals = filterCol_source.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    query = query.Where(e => vals.Contains(e.SourceType.ToString()));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_postVersionExpr))
                query = LedgerEntryListNumericExpr.ApplyPostVersionExpr(query, filterCol_postVersionExpr);
            else if (!string.IsNullOrWhiteSpace(filterCol_postVersion))
            {
                var vals = filterCol_postVersion.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0) query = query.Where(e => vals.Contains(e.PostVersion));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_sourceIdExpr))
                query = LedgerEntryListNumericExpr.ApplySourceIdExpr(query, filterCol_sourceIdExpr);
            else if (!string.IsNullOrWhiteSpace(filterCol_sourceId))
            {
                var vals = filterCol_sourceId.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0) query = query.Where(e => e.SourceId.HasValue && vals.Contains(e.SourceId.Value));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_accIdExpr))
                query = LedgerEntryListNumericExpr.ApplyAccountIdExpr(query, filterCol_accIdExpr);
            else if (!string.IsNullOrWhiteSpace(filterCol_accId))
            {
                var vals = filterCol_accId.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0) query = query.Where(e => vals.Contains(e.AccountId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_accName))
            {
                var vals = filterCol_accName.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    query = query.Where(e => e.Account != null && vals.Contains(e.Account.AccountName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_customer))
            {
                var vals = filterCol_customer.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    query = query.Where(e => e.Customer != null && vals.Contains(e.Customer.CustomerName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_debitExpr))
                query = LedgerEntryListNumericExpr.ApplyDebitExpr(query, filterCol_debitExpr);
            else if (!string.IsNullOrWhiteSpace(filterCol_debit))
            {
                var vals = filterCol_debit.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0) query = query.Where(e => vals.Contains(e.Debit));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_creditExpr))
                query = LedgerEntryListNumericExpr.ApplyCreditExpr(query, filterCol_creditExpr);
            else if (!string.IsNullOrWhiteSpace(filterCol_credit))
            {
                var vals = filterCol_credit.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0) query = query.Where(e => vals.Contains(e.Credit));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_desc))
            {
                var vals = filterCol_desc.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    query = query.Where(e => e.Description != null && vals.Any(v => e.Description.Contains(v)));
            }
            return query;
        }

        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var columnLower = (column ?? "").Trim().ToLowerInvariant();
            var canViewInvestors = await CanViewInvestorsAsync();
            IQueryable<LedgerEntry> q = _context.LedgerEntries.AsNoTracking()
                .Include(e => e.Account)
                .Include(e => e.Customer);
            q = await _accountVisibilityService.ApplyLedgerEntryListVisibilityFilterAsync(q, canViewInvestors);

            if (columnLower == "id")
            {
                var ids = await q.Select(e => e.Id).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "date")
            {
                var dates = await q.Select(e => e.EntryDate.Date).Distinct().OrderByDescending(x => x).Take(500).ToListAsync();
                return Json(dates.Select(d => new { value = d.ToString("yyyy-MM-dd"), display = d.ToString("yyyy-MM-dd") }));
            }
            if (columnLower == "source")
            {
                var list = await q.Select(e => e.SourceType.ToString()).Distinct().OrderBy(x => x).Take(100).ToListAsync();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "postversion")
            {
                var list = await q.Select(e => e.PostVersion).Distinct().Where(x => x > 0).OrderBy(x => x).Take(100).ToListAsync();
                return Json(list.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "sourceid")
            {
                var list = await q.Where(e => e.SourceId.HasValue).Select(e => e.SourceId!.Value).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(list.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "accid" || columnLower == "accountid")
            {
                var list = await q.Select(e => e.AccountId).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(list.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "accname" || columnLower == "accountname")
            {
                var list = await q.Where(e => e.Account != null).Select(e => e.Account!.AccountName).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "customer" || columnLower == "customername")
            {
                var list = await q.Where(e => e.Customer != null).Select(e => e.Customer!.CustomerName).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "desc" || columnLower == "description")
            {
                var list = await q.Where(e => e.Description != null && e.Description != "").Select(e => e.Description!).Distinct().OrderBy(x => x).Take(300).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v.Length > 50 ? v.Substring(0, 50) + "…" : v }));
            }
            if (columnLower == "debit")
            {
                var list = await q.Select(e => e.Debit).Distinct().OrderBy(x => x).Take(300).ToListAsync();
                return Json(list.Select(v => new { value = v.ToString(System.Globalization.CultureInfo.InvariantCulture), display = v.ToString("0.00") }));
            }
            if (columnLower == "credit")
            {
                var list = await q.Select(e => e.Credit).Distinct().OrderBy(x => x).Take(300).ToListAsync();
                return Json(list.Select(v => new { value = v.ToString(System.Globalization.CultureInfo.InvariantCulture), display = v.ToString("0.00") }));
            }
            return Json(Array.Empty<object>());
        }





        // =========================================================
        // Index — عرض قائمة القيود (نظام القوائم الموحد)
        // شاشة قراءة فقط لعرض دفتر الأستاذ.
        // ✅ إضافة: إجمالي المدين + إجمالي الدائن (يتفلتر مع الفلترة)
        // =========================================================
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",
            string? searchMode = "contains",
            string? sort = "EntryDate",
            string? dir = "desc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_date = null,
            string? filterCol_source = null,
            string? filterCol_postVersion = null,
            string? filterCol_postVersionExpr = null,
            string? filterCol_sourceId = null,
            string? filterCol_sourceIdExpr = null,
            string? filterCol_accId = null,
            string? filterCol_accIdExpr = null,
            string? filterCol_accName = null,
            string? filterCol_customer = null,
            string? filterCol_debit = null,
            string? filterCol_debitExpr = null,
            string? filterCol_credit = null,
            string? filterCol_creditExpr = null,
            string? filterCol_desc = null,
            int page = 1,
            int pageSize = 10)
        {
            var pageSizeQuery = Request.Query["pageSize"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageSizeQuery) && int.TryParse(pageSizeQuery.Trim(), out var psVal))
                pageSize = psVal;

            if (page < 1) page = 1;
            if (pageSize < 0) pageSize = 10;
            if (pageSize > 0 && pageSize != 10 && pageSize != 25 && pageSize != 50 && pageSize != 100 && pageSize != 200)
                pageSize = 10;

            var canViewInvestors = await CanViewInvestorsAsync();
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
                searchMode);
            q = await _accountVisibilityService.ApplyLedgerEntryListVisibilityFilterAsync(q, canViewInvestors);

            q = ApplyColumnFilters(q, filterCol_id, filterCol_idExpr, filterCol_date, filterCol_source, filterCol_postVersion, filterCol_postVersionExpr, filterCol_sourceId, filterCol_sourceIdExpr, filterCol_accId, filterCol_accIdExpr, filterCol_accName, filterCol_customer, filterCol_debit, filterCol_debitExpr, filterCol_credit, filterCol_creditExpr, filterCol_desc);

            // =========================================================
            // 2) حساب الإجماليات من نفس الاستعلام (بعد الفلاتر)
            // ✅ مهم: لازم قبل الـ PagedResult علشان ما تتحسبش على الصفحة بس
            // =========================================================
            // متغير: إجمالي المدين بعد الفلاتر
            decimal totalDebit = await q.SumAsync(e => (decimal?)e.Debit) ?? 0m;

            // متغير: إجمالي الدائن بعد الفلاتر
            decimal totalCredit = await q.SumAsync(e => (decimal?)e.Credit) ?? 0m;

            // متغير: صافي الحركة داخل الفلتر (مدين - دائن)
            decimal netBalance = totalDebit - totalCredit;

            // صافي حركة حسابات النقد/الخزينة — نفس تعريف «رصيد الخزينة» في تقرير أرباح الأصناف (أصول: خزينة/بنك/صندوق أو كود 1101/1102)
            var treasuryAccountIds = await _context.Accounts
                .AsNoTracking()
                .Where(a => a.AccountType == AccountType.Asset &&
                    (a.AccountName.Contains("خزينة") || a.AccountName.Contains("بنك") ||
                     a.AccountName.Contains("صندوق") || a.AccountCode.StartsWith("1101") || a.AccountCode.StartsWith("1102")))
                .Select(a => a.AccountId)
                .ToListAsync();
            decimal treasuryNetBalance = 0m;
            if (treasuryAccountIds.Count > 0)
                treasuryNetBalance = await q.Where(e => treasuryAccountIds.Contains(e.AccountId)).SumAsync(e => (decimal?)(e.Debit - e.Credit)) ?? 0m;

            // عدد العملاء/الأطراف المميزين (سطر له عميل) — يختلف عن عدد الأسطر لأن نفس العميل يتكرر بعدة قيود
            var distinctCustomerCount = await q
                .Where(e => e.CustomerId != null)
                .GroupBy(e => e.CustomerId!.Value)
                .CountAsync();

            // =========================================================
            // 3) الترقيم (0 = الكل — نمط موحّد)
            // =========================================================
            var totalCount = await q.CountAsync();

            int effectivePageSize = pageSize;
            if (pageSize == 0)
            {
                effectivePageSize = totalCount == 0 ? 10 : Math.Min(totalCount, 100_000);
                page = 1;
            }

            var totalPages = pageSize == 0 ? 1 : Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));

            var items = await q
                .Skip((page - 1) * effectivePageSize)
                .Take(effectivePageSize)
                .ToListAsync();

            var sortDesc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            var model = new PagedResult<LedgerEntry>(items, page, pageSize, totalCount)
            {
                TotalPages = totalPages,
                HasPrevious = page > 1,
                HasNext = pageSize != 0 && page * pageSize < totalCount,
                Search = search,
                SearchBy = searchBy,
                SortColumn = sort,
                SortDescending = sortDesc,
                UseDateRange = useDateRange,
                FromDate = fromDate,
                ToDate = toDate
            };

            // =========================================================
            // 4) تمرير القيم للواجهة
            // =========================================================
            ViewBag.Search = search ?? "";
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.SearchMode = string.IsNullOrWhiteSpace(searchMode) ? "contains" : searchMode.Trim().ToLowerInvariant();
            ViewBag.Sort = sort ?? "EntryDate";
            ViewBag.Dir = (dir?.ToLower() == "asc") ? "asc" : "desc";

            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_IdExpr = filterCol_idExpr;
            ViewBag.FilterCol_Date = filterCol_date;
            ViewBag.FilterCol_Source = filterCol_source;
            ViewBag.FilterCol_PostVersion = filterCol_postVersion;
            ViewBag.FilterCol_PostVersionExpr = filterCol_postVersionExpr;
            ViewBag.FilterCol_SourceId = filterCol_sourceId;
            ViewBag.FilterCol_SourceIdExpr = filterCol_sourceIdExpr;
            ViewBag.FilterCol_AccId = filterCol_accId;
            ViewBag.FilterCol_AccIdExpr = filterCol_accIdExpr;
            ViewBag.FilterCol_AccName = filterCol_accName;
            ViewBag.FilterCol_Customer = filterCol_customer;
            ViewBag.FilterCol_Debit = filterCol_debit;
            ViewBag.FilterCol_DebitExpr = filterCol_debitExpr;
            ViewBag.FilterCol_Credit = filterCol_credit;
            ViewBag.FilterCol_CreditExpr = filterCol_creditExpr;
            ViewBag.FilterCol_Desc = filterCol_desc;

            ViewBag.DateField = "EntryDate";
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;

            ViewBag.TotalCount = model.TotalCount;

            ViewBag.TotalDebit = totalDebit;
            ViewBag.TotalCredit = totalCredit;
            ViewBag.NetBalance = netBalance;
            ViewBag.TreasuryNetBalance = treasuryNetBalance;
            ViewBag.DistinctCustomerCount = distinctCustomerCount;

            return View(model);
        }










        // =========================================================
        // Show — عرض تفاصيل سطر قيد واحد (قراءة فقط)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Show(int id)
        {
            if (id <= 0)
                return BadRequest(); // رقم غير صحيح

            // قراءة السطر مع الحساب والعميل (للعرض فقط)
            var entry = await _context.LedgerEntries
                                      .AsNoTracking()
                                      .Include(e => e.Account)
                                      .Include(e => e.Customer)
                                      .FirstOrDefaultAsync(e => e.Id == id);

            if (entry == null)
                return NotFound();

            // منع عرض قيود الحساب المستثمر
            if (!await CanViewInvestorsAsync() &&
                entry.Account != null &&
                entry.Account.AccountCode == InvestorAccountCode)
                return NotFound();

            return View(entry); // Views/LedgerEntries/Show.cshtml (نعمله لاحقاً بنفس نمط Show الثابت)
        }

        // =========================================================
        // Export — تصدير قائمة القيود إلى CSV (يفتح في Excel)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy = "all",
            string? searchMode = "contains",
            string? sort = "EntryDate",
            string? dir = "desc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_date = null,
            string? filterCol_source = null,
            string? filterCol_postVersion = null,
            string? filterCol_postVersionExpr = null,
            string? filterCol_sourceId = null,
            string? filterCol_sourceIdExpr = null,
            string? filterCol_accId = null,
            string? filterCol_accIdExpr = null,
            string? filterCol_accName = null,
            string? filterCol_customer = null,
            string? filterCol_debit = null,
            string? filterCol_debitExpr = null,
            string? filterCol_credit = null,
            string? filterCol_creditExpr = null,
            string? filterCol_desc = null,
            string format = "excel")
        {
            var canViewInvestors = await CanViewInvestorsAsync();
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
                searchMode);
            q = await _accountVisibilityService.ApplyLedgerEntryListVisibilityFilterAsync(q, canViewInvestors);

            q = ApplyColumnFilters(q, filterCol_id, filterCol_idExpr, filterCol_date, filterCol_source, filterCol_postVersion, filterCol_postVersionExpr, filterCol_sourceId, filterCol_sourceIdExpr, filterCol_accId, filterCol_accIdExpr, filterCol_accName, filterCol_customer, filterCol_debit, filterCol_debitExpr, filterCol_credit, filterCol_creditExpr, filterCol_desc);

            var list = await q.ToListAsync();

            var sb = new StringBuilder();

            sb.AppendLine("رقم القيد,تاريخ القيد,نوع المستند,مرحلة,رقم المستند,معرّف المصدر,كود الحساب,اسم الحساب,كود الطرف,اسم الطرف,مدين,دائن,البيان");

            static string Q(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

            foreach (var e in list)
            {
                string accountCode = e.Account?.AccountCode ?? "";
                string accountName = e.Account?.AccountName ?? "";
                string customerName = e.Customer?.CustomerName ?? "";

                string line = string.Join(",",
                    e.Id,
                    e.EntryDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    Q(e.SourceType.ToString()),
                    e.PostVersion,
                    Q(e.VoucherNo),
                    e.SourceId?.ToString() ?? "",
                    e.AccountId,
                    Q(accountCode),
                    Q(accountName),
                    e.CustomerId?.ToString() ?? "",
                    Q(customerName),
                    e.Debit.ToString("0.00", CultureInfo.InvariantCulture),
                    e.Credit.ToString("0.00", CultureInfo.InvariantCulture),
                    Q(e.Description)
                );

                sb.AppendLine(line);
            }

            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString());
            var fileName = ExcelExportNaming.ArabicTimestampedFileName("قيود دفتر الأستاذ", ".csv");
            const string contentType = "text/csv; charset=utf-8";

            return File(bytes, contentType, fileName);
        }

        // =========================================================
        // حذف قيود الرصيد الافتتاحي للعملاء فقط (لإعادة استيراد أرصدة العملاء من إكسل)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteCustomerOpeningEntries()
        {
            var toRemove = await _context.LedgerEntries
                .Where(e => e.SourceType == LedgerSourceType.Opening && e.CustomerId != null)
                .ToListAsync();
            if (toRemove.Count == 0)
            {
                TempData["Info"] = "لا توجد قيود رصيد افتتاحي للعملاء لحذفها.";
                return RedirectToAction(nameof(Index));
            }
            try
            {
                _context.LedgerEntries.RemoveRange(toRemove);
                await _context.SaveChangesAsync();
                await _ledgerPostingService.RecalcAllCustomerBalancesAsync();
                TempData["Success"] = $"تم حذف {toRemove.Count} قيد رصيد افتتاحي للعملاء. يمكنك الآن إعادة استيراد أرصدة العملاء من شاشة الاستيراد.";
            }
            catch (DbUpdateException ex)
            {
                TempData["Error"] = "لم يتم الحذف: " + (ex.InnerException?.Message ?? ex.Message);
            }
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // BulkDelete — حذف مجموعة من القيود المحددة
        // ملاحظة: يفضّل استخدامه في بيئة تجريبية أو بصلاحيات خاصة جداً.
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(int[] ids)
        {
            // لو المستخدم لم يحدد أى قيد
            if (ids == null || ids.Length == 0)
            {
                TempData["Error"] = "لم يتم اختيار أى قيد للحذف.";
                return RedirectToAction(nameof(Index));
            }

            // نحضر القيود المطابقة للأرقام المختارة
            var entries = await _context.LedgerEntries
                                        .Where(e => ids.Contains(e.Id))
                                        .ToListAsync();

            if (entries.Count == 0)
            {
                TempData["Error"] = "لم يتم العثور على القيود المحددة.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _context.LedgerEntries.RemoveRange(entries);
                await _context.SaveChangesAsync();

                // إعادة حساب أرصدة العملاء من القيود المتبقية (لضمان توافق تقارير الربح/الأرصدة)
                await _ledgerPostingService.RecalcAllCustomerBalancesAsync();

                TempData["Success"] = $"تم حذف {entries.Count} من القيود المحددة.";
            }
            catch (DbUpdateException)
            {
                // في حالة وجود قيود علاقات أو قيود أخرى
                TempData["Error"] = "لا يمكن حذف بعض القيود بسبب ارتباطها بتقارير أو جداول أخرى.";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // DeleteAll — حذف جميع القيود
        // تنبيه: يُفضّل استخدامه لتهيئة قاعدة البيانات في بيئة TEST فقط.
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _context.LedgerEntries.ToListAsync();

            if (all.Count == 0)
            {
                TempData["Error"] = "لا توجد قيود لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _context.LedgerEntries.RemoveRange(all);
                await _context.SaveChangesAsync();

                // إعادة حساب أرصدة العملاء (تصفيرها عند عدم وجود قيود)
                await _ledgerPostingService.RecalcAllCustomerBalancesAsync();

                TempData["Success"] = "تم حذف جميع القيود من دفتر الأستاذ.";
            }
            catch (DbUpdateException)
            {
                TempData["Error"] = "لا يمكن حذف جميع القيود بسبب وجود ارتباطات محاسبية أخرى.";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
