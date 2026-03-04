using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;        // علشان [Key] و [Required] و [Timestamp]
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// هيدر مرتجع البيع:
    /// - مستند مرتجع واحد لعميل من مخزن معيّن.
    /// - فيه إجماليات المرتجع (قبل الخصم / بعد الخصم / الضريبة / الصافي).
    /// - التأثير الحقيقي على المخزون والمحاسبة هيتم عند الترحيل (Posting).
    /// </summary>
    public class SalesReturn
    {
        // ===== المعرّف الأساسي لمرتجع البيع (الكود الذي يراه المستخدم) =====
        [Key]                                                   // مفتاح أساسي للجدول
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]   // رقم متزايد تلقائياً (Identity)
        [Display(Name = "رقم مرتجع البيع")]
        public int SRId { get; set; }                           // رقم مرتجع البيع

        // ===== التاريخ والوقت =====
        [Display(Name = "تاريخ المرتجع")]
        public DateTime SRDate { get; set; }                    // تاريخ المرتجع (يُخزَّن كـ date في الداتا بيز)

        [Display(Name = "وقت المرتجع")]
        public TimeSpan SRTime { get; set; }                    // وقت المرتجع (time) لتتبع دقيق للحركة خلال اليوم

        // ===== العميل والمخزن =====
        [Required]
        [Display(Name = "معرّف العميل")]
        public int CustomerId { get; set; }                     // FK على Customer.CustomerId (int)

        [Required]
        
        [Display(Name = "معرّف المخزن")]
        public int WarehouseId { get; set; } = default!;     // كود المخزن الذي يرجع إليه المرتجع

        // ===== الخصم على مستوى الهيدر =====
        [Display(Name = "خصم الهيدر %")]
        public decimal HeaderDiscountPercent { get; set; }      // نسبة الخصم على مستوى رأس المرتجع

        [Display(Name = "خصم الهيدر (قيمة)")]
        public decimal HeaderDiscountValue { get; set; }        // قيمة الخصم على مستوى رأس المرتجع

        // ===== الإجماليات =====
        [Display(Name = "إجمالي قبل الخصم")]
        public decimal TotalBeforeDiscount { get; set; }        // إجمالي قيم السطور قبل أي خصومات

        [Display(Name = "إجمالي بعد الخصم وقبل الضريبة")]
        public decimal TotalAfterDiscountBeforeTax { get; set; }// بعد الخصم، قبل الضريبة

        [Display(Name = "قيمة الضريبة")]
        public decimal TaxAmount { get; set; }                  // مجموع الضريبة على مستوى المرتجع

        [Display(Name = "الصافي")]
        public decimal NetTotal { get; set; }                   // الصافي = بعد الخصم + الضريبة (للتقارير)

        // ===== حالة المستند والترحيل =====
        [Required]
        [StringLength(20)]
        [Display(Name = "حالة المستند")]
        public string Status { get; set; } = "Draft";           // Draft / Posted / Cancelled

        [Display(Name = "تم الترحيل؟")]
        public bool IsPosted { get; set; }                      // هل تم ترحيل المرتجع للحسابات/المخزون؟

        [Display(Name = "تاريخ الترحيل")]
        public DateTime? PostedAt { get; set; }                 // وقت الترحيل (اختياري)

        [StringLength(50)]
        [Display(Name = "مرحَّل بواسطة")]
        public string? PostedBy { get; set; }                   // اسم/كود المستخدم الذي قام بالترحيل

        // ===== بيانات الإنشاء والتعديل =====
        [Required]
        [StringLength(50)]
        [Display(Name = "أنشأه")]
        public string CreatedBy { get; set; } = default!;       // المستخدم الذي أنشأ المستند

        [Display(Name = "تاريخ الإنشاء")]
        public DateTime CreatedAt { get; set; }                 // وقت إنشاء السجل

        [Display(Name = "آخر تحديث")]
        public DateTime? UpdatedAt { get; set; }                // آخر تعديل على السجل

        // ===== RowVersion للمزامنة ومنع التعارض في التعديل المتوازي =====
        [Timestamp]                                             // خاصية من نوع rowversion / timestamp في SQL Server
        [Display(Name = "رقم المزامنة")]
        public byte[] RowVersion { get; set; } = default!;      // تستخدمها EF للمقارنة عند الـ Update

        [Display(Name = "رقم فاتورة البيع الأصلية")]
        public int? SalesInvoiceId { get; set; }

        [Display(Name = "فاتورة البيع الأصلية")]
        public virtual SalesInvoice? SalesInvoice { get; set; }


        // ===== سطور المرتجع =====
        [Display(Name = "سطور مرتجع البيع")]
        public virtual ICollection<SalesReturnLine> Lines { get; set; }
            = new List<SalesReturnLine>();                      // كل سطور المرتجع (أصناف + كميات + أسعار)

        // ===== علاقات الملاحة مع العميل والمخزن =====
        [Display(Name = "العميل")]
        public virtual Customer Customer { get; set; } = null!; // العميل الذي تم عمل المرتجع له

        [Display(Name = "المخزن")]
        public virtual Warehouse Warehouse { get; set; } = null!; // المخزن المرتبط بالمرتجع
    }
}
