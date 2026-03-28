using ERP.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ERP.Services.Caching
{
    /// <summary>
    /// كاش ذاكرة لقائمة أصناف خفيفة — يُبنى من قاعدة البيانات عند أول طلب بعد المسح أو انتهاء المدة.
    /// </summary>
    public sealed class ProductCacheService : IProductCacheService
    {
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

        private readonly AppDbContext _db;
        private readonly IMemoryCache _cache;

        public ProductCacheService(AppDbContext db, IMemoryCache cache)
        {
            _db = db;
            _cache = cache;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ProductLookupCacheItem>> GetProductsLookupAsync(
            CancellationToken cancellationToken = default)
        {
            if (_cache.TryGetValue(CacheKeys.Products.AllLookupV1, out IReadOnlyList<ProductLookupCacheItem>? cached) &&
                cached != null)
                return cached;

            // لا نفلتر IsActive هنا ليطابق سلوك SearchProducts/SearchProductsByCode القديم (كل الأصناف).
            var list = await _db.Products
                .AsNoTracking()
                .OrderBy(p => p.ProdName)
                .Select(p => new ProductLookupCacheItem
                {
                    ProdId = p.ProdId,
                    ProdName = p.ProdName,
                    Company = p.Company,
                    Barcode = p.Barcode,
                    IsActive = p.IsActive
                })
                .ToListAsync(cancellationToken);

            _cache.Set(
                CacheKeys.Products.AllLookupV1,
                list,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = CacheDuration
                });

            return list;
        }

        /// <inheritdoc />
        public void ClearProductsCache()
        {
            _cache.Remove(CacheKeys.Products.AllLookupV1);
        }
    }
}
