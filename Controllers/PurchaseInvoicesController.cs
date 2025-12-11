using ERP.Data;                                 // AppDbContext
using ERP.Infrastructure;                       // كلاس PagedResult لتقسيم الصفحات
using ERP.Models;                               // الموديل PurchaseInvoice
using ERP.Services;
using ERP.ViewModels;   // علشان نقدر نستعمل PurchaseInvoiceHeaderDto
using Microsoft.AspNetCore.Mvc;                 // أساس الكنترولر و IActionResult
using Microsoft.AspNetCore.Mvc.Rendering;       // SelectList للقوائم المنسدلة
using Microsoft.EntityFrameworkCore;            // Include / AsNoTracking / ToListAsync
using System;                                   // تواريخ وأوقات
using System.Collections.Generic;               // القوائم List
using System.Linq;                              // LINQ: Where / OrderBy / Any
using System.Text;                              // لبناء ملف CSV
using System.Threading.Tasks;                   // async / await



namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إدارة جدول فواتير المشتريات (PurchaseInvoices)
    /// - عرض القائمة مع بحث / ترتيب / تقسيم صفحات.
    /// - فلتر تاريخ/وقت.
    /// - فلتر من رقم / إلى رقم.
    /// - حذف محدد / حذف كل الفواتير.
    /// - تصدير CSV/Excel.
    /// - Show / Create / Edit / Delete.
    /// </summary>
    public class PurchaseInvoicesController : Controller
    {
        // كائن الاتصال بقاعدة البيانات
        private readonly AppDbContext _context;
        private readonly DocumentTotalsService _docTotals;      // متغير: خدمة إجماليات المستندات

        public PurchaseInvoicesController(AppDbContext context,
                                      DocumentTotalsService docTotals)
        {
            _context = context;
            _docTotals = docTotals;
        }

        #region Index (قائمة فواتير المشتريات)

        /// <summary>
        /// عرض قائمة فواتير المشتريات بنفس نظام القوائم الموحد.
        /// </summary>
        public async Task<IActionResult> Index(
            string? search,                      // نص البحث
            string? searchBy,                    // نوع البحث: id / vendor / warehouse / date / status
            string? sort,                        // عمود الترتيب: id / date / vendor / warehouse / net / status / posted ...
            string? dir,                         // اتجاه الترتيب: asc / desc
            bool useDateRange = false,           // هل فلتر التاريخ مفعّل؟
            DateTime? fromDate = null,           // من تاريخ/وقت
            DateTime? toDate = null,             // إلى تاريخ/وقت
            string? dateField = "PIDate",        // الحقل المستخدم في فلتر التاريخ (PIDate أو CreatedAt)
            int? fromCode = null,                // من رقم فاتورة
            int? toCode = null,                  // إلى رقم فاتورة
            int page = 1,                        // رقم الصفحة
            int pageSize = 25                    // حجم الصفحة
        )
        {
            // قيم افتراضية لو مش جاية من الكويري
            searchBy ??= "id";
            sort ??= "PIDate";
            dir ??= "desc";
            dateField ??= "PIDate";

            if (page < 1) page = 1;
            if (pageSize <= 0) pageSize = 25;

            // نبدأ بالاستعلام الأساسي بدون تنفيذ فعلي (IQueryable)
            IQueryable<PurchaseInvoice> query = _context.PurchaseInvoices.AsNoTracking();

            // قراءة codeFrom/codeTo من الكويري (للتوافق مع الاندكس/الإكسبورت)
            int? codeFrom = Request.Query.ContainsKey("codeFrom")
                ? TryParseNullableInt(Request.Query["codeFrom"])
                : null;

            int? codeTo = Request.Query.ContainsKey("codeTo")
                ? TryParseNullableInt(Request.Query["codeTo"])
                : null;

            // نحدد القيمة النهائية لفلتر الأرقام
            int? finalFromCode = fromCode ?? codeFrom;
            int? finalToCode = toCode ?? codeTo;

            // 1) تطبيق البحث + فلتر الكود + فلتر التاريخ
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

            // 4) قراءة صفحة واحدة فقط (Skip/Take)
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            // 5) تجهيز الموديل الخاص بالتقسيم PagedResult
            var model = new PagedResult<PurchaseInvoice>
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

            // 6) تمرير قيم للـ ViewBag علشان الواجهة تحفظ الحالة الحالية
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










       


         /// <summary>
         /// دالة مساعدة: تجهيز الموردين والمخازن للفورم (الهيدر فقط).
         /// </summary>
            private async Task PopulateDropDownsAsync(
                 int? selectedCustomerId = null,    // متغير: كود المورد المختار (لو فاتورة قديمة)
                int? selectedWarehouseId = null)   // متغير: كود المخزن المختار (لو فاتورة قديمة)
                 {
            
            

            // 1) تحميل كل العملاء/الموردين من جدول Customers
            var customers = await _context.Customers
                .AsNoTracking()
                .Include(c => c.Governorate)
                .Include(c => c.District)
                .Include(c => c.Area)
                .OrderBy(c => c.CustomerName)
                .Select(c => new
                {
                    Id = c.CustomerId,                               // كود المورد
                    Name = c.CustomerName,                           // اسم المورد
                    Phone = c.Phone1 ?? string.Empty,                // الهاتف
                    Address = c.Address ?? string.Empty,             // العنوان
                    Gov = c.Governorate != null
                                ? c.Governorate.GovernorateName
                                : string.Empty,                      // اسم المحافظة
                    District = c.District != null
                                ? c.District.DistrictName
                                : string.Empty,                      // اسم الحي
                    Area = c.Area != null
                                ? c.Area.AreaName
                                : string.Empty,                      // اسم المنطقة
                    Credit = c.CreditLimit                           // حد الائتمان
                })
                .ToListAsync();

            // متغير: إرسال قائمة الموردين للـ View لاستخدامها فى الـ datalist
            ViewBag.Customers = customers;

            // لو فى مورد مختار (فاتورة قديمة) نحضر اسمه لعرضه تلقائياً
            if (selectedCustomerId.HasValue)
            {
                var current = customers.FirstOrDefault(c => c.Id == selectedCustomerId.Value);
                if (current != null)
                {
                    ViewBag.SelectedCustomerName = current.Name; // متغير: اسم المورد الحالي
                }
            }

            // 3) تحميل المخازن للكومبو
            var warehouses = await _context.Warehouses
                .AsNoTracking()
                .OrderBy(w => w.WarehouseName)
                .ToListAsync();

            // متغير: إرسال قائمة المخازن للـ View كـ SelectList
            ViewBag.Warehouses = new SelectList(
                warehouses,
                "WarehouseId",      // كود المخزن
                "WarehouseName",    // اسم المخزن
                selectedWarehouseId // المخزن المختار (لو موجود)
            );
        }







        // دالة مساعدة: تحميل قائمة الأصناف للأوتوكومبليت فى سطر الفاتورة
        private async Task LoadProductsForAutoCompleteAsync()
        {
            // متغير: قائمة الأصناف من جدول Products
            var products = await _context.Products
                .AsNoTracking()                      // قراءة فقط بدون تتبع
                .OrderBy(p => p.ProdName)            // ترتيب حسب اسم الصنف
                .Select(p => new
                {
                    Id = p.ProdId,                          // كود الصنف الداخلى (الكود الوحيد)
                    Name = p.ProdName ?? string.Empty,        // اسم الصنف
                    GenericName = p.GenericName ?? string.Empty,     // الاسم العلمى (للـبدائل)
                    Company = p.Company ?? string.Empty,         // الشركة
                    PriceRetail = p.PriceRetail,                     // سعر الجمهور
                    HasQuota = p.HasQuota                         // هل للصنف كوتة أم لا
                                                                  // 🔹 لا نرجّع QuotaQuantity لأنه غير مستخدم فى الواجهة حاليًا
                })
                .ToListAsync();

            // متغير: نرسل القائمة إلى الواجهة لتغذية الـ datalist
            ViewBag.ProductsAuto = products;
        }






        // إرجاع بدائل الصنف (نفس الاسم العلمي) على شكل JSON
        [HttpGet]
        public async Task<IActionResult> GetAlternativeProducts(int prodId)
        {
            var mainProd = await _context.Products
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProdId == prodId);

            if (mainProd == null || string.IsNullOrWhiteSpace(mainProd.GenericName))
                return Json(Array.Empty<object>());

            string generic = mainProd.GenericName;

            var alts = await _context.Products
                .AsNoTracking()
                .Where(p => p.GenericName == generic && p.ProdId != prodId)
                .OrderBy(p => p.ProdName)
                .Select(p => new
                {
                    id = p.ProdId,
                    name = p.ProdName,
                    company = p.Company,
                    price = p.PriceRetail
                })
                .ToListAsync();

            return Json(alts);
        }














        /// <summary>
        /// شاشة إنشاء فاتورة مشتريات جديدة.
        /// </summary>
        // GET: PurchaseInvoices/Create
        public async Task<IActionResult> Create()
        {
            // متغير: موديل الفاتورة الجديدة بالقيم الافتراضية
            var model = new PurchaseInvoice
            {
                PIDate = DateTime.Today,      // تاريخ الفاتورة الافتراضي: تاريخ اليوم
                CreatedAt = DateTime.UtcNow,    // وقت إنشاء السجل في قاعدة البيانات (UTC)
                Status = "Draft",             // الحالة الافتراضية = مسودة
                IsPosted = false                // الفاتورة غير مُرحّلة عند الإنشاء
            };

            // ==============================
            // تعيين المخزن الافتراضي للفاتورة
            // ==============================
            // متغير: رقم المخزن الافتراضي (الدواء أو أول مخزن)
            var defaultWarehouseId = await GetDefaultWarehouseIdAsync();

            // لو أكبر من صفر → يعني عندنا مخزن فعلاً
            if (defaultWarehouseId > 0)
            {
                model.WarehouseId = defaultWarehouseId;  // تعيين المخزن الافتراضي للفاتورة الجديدة
            }

            // تجهيز القوائم المنسدلة (الموردين + المخازن) مع تمرير المورد والمخزن المختارين
            await PopulateDropDownsAsync(model.CustomerId, model.WarehouseId);

            // تجهيز الأصناف للأوتوكومبليت فى سطور الفاتورة
            await LoadProductsForAutoCompleteAsync();

            // نفس الموديل Show إظهار شاشة 
            // Create يفتح View "Show" علشان نتعامل مع شاشة موحدة
            return View("Show", model);
        }








        /// <summary>
        /// استقبال بيانات إنشاء فاتورة المشتريات من الفورم.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PurchaseInvoice model)
        {
            // 🔹 أولاً: التحقق أن الكود الموجود فعلاً هو مورد (Supplier) وليس عميل عادي
            bool supplierExists = await _context.Customers
                .AnyAsync(c => c.CustomerId == model.CustomerId
                            && c.PartyCategory == "Supplier");   // متغير: نتاكد أنه من نوع مورد

            if (!supplierExists)
            {
                // متغير: إضافة خطأ على حقل المورد لو لم نجد مورداً بهذا الكود
                ModelState.AddModelError("CustomerId", "يجب اختيار مورد من قائمة الموردين.");
            }

            // 🔹 لو في أخطاء في البيانات (فشل الفاليديشن)
            if (!ModelState.IsValid)
            {
                // دالة: تحميل الموردين + المخازن للفورم (الهيدر)
                await PopulateDropDownsAsync(model.CustomerId, model.WarehouseId);

                // دالة: تحميل الأصناف للأوتوكومبليت فى سطور الفاتورة (الجزء الخاص بالأصناف)
                await LoadProductsForAutoCompleteAsync();

                // نرجع نفس الفاتورة فى وضع العرض/الإنشاء مع إظهار الأخطاء
                return View("Show", model);
            }

            // 🔹 لو البيانات سليمة نكمل منطق إنشاء الفاتورة

            // متغير: تاريخ إنشاء الفاتورة
            model.CreatedAt = DateTime.UtcNow;

            // متغير: حالة الفاتورة (لو لم تُرسل من الفورم نضعها Draft تلقائياً)
            model.Status = string.IsNullOrWhiteSpace(model.Status) ? "Draft" : model.Status;

            // متغير: الفاتورة عند الإنشاء غير مرحّلة
            model.IsPosted = false;

            // إضافة الفاتورة الجديدة لجدول فواتير المشتريات
            _context.PurchaseInvoices.Add(model);
            await _context.SaveChangesAsync();   // حفظ التغييرات في قاعدة البيانات

            // رسالة نجاح تُعرض مرة واحدة بعد الرجوع للصفحة
            TempData["SuccessMessage"] = "تم إنشاء فاتورة المشتريات بنجاح.";

            // إعادة التوجيه لشاشة عرض الفاتورة بعد الإنشاء
            return RedirectToAction(nameof(Details), new { id = model.PIId });
        }








        /// <summary>
        /// شاشة تعديل فاتورة مشتريات موجودة.
        /// </summary>
        public async Task<IActionResult> Edit(int id)
        {
            var invoice = await _context.PurchaseInvoices
                .FirstOrDefaultAsync(p => p.PIId == id);

            if (invoice == null)
                return NotFound();

            // ✅ تجهيز المورد + المخزن
            await PopulateDropDownsAsync(invoice.CustomerId, invoice.WarehouseId);

            // ✅ تجهيز الأصناف للأوتوكومبليت
            await LoadProductsForAutoCompleteAsync();

            return View(invoice);    // Views/PurchaseInvoices/Edit.cshtml
        }










        /// <summary>
        /// استقبال بيانات التعديل وحفظها.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, PurchaseInvoice model)
        {
            // متغير: نتأكد أن رقم الفاتورة في الرابط هو نفسه في الموديل
            if (id != model.PIId)
                return BadRequest();

            // 🔹 لو في أخطاء في الفاليديشن
            if (!ModelState.IsValid)
            {
                // دالة: تحميل الموردين + المخازن للهيدر
                await PopulateDropDownsAsync(model.CustomerId, model.WarehouseId);

                // دالة: تحميل الأصناف للأوتوكومبليت فى سطور الفاتورة
                await LoadProductsForAutoCompleteAsync();

                // نرجع نفس شاشة التعديل مع عرض الأخطاء
                return View(model);
            }

            // متغير: جلب الفاتورة الأصلية من قاعدة البيانات
            var invoice = await _context.PurchaseInvoices
                .FirstOrDefaultAsync(p => p.PIId == id);

            if (invoice == null)
                return NotFound();

            // 🔹 تحديث الحقول المسموح بتعديلها في رأس الفاتورة
            invoice.PIDate = model.PIDate;                // تاريخ الفاتورة
            invoice.CustomerId = model.CustomerId;        // المورد
            invoice.WarehouseId = model.WarehouseId;      // المخزن
            invoice.RefPRId = model.RefPRId;              // رقم طلب الشراء المرجعي (لو موجود)

            // 🔹 إجماليات الفاتورة (يمكن تعديلها يدويًا أو من خدمة أخرى)
            invoice.ItemsTotal = model.ItemsTotal;        // إجمالي قيمة الأصناف
            invoice.DiscountTotal = model.DiscountTotal;  // إجمالي الخصم
            invoice.TaxTotal = model.TaxTotal;            // إجمالي الضريبة
            invoice.NetTotal = model.NetTotal;            // صافي الفاتورة

            // 🔹 حالة الفاتورة والترحيل
            invoice.Status = model.Status;                // حالة الفاتورة (مسودة، معتمدة، ... إلخ)
            invoice.IsPosted = model.IsPosted;            // هل الفاتورة مُرحّلة؟
            invoice.PostedAt = model.PostedAt;            // تاريخ/وقت الترحيل (لو موجود)
            invoice.PostedBy = model.PostedBy;            // المستخدم الذي قام بالترحيل

            // 🔹 وقت آخر تعديل
            invoice.UpdatedAt = DateTime.UtcNow;

            // حفظ التعديلات في قاعدة البيانات
            await _context.SaveChangesAsync();

            // 🔹 استدعاء الخدمة لحساب:
            //    ItemsTotal / DiscountTotal / TaxTotal / NetTotal فى هيدر الفاتورة
            await _docTotals.RecalcPurchaseInvoiceTotalsAsync(model.PIId);

            // رسالة نجاح للمستخدم
            TempData["SuccessMessage"] = "تم تعديل فاتورة المشتريات بنجاح.";

            // العودة لشاشة عرض الفاتورة بعد التعديل
            return RedirectToAction(nameof(Details), new { id = invoice.PIId });
        }










        /// <summary>
        /// عرض تفاصيل فاتورة مشتريات واحدة لزر "تفاصيل"
        /// يتم استدعاؤها من قائمة فواتير المشتريات (Index).
        /// </summary>
        public async Task<IActionResult> Details(int id)
        {
            // متغير: جلب الفاتورة المطلوبة من قاعدة البيانات
            var invoice = await _context.PurchaseInvoices
                .Include(p => p.Customer)       // لو حابب تعرض اسم المورد فى الهيدر
                .AsNoTracking()                 // قراءة فقط بدون تتبع
                .FirstOrDefaultAsync(p => p.PIId == id);

            // لو الفاتورة غير موجودة نرجّع 404
            if (invoice == null)
                return NotFound();

            // تجهيز الموردين والمخازن للـ datalist و الـ select
            // نمرر المورد والمخزن الحاليين عشان يظهروا مختارين فى الفورم
            await PopulateDropDownsAsync(invoice.CustomerId, invoice.WarehouseId);

            // تجهيز قائمة الأصناف للأوتوكومبليت فى سطور الفاتورة
            await LoadProductsForAutoCompleteAsync();

            // عرض شاشة تفاصيل الفاتورة
            // هيستخدم View باسم Details.cshtml تلقائياً
            return View(invoice);
        }







        #endregion

        #region Delete / DeleteConfirmed (حذف فاتورة واحدة)

        /// <summary>
        /// صفحة تأكيد الحذف لفاتورة مشتريات واحدة.
        /// </summary>
        public async Task<IActionResult> Delete(int id)
        {
            var invoice = await _context.PurchaseInvoices
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PIId == id);

            if (invoice == null)
            {
                return NotFound();
            }

            return View(invoice);   // View: Views/PurchaseInvoices/Delete.cshtml
        }










        /// <summary>
        /// تنفيذ الحذف الفعلي بعد التأكيد.
        /// </summary>
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var invoice = await _context.PurchaseInvoices
                .FirstOrDefaultAsync(p => p.PIId == id);

            if (invoice != null)
            {
                _context.PurchaseInvoices.Remove(invoice);

                // TODO: لاحقاً ممكن نستدعي خدمة DocumentTotalsService لإعادة حساب الإجماليات
                await _context.SaveChangesAsync();
            }

            TempData["SuccessMessage"] = "تم حذف فاتورة المشتريات بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        #endregion

        #region BulkDelete (حذف مجموعة فواتير دفعة واحدة)

        /// <summary>
        /// حذف مجموعة فواتير مشتريات بناءً على قائمة أرقام (selectedIds = "1,2,3")
        /// يُستدعى من زر "حذف فواتير المشتريات المحددة".
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["ErrorMessage"] = "لم يتم اختيار أي فاتورة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            // نحول "1,2,3" إلى List<int>
            var ids = selectedIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => TryParseNullableInt(s))
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();

            if (!ids.Any())
            {
                TempData["ErrorMessage"] = "لم يتم التعرف على أرقام الفواتير المحددة.";
                return RedirectToAction(nameof(Index));
            }

            var invoices = await _context.PurchaseInvoices
                .Where(p => ids.Contains(p.PIId))
                .ToListAsync();

            if (invoices.Any())
            {
                _context.PurchaseInvoices.RemoveRange(invoices);

                // TODO: استدعاء خدمة إعادة حساب الإجماليات لو هنربطها بالحسابات/المخزون
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"تم حذف {invoices.Count} فاتورة مشتريات بنجاح.";
            }
            else
            {
                TempData["ErrorMessage"] = "لم يتم العثور على الفواتير المحددة في قاعدة البيانات.";
            }

            return RedirectToAction(nameof(Index));
        }

        #endregion

        #region DeleteAll (حذف جميع فواتير المشتريات)

        /// <summary>
        /// حذف جميع فواتير المشتريات (عملية خطيرة) — يفضل ربطها بصلاحيات.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var allInvoices = await _context.PurchaseInvoices.ToListAsync();

            if (!allInvoices.Any())
            {
                TempData["ErrorMessage"] = "لا توجد فواتير مشتريات لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            _context.PurchaseInvoices.RemoveRange(allInvoices);

            // TODO: إعادة حساب أرصدة/حسابات لو في ربط مباشر
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "تم حذف جميع فواتير المشتريات بنجاح.";
            return RedirectToAction(nameof(Index));
        }





