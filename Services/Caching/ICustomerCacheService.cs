namespace ERP.Services.Caching
{
    /// <summary>
    /// كاش لقائمة عملاء خفيفة (بدون أرصدة).
    /// شاشات تطبق <c>UserAccountVisibility</c> يجب أن تفلتر النتيجة حسب المستخدم ولا تعتمد على القائمة الكاملة وحدها.
    /// </summary>
    public interface ICustomerCacheService
    {
        /// <summary>عملاء نشطون — حقول تعريف فقط (مدة افتراضية 10 دقائق).</summary>
        Task<IReadOnlyList<CustomerLookupCacheItem>> GetActiveCustomersLookupAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// بحث أطراف بالاسم لحركة الصنف (مثل SearchParties): يستخدم الكاش عندما لا يكون التقييد بقائمة مسموح مفعّلاً؛
        /// وإلا يُنفَّذ استعلام من قاعدة البيانات مع <c>ApplyCustomerVisibilityFilterAsync</c> (يحتاج LedgerEntries).
        /// </summary>
        Task<IReadOnlyList<CustomerPartySearchDto>> SearchPartiesAutocompleteAsync(string term, CancellationToken cancellationToken = default);

        /// <summary>مسح كل مفاتيح كاش العملاء (النشط والأطراف).</summary>
        void ClearCustomersCache();
    }
}
