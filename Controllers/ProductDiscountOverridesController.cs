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
        [RequirePermission("ProductDiscountOverrides.Index")]
        [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
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
                var emptyModel = new PagedResult<ProductDiscountGroupRow>(Array.Empty<ProductDiscountGroupRow>(), 1, pageSize, 0)
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

            // 2.1) تشغيلات لكل (ProdId, WarehouseId) من StockBatches
            var stockBatchDetails = await _db.StockBatches
                .AsNoTracking()
                .Where(x => x.QtyOnHand > 0 && prodIds.Contains(x.ProdId) && whIds.Contains(x.WarehouseId))
                .Select(x => new { x.ProdId, x.WarehouseId, x.BatchNo, x.Expiry })
                .ToListAsync();
            var batchMasterList = await _db.Batches
                .AsNoTracking()
                .Where(b => prodIds.Contains(b.ProdId))
                .Select(b => new { b.BatchId, b.ProdId, b.BatchNo, b.Expiry })
                .ToListAsync();
            var batchKeyToId = batchMasterList
                .ToDictionary(b => (b.ProdId, BatchNo: (b.BatchNo ?? "").Trim(), ExpiryDate: b.Expiry.Date), b => b.BatchId);

            var batchesPerKey = new Dictionary<(int ProdId, int WarehouseId), List<(int? BatchId, string BatchNo, DateTime? Expiry)>>();
            foreach (var g in stockBatchDetails.GroupBy(x => new { x.ProdId, x.WarehouseId }))
            {
                var distinct = g.Select(x => (BatchNo: (x.BatchNo ?? "").Trim(), Expiry: x.Expiry)).Distinct().ToList();
                var list = distinct.Select(x =>
                {
                    var expDate = x.Expiry?.Date ?? DateTime.MinValue;
                    int? batchId = batchKeyToId.TryGetValue((g.Key.ProdId, x.BatchNo, expDate), out var bid) ? bid : null;
                    return (BatchId: batchId, x.BatchNo, x.Expiry);
                }).ToList();
                batchesPerKey[(g.Key.ProdId, g.Key.WarehouseId)] = list;
            }

            // 3) أحدث override: مستوى صنف/مخزن (BatchId = null) ومستوى تشغيلة (BatchId != null)
            var overrides = await _db.ProductDiscountOverrides
                .AsNoTracking()
                .Where(o => prodIds.Contains(o.ProductId) && (o.WarehouseId == null || whIds.Contains(o.WarehouseId!.Value)))
                .OrderByDescending(o => o.CreatedAt)
                .Select(o => new { o.Id, o.ProductId, o.WarehouseId, o.BatchId, o.OverrideDiscountPct, o.Reason, o.CreatedBy, o.CreatedAt })
                .ToListAsync();

            var overrideByKey = new Dictionary<(int ProdId, int? WhId, int? BatchId), (int Id, decimal Pct, string? Reason, string? CreatedBy, DateTime CreatedAt)>();
            foreach (var o in overrides)
            {
                var key = (o.ProductId, o.WarehouseId, o.BatchId);
                if (!overrideByKey.ContainsKey(key))
                    overrideByKey[key] = (o.Id, o.OverrideDiscountPct, o.Reason, o.CreatedBy, o.CreatedAt);
            }

            // 4) بناء المجموعات: كل (ProdId, WarehouseId) → صف رئيسي + صفوف تشغيلات إن وُجدت (2+)
            var groups = new List<ProductDiscountGroupRow>();
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

                if (overrideByKey.TryGetValue((k.ProdId, (int?)k.WarehouseId, null), out var ov))
                {
                    effectivePct = ov.Pct;
                    isManual = true;
                    overrideId = ov.Id;
                    reason = ov.Reason;
                    createdBy = ov.CreatedBy;
                    createdAt = ov.CreatedAt;
                }
                else if (overrideByKey.TryGetValue((k.ProdId, (int?)null, null), out var ovProd))
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

                var mainRow = new ProductDiscountEffectiveRow
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
                };

                var batchRows = new List<ProductDiscountEffectiveRow>();
                if (batchesPerKey.TryGetValue((k.ProdId, k.WarehouseId), out var batchList) && batchList.Count >= 2)
                {
                    foreach (var bt in batchList.OrderBy(x => x.Expiry).ThenBy(x => x.BatchNo))
                    {
                        decimal batchPct;
                        bool batchManual;
                        int? batchOverrideId = null;
                        string? batchReason = null;
                        string? batchCreatedBy = null;
                        DateTime? batchCreatedAt = null;

                        if (bt.BatchId.HasValue && overrideByKey.TryGetValue((k.ProdId, (int?)k.WarehouseId, bt.BatchId), out var bov))
                        {
                            batchPct = bov.Pct;
                            batchManual = true;
                            batchOverrideId = bov.Id;
                            batchReason = bov.Reason;
                            batchCreatedBy = bov.CreatedBy;
                            batchCreatedAt = bov.CreatedAt;
                        }
                        else if (bt.BatchId.HasValue && overrideByKey.TryGetValue((k.ProdId, (int?)null, bt.BatchId), out var bovNullWh))
                        {
                            // خصم التشغيلة قد يكون محفوظاً من تقرير الأرصدة بدون تحديد مخزن — نعرضه كيدوي
                            batchPct = bovNullWh.Pct;
                            batchManual = true;
                            batchOverrideId = bovNullWh.Id;
                            batchReason = bovNullWh.Reason;
                            batchCreatedBy = bovNullWh.CreatedBy;
                            batchCreatedAt = bovNullWh.CreatedAt;
                        }
                        else
                        {
                            batchPct = await _stockAnalysis.GetEffectivePurchaseDiscountAsync(k.ProdId, k.WarehouseId, bt.BatchId);
                            batchManual = false;
                        }

                        batchRows.Add(new ProductDiscountEffectiveRow
                        {
                            ProdId = k.ProdId,
                            ProductName = prodName,
                            ProdCode = prodCode,
                            WarehouseId = k.WarehouseId,
                            WarehouseName = whName,
                            BatchId = bt.BatchId,
                            BatchNo = bt.BatchNo,
                            Expiry = bt.Expiry,
                            EffectiveDiscountPct = batchPct,
                            IsManual = batchManual,
                            OverrideId = batchOverrideId,
                            Reason = batchReason,
                            CreatedBy = batchCreatedBy,
                            CreatedAt = batchCreatedAt
                        });
                    }
                }

                groups.Add(new ProductDiscountGroupRow { MainRow = mainRow, BatchRows = batchRows });
            }

            // 5) بحث (حسب الصف الرئيسي)
            if (!string.IsNullOrEmpty(s))
            {
                var lower = s.ToLowerInvariant();
                groups = groups.Where(g =>
                    sb == "product" ? (g.MainRow.ProductName?.ToLowerInvariant().Contains(lower) == true || g.MainRow.ProdCode == s) :
                    sb == "reason" ? (g.MainRow.Reason?.ToLowerInvariant().Contains(lower) == true) :
                    sb == "createdby" ? (g.MainRow.CreatedBy?.ToLowerInvariant().Contains(lower) == true) :
                    (g.MainRow.ProductName?.ToLowerInvariant().Contains(lower) == true || g.MainRow.ProdCode?.Contains(s) == true || g.MainRow.Reason?.ToLowerInvariant().Contains(lower) == true || g.MainRow.CreatedBy?.ToLowerInvariant().Contains(lower) == true)
                ).ToList();
            }

            if (useDateRange && (fromDate.HasValue || toDate.HasValue))
                groups = groups.Where(g => g.MainRow.CreatedAt.HasValue && (!fromDate.HasValue || g.MainRow.CreatedAt >= fromDate) && (!toDate.HasValue || g.MainRow.CreatedAt <= toDate)).ToList();

            // 6) ترتيب حسب الصف الرئيسي
            groups = so switch
            {
                "id" => desc ? groups.OrderByDescending(g => g.MainRow.OverrideId ?? g.MainRow.ProdId).ToList() : groups.OrderBy(g => g.MainRow.OverrideId ?? g.MainRow.ProdId).ToList(),
                "product" => desc ? groups.OrderByDescending(g => g.MainRow.ProductName).ToList() : groups.OrderBy(g => g.MainRow.ProductName).ToList(),
                "warehouse" => desc ? groups.OrderByDescending(g => g.MainRow.WarehouseName).ToList() : groups.OrderBy(g => g.MainRow.WarehouseName).ToList(),
                "discount" => desc ? groups.OrderByDescending(g => g.MainRow.EffectiveDiscountPct).ToList() : groups.OrderBy(g => g.MainRow.EffectiveDiscountPct).ToList(),
                "created" => desc ? groups.OrderByDescending(g => g.MainRow.CreatedAt ?? DateTime.MinValue).ToList() : groups.OrderBy(g => g.MainRow.CreatedAt ?? DateTime.MinValue).ToList(),
                _ => desc ? groups.OrderByDescending(g => g.MainRow.ProductName).ToList() : groups.OrderBy(g => g.MainRow.ProductName).ToList()
            };

            int total = groups.Count;
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 25;
            var items = groups.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            var model = new PagedResult<ProductDiscountGroupRow>(items, page, pageSize, total)
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

        /// <summary>عرض نموذج تعديل سجل خصم يدوي.</summary>
        [HttpGet]
        [RequirePermission("ProductDiscountOverrides.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            var entity = await _db.ProductDiscountOverrides
                .AsNoTracking()
                .Include(o => o.Product)
                .Include(o => o.Warehouse)
                .FirstOrDefaultAsync(o => o.Id == id);
            if (entity == null)
                return NotFound();
            return View(entity);
        }

        /// <summary>حفظ تعديل سجل الخصم اليدوي.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("ProductDiscountOverrides.Edit")]
        public async Task<IActionResult> Edit(int id, [FromForm] decimal overrideDiscountPct, [FromForm] string? reason)
        {
            var entity = await _db.ProductDiscountOverrides.FindAsync(id);
            if (entity == null)
                return NotFound();
            entity.OverrideDiscountPct = Math.Min(100m, Math.Max(0m, overrideDiscountPct));
            entity.Reason = reason?.Length > 200 ? reason.Substring(0, 200) : reason;
            await _db.SaveChangesAsync();
            TempData["SuccessMessage"] = "تم تحديث الخصم اليدوي.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>تحديث من داخل الجدول (نموذج داخل الصف).</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("ProductDiscountOverrides.Edit")]
        public async Task<IActionResult> Update(int id, [FromForm] decimal overrideDiscountPct, [FromForm] string? reason)
        {
            var entity = await _db.ProductDiscountOverrides.FindAsync(id);
            if (entity == null)
                return NotFound();
            entity.OverrideDiscountPct = Math.Min(100m, Math.Max(0m, overrideDiscountPct));
            entity.Reason = reason?.Length > 200 ? reason.Substring(0, 200) : reason;
            await _db.SaveChangesAsync();
            TempData["SuccessMessage"] = "تم حفظ التعديل.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>صفحة تأكيد حذف سجل الخصم اليدوي.</summary>
        [HttpGet]
        [RequirePermission("ProductDiscountOverrides.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity = await _db.ProductDiscountOverrides
                .AsNoTracking()
                .Include(o => o.Product)
                .Include(o => o.Warehouse)
                .FirstOrDefaultAsync(o => o.Id == id);
            if (entity == null)
                return NotFound();
            return View(entity);
        }

        /// <summary>تنفيذ الحذف.</summary>
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [RequirePermission("ProductDiscountOverrides.Delete")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (id <= 0)
                return NotFound();
            var entity = await _db.ProductDiscountOverrides.FindAsync(id);
            if (entity == null)
                return NotFound();
            _db.ProductDiscountOverrides.Remove(entity);
            await _db.SaveChangesAsync();
            TempData["SuccessMessage"] = "تم حذف سجل الخصم اليدوي.";
            return RedirectToAction(nameof(Index), new { _t = DateTime.UtcNow.Ticks });
        }
    }
}
