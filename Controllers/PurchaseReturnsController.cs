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
using ERP.Models;                                 // PurchaseReturn, PurchaseReturnLine, StockLedger, StockBatch
using ERP.Services;                               // ILedgerPostingService, DocumentTotalsService
using ERP.ViewModels;                             // PurchaseReturnHeaderDto

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر قائمة مرتجعات الشراء:
    /// عرض / بحث / فرز / حذف جماعي / تصدير.
    /// </summary>
    public class PurchaseReturnsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ILedgerPostingService _ledgerPostingService;
        private readonly DocumentTotalsService _docTotals;

        public PurchaseReturnsController(AppDbContext context, ILedgerPostingService ledgerPostingService, DocumentTotalsService docTotals)
        {
            _context = context;
            _ledgerPostingService = ledgerPostingService;
            _docTotals = docTotals;
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

            await PopulateDropDownsAsync();

            var prodIds = model.Lines.Select(l => l.ProdId).Distinct().ToList();
            var prodNames = await _context.Products
                .AsNoTracking()
                .Where(p => prodIds.Contains(p.ProdId))
                .Select(p => new { p.ProdId, p.ProdName })
                .ToListAsync();
            ViewBag.ProdNames = prodNames.ToDictionary(x => x.ProdId, x => x.ProdName ?? "");

            if (model.IsPosted)
            {
                int? maxStage = await _context.LedgerEntries
                    .AsNoTracking()
                    .Where(e => e.SourceType == LedgerSourceType.PurchaseReturn && e.SourceId == id && e.LineNo == 1 && e.PostVersion > 0)
                    .MaxAsync(e => (int?)e.PostVersion);
                if (maxStage.HasValue)
                    ViewBag.ReturnStage = maxStage.Value;
            }

            await FillPurchaseReturnNavAsync(id);

            return View("Show", model);
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
                await PopulateDropDownsAsync();
                return View("Show", purchaseReturn);
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

                ModelState.AddModelError(
                    string.Empty,
                    "تعذر حفظ التعديل بسبب تعارض في البيانات. من فضلك أعد تحميل الصفحة ثم حاول مرة أخرى."
                );
                await PopulateDropDownsAsync();
                return View("Show", purchaseReturn);
            }

            // بعد الحفظ نرجع لقائمة مرتجعات الشراء
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> SaveHeader([FromBody] PurchaseReturnHeaderDto dto)
        {
            if (dto == null)
                return BadRequest("حدث خطأ في البيانات المرسلة.");
            if (dto.CustomerId <= 0)
                return BadRequest("يجب اختيار المورد قبل حفظ المرتجع.");
            if (dto.WarehouseId <= 0)
                return BadRequest("يجب اختيار المخزن قبل حفظ المرتجع.");
            var now = DateTime.UtcNow;
            var userName = User?.Identity?.Name ?? "system";
            if (dto.PRetId == 0)
            {
                var entity = new PurchaseReturn
                {
                    PRetDate = now.Date,
                    CustomerId = dto.CustomerId,
                    WarehouseId = dto.WarehouseId,
                    RefPIId = (dto.RefPIId ?? 0) > 0 ? dto.RefPIId : null,
                    Status = "Draft",
                    IsPosted = false,
                    CreatedAt = now,
                    CreatedBy = userName
                };
                _context.PurchaseReturns.Add(entity);
                await _context.SaveChangesAsync();
                return Json(new { success = true, pretId = entity.PRetId, returnNumber = entity.PRetId.ToString(), returnDate = entity.PRetDate.ToString("yyyy/MM/dd"), returnTime = entity.CreatedAt.ToLocalTime().ToString("HH:mm"), status = entity.Status, isPosted = entity.IsPosted, createdBy = entity.CreatedBy });
            }
            var existing = await _context.PurchaseReturns.FirstOrDefaultAsync(pr => pr.PRetId == dto.PRetId);
            if (existing == null)
                return NotFound("لم يتم العثور على المرتجع المطلوب.");
            if (existing.IsPosted)
                return BadRequest("لا يمكن تعديل مرتجع تم ترحيله.");
            existing.CustomerId = dto.CustomerId;
            existing.WarehouseId = dto.WarehouseId;
            existing.RefPIId = (dto.RefPIId ?? 0) > 0 ? dto.RefPIId : null;
            existing.UpdatedAt = now;
            await _context.SaveChangesAsync();
            return Json(new { success = true, pretId = existing.PRetId, returnNumber = existing.PRetId.ToString(), returnDate = existing.PRetDate.ToString("yyyy/MM/dd"), returnTime = existing.CreatedAt.ToLocalTime().ToString("HH:mm"), status = existing.Status, isPosted = existing.IsPosted, createdBy = existing.CreatedBy });
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
                    ["WarehouseId"] = pr => pr.WarehouseId,                   // كود المخزن
                    ["RefPIId"] = pr => pr.RefPIId ?? 0,                     // فاتورة الشراء المرجعية
                    ["NetTotal"] = pr => pr.NetTotal,                        // قيمة المرتجع
                    ["Status"] = pr => pr.Status ?? "",                   // الحالة
                    ["IsPosted"] = pr => pr.IsPosted,                       // مرحّل؟
                    ["PostedAt"] = pr => pr.PostedAt ?? pr.CreatedAt,        // تاريخ الترحيل
                    ["CreatedBy"] = pr => pr.CreatedBy ?? "",                 // أنشأه
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

            await FillPurchaseReturnNavAsync(model.PRetId);

            return View("Show", model);
        }

        /// <summary>
        /// دالة مساعدة: تجهيز بيانات التنقل (أول/سابق/التالي/آخر) لمرتجع المشتريات.
        /// </summary>
        private async Task FillPurchaseReturnNavAsync(int currentId)
        {
            var minMax = await _context.PurchaseReturns
                .AsNoTracking()
                .GroupBy(_ => 1)
                .Select(g => new { FirstId = g.Min(x => x.PRetId), LastId = g.Max(x => x.PRetId) })
                .FirstOrDefaultAsync();

            int? prevId = null, nextId = null;
            if (currentId > 0)
            {
                prevId = await _context.PurchaseReturns.AsNoTracking()
                    .Where(x => x.PRetId < currentId).OrderByDescending(x => x.PRetId)
                    .Select(x => (int?)x.PRetId).FirstOrDefaultAsync();
                nextId = await _context.PurchaseReturns.AsNoTracking()
                    .Where(x => x.PRetId > currentId).OrderBy(x => x.PRetId)
                    .Select(x => (int?)x.PRetId).FirstOrDefaultAsync();
            }
            else
            {
                prevId = minMax?.LastId;
                nextId = minMax?.FirstId;
            }

            ViewBag.NavFirstId = minMax?.FirstId ?? 0;
            ViewBag.NavLastId = minMax?.LastId ?? 0;
            ViewBag.NavPrevId = prevId ?? 0;
            ViewBag.NavNextId = nextId ?? 0;
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
            sb.AppendLine("PRetId,PRetDate,CustomerId,CustomerName,WarehouseId,RefPIId,NetTotal,Status,IsPosted,PostedAt,CreatedBy,CreatedAt,UpdatedAt");

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
                    pr.NetTotal.ToString("0.00"),
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
        // Delete — حذف مرتجع شراء واحد (نفس أسلوب فاتورة المشتريات)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var result = await TryDeletePurchaseReturnDeepAsync(id);
            if (result.Status == DeletePurchaseReturnStatus.Deleted)
            {
                TempData["Success"] = "تم حذف مرتجع الشراء بنجاح.";
                return RedirectToAction(nameof(Index));
            }
            TempData["Error"] = result.Message ?? "تعذر حذف المرتجع.";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // BulkDelete — حذف المرتجعات المحددة (حذف عميق مثل مرتجع البيع)
        // عكس المخزون + StockFifoMap + عكس القيود + حذف الهيدر
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(int[] ids)
        {
            if (ids == null || ids.Length == 0)
            {
                TempData["Error"] = "لم يتم اختيار أى مرتجع للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var existingIds = await _context.PurchaseReturns
                .Where(pr => ids.Contains(pr.PRetId))
                .Select(pr => pr.PRetId)
                .ToListAsync();

            if (existingIds.Count == 0)
            {
                TempData["Error"] = "لم يتم العثور على المرتجعات المحددة.";
                return RedirectToAction(nameof(Index));
            }

            int deletedCount = 0;
            int failedCount = 0;
            var failedIds = new List<int>();

            foreach (var pretId in existingIds)
            {
                var result = await TryDeletePurchaseReturnDeepAsync(pretId);
                if (result.Status == DeletePurchaseReturnStatus.Deleted)
                    deletedCount++;
                else
                {
                    failedCount++;
                    failedIds.Add(pretId);
                }
            }

            if (deletedCount > 0)
            {
                TempData["Success"] = $"تم حذف {deletedCount} مرتجع شراء (مع تحديث المخزون وعكس الأثر المحاسبي).";
                if (failedIds.Count > 0)
                    TempData["Error"] = $"فشل حذف المرتجعات: {string.Join(", ", failedIds)}";
            }
            else
                TempData["Error"] = failedIds.Count > 0
                    ? $"تعذر حذف المرتجعات المحددة: {string.Join(", ", failedIds)}"
                    : "لم يتم حذف أى مرتجع.";

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // DeleteAll — حذف جميع مرتجعات الشراء (حذف عميق)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var allIds = await _context.PurchaseReturns.Select(pr => pr.PRetId).ToListAsync();

            if (allIds.Count == 0)
            {
                TempData["Error"] = "لا توجد مرتجعات لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            int deletedCount = 0;
            int failedCount = 0;
            var failedIds = new List<int>();

            foreach (var pretId in allIds)
            {
                var result = await TryDeletePurchaseReturnDeepAsync(pretId);
                if (result.Status == DeletePurchaseReturnStatus.Deleted)
                    deletedCount++;
                else
                {
                    failedCount++;
                    failedIds.Add(pretId);
                }
            }

            if (deletedCount > 0)
            {
                TempData["Success"] = $"تم حذف {deletedCount} مرتجع شراء (مع تحديث المخزون وعكس الأثر المحاسبي).";
                if (failedIds.Count > 0)
                    TempData["Error"] = $"فشل حذف بعض المرتجعات: {string.Join(", ", failedIds)}";
            }
            else
                TempData["Error"] = "لم يتم حذف أى مرتجع.";

            return RedirectToAction(nameof(Index));
        }

        // ============================================================================
        // حذف عميق لمرتجع شراء واحد — مثل TryDeleteSalesReturnDeepAsync
        // 1) إرجاع StockBatches (مرتجع الشراء = QtyOut، الحذف يزيد المخزون)
        // 2) StockFifoMap 3) StockLedger 4) عكس القيود 5) حذف الهيدر
        // ============================================================================
        private async Task<DeletePurchaseReturnResult> TryDeletePurchaseReturnDeepAsync(int id)
        {
            var ret = await _context.PurchaseReturns.FirstOrDefaultAsync(x => x.PRetId == id);
            if (ret == null)
                return new DeletePurchaseReturnResult(DeletePurchaseReturnStatus.Failed, "المرتجع غير موجود.");

            var lines = await _context.PurchaseReturnLines
                .Where(l => l.PRetId == id)
                .OrderBy(l => l.LineNo)
                .ToListAsync();

            var allLedgers = await _context.StockLedger
                .Where(x => x.SourceType == "PurchaseReturn" && x.SourceId == id)
                .ToListAsync();

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1) إرجاع StockBatches (مرتجع الشراء كان QtyOut، الحذف يعيد الكمية)
                foreach (var line in lines)
                {
                    var batchNo = string.IsNullOrWhiteSpace(line.BatchNo) ? null : line.BatchNo.Trim();
                    var expDate = line.Expiry?.Date;

                    if (!string.IsNullOrWhiteSpace(batchNo) && expDate.HasValue)
                    {
                        var exp = expDate.Value.Date;
                        var sbRow = await _context.StockBatches
                            .FirstOrDefaultAsync(x =>
                                x.WarehouseId == ret.WarehouseId &&
                                x.ProdId == line.ProdId &&
                                x.BatchNo == batchNo &&
                                x.Expiry.HasValue &&
                                x.Expiry.Value.Date == exp);

                        if (sbRow != null)
                        {
                            sbRow.QtyOnHand += line.Qty;
                            sbRow.UpdatedAt = DateTime.UtcNow;
                            sbRow.Note = $"PR:{id} DeleteFromIndex (Line:{line.LineNo}) (+{line.Qty})";
                        }
                    }
                }

                // 2) حذف StockFifoMap المرتبط بحركات الخروج (مرتجع شراء = QtyOut)
                var ledgerIds = allLedgers.Select(l => l.EntryId).ToList();
                if (ledgerIds.Count > 0)
                {
                    var fifoMaps = await _context.Set<StockFifoMap>()
                        .Where(f => ledgerIds.Contains(f.OutEntryId))
                        .ToListAsync();
                    if (fifoMaps.Count > 0)
                        _context.Set<StockFifoMap>().RemoveRange(fifoMaps);
                }

                // 3) حذف StockLedger الخاص بالمرتجع
                if (allLedgers.Count > 0)
                    _context.StockLedger.RemoveRange(allLedgers);

                // 4) عكس الأثر المحاسبي
                await _ledgerPostingService.ReverseForHeaderDeleteAsync(
                    LedgerSourceType.PurchaseReturn,
                    id,
                    postedBy: User?.Identity?.Name,
                    reason: $"حذف مرتجع شراء من قائمة الهيدر PRetId={id}"
                );

                // 5) حذف الهيدر (Cascade يحذف السطور)
                _context.PurchaseReturns.Remove(ret);

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return new DeletePurchaseReturnResult(DeletePurchaseReturnStatus.Deleted, "تم الحذف.");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return new DeletePurchaseReturnResult(DeletePurchaseReturnStatus.Failed, ex.Message);
            }
        }

        private enum DeletePurchaseReturnStatus { Deleted = 1, Failed = 2 }

        private sealed class DeletePurchaseReturnResult
        {
            public DeletePurchaseReturnStatus Status { get; }
            public string? Message { get; }
            public DeletePurchaseReturnResult(DeletePurchaseReturnStatus status, string? message) { Status = status; Message = message; }
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

            // جلب سطور الفاتورة مع بيانات الصنف (فقط الأصناف الموجودة في Products)
            var lines = await _context.PILines
                .AsNoTracking()
                .Include(l => l.Product)
                .Where(l => l.PIId == invoiceId && l.Product != null)
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

            // كميات مرتجعة سابقاً من كل سطر (من جميع مرتجعات الشراء المرتبطة بنفس الفاتورة)
            var returnedByLine = await _context.PurchaseReturnLines
                .Where(l => l.RefPIId == invoiceId && l.RefPILineNo != null)
                .GroupBy(l => l.RefPILineNo)
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
                l.unitCost,
                l.purchaseDiscountPct
            }).ToList();

            return Json(new
            {
                ok = true,
                invoiceId = invoiceId,
                customerId = invoice.CustomerId,
                warehouseId = invoice.WarehouseId,
                invoiceDate = invoice.PIDate.ToString("yyyy-MM-dd"),
                items = items
            });
        }

        // =========================================================
        // AddLineJson — إضافة سطر لمرتجع المشتريات + StockLedger (QtyOut) + StockBatch
        // مرتجع الشراء = عكس الشراء → نقصان المخزون
        // =========================================================
        public class AddLineJsonDto
        {
            public int PRetId { get; set; }
            public int ProdId { get; set; }
            public int Qty { get; set; }
            public string? BatchNo { get; set; }
            public string? ExpiryText { get; set; }
            public decimal UnitCost { get; set; }
            public decimal PurchaseDiscountPct { get; set; }
            public decimal PriceRetail { get; set; }
            public int? RefPIId { get; set; }
            public int? RefPILineNo { get; set; }
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> AddLineJson([FromBody] AddLineJsonDto dto)
        {
            if (dto == null || dto.PRetId <= 0 || dto.ProdId <= 0 || dto.Qty <= 0)
                return Json(new { ok = false, message = "بيانات السطر غير صحيحة." });

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var ret = await _context.PurchaseReturns.Include(pr => pr.Lines).FirstOrDefaultAsync(pr => pr.PRetId == dto.PRetId);
                if (ret == null) { await tx.RollbackAsync(); return Json(new { ok = false, message = "المرتجع غير موجود." }); }
                if (ret.IsPosted) { await tx.RollbackAsync(); return Json(new { ok = false, message = "لا يمكن تعديل مرتجع مترحّل." }); }
                if (ret.WarehouseId <= 0) { await tx.RollbackAsync(); return Json(new { ok = false, message = "يجب اختيار المخزن. احفظ المرتجع بعد تعيين المورد والمخزن." }); }

                var productExists = await _context.Products.AnyAsync(p => p.ProdId == dto.ProdId);
                if (!productExists) { await tx.RollbackAsync(); return Json(new { ok = false, message = "الصنف المحدد غير موجود في قاعدة البيانات (جدول الأصناف). قد يكون الصنف محذوفاً. استبعد سطور الفاتورة المحذوف أصنافها." }); }

                // التحقق من الكمية مقابل الفاتورة والمرتجعات السابقة من نفس السطر
                int? refPIId = (dto.RefPIId ?? 0) > 0 ? dto.RefPIId : (ret.RefPIId);
                if (refPIId.HasValue && refPIId.Value > 0 && (dto.RefPILineNo ?? 0) > 0)
                {
                    var invoiceLine = await _context.PILines
                        .AsNoTracking()
                        .FirstOrDefaultAsync(l => l.PIId == refPIId.Value && l.LineNo == dto.RefPILineNo.Value);
                    if (invoiceLine == null)
                    {
                        await tx.RollbackAsync();
                        return Json(new { ok = false, message = "سطر الفاتورة غير موجود." });
                    }
                    int invoiceQty = invoiceLine.Qty;
                    int alreadyReturned = await _context.PurchaseReturnLines
                        .Where(l => l.RefPIId == refPIId && l.RefPILineNo == dto.RefPILineNo)
                        .SumAsync(l => l.Qty);
                    int remaining = invoiceQty - alreadyReturned;
                    if (remaining <= 0)
                    {
                        await tx.RollbackAsync();
                        return Json(new { ok = false, message = "تم إرجاع كامل كمية هذا السطر من الفاتورة مسبقاً. لا يمكن إضافة المزيد." });
                    }
                    if (dto.Qty > remaining)
                    {
                        await tx.RollbackAsync();
                        return Json(new { ok = false, message = $"الكمية المرتجعة ({dto.Qty}) تتجاوز المتبقي من هذا السطر في الفاتورة ({remaining}). الكمية في الفاتورة: {invoiceQty}، تم إرجاع {alreadyReturned} سابقاً." });
                    }
                }

                DateTime? exp = null;
                if (!string.IsNullOrWhiteSpace(dto.ExpiryText) && DateTime.TryParse(dto.ExpiryText.Trim(), out var pe))
                    exp = pe.Date;
                var batchNo = string.IsNullOrWhiteSpace(dto.BatchNo) ? null : dto.BatchNo.Trim();
                var unitCost = Math.Max(0, dto.UnitCost);
                var discPct = Math.Max(0, Math.Min(100, dto.PurchaseDiscountPct));

                if (!string.IsNullOrWhiteSpace(batchNo) && exp.HasValue)
                {
                    var sb = await _context.StockBatches.FirstOrDefaultAsync(b =>
                        b.WarehouseId == ret.WarehouseId && b.ProdId == dto.ProdId && b.BatchNo == batchNo && b.Expiry.HasValue && b.Expiry.Value.Date == exp.Value.Date);
                    if (sb == null || sb.QtyOnHand < dto.Qty)
                    {
                        await tx.RollbackAsync();
                        return Json(new { ok = false, message = sb == null ? "التشغيلة غير موجودة في المخزن. تأكد أن مخزن المرتجع = مخزن فاتورة الشراء واحفظ المرتجع." : "الكمية المتاحة في التشغيلة أقل من المطلوب." });
                    }
                }

                var nextLineNo = (ret.Lines?.Any() == true ? ret.Lines!.Max(x => x.LineNo) : 0) + 1;
                var line = new PurchaseReturnLine
                {
                    PRetId = dto.PRetId,
                    LineNo = nextLineNo,
                    ProdId = dto.ProdId,
                    Qty = dto.Qty,
                    UnitCost = unitCost,
                    PurchaseDiscountPct = discPct,
                    PriceRetail = Math.Max(0, dto.PriceRetail),
                    BatchNo = batchNo,
                    Expiry = exp,
                    RefPIId = refPIId,
                    RefPILineNo = (dto.RefPILineNo ?? 0) > 0 ? dto.RefPILineNo : null
                };
                _context.PurchaseReturnLines.Add(line);
                // تعيين الـ FK الظاهرة للعلاقات (ProductProdId + PurchaseReturnPRetId)
                _context.Entry(line).Property("ProductProdId").CurrentValue = dto.ProdId;
                _context.Entry(line).Property("PurchaseReturnPRetId").CurrentValue = dto.PRetId;
                await _context.SaveChangesAsync();

                var now = DateTime.UtcNow;
                _context.StockLedger.Add(new StockLedger
                {
                    TranDate = now,
                    WarehouseId = ret.WarehouseId,
                    ProdId = dto.ProdId,
                    BatchNo = batchNo ?? "",
                    Expiry = exp,
                    QtyIn = 0,
                    QtyOut = dto.Qty,
                    UnitCost = unitCost,
                    RemainingQty = null,
                    SourceType = "PurchaseReturn",
                    SourceId = dto.PRetId,
                    SourceLine = nextLineNo,
                    Note = "Purchase Return Line"
                });
                await _context.SaveChangesAsync();

                if (!string.IsNullOrWhiteSpace(batchNo) && exp.HasValue)
                {
                    var sb = await _context.StockBatches.FirstOrDefaultAsync(b =>
                        b.WarehouseId == ret.WarehouseId && b.ProdId == dto.ProdId && b.BatchNo == batchNo && b.Expiry.HasValue && b.Expiry.Value.Date == exp.Value.Date);
                    if (sb != null) { sb.QtyOnHand -= dto.Qty; sb.UpdatedAt = now; sb.Note = $"PR:{dto.PRetId} Line:{nextLineNo} (-{dto.Qty})"; }
                    await _context.SaveChangesAsync();
                }

                await _docTotals.RecalcPurchaseReturnTotalsAsync(dto.PRetId);
                await _context.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Json(new { ok = false, message = "حدث خطأ أثناء إضافة السطر. " + (ex.InnerException?.Message ?? ex.Message) });
            }

            var linesNow = await _context.PurchaseReturnLines.Where(l => l.PRetId == dto.PRetId).OrderBy(l => l.LineNo).ToListAsync();
            var prodIds = linesNow.Select(l => l.ProdId).Distinct().ToList();
            var prodMap = await _context.Products.AsNoTracking().Where(p => prodIds.Contains(p.ProdId)).Select(p => new { p.ProdId, p.ProdName }).ToDictionaryAsync(x => x.ProdId, x => x.ProdName ?? "");
            var linesDto = linesNow.Select(l => new { lineNo = l.LineNo, prodId = l.ProdId, prodName = prodMap.TryGetValue(l.ProdId, out var n) ? n : "", qty = l.Qty, unitCost = l.UnitCost, priceRetail = l.PriceRetail, batchNo = l.BatchNo, expiry = l.Expiry?.ToString("yyyy-MM-dd"), lineValue = l.Qty * l.UnitCost }).ToList();
            var h = await _context.PurchaseReturns.AsNoTracking().FirstAsync(pr => pr.PRetId == dto.PRetId);
            return Json(new { ok = true, message = "تمت إضافة السطر.", lines = linesDto, totals = new { itemsTotal = h.ItemsTotal, discountTotal = h.DiscountTotal, taxTotal = h.TaxTotal, netTotal = h.NetTotal } });
        }

        public class RemoveLineJsonDto { public int PRetId { get; set; } public int LineNo { get; set; } }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> RemoveLineJson([FromBody] RemoveLineJsonDto dto)
        {
            if (dto == null || dto.PRetId <= 0 || dto.LineNo <= 0)
                return BadRequest(new { ok = false, message = "بيانات المسح غير صحيحة." });

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var ret = await _context.PurchaseReturns.FirstOrDefaultAsync(pr => pr.PRetId == dto.PRetId);
                if (ret == null) { await tx.RollbackAsync(); return NotFound(new { ok = false, message = "المرتجع غير موجود." }); }
                if (ret.IsPosted) { await tx.RollbackAsync(); return BadRequest(new { ok = false, message = "لا يمكن تعديل مرتجع مترحّل." }); }

                var line = await _context.PurchaseReturnLines.FirstOrDefaultAsync(l => l.PRetId == dto.PRetId && l.LineNo == dto.LineNo);
                if (line == null) { await tx.RollbackAsync(); return NotFound(new { ok = false, message = "السطر غير موجود." }); }

                var batchNo = string.IsNullOrWhiteSpace(line.BatchNo) ? null : line.BatchNo.Trim();
                var exp = line.Expiry?.Date;
                if (!string.IsNullOrWhiteSpace(batchNo) && exp.HasValue)
                {
                    var sb = await _context.StockBatches.FirstOrDefaultAsync(b =>
                        b.WarehouseId == ret.WarehouseId && b.ProdId == line.ProdId && b.BatchNo == batchNo && b.Expiry.HasValue && b.Expiry.Value.Date == exp.Value);
                    if (sb != null) { sb.QtyOnHand += line.Qty; sb.UpdatedAt = DateTime.UtcNow; sb.Note = $"PR:{dto.PRetId} Line:{dto.LineNo} (+{line.Qty})"; }
                }
                var ledgers = await _context.StockLedger.Where(x => x.SourceType == "PurchaseReturn" && x.SourceId == dto.PRetId && x.SourceLine == dto.LineNo).ToListAsync();
                _context.StockLedger.RemoveRange(ledgers);
                _context.PurchaseReturnLines.Remove(line);
                await _context.SaveChangesAsync();
                await _docTotals.RecalcPurchaseReturnTotalsAsync(dto.PRetId);
                await _context.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }

            var linesNow = await _context.PurchaseReturnLines.Where(l => l.PRetId == dto.PRetId).OrderBy(l => l.LineNo).ToListAsync();
            var prodIds = linesNow.Select(l => l.ProdId).Distinct().ToList();
            var prodMap = await _context.Products.AsNoTracking().Where(p => prodIds.Contains(p.ProdId)).Select(p => new { p.ProdId, p.ProdName }).ToDictionaryAsync(x => x.ProdId, x => x.ProdName ?? "");
            var linesDto = linesNow.Select(l => new { lineNo = l.LineNo, prodId = l.ProdId, prodName = prodMap.TryGetValue(l.ProdId, out var n) ? n : "", qty = l.Qty, unitCost = l.UnitCost, priceRetail = l.PriceRetail, batchNo = l.BatchNo, expiry = l.Expiry?.ToString("yyyy-MM-dd"), lineValue = l.Qty * l.UnitCost }).ToList();
            var h = await _context.PurchaseReturns.AsNoTracking().FirstAsync(pr => pr.PRetId == dto.PRetId);
            return Json(new { ok = true, message = "تم حذف السطر.", lines = linesDto, totals = new { itemsTotal = h.ItemsTotal, discountTotal = h.DiscountTotal, taxTotal = h.TaxTotal, netTotal = h.NetTotal } });
        }

        public class ClearLinesJsonDto { public int PRetId { get; set; } }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> ClearLinesJson([FromBody] ClearLinesJsonDto dto)
        {
            if (dto == null || dto.PRetId <= 0)
                return BadRequest(new { ok = false, message = "رقم المرتجع غير صحيح." });

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var ret = await _context.PurchaseReturns.Include(pr => pr.Lines).FirstOrDefaultAsync(pr => pr.PRetId == dto.PRetId);
                if (ret == null) { await tx.RollbackAsync(); return NotFound(new { ok = false, message = "المرتجع غير موجود." }); }
                if (ret.IsPosted) { await tx.RollbackAsync(); return BadRequest(new { ok = false, message = "المرتجع مترحّل ومقفول. استخدم زر (فتح المرتجع) أولاً." }); }
                var lines = await _context.PurchaseReturnLines.Where(l => l.PRetId == dto.PRetId).ToListAsync();
                if (lines.Count == 0) { await tx.CommitAsync(); var header0 = await _context.PurchaseReturns.AsNoTracking().FirstAsync(pr => pr.PRetId == dto.PRetId); return Json(new { ok = true, message = "لا توجد أصناف لمسحها.", lines = new object[0], totals = new { itemsTotal = header0.ItemsTotal, discountTotal = header0.DiscountTotal, taxTotal = header0.TaxTotal, netTotal = header0.NetTotal } }); }
                foreach (var line in lines)
                {
                    var batchNo = string.IsNullOrWhiteSpace(line.BatchNo) ? null : line.BatchNo.Trim();
                    var exp = line.Expiry?.Date;
                    if (!string.IsNullOrWhiteSpace(batchNo) && exp.HasValue)
                    {
                        var sb = await _context.StockBatches.FirstOrDefaultAsync(b => b.WarehouseId == ret.WarehouseId && b.ProdId == line.ProdId && b.BatchNo == batchNo && b.Expiry.HasValue && b.Expiry.Value.Date == exp.Value);
                        if (sb != null) { sb.QtyOnHand += line.Qty; sb.UpdatedAt = DateTime.UtcNow; }
                    }
                    var ledgers = await _context.StockLedger.Where(x => x.SourceType == "PurchaseReturn" && x.SourceId == dto.PRetId && x.SourceLine == line.LineNo).ToListAsync();
                    _context.StockLedger.RemoveRange(ledgers);
                }
                _context.PurchaseReturnLines.RemoveRange(lines);
                await _context.SaveChangesAsync();
                await _docTotals.RecalcPurchaseReturnTotalsAsync(dto.PRetId);
                await _context.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }

            var linesNow = await _context.PurchaseReturnLines.Where(l => l.PRetId == dto.PRetId).OrderBy(l => l.LineNo).ToListAsync();
            var prodIds = linesNow.Select(l => l.ProdId).Distinct().ToList();
            var prodMap = await _context.Products.AsNoTracking().Where(p => prodIds.Contains(p.ProdId)).Select(p => new { p.ProdId, p.ProdName }).ToDictionaryAsync(x => x.ProdId, x => x.ProdName ?? "");
            var linesDto = linesNow.Select(l => new { lineNo = l.LineNo, prodId = l.ProdId, prodName = prodMap.TryGetValue(l.ProdId, out var n) ? n : "", qty = l.Qty, unitCost = l.UnitCost, priceRetail = l.PriceRetail, batchNo = l.BatchNo, expiry = l.Expiry?.ToString("yyyy-MM-dd"), lineValue = l.Qty * l.UnitCost }).ToList();
            var h = await _context.PurchaseReturns.AsNoTracking().FirstAsync(pr => pr.PRetId == dto.PRetId);
            return Json(new { ok = true, message = "تم مسح كل الأصناف.", lines = linesDto, totals = new { itemsTotal = h.ItemsTotal, discountTotal = h.DiscountTotal, taxTotal = h.TaxTotal, netTotal = h.NetTotal } });
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> OpenReturn(int id)
        {
            if (id <= 0) return BadRequest(new { ok = false, message = "رقم المرتجع غير صحيح." });
            var ret = await _context.PurchaseReturns.FirstOrDefaultAsync(pr => pr.PRetId == id);
            if (ret == null) return NotFound(new { ok = false, message = "المرتجع غير موجود." });
            if (!ret.IsPosted) return BadRequest(new { ok = false, message = "المرتجع غير مترحّل." });
            ret.IsPosted = false; ret.Status = "Draft"; ret.PostedAt = null; ret.PostedBy = null; ret.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
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
        // POST: ترحيل مرتجع الشراء
        // =========================================================
        [HttpPost]
        [IgnoreAntiforgeryToken]
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

                var updated = await _context.PurchaseReturns
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.PRetId == id);

                int stage = await _context.LedgerEntries
                    .AsNoTracking()
                    .Where(e => e.SourceType == LedgerSourceType.PurchaseReturn && e.SourceId == id && e.LineNo == 1 && e.PostVersion > 0)
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
    }
}
