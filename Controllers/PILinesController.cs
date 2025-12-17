using System;                                   // تواريخ وأوقات
using System.Collections.Generic;               // القوائم List
using System.Linq;                              // LINQ: Where / OrderBy
using System.Text;                              // لبناء ملف CSV
using System.Threading.Tasks;                   // async / await
using Microsoft.AspNetCore.Mvc;                 // أساس الكنترولر
using Microsoft.EntityFrameworkCore;            // Include / AsNoTracking
using ERP.Data;                                 // AppDbContext
using ERP.Models;                               // الموديل PILine + PurchaseInvoice
using ERP.Infrastructure;                       // PagedResult لتقسيم الصفحات

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إدارة جدول سطور فواتير الشراء (PILines)
    /// - عرض قائمة السطور مع بحث / ترتيب / تقسيم صفحات.
    /// - فلترة بتاريخ الفاتورة (من رأس الفاتورة).
    /// - فلترة من رقم فاتورة / إلى رقم فاتورة.
    /// - حذف محدد / حذف الكل.
    /// - تصدير CSV/Excel.
    /// </summary>
    public class PILinesController : Controller
    {
        // كائن الاتصال بقاعدة البيانات
        private readonly AppDbContext _context;

        public PILinesController(AppDbContext context)
        {
            _context = context;
        }

        #region Index (قائمة سطور فواتير الشراء)

        /// <summary>
        /// عرض قائمة سطور فواتير الشراء بنفس نظام القوائم الموحد.
        /// </summary>
        public async Task<IActionResult> Index(
            string? search,                      // نص البحث
            string? searchBy,                    // طريقة البحث: all / piid / prod / batch / expiry
            string? sort,                        // عمود الترتيب: piid / lineno / prod / qty / unitcost / disc / retail / batch / expiry / date
            string? dir,                         // اتجاه الترتيب: asc / desc
            bool useDateRange = false,           // تفعيل فلتر التاريخ؟
            DateTime? fromDate = null,           // من تاريخ
            DateTime? toDate = null,             // إلى تاريخ
            string? dateField = "PIDate",        // الحقل المستخدم لفلتر التاريخ (تاريخ الفاتورة)
            int? fromCode = null,                // من رقم فاتورة (PIId)
            int? toCode = null,                  // إلى رقم فاتورة
            int page = 1,                        // رقم الصفحة
            int pageSize = 25                    // حجم الصفحة
        )
        {
            // قيم افتراضية
            searchBy ??= "all";
            sort ??= "piid";
            dir ??= "asc";
            dateField ??= "PIDate";

            if (page < 1) page = 1;
            if (pageSize <= 0) pageSize = 25;

            // نبدأ بالاستعلام من جدول PILines مع تحميل رأس الفاتورة (PurchaseInvoice)
            IQueryable<PILine> query = _context.PILines
                .Include(l => l.Product)          // اسم الصنف
                .Include(l => l.PurchaseInvoice)       // تحميل الفاتورة للحصول على التاريخ وغيره
                .AsNoTracking();

            // نقرأ codeFrom / codeTo من الكويري لو موجودين
            int? codeFrom = Request.Query.ContainsKey("codeFrom")
                ? TryParseNullableInt(Request.Query["codeFrom"])
                : null;

            int? codeTo = Request.Query.ContainsKey("codeTo")
                ? TryParseNullableInt(Request.Query["codeTo"])
                : null;

            int? finalFromCode = fromCode ?? codeFrom;
            int? finalToCode = toCode ?? codeTo;

            // 1) تطبيق الفلاتر (بحث + من/إلى رقم فاتورة + تاريخ الفاتورة)
            query = ApplyFilters(
                query,
                search,
                searchBy,
                finalFromCode,
                finalToCode,
                useDateRange,
                fromDate,
                toDate,
                dateField
            );

            // 2) تطبيق الترتيب
            bool sortDesc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            query = ApplySort(query, sort, sortDesc);

            // 3) حساب العدد الكلي بعد الفلاتر
            int totalCount = await query.CountAsync();

            // 4) قراءة صفحة واحدة فقط
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            // 5) تجهيز PagedResult
            var model = new PagedResult<PILine>
            {
                Items = items,
                PageNumber = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                HasPrevious = page > 1,
                HasNext = page < totalPages,
                Search = search,
                SortColumn = sort,
                SortDescending = sortDesc,
                UseDateRange = useDateRange,
                FromDate = fromDate,
                ToDate = toDate
            };

            // 6) تمرير القيم للـ ViewBag علشان الواجهة تحفظ الحالة
            ViewBag.Search = search;
            ViewBag.SearchBy = searchBy;
            ViewBag.Sort = sort;
            ViewBag.Dir = sortDesc ? "desc" : "asc";
            ViewBag.DateField = dateField;

            ViewBag.FromCode = finalFromCode;
            ViewBag.ToCode = finalToCode;
            ViewBag.CodeFrom = finalFromCode;
            ViewBag.CodeTo = finalToCode;

            return View(model);
        }

        #endregion

        #region BulkDelete / DeleteAll (حذف السطور)

        /// <summary>
        /// حذف مجموعة من السطور.
        /// selectedKeys يجي بصيغة: "PIId:LineNo,PIId:LineNo,..."
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedKeys)
        {
            if (string.IsNullOrWhiteSpace(selectedKeys))
            {
                TempData["ErrorMessage"] = "لم يتم اختيار أي سطر للحذف.";
                return RedirectToAction(nameof(Index));
            }

            // نحول "1:1,1:2,2:1" إلى قائمة من (PIId, LineNo)
            var keys = selectedKeys
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseCompositeKey)
                .Where(k => k.HasValue)
                .Select(k => k!.Value)
                .ToList();

            if (!keys.Any())
            {
                TempData["ErrorMessage"] = "لم يتم التعرف على أرقام السطور المحددة.";
                return RedirectToAction(nameof(Index));
            }

            var invoicesIds = keys.Select(k => k.PIId).Distinct().ToList();

            var lines = await _context.PILines
                .Where(l => invoicesIds.Contains(l.PIId))
                .ToListAsync();

            // نفلتر تاني على LineNo علشان المفتاح مركب
            var linesToDelete = lines
                .Where(l => keys.Any(k => k.PIId == l.PIId && k.LineNo == l.LineNo))
                .ToList();

            if (linesToDelete.Any())
            {
                _context.PILines.RemoveRange(linesToDelete);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"تم حذف {linesToDelete.Count} سطر من فواتير الشراء.";
            }
            else
            {
                TempData["ErrorMessage"] = "لم يتم العثور على السطور المحددة في قاعدة البيانات.";
            }

            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// حذف جميع سطور فواتير الشراء (جدول PILines بالكامل).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var allLines = await _context.PILines.ToListAsync();

            if (!allLines.Any())
            {
                TempData["ErrorMessage"] = "لا توجد سطور لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            _context.PILines.RemoveRange(allLines);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "تم حذف جميع سطور فواتير الشراء بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        #endregion

        #region Export (تصدير السطور)

        /// <summary>
        /// تصدير سطور فواتير الشراء بعد الفلاتر إلى CSV/Excel.
        /// format = excel أو csv (الاتنين CSV حاليًا).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Export(
            string? format,
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? dateField = "PIDate",
            int? codeFrom = null,
            int? codeTo = null
        )
        {
            format = string.IsNullOrWhiteSpace(format) ? "excel" : format.ToLowerInvariant();
            searchBy ??= "all";
            sort ??= "piid";
            dir ??= "asc";
            dateField ??= "PIDate";

            bool sortDesc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            IQueryable<PILine> query = _context.PILines
                .Include(l => l.PurchaseInvoice)
                .AsNoTracking();

            query = ApplyFilters(
                query,
                search,
                searchBy,
                codeFrom,
                codeTo,
                useDateRange,
                fromDate,
                toDate,
                dateField
            );

            query = ApplySort(query, sort, sortDesc);

            var list = await query.ToListAsync();

            // بناء CSV
            var sb = new StringBuilder();
            sb.AppendLine("PIId,LineNo,ProdId,Qty,UnitCost,PurchaseDiscountPct,PriceRetail,BatchNo,Expiry,InvoiceDate");

            foreach (var l in list)
            {
                string batch = (l.BatchNo ?? "").Replace(",", " ");
                string expiryStr = l.Expiry.HasValue
                    ? l.Expiry.Value.ToString("yyyy-MM-dd")
                    : "";
                string dateStr = l.PurchaseInvoice?.PIDate.ToString("yyyy-MM-dd") ?? "";

                var line = string.Join(",",
                    l.PIId,
                    l.LineNo,
                    l.ProdId,
                    l.Qty,
                    l.UnitCost.ToString("0.0000"),
                    l.PurchaseDiscountPct.ToString("0.00"),
                    l.PriceRetail.ToString("0.00"),
                    batch,
                    expiryStr,
                    dateStr
                );

                sb.AppendLine(line);
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = $"PILines_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            const string contentType = "text/csv";

            return File(bytes, contentType, fileName);
        }

        #endregion

        #region دوال الفلترة والترتيب والمساعدة

        /// <summary>
        /// فلترة سطور الفواتير:
        /// - نص البحث (حسب searchBy)
        /// - من رقم فاتورة / إلى رقم فاتورة
        /// - فلتر بتاريخ الفاتورة (من PurchaseInvoice.PIDate)
        /// </summary>
        private static IQueryable<PILine> ApplyFilters(
            IQueryable<PILine> query,
            string? search,
            string? searchBy,
            int? fromCode,
            int? toCode,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            string dateField
        )
        {
            searchBy ??= "all";
            dateField ??= "PIDate";

            // 1) فلتر نص البحث
            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();

                switch (searchBy.ToLower())
                {
                    case "piid":
                        if (int.TryParse(search, out var piIdVal))
                        {
                            query = query.Where(l => l.PIId == piIdVal);
                        }
                        else
                        {
                            query = query.Where(l => l.PIId.ToString().Contains(search));
                        }
                        break;

                    case "prod":
                        if (int.TryParse(search, out var prodIdVal))
                        {
                            query = query.Where(l => l.ProdId == prodIdVal);
                        }
                        else
                        {
                            query = query.Where(l => l.ProdId.ToString().Contains(search));
                        }
                        break;

                    case "batch":
                        query = query.Where(l => l.BatchNo != null && l.BatchNo.Contains(search));
                        break;

                    case "expiry":
                        if (DateTime.TryParse(search, out var expVal))
                        {
                            var d = expVal.Date;
                            query = query.Where(l => l.Expiry.HasValue && l.Expiry.Value.Date == d);
                        }
                        break;

                    case "all":
                    default:
                        query = query.Where(l =>
                            l.PIId.ToString().Contains(search) ||
                            l.ProdId.ToString().Contains(search) ||
                            (l.BatchNo != null && l.BatchNo.Contains(search)) ||
                            (l.PurchaseInvoice != null &&
                             l.PurchaseInvoice.PIDate.ToString().Contains(search))
                        );
                        break;
                }
            }

            // 2) فلتر من/إلى رقم فاتورة (PIId)
            if (fromCode.HasValue)
                query = query.Where(l => l.PIId >= fromCode.Value);

            if (toCode.HasValue)
                query = query.Where(l => l.PIId <= toCode.Value);

            // 3) فلتر التاريخ من رأس الفاتورة (PIDate أو CreatedAt لو حبيت مستقبلاً)
            if (useDateRange && (fromDate.HasValue || toDate.HasValue))
            {
                bool useCreated = string.Equals(dateField, "CreatedAt", StringComparison.OrdinalIgnoreCase);

                if (fromDate.HasValue)
                {
                    if (useCreated)
                        query = query.Where(l => l.PurchaseInvoice != null && l.PurchaseInvoice.CreatedAt >= fromDate.Value);
                    else
                        query = query.Where(l => l.PurchaseInvoice != null && l.PurchaseInvoice.PIDate >= fromDate.Value);
                }

                if (toDate.HasValue)
                {
                    if (useCreated)
                        query = query.Where(l => l.PurchaseInvoice != null && l.PurchaseInvoice.CreatedAt <= toDate.Value);
                    else
                        query = query.Where(l => l.PurchaseInvoice != null && l.PurchaseInvoice.PIDate <= toDate.Value);
                }
            }

            return query;
        }

        /// <summary>
        /// ترتيب سطور الفواتير حسب العمود المحدد من الواجهة.
        /// </summary>
        private static IQueryable<PILine> ApplySort(
            IQueryable<PILine> query,
            string? sort,
            bool desc
        )
        {
            sort = (sort ?? "piid").ToLower();

            switch (sort)
            {
                case "lineno":
                    query = desc
                        ? query.OrderByDescending(l => l.PIId).ThenByDescending(l => l.LineNo)
                        : query.OrderBy(l => l.PIId).ThenBy(l => l.LineNo);
                    break;

                case "prod":
                    query = desc
                        ? query.OrderByDescending(l => l.ProdId).ThenByDescending(l => l.PIId).ThenByDescending(l => l.LineNo)
                        : query.OrderBy(l => l.ProdId).ThenBy(l => l.PIId).ThenBy(l => l.LineNo);
                    break;

                case "qty":
                    query = desc
                        ? query.OrderByDescending(l => l.Qty).ThenByDescending(l => l.PIId).ThenByDescending(l => l.LineNo)
                        : query.OrderBy(l => l.Qty).ThenBy(l => l.PIId).ThenBy(l => l.LineNo);
                    break;

                case "unitcost":
                    query = desc
                        ? query.OrderByDescending(l => l.UnitCost).ThenByDescending(l => l.PIId).ThenByDescending(l => l.LineNo)
                        : query.OrderBy(l => l.UnitCost).ThenBy(l => l.PIId).ThenBy(l => l.LineNo);
                    break;

                case "disc":
                    query = desc
                        ? query.OrderByDescending(l => l.PurchaseDiscountPct).ThenByDescending(l => l.PIId).ThenByDescending(l => l.LineNo)
                        : query.OrderBy(l => l.PurchaseDiscountPct).ThenBy(l => l.PIId).ThenBy(l => l.LineNo);
                    break;

                case "retail":
                    query = desc
                        ? query.OrderByDescending(l => l.PriceRetail).ThenByDescending(l => l.PIId).ThenByDescending(l => l.LineNo)
                        : query.OrderBy(l => l.PriceRetail).ThenBy(l => l.PIId).ThenBy(l => l.LineNo);
                    break;

                case "batch":
                    query = desc
                        ? query.OrderByDescending(l => l.BatchNo).ThenByDescending(l => l.PIId).ThenByDescending(l => l.LineNo)
                        : query.OrderBy(l => l.BatchNo).ThenBy(l => l.PIId).ThenBy(l => l.LineNo);
                    break;

                case "expiry":
                    query = desc
                        ? query.OrderByDescending(l => l.Expiry).ThenByDescending(l => l.PIId).ThenByDescending(l => l.LineNo)
                        : query.OrderBy(l => l.Expiry).ThenBy(l => l.PIId).ThenBy(l => l.LineNo);
                    break;

                case "date":
                    query = desc
                        ? query.OrderByDescending(l => l.PurchaseInvoice.PIDate).ThenByDescending(l => l.PIId).ThenByDescending(l => l.LineNo)
                        : query.OrderBy(l => l.PurchaseInvoice.PIDate).ThenBy(l => l.PIId).ThenBy(l => l.LineNo);
                    break;

                case "piid":
                default:
                    query = desc
                        ? query.OrderByDescending(l => l.PIId).ThenByDescending(l => l.LineNo)
                        : query.OrderBy(l => l.PIId).ThenBy(l => l.LineNo);
                    break;
            }

            return query;
        }

        /// <summary>
        /// تحويل نص مثل "123" إلى int?، يرجع null لو التحويل فشل.
        /// </summary>
        private static int? TryParseNullableInt(string? value)
        {
            if (int.TryParse(value, out var i))
                return i;

            return null;
        }

        /// <summary>
        /// تحويل نص مركب "PIId:LineNo" إلى (PIId, LineNo).
        /// </summary>
        private static (int PIId, int LineNo)? ParseCompositeKey(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;

            var parts = input.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return null;

            if (!int.TryParse(parts[0], out var piId)) return null;
            if (!int.TryParse(parts[1], out var lineNo)) return null;

            return (piId, lineNo);
        }

        #endregion
    }
}
