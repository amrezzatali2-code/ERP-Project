using ERP.Data;                             // كائن الاتصال بقاعدة البيانات AppDbContext
using ERP.Filters;
using ERP.Infrastructure;                  // PagedResult + ApplySearchSort + UserActivityLogger
using ERP.Models;                          // ProductGroup, UserActionType
using ERP.Security;
using ERP.Services.Caching;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;          // Dictionary
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;             // Expressions
using System.Text;                         // StringBuilder للتصدير
using System.Threading.Tasks;
using System.IO;
using ClosedXML.Excel;

namespace ERP.Controllers
{
    /// <summary>
    /// إدارة جدول مجموعات الأصناف (ProductGroup)
    /// كل صف = مجموعة أصناف لها اسم ووصف وحالة تفعيل.
    /// </summary>
    [RequirePermission("ProductGroups.Index")]
    public class ProductGroupsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IUserActivityLogger _activityLogger;
        private readonly ILookupCacheService _lookupCache;

        public ProductGroupsController(AppDbContext context, IUserActivityLogger activityLogger, ILookupCacheService lookupCache)
        {
            _context = context;
            _activityLogger = activityLogger;
            _lookupCache = lookupCache;
        }

        // =========================
        // دالة خاصة لبناء استعلام مجموعات الأصناف
        // (بحث + فلتر كود من/إلى + فلتر تاريخ اختياري + ترتيب)
        // =========================
        private IQueryable<ProductGroup> BuildGroupsQuery(
            string? search,
            string? searchBy,
            string? searchMode,
            string? sort,
            string? dir,
            int? fromCode,
            int? toCode,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate)
        {
            // 1) الاستعلام الأساسي (قراءة فقط لتحسين الأداء)
            IQueryable<ProductGroup> q =
                _context.ProductGroups.AsNoTracking();

            // 2) فلتر من/إلى (على ProductGroupId)
            if (fromCode.HasValue)
                q = q.Where(x => x.ProductGroupId >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(x => x.ProductGroupId <= toCode.Value);

            // 3) فلتر التاريخ (CreatedAt) لو مفعّل
            if (useDateRange)
            {
                if (fromDate.HasValue)
                    q = q.Where(x => x.CreatedAt >= fromDate.Value);

                if (toDate.HasValue)
                    q = q.Where(x => x.CreatedAt <= toDate.Value);
            }

            // 4) بحث مخصص لـ active و created
            string? searchForSort = search;
            string? searchByForSort = searchBy;
            if (!string.IsNullOrWhiteSpace(search) && !string.IsNullOrWhiteSpace(searchBy))
            {
                var sb = searchBy.Trim().ToLowerInvariant();
                var text = search!.Trim();
                if (sb == "active")
                {
                    if (text.Contains("نعم") || text.Contains("yes") || text.Equals("1", StringComparison.OrdinalIgnoreCase))
                        q = q.Where(x => x.IsActive);
                    else if (text.Contains("لا") || text.Contains("no") || text.Equals("0", StringComparison.OrdinalIgnoreCase))
                        q = q.Where(x => !x.IsActive);
                    searchForSort = null;
                    searchByForSort = null;
                }
                else if (sb == "created" && DateTime.TryParse(text, out var dtCreated))
                {
                    q = q.Where(x => x.CreatedAt.Date == dtCreated.Date);
                    searchForSort = null;
                    searchByForSort = null;
                }
                else if (sb == "updated" && DateTime.TryParse(text, out var dtUpdated))
                {
                    q = q.Where(x => x.UpdatedAt != null && x.UpdatedAt.Value.Date == dtUpdated.Date);
                    searchForSort = null;
                    searchByForSort = null;
                }
            }

            // 5) الحقول النصية للبحث
            var stringFields =
                new Dictionary<string, Expression<Func<ProductGroup, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["name"] = x => x.Name ?? string.Empty,
                    ["desc"] = x => x.Description ?? string.Empty,
                    ["active"] = x => x.IsActive ? "نعم" : "لا"
                };

            // 6) الحقول العددية للبحث
            var intFields =
                new Dictionary<string, Expression<Func<ProductGroup, int>>>()
                {
                    ["id"] = x => x.ProductGroupId
                };

            // 7) حقول الترتيب
            var orderFields =
                new Dictionary<string, Expression<Func<ProductGroup, object>>>()
                {
                    ["id"] = x => x.ProductGroupId,
                    ["name"] = x => x.Name ?? string.Empty,
                    ["active"] = x => x.IsActive,
                    ["created"] = x => x.CreatedAt,
                    ["updated"] = x => x.UpdatedAt ?? x.CreatedAt
                };

            // 8) تطبيق البحث + الترتيب باستخدام الإكستنشن الموحد
            q = q.ApplySearchSort(
                searchForSort,
                searchByForSort,
                sort,
                dir,
                stringFields,
                intFields,
                orderFields,
                defaultSearchBy: "all",
                defaultSortBy: "id",
                searchMode: searchMode
            );

            return q;
        }

