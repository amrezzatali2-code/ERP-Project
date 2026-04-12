using Azure.Core;
using ClosedXML.Excel;                            // مكتبة Excel
using DocumentFormat.OpenXml.InkML;
using ERP.Data;                                   // AppDbContext
using ERP.Filters;
using ERP.Infrastructure;                         // PagedResult + ApplySearchSort + UserActivityLogger
using ERP.Models;                                 // Governorate, UserActionType
using ERP.Security;
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
    /// كنترولر الفروع — نفس نمط الحسابات/المناطق:
    /// بحث + ترتيب + ترقيم + فلترة بتاريخ الإنشاء + CRUD كامل.
    /// </summary>
    public class BranchesController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IUserActivityLogger _activityLogger;

        public BranchesController(AppDbContext db, IUserActivityLogger activityLogger)
        {
            _db = db;
            _activityLogger = activityLogger;
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

        private static IQueryable<Branch> ApplyColumnFilters(
            IQueryable<Branch> query,
            string? filterCol_id,
            string? filterCol_idExpr,
            string? filterCol_name,
            string? filterCol_created,
            string? filterCol_updated)
        {
            if (!string.IsNullOrWhiteSpace(filterCol_idExpr))
            {
                query = ApplyIntExpr(query, b => b.BranchId, filterCol_idExpr);
            }
            else if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) query = query.Where(b => ids.Contains(b.BranchId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_name))
            {
                var vals = filterCol_name.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) query = query.Where(b => b.BranchName != null && vals.Contains(b.BranchName));
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
                        var param = Expression.Parameter(typeof(Branch), "b");
                        var createdProp = Expression.Property(param, nameof(Branch.CreatedAt));
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

                        var pred = Expression.Lambda<Func<Branch, bool>>(body, param);
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
                        var param = Expression.Parameter(typeof(Branch), "b");
                        var updatedProp = Expression.Property(param, nameof(Branch.UpdatedAt));
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

                        var pred = Expression.Lambda<Func<Branch, bool>>(body, param);
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
            // نفس فلاتر صفحة الفروع الحالية حتى قائمة القيم تعكس المفلتر
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

            int? codeFrom = null;
            int? codeTo = null;
            if (int.TryParse(Request.Query["fromCode"].LastOrDefault(), out var cf1)) codeFrom = cf1;
            else if (int.TryParse(Request.Query["codeFrom"].LastOrDefault(), out var cf2)) codeFrom = cf2;
            if (int.TryParse(Request.Query["toCode"].LastOrDefault(), out var ct1)) codeTo = ct1;
            else if (int.TryParse(Request.Query["codeTo"].LastOrDefault(), out var ct2)) codeTo = ct2;

            var filterCol_id = Request.Query["filterCol_id"].LastOrDefault();
            var filterCol_idExpr = Request.Query["filterCol_idExpr"].LastOrDefault();
            var filterCol_name = Request.Query["filterCol_name"].LastOrDefault();
            var filterCol_created = Request.Query["filterCol_created"].LastOrDefault();
            var filterCol_updated = Request.Query["filterCol_updated"].LastOrDefault();

            IQueryable<Branch> q = _db.Branches.AsNoTracking();

            // تاريخ من/إلى (CreatedAt/UpdatedAt) — في الفروع نستخدم created فقط في الفلترة العامة
            if (useDateRange || fromDate.HasValue || toDate.HasValue)
            {
                if (fromDate.HasValue) q = q.Where(b => b.CreatedAt.HasValue && b.CreatedAt.Value >= fromDate.Value);
                if (toDate.HasValue) q = q.Where(b => b.CreatedAt.HasValue && b.CreatedAt.Value <= toDate.Value);
            }
            if (codeFrom.HasValue) q = q.Where(b => b.BranchId >= codeFrom.Value);
            if (codeTo.HasValue) q = q.Where(b => b.BranchId <= codeTo.Value);

            // بحث (بنفس منطق Index)
            var s = (listSearch ?? string.Empty).Trim();
            var sb = string.IsNullOrWhiteSpace(listSearchBy) ? "name" : listSearchBy.Trim().ToLowerInvariant();
            var sm = string.IsNullOrWhiteSpace(listSearchMode) ? "contains" : listSearchMode.Trim().ToLowerInvariant();
            var isStarts = sm == "starts";
            var isEnds = sm == "ends";
            if (!isStarts && !isEnds) sm = "contains";

            if (!string.IsNullOrEmpty(s))
            {
                switch (sb)
                {
                    case "id":
                        if (int.TryParse(s, out int idValue))
                            q = q.Where(b => b.BranchId == idValue);
                        else
                            q = q.Where(b => 1 == 0);
                        break;

                    case "all":
                        if (isStarts)
                            q = q.Where(b => b.BranchName.StartsWith(s) || b.BranchId.ToString().StartsWith(s));
                        else if (isEnds)
                            q = q.Where(b => b.BranchName.EndsWith(s) || b.BranchId.ToString().EndsWith(s));
                        else
                            q = q.Where(b => b.BranchName.Contains(s) || b.BranchId.ToString().Contains(s));
                        break;

                    case "name":
                    default:
                        if (isStarts)
                            q = q.Where(b => b.BranchName.StartsWith(s));
                        else if (isEnds)
                            q = q.Where(b => b.BranchName.EndsWith(s));
                        else
                            q = q.Where(b => b.BranchName.Contains(s));
                        break;
                }
            }
            q = ApplyColumnFilters(q, filterCol_id, filterCol_idExpr, filterCol_name, filterCol_created, filterCol_updated);

            if (columnLower == "id" || columnLower == "branchid")
            {
                var ids = await q.Select(b => b.BranchId).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "name" || columnLower == "branchname")
            {
                var list = await q.Select(b => b.BranchName).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s != null && s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v ?? "", display = v ?? "" }));
            }
            if (columnLower == "created" || columnLower == "createdat")
            {
                var list = await q.Where(b => b.CreatedAt.HasValue).Select(b => b.CreatedAt!.Value).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd HH:mm"), display = d.ToString("yyyy-MM-dd HH:mm") }));
            }
            if (columnLower == "updated" || columnLower == "updatedat")
            {
                var list = await q.Where(b => b.UpdatedAt.HasValue).Select(b => b.UpdatedAt!.Value).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd HH:mm"), display = d.ToString("yyyy-MM-dd HH:mm") }));
            }
            return Json(Array.Empty<object>());
        }

        // دالة خاصة لتجهيز كويري الفروع مع تطبيق الفلاتر
        private IQueryable<Branch> FilterBranches(
            string? search,
            string? searchBy,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate)
        {
            // كويري الأساس من جدول الفروع
            var query = _db.Branches.AsQueryable();

            // فلتر البحث (بالكود أو بالاسم)
            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();

                switch (searchBy)
                {
                    case "id":   // بحث بالكود
                        if (int.TryParse(search, out var idVal))
                        {
                            query = query.Where(b => b.BranchId == idVal);
                        }
                        break;

                    default:     // بحث بالاسم (الافتراضي)
                        query = query.Where(b => b.BranchName.Contains(search));
                        break;
                }
            }

            // فلتر الفترة الزمنية (تاريخ الإنشاء)
            if (useDateRange)
            {
                if (fromDate.HasValue)
                {
                    query = query.Where(b => b.CreatedAt >= fromDate);
                }

                if (toDate.HasValue)
                {
                    query = query.Where(b => b.CreatedAt <= toDate);
                }
            }

            return query;
        }





        // =========================================================
        // GET: Branches
        // قائمة الفروع مع:
        //  - بحث (search + searchBy)
        //  - ترتيب (sort + dir)
        //  - فلترة بتاريخ الإنشاء (useDateRange + fromDate/toDate)
        //  - تقسيم صفحات (page + pageSize)
        // =========================================================
        [RequirePermission("Branches.Index")]
        [HttpGet]
        public async Task<IActionResult> Index(
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
            string? filterCol_idExpr = null,
            string? filterCol_name = null,
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
            search = (search ?? string.Empty).Trim();          // نص البحث
            searchBy = string.IsNullOrWhiteSpace(searchBy) ? "name" : searchBy.ToLowerInvariant();
            searchMode = string.IsNullOrWhiteSpace(searchMode) ? "contains" : searchMode.ToLowerInvariant();
            sort = string.IsNullOrWhiteSpace(sort) ? "name" : sort.ToLowerInvariant();
            dir = string.IsNullOrWhiteSpace(dir) ? "asc" : dir.ToLowerInvariant();

            bool desc = dir == "desc";   // متغير: هل الترتيب تنازلي؟

            // ===== الاستعلام الأساسي من جدول الفروع =====
            IQueryable<Branch> query = _db.Branches
                                          .AsNoTracking()
                                          .AsQueryable();

            // ===== تطبيق البحث =====
            if (!string.IsNullOrEmpty(search))
            {
                var isStarts = searchMode == "starts";
                var isEnds = searchMode == "ends";

                switch (searchBy)
                {
                    case "id":        // البحث برقم الفرع فقط
                        if (int.TryParse(search, out int idValue))
                        {
                            query = query.Where(b => b.BranchId == idValue);
                        }
                        else
                        {
                            // لو كتب نص مش رقم في خانة "الرقم" ⇒ لا نتائج
                            query = query.Where(b => 1 == 0);
                        }
                        break;

                    case "all":       // البحث في الاسم + الكود معًا
                        if (isStarts)
                            query = query.Where(b => b.BranchName.StartsWith(search) || b.BranchId.ToString().StartsWith(search));
                        else if (isEnds)
                            query = query.Where(b => b.BranchName.EndsWith(search) || b.BranchId.ToString().EndsWith(search));
                        else
                            query = query.Where(b => b.BranchName.Contains(search) || b.BranchId.ToString().Contains(search));
                        break;

                    case "name":
                    default:          // البحث بالاسم فقط
                        if (isStarts)
                            query = query.Where(b => b.BranchName.StartsWith(search));
                        else if (isEnds)
                            query = query.Where(b => b.BranchName.EndsWith(search));
                        else
                            query = query.Where(b => b.BranchName.Contains(search));
                        break;
                }
            }

            if (useDateRange)
            {
                if (fromDate.HasValue)
                    query = query.Where(b => b.CreatedAt.HasValue && b.CreatedAt.Value >= fromDate.Value);
                if (toDate.HasValue)
                    query = query.Where(b => b.CreatedAt.HasValue && b.CreatedAt.Value <= toDate.Value);
            }

            if (fromCode.HasValue) query = query.Where(b => b.BranchId >= fromCode.Value);
            if (toCode.HasValue) query = query.Where(b => b.BranchId <= toCode.Value);

            query = ApplyColumnFilters(query, filterCol_id, filterCol_idExpr, filterCol_name, filterCol_created, filterCol_updated);

            // ===== الترتيب =====
            // name   = اسم الفرع
            // id     = كود الفرع
            // created= تاريخ الإنشاء
            // updated= آخر تعديل
            query = (sort, desc) switch
            {
                ("id", false) => query.OrderBy(b => b.BranchId),
                ("id", true) => query.OrderByDescending(b => b.BranchId),

                ("created", false) => query.OrderBy(b => b.CreatedAt ?? DateTime.MinValue)
                                           .ThenBy(b => b.BranchName),
                ("created", true) => query.OrderByDescending(b => b.CreatedAt ?? DateTime.MinValue)
                                           .ThenByDescending(b => b.BranchName),

                ("updated", false) => query.OrderBy(b => b.UpdatedAt ?? DateTime.MinValue)
                                           .ThenBy(b => b.BranchName),
                ("updated", true) => query.OrderByDescending(b => b.UpdatedAt ?? DateTime.MinValue)
                                           .ThenByDescending(b => b.BranchName),

                ("name", false) => query.OrderBy(b => b.BranchName),
                ("name", true) => query.OrderByDescending(b => b.BranchName),

                // الافتراضي: اسم الفرع تصاعدي
                _ => query.OrderBy(b => b.BranchName),
            };

            // ===== إنشاء نتيجة PagedResult مع حفظ حالة البحث/الترتيب =====
            if (pageSize == 0) page = 1;
            var result = await PagedResult<Branch>.CreateAsync(query, page, pageSize, sort, desc, search, searchBy);

            // تخزين حالة فلتر التاريخ في الموديل لعرضها في الواجهة
            result.UseDateRange = useDateRange;
            result.FromDate = fromDate;
            result.ToDate = toDate;

            // ===== تجهيز خيارات شريط البحث/الترتيب (_IndexFilters) =====
            ViewBag.SearchOptions = new[]
            {
                new SelectListItem("اسم الفرع",      "name", searchBy == "name"),
                new SelectListItem("الكود",          "id",   searchBy == "id"),
                new SelectListItem("الاسم + الكود",  "all",  searchBy == "all"),
            };

            ViewBag.SortOptions = new[]
            {
                new SelectListItem("اسم الفرع",     "name",    sort == "name"),
                new SelectListItem("كود الفرع",     "id",      sort == "id"),
                new SelectListItem("تاريخ الإنشاء", "created", sort == "created"),
                new SelectListItem("آخر تعديل",     "updated", sort == "updated"),
            };

            // تمرير قيم الفلاتر للـ View علشان البارشال يعرضها
            ViewBag.Search = search;
            ViewBag.SearchBy = searchBy;
            ViewBag.SearchMode = searchMode;
            ViewBag.Sort = sort;
            ViewBag.Dir = dir;
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_IdExpr = filterCol_idExpr;
            ViewBag.FilterCol_Name = filterCol_name;
            ViewBag.FilterCol_Created = filterCol_created;
            ViewBag.FilterCol_Updated = filterCol_updated;

            return View(result);
        }

        // =========================================================
        // GET: Branches/Details/5
        // عرض تفاصيل فرع واحد
        // =========================================================
        [RequirePermission("Branches.Index")]
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var branch = await _db.Branches
                                  .AsNoTracking()
                                  .FirstOrDefaultAsync(b => b.BranchId == id);

            if (branch == null)
                return NotFound();

            return View(branch);
        }

        // =========================================================
        // GET: Branches/Create
        // عرض فورم إضافة فرع جديد
        // =========================================================
        [RequirePermission("Branches.Create")]
        [HttpGet]
        public IActionResult Create()
        {
            return View(new Branch());   // فورم فاضي
        }

        // =========================================================
        // POST: Branches/Create
        // استلام بيانات الفرع الجديد وحفظه
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Branches.Create")]
        public async Task<IActionResult> Create(Branch branch)
        {
            // التحقق من البيانات (الاسم مطلوب)
            if (!ModelState.IsValid)
                return View(branch);

            // تعيين تاريخ الإنشاء والتعديل
            branch.CreatedAt = DateTime.Now;
            branch.UpdatedAt = DateTime.Now;

            _db.Branches.Add(branch);
            await _db.SaveChangesAsync();

            await _activityLogger.LogAsync(UserActionType.Create, "Branch", branch.BranchId, $"إنشاء فرع: {branch.BranchName}");

            TempData["Ok"] = "تمت إضافة الفرع بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // GET: Branches/Edit/5
        // عرض فورم تعديل فرع
        // =========================================================
        [RequirePermission("Branches.Edit")]
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var branch = await _db.Branches.FindAsync(id);
            if (branch == null)
                return NotFound();

            return View(branch);
        }

        // =========================================================
        // POST: Branches/Edit/5
        // استلام التعديلات وحفظها
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Branches.Edit")]
        public async Task<IActionResult> Edit(int id, Branch branch)
        {
            if (id != branch.BranchId)
                return BadRequest();   // حماية من التلاعب في Id

            if (!ModelState.IsValid)
                return View(branch);

            // جلب السجل من قاعدة البيانات ثم تحديث الحقول المسموح بها فقط
            var dbBranch = await _db.Branches.FindAsync(id);
            if (dbBranch == null)
                return NotFound();

            var oldValues = System.Text.Json.JsonSerializer.Serialize(new { dbBranch.BranchName });
            dbBranch.BranchName = branch.BranchName;   // تعديل الاسم فقط
            dbBranch.UpdatedAt = DateTime.Now;        // تحديث تاريخ آخر تعديل

            await _db.SaveChangesAsync();

            var newValues = System.Text.Json.JsonSerializer.Serialize(new { dbBranch.BranchName });
            await _activityLogger.LogAsync(UserActionType.Edit, "Branch", id, $"تعديل فرع: {dbBranch.BranchName}", oldValues, newValues);

            TempData["Ok"] = "تم تعديل بيانات الفرع.";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // GET: Branches/Delete/5
        // عرض صفحة تأكيد حذف فرع
        // =========================================================
        [RequirePermission("Branches.Delete")]
        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            var branch = await _db.Branches
                                  .AsNoTracking()
                                  .FirstOrDefaultAsync(b => b.BranchId == id);

            if (branch == null)
                return NotFound();

            return View(branch);
        }

        // =========================================================
        // POST: Branches/Delete/5
        // تنفيذ الحذف بعد التأكيد
        // =========================================================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [RequirePermission("Branches.Delete")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var branch = await _db.Branches.FindAsync(id);
            if (branch == null)
                return NotFound();

            var oldValues = System.Text.Json.JsonSerializer.Serialize(new { branch.BranchName });
            _db.Branches.Remove(branch);
            await _db.SaveChangesAsync();

            await _activityLogger.LogAsync(UserActionType.Delete, "Branch", id, $"حذف فرع: {branch.BranchName}", oldValues: oldValues);

            TempData["Ok"] = "تم حذف السجل.";
            return RedirectToAction(nameof(Index));
        }




        /// <summary>
        /// حذف جماعي للفروع المختارة من الجدول (حسب التشيك بوكس).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Branches.Delete")]
        public async Task<IActionResult> BulkDelete(
            int[]? selectedIds,          // IDs المختارة من الجدول
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            int page = 1,
            int pageSize = 25)
        {
            // لو المستخدم ما اختار شيئ
            if (selectedIds == null || selectedIds.Length == 0)
            {
                TempData["Error"] = "لم يتم اختيار أي فروع للحذف.";
                return RedirectToAction(nameof(Index), new
                {
                    search,
                    searchBy,
                    sort,
                    dir,
                    page,
                    pageSize,
                    useDateRange,
                    fromDate,
                    toDate
                });
            }

            // قراءة الفروع المطابقة لقائمة الـ IDs
            var branches = await _db.Branches
                .Where(b => selectedIds.Contains(b.BranchId))
                .ToListAsync();

            if (branches.Count == 0)
            {
                TempData["Error"] = "لم يتم العثور على فروع مطابقة للمعرّفات المختارة.";
            }
            else
            {
                _db.Branches.RemoveRange(branches);
                await _db.SaveChangesAsync();

                TempData["Success"] = $"تم حذف {branches.Count} فرع/فروع بنجاح.";
            }

            // الرجوع لنفس الفلاتر والصفحة
            return RedirectToAction(nameof(Index), new
            {
                search,
                searchBy,
                sort,
                dir,
                page,
                pageSize,
                useDateRange,
                fromDate,
                toDate
            });
        }



        /// <summary>
        /// حذف كل الفروع المطابقة للفلاتر الحالية (بحث + فترة زمنية).
        /// لا يعتمد على التشيك بوكس، بل على الفلاتر.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Branches.Delete")]
        public async Task<IActionResult> DeleteAll(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate)
        {
            // تجهيز الكويري مع الفلاتر
            var query = FilterBranches(search, searchBy, useDateRange, fromDate, toDate);

            // عدد الفروع قبل الحذف
            var branches = await query.ToListAsync();
            var count = branches.Count;

            if (count == 0)
            {
                TempData["Info"] = "لا توجد فروع مطابقة للفلاتر الحالية للحذف.";
            }
            else
            {
                _db.Branches.RemoveRange(branches);
                await _db.SaveChangesAsync();

                TempData["Success"] = $"تم حذف {count} فرع/فروع مطابقة للفلاتر الحالية.";
            }

            // نرجع لنفس الفلاتر (صفحة 1 لأن البيانات اتغيّرت)
            return RedirectToAction(nameof(Index), new
            {
                search,
                searchBy,
                sort,
                dir,
                page = 1,
                pageSize = 25,
                useDateRange,
                fromDate,
                toDate
            });
        }




        /// <summary>
        /// تصدير الفروع المطابقة للفلاتر الحالية (Excel أو CSV).
        /// </summary>
        [RequirePermission("Branches.Export")]
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy,
            string? searchMode,
            string? sort,
            string? dir,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_name = null,
            string? filterCol_created = null,
            string? filterCol_updated = null,
            string? format = "excel")
        {
            var query = FilterBranches(search, searchBy, useDateRange, fromDate, toDate);
            query = ApplyColumnFilters(query, filterCol_id, filterCol_idExpr, filterCol_name, filterCol_created, filterCol_updated);

            // تطبيق الترتيب مثل شاشة الـ Index
            query = (sort, dir?.ToLower()) switch
            {
                ("id", "desc") => query.OrderByDescending(b => b.BranchId),
                ("id", _) => query.OrderBy(b => b.BranchId),

                ("created", "desc") => query.OrderByDescending(b => b.CreatedAt),
                ("created", _) => query.OrderBy(b => b.CreatedAt),

                ("updated", "desc") => query.OrderByDescending(b => b.UpdatedAt),
                ("updated", _) => query.OrderBy(b => b.UpdatedAt),

                ("name", "desc") => query.OrderByDescending(b => b.BranchName),
                ("name", _) => query.OrderBy(b => b.BranchName),

                _ => query.OrderBy(b => b.BranchId)
            };

            var list = await query.AsNoTracking().ToListAsync();
            var fmt = (format ?? "excel").Trim().ToLowerInvariant();

            // ---------- CSV ----------
            if (fmt == "csv")
            {
                string Csv(string? value)
                {
                    value ??= "";
                    if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                        value = "\"" + value.Replace("\"", "\"\"") + "\"";
                    return value;
                }

                var sb = new StringBuilder();
                sb.AppendLine("كود الفرع,اسم الفرع,تاريخ الإنشاء,آخر تعديل");
                foreach (var b in list)
                {
                    sb.AppendLine(string.Join(",",
                        b.BranchId,
                        Csv(b.BranchName),
                        Csv(b.CreatedAt?.ToString("yyyy-MM-dd HH:mm") ?? ""),
                        Csv(b.UpdatedAt?.ToString("yyyy-MM-dd HH:mm") ?? "")
                    ));
                }

                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString());
                return File(bytes, "text/csv; charset=utf-8", ExcelExportNaming.ArabicTimestampedFileName("الفروع", ".csv"));
            }

            // ---------- Excel (.xlsx) ----------
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("الفروع"));

            ws.Cell(1, 1).Value = "كود الفرع";
            ws.Cell(1, 2).Value = "اسم الفرع";
            ws.Cell(1, 3).Value = "تاريخ الإنشاء";
            ws.Cell(1, 4).Value = "آخر تعديل";

            var header = ws.Range(1, 1, 1, 4);
            header.Style.Font.Bold = true;
            header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int row = 2;
            foreach (var b in list)
            {
                ws.Cell(row, 1).Value = b.BranchId;
                ws.Cell(row, 2).Value = b.BranchName ?? "";
                ws.Cell(row, 3).Value = b.CreatedAt?.ToString("yyyy-MM-dd HH:mm") ?? "";
                ws.Cell(row, 4).Value = b.UpdatedAt?.ToString("yyyy-MM-dd HH:mm") ?? "";
                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            stream.Position = 0;
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ExcelExportNaming.ArabicTimestampedFileName("الفروع", ".xlsx"));
        }

    }
}
