// =========================================================
// DTO: بيانات حفظ هيدر فاتورة المبيعات
// - لازم أسماء الخصائص تطابق اللي الـ JavaScript بيبعتها
//   (invoiceId, customerId, warehouseId, SOId)
// - نستخدم System.Text.Json.Serialization للتأكد من الـ binding
// =========================================================
using System.Text.Json.Serialization;

namespace ERP.ViewModels
{
    public class SalesInvoiceHeaderDto
    {
        [JsonPropertyName("invoiceId")]
        public int InvoiceId { get; set; }      // متغير: رقم الفاتورة (0 لو جديدة) - مطابق للـ JS

        [JsonPropertyName("customerId")]
        public int CustomerId { get; set; }     // متغير: كود العميل - مطابق للـ JS

        [JsonPropertyName("warehouseId")]
        public int WarehouseId { get; set; }    // متغير: كود المخزن - مطابق للـ JS

        [JsonPropertyName("SOId")]
        public int? SOId { get; set; }       // متغير: للتوافق فقط مع سكربت المشتريات/المبيعات (مش هنستخدمه الآن)
    }
}
