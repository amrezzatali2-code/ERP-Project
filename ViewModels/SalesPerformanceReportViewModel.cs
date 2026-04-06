namespace ERP.ViewModels
{
    /// <summary>
    /// تقرير أداء المبيعات مع فلاتر التاريخ والمنطقة والمحافظة والعملاء والمستخدمين والأصناف.
    /// </summary>
    public class SalesPerformanceReportViewModel
    {
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        /// <summary>للعرض فقط؛ الفلتر الفعلي من قوائم الاختيار المتعدد.</summary>
        public List<int> SelectedGovernorateIds { get; set; } = new();
        public List<int> SelectedDistrictIds { get; set; } = new();
        public List<int> SelectedAreaIds { get; set; } = new();
        public List<int> SelectedCustomerIds { get; set; } = new();
        /// <summary>فلتر أنواع الحساب المحاسبية (AccountType) للحساب المرتبط بالطرف.</summary>
        public List<int> SelectedAccountTypes { get; set; } = new();
        public List<int> SelectedUserIds { get; set; } = new();
        public List<int> SelectedProductIds { get; set; } = new();

        /// <summary>الخصم المقدم للعميل % (متوسط مرجح)</summary>
        public decimal DiscountPctAvg { get; set; }
        /// <summary>صافي الربح الإجمالي</summary>
        public decimal NetProfit { get; set; }
        /// <summary>صافي المبيعات (مبيعات - مردودات)</summary>
        public decimal NetSales { get; set; }
        /// <summary>إجمالي المرتجعات</summary>
        public decimal TotalReturns { get; set; }
        /// <summary>إجمالي المبيعات (قبل خصم المرتجعات) — أساس حساب النسب</summary>
        public decimal TotalGrossSales { get; set; }
        /// <summary>نسبة صافي المبيعات من الإجمالي %</summary>
        public decimal NetSalesPct { get; set; }
        /// <summary>نسبة المرتجعات من الإجمالي %</summary>
        public decimal ReturnsPct { get; set; }
        /// <summary>نسبة صافي الربح من الإجمالي %</summary>
        public decimal NetProfitPct { get; set; }
        /// <summary>هل يوجد فلتر فعّال (محافظة/حي/منطقة/نوع طرف/عميل/مستخدم/صنف) — عندها فقط نعرض النسبة من الإجمالي.</summary>
        public bool HasActiveFilters { get; set; }
        /// <summary>إجمالي صافي المبيعات بدون فلتر (نفس الفترة) — أساس نسبة المبيعات المفلترة.</summary>
        public decimal GrandTotalNetSales { get; set; }
        /// <summary>إجمالي صافي الربح بدون فلتر (نفس الفترة) — أساس نسبة الربح المفلتر.</summary>
        public decimal GrandTotalNetProfit { get; set; }
        /// <summary>نسبة صافي المبيعات المفلترة من إجمالي البيع % (يُعرض فقط عند وجود فلتر).</summary>
        public decimal NetSalesPctOfTotal { get; set; }
        /// <summary>نسبة المرتجعات المفلترة من إجمالي البيع % (يُعرض فقط عند وجود فلتر).</summary>
        public decimal ReturnsPctOfTotal { get; set; }
        /// <summary>نسبة صافي الربح المفلتر من إجمالي البيع % (يُعرض فقط عند وجود فلتر).</summary>
        public decimal NetProfitPctOfTotalSales { get; set; }
        /// <summary>نسبة صافي الربح المفلتر من إجمالي الربح % (يُعرض فقط عند وجود فلتر).</summary>
        public decimal NetProfitPctOfTotalProfit { get; set; }
        /// <summary>عدد الفواتير</summary>
        public int InvoiceCount { get; set; }
        /// <summary>متوسط سعر القطعة (إجمالي المبيعات / كمية الوحدات)</summary>
        public decimal AvgItemPrice { get; set; }
        /// <summary>متوسط قيمة الفاتورة</summary>
        public decimal AvgInvoiceValue { get; set; }

        public List<SalesPerformanceRow> Rows { get; set; } = new();
        public List<SalesPerformanceChartPoint> ChartData { get; set; } = new();
        /// <summary>عند عنصر واحد (كاتب واحد): توزيع صافي المبيعات حسب التاريخ لرسم خطي.</summary>
        public List<string> ChartTimeSeriesLabels { get; set; } = new();
        public List<decimal> ChartTimeSeriesValues { get; set; } = new();
        public bool IsLineChartMode => ChartTimeSeriesLabels.Count > 0;

        public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Governorates { get; set; } = new();
        public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Districts { get; set; } = new();
        public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Areas { get; set; } = new();
        public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Users { get; set; } = new();
        public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Customers { get; set; } = new();
        public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> AccountTypeOptions { get; set; } = new();
        public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Products { get; set; } = new();
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
