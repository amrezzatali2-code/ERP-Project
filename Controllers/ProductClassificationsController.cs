using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ERP.Data;
using ERP.Filters;
using ERP.Models;
using ERP.Infrastructure;

namespace ERP.Controllers
{
    [RequirePermission("ProductClassifications.Index")]
    public class ProductClassificationsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IUserActivityLogger _activityLogger;

        public ProductClassificationsController(AppDbContext db, IUserActivityLogger activityLogger)
        {
            _db = db;
            _activityLogger = activityLogger;
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "name",
            string? sort = "SortOrder",
            string? dir = "asc",
            int page = 1,
            int pageSize = 25)
        {
            var q = _db.ProductClassifications.AsNoTracking();
            var s = (search ?? "").Trim();
            var sb = (searchBy ?? "name").Trim().ToLower();
            var so = (sort ?? "SortOrder").Trim();
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(s))
            {
                if (sb == "id" && int.TryParse(s, out int idVal))
                    q = q.Where(x => x.Id == idVal);
                else if (sb == "all")
                    q = q.Where(x => (x.Name != null && x.Name.Contains(s)) || (x.Code != null && x.Code.Contains(s)) || x.Id.ToString().Contains(s));
                else
                    q = q.Where(x => x.Name != null && x.Name.Contains(s));
            }

            q = so switch
            {
                "id" => desc ? q.OrderByDescending(x => x.Id) : q.OrderBy(x => x.Id),
                "name" => desc ? q.OrderByDescending(x => x.Name) : q.OrderBy(x => x.Name),
                "code" => desc ? q.OrderByDescending(x => x.Code) : q.OrderBy(x => x.Code),
                _ => desc ? q.OrderByDescending(x => x.SortOrder).ThenByDescending(x => x.Name) : q.OrderBy(x => x.SortOrder).ThenBy(x => x.Name),
            };

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 25;
            int total = await q.CountAsync();
            var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            var model = new PagedResult<ProductClassification>(items, page, pageSize, total)
            {
                Search = s,
                SearchBy = sb,
                SortColumn = so,
                SortDescending = desc
            };

            ViewBag.Search = s;
            ViewBag.SearchBy = sb;
            ViewBag.Sort = so;
            ViewBag.Dir = desc ? "desc" : "asc";
            ViewBag.Total = total;
            ViewBag.SearchOptions = new[]
            {
                new SelectListItem("الاسم", "name", sb == "name"),
                new SelectListItem("الكود", "code", sb == "code"),
                new SelectListItem("الرقم", "id", sb == "id"),
                new SelectListItem("الكل", "all", sb == "all"),
            };
            return View(model);
        }

        [HttpGet]
        [RequirePermission("ProductClassifications.Create")]
        public IActionResult Create() => View(new ProductClassification());

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("ProductClassifications.Create")]
        public async Task<IActionResult> Create([Bind("Name,IsActive")] ProductClassification entity)
        {
            if (!ModelState.IsValid) return View(entity);
            entity.CreatedAt = DateTime.UtcNow;
            entity.UpdatedAt = null;
            _db.ProductClassifications.Add(entity);
            await _db.SaveChangesAsync();
            entity.Code = entity.Id.ToString();
            await _db.SaveChangesAsync();
            TempData["Ok"] = "تمت إضافة التصنيف بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var entity = await _db.ProductClassifications.FindAsync(id);
            if (entity == null) return NotFound();
            return View(entity);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name,IsActive")] ProductClassification entity)
        {
            if (id != entity.Id) return NotFound();
            if (!ModelState.IsValid) return View(entity);
            var existing = await _db.ProductClassifications.FindAsync(id);
            if (existing == null) return NotFound();
            existing.Name = entity.Name;
            existing.IsActive = entity.IsActive;
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await _activityLogger.LogAsync(UserActionType.Edit, "ProductClassification", id, $"تعديل تصنيف: {entity.Name}");
            TempData["Ok"] = "تم تعديل التصنيف.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [RequirePermission("ProductClassifications.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity = await _db.ProductClassifications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (entity == null) return NotFound();
            return View(entity);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [RequirePermission("ProductClassifications.Delete")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var entity = await _db.ProductClassifications.FindAsync(id);
            if (entity == null) return NotFound();
            try
            {
                _db.ProductClassifications.Remove(entity);
                await _db.SaveChangesAsync();
                await _activityLogger.LogAsync(UserActionType.Delete, "ProductClassification", id, $"حذف تصنيف: {entity.Name}");
                TempData["Ok"] = "تم الحذف.";
            }
            catch (DbUpdateException)
            {
                TempData["Err"] = "لا يمكن الحذف لوجود أصناف مرتبطة بهذا التصنيف.";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}
