using ERP.Data;                        // سياق قاعدة البيانات AppDbContext
using ERP.Filters;
using ERP.Infrastructure;              // PagedResult + ApplySearchSort + UserActivityLogger
using ERP.Models;                      // SalesReturn, UserActionType
using ERP.Security;
using ERP.Services;                    // ILedgerPostingService, DocumentTotalsService
using ERP.ViewModels;                  // SalesReturnHeaderDto
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;                       // MemoryStream لتصدير Excel
using System.Linq;
using System.Linq.Expressions;
using System.Text;                     // علشان StringBuilder فى التصدير
using System.Threading.Tasks;
using ClosedXML.Excel;                // تصدير Excel الفعلى

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
    [RequirePermission("SalesReturns.Index")]
    public class SalesReturnsController : Controller
    {
        private readonly AppDbContext context;
        private readonly ILedgerPostingService _ledgerPostingService;
        private readonly DocumentTotalsService _docTotals;
        private readonly IUserActivityLogger _activityLogger;

        // فاصل لقيم فلاتر الأعمدة (نفسه فى باقى القوائم)
        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        public SalesReturnsController(AppDbContext ctx, ILedgerPostingService ledgerPostingService, DocumentTotalsService docTotals, IUserActivityLogger activityLogger)
        {
            context = ctx;
            _ledgerPostingService = ledgerPostingService;
            _docTotals = docTotals;
            _activityLogger = activityLogger;
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
                .Where(c => c.IsActive == true)
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
        public async Task<IActionResult> Create(int? salesInvoiceId, bool standalone = false)
        {
            if (salesInvoiceId.HasValue)
                standalone = false;

            ViewBag.StandaloneReturn = standalone;

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

            ViewBag.StandaloneReturn = !model.SalesInvoiceId.HasValue;

            await PopulateDropDownsAsync();

            var prodIds = model.Lines.Select(l => l.ProdId).Distinct().ToList();
            var prodRows = await context.Products
                .AsNoTracking()
                .Where(p => prodIds.Contains(p.ProdId))
                .Select(p => new { p.ProdId, p.ProdName, p.Location })
                .ToListAsync();
            ViewBag.ProdNames = prodRows.ToDictionary(x => x.ProdId, x => x.ProdName ?? "");
            ViewBag.ProdLocations = prodRows.ToDictionary(x => x.ProdId, x => string.IsNullOrWhiteSpace(x.Location) ? "—" : x.Location!.Trim());

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

        [HttpGet]
        public async Task<IActionResult> ExportShowExcel(int id)
        {
            if (id <= 0)
                return BadRequest();

            var sr = await context.SalesReturns
                .Include(x => x.Customer)
                .Include(x => x.Warehouse)
                .Include(x => x.Lines)
                    .ThenInclude(l => l.Product)
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.SRId == id);

            if (sr == null)
                return NotFound();

            var bytes = ShowDocumentExcelExport.SalesReturn(sr);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ExcelExportNaming.ArabicTimestampedFileName($"مرتجع بيع {id}", ".xlsx"));
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
                var existing = await context.SalesReturns.AsNoTracking().FirstOrDefaultAsync(s => s.SRId == id);
                var oldValues = existing != null ? System.Text.Json.JsonSerializer.Serialize(new { existing.SRDate, existing.CustomerId, existing.WarehouseId, existing.NetTotal }) : null;
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

                var newValues = System.Text.Json.JsonSerializer.Serialize(new { salesReturn.SRDate, salesReturn.CustomerId, salesReturn.WarehouseId, salesReturn.NetTotal });
                await _activityLogger.LogAsync(UserActionType.Edit, "SalesReturn", salesReturn.SRId, $"تعديل مرتجع بيع رقم {salesReturn.SRId}", oldValues, newValues);

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

                await _activityLogger.LogAsync(UserActionType.Create, "SalesReturn", entity.SRId, $"إنشاء مرتجع بيع رقم {entity.SRId}");

                return Json(new { success = true, srId = entity.SRId, returnNumber = entity.SRId.ToString(), returnDate = entity.SRDate.ToString("d/M/yyyy"), returnTime = DateTime.Today.Add(entity.SRTime).ToString("HH:mm"), status = entity.Status, isPosted = entity.IsPosted, createdBy = entity.CreatedBy });
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

            return Json(new { success = true, srId = existing.SRId, returnNumber = existing.SRId.ToString(), returnDate = existing.SRDate.ToString("d/M/yyyy"), returnTime = DateTime.Today.Add(existing.SRTime).ToString("HH:mm"), status = existing.Status, isPosted = existing.IsPosted, createdBy = existing.CreatedBy });
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
            // استعلام أساسى مع تحميل العميل والمخزن للعرض فى القوائم/التصدير
            IQueryable<SalesReturn> q = context.SalesReturns
                .AsNoTracking()
                .Include(x => x.Customer)
                    .ThenInclude(c => c.Area)
                .Include(x => x.Warehouse);

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
                    ["SRId"] = x => x.SRId,                           // رقم المرتجع
                    ["CustomerId"] = x => x.CustomerId,               // العميل (كود)
                    ["WarehouseId"] = x => x.WarehouseId,             // المخزن (كود)
                    ["Ref"] = x => x.SalesInvoiceId ?? 0,             // مرجع الفاتورة
                    ["NetTotal"] = x => x.NetTotal,                   // الصافى
                    ["Status"] = x => x.Status ?? string.Empty,       // الحالة
                    ["Posted"] = x => x.IsPosted ? 1 : 0,             // الترحيل
                    ["CreatedAt"] = x => x.CreatedAt,                 // تاريخ الإنشاء
                    ["Region"] = x => x.Customer != null && x.Customer.Area != null
                        ? x.Customer.Area.AreaName
                        : (x.Customer != null ? (x.Customer.RegionName ?? "") : ""),
                    ["CreatedBy"] = x => x.CreatedBy ?? ""
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
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_date = null,
            string? filterCol_time = null,
            string? filterCol_customer = null,
            string? filterCol_warehouse = null,
            string? filterCol_ref = null,
            string? filterCol_net = null,
            string? filterCol_netExpr = null,
            string? filterCol_status = null,
            string? filterCol_posted = null,
            string? filterCol_region = null,
            string? filterCol_createdby = null,
            int page = 1,                  // رقم الصفحة
            int pageSize = 10)             // حجم الصفحة (افتراضي 10؛ 0 = الكل)
        {
            var pageSizeQuery = Request.Query["pageSize"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageSizeQuery) && int.TryParse(pageSizeQuery, out var psParsed))
                pageSize = psParsed;

            page = page < 1 ? 1 : page;
            if (pageSize < 0) pageSize = 10;
            int[] allowedPageSizes = { 10, 25, 50, 100, 200, 0 };
            if (!allowedPageSizes.Contains(pageSize))
                pageSize = 10;

            // (1) بناء الاستعلام حسب الفلاتر العامة
            var q = BuildQuery(search, searchBy, sort, dir);

            // (1.1) تطبيق فلاتر الأعمدة بنمط Excel
            if (!string.IsNullOrWhiteSpace(filterCol_idExpr))
                q = SalesReturnListNumericExpr.ApplySrIdExpr(q, filterCol_idExpr);
            else if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value)
                    .ToList();
                if (ids.Count > 0)
                    q = q.Where(sr => ids.Contains(sr.SRId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_customer))
            {
                var vals = filterCol_customer.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(sr =>
                        vals.Contains(
                            sr.Customer != null
                                ? (sr.Customer.CustomerName ?? sr.CustomerId.ToString())
                                : sr.CustomerId.ToString()
                        ));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_warehouse))
            {
                var vals = filterCol_warehouse.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(sr =>
                        vals.Contains(
                            sr.Warehouse != null
                                ? (sr.Warehouse.WarehouseName ?? sr.WarehouseId.ToString())
                                : sr.WarehouseId.ToString()
                        ));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_status))
            {
                var vals = filterCol_status.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(sr => sr.Status != null && vals.Contains(sr.Status));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_posted))
            {
                var vals = filterCol_posted.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim().ToLowerInvariant())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();
                if (vals.Count > 0)
                {
                    bool includeTrue = vals.Any(v => v == "نعم" || v == "yes" || v == "1" || v == "true");
                    bool includeFalse = vals.Any(v => v == "لا" || v == "no" || v == "0" || v == "false");
                    if (includeTrue && !includeFalse)
                        q = q.Where(sr => sr.IsPosted);
                    else if (includeFalse && !includeTrue)
                        q = q.Where(sr => !sr.IsPosted);
                }
            }

            if (!string.IsNullOrWhiteSpace(filterCol_date))
            {
                var dates = filterCol_date.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => DateTime.TryParse(v.Trim(), out var d) ? d.Date : (DateTime?)null)
                    .Where(d => d.HasValue).Select(d => d!.Value)
                    .ToList();
                if (dates.Count > 0)
                    q = q.Where(sr => dates.Contains(sr.SRDate.Date));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_time))
            {
                var times = filterCol_time.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => TimeSpan.TryParse(v.Trim(), out var t) ? t : (TimeSpan?)null)
                    .Where(t => t.HasValue).Select(t => t!.Value)
                    .ToList();
                if (times.Count > 0)
                    q = q.Where(sr => times.Contains(sr.SRTime));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_netExpr))
                q = SalesReturnListNumericExpr.ApplyNetExpr(q, filterCol_netExpr);
            else if (!string.IsNullOrWhiteSpace(filterCol_net))
            {
                var nets = filterCol_net.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => decimal.TryParse(v.Trim(), out var d) ? d : (decimal?)null)
                    .Where(d => d.HasValue).Select(d => d!.Value)
                    .ToList();
                if (nets.Count > 0)
                    q = q.Where(sr => nets.Contains(sr.NetTotal));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_ref))
            {
                var refs = filterCol_ref.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => int.TryParse(v.Trim(), out var d) ? d : (int?)null)
                    .Where(d => d.HasValue).Select(d => d!.Value)
                    .ToList();
                if (refs.Count > 0)
                    q = q.Where(sr => sr.SalesInvoiceId.HasValue && refs.Contains(sr.SalesInvoiceId.Value));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_region))
            {
                var vals = filterCol_region.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(sr =>
                        vals.Contains(
                            sr.Customer != null
                                ? (sr.Customer.Area != null
                                    ? sr.Customer.Area.AreaName
                                    : (sr.Customer.RegionName ?? ""))
                                : ""));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_createdby))
            {
                var vals = filterCol_createdby.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(sr => sr.CreatedBy != null && vals.Contains(sr.CreatedBy));
            }

            ViewBag.TotalCountForCards = await q.CountAsync();
            ViewBag.TotalNetForCards = await q.SumAsync(sr => (decimal?)sr.NetTotal) ?? 0m;

            // (2) إنشاء PagedResult جاهز للفيو (0 = عرض الكل بحد أقصى للذاكرة)
            var sortDesc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            PagedResult<SalesReturn> model;
            if (pageSize == 0)
            {
                var totalAll = await q.CountAsync();
                var effectiveTake = totalAll == 0 ? 10 : Math.Min(totalAll, 100_000);
                var itemsAll = await q.Take(effectiveTake).ToListAsync();
                model = new PagedResult<SalesReturn>(itemsAll, 1, 0, totalAll)
                {
                    SortColumn = sort,
                    SortDescending = sortDesc,
                    Search = search,
                    SearchBy = searchBy
                };
            }
            else
            {
                model = await PagedResult<SalesReturn>.CreateAsync(
                    q, page, pageSize, sort, sortDesc, search, searchBy);
            }

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
                new("المنطقة",       "Region")     { Selected = sort == "Region" },
                new("الكاتب",        "CreatedBy") { Selected = sort == "CreatedBy" },
            };

            // (4) تخزين حالة الفلاتر فى ViewBag ليستعملها الفيو
            ViewBag.Search = search ?? "";
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.Sort = sort ?? "SRDate";
            ViewBag.Dir = (dir?.ToLower() == "asc") ? "asc" : "desc";

            // قيم فلاتر الأعمدة
            ViewBag.FilterCol_id = filterCol_id ?? string.Empty;
            ViewBag.FilterCol_idExpr = filterCol_idExpr ?? string.Empty;
            ViewBag.FilterCol_date = filterCol_date ?? string.Empty;
            ViewBag.FilterCol_time = filterCol_time ?? string.Empty;
            ViewBag.FilterCol_customer = filterCol_customer ?? string.Empty;
            ViewBag.FilterCol_warehouse = filterCol_warehouse ?? string.Empty;
            ViewBag.FilterCol_ref = filterCol_ref ?? string.Empty;
            ViewBag.FilterCol_net = filterCol_net ?? string.Empty;
            ViewBag.FilterCol_netExpr = filterCol_netExpr ?? string.Empty;
            ViewBag.FilterCol_status = filterCol_status ?? string.Empty;
            ViewBag.FilterCol_posted = filterCol_posted ?? string.Empty;
            ViewBag.FilterCol_region = filterCol_region ?? string.Empty;
            ViewBag.FilterCol_createdby = filterCol_createdby ?? string.Empty;

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
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_date = null,
            string? filterCol_time = null,
            string? filterCol_customer = null,
            string? filterCol_warehouse = null,
            string? filterCol_ref = null,
            string? filterCol_net = null,
            string? filterCol_netExpr = null,
            string? filterCol_status = null,
            string? filterCol_posted = null,
            string? filterCol_region = null,
            string? filterCol_createdby = null,
            string? format = "excel")        // excel | csv
        {
            // نفس منطق الفلاتر المستخدم فى Index
            var q = BuildQuery(search, searchBy, sort, dir);

            // فلاتر الأعمدة (نفس Index)
            if (!string.IsNullOrWhiteSpace(filterCol_idExpr))
                q = SalesReturnListNumericExpr.ApplySrIdExpr(q, filterCol_idExpr);
            else if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value)
                    .ToList();
                if (ids.Count > 0)
                    q = q.Where(sr => ids.Contains(sr.SRId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_customer))
            {
                var vals = filterCol_customer.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(sr =>
                        vals.Contains(
                            sr.Customer != null
                                ? (sr.Customer.CustomerName ?? sr.CustomerId.ToString())
                                : sr.CustomerId.ToString()
                        ));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_warehouse))
            {
                var vals = filterCol_warehouse.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(sr =>
                        vals.Contains(
                            sr.Warehouse != null
                                ? (sr.Warehouse.WarehouseName ?? sr.WarehouseId.ToString())
                                : sr.WarehouseId.ToString()
                        ));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_status))
            {
                var vals = filterCol_status.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(sr => sr.Status != null && vals.Contains(sr.Status));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_posted))
            {
                var vals = filterCol_posted.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim().ToLowerInvariant())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();
                if (vals.Count > 0)
                {
                    bool includeTrue = vals.Any(v => v == "نعم" || v == "yes" || v == "1" || v == "true");
                    bool includeFalse = vals.Any(v => v == "لا" || v == "no" || v == "0" || v == "false");
                    if (includeTrue && !includeFalse)
                        q = q.Where(sr => sr.IsPosted);
                    else if (includeFalse && !includeTrue)
                        q = q.Where(sr => !sr.IsPosted);
                }
            }

            if (!string.IsNullOrWhiteSpace(filterCol_date))
            {
                var dates = filterCol_date.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => DateTime.TryParse(v.Trim(), out var d) ? d.Date : (DateTime?)null)
                    .Where(d => d.HasValue).Select(d => d!.Value)
                    .ToList();
                if (dates.Count > 0)
                    q = q.Where(sr => dates.Contains(sr.SRDate.Date));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_time))
            {
                var times = filterCol_time.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => TimeSpan.TryParse(v.Trim(), out var t) ? t : (TimeSpan?)null)
                    .Where(t => t.HasValue).Select(t => t!.Value)
                    .ToList();
                if (times.Count > 0)
                    q = q.Where(sr => times.Contains(sr.SRTime));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_netExpr))
                q = SalesReturnListNumericExpr.ApplyNetExpr(q, filterCol_netExpr);
            else if (!string.IsNullOrWhiteSpace(filterCol_net))
            {
                var nets = filterCol_net.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => decimal.TryParse(v.Trim(), out var d) ? d : (decimal?)null)
                    .Where(d => d.HasValue).Select(d => d!.Value)
                    .ToList();
                if (nets.Count > 0)
                    q = q.Where(sr => nets.Contains(sr.NetTotal));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_ref))
            {
                var refs = filterCol_ref.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => int.TryParse(v.Trim(), out var d) ? d : (int?)null)
                    .Where(d => d.HasValue).Select(d => d!.Value)
                    .ToList();
                if (refs.Count > 0)
                    q = q.Where(sr => sr.SalesInvoiceId.HasValue && refs.Contains(sr.SalesInvoiceId.Value));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_region))
            {
                var vals = filterCol_region.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(sr =>
                        vals.Contains(
                            sr.Customer != null
                                ? (sr.Customer.Area != null
                                    ? sr.Customer.Area.AreaName
                                    : (sr.Customer.RegionName ?? ""))
                                : ""));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_createdby))
            {
                var vals = filterCol_createdby.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(sr => sr.CreatedBy != null && vals.Contains(sr.CreatedBy));
            }

            // ترتيب افتراضى بالتاريخ ثم الرقم
            q = q.OrderBy(sr => sr.SRDate).ThenBy(sr => sr.SRId);
            var list = await q.ToListAsync();

            format = (format ?? "excel").Trim().ToLowerInvariant();

            if (format == "csv")
            {
                var sb = new StringBuilder();
                sb.AppendLine("رقم المرتجع,تاريخ المرتجع,الوقت,العميل,المخزن,المنطقة,الكاتب,رقم الفاتورة,الصافي,الحالة,مرحّل");

                foreach (var x in list)
                {
                    string timeStr = x.SRTime.ToString(@"hh\:mm");
                    var customerText = x.Customer != null ? x.Customer.CustomerName : x.CustomerId.ToString();
                    var warehouseText = x.Warehouse != null ? x.Warehouse.WarehouseName : x.WarehouseId.ToString();
                    var regionText = x.Customer != null
                        ? (x.Customer.Area != null ? x.Customer.Area.AreaName : (x.Customer.RegionName ?? ""))
                        : "";

                    var line = string.Join(",",
                        x.SRId,
                        x.SRDate.ToString("yyyy-MM-dd"),
                        timeStr,
                        customerText.Replace(",", " "),
                        warehouseText.Replace(",", " "),
                        regionText.Replace(",", " "),
                        (x.CreatedBy ?? "").Replace(",", " "),
                        x.SalesInvoiceId?.ToString() ?? "",
                        x.NetTotal.ToString("0.00"),
                        (x.Status ?? "").Replace(",", " "),
                        x.IsPosted ? "نعم" : "لا"
                    );

                    sb.AppendLine(line);
                }

                var utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
                var bytesCsv = utf8Bom.GetBytes(sb.ToString());
                var fileNameCsv = ExcelExportNaming.ArabicTimestampedFileName("مرتجعات البيع", ".csv");

                return File(bytesCsv, "text/csv; charset=utf-8", fileNameCsv);
            }
            else
            {
                using var workbook = new XLWorkbook();
                var ws = workbook.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("مرتجعات البيع"));

                int r = 1;
                ws.Cell(r, 1).Value = "رقم المرتجع";
                ws.Cell(r, 2).Value = "تاريخ المرتجع";
                ws.Cell(r, 3).Value = "الوقت";
                ws.Cell(r, 4).Value = "العميل";
                ws.Cell(r, 5).Value = "المخزن";
                ws.Cell(r, 6).Value = "المنطقة";
                ws.Cell(r, 7).Value = "الكاتب";
                ws.Cell(r, 8).Value = "رقم الفاتورة";
                ws.Cell(r, 9).Value = "الصافي";
                ws.Cell(r, 10).Value = "الحالة";
                ws.Cell(r, 11).Value = "مرحل؟";

                var headerRange = ws.Range(r, 1, r, 11);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                foreach (var x in list)
                {
                    r++;
                    ws.Cell(r, 1).Value = x.SRId;
                    ws.Cell(r, 2).Value = x.SRDate;
                    ws.Cell(r, 3).Value = DateTime.Today.Add(x.SRTime);
                    ws.Cell(r, 4).Value = x.Customer != null ? x.Customer.CustomerName : x.CustomerId.ToString();
                    ws.Cell(r, 5).Value = x.Warehouse != null ? x.Warehouse.WarehouseName : x.WarehouseId.ToString();
                    ws.Cell(r, 6).Value = x.Customer != null
                        ? (x.Customer.Area != null ? x.Customer.Area.AreaName : (x.Customer.RegionName ?? ""))
                        : "";
                    ws.Cell(r, 7).Value = x.CreatedBy ?? "";
                    ws.Cell(r, 8).Value = x.SalesInvoiceId;
                    ws.Cell(r, 9).Value = x.NetTotal;
                    ws.Cell(r, 10).Value = x.Status ?? "";
                    ws.Cell(r, 11).Value = x.IsPosted ? "نعم" : "لا";
                }

                ws.Columns().AdjustToContents();
                ws.Column(2).Style.DateFormat.Format = "yyyy-mm-dd";
                ws.Column(3).Style.DateFormat.Format = "hh:mm";

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                stream.Position = 0;

                var fileNameXlsx = ExcelExportNaming.ArabicTimestampedFileName("مرتجعات البيع", ".xlsx");
                const string contentTypeXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

                return File(stream.ToArray(), contentTypeXlsx, fileNameXlsx);
            }
        }

        // =========================================================
        // API: جلب أصناف فاتورة البيع عند إدخال رقم الفاتورة
        // =========================================================
        // =========================================================
        // GetColumnValues — قيم مميزة لكل عمود لنمط فلترة الأعمدة (مثل فواتير المبيعات)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            if (string.IsNullOrWhiteSpace(column))
                return Json(Array.Empty<string>());

            column = column.ToLowerInvariant();
            search = (search ?? string.Empty).Trim();

            IQueryable<SalesReturn> q = context.SalesReturns
                .AsNoTracking()
                .Include(sr => sr.Customer)
                    .ThenInclude(c => c.Area)
                .Include(sr => sr.Warehouse);

            if (column == "id")
            {
                var idsQuery = q.Select(sr => sr.SRId.ToString());
                if (!string.IsNullOrEmpty(search))
                    idsQuery = idsQuery.Where(v => v.Contains(search));
                var ids = await idsQuery.Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(ids);
            }

            if (column == "date")
            {
                // نجلب التواريخ أولاً من قاعدة البيانات ثم ننسّقها كنص فى الذاكرة
                var datesQuery = q.Select(sr => sr.SRDate.Date);
                var rawDates = await datesQuery
                    .Distinct()
                    .OrderBy(v => v)
                    .Take(200)
                    .ToListAsync();

                var list = rawDates
                    .Select(d => d.ToString("yyyy-MM-dd"))
                    .Where(v => string.IsNullOrEmpty(search) || v.Contains(search))
                    .ToList();

                return Json(list);
            }

            if (column == "time")
            {
                // نرجّع قيم الوقت كمجموعة TimeSpan أولاً ثم نحوّلها إلى نص فى الذاكرة
                var timesQuery = q.Select(sr => sr.SRTime);
                var rawTimes = await timesQuery
                    .Distinct()
                    .OrderBy(v => v)
                    .Take(200)
                    .ToListAsync();

                var list = rawTimes
                    .Select(t => t.ToString(@"hh\:mm"))
                    .Where(v => string.IsNullOrEmpty(search) || v.Contains(search))
                    .ToList();

                return Json(list);
            }

            if (column == "customer")
            {
                var custQuery = q.Select(sr =>
                    sr.Customer != null
                        ? sr.Customer.CustomerName
                        : sr.CustomerId.ToString());
                if (!string.IsNullOrEmpty(search))
                    custQuery = custQuery.Where(v => v.Contains(search));
                var customers = await custQuery.Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(customers);
            }

            if (column == "warehouse")
            {
                var whQuery = q.Select(sr =>
                    sr.Warehouse != null
                        ? sr.Warehouse.WarehouseName
                        : sr.WarehouseId.ToString());
                if (!string.IsNullOrEmpty(search))
                    whQuery = whQuery.Where(v => v.Contains(search));
                var warehouses = await whQuery.Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(warehouses);
            }

            if (column == "ref")
            {
                var refQuery = q.Where(sr => sr.SalesInvoiceId.HasValue)
                                .Select(sr => sr.SalesInvoiceId!.Value.ToString());
                if (!string.IsNullOrEmpty(search))
                    refQuery = refQuery.Where(v => v.Contains(search));
                var refs = await refQuery.Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(refs);
            }

            if (column == "net")
            {
                var netQuery = q.Select(sr => sr.NetTotal.ToString("0.00"));
                if (!string.IsNullOrEmpty(search))
                    netQuery = netQuery.Where(v => v.Contains(search));
                var nets = await netQuery.Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(nets);
            }

            if (column == "status")
            {
                var statusQuery = q.Select(sr => sr.Status ?? "");
                if (!string.IsNullOrEmpty(search))
                    statusQuery = statusQuery.Where(v => v.Contains(search));
                var statuses = await statusQuery.Where(v => v != "")
                    .Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(statuses);
            }

            if (column == "posted")
            {
                var values = new List<string> { "نعم", "لا" };
                if (!string.IsNullOrEmpty(search))
                    values = values.Where(v => v.Contains(search)).ToList();
                return Json(values);
            }

            if (column == "region")
            {
                var regQuery = q.Select(sr =>
                    sr.Customer != null
                        ? (sr.Customer.Area != null ? sr.Customer.Area.AreaName : (sr.Customer.RegionName ?? ""))
                        : "");
                if (!string.IsNullOrEmpty(search))
                    regQuery = regQuery.Where(v => v.Contains(search));
                var regions = await regQuery.Where(v => v != "")
                    .Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(regions);
            }

            if (column == "createdby")
            {
                var byQuery = q.Select(sr => sr.CreatedBy ?? "");
                if (!string.IsNullOrEmpty(search))
                    byQuery = byQuery.Where(v => v.Contains(search));
                var authors = await byQuery.Where(v => v != "")
                    .Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(authors);
            }

            return Json(Array.Empty<string>());
        }

        /// <summary>
        /// بحث أصناف لإضافة سطر في مرتجع بدون فاتورة (واجهة Show).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> SearchProductsForReturn(string term)
        {
            if (string.IsNullOrWhiteSpace(term))
                return Json(Array.Empty<object>());

            term = term.Trim();

            var results = await context.Products
                .AsNoTracking()
                .Where(p => p.IsActive && (
                    (p.ProdName != null && p.ProdName.Contains(term)) ||
                    p.ProdId.ToString().Contains(term) ||
                    (p.Barcode != null && p.Barcode.Contains(term))))
                .OrderBy(p => p.ProdName)
                .Take(25)
                .Select(p => new
                {
                    id = p.ProdId,
                    name = p.ProdName ?? "",
                    priceRetail = p.PriceRetail,
                    barcode = p.Barcode,
                    company = p.Company
                })
                .ToListAsync();

            return Json(results);
        }

        /// <summary>
        /// قائمة أصناف لداتاليست مرتجع بدون فاتورة — أصناف لها رصيد (حسب المخزن إن وُجد)، مع سعر الجمهور من جدول الصنف.
        /// يُحدَّث من الواجهة عند تغيير المخزن (نفس فكرة فاتورة المبيعات).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetProductsForReturnDatalist(int? warehouseId = null)
        {
            var productsQuery = context.Products.AsNoTracking()
                .Where(p => p.IsActive)
                .OrderBy(p => p.ProdName);

            var prodIdsWithStock = context.StockLedger
                .AsNoTracking()
                .Where(sl => sl.QtyIn > 0 && (sl.RemainingQty ?? 0) > 0)
                .Select(sl => sl.ProdId);
            if (warehouseId.HasValue && warehouseId.Value > 0)
            {
                prodIdsWithStock = context.StockLedger
                    .AsNoTracking()
                    .Where(sl => sl.WarehouseId == warehouseId.Value && sl.QtyIn > 0 && (sl.RemainingQty ?? 0) > 0)
                    .Select(sl => sl.ProdId);
            }

            var ids = await prodIdsWithStock.Distinct().ToListAsync();
            productsQuery = productsQuery.Where(p => ids.Contains(p.ProdId)).OrderBy(p => p.ProdName);

            var products = await productsQuery
                .Select(p => new
                {
                    id = p.ProdId,
                    name = p.ProdName ?? string.Empty,
                    genericName = p.GenericName ?? string.Empty,
                    company = p.Company ?? string.Empty,
                    barcode = p.Barcode ?? string.Empty,
                    priceRetail = p.PriceRetail,
                    hasQuota = p.HasQuota,
                    hasBonus = p.ProductBonusGroupId != null,
                    bonusGroupName = p.ProductBonusGroup != null ? p.ProductBonusGroup.Name : null
                })
                .ToListAsync();

            return Json(products);
        }

        [HttpGet]
        public async Task<IActionResult> GetInvoiceItems(int invoiceId)
        {
            try
            {
                if (invoiceId <= 0)
                    return Json(new { ok = false, message = "رقم الفاتورة غير صحيح." });

                var invoice = await context.SalesInvoices
                    .AsNoTracking()
                    .FirstOrDefaultAsync(si => si.SIId == invoiceId);

                if (invoice == null)
                    return Json(new { ok = false, message = "الفاتورة غير موجودة." });

                var lines = await context.SalesInvoiceLines
                    .AsNoTracking()
                    .Where(l => l.SIId == invoiceId)
                    .OrderBy(l => l.LineNo)
                    .Select(l => new
                    {
                        lineNo = l.LineNo,
                        prodId = l.ProdId,
                        prodName = l.Product != null ? (l.Product.ProdName ?? "") : "",
                        externalCode = l.Product != null ? l.Product.ExternalCode : null,
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

                var returnLineRows = await context.SalesReturnLines
                    .AsNoTracking()
                    .Where(l => l.SalesInvoiceId == invoiceId && l.SalesInvoiceLineNo != null)
                    .Select(l => new { l.SalesInvoiceLineNo, l.Qty })
                    .ToListAsync();

                var returnedDict = returnLineRows
                    .GroupBy(x => x.SalesInvoiceLineNo!.Value)
                    .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));

                var items = lines.Select(l => new
                {
                    l.lineNo,
                    l.prodId,
                    l.prodName,
                    l.externalCode,
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
            catch (Exception ex)
            {
                return Json(new { ok = false, message = "تعذر تحميل بيانات الفاتورة: " + ex.Message });
            }
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

                // مرتجع بدون فاتورة: إدخال مخزون قابل للبيع. مرتجع من فاتورة: نفس منطق البيع الوهمي (بدون FIFO) إن وُجد.
                bool originalSaleHadFifo = true;
                if (dto.SalesInvoiceId.HasValue && dto.SalesInvoiceId.Value > 0 && dto.SalesInvoiceLineNo.HasValue)
                {
                    var saleLedgers = await context.StockLedger
                        .Where(x => x.SourceType == "Sales" && x.SourceId == dto.SalesInvoiceId.Value && x.SourceLine == dto.SalesInvoiceLineNo.Value && x.QtyOut > 0)
                        .Select(x => x.EntryId)
                        .ToListAsync();
                    originalSaleHadFifo = saleLedgers.Count > 0 && await context.Set<StockFifoMap>().AnyAsync(f => saleLedgers.Contains(f.OutEntryId));
                }

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
                    RemainingQty = originalSaleHadFifo ? dto.Qty : 0, // مرتجع من بيع وهمي لا يُنشئ رصيد قابل للاستهلاك
                    SourceType = "SalesReturn",
                    SourceId = dto.SRId,
                    SourceLine = nextLineNo,
                    Note = "Sales Return Line"
                });
                await context.SaveChangesAsync();

                if (originalSaleHadFifo && !string.IsNullOrWhiteSpace(batchNo) && exp.HasValue)
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
            var prodRows = await context.Products.AsNoTracking().Where(p => prodIds.Contains(p.ProdId)).Select(p => new { p.ProdId, p.ProdName, p.Location }).ToListAsync();
            var prodMap = prodRows.ToDictionary(x => x.ProdId, x => x.ProdName ?? "");
            var prodLocMap = prodRows.ToDictionary(x => x.ProdId, x => string.IsNullOrWhiteSpace(x.Location) ? "—" : x.Location!.Trim());
            var linesDto = linesNow.Select(l => new
            {
                lineNo = l.LineNo,
                prodId = l.ProdId,
                prodName = prodMap.TryGetValue(l.ProdId, out var n) ? n : "",
                location = prodLocMap.TryGetValue(l.ProdId, out var lc) ? lc : "—",
                qty = l.Qty,
                priceRetail = l.PriceRetail,
                disc1Percent = l.Disc1Percent,
                batchNo = l.BatchNo ?? "",
                expiry = l.Expiry.HasValue ? l.Expiry.Value.ToString("yyyy-MM-dd") : "",
                lineTotalAfterDiscount = l.LineTotalAfterDiscount
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
            var prodRowsRm = await context.Products.AsNoTracking().Where(p => prodIds.Contains(p.ProdId)).Select(p => new { p.ProdId, p.ProdName, p.Location }).ToListAsync();
            var prodMapRm = prodRowsRm.ToDictionary(x => x.ProdId, x => x.ProdName ?? "");
            var prodLocMapRm = prodRowsRm.ToDictionary(x => x.ProdId, x => string.IsNullOrWhiteSpace(x.Location) ? "—" : x.Location!.Trim());
            var linesDto = linesNow.Select(l => new
            {
                lineNo = l.LineNo,
                prodId = l.ProdId,
                prodName = prodMapRm.TryGetValue(l.ProdId, out var n) ? n : "",
                location = prodLocMapRm.TryGetValue(l.ProdId, out var lc) ? lc : "—",
                qty = l.Qty,
                priceRetail = l.PriceRetail,
                disc1Percent = l.Disc1Percent,
                batchNo = l.BatchNo ?? "",
                expiry = l.Expiry.HasValue ? l.Expiry.Value.ToString("yyyy-MM-dd") : "",
                lineTotalAfterDiscount = l.LineTotalAfterDiscount
            }).ToList();
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
            var prodRowsCl = await context.Products.AsNoTracking().Where(p => prodIds.Contains(p.ProdId)).Select(p => new { p.ProdId, p.ProdName, p.Location }).ToListAsync();
            var prodMapCl = prodRowsCl.ToDictionary(x => x.ProdId, x => x.ProdName ?? "");
            var prodLocMapCl = prodRowsCl.ToDictionary(x => x.ProdId, x => string.IsNullOrWhiteSpace(x.Location) ? "—" : x.Location!.Trim());
            var linesDto = linesNow.Select(l => new
            {
                lineNo = l.LineNo,
                prodId = l.ProdId,
                prodName = prodMapCl.TryGetValue(l.ProdId, out var n) ? n : "",
                location = prodLocMapCl.TryGetValue(l.ProdId, out var lc) ? lc : "—",
                qty = l.Qty,
                priceRetail = l.PriceRetail,
                disc1Percent = l.Disc1Percent,
                batchNo = l.BatchNo ?? "",
                expiry = l.Expiry.HasValue ? l.Expiry.Value.ToString("yyyy-MM-dd") : "",
                lineTotalAfterDiscount = l.LineTotalAfterDiscount
            }).ToList();
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
                var oldValues = System.Text.Json.JsonSerializer.Serialize(new { ret.SRDate, ret.CustomerId, ret.WarehouseId, ret.NetTotal });
                context.SalesReturns.Remove(ret);

                await context.SaveChangesAsync();
                await tx.CommitAsync();

                await _activityLogger.LogAsync(UserActionType.Delete, "SalesReturn", id, $"حذف مرتجع بيع رقم {id}", oldValues: oldValues);

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
