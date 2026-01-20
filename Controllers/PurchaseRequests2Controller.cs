using System;
using System.Collections.Generic;                 // القوائم (List)
using System.Linq;                                // أوامر LINQ: Where / OrderBy / Skip / Take
using System.Threading.Tasks;                     // async / Task
using ERP.Data;                                   // AppDbContext
using ERP.Infrastructure;                         // كلاس PagedResult لتقسيم الصفحات
using ERP.Models;                                 // الموديلات (PurchaseRequest, PRLine, Customer, Warehouse)
using ERP.ViewModels;                             // DTOs (PurchaseRequestHeaderDto)
using Microsoft.AspNetCore.Mvc;                   // Controller / IActionResult
using Microsoft.EntityFrameworkCore;              // Include / AsNoTracking
using ERP.Services;                               // استخدام DocumentTotalsService (سيرفيس إجماليات)

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إدارة "طلبات الشراء" - تصميم جديد (PurchaseRequests2)
    /// - تصميم محسّن وحديث لطلبات الشراء
    /// - نفس قاعدة البيانات والفهرس (PurchaseRequests)
    /// </summary>
    public class PurchaseRequests2Controller : Controller
    {
        private readonly AppDbContext _context;              // متغير: اتصال قاعدة البيانات
        private readonly DocumentTotalsService _docTotals;   // متغير: خدمة حساب إجماليات المستندات

        public PurchaseRequests2Controller(AppDbContext context,
                                           DocumentTotalsService docTotals)
        {
            _context = context;     // تخزين سياق قاعدة البيانات
            _docTotals = docTotals;   // تخزين سيرفيس الإجماليات لإعادة حساب إجماليات الطلب
        }


        // =========================================================
        // DTO: بيانات إضافة سطر لطلب الشراء (جاية من AJAX)
        // ملاحظة: طلب الشراء لا يُحدّث المخزون — فقط يحفظ في PRLines
        // =========================================================
        public class AddLineJsonDto
        {
            public int PRId { get; set; }                 // متغير: رقم طلب الشراء
            public int ProdId { get; set; }               // متغير: كود الصنف
            public int Qty { get; set; }                  // متغير: الكمية المطلوبة

            public decimal PriceRetail { get; set; }      // متغير: سعر الجمهور (للتطابق عند التحويل)
            public decimal PurchaseDiscountPct { get; set; } // متغير: نسبة خصم الشراء
            public decimal UnitCost { get; set; }         // متغير: التكلفة المتوقعة للوحدة (ExpectedCost)

            public string? BatchNo { get; set; }          // متغير: التشغيلة (اختياري)
            public string? ExpiryText { get; set; }       // متغير: الصلاحية كنص (اختياري)
        }


        // =========================================================
        // Helper: تحويل expiryText إلى DateTime? بشكل مرن
        // يدعم: yyyy-MM-dd أو MM/yyyy
        // =========================================================
        private static DateTime? ParseExpiryText(string? expiryText)
        {
            if (string.IsNullOrWhiteSpace(expiryText)) return null;

            var t = expiryText.Trim();

            // (1) محاولة DateTime مباشرة (مثال: 2026-01-19)
            if (DateTime.TryParse(t, out var dt))
                return dt.Date;

            // (2) محاولة MM/YYYY
            var parts = t.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out int mm) &&
                int.TryParse(parts[1], out int yyyy) &&
                mm >= 1 && mm <= 12)
            {
                return new DateTime(yyyy, mm, 1).Date;
            }

            return null;
        }


        // =========================================================
        // Helper: إعادة حساب إجماليات طلب الشراء (من السطور)
        // - TotalQtyRequested = مجموع QtyRequested
        // - ExpectedItemsTotal = مجموع (QtyRequested * ExpectedCost)
        // =========================================================
        private async Task RecalcPurchaseRequestTotalsAsync(int prId)
        {
            // متغير: تجميعة الإجماليات من السطور
            var agg = await _context.Set<PRLine>()
                .AsNoTracking()
                .Where(x => x.PRId == prId)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    TotalQty = g.Sum(x => (int?)x.QtyRequested) ?? 0,
                    ExpectedTotal = g.Sum(x => (decimal?)(x.QtyRequested * x.ExpectedCost)) ?? 0m
                })
                .FirstOrDefaultAsync();

            // متغير: رأس الطلب
            var request = await _context.PurchaseRequests.FirstOrDefaultAsync(x => x.PRId == prId);
            if (request == null) return;

            request.TotalQtyRequested = agg?.TotalQty ?? 0;
            request.ExpectedItemsTotal = agg?.ExpectedTotal ?? 0m;
            request.UpdatedAt = DateTime.UtcNow;
        }


        // =========================================================
        // POST: PurchaseRequests/AddLineJson
        // إضافة سطر جديد لطلب الشراء (بدون تأثير على المخزون)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddLineJson([FromBody] AddLineJsonDto dto)
        {
            try
            {
                // =========================
                // 0) فحص المدخلات
                // =========================
                if (dto == null)
                    return BadRequest(new { ok = false, message = "بيانات الإضافة غير صحيحة." });

                if (dto.PRId <= 0)
                    return BadRequest(new { ok = false, message = "لا يمكن إضافة أصناف قبل حفظ رأس الطلب." });

                if (dto.ProdId <= 0)
                    return BadRequest(new { ok = false, message = "من فضلك اختر صنف صحيح." });

                if (dto.Qty <= 0)
                    return BadRequest(new { ok = false, message = "الكمية يجب أن تكون أكبر من صفر." });

                // =========================
                // 1) تحميل الطلب (وتأكيد أنه غير محوّل)
                // =========================
                var request = await _context.PurchaseRequests
                    .FirstOrDefaultAsync(x => x.PRId == dto.PRId);

                if (request == null)
                    return NotFound(new { ok = false, message = "طلب الشراء غير موجود." });

                if (request.IsConverted)
                    return BadRequest(new { ok = false, message = "هذا الطلب تم تحويله لفاتورة شراء ولا يمكن تعديله." });

                // =========================
                // 2) رقم السطر التالي داخل نفس الطلب
                // =========================
                int nextLineNo = (await _context.Set<PRLine>()
                    .AsNoTracking()
                    .Where(x => x.PRId == dto.PRId)
                    .MaxAsync(x => (int?)x.LineNo) ?? 0) + 1;

                // =========================
                // 3) تجهيز بيانات السطر
                // =========================
                var expDate = ParseExpiryText(dto.ExpiryText);               // متغير: تاريخ الصلاحية (اختياري)
                var batchNo = string.IsNullOrWhiteSpace(dto.BatchNo) ? null : dto.BatchNo.Trim(); // متغير: التشغيلة

                var prLine = new PRLine
                {
                    PRId = dto.PRId,
                    LineNo = nextLineNo,
                    ProdId = dto.ProdId,
                    QtyRequested = dto.Qty,

                    PriceRetail = dto.PriceRetail,
                    PurchaseDiscountPct = dto.PurchaseDiscountPct,
                    ExpectedCost = dto.UnitCost,

                    PreferredBatchNo = batchNo,
                    PreferredExpiry = expDate
                };

                _context.Set<PRLine>().Add(prLine);

                // =========================
                // 4) تحديث إجماليات رأس الطلب (بدون مخزون)
                // =========================
                await _context.SaveChangesAsync();
                await RecalcPurchaseRequestTotalsAsync(dto.PRId);
                await _context.SaveChangesAsync();

                // =========================
                // 5) تجهيز خطوط للواجهة + Totals
                // =========================
                // ✅ ملاحظة مهمة:
                // جدول PRLines عندك لا يحتوي Navigation Property باسم Product
                // لذلك بنجيب اسم الصنف بعمل LEFT JOIN على جدول Products بدون كسر الموديل.
                var lines = await (
                    from l in _context.Set<PRLine>().AsNoTracking()
                    where l.PRId == dto.PRId
                    join p in _context.Set<Product>().AsNoTracking() on l.ProdId equals p.ProdId into pg
                    from p in pg.DefaultIfEmpty()
                    orderby l.LineNo
                    select new
                    {
                        lineNo = l.LineNo,
                        prodId = l.ProdId,
                        prodName = p != null ? p.ProdName : ("صنف #" + l.ProdId),
                        qty = l.QtyRequested,
                        priceRetail = l.PriceRetail,
                        purchaseDiscountPct = l.PurchaseDiscountPct,
                        unitCost = l.ExpectedCost,
                        batchNo = l.PreferredBatchNo,
                        expiry = l.PreferredExpiry,
                        expiryText = l.PreferredExpiry.HasValue ? l.PreferredExpiry.Value.ToString("yyyy-MM-dd") : "",
                        lineValue = (decimal)l.QtyRequested * l.PriceRetail * (1 - (l.PurchaseDiscountPct / 100m))
                    }
                ).ToListAsync();

                // تجميع إجماليات مرنة تناسب updateSummaryBadges
                var totalLines = lines.Count;
                var totalItems = lines.Select(x => x.prodId).Distinct().Count();
                var totalQty = lines.Sum(x => (int)x.qty);
                var totalRetail = lines.Sum(x => (decimal)x.qty * x.priceRetail);
                var totalDiscount = lines.Sum(x => (decimal)x.qty * x.priceRetail * (x.purchaseDiscountPct / 100m));
                var totalAfterDiscount = totalRetail - totalDiscount;

                return Json(new
                {
                    ok = true,
                    message = "تمت إضافة الصنف إلى الطلب.",
                    lines,
                    totals = new
                    {
                        totalLines,
                        totalItems,
                        totalQty,
                        totalRetail,
                        totalDiscount,
                        totalAfterDiscount,
                        taxAmount = 0m,
                        totalAfterDiscountAndTax = totalAfterDiscount,
                        netTotal = totalAfterDiscount
                    }
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { ok = false, message = $"خطأ أثناء إضافة السطر: {ex.Message}" });
            }
        }


        // =========================================================
        // دالة مساعدة: تجهيز بيانات التنقل (أول/سابق/التالي/آخر)
        // الهدف: تشتغل الأسهم حتى في الطلب الجديد (PRId = 0)
        // =========================================================
        private async Task FillPurchaseRequestNavAsync(int currentId)
        {
            // ==============================
            // 1) أول وآخر طلب (Query واحد)
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
            int? prevId = null; // متغير: رقم الطلب السابق
            int? nextId = null; // متغير: رقم الطلب التالي

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
                prevId = minMax?.LastId;   // السابق يأخذك لآخر طلب
                nextId = minMax?.FirstId;  // التالي يأخذك لأول طلب
            }

            // ==============================
            // 3) تعبئة ViewBag للـ View (بدون Null)
            // ==============================
            int firstId = minMax?.FirstId ?? 0;  // متغير: أول طلب
            int lastId = minMax?.LastId ?? 0;  // متغير: آخر طلب

            ViewBag.NavFirstId = firstId;
            ViewBag.NavLastId = lastId;
            ViewBag.NavPrevId = prevId ?? 0;
            ViewBag.NavNextId = nextId ?? 0;
        }

        /// <summary>
        /// تجهيز القوائم المنسدلة (الموردين + المخازن) لشاشة طلب الشراء.
        /// هنا بنعرض الموردين فقط: PartyCategory = "Supplier".
        /// </summary>
        private async Task PopulateDropDownsAsync(int? selectedSupplierId = null,
                                                  int? selectedWarehouseId = null)
        {
            // ===== الموردون فقط (نوع الطرف = Supplier) =====
            var suppliers = await _context.Customers
                .AsNoTracking()
                .Include(c => c.Governorate)
                .Include(c => c.District)
                .Include(c => c.Area)
                .OrderBy(c => c.CustomerName)
                .Select(c => new
                {
                    Id = c.CustomerId,                    // متغير: كود المورد
                    Code = c.CustomerId.ToString(),      // ✅ متغير: كود المورد الظاهر للمستخدم
                    Name = c.CustomerName,                  // متغير: اسم المورد
                    UserName = "",                              // حالياً مفيش مستخدم مربوط – نخليها فاضية
                    Phone = c.Phone1 ?? string.Empty,
                    Address = c.Address ?? string.Empty,
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

            // دى اللى بتتقرى في الـ datalist فى Show.cshtml
            ViewBag.Customers = suppliers;

            // لو فى مورد مختار (طلب قديم) نحضر اسمه لعرضه تلقائياً
            if (selectedSupplierId.HasValue)
            {
                var current = suppliers.FirstOrDefault(c => c.Id == selectedSupplierId.Value);
                if (current != null)
                {
                    ViewBag.SelectedCustomerName = current.Name; // متغير: اسم المورد الحالي
                }
            }
            else
            {
                ViewBag.SelectedCustomerName = ""; // متغير: طلب جديد - لا يوجد مورد مختار
            }

            // ===== المخازن =====
            var warehouses = await _context.Warehouses
                .AsNoTracking()
                .OrderBy(w => w.WarehouseName)
                .ToListAsync();

            ViewBag.Warehouses = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                warehouses,
                "WarehouseId",
                "WarehouseName",
                selectedWarehouseId
            );
        }

        // =========================================================
        // دالة مساعدة: تحميل قائمة الأصناف للأوتوكومبليت فى سطر طلب الشراء
        // =========================================================
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
                })
                .ToListAsync();

            // متغير: نرسل القائمة إلى الواجهة لتغذية الـ datalist
            ViewBag.ProductsAuto = products;
        }





        // ========================================================
        // GET: PurchaseRequests/Index
        // شاشة "قائمة طلبات الشراء" بنظام القوائم الموحّد
        // ========================================================
        public async Task<IActionResult> Index(
            string? search,                      // نص البحث الحر
            string? searchBy,                    // نوع البحث (id / customer / warehouse / status / date / all)
            string? sort,                        // عمود الترتيب (id / date / needby / customer / warehouse / status / created)
            string? dir,                         // اتجاه الترتيب asc / desc
            int page = 1,                        // رقم الصفحة
            int pageSize = 25,                   // عدد السطور في الصفحة
            bool useDateRange = false,           // تفعيل فلتر التاريخ؟
            DateTime? fromDate = null,           // تاريخ من
            DateTime? toDate = null,             // تاريخ إلى
            int? fromCode = null,                // من رقم طلب
            int? toCode = null,                  // إلى رقم طلب
            string? dateField = null             // اسم حقل التاريخ المستخدم (هنا PRDate)
        )
        {
            // 1) قيم افتراضية للباراميترات لو مش جاية من الكويري
            searchBy ??= "id";          // البحث الافتراضي برقم الطلب
            sort ??= "date";         // الترتيب الافتراضي بتاريخ الطلب
            dir = (dir == "asc") ? "asc" : "desc";
            dateField ??= "PRDate";

            // 2) استعلام الأساس مع Include للعميل والمخزن
            var query = _context.PurchaseRequests
                .Include(pr => pr.Customer)      // بيانات العميل
                .Include(pr => pr.Warehouse)     // بيانات المخزن
                .AsNoTracking()                  // للقراءة فقط
                .AsQueryable();

            // 3) تطبيق البحث حسب نوعه
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();

                switch (searchBy)
                {
                    case "id":      // البحث برقم الطلب
                        if (int.TryParse(s, out var idVal))
                        {
                            query = query.Where(pr => pr.PRId == idVal);
                        }
                        else
                        {
                            query = query.Where(pr => pr.PRId.ToString().Contains(s));
                        }
                        break;

                    case "customer": // البحث باسم العميل أو كوده
                        query = query.Where(pr =>
                            pr.Customer.CustomerName.Contains(s) ||
                            pr.CustomerId.ToString().Contains(s));
                        break;

                    case "warehouse": // البحث باسم المخزن أو كوده
                        query = query.Where(pr =>
                            pr.Warehouse.WarehouseName.Contains(s) ||
                            pr.WarehouseId.ToString().Contains(s));
                        break;

                    case "status":   // البحث بالحالة
                        query = query.Where(pr => pr.Status.Contains(s));
                        break;

                    case "date":     // البحث بتاريخ محدد مكتوب في صندوق البحث
                        if (DateTime.TryParse(s, out var d))
                        {
                            var dateOnly = d.Date;
                            query = query.Where(pr => pr.PRDate.Date == dateOnly);
                        }
                        break;

                    default:         // بحث عام في أكثر من حقل
                        query = query.Where(pr =>
                            pr.PRId.ToString().Contains(s) ||
                            pr.Customer.CustomerName.Contains(s) ||
                            pr.Status.Contains(s));
                        break;
                }
            }

            // 4) فلتر التاريخ من/إلى على PRDate
            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                var from = fromDate.Value.Date;
                var to = toDate.Value.Date;
                query = query.Where(pr => pr.PRDate.Date >= from && pr.PRDate.Date <= to);
            }

            // 5) فلتر "من رقم / إلى رقم"
            if (fromCode.HasValue)
                query = query.Where(pr => pr.PRId >= fromCode.Value);

            if (toCode.HasValue)
                query = query.Where(pr => pr.PRId <= toCode.Value);

            // 6) الترتيب
            bool descending = (dir == "desc");

            query = sort switch
            {
                "id" => descending
                    ? query.OrderByDescending(pr => pr.PRId)
                    : query.OrderBy(pr => pr.PRId),

                "date" => descending
                    ? query.OrderByDescending(pr => pr.PRDate).ThenByDescending(pr => pr.PRId)
                    : query.OrderBy(pr => pr.PRDate).ThenBy(pr => pr.PRId),

                "needby" => descending
                    ? query.OrderByDescending(pr => pr.NeedByDate).ThenByDescending(pr => pr.PRId)
                    : query.OrderBy(pr => pr.NeedByDate).ThenBy(pr => pr.PRId),

                "customer" => descending
                    ? query.OrderByDescending(pr => pr.Customer.CustomerName)
                    : query.OrderBy(pr => pr.Customer.CustomerName),

                "warehouse" => descending
                    ? query.OrderByDescending(pr => pr.Warehouse.WarehouseName)
                    : query.OrderBy(pr => pr.Warehouse.WarehouseName),

                "status" => descending
                    ? query.OrderByDescending(pr => pr.Status)
                    : query.OrderBy(pr => pr.Status),

                "created" => descending
                    ? query.OrderByDescending(pr => pr.CreatedAt).ThenByDescending(pr => pr.PRId)
                    : query.OrderBy(pr => pr.CreatedAt).ThenBy(pr => pr.PRId),

                _ => descending
                    ? query.OrderByDescending(pr => pr.PRDate).ThenByDescending(pr => pr.PRId)
                    : query.OrderBy(pr => pr.PRDate).ThenBy(pr => pr.PRId),
            };

            // 7) إعداد الترقيم (Paging)
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 25;

            var total = await query.CountAsync(); // إجمالي السطور في الفلتر الحالي

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // متغير: موديل نتيجة الترقيم لنظام القوائم الموّحد
            var model = new PagedResult<PurchaseRequest>
            {
                Items = items,
                TotalCount = total,
                PageNumber = page,
                PageSize = pageSize,
                Search = search,
                SearchBy = searchBy,
                SortColumn = sort,
                SortDescending = descending,
                UseDateRange = useDateRange,
                FromDate = fromDate,
                ToDate = toDate
                // من الممكن لاحقاً نضيف FromCode / ToCode لو حبيت توسّع PagedResult
            };

            // 8) قيم إضافية للواجهة (فلتر الكود من/إلى)
            ViewBag.Search = search;
            ViewBag.SearchBy = searchBy;
            ViewBag.Sort = sort;
            ViewBag.Dir = dir;
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.CodeFrom = fromCode;
            ViewBag.CodeTo = toCode;
            ViewBag.DateField = dateField;

            return View(model);
        }






        // ========================================================
        // GET: PurchaseRequests/Show/{id}
        // فتح شاشة عرض/تعديل طلب شراء (للطلبات القديمة)
        // ========================================================
        [HttpGet]
        public async Task<IActionResult> Show(int id, string? frag = null, int? frame = null)
        {
            // =========================================
            // متغير: هل هذا الطلب يطلب "Body فقط"؟
            // - frag=body معناها: نريد جزء الصفحة المتغير فقط (بدون Layout وبدون شريط الأزرار)
            // =========================================
            bool isBodyOnly = string.Equals(frag, "body", StringComparison.OrdinalIgnoreCase);

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
            var purchaseRequest = await _context.PurchaseRequests
                .Include(pr => pr.Customer)
                    .ThenInclude(c => c.Governorate)
                .Include(pr => pr.Customer)
                    .ThenInclude(c => c.District)
                .Include(pr => pr.Customer)
                    .ThenInclude(c => c.Area)
                .Include(pr => pr.Lines)
                    .ThenInclude(l => l.Product)
                .AsNoTracking()
                .FirstOrDefaultAsync(pr => pr.PRId == id);

            // =========================================
            // 2) لو الطلب غير موجود (ممسوح / رقم غلط)
            // =========================================
            if (purchaseRequest == null)
            {
                // -----------------------------------------
                // ✅ حالة خاصة: frag=body (تنقل/Fetch)
                // هنا ممنوع Redirect لأنه سيؤدي لصفحة فاضية/فلاش
                // الأفضل: نرجّع 404 برسالة واضحة، والـ JS يتعامل معها
                // -----------------------------------------
                if (isBodyOnly)
                {
                    return NotFound($"طلب الشراء رقم ({id}) غير موجود (قد يكون محذوفاً).");
                }

                // -----------------------------------------
                // منطق فتح أقرب طلب بدل NotFound داخل iframe
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
                    TempData["Error"] = $"رقم الطلب ({id}) غير موجود (قد يكون محذوفاً). تم فتح الطلب التالي رقم ({nearestNext.Value}).";
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
                    TempData["Error"] = $"رقم الطلب ({id}) غير موجود (قد يكون محذوفاً). تم فتح الطلب السابق رقم ({nearestPrev.Value}).";
                    return RedirectToAction(nameof(Show), new { id = nearestPrev.Value, frag = (string?)null, frame = 1 });
                }

                // لو مفيش أي طلبات أصلاً
                TempData["Error"] = "لا توجد طلبات شراء مسجلة حالياً.";
                return RedirectToAction(nameof(Create), new { frame = 1 });
            }

            // =========================================
            // 3) تجهيز القوائم + الأوتوكومبليت
            // ✅ مهم للأداء: لو frag=body (تنقل بالأسهم) ما نعملش تحميل تقيل
            // لأن الـ Body بيتبدّل كتير، وده كان سبب الـ 10MB والبطء
            // =========================================
            await PopulateDropDownsAsync(purchaseRequest.CustomerId, purchaseRequest.WarehouseId);
            await LoadProductsForAutoCompleteAsync();

            // متغير: هل الطلب مقفول (محوّل)
            ViewBag.IsLocked = purchaseRequest.IsConverted || purchaseRequest.Status == "Converted";

            // متغير: علامة للـ View أننا داخل Frame (في العرض الكامل فقط)
            ViewBag.Frame = (!isBodyOnly) ? 1 : 0;

            // =========================================
            // 4) ✅ تجهيز التنقل بشكل موحّد (استخدم دالتك المساعدة)
            // =========================================
            await FillPurchaseRequestNavAsync(purchaseRequest.PRId);

            // =========================================
            // 5) عرض الـ View نفسه
            // - الـ View سيقرر ماذا يعرض بناءً على ViewBag.Fragment
            // =========================================
            return View("Show", purchaseRequest);
        }

        // ========================================================
        // GET: PurchaseRequests/Create
        // فتح شاشة طلب جديد باستخدام View: Show
        // ========================================================
        public async Task<IActionResult> Create(int? frame = null)
        {
            // متغير: موديل طلب شراء جديد بالقيم الافتراضية
            var model = new PurchaseRequest
            {
                PRId = 0,                       // طلب جديد (لم يتم الحفظ بعد)
                PRDate = DateTime.Today,        // تاريخ اليوم كقيمة افتراضية
                NeedByDate = null,              // المستخدم يحددها من الشاشة
                WarehouseId = 0,                // يختار المخزن من الشاشة
                CustomerId = 0,                 // يختار المورد من الشاشة

                // إجماليات الطلب (هتتحسب من السطور عن طريق السيرفيس)
                TotalQtyRequested = 0,          // إجمالي الكمية المطلوبة = 0 مبدئياً
                ExpectedItemsTotal = 0m,        // إجمالي التكلفة المتوقعة = 0 مبدئياً

                Status = "Draft",               // الحالة الافتراضية: مسودة
                IsConverted = false,            // الطلب لسه متحوّلش لفاتورة شراء

                RequestedBy = "",               // سيتم ملؤه من الواجهة
                CreatedBy = "",                 // سيتم ملؤه عند الحفظ

                CreatedAt = DateTime.UtcNow,    // وقت إنشاء السجل
                Lines = new List<PRLine>()      // قائمة سطور فاضية
            };

            // ==============================
            // 2) تجهيز القوائم المنسدلة + الأوتوكومبليت
            // ==============================
            // متغير: تحميل الموردين + المخازن مع تمرير المخزن/المورد المختارين (حتى لو 0 عادى)
            await PopulateDropDownsAsync(model.CustomerId, model.WarehouseId);

            // متغير: تحميل الأصناف للأوتوكومبليت فى سطور الطلب
            await LoadProductsForAutoCompleteAsync();

            // ==============================
            // 3) تجهيز ViewBag
            // ==============================
            ViewBag.IsLocked = false;
            ViewBag.Frame = frame ?? 1;

            // ✅ تجهيز الأسهم حتى في الطلب الجديد (PRId = 0)
            await FillPurchaseRequestNavAsync(model.PRId);

            // ==============================
            // 4) فتح شاشة Show بنفس الموديل (نظام شاشة موحدة للإنشاء/التعديل)
            // ==============================
            return View("Show", model);
        }





        // =======================
        // GET: PurchaseRequests/Edit/5
        // شاشة تعديل رأس طلب الشراء
        // =======================
        public async Task<IActionResult> Edit(int? id)
        {
            // لو مفيش رقم طلب → نرجع 404
            if (id == null)
                return NotFound();

            // متغير: نجيب رأس طلب الشراء من قاعدة البيانات
            // مع ربط المخزن والعميل للعرض فقط
            var purchaseRequest = await _context.PurchaseRequests
                .Include(p => p.Warehouse)   // بيانات المخزن
                .Include(p => p.Customer)    // بيانات العميل / المورد
                .FirstOrDefaultAsync(p => p.PRId == id.Value);

            // لو الطلب مش موجود → نرجع 404
            if (purchaseRequest == null)
                return NotFound();

            // نرجع الموديل للفيو Edit.cshtml
            return View(purchaseRequest);
        }

        // =========================
        // Edit — GET: فتح طلب شراء قديم للعرض/التعديل
        // =========================
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            // تحقق بسيط من رقم الطلب
            if (id <= 0)
                return BadRequest("رقم طلب الشراء غير صالح.");

            // قراءة هيدر طلب الشراء من قاعدة البيانات (بدون تتبّع)
            var model = await _context.PurchaseRequests
                .AsNoTracking()
                .FirstOrDefaultAsync(pr => pr.PRId == id);

            // لو الطلب مش موجود
            if (model == null)
                return NotFound();

            // تعبئة القوائم المنسدلة (موردين، مخازن، مستخدمين... إلخ) لو عندك
            await PopulateDropDownsAsync();

            // فتح شاشة طلب الشراء (Edit = شاشة عرض + إمكانية التعديل حسب الصلاحيات)
            return View(model);
        }



        // =========================
        // Edit — POST: حفظ تعديل هيدر طلب الشراء
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, PurchaseRequest request)
        {
            // تأكد أن رقم الطلب في الرابط هو نفس الموجود في الموديل
            if (id != request.PRId)
                return NotFound();

            // لو فيه أخطاء تحقق (Validation) في الموديل
            if (!ModelState.IsValid)
            {
                // نرجّع القوائم المنسدلة قبل الرجوع للفيو
                await PopulateDropDownsAsync();
                return View(request);
            }

            try
            {
                // هنا لو الموديل فيه حقول زي UpdatedAt تقدر تحدثها يدويًا:
                // request.UpdatedAt = DateTime.Now;

                // تحديث الكيان في الـ DbContext
                _context.Update(request);

                // حفظ التغييرات فعلياً في قاعدة البيانات
                await _context.SaveChangesAsync();

                TempData["Msg"] = "تم تعديل طلب الشراء بنجاح.";
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateException ex)
            {
                // أي خطأ في التحديث/الحفظ
                ModelState.AddModelError(string.Empty, "تعذّر حفظ التعديلات: " + ex.Message);

                await PopulateDropDownsAsync();
                return View(request);
            }
        }








        // =========================================================
        // SaveHeader — POST: حفظ/إنشاء رأس طلب الشراء عبر AJAX
        // مشابه لـ PurchaseInvoice.SaveHeader لكن بدون تأثير على المخزون/الحسابات
        // =========================================================
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> SaveHeader([FromBody] PurchaseRequestHeaderDto dto)
        {
            // 1) فحص الداتا المرسلة من الواجهة
            if (dto == null)
            {
                return BadRequest("حدث خطأ في البيانات المرسلة.");
            }

            if (dto.CustomerId <= 0)
            {
                return BadRequest("يجب اختيار المورد قبل حفظ الطلب.");
            }

            if (dto.WarehouseId <= 0)
            {
                return BadRequest("يجب اختيار المخزن قبل حفظ الطلب.");
            }

            var now = DateTime.UtcNow;

            // 2) طلب جديد (PRId = 0)
            if (dto.PRId == 0)
            {
                var request = new PurchaseRequest
                {
                    PRDate = dto.PRDate.HasValue ? dto.PRDate.Value : DateTime.Today,
                    NeedByDate = dto.NeedByDate,
                    CustomerId = dto.CustomerId,
                    WarehouseId = dto.WarehouseId,
                    RequestedBy = dto.RequestedBy ?? "",
                    CreatedBy = GetCurrentUserDisplayName(),
                    Status = "Draft",
                    IsConverted = false,
                    TotalQtyRequested = 0,
                    ExpectedItemsTotal = 0m,
                    Notes = dto.Notes,
                    CreatedAt = now
                };

                _context.PurchaseRequests.Add(request);
                await _context.SaveChangesAsync();

                // الرد إلى الجافاسكربت
                return Json(new
                {
                    success = true,
                    prId = request.PRId,
                    requestNumber = request.PRId.ToString(),
                    requestDate = request.PRDate.ToString("yyyy/MM/dd"),
                    requestTime = request.CreatedAt.ToString("HH:mm"),
                    status = request.Status,
                    isConverted = request.IsConverted,
                    createdBy = request.CreatedBy
                });
            }

            // 3) تعديل طلب موجود (PRId > 0)
            var existing = await _context.PurchaseRequests
                .FirstOrDefaultAsync(pr => pr.PRId == dto.PRId);

            if (existing == null)
            {
                return NotFound("لم يتم العثور على الطلب المطلوب.");
            }

            if (existing.IsConverted)
            {
                return BadRequest("لا يمكن تعديل طلب تم تحويله إلى فاتورة شراء.");
            }

            existing.PRDate = dto.PRDate ?? existing.PRDate;
            existing.NeedByDate = dto.NeedByDate;
            existing.CustomerId = dto.CustomerId;
            existing.WarehouseId = dto.WarehouseId;
            existing.RequestedBy = dto.RequestedBy ?? existing.RequestedBy;
            existing.Notes = dto.Notes;
            existing.UpdatedAt = now;

            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                prId = existing.PRId,
                requestNumber = existing.PRId.ToString(),
                requestDate = existing.PRDate.ToString("yyyy/MM/dd"),
                requestTime = existing.CreatedAt.ToString("HH:mm"),
                status = existing.Status,
                isConverted = existing.IsConverted,
                createdBy = existing.CreatedBy
            });
        }




        // =========================================================
        // DTO: بيانات إضافة سطر لطلب الشراء (جاية من AJAX)
        // ملاحظة: طلب الشراء لا يُحدِّث المخزون ولا ينشئ StockLedger.
        // =========================================================
        public class AddRequestLineJsonDto
        {
            public int PRId { get; set; }                 // متغير: رقم طلب الشراء
            public int ProdId { get; set; }               // متغير: كود الصنف
            public int Qty { get; set; }                  // متغير: الكمية المطلوبة

            public decimal PriceRetail { get; set; }      // متغير: سعر الجمهور (للتجهيز قبل التحويل)
            public decimal PurchaseDiscountPct { get; set; } // متغير: خصم الشراء % (للتجهيز قبل التحويل)
            public decimal UnitCost { get; set; }         // متغير: تكلفة متوقعة للوحدة (اختياري)

            public string? BatchNo { get; set; }          // متغير: رقم التشغيلة (اختياري)
            public string? ExpiryText { get; set; }       // متغير: الصلاحية نصًا (اختياري)
        }

        // =========================================================
        // AddLineJson — POST: إضافة سطر داخل طلب الشراء عبر AJAX
        // ✅ المطلوب:
        // - حفظ السطر فقط داخل PRLines
        // - تحديث إجماليات طلب الشراء داخل PurchaseRequests (TotalQtyRequested / ExpectedItemsTotal)
        // - بدون أي تأثير على المخزون (لا StockLedger ولا StockBatches)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddLineJson([FromBody] AddRequestLineJsonDto dto)
        {
            try
            {
                // =========================
                // 0) فحص المدخلات الأساسية
                // =========================
                if (dto == null)
                    return BadRequest(new { ok = false, message = "بيانات الإضافة غير صحيحة." });

                int prId = dto.PRId;
                if (prId <= 0)
                    return BadRequest(new { ok = false, message = "يجب حفظ رأس طلب الشراء أولاً قبل إضافة أصناف." });

                if (dto.ProdId <= 0)
                    return BadRequest(new { ok = false, message = "من فضلك اختر صنف صحيح." });

                if (dto.Qty <= 0)
                    return BadRequest(new { ok = false, message = "الكمية يجب أن تكون أكبر من صفر." });

                // =========================
                // 1) تحميل الطلب والتحقق من حالته
                // =========================
                var request = await _context.PurchaseRequests
                    .FirstOrDefaultAsync(x => x.PRId == prId);

                if (request == null)
                    return NotFound(new { ok = false, message = "طلب الشراء غير موجود." });

                // ✅ قفل بعد التحويل
                if (request.IsConverted || string.Equals(request.Status, "Converted", StringComparison.OrdinalIgnoreCase))
                    return BadRequest(new { ok = false, message = "هذا الطلب تم تحويله إلى فاتورة شراء ولا يسمح بتعديله." });

                // =========================
                // 2) تحويل الصلاحية من نص إلى DateTime? (ندعم شكلين)
                // - yyyy-MM-dd (لو جاية من input type=date)
                // - MM/YYYY (لو المستخدم بيكتبها يدوي)
                // =========================
                DateTime? expiry = null; // متغير: الصلاحية
                var expText = (dto.ExpiryText ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(expText))
                {
                    // (أ) محاولة parse مباشر
                    if (DateTime.TryParse(expText, out var parsed))
                    {
                        expiry = parsed.Date;
                    }
                    else
                    {
                        // (ب) MM/YYYY
                        var parts = expText.Split('/', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 2 &&
                            int.TryParse(parts[0], out int mm) &&
                            int.TryParse(parts[1], out int yyyy) &&
                            mm >= 1 && mm <= 12)
                        {
                            expiry = new DateTime(yyyy, mm, 1);
                        }
                    }
                }

                // =========================
                // 3) تحديد رقم السطر (LineNo) = آخر رقم + 1
                // =========================
                int lastLineNo = await _context.Set<PRLine>()
                    .Where(l => l.PRId == prId)
                    .Select(l => (int?)l.LineNo)
                    .OrderByDescending(x => x)
                    .FirstOrDefaultAsync() ?? 0;

                int newLineNo = lastLineNo + 1; // متغير: رقم السطر الجديد

                // =========================
                // 4) إنشاء السطر وحفظه
                // =========================
                var line = new PRLine
                {
                    PRId = prId,
                    LineNo = newLineNo,
                    ProdId = dto.ProdId,
                    QtyRequested = dto.Qty,

                    // قيم تجهيز التحويل (ممكن تكون 0 لو المستخدم لسه ما يعرفهاش)
                    PriceRetail = dto.PriceRetail,
                    PurchaseDiscountPct = dto.PurchaseDiscountPct,
                    ExpectedCost = dto.UnitCost,

                    PreferredBatchNo = string.IsNullOrWhiteSpace(dto.BatchNo) ? null : dto.BatchNo.Trim(),
                    PreferredExpiry = expiry
                };

                _context.Set<PRLine>().Add(line);
                await _context.SaveChangesAsync();

                // =========================
                // 5) إعادة حساب إجماليات الطلب (من كل السطور)
                // =========================
                await RecalcPurchaseRequestTotalsAsync(prId);

                // =========================
                // 6) تجهيز بيانات السطور للواجهة (مع اسم الصنف)
                // =========================
                var lines = await _context.Set<PRLine>()
                    .AsNoTracking()
                    .Where(l => l.PRId == prId)
                    .OrderBy(l => l.LineNo)
                    .Select(l => new
                    {
                        lineNo = l.LineNo,
                        prodId = l.ProdId,
                        prodName = _context.Products.Where(p => p.ProdId == l.ProdId).Select(p => p.ProdName).FirstOrDefault(),
                        qty = l.QtyRequested,
                        priceRetail = l.PriceRetail,
                        purchaseDiscountPct = l.PurchaseDiscountPct,
                        unitCost = l.ExpectedCost,
                        batchNo = l.PreferredBatchNo,
                        expiry = l.PreferredExpiry,
                        expiryText = l.PreferredExpiry.HasValue ? l.PreferredExpiry.Value.ToString("yyyy-MM-dd") : "",
                        lineValue = (decimal)l.QtyRequested * l.PriceRetail * (1m - (l.PurchaseDiscountPct / 100m))
                    })
                    .ToListAsync();

                // =========================
                // 7) تجهيز إجماليات مرنة تتماشى مع updateSummaryBadges في الواجهة
                // =========================
                var totalLines = lines.Count;
                var totalItems = lines.Select(x => x.prodId).Distinct().Count();
                var totalQty = lines.Sum(x => x.qty);
                var totalRetail = lines.Sum(x => (decimal)x.qty * x.priceRetail);
                var totalDiscount = lines.Sum(x => (decimal)x.qty * x.priceRetail * (x.purchaseDiscountPct / 100m));
                var totalAfterDiscount = totalRetail - totalDiscount;

                return Json(new
                {
                    ok = true,
                    message = "تمت إضافة السطر بنجاح.",
                    lines = lines,
                    totals = new
                    {
                        totalLines,
                        totalItems,
                        totalQty,
                        totalRetail,
                        totalDiscount,
                        totalAfterDiscount,
                        taxAmount = 0m
                    }
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { ok = false, message = $"خطأ أثناء إضافة السطر: {ex.Message}" });
            }
        }


        // =========================================================
        // ConvertToInvoice — POST: تحويل طلب شراء إلى فاتورة شراء
        // ينشئ فاتورة شراء جديدة من بيانات الطلب (Header + Lines)
        // =========================================================
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> ConvertToInvoice(int prId)
        {
            // =========================
            // 1) تحميل طلب الشراء مع السطور
            // =========================
            var request = await _context.PurchaseRequests
                .Include(pr => pr.Lines)
                .FirstOrDefaultAsync(pr => pr.PRId == prId);

            if (request == null)
                return NotFound(new { ok = false, message = "طلب الشراء غير موجود." });

            if (request.Lines == null || !request.Lines.Any())
                return BadRequest(new { ok = false, message = "لا يمكن تحويل طلب بدون سطور." });

            // =========================
            // 2) استخدام Transaction للحفظ الآمن
            // =========================
            await using var tx = await _context.Database.BeginTransactionAsync();

            try
            {
                // =========================
                // 3) إنشاء فاتورة شراء جديدة
                // =========================
                var now = DateTime.UtcNow;
                var invoice = new PurchaseInvoice
                {
                    PIDate = now.Date,
                    CustomerId = request.CustomerId,
                    WarehouseId = request.WarehouseId,
                    RefPRId = prId,                  // ربط الفاتورة بالطلب

                    Status = "غير مرحلة",
                    IsPosted = false,

                    CreatedAt = now,
                    CreatedBy = GetCurrentUserDisplayName(),

                    // الإجماليات سيتم حسابها لاحقاً من السطور
                    ItemsTotal = 0m,
                    DiscountTotal = 0m,
                    TaxTotal = 0m,
                    NetTotal = 0m
                };

                _context.PurchaseInvoices.Add(invoice);
                await _context.SaveChangesAsync(); // لحصول على PIId

                // =========================
                // 4) نسخ السطور من PRLines إلى PILines + تحديث المخزون مباشرة
                // =========================
                int lineNo = 1;

                foreach (var prLine in request.Lines.OrderBy(l => l.LineNo))
                {
                    // حساب UnitCost من ExpectedCost (إن وُجد)
                    decimal unitCost = 0m;
                    if (prLine.QtyRequested > 0 && prLine.ExpectedCost > 0)
                    {
                        unitCost = prLine.ExpectedCost / prLine.QtyRequested;
                    }
                    else if (prLine.PriceRetail > 0)
                    {
                        // أو من PriceRetail بعد خصم الشراء
                        unitCost = prLine.PriceRetail * (1 - (prLine.PurchaseDiscountPct / 100m));
                    }

                    unitCost = Math.Round(unitCost, 2);

                    // تنظيف BatchNo و Expiry
                    var batchNo = string.IsNullOrWhiteSpace(prLine.PreferredBatchNo) ? null : prLine.PreferredBatchNo.Trim();
                    var expDate = prLine.PreferredExpiry?.Date;

                    // إنشاء PILine
                    var piLine = new PILine
                    {
                        PIId = invoice.PIId,
                        LineNo = lineNo++,
                        ProdId = prLine.ProdId,
                        Qty = prLine.QtyRequested,
                        PriceRetail = prLine.PriceRetail,
                        PurchaseDiscountPct = prLine.PurchaseDiscountPct,
                        UnitCost = unitCost,
                        BatchNo = batchNo,
                        Expiry = expDate
                    };

                    _context.PILines.Add(piLine);

                    // =========================
                    // 4.1) تحديث Batch (Master) - إذا كان BatchNo و Expiry موجودين
                    // =========================
                    Batch? batch = null;
                    if (!string.IsNullOrWhiteSpace(batchNo) && expDate.HasValue)
                    {
                        var exp = expDate.Value.Date;
                        batch = await _context.Batches
                            .FirstOrDefaultAsync(b =>
                                b.ProdId == prLine.ProdId &&
                                b.BatchNo == batchNo &&
                                b.Expiry.Date == exp);

                        if (batch == null)
                        {
                            batch = new Batch
                            {
                                ProdId = prLine.ProdId,
                                BatchNo = batchNo,
                                Expiry = exp,
                                PriceRetailBatch = prLine.PriceRetail,
                                UnitCostDefault = unitCost,
                                CustomerId = invoice.CustomerId,
                                EntryDate = now,
                                IsActive = true
                            };
                            _context.Batches.Add(batch);
                        }
                        else
                        {
                            batch.PriceRetailBatch = prLine.PriceRetail;
                            batch.UnitCostDefault = unitCost;
                            batch.UpdatedAt = now;
                            batch.IsActive = true;
                        }
                    }

                    // =========================
                    // 4.2) إنشاء StockLedger (حركة دخول) - تحديث المخزون مباشرة
                    // =========================
                    var ledger = new StockLedger
                    {
                        TranDate = now,
                        WarehouseId = invoice.WarehouseId,
                        ProdId = prLine.ProdId,
                        BatchNo = batchNo,
                        Expiry = expDate,
                        BatchId = batch?.BatchId,
                        QtyIn = prLine.QtyRequested,
                        QtyOut = 0,
                        UnitCost = unitCost,
                        RemainingQty = prLine.QtyRequested,
                        SourceType = "Purchase",
                        SourceId = invoice.PIId,
                        SourceLine = piLine.LineNo,
                        PurchaseDiscount = prLine.PurchaseDiscountPct,
                        Note = $"Purchase Line from PR#{prId}: {prLine.ProdId}"
                    };

                    _context.StockLedger.Add(ledger);

                    // =========================
                    // 4.3) تحديث StockBatches (الكمية في المخزن) - إذا كان BatchNo و Expiry موجودين
                    // =========================
                    if (!string.IsNullOrWhiteSpace(batchNo) && expDate.HasValue)
                    {
                        var exp = expDate.Value.Date;
                        var sbRow = await _context.StockBatches
                            .FirstOrDefaultAsync(x =>
                                x.WarehouseId == invoice.WarehouseId &&
                                x.ProdId == prLine.ProdId &&
                                x.BatchNo == batchNo &&
                                x.Expiry.HasValue &&
                                x.Expiry.Value.Date == exp);

                        if (sbRow != null)
                        {
                            sbRow.QtyOnHand += prLine.QtyRequested;
                            sbRow.UpdatedAt = now;
                            sbRow.Note = $"PI:{invoice.PIId} Line:{piLine.LineNo} (from PR#{prId})";
                        }
                        else
                        {
                            var newRow = new StockBatch
                            {
                                WarehouseId = invoice.WarehouseId,
                                ProdId = prLine.ProdId,
                                BatchNo = batchNo,
                                Expiry = exp,
                                QtyOnHand = prLine.QtyRequested,
                                QtyReserved = 0,
                                UpdatedAt = now,
                                Note = $"PI:{invoice.PIId} Line:{piLine.LineNo} (from PR#{prId})"
                            };
                            _context.StockBatches.Add(newRow);
                        }
                    }
                }

                await _context.SaveChangesAsync();

                // =========================
                // 5) تحديث حالة الطلب إلى "محوّل"
                // =========================
                request.IsConverted = true;
                request.Status = "Converted";
                request.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                // =========================
                // 6) إعادة حساب إجماليات الفاتورة
                // =========================
                await _docTotals.RecalcPurchaseInvoiceTotalsAsync(invoice.PIId);

                await tx.CommitAsync();

                // =========================
                // 7) الرد للواجهة
                // =========================
                return Json(new
                {
                    ok = true,
                    message = "تم تحويل الطلب إلى فاتورة شراء بنجاح.",
                    piId = invoice.PIId,
                    invoiceNumber = invoice.PIId.ToString()
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return BadRequest(new { ok = false, message = $"خطأ أثناء التحويل: {ex.Message}" });
            }
        }

        /// <summary>
        /// دالة مساعدة: الحصول على اسم المستخدم الحالي
        /// </summary>
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

            // لو مفيش يوزر → نرجع System
            return "System";
        }

        // ========================================================
        // POST: PurchaseRequests/BulkDelete
        // حذف مجموعة طلبات مختارة
        // ========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["Error"] = "من فضلك اختر على الأقل طلب واحد للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var ids = selectedIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var id) ? id : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();

            if (!ids.Any())
            {
                TempData["Error"] = "لم يتم العثور على أرقام صالحة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var requests = await _context.PurchaseRequests
                .Where(pr => ids.Contains(pr.PRId))
                .ToListAsync();

            if (!requests.Any())
            {
                TempData["Error"] = "لا توجد طلبات مطابقة للأرقام المختارة.";
                return RedirectToAction(nameof(Index));
            }

            _context.PurchaseRequests.RemoveRange(requests);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم حذف {requests.Count} من طلبات الشراء.";
            return RedirectToAction(nameof(Index));
        }

        // ========================================================
        // POST: PurchaseRequests/DeleteAll
        // حذف جميع طلبات الشراء
        // ========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var allRequests = await _context.PurchaseRequests.ToListAsync();

            if (!allRequests.Any())
            {
                TempData["Info"] = "لا توجد طلبات شراء لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            _context.PurchaseRequests.RemoveRange(allRequests);
            await _context.SaveChangesAsync();

            TempData["Success"] = "تم حذف جميع طلبات الشراء.";
            return RedirectToAction(nameof(Index));
        }

        // ========================================================
        // GET: PurchaseRequests/Export
        // تصدير نفس نتائج الفلترة إلى CSV
        // ========================================================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string format = "csv"
        )
        {
            dir = (dir == "asc") ? "asc" : "desc";

            var query = _context.PurchaseRequests
                .Include(pr => pr.Customer)
                .Include(pr => pr.Warehouse)
                .AsNoTracking()
                .AsQueryable();

            // نفس منطق البحث الموجود في Index
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();

                switch (searchBy)
                {
                    case "id":
                        if (int.TryParse(s, out var idVal))
                            query = query.Where(pr => pr.PRId == idVal);
                        else
                            query = query.Where(pr => pr.PRId.ToString().Contains(s));
                        break;

                    case "customer":
                        query = query.Where(pr =>
                            pr.Customer.CustomerName.Contains(s) ||
                            pr.CustomerId.ToString().Contains(s));
                        break;

                    case "warehouse":
                        query = query.Where(pr =>
                            pr.Warehouse.WarehouseName.Contains(s) ||
                            pr.WarehouseId.ToString().Contains(s));
                        break;

                    case "status":
                        query = query.Where(pr => pr.Status.Contains(s));
                        break;
                }
            }

            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                var from = fromDate.Value.Date;
                var to = toDate.Value.Date;
                query = query.Where(pr => pr.PRDate.Date >= from && pr.PRDate.Date <= to);
            }

            if (fromCode.HasValue)
                query = query.Where(pr => pr.PRId >= fromCode.Value);

            if (toCode.HasValue)
                query = query.Where(pr => pr.PRId <= toCode.Value);

            bool descending = (dir == "desc");
            query = descending
                ? query.OrderByDescending(pr => pr.PRDate).ThenByDescending(pr => pr.PRId)
                : query.OrderBy(pr => pr.PRDate).ThenBy(pr => pr.PRId);

            var data = await query.ToListAsync();

            // تجهيز CSV بسيط مع الأعمدة الجديدة (الإجماليات + التحويل)
            var lines = new List<string>
            {
                "PRId,PRDate,NeedByDate,Customer,Warehouse,Status,TotalQtyRequested,ExpectedItemsTotal,IsConverted"
            };

            foreach (var pr in data)
            {
                var line = string.Join(",",
                    pr.PRId,
                    pr.PRDate.ToString("yyyy-MM-dd"),
                    pr.NeedByDate?.ToString("yyyy-MM-dd") ?? "",
                    "\"" + (pr.Customer?.CustomerName ?? "") + "\"",
                    "\"" + (pr.Warehouse?.WarehouseName ?? "") + "\"",
                    "\"" + pr.Status + "\"",
                    pr.TotalQtyRequested,          // إجمالي الكمية المطلوبة
                    pr.ExpectedItemsTotal,         // إجمالي التكلفة المتوقعة
                    pr.IsConverted ? "1" : "0"     // تم التحويل؟ 1=نعم, 0=لا
                );

                lines.Add(line);
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, lines));
            var fileName = $"PurchaseRequests_{DateTime.Now:yyyyMMdd_HHmmss}.csv";

            return File(bytes, "text/csv", fileName);
        }
    }
}
