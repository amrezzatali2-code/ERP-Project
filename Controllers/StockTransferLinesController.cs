using System;
using System.Collections.Generic;             // القوائم List
using System.Globalization;                   // تنسيق التاريخ فى Export
using System.Linq;                            // أوامر LINQ
using System.Text;                            // StringBuilder و Encoding
using System.Threading.Tasks;                 // async / await
using Microsoft.AspNetCore.Mvc;               // أساس الكنترولر
using Microsoft.EntityFrameworkCore;          // Include, AsNoTracking
using ERP.Data;                               // AppDbContext
using ERP.Filters;
using ERP.Infrastructure;                     // كلاس PagedResult للنظام الموحد
using ERP.Models;                             // الموديلات
using ERP.Security;

namespace ERP.Controllers
{
    /// <summary>
    /// إدارة جدول سطور التحويل بين المخازن (StockTransferLines)
    /// كل صف = صنف واحد ضمن تحويل بين مخزنين.
    /// </summary>
    [RequirePermission("StockTransferLines.Index")]
    public class StockTransferLinesController : Controller
    {
        private readonly AppDbContext _context;       // كائن الاتصال بقاعدة البيانات

        public StockTransferLinesController(AppDbContext context)
        {
            _context = context;
        }

        // =========================================================
        // دالة خاصة لتطبيق الفلاتر والترتيب على استعلام السطور
        // =========================================================
        private IQueryable<StockTransferLine> ApplyFiltersAndSorting(
            IQueryable<StockTransferLine> query,   // الاستعلام الأساسي
            string? search,                        // نص البحث
            string? searchBy,                      // نوع البحث
            string? sort,                          // عمود الترتيب
            ref bool descending,                   // اتجاه الترتيب
            bool useDateRange,                     // فلتر التاريخ
            DateTime? fromDate,                    // من تاريخ
            DateTime? toDate,                      // إلى تاريخ
            int? fromCode,                         // من كود (Id)
            int? toCode                            // إلى كود (Id)
        )
        {
            // 1) فلتر الكود من / إلى
            if (fromCode.HasValue)
                query = query.Where(l => l.Id >= fromCode.Value);

            if (toCode.HasValue)
                query = query.Where(l => l.Id <= toCode.Value);

            // 2) فلتر التاريخ (يعتمد على تاريخ التحويل فى الهيدر TransferDate)
            if (useDateRange)
            {
                if (fromDate.HasValue)
                    query = query.Where(l => l.StockTransfer!.TransferDate >= fromDate.Value);

                if (toDate.HasValue)
                    query = query.Where(l => l.StockTransfer!.TransferDate <= toDate.Value);
            }

            // 3) البحث النصّي
            if (!string.IsNullOrWhiteSpace(search))
            {
                searchBy = (searchBy ?? "all").ToLowerInvariant();
                string term = search.Trim();

                query = searchBy switch
                {
                    "id" => query.Where(l => l.Id.ToString().Contains(term)),
                    "transfer" => query.Where(l => l.StockTransferId.ToString().Contains(term)),
                    "product" => query.Where(l => l.ProductId.ToString().Contains(term)),
                    "note" => query.Where(l => l.Note != null && l.Note.Contains(term)),
                    _ => query.Where(l =>
                            l.Id.ToString().Contains(term) ||
                            l.StockTransferId.ToString().Contains(term) ||
                            l.ProductId.ToString().Contains(term) ||
                            (l.Note != null && l.Note.Contains(term)))
                };
            }

            // 4) الترتيب
            sort = string.IsNullOrWhiteSpace(sort) ? "id" : sort.ToLowerInvariant();

            query = (sort, descending) switch
            {
                ("id", false) => query.OrderBy(l => l.Id),
                ("id", true) => query.OrderByDescending(l => l.Id),

                ("transfer", false) => query.OrderBy(l => l.StockTransferId).ThenBy(l => l.Id),
                ("transfer", true) => query.OrderByDescending(l => l.StockTransferId).ThenByDescending(l => l.Id),

                ("product", false) => query.OrderBy(l => l.ProductId).ThenBy(l => l.Id),
                ("product", true) => query.OrderByDescending(l => l.ProductId).ThenByDescending(l => l.Id),

                ("qty", false) => query.OrderBy(l => l.Qty).ThenBy(l => l.Id),
                ("qty", true) => query.OrderByDescending(l => l.Qty).ThenByDescending(l => l.Id),

                ("date", false) => query.OrderBy(l => l.StockTransfer!.TransferDate).ThenBy(l => l.Id),
                ("date", true) => query.OrderByDescending(l => l.StockTransfer!.TransferDate).ThenByDescending(l => l.Id),

                ("fromwh", false) => query.OrderBy(l => l.StockTransfer!.FromWarehouseId).ThenBy(l => l.Id),
                ("fromwh", true) => query.OrderByDescending(l => l.StockTransfer!.FromWarehouseId).ThenByDescending(l => l.Id),

                ("towh", false) => query.OrderBy(l => l.StockTransfer!.ToWarehouseId).ThenBy(l => l.Id),
                ("towh", true) => query.OrderByDescending(l => l.StockTransfer!.ToWarehouseId).ThenByDescending(l => l.Id),

                ("note", false) => query.OrderBy(l => l.Note ?? "").ThenBy(l => l.Id),
                ("note", true) => query.OrderByDescending(l => l.Note ?? "").ThenByDescending(l => l.Id),

                _ when !descending => query.OrderBy(l => l.Id),
                _ => query.OrderByDescending(l => l.Id)
            };

            return query;
        }

