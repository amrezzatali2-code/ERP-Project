using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ClosedXML.Excel;                          // لتصدير Excel
using ERP.Data;                                // سياق قاعدة البيانات
using ERP.Filters;
using ERP.Infrastructure;                      // PagedResult + UserActivityLogger
using ERP.Models;                              // Batch, UserActionType...
using ERP.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;      // القوائم المنسدلة
using Microsoft.EntityFrameworkCore;           // LINQ to Entities

namespace ERP.Controllers
{
    /// <summary>
    /// كونترولر إدارة جدول التشغيلات Batch:
    /// - عرض قائمة التشغيلات بنظام القوائم الموحد.
    /// - إضافة / تعديل / حذف تشغيلة.
    /// - حذف محدد / حذف الكل.
    /// - تصدير (Excel / CSV).
    /// </summary>
    [RequirePermission("Batches.Index")]
    public class BatchesController : Controller
    {
        private readonly AppDbContext _db;

        private static readonly char[] _filterSep = new[] { '|', ',', ';' };
        private readonly IUserActivityLogger _activityLogger;
        private static readonly CultureInfo _inv = CultureInfo.InvariantCulture;

        private static IQueryable<Batch> ApplyInt32ExprFilter(IQueryable<Batch> q, string? exprRaw, string field)
        {
            if (string.IsNullOrWhiteSpace(exprRaw)) return q;
            var expr = exprRaw.Trim();

            if (expr.StartsWith("<=", StringComparison.Ordinal) && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, _inv, out var le))
            {
                return field switch
                {
                    "id" => q.Where(x => x.BatchId <= le),
                    "prod" => q.Where(x => x.ProdId <= le),
                    _ => q
                };
            }
            if (expr.StartsWith(">=", StringComparison.Ordinal) && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, _inv, out var ge))
            {
                return field switch
                {
                    "id" => q.Where(x => x.BatchId >= ge),
                    "prod" => q.Where(x => x.ProdId >= ge),
                    _ => q
                };
            }
            if (expr.StartsWith("<", StringComparison.Ordinal) && !expr.StartsWith("<=", StringComparison.Ordinal) && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, _inv, out var lt))
            {
                return field switch
                {
                    "id" => q.Where(x => x.BatchId < lt),
                    "prod" => q.Where(x => x.ProdId < lt),
                    _ => q
                };
            }
            if (expr.StartsWith(">", StringComparison.Ordinal) && !expr.StartsWith(">=", StringComparison.Ordinal) && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, _inv, out var gt))
            {
                return field switch
                {
                    "id" => q.Where(x => x.BatchId > gt),
                    "prod" => q.Where(x => x.ProdId > gt),
                    _ => q
                };
            }
            if ((expr.Contains(':') || expr.Contains('-')) && !expr.StartsWith("-", StringComparison.Ordinal))
            {
                var sep = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 &&
                    int.TryParse(parts[0].Trim(), NumberStyles.Any, _inv, out var from) &&
                    int.TryParse(parts[1].Trim(), NumberStyles.Any, _inv, out var to))
                {
                    if (from > to) (from, to) = (to, from);
                    return field switch
                    {
                        "id" => q.Where(x => x.BatchId >= from && x.BatchId <= to),
                        "prod" => q.Where(x => x.ProdId >= from && x.ProdId <= to),
                        _ => q
                    };
                }
            }
            if (int.TryParse(expr, NumberStyles.Any, _inv, out var exact))
            {
                return field switch
                {
                    "id" => q.Where(x => x.BatchId == exact),
                    "prod" => q.Where(x => x.ProdId == exact),
                    _ => q
                };
            }
            return q;
        }

        private static IQueryable<Batch> ApplyDecimalExprFilter(IQueryable<Batch> q, string? exprRaw, string field)
        {
            if (string.IsNullOrWhiteSpace(exprRaw)) return q;
            var expr = exprRaw.Trim();

            if (expr.StartsWith("<=", StringComparison.Ordinal) && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, _inv, out var le))
            {
                return field switch
                {
                    "price" => q.Where(x => (x.PriceRetailBatch ?? 0m) <= le),
                    "cost" => q.Where(x => (x.UnitCostDefault ?? 0m) <= le),
                    _ => q
                };
            }
            if (expr.StartsWith(">=", StringComparison.Ordinal) && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, _inv, out var ge))
            {
                return field switch
                {
                    "price" => q.Where(x => (x.PriceRetailBatch ?? 0m) >= ge),
                    "cost" => q.Where(x => (x.UnitCostDefault ?? 0m) >= ge),
                    _ => q
                };
            }
            if (expr.StartsWith("<", StringComparison.Ordinal) && !expr.StartsWith("<=", StringComparison.Ordinal) && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, _inv, out var lt))
            {
                return field switch
                {
                    "price" => q.Where(x => (x.PriceRetailBatch ?? 0m) < lt),
                    "cost" => q.Where(x => (x.UnitCostDefault ?? 0m) < lt),
                    _ => q
                };
            }
            if (expr.StartsWith(">", StringComparison.Ordinal) && !expr.StartsWith(">=", StringComparison.Ordinal) && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, _inv, out var gt))
            {
                return field switch
                {
                    "price" => q.Where(x => (x.PriceRetailBatch ?? 0m) > gt),
                    "cost" => q.Where(x => (x.UnitCostDefault ?? 0m) > gt),
                    _ => q
                };
            }
            if ((expr.Contains(':') || expr.Contains('-')) && !expr.StartsWith("-", StringComparison.Ordinal))
            {
                var sep = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 &&
                    decimal.TryParse(parts[0].Trim(), NumberStyles.Any, _inv, out var from) &&
                    decimal.TryParse(parts[1].Trim(), NumberStyles.Any, _inv, out var to))
                {
                    if (from > to) (from, to) = (to, from);
                    return field switch
                    {
                        "price" => q.Where(x => (x.PriceRetailBatch ?? 0m) >= from && (x.PriceRetailBatch ?? 0m) <= to),
                        "cost" => q.Where(x => (x.UnitCostDefault ?? 0m) >= from && (x.UnitCostDefault ?? 0m) <= to),
                        _ => q
                    };
                }
            }
            if (decimal.TryParse(expr, NumberStyles.Any, _inv, out var exact))
            {
                return field switch
                {
                    "price" => q.Where(x => (x.PriceRetailBatch ?? 0m) == exact),
                    "cost" => q.Where(x => (x.UnitCostDefault ?? 0m) == exact),
                    _ => q
                };
            }
            return q;
        }

        public BatchesController(AppDbContext context, IUserActivityLogger activityLogger)
        {
            _db = context;
            _activityLogger = activityLogger;
        }

        /// <summary>
        /// API: جلب القيم المميزة لعمود في قائمة التشغيلات (لنمط فلترة الأعمدة مثل Excel).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var col = (column ?? "").Trim().ToLowerInvariant();
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();

            IQueryable<Batch> q = _db.Batches.AsNoTracking().Include(b => b.Product);

            List<(string Value, string Display)> items = col switch
            {
                "id" => (await q.Select(b => b.BatchId).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "prod" => (await q.Where(b => b.Product != null && b.Product.ProdName != null)
                        .Select(b => b.Product!.ProdName!)
                        .Distinct()
                        .OrderBy(v => v)
                        .Take(500)
                        .ToListAsync())
                    .Select(v => (v, v)).ToList(),
                "batchno" => (await q.Select(b => b.BatchNo).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v, v)).ToList(),
                "expiry" => (await q.Select(b => new { b.Expiry.Year, b.Expiry.Month }).Distinct()
                        .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month)
                        .Take(200)
                        .ToListAsync())
                    .Select(x => ($"{x.Year}-{x.Month:D2}", $"{x.Year}/{x.Month:D2}")).ToList(),
                "price" => (await q.Where(b => b.PriceRetailBatch.HasValue).Select(b => b.PriceRetailBatch!.Value).Distinct().OrderBy(v => v).Take(200).ToListAsync())
                    .Select(v => (v.ToString(System.Globalization.CultureInfo.InvariantCulture), v.ToString("0.00"))).ToList(),
                "cost" => (await q.Where(b => b.UnitCostDefault.HasValue).Select(b => b.UnitCostDefault!.Value).Distinct().OrderBy(v => v).Take(200).ToListAsync())
                    .Select(v => (v.ToString(System.Globalization.CultureInfo.InvariantCulture), v.ToString("0.####"))).ToList(),
                "active" => new List<(string, string)>
                {
                    ("true", "نشطة"),
                    ("false", "موقوفة")
                },
                "created" => (await q.Select(b => new { b.CreatedAt.Year, b.CreatedAt.Month }).Distinct()
                        .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month)
                        .Take(200)
                        .ToListAsync())
                    .Select(x => ($"{x.Year}-{x.Month:D2}", $"{x.Year}/{x.Month:D2}")).ToList(),
                "updated" => (await q.Where(b => b.UpdatedAt.HasValue)
                        .Select(b => new { b.UpdatedAt!.Value.Year, b.UpdatedAt.Value.Month })
                        .Distinct()
                        .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month)
                        .Take(200)
                        .ToListAsync())
                    .Select(x => ($"{x.Year}-{x.Month:D2}", $"{x.Year}/{x.Month:D2}")).ToList(),
                _ => new List<(string Value, string Display)>()
            };

            if (!string.IsNullOrEmpty(searchTerm) && items.Count > 0)
            {
                items = items.Where(x => (x.Display ?? x.Value).ToLowerInvariant().Contains(searchTerm)).ToList();
            }

            return Json(items.Select(x => new { value = x.Value, display = x.Display }));
        }

        // =========================================================
        // دالة مساعدة: تحميل قائمة الأصناف للكومبوبوكس
        // =========================================================
        private async Task FillProductsDropDownAsync(int? selectedProdId = null)
        {
            // نجيب الأصناف مرتبة بالاسم لسهولة الاختيار
            var products = await _db.Products
                .OrderBy(p => p.ProdName)
                .Select(p => new SelectListItem
                {
                    Value = p.ProdId.ToString(),          // لو عندك اسم مختلف غيّره هنا
                    Text = p.ProdName
                })
                .ToListAsync();

            ViewBag.Products = products;                     // متغير: القائمة المنسدلة في الفيو
            ViewBag.SelectedProdId = selectedProdId;         // متغير: الصنف المختار مبدئياً
        }





        // =========================================================
        // دالة مساعدة: تطبيق البحث / الفلترة / الترتيب
        // =========================================================
        private IQueryable<Batch> SearchSortFilter(
            string? search,
            string? searchBy,
            string? searchMode,
            string? sort,
            string? dir,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            string? dateField,
            int? fromCode,
            int? toCode,
            string? filterCol_id = null,
            string? filterCol_prod = null,
            string? filterCol_batchno = null,
            string? filterCol_expiry = null,
            string? filterCol_price = null,
            string? filterCol_cost = null,
            string? filterCol_active = null,
            string? filterCol_created = null,
            string? filterCol_updated = null,
            string? filterCol_idExpr = null,
            string? filterCol_prodExpr = null,
            string? filterCol_priceExpr = null,
            string? filterCol_costExpr = null)
        {
            // الاستعلام الأساسي من جدول Batch مع ربط اسم الصنف
            var q = _db.Batches
                .Include(b => b.Product)
                .AsNoTracking()
                .AsQueryable();

            // ------------------------------
            // فلاتر أعمدة بنمط Excel
            // ------------------------------
            if (!string.IsNullOrWhiteSpace(filterCol_idExpr))
            {
                q = ApplyInt32ExprFilter(q, filterCol_idExpr, "id");
            }
            else if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue)
                    .Select(v => v!.Value)
                    .ToList();
                if (ids.Count > 0)
                    q = q.Where(b => ids.Contains(b.BatchId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_prodExpr))
            {
                q = ApplyInt32ExprFilter(q, filterCol_prodExpr, "prod");
            }
            else if (!string.IsNullOrWhiteSpace(filterCol_prod))
            {
                var vals = filterCol_prod.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(b => b.Product != null && b.Product.ProdName != null && vals.Contains(b.Product.ProdName));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_batchno))
            {
                var vals = filterCol_batchno.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(b => vals.Contains(b.BatchNo));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_expiry))
            {
                var parts = filterCol_expiry.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => x.Length == 7 && x[4] == '-')
                    .ToList();

                var dateFilters = new List<(int Year, int Month)>();
                foreach (var p in parts)
                {
                    if (int.TryParse(p.Substring(0, 4), out var y) &&
                        int.TryParse(p.Substring(5, 2), out var m) &&
                        m >= 1 && m <= 12)
                    {
                        dateFilters.Add((y, m));
                    }
                }

                if (dateFilters.Count > 0)
                {
                    q = q.Where(b =>
                        dateFilters.Any(df => b.Expiry.Year == df.Year && b.Expiry.Month == df.Month));
                }
            }

            if (!string.IsNullOrWhiteSpace(filterCol_priceExpr))
            {
                q = ApplyDecimalExprFilter(q, filterCol_priceExpr, "price");
            }
            else if (!string.IsNullOrWhiteSpace(filterCol_price))
            {
                var vals = filterCol_price.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(v => v.HasValue)
                    .Select(v => v!.Value)
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(b => b.PriceRetailBatch.HasValue && vals.Contains(b.PriceRetailBatch.Value));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_costExpr))
            {
                q = ApplyDecimalExprFilter(q, filterCol_costExpr, "cost");
            }
            else if (!string.IsNullOrWhiteSpace(filterCol_cost))
            {
                var vals = filterCol_cost.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(v => v.HasValue)
                    .Select(v => v!.Value)
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(b => b.UnitCostDefault.HasValue && vals.Contains(b.UnitCostDefault.Value));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_active))
            {
                var vals = filterCol_active.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim().ToLowerInvariant())
                    .Where(x => x == "1" || x == "0" || x == "true" || x == "false" || x == "نشطة" || x == "موقوفة")
                    .ToList();

                if (vals.Count > 0)
                {
                    bool includeActive = vals.Any(x => x == "1" || x == "true" || x == "نشطة");
                    bool includeInactive = vals.Any(x => x == "0" || x == "false" || x == "موقوفة");
                    if (includeActive && !includeInactive)
                        q = q.Where(b => b.IsActive);
                    else if (!includeActive && includeInactive)
                        q = q.Where(b => !b.IsActive);
                }
            }

            if (!string.IsNullOrWhiteSpace(filterCol_created))
            {
                var parts = filterCol_created.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => x.Length == 7 && x[4] == '-')
                    .ToList();

                var dateFilters = new List<(int Year, int Month)>();
                foreach (var p in parts)
                {
                    if (int.TryParse(p.Substring(0, 4), out var y) &&
                        int.TryParse(p.Substring(5, 2), out var m) &&
                        m >= 1 && m <= 12)
                    {
                        dateFilters.Add((y, m));
                    }
                }

                if (dateFilters.Count > 0)
                {
                    q = q.Where(b =>
                        dateFilters.Any(df => b.CreatedAt.Year == df.Year && b.CreatedAt.Month == df.Month));
                }
            }

            if (!string.IsNullOrWhiteSpace(filterCol_updated))
            {
                var parts = filterCol_updated.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => x.Length == 7 && x[4] == '-')
                    .ToList();

                var dateFilters = new List<(int Year, int Month)>();
                foreach (var p in parts)
                {
                    if (int.TryParse(p.Substring(0, 4), out var y) &&
                        int.TryParse(p.Substring(5, 2), out var m) &&
                        m >= 1 && m <= 12)
                    {
                        dateFilters.Add((y, m));
                    }
                }

                if (dateFilters.Count > 0)
                {
                    q = q.Where(b =>
                        b.UpdatedAt.HasValue &&
                        dateFilters.Any(df => b.UpdatedAt.Value.Year == df.Year && b.UpdatedAt.Value.Month == df.Month));
                }
            }

            // ------------------------------
            // فلتر التاريخ (CreatedAt / UpdatedAt)
            // ------------------------------
            bool dateFilterActive = useDateRange || fromDate.HasValue || toDate.HasValue;
            string df = string.IsNullOrWhiteSpace(dateField) ? "CreatedAt" : dateField;

            if (dateFilterActive)
            {
                bool filterOnUpdated = df.Equals("UpdatedAt", StringComparison.OrdinalIgnoreCase);

                if (filterOnUpdated)
                {
                    if (fromDate.HasValue)
                    {
                        q = q.Where(b => b.UpdatedAt.HasValue && b.UpdatedAt.Value >= fromDate.Value);
                    }
                    if (toDate.HasValue)
                    {
                        q = q.Where(b => b.UpdatedAt.HasValue && b.UpdatedAt.Value <= toDate.Value);
                    }
                }
                else
                {
                    if (fromDate.HasValue)
                    {
                        q = q.Where(b => b.CreatedAt >= fromDate.Value);
                    }
                    if (toDate.HasValue)
                    {
                        q = q.Where(b => b.CreatedAt <= toDate.Value);
                    }
                }
            }

            // ------------------------------
            // فلتر كود من/إلى (BatchId)
            // ------------------------------
            if (fromCode.HasValue)
            {
                int cf = fromCode.Value;
                q = q.Where(b => b.BatchId >= cf);
            }

            if (toCode.HasValue)
            {
                int ct = toCode.Value;
                q = q.Where(b => b.BatchId <= ct);
            }

            // ------------------------------
            // البحث (نص حر)
            // ------------------------------
            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = search.Trim();
                string sb = (searchBy ?? "batchno").ToLower();
                var smRaw = (searchMode ?? "contains").Trim().ToLowerInvariant();
                string sm = smRaw == "starts" || smRaw == "ends" ? smRaw : "contains";

                switch (sb)
                {
                    case "id":      // كود التشغيلة
                        q = q.Where(b => b.BatchId.ToString() == term);
                        break;

                    case "prod":    // كود الصنف أو اسمه
                        if (sm == "starts")
                        {
                            q = q.Where(b =>
                                b.ProdId.ToString() == term ||
                                (b.Product != null && b.Product.ProdName != null && b.Product.ProdName.StartsWith(term)));
                        }
                        else if (sm == "ends")
                        {
                            q = q.Where(b =>
                                b.ProdId.ToString() == term ||
                                (b.Product != null && b.Product.ProdName != null && b.Product.ProdName.EndsWith(term)));
                        }
                        else
                        {
                            q = q.Where(b =>
                                b.ProdId.ToString() == term ||
                                (b.Product != null && b.Product.ProdName != null && b.Product.ProdName.Contains(term)));
                        }
                        break;

                    case "prodname":
                        if (sm == "starts")
                            q = q.Where(b => b.Product != null && b.Product.ProdName != null && b.Product.ProdName.StartsWith(term));
                        else if (sm == "ends")
                            q = q.Where(b => b.Product != null && b.Product.ProdName != null && b.Product.ProdName.EndsWith(term));
                        else
                            q = q.Where(b => b.Product != null && b.Product.ProdName != null && b.Product.ProdName.Contains(term));
                        break;

                    case "expiry":
                        if (DateTime.TryParse(term, out var dtExp))
                        {
                            var dateOnly = dtExp.Date;
                            q = q.Where(b => b.Expiry.Date == dateOnly);
                        }
                        break;

                    case "created":
                        if (DateTime.TryParse(term, out var dtCr))
                        {
                            var dateOnly = dtCr.Date;
                            q = q.Where(b => b.CreatedAt.Date == dateOnly);
                        }
                        break;

                    case "updated":
                        if (DateTime.TryParse(term, out var dtUp))
                        {
                            var dateOnly = dtUp.Date;
                            q = q.Where(b =>
                                b.UpdatedAt.HasValue &&
                                b.UpdatedAt.Value.Date == dateOnly);
                        }
                        break;

                    case "all":
                        if (sm == "starts")
                        {
                            q = q.Where(b =>
                                b.BatchId.ToString() == term ||
                                b.ProdId.ToString() == term ||
                                b.BatchNo.StartsWith(term) ||
                                (b.Product != null && b.Product.ProdName != null && b.Product.ProdName.StartsWith(term)));
                        }
                        else if (sm == "ends")
                        {
                            q = q.Where(b =>
                                b.BatchId.ToString() == term ||
                                b.ProdId.ToString() == term ||
                                b.BatchNo.EndsWith(term) ||
                                (b.Product != null && b.Product.ProdName != null && b.Product.ProdName.EndsWith(term)));
                        }
                        else
                        {
                            q = q.Where(b =>
                                b.BatchId.ToString() == term ||
                                b.ProdId.ToString() == term ||
                                b.BatchNo.Contains(term) ||
                                (b.Product != null && b.Product.ProdName != null && b.Product.ProdName.Contains(term)));
                        }
                        break;

                    case "batchno":
                    default:
                        if (sm == "starts")
                            q = q.Where(b => b.BatchNo.StartsWith(term));
                        else if (sm == "ends")
                            q = q.Where(b => b.BatchNo.EndsWith(term));
                        else
                            q = q.Where(b => b.BatchNo.Contains(term));
                        break;
                }
            }

            // ------------------------------
            // الترتيب (مع Tie-breaker ثابت)
            // ------------------------------
            bool descending = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            string sortCol = (sort ?? "expiry").ToLower();

            IOrderedQueryable<Batch> ordered;

            switch (sortCol)
            {
                case "id":
                    ordered = descending
                        ? q.OrderByDescending(b => b.BatchId)
                        : q.OrderBy(b => b.BatchId);
                    break;

                case "batchno":
                    ordered = descending
                        ? q.OrderByDescending(b => b.BatchNo).ThenByDescending(b => b.BatchId)
                        : q.OrderBy(b => b.BatchNo).ThenBy(b => b.BatchId);
                    break;

                case "prod":
                    ordered = descending
                        ? q.OrderByDescending(b => b.Product!.ProdName).ThenByDescending(b => b.BatchId)
                        : q.OrderBy(b => b.Product!.ProdName).ThenBy(b => b.BatchId);
                    break;

                case "created":
                    ordered = descending
                        ? q.OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.BatchId)
                        : q.OrderBy(b => b.CreatedAt).ThenBy(b => b.BatchId);
                    break;

                case "updated":
                    ordered = descending
                        ? q.OrderByDescending(b => b.UpdatedAt).ThenByDescending(b => b.BatchId)
                        : q.OrderBy(b => b.UpdatedAt).ThenBy(b => b.BatchId);
                    break;

                case "expiry":
                default:
                    ordered = descending
                        ? q.OrderByDescending(b => b.Expiry).ThenByDescending(b => b.BatchId)
                        : q.OrderBy(b => b.Expiry).ThenBy(b => b.BatchId);
                    break;
            }

            return ordered;
        }








        // =========================================================
        // GET: /Batches
        // شاشة قائمة التشغيلات (النظام الموحد) - Paging يدوي (حل ثابت)
        // =========================================================
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy,
            string? searchMode,
            string? sort,
            string? dir,
            int page = 1,
            int pageSize = 10,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_prod = null,
            string? filterCol_batchno = null,
            string? filterCol_expiry = null,
            string? filterCol_price = null,
            string? filterCol_cost = null,
            string? filterCol_active = null,
            string? filterCol_created = null,
            string? filterCol_updated = null,
            string? filterCol_idExpr = null,
            string? filterCol_prodExpr = null,
            string? filterCol_priceExpr = null,
            string? filterCol_costExpr = null)
        {
            // =========================
            // (1) قيم افتراضية + حماية Paging
            // =========================
            searchBy ??= "all";          // متغير: نوع البحث الافتراضي
            sort ??= "expiry";           // متغير: عمود الترتيب الافتراضي
            dir ??= "asc";               // متغير: اتجاه الترتيب الافتراضي

            var smNorm = string.IsNullOrWhiteSpace(searchMode) ? "contains" : searchMode.Trim().ToLowerInvariant();
            if (smNorm != "starts" && smNorm != "ends") smNorm = "contains";

            var pageSizeQuery = Request.Query["pageSize"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageSizeQuery) && int.TryParse(pageSizeQuery, out var psVal))
                pageSize = psVal;
            if (pageSize < 0) pageSize = 10;
            if (pageSize > 0 && pageSize != 10 && pageSize != 25 && pageSize != 50 && pageSize != 100 && pageSize != 200)
                pageSize = 10;

            if (page < 1) page = 1;

            // =========================
            // (2) استعلام واحد فقط: فلترة + بحث + ترتيب
            // =========================
            var query = SearchSortFilter(
                search,
                searchBy,
                smNorm,
                sort,
                dir,
                useDateRange,
                fromDate,
                toDate,
                "CreatedAt",   // ثابت عندك
                fromCode,
                toCode,
                filterCol_id,
                filterCol_prod,
                filterCol_batchno,
                filterCol_expiry,
                filterCol_price,
                filterCol_cost,
                filterCol_active,
                filterCol_created,
                filterCol_updated,
                filterCol_idExpr,
                filterCol_prodExpr,
                filterCol_priceExpr,
                filterCol_costExpr);

            // =========================
            // (3) إجمالي العدد بعد الفلاتر
            // =========================
            int totalCount = await query.CountAsync();
            var batchSummary = await query
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    ActiveCount = g.Count(x => x.IsActive),
                    TotalPriceRetail = g.Sum(x => x.PriceRetailBatch ?? 0m),
                    TotalUnitCost = g.Sum(x => x.UnitCostDefault ?? 0m)
                })
                .FirstOrDefaultAsync();

            int effectivePageSize = pageSize;
            if (pageSize == 0)
            {
                effectivePageSize = totalCount == 0 ? 10 : Math.Min(totalCount, 100_000);
                page = 1;
            }

            int totalPages = pageSize == 0
                ? 1
                : (int)Math.Ceiling(totalCount / (double)effectivePageSize);
            if (totalPages < 1) totalPages = 1;
            if (page > totalPages) page = totalPages;

            // =========================
            // (4) قراءة صفحة واحدة فقط (Skip/Take)
            // =========================
            var items = await query
                .Skip(pageSize == 0 ? 0 : (page - 1) * effectivePageSize)
                .Take(effectivePageSize)
                .ToListAsync();

            // =========================
            // (5) تجهيز PagedResult يدويًا (نفس نمط فواتير المشتريات)
            // =========================
            bool sortDesc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            var model = new PagedResult<Batch>
            {
                Items = items,
                PageNumber = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                HasPrevious = page > 1,
                HasNext = pageSize == 0 ? false : page < totalPages,
                Search = search,
                SortColumn = sort,
                SortDescending = sortDesc,
                UseDateRange = useDateRange,
                FromDate = fromDate,
                ToDate = toDate
            };

            // =========================
            // (6) ViewBag لحفظ الحالة في الواجهة
            // =========================
            ViewBag.Search = search;
            ViewBag.SearchBy = searchBy;
            ViewBag.SearchMode = smNorm;
            ViewBag.Sort = sort;
            ViewBag.Dir = sortDesc ? "desc" : "asc";

            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_Prod = filterCol_prod;
            ViewBag.FilterCol_BatchNo = filterCol_batchno;
            ViewBag.FilterCol_Expiry = filterCol_expiry;
            ViewBag.FilterCol_Price = filterCol_price;
            ViewBag.FilterCol_Cost = filterCol_cost;
            ViewBag.FilterCol_Active = filterCol_active;
            ViewBag.FilterCol_Created = filterCol_created;
            ViewBag.FilterCol_Updated = filterCol_updated;
            ViewBag.FilterCol_IdExpr = filterCol_idExpr;
            ViewBag.FilterCol_ProdExpr = filterCol_prodExpr;
            ViewBag.FilterCol_PriceExpr = filterCol_priceExpr;
            ViewBag.FilterCol_CostExpr = filterCol_costExpr;
            ViewBag.PageSize = pageSize;
            ViewBag.ActiveCountFiltered = batchSummary?.ActiveCount ?? 0;
            ViewBag.TotalPriceRetailFiltered = batchSummary?.TotalPriceRetail ?? 0m;
            ViewBag.TotalUnitCostFiltered = batchSummary?.TotalUnitCost ?? 0m;

            return View(model);
        }







        // =========================================================
        // GET: /Batches/Create
        // فتح شاشة إضافة تشغيلة جديدة
        // =========================================================
        public async Task<IActionResult> Create()
        {
            await FillProductsDropDownAsync();
            return View(new Batch());    // متغير: موديل فارغ
        }








        // =========================================================
        // POST: /Batches/Create
        // حفظ تشغيلة جديدة
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Batch model)
        {
            if (!ModelState.IsValid)
            {
                // لو في أخطاء فاليديشن نرجع نفس الشاشة مع نفس البيانات
                await FillProductsDropDownAsync(model.ProdId);
                return View(model);
            }

            // لو EntryDate لسه Default نحط تاريخ اليوم
            if (model.EntryDate == default)
            {
                model.EntryDate = DateTime.UtcNow;
            }

            model.CreatedAt = DateTime.UtcNow;

            _db.Batches.Add(model);
            await _db.SaveChangesAsync();

            await _activityLogger.LogAsync(UserActionType.Create, "Batch", model.BatchId, $"إنشاء تشغيلة: {model.BatchNo}");

            TempData["Success"] = "تم إضافة التشغيلة بنجاح.";
            return RedirectToAction(nameof(Index));
        }







        // =========================================================
        // GET: /Batches/Edit/5
        // فتح شاشة تعديل تشغيلة
        // =========================================================
        public async Task<IActionResult> Edit(int id)
        {
            var batch = await _db.Batches.FindAsync(id);
            if (batch == null)
            {
                return NotFound();
            }

            await FillProductsDropDownAsync(batch.ProdId);
            return View(batch);
        }







        // =========================================================
        // POST: /Batches/Edit/5
        // حفظ تعديل التشغيلة
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Batch model)
        {
            if (id != model.BatchId)
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                await FillProductsDropDownAsync(model.ProdId);
                return View(model);
            }

            try
            {
                var existing = await _db.Batches.AsNoTracking().FirstOrDefaultAsync(b => b.BatchId == id);
                var oldValues = existing != null ? System.Text.Json.JsonSerializer.Serialize(new { existing.BatchNo, existing.ProdId, existing.Expiry, existing.PriceRetailBatch }) : null;
                model.UpdatedAt = DateTime.UtcNow;   // تحديث آخر تعديل
                _db.Batches.Update(model);
                await _db.SaveChangesAsync();

                var newValues = System.Text.Json.JsonSerializer.Serialize(new { model.BatchNo, model.ProdId, model.Expiry, model.PriceRetailBatch });
                await _activityLogger.LogAsync(UserActionType.Edit, "Batch", id, $"تعديل تشغيلة: {model.BatchNo}", oldValues, newValues);

                TempData["Success"] = "تم حفظ تعديلات التشغيلة بنجاح.";
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _db.Batches.AnyAsync(b => b.BatchId == id))
                {
                    return NotFound();
                }
                throw;
            }
        }








        // =========================================================
        // GET: /Batches/Show/5
        // عرض تفاصيل تشغيلة واحدة
        // =========================================================
        public async Task<IActionResult> Show(int id)
        {
            var batch = await _db.Batches
                .Include(b => b.Product)
                .Include(b => b.Customer)
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.BatchId == id);

            if (batch == null)
            {
                return NotFound();
            }

            return View(batch);
        }







        // =========================================================
        // GET: /Batches/Delete/5
        // تأكيد حذف تشغيلة
        // =========================================================
        public async Task<IActionResult> Delete(int id)
        {
            var batch = await _db.Batches
                .Include(b => b.Product)
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.BatchId == id);

            if (batch == null)
            {
                return NotFound();
            }

            return View(batch);
        }






        // =========================================================
        // POST: /Batches/Delete/5
        // تنفيذ الحذف
        // =========================================================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var batch = await _db.Batches.FindAsync(id);
            if (batch != null)
            {
                var oldValues = System.Text.Json.JsonSerializer.Serialize(new { batch.BatchNo, batch.ProdId, batch.Expiry });
                _db.Batches.Remove(batch);
                await _db.SaveChangesAsync();

                await _activityLogger.LogAsync(UserActionType.Delete, "Batch", id, $"حذف تشغيلة: {batch.BatchNo}", oldValues: oldValues);

                TempData["Success"] = "تم حذف التشغيلة.";
            }

            return RedirectToAction(nameof(Index));
        }







        // =========================================================
        // POST: /Batches/BulkDelete
        // حذف مجموعة تشغيلات من جدول الاندكس
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["Error"] = "لم يتم اختيار أي تشغيلة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var ids = selectedIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => int.TryParse(id, out var v) ? v : (int?)null)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();

            if (ids.Count == 0)
            {
                TempData["Error"] = "لم يتم اختيار تشغيلات صالحة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var batches = await _db.Batches
                .Where(b => ids.Contains(b.BatchId))
                .ToListAsync();

            _db.Batches.RemoveRange(batches);
            await _db.SaveChangesAsync();

            TempData["Success"] = "تم حذف التشغيلات المحددة.";
            return RedirectToAction(nameof(Index));
        }








        // =========================================================
        // POST: /Batches/DeleteAll
        // حذف جميع تشغيلات الجدول
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _db.Batches.ToListAsync();
            _db.Batches.RemoveRange(all);
            await _db.SaveChangesAsync();

            TempData["Success"] = "تم حذف جميع التشغيلات.";
            return RedirectToAction(nameof(Index));
        }








        // =========================================================
        // GET: /Batches/Export
        // تصدير (Excel / CSV) بنفس فلاتر الاندكس
        // =========================================================
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy,
            string? searchMode,
            string? sort,
            string? dir,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_prod = null,
            string? filterCol_batchno = null,
            string? filterCol_expiry = null,
            string? filterCol_price = null,
            string? filterCol_cost = null,
            string? filterCol_active = null,
            string? filterCol_created = null,
            string? filterCol_updated = null,
            string? filterCol_idExpr = null,
            string? filterCol_prodExpr = null,
            string? filterCol_priceExpr = null,
            string? filterCol_costExpr = null,
            string format = "excel")
        {
            var smNorm = string.IsNullOrWhiteSpace(searchMode) ? "contains" : searchMode.Trim().ToLowerInvariant();
            if (smNorm != "starts" && smNorm != "ends") smNorm = "contains";

            var query = SearchSortFilter(
                search,
                searchBy,
                smNorm,
                sort,
                dir,
                useDateRange,
                fromDate,
                toDate,
                "CreatedAt",
                fromCode,
                toCode,
                filterCol_id,
                filterCol_prod,
                filterCol_batchno,
                filterCol_expiry,
                filterCol_price,
                filterCol_cost,
                filterCol_active,
                filterCol_created,
                filterCol_updated,
                filterCol_idExpr,
                filterCol_prodExpr,
                filterCol_priceExpr,
                filterCol_costExpr);

            var data = await query.ToListAsync();

            if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
            {
                // تصدير CSV بسيط
                var sb = new StringBuilder();
                sb.AppendLine("كود التشغيلة,كود الصنف,التشغيلة,تاريخ الصلاحية,سعر الجمهور للتشغيلة,التكلفة الافتراضية,تاريخ الإدخال,تاريخ الإنشاء,آخر تعديل,نشط؟");

                foreach (var b in data)
                {
                    sb.AppendLine(string.Join(",",
                        b.BatchId,
                        b.ProdId,
                        EscapeCsv(b.BatchNo),
                        b.Expiry.ToString("yyyy-MM-dd"),
                        b.PriceRetailBatch?.ToString("0.##") ?? "",
                        b.UnitCostDefault?.ToString("0.####") ?? "",
                        b.EntryDate.ToString("yyyy-MM-dd"),
                        b.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                        b.UpdatedAt?.ToString("yyyy-MM-dd HH:mm") ?? "",
                        (b.IsActive ? "نشط" : "موقوف").Replace(",", " ")
                    ));
                }

                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString());
                var csvName = ExcelExportNaming.ArabicTimestampedFileName("تشغيلات الأصناف", ".csv");
                return File(bytes, "text/csv", csvName);
            }
            else
            {
                // تصدير Excel باستخدام ClosedXML
                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("تشغيلات الأصناف"));

                // عناوين الأعمدة
                ws.Cell(1, 1).Value = "كود التشغيلة";
                ws.Cell(1, 2).Value = "كود الصنف";
                ws.Cell(1, 3).Value = "اسم الصنف";
                ws.Cell(1, 4).Value = "رقم التشغيلة";
                ws.Cell(1, 5).Value = "تاريخ الصلاحية";
                ws.Cell(1, 6).Value = "سعر الجمهور للتشغيلة";
                ws.Cell(1, 7).Value = "التكلفة الافتراضية";
                ws.Cell(1, 8).Value = "تاريخ الإدخال";
                ws.Cell(1, 9).Value = "تاريخ الإنشاء";
                ws.Cell(1, 10).Value = "آخر تعديل";
                ws.Cell(1, 11).Value = "نشط؟";

                int row = 2;

                foreach (var b in data)
                {
                    ws.Cell(row, 1).Value = b.BatchId;
                    ws.Cell(row, 2).Value = b.ProdId;
                    ws.Cell(row, 3).Value = b.Product?.ProdName ?? "";
                    ws.Cell(row, 4).Value = b.BatchNo;
                    ws.Cell(row, 5).Value = b.Expiry;
                    ws.Cell(row, 6).Value = b.PriceRetailBatch;
                    ws.Cell(row, 7).Value = b.UnitCostDefault;
                    ws.Cell(row, 8).Value = b.EntryDate;
                    ws.Cell(row, 9).Value = b.CreatedAt;
                    ws.Cell(row, 10).Value = b.UpdatedAt;
                    ws.Cell(row, 11).Value = b.IsActive ? "نشط" : "موقوف";

                    row++;
                }

                ws.Columns().AdjustToContents();

                using var stream = new System.IO.MemoryStream();
                wb.SaveAs(stream);
                var fileName = ExcelExportNaming.ArabicTimestampedFileName("تشغيلات الأصناف", ".xlsx");
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
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }
    }
}
