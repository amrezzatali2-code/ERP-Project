using DocumentFormat.OpenXml.VariantTypes;
using ERP.Data;
using ERP.Infrastructure;
using ERP.Models;
using ERP.Services;
using ERP.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;               // لاستخدام StringBuilder في التصدير
using System.Threading.Tasks;

namespace ERP.Controllers
{
    public class SalesInvoicesController : Controller
    {
        private readonly AppDbContext _context;               // سياق قاعدة البيانات
        private readonly DocumentTotalsService _docTotals;    // خدمة إجماليات المستندات
        private readonly IUserActivityLogger _activityLogger; // خدمة سجل النشاط
        private readonly ILedgerPostingService _ledgerPostingService; // متغير: خدمة الترحيل
        private readonly StockAnalysisService _StockAnalysisService; // متغير: خدمة الترحيل

        // مصفوفة طرق الدفع الثابتة لعرضها في الفورم
        private static readonly string[] PaymentMethods = new[] { "نقدي", "شبكة", "آجل", "مختلط" };

        public SalesInvoicesController(AppDbContext context,
                                        DocumentTotalsService docTotals,
                                        IUserActivityLogger activityLogger, ILedgerPostingService ledgerPosting,
                                        StockAnalysisService stockAnalysisService)

        {
            _context = context;
            _docTotals = docTotals;
            _activityLogger = activityLogger;
            _ledgerPostingService = ledgerPosting;     // ✅ متغير: خدمة الترحيل
            _StockAnalysisService = stockAnalysisService;
        }



        /// <summary>
        /// دالة مساعدة: تجهيز العملاء والمخازن للفورم (الهيدر فقط).
        /// - ترجع ViewBag.Customers لاستخدامها فى datalist + تعبئة بيانات العميل تلقائيًا
        /// - ترجع ViewBag.Warehouses لكومبو المخازن
        /// </summary>
        private async Task PopulateDropDownsAsync(
            int? selectedCustomerId = null,     // متغير: كود العميل المختار (لو فاتورة قديمة)
            int? selectedWarehouseId = null)    // متغير: كود المخزن المختار (لو فاتورة قديمة)
        {
            // =========================================================
            // (1) تحميل كل العملاء من جدول Customers + بياناتهم + سياسة العميل
            // =========================================================
            var customers = await _context.Customers
      .AsNoTracking()
      .Include(c => c.Governorate)
      .Include(c => c.District)
      .Include(c => c.Area)
      .Include(c => c.Policy) // ✅ تحميل السياسة
      .OrderBy(c => c.CustomerName)
      .Select(c => new
      {
          Id = c.CustomerId,                               // كود العميل
          Name = c.CustomerName,                           // اسم العميل
          Phone = c.Phone1 ?? string.Empty,                // الهاتف
          Address = c.Address ?? string.Empty,             // العنوان
          Gov = c.Governorate != null
                      ? c.Governorate.GovernorateName
                      : string.Empty,                      // المحافظة
          District = c.District != null
                      ? c.District.DistrictName
                      : string.Empty,                      // الحي
          Area = c.Area != null
                      ? c.Area.AreaName
                      : string.Empty,                      // المنطقة
          Credit = c.CreditLimit,                          // حد الائتمان

          // ✅ جديد: سياسة العميل
          PolicyId = c.PolicyId,                           // كود السياسة (nullable)
          PolicyName = c.Policy != null ? c.Policy.Name : "" // اسم السياسة من Policy.Name
      })
      .ToListAsync();

            // إرسال القائمة للـ View
            ViewBag.Customers = customers;

            // لو فى عميل مختار (فاتورة قديمة) نحضر اسمه لعرضه تلقائياً
            if (selectedCustomerId.HasValue)
            {
                var current = customers.FirstOrDefault(c => c.Id == selectedCustomerId.Value);
                if (current != null)
                {
                    ViewBag.SelectedCustomerName = current.Name; // متغير: اسم العميل الحالي
                }
            }

            // =========================================================
            // (2) تحميل المخازن للكومبو
            // =========================================================
            var warehouses = await _context.Warehouses
                .AsNoTracking()
                .OrderBy(w => w.WarehouseName)
                .ToListAsync();

            // متغير: إرسال قائمة المخازن للـ View كـ SelectList
            ViewBag.Warehouses = new SelectList(
                warehouses,
                "WarehouseId",        // متغير: كود المخزن
                "WarehouseName",      // متغير: اسم المخزن
                selectedWarehouseId   // متغير: المخزن المختار (لو موجود)
            );
        }






        // دالة مساعدة: تجيب اسم اليوزر الحالى من الـ Claims
        private string GetCurrentUserDisplayName()
        {
            // لو فيه يوزر عامل تسجيل دخول
            if (User?.Identity?.IsAuthenticated == true)
            {
                // نحاول الأول نجيب DisplayName (اسم الموظف الظاهر فى التقارير)
                var displayName = User.FindFirst("DisplayName")?.Value;
                if (!string.IsNullOrWhiteSpace(displayName))
                    return displayName;

                // لو مفيش DisplayName نرجع لاسم الدخول العادى (UserName)
                if (!string.IsNullOrWhiteSpace(User.Identity!.Name))
                    return User.Identity.Name!;
            }

            // فى الحالات الاستثنائية (مثلاً أثناء Seed أو لو مفيش Login)
            return "System";
        }





        // =========================================================
        // زر: حفظ هيدر فاتورة المبيعات (AJAX)
        // - متوافق مع JS الحالي الذي يرسل: InvoiceId / CustomerId / WarehouseId / RefPRId
        // - إنشاء جديد لو InvoiceId = 0
        // - تعديل لو InvoiceId > 0
        // - يمنع التعديل لو الفاتورة مُرحّلة IsPosted = true
        // - لا يستخدم PaymentMethod الآن (حسب اختيارك A)
        // - يحفظ في جدول SalesInvoices (قائمة المبيعات)
        // =========================================================
        [HttpPost]
        [IgnoreAntiforgeryToken] // تعليق: استدعاء من AJAX بدون AntiForgery
        public async Task<IActionResult> SaveHeader([FromBody] SalesInvoiceHeaderDto dto)
        {
            // =========================================================
            // (1) فحص الداتا المرسلة من الواجهة
            // =========================================================
            if (dto == null)
                return BadRequest("حدث خطأ فى البيانات المرسلة.");

            if (dto.CustomerId <= 0)
                return BadRequest("يجب اختيار العميل قبل حفظ الفاتورة.");

            if (dto.WarehouseId <= 0)
                return BadRequest("يجب اختيار المخزن قبل حفظ الفاتورة.");

            var now = DateTime.Now; // متغير: وقت التنفيذ الحالي

            // =========================================================
            // (2) فاتورة جديدة (InvoiceId = 0)
            // =========================================================
            if (dto.InvoiceId == 0)
            {
                var invoice = new SalesInvoice
                {
                    // -------------------------
                    // تاريخ/وقت الفاتورة
                    // -------------------------
                    SIDate = now.Date,        // متغير: تاريخ الفاتورة
                    SITime = now.TimeOfDay,   // متغير: وقت الفاتورة

                    // -------------------------
                    // بيانات الهيدر
                    // -------------------------
                    CustomerId = dto.CustomerId,    // متغير: كود العميل
                    WarehouseId = dto.WarehouseId,  // متغير: كود المخزن

                    // -------------------------
                    // الحالة
                    // -------------------------
                    Status = "مسودة",   // متغير: حالة مبدئية (هنحولها لمرحلة لاحقاً عند إضافة العمود للقائمة)
                    IsPosted = false,    // متغير: غير مرحّلة

                    // -------------------------
                    // التتبع
                    // -------------------------
                    CreatedAt = now,                      // متغير: وقت الإنشاء
                    CreatedBy = GetCurrentUserDisplayName() // متغير: اسم المستخدم الحالي
                };

                _context.SalesInvoices.Add(invoice);
                await _context.SaveChangesAsync();

                // =========================================================
                // الرد إلى الجافاسكربت
                // - مهم: نرجّع invoiceId لأن الـ JS يستخدم هذا الاسم
                // =========================================================
                return Json(new
                {
                    success = true,

                    // رقم الفاتورة
                    invoiceId = invoice.SIId,                 // متغير: رقم الفاتورة الداخلي (متوافق مع JS)
                    invoiceNumber = invoice.SIId.ToString(),  // متغير: رقم الفاتورة المعروض

                    // تاريخ/وقت
                    invoiceDate = invoice.SIDate.ToString("yyyy/MM/dd"),
                    invoiceTime = DateTime.Today.Add(invoice.SITime).ToString("HH:mm"),

                    // حالة/ترحيل
                    status = invoice.Status,
                    isPosted = invoice.IsPosted,

                    // تتبع
                    createdBy = invoice.CreatedBy
                });
            }

            // =========================================================
            // (3) تعديل فاتورة موجودة (InvoiceId > 0)
            // =========================================================
            var existing = await _context.SalesInvoices
                .FirstOrDefaultAsync(s => s.SIId == dto.InvoiceId);

            if (existing == null)
                return NotFound("لم يتم العثور على الفاتورة المطلوبة.");

            if (existing.IsPosted)
                return BadRequest("لا يمكن تعديل فاتورة تم ترحيلها.");

            // =========================================================
            // (4) تحديث بيانات الهيدر فقط (حسب اختيارك A)
            // =========================================================
            existing.CustomerId = dto.CustomerId;     // متغير: العميل
            existing.WarehouseId = dto.WarehouseId;   // متغير: المخزن
            existing.UpdatedAt = now;                 // متغير: وقت آخر تحديث

            await _context.SaveChangesAsync();

            // =========================================================
            // (5) الرد إلى الجافاسكربت بعد التعديل
            // =========================================================
            return Json(new
            {
                success = true,

                invoiceId = existing.SIId,
                invoiceNumber = existing.SIId.ToString(),

                invoiceDate = existing.SIDate.ToString("yyyy/MM/dd"),
                invoiceTime = DateTime.Today.Add(existing.SITime).ToString("HH:mm"),

                status = existing.Status,
                isPosted = existing.IsPosted,
                createdBy = existing.CreatedBy
            });
        }







        // =========================================================
        // دالة مساعدة: تحميل قائمة الأصناف للأوتوكومبليت فى سطر الفاتورة (المبيعات)
        // ثابت مهم:
        // - هذه القائمة للبحث السريع فقط (اسم/كود/شركة/اسم علمي...)
        // - سعر البيع "ليس اختياريًا" لكنه لا يُحدد هنا لأن FEFO يعتمد على WarehouseId
        // - السعر الحقيقي سيأتي بعد اختيار الصنف من:
        //   StockBatch (كمية + ترتيب FEFO) + Batch (PriceRetailBatch)
        // =========================================================
        private async Task LoadProductsForAutoCompleteAsync()
        {
            // متغير: قائمة الأصناف من جدول Products
            var products = await _context.Products
                .AsNoTracking()                      // تعليق: قراءة فقط بدون تتبع (أسرع)
                .OrderBy(p => p.ProdName)            // تعليق: ترتيب حسب اسم الصنف
                .Select(p => new
                {
                    Id = p.ProdId,                               // متغير: كود الصنف الداخلي (الكود الوحيد)
                    Name = p.ProdName ?? string.Empty,           // متغير: اسم الصنف
                    GenericName = p.GenericName ?? string.Empty, // متغير: الاسم العلمي (للـبدائل)
                    Company = p.Company ?? string.Empty,         // متغير: الشركة
                    HasQuota = p.HasQuota,                       // متغير: هل للصنف كوتة أم لا

                    // ✅ مهم: في المبيعات لا نأخذ سعر من Products هنا
                    // لأن السعر الحقيقي يعتمد على (Warehouse + FEFO) ويأتي من Batch.PriceRetailBatch
                    // لذلك نتركه 0 هنا، وسيتم ملؤه تلقائيًا بعد اختيار الصنف
                    PriceRetail = 0m
                })
                .ToListAsync();

            // متغير: نرسل القائمة إلى الواجهة لتغذية الـ datalist
            ViewBag.ProductsAuto = products;
        }








