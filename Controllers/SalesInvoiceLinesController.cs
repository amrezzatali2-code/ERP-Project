using System;                                     // متغيرات التاريخ DateTime
using System.Collections.Generic;                 // Dictionary, List
using System.Globalization;                       // CultureInfo لتنسيق الأرقام فى التصدير
using System.Linq;                                // أوامر LINQ
using System.Linq.Expressions;                    // تعبيرات الخرائط للبحث/الترتيب
using System.Text;                                // StringBuilder لإنشاء CSV
using System.Threading.Tasks;                     // async / await
using ClosedXML.Excel;                            // مكتبة إنشاء ملف Excel فعلياً
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;         // SelectListItem لقوائم البحث/الترتيب
using Microsoft.EntityFrameworkCore;
using ERP.Data;                                   // AppDbContext
using ERP.Filters;
using ERP.Models;                                 // SalesInvoiceLine
using ERP.Infrastructure;                         // PagedResult + ApplySearchSort
using ERP.Security;
using ERP.Services;                               // DocumentTotalsService

namespace ERP.Controllers
{
    /// <summary>كنترولر سطور فاتورة البيع — عرض/تفاصيل + بحث/ترتيب/تقسيم + حذف/تصدير</summary>
    [RequirePermission(PermissionCodes.SalesLines.InvoiceLines_View)]
    public class SalesInvoiceLinesController : Controller
    {
        private readonly AppDbContext _context;            // متغير: كائن الاتصال بقاعدة البيانات
        private readonly DocumentTotalsService _docTotals; // متغير: خدمة إجماليات المستندات (لإعادة تجميع هيدر فاتورة البيع بعد الحذف)

        public SalesInvoiceLinesController(AppDbContext context,
                                           DocumentTotalsService docTotals)
        {
            _context = context;       // تخزين سياق قاعدة البيانات
            _docTotals = docTotals;   // تخزين سيرفيس التجميع لإعادة حساب إجماليات فاتورة البيع
        }

        // =========================================================
        // دالة خاصة: إعادة حساب قيم السطر (منطق السطر نفسه)
        // =========================================================
        // متغير m: يمثل سطر فاتورة بيع واحد، يتم تعديل قيمه (UnitSalePrice / LineTotalAfterDiscount / TaxValue / LineNetTotal)
        private void RecalcLine(SalesInvoiceLine m)
        {
            // التأكد من القيم (حماية بسيطة)
            if (m.Qty < 0) m.Qty = 0;
            if (m.Disc1Percent < 0) m.Disc1Percent = 0;
            if (m.Disc2Percent < 0) m.Disc2Percent = 0;
            if (m.Disc3Percent < 0) m.Disc3Percent = 0;
            if (m.TaxPercent < 0) m.TaxPercent = 0;

            // 1) تطبيق خصومات النِّسَب على سعر الجمهور
            decimal p = m.PriceRetail;
            p = p * (1 - (m.Disc1Percent / 100m));
            p = p * (1 - (m.Disc2Percent / 100m));
            p = p * (1 - (m.Disc3Percent / 100m));

            // 2) خصم القيمة يوزّع على الوحدة
            if (m.Qty > 0 && m.DiscountValue > 0)
                p -= (m.DiscountValue / m.Qty);

            if (p < 0) p = 0;

            // 3) سعر الوحدة بعد الخصم (قبل الضريبة)
            m.UnitSalePrice = Math.Round(p, 2, MidpointRounding.AwayFromZero);

            // 4) إجمالي السطر بعد الخصم (قبل الضريبة)
            m.LineTotalAfterDiscount = Math.Round(m.UnitSalePrice * m.Qty, 2, MidpointRounding.AwayFromZero);

            // 5) الضريبة والصافي
            m.TaxValue = Math.Round(m.LineTotalAfterDiscount * (m.TaxPercent / 100m), 2, MidpointRounding.AwayFromZero);
            m.LineNetTotal = m.LineTotalAfterDiscount + m.TaxValue;
        }

