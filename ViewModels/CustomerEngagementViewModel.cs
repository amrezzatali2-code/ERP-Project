// ViewModels/CustomerEngagementViewModel.cs
using System;

namespace ERP.ViewModels
{
    /// <summary>
    /// ViewModel شاشة "حجم تعامل عميل"
    /// يجمع أرقام المبيعات/المرتجعات/المدفوعات … إلخ خلال مدى زمني اختياري
    /// </summary>
    public class CustomerEngagementViewModel
    {
        // =====================  مدخلات الفلترة (اختياري) =====================
        public int? CustomerId { get; set; }     // رقم العميل المُختار
        public DateTime? From { get; set; }      // من تاريخ
        public DateTime? To { get; set; }        // إلى تاريخ

        // =====================  الأرقام والتجميعات ===========================
        public decimal TotalSales { get; set; }      // إجمالي المبيعات
        public decimal TotalReturns { get; set; }    // إجمالي المرتجعات (الجديد)
        public decimal NetSales { get; set; }        // صافي البيع = المبيعات - المرتجعات
        public decimal TotalPayments { get; set; }   // إجمالي المدفوعات (تحصيلات)

        // حدود/أرصدة (اختياري حسب جداولك)
        public decimal CreditLimit { get; set; }         // الحد الائتماني
        public decimal EstimatedBalance { get; set; }    // رصيد تقديري (مثال حسابي)
        public decimal CurrentBalance { get; set; }      // الرصيد الحالي (لو عندك دفتر عام)

        // آخر فاتورة (اختياري)
        public string? LastInvoiceNo { get; set; }       // رقم آخر فاتورة
        public DateTime? LastInvoiceDate { get; set; }   // تاريخ آخر فاتورة
    }
}
