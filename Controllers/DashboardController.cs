using ERP.Data;
using ERP.Filters;
using ERP.Security;
using ERP.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Linq;

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
        /// قيم تطابق <c>CreatedBy</c> في الفواتير مع المستخدم (اسم الدخول، الاسم المعروض، البريد من الجدول).
        /// يُقلّل حالات «لا مبيعات» رغم وجود فواتير بسبب اختلاف النص عن الـ Claims فقط.
        /// </summary>
        private async Task<List<string>> GetSalesCreatorNameCandidatesAsync(int? userId)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string? s)
            {
                if (!string.IsNullOrWhiteSpace(s))
                    set.Add(s.Trim());
            }

            Add(User.Identity?.Name);
            Add(User.FindFirst("DisplayName")?.Value);

            if (userId.HasValue)
            {
                var row = await _context.Users.AsNoTracking()
                    .Where(u => u.UserId == userId.Value)
                    .Select(u => new { u.UserName, u.DisplayName, u.Email })
                    .FirstOrDefaultAsync();
                if (row != null)
                {
                    Add(row.UserName);
                    Add(row.DisplayName);
                    Add(row.Email);
                }
            }

            return set.ToList();
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
            var creatorNames = await GetSalesCreatorNameCandidatesAsync(userId);
            if (!userId.HasValue && creatorNames.Count == 0)
                return View(vm);

            // فواتير البيع: إما لعملاء المندوب أو التي أنشأها بنفسه — بدون كاش، دائماً من قاعدة البيانات
            var myCustomerIds = userId.HasValue
                ? await _context.Customers.Where(c => c.UserId == userId).Select(c => c.CustomerId).ToListAsync()
                : new List<int>();

            // الإجماليات والمخطط: فواتير مرحّلة فقط (متسق مع نص البطاقات في الواجهة)
            var salesBase = _context.SalesInvoices
                .Where(si => si.IsPosted &&
                    (myCustomerIds.Contains(si.CustomerId) ||
                     (creatorNames.Count > 0 && si.CreatedBy != null &&
                      creatorNames.Contains(si.CreatedBy))));

            var periodEnd = to.AddDays(1);

            // عملاء مربوطون بالمستخدم الحالي مباشرة (Customer.UserId) لعرضهم في النافذة
            if (userId.HasValue)
            {
                var linked = await _context.Customers.AsNoTracking()
                    .Where(c => c.IsActive && c.UserId == userId)
                    .OrderBy(c => c.CustomerName)
                    .Select(c => new { c.CustomerId, c.CustomerName, c.Phone1 })
                    .ToListAsync();

                vm.LinkedCustomersDetail = linked.Select(c => new DashboardLinkedCustomerRow
                {
                    CustomerId = c.CustomerId,
                    CustomerName = c.CustomerName,
                    Phone = c.Phone1
                }).ToList();

                vm.CustomersCount = vm.LinkedCustomersDetail.Count;
            }

            // مبيعات الفترة حسب المنطقة (من جدول المناطق أو حقل المنطقة كنص)
            var periodInvoices = await salesBase
                .Where(si => si.SIDate >= from && si.SIDate < periodEnd)
                .Select(si => new { si.SIId, si.NetTotal, si.CustomerId })
                .ToListAsync();

            if (periodInvoices.Count > 0)
            {
                var custIds = periodInvoices.Select(x => x.CustomerId).Distinct().ToList();
                var custRows = await _context.Customers.AsNoTracking()
                    .Where(c => custIds.Contains(c.CustomerId))
                    .Select(c => new { c.CustomerId, c.AreaId, c.RegionName, AreaName = c.Area != null ? c.Area.AreaName : null })
                    .ToListAsync();
                var cmap = custRows.ToDictionary(x => x.CustomerId, x => x);

                vm.RegionSalesRows = periodInvoices
                    .GroupBy(i =>
                    {
                        cmap.TryGetValue(i.CustomerId, out var c);
                        var label = !string.IsNullOrWhiteSpace(c?.AreaName)
                            ? c!.AreaName!.Trim()
                            : (!string.IsNullOrWhiteSpace(c?.RegionName) ? c.RegionName!.Trim() : "بدون منطقة");
                        return (Name: label, AreaId: c?.AreaId);
                    })
                    .Select(g => new DashboardRegionSalesRow
                    {
                        AreaId = g.Key.AreaId,
                        AreaName = g.Key.Name,
                        InvoiceCount = g.Select(x => x.SIId).Distinct().Count(),
                        SalesTotal = g.Sum(x => x.NetTotal)
                    })
                    .OrderByDescending(r => r.SalesTotal)
                    .ThenBy(r => r.AreaName)
                    .ToList();
                vm.RegionsCount = vm.RegionSalesRows.Count;
            }
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
                .Where(si => (myCustomerIds.Contains(si.CustomerId) ||
                    (creatorNames.Count > 0 && si.CreatedBy != null && creatorNames.Contains(si.CreatedBy)))
                    && si.SIDate >= from && si.SIDate < periodEnd)
                .Include(si => si.Customer)
                .OrderByDescending(si => si.SIDate).ThenByDescending(si => si.SITime)
                .Take(8)
                .Select(si => new DashboardRecentItem
                {
                    Type = si.IsPosted ? "فاتورة بيع" : "فاتورة بيع (غير مرحلة)",
                    PartyName = si.Customer!.CustomerName,
                    Amount = si.NetTotal,
                    Date = si.SIDate
                })
                .ToListAsync();

            return View(vm);
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
