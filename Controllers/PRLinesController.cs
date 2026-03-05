using System;                                     // لاستخدام DateTime
using System.Collections.Generic;                 // List<T>
using System.IO;                                  // MemoryStream لتجهيز ملف الإكسل
using System.Linq;                                // أوامر LINQ
using System.Threading.Tasks;                     // async / await
using ClosedXML.Excel;                            // مكتبة ClosedXML لتصدير Excel
using ERP.Data;                                   // AppDbContext
using ERP.Filters;
using ERP.Infrastructure;                         // PagedResult
using ERP.Models;                                 // الموديلات (PRLine, PurchaseRequest)
using ERP.Security;
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
    [RequirePermission("PRLines.Index")]
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
        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        public async Task<IActionResult> Index(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? dateField = "PRDate",
            int? page = null,
            int? pageSize = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_prid = null,
            string? filterCol_lineno = null,
            string? filterCol_prodid = null,
            string? filterCol_prodname = null,
            string? filterCol_qty = null,
            string? filterCol_priceBasis = null,
            string? filterCol_priceRetail = null,
            string? filterCol_disc = null,
            string? filterCol_expectedCost = null,
            string? filterCol_linevalue = null,
            string? filterCol_batch = null,
            string? filterCol_expiry = null,
            string? filterCol_qtyConv = null)
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

            // ٢) الكويري الأساسي من جدول PRLines مع تحميل Product و PurchaseRequest (لفلتر التاريخ)
            IQueryable<PRLine> query = _context.PRLines
                .Include(l => l.Product)          // ✅ تحميل اسم الصنف
                .Include(l => l.PurchaseRequest)  // ✅ تحميل رأس الطلب لفلترة بتاريخ الطلب
                .AsNoTracking();

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

            // ٤ ب) فلاتر الأعمدة (بنمط Excel)
            query = ApplyColumnFilters(query, filterCol_prid, filterCol_lineno, filterCol_prodid, filterCol_prodname, filterCol_qty, filterCol_priceBasis, filterCol_priceRetail, filterCol_disc, filterCol_expectedCost, filterCol_linevalue, filterCol_batch, filterCol_expiry, filterCol_qtyConv);

            // ٤ مكرر) فلترة بتاريخ طلب الشراء (PRDate)
            if (useDateRange && (fromDate.HasValue || toDate.HasValue))
            {
                if (fromDate.HasValue)
                    query = query.Where(l => l.PurchaseRequest != null && l.PurchaseRequest.PRDate >= fromDate.Value);
                if (toDate.HasValue)
                    query = query.Where(l => l.PurchaseRequest != null && l.PurchaseRequest.PRDate <= toDate.Value);
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

            // =========================================================
            // حساب إجمالي قيمة السطر من نفس الاستعلام (بعد الفلاتر)
            // قيمة السطر = (QtyRequested * PriceRetail) * (1 - PurchaseDiscountPct / 100)
            // ✅ مهم: لازم قبل الـ Paging علشان ما تتحسبش على الصفحة بس
            // =========================================================
            decimal totalLineValue = await query.SumAsync(line => 
                (decimal?)((line.QtyRequested * line.PriceRetail) * (1m - (line.PurchaseDiscountPct / 100m)))) ?? 0m;

            // ٦) إنشاء PagedResult (نفس نظام القوائم الموحّد)
            var model = await PagedResult<PRLine>.CreateAsync(
                query,
                pageNumber,
                ps,
                sort,
                sortDesc,
                search,
                searchBy);

            model.UseDateRange = useDateRange;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            // ٧) تمرير قيم الفلاتر للـ View
            ViewBag.Search = search;
            ViewBag.SearchBy = searchBy;
            ViewBag.Sort = sort;
            ViewBag.Dir = sortDesc ? "desc" : "asc";
            ViewBag.DateField = dateField ?? "PRDate";

            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;

            ViewBag.FilterCol_prid = filterCol_prid;
            ViewBag.FilterCol_lineno = filterCol_lineno;
            ViewBag.FilterCol_prodid = filterCol_prodid;
            ViewBag.FilterCol_prodname = filterCol_prodname;
            ViewBag.FilterCol_qty = filterCol_qty;
            ViewBag.FilterCol_priceBasis = filterCol_priceBasis;
            ViewBag.FilterCol_priceRetail = filterCol_priceRetail;
            ViewBag.FilterCol_disc = filterCol_disc;
            ViewBag.FilterCol_expectedCost = filterCol_expectedCost;
            ViewBag.FilterCol_linevalue = filterCol_linevalue;
            ViewBag.FilterCol_batch = filterCol_batch;
            ViewBag.FilterCol_expiry = filterCol_expiry;
            ViewBag.FilterCol_qtyConv = filterCol_qtyConv;

            // ✅ إجمالي قيمة السطر (بعد الفلاتر)
            ViewBag.TotalLineValue = totalLineValue;

            return View(model);
        }

        /// <summary>
        /// تطبيق فلاتر الأعمدة (بنمط Excel) على استعلام سطور طلبات الشراء.
        /// </summary>
        private static IQueryable<PRLine> ApplyColumnFilters(
            IQueryable<PRLine> query,
            string? filterCol_prid,
            string? filterCol_lineno,
            string? filterCol_prodid,
            string? filterCol_prodname,
            string? filterCol_qty,
            string? filterCol_priceBasis,
            string? filterCol_priceRetail,
            string? filterCol_disc,
            string? filterCol_expectedCost,
            string? filterCol_linevalue,
            string? filterCol_batch,
            string? filterCol_expiry,
            string? filterCol_qtyConv)
        {
            if (!string.IsNullOrWhiteSpace(filterCol_prid))
            {
                var ids = filterCol_prid.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) query = query.Where(l => ids.Contains(l.PRId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_lineno))
            {
                var nums = filterCol_lineno.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (nums.Count > 0) query = query.Where(l => nums.Contains(l.LineNo));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_prodid))
            {
                var ids = filterCol_prodid.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) query = query.Where(l => ids.Contains(l.ProdId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_prodname))
            {
                var vals = filterCol_prodname.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) query = query.Where(l => l.Product != null && l.Product.ProdName != null && vals.Contains(l.Product.ProdName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_qty))
            {
                var nums = filterCol_qty.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (nums.Count > 0) query = query.Where(l => nums.Contains(l.QtyRequested));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_priceBasis))
            {
                var vals = filterCol_priceBasis.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) query = query.Where(l => l.PriceBasis != null && vals.Contains(l.PriceBasis));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_priceRetail))
            {
                var vals = filterCol_priceRetail.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0) query = query.Where(l => vals.Contains(l.PriceRetail));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_disc))
            {
                var vals = filterCol_disc.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0) query = query.Where(l => vals.Contains(l.PurchaseDiscountPct));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_expectedCost))
            {
                var vals = filterCol_expectedCost.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0) query = query.Where(l => vals.Contains(l.ExpectedCost));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_linevalue))
            {
                var vals = filterCol_linevalue.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0) query = query.Where(l => vals.Contains((l.QtyRequested * l.PriceRetail) * (1m - (l.PurchaseDiscountPct / 100m))));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_batch))
            {
                var vals = filterCol_batch.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) query = query.Where(l => l.PreferredBatchNo != null && vals.Contains(l.PreferredBatchNo));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_expiry))
            {
                var dates = filterCol_expiry.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => DateTime.TryParse(x.Trim(), out var d) ? d.Date : (DateTime?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (dates.Count > 0) query = query.Where(l => l.PreferredExpiry.HasValue && dates.Contains(l.PreferredExpiry.Value.Date));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_qtyConv))
            {
                var nums = filterCol_qtyConv.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (nums.Count > 0) query = query.Where(l => nums.Contains(l.QtyConverted));
            }
            return query;
        }

        /// <summary>
        /// API: جلب القيم المميزة لعمود (للفلترة بنمط Excel).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var columnLower = (column ?? "").Trim().ToLowerInvariant();
            var baseQuery = _context.PRLines.Include(l => l.Product).AsNoTracking();

            if (columnLower == "prid")
            {
                var ids = await baseQuery.Select(l => l.PRId).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "lineno")
            {
                var nums = await baseQuery.Select(l => l.LineNo).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(nums.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "prodid")
            {
                var ids = await baseQuery.Select(l => l.ProdId).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "prodname")
            {
                var names = await baseQuery.Where(l => l.Product != null && l.Product.ProdName != null)
                    .Select(l => l.Product!.ProdName!).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) names = names.Where(s => s.ToLowerInvariant().Contains(searchTerm)).ToList();
                return Json(names.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "qty")
            {
                var vals = await baseQuery.Select(l => l.QtyRequested).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(vals.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "pricebasis")
            {
                var vals = await baseQuery.Where(l => l.PriceBasis != null).Select(l => l.PriceBasis!).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) vals = vals.Where(s => s.ToLowerInvariant().Contains(searchTerm)).ToList();
                return Json(vals.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "priceretail")
            {
                var vals = await baseQuery.Select(l => l.PriceRetail).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(vals.Select(v => new { value = v.ToString("0.00"), display = v.ToString("0.00") }));
            }
            if (columnLower == "disc")
            {
                var vals = await baseQuery.Select(l => l.PurchaseDiscountPct).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(vals.Select(v => new { value = v.ToString("0.00"), display = v.ToString("0.00") }));
            }
            if (columnLower == "expectedcost")
            {
                var vals = await baseQuery.Select(l => l.ExpectedCost).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(vals.Select(v => new { value = v.ToString("0.0000"), display = v.ToString("0.0000") }));
            }
            if (columnLower == "linevalue")
            {
                var vals = await baseQuery.Select(l => (l.QtyRequested * l.PriceRetail) * (1m - (l.PurchaseDiscountPct / 100m))).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(vals.Select(v => new { value = v.ToString("0.00"), display = v.ToString("0.00") }));
            }
            if (columnLower == "batch")
            {
                var vals = await baseQuery.Where(l => l.PreferredBatchNo != null).Select(l => l.PreferredBatchNo!).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) vals = vals.Where(s => s.ToLowerInvariant().Contains(searchTerm)).ToList();
                return Json(vals.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "expiry")
            {
                var dates = await baseQuery.Where(l => l.PreferredExpiry.HasValue).Select(l => l.PreferredExpiry!.Value.Date).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(dates.Select(d => new { value = d.ToString("yyyy-MM-dd"), display = d.ToString("yyyy-MM-dd") }));
            }
            if (columnLower == "qtyconv")
            {
                var vals = await baseQuery.Select(l => l.QtyConverted).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(vals.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            return Json(Array.Empty<object>());
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
        // AddLineJson — POST: إضافة/تعديل سطر طلب شراء عبر AJAX
        // مشابه لـ PILines.AddLineJson لكن بدون تأثير على المخزون
        // ============================================================
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> AddLineJson([FromBody] AddLineJsonDto dto)
        {
            // =========================
            // 0) فحص سريع للمدخلات
            // =========================
            if (dto == null)
                return BadRequest(new { ok = false, message = "لم يتم إرسال بيانات." });

            if (dto.PRId <= 0 || dto.prodId <= 0)
                return BadRequest(new { ok = false, message = "بيانات الطلب/الصنف غير صحيحة." });

            if (dto.qty <= 0)
                return BadRequest(new { ok = false, message = "الكمية يجب أن تكون أكبر من صفر." });

            // =========================
            // 1) تحميل طلب الشراء
            // =========================
            var request = await _context.PurchaseRequests
                .FirstOrDefaultAsync(pr => pr.PRId == dto.PRId);

            if (request == null)
                return NotFound(new { ok = false, message = "طلب الشراء غير موجود." });

            if (request.IsConverted)
                return BadRequest(new { ok = false, message = "لا يمكن تعديل طلب تم تحويله إلى فاتورة." });

            // =========================
            // 2) البحث عن سطر موجود (نفس ProdId)
            // =========================
            var existingLine = await _context.PRLines
                .FirstOrDefaultAsync(l => l.PRId == dto.PRId && l.ProdId == dto.prodId);

            if (existingLine != null)
            {
                // ✅ تعديل السطر الموجود (بدون تعديل سعر الجمهور)
                // ❌ لا يمكن تعديل PriceRetail - يجب حذف السطر وإضافته من جديد
                if (dto.priceRetail != existingLine.PriceRetail)
                {
                    return BadRequest(new 
                    { 
                        ok = false, 
                        message = "لا يمكن تعديل سعر الجمهور. يجب حذف السطر وإضافته من جديد." 
                    });
                }
                
                existingLine.QtyRequested = dto.qty;
                // existingLine.PriceRetail = dto.priceRetail; // ❌ غير مسموح
                existingLine.PurchaseDiscountPct = dto.purchaseDiscountPct;
                existingLine.PreferredBatchNo = string.IsNullOrWhiteSpace(dto.PreferredBatchNo) ? null : dto.PreferredBatchNo.Trim(); // ✅ التشغيلة المفضلة
                existingLine.PreferredExpiry = dto.PreferredExpiry?.Date; // ✅ الصلاحية المفضلة
                
                // حساب التكلفة المتوقعة (بعد الخصم) باستخدام السعر الأصلي
                decimal expectedCost = existingLine.QtyRequested * existingLine.PriceRetail * (1 - (dto.purchaseDiscountPct / 100m));
                existingLine.ExpectedCost = expectedCost;

                await _context.SaveChangesAsync();
                await _docTotals.RecalcPurchaseRequestTotalsAsync(dto.PRId);

                // جلب السطور المحدثة
                var linesNow = await GetLinesForRequestAsync(dto.PRId);

                return Json(new
                {
                    ok = true,
                    message = "تم تعديل السطر بنجاح.",
                    lines = linesNow.lines,
                    totals = linesNow.totals
                });
            }

            // =========================
            // 3) إضافة سطر جديد
            // =========================
            int nextLineNo = await _context.PRLines
                .Where(l => l.PRId == dto.PRId)
                .Select(l => (int?)l.LineNo)
                .MaxAsync() ?? 0;
            nextLineNo++;

            // حساب التكلفة المتوقعة
            decimal expectedCostNew = dto.qty * dto.priceRetail * (1 - (dto.purchaseDiscountPct / 100m));

            var newLine = new PRLine
            {
                PRId = dto.PRId,
                LineNo = nextLineNo,
                ProdId = dto.prodId,
                QtyRequested = dto.qty,
                PriceRetail = dto.priceRetail,
                PurchaseDiscountPct = dto.purchaseDiscountPct,
                ExpectedCost = expectedCostNew,
                PreferredBatchNo = string.IsNullOrWhiteSpace(dto.PreferredBatchNo) ? null : dto.PreferredBatchNo.Trim(), // ✅ التشغيلة المفضلة
                PreferredExpiry = dto.PreferredExpiry?.Date, // ✅ الصلاحية المفضلة
                QtyConverted = 0
            };

            _context.PRLines.Add(newLine);
            await _context.SaveChangesAsync();

            // =========================
            // 4) إعادة حساب الإجماليات
            // =========================
            await _docTotals.RecalcPurchaseRequestTotalsAsync(dto.PRId);

            // =========================
            // 5) جلب السطور المحدثة والإجماليات
            // =========================
            var result = await GetLinesForRequestAsync(dto.PRId);

            return Json(new
            {
                ok = true,
                message = "تم إضافة السطر بنجاح.",
                lines = result.lines,
                totals = result.totals
            });
        }

        /// <summary>
        /// DTO لإضافة/تعديل سطر طلب شراء
        /// </summary>
        public class AddLineJsonDto
        {
            public int PRId { get; set; }
            public int prodId { get; set; }
            public int qty { get; set; }
            public decimal priceRetail { get; set; }
            public decimal purchaseDiscountPct { get; set; }
            public string? PreferredBatchNo { get; set; }  // ✅ التشغيلة المفضلة
            public DateTime? PreferredExpiry { get; set; }  // ✅ الصلاحية المفضلة
        }

        /// <summary>
        /// دالة مساعدة: جلب السطور والإجماليات لطلب شراء
        /// </summary>
        private async Task<(List<object> lines, object totals)> GetLinesForRequestAsync(int prId)
        {
            var lines = await _context.PRLines
                .Where(l => l.PRId == prId)
                .OrderBy(l => l.LineNo)
                .ToListAsync();

            var prodIds = lines.Select(l => l.ProdId).Distinct().ToList();
            var prodMap = await _context.Products
                .Where(p => prodIds.Contains(p.ProdId))
                .Select(p => new { p.ProdId, p.ProdName })
                .ToDictionaryAsync(x => x.ProdId, x => x.ProdName ?? "");

            var linesDto = lines.Select(l =>
            {
                var name = prodMap.TryGetValue(l.ProdId, out var n) ? n : "";
                return new
                {
                    lineNo = l.LineNo,
                    prodId = l.ProdId,
                    prodName = name,
                    qty = l.QtyRequested,
                    priceRetail = l.PriceRetail,
                    purchaseDiscountPct = l.PurchaseDiscountPct,
                    expectedCost = l.ExpectedCost,
                    preferredBatchNo = l.PreferredBatchNo, // ✅ التشغيلة المفضلة
                    preferredExpiry = l.PreferredExpiry?.ToString("yyyy-MM-dd"), // ✅ الصلاحية المفضلة
                    qtyConverted = l.QtyConverted
                };
            }).ToList<object>();

            var request = await _context.PurchaseRequests
                .FirstOrDefaultAsync(pr => pr.PRId == prId);

            var totals = new
            {
                totalLines = lines.Count,
                totalItems = prodIds.Count,
                totalQty = lines.Sum(l => l.QtyRequested),
                totalExpectedCost = request?.ExpectedItemsTotal ?? 0m
            };

            return (linesDto, totals);
        }

        // ============================================================
        // EDIT (GET): فتح شاشة تعديل سطر طلب الشراء
        // ============================================================
        [HttpGet]
        public async Task<IActionResult> Edit(int prId, int lineNo)
        {
            var line = await _context.PRLines
                .Include(l => l.PurchaseRequest)
                .FirstOrDefaultAsync(l => l.PRId == prId && l.LineNo == lineNo);

            if (line == null)
                return NotFound();

            // التحقق من أن الطلب غير محول
            if (line.PurchaseRequest.IsConverted)
            {
                TempData["Error"] = "لا يمكن تعديل سطر في طلب تم تحويله إلى فاتورة.";
                return RedirectToAction(nameof(Index), new { search = prId.ToString(), searchBy = "pr" });
            }

            return View(line); // View: Edit.cshtml
        }

        // ============================================================
        // EDIT (POST): حفظ تعديل سطر طلب الشراء
        // ✅ مسموح التعديل: الكمية، التشغيلة، الصلاحية
        // ❌ غير مسموح: سعر الجمهور (يجب حذف السطر وإضافته من جديد)
        // ============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int prId, int lineNo, PRLine model)
        {
            // تأمين: نتأكد أن المفاتيح في الرابط = المفاتيح في الموديل
            if (prId != model.PRId || lineNo != model.LineNo)
                return BadRequest();

            var existing = await _context.PRLines
                .Include(l => l.PurchaseRequest)
                .FirstOrDefaultAsync(l => l.PRId == prId && l.LineNo == lineNo);

            if (existing == null)
                return NotFound();

            // ================================
            // 1) التحقق من أن الطلب غير محول
            // ================================
            if (existing.PurchaseRequest.IsConverted)
            {
                TempData["Error"] = "لا يمكن تعديل سطر في طلب تم تحويله إلى فاتورة.";
                return RedirectToAction(nameof(Index), new { search = existing.PRId.ToString(), searchBy = "pr" });
            }

            // ================================
            // 2) التحقق من أن سعر الجمهور لم يتغير
            // ================================
            if (model.PriceRetail != existing.PriceRetail)
            {
                ModelState.AddModelError("PriceRetail", "لا يمكن تعديل سعر الجمهور. يجب حذف السطر وإضافته من جديد.");
                return View(existing);
            }

            // ================================
            // 3) نسخ الحقول المسموح بتعديلها فقط
            // ================================
            existing.QtyRequested = model.QtyRequested;           // ✅ الكمية المطلوبة
            existing.PreferredBatchNo = model.PreferredBatchNo;   // ✅ التشغيلة المفضلة
            existing.PreferredExpiry = model.PreferredExpiry;     // ✅ الصلاحية المفضلة
            
            // إعادة حساب التكلفة المتوقعة بناءً على الكمية الجديدة (مع الحفاظ على السعر والخصم)
            decimal expectedCost = existing.QtyRequested * existing.PriceRetail * (1 - (existing.PurchaseDiscountPct / 100m));
            existing.ExpectedCost = expectedCost;

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

            // =========================
            // منع الحذف لو الطلب تم تحويله
            // =========================
            var request = await _context.PurchaseRequests
                .FirstOrDefaultAsync(pr => pr.PRId == prId);

            if (request == null)
            {
                TempData["Error"] = "طلب الشراء غير موجود.";
                return RedirectToAction(nameof(Index));
            }

            if (request.IsConverted)
            {
                TempData["Error"] = "لا يمكن حذف سطر من طلب تم تحويله إلى فاتورة شراء.";
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

            // جلب السطور المراد حذفها مع التحقق من حالة الطلبات
            var toDelete = new List<PRLine>();
            var blockedRequests = new HashSet<int>(); // طلبات محوّلة (ممنوع حذف سطورها)

            foreach (var (prId, lineNo) in ids)
            {
                var line = await _context.PRLines
                    .FirstOrDefaultAsync(l => l.PRId == prId && l.LineNo == lineNo);

                if (line != null)
                {
                    // التحقق من حالة الطلب
                    var request = await _context.PurchaseRequests
                        .FirstOrDefaultAsync(pr => pr.PRId == prId);

                    if (request != null && request.IsConverted)
                    {
                        blockedRequests.Add(prId);
                        continue; // تخطي هذا السطر
                    }

                    toDelete.Add(line);
                }
            }

            if (!toDelete.Any())
            {
                if (blockedRequests.Any())
                {
                    TempData["Error"] = $"لا يمكن حذف السطور: الطلبات {string.Join(", ", blockedRequests)} تم تحويلها إلى فواتير شراء.";
                }
                else
                {
                    TempData["Error"] = "لم يتم العثور على الصفوف المحددة.";
                }
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
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_prid = null,
            string? filterCol_lineno = null,
            string? filterCol_prodid = null,
            string? filterCol_prodname = null,
            string? filterCol_qty = null,
            string? filterCol_priceBasis = null,
            string? filterCol_priceRetail = null,
            string? filterCol_disc = null,
            string? filterCol_expectedCost = null,
            string? filterCol_linevalue = null,
            string? filterCol_batch = null,
            string? filterCol_expiry = null,
            string? filterCol_qtyConv = null,
            string format = "csv")
        {
            search = (search ?? string.Empty).Trim();
            searchBy = string.IsNullOrWhiteSpace(searchBy) ? "all" : searchBy;

            IQueryable<PRLine> query = _context.PRLines
                .Include(l => l.Product)
                .Include(l => l.PurchaseRequest)
                .AsNoTracking()
                .AsQueryable();

            // فلترة بتاريخ طلب الشراء
            if (useDateRange && (fromDate.HasValue || toDate.HasValue))
            {
                if (fromDate.HasValue)
                    query = query.Where(l => l.PurchaseRequest != null && l.PurchaseRequest.PRDate >= fromDate.Value);
                if (toDate.HasValue)
                    query = query.Where(l => l.PurchaseRequest != null && l.PurchaseRequest.PRDate <= toDate.Value);
            }

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

            query = ApplyColumnFilters(query, filterCol_prid, filterCol_lineno, filterCol_prodid, filterCol_prodname, filterCol_qty, filterCol_priceBasis, filterCol_priceRetail, filterCol_disc, filterCol_expectedCost, filterCol_linevalue, filterCol_batch, filterCol_expiry, filterCol_qtyConv);

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
