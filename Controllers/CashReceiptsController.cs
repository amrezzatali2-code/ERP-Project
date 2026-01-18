using System;                                     // للتعامل مع التواريخ DateTime
using System.Collections.Generic;                 // القوائم Dictionary / List
using System.Globalization;                       // تنسيق التواريخ فى التصدير
using System.Linq;                                // أوامر LINQ مثل Where / OrderBy
using System.Linq.Expressions;                    // Expression<Func<...>>
using System.Text;                                // StringBuilder للتصدير
using System.Threading.Tasks;                     // async / await
using Microsoft.AspNetCore.Mvc;                   // Controller, IActionResult
using Microsoft.AspNetCore.Mvc.Rendering;         // SelectList للقوائم المنسدلة
using Microsoft.EntityFrameworkCore;              // AsNoTracking, Include, ToListAsync
using ERP.Data;                                   // AppDbContext الاتصال بقاعدة البيانات
using ERP.Infrastructure;                         // PagedResult + ApplySearchSort
using ERP.Models;                                 // CashReceipt + Account + Customer

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إذون استلام النقدية (CashReceipts)
    /// - نظام القوائم الموحد في Index (بحث + ترتيب + فلترة + حذف جماعي + تصدير).
    /// - CRUD كامل: Create / Edit / Details / Delete.
    /// </summary>
    public class CashReceiptsController : Controller
    {
        // كائن الاتصال بقاعدة البيانات
        private readonly AppDbContext _context;   // متغير: السياق الأساسي للتعامل مع الـ DB

        public CashReceiptsController(AppDbContext context)
        {
            _context = context;
        }

        // =========================================================
        // دالة مساعدة: تجهيز القوائم المنسدلة (الطرف + الحسابات)
        // تُستخدم فى Create و Edit (GET + POST لو حصل خطأ).
        // =========================================================
        private void PopulateDropdowns(int? customerId = null,
                                       int? cashAccountId = null,
                                       int? counterAccountId = null)
        {
            // قائمة العملاء / الأطراف
            ViewData["CustomerId"] = new SelectList(
                _context.Customers
                        .AsNoTracking()
                        .OrderBy(c => c.CustomerName),
                "CustomerId",
                "CustomerName",
                customerId
            );

            // حسابات نشطة للصندوق / البنك
            ViewData["CashAccountId"] = new SelectList(
                _context.Accounts
                        .AsNoTracking()
                        .Where(a => a.IsActive)
                        .OrderBy(a => a.AccountName),
                "AccountId",
                "AccountName",
                cashAccountId
            );

            // حسابات نشطة للطرف المقابل
            ViewData["CounterAccountId"] = new SelectList(
                _context.Accounts
                        .AsNoTracking()
                        .Where(a => a.IsActive)
                        .OrderBy(a => a.AccountName),
                "AccountId",
                "AccountName",
                counterAccountId
            );
        }

        // =========================================================
        // دالة خاصة: تجهيز الاستعلام الأساسي + الفلاتر + البحث + الترتيب
        // نستخدمها في Index و Export حتى لا نكرر الكود.
        // =========================================================
        private IQueryable<CashReceipt> BuildQuery(
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
            // (1) الاستعلام الأساسي من جدول إذون الاستلام مع ربط العميل والحسابات
            IQueryable<CashReceipt> q = _context.CashReceipts
                .AsNoTracking()
                .Include(r => r.Customer)
                .Include(r => r.CashAccount)
                .Include(r => r.CounterAccount);

            // (2) فلتر كود من/إلى (نعتمد هنا على CashReceiptId)
            if (fromCode.HasValue)
                q = q.Where(r => r.CashReceiptId >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(r => r.CashReceiptId <= toCode.Value);

            // (3) فلتر التاريخ: نفلتر حسب تاريخ الإذن ReceiptDate
            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                DateTime from = fromDate.Value;
                DateTime to = toDate.Value;

                q = q.Where(r => r.ReceiptDate >= from && r.ReceiptDate <= to);
            }

            // (4) خرائط البحث: نحدد الأعمدة النصية والرقمية للبحث الموحد

            // الحقول النصية (string) التى يمكن البحث فيها
            var stringFields =
                new Dictionary<string, Expression<Func<CashReceipt, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["receiptNumber"] = r => r.ReceiptNumber,                                                // رقم المستند
                    ["customer"] = r => r.Customer != null ? r.Customer.CustomerName : "",              // اسم الطرف
                    ["cashAccount"] = r => r.CashAccount != null ? r.CashAccount.AccountName : "",         // حساب الصندوق
                    ["counterAccount"] = r => r.CounterAccount != null ? r.CounterAccount.AccountName : "",   // حساب الطرف
                    ["description"] = r => r.Description ?? "",                                           // البيان
                    ["status"] = r => r.IsPosted ? "Posted" : "Draft"                                // حالة الترحيل كنص
                };

            // الحقول الرقمية (int) التى يمكن البحث فيها
            var intFields =
                new Dictionary<string, Expression<Func<CashReceipt, int>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = r => r.CashReceiptId   // البحث برقم الإذن
                };

            // الحقول المسموح الترتيب عليها
            var orderFields =
                new Dictionary<string, Expression<Func<CashReceipt, object>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["CashReceiptId"] = r => r.CashReceiptId,                      // رقم الإذن
                    ["ReceiptNumber"] = r => r.ReceiptNumber,                      // رقم المستند
                    ["ReceiptDate"] = r => r.ReceiptDate,                        // تاريخ الإذن
                    ["CustomerName"] = r => r.Customer != null ? r.Customer.CustomerName : "",
                    ["CashAccount"] = r => r.CashAccount != null ? r.CashAccount.AccountName : "",
                    ["CounterAccount"] = r => r.CounterAccount != null ? r.CounterAccount.AccountName : "",
                    ["Amount"] = r => r.Amount,                             // المبلغ
                    ["IsPosted"] = r => r.IsPosted,                           // الترحيل
                    ["CreatedAt"] = r => r.CreatedAt,                          // تاريخ الإنشاء
                    ["UpdatedAt"] = r => r.UpdatedAt ?? DateTime.MinValue      // آخر تعديل
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
                defaultSearchBy: "all",         // لو المستخدم لم يحدد نوع البحث
                defaultSortBy: "ReceiptDate"    // الترتيب الافتراضي بتاريخ الإذن (من الأحدث للأقدم)
            );

            return q;
        }

        // =========================================================
        // Index — عرض قائمة إذون الاستلام (نظام القوائم الموحد)
        // =========================================================
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",
            string? sort = "ReceiptDate",
            string? dir = "desc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,   // من كود (CashReceiptId)
            int? toCode = null,     // إلى كود
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

            // إجمالي المبلغ فى كل النتائج (للسطر الإجمالي أسفل الجدول)
            var totalAmount = await q
                .Select(r => (decimal?)r.Amount)
                .SumAsync() ?? 0m;

            // إنشاء موديل التقسيم PagedResult
            var model = await PagedResult<CashReceipt>.CreateAsync(q, page, pageSize);

            // حفظ قيم الفلترة الزمنية داخل الموديل (لنظام القوائم الموحد)
            model.UseDateRange = useDateRange;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            // تمرير القيم للـ ViewBag لاستخدامها في الواجهة
            ViewBag.Search = search ?? "";
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.Sort = sort ?? "ReceiptDate";
            ViewBag.Dir = (dir?.ToLower() == "asc") ? "asc" : "desc";

            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;

            ViewBag.DateField = "ReceiptDate";       // نستخدم تاريخ الإذن للفلترة
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;

            ViewBag.TotalCount = model.TotalCount;   // إجمالي عدد الإذون
            ViewBag.TotalAmount = totalAmount;       // إجمالي المبلغ فى النتائج

            return View(model); // يعرض Views/CashReceipts/Index.cshtml
        }

        // =========================================================
        // Details — عرض تفاصيل إذن واحد (النموذج الكلاسيك)
        // =========================================================
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
                return NotFound();

            var cashReceipt = await _context.CashReceipts
                .Include(c => c.Customer)
                .Include(c => c.CashAccount)
                .Include(c => c.CounterAccount)
                .FirstOrDefaultAsync(m => m.CashReceiptId == id);

            if (cashReceipt == null)
                return NotFound();

            return View(cashReceipt);
        }

        // =========================================================
        // Show — فورم عرض محسّن يمكن استخدامه لاحقاً (نفس Data الـ Details حالياً)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Show(int id)
        {
            var cashReceipt = await _context.CashReceipts
                .Include(c => c.Customer)
                .Include(c => c.CashAccount)
                .Include(c => c.CounterAccount)
                .FirstOrDefaultAsync(m => m.CashReceiptId == id);

            if (cashReceipt == null)
                return NotFound();

            return View(cashReceipt);   // Views/CashReceipts/Show.cshtml
        }

        // =========================================================
        // Create — إضافة إذن استلام جديد
        // GET: يعرض الفورم، POST: يحفظ فى قاعدة البيانات.
        // =========================================================

        // GET: CashReceipts/Create
        public IActionResult Create(int? customerId = null)
        {
            PopulateDropdowns(customerId);  // تجهيز القوائم المنسدلة مع اختيار العميل إذا تم تمريره
            return View();
        }

        // POST: CashReceipts/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("CashReceiptId,ReceiptNumber,ReceiptDate,CustomerId,CashAccountId,CounterAccountId,Amount,Description")]
                                                CashReceipt cashReceipt)
        {
            if (ModelState.IsValid)
            {
                // تعبئة بيانات التتبع
                cashReceipt.CreatedAt = DateTime.Now;         // تاريخ الإنشاء
                // cashReceipt.CreatedBy = User?.Identity?.Name; // يمكن تفعيلها بعد إضافة نظام المستخدمين

                _context.Add(cashReceipt);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            // لو هناك خطأ نرجّع القوائم المنسدلة
            PopulateDropdowns(cashReceipt.CustomerId, cashReceipt.CashAccountId, cashReceipt.CounterAccountId);
            return View(cashReceipt);
        }

        // =========================================================
        // Edit — تعديل إذن موجود
        // =========================================================

        // GET: CashReceipts/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
                return NotFound();

            var cashReceipt = await _context.CashReceipts.FindAsync(id);
            if (cashReceipt == null)
                return NotFound();

            PopulateDropdowns(cashReceipt.CustomerId, cashReceipt.CashAccountId, cashReceipt.CounterAccountId);
            return View(cashReceipt);
        }

        // POST: CashReceipts/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id,
            [Bind("CashReceiptId,ReceiptNumber,ReceiptDate,CustomerId,CashAccountId,CounterAccountId,Amount,Description")]
            CashReceipt cashReceipt)
        {
            if (id != cashReceipt.CashReceiptId)
                return NotFound();

            if (!ModelState.IsValid)
            {
                PopulateDropdowns(cashReceipt.CustomerId, cashReceipt.CashAccountId, cashReceipt.CounterAccountId);
                return View(cashReceipt);
            }

            // نجيب السجل الأصلي من قاعدة البيانات ونحدّث الحقول المسموح بها
            var existing = await _context.CashReceipts.FindAsync(id);
            if (existing == null)
                return NotFound();

            existing.ReceiptNumber = cashReceipt.ReceiptNumber;
            existing.ReceiptDate = cashReceipt.ReceiptDate;
            existing.CustomerId = cashReceipt.CustomerId;
            existing.CashAccountId = cashReceipt.CashAccountId;
            existing.CounterAccountId = cashReceipt.CounterAccountId;
            existing.Amount = cashReceipt.Amount;
            existing.Description = cashReceipt.Description;
            existing.UpdatedAt = DateTime.Now;    // تحديث تاريخ آخر تعديل

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!CashReceiptExists(cashReceipt.CashReceiptId))
                    return NotFound();
                else
                    throw;
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // Delete — حذف إذن واحد (عن طريق شاشة التأكيد)
        // =========================================================

        // GET: CashReceipts/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
                return NotFound();

            var cashReceipt = await _context.CashReceipts
                .Include(c => c.Customer)
                .Include(c => c.CashAccount)
                .Include(c => c.CounterAccount)
                .FirstOrDefaultAsync(m => m.CashReceiptId == id);

            if (cashReceipt == null)
                return NotFound();

            return View(cashReceipt);
        }

        // POST: CashReceipts/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var cashReceipt = await _context.CashReceipts.FindAsync(id);
            if (cashReceipt != null)
            {
                _context.CashReceipts.Remove(cashReceipt);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // Export — تصدير قائمة إذون الاستلام إلى CSV (يفتح فى Excel)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy = "all",
            string? sort = "ReceiptDate",
            string? dir = "desc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string format = "excel")   // excel | csv (الاتنين حالياً CSV
        {
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

            // عناوين الأعمدة
            sb.AppendLine("CashReceiptId,ReceiptNumber,ReceiptDate,CustomerName,CashAccount,CounterAccount,Amount,IsPosted,CreatedAt,UpdatedAt,Description");

            foreach (var r in list)
            {
                string customerName = r.Customer?.CustomerName ?? "";
                string cashAcc = r.CashAccount?.AccountName ?? "";
                string counterAcc = r.CounterAccount?.AccountName ?? "";

                string line = string.Join(",",
                    r.CashReceiptId,
                    (r.ReceiptNumber ?? "").Replace(",", " "),
                    r.ReceiptDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    customerName.Replace(",", " "),
                    cashAcc.Replace(",", " "),
                    counterAcc.Replace(",", " "),
                    r.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                    r.IsPosted ? "1" : "0",
                    r.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    r.UpdatedAt.HasValue
                        ? r.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                        : "",
                    (r.Description ?? "").Replace(",", " ")
                );

                sb.AppendLine(line);
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = "CashReceipts.csv";
            const string contentType = "text/csv";

            return File(bytes, contentType, fileName);
        }

        // =========================================================
        // BulkDelete — حذف مجموعة من الإذون المحددة (يستخدم من زر "حذف المحدد")
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(int[] ids)
        {
            if (ids == null || ids.Length == 0)
            {
                TempData["Error"] = "لم يتم اختيار أى إذن للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var receipts = await _context.CashReceipts
                                         .Where(r => ids.Contains(r.CashReceiptId))
                                         .ToListAsync();

            if (receipts.Count == 0)
            {
                TempData["Error"] = "لم يتم العثور على الإذون المحددة.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _context.CashReceipts.RemoveRange(receipts);
                await _context.SaveChangesAsync();

                TempData["Success"] = $"تم حذف {receipts.Count} من إذون الاستلام المحددة.";
            }
            catch (DbUpdateException)
            {
                TempData["Error"] = "لا يمكن حذف بعض الإذون بسبب ارتباطها بقيود محاسبية أو جداول أخرى.";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // DeleteAll — حذف جميع إذون الاستلام (للبيئة التجريبية فقط!)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _context.CashReceipts.ToListAsync();

            if (all.Count == 0)
            {
                TempData["Error"] = "لا توجد إذون لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _context.CashReceipts.RemoveRange(all);
                await _context.SaveChangesAsync();

                TempData["Success"] = "تم حذف جميع إذون الاستلام.";
            }
            catch (DbUpdateException)
            {
                TempData["Error"] = "لا يمكن حذف جميع الإذون بسبب وجود ارتباطات محاسبية أخرى.";
            }

            return RedirectToAction(nameof(Index));
        }

        // دالة مساعدة للتأكد من وجود السجل
        private bool CashReceiptExists(int id)
        {
            return _context.CashReceipts.Any(e => e.CashReceiptId == id);
        }
    }
}
