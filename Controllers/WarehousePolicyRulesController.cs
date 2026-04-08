using ClosedXML.Excel;
using ERP.Data;                             // كائن الاتصال بقاعدة البيانات AppDbContext
using ERP.Filters;
using ERP.Infrastructure;                  // كلاس PagedResult لتقسيم الصفحات + الفلاتر
using ERP.Models;                          // الموديل WarehousePolicyRule
using ERP.Security;
using ERP.Services.Caching;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;   // علشان SelectList
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;          // القواميس Dictionary
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;             // التعبيرات Expressions
using System.Text;                         // StringBuilder للتصدير
using System.Threading.Tasks;




namespace ERP.Controllers
{
    /// <summary>
    /// إدارة جدول قواعد السياسات على مستوى المخزن (WarehousePolicyRule)
    /// كل صف = سياسة معيّنة (PolicyId) داخل مخزن معيّن (WarehouseId)
    /// وتحدد نسبة ربح المخزن وحدّ الخصم المسموح للعميل.
    /// </summary>
    [RequirePermission("WarehousePolicyRules.Index")]
    public class WarehousePolicyRulesController : Controller
    {
        private readonly AppDbContext _context;   // متغير: اتصال بقاعدة البيانات
        private readonly ILookupCacheService _lookupCache;

        public WarehousePolicyRulesController(AppDbContext context, ILookupCacheService lookupCache)
        {
            _context = context;
            _lookupCache = lookupCache;
        }














        // =========================
        // دالة خاصة لبناء استعلام قواعد السياسات
        // (بحث + فلتر كود من/إلى + فلتر تاريخ اختياري + ترتيب)
        // =========================
        private IQueryable<WarehousePolicyRule> BuildRulesQuery(
            string? search,
            string? searchBy,
            string? searchMode,
            string? sort,
            string? dir,
            int? fromCode,
            int? toCode,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate)
        {
            // 1) الاستعلام الأساسي مع تحميل أسماء السياسة والمخزن
            IQueryable<WarehousePolicyRule> q =
                _context.WarehousePolicyRules
                    .Include(r => r.Warehouse)
                    .Include(r => r.Policy)
                    .AsNoTracking();

            // 2) فلتر الكود من/إلى (كود القاعدة نفسها Id)
            if (fromCode.HasValue)
                q = q.Where(x => x.Id >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(x => x.Id <= toCode.Value);

            // 3) فلتر التاريخ (على CreatedAt) لو مفعّل
            if (useDateRange)
            {
                if (fromDate.HasValue)
                    q = q.Where(x => x.CreatedAt >= fromDate.Value);

                if (toDate.HasValue)
                    q = q.Where(x => x.CreatedAt <= toDate.Value);
            }

            // 4) بحث مخصص لـ created و profit و maxdisc
            string? searchForSort = search;
            string? searchByForSort = searchBy;
            if (!string.IsNullOrWhiteSpace(search) && !string.IsNullOrWhiteSpace(searchBy))
            {
                var sb = searchBy.Trim().ToLowerInvariant();
                var text = search!.Trim();
                if (sb == "created" && DateTime.TryParse(text, out var dtCreated))
                {
                    q = q.Where(x => x.CreatedAt.Date == dtCreated.Date);
                    searchForSort = null;
                    searchByForSort = null;
                }
                else if (sb == "profit" && decimal.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var profitVal))
                {
                    q = q.Where(x => x.ProfitPercent == profitVal);
                    searchForSort = null;
                    searchByForSort = null;
                }
                else if (sb == "maxdisc" && decimal.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var maxVal))
                {
                    q = q.Where(x => x.MaxDiscountToCustomer.HasValue && x.MaxDiscountToCustomer.Value == maxVal);
                    searchForSort = null;
                    searchByForSort = null;
                }
            }

            // 5) الحقول النصية للبحث
            var stringFields =
                new Dictionary<string, Expression<Func<WarehousePolicyRule, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["warehousename"] = x => x.Warehouse != null ? x.Warehouse.WarehouseName : null,
                    ["policyname"] = x => x.Policy != null ? x.Policy.Name : null
                };

            // 6) الحقول العددية للبحث
            var intFields =
                new Dictionary<string, Expression<Func<WarehousePolicyRule, int>>>()
                {
                    ["id"] = x => x.Id,
                    ["warehouse"] = x => x.WarehouseId,
                    ["policy"] = x => x.PolicyId
                };

