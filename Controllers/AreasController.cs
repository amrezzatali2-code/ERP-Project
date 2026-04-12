using ClosedXML.Excel;                              // تصدير Excel
using ERP.Data;                                    // كائن AppDbContext
using ERP.Filters;
using ERP.Infrastructure;                          // PagedResult + UserActivityLogger
using ERP.Models;                                  // Area, UserActionType
using ERP.Security;
using ERP.Services.Caching;
using Microsoft.AspNetCore.Mvc;                    // أساس الكنترولر
using Microsoft.AspNetCore.Mvc.Rendering;          // SelectList و SelectListItem
using Microsoft.EntityFrameworkCore;               // Include, AsNoTracking, ToListAsync
using System;                                      // متغيرات التوقيت DateTime
using System.Collections.Generic;                  // القوائم List
using System.Globalization;
using System.IO;                                  // MemoryStream للتصدير
using System.Linq;                                 // أوامر LINQ مثل Where و OrderBy
using System.Linq.Expressions;
using System.Text;                                 // UTF8Encoding للتصدير
using System.Threading.Tasks;                      // Task و async/await

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إدارة جدول المناطق (Areas)
    /// نفس نظام الحسابات: بحث + ترتيب + ترقيم + فلترة بتاريخ الإنشاء.
    /// </summary>
    public class AreasController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IUserActivityLogger _activityLogger;
        private readonly ILookupCacheService _lookupCache;

        public AreasController(
            AppDbContext db,
            IUserActivityLogger activityLogger,
            ILookupCacheService lookupCache)
        {
            _db = db;
            _activityLogger = activityLogger;
            _lookupCache = lookupCache;
        }

        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

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
                    '\u0660' => '0',
                    '\u0661' => '1',
                    '\u0662' => '2',
                    '\u0663' => '3',
                    '\u0664' => '4',
                    '\u0665' => '5',
                    '\u0666' => '6',
                    '\u0667' => '7',
                    '\u0668' => '8',
                    '\u0669' => '9',
                    _ => chars[i]
                };
            }

            return new string(chars).Replace(" ", "");
        }

        private static bool TryParseInt(string text, out int value)
            => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        private static IQueryable<T> ApplyIntExpr<T>(IQueryable<T> query, Expression<Func<T, int>> selector, string exprRaw)
        {
            var expr = NormalizeNumericExpr(exprRaw);
            if (string.IsNullOrWhiteSpace(expr))
                return query;

            var rangeSep = expr.Contains(':') ? ':' : (expr.Contains('-') ? '-' : '\0');
            if (rangeSep != '\0')
            {
                var parts = expr.Split(rangeSep, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 2)
                {
                    var hasMin = TryParseInt(parts[0], out var min);
                    var hasMax = TryParseInt(parts[1], out var max);
                    if (hasMin) query = query.Where(BuildComparison(selector, ">=", min));
                    if (hasMax) query = query.Where(BuildComparison(selector, "<=", max));
                    return query;
                }
            }

            if (expr.StartsWith(">=") && TryParseInt(expr.Substring(2), out var gte))
                return query.Where(BuildComparison(selector, ">=", gte));
            if (expr.StartsWith("<=") && TryParseInt(expr.Substring(2), out var lte))
                return query.Where(BuildComparison(selector, "<=", lte));
            if (expr.StartsWith(">") && TryParseInt(expr.Substring(1), out var gt))
                return query.Where(BuildComparison(selector, ">", gt));
            if (expr.StartsWith("<") && TryParseInt(expr.Substring(1), out var lt))
                return query.Where(BuildComparison(selector, "<", lt));

            if (TryParseInt(expr, out var eq))
                return query.Where(BuildComparison(selector, "==", eq));

            return query;
        }

        private static Expression<Func<T, bool>> BuildComparison<T>(Expression<Func<T, int>> selector, string op, int value)
        {
            var param = selector.Parameters[0];
            var left = selector.Body;
            var right = Expression.Constant(value, typeof(int));
            Expression body = op switch
            {
                "==" => Expression.Equal(left, right),
                ">" => Expression.GreaterThan(left, right),
                "<" => Expression.LessThan(left, right),
                ">=" => Expression.GreaterThanOrEqual(left, right),
                "<=" => Expression.LessThanOrEqual(left, right),
                _ => Expression.Equal(left, right),
            };
            return Expression.Lambda<Func<T, bool>>(body, param);
        }

        private static IQueryable<Area> ApplyColumnFilters(
            IQueryable<Area> query,
            string? filterCol_id,
            string? filterCol_idExpr,
            string? filterCol_name,
            string? filterCol_dist,
            string? filterCol_gov,
            string? filterCol_isactive,
            string? filterCol_created,
            string? filterCol_updated)
        {
            if (!string.IsNullOrWhiteSpace(filterCol_idExpr))
            {
                query = ApplyIntExpr(query, a => a.AreaId, filterCol_idExpr);
            }
            else if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) query = query.Where(a => ids.Contains(a.AreaId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_name))
            {
                var vals = filterCol_name.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) query = query.Where(a => vals.Contains(a.AreaName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_dist))
            {
                var vals = filterCol_dist.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) query = query.Where(a => a.District != null && vals.Contains(a.District.DistrictName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_gov))
            {
                var vals = filterCol_gov.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) query = query.Where(a => a.Governorate != null && vals.Contains(a.Governorate.GovernorateName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_isactive))
            {
                var vals = filterCol_isactive.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim().ToLowerInvariant()).ToList();
                var wantTrue = vals.Any(v => v == "true" || v == "1" || v == "\u0646\u0639\u0645");
                var wantFalse = vals.Any(v => v == "false" || v == "0" || v == "\u0644\u0627");
                if (wantTrue && !wantFalse) query = query.Where(a => a.IsActive);
                else if (wantFalse && !wantTrue) query = query.Where(a => !a.IsActive);
            }
            if (!string.IsNullOrWhiteSpace(filterCol_created))
            {
                var parts = filterCol_created.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length >= 8).ToList();
                if (parts.Count > 0)
                {
                    var mins = new List<DateTime>();
                    foreach (var p in parts)
                    {
                        if (DateTime.TryParse(p, out var dt))
                            mins.Add(new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0));
                    }

                    if (mins.Count > 0)
                    {
                        var param = Expression.Parameter(typeof(Area), "a");
                        var createdProp = Expression.Property(param, nameof(Area.CreatedAt));
                        var hasValue = Expression.Property(createdProp, "HasValue");
                        var value = Expression.Property(createdProp, "Value");

                        Expression body = Expression.Constant(false);
                        foreach (var m in mins.Distinct())
                        {
                            var ge = Expression.GreaterThanOrEqual(value, Expression.Constant(m));
                            var lt = Expression.LessThan(value, Expression.Constant(m.AddMinutes(1)));
                            var and = Expression.AndAlso(hasValue, Expression.AndAlso(ge, lt));
                            body = Expression.OrElse(body, and);
                        }

                        var pred = Expression.Lambda<Func<Area, bool>>(body, param);
                        query = query.Where(pred);
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_updated))
            {
                var parts = filterCol_updated.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length >= 8).ToList();
                if (parts.Count > 0)
                {
                    var mins = new List<DateTime>();
                    foreach (var p in parts)
                    {
                        if (DateTime.TryParse(p, out var dt))
                            mins.Add(new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0));
                    }

                    if (mins.Count > 0)
                    {
                        var param = Expression.Parameter(typeof(Area), "a");
                        var updatedProp = Expression.Property(param, nameof(Area.UpdatedAt));
                        var hasValue = Expression.Property(updatedProp, "HasValue");
                        var value = Expression.Property(updatedProp, "Value");

                        Expression body = Expression.Constant(false);
                        foreach (var m in mins.Distinct())
                        {
                            var ge = Expression.GreaterThanOrEqual(value, Expression.Constant(m));
                            var lt = Expression.LessThan(value, Expression.Constant(m.AddMinutes(1)));
                            var and = Expression.AndAlso(hasValue, Expression.AndAlso(ge, lt));
                            body = Expression.OrElse(body, and);
                        }

                        var pred = Expression.Lambda<Func<Area, bool>>(body, param);
                        query = query.Where(pred);
                    }
                }
            }
            return query;
        }

        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var valuesSearch = Request.Query["valuesSearch"].LastOrDefault();
            var searchTerm = (valuesSearch ?? search ?? "").Trim().ToLowerInvariant();
            var columnLower = (column ?? "").Trim().ToLowerInvariant();

            // تطبيق نفس فلاتر الصفحة الحالية على قائمة القيم
            var listSearch = Request.Query["listSearch"].LastOrDefault();
            var listSearchBy = Request.Query["listSearchBy"].LastOrDefault();
            var listSearchMode = Request.Query["listSearchMode"].LastOrDefault();
            var sort = Request.Query["sort"].LastOrDefault();
            var dir = Request.Query["dir"].LastOrDefault();
            bool useDateRange = string.Equals(Request.Query["useDateRange"].LastOrDefault(), "true", StringComparison.OrdinalIgnoreCase);
            DateTime? fromDate = null;
            DateTime? toDate = null;
            if (DateTime.TryParse(Request.Query["fromDate"].LastOrDefault(), out var fd)) fromDate = fd;
            if (DateTime.TryParse(Request.Query["toDate"].LastOrDefault(), out var td)) toDate = td;

            int? governorateId = null;
            if (int.TryParse(Request.Query["governorateId"].LastOrDefault(), out var gid)) governorateId = gid;
            int? districtId = null;
            if (int.TryParse(Request.Query["districtId"].LastOrDefault(), out var did)) districtId = did;

            var filterCol_id = Request.Query["filterCol_id"].LastOrDefault();
            var filterCol_idExpr = Request.Query["filterCol_idExpr"].LastOrDefault();
            var filterCol_name = Request.Query["filterCol_name"].LastOrDefault();
            var filterCol_dist = Request.Query["filterCol_dist"].LastOrDefault();
            var filterCol_gov = Request.Query["filterCol_gov"].LastOrDefault();
            var filterCol_isactive = Request.Query["filterCol_isactive"].LastOrDefault();
            var filterCol_created = Request.Query["filterCol_created"].LastOrDefault();
            var filterCol_updated = Request.Query["filterCol_updated"].LastOrDefault();

            IQueryable<Area> q = _db.Areas.AsNoTracking().Include(a => a.Governorate).Include(a => a.District);
            if (useDateRange || fromDate.HasValue || toDate.HasValue)
            {
                if (fromDate.HasValue) q = q.Where(a => a.CreatedAt.HasValue && a.CreatedAt.Value >= fromDate.Value);
                if (toDate.HasValue) q = q.Where(a => a.CreatedAt.HasValue && a.CreatedAt.Value <= toDate.Value);
            }
            if (governorateId.HasValue) q = q.Where(a => a.GovernorateId == governorateId.Value);
            if (districtId.HasValue) q = q.Where(a => a.DistrictId == districtId.Value);

            // بحث (بنفس منطق Index)
            var s = (listSearch ?? string.Empty).Trim();
            var sb = string.IsNullOrWhiteSpace(listSearchBy) ? "name" : listSearchBy.Trim().ToLowerInvariant();
            if (sb == "district") sb = "dist";
            var sm = string.IsNullOrWhiteSpace(listSearchMode) ? "contains" : listSearchMode.Trim().ToLowerInvariant();
            var isStarts = sm == "starts";
            var isEnds = sm == "ends";
            if (!isStarts && !isEnds) sm = "contains";

            if (!string.IsNullOrEmpty(s))
            {
                switch (sb)
                {
                    case "id":
                        if (int.TryParse(s, out var idValue))
                            q = q.Where(a => a.AreaId == idValue);
                        else
                            q = q.Where(a => a.AreaId.ToString().Contains(s));
                        break;

                    case "dist":
                        if (isStarts)
                            q = q.Where(a => a.District != null && a.District.DistrictName.StartsWith(s));
                        else if (isEnds)
                            q = q.Where(a => a.District != null && a.District.DistrictName.EndsWith(s));
                        else
                            q = q.Where(a => a.District != null && a.District.DistrictName.Contains(s));
                        break;

                    case "gov":
                        if (isStarts)
                            q = q.Where(a => a.Governorate != null && a.Governorate.GovernorateName.StartsWith(s));
                        else if (isEnds)
                            q = q.Where(a => a.Governorate != null && a.Governorate.GovernorateName.EndsWith(s));
                        else
                            q = q.Where(a => a.Governorate != null && a.Governorate.GovernorateName.Contains(s));
                        break;

                    case "name":
                    default:
                        if (isStarts)
                            q = q.Where(a => a.AreaName.StartsWith(s));
                        else if (isEnds)
                            q = q.Where(a => a.AreaName.EndsWith(s));
                        else
                            q = q.Where(a => a.AreaName.Contains(s));
                        break;
                }
            }
            q = ApplyColumnFilters(q, filterCol_id, filterCol_idExpr, filterCol_name, filterCol_dist, filterCol_gov, filterCol_isactive, filterCol_created, filterCol_updated);
            if (columnLower == "id" || columnLower == "areaid")
            {
                var ids = await q.Select(a => a.AreaId).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "name" || columnLower == "areaname")
            {
                var list = await q.Select(a => a.AreaName).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s != null && s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v ?? "", display = v ?? "" }));
            }
            if (columnLower == "dist" || columnLower == "district")
            {
                var list = await q.Where(a => a.District != null).Select(a => a.District!.DistrictName).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s != null && s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v ?? "", display = v ?? "" }));
            }
            if (columnLower == "gov" || columnLower == "governorate")
            {
                var list = await q.Where(a => a.Governorate != null).Select(a => a.Governorate!.GovernorateName).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s != null && s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v ?? "", display = v ?? "" }));
            }
            if (columnLower == "isactive")
                return Json(new[] { new { value = "true", display = "\u0646\u0639\u0645" }, new { value = "false", display = "\u0644\u0627" } });
            if (columnLower == "created" || columnLower == "createdat")
            {
                var list = await q.Where(a => a.CreatedAt.HasValue).Select(a => a.CreatedAt!.Value).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd HH:mm"), display = d.ToString("yyyy-MM-dd HH:mm") }));
            }
            if (columnLower == "updated" || columnLower == "updatedat")
            {
                var list = await q.Where(a => a.UpdatedAt.HasValue).Select(a => a.UpdatedAt!.Value).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd HH:mm"), display = d.ToString("yyyy-MM-dd HH:mm") }));
            }
            return Json(Array.Empty<object>());
        }

        // ===============================
        // GET: /Areas
        // قائمة المناطق + بحث/ترتيب/ترقيم + فلترة بتاريخ الإنشاء
        // ===============================
        [RequirePermission("Areas.Index")]
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy,
            string? searchMode,
            string? sort,
            string? dir,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? governorateId = null,
            int? districtId = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_name = null,
            string? filterCol_dist = null,
            string? filterCol_gov = null,
            string? filterCol_isactive = null,
            string? filterCol_created = null,
            string? filterCol_updated = null,
            int page = 1,
            int pageSize = 10)
        {
            var pageSizeQuery = Request.Query["pageSize"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageSizeQuery) && int.TryParse(pageSizeQuery, out var psVal))
                pageSize = psVal;

            var allowed = new HashSet<int> { 10, 25, 50, 100, 200, 0 };
            if (!allowed.Contains(pageSize)) pageSize = 10;
            if (pageSize < 0) pageSize = 10;

            // ===== تنظيف القيم الافتراضية =====
            search = (search ?? string.Empty).Trim();                        // نص البحث
            searchBy = string.IsNullOrWhiteSpace(searchBy) ? "name" : searchBy.ToLowerInvariant();
            if (searchBy == "district") searchBy = "dist";
            searchMode = string.IsNullOrWhiteSpace(searchMode) ? "contains" : searchMode.ToLowerInvariant();
            sort = string.IsNullOrWhiteSpace(sort) ? "name" : sort.ToLowerInvariant();         // عمود الترتيب
            dir = string.IsNullOrWhiteSpace(dir) ? "asc" : dir.ToLowerInvariant();           // اتجاه الترتيب

            bool desc = dir == "desc";               // هل الترتيب تنازلي؟

            // ===== الاستعلام الأساسي من جدول Areas =====
            var query = _db.Areas
                .Include(a => a.Governorate)                             // المحافظة
                .Include(a => a.District)!.ThenInclude(d => d.Governorate) // الحي/المركز + المحافظة
                .AsNoTracking()
                .AsQueryable();

            // ===== تطبيق البحث =====
            if (!string.IsNullOrEmpty(search))
            {
                var isStarts = searchMode == "starts";
                var isEnds = searchMode == "ends";

                switch (searchBy)
                {
                    case "id":   // البحث بالمعرّف
                        if (int.TryParse(search, out var idValue))
                        {
                            query = query.Where(a => a.AreaId == idValue);
                        }
                        else
                        {
                            query = query.Where(a => a.AreaId.ToString().Contains(search));
                        }
                        break;

                    case "dist": // البحث باسم الحي/المركز
                        if (isStarts)
                            query = query.Where(a => a.District != null && a.District.DistrictName.StartsWith(search));
                        else if (isEnds)
                            query = query.Where(a => a.District != null && a.District.DistrictName.EndsWith(search));
                        else
                            query = query.Where(a => a.District != null && a.District.DistrictName.Contains(search));
                        break;

                    case "gov":  // البحث باسم المحافظة
                        if (isStarts)
                            query = query.Where(a => a.Governorate != null && a.Governorate.GovernorateName.StartsWith(search));
                        else if (isEnds)
                            query = query.Where(a => a.Governorate != null && a.Governorate.GovernorateName.EndsWith(search));
                        else
                            query = query.Where(a => a.Governorate != null && a.Governorate.GovernorateName.Contains(search));
                        break;

                    case "name":
                    default:     // البحث باسم المنطقة
                        if (isStarts)
                            query = query.Where(a => a.AreaName.StartsWith(search));
                        else if (isEnds)
                            query = query.Where(a => a.AreaName.EndsWith(search));
                        else
                            query = query.Where(a => a.AreaName.Contains(search));
                        break;
                }
            }

            // ===== فلتر المحافظة + الحي/المركز =====
            if (governorateId.HasValue && governorateId.Value > 0)
                query = query.Where(a => a.GovernorateId == governorateId.Value);

            if (districtId.HasValue && districtId.Value > 0)
                query = query.Where(a => a.DistrictId == districtId.Value);

            if (fromCode.HasValue)
                query = query.Where(a => a.AreaId >= fromCode.Value);
            if (toCode.HasValue)
                query = query.Where(a => a.AreaId <= toCode.Value);

            // ===== فلترة بالتاريخ (تاريخ الإنشاء) =====
            if (useDateRange)
            {
                if (fromDate.HasValue)
                {
                    query = query.Where(a =>
                        a.CreatedAt.HasValue &&
                        a.CreatedAt.Value >= fromDate.Value);
                }

                if (toDate.HasValue)
                {
                    query = query.Where(a =>
                        a.CreatedAt.HasValue &&
                        a.CreatedAt.Value <= toDate.Value);
                }
            }

            query = ApplyColumnFilters(
                query,
                filterCol_id,
                filterCol_idExpr,
                filterCol_name,
                filterCol_dist,
                filterCol_gov,
                filterCol_isactive,
                filterCol_created,
                filterCol_updated);

            // ===== الترتيب (كل أعمدة الجدول لها مفتاح) =====
            // المفاتيح:
            // id       = المعرّف
            // name     = اسم المنطقة
            // dist     = الحي/المركز
            // gov      = المحافظة
            // isactive = الحالة
            // created  = تاريخ الإنشاء
            // updated  = آخر تعديل
            query = (sort, desc) switch
            {
                ("id", false) => query.OrderBy(a => a.AreaId),
                ("id", true) => query.OrderByDescending(a => a.AreaId),

                ("name", false) => query.OrderBy(a => a.AreaName),
                ("name", true) => query.OrderByDescending(a => a.AreaName),

                ("dist", false) => query.OrderBy(a => a.District!.DistrictName)
                                             .ThenBy(a => a.AreaName),
                ("dist", true) => query.OrderByDescending(a => a.District!.DistrictName)
                                             .ThenByDescending(a => a.AreaName),

                ("gov", false) => query.OrderBy(a => a.Governorate!.GovernorateName)
                                             .ThenBy(a => a.AreaName),
                ("gov", true) => query.OrderByDescending(a => a.Governorate!.GovernorateName)
                                             .ThenByDescending(a => a.AreaName),

                ("isactive", false) => query.OrderByDescending(a => a.IsActive)
                                             .ThenBy(a => a.AreaName),
                ("isactive", true) => query.OrderBy(a => a.IsActive)
                                             .ThenBy(a => a.AreaName),

                ("created", false) => query.OrderBy(a => a.CreatedAt),
                ("created", true) => query.OrderByDescending(a => a.CreatedAt),

                ("updated", false) => query.OrderBy(a => a.UpdatedAt),
                ("updated", true) => query.OrderByDescending(a => a.UpdatedAt),

                // الافتراضي: بالاسم تصاعدي
                _ => query.OrderBy(a => a.AreaName),
            };

            if (pageSize == 0) page = 1;

            // ===== إنشاء نتيجة PagedResult مع حفظ حالة البحث/الترتيب =====
            var result = await PagedResult<Area>.CreateAsync(
                query,
                page,
                pageSize,
                sort,        // عمود الترتيب الحالي
                desc,        // هل الترتيب تنازلي؟
                search,      // نص البحث الحالي
                searchBy     // حقل البحث الحالي
            );

            // تخزين حالة فلتر التاريخ داخل الموديل لعرضها في الشاشة
            result.UseDateRange = useDateRange;
            result.FromDate = fromDate;
            result.ToDate = toDate;

            // ===== إعداد خيارات البحث/الترتيب للـ _IndexFilters =====
            ViewBag.SearchOptions = new[]
            {
                new SelectListItem("اسم المنطقة",  "name", searchBy == "name"),
                new SelectListItem("المعرّف",      "id",   searchBy == "id"),
                new SelectListItem("الحي/المركز",  "dist", searchBy == "dist"),
                new SelectListItem("المحافظة",     "gov",  searchBy == "gov"),
            };

            ViewBag.SortOptions = new[]
            {
                new SelectListItem("المعرّف",        "id",       sort == "id"),
                new SelectListItem("اسم المنطقة",    "name",     sort == "name"),
                new SelectListItem("الحي/المركز",    "dist",     sort == "dist"),
                new SelectListItem("المحافظة",       "gov",      sort == "gov"),
                new SelectListItem("الحالة",         "isactive", sort == "isactive"),
                new SelectListItem("تاريخ الإنشاء",  "created",  sort == "created"),
                new SelectListItem("آخر تعديل",      "updated",  sort == "updated"),
            };

            // ===== تحميل القوائم المنسدلة (المحافظة + الحي/المركز) =====
            await LoadLookupsAsync(governorateId, districtId);

            // تمرير قيم الفلاتر للـ View (تُستخدم في _IndexFilters وفلتر المحافظة)
            ViewBag.Search = search;
            ViewBag.SearchBy = searchBy;
            ViewBag.SearchMode = searchMode;
            ViewBag.Sort = sort;
            ViewBag.Dir = dir;
            ViewBag.GovFilter = governorateId;
            ViewBag.DistrictFilter = districtId;
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_IdExpr = filterCol_idExpr;
            ViewBag.FilterCol_Name = filterCol_name;
            ViewBag.FilterCol_Dist = filterCol_dist;
            ViewBag.FilterCol_Gov = filterCol_gov;
            ViewBag.FilterCol_IsActive = filterCol_isactive;
            ViewBag.FilterCol_Created = filterCol_created;
            ViewBag.FilterCol_Updated = filterCol_updated;

            return View(result);
        }

        // =============================== تفاصيل/إضافة/تعديل/حذف ===============================

        /// <summary>
        /// عرض تفاصيل منطقة واحدة.
        /// </summary>
        [RequirePermission("Areas.Index")]
        public async Task<IActionResult> Details(int id)
        {
            var item = await _db.Areas
                                .Include(a => a.Governorate)
                                .Include(a => a.District)!.ThenInclude(d => d.Governorate)
                                .AsNoTracking()
                                .FirstOrDefaultAsync(a => a.AreaId == id);

            if (item == null)
                return NotFound();

            return View(item);
        }

        /// <summary>
        /// GET: عرض فورم إضافة منطقة جديدة.
        /// </summary>
        [RequirePermission("Areas.Create")]
        public async Task<IActionResult> Create()
        {
            // تحميل القوائم المنسدلة للمحافظة والحي/المركز
            await LoadLookupsAsync();

            // إنشاء موديل جديد مع جعل المنطقة نشطة افتراضياً
            var model = new Area
            {
                IsActive = true,
                CreatedAt = DateTime.Now
            };

            return View(model);
        }

        /// <summary>
        /// POST: استلام بيانات المنطقة الجديدة وحفظها في قاعدة البيانات.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Areas.Create")]
        public async Task<IActionResult> Create(Area area)
        {
            // تحقق منطقي: إذا وُجد حي/مركز يجب أن يتبع المحافظة المختارة
            if (area.DistrictId.HasValue && !await DistrictMatchesGovernorateAsync(area.DistrictId, area.GovernorateId))
                ModelState.AddModelError("DistrictId", "الحي/المركز المختار لا يتبع المحافظة المحددة.");

            if (!ModelState.IsValid)
            {
                await LoadLookupsAsync(area.GovernorateId, area.DistrictId);
                return View(area);
            }

            area.CreatedAt = DateTime.Now;
            area.UpdatedAt = DateTime.Now;

            _db.Areas.Add(area);
            await _db.SaveChangesAsync();
            _lookupCache.ClearAllGeographyCaches();

            await _activityLogger.LogAsync(UserActionType.Create, "Area", area.AreaId, $"إنشاء منطقة: {area.AreaName}");

            TempData["Ok"] = "تمت إضافة المنطقة بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// GET: عرض فورم تعديل منطقة موجودة.
        /// </summary>
        [RequirePermission("Areas.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            var item = await _db.Areas.FindAsync(id);
            if (item == null)
                return NotFound();

            await LoadLookupsAsync(item.GovernorateId, item.DistrictId);
            return View(item);
        }

        /// <summary>
        /// POST: استلام بيانات تعديل المنطقة وحفظها.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Areas.Edit")]
        public async Task<IActionResult> Edit(int id, Area area)
        {
            if (id != area.AreaId)
                return BadRequest();

            // تحقق منطقي: إذا وُجد حي/مركز يجب أن يتبع المحافظة المختارة
            if (area.DistrictId.HasValue && !await DistrictMatchesGovernorateAsync(area.DistrictId, area.GovernorateId))
                ModelState.AddModelError("DistrictId", "الحي/المركز المختار لا يتبع المحافظة المحددة.");

            if (!ModelState.IsValid)
            {
                await LoadLookupsAsync(area.GovernorateId, area.DistrictId);
                return View(area);
            }

            var dbItem = await _db.Areas.FindAsync(id);
            if (dbItem == null)
                return NotFound();

            var oldValues = System.Text.Json.JsonSerializer.Serialize(new { dbItem.AreaName, dbItem.GovernorateId, dbItem.DistrictId, dbItem.IsActive });
            dbItem.AreaName = area.AreaName;
            dbItem.GovernorateId = area.GovernorateId;
            dbItem.DistrictId = area.DistrictId;
            dbItem.IsActive = area.IsActive;
            dbItem.Notes = area.Notes;
            dbItem.UpdatedAt = DateTime.Now;

            await _db.SaveChangesAsync();
            _lookupCache.ClearAllGeographyCaches();

            var newValues = System.Text.Json.JsonSerializer.Serialize(new { dbItem.AreaName, dbItem.GovernorateId, dbItem.DistrictId, dbItem.IsActive });
            await _activityLogger.LogAsync(UserActionType.Edit, "Area", id, $"تعديل منطقة: {dbItem.AreaName}", oldValues, newValues);

            TempData["Ok"] = "تم تعديل المنطقة بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// GET: صفحة تأكيد حذف منطقة.
        /// </summary>
        [RequirePermission("Areas.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var item = await _db.Areas
                                .Include(a => a.Governorate)
                                .Include(a => a.District)
                                .AsNoTracking()
                                .FirstOrDefaultAsync(a => a.AreaId == id);

            if (item == null)
                return NotFound();

            return View(item);
        }

        /// <summary>
        /// POST: تنفيذ الحذف بعد التأكيد.
        /// </summary>
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [RequirePermission("Areas.Delete")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var item = await _db.Areas.FindAsync(id);
            if (item == null)
                return NotFound();

            var oldValues = System.Text.Json.JsonSerializer.Serialize(new { item.AreaName, item.GovernorateId, item.DistrictId });
            _db.Areas.Remove(item);
            await _db.SaveChangesAsync();
            _lookupCache.ClearAllGeographyCaches();

            await _activityLogger.LogAsync(UserActionType.Delete, "Area", id, $"حذف منطقة: {item.AreaName}", oldValues: oldValues);

            TempData["Ok"] = "تم حذف المنطقة بنجاح.";
            return RedirectToAction(nameof(Index));
        }



        // ====================== حذف مجموعة من المناطق (حذف المحدد) ======================
        [HttpPost]
        [ValidateAntiForgeryToken]          // حماية من طلبات مزيفة
        [RequirePermission("Areas.Delete")]
        public async Task<IActionResult> BulkDelete(string? selectedIds)
        {
            // selectedIds = "1,2,3" جاية من الهيدن في الفورم
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                // لو مفيش ولا رقم، نرجّع للشاشة بدون ما نعمل حاجة
                TempData["Error"] = "لم يتم اختيار أي منطقة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            // تحويل النص لقائمة أرقام صحيحة
            var ids = selectedIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)    // نفصل بالأComma
                .Select(x => int.TryParse(x, out var id) ? id : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();

            if (ids.Count == 0)
            {
                TempData["Error"] = "لم يتم اختيار أي منطقة صالحة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            // نحضر المناطق المطابقة للأرقام
            var areas = await _db.Areas
                .Where(a => ids.Contains(a.AreaId))
                .ToListAsync();

            if (areas.Count == 0)
            {
                TempData["Error"] = "لا توجد مناطق مطابقة للأرقام المحددة.";
                return RedirectToAction(nameof(Index));
            }

            _db.Areas.RemoveRange(areas);   // حذف جماعي
            await _db.SaveChangesAsync();   // حفظ التغييرات في الداتا بيز
            _lookupCache.ClearAllGeographyCaches();

            TempData["Success"] = $"تم حذف {areas.Count} منطقة/مناطق بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // ====================== حذف جميع المناطق ======================
        [HttpPost]
        [ValidateAntiForgeryToken]          // حماية من طلبات مزيفة
        [RequirePermission("Areas.Delete")]
        public async Task<IActionResult> DeleteAll()
        {
            // نحضر كل السجلات (ممكن تحط شرط لو حابب بعدين)
            var allAreas = await _db.Areas.ToListAsync();

            if (allAreas.Count == 0)
            {
                TempData["Error"] = "لا توجد مناطق لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            _db.Areas.RemoveRange(allAreas);   // حذف كل المناطق
            await _db.SaveChangesAsync();       // حفظ التغييرات
            _lookupCache.ClearAllGeographyCaches();

            TempData["Success"] = "تم حذف جميع المناطق بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // ====================== تصدير المناطق (Excel / CSV) ======================
        [RequirePermission("Areas.Export")]
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy,
            string? searchMode,
            string? sort,
            string? dir,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? governorateId = null,
            int? districtId = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_name = null,
            string? filterCol_dist = null,
            string? filterCol_gov = null,
            string? filterCol_isactive = null,
            string? filterCol_created = null,
            string? filterCol_updated = null,
            string format = "excel"
        )
        {
            // بداية الاستعلام: جدول المناطق + ربط المحافظة والحي/المركز
            var query = _db.Areas
                .Include(a => a.District)
                .Include(a => a.Governorate)
                .AsNoTracking()
                .AsQueryable();

            // ---------- نفس فلاتر الإندكس تقريباً ----------

            // فلتر البحث العام
            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();
                var sb = string.IsNullOrWhiteSpace(searchBy) ? "name" : searchBy.ToLowerInvariant();
                if (sb == "district") sb = "dist";
                searchMode = string.IsNullOrWhiteSpace(searchMode) ? "contains" : searchMode.Trim().ToLowerInvariant();
                var isStarts = searchMode == "starts";
                var isEnds = searchMode == "ends";

                switch (sb)
                {
                    case "id":
                        if (int.TryParse(search, out var idVal))
                            query = query.Where(a => a.AreaId == idVal);
                        else
                            query = query.Where(a => false);
                        break;

                    case "dist":
                        if (isStarts)
                            query = query.Where(a => a.District != null && a.District.DistrictName.StartsWith(search));
                        else if (isEnds)
                            query = query.Where(a => a.District != null && a.District.DistrictName.EndsWith(search));
                        else
                            query = query.Where(a => a.District != null && a.District.DistrictName.Contains(search));
                        break;

                    case "gov":
                        if (isStarts)
                            query = query.Where(a => a.Governorate != null && a.Governorate.GovernorateName.StartsWith(search));
                        else if (isEnds)
                            query = query.Where(a => a.Governorate != null && a.Governorate.GovernorateName.EndsWith(search));
                        else
                            query = query.Where(a => a.Governorate != null && a.Governorate.GovernorateName.Contains(search));
                        break;

                    case "name":
                    default:
                        if (isStarts)
                            query = query.Where(a => a.AreaName.StartsWith(search));
                        else if (isEnds)
                            query = query.Where(a => a.AreaName.EndsWith(search));
                        else
                            query = query.Where(a => a.AreaName.Contains(search));
                        break;
                }
            }

            // فلتر المحافظة
            if (governorateId.HasValue && governorateId.Value > 0)
            {
                query = query.Where(a => a.GovernorateId == governorateId.Value);
            }

            // فلتر الحي/المركز
            if (districtId.HasValue && districtId.Value > 0)
            {
                query = query.Where(a => a.DistrictId == districtId.Value);
            }

            // فلتر كود من/إلى
            if (fromCode.HasValue)
            {
                query = query.Where(a => a.AreaId >= fromCode.Value);
            }
            if (toCode.HasValue)
            {
                query = query.Where(a => a.AreaId <= toCode.Value);
            }

            if (useDateRange)
            {
                if (fromDate.HasValue)
                    query = query.Where(a => a.CreatedAt.HasValue && a.CreatedAt.Value >= fromDate.Value);
                if (toDate.HasValue)
                    query = query.Where(a => a.CreatedAt.HasValue && a.CreatedAt.Value <= toDate.Value);
            }

            query = ApplyColumnFilters(
                query,
                filterCol_id,
                filterCol_idExpr,
                filterCol_name,
                filterCol_dist,
                filterCol_gov,
                filterCol_isactive,
                filterCol_created,
                filterCol_updated);

            // ---------- الترتيب (نفس الإندكس) ----------
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            query = (sort ?? "").ToLower() switch
            {
                "id" => desc ? query.OrderByDescending(a => a.AreaId) : query.OrderBy(a => a.AreaId),
                "dist" => desc ? query.OrderByDescending(a => a.District!.DistrictName)
                                   : query.OrderBy(a => a.District!.DistrictName),
                "gov" => desc ? query.OrderByDescending(a => a.Governorate!.GovernorateName)
                                      : query.OrderBy(a => a.Governorate!.GovernorateName),
                "isactive" => desc ? query.OrderByDescending(a => a.IsActive) : query.OrderBy(a => a.IsActive),
                "created" => desc ? query.OrderByDescending(a => a.CreatedAt) : query.OrderBy(a => a.CreatedAt),
                "updated" => desc ? query.OrderByDescending(a => a.UpdatedAt) : query.OrderBy(a => a.UpdatedAt),
                "name" or _ => desc ? query.OrderByDescending(a => a.AreaName) : query.OrderBy(a => a.AreaName),
            };

            // نحضر النتيجة كاملة بدون تقسيم صفحات
            var data = await query.ToListAsync();
            var fmt = (format ?? "excel").Trim().ToLowerInvariant();

            // ---------- CSV ----------
            if (string.Equals(fmt, "csv", StringComparison.OrdinalIgnoreCase))
            {
                static string CsvEscape(string? value)
                {
                    if (string.IsNullOrEmpty(value)) return "";
                    return "\"" + value.Replace("\"", "\"\"") + "\"";
                }

                var lines = new List<string> { "كود المنطقة,اسم المنطقة,الحي/المركز,المحافظة,الحالة,تاريخ الإنشاء,آخر تعديل" };
                foreach (var a in data)
                {
                    lines.Add(string.Join(",",
                        a.AreaId.ToString(),
                        CsvEscape(a.AreaName),
                        CsvEscape(a.District?.DistrictName),
                        CsvEscape(a.Governorate?.GovernorateName),
                        a.IsActive ? "نشط" : "موقوف",
                        a.CreatedAt?.ToString("yyyy-MM-dd HH:mm") ?? "",
                        a.UpdatedAt?.ToString("yyyy-MM-dd HH:mm") ?? ""
                    ));
                }

                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(string.Join("\r\n", lines));
                return File(bytes, "text/csv; charset=utf-8", ExcelExportNaming.ArabicTimestampedFileName("المناطق", ".csv"));
            }

            // ---------- Excel (.xlsx) ----------
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("المناطق"));

            ws.Cell(1, 1).Value = "كود المنطقة";
            ws.Cell(1, 2).Value = "اسم المنطقة";
            ws.Cell(1, 3).Value = "الحي/المركز";
            ws.Cell(1, 4).Value = "المحافظة";
            ws.Cell(1, 5).Value = "الحالة";
            ws.Cell(1, 6).Value = "تاريخ الإنشاء";
            ws.Cell(1, 7).Value = "آخر تعديل";

            var header = ws.Range(1, 1, 1, 7);
            header.Style.Font.Bold = true;
            header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int row = 2;
            foreach (var a in data)
            {
                ws.Cell(row, 1).Value = a.AreaId;
                ws.Cell(row, 2).Value = a.AreaName ?? "";
                ws.Cell(row, 3).Value = a.District?.DistrictName ?? "";
                ws.Cell(row, 4).Value = a.Governorate?.GovernorateName ?? "";
                ws.Cell(row, 5).Value = a.IsActive ? "نشط" : "موقوف";
                ws.Cell(row, 6).Value = a.CreatedAt?.ToString("yyyy-MM-dd HH:mm") ?? "";
                ws.Cell(row, 7).Value = a.UpdatedAt?.ToString("yyyy-MM-dd HH:mm") ?? "";
                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            stream.Position = 0;
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ExcelExportNaming.ArabicTimestampedFileName("المناطق", ".xlsx"));
        }




        // =============================== دوال مساعدة ===============================

        /// <summary>
        /// تحميل القوائم المنسدلة للمحافظات والأحياء/المراكز.
        /// تُستخدم في Index + Create + Edit.
        /// </summary>
        private async Task LoadLookupsAsync(int? selectedGovId = null, int? selectedDistrictId = null)
        {
            ViewBag.GovernorateId = new SelectList(
                await _lookupCache.GetGovernoratesAsync(),
                "GovernorateId",
                "GovernorateName",
                selectedGovId);

            var districtList = (await _lookupCache.GetDistrictsAsync())
                .OrderBy(d => d.DistrictName);

            if (selectedGovId.HasValue)
                districtList = districtList
                    .Where(d => d.GovernorateId == selectedGovId.Value)
                    .OrderBy(d => d.DistrictName);
            var districtItems = new List<SelectListItem>
            {
                new SelectListItem { Value = "", Text = "— لا يوجد حي/مركز —", Selected = !selectedDistrictId.HasValue }
            };
            foreach (var d in districtList)
                districtItems.Add(new SelectListItem { Value = d.DistrictId.ToString(), Text = d.DistrictName ?? "", Selected = selectedDistrictId == d.DistrictId });
            ViewBag.DistrictId = new SelectList(districtItems, "Value", "Text", selectedDistrictId.HasValue ? selectedDistrictId.Value.ToString() : "");
        }

        /// <summary>
        /// فحص اتساق: هل الحي المختار يتبع نفس المحافظة؟ (إذا لم يُختر حي تُرجَع true)
        /// </summary>
        private async Task<bool> DistrictMatchesGovernorateAsync(int? districtId, int governorateId)
        {
            if (!districtId.HasValue || districtId.Value == 0) return true;
            var distGovId = await _db.Districts
                                     .Where(d => d.DistrictId == districtId.Value)
                                     .Select(d => d.GovernorateId)
                                     .FirstOrDefaultAsync();
            return distGovId == governorateId;
        }
    }
}
