using System;
using System.Collections.Generic;                 // Dictionary, List
using System.Globalization;                       // CultureInfo للتصدير
using System.IO;                                  // MemoryStream
using System.Linq;
using System.Linq.Expressions;                    // Expression<Func<...>>
using System.Text;                                // StringBuilder للـ CSV
using System.Threading.Tasks;

using ClosedXML.Excel;                            // مكتبة Excel

using ERP.Data;                                   // AppDbContext
using ERP.Filters;
using ERP.Infrastructure;                         // PagedResult + ApplySearchSort
using ERP.Models;                                 // SOLine
using ERP.Security;
using ERP.Services;                               // DocumentTotalsService

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر عرض سطور أوامر البيع (SOLines)
    /// - دعم فلترة برقم أمر البيع SOId
    /// - دعم البحث والترتيب بنظام القوائم الموحد
    /// - دعم فلترة بنطاق رقم السطر (fromCode/toCode)
    /// - دعم فلترة بالتاريخ/الوقت من تاريخ أمر البيع SODate
    /// - دعم حذف سطر/عدة أسطر/كل الأسطر مع إعادة تجميع أمر البيع
    /// - دعم تصدير البيانات إلى Excel أو CSV
    /// </summary>
    [RequirePermission(PermissionCodes.SalesLines.OrderLines_View)]
    public class SOLinesController : Controller
    {
        // كائن الاتصال بقاعدة البيانات
        private readonly AppDbContext _context;

        // سيرفيس إعادة حساب إجماليات أوامر البيع
        private readonly DocumentTotalsService _docTotals;

        public SOLinesController(AppDbContext context, DocumentTotalsService docTotals)
        {
            _context = context;
            _docTotals = docTotals;
        }

        // =========================================================
        // دالة خاصة: بناء الاستعلام مع كل الفلاتر + البحث + الترتيب
        // نستخدمها فى Index و Export حتى لا نكرر الكود.
        // =========================================================
        private IQueryable<SOLine> BuildQuery(
            int? soId,
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
            // 1) الاستعلام الأساسي: سطور أوامر البيع + الهيدر (SalesOrder)
            var q = _context.SOLines
                            .Include(x => x.SalesOrder)      // علشان SODate + الحالة لو احتجناها
                            .AsNoTracking()
                            .AsQueryable();

            // 2) فلترة باختياري برقم أمر البيع
            if (soId.HasValue && soId.Value > 0)
            {
                q = q.Where(x => x.SOId == soId.Value);
            }

            // 3) فلتر نطاق رقم السطر (من / إلى)
            if (fromCode.HasValue)
            {
                q = q.Where(x => x.LineNo >= fromCode.Value);
            }

            if (toCode.HasValue)
            {
                q = q.Where(x => x.LineNo <= toCode.Value);
            }

            // 4) فلترة بالتاريخ من الهيدر (SODate) لو تم تفعيلها
            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                var from = fromDate.Value;
                var to = toDate.Value;

                q = q.Where(x =>
                    x.SalesOrder != null &&
                    x.SalesOrder.SODate >= from &&
                    x.SalesOrder.SODate <= to);
            }

            // 5) حقول نصية (string) للبحث بالكلمات
            var stringFields =
                new Dictionary<string, Expression<Func<SOLine, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["pricebasis"] = x => x.PriceBasis ?? "",          // مرجع السعر
                    ["batch"] = x => x.PreferredBatchNo ?? ""     // التشغيلة المفضّلة
                };

            // 6) حقول رقمية int للبحث بالأرقام
            var intFields =
                new Dictionary<string, Expression<Func<SOLine, int>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["soid"] = x => x.SOId,           // رقم أمر البيع
                    ["prod"] = x => x.ProdId,         // رقم الصنف (ProdId)
                    ["lineno"] = x => x.LineNo,         // رقم السطر
                    ["qty"] = x => x.QtyRequested    // الكمية المطلوبة
                };

            // 7) حقول الترتيب (Sorting)
            var orderFields =
                new Dictionary<string, Expression<Func<SOLine, object>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SOId"] = x => x.SOId,
                    ["LineNo"] = x => x.LineNo,
                    ["ProdId"] = x => x.ProdId,
                    ["Qty"] = x => x.QtyRequested,
                    ["Batch"] = x => x.PreferredBatchNo ?? "",
                    ["PriceBasis"] = x => x.PriceBasis ?? "",
                    // لو حبيت ترتب بتاريخ الأمر نفسه:
                    ["SODate"] = x => x.SalesOrder != null ? x.SalesOrder.SODate : DateTime.MinValue
                };

            // 8) تطبيق البحث + الترتيب بالدالة الموحّدة ApplySearchSort
            q = q.ApplySearchSort(
                    search, searchBy,
                    sort, dir,
                    stringFields, intFields, orderFields,
                    defaultSearchBy: "all",
                    defaultSortBy: "SOId"
                );

            return q;
        }

        // =========================================================
        // INDEX: قائمة سطور أوامر البيع
        // =========================================================
        public async Task<IActionResult> Index(
            int? soId,                  // فلتر برقم أمر البيع (SOId كـ int)
            string? search,             // نص البحث
            string? searchBy = "all",   // حقل البحث
            string? sort = "SOId",      // عمود الترتيب
            string? dir = "asc",        // اتجاه الترتيب asc/desc
            int page = 1,               // رقم الصفحة
            int pageSize = 50,          // حجم الصفحة
            bool useDateRange = false,  // هل فلتر التاريخ مفعل؟
            DateTime? fromDate = null,  // من تاريخ (SODate)
            DateTime? toDate = null,    // إلى تاريخ
            int? fromCode = null,       // من رقم سطر
            int? toCode = null,         // إلى رقم سطر
            string? dateField = "SODate"// اسم حقل التاريخ (للعرض فقط)
        )
        {
            // نستخدم BuildQuery علشان نعيد نفس المنطق فى Index و Export
            var q = BuildQuery(
                soId,
                search,
                searchBy,
                sort,
                dir,
                useDateRange,
                fromDate,
                toDate,
                fromCode,
                toCode
            );

            // الترقيم PagedResult
            var model = await PagedResult<SOLine>.CreateAsync(q, page, pageSize);

            // تخزين إعدادات التاريخ فى الموديل علشان الواجهة تعرض القيم مرة أخرى
            model.UseDateRange = useDateRange;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            // فلتر SOId للواجهة
            ViewBag.FilterSOId = soId;
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.DateField = dateField ?? "SODate";

            // إعدادات البحث/الترتيب الحالية
            ViewBag.Search = search ?? "";
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.Sort = sort ?? "SOId";
            ViewBag.Dir = (dir?.ToLower() == "desc") ? "desc" : "asc";

            ViewBag.Page = model.PageNumber;
            ViewBag.PageSize = model.PageSize;
            ViewBag.Total = model.TotalCount;

            // خيارات البحث للـ DropDown فى الواجهة
            ViewBag.SearchOptions = new List<SelectListItem>
            {
                new("الكل",           "all")
                {
                    Selected = (searchBy ?? "all")
                               .Equals("all", StringComparison.OrdinalIgnoreCase)
                },

                new("رقم الأمر",      "soid")
                {
                    Selected = string.Equals(searchBy, "soid", StringComparison.OrdinalIgnoreCase)
                },

                new("الكود (ProdId)", "prod")
                {
                    Selected = string.Equals(searchBy, "prod", StringComparison.OrdinalIgnoreCase)
                },

                new("رقم السطر",      "lineno")
                {
                    Selected = string.Equals(searchBy, "lineno", StringComparison.OrdinalIgnoreCase)
                },

                new("الكمية",         "qty")
                {
                    Selected = string.Equals(searchBy, "qty", StringComparison.OrdinalIgnoreCase)
                },

                new("التشغيلة",       "batch")
                {
                    Selected = string.Equals(searchBy, "batch", StringComparison.OrdinalIgnoreCase)
                },

                new("مرجع السعر",     "pricebasis")
                {
                    Selected = string.Equals(searchBy, "pricebasis", StringComparison.OrdinalIgnoreCase)
                }
            };

            // خيارات الترتيب
            ViewBag.SortOptions = new List<SelectListItem>
            {
                new("رقم الأمر",      "SOId")
                {
                    Selected = string.Equals(sort, "SOId", StringComparison.OrdinalIgnoreCase)
                },

                new("رقم السطر",      "LineNo")
                {
                    Selected = string.Equals(sort, "LineNo", StringComparison.OrdinalIgnoreCase)
                },

                new("الصنف (ProdId)", "ProdId")
                {
                    Selected = string.Equals(sort, "ProdId", StringComparison.OrdinalIgnoreCase)
                },

                new("الكمية",         "Qty")
                {
                    Selected = string.Equals(sort, "Qty", StringComparison.OrdinalIgnoreCase)
                }
            };

            return View(model);
        }

        // =========================================================
        // DETAILS: عرض سطر واحد (SOId + LineNo)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Details(int soId, int lineNo)
        {
            if (soId <= 0)
                return BadRequest();

            var line = await _context.SOLines
                                     .Include(x => x.SalesOrder)
                                     .AsNoTracking()
                                     .FirstOrDefaultAsync(x =>
                                         x.SOId == soId &&
                                         x.LineNo == lineNo);

            if (line == null)
                return NotFound();

            return View(line);   // Views/SOLines/Details.cshtml
        }

        // =========================================================
        // DELETE: حذف سطر واحد + إعادة تجميع أمر البيع
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int soId, int lineNo)
        {
            var line = await _context.SOLines
                                     .Include(x => x.SalesOrder)
                                     .FirstOrDefaultAsync(x =>
                                         x.SOId == soId &&
                                         x.LineNo == lineNo);

            if (line == null)
            {
                TempData["Error"] = "السطر المطلوب غير موجود.";
                return RedirectToAction(nameof(Index), new { soId });
            }

            try
            {
                _context.SOLines.Remove(line);
                await _context.SaveChangesAsync();

                // بعد الحذف نعيد حساب إجماليات أمر البيع
                await _docTotals.RecalcSalesOrderTotalsAsync(soId);

                TempData["Success"] = "تم حذف السطر بنجاح.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "تعذر حذف السطر: " + ex.Message;
            }

            return RedirectToAction(nameof(Index), new { soId });
        }

        // =========================================================
        // BULK DELETE: حذف عدة أسطر معًا (selectedKeys = "SOId:LineNo,...")
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedKeys)
        {
            if (string.IsNullOrWhiteSpace(selectedKeys))
            {
                TempData["Error"] = "لم يتم اختيار أي أسطر للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var keys = selectedKeys
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(k =>
                {
                    var parts = k.Split(':');
                    if (parts.Length != 2) return (soId: (int?)null, lineNo: (int?)null);

                    bool ok1 = int.TryParse(parts[0], out int so);
                    bool ok2 = int.TryParse(parts[1], out int ln);
                    return (soId: ok1 ? so : (int?)null, lineNo: ok2 ? ln : (int?)null);
                })
                .Where(p => p.soId.HasValue && p.lineNo.HasValue)
                .Select(p => new { SOId = p.soId!.Value, LineNo = p.lineNo!.Value })
                .ToList();

            if (!keys.Any())
            {
                TempData["Error"] = "صيغة مفاتيح الأسطر غير صحيحة.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var soIds = keys.Select(k => k.SOId).Distinct().ToList();

                var lines = await _context.SOLines
                    .Where(l => keys.Any(k => k.SOId == l.SOId && k.LineNo == l.LineNo))
                    .ToListAsync();

                if (!lines.Any())
                {
                    TempData["Error"] = "لم يتم العثور على الأسطر المطلوبة.";
                    return RedirectToAction(nameof(Index));
                }

                _context.SOLines.RemoveRange(lines);
                await _context.SaveChangesAsync();

                // إعادة تجميع لكل أمر بيع متأثر
                foreach (var soId in soIds)
                {
                    await _docTotals.RecalcSalesOrderTotalsAsync(soId);
                }

                TempData["Success"] = $"تم حذف {lines.Count} سطر/أسطر بنجاح.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "تعذر حذف الأسطر: " + ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // DELETE ALL: حذف جميع سطور أوامر البيع + إعادة تجميع الهيدر
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var allIds = await _context.SOLines
                .Select(l => l.SOId)
                .Distinct()
                .ToListAsync();

            var allLines = await _context.SOLines.ToListAsync();

            if (!allLines.Any())
            {
                TempData["Error"] = "لا توجد سطور أوامر بيع لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _context.SOLines.RemoveRange(allLines);
                await _context.SaveChangesAsync();

                // إعادة تجميع كل أوامر البيع التى كان لها سطور
                foreach (var soId in allIds)
                {
                    await _docTotals.RecalcSalesOrderTotalsAsync(soId);
                }

                TempData["Success"] = "تم حذف جميع سطور أوامر البيع وإعادة تجميع الهيدر.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "تعذر حذف جميع السطور: " + ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // EXPORT: تصدير بيانات سطور أوامر البيع (Excel أو CSV)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Export(
            int? soId,
            string? search,
            string? searchBy = "all",
            string? sort = "SOId",
            string? dir = "asc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string format = "excel"   // excel | csv
        )
        {
            var q = BuildQuery(
                soId,
                search,
                searchBy,
                sort,
                dir,
                useDateRange,
                fromDate,
                toDate,
                fromCode,
                toCode
            );

            var data = await q.ToListAsync();

            var fileNameBase = $"SOLines_{DateTime.Now:yyyyMMdd_HHmmss}";

            if (string.Equals(format, "excel", StringComparison.OrdinalIgnoreCase))
            {
                // ===== تصدير إلى ملف Excel باستخدام ClosedXML =====
                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("SOLines");

                // عناوين الأعمدة
                int row = 1;
                ws.Cell(row, 1).Value = "SOId";
                ws.Cell(row, 2).Value = "LineNo";
                ws.Cell(row, 3).Value = "ProdId";
                ws.Cell(row, 4).Value = "QtyRequested";
                ws.Cell(row, 5).Value = "RequestedRetailPrice";
                ws.Cell(row, 6).Value = "SalesDiscountPct";
                ws.Cell(row, 7).Value = "ExpectedUnitPrice";
                ws.Cell(row, 8).Value = "ExpectedLineTotal";
                ws.Cell(row, 9).Value = "PriceBasis";
                ws.Cell(row, 10).Value = "PreferredBatchNo";
                ws.Cell(row, 11).Value = "PreferredExpiry";
                ws.Cell(row, 12).Value = "SODate";

                // البيانات
                row = 2;
                foreach (var l in data)
                {
                    ws.Cell(row, 1).Value = l.SOId;
                    ws.Cell(row, 2).Value = l.LineNo;
                    ws.Cell(row, 3).Value = l.ProdId;
                    ws.Cell(row, 4).Value = l.QtyRequested;
                    ws.Cell(row, 5).Value = l.RequestedRetailPrice;
                    ws.Cell(row, 6).Value = l.SalesDiscountPct;
                    ws.Cell(row, 7).Value = l.ExpectedUnitPrice;
                    ws.Cell(row, 8).Value = l.ExpectedUnitPrice * l.QtyRequested;
                    ws.Cell(row, 9).Value = l.PriceBasis ?? "";
                    ws.Cell(row, 10).Value = l.PreferredBatchNo ?? "";
                    ws.Cell(row, 11).Value = l.PreferredExpiry?.ToString("yyyy-MM-dd") ?? "";
                    ws.Cell(row, 12).Value = l.SalesOrder?.SODate.ToString("yyyy-MM-dd") ?? "";
                    row++;
                }

                ws.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                wb.SaveAs(stream);
                var content = stream.ToArray();

                var fileNameXlsx = fileNameBase + ".xlsx";
                const string contentTypeXlsx =
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

                return File(content, contentTypeXlsx, fileNameXlsx);
            }
            else
            {
                // ===== تصدير CSV بسيط كخيار بديل =====
                var sb = new StringBuilder();

                // عناوين الأعمدة
                sb.AppendLine("SOId,LineNo,ProdId,QtyRequested,RequestedRetailPrice,SalesDiscountPct,ExpectedUnitPrice,ExpectedLineTotal,PriceBasis,PreferredBatchNo,PreferredExpiry,SODate");

                foreach (var l in data)
                {
                    string expectedLineTotal = (l.ExpectedUnitPrice * l.QtyRequested)
                        .ToString("0.####", CultureInfo.InvariantCulture);

                    string preferredExpiry = l.PreferredExpiry?.ToString("yyyy-MM-dd") ?? "";
                    string soDate = l.SalesOrder?.SODate.ToString("yyyy-MM-dd") ?? "";

                    sb.AppendLine(string.Join(",",
                        l.SOId,
                        l.LineNo,
                        l.ProdId,
                        l.QtyRequested,
                        l.RequestedRetailPrice.ToString("0.00", CultureInfo.InvariantCulture),
                        l.SalesDiscountPct.ToString("0.##", CultureInfo.InvariantCulture),
                        l.ExpectedUnitPrice.ToString("0.####", CultureInfo.InvariantCulture),
                        expectedLineTotal,
                        EscapeCsv(l.PriceBasis),
                        EscapeCsv(l.PreferredBatchNo),
                        preferredExpiry,
                        soDate
                    ));
                }

                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                var fileNameCsv = fileNameBase + ".csv";

                return File(bytes, "text/csv", fileNameCsv);
            }
        }

        // دالة مساعدة للهروب داخل CSV (لو في فواصل/علامات تنصيص)
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
