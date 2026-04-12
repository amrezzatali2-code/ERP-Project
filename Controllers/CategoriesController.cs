using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;         // SelectListItem
using Microsoft.EntityFrameworkCore;
using ERP.Data;
using ERP.Filters;
using ERP.Models;
using ERP.Infrastructure;                         // PagedResult + UserActivityLogger
using ERP.Security;
using ERP.Services;
using System.IO;                 // MemoryStream
using System.Text;               // StringBuilder + Encoding للـ CSV
using System.Globalization;      // CultureInfo لو احتجنا تنسيق أرقام
using ClosedXML.Excel;           // مكتبة Excel


namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر فئات الأصناف — عرض/بحث/ترتيب/ترقيم + إضافة/تعديل/حذف
    /// باستخدام نظام القوائم الموحد مع فورم SHOW.
    /// </summary>
    [RequirePermission("Categories.Index")]
    public class CategoriesController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IUserActivityLogger _activityLogger;
        private readonly IPermissionService _permissionService;
        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        public CategoriesController(AppDbContext db, IUserActivityLogger activityLogger, IPermissionService permissionService)
        {
            _db = db;
            _activityLogger = activityLogger;
            _permissionService = permissionService;
        }

        // =========================
        // Index — قائمة الفئات (نمط موحّد مع المخازن: searchMode، حجم صفحة، كروت، صلاحيات)
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

            IQueryable<Category> q = _db.Categories.AsNoTracking();
            q = ApplyCategoryFilters(q, s, sb, sm, useDateRange, fromDate, toDate, fromCode, toCode,
                filterCol_id, filterCol_name, filterCol_created, filterCol_modified);

            var totalForStats = await q.CountAsync();
            var withEditCount = await q.CountAsync(x => x.UpdatedAt != null);
            var noEditCount = totalForStats - withEditCount;

            q = ApplyCategorySort(q, so, desc);

            var model = await PagedResult<Category>.CreateAsync(q, page, pageSize);
            model.Search = s;
            model.SearchBy = sb;
            model.SortColumn = so;
            model.SortDescending = desc;
            model.UseDateRange = useDateRange;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            ViewBag.SearchMode = sm;
            ViewBag.PageSize = model.PageSize;
            ViewBag.WithEditCount = withEditCount;
            ViewBag.NoEditCount = noEditCount;

            ViewBag.CanCreate = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Categories", "Create"));
            ViewBag.CanEdit = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Categories", "Edit"));
            ViewBag.CanShow = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Categories", "Show"));
            ViewBag.CanDelete = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Categories", "Delete"));
            ViewBag.CanBulkDelete = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Categories", "BulkDelete"));
            ViewBag.CanDeleteAll = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Categories", "DeleteAll"));
            ViewBag.CanExport = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Categories", "Export"));

            ViewBag.SearchOptions = new[]
            {
                new SelectListItem("اسم الفئة", "name", sb == "name"),
                new SelectListItem("كود الفئة", "id", sb == "id"),
                new SelectListItem("الكل", "all", sb == "all"),
                new SelectListItem("تاريخ الإنشاء", "created", sb == "created"),
                new SelectListItem("آخر تعديل", "modified", sb == "modified"),
            };

            ViewBag.SortOptions = new[]
            {
                new SelectListItem("الاسم", "name", so == "name"),
                new SelectListItem("الرقم", "id", so == "id"),
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
            ViewBag.FilterCol_Created = filterCol_created;
            ViewBag.FilterCol_Modified = filterCol_modified;

            return View(model);
        }

        private IQueryable<Category> ApplyCategoryFilters(
            IQueryable<Category> q,
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
                        if (int.TryParse(s, out var cid))
                            q = q.Where(x => x.CategoryId == cid);
                        else if (sm == "starts")
                            q = q.Where(x => EF.Functions.Like(x.CategoryId.ToString(), likeStarts));
                        else if (sm == "ends")
                            q = q.Where(x => EF.Functions.Like(x.CategoryId.ToString(), likeEnds));
                        else
                            q = q.Where(x => EF.Functions.Like(x.CategoryId.ToString(), likeContains));
                        break;

                    case "created":
                        q = q.Where(x => EF.Functions.Like(x.CreatedAt.ToString("yyyy/MM/dd HH:mm"), pattern));
                        break;

                    case "modified":
                        q = q.Where(x => EF.Functions.Like((x.UpdatedAt ?? x.CreatedAt).ToString("yyyy/MM/dd HH:mm"), pattern));
                        break;

                    case "all":
                        q = q.Where(x =>
                            (x.CategoryName != null && EF.Functions.Like(x.CategoryName!, pattern)) ||
                            EF.Functions.Like(x.CategoryId.ToString(), pattern));
                        break;

                    case "name":
                    default:
                        q = q.Where(x => x.CategoryName != null && EF.Functions.Like(x.CategoryName!, pattern));
                        break;
                }
            }

            if (useDateRange)
            {
                if (fromDate.HasValue)
                    q = q.Where(x => x.CreatedAt >= fromDate.Value);
                if (toDate.HasValue)
                    q = q.Where(x => x.CreatedAt <= toDate.Value);
            }

            if (fromCode.HasValue)
                q = q.Where(x => x.CategoryId >= fromCode.Value);
            if (toCode.HasValue)
                q = q.Where(x => x.CategoryId <= toCode.Value);

            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToList();
                if (ids.Count > 0)
                    q = q.Where(x => ids.Contains(x.CategoryId));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_name))
            {
                var vals = filterCol_name.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(x => vals.Contains(x.CategoryName));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_created))
            {
                var parts = filterCol_created.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => x.Length >= 7)
                    .ToList();
                var dateFilters = new List<(int Year, int Month)>();
                foreach (var p in parts)
                {
                    if (p.Length == 7 && p[4] == '-' &&
                        int.TryParse(p.Substring(0, 4), out var y) &&
                        int.TryParse(p.Substring(5, 2), out var m) &&
                        m >= 1 && m <= 12)
                        dateFilters.Add((y, m));
                }
                if (dateFilters.Count > 0)
                    q = q.Where(x => dateFilters.Any(df => x.CreatedAt.Year == df.Year && x.CreatedAt.Month == df.Month));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_modified))
            {
                var parts = filterCol_modified.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => x.Length >= 7)
                    .ToList();
                var dateFilters = new List<(int Year, int Month)>();
                foreach (var p in parts)
                {
                    if (p.Length == 7 && p[4] == '-' &&
                        int.TryParse(p.Substring(0, 4), out var y) &&
                        int.TryParse(p.Substring(5, 2), out var m) &&
                        m >= 1 && m <= 12)
                        dateFilters.Add((y, m));
                }
                if (dateFilters.Count > 0)
                    q = q.Where(x =>
                        dateFilters.Any(df =>
                            (x.UpdatedAt ?? x.CreatedAt).Year == df.Year &&
                            (x.UpdatedAt ?? x.CreatedAt).Month == df.Month));
            }

            return q;
        }

        private static IQueryable<Category> ApplyCategorySort(IQueryable<Category> q, string so, bool desc) =>
            so switch
            {
                "id" => desc
                    ? q.OrderByDescending(x => x.CategoryId)
                    : q.OrderBy(x => x.CategoryId),
                "created" => desc
                    ? q.OrderByDescending(x => x.CreatedAt)
                    : q.OrderBy(x => x.CreatedAt),
                "modified" => desc
                    ? q.OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
                    : q.OrderBy(x => x.UpdatedAt ?? x.CreatedAt),
                "name" or _ => desc
                    ? q.OrderByDescending(x => x.CategoryName)
                    : q.OrderBy(x => x.CategoryName),
            };

        /// <summary>
        /// API: جلب القيم المميزة لعمود (للفلترة بنمط Excel).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var q = _db.Categories.AsNoTracking();

            List<(string Value, string Display)> items = column?.ToLowerInvariant() switch
            {
                "id" => (await q.Select(x => x.CategoryId).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "name" => string.IsNullOrEmpty(searchTerm)
                    ? (await q.Where(x => x.CategoryName != null).Select(x => x.CategoryName!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v, v)).ToList()
                    : (await q.Where(x => x.CategoryName != null && x.CategoryName.ToLower().Contains(searchTerm)).Select(x => x.CategoryName!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v!, v)).ToList(),
                "created" => (await q.Select(x => new { x.CreatedAt.Year, x.CreatedAt.Month }).Distinct()
                    .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).Take(100).ToListAsync())
                    .Select(x => ($"{x.Year}-{x.Month:D2}", $"{x.Year}/{x.Month:D2}")).ToList(),
                "modified" => (await q.Select(x => new { Year = (x.UpdatedAt ?? x.CreatedAt).Year, Month = (x.UpdatedAt ?? x.CreatedAt).Month }).Distinct()
                    .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).Take(100).ToListAsync())
                    .Select(x => ($"{x.Year}-{x.Month:D2}", $"{x.Year}/{x.Month:D2}")).ToList(),
                _ => new List<(string Value, string Display)>()
            };

            if (!string.IsNullOrEmpty(searchTerm) && column?.ToLowerInvariant() == "name")
            {
                items = items.Where(x => (x.Display ?? x.Value).ToLowerInvariant().Contains(searchTerm)).ToList();
            }

            return Json(items.Select(x => new { value = x.Value, display = x.Display }));
        }

        // =========================
        // Show — عرض تفاصيل فئة (لـ فورم SHOW)
        // =========================
        [HttpGet]
        public async Task<IActionResult> Show(int id)
        {
            var cat = await _db.Categories
                               .AsNoTracking()
                               .FirstOrDefaultAsync(x => x.CategoryId == id);
            if (cat == null) return NotFound();

            return View(cat);   // نعمل View باسم Show لاحقاً أو نستخدمه كعرض بسيط
        }

        // (اختياري) الإبقاء على Details لو مستخدم في حتة تانية
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var cat = await _db.Categories
                               .AsNoTracking()
                               .FirstOrDefaultAsync(x => x.CategoryId == id);
            if (cat == null) return NotFound();
            return View(cat);
        }

        // =========================
        // Create — إضافة فئة جديدة
        // =========================
        [HttpGet]
        public IActionResult Create() => View();  // عرض فورم الإضافة

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("CategoryName")] Category cat)
        {
            // تعليق: CategoryId رقم هوية Identity — لا يدخله المستخدم

            if (!ModelState.IsValid) return View(cat);

            // حماية اختيارية: منع تكرار اسم الفئة
            bool nameExists = await _db.Categories
                                       .AnyAsync(x => x.CategoryName == cat.CategoryName);
            if (nameExists)
            {
                ModelState.AddModelError(nameof(cat.CategoryName), "اسم الفئة موجود بالفعل.");
                return View(cat);
            }

            // ضبط التواريخ
            cat.CreatedAt = DateTime.UtcNow;
            cat.UpdatedAt = null;

            _db.Categories.Add(cat);
            await _db.SaveChangesAsync();

            await _activityLogger.LogAsync(UserActionType.Create, "Category", cat.CategoryId, $"إنشاء فئة جديدة: {cat.CategoryName}");

            TempData["Ok"] = "تمت إضافة الفئة بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // Edit — تعديل فئة
        // =========================
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var cat = await _db.Categories.FindAsync(id);
            if (cat == null) return NotFound();
            return View(cat);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("CategoryId,CategoryName")] Category cat)
        {
            if (id != cat.CategoryId) return NotFound();   // حماية من التلاعب في الـ Id

            if (!ModelState.IsValid) return View(cat);

            // جلب السجل الأصلي للحفاظ على CreatedAt
            var existing = await _db.Categories.FindAsync(id);
            if (existing == null) return NotFound();

            // حماية اختيارية: منع تكرار اسم الفئة مع فئات أخرى
            bool nameExists = await _db.Categories
                                       .AnyAsync(x => x.CategoryId != id &&
                                                      x.CategoryName == cat.CategoryName);
            if (nameExists)
            {
                ModelState.AddModelError(nameof(cat.CategoryName), "اسم الفئة موجود بالفعل.");
                return View(cat);
            }

            var oldName = existing.CategoryName;
            existing.CategoryName = cat.CategoryName;
            existing.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            await _activityLogger.LogAsync(
                UserActionType.Edit,
                "Category",
                existing.CategoryId,
                $"تعديل فئة: {cat.CategoryName}",
                System.Text.Json.JsonSerializer.Serialize(new { CategoryName = oldName }),
                System.Text.Json.JsonSerializer.Serialize(new { CategoryName = existing.CategoryName }));

            TempData["Ok"] = "تم تعديل بيانات الفئة.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // Delete — حذف فئة مفردة
        // =========================
        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            var cat = await _db.Categories
                               .AsNoTracking()
                               .FirstOrDefaultAsync(x => x.CategoryId == id);
            if (cat == null) return NotFound();

            return View(cat);   // صفحة تأكيد الحذف
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var cat = await _db.Categories.FindAsync(id);
            if (cat == null) return NotFound();

            try
            {
                var oldValues = System.Text.Json.JsonSerializer.Serialize(new { cat.CategoryName });
                _db.Categories.Remove(cat);
                await _db.SaveChangesAsync();

                await _activityLogger.LogAsync(UserActionType.Delete, "Category", id, $"حذف فئة: {cat.CategoryName}", oldValues: oldValues);

                TempData["Ok"] = "تم حذف السجل.";
            }
            catch (DbUpdateException)
            {
                // في حالة وجود أصناف مرتبطة بهذه الفئة
                TempData["Err"] = "لا يمكن الحذف لوجود بيانات مرتبطة (مثل أصناف ضمن هذه الفئة).";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================
        // BulkDelete — حذف مجموعة فئات (حذف المحدد)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["Err"] = "لم يتم اختيار أي فئة للحذف.";
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
                TempData["Err"] = "قائمة المعرفات غير صحيحة.";
                return RedirectToAction(nameof(Index));
            }

            var cats = await _db.Categories
                                .Where(x => ids.Contains(x.CategoryId))
                                .ToListAsync();

            if (!cats.Any())
            {
                TempData["Err"] = "لم يتم العثور على الفئات المحددة.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _db.Categories.RemoveRange(cats);
                await _db.SaveChangesAsync();
                TempData["Ok"] = $"تم حذف {cats.Count} فئة/فئات.";
            }
            catch (DbUpdateException)
            {
                TempData["Err"] = "لا يمكن حذف بعض الفئات لوجود أصناف مرتبطة بها.";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================
        // DeleteAll — حذف جميع الفئات
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _db.Categories.ToListAsync();

            if (!all.Any())
            {
                TempData["Ok"] = "لا توجد فئات لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _db.Categories.RemoveRange(all);
                await _db.SaveChangesAsync();
                TempData["Ok"] = "تم حذف جميع الفئات من النظام.";
            }
            catch (DbUpdateException)
            {
                TempData["Err"] = "تعذر حذف جميع الفئات لوجود بيانات مرتبطة بها.";
            }

            return RedirectToAction(nameof(Index));
        }




        // =========================
        // =========================
        // Export — تصدير قائمة الفئات (Excel أو CSV)
        // =========================
        [HttpGet]
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

            IQueryable<Category> q = _db.Categories.AsNoTracking();
            q = ApplyCategoryFilters(q, s, sb, sm, useDateRange, fromDate, toDate, fromCode, toCode,
                filterCol_id, filterCol_name, filterCol_created, filterCol_modified);
            q = ApplyCategorySort(q, so, desc);

            var rows = await q.ToListAsync();

    // توحيد قيمة format
    format = (format ?? "excel").Trim().ToLowerInvariant();

    // =====================================
    // الفرع الأول: CSV
    // =====================================
    if (format == "csv")
    {
        var sbCsv = new StringBuilder();   // متغير: نص CSV

        // عناوين الأعمدة
        sbCsv.AppendLine(string.Join(",",
            Csv("كود الفئة"),
            Csv("اسم الفئة"),
            Csv("تاريخ الإنشاء"),
            Csv("آخر تعديل")
        ));

        // البيانات
        foreach (var c in rows)
        {
            sbCsv.AppendLine(string.Join(",",
                Csv(c.CategoryId.ToString()),
                Csv(c.CategoryName),
                Csv(c.CreatedAt.ToString("yyyy-MM-dd HH:mm")),
                Csv(c.UpdatedAt?.ToString("yyyy-MM-dd HH:mm"))
            ));
        }

        var utf8  = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var bytes = utf8.GetBytes(sbCsv.ToString());
        var fileNameCsv = ExcelExportNaming.ArabicTimestampedFileName("فئات الأصناف", ".csv");

        return File(bytes, "text/csv; charset=utf-8", fileNameCsv);
    }

    // =====================================
    // الفرع الثاني: Excel (XLSX)
// =====================================
    using var workbook = new XLWorkbook();                 // متغير: ملف Excel
    var ws = workbook.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("فئات الأصناف"));

    int r = 1; // متغير: رقم الصف الحالي

    // عناوين الأعمدة
    ws.Cell(r, 1).Value = "كود الفئة";
    ws.Cell(r, 2).Value = "اسم الفئة";
    ws.Cell(r, 3).Value = "تاريخ الإنشاء";
    ws.Cell(r, 4).Value = "آخر تعديل";

    // تنسيق العناوين
    var headerRange = ws.Range(r, 1, r, 4);
    headerRange.Style.Font.Bold = true;
    headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

    // البيانات
    foreach (var c in rows)
    {
        r++;

        ws.Cell(r, 1).Value = c.CategoryId;
        ws.Cell(r, 2).Value = c.CategoryName;
        ws.Cell(r, 3).Value = c.CreatedAt;
        ws.Cell(r, 4).Value = c.UpdatedAt;
    }

    // تنسيق التاريخ + ضبط عرض الأعمدة
    ws.Column(3).Style.DateFormat.Format = "yyyy-MM-dd HH:mm";
    ws.Column(4).Style.DateFormat.Format = "yyyy-MM-dd HH:mm";
    ws.Columns().AdjustToContents();

    using var stream = new MemoryStream();   // متغير: ذاكرة مؤقتة
    workbook.SaveAs(stream);
    stream.Position = 0;

    var fileNameXlsx = ExcelExportNaming.ArabicTimestampedFileName("فئات الأصناف", ".xlsx");
    const string contentTypeXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    return File(stream.ToArray(), contentTypeXlsx, fileNameXlsx);
}

// دالة Csv لو مش موجودة فوق (لو حابب تعيد استخدامها من كنترولر تاني، ينفع برضه)
private static string Csv(string? value)
{
    if (string.IsNullOrEmpty(value))
        return "";

    var s = value.Replace("\"", "\"\"");

    if (s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
        return "\"" + s + "\"";

    return s;
}

    }
}
