using ClosedXML.Excel;                              // تصدير Excel
using ERP.Data;                                    // كائن AppDbContext
using ERP.Infrastructure;                          // PagedResult + UserActivityLogger
using ERP.Models;                                  // Area, UserActionType
using Microsoft.AspNetCore.Mvc;                    // أساس الكنترولر
using Microsoft.AspNetCore.Mvc.Rendering;          // SelectList و SelectListItem
using Microsoft.EntityFrameworkCore;               // Include, AsNoTracking, ToListAsync
using System;                                      // متغيرات التوقيت DateTime
using System.Collections.Generic;                  // القوائم List
using System.IO;                                  // MemoryStream للتصدير
using System.Linq;                                 // أوامر LINQ مثل Where و OrderBy
using System.Threading.Tasks;                      // Task و async/await

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إدارة جدول المناطق (Areas)
    /// نفس نظام الحسابات: بحث + ترتيب + ترقيم + فلترة بتاريخ الإنشاء.
    /// </summary>
    public class AreasController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IUserActivityLogger _activityLogger;

        public AreasController(AppDbContext db, IUserActivityLogger activityLogger)
        {
            _db = db;
            _activityLogger = activityLogger;
        }

        // ===============================
        // GET: /Areas
        // قائمة المناطق + بحث/ترتيب/ترقيم + فلترة بتاريخ الإنشاء
        // ===============================
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? governorateId = null,
            int? districtId = null,
            int page = 1,
            int pageSize = 25)
        {
            // ===== تنظيف القيم الافتراضية =====
            search = (search ?? string.Empty).Trim();                        // نص البحث
            searchBy = string.IsNullOrWhiteSpace(searchBy) ? "name" : searchBy.ToLower(); // حقل البحث
            sort = string.IsNullOrWhiteSpace(sort) ? "name" : sort.ToLower();         // عمود الترتيب
            dir = string.IsNullOrWhiteSpace(dir) ? "asc" : dir.ToLower();           // اتجاه الترتيب

            bool desc = dir == "desc";               // هل الترتيب تنازلي؟

            // ===== الاستعلام الأساسي من جدول Areas =====
            var query = _db.Areas
                .Include(a => a.Governorate)                             // المحافظة
                .Include(a => a.District)!.ThenInclude(d => d.Governorate) // الحي/المركز + المحافظة
                .AsNoTracking()
                .AsQueryable();

            // ===== تطبيق البحث =====
            if (!string.IsNullOrEmpty(search))
            {
                switch (searchBy)
                {
                    case "id":   // البحث بالمعرّف
                        if (int.TryParse(search, out var idValue))
                        {
                            query = query.Where(a => a.AreaId == idValue);
                        }
                        else
                        {
                            query = query.Where(a => a.AreaId.ToString().Contains(search));
                        }
                        break;

                    case "dist": // البحث باسم الحي/المركز
                        query = query.Where(a =>
                            a.District != null &&
                            a.District.DistrictName.Contains(search));
                        break;

                    case "gov":  // البحث باسم المحافظة
                        query = query.Where(a =>
                            a.Governorate != null &&
                            a.Governorate.GovernorateName.Contains(search));
                        break;

                    case "name":
                    default:     // البحث باسم المنطقة
                        query = query.Where(a => a.AreaName.Contains(search));
                        break;
                }
            }

            // ===== فلتر المحافظة + الحي/المركز =====
            if (governorateId.HasValue)
                query = query.Where(a => a.GovernorateId == governorateId.Value);

            if (districtId.HasValue)
                query = query.Where(a => a.DistrictId == districtId.Value);

            // ===== فلترة بالتاريخ (تاريخ الإنشاء) =====
            if (useDateRange)
            {
                if (fromDate.HasValue)
                {
                    query = query.Where(a =>
                        a.CreatedAt.HasValue &&
                        a.CreatedAt.Value >= fromDate.Value);
                }

                if (toDate.HasValue)
                {
                    query = query.Where(a =>
                        a.CreatedAt.HasValue &&
                        a.CreatedAt.Value <= toDate.Value);
                }
            }

            // ===== الترتيب (كل أعمدة الجدول لها مفتاح) =====
            // المفاتيح:
            // id       = المعرّف
            // name     = اسم المنطقة
            // dist     = الحي/المركز
            // gov      = المحافظة
            // isactive = الحالة
            // created  = تاريخ الإنشاء
            // updated  = آخر تعديل
            query = (sort, desc) switch
            {
                ("id", false) => query.OrderBy(a => a.AreaId),
                ("id", true) => query.OrderByDescending(a => a.AreaId),

                ("name", false) => query.OrderBy(a => a.AreaName),
                ("name", true) => query.OrderByDescending(a => a.AreaName),

                ("dist", false) => query.OrderBy(a => a.District!.DistrictName)
                                             .ThenBy(a => a.AreaName),
                ("dist", true) => query.OrderByDescending(a => a.District!.DistrictName)
                                             .ThenByDescending(a => a.AreaName),

                ("gov", false) => query.OrderBy(a => a.Governorate!.GovernorateName)
                                             .ThenBy(a => a.AreaName),
                ("gov", true) => query.OrderByDescending(a => a.Governorate!.GovernorateName)
                                             .ThenByDescending(a => a.AreaName),

                ("isactive", false) => query.OrderByDescending(a => a.IsActive)
                                             .ThenBy(a => a.AreaName),
                ("isactive", true) => query.OrderBy(a => a.IsActive)
                                             .ThenBy(a => a.AreaName),

                ("created", false) => query.OrderBy(a => a.CreatedAt),
                ("created", true) => query.OrderByDescending(a => a.CreatedAt),

                ("updated", false) => query.OrderBy(a => a.UpdatedAt),
                ("updated", true) => query.OrderByDescending(a => a.UpdatedAt),

                // الافتراضي: بالاسم تصاعدي
                _ => query.OrderBy(a => a.AreaName),
            };

            // ===== إنشاء نتيجة PagedResult مع حفظ حالة البحث/الترتيب =====
            var result = await PagedResult<Area>.CreateAsync(
                query,
                page,
                pageSize,
                sort,        // عمود الترتيب الحالي
                desc,        // هل الترتيب تنازلي؟
                search,      // نص البحث الحالي
                searchBy     // حقل البحث الحالي
            );

            // تخزين حالة فلتر التاريخ داخل الموديل لعرضها في الشاشة
            result.UseDateRange = useDateRange;
            result.FromDate = fromDate;
            result.ToDate = toDate;

            // ===== إعداد خيارات البحث/الترتيب للـ _IndexFilters =====
            ViewBag.SearchOptions = new[]
            {
                new SelectListItem("اسم المنطقة",  "name", searchBy == "name"),
                new SelectListItem("المعرّف",      "id",   searchBy == "id"),
                new SelectListItem("الحي/المركز",  "dist", searchBy == "dist"),
                new SelectListItem("المحافظة",     "gov",  searchBy == "gov"),
            };

            ViewBag.SortOptions = new[]
            {
                new SelectListItem("المعرّف",        "id",       sort == "id"),
                new SelectListItem("اسم المنطقة",    "name",     sort == "name"),
                new SelectListItem("الحي/المركز",    "dist",     sort == "dist"),
                new SelectListItem("المحافظة",       "gov",      sort == "gov"),
                new SelectListItem("الحالة",         "isactive", sort == "isactive"),
                new SelectListItem("تاريخ الإنشاء",  "created",  sort == "created"),
                new SelectListItem("آخر تعديل",      "updated",  sort == "updated"),
            };

            // ===== تحميل القوائم المنسدلة (المحافظة + الحي/المركز) =====
            await LoadLookupsAsync(governorateId, districtId);

            // تمرير قيم الفلاتر للـ View (تُستخدم في _IndexFilters وفلتر المحافظة)
            ViewBag.Search = search;
            ViewBag.SearchBy = searchBy;
            ViewBag.Sort = sort;
            ViewBag.Dir = dir;

            ViewBag.GovFilter = governorateId;
            ViewBag.DistrictFilter = districtId;

            return View(result);    // إرسال النتيجة لواجهة Index
        }

        // =============================== تفاصيل/إضافة/تعديل/حذف ===============================

        /// <summary>
        /// عرض تفاصيل منطقة واحدة.
        /// </summary>
        public async Task<IActionResult> Details(int id)
        {
            var item = await _db.Areas
                                .Include(a => a.Governorate)
                                .Include(a => a.District)!.ThenInclude(d => d.Governorate)
                                .AsNoTracking()
                                .FirstOrDefaultAsync(a => a.AreaId == id);

            if (item == null)
                return NotFound();

            return View(item);
        }

        /// <summary>
        /// GET: عرض فورم إضافة منطقة جديدة.
        /// </summary>
        public async Task<IActionResult> Create()
        {
            // تحميل القوائم المنسدلة للمحافظة والحي/المركز
            await LoadLookupsAsync();

            // إنشاء موديل جديد مع جعل المنطقة نشطة افتراضياً
            var model = new Area
            {
                IsActive = true,
                CreatedAt = DateTime.Now
            };

            return View(model);
        }

        /// <summary>
        /// POST: استلام بيانات المنطقة الجديدة وحفظها في قاعدة البيانات.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Area area)
        {
            // تحقق منطقي: الحي يتبع المحافظة المختارة
            if (!await DistrictMatchesGovernorateAsync(area.DistrictId, area.GovernorateId))
                ModelState.AddModelError("DistrictId", "الحي/المركز المختار لا يتبع المحافظة المحددة.");

            if (!ModelState.IsValid)
            {
                // إعادة تحميل القوائم المنسدلة عند وجود أخطاء
                await LoadLookupsAsync(area.GovernorateId, area.DistrictId);
                return View(area);
            }

            area.CreatedAt = DateTime.Now;
            area.UpdatedAt = DateTime.Now;

            _db.Areas.Add(area);
            await _db.SaveChangesAsync();

            await _activityLogger.LogAsync(UserActionType.Create, "Area", area.AreaId, $"إنشاء منطقة: {area.AreaName}");

            TempData["Ok"] = "تمت إضافة المنطقة بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// GET: عرض فورم تعديل منطقة موجودة.
        /// </summary>
        public async Task<IActionResult> Edit(int id)
        {
            var item = await _db.Areas.FindAsync(id);
            if (item == null)
                return NotFound();

            await LoadLookupsAsync(item.GovernorateId, item.DistrictId);
            return View(item);
        }

        /// <summary>
        /// POST: استلام بيانات تعديل المنطقة وحفظها.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Area area)
        {
            if (id != area.AreaId)
                return BadRequest();

            // تحقق منطقي: الحي يتبع المحافظة المختارة
            if (!await DistrictMatchesGovernorateAsync(area.DistrictId, area.GovernorateId))
                ModelState.AddModelError("DistrictId", "الحي/المركز المختار لا يتبع المحافظة المحددة.");

            if (!ModelState.IsValid)
            {
                await LoadLookupsAsync(area.GovernorateId, area.DistrictId);
                return View(area);
            }

            var dbItem = await _db.Areas.FindAsync(id);
            if (dbItem == null)
                return NotFound();

            var oldValues = System.Text.Json.JsonSerializer.Serialize(new { dbItem.AreaName, dbItem.GovernorateId, dbItem.DistrictId, dbItem.IsActive });
            // نسخ القيم المسموح بتعديلها
            dbItem.AreaName = area.AreaName;
            dbItem.GovernorateId = area.GovernorateId;
            dbItem.DistrictId = area.DistrictId;
            dbItem.IsActive = area.IsActive;
            dbItem.Notes = area.Notes;
            dbItem.UpdatedAt = DateTime.Now;

            await _db.SaveChangesAsync();

            var newValues = System.Text.Json.JsonSerializer.Serialize(new { dbItem.AreaName, dbItem.GovernorateId, dbItem.DistrictId, dbItem.IsActive });
            await _activityLogger.LogAsync(UserActionType.Edit, "Area", id, $"تعديل منطقة: {dbItem.AreaName}", oldValues, newValues);

            TempData["Ok"] = "تم تعديل المنطقة بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// GET: صفحة تأكيد حذف منطقة.
        /// </summary>
        public async Task<IActionResult> Delete(int id)
        {
            var item = await _db.Areas
                                .Include(a => a.Governorate)
                                .Include(a => a.District)
                                .AsNoTracking()
                                .FirstOrDefaultAsync(a => a.AreaId == id);

            if (item == null)
                return NotFound();

            return View(item);
        }

        /// <summary>
        /// POST: تنفيذ الحذف بعد التأكيد.
        /// </summary>
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var item = await _db.Areas.FindAsync(id);
            if (item == null)
                return NotFound();

            var oldValues = System.Text.Json.JsonSerializer.Serialize(new { item.AreaName, item.GovernorateId, item.DistrictId });
            _db.Areas.Remove(item);
            await _db.SaveChangesAsync();

            await _activityLogger.LogAsync(UserActionType.Delete, "Area", id, $"حذف منطقة: {item.AreaName}", oldValues: oldValues);

            TempData["Ok"] = "تم حذف المنطقة بنجاح.";
            return RedirectToAction(nameof(Index));
        }



        // ====================== حذف مجموعة من المناطق (حذف المحدد) ======================
        [HttpPost]
        [ValidateAntiForgeryToken]          // حماية من طلبات مزيفة
        public async Task<IActionResult> BulkDelete(string? selectedIds)
        {
            // selectedIds = "1,2,3" جاية من الهيدن في الفورم
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                // لو مفيش ولا رقم، نرجّع للشاشة بدون ما نعمل حاجة
                TempData["Error"] = "لم يتم اختيار أي منطقة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            // تحويل النص لقائمة أرقام صحيحة
            var ids = selectedIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)    // نفصل بالأComma
                .Select(x => int.TryParse(x, out var id) ? id : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();

            if (ids.Count == 0)
            {
                TempData["Error"] = "لم يتم اختيار أي منطقة صالحة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            // نحضر المناطق المطابقة للأرقام
            var areas = await _db.Areas
                .Where(a => ids.Contains(a.AreaId))
                .ToListAsync();

            if (areas.Count == 0)
            {
                TempData["Error"] = "لا توجد مناطق مطابقة للأرقام المحددة.";
                return RedirectToAction(nameof(Index));
            }

            _db.Areas.RemoveRange(areas);   // حذف جماعي
            await _db.SaveChangesAsync();   // حفظ التغييرات في الداتا بيز

            TempData["Success"] = $"تم حذف {areas.Count} منطقة/مناطق بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // ====================== حذف جميع المناطق ======================
        [HttpPost]
        [ValidateAntiForgeryToken]          // حماية من طلبات مزيفة
        public async Task<IActionResult> DeleteAll()
        {
            // نحضر كل السجلات (ممكن تحط شرط لو حابب بعدين)
            var allAreas = await _db.Areas.ToListAsync();

            if (allAreas.Count == 0)
            {
                TempData["Error"] = "لا توجد مناطق لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            _db.Areas.RemoveRange(allAreas);   // حذف كل المناطق
            await _db.SaveChangesAsync();       // حفظ التغييرات

            TempData["Success"] = "تم حذف جميع المناطق بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // ====================== تصدير المناطق (Excel / CSV) ======================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? governorateId = null,
            int? districtId = null,
            int? fromCode = null,
            int? toCode = null,
            string format = "excel"   // excel أو csv حسب اختيار المستخدم
        )
        {
            // بداية الاستعلام: جدول المناطق + ربط المحافظة والحي/المركز
            var query = _db.Areas
                .Include(a => a.District)
                .Include(a => a.Governorate)
                .AsNoTracking()
                .AsQueryable();

            // ---------- نفس فلاتر الإندكس تقريباً ----------

            // فلتر البحث العام
            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();

                switch (searchBy?.ToLower())
                {
                    case "id":
                        query = query.Where(a => a.AreaId.ToString().Contains(search));
                        break;

                    case "district":
                        query = query.Where(a =>
                            a.District != null &&
                            a.District.DistrictName.Contains(search));
                        break;

                    case "governorate":
                        query = query.Where(a =>
                            a.Governorate != null &&
                            a.Governorate.GovernorateName.Contains(search));
                        break;

                    case "name":
                    default:
                        query = query.Where(a => a.AreaName.Contains(search));
                        break;
                }
            }

            // فلتر المحافظة
            if (governorateId.HasValue)
            {
                query = query.Where(a => a.GovernorateId == governorateId.Value);
            }

            // فلتر الحي/المركز
            if (districtId.HasValue)
            {
                query = query.Where(a => a.DistrictId == districtId.Value);
            }

            // فلتر كود من/إلى
            if (fromCode.HasValue)
            {
                query = query.Where(a => a.AreaId >= fromCode.Value);
            }
            if (toCode.HasValue)
            {
                query = query.Where(a => a.AreaId <= toCode.Value);
            }

            // فلتر التاريخ (تاريخ الإنشاء) لو مفعّل
            if (useDateRange)
            {
                if (fromDate.HasValue)
                    query = query.Where(a => a.CreatedAt >= fromDate.Value);

                if (toDate.HasValue)
                    query = query.Where(a => a.CreatedAt <= toDate.Value);
            }

            // ---------- الترتيب (نفس الإندكس) ----------
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            query = (sort ?? "").ToLower() switch
            {
                "id" => desc ? query.OrderByDescending(a => a.AreaId) : query.OrderBy(a => a.AreaId),
                "district" => desc ? query.OrderByDescending(a => a.District!.DistrictName)
                                   : query.OrderBy(a => a.District!.DistrictName),
                "governorate" => desc ? query.OrderByDescending(a => a.Governorate!.GovernorateName)
                                      : query.OrderBy(a => a.Governorate!.GovernorateName),
                "isactive" => desc ? query.OrderByDescending(a => a.IsActive) : query.OrderBy(a => a.IsActive),
                "created" => desc ? query.OrderByDescending(a => a.CreatedAt) : query.OrderBy(a => a.CreatedAt),
                "updated" => desc ? query.OrderByDescending(a => a.UpdatedAt) : query.OrderBy(a => a.UpdatedAt),
                "name" or _ => desc ? query.OrderByDescending(a => a.AreaName) : query.OrderBy(a => a.AreaName),
            };

            // نحضر النتيجة كاملة بدون تقسيم صفحات
            var data = await query.ToListAsync();
            var fmt = (format ?? "excel").Trim().ToLowerInvariant();

            // ---------- CSV ----------
            if (string.Equals(fmt, "csv", StringComparison.OrdinalIgnoreCase))
            {
                static string CsvEscape(string? value)
                {
                    if (string.IsNullOrEmpty(value)) return "";
                    return "\"" + value.Replace("\"", "\"\"") + "\"";
                }

                var lines = new List<string> { "Id,Name,District,Governorate,IsActive,CreatedAt,UpdatedAt" };
                foreach (var a in data)
                {
                    lines.Add(string.Join(",",
                        a.AreaId.ToString(),
                        CsvEscape(a.AreaName),
                        CsvEscape(a.District?.DistrictName),
                        CsvEscape(a.Governorate?.GovernorateName),
                        a.IsActive ? "نشط" : "موقوف",
                        a.CreatedAt?.ToString("yyyy-MM-dd HH:mm") ?? "",
                        a.UpdatedAt?.ToString("yyyy-MM-dd HH:mm") ?? ""
                    ));
                }

                var bytes = System.Text.Encoding.UTF8.GetBytes(string.Join("\r\n", lines));
                return File(bytes, "text/csv; charset=utf-8", $"Areas_{DateTime.Now:yyyyMMdd_HHmm}.csv");
            }

            // ---------- Excel (.xlsx) ----------
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("المناطق");

            ws.Cell(1, 1).Value = "كود المنطقة";
            ws.Cell(1, 2).Value = "اسم المنطقة";
            ws.Cell(1, 3).Value = "الحي/المركز";
            ws.Cell(1, 4).Value = "المحافظة";
            ws.Cell(1, 5).Value = "الحالة";
            ws.Cell(1, 6).Value = "تاريخ الإنشاء";
            ws.Cell(1, 7).Value = "آخر تعديل";

            var header = ws.Range(1, 1, 1, 7);
            header.Style.Font.Bold = true;
            header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int row = 2;
            foreach (var a in data)
            {
                ws.Cell(row, 1).Value = a.AreaId;
                ws.Cell(row, 2).Value = a.AreaName ?? "";
                ws.Cell(row, 3).Value = a.District?.DistrictName ?? "";
                ws.Cell(row, 4).Value = a.Governorate?.GovernorateName ?? "";
                ws.Cell(row, 5).Value = a.IsActive ? "نشط" : "موقوف";
                ws.Cell(row, 6).Value = a.CreatedAt?.ToString("yyyy-MM-dd HH:mm") ?? "";
                ws.Cell(row, 7).Value = a.UpdatedAt?.ToString("yyyy-MM-dd HH:mm") ?? "";
                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            stream.Position = 0;
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Areas_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }




        // =============================== دوال مساعدة ===============================

        /// <summary>
        /// تحميل القوائم المنسدلة للمحافظات والأحياء/المراكز.
        /// تُستخدم في Index + Create + Edit.
        /// </summary>
        private async Task LoadLookupsAsync(int? selectedGovId = null, int? selectedDistrictId = null)
        {
            ViewBag.GovernorateId = new SelectList(
                await _db.Governorates
                         .AsNoTracking()
                         .OrderBy(g => g.GovernorateName)
                         .ToListAsync(),
                "GovernorateId",
                "GovernorateName",
                selectedGovId);

            var dQuery = _db.Districts
                            .AsNoTracking()
                            .OrderBy(d => d.DistrictName);

            if (selectedGovId.HasValue)
                dQuery = dQuery
                    .Where(d => d.GovernorateId == selectedGovId.Value)
                    .OrderBy(d => d.DistrictName);

            ViewBag.DistrictId = new SelectList(
                await dQuery.ToListAsync(),
                "DistrictId",
                "DistrictName",
                selectedDistrictId);
        }

        /// <summary>
        /// فحص اتساق: هل الحي المختار يتبع نفس المحافظة؟
        /// </summary>
        private async Task<bool> DistrictMatchesGovernorateAsync(int districtId, int governorateId)
        {
            var distGovId = await _db.Districts
                                     .Where(d => d.DistrictId == districtId)
                                     .Select(d => d.GovernorateId)
                                     .FirstOrDefaultAsync();

            return distGovId == governorateId;
        }
    }
}
