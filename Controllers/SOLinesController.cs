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
    [RequirePermission("SOLines.Index")]
    public class SOLinesController : Controller
    {
        // كائن الاتصال بقاعدة البيانات
        private readonly AppDbContext _context;

        // سيرفيس إعادة حساب إجماليات أوامر البيع
        private readonly DocumentTotalsService _docTotals;

        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

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
                    ["RequestedRetailPrice"] = x => x.RequestedRetailPrice,
                    ["SalesDiscountPct"] = x => x.SalesDiscountPct,
                    ["ExpectedUnitPrice"] = x => x.ExpectedUnitPrice,
                    ["ExpectedLineTotal"] = x => x.ExpectedUnitPrice * x.QtyRequested,
                    ["Batch"] = x => x.PreferredBatchNo ?? "",
                    ["PreferredExpiry"] = x => x.PreferredExpiry ?? DateTime.MinValue,
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
            int? soId,
            string? search,
            string? searchBy = "all",
            string? sort = "SOId",
            string? dir = "asc",
            int page = 1,
            int pageSize = 50,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string? dateField = "SODate",
            string? filterCol_soid = null,
            string? filterCol_lineno = null,
            string? filterCol_prod = null,
            string? filterCol_qty = null,
            string? filterCol_reqretail = null,
            string? filterCol_disc = null,
            string? filterCol_unitprice = null,
            string? filterCol_linetotal = null,
            string? filterCol_batch = null,
            string? filterCol_expiry = null,
            string? filterCol_pricebasis = null,
            string? filterCol_date = null
        )
        {
            var q = BuildQuery(soId, search, searchBy, sort, dir, useDateRange, fromDate, toDate, fromCode, toCode);

            if (!string.IsNullOrWhiteSpace(filterCol_soid))
            {
                var ids = filterCol_soid.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (ids.Count > 0) q = q.Where(x => ids.Contains(x.SOId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_lineno))
            {
                var ids = filterCol_lineno.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (ids.Count > 0) q = q.Where(x => ids.Contains(x.LineNo));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_prod))
            {
                var ids = filterCol_prod.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (ids.Count > 0) q = q.Where(x => ids.Contains(x.ProdId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_qty))
            {
                var ids = filterCol_qty.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (ids.Count > 0) q = q.Where(x => ids.Contains(x.QtyRequested));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_reqretail))
            {
                var vals = filterCol_reqretail.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => decimal.TryParse(v.Trim(), out var d) ? d : (decimal?)null)
                    .Where(d => d.HasValue).Select(d => d!.Value).ToList();
                if (vals.Count > 0) q = q.Where(x => vals.Contains(x.RequestedRetailPrice));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_disc))
            {
                var vals = filterCol_disc.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => decimal.TryParse(v.Trim(), out var d) ? d : (decimal?)null)
                    .Where(d => d.HasValue).Select(d => d!.Value).ToList();
                if (vals.Count > 0) q = q.Where(x => vals.Contains(x.SalesDiscountPct));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_unitprice))
            {
                var vals = filterCol_unitprice.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => decimal.TryParse(v.Trim(), out var d) ? d : (decimal?)null)
                    .Where(d => d.HasValue).Select(d => d!.Value).ToList();
                if (vals.Count > 0) q = q.Where(x => vals.Contains(x.ExpectedUnitPrice));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_linetotal))
            {
                var vals = filterCol_linetotal.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => decimal.TryParse(v.Trim(), out var d) ? d : (decimal?)null)
                    .Where(d => d.HasValue).Select(d => d!.Value).ToList();
                if (vals.Count > 0) q = q.Where(x => vals.Contains(Math.Round(x.ExpectedUnitPrice * x.QtyRequested, 2)));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_batch))
            {
                var vals = filterCol_batch.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim()).Where(v => !string.IsNullOrEmpty(v)).ToList();
                if (vals.Count > 0) q = q.Where(x => x.PreferredBatchNo != null && vals.Contains(x.PreferredBatchNo));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_expiry))
            {
                var months = filterCol_expiry.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => v.Length >= 7)
                    .Select(v =>
                    {
                        var parts = v.Split('-', '/', '.');
                        if (parts.Length < 2) return (y: (int?)null, m: (int?)null);
                        return (y: int.TryParse(parts[0], out var y) ? y : (int?)null, m: int.TryParse(parts[1], out var m) ? m : (int?)null);
                    })
                    .Where(x => x.y.HasValue && x.m.HasValue)
                    .Select(x => (x.y!.Value * 100) + x.m!.Value)
                    .ToList();

                if (months.Count > 0)
                {
                    q = q.Where(x => x.PreferredExpiry.HasValue &&
                                     months.Contains((x.PreferredExpiry.Value.Year * 100) + x.PreferredExpiry.Value.Month));
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_pricebasis))
            {
                var vals = filterCol_pricebasis.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim()).Where(v => !string.IsNullOrEmpty(v)).ToList();
                if (vals.Count > 0) q = q.Where(x => x.PriceBasis != null && vals.Contains(x.PriceBasis));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_date))
            {
                var dates = filterCol_date.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => DateTime.TryParse(v.Trim(), out var d) ? d.Date : (DateTime?)null)
                    .Where(d => d.HasValue).Select(d => d!.Value).ToList();
                if (dates.Count > 0)
                    q = q.Where(x => x.SalesOrder != null && dates.Contains(x.SalesOrder.SODate.Date));
            }

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

            ViewBag.FilterCol_soid = filterCol_soid ?? "";
            ViewBag.FilterCol_lineno = filterCol_lineno ?? "";
            ViewBag.FilterCol_prod = filterCol_prod ?? "";
            ViewBag.FilterCol_qty = filterCol_qty ?? "";
            ViewBag.FilterCol_reqretail = filterCol_reqretail ?? "";
            ViewBag.FilterCol_disc = filterCol_disc ?? "";
            ViewBag.FilterCol_unitprice = filterCol_unitprice ?? "";
            ViewBag.FilterCol_linetotal = filterCol_linetotal ?? "";
            ViewBag.FilterCol_batch = filterCol_batch ?? "";
            ViewBag.FilterCol_expiry = filterCol_expiry ?? "";
            ViewBag.FilterCol_pricebasis = filterCol_pricebasis ?? "";
            ViewBag.FilterCol_date = filterCol_date ?? "";

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
            string? filterCol_soid = null,
            string? filterCol_lineno = null,
            string? filterCol_prod = null,
            string? filterCol_qty = null,
            string? filterCol_reqretail = null,
            string? filterCol_disc = null,
            string? filterCol_unitprice = null,
            string? filterCol_linetotal = null,
            string? filterCol_batch = null,
            string? filterCol_expiry = null,
            string? filterCol_pricebasis = null,
            string? filterCol_date = null,
            string format = "excel"
        )
        {
            var q = BuildQuery(soId, search, searchBy, sort, dir, useDateRange, fromDate, toDate, fromCode, toCode);

            if (!string.IsNullOrWhiteSpace(filterCol_soid))
            {
                var ids = filterCol_soid.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (ids.Count > 0) q = q.Where(x => ids.Contains(x.SOId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_lineno))
            {
                var ids = filterCol_lineno.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (ids.Count > 0) q = q.Where(x => ids.Contains(x.LineNo));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_prod))
            {
                var ids = filterCol_prod.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (ids.Count > 0) q = q.Where(x => ids.Contains(x.ProdId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_qty))
            {
                var ids = filterCol_qty.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (ids.Count > 0) q = q.Where(x => ids.Contains(x.QtyRequested));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_reqretail))
            {
                var vals = filterCol_reqretail.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => decimal.TryParse(v.Trim(), out var d) ? d : (decimal?)null)
                    .Where(d => d.HasValue).Select(d => d!.Value).ToList();
                if (vals.Count > 0) q = q.Where(x => vals.Contains(x.RequestedRetailPrice));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_disc))
            {
                var vals = filterCol_disc.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => decimal.TryParse(v.Trim(), out var d) ? d : (decimal?)null)
                    .Where(d => d.HasValue).Select(d => d!.Value).ToList();
                if (vals.Count > 0) q = q.Where(x => vals.Contains(x.SalesDiscountPct));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_unitprice))
            {
                var vals = filterCol_unitprice.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => decimal.TryParse(v.Trim(), out var d) ? d : (decimal?)null)
                    .Where(d => d.HasValue).Select(d => d!.Value).ToList();
                if (vals.Count > 0) q = q.Where(x => vals.Contains(x.ExpectedUnitPrice));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_linetotal))
            {
                var vals = filterCol_linetotal.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => decimal.TryParse(v.Trim(), out var d) ? d : (decimal?)null)
                    .Where(d => d.HasValue).Select(d => d!.Value).ToList();
                if (vals.Count > 0) q = q.Where(x => vals.Contains(Math.Round(x.ExpectedUnitPrice * x.QtyRequested, 2)));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_batch))
            {
                var vals = filterCol_batch.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim()).Where(v => !string.IsNullOrEmpty(v)).ToList();
                if (vals.Count > 0) q = q.Where(x => x.PreferredBatchNo != null && vals.Contains(x.PreferredBatchNo));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_expiry))
            {
                var months = filterCol_expiry.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => v.Length >= 7)
                    .Select(v =>
                    {
                        var parts = v.Split('-', '/', '.');
                        if (parts.Length < 2) return (y: (int?)null, m: (int?)null);
                        return (y: int.TryParse(parts[0], out var y) ? y : (int?)null, m: int.TryParse(parts[1], out var m) ? m : (int?)null);
                    })
                    .Where(x => x.y.HasValue && x.m.HasValue)
                    .Select(x => (x.y!.Value * 100) + x.m!.Value)
                    .ToList();

                if (months.Count > 0)
                {
                    q = q.Where(x => x.PreferredExpiry.HasValue &&
                                     months.Contains((x.PreferredExpiry.Value.Year * 100) + x.PreferredExpiry.Value.Month));
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_pricebasis))
            {
                var vals = filterCol_pricebasis.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim()).Where(v => !string.IsNullOrEmpty(v)).ToList();
                if (vals.Count > 0) q = q.Where(x => x.PriceBasis != null && vals.Contains(x.PriceBasis));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_date))
            {
                var dates = filterCol_date.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => DateTime.TryParse(v.Trim(), out var d) ? d.Date : (DateTime?)null)
                    .Where(d => d.HasValue).Select(d => d!.Value).ToList();
                if (dates.Count > 0)
                    q = q.Where(x => x.SalesOrder != null && dates.Contains(x.SalesOrder.SODate.Date));
            }

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

        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            if (string.IsNullOrWhiteSpace(column))
                return Json(Array.Empty<string>());
            column = column.ToLowerInvariant();
            search = (search ?? "").Trim();

            IQueryable<SOLine> q = _context.SOLines
                .AsNoTracking()
                .Include(x => x.SalesOrder);

            if (column == "soid")
            {
                var idsQuery = q.Select(x => x.SOId.ToString());
                if (!string.IsNullOrEmpty(search)) idsQuery = idsQuery.Where(v => v.Contains(search));
                var ids = await idsQuery.Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(ids);
            }
            if (column == "lineno")
            {
                var idsQuery = q.Select(x => x.LineNo.ToString());
                if (!string.IsNullOrEmpty(search)) idsQuery = idsQuery.Where(v => v.Contains(search));
                var ids = await idsQuery.Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(ids);
            }
            if (column == "prod")
            {
                var idsQuery = q.Select(x => x.ProdId.ToString());
                if (!string.IsNullOrEmpty(search)) idsQuery = idsQuery.Where(v => v.Contains(search));
                var ids = await idsQuery.Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(ids);
            }
            if (column == "qty")
            {
                var idsQuery = q.Select(x => x.QtyRequested.ToString());
                if (!string.IsNullOrEmpty(search)) idsQuery = idsQuery.Where(v => v.Contains(search));
                var ids = await idsQuery.Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(ids);
            }
            if (column == "reqretail")
            {
                var raw = await q.Select(x => x.RequestedRetailPrice).Distinct().OrderBy(v => v).Take(200).ToListAsync();
                var list = raw.Select(v => v.ToString("0.00")).Where(v => string.IsNullOrEmpty(search) || v.Contains(search)).ToList();
                return Json(list);
            }
            if (column == "disc")
            {
                var raw = await q.Select(x => x.SalesDiscountPct).Distinct().OrderBy(v => v).Take(200).ToListAsync();
                var list = raw.Select(v => v.ToString("0.00")).Where(v => string.IsNullOrEmpty(search) || v.Contains(search)).ToList();
                return Json(list);
            }
            if (column == "unitprice")
            {
                var raw = await q.Select(x => x.ExpectedUnitPrice).Distinct().OrderBy(v => v).Take(200).ToListAsync();
                var list = raw.Select(v => v.ToString("0.0000")).Where(v => string.IsNullOrEmpty(search) || v.Contains(search)).ToList();
                return Json(list);
            }
            if (column == "linetotal")
            {
                var raw = await q.Select(x => Math.Round(x.ExpectedUnitPrice * x.QtyRequested, 2)).Distinct().OrderBy(v => v).Take(200).ToListAsync();
                var list = raw.Select(v => v.ToString("0.00")).Where(v => string.IsNullOrEmpty(search) || v.Contains(search)).ToList();
                return Json(list);
            }
            if (column == "batch")
            {
                var batchQuery = q.Where(x => x.PreferredBatchNo != null && x.PreferredBatchNo != "")
                    .Select(x => x.PreferredBatchNo!);
                if (!string.IsNullOrEmpty(search)) batchQuery = batchQuery.Where(v => v.Contains(search));
                var list = await batchQuery.Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }
            if (column == "expiry")
            {
                var rawDates = await q.Where(x => x.PreferredExpiry.HasValue)
                    .Select(x => x.PreferredExpiry!.Value.Date)
                    .Distinct()
                    .OrderBy(v => v)
                    .Take(200)
                    .ToListAsync();

                var list = rawDates
                    .Select(d => d.ToString("yyyy-MM"))
                    .Distinct()
                    .Where(v => string.IsNullOrEmpty(search) || v.Contains(search))
                    .OrderBy(v => v)
                    .ToList();

                return Json(list);
            }
            if (column == "pricebasis")
            {
                var pbQuery = q.Where(x => x.PriceBasis != null && x.PriceBasis != "")
                    .Select(x => x.PriceBasis!);
                if (!string.IsNullOrEmpty(search)) pbQuery = pbQuery.Where(v => v.Contains(search));
                var list = await pbQuery.Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }
            if (column == "date")
            {
                var datesQuery = q.Where(x => x.SalesOrder != null).Select(x => x.SalesOrder!.SODate.Date);
                var rawDates = await datesQuery.Distinct().OrderBy(v => v).Take(200).ToListAsync();
                var list = rawDates.Select(d => d.ToString("yyyy-MM-dd"))
                    .Where(v => string.IsNullOrEmpty(search) || v.Contains(search)).ToList();
                return Json(list);
            }
            return Json(Array.Empty<string>());
        }
    }
}
