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
        [HttpGet]
        public async Task<IActionResult> Index(
            string? search,                 // نص البحث المكتوب في صندوق البحث
            string? searchBy = "prod",    // prod | code | user | reason
            string? sort = "date",    // date | prod | code | old | new | user
            string? dir = "desc",    // asc | desc
            int page = 1,         // رقم الصفحة الحالية
            int pageSize = 50,        // عدد السطور في الصفحة
            bool useDateRange = false,     // هل فلترة التاريخ مفعّلة؟
            DateTime? fromDate = null,      // من تاريخ (ChangeDate)
            DateTime? toDate = null       // إلى تاريخ
        )
        {
            // (1) مصدر البيانات — IQueryable للسماح بالبناء التدريجي
            IQueryable<ProductPriceHistory> q =
                _ctx.ProductPriceHistories
                    .AsNoTracking()
                    .Include(h => h.Product);   // تضمين بيانات الصنف لعرض الاسم

            // تجهيز قيم الفلاتر بعد التنظيف
            var term = (search ?? string.Empty).Trim();                // نص البحث بعد إزالة المسافات
            var sb = (searchBy ?? "prod").Trim().ToLowerInvariant(); // حقل البحث
            var so = (sort ?? "date").Trim().ToLowerInvariant();     // حقل الترتيب
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase); // هل الترتيب تنازلي؟

            bool hasSearch = !string.IsNullOrEmpty(term);              // هل يوجد نص بحث فعلاً؟

            // =====================================================
            // (2) البحث (اختياري حسب searchBy)
            // =====================================================
            if (hasSearch)
            {
                switch (sb)
                {
                    case "code":     // البحث برقم الصنف (ProdId كـ int)
                        if (int.TryParse(term, out int code))
                        {
                            q = q.Where(h => h.ProdId == code);
                        }
                        else
                        {
                            // لو المستخدم كتب نص مش رقم → لا توجد نتائج
                            q = q.Where(h => 1 == 0);
                        }
                        break;

                    case "user":     // البحث باسم المستخدم الذي غيّر السعر
                        q = q.Where(h => h.ChangedBy != null &&
                                         h.ChangedBy.Contains(term));
                        break;

                    case "reason":   // البحث في سبب التغيير
                        q = q.Where(h => h.Reason != null &&
                                         h.Reason.Contains(term));
                        break;

                    case "prod":     // البحث باسم الصنف (أولوية) أو برقم الصنف كنص
                    default:
                        q = q.Where(h =>
                               (h.Product != null &&
                                h.Product.ProdName != null &&
                                h.Product.ProdName.Contains(term))    // اسم الصنف
                            || h.ProdId.ToString().Contains(term));  // رقم الصنف بعد تحويله لنص
                        break;
                }
            }

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
            // (4) الترتيب
            // =====================================================
            switch (so)
            {
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
            if (pageSize < 1) pageSize = 50;

            int total = await q.CountAsync();                               // إجمالي السجلات بعد الفلترة
            var items = await q.Skip((page - 1) * pageSize)                 // تخطي الصفوف السابقة
                               .Take(pageSize)                              // أخذ عدد الصفوف المطلوبة
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

            // خيارات البحث (ابحث في)
            ViewBag.SearchOptions = new[]
            {
                new SelectListItem("اسم الصنف", "prod",   sb == "prod"),
                new SelectListItem("كود الصنف", "code",   sb == "code"),
                new SelectListItem("المستخدم", "user",   sb == "user"),
                new SelectListItem("سبب التغيير", "reason", sb == "reason"),
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

            // تمرير بعض القيم للواجهة (لو احتاجناها مباشرة من ViewBag)
            ViewBag.Search = term;
            ViewBag.SearchBy = sb;
            ViewBag.Sort = so;
            ViewBag.Dir = desc ? "desc" : "asc";
            ViewBag.PageSize = pageSize;
            ViewBag.UseDateRange = useDateRange;
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;

            return View(model);
        }

        // =========================
        // Export — تصدير سجل تغيّرات الأسعار (Excel أو CSV)
        // =========================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string searchBy = "prod",   // prod | code | user | reason
            string sort = "date",   // date | prod | code | old | new | user
            string dir = "desc",   // asc | desc
            string? format = "excel"   // excel | csv
        )
        {
            // (1) مصدر البيانات — مع تضمين اسم الصنف
            IQueryable<ProductPriceHistory> q =
                _ctx.ProductPriceHistories
                    .AsNoTracking()
                    .Include(h => h.Product);   // للوصول لاسم الصنف

            // (2) البحث — نفس منطق Index
            var term = (search ?? string.Empty).Trim();          // متغير: نص البحث بعد التنظيف
            bool hasSearch = !string.IsNullOrEmpty(term);

            if (hasSearch)
            {
                switch (searchBy.ToLowerInvariant())
                {
                    case "code":     // البحث برقم الصنف (ProdId كـ int)
                        if (int.TryParse(term, out int code))
                        {
                            q = q.Where(h => h.ProdId == code);
                        }
                        else
                        {
                            // لو مش رقم → لا نتائج
                            q = q.Where(h => 1 == 0);
                        }
                        break;

                    case "user":     // البحث باسم المستخدم
                        q = q.Where(h => h.ChangedBy != null &&
                                         h.ChangedBy.Contains(term));
                        break;

                    case "reason":   // البحث في سبب التغيير
                        q = q.Where(h => h.Reason != null &&
                                         h.Reason.Contains(term));
                        break;

                    case "prod":     // البحث باسم الصنف أو رقم الصنف كنص
                    default:
                        q = q.Where(h =>
                               (h.Product != null &&
                                h.Product.ProdName != null &&
                                h.Product.ProdName.Contains(term))   // اسم الصنف
                            || h.ProdId.ToString().Contains(term));   // كود الصنف كنص
                        break;
                }
            }

            // (3) الترتيب — نفس منطق Index
            bool desc = dir.Equals("desc", StringComparison.OrdinalIgnoreCase);  // متغير: هل الترتيب تنازلي؟

            switch (sort.ToLowerInvariant())
            {
                case "prod":   // ترتيب باسم الصنف
                    q = (desc
                            ? q.OrderByDescending(h => h.Product != null ? h.Product.ProdName : "")
                            : q.OrderBy(h => h.Product != null ? h.Product.ProdName : ""))
                        .ThenByDescending(h => h.ChangeDate);   // كسر التعادل بالتاريخ الأحدث
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
                var fileNameCsv = $"ProductPriceHistory_{DateTime.Now:yyyyMMdd_HHmm}_csv.csv";

                return File(bytes, "text/csv; charset=utf-8", fileNameCsv);
            }

            // =====================================
            // الفرع الثاني: تصدير Excel (XLSX)
            // =====================================
            using var workbook = new XLWorkbook();                      // متغير: ملف Excel
            var ws = workbook.Worksheets.Add("PriceHistory");           // متغير: ورقة العمل

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

            var fileNameXlsx = $"ProductPriceHistory_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
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



        // ==================== التصدير (Export) ====================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string format = "excel")
        {
            // كويرى الأساس مع ربط الصنف
            var query = _ctx.ProductPriceHistories
                .Include(r => r.Product)
                .AsNoTracking()
                .AsQueryable();

            searchBy = string.IsNullOrWhiteSpace(searchBy) ? "prod" : searchBy.ToLowerInvariant();
            dir = string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase) ? "asc" : "desc";
            search = search?.Trim();

            // ===== نفس منطق البحث باختصار =====
            if (!string.IsNullOrWhiteSpace(search))
            {
                switch (searchBy)
                {
                    case "prod":
                        query = query.Where(r =>
                            (r.Product != null && r.Product.ProdName.Contains(search)) ||
                            r.ProdId.ToString().Contains(search));
                        break;

                    case "user":
                        query = query.Where(r => r.ChangedBy!.Contains(search));
                        break;

                    case "reason":
                        query = query.Where(r => r.Reason!.Contains(search));
                        break;

                    case "date":
                        query = query.Where(r =>
                            r.ChangeDate.ToString("yyyy/MM/dd").Contains(search));
                        break;

                    case "all":
                    default:
                        query = query.Where(r =>
                            (r.Product != null && r.Product.ProdName.Contains(search)) ||
                            r.ProdId.ToString().Contains(search) ||
                            (r.ChangedBy != null && r.ChangedBy.Contains(search)) ||
                            (r.Reason != null && r.Reason.Contains(search)) ||
                            r.ChangeDate.ToString("yyyy/MM/dd").Contains(search));
                        break;
                }
            }

            // ===== فلترة التاريخ/الوقت =====
            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                var from = fromDate.Value;
                var to = toDate.Value;

                query = query.Where(r => r.ChangeDate >= from && r.ChangeDate <= to);
            }

            // ===== الترتيب =====
            sort = string.IsNullOrWhiteSpace(sort) ? "date" : sort.ToLowerInvariant();

            query = (sort, dir) switch
            {
                ("prod", "asc") => query.OrderBy(r => r.Product!.ProdName),
                ("prod", "desc") => query.OrderByDescending(r => r.Product!.ProdName),

                ("oldprice", "asc") => query.OrderBy(r => r.OldPrice),
                ("oldprice", "desc") => query.OrderByDescending(r => r.OldPrice),

                ("newprice", "asc") => query.OrderBy(r => r.NewPrice),
                ("newprice", "desc") => query.OrderByDescending(r => r.NewPrice),

                ("user", "asc") => query.OrderBy(r => r.ChangedBy),
                ("user", "desc") => query.OrderByDescending(r => r.ChangedBy),

                ("reason", "asc") => query.OrderBy(r => r.Reason),
                ("reason", "desc") => query.OrderByDescending(r => r.Reason),

                // الافتراضى: التاريخ
                (_, "asc") => query.OrderBy(r => r.ChangeDate),
                _ => query.OrderByDescending(r => r.ChangeDate)
            };

            var list = await query.ToListAsync();

            // ===== تكوين CSV بسيط يُفتح في Excel =====
            var sb = new StringBuilder();
            sb.AppendLine("ChangeDate,Product,OldPrice,NewPrice,User,Reason"); // عناوين الأعمدة

            foreach (var r in list)
            {
                var prodName = r.Product?.ProdName ?? $"ProdId {r.ProdId}";
                var user = r.ChangedBy ?? "";
                var reason = r.Reason ?? "";

                // نهرب الفواصل بعلامات اقتباس
                string LineEscape(string s) =>
                    "\"" + s.Replace("\"", "\"\"") + "\"";

                sb.AppendLine(string.Join(",",
                    r.ChangeDate.ToString("yyyy-MM-dd HH:mm"),
                    LineEscape(prodName),
                    r.OldPrice.ToString("0.00"),
                    r.NewPrice.ToString("0.00"),
                    LineEscape(user),
                    LineEscape(reason)
                ));
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());

            // نفس المحتوى CSV، بس نغير الامتداد حسب الاختيار
            format = format?.ToLowerInvariant() ?? "excel";
            var fileName = format == "csv"
                ? "ProductPriceHistory.csv"
                : "ProductPriceHistory.xlsx";

            return File(bytes, "text/csv", fileName);
        }


    }
}
