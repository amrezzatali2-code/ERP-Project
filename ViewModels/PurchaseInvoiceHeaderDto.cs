// ViewModels/PurchaseInvoiceHeaderDto.cs
using System;

namespace ERP.ViewModels
{
    /// <summary>
    /// داتا بسيطة يستقبلها الكنترولر من الشاشة عند حفظ هيدر فاتورة الشراء.
    /// </summary>
    public class PurchaseInvoiceHeaderDto
    {
        // متغير: رقم الفاتورة (0 = فاتورة جديدة، >0 = فاتورة موجودة)
        public int PIId { get; set; }

        // متغير: كود المورد المختار من شاشة الأوتوكمبليت
        public int CustomerId { get; set; }

        // متغير: كود المخزن المختار من الكومبو
        public int WarehouseId { get; set; }

        // متغير: طلب الشراء المرجعي (اختياري)
        public int? RefPRId { get; set; }
    }
}
