using System;                                     // متغيرات التاريخ DateTime
using System.Collections.Generic;                 // القوائم List
using System.Linq;                                // أوامر LINQ
using System.Text;                                // StringBuilder للتصدير
using System.Threading.Tasks;                     // Task و async
using ERP.Data;                                   // AppDbContext
using ERP.Infrastructure;                         // PagedResult
using ERP.Models;                                 // الموديلات RolePermission, Role, Permission
using Microsoft.AspNetCore.Mvc;                   // أساس الكنترولر
using Microsoft.AspNetCore.Mvc.Rendering;         // SelectList
using Microsoft.EntityFrameworkCore;              // Include, AsNoTracking, ToListAsync
using ClosedXML.Excel;                      // لتصدير Excel
using System.IO;

namespace ERP.Controllers
{
    /// <summary>
    /// إدارة ربط الأدوار بالصلاحيات (RolePermissions)
    /// كل سطر = صلاحية واحدة مرتبطة بدور معيّن.
    /// </summary>
    public class RolePermissionsController : Controller
    {
        private readonly AppDbContext _context;   // كائن الاتصال بقاعدة البيانات

        public RolePermissionsController(AppDbContext context)
        {
            _context = context;
        }






        // ========= دوال مساعدة: الفلاتر + القوائم المنسدلة =========






        // ========================================
        // دالة الفلترة والبحث لجدول صلاحيات الأدوار
        // ========================================

        // ========================================
        // ===== فلترة RolePermission بنفس فكرة صلاحيات Permissions =====
        // ===== فلترة RolePermission بنفس فكرة صلاحيات Permissions =====
        private IQueryable<RolePermission> ApplyFilters(
            IQueryable<RolePermission> query,
            string? search,
            string? searchBy,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            int? fromCode,
            int? toCode)
        {
            // 1) فلتر الكود من/إلى على المعرف Id
            if (fromCode.HasValue)
            {
                query = query.Where(rp => rp.Id >= fromCode.Value);
            }

            if (toCode.HasValue)
            {
                query = query.Where(rp => rp.Id <= toCode.Value);
            }

            // 2) فلتر التاريخ على CreatedAt فى جدول RolePermissions نفسه
            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                query = query.Where(rp =>
                    rp.CreatedAt >= fromDate.Value &&
                    rp.CreatedAt <= toDate.Value);
            }

            // 3) البحث النصّي
            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = search.Trim();
                string mode = (searchBy ?? "all").ToLower();

                switch (mode)
                {
                    case "roleid":
                        if (int.TryParse(term, out int rid))
                            query = query.Where(rp => rp.RoleId == rid);
                        else
                            query = query.Where(rp => false);
                        break;

                    case "permissionid":
                        if (int.TryParse(term, out int pid))
                            query = query.Where(rp => rp.PermissionId == pid);
                        else
                            query = query.Where(rp => false);
                        break;

                    case "role":
                    case "rolename":
                        query = query.Where(rp =>
                            rp.Role.Name.Contains(term) ||
                            (rp.Role.Description ?? "").Contains(term));
                        break;

                    case "permission":
                        query = query.Where(rp =>
                            rp.Permission.Code.Contains(term) ||
                            (rp.Permission.NameAr ?? "").Contains(term) ||
                            (rp.Permission.Module ?? "").Contains(term));
                        break;

                    case "module":
                        query = query.Where(rp =>
                            (rp.Permission.Module ?? "").Contains(term));
                        break;

                    case "id":
                        if (int.TryParse(term, out int idVal))
                            query = query.Where(rp => rp.Id == idVal);
                        else
                            query = query.Where(rp => false);
                        break;

                    default: // all
                        query = query.Where(rp =>
                            rp.Id.ToString().Contains(term) ||
                            rp.RoleId.ToString().Contains(term) ||
                            rp.PermissionId.ToString().Contains(term) ||
                            rp.Role.Name.Contains(term) ||
                            (rp.Role.Description ?? "").Contains(term) ||
                            rp.Permission.Code.Contains(term) ||
                            (rp.Permission.NameAr ?? "").Contains(term) ||
                            (rp.Permission.Module ?? "").Contains(term));
                        break;
                }
            }

