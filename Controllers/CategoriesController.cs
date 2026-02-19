using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;         // SelectListItem
using Microsoft.EntityFrameworkCore;
using ERP.Data;
using ERP.Models;
using ERP.Infrastructure;                         // PagedResult + UserActivityLogger
using System.IO;                 // MemoryStream
using System.Text;               // StringBuilder + Encoding للـ CSV
using System.Globalization;      // CultureInfo لو احتجنا تنسيق أرقام
using ClosedXML.Excel;           // مكتبة Excel


namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر فئات الأصناف — عرض/بحث/ترتيب/ترقيم + إضافة/تعديل/حذف
    /// باستخدام نظام القوائم الموحد مع فورم SHOW.
    /// </summary>
    public class CategoriesController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IUserActivityLogger _activityLogger;

        public CategoriesController(AppDbContext db, IUserActivityLogger activityLogger)
        {
            _db = db;
            _activityLogger = activityLogger;
        }

        // =========================
        // Index — قائمة الفئات مع البحث/الترتيب/الترقيم + فلترة بالتاريخ
        // =========================
        [HttpGet]
        public async Task<IActionResult> Index(
            string? search,               // نص البحث
            string? searchBy = "name", // name | id | all
            string? sort = "name", // name | id | created | modified
            string? dir = "asc",  // asc | desc
            int page = 1,      // رقم الصفحة
            int pageSize = 25,     // حجم الصفحة
            bool useDateRange = false, // تفعيل فلترة التاريخ
            DateTime? fromDate = null,   // من تاريخ (CreatedAt)
            DateTime? toDate = null    // إلى تاريخ
        )
        {
            // (1) الاستعلام الأساسي من جدول الفئات بدون تتبّع لزيادة سرعة القراءة
            IQueryable<Category> q = _db.Categories.AsNoTracking();

            // (2) تنظيف قيم الفلاتر
            var s = (search ?? "").Trim();                 // نص البحث بعد إزالة المسافات
            var sb = (searchBy ?? "name").Trim().ToLower();   // نوع البحث
            var so = (sort ?? "name").Trim().ToLower();   // عمود الترتيب
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            // (3) تطبيق البحث
            if (!string.IsNullOrEmpty(s))
            {
                switch (sb)
                {
                    case "id":
                        // بحث برقم الفئة (CategoryId كـ int)
                        if (int.TryParse(s, out int idValue))
                        {
                            q = q.Where(x => x.CategoryId == idValue);
                        }
                        else
                        {
                            // لو المستخدم كتب نص مش رقم في خانة رقم الفئة → لا نتائج
                            q = q.Where(x => 1 == 0);
                        }
                        break;

                    case "all":
                        // بحث في الاسم + تحويل رقم الفئة إلى نص ثم البحث فيه
                        q = q.Where(x =>
                            x.CategoryName.Contains(s) ||
                            x.CategoryId.ToString().Contains(s));
                        break;

                    case "name":
                    default:
                        // بحث بالاسم فقط
                        q = q.Where(x => x.CategoryName.Contains(s));
                        break;
                }
            }

            // (4) فلترة بالتاريخ (تاريخ الإنشاء CreatedAt)
            if (useDateRange)
            {
                if (fromDate.HasValue)
                {
                    q = q.Where(x => x.CreatedAt >= fromDate.Value);
                }

                if (toDate.HasValue)
                {
                    q = q.Where(x => x.CreatedAt <= toDate.Value);
                }
            }

            // (5) الترتيب
            q = so switch
            {
                "id" => (desc
                    ? q.OrderByDescending(x => x.CategoryId)
                    : q.OrderBy(x => x.CategoryId)),

                "created" => (desc
                    ? q.OrderByDescending(x => x.CreatedAt)
                    : q.OrderBy(x => x.CreatedAt)),

                "modified" => (desc
                    ? q.OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
                    : q.OrderBy(x => x.UpdatedAt ?? x.CreatedAt)),

                "name" or _ => (desc
                    ? q.OrderByDescending(x => x.CategoryName)
                    : q.OrderBy(x => x.CategoryName)),
            };

            // (6) الترقيم (Paging)
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 25;

            int total = await q.CountAsync();   // إجمالي عدد الفئات بعد الفلترة
            var items = await q
                .Skip((page - 1) * pageSize)    // تخطي السطور السابقة
                .Take(pageSize)                 // أخذ سطور الصفحة الحالية
                .ToListAsync();

            // إنشاء PagedResult مع تخزين قيم الفلاتر والتاريخ بداخله
            var model = new PagedResult<Category>(items, page, pageSize, total)
            {
                Search = s,
                SearchBy = sb,
                SortColumn = so,
                SortDescending = desc,
                UseDateRange = useDateRange,
                FromDate = fromDate,
                ToDate = toDate
            };

            // (7) خيارات البارشال _IndexFilters (اختيارات البحث)
            ViewBag.SearchOptions = new[]
            {
                new SelectListItem("الكل",   "all",  sb == "all"),
                new SelectListItem("الاسم", "name", sb == "name"),
                new SelectListItem("الرقم", "id",   sb == "id"),
            };

            // خيارات الترتيب
            ViewBag.SortOptions = new[]
            {
                new SelectListItem("الاسم",          "name",     so == "name"),
                new SelectListItem("الرقم",          "id",       so == "id"),
                new SelectListItem("تاريخ الإنشاء", "created",  so == "created"),
                new SelectListItem("آخر تعديل",     "modified", so == "modified"),
            };

            // تمرير القيم الحالية للواجهة (لو احتجناها من ViewBag)
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
        // Show — عرض تفاصيل فئة (لـ فورم SHOW)
        // =========================
        [HttpGet]
        public async Task<IActionResult> Show(int id)
        {
            var cat = await _db.Categories
                               .AsNoTracking()
                               .FirstOrDefaultAsync(x => x.CategoryId == id);
            if (cat == null) return NotFound();

            return View(cat);   // نعمل View باسم Show لاحقاً أو نستخدمه كعرض بسيط
        }

        // (اختياري) الإبقاء على Details لو مستخدم في حتة تانية
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var cat = await _db.Categories
                               .AsNoTracking()
                               .FirstOrDefaultAsync(x => x.CategoryId == id);
            if (cat == null) return NotFound();
            return View(cat);
        }

        // =========================
        // Create — إضافة فئة جديدة
        // =========================
        [HttpGet]
        public IActionResult Create() => View();  // عرض فورم الإضافة

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("CategoryName")] Category cat)
        {
            // تعليق: CategoryId رقم هوية Identity — لا يدخله المستخدم

            if (!ModelState.IsValid) return View(cat);

            // حماية اختيارية: منع تكرار اسم الفئة
            bool nameExists = await _db.Categories
                                       .AnyAsync(x => x.CategoryName == cat.CategoryName);
            if (nameExists)
            {
                ModelState.AddModelError(nameof(cat.CategoryName), "اسم الفئة موجود بالفعل.");
                return View(cat);
            }

            // ضبط التواريخ
            cat.CreatedAt = DateTime.UtcNow;
            cat.UpdatedAt = null;

            _db.Categories.Add(cat);
            await _db.SaveChangesAsync();

            await _activityLogger.LogAsync(UserActionType.Create, "Category", cat.CategoryId, $"إنشاء فئة جديدة: {cat.CategoryName}");

            TempData["Ok"] = "تمت إضافة الفئة بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // Edit — تعديل فئة
        // =========================
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var cat = await _db.Categories.FindAsync(id);
            if (cat == null) return NotFound();
            return View(cat);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("CategoryId,CategoryName")] Category cat)
        {
            if (id != cat.CategoryId) return NotFound();   // حماية من التلاعب في الـ Id

            if (!ModelState.IsValid) return View(cat);

            // جلب السجل الأصلي للحفاظ على CreatedAt
            var existing = await _db.Categories.FindAsync(id);
            if (existing == null) return NotFound();

            // حماية اختيارية: منع تكرار اسم الفئة مع فئات أخرى
            bool nameExists = await _db.Categories
                                       .AnyAsync(x => x.CategoryId != id &&
                                                      x.CategoryName == cat.CategoryName);
            if (nameExists)
            {
                ModelState.AddModelError(nameof(cat.CategoryName), "اسم الفئة موجود بالفعل.");
                return View(cat);
            }

            var oldName = existing.CategoryName;
            existing.CategoryName = cat.CategoryName;
            existing.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            await _activityLogger.LogAsync(
                UserActionType.Edit,
                "Category",
                existing.CategoryId,
                $"تعديل فئة: {cat.CategoryName}",
                System.Text.Json.JsonSerializer.Serialize(new { CategoryName = oldName }),
                System.Text.Json.JsonSerializer.Serialize(new { CategoryName = existing.CategoryName }));

            TempData["Ok"] = "تم تعديل بيانات الفئة.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // Delete — حذف فئة مفردة
        // =========================
        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            var cat = await _db.Categories
                               .AsNoTracking()
                               .FirstOrDefaultAsync(x => x.CategoryId == id);
            if (cat == null) return NotFound();

            return View(cat);   // صفحة تأكيد الحذف
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var cat = await _db.Categories.FindAsync(id);
            if (cat == null) return NotFound();

            try
            {
                var oldValues = System.Text.Json.JsonSerializer.Serialize(new { cat.CategoryName });
                _db.Categories.Remove(cat);
                await _db.SaveChangesAsync();

                await _activityLogger.LogAsync(UserActionType.Delete, "Category", id, $"حذف فئة: {cat.CategoryName}", oldValues: oldValues);

                TempData["Ok"] = "تم حذف السجل.";
            }
            catch (DbUpdateException)
            {
                // في حالة وجود أصناف مرتبطة بهذه الفئة
                TempData["Err"] = "لا يمكن الحذف لوجود بيانات مرتبطة (مثل أصناف ضمن هذه الفئة).";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================
        // BulkDelete — حذف مجموعة فئات (حذف المحدد)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["Err"] = "لم يتم اختيار أي فئة للحذف.";
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
                TempData["Err"] = "قائمة المعرفات غير صحيحة.";
                return RedirectToAction(nameof(Index));
            }

            var cats = await _db.Categories
                                .Where(x => ids.Contains(x.CategoryId))
                                .ToListAsync();

            if (!cats.Any())
            {
                TempData["Err"] = "لم يتم العثور على الفئات المحددة.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _db.Categories.RemoveRange(cats);
                await _db.SaveChangesAsync();
                TempData["Ok"] = $"تم حذف {cats.Count} فئة/فئات.";
            }
            catch (DbUpdateException)
            {
                TempData["Err"] = "لا يمكن حذف بعض الفئات لوجود أصناف مرتبطة بها.";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================
        // DeleteAll — حذف جميع الفئات
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _db.Categories.ToListAsync();

            if (!all.Any())
            {
                TempData["Ok"] = "لا توجد فئات لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _db.Categories.RemoveRange(all);
                await _db.SaveChangesAsync();
                TempData["Ok"] = "تم حذف جميع الفئات من النظام.";
            }
            catch (DbUpdateException)
            {
                TempData["Err"] = "تعذر حذف جميع الفئات لوجود بيانات مرتبطة بها.";
            }

            return RedirectToAction(nameof(Index));
        }




        // =========================
// Export — تصدير قائمة الفئات (Excel أو CSV)
// =========================
[HttpGet]
public async Task<IActionResult> Export(
    string? search,
    string? searchBy = "name",   // name | id | all
    string? sort     = "name",   // name | id
    string? dir      = "asc",    // asc | desc
    string? format   = "excel"   // excel | csv
)
{
    // الاستعلام الأساسي
    IQueryable<Category> q = _db.Categories.AsNoTracking();

    // ===== تنظيف الفلاتر =====
    var s  = (search   ?? "").Trim();                     // نص البحث بعد التنظيف
    var sb = (searchBy ?? "name").Trim().ToLower();       // نوع البحث
    var so = (sort     ?? "name").Trim().ToLower();       // عمود الترتيب
    bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase); // ترتيب تنازلي؟

    // ===== البحث (نفس منطق Index) =====
    if (!string.IsNullOrEmpty(s))
    {
        switch (sb)
        {
            case "id":
                if (int.TryParse(s, out int idValue))
                {
                    q = q.Where(x => x.CategoryId == idValue);
                }
                else
                {
                    q = q.Where(x => x.CategoryId.ToString().Contains(s));
                }
                break;

            case "all":
                q = q.Where(x =>
                    x.CategoryName.Contains(s) ||
                    x.CategoryId.ToString().Contains(s));
                break;

            case "name":
            default:
                q = q.Where(x => x.CategoryName.Contains(s));
                break;
        }
    }

    // ===== الترتيب (نفس منطق Index) =====
    q = so switch
    {
        "id" => (desc
                    ? q.OrderByDescending(x => x.CategoryId)
                    : q.OrderBy(x => x.CategoryId)),

        "name" or _ => (desc
                    ? q.OrderByDescending(x => x.CategoryName)
                    : q.OrderBy(x => x.CategoryName)),
    };

    // ===== جلب كل السجلات (بدون Paging) =====
    var rows = await q.ToListAsync();   // متغير: قائمة الفئات للتصدير

    // توحيد قيمة format
    format = (format ?? "excel").Trim().ToLowerInvariant();

    // =====================================
    // الفرع الأول: CSV
    // =====================================
    if (format == "csv")
    {
        var sbCsv = new StringBuilder();   // متغير: نص CSV

        // عناوين الأعمدة
        sbCsv.AppendLine(string.Join(",",
            Csv("كود الفئة"),
            Csv("اسم الفئة"),
            Csv("تاريخ الإنشاء"),
            Csv("آخر تعديل")
        ));

        // البيانات
        foreach (var c in rows)
        {
            sbCsv.AppendLine(string.Join(",",
                Csv(c.CategoryId.ToString()),
                Csv(c.CategoryName),
                Csv(c.CreatedAt.ToString("yyyy-MM-dd HH:mm")),
                Csv(c.UpdatedAt?.ToString("yyyy-MM-dd HH:mm"))
            ));
        }

        var utf8  = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var bytes = utf8.GetBytes(sbCsv.ToString());
        var fileNameCsv = $"Categories_{DateTime.Now:yyyyMMdd_HHmm}_csv.csv";

        return File(bytes, "text/csv; charset=utf-8", fileNameCsv);
    }

    // =====================================
    // الفرع الثاني: Excel (XLSX)
// =====================================
    using var workbook = new XLWorkbook();                 // متغير: ملف Excel
    var ws = workbook.Worksheets.Add("Categories");        // متغير: ورقة العمل

    int r = 1; // متغير: رقم الصف الحالي

    // عناوين الأعمدة
    ws.Cell(r, 1).Value = "كود الفئة";
    ws.Cell(r, 2).Value = "اسم الفئة";
    ws.Cell(r, 3).Value = "تاريخ الإنشاء";
    ws.Cell(r, 4).Value = "آخر تعديل";

    // تنسيق العناوين
    var headerRange = ws.Range(r, 1, r, 4);
    headerRange.Style.Font.Bold = true;
    headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

    // البيانات
    foreach (var c in rows)
    {
        r++;

        ws.Cell(r, 1).Value = c.CategoryId;
        ws.Cell(r, 2).Value = c.CategoryName;
        ws.Cell(r, 3).Value = c.CreatedAt;
        ws.Cell(r, 4).Value = c.UpdatedAt;
    }

    // تنسيق التاريخ + ضبط عرض الأعمدة
    ws.Column(3).Style.DateFormat.Format = "yyyy-MM-dd HH:mm";
    ws.Column(4).Style.DateFormat.Format = "yyyy-MM-dd HH:mm";
    ws.Columns().AdjustToContents();

    using var stream = new MemoryStream();   // متغير: ذاكرة مؤقتة
    workbook.SaveAs(stream);
    stream.Position = 0;

    var fileNameXlsx = $"Categories_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
    const string contentTypeXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    return File(stream.ToArray(), contentTypeXlsx, fileNameXlsx);
}

// دالة Csv لو مش موجودة فوق (لو حابب تعيد استخدامها من كنترولر تاني، ينفع برضه)
private static string Csv(string? value)
{
    if (string.IsNullOrEmpty(value))
        return "";

    var s = value.Replace("\"", "\"\"");

    if (s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
        return "\"" + s + "\"";

    return s;
}

    }
}
