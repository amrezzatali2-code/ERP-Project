namespace ERP.ViewModels
{
    /// <summary>
    /// صف في تقرير أصناف مفصّلة (مبيعات، مشتريات، مرتجعات، تسويات، تحويلات، طلبات شراء، أوامر بيع).
    /// </summary>
    public class ProductDetailsReportRow
    {
        public string ReportType { get; set; } = "";
        public DateTime Date { get; set; }
        public string DocNo { get; set; } = "";
        public int DocId { get; set; }
        public int ProductId { get; set; }
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public decimal Qty { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? Total { get; set; }
        public decimal? DiscountPercent { get; set; }
        public decimal? DiscountValue { get; set; }
        public TimeSpan? Time { get; set; }
        public string? CustomerCode { get; set; }
        public string? PartyName { get; set; }
        public string? WarehouseName { get; set; }
        public string? BatchNo { get; set; }
        public DateTime? Expiry { get; set; }
        public string? Notes { get; set; }
        /// <summary>الكاتب (من أنشأ المستند).</summary>
        public string? Author { get; set; }
        /// <summary>المنطقة (اسم الفرع من المخزن).</summary>
        public string? Region { get; set; }
        /// <summary>اسم المستند بالعربي (فاتورة مبيعات، طلب شراء، ...).</summary>
        public string? DocumentNameAr { get; set; }
    }
}
