using System;                                     // متغيرات التاريخ DateTime
using System.Collections.Generic;                 // List, Dictionary
using System.Globalization;                       // CultureInfo للتواريخ عند التصدير
using System.Linq;                                // LINQ: Where / OrderBy
using System.Linq.Expressions;                    // Expression<Func<...>>
using System.Text;                                // StringBuilder للتصدير
using System.Threading.Tasks;                     // async / await
using Microsoft.AspNetCore.Mvc;                   // Controller, IActionResult
using Microsoft.EntityFrameworkCore;              // AsNoTracking, Include, ToListAsync
using ERP.Data;                                   // AppDbContext الاتصال بقاعدة البيانات
using ERP.Infrastructure;                         // PagedResult + ApplySearchSort
using ERP.Models;                                 // CashPayment + Customer + Account

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إذون صرف النقدية (CashPayments)
    /// - CRUD كامل (إضافة / تعديل / تفاصيل / حذف).
    /// - شاشة Index بنظام القوائم الموحد (بحث + ترتيب + فلتر كود + فلتر تاريخ).
    /// - تصدير إلى CSV (Excel).
    /// - حذف جماعي + حذف الكل (يفضل استخدامها بحذر).
    /// </summary>
    public class CashPaymentsController : Controller
    {
        // كائن الاتصال بقاعدة البيانات
        private readonly AppDbContext _context;   // متغير: السياق الأساسي للتعامل مع الـ DB

        public CashPaymentsController(AppDbContext context)
        {
            _context = context;
        }

        // =========================================================
        // دالة خاصة: تجهيز الاستعلام الأساسي + الفلاتر + البحث + الترتيب
        // نستخدمها في Index و Export حتى لا نكرر الكود.
        // =========================================================
        private IQueryable<CashPayment> BuildQuery(
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
            // (1) الاستعلام الأساسي من جدول إذون الدفع مع ربط العميل والحسابات (بدون تتبّع لتحسين الأداء)
            IQueryable<CashPayment> q = _context.CashPayments
                .AsNoTracking()
                .Include(p => p.Customer)
                .Include(p => p.CashAccount)
                .Include(p => p.CounterAccount);

            // (2) فلتر كود من/إلى (نعتمد هنا على CashPaymentId كرقم الإذن)
            if (fromCode.HasValue)
                q = q.Where(p => p.CashPaymentId >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(p => p.CashPaymentId <= toCode.Value);

            // (3) فلتر التاريخ: نفلتر حسب تاريخ الإذن PaymentDate
            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                DateTime from = fromDate.Value;
                DateTime to = toDate.Value;

                q = q.Where(p => p.PaymentDate >= from && p.PaymentDate <= to);
            }

            // (4) خرائط البحث: نحدد الأعمدة النصية والرقمية للبحث الموحد

            // الحقول النصية (string) التى يمكن البحث فيها
            var stringFields =
                new Dictionary<string, Expression<Func<CashPayment, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["number"] = p => p.PaymentNumber,                                 // رقم المستند
                    ["desc"] = p => p.Description ?? "",                               // البيان
                    ["customer"] = p => p.Customer != null ? p.Customer.CustomerName : "",
                    ["cashAccount"] = p => p.CashAccount != null ? p.CashAccount.AccountName : "",
                    ["counterAccount"] = p => p.CounterAccount != null ? p.CounterAccount.AccountName : "",
                    ["posted"] = p => p.IsPosted ? "Posted" : "Draft"                  // حالة الترحيل كنص
                };

            // الحقول الرقمية (int) التى يمكن البحث فيها
            var intFields =
                new Dictionary<string, Expression<Func<CashPayment, int>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = p => p.CashPaymentId    // البحث برقم الإذن
                };

            // الحقول المسموح الترتيب عليها
            var orderFields =
                new Dictionary<string, Expression<Func<CashPayment, object>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["CashPaymentId"] = p => p.CashPaymentId,                   // رقم الإذن
                    ["PaymentNumber"] = p => p.PaymentNumber,                   // رقم المستند
                    ["PaymentDate"] = p => p.PaymentDate,                       // تاريخ الإذن
                    ["Amount"] = p => p.Amount,                                 // المبلغ
                    ["IsPosted"] = p => p.IsPosted,                             // حالة الترحيل
                    ["CreatedAt"] = p => p.CreatedAt,                           // تاريخ الإنشاء
                    ["UpdatedAt"] = p => p.UpdatedAt ?? DateTime.MinValue,      // آخر تعديل
                    ["CustomerName"] = p => p.Customer != null ? p.Customer.CustomerName : "",
                    ["CashAccountName"] = p => p.CashAccount != null ? p.CashAccount.AccountName : "",
                    ["CounterAccountName"] = p => p.CounterAccount != null ? p.CounterAccount.AccountName : ""
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
                defaultSortBy: "PaymentDate"     // الترتيب الافتراضي بتاريخ الإذن (الأحدث أولاً)
            );

            return q;
        }

        // =========================================================
        // Index — عرض قائمة إذون الدفع (نظام القوائم الموحد)
        // =========================================================
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",
            string? sort = "PaymentDate",
            string? dir = "desc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,    // من كود (CashPaymentId)
            int? toCode = null,      // إلى كود
            int page = 1,
            int pageSize = 50)
        {
            // تجهيز الاستعلام مع كل الفلاتر
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

            // إنشاء موديل التقسيم PagedResult
            var model = await PagedResult<CashPayment>.CreateAsync(q, page, pageSize);

            // حفظ قيم الفلترة الزمنية داخل الموديل (لنظام القوائم الموحد)
            model.UseDateRange = useDateRange;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            // تمرير القيم للـ ViewBag لاستخدامها في الواجهة
            ViewBag.Search = search ?? "";
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.Sort = sort ?? "PaymentDate";
            ViewBag.Dir = (dir?.ToLower() == "asc") ? "asc" : "desc";

            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;

            ViewBag.DateField = "PaymentDate";   // نستخدم تاريخ الإذن للفلترة
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;

            ViewBag.TotalCount = model.TotalCount; // إجمالي عدد الإذون

            return View(model); // يعرض Views/CashPayments/Index.cshtml
        }

        // =========================================================
        // Details — عرض تفاصيل إذن دفع واحد
        // =========================================================
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null || id <= 0)
                return BadRequest();

            var payment = await _context.CashPayments
                                        .Include(p => p.Customer)
                                        .Include(p => p.CashAccount)
                                        .Include(p => p.CounterAccount)
                                        .FirstOrDefaultAsync(p => p.CashPaymentId == id);

            if (payment == null)
                return NotFound();

            return View(payment);   // Views/CashPayments/Details.cshtml (الفورم العادي)
        }

        // (اختياري) لو حابب فورم Show موحّد ممكن نضيفه لاحقاً

        // =========================================================
        // Create — GET: عرض فورم إضافة إذن جديد
        // =========================================================
        public IActionResult Create()
        {
            // هنا لاحقاً ممكن نحمّل DropDown للحسابات والعملاء
            return View();
        }

        // =========================================================
        // Create — POST: حفظ إذن جديد
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CashPayment payment)
        {
            if (!ModelState.IsValid)
            {
                // لو فيه أخطاء في البيانات نرجع لنفس الفورم
                return View(payment);
            }

            // تعيين قيم الإنشاء
            payment.CreatedAt = DateTime.UtcNow;
            payment.IsPosted = false;       // مبدئياً غير مرحّل
            payment.PostedAt = null;
            payment.PostedBy = null;

            if (string.IsNullOrWhiteSpace(payment.CreatedBy))
            {
                // ممكن نستخدم اسم المستخدم الحالي لو النظام فيه Login
                payment.CreatedBy = User?.Identity?.Name ?? "System";
            }

            _context.Add(payment);
            await _context.SaveChangesAsync();

            TempData["Success"] = "تم حفظ إذن الدفع بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // Edit — GET: تعديل إذن دفع
        // =========================================================
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null || id <= 0)
                return BadRequest();

            var payment = await _context.CashPayments.FindAsync(id);
            if (payment == null)
                return NotFound();

            return View(payment);
        }

        // =========================================================
        // Edit — POST: حفظ التعديل
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, CashPayment payment)
        {
            if (id != payment.CashPaymentId)
                return BadRequest();

            if (!ModelState.IsValid)
            {
                return View(payment);
            }

            try
            {
                // نحدد أن الكائن تم تعديله
                payment.UpdatedAt = DateTime.UtcNow;

                _context.Update(payment);
                await _context.SaveChangesAsync();

                TempData["Success"] = "تم تعديل إذن الدفع بنجاح.";
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!CashPaymentExists(payment.CashPaymentId))
                    return NotFound();

                throw;
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // Delete — GET: تأكيد حذف إذن دفع
        // =========================================================
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null || id <= 0)
                return BadRequest();

            var payment = await _context.CashPayments
                                        .Include(p => p.Customer)
                                        .Include(p => p.CashAccount)
                                        .Include(p => p.CounterAccount)
                                        .FirstOrDefaultAsync(p => p.CashPaymentId == id);

            if (payment == null)
                return NotFound();

            return View(payment);
        }

        // =========================================================
        // DeleteConfirmed — POST: تنفيذ الحذف
        // =========================================================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var payment = await _context.CashPayments.FindAsync(id);
            if (payment == null)
            {
                TempData["Error"] = "إذن الدفع غير موجود.";
                return RedirectToAction(nameof(Index));
            }

            _context.CashPayments.Remove(payment);
            await _context.SaveChangesAsync();

            TempData["Success"] = "تم حذف إذن الدفع.";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // Export — تصدير قائمة الإذون إلى CSV (يفتح في Excel)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy = "all",
            string? sort = "PaymentDate",
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
            sb.AppendLine("CashPaymentId,PaymentNumber,PaymentDate,CustomerName,CashAccount,CounterAccount,Amount,IsPosted,CreatedAt,CreatedBy,PostedAt,PostedBy,Description");

            // كل صف إذن في سطر CSV
            foreach (var p in list)
            {
                string customerName = p.Customer?.CustomerName ?? "";
                string cashAccountName = p.CashAccount?.AccountName ?? "";
                string counterAccountName = p.CounterAccount?.AccountName ?? "";

                string line = string.Join(",",
                    p.CashPaymentId,
                    (p.PaymentNumber ?? "").Replace(",", " "),
                    p.PaymentDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    customerName.Replace(",", " "),
                    cashAccountName.Replace(",", " "),
                    counterAccountName.Replace(",", " "),
                    p.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                    p.IsPosted ? "1" : "0",
                    p.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    (p.CreatedBy ?? "").Replace(",", " "),
                    p.PostedAt.HasValue
                        ? p.PostedAt.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                        : "",
                    (p.PostedBy ?? "").Replace(",", " "),
                    (p.Description ?? "").Replace(",", " ")
                );

                sb.AppendLine(line);
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = "CashPayments.csv";
            const string contentType = "text/csv";

            return File(bytes, contentType, fileName);
        }

        // =========================================================
        // BulkDelete — حذف مجموعة من الإذون المحددة
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(int[] ids)
        {
            // لو المستخدم لم يحدد أى إذن
            if (ids == null || ids.Length == 0)
            {
                TempData["Error"] = "لم يتم اختيار أى إذن للحذف.";
                return RedirectToAction(nameof(Index));
            }

            // نحضر الإذون المطابقة للأرقام المختارة
            var payments = await _context.CashPayments
                                         .Where(p => ids.Contains(p.CashPaymentId))
                                         .ToListAsync();

            if (payments.Count == 0)
            {
                TempData["Error"] = "لم يتم العثور على الإذون المحددة.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _context.CashPayments.RemoveRange(payments);
                await _context.SaveChangesAsync();

                TempData["Success"] = $"تم حذف {payments.Count} من إذون الدفع المحددة.";
            }
            catch (DbUpdateException)
            {
                TempData["Error"] = "لا يمكن حذف بعض الإذون بسبب ارتباطها بحركات محاسبية أخرى.";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // DeleteAll — حذف جميع إذون الدفع
        // (غالباً تستخدم في بيئة تجريبية وليس في الإنتاج)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _context.CashPayments.ToListAsync();

            if (all.Count == 0)
            {
                TempData["Error"] = "لا توجد إذون دفع لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _context.CashPayments.RemoveRange(all);
                await _context.SaveChangesAsync();

                TempData["Success"] = "تم حذف جميع إذون الدفع.";
            }
            catch (DbUpdateException)
            {
                TempData["Error"] = "لا يمكن حذف جميع إذون الدفع بسبب وجود ارتباطات محاسبية.";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // دالة مساعدة: هل إذن الدفع موجود؟
        // =========================================================
        private bool CashPaymentExists(int id)
        {
            return _context.CashPayments.Any(e => e.CashPaymentId == id);
        }
    }
}
