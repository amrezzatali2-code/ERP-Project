namespace ERP.ViewModels
{
    /// <summary>
    /// DTO: بيانات تقرير أرصدة العملاء
    /// </summary>
    public class CustomerBalanceReportDto
    {
        public int CustomerId { get; set; }
        public string CustomerCode { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string? PartyCategory { get; set; } = "";
        public string? Phone1 { get; set; } = "";
        public decimal CurrentBalance { get; set; }
        public decimal CreditLimit { get; set; }
        public decimal TotalSales { get; set; }
        public decimal TotalPurchases { get; set; }
        public decimal TotalReturns { get; set; }
        public decimal AvailableCredit { get; set; }
    }
}
