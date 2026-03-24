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
    [RequirePermission("SalesInvoiceLines.Index")]
    public class SalesInvoiceLinesController : Controller
    {
        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        private readonly AppDbContext _context;            // متغير: كائن الاتصال بقاعدة البيانات
        private readonly DocumentTotalsService _docTotals; // متغير: خدمة إجماليات المستندات (لإعادة تجميع هيدر فاتورة البيع بعد الحذف)

        private static int? TryParseNullableInt(string? s) =>
            int.TryParse((s ?? "").Trim(), out var v) ? v : null;

        private static List<string> SplitFilterVals(string? s) =>
            string.IsNullOrWhiteSpace(s)
                ? new List<string>()
                : s!.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => x.Length > 0)
                    .ToList();

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
                "siid", "line", "lineno", "prodid", "prodname", "customername", "region", "createdby",
                "qty", "price", "unit", "total", "net", "disc1", "disc2", "disc3", "discval",
                "batch", "expiry", "رقم الفاتورة", "رقم السطر", "كود الصنف", "اسم الصنف",
                "اسم العميل", "المنطقة", "الكاتب", "الكمية", "سعر الجمهور", "التشغيلة", "الصلاحية",
                "الخصم", "قيمة الخصم"
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
        // فلاتر نطاق الفاتورة + التاريخ (الصلاحية) + فلاتر أعمدة الجدول (نمط Excel)
        // =========================================================
        private IQueryable<SalesInvoiceLine> ApplySilListFilters(
            IQueryable<SalesInvoiceLine> q,
            int? fromCode,
            int? toCode,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            string? filterCol_siid,
            string? filterCol_lineno,
            string? filterCol_prodid,
            string? filterCol_prodname,
            string? filterCol_customername,
            string? filterCol_qty,
            string? filterCol_price,
            string? filterCol_disc1,
            string? filterCol_disc2,
            string? filterCol_disc3,
            string? filterCol_discval,
            string? filterCol_total,
            string? filterCol_net,
            string? filterCol_batch,
            string? filterCol_expiry,
            string? filterCol_region = null,
            string? filterCol_createdby = null)
        {
            if (fromCode.HasValue)
                q = q.Where(x => x.SIId >= fromCode.Value);
            if (toCode.HasValue)
                q = q.Where(x => x.SIId <= toCode.Value);

            if (useDateRange && fromDate.HasValue)
                q = q.Where(x => x.Expiry.HasValue && x.Expiry.Value >= fromDate.Value);
            if (useDateRange && toDate.HasValue)
                q = q.Where(x => x.Expiry.HasValue && x.Expiry.Value <= toDate.Value);

            var inv = CultureInfo.InvariantCulture;

            if (!string.IsNullOrWhiteSpace(filterCol_siid))
            {
                var ids = SplitFilterVals(filterCol_siid)
                    .Select(s => int.TryParse(s, System.Globalization.NumberStyles.Integer, inv, out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (ids.Count > 0)
                    q = q.Where(x => ids.Contains(x.SIId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_lineno))
            {
                var lines = SplitFilterVals(filterCol_lineno)
                    .Select(s => int.TryParse(s, System.Globalization.NumberStyles.Integer, inv, out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (lines.Count > 0)
                    q = q.Where(x => lines.Contains(x.LineNo));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_prodid))
            {
                var pids = SplitFilterVals(filterCol_prodid)
                    .Select(s => int.TryParse(s, System.Globalization.NumberStyles.Integer, inv, out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (pids.Count > 0)
                    q = q.Where(x => pids.Contains(x.ProdId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_prodname))
            {
                var vals = SplitFilterVals(filterCol_prodname);
                if (vals.Count > 0)
                    q = q.Where(x => x.Product != null && vals.Contains(x.Product.ProdName ?? ""));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_customername))
            {
                var vals = SplitFilterVals(filterCol_customername);
                if (vals.Count > 0)
                    q = q.Where(x =>
                        x.SalesInvoice != null &&
                        x.SalesInvoice.Customer != null &&
                        vals.Contains(x.SalesInvoice.Customer.CustomerName ?? ""));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_qty))
            {
                var qtys = SplitFilterVals(filterCol_qty)
                    .Select(s => int.TryParse(s, System.Globalization.NumberStyles.Integer, inv, out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (qtys.Count > 0)
                    q = q.Where(x => qtys.Contains(x.Qty));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_price))
            {
                var prices = SplitFilterVals(filterCol_price)
                    .Select(s => decimal.TryParse(s, inv, out var v) ? v : (decimal?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (prices.Count > 0)
                    q = q.Where(x => prices.Contains(x.PriceRetail));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_disc1))
            {
                var ds = SplitFilterVals(filterCol_disc1)
                    .Select(s => decimal.TryParse(s, inv, out var v) ? v : (decimal?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (ds.Count > 0)
                    q = q.Where(x => ds.Contains(x.Disc1Percent));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_disc2))
            {
                var ds = SplitFilterVals(filterCol_disc2)
                    .Select(s => decimal.TryParse(s, inv, out var v) ? v : (decimal?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (ds.Count > 0)
                    q = q.Where(x => ds.Contains(x.Disc2Percent));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_disc3))
            {
                var ds = SplitFilterVals(filterCol_disc3)
                    .Select(s => decimal.TryParse(s, inv, out var v) ? v : (decimal?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (ds.Count > 0)
                    q = q.Where(x => ds.Contains(x.Disc3Percent));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_discval))
            {
                var ds = SplitFilterVals(filterCol_discval)
                    .Select(s => decimal.TryParse(s, inv, out var v) ? v : (decimal?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (ds.Count > 0)
                    q = q.Where(x => ds.Contains(x.DiscountValue));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_total))
            {
                var ds = SplitFilterVals(filterCol_total)
                    .Select(s => decimal.TryParse(s, inv, out var v) ? v : (decimal?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (ds.Count > 0)
                    q = q.Where(x => ds.Contains(x.LineTotalAfterDiscount));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_net))
            {
                var ds = SplitFilterVals(filterCol_net)
                    .Select(s => decimal.TryParse(s, inv, out var v) ? v : (decimal?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (ds.Count > 0)
                    q = q.Where(x => ds.Contains(x.LineNetTotal));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_batch))
            {
                var vals = SplitFilterVals(filterCol_batch);
                if (vals.Count > 0)
                    q = q.Where(x => x.BatchNo != null && vals.Contains(x.BatchNo));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_expiry))
            {
                var vals = SplitFilterVals(filterCol_expiry);
                if (vals.Count > 0)
                    q = q.Where(x =>
                        x.Expiry.HasValue &&
                        vals.Contains(x.Expiry.Value.ToString("yyyy-MM", inv)));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_region))
            {
                var vals = SplitFilterVals(filterCol_region);
                if (vals.Count > 0)
                    q = q.Where(x =>
                        vals.Contains(
                            x.SalesInvoice != null && x.SalesInvoice.Customer != null
                                ? (!string.IsNullOrWhiteSpace(x.SalesInvoice.Customer.RegionName)
                                    ? x.SalesInvoice.Customer.RegionName!
                                    : (x.SalesInvoice.Customer.Area != null && x.SalesInvoice.Customer.Area.AreaName != null
                                        ? x.SalesInvoice.Customer.Area.AreaName
                                        : ""))
                                : ""));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_createdby))
            {
                var vals = SplitFilterVals(filterCol_createdby);
                if (vals.Count > 0)
                    q = q.Where(x =>
                        x.SalesInvoice != null &&
                        x.SalesInvoice.CreatedBy != null &&
                        vals.Contains(x.SalesInvoice.CreatedBy));
            }

            return q;
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
            string? dir,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_siid = null,
            string? filterCol_lineno = null,
            string? filterCol_prodid = null,
            string? filterCol_prodname = null,
            string? filterCol_customername = null,
            string? filterCol_qty = null,
            string? filterCol_price = null,
            string? filterCol_disc1 = null,
            string? filterCol_disc2 = null,
            string? filterCol_disc3 = null,
            string? filterCol_discval = null,
            string? filterCol_total = null,
            string? filterCol_net = null,
            string? filterCol_batch = null,
            string? filterCol_expiry = null,
            string? filterCol_region = null,
            string? filterCol_createdby = null)
        {
            // (1) الاستعلام الأساسي مع تضمين العلاقات (Product و SalesInvoice.Customer)
            IQueryable<SalesInvoiceLine> q =
                _context.SalesInvoiceLines
                    .Include(x => x.Product)
                    .Include(x => x.SalesInvoice)
                        .ThenInclude(si => si!.Customer)
                            .ThenInclude(c => c!.Area)
                    .AsNoTracking();

            // (2) فلترة اختيارية برقم الفاتورة
            if (siId.HasValue)
                q = q.Where(x => x.SIId == siId.Value);

            q = ApplySilListFilters(
                q, fromCode, toCode, useDateRange, fromDate, toDate,
                filterCol_siid, filterCol_lineno, filterCol_prodid, filterCol_prodname, filterCol_customername,
                filterCol_qty, filterCol_price, filterCol_disc1, filterCol_disc2, filterCol_disc3, filterCol_discval,
                filterCol_total, filterCol_net, filterCol_batch, filterCol_expiry,
                filterCol_region, filterCol_createdby);

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
                    ["Region"] = x =>
                        x.SalesInvoice != null && x.SalesInvoice.Customer != null
                            ? (!string.IsNullOrWhiteSpace(x.SalesInvoice.Customer.RegionName)
                                ? x.SalesInvoice.Customer.RegionName!
                                : (x.SalesInvoice.Customer.Area != null
                                    ? x.SalesInvoice.Customer.Area.AreaName ?? ""
                                    : ""))
                            : "",
                    ["CreatedBy"] = x => x.SalesInvoice != null ? (x.SalesInvoice.CreatedBy ?? "") : "",
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
        // Index — عرض عام + بحث/فلتر أعمدة/ترقيم (نمط قائمة فواتير المبيعات)
        // =========================================================
        public async Task<IActionResult> Index(
            int? siId,
            string? search,
            string? searchBy = "all",
            string? sort = "LineNo",
            string? dir = "asc",
            int page = 1,
            int pageSize = 10,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? dateField = "Expiry",
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_siid = null,
            string? filterCol_lineno = null,
            string? filterCol_prodid = null,
            string? filterCol_prodname = null,
            string? filterCol_customername = null,
            string? filterCol_qty = null,
            string? filterCol_price = null,
            string? filterCol_disc1 = null,
            string? filterCol_disc2 = null,
            string? filterCol_disc3 = null,
            string? filterCol_discval = null,
            string? filterCol_total = null,
            string? filterCol_net = null,
            string? filterCol_batch = null,
            string? filterCol_expiry = null,
            string? filterCol_region = null,
            string? filterCol_createdby = null)
        {
            var pageSizeQuery = Request.Query["pageSize"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageSizeQuery) && int.TryParse(pageSizeQuery, out var psVal))
                pageSize = psVal;

            if (page < 1) page = 1;
            if (pageSize < 0) pageSize = 10;
            if (pageSize > 0 && pageSize != 10 && pageSize != 25 && pageSize != 50 && pageSize != 100 && pageSize != 200)
                pageSize = 10;

            int? codeFromQ = Request.Query.ContainsKey("codeFrom")
                ? TryParseNullableInt(Request.Query["codeFrom"].ToString())
                : null;
            int? codeToQ = Request.Query.ContainsKey("codeTo")
                ? TryParseNullableInt(Request.Query["codeTo"].ToString())
                : null;
            var finalFromCode = fromCode ?? codeFromQ;
            var finalToCode = toCode ?? codeToQ;

            ViewBag.FromCode = finalFromCode;
            ViewBag.ToCode = finalToCode;
            ViewBag.DateField = dateField;

            var cleanedSearch = CleanSearchFromColumnNames(search);

            var q = BuildQuery(
                siId, search, searchBy, sort, dir,
                useDateRange, fromDate, toDate, finalFromCode, finalToCode,
                filterCol_siid, filterCol_lineno, filterCol_prodid, filterCol_prodname, filterCol_customername,
                filterCol_qty, filterCol_price, filterCol_disc1, filterCol_disc2, filterCol_disc3, filterCol_discval,
                filterCol_total, filterCol_net, filterCol_batch, filterCol_expiry,
                filterCol_region, filterCol_createdby);

            int totalQty = await q.SumAsync(line => (int?)line.Qty) ?? 0;
            decimal totalDiscountValue = await q.SumAsync(line => (decimal?)line.DiscountValue) ?? 0m;
            decimal totalAfterDiscount = await q.SumAsync(line => (decimal?)line.LineTotalAfterDiscount) ?? 0m;
            decimal totalNet = await q.SumAsync(line => (decimal?)line.LineNetTotal) ?? 0m;
            int categoriesCount = await q.Where(l => l.Product != null && l.Product.CategoryId != null)
                .Select(l => l.Product!.CategoryId).Distinct().CountAsync();

            var dirNorm = (dir?.ToLower() == "asc") ? "asc" : "desc";
            bool descending = dirNorm == "desc";
            var sortNorm = (sort ?? "LineNo").Trim();

            int EffectiveSize(int totalCount) =>
                pageSize == 0 ? (totalCount == 0 ? 10 : Math.Min(totalCount, 100_000)) : pageSize;

            void BagColumnFilters()
            {
                ViewBag.FilterCol_Siid = filterCol_siid;
                ViewBag.FilterCol_Lineno = filterCol_lineno;
                ViewBag.FilterCol_Prodid = filterCol_prodid;
                ViewBag.FilterCol_Prodname = filterCol_prodname;
                ViewBag.FilterCol_Customername = filterCol_customername;
                ViewBag.FilterCol_Qty = filterCol_qty;
                ViewBag.FilterCol_Price = filterCol_price;
                ViewBag.FilterCol_Disc1 = filterCol_disc1;
                ViewBag.FilterCol_Disc2 = filterCol_disc2;
                ViewBag.FilterCol_Disc3 = filterCol_disc3;
                ViewBag.FilterCol_Discval = filterCol_discval;
                ViewBag.FilterCol_Total = filterCol_total;
                ViewBag.FilterCol_Net = filterCol_net;
                ViewBag.FilterCol_Batch = filterCol_batch;
                ViewBag.FilterCol_Expiry = filterCol_expiry;
                ViewBag.FilterCol_Region = filterCol_region;
                ViewBag.FilterCol_Createdby = filterCol_createdby;
            }

            void BagSearchSortLists(string sortForList)
            {
                ViewBag.SearchOptions = new List<SelectListItem>
                {
                    new("الكل", "all") { Selected = (ViewBag.SearchBy as string) == "all" },
                    new("رقم الفاتورة", "id") { Selected = (ViewBag.SearchBy as string) == "id" },
                    new("رقم السطر", "line") { Selected = (ViewBag.SearchBy as string) == "line" },
                    new("كود الصنف", "prod") { Selected = (ViewBag.SearchBy as string) == "prod" },
                    new("اسم الصنف", "prodname") { Selected = (ViewBag.SearchBy as string) == "prodname" },
                    new("اسم العميل", "customername") { Selected = (ViewBag.SearchBy as string) == "customername" },
                    new("الكمية", "qty") { Selected = (ViewBag.SearchBy as string) == "qty" },
                    new("التشغيلة", "batch") { Selected = (ViewBag.SearchBy as string) == "batch" },
                    new("الملاحظات", "notes") { Selected = (ViewBag.SearchBy as string) == "notes" },
                };
                ViewBag.SortOptions = new List<SelectListItem>
                {
                    new("رقم السطر", "LineNo") { Selected = sortForList == "LineNo" },
                    new("رقم الفاتورة", "SIId") { Selected = sortForList == "SIId" },
                    new("كود الصنف", "ProdId") { Selected = sortForList == "ProdId" },
                    new("اسم الصنف", "ProdName") { Selected = sortForList == "ProdName" },
                    new("اسم العميل", "CustomerName") { Selected = sortForList == "CustomerName" },
                    new("المنطقة", "Region") { Selected = sortForList == "Region" },
                    new("الكاتب", "CreatedBy") { Selected = sortForList == "CreatedBy" },
                    new("الكمية", "Qty") { Selected = sortForList == "Qty" },
                    new("سعر الجمهور", "Price") { Selected = sortForList == "Price" },
                    new("سعر الوحدة", "Unit") { Selected = sortForList == "Unit" },
                    new("الخصم", "Disc1") { Selected = sortForList == "Disc1" },
                    new("خصم 2%", "Disc2") { Selected = sortForList == "Disc2" },
                    new("خصم 3%", "Disc3") { Selected = sortForList == "Disc3" },
                    new("قيمة الخصم", "DiscVal") { Selected = sortForList == "DiscVal" },
                    new("إجمالي السطر", "Total") { Selected = sortForList == "Total" },
                    new("الصافي", "Net") { Selected = sortForList == "Net" },
                    new("التشغيلة", "Batch") { Selected = sortForList == "Batch" },
                    new("الصلاحية", "Expiry") { Selected = sortForList == "Expiry" },
                };
            }

            PagedResult<SalesInvoiceLine> model;

            if (string.Equals(sortNorm, "ProdName", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sortNorm, "CustomerName", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sortNorm, "Region", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sortNorm, "CreatedBy", StringComparison.OrdinalIgnoreCase))
            {
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
                else if (string.Equals(sortNorm, "Region", StringComparison.OrdinalIgnoreCase))
                {
                    string RegionKey(SalesInvoiceLine x)
                    {
                        var c = x.SalesInvoice?.Customer;
                        if (c == null) return "";
                        if (!string.IsNullOrWhiteSpace(c.RegionName)) return c.RegionName!;
                        return c.Area?.AreaName ?? "";
                    }

                    allItems = descending
                        ? allItems.OrderByDescending(x => RegionKey(x)).ToList()
                        : allItems.OrderBy(x => RegionKey(x)).ToList();
                }
                else
                {
                    allItems = descending
                        ? allItems.OrderByDescending(x => x.SalesInvoice?.CreatedBy ?? "").ToList()
                        : allItems.OrderBy(x => x.SalesInvoice?.CreatedBy ?? "").ToList();
                }

                var totalCount = allItems.Count;
                var eff = EffectiveSize(totalCount);
                if (pageSize == 0) page = 1;
                var pagedItems = allItems.Skip((page - 1) * eff).Take(eff).ToList();

                model = new PagedResult<SalesInvoiceLine>(pagedItems, page, pageSize, totalCount)
                {
                    Search = cleanedSearch ?? "",
                    SearchBy = searchBy ?? "all",
                    SortColumn = sortNorm,
                    SortDescending = descending,
                    UseDateRange = useDateRange,
                    FromDate = fromDate,
                    ToDate = toDate
                };

                ViewBag.Search = cleanedSearch ?? "";
                ViewBag.SearchBy = searchBy ?? "all";
                ViewBag.Sort = sortNorm;
                ViewBag.Dir = dirNorm;
                ViewBag.Page = page;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalCount = totalCount;
                ViewBag.SIId = siId;
                ViewBag.TotalQty = allItems.Sum(line => line.Qty);
                ViewBag.TotalDiscountValue = allItems.Sum(line => line.DiscountValue);
                ViewBag.TotalAfterDiscount = allItems.Sum(line => line.LineTotalAfterDiscount);
                ViewBag.TotalNet = allItems.Sum(line => line.LineNetTotal);
                ViewBag.CategoriesCount = allItems.Where(l => l.Product?.CategoryId != null)
                    .Select(l => l.Product!.CategoryId!.Value).Distinct().Count();
                BagColumnFilters();
                BagSearchSortLists(sortNorm);
                return View(model);
            }

            var totalCount2 = await q.CountAsync();
            var eff2 = EffectiveSize(totalCount2);
            if (pageSize == 0) page = 1;
            var items = await q.Skip((page - 1) * eff2).Take(eff2).ToListAsync();

            model = new PagedResult<SalesInvoiceLine>(items, page, pageSize, totalCount2)
            {
                Search = cleanedSearch ?? "",
                SearchBy = searchBy ?? "all",
                SortColumn = sort ?? "LineNo",
                SortDescending = descending,
                UseDateRange = useDateRange,
                FromDate = fromDate,
                ToDate = toDate
            };

            ViewBag.Search = cleanedSearch ?? "";
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.Sort = sort ?? "LineNo";
            ViewBag.Dir = dirNorm;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalCount = totalCount2;
            ViewBag.SIId = siId;
            ViewBag.TotalQty = totalQty;
            ViewBag.TotalDiscountValue = totalDiscountValue;
            ViewBag.TotalAfterDiscount = totalAfterDiscount;
            ViewBag.TotalNet = totalNet;
            ViewBag.CategoriesCount = categoriesCount;
            BagColumnFilters();
            BagSearchSortLists(sort ?? "LineNo");
            return View(model);
        }

        // =========================================================
        // GetColumnValues — قيم مميزة لعمود (فلترة الجدول بنمط Excel)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            if (string.IsNullOrWhiteSpace(column))
                return Json(Array.Empty<string>());

            column = column.Trim().ToLowerInvariant();
            search = (search ?? "").Trim();

            var q = _context.SalesInvoiceLines.AsNoTracking()
                .Include(x => x.Product)
                .Include(x => x.SalesInvoice)
                    .ThenInclude(si => si!.Customer)
                        .ThenInclude(c => c!.Area);

            if (column == "siid")
            {
                var idsQuery = q.Select(x => x.SIId.ToString());
                if (!string.IsNullOrEmpty(search))
                    idsQuery = idsQuery.Where(v => v.Contains(search));
                var list = await idsQuery.Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }

            if (column == "lineno")
            {
                var nq = q.Select(x => x.LineNo.ToString());
                if (!string.IsNullOrEmpty(search))
                    nq = nq.Where(v => v.Contains(search));
                return Json(await nq.Distinct().OrderBy(v => v).Take(200).ToListAsync());
            }

            if (column == "prodid")
            {
                var nq = q.Select(x => x.ProdId.ToString());
                if (!string.IsNullOrEmpty(search))
                    nq = nq.Where(v => v.Contains(search));
                return Json(await nq.Distinct().OrderBy(v => v).Take(200).ToListAsync());
            }

            if (column == "prodname")
            {
                var tq = q.Select(x => x.Product != null ? (x.Product.ProdName ?? "") : "");
                if (!string.IsNullOrEmpty(search))
                    tq = tq.Where(v => v.Contains(search));
                return Json(await tq.Where(v => v != "").Distinct().OrderBy(v => v).Take(200).ToListAsync());
            }

            if (column == "customername")
            {
                var tq = q.Select(x =>
                    x.SalesInvoice != null && x.SalesInvoice.Customer != null
                        ? (x.SalesInvoice.Customer.CustomerName ?? "")
                        : "");
                if (!string.IsNullOrEmpty(search))
                    tq = tq.Where(v => v.Contains(search));
                return Json(await tq.Where(v => v != "").Distinct().OrderBy(v => v).Take(200).ToListAsync());
            }

            if (column == "region")
            {
                var tq = q.Select(x =>
                    x.SalesInvoice != null && x.SalesInvoice.Customer != null
                        ? (!string.IsNullOrWhiteSpace(x.SalesInvoice.Customer.RegionName)
                            ? x.SalesInvoice.Customer.RegionName!
                            : (x.SalesInvoice.Customer.Area != null && x.SalesInvoice.Customer.Area.AreaName != null
                                ? x.SalesInvoice.Customer.Area.AreaName
                                : ""))
                        : "");
                if (!string.IsNullOrEmpty(search))
                    tq = tq.Where(v => v.Contains(search));
                return Json(await tq.Where(v => v != "").Distinct().OrderBy(v => v).Take(200).ToListAsync());
            }

            if (column == "createdby")
            {
                var tq = q.Select(x => x.SalesInvoice != null ? (x.SalesInvoice.CreatedBy ?? "") : "");
                if (!string.IsNullOrEmpty(search))
                    tq = tq.Where(v => v.Contains(search));
                return Json(await tq.Where(v => v != "").Distinct().OrderBy(v => v).Take(200).ToListAsync());
            }

            if (column == "qty")
            {
                var nq = q.Select(x => x.Qty.ToString());
                if (!string.IsNullOrEmpty(search))
                    nq = nq.Where(v => v.Contains(search));
                return Json(await nq.Distinct().OrderBy(v => v).Take(200).ToListAsync());
            }

            if (column == "price")
            {
                var raw = await q.Select(x => x.PriceRetail).Distinct().Take(400).ToListAsync();
                var list = raw.Select(x => x.ToString("0.00", CultureInfo.InvariantCulture)).Distinct().OrderBy(v => v).ToList();
                if (!string.IsNullOrEmpty(search))
                    list = list.Where(v => v.Contains(search, StringComparison.OrdinalIgnoreCase)).Take(200).ToList();
                else if (list.Count > 200)
                    list = list.Take(200).ToList();
                return Json(list);
            }

            if (column == "disc1" || column == "disc2" || column == "disc3")
            {
                List<decimal> raw = column == "disc1"
                    ? await q.Select(x => x.Disc1Percent).Distinct().Take(400).ToListAsync()
                    : column == "disc2"
                        ? await q.Select(x => x.Disc2Percent).Distinct().Take(400).ToListAsync()
                        : await q.Select(x => x.Disc3Percent).Distinct().Take(400).ToListAsync();
                var list = raw.Select(x => x.ToString("0.##", CultureInfo.InvariantCulture)).Distinct().OrderBy(v => v).ToList();
                if (!string.IsNullOrEmpty(search))
                    list = list.Where(v => v.Contains(search, StringComparison.OrdinalIgnoreCase)).Take(200).ToList();
                else if (list.Count > 200)
                    list = list.Take(200).ToList();
                return Json(list);
            }

            if (column == "discval")
            {
                var raw = await q.Select(x => x.DiscountValue).Distinct().Take(400).ToListAsync();
                var list = raw.Select(x => x.ToString("0.00", CultureInfo.InvariantCulture)).Distinct().OrderBy(v => v).ToList();
                if (!string.IsNullOrEmpty(search))
                    list = list.Where(v => v.Contains(search, StringComparison.OrdinalIgnoreCase)).Take(200).ToList();
                else if (list.Count > 200)
                    list = list.Take(200).ToList();
                return Json(list);
            }

            if (column == "total")
            {
                var raw = await q.Select(x => x.LineTotalAfterDiscount).Distinct().Take(400).ToListAsync();
                var list = raw.Select(x => x.ToString("0.00", CultureInfo.InvariantCulture)).Distinct().OrderBy(v => v).ToList();
                if (!string.IsNullOrEmpty(search))
                    list = list.Where(v => v.Contains(search, StringComparison.OrdinalIgnoreCase)).Take(200).ToList();
                else if (list.Count > 200)
                    list = list.Take(200).ToList();
                return Json(list);
            }

            if (column == "net")
            {
                var raw = await q.Select(x => x.LineNetTotal).Distinct().Take(400).ToListAsync();
                var list = raw.Select(x => x.ToString("0.00", CultureInfo.InvariantCulture)).Distinct().OrderBy(v => v).ToList();
                if (!string.IsNullOrEmpty(search))
                    list = list.Where(v => v.Contains(search, StringComparison.OrdinalIgnoreCase)).Take(200).ToList();
                else if (list.Count > 200)
                    list = list.Take(200).ToList();
                return Json(list);
            }

            if (column == "batch")
            {
                var tq = q.Select(x => x.BatchNo ?? "");
                if (!string.IsNullOrEmpty(search))
                    tq = tq.Where(v => v.Contains(search));
                return Json(await tq.Where(v => v != "").Distinct().OrderBy(v => v).Take(200).ToListAsync());
            }

            if (column == "expiry")
            {
                var raw = await q.Where(x => x.Expiry.HasValue).Select(x => x.Expiry!.Value).Distinct().Take(400).ToListAsync();
                var list = raw.Select(d => d.ToString("yyyy-MM", CultureInfo.InvariantCulture)).Distinct().OrderBy(v => v).ToList();
                if (!string.IsNullOrEmpty(search))
                    list = list.Where(v => v.Contains(search, StringComparison.OrdinalIgnoreCase)).Take(200).ToList();
                else if (list.Count > 200)
                    list = list.Take(200).ToList();
                return Json(list);
            }

            return Json(Array.Empty<string>());
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
            int page = 1,
            int pageSize = 50,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_siid = null,
            string? filterCol_lineno = null,
            string? filterCol_prodid = null,
            string? filterCol_prodname = null,
            string? filterCol_customername = null,
            string? filterCol_qty = null,
            string? filterCol_price = null,
            string? filterCol_disc1 = null,
            string? filterCol_disc2 = null,
            string? filterCol_disc3 = null,
            string? filterCol_discval = null,
            string? filterCol_total = null,
            string? filterCol_net = null,
            string? filterCol_batch = null,
            string? filterCol_expiry = null,
            string? filterCol_region = null,
            string? filterCol_createdby = null,
            string format = "excel")
        {
            int? codeFromQ = Request.Query.ContainsKey("codeFrom")
                ? TryParseNullableInt(Request.Query["codeFrom"].ToString())
                : null;
            int? codeToQ = Request.Query.ContainsKey("codeTo")
                ? TryParseNullableInt(Request.Query["codeTo"].ToString())
                : null;
            var finalFromCode = fromCode ?? codeFromQ;
            var finalToCode = toCode ?? codeToQ;

            var q = BuildQuery(
                siId, search, searchBy, sort, dir,
                useDateRange, fromDate, toDate, finalFromCode, finalToCode,
                filterCol_siid, filterCol_lineno, filterCol_prodid, filterCol_prodname, filterCol_customername,
                filterCol_qty, filterCol_price, filterCol_disc1, filterCol_disc2, filterCol_disc3, filterCol_discval,
                filterCol_total, filterCol_net, filterCol_batch, filterCol_expiry,
                filterCol_region, filterCol_createdby);

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
