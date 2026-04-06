using ClosedXML.Excel;
using ERP.Data;                                   // AppDbContext (الاتصال بقاعدة البيانات)
using ERP.Filters;
using ERP.Infrastructure;                         // PagedResult + ApplySearchSort + UserActivityLogger
using ERP.Models;                                 // Policy, UserActionType
using ERP.Security;
using ERP.Services.Caching;
using Microsoft.AspNetCore.Mvc;                   // أساس الكنترولر
using Microsoft.EntityFrameworkCore;              // AsNoTracking, ToListAsync, AnyAsync
using System;                                     // متغيرات الوقت DateTime
using System.Collections.Generic;                 // القوائم List, Dictionary
using System.Linq;                                // أوامر LINQ مثل Where و OrderBy
using System.Linq.Expressions;                    // Expression<Func<...>> لحقول البحث
using System.Text;                                // StringBuilder لتجهيز ملف التصدير
using System.Threading.Tasks;                     // async / await

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إدارة جدول السياسات (Policies)
    /// يطبق "النظام الثابت": قائمة + بحث + ترتيب + ترقيم + CRUD + Export + BulkDelete + DeleteAll + Details.
    /// </summary>
    [RequirePermission("Policies.Index")]
    public class PoliciesController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IUserActivityLogger _activityLogger;
        private readonly ILookupCacheService _lookupCache;

        public PoliciesController(AppDbContext context, IUserActivityLogger activityLogger, ILookupCacheService lookupCache)
        {
            _context = context;
            _activityLogger = activityLogger;
            _lookupCache = lookupCache;
        }






        // =========================
        // دالة مشتركة لبناء استعلام السياسات
        // (بحث + فلتر كود من/إلى + فلتر تاريخ + ترتيب)
        // =========================
        private IQueryable<Policy> BuildPoliciesQuery(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            int? fromCode,
            int? toCode,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null)
        {
            // 1) الاستعلام الأساسي (قراءة فقط لتحسين الأداء)
            IQueryable<Policy> q = _context.Policies.AsNoTracking();

            // 2) فلتر كود السياسة من/إلى
            if (fromCode.HasValue)
                q = q.Where(x => x.PolicyId >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(x => x.PolicyId <= toCode.Value);

            // 3) فلترة بالتاريخ على CreatedAt (اختيارية)
            if (useDateRange)
            {
                if (fromDate.HasValue)
                    q = q.Where(x => x.CreatedAt >= fromDate.Value);

                if (toDate.HasValue)
                    q = q.Where(x => x.CreatedAt <= toDate.Value);
            }

            // 4) بحث مخصص لحقول التاريخ والحالة (قبل ApplySearchSort)
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

            // 5) الحقول النصية للبحث (active للبحث في الكل فقط؛ البحث المخصص أعلاه يتولى searchBy=active)
            var stringFields = new Dictionary<string, Expression<Func<Policy, string?>>>
            {
                ["name"] = x => x.Name!,
                ["description"] = x => x.Description ?? string.Empty,
                ["active"] = x => x.IsActive ? "نعم" : "لا"
            };

            // 6) الحقول الرقمية للبحث
            var intFields = new Dictionary<string, Expression<Func<Policy, int>>>
            {
                ["id"] = x => x.PolicyId
            };

            // 7) حقول الترتيب — كل أعمدة الجدول
            var orderFields = new Dictionary<string, Expression<Func<Policy, object>>>
            {
                ["id"] = x => x.PolicyId,
                ["name"] = x => x.Name!,
                ["description"] = x => x.Description ?? string.Empty,
                ["active"] = x => x.IsActive,
                ["created"] = x => x.CreatedAt,
                ["updated"] = x => x.UpdatedAt ?? x.CreatedAt
            };

            // 8) تطبيق البحث + الترتيب عن طريق الإكستنشن الموحّد
            q = q.ApplySearchSort(
                searchForSort, searchByForSort,
                sort, dir,
                stringFields, intFields, orderFields,
                defaultSearchBy: "name",
                defaultSortBy: "id");

            return q;
        }

        // =========================
        // Index — عرض قائمة السياسات بالنظام الثابت
        // =========================
        private static readonly char[] _filterSep = { '|', ',', ';' };

        private IQueryable<Policy> ApplyColumnFilters(
            IQueryable<Policy> q,
            string? filterCol_id,
            string? filterCol_name,
            string? filterCol_description,
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
                    q = q.Where(x => ids.Contains(x.PolicyId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_name))
            {
                var terms = filterCol_name.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
                if (terms.Count > 0)
                    q = q.Where(x => x.Name != null && terms.Contains(x.Name));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_description))
            {
                var terms = filterCol_description.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
                if (terms.Count > 0)
                    q = q.Where(x => x.Description != null && terms.Any(t => x.Description.Contains(t)));
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
            var q = _context.Policies.AsNoTracking();

            if (col == "id")
            {
                var ids = await q.Select(x => x.PolicyId).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (col == "name")
            {
                var list = await q.Where(x => x.Name != null).Select(x => x.Name!).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm))
                    list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (col == "description")
            {
                var list = await q.Where(x => x.Description != null).Select(x => x.Description!).Distinct().OrderBy(x => x).Take(300).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm))
                    list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v.Length > 100 ? v.Substring(0, 100) : v, display = v.Length > 60 ? v.Substring(0, 60) + "..." : v }));
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

        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "name",
            string? sort = "id",
            string? dir = "asc",
            int page = 1,
            int pageSize = 25,
            int? fromCode = null,
            int? toCode = null,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? filterCol_id = null,
            string? filterCol_name = null,
            string? filterCol_description = null,
            string? filterCol_active = null,
            string? filterCol_created = null,
            string? filterCol_updated = null)
        {
            var q = BuildPoliciesQuery(
                search, searchBy,
                sort, dir,
                fromCode, toCode,
                useDateRange, fromDate, toDate);

            q = ApplyColumnFilters(q, filterCol_id, filterCol_name, filterCol_description, filterCol_active, filterCol_created, filterCol_updated);

            var model = await PagedResult<Policy>.CreateAsync(q, page, pageSize);

            // تعبئة خصائص إضافية في PagedResult (نفس ما نستخدمه في الجداول الأخرى)
            model.Search = search;
            model.SearchBy = searchBy;
            model.SortColumn = sort;
            model.SortDescending = (dir ?? "asc").ToLower() == "desc";
            model.UseDateRange = useDateRange;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            // 3) تجهيز قيم الـ ViewBag للفلاتر والواجهة
            ViewBag.Search = search ?? "";
            ViewBag.SearchBy = searchBy ?? "name";
            ViewBag.Sort = sort ?? "id";
            ViewBag.Dir = (dir ?? "asc").ToLower() == "desc" ? "desc" : "asc";

            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.CodeFrom = fromCode;
            ViewBag.CodeTo = toCode;
            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_Name = filterCol_name;
            ViewBag.FilterCol_Description = filterCol_description;
            ViewBag.FilterCol_Active = filterCol_active;
            ViewBag.FilterCol_Created = filterCol_created;
            ViewBag.FilterCol_Updated = filterCol_updated;

            ViewBag.DateField = "CreatedAt";

            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalCount = model.TotalCount;

            return View(model);
        }








        // =========================
        // Export — تصدير السياسات (CSV يفتح في Excel)
        [HttpGet]
        public async Task<IActionResult> Export(
        string? search,
        string? searchBy,
        string? sort,
        string? dir,
        bool useDateRange = false,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int? fromCode = null,
        int? toCode = null,
        string? filterCol_id = null,
        string? filterCol_name = null,
        string? filterCol_description = null,
        string? filterCol_active = null,
        string? filterCol_created = null,
        string? filterCol_updated = null,
        string format = "excel")
        {
            var query = BuildPoliciesQuery(
                search,
                searchBy,
                sort,
                dir,
                fromCode,
                toCode,
                useDateRange,
                fromDate,
                toDate);

            query = ApplyColumnFilters(query, filterCol_id, filterCol_name, filterCol_description, filterCol_active, filterCol_created, filterCol_updated);

            var list = await query.ToListAsync();   // متغير: قائمة السياسات الجاهزة للتصدير

            // 3) لو المطلوب Excel (افتراضي)
            if (string.Equals(format, "excel", StringComparison.OrdinalIgnoreCase))
            {
                // إنشاء ملف Excel في الذاكرة باستخدام ClosedXML
                using var workbook = new XLWorkbook();                 // متغير: مصنف Excel
                var worksheet = workbook.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("سياسات العملاء"));

                int row = 1; // متغير: رقم الصف الحالي

                // عناوين الأعمدة (الهيدر)
                worksheet.Cell(row, 1).Value = "كود السياسة";
                worksheet.Cell(row, 2).Value = "اسم السياسة";
                worksheet.Cell(row, 3).Value = "الوصف";
                worksheet.Cell(row, 4).Value = "مفعّلة؟";
                worksheet.Cell(row, 5).Value = "نسبة الربح الافتراضية %";
                worksheet.Cell(row, 6).Value = "تاريخ الإنشاء";
                worksheet.Cell(row, 7).Value = "آخر تعديل";

                // تنسيق الهيدر بخط عريض
                var headerRange = worksheet.Range(row, 1, row, 7);
                headerRange.Style.Font.Bold = true;

                // كتابة بيانات السياسات
                foreach (var p in list)
                {
                    row++;

                    worksheet.Cell(row, 1).Value = p.PolicyId;                           // كود السياسة
                    worksheet.Cell(row, 2).Value = p.Name;                               // اسم السياسة
                    worksheet.Cell(row, 3).Value = p.Description ?? string.Empty;        // الوصف
                    worksheet.Cell(row, 4).Value = p.IsActive ? "مفعّلة" : "موقوفة";    // حالة التفعيل
                    worksheet.Cell(row, 5).Value = p.DefaultProfitPercent;               // نسبة الربح الافتراضية
                    worksheet.Cell(row, 6).Value = p.CreatedAt;                          // تاريخ الإنشاء
                    worksheet.Cell(row, 7).Value = p.UpdatedAt;                          // آخر تعديل (قد تكون null)
                }

                // ضبط عرض الأعمدة تلقائياً
                worksheet.Columns().AdjustToContents();

                // حفظ الملف في MemoryStream ثم إرجاعه للمستخدم
                using var stream = new MemoryStream();       // متغير: ستريم في الذاكرة
                workbook.SaveAs(stream);
                stream.Position = 0;

                var fileName = ExcelExportNaming.ArabicTimestampedFileName("سياسات العملاء", ".xlsx");
                const string contentType =
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

                return File(stream.ToArray(), contentType, fileName);
            }
            else
            {
                // 4) حالة CSV (لو المستخدم اختار CSV من الواجهة)
                var sb = new StringBuilder();   // متغير: يبني نص ملف CSV

                sb.AppendLine("كود السياسة,اسم السياسة,الوصف,مفعّلة؟,نسبة الربح الافتراضية %,تاريخ الإنشاء,آخر تعديل");

                foreach (var p in list)
                {
                    string name = (p.Name ?? string.Empty).Replace("\"", "\"\"");          // اسم السياسة بشكل آمن
                    string desc = (p.Description ?? string.Empty).Replace("\"", "\"\"");   // الوصف
                    string created = p.CreatedAt.ToString("yyyy-MM-dd HH:mm");              // تاريخ الإنشاء كنص
                    string updated = p.UpdatedAt.HasValue
                        ? p.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm")
                        : string.Empty;                                                     // آخر تعديل (ممكن تكون فاضية)

                    sb.AppendLine(
                        $"{p.PolicyId}," +
                        $"\"{name}\"," +
                        $"\"{desc}\"," +
                        $"\"{(p.IsActive ? "مفعّلة" : "موقوفة")}\"," +
                        $"{p.DefaultProfitPercent}," +
                        $"{created}," +
                        $"{updated}");
                }

                byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString());
                string fileName = ExcelExportNaming.ArabicTimestampedFileName("سياسات العملاء", ".csv");

                return File(bytes, "text/csv", fileName);
            }
        }









        // =========================
        // Details — عرض تفاصيل سياسة واحدة (فورم قراءة فقط)
        // =========================
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var policy = await _context.Policies
                                       .AsNoTracking()
                                       .FirstOrDefaultAsync(x => x.PolicyId == id);

            if (policy == null)
                return NotFound();

            return View(policy);   // View: Views/Policies/Details.cshtml
        }








        // =========================
        // Create — GET: عرض فورم إضافة سياسة جديدة
        // =========================
        [HttpGet]
        public IActionResult Create()
        {
            // ممكن نحط قيم افتراضية هنا لو حابب
            var model = new Policy
            {
                IsActive = true
            };

            return View(model);   // View: Views/Policies/Create.cshtml
        }








        // =========================
        // Create — POST: حفظ سياسة جديدة في قاعدة البيانات
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Policy policy)
        {
            // 1) لو في أخطاء فاليديشن نرجّع نفس الفورم
            if (!ModelState.IsValid)
            {
                return View(policy);
            }

            // 2) ضبط تاريخ الإنشاء فقط
            //    (تقدر تعتمد على القيمة الافتراضية UtcNow أو تكتب Now لو حابب)
            policy.CreatedAt = DateTime.Now;    // تاريخ إنشاء السجل

            // 3) مهم: لا نلمس UpdatedAt هنا علشان تفضل null
            // policy.UpdatedAt = null;   // اختياري؛ هتكون null تلقائياً

            _context.Policies.Add(policy);
            await _context.SaveChangesAsync();

            _lookupCache.ClearPoliciesCache();

            await _activityLogger.LogAsync(UserActionType.Create, "Policy", policy.PolicyId, $"إنشاء سياسة: {policy.Name}");

            TempData["SuccessMessage"] = "تم إضافة السياسة بنجاح.";
            return RedirectToAction(nameof(Index));
        }








        // =========================
        // Edit — GET: عرض فورم تعديل سياسة
        // =========================
        [HttpGet]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
                return NotFound();

            var policy = await _context.Policies.FindAsync(id.Value);
            if (policy == null)
                return NotFound();

            return View(policy);   // View: Views/Policies/Edit.cshtml
        }








        // =========================
        // Edit — POST: حفظ التعديلات على سياسة
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Policy policy)
        {
            // تأكد أن الـ id في الرابط هو نفس المفتاح في الموديل
            if (id != policy.PolicyId)
                return NotFound();

            if (!ModelState.IsValid)
            {
                return View(policy);
            }

            try
            {
                var existing = await _context.Policies.AsNoTracking().FirstOrDefaultAsync(x => x.PolicyId == id);
                var oldValues = existing != null ? System.Text.Json.JsonSerializer.Serialize(new { existing.Name, existing.Description }) : null;
                // تحديث آخر تعديل
                policy.UpdatedAt = DateTime.Now;

                // تحديث الكيان في الـ DbContext
                _context.Update(policy);

                // حفظ التغييرات
                await _context.SaveChangesAsync();

                _lookupCache.ClearPoliciesCache();

                var newValues = System.Text.Json.JsonSerializer.Serialize(new { policy.Name, policy.Description });
                await _activityLogger.LogAsync(UserActionType.Edit, "Policy", id, $"تعديل سياسة: {policy.Name}", oldValues, newValues);

                TempData["SuccessMessage"] = "تم تعديل السياسة بنجاح.";
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateConcurrencyException)
            {
                // لو السياسة اتحذفت أثناء التعديل
                bool exists = await _context.Policies.AnyAsync(x => x.PolicyId == id);
                if (!exists)
                    return NotFound();

                // تعارض حقيقي في التعديل
                ModelState.AddModelError(
                    string.Empty,
                    "تعذر الحفظ بسبب تعديل متزامن. أعد تحميل الصفحة ثم حاول مرة أخرى.");

                return View(policy);
            }
        }









        // =========================
        // Delete — GET: صفحة تأكيد حذف سياسة واحدة
        // =========================
        [HttpGet]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
                return NotFound();

            var policy = await _context.Policies
                                       .AsNoTracking()
                                       .FirstOrDefaultAsync(x => x.PolicyId == id.Value);

            if (policy == null)
                return NotFound();

            return View(policy);   // View: Views/Policies/Delete.cshtml
        }

        // =========================
        // Delete — POST: تنفيذ الحذف لسجل واحد
        // =========================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var policy = await _context.Policies
                                       .FirstOrDefaultAsync(x => x.PolicyId == id);

            if (policy == null)
                return NotFound();

            try
            {
                var oldValues = System.Text.Json.JsonSerializer.Serialize(new { policy.Name, policy.Description });
                _context.Policies.Remove(policy);
                await _context.SaveChangesAsync();

                _lookupCache.ClearPoliciesCache();

                await _activityLogger.LogAsync(UserActionType.Delete, "Policy", id, $"حذف سياسة: {policy.Name}", oldValues: oldValues);

                TempData["SuccessMessage"] = "تم حذف السياسة بنجاح.";
            }
            catch (DbUpdateException)
            {
                // في حالة وجود علاقات تمنع الحذف (مثلاً سياسة مستخدمة في عملاء)
                TempData["ErrorMessage"] = "تعذر حذف هذه السياسة لأنها مرتبطة ببيانات أخرى.";
            }

            return RedirectToAction(nameof(Index));
        }








        // =========================
        // BulkDelete — حذف مجموعة سياسات محددة من الجدول
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedIds)
        {
            // selectedIds يأتي من الـ hidden في الفورم (قائمة أرقام مفصولة بفاصلة)
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["ErrorMessage"] = "لم يتم اختيار أي سياسات للحذف.";
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
                TempData["ErrorMessage"] = "لم يتم اختيار أي سياسات صحيحة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var policies = await _context.Policies
                                         .Where(x => ids.Contains(x.PolicyId))
                                         .ToListAsync();

            if (!policies.Any())
            {
                TempData["ErrorMessage"] = "لم يتم العثور على السياسات المحددة.";
                return RedirectToAction(nameof(Index));
            }

            _context.Policies.RemoveRange(policies);
            await _context.SaveChangesAsync();

            _lookupCache.ClearPoliciesCache();

            TempData["SuccessMessage"] = $"تم حذف {policies.Count} سياسة.";
            return RedirectToAction(nameof(Index));
        }









        // =========================
        // DeleteAll — حذف كل السياسات المطابقة للفلاتر الحالية
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            int? fromCode,
            int? toCode,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null)
        {
            // نستخدم نفس BuildPoliciesQuery عشان نحترم الفلاتر الحالية
            var q = BuildPoliciesQuery(
                search, searchBy,
                sort, dir,
                fromCode, toCode,
                useDateRange, fromDate, toDate);

            var policies = await q.ToListAsync();

            if (!policies.Any())
            {
                TempData["ErrorMessage"] = "لا توجد سياسات مطابقة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            _context.Policies.RemoveRange(policies);
            await _context.SaveChangesAsync();

            _lookupCache.ClearPoliciesCache();

            TempData["SuccessMessage"] = $"تم حذف {policies.Count} سياسة (حسب الفلاتر الحالية).";
            return RedirectToAction(nameof(Index));
        }
    }
}
