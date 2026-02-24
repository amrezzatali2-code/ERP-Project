// Controllers/CitiesController.cs
using ERP.Data;
using ERP.Infrastructure;            // PagedResult + UserActivityLogger
using ERP.Models;                    // City, UserActionType
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;        // للـ ApplySearchSort
using System.Threading.Tasks;

namespace ERP.Controllers
{
    public class CitiesController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IUserActivityLogger _activityLogger;

        public CitiesController(AppDbContext db, IUserActivityLogger activityLogger)
        {
            _db = db;
            _activityLogger = activityLogger;
        }

        // دالة مساعدة لتحميل قائمة المحافظات (للفورم Create/Edit)
        private async Task LoadGovernoratesAsync(int? selectedId = null)
        {
            ViewBag.GovernorateId = new SelectList(
                await _db.Governorates.AsNoTracking()
                                       .OrderBy(g => g.GovernorateName)
                                       .ToListAsync(),
                "GovernorateId", "GovernorateName", selectedId);
        }

        // ===========================
        // GET: /Cities/Index
        // قائمة المدن/المراكز مع بحث/ترتيب/فلترة بالمحافظة + ترقيم
        // (يرجع IEnumerable + ViewBag كما هو مستخدم في الـ View الحالي)
        // ===========================
        [HttpGet]
        public async Task<IActionResult> Index(
     string? search, string? searchBy = "name",
     string? sort = "name", string? dir = "asc",
     int? governorateId = null,
     int page = 1, int pageSize = 10)
        {
            // الاستعلام الأساسي + المحافظة
            var q = _db.Cities.AsNoTracking()
                              .Include(c => c.Governorate)
                              .AsQueryable();

            if (governorateId is > 0)
                q = q.Where(c => c.GovernorateId == governorateId);

            // البحث/الترتيب العام
            var stringFields = new Dictionary<string, Expression<Func<City, string?>>>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = c => c.CityName,
                ["gov"] = c => c.Governorate!.GovernorateName
            };
            var intFields = new Dictionary<string, Expression<Func<City, int>>>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = c => c.CityId
            };
            var orderFields = new Dictionary<string, Expression<Func<City, object>>>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = c => c.CityName!,
                ["id"] = c => c.CityId,
                ["gov"] = c => c.Governorate!.GovernorateName!
            };

            q = q.ApplySearchSort(search, searchBy, sort, dir,
                                  stringFields, intFields, orderFields,
                                  defaultSearchBy: "name",
                                  defaultSortBy: "name");

            // الترقيم
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;

            var total = await q.CountAsync();
            var items = await q.Skip((page - 1) * pageSize)
                               .Take(pageSize)
                               .ToListAsync();

            // قيم العرض
            ViewBag.Search = search;
            ViewBag.SearchBy = searchBy;
            ViewBag.Sort = sort;
            ViewBag.Dir = dir;

            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.Total = total;

            ViewBag.GovFilter = governorateId;
            ViewBag.GovernorateId = new SelectList(
                await _db.Governorates.AsNoTracking()
                                      .OrderBy(g => g.GovernorateName)
                                      .ToListAsync(),
                "GovernorateId", "GovernorateName", governorateId);

            return View(items);
        }


        // ===========================
        // GET: /Cities/Details/5
        // عرض تفاصيل مدينة واحدة
        // ===========================
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var city = await _db.Cities.AsNoTracking()
                                       .Include(c => c.Governorate)
                                       .FirstOrDefaultAsync(c => c.CityId == id);
            if (city == null) return NotFound();
            return View(city);
        }

        // ===========================
        // GET: /Cities/Create
        // فتح شاشة إضافة مدينة/مركز
        // ===========================
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            await LoadGovernoratesAsync();          // تحميل قائمة المحافظات للفورم
            return View(new City { IsActive = true });
        }

        // ===========================
        // POST: /Cities/Create
        // حفظ مدينة جديدة
        // ===========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(City model)
        {
            if (!ModelState.IsValid)
            {
                await LoadGovernoratesAsync(model.GovernorateId);
                return View(model);
            }

            // تتبع إنشاء/تعديل (لو الأعمدة موجودة)
            model.CreatedAt = model.CreatedAt == default ? DateTime.Now : model.CreatedAt;
            model.UpdatedAt = DateTime.Now;

            _db.Cities.Add(model);
            await _db.SaveChangesAsync();

            await _activityLogger.LogAsync(UserActionType.Create, "City", model.CityId, $"إنشاء مدينة: {model.CityName}");

            return RedirectToAction(nameof(Index));
        }

        // ===========================
        // GET: /Cities/Edit/5
        // فتح شاشة تعديل
        // ===========================
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var city = await _db.Cities.FindAsync(id);
            if (city == null) return NotFound();

            await LoadGovernoratesAsync(city.GovernorateId);
            return View(city);
        }

        // ===========================
        // POST: /Cities/Edit/5
        // حفظ التعديلات
        // ===========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, City model)
        {
            if (id != model.CityId) return BadRequest();

            if (!ModelState.IsValid)
            {
                await LoadGovernoratesAsync(model.GovernorateId);
                return View(model);
            }

            try
            {
                var existing = await _db.Cities.AsNoTracking().FirstOrDefaultAsync(c => c.CityId == id);
                var oldValues = existing != null ? System.Text.Json.JsonSerializer.Serialize(new { existing.CityName, existing.GovernorateId }) : null;
                model.UpdatedAt = DateTime.Now;     // تتبع التعديل (لو العمود موجود)
                _db.Update(model);
                await _db.SaveChangesAsync();

                var newValues = System.Text.Json.JsonSerializer.Serialize(new { model.CityName, model.GovernorateId });
                await _activityLogger.LogAsync(UserActionType.Edit, "City", id, $"تعديل مدينة: {model.CityName}", oldValues, newValues);
            }
            catch (DbUpdateConcurrencyException)
            {
                var exists = await _db.Cities.AnyAsync(c => c.CityId == id);
                if (!exists) return NotFound();
                throw;
            }

            return RedirectToAction(nameof(Index));
        }

        // ===========================
        // GET: /Cities/Delete/5
        // شاشة تأكيد الحذف
        // ===========================
        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            var city = await _db.Cities.AsNoTracking()
                                       .Include(c => c.Governorate)
                                       .FirstOrDefaultAsync(c => c.CityId == id);
            if (city == null) return NotFound();
            return View(city);
        }

        // ===========================
        // POST: /Cities/Delete/5
        // تنفيذ الحذف بعد التأكيد
        // ===========================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var city = await _db.Cities.FindAsync(id);
            if (city == null) return NotFound();

            var oldValues = System.Text.Json.JsonSerializer.Serialize(new { city.CityName, city.GovernorateId });
            _db.Cities.Remove(city);
            await _db.SaveChangesAsync();

            await _activityLogger.LogAsync(UserActionType.Delete, "City", id, $"حذف مدينة: {city.CityName}", oldValues: oldValues);

            return RedirectToAction(nameof(Index));
        }
    }
}
