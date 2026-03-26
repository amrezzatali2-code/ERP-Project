namespace ERP.ViewModels
{
    /// <summary>
    /// بيانات لوحة «مبيعاتي الشخصية» فقط.
    /// </summary>
    public class DashboardViewModel
    {
        public string Level { get; set; } = "sales";
        public string LevelName { get; set; } = "مبيعاتي الشخصية";
        public string? UserDisplayName { get; set; }
        /// <summary>فلتر التاريخ: من (للوحة مبيعاتي الشخصية).</summary>
        public DateTime? FromDate { get; set; }
        /// <summary>فلتر التاريخ: إلى (للوحة مبيعاتي الشخصية).</summary>
        public DateTime? ToDate { get; set; }

        // إحصائيات أساسية
        /// <summary>عدد العملاء المربوطين بالمستخدم الذين لديهم فواتير مبيعات مرحّلة في الفترة.</summary>
        public int CustomersCount { get; set; }
        /// <summary>عدد مناطق/تجميعات جغرافية مختلفة ظهرت في مبيعات الفترة.</summary>
        public int RegionsCount { get; set; }
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

        /// <summary>عملاء مربوطون بالمستخدم مع إحصائيات الفترة (للعرض في النافذة المنبثقة).</summary>
        public List<DashboardLinkedCustomerRow> LinkedCustomersDetail { get; set; } = new();

        /// <summary>مبيعات الفترة مجمّعة حسب المنطقة (أو نص المنطقة / بدون منطقة).</summary>
        public List<DashboardRegionSalesRow> RegionSalesRows { get; set; } = new();
    }

    public class DashboardLinkedCustomerRow
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; } = "";
        public string? Phone { get; set; }
        public int InvoicesInPeriod { get; set; }
        public decimal SalesTotalInPeriod { get; set; }
    }

    public class DashboardRegionSalesRow
    {
        public int? AreaId { get; set; }
        public string AreaName { get; set; } = "";
        public int InvoiceCount { get; set; }
        public decimal SalesTotal { get; set; }
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
