using Azure.Core;
using ClosedXML.Excel;                            // مكتبة Excel
using DocumentFormat.OpenXml.Wordprocessing;
using ERP.Data;                                   // AppDbContext
using ERP.Infrastructure;                         // PagedResult + ApplySearchSort + UserActivityLogger
using ERP.Models;                                 // Governorate, UserActionType
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
    /// كنترولر المحافظات:
    /// - قائمة المحافظات بنظام القوائم الموحد + فلتر تاريخ + فلتر كود من/إلى
    /// - اختيار أعمدة + حذف محدد/حذف الكل + تصدير Excel/CSV + طباعة
    /// - CRUD عادي (إضافة/تعديل/حذف/تفاصيل)
    /// </summary>
    public class GovernoratesController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IUserActivityLogger _activityLogger;

        public GovernoratesController(AppDbContext ctx, IUserActivityLogger activityLogger)
        {
            _db = ctx;
            _activityLogger = activityLogger;
        }

        // =========================================================================
        // دالة موحّدة: SearchSortFilter — مثل سجل الحركات (بحث واحد + فلتر كود + فلتر تاريخ)
        // =========================================================================
        private static readonly Dictionary<string, Expression<Func<Governorate, string?>>> GovStringFields
            = new(StringComparer.OrdinalIgnoreCase) { ["name"] = g => g.GovernorateName };

        private static readonly Dictionary<string, Expression<Func<Governorate, int>>> GovIntFields
            = new(StringComparer.OrdinalIgnoreCase) { ["id"] = g => g.GovernorateId };

        private static readonly Dictionary<string, Expression<Func<Governorate, object>>> GovOrderFields
            = new(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = g => g.GovernorateName!,
                ["id"] = g => g.GovernorateId,
                ["created"] = g => g.CreatedAt ?? DateTime.MinValue,
                ["updated"] = g => g.UpdatedAt ?? g.CreatedAt ?? DateTime.MinValue
            };

        private IQueryable<Governorate> SearchSortFilter(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            string? dateField,
            int? codeFrom,
            int? codeTo)
        {
            var q = _db.Governorates.AsNoTracking().AsQueryable();

            // فلتر التاريخ
            if (useDateRange || fromDate.HasValue || toDate.HasValue)
            {
                bool filterOnUpdated = string.Equals(dateField, "updated", StringComparison.OrdinalIgnoreCase);
                if (filterOnUpdated)
                {
                    if (fromDate.HasValue)
                        q = q.Where(g => g.UpdatedAt.HasValue && g.UpdatedAt.Value >= fromDate.Value);
                    if (toDate.HasValue)
                        q = q.Where(g => g.UpdatedAt.HasValue && g.UpdatedAt.Value <= toDate.Value);
                }
                else
                {
                    if (fromDate.HasValue)
                        q = q.Where(g => g.CreatedAt >= fromDate.Value);
                    if (toDate.HasValue)
                        q = q.Where(g => g.CreatedAt <= toDate.Value);
                }
            }

            // فلتر كود من/إلى
            if (codeFrom.HasValue)
                q = q.Where(g => g.GovernorateId >= codeFrom.Value);
            if (codeTo.HasValue)
                q = q.Where(g => g.GovernorateId <= codeTo.Value);

            // بحث + ترتيب (ApplySearchSort)
            q = q.ApplySearchSort(
                search, searchBy,
                sort, dir,
                GovStringFields, GovIntFields, GovOrderFields,
                defaultSearchBy: "all",
                defaultSortBy: "name"
            );

            return q;
        }






        // ===========================
        // قائمة المحافظات (Index) — نظام بحث مثل سجل الحركات
        // ===========================
        [HttpGet]
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",
            string? sort = "name",
            string? dir = "asc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? dateField = "created",
            int? codeFrom = null,
            int? codeTo = null,
            int page = 1,
            int pageSize = 50)
        {
            var q = SearchSortFilter(
                search, searchBy,
                sort, dir,
                useDateRange, fromDate, toDate, dateField ?? "created",
                codeFrom, codeTo
            );

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50;

            bool descending = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            var model = await PagedResult<Governorate>.CreateAsync(q, page, pageSize, null, descending, sort, null);

            bool dateFilterActive = useDateRange || fromDate.HasValue || toDate.HasValue;
            model.UseDateRange = dateFilterActive;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            ViewBag.Search = search;
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.Sort = sort;
            ViewBag.Dir = dir;
            ViewBag.DateField = dateField ?? "created";
            ViewBag.CodeFrom = codeFrom;
            ViewBag.CodeTo = codeTo;

            return View(model);
        }






        // =========================================================================
        // Details / Create / Edit / Delete (CRUD الأساسي)
        // =========================================================================

        public async Task<IActionResult> Details(int id)
        {
            var item = await _db.Governorates
                                .AsNoTracking()
                                .FirstOrDefaultAsync(x => x.GovernorateId == id);

            if (item == null) return NotFound();
            return View(item);
        }

        public IActionResult Create() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("GovernorateName")] Governorate vm)
        {
            if (!ModelState.IsValid)
                return View(vm);

            // تسجيل تاريخ الإنشاء (الخادم)
            vm.CreatedAt = DateTime.Now;

            _db.Governorates.Add(vm);
            await _db.SaveChangesAsync();

            await _activityLogger.LogAsync(UserActionType.Create, "Governorate", vm.GovernorateId, $"إنشاء محافظة: {vm.GovernorateName}");

            TempData["Ok"] = "تم إضافة المحافظة بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(int id)
        {
            var entity = await _db.Governorates.FindAsync(id);
            if (entity == null) return NotFound();
            return View(entity);
        }






        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("GovernorateName")] Governorate vm)
        {
            var entity = await _db.Governorates.FindAsync(id);
            if (entity == null) return NotFound();

            if (!ModelState.IsValid)
                return View(entity);

            entity.GovernorateName = vm.GovernorateName;
            entity.UpdatedAt = DateTime.Now;   // تسجيل آخر تعديل

            await _db.SaveChangesAsync();

            await _activityLogger.LogAsync(UserActionType.Edit, "Governorate", id, $"تعديل محافظة: {entity.GovernorateName}");

            TempData["Ok"] = "تم تعديل بيانات المحافظة.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Delete(int id)
        {
            var item = await _db.Governorates
                                .AsNoTracking()
                                .FirstOrDefaultAsync(x => x.GovernorateId == id);

            if (item == null) return NotFound();
            return View(item);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var entity = await _db.Governorates.FindAsync(id);
            if (entity != null)
            {
                _db.Governorates.Remove(entity);
                await _db.SaveChangesAsync();

                await _activityLogger.LogAsync(UserActionType.Delete, "Governorate", id, $"حذف محافظة: {entity.GovernorateName}");

                TempData["Ok"] = "تم حذف المحافظة.";
            }

            return RedirectToAction(nameof(Index));
        }






        // =========================================================================
        // BulkDelete — حذف المحافظات المحددة
        // =========================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["Err"] = "لم يتم اختيار أي محافظة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var ids = selectedIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var n) ? n : (int?)null)
                .Where(n => n.HasValue)
                .Select(n => n!.Value)
                .ToList();

            if (!ids.Any())
            {
                TempData["Err"] = "قائمة الأكواد غير صحيحة.";
                return RedirectToAction(nameof(Index));
            }

            var items = await _db.Governorates
                                 .Where(g => ids.Contains(g.GovernorateId))
                                 .ToListAsync();

            if (!items.Any())
            {
                TempData["Err"] = "لم يتم العثور على المحافظات المحددة.";
                return RedirectToAction(nameof(Index));
            }

            _db.Governorates.RemoveRange(items);
            await _db.SaveChangesAsync();

            TempData["Ok"] = $"تم حذف {items.Count} محافظة.";
            return RedirectToAction(nameof(Index));
        }








        // =========================================================================
        // DeleteAll — حذف جميع المحافظات
        // =========================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _db.Governorates.ToListAsync();

            if (!all.Any())
            {
                TempData["Ok"] = "لا توجد محافظات لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            _db.Governorates.RemoveRange(all);
            await _db.SaveChangesAsync();

            TempData["Ok"] = "تم حذف جميع المحافظات.";
            return RedirectToAction(nameof(Index));
        }








        // =========================================================================
        // Export — تصدير المحافظات (Excel أو CSV) مع نفس الفلاتر بالضبط
        // =========================================================================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy = "all",
            string? sort = "name",
            string? dir = "asc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? dateField = "created",
            int? codeFrom = null,
            int? codeTo = null,
            string? format = "excel")
        {
            var q = SearchSortFilter(
                search, searchBy,
                sort, dir,
                useDateRange, fromDate, toDate, dateField ?? "created",
                codeFrom, codeTo
            );



            var rows = await q.ToListAsync();

            format = (format ?? "excel").Trim().ToLowerInvariant();

            // ---------------- CSV ----------------
            if (format == "csv")
            {
                var sb = new StringBuilder();

                // عناوين الأعمدة
                sb.AppendLine(string.Join(",",
                    Csv("كود المحافظة"),
                    Csv("اسم المحافظة"),
                    Csv("تاريخ الإنشاء"),
                    Csv("آخر تعديل")
                ));

                // البيانات
                foreach (var g in rows)
                {
                    sb.AppendLine(string.Join(",",
                        Csv(g.GovernorateId.ToString()),
                        Csv(g.GovernorateName),
                        Csv(g.CreatedAt?.ToString("yyyy-MM-dd HH:mm")),
                        Csv(g.UpdatedAt?.ToString("yyyy-MM-dd HH:mm"))
                    ));
                }

                var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
                var bytes = utf8.GetBytes(sb.ToString());
                var name = $"Governorates_{DateTime.Now:yyyyMMdd_HHmm}_csv.csv";
                var ctype = "text/csv; charset=utf-8";

                return File(bytes, ctype, name);
            }

            // ---------------- Excel ----------------
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Governorates");

            int r = 1;

            ws.Cell(r, 1).Value = "كود المحافظة";
            ws.Cell(r, 2).Value = "اسم المحافظة";
            ws.Cell(r, 3).Value = "تاريخ الإنشاء";
            ws.Cell(r, 4).Value = "آخر تعديل";

            var header = ws.Range(r, 1, r, 4);
            header.Style.Font.Bold = true;
            header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            foreach (var g in rows)
            {
                r++;
                ws.Cell(r, 1).Value = g.GovernorateId;   // كود
                ws.Cell(r, 2).Value = g.GovernorateName; // اسم
                ws.Cell(r, 3).Value = g.CreatedAt;       // تاريخ إنشاء
                ws.Cell(r, 4).Value = g.UpdatedAt;       // آخر تعديل
            }

            ws.Column(3).Style.DateFormat.Format = "yyyy-MM-dd HH:mm";
            ws.Column(4).Style.DateFormat.Format = "yyyy-MM-dd HH:mm";
            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            stream.Position = 0;

            var fileNameXlsx = $"Governorates_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
            const string contentTypeXlsx =
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

            return File(stream.ToArray(), contentTypeXlsx, fileNameXlsx);
        }

        // دالة مساعدة صغيرة لتجهيز نص الـ CSV
        private static string Csv(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            var s = value.Replace("\"", "\"\""); // هروب علامة "

            if (s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
                return "\"" + s + "\"";

            return s;
        }
    }
}
