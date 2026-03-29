using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ClosedXML.Excel;                 // تصدير Excel
using ERP.Data;                        // AppDbContext
using ERP.Filters;
using ERP.Infrastructure;              // PagedResult
using ERP.Models;                      // StockBatch / Product / Warehouse
using ERP.Security;
using ERP.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Controllers
{
    /// <summary>
    /// كونترولر شاشة Stock_Batches (رصيد سريع لكل تشغيلة داخل مخزن)
    /// - Index فقط (نظام القوائم الموحد)
    /// - BulkDelete / DeleteAll
    /// - Export (Excel / CSV)
    /// </summary>
    [RequirePermission("StockBatches.Index")]
    public class StockBatchesController : Controller
    {
        private readonly AppDbContext _db; // متغير: DbContext
        private readonly StockAnalysisService _stockAnalysis;

        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        /// <summary>
        /// فلتر رقمي للأعمدة الصحيحة (نفس صيغة قائمة الأصناف: &lt; &gt; &lt;= &gt;= نطاق أو رقم مطابق).
        /// </summary>
        private static IQueryable<StockBatch> ApplyInt32ExprFilter(
            IQueryable<StockBatch> q,
            string? expr,
            string field)
        {
            if (string.IsNullOrWhiteSpace(expr)) return q;
            expr = expr.Trim();
            if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.Substring(2), out var maxLe))
            {
                return field switch
                {
                    "id" => q.Where(x => x.Id <= maxLe),
                    "wh" => q.Where(x => x.WarehouseId <= maxLe),
                    "prod" => q.Where(x => x.ProdId <= maxLe),
                    "qty" => q.Where(x => x.QtyOnHand <= maxLe),
                    "reserved" => q.Where(x => x.QtyReserved <= maxLe),
                    _ => q
                };
            }
            if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.Substring(2), out var minGe))
            {
                return field switch
                {
                    "id" => q.Where(x => x.Id >= minGe),
                    "wh" => q.Where(x => x.WarehouseId >= minGe),
                    "prod" => q.Where(x => x.ProdId >= minGe),
                    "qty" => q.Where(x => x.QtyOnHand >= minGe),
                    "reserved" => q.Where(x => x.QtyReserved >= minGe),
                    _ => q
                };
            }
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.Substring(1), out var maxLt))
            {
                return field switch
                {
                    "id" => q.Where(x => x.Id < maxLt),
                    "wh" => q.Where(x => x.WarehouseId < maxLt),
                    "prod" => q.Where(x => x.ProdId < maxLt),
                    "qty" => q.Where(x => x.QtyOnHand < maxLt),
                    "reserved" => q.Where(x => x.QtyReserved < maxLt),
                    _ => q
                };
            }
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.Substring(1), out var minGt))
            {
                return field switch
                {
                    "id" => q.Where(x => x.Id > minGt),
                    "wh" => q.Where(x => x.WarehouseId > minGt),
                    "prod" => q.Where(x => x.ProdId > minGt),
                    "qty" => q.Where(x => x.QtyOnHand > minGt),
                    "reserved" => q.Where(x => x.QtyReserved > minGt),
                    _ => q
                };
            }
            if ((expr.Contains(':') || expr.Contains('-')) && !expr.StartsWith('-'))
            {
                var separator = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 &&
                    int.TryParse(parts[0].Trim(), out var from) &&
                    int.TryParse(parts[1].Trim(), out var to))
                {
                    if (from > to) (from, to) = (to, from);
                    return field switch
                    {
                        "id" => q.Where(x => x.Id >= from && x.Id <= to),
                        "wh" => q.Where(x => x.WarehouseId >= from && x.WarehouseId <= to),
                        "prod" => q.Where(x => x.ProdId >= from && x.ProdId <= to),
                        "qty" => q.Where(x => x.QtyOnHand >= from && x.QtyOnHand <= to),
                        "reserved" => q.Where(x => x.QtyReserved >= from && x.QtyReserved <= to),
                        _ => q
                    };
                }
            }
            if (int.TryParse(expr, out var exact))
            {
                return field switch
                {
                    "id" => q.Where(x => x.Id == exact),
                    "wh" => q.Where(x => x.WarehouseId == exact),
                    "prod" => q.Where(x => x.ProdId == exact),
                    "qty" => q.Where(x => x.QtyOnHand == exact),
                    "reserved" => q.Where(x => x.QtyReserved == exact),
                    _ => q
                };
            }
            return q;
        }

        public StockBatchesController(AppDbContext context, StockAnalysisService stockAnalysis)
        {
            _db = context;
            _stockAnalysis = stockAnalysis;
        }

        /// <summary>
        /// API: جلب القيم المميزة لعمود معين لاستخدامها في فلترة الأعمدة بنمط Excel.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var col = (column ?? "").Trim().ToLowerInvariant();

            IQueryable<StockBatch> q = _db.Set<StockBatch>().AsNoTracking();

            List<(string Value, string Display)> items;
            if (col == "wh")
            {
                var whIds = await q.Select(x => x.WarehouseId).Distinct().ToListAsync();
                var rows = await _db.Warehouses
                    .AsNoTracking()
                    .Where(w => whIds.Contains(w.WarehouseId))
                    .OrderBy(w => w.WarehouseId)
                    .Take(500)
                    .Select(w => new { w.WarehouseId, w.WarehouseName })
                    .ToListAsync();
                items = rows
                    .Select(x => (x.WarehouseId.ToString(), (x.WarehouseName ?? "").Trim() + " (" + x.WarehouseId + ")"))
                    .ToList();
            }
            else if (col == "prod")
            {
                var prodIds = await q.Select(x => x.ProdId).Distinct().ToListAsync();
                var rows = await _db.Products
                    .AsNoTracking()
                    .Where(p => prodIds.Contains(p.ProdId))
                    .OrderBy(p => p.ProdId)
                    .Take(500)
                    .Select(p => new { p.ProdId, p.ProdName })
                    .ToListAsync();
                items = rows
                    .Select(x => (x.ProdId.ToString(), (x.ProdName ?? "").Trim() + " (" + x.ProdId + ")"))
                    .ToList();
            }
            else
            {
                items = col switch
                {
                    "id" => (await q.Select(x => x.Id).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                        .Select(v => (v.ToString(), v.ToString())).ToList(),
                    "batchno" => (await q.Where(x => x.BatchNo != null).Select(x => x.BatchNo!).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                        .Select(v => (v, v)).ToList(),
                    "expiry" => (await q.Where(x => x.Expiry.HasValue)
                            .Select(x => new { x.Expiry!.Value.Year, x.Expiry.Value.Month })
                            .Distinct()
                            .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month)
                            .Take(200)
                            .ToListAsync())
                        .Select(x => ($"{x.Year}-{x.Month:D2}", $"{x.Year}/{x.Month:D2}")).ToList(),
                    "qty" => (await q.Select(x => x.QtyOnHand).Distinct().OrderBy(v => v).Take(200).ToListAsync())
                        .Select(v => (v.ToString(), v.ToString())).ToList(),
                    "reserved" => (await q.Select(x => x.QtyReserved).Distinct().OrderBy(v => v).Take(200).ToListAsync())
                        .Select(v => (v.ToString(), v.ToString())).ToList(),
                    "updated" => (await q.Select(x => new { x.UpdatedAt.Year, x.UpdatedAt.Month }).Distinct()
                            .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month)
                            .Take(200)
                            .ToListAsync())
                        .Select(x => ($"{x.Year}-{x.Month:D2}", $"{x.Year}/{x.Month:D2}")).ToList(),
                    "note" => (await q.Where(x => x.Note != null).Select(x => x.Note!).Distinct().OrderBy(v => v).Take(300).ToListAsync())
                        .Select(v => (v, v)).ToList(),
                    _ => new List<(string Value, string Display)>()
                };
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
        // دالة مساعدة: تطبيق البحث / الفلترة / الترتيب
        // =========================================================
        private IQueryable<StockBatch> SearchSortFilter(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            int? fromCode,
            int? toCode,
            string? filterCol_id = null,
            string? filterCol_wh = null,
            string? filterCol_prod = null,
            string? filterCol_batchno = null,
            string? filterCol_expiry = null,
            string? filterCol_qty = null,
            string? filterCol_reserved = null,
            string? filterCol_updated = null,
            string? filterCol_note = null,
            string? filterCol_idExpr = null,
            string? filterCol_whExpr = null,
            string? filterCol_prodExpr = null,
            string? filterCol_qtyExpr = null,
            string? filterCol_reservedExpr = null,
            string? searchMode = null)
        {
            // الاستعلام الأساسي (AsNoTracking للسرعة)
            var q = _db.Set<StockBatch>()
                .AsNoTracking()
                .AsQueryable();

            // ------------------------------
            // فلاتر أعمدة بنمط Excel (id, wh, prod, batchno, expiry, qty, reserved, updated, note)
            // للأعمدة الرقمية: أولوية filterCol_*Expr على قائمة التشيك بوكس
            // ------------------------------
            if (!string.IsNullOrWhiteSpace(filterCol_idExpr))
                q = ApplyInt32ExprFilter(q, filterCol_idExpr, "id");
            else if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue)
                    .Select(v => v!.Value)
                    .ToList();
                if (ids.Count > 0)
                    q = q.Where(x => ids.Contains(x.Id));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_whExpr))
                q = ApplyInt32ExprFilter(q, filterCol_whExpr, "wh");
            else if (!string.IsNullOrWhiteSpace(filterCol_wh))
            {
                var ids = filterCol_wh.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue)
                    .Select(v => v!.Value)
                    .ToList();
                if (ids.Count > 0)
                    q = q.Where(x => ids.Contains(x.WarehouseId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_prodExpr))
                q = ApplyInt32ExprFilter(q, filterCol_prodExpr, "prod");
            else if (!string.IsNullOrWhiteSpace(filterCol_prod))
            {
                var ids = filterCol_prod.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue)
                    .Select(v => v!.Value)
                    .ToList();
                if (ids.Count > 0)
                    q = q.Where(x => ids.Contains(x.ProdId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_batchno))
            {
                var vals = filterCol_batchno.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(x => x.BatchNo != null && vals.Contains(x.BatchNo));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_expiry))
            {
                // القيم تأتي في صورة yyyy-MM
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
                    q = q.Where(x => x.Expiry.HasValue &&
                        dateFilters.Any(df => x.Expiry.Value.Year == df.Year && x.Expiry.Value.Month == df.Month));
                }
            }

            if (!string.IsNullOrWhiteSpace(filterCol_qtyExpr))
                q = ApplyInt32ExprFilter(q, filterCol_qtyExpr, "qty");
            else if (!string.IsNullOrWhiteSpace(filterCol_qty))
            {
                var ids = filterCol_qty.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue)
                    .Select(v => v!.Value)
                    .ToList();
                if (ids.Count > 0)
                    q = q.Where(x => ids.Contains(x.QtyOnHand));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_reservedExpr))
                q = ApplyInt32ExprFilter(q, filterCol_reservedExpr, "reserved");
            else if (!string.IsNullOrWhiteSpace(filterCol_reserved))
            {
                var ids = filterCol_reserved.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue)
                    .Select(v => v!.Value)
                    .ToList();
                if (ids.Count > 0)
                    q = q.Where(x => ids.Contains(x.QtyReserved));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_updated))
            {
                // القيم تأتي في صورة yyyy-MM
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
                    q = q.Where(x =>
                        dateFilters.Any(df => x.UpdatedAt.Year == df.Year && x.UpdatedAt.Month == df.Month));
                }
            }

            if (!string.IsNullOrWhiteSpace(filterCol_note))
            {
                var vals = filterCol_note.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(x => x.Note != null && vals.Any(v => x.Note.Contains(v)));
            }

            // ------------------------------
            // فلتر التاريخ/الوقت (UpdatedAt فقط لأن الجدول Cache)
            // ------------------------------
            bool dateFilterActive = useDateRange || fromDate.HasValue || toDate.HasValue;
            if (dateFilterActive)
            {
                if (fromDate.HasValue)
                    q = q.Where(x => x.UpdatedAt >= fromDate.Value);

                if (toDate.HasValue)
                    q = q.Where(x => x.UpdatedAt <= toDate.Value);
            }

            // ------------------------------
            // فلتر كود من/إلى (Id)
            // ------------------------------
            if (fromCode.HasValue)
                q = q.Where(x => x.Id >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(x => x.Id <= toCode.Value);

            // ------------------------------
            // البحث
            // searchMode: starts | contains | ends — يُطبَّق على حقول نصية (تشغيلة، ملاحظة، الكل)
            // searchBy: all | id | warehouse | prod | batchno | expiry | qty | note
            // ------------------------------
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                var sb = (searchBy ?? "all").ToLowerInvariant();
                var sm = (searchMode ?? "contains").ToLowerInvariant();
                if (sm != "starts" && sm != "ends") sm = "contains";

                switch (sb)
                {
                    case "id":
                        q = q.Where(x => x.Id.ToString() == term);
                        break;

                    case "warehouse":
                        q = q.Where(x => x.WarehouseId.ToString() == term);
                        break;

                    case "prod":
                        q = q.Where(x => x.ProdId.ToString() == term);
                        break;

                    case "batchno":
                        if (sm == "starts")
                            q = q.Where(x => x.BatchNo != null && x.BatchNo.StartsWith(term));
                        else if (sm == "ends")
                            q = q.Where(x => x.BatchNo != null && x.BatchNo.EndsWith(term));
                        else
                            q = q.Where(x => x.BatchNo != null && x.BatchNo.Contains(term));
                        break;

                    case "note":
                        if (sm == "starts")
                            q = q.Where(x => x.Note != null && x.Note.StartsWith(term));
                        else if (sm == "ends")
                            q = q.Where(x => x.Note != null && x.Note.EndsWith(term));
                        else
                            q = q.Where(x => x.Note != null && x.Note.Contains(term));
                        break;

                    case "qty":
                        if (int.TryParse(term, out var qtyTerm))
                            q = q.Where(x => x.QtyOnHand == qtyTerm || x.QtyReserved == qtyTerm);
                        break;

                    case "expiry":
                        if (DateTime.TryParse(term, out var dtExp))
                        {
                            var d = dtExp.Date;
                            q = q.Where(x => x.Expiry.HasValue && x.Expiry.Value.Date == d);
                        }
                        break;

                    case "all":
                    default:
                        q = q.Where(x =>
                            (x.BatchNo != null && (
                                sm == "starts" ? x.BatchNo.StartsWith(term) :
                                sm == "ends" ? x.BatchNo.EndsWith(term) :
                                x.BatchNo.Contains(term))) ||
                            (x.Note != null && (
                                sm == "starts" ? x.Note.StartsWith(term) :
                                sm == "ends" ? x.Note.EndsWith(term) :
                                x.Note.Contains(term))) ||
                            x.Id.ToString() == term ||
                            x.WarehouseId.ToString() == term ||
                            x.ProdId.ToString() == term ||
                            x.QtyOnHand.ToString() == term ||
                            x.QtyReserved.ToString() == term
                        );
                        break;
                }
            }

            // ------------------------------
            // الترتيب (مع Tie-breaker ثابت)
            // sort المقترح: id | warehouse | prod | batchno | expiry | onhand | reserved | updated
            // ------------------------------
            bool descending = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            string sortCol = (sort ?? "id").ToLower();

            IOrderedQueryable<StockBatch> ordered;

            switch (sortCol)
            {
                case "warehouse":
                    ordered = descending
                        ? q.OrderByDescending(x => x.WarehouseId).ThenByDescending(x => x.Id)
                        : q.OrderBy(x => x.WarehouseId).ThenBy(x => x.Id);
                    break;

                case "prod":
                    ordered = descending
                        ? q.OrderByDescending(x => x.ProdId).ThenByDescending(x => x.Id)
                        : q.OrderBy(x => x.ProdId).ThenBy(x => x.Id);
                    break;

                case "batchno":
                    ordered = descending
                        ? q.OrderByDescending(x => x.BatchNo).ThenByDescending(x => x.Id)
                        : q.OrderBy(x => x.BatchNo).ThenBy(x => x.Id);
                    break;

                case "expiry":
                    ordered = descending
                        ? q.OrderByDescending(x => x.Expiry).ThenByDescending(x => x.Id)
                        : q.OrderBy(x => x.Expiry).ThenBy(x => x.Id);
                    break;

                case "onhand":
                    ordered = descending
                        ? q.OrderByDescending(x => x.QtyOnHand).ThenByDescending(x => x.Id)
                        : q.OrderBy(x => x.QtyOnHand).ThenBy(x => x.Id);
                    break;

                case "reserved":
                    ordered = descending
                        ? q.OrderByDescending(x => x.QtyReserved).ThenByDescending(x => x.Id)
                        : q.OrderBy(x => x.QtyReserved).ThenBy(x => x.Id);
                    break;

                case "updated":
                    ordered = descending
                        ? q.OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id)
                        : q.OrderBy(x => x.UpdatedAt).ThenBy(x => x.Id);
                    break;

                case "note":
                    ordered = descending
                        ? q.OrderByDescending(x => x.Note).ThenByDescending(x => x.Id)
                        : q.OrderBy(x => x.Note).ThenBy(x => x.Id);
                    break;

                case "id":
                default:
                    ordered = descending
                        ? q.OrderByDescending(x => x.Id)
                        : q.OrderBy(x => x.Id);
                    break;
            }

            return ordered;
        }

        // =========================================================
        // GET: /StockBatches
        // Index (نظام القوائم الموحد)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            int page = 1,
            int pageSize = 10,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string? searchMode = null,
            string? filterCol_id = null,
            string? filterCol_wh = null,
            string? filterCol_prod = null,
            string? filterCol_batchno = null,
            string? filterCol_expiry = null,
            string? filterCol_qty = null,
            string? filterCol_reserved = null,
            string? filterCol_updated = null,
            string? filterCol_note = null,
            string? filterCol_idExpr = null,
            string? filterCol_whExpr = null,
            string? filterCol_prodExpr = null,
            string? filterCol_qtyExpr = null,
            string? filterCol_reservedExpr = null)
        {
            searchBy ??= "batchno";
            sort ??= "id";
            dir ??= "asc";
            searchMode ??= "contains";

            var pageSizeQuery = Request.Query["pageSize"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageSizeQuery) && int.TryParse(pageSizeQuery, out var psVal))
                pageSize = psVal;
            if (pageSize < 0) pageSize = 10;
            if (pageSize > 0 && pageSize != 10 && pageSize != 25 && pageSize != 50 && pageSize != 100 && pageSize != 200)
                pageSize = 10;

            if (page < 1) page = 1;

            // (2) Query واحد: فلترة + بحث + ترتيب
            var query = SearchSortFilter(
                search, searchBy, sort, dir,
                useDateRange, fromDate, toDate,
                fromCode, toCode,
                filterCol_id,
                filterCol_wh,
                filterCol_prod,
                filterCol_batchno,
                filterCol_expiry,
                filterCol_qty,
                filterCol_reserved,
                filterCol_updated,
                filterCol_note,
                filterCol_idExpr,
                filterCol_whExpr,
                filterCol_prodExpr,
                filterCol_qtyExpr,
                filterCol_reservedExpr,
                searchMode);

            // (3) Count
            int totalCount = await query.CountAsync();

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

            var skip = pageSize == 0 ? 0 : (page - 1) * effectivePageSize;
            var take = pageSize == 0 ? effectivePageSize : effectivePageSize;

            // (4) Page data
            var items = await query
                .Skip(skip)
                .Take(take)
                .ToListAsync();

            // (5) تحميل أسماء الأصناف والمخازن للصفحة فقط (سرعة)
            var prodIds = items.Select(x => x.ProdId).Distinct().ToList();
            var whIds = items.Select(x => x.WarehouseId).Distinct().ToList();

            var prodNames = await _db.Products
                .AsNoTracking()
                .Where(p => prodIds.Contains(p.ProdId))
                .Select(p => new { p.ProdId, p.ProdName })
                .ToDictionaryAsync(x => x.ProdId, x => x.ProdName ?? "");

            var warehouseNames = await _db.Warehouses
                .AsNoTracking()
                .Where(w => whIds.Contains(w.WarehouseId))
                .Select(w => new { w.WarehouseId, w.WarehouseName })
                .ToDictionaryAsync(x => x.WarehouseId, x => x.WarehouseName ?? "");

            ViewBag.ProdNames = prodNames;
            ViewBag.WarehouseNames = warehouseNames;

            var wdMap = new Dictionary<string, decimal>();
            foreach (var pair in items.Select(x => (x.ProdId, x.WarehouseId)).Distinct())
            {
                var d = await _stockAnalysis.GetWeightedPurchaseDiscountForWarehouseAsync(pair.ProdId, pair.WarehouseId);
                wdMap[$"{pair.ProdId}|{pair.WarehouseId}"] = d;
            }
            ViewBag.WeightedDiscounts = wdMap;

            // (6) تجهيز PagedResult
            bool sortDesc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            var model = new PagedResult<StockBatch>
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

            ViewBag.Search = search;
            ViewBag.SearchBy = searchBy;
            ViewBag.SearchMode = searchMode;
            ViewBag.Sort = sort;
            ViewBag.Dir = sortDesc ? "desc" : "asc";
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_Wh = filterCol_wh;
            ViewBag.FilterCol_Prod = filterCol_prod;
            ViewBag.FilterCol_BatchNo = filterCol_batchno;
            ViewBag.FilterCol_Expiry = filterCol_expiry;
            ViewBag.FilterCol_Qty = filterCol_qty;
            ViewBag.FilterCol_Reserved = filterCol_reserved;
            ViewBag.FilterCol_Updated = filterCol_updated;
            ViewBag.FilterCol_Note = filterCol_note;
            ViewBag.FilterCol_IdExpr = filterCol_idExpr;
            ViewBag.FilterCol_WhExpr = filterCol_whExpr;
            ViewBag.FilterCol_ProdExpr = filterCol_prodExpr;
            ViewBag.FilterCol_QtyExpr = filterCol_qtyExpr;
            ViewBag.FilterCol_ReservedExpr = filterCol_reservedExpr;
            ViewBag.PageSize = pageSize;

            return View(model);
        }

        // =========================================================
        // POST: /StockBatches/BulkDelete
        // حذف محدد (⚠️ هذا جدول Cache - استخدمه بحذر)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["Error"] = "لم يتم اختيار أي صف للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var ids = selectedIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x, out var v) ? v : (int?)null)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();

            if (ids.Count == 0)
            {
                TempData["Error"] = "القيم المختارة غير صالحة.";
                return RedirectToAction(nameof(Index));
            }

            var rows = await _db.Set<StockBatch>()
                .Where(x => ids.Contains(x.Id))
                .ToListAsync();

            _db.RemoveRange(rows);
            await _db.SaveChangesAsync();

            TempData["Success"] = "تم حذف الصفوف المحددة.";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // POST: /StockBatches/DeleteAll
        // حذف الجميع (⚠️ جدول Cache)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _db.Set<StockBatch>().ToListAsync();
            _db.RemoveRange(all);
            await _db.SaveChangesAsync();

            TempData["Success"] = "تم حذف جميع الصفوف من Stock_Batches.";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // GET: /StockBatches/Export
        // تصدير (Excel / CSV) بنفس فلاتر الاندكس
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string? searchMode = null,
            string? filterCol_id = null,
            string? filterCol_wh = null,
            string? filterCol_prod = null,
            string? filterCol_batchno = null,
            string? filterCol_expiry = null,
            string? filterCol_qty = null,
            string? filterCol_reserved = null,
            string? filterCol_updated = null,
            string? filterCol_note = null,
            string? filterCol_idExpr = null,
            string? filterCol_whExpr = null,
            string? filterCol_prodExpr = null,
            string? filterCol_qtyExpr = null,
            string? filterCol_reservedExpr = null,
            string format = "excel")
        {
            var query = SearchSortFilter(
                search, searchBy, sort, dir,
                useDateRange, fromDate, toDate,
                fromCode, toCode,
                filterCol_id,
                filterCol_wh,
                filterCol_prod,
                filterCol_batchno,
                filterCol_expiry,
                filterCol_qty,
                filterCol_reserved,
                filterCol_updated,
                filterCol_note,
                filterCol_idExpr,
                filterCol_whExpr,
                filterCol_prodExpr,
                filterCol_qtyExpr,
                filterCol_reservedExpr,
                searchMode);

            var data = await query.ToListAsync();

            // أسماء الأصناف والمخازن للتصدير
            var prodIds = data.Select(x => x.ProdId).Distinct().ToList();
            var whIds = data.Select(x => x.WarehouseId).Distinct().ToList();

            var prodNames = await _db.Products
                .AsNoTracking()
                .Where(p => prodIds.Contains(p.ProdId))
                .Select(p => new { p.ProdId, p.ProdName })
                .ToDictionaryAsync(x => x.ProdId, x => x.ProdName ?? "");

            var warehouseNames = await _db.Warehouses
                .AsNoTracking()
                .Where(w => whIds.Contains(w.WarehouseId))
                .Select(w => new { w.WarehouseId, w.WarehouseName })
                .ToDictionaryAsync(x => x.WarehouseId, x => x.WarehouseName ?? "");

            var wdMap = new Dictionary<string, decimal>();
            foreach (var pair in data.Select(x => (x.ProdId, x.WarehouseId)).Distinct())
            {
                var d = await _stockAnalysis.GetWeightedPurchaseDiscountForWarehouseAsync(pair.ProdId, pair.WarehouseId);
                wdMap[$"{pair.ProdId}|{pair.WarehouseId}"] = d;
            }

            if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
            {
                var sb = new StringBuilder();
                sb.AppendLine("كود السجل,كود المخزن,اسم المخزن,كود الصنف,اسم الصنف,الخصم المرجح %,رقم التشغيلة,تاريخ الصلاحية,المتاح,محجوز,آخر تحديث,ملاحظة");

                foreach (var x in data)
                {
                    var prodName = prodNames.TryGetValue(x.ProdId, out var pn) ? pn : "";
                    var whName = warehouseNames.TryGetValue(x.WarehouseId, out var wn) ? wn : "";
                    wdMap.TryGetValue($"{x.ProdId}|{x.WarehouseId}", out var wdisc);

                    sb.AppendLine(string.Join(",",
                        x.Id,
                        x.WarehouseId,
                        EscapeCsv(whName),
                        x.ProdId,
                        EscapeCsv(prodName),
                        wdisc.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        EscapeCsv(x.BatchNo),
                        x.Expiry?.ToString("yyyy-MM-dd") ?? "",
                        x.QtyOnHand,
                        x.QtyReserved,
                        x.UpdatedAt.ToString("yyyy-MM-dd HH:mm"),
                        EscapeCsv(x.Note ?? "")
                    ));
                }

                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString());
                var csvName = ExcelExportNaming.ArabicTimestampedFileName("أرصدة التشغيلات بالمخازن", ".csv");
                return File(bytes, "text/csv", csvName);
            }
            else
            {
                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("أرصدة التشغيلات"));

                ws.Cell(1, 1).Value = "كود السجل";
                ws.Cell(1, 2).Value = "كود المخزن";
                ws.Cell(1, 3).Value = "اسم المخزن";
                ws.Cell(1, 4).Value = "كود الصنف";
                ws.Cell(1, 5).Value = "اسم الصنف";
                ws.Cell(1, 6).Value = "الخصم المرجح %";
                ws.Cell(1, 7).Value = "رقم التشغيلة";
                ws.Cell(1, 8).Value = "تاريخ الصلاحية";
                ws.Cell(1, 9).Value = "المتاح";
                ws.Cell(1, 10).Value = "محجوز";
                ws.Cell(1, 11).Value = "آخر تحديث";
                ws.Cell(1, 12).Value = "ملاحظة";

                int row = 2;
                foreach (var x in data)
                {
                    var prodName = prodNames.TryGetValue(x.ProdId, out var pn) ? pn : "";
                    var whName = warehouseNames.TryGetValue(x.WarehouseId, out var wn) ? wn : "";
                    wdMap.TryGetValue($"{x.ProdId}|{x.WarehouseId}", out var wdisc);

                    ws.Cell(row, 1).Value = x.Id;
                    ws.Cell(row, 2).Value = x.WarehouseId;
                    ws.Cell(row, 3).Value = whName;
                    ws.Cell(row, 4).Value = x.ProdId;
                    ws.Cell(row, 5).Value = prodName;
                    ws.Cell(row, 6).Value = wdisc;
                    ws.Cell(row, 7).Value = x.BatchNo;
                    ws.Cell(row, 8).Value = x.Expiry;
                    ws.Cell(row, 9).Value = x.QtyOnHand;
                    ws.Cell(row, 10).Value = x.QtyReserved;
                    ws.Cell(row, 11).Value = x.UpdatedAt;
                    ws.Cell(row, 12).Value = x.Note ?? "";

                    row++;
                }

                ws.Columns().AdjustToContents();

                using var stream = new System.IO.MemoryStream();
                wb.SaveAs(stream);

                var fileName = ExcelExportNaming.ArabicTimestampedFileName("أرصدة التشغيلات بالمخازن", ".xlsx");
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
                return "\"" + value.Replace("\"", "\"\"") + "\"";

            return value;
        }
    }
}
