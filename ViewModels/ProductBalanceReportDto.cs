namespace ERP.ViewModels
{
    /// <summary>
    /// DTO: بيانات تقرير أرصدة الأصناف
    /// </summary>
    public class ProductBalanceReportDto
    {
        public int ProdId { get; set; }
        public string ProdCode { get; set; } = "";
        public string ProdName { get; set; } = "";
        public string CategoryName { get; set; } = "";
        public int CurrentQty { get; set; }
        public decimal WeightedDiscount { get; set; }
        public decimal SalesQty { get; set; }
        public decimal PriceRetail { get; set; }
        public decimal UnitCost { get; set; }
        public decimal TotalCost { get; set; }
    }
}
