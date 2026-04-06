using System;                                   // تواريخ وأوقات
using System.Collections.Generic;               // القوائم List
using System.IO;                                // MemoryStream لتصدير Excel
using System.Linq;                              // LINQ: Where / OrderBy
using System.Text;                              // لبناء ملف CSV
using System.Threading.Tasks;                   // async / await
using ClosedXML.Excel;                          // تصدير Excel (.xlsx)
using Microsoft.AspNetCore.Mvc;                 // أساس الكنترولر
using Microsoft.EntityFrameworkCore;            // Include / AsNoTracking
using ERP.Data;                                 // AppDbContext
using ERP.Filters;
using ERP.Models;                               // الموديل PILine + PurchaseInvoice
using ERP.Infrastructure;                       // PagedResult لتقسيم الصفحات
using ERP.Security;
using ERP.Services;

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إدارة جدول سطور فواتير الشراء (PILines)
    /// - عرض قائمة السطور مع بحث / ترتيب / تقسيم صفحات.
    /// - فلترة بتاريخ الفاتورة (من رأس الفاتورة).
    /// - فلترة من رقم فاتورة / إلى رقم فاتورة.
    /// - حذف محدد / حذف الكل.
    /// - تصدير CSV/Excel.
    /// </summary>
    [RequirePermission("PILines.Index")]
    public class PILinesController : Controller
    {
        // كائن الاتصال بقاعدة البيانات
        private readonly AppDbContext _context;
        private readonly IListVisibilityService _listVisibilityService;

        public PILinesController(AppDbContext context, IListVisibilityService listVisibilityService)
        {
            _context = context;
            _listVisibilityService = listVisibilityService;
        }

        private async Task<IQueryable<PILine>> ApplyOperationalListVisibilityAsync(IQueryable<PILine> query)
        {
            if (await _listVisibilityService.CanViewAllOperationalListsAsync())
                return query;

            var creatorNames = await _listVisibilityService.GetCurrentUserCreatorNamesAsync();
            if (creatorNames.Count == 0)
                return query.Where(_ => false);

            return query.Where(l => l.PurchaseInvoice != null && l.PurchaseInvoice.CreatedBy != null && creatorNames.Contains(l.PurchaseInvoice.CreatedBy.Trim()));
        }

        #region Index (قائمة سطور فواتير الشراء)

        /// <summary>
        /// عرض قائمة سطور فواتير الشراء بنفس نظام القوائم الموحد.
        /// </summary>
        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        public async Task<IActionResult> Index(
            string? search,                      // نص البحث
            string? searchBy,                    // طريقة البحث: all / piid / prod / batch / expiry
            string? searchMode = null,           // يحتوي / يبدأ / ينتهي
            string? sort = null,                 // عمود الترتيب: piid / lineno / prod / qty / unitcost / disc / retail / linevalue / batch / expiry / date
            string? dir = null,                  // اتجاه الترتيب: asc / desc
            bool useDateRange = false,           // تفعيل فلتر التاريخ؟
            DateTime? fromDate = null,           // من تاريخ
            DateTime? toDate = null,             // إلى تاريخ
            string? dateField = "PIDate",        // الحقل المستخدم لفلتر التاريخ (تاريخ الفاتورة)
            int? fromCode = null,                // من رقم فاتورة (PIId)
            int? toCode = null,                  // إلى رقم فاتورة
            string? filterCol_piid = null,
            string? filterCol_piidExpr = null,
            string? filterCol_lineno = null,
            string? filterCol_linenoExpr = null,
            string? filterCol_prod = null,
            string? filterCol_prodExpr = null,
            string? filterCol_prodname = null,
            string? filterCol_qty = null,
            string? filterCol_qtyExpr = null,
            string? filterCol_unitcost = null,
            string? filterCol_unitcostExpr = null,
            string? filterCol_disc = null,
            string? filterCol_discExpr = null,
            string? filterCol_retail = null,
            string? filterCol_retailExpr = null,
            string? filterCol_linevalue = null,
            string? filterCol_linevalueExpr = null,
            string? filterCol_batch = null,
            string? filterCol_expiry = null,
            string? filterCol_date = null,
            int page = 1,                        // رقم الصفحة
            int pageSize = 10                    // حجم الصفحة (افتراضي 10؛ 0 = الكل)
        )
        {
            var pageSizeQuery = Request.Query["pageSize"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageSizeQuery) && int.TryParse(pageSizeQuery, out var psVal))
                pageSize = psVal;

            searchBy ??= "all";
            sort ??= "piid";
            dir ??= "asc";
            dateField ??= "PIDate";
            searchMode = NormalizeSearchMode(searchMode);

            if (page < 1) page = 1;
            if (pageSize < 0) pageSize = 10;
            bool pageSizeOk = pageSize == 0 || pageSize == 10 || pageSize == 25 || pageSize == 50 || pageSize == 100 || pageSize == 200;
            if (pageSize > 0 && !pageSizeOk)
                pageSize = 10;

            // نبدأ بالاستعلام من جدول PILines مع تحميل رأس الفاتورة (PurchaseInvoice)
            IQueryable<PILine> query = _context.PILines
                .Include(l => l.Product)          // اسم الصنف
                .Include(l => l.PurchaseInvoice)       // تحميل الفاتورة للحصول على التاريخ وغيره
                .AsNoTracking();
            query = await ApplyOperationalListVisibilityAsync(query);

            // نقرأ codeFrom / codeTo من الكويري لو موجودين
            int? codeFrom = Request.Query.ContainsKey("codeFrom")
                ? TryParseNullableInt(Request.Query["codeFrom"])
                : null;

            int? codeTo = Request.Query.ContainsKey("codeTo")
                ? TryParseNullableInt(Request.Query["codeTo"])
                : null;

            int? finalFromCode = fromCode ?? codeFrom;
            int? finalToCode = toCode ?? codeTo;

            // 1) تطبيق الفلاتر (بحث + من/إلى رقم فاتورة + تاريخ الفاتورة)
            query = ApplyFilters(
                query,
                search,
                searchBy,
                searchMode,
                finalFromCode,
                finalToCode,
                useDateRange,
                fromDate,
                toDate,
                dateField
            );

            // 1b) تطبيق فلاتر الأعمدة (بنمط Excel + Expr للأعمدة الرقمية)
            query = ApplyColumnFilters(
                query,
                filterCol_piid, filterCol_piidExpr,
                filterCol_lineno, filterCol_linenoExpr,
                filterCol_prod, filterCol_prodExpr,
                filterCol_prodname,
                filterCol_qty, filterCol_qtyExpr,
                filterCol_unitcost, filterCol_unitcostExpr,
                filterCol_disc, filterCol_discExpr,
                filterCol_retail, filterCol_retailExpr,
                filterCol_linevalue, filterCol_linevalueExpr,
                filterCol_batch, filterCol_expiry, filterCol_date);

            // 2) تطبيق الترتيب
            bool sortDesc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            query = ApplySort(query, sort, sortDesc);

            // =========================================================
            // حساب إجمالي قيمة السطر من نفس الاستعلام (بعد الفلاتر)
            // قيمة السطر = (Qty * PriceRetail) * (1 - PurchaseDiscountPct / 100)
            // =========================================================
            decimal totalLineValue = await query.SumAsync(line =>
                (decimal?)((line.Qty * line.PriceRetail) * (1m - (line.PurchaseDiscountPct / 100m)))) ?? 0m;

            int totalQtyFiltered = await query.SumAsync(l => l.Qty);

            // 3) حساب العدد الكلي بعد الفلاتر
            int totalCount = await query.CountAsync();

            int effectivePageSize = pageSize;
            if (pageSize == 0)
            {
                effectivePageSize = totalCount == 0 ? 10 : Math.Min(totalCount, 100_000);
                page = 1;
            }

            // 4) قراءة صفحة واحدة فقط
            var items = await query
                .Skip((page - 1) * effectivePageSize)
                .Take(effectivePageSize)
                .ToListAsync();

            int totalPages = pageSize == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)effectivePageSize);

            // 5) تجهيز PagedResult
            var model = new PagedResult<PILine>
            {
                Items = items,
                PageNumber = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                HasPrevious = page > 1,
                HasNext = pageSize != 0 && page < totalPages,
                Search = search,
                SortColumn = sort,
                SortDescending = sortDesc,
                UseDateRange = useDateRange,
                FromDate = fromDate,
                ToDate = toDate
            };

            // 6) تمرير القيم للـ ViewBag علشان الواجهة تحفظ الحالة
            ViewBag.Search = search;
            ViewBag.SearchBy = searchBy;
            ViewBag.SearchMode = searchMode;
            ViewBag.Sort = sort;
            ViewBag.Dir = sortDesc ? "desc" : "asc";
            ViewBag.DateField = dateField;

            ViewBag.FromCode = finalFromCode;
            ViewBag.ToCode = finalToCode;
            ViewBag.CodeFrom = finalFromCode;
            ViewBag.CodeTo = finalToCode;

            ViewBag.FilterCol_Piid = filterCol_piid;
            ViewBag.FilterCol_PiidExpr = filterCol_piidExpr;
            ViewBag.FilterCol_Lineno = filterCol_lineno;
            ViewBag.FilterCol_LinenoExpr = filterCol_linenoExpr;
            ViewBag.FilterCol_Prod = filterCol_prod;
            ViewBag.FilterCol_ProdExpr = filterCol_prodExpr;
            ViewBag.FilterCol_Prodname = filterCol_prodname;
            ViewBag.FilterCol_Qty = filterCol_qty;
            ViewBag.FilterCol_QtyExpr = filterCol_qtyExpr;
            ViewBag.FilterCol_Unitcost = filterCol_unitcost;
            ViewBag.FilterCol_UnitcostExpr = filterCol_unitcostExpr;
            ViewBag.FilterCol_Disc = filterCol_disc;
            ViewBag.FilterCol_DiscExpr = filterCol_discExpr;
            ViewBag.FilterCol_Retail = filterCol_retail;
            ViewBag.FilterCol_RetailExpr = filterCol_retailExpr;
            ViewBag.FilterCol_Linevalue = filterCol_linevalue;
            ViewBag.FilterCol_LinevalueExpr = filterCol_linevalueExpr;
            ViewBag.FilterCol_Batch = filterCol_batch;
            ViewBag.FilterCol_Expiry = filterCol_expiry;
            ViewBag.FilterCol_Date = filterCol_date;

            ViewBag.TotalLineValue = totalLineValue;
            ViewBag.TotalQtyFiltered = totalQtyFiltered;

            return View(model);
        }

        /// <summary>
        /// API: جلب القيم المميزة لعمود (للفلترة بنمط Excel).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var columnLower = (column ?? "").Trim().ToLowerInvariant();

            var baseQuery = await ApplyOperationalListVisibilityAsync(
                _context.PILines.Include(l => l.Product).Include(l => l.PurchaseInvoice).AsNoTracking());

            if (columnLower == "piid")
            {
                var ids = await baseQuery.Select(l => l.PIId).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "lineno")
            {
                var nums = await baseQuery.Select(l => l.LineNo).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(nums.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "prod")
            {
                var ids = await baseQuery.Select(l => l.ProdId).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "prodname")
            {
                var names = await baseQuery.Where(l => l.Product != null && l.Product.ProdName != null)
                    .Select(l => l.Product!.ProdName!).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm))
                    names = names.Where(s => s.ToLowerInvariant().Contains(searchTerm)).ToList();
                return Json(names.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "batch")
            {
                var vals = await baseQuery.Where(l => l.BatchNo != null).Select(l => l.BatchNo!).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm))
                    vals = vals.Where(s => s.ToLowerInvariant().Contains(searchTerm)).ToList();
                return Json(vals.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "expiry")
            {
                var dates = await baseQuery.Where(l => l.Expiry.HasValue).Select(l => l.Expiry!.Value.Date).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(dates.Select(d => new { value = d.ToString("yyyy-MM-dd"), display = d.ToString("yyyy-MM-dd") }));
            }
            if (columnLower == "date")
            {
                var dates = await baseQuery.Where(l => l.PurchaseInvoice != null).Select(l => l.PurchaseInvoice!.PIDate.Date).Distinct().OrderByDescending(x => x).Take(500).ToListAsync();
                return Json(dates.Select(d => new { value = d.ToString("yyyy-MM-dd"), display = d.ToString("yyyy-MM-dd") }));
            }
            if (columnLower == "qty")
            {
                var vals = await baseQuery.Select(l => l.Qty).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(vals.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "unitcost")
            {
                var vals = await baseQuery.Select(l => l.UnitCost).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(vals.Select(v => new { value = v.ToString("0.00"), display = v.ToString("0.00") }));
            }
            if (columnLower == "disc")
            {
                var vals = await baseQuery.Select(l => l.PurchaseDiscountPct).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(vals.Select(v => new { value = v.ToString("0.00"), display = v.ToString("0.00") }));
            }
            if (columnLower == "retail")
            {
                var vals = await baseQuery.Select(l => l.PriceRetail).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(vals.Select(v => new { value = v.ToString("0.00"), display = v.ToString("0.00") }));
            }
            if (columnLower == "linevalue")
            {
                var vals = await baseQuery.Select(l => (l.Qty * l.PriceRetail) * (1m - (l.PurchaseDiscountPct / 100m))).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(vals.Select(v => new { value = v.ToString("0.00"), display = v.ToString("0.00") }));
            }

            return Json(Array.Empty<object>());
        }

        #endregion

        #region BulkDelete / DeleteAll (حذف السطور)

        /// <summary>
        /// حذف مجموعة من السطور.
        /// selectedKeys يجي بصيغة: "PIId:LineNo,PIId:LineNo,..."
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedKeys)
        {
            if (string.IsNullOrWhiteSpace(selectedKeys))
            {
                TempData["ErrorMessage"] = "لم يتم اختيار أي سطر للحذف.";
                return RedirectToAction(nameof(Index));
            }

            // نحول "1:1,1:2,2:1" إلى قائمة من (PIId, LineNo)
            var keys = selectedKeys
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseCompositeKey)
                .Where(k => k.HasValue)
                .Select(k => k!.Value)
                .ToList();

            if (!keys.Any())
            {
                TempData["ErrorMessage"] = "لم يتم التعرف على أرقام السطور المحددة.";
                return RedirectToAction(nameof(Index));
            }

            var invoicesIds = keys.Select(k => k.PIId).Distinct().ToList();

            var lines = await _context.PILines
                .Where(l => invoicesIds.Contains(l.PIId))
                .ToListAsync();

            // نفلتر تاني على LineNo علشان المفتاح مركب
            var linesToDelete = lines
                .Where(l => keys.Any(k => k.PIId == l.PIId && k.LineNo == l.LineNo))
                .ToList();

            if (linesToDelete.Any())
            {
                _context.PILines.RemoveRange(linesToDelete);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"تم حذف {linesToDelete.Count} سطر من فواتير الشراء.";
            }
            else
            {
                TempData["ErrorMessage"] = "لم يتم العثور على السطور المحددة في قاعدة البيانات.";
            }

            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// حذف جميع سطور فواتير الشراء (جدول PILines بالكامل).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var allLines = await _context.PILines.ToListAsync();

            if (!allLines.Any())
            {
                TempData["ErrorMessage"] = "لا توجد سطور لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            _context.PILines.RemoveRange(allLines);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "تم حذف جميع سطور فواتير الشراء بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        #endregion

        #region Export (تصدير السطور)

        /// <summary>
        /// تصدير سطور فواتير الشراء بعد الفلاتر إلى CSV/Excel.
        /// format = excel أو csv (الاتنين CSV حاليًا).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Export(
            string? format,
            string? search,
            string? searchBy,
            string? searchMode,
            string? sort,
            string? dir,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? dateField = "PIDate",
            int? codeFrom = null,
            int? codeTo = null,
            string? filterCol_piid = null,
            string? filterCol_piidExpr = null,
            string? filterCol_lineno = null,
            string? filterCol_linenoExpr = null,
            string? filterCol_prod = null,
            string? filterCol_prodExpr = null,
            string? filterCol_prodname = null,
            string? filterCol_batch = null,
            string? filterCol_expiry = null,
            string? filterCol_date = null,
            string? filterCol_qty = null,
            string? filterCol_qtyExpr = null,
            string? filterCol_unitcost = null,
            string? filterCol_unitcostExpr = null,
            string? filterCol_disc = null,
            string? filterCol_discExpr = null,
            string? filterCol_retail = null,
            string? filterCol_retailExpr = null,
            string? filterCol_linevalue = null,
            string? filterCol_linevalueExpr = null
        )
        {
            format = string.IsNullOrWhiteSpace(format) ? "excel" : format.ToLowerInvariant();
            searchBy ??= "all";
            sort ??= "piid";
            dir ??= "asc";
            dateField ??= "PIDate";
            searchMode = NormalizeSearchMode(searchMode);

            bool sortDesc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            IQueryable<PILine> query = _context.PILines
                .Include(l => l.Product)
                .Include(l => l.PurchaseInvoice)
                .AsNoTracking();
            query = await ApplyOperationalListVisibilityAsync(query);

            query = ApplyFilters(
                query,
                search,
                searchBy,
                searchMode,
                codeFrom,
                codeTo,
                useDateRange,
                fromDate,
                toDate,
                dateField
            );

            query = ApplyColumnFilters(
                query,
                filterCol_piid, filterCol_piidExpr,
                filterCol_lineno, filterCol_linenoExpr,
                filterCol_prod, filterCol_prodExpr,
                filterCol_prodname,
                filterCol_qty, filterCol_qtyExpr,
                filterCol_unitcost, filterCol_unitcostExpr,
                filterCol_disc, filterCol_discExpr,
                filterCol_retail, filterCol_retailExpr,
                filterCol_linevalue, filterCol_linevalueExpr,
                filterCol_batch, filterCol_expiry, filterCol_date);

            query = ApplySort(query, sort, sortDesc);

            var list = await query.ToListAsync();

            if (string.Equals(format, "excel", StringComparison.OrdinalIgnoreCase))
            {
                // ===== تصدير Excel حقيقي (.xlsx) باستخدام ClosedXML =====
                using var workbook = new XLWorkbook();
                var ws = workbook.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("أصناف فاتورة المشتريات"));

                int row = 1;
                ws.Cell(row, 1).Value = "رقم الفاتورة";
                ws.Cell(row, 2).Value = "رقم السطر";
                ws.Cell(row, 3).Value = "كود الصنف";
                ws.Cell(row, 4).Value = "اسم الصنف";
                ws.Cell(row, 5).Value = "الكمية";
                ws.Cell(row, 6).Value = "تكلفة الوحدة";
                ws.Cell(row, 7).Value = "خصم الشراء %";
                ws.Cell(row, 8).Value = "سعر الجمهور";
                ws.Cell(row, 9).Value = "قيمة السطر";
                ws.Cell(row, 10).Value = "التشغيلة";
                ws.Cell(row, 11).Value = "الصلاحية";
                ws.Cell(row, 12).Value = "تاريخ الفاتورة";
                ws.Range(row, 1, row, 12).Style.Font.Bold = true;

                foreach (var l in list)
                {
                    row++;
                    decimal lineValue = (l.Qty * l.PriceRetail) * (1m - (l.PurchaseDiscountPct / 100m));
                    ws.Cell(row, 1).Value = l.PIId;
                    ws.Cell(row, 2).Value = l.LineNo;
                    ws.Cell(row, 3).Value = l.ProdId;
                    ws.Cell(row, 4).Value = l.Product?.ProdName ?? "";
                    ws.Cell(row, 5).Value = l.Qty;
                    ws.Cell(row, 6).Value = l.UnitCost;
                    ws.Cell(row, 7).Value = l.PurchaseDiscountPct;
                    ws.Cell(row, 8).Value = l.PriceRetail;
                    ws.Cell(row, 9).Value = lineValue;
                    ws.Cell(row, 10).Value = l.BatchNo ?? "";
                    ws.Cell(row, 11).Value = l.Expiry?.ToString("yyyy-MM-dd") ?? "";
                    ws.Cell(row, 12).Value = l.PurchaseInvoice?.PIDate.ToString("yyyy-MM-dd") ?? "";
                }

                ws.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                var bytesXlsx = stream.ToArray();
                var fileNameXlsx = ExcelExportNaming.ArabicTimestampedFileName("أصناف فاتورة المشتريات", ".xlsx");
                const string contentTypeXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                return File(bytesXlsx, contentTypeXlsx, fileNameXlsx);
            }

            // ===== تصدير CSV =====
            var sb = new StringBuilder();
            sb.AppendLine("رقم الفاتورة,رقم السطر,كود الصنف,اسم الصنف,الكمية,تكلفة الوحدة,خصم الشراء %,سعر الجمهور,قيمة السطر,التشغيلة,الصلاحية,تاريخ الفاتورة");

            foreach (var l in list)
            {
                string batch = (l.BatchNo ?? "").Replace(",", " ");
                string expiryStr = l.Expiry.HasValue ? l.Expiry.Value.ToString("yyyy-MM-dd") : "";
                string dateStr = l.PurchaseInvoice?.PIDate.ToString("yyyy-MM-dd") ?? "";
                string prodName = (l.Product?.ProdName ?? "").Replace(",", " ");
                decimal lineValue = (l.Qty * l.PriceRetail) * (1m - (l.PurchaseDiscountPct / 100m));

                var line = string.Join(",",
                    l.PIId,
                    l.LineNo,
                    l.ProdId,
                    "\"" + prodName.Replace("\"", "\"\"") + "\"",
                    l.Qty,
                    l.UnitCost.ToString("0.0000"),
                    l.PurchaseDiscountPct.ToString("0.00"),
                    l.PriceRetail.ToString("0.00"),
                    lineValue.ToString("0.00"),
                    batch,
                    expiryStr,
                    dateStr
                );
                sb.AppendLine(line);
            }

            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString());
            var fileName = ExcelExportNaming.ArabicTimestampedFileName("أصناف فاتورة المشتريات", ".csv");
            return File(bytes, "text/csv", fileName);
        }

        #endregion

        #region دوال الفلترة والترتيب والمساعدة

        private static string NormalizeSearchMode(string? mode)
        {
            var m = (mode ?? "").Trim().ToLowerInvariant();
            if (m == "startswith" || m == "starts" || m == "start" || m == "يبدأ") return "startswith";
            if (m == "endswith" || m == "ends" || m == "end" || m == "ينتهي") return "endswith";
            return "contains";
        }

        private static bool MatchSearchText(string? hay, string needle, string searchMode)
        {
            if (string.IsNullOrEmpty(needle)) return true;
            hay ??= "";
            if (searchMode == "startswith")
                return hay.StartsWith(needle, StringComparison.OrdinalIgnoreCase);
            if (searchMode == "endswith")
                return hay.EndsWith(needle, StringComparison.OrdinalIgnoreCase);
            return hay.Contains(needle, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// فلترة سطور الفواتير:
        /// - نص البحث (حسب searchBy + searchMode)
        /// - من رقم فاتورة / إلى رقم فاتورة
        /// - فلتر بتاريخ الفاتورة (من PurchaseInvoice.PIDate)
        /// </summary>
        private static IQueryable<PILine> ApplyFilters(
            IQueryable<PILine> query,
            string? search,
            string? searchBy,
            string? searchMode,
            int? fromCode,
            int? toCode,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            string dateField
        )
        {
            searchBy ??= "all";
            dateField ??= "PIDate";
            searchMode = NormalizeSearchMode(searchMode);

            // 1) فلتر نص البحث
            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();

                switch (searchBy.ToLower())
                {
                    case "piid":
                        if (int.TryParse(search, out var piIdVal) && searchMode == "contains" && search == piIdVal.ToString())
                            query = query.Where(l => l.PIId == piIdVal);
                        else
                            query = query.Where(l => MatchSearchText(l.PIId.ToString(), search, searchMode));
                        break;

                    case "prod":
                        if (int.TryParse(search, out var prodIdVal) && searchMode == "contains" && search == prodIdVal.ToString())
                            query = query.Where(l => l.ProdId == prodIdVal);
                        else
                            query = query.Where(l => MatchSearchText(l.ProdId.ToString(), search, searchMode));
                        break;

                    case "batch":
                        query = query.Where(l => l.BatchNo != null && MatchSearchText(l.BatchNo, search, searchMode));
                        break;

                    case "expiry":
                        if (DateTime.TryParse(search, out var expVal))
                        {
                            var d = expVal.Date;
                            query = query.Where(l => l.Expiry.HasValue && l.Expiry.Value.Date == d);
                        }
                        break;

                    case "all":
                    default:
                        query = query.Where(l =>
                            MatchSearchText(l.PIId.ToString(), search, searchMode) ||
                            MatchSearchText(l.ProdId.ToString(), search, searchMode) ||
                            (l.BatchNo != null && MatchSearchText(l.BatchNo, search, searchMode)) ||
                            (l.PurchaseInvoice != null && MatchSearchText(l.PurchaseInvoice.PIDate.ToString(), search, searchMode)) ||
                            (l.Product != null && l.Product.ProdName != null && MatchSearchText(l.Product.ProdName, search, searchMode))
                        );
                        break;
                }
            }

            // 2) فلتر من/إلى رقم فاتورة (PIId)
            if (fromCode.HasValue)
                query = query.Where(l => l.PIId >= fromCode.Value);

            if (toCode.HasValue)
                query = query.Where(l => l.PIId <= toCode.Value);

            // 3) فلتر التاريخ من رأس الفاتورة (PIDate أو CreatedAt لو حبيت مستقبلاً)
            if (useDateRange && (fromDate.HasValue || toDate.HasValue))
            {
                bool useCreated = string.Equals(dateField, "CreatedAt", StringComparison.OrdinalIgnoreCase);

                if (fromDate.HasValue)
                {
                    if (useCreated)
                        query = query.Where(l => l.PurchaseInvoice != null && l.PurchaseInvoice.CreatedAt >= fromDate.Value);
                    else
                        query = query.Where(l => l.PurchaseInvoice != null && l.PurchaseInvoice.PIDate >= fromDate.Value);
                }

                if (toDate.HasValue)
                {
                    if (useCreated)
                        query = query.Where(l => l.PurchaseInvoice != null && l.PurchaseInvoice.CreatedAt <= toDate.Value);
                    else
                        query = query.Where(l => l.PurchaseInvoice != null && l.PurchaseInvoice.PIDate <= toDate.Value);
                }
            }

            return query;
        }

        /// <summary>
        /// تطبيق فلاتر الأعمدة (بنمط Excel) على استعلام سطور فواتير الشراء؛ الأعمدة الرقمية تدعم filterCol_*Expr.
        /// </summary>
        private static IQueryable<PILine> ApplyColumnFilters(
            IQueryable<PILine> query,
            string? filterCol_piid,
            string? filterCol_piidExpr,
            string? filterCol_lineno,
            string? filterCol_linenoExpr,
            string? filterCol_prod,
            string? filterCol_prodExpr,
            string? filterCol_prodname,
            string? filterCol_qty,
            string? filterCol_qtyExpr,
            string? filterCol_unitcost,
            string? filterCol_unitcostExpr,
            string? filterCol_disc,
            string? filterCol_discExpr,
            string? filterCol_retail,
            string? filterCol_retailExpr,
            string? filterCol_linevalue,
            string? filterCol_linevalueExpr,
            string? filterCol_batch,
            string? filterCol_expiry,
            string? filterCol_date
        )
        {
            if (!string.IsNullOrWhiteSpace(filterCol_piidExpr))
                query = PILineListNumericExpr.ApplyPiIdExpr(query, filterCol_piidExpr);
            else if (!string.IsNullOrWhiteSpace(filterCol_piid))
            {
                var ids = filterCol_piid.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    query = query.Where(l => ids.Contains(l.PIId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_linenoExpr))
                query = PILineListNumericExpr.ApplyLineNoExpr(query, filterCol_linenoExpr);
            else if (!string.IsNullOrWhiteSpace(filterCol_lineno))
            {
                var nums = filterCol_lineno.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (nums.Count > 0)
                    query = query.Where(l => nums.Contains(l.LineNo));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_prodExpr))
                query = PILineListNumericExpr.ApplyProdIdExpr(query, filterCol_prodExpr);
            else if (!string.IsNullOrWhiteSpace(filterCol_prod))
            {
                var ids = filterCol_prod.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    query = query.Where(l => ids.Contains(l.ProdId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_prodname))
            {
                var vals = filterCol_prodname.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    query = query.Where(l => l.Product != null && l.Product.ProdName != null && vals.Contains(l.Product.ProdName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_qtyExpr))
                query = PILineListNumericExpr.ApplyQtyExpr(query, filterCol_qtyExpr);
            else if (!string.IsNullOrWhiteSpace(filterCol_qty))
            {
                var nums = filterCol_qty.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (nums.Count > 0)
                    query = query.Where(l => nums.Contains(l.Qty));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_unitcostExpr))
                query = PILineListNumericExpr.ApplyUnitCostExpr(query, filterCol_unitcostExpr);
            else if (!string.IsNullOrWhiteSpace(filterCol_unitcost))
            {
                var vals = filterCol_unitcost.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0)
                    query = query.Where(l => vals.Contains(l.UnitCost));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_discExpr))
                query = PILineListNumericExpr.ApplyDiscExpr(query, filterCol_discExpr);
            else if (!string.IsNullOrWhiteSpace(filterCol_disc))
            {
                var vals = filterCol_disc.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0)
                    query = query.Where(l => vals.Contains(l.PurchaseDiscountPct));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_retailExpr))
                query = PILineListNumericExpr.ApplyRetailExpr(query, filterCol_retailExpr);
            else if (!string.IsNullOrWhiteSpace(filterCol_retail))
            {
                var vals = filterCol_retail.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0)
                    query = query.Where(l => vals.Contains(l.PriceRetail));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_linevalueExpr))
                query = PILineListNumericExpr.ApplyLineValueExpr(query, filterCol_linevalueExpr);
            else if (!string.IsNullOrWhiteSpace(filterCol_linevalue))
            {
                var vals = filterCol_linevalue.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0)
                    query = query.Where(l => vals.Contains((l.Qty * l.PriceRetail) * (1m - (l.PurchaseDiscountPct / 100m))));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_batch))
            {
                var vals = filterCol_batch.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    query = query.Where(l => l.BatchNo != null && vals.Contains(l.BatchNo));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_expiry))
            {
                var dates = filterCol_expiry.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => DateTime.TryParse(x.Trim(), out var d) ? d.Date : (DateTime?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (dates.Count > 0)
                    query = query.Where(l => l.Expiry.HasValue && dates.Contains(l.Expiry.Value.Date));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_date))
            {
                var dates = filterCol_date.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => DateTime.TryParse(x.Trim(), out var d) ? d.Date : (DateTime?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (dates.Count > 0)
                    query = query.Where(l => l.PurchaseInvoice != null && dates.Contains(l.PurchaseInvoice.PIDate.Date));
            }
            return query;
        }

        /// <summary>
        /// ترتيب سطور الفواتير حسب العمود المحدد من الواجهة.
        /// </summary>
        private static IQueryable<PILine> ApplySort(
            IQueryable<PILine> query,
            string? sort,
            bool desc
        )
        {
            sort = (sort ?? "piid").ToLower();

            switch (sort)
            {
                case "lineno":
                    query = desc
                        ? query.OrderByDescending(l => l.PIId).ThenByDescending(l => l.LineNo)
                        : query.OrderBy(l => l.PIId).ThenBy(l => l.LineNo);
                    break;

                case "prod":
                    query = desc
                        ? query.OrderByDescending(l => l.ProdId).ThenByDescending(l => l.PIId).ThenByDescending(l => l.LineNo)
                        : query.OrderBy(l => l.ProdId).ThenBy(l => l.PIId).ThenBy(l => l.LineNo);
                    break;

                case "qty":
                    query = desc
                        ? query.OrderByDescending(l => l.Qty).ThenByDescending(l => l.PIId).ThenByDescending(l => l.LineNo)
                        : query.OrderBy(l => l.Qty).ThenBy(l => l.PIId).ThenBy(l => l.LineNo);
                    break;

                case "unitcost":
                    query = desc
                        ? query.OrderByDescending(l => l.UnitCost).ThenByDescending(l => l.PIId).ThenByDescending(l => l.LineNo)
                        : query.OrderBy(l => l.UnitCost).ThenBy(l => l.PIId).ThenBy(l => l.LineNo);
                    break;

                case "disc":
                    query = desc
                        ? query.OrderByDescending(l => l.PurchaseDiscountPct).ThenByDescending(l => l.PIId).ThenByDescending(l => l.LineNo)
                        : query.OrderBy(l => l.PurchaseDiscountPct).ThenBy(l => l.PIId).ThenBy(l => l.LineNo);
                    break;

                case "retail":
                    query = desc
                        ? query.OrderByDescending(l => l.PriceRetail).ThenByDescending(l => l.PIId).ThenByDescending(l => l.LineNo)
                        : query.OrderBy(l => l.PriceRetail).ThenBy(l => l.PIId).ThenBy(l => l.LineNo);
                    break;

                case "batch":
                    query = desc
                        ? query.OrderByDescending(l => l.BatchNo).ThenByDescending(l => l.PIId).ThenByDescending(l => l.LineNo)
                        : query.OrderBy(l => l.BatchNo).ThenBy(l => l.PIId).ThenBy(l => l.LineNo);
                    break;

                case "expiry":
                    query = desc
                        ? query.OrderByDescending(l => l.Expiry).ThenByDescending(l => l.PIId).ThenByDescending(l => l.LineNo)
                        : query.OrderBy(l => l.Expiry).ThenBy(l => l.PIId).ThenBy(l => l.LineNo);
                    break;

                case "date":
                    query = desc
                        ? query.OrderByDescending(l => l.PurchaseInvoice.PIDate).ThenByDescending(l => l.PIId).ThenByDescending(l => l.LineNo)
                        : query.OrderBy(l => l.PurchaseInvoice.PIDate).ThenBy(l => l.PIId).ThenBy(l => l.LineNo);
                    break;

                case "linevalue":
                    query = desc
                        ? query.OrderByDescending(l => (l.Qty * l.PriceRetail) * (1m - (l.PurchaseDiscountPct / 100m))).ThenByDescending(l => l.PIId).ThenByDescending(l => l.LineNo)
                        : query.OrderBy(l => (l.Qty * l.PriceRetail) * (1m - (l.PurchaseDiscountPct / 100m))).ThenBy(l => l.PIId).ThenBy(l => l.LineNo);
                    break;

                case "piid":
                default:
                    query = desc
                        ? query.OrderByDescending(l => l.PIId).ThenByDescending(l => l.LineNo)
                        : query.OrderBy(l => l.PIId).ThenBy(l => l.LineNo);
                    break;
            }

            return query;
        }

        /// <summary>
        /// تحويل نص مثل "123" إلى int?، يرجع null لو التحويل فشل.
        /// </summary>
        private static int? TryParseNullableInt(string? value)
        {
            if (int.TryParse(value, out var i))
                return i;

            return null;
        }

        /// <summary>
        /// تحويل نص مركب "PIId:LineNo" إلى (PIId, LineNo).
        /// </summary>
        private static (int PIId, int LineNo)? ParseCompositeKey(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;

            var parts = input.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return null;

            if (!int.TryParse(parts[0], out var piId)) return null;
            if (!int.TryParse(parts[1], out var lineNo)) return null;

            return (piId, lineNo);
        }

        #endregion
    }
}
