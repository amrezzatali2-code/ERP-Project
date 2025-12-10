using Azure.Core;
using ClosedXML.Excel;                            // مكتبة Excel
using DocumentFormat.OpenXml.InkML;
using ERP.Data;                                   // AppDbContext
using ERP.Infrastructure;                         // PagedResult + ApplySearchSort
using ERP.Models;                                 // Governorate
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
        // كائن سياق قاعدة البيانات
        private readonly AppDbContext _db;

        public BranchesController(AppDbContext db)
        {
            _db = db;
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
        [HttpGet]
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
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

            // ===== فلترة بالتاريخ (تاريخ الإنشاء) =====
            if (useDateRange)
            {
                if (fromDate.HasValue)
                {
                    query = query.Where(b =>
                        b.CreatedAt.HasValue &&
                        b.CreatedAt.Value >= fromDate.Value);
                }

                if (toDate.HasValue)
                {
                    query = query.Where(b =>
                        b.CreatedAt.HasValue &&
                        b.CreatedAt.Value <= toDate.Value);
                }
            }

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

            return View(result);
        }

        // =========================================================
        // GET: Branches/Details/5
        // عرض تفاصيل فرع واحد
        // =========================================================
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

            TempData["Ok"] = "تمت إضافة الفرع بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // GET: Branches/Edit/5
        // عرض فورم تعديل فرع
        // =========================================================
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

            dbBranch.BranchName = branch.BranchName;   // تعديل الاسم فقط
            dbBranch.UpdatedAt = DateTime.Now;        // تحديث تاريخ آخر تعديل

            await _db.SaveChangesAsync();

            TempData["Ok"] = "تم تعديل بيانات الفرع.";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // GET: Branches/Delete/5
        // عرض صفحة تأكيد حذف فرع
        // =========================================================
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
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var branch = await _db.Branches.FindAsync(id);
            if (branch == null)
                return NotFound();

            _db.Branches.Remove(branch);
            await _db.SaveChangesAsync();

            TempData["Ok"] = "تم حذف السجل.";
            return RedirectToAction(nameof(Index));
        }




        /// <summary>
        /// حذف جماعي للفروع المختارة من الجدول (حسب التشيك بوكس).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
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
        /// تصدير الفروع المطابقة للفلاتر الحالية إلى ملف CSV
        /// يمكن فتحه مباشرة في Excel.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate)
        {
            // تطبيق الفلاتر
            var query = FilterBranches(search, searchBy, useDateRange, fromDate, toDate);

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

            // بناء CSV بسيط (يفتح في Excel)
            var sb = new StringBuilder();

            // هيدر الأعمدة
            sb.AppendLine("BranchId,BranchName,CreatedAt,UpdatedAt");

            // دالة مساعدة للهروب من الفواصل وعلامات الاقتباس
            string Csv(string? value)
            {
                value ??= "";
                if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                {
                    value = "\"" + value.Replace("\"", "\"\"") + "\"";
                }
                return value;
            }

            foreach (var b in list)
            {
                var created = b.CreatedAt?.ToString("yyyy-MM-dd HH:mm") ?? "";
                var updated = b.UpdatedAt?.ToString("yyyy-MM-dd HH:mm") ?? "";

                sb.AppendLine(string.Join(",",
                    b.BranchId,
                    Csv(b.BranchName),
                    Csv(created),
                    Csv(updated)
                ));
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = $"Branches_{DateTime.Now:yyyyMMdd_HHmmss}.csv";

            // Content-Type يسمح بفتح الملف في Excel مباشرة
            return File(bytes, "text/csv", fileName);
        }

    }
}
