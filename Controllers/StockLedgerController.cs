using System;                                       // متغيرات: DateTime
using System.Collections.Generic;                   // Dictionary
using System.IO;                                    // MemoryStream
using System.Linq;                                  // LINQ
using System.Linq.Expressions;                      // Expression<Func<>>
using System.Text;                                  // StringBuilder + Encoding (للـ CSV)
using System.Threading.Tasks;                       // Task/async
using ClosedXML.Excel;                              // مكتبة Excel
using ERP.Data;                                     // AppDbContext
using ERP.Filters;
using ERP.Infrastructure;                           // ApplySearchSort + PagedResult
using ERP.Models;                                   // StockLedger
using ERP.Security;
using Microsoft.AspNetCore.Mvc;                     // Controller
using Microsoft.AspNetCore.Mvc.Rendering;           // SelectListItem
using Microsoft.EntityFrameworkCore;                // AsNoTracking, ToListAsync

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر دفتر الحركة المخزنية:
    /// عرض + بحث + ترتيب + ترقيم + تصدير (Excel أو CSV) حسب اختيار المستخدم.
    /// </summary>
    [RequirePermission(PermissionCodes.InventoryScreens.StockLedger_View)]
    public class StockLedgerController : Controller
    {
        private readonly AppDbContext context;   // متغير: سياق قاعدة البيانات

        public StockLedgerController(AppDbContext ctx) => context = ctx;

        // =========================
        // قواميس البحث/الترتيب المشتركة
        // =========================

        // حقول نصية للبحث (string)
        private static readonly Dictionary<string, Expression<Func<StockLedger, string?>>> StringFields
            = new(StringComparer.OrdinalIgnoreCase)
            {
                ["batch"] = x => x.BatchNo!,    // التشغيلة
                ["source"] = x => x.SourceType,  // نوع المصدر
            };

        // حقول رقمية للبحث (int)
        private static readonly Dictionary<string, Expression<Func<StockLedger, int>>> IntFields
            = new(StringComparer.OrdinalIgnoreCase)
            {
                ["sourceline"] = x => x.SourceLine,    // رقم سطر المصدر
                ["qtyin"] = x => x.QtyIn,         // كمية داخلة
                ["qtyout"] = x => x.QtyOut,        // كمية خارجة
                ["entry"] = x => x.EntryId,       // رقم القيد
                ["warehouse"] = x => x.WarehouseId,   // المخزن
                ["product"] = x => x.ProdId,        // الصنف
                ["sourceid"] = x => x.SourceId       // رقم المصدر
            };

        // حقول للترتيب (OrderBy)
        private static readonly Dictionary<string, Expression<Func<StockLedger, object>>> OrderFields
            = new(StringComparer.OrdinalIgnoreCase)
            {
                ["TranDate"] = x => x.TranDate,
                ["EntryId"] = x => x.EntryId,
                ["Warehouse"] = x => x.WarehouseId,
                ["Product"] = x => x.ProdId,
                ["Expiry"] = x => x.Expiry ?? DateTime.MaxValue,
                ["QtyIn"] = x => x.QtyIn,
                ["QtyOut"] = x => x.QtyOut,
                ["UnitCost"] = x => x.UnitCost
            };

        // =========================
        // Index — تقرير دفتر الحركة
        // =========================
        [HttpGet]
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",
            string? sort = "TranDate",
            string? dir = "desc",
            int page = 1,
            int pageSize = 50,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null)
        {
            // الاستعلام الأساسي
            IQueryable<StockLedger> q = context.StockLedger
                .AsNoTracking()
                .Include(x => x.Product)     // تعليق: تحميل اسم الصنف
                .Include(x => x.Batch);      // تعليق: تحميل بيانات التشغيلة (لا نزيلها)

            // تطبيق نظام البحث/الترتيب الموحد
            q = q.ApplySearchSort(
                search, searchBy,
                sort, dir,
                StringFields, IntFields, OrderFields,
                defaultSearchBy: "all",
                defaultSortBy: "TranDate"
            );

            // فلترة بالتاريخ (اختيارية)
            if (useDateRange)
            {
                if (fromDate.HasValue)
                    q = q.Where(x => x.TranDate >= fromDate.Value);

                if (toDate.HasValue)
                    q = q.Where(x => x.TranDate <= toDate.Value);
            }

            // إنشاء PagedResult (نظام القوائم الموحد)
            var model = await PagedResult<StockLedger>.CreateAsync(q, page, pageSize);

            // =========================================================
            // ✅ (جديد) تعبئة خصم الشراء من "سطور الشراء" PILines
            // الفكرة:
            // - StockLedger فيه: SourceType + SourceId + SourceLine
            // - الخصم موجود في جدول PILines داخل PurchaseDiscountPct
            // - نملأ PurchaseDiscount في Rows الخاصة بالشراء فقط (لصفحة النتائج الحالية)
            // =========================================================
            var pageRows = model.Items; // متغير: صفوف الصفحة الحالية (اسمها Items عندك)

            // متغير: صفوف الشراء الظاهرة في الصفحة (اللي ينفع نجيب خصمها)
            var purchaseRows = pageRows
                .Where(x =>
                    x.SourceType == "Purchase" &&   // تعليق: حركة شراء
                    x.SourceId > 0 &&               // تعليق: رقم فاتورة الشراء
                    x.SourceLine > 0)               // ✅ مهم: اسم الحقل عندك SourceLine وليس SourceLineNo
                .Select(x => new { x.EntryId, PIId = x.SourceId, LineNo = x.SourceLine })
                .ToList();

            if (purchaseRows.Any())
            {
                // متغير: كل فواتير الشراء الموجودة في الصفحة
                var piIds = purchaseRows.Select(x => x.PIId).Distinct().ToList();

                // متغير: نجلب خصومات سطور الشراء لهذه الفواتير فقط (أسرع)
                // ⚠️ أسماء الأعمدة من ملف PILine.cs عندك: PIId, LineNo, PurchaseDiscountPct
                var piLines = await context.PILines
                    .AsNoTracking()
                    .Where(l => piIds.Contains(l.PIId))
                    .Select(l => new
                    {
                        l.PIId,
                        l.LineNo,
                        Disc = (decimal?)l.PurchaseDiscountPct ?? 0m   // متغير: خصم الشراء %
                    })
                    .ToListAsync();

                // متغير: Lookup سريع (PIId + LineNo) => Disc
                var discLookup = piLines.ToDictionary(
                    x => (x.PIId, x.LineNo),
                    x => x.Disc
                );

                // تعليق: نملأ PurchaseDiscount داخل صفوف StockLedger في الذاكرة
                // بدون أي Update للـ DB (عرض فقط)
                foreach (var row in pageRows)
                {
                    if (row.SourceType == "Purchase" && row.SourceId > 0 && row.SourceLine > 0)
                    {
                        if (discLookup.TryGetValue((row.SourceId, row.SourceLine), out var disc))
                        {
                            // ✅ اكتبها في العمود الموجود بالفعل في StockLedger
                            // (حتى لو كانت Null هتظهر الآن في الجدول)
                            row.PurchaseDiscount = disc;
                        }
                        else
                        {
                            // لو السطر مش موجود لأي سبب
                            row.PurchaseDiscount = 0m;
                        }
                    }
                }
            }

            // تخزين قيم الفلاتر داخل الموديل
            var s = (search ?? "").Trim();
            var sb = (searchBy ?? "all").Trim();
            var so = (sort ?? "TranDate").Trim();
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            model.Search = s;
            model.SearchBy = sb;
            model.SortColumn = so;
            model.SortDescending = desc;
            model.UseDateRange = useDateRange;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            // خيارات البحث
            ViewBag.SearchOptions = new List<SelectListItem>
    {
        new("الكل",              "all")       { Selected = sb.Equals("all",        StringComparison.OrdinalIgnoreCase) },
        new("رقم القيد",         "entry")     { Selected = sb.Equals("entry",      StringComparison.OrdinalIgnoreCase) },
        new("المخزن",           "warehouse") { Selected = sb.Equals("warehouse",   StringComparison.OrdinalIgnoreCase) },
        new("الصنف",            "product")   { Selected = sb.Equals("product",     StringComparison.OrdinalIgnoreCase) },
        new("التشغيلة",         "batch")     { Selected = sb.Equals("batch",       StringComparison.OrdinalIgnoreCase) },
        new("نوع المصدر",       "source")    { Selected = sb.Equals("source",      StringComparison.OrdinalIgnoreCase) },
        new("رقم المصدر",       "sourceid")  { Selected = sb.Equals("sourceid",    StringComparison.OrdinalIgnoreCase) },
        new("سطر المصدر (رقم)", "sourceline"){ Selected = sb.Equals("sourceline",  StringComparison.OrdinalIgnoreCase) }
    };

            // خيارات الترتيب
            ViewBag.SortOptions = new List<SelectListItem>
    {
        new("التاريخ",        "TranDate")  { Selected = so.Equals("TranDate",  StringComparison.OrdinalIgnoreCase) },
        new("رقم القيد",      "EntryId")   { Selected = so.Equals("EntryId",   StringComparison.OrdinalIgnoreCase) },
        new("المخزن",        "Warehouse") { Selected = so.Equals("Warehouse", StringComparison.OrdinalIgnoreCase) },
        new("الصنف",         "Product")   { Selected = so.Equals("Product",   StringComparison.OrdinalIgnoreCase) },
        new("الصلاحية",      "Expiry")    { Selected = so.Equals("Expiry",    StringComparison.OrdinalIgnoreCase) },
        new("كمية داخلة",    "QtyIn")     { Selected = so.Equals("QtyIn",     StringComparison.OrdinalIgnoreCase) },
        new("كمية خارجة",    "QtyOut")    { Selected = so.Equals("QtyOut",    StringComparison.OrdinalIgnoreCase) },
        new("تكلفة الوحدة",  "UnitCost")  { Selected = so.Equals("UnitCost",  StringComparison.OrdinalIgnoreCase) }
    };

            // تمرير القيم الحالية (احتياطى)
            ViewBag.Search = s;
            ViewBag.SearchBy = sb;
            ViewBag.Sort = so;
            ViewBag.Dir = desc ? "desc" : "asc";
            ViewBag.PageSize = model.PageSize;
            ViewBag.UseDateRange = useDateRange;
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;

            return View(model);
        }










        // =========================
        // Export — تصدير حسب اختيار المستخدم (Excel أو CSV)
        // =========================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy = "all",
            string? sort = "TranDate",
            string? dir = "desc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? format = "excel")   // المتغير المهم: نوع التصدير
        {
            // الاستعلام الأساسي
            IQueryable<StockLedger> q = context.StockLedger.AsNoTracking();

            // تطبيق نفس منطق البحث/الترتيب
            q = q.ApplySearchSort(
                search, searchBy,
                sort, dir,
                StringFields, IntFields, OrderFields,
                defaultSearchBy: "all",
                defaultSortBy: "TranDate"
            );

            // فلترة بالتاريخ
            if (useDateRange)
            {
                if (fromDate.HasValue)
                    q = q.Where(x => x.TranDate >= fromDate.Value);

                if (toDate.HasValue)
                    q = q.Where(x => x.TranDate <= toDate.Value);
            }

            // جلب كل الصفوف (بدون Paging)
            var rows = await q
                .OrderBy(x => x.TranDate)
                .ThenBy(x => x.EntryId)
                .ToListAsync();

            // توحيد قيمة format
            format = (format ?? "excel").Trim().ToLowerInvariant();

            // ===== الفرع الأول: CSV =====
            if (format == "csv")
            {
                var sb = new StringBuilder();   // متغير: نص CSV فى الذاكرة

                // عناوين الأعمدة
                sb.AppendLine(string.Join(",",
                    Csv("رقم القيد"),
                    Csv("تاريخ الحركة"),
                    Csv("كود المخزن"),
                    Csv("كود الصنف"),
                    Csv("التشغيلة"),
                    Csv("تاريخ الصلاحية"),
                    Csv("كمية داخلة"),
                    Csv("كمية خارجة"),
                    Csv("تكلفة/وحدة"),
                    Csv("المتبقي (للدخول)"),
                    Csv("نوع المصدر"),
                    Csv("رقم المصدر"),
                    Csv("سطر المصدر")
                ));

                // البيانات
                foreach (var x in rows)
                {
                    sb.AppendLine(string.Join(",",
                        Csv(x.EntryId.ToString()),
                        Csv(x.TranDate.ToString("yyyy-MM-dd HH:mm:ss")),
                        Csv(x.WarehouseId.ToString()),
                        Csv(x.ProdId.ToString()),
                        Csv(x.BatchNo),
                        Csv(x.Expiry?.ToString("yyyy-MM-dd")),
                        Csv(x.QtyIn.ToString()),
                        Csv(x.QtyOut.ToString()),
                        Csv(x.UnitCost.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        Csv(x.RemainingQty?.ToString()),
                        Csv(x.SourceType),
                        Csv(x.SourceId.ToString()),
                        Csv(x.SourceLine.ToString())
                    ));
                }

                // تحويل لـ UTF-8 مع BOM علشان Excel يقرأ عربى صح
                var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
                var bytes = utf8.GetBytes(sb.ToString());
                var fileNameCsv = $"StockLedger_{DateTime.Now:yyyyMMdd_HHmm}_csv.csv";

                return File(bytes, "text/csv; charset=utf-8", fileNameCsv);
            }

            // ===== الفرع الثانى: Excel (XLSX) =====
            using var workbook = new XLWorkbook();                // متغير: ملف Excel
            var ws = workbook.Worksheets.Add("StockLedger");      // متغير: ورقة العمل

            int r = 1; // متغير: رقم الصف الحالى

            // عناوين الأعمدة
            ws.Cell(r, 1).Value = "رقم القيد";
            ws.Cell(r, 2).Value = "تاريخ الحركة";
            ws.Cell(r, 3).Value = "كود المخزن";
            ws.Cell(r, 4).Value = "كود الصنف";
            ws.Cell(r, 5).Value = "التشغيلة";
            ws.Cell(r, 6).Value = "تاريخ الصلاحية";
            ws.Cell(r, 7).Value = "كمية داخلة";
            ws.Cell(r, 8).Value = "كمية خارجة";
            ws.Cell(r, 9).Value = "تكلفة/وحدة";
            ws.Cell(r, 10).Value = "المتبقي (للدخول)";
            ws.Cell(r, 11).Value = "نوع المصدر";
            ws.Cell(r, 12).Value = "رقم المصدر";
            ws.Cell(r, 13).Value = "سطر المصدر";

            // تنسيق العناوين
            var headerRange = ws.Range(r, 1, r, 13);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // تعبئة البيانات
            foreach (var x in rows)
            {
                r++;

                ws.Cell(r, 1).Value = x.EntryId;
                ws.Cell(r, 2).Value = x.TranDate;
                ws.Cell(r, 3).Value = x.WarehouseId;
                ws.Cell(r, 4).Value = x.ProdId;
                ws.Cell(r, 5).Value = x.BatchNo ?? "";
                ws.Cell(r, 6).Value = x.Expiry;
                ws.Cell(r, 7).Value = x.QtyIn;
                ws.Cell(r, 8).Value = x.QtyOut;
                ws.Cell(r, 9).Value = x.UnitCost;
                ws.Cell(r, 10).Value = x.RemainingQty ?? 0;
                ws.Cell(r, 11).Value = x.SourceType;
                ws.Cell(r, 12).Value = x.SourceId;
                ws.Cell(r, 13).Value = x.SourceLine;
            }

            // ضبط عرض الأعمدة + تنسيق الأرقام
            ws.Columns().AdjustToContents();
            ws.Column(7).Style.NumberFormat.Format = "0";
            ws.Column(8).Style.NumberFormat.Format = "0";
            ws.Column(9).Style.NumberFormat.Format = "0.0000";
            ws.Column(10).Style.NumberFormat.Format = "0";

            using var stream = new MemoryStream();     // متغير: الذاكرة المؤقتة
            workbook.SaveAs(stream);
            stream.Position = 0;

            var fileNameXlsx = $"StockLedger_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
            const string contentTypeXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

            return File(stream.ToArray(), contentTypeXlsx, fileNameXlsx);
        }

        // دالة مساعدة لتجهيز النص للـ CSV
        private static string Csv(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            var s = value.Replace("\"", "\"\"");   // استبدال " بـ ""

            if (s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
                return "\"" + s + "\"";           // لو فيه فواصل/سطور جديدة نحط النص بين ""

            return s;
        }
    }
}
