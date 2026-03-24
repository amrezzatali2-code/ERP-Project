namespace ERP.Models
{
    /// <summary>صف بسيط لعرض الحسابات في قوائم الاختيار (صلاحيات الأدوار / ظهور الحسابات).</summary>
    public class AccountListItem
    {
        public int AccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
    }
}
