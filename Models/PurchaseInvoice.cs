using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;   // علشان Precision للـ decimal

namespace ERP.Models
{
    /// <summary>
    /// رأس فاتورة الشراء: هذا المستند هو الذي يؤثر على المخزون والحسابات عند "الترحيل".
    /// </summary>
    public class PurchaseInvoice
    {
        [Display(Name = "رقم الفاتورة")]
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int PIId { get; set; }   // رقم فاتورة الشراء (مفتاح أساسي)

        [Display(Name = "تاريخ الفاتورة")]
        public DateTime PIDate { get; set; }            // تاريخ الفاتورة

        // ===== ربط فاتورة الشراء بالعميل / المورد =====

        [Display(Name = "كود العميل")]
        public int CustomerId { get; set; }             // كود العميل / المورد

        [Display(Name = "العميل")]
        public virtual Customer Customer { get; set; } = null!; // كائن العميل المرتبط بالفاتورة

        // ===== ربط فاتورة الشراء بالمخزن =====

        [Display(Name = "كود المخزن")]
        public int WarehouseId { get; set; }            // كود المخزن

        // ===== مرجع طلب الشراء (اختياري) =====

        [Display(Name = "طلب الشراء المرجعي")]
        public int? RefPRId { get; set; }               // رقم طلب الشراء إن وجِد

        [Display(Name = "طلب الشراء")]
        public virtual PurchaseRequest? RefPurchaseRequest { get; set; }

        // ===== إجماليات الفاتورة (لتوحيد النظام مع فواتير البيع) =====

        [Display(Name = "إجمالي السطور قبل الخصم")]
        [Precision(18, 2)]
        public decimal ItemsTotal { get; set; }         // إجمالي قيمة السطور قبل أي خصومات أو ضرائب

        [Display(Name = "إجمالي الخصم")]
        [Precision(18, 2)]
        public decimal DiscountTotal { get; set; }      // إجمالي الخصم على الفاتورة (سواء من السطور أو خصم رأس الفاتورة)

        [Display(Name = "إجمالي الضريبة")]
        [Precision(18, 2)]
        public decimal TaxTotal { get; set; }           // إجمالي الضريبة المضافة على الفاتورة

        [Display(Name = "صافي الفاتورة")]
        [Precision(18, 2)]
        public decimal NetTotal { get; set; }           // صافي الفاتورة النهائي بعد الخصم والضريبة

        // ===== حالة الفاتورة والترحيل =====

        [Display(Name = "الحالة")]
        public string Status { get; set; } = "Draft";   // الحالة: Draft/Posted/Cancelled

        [Display(Name = "مرحّلة؟")]
        public bool IsPosted { get; set; }              // هل تم ترحيلها للمخزون والحسابات؟

        [Display(Name = "تاريخ الترحيل")]
        public DateTime? PostedAt { get; set; }         // تاريخ الترحيل

        [Display(Name = "مرحّلة بواسطة")]
        public string? PostedBy { get; set; }           // المستخدم الذي قام بالترحيل

        // =========================================================
        // ✅ إضافات "فتح الفاتورة + الترحيل المتعدد" (PostVersion)
        // =========================================================

        [Display(Name = "مرحلة الترحيل")]
        public int PostVersion { get; set; } = 0;       // متغير: 0 قبل أي ترحيل، ثم 1، ثم 2... (للترحيل الثاني والثالث...)

        [Display(Name = "تم فتحها بعد الترحيل؟")]
        public bool WasReopened { get; set; } = false;  // متغير: هل تم فتح الفاتورة بعد أن كانت مُرحّلة؟

        [Display(Name = "تاريخ الفتح")]
        public DateTime? ReopenedAt { get; set; }       // متغير: تاريخ/وقت آخر فتح

        [Display(Name = "فُتحت بواسطة")]
        public string? ReopenedBy { get; set; }         // متغير: المستخدم الذي قام بفتح الفاتورة


        // ===== بيانات الإنشاء والتعديل =====

        [Display(Name = "أنشأها")]
        public string CreatedBy { get; set; } = null!;  // المستخدم الذي أنشأ الفاتورة

        [Display(Name = "تاريخ الإنشاء")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Display(Name = "آخر تعديل")]
        public DateTime? UpdatedAt { get; set; }        // تاريخ آخر تعديل

        // ===== سطور الفاتورة =====

        [Display(Name = "سطور الفاتورة")]
        public virtual ICollection<PILine> Lines { get; set; } = new List<PILine>();
        // كل الأصناف والكميات والأسعار داخل هذه الفاتورة
    }
}
