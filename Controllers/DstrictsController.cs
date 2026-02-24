using ClosedXML.Excel;
using DocumentFormat.OpenXml.InkML;
using ERP.Data;                         // AppDbContext
using ERP.Infrastructure;               // PagedResult + UserActivityLogger
using ERP.Models;                       // District, UserActionType
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;                        // MemoryStream للتصدير
using System.Linq;
using System.Text;                    // علشان StringBuilder و Encoding في التصدير
using System.Threading.Tasks;

namespace ERP.Controllers
{
    /// <summary>
    /// إدارة جدول الأحياء/المراكز
    /// نفس نظام Area: بحث + ترتيب + ترقيم + فلترة بتاريخ الإنشاء.
    /// </summary>
    public class DistrictsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IUserActivityLogger _activityLogger;

        public DistrictsController(AppDbContext db, IUserActivityLogger activityLogger)
        {
            _db = db;
            _activityLogger = activityLogger;
        }

        // =========================================
        // Index: قائمة الأحياء/المراكز مع الفلاتر
        // =========================================
        [HttpGet]
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? governorateId = null,
            byte? type = null,
            int page = 1,
            int pageSize = 25)
        {
            // تنظيف القيم الافتراضية
            search = (search ?? string.Empty).Trim();          // نص البحث
            searchBy = string.IsNullOrWhiteSpace(searchBy) ? "name" : searchBy.ToLower();
            sort = string.IsNullOrWhiteSpace(sort) ? "name" : sort.ToLower();
            dir = string.IsNullOrWhiteSpace(dir) ? "asc" : dir.ToLower();

            bool desc = dir == "desc";   // هل الترتيب تنازلي؟

            // ===== الاستعلام الأساسي =====
            var query = _db.Districts
                .Include(d => d.Governorate)    // جلب اسم المحافظة
                .AsNoTracking()
                .AsQueryable();

            // ===== البحث =====
            if (!string.IsNullOrEmpty(search))
            {
                switch (searchBy)
                {
                    case "id":   // البحث بالمعرّف
                        if (int.TryParse(search, out var idValue))
                        {
                            query = query.Where(d => d.DistrictId == idValue);
                        }
                        else
                        {
                            query = query.Where(d => d.DistrictId.ToString().Contains(search));
                        }
                        break;

                    case "gov":  // البحث باسم المحافظة
                        query = query.Where(d =>
                            d.Governorate != null &&
                            d.Governorate.GovernorateName.Contains(search));
                        break;

                    case "name":
                    default:     // البحث باسم الحي/المركز
                        query = query.Where(d => d.DistrictName.Contains(search));
                        break;
                }
            }

            // ===== فلتر المحافظة =====
            if (governorateId.HasValue && governorateId.Value > 0)
            {
                query = query.Where(d => d.GovernorateId == governorateId.Value);
            }

            // ===== فلتر النوع (حي / مركز) =====
            if (type.HasValue)
            {
                query = query.Where(d => d.DistrictType == type.Value);
            }

            // ===== فلترة بالتاريخ (تاريخ الإنشاء) =====
            if (useDateRange)
            {
                if (fromDate.HasValue)
                {
                    query = query.Where(d =>
                        d.CreatedAt.HasValue &&
                        d.CreatedAt.Value >= fromDate.Value);
                }

                if (toDate.HasValue)
                {
                    query = query.Where(d =>
                        d.CreatedAt.HasValue &&
                        d.CreatedAt.Value <= toDate.Value);
                }
            }

            // ===== الترتيب (كل أعمدة الجدول) =====
            // id, name, gov, type, isactive, created, updated
            query = (sort, desc) switch
            {
                ("id", false) => query.OrderBy(d => d.DistrictId),
                ("id", true) => query.OrderByDescending(d => d.DistrictId),

                ("name", false) => query.OrderBy(d => d.DistrictName),
                ("name", true) => query.OrderByDescending(d => d.DistrictName),

                ("gov", false) => query.OrderBy(d => d.Governorate!.GovernorateName)
                                           .ThenBy(d => d.DistrictName),
                ("gov", true) => query.OrderByDescending(d => d.Governorate!.GovernorateName)
                                           .ThenByDescending(d => d.DistrictName),

                ("type", false) => query.OrderBy(d => d.DistrictType)
                                           .ThenBy(d => d.DistrictName),
                ("type", true) => query.OrderByDescending(d => d.DistrictType)
                                           .ThenByDescending(d => d.DistrictName),

                ("isactive", false) => query.OrderByDescending(d => d.IsActive)
                                           .ThenBy(d => d.DistrictName),
                ("isactive", true) => query.OrderBy(d => d.IsActive)
                                           .ThenBy(d => d.DistrictName),

                ("created", false) => query.OrderBy(d => d.CreatedAt),
                ("created", true) => query.OrderByDescending(d => d.CreatedAt),

                ("updated", false) => query.OrderBy(d => d.UpdatedAt),
                ("updated", true) => query.OrderByDescending(d => d.UpdatedAt),

                _ => query.OrderBy(d => d.DistrictName),
            };

            // ===== إنشاء نتيجة PagedResult =====
            var result = await PagedResult<District>.CreateAsync(
                query,
                page,
                pageSize,
                sort,
                desc,
                search,
                searchBy
            );

            // تخزين حالة فلتر التاريخ داخل الموديل
            result.UseDateRange = useDateRange;
            result.FromDate = fromDate;
            result.ToDate = toDate;

            // ===== خيارات شريط البحث (_IndexFilters) =====
            ViewBag.SearchOptions = new[]
            {
                new SelectListItem("اسم الحي/المركز", "name",  searchBy == "name"),
                new SelectListItem("المعرّف",          "id",    searchBy == "id"),
                new SelectListItem("المحافظة",         "gov",   searchBy == "gov"),
            };

            ViewBag.SortOptions = new[]
            {
                new SelectListItem("المعرّف",          "id",       sort == "id"),
                new SelectListItem("اسم الحي/المركز", "name",     sort == "name"),
                new SelectListItem("المحافظة",         "gov",      sort == "gov"),
                new SelectListItem("النوع",            "type",     sort == "type"),
                new SelectListItem("الحالة",           "isactive", sort == "isactive"),
                new SelectListItem("تاريخ الإنشاء",    "created",  sort == "created"),
                new SelectListItem("آخر تعديل",        "updated",  sort == "updated"),
            };

            // قيم للفيو
            ViewBag.Search = search;
            ViewBag.SearchBy = searchBy;
            ViewBag.Sort = sort;
            ViewBag.Dir = dir;

           
           

            return View(result);
        }

        // =========================
        // GET: /Districts/Create
        // =========================
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            await LoadFormLookupsAsync();
            return View(new District { IsActive = true });
        }

        // =========================
        // POST: /Districts/Create
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(District model)
        {
            if (!ModelState.IsValid)
            {
                await LoadFormLookupsAsync(model.GovernorateId, model.DistrictType);
                return View(model);
            }

            // ضبط تاريخ الإنشاء والتعديل
            model.CreatedAt = DateTime.Now;
            model.UpdatedAt = DateTime.Now;

            _db.Districts.Add(model);
            await _db.SaveChangesAsync();

            await _activityLogger.LogAsync(UserActionType.Create, "District", model.DistrictId, $"إنشاء حي/مركز: {model.DistrictName}");

            TempData["Ok"] = "تمت إضافة الحي/المركز بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // GET: /Districts/Edit/5
        // =========================
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var model = await _db.Districts.FindAsync(id);
            if (model == null) return NotFound();

            await LoadFormLookupsAsync(model.GovernorateId, model.DistrictType);
            return View(model);
        }

        // =========================
        // POST: /Districts/Edit/5
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, District model)
        {
            if (id != model.DistrictId) return BadRequest();

            if (!ModelState.IsValid)
            {
                await LoadFormLookupsAsync(model.GovernorateId, model.DistrictType);
                return View(model);
            }

            var dbEntity = await _db.Districts.FindAsync(id);
            if (dbEntity == null) return NotFound();

            var oldValues = System.Text.Json.JsonSerializer.Serialize(new { dbEntity.DistrictName, dbEntity.GovernorateId, dbEntity.DistrictType, dbEntity.IsActive });
            // تحديث الحقول القابلة للتعديل
            dbEntity.DistrictName = model.DistrictName;
            dbEntity.GovernorateId = model.GovernorateId;
            dbEntity.DistrictType = model.DistrictType;
            dbEntity.IsActive = model.IsActive;
            dbEntity.Notes = model.Notes;
            dbEntity.UpdatedAt = DateTime.Now;   // وقت آخر تعديل

            await _db.SaveChangesAsync();

            var newValues = System.Text.Json.JsonSerializer.Serialize(new { dbEntity.DistrictName, dbEntity.GovernorateId, dbEntity.DistrictType, dbEntity.IsActive });
            await _activityLogger.LogAsync(UserActionType.Edit, "District", id, $"تعديل حي/مركز: {dbEntity.DistrictName}", oldValues, newValues);

            TempData["Ok"] = "تم تعديل البيانات بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // GET: /Districts/Details/5
        // =========================
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var item = await _db.Districts
                                .Include(d => d.Governorate)
                                .AsNoTracking()
                                .FirstOrDefaultAsync(d => d.DistrictId == id);
            if (item == null) return NotFound();
            return View(item);
        }

        // =========================
        // GET: /Districts/Delete/5
        // =========================
        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            var item = await _db.Districts
                                .Include(d => d.Governorate)
                                .AsNoTracking()
                                .FirstOrDefaultAsync(d => d.DistrictId == id);
            if (item == null) return NotFound();
            return View(item);
        }

        // =========================
        // POST: /Districts/Delete/5
        // =========================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var item = await _db.Districts.FindAsync(id);
            if (item == null) return NotFound();

            var oldValues = System.Text.Json.JsonSerializer.Serialize(new { item.DistrictName, item.GovernorateId, item.DistrictType });
            _db.Districts.Remove(item);
            await _db.SaveChangesAsync();

            await _activityLogger.LogAsync(UserActionType.Delete, "District", id, $"حذف حي/مركز: {item.DistrictName}", oldValues: oldValues);

            TempData["Ok"] = "تم حذف السجل.";
            return RedirectToAction(nameof(Index));
        }




        // ===================== حذف جميع الأحياء/المراكز =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            // نجيب كل السجلات من جدول الأحياء/المراكز
            var all = await _db.Districts.ToListAsync();

            if (all.Count == 0)
            {
                // رسالة بسيطة لو مفيش بيانات
                TempData["Warning"] = "لا توجد أحياء/مراكز للحذف.";
                return RedirectToAction(nameof(Index));
            }

            _db.Districts.RemoveRange(all);   // حذف جماعي
            await _db.SaveChangesAsync();     // حفظ التعديلات في قاعدة البيانات

            TempData["Success"] = $"تم حذف جميع الأحياء/المراكز ({all.Count}) بنجاح.";
            return RedirectToAction(nameof(Index));
        }



        // ===================== حذف الأحياء/المراكز المحددة من الجدول =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedIds)
        {
            // لو المستخدم مدخلش أي صفوف
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["Warning"] = "من فضلك اختر على الأقل حي/مركز واحد للحذف.";
                return RedirectToAction(nameof(Index));
            }

            // نحول النص "1,5,7" إلى List<int> [1,5,7]
            var ids = selectedIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var id) ? (int?)id : null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();

            if (ids.Count == 0)
            {
                TempData["Warning"] = "لم يتم التعرف على أي أكواد صحيحة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            // نجيب السجلات المطابقة من قاعدة البيانات
            var districts = await _db.Districts
                .Where(d => ids.Contains(d.DistrictId))
                .ToListAsync();

            if (districts.Count == 0)
            {
                TempData["Warning"] = "لم يتم العثور على الأحياء/المراكز المحددة.";
                return RedirectToAction(nameof(Index));
            }

            _db.Districts.RemoveRange(districts); // حذف السجلات
            await _db.SaveChangesAsync();

            TempData["Success"] = $"تم حذف {districts.Count} حي/مركز بنجاح.";
            return RedirectToAction(nameof(Index));
        }



        // ===================== تصدير الأحياء/المراكز إلى ملف CSV =====================
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? governorateId = null,
            byte? type = null,
            int? fromCode = null,
            int? toCode = null,
            string? format = "excel"   // excel أو csv من الواجهة
        )
        {
            // نبدأ بكويري على جدول الأحياء/المراكز مع المحافظة
            var query = _db.Districts
                .Include(d => d.Governorate)
                .AsNoTracking()
                .AsQueryable();

            // -------- تطبيق البحث (نفس منطق Index الحالي تقريباً) --------
            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();
                switch (searchBy?.ToLower())
                {
                    case "id":
                        if (int.TryParse(search, out var idVal))
                            query = query.Where(d => d.DistrictId == idVal);
                        else
                            query = query.Where(d => false); // لو المستخدم كتب نص مش رقم
                        break;

                    default: // الاسم (وأي حالة أخرى)
                        query = query.Where(d => d.DistrictName.Contains(search));
                        break;
                }
            }

            // -------- فلتر المحافظة --------
            if (governorateId.HasValue && governorateId.Value > 0)
            {
                query = query.Where(d => d.GovernorateId == governorateId.Value);
            }

            // -------- فلتر النوع (حي/مركز) --------
            if (type.HasValue)
            {
                query = query.Where(d => d.DistrictType == type.Value);
            }

            // -------- فلتر من كود / إلى كود --------
            if (fromCode.HasValue)
                query = query.Where(d => d.DistrictId >= fromCode.Value);

            if (toCode.HasValue)
                query = query.Where(d => d.DistrictId <= toCode.Value);

            // -------- فلتر الفترة الزمنية (تاريخ الإنشاء) --------
            if (useDateRange)
            {
                if (fromDate.HasValue)
                    query = query.Where(d => d.CreatedAt.HasValue && d.CreatedAt.Value >= fromDate.Value);

                if (toDate.HasValue)
                    query = query.Where(d => d.CreatedAt.HasValue && d.CreatedAt.Value <= toDate.Value);
            }

            // -------- الترتيب --------
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            query = (sort?.ToLower()) switch
            {
                "id" => desc ? query.OrderByDescending(d => d.DistrictId) : query.OrderBy(d => d.DistrictId),
                "gov" => desc ? query.OrderByDescending(d => d.Governorate!.GovernorateName)
                                   : query.OrderBy(d => d.Governorate!.GovernorateName),
                "type" => desc ? query.OrderByDescending(d => d.DistrictType) : query.OrderBy(d => d.DistrictType),
                "isactive" => desc ? query.OrderByDescending(d => d.IsActive) : query.OrderBy(d => d.IsActive),
                "created" => desc ? query.OrderByDescending(d => d.CreatedAt) : query.OrderBy(d => d.CreatedAt),
                "updated" => desc ? query.OrderByDescending(d => d.UpdatedAt) : query.OrderBy(d => d.UpdatedAt),
                _ => desc ? query.OrderByDescending(d => d.DistrictName) : query.OrderBy(d => d.DistrictName),
            };

            var list = await query.ToListAsync();
            var fmt = (format ?? "excel").Trim().ToLowerInvariant();

            // -------- CSV --------
            if (fmt == "csv")
            {
                string Csv(string? value)
                {
                    if (string.IsNullOrEmpty(value)) return "";
                    return "\"" + value.Replace("\"", "\"\"") + "\"";
                }

                var sb = new StringBuilder();
                sb.AppendLine("DistrictId,DistrictName,GovernorateName,DistrictType,IsActive,CreatedAt,UpdatedAt");

                foreach (var d in list)
                {
                    var typeName = d.DistrictType == 1 ? "مركز" : d.DistrictType == 0 ? "حي" : "";
                    var activeName = d.IsActive ? "نشط" : "موقوف";
                    var created = d.CreatedAt.HasValue ? d.CreatedAt.Value.ToString("yyyy-MM-dd HH:mm") : "";
                    var updated = d.UpdatedAt.HasValue ? d.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm") : "";

                    sb.AppendLine(string.Join(",", new[]
                    {
                        d.DistrictId.ToString(),
                        Csv(d.DistrictName),
                        Csv(d.Governorate?.GovernorateName),
                        Csv(typeName),
                        Csv(activeName),
                        Csv(created),
                        Csv(updated)
                    }));
                }

                var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
                var bytes = encoding.GetBytes(sb.ToString());
                return File(bytes, "text/csv; charset=utf-8", $"Districts_{DateTime.Now:yyyyMMdd_HHmm}.csv");
            }

            // -------- Excel (.xlsx) --------
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("الأحياء والمراكز");

            ws.Cell(1, 1).Value = "كود الحي/المركز";
            ws.Cell(1, 2).Value = "اسم الحي/المركز";
            ws.Cell(1, 3).Value = "المحافظة";
            ws.Cell(1, 4).Value = "النوع";
            ws.Cell(1, 5).Value = "الحالة";
            ws.Cell(1, 6).Value = "تاريخ الإنشاء";
            ws.Cell(1, 7).Value = "آخر تعديل";

            var header = ws.Range(1, 1, 1, 7);
            header.Style.Font.Bold = true;
            header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            int row = 2;
            foreach (var d in list)
            {
                var typeName = d.DistrictType == 1 ? "مركز" : d.DistrictType == 0 ? "حي" : "";
                ws.Cell(row, 1).Value = d.DistrictId;
                ws.Cell(row, 2).Value = d.DistrictName ?? "";
                ws.Cell(row, 3).Value = d.Governorate?.GovernorateName ?? "";
                ws.Cell(row, 4).Value = typeName;
                ws.Cell(row, 5).Value = d.IsActive ? "نشط" : "موقوف";
                ws.Cell(row, 6).Value = d.CreatedAt.HasValue ? d.CreatedAt.Value.ToString("yyyy-MM-dd HH:mm") : "";
                ws.Cell(row, 7).Value = d.UpdatedAt.HasValue ? d.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm") : "";
                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            stream.Position = 0;
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Districts_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }





        // =========================
        // دوال تحميل القوائم المنسدلة
        // =========================



        // قوائم فورم Create/Edit
        private async Task LoadFormLookupsAsync(int? governorateId = null, byte? districtType = null)
        {
            ViewBag.GovernorateId = new SelectList(
                await _db.Governorates.AsNoTracking()
                                       .OrderBy(g => g.GovernorateName)
                                       .ToListAsync(),
                "GovernorateId",
                "GovernorateName",
                governorateId
            );

            ViewBag.DistrictType = new SelectList(
                new[]
                {
                    new { Value = (byte?)0, Text = "حي"   },
                    new { Value = (byte?)1, Text = "مركز" }
                },
                "Value",
                "Text",
                districtType
            );
        }
    }
}
