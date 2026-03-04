using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;                      // لبناء ملف CSV
using System.Threading.Tasks;
using ERP.Data;
using ERP.Filters;
using ERP.Infrastructure;
using ERP.Models;
using ERP.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Controllers
{
    /// <summary>
    /// تقرير ربط FIFO بين الخروج والدخول (عرض فقط).
    /// </summary>
    [RequirePermission("StockFifoMap.Index")]
    public class StockFifoMapController : Controller
    {
        private readonly AppDbContext context;
        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        public StockFifoMapController(AppDbContext ctx) => context = ctx;

        /// <summary>
        /// شاشة عرض ربط FIFO مع بحث + ترتيب + فلتر من كود/إلى كود.
        /// </summary>
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",
            string? sort = "OutEntryId",
            string? dir = "asc",
            int page = 1,
            int pageSize = 50,
            int? fromCode = null,        // من MapId
            int? toCode = null,          // إلى MapId
            string? filterCol_map = null,
            string? filterCol_out = null,
            string? filterCol_in = null,
            string? filterCol_qty = null,
            string? filterCol_unitcost = null)
        {
            // الاستعلام الأساسي بدون تتبّع
            var q = context.StockFifoMap.AsNoTracking();

            // 1) الحقول النصّية (لا يوجد أعمدة string في هذا الجدول)
            var stringFields =
                new Dictionary<string, Expression<Func<StockFifoMap, string?>>>(StringComparer.OrdinalIgnoreCase);

            // 2) الحقول الرقمية للبحث
            var intFields =
                new Dictionary<string, Expression<Func<StockFifoMap, int>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["map"] = x => x.MapId,       // رقم الربط
                    ["out"] = x => x.OutEntryId,  // حركة الخروج
                    ["in"] = x => x.InEntryId,   // حركة الدخول
                    ["qty"] = x => x.Qty          // الكمية
                };

            // 3) حقول الترتيب
            var orderFields =
                new Dictionary<string, Expression<Func<StockFifoMap, object>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["OutEntryId"] = x => x.OutEntryId,
                    ["InEntryId"] = x => x.InEntryId,
                    ["Qty"] = x => x.Qty,
                    ["UnitCost"] = x => x.UnitCost,
                    ["MapId"] = x => x.MapId
                };

            // 4) تطبيق البحث + الترتيب بالدالة الموحّدة
            q = q.ApplySearchSort(
                    search, searchBy,
                    sort, dir,
                    stringFields, intFields, orderFields,
                    defaultSearchBy: "all",
                    defaultSortBy: "OutEntryId"
                );

            // 5) فلتر من كود / إلى كود على MapId
            if (fromCode.HasValue)
                q = q.Where(x => x.MapId >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(x => x.MapId <= toCode.Value);

            // 6) فلاتر أعمدة بنمط Excel
            if (!string.IsNullOrWhiteSpace(filterCol_map))
            {
                var ids = filterCol_map.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (ids.Count > 0)
                    q = q.Where(x => ids.Contains(x.MapId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_out))
            {
                var ids = filterCol_out.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (ids.Count > 0)
                    q = q.Where(x => ids.Contains(x.OutEntryId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_in))
            {
                var ids = filterCol_in.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (ids.Count > 0)
                    q = q.Where(x => ids.Contains(x.InEntryId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_qty))
            {
                var ids = filterCol_qty.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (ids.Count > 0)
                    q = q.Where(x => ids.Contains(x.Qty));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_unitcost))
            {
                var vals = filterCol_unitcost.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => decimal.TryParse(s.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (vals.Count > 0)
                    q = q.Where(x => vals.Contains(x.UnitCost));
            }

            // 7) الترقيم (Paging)
            if (pageSize <= 0) pageSize = 50;

            var totalRows = await q.CountAsync();
            var totalPages = (int)Math.Ceiling(totalRows / (double)pageSize);
            if (totalPages == 0) totalPages = 1;

            page = Math.Max(1, Math.Min(page, totalPages));

            var rows = await q
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // 8) قيم الواجهة (ViewBag) لربطها بالـ View
            ViewBag.Search = search ?? "";
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.Sort = sort ?? "OutEntryId";
            ViewBag.Dir = (dir?.ToLower() == "asc") ? "asc" : "desc";

            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalPages = totalPages;
            ViewBag.RangeStart = totalRows == 0 ? 0 : ((page - 1) * pageSize) + 1;
            ViewBag.RangeEnd = Math.Min(page * pageSize, totalRows);
            ViewBag.TotalRows = totalRows;

            // قيم فلتر من كود/إلى كود
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;

            // فلاتر الأعمدة
            ViewBag.FilterCol_Map = filterCol_map;
            ViewBag.FilterCol_Out = filterCol_out;
            ViewBag.FilterCol_In = filterCol_in;
            ViewBag.FilterCol_Qty = filterCol_qty;
            ViewBag.FilterCol_UnitCost = filterCol_unitcost;

            return View(rows);
        }

        /// <summary>
        /// API: جلب القيم المميزة لعمود محدد لفلترة الأعمدة بنمط Excel.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var col = (column ?? "").Trim().ToLowerInvariant();
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();

            IQueryable<StockFifoMap> q = context.StockFifoMap.AsNoTracking();

            List<(string Value, string Display)> items = col switch
            {
                "map" => (await q.Select(x => x.MapId).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "out" => (await q.Select(x => x.OutEntryId).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "in" => (await q.Select(x => x.InEntryId).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "qty" => (await q.Select(x => x.Qty).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "unitcost" => (await q.Select(x => x.UnitCost).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v.ToString(System.Globalization.CultureInfo.InvariantCulture), v.ToString("0.0000"))).ToList(),
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

        /// <summary>
        /// تصدير تقرير FIFO بفلتر البحث والكود (Excel/CSV).
        /// حاليًا نُرجع ملف CSV يمكن فتحه في Excel.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy = "all",
            string? sort = "OutEntryId",
            string? dir = "asc",
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_map = null,
            string? filterCol_out = null,
            string? filterCol_in = null,
            string? filterCol_qty = null,
            string? filterCol_unitcost = null,
            string format = "excel")
        {
            var q = context.StockFifoMap.AsNoTracking();

            var stringFields =
                new Dictionary<string, Expression<Func<StockFifoMap, string?>>>(StringComparer.OrdinalIgnoreCase);

            var intFields =
                new Dictionary<string, Expression<Func<StockFifoMap, int>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["map"] = x => x.MapId,
                    ["out"] = x => x.OutEntryId,
                    ["in"] = x => x.InEntryId,
                    ["qty"] = x => x.Qty
                };

            var orderFields =
                new Dictionary<string, Expression<Func<StockFifoMap, object>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["OutEntryId"] = x => x.OutEntryId,
                    ["InEntryId"] = x => x.InEntryId,
                    ["Qty"] = x => x.Qty,
                    ["UnitCost"] = x => x.UnitCost,
                    ["MapId"] = x => x.MapId
                };

            q = q.ApplySearchSort(
                    search, searchBy,
                    sort, dir,
                    stringFields, intFields, orderFields,
                    defaultSearchBy: "all",
                    defaultSortBy: "OutEntryId"
                );

            if (fromCode.HasValue)
                q = q.Where(x => x.MapId >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(x => x.MapId <= toCode.Value);

            if (!string.IsNullOrWhiteSpace(filterCol_map))
            {
                var ids = filterCol_map.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (ids.Count > 0)
                    q = q.Where(x => ids.Contains(x.MapId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_out))
            {
                var ids = filterCol_out.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (ids.Count > 0)
                    q = q.Where(x => ids.Contains(x.OutEntryId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_in))
            {
                var ids = filterCol_in.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (ids.Count > 0)
                    q = q.Where(x => ids.Contains(x.InEntryId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_qty))
            {
                var ids = filterCol_qty.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (ids.Count > 0)
                    q = q.Where(x => ids.Contains(x.Qty));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_unitcost))
            {
                var vals = filterCol_unitcost.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => decimal.TryParse(s.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (vals.Count > 0)
                    q = q.Where(x => vals.Contains(x.UnitCost));
            }

            var data = await q.ToListAsync();

            // بناء CSV بسيط: العناوين + الصفوف
            var sb = new StringBuilder();
            sb.AppendLine("MapId,OutEntryId,InEntryId,Qty,UnitCost");

            foreach (var x in data)
            {
                sb.AppendLine($"{x.MapId},{x.OutEntryId},{x.InEntryId},{x.Qty},{x.UnitCost}");
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = $"StockFifoMap_{DateTime.Now:yyyyMMdd_HHmmss}.csv";

            // سواء Excel أو CSV بنرجّع CSV (Excel يفتحه عادي)
            const string contentType = "text/csv";

            return File(bytes, contentType, fileName);
        }
    }
}
