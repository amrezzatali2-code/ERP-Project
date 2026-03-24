namespace ERP.ViewModels;

/// <summary>صف واحد لقائمة عملاء شاشة حجم التعامل (datalist + بحث موحّد مع فاتورة المبيعات).</summary>
public class CustomerVolumeDropdownItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public bool IsActive { get; set; }
}