        // =========================================================
        // CleanSearchFromColumnNames — تنظيف search من أسماء الأعمدة
        // =========================================================
        private string? CleanSearchFromColumnNames(string? search)
        {
            if (string.IsNullOrWhiteSpace(search))
                return null;

            var searchTrimmed = search.Trim();
            var searchLower = searchTrimmed.ToLowerInvariant();

            // قائمة أسماء الأعمدة المعروفة (بالإنجليزية والعربية)
            var columnNames = new[]
            {
                "siid", "line", "lineno", "prodid", "prodname", "customername",
                "qty", "price", "unit", "total", "net", "disc1", "disc2", "disc3", "discval",
                "batch", "expiry", "رقم الفاتورة", "رقم السطر", "كود الصنف", "اسم الصنف",
                "اسم العميل", "الكمية", "سعر الجمهور", "التشغيلة", "الصلاحية"
            };

            // إذا كان search مطابق تماماً لأي اسم عمود، نرجعه null
            if (columnNames.Contains(searchLower))
                return null;

            // إذا كان search يبدأ أو ينتهي باسم عمود، ننظفه
            foreach (var colName in columnNames)
            {
                if (searchLower == colName || searchLower.StartsWith(colName + " ") || searchLower.EndsWith(" " + colName))
                {
                    return null;
                }
            }

            return searchTrimmed;
        }

        // =========================================================
        // دالة خاصة: بناء الاستعلام مع البحث + الترتيب (نستخدمها فى Index و Export)
        // =========================================================
        // المتغير siId: فلتر اختياري برقم الفاتورة
        // المتغير search: نص البحث
        // المتغير searchBy: نوع البحث (all / id / prod / batch / line)
        // المتغير sort: عمود الترتيب
        // المتغير dir: اتجاه الترتيب asc/desc
        private IQueryable<SalesInvoiceLine> BuildQuery(
            int? siId,
            string? search,
            string? searchBy,
            string? sort,
            string? dir)
        {
            // (1) الاستعلام الأساسي مع تضمين العلاقات (Product و SalesInvoice.Customer)
            IQueryable<SalesInvoiceLine> q =
                _context.SalesInvoiceLines
                    .Include(x => x.Product)
                    .Include(x => x.SalesInvoice)
                        .ThenInclude(si => si.Customer)
                    .AsNoTracking();

            // (2) فلترة اختيارية برقم الفاتورة
            if (siId.HasValue)
                q = q.Where(x => x.SIId == siId.Value);

            // (3) خرائط الحقول للبحث (Strings + Ints) والفرز

            // الحقول النصية (string)
            var stringFields =
                new Dictionary<string, Expression<Func<SalesInvoiceLine, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["batch"] = x => x.BatchNo ?? "",                    // التشغيلة
                    ["prodname"] = x => x.Product != null ? (x.Product.ProdName ?? "") : "",  // اسم الصنف
                    ["customername"] = x => x.SalesInvoice != null && x.SalesInvoice.Customer != null 
                        ? (x.SalesInvoice.Customer.CustomerName ?? "") : "",  // اسم العميل
                    ["notes"] = x => x.Notes ?? ""                       // الملاحظات
                };

            // الحقول الرقمية (int) — رقم الفاتورة / الصنف / السطر
            var intFields =
                new Dictionary<string, Expression<Func<SalesInvoiceLine, int>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = x => x.SIId,      // رقم الفاتورة
                    ["si"] = x => x.SIId,      // رقم الفاتورة (مرادف)
                    ["prod"] = x => x.ProdId,  // رقم الصنف (ProdId)
                    ["line"] = x => x.LineNo,   // رقم السطر
                    ["qty"] = x => x.Qty        // الكمية
                };

