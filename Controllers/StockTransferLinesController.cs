using System;
using System.Collections.Generic;             // القوائم List
using System.Globalization;                   // تنسيق التاريخ فى Export
using System.IO;                              // MemoryStream لتصدير Excel
using System.Linq;                            // أوامر LINQ
using System.Text;                            // StringBuilder و Encoding
using System.Threading.Tasks;                 // async / await
using ClosedXML.Excel;                        // تصدير Excel
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
        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

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
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_transfer = null,
            string? filterCol_date = null,
            string? filterCol_fromwh = null,
            string? filterCol_towh = null,
            string? filterCol_product = null,
            string? filterCol_qty = null,
            string? filterCol_note = null
        )
        {
            bool descending = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            // الاستعلام الأساسي: السطور + الهيدر (للحصول على تاريخ التحويل)
            IQueryable<StockTransferLine> baseQuery = _context.StockTransferLines
                .AsNoTracking()
                .Include(l => l.StockTransfer);

            // تطبيق الفلاتر العامة (بحث + كود + تاريخ + ترتيب)
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

            // فلاتر الأعمدة بنمط Excel
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    query = query.Where(l => ids.Contains(l.Id));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_transfer))
            {
                var ids = filterCol_transfer.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    query = query.Where(l => ids.Contains(l.StockTransferId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_product))
            {
                var ids = filterCol_product.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    query = query.Where(l => ids.Contains(l.ProductId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_qty))
            {
                var ids = filterCol_qty.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    query = query.Where(l => ids.Contains(l.Qty));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_note))
            {
                var vals = filterCol_note.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    query = query.Where(l => l.Note != null && vals.Contains(l.Note));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_date))
            {
                var dates = filterCol_date.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => DateTime.TryParse(x.Trim(), out var d) ? d.Date : (DateTime?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (dates.Count > 0)
                    query = query.Where(l => l.StockTransfer != null && dates.Contains(l.StockTransfer.TransferDate.Date));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_fromwh))
            {
                var ids = filterCol_fromwh.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    query = query.Where(l => l.StockTransfer != null && ids.Contains(l.StockTransfer.FromWarehouseId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_towh))
            {
                var ids = filterCol_towh.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    query = query.Where(l => l.StockTransfer != null && ids.Contains(l.StockTransfer.ToWarehouseId));
            }

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

            // تمرير فلاتر الأعمدة للواجهة
            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_Transfer = filterCol_transfer;
            ViewBag.FilterCol_Date = filterCol_date;
            ViewBag.FilterCol_FromWh = filterCol_fromwh;
            ViewBag.FilterCol_ToWh = filterCol_towh;
            ViewBag.FilterCol_Product = filterCol_product;
            ViewBag.FilterCol_Qty = filterCol_qty;
            ViewBag.FilterCol_Note = filterCol_note;

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
            string? filterCol_id = null,
            string? filterCol_transfer = null,
            string? filterCol_date = null,
            string? filterCol_fromwh = null,
            string? filterCol_towh = null,
            string? filterCol_product = null,
            string? filterCol_qty = null,
            string? filterCol_note = null,
            string format = "excel"   // excel | csv
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

            // فلاتر الأعمدة مثل Index
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    query = query.Where(l => ids.Contains(l.Id));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_transfer))
            {
                var ids = filterCol_transfer.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    query = query.Where(l => ids.Contains(l.StockTransferId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_product))
            {
                var ids = filterCol_product.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    query = query.Where(l => ids.Contains(l.ProductId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_qty))
            {
                var ids = filterCol_qty.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    query = query.Where(l => ids.Contains(l.Qty));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_note))
            {
                var vals = filterCol_note.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    query = query.Where(l => l.Note != null && vals.Contains(l.Note));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_date))
            {
                var dates = filterCol_date.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => DateTime.TryParse(x.Trim(), out var d) ? d.Date : (DateTime?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (dates.Count > 0)
                    query = query.Where(l => l.StockTransfer != null && dates.Contains(l.StockTransfer.TransferDate.Date));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_fromwh))
            {
                var ids = filterCol_fromwh.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    query = query.Where(l => l.StockTransfer != null && ids.Contains(l.StockTransfer.FromWarehouseId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_towh))
            {
                var ids = filterCol_towh.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    query = query.Where(l => l.StockTransfer != null && ids.Contains(l.StockTransfer.ToWarehouseId));
            }

            var list = await query.ToListAsync();

            format = (format ?? "excel").Trim().ToLowerInvariant();

            if (format == "csv")
            {
                var sb = new StringBuilder();

                // عناوين الأعمدة في ملف CSV
                sb.AppendLine("Id,StockTransferId,TransferDate,FromWarehouseId,ToWarehouseId,ProductId,Qty,Note");

                foreach (var l in list)
                {
                    var note = (l.Note ?? "").Replace(",", " ");
                    var date = l.StockTransfer?.TransferDate
                               .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "";
                    var fromWh = l.StockTransfer?.FromWarehouseId.ToString() ?? "";
                    var toWh = l.StockTransfer?.ToWarehouseId.ToString() ?? "";

                    string line = string.Join(",",
                        l.Id,
                        l.StockTransferId,
                        date,
                        fromWh,
                        toWh,
                        l.ProductId,
                        l.Qty,
                        note
                    );

                    sb.AppendLine(line);
                }

                var utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
                var bytesCsv = utf8Bom.GetBytes(sb.ToString());
                var fileNameCsv = $"StockTransferLines_{DateTime.Now:yyyyMMdd_HHmm}.csv";
                const string contentTypeCsv = "text/csv; charset=utf-8";

                return File(bytesCsv, contentTypeCsv, fileNameCsv);
            }
            else
            {
                using var workbook = new XLWorkbook();
                var ws = workbook.Worksheets.Add("StockTransferLines");

                int r = 1;
                ws.Cell(r, 1).Value = "كود السطر";
                ws.Cell(r, 2).Value = "رقم التحويل";
                ws.Cell(r, 3).Value = "تاريخ التحويل";
                ws.Cell(r, 4).Value = "من مخزن";
                ws.Cell(r, 5).Value = "إلى مخزن";
                ws.Cell(r, 6).Value = "كود الصنف";
                ws.Cell(r, 7).Value = "الكمية";
                ws.Cell(r, 8).Value = "ملاحظات السطر";

                var headerRange = ws.Range(r, 1, r, 8);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                foreach (var l in list)
                {
                    r++;
                    ws.Cell(r, 1).Value = l.Id;
                    ws.Cell(r, 2).Value = l.StockTransferId;
                    ws.Cell(r, 3).Value = l.StockTransfer?.TransferDate;
                    ws.Cell(r, 4).Value = l.StockTransfer?.FromWarehouseId;
                    ws.Cell(r, 5).Value = l.StockTransfer?.ToWarehouseId;
                    ws.Cell(r, 6).Value = l.ProductId;
                    ws.Cell(r, 7).Value = l.Qty;
                    ws.Cell(r, 8).Value = l.Note ?? "";
                }

                ws.Columns().AdjustToContents();
                ws.Column(3).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                stream.Position = 0;

                var fileNameXlsx = $"StockTransferLines_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
                const string contentTypeXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

                return File(stream.ToArray(), contentTypeXlsx, fileNameXlsx);
            }
        }
    }
}
