namespace ERP.Services.Caching
{
    /// <summary>
    /// صورة خفيفة للصنف لأغراض البحث/الأوتوكومبليت فقط — بدون أسعار أو مخزون حي.
    /// </summary>
    public sealed class ProductLookupCacheItem
    {
        public int ProdId { get; init; }
        public string? ProdName { get; init; }
        public string? Company { get; init; }
        public string? Barcode { get; init; }
        public bool IsActive { get; init; }
    }
}
