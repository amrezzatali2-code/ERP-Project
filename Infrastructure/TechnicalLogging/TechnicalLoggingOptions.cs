namespace ERP.Infrastructure.TechnicalLogging
{
    /// <summary>
    /// إعدادات التسجيل الفني (منفصل عن سجل النشاط الإداري).
    /// </summary>
    public class TechnicalLoggingOptions
    {
        public const string SectionName = "TechnicalLogging";

        /// <summary>تفعيل قياس زمن أكشنات MVC في الفلتر.</summary>
        public bool EnableMvcPerformance { get; set; } = true;

        /// <summary>تسجيل الأداء فقط إذا تجاوزت المدة هذا الحد (ملي ثانية). 0 = تسجيل كل الطلبات المكتملة.</summary>
        public int MvcPerformanceMinDurationMs { get; set; } = 0;

        /// <summary>تسجيل زمن الطلبات البطيئة في Serilog Request Logging (يُمرَّر لـ GetLevel).</summary>
        public int HttpRequestSlowThresholdMs { get; set; } = 2000;
    }
}
