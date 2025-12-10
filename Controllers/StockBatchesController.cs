using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

using ERP.Data;
using ERP.Infrastructure;                 // ApplySearchSort
using ERP.Models;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ERP.Controllers
{
    /// <summary>
    /// تقرير مخزون التشغيلات — عرض فقط
    /// (WarehouseId + ProdId + BatchNo + Expiry + QtyOnHand)
    /// </summary>
    public class StockBatchesController : Controller
    {
        private readonly AppDbContext context;
        public StockBatchesController(AppDbContext ctx) => context = ctx;

        // GET: /StockBatches
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",
            string? sort = "Warehouse",
            string? dir = "asc",
            int page = 1,
            int pageSize = 50)
        {
            // الاستعلام الأساسي بدون تتبّع
            var q = context.StockBatches.AsNoTracking();

            // 1) الحقول النصّية (string) للبحث بالكلمات
            var stringFields =
                new Dictionary<string, Expression<Func<StockBatch, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["batch"] = x => x.BatchNo      // رقم التشغيلة
                };

            // 2) الحقول الرقمية int للبحث بالأرقام (المخزن/الصنف/الرصيد)
            var intFields =
                new Dictionary<string, Expression<Func<StockBatch, int>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["warehouse"] = x => x.WarehouseId,  // كود المخزن
                    ["product"] = x => x.ProdId,    // كود الصنف (ProdId)
                    ["qty"] = x => x.QtyOnHand     // الرصيد
                };

            // 3) حقول الترتيب
            var orderFields =
                new Dictionary<string, Expression<Func<StockBatch, object>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Warehouse"] = x => x.WarehouseId,
                    ["Product"] = x => x.ProdId,
                    ["Batch"] = x => x.BatchNo,
                    ["Expiry"] = x => x.Expiry,
                    ["Qty"] = x => x.QtyOnHand
                };

            // 4) تطبيق البحث + الترتيب بالدالة الموحّدة
            q = q.ApplySearchSort(
                    search, searchBy,
                    sort, dir,
                    stringFields, intFields, orderFields,
                    defaultSearchBy: "all",
                    defaultSortBy: "Warehouse"
                );

            // 5) الترقيم (Paging)
            var totalRows = await q.CountAsync();
            var totalPages = (int)Math.Ceiling(totalRows / (double)pageSize);
            page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

            var rows = await q
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // 6) خيارات البحث في البارشال
            ViewBag.SearchOptions = new List<SelectListItem>
            {
                new("الكل",        "all")
                {
                    Selected = (searchBy ?? "all")
                        .Equals("all", StringComparison.OrdinalIgnoreCase)
                },
                new("المخزن",      "warehouse"),
                new("الصنف",       "product"),
                new("التشغيلة",    "batch"),
                new("الرصيد (رقم)","qty"),
            };

            // 7) خيارات الترتيب
            ViewBag.SortOptions = new List<SelectListItem>
            {
                new("المخزن",    "Warehouse"),
                new("الصنف",     "Product"),
                new("التشغيلة",  "Batch"),
                new("الصلاحية",  "Expiry"),
                new("الرصيد",    "Qty"),
            };

            // 8) حالة الفلاتر للواجهة
            ViewBag.Search = search ?? "";
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.Sort = sort ?? "Warehouse";
            ViewBag.Dir = (dir?.ToLower() == "asc") ? "asc" : "desc";

            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalPages = totalPages;
            ViewBag.RangeStart = totalRows == 0 ? 0 : ((page - 1) * pageSize) + 1;
            ViewBag.RangeEnd = Math.Min(page * pageSize, totalRows);
            ViewBag.TotalRows = totalRows;

            return View(rows);
        }
    }
}
