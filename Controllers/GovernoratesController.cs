using Azure.Core;
using ClosedXML.Excel;                            // مكتبة Excel
using DocumentFormat.OpenXml.Wordprocessing;
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
    /// كنترولر المحافظات:
    /// - قائمة المحافظات بنظام القوائم الموحد + فلتر تاريخ + فلتر كود من/إلى
    /// - اختيار أعمدة + حذف محدد/حذف الكل + تصدير Excel/CSV + طباعة
    /// - CRUD عادي (إضافة/تعديل/حذف/تفاصيل)
    /// </summary>
    public class GovernoratesController : Controller
    {
        // متغير: سياق قاعدة البيانات
        private readonly AppDbContext _db;

        public GovernoratesController(AppDbContext ctx) => _db = ctx;

        // =========================================================================
        // دالة موحّدة: SearchSortFilter
        // مسئولة عن: 
        // 1) البحث بالنص من 4 كروت (OR بين الكروت)
        // 2) فلتر كود المحافظة من/إلى (OR مع الكروت)
        // 3) فلتر التاريخ (AND على الكل)
        // 4) الترتيب باستخدام ApplySearchSort
        // تُستخدم من Index و Export
        // =========================================================================
        private IQueryable<Governorate> SearchSortFilter(
            // نصوص البحث من الكروت الأربعة
            string? search1,
            string? searchBy1,
            string? search2,
            string? searchBy2,
            string? search3,
            string? searchBy3,
            string? search4,
            string? searchBy4,

            // الترتيب
            string? sort,
            string? dir,

            // فلتر التاريخ
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            string? dateField,

            // فلتر كود المحافظة من/إلى
            int? codeFrom,
            int? codeTo)
        {
            // 1) الاستعلام الأساسي من جدول المحافظات (قراءة فقط)
            var q = _db.Governorates
                       .AsNoTracking()
                       .AsQueryable();

            // ------------------------------
            // 2) فلتر التاريخ (AND مع باقى الفلاتر)
            // ------------------------------
            bool dateFilterActive = useDateRange || fromDate.HasValue || toDate.HasValue;

            if (dateFilterActive)
            {
                bool filterOnUpdated =
                    string.Equals(dateField, "updated", StringComparison.OrdinalIgnoreCase);

                if (filterOnUpdated)
                {
                    // الفلترة على UpdatedAt
                    if (fromDate.HasValue)
                        q = q.Where(g => g.UpdatedAt.HasValue &&
                                         g.UpdatedAt.Value >= fromDate.Value);

                    if (toDate.HasValue)
                        q = q.Where(g => g.UpdatedAt.HasValue &&
                                         g.UpdatedAt.Value <= toDate.Value);
                }
                else
                {
                    // الفلترة على CreatedAt
                    if (fromDate.HasValue)
                        q = q.Where(g => g.CreatedAt >= fromDate.Value);

                    if (toDate.HasValue)
                        q = q.Where(g => g.CreatedAt <= toDate.Value);
                }
            }

            // ------------------------------
            // 3) تحضير نصوص البحث (Trim + Flags)
            // ------------------------------
            string s1 = search1?.Trim() ?? string.Empty;
            string s2 = search2?.Trim() ?? string.Empty;
            string s3 = search3?.Trim() ?? string.Empty;
            string s4 = search4?.Trim() ?? string.Empty;

            bool hasSearch1 = !string.IsNullOrWhiteSpace(s1);
            bool hasSearch2 = !string.IsNullOrWhiteSpace(s2);
            bool hasSearch3 = !string.IsNullOrWhiteSpace(s3);
            bool hasSearch4 = !string.IsNullOrWhiteSpace(s4);

            // نوع العمود المختار لكل كارت (يتحسب مرة واحدة خارج الـ Where)
            bool sb1IsId = string.Equals(searchBy1, "id", StringComparison.OrdinalIgnoreCase);
            bool sb1IsCreated = string.Equals(searchBy1, "created", StringComparison.OrdinalIgnoreCase);
            bool sb1IsUpdated = string.Equals(searchBy1, "updated", StringComparison.OrdinalIgnoreCase);

            bool sb2IsId = string.Equals(searchBy2, "id", StringComparison.OrdinalIgnoreCase);
            bool sb2IsCreated = string.Equals(searchBy2, "created", StringComparison.OrdinalIgnoreCase);
            bool sb2IsUpdated = string.Equals(searchBy2, "updated", StringComparison.OrdinalIgnoreCase);

            bool sb3IsId = string.Equals(searchBy3, "id", StringComparison.OrdinalIgnoreCase);
            bool sb3IsCreated = string.Equals(searchBy3, "created", StringComparison.OrdinalIgnoreCase);
            bool sb3IsUpdated = string.Equals(searchBy3, "updated", StringComparison.OrdinalIgnoreCase);

            bool sb4IsId = string.Equals(searchBy4, "id", StringComparison.OrdinalIgnoreCase);
            bool sb4IsCreated = string.Equals(searchBy4, "created", StringComparison.OrdinalIgnoreCase);
            bool sb4IsUpdated = string.Equals(searchBy4, "updated", StringComparison.OrdinalIgnoreCase);

            // محاولة تحويل نص البحث فى حالة البحث بالكود
            int? id1 = null, id2 = null, id3 = null, id4 = null;

            if (hasSearch1 && sb1IsId && int.TryParse(s1, out var n1)) id1 = n1;
            if (hasSearch2 && sb2IsId && int.TryParse(s2, out var n2)) id2 = n2;
            if (hasSearch3 && sb3IsId && int.TryParse(s3, out var n3)) id3 = n3;
            if (hasSearch4 && sb4IsId && int.TryParse(s4, out var n4)) id4 = n4;

            // محاولة تحويل نص البحث لتاريخ فى حالة created/updated
            DateTime? d1 = null, d2 = null, d3 = null, d4 = null;

            if (hasSearch1 && (sb1IsCreated || sb1IsUpdated) &&
                DateTime.TryParse(s1, out var dt1)) d1 = dt1.Date;

            if (hasSearch2 && (sb2IsCreated || sb2IsUpdated) &&
                DateTime.TryParse(s2, out var dt2)) d2 = dt2.Date;

            if (hasSearch3 && (sb3IsCreated || sb3IsUpdated) &&
                DateTime.TryParse(s3, out var dt3)) d3 = dt3.Date;

            if (hasSearch4 && (sb4IsCreated || sb4IsUpdated) &&
                DateTime.TryParse(s4, out var dt4)) d4 = dt4.Date;

            // فلتر كود من/إلى
            bool hasCodeRange = codeFrom.HasValue || codeTo.HasValue;

            // هل فيه أى نص بحث من الكروت الأربعة؟
            bool hasAnyTextSearch = hasSearch1 || hasSearch2 || hasSearch3 || hasSearch4;

            // ------------------------------
            // 4) تطبيق فلتر البحث (OR بين الكروت + كارت الأكواد)
            // ------------------------------
            if (hasAnyTextSearch || hasCodeRange)
            {
                q = q.Where(g =>
                    // كارت 1
                    (hasSearch1 &&
                     (
                         sb1IsId
                             ? (id1.HasValue
                                 ? g.GovernorateId == id1.Value
                                 : g.GovernorateId.ToString().Contains(s1))
                         : sb1IsCreated
                             ? (d1.HasValue &&
                                g.CreatedAt.HasValue &&
                                g.CreatedAt.Value.Date == d1.Value)
                         : sb1IsUpdated
                             ? (d1.HasValue &&
                                g.UpdatedAt.HasValue &&
                                g.UpdatedAt.Value.Date == d1.Value)
                         : (g.GovernorateName != null &&
                            g.GovernorateName.Contains(s1))
                     ))
                    ||

                    // كارت 2
                    (hasSearch2 &&
                     (
                         sb2IsId
                             ? (id2.HasValue
                                 ? g.GovernorateId == id2.Value
                                 : g.GovernorateId.ToString().Contains(s2))
                         : sb2IsCreated
                             ? (d2.HasValue &&
                                g.CreatedAt.HasValue &&
                                g.CreatedAt.Value.Date == d2.Value)
                         : sb2IsUpdated
                             ? (d2.HasValue &&
                                g.UpdatedAt.HasValue &&
                                g.UpdatedAt.Value.Date == d2.Value)
                         : (g.GovernorateName != null &&
                            g.GovernorateName.Contains(s2))
                     ))
                    ||

                    // كارت 3
                    (hasSearch3 &&
                     (
                         sb3IsId
                             ? (id3.HasValue
                                 ? g.GovernorateId == id3.Value
                                 : g.GovernorateId.ToString().Contains(s3))
                         : sb3IsCreated
                             ? (d3.HasValue &&
                                g.CreatedAt.HasValue &&
                                g.CreatedAt.Value.Date == d3.Value)
                         : sb3IsUpdated
                             ? (d3.HasValue &&
                                g.UpdatedAt.HasValue &&
                                g.UpdatedAt.Value.Date == d3.Value)
                         : (g.GovernorateName != null &&
                            g.GovernorateName.Contains(s3))
                     ))
                    ||

                    // كارت 4
                    (hasSearch4 &&
                     (
                         sb4IsId
                             ? (id4.HasValue
                                 ? g.GovernorateId == id4.Value
                                 : g.GovernorateId.ToString().Contains(s4))
                         : sb4IsCreated
                             ? (d4.HasValue &&
                                g.CreatedAt.HasValue &&
                                g.CreatedAt.Value.Date == d4.Value)
                         : sb4IsUpdated
                             ? (d4.HasValue &&
                                g.UpdatedAt.HasValue &&
                                g.UpdatedAt.Value.Date == d4.Value)
                         : (g.GovernorateName != null &&
                            g.GovernorateName.Contains(s4))
                     ))
                    ||

                    // كارت الأكواد (كود من/إلى) — OR مع الكروت
                    (hasCodeRange &&
                        (!codeFrom.HasValue || g.GovernorateId >= codeFrom.Value) &&
                        (!codeTo.HasValue || g.GovernorateId <= codeTo.Value))
                );
            }

            // ------------------------------
            // 5) الترتيب باستخدام ApplySearchSort (بدون بحث)
            // ------------------------------
            var stringFields =
                new Dictionary<string, Expression<Func<Governorate, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["name"] = g => g.GovernorateName
                };

            var intFields =
                new Dictionary<string, Expression<Func<Governorate, int>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = g => g.GovernorateId
                };

            var orderFields =
                new Dictionary<string, Expression<Func<Governorate, object>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["name"] = g => g.GovernorateName!,
                    ["id"] = g => g.GovernorateId,
                    ["created"] = g => g.CreatedAt ?? DateTime.MinValue,
                    ["updated"] = g => g.UpdatedAt ?? g.CreatedAt ?? DateTime.MinValue
                };

            // نمرر search = null عشان ما نستخدمش منطق البحث بتاع ApplySearchSort
            q = q.ApplySearchSort(
                search: null,
                searchBy: null,
                sort: sort,
                dir: dir,
                stringFields: stringFields,
                intFields: intFields,
                orderFields: orderFields,
                defaultSearchBy: "name",
                defaultSortBy: "name"
            );

            return q;
        }






        // ===========================
        // قائمة المحافظات (Index)
        // ===========================
        [HttpGet]
        public async Task<IActionResult> Index(
            // الكروت الأربعة
            string? search1,
            string? searchBy1 = "name",
            string? search2 = null,
            string? searchBy2 = "name",
            string? search3 = null,
            string? searchBy3 = "name",
            string? search4 = null,
            string? searchBy4 = "name",

            // الترتيب
            string? sort = "name",
            string? dir = "asc",

            // فلتر التاريخ
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? dateField = "created",  // created | updated

            // فلتر كود المحافظة من/إلى
            int? codeFrom = null,
            int? codeTo = null,

            // الترقيم
            int page = 1,
            int pageSize = 50)
        {
            // بناء الاستعلام بالفلاتر والبحث والترتيب
            var q = SearchSortFilter(
                search1, searchBy1,
                search2, searchBy2,
                search3, searchBy3,
                search4, searchBy4,
                sort, dir,
                useDateRange, fromDate, toDate, dateField,
                codeFrom, codeTo
            );

            // الترقيم باستخدام PagedResult
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50;

            bool descending = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            var model = await PagedResult<Governorate>.CreateAsync(
                 q,
                  page,
                 pageSize,
                 null,          // search  -> البحث الموحد مش مستخدم (عندنا 4 كروت)
                 descending,    // bool    -> ترتيب تصاعدي/تنازلي
                 sort,          // string? -> اسم عمود الترتيب
                 null           // searchBy -> مش محتاجينها هنا
  );


            // حالة فلتر التاريخ للواجهة
            bool dateFilterActive = useDateRange || fromDate.HasValue || toDate.HasValue;
            model.UseDateRange = dateFilterActive;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            // تمرير القيم للـ View عن طريق ViewBag (لكروت البحث + كارت الأكواد)
            ViewBag.Search1 = search1;
            ViewBag.SearchBy1 = searchBy1;
            ViewBag.Search2 = search2;
            ViewBag.SearchBy2 = searchBy2;
            ViewBag.Search3 = search3;
            ViewBag.SearchBy3 = searchBy3;
            ViewBag.Search4 = search4;
            ViewBag.SearchBy4 = searchBy4;

            ViewBag.Sort = sort;
            ViewBag.Dir = dir;
            ViewBag.DateField = dateField ?? "created";

            ViewBag.CodeFrom = codeFrom;
            ViewBag.CodeTo = codeTo;

            // اختيار أول كارت كأنه البحث "الرئيسى" لو حابين نستخدمه فى شريط الفلاتر
            ViewBag.Search = search1;
            ViewBag.SearchBy = searchBy1;

            // إعداد اختيارات البحث والترتيب (مبنية على الكارت الأول)
            ViewBag.SearchOptions = new List<SelectListItem>
            {
                new("الاسم",         "name",
                    string.Equals(searchBy1, "name", StringComparison.OrdinalIgnoreCase)),
                new("كود المحافظة", "id",
                    string.Equals(searchBy1, "id",   StringComparison.OrdinalIgnoreCase))
            };

            ViewBag.SortOptions = new List<SelectListItem>
            {
                new("الاسم",          "name",
                    string.Equals(sort, "name",    StringComparison.OrdinalIgnoreCase)),
                new("كود المحافظة",  "id",
                    string.Equals(sort, "id",      StringComparison.OrdinalIgnoreCase)),
                new("تاريخ الإنشاء", "created",
                    string.Equals(sort, "created", StringComparison.OrdinalIgnoreCase)),
                new("آخر تعديل",     "updated",
                    string.Equals(sort, "updated", StringComparison.OrdinalIgnoreCase))
            };

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
            // نفس باراميترات Index الخاصة بالفلاتر
            string? search1,
            string? searchBy1 = "name",
            string? search2 = null,
            string? searchBy2 = "name",
            string? search3 = null,
            string? searchBy3 = "name",
            string? search4 = null,
            string? searchBy4 = "name",

            string? sort = "name",
            string? dir = "asc",

            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? dateField = "created",

            // فلتر كود المحافظة من/إلى
            int? fromCode = null,   // يطابق name="fromCode" فى الـ View
            int? toCode = null,   // يطابق name="toCode"   فى الـ View



            string? format = "excel")
        {
            // إعادة استخدام نفس دالة SearchSortFilter
           
            var q = SearchSortFilter(
                search1, searchBy1,
                search2, searchBy2,
                search3, searchBy3,
                search4, searchBy4,
                sort, dir,
                useDateRange, fromDate, toDate, dateField,
                fromCode, toCode   // ✅ نفس أسماء براميترات الأكشن
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
