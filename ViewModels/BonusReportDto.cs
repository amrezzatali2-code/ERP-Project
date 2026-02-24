namespace ERP.ViewModels
{
    /// <summary>
    /// DTO: تقرير مبيعات أصناف البونص لكل مستخدم
    /// </summary>
    public class BonusReportDto
    {
        public string UserName { get; set; } = "";
        public string ProdName { get; set; } = "";
        public string ProductBonusGroupName { get; set; } = "";
        public decimal BonusAmountPerUnit { get; set; }
        public int TotalQty { get; set; }
        public decimal TotalSalesValue { get; set; }
        public decimal TotalBonusAmount { get; set; }
    }
}
