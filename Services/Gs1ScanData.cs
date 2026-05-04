using System;

namespace ERP.Services
{
    /// <summary>
    /// نتيجة تفكيك كود GS1/2D لتهيئة الشراء والبيع بالسكانر.
    /// </summary>
    public class Gs1ScanData
    {
        public string Raw { get; set; } = string.Empty;
        public string Cleaned { get; set; } = string.Empty;
        public string? Gtin { get; set; }
        public string? SerialNo { get; set; }
        public string? BatchNo { get; set; }
        public DateTime? Expiry { get; set; }
        public bool IsValid => !string.IsNullOrWhiteSpace(Gtin) || !string.IsNullOrWhiteSpace(SerialNo);
    }
}
