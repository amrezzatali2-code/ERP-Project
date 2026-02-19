using ERP.Data;                             // كائن الاتصال بقاعدة البيانات AppDbContext
using ERP.Infrastructure;                  // PagedResult + ApplySearchSort + UserActivityLogger
using ERP.Models;                          // ProductGroup, UserActionType
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;          // Dictionary
using System.Linq;
using System.Linq.Expressions;             // Expressions
using System.Text;                         // StringBuilder للتصدير
using System.Threading.Tasks;

namespace ERP.Controllers
{
    /// <summary>
    /// إدارة جدول مجموعات الأصناف (ProductGroup)
    /// كل صف = مجموعة أصناف لها اسم ووصف وحالة تفعيل.
    /// </summary>
    public class ProductGroupsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IUserActivityLogger _activityLogger;

        public ProductGroupsController(AppDbContext context, IUserActivityLogger activityLogger)
        {
            _context = context;
            _activityLogger = activityLogger;
        }

        // =========================
        // دالة خاصة لبناء استعلام مجموعات الأصناف
        // (بحث + فلتر كود من/إلى + فلتر تاريخ اختياري + ترتيب)
        // =========================
        private IQueryable<ProductGroup> BuildGroupsQuery(
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
            // 1) الاستعلام الأساسي (قراءة فقط لتحسين الأداء)
            IQueryable<ProductGroup> q =
                _context.ProductGroups.AsNoTracking();

            // 2) فلتر من/إلى (على ProductGroupId)
            if (fromCode.HasValue)
                q = q.Where(x => x.ProductGroupId >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(x => x.ProductGroupId <= toCode.Value);

            // 3) فلتر التاريخ (CreatedAt) لو مفعّل
            if (useDateRange)
            {
                if (fromDate.HasValue)
                    q = q.Where(x => x.CreatedAt >= fromDate.Value);

                if (toDate.HasValue)
                    q = q.Where(x => x.CreatedAt <= toDate.Value);
            }

            // 4) الحقول النصية للبحث
            var stringFields =
                new Dictionary<string, Expression<Func<ProductGroup, string?>>>()
                {
                    ["name"] = x => x.Name,          // البحث باسم المجموعة
                    ["desc"] = x => x.Description    // البحث في الوصف
                };

            // 5) الحقول العددية للبحث
            var intFields =
                new Dictionary<string, Expression<Func<ProductGroup, int>>>()
                {
                    ["id"] = x => x.ProductGroupId   // البحث بالكود
                };

            // 6) حقول الترتيب
            var orderFields =
                new Dictionary<string, Expression<Func<ProductGroup, object>>>()
                {
                    ["id"] = x => x.ProductGroupId,
                    ["name"] = x => x.Name,
                    ["active"] = x => x.IsActive,
                    ["created"] = x => x.CreatedAt
                };

            // 7) تطبيق البحث + الترتيب باستخدام الإكستنشن الموحد
            q = q.ApplySearchSort(
                search,
                searchBy,
                sort,
                dir,
                stringFields,
                intFields,
                orderFields,
                defaultSearchBy: "name",
                defaultSortBy: "id"
            );

            return q;
        }

        // =========================
        // Index — قائمة مجموعات الأصناف
        // =========================
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "name",      // name | id | desc
            string? sort = "id",            // id | name | active | created
            string? dir = "asc",
            int page = 1,
            int pageSize = 25,
            int? fromCode = null,
            int? toCode = null,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null)
        {
            var q = BuildGroupsQuery(
                search,
                searchBy,
                sort,
                dir,
                fromCode,
                toCode,
                useDateRange,
                fromDate,
                toDate);

            var model = await PagedResult<ProductGroup>.CreateAsync(q, page, pageSize);

            model.Search = search ?? "";
            model.SearchBy = searchBy ?? "name";
            model.SortColumn = sort ?? "id";
            model.SortDescending = (dir?.ToLower() == "desc");
            model.UseDateRange = useDateRange;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.CodeFrom = fromCode;
            ViewBag.CodeTo = toCode;

            ViewBag.DateField = "CreatedAt";

            return View(model);
        }

