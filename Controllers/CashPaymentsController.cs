using System;                                     // متغيرات التاريخ DateTime
using System.Collections.Generic;                 // List, Dictionary
using System.Globalization;                       // CultureInfo للتواريخ عند التصدير
using System.Linq;                                // LINQ: Where / OrderBy
using System.Linq.Expressions;                    // Expression<Func<...>>
using System.Text;                                // StringBuilder للتصدير
using System.Threading.Tasks;                     // async / await
using Microsoft.AspNetCore.Mvc;                   // Controller, IActionResult
using Microsoft.AspNetCore.Mvc.Rendering;         // SelectList
using Microsoft.EntityFrameworkCore;              // AsNoTracking, Include, ToListAsync
using ERP.Data;                                   // AppDbContext الاتصال بقاعدة البيانات
using ERP.Filters;
using ERP.Infrastructure;                         // PagedResult + ApplySearchSort + UserActivityLogger
using ERP.Models;                                 // CashPayment + Customer + Account
using ERP.Security;
using ERP.Services;                               // ILedgerPostingService

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إذون صرف النقدية (CashPayments)
    /// - CRUD كامل (إضافة / تعديل / تفاصيل / حذف).
    /// - شاشة Index بنظام القوائم الموحد (بحث + ترتيب + فلتر كود + فلتر تاريخ).
    /// - تصدير إلى CSV (Excel).
    /// - حذف جماعي + حذف الكل (يفضل استخدامها بحذر).
    /// </summary>
    [RequirePermission("CashPayments.Index")]
    public class CashPaymentsController : Controller
    {
        // كائن الاتصال بقاعدة البيانات
        private readonly AppDbContext _context;   // متغير: السياق الأساسي للتعامل مع الـ DB
        private readonly ILedgerPostingService _ledgerPostingService; // متغير: خدمة الترحيل المحاسبي
        private readonly IUserActivityLogger _activityLogger; // متغير: تسجيل نشاط المستخدمين
        private readonly IPermissionService _permissionService;
        private readonly IUserAccountVisibilityService _accountVisibilityService;

        private const string InvestorAccountCode = "3101";

        public CashPaymentsController(
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
        // دالة مساعدة: تجهيز القوائم المنسدلة (الطرف + الحسابات)
        // تُستخدم فى Create و Edit (GET + POST لو حصل خطأ).
        // =========================================================
        private async Task PopulateDropdownsAsync(int? customerId = null,
                                                  int? cashAccountId = null,
                                                  int? counterAccountId = null)
        {
            var canViewInvestors = await CanViewInvestorsAsync();
            var customerQueryCp = _context.Customers.AsNoTracking().Where(c => c.IsActive == true);
            customerQueryCp = await _accountVisibilityService.ApplyCustomerVisibilityFilterAsync(customerQueryCp);
            // قائمة العملاء / الأطراف مع AccountId في data attribute
            var customers = await customerQueryCp
                .Include(c => c.Account)
                .OrderBy(c => c.CustomerName)
                .Select(c => new
                {
                    c.CustomerId,
                    c.CustomerName,
                    AccountId = c.AccountId ?? 0
                })
                .ToListAsync();

            var customerItems = customers.Select(c => new SelectListItem
            {
                Value = c.CustomerId.ToString(),
                Text = c.CustomerName ?? "",
                Selected = customerId.HasValue && c.CustomerId == customerId.Value
            }).ToList();

            ViewData["CustomerId"] = new SelectList(customerItems, "Value", "Text", customerId?.ToString());
            ViewData["CustomersWithAccounts"] = customers.ToDictionary(c => c.CustomerId, c => c.AccountId);

            var hiddenAccountIds = await _accountVisibilityService.GetHiddenAccountIdsForCurrentUserAsync();
            var cashAccounts = await TreasuryCashAccounts.QueryTreasuryCashBoxes(_context.Accounts.AsNoTracking())
                .Where(a => !hiddenAccountIds.Contains(a.AccountId))
                .OrderBy(a => a.AccountCode == "1101" ? 0 : 1)
                .ThenBy(a => a.AccountCode != null && a.AccountCode.StartsWith("1101") ? 0 : 1)
                .ThenBy(a => a.AccountCode)
                .ThenBy(a => a.AccountName)
                .Select(a => new { a.AccountId, a.AccountName })
                .ToListAsync();

            if (cashAccountId.HasValue && cashAccountId.Value > 0 && cashAccounts.All(a => a.AccountId != cashAccountId.Value))
            {
                var extra = await _context.Accounts.AsNoTracking()
                    .Where(a => a.AccountId == cashAccountId.Value)
                    .Select(a => new { a.AccountId, a.AccountName })
                    .FirstOrDefaultAsync();
                if (extra != null)
                    cashAccounts = cashAccounts.Append(extra).ToList();
            }

            int? selectedCash = cashAccountId;
            if ((selectedCash == null || selectedCash <= 0) && cashAccounts.Count > 0)
                selectedCash = cashAccounts[0].AccountId;
            else if (selectedCash.HasValue && selectedCash > 0 && cashAccounts.All(a => a.AccountId != selectedCash.Value) && cashAccounts.Count > 0)
                selectedCash = cashAccounts[0].AccountId;

            var cashAccountItems = cashAccounts.Select(a => new SelectListItem
            {
                Value = a.AccountId.ToString(),
                Text = a.AccountName ?? "",
                Selected = selectedCash == a.AccountId
            }).ToList();

            ViewData["CashAccountId"] = new SelectList(cashAccountItems, "Value", "Text", selectedCash?.ToString());
            ViewData["TreasuryCashBoxesEmpty"] = cashAccounts.Count == 0;

            // حسابات نشطة للطرف المقابل
            var counterAccounts = await _context.Accounts
                    .AsNoTracking()
                    .Where(a => a.IsActive && (canViewInvestors || a.AccountCode != InvestorAccountCode))
                    .OrderBy(a => a.AccountName)
                    .Select(a => new { a.AccountId, a.AccountName })
                    .ToListAsync();
            
            var counterAccountItems = counterAccounts.Select(a => new SelectListItem
            {
                Value = a.AccountId.ToString(),
                Text = a.AccountName ?? "",
                Selected = counterAccountId.HasValue && counterAccountId.Value == a.AccountId
            }).ToList();
            
            ViewData["CounterAccountId"] = new SelectList(counterAccountItems, "Value", "Text", counterAccountId);
        }

        // =========================================================
        // دالة مساعدة: جلب AccountId للعميل (للاستخدام في AJAX)
        // =========================================================
        [HttpGet]
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

            var hiddenAccountIds = await _accountVisibilityService.GetHiddenAccountIdsForCurrentUserAsync();
            if (hiddenAccountIds.Contains(customer.AccountId.Value))
            {
                return Json(new { success = false, message = "غير مسموح: هذا الحساب/الطرف مخفي عن المستخدم." });
            }

            if (!await CanViewInvestorsAsync())
            {
                var investorAccountId = await _context.Accounts.AsNoTracking()
                    .Where(a => a.AccountCode == InvestorAccountCode)
                    .Select(a => a.AccountId)
                    .FirstOrDefaultAsync();

                if (investorAccountId > 0 && customer.AccountId.Value == investorAccountId)
                    return Json(new { success = false, message = "غير مسموح: حساب المستثمرين غير متاح لهذا المستخدم." });
            }

            return Json(new { success = true, accountId = customer.AccountId.Value });
        }

        // =========================================================
        // دالة خاصة: تجهيز الاستعلام الأساسي + الفلاتر + البحث + الترتيب
        // نستخدمها في Index و Export حتى لا نكرر الكود.
        // =========================================================
        private IQueryable<CashPayment> BuildQuery(
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
            bool restrictedToAllowedOnly,
            string? searchMode = null)
        {
            // (1) الاستعلام الأساسي من جدول إذون الدفع مع ربط العميل والحسابات (بدون تتبّع لتحسين الأداء)
            IQueryable<CashPayment> q = _context.CashPayments
                .AsNoTracking()
                .Include(p => p.Customer)
                .Include(p => p.CashAccount)
                .Include(p => p.CounterAccount);

            if (!canViewInvestors)
                q = q.Where(p =>
                    (p.CashAccount != null && p.CashAccount.AccountCode != InvestorAccountCode) &&
                    (p.CounterAccount != null && p.CounterAccount.AccountCode != InvestorAccountCode));

            // إخفاء أي إذن مرتبط بعميل غير ظاهر (حساب رئيسي أو قيود بحساب مسموح)
            if (hiddenCustomerAccountIds != null && hiddenCustomerAccountIds.Count > 0)
            {
                q = restrictedToAllowedOnly
                    ? q.Where(p => (p.Customer != null && p.Customer.AccountId != null && !hiddenCustomerAccountIds.Contains(p.Customer.AccountId.Value))
                        || (p.Customer != null && (p.Customer.PartyCategory == "Customer" || p.Customer.PartyCategory == "Supplier")
                            && p.CustomerId != null && _context.LedgerEntries.Any(e => e.CustomerId == p.CustomerId && !hiddenCustomerAccountIds.Contains(e.AccountId))))
                    : q.Where(p => p.Customer == null || p.Customer.AccountId == null || !hiddenCustomerAccountIds.Contains(p.Customer.AccountId.Value)
                        || (p.Customer != null && (p.Customer.PartyCategory == "Customer" || p.Customer.PartyCategory == "Supplier")
                            && p.CustomerId != null && _context.LedgerEntries.Any(e => e.CustomerId == p.CustomerId && !hiddenCustomerAccountIds.Contains(e.AccountId))));
            }

            // (2) فلتر كود من/إلى (نعتمد هنا على CashPaymentId كرقم الإذن)
            if (fromCode.HasValue)
                q = q.Where(p => p.CashPaymentId >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(p => p.CashPaymentId <= toCode.Value);

            // (3) فلتر التاريخ: نفلتر حسب تاريخ الإذن PaymentDate
            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                DateTime from = fromDate.Value;
                DateTime to = toDate.Value;

                q = q.Where(p => p.PaymentDate >= from && p.PaymentDate <= to);
            }

            // (4) خرائط البحث: نحدد الأعمدة النصية والرقمية للبحث الموحد

            // الحقول النصية (string) التى يمكن البحث فيها
            var stringFields =
                new Dictionary<string, Expression<Func<CashPayment, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["number"] = p => p.PaymentNumber,                                 // رقم المستند
                    ["desc"] = p => p.Description ?? "",                               // البيان
                    ["customer"] = p => p.Customer != null ? p.Customer.CustomerName : "",
                    ["cashAccount"] = p => p.CashAccount != null ? p.CashAccount.AccountName : "",
                    ["counterAccount"] = p => p.CounterAccount != null ? p.CounterAccount.AccountName : "",
                    ["posted"] = p => p.IsPosted ? "Posted" : "Draft"                  // حالة الترحيل كنص
                };

            // الحقول الرقمية (int) التى يمكن البحث فيها
            var intFields =
                new Dictionary<string, Expression<Func<CashPayment, int>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = p => p.CashPaymentId    // البحث برقم الإذن
                };

            // الحقول المسموح الترتيب عليها
            var orderFields =
                new Dictionary<string, Expression<Func<CashPayment, object>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["CashPaymentId"] = p => p.CashPaymentId,                   // رقم الإذن
                    ["PaymentNumber"] = p => p.PaymentNumber,                   // رقم المستند
                    ["PaymentDate"] = p => p.PaymentDate,                       // تاريخ الإذن
                    ["Amount"] = p => p.Amount,                                 // المبلغ
                    ["IsPosted"] = p => p.IsPosted,                             // حالة الترحيل
                    ["CreatedAt"] = p => p.CreatedAt,                           // تاريخ الإنشاء
                    ["UpdatedAt"] = p => p.UpdatedAt ?? DateTime.MinValue,      // آخر تعديل
                    ["CustomerName"] = p => p.Customer != null ? p.Customer.CustomerName : "",
                    ["CashAccountName"] = p => p.CashAccount != null ? p.CashAccount.AccountName : "",
                    ["CounterAccountName"] = p => p.CounterAccount != null ? p.CounterAccount.AccountName : "",
                    ["Description"] = p => p.Description ?? ""
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
                defaultSortBy: "PaymentDate",     // الترتيب الافتراضي بتاريخ الإذن (الأحدث أولاً)
                searchMode: searchMode);

            return q;
        }

        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        private static IQueryable<CashPayment> ApplyColumnFilters(
            IQueryable<CashPayment> query,
            string? filterCol_id,
            string? filterCol_number,
            string? filterCol_date,
            string? filterCol_customer,
            string? filterCol_cashAccount,
            string? filterCol_counterAccount,
            string? filterCol_amount,
            string? filterCol_posted,
            string? filterCol_desc,
            string? filterCol_idExpr,
            string? filterCol_amountExpr)
        {
            if (!string.IsNullOrWhiteSpace(filterCol_idExpr))
                query = CashVoucherListNumericExpr.ApplyCashPaymentIdExpr(query, filterCol_idExpr);
            else if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) query = query.Where(p => ids.Contains(p.CashPaymentId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_number))
            {
                var vals = filterCol_number.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) query = query.Where(p => p.PaymentNumber != null && vals.Contains(p.PaymentNumber));
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
                    if (dates.Count > 0) query = query.Where(p => dates.Contains(p.PaymentDate.Date));
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_customer))
            {
                var vals = filterCol_customer.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    query = query.Where(p => p.Customer != null && vals.Contains(p.Customer.CustomerName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_cashAccount))
            {
                var vals = filterCol_cashAccount.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    query = query.Where(p => p.CashAccount != null && vals.Contains(p.CashAccount.AccountName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_counterAccount))
            {
                var vals = filterCol_counterAccount.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    query = query.Where(p => p.CounterAccount != null && vals.Contains(p.CounterAccount.AccountName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_amountExpr))
                query = CashVoucherListNumericExpr.ApplyCashPaymentAmountExpr(query, filterCol_amountExpr);
            else if (!string.IsNullOrWhiteSpace(filterCol_amount))
            {
                var vals = filterCol_amount.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0) query = query.Where(p => vals.Contains(p.Amount));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_posted))
            {
                var vals = filterCol_posted.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim().ToLowerInvariant()).Where(x => x == "true" || x == "1" || x == "مرحّل" || x == "false" || x == "0" || x == "مسودة").ToList();
                if (vals.Count > 0)
                {
                    var postTrue = vals.Any(v => v == "true" || v == "1" || v == "مرحّل");
                    var postFalse = vals.Any(v => v == "false" || v == "0" || v == "مسودة");
                    if (postTrue && !postFalse) query = query.Where(p => p.IsPosted);
                    else if (postFalse && !postTrue) query = query.Where(p => !p.IsPosted);
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_desc))
            {
                var vals = filterCol_desc.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    query = query.Where(p => p.Description != null && vals.Any(v => p.Description.Contains(v)));
            }
            return query;
        }

        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var columnLower = (column ?? "").Trim().ToLowerInvariant();
            var canViewInvestors = await CanViewInvestorsAsync();
            IQueryable<CashPayment> q = _context.CashPayments.AsNoTracking()
                .Include(p => p.Customer)
                .Include(p => p.CashAccount)
                .Include(p => p.CounterAccount);

            if (!canViewInvestors)
                q = q.Where(p =>
                    (p.CashAccount != null && p.CashAccount.AccountCode != InvestorAccountCode) &&
                    (p.CounterAccount != null && p.CounterAccount.AccountCode != InvestorAccountCode));

            if (columnLower == "id")
            {
                var ids = await q.Select(p => p.CashPaymentId).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "number")
            {
                var list = await q.Where(p => p.PaymentNumber != null).Select(p => p.PaymentNumber!).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "date")
            {
                var dates = await q.Select(p => p.PaymentDate.Date).Distinct().OrderByDescending(x => x).Take(500).ToListAsync();
                return Json(dates.Select(d => new { value = d.ToString("yyyy-MM-dd"), display = d.ToString("yyyy-MM-dd") }));
            }
            if (columnLower == "customer" || columnLower == "customername")
            {
                var list = await q.Where(p => p.Customer != null).Select(p => p.Customer!.CustomerName).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "cashaccount")
            {
                var list = await q.Where(p => p.CashAccount != null).Select(p => p.CashAccount!.AccountName).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "counteraccount")
            {
                var list = await q.Where(p => p.CounterAccount != null).Select(p => p.CounterAccount!.AccountName).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "amount")
            {
                var list = await q.Select(p => p.Amount).Distinct().OrderBy(x => x).Take(300).ToListAsync();
                return Json(list.Select(v => new { value = v.ToString(CultureInfo.InvariantCulture), display = v.ToString("0.00") }));
            }
            if (columnLower == "posted" || columnLower == "isposted")
            {
                return Json(new[] { new { value = "true", display = "مرحّل" }, new { value = "false", display = "مسودة" } });
            }
            if (columnLower == "created" || columnLower == "createdat")
            {
                var list = await q.Select(p => p.CreatedAt).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd HH:mm"), display = d.ToString("yyyy-MM-dd HH:mm") }));
            }
            if (columnLower == "updated" || columnLower == "updatedat")
            {
                var list = await q.Where(p => p.UpdatedAt.HasValue).Select(p => p.UpdatedAt!.Value).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd HH:mm"), display = d.ToString("yyyy-MM-dd HH:mm") }));
            }
            if (columnLower == "desc" || columnLower == "description")
            {
                var list = await q.Where(p => p.Description != null && p.Description != "").Select(p => p.Description!).Distinct().OrderBy(x => x).Take(300).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v.Length > 50 ? v.Substring(0, 50) + "…" : v }));
            }
            return Json(Array.Empty<object>());
        }

        // =========================================================
        // Index — عرض قائمة إذون الدفع (نظام القوائم الموحد)
        // =========================================================
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",
            string? searchMode = "contains",
            string? sort = "PaymentDate",
            string? dir = "desc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_number = null,
            string? filterCol_date = null,
            string? filterCol_customer = null,
            string? filterCol_cashAccount = null,
            string? filterCol_counterAccount = null,
            string? filterCol_amount = null,
            string? filterCol_amountExpr = null,
            string? filterCol_posted = null,
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
                restrictedOnly,
                searchMode);

            q = ApplyColumnFilters(q, filterCol_id, filterCol_number, filterCol_date, filterCol_customer, filterCol_cashAccount, filterCol_counterAccount, filterCol_amount, filterCol_posted, filterCol_desc, filterCol_idExpr, filterCol_amountExpr);

            var totalAmount = await q.Select(p => (decimal?)p.Amount).SumAsync() ?? 0m;
            int totalCount = await q.CountAsync();

            int effectivePageSize = pageSize;
            if (pageSize == 0)
            {
                effectivePageSize = totalCount == 0 ? 10 : Math.Min(totalCount, 100_000);
                page = 1;
            }

            bool sortDesc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            var items = await q
                .Skip((page - 1) * effectivePageSize)
                .Take(effectivePageSize)
                .ToListAsync();

            var model = new PagedResult<CashPayment>(items, page, pageSize, totalCount)
            {
                Search = search,
                SortColumn = sort,
                SortDescending = sortDesc,
                UseDateRange = useDateRange,
                FromDate = fromDate,
                ToDate = toDate
            };

            ViewBag.Search = search ?? "";
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.SearchMode = string.IsNullOrWhiteSpace(searchMode) ? "contains" : searchMode;
            ViewBag.Sort = sort ?? "PaymentDate";
            ViewBag.Dir = sortDesc ? "desc" : "asc";
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_IdExpr = filterCol_idExpr;
            ViewBag.FilterCol_Number = filterCol_number;
            ViewBag.FilterCol_Date = filterCol_date;
            ViewBag.FilterCol_Customer = filterCol_customer;
            ViewBag.FilterCol_CashAccount = filterCol_cashAccount;
            ViewBag.FilterCol_CounterAccount = filterCol_counterAccount;
            ViewBag.FilterCol_Amount = filterCol_amount;
            ViewBag.FilterCol_AmountExpr = filterCol_amountExpr;
            ViewBag.FilterCol_Posted = filterCol_posted;
            ViewBag.FilterCol_Desc = filterCol_desc;
            ViewBag.DateField = "PaymentDate";
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalCount = totalCount;
            ViewBag.TotalAmount = totalAmount;

            return View(model);
        }

        // =========================================================
        // Details — عرض تفاصيل إذن دفع واحد
        // =========================================================
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null || id <= 0)
                return BadRequest();

            var payment = await _context.CashPayments
                                        .Include(p => p.Customer)
                                        .Include(p => p.CashAccount)
                                        .Include(p => p.CounterAccount)
                                        .FirstOrDefaultAsync(p => p.CashPaymentId == id);

            if (payment == null)
                return NotFound();

            if (!await CanViewInvestorsAsync() &&
                ((payment.CashAccount != null && payment.CashAccount.AccountCode == InvestorAccountCode) ||
                 (payment.CounterAccount != null && payment.CounterAccount.AccountCode == InvestorAccountCode)))
                return NotFound();

            return View(payment);   // Views/CashPayments/Details.cshtml (الفورم العادي)
        }

        // =========================================================
        // Show — عرض إذن الدفع للطباعة
        // =========================================================
        public async Task<IActionResult> Show(int id)
        {
            var payment = await _context.CashPayments
                .Include(p => p.Customer)
                .Include(p => p.CashAccount)
                .Include(p => p.CounterAccount)
                .FirstOrDefaultAsync(p => p.CashPaymentId == id);

            if (payment == null)
                return NotFound();

            if (!await CanViewInvestorsAsync() &&
                ((payment.CashAccount != null && payment.CashAccount.AccountCode == InvestorAccountCode) ||
                 (payment.CounterAccount != null && payment.CounterAccount.AccountCode == InvestorAccountCode)))
                return NotFound();

            return View(payment);
        }

        // =========================================================
        // Create — GET: عرض فورم إضافة إذن جديد
        // =========================================================
        public async Task<IActionResult> Create(int? id = null, int? customerId = null)
        {
            CashPayment model;
            
            // ✅ إذا كان id موجود، نحمّل الإذن الموجود (للتعديل)
            if (id.HasValue && id.Value > 0)
            {
                model = await _context.CashPayments
                    .Include(p => p.Customer)
                    .ThenInclude(c => c.Account)
                    .FirstOrDefaultAsync(p => p.CashPaymentId == id.Value);
                
                if (model == null)
                    return NotFound();
                
                // ✅ إذا كان العميل محددًا، نحفظ هذه المعلومة للواجهة
                if (model.CustomerId.HasValue)
                    ViewBag.LockCustomer = true;
            }
            else
            {
                var hiddenIds = await _accountVisibilityService.GetHiddenAccountIdsForCurrentUserAsync();
                var defaultId = await TreasuryCashAccounts.GetDefaultTreasuryCashBoxAccountIdAsync(_context);
                if (defaultId.HasValue && hiddenIds.Contains(defaultId.Value))
                {
                    defaultId = await TreasuryCashAccounts.QueryTreasuryCashBoxes(_context.Accounts.AsNoTracking())
                        .Where(a => !hiddenIds.Contains(a.AccountId))
                        .OrderBy(a => a.AccountCode == "1101" ? 0 : 1)
                        .ThenBy(a => a.AccountCode)
                        .ThenBy(a => a.AccountName)
                        .Select(a => (int?)a.AccountId)
                        .FirstOrDefaultAsync();
                }

                model = new CashPayment
                {
                    PaymentDate = DateTime.Now.Date,
                    IsPosted = false,
                    Status = "غير مرحلة",
                    CashAccountId = defaultId ?? 0
                };
            }

            // ✅ إذا جاء من صفحة "حجم تعامل عميل" (customerId موجود) ولم يكن id موجود
            if (!id.HasValue && customerId.HasValue && customerId.Value > 0)
            {
                var customer = await _context.Customers
                    .AsNoTracking()
                    .Include(c => c.Account)
                    .FirstOrDefaultAsync(c => c.CustomerId == customerId.Value);

                if (customer != null)
                {
                    model.CustomerId = customer.CustomerId;
                    // ✅ حساب الطرف = حساب العميل تلقائيًا
                    if (customer.AccountId.HasValue)
                    {
                        model.CounterAccountId = customer.AccountId.Value;
                    }
                    // ✅ البيان الافتراضي
                    model.Description = $"دفع للعميل {customer.CustomerName}";
                    // ✅ قفل العميل وحساب الطرف في الواجهة
                    ViewBag.LockCustomer = true;
                }
            }

            await PopulateDropdownsAsync(model.CustomerId, model.CashAccountId > 0 ? (int?)model.CashAccountId : null, model.CounterAccountId > 0 ? (int?)model.CounterAccountId : null);
            return View(model);
        }

        // =========================================================
        // Create — POST: حفظ إذن جديد أو تعديل إذن موجود
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("CashPaymentId,PaymentDate,CustomerId,CashAccountId,CounterAccountId,Amount,Description")]
                                                CashPayment payment)
        {
            // ✅ تجاهل خطأ التحقق لـ PaymentNumber لأنه سيتم توليده تلقائياً
            ModelState.Remove(nameof(CashPayment.PaymentNumber));
            
            // ✅ تسجيل القيم المرسلة للتحقق
            System.Diagnostics.Debug.WriteLine($"DEBUG: CashPaymentId={payment.CashPaymentId}, CashAccountId={payment.CashAccountId}, CounterAccountId={payment.CounterAccountId}, Amount={payment.Amount}");

            if (payment.CustomerId.HasValue && payment.CustomerId.Value > 0)
            {
                var cust = await _context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.CustomerId == payment.CustomerId.Value);
                if (cust != null && !cust.IsActive)
                    ModelState.AddModelError(nameof(CashPayment.CustomerId), "لا يمكن التعامل مع عميل غير نشط. يرجى تفعيل العميل أولاً.");
            }

            if (payment.CashAccountId <= 0)
                ModelState.AddModelError(nameof(CashPayment.CashAccountId), "يجب اختيار الخزينة / الصندوق.");
            else
            {
                var hiddenCash = await _accountVisibilityService.GetHiddenAccountIdsForCurrentUserAsync();
                if (!await TreasuryCashAccounts.IsAllowedTreasuryCashBoxForUserAsync(_context, payment.CashAccountId, hiddenCash))
                    ModelState.AddModelError(nameof(CashPayment.CashAccountId), "يجب اختيار خزينة/صندوق صالح من القائمة.");
            }
            
            if (!ModelState.IsValid)
            {
                // لو فيه أخطاء في البيانات نرجع لنفس الفورم
                await PopulateDropdownsAsync(payment.CustomerId, payment.CashAccountId, payment.CounterAccountId);
                if (payment.CustomerId.HasValue)
                    ViewBag.LockCustomer = true;
                return View(payment);
            }

            try
            {
                // =========================================================
                // إذا كان CashPaymentId > 0، فهذا تعديل
                // =========================================================
                if (payment.CashPaymentId > 0)
                {
                    return await Edit(payment.CashPaymentId, payment);
                }

                // =========================================================
                // إنشاء إذن جديد
                // =========================================================
                // تعيين قيم الإنشاء
                payment.CreatedAt = DateTime.UtcNow;
                payment.IsPosted = false;       // مبدئياً غير مرحّل
                payment.PostedAt = null;
                payment.PostedBy = null;
                payment.Status = "غير مرحلة";   // ✅ الحالة الافتراضية

                if (string.IsNullOrWhiteSpace(payment.CreatedBy))
                {
                    payment.CreatedBy = User?.Identity?.Name ?? "System";
                }

                // ✅ توليد رقم المستند من CashPaymentId بعد الحفظ
                _context.Add(payment);
                await _context.SaveChangesAsync();
                
                // ✅ الآن CashPaymentId موجود بعد الحفظ
                payment.PaymentNumber = payment.CashPaymentId.ToString();
                await _context.SaveChangesAsync();

                // =========================================================
                // ترحيل محاسبي (LedgerEntries + تحديث حساب العميل)
                // =========================================================
                string? postedBy = User?.Identity?.Name ?? "SYSTEM";
                await _ledgerPostingService.PostCashPaymentAsync(payment.CashPaymentId, postedBy);

                // =========================================================
                // إغلاق الإذن (تغيير الحالة إلى "مغلق")
                // =========================================================
                payment.Status = "مغلق";
                payment.IsPosted = true;
                await _context.SaveChangesAsync();

                // =========================================================
                // تسجيل في اللوج
                // =========================================================
                await _activityLogger.LogAsync(
                    UserActionType.Create,
                    "CashPayment",
                    payment.CashPaymentId,
                    $"إضافة وإغلاق إذن دفع رقم {payment.PaymentNumber} بمبلغ {payment.Amount}"
                );

                TempData["CashPaymentSuccess"] = "تم حفظ وترحيل وإغلاق إذن الدفع بنجاح.";
                
                // ✅ إعادة تحميل الصفحة بنفس الإذن (بدون توجيه للقائمة)
                await PopulateDropdownsAsync(payment.CustomerId, payment.CashAccountId, payment.CounterAccountId);
                if (payment.CustomerId.HasValue)
                    ViewBag.LockCustomer = true;
                return View(payment);
            }
            catch (Exception ex)
            {
                TempData["CashPaymentError"] = $"حدث خطأ أثناء حفظ إذن الدفع: {ex.Message}";
                await PopulateDropdownsAsync(payment.CustomerId, payment.CashAccountId, payment.CounterAccountId);
                if (payment.CustomerId.HasValue)
                    ViewBag.LockCustomer = true;
                return View(payment);
            }
        }

        // =========================================================
        // Open — فتح إذن مغلق للتعديل
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("CashPayments.Open")]
        public async Task<IActionResult> Open(int id)
        {
            try
            {
                var payment = await _context.CashPayments
                    .FirstOrDefaultAsync(p => p.CashPaymentId == id);

                if (payment == null)
                {
                    TempData["CashPaymentError"] = "الإذن غير موجود.";
                    return RedirectToAction(nameof(Index));
                }

                // ================================
                // 1) لازم يكون مغلق عشان ينفع "فتح"
                // ================================
                if (payment.Status != "مغلق")
                {
                    TempData["CashPaymentError"] = "هذا الإذن غير مغلق، لا يوجد ما يمكن فتحه.";
                    return RedirectToAction(nameof(Create), new { id });
                }

                // ================================
                // 2) فتح الإذن للتعديل
                // ================================
                payment.Status = "مفتوحة للتعديل";
                payment.IsPosted = false;
                payment.PostedAt = null;
                payment.PostedBy = null;
                payment.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                // ================================
                // 3) تسجيل نشاط
                // ================================
                await _activityLogger.LogAsync(
                    actionType: UserActionType.Edit,
                    entityName: "CashPayment",
                    entityId: payment.CashPaymentId,
                    description: $"فتح إذن دفع رقم {payment.PaymentNumber} للتعديل"
                );

                TempData["CashPaymentSuccess"] = "تم فتح الإذن للتعديل بنجاح.";
                return RedirectToAction(nameof(Create), new { id });
            }
            catch (Exception ex)
            {
                TempData["CashPaymentError"] = $"حدث خطأ: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        // =========================================================
        // Edit — GET: تعديل إذن دفع (يستخدم Create view)
        // =========================================================
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null || id <= 0)
                return BadRequest();

            var payment = await _context.CashPayments
                .Include(p => p.Customer)
                .ThenInclude(c => c.Account)
                .FirstOrDefaultAsync(p => p.CashPaymentId == id);

            if (payment == null)
                return NotFound();

            // ✅ إذا كان العميل محددًا، نحفظ هذه المعلومة للواجهة
            if (payment.CustomerId.HasValue)
                ViewBag.LockCustomer = true;

            await PopulateDropdownsAsync(payment.CustomerId, payment.CashAccountId, payment.CounterAccountId);
            return View("Create", payment);
        }

        // =========================================================
        // Edit — POST: حفظ التعديل (يستخدم Create POST)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id,
            [Bind("CashPaymentId,PaymentDate,CustomerId,CashAccountId,CounterAccountId,Amount,Description")]
            CashPayment payment)
        {
            if (id != payment.CashPaymentId)
                return NotFound();

            // ✅ تجاهل خطأ التحقق لـ PaymentNumber
            ModelState.Remove(nameof(CashPayment.PaymentNumber));

            if (payment.CashAccountId <= 0)
                ModelState.AddModelError(nameof(CashPayment.CashAccountId), "يجب اختيار الخزينة / الصندوق.");
            else
            {
                var hiddenPayEdit = await _accountVisibilityService.GetHiddenAccountIdsForCurrentUserAsync();
                if (!await TreasuryCashAccounts.IsAllowedTreasuryCashBoxForUserAsync(_context, payment.CashAccountId, hiddenPayEdit))
                    ModelState.AddModelError(nameof(CashPayment.CashAccountId), "يجب اختيار خزينة/صندوق صالح من القائمة.");
            }

            if (!ModelState.IsValid)
            {
                await PopulateDropdownsAsync(payment.CustomerId, payment.CashAccountId, payment.CounterAccountId);
                if (payment.CustomerId.HasValue)
                    ViewBag.LockCustomer = true;
                return View("Create", payment);
            }

            try
            {
                // =========================================================
                // 1) جلب السجل الأصلي من قاعدة البيانات
                // =========================================================
                var existing = await _context.CashPayments
                    .Include(p => p.Customer)
                    .FirstOrDefaultAsync(p => p.CashPaymentId == id);

                if (existing == null)
                    return NotFound();

                // =========================================================
                // 2) حفظ Snapshot للقيم القديمة (للتسجيل في اللوج)
                // =========================================================
                var oldValues = new
                {
                    Amount = existing.Amount,
                    CashAccountId = existing.CashAccountId,
                    CounterAccountId = existing.CounterAccountId,
                    PaymentDate = existing.PaymentDate,
                    Description = existing.Description,
                    IsPosted = existing.IsPosted,
                    Status = existing.Status
                };

                // =========================================================
                // 3) تحديث الحقول المسموح بها
                // =========================================================
                existing.PaymentDate = payment.PaymentDate;
                existing.CustomerId = payment.CustomerId;
                existing.CashAccountId = payment.CashAccountId;
                existing.CounterAccountId = payment.CounterAccountId;
                existing.Amount = payment.Amount;
                existing.Description = payment.Description;
                existing.UpdatedAt = DateTime.UtcNow;

                // =========================================================
                // 4) حفظ التعديلات أولاً
                // =========================================================
                await _context.SaveChangesAsync();

                // =========================================================
                // 5) ترحيل محاسبي (يعكس القديم ويعمل جديد إذا كان مفتوح)
                // =========================================================
                if (!existing.IsPosted)
                {
                    string? postedBy = User?.Identity?.Name ?? "SYSTEM";
                    await _ledgerPostingService.PostCashPaymentAsync(existing.CashPaymentId, postedBy);
                }

                // =========================================================
                // 6) إغلاق الإذن بعد التعديل
                // =========================================================
                existing.Status = "مغلق";
                existing.IsPosted = true;
                await _context.SaveChangesAsync();

                // =========================================================
                // 7) تسجيل في اللوج
                // =========================================================
                await _activityLogger.LogAsync(
                    UserActionType.Edit,
                    "CashPayment",
                    existing.CashPaymentId,
                    $"تعديل إذن دفع رقم {existing.PaymentNumber}",
                    oldValues: System.Text.Json.JsonSerializer.Serialize(oldValues),
                    newValues: System.Text.Json.JsonSerializer.Serialize(new
                    {
                        Amount = existing.Amount,
                        CashAccountId = existing.CashAccountId,
                        CounterAccountId = existing.CounterAccountId,
                        PaymentDate = existing.PaymentDate,
                        Description = existing.Description,
                        IsPosted = existing.IsPosted,
                        Status = existing.Status
                    })
                );

                TempData["CashPaymentSuccess"] = "تم تعديل وإغلاق إذن الدفع بنجاح.";
                
                // ✅ إعادة تحميل الصفحة بنفس الإذن
                await PopulateDropdownsAsync(existing.CustomerId, existing.CashAccountId, existing.CounterAccountId);
                if (existing.CustomerId.HasValue)
                    ViewBag.LockCustomer = true;
                return View("Create", existing);
            }
            catch (Exception ex)
            {
                TempData["CashPaymentError"] = $"حدث خطأ: {ex.Message}";
                await PopulateDropdownsAsync(payment.CustomerId, payment.CashAccountId, payment.CounterAccountId);
                if (payment.CustomerId.HasValue)
                    ViewBag.LockCustomer = true;
                return View("Create", payment);
            }
        }

        // =========================================================
        // Delete — GET: تأكيد حذف إذن دفع
        // =========================================================
        [RequirePermission("CashPayments.Delete")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null || id <= 0)
                return BadRequest();

            var payment = await _context.CashPayments
                                        .Include(p => p.Customer)
                                        .Include(p => p.CashAccount)
                                        .Include(p => p.CounterAccount)
                                        .FirstOrDefaultAsync(p => p.CashPaymentId == id);

            if (payment == null)
                return NotFound();

            return View(payment);
        }

        // =========================================================
        // DeleteConfirmed — POST: تنفيذ الحذف
        // =========================================================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [RequirePermission("CashPayments.Delete")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var payment = await _context.CashPayments
                .Include(p => p.Customer)
                .FirstOrDefaultAsync(p => p.CashPaymentId == id);
            if (payment == null)
            {
                TempData["CashPaymentError"] = "إذن الدفع غير موجود.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                if (payment.IsPosted)
                {
                    await _ledgerPostingService.ReverseForHeaderDeleteAsync(
                        Models.LedgerSourceType.Payment,
                        payment.CashPaymentId,
                        User?.Identity?.Name ?? "SYSTEM",
                        "حذف إذن دفع نقدية");
                }

                var oldValues = System.Text.Json.JsonSerializer.Serialize(new
                {
                    payment.PaymentNumber,
                    payment.Amount,
                    payment.CustomerId,
                    payment.PaymentDate,
                    payment.IsPosted
                });

                _context.CashPayments.Remove(payment);
                await _context.SaveChangesAsync();
                await _ledgerPostingService.RecalcAllCustomerBalancesAsync();

                await _activityLogger.LogAsync(
                    UserActionType.Delete,
                    "CashPayment",
                    id,
                    $"حذف إذن دفع رقم {payment.PaymentNumber}",
                    oldValues: oldValues);

                TempData["CashPaymentSuccess"] = "تم حذف إذن الدفع.";
            }
            catch (Exception ex)
            {
                TempData["CashPaymentError"] = $"حدث خطأ أثناء حذف إذن الدفع: {ex.Message}";
            }
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // Export — تصدير قائمة الإذون إلى CSV (يفتح في Excel)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy = "all",
            string? searchMode = "contains",
            string? sort = "PaymentDate",
            string? dir = "desc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_number = null,
            string? filterCol_date = null,
            string? filterCol_customer = null,
            string? filterCol_cashAccount = null,
            string? filterCol_counterAccount = null,
            string? filterCol_amount = null,
            string? filterCol_amountExpr = null,
            string? filterCol_posted = null,
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
                restrictedOnly,
                searchMode);

            q = ApplyColumnFilters(q, filterCol_id, filterCol_number, filterCol_date, filterCol_customer, filterCol_cashAccount, filterCol_counterAccount, filterCol_amount, filterCol_posted, filterCol_desc, filterCol_idExpr, filterCol_amountExpr);

            var list = await q.ToListAsync();

            var sb = new StringBuilder();

            sb.AppendLine("رقم الإذن,رقم المستند,تاريخ الإذن,الطرف,حساب الصندوق/البنك,حساب الطرف,المبلغ,مرحّل؟,تاريخ الإنشاء,أنشأه,تاريخ الترحيل,مرحّل بواسطة,البيان");

            static string Q(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

            foreach (var p in list)
            {
                string customerName = p.Customer?.CustomerName ?? "";
                string cashAccountName = p.CashAccount?.AccountName ?? "";
                string counterAccountName = p.CounterAccount?.AccountName ?? "";

                string line = string.Join(",",
                    p.CashPaymentId,
                    Q(p.PaymentNumber),
                    p.PaymentDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    Q(customerName),
                    Q(cashAccountName),
                    Q(counterAccountName),
                    p.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                    p.IsPosted ? "نعم" : "لا",
                    p.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    Q(p.CreatedBy),
                    p.PostedAt.HasValue
                        ? p.PostedAt.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                        : "",
                    Q(p.PostedBy),
                    Q(p.Description)
                );

                sb.AppendLine(line);
            }

            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString());
            var fileName = ExcelExportNaming.ArabicTimestampedFileName("إذون دفع نقدية", ".csv");
            const string contentType = "text/csv; charset=utf-8";

            return File(bytes, contentType, fileName);
        }

        // =========================================================
        // BulkDelete — حذف مجموعة من الإذون المحددة
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("CashPayments.BulkDelete")]
        public async Task<IActionResult> BulkDelete(int[] ids)
        {
            // لو المستخدم لم يحدد أى إذن
            if (ids == null || ids.Length == 0)
            {
                TempData["CashPaymentError"] = "لم يتم اختيار أى إذن للحذف.";
                return RedirectToAction(nameof(Index));
            }

            // نحضر الإذون المطابقة للأرقام المختارة
            var payments = await _context.CashPayments
                                         .Where(p => ids.Contains(p.CashPaymentId))
                                         .ToListAsync();

            if (payments.Count == 0)
            {
                TempData["CashPaymentError"] = "لم يتم العثور على الإذون المحددة.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                foreach (var p in payments.Where(x => x.IsPosted))
                {
                    await _ledgerPostingService.ReverseForHeaderDeleteAsync(
                        Models.LedgerSourceType.Payment,
                        p.CashPaymentId,
                        User?.Identity?.Name ?? "SYSTEM",
                        "حذف جماعي إذون دفع");
                }

                foreach (var p in payments)
                {
                    await _activityLogger.LogAsync(
                        UserActionType.Delete,
                        "CashPayment",
                        p.CashPaymentId,
                        $"حذف إذن دفع رقم {p.PaymentNumber} (حذف جماعي)");
                }

                _context.CashPayments.RemoveRange(payments);
                await _context.SaveChangesAsync();
                await _ledgerPostingService.RecalcAllCustomerBalancesAsync();

                TempData["CashPaymentSuccess"] = $"تم حذف {payments.Count} من إذون الدفع المحددة.";
            }
            catch (Exception ex)
            {
                TempData["CashPaymentError"] = $"لا يمكن حذف بعض الإذون: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // DeleteAll — حذف جميع إذون الدفع
        // (غالباً تستخدم في بيئة تجريبية وليس في الإنتاج)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("CashPayments.DeleteAll")]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _context.CashPayments.ToListAsync();

            if (all.Count == 0)
            {
                TempData["CashPaymentError"] = "لا توجد إذون دفع لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                foreach (var p in all.Where(x => x.IsPosted))
                {
                    await _ledgerPostingService.ReverseForHeaderDeleteAsync(
                        Models.LedgerSourceType.Payment,
                        p.CashPaymentId,
                        User?.Identity?.Name ?? "SYSTEM",
                        "حذف جميع إذون الدفع");
                }

                foreach (var p in all)
                {
                    await _activityLogger.LogAsync(
                        UserActionType.Delete,
                        "CashPayment",
                        p.CashPaymentId,
                        $"حذف إذن دفع رقم {p.PaymentNumber} (حذف الكل)");
                }

                _context.CashPayments.RemoveRange(all);
                await _context.SaveChangesAsync();
                await _ledgerPostingService.RecalcAllCustomerBalancesAsync();

                TempData["CashPaymentSuccess"] = "تم حذف جميع إذون الدفع.";
            }
            catch (Exception ex)
            {
                TempData["CashPaymentError"] = $"لا يمكن حذف جميع إذون الدفع: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // دالة مساعدة: هل إذن الدفع موجود؟
        // =========================================================
        private bool CashPaymentExists(int id)
        {
            return _context.CashPayments.Any(e => e.CashPaymentId == id);
        }
    }
}
