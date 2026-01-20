using System;
using System.ComponentModel.DataAnnotations;            // خصائص التحقق من صحة البيانات + Display
using System.ComponentModel.DataAnnotations.Schema;     // خصائص الربط مع قاعدة البيانات

namespace ERP.Models
{
    /// <summary>
    /// إذن استلام نقدية (Cash Receipt)
    /// كل سجل = مستند واحد نستلم فيه مبلغ من عميل/مورد/طرف.
    /// </summary>
    public class CashReceipt
    {
        // ===== المفتاح الأساسي =====
        [Key]   // تعليق: هذا هو المفتاح الأساسي للجدول
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]   // تعليق: رقم يزيد تلقائيًا Identity
        [Display(Name = "رقم الإذن")]
        public int CashReceiptId { get; set; }   // متغير: رقم إذن الاستلام (PK داخلي)

        // ===== بيانات المستند الأساسية =====
        [Required]                                // تعليق: رقم المستند مطلوب
        [StringLength(50)]                        // تعليق: أقصى طول لرقم المستند 50 حرفًا
        [Display(Name = "رقم المستند")]
        public string ReceiptNumber { get; set; } = string.Empty;   // متغير: رقم المستند الظاهر للمستخدم

        [Required]                                // تعليق: تاريخ الإذن مطلوب
        [Display(Name = "تاريخ الإذن")]
        public DateTime ReceiptDate { get; set; }   // متغير: تاريخ إذن الاستلام

        // ===== الطرف (عميل / مورد / غيره) =====
        [Display(Name = "الطرف")]
        public int? CustomerId { get; set; }        // متغير: رقم العميل/الطرف (اختياري)

        [ForeignKey(nameof(CustomerId))]
        public virtual Customer? Customer { get; set; }  // متغير: كائن العميل/الطرف المرتبط بالإذن

        // ===== الحسابات المحاسبية =====

        [Required]
        [Display(Name = "حساب الصندوق / البنك")]
        public int CashAccountId { get; set; }      // متغير: حساب النقدية (صندوق/بنك) الذي يستلم الفلوس

        [ForeignKey(nameof(CashAccountId))]
        public virtual Account CashAccount { get; set; } = null!;  // متغير: كائن حساب الصندوق/البنك

        [Required]
        [Display(Name = "حساب الطرف")]
        public int CounterAccountId { get; set; }   // متغير: الحساب المقابل (حساب العميل/المورد/طرف آخر)

        [ForeignKey(nameof(CounterAccountId))]
        public virtual Account CounterAccount { get; set; } = null!; // متغير: كائن حساب الطرف المقابل

        // ===== المبلغ والبيان =====

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "المبلغ")]
        public decimal Amount { get; set; }    // متغير: قيمة إذن الاستلام

        [StringLength(250)]
        [Display(Name = "البيان")]
        public string? Description { get; set; }  // متغير: بيان/شرح الإذن (عن أي شيء هذا الاستلام؟)

        // ===== الترحيل والحالة =====

        [Display(Name = "الحالة")]
        [StringLength(20)]
        public string Status { get; set; } = "غير مرحلة";   // متغير: حالة الإذن (غير مرحلة / مرحّل / مفتوحة للتعديل)

        [Display(Name = "مرحّل؟")]
        public bool IsPosted { get; set; } = false;      // متغير: هل تم ترحيل الإذن لقيود اليومية؟

        [Display(Name = "تاريخ الترحيل")]
        public DateTime? PostedAt { get; set; }          // متغير: تاريخ ووقت الترحيل (لو تم)

        [StringLength(100)]
        [Display(Name = "مرحّل بواسطة")]
        public string? PostedBy { get; set; }            // متغير: اسم المستخدم الذي قام بالترحيل

        // ===== التتبع (من أنشأ ومتى) =====

        [StringLength(100)]
        [Display(Name = "أنشئ بواسطة")]
        public string? CreatedBy { get; set; }           // متغير: اسم المستخدم الذي أنشأ الإذن

        [Display(Name = "تاريخ الإنشاء")]
        public DateTime CreatedAt { get; set; }          // متغير: تاريخ ووقت إنشاء الإذن

        [Display(Name = "تاريخ آخر تعديل")]
        public DateTime? UpdatedAt { get; set; }         // متغير: تاريخ ووقت آخر تعديل للإذن
    }
}
