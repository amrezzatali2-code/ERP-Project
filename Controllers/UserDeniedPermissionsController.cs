using System;                                     // متغيرات التاريخ DateTime
using System.Collections.Generic;                 // القوائم List
using System.Linq;                                // أوامر LINQ مثل Where و OrderBy
using System.Text;                                // StringBuilder لتصدير CSV
using System.Threading.Tasks;                     // Task و async

using ERP.Data;                                   // AppDbContext
using ERP.Filters;
using ERP.Infrastructure;                         // PagedResult
using ERP.Models;                                 // الموديلات UserDeniedPermission, User, Permission
using ERP.Security;

using Microsoft.AspNetCore.Mvc;                   // أساس الكنترولر
using Microsoft.AspNetCore.Mvc.Rendering;         // SelectList
using Microsoft.EntityFrameworkCore;              // Include, AsNoTracking, ToListAsync
using ClosedXML.Excel;                      // لتصدير Excel
using System.IO;



namespace ERP.Controllers
{
    /// <summary>
    /// إدارة استثناءات الصلاحيات للمستخدمين:
    /// السماح/المنع لصلاحية معينة لمستخدم معيّن فوق الأدوار العادية.
    /// </summary>
    public class UserDeniedPermissionsController : Controller
    {
        private readonly AppDbContext _context;   // الاتصال بقاعدة البيانات

        public UserDeniedPermissionsController(AppDbContext context)
        {
            _context = context;
        }

        // ========= دوال مساعدة: فلاتر + قوائم منسدلة =========

        /// <summary>
        /// تطبيق البحث + فلترة الكود + التاريخ على استعلام UserDeniedPermission.
        /// تُستخدم في Index و Export.
        /// </summary>
        private IQueryable<UserDeniedPermission> ApplyFilters(
            IQueryable<UserDeniedPermission> query,
            string? search,
            string? searchBy,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            int? fromCode,
            int? toCode)
        {
            // فلتر من كود / إلى كود على Id
            if (fromCode.HasValue)
            {
                query = query.Where(x => x.Id >= fromCode.Value);
            }

            if (toCode.HasValue)
            {
                query = query.Where(x => x.Id <= toCode.Value);
            }

            // فلتر التاريخ على CreatedAt
            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                query = query.Where(x => x.CreatedAt >= fromDate.Value &&
                                         x.CreatedAt <= toDate.Value);
            }

            // البحث النصي
            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = search.Trim();

                switch ((searchBy ?? "all").ToLower())
                {
                    case "userid":
                        if (int.TryParse(term, out int userId))
                            query = query.Where(x => x.UserId == userId);
                        else
                            query = query.Where(x => false);
                        break;

                    case "permissionid":
                        if (int.TryParse(term, out int permId))
                            query = query.Where(x => x.PermissionId == permId);
                        else
                            query = query.Where(x => false);
                        break;

                    case "username":
                        query = query.Where(x =>
                            x.User.UserName.Contains(term) ||
                            (x.User.DisplayName ?? "").Contains(term));
                        break;

                    case "permission":
                        query = query.Where(x =>
                            x.Permission.Code.Contains(term) ||
                            (x.Permission.NameAr ?? "").Contains(term));
                        break;

                    case "id":
                        if (int.TryParse(term, out int idVal))
                            query = query.Where(x => x.Id == idVal);
                        else
                            query = query.Where(x => false);
                        break;

                    default: // all
                        query = query.Where(x =>
                            x.Id.ToString().Contains(term) ||
                            x.UserId.ToString().Contains(term) ||
                            x.PermissionId.ToString().Contains(term) ||
                            x.User.UserName.Contains(term) ||
                            (x.User.DisplayName ?? "").Contains(term) ||
                            x.Permission.Code.Contains(term) ||
                            (x.Permission.NameAr ?? "").Contains(term));
                        break;
                }
            }

