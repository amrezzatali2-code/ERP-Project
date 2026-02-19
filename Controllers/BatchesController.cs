using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ClosedXML.Excel;                          // لتصدير Excel
using ERP.Data;                                // سياق قاعدة البيانات
using ERP.Infrastructure;                      // PagedResult + UserActivityLogger
using ERP.Models;                              // Batch, UserActionType...
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;      // القوائم المنسدلة
using Microsoft.EntityFrameworkCore;           // LINQ to Entities

namespace ERP.Controllers
{
    /// <summary>
    /// كونترولر إدارة جدول التشغيلات Batch:
    /// - عرض قائمة التشغيلات بنظام القوائم الموحد.
    /// - إضافة / تعديل / حذف تشغيلة.
    /// - حذف محدد / حذف الكل.
    /// - تصدير (Excel / CSV).
    /// </summary>
    public class BatchesController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IUserActivityLogger _activityLogger;

        public BatchesController(AppDbContext context, IUserActivityLogger activityLogger)
        {
            _db = context;
            _activityLogger = activityLogger;
        }

        // =========================================================
        // دالة مساعدة: تحميل قائمة الأصناف للكومبوبوكس
        // =========================================================
        private async Task FillProductsDropDownAsync(int? selectedProdId = null)
        {
            // نجيب الأصناف مرتبة بالاسم لسهولة الاختيار
            var products = await _db.Products
                .OrderBy(p => p.ProdName)
                .Select(p => new SelectListItem
                {
                    Value = p.ProdId.ToString(),          // لو عندك اسم مختلف غيّره هنا
                    Text = p.ProdName
                })
                .ToListAsync();

            ViewBag.Products = products;                     // متغير: القائمة المنسدلة في الفيو
            ViewBag.SelectedProdId = selectedProdId;         // متغير: الصنف المختار مبدئياً
        }





        // =========================================================
        // دالة مساعدة: تطبيق البحث / الفلترة / الترتيب
        // =========================================================
        private IQueryable<Batch> SearchSortFilter(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            string? dateField,
            int? fromCode,
            int? toCode)
        {
            // الاستعلام الأساسي من جدول Batch مع ربط اسم الصنف
            var q = _db.Batches
                .Include(b => b.Product)
                .AsNoTracking()
                .AsQueryable();

            // ------------------------------
            // فلتر التاريخ (CreatedAt / UpdatedAt)
            // ------------------------------
            bool dateFilterActive = useDateRange || fromDate.HasValue || toDate.HasValue;
            string df = string.IsNullOrWhiteSpace(dateField) ? "CreatedAt" : dateField;

            if (dateFilterActive)
            {
                bool filterOnUpdated = df.Equals("UpdatedAt", StringComparison.OrdinalIgnoreCase);

                if (filterOnUpdated)
                {
                    if (fromDate.HasValue)
                    {
                        q = q.Where(b => b.UpdatedAt.HasValue && b.UpdatedAt.Value >= fromDate.Value);
                    }
                    if (toDate.HasValue)
                    {
                        q = q.Where(b => b.UpdatedAt.HasValue && b.UpdatedAt.Value <= toDate.Value);
                    }
                }
                else
                {
                    if (fromDate.HasValue)
                    {
                        q = q.Where(b => b.CreatedAt >= fromDate.Value);
                    }
                    if (toDate.HasValue)
                    {
                        q = q.Where(b => b.CreatedAt <= toDate.Value);
                    }
                }
            }

            // ------------------------------
            // فلتر كود من/إلى (BatchId)
            // ------------------------------
            if (fromCode.HasValue)
            {
                int cf = fromCode.Value;
                q = q.Where(b => b.BatchId >= cf);
            }

            if (toCode.HasValue)
            {
                int ct = toCode.Value;
                q = q.Where(b => b.BatchId <= ct);
            }

            // ------------------------------
            // البحث (نص حر)
            // ------------------------------
            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = search.Trim();
                string sb = (searchBy ?? "batchno").ToLower();

                switch (sb)
                {
                    case "id":      // كود التشغيلة
                        q = q.Where(b => b.BatchId.ToString() == term);
                        break;

                    case "prod":    // كود الصنف أو اسمه
                        q = q.Where(b =>
                            b.ProdId.ToString() == term ||
                            (b.Product != null && b.Product.ProdName.Contains(term)));
                        break;

                    case "prodname":
                        q = q.Where(b => b.Product != null && b.Product.ProdName.Contains(term));
                        break;

                    case "expiry":
                        if (DateTime.TryParse(term, out var dtExp))
                        {
                            var dateOnly = dtExp.Date;
                            q = q.Where(b => b.Expiry.Date == dateOnly);
                        }
                        break;

                    case "created":
                        if (DateTime.TryParse(term, out var dtCr))
                        {
                            var dateOnly = dtCr.Date;
                            q = q.Where(b => b.CreatedAt.Date == dateOnly);
                        }
                        break;

                    case "updated":
                        if (DateTime.TryParse(term, out var dtUp))
                        {
                            var dateOnly = dtUp.Date;
                            q = q.Where(b =>
                                b.UpdatedAt.HasValue &&
                                b.UpdatedAt.Value.Date == dateOnly);
                        }
                        break;

                    case "batchno":
                    default:
                        q = q.Where(b => b.BatchNo.Contains(term));
                        break;
                }
            }

