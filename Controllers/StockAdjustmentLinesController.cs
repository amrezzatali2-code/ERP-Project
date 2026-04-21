using ERP.Data;                             // الاتصال بقاعدة البيانات AppDbContext
using ERP.Filters;
using ERP.Infrastructure;                  // PagedResult + ApplySearchSort
using ERP.Models;                          // الموديلات StockAdjustmentLine, StockAdjustment
using ERP.Security;
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
    /// كنترولر إدارة سطور تسويات الجرد (StockAdjustmentLines).
    /// كل سطر = صنف واحد في تسوية معينة مع الكمية قبل/بعد والفارق.
    /// </summary>
    [RequirePermission("StockAdjustmentLines.Index")]
    public class StockAdjustmentLinesController : Controller
    {
        private readonly AppDbContext _context;  // متغير: الاتصال بقاعدة البيانات

        public StockAdjustmentLinesController(AppDbContext context)
        {
            _context = context;
        }

        // ==================================================
        // دالة خاصة: بناء استعلام السطور مع (بحث + فلترة + ترتيب)
        // ==================================================
        private IQueryable<StockAdjustmentLine> BuildLinesQuery(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            int? fromCode,
            int? toCode,
            int? stockAdjustmentId,
            string? searchMode = null)
        {
            // 1) الاستعلام الأساسي على جدول السطور (قراءة فقط)
            IQueryable<StockAdjustmentLine> q =
                _context.StockAdjustmentLines
                        .AsNoTracking();

            // 2) فلتر برقم رأس التسوية (اختياري)
            if (stockAdjustmentId.HasValue)
            {
                q = q.Where(l => l.StockAdjustmentId == stockAdjustmentId.Value);
            }

            // 3) فلتر من كود/إلى كود (على كود السطر Id)
            if (fromCode.HasValue)
                q = q.Where(l => l.Id >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(l => l.Id <= toCode.Value);

            // 4) حقول نصية للبحث (حالياً: الملاحظات فقط)
            var stringFields =
                new Dictionary<string, Expression<Func<StockAdjustmentLine, string?>>>()
                {
                    ["note"] = l => l.Note ?? "",
                    ["productname"] = l => l.Product != null ? (l.Product.ProdName ?? "") : ""
                };

            // 5) حقول رقمية للبحث
            var intFields =
                new Dictionary<string, Expression<Func<StockAdjustmentLine, int>>>()
                {
                    ["id"] = l => l.Id,
                    ["stock"] = l => l.StockAdjustmentId,
                    ["header"] = l => l.StockAdjustmentId,
                    ["product"] = l => l.ProductId,
                    ["batch"] = l => l.BatchId ?? 0,
                    ["qtyBefore"] = l => l.QtyBefore,
                    ["qtyAfter"] = l => l.QtyAfter,
                    ["qtyDiff"] = l => l.QtyDiff
                };

            // 6) حقول الترتيب
            var orderFields =
                new Dictionary<string, Expression<Func<StockAdjustmentLine, object>>>()
                {
                    ["id"] = l => l.Id,
                    ["header"] = l => l.StockAdjustmentId,
                    ["product"] = l => l.ProductId,
                    ["batch"] = l => l.BatchId ?? 0,
                    ["qtyBefore"] = l => l.QtyBefore,
                    ["qtyAfter"] = l => l.QtyAfter,
                    ["qtyDiff"] = l => l.QtyDiff,
                    ["costPer"] = l => l.CostPerUnit ?? 0,
                    ["costDiff"] = l => l.CostDiff ?? 0,
                    ["productname"] = l => (object)(l.Product != null ? (l.Product.ProdName ?? "") : ""),
                    ["note"] = l => (object)(l.Note ?? "")
                };

            // 7) تطبيق البحث + الترتيب بالنظام الموحد
            q = q.ApplySearchSort(
                search,
                searchBy,
                sort,
                dir,
                stringFields,
                intFields,
                orderFields,
                defaultSearchBy: "all",
                defaultSortBy: "id",
                searchMode: searchMode
            );

            return q;
        }

        // =========================
        // Index — قائمة سطور التسوية
        // =========================
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",         // all | id | stock | product | productname | batch | note
            string? sort = "id",              // id | header | product | productname | batch | qtyBefore | qtyAfter | qtyDiff | costPer | costDiff
            string? dir = "asc",
            string? searchMode = "contains",
            int page = 1,
            int pageSize = 10,
            int? fromCode = null,
            int? toCode = null,
            int? stockAdjustmentId = null,    // فلتر اختياري برقم رأس التسوية
            string? filterCol_id = null,
            string? filterCol_stock = null,
            string? filterCol_product = null,
            string? filterCol_productName = null,
            string? filterCol_batch = null,
            string? filterCol_qtyBefore = null,
            string? filterCol_qtyAfter = null,
            string? filterCol_qtyDiff = null,
            string? filterCol_costUnit = null,
            string? filterCol_costDiff = null,
            string? filterCol_note = null,
            string? filterCol_idExpr = null,
            string? filterCol_stockExpr = null,
            string? filterCol_productExpr = null,
            string? filterCol_batchExpr = null,
            string? filterCol_qtyBeforeExpr = null,
            string? filterCol_qtyAfterExpr = null,
            string? filterCol_qtyDiffExpr = null,
            string? filterCol_costUnitExpr = null,
            string? filterCol_costDiffExpr = null)
        {
            var pageSizeQuery = Request.Query["pageSize"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageSizeQuery) && int.TryParse(pageSizeQuery, out var psVal))
                pageSize = psVal;

            if (page < 1) page = 1;
            if (pageSize < 0) pageSize = 10;
            if (pageSize > 0 && pageSize != 10 && pageSize != 25 && pageSize != 50 && pageSize != 100 && pageSize != 200)
                pageSize = 10;

            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm != "starts" && sm != "ends") sm = "contains";

            var qBase = BuildLinesQuery(
                search,
                searchBy,
                sort,
                dir,
                fromCode,
                toCode,
                stockAdjustmentId,
                sm);

            qBase = StockAdjustmentLineColumnFilter.ApplyColumnFilters(
                qBase,
                filterCol_id, filterCol_idExpr,
                filterCol_stock, filterCol_stockExpr,
                filterCol_product, filterCol_productExpr,
                filterCol_batch, filterCol_batchExpr,
                filterCol_qtyBefore, filterCol_qtyBeforeExpr,
                filterCol_qtyAfter, filterCol_qtyAfterExpr,
                filterCol_qtyDiff, filterCol_qtyDiffExpr,
                filterCol_costUnit, filterCol_costUnitExpr,
                filterCol_costDiff, filterCol_costDiffExpr,
                filterCol_note);

            if (!string.IsNullOrWhiteSpace(filterCol_productName))
            {
                var names = filterCol_productName.Split(new[] { '|', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();
                if (names.Count > 0)
                    qBase = qBase.Where(l => l.Product != null && l.Product.ProdName != null && names.Contains(l.Product.ProdName));
            }

            IQueryable<StockAdjustmentLine> q = qBase
                .Include(l => l.Product)
                .Include(l => l.StockAdjustment)
                    .ThenInclude(s => s!.Warehouse);

            var total = await q.CountAsync();
            var summary = await qBase
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    TotalQtyBefore = g.Sum(x => x.QtyBefore),
                    TotalQtyAfter = g.Sum(x => x.QtyAfter),
                    TotalQtyDiff = g.Sum(x => x.QtyDiff),
                    TotalCostDiff = g.Sum(x => x.CostDiff ?? 0m)
                })
                .FirstOrDefaultAsync();
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

            var items = await q.Skip(skip).Take(effectivePageSize).ToListAsync();
            var model = new PagedResult<StockAdjustmentLine>(items, page, pageSize, total)
            {
                Search = search ?? "",
                SearchBy = searchBy ?? "all",
                SortColumn = sort ?? "id",
                SortDescending = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase)
            };

            ViewBag.SearchMode = sm;
            ViewBag.PageSize = pageSize;

            // تمرير القيم للواجهة
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.CodeFrom = fromCode;
            ViewBag.CodeTo = toCode;
            ViewBag.StockAdjustmentId = stockAdjustmentId;

            // تمرير فلاتر الأعمدة للواجهة
            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_Stock = filterCol_stock;
            ViewBag.FilterCol_Product = filterCol_product;
            ViewBag.FilterCol_ProductName = filterCol_productName;
            ViewBag.FilterCol_Batch = filterCol_batch;
            ViewBag.FilterCol_QtyBefore = filterCol_qtyBefore;
            ViewBag.FilterCol_QtyAfter = filterCol_qtyAfter;
            ViewBag.FilterCol_QtyDiff = filterCol_qtyDiff;
            ViewBag.FilterCol_CostUnit = filterCol_costUnit;
            ViewBag.FilterCol_CostDiff = filterCol_costDiff;
            ViewBag.FilterCol_Note = filterCol_note;
            ViewBag.FilterCol_IdExpr = filterCol_idExpr ?? string.Empty;
            ViewBag.FilterCol_StockExpr = filterCol_stockExpr ?? string.Empty;
            ViewBag.FilterCol_ProductExpr = filterCol_productExpr ?? string.Empty;
            ViewBag.FilterCol_BatchExpr = filterCol_batchExpr ?? string.Empty;
            ViewBag.FilterCol_QtyBeforeExpr = filterCol_qtyBeforeExpr ?? string.Empty;
            ViewBag.FilterCol_QtyAfterExpr = filterCol_qtyAfterExpr ?? string.Empty;
            ViewBag.FilterCol_QtyDiffExpr = filterCol_qtyDiffExpr ?? string.Empty;
            ViewBag.FilterCol_CostUnitExpr = filterCol_costUnitExpr ?? string.Empty;
            ViewBag.FilterCol_CostDiffExpr = filterCol_costDiffExpr ?? string.Empty;
            ViewBag.TotalQtyBeforeFiltered = summary?.TotalQtyBefore ?? 0;
            ViewBag.TotalQtyAfterFiltered = summary?.TotalQtyAfter ?? 0;
            ViewBag.TotalQtyDiffFiltered = summary?.TotalQtyDiff ?? 0;
            ViewBag.TotalCostDiffFiltered = summary?.TotalCostDiff ?? 0m;

            return View(model);
        }

        // =========================
        // Export — تصدير سطور التسوية (CSV)
        // =========================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            string? searchMode = null,
            int? codeFrom = null,
            int? codeTo = null,
            int? stockAdjustmentId = null,
            string? filterCol_id = null,
            string? filterCol_stock = null,
            string? filterCol_product = null,
            string? filterCol_productName = null,
            string? filterCol_batch = null,
            string? filterCol_qtyBefore = null,
            string? filterCol_qtyAfter = null,
            string? filterCol_qtyDiff = null,
            string? filterCol_costUnit = null,
            string? filterCol_costDiff = null,
            string? filterCol_note = null,
            string? filterCol_idExpr = null,
            string? filterCol_stockExpr = null,
            string? filterCol_productExpr = null,
            string? filterCol_batchExpr = null,
            string? filterCol_qtyBeforeExpr = null,
            string? filterCol_qtyAfterExpr = null,
            string? filterCol_qtyDiffExpr = null,
            string? filterCol_costUnitExpr = null,
            string? filterCol_costDiffExpr = null,
            string? format = "excel")
        {
            int? fromCode = codeFrom;
            int? toCode = codeTo;

            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm != "starts" && sm != "ends") sm = "contains";

            var qBase = BuildLinesQuery(
                search,
                searchBy,
                sort,
                dir,
                fromCode,
                toCode,
                stockAdjustmentId,
                sm);

            qBase = StockAdjustmentLineColumnFilter.ApplyColumnFilters(
                qBase,
                filterCol_id, filterCol_idExpr,
                filterCol_stock, filterCol_stockExpr,
                filterCol_product, filterCol_productExpr,
                filterCol_batch, filterCol_batchExpr,
                filterCol_qtyBefore, filterCol_qtyBeforeExpr,
                filterCol_qtyAfter, filterCol_qtyAfterExpr,
                filterCol_qtyDiff, filterCol_qtyDiffExpr,
                filterCol_costUnit, filterCol_costUnitExpr,
                filterCol_costDiff, filterCol_costDiffExpr,
                filterCol_note);
            if (!string.IsNullOrWhiteSpace(filterCol_productName))
            {
                var names = filterCol_productName.Split(new[] { '|', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();
                if (names.Count > 0)
                    qBase = qBase.Where(l => l.Product != null && l.Product.ProdName != null && names.Contains(l.Product.ProdName));
            }

            var q = qBase
                .Include(l => l.Product)
                .Include(l => l.StockAdjustment)
                    .ThenInclude(s => s!.Warehouse);

            var list = await q.ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("كود السطر,رقم التسوية,كود المخزن,اسم المخزن,كود الصنف,اسم الصنف,كود التشغيلة,الكمية قبل,الكمية بعد,فرق الكمية,تكلفة الوحدة,فرق التكلفة,ملاحظات");

            foreach (var l in list)
            {
                string note = "\"" + (l.Note ?? "").Replace("\"", "\"\"").Replace(",", "،") + "\"";
                var line = string.Join(",",
                    l.Id,
                    l.StockAdjustmentId,
                    l.StockAdjustment?.WarehouseId.ToString() ?? "",
                    "\"" + ((l.StockAdjustment?.Warehouse?.WarehouseName ?? "").Replace("\"", "\"\"").Replace(",", "،")) + "\"",
                    l.ProductId,
                    "\"" + ((l.Product?.ProdName ?? "").Replace("\"", "\"\"").Replace(",", "،")) + "\"",
                    l.BatchId?.ToString() ?? "",
                    l.QtyBefore,
                    l.QtyAfter,
                    l.QtyDiff,
                    l.CostPerUnit?.ToString("0.0000") ?? "",
                    l.CostDiff?.ToString("0.00") ?? "",
                    note
                );

                sb.AppendLine(line);
            }

            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString());
            var ext = (format ?? "excel").ToLower() == "csv" ? "csv" : "csv";
            var fileName = ExcelExportNaming.ArabicTimestampedFileName("سطور تسويات الجرد", "." + ext);

            return File(bytes, "text/csv; charset=utf-8", fileName);
        }

        // =========================
        // GetColumnValues — قيم أعمدة للفلاتر بنمط Excel
        // =========================
        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var col = column?.Trim().ToLowerInvariant() ?? "";

            IQueryable<StockAdjustmentLine> q = _context.StockAdjustmentLines.AsNoTracking();

            List<(string Value, string Display)> items = col switch
            {
                "id" => (await q.Select(l => l.Id).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "stock" => (await q.Select(l => l.StockAdjustmentId).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "product" => (await q.Select(l => l.ProductId).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "productname" => (await q.Where(l => l.Product != null && l.Product.ProdName != null && l.Product.ProdName != "")
                        .Select(l => l.Product!.ProdName!)
                        .Distinct()
                        .OrderBy(v => v)
                        .Take(400)
                        .ToListAsync())
                    .Select(v => (v, v)).ToList(),
                "batch" => (await q.Where(l => l.BatchId.HasValue).Select(l => l.BatchId!.Value).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "qtybefore" => (await q.Select(l => l.QtyBefore).Distinct().OrderBy(v => v).Take(200).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "qtyafter" => (await q.Select(l => l.QtyAfter).Distinct().OrderBy(v => v).Take(200).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "qtydiff" => (await q.Select(l => l.QtyDiff).Distinct().OrderBy(v => v).Take(200).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "costunit" => (await q.Where(l => l.CostPerUnit.HasValue).Select(l => l.CostPerUnit!.Value).Distinct().OrderBy(v => v).Take(200).ToListAsync())
                    .Select(v => (v.ToString(System.Globalization.CultureInfo.InvariantCulture), v.ToString("0.0000"))).ToList(),
                "costdiff" => (await q.Where(l => l.CostDiff.HasValue).Select(l => l.CostDiff!.Value).Distinct().OrderBy(v => v).Take(200).ToListAsync())
                    .Select(v => (v.ToString(System.Globalization.CultureInfo.InvariantCulture), v.ToString("0.00"))).ToList(),
                "note" => (await q.Where(l => l.Note != null).Select(l => l.Note!).Distinct().OrderBy(v => v).Take(300).ToListAsync())
                    .Select(v => (v, v)).ToList(),
                _ => new List<(string Value, string Display)>()
            };

            if (!string.IsNullOrEmpty(searchTerm) && items.Count > 0)
            {
                items = items
                    .Where(x => (x.Display ?? x.Value).ToLowerInvariant().Contains(searchTerm))
                    .ToList();
            }

            return Json(items.Select(x => new { value = x.Value, display = x.Display }));
        }

        // =========================
        // Details — تفاصيل سطر واحد
        // =========================
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
                return NotFound();

            var line = await _context.StockAdjustmentLines
                                     .AsNoTracking()
                                     .FirstOrDefaultAsync(l => l.Id == id.Value);

            if (line == null)
                return NotFound();

            return View(line);
        }

        // =========================
        // Create — GET
        // ممكن يستقبل stockAdjustmentId لتثبيت رقم التسوية
        // =========================
        public IActionResult Create(int? stockAdjustmentId)
        {
            var model = new StockAdjustmentLine
            {
                StockAdjustmentId = stockAdjustmentId ?? 0,
                QtyBefore = 0,
                QtyAfter = 0
            };

            ViewBag.StockAdjustmentId = stockAdjustmentId;
            return View(model);
        }

        // =========================
        // Create — POST
        // نحسب QtyDiff و CostDiff أوتوماتيك قبل الحفظ
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(StockAdjustmentLine line)
        {
            // تحقق بسيط من رأس التسوية
            if (line.StockAdjustmentId <= 0)
            {
                ModelState.AddModelError(nameof(StockAdjustmentLine.StockAdjustmentId),
                    "من فضلك أدخل رقم تسوية صحيح.");
            }

            if (line.ProductId <= 0)
            {
                ModelState.AddModelError(nameof(StockAdjustmentLine.ProductId),
                    "من فضلك أدخل كود صنف صحيح.");
            }

            if (!ModelState.IsValid)
            {
                return View(line);
            }

            // حساب فرق الكمية والتكلفة
            line.QtyDiff = line.QtyAfter - line.QtyBefore;

            if (line.CostPerUnit.HasValue)
            {
                line.CostDiff = line.QtyDiff * line.CostPerUnit.Value;
            }
            else
            {
                line.CostDiff = null;
            }

            _context.StockAdjustmentLines.Add(line);
            await _context.SaveChangesAsync();

            TempData["Msg"] = "تم إضافة سطر تسوية جديد بنجاح.";
            return RedirectToAction(nameof(Index), new { stockAdjustmentId = line.StockAdjustmentId });
        }

        // =========================
        // Edit — GET
        // =========================
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
                return NotFound();

            var line = await _context.StockAdjustmentLines
                                     .FirstOrDefaultAsync(l => l.Id == id.Value);

            if (line == null)
                return NotFound();

            return View(line);
        }

        // =========================
        // Edit — POST
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, StockAdjustmentLine line)
        {
            if (id != line.Id)
                return NotFound();

            if (line.StockAdjustmentId <= 0)
            {
                ModelState.AddModelError(nameof(StockAdjustmentLine.StockAdjustmentId),
                    "من فضلك أدخل رقم تسوية صحيح.");
            }

            if (line.ProductId <= 0)
            {
                ModelState.AddModelError(nameof(StockAdjustmentLine.ProductId),
                    "من فضلك أدخل كود صنف صحيح.");
            }

            if (!ModelState.IsValid)
            {
                return View(line);
            }

            // حساب الفروق مرة أخرى
            line.QtyDiff = line.QtyAfter - line.QtyBefore;

            if (line.CostPerUnit.HasValue)
            {
                line.CostDiff = line.QtyDiff * line.CostPerUnit.Value;
            }
            else
            {
                line.CostDiff = null;
            }

            try
            {
                _context.Update(line);
                await _context.SaveChangesAsync();
                TempData["Msg"] = "تم تعديل سطر التسوية بنجاح.";
            }
            catch (DbUpdateConcurrencyException)
            {
                bool exists = await _context.StockAdjustmentLines.AnyAsync(l => l.Id == id);
                if (!exists)
                    return NotFound();

                ModelState.AddModelError(string.Empty,
                    "حدث تعارض في حفظ البيانات، من فضلك أعد تحميل الصفحة وحاول مرة أخرى.");
                return View(line);
            }

            return RedirectToAction(nameof(Index), new { stockAdjustmentId = line.StockAdjustmentId });
        }

        // =========================
        // Delete — GET
        // =========================
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
                return NotFound();

            var line = await _context.StockAdjustmentLines
                                     .AsNoTracking()
                                     .FirstOrDefaultAsync(l => l.Id == id.Value);

            if (line == null)
                return NotFound();

            return View(line);
        }

        // =========================
        // Delete — POST
        // =========================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var line = await _context.StockAdjustmentLines
                                     .FirstOrDefaultAsync(l => l.Id == id);

            if (line == null)
                return NotFound();

            int headerId = line.StockAdjustmentId;

            _context.StockAdjustmentLines.Remove(line);
            await _context.SaveChangesAsync();

            TempData["Msg"] = "تم حذف سطر التسوية.";
            return RedirectToAction(nameof(Index), new { stockAdjustmentId = headerId });
        }

        // =========================
        // BulkDelete — حذف مجموعة سطور
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["Msg"] = "لم يتم اختيار أي سطور للحذف.";
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

            var lines = await _context.StockAdjustmentLines
                                      .Where(l => ids.Contains(l.Id))
                                      .ToListAsync();

            if (!lines.Any())
            {
                TempData["Msg"] = "لم يتم العثور على السطور المحددة.";
                return RedirectToAction(nameof(Index));
            }

            _context.StockAdjustmentLines.RemoveRange(lines);
            await _context.SaveChangesAsync();

            TempData["Msg"] = $"تم حذف {lines.Count} سطر تسوية.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // DeleteAll — حذف كل السطور (خطير)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var lines = await _context.StockAdjustmentLines.ToListAsync();
            if (!lines.Any())
            {
                TempData["Msg"] = "لا توجد سطور تسوية لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            _context.StockAdjustmentLines.RemoveRange(lines);
            await _context.SaveChangesAsync();

            TempData["Msg"] = $"تم حذف جميع سطور التسوية ({lines.Count}).";
            return RedirectToAction(nameof(Index));
        }
    }
}
