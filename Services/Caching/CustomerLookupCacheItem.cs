namespace ERP.Services.Caching
{
    /// <summary>
    /// صورة خفيفة للعميل للقوائم/الربط — عمداً **بدون** رصيد أو حد ائتمان
    /// حتى لا يُقرأ من الكاش رقم مالي قديم.
    /// </summary>
    public sealed class CustomerLookupCacheItem
    {
        public int CustomerId { get; init; }
        public string CustomerName { get; init; } = "";
        public string? Phone1 { get; init; }
        public string? PartyCategory { get; init; }
        public bool IsActive { get; init; }
        public int? AccountId { get; init; }
        public int? GovernorateId { get; init; }
        public int? DistrictId { get; init; }
        public int? AreaId { get; init; }
        public int? RouteId { get; init; }
    }
}
