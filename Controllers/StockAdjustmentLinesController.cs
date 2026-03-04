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
        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

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
            int? stockAdjustmentId)
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
                    ["note"] = l => l.Note ?? ""
                };

            // 5) حقول رقمية للبحث
            var intFields =
                new Dictionary<string, Expression<Func<StockAdjustmentLine, int>>>()
                {
                    ["id"] = l => l.Id,
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
                defaultSearchBy: "id",
                defaultSortBy: "id"
            );

            return q;
        }

        // =========================
        // Index — قائمة سطور التسوية
        // =========================
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "id",          // id | header | product | batch | note ...
            string? sort = "id",              // id | header | product | batch | qtyBefore | qtyAfter | qtyDiff | costPer | costDiff
            string? dir = "asc",
            int page = 1,
            int pageSize = 25,
            int? fromCode = null,
            int? toCode = null,
            int? stockAdjustmentId = null,    // فلتر اختياري برقم رأس التسوية
            string? filterCol_id = null,
            string? filterCol_stock = null,
            string? filterCol_product = null,
            string? filterCol_batch = null,
            string? filterCol_qtyBefore = null,
            string? filterCol_qtyAfter = null,
            string? filterCol_qtyDiff = null,
            string? filterCol_costUnit = null,
            string? filterCol_costDiff = null,
            string? filterCol_note = null)
        {
            var qBase = BuildLinesQuery(
                search,
                searchBy,
                sort,
                dir,
                fromCode,
                toCode,
                stockAdjustmentId);

            // فلاتر الأعمدة بنمط Excel
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(l => ids.Contains(l.Id));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_stock))
            {
                var ids = filterCol_stock.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(l => ids.Contains(l.StockAdjustmentId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_product))
            {
                var ids = filterCol_product.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(l => ids.Contains(l.ProductId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_batch))
            {
                var ids = filterCol_batch.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(l => l.BatchId.HasValue && ids.Contains(l.BatchId.Value));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_qtyBefore))
            {
                var ids = filterCol_qtyBefore.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(l => ids.Contains(l.QtyBefore));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_qtyAfter))
            {
                var ids = filterCol_qtyAfter.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(l => ids.Contains(l.QtyAfter));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_qtyDiff))
            {
                var ids = filterCol_qtyDiff.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(l => ids.Contains(l.QtyDiff));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_costUnit))
            {
                var vals = filterCol_costUnit.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0)
                    qBase = qBase.Where(l => l.CostPerUnit.HasValue && vals.Contains(l.CostPerUnit.Value));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_costDiff))
            {
                var vals = filterCol_costDiff.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0)
                    qBase = qBase.Where(l => l.CostDiff.HasValue && vals.Contains(l.CostDiff.Value));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_note))
            {
                var vals = filterCol_note.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    qBase = qBase.Where(l => l.Note != null && vals.Contains(l.Note));
            }

            var q = qBase;

            var model = await PagedResult<StockAdjustmentLine>.CreateAsync(q, page, pageSize);

            // تعبئة خصائص البحث/الترتيب في الموديل
            model.Search = search ?? "";
            model.SearchBy = searchBy ?? "id";
            model.SortColumn = sort ?? "id";
            model.SortDescending = (dir?.ToLower() == "desc");

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
            ViewBag.FilterCol_Batch = filterCol_batch;
            ViewBag.FilterCol_QtyBefore = filterCol_qtyBefore;
            ViewBag.FilterCol_QtyAfter = filterCol_qtyAfter;
            ViewBag.FilterCol_QtyDiff = filterCol_qtyDiff;
            ViewBag.FilterCol_CostUnit = filterCol_costUnit;
            ViewBag.FilterCol_CostDiff = filterCol_costDiff;
            ViewBag.FilterCol_Note = filterCol_note;

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
            int? codeFrom,
            int? codeTo,
            int? stockAdjustmentId,
            string? filterCol_id = null,
            string? filterCol_stock = null,
            string? filterCol_product = null,
            string? filterCol_batch = null,
            string? filterCol_qtyBefore = null,
            string? filterCol_qtyAfter = null,
            string? filterCol_qtyDiff = null,
            string? filterCol_costUnit = null,
            string? filterCol_costDiff = null,
            string? filterCol_note = null,
            string? format = "excel")
        {
            int? fromCode = codeFrom;
            int? toCode = codeTo;

            var qBase = BuildLinesQuery(
                search,
                searchBy,
                sort,
                dir,
                fromCode,
                toCode,
                stockAdjustmentId);

            // نفس منطق فلاتر الأعمدة المستخدم في Index
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(l => ids.Contains(l.Id));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_stock))
            {
                var ids = filterCol_stock.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(l => ids.Contains(l.StockAdjustmentId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_product))
            {
                var ids = filterCol_product.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(l => ids.Contains(l.ProductId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_batch))
            {
                var ids = filterCol_batch.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(l => l.BatchId.HasValue && ids.Contains(l.BatchId.Value));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_qtyBefore))
            {
                var ids = filterCol_qtyBefore.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(l => ids.Contains(l.QtyBefore));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_qtyAfter))
            {
                var ids = filterCol_qtyAfter.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(l => ids.Contains(l.QtyAfter));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_qtyDiff))
            {
                var ids = filterCol_qtyDiff.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(l => ids.Contains(l.QtyDiff));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_costUnit))
            {
                var vals = filterCol_costUnit.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0)
                    qBase = qBase.Where(l => l.CostPerUnit.HasValue && vals.Contains(l.CostPerUnit.Value));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_costDiff))
            {
                var vals = filterCol_costDiff.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0)
                    qBase = qBase.Where(l => l.CostDiff.HasValue && vals.Contains(l.CostDiff.Value));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_note))
            {
                var vals = filterCol_note.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    qBase = qBase.Where(l => l.Note != null && vals.Contains(l.Note));
            }

            var q = qBase;

            var list = await q.ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("Id,StockAdjustmentId,ProductId,BatchId,QtyBefore,QtyAfter,QtyDiff,CostPerUnit,CostDiff,Note");

            foreach (var l in list)
            {
                var line = string.Join(",",
                    l.Id,
                    l.StockAdjustmentId,
                    l.ProductId,
                    l.BatchId?.ToString() ?? "",
                    l.QtyBefore,
                    l.QtyAfter,
                    l.QtyDiff,
                    l.CostPerUnit?.ToString("0.0000") ?? "",
                    l.CostDiff?.ToString("0.00") ?? "",
                    (l.Note ?? "").Replace(",", " ")
                );

                sb.AppendLine(line);
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var ext = (format ?? "excel").ToLower() == "csv" ? "csv" : "csv";
            var fileName = $"StockAdjustmentLines_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}";

            return File(bytes, "text/csv", fileName);
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
