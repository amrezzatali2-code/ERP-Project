using ERP.Data;                             // كائن الاتصال بقاعدة البيانات AppDbContext
using ERP.Infrastructure;                  // كلاس PagedResult + ApplySearchSort
using ERP.Models;                          // الموديل StockAdjustment
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;          // Dictionary
using System.Linq;
using System.Linq.Expressions;            // Expressions
using System.Text;                        // StringBuilder للتصدير
using System.Threading.Tasks;

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إدارة تسويات الجرد (رأس التسوية فقط).
    /// كل سجل = تسوية واحدة على مخزن معيّن في تاريخ معيّن.
    /// </summary>
    public class StockAdjustmentsController : Controller
    {
        private readonly AppDbContext _context;   // متغير: الاتصال بقاعدة البيانات

        public StockAdjustmentsController(AppDbContext context)
        {
            _context = context;
        }

        // =========================
        // دالة خاصة لبناء استعلام التسويات
        // (بحث + فلتر كود من/إلى + فلتر تاريخ + ترتيب)
        // =========================
        private IQueryable<StockAdjustment> BuildAdjustmentsQuery(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            int? fromCode,
            int? toCode,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate)
        {
            // 1) الاستعلام الأساسي (قراءة فقط)
            IQueryable<StockAdjustment> q =
                _context.StockAdjustments
                        .AsNoTracking();

            // 2) فلتر الكود من/إلى (على كود التسوية Id)
            if (fromCode.HasValue)
                q = q.Where(x => x.Id >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(x => x.Id <= toCode.Value);

            // 3) فلتر التاريخ (على تاريخ التسوية AdjustmentDate)
            if (useDateRange)
            {
                if (fromDate.HasValue)
                    q = q.Where(x => x.AdjustmentDate >= fromDate.Value);

                if (toDate.HasValue)
                    q = q.Where(x => x.AdjustmentDate <= toDate.Value);
            }

            // 4) الحقول النصية للبحث
            var stringFields =
                new Dictionary<string, Expression<Func<StockAdjustment, string?>>>()
                {
                    ["reference"] = x => x.ReferenceNo ?? "",
                    ["reason"] = x => x.Reason ?? ""
                };

            // 5) الحقول العددية للبحث
            var intFields =
                new Dictionary<string, Expression<Func<StockAdjustment, int>>>()
                {
                    ["id"] = x => x.Id,
                    ["warehouse"] = x => x.WarehouseId
                };

            // 6) حقول الترتيب
            var orderFields =
                new Dictionary<string, Expression<Func<StockAdjustment, object>>>()
                {
                    ["id"] = x => x.Id,
                    ["date"] = x => x.AdjustmentDate,
                    ["warehouse"] = x => x.WarehouseId,
                    ["created"] = x => x.CreatedAt
                };

            // 7) تطبيق البحث + الترتيب باستخدام الإكستنشن الموحد
            q = q.ApplySearchSort(
                search,                    // نص البحث
                searchBy,                  // الحقل المختار للبحث
                sort,                      // اسم العمود للترتيب
                dir,                       // asc / desc
                stringFields,
                intFields,
                orderFields,
                defaultSearchBy: "id",     // البحث الافتراضي بالكود
                defaultSortBy: "id"        // الترتيب الافتراضي بالكود
            );

            return q;
        }

        // =========================
        // Index — قائمة تسويات الجرد
        // =========================
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "id",        // id | warehouse | reference | reason
            string? sort = "id",            // id | date | warehouse | created
            string? dir = "asc",            // asc | desc
            int page = 1,
            int pageSize = 25,
            int? fromCode = null,           // فلتر كود من
            int? toCode = null,             // فلتر كود إلى
            bool useDateRange = false,      // تفعيل فلتر التاريخ
            DateTime? fromDate = null,
            DateTime? toDate = null)
        {
            // بناء الاستعلام طبقاً للفلاتر
            var q = BuildAdjustmentsQuery(
                search,
                searchBy,
                sort,
                dir,
                fromCode,
                toCode,
                useDateRange,
                fromDate,
                toDate);

            // تقسيم الصفحات
            var model = await PagedResult<StockAdjustment>.CreateAsync(q, page, pageSize);

            // تعبئة خصائص البحث/الترتيب داخل الموديل (للاستخدام في الواجهة)
            model.Search = search ?? "";
            model.SearchBy = searchBy ?? "id";
            model.SortColumn = sort ?? "id";
            model.SortDescending = (dir?.ToLower() == "desc");
            model.UseDateRange = useDateRange;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            // تمرير فلتر الكود عن طريق ViewBag
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.CodeFrom = fromCode;
            ViewBag.CodeTo = toCode;

            // حقل التاريخ المستخدم في الفلترة (للعرض فقط في المودال)
            ViewBag.DateField = "AdjustmentDate";

            return View(model);
        }

        // =========================
        // Export — تصدير تسويات الجرد (CSV يفتح في Excel)
        // =========================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            int? codeFrom,
            int? codeTo,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? format = "excel")   // excel | csv (الاثنين CSV حالياً)
        {
            int? fromCode = codeFrom;
            int? toCode = codeTo;

            var q = BuildAdjustmentsQuery(
                search,
                searchBy,
                sort,
                dir,
                fromCode,
                toCode,
                useDateRange,
                fromDate,
                toDate);

            var list = await q.ToListAsync();

            var sb = new StringBuilder();

            // عناوين الأعمدة
            sb.AppendLine("Id,AdjustmentDate,WarehouseId,ReferenceNo,Reason,CreatedAt");

            // كل سطر = تسوية واحدة
            foreach (var x in list)
            {
                string dateText = x.AdjustmentDate.ToString("yyyy-MM-dd");
                string createdText = x.CreatedAt.ToString("yyyy-MM-dd HH:mm");

                var line = string.Join(",",
                    x.Id,
                    dateText,
                    x.WarehouseId,
                    (x.ReferenceNo ?? "").Replace(",", " "),
                    (x.Reason ?? "").Replace(",", " "),
                    createdText
                );

                sb.AppendLine(line);
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var ext = (format ?? "excel").ToLower() == "csv" ? "csv" : "csv";
            var fileName = $"StockAdjustments_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}";

            return File(bytes, "text/csv", fileName);
        }

        // =========================
        // Details — عرض رأس التسوية (قراءة فقط)
        // =========================
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
                return NotFound();

            var adjustment = await _context.StockAdjustments
                                           .AsNoTracking()
                                           .FirstOrDefaultAsync(a => a.Id == id.Value);

            if (adjustment == null)
                return NotFound();

            return View(adjustment);
        }

        // =========================
        // Create — GET: شاشة إضافة تسوية جديدة
        // =========================
        public IActionResult Create()
        {
            // ممكن نضبط التاريخ الافتراضي لليوم
            var model = new StockAdjustment
            {
                AdjustmentDate = DateTime.Today
            };

            return View(model);
        }

        // =========================
        // Create — POST: حفظ التسوية الجديدة
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(StockAdjustment adjustment)
        {
            // تحقق بسيط: المخزن لازم يكون رقم صحيح > 0
            if (adjustment.WarehouseId <= 0)
            {
                ModelState.AddModelError(
                    nameof(StockAdjustment.WarehouseId),
                    "من فضلك أدخل كود مخزن صحيح."
                );
            }

            if (!ModelState.IsValid)
                return View(adjustment);

            // تاريخ الإنشاء
            adjustment.CreatedAt = DateTime.UtcNow;

            _context.StockAdjustments.Add(adjustment);
            await _context.SaveChangesAsync();

            TempData["Msg"] = "تم إضافة تسوية جرد جديدة بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // Edit — GET: فتح التسوية للتعديل
        // =========================
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
                return NotFound();

            var adjustment = await _context.StockAdjustments
                                           .FirstOrDefaultAsync(a => a.Id == id.Value);

            if (adjustment == null)
                return NotFound();

            return View(adjustment);
        }

        // =========================
        // Edit — POST: حفظ التعديل
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, StockAdjustment adjustment)
        {
            if (id != adjustment.Id)
                return NotFound();

            if (adjustment.WarehouseId <= 0)
            {
                ModelState.AddModelError(
                    nameof(StockAdjustment.WarehouseId),
                    "من فضلك أدخل كود مخزن صحيح."
                );
            }

            if (!ModelState.IsValid)
                return View(adjustment);

            try
            {
                adjustment.UpdatedAt = DateTime.UtcNow;
                _context.Update(adjustment);
                await _context.SaveChangesAsync();

                TempData["Msg"] = "تم تعديل التسوية بنجاح.";
            }
            catch (DbUpdateConcurrencyException)
            {
                bool exists = await _context.StockAdjustments
                                            .AnyAsync(e => e.Id == id);
                if (!exists)
                    return NotFound();

                ModelState.AddModelError(
                    string.Empty,
                    "تعذر حفظ التعديل بسبب تعارض في البيانات. أعد تحميل الصفحة وحاول مرة أخرى."
                );
                return View(adjustment);
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================
        // Delete — GET: تأكيد الحذف
        // =========================
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
                return NotFound();

            var adjustment = await _context.StockAdjustments
                                           .AsNoTracking()
                                           .FirstOrDefaultAsync(a => a.Id == id.Value);

            if (adjustment == null)
                return NotFound();

            return View(adjustment);
        }

        // =========================
        // Delete — POST: حذف تسوية واحدة
        // =========================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var adjustment = await _context.StockAdjustments
                                           .FirstOrDefaultAsync(a => a.Id == id);

            if (adjustment == null)
                return NotFound();

            _context.StockAdjustments.Remove(adjustment);
            await _context.SaveChangesAsync();

            TempData["Msg"] = "تم حذف التسوية بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // BulkDelete — حذف مجموعة تسويات محددة
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["Msg"] = "لم يتم اختيار أي تسويات للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var ids = selectedIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var n) ? (int?)n : null)
                .Where(n => n.HasValue)
                .Select(n => n!.Value)
                .ToList();

            if (!ids.Any())
            {
                TempData["Msg"] = "لم يتم اختيار أكواد صحيحة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var adjustments = await _context.StockAdjustments
                                            .Where(a => ids.Contains(a.Id))
                                            .ToListAsync();

            if (!adjustments.Any())
            {
                TempData["Msg"] = "لم يتم العثور على التسويات المحددة.";
                return RedirectToAction(nameof(Index));
            }

            _context.StockAdjustments.RemoveRange(adjustments);
            await _context.SaveChangesAsync();

            TempData["Msg"] = $"تم حذف {adjustments.Count} تسوية.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // DeleteAll — حذف جميع التسويات (خطير)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var adjustments = await _context.StockAdjustments.ToListAsync();
            if (!adjustments.Any())
            {
                TempData["Msg"] = "لا توجد تسويات لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            _context.StockAdjustments.RemoveRange(adjustments);
            await _context.SaveChangesAsync();

            TempData["Msg"] = $"تم حذف جميع تسويات الجرد ({adjustments.Count}).";
            return RedirectToAction(nameof(Index));
        }
    }
}
