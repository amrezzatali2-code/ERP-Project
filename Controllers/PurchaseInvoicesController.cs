using ERP.Data;                                 // AppDbContext
using ERP.Filters;
using ERP.Infrastructure;                       // كلاس PagedResult لتقسيم الصفحات
using ERP.Models;                               // الموديل PurchaseInvoice
using ERP.Security;
using ERP.Services;
using ERP.ViewModels;   // علشان نقدر نستعمل PurchaseInvoiceHeaderDto
using Microsoft.AspNetCore.Mvc;                 // أساس الكنترولر و IActionResult
using Microsoft.AspNetCore.Mvc.Rendering;       // SelectList للقوائم المنسدلة
using Microsoft.EntityFrameworkCore;            // Include / AsNoTracking / ToListAsync
using System;                                   // تواريخ وأوقات
using System.Collections.Generic;               // القوائم List
using System.Globalization;
using System.Linq;                              // LINQ: Where / OrderBy / Any
using System.Security.Claims;   // متغير: للوصول للـ Claims بتاعة اليوزر
using System.Text;                              // لبناء ملف CSV
using System.Text.RegularExpressions;
using System.Threading.Tasks;                   // async / await
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;






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
        private readonly IFullReturnService _fullReturnService;
        private readonly IPermissionService _permissionService;
        private readonly IUserAccountVisibilityService _accountVisibilityService;
        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        public PurchaseInvoicesController(AppDbContext context,
                                          DocumentTotalsService docTotals,
                                          IUserActivityLogger activityLogger,
                                          ILedgerPostingService ledgerPosting,
                                          IFullReturnService fullReturnService,
                                          IPermissionService permissionService,
                                          IUserAccountVisibilityService accountVisibilityService)
        {
            _context = context;
            _docTotals = docTotals;
            _activityLogger = activityLogger;
            _ledgerPostingService = ledgerPosting;
            _fullReturnService = fullReturnService;
            _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
            _accountVisibilityService = accountVisibilityService ?? throw new ArgumentNullException(nameof(accountVisibilityService));
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
        [RequirePermission("PurchaseInvoices.Index")]
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
            string? filterCol_id = null,
            string? filterCol_status = null,
            string? filterCol_vendor = null,
            string? filterCol_warehouse = null,
            string? filterCol_refprid = null,
            string? filterCol_pidate = null,
            string? filterCol_customerid = null,
            string? filterCol_itemstotal = null,
            string? filterCol_discounttotal = null,
            string? filterCol_nettotal = null,
            string? filterCol_isposted = null,
            string? filterCol_postedby = null,
            string? filterCol_createdby = null,
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

            // نبدأ بالاستعلام الأساسي مع تحميل المورد (للعرض ولفلتر العمود vendor)
            IQueryable<PurchaseInvoice> query = _context.PurchaseInvoices
                .AsNoTracking()
                .Include(p => p.Customer);

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

            // 1b) تطبيق فلاتر الأعمدة (بنمط Excel)
            query = ApplyColumnFilters(query, filterCol_id, filterCol_status, filterCol_vendor, filterCol_warehouse, filterCol_refprid, filterCol_pidate, filterCol_customerid, filterCol_itemstotal, filterCol_discounttotal, filterCol_nettotal, filterCol_isposted, filterCol_postedby, filterCol_createdby);

            // 2) تطبيق الترتيب
            bool sortDesc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            query = ApplySort(query, sort, sortDesc);

            // =========================================================
            // حساب إجمالي الصافي من نفس الاستعلام (بعد الفلاتر)
            // ✅ مهم: لازم قبل الـ Paging علشان ما تتحسبش على الصفحة بس
            // =========================================================
            decimal totalNet = await query.SumAsync(pi => (decimal?)pi.NetTotal) ?? 0m;

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

            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_Status = filterCol_status;
            ViewBag.FilterCol_Vendor = filterCol_vendor;
            ViewBag.FilterCol_Warehouse = filterCol_warehouse;
            ViewBag.FilterCol_Refprid = filterCol_refprid;
            ViewBag.FilterCol_Pidate = filterCol_pidate;
            ViewBag.FilterCol_Customerid = filterCol_customerid;
            ViewBag.FilterCol_Itemstotal = filterCol_itemstotal;
            ViewBag.FilterCol_Discounttotal = filterCol_discounttotal;
            ViewBag.FilterCol_Nettotal = filterCol_nettotal;
            ViewBag.FilterCol_Isposted = filterCol_isposted;
            ViewBag.FilterCol_Postedby = filterCol_postedby;
            ViewBag.FilterCol_Createdby = filterCol_createdby;

            // إجمالي الصافي
            ViewBag.TotalNet = totalNet;

            return View(model);
        }

        /// <summary>
        /// API: جلب القيم المميزة لعمود (للفلترة بنمط Excel).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var columnLower = (column ?? "").Trim().ToLowerInvariant();

            if (columnLower == "id")
            {
                var ids = await _context.PurchaseInvoices.AsNoTracking()
                    .Select(p => p.PIId).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                var items = ids.Select(v => new { value = v.ToString(), display = v.ToString() });
                return Json(items);
            }
            if (columnLower == "status")
            {
                var q = _context.PurchaseInvoices.AsNoTracking().Where(p => p.Status != null).Select(p => p.Status!).Distinct();
                if (!string.IsNullOrEmpty(searchTerm))
                    q = q.Where(s => s.ToLower().Contains(searchTerm));
                var list = await q.OrderBy(x => x).Take(500).ToListAsync();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "vendor")
            {
                var q = _context.Customers.AsNoTracking()
                    .Where(c => _context.PurchaseInvoices.Any(pi => pi.CustomerId == c.CustomerId))
                    .Select(c => c.CustomerName ?? "");
                if (!string.IsNullOrEmpty(searchTerm))
                    q = q.Where(s => s.ToLower().Contains(searchTerm));
                var list = await q.Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "warehouse")
            {
                var ids = await _context.PurchaseInvoices.AsNoTracking()
                    .Select(p => p.WarehouseId).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "refprid")
            {
                var ids = await _context.PurchaseInvoices.AsNoTracking()
                    .Where(p => p.RefPRId.HasValue)
                    .Select(p => p.RefPRId!.Value).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "pidate")
            {
                var dates = await _context.PurchaseInvoices.AsNoTracking()
                    .Select(p => p.PIDate.Date).Distinct().OrderByDescending(x => x).Take(500).ToListAsync();
                return Json(dates.Select(d => new { value = d.ToString("yyyy-MM-dd"), display = d.ToString("yyyy-MM-dd") }));
            }
            if (columnLower == "customerid")
            {
                var ids = await _context.PurchaseInvoices.AsNoTracking()
                    .Select(p => p.CustomerId).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "itemstotal")
            {
                var vals = await _context.PurchaseInvoices.AsNoTracking()
                    .Select(p => p.ItemsTotal).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(vals.Select(v => new { value = v.ToString("0.00"), display = v.ToString("0.00") }));
            }
            if (columnLower == "discounttotal")
            {
                var vals = await _context.PurchaseInvoices.AsNoTracking()
                    .Select(p => p.DiscountTotal).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(vals.Select(v => new { value = v.ToString("0.00"), display = v.ToString("0.00") }));
            }
            if (columnLower == "nettotal")
            {
                var vals = await _context.PurchaseInvoices.AsNoTracking()
                    .Select(p => p.NetTotal).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(vals.Select(v => new { value = v.ToString("0.00"), display = v.ToString("0.00") }));
            }
            if (columnLower == "isposted")
            {
                var items = new[] { new { value = "true", display = "نعم" }, new { value = "false", display = "لا" } };
                return Json(items);
            }
            if (columnLower == "postedby")
            {
                var q = _context.PurchaseInvoices.AsNoTracking().Where(p => p.PostedBy != null).Select(p => p.PostedBy!).Distinct();
                if (!string.IsNullOrEmpty(searchTerm))
                    q = q.Where(s => s.ToLower().Contains(searchTerm));
                var list = await q.OrderBy(x => x).Take(500).ToListAsync();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "createdby")
            {
                var q = _context.PurchaseInvoices.AsNoTracking().Where(p => p.CreatedBy != null).Select(p => p.CreatedBy!).Distinct();
                if (!string.IsNullOrEmpty(searchTerm))
                    q = q.Where(s => s.ToLower().Contains(searchTerm));
                var list = await q.OrderBy(x => x).Take(500).ToListAsync();
                return Json(list.Select(v => new { value = v, display = v }));
            }

            return Json(Array.Empty<object>());
        }










       


         /// <summary>
         /// دالة مساعدة: تجهيز الموردين والمخازن للفورم (الهيدر فقط).
         /// </summary>
            private async Task PopulateDropDownsAsync(
                 int? selectedCustomerId = null,    // متغير: كود المورد المختار (لو فاتورة قديمة)
                int? selectedWarehouseId = null)   // متغير: كود المخزن المختار (لو فاتورة قديمة)
                 {
            
            

            // 1) تحميل كل العملاء/الموردين (مصدر واحد: خدمة ظهور الحسابات)
            var customerQueryPi = _context.Customers.AsNoTracking().Where(c => c.IsActive == true);
            customerQueryPi = await _accountVisibilityService.ApplyCustomerVisibilityFilterAsync(customerQueryPi);
            var customers = await customerQueryPi
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

            // لو فاتورة قديمة وموردها (مثلاً مستثمر) غير ظاهر في القائمة بعد الفلتر، نضيفه حتى يظهر اسمه في الحقل
            if (selectedCustomerId.HasValue && !customers.Any(c => c.Id == selectedCustomerId.Value))
            {
                var extra = await _context.Customers
                    .AsNoTracking()
                    .Where(c => c.CustomerId == selectedCustomerId.Value)
                    .Include(c => c.Governorate)
                    .Include(c => c.District)
                    .Include(c => c.Area)
                    .Select(c => new
                    {
                        Id = c.CustomerId,
                        Name = c.CustomerName ?? "",
                        Phone = c.Phone1 ?? string.Empty,
                        Address = c.Address ?? string.Empty,
                        Gov = c.Governorate != null ? c.Governorate.GovernorateName : string.Empty,
                        District = c.District != null ? c.District.DistrictName : string.Empty,
                        Area = c.Area != null ? c.Area.AreaName : string.Empty,
                        Credit = c.CreditLimit
                    })
                    .FirstOrDefaultAsync();
                if (extra != null)
                    customers.Insert(0, extra);
            }

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
        [RequirePermission("PurchaseInvoices.Edit")]
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
            public decimal unitCost { get; set; }            // متغير: تكلفة الشراء للوحدة (مش هنثق فيها - هنحسب)
            public decimal priceRetail { get; set; }         // متغير: سعر الجمهور
            public decimal purchaseDiscountPct { get; set; } // متغير: خصم الشراء %
            public string? BatchNo { get; set; }             // متغير: رقم التشغيلة
            public string? expiryText { get; set; }          // متغير: الصلاحية كنص MM/YYYY
        }

        [RequirePermission("PurchaseInvoices.Edit")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddLineJson([FromBody] AddLineJsonDto dto)
        {
            // تعليق: Transaction مهم هنا لأننا بنكتب في أكتر من جدول (PILines + StockLedger + StockBatches)
            await using var tx = await _context.Database.BeginTransactionAsync();

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
                // 0.1) تنظيف الخصم + حساب تكلفة الوحدة على السيرفر
                // =========================
                var disc = dto.purchaseDiscountPct; // متغير: الخصم
                if (disc < 0) disc = 0;
                if (disc > 100) disc = 100;

                // متغير: قيمة السطر بعد الخصم (على أساس سعر الجمهور)
                var lineValue = dto.qty * dto.priceRetail * (1m - (disc / 100m));

                // متغير: تكلفة الوحدة المحسوبة فعلياً
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
                // 2) تحميل الفاتورة
                // =========================
                var invoice = await _context.PurchaseInvoices
                    .FirstOrDefaultAsync(p => p.PIId == dto.PIId);

                if (invoice == null)
                    return NotFound(new { ok = false, message = "الفاتورة غير موجودة." });

                if (invoice.WarehouseId <= 0)
                    return BadRequest(new { ok = false, message = "يجب اختيار مخزن قبل إضافة سطور." });


                // =========================
                // 2.1) منع التعديل على فاتورة مُرحّلة
                // =========================
                if (invoice.IsPosted)
                {
                    await tx.RollbackAsync(); // تعليق: نفك الترانزاكشن لأننا هنخرج بدري
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
                    .FirstOrDefaultAsync(x => x.ProdId == dto.prodId);

                if (product == null)
                    return BadRequest(new { ok = false, message = "الصنف غير موجود." });

                // =========================
                // 3.1) تحميل السطور الحالية من قاعدة البيانات (بدلاً من invoice.Lines المحمّل في الذاكرة)
                // =========================
                var currentLines = await _context.PILines
                    .Where(l => l.PIId == dto.PIId)
                    .ToListAsync();

                // =========================
                // 4) Merge في PILines (نفس الصنف + نفس التشغيلة + نفس الصلاحية + نفس الأسعار + نفس الخصم + نفس تكلفة الوحدة)
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

                PILine affectedLine; // متغير: السطر الذي تأثر
                int qtyDelta;         // متغير: الزيادة التي ستدخل المخزن

                if (existingLine != null)
                {
                    qtyDelta = dto.qty;
                    existingLine.Qty += dto.qty;

                    existingLine.BatchNo = batchNo;
                    existingLine.Expiry = expDate;
                    existingLine.UnitCost = computedUnitCost;

                    affectedLine = existingLine;
                }
                else
                {
                    // ✅ حساب LineNo من السطور المحمّلة من قاعدة البيانات (وليس من invoice.Lines)
                    var nextLineNo = (currentLines.Any() ? currentLines.Max(l => l.LineNo) : 0) + 1;

                    affectedLine = new PILine
                    {
                        PIId = invoice.PIId,
                        LineNo = nextLineNo,
                        ProdId = dto.prodId,
                        Qty = dto.qty,
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
                // (4.1) Batch (Master) Upsert
                // الهدف: تسجيل/تحديث تعريف التشغيلة (ProdId + BatchNo + Expiry)
                // =========================
                Batch? batch = null; // متغير: صف التشغيلة في جدول Batch

                // شرط: لازم BatchNo + Expiry موجودين لأنهم تعريف التشغيلة الحقيقي
                if (!string.IsNullOrWhiteSpace(batchNo) && expDate.HasValue)
                {
                    var exp = expDate.Value.Date;

                    // تعليق: ندور هل التشغيلة موجودة لنفس الصنف + رقم التشغيلة + الصلاحية
                    batch = await _context.Batches
                        .FirstOrDefaultAsync(b =>
                            b.ProdId == dto.prodId &&
                            b.BatchNo == batchNo &&
                            b.Expiry.Date == exp
                        );


                    if (batch == null)
                    {
                        // ✅ إنشاء تشغيلة جديدة
                        batch = new Batch
                        {
                            ProdId = dto.prodId,
                            BatchNo = batchNo,
                            Expiry = exp,

                            // السعر الخاص بالتشغيلة (سعر الجمهور للتشغيلة)
                            PriceRetailBatch = dto.priceRetail,

                            // تكلفة افتراضية للتشغيلة (من حسابنا)
                            UnitCostDefault = computedUnitCost,

                            // ربط اختياري بالمورد (لو عندك العمود)
                            CustomerId = invoice.CustomerId,

                            EntryDate = DateTime.UtcNow,
                            IsActive = true
                        };

                        _context.Batches.Add(batch);
                    }
                    else
                    {
                        // ✅ تحديث بيانات التشغيلة لو موجودة
                        batch.PriceRetailBatch = dto.priceRetail;
                        batch.UnitCostDefault = computedUnitCost;
                        batch.UpdatedAt = DateTime.UtcNow;
                        batch.IsActive = true; // تعليق: نضمن أنها نشطة
                    }
                }





                // =========================
                // (قبل جزء الـ StockLedger) هات خصم سطر الشراء
                // =========================

                // متغير: خصم الشراء % الخاص بسطر الشراء (مصدر الحقيقة: سطر الشراء نفسه)
                decimal purchaseDiscountPct = affectedLine.PurchaseDiscountPct;


                // =========================
                // 5) StockLedger: Merge على نفس SourceLine لو السطر اتعمله Merge
                // =========================
                var existingLedger = await _context.StockLedger.FirstOrDefaultAsync(x =>
                    x.SourceType == "Purchase" &&
                    x.SourceId == invoice.PIId &&
                    x.SourceLine == affectedLine.LineNo &&
                    x.WarehouseId == invoice.WarehouseId &&
                    x.ProdId == dto.prodId &&
                    (x.BatchNo ?? "").Trim() == (batchNo ?? "") &&
                    ((x.Expiry.HasValue ? x.Expiry.Value.Date : (DateTime?)null) == expDate) &&
                    x.UnitCost == computedUnitCost &&
                    x.QtyOut == 0 &&
                    (x.PurchaseDiscount ?? 0m) == purchaseDiscountPct
                );

                if (existingLedger != null)
                {
                    // ✅ تعديل حركة موجودة
                    existingLedger.QtyIn += qtyDelta;

                    // تعليق: طالما لم يحدث سحب من هذه الحركة، RemainingQty تزيد
                    // لو حصل سحب لاحقًا - ده موضوع تاني (FIFO) لكن هنا نضيف فقط
                    existingLedger.RemainingQty = (existingLedger.RemainingQty ?? 0) + qtyDelta;

                    // ✅ تثبيت/تحديث خصم الشراء (لو موجود أصلاً نفس القيمة)
                    existingLedger.PurchaseDiscount = purchaseDiscountPct;

                    existingLedger.TranDate = DateTime.UtcNow;
                    existingLedger.Note = $"Purchase Line (Merged): {product.ProdName}";
                }
                else
                {
                    // ✅ إضافة حركة جديدة
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
                        UnitCost = computedUnitCost,

                        RemainingQty = qtyDelta,
                        SourceType = "Purchase",
                        SourceId = invoice.PIId,
                        SourceLine = affectedLine.LineNo,
                        PurchaseDiscount = purchaseDiscountPct,
                        Note = $"Purchase Line: {product.ProdName}"
                    };

                    _context.StockLedger.Add(ledger);
                }

                // =========================
                // 6) StockBatch Upsert (صف واحد للتشغيلة داخل المخزن)
                // =========================
                // شرط: StockBatch عندك يعتمد على BatchNo + Expiry (لازم الاتنين موجودين)
                if (!string.IsNullOrWhiteSpace(batchNo) && expDate.HasValue)
                {
                    var exp = expDate.Value.Date;

                    var sbRow = await _context.StockBatches
                        .FirstOrDefaultAsync(x =>
                            x.WarehouseId == invoice.WarehouseId &&
                            x.ProdId == dto.prodId &&
                            x.BatchNo == batchNo &&
                            x.Expiry.HasValue &&
                            x.Expiry.Value.Date == exp);

                    if (sbRow != null)
                    {
                        sbRow.QtyOnHand += qtyDelta;
                        sbRow.UpdatedAt = DateTime.UtcNow;
                        sbRow.Note = $"PI:{invoice.PIId} Line:{affectedLine.LineNo} (+{qtyDelta})";
                    }
                    else
                    {
                        var newRow = new ERP.Models.StockBatch
                        {
                            WarehouseId = invoice.WarehouseId,
                            ProdId = dto.prodId,
                            BatchNo = batchNo,
                            Expiry = exp,

                            QtyOnHand = qtyDelta,
                            QtyReserved = 0,

                            UpdatedAt = DateTime.UtcNow,
                            Note = $"PI:{invoice.PIId} Line:{affectedLine.LineNo} (+{qtyDelta})"
                        };

                        _context.StockBatches.Add(newRow);
                    }
                }

                // =========================
                // 7) حفظ + إعادة حساب الإجماليات
                // =========================
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                await _docTotals.RecalcPurchaseInvoiceTotalsAsync(dto.PIId);

                // =========================
                // 8) LogActivity
                // =========================
                await _activityLogger.LogAsync(
                    existingLine != null ? UserActionType.Edit : UserActionType.Create,
                    "PILine",
                    affectedLine.PIId,
                    $"PIId={invoice.PIId} | ProdId={dto.prodId} | QtyDelta={qtyDelta}"
                );

                // =========================
                // 9) رجّع السطور + الإجماليات
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
                await tx.RollbackAsync();
                return BadRequest(new { ok = false, message = ex.Message });
            }
        }















        // ================================================================
        // DTO: بيانات مسح سطر (جاية من AJAX)
        // ================================================================
        public class RemoveLineJsonDto
        {
            public int PIId { get; set; }    // متغير: رقم فاتورة الشراء
            public int LineNo { get; set; }  // متغير: رقم السطر داخل الفاتورة
        }

        [RequirePermission("PurchaseInvoices.Delete")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveLineJson([FromBody] RemoveLineJsonDto dto)
        {
            try
            {
                // =========================
                // 0) فحص المدخلات
                // =========================
                if (dto == null || dto.PIId <= 0 || dto.LineNo <= 0)
                    return BadRequest(new { ok = false, message = "بيانات المسح غير صحيحة." });

                // =========================
                // 1) تحميل الفاتورة
                // =========================
                var invoice = await _context.PurchaseInvoices
                    .FirstOrDefaultAsync(x => x.PIId == dto.PIId);

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
                var line = await _context.PILines
                    .FirstOrDefaultAsync(l => l.PIId == dto.PIId && l.LineNo == dto.LineNo);

                if (line == null)
                    return NotFound(new { ok = false, message = "السطر غير موجود." });


                

                // =========================
                // 3) تحميل حركات StockLedger المرتبطة بالسطر
                // =========================
                var ledgers = await _context.StockLedger
                    .Where(x =>
                        x.SourceType == "Purchase" &&   // ثابت: مصدر الحركة شراء
                        x.SourceId == dto.PIId &&       // ثابت: رقم الفاتورة
                        x.SourceLine == dto.LineNo)     // ثابت: رقم السطر
                    .ToListAsync();

                // =========================
                // 4) شرط الأمان FIFO (قبل أي تعديل)
                // =========================
                // ممنوع الحذف لو الكمية اتسحب/اتباع منها بعد كده
                foreach (var lg in ledgers)
                {
                    if (lg.QtyIn > 0)
                    {
                        var rem = lg.RemainingQty ?? 0; // متغير: المتبقي من قيد الدخول
                        if (rem < lg.QtyIn)
                        {
                            // ✅ رسالة واضحة جدًا حسب طلبك: "تم البيع منه"
                            return BadRequest(new
                            {
                                ok = false,
                                message = "لا يمكن حذف هذا السطر لأن جزءًا من كميته تم البيع/الصرف منه بالفعل. (تم البيع منه) — احذف السحب/البيع المرتبط أولاً."
                            });
                        }
                    }
                }

                // =========================
                // 5) نبدأ Transaction بعد ما اتأكدنا إن الحذف مسموح
                // =========================
                await using var tx = await _context.Database.BeginTransactionAsync();

                // =========================
                // 6) تحديث StockBatches (تعديل كمية فقط — بدون حذف الصف)
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
                        sbRow.QtyOnHand -= line.Qty; // ✅ تعديل الرصيد (نقص كمية الشراء)

                        // لا نسمح بالسالب
                        if (sbRow.QtyOnHand < 0) sbRow.QtyOnHand = 0;

                        sbRow.UpdatedAt = DateTime.UtcNow;
                        sbRow.Note = $"PI:{dto.PIId} Line:{dto.LineNo} (-{line.Qty})";

                        // ❌ ممنوع حذف صف StockBatches حتى لو الرصيد = 0 (حسب الاتفاق)
                        // (مفيش Remove هنا)
                    }
                }

                // =========================
                // 7) حذف StockLedger ثم حذف سطر الفاتورة
                // =========================
                if (ledgers.Count > 0)
                    _context.StockLedger.RemoveRange(ledgers);   // ✅ حذف وليس عكس

                _context.PILines.Remove(line);

                await _context.SaveChangesAsync();

                // =========================
                // 8) تحديث هيدر الفاتورة (داخل نفس الـ Transaction)
                // =========================
                await _docTotals.RecalcPurchaseInvoiceTotalsAsync(dto.PIId);

                await _context.SaveChangesAsync();

                // ✅ Commit بعد كل شيء يخص الداتا
                await tx.CommitAsync();

                // =========================
                // 9) LogActivity (بعد الـ Commit حتى لا يعطل المسح)
                // =========================
                try
                {
                    await _activityLogger.LogAsync(
                        UserActionType.Delete,
                        "PILine",
                        dto.PIId,
                        $"PIId={dto.PIId} | LineNo={dto.LineNo} | ProdId={line.ProdId} | Qty={line.Qty}"
                    );
                }
                catch
                {
                    // تعليق: لا نوقف العملية لو اللوج حصل فيه مشكلة
                }

                // =========================
                // 10) رجّع السطور + الإجماليات بعد المسح
                // =========================
                var linesNow = await _context.PILines
                    .Where(l => l.PIId == dto.PIId)
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
                    message = "تم حذف السطر بنجاح.",
                    lines = linesDto,
                    totals = new { totalLines, totalItems, totalQty, totalRetail, totalDiscount, totalAfterDiscount }
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
            public int PIId { get; set; } // متغير: رقم فاتورة الشراء
        }

        [RequirePermission("PurchaseInvoices.Delete")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClearAllLinesJson([FromBody] ClearAllLinesJsonDto dto)
        {
            try
            {
                // =========================
                // 0) فحص المدخلات
                // =========================
                if (dto == null || dto.PIId <= 0)
                    return BadRequest(new { ok = false, message = "بيانات المسح غير صحيحة." });

                // =========================
                // 1) تحميل الفاتورة
                // =========================
                var invoice = await _context.PurchaseInvoices
                    .FirstOrDefaultAsync(x => x.PIId == dto.PIId);

                if (invoice == null)
                    return NotFound(new { ok = false, message = "الفاتورة غير موجودة." });

                // =========================
                // 1.1) منع التعديل على فاتورة مُرحّلة
                // =========================
                if (invoice.IsPosted)
                    return BadRequest(new { ok = false, message = "الفاتورة مُرحّلة ومقفولة. استخدم زر (فتح الفاتورة) أولاً." });

                // =========================
                // 2) تحميل كل سطور الفاتورة
                // =========================
                var lines = await _context.PILines
                    .Where(l => l.PIId == dto.PIId)
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
                        totals = new { totalLines = 0, totalItems = 0, totalQty = 0, totalRetail = 0m, totalDiscount = 0m, totalAfterDiscount = 0m }
                    });
                }

                // =========================
                // 3) تحميل كل حركات StockLedger المرتبطة بالفاتورة مرة واحدة
                // =========================
                var allLedgers = await _context.StockLedger
                    .Where(x =>
                        x.SourceType == "Purchase" && // ثابت: مصدر الحركة شراء
                        x.SourceId == dto.PIId)       // ثابت: رقم الفاتورة
                    .ToListAsync();

                // =========================
                // 4) شرط الأمان FIFO (قبل أي تعديل) على كل القيود
                // =========================
                foreach (var lg in allLedgers)
                {
                    if (lg.QtyIn > 0)
                    {
                        var rem = lg.RemainingQty ?? 0; // متغير: المتبقي من قيد الدخول
                        if (rem < lg.QtyIn)
                        {
                            return BadRequest(new
                            {
                                ok = false,
                                message = "لا يمكن مسح جميع الأصناف لأن جزءًا من كمية أحد السطور تم البيع/الصرف منه بالفعل. (تم البيع منه) — احذف السحب/البيع المرتبط أولاً."
                            });
                        }
                    }
                }

                // =========================
                // 5) نبدأ Transaction بعد ما اتأكدنا إن المسح مسموح
                // =========================
                await using var tx = await _context.Database.BeginTransactionAsync();

                // =========================
                // 6) تحديث StockBatches (تعديل كمية فقط — بدون حذف الصف)
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
                            sbRow.QtyOnHand -= line.Qty; // ✅ نقص كمية الشراء

                            if (sbRow.QtyOnHand < 0) sbRow.QtyOnHand = 0; // لا نسمح بالسالب

                            sbRow.UpdatedAt = DateTime.UtcNow;
                            sbRow.Note = $"PI:{dto.PIId} ClearAll (Line:{line.LineNo}) (-{line.Qty})";

                            // ❌ ممنوع حذف صف StockBatches حتى لو الرصيد = 0 (حسب الاتفاق)
                        }
                    }
                }

                // =========================
                // 7) حذف StockLedger (كل قيود الفاتورة) ثم حذف كل سطور الفاتورة
                // =========================
                if (allLedgers.Count > 0)
                    _context.StockLedger.RemoveRange(allLedgers);

                _context.PILines.RemoveRange(lines);

                await _context.SaveChangesAsync();

                // =========================
                // 8) تحديث هيدر الفاتورة (داخل نفس الـ Transaction)
                // =========================
                await _docTotals.RecalcPurchaseInvoiceTotalsAsync(dto.PIId);

                await _context.SaveChangesAsync();

                await tx.CommitAsync();

                // =========================
                // 9) LogActivity (بعد الـ Commit)
                // =========================
                try
                {
                    await _activityLogger.LogAsync(
                        UserActionType.Delete,
                        "PILines",
                        dto.PIId,
                        $"PIId={dto.PIId} | ClearAllLines | LinesCount={lines.Count}"
                    );
                }
                catch
                {
                    // تعليق: لا نوقف العملية لو اللوج حصل فيه مشكلة
                }

                // =========================
                // 10) رجّع السطور + الإجماليات بعد المسح (هتكون فاضية)
                // =========================
                return Json(new
                {
                    ok = true,
                    message = "تم مسح جميع الأصناف بنجاح.",
                    lines = new object[0],
                    totals = new { totalLines = 0, totalItems = 0, totalQty = 0, totalRetail = 0m, totalDiscount = 0m, totalAfterDiscount = 0m }
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

        [RequirePermission("PurchaseInvoices.Edit")]
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

            // =========================
            // منع التعديل على فاتورة مُرحّلة
            // =========================
            if (invoice.IsPosted)
            {
                return BadRequest(new
                {
                    ok = false,
                    message = "لا يمكن تعديل الضريبة: هذه الفاتورة مُرحّلة ومقفولة. استخدم زر (فتح الفاتورة) أولاً."
                });
            }


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
        [RequirePermission("PurchaseInvoices.Create")]
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
                Status = "غير مرحلة",             // متغير: حالة الفاتورة = مسودة
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
        [RequirePermission("PurchaseInvoices.Create")]
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
            model.Status = string.IsNullOrWhiteSpace(model.Status) ? "غير مرحلة" : model.Status;

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
        [RequirePermission("PurchaseInvoices.Edit")]
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
        [RequirePermission("PurchaseInvoices.Edit")]
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
















        [RequirePermission("PurchaseInvoices.Show")]
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
        .ThenInclude(c => c.Governorate)
    .Include(p => p.Customer)
        .ThenInclude(c => c.District)
    .Include(p => p.Customer)
        .ThenInclude(c => c.Area)
    .Include(p => p.RefPurchaseRequest)
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
            // ✅ مهم للأداء: لو frag=body (تنقل بالأسهم) ما نعملش تحميل تقيل
            // لأن الـ Body بيتبدّل كتير، وده كان سبب الـ 10MB والبطء
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
            // 4.5) فاتورة لها مرتجع بالكامل → تصحيح الحالة في DB والقائمة (مرحلة)
            // =========================================
            var hasFullReturn = await _context.PurchaseReturns.AnyAsync(pr => pr.RefPIId == id);
            if (hasFullReturn && (invoice.Status == "مفتوحة للتعديل" || !invoice.IsPosted))
            {
                var lastStage = await _context.LedgerEntries
                    .Where(e => e.SourceType == LedgerSourceType.PurchaseInvoice && e.SourceId == id && e.LineNo == 1 && e.PostVersion > 0)
                    .MaxAsync(e => (int?)e.PostVersion) ?? 1;
                var correctStatus = $"مرحلة {lastStage}";
                invoice.Status = correctStatus;
                invoice.IsPosted = true;

                // تحديث قاعدة البيانات حتى تظهر الحالة الصحيحة في القائمة
                var toFix = await _context.PurchaseInvoices.FirstOrDefaultAsync(p => p.PIId == id);
                if (toFix != null)
                {
                    toFix.Status = correctStatus;
                    toFix.IsPosted = true;
                    toFix.PostedAt = toFix.PostedAt ?? DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }
            }

            // =========================================
            // 5) عرض الـ View نفسه
            // - الـ View سيقرر ماذا يعرض بناءً على ViewBag.Fragment
            // =========================================
            return View("Show", invoice);
        }




        #endregion








        #region Delete / DeleteConfirmed (حذف فاتورة واحدة)

        /// <summary>
        /// صفحة تأكيد الحذف لفاتورة مشتريات واحدة. التفرقة بين من القائمة ومن الشاشة عبر from (list|show).
        /// </summary>
        public async Task<IActionResult> Delete(int id, string? from)
        {
            var source = string.Equals(from, "show", StringComparison.OrdinalIgnoreCase) ? "show" : "list";
            var permCode = source == "show" ? "PurchaseInvoices.DeleteOneFromShow" : "PurchaseInvoices.DeleteOneFromList";
            var userId = GetCurrentUserId();
            if (!userId.HasValue || !await _permissionService.HasPermissionAsync(userId.Value, permCode))
                return Forbid();

            var invoice = await _context.PurchaseInvoices
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PIId == id);

            if (invoice == null)
                return NotFound();

            ViewBag.From = source;
            return View(invoice);   // View: Views/PurchaseInvoices/Delete.cshtml
        }

        /// <summary>
        /// تنفيذ الحذف الفعلي بعد التأكيد (حذف عميق من الهيدر). التفرقة بين من القائمة ومن الشاشة عبر from.
        /// </summary>
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id, [FromForm] string? from)
        {
            var source = string.Equals(from, "show", StringComparison.OrdinalIgnoreCase) ? "show" : "list";
            var permCode = source == "show" ? "PurchaseInvoices.DeleteOneFromShow" : "PurchaseInvoices.DeleteOneFromList";
            var userId = GetCurrentUserId();
            if (!userId.HasValue || !await _permissionService.HasPermissionAsync(userId.Value, permCode))
                return Forbid();

            // =========================
            // 0) تحميل الفاتورة (Tracked)
            // =========================
            var invoice = await _context.PurchaseInvoices
                .FirstOrDefaultAsync(x => x.PIId == id);

            if (invoice == null)
                return NotFound();

            // =========================
            // 1) تحميل سطور الفاتورة
            // =========================
            var lines = await _context.PILines
                .Where(l => l.PIId == id)
                .OrderBy(l => l.LineNo)
                .ToListAsync();

            // =========================
            // 2) تحميل StockLedger المرتبط بالفاتورة
            // =========================
            var allLedgers = await _context.StockLedger
                .Where(x => x.SourceType == "Purchase" && x.SourceId == id)
                .ToListAsync();

            // =========================
            // 3) شرط الأمان FIFO (لو عايز تفعّله)
            // =========================
            // ملاحظة: لو أنت بتجرب دلوقتي بدون الشرط، سيبه مُعلّق.
            
            foreach (var lg in allLedgers)
            {
                if (lg.QtyIn > 0)
                {
                    var rem = lg.RemainingQty ?? 0; // متغير: المتبقي من سطر الدخول
                    if (rem < lg.QtyIn)
                    {
                        TempData["ErrorMessage"] = "لا يمكن حذف الفاتورة لأن جزءًا من كمية أحد السطور تم البيع/الصرف منه بالفعل.";
                        return RedirectToAction(nameof(Index));
                    }
                }
            }
            

            // =========================
            // 4) Transaction (مجموعة عمليات كأنها خطوة واحدة)
            // =========================
            await using var tx = await _context.Database.BeginTransactionAsync();

            try
            {
                // =========================
                // 5) تحديث StockBatches (إنقاص الكمية)
                // =========================
                foreach (var line in lines)
                {
                    var batchNo = string.IsNullOrWhiteSpace(line.BatchNo) ? null : line.BatchNo.Trim(); // متغير: التشغيلة
                    var expDate = line.Expiry?.Date;                                                    // متغير: الصلاحية

                    if (!string.IsNullOrWhiteSpace(batchNo) && expDate.HasValue)
                    {
                        var exp = expDate.Value.Date;

                        var sbRow = await _context.StockBatches
                            .FirstOrDefaultAsync(x =>
                                x.WarehouseId == invoice.WarehouseId &&  // متغير: المخزن
                                x.ProdId == line.ProdId &&               // متغير: الصنف
                                x.BatchNo == batchNo &&                  // متغير: التشغيلة
                                x.Expiry.HasValue &&
                                x.Expiry.Value.Date == exp);

                        if (sbRow != null)
                        {
                            sbRow.QtyOnHand -= line.Qty; // متغير: إنقاص كمية الشراء
                            if (sbRow.QtyOnHand < 0) sbRow.QtyOnHand = 0;

                            sbRow.UpdatedAt = DateTime.UtcNow;
                            sbRow.Note = $"PI:{id} DeleteFromHeader (Line:{line.LineNo}) (-{line.Qty})";
                        }
                    }
                }

                // =========================
                // 6) حذف StockLedger الخاص بالفاتورة
                // =========================
                if (allLedgers.Count > 0)
                    _context.StockLedger.RemoveRange(allLedgers);

                // =========================
                // 7) عكس الأثر المحاسبي (Reverse) بدل الحذف
                // ✅ مهم: هذه الدالة (حسب الاتفاق) لا تعمل Transaction ولا SaveChanges وحدها
                // =========================
                await _ledgerPostingService.ReverseForHeaderDeleteAsync(
                    LedgerSourceType.PurchaseInvoice,
                    id,
                    postedBy: User?.Identity?.Name,
                    reason: $"حذف فاتورة مشتريات من قائمة الهيدر PIId={id}"
                );

                // =========================
                // 8) حذف الهيدر نفسه (PurchaseInvoices)
                // =========================
                _context.PurchaseInvoices.Remove(invoice);

                // =========================
                // 9) SaveChanges مرة واحدة + Commit
                // =========================
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                // =========================
                // 10) LogActivity (اختياري)
                // =========================
                try
                {
                    await _activityLogger.LogAsync(
                        UserActionType.Delete,
                        "PurchaseInvoices",
                        id,
                        $"PIId={id} | DeleteFromHeader | Lines={lines.Count} | StockLedger={allLedgers.Count}"
                    );
                }
                catch { }

                TempData["SuccessMessage"] = "تم حذف الفاتورة بنجاح (مع تحديث المخزون وعكس الأثر المحاسبي).";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                // ✅ هنا بقى هتشوف “السبب الحقيقي” في صفحة الـ Index بدل ما تحس إنه ما حذفش وخلاص
                TempData["ErrorMessage"] = $"تعذر حذف الفاتورة رقم {id}: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        #endregion









        #region BulkDelete (حذف مجموعة فواتير دفعة واحدة)

        // ============================================================================
        // ✅ BulkDelete (حذف مجموعة فواتير مشتريات)
        // - يحذف "المسموح فقط" (حسب اختيارك B)
        // - يمنع الحذف لو تم البيع/الصرف من كمية الفاتورة (FIFO RemainingQty < QtyIn)
        // - يحذف آثار المخزون + يعكس الأثر المحاسبي + يحذف الهيدر (Cascade يحذف السطور)
        // - يعرض ملخص بالأرقام: (تم حذف / تم منع / فشل بسبب خطأ)
        // ============================================================================
        [RequirePermission("PurchaseInvoices.BulkDelete")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["ErrorMessage"] = "لم يتم اختيار أي فاتورة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            // تحويل "1,2,3" إلى List<int>
            var ids = selectedIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => TryParseNullableInt(s))
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToList();

            if (!ids.Any())
            {
                TempData["ErrorMessage"] = "لم يتم التعرف على أرقام الفواتير المحددة.";
                return RedirectToAction(nameof(Index));
            }

            // نجيب الفواتير الموجودة فقط (علشان لو فيه أرقام مش موجودة)
            var existingIds = await _context.PurchaseInvoices
                .Where(p => ids.Contains(p.PIId))
                .Select(p => p.PIId)
                .ToListAsync();

            if (!existingIds.Any())
            {
                TempData["ErrorMessage"] = "لم يتم العثور على الفواتير المحددة في قاعدة البيانات.";
                return RedirectToAction(nameof(Index));
            }

            int deletedCount = 0;     // متغير: عدد الفواتير التي تم حذفها
            int blockedCount = 0;     // متغير: عدد الفواتير الممنوع حذفها (FIFO)
            int failedCount = 0;      // متغير: عدد الفواتير التي فشل حذفها بسبب خطأ

            var blockedIds = new List<int>(); // متغير: أرقام الفواتير الممنوعة
            var failedIds = new List<int>();  // متغير: أرقام الفواتير التي فشلت

            // ✅ حذف كل فاتورة لوحدها داخل Transaction مستقل
            // علشان لو فاتورة فشلت/ممنوعة ما توقفش باقي العملية
            foreach (var id in existingIds)
            {
                var result = await TryDeletePurchaseInvoiceDeepAsync(id);

                if (result.Status == DeleteInvoiceStatus.Deleted)
                {
                    deletedCount++;
                }
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

            // ✅ ملخص للمستخدم
            // تعليق: نستخدم SuccessMessage لو حصل حذف فعلاً، وإلا ErrorMessage
            var summary = $"تم حذف: {deletedCount} | تم منع: {blockedCount} | فشل: {failedCount}";

            if (deletedCount > 0)
            {
                TempData["SuccessMessage"] = summary;
                // لو تحب نضيف تفاصيل (اختياري):
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

        #endregion









        #region DeleteAll (حذف جميع فواتير المشتريات)

        // ============================================================================
        // ✅ DeleteAll (حذف جميع فواتير المشتريات)
        // - يحذف "المسموح فقط" ويترك الممنوع/الفاشل
        // - Transaction مستقل لكل فاتورة حتى لا يضيع الشغل كله بسبب واحدة
        // ============================================================================
        [RequirePermission("PurchaseInvoices.DeleteAll")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            // نجيب كل IDs فقط لتقليل الذاكرة
            var allIds = await _context.PurchaseInvoices
                .Select(x => x.PIId)
                .ToListAsync();

            if (!allIds.Any())
            {
                TempData["ErrorMessage"] = "لا توجد فواتير مشتريات لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            int deletedCount = 0;  // متغير: عدد المحذوف
            int blockedCount = 0;  // متغير: عدد الممنوع (FIFO)
            int failedCount = 0;   // متغير: عدد الفاشل

            var blockedIds = new List<int>(); // متغير: أرقام الممنوع
            var failedIds = new List<int>();  // متغير: أرقام الفاشل

            foreach (var id in allIds)
            {
                var result = await TryDeletePurchaseInvoiceDeepAsync(id);

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

        #endregion








        // ============================================================================
        // ✅ دالة مساعدة: تحاول حذف فاتورة مشتريات واحدة "حذف عميق" مثل زر Delete
        // - ترجع حالة: Deleted / BlockedByFifo / Failed
        // - كل فاتورة لها Transaction مستقل (حتى لا نفشل العملية كلها)
        // ============================================================================
        private async Task<DeleteInvoiceResult> TryDeletePurchaseInvoiceDeepAsync(int id)
        {
            // =========================
            // 0) تحميل الفاتورة (Tracked)
            // =========================
            var invoice = await _context.PurchaseInvoices
                .FirstOrDefaultAsync(x => x.PIId == id);

            if (invoice == null)
                return new DeleteInvoiceResult(DeleteInvoiceStatus.Failed, "الفاتورة غير موجودة.");

            // =========================
            // 1) تحميل سطور الفاتورة
            // =========================
            var lines = await _context.PILines
                .Where(l => l.PIId == id)
                .OrderBy(l => l.LineNo)
                .ToListAsync();

            // =========================
            // 2) تحميل StockLedger المرتبط بالفاتورة
            // =========================
            var allLedgers = await _context.StockLedger
                .Where(x => x.SourceType == "Purchase" && x.SourceId == id)
                .ToListAsync();

            // =========================
            // 3) شرط الأمان FIFO (إجباري)
            // =========================
            foreach (var lg in allLedgers)
            {
                if (lg.QtyIn > 0)
                {
                    var rem = lg.RemainingQty ?? 0; // متغير: المتبقي من سطر الدخول
                    if (rem < lg.QtyIn)
                    {
                        return new DeleteInvoiceResult(DeleteInvoiceStatus.BlockedByFifo,
                            "ممنوع الحذف: تم البيع/الصرف من كمية الفاتورة.");
                    }
                }
            }

            // =========================
            // 4) Transaction لكل فاتورة
            // =========================
            await using var tx = await _context.Database.BeginTransactionAsync();

            try
            {
                // =========================
                // 5) تحديث StockBatches (إنقاص الكمية)
                // =========================
                foreach (var line in lines)
                {
                    var batchNo = string.IsNullOrWhiteSpace(line.BatchNo) ? null : line.BatchNo.Trim(); // متغير: التشغيلة
                    var expDate = line.Expiry?.Date;                                                    // متغير: الصلاحية

                    if (!string.IsNullOrWhiteSpace(batchNo) && expDate.HasValue)
                    {
                        var exp = expDate.Value.Date;

                        var sbRow = await _context.StockBatches
                            .FirstOrDefaultAsync(x =>
                                x.WarehouseId == invoice.WarehouseId &&  // متغير: المخزن
                                x.ProdId == line.ProdId &&               // متغير: الصنف
                                x.BatchNo == batchNo &&                  // متغير: التشغيلة
                                x.Expiry.HasValue &&
                                x.Expiry.Value.Date == exp);

                        if (sbRow != null)
                        {
                            sbRow.QtyOnHand -= line.Qty; // متغير: إنقاص كمية الشراء
                            if (sbRow.QtyOnHand < 0) sbRow.QtyOnHand = 0;

                            sbRow.UpdatedAt = DateTime.UtcNow;
                            sbRow.Note = $"PI:{id} DeleteFromHeader (Line:{line.LineNo}) (-{line.Qty})";
                        }
                    }
                }

                // =========================
                // 6) حذف StockLedger الخاص بالفاتورة
                // =========================
                if (allLedgers.Count > 0)
                    _context.StockLedger.RemoveRange(allLedgers);

                // =========================
                // 7) عكس الأثر المحاسبي (Reverse) بدل الحذف
                // =========================
                await _ledgerPostingService.ReverseForHeaderDeleteAsync(
                    LedgerSourceType.PurchaseInvoice,
                    id,
                    postedBy: User?.Identity?.Name,
                    reason: $"حذف فاتورة مشتريات من قائمة الهيدر PIId={id}"
                );

                // =========================
                // 8) حذف الهيدر (Cascade يحذف السطور)
                // =========================
                _context.PurchaseInvoices.Remove(invoice);

                // =========================
                // 9) SaveChanges + Commit
                // =========================
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                // =========================
                // 10) LogActivity (اختياري)
                // =========================
                try
                {
                    await _activityLogger.LogAsync(
                        UserActionType.Delete,
                        "PurchaseInvoices",
                        id,
                        $"PIId={id} | Bulk/DeleteAll | Lines={lines.Count} | StockLedger={allLedgers.Count}"
                    );
                }
                catch { }

                return new DeleteInvoiceResult(DeleteInvoiceStatus.Deleted, "تم الحذف.");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return new DeleteInvoiceResult(DeleteInvoiceStatus.Failed, ex.Message);
            }
        }


        // ============================================================================
        // ✅ Enum + Result صغيرين لتحديد نتيجة حذف الفاتورة
        // ============================================================================
        private enum DeleteInvoiceStatus
        {
            Deleted = 1,        // تم حذف الفاتورة
            BlockedByFifo = 2,  // ممنوع الحذف بسبب FIFO (تم البيع/الصرف)
            Failed = 3          // فشل بسبب خطأ/قيود/استثناء
        }

        private sealed class DeleteInvoiceResult
        {
            public DeleteInvoiceStatus Status { get; }
            public string? Message { get; }

            public DeleteInvoiceResult(DeleteInvoiceStatus status, string? message)
            {
                Status = status;   // متغير: حالة النتيجة
                Message = message; // متغير: رسالة تفصيلية
            }
        }





        [RequirePermission("PurchaseInvoices.Edit")]
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

                Status = "غير مرحلة",                      // حالة الفاتورة
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

                invoiceDate = invoice.PIDate.ToString("d/M/yyyy"),
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

            invoiceDate = existing.PIDate.ToString("d/M/yyyy"),
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
        [RequirePermission("PurchaseInvoices.Export")]
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
            int? codeTo = null,
            string? filterCol_id = null,
            string? filterCol_status = null,
            string? filterCol_vendor = null,
            string? filterCol_warehouse = null,
            string? filterCol_refprid = null,
            string? filterCol_pidate = null,
            string? filterCol_customerid = null,
            string? filterCol_itemstotal = null,
            string? filterCol_discounttotal = null,
            string? filterCol_nettotal = null,
            string? filterCol_isposted = null,
            string? filterCol_postedby = null,
            string? filterCol_createdby = null
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
            query = ApplyColumnFilters(query, filterCol_id, filterCol_status, filterCol_vendor, filterCol_warehouse, filterCol_refprid, filterCol_pidate, filterCol_customerid, filterCol_itemstotal, filterCol_discounttotal, filterCol_nettotal, filterCol_isposted, filterCol_postedby, filterCol_createdby);

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
                            query = query.Where(p => p.PIId.ToString().StartsWith(search));
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
        /// تطبيق فلاتر الأعمدة (بنمط Excel) على استعلام فواتير المشتريات.
        /// </summary>
        private static IQueryable<PurchaseInvoice> ApplyColumnFilters(
            IQueryable<PurchaseInvoice> query,
            string? filterCol_id,
            string? filterCol_status,
            string? filterCol_vendor,
            string? filterCol_warehouse,
            string? filterCol_refprid,
            string? filterCol_pidate,
            string? filterCol_customerid,
            string? filterCol_itemstotal,
            string? filterCol_discounttotal,
            string? filterCol_nettotal,
            string? filterCol_isposted,
            string? filterCol_postedby,
            string? filterCol_createdby
        )
        {
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToList();
                if (ids.Count > 0)
                    query = query.Where(p => ids.Contains(p.PIId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_status))
            {
                var vals = filterCol_status.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();
                if (vals.Count > 0)
                    query = query.Where(p => p.Status != null && vals.Contains(p.Status));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_vendor))
            {
                var vals = filterCol_vendor.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();
                if (vals.Count > 0)
                    query = query.Where(p => p.Customer != null && p.Customer.CustomerName != null && vals.Contains(p.Customer.CustomerName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_warehouse))
            {
                var ids = filterCol_warehouse.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToList();
                if (ids.Count > 0)
                    query = query.Where(p => ids.Contains(p.WarehouseId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_refprid))
            {
                var ids = filterCol_refprid.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToList();
                if (ids.Count > 0)
                    query = query.Where(p => p.RefPRId.HasValue && ids.Contains(p.RefPRId!.Value));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_pidate))
            {
                var parts = filterCol_pidate.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => x.Length >= 8)
                    .ToList();
                if (parts.Count > 0)
                {
                    var dates = new List<DateTime>();
                    foreach (var p in parts)
                        if (DateTime.TryParse(p, out var d))
                            dates.Add(d.Date);
                    if (dates.Count > 0)
                        query = query.Where(pi => dates.Contains(pi.PIDate.Date));
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_customerid))
            {
                var ids = filterCol_customerid.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToList();
                if (ids.Count > 0)
                    query = query.Where(p => ids.Contains(p.CustomerId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_itemstotal))
            {
                var vals = filterCol_itemstotal.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToList();
                if (vals.Count > 0)
                    query = query.Where(p => vals.Contains(p.ItemsTotal));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_discounttotal))
            {
                var vals = filterCol_discounttotal.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToList();
                if (vals.Count > 0)
                    query = query.Where(p => vals.Contains(p.DiscountTotal));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_nettotal))
            {
                var vals = filterCol_nettotal.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToList();
                if (vals.Count > 0)
                    query = query.Where(p => vals.Contains(p.NetTotal));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_isposted))
            {
                var parts = filterCol_isposted.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim().ToLowerInvariant())
                    .Where(x => x == "true" || x == "false" || x == "1" || x == "0" || x == "نعم" || x == "لا")
                    .ToList();
                var includeTrue = parts.Any(x => x == "true" || x == "1" || x == "نعم");
                var includeFalse = parts.Any(x => x == "false" || x == "0" || x == "لا");
                if (includeTrue && !includeFalse)
                    query = query.Where(p => p.IsPosted);
                else if (includeFalse && !includeTrue)
                    query = query.Where(p => !p.IsPosted);
                else if (includeTrue && includeFalse)
                    { /* الكل = لا فلتر */ }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_postedby))
            {
                var vals = filterCol_postedby.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();
                if (vals.Count > 0)
                    query = query.Where(p => p.PostedBy != null && vals.Contains(p.PostedBy));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_createdby))
            {
                var vals = filterCol_createdby.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();
                if (vals.Count > 0)
                    query = query.Where(p => p.CreatedBy != null && vals.Contains(p.CreatedBy));
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
                case "customerid":
                    query = desc
                        ? query.OrderByDescending(p => p.CustomerId).ThenByDescending(p => p.PIId)
                        : query.OrderBy(p => p.CustomerId).ThenBy(p => p.PIId);
                    break;

                case "warehouse":
                    query = desc
                        ? query.OrderByDescending(p => p.WarehouseId).ThenByDescending(p => p.PIId)
                        : query.OrderBy(p => p.WarehouseId).ThenBy(p => p.PIId);
                    break;

                case "refprid":
                    query = desc
                        ? query.OrderByDescending(p => p.RefPRId ?? 0).ThenByDescending(p => p.PIId)
                        : query.OrderBy(p => p.RefPRId ?? 0).ThenBy(p => p.PIId);
                    break;

                case "net":
                case "nettotal":
                    query = desc
                        ? query.OrderByDescending(p => p.NetTotal).ThenByDescending(p => p.PIId)
                        : query.OrderBy(p => p.NetTotal).ThenBy(p => p.PIId);
                    break;

                case "itemstotal":
                    query = desc
                        ? query.OrderByDescending(p => p.ItemsTotal).ThenByDescending(p => p.PIId)
                        : query.OrderBy(p => p.ItemsTotal).ThenBy(p => p.PIId);
                    break;

                case "discounttotal":
                    query = desc
                        ? query.OrderByDescending(p => p.DiscountTotal).ThenByDescending(p => p.PIId)
                        : query.OrderBy(p => p.DiscountTotal).ThenBy(p => p.PIId);
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

                case "postedby":
                    query = desc
                        ? query.OrderByDescending(p => p.PostedBy ?? "").ThenByDescending(p => p.PIId)
                        : query.OrderBy(p => p.PostedBy ?? "").ThenBy(p => p.PIId);
                    break;

                case "createdat":
                    query = desc
                        ? query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.PIId)
                        : query.OrderBy(p => p.CreatedAt).ThenBy(p => p.PIId);
                    break;

                case "createdby":
                    query = desc
                        ? query.OrderByDescending(p => p.CreatedBy ?? "").ThenByDescending(p => p.PIId)
                        : query.OrderBy(p => p.CreatedBy ?? "").ThenBy(p => p.PIId);
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












        [RequirePermission("PurchaseInvoices.PostInvoice")]
        [HttpPost]
        [IgnoreAntiforgeryToken] // تعليق: لو انت بتستدعيه بـ fetch بدون توكن (زي بقية أزرار AJAX عندك)
        public async Task<IActionResult> PostInvoice(int id)
        {
            // ================================
            // 0) معرفة هل الطلب Ajax أم لا (بشكل أقوى)
            // ================================
            bool isAjax =
                string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                || Request.Headers["Accept"].ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);

            try
            {
                // ================================
                // 1) تحميل الفاتورة + المورد
                // ================================
                var invoice = await _context.PurchaseInvoices
                    .Include(x => x.Customer)
                    .FirstOrDefaultAsync(x => x.PIId == id);

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
                    return RedirectToAction("Show", new { id = invoice.PIId });
                }

                // ================================
                // 3) (مهم) تحقق سريع قبل الترحيل
                // ================================
                if (invoice.CustomerId <= 0)
                    return isAjax
                        ? BadRequest(new { ok = false, message = "يجب اختيار المورد قبل الترحيل." })
                        : RedirectToAction("Show", new { id = invoice.PIId });

                if (invoice.WarehouseId <= 0)
                    return isAjax
                        ? BadRequest(new { ok = false, message = "يجب اختيار المخزن قبل الترحيل." })
                        : RedirectToAction("Show", new { id = invoice.PIId });

                // الاحتفاظ بوقت الإنشاء الأصلي لئلا يتغيّر بعد الترحيل
                var originalCreatedAt = invoice.CreatedAt;

                // ================================
                // 4) تنفيذ الترحيل
                // ================================
                await _ledgerPostingService.PostPurchaseInvoiceAsync(invoice.PIId, User.Identity?.Name);





                // ================================
                // 5) تسجيل نشاط
                // ================================
                await _activityLogger.LogAsync(
                    actionType: UserActionType.Post,
                    entityName: "PurchaseInvoice",
                    entityId: invoice.PIId,
                    description: $"ترحيل فاتورة مشتريات رقم {invoice.PIId}"
                );

                // ================================
                // 6) حساب رقم المرحلة الحالية (PostVersion) ثم تحديث Status في جدول PurchaseInvoices
                // ================================
                int postVersion = await _context.LedgerEntries
                    .Where(e =>
                        e.SourceType == LedgerSourceType.PurchaseInvoice &&   // متغير: نوع المصدر (فاتورة مشتريات)
                        e.SourceId == id)                                     // متغير: رقم الفاتورة
                    .MaxAsync(e => (int?)e.PostVersion) ?? 1;                 // متغير: أكبر مرحلة، ولو مفيش يبقى 1

                // ✅ النص المعروض في البوكس (مصدر الحقيقة = عمود Status)
                string postedLabel = $"مرحلة {postVersion}";                 // متغير: النص الذي سيظهر للمستخدم

                invoice.Status = postedLabel;
                invoice.CreatedAt = originalCreatedAt;                      // إعادة وقت الإنشاء الأصلي لئلا يتغيّر
                _context.Entry(invoice).Property(x => x.CreatedAt).IsModified = true;
                await _context.SaveChangesAsync();                           // تعليق: حفظ الحالة بعد الترحيل

                // ================================
                // 6.1) إعادة تحميل الفاتورة بعد الحفظ (عشان نرجّع بيانات حديثة للـ JS)
                // ================================
                var updated = await _context.PurchaseInvoices
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.PIId == id);

                // ================================
                // 6.2) Ajax Response (تحديث فوري للواجهة)
                // ================================
                if (isAjax)
                {
                    return Ok(new
                    {
                        ok = true,
                        message = "تم ترحيل الفاتورة بنجاح.",
                        isPosted = updated?.IsPosted ?? true,

                        // ✅ الأهم: الحالة النصية الصحيحة فورًا (بدون قيم قديمة)
                        status = postedLabel,
                        postedLabel = postedLabel,
                        postVersion = postVersion,

                        postedAt = updated?.PostedAt,
                        postedBy = updated?.PostedBy
                    });
                }


                TempData["Success"] = "تم ترحيل الفاتورة بنجاح";
                return RedirectToAction("Show", new { id = invoice.PIId });
            }
            catch (Exception ex)
            {
                // تعليق: لا ترجع ex.Message لو فيها تفاصيل حساسة — بس دلوقتي هنخليها مفهومة للمستخدم
                var msg = string.IsNullOrWhiteSpace(ex.Message) ? "حدث خطأ أثناء الترحيل." : ex.Message;



                if (isAjax)
                    return BadRequest(new { ok = false, message = "فشل الترحيل: " + msg });

                TempData["Error"] = "فشل الترحيل: " + msg;
                return RedirectToAction("Show", new { id });
            }
        }

        /// <summary>مرتجع فاتورة بالكامل: ينشئ مرتجع شراء من كل أصناف الفاتورة ويرحّله تلقائياً. يدعم Ajax مثل زر التحويل في طلب الشراء.</summary>
        [RequirePermission("PurchaseInvoices.Create")]
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> CreateFullReturn(int id)
        {
            bool isAjax =
                string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                || Request.Headers["Accept"].ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);

            try
            {
                var (purchaseReturnId, message, invoiceReposted, invoiceStatus) = await _fullReturnService.CreateFullPurchaseReturnFromInvoiceAsync(id, User.Identity?.Name);
                if (isAjax)
                    return Ok(new { ok = true, message, purchaseReturnId, invoiceReposted, invoiceStatus });
                TempData["Success"] = message;
                return RedirectToAction("Edit", "PurchaseReturns", new { id = purchaseReturnId, frame = 1 });
            }
            catch (Exception ex)
            {
                if (isAjax)
                    return BadRequest(new { ok = false, message = ex.Message });
                TempData["Error"] = ex.Message;
                return RedirectToAction("Show", new { id });
            }
        }







        // ================================
        //      فتح الفاتورة المرحلة   
        // ================================

        [RequirePermission("PurchaseInvoices.OpenInvoice")]
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
                var invoice = await _context.PurchaseInvoices
                    .FirstOrDefaultAsync(x => x.PIId == id);

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
                    return RedirectToAction("Show", new { id = invoice.PIId });
                }

                // ================================
                // 2.5) لا يمكن فتح فاتورة لها مرتجع بالكامل — تبقى مرحلة
                // ================================
                var hasFullReturn = await _context.PurchaseReturns.AnyAsync(pr => pr.RefPIId == id);
                if (hasFullReturn)
                {
                    if (isAjax) return BadRequest(new { ok = false, message = "لا يمكن فتح الفاتورة: تم إنشاء مرتجع شراء من هذه الفاتورة. الفاتورة مغلقة." });
                    TempData["Error"] = "لا يمكن فتح الفاتورة: تم إنشاء مرتجع شراء من هذه الفاتورة. الفاتورة مغلقة.";
                    return RedirectToAction("Show", new { id = invoice.PIId });
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
                    entityName: "PurchaseInvoice",
                    entityId: invoice.PIId,
                    description: $"فتح فاتورة مشتريات رقم {invoice.PIId} للتعديل"
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
                return RedirectToAction("Show", new { id = invoice.PIId });
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








    




    


}
}