            // 7) حقول الترتيب
            var orderFields =
                new Dictionary<string, Expression<Func<WarehousePolicyRule, object>>>()
                {
                    ["id"] = x => x.Id,
                    ["warehouse"] = x => x.WarehouseId,
                    ["warehousename"] = x => x.Warehouse != null ? x.Warehouse.WarehouseName ?? "" : "",
                    ["policy"] = x => x.PolicyId,
                    ["policyname"] = x => x.Policy != null ? x.Policy.Name ?? "" : "",
                    ["profit"] = x => x.ProfitPercent,
                    ["maxdisc"] = x => x.MaxDiscountToCustomer ?? 0m,
                    ["created"] = x => x.CreatedAt
                };

            // 8) تطبيق البحث + الترتيب باستخدام الإكستنشن الموحد
            q = q.ApplySearchSort(
                searchForSort,
                searchByForSort,
                sort,                      // اسم العمود للترتيب
                dir,                       // asc / desc
                stringFields,
                intFields,
                orderFields,
                defaultSearchBy: "all",    // البحث الافتراضي في الكل
                defaultSortBy: "id",       // الترتيب الافتراضي بالكود
                searchMode: searchMode
            );

            return q;
        }

        private static readonly char[] _filterSep = { '|', ',', ';' };

        private static string NormalizeNumericExpr(string? value)
        {
            var text = (value ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text))
                return text;

            var chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = chars[i] switch
                {
                    >= '\u0660' and <= '\u0669' => (char)('0' + (chars[i] - '\u0660')),
                    >= '\u06F0' and <= '\u06F9' => (char)('0' + (chars[i] - '\u06F0')),
                    '\u061B' or '\u0589' or '\uFE13' or '\uFE55' => ':',
                    '\u2010' or '\u2011' or '\u2012' or '\u2013' or '\u2014' or '\u2015' or '\u2212' => '-',
                    '\u2264' => '≤',
                    '\u2265' => '≥',
                    _ => chars[i]
                };
            }

            text = new string(chars).Replace("≤", "<=").Replace("≥", ">=").Replace(" ", string.Empty);
            if ((text.Split(',').Length - 1) == 1 && text.IndexOf('.') < 0)
                text = text.Replace(',', '.');
            return text;
        }

        private static IQueryable<WarehousePolicyRule> ApplyIntExpr(
            IQueryable<WarehousePolicyRule> q,
            string expr,
            Expression<Func<WarehousePolicyRule, int>> selector)
        {
            if (string.IsNullOrWhiteSpace(expr))
                return q;

            if (expr.StartsWith("<=") && int.TryParse(expr[2..], NumberStyles.Any, CultureInfo.InvariantCulture, out var le))
                return q.Where(Expression.Lambda<Func<WarehousePolicyRule, bool>>(Expression.LessThanOrEqual(selector.Body, Expression.Constant(le)), selector.Parameters));
            if (expr.StartsWith(">=") && int.TryParse(expr[2..], NumberStyles.Any, CultureInfo.InvariantCulture, out var ge))
                return q.Where(Expression.Lambda<Func<WarehousePolicyRule, bool>>(Expression.GreaterThanOrEqual(selector.Body, Expression.Constant(ge)), selector.Parameters));
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && int.TryParse(expr[1..], NumberStyles.Any, CultureInfo.InvariantCulture, out var lt))
                return q.Where(Expression.Lambda<Func<WarehousePolicyRule, bool>>(Expression.LessThan(selector.Body, Expression.Constant(lt)), selector.Parameters));
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && int.TryParse(expr[1..], NumberStyles.Any, CultureInfo.InvariantCulture, out var gt))
                return q.Where(Expression.Lambda<Func<WarehousePolicyRule, bool>>(Expression.GreaterThan(selector.Body, Expression.Constant(gt)), selector.Parameters));

            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var sep = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    var geBody = Expression.GreaterThanOrEqual(selector.Body, Expression.Constant(a));
                    var leBody = Expression.LessThanOrEqual(selector.Body, Expression.Constant(b));
                    return q.Where(Expression.Lambda<Func<WarehousePolicyRule, bool>>(Expression.AndAlso(geBody, leBody), selector.Parameters));
                }
            }

            if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var eq))
                return q.Where(Expression.Lambda<Func<WarehousePolicyRule, bool>>(Expression.Equal(selector.Body, Expression.Constant(eq)), selector.Parameters));

            return q;
        }

        private static IQueryable<WarehousePolicyRule> ApplyDecimalExpr(
            IQueryable<WarehousePolicyRule> q,
            string expr,
            Expression<Func<WarehousePolicyRule, decimal>> selector)
        {
            if (string.IsNullOrWhiteSpace(expr))
                return q;

            if (expr.StartsWith("<=") && decimal.TryParse(expr[2..], NumberStyles.Any, CultureInfo.InvariantCulture, out var le))
                return q.Where(Expression.Lambda<Func<WarehousePolicyRule, bool>>(Expression.LessThanOrEqual(selector.Body, Expression.Constant(le)), selector.Parameters));
            if (expr.StartsWith(">=") && decimal.TryParse(expr[2..], NumberStyles.Any, CultureInfo.InvariantCulture, out var ge))
                return q.Where(Expression.Lambda<Func<WarehousePolicyRule, bool>>(Expression.GreaterThanOrEqual(selector.Body, Expression.Constant(ge)), selector.Parameters));
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && decimal.TryParse(expr[1..], NumberStyles.Any, CultureInfo.InvariantCulture, out var lt))
                return q.Where(Expression.Lambda<Func<WarehousePolicyRule, bool>>(Expression.LessThan(selector.Body, Expression.Constant(lt)), selector.Parameters));
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && decimal.TryParse(expr[1..], NumberStyles.Any, CultureInfo.InvariantCulture, out var gt))
                return q.Where(Expression.Lambda<Func<WarehousePolicyRule, bool>>(Expression.GreaterThan(selector.Body, Expression.Constant(gt)), selector.Parameters));

            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var sep = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && decimal.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && decimal.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    var geBody = Expression.GreaterThanOrEqual(selector.Body, Expression.Constant(a));
                    var leBody = Expression.LessThanOrEqual(selector.Body, Expression.Constant(b));
                    return q.Where(Expression.Lambda<Func<WarehousePolicyRule, bool>>(Expression.AndAlso(geBody, leBody), selector.Parameters));
                }
            }

            if (decimal.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var eq))
                return q.Where(Expression.Lambda<Func<WarehousePolicyRule, bool>>(Expression.Equal(selector.Body, Expression.Constant(eq)), selector.Parameters));

            return q;
        }

        private IQueryable<WarehousePolicyRule> ApplyColumnFilters(
            IQueryable<WarehousePolicyRule> q,
            string? filterCol_id,
            string? filterCol_idExpr,
            string? filterCol_warehouse,
            string? filterCol_warehouseExpr,
            string? filterCol_warehousename,
            string? filterCol_policy,
            string? filterCol_policyExpr,
            string? filterCol_policyname,
            string? filterCol_profit,
            string? filterCol_profitExpr,
            string? filterCol_maxdisc,
            string? filterCol_maxdiscExpr,
            string? filterCol_created)
        {
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    q = q.Where(x => ids.Contains(x.Id));
            }
            else if (!string.IsNullOrWhiteSpace(filterCol_idExpr))
            {
                q = ApplyIntExpr(q, NormalizeNumericExpr(filterCol_idExpr), x => x.Id);
            }
            if (!string.IsNullOrWhiteSpace(filterCol_warehouse))
            {
                var ids = filterCol_warehouse.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    q = q.Where(x => ids.Contains(x.WarehouseId));
            }
            else if (!string.IsNullOrWhiteSpace(filterCol_warehouseExpr))
            {
                q = ApplyIntExpr(q, NormalizeNumericExpr(filterCol_warehouseExpr), x => x.WarehouseId);
            }
            if (!string.IsNullOrWhiteSpace(filterCol_warehousename))
            {
                var terms = filterCol_warehousename.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
                if (terms.Count > 0)
                    q = q.Where(x => x.Warehouse != null && terms.Contains(x.Warehouse.WarehouseName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_policy))
            {
                var ids = filterCol_policy.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    q = q.Where(x => ids.Contains(x.PolicyId));
            }
            else if (!string.IsNullOrWhiteSpace(filterCol_policyExpr))
            {
                q = ApplyIntExpr(q, NormalizeNumericExpr(filterCol_policyExpr), x => x.PolicyId);
            }
            if (!string.IsNullOrWhiteSpace(filterCol_policyname))
            {
                var terms = filterCol_policyname.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
                if (terms.Count > 0)
                    q = q.Where(x => x.Policy != null && terms.Contains(x.Policy.Name));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_profit))
            {
                var terms = filterCol_profit.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .Select(t => decimal.TryParse(t, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (terms.Count > 0)
                    q = q.Where(x => terms.Contains(x.ProfitPercent));
            }
            else if (!string.IsNullOrWhiteSpace(filterCol_profitExpr))
            {
                q = ApplyDecimalExpr(q, NormalizeNumericExpr(filterCol_profitExpr), x => x.ProfitPercent);
            }
            if (!string.IsNullOrWhiteSpace(filterCol_maxdisc))
            {
                var terms = filterCol_maxdisc.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .Select(t => decimal.TryParse(t, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (terms.Count > 0)
                    q = q.Where(x => x.MaxDiscountToCustomer.HasValue && terms.Contains(x.MaxDiscountToCustomer.Value));
            }
            else if (!string.IsNullOrWhiteSpace(filterCol_maxdiscExpr))
            {
                q = q.Where(x => x.MaxDiscountToCustomer.HasValue);
                q = ApplyDecimalExpr(q, NormalizeNumericExpr(filterCol_maxdiscExpr), x => x.MaxDiscountToCustomer ?? 0m);
            }
            if (!string.IsNullOrWhiteSpace(filterCol_created))
            {
                var terms = filterCol_created.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
                if (terms.Count > 0)
                    q = q.Where(x => terms.Any(t => x.CreatedAt.ToString("yyyy-MM-dd HH:mm").Contains(t)));
            }
            return q;
        }

        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var col = (column ?? "").Trim().ToLowerInvariant();
            var q = _context.WarehousePolicyRules
                .Include(r => r.Warehouse)
                .Include(r => r.Policy)
                .AsNoTracking();

            if (col == "id")
            {
                var ids = await q.Select(x => x.Id).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (col == "warehouse")
            {
                var ids = await q.Select(x => x.WarehouseId).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (col == "warehousename")
            {
                var list = await q.Where(x => x.Warehouse != null).Select(x => x.Warehouse!.WarehouseName!).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm))
                    list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v ?? "", display = v ?? "" }));
            }
            if (col == "policy")
            {
                var ids = await q.Select(x => x.PolicyId).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (col == "policyname")
            {
                var list = await q.Where(x => x.Policy != null).Select(x => x.Policy!.Name!).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm))
                    list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v ?? "", display = v ?? "" }));
            }
            if (col == "profit")
            {
                var values = await q.Select(x => x.ProfitPercent).Distinct().OrderBy(x => x).Take(300).ToListAsync();
                var list = values.Select(v => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Distinct().ToList();
                if (!string.IsNullOrEmpty(searchTerm))
                    list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (col == "maxdisc")
            {
                var values = await q.Where(x => x.MaxDiscountToCustomer.HasValue).Select(x => x.MaxDiscountToCustomer!.Value).Distinct().OrderBy(x => x).Take(300).ToListAsync();
                var list = values.Select(v => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Distinct().ToList();
                if (!string.IsNullOrEmpty(searchTerm))
                    list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (col == "created")
            {
                var dates = await q.Select(x => x.CreatedAt).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                var list = dates.Select(d => d.ToString("yyyy-MM-dd HH:mm")).Distinct().ToList();
                if (!string.IsNullOrEmpty(searchTerm))
                    list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            return Json(new List<object>());
        }






        // =========================
        // دالة مساعدة لتحميل قوائم المخازن والسياسات
        // forCreate: عند الإضافة نعرض فقط السياسات التي لم يُحدد ربح لها لهذا المخزن
        // =========================
        private async Task LoadLookupsAsync(int? warehouseId = null, int? policyId = null, bool forCreate = false)
        {
            var warehouses = await _lookupCache.GetWarehousesAsync();

            ViewBag.WarehouseList = new SelectList(
                warehouses,
                "WarehouseId",
                "WarehouseName",
                warehouseId
            );

            List<Policy> policies;
            if (forCreate && warehouseId.HasValue)
            {
                var usedPolicyIds = await _context.WarehousePolicyRules
                    .AsNoTracking()
                    .Where(r => r.WarehouseId == warehouseId.Value)
                    .Select(r => r.PolicyId)
                    .ToListAsync();
                policies = (await _lookupCache.GetPoliciesAsync())
                    .Where(p => !usedPolicyIds.Contains(p.PolicyId))
                    .OrderBy(p => p.Name)
                    .ToList();
            }
            else if (forCreate)
            {
                policies = new List<Policy>();
            }
            else
            {
                policies = (await _lookupCache.GetPoliciesAsync()).ToList();
            }

            ViewBag.PolicyList = new SelectList(
                policies,
                "PolicyId",
                "Name",
                policyId
            );
        }

        /// <summary>جلب السياسات التي لم يُحدد لها ربح لهذا المخزن (للتعبئة في الإضافة)</summary>
        [HttpGet]
        public async Task<IActionResult> GetPoliciesNotUsedForWarehouse(int warehouseId)
        {
            var usedPolicyIds = await _context.WarehousePolicyRules
                .AsNoTracking()
                .Where(r => r.WarehouseId == warehouseId)
                .Select(r => r.PolicyId)
                .ToListAsync();

            var list = await _context.Policies
                .AsNoTracking()
                .Where(p => !usedPolicyIds.Contains(p.PolicyId))
                .OrderBy(p => p.Name)
                .Select(p => new { id = p.PolicyId, name = p.Name })
                .ToListAsync();

            return Json(list);
        }









        // =========================
        // Index — قائمة قواعد السياسات بالمخازن
        // =========================
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",       // all | id | warehouse | policy ...
            string? searchMode = "contains",
            string? sort = "id",            // id | warehouse | policy | profit | maxdisc | created
            string? dir = "asc",            // asc | desc
            int page = 1,
            int pageSize = 25,
            int? fromCode = null,           // فلتر كود من
            int? toCode = null,             // فلتر كود إلى
            bool useDateRange = false,      // تفعيل فلتر التاريخ
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_warehouse = null,
            string? filterCol_warehouseExpr = null,
            string? filterCol_warehousename = null,
            string? filterCol_policy = null,
            string? filterCol_policyExpr = null,
            string? filterCol_policyname = null,
            string? filterCol_profit = null,
            string? filterCol_profitExpr = null,
            string? filterCol_maxdisc = null,
            string? filterCol_maxdiscExpr = null,
            string? filterCol_created = null)
        {
            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm != "starts" && sm != "ends")
                sm = "contains";

            // بناء الاستعلام طبقاً للفلاتر
            var q = BuildRulesQuery(
                search,
                searchBy,
                sm,
                sort,
                dir,
                fromCode,
                toCode,
                useDateRange,
                fromDate,
                toDate);

            q = ApplyColumnFilters(q, filterCol_id, filterCol_idExpr, filterCol_warehouse, filterCol_warehouseExpr, filterCol_warehousename, filterCol_policy, filterCol_policyExpr, filterCol_policyname, filterCol_profit, filterCol_profitExpr, filterCol_maxdisc, filterCol_maxdiscExpr, filterCol_created);

            var totalCount = await q.CountAsync();
            var activeCount = await q.CountAsync(x => x.IsActive);
            var inactiveCount = totalCount - activeCount;

            // تقسيم الصفحات (النظام الثابت)
            var model = await PagedResult<WarehousePolicyRule>.CreateAsync(q, page, pageSize);

            // تعبئة خصائص البحث/الترتيب داخل الموديل (للاستخدام في الواجهة)
            model.Search = search ?? "";
            model.SearchBy = searchBy ?? "all";
            model.SortColumn = sort ?? "id";
            model.SortDescending = (dir?.ToLower() == "desc");
            model.UseDateRange = useDateRange;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            ViewBag.Search = search ?? "";
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.SearchMode = sm;
            ViewBag.Sort = sort ?? "id";
            ViewBag.Dir = (dir ?? "asc").ToLower() == "desc" ? "desc" : "asc";
            // تمرير فلتر الكود عن طريق ViewBag (مثل فواتير المبيعات)
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.CodeFrom = fromCode;
            ViewBag.CodeTo = toCode;

            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_IdExpr = filterCol_idExpr;
            ViewBag.FilterCol_Warehouse = filterCol_warehouse;
            ViewBag.FilterCol_WarehouseExpr = filterCol_warehouseExpr;
            ViewBag.FilterCol_Warehousename = filterCol_warehousename;
            ViewBag.FilterCol_Policy = filterCol_policy;
            ViewBag.FilterCol_PolicyExpr = filterCol_policyExpr;
            ViewBag.FilterCol_Policyname = filterCol_policyname;
            ViewBag.FilterCol_Profit = filterCol_profit;
            ViewBag.FilterCol_ProfitExpr = filterCol_profitExpr;
            ViewBag.FilterCol_Maxdisc = filterCol_maxdisc;
            ViewBag.FilterCol_MaxdiscExpr = filterCol_maxdiscExpr;
            ViewBag.FilterCol_Created = filterCol_created;
            ViewBag.ActiveCount = activeCount;
            ViewBag.InactiveCount = inactiveCount;
            ViewBag.TotalCount = totalCount;

            // حقل التاريخ المستخدم في الفلترة (للنموذج الموحد)
            ViewBag.DateField = "CreatedAt";

            return View(model);
        }








        // =========================
        // Export — تصدير قواعد السياسات (CSV يفتح في Excel)
        // =========================
        [HttpGet]
        // تصدير قواعد سياسات المخازن
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy,
            string? searchMode,
            string? sort,
            string? dir,
            int? fromCode = null,
            int? toCode = null,
            int? codeFrom = null,
            int? codeTo = null,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_warehouse = null,
            string? filterCol_warehouseExpr = null,
            string? filterCol_warehousename = null,
            string? filterCol_policy = null,
            string? filterCol_policyExpr = null,
            string? filterCol_policyname = null,
            string? filterCol_profit = null,
            string? filterCol_profitExpr = null,
            string? filterCol_maxdisc = null,
            string? filterCol_maxdiscExpr = null,
            string? filterCol_created = null,
            string format = "excel")
        {
            if (!fromCode.HasValue && codeFrom.HasValue) fromCode = codeFrom;
            if (!toCode.HasValue && codeTo.HasValue) toCode = codeTo;

            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm != "starts" && sm != "ends")
                sm = "contains";

            var query = BuildRulesQuery(
                search,
                searchBy,
                sm,
                sort,
                dir,
                fromCode,
                toCode,
                useDateRange,
                fromDate,
                toDate);

            query = ApplyColumnFilters(query, filterCol_id, filterCol_idExpr, filterCol_warehouse, filterCol_warehouseExpr, filterCol_warehousename, filterCol_policy, filterCol_policyExpr, filterCol_policyname, filterCol_profit, filterCol_profitExpr, filterCol_maxdisc, filterCol_maxdiscExpr, filterCol_created);

            // 2) جلب كل النتائج (بدون Paging) — Include موجود في BuildRulesQuery
            var list = await query.ToListAsync();

            // 3) لو المطلوب Excel (افتراضي)
            if (string.Equals(format, "excel", StringComparison.OrdinalIgnoreCase))
            {
                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("قواعد سياسات المخازن"));

                int row = 1;

                // عناوين الأعمدة
                worksheet.Cell(row, 1).Value = "المعرف";
                worksheet.Cell(row, 2).Value = "كود المخزن";
                worksheet.Cell(row, 3).Value = "اسم المخزن";
                worksheet.Cell(row, 4).Value = "كود السياسة";
                worksheet.Cell(row, 5).Value = "اسم السياسة";
                worksheet.Cell(row, 6).Value = "نسبة الربح %";
                worksheet.Cell(row, 7).Value = "أقصى خصم للعميل % (اختياري)";
                worksheet.Cell(row, 8).Value = "مفعّلة؟";
                worksheet.Cell(row, 9).Value = "تاريخ الإنشاء";
                worksheet.Cell(row, 10).Value = "آخر تعديل";

                // تنسيق الهيدر
                var header = worksheet.Range(row, 1, row, 10);
                header.Style.Font.Bold = true;

                // كتابة البيانات
                foreach (var r in list)
                {
                    row++;

                    worksheet.Cell(row, 1).Value = r.Id;
                    worksheet.Cell(row, 2).Value = r.WarehouseId;
                    worksheet.Cell(row, 3).Value = r.Warehouse?.WarehouseName ?? "";   // 🔴 عدّل Name لو مختلف
                    worksheet.Cell(row, 4).Value = r.PolicyId;
                    worksheet.Cell(row, 5).Value = r.Policy?.Name ?? "";
                    worksheet.Cell(row, 6).Value = r.ProfitPercent;
                    worksheet.Cell(row, 7).Value = r.MaxDiscountToCustomer;
                    worksheet.Cell(row, 8).Value = r.IsActive ? "مفعّلة" : "موقوفة";
                    worksheet.Cell(row, 9).Value = r.CreatedAt;
                    worksheet.Cell(row, 10).Value = r.UpdatedAt;
                }

                worksheet.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                stream.Position = 0;

                var fileName = ExcelExportNaming.ArabicTimestampedFileName("قواعد سياسات المخازن", ".xlsx");
                const string contentType =
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

                return File(stream.ToArray(), contentType, fileName);
            }
            else
            {
                // 4) حالة CSV
                var sb = new StringBuilder();

                sb.AppendLine("المعرف,كود المخزن,اسم المخزن,كود السياسة,اسم السياسة,نسبة الربح %,أقصى خصم للعميل % (اختياري),مفعّلة؟,تاريخ الإنشاء,آخر تعديل");

                foreach (var r in list)
                {
                    string warehouseName = (r.Warehouse?.WarehouseName ?? string.Empty)
                        .Replace("\"", "\"\"");
                    string policyName = (r.Policy?.Name ?? string.Empty)
                        .Replace("\"", "\"\"");

                    string created = r.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                    string updated = r.UpdatedAt.HasValue
                        ? r.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm")
                        : string.Empty;

                    string profitText = r.ProfitPercent
                        .ToString("0.##", CultureInfo.InvariantCulture);

                    string maxDiscountText = r.MaxDiscountToCustomer.HasValue
                        ? r.MaxDiscountToCustomer.Value.ToString("0.##", CultureInfo.InvariantCulture)
                        : string.Empty;

                    sb.AppendLine(
                        $"{r.Id}," +
                        $"{r.WarehouseId}," +
                        $"\"{warehouseName}\"," +
                        $"{r.PolicyId}," +
                        $"\"{policyName}\"," +
                        $"{profitText}," +
                        $"{maxDiscountText}," +
                        $"\"{(r.IsActive ? "مفعّلة" : "موقوفة")}\"," +
                        $"{created}," +
                        $"{updated}");
                }

                byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString());
                string fileName = ExcelExportNaming.ArabicTimestampedFileName("قواعد سياسات المخازن", ".csv");

                return File(bytes, "text/csv; charset=utf-8", fileName);
            }
        }










        // =========================
        // Details — عرض القاعدة (قراءة فقط)
        // =========================
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
                return NotFound();

            var rule = await _context.WarehousePolicyRules
                                     .AsNoTracking()
                                     .FirstOrDefaultAsync(r => r.Id == id.Value);
            if (rule == null)
                return NotFound();

            return View(rule);
        }








        // =========================
        // Create — GET: شاشة إضافة قاعدة جديدة
        // =========================
        // GET: WarehousePolicyRules/Create
        // =========================
        // Create — GET: شاشة إضافة قاعدة جديدة
        // =========================
        public async Task<IActionResult> Create()
        {
            await LoadLookupsAsync(null, null, forCreate: true);

            var model = new WarehousePolicyRule
            {
                IsActive = true
            };

            return View(model);
        }










        // =========================
        // Create — POST
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(WarehousePolicyRule model)
        {
            // منع تكرار (نفس السياسة + نفس المخزن)
            bool duplicate = await _context.WarehousePolicyRules
                .AnyAsync(r => r.WarehouseId == model.WarehouseId && r.PolicyId == model.PolicyId);
            if (duplicate)
            {
                ModelState.AddModelError(string.Empty, "هذه السياسة تم عمل ربح لها بالفعل لهذا المخزن. اختر سياسة أخرى أو مخزنًا آخر.");
            }

            if (!ModelState.IsValid)
            {
                await LoadLookupsAsync(model.WarehouseId, model.PolicyId, forCreate: true);
                return View(model);
            }

            model.CreatedAt = DateTime.Now;

            _context.WarehousePolicyRules.Add(model);
            await _context.SaveChangesAsync();

            TempData["Msg"] = "تم إضافة قاعدة سياسة للمخزن بنجاح.";
            return RedirectToAction(nameof(Index));
        }












        // =========================
        // =========================
        // Edit — GET
        // =========================
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
                return NotFound();

            var rule = await _context.WarehousePolicyRules
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id.Value);

            if (rule == null)
                return NotFound();

            // تحميل القوائم مع تحديد القيم المختارة
            await LoadLookupsAsync(rule.WarehouseId, rule.PolicyId);

            return View(rule);
        }











        // =========================
        // Edit — POST
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, WarehousePolicyRule model)
        {
            if (id != model.Id)
                return NotFound();

            if (!ModelState.IsValid)
            {
                await LoadLookupsAsync(model.WarehouseId, model.PolicyId);
                return View(model);
            }

            try
            {
                model.UpdatedAt = DateTime.Now;
                _context.Update(model);
                await _context.SaveChangesAsync();

                TempData["Msg"] = "تم تعديل قاعدة سياسة المخزن بنجاح.";
            }
            catch (DbUpdateConcurrencyException)
            {
                bool exists = await _context.WarehousePolicyRules
                    .AnyAsync(r => r.Id == id);

                if (!exists)
                    return NotFound();

                ModelState.AddModelError(string.Empty,
                    "حدث تعارض في التعديل، من فضلك أعد تحميل الصفحة وحاول مرة أخرى.");
                await LoadLookupsAsync(model.WarehouseId, model.PolicyId);
                return View(model);
            }

            return RedirectToAction(nameof(Index));
        }
    









