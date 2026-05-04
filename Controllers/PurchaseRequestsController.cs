using DocumentFormat.OpenXml.InkML;
using ERP.Data;                                 // AppDbContext
using ERP.Filters;
using ERP.Infrastructure;                       // كلاس PagedResult لتقسيم الصفحات
using ERP.Models;                               // الموديل PurchaseRequest
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
    /// كنترولر إدارة جدول فواتير المشتريات (PurchaseRequests)
    /// - عرض القائمة مع بحث / ترتيب / تقسيم صفحات.
    /// - فلتر تاريخ/وقت.
    /// - فلتر من رقم / إلى رقم.
    /// - حذف محدد / حذف كل الفواتير.
    /// - تصدير CSV/Excel.
    /// - Show / Create / Edit / Delete.
    /// </summary>
    public class PurchaseRequestsController : Controller
    {
        // بعد ✅
        private readonly AppDbContext _context;               // سياق قاعدة البيانات
        private readonly DocumentTotalsService _docTotals;    // خدمة إجماليات المستندات
        private readonly IUserActivityLogger _activityLogger; // خدمة سجل النشاط
        private readonly ILedgerPostingService _ledgerPostingService; // متغير: خدمة الترحيل
        private readonly IPermissionService _permissionService;
        private readonly IListVisibilityService _listVisibilityService;
        private readonly StockAnalysisService _stockAnalysis;
        private readonly IUserAccountVisibilityService _accountVisibilityService;
        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        public PurchaseRequestsController(AppDbContext context,
                                          DocumentTotalsService docTotals,
                                          IUserActivityLogger activityLogger,
                                          ILedgerPostingService ledgerPosting,
                                          IPermissionService permissionService,
                                          IListVisibilityService listVisibilityService,
                                          StockAnalysisService stockAnalysis,
                                          IUserAccountVisibilityService accountVisibilityService)
        {
            _context = context;
            _docTotals = docTotals;
            _activityLogger = activityLogger;
            _ledgerPostingService = ledgerPosting;
            _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
            _listVisibilityService = listVisibilityService ?? throw new ArgumentNullException(nameof(listVisibilityService));
            _stockAnalysis = stockAnalysis ?? throw new ArgumentNullException(nameof(stockAnalysis));
            _accountVisibilityService = accountVisibilityService ?? throw new ArgumentNullException(nameof(accountVisibilityService));
        }






        // =========================================================
        // دالة خاصة (طلب الشراء):
        // تحديث أو إنشاء الباتش + تسجيل تغيير السعر فى ProductPriceHistory
        // ✅ نفس منطق فاتورة الشراء تمامًا
        // ✅ التأثير هنا: Batch + PriceHistory فقط
        // ❌ لا مخزون هنا (StockLedger عند التحويل)
        // ❌ لا حسابات هنا (LedgerEntries عند ترحيل فاتورة الشراء)
        // =========================================================
        private async Task UpdateBatchPriceAndHistoryAsync(
            int prodId,                 // متغير: كود الصنف
            int customerId,             // متغير: كود المورد (من هيدر طلب الشراء)
            string? batchNo,            // متغير: رقم التشغيلة (من سطر طلب الشراء)
            DateTime? expiry,           // متغير: تاريخ الصلاحية (من سطر طلب الشراء)
            decimal newPublicPrice,     // متغير: سعر الجمهور الذي تم إدخاله/تعديله
            decimal unitCost)           // متغير: تكلفة متوقعة/مرجعية
        {
            // =========================================================
            // (0) حماية: لو مفيش باتش أو صلاحية → مش هنعمل حاجة
            // =========================================================
            if (string.IsNullOrWhiteSpace(batchNo) || !expiry.HasValue)
                return;

            // =========================================================
            // (1) قراءة الصنف (علشان نجيب السعر الأساسي)
            // =========================================================
            var product = await _context.Products
                .FirstOrDefaultAsync(p => p.ProdId == prodId);

            if (product == null)
                return; // حماية إضافية

            // =========================================================
            // (2) البحث عن Batch بنفس (الصنف + رقم التشغيلة + الصلاحية)
            // =========================================================
            var batch = await _context.Batches
                .FirstOrDefaultAsync(b =>
                    b.ProdId == prodId &&
                    b.BatchNo == batchNo &&
                    b.Expiry == expiry.Value);

            // متغير: السعر القديم للمقارنة وتحديد هل نكتب PriceHistory أم لا
            decimal oldPrice;

            // =========================================================
            // (3) إنشاء Batch جديد أو تحديث Batch موجود
            // =========================================================
            if (batch == null)
            {
                // لا يوجد Batch بهذه البيانات ⇒ إنشاؤه جديد
                oldPrice = product.PriceRetail; // نعتبر السعر القديم = سعر الصنف الأساسي

                batch = new Batch
                {
                    ProdId = prodId,
                    BatchNo = batchNo!,
                    Expiry = expiry.Value,

                    // تعليق: في طلب الشراء نربط أول مورد معروف للباتش (للرجوع)
                    CustomerId = customerId,

                    // تعليق: سعر الباتش (سعر الجمهور)
                    PriceRetailBatch = newPublicPrice,

                    // تعليق: تكلفة افتراضية/متوقعة
                    UnitCostDefault = unitCost,

                    EntryDate = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };

                _context.Batches.Add(batch);
            }
            else
            {
                // يوجد Batch بالفعل ⇒ نحدث سعره وتكلفته
                oldPrice = batch.PriceRetailBatch ?? product.PriceRetail;

                batch.PriceRetailBatch = newPublicPrice;
                batch.UnitCostDefault = unitCost;
                batch.UpdatedAt = DateTime.UtcNow;

                // لو CustomerId فاضي نملأه بأول مورد معروف
                if (!batch.CustomerId.HasValue)
                    batch.CustomerId = customerId;
            }

            // =========================================================
            // (4) لو السعر اتغيّر فعلاً ⇒ نسجل فى ProductPriceHistory
            // =========================================================
            if (oldPrice != newPublicPrice)
            {
                var history = new ProductPriceHistory
                {
                    ProdId = prodId,
                    OldPrice = oldPrice,
                    NewPrice = newPublicPrice,
                    ChangeDate = DateTime.UtcNow,

                    // تعليق: اسم اليوزر الحالي (تقدر تضيف "(PR)" لو تحب تمييز المصدر)
                    ChangedBy = GetCurrentUserDisplayName(),

                    CreatedAt = DateTime.UtcNow
                };

                _context.ProductPriceHistories.Add(history);

                // تعليق: تحديث خانة آخر تغيير سعر في جدول الأصناف (علامة فقط)
                product.LastPriceChangeDate = DateTime.UtcNow;
                _context.Products.Update(product);
            }

            // مفيش SaveChanges هنا → اللي ينادي الدالة هو اللي ينفذ SaveChangesAsync
        }






        // =========================================================
        // دالة مساعدة: تجيب اسم اليوزر الحالي من الـ Claims
        // ✅ تعديل بسيط: إضافة tag اختياري لتمييز مصدر العملية (مثلاً PR)
        // =========================================================
        private string GetCurrentUserDisplayName(string? tag = null)
        {
            string baseName = "System"; // متغير: الاسم الافتراضي لو مفيش تسجيل دخول

            // لو فيه يوزر عامل تسجيل دخول
            if (User?.Identity?.IsAuthenticated == true)
            {
                // نحاول الأول نجيب DisplayName (اسم الموظف الظاهر في التقارير)
                var displayName = User.FindFirst("DisplayName")?.Value;
                if (!string.IsNullOrWhiteSpace(displayName))
                    baseName = displayName!.Trim();
                else if (!string.IsNullOrWhiteSpace(User.Identity!.Name))
                    baseName = User.Identity.Name!.Trim(); // لو مفيش DisplayName نرجع لاسم الدخول العادي
            }

            // لو عايز تمييز للمصدر: (PR) أو (PI) أو غيره
            if (!string.IsNullOrWhiteSpace(tag))
                return $"{baseName} ({tag!.Trim()})";

            return baseName;
        }






 

// =========================================================
// دالة مساعدة اختيارية: تجيب رقم اليوزر من الـ Claims
// ✅ تعديل: دعم أكثر من Claim محتمل + Trim للحماية
// =========================================================
private int? GetCurrentUserId()
    {
        if (User?.Identity?.IsAuthenticated != true)
            return null; // مفيش يوزر حالي

        // متغير: نحاول نقرأ الـ ID من أكتر من Claim شائع
        string? idStr =
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("UserId")?.Value
            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrWhiteSpace(idStr))
            return null;

        idStr = idStr.Trim();

        // تعليق: لو الـ ID رقم صحيح نرجعه
        if (int.TryParse(idStr, out int id))
            return id;

        // تعليق: لو GUID أو قيمة غير رقمية → نرجع null (حسب تصميمك الحالي الـ PK int)
        return null;
    }






        #region Index (قائمة طلبات الشراء)

        /// <summary>
        /// عرض قائمة طلبات الشراء بنفس نظام القوائم الموحد.
        /// </summary>
        [RequirePermission("PurchaseRequests.Index")]
        public async Task<IActionResult> Index(
                string? search,                      // متغير: نص البحث
                string? searchBy,                    // متغير: نوع البحث: all / id / customer / warehouse / date / status ...
                string? sort,                        // متغير: عمود الترتيب: id / date / customer / warehouse / total / status ...
                string? dir,                         // متغير: اتجاه الترتيب: asc / desc
                string? searchMode = null,          // يحتوي / يبدأ / ينتهي
                bool useDateRange = false,           // متغير: هل فلتر التاريخ مفعّل؟
                DateTime? fromDate = null,           // متغير: من تاريخ/وقت
                DateTime? toDate = null,             // متغير: إلى تاريخ/وقت
                string? dateField = "PRDate",        // ✅ طلب الشراء: الحقل الافتراضي للتاريخ
                int? fromCode = null,                // متغير: من رقم طلب
                int? toCode = null,                  // متغير: إلى رقم طلب
                int page = 1,                        // متغير: رقم الصفحة
                int pageSize = 10,                   // متغير: حجم الصفحة (0 = الكل)
                string? filterCol_id = null,
                string? filterCol_date = null,
                string? filterCol_needby = null,
                string? filterCol_customer = null,
                string? filterCol_warehouse = null,
                string? filterCol_status = null,
                string? filterCol_total = null,
                string? filterCol_created = null
            )
        {
            // =========================================================
            // (0) قيم افتراضية — البحث الافتراضي على كل الأعمدة
            // =========================================================
            var pageSizeQuery = Request.Query["pageSize"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageSizeQuery) && int.TryParse(pageSizeQuery, out var psVal))
                pageSize = psVal;

            searchBy ??= "all";
            sort ??= "date";
            dir ??= "desc";
            dateField ??= "PRDate";
            searchMode = NormalizeSearchMode(searchMode);

            if (page < 1) page = 1;
            if (pageSize < 0) pageSize = 10;
            bool pageSizeOk = pageSize == 0 || pageSize == 10 || pageSize == 25 || pageSize == 50 || pageSize == 100 || pageSize == 200;
            if (pageSize > 0 && !pageSizeOk)
                pageSize = 10;

            // =========================================================
            // (1) الاستعلام الأساسي — Include للبحث في الأسماء
            // =========================================================
            IQueryable<PurchaseRequest> query = _context.PurchaseRequests.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
            {
                if (string.Equals(searchBy, "all", StringComparison.OrdinalIgnoreCase))
                    query = query.Include(pr => pr.Customer).Include(pr => pr.Warehouse);
                else if (string.Equals(searchBy, "customer", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(searchBy, "vendor", StringComparison.OrdinalIgnoreCase))
                    query = query.Include(pr => pr.Customer);
                else if (string.Equals(searchBy, "warehouse", StringComparison.OrdinalIgnoreCase))
                    query = query.Include(pr => pr.Warehouse);
            }
            query = await ApplyOperationalListVisibilityAsync(query);

            // =========================================================
            // (2) قراءة fromCode/toCode من الكويري للتوافق مع Export
            // =========================================================
            int? codeFrom = Request.Query.ContainsKey("codeFrom")
                ? TryParseNullableInt(Request.Query["codeFrom"])
                : null;
            int? codeTo = Request.Query.ContainsKey("codeTo")
                ? TryParseNullableInt(Request.Query["codeTo"])
                : null;
            int? finalFromCode = fromCode ?? codeFrom;
            int? finalToCode = toCode ?? codeTo;

            // =========================================================
            // (3) تطبيق الفلاتر (بحث + كود + تاريخ)
            // =========================================================
            query = ApplyFilters(
                query,
                search,
                searchBy,
                searchMode,
                finalFromCode,
                finalToCode,
                useDateRange,
                fromDate,
                toDate,
                dateField
            );

            // =========================================================
            // (3b) فلاتر الأعمدة (بنمط Excel)
            // =========================================================
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    query = query.Where(p => ids.Contains(p.PRId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_customer))
            {
                var ids = filterCol_customer.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    query = query.Where(p => ids.Contains(p.CustomerId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_warehouse))
            {
                var ids = filterCol_warehouse.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    query = query.Where(p => ids.Contains(p.WarehouseId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_status))
            {
                var vals = filterCol_status.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    query = query.Where(p => p.Status != null && vals.Contains(p.Status));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_total))
            {
                var vals = filterCol_total.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0)
                    query = query.Where(p => vals.Contains(p.ExpectedItemsTotal));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_date))
            {
                var parts = filterCol_date.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length >= 7).ToList();
                var dateFilters = new List<(int Year, int Month)>();
                foreach (var p in parts)
                {
                    if (p.Length >= 7 && p.IndexOf('-') >= 0 &&
                        int.TryParse(p.Substring(0, 4), out var y) &&
                        int.TryParse(p.Substring(5, 2), out var m) && m >= 1 && m <= 12)
                        dateFilters.Add((y, m));
                }
                if (dateFilters.Count > 0)
                    query = query.Where(p => dateFilters.Any(df => p.PRDate.Year == df.Year && p.PRDate.Month == df.Month));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_needby))
            {
                var parts = filterCol_needby.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length >= 7).ToList();
                var dateFilters = new List<(int Year, int Month)>();
                foreach (var p in parts)
                {
                    if (p.Length >= 7 && p.IndexOf('-') >= 0 &&
                        int.TryParse(p.Substring(0, 4), out var y) &&
                        int.TryParse(p.Substring(5, 2), out var m) && m >= 1 && m <= 12)
                        dateFilters.Add((y, m));
                }
                if (dateFilters.Count > 0)
                    query = query.Where(p => p.NeedByDate.HasValue && dateFilters.Any(df => p.NeedByDate!.Value.Year == df.Year && p.NeedByDate.Value.Month == df.Month));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_created))
            {
                var parts = filterCol_created.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length >= 7).ToList();
                var dateFilters = new List<(int Year, int Month)>();
                foreach (var p in parts)
                {
                    if (p.Length >= 7 && p.IndexOf('-') >= 0 &&
                        int.TryParse(p.Substring(0, 4), out var y) &&
                        int.TryParse(p.Substring(5, 2), out var m) && m >= 1 && m <= 12)
                        dateFilters.Add((y, m));
                }
                if (dateFilters.Count > 0)
                    query = query.Where(p => dateFilters.Any(df => p.CreatedAt.Year == df.Year && p.CreatedAt.Month == df.Month));
            }

            // =========================================================
            // (4) تطبيق الترتيب
            // =========================================================
            bool sortDesc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            query = ApplySort(query, sort, sortDesc);

            // =========================================================
            // (5) إجمالي الصافي من نفس الاستعلام بعد الفلاتر (قبل Paging)
            // =========================================================
            decimal totalNet = await query.SumAsync(x => (decimal?)x.ExpectedItemsTotal) ?? 0m; // PurchaseRequest يستخدم ExpectedItemsTotal بدلاً من NetTotal

            // =========================================================
            // (6) العدد الكلي بعد الفلاتر
            // =========================================================
            int totalCount = await query.CountAsync();

            int effectivePageSize = pageSize;
            if (pageSize == 0)
            {
                effectivePageSize = totalCount == 0 ? 10 : Math.Min(totalCount, 100_000);
                page = 1;
            }

            int totalPages = pageSize == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)effectivePageSize);

            // =========================================================
            // (7) قراءة صفحة واحدة فقط (مع تحميل Customer و Warehouse)
            // =========================================================
            var items = await query
                .Include(pr => pr.Customer)
                .Include(pr => pr.Warehouse)
                .Skip((page - 1) * effectivePageSize)
                .Take(effectivePageSize)
                .ToListAsync();

            // =========================================================
            // (8) تجهيز PagedResult
            // =========================================================
            var model = new PagedResult<PurchaseRequest>
            {
                Items = items,
                PageNumber = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                HasPrevious = page > 1,
                HasNext = pageSize != 0 && page < totalPages,

                Search = search,
                SortColumn = sort,
                SortDescending = sortDesc,

                UseDateRange = useDateRange,
                FromDate = fromDate,
                ToDate = toDate
            };

            // =========================================================
            // (9) ViewBag لحفظ حالة الفلاتر في الواجهة
            // =========================================================
            ViewBag.Search = search;
            ViewBag.SearchBy = searchBy;
            ViewBag.SearchMode = searchMode;
            ViewBag.Sort = sort;
            ViewBag.Dir = sortDesc ? "desc" : "asc";
            ViewBag.DateField = dateField;

            ViewBag.FromCode = finalFromCode;
            ViewBag.ToCode = finalToCode;
            ViewBag.CodeFrom = finalFromCode;
            ViewBag.CodeTo = finalToCode;
            ViewBag.TotalNet = totalNet;

            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_Date = filterCol_date;
            ViewBag.FilterCol_Needby = filterCol_needby;
            ViewBag.FilterCol_Customer = filterCol_customer;
            ViewBag.FilterCol_Warehouse = filterCol_warehouse;
            ViewBag.FilterCol_Status = filterCol_status;
            ViewBag.FilterCol_Total = filterCol_total;
            ViewBag.FilterCol_Created = filterCol_created;

            return View(model);
        }

        /// <summary>
        /// API: جلب القيم المميزة لعمود (للفلترة بنمط Excel).
        /// </summary>
        [RequirePermission("PurchaseRequests.Index")]
        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var col = (column ?? "").Trim().ToLowerInvariant();
            IQueryable<PurchaseRequest> q = _context.PurchaseRequests.AsNoTracking();
            q = await ApplyOperationalListVisibilityAsync(q);

            List<(string Value, string Display)> items;
            switch (col)
            {
                case "id":
                    items = (await q.Select(x => x.PRId).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                        .Select(v => (v.ToString(), v.ToString())).ToList();
                    break;
                case "date":
                    items = (await q.Select(x => new { x.PRDate.Year, x.PRDate.Month }).Distinct()
                        .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).Take(100).ToListAsync())
                        .Select(x => ($"{x.Year}-{x.Month:D2}", $"{x.Year}/{x.Month:D2}")).ToList();
                    break;
                case "needby":
                    items = (await q.Where(x => x.NeedByDate.HasValue).Select(x => new { x.NeedByDate!.Value.Year, x.NeedByDate.Value.Month }).Distinct()
                        .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).Take(100).ToListAsync())
                        .Select(x => ($"{x.Year}-{x.Month:D2}", $"{x.Year}/{x.Month:D2}")).ToList();
                    break;
                case "customer":
                    var custList = await q.Select(x => x.CustomerId).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                    var custNames = await _context.Customers.AsNoTracking()
                        .Where(c => custList.Contains(c.CustomerId))
                        .Select(c => new { c.CustomerId, c.CustomerName })
                        .ToListAsync();
                    items = custNames.Select(c => (c.CustomerId.ToString(), c.CustomerName ?? "")).ToList();
                    break;
                case "warehouse":
                    var whList = await q.Select(x => x.WarehouseId).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                    var whNames = await _context.Warehouses.AsNoTracking()
                        .Where(w => whList.Contains(w.WarehouseId))
                        .Select(w => new { w.WarehouseId, w.WarehouseName })
                        .ToListAsync();
                    items = whNames.Select(w => (w.WarehouseId.ToString(), w.WarehouseName ?? "")).ToList();
                    break;
                case "status":
                    items = (await q.Where(x => x.Status != null).Select(x => x.Status!).Distinct().OrderBy(c => c).Take(200).ToListAsync())
                        .Select(c => (c ?? "", c ?? "")).ToList();
                    break;
                case "total":
                    items = (await q.Select(x => x.ExpectedItemsTotal).Distinct().OrderBy(v => v).Take(200).ToListAsync())
                        .Select(v => (v.ToString(System.Globalization.CultureInfo.InvariantCulture), v.ToString("0.00"))).ToList();
                    break;
                case "created":
                    items = (await q.Select(x => new { x.CreatedAt.Year, x.CreatedAt.Month }).Distinct()
                        .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).Take(100).ToListAsync())
                        .Select(x => ($"{x.Year}-{x.Month:D2}", $"{x.Year}/{x.Month:D2}")).ToList();
                    break;
                default:
                    items = new List<(string Value, string Display)>();
                    break;
            }

            if (!string.IsNullOrEmpty(searchTerm) && items.Count > 0)
                items = items.Where(x => (x.Display ?? x.Value).ToLowerInvariant().Contains(searchTerm)).ToList();

            return Json(items.Select(x => new { value = x.Value, display = x.Display }));
        }

        #endregion













 