            // ------------------------------
            // الترتيب (مع Tie-breaker ثابت)
            // ------------------------------
            bool descending = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            string sortCol = (sort ?? "expiry").ToLower();

            IOrderedQueryable<Batch> ordered;

            switch (sortCol)
            {
                case "id":
                    ordered = descending
                        ? q.OrderByDescending(b => b.BatchId)
                        : q.OrderBy(b => b.BatchId);
                    break;

                case "batchno":
                    ordered = descending
                        ? q.OrderByDescending(b => b.BatchNo).ThenByDescending(b => b.BatchId)
                        : q.OrderBy(b => b.BatchNo).ThenBy(b => b.BatchId);
                    break;

                case "prod":
                    ordered = descending
                        ? q.OrderByDescending(b => b.Product!.ProdName).ThenByDescending(b => b.BatchId)
                        : q.OrderBy(b => b.Product!.ProdName).ThenBy(b => b.BatchId);
                    break;

                case "created":
                    ordered = descending
                        ? q.OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.BatchId)
                        : q.OrderBy(b => b.CreatedAt).ThenBy(b => b.BatchId);
                    break;

                case "updated":
                    ordered = descending
                        ? q.OrderByDescending(b => b.UpdatedAt).ThenByDescending(b => b.BatchId)
                        : q.OrderBy(b => b.UpdatedAt).ThenBy(b => b.BatchId);
                    break;

