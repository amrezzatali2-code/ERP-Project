using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ClosedXML.Excel;                 // تصدير Excel
using ERP.Data;                        // AppDbContext
using ERP.Filters;
using ERP.Infrastructure;              // PagedResult
using ERP.Models;                      // StockBatch / Product / Warehouse
using ERP.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Controllers
{
    /// <summary>
    /// كونترولر شاشة Stock_Batches (رصيد سريع لكل تشغيلة داخل مخزن)
    /// - Index فقط (نظام القوائم الموحد)
    /// - BulkDelete / DeleteAll
    /// - Export (Excel / CSV)
    /// </summary>
    [RequirePermission("StockBatches.Index")]
    public class StockBatchesController : Controller
    {
        private readonly AppDbContext _db; // متغير: DbContext

        public StockBatchesController(AppDbContext context)
        {
            _db = context;
        }

        // =========================================================
        // دالة مساعدة: تطبيق البحث / الفلترة / الترتيب
        // =========================================================
        private IQueryable<StockBatch> SearchSortFilter(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            int? fromCode,
            int? toCode)
        {
            // الاستعلام الأساسي (AsNoTracking للسرعة)
            var q = _db.Set<StockBatch>()
                .AsNoTracking()
                .AsQueryable();

            // ------------------------------
            // فلتر التاريخ/الوقت (UpdatedAt فقط لأن الجدول Cache)
            // ------------------------------
            bool dateFilterActive = useDateRange || fromDate.HasValue || toDate.HasValue;
            if (dateFilterActive)
            {
                if (fromDate.HasValue)
                    q = q.Where(x => x.UpdatedAt >= fromDate.Value);

                if (toDate.HasValue)
                    q = q.Where(x => x.UpdatedAt <= toDate.Value);
            }

            // ------------------------------
            // فلتر كود من/إلى (Id)
            // ------------------------------
            if (fromCode.HasValue)
                q = q.Where(x => x.Id >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(x => x.Id <= toCode.Value);

            // ------------------------------
            // البحث
            // searchBy المقترحة: all | id | warehouse | prod | batchno | expiry
            // ------------------------------
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                var sb = (searchBy ?? "all").ToLower();

                switch (sb)
                {
                    case "id":
                        q = q.Where(x => x.Id.ToString() == term);
                        break;

                    case "warehouse":
                        q = q.Where(x => x.WarehouseId.ToString() == term);
                        break;

                    case "prod":
                        q = q.Where(x => x.ProdId.ToString() == term);
                        break;

                    case "batchno":
                        q = q.Where(x => x.BatchNo.Contains(term));
                        break;

                    case "expiry":
                        if (DateTime.TryParse(term, out var dtExp))
                        {
                            var d = dtExp.Date;
                            q = q.Where(x => x.Expiry.HasValue && x.Expiry.Value.Date == d);
                        }
                        break;

                    case "all":
                    default:
                        // بحث عام سريع: BatchNo + الأكواد + الكميات
                        q = q.Where(x =>
                            x.BatchNo.Contains(term) ||
                            x.Id.ToString() == term ||
                            x.WarehouseId.ToString() == term ||
                            x.ProdId.ToString() == term ||
                            x.QtyOnHand.ToString() == term ||
                            x.QtyReserved.ToString() == term
                        );
                        break;
                }
            }

            // ------------------------------
            // الترتيب (مع Tie-breaker ثابت)
            // sort المقترح: id | warehouse | prod | batchno | expiry | onhand | reserved | updated
            // ------------------------------
            bool descending = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            string sortCol = (sort ?? "id").ToLower();

            IOrderedQueryable<StockBatch> ordered;

            switch (sortCol)
            {
                case "warehouse":
                    ordered = descending
                        ? q.OrderByDescending(x => x.WarehouseId).ThenByDescending(x => x.Id)
                        : q.OrderBy(x => x.WarehouseId).ThenBy(x => x.Id);
                    break;

                case "prod":
                    ordered = descending
                        ? q.OrderByDescending(x => x.ProdId).ThenByDescending(x => x.Id)
                        : q.OrderBy(x => x.ProdId).ThenBy(x => x.Id);
                    break;

                case "batchno":
                    ordered = descending
                        ? q.OrderByDescending(x => x.BatchNo).ThenByDescending(x => x.Id)
                        : q.OrderBy(x => x.BatchNo).ThenBy(x => x.Id);
                    break;

                case "expiry":
                    ordered = descending
                        ? q.OrderByDescending(x => x.Expiry).ThenByDescending(x => x.Id)
                        : q.OrderBy(x => x.Expiry).ThenBy(x => x.Id);
                    break;

                case "onhand":
                    ordered = descending
                        ? q.OrderByDescending(x => x.QtyOnHand).ThenByDescending(x => x.Id)
                        : q.OrderBy(x => x.QtyOnHand).ThenBy(x => x.Id);
                    break;

                case "reserved":
                    ordered = descending
                        ? q.OrderByDescending(x => x.QtyReserved).ThenByDescending(x => x.Id)
                        : q.OrderBy(x => x.QtyReserved).ThenBy(x => x.Id);
                    break;

                case "updated":
                    ordered = descending
                        ? q.OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id)
                        : q.OrderBy(x => x.UpdatedAt).ThenBy(x => x.Id);
                    break;

                case "id":
                default:
                    ordered = descending
                        ? q.OrderByDescending(x => x.Id)
                        : q.OrderBy(x => x.Id);
                    break;
            }

            return ordered;
        }

        // =========================================================
        // GET: /StockBatches
        // Index (نظام القوائم الموحد)
        // =========================================================
        [HttpGet]
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
            int? toCode = null)
        {
            // (1) Defaults + حماية paging
            searchBy ??= "all";
            sort ??= "id";
            dir ??= "asc";

            if (page < 1) page = 1;
            if (pageSize <= 0) pageSize = 25;
            if (pageSize < 10) pageSize = 10;
            if (pageSize > 500) pageSize = 500;

            // (2) Query واحد: فلترة + بحث + ترتيب
            var query = SearchSortFilter(
                search, searchBy, sort, dir,
                useDateRange, fromDate, toDate,
                fromCode, toCode);

            // (3) Count
            int totalCount = await query.CountAsync();
            int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            if (totalPages < 1) totalPages = 1;
            if (page > totalPages) page = 1;

            // (4) Page data
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // (5) تحميل أسماء الأصناف والمخازن للصفحة فقط (سرعة)
            var prodIds = items.Select(x => x.ProdId).Distinct().ToList();
            var whIds = items.Select(x => x.WarehouseId).Distinct().ToList();

            var prodNames = await _db.Products
                .AsNoTracking()
                .Where(p => prodIds.Contains(p.ProdId))
                .Select(p => new { p.ProdId, p.ProdName })
                .ToDictionaryAsync(x => x.ProdId, x => x.ProdName ?? "");

            var warehouseNames = await _db.Warehouses
                .AsNoTracking()
                .Where(w => whIds.Contains(w.WarehouseId))
                .Select(w => new { w.WarehouseId, w.WarehouseName })
                .ToDictionaryAsync(x => x.WarehouseId, x => x.WarehouseName ?? "");

            ViewBag.ProdNames = prodNames;               // متغير: قاموس أسماء الأصناف
            ViewBag.WarehouseNames = warehouseNames;     // متغير: قاموس أسماء المخازن

            // (6) تجهيز PagedResult
            bool sortDesc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            var model = new PagedResult<StockBatch>
            {
                Items = items,
                PageNumber = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                HasPrevious = page > 1,
                HasNext = page < totalPages,
                Search = search,
                SortColumn = sort,
                SortDescending = sortDesc,
                UseDateRange = useDateRange,
                FromDate = fromDate,
                ToDate = toDate
            };

            ViewBag.Search = search;
            ViewBag.SearchBy = searchBy;
            ViewBag.Sort = sort;
            ViewBag.Dir = sortDesc ? "desc" : "asc";
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;

            return View(model);
        }

        // =========================================================
        // POST: /StockBatches/BulkDelete
        // حذف محدد (⚠️ هذا جدول Cache - استخدمه بحذر)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["Error"] = "لم يتم اختيار أي صف للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var ids = selectedIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x, out var v) ? v : (int?)null)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();

            if (ids.Count == 0)
            {
                TempData["Error"] = "القيم المختارة غير صالحة.";
                return RedirectToAction(nameof(Index));
            }

            var rows = await _db.Set<StockBatch>()
                .Where(x => ids.Contains(x.Id))
                .ToListAsync();

            _db.RemoveRange(rows);
            await _db.SaveChangesAsync();

            TempData["Success"] = "تم حذف الصفوف المحددة.";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // POST: /StockBatches/DeleteAll
        // حذف الجميع (⚠️ جدول Cache)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _db.Set<StockBatch>().ToListAsync();
            _db.RemoveRange(all);
            await _db.SaveChangesAsync();

            TempData["Success"] = "تم حذف جميع الصفوف من Stock_Batches.";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // GET: /StockBatches/Export
        // تصدير (Excel / CSV) بنفس فلاتر الاندكس
        // =========================================================
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
            var query = SearchSortFilter(
                search, searchBy, sort, dir,
                useDateRange, fromDate, toDate,
                fromCode, toCode);

            var data = await query.ToListAsync();

            // أسماء الأصناف والمخازن للتصدير
            var prodIds = data.Select(x => x.ProdId).Distinct().ToList();
            var whIds = data.Select(x => x.WarehouseId).Distinct().ToList();

            var prodNames = await _db.Products
                .AsNoTracking()
                .Where(p => prodIds.Contains(p.ProdId))
                .Select(p => new { p.ProdId, p.ProdName })
                .ToDictionaryAsync(x => x.ProdId, x => x.ProdName ?? "");

            var warehouseNames = await _db.Warehouses
                .AsNoTracking()
                .Where(w => whIds.Contains(w.WarehouseId))
                .Select(w => new { w.WarehouseId, w.WarehouseName })
                .ToDictionaryAsync(x => x.WarehouseId, x => x.WarehouseName ?? "");

            if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
            {
                var sb = new StringBuilder();
                sb.AppendLine("Id,WarehouseId,WarehouseName,ProdId,ProdName,BatchNo,Expiry,QtyOnHand,QtyReserved,UpdatedAt,Note");

                foreach (var x in data)
                {
                    var prodName = prodNames.TryGetValue(x.ProdId, out var pn) ? pn : "";
                    var whName = warehouseNames.TryGetValue(x.WarehouseId, out var wn) ? wn : "";

                    sb.AppendLine(string.Join(",",
                        x.Id,
                        x.WarehouseId,
                        EscapeCsv(whName),
                        x.ProdId,
                        EscapeCsv(prodName),
                        EscapeCsv(x.BatchNo),
                        x.Expiry?.ToString("yyyy-MM-dd") ?? "",
                        x.QtyOnHand,
                        x.QtyReserved,
                        x.UpdatedAt.ToString("yyyy-MM-dd HH:mm"),
                        EscapeCsv(x.Note ?? "")
                    ));
                }

                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                var csvName = $"StockBatches_{DateTime.Now:yyyyMMdd_HHmm}.csv";
                return File(bytes, "text/csv", csvName);
            }
            else
            {
                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("Stock_Batches");

                ws.Cell(1, 1).Value = "كود السجل";
                ws.Cell(1, 2).Value = "كود المخزن";
                ws.Cell(1, 3).Value = "اسم المخزن";
                ws.Cell(1, 4).Value = "كود الصنف";
                ws.Cell(1, 5).Value = "اسم الصنف";
                ws.Cell(1, 6).Value = "رقم التشغيلة";
                ws.Cell(1, 7).Value = "تاريخ الصلاحية";
                ws.Cell(1, 8).Value = "المتاح";
                ws.Cell(1, 9).Value = "محجوز";
                ws.Cell(1, 10).Value = "آخر تحديث";
                ws.Cell(1, 11).Value = "ملاحظة";

                int row = 2;
                foreach (var x in data)
                {
                    var prodName = prodNames.TryGetValue(x.ProdId, out var pn) ? pn : "";
                    var whName = warehouseNames.TryGetValue(x.WarehouseId, out var wn) ? wn : "";

                    ws.Cell(row, 1).Value = x.Id;
                    ws.Cell(row, 2).Value = x.WarehouseId;
                    ws.Cell(row, 3).Value = whName;
                    ws.Cell(row, 4).Value = x.ProdId;
                    ws.Cell(row, 5).Value = prodName;
                    ws.Cell(row, 6).Value = x.BatchNo;
                    ws.Cell(row, 7).Value = x.Expiry;
                    ws.Cell(row, 8).Value = x.QtyOnHand;
                    ws.Cell(row, 9).Value = x.QtyReserved;
                    ws.Cell(row, 10).Value = x.UpdatedAt;
                    ws.Cell(row, 11).Value = x.Note ?? "";

                    row++;
                }

                ws.Columns().AdjustToContents();

                using var stream = new System.IO.MemoryStream();
                wb.SaveAs(stream);

                var fileName = $"StockBatches_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
                return File(stream.ToArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileName);
            }
        }

        // دالة صغيرة لهروب نص CSV
        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
                return "\"" + value.Replace("\"", "\"\"") + "\"";

            return value;
        }
    }
}
