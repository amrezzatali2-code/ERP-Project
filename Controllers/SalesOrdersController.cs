using ERP.Data;                               // تعليق: سياق قاعدة البيانات AppDbContext
using ERP.Filters;
using ERP.Infrastructure;                    // PagedResult + ApplySearchSort + UserActivityLogger
using ERP.Models;                            // SalesOrder, UserActionType
using ERP.Security;
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

        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        public SalesOrdersController(AppDbContext context, IUserActivityLogger activityLogger)
        {
            _context = context;
            _activityLogger = activityLogger;
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
        // Create — GET: فتح شاشة أمر بيع جديد
        // =========================
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            // متغير: موديل أمر بيع جديد بقيم افتراضية
            var model = new SalesOrder
            {
                SODate = DateTime.Today,                          // تاريخ الأمر = تاريخ اليوم
                Status = "Draft",                                 // حالة مبدئية: مسودة
               
                CreatedAt = DateTime.Now,                           // وقت الإنشاء الآن
                CreatedBy = User?.Identity?.Name ?? "system"        // اسم المستخدم الحالي إن وجد
            };

            // تعبئة القوائم المنسدلة (العملاء + المخازن + ... )
            await PopulateDropDownsAsync();

            // نستخدم نفس شاشة Edit لعرض أمر جديد (قراءة + أزرار الحركة)
            return View("Edit", model);
        }






        // =========================
        // Create — POST: حفظ أمر البيع الجديد
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(SalesOrder order)
        {
            // لو فيه أخطاء في الفاليديشن نرجّع نفس الشاشة
            if (!ModelState.IsValid)
            {
                await PopulateDropDownsAsync();   // نرجّع القوائم المنسدلة عشان ما تفضاش
                return View("Edit", order);
            }

            // تأكيد قيم التتبع والحالة
            order.Status ??= "Draft";                             // لو فارغة نخليها مسودة
            
            order.CreatedAt = DateTime.Now;                       // وقت الإنشاء
            order.CreatedBy ??= User?.Identity?.Name ?? "system"; // اسم المستخدم

            // إضافة الهيدر في قاعدة البيانات
            _context.SalesOrders.Add(order);
            await _context.SaveChangesAsync();

            await _activityLogger.LogAsync(UserActionType.Create, "SalesOrder", order.SOId, $"إنشاء أمر بيع رقم {order.SOId}");

            TempData["Msg"] = "تم إنشاء أمر البيع بنجاح.";

            // بعد الحفظ نفتح شاشة Edit للأمر الجديد
            return RedirectToAction(nameof(Edit), new { id = order.SOId });
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

    }
}
