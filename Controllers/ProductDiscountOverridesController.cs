// كنترولر قائمة الخصم اليدوي للبيع: عرض كل صنف له رصيد مع الخصم الفعّال (يدوي إن وُجد، وإلا المرجّح)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ERP.Data;
using ERP.Filters;
using ERP.Models;
using ERP.Infrastructure;
using ERP.Security;
using ERP.Services;
using ERP.ViewModels;

namespace ERP.Controllers
{
    /// <summary>
    /// قائمة الخصم اليدوي للبيع: كل صنف في الأرصدة مع الخصم الفعّال (يدوي أو مرجّح).
    /// </summary>
    [RequirePermission(PermissionCodes.SalesDiscounts.DiscountOverrides_View)]
    public class ProductDiscountOverridesController : Controller
    {
        private readonly AppDbContext _db;
        private readonly StockAnalysisService _stockAnalysis;

        public ProductDiscountOverridesController(AppDbContext db, StockAnalysisService stockAnalysis)
        {
            _db = db;
            _stockAnalysis = stockAnalysis;
        }

        /// <summary>
        /// قائمة: كل (صنف + مخزن) له رصيد، مع الخصم الفعّال (يدوي إن وُجد، وإلا المرجّح).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "product",
            string? sort = "product",
            string? dir = "asc",
            int page = 1,
            int pageSize = 25,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null)
        {
            var s = (search ?? "").Trim();
            var sb = (searchBy ?? "product").Trim().ToLowerInvariant();
            var so = (sort ?? "product").Trim().ToLowerInvariant();
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            // 1) أصناف لها رصيد: تجميع من StockBatches حسب (ProdId, WarehouseId)
            var stockKeys = await _db.StockBatches
                .AsNoTracking()
                .Where(sb => sb.QtyOnHand > 0)
                .GroupBy(sb => new { sb.ProdId, sb.WarehouseId })
                .Where(g => g.Sum(x => x.QtyOnHand) > 0)
                .Select(g => new { g.Key.ProdId, g.Key.WarehouseId })
                .ToListAsync();

            if (stockKeys.Count == 0)
            {
                var emptyModel = new PagedResult<ProductDiscountEffectiveRow>(Array.Empty<ProductDiscountEffectiveRow>(), 1, pageSize, 0)
                {
                    Search = s, SearchBy = sb, SortColumn = so, SortDescending = desc,
                    UseDateRange = useDateRange, FromDate = fromDate, ToDate = toDate
                };
                ViewBag.SearchOptions = GetSearchOptions(sb);
                return View(emptyModel);
            }

            var prodIds = stockKeys.Select(k => k.ProdId).Distinct().ToList();
            var whIds = stockKeys.Select(k => k.WarehouseId).Distinct().ToList();

            // 2) أسماء الأصناف والمخازن
            var products = await _db.Products
                .AsNoTracking()
                .Where(p => prodIds.Contains(p.ProdId))
                .Select(p => new { p.ProdId, p.ProdName, ProdCode = p.ProdId.ToString() })
                .ToDictionaryAsync(p => p.ProdId);
            var warehouses = await _db.Warehouses
                .AsNoTracking()
                .Where(w => whIds.Contains(w.WarehouseId))
                .Select(w => new { w.WarehouseId, w.WarehouseName })
                .ToDictionaryAsync(w => w.WarehouseId);

            // 3) أحدث override لكل (ProductId, WarehouseId) حيث BatchId = null
            var overrides = await _db.ProductDiscountOverrides
                .AsNoTracking()
                .Where(o => prodIds.Contains(o.ProductId) && o.BatchId == null
                    && (o.WarehouseId == null || whIds.Contains(o.WarehouseId!.Value)))
                .OrderByDescending(o => o.CreatedAt)
                .Select(o => new { o.Id, o.ProductId, o.WarehouseId, o.OverrideDiscountPct, o.Reason, o.CreatedBy, o.CreatedAt })
                .ToListAsync();

            // أحدث override لكل مفتاح (ProductId, WarehouseId) أو (ProductId, null) — القائمة مرتبة بـ CreatedAt تنازلي
            var overrideByKey = new Dictionary<(int ProdId, int? WhId), (int Id, decimal Pct, string? Reason, string? CreatedBy, DateTime CreatedAt)>();
            foreach (var o in overrides)
            {
                var key = (o.ProductId, o.WarehouseId);
                if (!overrideByKey.ContainsKey(key))
                    overrideByKey[key] = (o.Id, o.OverrideDiscountPct, o.Reason, o.CreatedBy, o.CreatedAt);
            }

            // 4) بناء الصفوف: لكل (ProdId, WarehouseId) له رصيد نحدد الخصم الفعّال
            var rows = new List<ProductDiscountEffectiveRow>();
            foreach (var k in stockKeys)
            {
                var prodName = products.TryGetValue(k.ProdId, out var p) ? (p.ProdName ?? "") : "";
                var prodCode = products.TryGetValue(k.ProdId, out var p2) ? p2.ProdCode : k.ProdId.ToString();
                var whName = warehouses.TryGetValue(k.WarehouseId, out var w) ? w.WarehouseName : "الكل";

                decimal effectivePct;
                bool isManual;
                int? overrideId = null;
                string? reason = null;
                string? createdBy = null;
                DateTime? createdAt = null;

                if (overrideByKey.TryGetValue((k.ProdId, (int?)k.WarehouseId), out var ov))
                {
                    effectivePct = ov.Pct;
                    isManual = true;
                    overrideId = ov.Id;
                    reason = ov.Reason;
                    createdBy = ov.CreatedBy;
                    createdAt = ov.CreatedAt;
                }
                else if (overrideByKey.TryGetValue((k.ProdId, (int?)null), out var ovProd))
                {
                    effectivePct = ovProd.Pct;
                    isManual = true;
                    overrideId = ovProd.Id;
                    reason = ovProd.Reason;
                    createdBy = ovProd.CreatedBy;
                    createdAt = ovProd.CreatedAt;
                }
                else
                {
                    effectivePct = await _stockAnalysis.GetWeightedPurchaseDiscountForWarehouseAsync(k.ProdId, k.WarehouseId);
                    isManual = false;
                }

                rows.Add(new ProductDiscountEffectiveRow
                {
                    ProdId = k.ProdId,
                    ProductName = prodName,
                    ProdCode = prodCode,
                    WarehouseId = k.WarehouseId,
                    WarehouseName = whName,
                    EffectiveDiscountPct = effectivePct,
                    IsManual = isManual,
                    OverrideId = overrideId,
                    Reason = reason,
                    CreatedBy = createdBy,
                    CreatedAt = createdAt
                });
            }

            // 5) بحث
            if (!string.IsNullOrEmpty(s))
            {
                var lower = s.ToLowerInvariant();
                rows = rows.Where(r =>
                    sb == "product" ? (r.ProductName?.ToLowerInvariant().Contains(lower) == true || r.ProdCode == s) :
                    sb == "reason" ? (r.Reason?.ToLowerInvariant().Contains(lower) == true) :
                    sb == "createdby" ? (r.CreatedBy?.ToLowerInvariant().Contains(lower) == true) :
                    (r.ProductName?.ToLowerInvariant().Contains(lower) == true || r.ProdCode?.Contains(s) == true || r.Reason?.ToLowerInvariant().Contains(lower) == true || r.CreatedBy?.ToLowerInvariant().Contains(lower) == true)
                ).ToList();
            }

            if (useDateRange && (fromDate.HasValue || toDate.HasValue))
                rows = rows.Where(r => r.CreatedAt.HasValue && (!fromDate.HasValue || r.CreatedAt >= fromDate) && (!toDate.HasValue || r.CreatedAt <= toDate)).ToList();

            // 6) ترتيب
            rows = so switch
            {
                "id" => desc ? rows.OrderByDescending(r => r.OverrideId ?? r.ProdId).ToList() : rows.OrderBy(r => r.OverrideId ?? r.ProdId).ToList(),
                "product" => desc ? rows.OrderByDescending(r => r.ProductName).ToList() : rows.OrderBy(r => r.ProductName).ToList(),
                "warehouse" => desc ? rows.OrderByDescending(r => r.WarehouseName).ToList() : rows.OrderBy(r => r.WarehouseName).ToList(),
                "discount" => desc ? rows.OrderByDescending(r => r.EffectiveDiscountPct).ToList() : rows.OrderBy(r => r.EffectiveDiscountPct).ToList(),
                "created" => desc ? rows.OrderByDescending(r => r.CreatedAt ?? DateTime.MinValue).ToList() : rows.OrderBy(r => r.CreatedAt ?? DateTime.MinValue).ToList(),
                _ => desc ? rows.OrderByDescending(r => r.ProductName).ToList() : rows.OrderBy(r => r.ProductName).ToList()
            };

            int total = rows.Count;
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 25;
            var items = rows.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            var model = new PagedResult<ProductDiscountEffectiveRow>(items, page, pageSize, total)
            {
                Search = s,
                SearchBy = sb,
                SortColumn = so,
                SortDescending = desc,
                UseDateRange = useDateRange,
                FromDate = fromDate,
                ToDate = toDate
            };

            ViewBag.SearchOptions = GetSearchOptions(sb);
            return View(model);
        }

        private static SelectListItem[] GetSearchOptions(string sb)
        {
            return new[]
            {
                new SelectListItem("المنتج / الكود", "product", sb == "product"),
                new SelectListItem("السبب", "reason", sb == "reason"),
                new SelectListItem("أنشأه", "createdby", sb == "createdby"),
                new SelectListItem("الكل", "all", sb == "all")
            };
        }
    }
}
