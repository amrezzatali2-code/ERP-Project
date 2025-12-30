using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// جدول قيود اليومية / دفتر الأستاذ العام
    /// كل صف = سطر واحد في القيد (حساب واحد مدين أو دائن).
    /// </summary>
    public class LedgerEntry
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Display(Name = "المعرّف")]
        public int Id { get; set; }   // متغير: رقم السطر في دفتر الأستاذ (PK داخلي)

        // ===== بيانات القيد الأساسية =====

        [Required]
        [Display(Name = "تاريخ القيد")]
        public DateTime EntryDate { get; set; }  // متغير: تاريخ القيد المحاسبي (تاريخ الفاتورة / الحركة)

        [Required]
        [Display(Name = "نوع المستند")]
        public LedgerSourceType SourceType { get; set; } // متغير: نوع المصدر (فاتورة، إيصال، تسوية، ...)

        [StringLength(50)]
        [Display(Name = "رقم المستند")]
        public string? VoucherNo { get; set; }   // متغير: رقم المستند الظاهر للمستخدم (رقم فاتورة/إيصال)

        [Display(Name = "معرّف المصدر")]
        public int? SourceId { get; set; }       // متغير: رقم السجل في جدول المصدر (SalesInvoiceId مثلاً)

        [Display(Name = "رقم السطر داخل القيد")]
        public int LineNo { get; set; }          // متغير: ترتيب السطر داخل نفس القيد

        [Display(Name = "مرحلة الترحيل")]
        public int PostVersion { get; set; } = 0; // متغير: رقم مرحلة الترحيل لنفس المصدر (1/2/3...) لسهولة عكس القيود بدقة


        // ===== الحساب المحاسبي =====

        [Required]
        [Display(Name = "رقم الحساب")]
        public int AccountId { get; set; }       // متغير: FK على جدول Accounts

        [ForeignKey(nameof(AccountId))]
        [Display(Name = "الحساب")]
        public virtual Account Account { get; set; } = null!; // متغير: كائن الحساب المرتبط

        // ===== العميل / الطرف (اختياري) =====

        [Display(Name = "العميل / الطرف")]
        public int? CustomerId { get; set; }     // متغير: FK اختياري على جدول Customers (عميل / مورد / طرف آخر)

        [ForeignKey(nameof(CustomerId))]
        public virtual Customer? Customer { get; set; }       // متغير: كائن العميل / الطرف

        // ===== المبالغ =====

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "مدين")]
        public decimal Debit { get; set; }       // متغير: قيمة المبلغ المدين

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "دائن")]
        public decimal Credit { get; set; }      // متغير: قيمة المبلغ الدائن

        [StringLength(250)]
        [Display(Name = "البيان")]
        public string? Description { get; set; } // متغير: شرح السطر (مثال: "فاتورة بيع رقم 123")

        // ===== التتبع الزمني (النظام الموحد) =====

        [Display(Name = "تاريخ الإنشاء")]
        public DateTime CreatedAt { get; set; } = DateTime.Now; // متغير: تاريخ ووقت إنشاء السطر في الدفتر

        [Display(Name = "آخر تعديل")]
        public DateTime? UpdatedAt { get; set; }                // متغير: آخر تعديل على السطر (إن وجد)
    }

    /// <summary>
    /// أنواع مصادر القيود المحاسبية (Opening / Sales / Purchase / Receipt / Payment / ...).
    /// نخزنها كرقم في قاعدة البيانات.
    /// </summary>
    public enum LedgerSourceType
    {
        // 1) أرصدة افتتاحية
        Opening = 1,            // رصيد افتتاحي للحسابات

        // 2) المبيعات
        SalesInvoice = 2,       // فاتورة بيع
        SalesReturn = 3,        // مرتجع بيع

        // 3) المشتريات
        PurchaseInvoice = 4,    // فاتورة شراء
        PurchaseReturn = 5,     // مرتجع شراء

        // 4) الخزينة (قبض / دفع نقدي أو بنك)
        Receipt = 6,            // إيصال قبض
        Payment = 7,            // إيصال دفع

        // 5) قيود يدوية
        Journal = 8,            // قيد يومية يدوي

        // 6) تسويات محاسبية عامة
        Adjustment = 9,         // تسوية / قيد تسوية عام

        // 7) حركات مخزون لها تأثير محاسبي
        StockTransfer = 10,     // تحويل بين المخازن (لو مربوط بالحسابات)
        StockAdjustment = 11,   // تسوية جرد المخزون (زيادة / عجز)

        // 8) إشعارات خصم / إضافة
        DebitNote = 12,         // إشعار خصم
        CreditNote = 13         // إشعار إضافة
    }
}
