using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ERP.Models
{
    /// <summary>
    /// رأس مرتجع شراء: يعكس خروج بضاعة عائدة للمورد.
    /// التأثير على المخزون والمحاسبة يكون عند "الترحيل".
    /// </summary>
    public class PurchaseReturn
    {
        [Display(Name = "رقم مرتجع الشراء")]
        public int PRetId { get; set; } 

        [Display(Name = "تاريخ المرتجع")]
        public DateTime PRetDate { get; set; }           // تاريخ المرتجع

        // ========= ربط مرتجع الشراء بالعميل =========

        [Display(Name = "كود العميل")]
        public int CustomerId { get; set; } 

        [Display(Name = "العميل")]
        public virtual Customer Customer { get; set; } = null!; // كائن العميل المرتبط بمرتجع الشراء

        // ========= ربط مرتجع الشراء بالمخزن (هنديره لاحقاً مع جدول المخازن) =========

        [Display(Name = "كود المخزن")]
        public int WarehouseId { get; set; } 

        // ========= مرجع فاتورة الشراء (اختياري) =========

        [Display(Name = "فاتورة الشراء المرجعية")]
        public int? RefPIId { get; set; }             // رقم فاتورة شراء إن وُجد (لمطابقة ما تم شراؤه)

        [Display(Name = "فاتورة الشراء")]
        public virtual PurchaseInvoice? RefPurchaseInvoice { get; set; } // الفاتورة المرتبط بها المرتجع (اختياري)


        [Display(Name = "إجمالي السطور قبل الخصم")]
        [Precision(18, 2)]
        public decimal ItemsTotal { get; set; }      // مجموع (الكمية × تكلفة الوحدة)

        [Display(Name = "إجمالي الخصم")]
        [Precision(18, 2)]
        public decimal DiscountTotal { get; set; }   // مجموع خصم الشراء على السطور

        [Display(Name = "إجمالي الضريبة")]
        [Precision(18, 2)]
        public decimal TaxTotal { get; set; }        // ضريبة (حاليًا ممكن نخليها 0 لحد ما نستخدمها)

        [Display(Name = "صافي المرتجع")]
        [Precision(18, 2)]
        public decimal NetTotal { get; set; }        // الصافي = إجمالي - خصم + ضريبة

        // ========= حالة المرتجع والترحيل =========

        [Display(Name = "الحالة")]
        public string Status { get; set; } = "Draft";    // Draft/Posted/Cancelled

        [Display(Name = "مرحّل؟")]
        public bool IsPosted { get; set; }               // تم ترحيله؟ (تأثير على المخزون والحسابات)

        [Display(Name = "تاريخ الترحيل")]
        public DateTime? PostedAt { get; set; }          // تاريخ الترحيل

        [Display(Name = "مرحّل بواسطة")]
        public string? PostedBy { get; set; }            // المستخدم الذي قام بالترحيل

        // ========= بيانات الإنشاء والتعديل =========

        [Display(Name = "أنشأه")]
        public string CreatedBy { get; set; } = null!;   // المستخدم الذي أنشأ المرتجع

        [Display(Name = "تاريخ الإنشاء")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // أُنشئ في

        [Display(Name = "تاريخ آخر تحديث")]
        public DateTime? UpdatedAt { get; set; }         // آخر تحديث

        // ========= سطور المرتجع =========

        [Display(Name = "سطور مرتجع الشراء")]
        public virtual ICollection<PurchaseReturnLine> Lines { get; set; }
            = new List<PurchaseReturnLine>();           // كل الأصناف والكميات المرتجعة داخل هذا المرتجع
    }
}
