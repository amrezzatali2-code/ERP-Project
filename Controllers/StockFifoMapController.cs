using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;                      // لبناء ملف CSV
using System.Threading.Tasks;
using ERP.Data;
using ERP.Infrastructure;
using ERP.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Controllers
{
    /// <summary>
    /// تقرير ربط FIFO بين الخروج والدخول (عرض فقط).
    /// </summary>
    public class StockFifoMapController : Controller
    {
        private readonly AppDbContext context;
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
            int? toCode = null)        // إلى MapId
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

            // 6) الترقيم (Paging)
            if (pageSize <= 0) pageSize = 50;

            var totalRows = await q.CountAsync();
            var totalPages = (int)Math.Ceiling(totalRows / (double)pageSize);
            if (totalPages == 0) totalPages = 1;

            page = Math.Max(1, Math.Min(page, totalPages));

            var rows = await q
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // 7) قيم الواجهة (ViewBag) لربطها بالـ View
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

            return View(rows);
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
