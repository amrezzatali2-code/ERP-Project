// =========================================================
// DTO: بيانات حفظ هيدر مرتجع المشتريات (مطابق للمرسل من الـ JS)
// =========================================================
using System.Text.Json.Serialization;

namespace ERP.ViewModels
{
    public class PurchaseReturnHeaderDto
    {
        [JsonPropertyName("pretId")]
        public int PRetId { get; set; }

        [JsonPropertyName("customerId")]
        public int CustomerId { get; set; }

        [JsonPropertyName("warehouseId")]
        public int WarehouseId { get; set; }

        [JsonPropertyName("refPIId")]
        public int? RefPIId { get; set; }
    }
}
