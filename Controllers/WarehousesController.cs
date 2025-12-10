using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;     // SelectList / SelectListItem
using Microsoft.EntityFrameworkCore;
using ERP.Data;
using ERP.Models;
using ERP.Infrastructure;                     // PagedResult
using System.IO;                 // MemoryStream
using System.Text;               // StringBuilder + Encoding للـ CSV
using System.Globalization;      // CultureInfo لو احتجنا تنسيق أرقام
using ClosedXML.Excel;           // مكتبة Excel


namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إدارة جدول المخازن (Warehouses)
    /// تطبيق نظام القوائم الموحد مع فورم SHOW:
    ///  - بحث + ترتيب + ترقيم صفحات
    ///  - فلترة بالتاريخ/الوقت (تاريخ الإنشاء)
    ///  - اختيار الأعمدة + حذف المحدد + حذف الكل
    /// </summary>
    public class WarehousesController : Controller
    {
        private readonly AppDbContext _db;    // متغير: سياق قاعدة البيانات

        public WarehousesController(AppDbContext db) => _db = db;

        // =========================
        // Index — قائمة المخازن
        // =========================
        [HttpGet]
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "name",      // name | id | branch | active
            string? sort = "name",          // name | id | branch | active | created | modified
            string? dir = "asc",
            int page = 1,
            int pageSize = 25,
            bool useDateRange = false,      // تفعيل فلترة التاريخ
            DateTime? fromDate = null,      // من تاريخ (تاريخ إنشاء المخزن)
            DateTime? toDate = null         // إلى تاريخ
        )
        {
            // الاستعلام الأساسي مع جلب الفرع (Branch)
            IQueryable<Warehouse> q = _db.Warehouses
                                         .AsNoTracking()
                                         .Include(w => w.Branch);

            // تنظيف قيم الفلاتر
            var s = (search ?? string.Empty).Trim();
            var sb = (searchBy ?? "name").Trim().ToLowerInvariant();
            var so = (sort ?? "name").Trim().ToLowerInvariant();
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            // ===== البحث =====
            if (!string.IsNullOrEmpty(s))
            {
                switch (sb)
                {
                    case "id":   // البحث برقم المخزن (WarehouseId = int)
                        if (int.TryParse(s, out var wid))
                        {
                            // لو كتب رقم صريح
                            q = q.Where(w => w.WarehouseId == wid);
                        }
                        else
                        {
                            // لو كتب نص وفيه أرقام جزئية
                            q = q.Where(w => w.WarehouseId.ToString().Contains(s));
                        }
                        break;

                    case "branch":   // البحث باسم الفرع
                        q = q.Where(w =>
                            w.Branch != null &&
                            w.Branch.BranchName.Contains(s));
                        break;

                    case "active":   // البحث بحالة الفعالية
                        // يسمح بـ "1/0" أو "نعم/لا" أو "true/false"
                        var yes = new[] { "1", "نعم", "yes", "true", "فعال" };
                        var no = new[] { "0", "لا", "no", "false", "غير" };

                        if (yes.Contains(s, StringComparer.OrdinalIgnoreCase))
                        {
                            q = q.Where(w => w.IsActive);
                        }
                        else if (no.Contains(s, StringComparer.OrdinalIgnoreCase))
                        {
                            q = q.Where(w => !w.IsActive);
                        }
                        else
                        {
                            // لو كتب كلمة مش مفهومة في active => لا نتيجة
                            q = q.Where(w => false);
                        }
                        break;

                    case "name":
                    default:         // البحث باسم المخزن
                        q = q.Where(w => w.WarehouseName.Contains(s));
                        break;
                }
            }

            // ===== فلترة بالتاريخ (تاريخ الإنشاء) =====
            if (useDateRange)
            {
                if (fromDate.HasValue)
                {
                    q = q.Where(w => w.CreatedAt >= fromDate.Value);
                }

                if (toDate.HasValue)
                {
                    q = q.Where(w => w.CreatedAt <= toDate.Value);
                }
            }

            // ===== الترتيب =====
            q = so switch
            {
                "id" => (desc
                    ? q.OrderByDescending(w => w.WarehouseId)
                    : q.OrderBy(w => w.WarehouseId)),

                "branch" => (desc
                    ? q.OrderByDescending(w => w.Branch!.BranchName)
                    : q.OrderBy(w => w.Branch!.BranchName)),

                "active" => (desc
                    ? q.OrderByDescending(w => w.IsActive).ThenBy(w => w.WarehouseName)
                    : q.OrderBy(w => w.IsActive).ThenBy(w => w.WarehouseName)),

                "created" => (desc
                    ? q.OrderByDescending(w => w.CreatedAt)
                    : q.OrderBy(w => w.CreatedAt)),

                "modified" => (desc
                    ? q.OrderByDescending(w => w.UpdatedAt ?? w.CreatedAt)
                    : q.OrderBy(w => w.UpdatedAt ?? w.CreatedAt)),

                "name" or _ => (desc
                    ? q.OrderByDescending(w => w.WarehouseName)
                    : q.OrderBy(w => w.WarehouseName)),
            };

            // ===== الترقيم =====
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 25;

            int total = await q.CountAsync();
            var items = await q.Skip((page - 1) * pageSize)
                               .Take(pageSize)
                               .ToListAsync();

            // إنشاء موديل PagedResult مع تخزين بيانات الفلترة/الترتيب
            var model = new PagedResult<Warehouse>(items, page, pageSize, total)
            {
                Search = s,               // نص البحث الحالي
                SearchBy = sb,              // الحقل الذي نبحث به
                SortColumn = so,              // عمود الترتيب الحالي
                SortDescending = desc,            // اتجاه الترتيب
                UseDateRange = useDateRange,    // هل فلترة التاريخ فعّالة؟
                FromDate = fromDate,        // من تاريخ
                ToDate = toDate           // إلى تاريخ
            };

            // خيارات البارشال (ابحث في / رتب حسب)
            ViewBag.SearchOptions = new[]
            {
                new SelectListItem("الاسم",    "name",   sb == "name"),
                new SelectListItem("المعرّف",  "id",     sb == "id"),
                new SelectListItem("الفرع",    "branch", sb == "branch"),
                new SelectListItem("الفعالية", "active", sb == "active"),
            };

            ViewBag.SortOptions = new[]
            {
                new SelectListItem("الاسم",          "name",     so == "name"),
                new SelectListItem("المعرّف",        "id",       so == "id"),
                new SelectListItem("الفرع",          "branch",   so == "branch"),
                new SelectListItem("الفعالية",       "active",   so == "active"),
                new SelectListItem("تاريخ الإنشاء", "created",  so == "created"),
                new SelectListItem("آخر تعديل",     "modified", so == "modified"),
            };

            // تمرير حالة الفلاتر للواجهة (للرجوع لها من ViewBag إذا احتجنا)
            ViewBag.Search = s;
            ViewBag.SearchBy = sb;
            ViewBag.Sort = so;
            ViewBag.Dir = desc ? "desc" : "asc";
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.Total = total;
            ViewBag.UseDateRange = useDateRange;
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;

            return View(model);
        }

        // =========================
        // Show — عرض تفاصيل مخزن
        // =========================
        [HttpGet]
        public async Task<IActionResult> Show(int id)
        {
            if (id <= 0) return NotFound();

            var w = await _db.Warehouses
                             .AsNoTracking()
                             .Include(x => x.Branch)
                             .FirstOrDefaultAsync(x => x.WarehouseId == id);

            if (w == null) return NotFound();

            return View(w);   // View بسيطة تعرض بيانات المخزن (نقدر نعملها بعدين)
        }

        // =========================
        // Create — إضافة مخزن
        // =========================
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            // تحميل الفروع للكومبو
            await LoadBranchesDDL(null);
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("WarehouseName,BranchId,IsActive,Notes")] Warehouse w)
        {
            // ملاحظة: لا نسمح بإدخال WarehouseId من الفورم لأنه Identity

            if (!ModelState.IsValid)
            {
                await LoadBranchesDDL(w.BranchId);
                return View(w);
            }

            // ضبط تاريخ الإنشاء صراحة (حتى لو له قيمة افتراضية في الموديل)
            w.CreatedAt = DateTime.UtcNow;
            w.UpdatedAt = null;

            _db.Warehouses.Add(w);
            await _db.SaveChangesAsync();

            TempData["Ok"] = "تمت إضافة المخزن بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // Edit — تعديل مخزن
        // =========================
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            if (id <= 0) return NotFound();

            var w = await _db.Warehouses.FindAsync(id);
            if (w == null) return NotFound();

            await LoadBranchesDDL(w.BranchId);
            return View(w);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("WarehouseId,WarehouseName,BranchId,IsActive,Notes")] Warehouse w)
        {
            if (id != w.WarehouseId) return NotFound();   // حماية من التلاعب في الفورم

            if (!ModelState.IsValid)
            {
                await LoadBranchesDDL(w.BranchId);
                return View(w);
            }

            // جلب النسخة الأصلية من الداتا بيز للحفاظ على CreatedAt
            var existing = await _db.Warehouses.FindAsync(id);
            if (existing == null) return NotFound();

            // تحديث الحقول المسموح بتعديلها فقط
            existing.WarehouseName = w.WarehouseName;
            existing.BranchId = w.BranchId;
            existing.IsActive = w.IsActive;
            existing.Notes = w.Notes;
            existing.UpdatedAt = DateTime.UtcNow;   // تسجيل وقت آخر تعديل

            await _db.SaveChangesAsync();

            TempData["Ok"] = "تم تعديل بيانات المخزن.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // Delete — حذف مخزن منفرد
        // =========================
        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            if (id <= 0) return NotFound();

            var w = await _db.Warehouses
                             .AsNoTracking()
                             .Include(x => x.Branch)
                             .FirstOrDefaultAsync(x => x.WarehouseId == id);

            if (w == null) return NotFound();

            return View(w);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var w = await _db.Warehouses.FindAsync(id);
            if (w == null) return NotFound();

            _db.Warehouses.Remove(w);
            await _db.SaveChangesAsync();

            TempData["Ok"] = "تم حذف السجل.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // BulkDelete — حذف مجموعة مخازن
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedIds)
        {
            // selectedIds تأتي من الفورم كقائمة "1,3,5,7"
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["Error"] = "لم يتم اختيار أي مخزن للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var ids = selectedIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x, out var n) ? n : (int?)null)
                .Where(n => n.HasValue)
                .Select(n => n!.Value)
                .ToList();

            if (!ids.Any())
            {
                TempData["Error"] = "قائمة المعرفات غير صحيحة.";
                return RedirectToAction(nameof(Index));
            }

            var warehouses = await _db.Warehouses
                                      .Where(w => ids.Contains(w.WarehouseId))
                                      .ToListAsync();

            if (warehouses.Any())
            {
                _db.Warehouses.RemoveRange(warehouses);
                await _db.SaveChangesAsync();
                TempData["Ok"] = $"تم حذف {warehouses.Count} مخزن/مخازن.";
            }
            else
            {
                TempData["Error"] = "لم يتم العثور على المخازن المحددة.";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================
        // DeleteAll — حذف كل المخازن
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _db.Warehouses.ToListAsync();

            if (!all.Any())
            {
                TempData["Ok"] = "لا توجد مخازن لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            _db.Warehouses.RemoveRange(all);
            await _db.SaveChangesAsync();

            TempData["Ok"] = "تم حذف جميع المخازن من النظام.";
            return RedirectToAction(nameof(Index));
        }





        // =========================
        // Export — تصدير قائمة المخازن (Excel أو CSV)
        // =========================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy = "name",   // name | id | branch | active
            string? sort = "name",   // name | id | branch | active
            string? dir = "asc",    // asc | desc
            string? format = "excel"   // excel | csv
        )
        {
            // الاستعلام الأساسي مع جلب الفرع
            IQueryable<Warehouse> q = _db.Warehouses
                                         .AsNoTracking()
                                         .Include(w => w.Branch);

            // ===== تنظيف قيم الفلاتر =====
            var s = (search ?? string.Empty).Trim();               // متغير: نص البحث
            var sb = (searchBy ?? "name").Trim().ToLowerInvariant();  // متغير: نوع البحث
            var so = (sort ?? "name").Trim().ToLowerInvariant();  // متغير: عمود الترتيب
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase); // متغير: هل الترتيب تنازلي؟

            // ===== البحث (نفس منطق Index) =====
            if (!string.IsNullOrEmpty(s))
            {
                switch (sb)
                {
                    case "id":
                        if (int.TryParse(s, out var wid))
                            q = q.Where(w => w.WarehouseId == wid);
                        else
                            q = q.Where(w => w.WarehouseId.ToString().Contains(s));
                        break;

                    case "branch":
                        q = q.Where(w => w.Branch != null &&
                                         w.Branch.BranchName.Contains(s));
                        break;

                    case "active":
                        var yes = new[] { "1", "نعم", "yes", "true", "فعال" };
                        var no = new[] { "0", "لا", "no", "false", "غير" };

                        if (yes.Contains(s, StringComparer.OrdinalIgnoreCase))
                            q = q.Where(w => w.IsActive);
                        else if (no.Contains(s, StringComparer.OrdinalIgnoreCase))
                            q = q.Where(w => !w.IsActive);
                        else
                            q = q.Where(w => false); // كلمة غير مفهومة
                        break;

                    case "name":
                    default:
                        q = q.Where(w => w.WarehouseName.Contains(s));
                        break;
                }
            }

            // ===== الترتيب (نفس منطق Index) =====
            q = so switch
            {
                "id" => (desc ? q.OrderByDescending(w => w.WarehouseId)
                              : q.OrderBy(w => w.WarehouseId)),

                "branch" => (desc ? q.OrderByDescending(w => w.Branch!.BranchName)
                                  : q.OrderBy(w => w.Branch!.BranchName)),

                "active" => (desc ? q.OrderByDescending(w => w.IsActive)
                                  : q.OrderBy(w => w.IsActive)),

                "name" or _ => (desc ? q.OrderByDescending(w => w.WarehouseName)
                                     : q.OrderBy(w => w.WarehouseName)),
            };

            // ===== جلب كل السجلات (بدون Paging) =====
            var rows = await q.ToListAsync();   // متغير: كل المخازن بعد الفلترة

            format = (format ?? "excel").Trim().ToLowerInvariant();

            // ============= CSV =============
            if (format == "csv")
            {
                var sbCsv = new StringBuilder();    // متغير: بناء نص CSV

                // عناوين الأعمدة
                sbCsv.AppendLine(string.Join(",",
                    Csv("كود المخزن"),
                    Csv("اسم المخزن"),
                    Csv("اسم الفرع"),
                    Csv("فعال؟"),
                    Csv("تاريخ الإنشاء"),
                    Csv("آخر تعديل"),
                    Csv("ملاحظات")
                ));

                // البيانات
                foreach (var w in rows)
                {
                    sbCsv.AppendLine(string.Join(",",
                        Csv(w.WarehouseId.ToString()),                   // كود المخزن
                        Csv(w.WarehouseName),                            // اسم المخزن
                        Csv(w.Branch?.BranchName),                       // اسم الفرع
                        Csv(w.IsActive ? "نشط" : "موقوف"),               // الحالة
                        Csv(w.CreatedAt.ToString("yyyy-MM-dd HH:mm")),   // تاريخ الإنشاء
                        Csv(w.UpdatedAt?.ToString("yyyy-MM-dd HH:mm")),   // آخر تعديل
                        Csv(w.Notes)                                     // الملاحظات
                    ));
                }

                var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true); // UTF-8 + BOM
                var bytes = utf8.GetBytes(sbCsv.ToString());
                var fileNameCsv = $"Warehouses_{DateTime.Now:yyyyMMdd_HHmm}_csv.csv";

                return File(bytes, "text/csv; charset=utf-8", fileNameCsv);
            }

            // ============= Excel =============
            using var workbook = new XLWorkbook();                 // متغير: ملف Excel
            var ws = workbook.Worksheets.Add("Warehouses");        // متغير: ورقة عمل

            int r = 1; // متغير: رقم الصف الحالي

            // عناوين الأعمدة
            ws.Cell(r, 1).Value = "كود المخزن";
            ws.Cell(r, 2).Value = "اسم المخزن";
            ws.Cell(r, 3).Value = "اسم الفرع";
            ws.Cell(r, 4).Value = "فعال؟";
            ws.Cell(r, 5).Value = "تاريخ الإنشاء";
            ws.Cell(r, 6).Value = "آخر تعديل";
            ws.Cell(r, 7).Value = "ملاحظات";

            var headerRange = ws.Range(r, 1, r, 7);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // البيانات
            foreach (var w in rows)
            {
                r++;

                ws.Cell(r, 1).Value = w.WarehouseId;
                ws.Cell(r, 2).Value = w.WarehouseName;
                ws.Cell(r, 3).Value = w.Branch?.BranchName ?? "";
                ws.Cell(r, 4).Value = w.IsActive ? "نشط" : "موقوف";
                ws.Cell(r, 5).Value = w.CreatedAt;
                ws.Cell(r, 6).Value = w.UpdatedAt;
                ws.Cell(r, 7).Value = w.Notes ?? "";
            }

            ws.Column(5).Style.DateFormat.Format = "yyyy-MM-dd HH:mm";
            ws.Column(6).Style.DateFormat.Format = "yyyy-MM-dd HH:mm";
            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();   // متغير: ستريم في الذاكرة
            workbook.SaveAs(stream);
            stream.Position = 0;

            var fileNameXlsx = $"Warehouses_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
            const string contentTypeXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

            return File(stream.ToArray(), contentTypeXlsx, fileNameXlsx);
        }

        // دالة مساعدة للـ CSV
        private static string Csv(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            var s = value.Replace("\"", "\"\""); // هروب علامة "

            if (s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
                return "\"" + s + "\"";

            return s;
        }





        // =========================
        // دالة مساعدة: تحميل قائمة الفروع للـ DDL
        // =========================
        private async Task LoadBranchesDDL(int? selectedId = null)
        {
            var branches = await _db.Branches
                                    .AsNoTracking()
                                    .OrderBy(b => b.BranchName)
                                    .ToListAsync();

            // BranchId الآن int — نمرره كما هو
            ViewBag.BranchId = new SelectList(branches, "BranchId", "BranchName", selectedId);
        }
    }
}
