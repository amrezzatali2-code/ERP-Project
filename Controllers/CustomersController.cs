using ClosedXML.Excel;                            // مكتبة Excel لإنشاء ملف xlsx
using OfficeOpenXml;                              // EPPlus لقراءة ملفات الإكسل
using ERP.Data;                                   // AppDbContext
using ERP.Filters;                                // RequirePermission
using ERP.Infrastructure;                         // PagedResult + UserActivityLogger
using ERP.Models;                                 // Customer, UserActionType
using ERP.Security;                               // PermissionCodes
using ERP.Services;                               // IPermissionService
using ERP.Services.Caching;                       // إبطال كاش بحث الأطراف بعد الحفظ
using ERP.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;                 // القوائم SelectListItem[]
using System.IO;                                  // MemoryStream للتصدير
using System.Linq;
using System.Security.Claims;
using System.Text;                                // StringBuilder + Encoding
using System.Threading.Tasks;

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إدارة جدول العملاء / الأطراف
    /// يعتمد على PagedResult لنفس نظام البحث + الترتيب + التقسيم (نظام القوائم الموحّد).
    /// </summary>
    public partial class CustomersController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IUserActivityLogger _activityLogger;
        private readonly IPermissionService _permissionService;
        private readonly IUserAccountVisibilityService _accountVisibilityService;
        private readonly ICustomerCacheService _customerCache;
        private readonly ILookupCacheService _lookupCache;


        // قائمة ثابتة لأنواع الأطراف (عميل / مورد / موظف / مستثمر / بنك / مصروف / مالك)
        private static readonly string[] PartyCategoryOptions = new[]
        {
            "Customer",   // عميل (عميل بيع عادي)
            "Supplier",   // مورد (أنا بشتري منه)
            "Employee",   // موظف (لو حابب تربطه مالياً بالسلف/العهد)
            "Investor",   // مستثمر (شريك أو صاحب رأس مال له تعامل مستقل)
            "Bank",       // بنك
            "Expense",    // جهة مصروف (شركة نقل، مكتب محاسبة، ... إلخ)
            "Owner"       // صاحب / شريك (مالك المنشأة أو الشريك الرئيسي)
        };

        public CustomersController(
            AppDbContext context,
            IUserActivityLogger activityLogger,
            IPermissionService permissionService,
            IUserAccountVisibilityService accountVisibilityService,
            ICustomerCacheService customerCache,
            ILookupCacheService lookupCache)
        {
            _context = context;
            _activityLogger = activityLogger;
            _permissionService = permissionService;
            _accountVisibilityService = accountVisibilityService;
            _customerCache = customerCache;
            _lookupCache = lookupCache;
        }

        /// <summary>
        /// دالة خاصة ترجع رقم الحساب الافتراضي لكل نوع طرف (حسب النص المخزَّن في PartyCategory)
        /// مثال: "Customer" → حساب العملاء 1103
        /// </summary>
        /// <param name="partyCategory">نوع الطرف المختار من الشاشة (string مثل "Customer")</param>
        /// <returns>AccountId أو null لو مفيش حساب مطابق</returns>
        /// 

        private async Task<int?> GetDefaultAccountIdForPartyAsync(string? partyCategory)
        {
            // لو نوع الطرف فاضي نرجّع null
            if (string.IsNullOrWhiteSpace(partyCategory))
                return null;

            // إزالة المسافات الزايدة في البداية/النهاية
            partyCategory = partyCategory.Trim();

            // -------------------------------------------
            // توحيد القيم: تحويل العربي إلى مفتاح ثابت
            // (Customer / Supplier / Employee / Investor / Owner / Bank / Expense)
            // -------------------------------------------
            switch (partyCategory)
            {
                // عميل
                case "عميل":
                case "زبون":
                    partyCategory = "Customer";
                    break;

                // مورد
                case "مورد":
                    partyCategory = "Supplier";
                    break;

                // موظف
                case "موظف":
                case "عامل":
                    partyCategory = "Employee";
                    break;

                // مستثمر / مالك
                case "مستثمر":
                    partyCategory = "Investor";
                    break;

                case "مالك":
                case "صاحب":
                    partyCategory = "Owner";
                    break;

                // بنك
                case "بنك":
                    partyCategory = "Bank";
                    break;

                // مصروف / مصروفات
                case "مصروف":
                case "مصروفات":
                    partyCategory = "Expense";
                    break;
            }

            // نقرأ كل الحسابات مرة واحدة في الميموري
            var accounts = await _context.Accounts
                .AsNoTracking()
                .ToListAsync();   // متغير: قائمة كل الحسابات

            // دالة داخلية للبحث عن حساب بالكود
            int? FindByCode(string code)
            {
                // نرجّع AccountId لأول حساب مطابق للكود
                return accounts
                    .Where(a => a.AccountCode == code)
                    .Select(a => (int?)a.AccountId)
                    .FirstOrDefault();
            }

            // خريطة بسيطة: كل نوع طرف ↔ كود الحساب في شجرة الحسابات
            switch (partyCategory)
            {
                case "Customer":
                    return FindByCode("1103");   // حساب العملاء

                case "Supplier":
                    return FindByCode("2101");   // الموردون

                case "Employee":
                    return FindByCode("5201");   // مرتبات وأجور (مثال)

                case "Investor":
                case "Owner":
                    return FindByCode("3101");   // حساب المستثمرين 3101 (الظهور يتحكم به «الحسابات المسموح رؤيتها»)

                case "Bank":
                    return FindByCode("1102");   // البنوك

                case "Expense":
                    return FindByCode("5205");   // مصروفات عمومية وإدارية أخرى (مثال)

                default:
                    return null;                 // مفيش حساب افتراضي لهذا النوع
            }
        }








        // ✅ أكشن ترجع رقم الحساب الافتراضي لنوع الطرف (عميل / مورد / ...)
        // تُستخدم من الجافاسكربت في شاشة العملاء
        [HttpGet]
        
        public async Task<IActionResult> GetDefaultAccountForParty(string partyCategory)
        {
            // لو نوع الطرف فاضي نرجع فشل
            if (string.IsNullOrWhiteSpace(partyCategory))
            {
                return Json(new
                {
                    success = false,
                    message = "نوع الطرف غير محدد"
                });
            }

            // ✅ استدعاء دالتك async باستخدام await
            // متغير: رقم الحساب الافتراضي لنوع الطرف (ممكن يكون null)
            int? accountId = await GetDefaultAccountIdForPartyAsync(partyCategory);

            // ✅ نرجع النتيجة للجافاسكربت
            return Json(new
            {
                success = true,
                accountId = accountId   // ممكن تكون null لو مفيش حساب افتراضي
            });
        }

        /// <summary>ترجع نوع الطرف المقترح حسب الحساب المحاسبي المختار (للربط العكسي: تغيير الحساب → تحديث نوع الطرف)</summary>
        [HttpGet]
        public async Task<IActionResult> GetPartyCategoryForAccount(int? accountId)
        {
            if (!accountId.HasValue || accountId.Value == 0)
            {
                return Json(new { success = false, partyCategory = (string?)null });
            }

            var account = await _context.Accounts
                .AsNoTracking()
                .Where(a => a.AccountId == accountId.Value)
                .Select(a => a.AccountCode)
                .FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(account)) 
                return Json(new { success = false, partyCategory = (string?)null });

            // خريطة كود الحساب → نوع الطرف (مطابقة لـ GetDefaultAccountForParty)
            string? partyCategory = account switch
            {
                "1103" => "Customer",
                "2101" => "Supplier",
                "5201" => "Employee",
                "3101" => "Investor",
                "1102" => "Bank",
                "5205" => "Expense",
                _ => null
            };

            return Json(new { success = partyCategory != null, partyCategory });
        }

        /// <summary>جلب الأحياء/المراكز حسب المحافظة المختارة (للتعبئة المتسلسلة)</summary>
        [HttpGet]
        public async Task<IActionResult> GetDistrictsByGovernorate(int governorateId)
        {
            var list = (await _lookupCache.GetDistrictsAsync())
                .Where(d => d.GovernorateId == governorateId)
                .OrderBy(d => d.DistrictName)
                .Select(d => new { id = d.DistrictId, name = d.DistrictName })
                .ToList();
            return Json(list);
        }

        /// <summary>جلب المناطق حسب الحي/المركز المختار (للتعبئة المتسلسلة)</summary>
        [HttpGet]
        public async Task<IActionResult> GetAreasByDistrict(int districtId)
        {
            var list = (await _lookupCache.GetAreasAsync())
                .Where(a => a.DistrictId == districtId)
                .OrderBy(a => a.AreaName)
                .Select(a => new { id = a.AreaId, name = a.AreaName })
                .ToList();
            return Json(list);
        }

        /// <summary>تحديث نوع الطرف والحساب من ملفي إكسل (الموظفين + المستثمرين).</summary>
        [HttpGet]
        [RequirePermission("Customers.Index")]
        public IActionResult UpdatePartyFromExcel()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Customers.Index")]
        public async Task<IActionResult> UpdatePartyFromExcel(IFormFile? employeesFile, IFormFile? investorsFile)
        {
            int employeeAccountId = 0;
            int investorAccountId = 0;
            var empAccount = await _context.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.AccountCode == "5201");
            if (empAccount != null) employeeAccountId = empAccount.AccountId;
            var invAccount = await _context.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.AccountCode == "3101");
            if (invAccount != null) investorAccountId = invAccount.AccountId;

            int updatedEmployees = 0;
            int updatedInvestors = 0;
            var errors = new List<string>();

            static (List<int> ids, List<string> names) ReadIdsAndNamesFromExcel(Stream stream)
            {
                var ids = new List<int>();
                var names = new List<string>();
                ExcelPackage.License.SetNonCommercialPersonal("Amr ERP Dev");
                using var package = new ExcelPackage(stream);
                var sheet = package.Workbook.Worksheets[0];
                if (sheet.Dimension == null) return (ids, names);
                int lastRow = sheet.Dimension.End.Row;
                int lastCol = sheet.Dimension.End.Column;
                var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int c = 1; c <= lastCol; c++)
                {
                    var h = sheet.Cells[1, c].Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(h)) headers[h] = c;
                }
                int? colCode = null;
                foreach (var key in new[] { "كود", "Code", "CustomerId", "رقم", "الرقم", "Id" })
                    if (headers.TryGetValue(key, out var cc)) { colCode = cc; break; }
                int? colName = null;
                foreach (var key in new[] { "اسم", "الاسم", "Name", "CustomerName", "العميل" })
                    if (headers.TryGetValue(key, out var cc)) { colName = cc; break; }
                int col = colCode ?? colName ?? 1;
                for (int row = 2; row <= lastRow; row++)
                {
                    var codeText = colCode.HasValue ? sheet.Cells[row, colCode.Value].Text?.Trim() : null;
                    var nameText = colName.HasValue ? sheet.Cells[row, colName.Value].Text?.Trim() : null;
                    if (!string.IsNullOrWhiteSpace(codeText) && int.TryParse(codeText, out var id))
                        ids.Add(id);
                    else if (!string.IsNullOrWhiteSpace(nameText))
                        names.Add(nameText.Trim());
                    else if (!string.IsNullOrWhiteSpace(sheet.Cells[row, col].Text))
                    {
                        var v = sheet.Cells[row, col].Text?.Trim();
                        if (!string.IsNullOrWhiteSpace(v) && int.TryParse(v, out var id2))
                            ids.Add(id2);
                        else if (!string.IsNullOrWhiteSpace(v))
                            names.Add(v);
                    }
                }
                return (ids, names);
            }

            if (employeesFile != null && employeesFile.Length > 0)
            {
                try
                {
                    using var stream = new MemoryStream();
                    await employeesFile.CopyToAsync(stream);
                    stream.Position = 0;
                    var (ids, names) = ReadIdsAndNamesFromExcel(stream);
                    var nameSet = new HashSet<string>(names.Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase);
                    var query = _context.Customers.AsQueryable();
                    if (ids.Count > 0 && nameSet.Count > 0)
                        query = query.Where(c => ids.Contains(c.CustomerId) || nameSet.Contains((c.CustomerName ?? "").Trim()));
                    else if (ids.Count > 0)
                        query = query.Where(c => ids.Contains(c.CustomerId));
                    else if (nameSet.Count > 0)
                        query = query.Where(c => nameSet.Contains((c.CustomerName ?? "").Trim()));
                    var toUpdate = await query.ToListAsync();
                    foreach (var c in toUpdate)
                    {
                        c.PartyCategory = "Employee";
                        c.AccountId = employeeAccountId > 0 ? employeeAccountId : c.AccountId;
                    }
                    await _context.SaveChangesAsync();
                    _customerCache.ClearCustomersCache(); // تعديل أطراف من الإكسل — إبطال كاش البحث
                    updatedEmployees = toUpdate.Count;
                }
                catch (Exception ex) { errors.Add("ملف الموظفين: " + (ex.InnerException?.Message ?? ex.Message)); }
            }

            if (investorsFile != null && investorsFile.Length > 0)
            {
                try
                {
                    using var stream = new MemoryStream();
                    await investorsFile.CopyToAsync(stream);
                    stream.Position = 0;
                    var (ids, names) = ReadIdsAndNamesFromExcel(stream);
                    var nameSet = new HashSet<string>(names.Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase);
                    var query = _context.Customers.AsQueryable();
                    if (ids.Count > 0 && nameSet.Count > 0)
                        query = query.Where(c => ids.Contains(c.CustomerId) || nameSet.Contains((c.CustomerName ?? "").Trim()));
                    else if (ids.Count > 0)
                        query = query.Where(c => ids.Contains(c.CustomerId));
                    else if (nameSet.Count > 0)
                        query = query.Where(c => nameSet.Contains((c.CustomerName ?? "").Trim()));
                    var toUpdate = await query.ToListAsync();
                    foreach (var c in toUpdate)
                    {
                        c.PartyCategory = "Investor";
                        c.AccountId = investorAccountId > 0 ? investorAccountId : c.AccountId;
                    }
                    await _context.SaveChangesAsync();
                    _customerCache.ClearCustomersCache();
                    updatedInvestors = toUpdate.Count;
                }
                catch (Exception ex) { errors.Add("ملف المستثمرين: " + (ex.InnerException?.Message ?? ex.Message)); }
            }

            if (errors.Count > 0)
                TempData["Error"] = string.Join(" ", errors);
            else
                TempData["Success"] = $"تم تحديث الموظفين: {updatedEmployees} سجل. تم تحديث المستثمرين: {updatedInvestors} سجل.";
            return RedirectToAction(nameof(UpdatePartyFromExcel));
        }



        // دالة مساعدة: تجهيز الـ DropDowns (أنواع الأطراف + الحسابات + السياسات + المستخدمين)
        /// <param name="hiddenAccountIds">الحسابات المخفية عن المستخدم؛ إن وُجدت تُستبعد من القائمة (عرض المسموح فقط).</param>
        private void PopulateDropDowns(
            int? selectedAccountId = null,        // رقم الحساب المحاسبي المختار (لو في تعديل)
            string? selectedPartyCategory = null, // نوع الطرف المختار (عميل / مورد / ...)
            int? selectedPolicyId = null,         // كود سياسة العميل المختارة
            int? selectedUserId = null,           // كود المستخدم المسئول (المندوب) المختار
            int? selectedRouteId = null,          // خط السير (للعميل)
            IReadOnlySet<int>? hiddenAccountIds = null  // الحسابات التي لا يحق للمستخدم رؤيتها (تُستبعد من القائمة)
        )
        {
            // 1) قائمة أنواع الأطراف (Customer / Supplier / ... )
            var partyList = PartyCategoryOptions
                .Select(pc => new SelectListItem
                {
                    Value = pc,                           // القيمة المخزّنة في الداتا بيز
                    Text = pc,                           // النص المعروض (هتحوّله لعربي من الفيو)
                    Selected = (pc == selectedPartyCategory)
                })
                .ToList();

            ViewBag.PartyCategoryList = partyList;

            // 2) قائمة الحسابات المحاسبية — عرض الحسابات المسموح بها فقط (غير المخفية)
            var accountsQuery = _context.Accounts.AsNoTracking();
            if (hiddenAccountIds != null && hiddenAccountIds.Count > 0)
                accountsQuery = accountsQuery.Where(a => !hiddenAccountIds.Contains(a.AccountId));
            var accounts = accountsQuery
                .OrderBy(a => a.AccountCode)
                .Select(a => new
                {
                    a.AccountId,
                    Display = a.AccountCode + " — " + a.AccountName
                })
                .ToList();

            // ✅ لو مفيش حسابات خالص
            if (!accounts.Any())
            {
                ViewBag.AccountId = new SelectList(
                    new[]
                    {
            new { AccountId = 0, Display = "⚠ لا توجد حسابات في شجرة الحسابات" }
                    },
                    "AccountId",
                    "Display",
                    selectedAccountId
                );
            }
            else
            {
                ViewBag.AccountId = new SelectList(
                    accounts,
                    "AccountId",
                    "Display",
                    selectedAccountId
                );
            }



            // 3) قائمة سياسات العملاء
            var policyQuery = _context.Policies
                .AsNoTracking()
                .OrderBy(p => p.PolicyId)
                .Select(p => new
                {
                    p.PolicyId,
                    Display = "سياسة رقم " + p.PolicyId
                })
                .ToList();

            ViewBag.PolicyList = new SelectList(
                policyQuery,
                "PolicyId",
                "Display",
                selectedPolicyId
            );

            // 4) قائمة المستخدمين (المندوبين)
            var usersQuery = _context.Users
                .AsNoTracking()
                .Where(u => u.IsActive)
                .OrderBy(u => u.DisplayName)
                .Select(u => new
                {
                    u.UserId,
                    UserDisplay = u.DisplayName
                })
                .ToList();

            ViewBag.UserList = new SelectList(
                usersQuery,
                "UserId",
                "UserDisplay",
                selectedUserId
            );

            // 5) قائمة خطوط السير
            var routesList = _context.Routes
                .AsNoTracking()
                .Where(r => r.IsActive)
                .OrderBy(r => r.SortOrder).ThenBy(r => r.Name)
                .Select(r => new { r.Id, r.Name })
                .ToList();
            ViewBag.RouteId = new SelectList(
                routesList,
                "Id",
                "Name",
                selectedRouteId
            );
        }






        // دالة مساعدة: تجهيز قوائم المحافظة / الحي / المنطقة (متسلسلة: حي حسب المحافظة، منطقة حسب الحي)
        private async Task FillGeoDropDownsAsync(
            int? selectedGovernorateId = null,
            int? selectedDistrictId = null,
            int? selectedAreaId = null)
        {
            // قائمة المحافظات (دائماً كاملة)
            var govs = await _lookupCache.GetGovernoratesAsync();

            ViewBag.GovernorateId = new SelectList(
                govs,
                "GovernorateId",
                "GovernorateName",
                selectedGovernorateId
            );

            // قائمة الأحياء: فقط عند وجود محافظة محددة (أو للعرض في Edit)
            if (selectedGovernorateId.HasValue)
            {
                var dists = (await _lookupCache.GetDistrictsAsync())
                    .Where(d => d.GovernorateId == selectedGovernorateId.Value)
                    .OrderBy(d => d.DistrictName)
                    .ToList();
                ViewBag.DistrictId = new SelectList(dists, "DistrictId", "DistrictName", selectedDistrictId);
            }
            else
            {
                ViewBag.DistrictId = new SelectList(Enumerable.Empty<SelectListItem>(), "Value", "Text", selectedDistrictId);
            }

            // قائمة المناطق: فقط عند وجود حي محدد (أو للعرض في Edit)
            if (selectedDistrictId.HasValue)
            {
                var areas = (await _lookupCache.GetAreasAsync())
                    .Where(a => a.DistrictId == selectedDistrictId.Value)
                    .OrderBy(a => a.AreaName)
                    .ToList();
                ViewBag.AreaId = new SelectList(areas, "AreaId", "AreaName", selectedAreaId);
            }
            else
            {
                ViewBag.AreaId = new SelectList(Enumerable.Empty<SelectListItem>(), "Value", "Text", selectedAreaId);
            }
        }










        /// <summary>بحث عام في قائمة العملاء مع وضع النص: يبدأ / يحتوي / ينتهي.</summary>
        private static IQueryable<Customer> ApplyCustomerListSearch(IQueryable<Customer> q, string? search, string? searchBy, string? searchMode)
        {
            var s = (search ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(s)) return q;

            var sb = (searchBy ?? "all").Trim().ToLowerInvariant();
            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm != "starts" && sm != "ends") sm = "contains";

            switch (sb)
            {
                case "id":
                    if (int.TryParse(s, out var custId))
                        return q.Where(c => c.CustomerId == custId);
                    return sm switch
                    {
                        "starts" => q.Where(c => c.CustomerId.ToString().StartsWith(s)),
                        "ends" => q.Where(c => c.CustomerId.ToString().EndsWith(s)),
                        _ => q.Where(c => c.CustomerId.ToString().Contains(s))
                    };

                case "name":
                    return sm switch
                    {
                        "starts" => q.Where(c => c.CustomerName.StartsWith(s)),
                        "ends" => q.Where(c => c.CustomerName.EndsWith(s)),
                        _ => q.Where(c => c.CustomerName.Contains(s))
                    };

                case "phone":
                    return sm switch
                    {
                        "starts" => q.Where(c =>
                            (c.Phone1 != null && c.Phone1.StartsWith(s)) ||
                            (c.Phone2 != null && c.Phone2.StartsWith(s)) ||
                            (c.Whatsapp != null && c.Whatsapp.StartsWith(s))),
                        "ends" => q.Where(c =>
                            (c.Phone1 != null && c.Phone1.EndsWith(s)) ||
                            (c.Phone2 != null && c.Phone2.EndsWith(s)) ||
                            (c.Whatsapp != null && c.Whatsapp.EndsWith(s))),
                        _ => q.Where(c =>
                            (c.Phone1 != null && c.Phone1.Contains(s)) ||
                            (c.Phone2 != null && c.Phone2.Contains(s)) ||
                            (c.Whatsapp != null && c.Whatsapp.Contains(s)))
                    };

                case "address":
                    return sm switch
                    {
                        "starts" => q.Where(c => c.Address != null && c.Address.StartsWith(s)),
                        "ends" => q.Where(c => c.Address != null && c.Address.EndsWith(s)),
                        _ => q.Where(c => c.Address != null && c.Address.Contains(s))
                    };

                case "type":
                    return sm switch
                    {
                        "starts" => q.Where(c => c.PartyCategory != null && c.PartyCategory.StartsWith(s)),
                        "ends" => q.Where(c => c.PartyCategory != null && c.PartyCategory.EndsWith(s)),
                        _ => q.Where(c => c.PartyCategory != null && c.PartyCategory.Contains(s))
                    };

                case "account":
                    return sm switch
                    {
                        "starts" => q.Where(c => c.Account != null &&
                            (c.Account.AccountCode.StartsWith(s) || c.Account.AccountName.StartsWith(s))),
                        "ends" => q.Where(c => c.Account != null &&
                            (c.Account.AccountCode.EndsWith(s) || c.Account.AccountName.EndsWith(s))),
                        _ => q.Where(c => c.Account != null &&
                            (c.Account.AccountCode.Contains(s) || c.Account.AccountName.Contains(s)))
                    };

                case "active":
                    var yes = new[] { "1", "نعم", "yes", "true", "صح" };
                    var no = new[] { "0", "لا", "no", "false" };
                    if (yes.Contains(s, StringComparer.OrdinalIgnoreCase))
                        return q.Where(c => c.IsActive);
                    if (no.Contains(s, StringComparer.OrdinalIgnoreCase))
                        return q.Where(c => !c.IsActive);
                    return q;

                case "all":
                default:
                    return sm switch
                    {
                        "starts" => q.Where(c =>
                            c.CustomerId.ToString().StartsWith(s) ||
                            c.CustomerName.StartsWith(s) ||
                            (c.Phone1 != null && c.Phone1.StartsWith(s)) ||
                            (c.Phone2 != null && c.Phone2.StartsWith(s)) ||
                            (c.Whatsapp != null && c.Whatsapp.StartsWith(s)) ||
                            (c.Address != null && c.Address.StartsWith(s)) ||
                            (c.PartyCategory != null && c.PartyCategory.StartsWith(s)) ||
                            (c.Account != null && (c.Account.AccountCode.StartsWith(s) || c.Account.AccountName.StartsWith(s)))),
                        "ends" => q.Where(c =>
                            c.CustomerId.ToString().EndsWith(s) ||
                            c.CustomerName.EndsWith(s) ||
                            (c.Phone1 != null && c.Phone1.EndsWith(s)) ||
                            (c.Phone2 != null && c.Phone2.EndsWith(s)) ||
                            (c.Whatsapp != null && c.Whatsapp.EndsWith(s)) ||
                            (c.Address != null && c.Address.EndsWith(s)) ||
                            (c.PartyCategory != null && c.PartyCategory.EndsWith(s)) ||
                            (c.Account != null && (c.Account.AccountCode.EndsWith(s) || c.Account.AccountName.EndsWith(s)))),
                        _ => q.Where(c =>
                            c.CustomerId.ToString().Contains(s) ||
                            c.CustomerName.Contains(s) ||
                            (c.Phone1 != null && c.Phone1.Contains(s)) ||
                            (c.Phone2 != null && c.Phone2.Contains(s)) ||
                            (c.Whatsapp != null && c.Whatsapp.Contains(s)) ||
                            (c.Address != null && c.Address.Contains(s)) ||
                            (c.PartyCategory != null && c.PartyCategory.Contains(s)) ||
                            (c.Account != null &&
                                (c.Account.AccountCode.Contains(s) || c.Account.AccountName.Contains(s))))
                    };
            }
        }

        // =======================================================
        //  أكشن Index — قائمة العملاء / الأطراف
        // =======================================================
        [RequirePermission("Customers.Index")]
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",
            string? searchMode = "contains",
            string? sort = "name",
            string? dir = "asc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,   // من كود عميل
            int? toCode = null,     // إلى كود عميل
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_name = null,
            string? filterCol_type = null,
            string? filterCol_phone = null,
            string? filterCol_Address = null,
            string? filterCol_governorate = null,
            string? filterCol_district = null,
            string? filterCol_area = null,
            string? filterCol_account = null,
            string? filterCol_PolicyId = null,
            string? filterCol_credit = null,
            string? filterCol_isactive = null,
            string? filterCol_ordercontact = null,
            string? filterCol_created = null,
            string? filterCol_updated = null,
            string? filterCol_quota = null,
            string? filterCol_taxid = null,
            string? filterCol_recordnumber = null,
            string? filterCol_licensenumber = null,
            string? filterCol_segment = null,
            int page = 1,
            int pageSize = 10
        )
        {
            IQueryable<Customer> q = await BuildCustomerListOrderedQueryAsync(
                search, searchBy, searchMode, sort, dir,
                useDateRange, fromDate, toDate, fromCode, toCode,
                filterCol_id, filterCol_idExpr, filterCol_name, filterCol_type, filterCol_phone,
                filterCol_Address, filterCol_governorate, filterCol_district, filterCol_area,
                filterCol_taxid, filterCol_recordnumber, filterCol_licensenumber, filterCol_segment,
                filterCol_account, filterCol_PolicyId, filterCol_credit, filterCol_isactive,
                filterCol_ordercontact, filterCol_created, filterCol_updated, filterCol_quota);

            var s = (search ?? string.Empty).Trim();
            var sb = (searchBy ?? "all").Trim().ToLowerInvariant();
            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm != "starts" && sm != "ends") sm = "contains";
            var so = (sort ?? "name").Trim().ToLowerInvariant();
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            // 7) الترقيم (آخر pageSize، افتراضي 10، «الكل» = 0)
            var pageSizeQuery = Request.Query["pageSize"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageSizeQuery) && int.TryParse(pageSizeQuery, out var psVal))
                pageSize = psVal;

            if (page < 1) page = 1;
            if (pageSize < 0) pageSize = 10;
            if (pageSize > 0 && pageSize != 10 && pageSize != 25 && pageSize != 50 && pageSize != 100 && pageSize != 200)
                pageSize = 10;

            int total = await q.CountAsync();

            int effectivePageSize = pageSize;
            if (pageSize == 0)
            {
                effectivePageSize = total == 0 ? 10 : Math.Min(total, 100_000);
                page = 1;
            }

            int pages = pageSize == 0 ? 1 : Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
            if (page > pages) page = pages;

            var skip = (page - 1) * effectivePageSize;
            if (total > 0 && skip >= total)
            {
                page = Math.Max(1, pages);
                skip = (page - 1) * effectivePageSize;
            }

            var items = await q.Skip(skip).Take(effectivePageSize).ToListAsync();

            var model = new PagedResult<Customer>(items, page, pageSize, total)
            {
                Search = s,
                SortColumn = so,
                SortDescending = desc,
                UseDateRange = useDateRange,
                FromDate = fromDate,
                ToDate = toDate
            };

            // ViewBag للقيم المستخدمة فى الواجهة
            ViewBag.Search = s;
            ViewBag.SearchBy = sb;
            ViewBag.SearchMode = sm;
            ViewBag.Sort = so;
            ViewBag.Dir = desc ? "desc" : "asc";

            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.Total = total;

            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.CodeFrom = fromCode;   // للـ inputs
            ViewBag.CodeTo = toCode;
            ViewBag.DateField = "created";

            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_IdExpr = filterCol_idExpr;
            ViewBag.FilterCol_Name = filterCol_name;
            ViewBag.FilterCol_Type = filterCol_type;
            ViewBag.FilterCol_Phone = filterCol_phone;
            ViewBag.FilterCol_Address = filterCol_Address;
            ViewBag.FilterCol_Governorate = filterCol_governorate;
            ViewBag.FilterCol_District = filterCol_district;
            ViewBag.FilterCol_Area = filterCol_area;
            ViewBag.FilterCol_Account = filterCol_account;
            ViewBag.FilterCol_PolicyId = filterCol_PolicyId;
            ViewBag.FilterCol_Credit = filterCol_credit;
            ViewBag.FilterCol_IsActive = filterCol_isactive;
            ViewBag.FilterCol_OrderContact = filterCol_ordercontact;
            ViewBag.FilterCol_Created = filterCol_created;
            ViewBag.FilterCol_Updated = filterCol_updated;
            ViewBag.FilterCol_Quota = filterCol_quota;
            ViewBag.FilterCol_TaxId = filterCol_taxid;
            ViewBag.FilterCol_RecordNumber = filterCol_recordnumber;
            ViewBag.FilterCol_LicenseNumber = filterCol_licensenumber;
            ViewBag.FilterCol_Segment = filterCol_segment;
            ViewBag.PartyCategoryDisplayNames = PartyCategoryDisplay.ArabicByKey;

            ViewBag.CanEdit = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Customers", "Edit"));
            ViewBag.CanShowEngagement = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Customers", "Show"));

            return View(model);
        }

        /// <summary>جلب القيم المميزة لعمود (للفلترة بنمط Excel) — مع تطبيق نفس فلتر ظهور الحسابات كقائمة العملاء.</summary>
        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var q = _context.Customers
                .Include(c => c.Governorate).Include(c => c.District).Include(c => c.Area).Include(c => c.Account)
                .AsNoTracking();

            q = await _accountVisibilityService.ApplyCustomerVisibilityFilterAsync(q);

            List<(string Value, string Display)> items = column?.ToLowerInvariant() switch
            {
                "id" => (await q.Select(c => c.CustomerId).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "name" => string.IsNullOrEmpty(searchTerm)
                    ? (await q.Where(c => c.CustomerName != null).Select(c => c.CustomerName!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v, v)).ToList()
                    : (await q.Where(c => c.CustomerName != null && EF.Functions.Like(c.CustomerName, "%" + searchTerm + "%")).Select(c => c.CustomerName!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v!, v)).ToList(),
                "type" => PartyCategoryOptions.Select(pc => (pc, pc)).ToList(),
                "phone" => string.IsNullOrEmpty(searchTerm)
                    ? (await q.Where(c => c.Phone1 != null || c.Phone2 != null || c.Whatsapp != null).Select(c => c.Phone1 ?? c.Phone2 ?? c.Whatsapp ?? "").Distinct().Where(x => x != "").OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v, v)).ToList()
                    : (await q.Where(c => (c.Phone1 != null && EF.Functions.Like(c.Phone1, "%" + searchTerm + "%")) || (c.Phone2 != null && EF.Functions.Like(c.Phone2, "%" + searchTerm + "%")) || (c.Whatsapp != null && EF.Functions.Like(c.Whatsapp, "%" + searchTerm + "%"))).Select(c => c.Phone1 ?? c.Phone2 ?? c.Whatsapp ?? "").Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v!, v)).ToList(),
                "address" => string.IsNullOrEmpty(searchTerm)
                    ? (await q.Where(c => c.Address != null).Select(c => c.Address!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v, v)).ToList()
                    : (await q.Where(c => c.Address != null && EF.Functions.Like(c.Address, "%" + searchTerm + "%")).Select(c => c.Address!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v!, v)).ToList(),
                "governorate" => string.IsNullOrEmpty(searchTerm)
                    ? (await q.Where(c => c.Governorate != null).Select(c => c.Governorate!.GovernorateName).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v ?? "", v ?? "")).ToList()
                    : (await q.Where(c => c.Governorate != null && c.Governorate.GovernorateName != null && EF.Functions.Like(c.Governorate.GovernorateName, "%" + searchTerm + "%")).Select(c => c.Governorate!.GovernorateName).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v ?? "", v ?? "")).ToList(),
                "district" => string.IsNullOrEmpty(searchTerm)
                    ? (await q.Where(c => c.District != null).Select(c => c.District!.DistrictName).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v ?? "", v ?? "")).ToList()
                    : (await q.Where(c => c.District != null && c.District.DistrictName != null && EF.Functions.Like(c.District.DistrictName, "%" + searchTerm + "%")).Select(c => c.District!.DistrictName).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v ?? "", v ?? "")).ToList(),
                "area" => await GetAreaColumnValuesMerged(q, searchTerm),
                "taxid" => string.IsNullOrEmpty(searchTerm)
                    ? (await q.Where(c => c.TaxIdOrNationalId != null).Select(c => c.TaxIdOrNationalId!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v ?? "", v ?? "")).ToList()
                    : (await q.Where(c => c.TaxIdOrNationalId != null && EF.Functions.Like(c.TaxIdOrNationalId, "%" + searchTerm + "%")).Select(c => c.TaxIdOrNationalId!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v!, v)).ToList(),
                "recordnumber" => string.IsNullOrEmpty(searchTerm)
                    ? (await q.Where(c => c.RecordNumber != null).Select(c => c.RecordNumber!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v ?? "", v ?? "")).ToList()
                    : (await q.Where(c => c.RecordNumber != null && EF.Functions.Like(c.RecordNumber, "%" + searchTerm + "%")).Select(c => c.RecordNumber!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v!, v)).ToList(),
                "licensenumber" => string.IsNullOrEmpty(searchTerm)
                    ? (await q.Where(c => c.LicenseNumber != null).Select(c => c.LicenseNumber!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v ?? "", v ?? "")).ToList()
                    : (await q.Where(c => c.LicenseNumber != null && EF.Functions.Like(c.LicenseNumber, "%" + searchTerm + "%")).Select(c => c.LicenseNumber!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v!, v)).ToList(),
                "segment" => string.IsNullOrEmpty(searchTerm)
                    ? (await q.Where(c => c.Segment != null).Select(c => c.Segment!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v ?? "", v ?? "")).ToList()
                    : (await q.Where(c => c.Segment != null && EF.Functions.Like(c.Segment, "%" + searchTerm + "%")).Select(c => c.Segment!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v!, v)).ToList(),
                "account" => string.IsNullOrEmpty(searchTerm)
                    ? (await q.Where(c => c.Account != null).Select(c => c.Account!.AccountCode + " — " + c.Account!.AccountName).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v, v)).ToList()
                    : (await q.Where(c => c.Account != null && (EF.Functions.Like(c.Account.AccountCode, "%" + searchTerm + "%") || EF.Functions.Like(c.Account.AccountName, "%" + searchTerm + "%"))).Select(c => c.Account!.AccountCode + " — " + c.Account!.AccountName).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v!, v)).ToList(),
                "policyid" => (await q.Where(c => c.PolicyId.HasValue).Select(c => c.PolicyId!.Value).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v.ToString(), v.ToString())).ToList(),
                "credit" => (await q.Select(c => c.CreditLimit).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v.ToString("0.00"), v.ToString("0.00"))).ToList(),
                "isactive" => new List<(string, string)> { ("نشط", "نشط"), ("موقوف", "موقوف") },
                "ordercontact" => string.IsNullOrEmpty(searchTerm)
                    ? (await q.Where(c => c.OrderContactName != null).Select(c => c.OrderContactName!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v, v)).ToList()
                    : (await q.Where(c => c.OrderContactName != null && EF.Functions.Like(c.OrderContactName, "%" + searchTerm + "%")).Select(c => c.OrderContactName!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v!, v)).ToList(),
                "created" => (await q.Select(c => new { c.CreatedAt.Year, Month = c.CreatedAt.Month }).Distinct().OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).Take(100).ToListAsync()).Select(x => ($"{x.Year}-{x.Month:D2}", $"{x.Year}/{x.Month:D2}")).ToList(),
                "updated" => (await q.Select(c => new { c.UpdatedAt.Year, Month = c.UpdatedAt.Month }).Distinct().OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).Take(100).ToListAsync()).Select(x => ($"{x.Year}-{x.Month:D2}", $"{x.Year}/{x.Month:D2}")).ToList(),
                "quota" => new List<(string, string)> { ("مفعّلة ×1", "مفعّلة ×1"), ("مفعّلة ×2", "مفعّلة ×2"), ("غير مفعّلة", "غير مفعّلة") },
                _ => new List<(string, string)>()
            };

            return Json(items.Select(x => new { value = x.Value, display = x.Display }));
        }

        /// <summary>قيم عمود المنطقة: من جدول المناطق (Area) + من النص (RegionName) لظهور كل المناطق في الفلتر.</summary>
        private static async Task<List<(string Value, string Display)>> GetAreaColumnValuesMerged(IQueryable<Customer> q, string searchTerm)
        {
            List<string> fromArea, fromRegion;
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                fromArea = await q.Where(c => c.Area != null && c.Area.AreaName != null).Select(c => c.Area!.AreaName!.Trim()).Distinct().ToListAsync();
                fromRegion = await q.Where(c => c.RegionName != null && c.RegionName.Trim() != "").Select(c => c.RegionName!.Trim()).Distinct().ToListAsync();
            }
            else
            {
                fromArea = await q.Where(c => c.Area != null && c.Area.AreaName != null && EF.Functions.Like(c.Area.AreaName, "%" + searchTerm + "%")).Select(c => c.Area!.AreaName!.Trim()).Distinct().ToListAsync();
                fromRegion = await q.Where(c => c.RegionName != null && EF.Functions.Like(c.RegionName, "%" + searchTerm + "%")).Select(c => c.RegionName!.Trim()).Distinct().ToListAsync();
            }
            var merged = fromArea.Concat(fromRegion).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).Take(500).Select(v => (v, v)).ToList();
            return merged;
        }




        // =================== حجم تعامل عميل / مورد ===================
        [HttpGet]
        [RequirePermission("Customers.Show")]
        public async Task<IActionResult> Show(int? id, DateTime? fromDate, DateTime? toDate)
        {
            // =========================================================
            // 1) تجهيز قائمة العملاء للأوتوكومبليت (مصدر واحد: خدمة ظهور الحسابات)
            // =========================================================
            var custQuery = _context.Customers.AsNoTracking();
            custQuery = await _accountVisibilityService.ApplyCustomerVisibilityFilterAsync(custQuery);
            var customersVolume = await custQuery
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

            // =========================================================
            // 2) حفظ فترة التاريخ فى ViewBag لعرضها فى الفيو
            // =========================================================
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;

            // =========================================================
            // 3) لو مفيش عميل مختار → نعرض الصفحة بدون بيانات
            // =========================================================
            if (!id.HasValue)
                return View(model: null);

            // =========================================================
            // 4) قراءة بيانات العميل + الحساب المحاسبي + خط السير
            // =========================================================
            var customer = await _context.Customers
                .Include(c => c.Account)
                .Include(c => c.Route)
                .FirstOrDefaultAsync(c => c.CustomerId == id.Value);

            if (customer == null)
            {
                TempData["Error"] = "لم يتم العثور على هذا العميل.";
                return View(model: null);
            }

            // لو العميل غير ظاهر للمستخدم (حسب صلاحيات الحسابات) → لا نسمح بعرض حجم التعامل
            var custVisibilityQuery = _context.Customers.AsNoTracking().Where(c => c.CustomerId == id.Value);
            custVisibilityQuery = await _accountVisibilityService.ApplyCustomerVisibilityFilterAsync(custVisibilityQuery);
            if (!await custVisibilityQuery.AnyAsync())
            {
                return NotFound();
            }

            // =========================================================
            // 5) تجهيز فلتر التاريخ لحجم التعامل فقط (شامل لليوم الأخير)
            // - from inclusive
            // - to inclusive (نحوّلها toExclusive باستخدام +1 يوم)
            // =========================================================
            DateTime? from = fromDate?.Date;                 // متغير: بداية المدة
            DateTime? toExclusive = toDate?.Date.AddDays(1); // متغير: نهاية المدة (حصري)

            // =========================================================
            // دفتر الأستاذ — قواعد موحّدة لجميع العملاء (عميل 1، 6، وأي عميل):
            // - فاتورة مبيعات: LineNo 1 = مدين العميل (CustomerId معبّأ)، نأخذ آخر PostVersion فقط لكل SourceId (لتجنب المضاعفة عند فتح الفاتورة وإعادة الترحيل).
            // - "محذوفة من القائمة" = وجود قيد عكسي 9001 وصفه يحتوي "قائمة الهيدر" فقط (لا نعتبر "عكس ترحيل" حذفاً).
            // =========================================================

            // فواتير المبيعات المحذوفة من القائمة فقط (نفس القائمة تُستخدم للإجمالي ولقائمة الفواتير)
            var deletedSalesInvoiceIds = await _context.LedgerEntries
                .AsNoTracking()
                .Where(e =>
                    e.CustomerId == customer.CustomerId &&
                    e.SourceType == LedgerSourceType.SalesInvoice &&
                    e.LineNo == 9001 &&
                    e.Description != null &&
                    e.Description.Contains("قائمة الهيدر"))
                .Select(e => e.SourceId)
                .Distinct()
                .ToListAsync();

            // إجمالي المبيعات من دفتر الأستاذ: LineNo 1 (مدين العميل)، آخر PostVersion فقط لكل فاتورة
            var salesLedgerEntries = await _context.LedgerEntries
                .AsNoTracking()
                .Where(e =>
                    e.CustomerId == customer.CustomerId &&
                    e.SourceType == LedgerSourceType.SalesInvoice &&
                    e.LineNo == 1 &&
                    e.PostVersion > 0 &&
                    e.SourceId.HasValue &&
                    !deletedSalesInvoiceIds.Contains(e.SourceId.Value))
                .Where(e => !from.HasValue || e.EntryDate >= from.Value)
                .Where(e => !toExclusive.HasValue || e.EntryDate < toExclusive.Value)
                .ToListAsync();

            decimal totalSales = salesLedgerEntries
                .GroupBy(e => e.SourceId!.Value)
                .Select(g => g.OrderByDescending(x => x.PostVersion).First())
                .Sum(e => e.Debit);

            var salesQ = _context.SalesInvoices
                .AsNoTracking()
                .Where(x =>
                    x.CustomerId == customer.CustomerId &&
                    !deletedSalesInvoiceIds.Contains(x.SIId)); // مرحلة وغير مرحلة؛ نستثني المحذوفة فقط

            if (from.HasValue) salesQ = salesQ.Where(x => x.SIDate >= from.Value);
            if (toExclusive.HasValue) salesQ = salesQ.Where(x => x.SIDate < toExclusive.Value);

            // =========================================================
            // دفتر الأستاذ — المشتريات (نفس القواعد لجميع العملاء):
            // - فاتورة مشتريات: LineNo 2 = دائن المورد (CustomerId معبّأ)، آخر PostVersion فقط لكل SourceId.
            // - "محذوفة من القائمة" = قيد عكسي 9001 أو 9002 وصفه "قائمة الهيدر" فقط.
            // =========================================================

            // فواتير المشتريات المحذوفة من القائمة فقط
            var deletedPurchaseInvoiceIds = await _context.LedgerEntries
                .AsNoTracking()
                .Where(e =>
                    e.CustomerId == customer.CustomerId &&
                    e.SourceType == LedgerSourceType.PurchaseInvoice &&
                    (e.LineNo == 9001 || e.LineNo == 9002) &&
                    e.Description != null &&
                    e.Description.Contains("قائمة الهيدر"))
                .Select(e => e.SourceId)
                .Distinct()
                .ToListAsync();

            // ثانياً: أرقام فواتير المشتريات الموجودة في الجدول (مرحلة وغير مرحلة)
            var existingPurchaseInvoiceIds = await _context.PurchaseInvoices
                .AsNoTracking()
                .Where(pi => pi.CustomerId == customer.CustomerId)
                .Select(pi => pi.PIId)
                .ToListAsync();
            
            // ثالثاً: نحسب المشتريات مباشرة من القيود الصحيحة مع استثناء الفواتير المحذوفة
            // ✅ نهج أبسط: نحسب من جميع القيود الصحيحة (LineNo = 2) ونستثني المحذوفة
            var purchasesLedgerQ = _context.LedgerEntries
                .AsNoTracking()
                .Where(e =>
                    e.CustomerId == customer.CustomerId &&
                    e.SourceType == LedgerSourceType.PurchaseInvoice &&
                    e.LineNo == 2 && // دائن المورد
                    e.LineNo < 9000 && // استثناء القيود العكسية
                    e.PostVersion > 0 &&
                    e.Description != null &&
                    !e.Description.Contains("عكس") &&
                    e.SourceId.HasValue && // ✅ التأكد من أن SourceId ليس null
                    !deletedPurchaseInvoiceIds.Contains(e.SourceId.Value) && // ✅ استثناء الفواتير المحذوفة (قيود عكسية)
                    existingPurchaseInvoiceIds.Contains(e.SourceId.Value)); // ✅ استثناء الفواتير غير الموجودة في الجدول (محذوفة قديماً)
            
            if (from.HasValue) purchasesLedgerQ = purchasesLedgerQ.Where(e => e.EntryDate >= from.Value);
            if (toExclusive.HasValue) purchasesLedgerQ = purchasesLedgerQ.Where(e => e.EntryDate < toExclusive.Value);
            
            // ✅ نحسب الإجمالي من Credit (دائن المورد) لأن هذا هو صافي الفاتورة
            // ✅ لكن نحتاج فقط آخر PostVersion لكل فاتورة
            var allPurchasesEntries = await purchasesLedgerQ.ToListAsync();
            
            // ✅ تصفية في الذاكرة: فقط القيود التي لها SourceId صحيح
            var validEntries = allPurchasesEntries
                .Where(e => e.SourceId.HasValue)
                .ToList();
            
            // ✅ تجميع حسب SourceId وأخذ آخر PostVersion لكل فاتورة
            var purchasesBySource = validEntries
                .GroupBy(e => e.SourceId!.Value)
                .Select(g => new
                {
                    SourceId = g.Key,
                    MaxPostVersion = g.Max(e => e.PostVersion),
                    Entries = g.Where(e => e.PostVersion == g.Max(x => x.PostVersion)).ToList()
                })
                .ToList();
            
            // ✅ نحسب الإجمالي من آخر PostVersion لكل فاتورة
            decimal totalPurchases = purchasesBySource.Sum(g => g.Entries.Sum(e => e.Credit));

            // =========================================================
            // 7.1) استعلام منفصل لـ PurchaseInvoices (لحساب عدد الفواتير + قائمة حركة المشتريات)
            // ✅ نعرض كل الفواتير (مرحلة وغير مرحلة) — نستثني المحذوفة من القائمة فقط
            // =========================================================
            var purchasesQ = _context.PurchaseInvoices
                .AsNoTracking()
                .Where(x =>
                    x.CustomerId == customer.CustomerId &&
                    !deletedPurchaseInvoiceIds.Contains(x.PIId));

            if (from.HasValue) purchasesQ = purchasesQ.Where(x => x.PIDate >= from.Value);
            if (toExclusive.HasValue) purchasesQ = purchasesQ.Where(x => x.PIDate < toExclusive.Value);

            // =========================================================
            // 8) إجمالي المرتجعات (مرتجعات البيع + مرتجعات الشراء) من LedgerEntries
            // - مرتجع البيع: LineNo 2 = دائن العميل (يزيد رصيده / يقلل دينه)
            // - مرتجع الشراء: LineNo 1 = مدين العميل (يقلل رصيده / يقلل ديوننا له)
            // ✅ استثناء المرتجعات المعكوسة (التي لها قيود 9001/9002)
            // =========================================================
            var reversedSalesReturnIds = await _context.LedgerEntries
                .AsNoTracking()
                .Where(e =>
                    e.SourceType == LedgerSourceType.SalesReturn &&
                    e.LineNo >= 9000 &&
                    e.SourceId.HasValue)
                .Select(e => e.SourceId!.Value)
                .Distinct()
                .ToListAsync();

            var reversedPurchaseReturnIds = await _context.LedgerEntries
                .AsNoTracking()
                .Where(e =>
                    e.SourceType == LedgerSourceType.PurchaseReturn &&
                    e.LineNo >= 9000 &&
                    e.SourceId.HasValue)
                .Select(e => e.SourceId!.Value)
                .Distinct()
                .ToListAsync();

            var salesReturnsLedgerEntries = await _context.LedgerEntries
                .AsNoTracking()
                .Where(e =>
                    e.CustomerId == customer.CustomerId &&
                    e.SourceType == LedgerSourceType.SalesReturn &&
                    e.LineNo == 2 &&
                    e.PostVersion > 0 &&
                    e.SourceId.HasValue &&
                    !reversedSalesReturnIds.Contains(e.SourceId.Value))
                .Where(e => !from.HasValue || e.EntryDate >= from.Value)
                .Where(e => !toExclusive.HasValue || e.EntryDate < toExclusive.Value)
                .ToListAsync();

            decimal totalSalesReturns = salesReturnsLedgerEntries
                .GroupBy(e => e.SourceId!.Value)
                .Select(g => g.OrderByDescending(x => x.PostVersion).First())
                .Sum(e => e.Credit);

            var purchaseReturnsLedgerEntries = await _context.LedgerEntries
                .AsNoTracking()
                .Where(e =>
                    e.CustomerId == customer.CustomerId &&
                    e.SourceType == LedgerSourceType.PurchaseReturn &&
                    e.LineNo == 1 &&
                    e.PostVersion > 0 &&
                    e.SourceId.HasValue &&
                    !reversedPurchaseReturnIds.Contains(e.SourceId.Value))
                .Where(e => !from.HasValue || e.EntryDate >= from.Value)
                .Where(e => !toExclusive.HasValue || e.EntryDate < toExclusive.Value)
                .ToListAsync();

            decimal totalPurchaseReturns = purchaseReturnsLedgerEntries
                .GroupBy(e => e.SourceId!.Value)
                .Select(g => g.OrderByDescending(x => x.PostVersion).First())
                .Sum(e => e.Debit);

            var salesReturnsQ = _context.SalesReturns
                .AsNoTracking()
                .Where(sr => sr.CustomerId == customer.CustomerId);
            if (from.HasValue) salesReturnsQ = salesReturnsQ.Where(sr => sr.SRDate >= from.Value);
            if (toExclusive.HasValue) salesReturnsQ = salesReturnsQ.Where(sr => sr.SRDate < toExclusive.Value);

            var purchaseReturnsQ = _context.PurchaseReturns
                .AsNoTracking()
                .Where(pr => pr.CustomerId == customer.CustomerId);
            if (from.HasValue) purchaseReturnsQ = purchaseReturnsQ.Where(pr => pr.PRetDate >= from.Value);
            if (toExclusive.HasValue) purchaseReturnsQ = purchaseReturnsQ.Where(pr => pr.PRetDate < toExclusive.Value);

            // =========================================================
            // 9) عدد الفواتير + آخر تاريخ حركة (داخل نفس فترة البحث)
            // =========================================================
            int salesCount = await salesQ.CountAsync();       // متغير: عدد فواتير البيع في الفترة
            int purchasesCount = await purchasesQ.CountAsync();// متغير: عدد فواتير الشراء في الفترة
            int invoiceCount = salesCount + purchasesCount;   // متغير: إجمالي عدد الفواتير

            // ✅ طريقة آمنة بدل MaxAsync (علشان لو مفيش بيانات)
            DateTime? lastSalesDate = await salesQ
                .OrderByDescending(x => x.SIDate)
                .Select(x => (DateTime?)x.SIDate)
                .FirstOrDefaultAsync();

            DateTime? lastPurchaseDate = await purchasesQ
                .OrderByDescending(x => x.PIDate)
                .Select(x => (DateTime?)x.PIDate)
                .FirstOrDefaultAsync();

            DateTime? lastSalesReturnDate = await salesReturnsQ
                .OrderByDescending(sr => sr.SRDate)
                .Select(sr => (DateTime?)sr.SRDate)
                .FirstOrDefaultAsync();
            DateTime? lastPurchaseReturnDate = await purchaseReturnsQ
                .OrderByDescending(pr => pr.PRetDate)
                .Select(pr => (DateTime?)pr.PRetDate)
                .FirstOrDefaultAsync();

            var lastDebitNoteDate = await _context.DebitNotes
                .Where(d => d.CustomerId == customer.CustomerId)
                .OrderByDescending(d => d.NoteDate)
                .Select(d => (DateTime?)d.NoteDate)
                .FirstOrDefaultAsync();
            var lastCreditNoteDate = await _context.CreditNotes
                .Where(c => c.CustomerId == customer.CustomerId)
                .OrderByDescending(c => c.NoteDate)
                .Select(c => (DateTime?)c.NoteDate)
                .FirstOrDefaultAsync();

            var lastDates = new[] { lastSalesDate, lastPurchaseDate, lastSalesReturnDate, lastPurchaseReturnDate, lastDebitNoteDate, lastCreditNoteDate }
                .Where(d => d.HasValue).Select(d => d!.Value).ToList();
            DateTime? lastTransactionDate = lastDates.Count > 0 ? lastDates.Max() : null;

            // =========================================================
            // 10) الرصيد الحالي (مصدر الحقيقة = LedgerEntries) ✅ بدون فلتر تاريخ
            // - الرصيد الحالي = Sum(Debit - Credit) لكل قيود العميل عبر كل الزمن
            // - نعتمد على CustomerId فقط لتفادي اختلاف AccountId
            // =========================================================
            decimal currentBalance = await _context.LedgerEntries
                .AsNoTracking()
                .Where(e => e.CustomerId == customer.CustomerId)
                .SumAsync(e => (decimal?)(e.Debit - e.Credit)) ?? 0m;

            // ✅ نعرضه في الكارت
            customer.CurrentBalance = currentBalance;

            // =========================================================
            // 11) تحميل قوائم الفواتير والمرتجعات للعرض في الجداول
            // =========================================================
            var salesInvoicesList = await salesQ
                .Include(s => s.Customer)
                .Include(s => s.Lines)
                .OrderByDescending(s => s.SIDate)
                .ThenByDescending(s => s.SITime)
                .ToListAsync();

            var purchaseInvoicesList = await purchasesQ
                .Include(p => p.Customer)
                .Include(p => p.Lines)
                .OrderByDescending(p => p.PIDate)
                .ToListAsync();

            var salesReturnsList = await _context.SalesReturns
                .AsNoTracking()
                .Include(sr => sr.Lines)
                .Where(sr => sr.CustomerId == customer.CustomerId)
                .Where(sr => !from.HasValue || sr.SRDate >= from.Value)
                .Where(sr => !toExclusive.HasValue || sr.SRDate < toExclusive.Value)
                .OrderByDescending(sr => sr.SRDate)
                .ThenByDescending(sr => sr.SRId)
                .ToListAsync();

            var purchaseReturnsList = await _context.PurchaseReturns
                .AsNoTracking()
                .Include(pr => pr.Lines)
                .Where(pr => pr.CustomerId == customer.CustomerId)
                .Where(pr => !from.HasValue || pr.PRetDate >= from.Value)
                .Where(pr => !toExclusive.HasValue || pr.PRetDate < toExclusive.Value)
                .OrderByDescending(pr => pr.PRetDate)
                .ThenByDescending(pr => pr.PRetId)
                .ToListAsync();

            var cashReceiptsList = await _context.CashReceipts
                .AsNoTracking()
                .Include(r => r.CashAccount)
                .Where(r => r.CustomerId == customer.CustomerId && r.IsPosted)
                .Where(r => !from.HasValue || r.ReceiptDate >= from.Value)
                .Where(r => !toExclusive.HasValue || r.ReceiptDate < toExclusive.Value)
                .OrderByDescending(r => r.ReceiptDate)
                .ThenByDescending(r => r.CashReceiptId)
                .ToListAsync();

            var cashPaymentsList = await _context.CashPayments
                .AsNoTracking()
                .Include(p => p.CashAccount)
                .Where(p => p.CustomerId == customer.CustomerId && p.IsPosted)
                .Where(p => !from.HasValue || p.PaymentDate >= from.Value)
                .Where(p => !toExclusive.HasValue || p.PaymentDate < toExclusive.Value)
                .OrderByDescending(p => p.PaymentDate)
                .ThenByDescending(p => p.CashPaymentId)
                .ToListAsync();

            // =========================================================
            // إشعارات الخصم والإضافة (مثل فواتير المبيعات والمشتريات)
            // =========================================================
            var debitNotesList = await _context.DebitNotes
                .AsNoTracking()
                .Include(d => d.Customer)
                .Where(d => d.CustomerId == customer.CustomerId)
                .Where(d => !from.HasValue || d.NoteDate >= from.Value)
                .Where(d => !toExclusive.HasValue || d.NoteDate < toExclusive.Value)
                .OrderByDescending(d => d.NoteDate)
                .ThenByDescending(d => d.DebitNoteId)
                .ToListAsync();

            var creditNotesList = await _context.CreditNotes
                .AsNoTracking()
                .Include(c => c.Customer)
                .Where(c => c.CustomerId == customer.CustomerId)
                .Where(c => !from.HasValue || c.NoteDate >= from.Value)
                .Where(c => !toExclusive.HasValue || c.NoteDate < toExclusive.Value)
                .OrderByDescending(c => c.NoteDate)
                .ThenByDescending(c => c.CreditNoteId)
                .ToListAsync();

            // =========================================================
            // 11.1) تحميل أسماء المخازن (فواتير + مرتجعات)
            // =========================================================
            var warehouseIds = salesInvoicesList.Select(s => s.WarehouseId)
                .Union(purchaseInvoicesList.Select(p => p.WarehouseId))
                .Union(salesReturnsList.Select(sr => sr.WarehouseId))
                .Union(purchaseReturnsList.Select(pr => pr.WarehouseId))
                .Distinct()
                .ToList();

            var warehouses = warehouseIds.Count > 0
                ? await _context.Warehouses
                    .AsNoTracking()
                    .Where(w => warehouseIds.Contains(w.WarehouseId))
                    .ToDictionaryAsync(w => w.WarehouseId, w => w.WarehouseName)
                : new Dictionary<int, string>();

            // =========================================================
            // 11.5) رصيد افتتاحي (عرض فقط — من قيود SourceType = Opening)
            // =========================================================
            var openingBalanceAmount = await _context.LedgerEntries
                .AsNoTracking()
                .Where(e => e.CustomerId == customer.CustomerId && e.SourceType == LedgerSourceType.Opening)
                .SumAsync(e => (decimal?)(e.Debit - e.Credit)) ?? 0m;
            ViewBag.OpeningBalanceAmount = openingBalanceAmount;

            // =========================================================
            // 12) تمرير النتائج للفيو
            // =========================================================
            ViewBag.TotalSales = totalSales;
            ViewBag.TotalPurchases = totalPurchases;
            ViewBag.TotalSalesReturns = totalSalesReturns;
            ViewBag.TotalPurchaseReturns = totalPurchaseReturns;
            ViewBag.InvoiceCount = invoiceCount;
            ViewBag.LastTransactionDate = lastTransactionDate;
            ViewBag.SalesInvoicesList = salesInvoicesList;
            ViewBag.PurchaseInvoicesList = purchaseInvoicesList;
            ViewBag.SalesReturnsList = salesReturnsList;
            ViewBag.PurchaseReturnsList = purchaseReturnsList;
            ViewBag.CashReceiptsList = cashReceiptsList;
            ViewBag.CashPaymentsList = cashPaymentsList;
            ViewBag.DebitNotesList = debitNotesList;
            ViewBag.CreditNotesList = creditNotesList;
            ViewBag.Warehouses = warehouses;

            return View(customer);
        }











        // ======================================================
        // GET: Customers/Create
        // ======================================================
        [RequirePermission("Customers.Create")]
        public async Task<IActionResult> Create()
        {
            var model = new Customer
            {
                IsActive = true
            };

            var hiddenAccountIds = await _accountVisibilityService.GetHiddenAccountIdsForCurrentUserAsync();
            PopulateDropDowns(null, null, null, null, null, hiddenAccountIds);

            await FillGeoDropDownsAsync();

            // نرسل الموديل للفيو بدل View() الفاضية
            return View(model);
        }


        // ======================================================
        // POST: Customers/Create
        // ======================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Customers.Create")]
        public async Task<IActionResult> Create(Customer customer)
        {
            // منطق الكوتة: لو الخيار غير مفعّل نخلى المضاعِف = 1 كقيمة آمنة
            if (!customer.IsQuotaMultiplierEnabled)
            {
                customer.QuotaMultiplier = 1;
            }

            // لو المستخدم ما اختارش حساب محاسبي، نحدد الافتراضي حسب نوع الطرف
            if (!customer.AccountId.HasValue)
            {
                customer.AccountId = await GetDefaultAccountIdForPartyAsync(customer.PartyCategory);
            }

            if (ModelState.IsValid)
            {
                customer.CreatedAt = DateTime.Now;
                customer.UpdatedAt = DateTime.Now;

                _context.Add(customer);
                await _context.SaveChangesAsync();
                _customerCache.ClearCustomersCache();

                await _activityLogger.LogAsync(
                    UserActionType.Create,
                    "Customer",
                    customer.CustomerId,
                    $"إنشاء عميل جديد: {customer.CustomerName}");

                TempData["SuccessMessage"] = "تم إضافة العميل بنجاح.";
                return RedirectToAction(nameof(Index));
            }

            var hiddenAccountIdsCreate = await _accountVisibilityService.GetHiddenAccountIdsForCurrentUserAsync();
            PopulateDropDowns(
                customer.AccountId,
                customer.PartyCategory,
                customer.PolicyId,
                customer.UserId,
                customer.RouteId,
                hiddenAccountIdsCreate
            );

            await FillGeoDropDownsAsync(
                 customer.GovernorateId,
                 customer.DistrictId,
                 customer.AreaId);

            return View(customer);
        }





        // ======================================================
        // GET: Customers/Edit/5
        // ======================================================
        [RequirePermission("Customers.Edit")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
                return NotFound();

            var hiddenAccountIds = await _accountVisibilityService.GetHiddenAccountIdsForCurrentUserAsync();
            var customer = await _context.Customers.FindAsync(id.Value);
            if (customer == null)
                return NotFound();

            if (customer.AccountId.HasValue && hiddenAccountIds.Contains(customer.AccountId.Value))
                return NotFound();

            PopulateDropDowns(
                customer.AccountId,
                customer.PartyCategory,
                customer.PolicyId,
                customer.UserId,
                customer.RouteId,
                hiddenAccountIds
            );

            await FillGeoDropDownsAsync(
                customer.GovernorateId,
                customer.DistrictId,
                customer.AreaId);

            return View(customer);
        }





        // ======================================================
        // POST: Customers/Edit/5
        // ======================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Customers.Edit")]
        public async Task<IActionResult> Edit(int id, Customer customer)
        {
            if (id != customer.CustomerId)
                return NotFound();

            // منطق الكوتة: إلغاء التفعيل يعيد المضاعف إلى 1
            if (!customer.IsQuotaMultiplierEnabled)
            {
                customer.QuotaMultiplier = 1;
            }

            // لو الحساب مش متحدد نحاول نجيب الافتراضي حسب نوع الطرف
            if (!customer.AccountId.HasValue || customer.AccountId == 0)
            {
                customer.AccountId = await GetDefaultAccountIdForPartyAsync(customer.PartyCategory);
            }

            if (!ModelState.IsValid)
            {
                var hiddenForEditPost = await _accountVisibilityService.GetHiddenAccountIdsForCurrentUserAsync();
                PopulateDropDowns(
                    customer.AccountId,
                    customer.PartyCategory,
                    customer.PolicyId,
                    customer.UserId,
                    customer.RouteId,
                    hiddenForEditPost
                );

                await FillGeoDropDownsAsync(
                    customer.GovernorateId,
                    customer.DistrictId,
                    customer.AreaId);

                return View(customer);
            }

            var existing = await _context.Customers
                .FirstOrDefaultAsync(c => c.CustomerId == id);

            if (existing == null)
                return NotFound();

            var hiddenAccountIdsEdit = await _accountVisibilityService.GetHiddenAccountIdsForCurrentUserAsync();
            if (existing.AccountId.HasValue && hiddenAccountIdsEdit.Contains(existing.AccountId.Value))
                return NotFound();

            var oldValues = System.Text.Json.JsonSerializer.Serialize(new
            {
                existing.CustomerName,
                existing.Phone1,
                existing.Phone2,
                existing.Whatsapp,
                existing.Address,
                existing.PartyCategory,
                existing.AccountId,
                existing.CreditLimit,
                existing.IsActive,
                existing.Notes,
                existing.GovernorateId,
                existing.DistrictId,
                existing.AreaId,
                existing.RouteId,
                existing.PolicyId,
                existing.UserId,
                existing.OrderContactName,
                existing.OrderContactPhone,
                existing.IsQuotaMultiplierEnabled,
                existing.QuotaMultiplier
            });

            try
            {
                // ===== البيانات الأساسية =====
                existing.CustomerName = customer.CustomerName;
                existing.Phone1 = customer.Phone1;
                existing.Phone2 = customer.Phone2;
                existing.Whatsapp = customer.Whatsapp;
                existing.Address = customer.Address;
                existing.PartyCategory = customer.PartyCategory;
                existing.AccountId = customer.AccountId;
                existing.CreditLimit = customer.CreditLimit;
                existing.IsActive = customer.IsActive;
                existing.Notes = customer.Notes;

                // ✅ الجديد: حفظ المحافظة / الحي / المنطقة
                existing.GovernorateId = customer.GovernorateId;
                existing.DistrictId = customer.DistrictId;
                existing.AreaId = customer.AreaId;
                existing.RouteId = customer.RouteId;

                // ===== السياسة والكوتة =====
                existing.PolicyId = customer.PolicyId;
                existing.UserId = customer.UserId;
                existing.OrderContactName = customer.OrderContactName;
                existing.OrderContactPhone = customer.OrderContactPhone;
                existing.IsQuotaMultiplierEnabled = customer.IsQuotaMultiplierEnabled;
                existing.QuotaMultiplier = customer.QuotaMultiplier;

                existing.UpdatedAt = DateTime.Now;

                await _context.SaveChangesAsync();
                _customerCache.ClearCustomersCache();

                var newValues = System.Text.Json.JsonSerializer.Serialize(new
                {
                    existing.CustomerName,
                    existing.Phone1,
                    existing.Phone2,
                    existing.Whatsapp,
                    existing.Address,
                    existing.PartyCategory,
                    existing.AccountId,
                    existing.CreditLimit,
                    existing.IsActive,
                    existing.Notes,
                    existing.GovernorateId,
                    existing.DistrictId,
                    existing.AreaId,
                    existing.RouteId,
                    existing.PolicyId,
                    existing.UserId,
                    existing.OrderContactName,
                    existing.OrderContactPhone,
                    existing.IsQuotaMultiplierEnabled,
                    existing.QuotaMultiplier
                });
                await _activityLogger.LogAsync(
                    UserActionType.Edit,
                    "Customer",
                    existing.CustomerId,
                    $"تعديل عميل: {existing.CustomerName}",
                    oldValues,
                    newValues);

                TempData["SuccessMessage"] = "تم تعديل بيانات العميل بنجاح.";
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!CustomerExists(customer.CustomerId))
                    return NotFound();
                else
                    throw;
            }

            return RedirectToAction(nameof(Index));
        }






        // ======================================================
        // GET: Customers/Delete/5
        // ======================================================
        [RequirePermission("Customers.Delete")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
                return NotFound();

            var hiddenAccountIds = await _accountVisibilityService.GetHiddenAccountIdsForCurrentUserAsync();
            var customer = await _context.Customers
                .Include(c => c.Account)
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.CustomerId == id.Value);

            if (customer == null)
                return NotFound();

            if (customer.AccountId.HasValue && hiddenAccountIds.Contains(customer.AccountId.Value))
                return NotFound();

            return View(customer);
        }

        // ======================================================
        // POST: Customers/Delete/5
        // ======================================================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [RequirePermission("Customers.Delete")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var hiddenAccountIds = await _accountVisibilityService.GetHiddenAccountIdsForCurrentUserAsync();
            var customer = await _context.Customers.FindAsync(id);
            if (customer != null)
            {
                if (customer.AccountId.HasValue && hiddenAccountIds.Contains(customer.AccountId.Value))
                    return NotFound();
                var oldValues = System.Text.Json.JsonSerializer.Serialize(new
                {
                    customer.CustomerName,
                    customer.Phone1,
                    customer.PartyCategory,
                    customer.CreditLimit
                });
                _context.Customers.Remove(customer);
                await _context.SaveChangesAsync();
                _customerCache.ClearCustomersCache();

                await _activityLogger.LogAsync(
                    UserActionType.Delete,
                    "Customer",
                    id,
                    $"حذف عميل: {customer.CustomerName}",
                    oldValues: oldValues);

                TempData["SuccessMessage"] = "تم حذف العميل بنجاح.";
            }

            return RedirectToAction(nameof(Index));
        }

        // ===================== DeleteAll =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Customers.Delete")]
        public async Task<IActionResult> DeleteAll()
        {
            var allCustomers = await _context.Customers.ToListAsync();

            if (!allCustomers.Any())
            {
                TempData["Error"] = "لا توجد أي سجلات عملاء لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _context.Customers.RemoveRange(allCustomers);
                await _context.SaveChangesAsync();
                _customerCache.ClearCustomersCache();

                TempData["Success"] = $"تم حذف جميع العملاء بنجاح (عددهم {allCustomers.Count}).";
            }
            catch (DbUpdateException)
            {
                TempData["Error"] = "تعذر حذف جميع العملاء بسبب وجود حركات أو بيانات مالية مرتبطة ببعضهم.";
            }

            return RedirectToAction(nameof(Index));
        }

        // ===================== BulkDelete =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Customers.Delete")]
        public async Task<IActionResult> BulkDelete(string selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["Error"] = "لم يتم اختيار أي عميل للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var ids = selectedIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var id) ? (int?)id : null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();

            if (!ids.Any())
            {
                TempData["Error"] = "تعذر قراءة الأكواد المختارة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var customers = await _context.Customers
                .Where(c => ids.Contains(c.CustomerId))
                .ToListAsync();

            if (!customers.Any())
            {
                TempData["Error"] = "لم يتم العثور على أي عميل مطابق للأكواد المحددة.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _context.Customers.RemoveRange(customers);
                await _context.SaveChangesAsync();
                _customerCache.ClearCustomersCache();

                TempData["Success"] = $"تم حذف {customers.Count} عميل/عملاء بنجاح.";
            }
            catch (DbUpdateException)
            {
                TempData["Error"] = "حدث خطأ أثناء الحذف. ربما يوجد عملاء مرتبطون بحركات أخرى تمنع حذفهم.";
            }

            return RedirectToAction(nameof(Index));
        }

        // ===================== Export =====================
        [HttpGet]
        [RequirePermission("Customers.Export")]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy,
            string? searchMode,
            string? sort,
            string? dir,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? codeFrom = null,
            int? codeTo = null,
            string format = "excel")
        {
            IQueryable<Customer> query = _context.Customers
                .Include(c => c.Account)
                .Include(c => c.Governorate).Include(c => c.District).Include(c => c.Area)
                .AsNoTracking();

            query = await _accountVisibilityService.ApplyCustomerVisibilityFilterAsync(query);

            var s = (search ?? string.Empty).Trim();
            var searchKey = (searchBy ?? "all").Trim().ToLowerInvariant();
            var so = (sort ?? "name").Trim().ToLowerInvariant();
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            query = ApplyCustomerListSearch(query, s, searchKey, searchMode);

            if (useDateRange)
            {
                if (fromDate.HasValue)
                    query = query.Where(c => c.CreatedAt >= fromDate.Value);

                if (toDate.HasValue)
                    query = query.Where(c => c.CreatedAt <= toDate.Value);
            }

            if (codeFrom.HasValue)
                query = query.Where(c => c.CustomerId >= codeFrom.Value);

            if (codeTo.HasValue)
                query = query.Where(c => c.CustomerId <= codeTo.Value);

            query = so switch
            {
                "id" => desc
                    ? query.OrderByDescending(c => c.CustomerId)
                    : query.OrderBy(c => c.CustomerId),

                "type" => desc
                    ? query.OrderByDescending(c => c.PartyCategory)
                    : query.OrderBy(c => c.PartyCategory),

                "account" => desc
                    ? query.OrderByDescending(c => c.Account != null ? c.Account.AccountCode : "")
                    : query.OrderBy(c => c.Account != null ? c.Account.AccountCode : ""),

                "isactive" => desc
                    ? query.OrderByDescending(c => c.IsActive)
                    : query.OrderBy(c => c.IsActive),

                "created" => desc
                    ? query.OrderByDescending(c => c.CreatedAt)
                    : query.OrderBy(c => c.CreatedAt),

                "updated" => desc
                    ? query.OrderByDescending(c => c.UpdatedAt)
                    : query.OrderBy(c => c.UpdatedAt),

                "name" or _ => desc
                    ? query.OrderByDescending(c => c.CustomerName)
                    : query.OrderBy(c => c.CustomerName),
            };

            var data = await query.ToListAsync();

            // CSV
            if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
            {
                var csvBuilder = new StringBuilder();

                csvBuilder.AppendLine(
                    "كود العميل,كود الإكسل,اسم العميل,نوع الطرف,الهاتف,العنوان,المحافظة,الحي,المنطقة,الرقم الضريبي/القومي,رقم السجل,رقم الرخصة,الشريحة,الحساب المحاسبي,كود السياسة,كود المستخدم,اسم مسئول الطلب,هاتف مسئول الطلب,مضاعفة الكوتة,مضاعف الكوتة,الحد الائتماني,الحالة,تاريخ الإنشاء,آخر تعديل");

                string CsvEscape(string? value)
                {
                    if (string.IsNullOrEmpty(value)) return "";
                    value = value.Replace("\"", "\"\"");
                    return $"\"{value}\"";
                }

                foreach (var c in data)
                {
                    var phone = c.Phone1 ?? c.Phone2 ?? c.Whatsapp ?? "";
                    var account = c.Account != null
                        ? $"{c.Account.AccountCode} - {c.Account.AccountName}"
                        : "";
                    var status = c.IsActive ? "نشط" : "موقوف";
                    var quotaEnabled = c.IsQuotaMultiplierEnabled ? "نعم" : "لا";
                    var region = c.RegionName ?? c.Area?.AreaName ?? "";

                    csvBuilder.AppendLine(string.Join(",",
                        c.CustomerId,
                        CsvEscape(c.ExternalCode ?? ""),
                        CsvEscape(c.CustomerName),
                        CsvEscape(c.PartyCategory ?? ""),
                        CsvEscape(phone),
                        CsvEscape(c.Address),
                        CsvEscape(c.Governorate?.GovernorateName ?? ""),
                        CsvEscape(c.District?.DistrictName ?? ""),
                        CsvEscape(region),
                        CsvEscape(c.TaxIdOrNationalId ?? ""),
                        CsvEscape(c.RecordNumber ?? ""),
                        CsvEscape(c.LicenseNumber ?? ""),
                        CsvEscape(c.Segment ?? ""),
                        CsvEscape(account),
                        CsvEscape(c.PolicyId?.ToString() ?? ""),
                        CsvEscape(c.UserId?.ToString() ?? ""),
                        CsvEscape(c.OrderContactName),
                        CsvEscape(c.OrderContactPhone),
                        CsvEscape(quotaEnabled),
                        CsvEscape(c.QuotaMultiplier.ToString()),
                        c.CreditLimit.ToString("0.00"),
                        CsvEscape(status),
                        c.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                        c.UpdatedAt.ToString("yyyy-MM-dd HH:mm")
                    ));
                }

                var bytes = Encoding.UTF8.GetPreamble()
                    .Concat(Encoding.UTF8.GetBytes(csvBuilder.ToString()))
                    .ToArray();

                var csvName = ExcelExportNaming.ArabicTimestampedFileName("العملاء والموردين", ".csv");
                return File(bytes, "text/csv; charset=utf-8", csvName);
            }
            else
            {
                using var workbook = new XLWorkbook();
                var ws = workbook.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("العملاء والموردين"));

                int row = 1;
                ws.Cell(row, 1).Value = "كود العميل";
                ws.Cell(row, 2).Value = "كود الإكسل";
                ws.Cell(row, 3).Value = "اسم العميل";
                ws.Cell(row, 4).Value = "نوع الطرف";
                ws.Cell(row, 5).Value = "الهاتف";
                ws.Cell(row, 6).Value = "العنوان";
                ws.Cell(row, 7).Value = "المحافظة";
                ws.Cell(row, 8).Value = "الحي";
                ws.Cell(row, 9).Value = "المنطقة";
                ws.Cell(row, 10).Value = "الرقم الضريبي/القومي";
                ws.Cell(row, 11).Value = "رقم السجل";
                ws.Cell(row, 12).Value = "رقم الرخصة";
                ws.Cell(row, 13).Value = "الشريحة";
                ws.Cell(row, 14).Value = "الحساب المحاسبي";
                ws.Cell(row, 15).Value = "كود السياسة";
                ws.Cell(row, 16).Value = "كود المستخدم";
                ws.Cell(row, 17).Value = "اسم مسئول الطلب";
                ws.Cell(row, 18).Value = "هاتف مسئول الطلب";
                ws.Cell(row, 19).Value = "مضاعفة الكوتة؟";
                ws.Cell(row, 20).Value = "مضاعِف الكوتة";
                ws.Cell(row, 21).Value = "الحد الائتماني";
                ws.Cell(row, 22).Value = "الحالة";
                ws.Cell(row, 23).Value = "تاريخ الإنشاء";
                ws.Cell(row, 24).Value = "آخر تعديل";

                foreach (var c in data)
                {
                    row++;
                    var phone = c.Phone1 ?? c.Phone2 ?? c.Whatsapp ?? "";
                    var account = c.Account != null
                        ? $"{c.Account.AccountCode} - {c.Account.AccountName}"
                        : "";
                    var status = c.IsActive ? "نشط" : "موقوف";
                    var quotaEnabled = c.IsQuotaMultiplierEnabled ? "نعم" : "لا";
                    var region = c.RegionName ?? c.Area?.AreaName ?? "";

                    ws.Cell(row, 1).Value = c.CustomerId;
                    ws.Cell(row, 2).Value = c.ExternalCode ?? "";
                    ws.Cell(row, 3).Value = c.CustomerName;
                    ws.Cell(row, 4).Value = c.PartyCategory;
                    ws.Cell(row, 5).Value = phone;
                    ws.Cell(row, 6).Value = c.Address;
                    ws.Cell(row, 7).Value = c.Governorate?.GovernorateName ?? "";
                    ws.Cell(row, 8).Value = c.District?.DistrictName ?? "";
                    ws.Cell(row, 9).Value = region;
                    ws.Cell(row, 10).Value = c.TaxIdOrNationalId ?? "";
                    ws.Cell(row, 11).Value = c.RecordNumber ?? "";
                    ws.Cell(row, 12).Value = c.LicenseNumber ?? "";
                    ws.Cell(row, 13).Value = c.Segment ?? "";
                    ws.Cell(row, 14).Value = account;
                    ws.Cell(row, 15).Value = c.PolicyId;
                    ws.Cell(row, 16).Value = c.UserId;
                    ws.Cell(row, 17).Value = c.OrderContactName;
                    ws.Cell(row, 18).Value = c.OrderContactPhone;
                    ws.Cell(row, 19).Value = quotaEnabled;
                    ws.Cell(row, 20).Value = c.QuotaMultiplier;
                    ws.Cell(row, 21).Value = c.CreditLimit;
                    ws.Cell(row, 22).Value = status;
                    ws.Cell(row, 23).Value = c.CreatedAt;
                    ws.Cell(row, 24).Value = c.UpdatedAt;
                }

                ws.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                var content = stream.ToArray();
                var fileName = ExcelExportNaming.ArabicTimestampedFileName("العملاء والموردين", ".xlsx");

                return File(content,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileName);
            }
        }

        // ======================================================
        // إنشاء مناطق من أسماء المناطق النصية للعملاء (استيراد المناطق من جدول العملاء)
        // يقرأ القيم المميزة من عمود المنطقة النصي (RegionName) وينشئ لها سجلات في جدول المناطق ثم يربط العملاء بها.
        // ======================================================
        [HttpGet]
        [RequirePermission("Customers.Index")]
        public async Task<IActionResult> CreateAreasFromCustomerRegions()
        {
            var governorates = await _context.Governorates.OrderBy(g => g.GovernorateName).Select(g => new { g.GovernorateId, g.GovernorateName }).ToListAsync();
            var districts = await _context.Districts.OrderBy(d => d.DistrictName).Select(d => new { d.DistrictId, d.DistrictName, d.GovernorateId }).ToListAsync();
            ViewBag.Governorates = governorates.Select(g => new SelectListItem { Value = g.GovernorateId.ToString(), Text = g.GovernorateName }).ToList();
            ViewBag.Districts = districts.Select(d => new SelectListItem { Value = d.DistrictId.ToString(), Text = d.DistrictName }).ToList();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Customers.Index")]
        public async Task<IActionResult> CreateAreasFromCustomerRegions(int? governorateId, int? districtId)
        {
            int gid;
            int did;
            if (governorateId.HasValue && districtId.HasValue)
            {
                var district = await _context.Districts.FindAsync(districtId.Value);
                if (district == null || district.GovernorateId != governorateId.Value)
                {
                    TempData["Error"] = "الحي المختار لا يتبع المحافظة المختارة.";
                    return RedirectToAction("Import", "Products");
                }
                gid = governorateId.Value;
                did = districtId.Value;
            }
            else
            {
                var firstGov = await _context.Governorates.OrderBy(g => g.GovernorateId).FirstOrDefaultAsync();
                if (firstGov == null)
                {
                    TempData["Error"] = "لا توجد محافظات في النظام. أضف محافظة وحيّاً أولاً.";
                    return RedirectToAction("Import", "Products");
                }
                var firstDist = await _context.Districts.Where(d => d.GovernorateId == firstGov.GovernorateId).OrderBy(d => d.DistrictId).FirstOrDefaultAsync();
                if (firstDist == null)
                {
                    TempData["Error"] = "لا يوجد حي/مركز في المحافظة الأولى. أضف حيّاً أولاً.";
                    return RedirectToAction("Import", "Products");
                }
                gid = firstGov.GovernorateId;
                did = firstDist.DistrictId;
            }

            var regionNames = await _context.Customers
                .Where(c => c.RegionName != null && c.RegionName.Trim() != "")
                .Select(c => c.RegionName!.Trim())
                .Distinct()
                .ToListAsync();

            if (regionNames.Count == 0)
            {
                TempData["Error"] = "لا توجد أسماء مناطق نصية عند العملاء (عمود المنطقة النصي فارغ). قم باستيراد العملاء أولاً مع عمود المنطقة.";
                return RedirectToAction("Import", "Products");
            }

            var existingAreas = await _context.Areas
                .Where(a => a.DistrictId == did)
                .ToDictionaryAsync(a => a.AreaName.Trim(), a => a.AreaId, StringComparer.OrdinalIgnoreCase);

            int created = 0, linked = 0;
            foreach (var name in regionNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (existingAreas.TryGetValue(name, out var areaId))
                {
                    var count = await _context.Customers.Where(c => c.RegionName != null && c.RegionName.Trim() == name).ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.AreaId, areaId)
                        .SetProperty(c => c.RegionName, (string?)null));
                    linked += count;
                    continue;
                }
                var newArea = new Area
                {
                    AreaName = name,
                    GovernorateId = gid,
                    DistrictId = did,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.Areas.Add(newArea);
                await _context.SaveChangesAsync();
                existingAreas[name] = newArea.AreaId;
                created++;
                var linkedCount = await _context.Customers.Where(c => c.RegionName != null && c.RegionName.Trim() == name).ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.AreaId, newArea.AreaId)
                    .SetProperty(c => c.RegionName, (string?)null));
                linked += linkedCount;
            }

            TempData["Success"] = $"تم إنشاء {created} منطقة جديدة وربط {linked} عميل بها. المناطق تظهر الآن في عمود «المنطقة» في قائمة العملاء من جدول المناطق.";
            return RedirectToAction("Import", "Products");
        }

        // ======================================================
        // دالة مساعدة: هل العميل موجود؟
        // ======================================================
        private bool CustomerExists(int id)
        {
            return _context.Customers.Any(e => e.CustomerId == id);
        }
    }
}
