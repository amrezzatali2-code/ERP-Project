using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;

namespace ERP.Models
{
    /// <summary>
    /// سطر مرتجع شراء: يمثل كمية مرتجعة لصنف معيّن من مورد (من رأس المرتجع)،
    /// مع بيانات التكلفة والتشغيلة والصلاحية.
    /// يمكن ربط السطر بسطر فاتورة الشراء الأصلية (RefPIId + RefPILineNo) للتحقق من الكميات والمرتجعات السابقة.
    /// </summary>
    public class PurchaseReturnLine
    {
        [Display(Name = "رقم مرتجع الشراء")]
        public int PRetId { get; set; }   // رقم المرتجع (FK يربط برأس مرتجع الشراء)

        [Display(Name = "رقم السطر")]
        public int LineNo { get; set; }   // رقم السطر داخل نفس المرتجع (1،2،3، ...)

        [Display(Name = "كود الصنف")]
        public int ProdId { get; set; }   // كود الصنف من جدول Products

        [Display(Name = "الكمية المرتجعة")]
        public int Qty { get; set; }      // الكمية المرتجعة من هذا الصنف

        /// <summary>رقم فاتورة الشراء المرجعية (لربط سطر المرتجع بسطر الفاتورة).</summary>
        [Display(Name = "رقم فاتورة الشراء المرجعية")]
        public int? RefPIId { get; set; }

        /// <summary>رقم سطر الفاتورة المرجعي.</summary>
        [Display(Name = "رقم سطر الفاتورة")]
        public int? RefPILineNo { get; set; }

        [Display(Name = "تكلفة الوحدة")]
        [Precision(18, 4)]
        public decimal UnitCost { get; set; }   // تكلفة شراء الوحدة (بدقة أعلى لاستخدامها في المخزون)

        [Display(Name = "خصم الشراء %")]
        [Precision(5, 2)]
        public decimal PurchaseDiscountPct { get; set; } // نسبة خصم الشراء (للمرجع/التقارير)

        [Display(Name = "سعر الجمهور المرجعي")]
        [Precision(18, 2)]
        public decimal PriceRetail { get; set; }    // سعر الجمهور للصنف (مرجعي)

        [Display(Name = "التشغيلة")]
        public string? BatchNo { get; set; }        // رقم التشغيلة المرتجعة

        [Display(Name = "تاريخ الصلاحية")]
        public DateTime? Expiry { get; set; }       // تاريخ صلاحية التشغيلة

        // ===== Navigation Properties =====

        [Display(Name = "مرتجع الشراء")]
        public virtual PurchaseReturn PurchaseReturn { get; set; } = null!;
        // كل سطر يتبع رأس مرتجع شراء واحد (PurchaseReturn)
        // المفتاح المركب: (PRetId + LineNo)

        [Display(Name = "الصنف")]
        public virtual Product Product { get; set; } = null!;
        // الصنف المرتبط بالسطر من جدول Products
    }
}
