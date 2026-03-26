using System;                                     // متغيرات التاريخ DateTime
using System.Collections.Generic;                 // List, Dictionary
using System.Globalization;                       // CultureInfo للتصدير
using System.Linq;                                // أوامر LINQ
using System.Linq.Expressions;                   // Expression<Func<...>>
using System.Text;                                // StringBuilder لإنشاء CSV
using System.Threading.Tasks;                     // async / await
using ClosedXML.Excel;                            // مكتبة إنشاء ملف Excel فعلياً
using Microsoft.AspNetCore.Mvc;                   // Controller, IActionResult
using Microsoft.AspNetCore.Mvc.Rendering;         // SelectListItem لو احتجناه لاحقاً
using Microsoft.EntityFrameworkCore;              // AsNoTracking, ToListAsync
using ERP.Data;                                   // AppDbContext
using ERP.Filters;
using ERP.Infrastructure;                         // PagedResult + ApplySearchSort
using ERP.Models;                                 // PurchaseReturnLine
using ERP.Security;
using ERP.Services;                               // DocumentTotalsService (لإجماليات المستندات مستقبلاً)

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر سطور مرتجع الشراء:
    /// عرض / بحث / فرز / فلترة / حذف / حذف جماعي / حذف الكل / تصدير CSV و Excel.
    /// </summary>
    [RequirePermission("PurchaseReturnLines.Index")]
    public class PurchaseReturnLinesController : Controller
    {
        // كائن الاتصال بقاعدة البيانات
        private readonly AppDbContext _context;               // متغير: سياق قاعدة البيانات
        private readonly DocumentTotalsService _docTotals;    // متغير: خدمة تجميع إجماليات المستندات (استخدامها لاحقاً مع الهيدر)

        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        public PurchaseReturnLinesController(AppDbContext context,
                                             DocumentTotalsService docTotals)
        {
            _context = context;       // حفظ سياق الداتا بيز
            _docTotals = docTotals;   // حفظ السيرفيس (مستخدَم مستقبلاً لو أضفنا إجماليات على هيدر PurchaseReturn)
        }

        // ---------------------------------------------------------
        // دالة خاصة: تطبيق فلاتر الأعمدة (بنمط Excel)
        // ---------------------------------------------------------
        private static IQueryable<PurchaseReturnLine> ApplyColumnFilters(
            IQueryable<PurchaseReturnLine> query,
            string? filterCol_pretId,
            string? filterCol_lineNo,
            string? filterCol_prodId,
            string? filterCol_qty,
            string? filterCol_priceRetail,
            string? filterCol_unitCost,
            string? filterCol_discount,
            string? filterCol_prodName,
            string? filterCol_customerName,
            string? filterCol_batch,
            string? filterCol_expiry)
        {
            if (!string.IsNullOrWhiteSpace(filterCol_pretId))
            {
                var ids = filterCol_pretId.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) query = query.Where(l => ids.Contains(l.PRetId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_lineNo))
            {
                var ids = filterCol_lineNo.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) query = query.Where(l => ids.Contains(l.LineNo));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_prodId))
            {
                var ids = filterCol_prodId.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) query = query.Where(l => ids.Contains(l.ProdId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_qty))
            {
                var vals = filterCol_qty.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0) query = query.Where(l => vals.Contains(l.Qty));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_priceRetail))
            {
                var vals = filterCol_priceRetail.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0) query = query.Where(l => vals.Contains(l.PriceRetail));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_unitCost))
            {
                var vals = filterCol_unitCost.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0) query = query.Where(l => vals.Contains(l.UnitCost));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_discount))
            {
                var vals = filterCol_discount.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0) query = query.Where(l => vals.Contains(l.PurchaseDiscountPct));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_prodName))
            {
                var vals = filterCol_prodName.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) query = query.Where(l => l.Product != null && l.Product.ProdName != null && vals.Contains(l.Product.ProdName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_customerName))
            {
                var vals = filterCol_customerName.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) query = query.Where(l => l.PurchaseReturn != null && l.PurchaseReturn.Customer != null && l.PurchaseReturn.Customer.CustomerName != null && vals.Contains(l.PurchaseReturn.Customer.CustomerName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_batch))
            {
                var vals = filterCol_batch.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) query = query.Where(l => l.BatchNo != null && vals.Contains(l.BatchNo));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_expiry))
            {
                var parts = filterCol_expiry.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length >= 6).ToList();
                if (parts.Count > 0)
                {
                    var dates = new List<DateTime>();
                    foreach (var p in parts)
                        if (DateTime.TryParse(p, out var d)) dates.Add(d.Date);
                    if (dates.Count > 0) query = query.Where(l => l.Expiry.HasValue && dates.Contains(l.Expiry.Value.Date));
                }
            }
            return query;
        }

        /// <summary>API: جلب القيم المميزة لعمود (للفلترة بنمط Excel).</summary>
        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var columnLower = (column ?? "").Trim().ToLowerInvariant();

            if (columnLower == "pretid")
            {
                var ids = await _context.PurchaseReturnLines.AsNoTracking()
                    .Select(l => l.PRetId).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "lineno")
            {
                var ids = await _context.PurchaseReturnLines.AsNoTracking()
                    .Select(l => l.LineNo).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "prodid")
            {
                var ids = await _context.PurchaseReturnLines.AsNoTracking()
                    .Select(l => l.ProdId).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "qty")
            {
                var vals = await _context.PurchaseReturnLines.AsNoTracking()
                    .Select(l => l.Qty).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(vals.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "priceretail")
            {
                var vals = await _context.PurchaseReturnLines.AsNoTracking()
                    .Select(l => l.PriceRetail).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(vals.Select(v => new { value = v.ToString("0.00"), display = v.ToString("0.00") }));
            }
            if (columnLower == "unitcost")
            {
                var vals = await _context.PurchaseReturnLines.AsNoTracking()
                    .Select(l => l.UnitCost).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(vals.Select(v => new { value = v.ToString("0.####"), display = v.ToString("0.####") }));
            }
            if (columnLower == "discount")
            {
                var vals = await _context.PurchaseReturnLines.AsNoTracking()
                    .Select(l => l.PurchaseDiscountPct).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(vals.Select(v => new { value = v.ToString("0.##"), display = v.ToString("0.##") }));
            }
            if (columnLower == "prodname")
            {
                var q = _context.Products.AsNoTracking()
                    .Where(p => _context.PurchaseReturnLines.Any(l => l.ProdId == p.ProdId))
                    .Select(p => p.ProdName ?? "");
                if (!string.IsNullOrEmpty(searchTerm)) q = q.Where(s => s.ToLower().Contains(searchTerm));
                var list = await q.Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "customername")
            {
                var q = _context.PurchaseReturnLines.AsNoTracking()
                    .Where(l => l.PurchaseReturn != null && l.PurchaseReturn.Customer != null)
                    .Select(l => l.PurchaseReturn!.Customer!.CustomerName ?? "")
                    .Where(s => s != "");
                if (!string.IsNullOrEmpty(searchTerm)) q = q.Where(s => s.ToLower().Contains(searchTerm));
                var list = await q.Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "batch")
            {
                var q = _context.PurchaseReturnLines.AsNoTracking().Where(l => l.BatchNo != null).Select(l => l.BatchNo!);
                if (!string.IsNullOrEmpty(searchTerm)) q = q.Where(s => s.ToLower().Contains(searchTerm));
                var list = await q.Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "expiry")
            {
                var dates = await _context.PurchaseReturnLines.AsNoTracking()
                    .Where(l => l.Expiry.HasValue).Select(l => l.Expiry!.Value.Date).Distinct().OrderByDescending(x => x).Take(500).ToListAsync();
                return Json(dates.Select(d => new { value = d.ToString("yyyy-MM-dd"), display = d.ToString("yyyy-MM-dd") }));
            }
            return Json(Array.Empty<object>());
        }

        // ---------------------------------------------------------
        // دالة خاصة: تجهيز الاستعلام مع كل الفلاتر + البحث + الترتيب
        // نستخدمها في Index و Export حتى لا نكرر الكود.
        // ---------------------------------------------------------
        private IQueryable<PurchaseReturnLine> BuildQuery(
            int? pretId,                     // فلتر اختياري برقم المرتجع
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            int? fromCode,                   // فلتر رقم السطر من/إلى
            int? toCode)
        {
            // (1) الاستعلام الأساسي من جدول سطور المرتجع + الصنف + المورد (العميل)
            IQueryable<PurchaseReturnLine> q =
                _context.PurchaseReturnLines
                        .Include(l => l.Product)
                        .Include(l => l.PurchaseReturn)
                            .ThenInclude(pr => pr!.Customer)
                        .AsNoTracking();     // تحسين الأداء (قراءة فقط)

            // (2) فلتر اختياري برقم مرتجع محدد
            if (pretId.HasValue)
                q = q.Where(l => l.PRetId == pretId.Value);

            // (3) فلتر رقم السطر من/إلى
            if (fromCode.HasValue)
                q = q.Where(l => l.LineNo >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(l => l.LineNo <= toCode.Value);

            // (4) فلتر التاريخ — هنا نستخدم تاريخ الصلاحية Expiry
            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                DateTime from = fromDate.Value.Date;
                DateTime to = toDate.Value.Date;

                q = q.Where(l =>
                    l.Expiry.HasValue &&
                    l.Expiry.Value.Date >= from &&
                    l.Expiry.Value.Date <= to);
            }

            // (5) خرائط الحقول للبحث (نفس نظام القوائم الموحد)

            // الحقول النصية (string) — مثلاً رقم التشغيلة
            var stringFields =
                new Dictionary<string, Expression<Func<PurchaseReturnLine, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["batch"] = l => l.BatchNo ?? ""
                };

            // الحقول الرقمية (int) — رقم المرتجع / رقم السطر / كود الصنف / الكمية
            var intFields =
                new Dictionary<string, Expression<Func<PurchaseReturnLine, int>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = l => l.PRetId,   // رقم المرتجع
                    ["line"] = l => l.LineNo, // رقم السطر
                    ["prod"] = l => l.ProdId, // كود الصنف
                    ["qty"] = l => l.Qty      // الكمية المرتجعة
                };

            // مفاتيح الترتيب المسموحة
            var orderFields =
                new Dictionary<string, Expression<Func<PurchaseReturnLine, object>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PRetId"] = l => l.PRetId,
                    ["LineNo"] = l => l.LineNo,
                    ["ProdId"] = l => l.ProdId,
                    ["Qty"] = l => l.Qty,
                    ["PriceRetail"] = l => l.PriceRetail,
                    ["PurchaseDiscountPct"] = l => l.PurchaseDiscountPct,
                    ["UnitCost"] = l => l.UnitCost,
                    ["BatchNo"] = l => l.BatchNo ?? "",
                    ["Expiry"] = l => l.Expiry ?? DateTime.MaxValue,
                    ["ProdName"] = l => l.Product != null ? (l.Product.ProdName ?? "") : "",
                    ["CustomerName"] = l => l.PurchaseReturn != null && l.PurchaseReturn.Customer != null ? (l.PurchaseReturn.Customer.CustomerName ?? "") : ""
                };

            // (6) تطبيق منظومة البحث/الترتيب الموحدة
            q = q.ApplySearchSort(
                search: search,
                searchBy: searchBy,
                sort: sort,
                dir: dir,
                stringFields: stringFields,
                intFields: intFields,
                orderFields: orderFields,
                defaultSearchBy: "all",
                defaultSortBy: "LineNo"
            );

            return q;
        }

        // =========================================================
        // Index — قائمة سطور مرتجع الشراء
        // =========================================================
        public async Task<IActionResult> Index(
            int? pretId,
            string? search,
            string? searchBy = "all",
            string? sort = "LineNo",
            string? dir = "asc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_pretId = null,
            string? filterCol_lineNo = null,
            string? filterCol_prodId = null,
            string? filterCol_qty = null,
            string? filterCol_priceRetail = null,
            string? filterCol_unitCost = null,
            string? filterCol_discount = null,
            string? filterCol_prodName = null,
            string? filterCol_customerName = null,
            string? filterCol_batch = null,
            string? filterCol_expiry = null,
            int page = 1,
            int pageSize = 50)
        {
            var q = BuildQuery(
                pretId,
                search, searchBy,
                sort, dir,
                useDateRange, fromDate, toDate,
                fromCode, toCode);

            q = ApplyColumnFilters(q, filterCol_pretId, filterCol_lineNo, filterCol_prodId, filterCol_qty, filterCol_priceRetail, filterCol_unitCost, filterCol_discount, filterCol_prodName, filterCol_customerName, filterCol_batch, filterCol_expiry);

            // اتجاه الترتيب (لازم نطلعه علشان PagedResult)
            var dirNorm = (dir?.ToLower() == "asc") ? "asc" : "desc";
            bool descending = dirNorm == "desc";

            // إنشاء نتيجة مقسّمة صفحات بالطريقة القياسية للنظام الموحد
            var model = await PagedResult<PurchaseReturnLine>.CreateAsync(
                q,
                page,
                pageSize,
                search,
                descending,
                sort,
                searchBy
            );

            // حفظ حالة فلتر التاريخ داخل الموديل (نفس النظام الثابت)
            model.UseDateRange = useDateRange;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            // تمرير قيم الفلاتر للواجهة
            ViewBag.Search = search ?? "";
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.Sort = sort ?? "LineNo";
            ViewBag.Dir = dirNorm;

            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.DateField = "Expiry";

            ViewBag.FilterCol_PretId = filterCol_pretId;
            ViewBag.FilterCol_LineNo = filterCol_lineNo;
            ViewBag.FilterCol_ProdId = filterCol_prodId;
            ViewBag.FilterCol_Qty = filterCol_qty;
            ViewBag.FilterCol_PriceRetail = filterCol_priceRetail;
            ViewBag.FilterCol_UnitCost = filterCol_unitCost;
            ViewBag.FilterCol_Discount = filterCol_discount;
            ViewBag.FilterCol_ProdName = filterCol_prodName;
            ViewBag.FilterCol_CustomerName = filterCol_customerName;
            ViewBag.FilterCol_Batch = filterCol_batch;
            ViewBag.FilterCol_Expiry = filterCol_expiry;

            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalCount = model.TotalCount;
            ViewBag.PRetId = pretId;

            return View(model);   // Views/PurchaseReturnLines/Index.cshtml
        }

        // =========================================================
        // Show — عرض سطر واحد (مفتاح مركب: PRetId + LineNo)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Show(int pretId, int lineNo)
        {
            if (pretId <= 0)
                return BadRequest();   // تأمين: رقم مرتجع غير صحيح

            var line = await _context.PurchaseReturnLines
                                     .AsNoTracking()
                                     .FirstOrDefaultAsync(l =>
                                         l.PRetId == pretId &&
                                         l.LineNo == lineNo);

            if (line == null)
                return NotFound();     // السطر غير موجود

            return View(line);    // Views/PurchaseReturnLines/Show.cshtml
        }

        // =========================================================
        // Delete — حذف سطر واحد بالمفتاح المركب (PRetId + LineNo)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int pretId, int lineNo)
        {
            // جلب السطر من قاعدة البيانات
            var line = await _context.PurchaseReturnLines
                .FirstOrDefaultAsync(l => l.PRetId == pretId && l.LineNo == lineNo);

            if (line == null)
            {
                TempData["Error"] = "السطر المطلوب غير موجود.";
                return RedirectToAction(nameof(Index), new { pretId });
            }

            _context.PurchaseReturnLines.Remove(line);
            await _context.SaveChangesAsync();

            // ملاحظة: لو أضفنا فيما بعد إجماليات على هيدر PurchaseReturn
            // هنا هنستدعي دالة إعادة التجميع من DocumentTotalsService
            // مثال مستقبلي:
            // await _docTotals.RecalcPurchaseReturnTotalsAsync(pretId);

            TempData["Success"] = "تم حذف سطر مرتجع الشراء بنجاح.";
            return RedirectToAction(nameof(Index), new { pretId });
        }

        // =========================================================
        // Export — تصدير سطور مرتجع الشراء
        //  - لو format = "csv" → ملف CSV
        //  - غير ذلك (excel أو null) → ملف Excel فعلي .xlsx
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Export(
            int? pretId,
            string? search,
            string? searchBy = "all",
            string? sort = "LineNo",
            string? dir = "asc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_pretId = null,
            string? filterCol_lineNo = null,
            string? filterCol_prodId = null,
            string? filterCol_qty = null,
            string? filterCol_priceRetail = null,
            string? filterCol_unitCost = null,
            string? filterCol_discount = null,
            string? filterCol_prodName = null,
            string? filterCol_customerName = null,
            string? filterCol_batch = null,
            string? filterCol_expiry = null,
            string format = "excel")
        {
            var q = BuildQuery(
                pretId,
                search, searchBy,
                sort, dir,
                useDateRange, fromDate, toDate,
                fromCode, toCode);

            q = ApplyColumnFilters(q, filterCol_pretId, filterCol_lineNo, filterCol_prodId, filterCol_qty, filterCol_priceRetail, filterCol_unitCost, filterCol_discount, filterCol_prodName, filterCol_customerName, filterCol_batch, filterCol_expiry);

            var list = await q
                .OrderBy(l => l.PRetId)
                .ThenBy(l => l.LineNo)
                .ToListAsync();

            format = (format ?? "excel").ToLowerInvariant();

            if (format == "csv")
            {
                // ===== تصدير CSV يفتح فى Excel =====
                var sb = new StringBuilder();

                // عناوين الأعمدة
                sb.AppendLine("رقم المرتجع,رقم السطر,كود الصنف,الكمية,تكلفة الوحدة,خصم الشراء %,سعر الجمهور,التشغيلة,الصلاحية");

                // كل سطر مرتجع في CSV
                foreach (var l in list)
                {
                    string line = string.Join(",",
                        l.PRetId,
                        l.LineNo,
                        l.ProdId,
                        l.Qty,
                        l.UnitCost.ToString("0.####", CultureInfo.InvariantCulture),
                        l.PurchaseDiscountPct.ToString("0.##", CultureInfo.InvariantCulture),
                        l.PriceRetail.ToString("0.00", CultureInfo.InvariantCulture),
                        (l.BatchNo ?? "").Replace(",", " "),
                        l.Expiry.HasValue ? l.Expiry.Value.ToString("yyyy-MM-dd") : ""
                    );

                    sb.AppendLine(line);
                }

                var bytesCsv = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString());
                var fileNameCsv = ExcelExportNaming.ArabicTimestampedFileName("أصناف مرتجع المشتريات", ".csv");
                const string contentTypeCsv = "text/csv";

                return File(bytesCsv, contentTypeCsv, fileNameCsv);
            }
            else
            {
                // ===== تصدير Excel فعلي (.xlsx) باستخدام ClosedXML =====
                using var workbook = new XLWorkbook();                 // متغير: مصنف Excel
                var worksheet = workbook.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("أصناف مرتجع المشتريات"));

                int row = 1;   // متغير: رقم الصف الحالي في الشيت

                // عناوين الأعمدة (عربي)
                worksheet.Cell(row, 1).Value = "رقم المرتجع";
                worksheet.Cell(row, 2).Value = "رقم السطر";
                worksheet.Cell(row, 3).Value = "كود الصنف";
                worksheet.Cell(row, 4).Value = "الكمية";
                worksheet.Cell(row, 5).Value = "تكلفة الوحدة";
                worksheet.Cell(row, 6).Value = "خصم الشراء %";
                worksheet.Cell(row, 7).Value = "سعر الجمهور";
                worksheet.Cell(row, 8).Value = "التشغيلة";
                worksheet.Cell(row, 9).Value = "الصلاحية";

                // تنسيق بسيط للهيدر (غامق + AutoFit بعدين)
                var headerRange = worksheet.Range(row, 1, row, 9);
                headerRange.Style.Font.Bold = true;

                // إضافة الصفوف من البيانات
                foreach (var l in list)
                {
                    row++;

                    worksheet.Cell(row, 1).Value = l.PRetId;
                    worksheet.Cell(row, 2).Value = l.LineNo;
                    worksheet.Cell(row, 3).Value = l.ProdId;
                    worksheet.Cell(row, 4).Value = l.Qty;
                    worksheet.Cell(row, 5).Value = l.UnitCost;
                    worksheet.Cell(row, 6).Value = l.PurchaseDiscountPct;
                    worksheet.Cell(row, 7).Value = l.PriceRetail;
                    worksheet.Cell(row, 8).Value = l.BatchNo ?? "";
                    worksheet.Cell(row, 9).Value = l.Expiry?.ToString("yyyy-MM-dd") ?? "";
                }

                // ضبط عرض الأعمدة تلقائياً
                worksheet.Columns().AdjustToContents();

                using var stream = new System.IO.MemoryStream();  // متغير: ستريم في الذاكرة لحفظ الملف
                workbook.SaveAs(stream);
                var bytesXlsx = stream.ToArray();

                var fileNameXlsx = ExcelExportNaming.ArabicTimestampedFileName("أصناف مرتجع المشتريات", ".xlsx");
                const string contentTypeXlsx =
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

                return File(bytesXlsx, contentTypeXlsx, fileNameXlsx);
            }
        }

        // =========================================================
        // BulkDelete — حذف السطور المحددة
        // نستقبل مفتاح مركب على شكل نص "PRetId:LineNo"
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string[] ids)
        {
            // لو مفيش أى اختيار
            if (ids == null || ids.Length == 0)
            {
                TempData["Error"] = "لم يتم اختيار أى سطر للحذف.";
                return RedirectToAction(nameof(Index));
            }

            // نحول الـ ids إلى List علشان نستخدم Contains بسهولة
            var idList = ids.ToList();   // متغير: قائمة المفاتيح النصية المختارة

            // نجيب السطور المطابقة لنفس صيغة المفتاح "PRetId:LineNo"
            var lines = await _context.PurchaseReturnLines
                .Where(l => idList.Contains(
                    l.PRetId.ToString() + ":" + l.LineNo.ToString()))
                .ToListAsync();

            if (lines.Count == 0)
            {
                TempData["Error"] = "لم يتم العثور على السطور المحددة.";
                return RedirectToAction(nameof(Index));
            }

            _context.PurchaseReturnLines.RemoveRange(lines);
            await _context.SaveChangesAsync();

            // ملاحظة: زى Delete، لو أضفنا إجماليات على الهيدر مستقبلاً
            // هنا ممكن نمرّ على الـ PRetId المميزة ونعيد تجميع كل هيدر.

            TempData["Success"] = $"تم حذف {lines.Count} من سطور مرتجع الشراء المحددة.";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // DeleteAll — حذف جميع سطور مرتجع الشراء
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _context.PurchaseReturnLines.ToListAsync();

            if (all.Count == 0)
            {
                TempData["Error"] = "لا توجد سطور مرتجع لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            _context.PurchaseReturnLines.RemoveRange(all);
            await _context.SaveChangesAsync();

            // نفس الملاحظة: لو أضفنا إجماليات للهيدر، هنحتاج هنا نعيد تجميع كل الهيدر المرتبطة.

            TempData["Success"] = "تم حذف جميع سطور مرتجعات الشراء.";
            return RedirectToAction(nameof(Index));
        }
    }
}
