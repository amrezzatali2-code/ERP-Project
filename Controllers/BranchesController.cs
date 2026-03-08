using Azure.Core;
using ClosedXML.Excel;                            // مكتبة Excel
using DocumentFormat.OpenXml.InkML;
using ERP.Data;                                   // AppDbContext
using ERP.Filters;
using ERP.Infrastructure;                         // PagedResult + ApplySearchSort + UserActivityLogger
using ERP.Models;                                 // Governorate, UserActionType
using ERP.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;                 // القوائم List
using System.Globalization;
using System.IO;                                  // MemoryStream للتصدير
using System.Linq;
using System.Linq.Expressions;                    // Expression<Func<>>
using System.Text;                                // StringBuilder + Encoding
using System.Threading.Tasks;

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر الفروع — نفس نمط الحسابات/المناطق:
    /// بحث + ترتيب + ترقيم + فلترة بتاريخ الإنشاء + CRUD كامل.
    /// </summary>
    public class BranchesController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IUserActivityLogger _activityLogger;

        public BranchesController(AppDbContext db, IUserActivityLogger activityLogger)
        {
            _db = db;
            _activityLogger = activityLogger;
        }

        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        private static IQueryable<Branch> ApplyColumnFilters(
            IQueryable<Branch> query,
            string? filterCol_id,
            string? filterCol_name,
            string? filterCol_created,
            string? filterCol_updated)
        {
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) query = query.Where(b => ids.Contains(b.BranchId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_name))
            {
                var vals = filterCol_name.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) query = query.Where(b => b.BranchName != null && vals.Contains(b.BranchName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_created))
            {
                var parts = filterCol_created.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length >= 8).ToList();
                if (parts.Count > 0)
                {
                    var dates = new List<DateTime?>();
                    foreach (var p in parts)
                        if (DateTime.TryParse(p, out var d)) dates.Add(d);
                    if (dates.Count > 0) query = query.Where(b => b.CreatedAt.HasValue && dates.Contains(b.CreatedAt));
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
                    if (dates.Count > 0) query = query.Where(b => b.UpdatedAt.HasValue && dates.Contains(b.UpdatedAt));
                }
            }
            return query;
        }

        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var columnLower = (column ?? "").Trim().ToLowerInvariant();
            var q = _db.Branches.AsNoTracking();
            if (columnLower == "id" || columnLower == "branchid")
            {
                var ids = await q.Select(b => b.BranchId).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "name" || columnLower == "branchname")
            {
                var list = await q.Select(b => b.BranchName).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s != null && s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v ?? "", display = v ?? "" }));
            }
            if (columnLower == "created" || columnLower == "createdat")
            {
                var list = await q.Where(b => b.CreatedAt.HasValue).Select(b => b.CreatedAt!.Value).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd HH:mm"), display = d.ToString("yyyy-MM-dd HH:mm") }));
            }
            if (columnLower == "updated" || columnLower == "updatedat")
            {
                var list = await q.Where(b => b.UpdatedAt.HasValue).Select(b => b.UpdatedAt!.Value).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd HH:mm"), display = d.ToString("yyyy-MM-dd HH:mm") }));
            }
            return Json(Array.Empty<object>());
        }

        // دالة خاصة لتجهيز كويري الفروع مع تطبيق الفلاتر
        private IQueryable<Branch> FilterBranches(
            string? search,
            string? searchBy,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate)
        {
            // كويري الأساس من جدول الفروع
            var query = _db.Branches.AsQueryable();

            // فلتر البحث (بالكود أو بالاسم)
            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();

                switch (searchBy)
                {
                    case "id":   // بحث بالكود
                        if (int.TryParse(search, out var idVal))
                        {
                            query = query.Where(b => b.BranchId == idVal);
                        }
                        break;

                    default:     // بحث بالاسم (الافتراضي)
                        query = query.Where(b => b.BranchName.Contains(search));
                        break;
                }
            }

            // فلتر الفترة الزمنية (تاريخ الإنشاء)
            if (useDateRange)
            {
                if (fromDate.HasValue)
                {
                    query = query.Where(b => b.CreatedAt >= fromDate);
                }

                if (toDate.HasValue)
                {
                    query = query.Where(b => b.CreatedAt <= toDate);
                }
            }

            return query;
        }





        // =========================================================
        // GET: Branches
        // قائمة الفروع مع:
        //  - بحث (search + searchBy)
        //  - ترتيب (sort + dir)
        //  - فلترة بتاريخ الإنشاء (useDateRange + fromDate/toDate)
        //  - تقسيم صفحات (page + pageSize)
        // =========================================================
        [RequirePermission("Branches.Index")]
        [HttpGet]
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? filterCol_id = null,
            string? filterCol_name = null,
            string? filterCol_created = null,
            string? filterCol_updated = null,
            int page = 1,
            int pageSize = 25)
        {
            // ===== تنظيف القيم الافتراضية =====
            search = (search ?? string.Empty).Trim();          // نص البحث
            searchBy = string.IsNullOrWhiteSpace(searchBy) ? "name" : searchBy.ToLower();
            sort = string.IsNullOrWhiteSpace(sort) ? "name" : sort.ToLower();
            dir = string.IsNullOrWhiteSpace(dir) ? "asc" : dir.ToLower();

            bool desc = dir == "desc";   // متغير: هل الترتيب تنازلي؟

            // ===== الاستعلام الأساسي من جدول الفروع =====
            IQueryable<Branch> query = _db.Branches
                                          .AsNoTracking()
                                          .AsQueryable();

            // ===== تطبيق البحث =====
            if (!string.IsNullOrEmpty(search))
            {
                switch (searchBy)
                {
                    case "id":        // البحث برقم الفرع فقط
                        if (int.TryParse(search, out int idValue))
                        {
                            query = query.Where(b => b.BranchId == idValue);
                        }
                        else
                        {
                            // لو كتب نص مش رقم في خانة "الرقم" ⇒ لا نتائج
                            query = query.Where(b => 1 == 0);
                        }
                        break;

                    case "all":       // البحث في الاسم + الكود معًا
                        query = query.Where(b =>
                            b.BranchName.Contains(search) ||
                            b.BranchId.ToString().Contains(search));
                        break;

                    case "name":
                    default:          // البحث بالاسم فقط
                        query = query.Where(b => b.BranchName.Contains(search));
                        break;
                }
            }

            if (useDateRange)
            {
                if (fromDate.HasValue)
                    query = query.Where(b => b.CreatedAt.HasValue && b.CreatedAt.Value >= fromDate.Value);
                if (toDate.HasValue)
                    query = query.Where(b => b.CreatedAt.HasValue && b.CreatedAt.Value <= toDate.Value);
            }

            query = ApplyColumnFilters(query, filterCol_id, filterCol_name, filterCol_created, filterCol_updated);

            // ===== الترتيب =====
            // name   = اسم الفرع
            // id     = كود الفرع
            // created= تاريخ الإنشاء
            // updated= آخر تعديل
            query = (sort, desc) switch
            {
                ("id", false) => query.OrderBy(b => b.BranchId),
                ("id", true) => query.OrderByDescending(b => b.BranchId),

                ("created", false) => query.OrderBy(b => b.CreatedAt ?? DateTime.MinValue)
                                           .ThenBy(b => b.BranchName),
                ("created", true) => query.OrderByDescending(b => b.CreatedAt ?? DateTime.MinValue)
                                           .ThenByDescending(b => b.BranchName),

                ("updated", false) => query.OrderBy(b => b.UpdatedAt ?? DateTime.MinValue)
                                           .ThenBy(b => b.BranchName),
                ("updated", true) => query.OrderByDescending(b => b.UpdatedAt ?? DateTime.MinValue)
                                           .ThenByDescending(b => b.BranchName),

                ("name", false) => query.OrderBy(b => b.BranchName),
                ("name", true) => query.OrderByDescending(b => b.BranchName),

                // الافتراضي: اسم الفرع تصاعدي
                _ => query.OrderBy(b => b.BranchName),
            };

            // ===== إنشاء نتيجة PagedResult مع حفظ حالة البحث/الترتيب =====
            var result = await PagedResult<Branch>.CreateAsync(
                query,
                page,
                pageSize,
                sort,
                desc,
                search,
                searchBy
            );

            // تخزين حالة فلتر التاريخ في الموديل لعرضها في الواجهة
            result.UseDateRange = useDateRange;
            result.FromDate = fromDate;
            result.ToDate = toDate;

            // ===== تجهيز خيارات شريط البحث/الترتيب (_IndexFilters) =====
            ViewBag.SearchOptions = new[]
            {
                new SelectListItem("اسم الفرع",      "name", searchBy == "name"),
                new SelectListItem("الكود",          "id",   searchBy == "id"),
                new SelectListItem("الاسم + الكود",  "all",  searchBy == "all"),
            };

            ViewBag.SortOptions = new[]
            {
                new SelectListItem("اسم الفرع",     "name",    sort == "name"),
                new SelectListItem("كود الفرع",     "id",      sort == "id"),
                new SelectListItem("تاريخ الإنشاء", "created", sort == "created"),
                new SelectListItem("آخر تعديل",     "updated", sort == "updated"),
            };

            // تمرير قيم الفلاتر للـ View علشان البارشال يعرضها
            ViewBag.Search = search;
            ViewBag.SearchBy = searchBy;
            ViewBag.Sort = sort;
            ViewBag.Dir = dir;
            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_Name = filterCol_name;
            ViewBag.FilterCol_Created = filterCol_created;
            ViewBag.FilterCol_Updated = filterCol_updated;

            return View(result);
        }

        // =========================================================
        // GET: Branches/Details/5
        // عرض تفاصيل فرع واحد
        // =========================================================
        [RequirePermission("Branches.Index")]
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var branch = await _db.Branches
                                  .AsNoTracking()
                                  .FirstOrDefaultAsync(b => b.BranchId == id);

            if (branch == null)
                return NotFound();

            return View(branch);
        }

        // =========================================================
        // GET: Branches/Create
        // عرض فورم إضافة فرع جديد
        // =========================================================
        [RequirePermission("Branches.Create")]
        [HttpGet]
        public IActionResult Create()
        {
            return View(new Branch());   // فورم فاضي
        }

        // =========================================================
        // POST: Branches/Create
        // استلام بيانات الفرع الجديد وحفظه
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Branches.Create")]
        public async Task<IActionResult> Create(Branch branch)
        {
            // التحقق من البيانات (الاسم مطلوب)
            if (!ModelState.IsValid)
                return View(branch);

            // تعيين تاريخ الإنشاء والتعديل
            branch.CreatedAt = DateTime.Now;
            branch.UpdatedAt = DateTime.Now;

            _db.Branches.Add(branch);
            await _db.SaveChangesAsync();

            await _activityLogger.LogAsync(UserActionType.Create, "Branch", branch.BranchId, $"إنشاء فرع: {branch.BranchName}");

            TempData["Ok"] = "تمت إضافة الفرع بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // GET: Branches/Edit/5
        // عرض فورم تعديل فرع
        // =========================================================
        [RequirePermission("Branches.Edit")]
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var branch = await _db.Branches.FindAsync(id);
            if (branch == null)
                return NotFound();

            return View(branch);
        }

        // =========================================================
        // POST: Branches/Edit/5
        // استلام التعديلات وحفظها
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Branches.Edit")]
        public async Task<IActionResult> Edit(int id, Branch branch)
        {
            if (id != branch.BranchId)
                return BadRequest();   // حماية من التلاعب في Id

            if (!ModelState.IsValid)
                return View(branch);

            // جلب السجل من قاعدة البيانات ثم تحديث الحقول المسموح بها فقط
            var dbBranch = await _db.Branches.FindAsync(id);
            if (dbBranch == null)
                return NotFound();

            var oldValues = System.Text.Json.JsonSerializer.Serialize(new { dbBranch.BranchName });
            dbBranch.BranchName = branch.BranchName;   // تعديل الاسم فقط
            dbBranch.UpdatedAt = DateTime.Now;        // تحديث تاريخ آخر تعديل

            await _db.SaveChangesAsync();

            var newValues = System.Text.Json.JsonSerializer.Serialize(new { dbBranch.BranchName });
            await _activityLogger.LogAsync(UserActionType.Edit, "Branch", id, $"تعديل فرع: {dbBranch.BranchName}", oldValues, newValues);

            TempData["Ok"] = "تم تعديل بيانات الفرع.";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // GET: Branches/Delete/5
        // عرض صفحة تأكيد حذف فرع
        // =========================================================
        [RequirePermission("Branches.Delete")]
        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            var branch = await _db.Branches
                                  .AsNoTracking()
                                  .FirstOrDefaultAsync(b => b.BranchId == id);

            if (branch == null)
                return NotFound();

            return View(branch);
        }

        // =========================================================
        // POST: Branches/Delete/5
        // تنفيذ الحذف بعد التأكيد
        // =========================================================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [RequirePermission("Branches.Delete")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var branch = await _db.Branches.FindAsync(id);
            if (branch == null)
                return NotFound();

            var oldValues = System.Text.Json.JsonSerializer.Serialize(new { branch.BranchName });
            _db.Branches.Remove(branch);
            await _db.SaveChangesAsync();

            await _activityLogger.LogAsync(UserActionType.Delete, "Branch", id, $"حذف فرع: {branch.BranchName}", oldValues: oldValues);

            TempData["Ok"] = "تم حذف السجل.";
            return RedirectToAction(nameof(Index));
        }




        /// <summary>
        /// حذف جماعي للفروع المختارة من الجدول (حسب التشيك بوكس).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Branches.Delete")]
        public async Task<IActionResult> BulkDelete(
            int[]? selectedIds,          // IDs المختارة من الجدول
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            int page = 1,
            int pageSize = 25)
        {
            // لو المستخدم ما اختار شيئ
            if (selectedIds == null || selectedIds.Length == 0)
            {
                TempData["Error"] = "لم يتم اختيار أي فروع للحذف.";
                return RedirectToAction(nameof(Index), new
                {
                    search,
                    searchBy,
                    sort,
                    dir,
                    page,
                    pageSize,
                    useDateRange,
                    fromDate,
                    toDate
                });
            }

            // قراءة الفروع المطابقة لقائمة الـ IDs
            var branches = await _db.Branches
                .Where(b => selectedIds.Contains(b.BranchId))
                .ToListAsync();

            if (branches.Count == 0)
            {
                TempData["Error"] = "لم يتم العثور على فروع مطابقة للمعرّفات المختارة.";
            }
            else
            {
                _db.Branches.RemoveRange(branches);
                await _db.SaveChangesAsync();

                TempData["Success"] = $"تم حذف {branches.Count} فرع/فروع بنجاح.";
            }

            // الرجوع لنفس الفلاتر والصفحة
            return RedirectToAction(nameof(Index), new
            {
                search,
                searchBy,
                sort,
                dir,
                page,
                pageSize,
                useDateRange,
                fromDate,
                toDate
            });
        }



        /// <summary>
        /// حذف كل الفروع المطابقة للفلاتر الحالية (بحث + فترة زمنية).
        /// لا يعتمد على التشيك بوكس، بل على الفلاتر.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Branches.Delete")]
        public async Task<IActionResult> DeleteAll(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate)
        {
            // تجهيز الكويري مع الفلاتر
            var query = FilterBranches(search, searchBy, useDateRange, fromDate, toDate);

            // عدد الفروع قبل الحذف
            var branches = await query.ToListAsync();
            var count = branches.Count;

            if (count == 0)
            {
                TempData["Info"] = "لا توجد فروع مطابقة للفلاتر الحالية للحذف.";
            }
            else
            {
                _db.Branches.RemoveRange(branches);
                await _db.SaveChangesAsync();

                TempData["Success"] = $"تم حذف {count} فرع/فروع مطابقة للفلاتر الحالية.";
            }

            // نرجع لنفس الفلاتر (صفحة 1 لأن البيانات اتغيّرت)
            return RedirectToAction(nameof(Index), new
            {
                search,
                searchBy,
                sort,
                dir,
                page = 1,
                pageSize = 25,
                useDateRange,
                fromDate,
                toDate
            });
        }




        /// <summary>
        /// تصدير الفروع المطابقة للفلاتر الحالية (Excel أو CSV).
        /// </summary>
        [RequirePermission("Branches.Export")]
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            string? filterCol_id = null,
            string? filterCol_name = null,
            string? filterCol_created = null,
            string? filterCol_updated = null,
            string? format = "excel")
        {
            var query = FilterBranches(search, searchBy, useDateRange, fromDate, toDate);
            query = ApplyColumnFilters(query, filterCol_id, filterCol_name, filterCol_created, filterCol_updated);

            // تطبيق الترتيب مثل شاشة الـ Index
            query = (sort, dir?.ToLower()) switch
            {
                ("id", "desc") => query.OrderByDescending(b => b.BranchId),
                ("id", _) => query.OrderBy(b => b.BranchId),

                ("created", "desc") => query.OrderByDescending(b => b.CreatedAt),
                ("created", _) => query.OrderBy(b => b.CreatedAt),

                ("updated", "desc") => query.OrderByDescending(b => b.UpdatedAt),
                ("updated", _) => query.OrderBy(b => b.UpdatedAt),

                ("name", "desc") => query.OrderByDescending(b => b.BranchName),
                ("name", _) => query.OrderBy(b => b.BranchName),

                _ => query.OrderBy(b => b.BranchId)
            };

            var list = await query.AsNoTracking().ToListAsync();
            var fmt = (format ?? "excel").Trim().ToLowerInvariant();

            // ---------- CSV ----------
            if (fmt == "csv")
            {
                string Csv(string? value)
                {
                    value ??= "";
                    if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                        value = "\"" + value.Replace("\"", "\"\"") + "\"";
                    return value;
                }

                var sb = new StringBuilder();
                sb.AppendLine("BranchId,BranchName,CreatedAt,UpdatedAt");
                foreach (var b in list)
                {
                    sb.AppendLine(string.Join(",",
                        b.BranchId,
                        Csv(b.BranchName),
                        Csv(b.CreatedAt?.ToString("yyyy-MM-dd HH:mm") ?? ""),
                        Csv(b.UpdatedAt?.ToString("yyyy-MM-dd HH:mm") ?? "")
                    ));
                }

                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                return File(bytes, "text/csv; charset=utf-8", $"Branches_{DateTime.Now:yyyyMMdd_HHmm}.csv");
            }

            // ---------- Excel (.xlsx) ----------
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("الفروع");

            ws.Cell(1, 1).Value = "كود الفرع";
            ws.Cell(1, 2).Value = "اسم الفرع";
            ws.Cell(1, 3).Value = "تاريخ الإنشاء";
            ws.Cell(1, 4).Value = "آخر تعديل";

            var header = ws.Range(1, 1, 1, 4);
            header.Style.Font.Bold = true;
            header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int row = 2;
            foreach (var b in list)
            {
                ws.Cell(row, 1).Value = b.BranchId;
                ws.Cell(row, 2).Value = b.BranchName ?? "";
                ws.Cell(row, 3).Value = b.CreatedAt?.ToString("yyyy-MM-dd HH:mm") ?? "";
                ws.Cell(row, 4).Value = b.UpdatedAt?.ToString("yyyy-MM-dd HH:mm") ?? "";
                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            stream.Position = 0;
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Branches_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }

    }
}
