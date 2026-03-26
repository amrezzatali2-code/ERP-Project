namespace ERP.ViewModels;

/// <summary>صف واحد لقائمة الأصناف في تقرير أرصدة الأصناف (datalist) — نوع صريح لتفادي أخطاء dynamic في الـ View.</summary>
public sealed class ProductBalancesDatalistRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string GenericName { get; set; } = "";
    public string Company { get; set; } = "";
    public decimal PriceRetail { get; set; }
    public bool HasQuota { get; set; }
}
