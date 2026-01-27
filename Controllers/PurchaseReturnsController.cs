using System;                                     // استخدام DateTime
using System.Collections.Generic;                 // List, Dictionary
using System.Linq;                                // أوامر LINQ
using System.Linq.Expressions;                   // Expression<Func<...>>
using System.Text;                                // StringBuilder لإنشاء CSV
using System.Threading.Tasks;                     // async / await
using Microsoft.AspNetCore.Mvc;                   // Controller, IActionResult
using Microsoft.AspNetCore.Mvc.Rendering;         // SelectListItem لقوائم البحث
using Microsoft.EntityFrameworkCore;              // AsNoTracking, Include, ToListAsync
using ERP.Data;                                   // AppDbContext
using ERP.Infrastructure;                         // PagedResult + ApplySearchSort
using ERP.Models;                                 // PurchaseReturn
using ERP.Services;                               // ILedgerPostingService

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر قائمة مرتجعات الشراء:
    /// عرض / بحث / فرز / حذف جماعي / تصدير.
    /// </summary>
    public class PurchaseReturnsController : Controller
    {
        // كائن الاتصال بقاعدة البيانات
        private readonly AppDbContext _context;
        private readonly ILedgerPostingService _ledgerPostingService;

        public PurchaseReturnsController(AppDbContext context, ILedgerPostingService ledgerPostingService)
        {
            _context = context;
            _ledgerPostingService = ledgerPostingService;
        }





        // دالة خاصة لتجهيز القوائم المنسدلة للموردين والمخازن
        private async Task PopulateDropDownsAsync(int? selectedCustomerId = null, int? selectedWarehouseId = null)
        {
            // قائمة الموردين للأوتوكومبليت (datalist)
            // هنستخدم نفس الأسماء اللى في الـ View: Id, Name, UserName, Phone, Address
            ViewBag.Customers = await _context.Customers
                .OrderBy(c => c.CustomerName)
                .Select(c => new
                {
                    Id = c.CustomerId,                      // كود العميل/المورد
                    Name = c.CustomerName,                  // اسم العميل/المورد
                    UserName = "",                          // ممكن تربطه بحساب مستخدم لو حبيت لاحقاً
                    Phone = c.Phone1,                       // التليفون الأساسي
                    Address = c.Address                     // العنوان
                })
                .ToListAsync();

            // قائمة المخازن للدروب داون
            ViewBag.Warehouses = new SelectList(
                await _context.Warehouses
                    .OrderBy(w => w.WarehouseName)
                    .ToListAsync(),
                "WarehouseId",                             // قيمة الـ option
                "WarehouseName",                           // النص الظاهر
                selectedWarehouseId                        // المخزن المختار (لو موجود)
            );
        }






        // =========================
        // Edit GET: فتح مرتجع شراء قديم للعرض/التعديل
        // =========================
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            // تحقق من رقم المرتجع
            if (id <= 0)
                return BadRequest("رقم مرتجع الشراء غير صالح.");

            // قراءة هيدر مرتجع الشراء + العميل + فاتورة الشراء المرجعية + السطور
            var model = await _context.PurchaseReturns
                .Include(pr => pr.Customer)           // ربط العميل
                .Include(pr => pr.RefPurchaseInvoice) // فاتورة الشراء المرجعية (لو موجودة)
                .Include(pr => pr.Lines)              // سطور المرتجع
                .AsNoTracking()                       // قراءة فقط (بدون تتبع)
                .FirstOrDefaultAsync(pr => pr.PRetId == id);

            // لو المرتجع مش موجود
            if (model == null)
                return NotFound();

            // تعبئة القوائم المنسدلة (العملاء + المخازن + فواتير الشراء ...)
            await PopulateDropDownsAsync();

            // فتح شاشة Edit.cshtml الخاصة بمرتجع الشراء
            return View(model);
        }

        // =========================
        // Edit POST: حفظ تعديل بيانات الهيدر
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, PurchaseReturn purchaseReturn)
        {
            // تأكد أن رقم المرتجع في الرابط = رقم المرتجع في الموديل
            if (id != purchaseReturn.PRetId)
                return NotFound();

            // التحقق من صلاحية البيانات
            if (!ModelState.IsValid)
            {
                // لو فيه أخطاء، رجّع نفس الشاشة مع إعادة تحميل القوائم
                await PopulateDropDownsAsync();
                return View(purchaseReturn);
            }

            try
            {
                // تحديث وقت آخر تعديل
                purchaseReturn.UpdatedAt = DateTime.Now;

                // تعليم الكيان أنه معدَّل
                _context.PurchaseReturns.Update(purchaseReturn);

                // حفظ التغييرات في قاعدة البيانات
                await _context.SaveChangesAsync();

                TempData["Msg"] = "تم تعديل مرتجع الشراء بنجاح.";
            }
            catch (DbUpdateConcurrencyException)
            {
                // لو حد مسح المرتجع أثناء التعديل
                bool exists = await _context.PurchaseReturns
                    .AnyAsync(e => e.PRetId == id);

                if (!exists)
                    return NotFound();

                // تعارض (نظريًا) في التعديل
                ModelState.AddModelError(
                    string.Empty,
                    "تعذر حفظ التعديل بسبب تعارض في البيانات. من فضلك أعد تحميل الصفحة ثم حاول مرة أخرى."
                );

                await PopulateDropDownsAsync();
                return View(purchaseReturn);
            }

            // بعد الحفظ نرجع لقائمة مرتجعات الشراء
            return RedirectToAction(nameof(Index));
        }





        // ---------------------------------------------------------
        // دالة خاصة: تجهيز الاستعلام مع كل الفلاتر + البحث + الترتيب
        // نستخدمها في Index و Export حتى لا نكرر الكود.
        // ---------------------------------------------------------
        private IQueryable<PurchaseReturn> BuildQuery(
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
            // (1) الاستعلام الأساسي من جدول المرتجعات + الجهة (Customer)
            IQueryable<PurchaseReturn> q =
                _context.PurchaseReturns
                        .Include(pr => pr.Customer)   // لعرض اسم الجهة
                        .AsNoTracking();

            // (2) فلتر رقم المرتجع من/إلى
            if (fromCode.HasValue)
                q = q.Where(pr => pr.PRetId >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(pr => pr.PRetId <= toCode.Value);

            // (3) فلتر التاريخ (تاريخ المرتجع)
            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                DateTime from = fromDate.Value.Date;
                DateTime to = toDate.Value.Date.AddDays(1).AddTicks(-1);
                q = q.Where(pr => pr.PRetDate >= from && pr.PRetDate <= to);
            }

            // (4) خرائط البحث والفرز — نفس نظام القوائم الموحد
            var stringFields =
                new Dictionary<string, Expression<Func<PurchaseReturn, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["customer"] = pr => pr.Customer.CustomerName ?? "",  // اسم الجهة
                    ["status"] = pr => pr.Status ?? ""                   // حالة المرتجع
                };

            var intFields =
                new Dictionary<string, Expression<Func<PurchaseReturn, int>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = pr => pr.PRetId,        // رقم المرتجع
                    ["customerId"] = pr => pr.CustomerId     // كود الجهة
                };

            var orderFields =
                new Dictionary<string, Expression<Func<PurchaseReturn, object>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PRetId"] = pr => pr.PRetId,                         // رقم المرتجع
                    ["PRetDate"] = pr => pr.PRetDate,                       // تاريخ المرتجع
                    ["Customer"] = pr => pr.Customer.CustomerName ?? "",    // اسم الجهة
                    ["CustomerId"] = pr => pr.CustomerId,                     // كود الجهة
                    ["Status"] = pr => pr.Status ?? "",                   // الحالة
                    ["IsPosted"] = pr => pr.IsPosted,                       // مرحّل؟
                    ["CreatedAt"] = pr => pr.CreatedAt,                      // تاريخ الإنشاء
                    ["UpdatedAt"] = pr => pr.UpdatedAt ?? pr.CreatedAt       // آخر تعديل
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
                defaultSearchBy: "all",
                defaultSortBy: "PRetDate"
            );

            return q;
        }

        // =========================================================
        // Index — قائمة مرتجعات الشراء
        // =========================================================
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",
            string? sort = "PRetDate",
            string? dir = "desc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            int page = 1,
            int pageSize = 50)
        {
            // تجهيز الاستعلام الموحد
            var q = BuildQuery(
                search, searchBy, sort, dir,
                useDateRange, fromDate, toDate,
                fromCode, toCode);

            // إنشاء نتيجة مقسّمة صفحات
            var model = await PagedResult<PurchaseReturn>.CreateAsync(q, page, pageSize);

            // تمرير حالة فلتر التاريخ للموديل (لاستخدامها في الواجهة)
            model.UseDateRange = useDateRange;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            // ViewBag للفلاتر
            ViewBag.Search = search ?? "";
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.Sort = sort ?? "PRetDate";
            ViewBag.Dir = (dir?.ToLower() == "asc") ? "asc" : "desc";

            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.DateField = "PRetDate";

            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalCount = model.TotalCount;

            // خيارات البحث (لو حبيت تستخدمها في واجهة أخرى)
            ViewBag.SearchOptions = new List<SelectListItem>
            {
                new("الكل",          "all")      { Selected = (ViewBag.SearchBy == "all")      },
                new("رقم المرتجع",   "id")       { Selected = (ViewBag.SearchBy == "id")       },
                new("اسم الجهة",     "customer") { Selected = (ViewBag.SearchBy == "customer") },
                new("الحالة",        "status")   { Selected = (ViewBag.SearchBy == "status")   },
            };

            // خيارات الترتيب
            ViewBag.SortOptions = new List<SelectListItem>
            {
                new("تاريخ المرتجع", "PRetDate") { Selected = (ViewBag.Sort == "PRetDate") },
                new("رقم المرتجع",   "PRetId")   { Selected = (ViewBag.Sort == "PRetId")   },
                new("اسم الجهة",     "Customer") { Selected = (ViewBag.Sort == "Customer") },
                new("الحالة",        "Status")   { Selected = (ViewBag.Sort == "Status")   },
                new("تاريخ الإنشاء", "CreatedAt"){ Selected = (ViewBag.Sort == "CreatedAt")}
            };

            return View(model);   // يعرض Views/PurchaseReturns/Index.cshtml
        }

        // فتح شاشة "مرتجع شراء جديد"
        [HttpGet]
        public async Task<IActionResult> Create(int? refPIId)
        {
            // متغير: كائن مرتجع جديد
            var model = new PurchaseReturn
            {
                PRetDate = DateTime.Today,                    // تاريخ المرتجع (اليوم)
                Status = "Draft",                             // حالة مبدئية: مسودة
                IsPosted = false,                             // غير مرحّل
                CreatedAt = DateTime.UtcNow,                  // تاريخ ووقت الإنشاء
                CreatedBy = User?.Identity?.Name ?? "System", // اسم المستخدم الحالي أو System
                RefPIId = refPIId                             // لو جاى من فاتورة شراء مرجعية
            };

            // لو جالي refPIId من فاتورة مشتريات، نحاول ناخد منها العميل والمخزن
            if (refPIId.HasValue)
            {
                // متغير: الفاتورة المرجعية
                var invoice = await _context.PurchaseInvoices
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.PIId == refPIId.Value);

                if (invoice != null)
                {
                    model.CustomerId = invoice.CustomerId;    // نفس العميل
                    model.WarehouseId = invoice.WarehouseId;  // نفس المخزن
                }
            }

            // تجهيز القوائم المنسدلة (الموردين + المخازن)
            await PopulateDropDownsAsync(model.CustomerId, model.WarehouseId);

            // نستخدم نفس الـ View بتاع عرض المرتجع (مثلاً Show.cshtml)
            // تأكد إن اسم الملف عندك "Show.cshtml" داخل Views/PurchaseReturns
            return View("Show", model);
        }


        // =========================================================
        // Export — تصدير مرتجعات الشراء (CSV بسيط يفتح في Excel)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy = "all",
            string? sort = "PRetDate",
            string? dir = "desc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string format = "excel")  // excel | csv (حالياً الاتنين CSV)
        {
            var q = BuildQuery(
                search, searchBy, sort, dir,
                useDateRange, fromDate, toDate,
                fromCode, toCode);

            var list = await q.ToListAsync();

            var sb = new StringBuilder();

            // عنوان الأعمدة
            sb.AppendLine("PRetId,PRetDate,CustomerId,CustomerName,WarehouseId,RefPIId,Status,IsPosted,PostedAt,CreatedBy,CreatedAt,UpdatedAt");

            // كل سطر مرتجع في CSV
            foreach (var pr in list)
            {
                string line = string.Join(",",
                    pr.PRetId,
                    pr.PRetDate.ToString("yyyy-MM-dd"),
                    pr.CustomerId,
                    (pr.Customer?.CustomerName ?? "").Replace(",", " "),
                    pr.WarehouseId,
                    pr.RefPIId?.ToString() ?? "",
                    (pr.Status ?? "").Replace(",", " "),
                    pr.IsPosted ? "1" : "0",
                    pr.PostedAt.HasValue ? pr.PostedAt.Value.ToString("yyyy-MM-dd HH:mm") : "",
                    (pr.CreatedBy ?? "").Replace(",", " "),
                    pr.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                    pr.UpdatedAt.HasValue ? pr.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm") : ""
                );

                sb.AppendLine(line);
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = "PurchaseReturns.csv";   // يكفي CSV — يفتح في Excel عادي
            const string contentType = "text/csv";

            return File(bytes, contentType, fileName);
        }

        // =========================================================
        // BulkDelete — حذف المرتجعات المحددة
        // يستقبل مصفوفة من أرقام المرتجعات (PRetId)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(int[] ids)
        {
            // لو مفيش أى ID مبعوت
            if (ids == null || ids.Length == 0)
            {
                TempData["Error"] = "لم يتم اختيار أى مرتجع للحذف.";
                return RedirectToAction(nameof(Index));
            }

            // جلب المرتجعات المطلوبة
            var items = await _context.PurchaseReturns
                                      .Where(pr => ids.Contains(pr.PRetId))
                                      .ToListAsync();

            if (items.Count == 0)
            {
                TempData["Error"] = "لم يتم العثور على المرتجعات المحددة.";
                return RedirectToAction(nameof(Index));
            }

            _context.PurchaseReturns.RemoveRange(items);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم حذف {items.Count} من مرتجعات الشراء المحددة.";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // DeleteAll — حذف جميع مرتجعات الشراء
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _context.PurchaseReturns.ToListAsync();

            if (all.Count == 0)
            {
                TempData["Error"] = "لا توجد مرتجعات لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            _context.PurchaseReturns.RemoveRange(all);
            await _context.SaveChangesAsync();

            TempData["Success"] = "تم حذف جميع مرتجعات الشراء.";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // API: جلب أصناف فاتورة الشراء عند إدخال رقم الفاتورة
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> GetInvoiceItems(int invoiceId)
        {
            if (invoiceId <= 0)
                return Json(new { ok = false, message = "رقم الفاتورة غير صحيح." });

            // جلب الفاتورة مع التحقق من وجودها
            var invoice = await _context.PurchaseInvoices
                .AsNoTracking()
                .FirstOrDefaultAsync(pi => pi.PIId == invoiceId);

            if (invoice == null)
                return Json(new { ok = false, message = "الفاتورة غير موجودة." });

            // جلب سطور الفاتورة مع بيانات الصنف
            var lines = await _context.PILines
                .AsNoTracking()
                .Include(l => l.Product)
                .Where(l => l.PIId == invoiceId)
                .OrderBy(l => l.LineNo)
                .Select(l => new
                {
                    lineNo = l.LineNo,
                    prodId = l.ProdId,
                    prodName = l.Product != null ? l.Product.ProdName : "",
                    qty = l.Qty,
                    batchNo = l.BatchNo ?? "",
                    expiry = l.Expiry.HasValue ? l.Expiry.Value.ToString("yyyy-MM-dd") : "",
                    priceRetail = l.PriceRetail,
                    unitCost = l.UnitCost,
                    purchaseDiscountPct = l.PurchaseDiscountPct
                })
                .ToListAsync();

            return Json(new
            {
                ok = true,
                invoiceId = invoiceId,
                customerId = invoice.CustomerId,
                warehouseId = invoice.WarehouseId,
                invoiceDate = invoice.PIDate.ToString("yyyy-MM-dd"),
                items = lines
            });
        }

        // =========================================================
        // POST: ترحيل مرتجع الشراء
        // =========================================================
        [HttpPost]
        public async Task<IActionResult> PostReturn(int id)
        {
            if (id <= 0)
                return Json(new { ok = false, message = "رقم المرتجع غير صحيح." });

            try
            {
                var purchaseReturn = await _context.PurchaseReturns
                    .AsNoTracking()
                    .FirstOrDefaultAsync(pr => pr.PRetId == id);

                if (purchaseReturn == null)
                    return Json(new { ok = false, message = "المرتجع غير موجود." });

                if (purchaseReturn.IsPosted)
                    return Json(new { ok = false, message = "هذا المرتجع مترحّل بالفعل." });

                var postedBy = User?.Identity?.Name ?? "SYSTEM";
                await _ledgerPostingService.PostPurchaseReturnAsync(id, postedBy);

                return Json(new { ok = true, message = "تم ترحيل المرتجع بنجاح." });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, message = $"حدث خطأ: {ex.Message}" });
            }
        }
    }
}
