namespace ERP.ViewModels
{
    /// <summary>
    /// تجميع تقرير البونص حسب المستخدم فقط (إجماليات في الفترة).
    /// </summary>
    public class BonusReportByUserDto
    {
        public string UserName { get; set; } = "";
        public int TotalQty { get; set; }
        public decimal TotalSalesValue { get; set; }
        public decimal TotalBonusAmount { get; set; }
    }
}
