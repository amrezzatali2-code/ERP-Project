using System;                                  // لو احتجنا تواريخ أو حسابات إضافية لاحقًا
using System.ComponentModel.DataAnnotations;   // خصائص التحقق من صحة البيانات
using Microsoft.EntityFrameworkCore;           // Precision للكسور العشرية

namespace ERP.Models
{
    /// <summary>
    /// سطر تسوية جرد:
    /// يمثل صنف واحد في تسوية معيّنة، مع الكمية قبل وبعد والفارق والتكلفة.
    /// </summary>
    public class StockAdjustmentLine
    {
        [Key]
        [Display(Name = "رقم السطر")]                 // يظهر في الشاشات كـ رقم سطر التسوية
        public int Id { get; set; }                    // متغير: كود السطر (PK)

        [Required]
        [Display(Name = "رقم التسوية")]               // يربط السطر برأس التسوية
        public int StockAdjustmentId { get; set; }     // متغير: FK على StockAdjustment

        [Required]
        [Display(Name = "كود الصنف")]                 // الصنف الذي يتم عمل تسوية له
        public int ProductId { get; set; }             // متغير: FK على Product

        [Display(Name = "كود التشغيلة")]              // ممكن يكون null لو التسوية على مستوى الصنف فقط
        public int? BatchId { get; set; }              // متغير: FK اختياري على Batch

        [Required]
        [Display(Name = "الكمية قبل التسوية")]        // الرصيد في النظام قبل الجرد
        public int QtyBefore { get; set; }             // متغير: الكمية الموجودة في النظام

        [Required]
        [Display(Name = "الكمية بعد التسوية")]        // الرصيد الفعلي بعد الجرد
        public int QtyAfter { get; set; }              // متغير: الكمية الفعلية بعد الجرد

        /// <summary>
        /// الفارق = الكمية بعد - الكمية قبل
        /// موجب = زيادة مخزون ، سالب = عجز في المخزون.
        /// يُحسب في الكود أثناء حفظ السطر.
        /// </summary>
        [Display(Name = "فرق الكمية")]                // لعرض الفرق مباشرة في التقارير
        public int QtyDiff { get; set; }               // متغير: فرق الكمية (After - Before)

        [Precision(18, 4)]
        [Display(Name = "تكلفة الوحدة")]              // تكلفة الصنف وقت التسوية (سعر التكلفة)
        public decimal? CostPerUnit { get; set; }      // متغير: تكلفة الوحدة (اختياري)

        [Precision(18, 2)]
        [Display(Name = "سعر الجمهور")]              // لقطة وقت التسوية (من الصنف أو التشغيلة)
        public decimal? PriceRetail { get; set; }

        [Precision(5, 2)]
        [Display(Name = "الخصم المرجح %")]           // نسبة الخصم المرجّح من المشتريات (قابلة للتعديل في الواجهة)
        public decimal? WeightedDiscountPct { get; set; }

        [Precision(18, 2)]
        [Display(Name = "فرق التكلفة")]               // إجمالي تأثير التسوية على القيمة
        public decimal? CostDiff { get; set; }         // متغير: فرق التكلفة = QtyDiff * CostPerUnit

        [MaxLength(200)]
        [Display(Name = "ملاحظات السطر")]             // ملاحظات خاصة بسطر معين
        public string? Note { get; set; }              // متغير: ملاحظات اختيارية

        // ===== علاقات الملاحة (Navigation Properties) =====

        [Display(Name = "التسوية")]
        public virtual StockAdjustment? StockAdjustment { get; set; }  // رأس التسوية

        [Display(Name = "الصنف")]
        public virtual Product? Product { get; set; }                  // الصنف

        [Display(Name = "التشغيلة")]
        public virtual Batch? Batch { get; set; }                      // التشغيلة (لو مستخدمة)
    }
}
