using ERP.Data;                                  // AppDbContext
using ERP.Filters;
using ERP.Infrastructure;                        // PagedResult + UserActivityLogger
using ERP.Models;                                // StockTransfer, UserActionType...
using ERP.Security;
using ERP.Services;                              // ILedgerPostingService
using Microsoft.AspNetCore.Mvc;                  // أساس الكنترولر
using Microsoft.AspNetCore.Mvc.Rendering;        // SelectList و SelectListItem
using Microsoft.EntityFrameworkCore;             // Include, AsNoTracking, ToListAsync
using Microsoft.Extensions.DependencyInjection;  // GetRequiredService
using System;                                     // متغيرات التوقيت DateTime
using System.Collections.Generic;                // القوائم List و ICollection
using System.Globalization;
using System.IO;                                  // MemoryStream لتصدير Excel
using System.Linq;                               // أوامر LINQ مثل Where و OrderBy
using System.Text;                               // لبناء نص ملف التصدير StringBuilder
using System.Threading.Tasks;                    // Task و async
using ClosedXML.Excel;                           // تصدير Excel

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إدارة التحويلات بين المخازن (الهيدر فقط).
    /// يطبق نظام القوائم الموحد:
    /// بحث + فلترة + ترتيب + تقسيم صفحات + حذف جماعي + حذف الكل + تصدير.
    /// </summary>
    [RequirePermission("StockTransfers.Index")]
    public class StockTransfersController : Controller
    {
        private readonly AppDbContext _context;
        private readonly StockAnalysisService _stockAnalysisService;
        private readonly IUserActivityLogger _activityLogger;
        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        public StockTransfersController(AppDbContext context, StockAnalysisService stockAnalysisService, IUserActivityLogger activityLogger)
        {
            _context = context;
            _stockAnalysisService = stockAnalysisService;
            _activityLogger = activityLogger;
        }

        #region Index (قائمة التحويلات بالنظام الموحد)

        /// <summary>
        /// عرض قائمة التحويلات بين المخازن مع:
        /// - بحث عام أو مخصص حسب الحقل
        /// - فلترة بالتاريخ
        /// - فلترة من كود / إلى كود
        /// - ترتيب الأعمدة
        /// - تقسيم الصفحات باستخدام PagedResult
        /// </summary>
        public async Task<IActionResult> Index(
            string? search,          // متغير: نص البحث
            string? searchBy,        // متغير: البحث بأي حقل (all, id, fromWarehouse, toWarehouse, note, date)
            string? sort,            // متغير: اسم عمود الترتيب
            string? dir,             // متغير: اتجاه الترتيب asc/desc
            int page = 1,            // متغير: رقم الصفحة الحالية
            int pageSize = 20,       // متغير: عدد السجلات في الصفحة
            bool useDateRange = false,   // متغير: هل نستخدم فلتر التاريخ؟
            DateTime? fromDate = null,   // متغير: تاريخ من
            DateTime? toDate = null,     // متغير: تاريخ إلى
            int? fromCode = null,        // متغير: كود من
            int? toCode = null,          // متغير: كود إلى
            string? filterCol_id = null,
            string? filterCol_date = null,
            string? filterCol_fromwarehouse = null,
            string? filterCol_towarehouse = null,
            string? filterCol_note = null,
            string? filterCol_created = null,
            string? filterCol_updated = null
        )
        {
            // قيم افتراضية
            searchBy ??= "all";
            sort ??= "id";
            bool descending = (dir == "desc");

            // نبدأ باستعلام بسيط بدون Include
            IQueryable<StockTransfer> baseQuery = _context.StockTransfers
                .AsNoTracking(); // متغير: استعلام أساسي على جدول التحويلات

            // نطبق الفلاتر العامة (بحث + كود + تاريخ + ترتيب)
            var filteredQuery = ApplyFiltersAndSorting(
                baseQuery,
                search,
                searchBy,
                sort,
                ref descending,
                useDateRange,
                fromDate,
                toDate,
                fromCode,
                toCode
            );

            // فلاتر الأعمدة (بنمط Excel)
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    filteredQuery = filteredQuery.Where(t => ids.Contains(t.Id));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_fromwarehouse))
            {
                var ids = filterCol_fromwarehouse.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    filteredQuery = filteredQuery.Where(t => ids.Contains(t.FromWarehouseId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_towarehouse))
            {
                var ids = filterCol_towarehouse.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    filteredQuery = filteredQuery.Where(t => ids.Contains(t.ToWarehouseId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_note))
            {
                var vals = filterCol_note.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    filteredQuery = filteredQuery.Where(t => t.Note != null && vals.Contains(t.Note));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_date))
            {
                var dates = filterCol_date.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => DateTime.TryParse(x.Trim(), out var d) ? d.Date : (DateTime?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (dates.Count > 0)
                    filteredQuery = filteredQuery.Where(t => dates.Contains(t.TransferDate.Date));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_created))
            {
                var dates = filterCol_created.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => DateTime.TryParse(x.Trim(), out var d) ? d.Date : (DateTime?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (dates.Count > 0)
                    filteredQuery = filteredQuery.Where(t => dates.Contains(t.CreatedAt.Date));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_updated))
            {
                var dates = filterCol_updated.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => DateTime.TryParse(x.Trim(), out var d) ? d.Date : (DateTime?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (dates.Count > 0)
                    filteredQuery = filteredQuery.Where(t => t.UpdatedAt.HasValue && dates.Contains(t.UpdatedAt.Value.Date));
            }

            // بعد الفلاتر نضيف الـ Include (مش هتأثر على نوع المتغير)
            var query = filteredQuery
                .Include(t => t.FromWarehouse)    // متغير: جلب بيانات المخزن المصدر
                .Include(t => t.ToWarehouse);     // متغير: جلب بيانات المخزن الوجهة

            // إنشاء نتيجة مقسمة إلى صفحات باستخدام PagedResult
            var result = await PagedResult<StockTransfer>.CreateAsync(
                query,
                page,
                pageSize,
                search,
                descending,
                sort,
                searchBy
            );

            // تعبئة خصائص إضافية داخل الـ PagedResult حتى تستخدمها الواجهة (الفلاتر)
            result.SearchBy = searchBy;
            result.SortColumn = sort;
            result.SortDescending = descending;
            result.UseDateRange = useDateRange;
            result.FromDate = fromDate;
            result.ToDate = toDate;
            // FromCode / ToCode غير موجودة في PagedResult حالياً، فهنستخدم ViewBag لهم

            // نرسل قيم من/إلى كود للواجهة عن طريق ViewBag
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;

            // حفظ قيم الترتيب في ViewBag عشان الـ sortLink في الواجهة
            ViewBag.Sort = sort;
            ViewBag.Dir = descending ? "desc" : "asc";

            // تمرير فلاتر الأعمدة إلى الواجهة
            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_Date = filterCol_date;
            ViewBag.FilterCol_FromWarehouse = filterCol_fromwarehouse;
            ViewBag.FilterCol_ToWarehouse = filterCol_towarehouse;
            ViewBag.FilterCol_Note = filterCol_note;
            ViewBag.FilterCol_Created = filterCol_created;
            ViewBag.FilterCol_Updated = filterCol_updated;

            return View(result);
        }

        /// <summary>
        /// دالة مساعدة لتطبيق:
        /// - البحث
        /// - فلتر التاريخ
        /// - فلتر من كود/إلى كود
        /// - الترتيب
        /// على استعلام StockTransfer.
        /// نستخدمها في Index و Export لتوحيد المنطق.
        /// </summary>
        private IQueryable<StockTransfer> ApplyFiltersAndSorting(
            IQueryable<StockTransfer> query,
            string? search,
            string? searchBy,
            string? sort,
            ref bool descending,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            int? fromCode,
            int? toCode
        )
        {
            // 1) فلتر التاريخ (نستخدم تاريخ التحويل)
            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                DateTime from = fromDate.Value.Date;
                DateTime to = toDate.Value.Date.AddDays(1).AddTicks(-1); // نهاية اليوم

                query = query.Where(t => t.TransferDate >= from && t.TransferDate <= to);
            }

            // 2) فلتر من كود / إلى كود
            if (fromCode.HasValue)
            {
                int codeFrom = fromCode.Value;
                query = query.Where(t => t.Id >= codeFrom);
            }

            if (toCode.HasValue)
            {
                int codeTo = toCode.Value;
                query = query.Where(t => t.Id <= codeTo);
            }

            // 3) البحث العام/المتخصص
            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = search.Trim();
                searchBy = searchBy?.ToLowerInvariant();

                switch (searchBy)
                {
                    case "id":
                        if (int.TryParse(term, out int idValue))
                            query = query.Where(t => t.Id == idValue);
                        else
                            query = query.Where(t => false);
                        break;

                    case "fromwarehouse":
                        query = query.Where(t =>
                            t.FromWarehouseId.ToString().Contains(term));
                        break;

                    case "towarehouse":
                        query = query.Where(t =>
                            t.ToWarehouseId.ToString().Contains(term));
                        break;

                    case "note":
                        query = query.Where(t =>
                            t.Note != null && t.Note.Contains(term));
                        break;

                    case "date":
                        if (DateTime.TryParse(term, out DateTime d))
                        {
                            DateTime dFrom = d.Date;
                            DateTime dTo = d.Date.AddDays(1).AddTicks(-1);
                            query = query.Where(t => t.TransferDate >= dFrom && t.TransferDate <= dTo);
                        }
                        break;

                    case "all":
                    default:
                        query = query.Where(t =>
                            t.Id.ToString().Contains(term) ||
                            t.FromWarehouseId.ToString().Contains(term) ||
                            t.ToWarehouseId.ToString().Contains(term) ||
                            (t.Note != null && t.Note.Contains(term)));
                        break;
                }
            }

            // 4) الترتيب
            sort = (sort ?? "id").ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(sort))
                sort = "id";

            switch (sort)
            {
                case "id":
                    query = descending
                        ? query.OrderByDescending(t => t.Id)
                        : query.OrderBy(t => t.Id);
                    break;

                case "date":
                    query = descending
                        ? query.OrderByDescending(t => t.TransferDate)
                        : query.OrderBy(t => t.TransferDate);
                    break;

                case "fromwarehouse":
                    query = descending
                        ? query.OrderByDescending(t => t.FromWarehouseId)
                        : query.OrderBy(t => t.FromWarehouseId);
                    break;

                case "towarehouse":
                    query = descending
                        ? query.OrderByDescending(t => t.ToWarehouseId)
                        : query.OrderBy(t => t.ToWarehouseId);
                    break;

                case "created":
                    query = descending
                        ? query.OrderByDescending(t => t.CreatedAt)
                        : query.OrderBy(t => t.CreatedAt);
                    break;

                case "updated":
                    query = descending
                        ? query.OrderByDescending(t => t.UpdatedAt)
                        : query.OrderBy(t => t.UpdatedAt);
                    break;

                default:
                    sort = "id";
                    descending = true;
                    query = query.OrderByDescending(t => t.Id);
                    break;
            }

            return query;
        }

        #endregion

        #region Details (عرض تفاصيل تحويل واحد)

        /// <summary>
        /// عرض تفاصيل تحويل واحد (رأس + سطور).
        /// </summary>
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
                return NotFound();

            var transfer = await _context.StockTransfers
                .AsNoTracking()
                .Include(t => t.FromWarehouse)
                .Include(t => t.ToWarehouse)
                .Include(t => t.Lines)
                    .ThenInclude(l => l.Product)
                .Include(t => t.Lines)
                    .ThenInclude(l => l.Batch)
                .FirstOrDefaultAsync(t => t.Id == id.Value);

            if (transfer == null)
                return NotFound();

            return View(transfer);
        }

        #endregion

        #region Show (عرض التحويل مع إضافة السطور والترحيل)

        [HttpGet]
        public async Task<IActionResult> Show(int id, string? frag = null, int? frame = null)
        {
            bool isBodyOnly = string.Equals(frag, "body", StringComparison.OrdinalIgnoreCase);

            if (!isBodyOnly && frame != 1)
                return RedirectToAction(nameof(Show), new { id = id, frag = frag, frame = 1 });

            ViewBag.Fragment = frag;

            StockTransfer? transfer = null;

            if (id > 0)
            {
                transfer = await _context.StockTransfers
                    .Include(t => t.FromWarehouse)
                    .Include(t => t.ToWarehouse)
                    .Include(t => t.Lines)
                        .ThenInclude(l => l.Product)
                    .Include(t => t.Lines)
                        .ThenInclude(l => l.Batch)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == id);

                if (transfer == null)
                {
                    if (isBodyOnly)
                        return NotFound("التحويل غير موجود.");
                    TempData["Error"] = "التحويل غير موجود.";
                    return RedirectToAction("Index");
                }
            }
            else
            {
                // إنشاء تحويل جديد
                transfer = new StockTransfer
                {
                    Id = 0,
                    TransferDate = DateTime.Now,
                    FromWarehouseId = 0,
                    ToWarehouseId = 0,
                    IsPosted = false,
                    Status = "مسودة",
                    Lines = new List<StockTransferLine>()
                };
            }

            await PopulateWarehousesDropDowns(
                transfer.FromWarehouseId > 0 ? transfer.FromWarehouseId : null,
                transfer.ToWarehouseId > 0 ? transfer.ToWarehouseId : null
            );

            // تجهيز قائمة المنتجات للأوتوكومبليت
            var products = await _context.Products
                .AsNoTracking()
                .OrderBy(p => p.ProdName)
                .Select(p => new
                {
                    Id = p.ProdId,
                    Name = p.ProdName ?? string.Empty,
                    GenericName = p.GenericName ?? string.Empty,
                    Company = p.Company ?? string.Empty,
                    HasQuota = p.HasQuota,
                    PriceRetail = p.PriceRetail
                })
                .ToListAsync();

            ViewBag.ProductsAuto = products;

            ViewBag.IsLocked = transfer.IsPosted || transfer.Status == "Posted" || transfer.Status == "Closed";
            ViewBag.Frame = (!isBodyOnly) ? 1 : 0;

            return View("Show", transfer);
        }

        #endregion

        #region Create (إضافة تحويل جديد)

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            await PopulateWarehousesDropDowns();
            return View(new StockTransfer
            {
                TransferDate = DateTime.Now
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            [Bind("TransferDate,FromWarehouseId,ToWarehouseId,Note,UserId")]
            StockTransfer stockTransfer
        )
        {
            if (stockTransfer.FromWarehouseId == stockTransfer.ToWarehouseId)
            {
                ModelState.AddModelError(nameof(StockTransfer.ToWarehouseId),
                    "لا يمكن أن يكون المخزن المصدر هو نفس المخزن الوجهة.");
            }

            if (!ModelState.IsValid)
            {
                await PopulateWarehousesDropDowns(
                    stockTransfer.FromWarehouseId,
                    stockTransfer.ToWarehouseId
                );
                return View(stockTransfer);
            }

            stockTransfer.CreatedAt = DateTime.Now;
            stockTransfer.UpdatedAt = null;

            _context.StockTransfers.Add(stockTransfer);
            await _context.SaveChangesAsync();

            await _activityLogger.LogAsync(UserActionType.Create, "StockTransfer", stockTransfer.Id, $"إنشاء تحويل مخزني رقم {stockTransfer.Id}");

            TempData["SuccessMessage"] = "تم إضافة التحويل المخزني بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        #endregion

        #region Edit (تعديل تحويل)

        [HttpGet]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
                return NotFound();

            var transfer = await _context.StockTransfers.FindAsync(id.Value);
            if (transfer == null)
                return NotFound();

            await PopulateWarehousesDropDowns(
                transfer.FromWarehouseId,
                transfer.ToWarehouseId
            );

            return View(transfer);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            int id,
            [Bind("Id,TransferDate,FromWarehouseId,ToWarehouseId,Note,UserId,CreatedAt")]
            StockTransfer stockTransfer
        )
        {
            if (id != stockTransfer.Id)
                return NotFound();

            if (stockTransfer.FromWarehouseId == stockTransfer.ToWarehouseId)
            {
                ModelState.AddModelError(nameof(StockTransfer.ToWarehouseId),
                    "لا يمكن أن يكون المخزن المصدر هو نفس المخزن الوجهة.");
            }

            if (!ModelState.IsValid)
            {
                await PopulateWarehousesDropDowns(
                    stockTransfer.FromWarehouseId,
                    stockTransfer.ToWarehouseId
                );
                return View(stockTransfer);
            }

            try
            {
                var existing = await _context.StockTransfers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
                var oldValues = existing != null ? System.Text.Json.JsonSerializer.Serialize(new { existing.TransferDate, existing.FromWarehouseId, existing.ToWarehouseId, existing.Note }) : null;
                stockTransfer.UpdatedAt = DateTime.Now;

                _context.Update(stockTransfer);
                await _context.SaveChangesAsync();

                var newValues = System.Text.Json.JsonSerializer.Serialize(new { stockTransfer.TransferDate, stockTransfer.FromWarehouseId, stockTransfer.ToWarehouseId, stockTransfer.Note });
                await _activityLogger.LogAsync(UserActionType.Edit, "StockTransfer", stockTransfer.Id, $"تعديل تحويل مخزني رقم {stockTransfer.Id}", oldValues, newValues);

                TempData["SuccessMessage"] = "تم تعديل التحويل المخزني بنجاح.";
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await StockTransferExists(stockTransfer.Id))
                    return NotFound();
                else
                    throw;
            }
        }

        #endregion

        #region Delete (حذف واحد) + BulkDelete + DeleteAll

        [HttpGet]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
                return NotFound();

            var transfer = await _context.StockTransfers
                .AsNoTracking()
                .Include(t => t.FromWarehouse)
                .Include(t => t.ToWarehouse)
                .FirstOrDefaultAsync(t => t.Id == id.Value);

            if (transfer == null)
                return NotFound();

            return View(transfer);
        }

        /// <summary>
        /// حذف تحويل واحد (مثل المبيعات/المشتريات: الحذف من القائمة بغض النظر عن الترحيل).
        /// إذا كان مترحلاً: نعكس الترحيل أولاً ثم نحذف.
        /// </summary>
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var transfer = await _context.StockTransfers
                .Include(t => t.Lines)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (transfer == null)
            {
                TempData["ErrorMessage"] = "لم يتم العثور على التحويل المطلوب حذفه.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var oldValues = System.Text.Json.JsonSerializer.Serialize(new { transfer.TransferDate, transfer.FromWarehouseId, transfer.ToWarehouseId, transfer.Note });
                if (transfer.IsPosted)
                {
                    var ledgerPostingService = HttpContext.RequestServices.GetRequiredService<ILedgerPostingService>();
                    await ledgerPostingService.ReverseStockTransferAsync(id, User?.Identity?.Name ?? "SYSTEM");
                }

                _context.StockTransfers.Remove(transfer);
                await _context.SaveChangesAsync();

                await _activityLogger.LogAsync(UserActionType.Delete, "StockTransfer", id, $"حذف تحويل مخزني رقم {id}", oldValues: oldValues);

                TempData["SuccessMessage"] = $"تم حذف التحويل رقم {id} بنجاح.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"تعذر حذف التحويل: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// حذف مجموعة مختارة من التحويلات (مثل المبيعات/المشتريات: بغض النظر عن الترحيل).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["ErrorMessage"] = "لم يتم اختيار أي سجلات للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var ids = selectedIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var n) ? (int?)n : null)
                .Where(n => n.HasValue)
                .Select(n => n!.Value)
                .Distinct()
                .ToList();

            if (!ids.Any())
            {
                TempData["ErrorMessage"] = "لم يتم اختيار أكواد صحيحة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var ledgerPostingService = HttpContext.RequestServices.GetRequiredService<ILedgerPostingService>();
            int deletedCount = 0;
            var failedIds = new List<int>();

            foreach (var id in ids)
            {
                try
                {
                    var transfer = await _context.StockTransfers
                        .Include(t => t.Lines)
                        .FirstOrDefaultAsync(t => t.Id == id);

                    if (transfer == null)
                        continue;

                    if (transfer.IsPosted)
                        await ledgerPostingService.ReverseStockTransferAsync(id, User?.Identity?.Name ?? "SYSTEM");

                    _context.StockTransfers.Remove(transfer);
                    await _context.SaveChangesAsync();
                    deletedCount++;
                }
                catch
                {
                    failedIds.Add(id);
                }
            }

            if (deletedCount > 0)
                TempData["SuccessMessage"] = failedIds.Any()
                    ? $"تم حذف {deletedCount} تحويل. فشل حذف: {string.Join(", ", failedIds)}"
                    : $"تم حذف {deletedCount} تحويل(ات) بنجاح.";
            if (failedIds.Any())
                TempData["ErrorMessage"] = $"فشل حذف التحويلات: {string.Join(", ", failedIds)}";

            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// حذف جميع التحويلات (مثل المبيعات/المشتريات: بغض النظر عن الترحيل).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var ids = await _context.StockTransfers.Select(t => t.Id).ToListAsync();

            if (!ids.Any())
            {
                TempData["ErrorMessage"] = "لا توجد سجلات لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            var ledgerPostingService = HttpContext.RequestServices.GetRequiredService<ILedgerPostingService>();
            int deletedCount = 0;
            var failedIds = new List<int>();

            foreach (var id in ids)
            {
                try
                {
                    var transfer = await _context.StockTransfers
                        .Include(t => t.Lines)
                        .FirstOrDefaultAsync(t => t.Id == id);

                    if (transfer == null)
                        continue;

                    if (transfer.IsPosted)
                        await ledgerPostingService.ReverseStockTransferAsync(id, User?.Identity?.Name ?? "SYSTEM");

                    _context.StockTransfers.Remove(transfer);
                    await _context.SaveChangesAsync();
                    deletedCount++;
                }
                catch
                {
                    failedIds.Add(id);
                }
            }

            if (deletedCount > 0)
                TempData["SuccessMessage"] = failedIds.Any()
                    ? $"تم حذف {deletedCount} تحويل. فشل حذف: {string.Join(", ", failedIds)}"
                    : $"تم حذف {deletedCount} تحويل بنجاح.";
            if (failedIds.Any())
                TempData["ErrorMessage"] = $"فشل حذف التحويلات: {string.Join(", ", failedIds)}";

            return RedirectToAction(nameof(Index));
        }

        #endregion

        #region Export (تصدير CSV)

        // =========================================================
        // Export — تصدير التحويلات بين المخازن إلى CSV
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy = "all",     // متغير: نوع البحث (كل الحقول / كود / من مخزن / إلى مخزن / ...)
            string? sort = "id",          // متغير: عمود الترتيب الافتراضي
            string? dir = "asc",          // متغير: اتجاه الترتيب asc / desc
            bool useDateRange = false,    // متغير: هل نستخدم فلتر التاريخ أم لا
            DateTime? fromDate = null,    // متغير: تاريخ من
            DateTime? toDate = null,      // متغير: تاريخ إلى
            int? fromCode = null,         // متغير: فلتر من كود
            int? toCode = null,           // متغير: فلتر إلى كود
            string? filterCol_id = null,
            string? filterCol_date = null,
            string? filterCol_fromwarehouse = null,
            string? filterCol_towarehouse = null,
            string? filterCol_note = null,
            string? filterCol_created = null,
            string? filterCol_updated = null,
            string format = "excel")      // excel | csv (الاثنين حالياً يخرجوا CSV)
        {
            // متغير: هل الترتيب تنازلي؟
            bool descending = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            // 1) نبني الاستعلام الأساسي بدون فلاتر (مع عدم التتبع لتحسين الأداء)
            IQueryable<StockTransfer> baseQuery = _context.StockTransfers
                .AsNoTracking();

            // 2) نطبق الفلاتر والترتيب بنفس دالة Index (نفس النتائج العامة تماماً)
            var filtered = ApplyFiltersAndSorting(
                baseQuery,
                search,
                searchBy,
                sort,
                ref descending,
                useDateRange,
                fromDate,
                toDate,
                fromCode,
                toCode);

            // 2.1 فلاتر الأعمدة (بنفس منطق Index)
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    filtered = filtered.Where(t => ids.Contains(t.Id));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_fromwarehouse))
            {
                var ids = filterCol_fromwarehouse.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    filtered = filtered.Where(t => ids.Contains(t.FromWarehouseId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_towarehouse))
            {
                var ids = filterCol_towarehouse.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    filtered = filtered.Where(t => ids.Contains(t.ToWarehouseId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_note))
            {
                var vals = filterCol_note.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    filtered = filtered.Where(t => t.Note != null && vals.Contains(t.Note));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_date))
            {
                var dates = filterCol_date.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => DateTime.TryParse(x.Trim(), out var d) ? d.Date : (DateTime?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (dates.Count > 0)
                    filtered = filtered.Where(t => dates.Contains(t.TransferDate.Date));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_created))
            {
                var dates = filterCol_created.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => DateTime.TryParse(x.Trim(), out var d) ? d.Date : (DateTime?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (dates.Count > 0)
                    filtered = filtered.Where(t => dates.Contains(t.CreatedAt.Date));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_updated))
            {
                var dates = filterCol_updated.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => DateTime.TryParse(x.Trim(), out var d) ? d.Date : (DateTime?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (dates.Count > 0)
                    filtered = filtered.Where(t => t.UpdatedAt.HasValue && dates.Contains(t.UpdatedAt.Value.Date));
            }

            // 3) نضيف Include لجلب أسماء/أكواد المخازن (من/إلى)
            var query = filtered
                .Include(t => t.FromWarehouse)   // متغير: المخزن المحوَّل منه
                .Include(t => t.ToWarehouse);    // متغير: المخزن المحوَّل إليه

            // 4) نحصل على كل النتائج فى قائمة
            var list = await query.ToListAsync();

            // 5) نقرر الصيغة بناءً على format
            format = (format ?? "excel").Trim().ToLowerInvariant();

            if (format == "csv")
            {
                // ===== فرع CSV =====
                var sb = new StringBuilder();

                // عناوين الأعمدة في ملف CSV
                sb.AppendLine("Id,TransferDate,FromWarehouseId,ToWarehouseId,FromWarehouseName,ToWarehouseName,Note,CreatedAt,UpdatedAt");

                // كل تحويل في سطر CSV
                foreach (var t in list)
                {
                    // متغير: اسم مخزن من / إلى بدون فواصل
                    var fromName = (t.FromWarehouse?.WarehouseName ?? "").Replace(",", " ");
                    var toName = (t.ToWarehouse?.WarehouseName ?? "").Replace(",", " ");
                    var note = (t.Note ?? "").Replace(",", " ");

                    string line = string.Join(",",
                        t.Id,   // كود التحويل
                        t.TransferDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), // تاريخ التحويل
                        t.FromWarehouseId,   // كود من مخزن
                        t.ToWarehouseId,     // كود إلى مخزن
                        fromName,            // اسم من مخزن
                        toName,              // اسم إلى مخزن
                        note,                // الملاحظات
                        t.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),    // تاريخ الإنشاء
                        t.UpdatedAt.HasValue
                            ? t.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                            : ""              // آخر تعديل (فارغ لو مفيش)
                    );

                    sb.AppendLine(line);
                }

                // استخدام UTF-8 مع BOM علشان Excel يقرأ عربى صح
                var utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
                var bytesCsv = utf8Bom.GetBytes(sb.ToString());
                var fileNameCsv = $"StockTransfers_{DateTime.Now:yyyyMMdd_HHmm}.csv";
                const string contentTypeCsv = "text/csv; charset=utf-8";

                return File(bytesCsv, contentTypeCsv, fileNameCsv);
            }
            else
            {
                // ===== فرع Excel (XLSX) =====
                using var workbook = new XLWorkbook();
                var ws = workbook.Worksheets.Add("StockTransfers");

                int r = 1;
                ws.Cell(r, 1).Value = "كود التحويل";
                ws.Cell(r, 2).Value = "تاريخ التحويل";
                ws.Cell(r, 3).Value = "كود من مخزن";
                ws.Cell(r, 4).Value = "كود إلى مخزن";
                ws.Cell(r, 5).Value = "من مخزن";
                ws.Cell(r, 6).Value = "إلى مخزن";
                ws.Cell(r, 7).Value = "الملاحظات";
                ws.Cell(r, 8).Value = "تاريخ الإنشاء";
                ws.Cell(r, 9).Value = "آخر تعديل";

                var headerRange = ws.Range(r, 1, r, 9);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                foreach (var t in list)
                {
                    r++;
                    ws.Cell(r, 1).Value = t.Id;
                    ws.Cell(r, 2).Value = t.TransferDate;
                    ws.Cell(r, 3).Value = t.FromWarehouseId;
                    ws.Cell(r, 4).Value = t.ToWarehouseId;
                    ws.Cell(r, 5).Value = t.FromWarehouse?.WarehouseName ?? "";
                    ws.Cell(r, 6).Value = t.ToWarehouse?.WarehouseName ?? "";
                    ws.Cell(r, 7).Value = t.Note ?? "";
                    ws.Cell(r, 8).Value = t.CreatedAt;
                    ws.Cell(r, 9).Value = t.UpdatedAt;
                }

                ws.Columns().AdjustToContents();
                ws.Column(2).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                ws.Column(8).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                ws.Column(9).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                stream.Position = 0;

                var fileNameXlsx = $"StockTransfers_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
                const string contentTypeXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

                return File(stream.ToArray(), contentTypeXlsx, fileNameXlsx);
            }
        }

        /// <summary>
        /// قيم الأعمدة المميزة لاستخدامها في فلاتر الأعمدة بنمط Excel.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var col = column?.Trim().ToLowerInvariant() ?? "";

            IQueryable<StockTransfer> q = _context.StockTransfers.AsNoTracking();

            List<(string Value, string Display)> items = col switch
            {
                "id" => (await q.Select(t => t.Id).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "fromwarehouse" => (await q.Select(t => t.FromWarehouseId).Distinct().OrderBy(v => v).Take(200).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "towarehouse" => (await q.Select(t => t.ToWarehouseId).Distinct().OrderBy(v => v).Take(200).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "note" => (await q.Where(t => t.Note != null).Select(t => t.Note!).Distinct().OrderBy(v => v).Take(300).ToListAsync())
                    .Select(v => (v, v)).ToList(),
                "date" => (await q.Select(t => t.TransferDate.Date).Distinct().OrderByDescending(d => d).Take(200).ToListAsync())
                    .Select(d => (d.ToString("yyyy-MM-dd"), d.ToString("yyyy/MM/dd"))).ToList(),
                "created" => (await q.Select(t => t.CreatedAt.Date).Distinct().OrderByDescending(d => d).Take(200).ToListAsync())
                    .Select(d => (d.ToString("yyyy-MM-dd"), d.ToString("yyyy/MM/dd"))).ToList(),
                "updated" => (await q.Where(t => t.UpdatedAt.HasValue).Select(t => t.UpdatedAt!.Value.Date).Distinct().OrderByDescending(d => d).Take(200).ToListAsync())
                    .Select(d => (d.ToString("yyyy-MM-dd"), d.ToString("yyyy/MM/dd"))).ToList(),
                _ => new List<(string Value, string Display)>()
            };

            if (!string.IsNullOrEmpty(searchTerm) && items.Count > 0)
            {
                items = items
                    .Where(x => (x.Display ?? x.Value).ToLowerInvariant().Contains(searchTerm))
                    .ToList();
            }

            return Json(items.Select(x => new { value = x.Value, display = x.Display }));
        }

        #endregion

        #region Helpers

        private async Task PopulateWarehousesDropDowns(int? fromSelectedId = null, int? toSelectedId = null)
        {
            var warehouses = await _context.Warehouses
                .AsNoTracking()
                .Where(w => w.IsActive)
                .OrderBy(w => w.WarehouseName)
                .                Select(w => new
                {
                    w.WarehouseId,
                    w.WarehouseName
                })
                .ToListAsync();

            ViewData["FromWarehouseId"] = new SelectList(
                warehouses,
                "WarehouseId",
                "WarehouseName",
                fromSelectedId
            );

            ViewData["ToWarehouseId"] = new SelectList(
                warehouses,
                "WarehouseId",
                "WarehouseName",
                toSelectedId
            );
        }

        private async Task<bool> StockTransferExists(int id)
        {
            return await _context.StockTransfers.AnyAsync(t => t.Id == id);
        }

        // =========================
        // CreateHeaderJson — إنشاء/حفظ رأس التحويل (JSON API)
        // =========================
        [HttpPost]
        public async Task<IActionResult> CreateHeaderJson([FromBody] StockTransferHeaderDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { ok = false, message = "بيانات غير صحيحة." });
            }

            if (dto.FromWarehouseId <= 0 || dto.ToWarehouseId <= 0)
            {
                return BadRequest(new { ok = false, message = "يجب اختيار المخازن صحيحة." });
            }

            if (dto.FromWarehouseId == dto.ToWarehouseId)
            {
                return BadRequest(new { ok = false, message = "لا يمكن أن يكون المخزن المصدر هو نفس المخزن الوجهة." });
            }

            var transfer = new StockTransfer
            {
                TransferDate = dto.TransferDate,
                FromWarehouseId = dto.FromWarehouseId,
                ToWarehouseId = dto.ToWarehouseId,
                Note = dto.Note,
                CreatedAt = DateTime.UtcNow,
                IsPosted = false,
                Status = "مسودة"
            };

            _context.StockTransfers.Add(transfer);
            await _context.SaveChangesAsync();

            return Json(new { ok = true, id = transfer.Id });
        }

        // =========================
        // UpdateHeaderJson — تحديث رأس التحويل (JSON API)
        // =========================
        [HttpPost]
        public async Task<IActionResult> UpdateHeaderJson([FromBody] StockTransferHeaderDto dto)
        {
            if (!ModelState.IsValid || dto.Id <= 0)
            {
                return BadRequest(new { ok = false, message = "بيانات غير صحيحة." });
            }

            var transfer = await _context.StockTransfers.FindAsync(dto.Id);
            if (transfer == null)
            {
                return NotFound(new { ok = false, message = "التحويل غير موجود." });
            }

            if (transfer.IsPosted)
            {
                return BadRequest(new { ok = false, message = "لا يمكن تعديل تحويل مترحل." });
            }

            if (dto.FromWarehouseId == dto.ToWarehouseId)
            {
                return BadRequest(new { ok = false, message = "لا يمكن أن يكون المخزن المصدر هو نفس المخزن الوجهة." });
            }

            transfer.TransferDate = dto.TransferDate;
            transfer.FromWarehouseId = dto.FromWarehouseId;
            transfer.ToWarehouseId = dto.ToWarehouseId;
            transfer.Note = dto.Note;
            transfer.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Json(new { ok = true, id = transfer.Id });
        }

        // =========================
        // GetTransferProductInfo — جلب بيانات الصنف للتحويل (تشغيلات، سعر، خصم مرجح)
        // =========================
        [HttpGet]
        public async Task<IActionResult> GetTransferProductInfo(int prodId, int fromWarehouseId)
        {
            if (prodId <= 0 || fromWarehouseId <= 0)
                return Json(new { ok = false, message = "بيانات غير صحيحة." });

            var product = await _context.Products
                .AsNoTracking()
                .Where(p => p.ProdId == prodId)
                .Select(p => new { p.ProdId, p.ProdName, p.PriceRetail })
                .FirstOrDefaultAsync();

            if (product == null)
                return Json(new { ok = false, message = "الصنف غير موجود." });

            var stockBatches = await _context.StockBatches
                .AsNoTracking()
                .Where(sb => sb.ProdId == prodId && sb.WarehouseId == fromWarehouseId && sb.QtyOnHand > 0)
                .OrderBy(sb => sb.Expiry)
                .ThenBy(sb => sb.BatchNo)
                .ToListAsync();

            var batchInfos = new List<TransferBatchInfo>();
            foreach (var sb in stockBatches)
            {
                var batch = await _context.Batches
                    .AsNoTracking()
                    .Where(b => b.ProdId == prodId && b.BatchNo == sb.BatchNo &&
                        (sb.Expiry == null || b.Expiry.Date == sb.Expiry.Value.Date))
                    .Select(b => new { b.BatchId, b.PriceRetailBatch, b.UnitCostDefault })
                    .FirstOrDefaultAsync();
                batchInfos.Add(new TransferBatchInfo
                {
                    BatchId = batch?.BatchId ?? 0,
                    BatchNo = sb.BatchNo ?? "",
                    ExpiryText = sb.Expiry.HasValue ? sb.Expiry.Value.ToString("yyyy-MM-dd") : "",
                    Qty = sb.QtyOnHand,
                    PriceRetailBatch = batch?.PriceRetailBatch ?? product.PriceRetail,
                    UnitCost = batch?.UnitCostDefault ?? 0m
                });
            }

            int? firstBatchId = null;
            if (batchInfos.Count > 0)
                firstBatchId = batchInfos[0].BatchId;
            // الخصم الفعّال = خصم يدوي من ProductDiscountOverrides إن وُجد، وإلا المرجّح من StockLedger
            decimal weightedDiscount = await _stockAnalysisService.GetEffectivePurchaseDiscountAsync(prodId, fromWarehouseId, firstBatchId);

            decimal priceRetail = product.PriceRetail;
            decimal unitCost = 0m;
            string? firstBatchNo = null;
            string? firstExpiry = null;
            if (batchInfos.Count > 0)
            {
                var first = batchInfos[0];
                priceRetail = first.PriceRetailBatch;
                unitCost = first.UnitCost;
                firstBatchNo = first.BatchNo;
                firstExpiry = first.ExpiryText;
                firstBatchId = first.BatchId;
            }

            return Json(new
            {
                ok = true,
                prodId = product.ProdId,
                prodName = product.ProdName,
                priceRetail,
                unitCost,
                weightedDiscount,
                firstBatchNo = firstBatchNo ?? "",
                firstExpiry = firstExpiry ?? "",
                firstBatchId,
                batches = batchInfos.Select(b => new { b.BatchId, b.BatchNo, b.ExpiryText, b.Qty, b.PriceRetailBatch, b.UnitCost })
            });
        }

        // =========================
        // AddLineJson — إضافة سطر للتحويل (JSON API)
        // =========================
        [HttpPost]
        public async Task<IActionResult> AddLineJson([FromBody] StockTransferLineDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { ok = false, message = "بيانات غير صحيحة." });
            }

            var transfer = await _context.StockTransfers
                .Include(t => t.Lines)
                .FirstOrDefaultAsync(t => t.Id == dto.StockTransferId);

            if (transfer == null)
            {
                return NotFound(new { ok = false, message = "التحويل غير موجود." });
            }

            if (transfer.IsPosted)
            {
                return BadRequest(new { ok = false, message = "لا يمكن إضافة سطور لتحويل مترحل." });
            }

            if (dto.ProductId <= 0)
            {
                return BadRequest(new { ok = false, message = "يجب اختيار صنف صحيح." });
            }

            if (dto.Qty <= 0)
            {
                return BadRequest(new { ok = false, message = "الكمية يجب أن تكون أكبر من صفر." });
            }

            int? batchId = dto.BatchId;
            if (!batchId.HasValue && !string.IsNullOrWhiteSpace(dto.BatchNo))
            {
                var batch = await _context.Batches
                    .FirstOrDefaultAsync(b => b.BatchNo.Trim() == dto.BatchNo.Trim() && b.ProdId == dto.ProductId);
                if (batch != null)
                    batchId = batch.BatchId;
            }

            decimal unitCost = dto.UnitCost;
            if (unitCost <= 0 && batchId.HasValue)
            {
                var batch = await _context.Batches.FindAsync(batchId.Value);
                if (batch != null)
                    unitCost = batch.UnitCostDefault ?? 0m;
            }

            int nextLineNo = transfer.Lines.Any() ? transfer.Lines.Max(l => l.LineNo) + 1 : 1;

            var line = new StockTransferLine
            {
                StockTransferId = dto.StockTransferId,
                LineNo = nextLineNo,
                ProductId = dto.ProductId,
                BatchId = batchId,
                Qty = dto.Qty,
                UnitCost = unitCost,
                PriceRetail = dto.PriceRetail,
                WeightedDiscountPct = dto.WeightedDiscountPct,
                DiscountPct = dto.DiscountPct,
                Note = dto.Note
            };

            _context.StockTransferLines.Add(line);
            await _context.SaveChangesAsync();

            var product = await _context.Products.FindAsync(dto.ProductId);
            var batchEntity = batchId.HasValue ? await _context.Batches.FindAsync(batchId.Value) : null;
            return Json(new
            {
                ok = true,
                lineId = line.Id,
                isUpdate = false,
                productName = product?.ProdName ?? $"صنف #{dto.ProductId}",
                batchNo = batchEntity?.BatchNo ?? dto.BatchNo ?? "-",
                expiryDisplay = batchEntity?.Expiry.ToString("yyyy-MM-dd") ?? "-",
                qty = line.Qty,
                priceRetail = line.PriceRetail,
                weightedDiscountPct = line.WeightedDiscountPct,
                discountPct = line.DiscountPct,
                unitCost = line.UnitCost,
                total = line.Qty * line.UnitCost
            });
        }

        // =========================
        // DeleteLineJson — حذف سطر من التحويل (JSON API)
        // =========================
        [HttpPost]
        public async Task<IActionResult> DeleteLineJson(int id)
        {
            var line = await _context.StockTransferLines
                .Include(l => l.StockTransfer)
                .FirstOrDefaultAsync(l => l.Id == id);

            if (line == null)
            {
                return NotFound(new { ok = false, message = "السطر غير موجود." });
            }

            if (line.StockTransfer.IsPosted)
            {
                return BadRequest(new { ok = false, message = "لا يمكن حذف سطر من تحويل مترحل." });
            }

            _context.StockTransferLines.Remove(line);
            await _context.SaveChangesAsync();

            return Json(new { ok = true });
        }

        // =========================
        // ClearLinesJson — مسح كل سطور التحويل (JSON API)
        // =========================
        [HttpPost]
        public async Task<IActionResult> ClearLinesJson(int id)
        {
            var transfer = await _context.StockTransfers
                .Include(t => t.Lines)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (transfer == null)
            {
                return NotFound(new { ok = false, message = "التحويل غير موجود." });
            }

            if (transfer.IsPosted)
            {
                return BadRequest(new { ok = false, message = "لا يمكن مسح سطور تحويل مترحل." });
            }

            _context.StockTransferLines.RemoveRange(transfer.Lines);
            await _context.SaveChangesAsync();

            return Json(new { ok = true });
        }

        // =========================
        // PostTransfer — ترحيل التحويل (JSON API)
        // =========================
        [HttpPost]
        public async Task<IActionResult> PostTransfer(int id)
        {
            var transfer = await _context.StockTransfers
                .Include(t => t.Lines)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (transfer == null)
            {
                return NotFound(new { ok = false, message = "التحويل غير موجود." });
            }

            if (transfer.IsPosted)
            {
                return BadRequest(new { ok = false, message = "هذا التحويل مترحل بالفعل." });
            }

            if (!transfer.Lines.Any())
            {
                return BadRequest(new { ok = false, message = "لا يمكن ترحيل تحويل بدون سطور." });
            }

            // استدعاء خدمة الترحيل
            var ledgerPostingService = HttpContext.RequestServices.GetRequiredService<ILedgerPostingService>();
            await ledgerPostingService.PostStockTransferAsync(id, User?.Identity?.Name ?? "SYSTEM");

            return Json(new { ok = true, message = "تم الترحيل بنجاح." });
        }

        // =========================
        // OpenTransfer — فتح التحويل (JSON API)
        // =========================
        [HttpPost]
        public async Task<IActionResult> OpenTransfer(int id)
        {
            var transfer = await _context.StockTransfers.FindAsync(id);

            if (transfer == null)
            {
                return NotFound(new { ok = false, message = "التحويل غير موجود." });
            }

            if (!transfer.IsPosted)
            {
                return BadRequest(new { ok = false, message = "هذا التحويل غير مترحل." });
            }

            // استدعاء خدمة فتح التحويل (عكس الترحيل)
            var ledgerPostingService = HttpContext.RequestServices.GetRequiredService<ILedgerPostingService>();
            await ledgerPostingService.ReverseStockTransferAsync(id, User?.Identity?.Name ?? "SYSTEM");

            return Json(new { ok = true, message = "تم فتح التحويل بنجاح." });
        }
        #endregion
    }

    // =========================
    // DTOs
    // =========================
    public class StockTransferHeaderDto
    {
        public int Id { get; set; }
        public DateTime TransferDate { get; set; }
        public int FromWarehouseId { get; set; }
        public int ToWarehouseId { get; set; }
        public string? Note { get; set; }
    }

    public class TransferBatchInfo
    {
        public int BatchId { get; set; }
        public string BatchNo { get; set; } = "";
        public string ExpiryText { get; set; } = "";
        public decimal Qty { get; set; }
        public decimal PriceRetailBatch { get; set; }
        public decimal UnitCost { get; set; }
    }

    public class StockTransferLineDto
    {
        public int StockTransferId { get; set; }
        public int ProductId { get; set; }
        public string? BatchNo { get; set; }
        public int? BatchId { get; set; }
        public int Qty { get; set; }
        public decimal UnitCost { get; set; }
        public decimal? PriceRetail { get; set; }
        public decimal? WeightedDiscountPct { get; set; }
        public decimal? DiscountPct { get; set; }
        public string? Note { get; set; }
    }
}