            return query;
        }








        /// <summary>
        /// تجهيز القوائم المنسدلة (المستخدمين + الصلاحيات) للفورمات.
        /// </summary>
        private async Task PopulateLookupsAsync(int? selectedUserId = null, int? selectedPermissionId = null)
        {
            // قائمة المستخدمين
            var userOptions = await _context.Users
                .OrderBy(u => u.DisplayName ?? u.UserName)
                .Select(u => new
                {
                    u.UserId,
                    Text = (u.DisplayName ?? u.UserName) + " (" + u.UserName + ")"
                })
                .ToListAsync();

            // قائمة الصلاحيات
            var permOptions = await _context.Permissions
                .OrderBy(p => p.Module)
                .ThenBy(p => p.NameAr)
                .Select(p => new
                {
                    p.PermissionId,
                    Text = (p.Module != null && p.Module != ""
                            ? "[" + p.Module + "] "
                            : "") + (p.NameAr ?? "") + " (" + p.Code + ")"
                })
                .ToListAsync();

            ViewBag.UserId = new SelectList(userOptions, "UserId", "Text", selectedUserId);
            ViewBag.PermissionId = new SelectList(permOptions, "PermissionId", "Text", selectedPermissionId);
        }








        // ========= INDEX =========

        /// <summary>
        /// قائمة استثناءات الصلاحيات للمستخدمين
        /// مع بحث + ترتيب + تقسيم صفحات + فلاتر، بنظام القوائم الموحد.
        /// </summary>
        [RequirePermission(PermissionCodes.Security.UserDeniedPermissions_View)]
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            int page = 1,
            int pageSize = 25,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? dateField = "CreatedAt",
            int? fromCode = null,
            int? toCode = null)
        {
            // استعلام أساسي مع Include على المستخدم والصلاحية
            IQueryable<UserDeniedPermission> query = _context.UserDeniedPermissions
                .AsNoTracking()
                .Include(x => x.User)
                .Include(x => x.Permission);

            // تطبيق الفلاتر
            query = ApplyFilters(query, search, searchBy, useDateRange, fromDate, toDate, fromCode, toCode);

            // الترتيب
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            sort ??= "Id";

            query = sort switch
            {
                "UserId" => desc
                    ? query.OrderByDescending(x => x.UserId)
                    : query.OrderBy(x => x.UserId),

                "PermissionId" => desc
                    ? query.OrderByDescending(x => x.PermissionId)
                    : query.OrderBy(x => x.PermissionId),

                "UserName" => desc
                    ? query.OrderByDescending(x => x.User.DisplayName ?? x.User.UserName)
                    : query.OrderBy(x => x.User.DisplayName ?? x.User.UserName),

                "PermissionCode" => desc
                    ? query.OrderByDescending(x => x.Permission.Code)
                    : query.OrderBy(x => x.Permission.Code),

                "IsAllowed" => desc
                    ? query.OrderByDescending(x => x.IsAllowed)
                    : query.OrderBy(x => x.IsAllowed),

                "CreatedAt" => desc
                    ? query.OrderByDescending(x => x.CreatedAt)
                    : query.OrderBy(x => x.CreatedAt),

                _ => desc
                    ? query.OrderByDescending(x => x.Id)
                    : query.OrderBy(x => x.Id)
            };

            // الترقيم
            int totalCount = await query.CountAsync();
            pageSize = Math.Max(1, pageSize);
            page = Math.Max(1, page);

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var model = new PagedResult<UserDeniedPermission>(items, totalCount, page, pageSize)
            {
                Search = search,
                SortColumn = sort,
                SortDescending = desc,
                UseDateRange = useDateRange,
                FromDate = fromDate,
                ToDate = toDate
            };

            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.DateField = dateField ?? "CreatedAt";

            return View(model);
        }










        // ========= DETAILS =========

        /// <summary>
        /// عرض تفاصيل استثناء صلاحية واحد.
        /// </summary>
        public async Task<IActionResult> Details(int id)
        {
            var row = await _context.UserDeniedPermissions
                .AsNoTracking()
                .Include(x => x.User)
                .Include(x => x.Permission)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (row == null)
                return NotFound();

            return View(row);
        }








        // ========= CREATE =========

        // GET: UserDeniedPermissions/Create
        public async Task<IActionResult> Create(int? userId)
        {
            // تحميل قائمة المستخدمين
            await PopulateUsersAsync(userId);

            // اسم الدور الحالى
            string? roleName = null;

            // صلاحيات الدور (RolePermissions) هنبعتها فى ViewBag
            var permissions = new List<RolePermission>();

            // الصلاحيات المستثناة فعليًا للمستخدم (UserDeniedPermissions)
            var deniedIds = new List<int>();

            if (userId.HasValue && userId.Value > 0)
            {
                // جلب الدور الأساسى للمستخدم (أو أول دور لو مفيش IsPrimary)
                var userRole = await _context.UserRoles
                    .Include(ur => ur.Role)
                    .Where(ur => ur.UserId == userId.Value)
                    .OrderByDescending(ur => ur.IsPrimary)
                    .FirstOrDefaultAsync();

                if (userRole != null)
                {
                    roleName = userRole.Role?.Name;

                    // صلاحيات الدور المسموحة
                    permissions = await _context.RolePermissions
                        .Include(rp => rp.Permission)
                        .Where(rp => rp.RoleId == userRole.RoleId && rp.IsAllowed)
                        .OrderBy(rp => rp.Permission.Module)
                        .ThenBy(rp => rp.Permission.NameAr)
                        .ToListAsync();

                    // الاستثناءات الحالية لهذا المستخدم
                    deniedIds = await _context.UserDeniedPermissions
                        .Where(x => x.UserId == userId.Value)
                        .Select(x => x.PermissionId)
                        .ToListAsync();
                }
            }

            ViewBag.RoleName = roleName ?? "لا يوجد دور محدد";
            ViewBag.Permissions = permissions;
            ViewBag.DeniedIds = deniedIds;

            // نستخدم نفس الموديل القديم لكن فعليًا يهمنا UserId فقط
            var model = new UserDeniedPermission
            {
                UserId = userId ?? 0
            };

            return View(model);
        }





        // POST: UserDeniedPermissions/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            int UserId,                  // من الـ <select name="UserId">
            int[] permissionIds,         // كل صلاحية ظهرت فى الجدول
            int[] allowedPermissionIds)  // الصلاحيات اللى تسيبت متعلَّم عليها
        {
            // تحقق من اختيار المستخدم
            if (UserId <= 0)
            {
                ModelState.AddModelError("UserId", "برجاء اختيار مستخدم.");
            }

            // فى حالة ما جاش أى مصفوفة من الفورم
            permissionIds ??= Array.Empty<int>();
            allowedPermissionIds ??= Array.Empty<int>();

            if (!ModelState.IsValid)
            {
                // لو فى خطأ نرجع لنفس الشاشة مع تحميل البيانات
                return await Create(UserId);   // نستدعى GET Create(userId)
            }

            // مجموعة الصلاحيات المسموحة (المتعلمة فى الفورم)
            var allowedSet = new HashSet<int>(allowedPermissionIds);

            // الصلاحيات اللى تم إلغاء الصح منها = استثناءات (منع)
            var toDeny = permissionIds
                .Where(id => !allowedSet.Contains(id))
                .Distinct()
                .ToList();

            // الاستثناءات الحالية فى الجدول للمستخدم
            var existing = await _context.UserDeniedPermissions
                .Where(x => x.UserId == UserId)
                .ToListAsync();

            // 1) إضافة استثناءات جديدة
            foreach (var permId in toDeny)
            {
                bool alreadyDenied = existing.Any(x => x.PermissionId == permId);
                if (!alreadyDenied)
                {
                    var row = new UserDeniedPermission
                    {
                        UserId = UserId,
                        PermissionId = permId,
                        IsAllowed = false   // هذا السطر يعنى "منع" هذه الصلاحية
                    };

                    _context.UserDeniedPermissions.Add(row);
                }
            }

            // 2) إزالة الاستثناءات التى رجعنا لها الصح
            var toRemove = existing
                .Where(x => !toDeny.Contains(x.PermissionId))
                .ToList();

            if (toRemove.Any())
            {
                _context.UserDeniedPermissions.RemoveRange(toRemove);
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = "تم حفظ استثناءات صلاحيات المستخدم.";
            return RedirectToAction(nameof(Index));
        }






        // دالة مساعدة لملء قائمة المستخدمين فى الكومبو
        private async Task PopulateUsersAsync(int? selectedUserId = null)
        {
            var users = await _context.Users
                .OrderBy(u => u.UserName)          // الترتيب باسم الدخول
                .Select(u => new
                {
                    u.UserId,
                    // لو عندك DisplayName أو Name بدل UserName استبدلها هنا
                    Text = u.UserName               // النص الظاهر فى الكومبو
                })
                .ToListAsync();

            ViewBag.UserId = new SelectList(users, "UserId", "Text", selectedUserId);
        }







        // ========= EDIT =========

        public async Task<IActionResult> Edit(int id)
        {
            var item = await _context.UserDeniedPermissions
                .Include(x => x.User)
                .Include(x => x.Permission)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (item == null)
                return NotFound();

            await PopulateLookupsAsync(item.UserId, item.PermissionId);
            return View(item);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            int id,
            [Bind("Id,UserId,PermissionId,IsAllowed,CreatedAt")] UserDeniedPermission item)
        {
            if (id != item.Id)
                return NotFound();

            // التحقق من عدم التكرار مع سطر آخر
            bool exists = await _context.UserDeniedPermissions
                .AnyAsync(x => x.Id != item.Id &&
                               x.UserId == item.UserId &&
                               x.PermissionId == item.PermissionId);

            if (exists)
            {
                ModelState.AddModelError(string.Empty, "يوجد استثناء آخر لنفس المستخدم ونفس الصلاحية.");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(item);
                    await _context.SaveChangesAsync();

                    TempData["Success"] = "تم تعديل استثناء الصلاحية بنجاح.";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!UserDeniedPermissionExists(item.Id))
                        return NotFound();
                    else
                        throw;
                }

                return RedirectToAction(nameof(Index));
            }

            await PopulateLookupsAsync(item.UserId, item.PermissionId);
            return View(item);
        }

        // ========= DELETE =========

        public async Task<IActionResult> Delete(int id)
        {
            var row = await _context.UserDeniedPermissions
                .AsNoTracking()
                .Include(x => x.User)
                .Include(x => x.Permission)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (row == null)
                return NotFound();

            return View(row);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var row = await _context.UserDeniedPermissions.FindAsync(id);
            if (row != null)
            {
                _context.UserDeniedPermissions.Remove(row);
                await _context.SaveChangesAsync();
                TempData["Success"] = "تم حذف استثناء الصلاحية.";
            }

            return RedirectToAction(nameof(Index));
        }

        // ========= BULK DELETE =========

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete([FromForm] int[] ids)
        {
            if (ids == null || ids.Length == 0)
            {
                TempData["Error"] = "من فضلك اختر على الأقل استثناء واحد للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var rows = await _context.UserDeniedPermissions
                .Where(x => ids.Contains(x.Id))
                .ToListAsync();

            if (rows.Count == 0)
            {
                TempData["Error"] = "لم يتم العثور على السطور المحددة.";
                return RedirectToAction(nameof(Index));
            }

            _context.UserDeniedPermissions.RemoveRange(rows);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم حذف {rows.Count} استثناء صلاحية.";
            return RedirectToAction(nameof(Index));
        }

        // ========= DELETE ALL =========

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _context.UserDeniedPermissions.ToListAsync();
            _context.UserDeniedPermissions.RemoveRange(all);
            await _context.SaveChangesAsync();

            TempData["Success"] = "تم حذف جميع استثناءات الصلاحيات.";
            return RedirectToAction(nameof(Index));
        }









        // ========= EXPORT =========

        [HttpGet]
        public async Task<IActionResult> Export(
        string? search,
        string? searchBy,
        string? sort,
        string? dir,
        bool useDateRange = false,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int? fromCode = null,
        int? toCode = null,
        string format = "excel")
        {
            // 1) الاستعلام الأساسي: استثناءات الصلاحيات + المستخدم + الصلاحية
            IQueryable<UserDeniedPermission> query = _context.UserDeniedPermissions
                .AsNoTracking()
                .Include(x => x.User)
                .Include(x => x.Permission);

            // 2) تطبيق نفس الفلاتر المستخدمة في Index
            query = ApplyFilters(
                query,
                search,
                searchBy,
                useDateRange,
                fromDate,
                toDate,
                fromCode,
                toCode);

            // 3) ترتيب ثابت حسب Id
            var list = await query
                .OrderBy(x => x.Id)
                .ToListAsync();

            // نتأكد من قيمة format
            format = (format ?? "excel").ToLowerInvariant();

            // ================= فرع Excel =================
            if (format == "excel")
            {
                // تأكد أن عندك:
                // using ClosedXML.Excel;
                // using System.IO;
                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("UserDeniedPermissions");

                int row = 1;

                // عناوين الأعمدة بالعربي
                worksheet.Cell(row, 1).Value = "رقم السطر";
                worksheet.Cell(row, 2).Value = "رقم المستخدم";
                worksheet.Cell(row, 3).Value = "اسم الدخول";
                worksheet.Cell(row, 4).Value = "رقم الصلاحية";
                worksheet.Cell(row, 5).Value = "كود الصلاحية";
                worksheet.Cell(row, 6).Value = "اسم الصلاحية";
                worksheet.Cell(row, 7).Value = "الحالة (Allow/Deny)";
                worksheet.Cell(row, 8).Value = "تاريخ الإضافة";

                var headerRange = worksheet.Range(row, 1, row, 8);
                headerRange.Style.Font.Bold = true;

                // البيانات
                foreach (var x in list)
                {
                    row++;

                    string userName = x.User?.UserName ?? string.Empty;         // اسم الدخول فقط
                    string permCode = x.Permission?.Code ?? string.Empty;
                    string permName = x.Permission?.NameAr ?? string.Empty;
                    string allowed = x.IsAllowed ? "Allow" : "Deny";

                    worksheet.Cell(row, 1).Value = x.Id;
                    worksheet.Cell(row, 2).Value = x.UserId;
                    worksheet.Cell(row, 3).Value = userName;
                    worksheet.Cell(row, 4).Value = x.PermissionId;
                    worksheet.Cell(row, 5).Value = permCode;
                    worksheet.Cell(row, 6).Value = permName;
                    worksheet.Cell(row, 7).Value = allowed;
                    worksheet.Cell(row, 8).Value = x.CreatedAt;
                }

                worksheet.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                stream.Position = 0;

                var fileNameExcel = $"UserDeniedPermissions_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                const string excelContentType =
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

                return File(stream.ToArray(), excelContentType, fileNameExcel);
            }

            // ================= فرع CSV =================
            var sb = new StringBuilder();
            sb.AppendLine("Id,UserId,UserName,PermissionId,PermissionCode,PermissionName,IsAllowed,CreatedAt");

            foreach (var x in list)
            {
                string userName = (x.User?.UserName ?? string.Empty)
                    .Replace("\"", "\"\"");                        // اسم الدخول
                string permCode = (x.Permission?.Code ?? string.Empty)
                    .Replace("\"", "\"\"");
                string permName = (x.Permission?.NameAr ?? string.Empty)
                    .Replace("\"", "\"\"");
                string allowed = x.IsAllowed ? "Allow" : "Deny";
                string created = x.CreatedAt.ToString("yyyy-MM-dd HH:mm");

                sb.AppendLine(
                    $"{x.Id}," +
                    $"{x.UserId}," +
                    $"\"{userName}\"," +
                    $"{x.PermissionId}," +
                    $"\"{permCode}\"," +
                    $"\"{permName}\"," +
                    $"{allowed}," +
                    $"{created}");
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileNameCsv = $"UserDeniedPermissions_{timeStamp}.csv";

            return File(bytes, "text/csv", fileNameCsv);
        }








        // ========= دالة مساعدة =========

        private bool UserDeniedPermissionExists(int id)
        {
            return _context.UserDeniedPermissions.Any(e => e.Id == id);
        }
    }
}
