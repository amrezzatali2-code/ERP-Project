// ============================
// الملف: Controllers/ProductPriceHistoryController.cs
// الغرض: عرض سجل تغييرات سعر الجمهور مع بحث وترتيب وترقيم
// ملاحظات هامة:
// 1) q معرّفة كـ IQueryable<ProductPriceHistory> عشان نستخدم Where / OrderBy بحرية.
// 2) تم توحيد البراميتر مع نظام القوائم الموحد (Search / Sort / DateRange).
// 3) نستخدم PagedResult مع تخزين قيم البحث والترتيب والتاريخ داخله.
// ============================

using ClosedXML.Excel;           // مكتبة Excel
using DocumentFormat.OpenXml.InkML;
using ERP.Data;                                         // AppDbContext
using ERP.Filters;
using ERP.Infrastructure;                               // PagedResult<T>
using ERP.Models;                                       // ProductPriceHistory
using ERP.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;               // SelectListItem لفلاتر البحث/الترتيب
using Microsoft.EntityFrameworkCore;                    // Include / AsNoTracking / ToListAsync
using System;
using System.Globalization;      // CultureInfo للأرقام
using System.IO;                 // MemoryStream
using System.Linq;                                      // أوامر LINQ: Where / OrderBy / Skip / Take
using System.Text;               // StringBuilder + Encoding للـ CSV
using System.Threading.Tasks;


namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر عرض سجل تغييرات سعر الجمهور لكل صنف.
    /// لا يوجد إدخال يدوي؛ السجل يُكتب تلقائياً من شاشة الأصناف.
    /// </summary>
    [RequirePermission("ProductPriceHistory.Index")]
    public class ProductPriceHistoryController : Controller
    {
        // متغير: سياق قاعدة البيانات
        private readonly AppDbContext _ctx;

        // المُنشئ: استلام السياق من الـ DI
        public ProductPriceHistoryController(AppDbContext ctx)
        {
            _ctx = ctx;
        }

        // =========================================================
        // Index — قائمة سجل تغييرات الأسعار
        // =========================================================
        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        /// <summary>فلتر رقمي لكود التغيير (PriceChangeId) — نفس صيغ قائمة الأصناف.</summary>
        private static IQueryable<ProductPriceHistory> ApplyPriceChangeIdExpr(IQueryable<ProductPriceHistory> q, string exprRaw)
        {
            var expr = exprRaw.Trim();
            if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.Substring(2), out var max))
                return q.Where(h => h.PriceChangeId <= max);
            if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.Substring(2), out var min))
                return q.Where(h => h.PriceChangeId >= min);
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.Substring(1), out var max2))
                return q.Where(h => h.PriceChangeId < max2);
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.Substring(1), out var min2))
                return q.Where(h => h.PriceChangeId > min2);
            if ((expr.Contains(':') || (expr.Contains('-') && !expr.StartsWith("-"))) && expr.Length > 1)
            {
                var separator = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 &&
                    int.TryParse(parts[0].Trim(), out var from) &&
                    int.TryParse(parts[1].Trim(), out var to))
                {
                    if (from > to) (from, to) = (to, from);
                    return q.Where(h => h.PriceChangeId >= from && h.PriceChangeId <= to);
                }
            }
            if (int.TryParse(expr, out var exactId))
                return q.Where(h => h.PriceChangeId == exactId);
            return q;
        }

        /// <summary>فلتر رقمي للسعر القديم أو الجديد — نفس صيغ سعر الجمهور في قائمة الأصناف.</summary>
        private static IQueryable<ProductPriceHistory> ApplyDecimalPriceExpr(IQueryable<ProductPriceHistory> q, string exprRaw, bool oldPrice)
        {
            var expr = exprRaw.Trim();
            if (expr.StartsWith("<=") && expr.Length > 2 && decimal.TryParse(expr.Substring(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var max))
                return oldPrice ? q.Where(h => h.OldPrice <= max) : q.Where(h => h.NewPrice <= max);
            if (expr.StartsWith(">=") && expr.Length > 2 && decimal.TryParse(expr.Substring(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var min))
                return oldPrice ? q.Where(h => h.OldPrice >= min) : q.Where(h => h.NewPrice >= min);
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && decimal.TryParse(expr.Substring(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var max2))
                return oldPrice ? q.Where(h => h.OldPrice < max2) : q.Where(h => h.NewPrice < max2);
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && decimal.TryParse(expr.Substring(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var min2))
                return oldPrice ? q.Where(h => h.OldPrice > min2) : q.Where(h => h.NewPrice > min2);
            if ((expr.Contains(':') || (expr.Contains('-') && !expr.StartsWith("-"))) && expr.Length > 1)
            {
                var separator = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 &&
                    decimal.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var from) &&
                    decimal.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var to))
                {
                    if (from > to) (from, to) = (to, from);
                    return oldPrice
                        ? q.Where(h => h.OldPrice >= from && h.OldPrice <= to)
                        : q.Where(h => h.NewPrice >= from && h.NewPrice <= to);
                }
            }
            if (decimal.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var exact))
                return oldPrice ? q.Where(h => h.OldPrice == exact) : q.Where(h => h.NewPrice == exact);
            return q;
        }

        /// <summary>بحث السجل: يحتوي / يبدأ بـ / ينتهي بـ — يُستخدم في Index و Export.</summary>
        private static IQueryable<ProductPriceHistory> ApplyListSearch(
            IQueryable<ProductPriceHistory> q,
            string? search,
            string? searchBy,
            string? searchMode)
        {
            var term = (search ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(term))
                return q;

            var sb = (searchBy ?? "prod").Trim().ToLowerInvariant();
            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm == "startswith") sm = "starts";
            if (sm != "contains" && sm != "starts" && sm != "ends")
                sm = "contains";

            var likeContains = $"%{term}%";
            var likeStarts = $"{term}%";
            var likeEnds = $"%{term}";
            // لا تستخدم دالة محلية داخل Where — تعبيرات EF لا تدعمها (CS8110).
            var pattern = sm == "starts" ? likeStarts : sm == "ends" ? likeEnds : likeContains;

            const string dateFmt = "yyyy/MM/dd HH:mm";

            switch (sb)
            {
                case "code":
                    if (int.TryParse(term, out var code))
                        return q.Where(h => h.ProdId == code);
                    return q.Where(h => EF.Functions.Like(h.ProdId.ToString(), pattern));

                case "user":
                    return q.Where(h => h.ChangedBy != null && EF.Functions.Like(h.ChangedBy, pattern));

                case "reason":
                    return q.Where(h => h.Reason != null && EF.Functions.Like(h.Reason, pattern));

                case "date":
                    return q.Where(h => EF.Functions.Like(h.ChangeDate.ToString(dateFmt), pattern));

                case "all":
                    return q.Where(h =>
                        (h.Product != null && h.Product.ProdName != null && EF.Functions.Like(h.Product.ProdName, pattern))
                        || EF.Functions.Like(h.ProdId.ToString(), pattern)
                        || (h.ChangedBy != null && EF.Functions.Like(h.ChangedBy, pattern))
                        || (h.Reason != null && EF.Functions.Like(h.Reason, pattern))
                        || EF.Functions.Like(h.ChangeDate.ToString(dateFmt), pattern));

                case "prod":
                default:
                    return q.Where(h =>
                        (h.Product != null && h.Product.ProdName != null && EF.Functions.Like(h.Product.ProdName, pattern))
                        || EF.Functions.Like(h.ProdId.ToString(), pattern));
            }
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            string? search,                 // نص البحث المكتوب في صندوق البحث
            string? searchBy = "prod",    // prod | code | user | reason | date | all
            string? searchMode = "contains", // contains | starts | ends
            string? sort = "date",    // date | prod | code | old | new | user
            string? dir = "desc",    // asc | desc
            int page = 1,         // رقم الصفحة الحالية
            int pageSize = 10,        // عدد السطور في الصفحة (0 = الكل)
            bool useDateRange = false,     // هل فلترة التاريخ مفعّلة؟
            DateTime? fromDate = null,      // من تاريخ (ChangeDate)
            DateTime? toDate = null,       // إلى تاريخ
            string? filterCol_code = null,
            string? filterCol_codeExpr = null,
            string? filterCol_date = null,
            string? filterCol_prod = null,
            string? filterCol_oldprice = null,
            string? filterCol_oldpriceExpr = null,
            string? filterCol_newprice = null,
            string? filterCol_newpriceExpr = null,
            string? filterCol_user = null,
            string? filterCol_reason = null
        )
        {
            var pageSizeQuery = Request.Query["pageSize"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageSizeQuery) && int.TryParse(pageSizeQuery, out var psVal))
                pageSize = psVal;

            var searchModeQuery = Request.Query["searchMode"].LastOrDefault();
            if (!string.IsNullOrEmpty(searchModeQuery))
                searchMode = searchModeQuery;

            // (1) مصدر البيانات — IQueryable للسماح بالبناء التدريجي
            IQueryable<ProductPriceHistory> q =
                _ctx.ProductPriceHistories
                    .AsNoTracking()
                    .Include(h => h.Product);   // تضمين بيانات الصنف لعرض الاسم

            // تجهيز قيم الفلاتر بعد التنظيف
            var term = (search ?? string.Empty).Trim();                // نص البحث بعد إزالة المسافات
            var sb = (searchBy ?? "prod").Trim().ToLowerInvariant(); // حقل البحث
            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm == "startswith") sm = "starts";
            if (sm != "contains" && sm != "starts" && sm != "ends")
                sm = "contains";
            var so = (sort ?? "date").Trim().ToLowerInvariant();     // حقل الترتيب
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase); // هل الترتيب تنازلي؟

            // =====================================================
            // (2) البحث (يبدأ بـ / يحتوي / ينتهي بـ)
            // =====================================================
            q = ApplyListSearch(q, term, sb, sm);

            // =====================================================
            // (3) فلترة بالتاريخ (ChangeDate) — حسب نظام القوائم الموحد
            // =====================================================
            if (useDateRange)
            {
                if (fromDate.HasValue)
                {
                    // من تاريخ معيّن فأعلى
                    q = q.Where(h => h.ChangeDate >= fromDate.Value);
                }

                if (toDate.HasValue)
                {
                    // حتى هذا التاريخ (يوم/ساعة)
                    q = q.Where(h => h.ChangeDate <= toDate.Value);
                }
            }

            // =====================================================
            // (3b) فلاتر الأعمدة (بنمط Excel) — كود التغيير والأسعار: أولوية للتعبير الرقمي Expr
            // =====================================================
            if (!string.IsNullOrWhiteSpace(filterCol_codeExpr))
                q = ApplyPriceChangeIdExpr(q, filterCol_codeExpr);
            else if (!string.IsNullOrWhiteSpace(filterCol_code))
            {
                var ids = filterCol_code.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToList();
                if (ids.Count > 0)
                    q = q.Where(h => ids.Contains(h.PriceChangeId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_date))
            {
                var parts = filterCol_date.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => x.Length >= 6) // yyyy-MM
                    .ToList();
                if (parts.Count > 0)
                {
                    var dateFilters = new List<(int Year, int Month)>();
                    foreach (var p in parts)
                    {
                        if (p.Length == 7 && p[4] == '-' && int.TryParse(p.Substring(0, 4), out var y) && int.TryParse(p.Substring(5, 2), out var m) && m >= 1 && m <= 12)
                            dateFilters.Add((y, m));
                    }
                    if (dateFilters.Count > 0)
                        q = q.Where(h => dateFilters.Any(df => h.ChangeDate.Year == df.Year && h.ChangeDate.Month == df.Month));
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_prod))
            {
                var prodIds = filterCol_prod.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToList();
                if (prodIds.Count > 0)
                    q = q.Where(h => prodIds.Contains(h.ProdId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_oldpriceExpr))
                q = ApplyDecimalPriceExpr(q, filterCol_oldpriceExpr, oldPrice: true);
            else if (!string.IsNullOrWhiteSpace(filterCol_oldprice))
            {
                var prices = filterCol_oldprice.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToList();
                if (prices.Count > 0)
                    q = q.Where(h => prices.Contains(h.OldPrice));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_newpriceExpr))
                q = ApplyDecimalPriceExpr(q, filterCol_newpriceExpr, oldPrice: false);
            else if (!string.IsNullOrWhiteSpace(filterCol_newprice))
            {
                var prices = filterCol_newprice.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToList();
                if (prices.Count > 0)
                    q = q.Where(h => prices.Contains(h.NewPrice));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_user))
            {
                var vals = filterCol_user.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(h => h.ChangedBy != null && vals.Contains(h.ChangedBy));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_reason))
            {
                var vals = filterCol_reason.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(h => h.Reason != null && vals.Contains(h.Reason));
            }

            // =====================================================
            // (4) الترتيب
            // =====================================================
            switch (so)
            {
                case "id":     // ترتيب بكود التغيير (PriceChangeId)
                    q = (desc ? q.OrderByDescending(h => h.PriceChangeId) : q.OrderBy(h => h.PriceChangeId));
                    break;
                case "prod":   // ترتيب باسم الصنف
                    q = (desc
                            ? q.OrderByDescending(h => h.Product != null ? h.Product.ProdName : "")
                            : q.OrderBy(h => h.Product != null ? h.Product.ProdName : ""))
                        .ThenByDescending(h => h.ChangeDate);   // كسر التعادل بالتاريخ الأحدث
                    break;

                case "code":   // ترتيب برقم الصنف (ProdId)
                    q = (desc
                            ? q.OrderByDescending(h => h.ProdId)
                            : q.OrderBy(h => h.ProdId))
                        .ThenByDescending(h => h.ChangeDate);
                    break;

                case "old":    // ترتيب بالسعر القديم
                    q = (desc
                            ? q.OrderByDescending(h => h.OldPrice)
                            : q.OrderBy(h => h.OldPrice))
                        .ThenByDescending(h => h.ChangeDate);
                    break;

                case "new":    // ترتيب بالسعر الجديد
                    q = (desc
                            ? q.OrderByDescending(h => h.NewPrice)
                            : q.OrderBy(h => h.NewPrice))
                        .ThenByDescending(h => h.ChangeDate);
                    break;

                case "user":   // ترتيب باسم المستخدم
                    q = (desc
                            ? q.OrderByDescending(h => h.ChangedBy)
                            : q.OrderBy(h => h.ChangedBy))
                        .ThenByDescending(h => h.ChangeDate);
                    break;

                case "date":   // ترتيب بالتاريخ
                default:
                    q = desc
                        ? q.OrderByDescending(h => h.ChangeDate)
                        : q.OrderBy(h => h.ChangeDate);
                    break;
            }

            // =====================================================
            // (5) الترقيم — تجهيز PagedResult مع حفظ قيم الفلاتر
            // =====================================================
            if (page < 1) page = 1;
            if (pageSize < 0) pageSize = 10;
            if (pageSize > 0 && pageSize != 10 && pageSize != 25 && pageSize != 50 && pageSize != 100 && pageSize != 200)
                pageSize = 10;

            int total = await q.CountAsync();                               // إجمالي السجلات بعد الفلترة
            var effectivePageSize = pageSize;
            if (pageSize == 0)
            {
                page = 1;
                effectivePageSize = total == 0 ? 10 : Math.Min(total, 100_000);
            }

            var items = await q.Skip((page - 1) * effectivePageSize)                 // تخطي الصفوف السابقة
                               .Take(effectivePageSize)                              // أخذ عدد الصفوف المطلوبة
                               .ToListAsync();                              // تنفيذ الاستعلام فعليًا

            // إنشاء نموذج الترقيم مع تخزين قيم الفلاتر بداخله
            var model = new PagedResult<ProductPriceHistory>(items, page, pageSize, total)
            {
                Search = term,          // نص البحث الحالي
                SearchBy = sb,            // الحقل الذي نبحث به
                SortColumn = so,            // عمود الترتيب الحالي
                SortDescending = desc,          // اتجاه الترتيب
                UseDateRange = useDateRange,  // هل فلترة التاريخ فعّالة؟
                FromDate = fromDate,      // من تاريخ
                ToDate = toDate         // إلى تاريخ
            };

            // =====================================================
            // (6) إعداد خيارات البارشال (_IndexFilters)
            // =====================================================

            // خيارات البحث (ابحث في) — للتوافق مع أي واجهة تستخدم ViewBag
            ViewBag.SearchOptions = new[]
            {
                new SelectListItem("الكل", "all", sb == "all"),
                new SelectListItem("اسم الصنف", "prod", sb == "prod"),
                new SelectListItem("كود الصنف", "code", sb == "code"),
                new SelectListItem("المستخدم", "user", sb == "user"),
                new SelectListItem("سبب التغيير", "reason", sb == "reason"),
                new SelectListItem("تاريخ التغيير", "date", sb == "date"),
            };

            // خيارات الترتيب (رتّب حسب)
            ViewBag.SortOptions = new[]
            {
                new SelectListItem("تاريخ التغيير", "date",  so == "date"),
                new SelectListItem("اسم الصنف",     "prod",  so == "prod"),
                new SelectListItem("كود الصنف",     "code",  so == "code"),
                new SelectListItem("السعر القديم",  "old",   so == "old"),
                new SelectListItem("السعر الجديد",  "new",   so == "new"),
                new SelectListItem("المستخدم",      "user",  so == "user"),
            };

            ViewBag.Search = term;
            ViewBag.SearchBy = sb;
            ViewBag.SearchMode = sm;
            ViewBag.Sort = so;
            ViewBag.Dir = desc ? "desc" : "asc";
            ViewBag.PageSize = pageSize;
            ViewBag.UseDateRange = useDateRange;
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;
            ViewBag.FilterCol_Code = filterCol_code;
            ViewBag.FilterCol_CodeExpr = filterCol_codeExpr;
            ViewBag.FilterCol_Date = filterCol_date;
            ViewBag.FilterCol_Prod = filterCol_prod;
            ViewBag.FilterCol_OldPrice = filterCol_oldprice;
            ViewBag.FilterCol_OldPriceExpr = filterCol_oldpriceExpr;
            ViewBag.FilterCol_NewPrice = filterCol_newprice;
            ViewBag.FilterCol_NewPriceExpr = filterCol_newpriceExpr;
            ViewBag.FilterCol_User = filterCol_user;
            ViewBag.FilterCol_Reason = filterCol_reason;

            // عرض فلتر كود من/إلى من filterCol_codeExpr إن وُجد
            int? codeFrom = null, codeTo = null;
            if (!string.IsNullOrWhiteSpace(filterCol_codeExpr))
            {
                var e = filterCol_codeExpr.Trim();
                if ((e.Contains(':') || (e.Contains('-') && !e.StartsWith("-"))) && e.Length > 1)
                {
                    var sep = e.Contains(':') ? ':' : '-';
                    var parts = e.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2 &&
                        int.TryParse(parts[0].Trim(), out var a) &&
                        int.TryParse(parts[1].Trim(), out var b))
                    {
                        codeFrom = a;
                        codeTo = b;
                    }
                }
                else if (e.StartsWith(">=", StringComparison.Ordinal) && e.Length > 2 && int.TryParse(e.Substring(2), out var mn))
                    codeFrom = mn;
                else if (e.StartsWith("<=", StringComparison.Ordinal) && e.Length > 2 && int.TryParse(e.Substring(2), out var mx))
                    codeTo = mx;
                else if (int.TryParse(e, out var id))
                {
                    codeFrom = id;
                    codeTo = id;
                }
            }
            ViewBag.CodeFrom = codeFrom;
            ViewBag.CodeTo = codeTo;

            return View(model);
        }

        /// <summary>
        /// API: جلب القيم المميزة لعمود (للفلترة بنمط Excel).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var q = _ctx.ProductPriceHistories
                .AsNoTracking()
                .Include(h => h.Product);

            List<(string Value, string Display)> items = column?.ToLowerInvariant() switch
            {
                // الأعمدة الرقمية تُفلتر عبر filterCol_*Expr في الواجهة — لا نحمّل قوائم قيم
                "code" => new List<(string Value, string Display)>(),
                "date" => (await q.Select(h => new { h.ChangeDate.Year, h.ChangeDate.Month }).Distinct()
                    .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).Take(100).ToListAsync())
                    .Select(x => ($"{x.Year}-{x.Month:D2}", $"{x.Year}/{x.Month:D2}")).ToList(),
                "prod" => (await q.Where(h => h.Product != null).Select(h => new { h.ProdId, Name = h.Product!.ProdName ?? "" })
                    .Distinct().OrderBy(x => x.Name).Take(500).ToListAsync())
                    .Select(x => (x.ProdId.ToString(), string.IsNullOrEmpty(x.Name) ? x.ProdId.ToString() : x.Name)).ToList(),
                "oldprice" => new List<(string Value, string Display)>(),
                "newprice" => new List<(string Value, string Display)>(),
                "user" => (await q.Where(h => h.ChangedBy != null && h.ChangedBy != "").Select(h => h.ChangedBy!).Distinct()
                    .OrderBy(c => c).Take(500).ToListAsync()).Select(c => (c!, c)).ToList(),
                "reason" => (await q.Where(h => h.Reason != null && h.Reason != "").Select(h => h.Reason!).Distinct()
                    .OrderBy(c => c).Take(500).ToListAsync()).Select(c => (c!, c)).ToList(),
                _ => new List<(string Value, string Display)>()
            };

            if (!string.IsNullOrEmpty(searchTerm))
            {
                items = items.Where(x => (x.Display ?? x.Value).ToLowerInvariant().Contains(searchTerm)).ToList();
            }

            return Json(items.Select(x => new { value = x.Value, display = x.Display }));
        }

        // =========================
        // Export — تصدير سجل تغيّرات الأسعار (Excel أو CSV)
        // =========================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string searchBy = "prod",
            string? searchMode = "contains",
            string sort = "date",
            string dir = "desc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? filterCol_code = null,
            string? filterCol_codeExpr = null,
            string? filterCol_date = null,
            string? filterCol_prod = null,
            string? filterCol_oldprice = null,
            string? filterCol_oldpriceExpr = null,
            string? filterCol_newprice = null,
            string? filterCol_newpriceExpr = null,
            string? filterCol_user = null,
            string? filterCol_reason = null,
            string? format = "excel"
        )
        {
            IQueryable<ProductPriceHistory> q =
                _ctx.ProductPriceHistories
                    .AsNoTracking()
                    .Include(h => h.Product);

            var term = (search ?? string.Empty).Trim();
            var smExport = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (smExport == "startswith") smExport = "starts";
            if (smExport != "contains" && smExport != "starts" && smExport != "ends")
                smExport = "contains";
            q = ApplyListSearch(q, term, searchBy, smExport);

            if (useDateRange)
            {
                if (fromDate.HasValue) q = q.Where(h => h.ChangeDate >= fromDate.Value);
                if (toDate.HasValue) q = q.Where(h => h.ChangeDate <= toDate.Value);
            }

            if (!string.IsNullOrWhiteSpace(filterCol_codeExpr))
                q = ApplyPriceChangeIdExpr(q, filterCol_codeExpr);
            else if (!string.IsNullOrWhiteSpace(filterCol_code))
            {
                var ids = filterCol_code.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null).Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) q = q.Where(h => ids.Contains(h.PriceChangeId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_date))
            {
                var parts = filterCol_date.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length >= 6).ToList();
                var dateFilters = new List<(int Year, int Month)>();
                foreach (var p in parts)
                {
                    if (p.Length == 7 && p[4] == '-' && int.TryParse(p.Substring(0, 4), out var y) && int.TryParse(p.Substring(5, 2), out var m) && m >= 1 && m <= 12)
                        dateFilters.Add((y, m));
                }
                if (dateFilters.Count > 0) q = q.Where(h => dateFilters.Any(df => h.ChangeDate.Year == df.Year && h.ChangeDate.Month == df.Month));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_prod))
            {
                var prodIds = filterCol_prod.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null).Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (prodIds.Count > 0) q = q.Where(h => prodIds.Contains(h.ProdId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_oldpriceExpr))
                q = ApplyDecimalPriceExpr(q, filterCol_oldpriceExpr, oldPrice: true);
            else if (!string.IsNullOrWhiteSpace(filterCol_oldprice))
            {
                var prices = filterCol_oldprice.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null).Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (prices.Count > 0) q = q.Where(h => prices.Contains(h.OldPrice));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_newpriceExpr))
                q = ApplyDecimalPriceExpr(q, filterCol_newpriceExpr, oldPrice: false);
            else if (!string.IsNullOrWhiteSpace(filterCol_newprice))
            {
                var prices = filterCol_newprice.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null).Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (prices.Count > 0) q = q.Where(h => prices.Contains(h.NewPrice));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_user))
            {
                var vals = filterCol_user.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) q = q.Where(h => h.ChangedBy != null && vals.Contains(h.ChangedBy));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_reason))
            {
                var vals = filterCol_reason.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) q = q.Where(h => h.Reason != null && vals.Contains(h.Reason));
            }

            bool desc = dir.Equals("desc", StringComparison.OrdinalIgnoreCase);

            switch (sort.ToLowerInvariant())
            {
                case "id":
                    q = (desc ? q.OrderByDescending(h => h.PriceChangeId) : q.OrderBy(h => h.PriceChangeId));
                    break;
                case "prod":   // ترتيب باسم الصنف
                    q = (desc
                            ? q.OrderByDescending(h => h.Product != null ? h.Product.ProdName : "")
                            : q.OrderBy(h => h.Product != null ? h.Product.ProdName : ""))
                        .ThenByDescending(h => h.ChangeDate);
                    break;
                case "code":   // ترتيب برقم الصنف
                    q = (desc
                            ? q.OrderByDescending(h => h.ProdId)
                            : q.OrderBy(h => h.ProdId))
                        .ThenByDescending(h => h.ChangeDate);
                    break;

                case "old":    // ترتيب بالسعر القديم
                    q = (desc
                            ? q.OrderByDescending(h => h.OldPrice)
                            : q.OrderBy(h => h.OldPrice))
                        .ThenByDescending(h => h.ChangeDate);
                    break;

                case "new":    // ترتيب بالسعر الجديد
                    q = (desc
                            ? q.OrderByDescending(h => h.NewPrice)
                            : q.OrderBy(h => h.NewPrice))
                        .ThenByDescending(h => h.ChangeDate);
                    break;

                case "user":   // ترتيب باسم المستخدم
                    q = (desc
                            ? q.OrderByDescending(h => h.ChangedBy)
                            : q.OrderBy(h => h.ChangedBy))
                        .ThenByDescending(h => h.ChangeDate);
                    break;

                case "date":
                default:       // ترتيب بالتاريخ مباشرة
                    q = desc
                        ? q.OrderByDescending(h => h.ChangeDate)
                        : q.OrderBy(h => h.ChangeDate);
                    break;
            }

            // (4) جلب كل الصفوف المطابقة (بدون Paging)
            var rows = await q.ToListAsync();   // متغير: قائمة السجلات المصدَّرة

            // توحيد قيمة format
            format = (format ?? "excel").Trim().ToLowerInvariant();

            // =====================================
            // الفرع الأول: تصدير CSV
            // =====================================
            if (format == "csv")
            {
                var sb = new StringBuilder();   // متغير: نص CSV في الذاكرة

                // عناوين الأعمدة
                sb.AppendLine(string.Join(",",
                    Csv("التاريخ"),
                    Csv("كود الصنف"),
                    Csv("اسم الصنف"),
                    Csv("السعر القديم"),
                    Csv("السعر الجديد"),
                    Csv("المستخدم"),
                    Csv("السبب")
                ));

                // الصفوف
                foreach (var h in rows)
                {
                    sb.AppendLine(string.Join(",",
                        Csv(h.ChangeDate.ToString("yyyy-MM-dd HH:mm")),
                        Csv(h.ProdId.ToString()),
                        Csv(h.Product?.ProdName),
                        Csv(h.OldPrice.ToString(CultureInfo.InvariantCulture)),
                        Csv(h.NewPrice.ToString(CultureInfo.InvariantCulture)),
                        Csv(h.ChangedBy),
                        Csv(h.Reason)
                    ));
                }

                // UTF-8 مع BOM علشان Excel يقرأ العربي صح
                var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
                var bytes = utf8.GetBytes(sb.ToString());
                var fileNameCsv = ExcelExportNaming.ArabicTimestampedFileName("سجل تغييرات أسعار الأصناف", ".csv");

                return File(bytes, "text/csv; charset=utf-8", fileNameCsv);
            }

            // =====================================
            // الفرع الثاني: تصدير Excel (XLSX)
            // =====================================
            using var workbook = new XLWorkbook();                      // متغير: ملف Excel
            var ws = workbook.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("سجل تغييرات الأسعار"));

            int r = 1; // متغير: رقم الصف الحالي

            // عناوين الأعمدة
            ws.Cell(r, 1).Value = "التاريخ";
            ws.Cell(r, 2).Value = "كود الصنف";
            ws.Cell(r, 3).Value = "اسم الصنف";
            ws.Cell(r, 4).Value = "السعر القديم";
            ws.Cell(r, 5).Value = "السعر الجديد";
            ws.Cell(r, 6).Value = "المستخدم";
            ws.Cell(r, 7).Value = "السبب";

            // تنسيق العناوين
            var headerRange = ws.Range(r, 1, r, 7);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // البيانات
            foreach (var h in rows)
            {
                r++;

                ws.Cell(r, 1).Value = h.ChangeDate;
                ws.Cell(r, 2).Value = h.ProdId;
                ws.Cell(r, 3).Value = h.Product?.ProdName ?? "";
                ws.Cell(r, 4).Value = h.OldPrice;
                ws.Cell(r, 5).Value = h.NewPrice;
                ws.Cell(r, 6).Value = h.ChangedBy ?? "";
                ws.Cell(r, 7).Value = h.Reason ?? "";
            }

            // تنسيق الأعمدة
            ws.Column(1).Style.DateFormat.Format = "yyyy-MM-dd HH:mm";
            ws.Column(4).Style.NumberFormat.Format = "0.00";   // السعر القديم
            ws.Column(5).Style.NumberFormat.Format = "0.00";   // السعر الجديد

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();         // متغير: ذاكرة مؤقتة
            workbook.SaveAs(stream);
            stream.Position = 0;

            var fileNameXlsx = ExcelExportNaming.ArabicTimestampedFileName("سجل تغييرات أسعار الأصناف", ".xlsx");
            const string contentTypeXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

            return File(stream.ToArray(), contentTypeXlsx, fileNameXlsx);
        }

        // دالة مساعدة لتجهيز النص للـ CSV (نفس المستخدمة في الجداول الأخرى)
        private static string Csv(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            var s = value.Replace("\"", "\"\"");   // استبدال " بـ ""

            if (s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
                return "\"" + s + "\"";           // لو فيه فواصل/سطور → نحوطه بين ""

            return s;
        }


        // ==================== حذف المحدد (BulkDelete) ====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedIds)
        {
            // selectedIds: نص فيه الأرقام مفصولة بفواصل "1,5,9"
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["Error"] = "من فضلك اختر حركة واحدة على الأقل للحذف.";
                return RedirectToAction(nameof(Index));
            }

            // تحويل النص إلى قائمة أرقام صحيحة
            var ids = selectedIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var id) ? (int?)id : null)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToList();

            if (ids.Count == 0)
            {
                TempData["Error"] = "لم يتم التعرف على أى أرقام صحيحة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            // جلب السجلات المطلوبة من قاعدة البيانات
            var rows = await _ctx.ProductPriceHistories
                .Where(r => ids.Contains(r.PriceChangeId))
                .ToListAsync();

            if (rows.Count == 0)
            {
                TempData["Error"] = "لم يتم العثور على السجلات المحددة.";
                return RedirectToAction(nameof(Index));
            }

            _ctx.ProductPriceHistories.RemoveRange(rows);
            await _ctx.SaveChangesAsync();

            TempData["Success"] = $"تم حذف {rows.Count} حركة من سجل تغييرات السعر.";
            return RedirectToAction(nameof(Index));
        }



        // ==================== حذف جميع السجلات (DeleteAll) ====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            // تحذير: هذا يمسح كل سجل الأسعار
            var allRows = await _ctx.ProductPriceHistories.ToListAsync();

            if (allRows.Count == 0)
            {
                TempData["Info"] = "لا توجد بيانات للحذف.";
                return RedirectToAction(nameof(Index));
            }

            _ctx.ProductPriceHistories.RemoveRange(allRows);
            await _ctx.SaveChangesAsync();

            TempData["Success"] = "تم حذف جميع سجلات تغييرات السعر.";
            return RedirectToAction(nameof(Index));
        }

    }
}
