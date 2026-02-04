using ERP.Data;                        // سياق قاعدة البيانات AppDbContext
using ERP.Infrastructure;              // PagedResult + ApplySearchSort
using ERP.Models;                      // الموديل SalesReturn
using ERP.Services;                    // ILedgerPostingService, DocumentTotalsService
using ERP.ViewModels;                  // SalesReturnHeaderDto
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;                     // علشان StringBuilder فى التصدير
using System.Threading.Tasks;

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر هيدر مرتجع البيع:
    /// - عرض قائمة مرتجعات البيع مع بحث / ترتيب / ترقيم.
    /// - عرض تفاصيل مرتجع واحد مع سطوره.
    /// - حذف مرتجع (مع حذف السطور بالكاسكيد).
    /// - حذف مجموعة مرتجعات / حذف الكل غير المُرحّل.
    /// - تصدير قائمة المرتجعات (Excel/CSV).
    /// </summary>
    public class SalesReturnsController : Controller
    {
        private readonly AppDbContext context;
        private readonly ILedgerPostingService _ledgerPostingService;
        private readonly DocumentTotalsService _docTotals;

        public SalesReturnsController(AppDbContext ctx, ILedgerPostingService ledgerPostingService, DocumentTotalsService docTotals)
        {
            context = ctx;
            _ledgerPostingService = ledgerPostingService;
            _docTotals = docTotals;
        }







        // =========================
        // دالة مساعدة: تحميل العملاء + المخازن للـ ViewBag
        // =========================
        private async Task PopulateDropDownsAsync(int? selectedCustomerId = null, int? selectedWarehouseId = null)
        {
            // قائمة العملاء للأوتوكومبليت في الهيدر
            // هنا بنرجّع نفس الشكل المستخدم في فاتورة البيع:
            // Name / Id / PolicyName / UserName / Phone / Address / CreditLimit
            var customers = await context.Customers
                .OrderBy(c => c.CustomerName)
                .Select(c => new
                {
                    Id = c.CustomerId,                 // كود العميل
                    Name = c.CustomerName,             // اسم العميل
                    PolicyName = "",                   // ممكن تربطها بسياسة العميل لاحقاً
                    UserName = "",                     // ممكن تربطها بالمستخدم المسئول عن العميل
                    Phone = c.Phone1,                  // التليفون
                    Address = c.Address,               // العنوان
                    CreditLimit = c.CreditLimit        // الحد الائتماني (لو موجود)
                })
                .ToListAsync();

            ViewBag.Customers = customers;

            // قائمة المخازن للدروب داون
            var warehouses = await context.Warehouses
                .OrderBy(w => w.WarehouseName)
                .ToListAsync();

            ViewBag.Warehouses = new SelectList(
                warehouses,
                "WarehouseId",          // اسم عمود المفتاح في جدول المخازن
                "WarehouseName",        // اسم المخزن المعروض
                selectedWarehouseId     // المخزن المختار (لو موجود)
            );
        }








        // =========================
        // GET: /SalesReturns/Create
        // فتح شاشة "مرتجع بيع جديد"
        // ممكن تيجي:
        // - بدون بارامتر (مرتجع مستقل)
        // - أو مع SalesInvoiceId لعمل مرتجع من فاتورة بيع
        // =========================
        [HttpGet]
        public async Task<IActionResult> Create(int? salesInvoiceId)
        {
            // متغير: نموذج المرتجع اللي هنبعته للفيو
            var model = new SalesReturn
            {
                // التاريخ والوقت الفعليين (مش هيظهروا في الشاشة إلا بعد الحفظ
                // لأن الفيو بيختبر SRId > 0 قبل ما يعرضهم)
                SRDate = DateTime.Today,
                SRTime = DateTime.Now.TimeOfDay,

                Status = "Draft",                           // غير مرحلة (يُعرض في الواجهة كـ "غير مرحلة" — القيد في DB: Draft/Posted/Cancelled)
                IsPosted = false,                           // لسه مش مترحّل
                CreatedAt = DateTime.UtcNow,                // وقت الإنشاء
                CreatedBy = User?.Identity?.Name ?? "system"
            };

            // لو جاي من فاتورة بيع: ننسخ العميل والمخزن والـ Id المرجعي
            if (salesInvoiceId.HasValue)
            {
                var invoice = await context.SalesInvoices
                    .AsNoTracking()
                    .FirstOrDefaultAsync(si => si.SIId == salesInvoiceId.Value);

                if (invoice == null)
                {
                    return NotFound(); // فاتورة البيع مش موجودة
                }

                model.SalesInvoiceId = invoice.SIId;    // ربط المرتجع بالفاتورة الأصلية
                model.CustomerId = invoice.CustomerId;  // نفس العميل
                model.WarehouseId = invoice.WarehouseId; // نفس المخزن
            }

            // تحميل العملاء والمخازن للواجهة
            await PopulateDropDownsAsync(model.CustomerId, model.WarehouseId);

            // تجهيز نظام الأسهم (نفس نظام فاتورة البيع)
            await FillSalesReturnNavAsync(model.SRId);

            // نعرض نفس شاشة الـ Show (زي ما عملنا في المشتريات / المبيعات)
            return View("Show", model);
        }






        // =========================
        // Edit — GET: فتح مرتجع بيع قديم للعرض/التعديل
        // =========================
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            // تحقق من رقم المرتجع
            if (id <= 0)
                return BadRequest("رقم مرتجع البيع غير صالح.");

            // قراءة هيدر المرتجع + العميل + الفاتورة الأصلية (لو موجودة) + السطور
            var model = await context.SalesReturns
                .Include(sr => sr.Customer)        // بيانات العميل
                .Include(sr => sr.SalesInvoice)    // الفاتورة الأصلية لو فيه ربط
                .Include(sr => sr.Lines)           // سطور المرتجع
                .AsNoTracking()                    // قراءة فقط بدون تتبّع
                .FirstOrDefaultAsync(sr => sr.SRId == id);

            if (model == null)
                return NotFound();                 // المرتجع غير موجود

            await PopulateDropDownsAsync();

            var prodIds = model.Lines.Select(l => l.ProdId).Distinct().ToList();
            var prodNames = await context.Products
                .AsNoTracking()
                .Where(p => prodIds.Contains(p.ProdId))
                .Select(p => new { p.ProdId, p.ProdName })
                .ToListAsync();
            ViewBag.ProdNames = prodNames.ToDictionary(x => x.ProdId, x => x.ProdName ?? "");

            // تجهيز نظام الأسهم (نفس نظام فاتورة البيع)
            await FillSalesReturnNavAsync(model.SRId);

            // رقم المرحلة للعرض "مرحلة X" عند تحميل مرتجع مرحّل (مثل المبيعات)
            if (model.IsPosted)
            {
                int stage = await context.LedgerEntries
                    .AsNoTracking()
                    .Where(e => e.SourceType == LedgerSourceType.SalesReturn && e.SourceId == id && e.LineNo == 1 && e.PostVersion > 0)
                    .MaxAsync(e => (int?)e.PostVersion) ?? 1;
                ViewBag.ReturnStage = stage;
            }

            return View("Show", model);
        }



        // =========================
        // Edit — POST: حفظ تعديل هيدر مرتجع البيع مع RowVersion
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, SalesReturn salesReturn)
        {
            // تأكد أن رقم المرتجع في الرابط هو نفس الموجود في الموديل
            if (id != salesReturn.SRId)
                return NotFound();

            // تحقق إضافي على نسبة خصم الهيدر (0..100)
            if (salesReturn.HeaderDiscountPercent < 0 || salesReturn.HeaderDiscountPercent > 100)
            {
                ModelState.AddModelError(
                    nameof(SalesReturn.HeaderDiscountPercent),
                    "نسبة خصم الهيدر يجب أن تكون بين 0 و 100");
            }

            // لو فيه أخطاء تحقق نرجع لنفس الفورم
            if (!ModelState.IsValid)
            {
                await PopulateDropDownsAsync();
                return View(salesReturn);
            }

            try
            {
                // تحديث وقت آخر تعديل
                salesReturn.UpdatedAt = DateTime.Now;

                // إعداد RowVersion الأصلي للتعامل مع التعارض (Concurrency)
                context.Entry(salesReturn)
                        .Property(x => x.RowVersion)
                        .OriginalValue = salesReturn.RowVersion;

                // تحديث الكيان في الـ DbContext
                context.Update(salesReturn);

                // حفظ التغييرات فعلياً في SQL Server
                await context.SaveChangesAsync();

                TempData["Msg"] = "تم تعديل مرتجع البيع بنجاح.";
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateConcurrencyException)
            {
                // لو المرتجع اختفى أثناء الحفظ (اتحذف مثلاً)
                bool exists = await context.SalesReturns.AnyAsync(e => e.SRId == id);
                if (!exists)
                    return NotFound();

                // تعارض حقيقي: حد تاني عدّل نفس المرتجع في نفس الوقت
                ModelState.AddModelError(
                    string.Empty,
                    "تعذّر حفظ التعديلات بسبب تعديل متزامن على نفس مرتجع البيع. أعد تحميل الصفحة وحاول مرة أخرى.");

                await PopulateDropDownsAsync();
                return View(salesReturn);
            }
        }








        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> SaveHeader([FromBody] SalesReturnHeaderDto dto)
        {
            if (dto == null)
                return BadRequest("حدث خطأ في البيانات المرسلة.");
            if (dto.CustomerId <= 0)
                return BadRequest("يجب اختيار العميل قبل حفظ المرتجع.");
            if (dto.WarehouseId <= 0)
                return BadRequest("يجب اختيار المخزن قبل حفظ المرتجع.");
            var now = DateTime.Now;
            var userName = User?.Identity?.Name ?? "system";
            if (dto.SRId == 0)
            {
                var entity = new SalesReturn
                {
                    SRDate = now.Date,
                    SRTime = now.TimeOfDay,
                    CustomerId = dto.CustomerId,
                    WarehouseId = dto.WarehouseId,
                    SalesInvoiceId = dto.SalesInvoiceId > 0 ? dto.SalesInvoiceId : null,
                    Status = "Draft",                       // القيد في DB يسمح فقط: Draft, Posted, Cancelled
                    IsPosted = false,
                    CreatedAt = now,
                    CreatedBy = userName
                };
                context.SalesReturns.Add(entity);
                await context.SaveChangesAsync();
                return Json(new { success = true, srId = entity.SRId, returnNumber = entity.SRId.ToString(), returnDate = entity.SRDate.ToString("yyyy/MM/dd"), returnTime = DateTime.Today.Add(entity.SRTime).ToString("HH:mm"), status = entity.Status, isPosted = entity.IsPosted, createdBy = entity.CreatedBy });
            }
            var existing = await context.SalesReturns.FirstOrDefaultAsync(sr => sr.SRId == dto.SRId);
            if (existing == null)
                return NotFound("لم يتم العثور على المرتجع المطلوب.");
            if (existing.IsPosted)
                return BadRequest("لا يمكن تعديل مرتجع تم ترحيله.");
            existing.CustomerId = dto.CustomerId;
            existing.WarehouseId = dto.WarehouseId;
            existing.SalesInvoiceId = dto.SalesInvoiceId > 0 ? dto.SalesInvoiceId : null;
            existing.UpdatedAt = now;
            await context.SaveChangesAsync();

            return Json(new { success = true, srId = existing.SRId, returnNumber = existing.SRId.ToString(), returnDate = existing.SRDate.ToString("yyyy/MM/dd"), returnTime = DateTime.Today.Add(existing.SRTime).ToString("HH:mm"), status = existing.Status, isPosted = existing.IsPosted, createdBy = existing.CreatedBy });
        }

        // =========================================================
        // دالة مساعدة: تبنى الاستعلام الأساسى مع البحث والترتيب
        // =========================================================
        private IQueryable<SalesReturn> BuildQuery(
            string? search,
            string? searchBy,
            string? sort,
            string? dir)
        {
            // استعلام أساسى بدون تتبع لسرعة التقارير
            IQueryable<SalesReturn> q = context.SalesReturns.AsNoTracking();

            // الحقول النصية للبحث كسلسلة نصية
            var stringFields =
                new Dictionary<string, Expression<Func<SalesReturn, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["status"] = x => x.Status,     // حالة المستند
                    ["createdby"] = x => x.CreatedBy   // المستخدم الذى أنشأ المرتجع
                };

            // الحقول الرقمية (int) للبحث العددى
            var intFields =
                new Dictionary<string, Expression<Func<SalesReturn, int>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = x => x.SRId,         // رقم المرتجع
                    ["customer"] = x => x.CustomerId,   // كود العميل
                    ["warehouse"] = x => x.WarehouseId   // كود المخزن
                };

            // حقول الترتيب
            var orderFields =
                new Dictionary<string, Expression<Func<SalesReturn, object>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SRDate"] = x => x.SRDate,                       // التاريخ
                    ["SRId"] = x => x.SRId,                         // رقم المرتجع
                    ["CustomerId"] = x => x.CustomerId,                   // العميل
                    ["WarehouseId"] = x => x.WarehouseId,                  // المخزن
                    ["NetTotal"] = x => x.NetTotal,                     // الصافى
                    ["Status"] = x => x.Status ?? string.Empty,       // الحالة
                    ["CreatedAt"] = x => x.CreatedAt                     // تاريخ الإنشاء
                };

            // تطبيق إكستنشن البحث + الترتيب الموحد
            q = q.ApplySearchSort(
                search: search,
                searchBy: searchBy,
                sort: sort,
                dir: dir,
                stringFields: stringFields,
                intFields: intFields,
                orderFields: orderFields,
                defaultSearchBy: "all",
                defaultSortBy: "SRDate");

            return q;
        }

        // =========================================================
        // GET: /SalesReturns
        // عرض قائمة مرتجعات البيع مع البحث / الترتيب / الترقيم
        // =========================================================
        public async Task<IActionResult> Index(
            string? search,                // نص البحث
            string? searchBy = "all",      // all | id | customer | warehouse | status | createdby
            string? sort = "SRDate",       // عمود الترتيب
            string? dir = "desc",          // asc | desc
            int page = 1,                  // رقم الصفحة
            int pageSize = 50)             // حجم الصفحة
        {
            // (1) بناء الاستعلام حسب الفلاتر
            var q = BuildQuery(search, searchBy, sort, dir);

            // (2) إنشاء PagedResult جاهز للفيو
            var model = await PagedResult<SalesReturn>.CreateAsync(q, page, pageSize);

            // (3) إعداد خيارات البحث للبارشال _IndexFilters
            ViewBag.SearchOptions = new List<SelectListItem>
            {
                new("الكل",          "all")       { Selected = (searchBy ?? "all")
                                                        .Equals("all", StringComparison.OrdinalIgnoreCase) },
                new("رقم المرتجع",   "id")        { Selected = searchBy == "id" },
                new("العميل",        "customer")  { Selected = searchBy == "customer" },
                new("المخزن",        "warehouse") { Selected = searchBy == "warehouse" },
                new("الحالة",        "status")    { Selected = searchBy == "status" },
                new("أنشأه",         "createdby") { Selected = searchBy == "createdby" },
            };

            // خيارات الترتيب
            ViewBag.SortOptions = new List<SelectListItem>
            {
                new("التاريخ",       "SRDate")     { Selected = sort == "SRDate" },
                new("رقم المرتجع",   "SRId")       { Selected = sort == "SRId" },
                new("العميل",        "CustomerId") { Selected = sort == "CustomerId" },
                new("المخزن",        "WarehouseId"){ Selected = sort == "WarehouseId" },
                new("الصافي",        "NetTotal")   { Selected = sort == "NetTotal" },
                new("الحالة",        "Status")     { Selected = sort == "Status" },
                new("أُنشئ في",      "CreatedAt")  { Selected = sort == "CreatedAt" },
            };

            // (4) تخزين حالة الفلاتر فى ViewBag ليستعملها الفيو
            ViewBag.Search = search ?? "";
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.Sort = sort ?? "SRDate";
            ViewBag.Dir = (dir?.ToLower() == "asc") ? "asc" : "desc";

            // قيم الترقيم (لو احتجناها فى الفيو أو البارشال)
            ViewBag.Page = model.PageNumber;
            ViewBag.PageSize = model.PageSize;
            ViewBag.TotalPages = model.TotalPages;
            ViewBag.RangeStart = model.TotalCount == 0
                                 ? 0
                                 : ((model.PageNumber - 1) * model.PageSize) + 1;
            ViewBag.RangeEnd = Math.Min(model.PageNumber * model.PageSize, model.TotalCount);
            ViewBag.TotalRows = model.TotalCount;

            // نرجع الموديل الكامل PagedResult لسطر الفيو
            return View(model);
        }

    
     






        // =========================================================
        // GET: /SalesReturns/Delete/{id}
        // صفحة تأكيد الحذف (تعرض ملخص المستند وعدد السطور)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Delete(int id)   // رقم المرتجع المطلوب حذفه
        {
            if (id <= 0) return NotFound();

            // جلب أقل بيانات نحتاجها لعرضها فى صفحة التأكيد
            var m = await context.SalesReturns
                .AsNoTracking()
                .Where(x => x.SRId == id)
                .Select(x => new SalesReturn
                {
                    SRId = x.SRId,
                    SRDate = x.SRDate,
                    SRTime = x.SRTime,
                    CustomerId = x.CustomerId,
                    WarehouseId = x.WarehouseId,
                    NetTotal = x.NetTotal,
                    Status = x.Status,
                    IsPosted = x.IsPosted,
                    CreatedBy = x.CreatedBy,
                    CreatedAt = x.CreatedAt
                })
                .FirstOrDefaultAsync();

            if (m == null) return NotFound();

            // عدد السطور لإظهارها فقط فى صفحة التأكيد
            ViewBag.LinesCount = await context.SalesReturnLines
                .Where(l => l.SRId == id)
                .CountAsync();

            return View(m);
        }

        // =========================================================
        // POST: /SalesReturns/Delete/{id}
        // حذف عميق من القائمة (مثل المبيعات): يُسمح بحذف المرحّل من الإندكس فقط.
        // 1) عكس المخزون (تقليل QtyOnHand) 2) StockFifoMap 3) StockLedger 4) عكس القيود المحاسبية 5) حذف الهيدر
        // =========================================================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (id <= 0) return NotFound();

            var result = await TryDeleteSalesReturnDeepAsync(id);

            if (result.Status == DeleteReturnStatus.Deleted)
                TempData["ok"] = "تم حذف المرتجع بنجاح (مع تحديث المخزون وعكس الأثر المحاسبي).";
            else
                TempData["error"] = $"تعذر حذف المرتجع رقم {id}: {result.Message ?? "خطأ غير معروف"}";

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // POST: /SalesReturns/BulkDelete
        // حذف مجموعة مرتجعات (مرحلة أو غير مرحلة) من القائمة — حذف عميق مثل المبيعات
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["error"] = "من فضلك اختر على الأقل مرتجعاً واحداً للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var ids = selectedIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var n) ? (int?)n : null)
                .Where(n => n.HasValue)
                .Select(n => n!.Value)
                .Distinct()
                .ToList();

            if (ids.Count == 0)
            {
                TempData["error"] = "لم يتم التعرف على أى أرقام مرتجعات صحيحة.";
                return RedirectToAction(nameof(Index));
            }

            var existingIds = await context.SalesReturns
                .Where(x => ids.Contains(x.SRId))
                .Select(x => x.SRId)
                .ToListAsync();

            if (existingIds.Count == 0)
            {
                TempData["error"] = "لم يتم العثور على المرتجعات المحددة في قاعدة البيانات.";
                return RedirectToAction(nameof(Index));
            }

            int deletedCount = 0;
            int failedCount = 0;
            var failedIds = new List<int>();

            foreach (var sid in existingIds)
            {
                var result = await TryDeleteSalesReturnDeepAsync(sid);
                if (result.Status == DeleteReturnStatus.Deleted)
                    deletedCount++;
                else
                {
                    failedCount++;
                    failedIds.Add(sid);
                }
            }

            if (deletedCount > 0)
            {
                TempData["ok"] = $"تم حذف {deletedCount} مرتجع (مع تحديث المخزون وعكس الأثر المحاسبي).";
                if (failedIds.Count > 0)
                    TempData["error"] = $"فشل حذف المرتجعات: {string.Join(", ", failedIds)}";
            }
            else
                TempData["error"] = failedCount > 0
                    ? $"لم يتم حذف أي مرتجع. فشل: {string.Join(", ", failedIds)}"
                    : "لم يتم حذف أي مرتجع.";

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // POST: /SalesReturns/DeleteAll
        // حذف كل مرتجعات البيع (مرحلة أو غير مرحلة) — حذف عميق مثل المبيعات
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var allIds = await context.SalesReturns.Select(x => x.SRId).ToListAsync();

            if (allIds.Count == 0)
            {
                TempData["error"] = "لا توجد مرتجعات بيع لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            int deletedCount = 0;
            int failedCount = 0;
            var failedIds = new List<int>();

            foreach (var id in allIds)
            {
                var result = await TryDeleteSalesReturnDeepAsync(id);
                if (result.Status == DeleteReturnStatus.Deleted)
                    deletedCount++;
                else
                {
                    failedCount++;
                    failedIds.Add(id);
                }
            }

            if (deletedCount > 0)
            {
                TempData["ok"] = $"تم حذف {deletedCount} مرتجع (مع تحديث المخزون وعكس الأثر المحاسبي).";
                if (failedIds.Count > 0)
                    TempData["error"] = $"فشل حذف المرتجعات: {string.Join(", ", failedIds)}";
            }
            else
                TempData["error"] = failedCount > 0
                    ? $"لم يتم حذف أي مرتجع. فشل: {string.Join(", ", failedIds)}"
                    : "لم يتم حذف أي مرتجع.";

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // GET: /SalesReturns/Export
        // تصدير قائمة مرتجعات البيع (Excel/CSV) بنفس فلاتر البحث
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy = "all",
            string? sort = "SRDate",
            string? dir = "desc",
            string? format = "excel")
        {
            // نفس منطق الفلاتر المستخدم فى Index
            var q = BuildQuery(search, searchBy, sort, dir);

            var list = await q
                .OrderBy(sr => sr.SRDate)
                .ThenBy(sr => sr.SRId)
                .ToListAsync();

            var sb = new StringBuilder();

            // العناوين
            sb.AppendLine("ReturnId,Date,Time,CustomerId,WarehouseId,SalesInvoiceId,NetTotal,Status,IsPosted");

            // الصفوف
            foreach (var x in list)
            {
                // وقت المرتجع فى شكل hh:mm
                var timeStr = x.SRTime.ToString(@"hh\:mm");

                var line = string.Join(",",
                    x.SRId,
                    x.SRDate.ToString("yyyy-MM-dd"),
                    timeStr,
                    x.CustomerId,
                    x.WarehouseId,
                    x.SalesInvoiceId?.ToString() ?? "",
                    x.NetTotal.ToString("0.00"),
                    (x.Status ?? "").Replace(",", " "),
                    x.IsPosted ? "Yes" : "No"
                );

                sb.AppendLine(line);
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());

            // حالياً بنصدر CSV حتى لو المستخدم اختار Excel (نفس الفكرة اللى عملناها فى فواتير البيع)
            var ext = (format ?? "excel").ToLower() == "csv" ? "csv" : "csv";
            var fileName = $"SalesReturns_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}";

            return File(bytes, "text/csv", fileName);
        }

        // =========================================================
        // API: جلب أصناف فاتورة البيع عند إدخال رقم الفاتورة
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> GetInvoiceItems(int invoiceId)
        {
            if (invoiceId <= 0)
                return Json(new { ok = false, message = "رقم الفاتورة غير صحيح." });

            // جلب الفاتورة مع التحقق من وجودها
            var invoice = await context.SalesInvoices
                .AsNoTracking()
                .FirstOrDefaultAsync(si => si.SIId == invoiceId);

            if (invoice == null)
                return Json(new { ok = false, message = "الفاتورة غير موجودة." });

            // جلب سطور الفاتورة مع بيانات الصنف
            var lines = await context.SalesInvoiceLines
                .AsNoTracking()
                .Include(l => l.Product)
                .Where(l => l.SIId == invoiceId)
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
                    unitSalePrice = l.UnitSalePrice,
                    disc1Percent = l.Disc1Percent,
                    disc2Percent = l.Disc2Percent,
                    disc3Percent = l.Disc3Percent,
                    lineNetTotal = l.LineNetTotal
                })
                .ToListAsync();

            // كميات مرتجعة سابقاً من كل سطر (من جميع المرتجعات المرتبطة بنفس الفاتورة)
            var returnedByLine = await context.SalesReturnLines
                .Where(l => l.SalesInvoiceId == invoiceId)
                .GroupBy(l => l.SalesInvoiceLineNo)
                .Select(g => new { LineNo = g.Key, Returned = g.Sum(l => l.Qty) })
                .ToListAsync();
            var returnedDict = returnedByLine.Where(x => x.LineNo.HasValue).ToDictionary(x => x.LineNo!.Value, x => x.Returned);

            var items = lines.Select(l => new
            {
                l.lineNo,
                l.prodId,
                l.prodName,
                l.qty,
                alreadyReturned = returnedDict.GetValueOrDefault(l.lineNo, 0),
                remaining = l.qty - returnedDict.GetValueOrDefault(l.lineNo, 0),
                l.batchNo,
                l.expiry,
                l.priceRetail,
                l.unitSalePrice,
                l.disc1Percent,
                l.disc2Percent,
                l.disc3Percent,
                l.lineNetTotal
            }).ToList();

            return Json(new
            {
                ok = true,
                invoiceId = invoiceId,
                customerId = invoice.CustomerId,
                warehouseId = invoice.WarehouseId,
                invoiceDate = invoice.SIDate.ToString("yyyy-MM-dd"),
                items = items
            });
        }

        private async Task<decimal> GetAverageCostAsync(int prodId, int warehouseId)
        {
            var avg = await context.StockLedger
                .Where(sl => sl.ProdId == prodId && sl.WarehouseId == warehouseId && sl.QtyIn > 0)
                .AverageAsync(sl => (decimal?)sl.UnitCost);
            return avg ?? 0m;
        }

        // =========================================================
        // AddLineJson — إضافة سطر لمرتجع البيع + StockLedger (QtyIn) + StockBatch
        // مرتجع البيع = عكس البيع → زيادة المخزون
        // =========================================================
        public class AddLineJsonDto
        {
            public int SRId { get; set; }
            public int ProdId { get; set; }
            public int Qty { get; set; }
            public string? BatchNo { get; set; }
            public string? ExpiryText { get; set; }
            public decimal PriceRetail { get; set; }
            public decimal Disc1Percent { get; set; }
            public int? SalesInvoiceId { get; set; }
            public int? SalesInvoiceLineNo { get; set; }
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> AddLineJson([FromBody] AddLineJsonDto dto)
        {
            if (dto == null || dto.SRId <= 0 || dto.ProdId <= 0 || dto.Qty <= 0)
                return BadRequest(new { ok = false, message = "بيانات السطر غير صحيحة." });

            await using var tx = await context.Database.BeginTransactionAsync();
            try
            {
                var ret = await context.SalesReturns.Include(sr => sr.Lines).FirstOrDefaultAsync(sr => sr.SRId == dto.SRId);
                if (ret == null) { await tx.RollbackAsync(); return NotFound(new { ok = false, message = "المرتجع غير موجود." }); }
                if (ret.IsPosted) { await tx.RollbackAsync(); return BadRequest(new { ok = false, message = "لا يمكن تعديل مرتجع مترحّل." }); }
                if (ret.WarehouseId <= 0) { await tx.RollbackAsync(); return BadRequest(new { ok = false, message = "يجب اختيار المخزن." }); }

                // التحقق من الكمية مقابل الفاتورة والمرتجعات السابقة من نفس السطر
                if (dto.SalesInvoiceId.HasValue && dto.SalesInvoiceId.Value > 0 && dto.SalesInvoiceLineNo.HasValue)
                {
                    var invoiceLine = await context.SalesInvoiceLines
                        .AsNoTracking()
                        .FirstOrDefaultAsync(l => l.SIId == dto.SalesInvoiceId.Value && l.LineNo == dto.SalesInvoiceLineNo.Value);
                    if (invoiceLine == null)
                    {
                        await tx.RollbackAsync();
                        return BadRequest(new { ok = false, message = "سطر الفاتورة غير موجود." });
                    }
                    int invoiceQty = invoiceLine.Qty;
                    int alreadyReturned = await context.SalesReturnLines
                        .Where(l => l.SalesInvoiceId == dto.SalesInvoiceId && l.SalesInvoiceLineNo == dto.SalesInvoiceLineNo)
                        .SumAsync(l => l.Qty);
                    int remaining = invoiceQty - alreadyReturned;
                    if (remaining <= 0)
                    {
                        await tx.RollbackAsync();
                        return BadRequest(new { ok = false, message = "تم إرجاع كامل كمية هذا السطر من الفاتورة مسبقاً. لا يمكن إضافة المزيد." });
                    }
                    if (dto.Qty > remaining)
                    {
                        await tx.RollbackAsync();
                        return BadRequest(new { ok = false, message = $"الكمية المرتجعة ({dto.Qty}) تتجاوز المتبقي من هذا السطر في الفاتورة ({remaining}). الكمية في الفاتورة: {invoiceQty}، تم إرجاع {alreadyReturned} سابقاً." });
                    }
                }

                DateTime? exp = null;
                if (!string.IsNullOrWhiteSpace(dto.ExpiryText))
                {
                    var s = dto.ExpiryText.Trim();
                    if (DateTime.TryParse(s, out var p)) exp = p.Date;
                    else
                    {
                        var parts = s.Split('/');
                        if (parts.Length == 2 && int.TryParse(parts[0], out var mm) && int.TryParse(parts[1], out var yyyy) && mm >= 1 && mm <= 12)
                            exp = new DateTime(yyyy, mm, 1).Date;
                    }
                }

                var batchNo = string.IsNullOrWhiteSpace(dto.BatchNo) ? null : dto.BatchNo.Trim();
                var disc1 = Math.Max(0, Math.Min(100, dto.Disc1Percent));
                var unitPrice = Math.Max(0, dto.PriceRetail);
                var totalBefore = dto.Qty * unitPrice;
                var discVal = totalBefore * (disc1 / 100m);
                var totalAfter = totalBefore - discVal;
                var taxVal = 0m;
                var netLine = totalAfter + taxVal;

                var nextLineNo = (ret.Lines.Any() ? ret.Lines.Max(x => x.LineNo) : 0) + 1;
                var line = new SalesReturnLine
                {
                    SRId = dto.SRId,
                    LineNo = nextLineNo,
                    ProdId = dto.ProdId,
                    Qty = dto.Qty,
                    PriceRetail = unitPrice,
                    Disc1Percent = disc1,
                    Disc2Percent = 0,
                    Disc3Percent = 0,
                    DiscountValue = discVal,
                    UnitSalePrice = unitPrice,
                    LineTotalAfterDiscount = totalAfter,
                    TaxPercent = 0,
                    TaxValue = taxVal,
                    LineNetTotal = netLine,
                    BatchNo = batchNo,
                    Expiry = exp,
                    SalesInvoiceId = dto.SalesInvoiceId > 0 ? dto.SalesInvoiceId : null,
                    SalesInvoiceLineNo = dto.SalesInvoiceLineNo
                };
                context.SalesReturnLines.Add(line);
                await context.SaveChangesAsync();

                var avgCost = await GetAverageCostAsync(dto.ProdId, ret.WarehouseId);
                var now = DateTime.UtcNow;
                context.StockLedger.Add(new StockLedger
                {
                    TranDate = now,
                    WarehouseId = ret.WarehouseId,
                    ProdId = dto.ProdId,
                    BatchNo = batchNo ?? "",
                    Expiry = exp,
                    QtyIn = dto.Qty,
                    QtyOut = 0,
                    UnitCost = avgCost,
                    RemainingQty = dto.Qty,
                    SourceType = "SalesReturn",
                    SourceId = dto.SRId,
                    SourceLine = nextLineNo,
                    Note = "Sales Return Line"
                });
                await context.SaveChangesAsync();

                if (!string.IsNullOrWhiteSpace(batchNo) && exp.HasValue)
                {
                    var sb = await context.StockBatches.FirstOrDefaultAsync(b =>
                        b.WarehouseId == ret.WarehouseId && b.ProdId == dto.ProdId && b.BatchNo == batchNo && b.Expiry.HasValue && b.Expiry.Value.Date == exp.Value.Date);
                    if (sb != null)
                    {
                        sb.QtyOnHand += dto.Qty;
                        sb.UpdatedAt = now;
                        sb.Note = $"SR:{dto.SRId} Line:{nextLineNo} (+{dto.Qty})";
                    }
                    else
                    {
                        context.StockBatches.Add(new StockBatch
                        {
                            WarehouseId = ret.WarehouseId,
                            ProdId = dto.ProdId,
                            BatchNo = batchNo,
                            Expiry = exp.Value,
                            QtyOnHand = dto.Qty,
                            UpdatedAt = now,
                            Note = $"SR:{dto.SRId} Line:{nextLineNo}"
                        });
                    }
                    await context.SaveChangesAsync();
                }

                await _docTotals.RecalcSalesReturnTotalsAsync(dto.SRId);
                await context.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }

            var linesNow = await context.SalesReturnLines.Where(l => l.SRId == dto.SRId).OrderBy(l => l.LineNo).ToListAsync();
            var prodIds = linesNow.Select(l => l.ProdId).Distinct().ToList();
            var prodMap = await context.Products.AsNoTracking().Where(p => prodIds.Contains(p.ProdId)).Select(p => new { p.ProdId, p.ProdName }).ToDictionaryAsync(x => x.ProdId, x => x.ProdName ?? "");
            var linesDto = linesNow.Select(l => new
            {
                lineNo = l.LineNo,
                prodId = l.ProdId,
                prodName = prodMap.TryGetValue(l.ProdId, out var n) ? n : "",
                qty = l.Qty,
                priceRetail = l.PriceRetail,
                disc1Percent = l.Disc1Percent,
                batchNo = l.BatchNo,
                expiry = l.Expiry?.ToString("yyyy-MM-dd"),
                lineNetTotal = l.LineNetTotal
            }).ToList();
            var h = await context.SalesReturns.AsNoTracking().FirstAsync(sr => sr.SRId == dto.SRId);
            return Json(new { ok = true, message = "تمت إضافة السطر.", lines = linesDto, totals = new { totalBeforeDiscount = h.TotalBeforeDiscount, totalAfterDiscountBeforeTax = h.TotalAfterDiscountBeforeTax, taxAmount = h.TaxAmount, netTotal = h.NetTotal } });
        }

        // =========================================================
        // RemoveLineJson — حذف سطر من مرتجع البيع + عكس StockLedger/StockBatch
        // =========================================================
        public class RemoveLineJsonDto { public int SRId { get; set; } public int LineNo { get; set; } }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> RemoveLineJson([FromBody] RemoveLineJsonDto dto)
        {
            if (dto == null || dto.SRId <= 0 || dto.LineNo <= 0)
                return BadRequest(new { ok = false, message = "بيانات المسح غير صحيحة." });

            await using var tx = await context.Database.BeginTransactionAsync();
            try
            {
                var ret = await context.SalesReturns.FirstOrDefaultAsync(sr => sr.SRId == dto.SRId);
                if (ret == null) { await tx.RollbackAsync(); return NotFound(new { ok = false, message = "المرتجع غير موجود." }); }
                if (ret.IsPosted) { await tx.RollbackAsync(); return BadRequest(new { ok = false, message = "المرتجع مترحّل ومقفول. استخدم زر (فتح المرتجع) أولاً." }); }

                var line = await context.SalesReturnLines.FirstOrDefaultAsync(l => l.SRId == dto.SRId && l.LineNo == dto.LineNo);
                if (line == null) { await tx.RollbackAsync(); return NotFound(new { ok = false, message = "السطر غير موجود." }); }

                // عكس المخزون فقط لو السطر كان له حركات (مرتجع كان مُرحّل ثم فُتح): مسح سطر المرتجع = تقليل المخزون (عكس البيع)
                var ledgers = await context.StockLedger.Where(x => x.SourceType == "SalesReturn" && x.SourceId == dto.SRId && x.SourceLine == dto.LineNo).ToListAsync();
                if (ledgers.Any())
                {
                    var batchNo = string.IsNullOrWhiteSpace(line.BatchNo) ? null : line.BatchNo.Trim();
                    var exp = line.Expiry?.Date;
                    if (!string.IsNullOrWhiteSpace(batchNo) && exp.HasValue)
                    {
                        var sb = await context.StockBatches.FirstOrDefaultAsync(b =>
                            b.WarehouseId == ret.WarehouseId && b.ProdId == line.ProdId && b.BatchNo == batchNo && b.Expiry.HasValue && b.Expiry.Value.Date == exp.Value);
                        if (sb != null) { sb.QtyOnHand -= line.Qty; sb.UpdatedAt = DateTime.UtcNow; sb.Note = $"SR:{dto.SRId} Line:{dto.LineNo} (-{line.Qty})"; }
                    }
                    context.StockLedger.RemoveRange(ledgers);
                }
                context.SalesReturnLines.Remove(line);
                await context.SaveChangesAsync();
                await _docTotals.RecalcSalesReturnTotalsAsync(dto.SRId);
                await context.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }

            var linesNow = await context.SalesReturnLines.Where(l => l.SRId == dto.SRId).OrderBy(l => l.LineNo).ToListAsync();
            var prodIds = linesNow.Select(l => l.ProdId).Distinct().ToList();
            var prodMap = await context.Products.AsNoTracking().Where(p => prodIds.Contains(p.ProdId)).Select(p => new { p.ProdId, p.ProdName }).ToDictionaryAsync(x => x.ProdId, x => x.ProdName ?? "");
            var linesDto = linesNow.Select(l => new { lineNo = l.LineNo, prodId = l.ProdId, prodName = prodMap.TryGetValue(l.ProdId, out var n) ? n : "", qty = l.Qty, priceRetail = l.PriceRetail, disc1Percent = l.Disc1Percent, batchNo = l.BatchNo, expiry = l.Expiry?.ToString("yyyy-MM-dd"), lineNetTotal = l.LineNetTotal }).ToList();
            var h = await context.SalesReturns.AsNoTracking().FirstAsync(sr => sr.SRId == dto.SRId);
            return Json(new { ok = true, message = "تم حذف السطر.", lines = linesDto, totals = new { totalBeforeDiscount = h.TotalBeforeDiscount, totalAfterDiscountBeforeTax = h.TotalAfterDiscountBeforeTax, taxAmount = h.TaxAmount, netTotal = h.NetTotal } });
        }

        // =========================================================
        // ClearLinesJson — مسح كل سطور مرتجع البيع + عكس StockLedger/StockBatch
        // =========================================================
        public class ClearLinesJsonDto { public int SRId { get; set; } }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> ClearLinesJson([FromBody] ClearLinesJsonDto dto)
        {
            if (dto == null || dto.SRId <= 0)
                return BadRequest(new { ok = false, message = "رقم المرتجع غير صحيح." });

            await using var tx = await context.Database.BeginTransactionAsync();
            try
            {
                var ret = await context.SalesReturns.Include(sr => sr.Lines).FirstOrDefaultAsync(sr => sr.SRId == dto.SRId);
                if (ret == null) { await tx.RollbackAsync(); return NotFound(new { ok = false, message = "المرتجع غير موجود." }); }
                if (ret.IsPosted) { await tx.RollbackAsync(); return BadRequest(new { ok = false, message = "المرتجع مترحّل ومقفول. استخدم زر (فتح المرتجع) أولاً." }); }

                var lines = await context.SalesReturnLines.Where(l => l.SRId == dto.SRId).ToListAsync();
                if (lines.Count == 0) { await tx.CommitAsync(); var header0 = await context.SalesReturns.AsNoTracking().FirstAsync(sr => sr.SRId == dto.SRId); return Json(new { ok = true, message = "لا توجد أصناف لمسحها.", lines = new object[0], totals = new { totalBeforeDiscount = header0.TotalBeforeDiscount, totalAfterDiscountBeforeTax = header0.TotalAfterDiscountBeforeTax, taxAmount = header0.TaxAmount, netTotal = header0.NetTotal } }); }

                // عكس المخزون لكل سطر كان له حركات (مرتجع كان مُرحّل ثم فُتح): مسح أصناف المرتجع = تقليل المخزون (عكس البيع)
                foreach (var line in lines)
                {
                    var ledgers = await context.StockLedger.Where(x => x.SourceType == "SalesReturn" && x.SourceId == dto.SRId && x.SourceLine == line.LineNo).ToListAsync();
                    if (ledgers.Any())
                    {
                        var batchNo = string.IsNullOrWhiteSpace(line.BatchNo) ? null : line.BatchNo.Trim();
                        var exp = line.Expiry?.Date;
                        if (!string.IsNullOrWhiteSpace(batchNo) && exp.HasValue)
                        {
                            var sb = await context.StockBatches.FirstOrDefaultAsync(b => b.WarehouseId == ret.WarehouseId && b.ProdId == line.ProdId && b.BatchNo == batchNo && b.Expiry.HasValue && b.Expiry.Value.Date == exp.Value);
                            if (sb != null) { sb.QtyOnHand -= line.Qty; sb.UpdatedAt = DateTime.UtcNow; sb.Note = $"SR:{dto.SRId} ClearAll Line:{line.LineNo} (-{line.Qty})"; }
                        }
                        context.StockLedger.RemoveRange(ledgers);
                    }
                }
                context.SalesReturnLines.RemoveRange(lines);
                await context.SaveChangesAsync();
                await _docTotals.RecalcSalesReturnTotalsAsync(dto.SRId);
                await context.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }

            var linesNow = await context.SalesReturnLines.Where(l => l.SRId == dto.SRId).OrderBy(l => l.LineNo).ToListAsync();
            var prodIds = linesNow.Select(l => l.ProdId).Distinct().ToList();
            var prodMap = await context.Products.AsNoTracking().Where(p => prodIds.Contains(p.ProdId)).Select(p => new { p.ProdId, p.ProdName }).ToDictionaryAsync(x => x.ProdId, x => x.ProdName ?? "");
            var linesDto = linesNow.Select(l => new { lineNo = l.LineNo, prodId = l.ProdId, prodName = prodMap.TryGetValue(l.ProdId, out var n) ? n : "", qty = l.Qty, priceRetail = l.PriceRetail, disc1Percent = l.Disc1Percent, batchNo = l.BatchNo, expiry = l.Expiry?.ToString("yyyy-MM-dd"), lineNetTotal = l.LineNetTotal }).ToList();
            var h = await context.SalesReturns.AsNoTracking().FirstAsync(sr => sr.SRId == dto.SRId);
            return Json(new { ok = true, message = "تم مسح كل الأصناف.", lines = linesDto, totals = new { totalBeforeDiscount = h.TotalBeforeDiscount, totalAfterDiscountBeforeTax = h.TotalAfterDiscountBeforeTax, taxAmount = h.TaxAmount, netTotal = h.NetTotal } });
        }

        // =========================================================
        // OpenReturn — فتح المرتجع (إلغاء الترحيل) — بنفس فكرة فتح فاتورة البيع:
        // لا عكس للقيود هنا؛ العكس يتم عند إعادة الترحيل (PostReturn).
        // =========================================================
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> OpenReturn(int id)
        {
            if (id <= 0) return BadRequest(new { ok = false, message = "رقم المرتجع غير صحيح." });

            var ret = await context.SalesReturns.FirstOrDefaultAsync(sr => sr.SRId == id);
            if (ret == null) return NotFound(new { ok = false, message = "المرتجع غير موجود." });
            if (!ret.IsPosted)
                return BadRequest(new { ok = false, message = "هذا المرتجع ليس مُرحّلاً، لا يوجد ما يمكن فتحه." });

            // فتح المرتجع للتعديل (بدون عكس القيود — العكس عند إعادة الترحيل)
            ret.IsPosted = false;
            ret.Status = "Draft"; // القيد في DB: Draft/Posted/Cancelled فقط
            ret.PostedAt = null;
            ret.PostedBy = null;
            ret.UpdatedAt = DateTime.UtcNow;

            await context.SaveChangesAsync();

            return Json(new
            {
                ok = true,
                message = "تم فتح المرتجع للتعديل.",
                isPosted = false,
                status = "مفتوحة للتعديل",
                postedLabel = "مفتوحة للتعديل"
            });
        }

        // =========================================================
        // POST: ترحيل مرتجع البيع
        // =========================================================
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> PostReturn(int id)
        {
            if (id <= 0)
                return Json(new { ok = false, message = "رقم المرتجع غير صحيح." });

            try
            {
                var salesReturn = await context.SalesReturns
                    .AsNoTracking()
                    .FirstOrDefaultAsync(sr => sr.SRId == id);

                if (salesReturn == null)
                    return Json(new { ok = false, message = "المرتجع غير موجود." });

                if (salesReturn.IsPosted)
                    return Json(new { ok = false, message = "هذا المرتجع مترحّل بالفعل." });

                var postedBy = User?.Identity?.Name ?? "SYSTEM";
                await _ledgerPostingService.PostSalesReturnAsync(id, postedBy);

                // إعادة تحميل المرتجع بعد الترحيل
                var updated = await context.SalesReturns
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.SRId == id);

                // رقم المرحلة (من دفتر الأستاذ — مثل المبيعات) للعرض "مرحلة 1"، "مرحلة 2"، ...
                int stage = await context.LedgerEntries
                    .AsNoTracking()
                    .Where(e => e.SourceType == LedgerSourceType.SalesReturn && e.SourceId == id && e.LineNo == 1 && e.PostVersion > 0)
                    .MaxAsync(e => (int?)e.PostVersion) ?? 1;

                string postedLabel = $"مرحلة {stage}";

                return Json(new
                {
                    ok = true,
                    message = "تم ترحيل المرتجع بنجاح.",
                    isPosted = updated?.IsPosted ?? true,
                    status = postedLabel,
                    postedLabel = postedLabel,
                    stage = stage
                });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, message = $"حدث خطأ: {ex.Message}" });
            }
        }

        /// <summary>
        /// دالة مساعدة: تجهيز بيانات التنقل (أول/سابق/التالي/آخر) لمرتجع البيع.
        /// </summary>
        private async Task FillSalesReturnNavAsync(int currentId)
        {
            // ==============================
            // 1) أول وآخر مرتجع (Query واحد)
            // ==============================
            var minMax = await context.SalesReturns
                .AsNoTracking()
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    FirstId = g.Min(x => x.SRId),
                    LastId = g.Max(x => x.SRId)
                })
                .FirstOrDefaultAsync();

            // ==============================
            // 2) السابقة/التالية
            // ملاحظة مهمة:
            // - لو currentId = 0 (مرتجع جديد) => السابقة = آخر مرتجع / التالية = أول مرتجع
            // ==============================
            int? prevId = null; // متغير: رقم المرتجع السابق
            int? nextId = null; // متغير: رقم المرتجع التالي

            if (currentId > 0)
            {
                // السابقة = أكبر رقم أقل من الحالي
                prevId = await context.SalesReturns
                    .AsNoTracking()
                    .Where(x => x.SRId < currentId)
                    .OrderByDescending(x => x.SRId)
                    .Select(x => (int?)x.SRId)
                    .FirstOrDefaultAsync();

                // التالية = أصغر رقم أكبر من الحالي
                nextId = await context.SalesReturns
                    .AsNoTracking()
                    .Where(x => x.SRId > currentId)
                    .OrderBy(x => x.SRId)
                    .Select(x => (int?)x.SRId)
                    .FirstOrDefaultAsync();
            }
            else
            {
                // ✅ مرتجع جديد: نخلي الأسهم شغالة كبحث سريع
                prevId = minMax?.LastId;   // السابق يأخذك لآخر مرتجع
                nextId = minMax?.FirstId;  // التالي يأخذك لأول مرتجع
            }

            // ==============================
            // 3) تعبئة ViewBag للـ View (بدون Null)
            // ==============================
            int firstId = minMax?.FirstId ?? 0;  // متغير: أول مرتجع
            int lastId = minMax?.LastId ?? 0;  // متغير: آخر مرتجع

            ViewBag.NavFirstId = firstId;
            ViewBag.NavLastId = lastId;
            ViewBag.NavPrevId = prevId ?? 0;
            ViewBag.NavNextId = nextId ?? 0;
        }

        // ============================================================================
        // حذف عميق لمرتجع بيع واحد (من القائمة) — مثل TryDeleteSalesInvoiceDeepAsync
        // 1) تقليل StockBatches (عكس المرتجع = تقليل المخزون) 2) StockFifoMap 3) StockLedger 4) عكس القيود 5) حذف الهيدر
        // ============================================================================
        private async Task<DeleteReturnResult> TryDeleteSalesReturnDeepAsync(int id)
        {
            var ret = await context.SalesReturns.FirstOrDefaultAsync(x => x.SRId == id);
            if (ret == null)
                return new DeleteReturnResult(DeleteReturnStatus.Failed, "المرتجع غير موجود.");

            var lines = await context.SalesReturnLines
                .Where(l => l.SRId == id)
                .OrderBy(l => l.LineNo)
                .ToListAsync();

            var allLedgers = await context.StockLedger
                .Where(x => x.SourceType == "SalesReturn" && x.SourceId == id)
                .ToListAsync();

            await using var tx = await context.Database.BeginTransactionAsync();
            try
            {
                // 1) تقليل StockBatches (عكس المرتجع: المرتجع زاد المخزون، الحذف يقلّله)
                foreach (var line in lines)
                {
                    var batchNo = string.IsNullOrWhiteSpace(line.BatchNo) ? null : line.BatchNo.Trim();
                    var expDate = line.Expiry?.Date;

                    if (!string.IsNullOrWhiteSpace(batchNo) && expDate.HasValue)
                    {
                        var exp = expDate.Value.Date;
                        var sbRow = await context.StockBatches
                            .FirstOrDefaultAsync(x =>
                                x.WarehouseId == ret.WarehouseId &&
                                x.ProdId == line.ProdId &&
                                x.BatchNo == batchNo &&
                                x.Expiry.HasValue &&
                                x.Expiry.Value.Date == exp);

                        if (sbRow != null)
                        {
                            sbRow.QtyOnHand -= line.Qty;
                            sbRow.UpdatedAt = DateTime.UtcNow;
                            sbRow.Note = $"SR:{id} DeleteFromIndex (Line:{line.LineNo}) (-{line.Qty})";
                        }
                    }
                }

                // 2) حذف StockFifoMap المرتبط بحركات الدخول (مرتجع = QtyIn) — إن وُجدت
                var ledgerIds = allLedgers.Select(l => l.EntryId).ToList();
                if (ledgerIds.Count > 0)
                {
                    var fifoMaps = await context.Set<StockFifoMap>()
                        .Where(f => f.InEntryId != 0 && ledgerIds.Contains(f.InEntryId))
                        .ToListAsync();
                    if (fifoMaps.Count > 0)
                        context.Set<StockFifoMap>().RemoveRange(fifoMaps);
                }

                // 3) حذف StockLedger الخاص بالمرتجع
                if (allLedgers.Count > 0)
                    context.StockLedger.RemoveRange(allLedgers);

                // 4) عكس الأثر المحاسبي
                await _ledgerPostingService.ReverseForHeaderDeleteAsync(
                    LedgerSourceType.SalesReturn,
                    id,
                    postedBy: User?.Identity?.Name,
                    reason: $"حذف مرتجع بيع من قائمة الهيدر SRId={id}"
                );

                // 5) حذف الهيدر (Cascade يحذف السطور)
                context.SalesReturns.Remove(ret);

                await context.SaveChangesAsync();
                await tx.CommitAsync();

                return new DeleteReturnResult(DeleteReturnStatus.Deleted, "تم الحذف.");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return new DeleteReturnResult(DeleteReturnStatus.Failed, ex.Message);
            }
        }

        private enum DeleteReturnStatus { Deleted = 1, Failed = 2 }

        private sealed class DeleteReturnResult
        {
            public DeleteReturnStatus Status { get; }
            public string? Message { get; }
            public DeleteReturnResult(DeleteReturnStatus status, string? message) { Status = status; Message = message; }
        }
    }
}
