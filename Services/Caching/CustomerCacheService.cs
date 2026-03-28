using ERP.Data;
using ERP.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ERP.Services.Caching
{
    public sealed class CustomerCacheService : ICustomerCacheService
    {
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

        private readonly AppDbContext _db;
        private readonly IMemoryCache _cache;
        private readonly IUserAccountVisibilityService _accountVisibility;

        public CustomerCacheService(
            AppDbContext db,
            IMemoryCache cache,
            IUserAccountVisibilityService accountVisibility)
        {
            _db = db;
            _cache = cache;
            _accountVisibility = accountVisibility;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<CustomerLookupCacheItem>> GetActiveCustomersLookupAsync(
            CancellationToken cancellationToken = default)
        {
            if (_cache.TryGetValue(CacheKeys.Customers.ActiveLookupV1, out IReadOnlyList<CustomerLookupCacheItem>? cached) &&
                cached != null)
                return cached;

            var list = await _db.Customers
                .AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.CustomerName)
                .Select(c => new CustomerLookupCacheItem
                {
                    CustomerId = c.CustomerId,
                    CustomerName = c.CustomerName,
                    Phone1 = c.Phone1,
                    PartyCategory = c.PartyCategory,
                    IsActive = c.IsActive,
                    AccountId = c.AccountId,
                    GovernorateId = c.GovernorateId,
                    DistrictId = c.DistrictId,
                    AreaId = c.AreaId,
                    RouteId = c.RouteId
                })
                .ToListAsync(cancellationToken);

            _cache.Set(
                CacheKeys.Customers.ActiveLookupV1,
                list,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = CacheDuration
                });

            return list;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<CustomerPartySearchDto>> SearchPartiesAutocompleteAsync(
            string term,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(term))
                return Array.Empty<CustomerPartySearchDto>();

            term = term.Trim();

            var (hiddenAccountIds, restrictedOnly) = await _accountVisibility.GetVisibilityStateForCurrentUserAsync();

            // عند التقييد بقائمة «مسموح» فقط: منطق الظهور يعتمد على LedgerEntries — لا نستخدم لقطة الكاش.
            if (restrictedOnly)
            {
                var q = _db.Customers.AsNoTracking().Where(c => c.CustomerName != null && c.CustomerName.Contains(term));
                q = await _accountVisibility.ApplyCustomerVisibilityFilterAsync(q);
                return await q
                    .OrderBy(c => c.CustomerName)
                    .Take(20)
                    .Select(c => new CustomerPartySearchDto { Id = c.CustomerId, Name = c.CustomerName ?? "" })
                    .ToListAsync(cancellationToken);
            }

            var snapshot = await GetPartyLookupSnapshotAsync(cancellationToken);

            IEnumerable<CustomerPartyLookupItem> filtered = snapshot.Where(c =>
                (c.CustomerName ?? "").Contains(term));

            if (hiddenAccountIds.Count > 0)
            {
                filtered = filtered.Where(c =>
                    c.AccountId == null || !hiddenAccountIds.Contains(c.AccountId.Value));
            }

            return filtered
                .OrderBy(c => c.CustomerName, StringComparer.CurrentCulture)
                .Take(20)
                .Select(c => new CustomerPartySearchDto { Id = c.CustomerId, Name = c.CustomerName })
                .ToList();
        }

        /// <summary>
        /// لقطة خفيفة لكل الأطراف — تُستخدم للبحث عندما لا يكون المستخدم في وضع «قائمة مسموح» فقط.
        /// </summary>
        private async Task<IReadOnlyList<CustomerPartyLookupItem>> GetPartyLookupSnapshotAsync(
            CancellationToken cancellationToken)
        {
            if (_cache.TryGetValue(CacheKeys.Customers.PartyLookupV1, out IReadOnlyList<CustomerPartyLookupItem>? cached) &&
                cached != null)
                return cached;

            var list = await _db.Customers
                .AsNoTracking()
                .OrderBy(c => c.CustomerName)
                .Select(c => new CustomerPartyLookupItem
                {
                    CustomerId = c.CustomerId,
                    CustomerName = c.CustomerName ?? "",
                    AccountId = c.AccountId
                })
                .ToListAsync(cancellationToken);

            _cache.Set(
                CacheKeys.Customers.PartyLookupV1,
                list,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = CacheDuration
                });

            return list;
        }

        /// <inheritdoc />
        public void ClearCustomersCache()
        {
            _cache.Remove(CacheKeys.Customers.ActiveLookupV1);
            _cache.Remove(CacheKeys.Customers.PartyLookupV1);
        }
    }
}