                case "expiry":
                default:
                    ordered = descending
                        ? q.OrderByDescending(b => b.Expiry).ThenByDescending(b => b.BatchId)
                        : q.OrderBy(b => b.Expiry).ThenBy(b => b.BatchId);
                    break;
            }

            return ordered;
        }








        // =========================================================
        // GET: /Batches
        // شاشة قائمة التشغيلات (النظام الموحد) - Paging يدوي (حل ثابت)
        // =========================================================
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            int page = 1,
            int pageSize = 25,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null)
        {
            // =========================
            // (1) قيم افتراضية + حماية Paging
            // =========================
            searchBy ??= "batchno";      // متغير: نوع البحث الافتراضي
            sort ??= "expiry";           // متغير: عمود الترتيب الافتراضي
            dir ??= "asc";               // متغير: اتجاه الترتيب الافتراضي

            if (page < 1) page = 1;
            if (pageSize <= 0) pageSize = 25;

            // حماية إضافية (اختياري) لمنع قيم غريبة
            if (pageSize < 10) pageSize = 10;
            if (pageSize > 500) pageSize = 500;

            // =========================
            // (2) استعلام واحد فقط: فلترة + بحث + ترتيب
            // =========================
            var query = SearchSortFilter(
                search,
                searchBy,
                sort,
                dir,
                useDateRange,
                fromDate,
                toDate,
                "CreatedAt",   // ثابت عندك
                fromCode,
                toCode);

            // =========================
            // (3) إجمالي العدد بعد الفلاتر
            // =========================
            int totalCount = await query.CountAsync();

            // حساب عدد الصفحات
            int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            if (totalPages < 1) totalPages = 1;

            // لو الصفحة الحالية أكبر من آخر صفحة (يحصل عند تغيير pageSize أو بعد فلترة)
            if (page > totalPages) page = 1;

            // =========================
            // (4) قراءة صفحة واحدة فقط (Skip/Take)
            // =========================
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // =========================
            // (5) تجهيز PagedResult يدويًا (نفس نمط فواتير المشتريات)
            // =========================
            bool sortDesc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            var model = new PagedResult<Batch>
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

            // =========================
            // (6) ViewBag لحفظ الحالة في الواجهة
            // =========================
            ViewBag.Search = search;
            ViewBag.SearchBy = searchBy;
            ViewBag.Sort = sort;
            ViewBag.Dir = sortDesc ? "desc" : "asc";

            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;

            return View(model);
        }







        // =========================================================
        // GET: /Batches/Create
        // فتح شاشة إضافة تشغيلة جديدة
        // =========================================================
        public async Task<IActionResult> Create()
        {
            await FillProductsDropDownAsync();
            return View(new Batch());    // متغير: موديل فارغ
        }








        // =========================================================
        // POST: /Batches/Create
        // حفظ تشغيلة جديدة
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Batch model)
        {
            if (!ModelState.IsValid)
            {
                // لو في أخطاء فاليديشن نرجع نفس الشاشة مع نفس البيانات
                await FillProductsDropDownAsync(model.ProdId);
                return View(model);
            }

            // لو EntryDate لسه Default نحط تاريخ اليوم
            if (model.EntryDate == default)
            {
                model.EntryDate = DateTime.UtcNow;
            }

            model.CreatedAt = DateTime.UtcNow;

            _db.Batches.Add(model);
            await _db.SaveChangesAsync();

            await _activityLogger.LogAsync(UserActionType.Create, "Batch", model.BatchId, $"إنشاء تشغيلة: {model.BatchNo}");

            TempData["Success"] = "تم إضافة التشغيلة بنجاح.";
            return RedirectToAction(nameof(Index));
        }







        // =========================================================
        // GET: /Batches/Edit/5
        // فتح شاشة تعديل تشغيلة
        // =========================================================
        public async Task<IActionResult> Edit(int id)
        {
            var batch = await _db.Batches.FindAsync(id);
            if (batch == null)
            {
                return NotFound();
            }

            await FillProductsDropDownAsync(batch.ProdId);
            return View(batch);
        }







        // =========================================================
        // POST: /Batches/Edit/5
        // حفظ تعديل التشغيلة
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Batch model)
        {
            if (id != model.BatchId)
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                await FillProductsDropDownAsync(model.ProdId);
                return View(model);
            }

            try
            {
                model.UpdatedAt = DateTime.UtcNow;   // تحديث آخر تعديل
                _db.Batches.Update(model);
                await _db.SaveChangesAsync();

                await _activityLogger.LogAsync(UserActionType.Edit, "Batch", id, $"تعديل تشغيلة: {model.BatchNo}");

                TempData["Success"] = "تم حفظ تعديلات التشغيلة بنجاح.";
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _db.Batches.AnyAsync(b => b.BatchId == id))
                {
                    return NotFound();
                }
                throw;
            }
        }








        // =========================================================
        // GET: /Batches/Show/5
        // عرض تفاصيل تشغيلة واحدة
        // =========================================================
        public async Task<IActionResult> Show(int id)
        {
            var batch = await _db.Batches
                .Include(b => b.Product)
                .Include(b => b.Customer)
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.BatchId == id);

            if (batch == null)
            {
                return NotFound();
            }

            return View(batch);
        }







        // =========================================================
        // GET: /Batches/Delete/5
        // تأكيد حذف تشغيلة
        // =========================================================
        public async Task<IActionResult> Delete(int id)
        {
            var batch = await _db.Batches
                .Include(b => b.Product)
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.BatchId == id);

            if (batch == null)
            {
                return NotFound();
            }

            return View(batch);
        }






        // =========================================================
        // POST: /Batches/Delete/5
        // تنفيذ الحذف
        // =========================================================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var batch = await _db.Batches.FindAsync(id);
            if (batch != null)
            {
                _db.Batches.Remove(batch);
                await _db.SaveChangesAsync();

                await _activityLogger.LogAsync(UserActionType.Delete, "Batch", id, $"حذف تشغيلة: {batch?.BatchNo}");

                TempData["Success"] = "تم حذف التشغيلة.";
            }

            return RedirectToAction(nameof(Index));
        }







        // =========================================================
        // POST: /Batches/BulkDelete
        // حذف مجموعة تشغيلات من جدول الاندكس
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["Error"] = "لم يتم اختيار أي تشغيلة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var ids = selectedIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => int.TryParse(id, out var v) ? v : (int?)null)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();

            if (ids.Count == 0)
            {
                TempData["Error"] = "لم يتم اختيار تشغيلات صالحة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var batches = await _db.Batches
                .Where(b => ids.Contains(b.BatchId))
                .ToListAsync();

            _db.Batches.RemoveRange(batches);
            await _db.SaveChangesAsync();

            TempData["Success"] = "تم حذف التشغيلات المحددة.";
            return RedirectToAction(nameof(Index));
        }








        // =========================================================
        // POST: /Batches/DeleteAll
        // حذف جميع تشغيلات الجدول
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _db.Batches.ToListAsync();
            _db.Batches.RemoveRange(all);
            await _db.SaveChangesAsync();

            TempData["Success"] = "تم حذف جميع التشغيلات.";
            return RedirectToAction(nameof(Index));
        }








        // =========================================================
        // GET: /Batches/Export
        // تصدير (Excel / CSV) بنفس فلاتر الاندكس
        // =========================================================
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
            string format = "excel")
        {
            var query = SearchSortFilter(
                search,
                searchBy,
                sort,
                dir,
                useDateRange,
                fromDate,
                toDate,
                "CreatedAt",
                fromCode,
                toCode);

            var data = await query.ToListAsync();

            if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
            {
                // تصدير CSV بسيط
                var sb = new StringBuilder();
                sb.AppendLine("BatchId,ProdId,BatchNo,Expiry,PriceRetailBatch,UnitCostDefault,EntryDate,CreatedAt,UpdatedAt,IsActive");

                foreach (var b in data)
                {
                    sb.AppendLine(string.Join(",",
                        b.BatchId,
                        b.ProdId,
                        EscapeCsv(b.BatchNo),
                        b.Expiry.ToString("yyyy-MM-dd"),
                        b.PriceRetailBatch?.ToString("0.##") ?? "",
                        b.UnitCostDefault?.ToString("0.####") ?? "",
                        b.EntryDate.ToString("yyyy-MM-dd"),
                        b.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                        b.UpdatedAt?.ToString("yyyy-MM-dd HH:mm") ?? "",
                        b.IsActive ? "1" : "0"
                    ));
                }

                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                var csvName = $"Batches_{DateTime.Now:yyyyMMdd_HHmm}.csv";
                return File(bytes, "text/csv", csvName);
            }
            else
            {
                // تصدير Excel باستخدام ClosedXML
                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("Batches");

                // عناوين الأعمدة
                ws.Cell(1, 1).Value = "كود التشغيلة";
                ws.Cell(1, 2).Value = "كود الصنف";
                ws.Cell(1, 3).Value = "اسم الصنف";
                ws.Cell(1, 4).Value = "رقم التشغيلة";
                ws.Cell(1, 5).Value = "تاريخ الصلاحية";
                ws.Cell(1, 6).Value = "سعر الجمهور للتشغيلة";
                ws.Cell(1, 7).Value = "التكلفة الافتراضية";
                ws.Cell(1, 8).Value = "تاريخ الإدخال";
                ws.Cell(1, 9).Value = "تاريخ الإنشاء";
                ws.Cell(1, 10).Value = "آخر تعديل";
                ws.Cell(1, 11).Value = "نشط؟";

                int row = 2;

                foreach (var b in data)
                {
                    ws.Cell(row, 1).Value = b.BatchId;
                    ws.Cell(row, 2).Value = b.ProdId;
                    ws.Cell(row, 3).Value = b.Product?.ProdName ?? "";
                    ws.Cell(row, 4).Value = b.BatchNo;
                    ws.Cell(row, 5).Value = b.Expiry;
                    ws.Cell(row, 6).Value = b.PriceRetailBatch;
                    ws.Cell(row, 7).Value = b.UnitCostDefault;
                    ws.Cell(row, 8).Value = b.EntryDate;
                    ws.Cell(row, 9).Value = b.CreatedAt;
                    ws.Cell(row, 10).Value = b.UpdatedAt;
                    ws.Cell(row, 11).Value = b.IsActive ? "نشط" : "موقوف";

                    row++;
                }

                ws.Columns().AdjustToContents();

                using var stream = new System.IO.MemoryStream();
                wb.SaveAs(stream);
                var fileName = $"Batches_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
                return File(stream.ToArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileName);
            }
        }

        // دالة صغيرة لهروب نص CSV
        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }
    }
}
