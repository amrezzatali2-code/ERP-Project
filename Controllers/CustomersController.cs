using ClosedXML.Excel;                            // مكتبة Excel لإنشاء ملف xlsx
using ERP.Data;                                   // AppDbContext
using ERP.Infrastructure;                         // PagedResult
using ERP.Models;                                 // Customer
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
        // كائن الاتصال بقاعدة البيانات
        private readonly AppDbContext _context;

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

        public CustomersController(AppDbContext context)
        {
            _context = context;
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







        // دالة مساعدة: تجهيز الـ DropDowns (أنواع الأطراف + الحسابات + السياسات + المستخدمين)
        private void PopulateDropDowns(
            int? selectedAccountId = null,        // رقم الحساب المحاسبي المختار (لو في تعديل)
            string? selectedPartyCategory = null, // نوع الطرف المختار (عميل / مورد / ...)
            int? selectedPolicyId = null,         // كود سياسة العميل المختارة
            int? selectedUserId = null            // كود المستخدم المسئول (المندوب) المختار
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
        }






        // دالة مساعدة: تجهيز قوائم المحافظة / الحي / المنطقة
        private async Task FillGeoDropDownsAsync(
            int? selectedGovernorateId = null,
            int? selectedDistrictId = null,
            int? selectedAreaId = null)
        {
            // قائمة المحافظات
            var govs = await _context.Governorates
                .AsNoTracking()
                .OrderBy(g => g.GovernorateName)
                .ToListAsync();

            ViewBag.GovernorateId = new SelectList(
                govs,
                "GovernorateId",      // اسم عمود الكود في جدول المحافظات
                "GovernorateName",    // اسم عمود الاسم
                selectedGovernorateId // القيمة المختارة (لو Edit)
            );

            // قائمة الأحياء / المراكز
            var dists = await _context.Districts
                .AsNoTracking()
                .OrderBy(d => d.DistrictName)
                .ToListAsync();

            ViewBag.DistrictId = new SelectList(
                dists,
                "DistrictId",
                "DistrictName",
                selectedDistrictId
            );

            // قائمة المناطق
            var areas = await _context.Areas
                .AsNoTracking()
                .OrderBy(a => a.AreaName)
                .ToListAsync();

            ViewBag.AreaId = new SelectList(
                areas,
                "AreaId",
                "AreaName",
                selectedAreaId
            );
        }










        // =======================================================
        //  أكشن Index — قائمة العملاء / الأطراف
        // =======================================================
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
            int page = 1,
            int pageSize = 50
        )
        {
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

            return View(model);
        }








        // =================== حجم تعامل عميل / مورد ===================
        [HttpGet]
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
            // 4) قراءة بيانات العميل + الحساب المحاسبي
            // =========================================================
            var customer = await _context.Customers
                .Include(c => c.Account)
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
            // 6) إجمالي المبيعات (من قيود دفتر الأستاذ - مصدر الحقيقة)
            // ✅ مهم: نحسب من LedgerEntries وليس من SalesInvoices مباشرة
            // لأن القيود هي المصدر الحقيقي بعد الترحيل
            // =========================================================
            var salesLedgerQ = _context.LedgerEntries
                .AsNoTracking()
                .Where(e =>
                    e.CustomerId == customer.CustomerId &&
                    e.SourceType == LedgerSourceType.SalesInvoice &&
                    e.LineNo == 1 && // سطر مدين العميل فقط (ليس سطر الإيرادات)
                    e.PostVersion > 0); // فقط القيود المرحلة (ليست القيود العكسية)

            if (from.HasValue) salesLedgerQ = salesLedgerQ.Where(e => e.EntryDate >= from.Value);
            if (toExclusive.HasValue) salesLedgerQ = salesLedgerQ.Where(e => e.EntryDate < toExclusive.Value);

            // ✅ نحسب من Debit (مدين العميل) لأن هذا هو صافي الفاتورة
            decimal totalSales = await salesLedgerQ.SumAsync(e => (decimal?)e.Debit) ?? 0m;

            // =========================================================
            // 6.1) استعلام منفصل لـ SalesInvoices (لحساب عدد الفواتير وآخر تاريخ)
            // =========================================================
            var salesQ = _context.SalesInvoices
                .AsNoTracking()
                .Where(x => x.CustomerId == customer.CustomerId && x.IsPosted);

            if (from.HasValue) salesQ = salesQ.Where(x => x.SIDate >= from.Value);
            if (toExclusive.HasValue) salesQ = salesQ.Where(x => x.SIDate < toExclusive.Value);

            // =========================================================
            // 7) إجمالي المشتريات (من قيود دفتر الأستاذ - مصدر الحقيقة)
            // ✅ مهم: نحسب من LedgerEntries وليس من PurchaseInvoices مباشرة
            // لأن القيود هي المصدر الحقيقي بعد الترحيل
            // ✅ مهم: نحسب من آخر PostVersion لكل فاتورة (SourceId) لضمان عدم حساب القيود القديمة
            // =========================================================
            
            // أولاً: نجد آخر PostVersion لكل فاتورة (SourceId)
            var maxPostVersions = await _context.LedgerEntries
                .AsNoTracking()
                .Where(e =>
                    e.CustomerId == customer.CustomerId &&
                    e.SourceType == LedgerSourceType.PurchaseInvoice &&
                    e.LineNo == 2 &&
                    e.LineNo < 9000 &&
                    e.PostVersion > 0 &&
                    e.Description != null &&
                    !e.Description.Contains("عكس"))
                .GroupBy(e => e.SourceId)
                .Select(g => new { SourceId = g.Key, MaxPostVersion = g.Max(e => e.PostVersion) })
                .ToDictionaryAsync(x => x.SourceId, x => x.MaxPostVersion);

            // ثانياً: نحسب الإجمالي من القيود التي تطابق آخر PostVersion لكل فاتورة
            // ✅ مهم: يجب تحميل البيانات أولاً ثم التصفية في الذاكرة لأن Dictionary.get_Item لا يمكن ترجمته إلى SQL
            decimal totalPurchases = 0m; // متغير: إجمالي المشتريات
            var sourceIds = maxPostVersions.Keys.ToList();
            if (sourceIds.Count == 0)
            {
                totalPurchases = 0m; // ✅ إذا لم توجد فواتير مشتريات، الإجمالي = 0
            }
            else
            {
                // ✅ تحميل جميع القيود المرشحة أولاً إلى الذاكرة
                var allPurchasesEntries = await _context.LedgerEntries
                    .AsNoTracking()
                    .Where(e =>
                        e.CustomerId == customer.CustomerId &&
                        e.SourceType == LedgerSourceType.PurchaseInvoice &&
                        e.LineNo == 2 &&
                        e.LineNo < 9000 &&
                        e.PostVersion > 0 &&
                        e.Description != null &&
                        !e.Description.Contains("عكس") &&
                        sourceIds.Contains(e.SourceId))
                    .ToListAsync();

                // ✅ تصفية في الذاكرة: فقط القيود التي تطابق آخر PostVersion لكل فاتورة
                var filteredEntries = allPurchasesEntries
                    .Where(e =>
                        maxPostVersions.ContainsKey(e.SourceId) &&
                        maxPostVersions[e.SourceId] == e.PostVersion)
                    .ToList();

                // ✅ تطبيق فلتر التاريخ (إن وجد)
                if (from.HasValue) filteredEntries = filteredEntries.Where(e => e.EntryDate >= from.Value).ToList();
                if (toExclusive.HasValue) filteredEntries = filteredEntries.Where(e => e.EntryDate < toExclusive.Value).ToList();

                // ✅ نحسب من Credit (دائن المورد) لأن هذا هو صافي الفاتورة
                totalPurchases = filteredEntries.Sum(e => e.Credit);
            }

            // =========================================================
            // 7.1) استعلام منفصل لـ PurchaseInvoices (لحساب عدد الفواتير وآخر تاريخ)
            // =========================================================
            var purchasesQ = _context.PurchaseInvoices
                .AsNoTracking()
                .Where(x => x.CustomerId == customer.CustomerId && x.IsPosted);

            if (from.HasValue) purchasesQ = purchasesQ.Where(x => x.PIDate >= from.Value);
            if (toExclusive.HasValue) purchasesQ = purchasesQ.Where(x => x.PIDate < toExclusive.Value);

            // =========================================================
            // 8) إجمالي المرتجعات
            // تعليق: مؤقتًا = 0 لحد ما تربط SalesReturns / PurchaseReturns
            // =========================================================
            decimal totalReturns = 0m;

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

            DateTime? lastTransactionDate =
                (lastSalesDate == null && lastPurchaseDate == null) ? null :
                (lastSalesDate == null) ? lastPurchaseDate :
                (lastPurchaseDate == null) ? lastSalesDate :
                (lastSalesDate > lastPurchaseDate ? lastSalesDate : lastPurchaseDate);

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
            // 11) تحميل قوائم الفواتير للعرض في الجداول
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

            // =========================================================
            // 11.1) تحميل أسماء المخازن للعرض (اختياري - يمكن عرض WarehouseId فقط)
            // =========================================================
            var warehouseIds = salesInvoicesList.Select(s => s.WarehouseId)
                .Union(purchaseInvoicesList.Select(p => p.WarehouseId))
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
            ViewBag.TotalReturns = totalReturns;
            ViewBag.InvoiceCount = invoiceCount;
            ViewBag.LastTransactionDate = lastTransactionDate;
            ViewBag.SalesInvoicesList = salesInvoicesList;
            ViewBag.PurchaseInvoicesList = purchaseInvoicesList;
            ViewBag.Warehouses = warehouses;

            return View(customer);
        }











        // ======================================================
        // GET: Customers/Create
        // ======================================================
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

                TempData["SuccessMessage"] = "تم إضافة العميل بنجاح.";
                return RedirectToAction(nameof(Index));
            }

            PopulateDropDowns(
                customer.AccountId,
                customer.PartyCategory,
                customer.PolicyId,
                customer.UserId
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
                customer.UserId
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
                    customer.UserId
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

                // ===== السياسة والكوتة =====
                existing.PolicyId = customer.PolicyId;
                existing.UserId = customer.UserId;
                existing.OrderContactName = customer.OrderContactName;
                existing.OrderContactPhone = customer.OrderContactPhone;
                existing.IsQuotaMultiplierEnabled = customer.IsQuotaMultiplierEnabled;
                existing.QuotaMultiplier = customer.QuotaMultiplier;

                existing.UpdatedAt = DateTime.Now;

                await _context.SaveChangesAsync();
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
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var customer = await _context.Customers.FindAsync(id);
            if (customer != null)
            {
                _context.Customers.Remove(customer);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم حذف العميل بنجاح.";
            }

            return RedirectToAction(nameof(Index));
        }

        // ===================== DeleteAll =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
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
