using ERP.Data;
using ERP.Filters;
using ERP.Infrastructure;
using ERP.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ERP.Controllers
{
    [RequirePermission("Customers.Edit")]
    public class CustomerCollectorsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IUserActivityLogger _activityLogger;

        public CustomerCollectorsController(AppDbContext context, IUserActivityLogger activityLogger)
        {
            _context = context;
            _activityLogger = activityLogger;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            await PopulateLookupsAsync();

            var rows = await _context.CustomerCollectors
                .AsNoTracking()
                .Include(x => x.Customer)
                .Include(x => x.User)
                .OrderBy(x => x.User.UserName)
                .ThenBy(x => x.Customer.CustomerName)
                .ToListAsync();

            return View(rows);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(int customerId, int userId)
        {
            if (customerId <= 0 || userId <= 0)
            {
                TempData["Error"] = "اختر العميل والموزع أولاً.";
                return RedirectToAction(nameof(Index));
            }

            var customer = await _context.Customers
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.CustomerId == customerId && x.IsActive);

            if (customer == null)
            {
                TempData["Error"] = "العميل غير موجود أو غير نشط.";
                return RedirectToAction(nameof(Index));
            }

            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == userId && x.IsActive);

            if (user == null)
            {
                TempData["Error"] = "الموزع غير موجود أو غير نشط.";
                return RedirectToAction(nameof(Index));
            }

            if (!string.Equals(user.PortalRole, "Collector", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "المستخدم المختار ليس موزعًا.";
                return RedirectToAction(nameof(Index));
            }

            var exists = await _context.CustomerCollectors
                .AnyAsync(x => x.CustomerId == customerId && x.UserId == userId);

            if (exists)
            {
                TempData["Error"] = "هذا الربط موجود بالفعل.";
                return RedirectToAction(nameof(Index));
            }

            var row = new CustomerCollector
            {
                CustomerId = customerId,
                UserId = userId,
                CreatedAt = DateTime.Now,
                CreatedBy = User?.Identity?.Name ?? "SYSTEM"
            };

            _context.CustomerCollectors.Add(row);
            await _context.SaveChangesAsync();

            await _activityLogger.LogAsync(
                UserActionType.Create,
                "CustomerCollector",
                row.Id,
                $"ربط العميل {customer.CustomerName} بالموزع {user.UserName}");

            TempData["Success"] = "تم ربط العميل بالموزع بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var row = await _context.CustomerCollectors
                .Include(x => x.Customer)
                .Include(x => x.User)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (row == null)
            {
                TempData["Error"] = "رابط العميل والموزع غير موجود.";
                return RedirectToAction(nameof(Index));
            }

            var description = $"فك ربط العميل {row.Customer?.CustomerName ?? row.CustomerId.ToString()} بالموزع {row.User?.UserName ?? row.UserId.ToString()}";

            _context.CustomerCollectors.Remove(row);
            await _context.SaveChangesAsync();

            await _activityLogger.LogAsync(UserActionType.Delete, "CustomerCollector", id, description);

            TempData["Success"] = "تم فك الربط بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        private async Task PopulateLookupsAsync()
        {
            var customers = await _context.Customers
                .AsNoTracking()
                .Where(x => x.IsActive && (x.PartyCategory == null || x.PartyCategory == "" || x.PartyCategory == "Customer" || x.PartyCategory == "عميل"))
                .OrderBy(x => x.CustomerName)
                .Select(x => new
                {
                    x.CustomerId,
                    Text = (x.CustomerName ?? "") + (string.IsNullOrWhiteSpace(x.ExternalCode) ? "" : $" - {x.ExternalCode}")
                })
                .ToListAsync();

            var collectors = await _context.Users
                .AsNoTracking()
                .Where(x => x.IsActive && x.PortalRole == "Collector")
                .OrderBy(x => x.UserName)
                .Select(x => new
                {
                    x.UserId,
                    Text = string.IsNullOrWhiteSpace(x.DisplayName) ? x.UserName : $"{x.DisplayName} - {x.UserName}"
                })
                .ToListAsync();

            ViewBag.Customers = new SelectList(customers, "CustomerId", "Text");
            ViewBag.Collectors = new SelectList(collectors, "UserId", "Text");
        }
    }
}
