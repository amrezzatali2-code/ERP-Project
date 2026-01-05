// =========================================================
// DTO: بيانات حفظ هيدر فاتورة المبيعات
// - لازم أسماء الخصائص تطابق اللي الـ JavaScript بيبعتها
//   (InvoiceId, CustomerId, WarehouseId, RefPRId)
// - RefPRId موجود فقط للتوافق مع السكربت (لن نستخدمه الآن)
// =========================================================
namespace ERP.ViewModels
{
    public class SalesInvoiceHeaderDto
    {
        public int InvoiceId { get; set; }      // متغير: رقم الفاتورة (0 لو جديدة) - مطابق للـ JS
        public int CustomerId { get; set; }     // متغير: كود العميل - مطابق للـ JS
        public int WarehouseId { get; set; }    // متغير: كود المخزن - مطابق للـ JS

        public int? RefPRId { get; set; }       // متغير: للتوافق فقط مع سكربت المشتريات/المبيعات (مش هنستخدمه الآن)
    }
}
