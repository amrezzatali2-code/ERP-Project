using System;                                     // متغيرات التاريخ DateTime
using System.Collections.Generic;                 // List, Dictionary
using System.Globalization;                       // CultureInfo للتواريخ عند التصدير
using System.Linq;                                // LINQ: Where / OrderBy
using System.Linq.Expressions;                    // Expression<Func<...>>
using System.Text;                                // StringBuilder للتصدير
using System.Threading.Tasks;                     // async / await
using Microsoft.AspNetCore.Mvc;                   // Controller, IActionResult
using Microsoft.AspNetCore.Mvc.Rendering;         // SelectList
using Microsoft.EntityFrameworkCore;              // AsNoTracking, Include, ToListAsync
using ERP.Data;                                   // AppDbContext الاتصال بقاعدة البيانات
using ERP.Infrastructure;                         // PagedResult + ApplySearchSort + UserActivityLogger
using ERP.Models;                                 // CashPayment + Customer + Account
using ERP.Services;                               // ILedgerPostingService

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
        private readonly ILedgerPostingService _ledgerPostingService; // متغير: خدمة الترحيل المحاسبي
        private readonly IUserActivityLogger _activityLogger; // متغير: تسجيل نشاط المستخدمين

        public CashPaymentsController(AppDbContext context, ILedgerPostingService ledgerPostingService, IUserActivityLogger activityLogger)
        {
            _context = context;
            _ledgerPostingService = ledgerPostingService;
            _activityLogger = activityLogger;
        }

        // =========================================================
        // دالة مساعدة: تجهيز القوائم المنسدلة (الطرف + الحسابات)
        // تُستخدم فى Create و Edit (GET + POST لو حصل خطأ).
        // =========================================================
        private async Task PopulateDropdownsAsync(int? customerId = null,
                                                  int? cashAccountId = null,
                                                  int? counterAccountId = null)
        {
            // قائمة العملاء / الأطراف مع AccountId في data attribute
            var customers = await _context.Customers
                .AsNoTracking()
                .Where(c => c.IsActive == true)
                .Include(c => c.Account)
                .OrderBy(c => c.CustomerName)
                .Select(c => new
                {
                    c.CustomerId,
                    c.CustomerName,
                    AccountId = c.AccountId ?? 0
                })
                .ToListAsync();

            var customerItems = customers.Select(c => new SelectListItem
            {
                Value = c.CustomerId.ToString(),
                Text = c.CustomerName ?? "",
                Selected = customerId.HasValue && c.CustomerId == customerId.Value
            }).ToList();

            ViewData["CustomerId"] = new SelectList(customerItems, "Value", "Text", customerId?.ToString());
            ViewData["CustomersWithAccounts"] = customers.ToDictionary(c => c.CustomerId, c => c.AccountId);

            // حسابات نشطة للصندوق / البنك
            var cashAccounts = await _context.Accounts
                    .AsNoTracking()
                    .Where(a => a.IsActive)
                    .OrderBy(a => a.AccountName)
                    .Select(a => new { a.AccountId, a.AccountName })
                    .ToListAsync();
            
            var cashAccountItems = cashAccounts.Select(a => new SelectListItem
            {
                Value = a.AccountId.ToString(),
                Text = a.AccountName ?? "",
                Selected = cashAccountId.HasValue && cashAccountId.Value == a.AccountId
            }).ToList();
            
            ViewData["CashAccountId"] = new SelectList(cashAccountItems, "Value", "Text", cashAccountId);

            // حسابات نشطة للطرف المقابل
            var counterAccounts = await _context.Accounts
                    .AsNoTracking()
                    .Where(a => a.IsActive)
                    .OrderBy(a => a.AccountName)
                    .Select(a => new { a.AccountId, a.AccountName })
                    .ToListAsync();
            
            var counterAccountItems = counterAccounts.Select(a => new SelectListItem
            {
                Value = a.AccountId.ToString(),
                Text = a.AccountName ?? "",
                Selected = counterAccountId.HasValue && counterAccountId.Value == a.AccountId
            }).ToList();
            
            ViewData["CounterAccountId"] = new SelectList(counterAccountItems, "Value", "Text", counterAccountId);
        }

        // دالة بديلة بدون async (للاستخدام في Post بدون await)
        private void PopulateDropdowns(int? customerId = null,
                                       int? cashAccountId = null,
                                       int? counterAccountId = null)
        {
            // قائمة العملاء / الأطراف
            ViewData["CustomerId"] = new SelectList(
                _context.Customers
                        .AsNoTracking()
                        .Where(c => c.IsActive == true)
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
        // دالة مساعدة: جلب AccountId للعميل (للاستخدام في AJAX)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> GetCustomerAccount(int customerId)
        {
            var customer = await _context.Customers
                .AsNoTracking()
                .Where(c => c.CustomerId == customerId)
                .Select(c => new { c.AccountId })
                .FirstOrDefaultAsync();

            if (customer == null || !customer.AccountId.HasValue)
            {
                return Json(new { success = false, message = "العميل غير موجود أو غير مربوط بحساب محاسبي." });
            }

            return Json(new { success = true, accountId = customer.AccountId.Value });
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

        // =========================================================
        // Show — عرض إذن الدفع للطباعة
        // =========================================================
        public async Task<IActionResult> Show(int id)
        {
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
        // Create — GET: عرض فورم إضافة إذن جديد
        // =========================================================
        public async Task<IActionResult> Create(int? id = null, int? customerId = null)
        {
            CashPayment model;
            
            // ✅ إذا كان id موجود، نحمّل الإذن الموجود (للتعديل)
            if (id.HasValue && id.Value > 0)
            {
                model = await _context.CashPayments
                    .Include(p => p.Customer)
                    .ThenInclude(c => c.Account)
                    .FirstOrDefaultAsync(p => p.CashPaymentId == id.Value);
                
                if (model == null)
                    return NotFound();
                
                // ✅ إذا كان العميل محددًا، نحفظ هذه المعلومة للواجهة
                if (model.CustomerId.HasValue)
                    ViewBag.LockCustomer = true;
            }
            else
            {
                // ✅ جلب حساب الخزينة الافتراضي (كود 1101) أو أول حساب خزينة/صندوق
                var defaultCashAccount = await _context.Accounts
                    .AsNoTracking()
                    .Where(a => a.IsActive && 
                               (a.AccountCode == "1101" || 
                                a.AccountName.Contains("خزينة") || 
                                a.AccountName.Contains("صندوق")))
                    .OrderBy(a => a.AccountCode == "1101" ? 0 : 1) // أولوية لكود 1101
                    .ThenBy(a => a.AccountName)
                    .Select(a => new { a.AccountId })
                    .FirstOrDefaultAsync();

                model = new CashPayment
                {
                    PaymentDate = DateTime.Now.Date,
                    IsPosted = false,
                    Status = "غير مرحلة",
                    CashAccountId = defaultCashAccount?.AccountId ?? 0 // ✅ تعيين حساب الصندوق الافتراضي
                };
            }

            // ✅ إذا جاء من صفحة "حجم تعامل عميل" (customerId موجود) ولم يكن id موجود
            if (!id.HasValue && customerId.HasValue && customerId.Value > 0)
            {
                var customer = await _context.Customers
                    .AsNoTracking()
                    .Include(c => c.Account)
                    .FirstOrDefaultAsync(c => c.CustomerId == customerId.Value);

                if (customer != null)
                {
                    model.CustomerId = customer.CustomerId;
                    // ✅ حساب الطرف = حساب العميل تلقائيًا
                    if (customer.AccountId.HasValue)
                    {
                        model.CounterAccountId = customer.AccountId.Value;
                    }
                    // ✅ البيان الافتراضي
                    model.Description = $"دفع للعميل {customer.CustomerName}";
                    // ✅ قفل العميل وحساب الطرف في الواجهة
                    ViewBag.LockCustomer = true;
                }
            }

            await PopulateDropdownsAsync(model.CustomerId, model.CashAccountId > 0 ? (int?)model.CashAccountId : null, model.CounterAccountId > 0 ? (int?)model.CounterAccountId : null);
            return View(model);
        }

        // =========================================================
        // Create — POST: حفظ إذن جديد أو تعديل إذن موجود
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("CashPaymentId,PaymentDate,CustomerId,CashAccountId,CounterAccountId,Amount,Description")]
                                                CashPayment payment)
        {
            // ✅ تجاهل خطأ التحقق لـ PaymentNumber لأنه سيتم توليده تلقائياً
            ModelState.Remove(nameof(CashPayment.PaymentNumber));
            
            // ✅ تسجيل القيم المرسلة للتحقق
            System.Diagnostics.Debug.WriteLine($"DEBUG: CashPaymentId={payment.CashPaymentId}, CashAccountId={payment.CashAccountId}, CounterAccountId={payment.CounterAccountId}, Amount={payment.Amount}");

            if (payment.CustomerId.HasValue && payment.CustomerId.Value > 0)
            {
                var cust = await _context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.CustomerId == payment.CustomerId.Value);
                if (cust != null && !cust.IsActive)
                    ModelState.AddModelError(nameof(CashPayment.CustomerId), "لا يمكن التعامل مع عميل غير نشط. يرجى تفعيل العميل أولاً.");
            }
            
            if (!ModelState.IsValid)
            {
                // لو فيه أخطاء في البيانات نرجع لنفس الفورم
                await PopulateDropdownsAsync(payment.CustomerId, payment.CashAccountId, payment.CounterAccountId);
                if (payment.CustomerId.HasValue)
                    ViewBag.LockCustomer = true;
                return View(payment);
            }

            try
            {
                // =========================================================
                // إذا كان CashPaymentId > 0، فهذا تعديل
                // =========================================================
                if (payment.CashPaymentId > 0)
                {
                    return await Edit(payment.CashPaymentId, payment);
                }

                // =========================================================
                // إنشاء إذن جديد
                // =========================================================
                // تعيين قيم الإنشاء
                payment.CreatedAt = DateTime.UtcNow;
                payment.IsPosted = false;       // مبدئياً غير مرحّل
                payment.PostedAt = null;
                payment.PostedBy = null;
                payment.Status = "غير مرحلة";   // ✅ الحالة الافتراضية

                if (string.IsNullOrWhiteSpace(payment.CreatedBy))
                {
                    payment.CreatedBy = User?.Identity?.Name ?? "System";
                }

                // ✅ توليد رقم المستند من CashPaymentId بعد الحفظ
                _context.Add(payment);
                await _context.SaveChangesAsync();
                
                // ✅ الآن CashPaymentId موجود بعد الحفظ
                payment.PaymentNumber = payment.CashPaymentId.ToString();
                await _context.SaveChangesAsync();

                // =========================================================
                // ترحيل محاسبي (LedgerEntries + تحديث حساب العميل)
                // =========================================================
                string? postedBy = User?.Identity?.Name ?? "SYSTEM";
                await _ledgerPostingService.PostCashPaymentAsync(payment.CashPaymentId, postedBy);

                // =========================================================
                // إغلاق الإذن (تغيير الحالة إلى "مغلق")
                // =========================================================
                payment.Status = "مغلق";
                payment.IsPosted = true;
                await _context.SaveChangesAsync();

                // =========================================================
                // تسجيل في اللوج
                // =========================================================
                await _activityLogger.LogAsync(
                    UserActionType.Create,
                    "CashPayment",
                    payment.CashPaymentId,
                    $"إضافة وإغلاق إذن دفع رقم {payment.PaymentNumber} بمبلغ {payment.Amount}"
                );

                TempData["CashPaymentSuccess"] = "تم حفظ وترحيل وإغلاق إذن الدفع بنجاح.";
                
                // ✅ إعادة تحميل الصفحة بنفس الإذن (بدون توجيه للقائمة)
                await PopulateDropdownsAsync(payment.CustomerId, payment.CashAccountId, payment.CounterAccountId);
                if (payment.CustomerId.HasValue)
                    ViewBag.LockCustomer = true;
                return View(payment);
            }
            catch (Exception ex)
            {
                TempData["CashPaymentError"] = $"حدث خطأ أثناء حفظ إذن الدفع: {ex.Message}";
                await PopulateDropdownsAsync(payment.CustomerId, payment.CashAccountId, payment.CounterAccountId);
                if (payment.CustomerId.HasValue)
                    ViewBag.LockCustomer = true;
                return View(payment);
            }
        }

        // =========================================================
        // Open — فتح إذن مغلق للتعديل
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Open(int id)
        {
            try
            {
                var payment = await _context.CashPayments
                    .FirstOrDefaultAsync(p => p.CashPaymentId == id);

                if (payment == null)
                {
                    TempData["CashPaymentError"] = "الإذن غير موجود.";
                    return RedirectToAction(nameof(Index));
                }

                // ================================
                // 1) لازم يكون مغلق عشان ينفع "فتح"
                // ================================
                if (payment.Status != "مغلق")
                {
                    TempData["CashPaymentError"] = "هذا الإذن غير مغلق، لا يوجد ما يمكن فتحه.";
                    return RedirectToAction(nameof(Create), new { id });
                }

                // ================================
                // 2) فتح الإذن للتعديل
                // ================================
                payment.Status = "مفتوحة للتعديل";
                payment.IsPosted = false;
                payment.PostedAt = null;
                payment.PostedBy = null;
                payment.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                // ================================
                // 3) تسجيل نشاط
                // ================================
                await _activityLogger.LogAsync(
                    actionType: UserActionType.Edit,
                    entityName: "CashPayment",
                    entityId: payment.CashPaymentId,
                    description: $"فتح إذن دفع رقم {payment.PaymentNumber} للتعديل"
                );

                TempData["CashPaymentSuccess"] = "تم فتح الإذن للتعديل بنجاح.";
                return RedirectToAction(nameof(Create), new { id });
            }
            catch (Exception ex)
            {
                TempData["CashPaymentError"] = $"حدث خطأ: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        // =========================================================
        // Edit — GET: تعديل إذن دفع (يستخدم Create view)
        // =========================================================
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null || id <= 0)
                return BadRequest();

            var payment = await _context.CashPayments
                .Include(p => p.Customer)
                .ThenInclude(c => c.Account)
                .FirstOrDefaultAsync(p => p.CashPaymentId == id);

            if (payment == null)
                return NotFound();

            // ✅ إذا كان العميل محددًا، نحفظ هذه المعلومة للواجهة
            if (payment.CustomerId.HasValue)
                ViewBag.LockCustomer = true;

            await PopulateDropdownsAsync(payment.CustomerId, payment.CashAccountId, payment.CounterAccountId);
            return View("Create", payment);
        }

        // =========================================================
        // Edit — POST: حفظ التعديل (يستخدم Create POST)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id,
            [Bind("CashPaymentId,PaymentDate,CustomerId,CashAccountId,CounterAccountId,Amount,Description")]
            CashPayment payment)
        {
            if (id != payment.CashPaymentId)
                return NotFound();

            // ✅ تجاهل خطأ التحقق لـ PaymentNumber
            ModelState.Remove(nameof(CashPayment.PaymentNumber));

            if (!ModelState.IsValid)
            {
                await PopulateDropdownsAsync(payment.CustomerId, payment.CashAccountId, payment.CounterAccountId);
                if (payment.CustomerId.HasValue)
                    ViewBag.LockCustomer = true;
                return View("Create", payment);
            }

            try
            {
                // =========================================================
                // 1) جلب السجل الأصلي من قاعدة البيانات
                // =========================================================
                var existing = await _context.CashPayments
                    .Include(p => p.Customer)
                    .FirstOrDefaultAsync(p => p.CashPaymentId == id);

                if (existing == null)
                    return NotFound();

                // =========================================================
                // 2) حفظ Snapshot للقيم القديمة (للتسجيل في اللوج)
                // =========================================================
                var oldValues = new
                {
                    Amount = existing.Amount,
                    CashAccountId = existing.CashAccountId,
                    CounterAccountId = existing.CounterAccountId,
                    PaymentDate = existing.PaymentDate,
                    Description = existing.Description,
                    IsPosted = existing.IsPosted,
                    Status = existing.Status
                };

                // =========================================================
                // 3) تحديث الحقول المسموح بها
                // =========================================================
                existing.PaymentDate = payment.PaymentDate;
                existing.CustomerId = payment.CustomerId;
                existing.CashAccountId = payment.CashAccountId;
                existing.CounterAccountId = payment.CounterAccountId;
                existing.Amount = payment.Amount;
                existing.Description = payment.Description;
                existing.UpdatedAt = DateTime.UtcNow;

                // =========================================================
                // 4) حفظ التعديلات أولاً
                // =========================================================
                await _context.SaveChangesAsync();

                // =========================================================
                // 5) ترحيل محاسبي (يعكس القديم ويعمل جديد إذا كان مفتوح)
                // =========================================================
                if (!existing.IsPosted)
                {
                    string? postedBy = User?.Identity?.Name ?? "SYSTEM";
                    await _ledgerPostingService.PostCashPaymentAsync(existing.CashPaymentId, postedBy);
                }

                // =========================================================
                // 6) إغلاق الإذن بعد التعديل
                // =========================================================
                existing.Status = "مغلق";
                existing.IsPosted = true;
                await _context.SaveChangesAsync();

                // =========================================================
                // 7) تسجيل في اللوج
                // =========================================================
                await _activityLogger.LogAsync(
                    UserActionType.Edit,
                    "CashPayment",
                    existing.CashPaymentId,
                    $"تعديل إذن دفع رقم {existing.PaymentNumber}",
                    oldValues: System.Text.Json.JsonSerializer.Serialize(oldValues),
                    newValues: System.Text.Json.JsonSerializer.Serialize(new
                    {
                        Amount = existing.Amount,
                        CashAccountId = existing.CashAccountId,
                        CounterAccountId = existing.CounterAccountId,
                        PaymentDate = existing.PaymentDate,
                        Description = existing.Description,
                        IsPosted = existing.IsPosted,
                        Status = existing.Status
                    })
                );

                TempData["CashPaymentSuccess"] = "تم تعديل وإغلاق إذن الدفع بنجاح.";
                
                // ✅ إعادة تحميل الصفحة بنفس الإذن
                await PopulateDropdownsAsync(existing.CustomerId, existing.CashAccountId, existing.CounterAccountId);
                if (existing.CustomerId.HasValue)
                    ViewBag.LockCustomer = true;
                return View("Create", existing);
            }
            catch (Exception ex)
            {
                TempData["CashPaymentError"] = $"حدث خطأ: {ex.Message}";
                await PopulateDropdownsAsync(payment.CustomerId, payment.CashAccountId, payment.CounterAccountId);
                if (payment.CustomerId.HasValue)
                    ViewBag.LockCustomer = true;
                return View("Create", payment);
            }
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
                TempData["CashPaymentError"] = "إذن الدفع غير موجود.";
                return RedirectToAction(nameof(Index));
            }

            _context.CashPayments.Remove(payment);
            await _context.SaveChangesAsync();

            TempData["CashPaymentSuccess"] = "تم حذف إذن الدفع.";
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
                TempData["CashPaymentError"] = "لم يتم اختيار أى إذن للحذف.";
                return RedirectToAction(nameof(Index));
            }

            // نحضر الإذون المطابقة للأرقام المختارة
            var payments = await _context.CashPayments
                                         .Where(p => ids.Contains(p.CashPaymentId))
                                         .ToListAsync();

            if (payments.Count == 0)
            {
                TempData["CashPaymentError"] = "لم يتم العثور على الإذون المحددة.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                foreach (var p in payments.Where(x => x.IsPosted))
                {
                    await _ledgerPostingService.ReverseForHeaderDeleteAsync(
                        Models.LedgerSourceType.Payment,
                        p.CashPaymentId,
                        User?.Identity?.Name ?? "SYSTEM",
                        "حذف جماعي إذون دفع");
                }

                _context.CashPayments.RemoveRange(payments);
                await _context.SaveChangesAsync();
                await _ledgerPostingService.RecalcAllCustomerBalancesAsync();

                TempData["CashPaymentSuccess"] = $"تم حذف {payments.Count} من إذون الدفع المحددة.";
            }
            catch (Exception ex)
            {
                TempData["CashPaymentError"] = $"لا يمكن حذف بعض الإذون: {ex.Message}";
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
                TempData["CashPaymentError"] = "لا توجد إذون دفع لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                foreach (var p in all.Where(x => x.IsPosted))
                {
                    await _ledgerPostingService.ReverseForHeaderDeleteAsync(
                        Models.LedgerSourceType.Payment,
                        p.CashPaymentId,
                        User?.Identity?.Name ?? "SYSTEM",
                        "حذف جميع إذون الدفع");
                }

                _context.CashPayments.RemoveRange(all);
                await _context.SaveChangesAsync();
                await _ledgerPostingService.RecalcAllCustomerBalancesAsync();

                TempData["CashPaymentSuccess"] = "تم حذف جميع إذون الدفع.";
            }
            catch (Exception ex)
            {
                TempData["CashPaymentError"] = $"لا يمكن حذف جميع إذون الدفع: {ex.Message}";
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
