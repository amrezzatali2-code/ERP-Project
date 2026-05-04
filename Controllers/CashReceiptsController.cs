using System;                                     // للتعامل مع التواريخ DateTime
using System.Collections.Generic;                 // القوائم Dictionary / List
using System.Globalization;                       // تنسيق التواريخ فى التصدير
using System.Linq;                                // أوامر LINQ مثل Where / OrderBy
using System.Linq.Expressions;                    // Expression<Func<...>>
using System.Text;                                // StringBuilder للتصدير
using System.Threading.Tasks;                     // async / await
using Microsoft.AspNetCore.Mvc;                   // Controller, IActionResult
using Microsoft.AspNetCore.Mvc.Rendering;         // SelectList للقوائم المنسدلة
using Microsoft.EntityFrameworkCore;              // AsNoTracking, Include, ToListAsync
using ERP.Data;                                   // AppDbContext الاتصال بقاعدة البيانات
using ERP.Filters;
using ERP.Infrastructure;                         // PagedResult + ApplySearchSort + UserActivityLogger
using ERP.Models;                                 // CashReceipt + Account + Customer
using ERP.Security;
using ERP.Services;                               // ILedgerPostingService

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إذون استلام النقدية (CashReceipts)
    /// - نظام القوائم الموحد في Index (بحث + ترتيب + فلترة + حذف جماعي + تصدير).
    /// - CRUD كامل: Create / Edit / Details / Delete.
    /// - زر حفظ = حفظ + ترحيل محاسبي (LedgerEntries + تحديث حساب العميل)
    /// - زر فتح = فتح للتعديل (لا يعكس، الحفظ هو الذي يعكس)
    /// </summary>
    [RequirePermission("CashReceipts.Index")]
    public class CashReceiptsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IUserActivityLogger _activityLogger;
        private readonly ILedgerPostingService _ledgerPostingService;
        private readonly IPermissionService _permissionService;
        private readonly IUserAccountVisibilityService _accountVisibilityService;

        private const string InvestorAccountCode = "3101";

        public CashReceiptsController(
            AppDbContext context,
            IUserActivityLogger activityLogger,
            ILedgerPostingService ledgerPostingService,
            IPermissionService permissionService,
            IUserAccountVisibilityService accountVisibilityService)
        {
            _context = context;
            _activityLogger = activityLogger;
            _ledgerPostingService = ledgerPostingService;
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
                                                  int? counterAccountId = null,
                                                  int? areaId = null,
                                                  int? distributorUserId = null)
        {
            var canViewInvestors = await CanViewInvestorsAsync();
            var customerQueryCr = _context.Customers.AsNoTracking().Where(c => c.IsActive == true);
            if (areaId.HasValue && areaId.Value > 0)
                customerQueryCr = customerQueryCr.Where(c => c.AreaId == areaId.Value);
            if (distributorUserId.HasValue && distributorUserId.Value > 0)
                customerQueryCr = customerQueryCr.Where(c => c.UserId == distributorUserId.Value);
            customerQueryCr = await _accountVisibilityService.ApplyCustomerVisibilityFilterAsync(customerQueryCr);
            // قائمة العملاء / الأطراف مع AccountId في data attribute
            var customers = await customerQueryCr
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

            ViewData["CustomerId"] = new SelectList(customerItems, "Value", "Text", customerId?.ToString()); // ✅ تحويل إلى string
            ViewData["CustomersWithAccounts"] = customers.ToDictionary(c => c.CustomerId, c => c.AccountId);

            // خزائن/صناديق فقط (حسابات 1101* / 1102* أو اسم خزينة/بنك/صندوق) — غير المخفية عن المستخدم
            var hiddenAccountIds = await _accountVisibilityService.GetHiddenAccountIdsForCurrentUserAsync();
            var cashAccounts = await TreasuryCashAccounts.QueryTreasuryCashBoxes(_context.Accounts.AsNoTracking())
                .Where(a => !hiddenAccountIds.Contains(a.AccountId))
                .OrderBy(a => a.AccountCode == "1101" ? 0 : 1)
                .ThenBy(a => a.AccountCode != null && a.AccountCode.StartsWith("1101") ? 0 : 1)
                .ThenBy(a => a.AccountCode)
                .ThenBy(a => a.AccountName)
                .Select(a => new { a.AccountId, a.AccountName })
                .ToListAsync();

            // إن كان الإذن يشير لحساب قديم خارج قائمة الخزائن — نضيفه للعرض
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
            var counterAccountsQ = _context.Accounts
                    .AsNoTracking()
                    .Where(a => a.IsActive)
                    .OrderBy(a => a.AccountName)
                    .Select(a => new { a.AccountId, a.AccountName });

            if (!canViewInvestors)
            {
                var investorAccountId = await _context.Accounts.AsNoTracking()
                    .Where(a => a.AccountCode == InvestorAccountCode)
                    .Select(a => a.AccountId)
                    .FirstOrDefaultAsync();

                if (investorAccountId > 0)
                    counterAccountsQ = counterAccountsQ.Where(a => a.AccountId != investorAccountId);
            }

            var counterAccounts = await counterAccountsQ.ToListAsync();
            
            // ✅ إنشاء SelectList باستخدام SelectListItem مباشرة
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

            // لو الحساب المحاسبي الخاص بالعميل مخفي عن المستخدم → لا نرجع AccountId
            var hiddenAccountIds = await _accountVisibilityService.GetHiddenAccountIdsForCurrentUserAsync();
            if (hiddenAccountIds.Contains(customer.AccountId.Value))
                return Json(new { success = false, message = "غير مسموح: هذا الحساب/الطرف مخفي عن المستخدم." });

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

        [HttpGet]
        public async Task<IActionResult> GetBatchCustomers(int? areaId, int? distributorEmployeeId = null)
        {
            var query = _context.Customers.AsNoTracking()
                .Where(c => c.IsActive);
            query = await _accountVisibilityService.ApplyCustomerVisibilityFilterAsync(query);

            var allItems = await query
                .OrderBy(c => c.CustomerName)
                .Select(c => new
                {
                    id = c.CustomerId,
                    name = c.CustomerName
                })
                .ToListAsync();

            return Json(allItems);
        }

        [HttpGet]
        public async Task<IActionResult> GetBatchCustomerByCode(int? areaId, string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return Json(new { found = false });

            var normalizedCode = code.Trim();
            var parsedNumeric = int.TryParse(normalizedCode, out var parsedId);

            var query = _context.Customers.AsNoTracking()
                .Where(c => c.IsActive);
            query = await _accountVisibilityService.ApplyCustomerVisibilityFilterAsync(query);

            var item = await query
                .Where(c =>
                    (parsedNumeric && c.CustomerId == parsedId) ||
                    (c.ExternalCode != null && c.ExternalCode.Trim() == normalizedCode))
                .OrderBy(c => c.CustomerName)
                .Select(c => new
                {
                    id = c.CustomerId,
                    name = c.CustomerName,
                    code = c.ExternalCode
                })
                .FirstOrDefaultAsync();

            if (item == null)
            {
                var fallbackQuery = _context.Customers.AsNoTracking().Where(c => c.IsActive);
                fallbackQuery = await _accountVisibilityService.ApplyCustomerVisibilityFilterAsync(fallbackQuery);
                item = await fallbackQuery
                    .Where(c =>
                        (parsedNumeric && c.CustomerId == parsedId) ||
                        (c.ExternalCode != null && c.ExternalCode.Trim() == normalizedCode))
                    .OrderBy(c => c.CustomerName)
                    .Select(c => new
                    {
                        id = c.CustomerId,
                        name = c.CustomerName,
                        code = c.ExternalCode
                    })
                    .FirstOrDefaultAsync();
            }

            if (item == null)
                return Json(new { found = false });

            return Json(new
            {
                found = true,
                id = item.id,
                name = item.name,
                code = item.code
            });
        }

        private async Task SetGroupedContextViewBagAsync(int? groupAreaId, int? groupDistributorUserId, DateTime? groupDate)
        {
            ViewBag.GroupAreaId = groupAreaId;
            ViewBag.GroupDistributorUserId = groupDistributorUserId;
            ViewBag.GroupDate = groupDate?.Date;
            ViewBag.GroupMode = groupAreaId.HasValue && groupAreaId.Value > 0 && groupDistributorUserId.HasValue && groupDistributorUserId.Value > 0;

            if (groupAreaId.HasValue && groupAreaId.Value > 0)
                ViewBag.GroupAreaName = await _context.Areas.AsNoTracking()
                    .Where(a => a.AreaId == groupAreaId.Value)
                    .Select(a => a.AreaName)
                    .FirstOrDefaultAsync();

            if (groupDistributorUserId.HasValue && groupDistributorUserId.Value > 0)
            {
                var employeeName = await _context.Employees.AsNoTracking()
                    .Where(e => e.UserId == groupDistributorUserId.Value)
                    .OrderByDescending(e => e.IsActive)
                    .ThenBy(e => e.FullName)
                    .Select(e => e.FullName)
                    .FirstOrDefaultAsync();

                ViewBag.GroupDistributorName = !string.IsNullOrWhiteSpace(employeeName)
                    ? employeeName
                    : await _context.Users.AsNoTracking()
                        .Where(u => u.UserId == groupDistributorUserId.Value)
                        .Select(u => u.DisplayName != null && u.DisplayName != "" ? u.DisplayName : u.UserName)
                        .FirstOrDefaultAsync();
            }
        }

        private async Task PopulateCreateNavigationAsync(int? currentId)
        {
            var canViewInvestors = await CanViewInvestorsAsync();
            var hiddenCustomerAccountIds = (await _accountVisibilityService.GetHiddenAccountIdsForCurrentUserAsync()).ToList();
            var restrictedOnly = false;

            var ids = await BuildQuery(
                    search: null,
                    searchBy: null,
                    sort: "CashReceiptId",
                    dir: "asc",
                    useDateRange: false,
                    fromDate: null,
                    toDate: null,
                    fromCode: null,
                    toCode: null,
                    canViewInvestors: canViewInvestors,
                    hiddenCustomerAccountIds: hiddenCustomerAccountIds,
                    restrictedToAllowedOnly: restrictedOnly,
                    searchMode: null)
                .Select(x => x.CashReceiptId)
                .Distinct()
                .OrderBy(x => x)
                .ToArrayAsync();

            int current = currentId.GetValueOrDefault();
            int first = ids.Length > 0 ? ids[0] : 0;
            int last = ids.Length > 0 ? ids[ids.Length - 1] : 0;
            int prev = 0;
            int next = 0;

            if (current > 0 && ids.Length > 0)
            {
                var idx = Array.IndexOf(ids, current);
                if (idx >= 0)
                {
                    prev = idx > 0 ? ids[idx - 1] : 0;
                    next = idx < ids.Length - 1 ? ids[idx + 1] : 0;
                }
                else
                {
                    prev = last;
                    next = first;
                }
            }
            else if (ids.Length > 0)
            {
                prev = last;
                next = first;
            }

            ViewBag.NavCurrentId = current > 0 ? current : (int?)null;
            ViewBag.NavFirstId = first > 0 ? first : (int?)null;
            ViewBag.NavLastId = last > 0 ? last : (int?)null;
            ViewBag.NavPrevId = prev > 0 ? prev : (int?)null;
            ViewBag.NavNextId = next > 0 ? next : (int?)null;
        }

        private async Task<(int? DistributorUserId, string? DistributorName)> ResolveDistributorFromEmployeeAsync(int? distributorEmployeeId)
        {
            if (!distributorEmployeeId.HasValue || distributorEmployeeId.Value <= 0)
                return (null, null);

            var employee = await _context.Employees.AsNoTracking()
                .Where(e => e.Id == distributorEmployeeId.Value)
                .Select(e => new { e.UserId, e.FullName })
                .FirstOrDefaultAsync();

            if (employee == null)
                return (null, null);

            return (employee.UserId, employee.FullName);
        }

        private async Task PopulateBatchCreateLookupsAsync(int? areaId, int? distributorEmployeeId)
        {
            ViewBag.Areas = new SelectList(
                await _context.Areas.AsNoTracking().OrderBy(a => a.AreaName).ToListAsync(),
                "AreaId",
                "AreaName",
                areaId);

            var distributors = await _context.Employees.AsNoTracking()
                .Where(e => e.IsActive)
                .OrderBy(e => e.FullName)
                .Select(e => new
                {
                    EmployeeId = e.Id,
                    UserId = e.UserId,
                    Label = string.IsNullOrWhiteSpace(e.Code) ? e.FullName : (e.FullName + " - " + e.Code)
                })
                .ToListAsync();
            ViewBag.Distributors = new SelectList(distributors, "EmployeeId", "Label", distributorEmployeeId);

            if (areaId.HasValue && areaId.Value > 0 && distributorEmployeeId.HasValue && distributorEmployeeId.Value > 0)
            {
                var customersQ = _context.Customers.AsNoTracking()
                    .Where(c => c.IsActive);
                customersQ = await _accountVisibilityService.ApplyCustomerVisibilityFilterAsync(customersQ);
                var customers = await customersQ
                    .OrderBy(c => c.CustomerName)
                    .ToListAsync();
                ViewBag.BatchCustomers = new SelectList(customers, "CustomerId", "CustomerName");
            }
            else
            {
                ViewBag.BatchCustomers = new SelectList(Array.Empty<SelectListItem>(), "Value", "Text");
            }
        }

        [HttpGet]
        public async Task<IActionResult> BatchCreate(
            int? areaId = null,
            int? distributorEmployeeId = null,
            int? distributorUserId = null,
            DateTime? batchDate = null,
            string? batchTime = null,
            string? autofocus = null)
        {
            if ((!distributorEmployeeId.HasValue || distributorEmployeeId.Value <= 0) && distributorUserId.HasValue && distributorUserId.Value > 0)
            {
                distributorEmployeeId = await _context.Employees.AsNoTracking()
                    .Where(e => e.UserId == distributorUserId.Value)
                    .OrderByDescending(e => e.IsActive)
                    .ThenBy(e => e.FullName)
                    .Select(e => (int?)e.Id)
                    .FirstOrDefaultAsync();
            }

            var (_, resolvedDistributorName) = await ResolveDistributorFromEmployeeAsync(distributorEmployeeId);

            var model = new CashReceiptBatchViewModel
            {
                AreaId = areaId,
                DistributorEmployeeId = distributorEmployeeId,
                DistributorUserId = null,
                BatchDate = (batchDate ?? DateTime.Today).Date,
                BatchTime = DateTime.Now.TimeOfDay
            };

            if (!string.IsNullOrWhiteSpace(batchTime) && TimeSpan.TryParse(batchTime, out var parsedTime))
                model.BatchTime = parsedTime;

            await PopulateBatchCreateLookupsAsync(areaId, distributorEmployeeId);

            if (areaId.HasValue && areaId.Value > 0)
            {
                model.AreaName = await _context.Areas.AsNoTracking()
                    .Where(a => a.AreaId == areaId.Value)
                    .Select(a => a.AreaName)
                    .FirstOrDefaultAsync();
            }

            model.DistributorName = resolvedDistributorName;

            var receiptsQ = _context.CashReceipts.AsNoTracking()
                .Include(r => r.Customer)
                .ThenInclude(c => c.Area)
                .Where(r => r.ReceiptDate.Date == model.BatchDate.Date);

            if (areaId.HasValue && areaId.Value > 0)
                receiptsQ = receiptsQ.Where(r => r.Customer != null && r.Customer.AreaId == areaId.Value);
            if (distributorEmployeeId.HasValue && distributorEmployeeId.Value > 0)
            {
                var distToken = $"[DIST:{distributorEmployeeId.Value}]";
                receiptsQ = receiptsQ.Where(r => r.Description != null && r.Description.Contains(distToken));
            }

            model.Receipts = await receiptsQ
                .OrderByDescending(r => r.CashReceiptId)
                .Take(250)
                .ToListAsync();

            ViewBag.AutoFocusCustomer = string.Equals(autofocus, "customer", StringComparison.OrdinalIgnoreCase);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BatchCreate(
            int? areaId,
            int? distributorEmployeeId,
            DateTime? batchDate,
            string? batchTime,
            int? customerId,
            decimal? amount)
        {
            var date = (batchDate ?? DateTime.Today).Date;
            var distributor = distributorEmployeeId.HasValue && distributorEmployeeId.Value > 0
                ? await _context.Employees.AsNoTracking()
                    .Where(e => e.Id == distributorEmployeeId.Value)
                    .Select(e => new { e.Id, e.FullName, e.UserId })
                    .FirstOrDefaultAsync()
                : null;

            if (!areaId.HasValue || areaId.Value <= 0 || distributor == null)
            {
                TempData["CashReceiptError"] = "اختر المنطقة والموزع أولاً لبدء إذن الاستلام المجمّع.";
                return RedirectToAction(nameof(BatchCreate), new { areaId, distributorEmployeeId, batchDate = date.ToString("yyyy-MM-dd") });
            }

            if (!customerId.HasValue || customerId.Value <= 0)
            {
                TempData["CashReceiptError"] = "اختر اسم العميل.";
                return RedirectToAction(nameof(BatchCreate), new { areaId, distributorEmployeeId, batchDate = date.ToString("yyyy-MM-dd") });
            }

            if (!amount.HasValue || amount.Value <= 0)
            {
                TempData["CashReceiptError"] = "أدخل مبلغًا أكبر من صفر.";
                return RedirectToAction(nameof(BatchCreate), new { areaId, distributorEmployeeId, batchDate = date.ToString("yyyy-MM-dd") });
            }

            var customer = await _context.Customers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.CustomerId == customerId.Value && c.IsActive);

            if (customer == null)
            {
                TempData["CashReceiptError"] = "العميل غير موجود أو غير نشط.";
                return RedirectToAction(nameof(BatchCreate), new { areaId, distributorEmployeeId, batchDate = date.ToString("yyyy-MM-dd") });
            }

            if (!customer.AccountId.HasValue || customer.AccountId.Value <= 0)
            {
                TempData["CashReceiptError"] = "العميل غير مربوط بحساب طرف محاسبي.";
                return RedirectToAction(nameof(BatchCreate), new { areaId, distributorEmployeeId, batchDate = date.ToString("yyyy-MM-dd") });
            }

            try
            {
                var hiddenAccountIds = await _accountVisibilityService.GetHiddenAccountIdsForCurrentUserAsync();
                var cashAccountId = await TreasuryCashAccounts.QueryTreasuryCashBoxes(_context.Accounts.AsNoTracking())
                    .Where(a => !hiddenAccountIds.Contains(a.AccountId))
                    .OrderBy(a => a.AccountCode == "1101" ? 0 : 1)
                    .ThenBy(a => a.AccountCode)
                    .ThenBy(a => a.AccountName)
                    .Select(a => (int?)a.AccountId)
                    .FirstOrDefaultAsync();

                if (!cashAccountId.HasValue || cashAccountId.Value <= 0)
                {
                    TempData["CashReceiptError"] = "لا توجد خزينة/صندوق متاح للحفظ.";
                    return RedirectToAction(nameof(BatchCreate), new { areaId, distributorEmployeeId, batchDate = date.ToString("yyyy-MM-dd") });
                }

                var areaName = await _context.Areas.AsNoTracking()
                    .Where(a => a.AreaId == areaId.Value)
                    .Select(a => a.AreaName)
                    .FirstOrDefaultAsync();
                var distributorName = distributor.FullName;

                var receipt = new CashReceipt
                {
                    ReceiptDate = date,
                    CustomerId = customer.CustomerId,
                    CashAccountId = cashAccountId.Value,
                    CounterAccountId = customer.AccountId.Value,
                    Amount = amount.Value,
                    Description = $"[DIST:{distributor.Id}] إذن استلام مجمع - المنطقة: {areaName ?? "-"} - الموزع: {distributorName ?? "-"}",
                    Status = "غير مرحلة",
                    IsPosted = false,
                    CreatedAt = DateTime.Now,
                    CreatedBy = User?.Identity?.Name ?? "SYSTEM"
                };

                _context.CashReceipts.Add(receipt);
                await _context.SaveChangesAsync();

                receipt.ReceiptNumber = receipt.CashReceiptId.ToString();
                await _context.SaveChangesAsync();

                await _ledgerPostingService.PostCashReceiptAsync(receipt.CashReceiptId, User?.Identity?.Name ?? "SYSTEM");

                TempData["CashReceiptSuccess"] = $"تم إضافة الإيصال رقم {receipt.CashReceiptId} بنجاح.";
                return RedirectToAction(nameof(BatchCreate), new
                {
                    areaId,
                    distributorEmployeeId,
                    batchDate = date.ToString("yyyy-MM-dd"),
                    batchTime,
                    autofocus = "customer"
                });
            }
            catch (Exception ex)
            {
                TempData["CashReceiptError"] = $"حدث خطأ أثناء إضافة الإيصال: {ex.Message}";
                return RedirectToAction(nameof(BatchCreate), new { areaId, distributorEmployeeId, batchDate = date.ToString("yyyy-MM-dd"), batchTime });
            }
        }

        // =========================================================
        // دالة خاصة: تجهيز الاستعلام الأساسي + الفلاتر + البحث + الترتيب
        // نستخدمها في Index و Export حتى لا نكرر الكود.
        // =========================================================
        private IQueryable<CashReceipt> BuildQuery(
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
            // (1) الاستعلام الأساسي من جدول إذون الاستلام مع ربط العميل والحسابات
            IQueryable<CashReceipt> q = _context.CashReceipts
                .AsNoTracking()
                .Include(r => r.Customer)
                .ThenInclude(c => c.Area)
                .Include(r => r.Customer)
                .ThenInclude(c => c.User)
                .Include(r => r.CashAccount)
                .Include(r => r.CounterAccount);

            if (!canViewInvestors)
                q = q.Where(r =>
                    r.CashAccount != null && r.CashAccount.AccountCode != InvestorAccountCode &&
                    r.CounterAccount != null && r.CounterAccount.AccountCode != InvestorAccountCode);

            // إخفاء أي إذن مرتبط بعميل غير ظاهر (حساب رئيسي أو قيود بحساب مسموح)
            if (hiddenCustomerAccountIds != null && hiddenCustomerAccountIds.Count > 0)
            {
                q = restrictedToAllowedOnly
                    ? q.Where(r => (r.Customer != null && r.Customer.AccountId != null && !hiddenCustomerAccountIds.Contains(r.Customer.AccountId.Value))
                        || (r.Customer != null && (r.Customer.PartyCategory == "Customer" || r.Customer.PartyCategory == "Supplier")
                            && r.CustomerId != null && _context.LedgerEntries.Any(e => e.CustomerId == r.CustomerId && !hiddenCustomerAccountIds.Contains(e.AccountId))))
                    : q.Where(r => r.Customer == null || r.Customer.AccountId == null || !hiddenCustomerAccountIds.Contains(r.Customer.AccountId.Value)
                        || (r.Customer != null && (r.Customer.PartyCategory == "Customer" || r.Customer.PartyCategory == "Supplier")
                            && r.CustomerId != null && _context.LedgerEntries.Any(e => e.CustomerId == r.CustomerId && !hiddenCustomerAccountIds.Contains(e.AccountId))));
            }

            // (2) فلتر كود من/إلى (نعتمد هنا على CashReceiptId)
            if (fromCode.HasValue)
                q = q.Where(r => r.CashReceiptId >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(r => r.CashReceiptId <= toCode.Value);

            // (3) فلتر التاريخ: نفلتر حسب تاريخ الإذن ReceiptDate
            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                DateTime from = fromDate.Value;
                DateTime to = toDate.Value;

                q = q.Where(r => r.ReceiptDate >= from && r.ReceiptDate <= to);
            }

            // (4) خرائط البحث: نحدد الأعمدة النصية والرقمية للبحث الموحد

            // الحقول النصية (string) التى يمكن البحث فيها
            var stringFields =
                new Dictionary<string, Expression<Func<CashReceipt, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["receiptNumber"] = r => r.ReceiptNumber,                                                // رقم المستند
                    ["customer"] = r => r.Customer != null ? r.Customer.CustomerName : "",              // اسم الطرف
                    ["cashAccount"] = r => r.CashAccount != null ? r.CashAccount.AccountName : "",         // حساب الصندوق
                    ["counterAccount"] = r => r.CounterAccount != null ? r.CounterAccount.AccountName : "",   // حساب الطرف
                    ["description"] = r => r.Description ?? "",                                           // البيان
                    ["status"] = r => r.IsPosted ? "مرحل" : "غير مرحل",                               // حالة الترحيل كنص
                    ["writer"] = r => r.CreatedBy ?? "",
                    ["region"] = r => r.Customer != null
                        ? (r.Customer.Area != null ? r.Customer.Area.AreaName : (r.Customer.RegionName ?? ""))
                        : "",
                    ["employee"] = r => r.Customer != null && r.Customer.UserId.HasValue
                        ? (_context.Employees
                            .Where(e => e.UserId == r.Customer.UserId)
                            .OrderByDescending(e => e.IsActive)
                            .Select(e => e.FullName)
                            .FirstOrDefault() ?? "")
                        : ""
                };

            // الحقول الرقمية (int) التى يمكن البحث فيها
            var intFields =
                new Dictionary<string, Expression<Func<CashReceipt, int>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = r => r.CashReceiptId   // البحث برقم الإذن
                };

            // الحقول المسموح الترتيب عليها
            var orderFields =
                new Dictionary<string, Expression<Func<CashReceipt, object>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["CashReceiptId"] = r => r.CashReceiptId,                      // رقم الإذن
                    ["ReceiptNumber"] = r => r.ReceiptNumber,                      // رقم المستند
                    ["ReceiptDate"] = r => r.ReceiptDate,                        // تاريخ الإذن
                    ["CustomerName"] = r => r.Customer != null ? r.Customer.CustomerName : "",
                    ["CashAccount"] = r => r.CashAccount != null ? r.CashAccount.AccountName : "",
                    ["CounterAccount"] = r => r.CounterAccount != null ? r.CounterAccount.AccountName : "",
                    ["Amount"] = r => r.Amount,                             // المبلغ
                    ["IsPosted"] = r => r.IsPosted,                           // الترحيل
                    ["CreatedBy"] = r => r.CreatedBy ?? "",
                    ["Region"] = r => r.Customer != null
                        ? (r.Customer.Area != null ? r.Customer.Area.AreaName : (r.Customer.RegionName ?? ""))
                        : "",
                    ["Employee"] = r => r.Customer != null && r.Customer.UserId.HasValue
                        ? (_context.Employees
                            .Where(e => e.UserId == r.Customer.UserId)
                            .OrderByDescending(e => e.IsActive)
                            .Select(e => e.FullName)
                            .FirstOrDefault() ?? "")
                        : "",
                    ["CreatedAt"] = r => r.CreatedAt,                          // تاريخ الإنشاء
                    ["UpdatedAt"] = r => r.UpdatedAt ?? DateTime.MinValue      // آخر تعديل
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
                defaultSortBy: "ReceiptDate",    // الترتيب الافتراضي بتاريخ الإذن (من الأحدث للأقدم)
                searchMode: searchMode);

            return q;
        }

        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        private IQueryable<CashReceipt> ApplyColumnFilters(
            IQueryable<CashReceipt> query,
            string? filterCol_id,
            string? filterCol_receiptNumber,
            string? filterCol_date,
            string? filterCol_customer,
            string? filterCol_region,
            string? filterCol_employee,
            string? filterCol_writer,
            string? filterCol_cashAccount,
            string? filterCol_counterAccount,
            string? filterCol_amount,
            string? filterCol_posted,
            string? filterCol_desc,
            string? filterCol_idExpr,
            string? filterCol_amountExpr)
        {
            if (!string.IsNullOrWhiteSpace(filterCol_idExpr))
                query = CashVoucherListNumericExpr.ApplyCashReceiptIdExpr(query, filterCol_idExpr);
            else if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) query = query.Where(r => ids.Contains(r.CashReceiptId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_receiptNumber))
            {
                var vals = filterCol_receiptNumber.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) query = query.Where(r => r.ReceiptNumber != null && vals.Contains(r.ReceiptNumber));
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
                    if (dates.Count > 0) query = query.Where(r => dates.Contains(r.ReceiptDate.Date));
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_customer))
            {
                var vals = filterCol_customer.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    query = query.Where(r => r.Customer != null && vals.Contains(r.Customer.CustomerName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_region))
            {
                var vals = filterCol_region.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                {
                    query = query.Where(r =>
                        r.Customer != null &&
                        vals.Contains(r.Customer.Area != null ? r.Customer.Area.AreaName : (r.Customer.RegionName ?? "")));
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_employee))
            {
                var vals = filterCol_employee.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                {
                    query = query.Where(r =>
                        r.Customer != null &&
                        r.Customer.UserId.HasValue &&
                        _context.Employees.Any(e => e.UserId == r.Customer.UserId && vals.Contains(e.FullName)));
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_writer))
            {
                var vals = filterCol_writer.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    query = query.Where(r => r.CreatedBy != null && vals.Contains(r.CreatedBy));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_cashAccount))
            {
                var vals = filterCol_cashAccount.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    query = query.Where(r => r.CashAccount != null && vals.Contains(r.CashAccount.AccountName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_counterAccount))
            {
                var vals = filterCol_counterAccount.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    query = query.Where(r => r.CounterAccount != null && vals.Contains(r.CounterAccount.AccountName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_amountExpr))
                query = CashVoucherListNumericExpr.ApplyCashReceiptAmountExpr(query, filterCol_amountExpr);
            else if (!string.IsNullOrWhiteSpace(filterCol_amount))
            {
                var vals = filterCol_amount.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0) query = query.Where(r => vals.Contains(r.Amount));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_posted))
            {
                var vals = filterCol_posted.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim().ToLowerInvariant())
                    .Where(x => x == "true" || x == "1" || x == "مرحل" || x == "مرحّل" || x == "مرحلة 1" || x == "false" || x == "0" || x == "مسودة" || x == "غير مرحلة" || x == "غير مرحل")
                    .ToList();
                if (vals.Count > 0)
                {
                    var postTrue = vals.Any(v => v == "true" || v == "1" || v == "مرحل" || v == "مرحّل" || v == "مرحلة 1");
                    var postFalse = vals.Any(v => v == "false" || v == "0" || v == "مسودة" || v == "غير مرحلة" || v == "غير مرحل");
                    if (postTrue && !postFalse) query = query.Where(r => r.IsPosted);
                    else if (postFalse && !postTrue) query = query.Where(r => !r.IsPosted);
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_desc))
            {
                var vals = filterCol_desc.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    query = query.Where(r => r.Description != null && vals.Any(v => r.Description.Contains(v)));
            }
            return query;
        }

        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var columnLower = (column ?? "").Trim().ToLowerInvariant();
            var canViewInvestors = await CanViewInvestorsAsync();
            IQueryable<CashReceipt> q = _context.CashReceipts.AsNoTracking()
                .Include(r => r.Customer)
                .ThenInclude(c => c.Area)
                .Include(r => r.Customer)
                .ThenInclude(c => c.User)
                .Include(r => r.CashAccount)
                .Include(r => r.CounterAccount);

            if (!canViewInvestors)
                q = q.Where(r =>
                    (r.CashAccount != null && r.CashAccount.AccountCode != InvestorAccountCode) &&
                    (r.CounterAccount != null && r.CounterAccount.AccountCode != InvestorAccountCode));

            if (columnLower == "id")
            {
                var ids = await q.Select(r => r.CashReceiptId).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "receiptnumber")
            {
                var list = await q.Where(r => r.ReceiptNumber != null).Select(r => r.ReceiptNumber!).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "date")
            {
                var dates = await q.Select(r => r.ReceiptDate.Date).Distinct().OrderByDescending(x => x).Take(500).ToListAsync();
                return Json(dates.Select(d => new { value = d.ToString("yyyy-MM-dd"), display = d.ToString("yyyy-MM-dd") }));
            }
            if (columnLower == "customer" || columnLower == "customername")
            {
                var list = await q.Where(r => r.Customer != null).Select(r => r.Customer!.CustomerName).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "region")
            {
                var list = await q
                    .Where(r => r.Customer != null)
                    .Select(r => r.Customer!.Area != null ? r.Customer.Area.AreaName : (r.Customer.RegionName ?? ""))
                    .Where(s => s != "")
                    .Distinct()
                    .OrderBy(x => x)
                    .Take(500)
                    .ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "employee")
            {
                var userIdsQuery = q
                    .Where(r => r.Customer != null && r.Customer.UserId.HasValue)
                    .Select(r => r.Customer!.UserId!.Value)
                    .Distinct();
                var list = await _context.Employees.AsNoTracking()
                    .Where(e => e.UserId.HasValue && userIdsQuery.Contains(e.UserId.Value))
                    .Select(e => e.FullName)
                    .Where(s => s != "")
                    .Distinct()
                    .OrderBy(x => x)
                    .Take(500)
                    .ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "writer" || columnLower == "createdby")
            {
                var list = await q.Where(r => r.CreatedBy != null && r.CreatedBy != "").Select(r => r.CreatedBy!).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "cashaccount")
            {
                var list = await q.Where(r => r.CashAccount != null).Select(r => r.CashAccount!.AccountName).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "counteraccount")
            {
                var list = await q.Where(r => r.CounterAccount != null).Select(r => r.CounterAccount!.AccountName).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "amount")
            {
                var list = await q.Select(r => r.Amount).Distinct().OrderBy(x => x).Take(300).ToListAsync();
                return Json(list.Select(v => new { value = v.ToString(CultureInfo.InvariantCulture), display = v.ToString("0.00") }));
            }
            if (columnLower == "posted" || columnLower == "isposted")
            {
                return Json(new[] { new { value = "true", display = "مرحل" }, new { value = "false", display = "غير مرحل" } });
            }
            if (columnLower == "created" || columnLower == "createdat")
            {
                var list = await q.Where(r => r.CreatedAt != default).Select(r => r.CreatedAt).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd HH:mm"), display = d.ToString("yyyy-MM-dd HH:mm") }));
            }
            if (columnLower == "updated" || columnLower == "updatedat")
            {
                var list = await q.Where(r => r.UpdatedAt.HasValue).Select(r => r.UpdatedAt!.Value).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd HH:mm"), display = d.ToString("yyyy-MM-dd HH:mm") }));
            }
            if (columnLower == "desc" || columnLower == "description")
            {
                var list = await q.Where(r => r.Description != null && r.Description != "").Select(r => r.Description!).Distinct().OrderBy(x => x).Take(300).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v.Length > 50 ? v.Substring(0, 50) + "…" : v }));
            }
            return Json(Array.Empty<object>());
        }

        // =========================================================
        // Index — عرض قائمة إذون الاستلام (نظام القوائم الموحد)
        // =========================================================
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",
            string? searchMode = "contains",
            string? sort = "ReceiptDate",
            string? dir = "desc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_receiptNumber = null,
            string? filterCol_date = null,
            string? filterCol_customer = null,
            string? filterCol_region = null,
            string? filterCol_employee = null,
            string? filterCol_writer = null,
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

            q = ApplyColumnFilters(q, filterCol_id, filterCol_receiptNumber, filterCol_date, filterCol_customer, filterCol_region, filterCol_employee, filterCol_writer, filterCol_cashAccount, filterCol_counterAccount, filterCol_amount, filterCol_posted, filterCol_desc, filterCol_idExpr, filterCol_amountExpr);

            var totalAmount = await q.Select(r => (decimal?)r.Amount).SumAsync() ?? 0m;
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

            var userIds = items.Where(r => r.Customer?.UserId.HasValue == true)
                .Select(r => r.Customer!.UserId!.Value)
                .Distinct()
                .ToList();
            var employeeByUserId = userIds.Count == 0
                ? new Dictionary<int, string>()
                : await _context.Employees.AsNoTracking()
                    .Where(e => e.UserId.HasValue && userIds.Contains(e.UserId.Value))
                    .GroupBy(e => e.UserId!.Value)
                    .Select(g => new
                    {
                        UserId = g.Key,
                        Name = g.OrderByDescending(e => e.IsActive).ThenBy(e => e.FullName).Select(e => e.FullName).FirstOrDefault() ?? ""
                    })
                    .ToDictionaryAsync(x => x.UserId, x => x.Name);

            var model = new PagedResult<CashReceipt>(items, page, pageSize, totalCount)
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
            ViewBag.Sort = sort ?? "ReceiptDate";
            ViewBag.Dir = sortDesc ? "desc" : "asc";
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_IdExpr = filterCol_idExpr;
            ViewBag.FilterCol_ReceiptNumber = filterCol_receiptNumber;
            ViewBag.FilterCol_Date = filterCol_date;
            ViewBag.FilterCol_Customer = filterCol_customer;
            ViewBag.FilterCol_Region = filterCol_region;
            ViewBag.FilterCol_Employee = filterCol_employee;
            ViewBag.FilterCol_Writer = filterCol_writer;
            ViewBag.FilterCol_CashAccount = filterCol_cashAccount;
            ViewBag.FilterCol_CounterAccount = filterCol_counterAccount;
            ViewBag.FilterCol_Amount = filterCol_amount;
            ViewBag.FilterCol_AmountExpr = filterCol_amountExpr;
            ViewBag.FilterCol_Posted = filterCol_posted;
            ViewBag.FilterCol_Desc = filterCol_desc;
            ViewBag.DateField = "ReceiptDate";
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalCount = totalCount;
            ViewBag.TotalAmount = totalAmount;
            ViewBag.EmployeeByUserId = employeeByUserId;

            return View(model);
        }

        // =========================================================
        // Details — عرض تفاصيل إذن واحد (النموذج الكلاسيك)
        // =========================================================
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
                return NotFound();

            var cashReceipt = await _context.CashReceipts
                .Include(c => c.Customer)
                .Include(c => c.CashAccount)
                .Include(c => c.CounterAccount)
                .FirstOrDefaultAsync(m => m.CashReceiptId == id);

            if (cashReceipt == null)
                return NotFound();

            if (!await CanViewInvestorsAsync() &&
                ((cashReceipt.CashAccount != null && cashReceipt.CashAccount.AccountCode == InvestorAccountCode) ||
                 (cashReceipt.CounterAccount != null && cashReceipt.CounterAccount.AccountCode == InvestorAccountCode)))
                return NotFound();

            return View(cashReceipt);
        }

        // =========================================================
        // Show — فورم عرض محسّن يمكن استخدامه لاحقاً (نفس Data الـ Details حالياً)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Show(int id)
        {
            var cashReceipt = await _context.CashReceipts
                .Include(c => c.Customer)
                .Include(c => c.CashAccount)
                .Include(c => c.CounterAccount)
                .FirstOrDefaultAsync(m => m.CashReceiptId == id);

            if (cashReceipt == null)
                return NotFound();

            if (!await CanViewInvestorsAsync() &&
                ((cashReceipt.CashAccount != null && cashReceipt.CashAccount.AccountCode == InvestorAccountCode) ||
                 (cashReceipt.CounterAccount != null && cashReceipt.CounterAccount.AccountCode == InvestorAccountCode)))
                return NotFound();

            return View(cashReceipt);   // Views/CashReceipts/Show.cshtml
        }

        // =========================================================
        // Create — إضافة إذن استلام جديد
        // GET: يعرض الفورم مع تعبئة تلقائية إذا جاء من customerId
        // POST: يحفظ + يرحّل محاسبيًا
        // =========================================================

        // GET: CashReceipts/Create — يدعم إنشاء جديد أو عرض إذن موجود (مثل إذن الدفع)
        public async Task<IActionResult> Create(
            int? id = null,
            int? customerId = null,
            int? groupAreaId = null,
            int? groupDistributorUserId = null,
            DateTime? groupDate = null)
        {
            CashReceipt model;

            if (id.HasValue && id.Value > 0)
            {
                model = await _context.CashReceipts
                    .Include(r => r.Customer)
                    .ThenInclude(c => c.Account)
                    .FirstOrDefaultAsync(r => r.CashReceiptId == id.Value);

                if (model == null)
                    return NotFound();

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

                model = new CashReceipt
                {
                    ReceiptDate = (groupDate ?? DateTime.Now).Date,
                    Status = "غير مرحلة",
                    IsPosted = false,
                    CashAccountId = defaultId ?? 0
                };

                if (customerId.HasValue && customerId.Value > 0)
                {
                    var customer = await _context.Customers
                        .AsNoTracking()
                        .Include(c => c.Account)
                        .FirstOrDefaultAsync(c => c.CustomerId == customerId.Value);

                    if (customer != null)
                    {
                        model.CustomerId = customer.CustomerId;
                        if (customer.AccountId.HasValue)
                            model.CounterAccountId = customer.AccountId.Value;
                        model.Description = $"تحصيل من العميل {customer.CustomerName}";
                        ViewBag.LockCustomer = true;
                    }
                }
            }

            await PopulateDropdownsAsync(
                model.CustomerId,
                model.CashAccountId > 0 ? (int?)model.CashAccountId : null,
                model.CounterAccountId > 0 ? (int?)model.CounterAccountId : null,
                groupAreaId,
                groupDistributorUserId);
            await SetGroupedContextViewBagAsync(groupAreaId, groupDistributorUserId, groupDate);
            await PopulateCreateNavigationAsync(model.CashReceiptId > 0 ? model.CashReceiptId : null);
            return View(model);
        }

        // POST: CashReceipts/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            [Bind("CashReceiptId,ReceiptDate,CustomerId,CashAccountId,CounterAccountId,Amount,Description")] CashReceipt cashReceipt,
            int? groupAreaId = null,
            int? groupDistributorUserId = null,
            DateTime? groupDate = null)
        {
            // ✅ تجاهل خطأ التحقق لـ ReceiptNumber لأنه سيتم توليده تلقائياً
            ModelState.Remove(nameof(CashReceipt.ReceiptNumber));

            // في وضع سريع/مجمّع: لو حساب الطرف لم يُرسل من الواجهة نملؤه تلقائياً من حساب العميل.
            if (cashReceipt.CustomerId.HasValue && cashReceipt.CustomerId.Value > 0 && cashReceipt.CounterAccountId <= 0)
            {
                var customerAccountId = await _context.Customers.AsNoTracking()
                    .Where(c => c.CustomerId == cashReceipt.CustomerId.Value)
                    .Select(c => c.AccountId)
                    .FirstOrDefaultAsync();
                if (customerAccountId.HasValue && customerAccountId.Value > 0)
                    cashReceipt.CounterAccountId = customerAccountId.Value;
            }
            
            // ✅ تسجيل القيم الواردة للتأكد من وصولها (للـ debugging)
            // يمكن إزالة هذا لاحقاً بعد حل المشكلة
            // System.Diagnostics.Debug.WriteLine($"CashAccountId: {cashReceipt.CashAccountId}, CounterAccountId: {cashReceipt.CounterAccountId}");
            
            // ✅ التحقق من الحقول المطلوبة يدوياً (بعد إزالة [Required] من Model)
            // ملاحظة: int لا يمكن أن تكون null، لذلك القيمة الافتراضية هي 0
            // إذا كانت القيمة 0، فهذا يعني أنه لم يتم اختيار حساب
            
            // ✅ تسجيل القيم المرسلة للتحقق
            System.Diagnostics.Debug.WriteLine($"DEBUG: CashAccountId={cashReceipt.CashAccountId}, CounterAccountId={cashReceipt.CounterAccountId}, Amount={cashReceipt.Amount}");
            
            if (cashReceipt.CashAccountId <= 0)
            {
                ModelState.AddModelError(nameof(CashReceipt.CashAccountId), $"يجب اختيار حساب الصندوق/البنك. (القيمة المرسلة: {cashReceipt.CashAccountId})");
            }
            else
            {
                var hiddenForCash = await _accountVisibilityService.GetHiddenAccountIdsForCurrentUserAsync();
                if (!await TreasuryCashAccounts.IsAllowedTreasuryCashBoxForUserAsync(_context, cashReceipt.CashAccountId, hiddenForCash))
                    ModelState.AddModelError(nameof(CashReceipt.CashAccountId), "يجب اختيار خزينة/صندوق صالح من القائمة.");
            }
            
            if (cashReceipt.CounterAccountId <= 0)
            {
                ModelState.AddModelError(nameof(CashReceipt.CounterAccountId), $"يجب اختيار حساب الطرف. (القيمة المرسلة: {cashReceipt.CounterAccountId})");
            }
            
            if (cashReceipt.Amount <= 0)
            {
                ModelState.AddModelError(nameof(CashReceipt.Amount), "يجب إدخال مبلغ أكبر من الصفر.");
            }

            if (groupDate.HasValue && cashReceipt.ReceiptDate == default)
                cashReceipt.ReceiptDate = groupDate.Value.Date;

            if (cashReceipt.CustomerId.HasValue && cashReceipt.CustomerId.Value > 0)
            {
                var cust = await _context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.CustomerId == cashReceipt.CustomerId.Value);
                if (cust != null && !cust.IsActive)
                    ModelState.AddModelError(nameof(CashReceipt.CustomerId), "لا يمكن التعامل مع عميل غير نشط. يرجى تفعيل العميل أولاً.");

                if (cust != null && groupAreaId.HasValue && groupAreaId.Value > 0 && cust.AreaId != groupAreaId.Value)
                    ModelState.AddModelError(nameof(CashReceipt.CustomerId), "العميل المختار ليس ضمن المنطقة المحددة للدورة.");

                if (cust != null && groupDistributorUserId.HasValue && groupDistributorUserId.Value > 0 && cust.UserId != groupDistributorUserId.Value)
                    ModelState.AddModelError(nameof(CashReceipt.CustomerId), "العميل المختار غير مرتبط بالموزع المحدد للدورة.");
            }
            
            if (ModelState.IsValid)
            {
                // تعديل إذن موجود (مثل إذن الدفع)
                if (cashReceipt.CashReceiptId > 0)
                {
                    return await Edit(cashReceipt.CashReceiptId, cashReceipt);
                }

                try
                {
                    cashReceipt.CreatedAt = DateTime.Now;
                    cashReceipt.CreatedBy = User?.Identity?.Name ?? "SYSTEM";
                    cashReceipt.Status = "غير مرحلة";
                    cashReceipt.IsPosted = false;

                    _context.Add(cashReceipt);
                    await _context.SaveChangesAsync();
                    
                    // ✅ الآن CashReceiptId موجود بعد الحفظ
                    // إذا تم إلغاء الإذن بعد هذه النقطة، سيتم حفظه في قاعدة البيانات
                    // لكن يمكن حذفه إذا فشل الترحيل (موجود في catch block)

                    // =========================================================
                    // 3) توليد رقم المستند من CashReceiptId
                    // =========================================================
                    cashReceipt.ReceiptNumber = cashReceipt.CashReceiptId.ToString();
                    await _context.SaveChangesAsync();

                    // =========================================================
                    // 4) ترحيل محاسبي (LedgerEntries + تحديث حساب العميل)
                    // =========================================================
                    string? postedBy = User?.Identity?.Name ?? "SYSTEM";
                    await _ledgerPostingService.PostCashReceiptAsync(cashReceipt.CashReceiptId, postedBy);

                    // =========================================================
                    // 5) تسجيل في اللوج
                    // =========================================================
                    await _activityLogger.LogAsync(
                        UserActionType.Create,
                        "CashReceipt",
                        cashReceipt.CashReceiptId,
                        $"إضافة إذن استلام رقم {cashReceipt.ReceiptNumber} بمبلغ {cashReceipt.Amount} من عميل {cashReceipt.CustomerId}"
                    );

                    TempData["CashReceiptSuccess"] = "تم حفظ وترحيل إذن الاستلام بنجاح.";
                    // البقاء داخل الإذن مع عرض الرسالة فقط (مثل إذن الدفع)
                    var saved = await _context.CashReceipts
                        .Include(r => r.Customer)
                        .ThenInclude(c => c.Account)
                        .AsNoTracking()
                        .FirstOrDefaultAsync(r => r.CashReceiptId == cashReceipt.CashReceiptId);
                    if (saved != null)
                    {
                        await PopulateDropdownsAsync(saved.CustomerId, saved.CashAccountId, saved.CounterAccountId, groupAreaId, groupDistributorUserId);
                        await SetGroupedContextViewBagAsync(groupAreaId, groupDistributorUserId, groupDate);
                        await PopulateCreateNavigationAsync(saved.CashReceiptId);
                        if (saved.CustomerId.HasValue)
                            ViewBag.LockCustomer = true;
                        return View(saved);
                    }
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    // ✅ تسجيل الخطأ الكامل للمطورين
                    var errorMessage = ex.Message;
                    if (ex.InnerException != null)
                    {
                        errorMessage += $" | التفاصيل: {ex.InnerException.Message}";
                    }
                    
                    TempData["CashReceiptError"] = $"حدث خطأ أثناء حفظ إذن الاستلام: {errorMessage}";
                    
                    // ✅ محاولة حذف الإذن من قاعدة البيانات إذا تم حفظه جزئياً
                    if (cashReceipt.CashReceiptId > 0)
                    {
                        try
                        {
                            var existingReceipt = await _context.CashReceipts.FindAsync(cashReceipt.CashReceiptId);
                            if (existingReceipt != null && !existingReceipt.IsPosted)
                            {
                                _context.CashReceipts.Remove(existingReceipt);
                                await _context.SaveChangesAsync();
                            }
                        }
                        catch { /* تجاهل أخطاء الحذف */ }
                    }
                    
                    await PopulateDropdownsAsync(cashReceipt.CustomerId, cashReceipt.CashAccountId, cashReceipt.CounterAccountId, groupAreaId, groupDistributorUserId);
                    await SetGroupedContextViewBagAsync(groupAreaId, groupDistributorUserId, groupDate);
                    await PopulateCreateNavigationAsync(cashReceipt.CashReceiptId > 0 ? cashReceipt.CashReceiptId : null);
                    if (cashReceipt.CustomerId.HasValue)
                        ViewBag.LockCustomer = true;
                    return View(cashReceipt);
                }
            }

            // لو هناك خطأ تحقق نرجّع القوائم المنسدلة
            await PopulateDropdownsAsync(cashReceipt.CustomerId, cashReceipt.CashAccountId, cashReceipt.CounterAccountId, groupAreaId, groupDistributorUserId);
            await SetGroupedContextViewBagAsync(groupAreaId, groupDistributorUserId, groupDate);
            await PopulateCreateNavigationAsync(cashReceipt.CashReceiptId > 0 ? cashReceipt.CashReceiptId : null);
            if (cashReceipt.CustomerId.HasValue)
                ViewBag.LockCustomer = true;
            return View(cashReceipt);
        }

        // =========================================================
        // Edit — تعديل إذن موجود
        // =========================================================

        // GET: CashReceipts/Edit/5 — يعيد نفس واجهة Create (مثل إذن الدفع)
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
                return NotFound();

            var cashReceipt = await _context.CashReceipts
                .Include(r => r.Customer)
                .ThenInclude(c => c.Account)
                .FirstOrDefaultAsync(r => r.CashReceiptId == id);

            if (cashReceipt == null)
                return NotFound();

            if (cashReceipt.CustomerId.HasValue)
                ViewBag.LockCustomer = true;

            await PopulateDropdownsAsync(cashReceipt.CustomerId, cashReceipt.CashAccountId, cashReceipt.CounterAccountId);
            await PopulateCreateNavigationAsync(cashReceipt.CashReceiptId);
            return View("Create", cashReceipt);
        }

        // POST: CashReceipts/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id,
            [Bind("CashReceiptId,ReceiptDate,CustomerId,CashAccountId,CounterAccountId,Amount,Description")]
            CashReceipt cashReceipt)
        {
            if (id != cashReceipt.CashReceiptId)
                return NotFound();

            if (cashReceipt.CashAccountId > 0)
            {
                var hiddenEdit = await _accountVisibilityService.GetHiddenAccountIdsForCurrentUserAsync();
                if (!await TreasuryCashAccounts.IsAllowedTreasuryCashBoxForUserAsync(_context, cashReceipt.CashAccountId, hiddenEdit))
                    ModelState.AddModelError(nameof(CashReceipt.CashAccountId), "يجب اختيار خزينة/صندوق صالح من القائمة.");
            }

            if (!ModelState.IsValid)
            {
                await PopulateDropdownsAsync(cashReceipt.CustomerId, cashReceipt.CashAccountId, cashReceipt.CounterAccountId);
                await PopulateCreateNavigationAsync(cashReceipt.CashReceiptId > 0 ? cashReceipt.CashReceiptId : null);
                if (cashReceipt.CustomerId.HasValue)
                    ViewBag.LockCustomer = true;
                return View("Create", cashReceipt);
            }

            try
            {
                // =========================================================
                // 1) جلب السجل الأصلي من قاعدة البيانات
                // =========================================================
                var existing = await _context.CashReceipts
                    .Include(r => r.Customer)
                    .FirstOrDefaultAsync(r => r.CashReceiptId == id);

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
                    ReceiptDate = existing.ReceiptDate,
                    Description = existing.Description,
                    IsPosted = existing.IsPosted,
                    Status = existing.Status
                };

                // =========================================================
                // 3) تحديث الحقول المسموح بها (رقم المستند لا يتغير)
                // =========================================================
                existing.ReceiptDate = cashReceipt.ReceiptDate;
                existing.CustomerId = cashReceipt.CustomerId;
                existing.CashAccountId = cashReceipt.CashAccountId;
                existing.CounterAccountId = cashReceipt.CounterAccountId;
                existing.Amount = cashReceipt.Amount;
                existing.Description = cashReceipt.Description;
                existing.UpdatedAt = DateTime.Now;

                // =========================================================
                // 4) حفظ التعديلات أولاً
                // =========================================================
                await _context.SaveChangesAsync();

                // =========================================================
                // 5) ترحيل محاسبي (يعكس القديم ويعمل جديد إذا كان مفتوح)
                // =========================================================
                // ✅ إذا كان الإذن مفتوح (غير مرحّل)، الترحيل سيعمل مرحلة جديدة
                // ✅ إذا كان مرحّل، يجب فتحه أولاً (زر Open)
                if (!existing.IsPosted)
                {
                    string? postedBy = User?.Identity?.Name ?? "SYSTEM";
                    await _ledgerPostingService.PostCashReceiptAsync(existing.CashReceiptId, postedBy);
                }

                // =========================================================
                // 6) تسجيل في اللوج
                // =========================================================
                await _activityLogger.LogAsync(
                    UserActionType.Edit,
                    "CashReceipt",
                    existing.CashReceiptId,
                    $"تعديل إذن استلام رقم {existing.ReceiptNumber}",
                    oldValues: System.Text.Json.JsonSerializer.Serialize(oldValues),
                    newValues: System.Text.Json.JsonSerializer.Serialize(new
                    {
                        Amount = existing.Amount,
                        CashAccountId = existing.CashAccountId,
                        CounterAccountId = existing.CounterAccountId,
                        ReceiptDate = existing.ReceiptDate,
                        Description = existing.Description,
                        IsPosted = existing.IsPosted,
                        Status = existing.Status
                    })
                );

                TempData["CashReceiptSuccess"] = "تم تعديل وترحيل إذن الاستلام بنجاح.";
                await PopulateDropdownsAsync(existing.CustomerId, existing.CashAccountId, existing.CounterAccountId);
                await PopulateCreateNavigationAsync(existing.CashReceiptId);
                if (existing.CustomerId.HasValue)
                    ViewBag.LockCustomer = true;
                return View("Create", existing);
            }
            catch (Exception ex)
            {
                TempData["CashReceiptError"] = $"حدث خطأ: {ex.Message}";
                await PopulateDropdownsAsync(cashReceipt.CustomerId, cashReceipt.CashAccountId, cashReceipt.CounterAccountId);
                await PopulateCreateNavigationAsync(cashReceipt.CashReceiptId > 0 ? cashReceipt.CashReceiptId : null);
                if (cashReceipt.CustomerId.HasValue)
                    ViewBag.LockCustomer = true;
                return View("Create", cashReceipt);
            }
        }

        // =========================================================
        // Open — فتح إذن مرحّل للتعديل (مثل فواتير البيع)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("CashReceipts.Open")]
        public async Task<IActionResult> Open(int id)
        {
            try
            {
                var receipt = await _context.CashReceipts
                    .FirstOrDefaultAsync(r => r.CashReceiptId == id);

                if (receipt == null)
                {
                    TempData["CashReceiptError"] = "الإذن غير موجود.";
                    return RedirectToAction(nameof(Index));
                }

                // ================================
                // 1) لازم يكون مرحّل عشان ينفع "فتح"
                // ================================
                if (!receipt.IsPosted)
                {
                    TempData["CashReceiptError"] = "هذا الإذن غير مُرحّل، لا يوجد ما يمكن فتحه.";
                    return RedirectToAction(nameof(Create), new { id });
                }

                // ================================
                // 2) فتح الإذن للتعديل (لا يعكس القيود - الحفظ هو الذي يعكس)
                // ================================
                receipt.IsPosted = false;
                receipt.Status = "مفتوحة للتعديل";
                receipt.PostedAt = null;
                receipt.PostedBy = null;
                receipt.UpdatedAt = DateTime.Now;

                await _context.SaveChangesAsync();

                // ================================
                // 3) تسجيل نشاط
                // ================================
                await _activityLogger.LogAsync(
                    actionType: UserActionType.Unpost,
                    entityName: "CashReceipt",
                    entityId: receipt.CashReceiptId,
                    description: $"فتح إذن استلام رقم {receipt.ReceiptNumber} للتعديل"
                );

                TempData["CashReceiptSuccess"] = "تم فتح الإذن للتعديل بنجاح.";
                return RedirectToAction(nameof(Create), new { id });
            }
            catch (Exception ex)
            {
                TempData["CashReceiptError"] = $"حدث خطأ: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        // =========================================================
        // Delete — حذف إذن واحد (عن طريق شاشة التأكيد)
        // =========================================================

        // GET: CashReceipts/Delete/5
        [RequirePermission("CashReceipts.Delete")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
                return NotFound();

            var cashReceipt = await _context.CashReceipts
                .Include(c => c.Customer)
                .Include(c => c.CashAccount)
                .Include(c => c.CounterAccount)
                .FirstOrDefaultAsync(m => m.CashReceiptId == id);

            if (cashReceipt == null)
                return NotFound();

            return View(cashReceipt);
        }

        // POST: CashReceipts/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [RequirePermission("CashReceipts.Delete")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                var cashReceipt = await _context.CashReceipts
                    .Include(r => r.Customer)
                    .FirstOrDefaultAsync(r => r.CashReceiptId == id);

                if (cashReceipt == null)
                {
                    TempData["CashReceiptError"] = "الإذن غير موجود.";
                    return RedirectToAction(nameof(Index));
                }

                // =========================================================
                // 1) إذا كان مرحّل، نعكس قيوده أولاً
                // =========================================================
                if (cashReceipt.IsPosted)
                {
                    string? postedBy = User?.Identity?.Name ?? "SYSTEM";
                    await _ledgerPostingService.ReverseForHeaderDeleteAsync(
                        LedgerSourceType.Receipt,
                        cashReceipt.CashReceiptId,
                        postedBy,
                        "حذف إذن استلام نقدية"
                    );
                }

                // =========================================================
                // 2) حفظ Snapshot قبل الحذف (للتسجيل في اللوج)
                // =========================================================
                var oldValues = System.Text.Json.JsonSerializer.Serialize(new
                {
                    ReceiptNumber = cashReceipt.ReceiptNumber,
                    Amount = cashReceipt.Amount,
                    CustomerId = cashReceipt.CustomerId,
                    ReceiptDate = cashReceipt.ReceiptDate,
                    IsPosted = cashReceipt.IsPosted
                });

                // =========================================================
                // 3) حذف الإذن
                // =========================================================
                _context.CashReceipts.Remove(cashReceipt);
                await _context.SaveChangesAsync();

                // =========================================================
                // 4) تسجيل في اللوج
                // =========================================================
                await _activityLogger.LogAsync(
                    UserActionType.Delete,
                    "CashReceipt",
                    id,
                    $"حذف إذن استلام رقم {cashReceipt.ReceiptNumber}",
                    oldValues: oldValues
                );

                TempData["CashReceiptSuccess"] = "تم حذف إذن الاستلام بنجاح.";
            }
            catch (Exception ex)
            {
                TempData["CashReceiptError"] = $"حدث خطأ أثناء الحذف: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // Export — تصدير قائمة إذون الاستلام إلى CSV (يفتح فى Excel)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy = "all",
            string? searchMode = "contains",
            string? sort = "ReceiptDate",
            string? dir = "desc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_receiptNumber = null,
            string? filterCol_date = null,
            string? filterCol_customer = null,
            string? filterCol_region = null,
            string? filterCol_employee = null,
            string? filterCol_writer = null,
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

            q = ApplyColumnFilters(q, filterCol_id, filterCol_receiptNumber, filterCol_date, filterCol_customer, filterCol_region, filterCol_employee, filterCol_writer, filterCol_cashAccount, filterCol_counterAccount, filterCol_amount, filterCol_posted, filterCol_desc, filterCol_idExpr, filterCol_amountExpr);

            var list = await q.ToListAsync();
            var userIds = list
                .Where(r => r.Customer != null && r.Customer.UserId.HasValue)
                .Select(r => r.Customer!.UserId!.Value)
                .Distinct()
                .ToList();
            var employeeByUserId = userIds.Count == 0
                ? new Dictionary<int, string>()
                : await _context.Employees.AsNoTracking()
                    .Where(e => e.UserId.HasValue && userIds.Contains(e.UserId.Value))
                    .GroupBy(e => e.UserId!.Value)
                    .Select(g => new
                    {
                        UserId = g.Key,
                        Name = g.OrderByDescending(e => e.IsActive)
                            .ThenBy(e => e.FullName)
                            .Select(e => e.FullName)
                            .FirstOrDefault() ?? ""
                    })
                    .ToDictionaryAsync(x => x.UserId, x => x.Name);

            var sb = new StringBuilder();

            sb.AppendLine("رقم الإذن,رقم المستند,تاريخ الإذن,الطرف,المنطقة,الموظف,الكاتب,حساب الصندوق/البنك,حساب الطرف,المبلغ,مرحّل؟,تاريخ الإنشاء,آخر تعديل,البيان");

            static string Q(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

            foreach (var r in list)
            {
                string customerName = r.Customer?.CustomerName ?? "";
                string regionName = r.Customer?.Area?.AreaName ?? r.Customer?.RegionName ?? "";
                string employeeName = (r.Customer != null
                                       && r.Customer.UserId.HasValue
                                       && employeeByUserId.TryGetValue(r.Customer.UserId.Value, out var empName))
                    ? empName
                    : "";
                string cashAcc = r.CashAccount?.AccountName ?? "";
                string counterAcc = r.CounterAccount?.AccountName ?? "";

                string line = string.Join(",",
                    r.CashReceiptId,
                    Q(r.ReceiptNumber),
                    r.ReceiptDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    Q(customerName),
                    Q(regionName),
                    Q(employeeName),
                    Q(r.CreatedBy),
                    Q(cashAcc),
                    Q(counterAcc),
                    r.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                    r.IsPosted ? "نعم" : "لا",
                    r.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    r.UpdatedAt.HasValue
                        ? r.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                        : "",
                    Q(r.Description)
                );

                sb.AppendLine(line);
            }

            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString());
            var fileName = ExcelExportNaming.ArabicTimestampedFileName("إذون استلام نقدية", ".csv");
            const string contentType = "text/csv; charset=utf-8";

            return File(bytes, contentType, fileName);
        }

        // =========================================================
        // BulkDelete — حذف مجموعة من الإذون المحددة (يستخدم من زر "حذف المحدد")
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("CashReceipts.BulkDelete")]
        public async Task<IActionResult> BulkDelete(int[] ids)
        {
            if (ids == null || ids.Length == 0)
            {
                TempData["CashReceiptError"] = "لم يتم اختيار أى إذن للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var receipts = await _context.CashReceipts
                                         .Include(r => r.Customer)
                                         .Where(r => ids.Contains(r.CashReceiptId))
                                         .ToListAsync();

            if (receipts.Count == 0)
            {
                TempData["CashReceiptError"] = "لم يتم العثور على الإذون المحددة.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                string? postedBy = User?.Identity?.Name ?? "SYSTEM";

                // عكس القيود لكل إذن مرحّل
                foreach (var receipt in receipts)
                {
                    if (receipt.IsPosted)
                    {
                        await _ledgerPostingService.ReverseForHeaderDeleteAsync(
                            LedgerSourceType.Receipt,
                            receipt.CashReceiptId,
                            postedBy,
                            "حذف إذن استلام نقدية"
                        );
                    }

                    // تسجيل في اللوج
                    await _activityLogger.LogAsync(
                        UserActionType.Delete,
                        "CashReceipt",
                        receipt.CashReceiptId,
                        $"حذف إذن استلام رقم {receipt.ReceiptNumber} (حذف جماعي)"
                    );
                }

                _context.CashReceipts.RemoveRange(receipts);
                await _context.SaveChangesAsync();
                await _ledgerPostingService.RecalcAllCustomerBalancesAsync();

                TempData["CashReceiptSuccess"] = $"تم حذف {receipts.Count} من إذون الاستلام المحددة.";
            }
            catch (Exception ex)
            {
                TempData["CashReceiptError"] = $"حدث خطأ أثناء الحذف: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // DeleteAll — حذف جميع إذون الاستلام (للبيئة التجريبية فقط!)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("CashReceipts.DeleteAll")]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _context.CashReceipts
                .Include(r => r.Customer)
                .ToListAsync();

            if (all.Count == 0)
            {
                TempData["CashReceiptError"] = "لا توجد إذون لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                string? postedBy = User?.Identity?.Name ?? "SYSTEM";

                // عكس القيود لكل إذن مرحّل
                foreach (var receipt in all)
                {
                    if (receipt.IsPosted)
                    {
                        await _ledgerPostingService.ReverseForHeaderDeleteAsync(
                            LedgerSourceType.Receipt,
                            receipt.CashReceiptId,
                            postedBy,
                            "حذف إذن استلام نقدية (حذف جميع)"
                        );
                    }
                }

                _context.CashReceipts.RemoveRange(all);
                await _context.SaveChangesAsync();
                await _ledgerPostingService.RecalcAllCustomerBalancesAsync();

                TempData["CashReceiptSuccess"] = $"تم حذف جميع إذون الاستلام ({all.Count}).";
            }
            catch (Exception ex)
            {
                TempData["CashReceiptError"] = $"حدث خطأ أثناء الحذف: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // دالة مساعدة للتأكد من وجود السجل
        private bool CashReceiptExists(int id)
        {
            return _context.CashReceipts.Any(e => e.CashReceiptId == id);
        }
    }
}
