using System;                                     // متغيرات التاريخ DateTime
using System.Collections.Generic;                 // القوائم List
using System.Linq;                                // أوامر LINQ مثل Where و OrderBy
using System.Text;                                // StringBuilder لبناء CSV
using System.Threading.Tasks;                     // Task و async

using ERP.Data;                                   // AppDbContext
using ERP.Infrastructure;                         // PagedResult
using ERP.Models;                                 // موديل Permission
using Microsoft.AspNetCore.Mvc;                   // أساس الكنترولر
using Microsoft.EntityFrameworkCore;              // Include, AsNoTracking, ToListAsync
using ClosedXML.Excel;                      // لتصدير Excel
using System.IO;

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إدارة جدول الصلاحيات (Permissions).
    /// مسئول عن:
    /// - عرض قائمة الصلاحيات مع بحث/ترتيب/فلترة وتصدير.
    /// - CRUD كامل (إضافة، تعديل، حذف).
    /// - الحذف الجماعي والحذف الكلي (للاستخدام في بيئة تجريبية).
    /// </summary>
    public class PermissionsController : Controller
    {
        private readonly AppDbContext _context;   // متغير: كائن الاتصال بقاعدة البيانات

        public PermissionsController(AppDbContext context)
        {
            _context = context;
        }

        // ========= دالة مساعدة لتطبيق الفلاتر (بحث + كود + تاريخ) =========

        /// <summary>
        /// تطبيق البحث والفلاتر على استعلام الصلاحيات.
        /// تُستخدم في Index و Export لضمان نفس المنطق.
        /// </summary>
        private IQueryable<Permission> ApplyFilters(
            IQueryable<Permission> query,
            string? search,
            string? searchBy,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            int? fromCode,
            int? toCode)
        {
            // فلتر الكود من/إلى على PermissionId
            if (fromCode.HasValue)
            {
                query = query.Where(p => p.PermissionId >= fromCode.Value);
            }

            if (toCode.HasValue)
            {
                query = query.Where(p => p.PermissionId <= toCode.Value);
            }

            // فلتر التاريخ (نستخدم CreatedAt)
            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                query = query.Where(p => p.CreatedAt >= fromDate.Value &&
                                         p.CreatedAt <= toDate.Value);
            }

            // بحث نصي حسب اختيار المستخدم
            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = search.Trim();

                switch ((searchBy ?? "all").ToLower())
                {
                    case "code":
                        query = query.Where(p => p.Code.Contains(term));
                        break;

                    case "name":
                        query = query.Where(p => p.NameAr.Contains(term));
                        break;

                    case "module":
                        query = query.Where(p => p.Module != null &&
                                                 p.Module.Contains(term));
                        break;

                    case "id":
                        if (int.TryParse(term, out int idVal))
                        {
                            query = query.Where(p => p.PermissionId == idVal);
                        }
                        else
                        {
                            // لو كتب نص مش رقم مع searchBy = id نرجّع لا شيء
                            query = query.Where(p => false);
                        }
                        break;

                    default: // all
                        query = query.Where(p =>
                            p.Code.Contains(term) ||
                            p.NameAr.Contains(term) ||
                            (p.Module ?? "").Contains(term) ||
                            (p.Description ?? "").Contains(term));
                        break;
                }
            }

            return query;
        }








        // ========= INDEX: قائمة الصلاحيات بنظام القوائم الموحّد =========

        /// <summary>
        /// عرض قائمة الصلاحيات مع:
        /// - بحث (نص عام / كود / اسم / موديول / رقم)
        /// - فلتر كود من/إلى
        /// - فلتر تاريخ إنشاء من/إلى
        /// - ترتيب + تقسيم صفحات باستخدام PagedResult
        /// </summary>
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
            // استعلام أساسي بدون تتبع (للقراءة فقط)
            var query = _context.Permissions
                .AsNoTracking();

            // تطبيق البحث + فلاتر الكود + فلاتر التاريخ
            query = ApplyFilters(query, search, searchBy, useDateRange, fromDate, toDate, fromCode, toCode);

            // الترتيب
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            sort ??= "PermissionId"; // الترتيب الافتراضي برقم الصلاحية

            query = sort switch
            {
                "Code" => desc
                    ? query.OrderByDescending(p => p.Code)
                    : query.OrderBy(p => p.Code),

                "NameAr" => desc
                    ? query.OrderByDescending(p => p.NameAr)
                    : query.OrderBy(p => p.NameAr),

                "Module" => desc
                    ? query.OrderByDescending(p => p.Module)
                    : query.OrderBy(p => p.Module),

                "CreatedAt" => desc
                    ? query.OrderByDescending(p => p.CreatedAt)
                    : query.OrderBy(p => p.CreatedAt),

                "UpdatedAt" => desc
                    ? query.OrderByDescending(p => p.UpdatedAt)
                    : query.OrderBy(p => p.UpdatedAt),

                _ => desc
                    ? query.OrderByDescending(p => p.PermissionId)
                    : query.OrderBy(p => p.PermissionId)
            };

            // حساب إجمالي السجلات والصفحة الحالية
            int totalCount = await query.CountAsync();
            pageSize = Math.Max(1, pageSize);
            page = Math.Max(1, page);

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // تجهيز الموديل للعرض في الواجهة
            // ✅ الترتيب الصحيح
            var model = new PagedResult<Permission>(items, page, pageSize, totalCount)
            {
                Search = search,
                SortColumn = sort,
                SortDescending = desc,
                UseDateRange = useDateRange,
                FromDate = fromDate,
                ToDate = toDate
            };


            // إرسال قيم إضافية للـ ViewBag لاحتياج الواجهة
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.DateField = dateField ?? "CreatedAt";

            return View(model);
        }







        // ========= DETAILS: عرض تفاصيل صلاحية واحدة =========

        /// <summary>
        /// عرض تفاصيل صلاحية واحدة، مع بيان الأدوار المرتبطة
        /// واستثناءات المستخدمين (إن وجدت).
        /// </summary>
        public async Task<IActionResult> Details(int id)
        {
            var permission = await _context.Permissions
                .AsNoTracking()
                .Include(p => p.RolePermissions)          // العلاقات مع الأدوار
                .Include(p => p.UserDeniedPermissions)    // استثناءات المستخدمين
                .FirstOrDefaultAsync(p => p.PermissionId == id);

            if (permission == null)
            {
                return NotFound();
            }

            return View(permission);
        }






        // ========= CREATE: إضافة صلاحية جديدة =========

        /// <summary>
        /// شاشة إضافة صلاحية جديدة.
        /// </summary>
        public IActionResult Create()
        {
            return View();
        }

        /// <summary>
        /// استلام بيانات الصلاحية الجديدة وحفظها.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Code,NameAr,Module,Description")] Permission permission)
        {
            if (ModelState.IsValid)
            {
                permission.CreatedAt = DateTime.UtcNow;   // تاريخ الإنشاء
                _context.Add(permission);
                await _context.SaveChangesAsync();

                TempData["Success"] = "تم إضافة الصلاحية بنجاح.";
                return RedirectToAction(nameof(Index));
            }

            // لو في أخطاء تحقق من الصحة نرجع لنفس الفورم
            return View(permission);
        }






        // ========= EDIT: تعديل صلاحية =========

        /// <summary>
        /// عرض بيانات صلاحية للتعديل.
        /// </summary>
        public async Task<IActionResult> Edit(int id)
        {
            var permission = await _context.Permissions.FindAsync(id);
            if (permission == null)
            {
                return NotFound();
            }

            return View(permission);
        }

        /// <summary>
        /// استلام بيانات التعديل وحفظها في قاعدة البيانات.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            int id,
            [Bind("PermissionId,Code,NameAr,Module,Description,CreatedAt")] Permission permission)
        {
            if (id != permission.PermissionId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    permission.UpdatedAt = DateTime.UtcNow;   // تحديث تاريخ التعديل
                    _context.Update(permission);
                    await _context.SaveChangesAsync();

                    TempData["Success"] = "تم تعديل الصلاحية بنجاح.";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!PermissionExists(permission.PermissionId))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }

                return RedirectToAction(nameof(Index));
            }

            return View(permission);
        }

        // ========= DELETE (مفردة) – لو حبّيت تستخدمها لاحقًا =========

        /// <summary>
        /// شاشة تأكيد حذف لصلاحية واحدة (اختيارية).
        /// </summary>
        public async Task<IActionResult> Delete(int id)
        {
            var permission = await _context.Permissions
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PermissionId == id);

            if (permission == null)
            {
                return NotFound();
            }

            return View(permission);
        }

        /// <summary>
        /// تنفيذ الحذف لصلاحية واحدة.
        /// </summary>
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var permission = await _context.Permissions.FindAsync(id);
            if (permission != null)
            {
                _context.Permissions.Remove(permission);
                await _context.SaveChangesAsync();

                TempData["Success"] = "تم حذف الصلاحية.";
            }

            return RedirectToAction(nameof(Index));
        }

        // ========= BULK DELETE: حذف مجموعة من الصلاحيات =========

        /// <summary>
        /// حذف جماعي لمجموعة من الصلاحيات بناءً على المعرّفات القادمة من الواجهة.
        /// يُستخدم مع زر "حذف المحدد".
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete([FromForm] int[] ids)
        {
            if (ids == null || ids.Length == 0)
            {
                TempData["Error"] = "من فضلك اختر على الأقل صلاحية واحدة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var permissions = await _context.Permissions
                .Where(p => ids.Contains(p.PermissionId))
                .ToListAsync();

            if (permissions.Count == 0)
            {
                TempData["Error"] = "لم يتم العثور على الصلاحيات المحددة.";
                return RedirectToAction(nameof(Index));
            }

            _context.Permissions.RemoveRange(permissions);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم حذف {permissions.Count} صلاحية بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // ========= DELETE ALL: حذف كل الصلاحيات (للبيئة التجريبية) =========

        /// <summary>
        /// حذف جميع الصلاحيات من الجدول.
        /// ⚠ يُفضّل استخدامه في بيئة تجريبية فقط.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _context.Permissions.ToListAsync();
            _context.Permissions.RemoveRange(all);
            await _context.SaveChangesAsync();

            TempData["Success"] = "تم حذف جميع الصلاحيات.";
            return RedirectToAction(nameof(Index));
        }







        // ========= EXPORT: تصدير الصلاحيات إلى CSV/Excel =========

        /// <summary>
        /// تصدير الصلاحيات بنفس فلاتر الشاشة (بحث/كود/تاريخ).
        /// format: "excel" أو "csv" (الاتنين حالياً CSV مع اختلاف الاسم فقط).
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
            // 1) الاستعلام الأساسي من جدول الصلاحيات (قراءة فقط)
            var query = _context.Permissions
                .AsNoTracking();

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

            // 3) ترتيب ثابت بالتصدير برقم الصلاحية
            var list = await query
                .OrderBy(p => p.PermissionId)
                .ToListAsync();

            // نحدد نوع التصدير المطلوب
            format = (format ?? "excel").ToLowerInvariant();

            // ============= فرع Excel =============
            if (format == "excel")
            {
                // تأكد إن عندك using ClosedXML.Excel; و using System.IO; فى أعلى الملف
                using var workbook = new XLWorkbook();                 // مصنف جديد
                var worksheet = workbook.Worksheets.Add("Permissions"); // شيت باسم Permissions

                int row = 1; // الهيدر

                // عناوين الأعمدة بالعربى
                worksheet.Cell(row, 1).Value = "رقم الصلاحية";
                worksheet.Cell(row, 2).Value = "كود الصلاحية";
                worksheet.Cell(row, 3).Value = "الاسم (عربي)";
                worksheet.Cell(row, 4).Value = "الوحدة / الموديول";
                worksheet.Cell(row, 5).Value = "الوصف";
                worksheet.Cell(row, 6).Value = "تاريخ الإنشاء";
                worksheet.Cell(row, 7).Value = "آخر تعديل";

                var headerRange = worksheet.Range(row, 1, row, 7);
                headerRange.Style.Font.Bold = true;

                // البيانات
                foreach (var p in list)
                {
                    row++;

                    worksheet.Cell(row, 1).Value = p.PermissionId;
                    worksheet.Cell(row, 2).Value = p.Code;
                    worksheet.Cell(row, 3).Value = p.NameAr;
                    worksheet.Cell(row, 4).Value = p.Module;
                    worksheet.Cell(row, 5).Value = p.Description;
                    worksheet.Cell(row, 6).Value = p.CreatedAt;
                    worksheet.Cell(row, 7).Value = p.UpdatedAt;
                }

                // ضبط عرض الأعمدة
                worksheet.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                stream.Position = 0;

                var fileNameExcel = $"Permissions_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                const string excelContentType =
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

                return File(stream.ToArray(), excelContentType, fileNameExcel);
            }

            // ============= فرع CSV (النمط القديم) =============
            var sb = new StringBuilder();

            // عناوين الأعمدة بالإنجليزي (نفس القديم)
            sb.AppendLine("PermissionId,Code,NameAr,Module,Description,CreatedAt,UpdatedAt");

            foreach (var p in list)
            {
                // تنظيف النص من علامات " حتى لا تكسر CSV
                string safeCode = (p.Code ?? string.Empty).Replace("\"", "\"\"");
                string safeName = (p.NameAr ?? string.Empty).Replace("\"", "\"\"");
                string safeModule = (p.Module ?? string.Empty).Replace("\"", "\"\"");
                string safeDesc = (p.Description ?? string.Empty).Replace("\"", "\"\"");

                string created = p.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                string updated = p.UpdatedAt.HasValue
                    ? p.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm")
                    : string.Empty;

                sb.AppendLine(
                    $"{p.PermissionId}," +
                    $"\"{safeCode}\"," +
                    $"\"{safeName}\"," +
                    $"\"{safeModule}\"," +
                    $"\"{safeDesc}\"," +
                    $"{created}," +
                    $"{updated}");
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"Permissions_{timeStamp}.csv";

            return File(bytes, "text/csv", fileName);
        }









        // ========= دالة مساعدة للتحقق من وجود الصلاحية =========

        private bool PermissionExists(int id)
        {
            return _context.Permissions.Any(e => e.PermissionId == id);
        }
    }
}
