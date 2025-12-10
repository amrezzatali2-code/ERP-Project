using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;   // لاستخدام Display

namespace ERP.Models
{
    /// <summary>
    /// سطر طلب الشراء: الكمية المطلوبة ومعلومات سعر مرجعية + التشغيلة/الصالحيّة المقترحة.
    /// لا تأثير مخزني هنا.
    /// </summary>
    public class PRLine
    {
        [Display(Name = "رقم طلب الشراء")]
        public int PRId { get; set; }             // FK رقم طلب الشراء (يربط السطر بالرأس)

        [Display(Name = "رقم السطر")]
        public int LineNo { get; set; }           // رقم السطر داخل الطلب (1،2،3،...)

        [Display(Name = "كود الصنف")]
        public int ProdId { get; set; }           // كود الصنف

        [Display(Name = "الكمية المطلوبة")]
        public int QtyRequested { get; set; }     // الكمية المطلوبة (int كما اتفقنا)

        // مرجع السعر (مثلاً: "آخر شراء" / "سعر الجمهور" … للاسترشاد فقط)
        [Display(Name = "مرجع السعر")]
        public string? PriceBasis { get; set; }

        [Display(Name = "سعر الجمهور المرجعي")]
        [Precision(18, 2)]
        public decimal PriceRetail { get; set; }  // سعر الجمهور المرجعي

        [Display(Name = "خصم الشراء %")]
        [Precision(5, 2)]
        public decimal PurchaseDiscountPct { get; set; } // خصم الشراء %

        [Display(Name = "التكلفة المتوقعة")]
        [Precision(18, 4)]
        public decimal ExpectedCost { get; set; } // التكلفة المتوقعة (بدقة أعلى)

        // تشغيلة وصلاحية مفضلة (اقتراح فقط — قابلة للتعديل عند التحويل لفاتورة)
        [Display(Name = "التشغيلة المفضلة")]
        public string? PreferredBatchNo { get; set; }

        [Display(Name = "الصلاحية المفضلة")]
        public DateTime? PreferredExpiry { get; set; }

        [Display(Name = "الكمية المحوّلة")]
        public int QtyConverted { get; set; }     // ما تم تحويله من هذه الكمية (للتحكم في التحويل الجزئي)

        // ===== Navigation Properties =====

        [Display(Name = "طلب الشراء")]
        public virtual PurchaseRequest PurchaseRequest { get; set; } = null!;
        // كل سطر يتبع طلب شراء واحد
        // المقابل في PurchaseRequest: ICollection<PRLine> Lines

        // (اختياري لاحقاً) ممكن نضيف Navigation للصنف:
        // public virtual Product Product { get; set; } = null!;
    }
}
