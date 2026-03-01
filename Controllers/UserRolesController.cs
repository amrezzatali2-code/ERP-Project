using System;                                     // متغيرات التاريخ DateTime
using System.Collections.Generic;                 // القوائم List
using System.Linq;                                // أوامر LINQ
using System.Text;                                // StringBuilder للتصدير
using System.Threading.Tasks;                     // Task و async
using ERP.Data;                                   // AppDbContext
using ERP.Filters;
using ERP.Infrastructure;                         // PagedResult لتقسيم الصفحات
using ERP.Models;                                 // الموديلات UserRole, User, Role
using ERP.Security;
using ERP.ViewModels;                             // RolePermissionEditItem
using Microsoft.AspNetCore.Mvc;                   // أساس الكنترولر
using Microsoft.AspNetCore.Mvc.Rendering;         // SelectList
using Microsoft.EntityFrameworkCore;              // Include, AsNoTracking, ToListAsync
using ClosedXML.Excel;                      // لتصدير Excel
using System.IO;

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إدارة ربط المستخدمين بالأدوار (UserRoles).
    /// كل سطر = دور واحد لمستخدم معيّن.
    /// </summary>
    public class UserRolesController : Controller
    {
        private readonly AppDbContext _context;   // كائن الاتصال بقاعدة البيانات

        public UserRolesController(AppDbContext context)
        {
            _context = context;
        }







        // ========= دوال مساعدة: الفلاتر + القوائم المنسدلة =========

        /// <summary>
        /// تطبيق البحث + فلتر الكود + فلتر التاريخ على استعلام UserRole.
        /// التاريخ هنا على حقل AssignedAt (تاريخ إسناد الدور للمستخدم).
        /// </summary>
        private IQueryable<UserRole> ApplyFilters(
            IQueryable<UserRole> query,
            string? search,
            string? searchBy,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            int? fromCode,
            int? toCode)
        {
            // فلتر من كود / إلى كود على المعرّف Id
            if (fromCode.HasValue)
            {
                query = query.Where(x => x.Id >= fromCode.Value);
            }

            if (toCode.HasValue)
            {
                query = query.Where(x => x.Id <= toCode.Value);
            }

            // فلترة بالتاريخ على تاريخ الإسناد
            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                query = query.Where(x =>
                    x.AssignedAt >= fromDate.Value &&
                    x.AssignedAt <= toDate.Value);
            }

            // البحث النصي
            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = search.Trim();
                string mode = (searchBy ?? "all").ToLower();

                switch (mode)
                {
                    case "userid":
                        if (int.TryParse(term, out int uid))
                            query = query.Where(x => x.UserId == uid);
                        else
                            query = query.Where(x => false);
                        break;

                    case "username":
                        query = query.Where(x =>
                            x.User.UserName.Contains(term));
                        break;

                    case "display":
                        query = query.Where(x =>
                            (x.User.DisplayName ?? "").Contains(term));
                        break;

                    case "roleid":
                        if (int.TryParse(term, out int rid))
                            query = query.Where(x => x.RoleId == rid);
                        else
                            query = query.Where(x => false);
                        break;

                    case "rolename":
                    case "role":
                        query = query.Where(x =>
                            x.Role.Name.Contains(term) ||
                            (x.Role.Description ?? "").Contains(term));
                        break;

                    case "primary":
                        // البحث عن الأدوار الافتراضية فقط
                        query = query.Where(x => x.IsPrimary);
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
                            x.RoleId.ToString().Contains(term) ||
                            x.User.UserName.Contains(term) ||
                            (x.User.DisplayName ?? "").Contains(term) ||
                            x.Role.Name.Contains(term) ||
                            (x.Role.Description ?? "").Contains(term));
                        break;
                }
            }

            return query;
        }








        /// <summary>
        /// تجهيز القوائم المنسدلة للمستخدمين والأدوار للفورمات.
        /// </summary>
        private async Task PopulateLookupsAsync(int? selectedUserId = null, int? selectedRoleId = null)
        {
            // قائمة المستخدمين (اسم واحد بدون تكرار)
            var users = await _context.Users
                .OrderBy(u => u.UserName)
                .Select(u => new
                {
                    u.UserId,
                    Text = u.DisplayName ?? u.UserName
                })
                .ToListAsync();

            // قائمة الأدوار
            var roles = await _context.Roles
                .OrderBy(r => r.Name)
                .Select(r => new
                {
                    r.RoleId,
                    Text = r.Name + " (" + r.RoleId + ")"
                })
                .ToListAsync();

            ViewBag.UserId = new SelectList(users, "UserId", "Text", selectedUserId);
            ViewBag.RoleId = new SelectList(roles, "RoleId", "Text", selectedRoleId);
        }








        // ========= INDEX =========

        /// <summary>
        /// قائمة ربط المستخدمين بالأدوار مع:
        /// بحث + ترتيب + تقسيم صفحات + فلاتر، بنظام القوائم الموحد.
        /// </summary>
        [RequirePermission(PermissionCodes.Security.UserRoles_View)]
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
            string? dateField = "AssignedAt",
            int? fromCode = null,
            int? toCode = null)
        {
            // استعلام أساسي مع Include على المستخدم والدور
            IQueryable<UserRole> query = _context.UserRoles
                .AsNoTracking()
                .Include(x => x.User)
                .Include(x => x.Role);

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

                "UserName" => desc
                    ? query.OrderByDescending(x => x.User.UserName)
                    : query.OrderBy(x => x.User.UserName),

                "RoleId" => desc
                    ? query.OrderByDescending(x => x.RoleId)
                    : query.OrderBy(x => x.RoleId),

                "RoleName" => desc
                    ? query.OrderByDescending(x => x.Role.Name)
                    : query.OrderBy(x => x.Role.Name),

                "IsPrimary" => desc
                    ? query.OrderByDescending(x => x.IsPrimary)
                    : query.OrderBy(x => x.IsPrimary),

                "AssignedAt" => desc
                    ? query.OrderByDescending(x => x.AssignedAt)
                    : query.OrderBy(x => x.AssignedAt),

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

            var model = new PagedResult<UserRole>(items, totalCount, page, pageSize)
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
            ViewBag.DateField = dateField ?? "AssignedAt";

            return View(model);
        }









        // ========= DETAILS =========

        /// <summary>
        /// عرض تفاصيل ربط مستخدم بدور معيّن.
        /// </summary>
        public async Task<IActionResult> Details(int id)
        {
            var item = await _context.UserRoles
                .AsNoTracking()
                .Include(x => x.User)
                .Include(x => x.Role)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (item == null)
                return NotFound();

            return View(item);
        }








        // ========= CREATE =========

        // GET: UserRoles/Create
        public async Task<IActionResult> Create()
        {
            // تحميل قائمة المستخدمين والأدوار للكومبو بوكس
            await PopulateLookupsAsync();
            return View();
        }

        // POST: UserRoles/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            [Bind("UserId,RoleId,IsPrimary")] UserRole item,
            int[]? selectedPermissionIds)
        {
            ModelState.Remove(nameof(UserRole.User));
            ModelState.Remove(nameof(UserRole.Role));

            if (item.UserId <= 0)
                ModelState.AddModelError("UserId", "من فضلك اختر مستخدم.");
            if (item.RoleId <= 0)
                ModelState.AddModelError("RoleId", "من فضلك اختر دور.");

            bool exists = await _context.UserRoles
                .AnyAsync(x => x.UserId == item.UserId && x.RoleId == item.RoleId);
            if (exists)
                ModelState.AddModelError(string.Empty, "هذا المستخدم لديه بالفعل نفس الدور.");

            if (!ModelState.IsValid)
            {
                await PopulateLookupsAsync(item.UserId, item.RoleId);
                return View(item);
            }

            item.AssignedAt = DateTime.UtcNow;
            _context.Add(item);
            await _context.SaveChangesAsync();

            // حفظ استثناءات الصلاحيات (تعديلات المستخدم)
            var allowed = new HashSet<int>(selectedPermissionIds ?? Array.Empty<int>());
            var rolePermIds = await _context.RolePermissions
                .Where(rp => rp.RoleId == item.RoleId && rp.IsAllowed)
                .Select(rp => rp.PermissionId)
                .ToListAsync();

            foreach (var permId in rolePermIds.Where(id => !allowed.Contains(id)))
            {
                if (!await _context.UserDeniedPermissions.AnyAsync(x => x.UserId == item.UserId && x.PermissionId == permId))
                {
                    _context.UserDeniedPermissions.Add(new UserDeniedPermission
                    {
                        UserId = item.UserId,
                        PermissionId = permId,
                        IsAllowed = false
                    });
                }
            }
            var toRemoveDenied = await _context.UserDeniedPermissions
                .Where(x => x.UserId == item.UserId && rolePermIds.Contains(x.PermissionId) && allowed.Contains(x.PermissionId))
                .ToListAsync();
            _context.UserDeniedPermissions.RemoveRange(toRemoveDenied);

            var allPermIds = await _context.Permissions.Where(p => p.IsActive).Select(p => p.PermissionId).ToListAsync();
            foreach (var permId in allPermIds.Where(id => allowed.Contains(id) && !rolePermIds.Contains(id)))
            {
                if (!await _context.UserExtraPermissions.AnyAsync(x => x.UserId == item.UserId && x.PermissionId == permId))
                {
                    _context.UserExtraPermissions.Add(new UserExtraPermissions
                    {
                        UserId = item.UserId,
                        PermissionId = permId
                    });
                }
            }
            var toRemoveExtra = await _context.UserExtraPermissions
                .Where(x => x.UserId == item.UserId && !allowed.Contains(x.PermissionId) && !rolePermIds.Contains(x.PermissionId))
                .ToListAsync();
            _context.UserExtraPermissions.RemoveRange(toRemoveExtra);

            await _context.SaveChangesAsync();

            TempData["Success"] = "تم إسناد الدور للمستخدم بنجاح.";
            return RedirectToAction(nameof(Index));
        }








        /// <summary>
        /// إرجاع معاينة صلاحيات الدور لاستخدامها في شاشة إسناد دور للمستخدم.
        /// النتيجة تُعرض كـ Partial View داخل Create.cshtml عن طريق AJAX.
        /// </summary>
        /// <param name="roleId">رقم الدور المطلوب عرض صلاحياته</param>
        [HttpGet]
        public async Task<IActionResult> GetRolePermissionsPreview(int roleId)
        {
            // حماية بسيطة: لو roleId غير صحيح نرجّع بارشيال فاضي
            if (roleId <= 0)
            {
                return PartialView("_RolePermissionsPreview",
                    Enumerable.Empty<RolePermission>());
            }

            // متغير: تحميل صلاحيات هذا الدور مع بيانات الصلاحية المرتبطة
            var list = await _context.RolePermissions
                .Include(rp => rp.Permission)              // جلب بيانات Permission (الاسم / الموديول)
                .Where(rp => rp.RoleId == roleId)          // فلترة على الدور المطلوب
                .OrderBy(rp => rp.Permission.Module)       // ترتيب حسب الموديول
                .ThenBy(rp => rp.Permission.NameAr)        // ثم حسب اسم الصلاحية
                .AsNoTracking()                            // قراءة فقط لتحسين الأداء
                .ToListAsync();

            // إرجاع البارشيال مع البيانات
            return PartialView("_RolePermissionsPreview", list);
        }

        /// <summary>
        /// إرجاع كل الصلاحيات مع إمكانية التعديل (شيك بوكس).
        /// الحالة الابتدائية من الدور. إذا وُجد userId تُحمّل صلاحيات المستخدم الفعلية (دور + إضافية − ممنوعة).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetRolePermissionsEditable(int roleId, int userId = 0)
        {
            if (roleId <= 0)
            {
                return PartialView("_RolePermissionsEditable", Enumerable.Empty<RolePermissionEditItem>());
            }

            var rolePermIds = new HashSet<int>(await _context.RolePermissions
                .Where(rp => rp.RoleId == roleId && rp.IsAllowed)
                .Select(rp => rp.PermissionId)
                .ToListAsync());

            var effectiveAllowed = rolePermIds;
            if (userId > 0)
            {
                var userExtraIds = new HashSet<int>(await _context.UserExtraPermissions
                    .Where(x => x.UserId == userId)
                    .Select(x => x.PermissionId)
                    .ToListAsync());
                var userDeniedIds = new HashSet<int>(await _context.UserDeniedPermissions
                    .Where(x => x.UserId == userId)
                    .Select(x => x.PermissionId)
                    .ToListAsync());
                effectiveAllowed = new HashSet<int>(
                    rolePermIds.Where(pid => !userDeniedIds.Contains(pid)).Union(userExtraIds));
            }

            var allPerms = await _context.Permissions
                .AsNoTracking()
                .OrderBy(p => p.Module)
                .ThenBy(p => p.NameAr)
                .Select(p => new RolePermissionEditItem
                {
                    PermissionId = p.PermissionId,
                    Code = p.Code,
                    NameAr = p.NameAr,
                    Module = p.Module,
                    IsAllowed = false
                })
                .ToListAsync();

            foreach (var p in allPerms)
                p.IsAllowed = effectiveAllowed.Contains(p.PermissionId);

            return PartialView("_RolePermissionsEditable", allPerms);
        }










        // ========= EDIT =========

        public async Task<IActionResult> Edit(int id)
        {
            var item = await _context.UserRoles
                .Include(x => x.User)
                .Include(x => x.Role)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (item == null)
                return NotFound();

            await PopulateLookupsAsync(item.UserId, item.RoleId);
            return View(item);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            int id,
            [Bind("Id,UserId,RoleId,IsPrimary,AssignedAt")] UserRole item,
            int[]? selectedPermissionIds)
        {
            if (id != item.Id)
                return NotFound();

            // التأكد من عدم وجود سطر آخر بنفس (UserId, RoleId)
            bool exists = await _context.UserRoles
                .AnyAsync(x => x.Id != item.Id &&
                               x.UserId == item.UserId &&
                               x.RoleId == item.RoleId);

            if (exists)
            {
                ModelState.AddModelError(string.Empty,
                    "يوجد سطر آخر بنفس المستخدم ونفس الدور، لا يمكن التكرار.");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(item);
                    await _context.SaveChangesAsync();

                    // حفظ الصلاحيات (إضافية / ممنوعة) مثل Create — يقبل صلاحيات أكثر من الدور
                    var allowed = new HashSet<int>(selectedPermissionIds ?? Array.Empty<int>());
                    var rolePermIds = await _context.RolePermissions
                        .Where(rp => rp.RoleId == item.RoleId && rp.IsAllowed)
                        .Select(rp => rp.PermissionId)
                        .ToListAsync();

                    foreach (var permId in rolePermIds.Where(pid => !allowed.Contains(pid)))
                    {
                        if (!await _context.UserDeniedPermissions.AnyAsync(x => x.UserId == item.UserId && x.PermissionId == permId))
                        {
                            _context.UserDeniedPermissions.Add(new UserDeniedPermission
                            {
                                UserId = item.UserId,
                                PermissionId = permId,
                                IsAllowed = false
                            });
                        }
                    }
                    var toRemoveDenied = await _context.UserDeniedPermissions
                        .Where(x => x.UserId == item.UserId && rolePermIds.Contains(x.PermissionId) && allowed.Contains(x.PermissionId))
                        .ToListAsync();
                    _context.UserDeniedPermissions.RemoveRange(toRemoveDenied);

                    var allPermIds = await _context.Permissions.Where(p => p.IsActive).Select(p => p.PermissionId).ToListAsync();
                    foreach (var permId in allPermIds.Where(pid => allowed.Contains(pid) && !rolePermIds.Contains(pid)))
                    {
                        if (!await _context.UserExtraPermissions.AnyAsync(x => x.UserId == item.UserId && x.PermissionId == permId))
                        {
                            _context.UserExtraPermissions.Add(new UserExtraPermissions
                            {
                                UserId = item.UserId,
                                PermissionId = permId
                            });
                        }
                    }
                    var toRemoveExtra = await _context.UserExtraPermissions
                        .Where(x => x.UserId == item.UserId && !allowed.Contains(x.PermissionId) && !rolePermIds.Contains(x.PermissionId))
                        .ToListAsync();
                    _context.UserExtraPermissions.RemoveRange(toRemoveExtra);

                    await _context.SaveChangesAsync();

                    TempData["Success"] = "تم تعديل دور المستخدم وصلاحياته بنجاح.";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!UserRoleExists(item.Id))
                        return NotFound();
                    else
                        throw;
                }

                return RedirectToAction(nameof(Index));
            }

            await PopulateLookupsAsync(item.UserId, item.RoleId);
            return View(item);
        }

        // ========= DELETE =========

        public async Task<IActionResult> Delete(int id)
        {
            var item = await _context.UserRoles
                .AsNoTracking()
                .Include(x => x.User)
                .Include(x => x.Role)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (item == null)
                return NotFound();

            return View(item);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var item = await _context.UserRoles.FindAsync(id);
            if (item != null)
            {
                _context.UserRoles.Remove(item);
                await _context.SaveChangesAsync();

                TempData["Success"] = "تم حذف ربط الدور بالمستخدم.";
            }

            return RedirectToAction(nameof(Index));
        }

        // ========= BULK DELETE =========

        /// <summary>
        /// حذف جماعي لعدة سطور UserRole دفعة واحدة.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete([FromForm] int[] ids)
        {
            if (ids == null || ids.Length == 0)
            {
                TempData["Error"] = "من فضلك اختر على الأقل سطر واحد للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var rows = await _context.UserRoles
                .Where(x => ids.Contains(x.Id))
                .ToListAsync();

            if (rows.Count == 0)
            {
                TempData["Error"] = "لم يتم العثور على السطور المحددة.";
                return RedirectToAction(nameof(Index));
            }

            _context.UserRoles.RemoveRange(rows);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم حذف {rows.Count} سطر من جدول ربط المستخدمين بالأدوار.";
            return RedirectToAction(nameof(Index));
        }

        // ========= DELETE ALL =========

        /// <summary>
        /// حذف كل سطور UserRoles (للبيئة التجريبية).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _context.UserRoles.ToListAsync();
            _context.UserRoles.RemoveRange(all);
            await _context.SaveChangesAsync();

            TempData["Success"] = "تم حذف جميع ربط المستخدمين بالأدوار.";
            return RedirectToAction(nameof(Index));
        }








        // ========= EXPORT =========

        /// <summary>
        /// تصدير ربط المستخدمين بالأدوار بصيغة CSV.
        /// يحترم نفس فلاتر شاشة الـ Index.
        /// </summary>
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
            // 1) الاستعلام الأساسي: ربط المستخدمين بالأدوار + تحميل بياناتهم
            IQueryable<UserRole> query = _context.UserRoles
                .AsNoTracking()
                .Include(x => x.User)
                .Include(x => x.Role);

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

            // 3) ترتيب افتراضي: باسم الدخول ثم اسم الدور
            query = query
                .OrderBy(x => x.User.UserName)
                .ThenBy(x => x.Role.Name);

            var list = await query.ToListAsync();

            // نتأكد من قيمة format
            format = (format ?? "excel").ToLowerInvariant();

            // ================= فرع Excel =================
            if (format == "excel")
            {
                // تأكد إن عندك:
                // using ClosedXML.Excel;
                // using System.IO;
                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("UserRoles");

                int row = 1;

                // عناوين الأعمدة بالعربي (بدون DisplayName)
                worksheet.Cell(row, 1).Value = "رقم السطر";
                worksheet.Cell(row, 2).Value = "رقم المستخدم";
                worksheet.Cell(row, 3).Value = "اسم الدخول";
                worksheet.Cell(row, 4).Value = "رقم الدور";
                worksheet.Cell(row, 5).Value = "اسم الدور";
                worksheet.Cell(row, 6).Value = "دور أساسي؟";
                worksheet.Cell(row, 7).Value = "تاريخ الإسناد";

                var headerRange = worksheet.Range(row, 1, row, 7);
                headerRange.Style.Font.Bold = true;

                // البيانات
                foreach (var x in list)
                {
                    row++;

                    string userName = x.User?.UserName ?? string.Empty;  // اسم الدخول فقط
                    string roleName = x.Role?.Name ?? string.Empty;
                    string primary = x.IsPrimary ? "Primary" : string.Empty;

                    worksheet.Cell(row, 1).Value = x.Id;
                    worksheet.Cell(row, 2).Value = x.UserId;
                    worksheet.Cell(row, 3).Value = userName;
                    worksheet.Cell(row, 4).Value = x.RoleId;
                    worksheet.Cell(row, 5).Value = roleName;
                    worksheet.Cell(row, 6).Value = primary;
                    worksheet.Cell(row, 7).Value = x.AssignedAt;
                }

                worksheet.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                stream.Position = 0;

                var fileNameExcel = $"UserRoles_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                const string excelContentType =
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

                return File(stream.ToArray(), excelContentType, fileNameExcel);
            }

            // ================= فرع CSV =================
            var sb = new StringBuilder();

            // الهيدر (بدون DisplayName)
            sb.AppendLine("Id,UserId,UserName,RoleId,RoleName,IsPrimary,AssignedAt");

            foreach (var x in list)
            {
                string userName = (x.User?.UserName ?? string.Empty).Replace("\"", "\"\"");
                string roleName = (x.Role?.Name ?? string.Empty).Replace("\"", "\"\"");
                string primary = x.IsPrimary ? "Primary" : "";
                string assigned = x.AssignedAt.ToString("yyyy-MM-dd HH:mm");

                sb.AppendLine(
                    $"{x.Id}," +
                    $"{x.UserId}," +
                    $"\"{userName}\"," +
                    $"{x.RoleId}," +
                    $"\"{roleName}\"," +
                    $"{primary}," +
                    $"{assigned}");
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileNameCsv = $"UserRoles_{timeStamp}.csv";

            return File(bytes, "text/csv", fileNameCsv);
        }









        // ========= دالة مساعدة =========

        private bool UserRoleExists(int id)
        {
            return _context.UserRoles.Any(e => e.Id == id);
        }
    }
}
