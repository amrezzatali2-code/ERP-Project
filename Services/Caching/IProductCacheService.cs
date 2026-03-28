namespace ERP.Services.Caching
{
    /// <summary>
    /// كاش قراءة لقائمة أصناف خفيفة للبحث/الأوتوكومبليت — لا يُستخدم لفواتير أو مخزون حي.
    /// </summary>
    public interface IProductCacheService
    {
        /// <summary>لقطة أصناف خفيفة من قاعدة البيانات مع AsNoTracking (مدة افتراضية 15 دقيقة).</summary>
        Task<IReadOnlyList<ProductLookupCacheItem>> GetProductsLookupAsync(CancellationToken cancellationToken = default);

        /// <summary>إزالة إدخال الكاش (استدعِها بعد إنشاء/تعديل/حذف صنف أو استيراد يغيّر الأصناف).</summary>
        void ClearProductsCache();
    }
}
