using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;   // علشان NotMapped

namespace ERP.Models
{
    /// <summary>
    /// سطر أمر البيع — يحدد الصنف والكمية والسعر المتوقع من غير تأثير مخزني مباشر.
    /// المفتاح المركّب: SOId + LineNo.
    /// </summary>
    public class SOLine
    {
        // ===== مفاتيح السطر =====

        [Required]                         // رقم أمر البيع (مفتاح خارجي على الهيدر)
        [Display(Name = "رقم أمر البيع")]
        public int SOId { get; set; }

        [Required]                         // رقم السطر داخل نفس الأمر 1,2,3,...
        [Display(Name = "رقم السطر")]
        public int LineNo { get; set; }

        // ===== بيانات الصنف والكمية =====

        [Required]                         // كود الصنف
        [Display(Name = "كود الصنف")]
        public int ProdId { get; set; }

        [Display(Name = "الكمية المطلوبة")]
        public int QtyRequested { get; set; }            // الكمية المطلوبة

        [Display(Name = "الكمية المحوّلة")]
        public int QtyConverted { get; set; }             // الكمية المحوّلة إلى فاتورة مبيعات (للتحويل الجزئي)

        [StringLength(100)]
        [Display(Name = "مرجع السعر")]
        public string? PriceBasis { get; set; }          // مرجع السعر (سعر جمهور / عرض خاص / قائمة...)

        [Precision(18, 2)]
        [Display(Name = "سعر الجمهور المطلوب")]
        public decimal RequestedRetailPrice { get; set; } // سعر الجمهور المطلوب

        [Precision(5, 2)]
        [Display(Name = "خصم المبيعات %")]
        public decimal SalesDiscountPct { get; set; }     // نسبة خصم المبيعات %

        [Precision(18, 4)]
        [Display(Name = "السعر/التكلفة المتوقعة للوحدة")]
        public decimal ExpectedUnitPrice { get; set; }    // السعر / التكلفة المتوقعة للوحدة

        [StringLength(50)]
        [Display(Name = "التشغيلة المفضّلة")]
        public string? PreferredBatchNo { get; set; }     // التشغيلة المفضّلة (اختياري)

        [Display(Name = "الصلاحية المفضّلة")]
        public DateTime? PreferredExpiry { get; set; }    // الصلاحية المفضّلة (اختياري)

        // ===== حقول التتبع (تاريخ / مستخدم) =====

        [Display(Name = "تاريخ إنشاء السطر")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        // متغير: وقت إنشاء السطر (نقدر نضبطه من الكنترولر وقت إضافة السطر)

        [Display(Name = "آخر تعديل")]
        public DateTime? UpdatedAt { get; set; }
        // متغير: آخر وقت تم تعديل السطر فيه (نعدّله عند أى تعديل فى الكنترولر)

        [Display(Name = "أنشأه المستخدم")]
        [StringLength(100)]
        public string? CreatedBy { get; set; }
        // متغير: اسم/كود المستخدم الذى أضاف السطر (مثلاً UserName أو UserId كنص)

        // ===== قيمة السطر المتوقعة (حقل محسوب لا يُخزَّن في الداتا بيز) =====

        [NotMapped]                                      // لا يتم إنشاء عمود في الجدول
        [Display(Name = "إجمالي السطر المتوقع")]
        public decimal ExpectedLineTotal                 // إجمالي السطر = الكمية * السعر المتوقع
        {
            get => ExpectedUnitPrice * QtyRequested;
        }

        // ===== علاقات الملاحة =====

        [Display(Name = "أمر البيع")]
        public virtual SalesOrder SalesOrder { get; set; } = null!;

        [Display(Name = "الصنف")]
        public virtual Product? Product { get; set; }
    }
}
