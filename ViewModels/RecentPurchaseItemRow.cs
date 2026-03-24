namespace ERP.ViewModels
{
    /// <summary>
    /// صف لعرض أصناف المشتريات (سطور فواتير الشراء) في قائمة أصناف المشتريات الحديثة.
    /// </summary>
    public class RecentPurchaseItemRow
    {
        public string ProdName { get; set; } = "";
        public decimal PriceRetail { get; set; }
        public decimal PurchaseDiscountPct { get; set; }
        public int Qty { get; set; }
    }
}
