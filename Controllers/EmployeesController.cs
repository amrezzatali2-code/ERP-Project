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
    [RequirePermission("Employees.Index")]
    public class EmployeesController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IUserActivityLogger _activityLogger;

        public EmployeesController(AppDbContext db, IUserActivityLogger activityLogger)
        {
            _db = db;
            _activityLogger = activityLogger;
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "name",
            string? sort = "FullName",
            string? dir = "asc",
            int page = 1,
            int pageSize = 25)
        {
            IQueryable<Employee> q = _db.Employees.AsNoTracking()
                .Include(e => e.User)
                .Include(e => e.Department)
                .Include(e => e.Job);
            var s = (search ?? "").Trim();
            var sb = (searchBy ?? "name").Trim().ToLower();
            var so = (sort ?? "FullName").Trim();
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(s))
            {
                if (sb == "id" && int.TryParse(s, out int idVal))
                    q = q.Where(x => x.Id == idVal);
                else if (sb == "all")
                    q = q.Where(x =>
                        (x.FullName != null && x.FullName.Contains(s)) ||
                        (x.Code != null && x.Code.Contains(s)) ||
                        (x.Department != null && x.Department.Name != null && x.Department.Name.Contains(s)) ||
                        (x.Job != null && x.Job.Name != null && x.Job.Name.Contains(s)) ||
                        x.Id.ToString().Contains(s));
                else if (sb == "code")
                    q = q.Where(x => x.Code != null && x.Code.Contains(s));
                else if (sb == "department")
                    q = q.Where(x => x.Department != null && x.Department.Name != null && x.Department.Name.Contains(s));
                else
                    q = q.Where(x => x.FullName != null && x.FullName.Contains(s));
            }

            q = so switch
            {
                "id" => desc ? q.OrderByDescending(x => x.Id) : q.OrderBy(x => x.Id),
                "code" => desc ? q.OrderByDescending(x => x.Code) : q.OrderBy(x => x.Code),
                "department" => desc ? q.OrderByDescending(x => x.Department != null ? x.Department.Name : "") : q.OrderBy(x => x.Department != null ? x.Department.Name : ""),
                "jobtitle" => desc ? q.OrderByDescending(x => x.Job != null ? x.Job.Name : "") : q.OrderBy(x => x.Job != null ? x.Job.Name : ""),
                "hiredate" => desc ? q.OrderByDescending(x => x.HireDate) : q.OrderBy(x => x.HireDate),
                _ => desc ? q.OrderByDescending(x => x.FullName) : q.OrderBy(x => x.FullName),
            };

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 25;
            int total = await q.CountAsync();
            var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            // المبيعات والبونص من فواتير البيع المرحّلة حسب المستخدم المرتبط بالموظف (شهر الحالي)
            var salesByEmployeeId = new Dictionary<int, decimal>();
            var bonusByEmployeeId = new Dictionary<int, decimal>();
            var fromDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var toDate = fromDate.AddMonths(1);
            var userIds = items.Where(e => e.UserId.HasValue).Select(e => e.UserId!.Value).Distinct().ToList();
            if (userIds.Any())
            {
                var users = await _db.Users.AsNoTracking()
                    .Where(u => userIds.Contains(u.UserId))
                    .Select(u => new { u.UserId, u.DisplayName, u.UserName })
                    .ToListAsync();
                var namesByUserId = users.Select(u => new { u.UserId, Names = new[] { u.DisplayName, u.UserName }.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList() })
                    .ToDictionary(x => x.UserId, x => x.Names);

                var salesByCreatedBy = await _db.SalesInvoices.AsNoTracking()
                    .Where(si => si.IsPosted && si.SIDate >= fromDate && si.SIDate < toDate && si.CreatedBy != null)
                    .GroupBy(si => si.CreatedBy!)
                    .Select(g => new { CreatedBy = g.Key, Total = g.Sum(si => si.NetTotal) })
                    .ToListAsync();
                var salesDict = salesByCreatedBy.ToDictionary(x => x.CreatedBy, x => x.Total, StringComparer.OrdinalIgnoreCase);

                var bonusByCreatedBy = await _db.SalesInvoiceLines.AsNoTracking()
                    .Where(sil => sil.SalesInvoice != null && sil.SalesInvoice.IsPosted
                        && sil.SalesInvoice.SIDate >= fromDate && sil.SalesInvoice.SIDate < toDate
                        && sil.SalesInvoice.CreatedBy != null
                        && sil.Product != null && sil.Product.ProductBonusGroupId != null)
                    .GroupBy(sil => sil.SalesInvoice!.CreatedBy!)
                    .Select(g => new { CreatedBy = g.Key, Total = g.Sum(sil => sil.Qty * (sil.Product != null && sil.Product.ProductBonusGroup != null ? sil.Product.ProductBonusGroup.BonusAmount : 0m)) })
                    .ToListAsync();
                var bonusDict = bonusByCreatedBy.ToDictionary(x => x.CreatedBy, x => x.Total, StringComparer.OrdinalIgnoreCase);

                foreach (var emp in items)
                {
                    salesByEmployeeId[emp.Id] = 0;
                    bonusByEmployeeId[emp.Id] = 0;
                    if (emp.UserId.HasValue && namesByUserId.TryGetValue(emp.UserId.Value, out var names))
                    {
                        foreach (var name in names)
                        {
                            if (salesDict.TryGetValue(name, out var sales)) salesByEmployeeId[emp.Id] += sales;
                            if (bonusDict.TryGetValue(name, out var bonus)) bonusByEmployeeId[emp.Id] += bonus;
                        }
                    }
                }
            }
            else
            {
                foreach (var emp in items)
                {
                    salesByEmployeeId[emp.Id] = 0;
                    bonusByEmployeeId[emp.Id] = 0;
                }
            }

            ViewBag.SalesByEmployeeId = salesByEmployeeId;
            ViewBag.BonusByEmployeeId = bonusByEmployeeId;
            ViewBag.SalesBonusPeriod = $"{fromDate:yyyy-MM}";

            var model = new PagedResult<Employee>(items, page, pageSize, total)
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
                new SelectListItem("القسم", "department", sb == "department"),
                new SelectListItem("الرقم", "id", sb == "id"),
                new SelectListItem("الكل", "all", sb == "all"),
            };
            return View(model);
        }

        [HttpGet]
        [RequirePermission("Employees.Create")]
        public async Task<IActionResult> Create()
        {
            await PopulateUserListAsync(null);
            await PopulateDepartmentsAndJobsAsync(null, null);
            return View(new Employee());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Employees.Create")]
        public async Task<IActionResult> Create([Bind("FullName,NationalId,BirthDate,HireDate,DepartmentId,JobId,Phone1,Phone2,Email,Address,BaseSalary,IsActive,Notes,UserId")] Employee entity)
        {
            if (!ModelState.IsValid)
            {
                await PopulateUserListAsync(entity.UserId);
                await PopulateDepartmentsAndJobsAsync(entity.DepartmentId, entity.JobId);
                return View(entity);
            }
            entity.CreatedAt = DateTime.UtcNow;
            entity.UpdatedAt = null;
            _db.Employees.Add(entity);
            await _db.SaveChangesAsync();
            entity.Code = entity.Id.ToString();
            await _db.SaveChangesAsync();
            TempData["Ok"] = "تمت إضافة الموظف بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [RequirePermission("Employees.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            var entity = await _db.Employees.FindAsync(id);
            if (entity == null) return NotFound();
            await PopulateUserListAsync(entity.UserId);
            await PopulateDepartmentsAndJobsAsync(entity.DepartmentId, entity.JobId);
            return View(entity);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Employees.Edit")]
        public async Task<IActionResult> Edit(int id, [Bind("Id,FullName,NationalId,BirthDate,HireDate,DepartmentId,JobId,Phone1,Phone2,Email,Address,BaseSalary,IsActive,Notes,UserId")] Employee entity)
        {
            if (id != entity.Id) return NotFound();
            if (!ModelState.IsValid)
            {
                await PopulateUserListAsync(entity.UserId);
                await PopulateDepartmentsAndJobsAsync(entity.DepartmentId, entity.JobId);
                return View(entity);
            }
            var existing = await _db.Employees.FindAsync(id);
            if (existing == null) return NotFound();
            existing.FullName = entity.FullName;
            existing.NationalId = entity.NationalId;
            existing.BirthDate = entity.BirthDate;
            existing.HireDate = entity.HireDate;
            existing.DepartmentId = entity.DepartmentId;
            existing.JobId = entity.JobId;
            existing.Phone1 = entity.Phone1;
            existing.Phone2 = entity.Phone2;
            existing.Email = entity.Email;
            existing.Address = entity.Address;
            existing.BaseSalary = entity.BaseSalary;
            existing.IsActive = entity.IsActive;
            existing.Notes = entity.Notes;
            existing.UserId = entity.UserId;
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await _activityLogger.LogAsync(UserActionType.Edit, "Employee", id, $"تعديل موظف: {entity.FullName}");
            TempData["Ok"] = "تم تعديل بيانات الموظف.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [RequirePermission("Employees.Show")]
        public async Task<IActionResult> Show(int id)
        {
            var entity = await _db.Employees.AsNoTracking()
                .Include(e => e.User)
                .Include(e => e.Department)
                .Include(e => e.Job)
                .FirstOrDefaultAsync(e => e.Id == id);
            if (entity == null) return NotFound();
            return View(entity);
        }

        [HttpGet]
        [RequirePermission("Employees.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var entity = await _db.Employees.AsNoTracking()
                .Include(e => e.Department)
                .Include(e => e.Job)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (entity == null) return NotFound();
            return View(entity);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [RequirePermission("Employees.Delete")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var entity = await _db.Employees.FindAsync(id);
            if (entity == null) return NotFound();
            _db.Employees.Remove(entity);
            await _db.SaveChangesAsync();
            await _activityLogger.LogAsync(UserActionType.Delete, "Employee", id, $"حذف موظف: {entity.FullName}");
            TempData["Ok"] = "تم الحذف.";
            return RedirectToAction(nameof(Index));
        }

        private async Task PopulateUserListAsync(int? selectedUserId)
        {
            var users = await _db.Users
                .AsNoTracking()
                .Where(u => u.IsActive)
                .OrderBy(u => u.DisplayName)
                .Select(u => new { u.UserId, Display = u.DisplayName ?? u.UserName })
                .ToListAsync();
            ViewBag.UserId = new SelectList(users, "UserId", "Display", selectedUserId);
        }

        private async Task PopulateDepartmentsAndJobsAsync(int? selectedDepartmentId, int? selectedJobId)
        {
            var depts = await _db.Departments
                .AsNoTracking()
                .Where(d => d.IsActive)
                .OrderBy(d => d.SortOrder).ThenBy(d => d.Name)
                .Select(d => new { d.Id, d.Name })
                .ToListAsync();
            ViewBag.DepartmentId = new SelectList(depts, "Id", "Name", selectedDepartmentId);

            var jobs = await _db.Jobs
                .AsNoTracking()
                .Where(j => j.IsActive)
                .OrderBy(j => j.SortOrder).ThenBy(j => j.Name)
                .Select(j => new { j.Id, j.Name })
                .ToListAsync();
            ViewBag.JobId = new SelectList(jobs, "Id", "Name", selectedJobId);
        }
    }
}
