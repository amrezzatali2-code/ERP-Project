using System;
using System.Collections.Generic;                    // القوائم List / Dictionary
using System.Globalization;                          // CultureInfo لتنسيق الأرقام فى التصدير
using System.Linq;                                   // أوامر LINQ مثل Where / OrderBy
using System.Linq.Expressions;                       // Expression<Func<...>>
using System.Text;                                   // StringBuilder + Encoding
using System.Threading.Tasks;                        // async / await
using ClosedXML.Excel;                               // علشان نصدر Excel فعلي
using ERP.Data;                                      // AppDbContext
using ERP.Filters;
using ERP.Infrastructure;                            // ApplySearchSort + PagedResult
using ERP.Models;                                    // SalesReturnLine , SalesReturn
using ERP.Security;
using ERP.Services;                                  // DocumentTotalsService لإعادة تجميع الهيدر
using Microsoft.AspNetCore.Mvc;                      // Controller / IActionResult
using Microsoft.EntityFrameworkCore;                 // Include / AsNoTracking

namespace ERP.Controllers
{
    /// <summary>
    /// شاشة "أصناف مرتجعات البيع" بنظام القوائم الموحد:
    /// - عرض سطور المرتجع مع بحث وترتيب وتقسيم صفحات.
    /// - فلترة حسب رقم المرتجع أو رقم السطر أو التاريخ.
    /// - حذف سطر/عدة أسطر بشرط أن يكون الهيدر في حالة Draft فقط.
    /// - تصدير CSV أو Excel.
    /// </summary>
    [RequirePermission("SalesReturnLines.Index")]
    public class SalesReturnLinesController : Controller
    {
        // متغير: سياق قاعدة البيانات للتعامل مع الجداول
        private readonly AppDbContext _context;

        // متغير: خدمة إعادة تجميع إجماليات الهيدر (SalesReturn)
        private readonly DocumentTotalsService _docTotals;

        // فاصل لقيم فلاتر الأعمدة (نفسه المستخدم فى القوائم الأخرى)
        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        public SalesReturnLinesController(AppDbContext context,
                                          DocumentTotalsService docTotals)
        {
            _context = context;        // كائن الاتصال بقاعدة البيانات
            _docTotals = docTotals;    // سيرفيس إعادة تجميع إجماليات مرتجع البيع
        }

        // ---------------------------------------------------------
        // دالة داخلية: بناء الاستعلام مع كل الفلاتر + البحث + الترتيب
        // نستخدمها فى Index و Export حتى لا نكرر الكود.
        // ---------------------------------------------------------
        private IQueryable<SalesReturnLine> BuildQuery(
            int? srId,
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
            // 1) الاستعلام الأساسي: سطور المرتجع + الهيدر (لأجل الحالة والتاريخ)
            IQueryable<SalesReturnLine> q = _context.SalesReturnLines
                .Include(l => l.SalesReturn)           // نحتاج حالة المرتجع / التاريخ
                .AsNoTracking();

            // 2) فلترة برقم مرتجع معيّن (لو جاي من شاشة الهيدر)
            if (srId.HasValue)
            {
                q = q.Where(l => l.SRId == srId.Value);
            }

            // 3) فلتر من رقم سطر / إلى رقم سطر
            if (fromCode.HasValue)
            {
                q = q.Where(l => l.LineNo >= fromCode.Value);
            }
            if (toCode.HasValue)
            {
                q = q.Where(l => l.LineNo <= toCode.Value);
            }

            // 4) فلتر التاريخ/الوقت على مستوى الهيدر (SRDate)
            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                DateTime from = fromDate.Value;
                DateTime to = toDate.Value;

                q = q.Where(l =>
                    l.SalesReturn != null &&
                    l.SalesReturn.SRDate >= from &&
                    l.SalesReturn.SRDate <= to);
            }

            // 5) خرائط الحقول للبحث والفرز (نفس فكرة أوامر البيع)

