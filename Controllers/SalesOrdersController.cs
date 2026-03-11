using ERP.Data;                               // تعليق: سياق قاعدة البيانات AppDbContext
using ERP.Filters;
using ERP.Infrastructure;                    // PagedResult + ApplySearchSort + UserActivityLogger
using ERP.Models;                            // SalesOrder, SOLine, SalesInvoice, SalesInvoiceLine, UserActionType
using ERP.Security;
using ERP.Services;                          // DocumentTotalsService
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;                              // MemoryStream لتصدير Excel
using System.Linq;
using System.Linq.Expressions;
using System.Text;                            // تعليق: علشان تكوين ملف CSV في Export
using System.Threading.Tasks;
using ClosedXML.Excel;                       // تصدير Excel الفعلي

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر هيدر أوامر البيع:
    /// - عرض قائمة أوامر البيع مع بحث/ترتيب/تقسيم صفحات + فلاتر التاريخ والكود.
    /// - حذف أمر واحد + حذف مجموعة + حذف كل الأوامر.
    /// - زر تفاصيل يفتح سطور الأمر في كنترولر SOLines.
    /// </summary>
    [RequirePermission("SalesOrders.Index")]
    public class SalesOrdersController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IUserActivityLogger _activityLogger;
        private readonly DocumentTotalsService _docTotals;

        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        public SalesOrdersController(AppDbContext context, IUserActivityLogger activityLogger, DocumentTotalsService docTotals)
        {
            _context = context;
            _activityLogger = activityLogger;
            _docTotals = docTotals ?? throw new ArgumentNullException(nameof(docTotals));
        }

        private string GetCurrentUserDisplayName()
        {
            if (User?.Identity?.IsAuthenticated == true)
            {
                var displayName = User.FindFirst("DisplayName")?.Value;
                if (!string.IsNullOrWhiteSpace(displayName)) return displayName.Trim();
                var claimName = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
                if (!string.IsNullOrWhiteSpace(claimName)) return claimName.Trim();
                if (!string.IsNullOrWhiteSpace(User.Identity?.Name)) return User.Identity.Name.Trim();
            }
            return "System";
        }

        #region خرائط الحقول للبحث والترتيب (مستخدمة في Index و Export)

        // تعليق: الحقول النصية التي يمكن البحث فيها بسلسلة حروف (Contains)
        private static readonly Dictionary<string, Expression<Func<SalesOrder, string?>>> StringFields =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["status"] = o => o.Status ?? "",
                ["notes"] = o => o.Notes ?? "",
                ["createdby"] = o => o.CreatedBy ?? ""
            };

        // تعليق: الحقول الرقمية (int) التي يمكن البحث فيها كأرقام
        private static readonly Dictionary<string, Expression<Func<SalesOrder, int>>> IntFields =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = o => o.SOId,        // رقم أمر البيع
                ["customer"] = o => o.CustomerId,  // كود العميل
                ["warehouse"] = o => o.WarehouseId  // كود المخزن
            };

        // تعليق: حقول الترتيب Sorting
        private static readonly Dictionary<string, Expression<Func<SalesOrder, object>>> OrderFields =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["SODate"] = o => o.SODate,
                ["SOId"] = o => o.SOId,
                ["CustomerId"] = o => o.CustomerId,
                ["WarehouseId"] = o => o.WarehouseId,
                ["Status"] = o => o.Status ?? "",
                ["CreatedBy"] = o => o.CreatedBy ?? ""
            };






        // =========================
        // دالة مساعدة: تحميل العملاء + المخازن للـ ViewBag
        // =========================
        private async Task PopulateDropDownsAsync(int? selectedCustomerId = null, int? selectedWarehouseId = null)
        {
            // قائمة العملاء للأوتوكومبليت في الهيدر
            // هنا بنرجّع نفس الشكل المستخدم في فاتورة البيع:
            // Name / Id / PolicyName / UserName / Phone / Address / CreditLimit
            var customers = await _context.Customers
                .Where(c => c.IsActive == true)
                .OrderBy(c => c.CustomerName)
                .Select(c => new
                {
                    Id = c.CustomerId,                 // كود العميل
                    Name = c.CustomerName,             // اسم العميل
                    PolicyName = "",                   // ممكن تربطها بسياسة العميل لاحقاً
                    UserName = "",                     // ممكن تربطها بالمستخدم المسئول عن العميل
                    Phone = c.Phone1,                  // التليفون
                    Address = c.Address,               // العنوان
                    CreditLimit = c.CreditLimit        // الحد الائتماني (لو موجود)
                })
                .ToListAsync();

            ViewBag.Customers = customers;

            // قائمة المخازن للدروب داون
            var warehouses = await _context.Warehouses
                .OrderBy(w => w.WarehouseName)
                .ToListAsync();

            ViewBag.Warehouses = new SelectList(
                warehouses,
                "WarehouseId",          // اسم عمود المفتاح في جدول المخازن
                "WarehouseName",        // اسم المخزن المعروض
                selectedWarehouseId     // المخزن المختار (لو موجود)
            );
        }




        #endregion

        #region دالة مساعدة لتطبيق فلتر التاريخ + رقم الأمر

        /// <summary>
        /// تطبيق فلترة التاريخ (SODate) + رقم الأمر من/إلى على الاستعلام.
        /// </summary>
        private static IQueryable<SalesOrder> ApplyCodeAndDateFilters(
            IQueryable<SalesOrder> query,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            int? fromCode,
            int? toCode)
        {
            // فلتر رقم الأمر من/إلى
            if (fromCode.HasValue)
            {
                int cFrom = fromCode.Value;
                query = query.Where(o => o.SOId >= cFrom);
            }

            if (toCode.HasValue)
            {
                int cTo = toCode.Value;
                query = query.Where(o => o.SOId <= cTo);
            }

            // فلتر التاريخ من/إلى (على SODate)
            if (useDateRange)
            {
                if (fromDate.HasValue)
                {
                    var dFrom = fromDate.Value.Date;
                    query = query.Where(o => o.SODate >= dFrom);
                }

                if (toDate.HasValue)
                {
                    var dTo = toDate.Value.Date;
                    query = query.Where(o => o.SODate <= dTo);
                }
            }

            return query;
        }

        #endregion

        // =========================================================
        // GET: /SalesOrders
        // شاشة قائمة أوامر البيع بنفس نظام فواتير المبيعات
        // =========================================================
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",
            string? sort = "SODate",
            string? dir = "desc",
            int page = 1,
            int pageSize = 50,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? dateField = "SODate",
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_date = null,
            string? filterCol_customer = null,
            string? filterCol_warehouse = null,
            string? filterCol_status = null,
            string? filterCol_createdBy = null
        )
        {
            // 1) الاستعلام الأساسي مع تحميل العميل والمخزن
            IQueryable<SalesOrder> q = _context.SalesOrders
                .AsNoTracking()
                .Include(o => o.Customer)
                .Include(o => o.Warehouse);

            // 2) تطبيق فلتر التاريخ + فلتر رقم الأمر
            q = ApplyCodeAndDateFilters(q, useDateRange, fromDate, toDate, fromCode, toCode);

            // 3) تطبيق منظومة البحث + الترتيب الموحدة
            q = q.ApplySearchSort(
                search: search,
                searchBy: searchBy,
                sort: sort,
                dir: dir,
                stringFields: StringFields,
                intFields: IntFields,
                orderFields: OrderFields,
                defaultSearchBy: "all",
                defaultSortBy: "SODate"
            );

            // 3.1) فلاتر الأعمدة بنمط Excel
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value)
                    .ToList();
                if (ids.Count > 0)
                    q = q.Where(o => ids.Contains(o.SOId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_date))
            {
                var dates = filterCol_date.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => DateTime.TryParse(v.Trim(), out var d) ? d.Date : (DateTime?)null)
                    .Where(d => d.HasValue).Select(d => d!.Value)
                    .ToList();
                if (dates.Count > 0)
                    q = q.Where(o => dates.Contains(o.SODate.Date));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_customer))
            {
                var vals = filterCol_customer.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(o =>
                        vals.Contains(
                            o.Customer != null ? (o.Customer.CustomerName ?? o.CustomerId.ToString()) : o.CustomerId.ToString()
                        ));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_warehouse))
            {
                var vals = filterCol_warehouse.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(o =>
                        vals.Contains(
                            o.Warehouse != null ? (o.Warehouse.WarehouseName ?? o.WarehouseId.ToString()) : o.WarehouseId.ToString()
                        ));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_status))
            {
                var vals = filterCol_status.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(o => o.Status != null && vals.Contains(o.Status));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_createdBy))
            {
                var vals = filterCol_createdBy.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(o => o.CreatedBy != null && vals.Contains(o.CreatedBy));
            }

            // 4) إنشاء الموديل المقسّم إلى صفحات
            var model = await PagedResult<SalesOrder>.CreateAsync(q, page, pageSize);

            // 5) تخزين قيم البحث/الترتيب في الموديل (للاستخدام في الفيو)
            model.Search = search;
            model.SearchBy = searchBy;
            model.SortColumn = sort;
            model.SortDescending = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            model.UseDateRange = useDateRange;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            // 6) خيارات البحث والفرز للبارشال المشترك أعلى الجدول
            ViewBag.SearchOptions = new[]
            {
                new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("الكل",       "all",       string.Equals(searchBy, "all",       StringComparison.OrdinalIgnoreCase)),
                new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("رقم الأمر",  "id",        string.Equals(searchBy, "id",        StringComparison.OrdinalIgnoreCase)),
                new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("العميل",     "customer",  string.Equals(searchBy, "customer",  StringComparison.OrdinalIgnoreCase)),
                new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("المخزن",     "warehouse", string.Equals(searchBy, "warehouse", StringComparison.OrdinalIgnoreCase)),
                new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("الحالة",     "status",    string.Equals(searchBy, "status",    StringComparison.OrdinalIgnoreCase)),
            };

            ViewBag.SortOptions = new[]
            {
                new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("التاريخ", "SODate",      string.Equals(sort, "SODate",      StringComparison.OrdinalIgnoreCase)),
                new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("الرقم",   "SOId",        string.Equals(sort, "SOId",        StringComparison.OrdinalIgnoreCase)),
                new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("العميل",  "CustomerId",  string.Equals(sort, "CustomerId",  StringComparison.OrdinalIgnoreCase)),
                new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("المخزن",  "WarehouseId", string.Equals(sort, "WarehouseId", StringComparison.OrdinalIgnoreCase)),
                new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("الحالة",  "Status",      string.Equals(sort, "Status",      StringComparison.OrdinalIgnoreCase)),
                new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("أنشأه",  "CreatedBy",   string.Equals(sort, "CreatedBy",   StringComparison.OrdinalIgnoreCase)),
            };

            // 7) تخزين القيم في ViewBag (للنماذج والهيدرات)
            ViewBag.Search = search ?? "";
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.Sort = sort ?? "SODate";
            ViewBag.Dir = model.SortDescending ? "desc" : "asc";

            ViewBag.Page = model.PageNumber;
            ViewBag.PageSize = model.PageSize;

            // فلتر التاريخ في الفيو
            ViewBag.DateField = dateField ?? "SODate";

            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.CodeFrom = fromCode;
            ViewBag.CodeTo = toCode;

            ViewBag.FilterCol_Id = filterCol_id ?? "";
            ViewBag.FilterCol_Date = filterCol_date ?? "";
            ViewBag.FilterCol_Customer = filterCol_customer ?? "";
            ViewBag.FilterCol_Warehouse = filterCol_warehouse ?? "";
            ViewBag.FilterCol_Status = filterCol_status ?? "";
            ViewBag.FilterCol_CreatedBy = filterCol_createdBy ?? "";

            return View(model);
        }

        // =========================================================
        // GET: /SalesOrders/Delete/5
        // صفحة تأكيد حذف أمر بيع واحد
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            if (id <= 0) return NotFound();

            var order = await _context.SalesOrders
                                      .AsNoTracking()
                                      .FirstOrDefaultAsync(o => o.SOId == id);

            if (order == null) return NotFound();

            return View(order); // تعليق: يعرض بيانات بسيطة في صفحة التأكيد
        }

        // =========================================================
        // POST: /SalesOrders/Delete/5
        // تنفيذ الحذف الفعلي لأمر واحد + سطوره (Cascade)
        // =========================================================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (id <= 0) return NotFound();

            using var tx = await _context.Database.BeginTransactionAsync();

            try
            {
                var order = await _context.SalesOrders
                                          .FirstOrDefaultAsync(o => o.SOId == id);

                if (order == null)
                {
                    TempData["Err"] = "لم يتم العثور على أمر البيع.";
                    return RedirectToAction(nameof(Index));
                }

                var oldValues = System.Text.Json.JsonSerializer.Serialize(new { order.SODate, order.CustomerId, order.WarehouseId, order.ExpectedItemsTotal });
                // ملاحظة: يمكن لاحقاً منع الحذف لو الحالة Approved / Closed
                _context.SalesOrders.Remove(order);   // حذف الهيدر، والسطور تُحذف بالكاسكيد
                await _context.SaveChangesAsync();

                await tx.CommitAsync();

                await _activityLogger.LogAsync(UserActionType.Delete, "SalesOrder", id, $"حذف أمر بيع رقم {id}", oldValues: oldValues);

                TempData["ok"] = $"تم حذف أمر البيع رقم {id}.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Err"] = "تعذّر حذف أمر البيع: " + ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // POST: /SalesOrders/BulkDelete
        // حذف مجموعة من أوامر البيع المحددة من checkbox
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["Err"] = "لم يتم اختيار أي أوامر للحذف.";
                return RedirectToAction(nameof(Index));
            }

            // تحويل النص القادم "1,2,3" إلى قائمة أرقام صحيحة
            var ids = selectedIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var n) ? n : (int?)null)
                .Where(n => n.HasValue)
                .Select(n => n!.Value)
                .ToList();

            if (ids.Count == 0)
            {
                TempData["Err"] = "لم يتم تحديد أرقام صحيحة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            using var tx = await _context.Database.BeginTransactionAsync();

            try
            {
                var orders = await _context.SalesOrders
                                           .Where(o => ids.Contains(o.SOId))
                                           .ToListAsync();

                if (!orders.Any())
                {
                    TempData["Err"] = "لا توجد أوامر مطابقة للحذف.";
                    return RedirectToAction(nameof(Index));
                }

                // ملاحظة: يمكن لاحقاً فلترة الأوامر المسموح حذفها حسب الحالة
                _context.SalesOrders.RemoveRange(orders);
                await _context.SaveChangesAsync();

                await tx.CommitAsync();

                TempData["ok"] = $"تم حذف {orders.Count} من أوامر البيع المحددة.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Err"] = "حدث خطأ أثناء حذف الأوامر المحددة: " + ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // POST: /SalesOrders/DeleteAll
        // حذف جميع أوامر البيع (حذر!)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            using var tx = await _context.Database.BeginTransactionAsync();

            try
            {
                var allOrders = await _context.SalesOrders.ToListAsync();
                if (!allOrders.Any())
                {
                    TempData["Err"] = "لا توجد أوامر لحذفها.";
                    return RedirectToAction(nameof(Index));
                }

                _context.SalesOrders.RemoveRange(allOrders);
                await _context.SaveChangesAsync();

                await tx.CommitAsync();

                TempData["ok"] = "تم حذف جميع أوامر البيع.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Err"] = "حدث خطأ أثناء حذف جميع أوامر البيع: " + ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // GET: /SalesOrders/Export
        // تصدير أوامر البيع (Excel أو CSV) مع احترام كل الفلاتر
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy = "all",
            string? sort = "SODate",
            string? dir = "desc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? codeFrom = null,
            int? codeTo = null,
            string? filterCol_id = null,
            string? filterCol_date = null,
            string? filterCol_customer = null,
            string? filterCol_warehouse = null,
            string? filterCol_status = null,
            string? filterCol_createdBy = null,
            string? format = "excel"
        )
        {
            IQueryable<SalesOrder> q = _context.SalesOrders
                .AsNoTracking()
                .Include(o => o.Customer)
                .Include(o => o.Warehouse);

            q = ApplyCodeAndDateFilters(q, useDateRange, fromDate, toDate, codeFrom, codeTo);
            q = q.ApplySearchSort(
                search, searchBy, sort, dir,
                StringFields, IntFields, OrderFields,
                defaultSearchBy: "all",
                defaultSortBy: "SODate"
            );

            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                if (ids.Count > 0) q = q.Where(o => ids.Contains(o.SOId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_date))
            {
                var dates = filterCol_date.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => DateTime.TryParse(v.Trim(), out var d) ? d.Date : (DateTime?)null)
                    .Where(d => d.HasValue).Select(d => d!.Value).ToList();
                if (dates.Count > 0) q = q.Where(o => dates.Contains(o.SODate.Date));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_customer))
            {
                var vals = filterCol_customer.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim()).Where(v => !string.IsNullOrEmpty(v)).ToList();
                if (vals.Count > 0)
                    q = q.Where(o => vals.Contains(
                        o.Customer != null ? (o.Customer.CustomerName ?? o.CustomerId.ToString()) : o.CustomerId.ToString()));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_warehouse))
            {
                var vals = filterCol_warehouse.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim()).Where(v => !string.IsNullOrEmpty(v)).ToList();
                if (vals.Count > 0)
                    q = q.Where(o => vals.Contains(
                        o.Warehouse != null ? (o.Warehouse.WarehouseName ?? o.WarehouseId.ToString()) : o.WarehouseId.ToString()));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_status))
            {
                var vals = filterCol_status.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim()).Where(v => !string.IsNullOrEmpty(v)).ToList();
                if (vals.Count > 0) q = q.Where(o => o.Status != null && vals.Contains(o.Status));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_createdBy))
            {
                var vals = filterCol_createdBy.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim()).Where(v => !string.IsNullOrEmpty(v)).ToList();
                if (vals.Count > 0) q = q.Where(o => o.CreatedBy != null && vals.Contains(o.CreatedBy));
            }

            var data = await q.OrderBy(o => o.SODate).ThenBy(o => o.SOId).ToListAsync();
            format = (format ?? "excel").Trim().ToLowerInvariant();

            if (format == "csv")
            {
                var sb = new StringBuilder();
                sb.AppendLine("SOId,SODate,Customer,Warehouse,Status,CreatedBy");
                foreach (var o in data)
                {
                    var cust = o.Customer != null ? o.Customer.CustomerName : o.CustomerId.ToString();
                    var wh = o.Warehouse != null ? o.Warehouse.WarehouseName : o.WarehouseId.ToString();
                    sb.AppendLine($"{o.SOId},{o.SODate:yyyy-MM-dd},\"{cust?.Replace("\"", "\"\"")}\",\"{wh?.Replace("\"", "\"\"")}\",\"{(o.Status ?? "").Replace("\"", "\"\"")}\",\"{(o.CreatedBy ?? "").Replace("\"", "\"\"")}\"");
                }
                var utf8Bom = new UTF8Encoding(true);
                return File(utf8Bom.GetBytes(sb.ToString()), "text/csv; charset=utf-8", $"SalesOrders_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            }

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("SalesOrders");
            int r = 1;
            ws.Cell(r, 1).Value = "رقم الأمر";
            ws.Cell(r, 2).Value = "التاريخ";
            ws.Cell(r, 3).Value = "العميل";
            ws.Cell(r, 4).Value = "المخزن";
            ws.Cell(r, 5).Value = "الحالة";
            ws.Cell(r, 6).Value = "أنشأه";
            var headerRange = ws.Range(r, 1, r, 6);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            foreach (var o in data)
            {
                r++;
                ws.Cell(r, 1).Value = o.SOId;
                ws.Cell(r, 2).Value = o.SODate;
                ws.Cell(r, 3).Value = o.Customer != null ? o.Customer.CustomerName : o.CustomerId.ToString();
                ws.Cell(r, 4).Value = o.Warehouse != null ? o.Warehouse.WarehouseName : o.WarehouseId.ToString();
                ws.Cell(r, 5).Value = o.Status ?? "";
                ws.Cell(r, 6).Value = o.CreatedBy ?? "";
            }
            ws.Columns().AdjustToContents();
            ws.Column(2).Style.DateFormat.Format = "yyyy-mm-dd";
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            stream.Position = 0;
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"SalesOrders_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        }

        // =========================================================
        // GetColumnValues — قيم مميزة لكل عمود لفلترة الأعمدة
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            if (string.IsNullOrWhiteSpace(column))
                return Json(Array.Empty<string>());
            column = column.ToLowerInvariant();
            search = (search ?? "").Trim();

            IQueryable<SalesOrder> q = _context.SalesOrders
                .AsNoTracking()
                .Include(o => o.Customer)
                .Include(o => o.Warehouse);

            if (column == "id")
            {
                var idsQuery = q.Select(o => o.SOId.ToString());
                if (!string.IsNullOrEmpty(search)) idsQuery = idsQuery.Where(v => v.Contains(search));
                var ids = await idsQuery.Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(ids);
            }
            if (column == "date")
            {
                var datesQuery = q.Select(o => o.SODate.Date);
                var rawDates = await datesQuery.Distinct().OrderBy(v => v).Take(200).ToListAsync();
                var list = rawDates.Select(d => d.ToString("yyyy-MM-dd"))
                    .Where(v => string.IsNullOrEmpty(search) || v.Contains(search)).ToList();
                return Json(list);
            }
            if (column == "customer")
            {
                var custQuery = q.Select(o => o.Customer != null ? o.Customer.CustomerName : o.CustomerId.ToString());
                if (!string.IsNullOrEmpty(search)) custQuery = custQuery.Where(v => v != null && v.Contains(search));
                var list = await custQuery.Where(v => v != null).Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }
            if (column == "warehouse")
            {
                var whQuery = q.Select(o => o.Warehouse != null ? o.Warehouse.WarehouseName : o.WarehouseId.ToString());
                if (!string.IsNullOrEmpty(search)) whQuery = whQuery.Where(v => v != null && v.Contains(search));
                var list = await whQuery.Where(v => v != null).Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }
            if (column == "status")
            {
                var statusQuery = q.Select(o => o.Status ?? "");
                if (!string.IsNullOrEmpty(search)) statusQuery = statusQuery.Where(v => v.Contains(search));
                var list = await statusQuery.Where(v => v != "").Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }
            if (column == "createdby")
            {
                var createdQuery = q.Select(o => o.CreatedBy ?? "");
                if (!string.IsNullOrEmpty(search)) createdQuery = createdQuery.Where(v => v.Contains(search));
                var list = await createdQuery.Where(v => v != "").Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }
            return Json(Array.Empty<string>());
        }




        // =========================
        // Create — GET: فتح شاشة أمر بيع جديد (نفس نمط طلب الشراء: Show مع SOId=0)
        // =========================
        [HttpGet]
        public async Task<IActionResult> Create(int? frame = null)
        {
            var defaultWarehouseId = await GetDefaultWarehouseIdAsync();
            var model = new SalesOrder
            {
                SOId = 0,
                SODate = DateTime.Today,
                CustomerId = 0,
                WarehouseId = defaultWarehouseId > 0 ? defaultWarehouseId : 0,
                Status = "Draft",
                IsConverted = false,
                Notes = null,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = GetCurrentUserDisplayName(),
                TotalQtyRequested = 0,
                ExpectedItemsTotal = 0m
            };

            await PopulateDropDownsAsync(null, model.WarehouseId);
            await LoadProductsForShowAsync();
            await FillSalesOrderNavAsync(0);

            ViewBag.Fragment = null;
            ViewBag.Frame = (frame == 1) ? 1 : 0;
            ViewBag.IsLocked = false;

            return View("Show", model);
        }

        private async Task<int> GetDefaultWarehouseIdAsync()
        {
            var id = await _context.Warehouses
                .AsNoTracking()
                .OrderBy(w => w.WarehouseId)
                .Select(w => w.WarehouseId)
                .FirstOrDefaultAsync();
            return id;
        }

        private async Task LoadProductsForShowAsync()
        {
            var list = await _context.Products
                .AsNoTracking()
                .OrderBy(p => p.ProdName)
                .Select(p => new { p.ProdId, p.ProdName })
                .Take(1000)
                .ToListAsync();
            ViewBag.Products = list;
        }






        // =========================
        // Create — POST: حفظ أمر البيع الجديد (من فورم تقليدي إن وُجد، وإلا الاعتماد على SaveHeader من Show)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(SalesOrder order)
        {
            if (!ModelState.IsValid)
            {
                await PopulateDropDownsAsync();
                return View("Show", order);
            }

            order.Status ??= "Draft";
            order.IsConverted = false;
            order.CreatedAt = DateTime.UtcNow;
            order.CreatedBy ??= GetCurrentUserDisplayName();

            _context.SalesOrders.Add(order);
            await _context.SaveChangesAsync();

            await _activityLogger.LogAsync(UserActionType.Create, "SalesOrder", order.SOId, $"إنشاء أمر بيع رقم {order.SOId}");
            TempData["Msg"] = "تم إنشاء أمر البيع بنجاح.";
            return RedirectToAction(nameof(Show), new { id = order.SOId, frame = 1 });
        }






        // =========================
        // Show — عرض أمر بيع (هيدر + سطور + تنقل أول/سابق/تالي/آخر، مثل طلب الشراء)
        // =========================
        [HttpGet]
        [ResponseCache(NoStore = true, Duration = 0)]
        public async Task<IActionResult> Show(int id, string? frag = null, int? frame = null)
        {
            bool isBodyOnly = string.Equals(frag, "body", StringComparison.OrdinalIgnoreCase);
            if (!isBodyOnly && frame != 1)
                return RedirectToAction(nameof(Show), new { id, frag, frame = 1 });

            ViewBag.Fragment = frag;

            if (id <= 0)
            {
                TempData["Error"] = "رقم أمر البيع غير صالح.";
                return RedirectToAction(nameof(Create), new { frame = 1 });
            }

            var order = await _context.SalesOrders
                .Include(o => o.Customer)
                .Include(o => o.Warehouse)
                .Include(o => o.Lines)
                    .ThenInclude(l => l.Product)
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.SOId == id);

            if (order == null)
            {
                if (isBodyOnly)
                    return NotFound($"أمر البيع رقم ({id}) غير موجود.");
                int? nextId = await _context.SalesOrders.AsNoTracking().Where(x => x.SOId > id).OrderBy(x => x.SOId).Select(x => (int?)x.SOId).FirstOrDefaultAsync();
                if (nextId.HasValue && nextId.Value > 0)
                {
                    TempData["Error"] = $"رقم أمر البيع ({id}) غير موجود. تم فتح الأمر التالي ({nextId.Value}).";
                    return RedirectToAction(nameof(Show), new { id = nextId.Value, frag = (string?)null, frame = 1 });
                }
                int? prevId = await _context.SalesOrders.AsNoTracking().Where(x => x.SOId < id).OrderByDescending(x => x.SOId).Select(x => (int?)x.SOId).FirstOrDefaultAsync();
                if (prevId.HasValue && prevId.Value > 0)
                {
                    TempData["Error"] = $"رقم أمر البيع ({id}) غير موجود. تم فتح الأمر السابق ({prevId.Value}).";
                    return RedirectToAction(nameof(Show), new { id = prevId.Value, frag = (string?)null, frame = 1 });
                }
                TempData["Error"] = "لا توجد أوامر بيع مسجلة.";
                return RedirectToAction(nameof(Create), new { frame = 1 });
            }

            await PopulateDropDownsAsync(order.CustomerId, order.WarehouseId);
            await LoadProductsForShowAsync();
            ViewBag.IsLocked = order.IsConverted
                || string.Equals(order.Status, "Converted", StringComparison.OrdinalIgnoreCase)
                || string.Equals(order.Status, "Cancelled", StringComparison.OrdinalIgnoreCase);
            ViewBag.Frame = isBodyOnly ? 0 : 1;
            await FillSalesOrderNavAsync(order.SOId);

            return View("Show", order);
        }

        private async Task FillSalesOrderNavAsync(int currentId)
        {
            var minMax = await _context.SalesOrders.AsNoTracking().GroupBy(_ => 1)
                .Select(g => new { FirstId = g.Min(x => x.SOId), LastId = g.Max(x => x.SOId) })
                .FirstOrDefaultAsync();

            int? prevId = null, nextId = null;
            if (currentId > 0)
            {
                prevId = await _context.SalesOrders.AsNoTracking().Where(x => x.SOId < currentId).OrderByDescending(x => x.SOId).Select(x => (int?)x.SOId).FirstOrDefaultAsync();
                nextId = await _context.SalesOrders.AsNoTracking().Where(x => x.SOId > currentId).OrderBy(x => x.SOId).Select(x => (int?)x.SOId).FirstOrDefaultAsync();
            }
            else
            {
                prevId = minMax?.LastId;
                nextId = minMax?.FirstId;
            }

            ViewBag.NavFirstId = minMax?.FirstId ?? 0;
            ViewBag.NavLastId = minMax?.LastId ?? 0;
            ViewBag.NavPrevId = prevId ?? 0;
            ViewBag.NavNextId = nextId ?? 0;
        }

        // =========================
        // Edit — GET: فتح أمر بيع قديم للعرض/التعديل
        // =========================
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            // تحقق من رقم الأمر
            if (id <= 0)
                return BadRequest("رقم أمر البيع غير صالح.");

            // قراءة هيدر أمر البيع + بيانات العميل + سطور الأمر
            var model = await _context.SalesOrders
                .Include(so => so.Customer)   // العميل المرتبط بأمر البيع
                .Include(so => so.Lines)      // سطور أمر البيع
                .AsNoTracking()               // قراءة فقط بدون تتبع للتعديل
                .FirstOrDefaultAsync(so => so.SOId == id);

            if (model == null)
                return NotFound();            // الأمر غير موجود

            // تعبئة القوائم المنسدلة (العملاء + المخازن + …)
            await PopulateDropDownsAsync();

            // فتح شاشة أمر البيع (Edit = شاشة عرض + زرار فتح/ترحيل/طباعة)
            return View(model);
        }



        // =========================
        // Edit — POST: حفظ تعديل أمر البيع مع RowVersion
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, SalesOrder order)
        {
            // تأكد أن رقم الأمر في الرابط هو نفس الموجود في الموديل
            if (id != order.SOId)
                return NotFound();

            // لو فيه أخطاء تحقق نرجع لنفس الفورم
            if (!ModelState.IsValid)
            {
                // لازم نرجّع القوائم المنسدلة تاني
                await PopulateDropDownsAsync();
                return View(order);
            }

            try
            {
                var existing = await _context.SalesOrders.AsNoTracking().FirstOrDefaultAsync(o => o.SOId == id);
                var oldValues = existing != null ? System.Text.Json.JsonSerializer.Serialize(new { existing.SODate, existing.CustomerId, existing.WarehouseId, existing.ExpectedItemsTotal }) : null;
                // تحديث وقت آخر تعديل (لو الخاصية موجودة في الموديل)
                order.UpdatedAt = DateTime.Now;

                // إعداد RowVersion الأصلي للتعامل مع التعارض (لو الخاصية موجودة في الموديل)
                _context.Entry(order)
                        .Property(x => x.RowVersion)
                        .OriginalValue = order.RowVersion;

                // تحديث الكيان في الـ DbContext
                _context.Update(order);

                // حفظ التغييرات فعلياً في SQL Server
                await _context.SaveChangesAsync();

                var newValues = System.Text.Json.JsonSerializer.Serialize(new { order.SODate, order.CustomerId, order.WarehouseId, order.ExpectedItemsTotal });
                await _activityLogger.LogAsync(UserActionType.Edit, "SalesOrder", order.SOId, $"تعديل أمر بيع رقم {order.SOId}", oldValues, newValues);

                TempData["Msg"] = "تم تعديل أمر البيع بنجاح.";
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateConcurrencyException)
            {
                // لو الأمر اختفى أثناء الحفظ (اتحذف مثلاً)
                bool exists = await _context.SalesOrders.AnyAsync(e => e.SOId == id);
                if (!exists)
                    return NotFound();

                // تعارض حقيقي: حد آخر عدّل نفس أمر البيع
                ModelState.AddModelError(
                    string.Empty,
                    "تعذّر حفظ التعديلات بسبب تعديل متزامن على نفس أمر البيع. أعد تحميل الصفحة وحاول مرة أخرى.");

                await PopulateDropDownsAsync();
                return View(order);
            }
        }

        #region أمر البيع — SaveHeader / سطور / تحويل إلى فاتورة مبيعات

        public class SalesOrderHeaderDto
        {
            public int SOId { get; set; }
            public int CustomerId { get; set; }
            public int WarehouseId { get; set; }
            public string? Notes { get; set; }
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> SaveHeader([FromBody] SalesOrderHeaderDto dto)
        {
            if (dto == null)
                return BadRequest("حدث خطأ فى البيانات المرسلة.");
            if (dto.CustomerId <= 0)
                return BadRequest("يجب اختيار العميل قبل حفظ أمر البيع.");
            if (dto.WarehouseId <= 0)
                return BadRequest("يجب اختيار المخزن قبل حفظ أمر البيع.");

            var now = DateTime.Now;
            var userName = GetCurrentUserDisplayName();

            if (dto.SOId == 0)
            {
                var so = new SalesOrder
                {
                    SODate = now.Date,
                    CustomerId = dto.CustomerId,
                    WarehouseId = dto.WarehouseId,
                    Status = "Draft",
                    IsConverted = false,
                    Notes = dto.Notes,
                    CreatedAt = now,
                    CreatedBy = userName,
                    TotalQtyRequested = 0,
                    ExpectedItemsTotal = 0m
                };
                _context.SalesOrders.Add(so);
                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    SOId = so.SOId,
                    orderNumber = so.SOId.ToString(),
                    orderDate = so.SODate.ToString("yyyy/MM/dd"),
                    orderTime = so.CreatedAt.ToString("HH:mm"),
                    status = so.Status,
                    isConverted = so.IsConverted,
                    createdBy = so.CreatedBy
                });
            }

            var existing = await _context.SalesOrders.FirstOrDefaultAsync(o => o.SOId == dto.SOId);
            if (existing == null)
                return NotFound("لم يتم العثور على أمر البيع المطلوب.");
            if (existing.IsConverted)
                return BadRequest("لا يمكن تعديل أمر بيع تم تحويله بالفعل إلى فاتورة مبيعات.");

            existing.CustomerId = dto.CustomerId;
            existing.WarehouseId = dto.WarehouseId;
            existing.Notes = dto.Notes;
            existing.UpdatedAt = now;
            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                SOId = existing.SOId,
                orderNumber = existing.SOId.ToString(),
                orderDate = existing.SODate.ToString("yyyy/MM/dd"),
                orderTime = existing.CreatedAt.ToString("HH:mm"),
                status = existing.Status,
                isConverted = existing.IsConverted,
                createdBy = existing.CreatedBy
            });
        }

        public class AddLineJsonDto
        {
            public int SOId { get; set; }
            public int prodId { get; set; }
            public int qty { get; set; }
            public decimal requestRetailPrice { get; set; }
            public decimal salesDiscountPct { get; set; }
            public decimal expectedUnitPrice { get; set; }
            public string? BatchNo { get; set; }
            public string? expiryText { get; set; }
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> AddLineJson([FromBody] AddLineJsonDto dto)
        {
            if (dto == null || dto.SOId <= 0 || dto.prodId <= 0)
                return BadRequest(new { ok = false, message = "بيانات الأمر/الصنف غير صحيحة." });
            if (dto.qty <= 0)
                return BadRequest(new { ok = false, message = "الكمية يجب أن تكون أكبر من صفر." });

            var disc = Math.Clamp(dto.salesDiscountPct, 0, 100);
            var expectedPrice = dto.expectedUnitPrice > 0 ? dto.expectedUnitPrice : dto.requestRetailPrice * (1m - disc / 100m);

            DateTime? expiry = null;
            if (!string.IsNullOrWhiteSpace(dto.expiryText))
            {
                var parts = dto.expiryText.Trim().Split('/');
                if (parts.Length == 2 && int.TryParse(parts[0], out int mm) && int.TryParse(parts[1], out int yyyy) && mm >= 1 && mm <= 12)
                    expiry = new DateTime(yyyy, mm, 1);
            }
            var batchNo = string.IsNullOrWhiteSpace(dto.BatchNo) ? null : dto.BatchNo.Trim();

            var order = await _context.SalesOrders.FirstOrDefaultAsync(o => o.SOId == dto.SOId);
            if (order == null)
                return NotFound(new { ok = false, message = "أمر البيع غير موجود." });
            if (order.IsConverted)
                return BadRequest(new { ok = false, message = "لا يمكن إضافة سطور: الأمر محوّل إلى فاتورة مبيعات." });

            var product = await _context.Products.AsNoTracking().FirstOrDefaultAsync(p => p.ProdId == dto.prodId);
            if (product == null)
                return BadRequest(new { ok = false, message = "الصنف غير موجود." });

            var currentLines = await _context.SOLines.Where(l => l.SOId == dto.SOId).ToListAsync();
            var nextLineNo = (currentLines.Any() ? currentLines.Max(l => l.LineNo) : 0) + 1;

            var newLine = new SOLine
            {
                SOId = dto.SOId,
                LineNo = nextLineNo,
                ProdId = dto.prodId,
                QtyRequested = dto.qty,
                RequestedRetailPrice = dto.requestRetailPrice,
                SalesDiscountPct = disc,
                ExpectedUnitPrice = expectedPrice,
                PreferredBatchNo = batchNo,
                PreferredExpiry = expiry?.Date,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = GetCurrentUserDisplayName()
            };
            _context.SOLines.Add(newLine);
            await _context.SaveChangesAsync();
            await _docTotals.RecalcSalesOrderTotalsAsync(dto.SOId);

            var linesNow = await _context.SOLines.Where(l => l.SOId == dto.SOId).OrderBy(l => l.LineNo).ToListAsync();
            var prodIds = linesNow.Select(l => l.ProdId).Distinct().ToList();
            var prodMap = await _context.Products.Where(p => prodIds.Contains(p.ProdId)).Select(p => new { p.ProdId, p.ProdName }).ToDictionaryAsync(x => x.ProdId, x => x.ProdName ?? "");

            var linesDto = linesNow.Select(l =>
            {
                var name = prodMap.TryGetValue(l.ProdId, out var n) ? n : "";
                var lv = l.QtyRequested * l.ExpectedUnitPrice;
                return new { lineNo = l.LineNo, prodId = l.ProdId, prodName = name, qty = l.QtyRequested, requestRetailPrice = l.RequestedRetailPrice, salesDiscountPct = l.SalesDiscountPct, expectedUnitPrice = l.ExpectedUnitPrice, batchNo = l.PreferredBatchNo, expiry = l.PreferredExpiry?.ToString("yyyy-MM-dd"), lineValue = lv };
            }).ToList();

            var soHeader = await _context.SalesOrders.FirstOrDefaultAsync(o => o.SOId == dto.SOId);
            return Json(new
            {
                ok = true,
                message = "تم إضافة السطر بنجاح.",
                lines = linesDto,
                totals = new { totalLines = linesNow.Count, totalQty = linesNow.Sum(x => x.QtyRequested), expectedItemsTotal = soHeader?.ExpectedItemsTotal ?? 0m }
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetLinesJson(int id)
        {
            if (id <= 0)
                return BadRequest(new { ok = false, message = "رقم الأمر غير صحيح." });
            var order = await _context.SalesOrders.AsNoTracking().FirstOrDefaultAsync(o => o.SOId == id);
            if (order == null)
                return NotFound(new { ok = false, message = "أمر البيع غير موجود." });

            var linesNow = await _context.SOLines.Where(l => l.SOId == id).OrderBy(l => l.LineNo).ToListAsync();
            var prodIds = linesNow.Select(l => l.ProdId).Distinct().ToList();
            var prodMap = await _context.Products.Where(p => prodIds.Contains(p.ProdId)).Select(p => new { p.ProdId, p.ProdName }).ToDictionaryAsync(x => x.ProdId, x => x.ProdName ?? "");

            var linesDto = linesNow.Select(l =>
            {
                var name = prodMap.TryGetValue(l.ProdId, out var n) ? n : "";
                var lv = l.QtyRequested * l.ExpectedUnitPrice;
                return new { lineNo = l.LineNo, prodId = l.ProdId, prodName = name, qty = l.QtyRequested, requestRetailPrice = l.RequestedRetailPrice, salesDiscountPct = l.SalesDiscountPct, expectedUnitPrice = l.ExpectedUnitPrice, batchNo = l.PreferredBatchNo, expiry = l.PreferredExpiry?.ToString("yyyy-MM-dd"), lineValue = lv };
            }).ToList();

            return Json(new { ok = true, lines = linesDto, totals = new { totalLines = linesNow.Count, totalQty = linesNow.Sum(x => x.QtyRequested), expectedItemsTotal = order.ExpectedItemsTotal } });
        }

        public class RemoveLineJsonDto { public int SOId { get; set; } public int LineNo { get; set; } }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> RemoveLineJson([FromBody] RemoveLineJsonDto dto)
        {
            if (dto == null || dto.SOId <= 0 || dto.LineNo <= 0)
                return BadRequest(new { ok = false, message = "بيانات المسح غير صحيحة." });

            var order = await _context.SalesOrders.FirstOrDefaultAsync(o => o.SOId == dto.SOId);
            if (order == null)
                return NotFound(new { ok = false, message = "أمر البيع غير موجود." });
            if (order.IsConverted)
                return BadRequest(new { ok = false, message = "لا يمكن تعديل أمر محوّل." });

            var line = await _context.SOLines.FirstOrDefaultAsync(l => l.SOId == dto.SOId && l.LineNo == dto.LineNo);
            if (line == null)
                return NotFound(new { ok = false, message = "السطر غير موجود." });

            _context.SOLines.Remove(line);
            await _context.SaveChangesAsync();
            await _docTotals.RecalcSalesOrderTotalsAsync(dto.SOId);

            var linesNow = await _context.SOLines.Where(l => l.SOId == dto.SOId).OrderBy(l => l.LineNo).ToListAsync();
            var prodIds = linesNow.Select(l => l.ProdId).Distinct().ToList();
            var prodMap = await _context.Products.Where(p => prodIds.Contains(p.ProdId)).Select(p => new { p.ProdId, p.ProdName }).ToDictionaryAsync(x => x.ProdId, x => x.ProdName ?? "");
            var linesDto = linesNow.Select(l =>
            {
                var name = prodMap.TryGetValue(l.ProdId, out var n) ? n : "";
                return new { lineNo = l.LineNo, prodId = l.ProdId, prodName = name, qty = l.QtyRequested, requestRetailPrice = l.RequestedRetailPrice, salesDiscountPct = l.SalesDiscountPct, expectedUnitPrice = l.ExpectedUnitPrice, batchNo = l.PreferredBatchNo, expiry = l.PreferredExpiry?.ToString("yyyy-MM-dd"), lineValue = l.QtyRequested * l.ExpectedUnitPrice };
            }).ToList();
            var soHeader = await _context.SalesOrders.FirstOrDefaultAsync(o => o.SOId == dto.SOId);
            return Json(new { ok = true, message = "تم حذف السطر.", lines = linesDto, totals = new { totalLines = linesNow.Count, totalQty = linesNow.Sum(x => x.QtyRequested), expectedItemsTotal = soHeader?.ExpectedItemsTotal ?? 0m } });
        }

        public class ClearAllLinesJsonDto { public int SOId { get; set; } }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> ClearAllLinesJson([FromBody] ClearAllLinesJsonDto dto)
        {
            if (dto == null || dto.SOId <= 0)
                return BadRequest(new { ok = false, message = "بيانات المسح غير صحيحة." });

            var order = await _context.SalesOrders.FirstOrDefaultAsync(o => o.SOId == dto.SOId);
            if (order == null)
                return NotFound(new { ok = false, message = "أمر البيع غير موجود." });
            if (order.IsConverted)
                return BadRequest(new { ok = false, message = "لا يمكن تعديل أمر محوّل." });

            var lines = await _context.SOLines.Where(l => l.SOId == dto.SOId).ToListAsync();
            if (lines.Count == 0)
                return Json(new { ok = true, message = "لا توجد أصناف لمسحها.", lines = Array.Empty<object>(), totals = new { totalLines = 0, totalQty = 0, expectedItemsTotal = 0m } });

            _context.SOLines.RemoveRange(lines);
            await _context.SaveChangesAsync();
            await _docTotals.RecalcSalesOrderTotalsAsync(dto.SOId);
            return Json(new { ok = true, message = "تم مسح جميع الأصناف.", lines = Array.Empty<object>(), totals = new { totalLines = 0, totalQty = 0, expectedItemsTotal = 0m } });
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> RecalcTotals(int id)
        {
            if (id <= 0)
                return BadRequest(new { ok = false, message = "رقم الأمر غير صحيح." });
            await _docTotals.RecalcSalesOrderTotalsAsync(id);
            var so = await _context.SalesOrders.AsNoTracking().FirstOrDefaultAsync(o => o.SOId == id);
            return Json(new { ok = true, expectedItemsTotal = so?.ExpectedItemsTotal ?? 0m, totalQtyRequested = so?.TotalQtyRequested ?? 0 });
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> ConvertToSalesInvoice(int id)
        {
            bool isAjax = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                || (Request.Headers["Accept"].ToString()?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true);

            try
            {
                var order = await _context.SalesOrders.Include(o => o.Customer).Include(o => o.Warehouse).Include(o => o.Lines).FirstOrDefaultAsync(o => o.SOId == id);
                if (order == null)
                {
                    if (isAjax) return NotFound(new { ok = false, message = "أمر البيع غير موجود." });
                    TempData["Error"] = "أمر البيع غير موجود.";
                    return RedirectToAction(nameof(Index));
                }
                if (order.IsConverted)
                {
                    if (isAjax) return BadRequest(new { ok = false, message = "هذا الأمر تم تحويله بالفعل إلى فاتورة مبيعات." });
                    TempData["Error"] = "هذا الأمر تم تحويله بالفعل.";
                    return RedirectToAction(nameof(Show), new { id = order.SOId, frame = 1 });
                }
                if (order.CustomerId <= 0) { var msg = "يجب اختيار العميل قبل التحويل."; if (isAjax) return BadRequest(new { ok = false, message = msg }); TempData["Error"] = msg; return RedirectToAction(nameof(Show), new { id = order.SOId, frame = 1 }); }
                if (order.WarehouseId <= 0) { var msg = "يجب اختيار المخزن قبل التحويل."; if (isAjax) return BadRequest(new { ok = false, message = msg }); TempData["Error"] = msg; return RedirectToAction(nameof(Show), new { id = order.SOId, frame = 1 }); }
                if (order.Lines == null || !order.Lines.Any()) { var msg = "لا يمكن تحويل أمر بيع بدون سطور."; if (isAjax) return BadRequest(new { ok = false, message = msg }); TempData["Error"] = msg; return RedirectToAction(nameof(Show), new { id = order.SOId, frame = 1 }); }

                await using var tx = await _context.Database.BeginTransactionAsync();
                var now = DateTime.Now;
                var userName = GetCurrentUserDisplayName();

                var si = new SalesInvoice
                {
                    SIDate = now.Date,
                    SITime = now.TimeOfDay,
                    CustomerId = order.CustomerId,
                    WarehouseId = order.WarehouseId,
                    RefSOId = order.SOId,
                    Status = "مسودة",
                    IsPosted = false,
                    CreatedBy = userName,
                    CreatedAt = now
                };
                _context.SalesInvoices.Add(si);
                await _context.SaveChangesAsync();

                var soLines = await _context.SOLines.Include(l => l.Product).Where(l => l.SOId == order.SOId).OrderBy(l => l.LineNo).ToListAsync();
                int lineNo = 1;
                foreach (var sl in soLines)
                {
                    var priceRetail = sl.RequestedRetailPrice > 0 ? sl.RequestedRetailPrice : (sl.Product?.PriceRetail ?? 0m);
                    var unitSale = priceRetail * (1m - sl.SalesDiscountPct / 100m);
                    var lineTotalAfterDiscount = sl.QtyRequested * unitSale;
                    var sil = new SalesInvoiceLine
                    {
                        SIId = si.SIId,
                        LineNo = lineNo++,
                        ProdId = sl.ProdId,
                        Qty = sl.QtyRequested,
                        PriceRetail = priceRetail,
                        Disc1Percent = sl.SalesDiscountPct,
                        UnitSalePrice = unitSale,
                        LineTotalAfterDiscount = lineTotalAfterDiscount,
                        TaxPercent = 0,
                        TaxValue = 0,
                        LineNetTotal = lineTotalAfterDiscount,
                        BatchNo = sl.PreferredBatchNo ?? "",
                        Expiry = sl.PreferredExpiry
                    };
                    _context.SalesInvoiceLines.Add(sil);
                }
                await _context.SaveChangesAsync();

                await _docTotals.RecalcSalesInvoiceTotalsAsync(si.SIId);
                await _context.SaveChangesAsync();

                order.IsConverted = true;
                order.Status = "Converted";
                order.UpdatedAt = now;
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                try { await _activityLogger.LogAsync(UserActionType.Post, "SalesOrder", order.SOId, $"تحويل أمر بيع رقم {order.SOId} إلى فاتورة مبيعات رقم {si.SIId}"); } catch { }

                if (isAjax)
                    return Ok(new { ok = true, message = $"تم تحويل أمر البيع بنجاح إلى فاتورة مبيعات رقم {si.SIId}.", soId = order.SOId, siId = si.SIId, status = order.Status, isConverted = true });
                TempData["Success"] = $"تم تحويل أمر البيع إلى فاتورة مبيعات رقم {si.SIId}.";
                return RedirectToAction("Show", "SalesInvoices", new { id = si.SIId, frame = 1 });
            }
            catch (Exception ex)
            {
                var msg = string.IsNullOrWhiteSpace(ex.Message) ? "حدث خطأ أثناء التحويل." : ex.Message;
                if (ex.InnerException != null && !string.IsNullOrWhiteSpace(ex.InnerException.Message)) msg = msg + " " + ex.InnerException.Message;
                if (isAjax) return BadRequest(new { ok = false, message = "فشل التحويل: " + msg });
                TempData["Error"] = "فشل التحويل: " + msg;
                return RedirectToAction(nameof(Show), new { id, frame = 1 });
            }
        }

        #endregion
    }
}
