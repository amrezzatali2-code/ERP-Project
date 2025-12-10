using Microsoft.EntityFrameworkCore;
using System;

namespace ERP.Models
{
    /// <summary>
    /// سطر فاتورة الشراء: هنا تُسجل الكمية والتكلفة الفعلية + التشغيلة والصلاحية.
    /// عند "الترحيل" سيتم إضافة حركة دخول للمخزون.
    /// </summary>
    public class PILine
    {
        public int PIId { get; set; }     // FK إلى رأس الفاتورة (رقم فاتورة الشراء)
        public int LineNo { get; set; }   // رقم السطر داخل الفاتورة (1،2،3،...)

        public int ProdId { get; set; }   // كود الصنف
        public int Qty { get; set; }      // الكمية المشتراة

        [Precision(18, 4)]
        public decimal UnitCost { get; set; }   // تكلفة الوحدة (بدقة أعلى علشان التكلفة)

        [Precision(5, 2)]
        public decimal PurchaseDiscountPct { get; set; } // خصم الشراء %

        [Precision(18, 2)]
        public decimal PriceRetail { get; set; }         // سعر الجمهور (المعلن للتشغيلة)

        public string? BatchNo { get; set; }    // رقم التشغيلة
        public DateTime? Expiry { get; set; }   // تاريخ الصلاحية

        // ===== Navigation Property =====

        public virtual PurchaseInvoice PurchaseInvoice { get; set; } = null!;
        // سطر واحد يتبع فاتورة شراء واحدة (رأس الفاتورة)
        // ومقابلها في PurchaseInvoice: ICollection<PILine> Lines

        // مفتاح مركب: (PIId + LineNo)
    }
}