        private static readonly char[] _filterSep = { '|', ',', ';' };

        private static IQueryable<ProductGroup> ApplyGroupIdNumericExpr(IQueryable<ProductGroup> q, string? rawExpr)
        {
            if (string.IsNullOrWhiteSpace(rawExpr))
                return q;

            var expr = rawExpr.Trim();

            if (expr.StartsWith("<=") && expr.Length > 2
                && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var le))
                return q.Where(x => x.ProductGroupId <= le);

            if (expr.StartsWith(">=") && expr.Length > 2
                && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var ge))
                return q.Where(x => x.ProductGroupId >= ge);

            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1
                && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var lt))
                return q.Where(x => x.ProductGroupId < lt);

            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1
                && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var gt))
                return q.Where(x => x.ProductGroupId > gt);

            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0))
                && !expr.StartsWith("-"))
            {
                var separator = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b)
                        (a, b) = (b, a);
                    return q.Where(x => x.ProductGroupId >= a && x.ProductGroupId <= b);
                }
            }

            if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var eq))
                return q.Where(x => x.ProductGroupId == eq);

            return q;
        }

        private IQueryable<ProductGroup> ApplyColumnFilters(
            IQueryable<ProductGroup> q,
            string? filterCol_id,
            string? filterCol_idExpr,
            string? filterCol_name,
            string? filterCol_active,
            string? filterCol_created,
            string? filterCol_updated)
        {
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    q = q.Where(x => ids.Contains(x.ProductGroupId));
            }
            else if (!string.IsNullOrWhiteSpace(filterCol_idExpr))
            {
                q = ApplyGroupIdNumericExpr(q, filterCol_idExpr);
            }
            if (!string.IsNullOrWhiteSpace(filterCol_name))
            {
                var terms = filterCol_name.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
                if (terms.Count > 0)
                    q = q.Where(x => x.Name != null && terms.Contains(x.Name));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_active))
            {
                var parts = filterCol_active.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim().ToLowerInvariant()).ToHashSet();
                if (parts.Contains("true") && !parts.Contains("false"))
                    q = q.Where(x => x.IsActive);
                else if (parts.Contains("false") && !parts.Contains("true"))
                    q = q.Where(x => !x.IsActive);
            }
            if (!string.IsNullOrWhiteSpace(filterCol_created))
            {
                var terms = filterCol_created.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
                if (terms.Count > 0)
                    q = q.Where(x => terms.Any(t => x.CreatedAt.ToString("yyyy-MM-dd HH:mm").Contains(t)));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_updated))
            {
                var terms = filterCol_updated.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
                if (terms.Count > 0)
                    q = q.Where(x => x.UpdatedAt != null && terms.Any(t => x.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm").Contains(t)));
            }
            return q;
        }

        /// <summary>قيم مميزة للعمود (للوحة فلتر الأعمدة بنمط Excel).</summary>
        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var col = (column ?? "").Trim().ToLowerInvariant();
            var q = _context.ProductGroups.AsNoTracking();

            if (col == "id")
            {
                var ids = await q.Select(x => x.ProductGroupId).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (col == "name")
            {
                var list = await q.Where(x => x.Name != null).Select(x => x.Name!).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm))
                    list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (col == "active")
            {
                return Json(new[] { new { value = "true", display = "نعم" }, new { value = "false", display = "لا" } });
            }
            if (col == "created")
            {
                var dates = await q.Select(x => x.CreatedAt).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                var list = dates.Select(d => d.ToString("yyyy-MM-dd HH:mm")).Distinct().ToList();
                if (!string.IsNullOrEmpty(searchTerm))
                    list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (col == "updated")
            {
                var dates = await q.Where(x => x.UpdatedAt != null).Select(x => x.UpdatedAt!.Value).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                var list = dates.Select(d => d.ToString("yyyy-MM-dd HH:mm")).Distinct().ToList();
                if (!string.IsNullOrEmpty(searchTerm))
                    list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            return Json(new List<object>());
        }

        // =========================
        // Index — قائمة مجموعات الأصناف
        // =========================
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",       // all | name | id | desc
            string? searchMode = "contains",
            string? sort = "id",            // id | name | active | created
            string? dir = "asc",
            int page = 1,
            int pageSize = 25,
            int? fromCode = null,
            int? toCode = null,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_name = null,
            string? filterCol_active = null,
            string? filterCol_created = null,
            string? filterCol_updated = null)
        {
            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm != "starts" && sm != "ends")
                sm = "contains";

            var q = BuildGroupsQuery(
                search,
                searchBy,
                sm,
                sort,
                dir,
                fromCode,
                toCode,
                useDateRange,
                fromDate,
                toDate);

            q = ApplyColumnFilters(q, filterCol_id, filterCol_idExpr, filterCol_name, filterCol_active, filterCol_created, filterCol_updated);

            var totalCount = await q.CountAsync();
            var activeCount = await q.CountAsync(x => x.IsActive);
            var inactiveCount = totalCount - activeCount;

            var model = await PagedResult<ProductGroup>.CreateAsync(q, page, pageSize);

            model.Search = search ?? "";
            model.SearchBy = searchBy ?? "all";
            model.SortColumn = sort ?? "id";
            model.SortDescending = (dir?.ToLower() == "desc");
            model.UseDateRange = useDateRange;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.CodeFrom = fromCode;
            ViewBag.CodeTo = toCode;
            ViewBag.SearchMode = sm;
            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_IdExpr = filterCol_idExpr;
            ViewBag.FilterCol_Name = filterCol_name;
            ViewBag.FilterCol_Active = filterCol_active;
            ViewBag.FilterCol_Created = filterCol_created;
            ViewBag.FilterCol_Updated = filterCol_updated;
            ViewBag.ActiveCount = activeCount;
            ViewBag.InactiveCount = inactiveCount;
            ViewBag.TotalCount = totalCount;

            ViewBag.DateField = "CreatedAt";

            return View(model);
        }

        // =========================
        // Export — تصدير Excel / CSV
        // =========================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy,
            string? searchMode,
            string? sort,
            string? dir,
            int? fromCode = null,
            int? toCode = null,
            int? codeFrom = null,
            int? codeTo = null,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? format = "excel",
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_name = null,
            string? filterCol_active = null,
            string? filterCol_created = null,
            string? filterCol_updated = null)
        {
            if (!fromCode.HasValue && codeFrom.HasValue) fromCode = codeFrom;
            if (!toCode.HasValue && codeTo.HasValue) toCode = codeTo;

            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm != "starts" && sm != "ends")
                sm = "contains";

            var q = BuildGroupsQuery(
                search,
                searchBy,
                sm,
                sort,
                dir,
                fromCode,
                toCode,
                useDateRange,
                fromDate,
                toDate);

            q = ApplyColumnFilters(q, filterCol_id, filterCol_idExpr, filterCol_name, filterCol_active, filterCol_created, filterCol_updated);

            var list = await q.ToListAsync();

            if (string.Equals(format, "excel", StringComparison.OrdinalIgnoreCase))
            {
                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("مجموعات الأصناف"));

                int row = 1;
                worksheet.Cell(row, 1).Value = "كود المجموعة";
                worksheet.Cell(row, 2).Value = "اسم المجموعة";
                worksheet.Cell(row, 3).Value = "الوصف";
                worksheet.Cell(row, 4).Value = "مفعّلة؟";
                worksheet.Cell(row, 5).Value = "تاريخ الإنشاء";

                var header = worksheet.Range(row, 1, row, 5);
                header.Style.Font.Bold = true;

                foreach (var g in list)
                {
                    row++;
                    worksheet.Cell(row, 1).Value = g.ProductGroupId;
                    worksheet.Cell(row, 2).Value = g.Name;
                    worksheet.Cell(row, 3).Value = g.Description ?? string.Empty;
                    worksheet.Cell(row, 4).Value = g.IsActive ? "نعم" : "لا";
                    worksheet.Cell(row, 5).Value = g.CreatedAt;
                }

                worksheet.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                stream.Position = 0;

                var fileNameXlsx = ExcelExportNaming.ArabicTimestampedFileName("مجموعات الأصناف", ".xlsx");
                const string contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                return File(stream.ToArray(), contentType, fileNameXlsx);
            }
            else
            {
                var sb = new StringBuilder();
                sb.AppendLine("كود المجموعة,اسم المجموعة,الوصف,مفعّلة؟,تاريخ الإنشاء");

                foreach (var g in list)
                {
                    string createdText = g.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                    string desc = (g.Description ?? "").Replace("\"", "\"\"");

                    var line = string.Join(",",
                        g.ProductGroupId,
                        $"\"{(g.Name ?? "").Replace("\"", "\"\"")}\"",
                        $"\"{desc}\"",
                        $"\"{(g.IsActive ? "مفعّلة" : "موقوفة")}\"",
                        createdText
                    );

                    sb.AppendLine(line);
                }

                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString());
                var fileNameCsv = ExcelExportNaming.ArabicTimestampedFileName("مجموعات الأصناف", ".csv");
                return File(bytes, "text/csv; charset=utf-8", fileNameCsv);
            }
        }

        // =========================
        // Details — عرض التفاصيل
        // =========================
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
                return NotFound();

            var group = await _context.ProductGroups
                                      .AsNoTracking()
                                      .FirstOrDefaultAsync(g => g.ProductGroupId == id.Value);
            if (group == null)
                return NotFound();

            return View(group);
        }

        // =========================
        // Create — GET
        // =========================
        public IActionResult Create()
        {
            return View();
        }

        // =========================
        // Create — POST
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ProductGroup group)
        {
            if (!ModelState.IsValid)
                return View(group);

            group.CreatedAt = DateTime.Now;

            _context.ProductGroups.Add(group);
            await _context.SaveChangesAsync();

            _lookupCache.ClearProductGroupsCache();

            await _activityLogger.LogAsync(UserActionType.Create, "ProductGroup", group.ProductGroupId, $"إنشاء مجموعة أصناف: {group.Name}");

            TempData["Msg"] = "تم إضافة مجموعة أصناف جديدة بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // Edit — GET
        // =========================
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
                return NotFound();

            var group = await _context.ProductGroups
                                      .FirstOrDefaultAsync(g => g.ProductGroupId == id.Value);
            if (group == null)
                return NotFound();

            return View(group);
        }

        // =========================
        // Edit — POST
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ProductGroup group)
        {
            if (id != group.ProductGroupId)
                return NotFound();

            if (!ModelState.IsValid)
                return View(group);

            try
            {
                var existing = await _context.ProductGroups.AsNoTracking().FirstOrDefaultAsync(g => g.ProductGroupId == id);
                var oldValues = existing != null ? System.Text.Json.JsonSerializer.Serialize(new { existing.Name }) : null;
                group.UpdatedAt = DateTime.Now;
                _context.Update(group);
                await _context.SaveChangesAsync();

                _lookupCache.ClearProductGroupsCache();

                var newValues = System.Text.Json.JsonSerializer.Serialize(new { group.Name });
                await _activityLogger.LogAsync(UserActionType.Edit, "ProductGroup", id, $"تعديل مجموعة أصناف: {group.Name}", oldValues, newValues);

                TempData["Msg"] = "تم تعديل مجموعة الأصناف بنجاح.";
            }
            catch (DbUpdateConcurrencyException)
            {
                bool exists = await _context.ProductGroups
                                            .AnyAsync(e => e.ProductGroupId == id);
                if (!exists)
                    return NotFound();

                ModelState.AddModelError(
                    string.Empty,
                    "تعذر الحفظ بسبب تعارض في التعديل. أعد تحميل الصفحة وحاول مرة أخرى.");
                return View(group);
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================
        // Delete — GET (تأكيد الحذف)
        // =========================
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
                return NotFound();

            var group = await _context.ProductGroups
                                      .AsNoTracking()
                                      .FirstOrDefaultAsync(g => g.ProductGroupId == id.Value);
            if (group == null)
                return NotFound();

            return View(group);
        }

        // =========================
        // Delete — POST
        // =========================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var group = await _context.ProductGroups
                                      .FirstOrDefaultAsync(g => g.ProductGroupId == id);
            if (group == null)
                return NotFound();

            var oldValues = System.Text.Json.JsonSerializer.Serialize(new { group.Name });
            _context.ProductGroups.Remove(group);
            await _context.SaveChangesAsync();

            _lookupCache.ClearProductGroupsCache();

            await _activityLogger.LogAsync(UserActionType.Delete, "ProductGroup", id, $"حذف مجموعة أصناف: {group.Name}", oldValues: oldValues);

            TempData["Msg"] = "تم حذف مجموعة الأصناف.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // BulkDelete — حذف مجموعة مختارة
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["Msg"] = "لم يتم اختيار أي مجموعات للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var ids = selectedIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var n) ? (int?)n : null)
                .Where(n => n.HasValue)
                .Select(n => n!.Value)
                .ToList();

            if (!ids.Any())
            {
                TempData["Msg"] = "لم يتم اختيار أكواد صحيحة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var groups = await _context.ProductGroups
                                       .Where(g => ids.Contains(g.ProductGroupId))
                                       .ToListAsync();

            if (!groups.Any())
            {
                TempData["Msg"] = "لم يتم العثور على المجموعات المحددة.";
                return RedirectToAction(nameof(Index));
            }

            _context.ProductGroups.RemoveRange(groups);
            await _context.SaveChangesAsync();

            _lookupCache.ClearProductGroupsCache();

            TempData["Msg"] = $"تم حذف {groups.Count} مجموعة أصناف.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // DeleteAll — حذف جميع المجموعات (بحذر)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var groups = await _context.ProductGroups.ToListAsync();
            if (!groups.Any())
            {
                TempData["Msg"] = "لا توجد مجموعات لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            _context.ProductGroups.RemoveRange(groups);
            await _context.SaveChangesAsync();

            _lookupCache.ClearProductGroupsCache();

            TempData["Msg"] = $"تم حذف جميع مجموعات الأصناف ({groups.Count}).";
            return RedirectToAction(nameof(Index));
        }
    }
}
