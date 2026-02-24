// Models/SalesInvoice.cs
// كيان هيدر فاتورة البيع (بدون السطور). جميع الخصائص عليها شروح عربية.

using System;
using System.Collections.Generic;                    // علشان ICollection
using System.ComponentModel.DataAnnotations;        // [Key] / [Required] / [Display] / [Timestamp]
using System.ComponentModel.DataAnnotations.Schema; // [DatabaseGenerated]

namespace ERP.Models
{
    /// <summary>
    /// هيدر فاتورة البيع:
    /// - كل فاتورة لها رقم (SIId) متزايد تلقائياً (Identity) يُستخدم ككود الفاتورة.
    /// - مربوطة بعميل واحد ومخزن واحد، ولها سطور في SalesInvoiceLine.
    /// - تحتوي على ملخصات مالية (إجمالي/خصم/ضريبة/صافي) وحالة الترحيل.
    /// </summary>
    public class SalesInvoice
    {
        // ========== هوية وترقيم ==========
        [Key]                                                   // المفتاح الأساسي للجدول
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]   // رقم متزايد تلقائياً Identity
        [Display(Name = "رقم الفاتورة")]
        public int SIId { get; set; }                           // رقم الفاتورة (هو الكود الذي يراه المستخدم)

        [Display(Name = "سلسلة الترقيم")]
        [StringLength(10)]
        public int? SeriesCode { get; set; }                 // كود السلسلة (اختياري – لو استخدمت سلاسل ترقيم مختلفة)

        [Display(Name = "السنة المالية")]
        [StringLength(4)]
        public string? FiscalYear { get; set; }                 // السنة المالية (اختياري)

        // ========== التاريخ والوقت ==========
        [Display(Name = "تاريخ الفاتورة")]
        [DataType(DataType.Date)]
        public DateTime SIDate { get; set; }                    // تاريخ الفاتورة

        [Display(Name = "وقت الفاتورة")]
        [DataType(DataType.Time)]
        public TimeSpan SITime { get; set; }                    // وقت الفاتورة (للفرز داخل نفس اليوم)

        // ========== مراجع أساسية ==========
        [Display(Name = "معرّف العميل")]
        [Required]                                              // FK ضروري
        public int CustomerId { get; set; }                     // FK إلى Customer.CustomerId (int)

        [Display(Name = "كود المخزن")]           // تعليق عربي: كود المخزن (رقم المخزن)
        public int WarehouseId { get; set; }      // متغير: رقم المخزن (int) يُستخدم كـ FK على جدول المخازن

        [Display(Name = "المخزن")]
        public virtual Warehouse? Warehouse { get; set; }  // متغير: كائن المخزن المرتبط

        // ========== طريقة الدفع ==========
        [Display(Name = "طريقة الدفع")]
        [StringLength(20)]
        public string? PaymentMethod { get; set; }              // نقدي / شبكة / آجل / مختلط

        // ========== خصم الهيدر ==========
        [Display(Name = "خصم الهيدر %")]
        [Range(0, 100, ErrorMessage = "النسبة بين 0 و 100")]
        public decimal HeaderDiscountPercent { get; set; }      // نسبة الخصم على مستوى رأس الفاتورة

        [Display(Name = "خصم الهيدر (قيمة)")]
        public decimal HeaderDiscountValue { get; set; }        // قيمة الخصم على مستوى رأس الفاتورة

        // ========== ملخصات ==========
        [Display(Name = "إجمالي قبل الخصم")]
        public decimal TotalBeforeDiscount { get; set; }        // مجموع السطور قبل أي خصومات

        [Display(Name = "إجمالي بعد الخصم قبل الضريبة")]
        public decimal TotalAfterDiscountBeforeTax { get; set; }// بعد الخصم وقبل الضريبة

        [Display(Name = "قيمة الضريبة")]
        public decimal TaxAmount { get; set; }                  // مجموع الضريبة على الفاتورة

        [Display(Name = "الصافي")]
        public decimal NetTotal { get; set; }                   // الصافي بعد الخصم + الضريبة

        // ========== الحالة والترحيل ==========
        [Display(Name = "الحالة")]
        [Required, StringLength(20)]
        public string Status { get; set; } = "غير مرحلة";           // مسودة / مرحل / ملغى (يجب أن تطابق CHECK constraint: CK_SalesInvoices_Status)

        [Display(Name = "مرحل؟")]
        public bool IsPosted { get; set; }                      // هل الفاتورة مرحّلة للمخزون/الحسابات؟

        [Display(Name = "تاريخ/وقت الترحيل")]
        public DateTime? PostedAt { get; set; }                 // وقت الترحيل

        [Display(Name = "مرحل بواسطة")]
        [StringLength(50)]
        public string? PostedBy { get; set; }                   // المستخدم الذي قام بالترحيل

        // ========== تتبّع ==========
        [Display(Name = "أنشأه")]
        [Required, StringLength(50)]
        public string CreatedBy { get; set; } = null!;          // المستخدم الذي أنشأ الفاتورة

        [Display(Name = "أُنشئت في")]
        public DateTime CreatedAt { get; set; }                 // يُضبط افتراضياً من SQL (SYSDATETIME())

        [Display(Name = "آخر تحديث")]
        public DateTime? UpdatedAt { get; set; }                // آخر تعديل على الفاتورة

        // ========== تزامن — لمنع التعارض ==========
        [Timestamp]                                             // RowVersion في SQL Server
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        // ========== علاقات الملاحة ==========
        [Display(Name = "العميل")]
        public virtual Customer Customer { get; set; } = null!; // Navigation للعميل

        [Display(Name = "سطور الفاتورة")]
        public virtual ICollection<SalesInvoiceLine> Lines { get; set; }
            = new List<SalesInvoiceLine>();                    // سطور فاتورة البيع
    }
}
