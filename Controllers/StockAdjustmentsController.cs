using ERP.Data;                             // كائن الاتصال بقاعدة البيانات AppDbContext
using ERP.Filters;
using ERP.Infrastructure;                  // PagedResult + ApplySearchSort + UserActivityLogger
using ERP.Models;                          // StockAdjustment, UserActionType...
using ERP.Security;
using ERP.Services;                        // ILedgerPostingService
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;  // SelectList
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    [RequirePermission("StockAdjustments.Index")]
    public class StockAdjustmentsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IUserActivityLogger _activityLogger;

        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        public StockAdjustmentsController(AppDbContext context, IUserActivityLogger activityLogger)
        {
            _context = context;
            _activityLogger = activityLogger;
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
            DateTime? toDate = null,
            string? filterCol_id = null,
            string? filterCol_date = null,
            string? filterCol_warehouse = null,
            string? filterCol_reference = null,
            string? filterCol_reason = null,
            string? filterCol_created = null)
        {
            // بناء الاستعلام طبقاً للفلاتر العامة (بحث + كود + تاريخ)
            var qBase = BuildAdjustmentsQuery(
                search,
                searchBy,
                sort,
                dir,
                fromCode,
                toCode,
                useDateRange,
                fromDate,
                toDate);

            // فلاتر الأعمدة (بنمط Excel) على رأس التسوية
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(x => ids.Contains(x.Id));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_warehouse))
            {
                var ids = filterCol_warehouse.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(x => ids.Contains(x.WarehouseId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_reference))
            {
                var vals = filterCol_reference.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    qBase = qBase.Where(x => x.ReferenceNo != null && vals.Contains(x.ReferenceNo));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_reason))
            {
                var vals = filterCol_reason.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    qBase = qBase.Where(x => x.Reason != null && vals.Contains(x.Reason));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_date))
            {
                var dates = filterCol_date.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => DateTime.TryParse(x.Trim(), out var d) ? d.Date : (DateTime?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (dates.Count > 0)
                    qBase = qBase.Where(x => dates.Contains(x.AdjustmentDate.Date));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_created))
            {
                var dates = filterCol_created.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => DateTime.TryParse(x.Trim(), out var d) ? d.Date : (DateTime?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (dates.Count > 0)
                    qBase = qBase.Where(x => x.CreatedAt.Date != DateTime.MinValue && dates.Contains(x.CreatedAt.Date));
            }

            // تضمين السطور بعد تطبيق الفلاتر
            var q = qBase
                .Include(a => a.Lines)
                .ThenInclude(l => l.Product);

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

            // تمرير فلاتر الأعمدة إلى الواجهة
            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_Date = filterCol_date;
            ViewBag.FilterCol_Warehouse = filterCol_warehouse;
            ViewBag.FilterCol_Reference = filterCol_reference;
            ViewBag.FilterCol_Reason = filterCol_reason;
            ViewBag.FilterCol_Created = filterCol_created;

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
            string? filterCol_id = null,
            string? filterCol_date = null,
            string? filterCol_warehouse = null,
            string? filterCol_reference = null,
            string? filterCol_reason = null,
            string? filterCol_created = null,
            string? format = "excel")   // excel | csv (الاثنين CSV حالياً)
        {
            int? fromCode = codeFrom;
            int? toCode = codeTo;

            var qBase = BuildAdjustmentsQuery(
                search,
                searchBy,
                sort,
                dir,
                fromCode,
                toCode,
                useDateRange,
                fromDate,
                toDate);

            // نفس منطق فلاتر الأعمدة المستخدم في Index
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(x => ids.Contains(x.Id));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_warehouse))
            {
                var ids = filterCol_warehouse.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    qBase = qBase.Where(x => ids.Contains(x.WarehouseId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_reference))
            {
                var vals = filterCol_reference.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    qBase = qBase.Where(x => x.ReferenceNo != null && vals.Contains(x.ReferenceNo));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_reason))
            {
                var vals = filterCol_reason.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    qBase = qBase.Where(x => x.Reason != null && vals.Contains(x.Reason));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_date))
            {
                var dates = filterCol_date.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => DateTime.TryParse(x.Trim(), out var d) ? d.Date : (DateTime?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (dates.Count > 0)
                    qBase = qBase.Where(x => dates.Contains(x.AdjustmentDate.Date));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_created))
            {
                var dates = filterCol_created.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => DateTime.TryParse(x.Trim(), out var d) ? d.Date : (DateTime?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (dates.Count > 0)
                    qBase = qBase.Where(x => x.CreatedAt.Date != DateTime.MinValue && dates.Contains(x.CreatedAt.Date));
            }

            var q = qBase;

            var list = await q.ToListAsync();

            var sb = new StringBuilder();

            // عناوين الأعمدة
            sb.AppendLine("كود التسوية,تاريخ التسوية,كود المخزن,رقم مرجعي,السبب,تاريخ الإنشاء");

            // كل سطر = تسوية واحدة
            foreach (var x in list)
            {
                string dateText = x.AdjustmentDate.ToString("yyyy-MM-dd");
                string createdText = x.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                string Safe(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"").Replace(",", "،") + "\"";

                var line = string.Join(",",
                    x.Id,
                    dateText,
                    x.WarehouseId,
                    Safe(x.ReferenceNo),
                    Safe(x.Reason),
                    createdText
                );

                sb.AppendLine(line);
            }

            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString());
            var ext = (format ?? "excel").ToLower() == "csv" ? "csv" : "csv";
            var fileName = ExcelExportNaming.ArabicTimestampedFileName("تسويات الجرد", "." + ext);

            return File(bytes, "text/csv; charset=utf-8", fileName);
        }

        // =========================
        // GetColumnValues — فلاتر الأعمدة بنمط Excel
        // =========================
        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var col = column?.Trim().ToLowerInvariant() ?? "";

            IQueryable<StockAdjustment> q = _context.StockAdjustments.AsNoTracking();

            List<(string Value, string Display)> items = col switch
            {
                "id" => (await q.Select(x => x.Id).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "warehouse" => (await q.Select(x => x.WarehouseId).Distinct().OrderBy(v => v).Take(200).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "reference" => (await q.Where(x => x.ReferenceNo != null)
                        .Select(x => x.ReferenceNo!)
                        .Distinct()
                        .OrderBy(v => v)
                        .Take(300)
                        .ToListAsync())
                    .Select(v => (v, v)).ToList(),
                "reason" => (await q.Where(x => x.Reason != null)
                        .Select(x => x.Reason!)
                        .Distinct()
                        .OrderBy(v => v)
                        .Take(300)
                        .ToListAsync())
                    .Select(v => (v, v)).ToList(),
                "date" => (await q.Select(x => x.AdjustmentDate.Date)
                        .Distinct()
                        .OrderByDescending(d => d)
                        .Take(200)
                        .ToListAsync())
                    .Select(d => (d.ToString("yyyy-MM-dd"), d.ToString("yyyy/MM/dd"))).ToList(),
                "created" => (await q.Select(x => x.CreatedAt.Date)
                        .Distinct()
                        .OrderByDescending(d => d)
                        .Take(200)
                        .ToListAsync())
                    .Select(d => (d.ToString("yyyy-MM-dd"), d.ToString("yyyy/MM/dd"))).ToList(),
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
        // Show — شاشة عرض التسوية مع إضافة السطور والترحيل
        // =========================
        [HttpGet]
        public async Task<IActionResult> Show(int id, string? frag = null, int? frame = null)
        {
            bool isBodyOnly = string.Equals(frag, "body", StringComparison.OrdinalIgnoreCase);

            if (!isBodyOnly && frame != 1)
                return RedirectToAction(nameof(Show), new { id = id, frag = frag, frame = 1 });

            ViewBag.Fragment = frag;

            StockAdjustment? adjustment = null;

            if (id > 0)
            {
                adjustment = await _context.StockAdjustments
                    .Include(a => a.Warehouse)
                    .Include(a => a.Lines)
                        .ThenInclude(l => l.Product)
                    .Include(a => a.Lines)
                        .ThenInclude(l => l.Batch)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Id == id);

                if (adjustment == null)
                {
                    if (isBodyOnly)
                        return NotFound("التسوية غير موجودة.");
                    TempData["Error"] = "التسوية غير موجودة.";
                    return RedirectToAction("Index");
                }
            }
            else
            {
                // إنشاء تسوية جديدة
                adjustment = new StockAdjustment
                {
                    Id = 0,
                    AdjustmentDate = DateTime.Today,
                    WarehouseId = 0,
                    IsPosted = false,
                    Status = "مسودة",
                    Lines = new List<StockAdjustmentLine>()
                };
            }

            // تجهيز قائمة المخازن
            var warehouses = await _context.Warehouses
                .AsNoTracking()
                .OrderBy(w => w.WarehouseId)
                .Select(w => new { w.WarehouseId, w.WarehouseName })
                .ToListAsync();

            ViewBag.Warehouses = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                warehouses,
                "WarehouseId",
                "WarehouseName",
                adjustment.WarehouseId > 0 ? adjustment.WarehouseId : null);

            // تجهيز قائمة المنتجات للأوتوكومبليت
            var products = await _context.Products
                .AsNoTracking()
                .OrderBy(p => p.ProdName)
                .Select(p => new
                {
                    Id = p.ProdId,
                    Name = p.ProdName ?? string.Empty,
                    GenericName = p.GenericName ?? string.Empty,
                    Company = p.Company ?? string.Empty,
                    HasQuota = p.HasQuota,
                    PriceRetail = p.PriceRetail
                })
                .ToListAsync();

            ViewBag.ProductsAuto = products;

            // تجهيز نظام الأسهم (فقط إذا كان id > 0)
            if (adjustment.Id > 0)
            {
                await FillStockAdjustmentNavAsync(adjustment.Id);
            }
            else
            {
                ViewBag.NavFirstId = 0;
                ViewBag.NavLastId = 0;
                ViewBag.NavPrevId = 0;
                ViewBag.NavNextId = 0;
            }

            ViewBag.IsLocked = adjustment.IsPosted || adjustment.Status == "Posted" || adjustment.Status == "Closed";
            ViewBag.Frame = (!isBodyOnly) ? 1 : 0;

            return View("Show", adjustment);
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

        /// <summary>
        /// دالة مساعدة: تجهيز بيانات التنقل (أول/سابق/التالي/آخر) لتسوية الجرد.
        /// </summary>
        private async Task FillStockAdjustmentNavAsync(int currentId)
        {
            var minMax = await _context.StockAdjustments
                .AsNoTracking()
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    FirstId = g.Min(x => x.Id),
                    LastId = g.Max(x => x.Id)
                })
                .FirstOrDefaultAsync();

            int? prevId = null;
            int? nextId = null;

            if (currentId > 0)
            {
                prevId = await _context.StockAdjustments
                    .AsNoTracking()
                    .Where(x => x.Id < currentId)
                    .OrderByDescending(x => x.Id)
                    .Select(x => (int?)x.Id)
                    .FirstOrDefaultAsync();

                nextId = await _context.StockAdjustments
                    .AsNoTracking()
                    .Where(x => x.Id > currentId)
                    .OrderBy(x => x.Id)
                    .Select(x => (int?)x.Id)
                    .FirstOrDefaultAsync();
            }
            else
            {
                prevId = minMax?.LastId;
                nextId = minMax?.FirstId;
            }

            int firstId = minMax?.FirstId ?? 0;
            int lastId = minMax?.LastId ?? 0;

            ViewBag.NavFirstId = firstId;
            ViewBag.NavLastId = lastId;
            ViewBag.NavPrevId = prevId ?? 0;
            ViewBag.NavNextId = nextId ?? 0;
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

            await _activityLogger.LogAsync(UserActionType.Create, "StockAdjustment", adjustment.Id, $"إنشاء تسوية جرد رقم {adjustment.Id}");

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
                var existing = await _context.StockAdjustments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
                var oldValues = existing != null ? System.Text.Json.JsonSerializer.Serialize(new { existing.AdjustmentDate, existing.WarehouseId, existing.ReferenceNo, existing.Reason }) : null;
                adjustment.UpdatedAt = DateTime.UtcNow;
                _context.Update(adjustment);
                await _context.SaveChangesAsync();

                var newValues = System.Text.Json.JsonSerializer.Serialize(new { adjustment.AdjustmentDate, adjustment.WarehouseId, adjustment.ReferenceNo, adjustment.Reason });
                await _activityLogger.LogAsync(UserActionType.Edit, "StockAdjustment", adjustment.Id, $"تعديل تسوية جرد رقم {adjustment.Id}", oldValues, newValues);

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
        // Delete — POST: حذف تسوية واحدة (مثل المبيعات/المشتريات: الحذف من القائمة بغض النظر عن الترحيل)
        // إذا كانت مترحلة: نعكس الترحيل أولاً ثم نحذف
        // =========================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var adjustment = await _context.StockAdjustments
                .Include(a => a.Lines)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (adjustment == null)
                return NotFound();

            try
            {
                var oldValues = System.Text.Json.JsonSerializer.Serialize(new { adjustment.AdjustmentDate, adjustment.WarehouseId, adjustment.ReferenceNo, adjustment.Reason });
                if (adjustment.IsPosted)
                {
                    var ledgerPostingService = HttpContext.RequestServices.GetRequiredService<ILedgerPostingService>();
                    await ledgerPostingService.ReverseStockAdjustmentAsync(id, User?.Identity?.Name ?? "SYSTEM");
                }

                _context.StockAdjustments.Remove(adjustment);
                await _context.SaveChangesAsync();

                await _activityLogger.LogAsync(UserActionType.Delete, "StockAdjustment", id, $"حذف تسوية جرد رقم {id}", oldValues: oldValues);

                TempData["Msg"] = "تم حذف التسوية بنجاح.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"تعذر حذف التسوية: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================
        // BulkDelete — حذف مجموعة تسويات محددة (مثل المبيعات/المشتريات: بغض النظر عن الترحيل)
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
                .Distinct()
                .ToList();

            if (!ids.Any())
            {
                TempData["Msg"] = "لم يتم اختيار أكواد صحيحة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var ledgerPostingService = HttpContext.RequestServices.GetRequiredService<ILedgerPostingService>();
            int deletedCount = 0;
            var failedIds = new List<int>();

            foreach (var id in ids)
            {
                try
                {
                    var adjustment = await _context.StockAdjustments
                        .Include(a => a.Lines)
                        .FirstOrDefaultAsync(a => a.Id == id);

                    if (adjustment == null)
                        continue;

                    if (adjustment.IsPosted)
                        await ledgerPostingService.ReverseStockAdjustmentAsync(id, User?.Identity?.Name ?? "SYSTEM");

                    _context.StockAdjustments.Remove(adjustment);
                    await _context.SaveChangesAsync();
                    deletedCount++;
                }
                catch
                {
                    failedIds.Add(id);
                }
            }

            if (deletedCount > 0)
                TempData["Msg"] = failedIds.Any()
                    ? $"تم حذف {deletedCount} تسوية. فشل حذف: {string.Join(", ", failedIds)}"
                    : $"تم حذف {deletedCount} تسوية.";
            if (failedIds.Any())
                TempData["ErrorMessage"] = $"فشل حذف التسويات: {string.Join(", ", failedIds)}";

            return RedirectToAction(nameof(Index));
        }

        // =========================
        // DeleteAll — حذف جميع التسويات (مثل المبيعات/المشتريات: بغض النظر عن الترحيل)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var ids = await _context.StockAdjustments.Select(a => a.Id).ToListAsync();
            if (!ids.Any())
            {
                TempData["Msg"] = "لا توجد تسويات لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            var ledgerPostingService = HttpContext.RequestServices.GetRequiredService<ILedgerPostingService>();
            int deletedCount = 0;
            var failedIds = new List<int>();

            foreach (var id in ids)
            {
                try
                {
                    var adjustment = await _context.StockAdjustments
                        .Include(a => a.Lines)
                        .FirstOrDefaultAsync(a => a.Id == id);

                    if (adjustment == null)
                        continue;

                    if (adjustment.IsPosted)
                        await ledgerPostingService.ReverseStockAdjustmentAsync(id, User?.Identity?.Name ?? "SYSTEM");

                    _context.StockAdjustments.Remove(adjustment);
                    await _context.SaveChangesAsync();
                    deletedCount++;
                }
                catch
                {
                    failedIds.Add(id);
                }
            }

            if (deletedCount > 0)
                TempData["Msg"] = failedIds.Any()
                    ? $"تم حذف {deletedCount} تسوية. فشل حذف: {string.Join(", ", failedIds)}"
                    : $"تم حذف {deletedCount} تسوية.";
            if (failedIds.Any())
                TempData["ErrorMessage"] = $"فشل حذف التسويات: {string.Join(", ", failedIds)}";

            return RedirectToAction(nameof(Index));
        }

        // =========================
        // CreateHeaderJson — إنشاء/حفظ رأس التسوية (JSON API)
        // =========================
        [HttpPost]
        public async Task<IActionResult> CreateHeaderJson([FromBody] StockAdjustmentHeaderDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { ok = false, message = "بيانات غير صحيحة." });
            }

            if (dto.WarehouseId <= 0)
            {
                return BadRequest(new { ok = false, message = "يجب اختيار مخزن صحيح." });
            }

            var adjustment = new StockAdjustment
            {
                AdjustmentDate = dto.AdjustmentDate,
                WarehouseId = dto.WarehouseId,
                ReferenceNo = dto.ReferenceNo,
                Reason = dto.Reason,
                CreatedAt = DateTime.UtcNow,
                IsPosted = false,
                Status = "مسودة"
            };

            _context.StockAdjustments.Add(adjustment);
            await _context.SaveChangesAsync();

            return Json(new { ok = true, id = adjustment.Id });
        }

        // =========================
        // UpdateHeaderJson — تحديث رأس التسوية (JSON API)
        // =========================
        [HttpPost]
        public async Task<IActionResult> UpdateHeaderJson([FromBody] StockAdjustmentHeaderDto dto)
        {
            if (!ModelState.IsValid || dto.Id <= 0)
            {
                return BadRequest(new { ok = false, message = "بيانات غير صحيحة." });
            }

            var adjustment = await _context.StockAdjustments.FindAsync(dto.Id);
            if (adjustment == null)
            {
                return NotFound(new { ok = false, message = "التسوية غير موجودة." });
            }

            if (adjustment.IsPosted)
            {
                return BadRequest(new { ok = false, message = "لا يمكن تعديل تسوية مترحلة." });
            }

            adjustment.AdjustmentDate = dto.AdjustmentDate;
            adjustment.WarehouseId = dto.WarehouseId;
            adjustment.ReferenceNo = dto.ReferenceNo;
            adjustment.Reason = dto.Reason;
            adjustment.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Json(new { ok = true, id = adjustment.Id });
        }

        // =========================
        // GetStockAdjustmentProductInfo — جلب بيانات الصنف للتسوية (مثل فاتورة البيع)
        // - الكمية قبل من StockBatch (حسب الصنف + المخزن + التشغيلة + الصلاحية)
        // - التكلفة من متوسط StockLedger أو من Batch
        // - التشغيلات المتاحة للمخزن
        // =========================
        [HttpGet]
        public async Task<IActionResult> GetStockAdjustmentProductInfo(int prodId, int warehouseId, string? batchNo, string? expiry)
        {
            if (prodId <= 0 || warehouseId <= 0)
                return Json(new { ok = false, message = "بيانات غير صحيحة." });

            var product = await _context.Products
                .AsNoTracking()
                .Where(p => p.ProdId == prodId)
                .Select(p => new { p.ProdId, p.ProdName, p.PriceRetail })
                .FirstOrDefaultAsync();

            if (product == null)
                return Json(new { ok = false, message = "الصنف غير موجود." });

            int qtyBefore = 0;
            decimal costPerUnit = 0m;
            string? firstBatchNo = null;
            string? firstExpiry = null;

            var stockQuery = _context.StockBatches
                .AsNoTracking()
                .Where(sb => sb.ProdId == prodId && sb.WarehouseId == warehouseId && sb.QtyOnHand > 0);

            if (!string.IsNullOrWhiteSpace(batchNo) && !string.IsNullOrWhiteSpace(expiry) && DateTime.TryParse(expiry, out var expiryDate))
            {
                var sb = await stockQuery
                    .Where(sb => sb.BatchNo.Trim() == batchNo.Trim() && sb.Expiry.HasValue && sb.Expiry.Value.Date == expiryDate.Date)
                    .FirstOrDefaultAsync();
                if (sb != null)
                {
                    qtyBefore = sb.QtyOnHand;
                    firstBatchNo = sb.BatchNo;
                    firstExpiry = sb.Expiry?.ToString("yyyy-MM-dd");
                }
                costPerUnit = await _context.StockLedger
                    .Where(sl => sl.ProdId == prodId && sl.WarehouseId == warehouseId && sl.QtyIn > 0)
                    .AverageAsync(sl => (decimal?)sl.UnitCost) ?? 0m;
            }
            else
            {
                qtyBefore = await stockQuery.SumAsync(sb => sb.QtyOnHand);
                var firstStock = await stockQuery
                    .OrderBy(sb => sb.Expiry)
                    .ThenBy(sb => sb.BatchNo)
                    .Select(sb => new { sb.BatchNo, sb.Expiry })
                    .FirstOrDefaultAsync();
                if (firstStock != null)
                {
                    firstBatchNo = firstStock.BatchNo;
                    firstExpiry = firstStock.Expiry?.ToString("yyyy-MM-dd");
                }
                costPerUnit = await _context.StockLedger
                    .Where(sl => sl.ProdId == prodId && sl.WarehouseId == warehouseId && sl.QtyIn > 0)
                    .AverageAsync(sl => (decimal?)sl.UnitCost) ?? 0m;
            }

            // تشغيلات الصنف: نفس مصدر تقرير أرصدة الأصناف — StockLedger حركات الشراء فقط مع RemainingQty > 0
            // (تقرير الأرصدة يعرض التشغيلات من batchLedgerQuery: Purchase + RemainingQty > 0 ثم يطابقها مع Batches)
            var ledgerBatchList = await _context.StockLedger
                .AsNoTracking()
                .Where(sl => sl.ProdId == prodId && sl.WarehouseId == warehouseId
                    && sl.SourceType == "Purchase" && sl.QtyIn > 0 && (sl.RemainingQty ?? 0) > 0)
                .Select(sl => new { sl.BatchNo, sl.Expiry, RemainingQty = sl.RemainingQty ?? 0 })
                .ToListAsync();

            var rawLedgerBatches = ledgerBatchList
                .GroupBy(x => new { BatchNo = (x.BatchNo ?? "").Trim(), Expiry = x.Expiry.HasValue ? x.Expiry.Value.Date : (DateTime?)null })
                .Where(g => g.Key.Expiry.HasValue)
                .Select(g => new { g.Key.BatchNo, g.Key.Expiry, Qty = g.Sum(x => x.RemainingQty) })
                .Where(x => x.Qty > 0)
                .OrderBy(x => x.Expiry)
                .ThenBy(x => x.BatchNo)
                .ToList();

            // إضافة أي تشغيلة من جدول Batches للصنف لها رصيد في الدفتر (حتى لو المصدر غير Purchase، كالتسويات أو التحويلات)
            var ledgerKeys = new HashSet<string>(rawLedgerBatches.Select(x => (x.BatchNo ?? "").Trim() + "|" + (x.Expiry?.ToString("yyyy-MM-dd") ?? "")));
            var fromBatches = await _context.Batches
                .AsNoTracking()
                .Where(b => b.ProdId == prodId && b.IsActive)
                .OrderBy(b => b.Expiry).ThenBy(b => b.BatchNo)
                .Select(b => new { b.BatchNo, b.Expiry })
                .ToListAsync();

            foreach (var b in fromBatches)
            {
                var batchExpiry = b.Expiry.Date;
                var key = (b.BatchNo ?? "").Trim() + "|" + batchExpiry.ToString("yyyy-MM-dd");
                if (ledgerKeys.Contains(key)) continue;
                // تشغيلة من Batches بدون رصيد شراء: نتحقق إن كان لها رصيد من أي مصدر (تسوية/تحويل/مرتجع)
                var anyQty = await _context.StockLedger
                    .AsNoTracking()
                    .Where(sl => sl.ProdId == prodId && sl.WarehouseId == warehouseId && sl.QtyIn > 0 && (sl.RemainingQty ?? 0) > 0
                        && (sl.BatchNo ?? "").Trim() == (b.BatchNo ?? "").Trim() && sl.Expiry.HasValue && sl.Expiry.Value.Date == batchExpiry)
                    .SumAsync(sl => sl.RemainingQty ?? 0);
                if (anyQty > 0)
                {
                    rawLedgerBatches.Add(new { BatchNo = (b.BatchNo ?? "").Trim(), Expiry = (DateTime?)batchExpiry, Qty = anyQty });
                    ledgerKeys.Add(key);
                }
            }

            rawLedgerBatches = rawLedgerBatches.OrderBy(x => x.Expiry).ThenBy(x => x.BatchNo).ToList();

            var batches = rawLedgerBatches
                .Select(x => new
                {
                    batchNo = x.BatchNo,
                    expiry = x.Expiry,
                    expiryText = x.Expiry.HasValue ? x.Expiry.Value.ToString("yyyy-MM-dd") : "",
                    qty = (int)x.Qty
                })
                .ToList();

            int totalQty = batches.Sum(x => x.qty);

            return Json(new
            {
                ok = true,
                prodId = product.ProdId,
                prodName = product.ProdName,
                priceRetail = product.PriceRetail,
                qtyBefore,
                totalQty,
                costPerUnit,
                firstBatchNo = firstBatchNo ?? "",
                firstExpiry = firstExpiry ?? "",
                batches
            });
        }

        // =========================
        // AddLineJson — إضافة سطر للتسوية (JSON API)
        // =========================
        [HttpPost]
        public async Task<IActionResult> AddLineJson([FromBody] StockAdjustmentLineDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { ok = false, message = "بيانات غير صحيحة." });
            }

            var adjustment = await _context.StockAdjustments
                .Include(a => a.Lines)
                .FirstOrDefaultAsync(a => a.Id == dto.StockAdjustmentId);

            if (adjustment == null)
            {
                return NotFound(new { ok = false, message = "التسوية غير موجودة." });
            }

            if (adjustment.IsPosted)
            {
                return BadRequest(new { ok = false, message = "لا يمكن إضافة سطور لتسوية مترحلة." });
            }

            if (dto.ProductId <= 0)
            {
                return BadRequest(new { ok = false, message = "يجب اختيار صنف صحيح." });
            }

            // حساب الفروق
            int qtyDiff = dto.QtyAfter - dto.QtyBefore;
            decimal? costDiff = null;
            if (dto.CostPerUnit.HasValue)
            {
                costDiff = qtyDiff * dto.CostPerUnit.Value;
            }

            // البحث عن أو إنشاء Batch (ProdId + BatchNo + Expiry)
            int? batchId = dto.BatchId;
            if (!batchId.HasValue && !string.IsNullOrWhiteSpace(dto.BatchNo))
            {
                DateTime? expiryDate = null;
                if (!string.IsNullOrWhiteSpace(dto.Expiry) && DateTime.TryParse(dto.Expiry, out var ed))
                    expiryDate = ed.Date;

                var batch = expiryDate.HasValue
                    ? await _context.Batches.FirstOrDefaultAsync(b =>
                        b.BatchNo.Trim() == dto.BatchNo.Trim() &&
                        b.ProdId == dto.ProductId &&
                        b.Expiry.Date == expiryDate.Value.Date)
                    : await _context.Batches
                        .Where(b => b.BatchNo.Trim() == dto.BatchNo.Trim() && b.ProdId == dto.ProductId)
                        .OrderBy(b => b.Expiry)
                        .FirstOrDefaultAsync();
                if (batch != null)
                {
                    batchId = batch.BatchId;
                }
                else if (expiryDate.HasValue)
                {
                    var newBatch = new Batch
                    {
                        ProdId = dto.ProductId,
                        BatchNo = dto.BatchNo.Trim(),
                        Expiry = expiryDate.Value,
                        UnitCostDefault = dto.CostPerUnit,
                        PriceRetailBatch = dto.PriceRetail,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.Batches.Add(newBatch);
                    await _context.SaveChangesAsync();
                    batchId = newBatch.BatchId;
                }
            }

            // البحث عن سطر موجود لنفس الصنف والتشغيلة (تحديث بدل إضافة مكررة)
            var existingLine = await _context.StockAdjustmentLines
                .Include(l => l.Product)
                .Include(l => l.Batch)
                .FirstOrDefaultAsync(l =>
                    l.StockAdjustmentId == dto.StockAdjustmentId &&
                    l.ProductId == dto.ProductId &&
                    (l.BatchId == batchId || (l.BatchId == null && batchId == null)));

            bool isUpdate = existingLine != null;
            StockAdjustmentLine line;

            if (existingLine != null)
            {
                existingLine.QtyBefore = dto.QtyBefore;
                existingLine.QtyAfter = dto.QtyAfter;
                existingLine.QtyDiff = qtyDiff;
                existingLine.CostPerUnit = dto.CostPerUnit;
                existingLine.CostDiff = costDiff;
                existingLine.Note = dto.Note;
                line = existingLine;
                await _context.SaveChangesAsync();
            }
            else
            {
                line = new StockAdjustmentLine
                {
                    StockAdjustmentId = dto.StockAdjustmentId,
                    ProductId = dto.ProductId,
                    BatchId = batchId,
                    QtyBefore = dto.QtyBefore,
                    QtyAfter = dto.QtyAfter,
                    QtyDiff = qtyDiff,
                    CostPerUnit = dto.CostPerUnit,
                    CostDiff = costDiff,
                    Note = dto.Note
                };
                _context.StockAdjustmentLines.Add(line);
                await _context.SaveChangesAsync();
            }

            var product = await _context.Products.FindAsync(dto.ProductId);
            var batchEntity = batchId.HasValue ? await _context.Batches.FindAsync(batchId.Value) : null;
            return Json(new
            {
                ok = true,
                lineId = line.Id,
                isUpdate,
                productName = product?.ProdName ?? $"صنف #{dto.ProductId}",
                batchNo = batchEntity?.BatchNo ?? "-",
                expiryDisplay = batchEntity?.Expiry.ToString("yyyy-MM-dd") ?? "-",
                qtyBefore = line.QtyBefore,
                qtyAfter = line.QtyAfter,
                qtyDiff = line.QtyDiff,
                costPerUnit = line.CostPerUnit,
                costDiff = line.CostDiff
            });
        }

        // =========================
        // DeleteLineJson — حذف سطر من التسوية (JSON API)
        // =========================
        [HttpPost]
        public async Task<IActionResult> DeleteLineJson(int id)
        {
            var line = await _context.StockAdjustmentLines
                .Include(l => l.StockAdjustment)
                .FirstOrDefaultAsync(l => l.Id == id);

            if (line == null)
            {
                return NotFound(new { ok = false, message = "السطر غير موجود." });
            }

            if (line.StockAdjustment.IsPosted)
            {
                return BadRequest(new { ok = false, message = "لا يمكن حذف سطر من تسوية مترحلة." });
            }

            _context.StockAdjustmentLines.Remove(line);
            await _context.SaveChangesAsync();

            return Json(new { ok = true });
        }

        // =========================
        // ClearLinesJson — مسح كل سطور التسوية (JSON API)
        // =========================
        [HttpPost]
        public async Task<IActionResult> ClearLinesJson(int id)
        {
            var adjustment = await _context.StockAdjustments
                .Include(a => a.Lines)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (adjustment == null)
            {
                return NotFound(new { ok = false, message = "التسوية غير موجودة." });
            }

            if (adjustment.IsPosted)
            {
                return BadRequest(new { ok = false, message = "لا يمكن مسح سطور تسوية مترحلة." });
            }

            _context.StockAdjustmentLines.RemoveRange(adjustment.Lines);
            await _context.SaveChangesAsync();

            return Json(new { ok = true });
        }

        // =========================
        // PostAdjustment — ترحيل التسوية (JSON API)
        // =========================
        [HttpPost]
        public async Task<IActionResult> PostAdjustment(int id)
        {
            var adjustment = await _context.StockAdjustments
                .Include(a => a.Lines)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (adjustment == null)
            {
                return NotFound(new { ok = false, message = "التسوية غير موجودة." });
            }

            if (adjustment.IsPosted)
            {
                return BadRequest(new { ok = false, message = "هذه التسوية مترحلة بالفعل." });
            }

            if (!adjustment.Lines.Any())
            {
                return BadRequest(new { ok = false, message = "لا يمكن ترحيل تسوية بدون سطور." });
            }

            // استدعاء خدمة الترحيل
            var ledgerPostingService = HttpContext.RequestServices.GetRequiredService<ILedgerPostingService>();
            await ledgerPostingService.PostStockAdjustmentAsync(id, User?.Identity?.Name ?? "SYSTEM");

            return Json(new { ok = true, message = "تم الترحيل بنجاح." });
        }

        // =========================
        // OpenAdjustment — فتح التسوية (JSON API)
        // =========================
        [HttpPost]
        public async Task<IActionResult> OpenAdjustment(int id)
        {
            var adjustment = await _context.StockAdjustments.FindAsync(id);

            if (adjustment == null)
            {
                return NotFound(new { ok = false, message = "التسوية غير موجودة." });
            }

            if (!adjustment.IsPosted)
            {
                return BadRequest(new { ok = false, message = "هذه التسوية غير مترحلة." });
            }

            // استدعاء خدمة فتح التسوية (عكس الترحيل)
            var ledgerPostingService = HttpContext.RequestServices.GetRequiredService<ILedgerPostingService>();
            await ledgerPostingService.ReverseStockAdjustmentAsync(id, User?.Identity?.Name ?? "SYSTEM");

            return Json(new { ok = true, message = "تم فتح التسوية بنجاح." });
        }
    }

    // =========================
    // DTOs
    // =========================
    public class StockAdjustmentHeaderDto
    {
        public int Id { get; set; }
        public DateTime AdjustmentDate { get; set; }
        public int WarehouseId { get; set; }
        public string? ReferenceNo { get; set; }
        public string? Reason { get; set; }
    }

    public class StockAdjustmentLineDto
    {
        public int StockAdjustmentId { get; set; }
        public int ProductId { get; set; }
        public string? BatchNo { get; set; }
        public string? Expiry { get; set; }  // yyyy-MM-dd
        public decimal? PriceRetail { get; set; }
        public int? BatchId { get; set; }
        public int QtyBefore { get; set; }
        public int QtyAfter { get; set; }
        public decimal? CostPerUnit { get; set; }
        public string? Note { get; set; }
    }
}
