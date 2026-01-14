using System;                                     // متغيرات التاريخ DateTime
using System.Collections.Generic;                 // Dictionary, List
using System.Globalization;                       // تنسيق التواريخ عند التصدير
using System.Linq;                                // LINQ: Where / OrderBy
using System.Linq.Expressions;                    // Expression<Func<...>>
using System.Text;                                // StringBuilder للتصدير
using System.Threading.Tasks;                     // async / await
using Microsoft.AspNetCore.Mvc;                   // Controller, IActionResult
using Microsoft.EntityFrameworkCore;              // AsNoTracking, Include, ToListAsync
using ERP.Data;                                   // AppDbContext الاتصال بقاعدة البيانات
using ERP.Infrastructure;                         // PagedResult + ApplySearchSort
using ERP.Models;                                 // LedgerEntry + Account + Customer

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر دفتر الأستاذ (LedgerEntries)
    /// شاشة قراءة فقط:
    /// - عرض القيود المحاسبية مع بحث/ترتيب/فلترة بالتاريخ والكود.
    /// - تصدير القيود إلى CSV.
    /// - حذف جماعي / حذف الكل (يُفضّل لبيئة تجريبية أو بإذن خاص).
    /// لا يوجد إنشاء/تعديل قيود من هنا؛ القيود تُنشأ من الشاشات الأخرى (فواتير، إيصالات، قيود يدوية).
    /// </summary>
    public class LedgerEntriesController : Controller
    {
        // كائن الاتصال بقاعدة البيانات
        private readonly AppDbContext _context;   // متغير: السياق الأساسي للتعامل مع الـ DB

        public LedgerEntriesController(AppDbContext context)
        {
            _context = context;
        }

        // =========================================================
        // دالة خاصة: تجهيز الاستعلام الأساسي + الفلاتر + البحث + الترتيب
        // نستخدمها في Index و Export حتى لا نكرر الكود.
        // =========================================================
        private IQueryable<LedgerEntry> BuildQuery(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            int? fromCode,
            int? toCode)
        {
            // (1) الاستعلام الأساسي من جدول القيود مع الحساب والعميل (بدون تتبّع لتحسين الأداء)
            IQueryable<LedgerEntry> q = _context.LedgerEntries
                .AsNoTracking()
                .Include(e => e.Account)
                .Include(e => e.Customer);

            // (2) فلتر كود من/إلى (نعتمد هنا على Id كرقم القيد)
            if (fromCode.HasValue)
                q = q.Where(e => e.Id >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(e => e.Id <= toCode.Value);

            // (3) فلتر التاريخ: نفلتر حسب تاريخ القيد EntryDate
            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                DateTime from = fromDate.Value;
                DateTime to = toDate.Value;

                q = q.Where(e => e.EntryDate >= from && e.EntryDate <= to);
            }

            // (4) خرائط البحث: نحدد الأعمدة النصية والرقمية للبحث الموحد

            // الحقول النصية (string) التى يمكن البحث فيها
            var stringFields =
                new Dictionary<string, Expression<Func<LedgerEntry, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["voucher"] = e => e.VoucherNo ?? "",                                         // رقم المستند
                    ["desc"] = e => e.Description ?? "",                                      // البيان
                    ["source"] = e => e.SourceType.ToString(),                                  // نوع المستند
                    ["account"] = e => e.Account != null ? e.Account.AccountName : "",           // اسم الحساب
                    ["customer"] = e => e.Customer != null ? e.Customer.CustomerName : ""         // اسم العميل/الطرف
                };

            // الحقول الرقمية (int) التى يمكن البحث فيها
            var intFields =
                new Dictionary<string, Expression<Func<LedgerEntry, int>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = e => e.Id                       // البحث برقم القيد
                };

            // الحقول المسموح الترتيب عليها
            var orderFields =
                new Dictionary<string, Expression<Func<LedgerEntry, object>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Id"] = e => e.Id,                                       // رقم القيد
                    ["EntryDate"] = e => e.EntryDate,                                // تاريخ القيد
                    ["SourceType"] = e => e.SourceType,                               // نوع المستند
                    ["VoucherNo"] = e => e.VoucherNo ?? "",                          // رقم المستند
                    ["SourceId"] = e => e.SourceId ?? 0,                            // معرّف المصدر
                    ["AccountId"] = e => e.AccountId,                                // رقم الحساب
                    ["AccountName"] = e => e.Account != null ? e.Account.AccountName : "",
                    ["CustomerName"] = e => e.Customer != null ? e.Customer.CustomerName : "",
                    ["Debit"] = e => e.Debit,                                    // مدين
                    ["Credit"] = e => e.Credit,                                   // دائن
                    ["Description"] = e => e.Description ?? ""                         // البيان
                };

            // (5) تطبيق منظومة البحث/الترتيب الموحدة
            q = q.ApplySearchSort(
                search: search,
                searchBy: searchBy,
                sort: sort,
                dir: dir,
                stringFields: stringFields,
                intFields: intFields,
                orderFields: orderFields,
                defaultSearchBy: "all",          // لو المستخدم لم يحدد نوع البحث
                defaultSortBy: "EntryDate"       // الترتيب الافتراضي بتاريخ القيد (الأحدث أولاً)
            );

            return q;
        }








        // =========================================================
        // Index — عرض قائمة القيود (نظام القوائم الموحد)
        // شاشة قراءة فقط لعرض دفتر الأستاذ.
        // ✅ إضافة: إجمالي المدين + إجمالي الدائن (يتفلتر مع الفلترة)
        // =========================================================
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",
            string? sort = "EntryDate",
            string? dir = "desc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,   // من كود (Id)
            int? toCode = null,     // إلى كود
            int page = 1,
            int pageSize = 50)
        {
            // =========================================================
            // 1) تجهيز الاستعلام مع كل الفلاتر (قبل الـ Paging)
            // =========================================================
            var q = BuildQuery(
                search,
                searchBy,
                sort,
                dir,
                useDateRange,
                fromDate,
                toDate,
                fromCode,
                toCode);

            // =========================================================
            // 2) حساب الإجماليات من نفس الاستعلام (بعد الفلاتر)
            // ✅ مهم: لازم قبل الـ PagedResult علشان ما تتحسبش على الصفحة بس
            // =========================================================
            // متغير: إجمالي المدين بعد الفلاتر
            decimal totalDebit = await q.SumAsync(e => (decimal?)e.Debit) ?? 0m;

            // متغير: إجمالي الدائن بعد الفلاتر
            decimal totalCredit = await q.SumAsync(e => (decimal?)e.Credit) ?? 0m;

            // متغير: صافي الحركة داخل الفلتر (مدين - دائن)
            decimal netBalance = totalDebit - totalCredit;

            // =========================================================
            // 3) إنشاء موديل التقسيم PagedResult (يعرض صفحة فقط)
            // =========================================================
            var model = await PagedResult<LedgerEntry>.CreateAsync(q, page, pageSize);

            // =========================================================
            // 4) حفظ قيم الفلاتر داخل الموديل (لنظام القوائم الموحد)
            // =========================================================
            model.UseDateRange = useDateRange;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            // =========================================================
            // 5) تمرير القيم للواجهة
            // =========================================================
            ViewBag.Search = search ?? "";
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.Sort = sort ?? "EntryDate";
            ViewBag.Dir = (dir?.ToLower() == "asc") ? "asc" : "desc";

            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;

            ViewBag.DateField = "EntryDate";       // متغير: اسم حقل التاريخ المستخدم في الفلترة
            ViewBag.Page = page;                   // متغير: رقم الصفحة الحالية
            ViewBag.PageSize = pageSize;           // متغير: حجم الصفحة

            ViewBag.TotalCount = model.TotalCount; // متغير: إجمالي عدد القيود بعد الفلاتر

            // ✅ الإجماليات التي نريد التأكد منها
            ViewBag.TotalDebit = totalDebit;       // متغير: إجمالي المدين بعد الفلاتر
            ViewBag.TotalCredit = totalCredit;     // متغير: إجمالي الدائن بعد الفلاتر
            ViewBag.NetBalance = netBalance;       // متغير: صافي الحركة داخل الفلتر

            return View(model); // يعرض Views/LedgerEntries/Index.cshtml
        }










        // =========================================================
        // Show — عرض تفاصيل سطر قيد واحد (قراءة فقط)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Show(int id)
        {
            if (id <= 0)
                return BadRequest(); // رقم غير صحيح

            // قراءة السطر مع الحساب والعميل (للعرض فقط)
            var entry = await _context.LedgerEntries
                                      .AsNoTracking()
                                      .Include(e => e.Account)
                                      .Include(e => e.Customer)
                                      .FirstOrDefaultAsync(e => e.Id == id);

            if (entry == null)
                return NotFound();

            return View(entry); // Views/LedgerEntries/Show.cshtml (نعمله لاحقاً بنفس نمط Show الثابت)
        }

        // =========================================================
        // Export — تصدير قائمة القيود إلى CSV (يفتح في Excel)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy = "all",
            string? sort = "EntryDate",
            string? dir = "desc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string format = "excel")   // excel | csv (الاثنين حالياً يخرجوا CSV
        {
            // نبني نفس الاستعلام المستخدم في Index لضمان نفس النتائج
            var q = BuildQuery(
                search,
                searchBy,
                sort,
                dir,
                useDateRange,
                fromDate,
                toDate,
                fromCode,
                toCode);

            var list = await q.ToListAsync();

            var sb = new StringBuilder();

            // عناوين الأعمدة في ملف CSV
            sb.AppendLine("Id,EntryDate,SourceType,VoucherNo,SourceId,AccountId,AccountCode,AccountName,CustomerId,CustomerName,Debit,Credit,Description");

            // كل صف قيد في سطر CSV
            foreach (var e in list)
            {
                string accountCode = e.Account?.AccountCode ?? "";
                string accountName = e.Account?.AccountName ?? "";
                string customerName = e.Customer?.CustomerName ?? "";

                string line = string.Join(",",
                    e.Id,
                    e.EntryDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    e.SourceType.ToString(),
                    (e.VoucherNo ?? "").Replace(",", " "),
                    e.SourceId?.ToString() ?? "",
                    e.AccountId,
                    accountCode.Replace(",", " "),
                    accountName.Replace(",", " "),
                    e.CustomerId?.ToString() ?? "",
                    customerName.Replace(",", " "),
                    e.Debit.ToString("0.00", CultureInfo.InvariantCulture),
                    e.Credit.ToString("0.00", CultureInfo.InvariantCulture),
                    (e.Description ?? "").Replace(",", " ")
                );

                sb.AppendLine(line);
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = "LedgerEntries.csv";
            const string contentType = "text/csv";

            return File(bytes, contentType, fileName);
        }

        // =========================================================
        // BulkDelete — حذف مجموعة من القيود المحددة
        // ملاحظة: يفضّل استخدامه في بيئة تجريبية أو بصلاحيات خاصة جداً.
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(int[] ids)
        {
            // لو المستخدم لم يحدد أى قيد
            if (ids == null || ids.Length == 0)
            {
                TempData["Error"] = "لم يتم اختيار أى قيد للحذف.";
                return RedirectToAction(nameof(Index));
            }

            // نحضر القيود المطابقة للأرقام المختارة
            var entries = await _context.LedgerEntries
                                        .Where(e => ids.Contains(e.Id))
                                        .ToListAsync();

            if (entries.Count == 0)
            {
                TempData["Error"] = "لم يتم العثور على القيود المحددة.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _context.LedgerEntries.RemoveRange(entries);
                await _context.SaveChangesAsync();

                TempData["Success"] = $"تم حذف {entries.Count} من القيود المحددة.";
            }
            catch (DbUpdateException)
            {
                // في حالة وجود قيود علاقات أو قيود أخرى
                TempData["Error"] = "لا يمكن حذف بعض القيود بسبب ارتباطها بتقارير أو جداول أخرى.";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // DeleteAll — حذف جميع القيود
        // تنبيه: يُفضّل استخدامه لتهيئة قاعدة البيانات في بيئة TEST فقط.
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _context.LedgerEntries.ToListAsync();

            if (all.Count == 0)
            {
                TempData["Error"] = "لا توجد قيود لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _context.LedgerEntries.RemoveRange(all);
                await _context.SaveChangesAsync();

                TempData["Success"] = "تم حذف جميع القيود من دفتر الأستاذ.";
            }
            catch (DbUpdateException)
            {
                TempData["Error"] = "لا يمكن حذف جميع القيود بسبب وجود ارتباطات محاسبية أخرى.";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
