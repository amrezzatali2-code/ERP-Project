using System;
using System.ComponentModel.DataAnnotations;           // خصائص Display / Required / StringLength
using System.ComponentModel.DataAnnotations.Schema;    // DatabaseGenerated / Column

namespace ERP.Models
{
    /// <summary>
    /// إذن صرف / دفع نقدية (Cash Payment)
    /// كل سجل = مستند واحد نصرف فيه مبلغ لطرف.
    /// </summary>
    public class CashPayment
    {
        [Key]   // تعليق: هذا هو المفتاح الأساسي للجدول (رقم الإذن)
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]   // تعليق: رقم يزيد تلقائياً Identity
        [Display(Name = "رقم الإذن")]
        public int CashPaymentId { get; set; }   // متغير: رقم إذن الدفع (PK داخلي Identity)

        [Required]                               // تعليق: رقم المستند لابد أن يكون إلزامي
        [StringLength(50)]                       // تعليق: أقصى طول لرقم المستند 50 خانة
        [Display(Name = "رقم المستند")]
        public string PaymentNumber { get; set; } = string.Empty;   // متغير: رقم المستند الظاهر للمستخدم

        [Required]                               // تعليق: تاريخ الإذن إلزامي
        [Display(Name = "تاريخ الإذن")]
        public DateTime PaymentDate { get; set; }   // متغير: تاريخ إذن الدفع

        // ===== الطرف (عميل / مورد / غيره) =====

        [Display(Name = "الطرف")]
        public int? CustomerId { get; set; }        // متغير: رقم العميل/المورد/الطرف (اختياري)

        [ForeignKey(nameof(CustomerId))]
        public virtual Customer? Customer { get; set; }  // متغير: كائن العميل/الطرف المرتبط بالإذن

        // ===== الحسابات المحاسبية =====

        [Required]                               // تعليق: لابد من تحديد حساب الصندوق/البنك
        [Display(Name = "حساب الصندوق / البنك")]
        public int CashAccountId { get; set; }      // متغير: حساب النقدية (الصندوق/البنك) الذي نصرف منه

        [ForeignKey(nameof(CashAccountId))]
        public virtual Account CashAccount { get; set; } = null!;  // متغير: كائن حساب الصندوق/البنك

        [Required]                               // تعليق: لابد من تحديد حساب الطرف المقابل
        [Display(Name = "حساب الطرف")]
        public int CounterAccountId { get; set; }   // متغير: الحساب المقابل (حساب المورد/العميل/طرف آخر)

        [ForeignKey(nameof(CounterAccountId))]
        public virtual Account CounterAccount { get; set; } = null!; // متغير: كائن حساب الطرف

        // ===== المبلغ والبيان =====

        [Column(TypeName = "decimal(18,2)")]     // تعليق: حفظ المبلغ بدقة 2 رقم عشري
        [Display(Name = "المبلغ")]
        public decimal Amount { get; set; }      // متغير: قيمة إذن الدفع

        [StringLength(250)]                      // تعليق: أقصى طول للبيان 250 خانة
        [Display(Name = "البيان")]
        public string? Description { get; set; }  // متغير: بيان/شرح الإذن

        // ===== التواريخ وحالة الترحيل (نفس نظام CashReceipt) =====

        [Display(Name = "تاريخ الإنشاء")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;   // متغير: تاريخ إنشاء الإذن

        [Display(Name = "تاريخ آخر تحديث")]
        public DateTime? UpdatedAt { get; set; }                     // متغير: آخر تاريخ تعديل

        [StringLength(100)]
        [Display(Name = "أنشأه")]
        public string? CreatedBy { get; set; }       // متغير: اسم المستخدم الذي أنشأ الإذن

        [Display(Name = "مرحّل؟")]
        public bool IsPosted { get; set; }           // متغير: هل تم ترحيل الإذن لدفتر الأستاذ؟

        [Display(Name = "تاريخ الترحيل")]
        public DateTime? PostedAt { get; set; }      // متغير: تاريخ الترحيل

        [StringLength(100)]
        [Display(Name = "مرحّل بواسطة")]
        public string? PostedBy { get; set; }        // متغير: اسم المستخدم الذي قام بالترحيل
    }
}
