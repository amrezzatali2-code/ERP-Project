using System;                                     // لاستخدام DateTime
using System.Collections.Generic;                 // List<T>
using System.IO;                                  // MemoryStream لتجهيز ملف الإكسل
using System.Linq;                                // أوامر LINQ
using System.Threading.Tasks;                     // async / await
using ClosedXML.Excel;                            // مكتبة ClosedXML لتصدير Excel
using ERP.Data;                                   // AppDbContext
using ERP.Infrastructure;                         // PagedResult
using ERP.Models;                                 // الموديلات (PRLine, PurchaseRequest)
using ERP.Services;                               // DocumentTotalsService (سيرفيس إجماليات)
using Microsoft.AspNetCore.Mvc;                   // أساس الكنترولر MVC
using Microsoft.EntityFrameworkCore;              // AsNoTracking, ToListAsync

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إدارة جدول سطور طلبات الشراء (PRLines)
    /// - عرض بالقوائم الموحّدة (بحث + ترتيب + من/إلى كود + تقسيم صفحات)
    /// - إضافة / تعديل / حذف سطر
    /// - حذف مجموعة سطور
    /// - تصدير CSV أو Excel
    /// </summary>
    public class PRLinesController : Controller
    {
        // كائن الاتصال بقاعدة البيانات
        private readonly AppDbContext _context;

        // متغير: خدمة حساب إجماليات مستندات المشتريات (طلب الشراء / فاتورة الشراء)
        private readonly DocumentTotalsService _docTotals;

        /// <summary>
        /// constructor لاستقبال DbContext + DocumentTotalsService من الـ DI
        /// </summary>
        public PRLinesController(AppDbContext context, DocumentTotalsService docTotals)
        {
            _context = context;
            _docTotals = docTotals;   // تخزين سيرفيس الإجماليات لإعادة حساب إجماليات الطلب بعد تعديل السطور
        }

        // ============================================================
        // INDEX: قائمة سطور طلبات الشراء بنظام القوائم الموحّد
        // ============================================================
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            int? page,
            int? pageSize,
            int? fromCode,
            int? toCode)
        {
            // ١) تجهيز القيم الافتراضية
            search = (search ?? string.Empty).Trim();              // نص البحث
            searchBy = string.IsNullOrWhiteSpace(searchBy) ? "all" : searchBy;

            sort ??= "PRId";                                       // عمود الترتيب الافتراضي
            dir = string.IsNullOrWhiteSpace(dir) ? "asc" : dir.ToLower();
            bool sortDesc = dir == "desc";                         // true لو ترتيب تنازلي

            int pageNumber = page.GetValueOrDefault(1);            // رقم الصفحة
            if (pageNumber < 1) pageNumber = 1;

            int ps = pageSize.GetValueOrDefault(50);               // حجم الصفحة
            if (ps <= 0) ps = 50;

            // ٢) الكويري الأساسي من جدول PRLines
            IQueryable<PRLine> query = _context.PRLines
                .AsNoTracking()
                .AsQueryable();

            // ٣) تطبيق البحث
            if (!string.IsNullOrWhiteSpace(search) &&
                int.TryParse(search, out int n))
            {
                switch (searchBy)
                {
                    case "pr":   // البحث برقم طلب الشراء
                        query = query.Where(l => l.PRId == n);
                        break;

                    case "line": // البحث برقم السطر
                        query = query.Where(l => l.LineNo == n);
                        break;

                    case "prod": // البحث بكود الصنف
                        query = query.Where(l => l.ProdId == n);
                        break;

                    case "qty":  // البحث بالكمية المطلوبة
                        query = query.Where(l => l.QtyRequested == n);
                        break;

                    default:     // البحث في أكثر من عمود
                        query = query.Where(l =>
                            l.PRId == n ||
                            l.LineNo == n ||
                            l.ProdId == n ||
                            l.QtyRequested == n
                        );
                        break;
                }
            }

            // ٤) فلتر من/إلى كود على ProdId
            if (fromCode.HasValue)
            {
                query = query.Where(l => l.ProdId >= fromCode.Value);
            }

            if (toCode.HasValue)
            {
                query = query.Where(l => l.ProdId <= toCode.Value);
            }

            // ٥) تطبيق الترتيب
            query = (sort, sortDesc) switch
            {
                ("PRId", false) => query.OrderBy(l => l.PRId).ThenBy(l => l.LineNo),
                ("PRId", true) => query.OrderByDescending(l => l.PRId).ThenByDescending(l => l.LineNo),

                ("LineNo", false) => query.OrderBy(l => l.LineNo).ThenBy(l => l.PRId),
                ("LineNo", true) => query.OrderByDescending(l => l.LineNo).ThenByDescending(l => l.PRId),

                ("ProdId", false) => query.OrderBy(l => l.ProdId).ThenBy(l => l.PRId).ThenBy(l => l.LineNo),
                ("ProdId", true) => query.OrderByDescending(l => l.ProdId).ThenByDescending(l => l.PRId).ThenByDescending(l => l.LineNo),

                ("QtyRequested", false) => query.OrderBy(l => l.QtyRequested).ThenBy(l => l.PRId),
                ("QtyRequested", true) => query.OrderByDescending(l => l.QtyRequested).ThenByDescending(l => l.PRId),

                _ when !sortDesc => query.OrderBy(l => l.PRId).ThenBy(l => l.LineNo),
                _ => query.OrderByDescending(l => l.PRId).ThenByDescending(l => l.LineNo),
            };

            // ٦) إنشاء PagedResult (نفس نظام القوائم الموحّد)
            var model = await PagedResult<PRLine>.CreateAsync(
                query,
                pageNumber,
                ps,
                search,
                sortDesc,
                sort,
                searchBy);

            // ٧) تمرير قيم الفلاتر للـ View
            ViewBag.Search = search;
            ViewBag.SearchBy = searchBy;
            ViewBag.Sort = sort;
            ViewBag.Dir = sortDesc ? "desc" : "asc";

            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;

            return View(model);
        }

        // ============================================================
        // SHOW: عرض سطر واحد بالمفتاح المركب (PRId + LineNo)
        // ============================================================
        public async Task<IActionResult> Show(int prId, int lineNo)
        {
            var line = await _context.PRLines
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.PRId == prId && l.LineNo == lineNo);

            if (line == null)
                return NotFound();

            return View(line);
        }

        // ============================================================
        // CREATE (GET): فتح شاشة إضافة سطر جديد لطلب شراء معيّن
        // ============================================================
        [HttpGet]
        public async Task<IActionResult> Create(int prId)
        {
            // نتحقق أن طلب الشراء موجود فعلاً
            var header = await _context.PurchaseRequests
                .AsNoTracking()
                .FirstOrDefaultAsync(h => h.PRId == prId);

            if (header == null)
                return NotFound(); // لو الطلب مش موجود

            // نحسب رقم السطر التالي داخل نفس الطلب
            int nextLineNo = await _context.PRLines
                .Where(l => l.PRId == prId)
                .Select(l => (int?)l.LineNo)
                .MaxAsync() ?? 0;

            nextLineNo++;

            // تجهيز موديل السطر الجديد بقيم افتراضية
            var model = new PRLine
            {
                PRId = prId,                  // رقم طلب الشراء
                LineNo = nextLineNo,         // رقم السطر الجديد
                QtyRequested = 0,            // الكمية المطلوبة مبدئياً صفر
                PurchaseDiscountPct = 0,     // خصم الشراء %
                PriceRetail = 0,             // سعر الجمهور المرجعي
                ExpectedCost = 0,            // التكلفة المتوقعة
                QtyConverted = 0             // الكمية المحوّلة (لسه صفر)
            };

            return View(model); // View: Create.cshtml
        }

        // ============================================================
        // CREATE (POST): حفظ سطر جديد في جدول PRLines
        // ============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PRLine model)
        {
            // التحقق من صحة البيانات
            if (!ModelState.IsValid)
            {
                // لو فيه أخطاء نرجع نفس الفيو بالبيانات والأخطاء
                return View(model);
            }

            // إضافة السطر لجدول PRLines
            _context.PRLines.Add(model);
            await _context.SaveChangesAsync();

            // 🔹 بعد الحفظ نعيد حساب إجماليات طلب الشراء من السطور
            await _docTotals.RecalcPurchaseRequestTotalsAsync(model.PRId);

            TempData["Success"] = "تم إضافة سطر طلب الشراء بنجاح.";

            // ممكن نرجع لقائمة السطور مفلترة على نفس الطلب
            return RedirectToAction(nameof(Index), new { search = model.PRId.ToString(), searchBy = "pr" });
        }

        // ============================================================
        // EDIT (GET): فتح شاشة تعديل سطر طلب الشراء
        // ============================================================
        [HttpGet]
        public async Task<IActionResult> Edit(int prId, int lineNo)
        {
            var line = await _context.PRLines
                .FirstOrDefaultAsync(l => l.PRId == prId && l.LineNo == lineNo);

            if (line == null)
                return NotFound();

            return View(line); // View: Edit.cshtml
        }

        // ============================================================
        // EDIT (POST): حفظ تعديل سطر طلب الشراء
        // ============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int prId, int lineNo, PRLine model)
        {
            // تأمين: نتأكد أن المفاتيح في الرابط = المفاتيح في الموديل
            if (prId != model.PRId || lineNo != model.LineNo)
                return BadRequest();

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var existing = await _context.PRLines
                .FirstOrDefaultAsync(l => l.PRId == prId && l.LineNo == lineNo);

            if (existing == null)
                return NotFound();

            // نسخ الحقول المسموح بتعديلها
            existing.ProdId = model.ProdId;                       // كود الصنف
            existing.QtyRequested = model.QtyRequested;           // الكمية المطلوبة
            existing.PriceBasis = model.PriceBasis;               // مرجع السعر
            existing.PriceRetail = model.PriceRetail;             // سعر الجمهور
            existing.PurchaseDiscountPct = model.PurchaseDiscountPct; // خصم الشراء %
            existing.ExpectedCost = model.ExpectedCost;           // التكلفة المتوقعة
            existing.PreferredBatchNo = model.PreferredBatchNo;   // التشغيلة المفضلة
            existing.PreferredExpiry = model.PreferredExpiry;     // الصلاحية المفضلة
            existing.QtyConverted = model.QtyConverted;           // الكمية المحوّلة (لو محتاجها)

            await _context.SaveChangesAsync();

            // 🔹 بعد التعديل نعيد حساب إجماليات طلب الشراء
            await _docTotals.RecalcPurchaseRequestTotalsAsync(existing.PRId);

            TempData["Success"] = "تم تعديل سطر طلب الشراء بنجاح.";

            return RedirectToAction(nameof(Index), new { search = existing.PRId.ToString(), searchBy = "pr" });
        }

        // ============================================================
        // DELETE: حذف سطر واحد (PRId + LineNo)
        // ============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int prId, int lineNo)
        {
            var line = await _context.PRLines
                .FirstOrDefaultAsync(l => l.PRId == prId && l.LineNo == lineNo);

            if (line == null)
            {
                TempData["Error"] = "السطر المطلوب غير موجود.";
                return RedirectToAction(nameof(Index));
            }

            int headerId = line.PRId;   // نحفظ رقم الطلب علشان بعد الحذف نعيد حساب الإجماليات

            _context.PRLines.Remove(line);
            await _context.SaveChangesAsync();

            // 🔹 إعادة حساب إجماليات طلب الشراء بعد حذف السطر
            await _docTotals.RecalcPurchaseRequestTotalsAsync(headerId);

            TempData["Success"] = "تم حذف سطر طلب الشراء بنجاح.";
            return RedirectToAction(nameof(Index), new { search = headerId.ToString(), searchBy = "pr" });
        }

        // ============================================================
        // BULK DELETE: حذف مجموعة من السطور دفعة واحدة
        // المفاتيح المرسَلة تكون بصيغة: "PRId_LineNo"
        // ============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string[] keys)
        {
            if (keys == null || keys.Length == 0)
            {
                TempData["Error"] = "لم يتم اختيار أي صفوف للحذف.";
                return RedirectToAction(nameof(Index));
            }

            // تحويل النصوص إلى أزواج (PRId, LineNo)
            var ids = new List<(int prId, int lineNo)>();

            foreach (var key in keys)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;

                var parts = key.Split('_', '-');
                if (parts.Length < 2) continue;

                if (int.TryParse(parts[0], out int prId) &&
                    int.TryParse(parts[1], out int lineNo))
                {
                    ids.Add((prId, lineNo));
                }
            }

            if (!ids.Any())
            {
                TempData["Error"] = "لم يتم التعرف على المفاتيح المحددة.";
                return RedirectToAction(nameof(Index));
            }

            // جلب السطور المراد حذفها
            var toDelete = new List<PRLine>();

            foreach (var (prId, lineNo) in ids)
            {
                var line = await _context.PRLines
                    .FirstOrDefaultAsync(l => l.PRId == prId && l.LineNo == lineNo);

                if (line != null)
                    toDelete.Add(line);
            }

            if (!toDelete.Any())
            {
                TempData["Error"] = "لم يتم العثور على الصفوف المحددة.";
                return RedirectToAction(nameof(Index));
            }

            // نحتفظ بقائمة أرقام الطلبات المتأثرة علشان نعيد حساب إجمالياتها بعد الحذف
            var affectedHeaders = toDelete
                .Select(l => l.PRId)
                .Distinct()
                .ToList();

            _context.PRLines.RemoveRange(toDelete);
            await _context.SaveChangesAsync();

            // 🔹 إعادة حساب إجماليات كل طلب متأثر بالحذف
            foreach (var headerId in affectedHeaders)
            {
                await _docTotals.RecalcPurchaseRequestTotalsAsync(headerId);
            }

            TempData["Success"] = $"تم حذف {toDelete.Count} سطر / أسطر.";
            return RedirectToAction(nameof(Index));
        }

        // ============================================================
        // EXPORT: تصدير السطور الحالية إلى CSV أو Excel
        // format = "csv" (افتراضي) أو "excel" / "xlsx"
        // ============================================================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy,
            int? fromCode,
            int? toCode,
            string format = "csv")
        {
            search = (search ?? string.Empty).Trim();
            searchBy = string.IsNullOrWhiteSpace(searchBy) ? "all" : searchBy;

            IQueryable<PRLine> query = _context.PRLines
                .AsNoTracking()
                .AsQueryable();

            // نفس منطق البحث بتاع Index
            if (!string.IsNullOrWhiteSpace(search) &&
                int.TryParse(search, out int n))
            {
                switch (searchBy)
                {
                    case "pr":
                        query = query.Where(l => l.PRId == n);
                        break;
                    case "line":
                        query = query.Where(l => l.LineNo == n);
                        break;
                    case "prod":
                        query = query.Where(l => l.ProdId == n);
                        break;
                    case "qty":
                        query = query.Where(l => l.QtyRequested == n);
                        break;
                    default:
                        query = query.Where(l =>
                            l.PRId == n ||
                            l.LineNo == n ||
                            l.ProdId == n ||
                            l.QtyRequested == n
                        );
                        break;
                }
            }

            if (fromCode.HasValue)
                query = query.Where(l => l.ProdId >= fromCode.Value);

            if (toCode.HasValue)
                query = query.Where(l => l.ProdId <= toCode.Value);

            var data = await query
                .OrderBy(l => l.PRId)
                .ThenBy(l => l.LineNo)
                .ToListAsync();

            // -----------------------------------
            // لو المطلوب Excel (XLSX)
            // -----------------------------------
            var fmt = (format ?? "csv").ToLower();

            if (fmt == "excel" || fmt == "xlsx")
            {
                // نستخدم ClosedXML لإنشاء ملف إكسل في الذاكرة
                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("PRLines");

                // عناوين الأعمدة
                ws.Cell(1, 1).Value = "PRId";
                ws.Cell(1, 2).Value = "LineNo";
                ws.Cell(1, 3).Value = "ProdId";
                ws.Cell(1, 4).Value = "QtyRequested";
                ws.Cell(1, 5).Value = "PriceBasis";
                ws.Cell(1, 6).Value = "PriceRetail";
                ws.Cell(1, 7).Value = "PurchaseDiscountPct";
                ws.Cell(1, 8).Value = "ExpectedCost";
                ws.Cell(1, 9).Value = "PreferredBatchNo";
                ws.Cell(1, 10).Value = "PreferredExpiry";
                ws.Cell(1, 11).Value = "QtyConverted";

                int row = 2;

                foreach (var l in data)
                {
                    ws.Cell(row, 1).Value = l.PRId;
                    ws.Cell(row, 2).Value = l.LineNo;
                    ws.Cell(row, 3).Value = l.ProdId;
                    ws.Cell(row, 4).Value = l.QtyRequested;
                    ws.Cell(row, 5).Value = l.PriceBasis ?? "";
                    ws.Cell(row, 6).Value = l.PriceRetail;
                    ws.Cell(row, 7).Value = l.PurchaseDiscountPct;
                    ws.Cell(row, 8).Value = l.ExpectedCost;
                    ws.Cell(row, 9).Value = l.PreferredBatchNo ?? "";
                    ws.Cell(row, 10).Value = l.PreferredExpiry?.ToString("yyyy-MM-dd") ?? "";
                    ws.Cell(row, 11).Value = l.QtyConverted;
                    row++;
                }

                ws.Columns().AdjustToContents(); // ضبط عرض الأعمدة

                using var stream = new MemoryStream();
                wb.SaveAs(stream);
                var content = stream.ToArray();

                var excelName = $"PRLines_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                const string excelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

                return File(content, excelContentType, excelName);
            }

            // -----------------------------------
            // الوضع الافتراضي: CSV
            // -----------------------------------
            var lines = new List<string>
            {
                "PRId,LineNo,ProdId,QtyRequested,PriceBasis,PriceRetail,PurchaseDiscountPct,ExpectedCost,PreferredBatchNo,PreferredExpiry,QtyConverted"
            };

            foreach (var l in data)
            {
                var preferredExpiry = l.PreferredExpiry?.ToString("yyyy-MM-dd") ?? "";

                string line = string.Join(",", new[]
                {
                    l.PRId.ToString(),
                    l.LineNo.ToString(),
                    l.ProdId.ToString(),
                    l.QtyRequested.ToString(),
                    EscapeCsv(l.PriceBasis),
                    l.PriceRetail.ToString("0.00"),
                    l.PurchaseDiscountPct.ToString("0.00"),
                    l.ExpectedCost.ToString("0.0000"),
                    EscapeCsv(l.PreferredBatchNo),
                    preferredExpiry,
                    l.QtyConverted.ToString()
                });

                lines.Add(line);
            }

            var csv = string.Join(Environment.NewLine, lines);
            var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
            var fileName = $"PRLines_{DateTime.Now:yyyyMMdd_HHmmss}.csv";

            return File(bytes, "text/csv", fileName);
        }

        /// <summary>
        /// دالة لمراعاة وجود فواصل أو علامات تنصيص داخل النص عند التصدير لـ CSV
        /// </summary>
        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            if (value.Contains(',') || value.Contains('"'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";

            return value;
        }
    }
}
