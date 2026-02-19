namespace ERP.ViewModels
{
    /// <summary>
    /// بيانات لوحة التحكم (جميع المستويات)
    /// </summary>
    public class DashboardViewModel
    {
        public string Level { get; set; } = "owner";  // sales | manager | owner
        public string LevelName { get; set; } = "لوحة الإدارة الكاملة";
        public string? UserDisplayName { get; set; }

        // إحصائيات أساسية
        public int CustomersCount { get; set; }
        public int ProductsCount { get; set; }
        public int SalesInvoicesTodayCount { get; set; }
        public decimal SalesInvoicesTodayTotal { get; set; }
        public int SalesInvoicesMonthCount { get; set; }
        public decimal SalesInvoicesMonthTotal { get; set; }
        public int PurchaseInvoicesMonthCount { get; set; }
        public decimal PurchaseInvoicesMonthTotal { get; set; }
        public int LowStockProductsCount { get; set; }
        public decimal CashReceiptsMonthTotal { get; set; }
        public decimal CashPaymentsMonthTotal { get; set; }

        // للأرباح (مستوى المالك/المدير)
        public decimal? ProfitMonth { get; set; }
        public decimal? SalesMonthTotal { get; set; }
        public decimal? PurchasesMonthTotal { get; set; }

        // آخر الحركات
        public List<DashboardRecentItem> RecentItems { get; set; } = new();

        /// <summary>عدد أصناف المبيعات (أصناف متميزة مباعة في الشهر)</summary>
        public int SalesProductsSoldCount { get; set; }

        /// <summary>بيانات المخطط البياني: مبيعات آخر 7 أيام</summary>
        public List<DashboardChartPoint> ChartData { get; set; } = new();
    }

    public class DashboardChartPoint
    {
        public string Date { get; set; } = "";
        public decimal Amount { get; set; }
    }

    public class DashboardRecentItem
    {
        public string Type { get; set; } = "";
        public string PartyName { get; set; } = "";
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
    }
}
