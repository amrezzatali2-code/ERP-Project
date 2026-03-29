using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        private readonly AppDbContext _context;

        private static string NormalizeArabicDigitsToLatin(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var chars = s.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (chars[i] >= '٠' && chars[i] <= '٩')
                    chars[i] = (char)('0' + (chars[i] - '٠'));
            }
            return new string(chars);
        }

        public StockTransferLinesController(AppDbContext context)
        {
            _context = context;
        }

        private IQueryable<StockTransferLine> ApplyFiltersAndSorting(
            IQueryable<StockTransferLine> query,
            string? search,
            string? searchBy,
            string? sort,
            ref bool descending,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            int? fromCode,
            int? toCode,
            string searchMode)
        {
            if (fromCode.HasValue)
                query = query.Where(l => l.Id >= fromCode.Value);

            if (toCode.HasValue)
                query = query.Where(l => l.Id <= toCode.Value);

            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                var from = fromDate.Value.Date;
                var to = toDate.Value.Date.AddDays(1).AddTicks(-1);
                query = query.Where(l => l.StockTransfer != null && l.StockTransfer.TransferDate >= from && l.StockTransfer.TransferDate <= to);
            }
            else if (useDateRange)
            {
                if (fromDate.HasValue)
                    query = query.Where(l => l.StockTransfer!.TransferDate >= fromDate.Value);
                if (toDate.HasValue)
                    query = query.Where(l => l.StockTransfer!.TransferDate <= toDate.Value);
            }

            var sm = searchMode;
            if (sm != "starts" && sm != "ends") sm = "contains";

            if (!string.IsNullOrWhiteSpace(search))
            {
                searchBy = (searchBy ?? "all").ToLowerInvariant();
                string term = NormalizeArabicDigitsToLatin(search.Trim());
                var termLower = term.ToLowerInvariant();

                switch (searchBy)
                {
                    case "id":
                        if (int.TryParse(term, NumberStyles.Any, CultureInfo.InvariantCulture, out var idVal))
                            query = query.Where(l => l.Id == idVal);
                        else
                            query = sm switch
                            {
                                "starts" => query.Where(l => l.Id.ToString().StartsWith(term)),
                                "ends" => query.Where(l => l.Id.ToString().EndsWith(term)),
                                _ => query.Where(l => l.Id.ToString().Contains(term))
                            };
                        break;
                    case "transfer":
                        query = sm switch
                        {
                            "starts" => query.Where(l => l.StockTransferId.ToString().StartsWith(term)),
                            "ends" => query.Where(l => l.StockTransferId.ToString().EndsWith(term)),
                            _ => query.Where(l => l.StockTransferId.ToString().Contains(term))
                        };
                        break;
                    case "product":
                        query = sm switch
                        {
                            "starts" => query.Where(l => l.ProductId.ToString().StartsWith(term)),
                            "ends" => query.Where(l => l.ProductId.ToString().EndsWith(term)),
                            _ => query.Where(l => l.ProductId.ToString().Contains(term))
                        };
                        break;
                    case "note":
                        query = sm switch
                        {
                            "starts" => query.Where(l => l.Note != null && l.Note.ToLower().StartsWith(termLower)),
                            "ends" => query.Where(l => l.Note != null && l.Note.ToLower().EndsWith(termLower)),
                            _ => query.Where(l => l.Note != null && l.Note.ToLower().Contains(termLower))
                        };
                        break;
                    default:
                        query = query.Where(l =>
                            (sm == "starts" && l.Id.ToString().StartsWith(term)) ||
                            (sm == "ends" && l.Id.ToString().EndsWith(term)) ||
                            (sm == "contains" && l.Id.ToString().Contains(term)) ||
                            (sm == "starts" && l.StockTransferId.ToString().StartsWith(term)) ||
                            (sm == "ends" && l.StockTransferId.ToString().EndsWith(term)) ||
                            (sm == "contains" && l.StockTransferId.ToString().Contains(term)) ||
                            (sm == "starts" && l.ProductId.ToString().StartsWith(term)) ||
                            (sm == "ends" && l.ProductId.ToString().EndsWith(term)) ||
                            (sm == "contains" && l.ProductId.ToString().Contains(term)) ||
                            (l.Note != null && (
                                (sm == "starts" && l.Note.ToLower().StartsWith(termLower)) ||
                                (sm == "ends" && l.Note.ToLower().EndsWith(termLower)) ||
                                (sm == "contains" && l.Note.ToLower().Contains(termLower)))));
                        break;
                }
            }

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
                ("fromwh", false) => query.OrderBy(l => l.StockTransfer!.FromWarehouse!.WarehouseName).ThenBy(l => l.Id),
                ("fromwh", true) => query.OrderByDescending(l => l.StockTransfer!.FromWarehouse!.WarehouseName).ThenByDescending(l => l.Id),
                ("towh", false) => query.OrderBy(l => l.StockTransfer!.ToWarehouse!.WarehouseName).ThenBy(l => l.Id),
                ("towh", true) => query.OrderByDescending(l => l.StockTransfer!.ToWarehouse!.WarehouseName).ThenByDescending(l => l.Id),
                ("note", false) => query.OrderBy(l => l.Note ?? "").ThenBy(l => l.Id),
                ("note", true) => query.OrderByDescending(l => l.Note ?? "").ThenByDescending(l => l.Id),
                _ when !descending => query.OrderBy(l => l.Id),
                _ => query.OrderByDescending(l => l.Id)
            };

            return query;
        }

        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",
            string? sort = "id",
            string? dir = "asc",
            string? searchMode = "contains",
            int page = 1,
            int pageSize = 10,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_transfer = null,
            string? filterCol_transferExpr = null,
            string? filterCol_date = null,
            string? filterCol_fromwh = null,
            string? filterCol_fromwhExpr = null,
            string? filterCol_towh = null,
            string? filterCol_towhExpr = null,
            string? filterCol_product = null,
            string? filterCol_productExpr = null,
            string? filterCol_qty = null,
            string? filterCol_qtyExpr = null,
            string? filterCol_note = null)
        {
            var pageSizeQuery = Request.Query["pageSize"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageSizeQuery) && int.TryParse(pageSizeQuery, out var psVal))
                pageSize = psVal;

            if (page < 1) page = 1;
            if (pageSize < 0) pageSize = 10;
            if (pageSize > 0 && pageSize != 10 && pageSize != 25 && pageSize != 50 && pageSize != 100 && pageSize != 200)
                pageSize = 10;

            bool descending = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm != "starts" && sm != "ends") sm = "contains";

            IQueryable<StockTransferLine> baseQuery = _context.StockTransferLines
                .AsNoTracking()
                .Include(l => l.StockTransfer)
                    .ThenInclude(st => st!.FromWarehouse)
                .Include(l => l.StockTransfer)
                    .ThenInclude(st => st!.ToWarehouse);

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
                toCode,
                sm);

            query = StockTransferLineColumnFilter.ApplyColumnFilters(
                query,
                filterCol_id,
                filterCol_idExpr,
                filterCol_transfer,
                filterCol_transferExpr,
                filterCol_date,
                filterCol_fromwh,
                filterCol_fromwhExpr,
                filterCol_towh,
                filterCol_towhExpr,
                filterCol_product,
                filterCol_productExpr,
                filterCol_qty,
                filterCol_qtyExpr,
                filterCol_note);

            var total = await query.CountAsync();
            var totalQtyFiltered = await query.SumAsync(l => (long)l.Qty);

            int effectivePageSize = pageSize;
            if (pageSize == 0)
            {
                effectivePageSize = total == 0 ? 10 : Math.Min(total, 100_000);
                page = 1;
            }

            var skip = (page - 1) * effectivePageSize;
            if (total > 0 && skip >= total)
            {
                page = Math.Max(1, (int)Math.Ceiling((double)total / effectivePageSize));
                skip = (page - 1) * effectivePageSize;
            }

            var items = await query.Skip(skip).Take(effectivePageSize).ToListAsync();
            var result = new PagedResult<StockTransferLine>(items, page, pageSize, total)
            {
                Search = search ?? "",
                SearchBy = searchBy ?? "all",
                SortColumn = sort ?? "id",
                SortDescending = descending,
                UseDateRange = useDateRange,
                FromDate = fromDate,
                ToDate = toDate
            };

            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.Search = result.Search;
            ViewBag.SearchBy = result.SearchBy;
            ViewBag.Sort = result.SortColumn;
            ViewBag.Dir = result.SortDescending ? "desc" : "asc";
            ViewBag.SearchMode = sm;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalQtyFiltered = totalQtyFiltered;

            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_IdExpr = filterCol_idExpr ?? string.Empty;
            ViewBag.FilterCol_Transfer = filterCol_transfer;
            ViewBag.FilterCol_TransferExpr = filterCol_transferExpr ?? string.Empty;
            ViewBag.FilterCol_Date = filterCol_date;
            ViewBag.FilterCol_FromWh = filterCol_fromwh;
            ViewBag.FilterCol_FromWhExpr = filterCol_fromwhExpr ?? string.Empty;
            ViewBag.FilterCol_ToWh = filterCol_towh;
            ViewBag.FilterCol_ToWhExpr = filterCol_towhExpr ?? string.Empty;
            ViewBag.FilterCol_Product = filterCol_product;
            ViewBag.FilterCol_ProductExpr = filterCol_productExpr ?? string.Empty;
            ViewBag.FilterCol_Qty = filterCol_qty;
            ViewBag.FilterCol_QtyExpr = filterCol_qtyExpr ?? string.Empty;
            ViewBag.FilterCol_Note = filterCol_note;

            return View(result);
        }

        /// <summary>قيم الأعمدة المميزة لفلتر الأعمدة (نصية؛ الأعمدة الرقمية تستخدم Expr من الواجهة).</summary>
        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var col = column?.Trim().ToLowerInvariant() ?? "";

            IQueryable<StockTransferLine> q = _context.StockTransferLines.AsNoTracking().Include(l => l.StockTransfer);

            List<(string Value, string Display)> items = col switch
            {
                "date" => (await q.Where(l => l.StockTransfer != null).Select(l => l.StockTransfer!.TransferDate.Date).Distinct().OrderByDescending(d => d).Take(200).ToListAsync())
                    .Select(d => (d.ToString("yyyy-MM-dd"), d.ToString("yyyy/MM/dd"))).ToList(),
                "note" => (await q.Where(l => l.Note != null).Select(l => l.Note!).Distinct().OrderBy(v => v).Take(300).ToListAsync())
                    .Select(v => (v, v)).ToList(),
                _ => new List<(string Value, string Display)>()
            };

            if (col == "fromwh")
            {
                var whIds = await q.Where(l => l.StockTransfer != null).Select(l => l.StockTransfer!.FromWarehouseId).Distinct().Take(500).ToListAsync();
                var whRows = await _context.Warehouses.AsNoTracking()
                    .Where(w => whIds.Contains(w.WarehouseId))
                    .OrderBy(w => w.WarehouseName)
                    .Select(w => new { w.WarehouseId, w.WarehouseName })
                    .Take(300)
                    .ToListAsync();
                items = whRows.Select(w => (w.WarehouseId.ToString(CultureInfo.InvariantCulture), $"{w.WarehouseName} ({w.WarehouseId})")).ToList();
            }
            else if (col == "towh")
            {
                var whIds = await q.Where(l => l.StockTransfer != null).Select(l => l.StockTransfer!.ToWarehouseId).Distinct().Take(500).ToListAsync();
                var whRows = await _context.Warehouses.AsNoTracking()
                    .Where(w => whIds.Contains(w.WarehouseId))
                    .OrderBy(w => w.WarehouseName)
                    .Select(w => new { w.WarehouseId, w.WarehouseName })
                    .Take(300)
                    .ToListAsync();
                items = whRows.Select(w => (w.WarehouseId.ToString(CultureInfo.InvariantCulture), $"{w.WarehouseName} ({w.WarehouseId})")).ToList();
            }

            if (!string.IsNullOrEmpty(searchTerm) && items.Count > 0)
            {
                items = items
                    .Where(x => (x.Display ?? x.Value).ToLowerInvariant().Contains(searchTerm))
                    .ToList();
            }

            return Json(items.Select(x => new { value = x.Value, display = x.Display }));
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
            string? searchMode = "contains",
            string? sort = "id",
            string? dir = "asc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_transfer = null,
            string? filterCol_transferExpr = null,
            string? filterCol_date = null,
            string? filterCol_fromwh = null,
            string? filterCol_fromwhExpr = null,
            string? filterCol_towh = null,
            string? filterCol_towhExpr = null,
            string? filterCol_product = null,
            string? filterCol_productExpr = null,
            string? filterCol_qty = null,
            string? filterCol_qtyExpr = null,
            string? filterCol_note = null,
            string format = "excel")
        {
            bool descending = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm != "starts" && sm != "ends") sm = "contains";

            IQueryable<StockTransferLine> baseQuery = _context.StockTransferLines
                .AsNoTracking()
                .Include(l => l.StockTransfer)
                    .ThenInclude(st => st!.FromWarehouse)
                .Include(l => l.StockTransfer)
                    .ThenInclude(st => st!.ToWarehouse);

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
                toCode,
                sm);

            query = StockTransferLineColumnFilter.ApplyColumnFilters(
                query,
                filterCol_id,
                filterCol_idExpr,
                filterCol_transfer,
                filterCol_transferExpr,
                filterCol_date,
                filterCol_fromwh,
                filterCol_fromwhExpr,
                filterCol_towh,
                filterCol_towhExpr,
                filterCol_product,
                filterCol_productExpr,
                filterCol_qty,
                filterCol_qtyExpr,
                filterCol_note);

            var list = await query.ToListAsync();

            format = (format ?? "excel").Trim().ToLowerInvariant();

            if (format == "csv")
            {
                var sb = new StringBuilder();

                // عناوين الأعمدة في ملف CSV
                sb.AppendLine("كود السطر,رقم التحويل,تاريخ التحويل,من مخزن,إلى مخزن,كود الصنف,الكمية,ملاحظات السطر");

                foreach (var l in list)
                {
                    var note = (l.Note ?? "").Replace(",", " ");
                    var date = l.StockTransfer?.TransferDate
                               .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "";
                    var fromWh = (l.StockTransfer?.FromWarehouse?.WarehouseName ?? l.StockTransfer?.FromWarehouseId.ToString() ?? "").Replace(",", " ");
                    var toWh = (l.StockTransfer?.ToWarehouse?.WarehouseName ?? l.StockTransfer?.ToWarehouseId.ToString() ?? "").Replace(",", " ");

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
                var fileNameCsv = ExcelExportNaming.ArabicTimestampedFileName("سطور تحويل المخزون", ".csv");
                const string contentTypeCsv = "text/csv; charset=utf-8";

                return File(bytesCsv, contentTypeCsv, fileNameCsv);
            }
            else
            {
                using var workbook = new XLWorkbook();
                var ws = workbook.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("سطور تحويل المخزون"));

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
                    ws.Cell(r, 4).Value = l.StockTransfer?.FromWarehouse?.WarehouseName
                        ?? (l.StockTransfer != null ? l.StockTransfer.FromWarehouseId.ToString(CultureInfo.InvariantCulture) : "");
                    ws.Cell(r, 5).Value = l.StockTransfer?.ToWarehouse?.WarehouseName
                        ?? (l.StockTransfer != null ? l.StockTransfer.ToWarehouseId.ToString(CultureInfo.InvariantCulture) : "");
                    ws.Cell(r, 6).Value = l.ProductId;
                    ws.Cell(r, 7).Value = l.Qty;
                    ws.Cell(r, 8).Value = l.Note ?? "";
                }

                ws.Columns().AdjustToContents();
                ws.Column(3).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                stream.Position = 0;

                var fileNameXlsx = ExcelExportNaming.ArabicTimestampedFileName("سطور تحويل المخزون", ".xlsx");
                const string contentTypeXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

                return File(stream.ToArray(), contentTypeXlsx, fileNameXlsx);
            }
        }
    }
}
