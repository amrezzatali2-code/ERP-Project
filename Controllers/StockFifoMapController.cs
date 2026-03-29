using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ERP.Data;
using ERP.Filters;
using ERP.Infrastructure;
using ERP.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Controllers
{
    /// <summary>تقرير ربط FIFO بين الخروج والدخول — نظام القوائم الموحد.</summary>
    [RequirePermission("StockFifoMap.Index")]
    public class StockFifoMapController : Controller
    {
        private readonly AppDbContext _db;
        private static readonly char[] _filterSep = new[] { '|', ',', ';' };
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public StockFifoMapController(AppDbContext ctx) => _db = ctx;

        /// <summary>صف مُشتق للاستعلام: ربط FIFO + قيود الخروج/الدخول + اسم الصنف.</summary>
        internal sealed class FifoListRow
        {
            public StockFifoMap M { get; set; } = null!;
            public StockLedger OutL { get; set; } = null!;
            public StockLedger InL { get; set; } = null!;
            public int ProdId { get; set; }
            public string? ProdName { get; set; }
        }

        private IQueryable<FifoListRow> BaseFifoJoin()
        {
            return from m in _db.StockFifoMap.AsNoTracking()
                   join o in _db.StockLedger.AsNoTracking() on m.OutEntryId equals o.EntryId
                   join i in _db.StockLedger.AsNoTracking() on m.InEntryId equals i.EntryId
                   join p in _db.Products.AsNoTracking() on o.ProdId equals p.ProdId into pg
                   from p in pg.DefaultIfEmpty()
                   select new FifoListRow
                   {
                       M = m,
                       OutL = o,
                       InL = i,
                       ProdId = o.ProdId,
                       ProdName = p != null ? p.ProdName : null
                   };
        }

        private static IQueryable<FifoListRow> ApplyInt32Expr(IQueryable<FifoListRow> q, string? expr, string field)
        {
            if (string.IsNullOrWhiteSpace(expr)) return q;
            expr = expr.Trim();
            if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), out var maxLe))
                return field switch
                {
                    "map" => q.Where(x => x.M.MapId <= maxLe),
                    "out" => q.Where(x => x.M.OutEntryId <= maxLe),
                    "in" => q.Where(x => x.M.InEntryId <= maxLe),
                    "qty" => q.Where(x => x.M.Qty <= maxLe),
                    "prod" => q.Where(x => x.ProdId <= maxLe),
                    _ => q
                };
            if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), out var minGe))
                return field switch
                {
                    "map" => q.Where(x => x.M.MapId >= minGe),
                    "out" => q.Where(x => x.M.OutEntryId >= minGe),
                    "in" => q.Where(x => x.M.InEntryId >= minGe),
                    "qty" => q.Where(x => x.M.Qty >= minGe),
                    "prod" => q.Where(x => x.ProdId >= minGe),
                    _ => q
                };
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), out var maxLt))
                return field switch
                {
                    "map" => q.Where(x => x.M.MapId < maxLt),
                    "out" => q.Where(x => x.M.OutEntryId < maxLt),
                    "in" => q.Where(x => x.M.InEntryId < maxLt),
                    "qty" => q.Where(x => x.M.Qty < maxLt),
                    "prod" => q.Where(x => x.ProdId < maxLt),
                    _ => q
                };
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), out var minGt))
                return field switch
                {
                    "map" => q.Where(x => x.M.MapId > minGt),
                    "out" => q.Where(x => x.M.OutEntryId > minGt),
                    "in" => q.Where(x => x.M.InEntryId > minGt),
                    "qty" => q.Where(x => x.M.Qty > minGt),
                    "prod" => q.Where(x => x.ProdId > minGt),
                    _ => q
                };
            if ((expr.Contains(':') || (expr.Contains('-') && !expr.StartsWith("-"))) && !expr.StartsWith("-"))
            {
                var sep = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 &&
                    int.TryParse(parts[0].Trim(), out var from) &&
                    int.TryParse(parts[1].Trim(), out var to))
                {
                    if (from > to) (from, to) = (to, from);
                    return field switch
                    {
                        "map" => q.Where(x => x.M.MapId >= from && x.M.MapId <= to),
                        "out" => q.Where(x => x.M.OutEntryId >= from && x.M.OutEntryId <= to),
                        "in" => q.Where(x => x.M.InEntryId >= from && x.M.InEntryId <= to),
                        "qty" => q.Where(x => x.M.Qty >= from && x.M.Qty <= to),
                        "prod" => q.Where(x => x.ProdId >= from && x.ProdId <= to),
                        _ => q
                    };
                }
            }
            if (int.TryParse(expr, out var exact))
                return field switch
                {
                    "map" => q.Where(x => x.M.MapId == exact),
                    "out" => q.Where(x => x.M.OutEntryId == exact),
                    "in" => q.Where(x => x.M.InEntryId == exact),
                    "qty" => q.Where(x => x.M.Qty == exact),
                    "prod" => q.Where(x => x.ProdId == exact),
                    _ => q
                };
            return q;
        }

        private static IQueryable<FifoListRow> ApplyDecimalExprUnitCost(IQueryable<FifoListRow> q, string? expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return q;
            var s = expr.Trim();
            if (s.StartsWith("<=") && s.Length > 2 && decimal.TryParse(s.AsSpan(2), NumberStyles.Any, Inv, out var max))
                return q.Where(x => x.M.UnitCost <= max);
            if (s.StartsWith(">=") && s.Length > 2 && decimal.TryParse(s.AsSpan(2), NumberStyles.Any, Inv, out var min))
                return q.Where(x => x.M.UnitCost >= min);
            if (s.StartsWith("<") && !s.StartsWith("<=") && s.Length > 1 && decimal.TryParse(s.AsSpan(1), NumberStyles.Any, Inv, out var max2))
                return q.Where(x => x.M.UnitCost < max2);
            if (s.StartsWith(">") && !s.StartsWith(">=") && s.Length > 1 && decimal.TryParse(s.AsSpan(1), NumberStyles.Any, Inv, out var min2))
                return q.Where(x => x.M.UnitCost > min2);
            if ((s.Contains(':') || (s.Contains('-') && !s.StartsWith("-"))) && !s.StartsWith("-"))
            {
                var sep = s.Contains(':') ? ':' : '-';
                var parts = s.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 &&
                    decimal.TryParse(parts[0].Trim(), NumberStyles.Any, Inv, out var from) &&
                    decimal.TryParse(parts[1].Trim(), NumberStyles.Any, Inv, out var to))
                {
                    if (from > to) (from, to) = (to, from);
                    return q.Where(x => x.M.UnitCost >= from && x.M.UnitCost <= to);
                }
            }
            if (decimal.TryParse(s, NumberStyles.Any, Inv, out var exact))
                return q.Where(x => x.M.UnitCost == exact);
            return q;
        }


        private IQueryable<FifoListRow> ApplyColumnFilters(
            IQueryable<FifoListRow> q,
            string? filterCol_map, string? filterCol_mapExpr,
            string? filterCol_out, string? filterCol_outExpr,
            string? filterCol_in, string? filterCol_inExpr,
            string? filterCol_qty, string? filterCol_qtyExpr,
            string? filterCol_prod, string? filterCol_prodExpr,
            string? filterCol_unitcost, string? filterCol_unitcostExpr,
            string? filterCol_prodname,
            string? filterCol_outsource,
            string? filterCol_insource)
        {
            if (!string.IsNullOrWhiteSpace(filterCol_mapExpr))
                q = ApplyInt32Expr(q, filterCol_mapExpr, "map");
            else if (!string.IsNullOrWhiteSpace(filterCol_map))
            {
                var ids = filterCol_map.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (ids.Count > 0) q = q.Where(x => ids.Contains(x.M.MapId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_outExpr))
                q = ApplyInt32Expr(q, filterCol_outExpr, "out");
            else if (!string.IsNullOrWhiteSpace(filterCol_out))
            {
                var ids = filterCol_out.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (ids.Count > 0) q = q.Where(x => ids.Contains(x.M.OutEntryId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_inExpr))
                q = ApplyInt32Expr(q, filterCol_inExpr, "in");
            else if (!string.IsNullOrWhiteSpace(filterCol_in))
            {
                var ids = filterCol_in.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (ids.Count > 0) q = q.Where(x => ids.Contains(x.M.InEntryId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_qtyExpr))
                q = ApplyInt32Expr(q, filterCol_qtyExpr, "qty");
            else if (!string.IsNullOrWhiteSpace(filterCol_qty))
            {
                var ids = filterCol_qty.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (ids.Count > 0) q = q.Where(x => ids.Contains(x.M.Qty));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_prodExpr))
                q = ApplyInt32Expr(q, filterCol_prodExpr, "prod");
            else if (!string.IsNullOrWhiteSpace(filterCol_prod))
            {
                var ids = filterCol_prod.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (ids.Count > 0) q = q.Where(x => ids.Contains(x.ProdId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_unitcostExpr))
                q = ApplyDecimalExprUnitCost(q, filterCol_unitcostExpr);
            else if (!string.IsNullOrWhiteSpace(filterCol_unitcost))
            {
                var vals = filterCol_unitcost.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => decimal.TryParse(s.Trim(), NumberStyles.Any, Inv, out var v) ? v : (decimal?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (vals.Count > 0) q = q.Where(x => vals.Contains(x.M.UnitCost));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_prodname))
            {
                var vals = filterCol_prodname.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    q = q.Where(x => x.ProdName != null && vals.Contains(x.ProdName));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_outsource))
            {
                var vals = filterCol_outsource.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    q = q.Where(x => vals.Contains(x.OutL.SourceType + " " + x.OutL.SourceId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_insource))
            {
                var vals = filterCol_insource.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    q = q.Where(x => vals.Contains(x.InL.SourceType + " " + x.InL.SourceId));
            }

            return q;
        }

        private static IQueryable<FifoListRow> ApplyFifoSearch(
            IQueryable<FifoListRow> q,
            string? search,
            string? searchBy,
            string? searchMode)
        {
            if (string.IsNullOrWhiteSpace(search)) return q;
            var term = search.Trim();
            var sb = (searchBy ?? "all").Trim().ToLowerInvariant();
            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm != "starts" && sm != "ends") sm = "contains";

            switch (sb)
            {
                case "map":
                    return int.TryParse(term, out var mapId)
                        ? q.Where(x => x.M.MapId == mapId)
                        : q;
                case "out":
                    return int.TryParse(term, out var outId)
                        ? q.Where(x => x.M.OutEntryId == outId)
                        : q;
                case "in":
                    return int.TryParse(term, out var inId)
                        ? q.Where(x => x.M.InEntryId == inId)
                        : q;
                case "qty":
                    return int.TryParse(term, out var qty)
                        ? q.Where(x => x.M.Qty == qty)
                        : q;
                case "prod":
                    return int.TryParse(term, out var pid)
                        ? q.Where(x => x.ProdId == pid)
                        : q;
                case "prodname":
                    return sm switch
                    {
                        "starts" => q.Where(x => x.ProdName != null &&
                            (x.ProdName.StartsWith(term) || x.ProdName.Replace("ة", "ه").StartsWith(term.Replace("ة", "ه")))),
                        "ends" => q.Where(x => x.ProdName != null &&
                            (x.ProdName.EndsWith(term) || x.ProdName.Replace("ة", "ه").EndsWith(term.Replace("ة", "ه")))),
                        _ => q.Where(x => x.ProdName != null &&
                            (x.ProdName.Contains(term) || x.ProdName.Replace("ة", "ه").Contains(term.Replace("ة", "ه"))))
                    };
                case "unitcost":
                    return decimal.TryParse(term, NumberStyles.Any, Inv, out var uc)
                        ? q.Where(x => x.M.UnitCost == uc)
                        : q;
                case "outsource":
                    return sm == "starts"
                        ? q.Where(x => (x.OutL.SourceType + " " + x.OutL.SourceId).StartsWith(term))
                        : sm == "ends"
                            ? q.Where(x => (x.OutL.SourceType + " " + x.OutL.SourceId).EndsWith(term))
                            : q.Where(x => (x.OutL.SourceType + " " + x.OutL.SourceId).Contains(term));
                case "insource":
                    return sm == "starts"
                        ? q.Where(x => (x.InL.SourceType + " " + x.InL.SourceId).StartsWith(term))
                        : sm == "ends"
                            ? q.Where(x => (x.InL.SourceType + " " + x.InL.SourceId).EndsWith(term))
                            : q.Where(x => (x.InL.SourceType + " " + x.InL.SourceId).Contains(term));
                case "all":
                default:
                {
                    var hasInt = int.TryParse(term, out var allInt);
                    var hasDec = decimal.TryParse(term, NumberStyles.Any, Inv, out var allDec);
                    return q.Where(x =>
                        (hasInt && x.M.MapId == allInt) ||
                        (hasInt && x.M.OutEntryId == allInt) ||
                        (hasInt && x.M.InEntryId == allInt) ||
                        (hasInt && x.M.Qty == allInt) ||
                        (hasInt && x.ProdId == allInt) ||
                        (hasDec && x.M.UnitCost == allDec) ||
                        (x.ProdName != null && (x.ProdName.Contains(term) || x.ProdName.Replace("ة", "ه").Contains(term.Replace("ة", "ه")))) ||
                        (x.OutL.SourceType + " " + x.OutL.SourceId).Contains(term) ||
                        (x.InL.SourceType + " " + x.InL.SourceId).Contains(term));
                }
            }
        }

        private static IQueryable<FifoListRow> ApplyFifoSort(IQueryable<FifoListRow> q, string? sort, string? dir)
        {
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            var s = (sort ?? "MapId").Trim().ToLowerInvariant();
            return s switch
            {
                "mapid" or "map" => desc ? q.OrderByDescending(x => x.M.MapId) : q.OrderBy(x => x.M.MapId),
                "outentryid" or "out" => desc ? q.OrderByDescending(x => x.M.OutEntryId) : q.OrderBy(x => x.M.OutEntryId),
                "inentryid" or "in" => desc ? q.OrderByDescending(x => x.M.InEntryId) : q.OrderBy(x => x.M.InEntryId),
                "qty" => desc ? q.OrderByDescending(x => x.M.Qty) : q.OrderBy(x => x.M.Qty),
                "unitcost" => desc ? q.OrderByDescending(x => x.M.UnitCost) : q.OrderBy(x => x.M.UnitCost),
                "prodid" or "prod" => desc ? q.OrderByDescending(x => x.ProdId) : q.OrderBy(x => x.ProdId),
                "prodname" => desc ? q.OrderByDescending(x => x.ProdName) : q.OrderBy(x => x.ProdName),
                _ => desc ? q.OrderByDescending(x => x.M.MapId) : q.OrderBy(x => x.M.MapId)
            };
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",
            string? searchMode = "contains",
            string? sort = "MapId",
            string? dir = "asc",
            int page = 1,
            int pageSize = 10,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_map = null,
            string? filterCol_mapExpr = null,
            string? filterCol_out = null,
            string? filterCol_outExpr = null,
            string? filterCol_in = null,
            string? filterCol_inExpr = null,
            string? filterCol_qty = null,
            string? filterCol_qtyExpr = null,
            string? filterCol_prod = null,
            string? filterCol_prodExpr = null,
            string? filterCol_prodname = null,
            string? filterCol_outsource = null,
            string? filterCol_insource = null,
            string? filterCol_unitcost = null,
            string? filterCol_unitcostExpr = null)
        {
            var pageSizeQuery = Request.Query["pageSize"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageSizeQuery) && int.TryParse(pageSizeQuery, out var psVal))
                pageSize = psVal;
            if (pageSize < 0) pageSize = 10;
            if (pageSize > 0 && pageSize != 10 && pageSize != 25 && pageSize != 50 && pageSize != 100 && pageSize != 200)
                pageSize = 10;

            searchMode ??= "contains";
            sort ??= "MapId";
            dir ??= "asc";

            IQueryable<FifoListRow> q = BaseFifoJoin();

            q = ApplyColumnFilters(q,
                filterCol_map, filterCol_mapExpr,
                filterCol_out, filterCol_outExpr,
                filterCol_in, filterCol_inExpr,
                filterCol_qty, filterCol_qtyExpr,
                filterCol_prod, filterCol_prodExpr,
                filterCol_unitcost, filterCol_unitcostExpr,
                filterCol_prodname,
                filterCol_outsource,
                filterCol_insource);

            if (fromCode.HasValue)
                q = q.Where(x => x.M.MapId >= fromCode.Value);
            if (toCode.HasValue)
                q = q.Where(x => x.M.MapId <= toCode.Value);

            q = ApplyFifoSearch(q, search, searchBy, searchMode);
            q = ApplyFifoSort(q, sort, dir);

            var totalRows = await q.CountAsync();

            int effectivePageSize = pageSize;
            if (pageSize == 0)
            {
                effectivePageSize = totalRows == 0 ? 10 : Math.Min(totalRows, 100_000);
                page = 1;
            }

            var totalPages = pageSize == 0 ? 1 : (int)Math.Ceiling(totalRows / (double)effectivePageSize);
            if (totalPages < 1) totalPages = 1;
            page = Math.Max(1, Math.Min(page, totalPages));

            var skip = pageSize == 0 ? 0 : (page - 1) * effectivePageSize;
            var take = pageSize == 0 ? effectivePageSize : effectivePageSize;

            var pageRows = await q.Skip(skip).Take(take).Select(x => x.M).ToListAsync();

            var outIds = pageRows.Select(x => x.OutEntryId).Distinct().ToList();
            var inIds = pageRows.Select(x => x.InEntryId).Distinct().ToList();
            var outLedgers = await _db.StockLedger.AsNoTracking()
                .Where(sl => outIds.Contains(sl.EntryId))
                .Select(sl => new { sl.EntryId, sl.ProdId, sl.SourceType, sl.SourceId })
                .ToListAsync();
            var inLedgers = await _db.StockLedger.AsNoTracking()
                .Where(sl => inIds.Contains(sl.EntryId))
                .Select(sl => new { sl.EntryId, sl.SourceType, sl.SourceId })
                .ToListAsync();
            var prodIds = outLedgers.Select(x => x.ProdId).Distinct().ToList();
            var prodNames = prodIds.Count > 0
                ? await _db.Products.AsNoTracking().Where(p => prodIds.Contains(p.ProdId))
                    .ToDictionaryAsync(p => p.ProdId, p => p.ProdName ?? "")
                : new Dictionary<int, string>();
            var outDict = new Dictionary<int, object>();
            foreach (var o in outLedgers) outDict[o.EntryId] = o;
            var inDict = new Dictionary<int, object>();
            foreach (var i in inLedgers) inDict[i.EntryId] = i;

            ViewBag.OutLedgerDict = outDict;
            ViewBag.InLedgerDict = inDict;
            ViewBag.ProdNames = prodNames;

            ViewBag.Search = search ?? "";
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.SearchMode = searchMode;
            ViewBag.Sort = sort;
            ViewBag.Dir = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc";
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalPages = totalPages;
            ViewBag.RangeStart = totalRows == 0 ? 0 : (pageSize == 0 ? 1 : ((page - 1) * effectivePageSize) + 1);
            ViewBag.RangeEnd = pageSize == 0 || totalRows == 0 ? totalRows : Math.Min(totalRows, page * effectivePageSize);
            ViewBag.TotalRows = totalRows;

            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;

            ViewBag.FilterCol_Map = filterCol_map;
            ViewBag.FilterCol_MapExpr = filterCol_mapExpr;
            ViewBag.FilterCol_Out = filterCol_out;
            ViewBag.FilterCol_OutExpr = filterCol_outExpr;
            ViewBag.FilterCol_In = filterCol_in;
            ViewBag.FilterCol_InExpr = filterCol_inExpr;
            ViewBag.FilterCol_Qty = filterCol_qty;
            ViewBag.FilterCol_QtyExpr = filterCol_qtyExpr;
            ViewBag.FilterCol_Prod = filterCol_prod;
            ViewBag.FilterCol_ProdExpr = filterCol_prodExpr;
            ViewBag.FilterCol_ProdName = filterCol_prodname;
            ViewBag.FilterCol_OutSource = filterCol_outsource;
            ViewBag.FilterCol_InSource = filterCol_insource;
            ViewBag.FilterCol_UnitCost = filterCol_unitcost;
            ViewBag.FilterCol_UnitCostExpr = filterCol_unitcostExpr;

            return View(pageRows);
        }

        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var col = (column ?? "").Trim().ToLowerInvariant();
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();

            var q = BaseFifoJoin();

            List<(string Value, string Display)> items = col switch
            {
                "map" => (await q.Select(x => x.M.MapId).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "out" => (await q.Select(x => x.M.OutEntryId).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "in" => (await q.Select(x => x.M.InEntryId).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "qty" => (await q.Select(x => x.M.Qty).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "prod" => (await q.Select(x => x.ProdId).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "unitcost" => (await q.Select(x => x.M.UnitCost).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v.ToString(Inv), v.ToString("0.0000", Inv))).ToList(),
                "prodname" => (await q.Where(x => x.ProdName != null).Select(x => x.ProdName!).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v, v)).ToList(),
                "outsource" => (await q.Select(x => x.OutL.SourceType + " " + x.OutL.SourceId).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v, v)).ToList(),
                "insource" => (await q.Select(x => x.InL.SourceType + " " + x.InL.SourceId).Distinct().OrderBy(v => v).Take(500).ToListAsync())
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

        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy = "all",
            string? searchMode = "contains",
            string? sort = "MapId",
            string? dir = "asc",
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_map = null,
            string? filterCol_mapExpr = null,
            string? filterCol_out = null,
            string? filterCol_outExpr = null,
            string? filterCol_in = null,
            string? filterCol_inExpr = null,
            string? filterCol_qty = null,
            string? filterCol_qtyExpr = null,
            string? filterCol_prod = null,
            string? filterCol_prodExpr = null,
            string? filterCol_prodname = null,
            string? filterCol_outsource = null,
            string? filterCol_insource = null,
            string? filterCol_unitcost = null,
            string? filterCol_unitcostExpr = null,
            string format = "excel")
        {
            IQueryable<FifoListRow> q = BaseFifoJoin();

            q = ApplyColumnFilters(q,
                filterCol_map, filterCol_mapExpr,
                filterCol_out, filterCol_outExpr,
                filterCol_in, filterCol_inExpr,
                filterCol_qty, filterCol_qtyExpr,
                filterCol_prod, filterCol_prodExpr,
                filterCol_unitcost, filterCol_unitcostExpr,
                filterCol_prodname,
                filterCol_outsource,
                filterCol_insource);

            if (fromCode.HasValue)
                q = q.Where(x => x.M.MapId >= fromCode.Value);
            if (toCode.HasValue)
                q = q.Where(x => x.M.MapId <= toCode.Value);

            q = ApplyFifoSearch(q, search, searchBy, searchMode);
            q = ApplyFifoSort(q, sort, dir);

            var data = await q.Select(x => x.M).ToListAsync();

            var outIds = data.Select(x => x.OutEntryId).Distinct().ToList();
            var inIds = data.Select(x => x.InEntryId).Distinct().ToList();
            var outLedgersExp = await _db.StockLedger.AsNoTracking()
                .Where(sl => outIds.Contains(sl.EntryId))
                .Select(sl => new { sl.EntryId, sl.ProdId, sl.SourceType, sl.SourceId })
                .ToListAsync();
            var inLedgersExp = await _db.StockLedger.AsNoTracking()
                .Where(sl => inIds.Contains(sl.EntryId))
                .Select(sl => new { sl.EntryId, sl.SourceType, sl.SourceId })
                .ToListAsync();
            var prodIdsExp = outLedgersExp.Select(o => o.ProdId).Distinct().ToList();
            var prodNamesExp = prodIdsExp.Count > 0
                ? await _db.Products.AsNoTracking().Where(p => prodIdsExp.Contains(p.ProdId)).ToDictionaryAsync(p => p.ProdId, p => p.ProdName ?? "")
                : new Dictionary<int, string>();
            var outDictExp = outLedgersExp.ToDictionary(x => x.EntryId);
            var inDictExp = inLedgersExp.ToDictionary(x => x.EntryId);

            var sb = new StringBuilder();
            sb.AppendLine("كود الربط,كود الصنف,اسم الصنف,الخروج (قيد),مصدر الخروج,الدخول (قيد),مصدر الدخول,الكمية,تكلفة الوحدة");

            foreach (var x in data)
            {
                var o = outDictExp.GetValueOrDefault(x.OutEntryId);
                var i = inDictExp.GetValueOrDefault(x.InEntryId);
                int? pid = o?.ProdId;
                string pname = (pid.HasValue && prodNamesExp.TryGetValue(pid.Value, out var n)) ? n : "";
                if (!string.IsNullOrEmpty(pname) && pname.Contains(',')) pname = "\"" + pname.Replace("\"", "\"\"") + "\"";
                string outSrc = o != null ? $"{o.SourceType} {o.SourceId}" : "";
                string inSrc = i != null ? $"{i.SourceType} {i.SourceId}" : "";
                sb.AppendLine($"{x.MapId},{pid ?? 0},{pname},{x.OutEntryId},{outSrc},{x.InEntryId},{inSrc},{x.Qty},{x.UnitCost.ToString(Inv)}");
            }

            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString());
            var fileName = ExcelExportNaming.ArabicTimestampedFileName("ربط FIFO للمخزون", ".csv");
            return File(bytes, "text/csv; charset=utf-8", fileName);
        }
    }
}