        // =========================================================
        // إرجاع بدائل الصنف (نفس الاسم العلمي) على شكل JSON
        // ✅ ثابت مهم:
        // - الكمية في المبيعات: من StockBatches
        // - سعر التشغيلة: من جدول Batches (Batch.PriceRetailBatch)
        // - هنا نرجّع سعر "افتراضي" من أول تشغيلة FEFO متاحة (بدون تحديد مخزن)
        // =========================================================
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
                    id = p.ProdId,         // متغير: كود الصنف البديل
                    name = p.ProdName,     // متغير: اسم الصنف البديل
                    company = p.Company,   // متغير: الشركة

                    // ✅ السعر الافتراضي من أول تشغيلة FEFO (بدون تحديد مخزن)
                    // - الكمية من StockBatch
                    // - السعر من Batch (PriceRetailBatch)
                    price =
                        (from sb in _context.StockBatches
                         join b in _context.Batches
                           on new { sb.ProdId, sb.BatchNo } equals new { b.ProdId, b.BatchNo }
                         where sb.ProdId == p.ProdId
                               && sb.QtyOnHand > 0
                         orderby sb.Expiry, sb.BatchNo
                         select (decimal?)b.PriceRetailBatch
                        ).FirstOrDefault() ?? 0m
                })
                .ToListAsync();

            return Json(alts);
        }





        // =========================================================
        // ✅ API: GetSalesProductInfo
        // جلب بيانات الصنف داخل فاتورة البيع (بعد اختيار الصنف)
        // - الكمية من StockBatches
        // - سعر التشغيلة من Batches.PriceRetailBatch
        // - ترتيب تشغيلات FEFO: Expiry ثم BatchNo
        // - يرجّع auto = أول تشغيلة FEFO لملء (Batch/Expiry/Price) تلقائيًا
        // ✅ إضافات مطلوبة الآن:
        //   1) weightedDiscount (الخصم المرجّح)
        //   2) saleDisc1 (خصم البيع الافتراضي)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> GetSalesProductInfo(int prodId, int warehouseId, int customerId)
        {
            // =========================
            // (1) تحقق من المدخلات
            // =========================
            if (prodId <= 0 || warehouseId <= 0)
                return Json(new { ok = false, message = "بيانات غير صحيحة." });

            // العميل ممكن يكون 0 لو لسه مش متحدد (فاتورة جديدة)
            // ساعتها هنعتبره سياسة افتراضية (Policy1) داخل السيرفيس
            if (customerId < 0) customerId = 0;

            // =========================
            // (2) هل الصنف عليه كوتة؟ (من Products)
            // =========================
            var prodInfo = await _context.Products
                .AsNoTracking()
                .Where(p => p.ProdId == prodId)
                .Select(p => new
                {
                    hasQuota = p.HasQuota // متغير: هل عليه كوتة؟
                })
                .FirstOrDefaultAsync();

            if (prodInfo == null)
                return Json(new { ok = false, message = "الصنف غير موجود." });

            // =========================
            // (3) كميات لحظية من StockBatches
            // =========================
            var qtyCurrentWarehouse = await _context.StockBatches
                .AsNoTracking()
                .Where(sb => sb.ProdId == prodId && sb.WarehouseId == warehouseId)
                .SumAsync(sb => (decimal?)sb.QtyOnHand) ?? 0m; // متغير: كمية هذا المخزن

            var qtyAllWarehouses = await _context.StockBatches
                .AsNoTracking()
                .Where(sb => sb.ProdId == prodId)
                .SumAsync(sb => (decimal?)sb.QtyOnHand) ?? 0m; // متغير: إجمالي كل المخازن

            // =========================
            // (4) الخصم المرجّح للصنف
            // =========================
            decimal weightedDiscount = 0m;
            weightedDiscount = await _StockAnalysisService.GetWeightedPurchaseDiscountCurrentAsync(prodId);

            string? wdError = null;        // متغير: تشخيص لو فيه خطأ

            try
            {
                // ✅ الخصم المرجّح الحقيقي من الخدمة
                weightedDiscount = await _StockAnalysisService.GetWeightedPurchaseDiscountCurrentAsync(prodId);
            }
            catch (Exception ex)
            {
                // تعليق: لو حصلت مشكلة جوه الخدمة، نرجّع 0 ونطلع رسالة للتشخيص
                wdError = ex.Message;
                weightedDiscount = 0m;
            }

            // =========================================================
            // (4.1) خصم البيع النهائي (Auto) حسب السياسات + الخصم المرجح
            // =========================================================
            decimal saleDisc1 = 0m; // متغير: خصم البيع النهائي %

            // مثال: استدعاء من السيرفيس (أنت عندك StockAnalysisService شغال)
            saleDisc1 = await _StockAnalysisService.GetSaleDiscountAsync(
                prodId: prodId,
                warehouseId: warehouseId,
                customerId: customerId,
                weightedPurchaseDiscount: weightedDiscount
            );

            // حماية (0..100)
            if (saleDisc1 < 0) saleDisc1 = 0;
            if (saleDisc1 > 100) saleDisc1 = 100;


          


            // =========================
            // (5) تشغيلات FEFO للمخزن المختار
            // - الكمية: StockBatches
            // - السعر: Batches.PriceRetailBatch
            // - Left Join احتياطي: لو Batch غير موجود لا يكسر
            // =========================
            var batches = await (
                from sb in _context.StockBatches.AsNoTracking()
                join b in _context.Batches.AsNoTracking()
                    on new { sb.ProdId, sb.BatchNo } equals new { b.ProdId, b.BatchNo } into bb
                from b in bb.DefaultIfEmpty() // ✅ Left Join
                where sb.ProdId == prodId
                      && sb.WarehouseId == warehouseId
                      && sb.QtyOnHand > 0
                orderby sb.Expiry, sb.BatchNo
                select new
                {
                    batchNo = sb.BatchNo,
                    expiry = sb.Expiry,
                    expiryText = sb.Expiry.HasValue ? sb.Expiry.Value.ToString("MM/yyyy") : "",
                    qty = sb.QtyOnHand,

                    // ✅ سعر التشغيلة من Batch (ولو مش موجود -> 0)
                    priceRetailBatch = (decimal?)(b != null ? b.PriceRetailBatch : 0m) ?? 0m
                }
            ).ToListAsync();

            // =========================
            // (6) أول تشغيلة (Auto) لملء Card 3 تلقائيًا
            // =========================
            var first = batches.FirstOrDefault();

            // =========================
            // (7) الرد النهائي للـ JS
            // =========================
            return Json(new
            {
                ok = true,

                // كميات + كوتة + خصم مرجح + خصم بيع
                qtyCurrentWarehouse,
                qtyAllWarehouses,
                hasQuota = prodInfo.hasQuota,
                weightedDiscount,
                saleDisc1,

                // تشغيلات FEFO
                batches,

                // تعبئة تلقائية لأول تشغيلة
                auto = new
                {
                    batchNo = first?.batchNo ?? "",
                    expiryText = first?.expiryText ?? "",
                    priceRetailBatch = first?.priceRetailBatch ?? 0m
                }
            });
        }







        // ================================================================
        // DTO: بيانات إضافة سطر بيع (جاية من AJAX)
        // ملاحظة:
        // - أنت في الـ JS الحالي ما زلت ترسل purchaseDiscountPct (من كود المشتريات)
        // - لذلك هنا سأستقبلها لكن سأعتبرها "خصم البيع" (Disc1Percent)
        // ================================================================
        public class AddLineJsonDto
        {
            public int SIId { get; set; }                 // متغير: رقم فاتورة البيع
            public int prodId { get; set; }               // متغير: كود الصنف
            public int qty { get; set; }                  // متغير: الكمية المطلوبة

            public decimal priceRetail { get; set; }      // متغير: سعر بيع (لن نعتمد عليه إلا fallback)
            public decimal purchaseDiscountPct { get; set; } // متغير: خصم البيع % (مرسل من UI باسم قديم)

            public string? BatchNo { get; set; }          // متغير: رقم التشغيلة (جاية من الواجهة)
            public string? expiryText { get; set; }       // متغير: الصلاحية كنص MM/YYYY أو yyyy-MM-dd حسب الواجهة
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddLineJson([FromBody] AddLineJsonDto dto)
        {
            // تعليق: Transaction مهم لأننا نكتب في أكتر من جدول (SalesLines + StockLedger + FIFOMap + StockBatches)
            await using var tx = await _context.Database.BeginTransactionAsync();

            try
            {
                // =========================
                // 0) فحص سريع للمدخلات
                // =========================
                if (dto == null)
                    return BadRequest(new { ok = false, message = "لم يتم إرسال بيانات." });

                // متغير: رقم الفاتورة (نقبل SIId أو invoiceId لو الواجهة بتبعته)
                int siId = dto.SIId > 0 ? dto.SIId : 0;
                if (siId <= 0 || dto.prodId <= 0)
                    return BadRequest(new { ok = false, message = "بيانات الفاتورة/الصنف غير صحيحة." });

                if (dto.qty <= 0)
                    return BadRequest(new { ok = false, message = "الكمية يجب أن تكون أكبر من صفر." });

                // =========================
                // 0.1) تنظيف خصم البيع (Disc1)
                // =========================
                decimal saleDisc1 = dto.purchaseDiscountPct; // متغير: خصم البيع %
                if (saleDisc1 < 0) saleDisc1 = 0;
                if (saleDisc1 > 100) saleDisc1 = 100;

                // =========================
                // 1) تحويل expiryText إلى DateTime? (مرن)
                // - نقبل MM/YYYY أو yyyy-MM-dd
                // =========================
                DateTime? expDate = null; // متغير: الصلاحية (Date فقط)
                if (!string.IsNullOrWhiteSpace(dto.expiryText))
                {
                    var s = dto.expiryText.Trim();

                    // (أ) محاولة yyyy-MM-dd
                    if (DateTime.TryParse(s, out var parsed))
                    {
                        expDate = parsed.Date;
                    }
                    else
                    {
                        // (ب) محاولة MM/YYYY
                        var parts = s.Split('/');
                        if (parts.Length == 2 &&
                            int.TryParse(parts[0], out int mm) &&
                            int.TryParse(parts[1], out int yyyy) &&
                            mm >= 1 && mm <= 12)
                        {
                            expDate = new DateTime(yyyy, mm, 1).Date;
                        }
                    }
                }

                // متغير: التشغيلة بعد تنظيف المسافات
                var startBatchNo = string.IsNullOrWhiteSpace(dto.BatchNo) ? null : dto.BatchNo.Trim();

                // =========================
                // 2) تحميل الفاتورة + السطور
                // =========================
                var invoice = await _context.SalesInvoices
                    .Include(x => x.Lines)
                    .FirstOrDefaultAsync(x => x.SIId == siId);

                if (invoice == null)
                    return NotFound(new { ok = false, message = "الفاتورة غير موجودة." });

                if (invoice.WarehouseId <= 0)
                    return BadRequest(new { ok = false, message = "يجب اختيار مخزن قبل إضافة سطور." });

                // =========================
                // 2.1) منع التعديل على فاتورة مُرحّلة/مقفولة
                // =========================
                bool isLocked = invoice.IsPosted || invoice.Status == "Posted" || invoice.Status == "Closed";
                if (isLocked)
                {
                    await tx.RollbackAsync();
                    return BadRequest(new
                    {
                        ok = false,
                        message = "لا يمكن إضافة/تعديل سطور: هذه الفاتورة مُرحّلة ومقفولة. استخدم زر (فتح الفاتورة) أولاً."
                    });
                }

                // =========================
                // 3) التأكد أن الصنف موجود
                // =========================
                var product = await _context.Products
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.ProdId == dto.prodId);

                if (product == null)
                    return BadRequest(new { ok = false, message = "الصنف غير موجود." });

                // =========================
                // 4) تجهيز قائمة التشغيلات التي سنسحب منها (Auto Split)
                // - نبدأ من التشغيلة/الصلاحية القادمة من الواجهة
                // - ثم نكمل تلقائيًا على باقي التشغيلات FEFO إذا الكمية المطلوبة أكبر من المتاح
                // =========================

                // متغير: الكمية المتبقية التي نريد بيعها
                int remainingToSell = dto.qty;

                // متغير: قائمة segments (كل segment = تشغيلة + كمية ستباع منها)
                var segments = new List<(string BatchNo, DateTime Expiry, int Qty)>();

                // دالة محلية: قراءة المتاح من StockBatches لتشغيلة محددة
                async Task<int> GetOnHandAsync(string bno, DateTime exp)
                {
                    var sb = await _context.StockBatches
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x =>
                            x.WarehouseId == invoice.WarehouseId &&
                            x.ProdId == dto.prodId &&
                            x.BatchNo == bno &&
                            x.Expiry.HasValue &&
                            x.Expiry.Value.Date == exp.Date);

                    return sb?.QtyOnHand ?? 0;
                }

                // (أ) نحاول نستخدم التشغيلة المرسلة أولاً (لازم تكون موجودة وإلا هنكمل على FEFO)
                if (!string.IsNullOrWhiteSpace(startBatchNo) && expDate.HasValue)
                {
                    int onHand = await GetOnHandAsync(startBatchNo!, expDate.Value);
                    if (onHand > 0)
                    {
                        int take = Math.Min(remainingToSell, onHand);
                        segments.Add((startBatchNo!, expDate.Value.Date, take));
                        remainingToSell -= take;
                    }
                }

                // (ب) لو لسه في كمية متبقية → نكمل على باقي التشغيلات FEFO
                if (remainingToSell > 0)
                {
                    // متغير: التشغيلات المتاحة FEFO من StockBatches (QtyOnHand > 0)
                    var candidates = await _context.StockBatches
                        .AsNoTracking()
                        .Where(x =>
                            x.WarehouseId == invoice.WarehouseId &&
                            x.ProdId == dto.prodId &&
                            x.QtyOnHand > 0 &&
                            x.Expiry.HasValue)
                        .OrderBy(x => x.Expiry)     // FEFO
                        .ThenBy(x => x.BatchNo)     // تثبيت ترتيب
                        .Select(x => new { x.BatchNo, Expiry = x.Expiry!.Value, x.QtyOnHand })
                        .ToListAsync();

                    foreach (var c in candidates)
                    {
                        if (remainingToSell <= 0) break;

                        // نتخطى التشغيلة الأولى لو هي نفسها التي أخذنا منها بالفعل
                        if (!string.IsNullOrWhiteSpace(startBatchNo) && expDate.HasValue)
                        {
                            if ((c.BatchNo ?? "").Trim() == startBatchNo!.Trim() && c.Expiry.Date == expDate.Value.Date)
                                continue;
                        }

                        int take = Math.Min(remainingToSell, c.QtyOnHand);
                        if (take <= 0) continue;

                        segments.Add(((c.BatchNo ?? "").Trim(), c.Expiry.Date, take));
                        remainingToSell -= take;
                    }
                }

                // لو بعد كل ده لسه في كمية متبقية → المخزون غير كافي
                if (remainingToSell > 0)
                {
                    await tx.RollbackAsync();
                    return BadRequest(new
                    {
                        ok = false,
                        message = $"المخزون غير كافي لهذا الصنف. المتاح أقل من المطلوب. (المتبقي غير متاح: {remainingToSell})"
                    });
                }

                // =========================
                // 5) تنفيذ كل Segment:
                // - Merge/Insert في SalesInvoiceLines
                // - StockLedger (QtyOut)
                // - FIFO Map + تقليل RemainingQty من الدُخلات
                // - تحديث StockBatches (QtyOnHand -=)
                // =========================

                foreach (var seg in segments)
                {
                    string batchNo = seg.BatchNo;          // متغير: التشغيلة الحالية
                    DateTime expiry = seg.Expiry.Date;     // متغير: الصلاحية الحالية
                    int qtySeg = seg.Qty;                  // متغير: كمية هذا الجزء

                    // -------------------------
                    // 5.1) جلب سعر التشغيلة (الأفضل من جدول Batches)
                    // -------------------------
                    decimal unitSalePrice = 0m; // متغير: سعر بيع الوحدة للتشغيلة

                    var batchRow = await _context.Batches
                        .AsNoTracking()
                        .FirstOrDefaultAsync(b =>
                            b.ProdId == dto.prodId &&
                            b.BatchNo == batchNo &&
                            b.Expiry.Date == expiry.Date);

                    if (batchRow != null)
                    {
                        unitSalePrice = batchRow.PriceRetailBatch ?? dto.priceRetail; // تعليق: لو سعر التشغيلة Null نستخدم السعر القادم من الفاتورة كـ fallback

                    }
                    else
                    {
                        // fallback: السعر القادم من الواجهة (لعدم تعطيل الإضافة)
                        unitSalePrice = dto.priceRetail;
                    }

                    if (unitSalePrice < 0) unitSalePrice = 0;

                    // -------------------------
                    // 5.2) حسابات السطر (بيع)
                    // -------------------------
                    decimal totalBeforeDisc = qtySeg * unitSalePrice; // متغير: إجمالي قبل الخصم
                    decimal discountValue = totalBeforeDisc * (saleDisc1 / 100m); // متغير: قيمة الخصم
                    decimal totalAfterDisc = totalBeforeDisc - discountValue; // متغير: بعد الخصم

                    // (مبدئياً) الضريبة صفر (يمكن ربطها لاحقًا)
                    decimal taxPercent = 0m; // متغير: نسبة الضريبة
                    decimal taxValue = 0m;   // متغير: قيمة الضريبة
                    decimal netLine = totalAfterDisc + taxValue; // متغير: صافي السطر

                    // -------------------------
                    // 5.3) Merge في SalesInvoiceLines
                    // شرط الميرج: نفس prod + نفس batch + نفس expiry + نفس السعر + نفس الخصم
                    // -------------------------
                    var existingLine = await _context.SalesInvoiceLines.FirstOrDefaultAsync(l =>
                        l.SIId == invoice.SIId &&
                        l.ProdId == dto.prodId &&
                        (l.BatchNo ?? "").Trim() == (batchNo ?? "").Trim() &&
                        (l.Expiry.HasValue ? l.Expiry.Value.Date : (DateTime?)null) == expiry.Date &&
                        l.UnitSalePrice == unitSalePrice 
                    
                    );

                    SalesInvoiceLine affectedLine; // متغير: السطر الذي تأثر
                    int qtyDelta = qtySeg;         // متغير: كمية هذا الجزء (هتخرج من المخزن)

                    if (existingLine != null)
                    {
                        // ✅ زيادة الكمية على نفس السطر
                        existingLine.Qty += qtySeg;

                        // إعادة حساب قيم السطر بعد زيادة الكمية
                        var newTotalBefore = existingLine.Qty * unitSalePrice;
                        var newDiscVal = newTotalBefore * (saleDisc1 / 100m);
                        var newAfter = newTotalBefore - newDiscVal;

                        existingLine.PriceRetail = unitSalePrice;              // تثبيت سعر الجمهور للتشغيلة
                        existingLine.UnitSalePrice = unitSalePrice;            // سعر البيع الفعلي
                        existingLine.DiscountValue = newDiscVal;               // قيمة الخصم
                        existingLine.LineTotalAfterDiscount = newAfter;        // بعد الخصم
                        existingLine.TaxPercent = taxPercent;
                        existingLine.TaxValue = 0m;
                        existingLine.LineNetTotal = newAfter;

                        affectedLine = existingLine;
                    }
                    else
                    {
                        // ✅ إنشاء سطر جديد
                        var nextLineNo = (invoice.Lines.Any() ? invoice.Lines.Max(x => x.LineNo) : 0) + 1;

                        affectedLine = new SalesInvoiceLine
                        {
                            SIId = invoice.SIId,
                            LineNo = nextLineNo,
                            ProdId = dto.prodId,
                            Qty = qtySeg,

                            // تسعير
                            PriceRetail = unitSalePrice,
                            UnitSalePrice = unitSalePrice,

                            // خصومات البيع (نستخدم Disc1 فقط الآن)
                            Disc1Percent = saleDisc1,
                            Disc2Percent = 0m,
                            Disc3Percent = 0m,
                            DiscountValue = discountValue,

                            // إجماليات السطر
                            LineTotalAfterDiscount = totalAfterDisc,
                            TaxPercent = taxPercent,
                            TaxValue = taxValue,
                            LineNetTotal = netLine,

                            // تشغيلات
                            BatchNo = batchNo,
                            Expiry = expiry,

                            // (تكلفة/ربح سيتم ملؤها بعد FIFO)
                            CostPerUnit = 0m,
                            CostTotal = 0m,
                            ProfitValue = 0m,
                            ProfitPercent = 0m
                        };

                        _context.SalesInvoiceLines.Add(affectedLine);
                    }

                    // لازم نحفظ هنا لو السطر جديد علشان يبقى موجود في DB قبل ربط StockLedger.SourceLine
                    await _context.SaveChangesAsync();

                    // -------------------------
                    // 5.4) إنشاء حركة خروج في StockLedger (Sales)
                    // - UnitCost هيتحسب من FIFO بعد ما نستهلك من الدخلات
                    // -------------------------
                    var outLedger = new StockLedger
                    {
                        TranDate = DateTime.UtcNow,
                        WarehouseId = invoice.WarehouseId,
                        ProdId = dto.prodId,
                        BatchNo = batchNo,
                        Expiry = expiry,
                        BatchId = null,

                        QtyIn = 0,
                        QtyOut = qtyDelta,

                        UnitCost = 0m, // سيتم ملؤه بعد FIFO
                        RemainingQty = null, // خروج لا يحتاج RemainingQty

                        SourceType = "Sales",
                        SourceId = invoice.SIId,
                        SourceLine = affectedLine.LineNo,

                        Note = $"Sales Line: {product.ProdName}"
                    };

                    _context.StockLedger.Add(outLedger);
                    await _context.SaveChangesAsync(); // مهم للحصول على EntryId

                    // -------------------------
                    // 5.5) FIFO: استهلاك من دخلات StockLedger (QtyIn + RemainingQty > 0)
                    // - نفس الصنف + نفس المخزن + نفس التشغيلة/الصلاحية
                    // - ننقص RemainingQty
                    // - نضيف StockFifoMap
                    // -------------------------
                    int need = qtyDelta;           // متغير: كمية نحتاج نسحبها من الدخلات
                    decimal costTotal = 0m;        // متغير: إجمالي تكلفة الكمية المباعة (COGS)

                    // الدخلات المتاحة لنفس التشغيلة
                    var inLedgers = await _context.StockLedger
                        .Where(x =>
                            x.WarehouseId == invoice.WarehouseId &&
                            x.ProdId == dto.prodId &&
                            x.QtyIn > 0 &&
                            (x.RemainingQty ?? 0) > 0 &&
                            (x.BatchNo ?? "").Trim() == (batchNo ?? "").Trim() &&
                            ((x.Expiry.HasValue ? x.Expiry.Value.Date : (DateTime?)null) == expiry.Date))
                        .OrderBy(x => x.Expiry)     // FEFO
                        .ThenBy(x => x.EntryId)     // تثبيت
                        .ToListAsync();

                    foreach (var inL in inLedgers)
                    {
                        if (need <= 0) break;

                        int avail = (inL.RemainingQty ?? 0);
                        if (avail <= 0) continue;

                        int take = Math.Min(need, avail);

                        // تقليل المتبقي في الدخلة
                        inL.RemainingQty = avail - take;

                        // تسجيل الربط في FIFO Map
                        var mapRow = new StockFifoMap
                        {
                            OutEntryId = outLedger.EntryId,
                            InEntryId = inL.EntryId,
                            Qty = take,
                            UnitCost = inL.UnitCost // لقطة تكلفة الدخلة
                        };
                        _context.Set<StockFifoMap>().Add(mapRow);

                        // تجميع التكلفة
                        costTotal += (take * inL.UnitCost);

                        need -= take;
                    }

                    // لو لسه في احتياج → ده خطأ منطقي (StockBatches قالت متاح لكن StockLedger مفيهوش RemainingQty كفاية)
                    if (need > 0)
                    {
                        await tx.RollbackAsync();
                        return BadRequest(new
                        {
                            ok = false,
                            message = "حدث تعارض في FIFO: لا توجد دخلات كافية لاستهلاك الكمية (RemainingQty غير كافي)."
                        });
                    }

                    // حساب تكلفة الوحدة من FIFO
                    decimal costPerUnit = qtyDelta > 0 ? (costTotal / qtyDelta) : 0m;
                    costPerUnit = Math.Round(costPerUnit, 2);

                    // تحديث حركة الخروج بتكلفة الوحدة (اختياري لكنه مفيد)
                    outLedger.UnitCost = costPerUnit;

                    // تحديث السطر بتكلفة وربحية
                    decimal revenueSeg = qtyDelta * unitSalePrice * (1m - (saleDisc1 / 100m));
                    decimal profitValue = revenueSeg - costTotal;
                    decimal profitPercent = revenueSeg > 0 ? (profitValue / revenueSeg) * 100m : 0m;

                    // ملء حقول التكلفة/الربح في SalesInvoiceLine
                    affectedLine.CostPerUnit = costPerUnit;
                    affectedLine.CostTotal = Math.Round(costTotal, 2);
                    affectedLine.ProfitValue = Math.Round(profitValue, 2);
                    affectedLine.ProfitPercent = Math.Round(profitPercent, 2);

                    // -------------------------
                    // 5.6) تحديث StockBatches (QtyOnHand -= qtyDelta)
                    // -------------------------
                    var sbRow = await _context.StockBatches.FirstOrDefaultAsync(x =>
                        x.WarehouseId == invoice.WarehouseId &&
                        x.ProdId == dto.prodId &&
                        x.BatchNo == batchNo &&
                        x.Expiry.HasValue &&
                        x.Expiry.Value.Date == expiry.Date);

                    if (sbRow == null)
                    {
                        await tx.RollbackAsync();
                        return BadRequest(new { ok = false, message = "تعذر تحديث رصيد التشغيلة: StockBatch غير موجود." });
                    }

                    if (sbRow.QtyOnHand < qtyDelta)
                    {
                        await tx.RollbackAsync();
                        return BadRequest(new { ok = false, message = "المخزون غير كافي داخل StockBatch لهذه التشغيلة." });
                    }

                    sbRow.QtyOnHand -= qtyDelta;
                    sbRow.UpdatedAt = DateTime.UtcNow;
                    sbRow.Note = $"SI:{invoice.SIId} Line:{affectedLine.LineNo} (-{qtyDelta})";

                    // حفظ تأثير هذا الجزء قبل الانتقال للجزء التالي
                    await _context.SaveChangesAsync();
                }

                // =========================
                // 6) تحديث إجماليات الهيدر يدويًا (بدون الاعتماد على اسم دالة في السيرفيس)
                // =========================
                var linesNow = await _context.SalesInvoiceLines
                    .AsNoTracking()
                    .Where(l => l.SIId == invoice.SIId)
                    .OrderBy(l => l.LineNo)
                    .ToListAsync();

                // متغير: إجمالي قبل الخصم (سعر بيع * كمية)
                decimal totalRetail = linesNow.Sum(x => x.Qty * x.UnitSalePrice);

                // متغير: إجمالي الخصم (نجمع DiscountValue المخزن لكل سطر)
                decimal totalDiscount = linesNow.Sum(x => x.DiscountValue);

                // متغير: الإجمالي بعد الخصم وقبل الضريبة
                decimal totalAfterDiscount = totalRetail - totalDiscount;

                // متغير: الضريبة الحالية (لو عندك لاحقًا، اجمع TaxValue)
                decimal taxAmount = linesNow.Sum(x => x.TaxValue);

                // متغير: الصافي
                decimal netTotal = totalAfterDiscount + taxAmount;

                // تحديث هيدر الفاتورة
                invoice.TotalBeforeDiscount = Math.Round(totalRetail, 2);
                invoice.TotalAfterDiscountBeforeTax = Math.Round(totalAfterDiscount, 2);
                invoice.TaxAmount = Math.Round(taxAmount, 2);
                invoice.NetTotal = Math.Round(netTotal, 2);

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                // =========================
                // 7) LogActivity
                // =========================
                await _activityLogger.LogAsync(
                    UserActionType.Create,
                    "SalesInvoiceLine",
                    invoice.SIId,
                    $"SIId={invoice.SIId} | ProdId={dto.prodId} | Qty={dto.qty} | Segments={segments.Count}"
                );

                // =========================
                // 8) تجهيز DTO للرجوع للواجهة (lines + totals)
                // =========================
                var prodIds = linesNow.Select(l => l.ProdId).Distinct().ToList();
                var prodMap = await _context.Products
                    .AsNoTracking()
                    .Where(p => prodIds.Contains(p.ProdId))
                    .Select(p => new { p.ProdId, p.ProdName })
                    .ToDictionaryAsync(x => x.ProdId, x => x.ProdName ?? "");

                int totalLines = linesNow.Count;
                int totalItems = linesNow.Select(x => x.ProdId).Distinct().Count();
                int totalQty = linesNow.Sum(x => x.Qty);

                var linesDto = linesNow.Select(l =>
                {
                    var name = prodMap.TryGetValue(l.ProdId, out var n) ? n : "";

                    // متغير: قيمة السطر (بعد الخصم)
                    var lv = l.LineTotalAfterDiscount;

                    return new
                    {
                        lineNo = l.LineNo,
                        prodId = l.ProdId,
                        prodName = name,

                        qty = l.Qty,
                        priceRetail = l.UnitSalePrice, // في البيع نعرض سعر التشغيلة
                        discPct = l.Disc1Percent,      // خصم البيع

                        batchNo = l.BatchNo,
                        expiry = l.Expiry?.ToString("yyyy-MM-dd"),

                        // لتسهيل العرض
                        lineValue = lv,
                        expiryText = l.Expiry?.ToString("yyyy-MM-dd")
                    };
                }).ToList();

                return Json(new
                {
                    ok = true,
                    message = "تم إضافة السطر بنجاح.",
                    lines = linesDto,
                    totals = new
                    {
                        totalLines,
                        totalItems,
                        totalQty,
                        totalRetail,
                        totalDiscount,
                        totalAfterDiscount,
                        taxAmount,
                        totalAfterDiscountAndTax = totalAfterDiscount + taxAmount,
                        netTotal
                    }
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return BadRequest(new { ok = false, message = ex.Message });
            }
        }








        // ================================================================
        // DTO: بيانات مسح سطر بيع (جاية من AJAX)
        // ================================================================
        public class RemoveLineJsonDto
        {
            public int SIId { get; set; }    // متغير: رقم فاتورة البيع
            public int LineNo { get; set; }  // متغير: رقم السطر داخل الفاتورة
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveLineJson([FromBody] RemoveLineJsonDto dto)
        {
            try
            {
                // =========================
                // 0) فحص المدخلات
                // =========================
                if (dto == null || dto.SIId <= 0 || dto.LineNo <= 0)
                    return BadRequest(new { ok = false, message = "بيانات المسح غير صحيحة." });

                // =========================
                // 1) تحميل الفاتورة
                // =========================
                var invoice = await _context.SalesInvoices
                    .FirstOrDefaultAsync(x => x.SIId == dto.SIId);

                if (invoice == null)
                    return NotFound(new { ok = false, message = "الفاتورة غير موجودة." });

                // =========================
                // 1.1) منع التعديل على فاتورة مُرحّلة
                // =========================
                if (invoice.IsPosted)
                    return BadRequest(new { ok = false, message = "الفاتورة مُرحّلة ومقفولة. استخدم زر (فتح الفاتورة) أولاً." });

                // =========================
                // 2) تحميل السطر المطلوب
                // =========================
                var line = await _context.SalesInvoiceLines
                    .FirstOrDefaultAsync(l => l.SIId == dto.SIId && l.LineNo == dto.LineNo);

                if (line == null)
                    return NotFound(new { ok = false, message = "السطر غير موجود." });

                // =========================
                // 3) تحميل حركات StockLedger المرتبطة بالسطر
                // =========================
                var ledgers = await _context.StockLedger
                    .Where(x =>
                        x.SourceType == "Sales" &&    // ثابت: مصدر الحركة بيع
                        x.SourceId == dto.SIId &&     // ثابت: رقم الفاتورة
                        x.SourceLine == dto.LineNo)   // ثابت: رقم السطر
                    .ToListAsync();

                // =========================
                // 4) نبدأ Transaction (الحذف مسموح للفواتير غير المرحلة)
                // =========================
                await using var tx = await _context.Database.BeginTransactionAsync();

                // =========================
                // 6) تحديث StockBatches (إرجاع الكمية للمخزن)
                // =========================
                var batchNo = string.IsNullOrWhiteSpace(line.BatchNo) ? null : line.BatchNo.Trim(); // متغير: رقم التشغيلة
                var expDate = line.Expiry?.Date;                                                   // متغير: تاريخ الصلاحية

                // شرطنا الثابت: لازم BatchNo + Expiry
                if (!string.IsNullOrWhiteSpace(batchNo) && expDate.HasValue)
                {
                    var exp = expDate.Value.Date;

                    var sbRow = await _context.StockBatches
                        .FirstOrDefaultAsync(x =>
                            x.WarehouseId == invoice.WarehouseId &&         // متغير: المخزن
                            x.ProdId == line.ProdId &&                      // متغير: الصنف
                            x.BatchNo == batchNo &&                          // متغير: التشغيلة
                            x.Expiry.HasValue &&
                            x.Expiry.Value.Date == exp);

                    if (sbRow != null)
                    {
                        sbRow.QtyOnHand += line.Qty; // ✅ إرجاع الكمية للمخزن (زيادة رصيد البيع)

                        sbRow.UpdatedAt = DateTime.UtcNow;
                        sbRow.Note = $"SI:{dto.SIId} Line:{dto.LineNo} (+{line.Qty})";
                    }
                }

                // =========================
                // 7) حذف StockFifoMap المرتبط بحركات الخروج أولاً
                // =========================
                foreach (var lg in ledgers)
                {
                    if (lg.QtyOut > 0)
                    {
                        // حذف كل الـ FIFO Maps المرتبطة بهذه الحركة
                        var fifoMaps = await _context.Set<StockFifoMap>()
                            .Where(f => f.OutEntryId == lg.EntryId)
                            .ToListAsync();

                        if (fifoMaps.Any())
                        {
                            // إرجاع RemainingQty للدخلات المرتبطة
                            foreach (var map in fifoMaps)
                            {
                                var inLedger = await _context.StockLedger
                                    .FirstOrDefaultAsync(x => x.EntryId == map.InEntryId);

                                if (inLedger != null)
                                {
                                    inLedger.RemainingQty = (inLedger.RemainingQty ?? 0) + map.Qty;
                                }
                            }

                            _context.Set<StockFifoMap>().RemoveRange(fifoMaps);
                        }
                    }
                }

                // =========================
                // 8) حذف StockLedger ثم حذف سطر الفاتورة
                // =========================
                if (ledgers.Count > 0)
                    _context.StockLedger.RemoveRange(ledgers);   // ✅ حذف وليس عكس

                _context.SalesInvoiceLines.Remove(line);

                await _context.SaveChangesAsync();

                // =========================
                // 9) تحديث هيدر الفاتورة (داخل نفس الـ Transaction)
                // =========================
                await _docTotals.RecalcSalesInvoiceTotalsAsync(dto.SIId);

                await _context.SaveChangesAsync();

                // ✅ Commit بعد كل شيء يخص الداتا
                await tx.CommitAsync();

                // =========================
                // 10) LogActivity (بعد الـ Commit حتى لا يعطل المسح)
                // =========================
                try
                {
                    await _activityLogger.LogAsync(
                        UserActionType.Delete,
                        "SalesInvoiceLine",
                        dto.SIId,
                        $"SIId={dto.SIId} | LineNo={dto.LineNo} | ProdId={line.ProdId} | Qty={line.Qty}"
                    );
                }
                catch
                {
                    // تعليق: لا نوقف العملية لو اللوج حصل فيه مشكلة
                }

                // =========================
                // 11) رجّع السطور + الإجماليات بعد المسح
                // =========================
                var linesNow = await _context.SalesInvoiceLines
                    .Where(l => l.SIId == dto.SIId)
                    .OrderBy(l => l.LineNo)
                    .ToListAsync();

                var prodIds = linesNow.Select(l => l.ProdId).Distinct().ToList();
                var prodMap = await _context.Products
                    .AsNoTracking()
                    .Where(p => prodIds.Contains(p.ProdId))
                    .Select(p => new { p.ProdId, p.ProdName })
                    .ToDictionaryAsync(x => x.ProdId, x => x.ProdName ?? "");

                int totalLines = linesNow.Count;
                int totalItems = linesNow.Select(x => x.ProdId).Distinct().Count();
                int totalQty = linesNow.Sum(x => x.Qty);

                decimal totalRetail = linesNow.Sum(x => x.Qty * x.UnitSalePrice);
                decimal totalDiscount = linesNow.Sum(x => x.DiscountValue);
                decimal totalAfterDiscount = linesNow.Sum(x => x.LineTotalAfterDiscount);
                decimal taxAmount = linesNow.Sum(x => x.TaxValue);
                decimal netTotal = totalAfterDiscount + taxAmount;

                var linesDto = linesNow.Select(l =>
                {
                    var name = prodMap.TryGetValue(l.ProdId, out var n) ? n : "";

                    return new
                    {
                        lineNo = l.LineNo,
                        prodId = l.ProdId,
                        prodName = name,
                        qty = l.Qty,
                        priceRetail = l.UnitSalePrice,
                        discPct = l.Disc1Percent,
                        batchNo = l.BatchNo,
                        expiry = l.Expiry?.ToString("yyyy-MM-dd"),
                        lineValue = l.LineTotalAfterDiscount
                    };
                }).ToList();

                return Json(new
                {
                    ok = true,
                    message = "تم حذف السطر بنجاح.",
                    lines = linesDto,
                    totals = new
                    {
                        totalLines,
                        totalItems,
                        totalQty,
                        totalRetail,
                        totalDiscount,
                        totalAfterDiscount,
                        taxAmount,
                        netTotal
                    }
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { ok = false, message = ex.Message });
            }
        }

        // ================================================================
        // DTO: بيانات مسح كل السطور (جاية من AJAX)
        // ================================================================
        public class ClearAllLinesJsonDto
        {
            public int SIId { get; set; } // متغير: رقم فاتورة البيع
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClearAllLinesJson([FromBody] ClearAllLinesJsonDto dto)
        {
            try
            {
                // =========================
                // 0) فحص المدخلات
                // =========================
                if (dto == null || dto.SIId <= 0)
                    return BadRequest(new { ok = false, message = "بيانات المسح غير صحيحة." });

                // =========================
                // 1) تحميل الفاتورة
                // =========================
                var invoice = await _context.SalesInvoices
                    .FirstOrDefaultAsync(x => x.SIId == dto.SIId);

                if (invoice == null)
                    return NotFound(new { ok = false, message = "الفاتورة غير موجودة." });

                // =========================
                // 1.1) إذا كانت الفاتورة مُرحّلة، نفتحها تلقائياً قبل المسح
                // (لأن الترحيل بعد المسح سيعمل قيود عكسية + قيد جديد)
                // =========================
                bool wasPosted = invoice.IsPosted;
                if (wasPosted)
                {
                    // فتح الفاتورة تلقائياً (إرجاع IsPosted = false) قبل المسح
                    invoice.IsPosted = false;
                    invoice.Status = "فاتورة مفتوحة";
                    await _context.SaveChangesAsync();
                }

                // =========================
                // 2) تحميل كل سطور الفاتورة
                // =========================
                var lines = await _context.SalesInvoiceLines
                    .Where(l => l.SIId == dto.SIId)
                    .OrderBy(l => l.LineNo)
                    .ToListAsync();

                if (lines.Count == 0)
                {
                    // رجّع نفس شكل RemoveLineJson (lines + totals)
                    return Json(new
                    {
                        ok = true,
                        message = "لا توجد أصناف لمسحها.",
                        lines = new object[0],
                        totals = new { totalLines = 0, totalItems = 0, totalQty = 0, totalRetail = 0m, totalDiscount = 0m, totalAfterDiscount = 0m, taxAmount = 0m, netTotal = 0m }
                    });
                }

                // =========================
                // 3) تحميل كل حركات StockLedger المرتبطة بالفاتورة مرة واحدة
                // =========================
                var allLedgers = await _context.StockLedger
                    .Where(x =>
                        x.SourceType == "Sales" &&  // ثابت: مصدر الحركة بيع
                        x.SourceId == dto.SIId)     // ثابت: رقم الفاتورة
                    .ToListAsync();

                // =========================
                // 4) نبدأ Transaction بعد ما اتأكدنا إن المسح مسموح
                // =========================
                await using var tx = await _context.Database.BeginTransactionAsync();

                // =========================
                // 5) تحديث StockBatches (إرجاع الكمية للمخزن — زياده QtyOnHand)
                //    (بنفس منطق RemoveLineJson لكن على كل السطور)
                // =========================
                foreach (var line in lines)
                {
                    var batchNo = string.IsNullOrWhiteSpace(line.BatchNo) ? null : line.BatchNo.Trim(); // متغير: رقم التشغيلة
                    var expDate = line.Expiry?.Date;                                                    // متغير: تاريخ الصلاحية

                    if (!string.IsNullOrWhiteSpace(batchNo) && expDate.HasValue)
                    {
                        var exp = expDate.Value.Date;

                        var sbRow = await _context.StockBatches
                            .FirstOrDefaultAsync(x =>
                                x.WarehouseId == invoice.WarehouseId &&   // متغير: المخزن
                                x.ProdId == line.ProdId &&                // متغير: الصنف
                                x.BatchNo == batchNo &&                    // متغير: التشغيلة
                                x.Expiry.HasValue &&
                                x.Expiry.Value.Date == exp);

                        if (sbRow != null)
                        {
                            sbRow.QtyOnHand += line.Qty; // ✅ إرجاع الكمية للمخزن (زيادة رصيد البيع)

                            sbRow.UpdatedAt = DateTime.UtcNow;
                            sbRow.Note = $"SI:{dto.SIId} ClearAll (Line:{line.LineNo}) (+{line.Qty})";

                            // ❌ ممنوع حذف صف StockBatches حتى لو الرصيد = 0 (حسب الاتفاق)
                        }
                    }
                }

                // =========================
                // 6) حذف StockFifoMap المرتبط بحركات الخروج أولاً
                // =========================
                foreach (var lg in allLedgers)
                {
                    if (lg.QtyOut > 0)
                    {
                        // حذف كل الـ FIFO Maps المرتبطة بهذه الحركة
                        var fifoMaps = await _context.Set<StockFifoMap>()
                            .Where(f => f.OutEntryId == lg.EntryId)
                            .ToListAsync();

                        if (fifoMaps.Any())
                        {
                            // إرجاع RemainingQty للدخلات المرتبطة
                            foreach (var map in fifoMaps)
                            {
                                var inLedger = await _context.StockLedger
                                    .FirstOrDefaultAsync(x => x.EntryId == map.InEntryId);

                                if (inLedger != null)
                                {
                                    inLedger.RemainingQty = (inLedger.RemainingQty ?? 0) + map.Qty;
                                }
                            }

                            _context.Set<StockFifoMap>().RemoveRange(fifoMaps);
                        }
                    }
                }

                // =========================
                // 7) حذف StockLedger (كل قيود الفاتورة) ثم حذف كل سطور الفاتورة
                // =========================
                if (allLedgers.Count > 0)
                    _context.StockLedger.RemoveRange(allLedgers);

                _context.SalesInvoiceLines.RemoveRange(lines);

                await _context.SaveChangesAsync();

                // =========================
                // 8) تحديث هيدر الفاتورة (داخل نفس الـ Transaction)
                // =========================
                await _docTotals.RecalcSalesInvoiceTotalsAsync(dto.SIId);

                await _context.SaveChangesAsync();

                await tx.CommitAsync();

                // =========================
                // 9) LogActivity (بعد الـ Commit)
                // =========================
                try
                {
                    await _activityLogger.LogAsync(
                        UserActionType.Delete,
                        "SalesInvoiceLines",
                        dto.SIId,
                        $"SIId={dto.SIId} | ClearAllLines | LinesCount={lines.Count}"
                    );
                }
                catch
                {
                    // تعليق: لا نوقف العملية لو اللوج حصل فيه مشكلة
                }

                // =========================
                // 10) ترحيل الفاتورة تلقائياً بعد مسح الأصناف (بنفس منطق فاتورة المشتريات)
                // - PostSalesInvoiceAsync سيعمل قيود عكسية تلقائياً إذا كانت هناك مرحلة سابقة
                // - ثم سينشئ قيد جديد للفاتورة (حتى لو كانت فارغة)
                // =========================
                try
                {
                    // ترحيل الفاتورة (سيعمل قيود عكسية إذا كانت هناك مرحلة سابقة، ثم قيد جديد)
                    await _ledgerPostingService.PostSalesInvoiceAsync(dto.SIId, User.Identity?.Name);
                }
                catch (Exception postEx)
                {
                    // تعليق: لو الترحيل فشل، نرجع رسالة خطأ واضحة
                    throw new Exception($"تم مسح الأصناف بنجاح، لكن الترحيل فشل: {postEx.Message}");
                }

                // =========================
                // 11) رجّع السطور + الإجماليات بعد المسح (هتكون فاضية)
                // =========================
                return Json(new
                {
                    ok = true,
                    message = wasPosted 
                        ? "تم مسح جميع الأصناف وترحيل الفاتورة تلقائياً (تم عمل قيود عكسية وقيد جديد)." 
                        : "تم مسح جميع الأصناف وترحيل الفاتورة تلقائياً.",
                    lines = new object[0],
                    totals = new { totalLines = 0, totalItems = 0, totalQty = 0, totalRetail = 0m, totalDiscount = 0m, totalAfterDiscount = 0m, taxAmount = 0m, netTotal = 0m },
                    isPosted = true // تعليق: الفاتورة أصبحت مُرحّلة
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { ok = false, message = ex.Message });
            }
        }




        [HttpPost]
        [IgnoreAntiforgeryToken] // تعليق: لأن الاستدعاء من fetch بدون توكن
        public async Task<IActionResult> PostInvoice(int id)
        {
            // ================================
            // 0) معرفة هل الطلب Ajax أم لا
            // ================================
            bool isAjax =
                string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                || Request.Headers["Accept"].ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);

            try
            {
                // ================================
                // 1) تحميل الفاتورة (للتحقق السريع فقط)
                // ================================
                var invoice = await _context.SalesInvoices
                    .AsNoTracking() // تعليق: هنا قراءة فقط
                    .FirstOrDefaultAsync(x => x.SIId == id);

                if (invoice == null)
                {
                    if (isAjax) return NotFound(new { ok = false, message = "الفاتورة غير موجودة." });
                    TempData["Error"] = "الفاتورة غير موجودة.";
                    return RedirectToAction("Index");
                }

                // ================================
                // 2) منع الترحيل لو مترحّلة بالفعل
                // ================================
                if (invoice.IsPosted)
                {
                    if (isAjax) return BadRequest(new { ok = false, message = "هذه الفاتورة مترحّلة بالفعل." });
                    TempData["Error"] = "هذه الفاتورة مترحّلة بالفعل.";
                    return RedirectToAction("Show", new { id = invoice.SIId });
                }

                // ================================
                // 3) تحقق سريع قبل الترحيل
                // ================================
                if (invoice.CustomerId <= 0)
                {
                    var m = "يجب اختيار العميل قبل الترحيل.";
                    if (isAjax) return BadRequest(new { ok = false, message = m });
                    TempData["Error"] = m;
                    return RedirectToAction("Show", new { id = invoice.SIId });
                }

                if (invoice.WarehouseId <= 0)
                {
                    var m = "يجب اختيار المخزن قبل الترحيل.";
                    if (isAjax) return BadRequest(new { ok = false, message = m });
                    TempData["Error"] = m;
                    return RedirectToAction("Show", new { id = invoice.SIId });
                }

                // ================================
                // 4) تنفيذ الترحيل (كل المنطق داخل السيرفيس)
                // - السيرفيس هو اللي يكتب: IsPosted + Status(مرحلة X) + PostedAt/PostedBy
                // ================================
                await _ledgerPostingService.PostSalesInvoiceAsync(id, User.Identity?.Name);

                // ================================
                // 5) تسجيل نشاط
                // ================================
                await _activityLogger.LogAsync(
                    actionType: UserActionType.Post,
                    entityName: "SalesInvoice",
                    entityId: id,
                    description: $"ترحيل فاتورة مبيعات رقم {id}"
                );

                // ================================
                // 6) إعادة تحميل الفاتورة بعد الترحيل (نأخذ الحالة من DB)
                // ================================
                var updated = await _context.SalesInvoices
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.SIId == id);

                // متغير: الليبل المعروض للمستخدم
                string postedLabel = updated?.Status ?? "مرحلة 1";

                if (isAjax)
                {
                    return Ok(new
                    {
                        ok = true,
                        message = "تم ترحيل الفاتورة بنجاح.",
                        isPosted = updated?.IsPosted ?? true,

                        // مهم: نخلي الواجهة تعتمد على Status الحقيقي في الجدول
                        status = postedLabel,
                        postedLabel = postedLabel
                    });
                }

                TempData["Success"] = "تم ترحيل الفاتورة بنجاح";
                return RedirectToAction("Show", new { id });
            }
            catch (Exception ex)
            {
                // =========================================================
                // تعليق: تجميع رسالة خطأ واضحة (Message + InnerException)
                // الهدف: نعرف "السبب الحقيقي" لفشل SaveChanges داخل EF
                // =========================================================

                // متغير: الرسالة الأساسية
                var msg = string.IsNullOrWhiteSpace(ex.Message) ? "حدث خطأ أثناء الترحيل." : ex.Message;

                // متغير: رسالة الـ InnerException (لو موجودة)
                var inner1 = ex.InnerException?.Message;

                // متغير: رسالة InnerException داخل InnerException (أحيانًا EF بيحط التفاصيل هنا)
                var inner2 = ex.InnerException?.InnerException?.Message;

                // متغير: تجميع الرسائل
                var details = msg;

                if (!string.IsNullOrWhiteSpace(inner1))
                    details += " | INNER: " + inner1;

                if (!string.IsNullOrWhiteSpace(inner2))
                    details += " | INNER2: " + inner2;

                // =========================================================
                // تعليق: لو الخطأ من EF DbUpdateException نطلع أعمق رسالة
                // =========================================================
                if (ex is Microsoft.EntityFrameworkCore.DbUpdateException dbEx)
                {
                    var dbInner1 = dbEx.InnerException?.Message;
                    var dbInner2 = dbEx.InnerException?.InnerException?.Message;

                    if (!string.IsNullOrWhiteSpace(dbInner1) && (inner1 != dbInner1))
                        details += " | DB-INNER: " + dbInner1;

                    if (!string.IsNullOrWhiteSpace(dbInner2) && (inner2 != dbInner2))
                        details += " | DB-INNER2: " + dbInner2;
                }

                // =========================================================
                // تعليق: لو فيه تعارض تحديث (Concurrency) نوضح ده
                // =========================================================
                if (ex is Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
                {
                    details = "فشل الترحيل بسبب تعارض تحديث (Concurrency). حاول إعادة تحميل الفاتورة ثم الترحيل مرة أخرى."
                              + " | DETAILS: " + details;
                }

                // =========================================================
                // (نفس سلوكك) Ajax / غير Ajax
                // =========================================================
                if (isAjax)
                    return BadRequest(new { ok = false, message = "فشل الترحيل: " + details });

                TempData["Error"] = "فشل الترحيل: " + details;
                return RedirectToAction("Show", new { id });
            }

        }









        // ================================
        //      فتح الفاتورة المرحلة   
        // ================================

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> OpenInvoice(int id)
        {
            // ================================
            // 0) تحديد هل الطلب Ajax أم لا
            // ================================
            bool isAjax =
                string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                || Request.Headers["Accept"].ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);

            try
            {
                // ================================
                // 1) تحميل الفاتورة
                // ================================
                var invoice = await _context.SalesInvoices
                    .FirstOrDefaultAsync(x => x.SIId == id);

                if (invoice == null)
                {
                    if (isAjax) return NotFound(new { ok = false, message = "الفاتورة غير موجودة." });
                    TempData["Error"] = "الفاتورة غير موجودة.";
                    return RedirectToAction("Index");
                }

                // ================================
                // 2) لازم تكون مترحلة عشان ينفع "فتح"
                // ================================
                if (!invoice.IsPosted)
                {
                    if (isAjax) return BadRequest(new { ok = false, message = "هذه الفاتورة ليست مُرحّلة، لا يوجد ما يمكن فتحه." });
                    TempData["Error"] = "هذه الفاتورة ليست مُرحّلة.";
                    return RedirectToAction("Show", new { id = invoice.SIId });
                }

                // ================================
                // 3) (لاحقًا) صلاحية فتح التعديل
                // ================================
                // تعليق: هنا هتحط شرط الصلاحية لما نعمل نظام Permissions
                // مثال:
                // if (!User.IsInRole("Admin")) return Forbid();

                // ================================
                // 4) فتح الفاتورة للتعديل (إلغاء الترحيل)
                // ================================
                invoice.IsPosted = false;                 // متغير: إلغاء حالة الترحيل
                invoice.Status = "مفتوحة للتعديل";                  // متغير: حالة عرضية
                invoice.PostedAt = null;                  // متغير: مسح وقت الترحيل
                invoice.PostedBy = null;                  // متغير: مسح من قام بالترحيل
                invoice.UpdatedAt = DateTime.Now;         // متغير: آخر تعديل

                await _context.SaveChangesAsync();

                // ================================
                // 5) تسجيل نشاط
                // ================================
                await _activityLogger.LogAsync(
                    actionType: UserActionType.Unpost,     // متغير: نوع العملية (فتح/إلغاء ترحيل)
                    entityName: "SalesInvoice",
                    entityId: invoice.SIId,
                    description: $"فتح فاتورة مبيعات رقم {invoice.SIId} للتعديل"
                );

                // ================================
                // 6) رد Ajax
                // ================================
                if (isAjax)
                {
                    return Ok(new
                    {
                        ok = true,
                        message = "تم فتح الفاتورة للتعديل.",
                        isPosted = false,
                        status = invoice.Status
                    });
                }

                TempData["Success"] = "تم فتح الفاتورة للتعديل.";
                return RedirectToAction("Show", new { id = invoice.SIId });
            }
            catch (Exception ex)
            {
                var msg = string.IsNullOrWhiteSpace(ex.Message) ? "حدث خطأ أثناء فتح الفاتورة." : ex.Message;

                if (isAjax)
                    return BadRequest(new { ok = false, message = "فشل فتح الفاتورة: " + msg });

                TempData["Error"] = "فشل فتح الفاتورة: " + msg;
                return RedirectToAction("Show", new { id });
            }
        }




        // =========================
        // دالة مشتركة لبناء استعلام فواتير المبيعات
        // (بحث + فلتر رقم من/إلى + ترتيب)
        // =========================
        private IQueryable<SalesInvoice> BuildSalesInvoicesQuery(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            int? fromCode,
            int? toCode)
        {
            // 1) الاستعلام الأساسي (قراءة فقط لتحسين الأداء)
            IQueryable<SalesInvoice> q = _context.SalesInvoices.AsNoTracking();

            // 2) فلتر رقم الفاتورة من/إلى (SIId)
            if (fromCode.HasValue)
                q = q.Where(x => x.SIId >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(x => x.SIId <= toCode.Value);

            // 3) الحقول النصية للبحث
            var stringFields = new Dictionary<string, Expression<Func<SalesInvoice, string?>>>
            {
                ["status"] = x => x.Status,                     // حالة الفاتورة
                ["payment"] = x => x.PaymentMethod,              // طريقة الدفع
                ["date"] = x => x.SIDate.ToString("yyyy-MM-dd") // بحث بالتاريخ كنص (اختياري)
            };

            // 4) الحقول الرقمية للبحث
            var intFields = new Dictionary<string, Expression<Func<SalesInvoice, int>>>
            {
                ["id"] = x => x.SIId,         // رقم الفاتورة
                ["customer"] = x => x.CustomerId,   // كود العميل
                ["warehouse"] = x => x.WarehouseId   // كود المخزن
            };

            // 5) حقول الترتيب — نفس أسماء الأعمدة في الواجهة
            var orderFields = new Dictionary<string, Expression<Func<SalesInvoice, object>>>
            {
                ["date"] = x => x.SIDate,       // تاريخ الفاتورة
                ["id"] = x => x.SIId,         // رقم الفاتورة
                ["time"] = x => x.SITime!,      // وقت الفاتورة
                ["customer"] = x => x.CustomerId,
                ["warehouse"] = x => x.WarehouseId,
                ["net"] = x => x.NetTotal,
                ["status"] = x => x.Status!,
                ["posted"] = x => x.IsPosted      // مرحّل أم لا
            };

            // 6) تطبيق البحث + الترتيب عن طريق الإكستنشن الموحّد
            q = q.ApplySearchSort(
                search, searchBy,
                sort, dir,
                stringFields, intFields, orderFields,
                defaultSearchBy: "all",
                defaultSortBy: "date");

            return q;
        }





        // =========================================================
        // Show — عرض فاتورة المبيعات (Shell كامل أو Body فقط)
        // - يدعم frag=body (لتبديل جسم الفاتورة بسرعة بدون Reload كامل)
        // - يدعم frame=1 (نمط التابات)
        // - لو رقم الفاتورة غير موجود:
        //   - لو frag=body => NotFound برسالة (الـ JS يتعامل)
        //   - لو فتح عادي => يفتح أقرب فاتورة (التالي ثم السابق) بدل صفحة فاضية
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Show(int id, string? frag = null, int? frame = null)
        {
            // =========================================
            // متغير: هل هذا الطلب يطلب "Body فقط"؟
            // - frag=body معناها: نريد جزء الصفحة المتغير فقط (بدون Layout)
            // =========================================
            bool isBodyOnly = string.Equals(frag, "body", StringComparison.OrdinalIgnoreCase); // متغير: هل نعرض الجسم فقط؟

            // =========================================
            // ✅ Frame Guard (لنمط التابات)
            // - لو frag=body (Fetch) => ممنوع Redirect عشان مايحصلش Reload كامل
            // - لو فتح عادي => نُجبر frame=1 عشان يفتح داخل التابات دائمًا
            // =========================================
            if (!isBodyOnly && frame != 1)
                return RedirectToAction(nameof(Show), new { id = id, frag = frag, frame = 1 });

            // =========================================
            // 0) تمرير حالة الـ Fragment للـ View
            // - الـ View سيقرر: يعرض Shell كامل أو Body فقط
            // =========================================
            ViewBag.Fragment = frag; // متغير: نوع العرض (null أو body)

            // =========================================
            // 1) تحميل فاتورة المبيعات المطلوبة (قراءة فقط لتحسين الأداء)
            // =========================================
            var invoice = await _context.SalesInvoices
                .Include(s => s.Customer)                    // متغير: العميل
                    .ThenInclude(c => c.Governorate)         // متغير: المحافظة
                .Include(s => s.Customer)
                    .ThenInclude(c => c.District)            // متغير: الحي/المركز
                .Include(s => s.Customer)
                    .ThenInclude(c => c.Area)                // متغير: المنطقة
                .Include(s => s.Lines)                       // متغير: سطور الفاتورة
                    .ThenInclude(l => l.Product)             // متغير: الصنف داخل السطر
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.SIId == id);     // ✅ المفتاح عندك SIId

            // =========================================
            // 2) لو الفاتورة غير موجودة (ممسوحة / رقم غلط)
            // =========================================
            if (invoice == null)
            {
                // ✅ لو Body فقط: نرجع NotFound برسالة واضحة (بدون Redirect)
                if (isBodyOnly)
                    return NotFound($"فاتورة المبيعات رقم ({id}) غير موجودة (قد تكون ممسوحة).");

                // -----------------------------------------
                // منطق فتح أقرب فاتورة بدل صفحة فاضية
                // -----------------------------------------

                // متغير: أقرب "التالي" (أصغر رقم أكبر من id)
                int? nearestNext = await _context.SalesInvoices
                    .AsNoTracking()
                    .Where(x => x.SIId > id)
                    .OrderBy(x => x.SIId)
                    .Select(x => (int?)x.SIId)
                    .FirstOrDefaultAsync();

                if (nearestNext.HasValue && nearestNext.Value > 0)
                {
                    TempData["Error"] = $"رقم فاتورة المبيعات ({id}) غير موجود (قد تكون ممسوحة). تم فتح الفاتورة التالية رقم ({nearestNext.Value}).";
                    return RedirectToAction(nameof(Show), new { id = nearestNext.Value, frag = (string?)null, frame = 1 });
                }

                // متغير: أقرب "السابق" (أكبر رقم أقل من id)
                int? nearestPrev = await _context.SalesInvoices
                    .AsNoTracking()
                    .Where(x => x.SIId < id)
                    .OrderByDescending(x => x.SIId)
                    .Select(x => (int?)x.SIId)
                    .FirstOrDefaultAsync();

                if (nearestPrev.HasValue && nearestPrev.Value > 0)
                {
                    TempData["Error"] = $"رقم فاتورة المبيعات ({id}) غير موجود (قد تكون ممسوحة). تم فتح الفاتورة السابقة رقم ({nearestPrev.Value}).";
                    return RedirectToAction(nameof(Show), new { id = nearestPrev.Value, frag = (string?)null, frame = 1 });
                }

                // لو مفيش فواتير أصلًا
                TempData["Error"] = "لا توجد فواتير مبيعات مسجلة حالياً.";
                return RedirectToAction(nameof(Create), new { frame = 1 });
            }

            // =========================================
            // 3) تجهيز القوائم + الأوتوكومبليت (نفس أسلوبك الحالي)
            // ملاحظة: في مرجع المشتريات كنت عامل شرط لتفادي تحميل تقيل مع frag=body،
            // لكن بما إنك لم تثبّت هذا الشرط هنا بعد، سنلتزم بسلوكك الحالي بدون تغيير.
            // =========================================
            await PopulateDropDownsAsync(invoice.CustomerId, invoice.WarehouseId); // متغير: تثبيت العميل/المخزن المختارين
            await LoadProductsForAutoCompleteAsync();                               // متغير: تجهيز الداتا ليست للأصناف

            // =========================================
            // 4) حالة القفل (مقفولة لو IsPosted أو Status Posted/Closed)
            // =========================================
            ViewBag.IsLocked = invoice.IsPosted || invoice.Status == "Posted" || invoice.Status == "Closed"; // متغير: هل الفاتورة مقفولة؟

            // متغير: هل العرض داخل frame (في العرض الكامل فقط)
            ViewBag.Frame = (!isBodyOnly) ? 1 : 0; // متغير: frame flag

            // =========================================
            // 5) تجهيز نظام الأسهم (نفس نظام المشتريات)
            // - أنت قلت: نفس نظام الأسهم => نفس أسماء الـ ViewBag
            // ⚠️ نفترض عندك دالة FillSalesInvoiceNavAsync جاهزة أو ستضيفها بنفس نمط المشتريات
            // =========================================
          //  await FillSalesInvoiceNavAsync(invoice.SIId); // متغير: تجهيز First/Prev/Next/Last

            // =========================================
            // 6) عرض View "Show"
            // =========================================
            return View("Show", invoice);
        }





        // =========================
        // Index — عرض قائمة فواتير البيع
        // =========================
        public async Task<IActionResult> Index(
            string? search,              // نص البحث
            string? searchBy = "all",    // all | id | customer | warehouse | status | date
            string? sort = "date",       // date | id | time | customer | warehouse | net | status | posted
            string? dir = "desc",        // asc | desc
            int page = 1,
            int pageSize = 50,
            int? fromCode = null,        // فلتر رقم فاتورة من
            int? toCode = null)          // فلتر رقم فاتورة إلى
        {
            // بناء الاستعلام الموحّد
            var q = BuildSalesInvoicesQuery(search, searchBy, sort, dir, fromCode, toCode);

            // =========================================================
            // حساب إجمالي الصافي من نفس الاستعلام (بعد الفلاتر)
            // ✅ مهم: لازم قبل الـ PagedResult علشان ما تتحسبش على الصفحة بس
            // =========================================================
            decimal totalNet = await q.SumAsync(si => (decimal?)si.NetTotal) ?? 0m;

            // التقسيم إلى صفحات
            var model = await PagedResult<SalesInvoice>.CreateAsync(q, page, pageSize);

            // تجهيز قيم الـ ViewBag للفلاتر والواجهة
            ViewBag.Search = search ?? "";
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.Sort = sort ?? "date";
            ViewBag.Dir = (dir?.ToLower() == "asc") ? "asc" : "desc";

            // أرقام من/إلى (نستخدم أكثر من اسم لتوافق الواجهة)
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.CodeFrom = fromCode;
            ViewBag.CodeTo = toCode;

            // حقل التاريخ المستخدم في الفلترة (ثابت هنا SIDate)
            ViewBag.DateField = "SIDate";

            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalCount = model.TotalCount;

            // إجمالي الصافي
            ViewBag.TotalNet = totalNet;

            return View(model);
        }


       
        
        
        
        
        
        // =========================================================
        // Create — GET: فتح شاشة إنشاء فاتورة مبيعات جديدة
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            // =========================================================
            // (1) تجهيز موديل جديد بقيم افتراضية
            // =========================================================
            var model = new SalesInvoice
            {
                SIDate = DateTime.Today,           // متغير: تاريخ الفاتورة
                SITime = DateTime.Now.TimeOfDay,   // متغير: وقت الفاتورة
                IsPosted = false,                  // متغير: لسه مش مرحّلة
                Status = "غير مرحلة"                   // متغير: حالة مبدئية
            };

            // =========================================================
            // (2) تحديد المخزن الافتراضي (الدواء)
            // =========================================================
            int? defaultWarehouseId = await _context.Warehouses
                .AsNoTracking()
                .Where(w => w.WarehouseName != null && w.WarehouseName.Trim().Contains("الدواء"))
                .Select(w => (int?)w.WarehouseId)
                .FirstOrDefaultAsync();

            // =========================================================
            // (3) تثبيت المخزن داخل الموديل
            // =========================================================
            if (defaultWarehouseId.HasValue)
                model.WarehouseId = defaultWarehouseId.Value;

            // =========================================================
            // (4) تجهيز القوائم (عملاء + مخازن + طرق الدفع)
            // =========================================================
            await PopulateDropDownsAsync(
                selectedCustomerId: null,
                selectedWarehouseId: defaultWarehouseId
            );

            // =========================================================
            // ✅ (4.1) تجهيز الأصناف للداتا ليست (ضروري لظهور الأصناف)
            // =========================================================
            await LoadProductsForAutoCompleteAsync();

            // =========================================================
            // (5) نرجّع فيو "Show"
            // =========================================================
            return View("Show", model);
        }





        // =========================================================
        // Create — POST: حفظ بيانات الفاتورة الجديدة في قاعدة البيانات
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(SalesInvoice invoice)
        {
            // تحقق إضافي على نسبة خصم الهيدر (0..100)
            if (invoice.HeaderDiscountPercent < 0 || invoice.HeaderDiscountPercent > 100)
            {
                ModelState.AddModelError(
                    nameof(SalesInvoice.HeaderDiscountPercent),
                    "نسبة خصم الهيدر يجب أن تكون بين 0 و 100.");
            }

            // لو فيه أخطاء تحقق نرجع لنفس الشاشة (Show)
            if (!ModelState.IsValid)
            {
                // ✅ مهم: إعادة تحميل القوائم مع تثبيت الاختيارات الحالية
                await PopulateDropDownsAsync(
                    selectedCustomerId: invoice.CustomerId == 0 ? (int?)null : invoice.CustomerId,
                    selectedWarehouseId: invoice.WarehouseId == 0 ? (int?)null : invoice.WarehouseId
                );

                // ✅ مهم: تجهيز الأصناف للداتا ليست حتى لا تختفي
                await LoadProductsForAutoCompleteAsync();

                // ✅ مهم: لأن التصميم عندك يعتمد Show
                return View("Show", invoice);
            }

            // ضبط قيم التتبع
            invoice.SIDate = invoice.SIDate == default ? DateTime.Today : invoice.SIDate;
            invoice.SITime = invoice.SITime == default ? DateTime.Now.TimeOfDay : invoice.SITime;
            invoice.CreatedAt = DateTime.Now;

            if (string.IsNullOrWhiteSpace(invoice.CreatedBy))
                invoice.CreatedBy = User?.Identity?.Name ?? "system";

            _context.SalesInvoices.Add(invoice);
            await _context.SaveChangesAsync();

            TempData["Msg"] = "تم إنشاء الفاتورة بنجاح.";

            return RedirectToAction(nameof(Edit), new { id = invoice.SIId });
        }







        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            // تحقق بسيط من رقم الفاتورة
            if (id <= 0)
                return BadRequest("رقم الفاتورة غير صالح.");

            // قراءة هيدر الفاتورة + بيانات العميل + سطور الفاتورة
            var model = await _context.SalesInvoices
                .Include(si => si.Customer)   // العميل
                .Include(si => si.Lines)      // سطور الفاتورة
                .AsNoTracking()
                .FirstOrDefaultAsync(si => si.SIId == id);

            if (model == null)
                return NotFound();            // لو الفاتورة مش موجودة

            // تعبئة القوائم المنسدلة (العملاء + المخازن + ... )
            await PopulateDropDownsAsync();

            // فتح شاشة الفاتورة (Edit = شاشة عرض + أزرار فتح/ترحيل/طباعة)
            return View(model);
        }






        // =========================
        // Edit — POST: حفظ تعديل الهيدر مع RowVersion
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, SalesInvoice invoice)
        {
            // تأكد أن رقم الفاتورة في الرابط هو نفس الموجود في الموديل
            if (id != invoice.SIId)
                return NotFound();

            // تحقق إضافي على نسبة خصم الهيدر (0..100)
            if (invoice.HeaderDiscountPercent < 0 || invoice.HeaderDiscountPercent > 100)
            {
                ModelState.AddModelError(
                    nameof(SalesInvoice.HeaderDiscountPercent),
                    "النسبة يجب أن تكون بين 0 و 100");
            }

            // لو فيه أخطاء تحقق نرجع لنفس الفورم
            if (!ModelState.IsValid)
            {
                ViewBag.PaymentMethods = PaymentMethods;
                return View(invoice);
            }

            try
            {
                // تحديث وقت آخر تعديل
                invoice.UpdatedAt = DateTime.Now;

                // إعداد RowVersion الأصلي للتعامل مع التعارض
                _context.Entry(invoice)
                        .Property(x => x.RowVersion)
                        .OriginalValue = invoice.RowVersion;

                // تحديث الكيان في الـ DbContext
                _context.Update(invoice);

                // حفظ التغييرات فعلياً في SQL Server
                await _context.SaveChangesAsync();

                TempData["Msg"] = "تم تعديل الفاتورة بنجاح.";
            }
            catch (DbUpdateConcurrencyException)
            {
                // لو الفاتورة اختفت أثناء الحفظ (اتحذفت مثلاً)
                bool exists = await _context.SalesInvoices.AnyAsync(e => e.SIId == id);
                if (!exists)
                    return NotFound();

                // تعارض حقيقي: حد آخر عدّل نفس الفاتورة
                ModelState.AddModelError(
                    string.Empty,
                    "تعذر الحفظ بسبب تعديل متزامن. أعد تحميل الصفحة وحاول مجددًا.");

                ViewBag.PaymentMethods = PaymentMethods;
                return View(invoice);
            }

            return RedirectToAction(nameof(Index));
        }





        // =========================
        // Export — تصدير فواتير المبيعات (CSV يفتح في Excel)
        // =========================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            int? codeFrom,
            int? codeTo,
            bool useDateRange = false,       // موجود للتماشي مع الفورم، حالياً لا نستخدمه
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? format = "excel")        // excel | csv (حاليًا الاثنين CSV)
        {
            // نعيد استخدام نفس منطق الفلترة والترتيب
            int? fromCode = codeFrom;
            int? toCode = codeTo;

            var q = BuildSalesInvoicesQuery(search, searchBy, sort, dir, fromCode, toCode);

            var list = await q.ToListAsync();

            // تجهيز CSV بسيط — Excel يفتحه بدون مشكلة
            var sb = new StringBuilder();

            // العناوين
            sb.AppendLine("InvoiceId,Date,Time,CustomerId,WarehouseId,NetTotal,Status,IsPosted");

            foreach (var x in list)
            {
                // تحويل الوقت من TimeSpan إلى نص بصيغة hh:mm
                string timeText = x.SITime.ToString(@"hh\:mm");   // مثال: 14:35

                // سطر واحد في CSV لكل فاتورة
                var line = string.Join(",",
                    x.SIId,                                      // رقم الفاتورة
                    x.SIDate.ToString("yyyy-MM-dd"),             // التاريخ
                    timeText,                                    // الوقت كنص
                    x.CustomerId,                                // كود العميل
                    x.WarehouseId,                               // كود المخزن
                    x.NetTotal.ToString("0.00"),                 // الصافي (منسّق)
                    (x.Status ?? "").Replace(",", " "),          // الحالة (نزيل الفاصلة عشان CSV)
                    x.IsPosted ? "Yes" : "No"                    // مرحل أم لا
                );

                sb.AppendLine(line);
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var ext = (format ?? "excel").ToLower() == "csv" ? "csv" : "csv"; // الاثنين CSV حاليًا
            var fileName = $"SalesInvoices_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}";

            return File(bytes, "text/csv", fileName);
        }










        // =========================
        // Delete — GET: صفحة تأكيد الحذف
        // =========================
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
                return NotFound();

            var invoice = await _context.SalesInvoices
                                        .AsNoTracking()
                                        .FirstOrDefaultAsync(m => m.SIId == id.Value);
            if (invoice == null)
                return NotFound();

            return View(invoice);
        }





        // =========================
        // Delete — POST: تنفيذ الحذف لفاتورة واحدة (حذف عميق)
        // ✅ نفس منطق فاتورة المشتريات:
        // 1) تحديث StockBatches (إرجاع الكمية للمخزن)
        // 2) حذف StockFifoMap + إرجاع RemainingQty
        // 3) حذف StockLedger الخاص بالفاتورة
        // 4) عكس الأثر المحاسبي (Reverse) بدل حذف LedgerEntries
        // 5) حذف الهيدر (Cascade يحذف السطور)
        // =========================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var result = await TryDeleteSalesInvoiceDeepAsync(id);

            if (result.Status == DeleteInvoiceStatus.Deleted)
            {
                TempData["SuccessMessage"] = "تم حذف الفاتورة بنجاح (مع تحديث المخزون وعكس الأثر المحاسبي).";
            }
            else if (result.Status == DeleteInvoiceStatus.BlockedByFifo)
            {
                TempData["ErrorMessage"] = result.Message ?? "لا يمكن حذف الفاتورة لأن جزءًا من كميته تم البيع/الصرف منه بالفعل.";
            }
            else
            {
                TempData["ErrorMessage"] = $"تعذر حذف الفاتورة رقم {id}: {result.Message ?? "خطأ غير معروف"}";
            }

            return RedirectToAction(nameof(Index));
        }





        // =========================
        // BulkDelete — حذف مجموعة فواتير مختارة (حذف عميق)
        // ✅ نفس منطق فاتورة المشتريات: يحذف "المسموح فقط" ويترك الممنوع/الفاشل
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["ErrorMessage"] = "لم يتم اختيار أي فواتير للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var ids = selectedIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                 .Select(s => int.TryParse(s, out var n) ? (int?)n : null)
                                 .Where(n => n.HasValue)
                                 .Select(n => n!.Value)
                                 .Distinct()
                                 .ToList();

            if (!ids.Any())
            {
                TempData["ErrorMessage"] = "لم يتم اختيار أي فواتير صحيحة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var existingIds = await _context.SalesInvoices
                .Where(x => ids.Contains(x.SIId))
                .Select(x => x.SIId)
                .ToListAsync();

            if (!existingIds.Any())
            {
                TempData["ErrorMessage"] = "لم يتم العثور على الفواتير المحددة في قاعدة البيانات.";
                return RedirectToAction(nameof(Index));
            }

            int deletedCount = 0;
            int blockedCount = 0;
            int failedCount = 0;

            var blockedIds = new List<int>();
            var failedIds = new List<int>();

            foreach (var id in existingIds)
            {
                var result = await TryDeleteSalesInvoiceDeepAsync(id);

                if (result.Status == DeleteInvoiceStatus.Deleted)
                    deletedCount++;
                else if (result.Status == DeleteInvoiceStatus.BlockedByFifo)
                {
                    blockedCount++;
                    blockedIds.Add(id);
                }
                else
                {
                    failedCount++;
                    failedIds.Add(id);
                }
            }

            var summary = $"تم حذف: {deletedCount} | تم منع: {blockedCount} | فشل: {failedCount}";

            if (deletedCount > 0)
            {
                TempData["SuccessMessage"] = summary;
                if (blockedIds.Count > 0)
                    TempData["WarningMessage"] = $"فواتير ممنوع حذفها (تم البيع/الصرف منها): {string.Join(", ", blockedIds)}";
                if (failedIds.Count > 0)
                    TempData["ErrorMessage"] = $"فواتير فشل حذفها بسبب خطأ: {string.Join(", ", failedIds)}";
            }
            else
            {
                TempData["ErrorMessage"] = $"لم يتم حذف أي فاتورة. {summary}";
                if (blockedIds.Count > 0)
                    TempData["WarningMessage"] = $"فواتير ممنوع حذفها (تم البيع/الصرف منها): {string.Join(", ", blockedIds)}";
                if (failedIds.Count > 0)
                    TempData["ErrorMessage"] = $"{TempData["ErrorMessage"]} | فواتير فشل حذفها: {string.Join(", ", failedIds)}";
            }

            return RedirectToAction(nameof(Index));
        }







        // =========================
        // DeleteAll — حذف جميع فواتير المبيعات
        // (استخدمه بحذر شديد)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var invoices = await _context.SalesInvoices.ToListAsync();

            if (!invoices.Any())
            {
                TempData["Msg"] = "لا توجد فواتير لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            _context.SalesInvoices.RemoveRange(invoices);
            await _context.SaveChangesAsync();

            TempData["Msg"] = $"تم حذف جميع فواتير المبيعات ({invoices.Count}) مع السطور التابعة لها.";
            return RedirectToAction(nameof(Index));
        }



    }
}
