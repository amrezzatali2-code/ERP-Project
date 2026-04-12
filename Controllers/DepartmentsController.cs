using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using ClosedXML.Excel;
using ERP.Data;
using ERP.Filters;
using ERP.Infrastructure;
using ERP.Models;
using ERP.Security;
using ERP.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace ERP.Controllers
{
    [RequirePermission("Departments.Index")]
    public class DepartmentsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IUserActivityLogger _activityLogger;
        private readonly IPermissionService _permissionService;

        public DepartmentsController(AppDbContext db, IUserActivityLogger activityLogger, IPermissionService permissionService)
        {
            _db = db;
            _activityLogger = activityLogger;
            _permissionService = permissionService;
        }

        [HttpGet]
        private IQueryable<Department> BuildQuery(
            string? search,
            string? searchBy,
            string? searchMode,
            string? sort,
            string? dir,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            int? fromCode,
            int? toCode)
        {
            var q = _db.Departments.AsNoTracking();

            if (fromCode.HasValue)
                q = q.Where(d => d.Id >= fromCode.Value);
            if (toCode.HasValue)
                q = q.Where(d => d.Id <= toCode.Value);

            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                DateTime from = fromDate.Value.Date;
                DateTime to = toDate.Value.Date.AddDays(1).AddTicks(-1);
                q = q.Where(d => d.CreatedAt >= from && d.CreatedAt <= to);
            }

            var mode = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (mode != "starts" && mode != "ends") mode = "contains";

            var stringFields = new Dictionary<string, Expression<Func<Department, string?>>>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = d => d.Name,
                ["code"] = d => d.Code,
            };

            var intFields = new Dictionary<string, Expression<Func<Department, int>>>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = d => d.Id,
                ["sortorder"] = d => d.SortOrder
            };

            var orderFields = new Dictionary<string, Expression<Func<Department, object>>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Id"] = d => d.Id,
                ["Name"] = d => d.Name,
                ["Code"] = d => d.Code ?? "",
                ["SortOrder"] = d => d.SortOrder,
                ["IsActive"] = d => d.IsActive,
                ["CreatedAt"] = d => d.CreatedAt,
                ["UpdatedAt"] = d => d.UpdatedAt ?? DateTime.MinValue
            };

            q = q.ApplySearchSort(
                search: search,
                searchBy: searchBy ?? "all",
                sort: sort,
                dir: dir,
                stringFields: stringFields,
                intFields: intFields,
                orderFields: orderFields,
                defaultSearchBy: "all",
                defaultSortBy: "SortOrder",
                searchMode: mode);

            return q;
        }

        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        private static string NormalizeNumericExpr(string? value)
        {
            var text = (value ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text)) return text;

            var chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = chars[i] switch
                {
                    >= '\u0660' and <= '\u0669' => (char)('0' + (chars[i] - '\u0660')),
                    >= '\u06F0' and <= '\u06F9' => (char)('0' + (chars[i] - '\u06F0')),
                    '\u061B' or '\u0589' or '\uFE13' or '\uFE55' => ':',
                    '\u2010' or '\u2011' or '\u2012' or '\u2013' or '\u2014' or '\u2015' or '\u2212' => '-',
                    '\u2264' => '≤',
                    '\u2265' => '≥',
                    _ => chars[i]
                };
            }

            text = new string(chars).Replace("≤", "<=").Replace("≥", ">=").Replace(" ", string.Empty);
            return text;
        }

        private static IQueryable<Department> ApplyIntExpr(IQueryable<Department> q, string expr, Expression<Func<Department, int>> selector)
        {
            if (string.IsNullOrWhiteSpace(expr)) return q;

            if (expr.StartsWith("<=") && int.TryParse(expr[2..], NumberStyles.Any, CultureInfo.InvariantCulture, out var le))
                return q.Where(Expression.Lambda<Func<Department, bool>>(Expression.LessThanOrEqual(selector.Body, Expression.Constant(le)), selector.Parameters));
            if (expr.StartsWith(">=") && int.TryParse(expr[2..], NumberStyles.Any, CultureInfo.InvariantCulture, out var ge))
                return q.Where(Expression.Lambda<Func<Department, bool>>(Expression.GreaterThanOrEqual(selector.Body, Expression.Constant(ge)), selector.Parameters));
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && int.TryParse(expr[1..], NumberStyles.Any, CultureInfo.InvariantCulture, out var lt))
                return q.Where(Expression.Lambda<Func<Department, bool>>(Expression.LessThan(selector.Body, Expression.Constant(lt)), selector.Parameters));
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && int.TryParse(expr[1..], NumberStyles.Any, CultureInfo.InvariantCulture, out var gt))
                return q.Where(Expression.Lambda<Func<Department, bool>>(Expression.GreaterThan(selector.Body, Expression.Constant(gt)), selector.Parameters));

            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var sep = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    var geBody = Expression.GreaterThanOrEqual(selector.Body, Expression.Constant(a));
                    var leBody = Expression.LessThanOrEqual(selector.Body, Expression.Constant(b));
                    return q.Where(Expression.Lambda<Func<Department, bool>>(Expression.AndAlso(geBody, leBody), selector.Parameters));
                }
            }

            if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var eq))
                return q.Where(Expression.Lambda<Func<Department, bool>>(Expression.Equal(selector.Body, Expression.Constant(eq)), selector.Parameters));

            return q;
        }

        private static IQueryable<Department> ApplyColumnFilters(
            IQueryable<Department> q,
            string? filterCol_id,
            string? filterCol_idExpr,
            string? filterCol_name,
            string? filterCol_sortorder,
            string? filterCol_sortorderExpr,
            string? filterCol_active,
            string? filterCol_created,
            string? filterCol_updated)
        {
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) q = q.Where(d => ids.Contains(d.Id));
            }
            else if (!string.IsNullOrWhiteSpace(filterCol_idExpr))
            {
                q = ApplyIntExpr(q, NormalizeNumericExpr(filterCol_idExpr), d => d.Id);
            }

            if (!string.IsNullOrWhiteSpace(filterCol_sortorder))
            {
                var vals = filterCol_sortorder.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0) q = q.Where(d => vals.Contains(d.SortOrder));
            }
            else if (!string.IsNullOrWhiteSpace(filterCol_sortorderExpr))
            {
                q = ApplyIntExpr(q, NormalizeNumericExpr(filterCol_sortorderExpr), d => d.SortOrder);
            }

            if (!string.IsNullOrWhiteSpace(filterCol_name))
            {
                var vals = filterCol_name.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0) q = q.Where(d => vals.Contains(d.Name));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_active))
            {
                var vals = filterCol_active.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().ToLowerInvariant()).ToList();
                var wantTrue = vals.Any(v => v == "true" || v == "1" || v == "نعم" || v == "نشط");
                var wantFalse = vals.Any(v => v == "false" || v == "0" || v == "لا" || v == "موقوف");
                if (wantTrue && !wantFalse) q = q.Where(d => d.IsActive);
                else if (wantFalse && !wantTrue) q = q.Where(d => !d.IsActive);
            }

            if (!string.IsNullOrWhiteSpace(filterCol_created))
            {
                var vals = filterCol_created.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0) q = q.Where(d => vals.Contains(d.CreatedAt.ToString("yyyy-MM-dd HH:mm")));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_updated))
            {
                var vals = filterCol_updated.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0) q = q.Where(d => d.UpdatedAt.HasValue && vals.Contains(d.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm")));
            }

            return q;
        }

        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var col = (column ?? "").Trim().ToLowerInvariant();

            var q = _db.Departments.AsNoTracking();

            if (col == "id")
            {
                var ids = await q.Select(d => d.Id).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (col == "name")
            {
                var list = await q.Select(d => d.Name).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s != null && s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v ?? "", display = v ?? "" }));
            }
            if (col == "sortorder")
            {
                var list = await q.Select(d => d.SortOrder).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(list.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (col == "active")
            {
                return Json(new[] { new { value = "true", display = "نعم" }, new { value = "false", display = "لا" } });
            }
            if (col == "created")
            {
                var list = await q.Select(d => d.CreatedAt).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd HH:mm"), display = d.ToString("yyyy-MM-dd HH:mm") }));
            }
            if (col == "updated")
            {
                var list = await q.Where(d => d.UpdatedAt.HasValue).Select(d => d.UpdatedAt!.Value).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd HH:mm"), display = d.ToString("yyyy-MM-dd HH:mm") }));
            }

            return Json(Array.Empty<object>());
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",
            string? searchMode = "contains",
            string? sort = "SortOrder",
            string? dir = "asc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_name = null,
            string? filterCol_sortorder = null,
            string? filterCol_sortorderExpr = null,
            string? filterCol_active = null,
            string? filterCol_created = null,
            string? filterCol_updated = null,
            int page = 1,
            int pageSize = 10)
        {
            var pageSizeQuery = Request.Query["pageSize"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageSizeQuery) && int.TryParse(pageSizeQuery, out var psVal))
                pageSize = psVal;
            if (Request.Query.ContainsKey("search"))
                search = Request.Query["search"].LastOrDefault();
            if (Request.Query.ContainsKey("searchBy"))
                searchBy = Request.Query["searchBy"].LastOrDefault();
            if (Request.Query.ContainsKey("searchMode"))
                searchMode = Request.Query["searchMode"].LastOrDefault();

            if (page < 1) page = 1;
            if (pageSize < 0) pageSize = 10;
            if (pageSize > 0 && pageSize != 10 && pageSize != 25 && pageSize != 50 && pageSize != 100 && pageSize != 200)
                pageSize = 10;

            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm != "starts" && sm != "ends") sm = "contains";

            var q = BuildQuery(search, searchBy, sm, sort, dir, useDateRange, fromDate, toDate, fromCode, toCode);
            q = ApplyColumnFilters(q, filterCol_id, filterCol_idExpr, filterCol_name, filterCol_sortorder, filterCol_sortorderExpr, filterCol_active, filterCol_created, filterCol_updated);

            var totalCount = await q.CountAsync();
            var activeCount = await q.CountAsync(d => d.IsActive);
            var inactiveCount = totalCount - activeCount;

            var model = await PagedResult<Department>.CreateAsync(q, page, pageSize);
            model.Search = search ?? "";
            model.SearchBy = searchBy ?? "all";
            model.SortColumn = sort ?? "SortOrder";
            model.SortDescending = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            model.UseDateRange = useDateRange;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            ViewBag.Search = search ?? "";
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.SearchMode = sm;
            ViewBag.Sort = sort ?? "SortOrder";
            ViewBag.Dir = (dir ?? "asc").ToLowerInvariant() == "desc" ? "desc" : "asc";
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;

            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_IdExpr = filterCol_idExpr;
            ViewBag.FilterCol_Name = filterCol_name;
            ViewBag.FilterCol_SortOrder = filterCol_sortorder;
            ViewBag.FilterCol_SortOrderExpr = filterCol_sortorderExpr;
            ViewBag.FilterCol_Active = filterCol_active;
            ViewBag.FilterCol_Created = filterCol_created;
            ViewBag.FilterCol_Updated = filterCol_updated;

            ViewBag.DateField = "CreatedAt";
            ViewBag.TotalCount = totalCount;
            ViewBag.ActiveCount = activeCount;
            ViewBag.InactiveCount = inactiveCount;

            ViewBag.CanEdit = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Departments", "Edit"));
            ViewBag.CanDelete = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Departments", "Delete"));

            return View(model);
        }

        [HttpGet]
        [RequirePermission("Departments.Export")]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy,
            string? searchMode,
            string? sort,
            string? dir,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_name = null,
            string? filterCol_sortorder = null,
            string? filterCol_sortorderExpr = null,
            string? filterCol_active = null,
            string? filterCol_created = null,
            string? filterCol_updated = null,
            string format = "excel")
        {
            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm != "starts" && sm != "ends") sm = "contains";

            var q = BuildQuery(search, searchBy, sm, sort, dir, useDateRange, fromDate, toDate, fromCode, toCode);
            q = ApplyColumnFilters(q, filterCol_id, filterCol_idExpr, filterCol_name, filterCol_sortorder, filterCol_sortorderExpr, filterCol_active, filterCol_created, filterCol_updated);
            var list = await q.ToListAsync();

            if (string.Equals(format, "excel", StringComparison.OrdinalIgnoreCase))
            {
                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("الأقسام"));
                int row = 1;
                worksheet.Cell(row, 1).Value = "كود";
                worksheet.Cell(row, 2).Value = "اسم القسم";
                worksheet.Cell(row, 3).Value = "ترتيب العرض";
                worksheet.Cell(row, 4).Value = "فعال";
                worksheet.Cell(row, 5).Value = "تاريخ الإنشاء";
                worksheet.Cell(row, 6).Value = "آخر تعديل";
                worksheet.Range(row, 1, row, 6).Style.Font.Bold = true;

                foreach (var d in list)
                {
                    row++;
                    worksheet.Cell(row, 1).Value = d.Id;
                    worksheet.Cell(row, 2).Value = d.Name;
                    worksheet.Cell(row, 3).Value = d.SortOrder;
                    worksheet.Cell(row, 4).Value = d.IsActive ? "نعم" : "لا";
                    worksheet.Cell(row, 5).Value = d.CreatedAt;
                    worksheet.Cell(row, 6).Value = d.UpdatedAt;
                }

                worksheet.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                stream.Position = 0;

                var fileName = ExcelExportNaming.ArabicTimestampedFileName("الأقسام", ".xlsx");
                const string contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                return File(stream.ToArray(), contentType, fileName);
            }

            // CSV (يُستخدم أيضاً لتحويل PDF عبر PdfExportMiddleware)
            var sb = new StringBuilder();
            sb.AppendLine("كود,اسم القسم,ترتيب العرض,فعال,تاريخ الإنشاء,آخر تعديل");
            foreach (var d in list)
            {
                string safeName = (d.Name ?? string.Empty).Replace("\"", "\"\"");
                string created = d.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                string updated = d.UpdatedAt.HasValue ? d.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm") : "";
                sb.AppendLine(
                    $"{d.Id}," +
                    $"\"{safeName}\"," +
                    $"{d.SortOrder}," +
                    $"{(d.IsActive ? "نعم" : "لا")}," +
                    $"{created}," +
                    $"{updated}");
            }

            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString());
            var csvName = ExcelExportNaming.ArabicTimestampedFileName("الأقسام", ".csv");
            return File(bytes, "text/csv; charset=utf-8", csvName);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Departments.Delete")]
        public async Task<IActionResult> BulkDelete([FromForm] int[]? ids)
        {
            var idList = new List<int>();
            if (ids != null && ids.Length > 0)
                idList.AddRange(ids);
            else if (Request.HasFormContentType && Request.Form["ids"].Count > 0)
            {
                foreach (var s in Request.Form["ids"])
                {
                    if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
                        idList.Add(pid);
                }
            }
            idList = idList.Distinct().ToList();

            if (idList.Count == 0)
            {
                TempData["Err"] = "لم يتم اختيار أى قسم للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var items = await _db.Departments.Where(d => idList.Contains(d.Id)).ToListAsync();
            if (items.Count == 0)
            {
                TempData["Err"] = "لم يتم العثور على الأقسام المحددة.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _db.Departments.RemoveRange(items);
                await _db.SaveChangesAsync();
                TempData["Ok"] = $"تم حذف {items.Count} من الأقسام المحددة.";
            }
            catch (DbUpdateException)
            {
                TempData["Err"] = "تعذر حذف بعض الأقسام لوجود ارتباطات (مثلاً موظفين).";
            }
            catch
            {
                TempData["Err"] = "تعذر حذف الأقسام المحددة.";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Departments.Delete")]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _db.Departments.ToListAsync();
            if (all.Count == 0)
            {
                TempData["Err"] = "لا توجد أقسام لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _db.Departments.RemoveRange(all);
                await _db.SaveChangesAsync();
                TempData["Ok"] = "تم حذف جميع الأقسام.";
            }
            catch (DbUpdateException)
            {
                TempData["Err"] = "تعذر حذف جميع الأقسام لوجود ارتباطات (مثلاً موظفين).";
            }
            catch
            {
                TempData["Err"] = "تعذر حذف جميع الأقسام.";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [RequirePermission("Departments.Create")]
        public IActionResult Create() => View(new Department { SortOrder = 0 });

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Departments.Create")]
        public async Task<IActionResult> Create([Bind("Name,IsActive")] Department entity)
        {
            if (!ModelState.IsValid) return View(entity);
            entity.CreatedAt = DateTime.UtcNow;
            entity.UpdatedAt = null;
            _db.Departments.Add(entity);
            await _db.SaveChangesAsync();
            entity.Code = entity.Id.ToString();
            await _db.SaveChangesAsync();
            TempData["Ok"] = "تمت إضافة القسم بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [RequirePermission("Departments.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            var entity = await _db.Departments.FindAsync(id);
            if (entity == null) return NotFound();
            return View(entity);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Departments.Edit")]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name,IsActive")] Department entity)
        {
            if (id != entity.Id) return NotFound();
            if (!ModelState.IsValid) return View(entity);
            var existing = await _db.Departments.FindAsync(id);
            if (existing == null) return NotFound();
            existing.Name = entity.Name;
            existing.IsActive = entity.IsActive;
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await _activityLogger.LogAsync(UserActionType.Edit, "Department", id, $"تعديل قسم: {entity.Name}");
            TempData["Ok"] = "تم تعديل القسم.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [RequirePermission("Departments.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity = await _db.Departments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (entity == null) return NotFound();
            return View(entity);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [RequirePermission("Departments.Delete")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var entity = await _db.Departments.FindAsync(id);
            if (entity == null) return NotFound();
            try
            {
                _db.Departments.Remove(entity);
                await _db.SaveChangesAsync();
                await _activityLogger.LogAsync(UserActionType.Delete, "Department", id, $"حذف قسم: {entity.Name}");
                TempData["Ok"] = "تم الحذف.";
            }
            catch (DbUpdateException)
            {
                TempData["Err"] = "لا يمكن الحذف لوجود موظفين مرتبطين بهذا القسم.";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}