            // الحقول النصية
            var stringFields =
                new Dictionary<string, Expression<Func<SalesReturnLine, string?>>>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["batch"] = x => x.BatchNo ?? "",    // رقم التشغيلة
                    ["expiry"] = x => x.Expiry.HasValue
                                     ? x.Expiry.Value.ToString("yyyy-MM-dd")
                                     : ""
                };

            // الحقول الرقمية
            var intFields =
                new Dictionary<string, Expression<Func<SalesReturnLine, int>>>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["srid"] = x => x.SRId,    // رقم المرتجع
                    ["lineno"] = x => x.LineNo,  // رقم السطر
                    ["prod"] = x => x.ProdId   // كود الصنف
                };

            // مفاتيح الترتيب المسموحة
            var orderFields =
                new Dictionary<string, Expression<Func<SalesReturnLine, object>>>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["SRId"] = x => x.SRId,
                    ["SalesInvoiceId"] = x => x.SalesInvoiceId ?? 0,
                    ["LineNo"] = x => x.LineNo,
                    ["ProdId"] = x => x.ProdId,
                    ["Qty"] = x => x.Qty,
                    ["PriceRetail"] = x => x.PriceRetail,
                    ["UnitSalePrice"] = x => x.UnitSalePrice,
                    ["LineNetTotal"] = x => x.LineNetTotal,
                    ["BatchNo"] = x => x.BatchNo ?? "",
                    ["Expiry"] = x => x.Expiry ?? DateTime.MinValue,
                    ["Status"] = x => x.SalesReturn != null ? x.SalesReturn.Status ?? "" : ""
                };

            // 6) تطبيق دالة البحث/الترتيب الموحدة
            q = q.ApplySearchSort(
                    search: search,
                    searchBy: searchBy,
                    sort: sort,
                    dir: dir,
                    stringFields: stringFields,
                    intFields: intFields,
                    orderFields: orderFields,
                    defaultSearchBy: "all",
                    defaultSortBy: "SRId");

            return q;
        }

        // =========================
        // INDEX: قائمة سطور مرتجعات البيع
        // =========================
        public async Task<IActionResult> Index(
            int? srId,                      // رقم مرتجع معين (لو جاي من شاشة الهيدر)
            string? search,                 // نص البحث
            string? searchBy = "all",       // اسم الحقل الذي نبحث فيه
            string? sort = "SRId",          // عمود الترتيب
            string? dir = "asc",            // اتجاه الترتيب asc/desc
            bool useDateRange = false,      // هل نفعّل فلتر التاريخ؟
            DateTime? fromDate = null,      // من تاريخ (SRDate للهيدر)
            DateTime? toDate = null,        // إلى تاريخ
            int? fromCode = null,           // من رقم سطر
            int? toCode = null,             // إلى رقم سطر
            string? filterCol_srid = null,
            string? filterCol_siid = null,
            string? filterCol_lineno = null,
            string? filterCol_prod = null,
            string? filterCol_qty = null,
            string? filterCol_priceretail = null,
            string? filterCol_unitprice = null,
            string? filterCol_net = null,
            string? filterCol_batch = null,
            string? filterCol_expiry = null,
            string? filterCol_status = null,
            int page = 1,                   // رقم الصفحة
            int pageSize = 50               // حجم الصفحة
        )
        {
            // تجهيز الاستعلام الموحد (بحث + ترتيب + فلاتر أساسية)
            var q = BuildQuery(
                srId,
                search, searchBy,
                sort, dir,
                useDateRange, fromDate, toDate,
                fromCode, toCode);

            // تطبيق فلاتر الأعمدة بنمط Excel
            if (!string.IsNullOrWhiteSpace(filterCol_srid))
            {
                var ids = filterCol_srid.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value)
                    .ToList();
                if (ids.Count > 0)
                    q = q.Where(l => ids.Contains(l.SRId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_siid))
            {
                var ids = filterCol_siid.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value)
                    .ToList();
                if (ids.Count > 0)
                    q = q.Where(l => l.SalesInvoiceId.HasValue && ids.Contains(l.SalesInvoiceId.Value));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_lineno))
            {
                var lines = filterCol_lineno.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value)
                    .ToList();
                if (lines.Count > 0)
                    q = q.Where(l => lines.Contains(l.LineNo));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_prod))
            {
                var prods = filterCol_prod.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value)
                    .ToList();
                if (prods.Count > 0)
                    q = q.Where(l => prods.Contains(l.ProdId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_qty))
            {
                var qtys = filterCol_qty.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value)
                    .ToList();
                if (qtys.Count > 0)
                    q = q.Where(l => qtys.Contains(l.Qty));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_priceretail))
            {
                var vals = filterCol_priceretail.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), out var v) ? v : (decimal?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value)
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(l => vals.Contains(l.PriceRetail));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_unitprice))
            {
                var vals = filterCol_unitprice.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), out var v) ? v : (decimal?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value)
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(l => vals.Contains(l.UnitSalePrice));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_net))
            {
                var vals = filterCol_net.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), out var v) ? v : (decimal?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value)
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(l => vals.Contains(l.LineNetTotal));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_batch))
            {
                var vals = filterCol_batch.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(l => vals.Contains(l.BatchNo ?? ""));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_expiry))
            {
                var dates = filterCol_expiry.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => DateTime.TryParse(v.Trim(), out var d) ? d.Date : (DateTime?)null)
                    .Where(d => d.HasValue).Select(d => d!.Value)
                    .ToList();
                if (dates.Count > 0)
                    q = q.Where(l => l.Expiry.HasValue && dates.Contains(l.Expiry.Value.Date));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_status))
            {
                var vals = filterCol_status.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(l => l.SalesReturn != null && l.SalesReturn.Status != null && vals.Contains(l.SalesReturn.Status));
            }

            // تطبيع اتجاه الترتيب
            var dirNorm = (dir?.ToLower() == "asc") ? "asc" : "desc";
            bool descending = dirNorm == "desc";

            // إنشاء PagedResult بالنظام الموحد
            var model = await PagedResult<SalesReturnLine>.CreateAsync(
                q,
                page,
                pageSize,
                search,
                descending,
                sort,
                searchBy
            );

            // تعبئة قيم إضافية داخل PagedResult (للاستخدام في الواجهة)
            model.UseDateRange = useDateRange;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            // تمرير قيم الفلاتر للواجهة
            ViewBag.FilterSRId = srId;
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.DateField = "SRDate";

            ViewBag.Search = search ?? "";
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.Sort = sort ?? "SRId";
            ViewBag.Dir = dirNorm;

            // فلاتر الأعمدة الحالية
            ViewBag.FilterCol_srid = filterCol_srid ?? string.Empty;
            ViewBag.FilterCol_siid = filterCol_siid ?? string.Empty;
            ViewBag.FilterCol_lineno = filterCol_lineno ?? string.Empty;
            ViewBag.FilterCol_prod = filterCol_prod ?? string.Empty;
            ViewBag.FilterCol_qty = filterCol_qty ?? string.Empty;
            ViewBag.FilterCol_priceretail = filterCol_priceretail ?? string.Empty;
            ViewBag.FilterCol_unitprice = filterCol_unitprice ?? string.Empty;
            ViewBag.FilterCol_net = filterCol_net ?? string.Empty;
            ViewBag.FilterCol_batch = filterCol_batch ?? string.Empty;
            ViewBag.FilterCol_expiry = filterCol_expiry ?? string.Empty;
            ViewBag.FilterCol_status = filterCol_status ?? string.Empty;

            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalCount = model.TotalCount;

            return View(model);
        }

        // =========================
        // DELETE: حذف سطر واحد
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int srId, int lineNo)
        {
            // نجيب السطر مع الهيدر للتأكد من حالة المرتجع
            var line = await _context.SalesReturnLines
                .Include(l => l.SalesReturn)
                .FirstOrDefaultAsync(l => l.SRId == srId && l.LineNo == lineNo);

            if (line == null)
            {
                TempData["error"] = "السطر المطلوب غير موجود.";
                return RedirectToAction(nameof(Index), new { srId });
            }

            // التحقق من أن حالة المرتجع Draft فقط
            var status = line.SalesReturn?.Status ?? "";
            if (!string.Equals(status, "Draft", StringComparison.OrdinalIgnoreCase))
            {
                TempData["error"] = "لا يمكن حذف سطر من مرتجع حالته ليست Draft.";
                return RedirectToAction(nameof(Index), new { srId });
            }

            try
            {
                _context.SalesReturnLines.Remove(line);
                await _context.SaveChangesAsync();

                // بعد الحذف: إعادة تجميع إجماليات هيدر مرتجع البيع
                await _docTotals.RecalcSalesReturnTotalsAsync(srId);

                TempData["ok"] = "تم حذف السطر بنجاح.";
            }
            catch (Exception ex)
            {
                TempData["error"] = "تعذر حذف السطر: " + ex.Message;
            }

            return RedirectToAction(nameof(Index), new { srId });
        }

        // =========================
        // BULK DELETE: حذف عدة أسطر معًا
        // selectedKeys: "SRId:LineNo,SRId:LineNo,..."
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedKeys)
        {
            if (string.IsNullOrWhiteSpace(selectedKeys))
            {
                TempData["error"] = "لم يتم اختيار أي أسطر للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var pairs = selectedKeys
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(k =>
                {
                    var parts = k.Split(':');
                    if (parts.Length != 2) return (srId: (int?)null, lineNo: (int?)null);

                    bool ok1 = int.TryParse(parts[0], out int s);
                    bool ok2 = int.TryParse(parts[1], out int l);
                    return (srId: ok1 ? s : (int?)null, lineNo: ok2 ? l : (int?)null);
                })
                .Where(p => p.srId.HasValue && p.lineNo.HasValue)
                .Select(p => new { SRId = p.srId!.Value, LineNo = p.lineNo!.Value })
                .ToList();

            if (!pairs.Any())
            {
                TempData["error"] = "صيغة مفاتيح الأسطر غير صحيحة.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                // نجيب كل الأسطر المطلوبة مرة واحدة
                var srIds = pairs.Select(p => p.SRId).Distinct().ToList();
                var lineNos = pairs.Select(p => p.LineNo).Distinct().ToList();

                var lines = await _context.SalesReturnLines
                    .Include(l => l.SalesReturn)
                    .Where(l => srIds.Contains(l.SRId) && lineNos.Contains(l.LineNo))
                    .ToListAsync();

                int deleted = 0;
                int blocked = 0;

                // قائمة المرتجعات التى ستحتاج إعادة تجميع
                var affectedSrIds = new HashSet<int>();

                foreach (var line in lines)
                {
                    var status = line.SalesReturn?.Status ?? "";
                    if (string.Equals(status, "Draft", StringComparison.OrdinalIgnoreCase))
                    {
                        _context.SalesReturnLines.Remove(line);
                        deleted++;
                        affectedSrIds.Add(line.SRId);
                    }
                    else
                    {
                        blocked++;
                    }
                }

                if (deleted > 0)
                {
                    await _context.SaveChangesAsync();

                    // إعادة تجميع إجماليات كل هيدر متأثر
                    foreach (var id in affectedSrIds)
                    {
                        await _docTotals.RecalcSalesReturnTotalsAsync(id);
                    }
                }

                if (deleted > 0 && blocked == 0)
                    TempData["ok"] = $"تم حذف {deleted} سطر/أسطر بنجاح.";
                else if (deleted > 0 && blocked > 0)
                    TempData["ok"] = $"تم حذف {deleted} سطر، وتم منع حذف {blocked} سطر لأن حالة المرتجع ليست Draft.";
                else if (blocked > 0)
                    TempData["error"] = "تم منع الحذف لأن جميع الأسطر مرتبطة بمرتجعات ليست Draft.";
                else
                    TempData["error"] = "لم يتم العثور على الأسطر المطلوبة.";

            }
            catch (Exception ex)
            {
                TempData["error"] = "تعذر حذف الأسطر: " + ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================
        // DELETE ALL: حذف جميع الأسطر (للمرتجعات Draft فقط)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _context.SalesReturnLines
                .Include(l => l.SalesReturn)
                .ToListAsync();

            if (all.Count == 0)
            {
                TempData["error"] = "لا توجد سطور مرتجع لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            var deletable = all
                .Where(l => string.Equals(l.SalesReturn?.Status ?? "",
                                          "Draft",
                                          StringComparison.OrdinalIgnoreCase))
                .ToList();

            var blocked = all.Count - deletable.Count;

            if (deletable.Count == 0)
            {
                TempData["error"] = "كل السطور مرتبطة بمرتجعات ليست Draft، لا يمكن حذفها.";
                return RedirectToAction(nameof(Index));
            }

            var affectedSrIds = deletable
                .Select(l => l.SRId)
                .Distinct()
                .ToList();

            _context.SalesReturnLines.RemoveRange(deletable);
            await _context.SaveChangesAsync();

            // إعادة تجميع إجماليات كل هيدر متأثر
            foreach (var id in affectedSrIds)
            {
                await _docTotals.RecalcSalesReturnTotalsAsync(id);
            }

            if (blocked > 0)
                TempData["ok"] = $"تم حذف {deletable.Count} سطر/أسطر، وتم منع حذف {blocked} سطر لأن حالة المرتجع ليست Draft.";
            else
                TempData["ok"] = "تم حذف جميع سطور مرتجعات البيع (الخاصة بمرتجعات Draft).";

            return RedirectToAction(nameof(Index));
        }

        // =========================
        // EXPORT: تصدير نفس البيانات المعروضة (بعد الفلاتر)
        //  - format = "csv"  ⇒ ملف CSV
        //  - غير ذلك         ⇒ ملف Excel حقيقي (.xlsx)
        // =========================
        public async Task<IActionResult> Export(
            int? srId,
            string? search,
            string? searchBy = "all",
            string? sort = "SRId",
            string? dir = "asc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_srid = null,
            string? filterCol_siid = null,
            string? filterCol_lineno = null,
            string? filterCol_prod = null,
            string? filterCol_qty = null,
            string? filterCol_priceretail = null,
            string? filterCol_unitprice = null,
            string? filterCol_net = null,
            string? filterCol_batch = null,
            string? filterCol_expiry = null,
            string? filterCol_status = null,
            string format = "excel"
        )
        {
            var q = BuildQuery(
                srId,
                search, searchBy,
                sort, dir,
                useDateRange, fromDate, toDate,
                fromCode, toCode);

            // نفس منطق فلاتر الأعمدة المستخدم فى Index
            if (!string.IsNullOrWhiteSpace(filterCol_srid))
            {
                var ids = filterCol_srid.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value)
                    .ToList();
                if (ids.Count > 0)
                    q = q.Where(l => ids.Contains(l.SRId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_siid))
            {
                var ids = filterCol_siid.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value)
                    .ToList();
                if (ids.Count > 0)
                    q = q.Where(l => l.SalesInvoiceId.HasValue && ids.Contains(l.SalesInvoiceId.Value));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_lineno))
            {
                var lines = filterCol_lineno.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value)
                    .ToList();
                if (lines.Count > 0)
                    q = q.Where(l => lines.Contains(l.LineNo));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_prod))
            {
                var prods = filterCol_prod.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value)
                    .ToList();
                if (prods.Count > 0)
                    q = q.Where(l => prods.Contains(l.ProdId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_qty))
            {
                var qtys = filterCol_qty.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value)
                    .ToList();
                if (qtys.Count > 0)
                    q = q.Where(l => qtys.Contains(l.Qty));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_priceretail))
            {
                var vals = filterCol_priceretail.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), out var v) ? v : (decimal?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value)
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(l => vals.Contains(l.PriceRetail));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_unitprice))
            {
                var vals = filterCol_unitprice.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), out var v) ? v : (decimal?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value)
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(l => vals.Contains(l.UnitSalePrice));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_net))
            {
                var vals = filterCol_net.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), out var v) ? v : (decimal?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value)
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(l => vals.Contains(l.LineNetTotal));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_batch))
            {
                var vals = filterCol_batch.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(l => vals.Contains(l.BatchNo ?? ""));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_expiry))
            {
                var dates = filterCol_expiry.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => DateTime.TryParse(v.Trim(), out var d) ? d.Date : (DateTime?)null)
                    .Where(d => d.HasValue).Select(d => d!.Value)
                    .ToList();
                if (dates.Count > 0)
                    q = q.Where(l => l.Expiry.HasValue && dates.Contains(l.Expiry.Value.Date));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_status))
            {
                var vals = filterCol_status.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(l => l.SalesReturn != null && l.SalesReturn.Status != null && vals.Contains(l.SalesReturn.Status));
            }

            var data = await q
                .OrderBy(l => l.SRId)
                .ThenBy(l => l.LineNo)
                .ToListAsync();

            format = (format ?? "excel").ToLowerInvariant();

            if (format == "csv")
            {
                // ====== تصدير CSV بسيط ======
                var lines = new List<string>
                {
                    "SRId,SalesInvoiceId,LineNo,ProdId,Qty,PriceRetail,UnitSalePrice,LineNetTotal,BatchNo,Expiry,Status"
                };

                foreach (var l in data)
                {
                    string expiry = l.Expiry.HasValue
                        ? l.Expiry.Value.ToString("yyyy-MM-dd")
                        : "";
                    string status = l.SalesReturn?.Status ?? "";

                    lines.Add(string.Join(",",
                        l.SRId,
                        l.SalesInvoiceId.HasValue ? l.SalesInvoiceId.Value.ToString() : "",
                        l.LineNo,
                        l.ProdId,
                        l.Qty,
                        l.PriceRetail.ToString("0.00", CultureInfo.InvariantCulture),
                        l.UnitSalePrice.ToString("0.00", CultureInfo.InvariantCulture),
                        l.LineNetTotal.ToString("0.00", CultureInfo.InvariantCulture),
                        EscapeCsv(l.BatchNo),
                        EscapeCsv(expiry),
                        EscapeCsv(status)
                    ));
                }

                var bytesCsv = Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, lines));
                var fileNameCsv = $"SalesReturnLines_{DateTime.Now:yyyyMMdd_HHmmss}.csv";

                return File(bytesCsv, "text/csv", fileNameCsv);
            }
            else
            {
                // ====== تصدير Excel حقيقي باستخدام ClosedXML ======
                using var workbook = new XLWorkbook();
                var ws = workbook.Worksheets.Add("SalesReturnLines");

                int row = 1;

                // عناوين الأعمدة
                ws.Cell(row, 1).Value = "SRId";
                ws.Cell(row, 2).Value = "SalesInvoiceId";
                ws.Cell(row, 3).Value = "LineNo";
                ws.Cell(row, 4).Value = "ProdId";
                ws.Cell(row, 5).Value = "Qty";
                ws.Cell(row, 6).Value = "PriceRetail";
                ws.Cell(row, 7).Value = "UnitSalePrice";
                ws.Cell(row, 8).Value = "LineNetTotal";
                ws.Cell(row, 9).Value = "BatchNo";
                ws.Cell(row, 10).Value = "Expiry";
                ws.Cell(row, 11).Value = "Status";

                ws.Range(row, 1, row, 11).Style.Font.Bold = true;

                // البيانات
                foreach (var l in data)
                {
                    row++;

                    ws.Cell(row, 1).Value = l.SRId;
                    ws.Cell(row, 2).Value = l.SalesInvoiceId.HasValue ? l.SalesInvoiceId.Value.ToString() : "";
                    ws.Cell(row, 3).Value = l.LineNo;
                    ws.Cell(row, 4).Value = l.ProdId;
                    ws.Cell(row, 5).Value = l.Qty;
                    ws.Cell(row, 6).Value = l.PriceRetail;
                    ws.Cell(row, 7).Value = l.UnitSalePrice;
                    ws.Cell(row, 8).Value = l.LineNetTotal;
                    ws.Cell(row, 9).Value = l.BatchNo ?? "";
                    ws.Cell(row, 10).Value = l.Expiry?.ToString("yyyy-MM-dd") ?? "";
                    ws.Cell(row, 11).Value = l.SalesReturn?.Status ?? "";
                }

                ws.Columns().AdjustToContents();

                using var stream = new System.IO.MemoryStream();
                workbook.SaveAs(stream);
                var bytesXlsx = stream.ToArray();

                var fileNameXlsx = $"SalesReturnLines_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                const string contentTypeXlsx =
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

                return File(bytesXlsx, contentTypeXlsx, fileNameXlsx);
            }
        }

        // دالة مساعدة للهروب داخل CSV (لو في فاصلة / علامات تنصيص)
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

        // =========================
        // GetColumnValues — قيم مميزة لكل عمود لنمط فلترة الأعمدة (Excel-like)
        // =========================
        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            if (string.IsNullOrWhiteSpace(column))
                return Json(Array.Empty<string>());

            column = column.ToLowerInvariant();
            search = (search ?? string.Empty).Trim();

            IQueryable<SalesReturnLine> q = _context.SalesReturnLines
                .AsNoTracking()
                .Include(l => l.SalesReturn);

            if (column == "srid")
            {
                var query = q.Select(l => l.SRId.ToString());
                if (!string.IsNullOrEmpty(search))
                    query = query.Where(v => v.Contains(search));
                var list = await query.Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }

            if (column == "siid")
            {
                var query = q.Where(l => l.SalesInvoiceId.HasValue)
                             .Select(l => l.SalesInvoiceId!.Value.ToString());
                if (!string.IsNullOrEmpty(search))
                    query = query.Where(v => v.Contains(search));
                var list = await query.Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }

            if (column == "lineno")
            {
                var query = q.Select(l => l.LineNo.ToString());
                if (!string.IsNullOrEmpty(search))
                    query = query.Where(v => v.Contains(search));
                var list = await query.Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }

            if (column == "prod")
            {
                var query = q.Select(l => l.ProdId.ToString());
                if (!string.IsNullOrEmpty(search))
                    query = query.Where(v => v.Contains(search));
                var list = await query.Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }

            if (column == "qty")
            {
                var query = q.Select(l => l.Qty.ToString());
                if (!string.IsNullOrEmpty(search))
                    query = query.Where(v => v.Contains(search));
                var list = await query.Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }

            if (column == "priceretail")
            {
                var query = q.Select(l => l.PriceRetail.ToString("0.00"));
                if (!string.IsNullOrEmpty(search))
                    query = query.Where(v => v.Contains(search));
                var list = await query.Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }

            if (column == "unitprice")
            {
                var query = q.Select(l => l.UnitSalePrice.ToString("0.00"));
                if (!string.IsNullOrEmpty(search))
                    query = query.Where(v => v.Contains(search));
                var list = await query.Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }

            if (column == "net")
            {
                var query = q.Select(l => l.LineNetTotal.ToString("0.00"));
                if (!string.IsNullOrEmpty(search))
                    query = query.Where(v => v.Contains(search));
                var list = await query.Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }

            if (column == "batch")
            {
                var query = q.Select(l => l.BatchNo ?? "");
                if (!string.IsNullOrEmpty(search))
                    query = query.Where(v => v.Contains(search));
                var list = await query.Where(v => v != "").Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }

            if (column == "expiry")
            {
                var query = q.Select(l => l.Expiry.HasValue ? l.Expiry.Value.ToString("yyyy-MM-dd") : "");
                if (!string.IsNullOrEmpty(search))
                    query = query.Where(v => v.Contains(search));
                var list = await query.Where(v => v != "").Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }

            if (column == "status")
            {
                var query = q.Select(l => l.SalesReturn != null ? (l.SalesReturn.Status ?? "") : "");
                if (!string.IsNullOrEmpty(search))
                    query = query.Where(v => v.Contains(search));
                var list = await query.Where(v => v != "").Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }

            return Json(Array.Empty<string>());
        }
    }
}
