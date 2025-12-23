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
using System.Security.Claims;   // متغير: للوصول للـ Claims بتاعة اليوزر
using System.Text.RegularExpressions;






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
        // بعد ✅
        private readonly AppDbContext _context;               // سياق قاعدة البيانات
        private readonly DocumentTotalsService _docTotals;    // خدمة إجماليات المستندات
        private readonly IUserActivityLogger _activityLogger; // خدمة سجل النشاط
        private readonly ILedgerPostingService _ledgerPostingService; // متغير: خدمة الترحيل



        public PurchaseInvoicesController(AppDbContext context,
                                          DocumentTotalsService docTotals,
                                          IUserActivityLogger activityLogger,ILedgerPostingService ledgerPosting)
                                       
        {
            _context = context;
            _docTotals = docTotals;
            _activityLogger = activityLogger;
            _ledgerPostingService = ledgerPosting;     // ✅ متغير: خدمة الترحيل

        }






        // دالة خاصة: تحديث أو إنشاء الباتش + تسجيل تغيير السعر فى ProductPriceHistory
        private async Task UpdateBatchPriceAndHistoryAsync(
            int prodId,                 // كود الصنف
            int customerId,             // كود المورد (من الهيدر)
            string? batchNo,            // رقم التشغيلة من الفورم
            DateTime? expiry,           // تاريخ الصلاحية من الفورم
            decimal newPublicPrice,     // سعر الجمهور اللى كتبته فى شاشة الفاتورة
            decimal unitCost)           // تكلفة الشراء (من سطر الفاتورة)
        {
            // لو مفيش باتش أو صلاحية → مش هنعمل حاجة
            if (string.IsNullOrWhiteSpace(batchNo) || !expiry.HasValue)
                return;

            // 1) قراءة الصنف من الجدول (عشان نجيب السعر الأساسى)
            var product = await _context.Products
                .FirstOrDefaultAsync(p => p.ProdId == prodId);

            if (product == null)
                return; // حماية إضافية

            // 2) البحث عن باتش بنفس (الصنف + رقم التشغيلة + الصلاحية)
            var batch = await _context.Batches
                .FirstOrDefaultAsync(b =>
                    b.ProdId == prodId &&
                    b.BatchNo == batchNo &&
                    b.Expiry == expiry.Value);

            decimal oldPrice;   // متغير: هنحفظ فيه السعر القديم للمقارنة

            if (batch == null)
            {
                // لا يوجد باتش بهذه البيانات ⇒ إنشائه جديد
                oldPrice = product.PriceRetail;   // نعتبر السعر القديم هو سعر الصنف الأساسى

                batch = new Batch
                {
                    ProdId = prodId,
                    BatchNo = batchNo!,
                    Expiry = expiry.Value,
                    CustomerId = customerId,
                    PriceRetailBatch = newPublicPrice,
                    UnitCostDefault = unitCost,
                    EntryDate = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };

                _context.Batches.Add(batch);
            }
            else
            {
                // يوجد باتش بالفعل ⇒ نحدّث سعره وتكلفته
                oldPrice = batch.PriceRetailBatch ?? product.PriceRetail;

                batch.PriceRetailBatch = newPublicPrice;
                batch.UnitCostDefault = unitCost;
                batch.UpdatedAt = DateTime.UtcNow;

                // لو CustomerId فاضى نملأه بأول مورد معروف
                if (!batch.CustomerId.HasValue)
                    batch.CustomerId = customerId;
            }

            // 3) لو السعر اتغيّر فعلاً ⇒ نسجل فى ProductPriceHistory
            if (oldPrice != newPublicPrice)
            {
                var history = new ProductPriceHistory
                {
                    ProdId = prodId,
                    OldPrice = oldPrice,
                    NewPrice = newPublicPrice,
                    ChangeDate = DateTime.UtcNow,
                    ChangedBy = GetCurrentUserDisplayName(),  // اسم اليوزر الحالى
                    CreatedAt = DateTime.UtcNow
                };

                _context.ProductPriceHistories.Add(history);

                // نحدّث خانة آخر تغيير سعر فى جدول الأصناف كعلامة فقط
                product.LastPriceChangeDate = DateTime.UtcNow;
                _context.Products.Update(product);
            }

            // مفيش SaveChanges هنا → اللى ينده الدالة هو اللى ينفذ SaveChangesAsync
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





        // دالة مساعدة اختيارية: تجيب رقم اليوزر من الـ Claims
        private int? GetCurrentUserId()
        {
            if (User?.Identity?.IsAuthenticated == true)
            {
                var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value; // متغير: قيمة الـ Claim
                if (int.TryParse(idStr, out var id))
                    return id;
            }

            return null; // مفيش يوزر حالي
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








            // ================================================================
            // DTO: بيانات إضافة سطر (جاية من AJAX)
            // ================================================================
            public class AddLineJsonDto
            {
                public int PIId { get; set; }                    // متغير: رقم فاتورة الشراء
                public int prodId { get; set; }                  // متغير: كود الصنف
                public int qty { get; set; }                     // متغير: الكمية
                public decimal unitCost { get; set; }            // متغير: تكلفة الشراء للوحدة
                public decimal priceRetail { get; set; }         // متغير: سعر الجمهور
                public decimal purchaseDiscountPct { get; set; } // متغير: خصم الشراء %
                public string? BatchNo { get; set; }             // متغير: رقم التشغيلة
                public string? expiryText { get; set; }          // متغير: الصلاحية كنص MM/YYYY
            }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddLineJson([FromBody] AddLineJsonDto dto)
        {
            try
            {
                // =========================
                // 0) فحص سريع للمدخلات
                // =========================
                if (dto == null)
                    return BadRequest(new { ok = false, message = "لم يتم إرسال بيانات." });

                if (dto.PIId <= 0 || dto.prodId <= 0)
                    return BadRequest(new { ok = false, message = "بيانات الفاتورة/الصنف غير صحيحة." });

                if (dto.qty <= 0)
                    return BadRequest(new { ok = false, message = "الكمية يجب أن تكون أكبر من صفر." });

                if (dto.priceRetail < 0)
                    return BadRequest(new { ok = false, message = "سعر الجمهور لا يمكن أن يكون سالب." });

                // =========================
                // ✅ 0.1) تنظيف الخصم + حساب تكلفة الوحدة على السيرفر
                // =========================
                // متغير: الخصم بين 0 و 100
                var disc = dto.purchaseDiscountPct;
                if (disc < 0) disc = 0;
                if (disc > 100) disc = 100;

                // متغير: قيمة السطر بعد الخصم
                var lineValue = dto.qty * dto.priceRetail * (1m - (disc / 100m));

                // متغير: تكلفة الوحدة المحسوبة فعلياً (هي اللي لازم تتسجل في اللين + الليدجر)
                var computedUnitCost = (dto.qty > 0) ? (lineValue / dto.qty) : 0m;
                computedUnitCost = Math.Round(computedUnitCost, 2);

                // =========================
                // 1) تحويل expiryText إلى DateTime? (MM/YYYY)
                // =========================
                DateTime? expiry = null; // متغير: تاريخ الصلاحية
                if (!string.IsNullOrWhiteSpace(dto.expiryText))
                {
                    var parts = dto.expiryText.Trim().Split('/');
                    if (parts.Length == 2 &&
                        int.TryParse(parts[0], out int mm) &&
                        int.TryParse(parts[1], out int yyyy) &&
                        mm >= 1 && mm <= 12)
                    {
                        expiry = new DateTime(yyyy, mm, 1);
                    }
                }

                // متغير: التشغيلة بعد تنظيف المسافات
                var batchNo = string.IsNullOrWhiteSpace(dto.BatchNo) ? null : dto.BatchNo.Trim();

                // متغير: الصلاحية كـ Date فقط (نثبتها لتفادي اختلاف الوقت)
                DateTime? expDate = expiry?.Date;

                // =========================
                // 2) تحميل الفاتورة + السطور
                // =========================
                var invoice = await _context.PurchaseInvoices
                    .Include(p => p.Lines)
                    .FirstOrDefaultAsync(p => p.PIId == dto.PIId);

                if (invoice == null)
                    return NotFound(new { ok = false, message = "الفاتورة غير موجودة." });

                if (invoice.WarehouseId <= 0)
                    return BadRequest(new { ok = false, message = "يجب اختيار مخزن قبل إضافة سطور." });

                // =========================
                // 3) التأكد أن الصنف موجود
                // =========================
                var product = await _context.Products
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.ProdId == dto.prodId);

                if (product == null)
                    return BadRequest(new { ok = false, message = "الصنف غير موجود." });

                // =========================
                // 4) Merge: لو نفس الصنف + نفس التشغيلة + نفس الصلاحية + نفس الأسعار + نفس الخصم + نفس تكلفة الوحدة المحسوبة
                // =========================
                var existingLine = await _context.PILines.FirstOrDefaultAsync(x =>
                    x.PIId == dto.PIId &&
                    x.ProdId == dto.prodId &&
                    (x.BatchNo ?? "").Trim() == (batchNo ?? "") &&
                    ((x.Expiry.HasValue ? x.Expiry.Value.Date : (DateTime?)null) == expDate) &&
                    x.PriceRetail == dto.priceRetail &&
                    x.PurchaseDiscountPct == disc &&
                    x.UnitCost == computedUnitCost
                );

                PILine affectedLine; // متغير: السطر الذي تأثر (قديم أو جديد)
                int qtyDelta;         // متغير: الزيادة التي ستدخل المخزن

                if (existingLine != null)
                {
                    qtyDelta = dto.qty;
                    existingLine.Qty += dto.qty;

                    // تثبيت نفس البيانات
                    existingLine.BatchNo = batchNo;
                    existingLine.Expiry = expDate;

                    // ✅ تثبيت تكلفة الوحدة المحسوبة
                    existingLine.UnitCost = computedUnitCost;

                    affectedLine = existingLine;
                }
                else
                {
                    var nextLineNo = (invoice.Lines.Any() ? invoice.Lines.Max(l => l.LineNo) : 0) + 1;

                    affectedLine = new PILine
                    {
                        PIId = invoice.PIId,
                        LineNo = nextLineNo,
                        ProdId = dto.prodId,
                        Qty = dto.qty,

                        // ✅ أهم تعديل: نكتب تكلفة الوحدة المحسوبة على السيرفر
                        UnitCost = computedUnitCost,

                        PriceRetail = dto.priceRetail,
                        PurchaseDiscountPct = disc,
                        BatchNo = batchNo,
                        Expiry = expDate
                    };

                    qtyDelta = dto.qty;
                    _context.PILines.Add(affectedLine);
                }

                // =========================
                // 5) Upsert Batch (لو التشغيلة + الصلاحية موجودين)
                // =========================
                Batch? batch = null;
                if (!string.IsNullOrWhiteSpace(batchNo) && expDate.HasValue)
                {
                    var exp = expDate.Value;

                    batch = await _context.Set<Batch>()
                        .FirstOrDefaultAsync(b => b.ProdId == dto.prodId && b.BatchNo == batchNo && b.Expiry == exp);

                    if (batch == null)
                    {
                        batch = new Batch
                        {
                            ProdId = dto.prodId,
                            BatchNo = batchNo,
                            Expiry = exp,
                            PriceRetailBatch = dto.priceRetail,
                            UnitCostDefault = computedUnitCost, // ✅ مهم
                            CustomerId = invoice.CustomerId,
                            EntryDate = DateTime.UtcNow,
                            IsActive = true
                        };
                        _context.Set<Batch>().Add(batch);
                    }
                    else
                    {
                        batch.PriceRetailBatch = dto.priceRetail;
                        batch.UnitCostDefault = computedUnitCost; // ✅ مهم
                        batch.UpdatedAt = DateTime.UtcNow;
                    }
                }

                // =========================
                // 6) StockLedger دخول (للزيادة فقط)
                // =========================
                var ledger = new StockLedger
                {
                    TranDate = DateTime.UtcNow,
                    WarehouseId = invoice.WarehouseId,
                    ProdId = dto.prodId,
                    BatchNo = batchNo,
                    Expiry = expDate,
                    BatchId = null,

                    QtyIn = qtyDelta,
                    QtyOut = 0,

                    // ✅ أهم تعديل: تسجيل تكلفة الوحدة المحسوبة على السيرفر
                    UnitCost = computedUnitCost,

                    RemainingQty = qtyDelta,
                    SourceType = "Purchase",
                    SourceId = invoice.PIId,
                    SourceLine = affectedLine.LineNo,
                    Note = $"Purchase Line: {product.ProdName}"
                };

                // ⚠️ لو عندك أعمدة جديدة في StockLedger (زي PriceRetail / PurchaseDiscountPct / LineTotalCost)
                // اكتبها هنا بنفس الأسلوب، لكن لازم أعرف أسماء الخصائص بالضبط عشان ما نكسرش Build.

                _context.StockLedger.Add(ledger);

                await _context.SaveChangesAsync();

                // ✅ إعادة حساب إجماليات الهيدر (يظل كما هو)
                await _docTotals.RecalcPurchaseInvoiceTotalsAsync(dto.PIId);

                // ربط BatchId بعد الحفظ (لو اتولد)
                if (batch != null && ledger.BatchId == null)
                {
                    ledger.BatchId = batch.BatchId;
                    await _context.SaveChangesAsync();
                }

                // =========================
                // 7) LogActivity
                // =========================
                await _activityLogger.LogAsync(
                    existingLine != null ? UserActionType.Edit : UserActionType.Create,
                    "PILine",
                    affectedLine.PIId,
                    $"PIId={invoice.PIId} | ProdId={dto.prodId} | QtyDelta={qtyDelta}"
                );

                // =========================
                // 8) رجّع السطور + الإجماليات
                // =========================
                var linesNow = await _context.PILines
                    .Where(l => l.PIId == invoice.PIId)
                    .OrderBy(l => l.LineNo)
                    .ToListAsync();

                var prodIds = linesNow.Select(l => l.ProdId).Distinct().ToList();
                var prodMap = await _context.Products
                    .Where(p => prodIds.Contains(p.ProdId))
                    .Select(p => new { p.ProdId, p.ProdName })
                    .ToDictionaryAsync(x => x.ProdId, x => x.ProdName ?? "");

                int totalLines = linesNow.Count;
                int totalItems = linesNow.Select(x => x.ProdId).Distinct().Count();
                int totalQty = linesNow.Sum(x => x.Qty);

                decimal totalRetail = linesNow.Sum(x => x.Qty * x.PriceRetail);
                decimal totalDiscount = linesNow.Sum(x => (x.Qty * x.PriceRetail) * (x.PurchaseDiscountPct / 100m));
                decimal totalAfterDiscount = totalRetail - totalDiscount;

                var linesDto = linesNow.Select(l =>
                {
                    var name = prodMap.TryGetValue(l.ProdId, out var n) ? n : "";
                    var lv = (l.Qty * l.PriceRetail) * (1 - (l.PurchaseDiscountPct / 100m));

                    return new
                    {
                        lineNo = l.LineNo,
                        prodId = l.ProdId,
                        prodName = name,
                        qty = l.Qty,
                        priceRetail = l.PriceRetail,
                        discPct = l.PurchaseDiscountPct,
                        batchNo = l.BatchNo,
                        expiry = l.Expiry?.ToString("yyyy-MM-dd"),
                        lineValue = lv
                    };
                }).ToList();

                return Json(new
                {
                    ok = true,
                    message = existingLine != null ? "تم تعديل السطر (زيادة الكمية)." : "تم إضافة السطر بنجاح.",
                    lines = linesDto,
                    totals = new { totalLines, totalItems, totalQty, totalRetail, totalDiscount, totalAfterDiscount }
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { ok = false, message = ex.Message });
            }
        }












        public class SaveTaxJsonDto
        {
            public int PIId { get; set; }            // متغير: رقم الفاتورة
            public decimal taxTotal { get; set; }    // متغير: قيمة الضريبة
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveTaxJson([FromBody] SaveTaxJsonDto dto)
        {
            if (dto == null || dto.PIId <= 0)
                return BadRequest(new { ok = false, message = "بيانات الضريبة غير صحيحة." });

            var invoice = await _context.PurchaseInvoices
                .FirstOrDefaultAsync(p => p.PIId == dto.PIId);

            if (invoice == null)
                return NotFound(new { ok = false, message = "الفاتورة غير موجودة." });

            // متغير: تحديث الضريبة
            invoice.TaxTotal = dto.taxTotal;
            invoice.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // ✅ تشغيل خدمة الإجماليات
            await _docTotals.RecalcPurchaseInvoiceTotalsAsync(dto.PIId);

            // متغير: إجماليات الهيدر بعد الحساب
            var headerTotals = await _context.PurchaseInvoices.AsNoTracking()
                .Where(p => p.PIId == dto.PIId)
                .Select(p => new { p.ItemsTotal, p.DiscountTotal, p.TaxTotal, p.NetTotal })
                .FirstAsync();

            return Json(new
            {
                ok = true,
                totals = new
                {
                    totalRetail = headerTotals.ItemsTotal,
                    totalDiscount = headerTotals.DiscountTotal,
                    taxAmount = headerTotals.TaxTotal,
                    netTotal = headerTotals.NetTotal
                }
            });
        }










        public class RemoveProductLinesJsonDto
        {
            public int PIId { get; set; }     // متغير: رقم الفاتورة
            public int prodId { get; set; }   // متغير: كود الصنف
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveProductLinesJson([FromBody] RemoveProductLinesJsonDto dto)
        {
            if (dto == null || dto.PIId <= 0 || dto.prodId <= 0)
                return BadRequest(new { ok = false, message = "بيانات الحذف غير صحيحة." });

            // متغير: سطور الصنف داخل الفاتورة
            var linesToDelete = await _context.PILines
                .Where(l => l.PIId == dto.PIId && l.ProdId == dto.prodId)
                .ToListAsync();

            if (linesToDelete.Count == 0)
                return Json(new { ok = true, message = "لا يوجد سطور لهذا الصنف." });

            // متغير: أرقام السطور (للربط مع SourceLine في المخزن)
            var lineNos = linesToDelete.Select(l => l.LineNo).ToList();

            // متغير: حذف قيود المخزن المرتبطة (دخول مشتريات)
            var ledgerToDelete = await _context.StockLedger
                .Where(s =>
                    s.SourceType == "Purchase" &&
                    s.SourceId == dto.PIId &&
                    s.ProdId == dto.prodId &&
                    lineNos.Contains(s.SourceLine))
                .ToListAsync();

            _context.StockLedger.RemoveRange(ledgerToDelete);

            // حذف السطور
            _context.PILines.RemoveRange(linesToDelete);

            await _context.SaveChangesAsync();

            // LogActivity (اختياري)
            await _activityLogger.LogAsync(
                UserActionType.Delete,
                "PILine",
                dto.prodId,
                $"حذف كل سطور الصنف ProdId={dto.prodId} من فاتورة مشتريات PIId={dto.PIId}"
            );

            // ✅ إعادة حساب الإجماليات
            await _docTotals.RecalcPurchaseInvoiceTotalsAsync(dto.PIId);

            // تجهيز الداتا المحدثة للواجهة
            var linesNow = await _context.PILines.AsNoTracking()
                .Where(l => l.PIId == dto.PIId)
                .OrderBy(l => l.LineNo)
                .ToListAsync();

            var prodIds = linesNow.Select(l => l.ProdId).Distinct().ToList();

            var prodMap = await _context.Products.AsNoTracking()
                .Where(p => prodIds.Contains(p.ProdId))
                .Select(p => new { p.ProdId, p.ProdName })
                .ToListAsync();

            var nameMap = prodMap.ToDictionary(x => x.ProdId, x => x.ProdName);

            var linesDto = linesNow.Select(l => new
            {
                lineNo = l.LineNo,
                prodId = l.ProdId,
                prodName = nameMap.TryGetValue(l.ProdId, out var n) ? n : "",
                batchNo = l.BatchNo,
                expiryText = l.Expiry.HasValue ? l.Expiry.Value.ToString("yyyy-MM-dd") : "",
                qty = l.Qty,
                priceRetail = l.PriceRetail,
                purchaseDiscountPct = l.PurchaseDiscountPct,
                lineValue = (decimal)l.Qty * l.PriceRetail * (1m - (l.PurchaseDiscountPct / 100m))
            }).ToList();

            var headerTotals = await _context.PurchaseInvoices.AsNoTracking()
                .Where(p => p.PIId == dto.PIId)
                .Select(p => new { p.ItemsTotal, p.DiscountTotal, p.TaxTotal, p.NetTotal })
                .FirstAsync();

            return Json(new
            {
                ok = true,
                lines = linesDto,
                totals = new
                {
                    totalLines = linesNow.Count,
                    totalItems = linesNow.Select(x => x.ProdId).Distinct().Count(),
                    totalQty = linesNow.Sum(x => x.Qty),
                    totalRetail = headerTotals.ItemsTotal,
                    totalDiscount = headerTotals.DiscountTotal,
                    totalAfterDiscount = headerTotals.ItemsTotal - headerTotals.DiscountTotal,
                    taxAmount = headerTotals.TaxTotal,
                    totalAfterDiscountAndTax = (headerTotals.ItemsTotal - headerTotals.DiscountTotal) + headerTotals.TaxTotal,
                    netTotal = headerTotals.NetTotal
                }
            });
        }



        // =========================================================
        // DTO: بيانات طلب مسح صنف من الفاتورة (كل تشغيلاته)
        // =========================================================
        public class DeleteProductLinesJsonDto
        {
            public int PIId { get; set; }     // متغير: رقم فاتورة المشتريات
            public int prodId { get; set; }   // متغير: كود الصنف المراد مسحه
        }





        //// =========================================================
        //// POST: مسح كل سطور صنف (كل تشغيلاته) + مسح StockLedger
        //// =========================================================
        //[HttpPost]
        //[ValidateAntiForgeryToken]
        //public async Task<IActionResult> DeleteProductLinesJson([FromBody] DeleteProductLinesJsonDto dto)
        //{
        //    if (dto == null || dto.PIId <= 0 || dto.prodId <= 0)
        //        return BadRequest(new { ok = false, message = "بيانات غير صحيحة." });

        //    // متغير: تحميل الهيدر (علشان WarehouseId)
        //    var invoice = await _context.PurchaseInvoices
        //        .FirstOrDefaultAsync(x => x.PIId == dto.PIId);

        //    if (invoice == null)
        //        return NotFound(new { ok = false, message = "الفاتورة غير موجودة." });

        //    // Transaction علشان الحذف يكون “متزامن” (سطور + ليدجر + إجماليات)
        //    using var tx = await _context.Database.BeginTransactionAsync();

        //    try
        //    {
        //        // 1) نجيب سطور الصنف داخل الفاتورة
        //        var linesToDelete = await _context.PILines
        //            .Where(l => l.PIId == dto.PIId && l.ProdId == dto.prodId)
        //            .ToListAsync();

        //        if (!linesToDelete.Any())
        //        {
        //            // حتى لو مفيش سطور، نرجّع الإجماليات الحالية
        //            await _docTotals.RecalcPurchaseInvoiceTotalsAsync(dto.PIId);

        //            var header0 = await _context.PurchaseInvoices.AsNoTracking()
        //                .FirstOrDefaultAsync(x => x.PIId == dto.PIId);

        //            return Ok(new
        //            {
        //                ok = true,
        //                message = "لا توجد سطور لهذا الصنف.",
        //                totals = BuildTotalsForUi(header0),
        //                lines = await LoadInvoiceLinesForUiAsync(dto.PIId)
        //            });
        //        }

        //        // 2) نجمع أرقام السطور (LineNo) علشان نمسح StockLedger لنفس السطور
        //        var lineNos = linesToDelete.Select(x => x.LineNo).ToList();

        //        // 3) مسح سطور الفاتورة
        //        _context.PILines.RemoveRange(linesToDelete);

        //        // 4) مسح قيود المخزون التي اتكتبت عند إضافة السطر
        //        //    (نربطها بـ SourceType=Purchase و SourceId=PIId و SourceLine = LineNo)
        //        var ledgersToDelete = await _context.StockLedger
        //            .Where(s =>
        //                s.SourceType == "Purchase" &&
        //                s.SourceId == dto.PIId &&
        //                lineNos.Contains(s.SourceLine) &&
        //                s.WarehouseId == invoice.WarehouseId)
        //            .ToListAsync();

        //        if (ledgersToDelete.Any())
        //            _context.StockLedger.RemoveRange(ledgersToDelete);

        //        await _context.SaveChangesAsync();

        //        // 5) LogActivity (بدون PILineId لأنه غير موجود)
        //        await _activityLogger.LogAsync(
        //            UserActionType.Delete,
        //            "PILine",
        //            dto.PIId,
        //            $"PIId={dto.PIId} | ProdId={dto.prodId} | DeletedLines={linesToDelete.Count}"
        //        );

        //        // 6) إعادة حساب الإجماليات بعد الحذف
        //        await _docTotals.RecalcPurchaseInvoiceTotalsAsync(dto.PIId);

        //        var header = await _context.PurchaseInvoices.AsNoTracking()
        //            .FirstOrDefaultAsync(x => x.PIId == dto.PIId);

        //        await tx.CommitAsync();

        //        return Ok(new
        //        {
        //            ok = true,
        //            message = "تم مسح الصنف وجميع تشغيلاته من الفاتورة.",
        //            totals = BuildTotalsForUi(header),
        //            lines = await LoadInvoiceLinesForUiAsync(dto.PIId)
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        await tx.RollbackAsync();
        //        return BadRequest(new { ok = false, message = "حصل خطأ أثناء مسح الصنف.", error = ex.Message });
        //    }
        //}



        // =========================================================
        // دالة مساعدة: تجهيز Totals للواجهة (Badges)
        // =========================================================
        private static object BuildTotalsForUi(PurchaseInvoice? header)
        {
            if (header == null)
            {
                return new
                {
                    totalLines = 0,
                    totalItems = 0,
                    totalQty = 0,
                    totalRetail = 0m,
                    totalAfterDiscount = 0m,
                    taxAmount = 0m,
                    totalAfterDiscountAndTax = 0m,
                    netTotal = 0m
                };
            }

            var totalAfterDiscount = header.ItemsTotal - header.DiscountTotal;
            var totalAfterDiscountAndTax = totalAfterDiscount + header.TaxTotal;

            return new
            {
                // ملاحظة: totalLines/Items/Qty هنجيبهم من LoadInvoiceLinesForUiAsync في الواجهة
                totalRetail = header.ItemsTotal,
                totalAfterDiscount = totalAfterDiscount,
                taxAmount = header.TaxTotal,
                totalAfterDiscountAndTax = totalAfterDiscountAndTax,
                netTotal = header.NetTotal
            };
        }








        // =========================================================
        // دالة مساعدة: تحميل سطور الفاتورة للواجهة (مع اسم الصنف)
        // =========================================================
        private async Task<object[]> LoadInvoiceLinesForUiAsync(int piId)
        {
            // بنرجع بيانات كفاية للعرض والجروبنج في الـ JS
            var data = await (
                from l in _context.PILines.AsNoTracking()
                join p in _context.Products.AsNoTracking()
                    on l.ProdId equals p.ProdId
                where l.PIId == piId
                orderby p.ProdName, l.LineNo
                select new
                {
                    lineNo = l.LineNo,
                    prodId = l.ProdId,
                    prodName = p.ProdName,
                    batchNo = l.BatchNo,
                    expiryText = l.Expiry.HasValue ? l.Expiry.Value.ToString("yyyy-MM-dd") : "",
                    qty = l.Qty,
                    priceRetail = l.PriceRetail,
                    purchaseDiscountPct = l.PurchaseDiscountPct,
                    lineValue = ((decimal)l.Qty * l.PriceRetail) * (1m - (l.PurchaseDiscountPct / 100m))

                }
            ).ToArrayAsync();

            return data.Cast<object>().ToArray();
        }





        // =========================================================
        // دالة مساعدة: تجهيز بيانات التنقل (أول/سابق/التالي/آخر)
        // الهدف: تشتغل الأسهم حتى في الفاتورة الجديدة (PIId = 0)
        // =========================================================
        private async Task FillPurchaseInvoiceNavAsync(int currentId)
        {
            // ==============================
            // 1) أول وآخر فاتورة (Query واحد)
            // ==============================
            var minMax = await _context.PurchaseInvoices
                .AsNoTracking()
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    FirstId = g.Min(x => x.PIId),
                    LastId = g.Max(x => x.PIId)
                })
                .FirstOrDefaultAsync();

            // ==============================
            // 2) السابقة/التالية
            // ملاحظة مهمة:
            // - لو currentId = 0 (فاتورة جديدة) => السابقة = آخر فاتورة / التالية = أول فاتورة
            // ==============================
            int? prevId = null; // متغير: رقم الفاتورة السابقة
            int? nextId = null; // متغير: رقم الفاتورة التالية

            if (currentId > 0)
            {
                // السابقة = أكبر رقم أقل من الحالي
                prevId = await _context.PurchaseInvoices
                    .AsNoTracking()
                    .Where(x => x.PIId < currentId)
                    .OrderByDescending(x => x.PIId)
                    .Select(x => (int?)x.PIId)
                    .FirstOrDefaultAsync();

                // التالية = أصغر رقم أكبر من الحالي
                nextId = await _context.PurchaseInvoices
                    .AsNoTracking()
                    .Where(x => x.PIId > currentId)
                    .OrderBy(x => x.PIId)
                    .Select(x => (int?)x.PIId)
                    .FirstOrDefaultAsync();
            }
            else
            {
                // ✅ فاتورة جديدة: نخلي الأسهم شغالة كبحث سريع
                prevId = minMax?.LastId;   // السابق يأخذك لآخر فاتورة
                nextId = minMax?.FirstId;  // التالي يأخذك لأول فاتورة
            }

            // ==============================
            // 3) تعبئة ViewBag للـ View (بدون Null)
            // ==============================
            int firstId = minMax?.FirstId ?? 0;  // متغير: أول فاتورة
            int lastId = minMax?.LastId ?? 0;  // متغير: آخر فاتورة

            ViewBag.NavFirstId = firstId;
            ViewBag.NavLastId = lastId;
            ViewBag.NavPrevId = prevId ?? 0;
            ViewBag.NavNextId = nextId ?? 0;



        }








        /// <summary>
        /// شاشة إنشاء فاتورة مشتريات جديدة.
        /// </summary>
        // GET: PurchaseInvoices/Create
        public async Task<IActionResult> Create()
        {
            // ==============================
            // 0) قراءة اسم المستخدم الحالى من الـ Claims
            // ==============================
            // متغير: الاسم المعروض للمستخدم (DisplayName) لو موجود
            var currentUserDisplayName = User.FindFirst("DisplayName")?.Value;

            // لو مش موجود نرجع لاسم الدخول العادى أو System كافتراضى
            if (string.IsNullOrWhiteSpace(currentUserDisplayName))
            {
                // متغير: اسم الدخول (UserName) من الـ Identity
                var loginName = User.Identity?.Name;

                currentUserDisplayName = string.IsNullOrWhiteSpace(loginName)
                    ? "System"          // لو مفيش يوزر (حالات نادرة)
                    : loginName;        // استخدم اسم الدخول
            }

            // ==============================
            // 1) تجهيز موديل الفاتورة الجديدة بالقيم الافتراضية
            // ==============================
            var model = new PurchaseInvoice
            {
                PIDate = DateTime.Today,      // متغير: تاريخ الفاتورة الافتراضى = تاريخ اليوم
                CreatedAt = DateTime.UtcNow,     // متغير: وقت إنشاء السجل (UTC)               
                Status = "Draft",             // متغير: حالة الفاتورة = مسودة
                IsPosted = false,                // متغير: الفاتورة غير مُرحّلة عند الإنشاء
                CreatedBy = GetCurrentUserDisplayName()   // متغير: اسم كاتب الفاتورة من اليوزر الحالي
            };

            // ==============================
            // 2) تعيين المخزن الافتراضى للفاتورة
            // ==============================
            // متغير: رقم المخزن الافتراضى (مثلاً مخزن "الدواء" أو أول مخزن فى الجدول)
            var defaultWarehouseId = await GetDefaultWarehouseIdAsync();

            // لو أكبر من صفر ⇒ عندنا مخزن فعلاً
            if (defaultWarehouseId > 0)
            {
                model.WarehouseId = defaultWarehouseId;  // متغير: تعيين المخزن الافتراضى للفاتورة الجديدة
            }

            // ==============================
            // 3) تجهيز القوائم المنسدلة والأوتوكومبليت
            // ==============================

            // متغير: تحميل الموردين + المخازن مع تمرير المخزن/المورد المختارين (حتى لو 0 عادى)
            await PopulateDropDownsAsync(model.CustomerId, model.WarehouseId);

            // متغير: تحميل الأصناف للأوتوكومبليت فى سطور الفاتورة
            await LoadProductsForAutoCompleteAsync();

            // تسجيل حركة "فتح شاشة فاتورة مشتريات جديدة" فى سجل النشاط
            await _activityLogger.LogAsync(
                UserActionType.View,                 // نوع العملية: فتح/عرض
                "PurchaseInvoice",                   // اسم الكيان
                null,                                // مفيش رقم فاتورة محفوظ لسه
                $"فتح شاشة إنشاء فاتورة مشتريات جديدة بواسطة {GetCurrentUserDisplayName()}"
            );



            // ✅ تجهيز الأسهم حتى في الفاتورة الجديدة (PIId = 0)
            await FillPurchaseInvoiceNavAsync(model.PIId);

            // ==============================
            // 4) فتح شاشة Show بنفس الموديل (نظام شاشة موحدة للإنشاء/التعديل)
            // ==============================
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
            return RedirectToAction(nameof(Show), new { id = model.PIId, frame = 1 });

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
            return RedirectToAction(nameof(Show), new { id = invoice.PIId, frame = 1 });

        }







        [HttpGet]
        public async Task<IActionResult> Show(int id, string? frag = null, int? frame = null)
        {
            // =========================================
            // متغير: هل هذا الطلب يطلب "Body فقط"؟
            // - frag=body معناها: نريد جزء الصفحة المتغير فقط (بدون Layout وبدون شريط الأزرار)
            // =========================================
            bool isBodyOnly = string.Equals(frag, "body", StringComparison.OrdinalIgnoreCase); // متغير: هل نعرض الجسم فقط؟

            // =========================================
            // ✅ Frame Guard (لنمط التابات)
            // مهم جدًا:
            // - لو frag=body (Fetch) => ممنوع Redirect للـ frame=1 لأننا لا نريد Reload كامل
            // - لو فتح عادي => نُجبر frame=1 عشان يفتح بنفس التصميم داخل التابات دائمًا
            // =========================================
            if (!isBodyOnly && frame != 1)
                return RedirectToAction(nameof(Show), new { id = id, frag = frag, frame = 1 });

            // =========================================
            // 0) تمرير حالة الـ Fragment للـ View
            // - الـ View سيقرر: يعرض Shell كامل أو Body فقط
            // =========================================
            ViewBag.Fragment = frag; // متغير: نوع العرض (null أو body)

            // =========================================
            // 1) محاولة تحميل الفاتورة المطلوبة
            // =========================================
            var invoice = await _context.PurchaseInvoices
                .Include(p => p.Customer)
                .Include(p => p.Lines)
                    .ThenInclude(l => l.Product)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PIId == id);

            // =========================================
            // 2) لو الفاتورة غير موجودة (ممسوحة / رقم غلط)
            // =========================================
            if (invoice == null)
            {
                // -----------------------------------------
                // ✅ حالة خاصة: frag=body (تنقل/Fetch)
                // هنا ممنوع Redirect لأنه سيؤدي لصفحة فاضية/فلاش
                // الأفضل: نرجّع 404 برسالة واضحة، والـ JS يتعامل معها
                // -----------------------------------------
                if (isBodyOnly)
                {
                    return NotFound($"الفاتورة رقم ({id}) غير موجودة (قد تكون ممسوحة).");
                }

                // -----------------------------------------
                // منطقك الحالي (فتح أقرب فاتورة بدل NotFound داخل iframe)
                // -----------------------------------------

                // متغير: نحاول نلاقي "التالي" (أصغر رقم أكبر من id)
                int? nearestNext = await _context.PurchaseInvoices
                    .AsNoTracking()
                    .Where(x => x.PIId > id)
                    .OrderBy(x => x.PIId)
                    .Select(x => (int?)x.PIId)
                    .FirstOrDefaultAsync();

                if (nearestNext.HasValue && nearestNext.Value > 0)
                {
                    TempData["Error"] = $"رقم الفاتورة ({id}) غير موجود (قد تكون ممسوحة). تم فتح الفاتورة التالية رقم ({nearestNext.Value}).";
                    return RedirectToAction(nameof(Show), new { id = nearestNext.Value, frag = (string?)null, frame = 1 });
                }

                // متغير: لو مفيش التالي… نجرب "السابق" (أكبر رقم أقل من id)
                int? nearestPrev = await _context.PurchaseInvoices
                    .AsNoTracking()
                    .Where(x => x.PIId < id)
                    .OrderByDescending(x => x.PIId)
                    .Select(x => (int?)x.PIId)
                    .FirstOrDefaultAsync();

                if (nearestPrev.HasValue && nearestPrev.Value > 0)
                {
                    TempData["Error"] = $"رقم الفاتورة ({id}) غير موجود (قد تكون ممسوحة). تم فتح الفاتورة السابقة رقم ({nearestPrev.Value}).";
                    return RedirectToAction(nameof(Show), new { id = nearestPrev.Value, frag = (string?)null, frame = 1 });
                }

                // لو مفيش أي فواتير أصلاً
                TempData["Error"] = "لا توجد فواتير مشتريات مسجلة حالياً.";
                return RedirectToAction(nameof(Create), new { frame = 1 });
            }

            // =========================================
            // 3) تجهيز القوائم + الأوتوكومبليت
            // ملاحظة مهمة:
            // - حتى لو frag=body نحن نترك نفس السلوك الحالي حفاظًا على أي اعتماد داخل الـ View
            // - لو احتجنا لاحقًا تحسين الأداء، يمكننا جعلها اختيارية للـ body فقط
            // =========================================
            await PopulateDropDownsAsync(invoice.CustomerId, invoice.WarehouseId);
            await LoadProductsForAutoCompleteAsync();

            // متغير: هل الفاتورة مقفولة
            ViewBag.IsLocked = invoice.IsPosted || invoice.Status == "Posted" || invoice.Status == "Closed";

            // متغير: علامة للـ View أننا داخل Frame (في العرض الكامل فقط)
            ViewBag.Frame = (!isBodyOnly) ? 1 : 0;

            // =========================================
            // 4) ✅ تجهيز التنقل بشكل موحّد (استخدم دالتك المساعدة)
            // =========================================
            await FillPurchaseInvoiceNavAsync(invoice.PIId);

            // =========================================
            // 5) عرض الـ View نفسه
            // - الـ View سيقرر ماذا يعرض بناءً على ViewBag.Fragment
            // =========================================
            return View("Show", invoice);
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
                CreatedBy   = GetCurrentUserDisplayName() 
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




        [HttpPost]
        public async Task<IActionResult> PostInvoice(int id)
        {
            // ================================
            // 0) معرفة هل الطلب Ajax أم لا
            // ================================
            bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest"; // متغير: هل الطلب من fetch؟

            try
            {
                // ================================
                // 1) تحميل الفاتورة + المورد
                // ================================
                var invoice = await _context.PurchaseInvoices
                    .Include(x => x.Customer)   // مهم: علشان AccountId
                    .FirstOrDefaultAsync(x => x.PIId == id);

                if (invoice == null)
                {
                    if (isAjax) return NotFound(new { ok = false, message = "الفاتورة غير موجودة." });
                    return NotFound();
                }

                // ================================
                // 2) منع الترحيل لو مترحّلة بالفعل
                // ================================
                if (invoice.IsPosted)
                {
                    if (isAjax) return BadRequest(new { ok = false, message = "هذه الفاتورة مترحّلة بالفعل." });

                    TempData["Error"] = "هذه الفاتورة مترحّلة بالفعل.";
                    return RedirectToAction("Show", new { id = invoice.PIId });
                }

                // ========================================
                // 3) تنفيذ الترحيل
                // ========================================
                await _ledgerPostingService.PostPurchaseInvoiceAsync(invoice.PIId, User.Identity?.Name);

                // ========================================
                // 4) تسجيل نشاط (ترحيل فقط)
                // ========================================
                await _activityLogger.LogAsync(
                    actionType: UserActionType.Post,               // متغير: نوع العملية
                    entityName: "PurchaseInvoice",                 // متغير: اسم الكيان
                    entityId: invoice.PIId,                        // متغير: رقم الفاتورة
                    description: $"ترحيل فاتورة مشتريات رقم {invoice.PIId}" // متغير: وصف
                );

                // ================================
                // 5) إعادة تحميل بيانات الفاتورة بعد الترحيل (لإرجاع الحالة الجديدة للـ JS)
                // ================================
                var updated = await _context.PurchaseInvoices
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.PIId == id);

                // ================================
                // 6) الرد المناسب حسب نوع الطلب
                // ================================
                if (isAjax)
                {
                    return Ok(new
                    {
                        ok = true,
                        message = "تم ترحيل الفاتورة بنجاح.",
                        isPosted = updated?.IsPosted ?? true,
                        status = updated?.Status ?? "Posted",
                        postedAt = updated?.PostedAt,
                        postedBy = updated?.PostedBy
                    });
                }

                TempData["Success"] = "تم ترحيل الفاتورة بنجاح";
                return RedirectToAction("Show", new { id = invoice.PIId });
            }
            catch (Exception ex)
            {
                // ================================
                // 7) لو حصل خطأ في الترحيل
                // ================================
                if (isAjax)
                {
                    // ✅ رسالة مفهومة للزر
                    return BadRequest(new { ok = false, message = ex.Message });
                }

                TempData["Error"] = "فشل الترحيل: " + ex.Message;
                return RedirectToAction("Show", new { id });
            }
        }


        #endregion


    }
}
