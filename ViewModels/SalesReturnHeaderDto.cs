// =========================================================
// DTO: بيانات حفظ هيدر مرتجع البيع (مطابق للمرسل من الـ JS)
// =========================================================
using System.Text.Json.Serialization;

namespace ERP.ViewModels
{
    public class SalesReturnHeaderDto
    {
        [JsonPropertyName("srId")]
        public int SRId { get; set; }

        [JsonPropertyName("customerId")]
        public int CustomerId { get; set; }

        [JsonPropertyName("warehouseId")]
        public int WarehouseId { get; set; }

        [JsonPropertyName("salesInvoiceId")]
        public int? SalesInvoiceId { get; set; }
    }
}
