using ERP.Data;
using ERP.Filters;
using ERP.Security;
using ERP.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ERP.Controllers
{
    /// <summary>
    /// لوحة واحدة: مبيعاتي الشخصية (صلاحية Dashboard.Sales). توجيه /Dashboard إلى نفس اللوحة.
    /// </summary>
    public class DashboardController : Controller
    {
        private readonly AppDbContext _context;

        public DashboardController(AppDbContext context)
        {
            _context = context;
        }

        private int? GetCurrentUserId()
        {
            var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(idStr, out var id) ? id : null;
        }

        /// <summary>
        /// مبيعاتي الشخصية — تتطلب صلاحية Dashboard.Sales. بدون كاش لضمان بيانات محدثة.
        /// </summary>
        [HttpGet]
        [RequirePermission("Dashboard.Sales")]
        [ResponseCache(NoStore = true, Duration = 0, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Sales(DateTime? fromDate, DateTime? toDate)
        {
            var today = DateTime.Today;
            var from = fromDate?.Date ?? new DateTime(today.Year, today.Month, 1);
            var to = toDate?.Date ?? today;
            if (from > to) to = from;

            var vm = new DashboardViewModel
            {
                Level = "sales",
                LevelName = "مبيعاتي الشخصية",
                UserDisplayName = User.Identity?.Name ?? "",
                FromDate = from,
                ToDate = to
            };

            var userId = GetCurrentUserId();
            var currentUserName = User.Identity?.Name ?? "";
            var displayName = User.FindFirst("DisplayName")?.Value ?? "";
            var creatorNames = new[] { currentUserName, displayName }.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
            if (!userId.HasValue && creatorNames.Count == 0)
                return View("Index", vm);

            // عملاء المندوب + عدد الأصناف (للعرض)
            vm.CustomersCount = await _context.Customers
                .Where(c => c.IsActive && c.UserId == userId)
                .CountAsync();
            vm.ProductsCount = await _context.Products.CountAsync(p => p.IsActive);

            // فواتير البيع: إما لعملاء المندوب أو التي أنشأها بنفسه — بدون كاش، دائماً من قاعدة البيانات
            var myCustomerIds = userId.HasValue
                ? await _context.Customers.Where(c => c.UserId == userId).Select(c => c.CustomerId).ToListAsync()
                : new List<int>();

            var salesBase = _context.SalesInvoices
                .Where(si => si.IsPosted &&
                    (myCustomerIds.Contains(si.CustomerId) || (creatorNames.Any() && creatorNames.Contains(si.CreatedBy))));

            var periodEnd = to.AddDays(1);
            vm.SalesInvoicesTodayCount = await salesBase
                .Where(si => si.SIDate.Date == today)
                .CountAsync();
            vm.SalesInvoicesTodayTotal = await salesBase
                .Where(si => si.SIDate.Date == today)
                .SumAsync(si => si.NetTotal);

            vm.SalesInvoicesMonthCount = await salesBase
                .Where(si => si.SIDate >= from && si.SIDate < periodEnd)
                .CountAsync();
            vm.SalesInvoicesMonthTotal = await salesBase
                .Where(si => si.SIDate >= from && si.SIDate < periodEnd)
                .SumAsync(si => si.NetTotal);

            // عدد أصناف المبيعات (أصناف متميزة مباعة في الفترة)
            var periodSalesIds = await salesBase.Where(si => si.SIDate >= from && si.SIDate < periodEnd).Select(si => si.SIId).ToListAsync();
            if (periodSalesIds.Any())
            {
                vm.SalesProductsSoldCount = await _context.SalesInvoiceLines
                    .Where(l => periodSalesIds.Contains(l.SIId))
                    .Select(l => l.ProdId)
                    .Distinct()
                    .CountAsync();
            }

            // بيانات المخطط: مبيعات كل يوم في الفترة [from, to]
            for (var d = from; d <= to; d = d.AddDays(1))
            {
                var dayTotal = await salesBase.Where(si => si.SIDate.Date == d).SumAsync(si => si.NetTotal);
                vm.ChartData.Add(new DashboardChartPoint { Date = d.ToString("yyyy-MM-dd"), Amount = dayTotal });
            }

            // آخر فواتير المندوب في الفترة (التي أنشأها أو لعملائه) — تشمل المسودة والمرحّلة
            vm.RecentItems = await _context.SalesInvoices
                .Where(si => (myCustomerIds.Contains(si.CustomerId) || (creatorNames.Any() && creatorNames.Contains(si.CreatedBy)))
                    && si.SIDate >= from && si.SIDate < periodEnd)
                .Include(si => si.Customer)
                .OrderByDescending(si => si.SIDate).ThenByDescending(si => si.SITime)
                .Take(8)
                .Select(si => new DashboardRecentItem
                {
                    Type = si.IsPosted ? "فاتورة بيع" : "فاتورة بيع (مسودة)",
                    PartyName = si.Customer!.CustomerName,
                    Amount = si.NetTotal,
                    Date = si.SIDate
                })
                .ToListAsync();

            return View("Index", vm);
        }

        /// <summary>
        /// /Dashboard و /Dashboard/Index → نفس لوحة مبيعاتي الشخصية.
        /// </summary>
        [HttpGet]
        [RequirePermission("Dashboard.Sales")]
        public IActionResult Index()
        {
            return RedirectToAction(nameof(Sales));
        }
    }
}
