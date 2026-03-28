namespace ERP.Services.Caching
{
    /// <summary>
    /// مفاتيح كاش ثابتة (نص واحد لكل إدخال) — يُفضّل البادئة ERP:Cache لتفادي التعارض.
    /// عند تغيير شكل البيانات المخزّنة، غيّر رقم الإصدار في المفتاح (v1 → v2).
    /// </summary>
    public static class CacheKeys
    {
        public static class Products
        {
            /// <summary>جميع الأصناف — حقول بحث خفيفة فقط (قراءة AsNoTracking؛ لا أسعار/مخزون حي).</summary>
            public const string AllLookupV1 = "ERP:Cache:v1:Products:AllLookup";
        }

        public static class Customers
        {
            /// <summary>عملاء نشطون — حقول تعريف/ربط فقط (بدون أرصدة أو حد ائتمان).</summary>
            public const string ActiveLookupV1 = "ERP:Cache:v1:Customers:ActiveLookup";

            /// <summary>كل الأطراف (للبحث في حركة الصنف) — خفيف؛ يُمسح مع ActiveLookup عند التعديل.</summary>
            public const string PartyLookupV1 = "ERP:Cache:v1:Customers:PartyLookup";
        }

        public static class Warehouses
        {
            public const string AllV1 = "ERP:Cache:v1:Warehouses:All";
        }

        public static class Geography
        {
            public const string GovernoratesV1 = "ERP:Cache:v1:Geo:Governorates";
            public const string DistrictsV1 = "ERP:Cache:v1:Geo:Districts";
            public const string AreasV1 = "ERP:Cache:v1:Geo:Areas";
        }
    }
}
