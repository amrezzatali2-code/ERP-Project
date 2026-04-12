using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
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

namespace ERP.Controllers
{
    [RequirePermission("Employees.Index")]
    public class EmployeesController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IUserActivityLogger _activityLogger;
        private readonly IPermissionService _permissionService;

        public EmployeesController(AppDbContext db, IUserActivityLogger activityLogger, IPermissionService permissionService)
        {
            _db = db;
            _activityLogger = activityLogger;
            _permissionService = permissionService;
        }

        private IQueryable<Employee> BuildQuery(
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
            IQueryable<Employee> q = _db.Employees.AsNoTracking()
                .Include(e => e.User)
                .Include(e => e.Department)
                .Include(e => e.Job);

            if (fromCode.HasValue)
                q = q.Where(e => e.Id >= fromCode.Value);
            if (toCode.HasValue)
                q = q.Where(e => e.Id <= toCode.Value);

            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                DateTime from = fromDate.Value.Date;
                DateTime to = toDate.Value.Date.AddDays(1).AddTicks(-1);
                q = q.Where(e => e.CreatedAt >= from && e.CreatedAt <= to);
            }

            var mode = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (mode != "starts" && mode != "ends") mode = "contains";

            var stringFields = new Dictionary<string, Expression<Func<Employee, string?>>>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = e => e.FullName,
                ["code"] = e => e.Code,
                ["department"] = e => e.Department != null ? e.Department.Name : null,
                ["job"] = e => e.Job != null ? e.Job.Name : null,
                ["phone"] = e => e.Phone1 ?? e.Phone2
            };

            var intFields = new Dictionary<string, Expression<Func<Employee, int>>>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = e => e.Id
            };

            var orderFields = new Dictionary<string, Expression<Func<Employee, object>>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Id"] = e => e.Id,
                ["FullName"] = e => e.FullName,
                ["Department"] = e => e.Department != null ? e.Department.Name ?? "" : "",
                ["Job"] = e => e.Job != null ? e.Job.Name ?? "" : "",
                ["HireDate"] = e => e.HireDate ?? DateTime.MinValue,
                ["IsActive"] = e => e.IsActive,
                ["CreatedAt"] = e => e.CreatedAt,
                ["UpdatedAt"] = e => e.UpdatedAt ?? DateTime.MinValue
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
                defaultSortBy: "FullName",
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

        private static IQueryable<Employee> ApplyIntExpr(IQueryable<Employee> q, string expr, Expression<Func<Employee, int>> selector)
        {
            if (string.IsNullOrWhiteSpace(expr)) return q;

            if (expr.StartsWith("<=") && int.TryParse(expr[2..], NumberStyles.Any, CultureInfo.InvariantCulture, out var le))
                return q.Where(Expression.Lambda<Func<Employee, bool>>(Expression.LessThanOrEqual(selector.Body, Expression.Constant(le)), selector.Parameters));
            if (expr.StartsWith(">=") && int.TryParse(expr[2..], NumberStyles.Any, CultureInfo.InvariantCulture, out var ge))
                return q.Where(Expression.Lambda<Func<Employee, bool>>(Expression.GreaterThanOrEqual(selector.Body, Expression.Constant(ge)), selector.Parameters));
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && int.TryParse(expr[1..], NumberStyles.Any, CultureInfo.InvariantCulture, out var lt))
                return q.Where(Expression.Lambda<Func<Employee, bool>>(Expression.LessThan(selector.Body, Expression.Constant(lt)), selector.Parameters));
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && int.TryParse(expr[1..], NumberStyles.Any, CultureInfo.InvariantCulture, out var gt))
                return q.Where(Expression.Lambda<Func<Employee, bool>>(Expression.GreaterThan(selector.Body, Expression.Constant(gt)), selector.Parameters));

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
                    return q.Where(Expression.Lambda<Func<Employee, bool>>(Expression.AndAlso(geBody, leBody), selector.Parameters));
                }
            }

            if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var eq))
                return q.Where(Expression.Lambda<Func<Employee, bool>>(Expression.Equal(selector.Body, Expression.Constant(eq)), selector.Parameters));

            return q;
        }

        private static IQueryable<Employee> ApplyColumnFilters(
            IQueryable<Employee> q,
            string? filterCol_id,
            string? filterCol_idExpr,
            string? filterCol_name,
            string? filterCol_department,
            string? filterCol_job,
            string? filterCol_phone,
            string? filterCol_hiredate,
            string? filterCol_active,
            string? filterCol_created,
            string? filterCol_updated)
        {
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) q = q.Where(e => ids.Contains(e.Id));
            }
            else if (!string.IsNullOrWhiteSpace(filterCol_idExpr))
            {
                q = ApplyIntExpr(q, NormalizeNumericExpr(filterCol_idExpr), e => e.Id);
            }

            if (!string.IsNullOrWhiteSpace(filterCol_name))
            {
                var vals = filterCol_name.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0) q = q.Where(e => vals.Contains(e.FullName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_department))
            {
                var vals = filterCol_department.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0) q = q.Where(e => e.Department != null && vals.Contains(e.Department.Name));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_job))
            {
                var vals = filterCol_job.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0) q = q.Where(e => e.Job != null && vals.Contains(e.Job.Name));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_phone))
            {
                var vals = filterCol_phone.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0) q = q.Where(e => vals.Contains(e.Phone1 ?? e.Phone2 ?? ""));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_hiredate))
            {
                var vals = filterCol_hiredate.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0) q = q.Where(e => e.HireDate.HasValue && vals.Contains(e.HireDate.Value.ToString("yyyy-MM-dd")));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_active))
            {
                var vals = filterCol_active.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().ToLowerInvariant()).ToList();
                var wantTrue = vals.Any(v => v == "true" || v == "1" || v == "نعم" || v == "نشط");
                var wantFalse = vals.Any(v => v == "false" || v == "0" || v == "لا" || v == "موقوف");
                if (wantTrue && !wantFalse) q = q.Where(e => e.IsActive);
                else if (wantFalse && !wantTrue) q = q.Where(e => !e.IsActive);
            }
            if (!string.IsNullOrWhiteSpace(filterCol_created))
            {
                var vals = filterCol_created.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0) q = q.Where(e => vals.Contains(e.CreatedAt.ToString("yyyy-MM-dd HH:mm")));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_updated))
            {
                var vals = filterCol_updated.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (vals.Count > 0) q = q.Where(e => e.UpdatedAt.HasValue && vals.Contains(e.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm")));
            }

            return q;
        }

        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var col = (column ?? "").Trim().ToLowerInvariant();

            var q = _db.Employees.AsNoTracking()
                .Include(e => e.Department)
                .Include(e => e.Job);

            if (col == "id")
            {
                var ids = await q.Select(e => e.Id).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (col == "name")
            {
                var list = await q.Select(e => e.FullName).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s != null && s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v ?? "", display = v ?? "" }));
            }
            if (col == "department")
            {
                var list = await q.Where(e => e.Department != null).Select(e => e.Department!.Name).Distinct().OrderBy(x => x).Take(300).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s != null && s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v ?? "", display = v ?? "" }));
            }
            if (col == "job")
            {
                var list = await q.Where(e => e.Job != null).Select(e => e.Job!.Name).Distinct().OrderBy(x => x).Take(300).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s != null && s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v ?? "", display = v ?? "" }));
            }
            if (col == "phone")
            {
                var list = await q.Select(e => e.Phone1 ?? e.Phone2).Where(x => x != null).Distinct().OrderBy(x => x).Take(300).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s != null && s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v ?? "", display = v ?? "" }));
            }
            if (col == "hiredate")
            {
                var list = await q.Where(e => e.HireDate.HasValue).Select(e => e.HireDate!.Value).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd"), display = d.ToString("yyyy-MM-dd") }));
            }
            if (col == "active")
            {
                return Json(new[] { new { value = "true", display = "نعم" }, new { value = "false", display = "لا" } });
            }
            if (col == "created")
            {
                var list = await q.Select(e => e.CreatedAt).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd HH:mm"), display = d.ToString("yyyy-MM-dd HH:mm") }));
            }
            if (col == "updated")
            {
                var list = await q.Where(e => e.UpdatedAt.HasValue).Select(e => e.UpdatedAt!.Value).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd HH:mm"), display = d.ToString("yyyy-MM-dd HH:mm") }));
            }

            return Json(Array.Empty<object>());
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",
            string? searchMode = "contains",
            string? sort = "FullName",
            string? dir = "asc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_name = null,
            string? filterCol_department = null,
            string? filterCol_job = null,
            string? filterCol_phone = null,
            string? filterCol_hiredate = null,
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
            q = ApplyColumnFilters(q, filterCol_id, filterCol_idExpr, filterCol_name, filterCol_department, filterCol_job, filterCol_phone, filterCol_hiredate, filterCol_active, filterCol_created, filterCol_updated);

            var totalCount = await q.CountAsync();
            var activeCount = await q.CountAsync(e => e.IsActive);
            var inactiveCount = totalCount - activeCount;

            var model = await PagedResult<Employee>.CreateAsync(q, page, pageSize);
            model.Search = search ?? "";
            model.SearchBy = searchBy ?? "all";
            model.SortColumn = sort ?? "FullName";
            model.SortDescending = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            model.UseDateRange = useDateRange;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            ViewBag.Search = search ?? "";
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.SearchMode = sm;
            ViewBag.Sort = sort ?? "FullName";
            ViewBag.Dir = (dir ?? "asc").ToLowerInvariant() == "desc" ? "desc" : "asc";
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;

            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_IdExpr = filterCol_idExpr;
            ViewBag.FilterCol_Name = filterCol_name;
            ViewBag.FilterCol_Department = filterCol_department;
            ViewBag.FilterCol_Job = filterCol_job;
            ViewBag.FilterCol_Phone = filterCol_phone;
            ViewBag.FilterCol_Hiredate = filterCol_hiredate;
            ViewBag.FilterCol_Active = filterCol_active;
            ViewBag.FilterCol_Created = filterCol_created;
            ViewBag.FilterCol_Updated = filterCol_updated;

            ViewBag.DateField = "CreatedAt";
            ViewBag.TotalCount = totalCount;
            ViewBag.ActiveCount = activeCount;
            ViewBag.InactiveCount = inactiveCount;

            ViewBag.CanEdit = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Employees", "Edit"));
            ViewBag.CanDelete = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Employees", "Delete"));

            return View(model);
        }

        [HttpGet]
        [RequirePermission("Employees.Export")]
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
            string? filterCol_department = null,
            string? filterCol_job = null,
            string? filterCol_phone = null,
            string? filterCol_hiredate = null,
            string? filterCol_active = null,
            string? filterCol_created = null,
            string? filterCol_updated = null,
            string format = "excel")
        {
            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm != "starts" && sm != "ends") sm = "contains";

            var q = BuildQuery(search, searchBy, sm, sort, dir, useDateRange, fromDate, toDate, fromCode, toCode);
            q = ApplyColumnFilters(q, filterCol_id, filterCol_idExpr, filterCol_name, filterCol_department, filterCol_job, filterCol_phone, filterCol_hiredate, filterCol_active, filterCol_created, filterCol_updated);
            var list = await q.ToListAsync();

            if (string.Equals(format, "excel", StringComparison.OrdinalIgnoreCase))
            {
                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("الموظفون"));
                int row = 1;
                worksheet.Cell(row, 1).Value = "كود";
                worksheet.Cell(row, 2).Value = "الاسم";
                worksheet.Cell(row, 3).Value = "القسم";
                worksheet.Cell(row, 4).Value = "الوظيفة";
                worksheet.Cell(row, 5).Value = "هاتف";
                worksheet.Cell(row, 6).Value = "تاريخ التعيين";
                worksheet.Cell(row, 7).Value = "نشط";
                worksheet.Cell(row, 8).Value = "تاريخ الإنشاء";
                worksheet.Cell(row, 9).Value = "آخر تعديل";
                worksheet.Range(row, 1, row, 9).Style.Font.Bold = true;

                foreach (var e in list)
                {
                    row++;
                    worksheet.Cell(row, 1).Value = e.Id;
                    worksheet.Cell(row, 2).Value = e.FullName;
                    worksheet.Cell(row, 3).Value = e.Department?.Name ?? "";
                    worksheet.Cell(row, 4).Value = e.Job?.Name ?? "";
                    worksheet.Cell(row, 5).Value = e.Phone1 ?? e.Phone2 ?? "";
                    worksheet.Cell(row, 6).Value = e.HireDate;
                    worksheet.Cell(row, 7).Value = e.IsActive ? "نعم" : "لا";
                    worksheet.Cell(row, 8).Value = e.CreatedAt;
                    worksheet.Cell(row, 9).Value = e.UpdatedAt;
                }

                worksheet.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                stream.Position = 0;

                var fileName = ExcelExportNaming.ArabicTimestampedFileName("الموظفون", ".xlsx");
                const string contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                return File(stream.ToArray(), contentType, fileName);
            }

            // CSV (يُستخدم أيضاً لتحويل PDF عبر PdfExportMiddleware)
            var sb = new StringBuilder();
            sb.AppendLine("كود,الاسم,القسم,الوظيفة,هاتف,تاريخ التعيين,نشط,تاريخ الإنشاء,آخر تعديل");
            foreach (var e in list)
            {
                string safeName = (e.FullName ?? string.Empty).Replace("\"", "\"\"");
                string safeDept = (e.Department?.Name ?? string.Empty).Replace("\"", "\"\"");
                string safeJob = (e.Job?.Name ?? string.Empty).Replace("\"", "\"\"");
                string safePhone = ((e.Phone1 ?? e.Phone2) ?? string.Empty).Replace("\"", "\"\"");
                string hire = e.HireDate.HasValue ? e.HireDate.Value.ToString("yyyy-MM-dd") : "";
                string created = e.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                string updated = e.UpdatedAt.HasValue ? e.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm") : "";
                sb.AppendLine(
                    $"{e.Id}," +
                    $"\"{safeName}\"," +
                    $"\"{safeDept}\"," +
                    $"\"{safeJob}\"," +
                    $"\"{safePhone}\"," +
                    $"{hire}," +
                    $"{(e.IsActive ? "نعم" : "لا")}," +
                    $"{created}," +
                    $"{updated}");
            }

            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString());
            var csvName = ExcelExportNaming.ArabicTimestampedFileName("الموظفون", ".csv");
            return File(bytes, "text/csv; charset=utf-8", csvName);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Employees.Delete")]
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
                TempData["Err"] = "لم يتم اختيار أى موظف للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var employees = await _db.Employees.Where(e => idList.Contains(e.Id)).ToListAsync();
            if (employees.Count == 0)
            {
                TempData["Err"] = "لم يتم العثور على الموظفين المحددين.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _db.Employees.RemoveRange(employees);
                await _db.SaveChangesAsync();
                TempData["Ok"] = $"تم حذف {employees.Count} من الموظفين المحددين.";
            }
            catch
            {
                TempData["Err"] = "تعذر حذف الموظفين المحددين.";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Employees.Delete")]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _db.Employees.ToListAsync();
            if (all.Count == 0)
            {
                TempData["Err"] = "لا توجد موظفين لحذفهم.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _db.Employees.RemoveRange(all);
                await _db.SaveChangesAsync();
                TempData["Ok"] = "تم حذف جميع الموظفين.";
            }
            catch
            {
                TempData["Err"] = "تعذر حذف جميع الموظفين بسبب ارتباطات أخرى.";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [RequirePermission("Employees.Create")]
        public async Task<IActionResult> Create()
        {
            await PopulateUserListAsync(null);
            await PopulateDepartmentsAndJobsAsync(null, null);
            return View(new Employee());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Employees.Create")]
        public async Task<IActionResult> Create([Bind("FullName,NationalId,BirthDate,HireDate,DepartmentId,JobId,Phone1,Phone2,Email,Address,BaseSalary,IsActive,Notes,UserId")] Employee entity)
        {
            if (!ModelState.IsValid)
            {
                await PopulateUserListAsync(entity.UserId);
                await PopulateDepartmentsAndJobsAsync(entity.DepartmentId, entity.JobId);
                return View(entity);
            }
            entity.CreatedAt = DateTime.UtcNow;
            entity.UpdatedAt = null;
            _db.Employees.Add(entity);
            await _db.SaveChangesAsync();
            entity.Code = entity.Id.ToString();
            await _db.SaveChangesAsync();
            TempData["Ok"] = "تمت إضافة الموظف بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [RequirePermission("Employees.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            var entity = await _db.Employees.FindAsync(id);
            if (entity == null) return NotFound();
            await PopulateUserListAsync(entity.UserId);
            await PopulateDepartmentsAndJobsAsync(entity.DepartmentId, entity.JobId);
            return View(entity);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Employees.Edit")]
        public async Task<IActionResult> Edit(int id, [Bind("Id,FullName,NationalId,BirthDate,HireDate,DepartmentId,JobId,Phone1,Phone2,Email,Address,BaseSalary,IsActive,Notes,UserId")] Employee entity)
        {
            if (id != entity.Id) return NotFound();
            if (!ModelState.IsValid)
            {
                await PopulateUserListAsync(entity.UserId);
                await PopulateDepartmentsAndJobsAsync(entity.DepartmentId, entity.JobId);
                return View(entity);
            }
            var existing = await _db.Employees.FindAsync(id);
            if (existing == null) return NotFound();
            existing.FullName = entity.FullName;
            existing.NationalId = entity.NationalId;
            existing.BirthDate = entity.BirthDate;
            existing.HireDate = entity.HireDate;
            existing.DepartmentId = entity.DepartmentId;
            existing.JobId = entity.JobId;
            existing.Phone1 = entity.Phone1;
            existing.Phone2 = entity.Phone2;
            existing.Email = entity.Email;
            existing.Address = entity.Address;
            existing.BaseSalary = entity.BaseSalary;
            existing.IsActive = entity.IsActive;
            existing.Notes = entity.Notes;
            existing.UserId = entity.UserId;
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await _activityLogger.LogAsync(UserActionType.Edit, "Employee", id, $"تعديل موظف: {entity.FullName}");
            TempData["Ok"] = "تم تعديل بيانات الموظف.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [RequirePermission("Employees.Show")]
        public async Task<IActionResult> Show(int id)
        {
            var entity = await _db.Employees.AsNoTracking()
                .Include(e => e.User)
                .Include(e => e.Department)
                .Include(e => e.Job)
                .FirstOrDefaultAsync(e => e.Id == id);
            if (entity == null) return NotFound();
            return View(entity);
        }

        [HttpGet]
        [RequirePermission("Employees.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity = await _db.Employees.AsNoTracking()
                .Include(e => e.Department)
                .Include(e => e.Job)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (entity == null) return NotFound();
            return View(entity);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [RequirePermission("Employees.Delete")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var entity = await _db.Employees.FindAsync(id);
            if (entity == null) return NotFound();
            _db.Employees.Remove(entity);
            await _db.SaveChangesAsync();
            await _activityLogger.LogAsync(UserActionType.Delete, "Employee", id, $"حذف موظف: {entity.FullName}");
            TempData["Ok"] = "تم الحذف.";
            return RedirectToAction(nameof(Index));
        }

        private async Task PopulateUserListAsync(int? selectedUserId)
        {
            var users = await _db.Users
                .AsNoTracking()
                .Where(u => u.IsActive)
                .OrderBy(u => u.DisplayName)
                .Select(u => new { u.UserId, Display = u.DisplayName ?? u.UserName })
                .ToListAsync();
            ViewBag.UserId = new SelectList(users, "UserId", "Display", selectedUserId);
        }

        private async Task PopulateDepartmentsAndJobsAsync(int? selectedDepartmentId, int? selectedJobId)
        {
            var depts = await _db.Departments
                .AsNoTracking()
                .Where(d => d.IsActive)
                .OrderBy(d => d.SortOrder).ThenBy(d => d.Name)
                .Select(d => new { d.Id, d.Name })
                .ToListAsync();
            ViewBag.DepartmentId = new SelectList(depts, "Id", "Name", selectedDepartmentId);

            var jobs = await _db.Jobs
                .AsNoTracking()
                .Where(j => j.IsActive)
                .OrderBy(j => j.SortOrder).ThenBy(j => j.Name)
                .Select(j => new { j.Id, j.Name })
                .ToListAsync();
            ViewBag.JobId = new SelectList(jobs, "Id", "Name", selectedJobId);
        }
    }
}