// =========================
// Delete — GET: صفحة تأكيد الحذف
// =========================
public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
                return NotFound();

            var rule = await _context.WarehousePolicyRules
                                     .AsNoTracking()
                                     .FirstOrDefaultAsync(r => r.Id == id.Value);
            if (rule == null)
                return NotFound();

            return View(rule);
        }








        // =========================
        // Delete — POST: حذف قاعدة واحدة
        // =========================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var rule = await _context.WarehousePolicyRules
                                     .FirstOrDefaultAsync(r => r.Id == id);
            if (rule == null)
                return NotFound();

            _context.WarehousePolicyRules.Remove(rule);
            await _context.SaveChangesAsync();

            TempData["Msg"] = "تم حذف القاعدة بنجاح.";
            return RedirectToAction(nameof(Index));
        }









        // =========================
        // BulkDelete — حذف مجموعة قواعد محددة
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["Msg"] = "لم يتم اختيار أي قواعد للحذف.";
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

            var rules = await _context.WarehousePolicyRules
                                      .Where(r => ids.Contains(r.Id))
                                      .ToListAsync();

            if (!rules.Any())
            {
                TempData["Msg"] = "لم يتم العثور على القواعد المحددة.";
                return RedirectToAction(nameof(Index));
            }

            _context.WarehousePolicyRules.RemoveRange(rules);
            await _context.SaveChangesAsync();

            TempData["Msg"] = $"تم حذف {rules.Count} قاعدة سياسة.";
            return RedirectToAction(nameof(Index));
        }








        // =========================
        // DeleteAll — حذف جميع القواعد (خطير)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll(
            string? search,
            string? searchBy,
            string? searchMode,
            string? sort,
            string? dir,
            int? fromCode = null,
            int? toCode = null,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_warehouse = null,
            string? filterCol_warehouseExpr = null,
            string? filterCol_warehousename = null,
            string? filterCol_policy = null,
            string? filterCol_policyExpr = null,
            string? filterCol_policyname = null,
            string? filterCol_profit = null,
            string? filterCol_profitExpr = null,
            string? filterCol_maxdisc = null,
            string? filterCol_maxdiscExpr = null,
            string? filterCol_created = null)
        {
            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm != "starts" && sm != "ends")
                sm = "contains";

            var q = BuildRulesQuery(
                search,
                searchBy,
                sm,
                sort,
                dir,
                fromCode,
                toCode,
                useDateRange,
                fromDate,
                toDate);

            q = ApplyColumnFilters(q, filterCol_id, filterCol_idExpr, filterCol_warehouse, filterCol_warehouseExpr, filterCol_warehousename, filterCol_policy, filterCol_policyExpr, filterCol_policyname, filterCol_profit, filterCol_profitExpr, filterCol_maxdisc, filterCol_maxdiscExpr, filterCol_created);

            var rules = await q.ToListAsync();
            if (!rules.Any())
            {
                TempData["Msg"] = "لا توجد قواعد لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            _context.WarehousePolicyRules.RemoveRange(rules);
            await _context.SaveChangesAsync();

            TempData["Msg"] = $"تم حذف جميع قواعد السياسات ({rules.Count}).";
            return RedirectToAction(nameof(Index));
        }
    }
}



