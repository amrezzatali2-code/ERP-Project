using System;                                        // متغيرات التاريخ DateTime
using System.Collections.Generic;                    // Dictionary, List
using System.Linq;                                   // أوامر LINQ
using System.Linq.Expressions;                       // Expressions
using System.Text;                                   // StringBuilder للتصدير
using System.Threading.Tasks;                        // Task / async
using ERP.Data;                                      // AppDbContext
using ERP.Infrastructure;                            // PagedResult + ApplySearchSort
using ERP.Models;                                    // ProductBonusGroup
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Controllers
{
    /// <summary>
    /// إدارة جدول مجموعات الحوافز للأصناف (ProductBonusGroup)
    /// كل صف = مجموعة حافز لها اسم وقيمة حافز لكل علبة.
    /// </summary>
    public class ProductBonusGroupsController : Controller
    {
        private readonly AppDbContext _context;   // متغير: اتصال بقاعدة البيانات

        public ProductBonusGroupsController(AppDbContext context)
        {
            _context = context;
        }

        // =========================
        // دالة خاصة لبناء استعلام مجموعات الحوافز
        // (بحث + فلتر كود من/إلى + فلتر تاريخ اختياري + ترتيب)
        // =========================
        private IQueryable<ProductBonusGroup> BuildBonusGroupsQuery(
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
            IQueryable<ProductBonusGroup> q =
                _context.ProductBonusGroups.AsNoTracking();

            // 2) فلتر الكود من/إلى (على ProductBonusGroupId)
            if (fromCode.HasValue)
                q = q.Where(x => x.ProductBonusGroupId >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(x => x.ProductBonusGroupId <= toCode.Value);

            // 3) فلتر التاريخ (على CreatedAt) لو مفعّل
            if (useDateRange)
            {
                if (fromDate.HasValue)
                    q = q.Where(x => x.CreatedAt >= fromDate.Value);

                if (toDate.HasValue)
                    q = q.Where(x => x.CreatedAt <= toDate.Value);
            }

            // 4) الحقول النصية للبحث
            var stringFields =
                new Dictionary<string, Expression<Func<ProductBonusGroup, string?>>>()
                {
                    ["name"] = x => x.Name,                           // اسم المجموعة
                    ["status"] = x => x.IsActive ? "active" : "inactive",
                    ["desc"] = x => x.Description ?? ""              // الوصف
                };

            // 5) الحقول العددية للبحث (لو نص البحث رقم)
            var intFields =
                new Dictionary<string, Expression<Func<ProductBonusGroup, int>>>()
                {
                    ["id"] = x => x.ProductBonusGroupId               // كود المجموعة
                };

            // 6) حقول الترتيب في رأس الجدول
            var orderFields =
                new Dictionary<string, Expression<Func<ProductBonusGroup, object>>>()
                {
                    ["id"] = x => x.ProductBonusGroupId,
                    ["name"] = x => x.Name,
                    ["bonus"] = x => x.BonusAmount,
                    ["active"] = x => x.IsActive,
                    ["created"] = x => x.CreatedAt,
                    ["updated"] = x => x.UpdatedAt ?? x.CreatedAt
                };

            // 7) تطبيق البحث + الترتيب باستخدام الإكستنشن الموحد
            q = q.ApplySearchSort(
                search,                    // نص البحث
                searchBy,                  // نوع البحث
                sort,                      // عمود الترتيب
                dir,                       // اتجاه الترتيب asc/desc
                stringFields,
                intFields,
                orderFields,
                defaultSearchBy: "name",   // افتراضياً نبحث بالاسم
                defaultSortBy: "id"        // وافتراضياً نرتّب بالكود
            );

            return q;
        }

        // =========================
        // Index — قائمة مجموعات الحوافز
        // =========================
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "name",       // name | id | status | desc
            string? sort = "id",             // id | name | bonus | active | created | updated
            string? dir = "asc",             // asc | desc
            int page = 1,
            int pageSize = 25,
            int? fromCode = null,            // فلتر كود من
            int? toCode = null,              // فلتر كود إلى
            bool useDateRange = false,       // تفعيل فلتر التاريخ
            DateTime? fromDate = null,
            DateTime? toDate = null)
        {
            // بناء الاستعلام طبقاً للفلاتر
            var q = BuildBonusGroupsQuery(
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
            var model = await PagedResult<ProductBonusGroup>.CreateAsync(q, page, pageSize);

            // تعبئة خصائص البحث/الترتيب داخل الموديل (للاستخدام في الواجهة)
            model.Search = search ?? "";
            model.SearchBy = searchBy ?? "name";
            model.SortColumn = sort ?? "id";
            model.SortDescending = (dir?.ToLower() == "desc");
            model.UseDateRange = useDateRange;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            // تمرير فلتر الكود عن طريق ViewBag (مثل الجداول السابقة)
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.CodeFrom = fromCode;
            ViewBag.CodeTo = toCode;

            // حقل التاريخ المستخدم في الفلترة (للنموذج الموحد)
            ViewBag.DateField = "CreatedAt";

            return View(model);
        }

        // =========================
        // Export — تصدير مجموعات الحوافز (CSV)
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
            string? format = "excel")   // excel | csv (الاتنين CSV حالياً)
        {
            int? fromCode = codeFrom;
            int? toCode = codeTo;

            var q = BuildBonusGroupsQuery(
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
            sb.AppendLine("ProductBonusGroupId,Name,BonusAmount,IsActive,CreatedAt,UpdatedAt");

            // كل سطر = مجموعة حافز واحدة
            foreach (var x in list)
            {
                string createdText = x.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                string updatedText = x.UpdatedAt.HasValue
                    ? x.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm")
                    : "";

                var line = string.Join(",",
                    x.ProductBonusGroupId,
                    x.Name.Replace(",", " "),         // إزالة الفواصل من الاسم
                    x.BonusAmount.ToString("0.00"),
                    x.IsActive ? "Yes" : "No",
                    createdText,
                    updatedText
                );

                sb.AppendLine(line);
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var ext = (format ?? "excel").ToLower() == "csv" ? "csv" : "csv";
            var fileName = $"ProductBonusGroups_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}";

            return File(bytes, "text/csv", fileName);
        }

        // =========================
        // Details — عرض مجموعة حافز (قراءة فقط)
        // =========================
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
                return NotFound();

            var group = await _context.ProductBonusGroups
                                      .AsNoTracking()
                                      .FirstOrDefaultAsync(p => p.ProductBonusGroupId == id.Value);
            if (group == null)
                return NotFound();

            return View(group);
        }

        // =========================
        // Create — GET: شاشة إضافة مجموعة حافز جديدة
        // =========================
        public IActionResult Create()
        {
            return View();
        }

        // =========================
        // Create — POST: حفظ المجموعة الجديدة
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ProductBonusGroup group)
        {
            // تحقق إضافي: قيمة الحافز لا تكون سالبة
            if (group.BonusAmount < 0)
            {
                ModelState.AddModelError(nameof(ProductBonusGroup.BonusAmount),
                    "قيمة الحافز لكل علبة يجب ألا تقل عن صفر.");
            }

            if (!ModelState.IsValid)
                return View(group);

            group.CreatedAt = DateTime.Now;   // تثبيت تاريخ الإنشاء

            _context.ProductBonusGroups.Add(group);
            await _context.SaveChangesAsync();

            TempData["Msg"] = "تم إضافة مجموعة الحافز بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // Edit — GET: فتح مجموعة الحافز للتعديل
        // =========================
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
                return NotFound();

            var group = await _context.ProductBonusGroups
                                      .FirstOrDefaultAsync(p => p.ProductBonusGroupId == id.Value);
            if (group == null)
                return NotFound();

            return View(group);
        }

        // =========================
        // Edit — POST: حفظ التعديل
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ProductBonusGroup group)
        {
            if (id != group.ProductBonusGroupId)
                return NotFound();

            if (group.BonusAmount < 0)
            {
                ModelState.AddModelError(nameof(ProductBonusGroup.BonusAmount),
                    "قيمة الحافز لكل علبة يجب ألا تقل عن صفر.");
            }

            if (!ModelState.IsValid)
                return View(group);

            try
            {
                group.UpdatedAt = DateTime.Now;    // آخر تعديل
                _context.Update(group);
                await _context.SaveChangesAsync();

                TempData["Msg"] = "تم تعديل مجموعة الحافز بنجاح.";
            }
            catch (DbUpdateConcurrencyException)
            {
                bool exists = await _context.ProductBonusGroups
                                             .AnyAsync(e => e.ProductBonusGroupId == id);
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
        // Delete — GET: صفحة تأكيد الحذف
        // =========================
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
                return NotFound();

            var group = await _context.ProductBonusGroups
                                      .AsNoTracking()
                                      .FirstOrDefaultAsync(p => p.ProductBonusGroupId == id.Value);
            if (group == null)
                return NotFound();

            return View(group);
        }

        // =========================
        // Delete — POST: حذف مجموعة واحدة
        // =========================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var group = await _context.ProductBonusGroups
                                      .FirstOrDefaultAsync(p => p.ProductBonusGroupId == id);
            if (group == null)
                return NotFound();

            _context.ProductBonusGroups.Remove(group);
            await _context.SaveChangesAsync();

            TempData["Msg"] = "تم حذف مجموعة الحافز.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // BulkDelete — حذف مجموعة من المجموعات المحددة
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

            var groups = await _context.ProductBonusGroups
                                       .Where(p => ids.Contains(p.ProductBonusGroupId))
                                       .ToListAsync();

            if (!groups.Any())
            {
                TempData["Msg"] = "لم يتم العثور على المجموعات المحددة.";
                return RedirectToAction(nameof(Index));
            }

            _context.ProductBonusGroups.RemoveRange(groups);
            await _context.SaveChangesAsync();

            TempData["Msg"] = $"تم حذف {groups.Count} مجموعة حافز.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // DeleteAll — حذف جميع مجموعات الحافز (خطير)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var groups = await _context.ProductBonusGroups.ToListAsync();
            if (!groups.Any())
            {
                TempData["Msg"] = "لا توجد مجموعات حافز لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            _context.ProductBonusGroups.RemoveRange(groups);
            await _context.SaveChangesAsync();

            TempData["Msg"] = $"تم حذف جميع مجموعات الحافز ({groups.Count}).";
            return RedirectToAction(nameof(Index));
        }
    }
}
