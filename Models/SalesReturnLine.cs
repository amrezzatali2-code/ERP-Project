using Microsoft.EntityFrameworkCore;          // علشان [Precision]
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;  // علشان [Display] و [Range]

namespace ERP.Models
{
    /// <summary>
    /// سطر مرتجع البيع:
    /// - كل سطر تابع لهيدر واحد من جدول SalesReturn (عن طريق SRId).
    /// - يمكن ربطه اختيارياً بسطر من فاتورة البيع الأصلية لمعرفة مصدر المرتجع.
    /// - الكمية المرتجعة ترجع المخزن (هتتحول لاحقاً لحركات FIFO).
    /// - PriceRetail: سعر الجمهور الأساسى.
    /// - UnitSalePrice: سعر الوحدة بعد الخصم (قبل الضريبة).
    /// - LineNetTotal: الإجمالى بعد الخصم + الضريبة.
    /// </summary>
    public class SalesReturnLine
    {
        // ===== مفاتيح السطر =====

        [Display(Name = "رقم مرتجع البيع")]            // تعليق: FK على SalesReturn.SRId
        [Required]                                     // تعليق: لازم يكون مرتبط بهيدر
        public int SRId { get; set; }                  // متغير: رقم مرتجع البيع الذى ينتمى له السطر

        [Display(Name = "رقم السطر")]                  // تعليق: رقم السطر داخل نفس المرتجع
        [Required]
        public int LineNo { get; set; }                // متغير: رقم السطر (جزء من الـ Key المركب)

        // ===== ربط اختيارى مع فاتورة البيع الأصلية =====

        [Display(Name = "رقم فاتورة البيع الأصلية")]   // تعليق: FK اختيارى على SalesInvoice.SIId
        public int? SalesInvoiceId { get; set; }       // متغير: رقم فاتورة البيع التى خرج منها هذا المرتجع (إن وُجد)

        [Display(Name = "رقم سطر فاتورة البيع")]       // تعليق: رقم السطر فى فاتورة البيع الأصلية
        public int? SalesInvoiceLineNo { get; set; }   // متغير: رقم السطر فى SalesInvoiceLine (اختيارى)

        // ===== بيانات الصنف والكمية =====

        [Display(Name = "كود الصنف")]                  // تعليق: كود الصنف (FK على Products)
        [Required]
        public int ProdId { get; set; }                // متغير: كود الصنف المرتجع

        [ForeignKey(nameof(ProdId))]
        [Display(Name = "الصنف")]
        public virtual Product? Product { get; set; }

        [Display(Name = "الكمية المرتجعة")]            // تعليق: عدد العلب المرتجعة
        [Range(1, int.MaxValue, ErrorMessage = "الكمية يجب أن تكون أكبر من صفر")]
        public int Qty { get; set; }                   // متغير: الكمية المرتجعة (int بدون كسور)

        // ===== الأسعار والخصومات =====

        [Display(Name = "سعر الجمهور")]               // تعليق: سعر الجمهور وقت البيع (أساس الخصم)
        [Precision(18, 2)]
        [Range(typeof(decimal), "0", "9999999999999999.99",
            ErrorMessage = "قيمة غير صالحة")]
        public decimal PriceRetail { get; set; }       // متغير: سعر الجمهور للوحدة

        [Display(Name = "خصم 1 %")]
        [Precision(5, 2)]
        [Range(typeof(decimal), "0", "100",
            ErrorMessage = "النسبة بين 0 و 100")]
        public decimal Disc1Percent { get; set; }      // متغير: نسبة خصم 1

        [Display(Name = "خصم 2 %")]
        [Precision(5, 2)]
        [Range(typeof(decimal), "0", "100",
            ErrorMessage = "النسبة بين 0 و 100")]
        public decimal Disc2Percent { get; set; }      // متغير: نسبة خصم 2

        [Display(Name = "خصم 3 %")]
        [Precision(5, 2)]
        [Range(typeof(decimal), "0", "100",
            ErrorMessage = "النسبة بين 0 و 100")]
        public decimal Disc3Percent { get; set; }      // متغير: نسبة خصم 3

        [Display(Name = "خصم (قيمة)")]                 // تعليق: خصم بالقيمة على السطر كله
        [Precision(18, 2)]
        [Range(typeof(decimal), "0", "9999999999999999.99",
            ErrorMessage = "قيمة غير صالحة")]
        public decimal DiscountValue { get; set; }     // متغير: قيمة الخصم على السطر

        [Display(Name = "ضريبة %")]                    // تعليق: نسبة الضريبة على هذا السطر
        [Precision(5, 2)]
        [Range(typeof(decimal), "0", "100",
            ErrorMessage = "النسبة بين 0 و 100")]
        public decimal TaxPercent { get; set; }        // متغير: نسبة الضريبة %

        // ===== نواتج الحساب =====

        [Display(Name = "سعر البيع للوحدة")]           // تعليق: بعد الخصم وقبل الضريبة
        [Precision(18, 2)]
        [Range(typeof(decimal), "0", "9999999999999999.99",
            ErrorMessage = "قيمة غير صالحة")]
        public decimal UnitSalePrice { get; set; }     // متغير: سعر الوحدة بعد الخصم (قبل الضريبة)

        [Display(Name = "الإجمالي بعد الخصم (قبل الضريبة)")]
        [Precision(18, 2)]
        [Range(typeof(decimal), "0", "9999999999999999.99",
            ErrorMessage = "قيمة غير صالحة")]
        public decimal LineTotalAfterDiscount { get; set; } // متغير: Qty * UnitSalePrice

        [Display(Name = "قيمة الضريبة")]              // تعليق: LineTotalAfterDiscount * TaxPercent%
        [Precision(18, 2)]
        [Range(typeof(decimal), "0", "9999999999999999.99",
            ErrorMessage = "قيمة غير صالحة")]
        public decimal TaxValue { get; set; }          // متغير: قيمة الضريبة على السطر

        [Display(Name = "الصافي بعد الضريبة")]        // تعليق: بعد الخصم + الضريبة
        [Precision(18, 2)]
        [Range(typeof(decimal), "0", "9999999999999999.99",
            ErrorMessage = "قيمة غير صالحة")]
        public decimal LineNetTotal { get; set; }      // متغير: الصافي النهائى للسطر

        // ===== بيانات التشغيلة / الصلاحية =====

        [Display(Name = "رقم التشغيلة")]              // تعليق: تشغيلة الصنف (إن وُجدت)
        [StringLength(50)]
        public string? BatchNo { get; set; }           // متغير: رقم التشغيلة

        [Display(Name = "تاريخ الصلاحية")]            // تعليق: تاريخ انتهاء الصلاحية
        public DateTime? Expiry { get; set; }          // متغير: تاريخ صلاحية التشغيلة (اختياري)

        // ===== علاقات الملاحة (Navigation Properties) =====

        // ========= الربط مع هيدر المرتجع =========
       
        [ForeignKey(nameof(SRId))]                 // مهم: يربط الـ FK الموجود فعلاً
        [Display(Name = "هيدر مرتجع البيع")]
        public virtual SalesReturn SalesReturn { get; set; } = null!;

        [Display(Name = "فاتورة البيع الأصلية")]
        public virtual SalesInvoice? SalesInvoice { get; set; }            // تعليق: الهيدر الأصلى لبيع الصنف (اختياري)

        [Display(Name = "سطر فاتورة البيع الأصلية")]
        public virtual SalesInvoiceLine? SalesInvoiceLine { get; set; }    // تعليق: سطر البيع الأصلى (اختياري)
    }
}
