namespace ERP.ViewModels
{
    /// <summary>
    /// تقرير أداء المبيعات (بالكاتب/المستخدم) مع فلاتر التاريخ والمنطقة والمحافظة والعملاء.
    /// </summary>
    public class SalesPerformanceReportViewModel
    {
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public List<int>? UserIds { get; set; }
        public List<string>? UserNames { get; set; }
        public int? GovernorateId { get; set; }
        public int? DistrictId { get; set; }
        public int? AreaId { get; set; }
        public List<int>? CustomerIds { get; set; }

        /// <summary>الخصم المقدم للعميل % (متوسط مرجح)</summary>
        public decimal DiscountPctAvg { get; set; }
        /// <summary>صافي الربح الإجمالي</summary>
        public decimal NetProfit { get; set; }
        /// <summary>صافي المبيعات (مبيعات - مردودات)</summary>
        public decimal NetSales { get; set; }
        /// <summary>عدد الفواتير</summary>
        public int InvoiceCount { get; set; }
        /// <summary>متوسط سعر القطعة (إجمالي المبيعات / كمية الوحدات)</summary>
        public decimal AvgItemPrice { get; set; }
        /// <summary>متوسط قيمة الفاتورة</summary>
        public decimal AvgInvoiceValue { get; set; }

        public List<SalesPerformanceRow> Rows { get; set; } = new();
        public List<SalesPerformanceChartPoint> ChartData { get; set; } = new();

        public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Governorates { get; set; } = new();
        public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Districts { get; set; } = new();
        public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Areas { get; set; } = new();
        public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Users { get; set; } = new();
        public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Customers { get; set; } = new();
    }

    public class SalesPerformanceRow
    {
        public string WriterName { get; set; } = "";
        public int? UserId { get; set; }
        public decimal TotalSales { get; set; }
        public decimal SalesPct { get; set; }
        public decimal TotalReturns { get; set; }
        public decimal ReturnsPct { get; set; }
        public decimal NetProfit { get; set; }
        public decimal NetProfitPct { get; set; }
        public int InvoiceCount { get; set; }
        public decimal AvgInvoiceValue { get; set; }
        public decimal QtySold { get; set; }
        public decimal DiscountPct { get; set; }
    }

    public class SalesPerformanceChartPoint
    {
        public string WriterName { get; set; } = "";
        public decimal NetSales { get; set; }
    }
}
