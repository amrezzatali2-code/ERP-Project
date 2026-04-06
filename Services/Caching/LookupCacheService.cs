using ERP.Data;
using ERP.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ERP.Services.Caching
{
    public sealed class LookupCacheService : ILookupCacheService
    {
        private static readonly TimeSpan GeoCacheDuration = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan WarehouseCacheDuration = TimeSpan.FromMinutes(30);

        private readonly AppDbContext _db;
        private readonly IMemoryCache _cache;

        public LookupCacheService(AppDbContext db, IMemoryCache cache)
        {
            _db = db;
            _cache = cache;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<Governorate>> GetGovernoratesAsync(CancellationToken cancellationToken = default)
        {
            if (_cache.TryGetValue(CacheKeys.Geography.GovernoratesV1, out IReadOnlyList<Governorate>? cached) &&
                cached != null)
                return cached;

            var list = await _db.Governorates
                .AsNoTracking()
                .OrderBy(g => g.GovernorateName)
                .ToListAsync(cancellationToken);

            _cache.Set(
                CacheKeys.Geography.GovernoratesV1,
                list,
                new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = GeoCacheDuration });

            return list;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<District>> GetDistrictsAsync(CancellationToken cancellationToken = default)
        {
            if (_cache.TryGetValue(CacheKeys.Geography.DistrictsV1, out IReadOnlyList<District>? cached) &&
                cached != null)
                return cached;

            var list = await _db.Districts
                .AsNoTracking()
                .OrderBy(d => d.GovernorateId)
                .ThenBy(d => d.DistrictName)
                .ToListAsync(cancellationToken);

            _cache.Set(
                CacheKeys.Geography.DistrictsV1,
                list,
                new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = GeoCacheDuration });

            return list;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<Area>> GetAreasAsync(CancellationToken cancellationToken = default)
        {
            if (_cache.TryGetValue(CacheKeys.Geography.AreasV1, out IReadOnlyList<Area>? cached) && cached != null)
                return cached;

            var list = await _db.Areas
                .AsNoTracking()
                .OrderBy(a => a.GovernorateId)
                .ThenBy(a => a.DistrictId)
                .ThenBy(a => a.AreaName)
                .ToListAsync(cancellationToken);

            _cache.Set(
                CacheKeys.Geography.AreasV1,
                list,
                new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = GeoCacheDuration });

            return list;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<Policy>> GetPoliciesAsync(CancellationToken cancellationToken = default)
        {
            if (_cache.TryGetValue(CacheKeys.Policies.AllV1, out IReadOnlyList<Policy>? cached) && cached != null)
                return cached;

            var list = await _db.Policies
                .AsNoTracking()
                .OrderBy(p => p.Name)
                .ToListAsync(cancellationToken);

            _cache.Set(
                CacheKeys.Policies.AllV1,
                list,
                new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = WarehouseCacheDuration });

            return list;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ProductGroup>> GetProductGroupsAsync(CancellationToken cancellationToken = default)
        {
            if (_cache.TryGetValue(CacheKeys.ProductGroups.AllV1, out IReadOnlyList<ProductGroup>? cached) && cached != null)
                return cached;

            var list = await _db.ProductGroups
                .AsNoTracking()
                .OrderBy(g => g.Name)
                .ToListAsync(cancellationToken);

            _cache.Set(
                CacheKeys.ProductGroups.AllV1,
                list,
                new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = WarehouseCacheDuration });

            return list;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<Warehouse>> GetWarehousesAsync(CancellationToken cancellationToken = default)
        {
            if (_cache.TryGetValue(CacheKeys.Warehouses.AllV1, out IReadOnlyList<Warehouse>? cached) && cached != null)
                return cached;

            var list = await _db.Warehouses
                .AsNoTracking()
                .Include(w => w.Branch)
                .OrderBy(w => w.WarehouseName)
                .ToListAsync(cancellationToken);

            _cache.Set(
                CacheKeys.Warehouses.AllV1,
                list,
                new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = WarehouseCacheDuration });

            return list;
        }

        /// <inheritdoc />
        public void ClearGovernoratesCache() => _cache.Remove(CacheKeys.Geography.GovernoratesV1);

        /// <inheritdoc />
        public void ClearDistrictsCache() => _cache.Remove(CacheKeys.Geography.DistrictsV1);

        /// <inheritdoc />
        public void ClearAreasCache() => _cache.Remove(CacheKeys.Geography.AreasV1);

        /// <inheritdoc />
        public void ClearPoliciesCache() => _cache.Remove(CacheKeys.Policies.AllV1);

        /// <inheritdoc />
        public void ClearProductGroupsCache() => _cache.Remove(CacheKeys.ProductGroups.AllV1);

        /// <inheritdoc />
        public void ClearWarehousesCache() => _cache.Remove(CacheKeys.Warehouses.AllV1);

        /// <inheritdoc />
        public void ClearAllGeographyCaches()
        {
            ClearGovernoratesCache();
            ClearDistrictsCache();
            ClearAreasCache();
        }
    }
}