            return query;
        }














        // ========= INDEX: قائمة صلاحيات الأدوار بنظام القوائم الموحد =========

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
            // استعلام أساسي مع Include على الدور والصلاحية
            IQueryable<RolePermission> query = _context.RolePermissions
                .AsNoTracking()
                .Include(rp => rp.Role)
                .Include(rp => rp.Permission);

            // تطبيق البحث + فلاتر الكود + فلاتر التاريخ
            query = ApplyFilters(query, search, searchBy, useDateRange, fromDate, toDate, fromCode, toCode);

            // الترتيب
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            sort ??= "Id";  // الترتيب الافتراضي برقم السطر

            query = sort switch
            {
                "RoleId" => desc
                    ? query.OrderByDescending(rp => rp.RoleId)
                    : query.OrderBy(rp => rp.RoleId),

                "RoleName" => desc
                    ? query.OrderByDescending(rp => rp.Role.Name)
                    : query.OrderBy(rp => rp.Role.Name),

                "PermissionId" => desc
                    ? query.OrderByDescending(rp => rp.PermissionId)
                    : query.OrderBy(rp => rp.PermissionId),

                "PermissionCode" => desc
                    ? query.OrderByDescending(rp => rp.Permission.Code)
                    : query.OrderBy(rp => rp.Permission.Code),

                "Module" => desc
                    ? query.OrderByDescending(rp => rp.Permission.Module)
                    : query.OrderBy(rp => rp.Permission.Module),

                "IsAllowed" => desc
                    ? query.OrderByDescending(rp => rp.IsAllowed)
                    : query.OrderBy(rp => rp.IsAllowed),

                "CreatedAt" => desc
                    ? query.OrderByDescending(rp => rp.CreatedAt)
                    : query.OrderBy(rp => rp.CreatedAt),

                "UpdatedAt" => desc
                    ? query.OrderByDescending(rp => rp.UpdatedAt)
                    : query.OrderBy(rp => rp.UpdatedAt),

                _ => desc
                    ? query.OrderByDescending(rp => rp.Id)
                    : query.OrderBy(rp => rp.Id)
            };

            // الترقيم
            int totalCount = await query.CountAsync();
            pageSize = Math.Max(1, pageSize);
            page = Math.Max(1, page);

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // نفس الكونستركتور الذي استخدمته في PermissionsController
            var model = new PagedResult<RolePermission>(items, page, pageSize, totalCount)
            {
                Search = search,
                SortColumn = sort,
                SortDescending = desc,
                UseDateRange = useDateRange,
                FromDate = fromDate,
                ToDate = toDate
            };

            // إرسال قيم الفلاتر للواجهة
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.DateField = dateField ?? "CreatedAt";

            return View(model);
        }









        // ========= DETAILS =========

        /// <summary>
        /// عرض تفاصيل ربط دور بصلاحية معيّنة.
        /// </summary>
        public async Task<IActionResult> Details(int id)
        {
            // تأكدنا أن رقم المعرّف صحيح (أكبر من صفر)
            if (id <= 0)
                return NotFound();

            // جلب السطر من قاعدة البيانات مع تحميل بيانات الدور والصلاحية
            var row = await _context.RolePermissions
                .AsNoTracking()              // قراءة فقط بدون تتبع للتعديل
                .Include(x => x.Role)        // يشمل بيانات الدور المرتبط
                .Include(x => x.Permission)  // يشمل بيانات الصلاحية المرتبطة
                .FirstOrDefaultAsync(x => x.Id == id);

            // لو السطر غير موجود نرجع 404
            if (row == null)
                return NotFound();

            // إرسال السطر إلى صفحة العرض (Details.cshtml)
            return View(row);
        }






        // تحميل القوائم المنسدلة للأدوار والصلاحيات للفورمات (Create / Edit)
        private async Task PopulateLookupsAsync(
            int? selectedRoleId = null,
            int? selectedPermissionId = null)
        {
            // ===== قائمة الأدوار =====
            var roles = await _context.Roles
                .AsNoTracking()
                .OrderBy(r => r.Name)
                .Select(r => new
                {
                    r.RoleId,
                    Text = r.Name + " (" + r.RoleId + ")"
                })
                .ToListAsync();

            // ===== قائمة الصلاحيات =====
            var perms = await _context.Permissions
                .AsNoTracking()
                .OrderBy(p => p.Module)
                .ThenBy(p => p.NameAr)
                .Select(p => new
                {
                    p.PermissionId,
                    Text =
                        // لو فيه موديول نعرضه بين أقواس
                        ((p.Module ?? "") != ""
                            ? "[" + p.Module + "] "
                            : "") +
                        (p.NameAr ?? "") +
                        " (" + p.Code + ")"
                })
                .ToListAsync();

            // ربط القوائم بالـ ViewBag علشان الفورم تستخدمها
            ViewBag.RoleId = new SelectList(roles, "RoleId", "Text", selectedRoleId);
            ViewBag.PermissionId = new SelectList(perms, "PermissionId", "Text", selectedPermissionId);
        }








        // ========= CREATE =========
        public async Task<IActionResult> Create()
        {
            // تحميل القوائم المنسدلة للأدوار والصلاحيات
            await PopulateLookupsAsync();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            [Bind("RoleId,PermissionId,IsAllowed")] RolePermission item)
        {
            // ✅ التحقق من اختيار الدور
            if (item.RoleId <= 0)
            {
                ModelState.AddModelError("RoleId", "من فضلك اختر دوراً.");
            }

            // ✅ التحقق من اختيار الصلاحية
            if (item.PermissionId <= 0)
            {
                ModelState.AddModelError("PermissionId", "من فضلك اختر صلاحية.");
            }

            // ✅ التأكد من عدم تكرار (RoleId, PermissionId) في جدول RolePermissions
            bool exists = await _context.RolePermissions
                .AnyAsync(x => x.RoleId == item.RoleId &&
                               x.PermissionId == item.PermissionId);

            if (exists)
            {
                ModelState.AddModelError(string.Empty,
                    "هذا الدور يمتلك هذه الصلاحية بالفعل (يوجد سطر بنفس الدور والصلاحية).");
            }

            if (ModelState.IsValid)
            {
                // ضبط تاريخ الإنشاء (أول مرة إضافة)
                item.CreatedAt = DateTime.UtcNow;   // تاريخ إنشاء الربط
                item.UpdatedAt = null;              // لا يوجد تعديل حتى الآن

                _context.RolePermissions.Add(item);
                await _context.SaveChangesAsync();

                TempData["Success"] = "تم إضافة صلاحية للدور بنجاح.";
                return RedirectToAction(nameof(Index));
            }

            // في حالة وجود خطأ: نعيد تحميل القوائم مع اختيار القيم الحالية
            await PopulateLookupsAsync(item.RoleId, item.PermissionId);
            return View(item);
        }








        // ========= EDIT =========

        // GET: RolePermissions/Edit/1   => 1 هنا = RoleId
        public async Task<IActionResult> Edit(int id)
        {
            // 🟣 1) جلب الدور
            var role = await _context.Roles.FindAsync(id);
            if (role == null)
                return NotFound();

            // 🟣 2) جلب كل الصلاحيات
            var permissions = await _context.Permissions
                .OrderBy(p => p.Module)    // ترتيب بالموديول
                .ThenBy(p => p.NameAr)     // ثم باسم الصلاحية
                .ToListAsync();

            // 🟣 3) جلب صلاحيات هذا الدور
            var currentIdsList = await _context.RolePermissions
                .Where(rp => rp.RoleId == id)
                .Select(rp => rp.PermissionId)
                .ToListAsync();

            var currentIds = new HashSet<int>(currentIdsList);

            // 🟣 4) تجهيز البيانات للفيو عن طريق ViewBag
            ViewBag.RoleName = role.Name;          // اسم الدور
            ViewBag.Permissions = permissions;     // كل الصلاحيات
            ViewBag.SelectedPermissionIds = currentIds;  // الصلاحيات الحالية للدور

            // 🟣 5) موديل بسيط يحتوى على RoleId فقط
            var model = new RolePermission
            {
                RoleId = id
            };

            return View(model);
        }










        // POST: RolePermissions/Edit  (حفظ التعديلات)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int roleId, int[] selectedPermissionIds)
        {
            // 🟣 تحويل المصفوفة إلى HashSet لسهولة البحث
            var selected = selectedPermissionIds?.ToHashSet() ?? new HashSet<int>();

            // 🟣 1) الصلاحيات الحالية فى قاعدة البيانات لهذا الدور
            var existing = await _context.RolePermissions
                .Where(rp => rp.RoleId == roleId)
                .ToListAsync();

            var existingIds = existing
                .Select(rp => rp.PermissionId)
                .ToHashSet();

            // 🟣 2) حذف أى صلاحيات لم تعد محددة بالتشيك بوكس
            var toDelete = existing
                .Where(rp => !selected.Contains(rp.PermissionId))
                .ToList();

            if (toDelete.Count > 0)
                _context.RolePermissions.RemoveRange(toDelete);

            // 🟣 3) إضافة الصلاحيات الجديدة التى تم تعليمها
            var now = DateTime.UtcNow;
            var toAdd = new List<RolePermission>();

            foreach (var permId in selected)
            {
                if (!existingIds.Contains(permId))
                {
                    toAdd.Add(new RolePermission
                    {
                        RoleId = roleId,          // الدور
                        PermissionId = permId,    // الصلاحية
                        IsAllowed = true,         // الدور مسموح له بهذه الصلاحية
                        CreatedAt = now,
                        UpdatedAt = null
                    });
                }
            }

            if (toAdd.Count > 0)
                await _context.RolePermissions.AddRangeAsync(toAdd);

            // 🟣 4) حفظ التغييرات
            await _context.SaveChangesAsync();

            TempData["Success"] = "تم تحديث صلاحيات الدور بنجاح.";
            return RedirectToAction(nameof(Index));
        }








        // ========= DELETE =========

        public async Task<IActionResult> Delete(int id)
        {
            var row = await _context.RolePermissions
                .AsNoTracking()
                .Include(x => x.Role)
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
            var row = await _context.RolePermissions.FindAsync(id);
            if (row != null)
            {
                _context.RolePermissions.Remove(row);
                await _context.SaveChangesAsync();

                TempData["Success"] = "تم حذف سطر ربط الدور بالصلاحية.";
            }

            return RedirectToAction(nameof(Index));
        }

        // ========= BULK DELETE =========

        /// <summary>
        /// حذف جماعي لعدة سطور RolePermission دفعة واحدة.
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

            var rows = await _context.RolePermissions
                .Where(x => ids.Contains(x.Id))
                .ToListAsync();

            if (rows.Count == 0)
            {
                TempData["Error"] = "لم يتم العثور على السطور المحددة.";
                return RedirectToAction(nameof(Index));
            }

            _context.RolePermissions.RemoveRange(rows);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم حذف {rows.Count} سطر من ربط الأدوار بالصلاحيات.";
            return RedirectToAction(nameof(Index));
        }

        // ========= DELETE ALL =========

        /// <summary>
        /// حذف كل سطور RolePermissions (للبيئة التجريبية).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _context.RolePermissions.ToListAsync();
            _context.RolePermissions.RemoveRange(all);
            await _context.SaveChangesAsync();

            TempData["Success"] = "تم حذف جميع صلاحيات الأدوار.";
            return RedirectToAction(nameof(Index));
        }









        // ========= EXPORT =========

        /// <summary>
        /// تصدير ربط الأدوار بالصلاحيات بصيغة CSV (أو Excel عند فتحه).
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
            // 1) الاستعلام الأساسي: صلاحيات الأدوار + الدور + الصلاحية (قراءة فقط)
            IQueryable<RolePermission> query = _context.RolePermissions
                .AsNoTracking()
                .Include(x => x.Role)
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

            // 3) ترتيب افتراضي للتصدير:
            // أولاً باسم الدور، ثم بالموديول، ثم كود الصلاحية
            query = query
                .OrderBy(x => x.Role.Name)
                .ThenBy(x => x.Permission.Module)
                .ThenBy(x => x.Permission.Code);

            var list = await query.ToListAsync();   // متغير: القائمة النهائية للتصدير

            // نتأكد من قيمة format
            format = (format ?? "excel").ToLowerInvariant();

            // ================= فرع Excel =================
            if (format == "excel")
            {
                // تأكد من وجود:
                // using ClosedXML.Excel;
                // using System.IO;
                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("RolePermissions");

                int row = 1;

                // عناوين الأعمدة بالعربي
                worksheet.Cell(row, 1).Value = "رقم السطر";
                worksheet.Cell(row, 2).Value = "رقم الدور";
                worksheet.Cell(row, 3).Value = "اسم الدور";
                worksheet.Cell(row, 4).Value = "رقم الصلاحية";
                worksheet.Cell(row, 5).Value = "كود الصلاحية";
                worksheet.Cell(row, 6).Value = "اسم الصلاحية";
                worksheet.Cell(row, 7).Value = "الموديول";
                worksheet.Cell(row, 8).Value = "الحالة (Allow/Deny)";

                var headerRange = worksheet.Range(row, 1, row, 8);
                headerRange.Style.Font.Bold = true;   // جعل الهيدر Bold

                // البيانات سطر بسطر
                foreach (var x in list)
                {
                    row++;

                    string roleName = x.Role?.Name ?? string.Empty;
                    string permCode = x.Permission?.Code ?? string.Empty;
                    string permName = x.Permission?.NameAr ?? string.Empty;
                    string module = x.Permission?.Module ?? string.Empty;
                    string allowed = x.IsAllowed ? "Allow" : "Deny";

                    worksheet.Cell(row, 1).Value = x.Id;
                    worksheet.Cell(row, 2).Value = x.RoleId;
                    worksheet.Cell(row, 3).Value = roleName;
                    worksheet.Cell(row, 4).Value = x.PermissionId;
                    worksheet.Cell(row, 5).Value = permCode;
                    worksheet.Cell(row, 6).Value = permName;
                    worksheet.Cell(row, 7).Value = module;
                    worksheet.Cell(row, 8).Value = allowed;
                }

                // ضبط عرض الأعمدة تلقائياً
                worksheet.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                stream.Position = 0;

                var fileNameExcel = $"RolePermissions_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                const string excelContentType =
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

                return File(stream.ToArray(), excelContentType, fileNameExcel);
            }

            // ================= فرع CSV (النمط القديم) =================
            var sb = new StringBuilder();
            sb.AppendLine("Id,RoleId,RoleName,PermissionId,PermissionCode,PermissionName,Module,IsAllowed");

            foreach (var x in list)
            {
                string roleName = (x.Role?.Name ?? string.Empty).Replace("\"", "\"\"");
                string permCode = (x.Permission?.Code ?? string.Empty).Replace("\"", "\"\"");
                string permName = (x.Permission?.NameAr ?? string.Empty).Replace("\"", "\"\"");
                string module = (x.Permission?.Module ?? string.Empty).Replace("\"", "\"\"");
                string allowed = x.IsAllowed ? "Allow" : "Deny";

                sb.AppendLine(
                    $"{x.Id}," +
                    $"{x.RoleId}," +
                    $"\"{roleName}\"," +
                    $"{x.PermissionId}," +
                    $"\"{permCode}\"," +
                    $"\"{permName}\"," +
                    $"\"{module}\"," +
                    $"{allowed}");
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileNameCsv = $"RolePermissions_{timeStamp}.csv";

            return File(bytes, "text/csv", fileNameCsv);
        }







        // ========= دالة مساعدة =========

        private bool RolePermissionExists(int id)
        {
            return _context.RolePermissions.Any(e => e.Id == id);
        }
    }
}
