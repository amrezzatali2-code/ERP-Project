using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace ERP.Models
{
    /// <summary>
    /// إشعار إضافة (Credit Note)
    /// إشعار يُسجّل إضافة على حساب الطرف (عميل/مورد).
    /// </summary>
    public class CreditNote
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Display(Name = "رقم الإشعار / رقم المستند")]
        public int CreditNoteId { get; set; }   // متغير: رقم إشعار الإضافة (PK) — يُستخدم كرقم المستند

        [Required]
        [Display(Name = "تاريخ الإشعار")]
        public DateTime NoteDate { get; set; }   // متغير: تاريخ الإشعار

        // ===== الطرف (عميل / مورد / غيره) =====
        [Display(Name = "الطرف")]
        public int? CustomerId { get; set; }     // متغير: رقم العميل/الطرف

        [ForeignKey(nameof(CustomerId))]
        public virtual Customer? Customer { get; set; }  // متغير: كائن العميل/الطرف

        // ===== الحسابات =====
        // ✅ نفس نمط إذن الاستلام: [Range] على الـ ID و [ValidateNever] على الـ Navigation

        [Display(Name = "حساب الطرف")]
        [Range(1, int.MaxValue, ErrorMessage = "حساب الطرف مطلوب.")]
        public int AccountId { get; set; }      // متغير: حساب العميل/المورد في الدليل المحاسبي

        [ForeignKey(nameof(AccountId))]
        [ValidateNever]
        public virtual Account Account { get; set; } = null!;  // متغير: كائن حساب الطرف

        [Display(Name = "حساب مقابل (اختياري)")]
        public int? OffsetAccountId { get; set; }              // متغير: الحساب المقابل (مثل: إيرادات إضافية، خصم مكتسب)

        [ForeignKey(nameof(OffsetAccountId))]
        [ValidateNever]
        public virtual Account? OffsetAccount { get; set; }    // متغير: كائن حساب مقابل

        // ===== المبلغ والسبب =====

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "المبلغ")]
        public decimal Amount { get; set; }     // متغير: قيمة الإضافة

        [StringLength(100)]
        [Display(Name = "سبب الإشعار")]
        public string? Reason { get; set; }     // متغير: سبب الإضافة

        [StringLength(250)]
        [Display(Name = "البيان")]
        public string? Description { get; set; }  // متغير: بيان تفصيلي أكثر

        // ===== التتبع (تاريخ إنشاء / تعديل) =====

        [Display(Name = "تاريخ الإنشاء")]
        public DateTime CreatedAt { get; set; }    // متغير: تاريخ ووقت إنشاء الإشعار

        [Display(Name = "آخر تعديل")]
        public DateTime? UpdatedAt { get; set; }   // متغير: آخر تاريخ ووقت تعديل

        [StringLength(100)]
        [Display(Name = "أنشئ بواسطة")]
        public string? CreatedBy { get; set; }    // متغير: اسم المستخدم الذي أنشأ الإشعار

        [Display(Name = "مرحَّل؟")]
        public bool IsPosted { get; set; }        // متغير: هل الإشعار مرحَّل للمحاسبة أم لا

        [Display(Name = "تاريخ الترحيل")]
        public DateTime? PostedAt { get; set; }   // متغير: تاريخ ووقت الترحيل

        [StringLength(100)]
        [Display(Name = "رحّله بواسطة")]
        public string? PostedBy { get; set; }     // متغير: اسم المستخدم الذي قام بالترحيل

        /// <summary>
        /// مغلق = لا يمكن التعديل إلا بصلاحية (زر تعديل). يتم الإغلاق تلقائياً عند الحفظ.
        /// </summary>
        [Display(Name = "مغلق")]
        public bool IsLocked { get; set; }        // متغير: هل الإشعار مغلق بعد الحفظ
    }
}
