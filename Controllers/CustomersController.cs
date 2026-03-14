using ClosedXML.Excel;                            // مكتبة Excel لإنشاء ملف xlsx
using ERP.Data;                                   // AppDbContext
using ERP.Filters;                                // RequirePermission
using ERP.Infrastructure;                         // PagedResult + UserActivityLogger
using ERP.Models;                                 // Customer, UserActionType
using ERP.Security;                               // PermissionCodes
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;                 // القوائم SelectListItem[]
using System.IO;                                  // MemoryStream للتصدير
using System.Linq;
using System.Text;                                // StringBuilder + Encoding
using System.Threading.Tasks;

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إدارة جدول العملاء / الأطراف
    /// يعتمد على PagedResult لنفس نظام البحث + الترتيب + التقسيم (نظام القوائم الموحّد).
    /// </summary>
    public class CustomersController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IUserActivityLogger _activityLogger;


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

        public CustomersController(AppDbContext context, IUserActivityLogger activityLogger)
        {
            _context = context;
            _activityLogger = activityLogger;
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
                    return FindByCode("3101");   // رأس المال / حقوق الملكية

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

        /// <summary>جلب الأحياء/المراكز حسب المحافظة المختارة (للتعبئة المتسلسلة)</summary>
        [HttpGet]
        public async Task<IActionResult> GetDistrictsByGovernorate(int governorateId)
        {
            var list = await _context.Districts
                .AsNoTracking()
                .Where(d => d.GovernorateId == governorateId)
                .OrderBy(d => d.DistrictName)
                .Select(d => new { id = d.DistrictId, name = d.DistrictName })
                .ToListAsync();
            return Json(list);
        }

        /// <summary>جلب المناطق حسب الحي/المركز المختار (للتعبئة المتسلسلة)</summary>
        [HttpGet]
        public async Task<IActionResult> GetAreasByDistrict(int districtId)
        {
            var list = await _context.Areas
                .AsNoTracking()
                .Where(a => a.DistrictId == districtId)
                .OrderBy(a => a.AreaName)
                .Select(a => new { id = a.AreaId, name = a.AreaName })
                .ToListAsync();
            return Json(list);
        }





        // دالة مساعدة: تجهيز الـ DropDowns (أنواع الأطراف + الحسابات + السياسات + المستخدمين)
        private void PopulateDropDowns(
            int? selectedAccountId = null,        // رقم الحساب المحاسبي المختار (لو في تعديل)
            string? selectedPartyCategory = null, // نوع الطرف المختار (عميل / مورد / ...)
            int? selectedPolicyId = null,         // كود سياسة العميل المختارة
            int? selectedUserId = null,           // كود المستخدم المسئول (المندوب) المختار
            int? selectedRouteId = null           // خط السير (للعميل)
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

            // 2) قائمة الحسابات المحاسبية
            //    هنا نجيب *كل* الحسابات من جدول Accounts بدون أي فلاتر
            // ✅ تحميل الحسابات من جدول Accounts لربطها بالعميل
            // ✅ قائمة الحسابات المحاسبية لربطها بالعميل
            var accounts = _context.Accounts
                .AsNoTracking()                          // قراءة بدون تتبّع (أسرع للقراءة فقط)
                .OrderBy(a => a.AccountCode)             // ترتيب حسب كود الحساب
                .Select(a => new
                {
                    a.AccountId,                         // رقم الحساب (القيمة المخزّنة في العميل)
                    Display = a.AccountCode + " — " + a.AccountName  // النص الظاهر في الكومبو
                })
                .ToList();                               // ✅ مفيش await هنا، دالة عادية

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
            var govs = await _context.Governorates
                .AsNoTracking()
                .OrderBy(g => g.GovernorateName)
                .ToListAsync();

            ViewBag.GovernorateId = new SelectList(
                govs,
                "GovernorateId",
                "GovernorateName",
                selectedGovernorateId
            );

            // قائمة الأحياء: فقط عند وجود محافظة محددة (أو للعرض في Edit)
            if (selectedGovernorateId.HasValue)
            {
                var dists = await _context.Districts
                    .AsNoTracking()
                    .Where(d => d.GovernorateId == selectedGovernorateId.Value)
                    .OrderBy(d => d.DistrictName)
                    .ToListAsync();
                ViewBag.DistrictId = new SelectList(dists, "DistrictId", "DistrictName", selectedDistrictId);
            }
            else
            {
                ViewBag.DistrictId = new SelectList(Enumerable.Empty<SelectListItem>(), "Value", "Text", selectedDistrictId);
            }

            // قائمة المناطق: فقط عند وجود حي محدد (أو للعرض في Edit)
            if (selectedDistrictId.HasValue)
            {
                var areas = await _context.Areas
                    .AsNoTracking()
                    .Where(a => a.DistrictId == selectedDistrictId.Value)
                    .OrderBy(a => a.AreaName)
                    .ToListAsync();
                ViewBag.AreaId = new SelectList(areas, "AreaId", "AreaName", selectedAreaId);
            }
            else
            {
                ViewBag.AreaId = new SelectList(Enumerable.Empty<SelectListItem>(), "Value", "Text", selectedAreaId);
            }
        }










        // =======================================================
        //  أكشن Index — قائمة العملاء / الأطراف
        // =======================================================
        [RequirePermission("Customers.Index")]
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",
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
            string? filterCol_CurrentBalance = null,
            string? filterCol_ordercontact = null,
            string? filterCol_created = null,
            string? filterCol_updated = null,
            string? filterCol_quota = null,
            int page = 1,
            int pageSize = 50
        )
        {
            var sep = new[] { '|', ',' };
            // 1) الاستعلام الأساسى + الحساب المحاسبى
            IQueryable<Customer> q = _context.Customers
                                              .Include(c => c.Account)
                                              .Include(c => c.Governorate)    // ✅ تحميل المحافظة
                                              .Include(c => c.District)       // ✅ تحميل الحي / المركز
                                              .Include(c => c.Area) 
                                             .AsNoTracking();

            // 2) تهيئة قيم البحث والترتيب
            var s = (search ?? string.Empty).Trim();
            var sb = (searchBy ?? "all").Trim().ToLowerInvariant();
            var so = (sort ?? "name").Trim().ToLowerInvariant();
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            // 3) تطبيق البحث
            if (!string.IsNullOrWhiteSpace(s))
            {
                switch (sb)
                {
                    case "id":
                        if (int.TryParse(s, out var custId))
                            q = q.Where(c => c.CustomerId == custId);
                        else
                            q = q.Where(c => c.CustomerId.ToString().Contains(s));
                        break;

                    case "name":
                        q = q.Where(c => c.CustomerName.Contains(s));
                        break;

                    case "phone":
                        q = q.Where(c =>
                            (c.Phone1 != null && c.Phone1.Contains(s)) ||
                            (c.Phone2 != null && c.Phone2.Contains(s)) ||
                            (c.Whatsapp != null && c.Whatsapp.Contains(s)));
                        break;

                    case "address":
                        q = q.Where(c => c.Address != null && c.Address.Contains(s));
                        break;

                    case "type":
                        q = q.Where(c => c.PartyCategory != null && c.PartyCategory.Contains(s));
                        break;

                    case "account":
                        q = q.Where(c =>
                            c.Account != null &&
                            (c.Account.AccountCode.Contains(s) ||
                             c.Account.AccountName.Contains(s)));
                        break;

                    case "active":
                        var yes = new[] { "1", "نعم", "yes", "true", "صح" };
                        var no = new[] { "0", "لا", "no", "false" };

                        if (yes.Contains(s, StringComparer.OrdinalIgnoreCase))
                            q = q.Where(c => c.IsActive);
                        else if (no.Contains(s, StringComparer.OrdinalIgnoreCase))
                            q = q.Where(c => !c.IsActive);
                        break;

                    case "all":
                    default:
                        q = q.Where(c =>
                            c.CustomerId.ToString().Contains(s) ||
                            c.CustomerName.Contains(s) ||
                            (c.Phone1 != null && c.Phone1.Contains(s)) ||
                            (c.Phone2 != null && c.Phone2.Contains(s)) ||
                            (c.Whatsapp != null && c.Whatsapp.Contains(s)) ||
                            (c.Address != null && c.Address.Contains(s)) ||
                            (c.PartyCategory != null && c.PartyCategory.Contains(s)) ||
                            (
                                c.Account != null &&
                                (c.Account.AccountCode.Contains(s) ||
                                 c.Account.AccountName.Contains(s))
                            )
                        );
                        break;
                }
            }

            // 4) فلتر كود من/إلى
            if (fromCode.HasValue)
                q = q.Where(c => c.CustomerId >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(c => c.CustomerId <= toCode.Value);

            // 4b) فلاتر أعمدة بنمط Excel (قيم متعددة مفصولة بـ | أو ,)
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var id) ? id : (int?)null).Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    q = q.Where(c => ids.Contains(c.CustomerId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_idExpr))
            {
                var expr = filterCol_idExpr.Trim();
                if (expr.StartsWith("<") && int.TryParse(expr.AsSpan(1).Trim(), out var maxId))
                    q = q.Where(c => c.CustomerId < maxId);
                else if (expr.StartsWith(">") && int.TryParse(expr.AsSpan(1).Trim(), out var minId))
                    q = q.Where(c => c.CustomerId > minId);
                else if (expr.Contains(":") && int.TryParse(expr.Split(':')[0].Trim(), out var fromId) && int.TryParse(expr.Split(':')[1].Trim(), out var toId))
                    q = q.Where(c => c.CustomerId >= fromId && c.CustomerId <= toId);
                else if (int.TryParse(expr, out var exactId))
                    q = q.Where(c => c.CustomerId == exactId);
            }
            if (!string.IsNullOrWhiteSpace(filterCol_name))
            {
                var vals = filterCol_name.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    q = q.Where(c => c.CustomerName != null && vals.Any(v => c.CustomerName.Contains(v)));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_type))
            {
                var vals = filterCol_type.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    q = q.Where(c => c.PartyCategory != null && vals.Contains(c.PartyCategory));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_phone))
            {
                var vals = filterCol_phone.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    q = q.Where(c => vals.Any(v => (c.Phone1 != null && c.Phone1.Contains(v)) || (c.Phone2 != null && c.Phone2.Contains(v)) || (c.Whatsapp != null && c.Whatsapp.Contains(v))));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_Address))
            {
                var vals = filterCol_Address.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    q = q.Where(c => c.Address != null && vals.Any(v => c.Address.Contains(v)));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_governorate))
            {
                var vals = filterCol_governorate.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    q = q.Where(c => c.Governorate != null && c.Governorate.GovernorateName != null && vals.Any(v => c.Governorate.GovernorateName.Contains(v)));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_district))
            {
                var vals = filterCol_district.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    q = q.Where(c => c.District != null && c.District.DistrictName != null && vals.Any(v => c.District.DistrictName.Contains(v)));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_area))
            {
                var vals = filterCol_area.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    q = q.Where(c => c.Area != null && c.Area.AreaName != null && vals.Any(v => c.Area.AreaName.Contains(v)));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_account))
            {
                var vals = filterCol_account.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    q = q.Where(c => c.Account != null && vals.Any(v => (c.Account.AccountCode != null && c.Account.AccountCode.Contains(v)) || (c.Account.AccountName != null && c.Account.AccountName.Contains(v))));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_PolicyId))
            {
                var vals = filterCol_PolicyId.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    q = q.Where(c => c.PolicyId.HasValue && vals.Contains(c.PolicyId.Value.ToString()));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_credit))
            {
                var vals = filterCol_credit.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                {
                    var decimals = vals.Select(x => decimal.TryParse(x, out var d) ? d : (decimal?)null).Where(x => x.HasValue).Select(x => x!.Value).ToList();
                    if (decimals.Count > 0)
                        q = q.Where(c => decimals.Contains(c.CreditLimit));
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_isactive))
            {
                var vals = filterCol_isactive.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                var activeList = new List<bool>();
                foreach (var v in vals)
                {
                    if (new[] { "نشط", "1", "yes", "true" }.Contains(v, StringComparer.OrdinalIgnoreCase)) activeList.Add(true);
                    else if (new[] { "موقوف", "0", "no", "false" }.Contains(v, StringComparer.OrdinalIgnoreCase)) activeList.Add(false);
                }
                if (activeList.Count > 0)
                    q = q.Where(c => activeList.Contains(c.IsActive));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_CurrentBalance))
            {
                var vals = filterCol_CurrentBalance.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                {
                    var decimals = vals.Select(x => decimal.TryParse(x, out var d) ? d : (decimal?)null).Where(x => x.HasValue).Select(x => x!.Value).ToList();
                    if (decimals.Count > 0)
                        q = q.Where(c => decimals.Contains(c.CurrentBalance));
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_ordercontact))
            {
                var vals = filterCol_ordercontact.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                    q = q.Where(c => c.OrderContactName != null && vals.Any(v => c.OrderContactName.Contains(v)));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_created))
            {
                var dateParts = filterCol_created.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                foreach (var part in dateParts)
                {
                    if (part.Length >= 7 && part.Contains("-") && int.TryParse(part.AsSpan(0, 4), out var y) && int.TryParse(part.AsSpan(5, 2), out var m))
                    {
                        var from = new DateTime(y, m, 1, 0, 0, 0);
                        var to = from.AddMonths(1).AddTicks(-1);
                        q = q.Where(c => c.CreatedAt >= from && c.CreatedAt <= to);
                        break;
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_updated))
            {
                var dateParts = filterCol_updated.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                foreach (var part in dateParts)
                {
                    if (part.Length >= 7 && part.Contains("-") && int.TryParse(part.AsSpan(0, 4), out var y) && int.TryParse(part.AsSpan(5, 2), out var m))
                    {
                        var from = new DateTime(y, m, 1, 0, 0, 0);
                        var to = from.AddMonths(1).AddTicks(-1);
                        q = q.Where(c => c.UpdatedAt >= from && c.UpdatedAt <= to);
                        break;
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_quota))
            {
                var vals = filterCol_quota.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0)
                {
                    var hasMulti = vals.Any(v => new[] { "مفعّل", "مفعّلة", "نعم", "yes", "1" }.Contains(v, StringComparer.OrdinalIgnoreCase));
                    var hasNone = vals.Any(v => new[] { "غير مفعّلة", "لا", "no", "0" }.Contains(v, StringComparer.OrdinalIgnoreCase));
                    if (hasMulti && !hasNone) q = q.Where(c => c.IsQuotaMultiplierEnabled);
                    else if (hasNone && !hasMulti) q = q.Where(c => !c.IsQuotaMultiplierEnabled);
                }
            }

            // 5) فلترة بالتاريخ (تاريخ الإنشاء)
            if (useDateRange)
            {
                if (fromDate.HasValue)
                    q = q.Where(c => c.CreatedAt >= fromDate.Value);

                if (toDate.HasValue)
                    q = q.Where(c => c.CreatedAt <= toDate.Value);
            }

            // 6) الترتيب
            q = so switch
            {
                "id" => desc ? q.OrderByDescending(c => c.CustomerId)
                             : q.OrderBy(c => c.CustomerId),

                "name" => desc ? q.OrderByDescending(c => c.CustomerName)
                               : q.OrderBy(c => c.CustomerName),

                "type" => desc ? q.OrderByDescending(c => c.PartyCategory)
                               : q.OrderBy(c => c.PartyCategory),

                "governorate" => desc ? q.OrderByDescending(c => c.Governorate != null ? c.Governorate.GovernorateName : "")
                                      : q.OrderBy(c => c.Governorate != null ? c.Governorate.GovernorateName : ""),
                "district" => desc ? q.OrderByDescending(c => c.District != null ? c.District.DistrictName : "")
                                   : q.OrderBy(c => c.District != null ? c.District.DistrictName : ""),
                "area" => desc ? q.OrderByDescending(c => c.Area != null ? c.Area.AreaName : "")
                              : q.OrderBy(c => c.Area != null ? c.Area.AreaName : ""),

                "account" => desc
                    ? q.OrderByDescending(c => c.Account != null ? c.Account.AccountCode : "")
                    : q.OrderBy(c => c.Account != null ? c.Account.AccountCode : ""),

                "isactive" => desc ? q.OrderByDescending(c => c.IsActive)
                                   : q.OrderBy(c => c.IsActive),

                "created" => desc ? q.OrderByDescending(c => c.CreatedAt)
                                  : q.OrderBy(c => c.CreatedAt),

                "updated" => desc ? q.OrderByDescending(c => c.UpdatedAt)
                                  : q.OrderBy(c => c.UpdatedAt),

                _ => desc ? q.OrderByDescending(c => c.CustomerName)
                          : q.OrderBy(c => c.CustomerName),
            };

            // 7) إجمالى الرصيد الحالى (بعد الفلاتر)
            decimal totalCurrentBalance = 0m;
            try
            {
                totalCurrentBalance = await q.SumAsync(c => c.CurrentBalance);
            }
            catch
            {
                // فى حالة وجود Null أو مشكلة فى الجمع نتجاهل الخطأ
            }

            // 8) الترقيم
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50;

            int total = await q.CountAsync();
            int pages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
            if (page > pages) page = pages;

            var items = await q.Skip((page - 1) * pageSize)
                               .Take(pageSize)
                               .ToListAsync();

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
            ViewBag.TotalCurrentBalance = totalCurrentBalance;

            ViewBag.Search = s;
            ViewBag.SearchBy = sb;
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
            ViewBag.FilterCol_CurrentBalance = filterCol_CurrentBalance;
            ViewBag.FilterCol_OrderContact = filterCol_ordercontact;
            ViewBag.FilterCol_Created = filterCol_created;
            ViewBag.FilterCol_Updated = filterCol_updated;
            ViewBag.FilterCol_Quota = filterCol_quota;

            return View(model);
        }

        /// <summary>جلب القيم المميزة لعمود (للفلترة بنمط Excel)</summary>
        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var q = _context.Customers
                .Include(c => c.Governorate).Include(c => c.District).Include(c => c.Area).Include(c => c.Account)
                .AsNoTracking();

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
                "area" => string.IsNullOrEmpty(searchTerm)
                    ? (await q.Where(c => c.Area != null).Select(c => c.Area!.AreaName).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v ?? "", v ?? "")).ToList()
                    : (await q.Where(c => c.Area != null && c.Area.AreaName != null && EF.Functions.Like(c.Area.AreaName, "%" + searchTerm + "%")).Select(c => c.Area!.AreaName).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v ?? "", v ?? "")).ToList(),
                "account" => string.IsNullOrEmpty(searchTerm)
                    ? (await q.Where(c => c.Account != null).Select(c => c.Account!.AccountCode + " — " + c.Account!.AccountName).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v, v)).ToList()
                    : (await q.Where(c => c.Account != null && (EF.Functions.Like(c.Account.AccountCode, "%" + searchTerm + "%") || EF.Functions.Like(c.Account.AccountName, "%" + searchTerm + "%"))).Select(c => c.Account!.AccountCode + " — " + c.Account!.AccountName).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v!, v)).ToList(),
                "policyid" => (await q.Where(c => c.PolicyId.HasValue).Select(c => c.PolicyId!.Value).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v.ToString(), v.ToString())).ToList(),
                "credit" => (await q.Select(c => c.CreditLimit).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v.ToString("0.00"), v.ToString("0.00"))).ToList(),
                "isactive" => new List<(string, string)> { ("نشط", "نشط"), ("موقوف", "موقوف") },
                "currentbalance" => (await q.Select(c => c.CurrentBalance).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v.ToString("0.00"), v.ToString("0.00"))).ToList(),
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






        // =================== حجم تعامل عميل / مورد ===================
        [HttpGet]
        [RequirePermission("Customers.Show")]
        public async Task<IActionResult> Show(int? id, DateTime? fromDate, DateTime? toDate)
        {
            // =========================================================
            // 1) تجهيز قائمة العملاء للأوتوكومبليت
            // =========================================================
            var customersList = await _context.Customers
                .AsNoTracking()
                .OrderBy(c => c.CustomerName)
                .Select(c => new SelectListItem
                {
                    Value = c.CustomerId.ToString(),   // متغير: كود العميل
                    Text = c.CustomerName              // متغير: اسم العميل
                })
                .ToListAsync();

            ViewBag.CustomersList = customersList;

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
            // متغير: موديل عميل جديد، نجعله نشِط افتراضيًا
            var model = new Customer
            {
                IsActive = true    // ✅ الديفولت: العميل نشِط
            };

            // استدعاء دالة تجهيز الكومبوهات بنفس ما كان موجود
            PopulateDropDowns(null);   // لا تغيّر أي شيء هنا

            // تجهيز القوائم المنسدلة: محافظة / حي / منطقة
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

                await _activityLogger.LogAsync(
                    UserActionType.Create,
                    "Customer",
                    customer.CustomerId,
                    $"إنشاء عميل جديد: {customer.CustomerName}");

                TempData["SuccessMessage"] = "تم إضافة العميل بنجاح.";
                return RedirectToAction(nameof(Index));
            }

            PopulateDropDowns(
                customer.AccountId,
                customer.PartyCategory,
                customer.PolicyId,
                customer.UserId,
                customer.RouteId
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

            var customer = await _context.Customers.FindAsync(id.Value);
            if (customer == null)
                return NotFound();

            // تجهيز بقية الكومبوهات
            PopulateDropDowns(
                customer.AccountId,
                customer.PartyCategory,
                customer.PolicyId,
                customer.UserId,
                customer.RouteId
            );

            // ✅ مهم: نمرّر القيم المختارة عشان الكومبو يعرض المحافظة/الحي/المنطقة الحالية
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

            // لو فى أخطاء فاليديشن نرجّع نفس القيم المختارة
            if (!ModelState.IsValid)
            {
                PopulateDropDowns(
                    customer.AccountId,
                    customer.PartyCategory,
                    customer.PolicyId,
                    customer.UserId,
                    customer.RouteId
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

            var oldValues = System.Text.Json.JsonSerializer.Serialize(new
            {
                existing.CustomerName,
                existing.Phone1,
                existing.Address,
                existing.PartyCategory,
                existing.CreditLimit,
                existing.IsActive
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

                var newValues = System.Text.Json.JsonSerializer.Serialize(new
                {
                    existing.CustomerName,
                    existing.Phone1,
                    existing.Address,
                    existing.PartyCategory,
                    existing.CreditLimit,
                    existing.IsActive
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

            var customer = await _context.Customers
                .Include(c => c.Account)
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.CustomerId == id.Value);

            if (customer == null)
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
            var customer = await _context.Customers.FindAsync(id);
            if (customer != null)
            {
                var oldValues = System.Text.Json.JsonSerializer.Serialize(new
                {
                    customer.CustomerName,
                    customer.Phone1,
                    customer.PartyCategory,
                    customer.CreditLimit
                });
                _context.Customers.Remove(customer);
                await _context.SaveChangesAsync();

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
                .AsNoTracking();

            var s = (search ?? string.Empty).Trim();
            var searchKey = (searchBy ?? "all").Trim().ToLowerInvariant();
            var so = (sort ?? "name").Trim().ToLowerInvariant();
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(s))
            {
                switch (searchKey)
                {
                    case "id":
                        if (int.TryParse(s, out var custId))
                            query = query.Where(c => c.CustomerId == custId);
                        else
                            query = query.Where(c => c.CustomerId.ToString().Contains(s));
                        break;

                    case "name":
                        query = query.Where(c => c.CustomerName.Contains(s));
                        break;

                    case "phone":
                        query = query.Where(c =>
                            (c.Phone1 != null && c.Phone1.Contains(s)) ||
                            (c.Phone2 != null && c.Phone2.Contains(s)) ||
                            (c.Whatsapp != null && c.Whatsapp.Contains(s)));
                        break;

                    case "address":
                        query = query.Where(c => c.Address != null && c.Address.Contains(s));
                        break;

                    case "type":
                        query = query.Where(c => c.PartyCategory != null && c.PartyCategory.Contains(s));
                        break;

                    case "account":
                        query = query.Where(c =>
                            c.Account != null &&
                            (
                                c.Account.AccountCode.Contains(s) ||
                                c.Account.AccountName.Contains(s)
                            ));
                        break;

                    case "active":
                        var yes = new[] { "1", "نعم", "yes", "true", "صح" };
                        var no = new[] { "0", "لا", "no", "false" };

                        if (yes.Contains(s, StringComparer.OrdinalIgnoreCase))
                            query = query.Where(c => c.IsActive);
                        else if (no.Contains(s, StringComparer.OrdinalIgnoreCase))
                            query = query.Where(c => !c.IsActive);
                        break;

                    case "all":
                    default:
                        query = query.Where(c =>
                            c.CustomerId.ToString().Contains(s) ||
                            c.CustomerName.Contains(s) ||
                            (c.Phone1 != null && c.Phone1.Contains(s)) ||
                            (c.Phone2 != null && c.Phone2.Contains(s)) ||
                            (c.Whatsapp != null && c.Whatsapp.Contains(s)) ||
                            (c.Address != null && c.Address.Contains(s)) ||
                            (c.PartyCategory != null && c.PartyCategory.Contains(s)) ||
                            (
                                c.Account != null &&
                                (
                                    c.Account.AccountCode.Contains(s) ||
                                    c.Account.AccountName.Contains(s)
                                )
                            )
                        );
                        break;
                }
            }

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
                    "كود العميل,اسم العميل,نوع الطرف,الهاتف,العنوان,الحساب المحاسبي,كود السياسة,كود المستخدم,اسم مسئول الطلب,هاتف مسئول الطلب,مضاعفة الكوتة,مضاعف الكوتة,الحد الائتماني,الحالة,تاريخ الإنشاء,آخر تعديل,الرصيد الحالي");

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

                    csvBuilder.AppendLine(string.Join(",",
                        c.CustomerId,
                        CsvEscape(c.CustomerName),
                        CsvEscape(c.PartyCategory ?? ""),
                        CsvEscape(phone),
                        CsvEscape(c.Address),
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
                        c.UpdatedAt.ToString("yyyy-MM-dd HH:mm"),
                        c.CurrentBalance.ToString("0.00")
                    ));
                }

                var bytes = Encoding.UTF8.GetPreamble()
                    .Concat(Encoding.UTF8.GetBytes(csvBuilder.ToString()))
                    .ToArray();

                var csvName = $"Customers_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                return File(bytes, "text/csv; charset=utf-8", csvName);
            }
            else
            {
                using var workbook = new XLWorkbook();
                var ws = workbook.Worksheets.Add("Customers");

                int row = 1;
                ws.Cell(row, 1).Value = "كود العميل";
                ws.Cell(row, 2).Value = "اسم العميل";
                ws.Cell(row, 3).Value = "نوع الطرف";
                ws.Cell(row, 4).Value = "الهاتف";
                ws.Cell(row, 5).Value = "العنوان";
                ws.Cell(row, 6).Value = "الحساب المحاسبي";
                ws.Cell(row, 7).Value = "كود السياسة";
                ws.Cell(row, 8).Value = "كود المستخدم";
                ws.Cell(row, 9).Value = "اسم مسئول الطلب";
                ws.Cell(row, 10).Value = "هاتف مسئول الطلب";
                ws.Cell(row, 11).Value = "مضاعفة الكوتة؟";
                ws.Cell(row, 12).Value = "مضاعِف الكوتة";
                ws.Cell(row, 13).Value = "الحد الائتماني";
                ws.Cell(row, 14).Value = "الحالة";
                ws.Cell(row, 15).Value = "تاريخ الإنشاء";
                ws.Cell(row, 16).Value = "آخر تعديل";
                ws.Cell(row, 17).Value = "الرصيد الحالي";

                foreach (var c in data)
                {
                    row++;
                    var phone = c.Phone1 ?? c.Phone2 ?? c.Whatsapp ?? "";
                    var account = c.Account != null
                        ? $"{c.Account.AccountCode} - {c.Account.AccountName}"
                        : "";
                    var status = c.IsActive ? "نشط" : "موقوف";
                    var quotaEnabled = c.IsQuotaMultiplierEnabled ? "نعم" : "لا";

                    ws.Cell(row, 1).Value = c.CustomerId;
                    ws.Cell(row, 2).Value = c.CustomerName;
                    ws.Cell(row, 3).Value = c.PartyCategory;
                    ws.Cell(row, 4).Value = phone;
                    ws.Cell(row, 5).Value = c.Address;
                    ws.Cell(row, 6).Value = account;
                    ws.Cell(row, 7).Value = c.PolicyId;
                    ws.Cell(row, 8).Value = c.UserId;
                    ws.Cell(row, 9).Value = c.OrderContactName;
                    ws.Cell(row, 10).Value = c.OrderContactPhone;
                    ws.Cell(row, 11).Value = quotaEnabled;
                    ws.Cell(row, 12).Value = c.QuotaMultiplier;
                    ws.Cell(row, 13).Value = c.CreditLimit;
                    ws.Cell(row, 14).Value = status;
                    ws.Cell(row, 15).Value = c.CreatedAt;
                    ws.Cell(row, 16).Value = c.UpdatedAt;
                    ws.Cell(row, 17).Value = c.CurrentBalance;
                }

                ws.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                var content = stream.ToArray();
                var fileName = $"Customers_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                return File(content,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileName);
            }
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
