using System;                                     // متغيرات التوقيت DateTime
using System.Linq;                                // أوامر LINQ مثل Where و OrderBy
using System.Text;                                // StringBuilder لتكوين ملف CSV
using System.Threading.Tasks;                     // async / await
using Microsoft.AspNetCore.Mvc;                   // أساس الكنترولر
using Microsoft.EntityFrameworkCore;              // AsNoTracking, ToListAsync, FindAsync
using ERP.Data;                                   // AppDbContext كائن قاعدة البيانات
using ERP.Filters;
using ERP.Infrastructure;                         // PagedResult + UserActivityLogger
using ERP.Models;                                 // Role, UserActionType
using ERP.Security;
using ClosedXML.Excel;                      // لتصدير Excel
using System.IO;

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إدارة جدول الأدوار (Roles)
    /// فيه CRUD كامل + Export + BulkDelete + DeleteAll
    /// بنفس النظام الثابت الذي استخدمناه مع Users.
    /// </summary>
    public class RolesController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IUserActivityLogger _activityLogger;

        public RolesController(AppDbContext context, IUserActivityLogger activityLogger)
        {
            _context = context;
            _activityLogger = activityLogger;
        }





        /// <summary>
        /// تطبيق فلاتر البحث والكود والتاريخ على استعلام الأدوار.
        /// نفس فكرة ApplyFilters في جدول الصلاحيات.
        /// </summary>
        private IQueryable<Role> ApplyFilters(
            IQueryable<Role> query,   // استعلام الأدوار الأساسي
            string? search,           // نص البحث
            string? searchBy,         // الحقل الذي نبحث فيه
            bool useDateRange,        // هل نستخدم فلتر التاريخ؟
            DateTime? fromDate,       // تاريخ من
            DateTime? toDate,         // تاريخ إلى
            int? fromCode,            // من كود (RoleId من)
            int? toCode)              // إلى كود (RoleId إلى)
        {
            // 1) فلتر الكود من/إلى على RoleId
            if (fromCode.HasValue)
            {
                query = query.Where(r => r.RoleId >= fromCode.Value);
            }

            if (toCode.HasValue)
            {
                query = query.Where(r => r.RoleId <= toCode.Value);
            }

            // 2) فلتر التاريخ (نستخدم CreatedAt مثل الصلاحيات)
            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                query = query.Where(r => r.CreatedAt >= fromDate.Value &&
                                         r.CreatedAt <= toDate.Value);
            }

            // 3) بحث نصي حسب اختيار المستخدم
            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = search.Trim();

                // توحيد قيمة searchBy (all / name / description / id)
                var key = (searchBy ?? "all").ToLower();

                // معالجة لو الواجهة بعتت قيم بالعربي
                key = key switch
                {
                    "الاسم" or "اسم" or "اسم الدور" => "name",
                    "الوصف" => "description",
                    "الرقم" or "رقم" or "رقم الدور" => "id",
                    _ => key
                };

                switch (key)
                {
                    case "name":     // البحث في اسم الدور فقط
                        query = query.Where(r => r.Name.Contains(term));
                        break;

                    case "description":   // البحث في الوصف فقط
                        query = query.Where(r =>
                            r.Description != null &&
                            r.Description.Contains(term));
                        break;

                    case "id":       // البحث برقم الدور
                        if (int.TryParse(term, out int idVal))
                        {
                            query = query.Where(r => r.RoleId == idVal);
                        }
                        else
                        {
                            // لو كتب نص غير رقم مع searchBy = id → لا شيء
                            query = query.Where(r => false);
                        }
                        break;

                    default:         // all: البحث في أكثر من حقل
                        query = query.Where(r =>
                            r.Name.Contains(term) ||
                            r.RoleId.ToString().Contains(term) ||
                            (r.Description ?? "").Contains(term));
                        break;
                }
            }

            return query;
        }

        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        private static IQueryable<Role> ApplyColumnFilters(
            IQueryable<Role> query,
            string? filterCol_id,
            string? filterCol_name,
            string? filterCol_description,
            string? filterCol_system,
            string? filterCol_active,
            string? filterCol_created,
            string? filterCol_updated)
        {
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) query = query.Where(r => ids.Contains(r.RoleId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_name))
            {
                var vals = filterCol_name.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) query = query.Where(r => vals.Contains(r.Name));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_description))
            {
                var vals = filterCol_description.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) query = query.Where(r => r.Description != null && vals.Contains(r.Description));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_system))
            {
                var vals = filterCol_system.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim().ToLowerInvariant()).ToList();
                var wantTrue = vals.Any(v => v == "true" || v == "1" || v == "نعم");
                var wantFalse = vals.Any(v => v == "false" || v == "0" || v == "لا");
                if (wantTrue && !wantFalse) query = query.Where(r => r.IsSystemRole);
                else if (wantFalse && !wantTrue) query = query.Where(r => !r.IsSystemRole);
            }
            if (!string.IsNullOrWhiteSpace(filterCol_active))
            {
                var vals = filterCol_active.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim().ToLowerInvariant()).ToList();
                var wantTrue = vals.Any(v => v == "true" || v == "1" || v == "نعم");
                var wantFalse = vals.Any(v => v == "false" || v == "0" || v == "لا");
                if (wantTrue && !wantFalse) query = query.Where(r => r.IsActive);
                else if (wantFalse && !wantTrue) query = query.Where(r => !r.IsActive);
            }
            if (!string.IsNullOrWhiteSpace(filterCol_created))
            {
                var parts = filterCol_created.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length >= 8).ToList();
                if (parts.Count > 0)
                {
                    var dates = new List<DateTime>();
                    foreach (var p in parts)
                        if (DateTime.TryParse(p, out var d)) dates.Add(d);
                    if (dates.Count > 0) query = query.Where(r => dates.Contains(r.CreatedAt));
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_updated))
            {
                var parts = filterCol_updated.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length >= 8).ToList();
                if (parts.Count > 0)
                {
                    var dates = new List<DateTime>();
                    foreach (var p in parts)
                        if (DateTime.TryParse(p, out var d)) dates.Add(d);
                    if (dates.Count > 0) query = query.Where(r => r.UpdatedAt.HasValue && dates.Contains(r.UpdatedAt.Value));
                }
            }
            return query;
        }

        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var columnLower = (column ?? "").Trim().ToLowerInvariant();
            var q = _context.Roles.AsNoTracking();

            if (columnLower == "id")
            {
                var ids = await q.Select(r => r.RoleId).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "name")
            {
                var list = await q.Select(r => r.Name).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s != null && s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v ?? "", display = v ?? "" }));
            }
            if (columnLower == "description")
            {
                var list = await q.Where(r => r.Description != null && r.Description != "").Select(r => r.Description!).Distinct().OrderBy(x => x).Take(300).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s != null && s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v ?? "", display = (v != null && v.Length > 50 ? v.Substring(0, 50) + "..." : v) ?? "" }));
            }
            if (columnLower == "system" || columnLower == "issystemrole")
                return Json(new[] { new { value = "true", display = "\u0646\u0639\u0645" }, new { value = "false", display = "\u0644\u0627" } });
            if (columnLower == "active" || columnLower == "isactive")
                return Json(new[] { new { value = "true", display = "\u0646\u0639\u0645" }, new { value = "false", display = "\u0644\u0627" } });
            if (columnLower == "created" || columnLower == "createdat")
            {
                var list = await q.Select(r => r.CreatedAt).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd HH:mm"), display = d.ToString("yyyy-MM-dd HH:mm") }));
            }
            if (columnLower == "updated" || columnLower == "updatedat")
            {
                var list = await q.Where(r => r.UpdatedAt.HasValue).Select(r => r.UpdatedAt!.Value).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd HH:mm"), display = d.ToString("yyyy-MM-dd HH:mm") }));
            }
            return Json(Array.Empty<object>());
        }







        // ===============================================================
        // 1) INDEX: عرض قائمة الأدوار بالنظام الموحد (بحث + ترتيب + صفحات)
        // ===============================================================
        [RequirePermission("Roles.Index")]
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
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_name = null,
            string? filterCol_description = null,
            string? filterCol_system = null,
            string? filterCol_active = null,
            string? filterCol_created = null,
            string? filterCol_updated = null)
        {
            var query = _context.Roles.AsNoTracking();
            query = ApplyFilters(query, search, searchBy, useDateRange, fromDate, toDate, fromCode, toCode);
            query = ApplyColumnFilters(query, filterCol_id, filterCol_name, filterCol_description, filterCol_system, filterCol_active, filterCol_created, filterCol_updated);

            // ❸ الترتيب
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            sort ??= "RoleId";   // الترتيب الافتراضي برقم الدور

            query = sort switch
            {
                "Name" => desc
                    ? query.OrderByDescending(r => r.Name)
                    : query.OrderBy(r => r.Name),

                "Description" => desc
                    ? query.OrderByDescending(r => r.Description)
                    : query.OrderBy(r => r.Description),

                "IsActive" => desc
                    ? query.OrderByDescending(r => r.IsActive).ThenBy(r => r.Name)
                    : query.OrderBy(r => r.IsActive).ThenBy(r => r.Name),

                "IsSystemRole" => desc
                    ? query.OrderByDescending(r => r.IsSystemRole).ThenBy(r => r.Name)
                    : query.OrderBy(r => r.IsSystemRole).ThenBy(r => r.Name),

                "CreatedAt" => desc
                    ? query.OrderByDescending(r => r.CreatedAt)
                    : query.OrderBy(r => r.CreatedAt),

                "UpdatedAt" => desc
                    ? query.OrderByDescending(r => r.UpdatedAt)
                    : query.OrderBy(r => r.UpdatedAt),

                _ => desc    // أي حالة أخرى → الترتيب برقم الدور
                    ? query.OrderByDescending(r => r.RoleId)
                    : query.OrderBy(r => r.RoleId)
            };

            // ❹ حساب إجمالي السجلات والصفحة الحالية
            int totalCount = await query.CountAsync();
            pageSize = Math.Max(1, pageSize);
            page = Math.Max(1, page);

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // ❺ تجهيز الموديل للعرض في الواجهة
            var model = new PagedResult<Role>(items, page, pageSize, totalCount)
            {
                Search = search,         // نص البحث الحالي
                SortColumn = sort,           // عمود الترتيب الحالي
                SortDescending = desc,           // هل الترتيب تنازلي؟
                UseDateRange = useDateRange,   // هل فلتر التاريخ مفعّل؟
                FromDate = fromDate,       // تاريخ من
                ToDate = toDate          // تاريخ إلى
            };

            // ❻ إرسال قيم إضافية للـ ViewBag لاستخدامها في الواجهة
            ViewBag.Search = search;
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.Sort = sort;
            ViewBag.Dir = desc ? "desc" : "asc";

            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.DateField = dateField ?? "CreatedAt";
            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_Name = filterCol_name;
            ViewBag.FilterCol_Description = filterCol_description;
            ViewBag.FilterCol_System = filterCol_system;
            ViewBag.FilterCol_Active = filterCol_active;
            ViewBag.FilterCol_Created = filterCol_created;
            ViewBag.FilterCol_Updated = filterCol_updated;

            return View(model);
        }









        // عرض تفاصيل دور واحد
        public async Task<IActionResult> Details(int? id)
        {
            // لو مفيش رقم دور في الرابط نرجّع NotFound
            if (id == null)
            {
                return NotFound();
            }

            // جلب الدور من قاعدة البيانات مع العلاقات:
            // - المستخدمون داخل الدور (UserRoles -> User)
            // - صلاحيات الدور (RolePermissions -> Permission)
            var role = await _context.Roles
                .Include(r => r.UserRoles)       // قائمة ربط الدور بالمستخدمين
                    .ThenInclude(ur => ur.User)  // جلب بيانات المستخدم
                .Include(r => r.RolePermissions) // قائمة ربط الدور بالصلاحيات
                    .ThenInclude(rp => rp.Permission) // جلب بيانات الصلاحية
                .AsNoTracking()                  // قراءة فقط بدون تتبع
                .FirstOrDefaultAsync(r => r.RoleId == id.Value);

            // لو الدور غير موجود نرجّع NotFound
            if (role == null)
            {
                return NotFound();
            }

            // إرسال الموديل لواجهة Details.cshtml
            return View(role);
        }









        // ===============================================================
        // 3) CREATE (GET): فتح فورم إضافة دور جديد
        // ===============================================================
        public IActionResult Create()
        {
            // نرجّع فورم فاضي للمستخدم
            var model = new Role
            {
                IsActive = true,        // افتراضياً الدور يكون نشط
                IsSystemRole = false    // افتراضياً ليس دور نظامي
            };

            return View(model);
        }

        // ===============================================================
        // 4) CREATE (POST): استلام بيانات الدور الجديد وحفظها
        // ===============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Role role)
        {
            // التحقق من صحة البيانات القادمة من الفورم
            if (!ModelState.IsValid)
            {
                // لو في أخطاء، نرجّع نفس الفورم مع رسائل الخطأ
                return View(role);
            }

            // تعيين تاريخ الإنشاء الحالي (UTC)
            role.CreatedAt = DateTime.UtcNow;
            role.UpdatedAt = null;

            _context.Roles.Add(role);
            await _context.SaveChangesAsync();

            await _activityLogger.LogAsync(UserActionType.Create, "Role", role.RoleId, $"إنشاء دور: {role.Name}");

            TempData["Success"] = "تم إنشاء الدور بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // ===============================================================
        // 5) EDIT (GET): فتح فورم تعديل دور
        // ===============================================================
        public async Task<IActionResult> Edit(int id)
        {
            var role = await _context.Roles.FindAsync(id);  // تحميل الدور من DB

            if (role == null)
            {
                return NotFound();
            }

            return View(role);   // عرض الفورم مع بيانات الدور
        }

        // ===============================================================
        // 6) EDIT (POST): استلام التعديلات وحفظها
        // ===============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Role input)
        {
            if (id != input.RoleId)
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                // لو في أخطاء تحقق من البيانات نرجّع الفورم بنفس القيم
                return View(input);
            }

            var role = await _context.Roles.FindAsync(id);  // تحميل الدور الأصلي

            if (role == null)
            {
                return NotFound();
            }

            var oldValues = System.Text.Json.JsonSerializer.Serialize(new { role.Name, role.Description, role.IsActive });
            // تحديث الحقول القابلة للتعديل
            role.Name = input.Name;
            role.Description = input.Description;
            role.IsActive = input.IsActive;
            role.IsSystemRole = input.IsSystemRole; // ممكن بعدها تقرر تقفله للأدوار النظامية

            // تحديث وقت آخر تعديل
            role.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            var newValues = System.Text.Json.JsonSerializer.Serialize(new { role.Name, role.Description, role.IsActive });
            await _activityLogger.LogAsync(UserActionType.Edit, "Role", id, $"تعديل دور: {role.Name}", oldValues, newValues);

            TempData["Success"] = "تم تعديل بيانات الدور بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // ===============================================================
        // 7) DELETE (GET): شاشة تأكيد حذف دور
        // ===============================================================
        public async Task<IActionResult> Delete(int id)
        {
            var role = await _context.Roles
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.RoleId == id);

            if (role == null)
            {
                return NotFound();
            }

            if (role.IsSystemRole)
            {
                TempData["Error"] = "لا يمكن حذف دور نظامي.";
                return RedirectToAction(nameof(Index));
            }

            return View(role);  // عرض شاشة تأكيد الحذف
        }

        // ===============================================================
        // 8) DELETE (POST): تنفيذ الحذف بعد التأكيد
        // ===============================================================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var role = await _context.Roles.FindAsync(id);

            if (role == null)
            {
                TempData["Error"] = "لم يتم العثور على الدور.";
                return RedirectToAction(nameof(Index));
            }

            if (role.IsSystemRole)
            {
                TempData["Error"] = "لا يمكن حذف دور نظامي.";
                return RedirectToAction(nameof(Index));
            }

            var oldValues = System.Text.Json.JsonSerializer.Serialize(new { role.Name, role.Description });
            _context.Roles.Remove(role);
            await _context.SaveChangesAsync();

            await _activityLogger.LogAsync(UserActionType.Delete, "Role", id, $"حذف دور: {role.Name}", oldValues: oldValues);

            TempData["Success"] = "تم حذف الدور بنجاح.";
            return RedirectToAction(nameof(Index));
        }








        // ===============================================================
        // 9) EXPORT: تصدير قائمة الأدوار إلى CSV بنفس فلاتر Index
        // ===============================================================
        [HttpGet]
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
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_name = null,
            string? filterCol_description = null,
            string? filterCol_system = null,
            string? filterCol_active = null,
            string? filterCol_created = null,
            string? filterCol_updated = null)
        {
            IQueryable<Role> query = _context.Roles.AsNoTracking();
            query = ApplyFilters(query, search, searchBy, useDateRange, fromDate, toDate, fromCode, toCode);
            query = ApplyColumnFilters(query, filterCol_id, filterCol_name, filterCol_description, filterCol_system, filterCol_active, filterCol_created, filterCol_updated);

            // =========================
            // الترتيب (نفس فكرة الاندكس)
            // =========================
            bool descending = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            sort = string.IsNullOrEmpty(sort) ? "RoleId" : sort;

            query = sort switch
            {
                "Name" => descending
                    ? query.OrderByDescending(r => r.Name)
                    : query.OrderBy(r => r.Name),

                "IsSystemRole" => descending
                    ? query.OrderByDescending(r => r.IsSystemRole)
                    : query.OrderBy(r => r.IsSystemRole),

                "IsActive" => descending
                    ? query.OrderByDescending(r => r.IsActive)
                    : query.OrderBy(r => r.IsActive),

                "CreatedAt" => descending
                    ? query.OrderByDescending(r => r.CreatedAt)
                    : query.OrderBy(r => r.CreatedAt),

                "UpdatedAt" => descending
                    ? query.OrderByDescending(r => r.UpdatedAt)
                    : query.OrderBy(r => r.UpdatedAt),

                _ => descending
                    ? query.OrderByDescending(r => r.RoleId)
                    : query.OrderBy(r => r.RoleId),
            };

            // جلب البيانات بعد كل الفلاتر والترتيب
            var roles = await query.ToListAsync();

            // =========================
            // إنشاء ملف Excel بـ ClosedXML
            // =========================
            using var workbook = new XLWorkbook();              // مصنف جديد
            var worksheet = workbook.Worksheets.Add("Roles"); // شيت باسم Roles

            int row = 1;

            // عناوين الأعمدة
            worksheet.Cell(row, 1).Value = "رقم الدور";
            worksheet.Cell(row, 2).Value = "اسم الدور";
            worksheet.Cell(row, 3).Value = "الوصف";
            worksheet.Cell(row, 4).Value = "دور نظامي؟";
            worksheet.Cell(row, 5).Value = "نشط؟";
            worksheet.Cell(row, 6).Value = "تاريخ الإنشاء";
            worksheet.Cell(row, 7).Value = "آخر تعديل";

            // تنسيق الهيدر
            var headerRange = worksheet.Range(row, 1, row, 7);
            headerRange.Style.Font.Bold = true;

            // البيانات
            foreach (var r in roles)
            {
                row++;

                worksheet.Cell(row, 1).Value = r.RoleId;
                worksheet.Cell(row, 2).Value = r.Name;
                worksheet.Cell(row, 3).Value = r.Description;
                worksheet.Cell(row, 4).Value = r.IsSystemRole ? "نعم" : "لا";
                worksheet.Cell(row, 5).Value = r.IsActive ? "نعم" : "لا";
                worksheet.Cell(row, 6).Value = r.CreatedAt;
                worksheet.Cell(row, 7).Value = r.UpdatedAt;
            }

            // ضبط عرض الأعمدة
            worksheet.Columns().AdjustToContents();

            // حفظ فى Stream
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            stream.Position = 0;

            var fileName = $"Roles_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            const string contentType =
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

            return File(stream.ToArray(), contentType, fileName);
        }









        // ===============================================================
        // 10) BULK DELETE: حذف مجموعة من الأدوار المحددة
        // ===============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(int[]? selectedIds)
        {
            if (selectedIds == null || selectedIds.Length == 0)
            {
                TempData["Error"] = "لم يتم تحديد أي أدوار للحذف.";
                return RedirectToAction(nameof(Index));
            }

            // اختيار الأدوار غير النظامية فقط
            var rolesToDelete = await _context.Roles
                .Where(r => selectedIds.Contains(r.RoleId) && !r.IsSystemRole)
                .ToListAsync();

            if (rolesToDelete.Count == 0)
            {
                TempData["Error"] = "لا توجد أدوار غير نظامية متاحة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            _context.Roles.RemoveRange(rolesToDelete);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم حذف {rolesToDelete.Count} دور/أدوار بنجاح.";
            return RedirectToAction(nameof(Index));
        }








        // ===============================================================
        // 11) DELETE ALL: حذف جميع الأدوار غير النظامية
        // ===============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var rolesToDelete = await _context.Roles
                .Where(r => !r.IsSystemRole)
                .ToListAsync();

            if (rolesToDelete.Count == 0)
            {
                TempData["Error"] = "لا توجد أدوار غير نظامية للحذف.";
                return RedirectToAction(nameof(Index));
            }

            _context.Roles.RemoveRange(rolesToDelete);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم حذف جميع الأدوار غير النظامية ({rolesToDelete.Count}) بنجاح.";
            return RedirectToAction(nameof(Index));
        }
    }
}