        // =========================================================
        // Index — قائمة سطور التحويل بالنظام الموحد
        // =========================================================
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
            int? fromCode = null,
            int? toCode = null
        )
        {
            bool descending = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            // الاستعلام الأساسي: السطور + الهيدر (للحصول على تاريخ التحويل)
            IQueryable<StockTransferLine> baseQuery = _context.StockTransferLines
                .AsNoTracking()
                .Include(l => l.StockTransfer);

            // تطبيق الفلاتر والترتيب
            var query = ApplyFiltersAndSorting(
                baseQuery,
                search,
                searchBy,
                sort,
                ref descending,
                useDateRange,
                fromDate,
                toDate,
                fromCode,
                toCode);

            // PagedResult بالنظام الموحد
            var result = await PagedResult<StockTransferLine>.CreateAsync(
                query,
                page,
                pageSize,
                search ?? "",
                descending,
                sort ?? "id",
                searchBy ?? "all");

            // تعبئة الخواص المستخدمة في الواجهة
            result.Search = search ?? "";
            result.SearchBy = searchBy ?? "all";
            result.SortColumn = sort ?? "id";
            result.SortDescending = descending;
            result.UseDateRange = useDateRange;
            result.FromDate = fromDate;
            result.ToDate = toDate;

            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.Search = result.Search;
            ViewBag.SearchBy = result.SearchBy;
            ViewBag.Sort = result.SortColumn;
            ViewBag.Dir = result.SortDescending ? "desc" : "asc";

            return View(result);
        }

        // =========================================================
        // Details — عرض سطر واحد
        // =========================================================
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var line = await _context.StockTransferLines
                .Include(l => l.StockTransfer)
                .Include(l => l.Product)
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == id.Value);

            if (line == null) return NotFound();

            return View(line);
        }

        // =========================================================
        // Create — إضافة سطر جديد
        // =========================================================
        [HttpGet]
        public IActionResult Create()
        {
            // ممكن لاحقاً نضيف DropDown للـ StockTransfer و Product
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            [Bind("StockTransferId,ProductId,Qty,Note")] StockTransferLine line)
        {
            if (!ModelState.IsValid)
                return View(line);

            _context.StockTransferLines.Add(line);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "تم إضافة سطر التحويل بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // Edit — تعديل سطر
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var line = await _context.StockTransferLines.FindAsync(id.Value);
            if (line == null) return NotFound();

            return View(line);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            int id,
            [Bind("Id,StockTransferId,ProductId,Qty,Note")] StockTransferLine line)
        {
            if (id != line.Id) return NotFound();
            if (!ModelState.IsValid) return View(line);

            try
            {
                _context.Update(line);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "تم تعديل سطر التحويل بنجاح.";
            }
            catch (DbUpdateConcurrencyException)
            {
                bool exists = await _context.StockTransferLines.AnyAsync(e => e.Id == id);
                if (!exists) return NotFound();
                throw;
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // Delete — حذف سطر واحد
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var line = await _context.StockTransferLines
                .Include(l => l.StockTransfer)
                .Include(l => l.Product)
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == id.Value);

            if (line == null) return NotFound();

            return View(line);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var line = await _context.StockTransferLines.FindAsync(id);
            if (line != null)
            {
                _context.StockTransferLines.Remove(line);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم حذف سطر التحويل.";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // BulkDelete — حذف مجموعة من السطور المحددة
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["ErrorMessage"] = "لم يتم اختيار أى سطور للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var idStrings = selectedIds.Split(',', StringSplitOptions.RemoveEmptyEntries);

            var ids = new List<int>();
            foreach (var s in idStrings)
            {
                if (int.TryParse(s, out var id))
                    ids.Add(id);
            }

            if (ids.Count == 0)
            {
                TempData["ErrorMessage"] = "لم يتم العثور على أرقام صالحة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var lines = await _context.StockTransferLines
                .Where(l => ids.Contains(l.Id))
                .ToListAsync();

            if (lines.Count == 0)
            {
                TempData["ErrorMessage"] = "لم يتم العثور على السطور المحددة.";
                return RedirectToAction(nameof(Index));
            }

            _context.StockTransferLines.RemoveRange(lines);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"تم حذف {lines.Count} من سطور التحويل.";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // DeleteAll — حذف جميع السطور
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var allLines = await _context.StockTransferLines.ToListAsync();
            if (allLines.Count == 0)
            {
                TempData["ErrorMessage"] = "لا توجد سطور تحويل لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            _context.StockTransferLines.RemoveRange(allLines);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"تم حذف كل سطور التحويل ({allLines.Count}).";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // Export — تصدير السطور إلى CSV (مع معامل format زى النظام الموحد)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy = "all",
            string? sort = "id",
            string? dir = "asc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string format = "excel"   // excel | csv (الاتنين حالياً يخرجوا CSV)
        )
        {
            bool descending = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            IQueryable<StockTransferLine> baseQuery = _context.StockTransferLines
                .AsNoTracking()
                .Include(l => l.StockTransfer);

            var query = ApplyFiltersAndSorting(
                baseQuery,
                search,
                searchBy,
                sort,
                ref descending,
                useDateRange,
                fromDate,
                toDate,
                fromCode,
                toCode);

            var list = await query.ToListAsync();

            var sb = new StringBuilder();

            // عناوين الأعمدة في ملف CSV
            sb.AppendLine("Id,StockTransferId,TransferDate,ProductId,Qty,Note");

            foreach (var l in list)
            {
                var note = (l.Note ?? "").Replace(",", " ");
                var date = l.StockTransfer?.TransferDate
                           .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "";

                string line = string.Join(",",
                    l.Id,
                    l.StockTransferId,
                    date,
                    l.ProductId,
                    l.Qty,
                    note
                );

                sb.AppendLine(line);
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = "StockTransferLines.csv";
            const string contentType = "text/csv";

            return File(bytes, contentType, fileName);
        }
    }
}
