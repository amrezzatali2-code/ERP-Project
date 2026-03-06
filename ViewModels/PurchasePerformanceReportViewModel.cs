namespace ERP.ViewModels
{
    /// <summary>
    /// تقرير أداء المشتريات مع فلاتر التاريخ والمنطقة والمحافظة والموردين والمستخدمين والأصناف.
    /// </summary>
    public class PurchasePerformanceReportViewModel
    {
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public List<int> SelectedGovernorateIds { get; set; } = new();
        public List<int> SelectedDistrictIds { get; set; } = new();
        public List<int> SelectedAreaIds { get; set; } = new();
        public List<int> SelectedCustomerIds { get; set; } = new();
        public List<string> SelectedPartyCategories { get; set; } = new();
        public List<int> SelectedUserIds { get; set; } = new();
        public List<int> SelectedProductIds { get; set; } = new();

        public decimal DiscountPctAvg { get; set; }
        public decimal NetProfit { get; set; }
        public decimal NetPurchases { get; set; }
        public decimal TotalReturns { get; set; }
        public decimal TotalGrossPurchases { get; set; }
        public decimal NetPurchasesPct { get; set; }
        public decimal ReturnsPct { get; set; }
        public decimal NetProfitPct { get; set; }
        public int InvoiceCount { get; set; }
        public decimal AvgItemPrice { get; set; }
        public decimal AvgInvoiceValue { get; set; }

        public List<PurchasePerformanceRow> Rows { get; set; } = new();
        public List<PurchasePerformanceChartPoint> ChartData { get; set; } = new();
        public List<string> ChartTimeSeriesLabels { get; set; } = new();
        public List<decimal> ChartTimeSeriesValues { get; set; } = new();
        public bool IsLineChartMode => ChartTimeSeriesLabels.Count > 0;

        public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Governorates { get; set; } = new();
        public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Districts { get; set; } = new();
        public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Areas { get; set; } = new();
        public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Users { get; set; } = new();
        public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Customers { get; set; } = new();
        public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> PartyTypes { get; set; } = new();
        public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Products { get; set; } = new();
    }

    public class PurchasePerformanceRow
    {
        public string WriterName { get; set; } = "";
        public int? UserId { get; set; }
        public decimal TotalPurchases { get; set; }
        public decimal PurchasesPct { get; set; }
        public decimal TotalReturns { get; set; }
        public decimal ReturnsPct { get; set; }
        public decimal NetProfit { get; set; }
        public decimal NetProfitPct { get; set; }
        public int InvoiceCount { get; set; }
        public decimal AvgInvoiceValue { get; set; }
        public decimal QtyBought { get; set; }
        public decimal DiscountPct { get; set; }
    }

    public class PurchasePerformanceChartPoint
    {
        public string WriterName { get; set; } = "";
        public decimal NetPurchases { get; set; }
    }
}
