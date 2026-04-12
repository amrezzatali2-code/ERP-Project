using Azure.Core;
using ClosedXML.Excel;                            // مكتبة Excel
using DocumentFormat.OpenXml.Wordprocessing;
using ERP.Data;                                   // AppDbContext
using ERP.Filters;
using ERP.Infrastructure;                         // PagedResult + ApplySearchSort + UserActivityLogger
using ERP.Models;                                 // Governorate, UserActionType
using ERP.Security;
using ERP.Services.Caching;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;                 // القوائم List
using System.Globalization;
using System.IO;                                  // MemoryStream للتصدير
using System.Linq;
using System.Linq.Expressions;                    // Expression<Func<>>
using System.Text;                                // StringBuilder + Encoding
using System.Threading.Tasks;

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر المحافظات:
    /// - قائمة المحافظات بنظام القوائم الموحد + فلتر تاريخ + فلتر كود من/إلى
    /// - اختيار أعمدة + حذف محدد/حذف الكل + تصدير Excel/CSV + طباعة
    /// - CRUD عادي (إضافة/تعديل/حذف/تفاصيل)
    /// </summary>
    public class GovernoratesController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IUserActivityLogger _activityLogger;
        private readonly ILookupCacheService _lookupCache;

        public GovernoratesController(
            AppDbContext ctx,
            IUserActivityLogger activityLogger,
            ILookupCacheService lookupCache)
        {
            _db = ctx;
            _activityLogger = activityLogger;
            _lookupCache = lookupCache;
        }

        // =========================================================================
        // دالة موحّدة: SearchSortFilter — مثل سجل الحركات (بحث واحد + فلتر كود + فلتر تاريخ)
        // =========================================================================
        private static readonly Dictionary<string, Expression<Func<Governorate, string?>>> GovStringFields
            = new(StringComparer.OrdinalIgnoreCase) { ["name"] = g => g.GovernorateName };

        private static readonly Dictionary<string, Expression<Func<Governorate, int>>> GovIntFields
            = new(StringComparer.OrdinalIgnoreCase) { ["id"] = g => g.GovernorateId };

        private static readonly Dictionary<string, Expression<Func<Governorate, object>>> GovOrderFields
            = new(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = g => g.GovernorateName!,
                ["id"] = g => g.GovernorateId,
                ["created"] = g => g.CreatedAt ?? DateTime.MinValue,
                ["updated"] = g => g.UpdatedAt ?? g.CreatedAt ?? DateTime.MinValue
            };

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
            return text;
        }

        private static IQueryable<Governorate> ApplyIntExpr(IQueryable<Governorate> q, string expr, Expression<Func<Governorate, int>> selector)
        {
            if (string.IsNullOrWhiteSpace(expr))
                return q;

            if (expr.StartsWith("<=") && int.TryParse(expr[2..], NumberStyles.Any, CultureInfo.InvariantCulture, out var le))
                return q.Where(Expression.Lambda<Func<Governorate, bool>>(Expression.LessThanOrEqual(selector.Body, Expression.Constant(le)), selector.Parameters));
            if (expr.StartsWith(">=") && int.TryParse(expr[2..], NumberStyles.Any, CultureInfo.InvariantCulture, out var ge))
                return q.Where(Expression.Lambda<Func<Governorate, bool>>(Expression.GreaterThanOrEqual(selector.Body, Expression.Constant(ge)), selector.Parameters));
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && int.TryParse(expr[1..], NumberStyles.Any, CultureInfo.InvariantCulture, out var lt))
                return q.Where(Expression.Lambda<Func<Governorate, bool>>(Expression.LessThan(selector.Body, Expression.Constant(lt)), selector.Parameters));
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && int.TryParse(expr[1..], NumberStyles.Any, CultureInfo.InvariantCulture, out var gt))
                return q.Where(Expression.Lambda<Func<Governorate, bool>>(Expression.GreaterThan(selector.Body, Expression.Constant(gt)), selector.Parameters));

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
                    return q.Where(Expression.Lambda<Func<Governorate, bool>>(Expression.AndAlso(geBody, leBody), selector.Parameters));
                }
            }

            if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var eq))
                return q.Where(Expression.Lambda<Func<Governorate, bool>>(Expression.Equal(selector.Body, Expression.Constant(eq)), selector.Parameters));

            return q;
        }

        private static IQueryable<Governorate> ApplyColumnFilters(
            IQueryable<Governorate> query,
            string? filterCol_id,
            string? filterCol_idExpr,
            string? filterCol_name,
            string? filterCol_created,
            string? filterCol_updated)
        {
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) query = query.Where(g => ids.Contains(g.GovernorateId));
            }
            else if (!string.IsNullOrWhiteSpace(filterCol_idExpr))
            {
                query = ApplyIntExpr(query, NormalizeNumericExpr(filterCol_idExpr), g => g.GovernorateId);
            }
            if (!string.IsNullOrWhiteSpace(filterCol_name))
            {
                var vals = filterCol_name.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) query = query.Where(g => g.GovernorateName != null && vals.Contains(g.GovernorateName));
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
                        if (DateTime.TryParse(p, out var d))
                            mins.Add(new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0));
                    }

                    if (mins.Count > 0)
                    {
                        // مطابقة بالدقيقة (yyyy-MM-dd HH:mm) بدل المطابقة الكاملة للثواني
                        var param = Expression.Parameter(typeof(Governorate), "g");
                        var createdProp = Expression.Property(param, nameof(Governorate.CreatedAt));
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

                        var pred = Expression.Lambda<Func<Governorate, bool>>(body, param);
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
                        if (DateTime.TryParse(p, out var d))
                            mins.Add(new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0));
                    }

                    if (mins.Count > 0)
                    {
                        var param = Expression.Parameter(typeof(Governorate), "g");
                        var updatedProp = Expression.Property(param, nameof(Governorate.UpdatedAt));
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

                        var pred = Expression.Lambda<Func<Governorate, bool>>(body, param);
                        query = query.Where(pred);
                    }
                }
            }
            return query;
        }

        private IQueryable<Governorate> SearchSortFilter(
            string? search,
            string? searchBy,
            string? searchMode,
            string? sort,
            string? dir,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            string? dateField,
            int? codeFrom,
            int? codeTo,
            string? filterCol_id,
            string? filterCol_idExpr,
            string? filterCol_name,
            string? filterCol_created,
            string? filterCol_updated)
        {
            var q = _db.Governorates.AsNoTracking().AsQueryable();

            // فلتر التاريخ
            if (useDateRange || fromDate.HasValue || toDate.HasValue)
            {
                bool filterOnUpdated = string.Equals(dateField, "updated", StringComparison.OrdinalIgnoreCase);
                if (filterOnUpdated)
                {
                    if (fromDate.HasValue)
                        q = q.Where(g => g.UpdatedAt.HasValue && g.UpdatedAt.Value >= fromDate.Value);
                    if (toDate.HasValue)
                        q = q.Where(g => g.UpdatedAt.HasValue && g.UpdatedAt.Value <= toDate.Value);
                }
                else
                {
                    if (fromDate.HasValue)
                        q = q.Where(g => g.CreatedAt >= fromDate.Value);
                    if (toDate.HasValue)
                        q = q.Where(g => g.CreatedAt <= toDate.Value);
                }
            }

            // فلتر كود من/إلى
            if (codeFrom.HasValue)
                q = q.Where(g => g.GovernorateId >= codeFrom.Value);
            if (codeTo.HasValue)
                q = q.Where(g => g.GovernorateId <= codeTo.Value);

            q = ApplyColumnFilters(q, filterCol_id, filterCol_idExpr, filterCol_name, filterCol_created, filterCol_updated);

            // بحث + ترتيب (ApplySearchSort)
            q = q.ApplySearchSort(
                search, searchBy,
                sort, dir,
                GovStringFields, GovIntFields, GovOrderFields,
                defaultSearchBy: "all",
                defaultSortBy: "name",
                searchMode: searchMode
            );

            return q;
        }






        // ===========================
        // قائمة المحافظات (Index) — نظام بحث مثل سجل الحركات
        // ===========================
        [RequirePermission("Governorates.Index")]
        [HttpGet]
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",
            string? searchMode = "contains",
            string? sort = "name",
            string? dir = "asc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? dateField = "created",
            int? codeFrom = null,
            int? codeTo = null,
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_name = null,
            string? filterCol_created = null,
            string? filterCol_updated = null,
            int page = 1,
            int pageSize = 10)
        {
            // تجنّب أخذ قيمة قديمة عند تكرار المعامل في الـ Query (مثل pageSize من فورم آخر)
            var pageSizeQuery = Request.Query["pageSize"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageSizeQuery) && int.TryParse(pageSizeQuery, out var psVal))
                pageSize = psVal;
            if (Request.Query.ContainsKey("search"))
                search = Request.Query["search"].LastOrDefault();
            if (Request.Query.ContainsKey("searchBy"))
                searchBy = Request.Query["searchBy"].LastOrDefault();
            if (Request.Query.ContainsKey("searchMode"))
                searchMode = Request.Query["searchMode"].LastOrDefault();

            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm != "starts" && sm != "ends") sm = "contains";

            var q = SearchSortFilter(
                search, searchBy, sm,
                sort, dir,
                useDateRange, fromDate, toDate, dateField ?? "created",
                codeFrom, codeTo,
                filterCol_id, filterCol_idExpr, filterCol_name, filterCol_created, filterCol_updated
            );

            if (page < 1) page = 1;
            if (pageSize < 0) pageSize = 10;
            if (pageSize > 0 && pageSize != 10 && pageSize != 25 && pageSize != 50 && pageSize != 100 && pageSize != 200)
                pageSize = 10;

            bool descending = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            var model = await PagedResult<Governorate>.CreateAsync(q, page, pageSize, sort, descending, search, searchBy);

            bool dateFilterActive = useDateRange || fromDate.HasValue || toDate.HasValue;
            model.UseDateRange = dateFilterActive;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            ViewBag.Search = search;
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.SearchMode = sm;
            ViewBag.Sort = sort;
            ViewBag.Dir = dir;
            ViewBag.DateField = dateField ?? "created";
            ViewBag.CodeFrom = codeFrom;
            ViewBag.CodeTo = codeTo;
            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_IdExpr = filterCol_idExpr;
            ViewBag.FilterCol_Name = filterCol_name;
            ViewBag.FilterCol_Created = filterCol_created;
            ViewBag.FilterCol_Updated = filterCol_updated;

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var valuesSearch = Request.Query["valuesSearch"].LastOrDefault();
            var searchTerm = (valuesSearch ?? search ?? "").Trim().ToLowerInvariant();
            var columnLower = (column ?? "").Trim().ToLowerInvariant();

            // نطبّق نفس فلاتر الصفحة الحالية على قائمة القيم (حتى القيم تعكس "المفلتر الحالي")
            var listSearch = Request.Query["listSearch"].LastOrDefault();
            var listSearchBy = Request.Query["listSearchBy"].LastOrDefault();
            var listSearchMode = Request.Query["listSearchMode"].LastOrDefault();
            var sort = Request.Query["sort"].LastOrDefault();
            var dir = Request.Query["dir"].LastOrDefault();
            var dateField = Request.Query["dateField"].LastOrDefault() ?? "created";

            bool useDateRange = string.Equals(Request.Query["useDateRange"].LastOrDefault(), "true", StringComparison.OrdinalIgnoreCase);
            DateTime? fromDate = null;
            DateTime? toDate = null;
            if (DateTime.TryParse(Request.Query["fromDate"].LastOrDefault(), out var fd)) fromDate = fd;
            if (DateTime.TryParse(Request.Query["toDate"].LastOrDefault(), out var td)) toDate = td;

            int? codeFrom = null;
            int? codeTo = null;
            if (int.TryParse(Request.Query["codeFrom"].LastOrDefault(), out var cf)) codeFrom = cf;
            if (int.TryParse(Request.Query["codeTo"].LastOrDefault(), out var ct)) codeTo = ct;

            var filterCol_id = Request.Query["filterCol_id"].LastOrDefault();
            var filterCol_idExpr = Request.Query["filterCol_idExpr"].LastOrDefault();
            var filterCol_name = Request.Query["filterCol_name"].LastOrDefault();
            var filterCol_created = Request.Query["filterCol_created"].LastOrDefault();
            var filterCol_updated = Request.Query["filterCol_updated"].LastOrDefault();

            var q = SearchSortFilter(
                listSearch, listSearchBy, (listSearchMode ?? "contains").Trim().ToLowerInvariant(),
                sort ?? "name", dir ?? "asc",
                useDateRange, fromDate, toDate, dateField,
                codeFrom, codeTo,
                filterCol_id, filterCol_idExpr, filterCol_name, filterCol_created, filterCol_updated
            );

            if (columnLower == "id" || columnLower == "governorateid")
            {
                var ids = await q.Select(g => g.GovernorateId).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "name" || columnLower == "governoratename")
            {
                var list = await q.Select(g => g.GovernorateName).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s != null && s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v ?? "", display = v ?? "" }));
            }
            if (columnLower == "created" || columnLower == "createdat")
            {
                var list = await q.Where(g => g.CreatedAt.HasValue).Select(g => g.CreatedAt!.Value).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd HH:mm"), display = d.ToString("yyyy-MM-dd HH:mm") }));
            }
            if (columnLower == "updated" || columnLower == "updatedat")
            {
                var list = await q.Where(g => g.UpdatedAt.HasValue).Select(g => g.UpdatedAt!.Value).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd HH:mm"), display = d.ToString("yyyy-MM-dd HH:mm") }));
            }
            return Json(Array.Empty<object>());
        }






        // =========================================================================
        // Details / Create / Edit / Delete (CRUD الأساسي)
        // =========================================================================

        [RequirePermission("Governorates.Index")]
        public async Task<IActionResult> Details(int id)
        {
            var item = await _db.Governorates
                                .AsNoTracking()
                                .FirstOrDefaultAsync(x => x.GovernorateId == id);

            if (item == null) return NotFound();
            return View(item);
        }

        [RequirePermission("Governorates.Create")]
        public IActionResult Create() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Governorates.Create")]
        public async Task<IActionResult> Create([Bind("GovernorateName")] Governorate vm)
        {
            if (!ModelState.IsValid)
                return View(vm);

            // تسجيل تاريخ الإنشاء (الخادم)
            vm.CreatedAt = DateTime.Now;

            _db.Governorates.Add(vm);
            await _db.SaveChangesAsync();
            _lookupCache.ClearAllGeographyCaches();

            await _activityLogger.LogAsync(UserActionType.Create, "Governorate", vm.GovernorateId, $"إنشاء محافظة: {vm.GovernorateName}");

            TempData["Ok"] = "تم إضافة المحافظة بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        [RequirePermission("Governorates.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            var entity = await _db.Governorates.FindAsync(id);
            if (entity == null) return NotFound();
            return View(entity);
        }






        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Governorates.Edit")]
        public async Task<IActionResult> Edit(int id, [Bind("GovernorateName")] Governorate vm)
        {
            var entity = await _db.Governorates.FindAsync(id);
            if (entity == null) return NotFound();

            if (!ModelState.IsValid)
                return View(entity);

            var oldValues = System.Text.Json.JsonSerializer.Serialize(new { entity.GovernorateName });
            entity.GovernorateName = vm.GovernorateName;
            entity.UpdatedAt = DateTime.Now;   // تسجيل آخر تعديل

            await _db.SaveChangesAsync();
            _lookupCache.ClearAllGeographyCaches();

            var newValues = System.Text.Json.JsonSerializer.Serialize(new { entity.GovernorateName });
            await _activityLogger.LogAsync(UserActionType.Edit, "Governorate", id, $"تعديل محافظة: {entity.GovernorateName}", oldValues, newValues);

            TempData["Ok"] = "تم تعديل بيانات المحافظة.";
            return RedirectToAction(nameof(Index));
        }

        [RequirePermission("Governorates.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var item = await _db.Governorates
                                .AsNoTracking()
                                .FirstOrDefaultAsync(x => x.GovernorateId == id);

            if (item == null) return NotFound();
            return View(item);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [RequirePermission("Governorates.Delete")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var entity = await _db.Governorates.FindAsync(id);
            if (entity != null)
            {
                var oldValues = System.Text.Json.JsonSerializer.Serialize(new { entity.GovernorateName });
                _db.Governorates.Remove(entity);
                await _db.SaveChangesAsync();
                _lookupCache.ClearAllGeographyCaches();

                await _activityLogger.LogAsync(UserActionType.Delete, "Governorate", id, $"حذف محافظة: {entity.GovernorateName}", oldValues: oldValues);

                TempData["Ok"] = "تم حذف المحافظة.";
            }

            return RedirectToAction(nameof(Index));
        }






        // =========================================================================
        // BulkDelete — حذف المحافظات المحددة
        // =========================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Governorates.Delete")]
        public async Task<IActionResult> BulkDelete(string? selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["Err"] = "لم يتم اختيار أي محافظة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var ids = selectedIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var n) ? n : (int?)null)
                .Where(n => n.HasValue)
                .Select(n => n!.Value)
                .ToList();

            if (!ids.Any())
            {
                TempData["Err"] = "قائمة الأكواد غير صحيحة.";
                return RedirectToAction(nameof(Index));
            }

            var items = await _db.Governorates
                                 .Where(g => ids.Contains(g.GovernorateId))
                                 .ToListAsync();

            if (!items.Any())
            {
                TempData["Err"] = "لم يتم العثور على المحافظات المحددة.";
                return RedirectToAction(nameof(Index));
            }

            _db.Governorates.RemoveRange(items);
            await _db.SaveChangesAsync();
            _lookupCache.ClearAllGeographyCaches();

            TempData["Ok"] = $"تم حذف {items.Count} محافظة.";
            return RedirectToAction(nameof(Index));
        }








        // =========================================================================
        // DeleteAll — حذف جميع المحافظات
        // =========================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Governorates.Delete")]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _db.Governorates.ToListAsync();

            if (!all.Any())
            {
                TempData["Ok"] = "لا توجد محافظات لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            _db.Governorates.RemoveRange(all);
            await _db.SaveChangesAsync();
            _lookupCache.ClearAllGeographyCaches();

            TempData["Ok"] = "تم حذف جميع المحافظات.";
            return RedirectToAction(nameof(Index));
        }








        // =========================================================================
        // Export — تصدير كل المحافظات (Excel أو CSV) بدون اعتماد على الفلاتر
        // =========================================================================
        [RequirePermission("Governorates.Export")]
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
            string? dateField = "created",
            int? codeFrom = null,
            int? codeTo = null,
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_name = null,
            string? filterCol_created = null,
            string? filterCol_updated = null,
            string? format = "excel")
        {
            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm != "starts" && sm != "ends") sm = "contains";

            var q = SearchSortFilter(
                search, searchBy, sm,
                sort ?? "name", dir ?? "asc",
                useDateRange, fromDate, toDate, dateField ?? "created",
                codeFrom, codeTo,
                filterCol_id, filterCol_idExpr, filterCol_name, filterCol_created, filterCol_updated
            );
            var rows = await q.ToListAsync();
            var fmt = (format ?? "excel").Trim().ToLowerInvariant();

            // ---------------- CSV ----------------
            if (fmt == "csv")
            {
                var sb = new StringBuilder();

                // عناوين الأعمدة
                sb.AppendLine(string.Join(",",
                    Csv("كود المحافظة"),
                    Csv("اسم المحافظة"),
                    Csv("تاريخ الإنشاء"),
                    Csv("آخر تعديل")
                ));

                // البيانات
                foreach (var g in rows)
                {
                    sb.AppendLine(string.Join(",",
                        Csv(g.GovernorateId.ToString()),
                        Csv(g.GovernorateName),
                        Csv(g.CreatedAt?.ToString("yyyy-MM-dd HH:mm")),
                        Csv(g.UpdatedAt?.ToString("yyyy-MM-dd HH:mm"))
                    ));
                }

                var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
                var bytes = utf8.GetBytes(sb.ToString());
                var name = ExcelExportNaming.ArabicTimestampedFileName("المحافظات", ".csv");
                var ctype = "text/csv; charset=utf-8";

                return File(bytes, ctype, name);
            }

            // ---------------- Excel (.xlsx) ----------------
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("المحافظات"));

            ws.Cell(1, 1).Value = "كود المحافظة";
            ws.Cell(1, 2).Value = "اسم المحافظة";
            ws.Cell(1, 3).Value = "تاريخ الإنشاء";
            ws.Cell(1, 4).Value = "آخر تعديل";

            var header = ws.Range(1, 1, 1, 4);
            header.Style.Font.Bold = true;
            header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int row = 2;
            foreach (var g in rows)
            {
                ws.Cell(row, 1).Value = g.GovernorateId;
                ws.Cell(row, 2).Value = g.GovernorateName ?? "";
                ws.Cell(row, 3).Value = g.CreatedAt.HasValue ? g.CreatedAt.Value.ToString("yyyy-MM-dd HH:mm") : "";
                ws.Cell(row, 4).Value = g.UpdatedAt.HasValue ? g.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm") : "";
                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            stream.Position = 0;
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ExcelExportNaming.ArabicTimestampedFileName("المحافظات", ".xlsx"));
        }

        // دالة مساعدة صغيرة لتجهيز نص الـ CSV
        private static string Csv(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            var s = value.Replace("\"", "\"\""); // هروب علامة "

            if (s.Contains(',') || s.Contains('\n') || s.Contains('\r') || s.Contains('"'))
                return "\"" + s + "\"";

            return s;
        }
    }
}
