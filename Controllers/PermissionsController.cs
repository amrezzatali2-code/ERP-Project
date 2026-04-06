using System;                                     // متغيرات التاريخ DateTime
using System.Collections.Generic;                 // القوائم List
using System.Linq;                                // أوامر LINQ مثل Where و OrderBy
using System.Text;                                // StringBuilder لبناء CSV
using System.Threading.Tasks;                     // Task و async

using ERP.Data;                                   // AppDbContext
using ERP.Filters;
using ERP.Infrastructure;                         // PagedResult + UserActivityLogger
using ERP.Models;                                 // Permission, UserActionType
using ERP.Security;
using Microsoft.AspNetCore.Mvc;                   // أساس الكنترولر
using Microsoft.EntityFrameworkCore;              // Include, AsNoTracking, ToListAsync
using ClosedXML.Excel;                      // لتصدير Excel
using System.IO;

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إدارة جدول الصلاحيات (Permissions).
    /// مسئول عن:
    /// - عرض قائمة الصلاحيات مع بحث/ترتيب/فلترة وتصدير.
    /// - CRUD كامل (إضافة، تعديل، حذف).
    /// - الحذف الجماعي والحذف الكلي (للاستخدام في بيئة تجريبية).
    /// </summary>
    public class PermissionsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IUserActivityLogger _activityLogger;

        public PermissionsController(AppDbContext context, IUserActivityLogger activityLogger)
        {
            _context = context;
            _activityLogger = activityLogger;
        }

        // ========= دالة مساعدة لتطبيق الفلاتر (بحث + كود + تاريخ) =========

        /// <summary>
        /// تطبيق البحث والفلاتر على استعلام الصلاحيات.
        /// تُستخدم في Index و Export لضمان نفس المنطق.
        /// </summary>
        private IQueryable<Permission> ApplyFilters(
            IQueryable<Permission> query,
            string? search,
            string? searchBy,
            string? searchMode,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            int? fromCode,
            int? toCode)
        {
            // فلتر الكود من/إلى على PermissionId
            if (fromCode.HasValue)
            {
                query = query.Where(p => p.PermissionId >= fromCode.Value);
            }

            if (toCode.HasValue)
            {
                query = query.Where(p => p.PermissionId <= toCode.Value);
            }

            // فلتر التاريخ (نستخدم CreatedAt)
            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                query = query.Where(p => p.CreatedAt >= fromDate.Value &&
                                         p.CreatedAt <= toDate.Value);
            }

            // بحث نصي حسب اختيار المستخدم
            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = search.Trim();
                string mode = (searchMode ?? "contains").Trim().ToLowerInvariant();
                if (mode != "starts" && mode != "ends")
                {
                    mode = "contains";
                }

                IQueryable<Permission> ApplyTextMatch(IQueryable<Permission> source, Func<Permission, string?> selector) =>
                    mode switch
                    {
                        "starts" => source.Where(p => (selector(p) ?? string.Empty).StartsWith(term)),
                        "ends" => source.Where(p => (selector(p) ?? string.Empty).EndsWith(term)),
                        _ => source.Where(p => (selector(p) ?? string.Empty).Contains(term))
                    };

                switch ((searchBy ?? "all").ToLower())
                {
                    case "code":
                        query = ApplyTextMatch(query, p => p.Code);
                        break;

                    case "name":
                        query = ApplyTextMatch(query, p => p.NameAr);
                        break;

                    case "module":
                        query = ApplyTextMatch(query, p => p.Module);
                        break;

                    case "description":
                        query = ApplyTextMatch(query, p => p.Description);
                        break;

                    case "id":
                        if (int.TryParse(term, out int idVal))
                        {
                            query = query.Where(p => p.PermissionId == idVal);
                        }
                        else
                        {
                            // لو كتب نص مش رقم مع searchBy = id نرجّع لا شيء
                            query = query.Where(p => false);
                        }
                        break;

                    default: // all
                        query = mode switch
                        {
                            "starts" => query.Where(p =>
                                (p.Code ?? string.Empty).StartsWith(term) ||
                                (p.NameAr ?? string.Empty).StartsWith(term) ||
                                (p.Module ?? string.Empty).StartsWith(term) ||
                                (p.Description ?? string.Empty).StartsWith(term)),
                            "ends" => query.Where(p =>
                                (p.Code ?? string.Empty).EndsWith(term) ||
                                (p.NameAr ?? string.Empty).EndsWith(term) ||
                                (p.Module ?? string.Empty).EndsWith(term) ||
                                (p.Description ?? string.Empty).EndsWith(term)),
                            _ => query.Where(p =>
                                (p.Code ?? string.Empty).Contains(term) ||
                                (p.NameAr ?? string.Empty).Contains(term) ||
                                (p.Module ?? string.Empty).Contains(term) ||
                                (p.Description ?? string.Empty).Contains(term))
                        };
                        break;
                }
            }

            return query;
        }

        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        private static IQueryable<Permission> ApplyPermissionIdExpr(IQueryable<Permission> query, string expr)
        {
            if (string.IsNullOrWhiteSpace(expr))
            {
                return query;
            }

            var raw = expr.Trim();

            if (raw.Contains(':') || raw.Contains('-'))
            {
                var parts = raw.Split(new[] { ':', '-' }, 2, StringSplitOptions.TrimEntries);
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out var minVal) &&
                    int.TryParse(parts[1], out var maxVal))
                {
                    var min = Math.Min(minVal, maxVal);
                    var max = Math.Max(minVal, maxVal);
                    return query.Where(p => p.PermissionId >= min && p.PermissionId <= max);
                }
            }

            string[] prefixes = { ">=", "<=", ">", "<" };
            foreach (var prefix in prefixes)
            {
                if (raw.StartsWith(prefix, StringComparison.Ordinal))
                {
                    var numberPart = raw.Substring(prefix.Length).Trim();
                    if (!int.TryParse(numberPart, out var number))
                    {
                        return query;
                    }

                    return prefix switch
                    {
                        ">=" => query.Where(p => p.PermissionId >= number),
                        "<=" => query.Where(p => p.PermissionId <= number),
                        ">" => query.Where(p => p.PermissionId > number),
                        "<" => query.Where(p => p.PermissionId < number),
                        _ => query
                    };
                }
            }

            if (int.TryParse(raw, out var exact))
            {
                return query.Where(p => p.PermissionId == exact);
            }

            return query;
        }

        private static IQueryable<Permission> ApplyColumnFilters(
            IQueryable<Permission> query,
            string? filterCol_id,
            string? filterCol_idExpr,
            string? filterCol_code,
            string? filterCol_name,
            string? filterCol_module,
            string? filterCol_description,
            string? filterCol_created,
            string? filterCol_updated)
        {
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) query = query.Where(p => ids.Contains(p.PermissionId));
            }
            else if (!string.IsNullOrWhiteSpace(filterCol_idExpr))
            {
                query = ApplyPermissionIdExpr(query, filterCol_idExpr);
            }
            if (!string.IsNullOrWhiteSpace(filterCol_code))
            {
                var vals = filterCol_code.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) query = query.Where(p => vals.Contains(p.Code));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_name))
            {
                var vals = filterCol_name.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) query = query.Where(p => p.NameAr != null && vals.Contains(p.NameAr));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_module))
            {
                var vals = filterCol_module.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) query = query.Where(p => p.Module != null && vals.Contains(p.Module));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_description))
            {
                var vals = filterCol_description.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) query = query.Where(p => p.Description != null && vals.Contains(p.Description));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_created))
            {
                var parts = filterCol_created.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length >= 8).ToList();
                if (parts.Count > 0)
                {
                    var dates = new List<DateTime>();
                    foreach (var p in parts)
                        if (DateTime.TryParse(p, out var d)) dates.Add(d);
                    if (dates.Count > 0) query = query.Where(p => dates.Contains(p.CreatedAt));
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_updated))
            {
                var parts = filterCol_updated.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length >= 8).ToList();
                if (parts.Count > 0)
                {
                    var dates = new List<DateTime?>();
                    foreach (var p in parts)
                        if (DateTime.TryParse(p, out var d)) dates.Add(d);
                    if (dates.Count > 0) query = query.Where(p => p.UpdatedAt.HasValue && dates.Contains(p.UpdatedAt));
                }
            }
            return query;
        }

        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var columnLower = (column ?? "").Trim().ToLowerInvariant();
            var q = _context.Permissions.AsNoTracking();

            if (columnLower == "id" || columnLower == "permissionid")
            {
                var ids = await q.Select(p => p.PermissionId).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "code")
            {
                var list = await q.Select(p => p.Code).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s != null && s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v ?? "", display = v ?? "" }));
            }
            if (columnLower == "name" || columnLower == "namear")
            {
                var list = await q.Select(p => p.NameAr).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s != null && s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v ?? "", display = v ?? "" }));
            }
            if (columnLower == "module")
            {
                var list = await q.Where(p => p.Module != null).Select(p => p.Module!).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s != null && s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v ?? "", display = v ?? "" }));
            }
            if (columnLower == "description")
            {
                var list = await q.Where(p => p.Description != null).Select(p => p.Description!).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s != null && s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v ?? "", display = v ?? "" }));
            }
            if (columnLower == "created" || columnLower == "createdat")
            {
                var list = await q.Select(p => p.CreatedAt).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd HH:mm"), display = d.ToString("yyyy-MM-dd HH:mm") }));
            }
            if (columnLower == "updated" || columnLower == "updatedat")
            {
                var list = await q.Where(p => p.UpdatedAt.HasValue).Select(p => p.UpdatedAt!.Value).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd HH:mm"), display = d.ToString("yyyy-MM-dd HH:mm") }));
            }
            return Json(Array.Empty<object>());
        }





        // ========= INDEX: قائمة الصلاحيات بنظام القوائم الموحّد =========

        /// <summary>
        /// عرض قائمة الصلاحيات مع:
        /// - بحث (نص عام / كود / اسم / موديول / رقم)
        /// - فلتر كود من/إلى
        /// - فلتر تاريخ إنشاء من/إلى
        /// - ترتيب + تقسيم صفحات باستخدام PagedResult
        /// </summary>
        [RequirePermission("Permissions.Index")]
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy,
            string? searchMode,
            string? sort,
            string? dir,
            int page = 1,
            int pageSize = 10,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? dateField = "CreatedAt",
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_code = null,
            string? filterCol_name = null,
            string? filterCol_module = null,
            string? filterCol_description = null,
            string? filterCol_created = null,
            string? filterCol_updated = null)
        {
            var pageSizeQuery = Request.Query["pageSize"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageSizeQuery) && int.TryParse(pageSizeQuery, out var psVal))
            {
                pageSize = psVal;
            }

            if (Request.Query.ContainsKey("search"))
            {
                search = Request.Query["search"].LastOrDefault();
            }

            if (Request.Query.ContainsKey("searchBy"))
            {
                searchBy = Request.Query["searchBy"].LastOrDefault();
            }

            if (Request.Query.ContainsKey("searchMode"))
            {
                searchMode = Request.Query["searchMode"].LastOrDefault();
            }

            if (page < 1) page = 1;
            if (pageSize < 0) pageSize = 10;
            if (pageSize > 0 && pageSize != 10 && pageSize != 25 && pageSize != 50 && pageSize != 100 && pageSize != 200)
            {
                pageSize = 10;
            }

            var query = _context.Permissions.AsNoTracking();

            query = ApplyFilters(query, search, searchBy, searchMode, useDateRange, fromDate, toDate, fromCode, toCode);
            query = ApplyColumnFilters(query, filterCol_id, filterCol_idExpr, filterCol_code, filterCol_name, filterCol_module, filterCol_description, filterCol_created, filterCol_updated);

            // الترتيب
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            sort ??= "PermissionId"; // الترتيب الافتراضي برقم الصلاحية

            query = sort switch
            {
                "Code" => desc
                    ? query.OrderByDescending(p => p.Code)
                    : query.OrderBy(p => p.Code),

                "NameAr" => desc
                    ? query.OrderByDescending(p => p.NameAr)
                    : query.OrderBy(p => p.NameAr),

                "Module" => desc
                    ? query.OrderByDescending(p => p.Module)
                    : query.OrderBy(p => p.Module),

                "Description" => desc
                    ? query.OrderByDescending(p => p.Description)
                    : query.OrderBy(p => p.Description),

                "CreatedAt" => desc
                    ? query.OrderByDescending(p => p.CreatedAt)
                    : query.OrderBy(p => p.CreatedAt),

                "UpdatedAt" => desc
                    ? query.OrderByDescending(p => p.UpdatedAt)
                    : query.OrderBy(p => p.UpdatedAt),

                _ => desc
                    ? query.OrderByDescending(p => p.PermissionId)
                    : query.OrderBy(p => p.PermissionId)
            };

            int totalCount = await query.CountAsync();
            int moduleCount = await query
                .Where(p => !string.IsNullOrWhiteSpace(p.Module))
                .Select(p => p.Module!)
                .Distinct()
                .CountAsync();
            int describedCount = await query.CountAsync(p => !string.IsNullOrWhiteSpace(p.Description));

            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm != "starts" && sm != "ends")
            {
                sm = "contains";
            }

            var model = await PagedResult<Permission>.CreateAsync(query, page, pageSize, sort, desc, search, searchBy ?? "all");
            model.UseDateRange = useDateRange;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            ViewBag.Search = search;
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.SearchMode = sm;
            ViewBag.Sort = sort;
            ViewBag.Dir = desc ? "desc" : "asc";
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.DateField = dateField ?? "CreatedAt";
            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_IdExpr = filterCol_idExpr;
            ViewBag.FilterCol_Code = filterCol_code;
            ViewBag.FilterCol_Name = filterCol_name;
            ViewBag.FilterCol_Module = filterCol_module;
            ViewBag.FilterCol_Description = filterCol_description;
            ViewBag.FilterCol_Created = filterCol_created;
            ViewBag.FilterCol_Updated = filterCol_updated;
            ViewBag.TotalCount = totalCount;
            ViewBag.ModulesCount = moduleCount;
            ViewBag.DescribedCount = describedCount;

            return View(model);
        }







        // ========= DETAILS: عرض تفاصيل صلاحية واحدة =========

        /// <summary>
        /// عرض تفاصيل صلاحية واحدة، مع بيان الأدوار المرتبطة
        /// واستثناءات المستخدمين (إن وجدت).
        /// </summary>
        public async Task<IActionResult> Details(int id, string? returnUrl = null)
        {
            var permission = await _context.Permissions
                .AsNoTracking()
                .Include(p => p.RolePermissions)          // العلاقات مع الأدوار
                .Include(p => p.UserDeniedPermissions)    // استثناءات المستخدمين
                .FirstOrDefaultAsync(p => p.PermissionId == id);

            if (permission == null)
            {
                return NotFound();
            }

            ViewBag.ReturnUrl = returnUrl;
            return View(permission);
        }






        // ========= CREATE: إضافة صلاحية جديدة =========

        /// <summary>
        /// شاشة إضافة صلاحية جديدة.
        /// </summary>
        public IActionResult Create()
        {
            return View();
        }

        /// <summary>
        /// استلام بيانات الصلاحية الجديدة وحفظها.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Code,NameAr,Module,Description")] Permission permission)
        {
            // منع تكرار كود الصلاحية (بدون مراعاة حالة الحروف)
            if (!string.IsNullOrWhiteSpace(permission?.Code))
            {
                var codeExists = await _context.Permissions
                    .AnyAsync(p => p.Code != null && p.Code.Trim().ToLower() == permission.Code.Trim().ToLower());
                if (codeExists)
                {
                    ModelState.AddModelError(nameof(Permission.Code), "كود الصلاحية موجود مسبقاً. لا يمكن تكرار نفس الكود (يمكنك تعديل الصلاحية الموجودة بدلاً من إضافة جديدة).");
                }
            }

            if (ModelState.IsValid)
            {
                permission.CreatedAt = DateTime.UtcNow;   // تاريخ الإنشاء
                _context.Add(permission);
                await _context.SaveChangesAsync();

                await _activityLogger.LogAsync(UserActionType.Create, "Permission", permission.PermissionId, $"إنشاء صلاحية: {permission.NameAr}");

                TempData["Success"] = "تم إضافة الصلاحية بنجاح.";
                return RedirectToAction(nameof(Index));
            }

            // لو في أخطاء تحقق من الصحة نرجع لنفس الفورم
            return View(permission);
        }






        // ========= EDIT: تعديل صلاحية =========

        /// <summary>
        /// عرض بيانات صلاحية للتعديل.
        /// </summary>
        public async Task<IActionResult> Edit(int id, string? returnUrl = null)
        {
            var permission = await _context.Permissions.FindAsync(id);
            if (permission == null)
            {
                return NotFound();
            }

            ViewBag.ReturnUrl = returnUrl;
            return View(permission);
        }

        /// <summary>
        /// استلام بيانات التعديل وحفظها في قاعدة البيانات.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            int id,
            [Bind("PermissionId,Code,NameAr,Module,Description,CreatedAt")] Permission permission,
            string? returnUrl = null)
        {
            if (id != permission.PermissionId)
            {
                return NotFound();
            }

            // منع تعديل الكود إلى قيمة مكررة (صلاحية أخرى لها نفس الكود)
            if (ModelState.IsValid && !string.IsNullOrWhiteSpace(permission?.Code))
            {
                var duplicateCode = await _context.Permissions
                    .AnyAsync(p => p.PermissionId != id && p.Code != null && p.Code.Trim().ToLower() == permission.Code.Trim().ToLower());
                if (duplicateCode)
                {
                    ModelState.AddModelError(nameof(Permission.Code), "كود الصلاحية هذا مستخدم لصلاحية أخرى. اختر كوداً فريداً.");
                }
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var existing = await _context.Permissions.AsNoTracking().FirstOrDefaultAsync(p => p.PermissionId == id);
                    var oldValues = existing != null ? System.Text.Json.JsonSerializer.Serialize(new { existing.Code, existing.NameAr, existing.Module }) : null;
                    permission.UpdatedAt = DateTime.UtcNow;   // تحديث تاريخ التعديل
                    _context.Update(permission);
                    await _context.SaveChangesAsync();

                    var newValues = System.Text.Json.JsonSerializer.Serialize(new { permission.Code, permission.NameAr, permission.Module });
                    await _activityLogger.LogAsync(UserActionType.Edit, "Permission", id, $"تعديل صلاحية: {permission.NameAr}", oldValues, newValues);

                    TempData["Success"] = "تم تعديل الصلاحية بنجاح.";
                }
                catch (DbUpdateException ex) when (ex.InnerException?.Message?.Contains("IX_Permissions_Code") == true || ex.InnerException?.Message?.Contains("duplicate key") == true)
                {
                    ModelState.AddModelError(nameof(Permission.Code), "كود الصلاحية مستخدم مسبقاً. لا يمكن تكرار نفس الكود.");
                    ViewBag.ReturnUrl = returnUrl;
                    return View(permission);
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!PermissionExists(permission.PermissionId))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }

                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);
                return RedirectToAction(nameof(Index));
            }

            ViewBag.ReturnUrl = returnUrl;
            return View(permission);
        }

        // ========= DELETE (مفردة) – لو حبّيت تستخدمها لاحقًا =========

        /// <summary>
        /// شاشة تأكيد حذف لصلاحية واحدة (اختيارية).
        /// </summary>
        public async Task<IActionResult> Delete(int id, string? returnUrl = null)
        {
            var permission = await _context.Permissions
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PermissionId == id);

            if (permission == null)
            {
                return NotFound();
            }

            ViewBag.ReturnUrl = returnUrl;
            return View(permission);
        }

        /// <summary>
        /// تنفيذ الحذف لصلاحية واحدة.
        /// </summary>
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id, string? returnUrl = null)
        {
            var permission = await _context.Permissions.FindAsync(id);
            if (permission != null)
            {
                var oldValues = System.Text.Json.JsonSerializer.Serialize(new { permission.Code, permission.NameAr, permission.Module });
                _context.Permissions.Remove(permission);
                await _context.SaveChangesAsync();

                await _activityLogger.LogAsync(UserActionType.Delete, "Permission", id, $"حذف صلاحية: {permission.NameAr}", oldValues: oldValues);

                TempData["Success"] = "تم حذف الصلاحية.";
            }

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction(nameof(Index));
        }

        // ========= BULK DELETE: حذف مجموعة من الصلاحيات =========

        /// <summary>
        /// حذف جماعي لمجموعة من الصلاحيات بناءً على المعرّفات القادمة من الواجهة.
        /// يُستخدم مع زر "حذف المحدد".
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete([FromForm] int[] ids, string? returnUrl = null)
        {
            if (ids == null || ids.Length == 0)
            {
                TempData["Error"] = "من فضلك اختر على الأقل صلاحية واحدة للحذف.";
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);
                return RedirectToAction(nameof(Index));
            }

            var permissions = await _context.Permissions
                .Where(p => ids.Contains(p.PermissionId))
                .ToListAsync();

            if (permissions.Count == 0)
            {
                TempData["Error"] = "لم يتم العثور على الصلاحيات المحددة.";
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);
                return RedirectToAction(nameof(Index));
            }

            _context.Permissions.RemoveRange(permissions);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم حذف {permissions.Count} صلاحية بنجاح.";
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction(nameof(Index));
        }

        // ========= DELETE ALL: حذف كل الصلاحيات (للبيئة التجريبية) =========

        /// <summary>
        /// حذف جميع الصلاحيات من الجدول.
        /// ⚠ يُفضّل استخدامه في بيئة تجريبية فقط.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll(string? returnUrl = null)
        {
            var all = await _context.Permissions.ToListAsync();
            _context.Permissions.RemoveRange(all);
            await _context.SaveChangesAsync();

            TempData["Success"] = "تم حذف جميع الصلاحيات.";
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction(nameof(Index));
        }







        // ========= EXPORT: تصدير الصلاحيات إلى CSV/Excel =========

        /// <summary>
        /// تصدير الصلاحيات بنفس فلاتر الشاشة (بحث/كود/تاريخ).
        /// format: "excel" أو "csv" (الاتنين حالياً CSV مع اختلاف الاسم فقط).
        /// </summary>
        [HttpGet]
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
            string? filterCol_code = null,
            string? filterCol_name = null,
            string? filterCol_module = null,
            string? filterCol_description = null,
            string? filterCol_created = null,
            string? filterCol_updated = null,
            string format = "excel")
        {
            var query = _context.Permissions.AsNoTracking();

            query = ApplyFilters(query, search, searchBy, searchMode, useDateRange, fromDate, toDate, fromCode, toCode);
            query = ApplyColumnFilters(query, filterCol_id, filterCol_idExpr, filterCol_code, filterCol_name, filterCol_module, filterCol_description, filterCol_created, filterCol_updated);

            // ترتيب ثابت بالتصدير
            var list = await query
                .OrderBy(p => p.PermissionId)
                .ToListAsync();

            // نحدد نوع التصدير المطلوب
            format = (format ?? "excel").ToLowerInvariant();

            // ============= فرع Excel =============
            if (format == "excel")
            {
                // تأكد إن عندك using ClosedXML.Excel; و using System.IO; فى أعلى الملف
                using var workbook = new XLWorkbook();                 // مصنف جديد
                var worksheet = workbook.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("صلاحيات النظام"));

                int row = 1; // الهيدر

                // عناوين الأعمدة بالعربى
                worksheet.Cell(row, 1).Value = "رقم الصلاحية";
                worksheet.Cell(row, 2).Value = "كود الصلاحية";
                worksheet.Cell(row, 3).Value = "الاسم (عربي)";
                worksheet.Cell(row, 4).Value = "الوحدة / الموديول";
                worksheet.Cell(row, 5).Value = "الوصف";
                worksheet.Cell(row, 6).Value = "تاريخ الإنشاء";
                worksheet.Cell(row, 7).Value = "آخر تعديل";

                var headerRange = worksheet.Range(row, 1, row, 7);
                headerRange.Style.Font.Bold = true;

                // البيانات
                foreach (var p in list)
                {
                    row++;

                    worksheet.Cell(row, 1).Value = p.PermissionId;
                    worksheet.Cell(row, 2).Value = p.Code;
                    worksheet.Cell(row, 3).Value = p.NameAr;
                    worksheet.Cell(row, 4).Value = p.Module;
                    worksheet.Cell(row, 5).Value = p.Description;
                    worksheet.Cell(row, 6).Value = p.CreatedAt;
                    worksheet.Cell(row, 7).Value = p.UpdatedAt;
                }

                // ضبط عرض الأعمدة
                worksheet.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                stream.Position = 0;

                var fileNameExcel = ExcelExportNaming.ArabicTimestampedFileName("صلاحيات النظام", ".xlsx");
                const string excelContentType =
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

                return File(stream.ToArray(), excelContentType, fileNameExcel);
            }

            // ============= فرع CSV (النمط القديم) =============
            var sb = new StringBuilder();

            sb.AppendLine("رقم الصلاحية,كود الصلاحية,الاسم (عربي),الوحدة / الموديول,الوصف,تاريخ الإنشاء,آخر تعديل");

            foreach (var p in list)
            {
                // تنظيف النص من علامات " حتى لا تكسر CSV
                string safeCode = (p.Code ?? string.Empty).Replace("\"", "\"\"");
                string safeName = (p.NameAr ?? string.Empty).Replace("\"", "\"\"");
                string safeModule = (p.Module ?? string.Empty).Replace("\"", "\"\"");
                string safeDesc = (p.Description ?? string.Empty).Replace("\"", "\"\"");

                string created = p.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                string updated = p.UpdatedAt.HasValue
                    ? p.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm")
                    : string.Empty;

                sb.AppendLine(
                    $"{p.PermissionId}," +
                    $"\"{safeCode}\"," +
                    $"\"{safeName}\"," +
                    $"\"{safeModule}\"," +
                    $"\"{safeDesc}\"," +
                    $"{created}," +
                    $"{updated}");
            }

            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString());
            string fileName = ExcelExportNaming.ArabicTimestampedFileName("صلاحيات النظام", ".csv");

            return File(bytes, "text/csv", fileName);
        }









        // ========= دالة مساعدة للتحقق من وجود الصلاحية =========

        private bool PermissionExists(int id)
        {
            return _context.Permissions.Any(e => e.PermissionId == id);
        }
    }
}
