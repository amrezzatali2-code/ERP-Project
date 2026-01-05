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
                Status = "مسودة"                   // متغير: حالة مبدئية
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
        // Delete — POST: تنفيذ الحذف لفاتورة واحدة
        // (معتمد على Cascade لحذف السطور)
        // =========================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var invoice = await _context.SalesInvoices
                                        .FirstOrDefaultAsync(e => e.SIId == id);
            if (invoice == null)
                return NotFound();

            try
            {
                _context.SalesInvoices.Remove(invoice);
                await _context.SaveChangesAsync();
                TempData["Msg"] = "تم حذف الفاتورة (مع السطور التابعة لها إن وُجدت).";
            }
            catch (DbUpdateConcurrencyException)
            {
                ModelState.AddModelError(string.Empty, "تعذر الحذف بسبب تعارض متزامن.");
                return View(invoice);
            }

            return RedirectToAction(nameof(Index));
        }





        // =========================
        // BulkDelete — حذف مجموعة فواتير مختارة
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["Msg"] = "لم يتم اختيار أي فواتير للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var ids = selectedIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                 .Select(s => int.TryParse(s, out var n) ? (int?)n : null)
                                 .Where(n => n.HasValue)
                                 .Select(n => n!.Value)
                                 .ToList();

            if (!ids.Any())
            {
                TempData["Msg"] = "لم يتم اختيار أي فواتير صحيحة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var invoices = await _context.SalesInvoices
                                         .Where(x => ids.Contains(x.SIId))
                                         .ToListAsync();

            if (!invoices.Any())
            {
                TempData["Msg"] = "لم يتم العثور على الفواتير المحددة.";
                return RedirectToAction(nameof(Index));
            }

            // نفترض أن العلاقة مع السطور عليها Cascade Delete
            _context.SalesInvoices.RemoveRange(invoices);
            await _context.SaveChangesAsync();

            TempData["Msg"] = $"تم حذف {invoices.Count} فاتورة (مع السطور التابعة لها).";
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