            // مفاتيح الترتيب المسموحة
            var orderFields =
                new Dictionary<string, Expression<Func<SalesInvoiceLine, object>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SIId"] = x => x.SIId,                      // رقم الفاتورة
                    ["LineNo"] = x => x.LineNo,                    // رقم السطر
                    ["ProdId"] = x => x.ProdId,                    // رقم الصنف
                    ["ProdName"] = x => x.Product != null ? (x.Product.ProdName ?? "") : "",  // اسم الصنف
                    ["CustomerName"] = x => x.SalesInvoice != null && x.SalesInvoice.Customer != null 
                        ? (x.SalesInvoice.Customer.CustomerName ?? "") : "",  // اسم العميل
                    ["Qty"] = x => x.Qty,                       // الكمية
                    ["Price"] = x => x.PriceRetail,               // سعر الجمهور
                    ["Unit"] = x => x.UnitSalePrice,             // سعر الوحدة بعد الخصم
                    ["Total"] = x => x.LineTotalAfterDiscount,    // إجمالي السطر بعد الخصم
                    ["Net"] = x => x.LineNetTotal,              // الصافي بعد الضريبة
                    ["Expiry"] = x => x.Expiry ?? DateTime.MaxValue,  // الصلاحية (لو null نخليها تاريخ كبير)
                    ["Disc1"] = x => x.Disc1Percent,             // خصم 1%
                    ["Disc2"] = x => x.Disc2Percent,             // خصم 2%
                    ["Disc3"] = x => x.Disc3Percent,             // خصم 3%
                    ["DiscVal"] = x => x.DiscountValue,          // قيمة الخصم
                    ["Batch"] = x => x.BatchNo ?? ""             // التشغيلة
                };

            // (4) تطبيق منظومة البحث/الترتيب الموحدة
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
        // Index — عرض عام + إمكانية التصفية برقم الفاتورة (SIId)
        // مع بحث/ترتيب/تقسيم باستخدام ApplySearchSort + PagedResult
        // =========================================================
        public async Task<IActionResult> Index(
            int? siId,                   // فلتر اختياري برقم الفاتورة (int?)
            string? search,              // نص البحث
            string? searchBy = "all",    // all | id | prod | batch | line
            string? sort = "LineNo",     // LineNo | SIId | ProdId | Qty | ...
            string? dir = "asc",         // asc | desc
            int page = 1,
            int pageSize = 50)
        {
            // تنظيف search من أسماء الأعمدة المعروفة
            var cleanedSearch = CleanSearchFromColumnNames(search);

            // تجهيز الاستعلام باستخدام الدالة الموحدة (باستخدام search الأصلي للبحث)
            var q = BuildQuery(siId, search, searchBy, sort, dir);

            // =========================================================
            // حساب الإجماليات من نفس الاستعلام (بعد الفلاتر)
            // ✅ مهم: لازم قبل الـ PagedResult علشان ما تتحسبش على الصفحة بس
            // =========================================================
            int totalQty = await q.SumAsync(line => (int?)line.Qty) ?? 0;
            decimal totalDiscountValue = await q.SumAsync(line => (decimal?)line.DiscountValue) ?? 0m;
            decimal totalAfterDiscount = await q.SumAsync(line => (decimal?)line.LineTotalAfterDiscount) ?? 0m;
            decimal totalNet = await q.SumAsync(line => (decimal?)line.LineNetTotal) ?? 0m;

            // تطبيع اتجاه الترتيب وتحويله إلى bool
            var dirNorm = (dir?.ToLower() == "asc") ? "asc" : "desc";
            bool descending = dirNorm == "desc";

            // تعريف متغير model قبل if statement
            PagedResult<SalesInvoiceLine> model;

            // (5) تطبيق ترتيب مخصص للحقول التي تحتوي على navigation properties
            // لأن EF Core لا يستطيع ترجمة OrderBy على navigation properties مباشرة
            var sortNorm = (sort ?? "LineNo").Trim();
            if (string.Equals(sortNorm, "ProdName", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sortNorm, "CustomerName", StringComparison.OrdinalIgnoreCase))
            {
                // تحميل البيانات أولاً ثم الترتيب في الذاكرة
                var allItems = await q.ToListAsync();
                
                if (string.Equals(sortNorm, "ProdName", StringComparison.OrdinalIgnoreCase))
                {
                    allItems = descending
                        ? allItems.OrderByDescending(x => x.Product?.ProdName ?? "").ToList()
                        : allItems.OrderBy(x => x.Product?.ProdName ?? "").ToList();
                }
                else if (string.Equals(sortNorm, "CustomerName", StringComparison.OrdinalIgnoreCase))
                {
                    allItems = descending
                        ? allItems.OrderByDescending(x => x.SalesInvoice?.Customer?.CustomerName ?? "").ToList()
                        : allItems.OrderBy(x => x.SalesInvoice?.Customer?.CustomerName ?? "").ToList();
                }

                // تطبيق Pagination يدوياً
                var totalCount = allItems.Count;
                var pagedItems = allItems
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                // إنشاء PagedResult
                model = new PagedResult<SalesInvoiceLine>(pagedItems, page, pageSize, totalCount)
                {
                    Search = cleanedSearch ?? "",
                    SearchBy = searchBy ?? "all",
                    SortColumn = sortNorm,
                    SortDescending = descending
                };

                // تمرير حالة الفلاتر للواجهة
                ViewBag.Search = cleanedSearch ?? "";
                ViewBag.SearchBy = searchBy ?? "all";
                ViewBag.Sort = sortNorm;
                ViewBag.Dir = dirNorm;
                ViewBag.Page = page;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalCount = totalCount;
                ViewBag.SIId = siId;

                // إجماليات الأعمدة (بعد الفلاتر)
                ViewBag.TotalQty = allItems.Sum(line => line.Qty);
                ViewBag.TotalDiscountValue = allItems.Sum(line => line.DiscountValue);
                ViewBag.TotalAfterDiscount = allItems.Sum(line => line.LineTotalAfterDiscount);
                ViewBag.TotalNet = allItems.Sum(line => line.LineNetTotal);

                // (7) خيارات البحث (القائمة المنسدلة في البارشال _IndexFilters)
                ViewBag.SearchOptions = new List<SelectListItem>
                {
                    new("الكل",          "all")  { Selected = (ViewBag.SearchBy == "all")  },
                    new("رقم الفاتورة",  "id")   { Selected = (ViewBag.SearchBy == "id")   },
                    new("رقم الفاتورة",  "si")   { Selected = (ViewBag.SearchBy == "si")   },
                    new("رقم السطر",     "line") { Selected = (ViewBag.SearchBy == "line") },
                    new("كود الصنف",     "prod") { Selected = (ViewBag.SearchBy == "prod") },
                    new("اسم الصنف",     "prodname") { Selected = (ViewBag.SearchBy == "prodname") },
                    new("اسم العميل",    "customername") { Selected = (ViewBag.SearchBy == "customername") },
                    new("الكمية",        "qty")   { Selected = (ViewBag.SearchBy == "qty") },
                    new("التشغيلة",      "batch"){ Selected = (ViewBag.SearchBy == "batch")},
                    new("الملاحظات",     "notes") { Selected = (ViewBag.SearchBy == "notes") },
                };

                // (8) خيارات الترتيب
                ViewBag.SortOptions = new List<SelectListItem>
                {
                    new("رقم السطر",     "LineNo") { Selected = (ViewBag.Sort == "LineNo") },
                    new("رقم الفاتورة",  "SIId")   { Selected = (ViewBag.Sort == "SIId")   },
                    new("كود الصنف",     "ProdId") { Selected = (ViewBag.Sort == "ProdId") },
                    new("اسم الصنف",     "ProdName") { Selected = (ViewBag.Sort == "ProdName") },
                    new("اسم العميل",    "CustomerName") { Selected = (ViewBag.Sort == "CustomerName") },
                    new("الكمية",        "Qty")    { Selected = (ViewBag.Sort == "Qty")    },
                    new("سعر الجمهور",   "Price")  { Selected = (ViewBag.Sort == "Price")  },
                    new("سعر الوحدة",    "Unit")   { Selected = (ViewBag.Sort == "Unit")   },
                    new("خصم 1%",        "Disc1")  { Selected = (ViewBag.Sort == "Disc1")  },
                    new("خصم 2%",        "Disc2")  { Selected = (ViewBag.Sort == "Disc2")  },
                    new("خصم 3%",        "Disc3")  { Selected = (ViewBag.Sort == "Disc3")  },
                    new("قيمة الخصم",    "DiscVal") { Selected = (ViewBag.Sort == "DiscVal") },
                    new("إجمالي السطر",  "Total")  { Selected = (ViewBag.Sort == "Total")  },
                    new("الصافي",        "Net")    { Selected = (ViewBag.Sort == "Net")    },
                    new("التشغيلة",      "Batch")  { Selected = (ViewBag.Sort == "Batch")  },
                    new("الصلاحية",      "Expiry") { Selected = (ViewBag.Sort == "Expiry") },
                };

                return View(model);
            }

            // (5) إنشاء نتيجة مقسّمة صفحات بالنظام الموحد (للحقول العادية)
            model = await PagedResult<SalesInvoiceLine>.CreateAsync(
                q,
                page,
                pageSize,
                search,
                descending,
                sort,
                searchBy
            );

            // (6) تمرير حالة الفلاتر للواجهة
            ViewBag.Search = cleanedSearch ?? "";
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.Sort = sort ?? "LineNo";
            ViewBag.Dir = dirNorm;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalCount = model.TotalCount;

            // نمرّر رقم الفاتورة المُرشَّح (لو موجود) لعرضه في الفيو أو للروابط
            ViewBag.SIId = siId;

            // إجماليات الأعمدة (بعد الفلاتر)
            ViewBag.TotalQty = totalQty;
            ViewBag.TotalDiscountValue = totalDiscountValue;
            ViewBag.TotalAfterDiscount = totalAfterDiscount;
            ViewBag.TotalNet = totalNet;

            // (7) خيارات البحث (القائمة المنسدلة في البارشال _IndexFilters)
            ViewBag.SearchOptions = new List<SelectListItem>
            {
                new("الكل",          "all")  { Selected = (ViewBag.SearchBy == "all")  },
                new("رقم الفاتورة",  "id")   { Selected = (ViewBag.SearchBy == "id")   },
                new("رقم الفاتورة",  "si")   { Selected = (ViewBag.SearchBy == "si")   },
                new("رقم السطر",     "line") { Selected = (ViewBag.SearchBy == "line") },
                new("كود الصنف",     "prod") { Selected = (ViewBag.SearchBy == "prod") },
                new("اسم الصنف",     "prodname") { Selected = (ViewBag.SearchBy == "prodname") },
                new("اسم العميل",    "customername") { Selected = (ViewBag.SearchBy == "customername") },
                new("الكمية",        "qty")   { Selected = (ViewBag.SearchBy == "qty") },
                new("التشغيلة",      "batch"){ Selected = (ViewBag.SearchBy == "batch")},
                new("الملاحظات",     "notes") { Selected = (ViewBag.SearchBy == "notes") },
            };

            // (8) خيارات الترتيب
            ViewBag.SortOptions = new List<SelectListItem>
            {
                new("رقم السطر",     "LineNo") { Selected = (ViewBag.Sort == "LineNo") },
                new("رقم الفاتورة",  "SIId")   { Selected = (ViewBag.Sort == "SIId")   },
                new("كود الصنف",     "ProdId") { Selected = (ViewBag.Sort == "ProdId") },
                new("اسم الصنف",     "ProdName") { Selected = (ViewBag.Sort == "ProdName") },
                new("اسم العميل",    "CustomerName") { Selected = (ViewBag.Sort == "CustomerName") },
                new("الكمية",        "Qty")    { Selected = (ViewBag.Sort == "Qty")    },
                new("سعر الجمهور",   "Price")  { Selected = (ViewBag.Sort == "Price")  },
                new("سعر الوحدة",    "Unit")   { Selected = (ViewBag.Sort == "Unit")   },
                new("خصم 1%",        "Disc1")  { Selected = (ViewBag.Sort == "Disc1")  },
                new("خصم 2%",        "Disc2")  { Selected = (ViewBag.Sort == "Disc2")  },
                new("خصم 3%",        "Disc3")  { Selected = (ViewBag.Sort == "Disc3")  },
                new("قيمة الخصم",    "DiscVal") { Selected = (ViewBag.Sort == "DiscVal") },
                new("إجمالي السطر",  "Total")  { Selected = (ViewBag.Sort == "Total")  },
                new("الصافي",        "Net")    { Selected = (ViewBag.Sort == "Net")    },
                new("التشغيلة",      "Batch")  { Selected = (ViewBag.Sort == "Batch")  },
                new("الصلاحية",      "Expiry") { Selected = (ViewBag.Sort == "Expiry") },
            };

            return View(model);
        }

        // =========================================================
        // Details — عرض سطر واحد (بالمفتاح المركّب: SIId + LineNo)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Details(int siId, int lineNo)
        {
            if (siId <= 0) return BadRequest();

            var line = await _context.SalesInvoiceLines
                                     .Include(x => x.Product)
                                     .Include(x => x.SalesInvoice)
                                         .ThenInclude(si => si.Customer)
                                     .AsNoTracking()
                                     .FirstOrDefaultAsync(x => x.SIId == siId &&
                                                               x.LineNo == lineNo);

            if (line == null)
                return NotFound();

            return View(line);
        }

        // =========================================================
        // Delete — حذف سطر واحد بالمفتاح المركب (SIId + LineNo)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int siId, int lineNo)
        {
            var line = await _context.SalesInvoiceLines
                                     .FirstOrDefaultAsync(x => x.SIId == siId &&
                                                               x.LineNo == lineNo);

            if (line == null)
            {
                TempData["Error"] = "السطر المطلوب غير موجود.";
                return RedirectToAction(nameof(Index), new { siId });
            }

            _context.SalesInvoiceLines.Remove(line);
            await _context.SaveChangesAsync();

            // بعد الحذف: إعادة تجميع إجماليات هيدر فاتورة البيع
            await _docTotals.RecalcSalesInvoiceTotalsAsync(siId);

            TempData["Success"] = "تم حذف سطر فاتورة البيع بنجاح.";
            return RedirectToAction(nameof(Index), new { siId });
        }

        // =========================================================
        // BulkDelete — حذف السطور المحددة
        // نستقبل مفتاح مركب على شكل نص "SIId:LineNo"
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string[] ids)
        {
            if (ids == null || ids.Length == 0)
            {
                TempData["Error"] = "لم يتم اختيار أى سطر للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var idList = ids
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();   // متغير: قائمة المفاتيح النصية المختارة

            // نجيب السطور المطابقة لنفس صيغة المفتاح "SIId:LineNo"
            var lines = await _context.SalesInvoiceLines
                .Where(l => idList.Contains(
                    l.SIId.ToString() + ":" + l.LineNo.ToString()))
                .ToListAsync();

            if (lines.Count == 0)
            {
                TempData["Error"] = "لم يتم العثور على السطور المحددة.";
                return RedirectToAction(nameof(Index));
            }

            // حفظ الفواتير المتأثرة لإعادة التجميع بعد الحذف
            var affectedInvoiceIds = lines
                .Select(l => l.SIId)
                .Distinct()
                .ToList();

            _context.SalesInvoiceLines.RemoveRange(lines);
            await _context.SaveChangesAsync();

            // إعادة تجميع إجماليات كل فاتورة متأثرة
            foreach (var id in affectedInvoiceIds)
            {
                await _docTotals.RecalcSalesInvoiceTotalsAsync(id);
            }

            TempData["Success"] = $"تم حذف {lines.Count} من سطور فواتير البيع المحددة.";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // DeleteAll — حذف جميع سطور فواتير البيع
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _context.SalesInvoiceLines.ToListAsync();

            if (all.Count == 0)
            {
                TempData["Error"] = "لا توجد سطور فواتير بيع لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            // الفواتير المتأثرة قبل الحذف
            var affectedInvoiceIds = all
                .Select(l => l.SIId)
                .Distinct()
                .ToList();

            _context.SalesInvoiceLines.RemoveRange(all);
            await _context.SaveChangesAsync();

            // إعادة تجميع إجماليات كل فاتورة متأثرة (ستصبح 0 بعد الحذف)
            foreach (var id in affectedInvoiceIds)
            {
                await _docTotals.RecalcSalesInvoiceTotalsAsync(id);
            }

            TempData["Success"] = "تم حذف جميع سطور فواتير البيع.";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // Export — تصدير سطور فاتورة البيع
        //  - لو format = "csv" → ملف CSV
        //  - غير ذلك (excel أو null) → ملف Excel فعلي .xlsx
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Export(
            int? siId,
            string? search,
            string? searchBy = "all",
            string? sort = "LineNo",
            string? dir = "asc",
            int page = 1,          // مش هنستخدمهم فى التصدير، بس بنحافظ على الـ Signature لو اتنادى بيهم
            int pageSize = 50,
            string format = "excel")   // excel | csv
        {
            var q = BuildQuery(siId, search, searchBy, sort, dir);

            // نرتّب نفس ترتيب الشاشة (LineNo ثم SIId مثلاً)
            var list = await q
                .OrderBy(l => l.SIId)
                .ThenBy(l => l.LineNo)
                .ToListAsync();

            format = (format ?? "excel").ToLowerInvariant();

            if (format == "csv")
            {
                // ===== تصدير CSV يفتح فى Excel =====
                var sb = new StringBuilder();

                // عناوين الأعمدة
                sb.AppendLine("SIId,LineNo,ProdId,Qty,PriceRetail,Disc1Percent,Disc2Percent,Disc3Percent,DiscountValue,UnitSalePrice,LineTotalAfterDiscount,TaxPercent,TaxValue,LineNetTotal,BatchNo,Expiry");

                // كل سطر فى CSV
                foreach (var l in list)
                {
                    string line = string.Join(",",
                        l.SIId,
                        l.LineNo,
                        l.ProdId,
                        l.Qty,
                        l.PriceRetail.ToString("0.00", CultureInfo.InvariantCulture),
                        l.Disc1Percent.ToString("0.##", CultureInfo.InvariantCulture),
                        l.Disc2Percent.ToString("0.##", CultureInfo.InvariantCulture),
                        l.Disc3Percent.ToString("0.##", CultureInfo.InvariantCulture),
                        l.DiscountValue.ToString("0.00", CultureInfo.InvariantCulture),
                        l.UnitSalePrice.ToString("0.00", CultureInfo.InvariantCulture),
                        l.LineTotalAfterDiscount.ToString("0.00", CultureInfo.InvariantCulture),
                        l.TaxPercent.ToString("0.##", CultureInfo.InvariantCulture),
                        l.TaxValue.ToString("0.00", CultureInfo.InvariantCulture),
                        l.LineNetTotal.ToString("0.00", CultureInfo.InvariantCulture),
                        (l.BatchNo ?? "").Replace(",", " "),
                        l.Expiry.HasValue ? l.Expiry.Value.ToString("yyyy-MM-dd") : ""
                    );

                    sb.AppendLine(line);
                }

                var bytesCsv = Encoding.UTF8.GetBytes(sb.ToString());
                var fileNameCsv = $"SalesInvoiceLines_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                const string contentTypeCsv = "text/csv";

                return File(bytesCsv, contentTypeCsv, fileNameCsv);
            }
            else
            {
                // ===== تصدير Excel فعلي (.xlsx) باستخدام ClosedXML =====
                using var workbook = new XLWorkbook();  // متغير: مصنف Excel
                var worksheet = workbook.Worksheets.Add("SalesInvoiceLines");   // متغير: ورقة العمل

                int row = 1;   // متغير: رقم الصف الحالي فى الشيت

                // عناوين الأعمدة
                worksheet.Cell(row, 1).Value = "SIId";
                worksheet.Cell(row, 2).Value = "LineNo";
                worksheet.Cell(row, 3).Value = "ProdId";
                worksheet.Cell(row, 4).Value = "Qty";
                worksheet.Cell(row, 5).Value = "PriceRetail";
                worksheet.Cell(row, 6).Value = "Disc1Percent";
                worksheet.Cell(row, 7).Value = "Disc2Percent";
                worksheet.Cell(row, 8).Value = "Disc3Percent";
                worksheet.Cell(row, 9).Value = "DiscountValue";
                worksheet.Cell(row, 10).Value = "UnitSalePrice";
                worksheet.Cell(row, 11).Value = "LineTotalAfterDiscount";
                worksheet.Cell(row, 12).Value = "TaxPercent";
                worksheet.Cell(row, 13).Value = "TaxValue";
                worksheet.Cell(row, 14).Value = "LineNetTotal";
                worksheet.Cell(row, 15).Value = "BatchNo";
                worksheet.Cell(row, 16).Value = "Expiry";

                var headerRange = worksheet.Range(row, 1, row, 16);
                headerRange.Style.Font.Bold = true;

                // إضافة البيانات
                foreach (var l in list)
                {
                    row++;

                    worksheet.Cell(row, 1).Value = l.SIId;
                    worksheet.Cell(row, 2).Value = l.LineNo;
                    worksheet.Cell(row, 3).Value = l.ProdId;
                    worksheet.Cell(row, 4).Value = l.Qty;
                    worksheet.Cell(row, 5).Value = l.PriceRetail;
                    worksheet.Cell(row, 6).Value = l.Disc1Percent;
                    worksheet.Cell(row, 7).Value = l.Disc2Percent;
                    worksheet.Cell(row, 8).Value = l.Disc3Percent;
                    worksheet.Cell(row, 9).Value = l.DiscountValue;
                    worksheet.Cell(row, 10).Value = l.UnitSalePrice;
                    worksheet.Cell(row, 11).Value = l.LineTotalAfterDiscount;
                    worksheet.Cell(row, 12).Value = l.TaxPercent;
                    worksheet.Cell(row, 13).Value = l.TaxValue;
                    worksheet.Cell(row, 14).Value = l.LineNetTotal;
                    worksheet.Cell(row, 15).Value = l.BatchNo ?? "";
                    worksheet.Cell(row, 16).Value = l.Expiry?.ToString("yyyy-MM-dd") ?? "";
                }

                // ضبط عرض الأعمدة تلقائياً
                worksheet.Columns().AdjustToContents();

                using var stream = new System.IO.MemoryStream();  // متغير: ستريم في الذاكرة لحفظ الملف
                workbook.SaveAs(stream);
                var bytesXlsx = stream.ToArray();

                var fileNameXlsx = $"SalesInvoiceLines_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                const string contentTypeXlsx =
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

                return File(bytesXlsx, contentTypeXlsx, fileNameXlsx);
            }
        }
    }
}
