using Microsoft.EntityFrameworkCore;                  // [Precision]
using System;
using System.ComponentModel.DataAnnotations;

namespace ERP.Models
{
    /// <summary>
    /// سطر من فاتورة البيع: يحتوي على الصنف والكمية والتسعير والخصومات والضريبة والتشغيلة.
    /// كل القيم النقدية بدقة (18,2) والنِّسب (5,2). الكمية عدد صحيح (int).
    /// </summary>
    public class SalesInvoiceLine
    {
        // ===== مفاتيح السطر =====
        [Display(Name = "رقم الفاتورة")]                 // FK إلى SalesInvoice.SIId
        [Required]
        public int SIId { get; set; }

        [Display(Name = "رقم السطر")]
        [Required]
        public int LineNo { get; set; }                   // رقم السطر داخل نفس الفاتورة (مفتاح مركب)

        // ===== بيانات الصنف والكمية =====
        [Display(Name = "كود الصنف")]
        [Required]
        public int ProdId { get; set; }

        [Display(Name = "الكمية")]
        [Range(1, int.MaxValue, ErrorMessage = "الكمية يجب أن تكون أكبر من صفر")]
        public int Qty { get; set; }                      // الكمية (علب) — عدد صحيح لمنع الكسور

        // ===== التسعير والخصومات (سعر الجمهور هو الأساس) =====
        [Display(Name = "سعر الجمهور")]
        [Precision(18, 2)]                                // فلوس بدقة 18,2
        [Range(typeof(decimal), "0", "9999999999999999.99", ErrorMessage = "قيمة غير صالحة")]
        public decimal PriceRetail { get; set; }          // سعر الجمهور وقت البيع (نفس الاسم في Products)

        [Display(Name = "خصم 1 %")]
        [Precision(5, 2)]                                 // نسبة % بدقة 5,2
        [Range(typeof(decimal), "0", "100", ErrorMessage = "النسبة بين 0 و 100")]
        public decimal Disc1Percent { get; set; }         // خصم 1 (اختياري)

        [Display(Name = "خصم 2 %")]
        [Precision(5, 2)]
        [Range(typeof(decimal), "0", "100", ErrorMessage = "النسبة بين 0 و 100")]
        public decimal Disc2Percent { get; set; }         // خصم 2 (اختياري)

        [Display(Name = "خصم 3 %")]
        [Precision(5, 2)]
        [Range(typeof(decimal), "0", "100", ErrorMessage = "النسبة بين 0 و 100")]
        public decimal Disc3Percent { get; set; }         // خصم 3 (اختياري)

        [Display(Name = "خصم (قيمة)")]
        [Precision(18, 2)]
        [Range(typeof(decimal), "0", "9999999999999999.99", ErrorMessage = "قيمة غير صالحة")]
        public decimal DiscountValue { get; set; }        // خصم بالقيمة على السطر (إن وُجد)

        // ===== نواتج الحساب على مستوى السطر =====
        [Display(Name = "سعر البيع للوحدة (بعد الخصم)")]
        [Precision(18, 2)]
        [Range(typeof(decimal), "0", "9999999999999999.99", ErrorMessage = "قيمة غير صالحة")]
        public decimal UnitSalePrice { get; set; }        // ناتج تطبيق الخصومات على PriceRetail (قبل الضريبة)

        [Display(Name = "الإجمالي بعد الخصم (قبل الضريبة)")]
        [Precision(18, 2)]
        [Range(typeof(decimal), "0", "9999999999999999.99", ErrorMessage = "قيمة غير صالحة")]
        public decimal LineTotalAfterDiscount { get; set; } // Qty * UnitSalePrice

        [Display(Name = "ضريبة %")]
        [Precision(5, 2)]
        [Range(typeof(decimal), "0", "100", ErrorMessage = "النسبة بين 0 و 100")]
        public decimal TaxPercent { get; set; }           // نسبة الضريبة على السطر

        [Display(Name = "قيمة الضريبة")]
        [Precision(18, 2)]
        [Range(typeof(decimal), "0", "9999999999999999.99", ErrorMessage = "قيمة غير صالحة")]
        public decimal TaxValue { get; set; }             // قيمة الضريبة = LineTotalAfterDiscount * TaxPercent%

        [Display(Name = "الصافي")]
        [Precision(18, 2)]
        [Range(typeof(decimal), "0", "9999999999999999.99", ErrorMessage = "قيمة غير صالحة")]
        public decimal LineNetTotal { get; set; }         // الصافي بعد الخصم + الضريبة

        // ===== بيانات التشغيلة (FIFO) =====
        [Display(Name = "رقم التشغيلة")]
        [StringLength(50)]
        public string BatchNo { get; set; } = string.Empty; // قد يكون فارغًا إن لم تُحدَّد

        [Display(Name = "تاريخ الصلاحية")]
        public DateTime? Expiry { get; set; }            // صلاحية التشغيلة (اختياري)

        // ===== تجميع تشغيلات متعددة لنفس الصنف داخل نفس الفاتورة (اختياري) =====
        [Display(Name = "تجميع تشغيلات (اختياري)")]
        public int? GroupNo { get; set; }                // لتجميع أكثر من تشغيلة تحت نفس المنتج (إن استخدمت)

        [Display(Name = "ملاحظات")]
        [StringLength(250)]
        public string? Notes { get; set; }               // ملاحظات داخل السطر (اختياري)

        // ===== علاقة السطر مع رأس الفاتورة =====
        [Display(Name = "فاتورة البيع")]
        public virtual SalesInvoice SalesInvoice { get; set; } = null!; // رأس الفاتورة التي ينتمي لها هذا السطر

    }
}
