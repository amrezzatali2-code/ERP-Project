using System;                                     // متغيرات التاريخ DateTime
using System.Collections.Generic;                 // Dictionary, List
using System.Globalization;                       // تنسيق التواريخ عند التصدير
using System.Linq;                                // LINQ: Where / OrderBy
using System.Linq.Expressions;                    // Expression<Func<...>>
using System.Text;                                // StringBuilder للتصدير
using System.Threading.Tasks;                     // async / await
using Microsoft.AspNetCore.Mvc;                   // Controller, IActionResult
using Microsoft.EntityFrameworkCore;              // AsNoTracking, ToListAsync
using ERP.Data;                                   // AppDbContext الاتصال بقاعدة البيانات
using ERP.Filters;
using ERP.Infrastructure;                         // PagedResult + ApplySearchSort + UserActivityLogger
using ERP.Models;                                 // User, UserActionType
using ERP.Security;
using ClosedXML.Excel;                      // لتصدير Excel
using System.IO;






namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إدارة المستخدمين (Users)
    /// - شاشة قائمة بنظام القوائم الموحد (Index).
    /// - CRUD كامل: Create / Edit / Details / Delete.
    /// - Export + BulkDelete + DeleteAll.
    /// </summary>
    public class UsersController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IUserActivityLogger _activityLogger;

        public UsersController(AppDbContext context, IUserActivityLogger activityLogger)
        {
            _context = context;
            _activityLogger = activityLogger;
        }






        // =========================================================
        // دالة خاصة: تجهيز الاستعلام الأساسي + الفلاتر + البحث + الترتيب
        // نستخدمها في Index و Export حتى لا نكرر الكود.
        // =========================================================
        private IQueryable<User> BuildQuery(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            int? fromCode,
            int? toCode)
        {
            // (1) الاستعلام الأساسي من جدول المستخدمين مع تحميل الأدوار
            //     + بدون تتبّع (قراءة فقط لتحسين الأداء)
            IQueryable<User> q = _context.Users
                .Include(u => u.UserRoles)          // تحميل أدوار المستخدم
                    .ThenInclude(ur => ur.Role)     // تحميل كائن الدور للحصول على الاسم
                .AsNoTracking();

            // (2) فلتر كود من/إلى (نعتمد هنا على UserId كرقم المستخدم)
            if (fromCode.HasValue)
                q = q.Where(u => u.UserId >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(u => u.UserId <= toCode.Value);

            // (3) فلتر التاريخ: نفلتر حسب تاريخ الإنشاء CreatedAt
            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                // نجعل النهاية حتى آخر اليوم
                DateTime from = fromDate.Value.Date;
                DateTime to = toDate.Value.Date.AddDays(1).AddTicks(-1);

                q = q.Where(u => u.CreatedAt >= from && u.CreatedAt <= to);
            }

            // (4) خرائط البحث: نحدد الأعمدة النصية والرقمية للبحث الموحد

            // الحقول النصية (string) التى يمكن البحث فيها
            var stringFields =
                new Dictionary<string, Expression<Func<User, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["username"] = u => u.UserName,       // البحث باسم الدخول
                    ["display"] = u => u.DisplayName,    // البحث بالاسم الظاهر
                };

            // الحقول الرقمية (int) التى يمكن البحث فيها
            var intFields =
                new Dictionary<string, Expression<Func<User, int>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = u => u.UserId                // البحث برقم المستخدم
                };

            // الحقول المسموح الترتيب عليها
            var orderFields =
                new Dictionary<string, Expression<Func<User, object>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["UserId"] = u => u.UserId,                      // رقم المستخدم
                    ["UserName"] = u => u.UserName,                    // اسم الدخول
                    ["DisplayName"] = u => u.DisplayName,                 // الاسم الظاهر
                    ["IsActive"] = u => u.IsActive,                    // نشط؟
                                                                       // ["IsAdmin"]  = u => u.IsAdmin,                     // ❌ تم إلغاؤه من الواجهة
                    ["CreatedAt"] = u => u.CreatedAt,                   // تاريخ الإنشاء
                    ["UpdatedAt"] = u => u.UpdatedAt ?? DateTime.MinValue // آخر تعديل
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
                defaultSearchBy: "all",       // لو المستخدم لم يحدد نوع البحث
                defaultSortBy: "UserName"     // الترتيب الافتراضي باسم الدخول
            );

            return q;
        }









        // =========================================================
        // Index — عرض قائمة المستخدمين (نظام القوائم الموحد)
        // =========================================================
        [RequirePermission(PermissionCodes.Security.Users_View)]
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",
            string? sort = "UserName",
            string? dir = "asc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,   // من كود (UserId)
            int? toCode = null,     // إلى كود
            int page = 1,
            int pageSize = 50)
        {
            // تجهيز الاستعلام مع كل الفلاتر + تحميل الأدوار
            var q = BuildQuery(
                search,
                searchBy,
                sort,
                dir,
                useDateRange,
                fromDate,
                toDate,
                fromCode,
                toCode);

            // إنشاء موديل التقسيم PagedResult
            var model = await PagedResult<User>.CreateAsync(q, page, pageSize);

            // حفظ قيم الفلاتر داخل الموديل
            model.UseDateRange = useDateRange;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            // تمرير القيم للـ ViewBag لاستخدامها في الواجهة
            ViewBag.Search = search ?? "";
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.Sort = sort ?? "UserName";
            ViewBag.Dir = (dir?.ToLower() == "asc") ? "asc" : "desc";

            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;

            ViewBag.DateField = "CreatedAt";       // نستخدم تاريخ الإنشاء للفلترة
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;

            ViewBag.TotalCount = model.TotalCount; // إجمالي عدد المستخدمين

            return View(model); // Views/Users/Index.cshtml
        }







        // =========================================================
        // Details — عرض تفاصيل مستخدم واحد
        // =========================================================
        [RequirePermission(PermissionCodes.Security.Users_View)]
        [HttpGet]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
                return NotFound();

            var user = await _context.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == id);

            if (user == null)
                return NotFound();

            return View(user);
        }








        // =========================================================
        // Create — إضافة مستخدم جديد (GET)
        // =========================================================
        [RequirePermission(PermissionCodes.Security.Users_Create)]
        [HttpGet]
        public IActionResult Create()
        {
            // فورم فارغ لإضافة مستخدم جديد
            var model = new User
            {
                IsActive = true,
                IsAdmin = false
            };

            return View(model); // Views/Users/Create.cshtml
        }








        // =========================================================
        // Create — إضافة مستخدم جديد (POST)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission(PermissionCodes.Security.Users_Create)]
        public async Task<IActionResult> Create(User model)
        {
            // ======================================================
            // 1) تجهيز اسم الدخول + الاسم الظاهر
            // ======================================================

            if (!string.IsNullOrWhiteSpace(model.UserName))
            {
                // إزالة المسافات الزائدة من اسم الدخول
                model.UserName = model.UserName.Trim();        // متغير: اسم الدخول بعد التنظيف

                // جعل الاسم الظاهر = اسم الدخول دائماً
                // حتى لو الفورم لا يحتوي على حقل DisplayName
                model.DisplayName = model.UserName;            // متغير: الاسم الظاهر الداخلي
            }
            else
            {
                // لو اسم الدخول فاضي نرجّع خطأ في الموديل
                ModelState.AddModelError("UserName", "اسم الدخول مطلوب.");
            }

            // ======================================================
            // 2) التحقق من تكرار اسم الدخول (لو الموديل سليم حتى الآن)
            // ======================================================
            if (ModelState.IsValid)
            {
                bool userExists = await _context.Users
                    .AnyAsync(u => u.UserName == model.UserName);   // متغير: هل يوجد مستخدم بنفس اسم الدخول؟

                if (userExists)
                {
                    ModelState.AddModelError("UserName", "يوجد مستخدم آخر بنفس اسم الدخول.");
                }
            }

            // لو في أخطاء نرجع لنفس الفورم
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // ======================================================
            // 3) تعيين التواريخ الافتراضية
            // ======================================================
            model.CreatedAt = DateTime.Now;   // متغير: تاريخ إنشاء المستخدم
            model.UpdatedAt = null;          // متغير: لا يوجد تعديل بعد

            // ملاحظة: حالياً نخزن PasswordHash كما هو.
            // لاحقاً نضيف دالة Hash لتشفير الباسورد قبل التخزين.

            // ======================================================
            // 4) حفظ المستخدم في قاعدة البيانات
            // ======================================================
            _context.Users.Add(model);
            await _context.SaveChangesAsync();

            await _activityLogger.LogAsync(UserActionType.Create, "User", model.UserId, $"إنشاء مستخدم: {model.UserName}");

            TempData["Success"] = "تم إضافة المستخدم بنجاح.";
            return RedirectToAction(nameof(Index));
        }












        // =========================================================
        // Edit — تعديل مستخدم (GET)
        // =========================================================
        [RequirePermission(PermissionCodes.Security.Users_Edit)]
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            // التحقق من أن رقم المستخدم صحيح
            if (id <= 0)
                return BadRequest();                   // متغير: طلب غير صالح

            // جلب بيانات المستخدم للعرض في فورم التعديل
            var user = await _context.Users
                .AsNoTracking()                        // قراءة فقط (لا نحتاج تتبّع هنا)
                .FirstOrDefaultAsync(u => u.UserId == id);

            if (user == null)
                return NotFound();                     // متغير: المستخدم غير موجود

            return View(user); // Views/Users/Edit.cshtml
        }









        // =========================================================
        // Edit — تعديل مستخدم (POST)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission(PermissionCodes.Security.Users_Edit)]
        public async Task<IActionResult> Edit(int id, User model)
        {
            // التأكد من تطابق المعرّف في الرابط مع الموديل
            if (id != model.UserId)
                return BadRequest();                  // متغير: طلب غير متطابق

            // ======================================================
            // 1) تجهيز اسم الدخول + الاسم الظاهر
            // ======================================================

            if (!string.IsNullOrWhiteSpace(model.UserName))
            {
                // إزالة المسافات الزائدة من اسم الدخول
                model.UserName = model.UserName.Trim();      // متغير: اسم الدخول بعد التنظيف

                // جعل الاسم الظاهر = اسم الدخول دائماً
                model.DisplayName = model.UserName;          // متغير: الاسم الظاهر الداخلي
            }
            else
            {
                ModelState.AddModelError("UserName", "اسم الدخول مطلوب.");
            }

            // ======================================================
            // 2) التحقق من عدم تكرار اسم الدخول مع مستخدم آخر
            // ======================================================
            if (ModelState.IsValid)
            {
                bool exists = await _context.Users
                    .AnyAsync(u =>
                        u.UserId != model.UserId &&          // متغير: نستبعد نفس المستخدم
                        u.UserName == model.UserName);       // متغير: نفس اسم الدخول الجديد؟

                if (exists)
                {
                    ModelState.AddModelError("UserName", "يوجد مستخدم آخر بنفس اسم الدخول.");
                }
            }

            // لو في أخطاء نرجع لنفس الفورم مع رسائل الخطأ
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // ======================================================
            // 3) جلب المستخدم الأصلي من قاعدة البيانات
            // ======================================================
            var dbUser = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == id);

            if (dbUser == null)
                return NotFound();                    // متغير: المستخدم لم يعد موجوداً

            var oldValues = System.Text.Json.JsonSerializer.Serialize(new { dbUser.UserName, dbUser.DisplayName, dbUser.Email, dbUser.IsActive });
            // ======================================================
            // 4) تحديث خصائص المستخدم
            // ======================================================

            dbUser.UserName = model.UserName;      // متغير: اسم الدخول
            dbUser.DisplayName = model.DisplayName;   // متغير: نحتفظ به مساويًا لاسم الدخول
            dbUser.Email = model.Email;         // متغير: البريد الإلكتروني
            dbUser.IsActive = model.IsActive;      // متغير: حالة التفعيل

            // لو عندك TextBox في الفورم لكتابة "كلمة مرور جديدة"
            // والموديل يستخدم نفس الخاصية PasswordHash مؤقتًا:
            if (!string.IsNullOrWhiteSpace(model.PasswordHash))
            {
                // حاليًا نخزنها كما هي، لاحقًا نضيف تشفير
                dbUser.PasswordHash = model.PasswordHash;    // متغير: كلمة المرور الجديدة
            }

            dbUser.UpdatedAt = DateTime.Now;         // متغير: وقت آخر تعديل

            // ======================================================
            // 5) حفظ التغييرات في قاعدة البيانات
            // ======================================================
            await _context.SaveChangesAsync();

            var newValues = System.Text.Json.JsonSerializer.Serialize(new { dbUser.UserName, dbUser.DisplayName, dbUser.Email, dbUser.IsActive });
            await _activityLogger.LogAsync(UserActionType.Edit, "User", model.UserId, $"تعديل مستخدم: {model.UserName}", oldValues, newValues);

            TempData["Success"] = "تم حفظ تعديلات المستخدم بنجاح.";
            return RedirectToAction(nameof(Index));
        }















        // =========================================================
        // Delete — تأكيد الحذف (GET)
        // =========================================================
        [RequirePermission(PermissionCodes.Security.Users_Delete)]
        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            if (id <= 0)
                return BadRequest();

            var user = await _context.Users
                                     .AsNoTracking()
                                     .FirstOrDefaultAsync(u => u.UserId == id);

            if (user == null)
                return NotFound();

            return View(user); // Views/Users/Delete.cshtml
        }












        // =========================================================
        // DeleteConfirmed — تنفيذ الحذف (POST)
        // =========================================================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [RequirePermission(PermissionCodes.Security.Users_Delete)]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                TempData["Error"] = "المستخدم غير موجود.";
                return RedirectToAction(nameof(Index));
            }

            // ممكن لاحقاً نمنع حذف الـ Admin الرئيسي هنا لو حابب.
            try
            {
                var oldValues = System.Text.Json.JsonSerializer.Serialize(new { user.UserName, user.DisplayName, user.Email });
                _context.Users.Remove(user);
                await _context.SaveChangesAsync();

                await _activityLogger.LogAsync(UserActionType.Delete, "User", id, $"حذف مستخدم: {user.UserName}", oldValues: oldValues);

                TempData["Success"] = "تم حذف المستخدم بنجاح.";
            }
            catch
            {
                TempData["Error"] = "تعذر حذف المستخدم، ربما لوجود ارتباطات أخرى.";
            }

            return RedirectToAction(nameof(Index));
        }











        // =========================================================
        // Export — تصدير قائمة المستخدمين إلى CSV (Excel)
        // =========================================================
        /// <summary>
        /// تصدير قائمة المستخدمين إلى ملف Excel
        /// بنفس فلاتر البحث الموجودة في شاشة القائمة.
        /// </summary>
        [RequirePermission(PermissionCodes.Security.Users_Export)]
        public async Task<IActionResult> Export(
       string? search,
       string? searchBy,
       string? sort,
       string? dir,
       bool useDateRange = false,
       DateTime? fromDate = null,
       DateTime? toDate = null,
       string? dateField = "CreatedAt",
       int? fromCode = null,
       int? toCode = null)
        {
            // 1) الاستعلام الأساسي من قاعدة البيانات (مع تحميل الأدوار) للقراءة فقط
            var query = _context.Users
                .Include(u => u.UserRoles)          // متغير: أدوار المستخدم
                    .ThenInclude(ur => ur.Role)     // متغير: كائن الدور للحصول على الاسم
                .AsNoTracking();

            // 2) تطبيق البحث النصي (باسم الدخول والبريد فقط)
            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();

                switch (searchBy)
                {
                    case "login":        // البحث باسم الدخول (القيمة القديمة)
                    case "username":     // البحث باسم الدخول (قيمة جديدة محتملة)
                        query = query.Where(u => u.UserName.Contains(search));
                        break;

                    case "email":        // البحث بالبريد
                        query = query.Where(u => u.Email != null && u.Email.Contains(search));
                        break;

                    default:             // البحث في الكل: اسم الدخول + البريد
                        query = query.Where(u =>
                            u.UserName.Contains(search) ||
                            (u.Email != null && u.Email.Contains(search)));
                        break;
                }
            }

            // 3) فلتر التاريخ (إن تم تفعيله من شاشة الفلترة)
            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                var from = fromDate.Value.Date;
                var to = toDate.Value.Date.AddDays(1).AddTicks(-1);

                if (string.Equals(dateField, "UpdatedAt", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(u => u.UpdatedAt >= from && u.UpdatedAt <= to);
                }
                else
                {
                    // الافتراضي: CreatedAt
                    query = query.Where(u => u.CreatedAt >= from && u.CreatedAt <= to);
                }
            }

            // 4) فلتر من كود / إلى كود
            if (fromCode.HasValue)
                query = query.Where(u => u.UserId >= fromCode.Value);

            if (toCode.HasValue)
                query = query.Where(u => u.UserId <= toCode.Value);

            // 5) الترتيب
            bool descending = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            sort = string.IsNullOrEmpty(sort) ? "UserId" : sort;

            query = sort switch
            {
                "UserName" or "login" => descending
                    ? query.OrderByDescending(u => u.UserName)
                    : query.OrderBy(u => u.UserName),

                "CreatedAt" => descending
                    ? query.OrderByDescending(u => u.CreatedAt)
                    : query.OrderBy(u => u.CreatedAt),

                "UpdatedAt" => descending
                    ? query.OrderByDescending(u => u.UpdatedAt)
                    : query.OrderBy(u => u.UpdatedAt),

                _ => descending
                    ? query.OrderByDescending(u => u.UserId)
                    : query.OrderBy(u => u.UserId),
            };

            // 6) جلب البيانات بعد تطبيق كل الفلاتر والترتيب
            var users = await query.ToListAsync();   // متغير: قائمة المستخدمين الجاهزة للتصدير

            // 7) إنشاء ملف Excel في الذاكرة باستخدام ClosedXML
            using var workbook = new XLWorkbook();            // مصنف جديد
            var worksheet = workbook.Worksheets.Add("Users"); // شيت باسم Users

            int row = 1; // رقم الصف الحالي (نبدأ بالهيدر)

            // عناوين الأعمدة (الهيدر)
            worksheet.Cell(row, 1).Value = "رقم المستخدم";
            worksheet.Cell(row, 2).Value = "اسم الدخول";
            worksheet.Cell(row, 3).Value = "الأدوار";
            worksheet.Cell(row, 4).Value = "البريد الإلكتروني";
            worksheet.Cell(row, 5).Value = "نشط؟";
            worksheet.Cell(row, 6).Value = "تاريخ الإنشاء";
            worksheet.Cell(row, 7).Value = "آخر تعديل";
            worksheet.Cell(row, 8).Value = "آخر تسجيل دخول";

            // تنسيق الهيدر (خط عريض)
            var headerRange = worksheet.Range(row, 1, row, 8);
            headerRange.Style.Font.Bold = true;

            // 8) كتابة بيانات المستخدمين سطرًا بسطر
            foreach (var u in users)
            {
                row++;

                worksheet.Cell(row, 1).Value = u.UserId;                         // رقم المستخدم
                worksheet.Cell(row, 2).Value = u.UserName;                       // اسم الدخول

                // الأدوار (نستخدم RolesSummary من الموديل)
                var rolesText = !string.IsNullOrWhiteSpace(u.RolesSummary)
                    ? u.RolesSummary
                    : "لا يوجد دور";
                worksheet.Cell(row, 3).Value = rolesText;                        // الأدوار

                worksheet.Cell(row, 4).Value = u.Email;                          // البريد
                worksheet.Cell(row, 5).Value = u.IsActive ? "نشط" : "غير نشط";  // حالة التفعيل
                worksheet.Cell(row, 6).Value = u.CreatedAt;                      // تاريخ الإنشاء
                worksheet.Cell(row, 7).Value = u.UpdatedAt;                      // آخر تعديل
                worksheet.Cell(row, 8).Value = u.LastLoginAt;                    // آخر دخول
            }

            // 9) ضبط عرض الأعمدة تلقائيًا حسب المحتوى
            worksheet.Columns().AdjustToContents();

            // 10) حفظ الملف في MemoryStream وإرجاعه للمستخدم
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            stream.Position = 0;

            var fileName = $"Users_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            const string contentType =
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

            return File(stream.ToArray(), contentType, fileName);
        }

















        // =========================================================
        // BulkDelete — حذف مجموعة من المستخدمين المحددين
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(int[] ids)
        {
            if (ids == null || ids.Length == 0)
            {
                TempData["Error"] = "لم يتم اختيار أى مستخدم للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var users = await _context.Users
                                      .Where(u => ids.Contains(u.UserId))
                                      .ToListAsync();

            if (users.Count == 0)
            {
                TempData["Error"] = "لم يتم العثور على المستخدمين المحددين.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _context.Users.RemoveRange(users);
                await _context.SaveChangesAsync();
                TempData["Success"] = $"تم حذف {users.Count} من المستخدمين المحددين.";
            }
            catch
            {
                TempData["Error"] = "لا يمكن حذف بعض المستخدمين بسبب ارتباطهم ببيانات أخرى.";
            }

            return RedirectToAction(nameof(Index));
        }














        // =========================================================
        // DeleteAll — حذف جميع المستخدمين (يفضل للبيئة التجريبية فقط)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission(PermissionCodes.Security.Users_Delete)]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _context.Users.ToListAsync();

            if (all.Count == 0)
            {
                TempData["Error"] = "لا توجد مستخدمين لحذفهم.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _context.Users.RemoveRange(all);
                await _context.SaveChangesAsync();
                TempData["Success"] = "تم حذف جميع المستخدمين.";
            }
            catch
            {
                TempData["Error"] = "لا يمكن حذف جميع المستخدمين بسبب وجود ارتباطات أخرى.";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
