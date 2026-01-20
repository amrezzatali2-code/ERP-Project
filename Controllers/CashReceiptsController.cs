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
using ERP.Infrastructure;                         // PagedResult + ApplySearchSort + UserActivityLogger
using ERP.Models;                                 // CashReceipt + Account + Customer
using ERP.Services;                               // ILedgerPostingService

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إذون استلام النقدية (CashReceipts)
    /// - نظام القوائم الموحد في Index (بحث + ترتيب + فلترة + حذف جماعي + تصدير).
    /// - CRUD كامل: Create / Edit / Details / Delete.
    /// - زر حفظ = حفظ + ترحيل محاسبي (LedgerEntries + تحديث حساب العميل)
    /// - زر فتح = فتح للتعديل (لا يعكس، الحفظ هو الذي يعكس)
    /// </summary>
    public class CashReceiptsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IUserActivityLogger _activityLogger;
        private readonly ILedgerPostingService _ledgerPostingService;

        public CashReceiptsController(
            AppDbContext context,
            IUserActivityLogger activityLogger,
            ILedgerPostingService ledgerPostingService)
        {
            _context = context;
            _activityLogger = activityLogger;
            _ledgerPostingService = ledgerPostingService;
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

            ViewData["CustomerId"] = new SelectList(customerItems, "Value", "Text", customerId);
            ViewData["CustomersWithAccounts"] = customers.ToDictionary(c => c.CustomerId, c => c.AccountId);

            // حسابات نشطة للصندوق / البنك
            ViewData["CashAccountId"] = new SelectList(
                await _context.Accounts
                        .AsNoTracking()
                        .Where(a => a.IsActive)
                        .OrderBy(a => a.AccountName)
                        .Select(a => new { a.AccountId, a.AccountName })
                        .ToListAsync(),
                "AccountId",
                "AccountName",
                cashAccountId
            );

            // حسابات نشطة للطرف المقابل
            ViewData["CounterAccountId"] = new SelectList(
                await _context.Accounts
                        .AsNoTracking()
                        .Where(a => a.IsActive)
                        .OrderBy(a => a.AccountName)
                        .Select(a => new { a.AccountId, a.AccountName })
                        .ToListAsync(),
                "AccountId",
                "AccountName",
                counterAccountId
            );
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
        // GET: يعرض الفورم مع تعبئة تلقائية إذا جاء من customerId
        // POST: يحفظ + يرحّل محاسبيًا
        // =========================================================

        // GET: CashReceipts/Create
        public async Task<IActionResult> Create(int? customerId = null)
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

            var model = new CashReceipt
            {
                ReceiptDate = DateTime.Now.Date,
                Status = "غير مرحلة",
                IsPosted = false,
                CashAccountId = defaultCashAccount?.AccountId ?? 0 // ✅ تعيين حساب الصندوق الافتراضي
            };

            // ✅ إذا جاء من صفحة "حجم تعامل عميل" (customerId موجود)
            if (customerId.HasValue && customerId.Value > 0)
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
                    model.Description = $"تحصيل من العميل {customer.CustomerName}";
                    // ✅ قفل العميل وحساب الطرف في الواجهة
                    ViewBag.LockCustomer = true;
                }
            }

            await PopulateDropdownsAsync(model.CustomerId, model.CashAccountId > 0 ? model.CashAccountId : null, model.CounterAccountId);
            return View(model);
        }

        // POST: CashReceipts/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("ReceiptDate,CustomerId,CashAccountId,CounterAccountId,Amount,Description")]
                                                CashReceipt cashReceipt)
        {
            // ✅ تجاهل خطأ التحقق لـ ReceiptNumber لأنه سيتم توليده تلقائياً
            ModelState.Remove(nameof(CashReceipt.ReceiptNumber));
            
            // ✅ التحقق من الحقول المطلوبة يدوياً
            if (cashReceipt.CashAccountId <= 0)
            {
                ModelState.AddModelError(nameof(CashReceipt.CashAccountId), "يجب اختيار حساب الصندوق/البنك.");
            }
            
            if (cashReceipt.CounterAccountId <= 0)
            {
                ModelState.AddModelError(nameof(CashReceipt.CounterAccountId), "يجب اختيار حساب الطرف.");
            }
            
            if (cashReceipt.Amount <= 0)
            {
                ModelState.AddModelError(nameof(CashReceipt.Amount), "يجب إدخال مبلغ أكبر من الصفر.");
            }
            
            if (ModelState.IsValid)
            {
                try
                {
                    // =========================================================
                    // 1) تعبئة بيانات التتبع الأساسية
                    // =========================================================
                    cashReceipt.CreatedAt = DateTime.Now;
                    cashReceipt.CreatedBy = User?.Identity?.Name ?? "SYSTEM";
                    cashReceipt.Status = "غير مرحلة";
                    cashReceipt.IsPosted = false;
                    cashReceipt.ReceiptNumber = ""; // سيتم تعبئته بعد الحفظ

                    // =========================================================
                    // 2) حفظ الهيدر أولاً (للحصول على CashReceiptId)
                    // =========================================================
                    _context.Add(cashReceipt);
                    await _context.SaveChangesAsync();

                    // =========================================================
                    // 3) توليد رقم المستند تلقائيًا (بعد الحفظ)
                    // =========================================================
                    cashReceipt.ReceiptNumber = cashReceipt.CashReceiptId.ToString();
                    await _context.SaveChangesAsync();

                    // =========================================================
                    // 4) ترحيل محاسبي (LedgerEntries + تحديث حساب العميل)
                    // =========================================================
                    string? postedBy = User?.Identity?.Name ?? "SYSTEM";
                    await _ledgerPostingService.PostCashReceiptAsync(cashReceipt.CashReceiptId, postedBy);

                    // =========================================================
                    // 5) تسجيل في اللوج
                    // =========================================================
                    await _activityLogger.LogAsync(
                        UserActionType.Create,
                        "CashReceipt",
                        cashReceipt.CashReceiptId,
                        $"إضافة إذن استلام رقم {cashReceipt.ReceiptNumber} بمبلغ {cashReceipt.Amount} من عميل {cashReceipt.CustomerId}"
                    );

                    TempData["Success"] = "تم حفظ وترحيل إذن الاستلام بنجاح.";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    // ✅ تسجيل الخطأ الكامل للمطورين
                    var errorMessage = ex.Message;
                    if (ex.InnerException != null)
                    {
                        errorMessage += $" | التفاصيل: {ex.InnerException.Message}";
                    }
                    
                    TempData["Error"] = $"حدث خطأ أثناء حفظ إذن الاستلام: {errorMessage}";
                    
                    // ✅ محاولة حذف الإذن من قاعدة البيانات إذا تم حفظه جزئياً
                    if (cashReceipt.CashReceiptId > 0)
                    {
                        try
                        {
                            var existingReceipt = await _context.CashReceipts.FindAsync(cashReceipt.CashReceiptId);
                            if (existingReceipt != null && !existingReceipt.IsPosted)
                            {
                                _context.CashReceipts.Remove(existingReceipt);
                                await _context.SaveChangesAsync();
                            }
                        }
                        catch { /* تجاهل أخطاء الحذف */ }
                    }
                    
                    await PopulateDropdownsAsync(cashReceipt.CustomerId, cashReceipt.CashAccountId, cashReceipt.CounterAccountId);
                    if (cashReceipt.CustomerId.HasValue)
                        ViewBag.LockCustomer = true;
                    return View(cashReceipt);
                }
            }

            // لو هناك خطأ تحقق نرجّع القوائم المنسدلة
            await PopulateDropdownsAsync(cashReceipt.CustomerId, cashReceipt.CashAccountId, cashReceipt.CounterAccountId);
            if (cashReceipt.CustomerId.HasValue)
                ViewBag.LockCustomer = true;
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

            var cashReceipt = await _context.CashReceipts
                .Include(r => r.Customer)
                .ThenInclude(c => c.Account)
                .FirstOrDefaultAsync(r => r.CashReceiptId == id);

            if (cashReceipt == null)
                return NotFound();

            // ✅ إذا كان العميل محددًا، نحفظ هذه المعلومة للواجهة
            if (cashReceipt.CustomerId.HasValue)
                ViewBag.LockCustomer = true;

            await PopulateDropdownsAsync(cashReceipt.CustomerId, cashReceipt.CashAccountId, cashReceipt.CounterAccountId);
            return View(cashReceipt);
        }

        // POST: CashReceipts/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id,
            [Bind("CashReceiptId,ReceiptDate,CustomerId,CashAccountId,CounterAccountId,Amount,Description")]
            CashReceipt cashReceipt)
        {
            if (id != cashReceipt.CashReceiptId)
                return NotFound();

            if (!ModelState.IsValid)
            {
                PopulateDropdowns(cashReceipt.CustomerId, cashReceipt.CashAccountId, cashReceipt.CounterAccountId);
                if (cashReceipt.CustomerId.HasValue)
                    ViewBag.LockCustomer = true;
                return View(cashReceipt);
            }

            try
            {
                // =========================================================
                // 1) جلب السجل الأصلي من قاعدة البيانات
                // =========================================================
                var existing = await _context.CashReceipts
                    .Include(r => r.Customer)
                    .FirstOrDefaultAsync(r => r.CashReceiptId == id);

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
                    ReceiptDate = existing.ReceiptDate,
                    Description = existing.Description,
                    IsPosted = existing.IsPosted,
                    Status = existing.Status
                };

                // =========================================================
                // 3) تحديث الحقول المسموح بها (رقم المستند لا يتغير)
                // =========================================================
                existing.ReceiptDate = cashReceipt.ReceiptDate;
                existing.CustomerId = cashReceipt.CustomerId;
                existing.CashAccountId = cashReceipt.CashAccountId;
                existing.CounterAccountId = cashReceipt.CounterAccountId;
                existing.Amount = cashReceipt.Amount;
                existing.Description = cashReceipt.Description;
                existing.UpdatedAt = DateTime.Now;

                // =========================================================
                // 4) حفظ التعديلات أولاً
                // =========================================================
                await _context.SaveChangesAsync();

                // =========================================================
                // 5) ترحيل محاسبي (يعكس القديم ويعمل جديد إذا كان مفتوح)
                // =========================================================
                // ✅ إذا كان الإذن مفتوح (غير مرحّل)، الترحيل سيعمل مرحلة جديدة
                // ✅ إذا كان مرحّل، يجب فتحه أولاً (زر Open)
                if (!existing.IsPosted)
                {
                    string? postedBy = User?.Identity?.Name ?? "SYSTEM";
                    await _ledgerPostingService.PostCashReceiptAsync(existing.CashReceiptId, postedBy);
                }

                // =========================================================
                // 6) تسجيل في اللوج
                // =========================================================
                await _activityLogger.LogAsync(
                    UserActionType.Edit,
                    "CashReceipt",
                    existing.CashReceiptId,
                    $"تعديل إذن استلام رقم {existing.ReceiptNumber}",
                    oldValues: System.Text.Json.JsonSerializer.Serialize(oldValues),
                    newValues: System.Text.Json.JsonSerializer.Serialize(new
                    {
                        Amount = existing.Amount,
                        CashAccountId = existing.CashAccountId,
                        CounterAccountId = existing.CounterAccountId,
                        ReceiptDate = existing.ReceiptDate,
                        Description = existing.Description,
                        IsPosted = existing.IsPosted,
                        Status = existing.Status
                    })
                );

                TempData["Success"] = "تم تعديل وترحيل إذن الاستلام بنجاح.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"حدث خطأ: {ex.Message}";
                PopulateDropdowns(cashReceipt.CustomerId, cashReceipt.CashAccountId, cashReceipt.CounterAccountId);
                if (cashReceipt.CustomerId.HasValue)
                    ViewBag.LockCustomer = true;
                return View(cashReceipt);
            }
        }

        // =========================================================
        // Open — فتح إذن مرحّل للتعديل (مثل فواتير البيع)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Open(int id)
        {
            try
            {
                var receipt = await _context.CashReceipts
                    .FirstOrDefaultAsync(r => r.CashReceiptId == id);

                if (receipt == null)
                {
                    TempData["Error"] = "الإذن غير موجود.";
                    return RedirectToAction(nameof(Index));
                }

                // ================================
                // 1) لازم يكون مرحّل عشان ينفع "فتح"
                // ================================
                if (!receipt.IsPosted)
                {
                    TempData["Error"] = "هذا الإذن غير مُرحّل، لا يوجد ما يمكن فتحه.";
                    return RedirectToAction(nameof(Details), new { id });
                }

                // ================================
                // 2) فتح الإذن للتعديل (لا يعكس القيود - الحفظ هو الذي يعكس)
                // ================================
                receipt.IsPosted = false;
                receipt.Status = "مفتوحة للتعديل";
                receipt.PostedAt = null;
                receipt.PostedBy = null;
                receipt.UpdatedAt = DateTime.Now;

                await _context.SaveChangesAsync();

                // ================================
                // 3) تسجيل نشاط
                // ================================
                await _activityLogger.LogAsync(
                    actionType: UserActionType.Unpost,
                    entityName: "CashReceipt",
                    entityId: receipt.CashReceiptId,
                    description: $"فتح إذن استلام رقم {receipt.ReceiptNumber} للتعديل"
                );

                TempData["Success"] = "تم فتح الإذن للتعديل بنجاح.";
                return RedirectToAction(nameof(Edit), new { id });
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"حدث خطأ: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
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
            try
            {
                var cashReceipt = await _context.CashReceipts
                    .Include(r => r.Customer)
                    .FirstOrDefaultAsync(r => r.CashReceiptId == id);

                if (cashReceipt == null)
                {
                    TempData["Error"] = "الإذن غير موجود.";
                    return RedirectToAction(nameof(Index));
                }

                // =========================================================
                // 1) إذا كان مرحّل، نعكس قيوده أولاً
                // =========================================================
                if (cashReceipt.IsPosted)
                {
                    string? postedBy = User?.Identity?.Name ?? "SYSTEM";
                    await _ledgerPostingService.ReverseForHeaderDeleteAsync(
                        LedgerSourceType.Receipt,
                        cashReceipt.CashReceiptId,
                        postedBy,
                        "حذف إذن استلام نقدية"
                    );
                }

                // =========================================================
                // 2) حفظ Snapshot قبل الحذف (للتسجيل في اللوج)
                // =========================================================
                var oldValues = System.Text.Json.JsonSerializer.Serialize(new
                {
                    ReceiptNumber = cashReceipt.ReceiptNumber,
                    Amount = cashReceipt.Amount,
                    CustomerId = cashReceipt.CustomerId,
                    ReceiptDate = cashReceipt.ReceiptDate,
                    IsPosted = cashReceipt.IsPosted
                });

                // =========================================================
                // 3) حذف الإذن
                // =========================================================
                _context.CashReceipts.Remove(cashReceipt);
                await _context.SaveChangesAsync();

                // =========================================================
                // 4) تسجيل في اللوج
                // =========================================================
                await _activityLogger.LogAsync(
                    UserActionType.Delete,
                    "CashReceipt",
                    id,
                    $"حذف إذن استلام رقم {cashReceipt.ReceiptNumber}",
                    oldValues: oldValues
                );

                TempData["Success"] = "تم حذف إذن الاستلام بنجاح.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"حدث خطأ أثناء الحذف: {ex.Message}";
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
                                         .Include(r => r.Customer)
                                         .Where(r => ids.Contains(r.CashReceiptId))
                                         .ToListAsync();

            if (receipts.Count == 0)
            {
                TempData["Error"] = "لم يتم العثور على الإذون المحددة.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                string? postedBy = User?.Identity?.Name ?? "SYSTEM";

                // عكس القيود لكل إذن مرحّل
                foreach (var receipt in receipts)
                {
                    if (receipt.IsPosted)
                    {
                        await _ledgerPostingService.ReverseForHeaderDeleteAsync(
                            LedgerSourceType.Receipt,
                            receipt.CashReceiptId,
                            postedBy,
                            "حذف إذن استلام نقدية"
                        );
                    }

                    // تسجيل في اللوج
                    await _activityLogger.LogAsync(
                        UserActionType.Delete,
                        "CashReceipt",
                        receipt.CashReceiptId,
                        $"حذف إذن استلام رقم {receipt.ReceiptNumber} (حذف جماعي)"
                    );
                }

                _context.CashReceipts.RemoveRange(receipts);
                await _context.SaveChangesAsync();

                TempData["Success"] = $"تم حذف {receipts.Count} من إذون الاستلام المحددة.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"حدث خطأ أثناء الحذف: {ex.Message}";
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
            var all = await _context.CashReceipts
                .Include(r => r.Customer)
                .ToListAsync();

            if (all.Count == 0)
            {
                TempData["Error"] = "لا توجد إذون لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                string? postedBy = User?.Identity?.Name ?? "SYSTEM";

                // عكس القيود لكل إذن مرحّل
                foreach (var receipt in all)
                {
                    if (receipt.IsPosted)
                    {
                        await _ledgerPostingService.ReverseForHeaderDeleteAsync(
                            LedgerSourceType.Receipt,
                            receipt.CashReceiptId,
                            postedBy,
                            "حذف إذن استلام نقدية (حذف جميع)"
                        );
                    }
                }

                _context.CashReceipts.RemoveRange(all);
                await _context.SaveChangesAsync();

                TempData["Success"] = $"تم حذف جميع إذون الاستلام ({all.Count}).";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"حدث خطأ أثناء الحذف: {ex.Message}";
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
