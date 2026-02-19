using ERP.Data;
using ERP.Models;
using ERP.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ERP.Controllers
{
    /// <summary>
    /// لوحات التحكم بمستويات متعددة: مندوب مبيعات → مدير → مالك النظام
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

        private bool IsSalesRole()
        {
            return User.IsInRole("مندوب مبيعات") || User.IsInRole("مشرف مبيعات") ||
                   User.IsInRole("SalesRep") || User.IsInRole("SalesSupervisor");
        }

        private bool IsOwnerOrManager()
        {
            return User.IsInRole("مالك النظام") || User.IsInRole("Owner") ||
                   User.IsInRole("المدير العام") || User.IsInRole("GeneralManager") ||
                   User.IsInRole("مدير المبيعات") || User.IsInRole("مدير الشئون المالية") ||
                   User.IsInRole("SalesManager") || User.IsInRole("FinanceManager");
        }

        /// <summary>
        /// لوحة المبيعات الشخصية (لمندوب المبيعات)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Sales()
        {
            var vm = new DashboardViewModel
            {
                Level = "sales",
                LevelName = "مبيعاتي الشخصية",
                UserDisplayName = User.Identity?.Name ?? ""
            };

            var userId = GetCurrentUserId();
            if (!userId.HasValue)
                return View("Index", vm);

            var today = DateTime.Today;
            var monthStart = new DateTime(today.Year, today.Month, 1);

            // عملاء المندوب
            vm.CustomersCount = await _context.Customers
                .Where(c => c.IsActive && c.UserId == userId)
                .CountAsync();

            // فواتير البيع للمندوب (عبر عملائه فقط)
            var myCustomerIds = await _context.Customers
                .Where(c => c.UserId == userId)
                .Select(c => c.CustomerId)
                .ToListAsync();

            if (myCustomerIds.Any())
            {
                var salesBase = _context.SalesInvoices
                    .Where(si => si.IsPosted && myCustomerIds.Contains(si.CustomerId));

                vm.SalesInvoicesTodayCount = await salesBase
                    .Where(si => si.SIDate.Date == today)
                    .CountAsync();
                vm.SalesInvoicesTodayTotal = await salesBase
                    .Where(si => si.SIDate.Date == today)
                    .SumAsync(si => si.NetTotal);

                vm.SalesInvoicesMonthCount = await salesBase
                    .Where(si => si.SIDate >= monthStart)
                    .CountAsync();
                vm.SalesInvoicesMonthTotal = await salesBase
                    .Where(si => si.SIDate >= monthStart)
                    .SumAsync(si => si.NetTotal);
            }

            // آخر فواتير المندوب
            vm.RecentItems = await _context.SalesInvoices
                .Where(si => si.IsPosted && myCustomerIds.Contains(si.CustomerId))
                .Include(si => si.Customer)
                .OrderByDescending(si => si.SIDate).ThenByDescending(si => si.SITime)
                .Take(8)
                .Select(si => new DashboardRecentItem
                {
                    Type = "فاتورة بيع",
                    PartyName = si.Customer!.CustomerName,
                    Amount = si.NetTotal,
                    Date = si.SIDate
                })
                .ToListAsync();

            return View("Index", vm);
        }

        /// <summary>
        /// لوحة المدير (مبيعات، مشتريات، إيصالات، مدفوعات)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Manager()
        {
            var vm = await BuildOwnerDashboardAsync();
            vm.Level = "manager";
            vm.LevelName = "لوحة المدير";
            vm.UserDisplayName = User.Identity?.Name ?? "";
            vm.ProfitMonth = null; // المدير قد لا يرى الأرباح حسب الصلاحيات
            return View("Index", vm);
        }

        /// <summary>
        /// لوحة المالك الكاملة (كل البيانات + الأرباح)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Owner()
        {
            var vm = await BuildOwnerDashboardAsync();
            vm.Level = "owner";
            vm.LevelName = "لوحة الإدارة الكاملة";
            vm.UserDisplayName = User.Identity?.Name ?? "";
            return View("Index", vm);
        }

        /// <summary>
        /// الافتراضي: يوجّه حسب دور المستخدم
        /// </summary>
        [HttpGet]
        public IActionResult Index()
        {
            if (IsSalesRole() && !IsOwnerOrManager())
                return RedirectToAction(nameof(Sales));
            if (IsOwnerOrManager())
                return RedirectToAction(nameof(Owner));
            return RedirectToAction(nameof(Manager));
        }

        private async Task<DashboardViewModel> BuildOwnerDashboardAsync()
        {
            var vm = new DashboardViewModel();
            var today = DateTime.Today;
            var monthStart = new DateTime(today.Year, today.Month, 1);

            vm.CustomersCount = await _context.Customers.CountAsync(c => c.IsActive);
            vm.ProductsCount = await _context.Products.CountAsync(p => p.IsActive);

            // فواتير البيع
            var salesQ = _context.SalesInvoices.Where(si => si.IsPosted);
            vm.SalesInvoicesTodayCount = await salesQ.Where(si => si.SIDate.Date == today).CountAsync();
            vm.SalesInvoicesTodayTotal = await salesQ.Where(si => si.SIDate.Date == today).SumAsync(si => si.NetTotal);
            vm.SalesInvoicesMonthCount = await salesQ.Where(si => si.SIDate >= monthStart).CountAsync();
            vm.SalesInvoicesMonthTotal = await salesQ.Where(si => si.SIDate >= monthStart).SumAsync(si => si.NetTotal);
            vm.SalesMonthTotal = vm.SalesInvoicesMonthTotal;

            // فواتير المشتريات
            var purchQ = _context.PurchaseInvoices.Where(pi => pi.IsPosted);
            vm.PurchaseInvoicesMonthCount = await purchQ.Where(pi => pi.PIDate >= monthStart).CountAsync();
            vm.PurchaseInvoicesMonthTotal = await purchQ.Where(pi => pi.PIDate >= monthStart).SumAsync(pi => pi.NetTotal);
            vm.PurchasesMonthTotal = vm.PurchaseInvoicesMonthTotal;

            // إيصالات ومدفوعات الشهر
            vm.CashReceiptsMonthTotal = await _context.CashReceipts
                .Where(r => r.IsPosted && r.ReceiptDate >= monthStart)
                .SumAsync(r => r.Amount);
            vm.CashPaymentsMonthTotal = await _context.CashPayments
                .Where(p => p.IsPosted && p.PaymentDate >= monthStart)
                .SumAsync(p => p.Amount);

            // أرباح الشهر (تقدير: مبيعات - مشتريات) - تبسيط
            vm.ProfitMonth = vm.SalesInvoicesMonthTotal - vm.PurchaseInvoicesMonthTotal;

            // تنبيهات مخزون: أصناف نفدت أو رصيدها صفر
            var productIdsWithStock = await _context.StockBatches
                .Where(sb => sb.QtyOnHand > 0)
                .Select(sb => sb.ProdId)
                .Distinct()
                .ToListAsync();
            vm.LowStockProductsCount = await _context.Products
                .Where(p => p.IsActive && !productIdsWithStock.Contains(p.ProdId))
                .CountAsync();

            // آخر الحركات: فواتير بيع وشراء وإيصالات
            var recentSales = await _context.SalesInvoices
                .Where(si => si.IsPosted)
                .Include(si => si.Customer)
                .OrderByDescending(si => si.SIDate).ThenByDescending(si => si.SITime)
                .Take(4)
                .Select(si => new DashboardRecentItem { Type = "فاتورة بيع", PartyName = si.Customer!.CustomerName!, Amount = si.NetTotal, Date = si.SIDate })
                .ToListAsync();

            var recentPurch = await _context.PurchaseInvoices
                .Where(pi => pi.IsPosted)
                .Include(pi => pi.Customer)
                .OrderByDescending(pi => pi.PIDate)
                .Take(3)
                .Select(pi => new DashboardRecentItem { Type = "فاتورة شراء", PartyName = pi.Customer!.CustomerName!, Amount = pi.NetTotal, Date = pi.PIDate })
                .ToListAsync();

            var recentReceipts = await _context.CashReceipts
                .Where(r => r.IsPosted)
                .Include(r => r.Customer)
                .OrderByDescending(r => r.ReceiptDate)
                .Take(3)
                .Select(r => new DashboardRecentItem { Type = "إيصال قبض", PartyName = r.Customer!.CustomerName ?? "-", Amount = r.Amount, Date = r.ReceiptDate })
                .ToListAsync();

            vm.RecentItems = recentSales.Concat(recentPurch).Concat(recentReceipts)
                .OrderByDescending(x => x.Date)
                .Take(10)
                .ToList();

            return vm;
        }
    }
}
