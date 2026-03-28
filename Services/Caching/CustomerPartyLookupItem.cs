namespace ERP.Services.Caching
{
    /// <summary>
    /// صف خفيف لقائمة أطراف شاشة حركة الصنف (بحث بالاسم + فلترة ظهور الحسابات في الذاكرة).
    /// </summary>
    public sealed class CustomerPartyLookupItem
    {
        public int CustomerId { get; init; }
        public string CustomerName { get; init; } = "";
        public int? AccountId { get; init; }
    }
}