        // =========================
        // Export — تصدير CSV
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
            string? format = "excel")
        {
            int? fromCode = codeFrom;
            int? toCode = codeTo;

            var q = BuildGroupsQuery(
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
            sb.AppendLine("ProductGroupId,Name,Description,IsActive,CreatedAt");

            foreach (var g in list)
            {
                string createdText = g.CreatedAt.ToString("yyyy-MM-dd HH:mm");

                var line = string.Join(",",
                    g.ProductGroupId,
                    $"\"{g.Name}\"",
                    $"\"{g.Description}\"",
                    g.IsActive ? "Yes" : "No",
                    createdText
                );

                sb.AppendLine(line);
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var ext = (format ?? "excel").ToLower() == "csv" ? "csv" : "csv";
            var fileName = $"ProductGroups_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}";

            return File(bytes, "text/csv", fileName);
        }

        // =========================
        // Details — عرض التفاصيل
        // =========================
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
                return NotFound();

            var group = await _context.ProductGroups
                                      .AsNoTracking()
                                      .FirstOrDefaultAsync(g => g.ProductGroupId == id.Value);
            if (group == null)
                return NotFound();

            return View(group);
        }

        // =========================
        // Create — GET
        // =========================
        public IActionResult Create()
        {
            return View();
        }

        // =========================
        // Create — POST
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ProductGroup group)
        {
            if (!ModelState.IsValid)
                return View(group);

            group.CreatedAt = DateTime.Now;

            _context.ProductGroups.Add(group);
            await _context.SaveChangesAsync();

            await _activityLogger.LogAsync(UserActionType.Create, "ProductGroup", group.ProductGroupId, $"إنشاء مجموعة أصناف: {group.Name}");

            TempData["Msg"] = "تم إضافة مجموعة أصناف جديدة بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // Edit — GET
        // =========================
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
                return NotFound();

            var group = await _context.ProductGroups
                                      .FirstOrDefaultAsync(g => g.ProductGroupId == id.Value);
            if (group == null)
                return NotFound();

            return View(group);
        }

        // =========================
        // Edit — POST
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ProductGroup group)
        {
            if (id != group.ProductGroupId)
                return NotFound();

            if (!ModelState.IsValid)
                return View(group);

            try
            {
                group.UpdatedAt = DateTime.Now;
                _context.Update(group);
                await _context.SaveChangesAsync();

                await _activityLogger.LogAsync(UserActionType.Edit, "ProductGroup", id, $"تعديل مجموعة أصناف: {group.Name}");

                TempData["Msg"] = "تم تعديل مجموعة الأصناف بنجاح.";
            }
            catch (DbUpdateConcurrencyException)
            {
                bool exists = await _context.ProductGroups
                                            .AnyAsync(e => e.ProductGroupId == id);
                if (!exists)
                    return NotFound();

                ModelState.AddModelError(
                    string.Empty,
                    "تعذر الحفظ بسبب تعارض في التعديل. أعد تحميل الصفحة وحاول مرة أخرى.");
                return View(group);
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================
        // Delete — GET (تأكيد الحذف)
        // =========================
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
                return NotFound();

            var group = await _context.ProductGroups
                                      .AsNoTracking()
                                      .FirstOrDefaultAsync(g => g.ProductGroupId == id.Value);
            if (group == null)
                return NotFound();

            return View(group);
        }

        // =========================
        // Delete — POST
        // =========================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var group = await _context.ProductGroups
                                      .FirstOrDefaultAsync(g => g.ProductGroupId == id);
            if (group == null)
                return NotFound();

            _context.ProductGroups.Remove(group);
            await _context.SaveChangesAsync();

            await _activityLogger.LogAsync(UserActionType.Delete, "ProductGroup", id, $"حذف مجموعة أصناف: {group.Name}");

            TempData["Msg"] = "تم حذف مجموعة الأصناف.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // BulkDelete — حذف مجموعة مختارة
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["Msg"] = "لم يتم اختيار أي مجموعات للحذف.";
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

            var groups = await _context.ProductGroups
                                       .Where(g => ids.Contains(g.ProductGroupId))
                                       .ToListAsync();

            if (!groups.Any())
            {
                TempData["Msg"] = "لم يتم العثور على المجموعات المحددة.";
                return RedirectToAction(nameof(Index));
            }

            _context.ProductGroups.RemoveRange(groups);
            await _context.SaveChangesAsync();

            TempData["Msg"] = $"تم حذف {groups.Count} مجموعة أصناف.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // DeleteAll — حذف جميع المجموعات (بحذر)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var groups = await _context.ProductGroups.ToListAsync();
            if (!groups.Any())
            {
                TempData["Msg"] = "لا توجد مجموعات لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            _context.ProductGroups.RemoveRange(groups);
            await _context.SaveChangesAsync();

            TempData["Msg"] = $"تم حذف جميع مجموعات الأصناف ({groups.Count}).";
            return RedirectToAction(nameof(Index));
        }
    }
}
