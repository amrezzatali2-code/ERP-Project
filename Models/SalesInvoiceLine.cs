using Microsoft.EntityFrameworkCore;                  // [Precision] لتحديد دقة الأرقام العشرية
using System;
using System.ComponentModel.DataAnnotations;          // خصائص التحقق من البيانات Display + Required + Range
using System.ComponentModel.DataAnnotations.Schema;   // لو احتجنا خصائص قاعدة البيانات لاحقًا

namespace ERP.Models
{
    /// <summary>
    /// سطر من فاتورة البيع: يحتوي على الصنف والكمية والتسعير والخصومات والضريبة والتشغيلة.
    /// كل القيم النقدية بدقة (18,2) والنِّسب (5,2). الكمية عدد صحيح (int).
    /// </summary>
    public class SalesInvoiceLine
    {
        // ===== مفاتيح السطر =====

        [Display(Name = "رقم الفاتورة")]                 // تعليق: مفتاح أجنبي إلى SalesInvoice.SIId
        [Required]
        public int SIId { get; set; }                     // متغير: رقم الفاتورة التي ينتمي لها السطر

        [Display(Name = "رقم السطر")]
        [Required]
        public int LineNo { get; set; }                   // متغير: رقم السطر داخل نفس الفاتورة (جزء من المفتاح المركب)

        // ===== بيانات الصنف والكمية =====

        [Display(Name = "كود الصنف")]
        [Required]
        public int ProdId { get; set; }                   // متغير: كود الصنف (FK → Product.ProdId)

        [Display(Name = "الكمية")]
        [Range(1, int.MaxValue, ErrorMessage = "الكمية يجب أن تكون أكبر من صفر")]
        public int Qty { get; set; }                      // متغير: الكمية (علب) — عدد صحيح لمنع الكسور

        // ===== التسعير والخصومات (سعر الجمهور هو الأساس) =====

        [Display(Name = "سعر الجمهور")]
        [Precision(18, 2)]                                // تعليق: فلوس بدقة 18,2
        [Range(typeof(decimal), "0", "9999999999999999.99", ErrorMessage = "قيمة غير صالحة")]
        public decimal PriceRetail { get; set; }          // متغير: سعر الجمهور وقت البيع (نفس الاسم في Products)

        [Display(Name = "خصم 1 %")]
        [Precision(5, 2)]                                 // تعليق: نسبة مئوية بين 0 و 100
        [Range(typeof(decimal), "0", "100", ErrorMessage = "النسبة بين 0 و 100")]
        public decimal Disc1Percent { get; set; }         // متغير: خصم 1 (اختياري)

        [Display(Name = "خصم 2 %")]
        [Precision(5, 2)]
        [Range(typeof(decimal), "0", "100", ErrorMessage = "النسبة بين 0 و 100")]
        public decimal Disc2Percent { get; set; }         // متغير: خصم 2 (اختياري)

        [Display(Name = "خصم 3 %")]
        [Precision(5, 2)]
        [Range(typeof(decimal), "0", "100", ErrorMessage = "النسبة بين 0 و 100")]
        public decimal Disc3Percent { get; set; }         // متغير: خصم 3 (اختياري)

        [Display(Name = "خصم (قيمة)")]
        [Precision(18, 2)]
        [Range(typeof(decimal), "0", "9999999999999999.99", ErrorMessage = "قيمة غير صالحة")]
        public decimal DiscountValue { get; set; }        // متغير: خصم بالقيمة على السطر (إن وُجد)

        // ===== نواتج الحساب على مستوى السطر =====

        [Display(Name = "سعر البيع للوحدة (بعد الخصم)")]
        [Precision(18, 2)]
        [Range(typeof(decimal), "0", "9999999999999999.99", ErrorMessage = "قيمة غير صالحة")]
        public decimal UnitSalePrice { get; set; }        // متغير: ناتج تطبيق الخصومات على PriceRetail (قبل الضريبة)

        [Display(Name = "الإجمالي بعد الخصم (قبل الضريبة)")]
        [Precision(18, 2)]
        [Range(typeof(decimal), "0", "9999999999999999.99", ErrorMessage = "قيمة غير صالحة")]
        public decimal LineTotalAfterDiscount { get; set; } // متغير: إجمالي السطر قبل الضريبة = Qty * UnitSalePrice