// =========================================================
// دالة مساعدة: تجهيز الموردين والمخازن للفورم (هيدر طلب الشراء)
// - الموردين: من جدول Customers (لأن المورد عندك مُعرّف كـ Customer)
// - المخازن: من جدول Warehouses
// =========================================================
private async Task PopulateDropDownsAsync(
    int? selectedCustomerId = null,     // متغير: كود المورد المختار (لو طلب قديم)
    int? selectedWarehouseId = null)    // متغير: كود المخزن المختار (لو طلب قديم)
    {
        // =========================================================
        // (1) تحميل العملاء بنفس منطق فاتورة البيع (بدون فلترة نشط فقط)
        // =========================================================
        var customerQueryPr = _context.Customers.AsNoTracking();
        customerQueryPr = await _accountVisibilityService.ApplyCustomerVisibilityFilterAsync(customerQueryPr);
        var nowForCredit = DateTime.Now;
        var customers = await customerQueryPr
            .Include(c => c.Governorate)
            .Include(c => c.District)
            .Include(c => c.Area)
            .Include(c => c.Policy)
            .OrderBy(c => c.CustomerName)
            .Select(c => new
            {
                Id = c.CustomerId,                               // متغير: كود المورد
                Name = c.CustomerName,                           // متغير: اسم المورد
                Phone = c.Phone1 ?? string.Empty,                // متغير: الهاتف
                Address = c.Address ?? string.Empty,             // متغير: العنوان

                // متغير: اسم المحافظة/الحي/المنطقة (لو موجودين)
                Gov = c.Governorate != null
                            ? c.Governorate.GovernorateName
                            : string.Empty,
                District = c.District != null
                            ? c.District.DistrictName
                            : string.Empty,
                Area = c.Area != null
                            ? c.Area.AreaName
                            : string.Empty,

                Credit = c.CreditLimit
                    + ((c.CreditLimitTemporaryIncrease.HasValue
                        && c.CreditLimitTemporaryIncrease.Value > 0
                        && c.CreditLimitTemporaryUntil.HasValue
                        && c.CreditLimitTemporaryUntil.Value > nowForCredit)
                        ? c.CreditLimitTemporaryIncrease.Value
                        : 0m),
                CurrentBalance = c.CurrentBalance,
                PolicyId = c.PolicyId,
                PolicyName = c.Policy != null ? c.Policy.Name : "",
                IsActive = c.IsActive
            })
            .ToListAsync();

        if (selectedCustomerId.HasValue && !customers.Any(c => c.Id == selectedCustomerId.Value))
        {
            var extra = await _context.Customers
                .AsNoTracking()
                .Where(c => c.CustomerId == selectedCustomerId.Value)
                .Include(c => c.Governorate)
                .Include(c => c.District)
                .Include(c => c.Area)
                .Include(c => c.Policy)
                .Select(c => new
                {
                    Id = c.CustomerId,
                    Name = c.CustomerName ?? string.Empty,
                    Phone = c.Phone1 ?? string.Empty,
                    Address = c.Address ?? string.Empty,
                    Gov = c.Governorate != null ? c.Governorate.GovernorateName : string.Empty,
                    District = c.District != null ? c.District.DistrictName : string.Empty,
                    Area = c.Area != null ? c.Area.AreaName : string.Empty,
                    Credit = c.CreditLimit
                        + ((c.CreditLimitTemporaryIncrease.HasValue
                            && c.CreditLimitTemporaryIncrease.Value > 0
                            && c.CreditLimitTemporaryUntil.HasValue
                            && c.CreditLimitTemporaryUntil.Value > nowForCredit)
                            ? c.CreditLimitTemporaryIncrease.Value
                            : 0m),
                    CurrentBalance = c.CurrentBalance,
                    PolicyId = c.PolicyId,
                    PolicyName = c.Policy != null ? c.Policy.Name : "",
                    IsActive = c.IsActive
                })
                .FirstOrDefaultAsync();
            if (extra != null)
                customers.Insert(0, extra);
        }

        // متغير: إرسال قائمة الموردين/العملاء للـ View لاستخدامها في datalist/combos
        ViewBag.Customers = customers;

        // =========================================================
        // (2) لو في مورد مختار (طلب قديم) نحضر اسمه لعرضه تلقائيًا
        // =========================================================
        if (selectedCustomerId.HasValue)
        {
            var current = customers.FirstOrDefault(c => c.Id == selectedCustomerId.Value);
            if (current != null)
            {
                ViewBag.SelectedCustomerName = current.Name; // متغير: اسم المورد الحالي
            }
        }

        // =========================================================
        // (3) تحميل المخازن للكومبو
        // =========================================================
        var warehouses = await _context.Warehouses
            .AsNoTracking()
            .OrderBy(w => w.WarehouseName)
            .ToListAsync();

        // متغير: إرسال قائمة المخازن للـ View كـ SelectList
    ViewBag.Warehouses = new SelectList(
        warehouses,
        "WarehouseId",       // متغير: كود المخزن
        "WarehouseName",     // متغير: اسم المخزن
        selectedWarehouseId  // متغير: المخزن المختار (لو موجود)
    );
}

private async Task LoadPrintHeaderSettingsAsync()
{
    try
    {
        var printHeader = await _context.PrintHeaderSettings
            .AsNoTracking()
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync();

        ViewBag.PrintHeaderCompanyName = string.IsNullOrWhiteSpace(printHeader?.CompanyName)
            ? "شركة الهدى"
            : printHeader!.CompanyName.Trim();
        ViewBag.PrintHeaderLogoUrl = string.IsNullOrWhiteSpace(printHeader?.LogoPath)
            ? null
            : Url.Content(printHeader!.LogoPath!);
    }
    catch (Exception ex) when (IsMissingPrintHeaderSettingsTable(ex))
    {
        ViewBag.PrintHeaderCompanyName = "شركة الهدى";
        ViewBag.PrintHeaderLogoUrl = null;
    }
}

