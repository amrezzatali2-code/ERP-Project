using ERP.Models;

namespace ERP.Services.Caching
{
    /// <summary>
    /// بيانات lookups شبه ثابتة — محافظات / أحياء / مناطق / مخازن / سياسات / مجموعات أصناف.
    /// </summary>
    public interface ILookupCacheService
    {
        Task<IReadOnlyList<Governorate>> GetGovernoratesAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<District>> GetDistrictsAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<Area>> GetAreasAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<Policy>> GetPoliciesAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ProductGroup>> GetProductGroupsAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<Warehouse>> GetWarehousesAsync(CancellationToken cancellationToken = default);

        void ClearGovernoratesCache();
        void ClearDistrictsCache();
        void ClearAreasCache();
        void ClearPoliciesCache();
        void ClearProductGroupsCache();
        void ClearWarehousesCache();

        /// <summary>مسح كل مفاتيح الجغرافيا دفعة واحدة (بعد استيراد أو تعديل جماعي).</summary>
        void ClearAllGeographyCaches();
    }
}