        [Display(Name = "ضريبة %")]
        [Precision(5, 2)]
        [Range(typeof(decimal), "0", "100", ErrorMessage = "النسبة بين 0 و 100")]
        public decimal TaxPercent { get; set; }           // متغير: نسبة الضريبة على السطر

        [Display(Name = "قيمة الضريبة")]
        [Precision(18, 2)]
        [Range(typeof(decimal), "0", "9999999999999999.99", ErrorMessage = "قيمة غير صالحة")]
        public decimal TaxValue { get; set; }             // متغير: قيمة الضريبة = LineTotalAfterDiscount * TaxPercent%

        [Display(Name = "الصافي")]
        [Precision(18, 2)]
        [Range(typeof(decimal), "0", "9999999999999999.99", ErrorMessage = "قيمة غير صالحة")]
        public decimal LineNetTotal { get; set; }         // متغير: الصافي بعد الخصم + الضريبة

        // ===== بيانات الربحية والخصم المرجّح (Snapshot من FIFO وقت البيع) =====

        [Display(Name = "الخصم المرجّح للشراء %")]
        [Precision(5, 2)]                                 // تعليق: نسبة الخصم الفعلية على الشراء للصنف فى هذا السطر
        public decimal PurchaseDiscountEffective { get; set; }  // متغير: الخصم المرجّح المستخدم لحساب تكلفة السطر (من FIFO / ستوك ليدجر)

        [Display(Name = "تكلفة الوحدة الفعلية")]
        [Precision(18, 2)]
        public decimal CostPerUnit { get; set; }          // متغير: تكلفة الشراء للوحدة بعد الخصم (من FIFO) – ده اللى بنعتبره "سعر التكلفة"

        [Display(Name = "إجمالي التكلفة")]
        [Precision(18, 2)]
        public decimal CostTotal { get; set; }            // متغير: إجمالي تكلفة السطر = مجموع (Qty من كل دخلة × UnitCost من FIFO)

        [Display(Name = "قيمة الربح")]
        [Precision(18, 2)]
        public decimal ProfitValue { get; set; }          // متغير: قيمة الربح = LineNetTotal (صافي البيع) - CostTotal (إجمالي التكلفة)

        [Display(Name = "نسبة الربح %")]
        [Precision(5, 2)]
        public decimal ProfitPercent { get; set; }        // متغير: نسبة الربح = (ProfitValue / LineNetTotal) × 100 (لو الصافي > 0)

        // ===== بيانات التشغيلة (FIFO) =====

        [Display(Name = "رقم التشغيلة")]
        [StringLength(50)]
        public string BatchNo { get; set; } = string.Empty; // متغير: رقم التشغيلة (قد يكون فارغًا إن لم تُحدَّد)

        [Display(Name = "تاريخ الصلاحية")]
        public DateTime? Expiry { get; set; }             // متغير: صلاحية التشغيلة (اختياري)

        // ===== تجميع تشغيلات متعددة لنفس الصنف داخل نفس الفاتورة (اختياري) =====

        [Display(Name = "تجميع تشغيلات (اختياري)")]
        public int? GroupNo { get; set; }                 // متغير: لتجميع أكثر من تشغيلة تحت نفس المنتج (إن استخدمت)

        [Display(Name = "ملاحظات")]
        [StringLength(250)]
        public string? Notes { get; set; }                // متغير: ملاحظات داخل السطر (اختياري)

        // ===== علاقة السطر مع رأس الفاتورة =====

        [Display(Name = "فاتورة البيع")]
        public virtual SalesInvoice SalesInvoice { get; set; } = null!; // متغير: رأس الفاتورة التي ينتمي لها هذا السطر

        // ===== علاقة السطر مع الصنف =====

        [Display(Name = "الصنف")]
        [ForeignKey(nameof(ProdId))] // تعليق: ربط ProdId بـ Product.ProdId
        public virtual Product? Product { get; set; } // متغير: كيان الصنف المرتبط بالسطر (لجلب الاسم/السعر... إلخ)

    }
}