[HttpPost]
    [IgnoreAntiforgeryToken]  // استدعاء من AJAX بدون AntiForgery
    public async Task<IActionResult> SaveHeader([FromBody] PurchaseInvoiceHeaderDto dto)
    {
        // 1) فحص الداتا المرسلة من الواجهة
        if (dto == null)
        {
            return BadRequest("حدث خطأ فى البيانات المرسلة.");
        }

        if (dto.CustomerId <= 0)
        {
            return BadRequest("يجب اختيار المورد قبل حفظ الفاتورة.");
        }

        if (dto.WarehouseId <= 0)
        {
            return BadRequest("يجب اختيار المخزن قبل حفظ الفاتورة.");
        }

        var now = DateTime.Now;   // وقت التنفيذ الحالي

        // 2) فاتورة جديدة (PIId = 0)
        if (dto.PIId == 0)
        {
            var invoice = new PurchaseInvoice
            {
                PIDate = now.Date,                     // تاريخ الفاتورة
                CustomerId = dto.CustomerId,               // كود المورد
                WarehouseId = dto.WarehouseId,             // كود المخزن
                RefPRId = dto.RefPRId,                  // طلب الشراء المرجعي (لو موجود)

                Status = "Draft",                      // حالة الفاتورة
                IsPosted = false,

                CreatedAt = now,                          // وقت الإنشاء
                CreatedBy = User?.Identity?.Name ?? "System"
            };

            _context.PurchaseInvoices.Add(invoice);
            await _context.SaveChangesAsync();

            // الرد إلى الجافاسكربت
            return Json(new
            {
                success = true,
                piId = invoice.PIId,                  // رقم الفاتورة الداخلي
                invoiceNumber = invoice.PIId.ToString(),       // رقم الفاتورة المعروض

                invoiceDate = invoice.PIDate.ToString("yyyy/MM/dd"),
                invoiceTime = invoice.CreatedAt.ToString("HH:mm"),

                status = invoice.Status,
                isPosted = invoice.IsPosted,
                createdBy = invoice.CreatedBy
            });
        }

        // 3) تعديل فاتورة موجودة (PIId > 0)
        var existing = await _context.PurchaseInvoices
                                     .FirstOrDefaultAsync(p => p.PIId == dto.PIId);

        if (existing == null)
        {
            return NotFound("لم يتم العثور على الفاتورة المطلوبة.");
        }

        if (existing.IsPosted)
        {
            return BadRequest("لا يمكن تعديل فاتورة تم ترحيلها.");
        }

        existing.CustomerId = dto.CustomerId;
        existing.WarehouseId = dto.WarehouseId;
        existing.RefPRId = dto.RefPRId;
        existing.UpdatedAt = now;

        await _context.SaveChangesAsync();

        return Json(new
        {
            success = true,
            piId = existing.PIId,
            invoiceNumber = existing.PIId.ToString(),

            invoiceDate = existing.PIDate.ToString("yyyy/MM/dd"),
            invoiceTime = existing.CreatedAt.ToString("HH:mm"),

            status = existing.Status,
            isPosted = existing.IsPosted,
            createdBy = existing.CreatedBy
        });
    }










    // دالة مساعدة: تجيب رقم المخزن الافتراضي للفاتورة
    // المنطق:
    // 1) نحاول نلاقي مخزن اسمه "الدواء" (المخزن الرئيسي).
    // 2) لو مش موجود، ناخد أول مخزن في الجدول.
    // 3) لو مفيش مخازن خالص → ترجع 0.
    private async Task<int> GetDefaultWarehouseIdAsync()
        {
            // متغير: نحاول نجيب رقم مخزن اسمه "الدواء"
            var id = await _context.Warehouses
                .Where(w => w.WarehouseName == "الدواء")   // 🔹 غيّر WarehouseName لو اسم الخاصية مختلف
                .Select(w => w.WarehouseId)                // متغير: رقم المخزن
                .FirstOrDefaultAsync();                    // لو مش لاقي → هترجع 0

            // لو لقينا مخزن اسمه "الدواء" فعلاً
            if (id != 0)
                return id;

            // لو مفيش مخزن اسمه "الدواء" → نجيب أول مخزن في الجدول
            id = await _context.Warehouses
                .OrderBy(w => w.WarehouseId)               // ترتيب علشان ناخد أول واحد ثابت
                .Select(w => w.WarehouseId)
                .FirstOrDefaultAsync();                    // برضه لو الجدول فاضي → 0

            // لو 0 يبقى مفيش مخازن خالص، وده هنكشفه فى الفاليديشن بعدين
            return id;
        }







        /// <summary>
        /// تصدير فواتير المشتريات (بعد تطبيق نفس فلاتر Index) إلى ملف CSV.
        /// - format: "excel" أو "csv" (الاتنين حالياً CSV يفتح في إكسل).
        /// </summary>
        // دالة تصدير فواتير المشتريات بعد تطبيق نفس فلاتر Index
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
            // ✅ تجهيز القيم الافتراضية
            format = string.IsNullOrWhiteSpace(format) ? "excel" : format.ToLowerInvariant();
            searchBy ??= "id";
            sort ??= "PIDate";
            dir ??= "desc";
            dateField ??= "PIDate";

            bool sortDesc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            // ✅ استعلام أساسي بدون تتبع
            IQueryable<PurchaseInvoice> query = _context.PurchaseInvoices.AsNoTracking();

            // ✅ تطبيق نفس الفلاتر بتاعة الـ Index
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

            // ✅ تطبيق نفس الترتيب
            query = ApplySort(query, sort, sortDesc);

            var list = await query.ToListAsync();

            // ✅ بناء ملف CSV (إكسل يفتحه عادي)
            var sb = new StringBuilder();

            // عناوين الأعمدة (الهيدر)
            sb.AppendLine("PIId,PIDate,CustomerId,WarehouseId,ItemsTotal,DiscountTotal,TaxTotal,NetTotal,Status,IsPosted,CreatedAt,PostedAt");

            foreach (var p in list)
            {
                // استبدال الفواصل في النصوص علشان ما تبهدلش CSV
                string status = (p.Status ?? string.Empty).Replace(",", " ");

                string line = string.Join(",",
                    p.PIId,                                         // رقم الفاتورة
                    p.PIDate.ToString("yyyy-MM-dd"),               // تاريخ الفاتورة
                    p.CustomerId,                                  // كود المورد
                    p.WarehouseId,                                 // كود المخزن
                    p.ItemsTotal.ToString("0.00"),                 // إجمالي السطور
                    p.DiscountTotal.ToString("0.00"),              // إجمالي الخصم
                    p.TaxTotal.ToString("0.00"),                   // إجمالي الضريبة
                    p.NetTotal.ToString("0.00"),                   // صافي الفاتورة
                    status,                                        // حالة الفاتورة
                    p.IsPosted ? "1" : "0",                        // مرحّلة؟
                    p.CreatedAt.ToString("yyyy-MM-dd HH:mm"),      // تاريخ الإنشاء
                    p.PostedAt.HasValue
                        ? p.PostedAt.Value.ToString("yyyy-MM-dd HH:mm")
                        : string.Empty                             // تاريخ الترحيل (لو موجود)
                );

                sb.AppendLine(line);
            }

            // تحويل النص إلى بايتس
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());

            // اسم الملف (إمتداد CSV – يفتح في إكسل عادي)
            var fileName = $"PurchaseInvoices_{DateTime.Now:yyyyMMdd_HHmmss}.csv";

            // ✅ نوع الملف نخليه نوع إكسل علشان المتصفح يفتحه بـ Excel مباشرة
            const string contentType = "application/vnd.ms-excel";

            return File(bytes, contentType, fileName);
        }











        /// <summary>
        /// دالة فلترة موحدة: نص البحث + من/إلى كود + فلتر التاريخ.
        /// </summary>
        private static IQueryable<PurchaseInvoice> ApplyFilters(
            IQueryable<PurchaseInvoice> query,
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
            searchBy ??= "id";
            dateField ??= "PIDate";

            // 1) فلتر نص البحث
            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();
                switch (searchBy.ToLower())
                {
                    case "id":
                        if (int.TryParse(search, out var idVal))
                        {
                            query = query.Where(p => p.PIId == idVal);
                        }
                        else
                        {
                            query = query.Where(p => p.PIId.ToString().Contains(search));
                        }
                        break;

                    // vendor/customer → نفس الحقل CustomerId (هو المورد في المشتريات)
                    case "vendor":
                    case "customer":
                        if (int.TryParse(search, out var custId))
                        {
                            query = query.Where(p => p.CustomerId == custId);
                        }
                        else
                        {
                            query = query.Where(p =>
                                p.CustomerId.ToString().Contains(search)
                            );
                        }
                        break;

                    case "warehouse":
                        if (int.TryParse(search, out var whId))
                        {
                            query = query.Where(p => p.WarehouseId == whId);
                        }
                        else
                        {
                            query = query.Where(p =>
                                p.WarehouseId.ToString().Contains(search)
                            );
                        }
                        break;

                    case "date":
                        if (DateTime.TryParse(search, out var dateVal))
                        {
                            var d = dateVal.Date;
                            query = query.Where(p => p.PIDate.Date == d);
                        }
                        break;

                    case "status":
                        query = query.Where(p => p.Status.Contains(search));
                        break;

                    // بحث عام على أكثر من حقل
                    default:
                        query = query.Where(p =>
                            p.PIId.ToString().Contains(search) ||
                            p.CustomerId.ToString().Contains(search) ||
                            p.WarehouseId.ToString().Contains(search) ||
                            p.Status.Contains(search)
                        );
                        break;
                }
            }

            // 2) فلتر من رقم / إلى رقم (PIId)
            if (fromCode.HasValue)
                query = query.Where(p => p.PIId >= fromCode.Value);

            if (toCode.HasValue)
                query = query.Where(p => p.PIId <= toCode.Value);

            // 3) فلتر التاريخ/الوقت
            if (useDateRange && (fromDate.HasValue || toDate.HasValue))
            {
                bool useCreated = string.Equals(dateField, "CreatedAt", StringComparison.OrdinalIgnoreCase);

                if (fromDate.HasValue)
                {
                    if (useCreated)
                        query = query.Where(p => p.CreatedAt >= fromDate.Value);
                    else
                        query = query.Where(p => p.PIDate >= fromDate.Value);
                }

                if (toDate.HasValue)
                {
                    if (useCreated)
                        query = query.Where(p => p.CreatedAt <= toDate.Value);
                    else
                        query = query.Where(p => p.PIDate <= toDate.Value);
                }
            }

            return query;
        }

        /// <summary>
        /// دالة الترتيب الموحدة بحسب اسم العمود المنطقي القادم من الواجهة.
        /// </summary>
        private static IQueryable<PurchaseInvoice> ApplySort(
            IQueryable<PurchaseInvoice> query,
            string? sort,
            bool desc
        )
        {
            sort = (sort ?? "PIDate").ToLower();

            switch (sort)
            {
                case "id":
                    query = desc
                        ? query.OrderByDescending(p => p.PIId)
                        : query.OrderBy(p => p.PIId);
                    break;

                case "date":
                case "pidate":
                    query = desc
                        ? query.OrderByDescending(p => p.PIDate).ThenByDescending(p => p.PIId)
                        : query.OrderBy(p => p.PIDate).ThenBy(p => p.PIId);
                    break;

                case "vendor":
                case "customer":
                    query = desc
                        ? query.OrderByDescending(p => p.CustomerId).ThenByDescending(p => p.PIId)
                        : query.OrderBy(p => p.CustomerId).ThenBy(p => p.PIId);
                    break;

                case "warehouse":
                    query = desc
                        ? query.OrderByDescending(p => p.WarehouseId).ThenByDescending(p => p.PIId)
                        : query.OrderBy(p => p.WarehouseId).ThenBy(p => p.PIId);
                    break;

                case "net":
                    query = desc
                        ? query.OrderByDescending(p => p.NetTotal).ThenByDescending(p => p.PIId)
                        : query.OrderBy(p => p.NetTotal).ThenBy(p => p.PIId);
                    break;

                case "status":
                    query = desc
                        ? query.OrderByDescending(p => p.Status).ThenByDescending(p => p.PIId)
                        : query.OrderBy(p => p.Status).ThenBy(p => p.PIId);
                    break;

                case "posted":
                    query = desc
                        ? query.OrderByDescending(p => p.IsPosted).ThenByDescending(p => p.PIId)
                        : query.OrderBy(p => p.IsPosted).ThenBy(p => p.PIId);
                    break;

                case "createdat":
                    query = desc
                        ? query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.PIId)
                        : query.OrderBy(p => p.CreatedAt).ThenBy(p => p.PIId);
                    break;

                default:
                    // الترتيب الافتراضي: بتاريخ الفاتورة ثم رقم الفاتورة
                    query = desc
                        ? query.OrderByDescending(p => p.PIDate).ThenByDescending(p => p.PIId)
                        : query.OrderBy(p => p.PIDate).ThenBy(p => p.PIId);
                    break;
            }

            return query;
        }

        /// <summary>
        /// دالة مساعدة لتحويل نص إلى int? بأمان.
        /// ترجع null لو التحويل فشل.
        /// </summary>
        private static int? TryParseNullableInt(string? value)
        {
            if (int.TryParse(value, out var i))
                return i;

            return null;
        }

        #endregion
    }
}
