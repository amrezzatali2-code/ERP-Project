namespace ERP.Services.Caching
{
    /// <summary>
    /// نتيجة بحث أطراف للـ JSON (يُسلسل كـ id / name بخيارات الـ camelCase الافتراضية).
    /// </summary>
    public sealed class CustomerPartySearchDto
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
    }
}