private static bool IsMissingPrintHeaderSettingsTable(Exception ex)
{
    Exception? current = ex;
    while (current != null)
    {
        var msg = current.Message ?? string.Empty;
        if (msg.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase) &&
            msg.Contains("PrintHeaderSettings", StringComparison.OrdinalIgnoreCase))
            return true;
        current = current.InnerException;
    }
    return false;
}








        // =========================================================
        // دالة مساعدة: تحميل قائمة الأصناف للأوتوكومبليت في سطر طلب الشراء
        // - الهدف: تغذية datalist بسرعة باسم الصنف + كوده + سعر الجمهور
        // =========================================================
        private async Task LoadProductsForAutoCompleteAsync()
        {
            // متغير: قائمة الأصناف من جدول Products
            var products = await _context.Products
                .AsNoTracking()                       // تعليق: قراءة فقط بدون تتبع
                .OrderBy(p => p.ProdName)             // تعليق: ترتيب حسب اسم الصنف
                .Select(p => new
                {
                    Id = p.ProdId,                            // متغير: كود الصنف الداخلي (الكود الوحيد)
                    Name = p.ProdName ?? string.Empty,        // متغير: اسم الصنف
                    GenericName = p.GenericName ?? string.Empty, // متغير: الاسم العلمي (للـ بدائل - لو الواجهة تستخدمه)
                    Company = p.Company ?? string.Empty,      // متغير: الشركة (لو الواجهة تستخدمه)
                    PriceRetail = p.PriceRetail,              // متغير: سعر الجمهور
                    HasQuota = p.HasQuota                     // متغير: هل للصنف كوتة أم لا
                                                              // ملاحظة: لا نرجع QuotaQuantity لأنه غير مستخدم في الواجهة حاليًا
                })
                .ToListAsync();

            // متغير: نرسل القائمة إلى الواجهة لتغذية الـ datalist
            ViewBag.ProductsAuto = products;
        }







        // =========================================================
        // API: إرجاع بدائل الصنف (نفس الاسم العلمي GenericName) على شكل JSON
        // ✅ مفيد في طلب الشراء لاقتراح بدائل بنفس المادة/الاسم العلمي
        // =========================================================
        [RequirePermission("PurchaseRequests.Edit")]
        [HttpGet]
        public async Task<IActionResult> GetAlternativeProducts(int prodId)
        {
            // حماية: كود صنف غير صحيح
            if (prodId <= 0)
                return Json(Array.Empty<object>());

            // (1) جلب الصنف الأساسي لمعرفة الـ GenericName
            var mainProd = await _context.Products
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProdId == prodId);

            // حماية: لو الصنف غير موجود أو مفيش اسم علمي
            if (mainProd == null || string.IsNullOrWhiteSpace(mainProd.GenericName))
                return Json(Array.Empty<object>());

            // متغير: الاسم العلمي بعد تنظيف المسافات
            string generic = mainProd.GenericName.Trim();

            // (2) جلب البدائل: نفس GenericName مع استبعاد الصنف نفسه
            var alts = await _context.Products
                .AsNoTracking()
                .Where(p => p.GenericName != null
                            && p.GenericName.Trim() == generic
                            && p.ProdId != prodId)
                .OrderBy(p => p.ProdName)
                .Select(p => new
                {
                    id = p.ProdId,                          // متغير: كود الصنف البديل
                    name = p.ProdName ?? string.Empty,      // متغير: اسم الصنف البديل
                    company = p.Company ?? string.Empty,    // متغير: الشركة
                    price = p.PriceRetail                   // متغير: سعر الجمهور
                })
                .ToListAsync();

            return Json(alts);
        }

        // =========================================================
        // API: بيانات كارت الصنف (الكمية في المخزن، إجمالي الكمية، الخصم المرجح %) للعرض عند اختيار الصنف
        // =========================================================
        [RequirePermission("PurchaseRequests.Edit")]
        [HttpGet]
        public async Task<IActionResult> GetProductCardInfo(int prodId, int? warehouseId = null)
        {
            if (prodId <= 0)
                return Json(new { ok = false, qtyInWarehouse = 0, qtyAllWarehouses = 0, weightedDiscount = 0m });

            int qtyAll = await _stockAnalysis.GetCurrentQtyAsync(prodId, null);
            int qtyInWh = warehouseId.HasValue && warehouseId.Value > 0
                ? await _stockAnalysis.GetCurrentQtyAsync(prodId, warehouseId)
                : qtyAll;
            // الخصم المرجح: نفس منطق تقرير أرصدة الأصناف (مخزن محدد = خصم فعّال يشمل الـ override، وإلا متوسط عام)
            decimal weightedDisc = warehouseId.HasValue && warehouseId.Value > 0
                ? await _stockAnalysis.GetEffectivePurchaseDiscountAsync(prodId, warehouseId, null)
                : await _stockAnalysis.GetWeightedPurchaseDiscountCurrentAsync(prodId);

            return Json(new
            {
                ok = true,
                qtyInWarehouse = qtyInWh,
                qtyAllWarehouses = qtyAll,
                weightedDiscount = weightedDisc
            });
        }

        // =========================================================
        // API: إرجاع المطلوب (طلبات غير محولة) التي تحتوي على الصنف المعطى
        // للعرض في جدول "المطلوب (طلبات غير محولة)" عند اختيار الصنف
        // =========================================================
        [RequirePermission("PurchaseRequests.Edit")]
        [HttpGet]
        public async Task<IActionResult> GetProductDemandInfo(int prodId, int currentPRId = 0)
        {
            if (prodId <= 0)
                return Json(new { ok = false, required = Array.Empty<object>(), sales = Array.Empty<object>(), intermediary = Array.Empty<object>() });

            // طلبات غير محولة تحتوي على هذا الصنف (مع استبعاد الطلب الحالي إن وُجد)
            var lines = await _context.PRLines
                .AsNoTracking()
                .Include(l => l.PurchaseRequest)
                .ThenInclude(pr => pr.Customer)
                .Where(l => l.ProdId == prodId
                    && l.PurchaseRequest != null
                    && !l.PurchaseRequest.IsConverted
                    && (currentPRId <= 0 || l.PRId != currentPRId))
                .OrderBy(l => l.PRId)
                .ThenBy(l => l.LineNo)
                .ToListAsync();

            // تجميع حسب طلب الشراء: اسم العميل، التاريخ، إجمالي الكمية، متوسط الخصم المرجح %
            var grouped = lines
                .GroupBy(l => l.PRId)
                .Select(g =>
                {
                    var first = g.First();
                    var pr = first.PurchaseRequest;
                    return new
                    {
                        customerName = pr?.Customer?.CustomerName ?? "",
                        date = pr != null ? pr.PRDate.ToString("yyyy-MM-dd") : "",
                        qty = g.Sum(l => l.QtyRequested),
                        weightedDiscount = g.Average(l => l.PurchaseDiscountPct)
                    };
                })
                .OrderByDescending(x => x.date)
                .ToList();

            return Json(new
            {
                ok = true,
                required = grouped,
                sales = Array.Empty<object>(),
                intermediary = Array.Empty<object>()
            });
        }

        // =========================================================
        // API: إجمالي الكمية المبيعة والوسيط للصنف في الفترة (من - إلى)
        // الوسيط = ترتيب الكميات وأخذ الرقم في الوسط
        // =========================================================
        [RequirePermission("PurchaseRequests.Edit")]
        [HttpGet]
        public async Task<IActionResult> GetProductSalesInPeriod(int prodId, string fromDate, string toDate)
        {
            if (prodId <= 0)
                return Json(new { ok = false, totalQty = 0, medianQty = 0m });

            DateTime dateFrom = DateTime.Today.AddMonths(-1);
            DateTime dateTo = DateTime.Today;
            if (!string.IsNullOrWhiteSpace(fromDate) && DateTime.TryParse(fromDate, out var fd)) dateFrom = fd.Date;
            if (!string.IsNullOrWhiteSpace(toDate) && DateTime.TryParse(toDate, out var td)) dateTo = td.Date;
            if (dateFrom > dateTo) { var t = dateFrom; dateFrom = dateTo; dateTo = t; }

            var quantities = await (from l in _context.SalesInvoiceLines.AsNoTracking()
                                   join si in _context.SalesInvoices on l.SIId equals si.SIId
                                   where l.ProdId == prodId && si.SIDate >= dateFrom && si.SIDate <= dateTo
                                   select l.Qty).ToListAsync();

            int totalQty = quantities.Sum();
            decimal medianQty = 0m;
            if (quantities.Count > 0)
            {
                var sorted = quantities.OrderBy(q => q).ToList();
                int n = sorted.Count;
                if (n % 2 == 1)
                    medianQty = sorted[n / 2];
                else
                    medianQty = (sorted[n / 2 - 1] + sorted[n / 2]) / 2m;
            }

            return Json(new { ok = true, totalQty, medianQty });
        }





        // ================================================================
        // DTO: بيانات إضافة سطر (جاية من AJAX) — طلب الشراء
        // ================================================================
        public class AddLineJsonDto
        {
            public int PRId { get; set; }                    // متغير: رقم طلب الشراء
            public int prodId { get; set; }                  // متغير: كود الصنف
            public int qty { get; set; }                     // متغير: الكمية المطلوبة
            public decimal unitCost { get; set; }            // متغير: تكلفة متوقعة للوحدة (مش هنثق فيها - هنحسب)
            public decimal priceRetail { get; set; }         // متغير: سعر الجمهور
            public decimal purchaseDiscountPct { get; set; } // متغير: خصم الشراء %
            public string? BatchNo { get; set; }             // متغير: رقم التشغيلة (مفضلة)
            public string? expiryText { get; set; }          // متغير: الصلاحية كنص MM/YYYY (مفضلة)
        }

        [RequirePermission("PurchaseRequests.Edit")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddLineJson([FromBody] AddLineJsonDto dto)
        {
            // تعليق: Transaction مهم لأننا بنكتب في أكتر من جدول:
            // PRLines + Batches + ProductPriceHistories (+ Products.LastPriceChangeDate)
            await using var tx = await _context.Database.BeginTransactionAsync();

            try
            {
                // =========================
                // 0) فحص سريع للمدخلات
                // =========================
                if (dto == null)
                    return BadRequest(new { ok = false, message = "لم يتم إرسال بيانات." });

                if (dto.PRId <= 0 || dto.prodId <= 0)
                    return BadRequest(new { ok = false, message = "بيانات الطلب/الصنف غير صحيحة." });

                if (dto.qty <= 0)
                    return BadRequest(new { ok = false, message = "الكمية يجب أن تكون أكبر من صفر." });

                if (dto.priceRetail < 0)
                    return BadRequest(new { ok = false, message = "سعر الجمهور لا يمكن أن يكون سالب." });

                // =========================
                // 0.1) تنظيف الخصم + حساب التكلفة المتوقعة للوحدة على السيرفر
                // =========================
                var disc = dto.purchaseDiscountPct; // متغير: الخصم
                if (disc < 0) disc = 0;
                if (disc > 100) disc = 100;

                // متغير: قيمة السطر بعد الخصم (على أساس سعر الجمهور)
                var lineValue = dto.qty * dto.priceRetail * (1m - (disc / 100m));

                // متغير: التكلفة المتوقعة للوحدة (محسوبة فعلياً)
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

                // متغير: الصلاحية كـ Date فقط (لتثبيت التاريخ بدون وقت)
                DateTime? expDate = expiry?.Date;

                // =========================
                // 2) تحميل طلب الشراء (الهيدر)
                // =========================
                var pr = await _context.PurchaseRequests
                    .FirstOrDefaultAsync(p => p.PRId == dto.PRId);

                if (pr == null)
                    return NotFound(new { ok = false, message = "طلب الشراء غير موجود." });

                if (pr.WarehouseId <= 0)
                    return BadRequest(new { ok = false, message = "يجب اختيار مخزن قبل إضافة سطور." });

                // =========================
                // 2.1) منع التعديل لو الطلب تم تحويله بالفعل
                // =========================
                // ملاحظة: لو اسم الفلاج عندك مختلف بدّله فقط هنا
                if (pr.IsConverted)
                {
                    await tx.RollbackAsync();
                    return BadRequest(new
                    {
                        ok = false,
                        message = "لا يمكن إضافة/تعديل سطور: هذا الطلب تم تحويله بالفعل إلى فاتورة شراء."
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
                // 3.1) تحميل السطور الحالية من قاعدة البيانات (علشان LineNo)
                // =========================
                var currentLines = await _context.PRLines
                    .Where(l => l.PRId == dto.PRId)
                    .ToListAsync();

                // =========================
                // 4) Merge في PRLines
                // نفس الصنف + نفس التشغيلة/الصلاحية المفضلة + نفس السعر + نفس الخصم + نفس التكلفة المتوقعة
                // =========================
                var existingLine = await _context.PRLines.FirstOrDefaultAsync(x =>
                    x.PRId == dto.PRId &&
                    x.ProdId == dto.prodId &&
                    (x.PreferredBatchNo ?? "").Trim() == (batchNo ?? "") &&
                    ((x.PreferredExpiry.HasValue ? x.PreferredExpiry.Value.Date : (DateTime?)null) == expDate) &&
                    x.PriceRetail == dto.priceRetail &&
                    x.PurchaseDiscountPct == disc &&
                    x.ExpectedCost == computedUnitCost
                );

                PRLine affectedLine; // متغير: السطر المتأثر
                int qtyDelta;         // متغير: فرق الكمية (مفيد للّوج)

                if (existingLine != null)
                {
                    qtyDelta = dto.qty;

                    // ✅ طلب الشراء: الكمية اسمها QtyRequested
                    existingLine.QtyRequested += dto.qty;

                    // ✅ أسماء الأعمدة الصحيحة في PRLine
                    existingLine.PreferredBatchNo = batchNo;
                    existingLine.PreferredExpiry = expDate;
                    existingLine.ExpectedCost = computedUnitCost;

                    // تثبيت السعر/الخصم كما هو في نفس السطر
                    existingLine.PriceRetail = dto.priceRetail;
                    existingLine.PurchaseDiscountPct = disc;

                    affectedLine = existingLine;
                }
                else
                {
                    // ✅ حساب LineNo من السطور الموجودة
                    var nextLineNo = (currentLines.Any() ? currentLines.Max(l => l.LineNo) : 0) + 1;

                    affectedLine = new PRLine
                    {
                        PRId = pr.PRId,
                        LineNo = nextLineNo,
                        ProdId = dto.prodId,

                        // ✅ أسماء الأعمدة الصحيحة في PRLine
                        QtyRequested = dto.qty,               // متغير: الكمية المطلوبة
                        ExpectedCost = computedUnitCost,      // متغير: التكلفة المتوقعة للوحدة

                        PriceRetail = dto.priceRetail,
                        PurchaseDiscountPct = disc,

                        PreferredBatchNo = batchNo,           // متغير: التشغيلة المفضلة
                        PreferredExpiry = expDate             // متغير: الصلاحية المفضلة
                    };

                    qtyDelta = dto.qty;
                    _context.PRLines.Add(affectedLine);
                }

                // =========================
                // 4.1) تحديث/إنشاء Batch + تسجيل تغيير السعر في ProductPriceHistory
                // ✅ التأثير في طلب الشراء: السعر والباتش فقط
                // =========================
                await UpdateBatchPriceAndHistoryAsync(
                    prodId: dto.prodId,
                    customerId: pr.CustomerId,        // متغير: المورد من هيدر الطلب
                    batchNo: batchNo,
                    expiry: expDate,
                    newPublicPrice: dto.priceRetail,
                    unitCost: computedUnitCost
                );

                // =========================
                // 5) حفظ + Commit
                // =========================
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                // =========================
                // 6) إعادة حساب إجماليات طلب الشراء
                // =========================
                await _docTotals.RecalcPurchaseRequestTotalsAsync(dto.PRId);

                // =========================
                // 7) LogActivity
                // =========================
                await _activityLogger.LogAsync(
                    existingLine != null ? UserActionType.Edit : UserActionType.Create,
                    "PRLine",
                    affectedLine.PRId,
                    $"PRId={pr.PRId} | ProdId={dto.prodId} | QtyDelta={qtyDelta}"
                );

                // =========================
                // 8) رجّع السطور + الإجماليات (بنفس شكل JSON)
                // =========================
                var linesNow = await _context.PRLines
                    .Where(l => l.PRId == pr.PRId)
                    .OrderBy(l => l.LineNo)
                    .ToListAsync();

                var prodIds = linesNow.Select(l => l.ProdId).Distinct().ToList();
                var prodMap = await _context.Products
                    .Where(p => prodIds.Contains(p.ProdId))
                    .Select(p => new { p.ProdId, p.ProdName })
                    .ToDictionaryAsync(x => x.ProdId, x => x.ProdName ?? "");

                int totalLines = linesNow.Count;
                int totalItems = linesNow.Select(x => x.ProdId).Distinct().Count();
                int totalQty = linesNow.Sum(x => x.QtyRequested);

                decimal totalRetail = linesNow.Sum(x => x.QtyRequested * x.PriceRetail);
                decimal totalDiscount = linesNow.Sum(x => (x.QtyRequested * x.PriceRetail) * (x.PurchaseDiscountPct / 100m));
                decimal totalAfterDiscount = totalRetail - totalDiscount;

                var linesDto = linesNow.Select(l =>
                {
                    var name = prodMap.TryGetValue(l.ProdId, out var n) ? n : "";
                    var lv = (l.QtyRequested * l.PriceRetail) * (1 - (l.PurchaseDiscountPct / 100m));

                    return new
                    {
                        lineNo = l.LineNo,
                        prodId = l.ProdId,
                        prodName = name,
                        qty = l.QtyRequested,                    // ✅
                        priceRetail = l.PriceRetail,
                        discPct = l.PurchaseDiscountPct,
                        batchNo = l.PreferredBatchNo,            // ✅
                        expiry = l.PreferredExpiry?.ToString("yyyy-MM-dd"), // ✅
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
        // GetLinesJson — جلب سطور طلب الشراء كـ JSON (للاستخدام بعد تحميل body بالأسهم/بحث)
        // ================================================================
        [RequirePermission("PurchaseRequests.Show")]
        [HttpGet]
        public async Task<IActionResult> GetLinesJson(int id)
        {
            if (id <= 0)
                return BadRequest(new { ok = false, message = "رقم الطلب غير صحيح." });

            var pr = await _context.PurchaseRequests.AsNoTracking()
                .FirstOrDefaultAsync(x => x.PRId == id);
            if (pr == null)
                return NotFound(new { ok = false, message = "طلب الشراء غير موجود." });

            var linesNow = await _context.PRLines
                .Where(l => l.PRId == id)
                .OrderBy(l => l.LineNo)
                .ToListAsync();

            var prodIds = linesNow.Select(l => l.ProdId).Distinct().ToList();
            var prodMap = await _context.Products
                .Where(p => prodIds.Contains(p.ProdId))
                .Select(p => new { p.ProdId, p.ProdName })
                .ToDictionaryAsync(x => x.ProdId, x => x.ProdName ?? "");

            int totalLines = linesNow.Count;
            int totalItems = linesNow.Select(x => x.ProdId).Distinct().Count();
            int totalQty = linesNow.Sum(x => x.QtyRequested);
            decimal totalRetail = linesNow.Sum(x => x.QtyRequested * x.PriceRetail);
            decimal totalDiscount = linesNow.Sum(x => (x.QtyRequested * x.PriceRetail) * (x.PurchaseDiscountPct / 100m));
            decimal totalAfterDiscount = totalRetail - totalDiscount;
            decimal taxAmount = pr.TaxAmount;
            decimal totalAfterDiscountAndTax = totalAfterDiscount + taxAmount;

            var linesDto = linesNow.Select(l =>
            {
                var name = prodMap.TryGetValue(l.ProdId, out var n) ? n : "";
                var lv = (l.QtyRequested * l.PriceRetail) * (1 - (l.PurchaseDiscountPct / 100m));
                return new
                {
                    lineNo = l.LineNo,
                    prodId = l.ProdId,
                    prodName = name,
                    qty = l.QtyRequested,
                    qtyRequested = l.QtyRequested,
                    priceRetail = l.PriceRetail,
                    discPct = l.PurchaseDiscountPct,
                    purchaseDiscountPct = l.PurchaseDiscountPct,
                    batchNo = l.PreferredBatchNo,
                    preferredBatchNo = l.PreferredBatchNo,
                    expiry = l.PreferredExpiry?.ToString("yyyy-MM-dd"),
                    preferredExpiry = l.PreferredExpiry?.ToString("yyyy-MM-dd"),
                    lineValue = lv
                };
            }).ToList();

            return Json(new
            {
                ok = true,
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
                    totalAfterDiscountAndTax,
                    netTotal = totalAfterDiscountAndTax
                }
            });
        }

        // ================================================================
        // DTO: بيانات مسح سطر (جاية من AJAX) — طلب الشراء
        // ================================================================
        public class RemoveLineJsonDto
        {
            public int PRId { get; set; }    // متغير: رقم طلب الشراء
            public int LineNo { get; set; }  // متغير: رقم السطر داخل الطلب
        }

        [RequirePermission("PurchaseRequests.Delete")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveLineJson([FromBody] RemoveLineJsonDto dto)
        {
            try
            {
                // =========================
                // 0) فحص المدخلات
                // =========================
                if (dto == null || dto.PRId <= 0 || dto.LineNo <= 0)
                    return BadRequest(new { ok = false, message = "بيانات المسح غير صحيحة." });

                // =========================
                // 1) تحميل طلب الشراء (الهيدر)
                // =========================
                var pr = await _context.PurchaseRequests
                    .FirstOrDefaultAsync(x => x.PRId == dto.PRId);

                if (pr == null)
                    return NotFound(new { ok = false, message = "طلب الشراء غير موجود." });

                // =========================
                // 1.1) منع التعديل لو الطلب تم تحويله بالفعل
                // =========================
                // ملاحظة: لو اسم الفلاج عندك مختلف بدّله فقط هنا
                if (pr.IsConverted)
                    return BadRequest(new { ok = false, message = "هذا الطلب تم تحويله بالفعل ولا يمكن تعديله." });

                // =========================
                // 2) تحميل السطر المطلوب من PRLines
                // =========================
                var line = await _context.PRLines
                    .FirstOrDefaultAsync(l => l.PRId == dto.PRId && l.LineNo == dto.LineNo);

                if (line == null)
                    return NotFound(new { ok = false, message = "السطر غير موجود." });

                // =========================
                // 3) Transaction (حذف السطر + Recalc)
                // =========================
                await using var tx = await _context.Database.BeginTransactionAsync();

                // =========================
                // 4) حذف سطر طلب الشراء
                // =========================
                _context.PRLines.Remove(line);
                await _context.SaveChangesAsync();

                // =========================
                // 5) إعادة حساب إجماليات طلب الشراء (داخل نفس الترانزاكشن)
                // =========================
                await _docTotals.RecalcPurchaseRequestTotalsAsync(dto.PRId);
                await _context.SaveChangesAsync();

                await tx.CommitAsync();

                // =========================
                // 6) LogActivity (بعد الـ Commit)
                // =========================
                try
                {
                    await _activityLogger.LogAsync(
                        UserActionType.Delete,
                        "PRLine",
                        dto.PRId,
                        $"PRId={dto.PRId} | LineNo={dto.LineNo} | ProdId={line.ProdId} | QtyRequested={line.QtyRequested}"
                    );
                }
                catch
                {
                    // تعليق: لا نوقف العملية لو اللوج حصل فيه مشكلة
                }

                // =========================
                // 7) رجّع السطور + الإجماليات بعد المسح (نفس شكل JSON)
                // =========================
                var linesNow = await _context.PRLines
                    .Where(l => l.PRId == dto.PRId)
                    .OrderBy(l => l.LineNo)
                    .ToListAsync();

                var prodIds = linesNow.Select(l => l.ProdId).Distinct().ToList();
                var prodMap = await _context.Products
                    .Where(p => prodIds.Contains(p.ProdId))
                    .Select(p => new { p.ProdId, p.ProdName })
                    .ToDictionaryAsync(x => x.ProdId, x => x.ProdName ?? "");

                int totalLines = linesNow.Count;
                int totalItems = linesNow.Select(x => x.ProdId).Distinct().Count();
                int totalQty = linesNow.Sum(x => x.QtyRequested);

                decimal totalRetail = linesNow.Sum(x => x.QtyRequested * x.PriceRetail);
                decimal totalDiscount = linesNow.Sum(x => (x.QtyRequested * x.PriceRetail) * (x.PurchaseDiscountPct / 100m));
                decimal totalAfterDiscount = totalRetail - totalDiscount;

                var linesDto = linesNow.Select(l =>
                {
                    var name = prodMap.TryGetValue(l.ProdId, out var n) ? n : "";
                    var lv = (l.QtyRequested * l.PriceRetail) * (1 - (l.PurchaseDiscountPct / 100m));

                    return new
                    {
                        lineNo = l.LineNo,
                        prodId = l.ProdId,
                        prodName = name,
                        qty = l.QtyRequested,                              // ✅
                        priceRetail = l.PriceRetail,
                        discPct = l.PurchaseDiscountPct,
                        batchNo = l.PreferredBatchNo,                      // ✅
                        expiry = l.PreferredExpiry?.ToString("yyyy-MM-dd"),// ✅
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
        // DTO: بيانات مسح كل السطور (جاية من AJAX) — طلب الشراء
        // ================================================================
        public class ClearAllLinesJsonDto
        {
            public int PRId { get; set; } // متغير: رقم طلب الشراء
        }

        [RequirePermission("PurchaseRequests.Delete")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClearAllLinesJson([FromBody] ClearAllLinesJsonDto dto)
        {
            try
            {
                // =========================
                // 0) فحص المدخلات
                // =========================
                if (dto == null || dto.PRId <= 0)
                    return BadRequest(new { ok = false, message = "بيانات المسح غير صحيحة." });

                // =========================
                // 1) تحميل طلب الشراء (الهيدر)
                // =========================
                var pr = await _context.PurchaseRequests
                    .FirstOrDefaultAsync(x => x.PRId == dto.PRId);

                if (pr == null)
                    return NotFound(new { ok = false, message = "طلب الشراء غير موجود." });

                // =========================
                // 1.1) منع التعديل لو الطلب تم تحويله بالفعل
                // =========================
                // ملاحظة: لو اسم الفلاج عندك مختلف بدّله فقط هنا
                if (pr.IsConverted)
                    return BadRequest(new { ok = false, message = "هذا الطلب تم تحويله بالفعل ولا يمكن تعديله." });

                // =========================
                // 2) تحميل كل سطور الطلب
                // =========================
                var lines = await _context.PRLines
                    .Where(l => l.PRId == dto.PRId)
                    .OrderBy(l => l.LineNo)
                    .ToListAsync();

                if (lines.Count == 0)
                {
                    // رجّع نفس شكل RemoveLineJson (lines + totals)
                    return Json(new
                    {
                        ok = true,
                        message = "لا توجد أصناف لمسحها.",
                        lines = Array.Empty<object>(),
                        totals = new
                        {
                            totalLines = 0,
                            totalItems = 0,
                            totalQty = 0,
                            totalRetail = 0m,
                            totalDiscount = 0m,
                            totalAfterDiscount = 0m
                        }
                    });
                }

                // =========================
                // 3) Transaction: مسح السطور + Recalc للهيدر
                // =========================
                await using var tx = await _context.Database.BeginTransactionAsync();

                // =========================
                // 4) حذف كل سطور الطلب
                // =========================
                _context.PRLines.RemoveRange(lines);
                await _context.SaveChangesAsync();

                // =========================
                // 5) إعادة حساب إجماليات طلب الشراء (داخل نفس الـ Transaction)
                // =========================
                await _docTotals.RecalcPurchaseRequestTotalsAsync(dto.PRId);
                await _context.SaveChangesAsync();

                await tx.CommitAsync();

                // =========================
                // 6) LogActivity (بعد الـ Commit)
                // =========================
                try
                {
                    await _activityLogger.LogAsync(
                        UserActionType.Delete,
                        "PRLines",
                        dto.PRId,
                        $"PRId={dto.PRId} | ClearAllLines | LinesCount={lines.Count}"
                    );
                }
                catch
                {
                    // تعليق: لا نوقف العملية لو اللوج حصل فيه مشكلة
                }

                // =========================
                // 7) رجّع السطور + الإجماليات بعد المسح (هتكون فاضية)
                // =========================
                return Json(new
                {
                    ok = true,
                    message = "تم مسح جميع الأصناف بنجاح.",
                    lines = Array.Empty<object>(),
                    totals = new
                    {
                        totalLines = 0,
                        totalItems = 0,
                        totalQty = 0,
                        totalRetail = 0m,
                        totalDiscount = 0m,
                        totalAfterDiscount = 0m
                    }
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { ok = false, message = ex.Message });
            }
        }










        // ================================================================
        // SaveTaxJson — طلب الشراء (PR)
        // ================================================================
        // ✅ الدور الحقيقي هنا في طلب الشراء:
        // - طلب الشراء لا يحتوي ضريبة محفوظة (لا يوجد TaxTotal في الموديل).
        // - لذلك هذه الدالة تستخدم لإعادة حساب إجماليات الطلب (TotalQtyRequested + ExpectedItemsTotal)
        //   ثم إرجاع النتائج للواجهة بعد أي تعديل (إضافة/مسح/تعديل سطور).
        //
        // ❗ملاحظة مهمة:
        // - لو أنت فعلاً تريد "ضريبة تقديرية" في طلب الشراء، لازم نضيف عمود جديد في PurchaseRequest
        //   + نعدل DocumentTotalsService ليحسب صافي متوقع. لكن هذا خارج هذا التعديل حسب طلبك.
        // ================================================================

        public class SaveTaxJsonDto
        {
            public int PRId { get; set; }            // متغير: رقم طلب الشراء
            public decimal taxTotal { get; set; }    // متغير: قيمة الضريبة (غير مستخدمة في طلب الشراء حالياً)
        }

        [RequirePermission("PurchaseRequests.Edit")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveTaxJson([FromBody] SaveTaxJsonDto dto)
        {
            try
            {
                // =========================
                // 0) فحص المدخلات
                // =========================
                if (dto == null || dto.PRId <= 0)
                    return BadRequest(new { ok = false, message = "بيانات الطلب غير صحيحة." });

                // =========================
                // 1) تحميل طلب الشراء
                // =========================
                var pr = await _context.PurchaseRequests
                    .FirstOrDefaultAsync(x => x.PRId == dto.PRId);

                if (pr == null)
                    return NotFound(new { ok = false, message = "طلب الشراء غير موجود." });

                // =========================
                // 2) منع التعديل على طلب تم تحويله لفاتورة
                // =========================
                // في PR عندك القفل الأساسي هو IsConverted (وليس IsPosted)
                if (pr.IsConverted)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        message = "لا يمكن تعديل هذا الطلب لأنه تم تحويله إلى فاتورة شراء."
                    });
                }

                // =========================
                // 3) حفظ قيمة الضريبة في طلب الشراء (مثل فاتورة المبيعات)
                // =========================
                pr.TaxAmount = Math.Round(dto.taxTotal, 2);
                pr.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                // =========================
                // 4) إعادة حساب إجماليات طلب الشراء (TotalQtyRequested + ExpectedItemsTotal)
                // =========================
                await _docTotals.RecalcPurchaseRequestTotalsAsync(dto.PRId);

                // =========================
                // 5) قراءة الإجماليات بعد الحساب وإرجاعها للواجهة (مع الضريبة)
                // =========================
                var headerTotals = await _context.PurchaseRequests.AsNoTracking()
                    .Where(p => p.PRId == dto.PRId)
                    .Select(p => new
                    {
                        p.TotalQtyRequested,
                        p.ExpectedItemsTotal,
                        p.TaxAmount
                    })
                    .FirstAsync();

                decimal totalAfterDiscount = headerTotals.ExpectedItemsTotal; // في طلب الشراء الإجمالي بعد الخصم = المتوقع
                decimal totalAfterDiscountAndTax = totalAfterDiscount + headerTotals.TaxAmount;

                return Json(new
                {
                    ok = true,
                    totals = new
                    {
                        totalQty = headerTotals.TotalQtyRequested,
                        expectedTotal = headerTotals.ExpectedItemsTotal,
                        totalAfterDiscount = totalAfterDiscount,
                        taxAmount = headerTotals.TaxAmount,
                        totalAfterDiscountAndTax = totalAfterDiscountAndTax,
                        netTotal = totalAfterDiscountAndTax
                    }
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { ok = false, message = ex.Message });
            }
        }





















        // =========================================================
        // دالة مساعدة: تجهيز Totals للواجهة (Badges) — طلب الشراء
        // ✅ هدفها: تجهيز ملخص سريع من هيدر طلب الشراء لعرضه في الواجهة
        // - طلب الشراء لا يحتوي على (ItemsTotal/Discount/Tax/Net)
        // - لذلك نستخدم (TotalQtyRequested + ExpectedItemsTotal)
        // =========================================================
        private static object BuildTotalsForUi(PurchaseRequest? header)
        {
            // تعليق: لو الهيدر غير موجود (رقم طلب غير صحيح أو لسه جديد)
            // نرجّع قيم صفرية علشان الواجهة متكسرش
            if (header == null)
            {
                return new
                {
                    totalLines = 0,        // متغير: عدد السطور (غالبًا بيتجاب من دالة السطور)
                    totalItems = 0,        // متغير: عدد الأصناف المختلفة
                    totalQty = 0,          // متغير: إجمالي الكميات المطلوبة
                    expectedTotal = 0m     // متغير: إجمالي القيمة/التكلفة المتوقعة
                };
            }

            return new
            {
                totalQty = header.TotalQtyRequested,
                expectedTotal = header.ExpectedItemsTotal,
                taxAmount = header.TaxAmount,
                totalAfterDiscount = header.ExpectedItemsTotal,
                totalAfterDiscountAndTax = header.ExpectedItemsTotal + header.TaxAmount,
                netTotal = header.ExpectedItemsTotal + header.TaxAmount
            };
        }








        // =========================================================
        // دالة مساعدة: تحميل سطور طلب الشراء للواجهة (مع اسم الصنف)
        // ✅ هدفها: تجهيز بيانات كافية للعرض + الجروبنج في الـ JS
        // - مصدر السطور: PRLines (طلب الشراء)
        // - نرجع: رقم السطر + الصنف + الكمية المطلوبة + التكلفة المتوقعة + قيمة السطر المتوقعة
        // =========================================================
        private async Task<object[]> LoadInvoiceLinesForUiAsync(int PRId)
        {
            // تعليق: بنرجع بيانات كفاية للعرض والجروبنج في الواجهة
            var data = await (
                from l in _context.PRLines.AsNoTracking()          // ✅ سطور طلب الشراء
                join p in _context.Products.AsNoTracking()
                    on l.ProdId equals p.ProdId
                where l.PRId == PRId
                orderby p.ProdName, l.LineNo
                select new
                {
                    lineNo = l.LineNo,                             // متغير: رقم السطر
                    prodId = l.ProdId,                             // متغير: كود الصنف
                    prodName = p.ProdName,                         // متغير: اسم الصنف

                    qty = l.QtyRequested,                          // متغير: الكمية المطلوبة
                    expectedCost = l.ExpectedCost,                 // متغير: التكلفة/السعر المتوقع للوحدة

                    // متغير: قيمة السطر المتوقعة = الكمية * التكلفة المتوقعة
                    lineValue = ((decimal)l.QtyRequested * l.ExpectedCost)
                }
            ).ToArrayAsync();

            return data.Cast<object>().ToArray();
        }















        /// <summary>
        /// شاشة إنشاء طلب شراء جديد.
        /// سياسة الصلاحيات: صلاحية View (قائمة طلبات الشراء) خاصة بفتح الشاشات — من لديه View أو Create يفتح هذه الشاشة.
        /// </summary>
        // GET: PurchaseRequests/Create
        public async Task<IActionResult> Create(int? frame = null)
        {
            // صلاحية الشاشة: Create أو View (القائمة) — View مخصّصة لفتح الشاشات
            var canCreate = await _permissionService.HasPermissionAsync(PermissionCodes.Code("PurchaseRequests", "Create"));
            var canViewList = await _permissionService.HasPermissionAsync(PermissionCodes.Code("PurchaseRequests", "Index"));
            if (!canCreate && !canViewList)
                return RedirectToAction("AccessDenied", "Home");

            // ==============================
            // 1) تجهيز موديل "طلب شراء" جديد بالقيم الافتراضية
            // ==============================
            var defaultWarehouseId = await GetDefaultWarehouseIdAsync();

            var model = new PurchaseRequest
            {
                PRId = 0,                        // متغير: طلب جديد (لم يتم الحفظ بعد)
                PRDate = DateTime.Today,         // متغير: تاريخ الطلب الافتراضي = اليوم
                NeedByDate = null,               // متغير: تاريخ الاحتياج (المستخدم يحدده)
                WarehouseId = defaultWarehouseId > 0 ? defaultWarehouseId : 0,  // متغير: افتراضي = مخزن الدواء (مثل فاتورة المشتريات)
                CustomerId = 0,                  // متغير: المورد (المستخدم يختاره)

                // إجماليات الطلب (مصدرها سطور الطلب عبر DocumentTotalsService)
                TotalQtyRequested = 0,           // متغير: إجمالي الكمية المطلوبة
                ExpectedItemsTotal = 0m,         // متغير: إجمالي التكلفة/القيمة المتوقعة

                Status = "غير مرحلة",                // متغير: حالة الطلب الافتراضية
                IsConverted = false,             // متغير: الطلب لم يتحول لفاتورة شراء بعد

                RequestedBy = GetCurrentUserDisplayName(), // متغير: طالب الطلب (افتراضياً اليوزر الحالي)
                CreatedBy = GetCurrentUserDisplayName(),   // متغير: منشئ الطلب

                CreatedAt = DateTime.UtcNow      // متغير: وقت إنشاء السجل
            };

            // ==============================
            // 2) تجهيز القوائم المنسدلة والأوتوكومبليت
            // ==============================
            await PopulateDropDownsAsync(model.CustomerId, model.WarehouseId); // مورد + مخزن
            await LoadProductsForAutoCompleteAsync();                          // أصناف للأوتوكومبليت

            // ==============================
            // 3) تسجيل لوج "فتح شاشة طلب شراء جديد"
            // ==============================
            try
            {
                await _activityLogger.LogAsync(
                    UserActionType.View,
                    "PurchaseRequest",
                    null,
                    $"فتح شاشة إنشاء طلب شراء جديد بواسطة {GetCurrentUserDisplayName()}"
                );
            }
            catch
            {
                // تعليق: لا نعطل فتح الشاشة لو اللوج فشل
            }

            // ==============================
            // 4) تجهيز الأسهم حتى في الطلب الجديد (PRId = 0)
            // ==============================
            await FillPurchaseRequestNavAsync(model.PRId);

            // ==============================
            // 5) فتح شاشة Show بنفس الموديل (نظام شاشة موحدة للإنشاء/التعديل)
            // ==============================
            return View("Show", model);
        }










        /// <summary>
        /// استقبال بيانات إنشاء "طلب الشراء" من الفورم (حفظ الهيدر فقط).
        /// ملاحظة: طلب الشراء لا يرحّل حسابات ولا مخزون.
        /// </summary>
        [RequirePermission("PurchaseRequests.Create")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PurchaseRequest model)
        {
            // ==============================
            // 0) فحوصات أساسية للهيدر
            // ==============================

            // متغير: لازم اختيار مورد
            if (model.CustomerId <= 0)
                ModelState.AddModelError("CustomerId", "يجب اختيار المورد.");

            // متغير: لازم اختيار مخزن
            if (model.WarehouseId <= 0)
                ModelState.AddModelError("WarehouseId", "يجب اختيار المخزن.");

            // متغير: التحقق أن المورد موجود فعلاً
            if (model.CustomerId > 0)
            {
                bool customerExists = await _context.Customers
                    .AnyAsync(c => c.CustomerId == model.CustomerId);

                if (!customerExists)
                    ModelState.AddModelError("CustomerId", "المورد المختار غير موجود.");
            }

            // ==============================
            // 1) لو فيه أخطاء: نرجع Show مع تحميل القوائم
            // ==============================
            if (!ModelState.IsValid)
            {
                await PopulateDropDownsAsync(model.CustomerId, model.WarehouseId);
                await LoadProductsForAutoCompleteAsync();

                // تجهيز الأسهم (حتى لو فشل)
                await FillPurchaseRequestNavAsync(model.PRId);

                return View("Show", model);
            }

            // ==============================
            // 2) تجهيز قيم الحفظ الافتراضية (لو مش جاية من الفورم)
            // ==============================
            model.PRId = 0; // حماية: ده إنشاء جديد

            model.CreatedAt = DateTime.UtcNow;                 // متغير: وقت الإنشاء
            model.CreatedBy = GetCurrentUserDisplayName();     // متغير: منشئ الطلب

            // متغير: طالب الطلب (لو الواجهة ما بعتتهوش)
            if (string.IsNullOrWhiteSpace(model.RequestedBy))
                model.RequestedBy = GetCurrentUserDisplayName();

            // متغير: الحالة الافتراضية
            model.Status = string.IsNullOrWhiteSpace(model.Status) ? "غير مرحلة" : model.Status;

            // متغير: الطلب جديد ولم يتحول بعد
            model.IsConverted = false;

            // متغير: إجماليات تبدأ بصفر (هتتحسب بعد إضافة سطور)
            model.TotalQtyRequested = 0;
            model.ExpectedItemsTotal = 0m;

            // ==============================
            // 3) حفظ الهيدر
            // ==============================
            _context.PurchaseRequests.Add(model);
            await _context.SaveChangesAsync();

            // ==============================
            // 4) لوج إنشاء الطلب
            // ==============================
            try
            {
                await _activityLogger.LogAsync(
                    UserActionType.Create,
                    "PurchaseRequest",
                    model.PRId,
                    $"إنشاء طلب شراء جديد PRId={model.PRId} بواسطة {GetCurrentUserDisplayName()}"
                );
            }
            catch
            {
                // تعليق: لا نوقف الحفظ لو اللوج فشل
            }

            TempData["SuccessMessage"] = "تم إنشاء طلب الشراء بنجاح.";

            // ==============================
            // 5) نروح لشاشة Show للطلب الجديد
            // ==============================
            return RedirectToAction(nameof(Show), new { id = model.PRId, frame = 1 });
        }









        // =========================================================
        // Edit (GET) — فتح طلب شراء موجود للتعديل (لكن نعرضه في Show)
        // =========================================================
        /// <summary>
        /// شاشة تعديل طلب شراء موجود.
        /// ملاحظة: نحن نستخدم View "Show" كشاشة موحدة للعرض/التعديل.
        /// </summary>
        [RequirePermission("PurchaseRequests.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            // =========================
            // 1) تحميل طلب الشراء + السطور + المورد (مثل Show)
            // =========================
            var request = await _context.PurchaseRequests
                .Include(p => p.Customer)
                    .ThenInclude(c => c.Governorate)
                .Include(p => p.Customer)
                    .ThenInclude(c => c.District)
                .Include(p => p.Customer)
                    .ThenInclude(c => c.Area)
                .Include(p => p.Lines)
                    .ThenInclude(l => l.Product)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PRId == id);

            if (request == null)
                return NotFound();

            // =========================
            // 2) تجهيز المورد + المخزن للهيدر
            // =========================
            await PopulateDropDownsAsync(request.CustomerId, request.WarehouseId);

            // =========================
            // 3) تجهيز الأصناف للأوتوكومبليت (سطور الطلب)
            // =========================
            await LoadProductsForAutoCompleteAsync();

            // =========================
            // 3.1) التنقل + القفل (مثل Show)
            // =========================
            await FillPurchaseRequestNavAsync(request.PRId);
            ViewBag.IsLocked =
                request.IsConverted
                || string.Equals(request.Status, "Converted", StringComparison.OrdinalIgnoreCase)
                || string.Equals(request.Status, "Closed", StringComparison.OrdinalIgnoreCase);
            ViewBag.Frame = 1;

            // =========================
            // 4) تسجيل لوج فتح شاشة التعديل
            // =========================
            try
            {
                await _activityLogger.LogAsync(
                    UserActionType.View,
                    "PurchaseRequest",
                    request.PRId,
                    $"فتح تعديل طلب شراء PRId={request.PRId}"
                );
            }
            catch
            {
                // تعليق: لا نوقف فتح الصفحة لو اللوج فشل
            }

            // =========================
            // 5) عرض شاشة Show (الشاشة الموحدة)
            // =========================
            return View("Show", request);
        }










        // =========================================================
        // Edit (POST) — حفظ تعديل الهيدر في طلب الشراء
        // =========================================================
        /// <summary>
        /// استقبال بيانات تعديل طلب الشراء وحفظها.
        /// </summary>
        [RequirePermission("PurchaseRequests.Edit")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, PurchaseRequest model)
        {
            // =========================
            // 0) حماية: رقم الطلب في الرابط لازم يطابق الموديل
            // =========================
            if (id != model.PRId)
                return BadRequest();

            // =========================
            // 1) التأكد أن المورد فعلاً Supplier (حسب نظامك)
            // =========================
            bool supplierExists = await _context.Customers
                .AnyAsync(c => c.CustomerId == model.CustomerId
                            && c.PartyCategory == "Supplier"); // متغير: لازم يكون مورد

            if (!supplierExists)
                ModelState.AddModelError("CustomerId", "يجب اختيار مورد من قائمة الموردين.");

            // =========================
            // 2) لو في أخطاء Validation نرجع نفس الشاشة
            // =========================
            if (!ModelState.IsValid)
            {
                await PopulateDropDownsAsync(model.CustomerId, model.WarehouseId);
                await LoadProductsForAutoCompleteAsync();
                return View("Show", model);
            }

            // =========================
            // 3) تحميل الطلب الأصلي من قاعدة البيانات
            // =========================
            var request = await _context.PurchaseRequests
                .FirstOrDefaultAsync(p => p.PRId == id);

            if (request == null)
                return NotFound();

            // =========================
            // 4) منع التعديل لو الطلب اتحول لفاتورة (مقفول منطقياً)
            // =========================
            if (request.IsConverted)
            {
                await PopulateDropDownsAsync(request.CustomerId, request.WarehouseId);
                await LoadProductsForAutoCompleteAsync();

                ModelState.AddModelError("", "لا يمكن تعديل طلب الشراء لأنه تم تحويله بالفعل.");
                return View("Show", request);
            }

            // =========================
            // 5) تحديث حقول الهيدر الخاصة بطلب الشراء فقط
            // =========================
            request.PRDate = model.PRDate;                 // متغير: تاريخ الطلب
            request.NeedByDate = model.NeedByDate;         // متغير: مطلوب قبل تاريخ
            request.CustomerId = model.CustomerId;         // متغير: المورد
            request.WarehouseId = model.WarehouseId;       // متغير: المخزن
            request.RequestedBy = model.RequestedBy;       // متغير: تم الطلب بواسطة
            request.Notes = model.Notes;                   // متغير: ملاحظات

            // ملاحظة مهمة:
            // لا نسمح من هنا بتغيير:
            // - IsConverted / RefPIId / Status
            // لأن دول بيتغيروا من منطق التحويل/الخدمة وليس من شاشة تعديل الهيدر.

            // =========================
            // 6) بيانات آخر تعديل + حفظ
            // =========================
            request.UpdatedAt = DateTime.UtcNow; // متغير: وقت آخر تعديل

            await _context.SaveChangesAsync();

            // =========================
            // 7) إعادة حساب إجماليات طلب الشراء (من سطور PRLines)
            // =========================
            await _docTotals.RecalcPurchaseRequestTotalsAsync(request.PRId);

            // =========================
            // 8) LogActivity
            // =========================
            try
            {
                await _activityLogger.LogAsync(
                    UserActionType.Edit,
                    "PurchaseRequest",
                    request.PRId,
                    $"تعديل هيدر طلب شراء PRId={request.PRId}"
                );
            }
            catch
            {
                // تعليق: لا نوقف الحفظ لو اللوج فشل
            }

            TempData["SuccessMessage"] = "تم تعديل طلب الشراء بنجاح.";

            // =========================
            // 9) رجوع لشاشة Show بعد الحفظ
            // =========================
            return RedirectToAction(nameof(Show), new { id = request.PRId, frame = 1 });
        }

















        [RequirePermission("PurchaseRequests.Show")]
        [HttpGet]
        [ResponseCache(NoStore = true, Duration = 0)]
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
            // 1) محاولة تحميل طلب الشراء المطلوب
            // =========================================
            var request = await _context.PurchaseRequests
                .Include(p => p.Customer)
                    .ThenInclude(c => c.Governorate)
                .Include(p => p.Customer)
                    .ThenInclude(c => c.District)
                .Include(p => p.Customer)
                    .ThenInclude(c => c.Area)
                .Include(p => p.Lines)
                    .ThenInclude(l => l.Product)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PRId == id);

            // =========================================
            // 2) لو طلب الشراء غير موجود (ممسوح / رقم غلط)
            // =========================================
            if (request == null)
            {
                // -----------------------------------------
                // ✅ حالة خاصة: frag=body (تنقل/Fetch)
                // هنا ممنوع Redirect لأنه سيؤدي لصفحة فاضية/فلاش
                // الأفضل: نرجّع 404 برسالة واضحة، والـ JS يتعامل معها
                // -----------------------------------------
                if (isBodyOnly)
                {
                    return NotFound($"طلب الشراء رقم ({id}) غير موجود (قد يكون ممسوح).");
                }

                // -----------------------------------------
                // منطقك الحالي: فتح أقرب طلب بدل NotFound داخل iframe
                // -----------------------------------------

                // متغير: نحاول نلاقي "التالي" (أصغر رقم أكبر من id)
                int? nearestNext = await _context.PurchaseRequests
                    .AsNoTracking()
                    .Where(x => x.PRId > id)
                    .OrderBy(x => x.PRId)
                    .Select(x => (int?)x.PRId)
                    .FirstOrDefaultAsync();

                if (nearestNext.HasValue && nearestNext.Value > 0)
                {
                    TempData["Error"] = $"رقم طلب الشراء ({id}) غير موجود (قد يكون ممسوح). تم فتح الطلب التالي رقم ({nearestNext.Value}).";
                    return RedirectToAction(nameof(Show), new { id = nearestNext.Value, frag = (string?)null, frame = 1 });
                }

                // متغير: لو مفيش التالي… نجرب "السابق" (أكبر رقم أقل من id)
                int? nearestPrev = await _context.PurchaseRequests
                    .AsNoTracking()
                    .Where(x => x.PRId < id)
                    .OrderByDescending(x => x.PRId)
                    .Select(x => (int?)x.PRId)
                    .FirstOrDefaultAsync();

                if (nearestPrev.HasValue && nearestPrev.Value > 0)
                {
                    TempData["Error"] = $"رقم طلب الشراء ({id}) غير موجود (قد يكون ممسوح). تم فتح الطلب السابق رقم ({nearestPrev.Value}).";
                    return RedirectToAction(nameof(Show), new { id = nearestPrev.Value, frag = (string?)null, frame = 1 });
                }

                // لو مفيش أي طلبات أصلاً
                TempData["Error"] = "لا توجد طلبات شراء مسجلة حالياً.";
                return RedirectToAction(nameof(Create), new { frame = 1 });
            }

            // =========================================
            // 3) تجهيز القوائم + الأوتوكومبليت
            // ✅ نحمّل الموردين والأصناف دائماً (بما فيها عند frag=body) حتى تظهر أسماء الموردين وقائمة الأصناف بعد البحث/الأسهم
            // =========================================
            await PopulateDropDownsAsync(request.CustomerId, request.WarehouseId);
            await LoadProductsForAutoCompleteAsync();

            // =========================================
            // 3.1) متغير: هل الطلب مقفول
            // طلب الشراء يُقفل عادة عند التحويل (IsConverted) أو عند حالات مقفولة
            // =========================================
            ViewBag.IsLocked =
                request.IsConverted
                || string.Equals(request.Status, "Converted", StringComparison.OrdinalIgnoreCase)
                || string.Equals(request.Status, "Closed", StringComparison.OrdinalIgnoreCase);

            // متغير: علامة للـ View أننا داخل Frame (في العرض الكامل فقط)
            ViewBag.Frame = (!isBodyOnly) ? 1 : 0;

            // =========================================
            // 4) ✅ تجهيز التنقل بشكل موحّد
            // ملاحظة: بنحافظ على اسم الدالة كما هو عندك حتى لا نكسر الاستدعاءات
            // =========================================
            await FillPurchaseRequestNavAsync(request.PRId);
            await LoadPrintHeaderSettingsAsync();

            // =========================================
            // 5) عرض الـ View نفسه
            // - الـ View سيقرر ماذا يعرض بناءً على ViewBag.Fragment
            // =========================================
            return View("Show", request);
        }






        [HttpGet]
        [RequirePermission("PurchaseRequests.Show")]
        public async Task<IActionResult> ExportShowExcel(int id)
        {
            if (id <= 0)
                return BadRequest();

            var request = await _context.PurchaseRequests
                .Include(p => p.Customer)
                .Include(p => p.Warehouse)
                .Include(p => p.Lines)
                    .ThenInclude(l => l.Product)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PRId == id);

            if (request == null)
                return NotFound();

            var bytes = ShowDocumentExcelExport.PurchaseRequest(request);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ExcelExportNaming.ArabicTimestampedFileName($"طلب شراء {id}", ".xlsx"));
        }

        // =========================================================
        // دالة مساعدة: تجهيز بيانات التنقل (أول/سابق/التالي/آخر) لطلب الشراء
        // الهدف:
        // - تفعيل الأسهم في شاشة Show لطلب الشراء
        // - تشتغل حتى لو الطلب جديد (PRId = 0)
        // =========================================================
        private async Task FillPurchaseRequestNavAsync(int currentId)
        {
            // ==============================
            // 1) أول وآخر طلب شراء (Query واحد)
            // ==============================
            var minMax = await _context.PurchaseRequests
                .AsNoTracking()
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    FirstId = g.Min(x => x.PRId),
                    LastId = g.Max(x => x.PRId)
                })
                .FirstOrDefaultAsync();

            // ==============================
            // 2) السابقة/التالية
            // ملاحظة مهمة:
            // - لو currentId = 0 (طلب جديد) => السابقة = آخر طلب / التالية = أول طلب
            // ==============================
            int? prevId = null; // متغير: رقم طلب الشراء السابق
            int? nextId = null; // متغير: رقم طلب الشراء التالي

            if (currentId > 0)
            {
                // السابقة = أكبر رقم أقل من الحالي
                prevId = await _context.PurchaseRequests
                    .AsNoTracking()
                    .Where(x => x.PRId < currentId)
                    .OrderByDescending(x => x.PRId)
                    .Select(x => (int?)x.PRId)
                    .FirstOrDefaultAsync();

                // التالية = أصغر رقم أكبر من الحالي
                nextId = await _context.PurchaseRequests
                    .AsNoTracking()
                    .Where(x => x.PRId > currentId)
                    .OrderBy(x => x.PRId)
                    .Select(x => (int?)x.PRId)
                    .FirstOrDefaultAsync();
            }
            else
            {
                // ✅ طلب جديد: نخلي الأسهم شغالة كبحث سريع
                prevId = minMax?.LastId;   // السابق يأخذك لآخر طلب شراء
                nextId = minMax?.FirstId;  // التالي يأخذك لأول طلب شراء
            }

            // ==============================
            // 3) تعبئة ViewBag للـ View (بدون Null)
            // ==============================
            int firstId = minMax?.FirstId ?? 0; // متغير: أول طلب شراء
            int lastId = minMax?.LastId ?? 0;  // متغير: آخر طلب شراء

            ViewBag.NavFirstId = firstId;
            ViewBag.NavLastId = lastId;
            ViewBag.NavPrevId = prevId ?? 0;
            ViewBag.NavNextId = nextId ?? 0;
        }











        /// <summary>
        /// صفحة تأكيد الحذف لطلب شراء واحد (PurchaseRequest).
        /// </summary>
        [RequirePermission("PurchaseRequests.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            // متغير: الهيدر الخاص بطلب الشراء (قراءة فقط)
            var pr = await _context.PurchaseRequests
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.PRId == id);

            if (pr == null)
                return NotFound();

            return View(pr); // View: Views/PurchaseRequests/Delete.cshtml
        }






        /// <summary>
        /// تنفيذ الحذف الفعلي بعد التأكيد (PurchaseRequest).
        /// ✅ مناسب لطلب الشراء لأنه مستند إداري (لا مخزون ولا قيود).
        ///
        /// المنطق:
        /// 1) تحميل الطلب (Tracked)
        /// 2) تحميل السطور (PRLines)
        /// 3) شرط أمان: منع الحذف حسب حالة الطلب (Status) لو الطلب غير مفتوح للتعديل
        /// 4) Transaction
        /// 5) حذف السطور ثم حذف الهيدر
        /// 6) SaveChanges + Commit مرة واحدة
        /// </summary>
        [RequirePermission("PurchaseRequests.Delete")]
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            // =========================
            // 0) تحميل الهيدر (Tracked)
            // =========================
            var pr = await _context.PurchaseRequests
                .FirstOrDefaultAsync(x => x.PRId == id);

            if (pr == null)
                return NotFound();

            // =========================
            // 1) تحميل سطور طلب الشراء (PRLines)
            // =========================
            // ملاحظة: ده طلب شراء => السطور لازم تبقى PRLines وليس PILines
            var lines = await _context.PRLines
                .Where(l => l.PRId == id)
                .OrderBy(l => l.LineNo)
                .ToListAsync();

            // =========================
            // 2) شرط أمان: منع الحذف فقط إذا وُجدت فاتورة شراء مرتبطة بالطلب
            // =========================
            // يُسمح بحذف الطلب (حتى لو مغلق أو محوّل) طالما فاتورة الشراء المرتبطة به تم حذفها
            var hasLinkedInvoice = await _context.PurchaseInvoices.AnyAsync(pi => pi.RefPRId == id);
            if (hasLinkedInvoice)
            {
                TempData["ErrorMessage"] = "لا يمكن حذف طلب الشراء لأن هناك فاتورة شراء مرتبطة به. احذف فاتورة الشراء أولاً.";
                return RedirectToAction(nameof(Index));
            }

            // =========================
            // 3) Transaction (عملية واحدة)
            // =========================
            await using var tx = await _context.Database.BeginTransactionAsync();

            try
            {
                // =========================
                // 4) حذف السطور أولاً (لو الكاسكيد غير مضمون)
                // =========================
                if (lines.Count > 0)
                    _context.PRLines.RemoveRange(lines);

                // =========================
                // 5) حذف الهيدر
                // =========================
                _context.PurchaseRequests.Remove(pr);

                // =========================
                // 6) SaveChanges مرة واحدة + Commit
                // =========================
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                // =========================
                // 7) LogActivity (اختياري)
                // =========================
                try
                {
                    await _activityLogger.LogAsync(
                        UserActionType.Delete,
                        "PurchaseRequests",
                        id,
                        $"PRId={id} | DeleteHeader | Lines={lines.Count}"
                    );
                }
                catch
                {
                    // تجاهل أي خطأ في اللوج حتى لا يعطل الحذف
                }

                TempData["SuccessMessage"] = "تم حذف طلب الشراء بنجاح.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["ErrorMessage"] = $"تعذر حذف طلب الشراء رقم {id}: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }












        // =========================================================
        // BulkDelete: مسح مجموعة طلبات شراء محددة من شاشة القائمة
        // =========================================================
        [RequirePermission("PurchaseRequests.Delete")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["ErrorMessage"] = "لم يتم اختيار أي طلب للحذف.";
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
                TempData["ErrorMessage"] = "لم يتم التعرف على أرقام الطلبات المحددة.";
                return RedirectToAction(nameof(Index));
            }

            // نجيب الطلبات الموجودة فقط
            var existingIds = await _context.PurchaseRequests
                .Where(p => ids.Contains(p.PRId))
                .Select(p => p.PRId)
                .ToListAsync();

            if (!existingIds.Any())
            {
                TempData["ErrorMessage"] = "لم يتم العثور على الطلبات المحددة في قاعدة البيانات.";
                return RedirectToAction(nameof(Index));
            }

            int deletedCount = 0;     // متغير: عدد الطلبات التي تم حذفها
            int blockedCount = 0;     // متغير: عدد الطلبات الممنوع حذفها
            int failedCount = 0;      // متغير: عدد الطلبات التي فشل حذفها بسبب خطأ

            var blockedIds = new List<int>(); // متغير: أرقام الطلبات الممنوعة
            var failedIds = new List<int>();  // متغير: أرقام الطلبات التي فشلت

            // ✅ حذف كل طلب لوحده داخل Transaction مستقل
            // علشان لو طلب فشل/ممنوع ما يوقفش باقي العملية
            foreach (var id in existingIds)
            {
                var result = await TryDeletePurchaseRequestDeepAsync(id);

                if (result.Status == DeleteInvoiceStatus.Deleted)
                {
                    deletedCount++;
                }
                else if (result.Status == DeleteInvoiceStatus.BlockedByStatus)
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
            var summary = $"تم حذف: {deletedCount} | تم منع: {blockedCount} | فشل: {failedCount}";

            if (deletedCount > 0)
            {
                TempData["SuccessMessage"] = summary;
                if (blockedIds.Count > 0)
                    TempData["WarningMessage"] = $"طلبات ممنوع حذفها (لها فواتير شراء مرتبطة): {string.Join(", ", blockedIds)}";
                if (failedIds.Count > 0)
                    TempData["ErrorMessage"] = $"طلبات فشل حذفها بسبب خطأ: {string.Join(", ", failedIds)}";
            }
            else
            {
                TempData["ErrorMessage"] = $"لم يتم حذف أي طلب. {summary}";
                if (blockedIds.Count > 0)
                    TempData["WarningMessage"] = $"طلبات ممنوع حذفها (لها فواتير شراء مرتبطة): {string.Join(", ", blockedIds)}";
                if (failedIds.Count > 0)
                    TempData["ErrorMessage"] = $"{TempData["ErrorMessage"]} | طلبات فشل حذفها: {string.Join(", ", failedIds)}";
            }

            return RedirectToAction(nameof(Index));
        }










        // =========================================================
        // DeleteAll: مسح كل طلبات الشراء (حسب نفس نمط النظام الموحد)
        // =========================================================
        [RequirePermission("PurchaseRequests.Delete")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            // نجيب كل IDs فقط لتقليل الذاكرة
            var allIds = await _context.PurchaseRequests
                .Select(x => x.PRId)
                .ToListAsync();

            if (!allIds.Any())
            {
                TempData["ErrorMessage"] = "لا توجد طلبات شراء لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            int deletedCount = 0;  // متغير: عدد المحذوف
            int blockedCount = 0;   // متغير: عدد الممنوع
            int failedCount = 0;    // متغير: عدد الفاشل

            var blockedIds = new List<int>(); // متغير: أرقام الممنوع
            var failedIds = new List<int>();  // متغير: أرقام الفاشل

            foreach (var id in allIds)
            {
                var result = await TryDeletePurchaseRequestDeepAsync(id);

                if (result.Status == DeleteInvoiceStatus.Deleted)
                    deletedCount++;
                else if (result.Status == DeleteInvoiceStatus.BlockedByStatus)
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
                    TempData["WarningMessage"] = $"طلبات ممنوع حذفها (لها فواتير شراء مرتبطة): {string.Join(", ", blockedIds)}";
                if (failedIds.Count > 0)
                    TempData["ErrorMessage"] = $"طلبات فشل حذفها بسبب خطأ: {string.Join(", ", failedIds)}";
            }
            else
            {
                TempData["ErrorMessage"] = $"لم يتم حذف أي طلب. {summary}";
                if (blockedIds.Count > 0)
                    TempData["WarningMessage"] = $"طلبات ممنوع حذفها (لها فواتير شراء مرتبطة): {string.Join(", ", blockedIds)}";
                if (failedIds.Count > 0)
                    TempData["ErrorMessage"] = $"{TempData["ErrorMessage"]} | طلبات فشل حذفها: {string.Join(", ", failedIds)}";
            }

            return RedirectToAction(nameof(Index));
        }
















        // ============================================================================
        // ✅ Enum + Result لتحديد نتيجة حذف طلب الشراء (PurchaseRequest)
        // - الهدف: BulkDelete/DeleteAll يرجّعوا ملخص محترم (اتحذف/اترفض/فشل)
        // ============================================================================
        private enum DeleteInvoiceStatus
        {
            Deleted = 1,           // تم حذف الطلب
            BlockedByStatus = 2,   // ممنوع الحذف بسبب حالة الطلب (معتمد/مغلق/منفذ/مرحّل)
            Failed = 3             // فشل بسبب خطأ/استثناء
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

        // ============================================================================
        // ✅ دالة مساعدة: تحاول حذف طلب شراء واحد "حذف عميق" مثل زر Delete
        // - ترجع حالة: Deleted / BlockedByStatus / Failed
        // - كل طلب له Transaction مستقل (حتى لا نفشل العملية كلها)
        // ============================================================================
        private async Task<DeleteInvoiceResult> TryDeletePurchaseRequestDeepAsync(int id)
        {
            // =========================
            // 0) تحميل الطلب (Tracked)
            // =========================
            var pr = await _context.PurchaseRequests
                .FirstOrDefaultAsync(x => x.PRId == id);

            if (pr == null)
                return new DeleteInvoiceResult(DeleteInvoiceStatus.Failed, "الطلب غير موجود.");

            // =========================
            // 1) شرط أمان: منع الحذف فقط إذا وُجدت فاتورة شراء مرتبطة بالطلب
            // =========================
            // يُسمح بحذف الطلب (حتى لو مغلق أو محوّل) طالما فاتورة الشراء المرتبطة به تم حذفها
            var hasLinkedInvoice = await _context.PurchaseInvoices.AnyAsync(pi => pi.RefPRId == id);
            if (hasLinkedInvoice)
            {
                return new DeleteInvoiceResult(DeleteInvoiceStatus.BlockedByStatus,
                    "ممنوع الحذف: توجد فاتورة شراء مرتبطة بالطلب. احذف فاتورة الشراء أولاً.");
            }

            // =========================
            // 2) تحميل سطور الطلب
            // =========================
            var lines = await _context.PRLines
                .Where(l => l.PRId == id)
                .OrderBy(l => l.LineNo)
                .ToListAsync();

            // =========================
            // 3) Transaction لكل طلب
            // =========================
            await using var tx = await _context.Database.BeginTransactionAsync();

            try
            {
                // =========================
                // 4) حذف السطور أولاً
                // =========================
                if (lines.Count > 0)
                    _context.PRLines.RemoveRange(lines);

                // =========================
                // 5) حذف الهيدر
                // =========================
                _context.PurchaseRequests.Remove(pr);

                // =========================
                // 6) SaveChanges + Commit
                // =========================
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                // =========================
                // 7) LogActivity (اختياري)
                // =========================
                try
                {
                    await _activityLogger.LogAsync(
                        UserActionType.Delete,
                        "PurchaseRequests",
                        id,
                        $"PRId={id} | Bulk/DeleteAll | Lines={lines.Count}"
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











        // =========================================================
        // DTO: بيانات حفظ هيدر طلب الشراء (جاية من AJAX)
        // الهدف:
        // - إنشاء طلب شراء جديد لو PRId = 0
        // - أو تعديل هيدر طلب شراء موجود
        // - مع منع التعديل لو الطلب تم تحويله لفاتورة شراء (IsConverted = true)
        // =========================================================
        public class PurchaseRequestHeaderDto
        {
            public int PRId { get; set; }           // متغير: رقم طلب الشراء (0 = جديد)
            public int CustomerId { get; set; }     // متغير: كود المورد
            public int WarehouseId { get; set; }    // متغير: كود المخزن
            // ملاحظة: لا يوجد RefPRId في موديل PurchaseRequest الحالي
        }

      
        
        
        
        
        
        
        // =========================================================
        // SaveHeader — حفظ هيدر طلب الشراء (Create / Update)
        // دورها في طلب الشراء:
        // - تثبيت (المورد + المخزن + تاريخ الطلب) قبل إضافة السطور
        // - إنشاء رقم طلب شراء رسمي PRId لو كان جديد
        // - تعديل الهيدر بسرعة عبر AJAX بدون Reload
        // =========================================================
        [RequirePermission("PurchaseRequests.Edit")]
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> SaveHeader([FromBody] PurchaseRequestHeaderDto dto)
        {
            // =========================
            // (0) فحص البيانات القادمة من الواجهة
            // =========================
            if (dto == null)
                return BadRequest("حدث خطأ فى البيانات المرسلة.");

            if (dto.CustomerId <= 0)
                return BadRequest("يجب اختيار المورد قبل حفظ طلب الشراء.");

            if (dto.WarehouseId <= 0)
                return BadRequest("يجب اختيار المخزن قبل حفظ طلب الشراء.");

            var now = DateTime.Now;                         // متغير: وقت التنفيذ الحالي
            var userName = GetCurrentUserDisplayName();     // متغير: اسم المستخدم الحالي

            // =========================
            // (1) إنشاء طلب شراء جديد
            // =========================
            if (dto.PRId == 0)
            {
                var pr = new PurchaseRequest
                {
                    PRDate = now.Date,              // متغير: تاريخ طلب الشراء
                    CustomerId = dto.CustomerId,    // متغير: المورد
                    WarehouseId = dto.WarehouseId,  // متغير: المخزن

                    Status = "غير محول",            // متغير: حالة طلب الشراء (افتراضي)
                    IsConverted = false,            // متغير: لم يتم تحويله بعد لفاتورة شراء

                    RequestedBy = userName,         // متغير: طالب الطلب (نفس المنشئ عند الحفظ من الواجهة)
                    CreatedAt = now,                // متغير: وقت الإنشاء
                    CreatedBy = userName,           // متغير: اسم المنشئ
                    UpdatedAt = now                 // متغير: آخر تعديل (نفس وقت الإنشاء)
                };

                _context.PurchaseRequests.Add(pr);
                await _context.SaveChangesAsync();

                var reqDate = pr.PRDate.ToString("yyyy/MM/dd");
                var reqTime = pr.CreatedAt.ToString("HH:mm");
                var reqNum = pr.PRId.ToString();
                // ✅ نفس شكل JSON القديم + invoiceNumber/Date/Time لأن الـ JS في الـ View يتوقعهما
                return Json(new
                {
                    success = true,
                    PRId = pr.PRId,
                    requestNumber = reqNum,
                    requestDate = reqDate,
                    requestTime = reqTime,
                    invoiceNumber = reqNum,
                    invoiceDate = reqDate,
                    invoiceTime = reqTime,
                    status = pr.Status,
                    isConverted = pr.IsConverted,
                    createdBy = pr.CreatedBy
                });
            }

            // =========================
            // (2) تعديل طلب شراء موجود
            // =========================
            var existing = await _context.PurchaseRequests
                .FirstOrDefaultAsync(p => p.PRId == dto.PRId);

            if (existing == null)
                return NotFound("لم يتم العثور على طلب الشراء المطلوب.");

            // =========================
            // (2.1) منع التعديل لو تم تحويله لفاتورة شراء
            // =========================
            if (existing.IsConverted)
                return BadRequest("لا يمكن تعديل طلب شراء تم تحويله بالفعل إلى فاتورة شراء.");

            // =========================
            // (2.2) تحديث الحقول المسموح بها في الهيدر
            // =========================
            existing.CustomerId = dto.CustomerId;     // متغير: تحديث المورد
            existing.WarehouseId = dto.WarehouseId;   // متغير: تحديث المخزن
            existing.UpdatedAt = now;                 // متغير: وقت آخر تعديل
            // ملاحظة: لا يوجد UpdatedBy في PurchaseRequest.cs الحالي

            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                PRId = existing.PRId,
                requestNumber = existing.PRId.ToString(),
                requestDate = existing.PRDate.ToString("yyyy/MM/dd"),
                requestTime = existing.CreatedAt.ToString("HH:mm"),
                status = existing.Status,
                isConverted = existing.IsConverted,
                createdBy = existing.CreatedBy
            });
        }








        // =========================================================
        // دالة مساعدة: جلب اسم المستخدم الحالي (للإنشاء/التعديل/اللوج)
        // الاستخدام في طلب الشراء:
        // - CreatedBy = GetCurrentUserDisplayName()
        // - في اللوج: "تم تعديل الطلب بواسطة ..." إلخ
        // =========================================================
        private string GetCurrentUserDisplayName()
        {
            // ✅ لو فيه يوزر عامل Login
            if (User?.Identity?.IsAuthenticated == true)
            {
                // (1) محاولة 1: DisplayName (الاسم الظاهر داخل النظام)
                var displayName = User.FindFirst("DisplayName")?.Value; // متغير: الاسم المعروض
                if (!string.IsNullOrWhiteSpace(displayName))
                    return displayName.Trim();

                // (2) محاولة 2: ClaimTypes.Name (أحيانًا بيحتوي الاسم)
                var claimName = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value; // متغير: اسم من الـ Claims
                if (!string.IsNullOrWhiteSpace(claimName))
                    return claimName.Trim();

                // (3) محاولة 3: User.Identity.Name (اسم الدخول)
                var loginName = User.Identity?.Name; // متغير: اسم تسجيل الدخول
                if (!string.IsNullOrWhiteSpace(loginName))
                    return loginName.Trim();
            }

            // ✅ حالات استثنائية: تشغيل سيستم/Seed/بدون Login
            return "System";
        }

        private async Task<IQueryable<PurchaseRequest>> ApplyOperationalListVisibilityAsync(IQueryable<PurchaseRequest> query)
        {
            if (await _listVisibilityService.CanViewAllOperationalListsAsync())
                return query;

            var creatorNames = await _listVisibilityService.GetCurrentUserCreatorNamesAsync();
            if (creatorNames.Count == 0)
                return query.Where(_ => false);

            return query.Where(pr => pr.CreatedBy != null && creatorNames.Contains(pr.CreatedBy));
        }












        // =========================================================
        // دالة مساعدة: جلب رقم المخزن الافتراضي لطلب الشراء
        // الهدف في طلب الشراء:
        // - عند فتح شاشة Create لطلب شراء جديد: نملأ WarehouseId تلقائيًا
        // المنطق:
        // 1) نحاول نلاقي مخزن اسمه "الدواء" (المخزن الرئيسي) أو يحتوي الكلمة.
        // 2) لو مش موجود، ناخد أول مخزن في الجدول.
        // 3) لو مفيش مخازن خالص → ترجع 0 (وسيتم منع الحفظ لاحقًا).
        // =========================================================
        private async Task<int> GetDefaultWarehouseIdAsync()
        {
            // =========================
            // 1) محاولة إيجاد المخزن الرئيسي بالاسم
            // =========================
            // متغير: رقم المخزن الافتراضي
            var id = await _context.Warehouses
                .AsNoTracking()
                .Where(w =>
                    w.WarehouseName != null &&
                    (
                        w.WarehouseName.Trim() == "الدواء" ||
                        w.WarehouseName.Contains("الدواء")
                    )
                )
                .OrderBy(w => w.WarehouseId)     // تعليق: لو فيه أكثر من مخزن مطابق نأخذ الأقدم/الأصغر
                .Select(w => w.WarehouseId)
                .FirstOrDefaultAsync();          // لو مش لاقي → 0

            if (id > 0)
                return id;

            // =========================
            // 2) لو مفيش "الدواء" → نجيب أول مخزن في الجدول
            // =========================
            id = await _context.Warehouses
                .AsNoTracking()
                .OrderBy(w => w.WarehouseId)     // تعليق: ثابت علشان كل مرة يرجّع نفس المخزن
                .Select(w => w.WarehouseId)
                .FirstOrDefaultAsync();          // لو الجدول فاضي → 0

            // =========================
            // 3) لو 0 يبقى مفيش مخازن
            // =========================
            return id;
        }







        /// <summary>
        /// تصدير قائمة "طلبات الشراء" بعد تطبيق نفس فلاتر Index إلى ملف CSV (يفتح في Excel).
        /// ملاحظة: Excel يفتح CSV عادي، لكن لازم UTF8 BOM علشان العربي يظهر صح.
        /// </summary>
        [RequirePermission("PurchaseRequests.Index")]
        [HttpGet]
        public async Task<IActionResult> Export(
            string? format,
            string? search,
            string? searchBy,
            string? searchMode,
            string? sort,
            string? dir,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? dateField = "PRDate",
            int? codeFrom = null,
            int? codeTo = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_date = null,
            string? filterCol_needby = null,
            string? filterCol_customer = null,
            string? filterCol_warehouse = null,
            string? filterCol_status = null,
            string? filterCol_total = null,
            string? filterCol_created = null
        )
        {
            format = string.IsNullOrWhiteSpace(format) ? "excel" : format.ToLowerInvariant();
            searchBy ??= "all";
            sort ??= "date";
            dir ??= "desc";
            dateField ??= "PRDate";
            searchMode = NormalizeSearchMode(searchMode);

            int? finalFrom = fromCode ?? codeFrom;
            int? finalTo = toCode ?? codeTo;

            bool sortDesc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            IQueryable<PurchaseRequest> query = _context.PurchaseRequests.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
            {
                if (string.Equals(searchBy, "all", StringComparison.OrdinalIgnoreCase))
                    query = query.Include(pr => pr.Customer).Include(pr => pr.Warehouse);
                else if (string.Equals(searchBy, "customer", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(searchBy, "vendor", StringComparison.OrdinalIgnoreCase))
                    query = query.Include(pr => pr.Customer);
                else if (string.Equals(searchBy, "warehouse", StringComparison.OrdinalIgnoreCase))
                    query = query.Include(pr => pr.Warehouse);
            }
            query = await ApplyOperationalListVisibilityAsync(query);

            query = ApplyFilters(query, search, searchBy, searchMode, finalFrom, finalTo, useDateRange, fromDate, toDate, dateField);

            // فلاتر الأعمدة (نفس Index)
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null).Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) query = query.Where(p => ids.Contains(p.PRId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_customer))
            {
                var ids = filterCol_customer.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null).Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) query = query.Where(p => ids.Contains(p.CustomerId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_warehouse))
            {
                var ids = filterCol_warehouse.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null).Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) query = query.Where(p => ids.Contains(p.WarehouseId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_status))
            {
                var vals = filterCol_status.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) query = query.Where(p => p.Status != null && vals.Contains(p.Status));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_total))
            {
                var vals = filterCol_total.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null).Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0) query = query.Where(p => vals.Contains(p.ExpectedItemsTotal));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_date))
            {
                var parts = filterCol_date.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length >= 7).ToList();
                var dateFilters = new List<(int Year, int Month)>();
                foreach (var p in parts)
                {
                    if (p.Length >= 7 && int.TryParse(p.Substring(0, 4), out var y) && int.TryParse(p.Substring(5, 2), out var m) && m >= 1 && m <= 12)
                        dateFilters.Add((y, m));
                }
                if (dateFilters.Count > 0) query = query.Where(p => dateFilters.Any(df => p.PRDate.Year == df.Year && p.PRDate.Month == df.Month));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_needby))
            {
                var parts = filterCol_needby.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length >= 7).ToList();
                var dateFilters = new List<(int Year, int Month)>();
                foreach (var p in parts)
                {
                    if (p.Length >= 7 && int.TryParse(p.Substring(0, 4), out var y) && int.TryParse(p.Substring(5, 2), out var m) && m >= 1 && m <= 12)
                        dateFilters.Add((y, m));
                }
                if (dateFilters.Count > 0) query = query.Where(p => p.NeedByDate.HasValue && dateFilters.Any(df => p.NeedByDate!.Value.Year == df.Year && p.NeedByDate.Value.Month == df.Month));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_created))
            {
                var parts = filterCol_created.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length >= 7).ToList();
                var dateFilters = new List<(int Year, int Month)>();
                foreach (var p in parts)
                {
                    if (p.Length >= 7 && int.TryParse(p.Substring(0, 4), out var y) && int.TryParse(p.Substring(5, 2), out var m) && m >= 1 && m <= 12)
                        dateFilters.Add((y, m));
                }
                if (dateFilters.Count > 0) query = query.Where(p => dateFilters.Any(df => p.CreatedAt.Year == df.Year && p.CreatedAt.Month == df.Month));
            }

            query = ApplySort(query, sort, sortDesc);

            var list = await query.ToListAsync();

            // =========================
            // 4) بناء CSV مناسب لـ Excel
            // =========================
            // ✅ Excel أحيانًا يحتاج BOM علشان العربي يظهر صح
            var utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

            var sb = new StringBuilder();

            // ✅ سطر مهم جدًا: يخلي Excel يفهم الفاصل (خصوصًا لو إعدادات الجهاز مختلفة)
            sb.AppendLine("sep=,");

            sb.AppendLine("رقم الطلب,تاريخ الطلب,كود العميل,كود المخزن,إجمالي الكمية المطلوبة,إجمالي التكلفة المتوقعة,الحالة,تم التحويل إلى فاتورة؟,تاريخ الإنشاء,آخر تعديل,أنشأها");

            const string sep = ",";

            foreach (var pr in list)
            {
                // متغيرات نصية (تنضيف بسيط علشان CSV ما يتكسرش)
                string status = (pr.Status ?? string.Empty).Replace(",", " ").Replace("\r", " ").Replace("\n", " ");
                string createdBy = (pr.CreatedBy ?? string.Empty).Replace(",", " ").Replace("\r", " ").Replace("\n", " ");

                // متغير: التاريخ كنص
                string prDateText = pr.PRDate.ToString("yyyy-MM-dd");

                // متغير: وقت الإنشاء/التعديل
                string createdAtText = pr.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                string updatedAtText = pr.UpdatedAt.HasValue ? pr.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm") : "";

                // ✅ بناء السطر (بدون string.Join لتفادي تخصيصات زيادة — بس برضه الاتنين تمام)
                sb.Append(pr.PRId).Append(sep)
                  .Append(prDateText).Append(sep)
                  .Append(pr.CustomerId).Append(sep)
                  .Append(pr.WarehouseId).Append(sep)
                  .Append(pr.TotalQtyRequested.ToString("0")).Append(sep)          // إجمالي الكمية المطلوبة
                  .Append(pr.ExpectedItemsTotal.ToString("0.00")).Append(sep)      // إجمالي التكلفة المتوقعة
                  .Append(status).Append(sep)
                  .Append(pr.IsConverted ? "نعم" : "لا").Append(sep)
                  .Append(createdAtText).Append(sep)
                  .Append(updatedAtText).Append(sep)
                  .Append(createdBy)
                  .AppendLine();
            }

            // =========================
            // 5) إخراج الملف
            // =========================
            var bytes = utf8Bom.GetBytes(sb.ToString());

            // اسم الملف
            var fileName = ExcelExportNaming.ArabicTimestampedFileName("طلبات الشراء", ".csv");

            // نوع الملف (يفتح في Excel)
            const string contentType = "application/vnd.ms-excel";

            return File(bytes, contentType, fileName);
        }











        private static string NormalizeSearchMode(string? mode)
        {
            var m = (mode ?? "").Trim().ToLowerInvariant();
            if (m == "startswith" || m == "starts" || m == "start" || m == "يبدأ") return "startswith";
            if (m == "endswith" || m == "ends" || m == "end" || m == "ينتهي") return "endswith";
            return "contains";
        }

        private static bool MatchSearchText(string? hay, string needle, string searchMode)
        {
            if (string.IsNullOrEmpty(needle)) return true;
            hay ??= "";
            if (searchMode == "startswith")
                return hay.StartsWith(needle, StringComparison.OrdinalIgnoreCase);
            if (searchMode == "endswith")
                return hay.EndsWith(needle, StringComparison.OrdinalIgnoreCase);
            return hay.Contains(needle, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// دالة فلترة موحدة لطلبات الشراء:
        /// - نص البحث (حسب searchBy + searchMode: يحتوي / يبدأ / ينتهي)
        /// - من/إلى كود (PRId)
        /// - فلتر التاريخ (PRDate أو CreatedAt أو UpdatedAt)
        /// </summary>
        private static IQueryable<PurchaseRequest> ApplyFilters(
            IQueryable<PurchaseRequest> query,
            string? search,            // متغير: نص البحث
            string? searchBy,          // متغير: نوع البحث (id / vendor / customer / warehouse / date / status / all)
            string? searchMode,
            int? fromCode,             // متغير: من رقم طلب
            int? toCode,               // متغير: إلى رقم طلب
            bool useDateRange,         // متغير: تفعيل فلتر التاريخ؟
            DateTime? fromDate,        // متغير: من تاريخ
            DateTime? toDate,          // متغير: إلى تاريخ
            string dateField           // متغير: اسم حقل التاريخ المستخدم (PRDate / CreatedAt / UpdatedAt)
        )
        {
            // =========================
            // 0) قيم افتراضية تناسب طلب الشراء
            // =========================
            searchBy ??= "all";
            dateField = string.IsNullOrWhiteSpace(dateField) ? "PRDate" : dateField;
            searchMode = NormalizeSearchMode(searchMode);

            // =========================
            // 1) فلتر نص البحث
            // =========================
            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();

                switch (searchBy.ToLower())
                {
                    case "all":
                        query = query.Where(p =>
                            MatchSearchText(p.PRId.ToString(), search, searchMode) ||
                            (p.Customer != null && p.Customer.CustomerName != null && MatchSearchText(p.Customer.CustomerName, search, searchMode)) ||
                            (p.Warehouse != null && p.Warehouse.WarehouseName != null && MatchSearchText(p.Warehouse.WarehouseName, search, searchMode)) ||
                            (p.Status != null && MatchSearchText(p.Status, search, searchMode)) ||
                            MatchSearchText(p.ExpectedItemsTotal.ToString(System.Globalization.CultureInfo.InvariantCulture), search, searchMode) ||
                            MatchSearchText(p.PRDate.ToString("yyyy-MM-dd"), search, searchMode) ||
                            (p.NeedByDate.HasValue && MatchSearchText(p.NeedByDate.Value.ToString("yyyy-MM-dd"), search, searchMode)) ||
                            MatchSearchText(p.CreatedAt.ToString("yyyy-MM-dd"), search, searchMode) ||
                            MatchSearchText(p.CustomerId.ToString(), search, searchMode) ||
                            MatchSearchText(p.WarehouseId.ToString(), search, searchMode));
                        break;

                    case "id":
                        if (int.TryParse(search, out var idVal) && searchMode == "contains" && search == idVal.ToString())
                            query = query.Where(p => p.PRId == idVal);
                        else
                            query = query.Where(p => MatchSearchText(p.PRId.ToString(), search, searchMode));
                        break;

                    case "vendor":
                    case "customer":
                        if (int.TryParse(search, out var custId) && searchMode == "contains" && search == custId.ToString())
                            query = query.Where(p => p.CustomerId == custId);
                        else
                            query = query.Where(p =>
                                MatchSearchText(p.CustomerId.ToString(), search, searchMode) ||
                                (p.Customer != null && p.Customer.CustomerName != null && MatchSearchText(p.Customer.CustomerName, search, searchMode)));
                        break;

                    case "warehouse":
                        if (int.TryParse(search, out var whId) && searchMode == "contains" && search == whId.ToString())
                            query = query.Where(p => p.WarehouseId == whId);
                        else
                            query = query.Where(p =>
                                MatchSearchText(p.WarehouseId.ToString(), search, searchMode) ||
                                (p.Warehouse != null && p.Warehouse.WarehouseName != null && MatchSearchText(p.Warehouse.WarehouseName, search, searchMode)));
                        break;

                    case "date":
                        if (DateTime.TryParse(search, out var dateVal))
                        {
                            var d = dateVal.Date;
                            query = query.Where(p => p.PRDate.Date == d);
                        }
                        break;

                    case "status":
                        query = query.Where(p => p.Status != null && MatchSearchText(p.Status, search, searchMode));
                        break;

                    default:
                        query = query.Where(p =>
                            MatchSearchText(p.PRId.ToString(), search, searchMode) ||
                            MatchSearchText(p.CustomerId.ToString(), search, searchMode) ||
                            MatchSearchText(p.WarehouseId.ToString(), search, searchMode) ||
                            (p.Status != null && MatchSearchText(p.Status, search, searchMode)));
                        break;
                }
            }

            // =========================
            // 2) فلتر من رقم / إلى رقم (PRId)
            // =========================
            if (fromCode.HasValue)
                query = query.Where(p => p.PRId >= fromCode.Value);

            if (toCode.HasValue)
                query = query.Where(p => p.PRId <= toCode.Value);

            // =========================
            // 3) فلتر التاريخ/الوقت (PRDate أو CreatedAt أو UpdatedAt)
            // =========================
            if (useDateRange && (fromDate.HasValue || toDate.HasValue))
            {
                bool useCreated = string.Equals(dateField, "CreatedAt", StringComparison.OrdinalIgnoreCase); // متغير: هل نفلتر بتاريخ الإنشاء؟
                bool useUpdated = string.Equals(dateField, "UpdatedAt", StringComparison.OrdinalIgnoreCase); // متغير: هل نفلتر بتاريخ آخر تعديل؟

                if (fromDate.HasValue)
                {
                    if (useCreated)
                        query = query.Where(p => p.CreatedAt >= fromDate.Value);
                    else if (useUpdated)
                        query = query.Where(p => p.UpdatedAt.HasValue && p.UpdatedAt.Value >= fromDate.Value);
                    else
                        query = query.Where(p => p.PRDate >= fromDate.Value);
                }

                if (toDate.HasValue)
                {
                    if (useCreated)
                        query = query.Where(p => p.CreatedAt <= toDate.Value);
                    else if (useUpdated)
                        query = query.Where(p => p.UpdatedAt.HasValue && p.UpdatedAt.Value <= toDate.Value);
                    else
                        query = query.Where(p => p.PRDate <= toDate.Value);
                }
            }

            return query;
        }








        /// <summary>
        /// دالة الترتيب الموحدة لقائمة "طلبات الشراء" بحسب اسم العمود القادم من الواجهة.
        /// ✅ تم تعديلها لتناسب PurchaseRequest:
        /// - PRDate بدل PIDate
        /// - IsConverted بدل IsPosted
        /// - حذف أي أعمدة غير موجودة (NetTotal / ItemsTotal / TaxTotal / DiscountTotal)
        /// </summary>
        private static IQueryable<PurchaseRequest> ApplySort(
            IQueryable<PurchaseRequest> query,
            string? sort,
            bool desc
        )
        {
            // متغير: عمود الترتيب الافتراضي
            sort = (sort ?? "prdate").ToLowerInvariant();

            switch (sort)
            {
                case "id":
                    query = desc
                        ? query.OrderByDescending(p => p.PRId)
                        : query.OrderBy(p => p.PRId);
                    break;

                case "date":
                case "prdate":
                    query = desc
                        ? query.OrderByDescending(p => p.PRDate).ThenByDescending(p => p.PRId)
                        : query.OrderBy(p => p.PRDate).ThenBy(p => p.PRId);
                    break;

                case "vendor":
                case "customer":
                    query = desc
                        ? query.OrderByDescending(p => p.CustomerId).ThenByDescending(p => p.PRId)
                        : query.OrderBy(p => p.CustomerId).ThenBy(p => p.PRId);
                    break;

                case "warehouse":
                    query = desc
                        ? query.OrderByDescending(p => p.WarehouseId).ThenByDescending(p => p.PRId)
                        : query.OrderBy(p => p.WarehouseId).ThenBy(p => p.PRId);
                    break;

                case "status":
                    query = desc
                        ? query.OrderByDescending(p => p.Status).ThenByDescending(p => p.PRId)
                        : query.OrderBy(p => p.Status).ThenBy(p => p.PRId);
                    break;

                case "total":
                case "expectedtotal":
                case "expecteditemstotal":
                    query = desc
                        ? query.OrderByDescending(p => p.ExpectedItemsTotal).ThenByDescending(p => p.PRId)
                        : query.OrderBy(p => p.ExpectedItemsTotal).ThenBy(p => p.PRId);
                    break;

                // ✅ بدل posted (طلبات الشراء عندك فيها IsConverted)
                case "posted":
                case "converted":
                    query = desc
                        ? query.OrderByDescending(p => p.IsConverted).ThenByDescending(p => p.PRId)
                        : query.OrderBy(p => p.IsConverted).ThenBy(p => p.PRId);
                    break;

                case "created":
                case "createdat":
                    query = desc
                        ? query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.PRId)
                        : query.OrderBy(p => p.CreatedAt).ThenBy(p => p.PRId);
                    break;

                case "needby":
                    query = desc
                        ? query.OrderByDescending(p => p.NeedByDate ?? DateTime.MinValue).ThenByDescending(p => p.PRId)
                        : query.OrderBy(p => p.NeedByDate ?? DateTime.MaxValue).ThenBy(p => p.PRId);
                    break;

                case "updatedat":
                    query = desc
                        ? query.OrderByDescending(p => p.UpdatedAt).ThenByDescending(p => p.PRId)
                        : query.OrderBy(p => p.UpdatedAt).ThenBy(p => p.PRId);
                    break;

                // ✅ أعمدة موجودة في PurchaseRequest عندك
                case "qty":
                case "totalqty":
                    query = desc
                        ? query.OrderByDescending(p => p.TotalQtyRequested).ThenByDescending(p => p.PRId)
                        : query.OrderBy(p => p.TotalQtyRequested).ThenBy(p => p.PRId);
                    break;


                default:
                    // الترتيب الافتراضي: بتاريخ طلب الشراء ثم رقم الطلب
                    query = desc
                        ? query.OrderByDescending(p => p.PRDate).ThenByDescending(p => p.PRId)
                        : query.OrderBy(p => p.PRDate).ThenBy(p => p.PRId);
                    break;
            }

            return query;
        }









        /// <summary>
        /// دالة مساعدة: تحويل نص إلى رقم (int?) بأمان لقائمة "طلبات الشراء".
        /// دورها:
        /// - تُستخدم لقراءة قيم الفلاتر من QueryString مثل: codeFrom / codeTo / customerId / warehouseId
        /// - ترجع null لو القيمة فاضية أو التحويل فشل (بدل ما ترمي Exception)
        /// </summary>
        private static int? TryParseNullableInt(string? value)
        {
            // متغير: لو القيمة جاية null أو فاضية → مفيش فلتر
            if (string.IsNullOrWhiteSpace(value))
                return null;

            // متغير: تنظيف النص من المسافات
            value = value.Trim();

            // متغير: نتيجة التحويل
            if (int.TryParse(value, out var number))
                return number;

            // لو فشل التحويل → نرجع null
            return null;
        }












        // ================================================================
        // إعادة حساب إجماليات طلب محدد
        // ================================================================
        [RequirePermission("PurchaseRequests.Edit")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecalcTotals(int id)
        {
            try
            {
                var pr = await _context.PurchaseRequests
                    .FirstOrDefaultAsync(x => x.PRId == id);

                if (pr == null)
                    return NotFound(new { ok = false, message = "طلب الشراء غير موجود." });

                await _docTotals.RecalcPurchaseRequestTotalsAsync(id);

                return Json(new { ok = true, message = $"تم إعادة حساب إجماليات الطلب رقم {id} بنجاح." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { ok = false, message = ex.Message });
            }
        }

        // =========================================================
        // تحويل طلب شراء (PurchaseRequest) إلى فاتورة مشتريات (PurchaseInvoice)
        // الهدف:
        // - إنشاء PurchaseInvoice جديدة برقم جديد
        // - نسخ سطور PRLines إلى PILines
        // - ترحيل فاتورة المشتريات الجديدة (لتأثير المخزون) عبر LedgerPostingService
        // - تحديث طلب الشراء: IsConverted = true + Status = "تم التحويل"
        // ملاحظة: لا نستخدم UserActionType.Convert لأنه غير موجود عندك
        // =========================================================
        [RequirePermission("PurchaseRequests.Edit")]
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> ConvertToPurchaseInvoice(int id)
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
                // 1) تحميل طلب الشراء + السطور
                // ================================
                var request = await _context.PurchaseRequests
                    .Include(x => x.Customer)
                    .Include(x => x.Lines)
                    .FirstOrDefaultAsync(x => x.PRId == id);

                if (request == null)
                {
                    if (isAjax) return NotFound(new { ok = false, message = "طلب الشراء غير موجود." });
                    TempData["Error"] = "طلب الشراء غير موجود.";
                    return RedirectToAction("Index");
                }

                // ================================
                // 2) منع التحويل لو تم تحويله بالفعل
                // ================================
                if (request.IsConverted)
                {
                    if (isAjax) return BadRequest(new { ok = false, message = "هذا الطلب تم تحويله بالفعل إلى فاتورة مشتريات." });
                    TempData["Error"] = "هذا الطلب تم تحويله بالفعل.";
                    return RedirectToAction("Show", new { id = request.PRId, frame = 1 });
                }

                // ================================
                // 3) تحقق سريع قبل التحويل
                // ================================
                if (request.CustomerId <= 0)
                {
                    var msg = "يجب اختيار المورد قبل تحويل طلب الشراء.";
                    if (isAjax) return BadRequest(new { ok = false, message = msg });
                    TempData["Error"] = msg;
                    return RedirectToAction("Show", new { id = request.PRId, frame = 1 });
                }

                if (request.WarehouseId <= 0)
                {
                    var msg = "يجب اختيار المخزن قبل تحويل طلب الشراء.";
                    if (isAjax) return BadRequest(new { ok = false, message = msg });
                    TempData["Error"] = msg;
                    return RedirectToAction("Show", new { id = request.PRId, frame = 1 });
                }

                if (request.Lines == null || request.Lines.Count == 0)
                {
                    var msg = "لا يمكن تحويل طلب شراء بدون سطور.";
                    if (isAjax) return BadRequest(new { ok = false, message = msg });
                    TempData["Error"] = msg;
                    return RedirectToAction("Show", new { id = request.PRId, frame = 1 });
                }

                // ================================
                // 4) Transaction (كل العملية دفعة واحدة)
                // ================================
                await using var tx = await _context.Database.BeginTransactionAsync();

                var now = DateTime.Now;                         // متغير: وقت التنفيذ الحالي
                var userName = GetCurrentUserDisplayName();     // متغير: اسم المستخدم الحالي

                // ================================
                // 5) إنشاء هيدر فاتورة مشتريات جديدة
                // ================================
                // وقت إنشاء الفاتورة = وقت إنشاء الطلب (نفس القيمة المخزنة حتى يتطابق العرض مع الطلب)
                var pi = new PurchaseInvoice
                {
                    PIDate = request.PRDate,        // تاريخ الفاتورة = تاريخ الطلب
                    CustomerId = request.CustomerId,// متغير: المورد
                    WarehouseId = request.WarehouseId, // متغير: المخزن

                    // متغير: ربط الفاتورة بطلب الشراء (لو عندك عمود مرجعي)
                    RefPRId = request.PRId,

                    TaxTotal = Math.Round(request.TaxAmount, 2),  // نقل الضريبة من طلب الشراء إلى الفاتورة

                    Status = "غير مرحلة",          // متغير: الحالة الافتراضية
                    IsPosted = false,              // متغير: غير مُرحلة مبدئياً (الترحيل سيتم بعد قليل)

                    CreatedAt = request.CreatedAt,  // وقت الإنشاء = وقت إنشاء الطلب (لا تحويل حتى لا يظهر فرق ساعتين)
                    CreatedBy = userName           // متغير: اسم المنشئ
                };

                _context.PurchaseInvoices.Add(pi);
                await _context.SaveChangesAsync(); // ✅ للحصول على PIId

                // ================================================================
                // 6) نسخ سطور طلب الشراء إلى سطور فاتورة المشتريات
                //    - ننسخ PriceRetail و PurchaseDiscountPct من PRLine ليتطابق إجمالي الفاتورة مع الطلب
                //    - PriceRetail: من السطر إن وُجد، وإلا من الصنف
                // ================================================================
                var prLines = await _context.PRLines
                    .AsNoTracking()
                    .Include(l => l.Product)
                    .Where(l => l.PRId == request.PRId)
                    .OrderBy(l => l.LineNo)
                    .ToListAsync();

                foreach (var l in prLines)
                {
                    var priceRetail = l.PriceRetail > 0 ? l.PriceRetail : (l.Product?.PriceRetail ?? 0m);
                    
                    // حساب تكلفة الوحدة المحسوبة
                    var lineValue = l.QtyRequested * priceRetail * (1m - (l.PurchaseDiscountPct / 100m));
                    var computedUnitCost = (l.QtyRequested > 0) ? (lineValue / l.QtyRequested) : 0m;
                    computedUnitCost = Math.Round(computedUnitCost, 2);
                    
                    // معالجة التشغيلة والصلاحية من PRLine (إن وجدت)
                    var batchNo = string.IsNullOrWhiteSpace(l.PreferredBatchNo) ? null : l.PreferredBatchNo.Trim();
                    DateTime? expDate = l.PreferredExpiry?.Date;
                    
                    var newLine = new PILine
                    {
                        PIId = pi.PIId,
                        LineNo = l.LineNo,
                        ProdId = l.ProdId,
                        Qty = l.QtyRequested,
                        UnitCost = computedUnitCost,
                        PriceRetail = priceRetail,
                        PurchaseDiscountPct = l.PurchaseDiscountPct,
                        BatchNo = batchNo,
                        Expiry = expDate
                    };

                    _context.PILines.Add(newLine);
                    
                    // ================================
                    // تحديث المخزون لكل سطر (بدون ترحيل محاسبي)
                    // ================================
                    
                    // 1) تحديث Batch (Master) - جدول التشغيلات الرئيسي
                    if (!string.IsNullOrWhiteSpace(batchNo) && expDate.HasValue)
                    {
                        var exp = expDate.Value.Date;
                        var batch = await _context.Batches
                            .FirstOrDefaultAsync(b =>
                                b.ProdId == l.ProdId &&
                                b.BatchNo == batchNo &&
                                b.Expiry.Date == exp);

                        if (batch == null)
                        {
                            batch = new Batch
                            {
                                ProdId = l.ProdId,
                                BatchNo = batchNo,
                                Expiry = exp,
                                PriceRetailBatch = priceRetail,
                                UnitCostDefault = computedUnitCost,
                                CustomerId = request.CustomerId,
                                EntryDate = DateTime.UtcNow,
                                IsActive = true
                            };
                            _context.Batches.Add(batch);
                        }
                        else
                        {
                            batch.PriceRetailBatch = priceRetail;
                            batch.UnitCostDefault = computedUnitCost;
                            batch.UpdatedAt = DateTime.UtcNow;
                            batch.IsActive = true;
                        }
                    }
                    
                    // 2) تحديث StockLedger - حركات المخزون
                    var purchaseDiscountPct = l.PurchaseDiscountPct;
                    var existingLedger = await _context.StockLedger.FirstOrDefaultAsync(x =>
                        x.SourceType == "Purchase" &&
                        x.SourceId == pi.PIId &&
                        x.SourceLine == l.LineNo &&
                        x.WarehouseId == request.WarehouseId &&
                        x.ProdId == l.ProdId &&
                        (x.BatchNo ?? "").Trim() == (batchNo ?? "") &&
                        ((x.Expiry.HasValue ? x.Expiry.Value.Date : (DateTime?)null) == expDate) &&
                        x.UnitCost == computedUnitCost &&
                        x.QtyOut == 0 &&
                        (x.PurchaseDiscount ?? 0m) == purchaseDiscountPct
                    );

                    if (existingLedger != null)
                    {
                        existingLedger.QtyIn += l.QtyRequested;
                        existingLedger.RemainingQty = (existingLedger.RemainingQty ?? 0) + l.QtyRequested;
                        existingLedger.PurchaseDiscount = purchaseDiscountPct;
                        existingLedger.TranDate = DateTime.UtcNow;
                        existingLedger.Note = $"Purchase Line (Merged): {l.Product?.ProdName ?? ""}";
                    }
                    else
                    {
                        var ledger = new StockLedger
                        {
                            TranDate = DateTime.UtcNow,
                            WarehouseId = request.WarehouseId,
                            ProdId = l.ProdId,
                            BatchNo = batchNo,
                            Expiry = expDate,
                            BatchId = null,
                            QtyIn = l.QtyRequested,
                            QtyOut = 0,
                            UnitCost = computedUnitCost,
                            RemainingQty = l.QtyRequested,
                            SourceType = "Purchase",
                            SourceId = pi.PIId,
                            SourceLine = l.LineNo,
                            PurchaseDiscount = purchaseDiscountPct,
                            Note = $"Purchase Line: {l.Product?.ProdName ?? ""}"
                        };
                        _context.StockLedger.Add(ledger);
                    }
                    
                    // 3) تحديث StockBatches - رصيد التشغيلات في المخزن
                    if (!string.IsNullOrWhiteSpace(batchNo) && expDate.HasValue)
                    {
                        var exp = expDate.Value.Date;
                        var sbRow = await _context.StockBatches
                            .FirstOrDefaultAsync(x =>
                                x.WarehouseId == request.WarehouseId &&
                                x.ProdId == l.ProdId &&
                                x.BatchNo == batchNo &&
                                x.Expiry.HasValue &&
                                x.Expiry.Value.Date == exp);

                        if (sbRow != null)
                        {
                            sbRow.QtyOnHand += l.QtyRequested;
                            sbRow.UpdatedAt = DateTime.UtcNow;
                            sbRow.Note = $"PI:{pi.PIId} Line:{l.LineNo} (+{l.QtyRequested})";
                        }
                        else
                        {
                            var newRow = new StockBatch
                            {
                                WarehouseId = request.WarehouseId,
                                ProdId = l.ProdId,
                                BatchNo = batchNo,
                                Expiry = exp,
                                QtyOnHand = l.QtyRequested,
                                QtyReserved = 0,
                                UpdatedAt = DateTime.UtcNow,
                                Note = $"PI:{pi.PIId} Line:{l.LineNo} (+{l.QtyRequested})"
                            };
                            _context.StockBatches.Add(newRow);
                        }
                    }
                }

                // حفظ سطور الفاتورة + تحديثات المخزون
                await _context.SaveChangesAsync();

                // ================================
                // 7) إعادة حساب إجماليات فاتورة المشتريات
                // ================================
                await _docTotals.RecalcPurchaseInvoiceTotalsAsync(pi.PIId);
                await _context.SaveChangesAsync();

                // ================================
                // 8) ملاحظة: لا نقوم بترحيل الحسابات هنا
                // الترحيل المحاسبي سيتم من خلال زر "ترحيل الفاتورة" في صفحة فاتورة المشتريات
                // ================================

                // ================================
                // 9) تحديث طلب الشراء بأنه تم تحويله
                // ================================
                request.IsConverted = true;             // متغير: تم التحويل
                request.Status = "تم التحويل";          // متغير: حالة واضحة في قائمة الطلبات
                request.UpdatedAt = DateTime.UtcNow;    // متغير: آخر تعديل

                await _context.SaveChangesAsync();

                // ✅ Commit لكل شيء
                await tx.CommitAsync();

                // ================================
                // 10) تسجيل نشاط (بدون Convert لأن enum مش موجود عندك)
                // ================================
                try
                {
                    await _activityLogger.LogAsync(
                        actionType: UserActionType.Post,     // ✅ استخدم قيمة موجودة عندك
                        entityName: "PurchaseRequest",
                        entityId: request.PRId,
                        description: $"تحويل طلب شراء رقم {request.PRId} إلى فاتورة مشتريات رقم {pi.PIId}"
                    );
                }
                catch
                {
                    // تعليق: لا نوقف العملية لو اللوج حصل فيه مشكلة
                }

                // ================================
                // 11) الرد للواجهة
                // ================================
                if (isAjax)
                {
                    return Ok(new
                    {
                        ok = true,
                        message = $"تم تحويل طلب الشراء بنجاح إلى فاتورة مشتريات رقم {pi.PIId}.",
                        prId = request.PRId,
                        piId = pi.PIId,
                        prStatus = request.Status,
                        isConverted = request.IsConverted,
                        postedLabel = "محول"
                    });
                }

                TempData["Success"] = $"تم تحويل طلب الشراء إلى فاتورة مشتريات رقم {pi.PIId}.";
                return RedirectToAction("Show", "PurchaseInvoices", new { id = pi.PIId, frame = 1 });
            }
            catch (Exception ex)
            {
                var msg = string.IsNullOrWhiteSpace(ex.Message) ? "حدث خطأ أثناء التحويل." : ex.Message;
                if (ex.InnerException != null && !string.IsNullOrWhiteSpace(ex.InnerException.Message))
                    msg = msg + " " + ex.InnerException.Message;

                if (isAjax)
                    return BadRequest(new { ok = false, message = "فشل التحويل: " + msg });

                TempData["Error"] = "فشل التحويل: " + msg;
                return RedirectToAction("Show", new { id, frame = 1 });
            }
        }
















        // ================================================================
        // إعادة حساب إجماليات جميع الطلبات
        // ================================================================
        [RequirePermission("PurchaseRequests.Edit")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecalcAllTotals()
        {
            try
            {
                var allRequests = await _context.PurchaseRequests
                    .Select(pr => pr.PRId)
                    .ToListAsync();

                int count = 0;
                foreach (var prId in allRequests)
                {
                    await _docTotals.RecalcPurchaseRequestTotalsAsync(prId);
                    count++;
                }

                return Json(new { ok = true, message = $"تم إعادة حساب إجماليات {count} طلب بنجاح." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { ok = false, message = ex.Message });
            }
        }

    }
}
