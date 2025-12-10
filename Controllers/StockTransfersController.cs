using ERP.Data;                                  // AppDbContext
using ERP.Infrastructure;                        // كلاس PagedResult لتقسيم الصفحات
using ERP.Models;                                // StockTransfer و Warehouse و StockTransferLine
using Microsoft.AspNetCore.Mvc;                  // أساس الكنترولر
using Microsoft.AspNetCore.Mvc.Rendering;        // SelectList و SelectListItem
using Microsoft.EntityFrameworkCore;             // Include, AsNoTracking, ToListAsync
using System;                                     // متغيرات التوقيت DateTime
using System.Collections.Generic;                // القوائم List و ICollection
using System.Globalization;
using System.Linq;                               // أوامر LINQ مثل Where و OrderBy
using System.Text;                               // لبناء نص ملف التصدير StringBuilder
using System.Threading.Tasks;                    // Task و async

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إدارة التحويلات بين المخازن (الهيدر فقط).
    /// يطبق نظام القوائم الموحد:
    /// بحث + فلترة + ترتيب + تقسيم صفحات + حذف جماعي + حذف الكل + تصدير.
    /// </summary>
    public class StockTransfersController : Controller
    {
        // كائن الاتصال بقاعدة البيانات
        private readonly AppDbContext _context;   // متغير: يحتفظ بالسياق للتعامل مع الجداول

        public StockTransfersController(AppDbContext context)
        {
            _context = context;
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
            int? toCode = null           // متغير: كود إلى
        )
        {
            // قيم افتراضية
            searchBy ??= "all";
            sort ??= "id";
            bool descending = (dir == "desc");

            // نبدأ باستعلام بسيط بدون Include
            IQueryable<StockTransfer> baseQuery = _context.StockTransfers
                .AsNoTracking(); // متغير: استعلام أساسي على جدول التحويلات

            // نطبق الفلاتر والبحث والترتيب على الاستعلام الأساسي
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
                stockTransfer.UpdatedAt = DateTime.Now;

                _context.Update(stockTransfer);
                await _context.SaveChangesAsync();

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

            _context.StockTransfers.Remove(transfer);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"تم حذف التحويل رقم {id} بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// حذف مجموعة مختارة من التحويلات (BulkDelete).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(int[] selectedIds)
        {
            if (selectedIds == null || selectedIds.Length == 0)
            {
                TempData["ErrorMessage"] = "لم يتم اختيار أي سجلات للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var items = await _context.StockTransfers
                .Where(t => selectedIds.Contains(t.Id))
                .ToListAsync();

            if (items.Count == 0)
            {
                TempData["ErrorMessage"] = "لم يتم العثور على السجلات المحددة.";
                return RedirectToAction(nameof(Index));
            }

            _context.StockTransfers.RemoveRange(items);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"تم حذف {items.Count} تحويل(ات) بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// حذف جميع التحويلات من الجدول (DeleteAll).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var allTransfers = await _context.StockTransfers.ToListAsync();

            if (allTransfers.Count == 0)
            {
                TempData["ErrorMessage"] = "لا توجد سجلات لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            _context.StockTransfers.RemoveRange(allTransfers);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "تم حذف جميع التحويلات المخزنية بنجاح.";
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
            string format = "excel")      // excel | csv (الاثنين حالياً يخرجوا CSV)
        {
            // متغير: هل الترتيب تنازلي؟
            bool descending = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            // 1) نبني الاستعلام الأساسي بدون فلاتر (مع عدم التتبع لتحسين الأداء)
            IQueryable<StockTransfer> baseQuery = _context.StockTransfers
                .AsNoTracking();

            // 2) نطبق الفلاتر والترتيب بنفس دالة Index (نفس النتائج تماماً)
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

            // 3) نضيف Include لجلب أسماء/أكواد المخازن (من/إلى)
            var query = filtered
                .Include(t => t.FromWarehouse)   // متغير: المخزن المحوَّل منه
                .Include(t => t.ToWarehouse);    // متغير: المخزن المحوَّل إليه

            // 4) نحصل على كل النتائج فى قائمة
            var list = await query.ToListAsync();

            // 5) نبني محتوى CSV فى StringBuilder
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

            // 6) تحويل النص إلى Bytes وتجهيزه كملف
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());

            // حالياً بغضّ النظر عن قيمة format (excel / csv) هنطلع CSV
            var fileName = "StockTransfers.csv";
            const string contentType = "text/csv";

            return File(bytes, contentType, fileName);
        }

        #endregion

        #region Helpers

        private async Task PopulateWarehousesDropDowns(int? fromSelectedId = null, int? toSelectedId = null)
        {
            var warehouses = await _context.Warehouses
                .AsNoTracking()
                .OrderBy(w => w.WarehouseId)
                .Select(w => new
                {
                    w.WarehouseId
                })
                .ToListAsync();

            ViewData["FromWarehouseId"] = new SelectList(
                warehouses,
                "WarehouseId",
                "WarehouseId",
                fromSelectedId
            );

            ViewData["ToWarehouseId"] = new SelectList(
                warehouses,
                "WarehouseId",
                "WarehouseId",
                toSelectedId
            );
        }

        private async Task<bool> StockTransferExists(int id)
        {
            return await _context.StockTransfers.AnyAsync(t => t.Id == id);
        }

        #endregion
    }
}
