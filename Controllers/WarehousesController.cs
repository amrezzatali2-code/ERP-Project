using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;     // SelectList / SelectListItem
using Microsoft.EntityFrameworkCore;
using ERP.Data;
using ERP.Filters;
using ERP.Models;
using ERP.Infrastructure;                     // PagedResult + UserActivityLogger
using ERP.Security;
using ERP.Services;
using ERP.Services.Caching;
using System.IO;                 // MemoryStream
using System.Text;               // StringBuilder + Encoding للـ CSV
using System.Globalization;      // CultureInfo لو احتجنا تنسيق أرقام
using ClosedXML.Excel;           // مكتبة Excel


namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إدارة جدول المخازن (Warehouses)
    /// تطبيق نظام القوائم الموحد مع فورم SHOW:
    ///  - بحث + ترتيب + ترقيم صفحات
    ///  - فلترة بالتاريخ/الوقت (تاريخ الإنشاء)
    ///  - اختيار الأعمدة + حذف المحدد + حذف الكل
    /// </summary>
    [RequirePermission("Warehouses.Index")]
    public class WarehousesController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IUserActivityLogger _activityLogger;
        private readonly ILookupCacheService _lookupCache;
        private readonly IPermissionService _permissionService;

        public WarehousesController(
            AppDbContext db,
            IUserActivityLogger activityLogger,
            ILookupCacheService lookupCache,
            IPermissionService permissionService)
        {
            _db = db;
            _activityLogger = activityLogger;
            _lookupCache = lookupCache;
            _permissionService = permissionService;
        }

        // =========================
        // Index — قائمة المخازن
        // =========================
        [HttpGet]
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "name",
            string? searchMode = "contains",
            string? sort = "name",
            string? dir = "asc",
            int page = 1,
            int pageSize = 10,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_name = null,
            string? filterCol_branch = null,
            string? filterCol_active = null,
            string? filterCol_created = null,
            string? filterCol_modified = null)
        {
            var pageSizeQuery = Request.Query["pageSize"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageSizeQuery) && int.TryParse(pageSizeQuery, out var psVal))
                pageSize = psVal;

            var smRaw = Request.Query["searchMode"];
            if (smRaw.Count > 0)
            {
                static string NormSm(string? v)
                {
                    if (string.IsNullOrWhiteSpace(v)) return "";
                    var t = v.Trim().ToLowerInvariant();
                    if (t == "startswith") t = "starts";
                    if (t == "eq" || t == "equals") t = "contains";
                    if (t != "contains" && t != "starts" && t != "ends") t = "contains";
                    return t;
                }
                if (smRaw.Count == 1 && !string.IsNullOrEmpty(smRaw[0]))
                    searchMode = smRaw[0];
                else
                {
                    var norms = smRaw.Select(NormSm).Where(x => x.Length > 0).ToList();
                    if (norms.Count == 0) { }
                    else if (norms.Distinct().Count() == 1)
                        searchMode = norms[0];
                    else if (norms.Contains("contains"))
                        searchMode = "contains";
                    else
                        searchMode = norms[^1];
                }
            }

            var s = (search ?? string.Empty).Trim();
            var sb = (searchBy ?? "name").Trim().ToLowerInvariant();
            if (sb == "updated") sb = "modified";
            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm == "startswith") sm = "starts";
            if (sm == "eq" || sm == "equals") sm = "contains";
            if (sm != "contains" && sm != "starts" && sm != "ends")
                sm = "contains";

            var so = (sort ?? "name").Trim().ToLowerInvariant();
            var desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            page = page <= 0 ? 1 : page;
            if (pageSize < 0) pageSize = 10;
            if (pageSize > 0 && pageSize != 10 && pageSize != 25 && pageSize != 50 && pageSize != 100 && pageSize != 200)
                pageSize = 10;

            IQueryable<Warehouse> q = _db.Warehouses.AsNoTracking().Include(w => w.Branch);
            q = ApplyWarehouseFilters(q, s, sb, sm, useDateRange, fromDate, toDate, fromCode, toCode,
                filterCol_id, filterCol_name, filterCol_branch, filterCol_active, filterCol_created, filterCol_modified);

            var activeFiltered = await q.CountAsync(w => w.IsActive);
            var inactiveFiltered = await q.CountAsync(w => !w.IsActive);

            q = ApplyWarehouseSort(q, so, desc);

            var model = await PagedResult<Warehouse>.CreateAsync(q, page, pageSize);
            model.Search = s;
            model.SearchBy = sb;
            model.SortColumn = so;
            model.SortDescending = desc;
            model.UseDateRange = useDateRange;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            ViewBag.SearchMode = sm;
            ViewBag.PageSize = model.PageSize;
            ViewBag.ActiveFilteredCount = activeFiltered;
            ViewBag.InactiveFilteredCount = inactiveFiltered;

            ViewBag.CanCreate = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Warehouses", "Create"));
            ViewBag.CanEdit = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Warehouses", "Edit"));
            ViewBag.CanShow = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Warehouses", "Show"));
            ViewBag.CanDelete = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Warehouses", "Delete"));
            ViewBag.CanBulkDelete = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Warehouses", "BulkDelete"));
            ViewBag.CanDeleteAll = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Warehouses", "DeleteAll"));
            ViewBag.CanExport = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Warehouses", "Export"));

            ViewBag.SearchOptions = new[]
            {
                new SelectListItem("الاسم", "name", sb == "name"),
                new SelectListItem("المعرّف", "id", sb == "id"),
                new SelectListItem("الفرع", "branch", sb == "branch"),
                new SelectListItem("الفعالية", "active", sb == "active"),
                new SelectListItem("تاريخ الإنشاء", "created", sb == "created"),
                new SelectListItem("آخر تعديل", "modified", sb == "modified"),
            };

            ViewBag.SortOptions = new[]
            {
                new SelectListItem("الاسم", "name", so == "name"),
                new SelectListItem("المعرّف", "id", so == "id"),
                new SelectListItem("الفرع", "branch", so == "branch"),
                new SelectListItem("الفعالية", "active", so == "active"),
                new SelectListItem("تاريخ الإنشاء", "created", so == "created"),
                new SelectListItem("آخر تعديل", "modified", so == "modified"),
            };

            ViewBag.Search = s;
            ViewBag.SearchBy = sb;
            ViewBag.Sort = so;
            ViewBag.Dir = desc ? "desc" : "asc";
            ViewBag.Page = page;
            ViewBag.Total = model.TotalCount;
            ViewBag.UseDateRange = useDateRange;
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_Name = filterCol_name;
            ViewBag.FilterCol_Branch = filterCol_branch;
            ViewBag.FilterCol_Active = filterCol_active;
            ViewBag.FilterCol_Created = filterCol_created;
            ViewBag.FilterCol_Modified = filterCol_modified;

            return View(model);
        }

        private static IQueryable<Warehouse> ApplyWarehouseFilters(
            IQueryable<Warehouse> q,
            string s,
            string sb,
            string sm,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            int? fromCode,
            int? toCode,
            string? filterCol_id,
            string? filterCol_name,
            string? filterCol_branch,
            string? filterCol_active,
            string? filterCol_created,
            string? filterCol_modified)
        {
            var likeContains = $"%{s}%";
            var likeStarts = $"{s}%";
            var likeEnds = $"%{s}";
            var pattern = sm == "starts" ? likeStarts : sm == "ends" ? likeEnds : likeContains;

            if (!string.IsNullOrEmpty(s))
            {
                switch (sb)
                {
                    case "id":
                        if (int.TryParse(s, out var wid))
                            q = q.Where(w => w.WarehouseId == wid);
                        else if (sm == "starts")
                            q = q.Where(w => w.WarehouseId.ToString().StartsWith(s));
                        else if (sm == "ends")
                            q = q.Where(w => w.WarehouseId.ToString().EndsWith(s));
                        else
                            q = q.Where(w => w.WarehouseId.ToString().Contains(s));
                        break;

                    case "branch":
                        q = q.Where(w =>
                            w.Branch != null &&
                            w.Branch.BranchName != null &&
                            EF.Functions.Like(w.Branch.BranchName, pattern));
                        break;

                    case "active":
                        var yes = new[] { "1", "نعم", "yes", "true", "فعال" };
                        var no = new[] { "0", "لا", "no", "false", "غير" };
                        if (yes.Contains(s, StringComparer.OrdinalIgnoreCase))
                            q = q.Where(w => w.IsActive);
                        else if (no.Contains(s, StringComparer.OrdinalIgnoreCase))
                            q = q.Where(w => !w.IsActive);
                        else
                            q = q.Where(w => false);
                        break;

                    case "created":
                        q = q.Where(w => EF.Functions.Like(w.CreatedAt.ToString("yyyy/MM/dd HH:mm"), pattern));
                        break;

                    case "modified":
                        q = q.Where(w =>
                            EF.Functions.Like((w.UpdatedAt ?? w.CreatedAt).ToString("yyyy/MM/dd HH:mm"), pattern));
                        break;

                    case "name":
                    default:
                        q = q.Where(w =>
                            w.WarehouseName != null &&
                            EF.Functions.Like(w.WarehouseName, pattern));
                        break;
                }
            }

            if (fromCode.HasValue)
                q = q.Where(w => w.WarehouseId >= fromCode.Value);
            if (toCode.HasValue)
                q = q.Where(w => w.WarehouseId <= toCode.Value);

            if (useDateRange)
            {
                if (fromDate.HasValue)
                    q = q.Where(w => w.CreatedAt >= fromDate.Value);
                if (toDate.HasValue)
                    q = q.Where(w => w.CreatedAt <= toDate.Value);
            }

            var sep = new[] { '|', ',' };
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? (int?)v : null)
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToList();
                if (ids.Count > 0)
                    q = q.Where(w => ids.Contains(w.WarehouseId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_name))
            {
                var vals = filterCol_name.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(w => vals.Contains(w.WarehouseName ?? ""));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_branch))
            {
                var vals = filterCol_branch.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(w => w.Branch != null && vals.Contains(w.Branch.BranchName ?? ""));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_active))
            {
                var vals = filterCol_active.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim().ToLowerInvariant())
                    .ToList();
                var activeList = new List<string> { "نشط", "active", "1", "نعم", "yes", "true" };
                var inactiveList = new List<string> { "موقوف", "inactive", "0", "لا", "no", "false" };
                var wantActive = vals.Any(v => activeList.Contains(v));
                var wantInactive = vals.Any(v => inactiveList.Contains(v));
                if (wantActive && !wantInactive)
                    q = q.Where(w => w.IsActive);
                else if (wantInactive && !wantActive)
                    q = q.Where(w => !w.IsActive);
                else if (!wantActive && !wantInactive)
                    q = q.Where(w => false);
            }
            if (!string.IsNullOrWhiteSpace(filterCol_created))
            {
                var yearMonthKeys = new List<int>();
                foreach (var part in filterCol_created.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()))
                {
                    if (part.Length >= 7 && part[4] == '-' && int.TryParse(part.AsSpan(0, 4), out var y) && int.TryParse(part.AsSpan(5, 2), out var m))
                        yearMonthKeys.Add(y * 100 + m);
                }
                if (yearMonthKeys.Count > 0)
                    q = q.Where(w => yearMonthKeys.Contains(w.CreatedAt.Year * 100 + w.CreatedAt.Month));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_modified))
            {
                var yearMonthKeys = new List<int>();
                foreach (var part in filterCol_modified.Split(sep, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()))
                {
                    if (part.Length >= 7 && part[4] == '-' && int.TryParse(part.AsSpan(0, 4), out var y) && int.TryParse(part.AsSpan(5, 2), out var m))
                        yearMonthKeys.Add(y * 100 + m);
                }
                if (yearMonthKeys.Count > 0)
                    q = q.Where(w => yearMonthKeys.Contains((w.UpdatedAt ?? w.CreatedAt).Year * 100 + (w.UpdatedAt ?? w.CreatedAt).Month));
            }

            return q;
        }

        private static IQueryable<Warehouse> ApplyWarehouseSort(IQueryable<Warehouse> q, string so, bool desc) =>
            so switch
            {
                "id" => desc
                    ? q.OrderByDescending(w => w.WarehouseId)
                    : q.OrderBy(w => w.WarehouseId),
                "branch" => desc
                    ? q.OrderByDescending(w => w.Branch != null ? w.Branch.BranchName ?? "" : "")
                    : q.OrderBy(w => w.Branch != null ? w.Branch.BranchName ?? "" : ""),
                "active" => desc
                    ? q.OrderByDescending(w => w.IsActive).ThenBy(w => w.WarehouseName)
                    : q.OrderBy(w => w.IsActive).ThenBy(w => w.WarehouseName),
                "created" => desc
                    ? q.OrderByDescending(w => w.CreatedAt)
                    : q.OrderBy(w => w.CreatedAt),
                "modified" => desc
                    ? q.OrderByDescending(w => w.UpdatedAt ?? w.CreatedAt)
                    : q.OrderBy(w => w.UpdatedAt ?? w.CreatedAt),
                "name" or _ => desc
                    ? q.OrderByDescending(w => w.WarehouseName)
                    : q.OrderBy(w => w.WarehouseName),
            };

        // =========================================================
        // API: جلب القيم المميزة لعمود (للفلترة بنمط Excel)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var q = _db.Warehouses.Include(w => w.Branch).AsNoTracking();

            List<(string Value, string Display)> items = column?.ToLowerInvariant() switch
            {
                "id" => (await q.Select(w => w.WarehouseId).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "name" => string.IsNullOrEmpty(searchTerm)
                    ? (await q.Where(w => !string.IsNullOrWhiteSpace(w.WarehouseName)).Select(w => w.WarehouseName!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v, v)).ToList()
                    : (await q.Where(w => w.WarehouseName != null && EF.Functions.Like(w.WarehouseName, "%" + searchTerm + "%")).Select(w => w.WarehouseName!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v!, v)).ToList(),
                "branch" => (await q.Where(w => w.Branch != null && !string.IsNullOrEmpty(w.Branch.BranchName)).Select(w => w.Branch!.BranchName!).Distinct().OrderBy(x => x).Take(500).ToListAsync())
                    .Select(x => (x, x)).ToList(),
                "active" => new List<(string, string)> { ("نشط", "نشط"), ("موقوف", "موقوف") },
                "created" => (await q.Select(w => new { w.CreatedAt.Year, w.CreatedAt.Month }).Distinct().OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).Take(100).ToListAsync())
                    .Select(x => ($"{x.Year}-{x.Month:D2}", $"{x.Year}/{x.Month:D2}")).ToList(),
                "modified" => (await q.Select(w => new { Year = (w.UpdatedAt ?? w.CreatedAt).Year, Month = (w.UpdatedAt ?? w.CreatedAt).Month }).Distinct().OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).Take(100).ToListAsync())
                    .Select(x => ($"{x.Year}-{x.Month:D2}", $"{x.Year}/{x.Month:D2}")).ToList(),
                _ => new List<(string Value, string Display)>(),
            };

            return Json(items.Select(x => new { value = x.Value, display = x.Display }));
        }

        // =========================
        // Show — عرض تفاصيل مخزن
        // =========================
        [HttpGet]
        public async Task<IActionResult> Show(int id)
        {
            if (id <= 0) return NotFound();

            var w = await _db.Warehouses
                             .AsNoTracking()
                             .Include(x => x.Branch)
                             .FirstOrDefaultAsync(x => x.WarehouseId == id);

            if (w == null) return NotFound();

            return View(w);   // View بسيطة تعرض بيانات المخزن (نقدر نعملها بعدين)
        }

        // =========================
        // Create — إضافة مخزن
        // =========================
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            // تحميل الفروع للكومبو
            await LoadBranchesDDL(null);
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("WarehouseName,BranchId,IsActive,Notes")] Warehouse w)
        {
            // ملاحظة: لا نسمح بإدخال WarehouseId من الفورم لأنه Identity

            if (!ModelState.IsValid)
            {
                await LoadBranchesDDL(w.BranchId);
                return View(w);
            }

            // ضبط تاريخ الإنشاء صراحة (حتى لو له قيمة افتراضية في الموديل)
            w.CreatedAt = DateTime.UtcNow;
            w.UpdatedAt = null;

            _db.Warehouses.Add(w);
            await _db.SaveChangesAsync();
            _lookupCache.ClearWarehousesCache();

            await _activityLogger.LogAsync(UserActionType.Create, "Warehouse", w.WarehouseId, $"إنشاء مخزن جديد: {w.WarehouseName}");

            TempData["Ok"] = "تمت إضافة المخزن بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // Edit — تعديل مخزن
        // =========================
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            if (id <= 0) return NotFound();

            var w = await _db.Warehouses.FindAsync(id);
            if (w == null) return NotFound();

            await LoadBranchesDDL(w.BranchId);
            return View(w);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("WarehouseId,WarehouseName,BranchId,IsActive,Notes")] Warehouse w)
        {
            if (id != w.WarehouseId) return NotFound();   // حماية من التلاعب في الفورم

            if (!ModelState.IsValid)
            {
                await LoadBranchesDDL(w.BranchId);
                return View(w);
            }

            // جلب النسخة الأصلية من الداتا بيز للحفاظ على CreatedAt
            var existing = await _db.Warehouses.FindAsync(id);
            if (existing == null) return NotFound();

            var oldValues = System.Text.Json.JsonSerializer.Serialize(new { existing.WarehouseName, existing.BranchId, existing.IsActive });
            existing.WarehouseName = w.WarehouseName;
            existing.BranchId = w.BranchId;
            existing.IsActive = w.IsActive;
            existing.Notes = w.Notes;
            existing.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            _lookupCache.ClearWarehousesCache();

            var newValues = System.Text.Json.JsonSerializer.Serialize(new { existing.WarehouseName, existing.BranchId, existing.IsActive });
            await _activityLogger.LogAsync(UserActionType.Edit, "Warehouse", existing.WarehouseId, $"تعديل مخزن: {existing.WarehouseName}", oldValues, newValues);

            TempData["Ok"] = "تم تعديل بيانات المخزن.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // Delete — حذف مخزن منفرد
        // =========================
        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            if (id <= 0) return NotFound();

            var w = await _db.Warehouses
                             .AsNoTracking()
                             .Include(x => x.Branch)
                             .FirstOrDefaultAsync(x => x.WarehouseId == id);

            if (w == null) return NotFound();

            return View(w);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var w = await _db.Warehouses.FindAsync(id);
            if (w == null) return NotFound();

            var oldValues = System.Text.Json.JsonSerializer.Serialize(new { w.WarehouseName });
            _db.Warehouses.Remove(w);
            await _db.SaveChangesAsync();
            _lookupCache.ClearWarehousesCache();

            await _activityLogger.LogAsync(UserActionType.Delete, "Warehouse", id, $"حذف مخزن: {w.WarehouseName}", oldValues: oldValues);

            TempData["Ok"] = "تم حذف السجل.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // BulkDelete — حذف مجموعة مخازن
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Warehouses.BulkDelete")]
        public async Task<IActionResult> BulkDelete(string? selectedIds)
        {
            // selectedIds تأتي من الفورم كقائمة "1,3,5,7"
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["Error"] = "لم يتم اختيار أي مخزن للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var ids = selectedIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x, out var n) ? n : (int?)null)
                .Where(n => n.HasValue)
                .Select(n => n!.Value)
                .ToList();

            if (!ids.Any())
            {
                TempData["Error"] = "قائمة المعرفات غير صحيحة.";
                return RedirectToAction(nameof(Index));
            }

            var warehouses = await _db.Warehouses
                                      .Where(w => ids.Contains(w.WarehouseId))
                                      .ToListAsync();

            if (warehouses.Any())
            {
                _db.Warehouses.RemoveRange(warehouses);
                await _db.SaveChangesAsync();
                _lookupCache.ClearWarehousesCache();
                TempData["Ok"] = $"تم حذف {warehouses.Count} مخزن/مخازن.";
            }
            else
            {
                TempData["Error"] = "لم يتم العثور على المخازن المحددة.";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================
        // DeleteAll — حذف كل المخازن
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Warehouses.DeleteAll")]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _db.Warehouses.ToListAsync();

            if (!all.Any())
            {
                TempData["Ok"] = "لا توجد مخازن لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            _db.Warehouses.RemoveRange(all);
            await _db.SaveChangesAsync();
            _lookupCache.ClearWarehousesCache();

            TempData["Ok"] = "تم حذف جميع المخازن من النظام.";
            return RedirectToAction(nameof(Index));
        }





        // =========================
        // Export — تصدير قائمة المخازن (Excel أو CSV)
        // =========================
        [HttpGet]
        [RequirePermission("Warehouses.Export")]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy = "name",
            string? searchMode = "contains",
            string? sort = "name",
            string? dir = "asc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_name = null,
            string? filterCol_branch = null,
            string? filterCol_active = null,
            string? filterCol_created = null,
            string? filterCol_modified = null,
            string? format = "excel")
        {
            var s = (search ?? string.Empty).Trim();
            var sb = (searchBy ?? "name").Trim().ToLowerInvariant();
            if (sb == "updated") sb = "modified";
            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm == "startswith") sm = "starts";
            if (sm == "eq" || sm == "equals") sm = "contains";
            if (sm != "contains" && sm != "starts" && sm != "ends")
                sm = "contains";

            var so = (sort ?? "name").Trim().ToLowerInvariant();
            var desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            IQueryable<Warehouse> q = _db.Warehouses.AsNoTracking().Include(w => w.Branch);
            q = ApplyWarehouseFilters(q, s, sb, sm, useDateRange, fromDate, toDate, fromCode, toCode,
                filterCol_id, filterCol_name, filterCol_branch, filterCol_active, filterCol_created, filterCol_modified);
            q = ApplyWarehouseSort(q, so, desc);

            var rows = await q.ToListAsync();

            format = (format ?? "excel").Trim().ToLowerInvariant();

            // ============= CSV (يُحوَّل إلى PDF عبر PdfExportMiddleware عند format=pdf في الطلب الأصلي) =============
            if (format == "csv")
            {
                var sbCsv = new StringBuilder();    // متغير: بناء نص CSV

                // عناوين الأعمدة
                sbCsv.AppendLine(string.Join(",",
                    Csv("كود المخزن"),
                    Csv("اسم المخزن"),
                    Csv("اسم الفرع"),
                    Csv("فعال؟"),
                    Csv("تاريخ الإنشاء"),
                    Csv("آخر تعديل"),
                    Csv("ملاحظات")
                ));

                // البيانات
                foreach (var w in rows)
                {
                    sbCsv.AppendLine(string.Join(",",
                        Csv(w.WarehouseId.ToString()),                   // كود المخزن
                        Csv(w.WarehouseName),                            // اسم المخزن
                        Csv(w.Branch?.BranchName),                       // اسم الفرع
                        Csv(w.IsActive ? "نشط" : "موقوف"),               // الحالة
                        Csv(w.CreatedAt.ToString("yyyy-MM-dd HH:mm")),   // تاريخ الإنشاء
                        Csv(w.UpdatedAt?.ToString("yyyy-MM-dd HH:mm")),   // آخر تعديل
                        Csv(w.Notes)                                     // الملاحظات
                    ));
                }

                var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true); // UTF-8 + BOM
                var bytes = utf8.GetBytes(sbCsv.ToString());
                var fileNameCsv = ExcelExportNaming.ArabicTimestampedFileName("المخازن", ".csv");

                return File(bytes, "text/csv; charset=utf-8", fileNameCsv);
            }

            // ============= Excel =============
            using var workbook = new XLWorkbook();                 // متغير: ملف Excel
            var ws = workbook.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("المخازن"));

            int r = 1; // متغير: رقم الصف الحالي

            // عناوين الأعمدة
            ws.Cell(r, 1).Value = "كود المخزن";
            ws.Cell(r, 2).Value = "اسم المخزن";
            ws.Cell(r, 3).Value = "اسم الفرع";
            ws.Cell(r, 4).Value = "فعال؟";
            ws.Cell(r, 5).Value = "تاريخ الإنشاء";
            ws.Cell(r, 6).Value = "آخر تعديل";
            ws.Cell(r, 7).Value = "ملاحظات";

            var headerRange = ws.Range(r, 1, r, 7);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // البيانات
            foreach (var w in rows)
            {
                r++;

                ws.Cell(r, 1).Value = w.WarehouseId;
                ws.Cell(r, 2).Value = w.WarehouseName;
                ws.Cell(r, 3).Value = w.Branch?.BranchName ?? "";
                ws.Cell(r, 4).Value = w.IsActive ? "نشط" : "موقوف";
                ws.Cell(r, 5).Value = w.CreatedAt;
                ws.Cell(r, 6).Value = w.UpdatedAt;
                ws.Cell(r, 7).Value = w.Notes ?? "";
            }

            ws.Column(5).Style.DateFormat.Format = "yyyy-MM-dd HH:mm";
            ws.Column(6).Style.DateFormat.Format = "yyyy-MM-dd HH:mm";
            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();   // متغير: ستريم في الذاكرة
            workbook.SaveAs(stream);
            stream.Position = 0;

            var fileNameXlsx = ExcelExportNaming.ArabicTimestampedFileName("المخازن", ".xlsx");
            const string contentTypeXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

            return File(stream.ToArray(), contentTypeXlsx, fileNameXlsx);
        }

        // دالة مساعدة للـ CSV
        private static string Csv(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            var s = value.Replace("\"", "\"\""); // هروب علامة "

            if (s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
                return "\"" + s + "\"";

            return s;
        }





        // =========================
        // دالة مساعدة: تحميل قائمة الفروع للـ DDL
        // =========================
        private async Task LoadBranchesDDL(int? selectedId = null)
        {
            var branches = await _db.Branches
                                    .AsNoTracking()
                                    .OrderBy(b => b.BranchName)
                                    .ToListAsync();

            // BranchId الآن int — نمرره كما هو
            ViewBag.BranchId = new SelectList(branches, "BranchId", "BranchName", selectedId);
        }
    }
}
