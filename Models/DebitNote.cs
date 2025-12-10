using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// إشعار خصم (Debit Note)
    /// إشعار يُسجّل خصمًا على طرف (عميل/مورد) حسب نوع المعاملة.
    /// </summary>
    public class DebitNote
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Display(Name = "رقم الإشعار")]
        public int DebitNoteId { get; set; }   // متغير: رقم إشعار الخصم (PK داخلي Identity)

        [Required]
        [StringLength(50)]
        [Display(Name = "رقم المستند")]
        public string NoteNumber { get; set; } = string.Empty;   // متغير: رقم الإشعار الظاهر للمستخدم

        [Required]
        [Display(Name = "تاريخ الإشعار")]
        public DateTime NoteDate { get; set; }   // متغير: تاريخ الإشعار

        // ===== الطرف (عميل / مورد / غيره) =====

        [Display(Name = "الطرف")]
        public int? CustomerId { get; set; }     // متغير: رقم العميل/الطرف

        [ForeignKey(nameof(CustomerId))]
        public virtual Customer? Customer { get; set; }  // متغير: كائن العميل/الطرف

        // ===== الحسابات =====

        [Required]
        [Display(Name = "حساب الطرف")]
        public int AccountId { get; set; }      // متغير: حساب العميل/المورد في الدليل المحاسبي

        [ForeignKey(nameof(AccountId))]
        public virtual Account Account { get; set; } = null!;  // متغير: كائن حساب الطرف

        [Display(Name = "حساب مقابل (اختياري)")]
        public int? OffsetAccountId { get; set; }              // متغير: الحساب المقابل (مثل: خصم مسموح/خصم مكتسب)

        [ForeignKey(nameof(OffsetAccountId))]
        public virtual Account? OffsetAccount { get; set; }    // متغير: كائن حساب مقابل

        // ===== المبلغ والسبب =====

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "المبلغ")]
        public decimal Amount { get; set; }     // متغير: قيمة الخصم

        [StringLength(100)]
        [Display(Name = "سبب الإشعار")]
        public string? Reason { get; set; }     // متغير: سبب الخصم (تأخير، بضاعة تالفة، ...)

        [StringLength(250)]
        [Display(Name = "البيان")]
        public string? Description { get; set; }  // متغير: بيان تفصيلي أكثر إن لزم

        // ===== التتبع (تاريخ إنشاء / تعديل) =====

        [Display(Name = "تاريخ الإنشاء")]
        public DateTime CreatedAt { get; set; }    // متغير: تاريخ ووقت إنشاء الإشعار

        [Display(Name = "آخر تعديل")]
        public DateTime? UpdatedAt { get; set; }   // متغير: آخر تاريخ ووقت تعديل

        // ===== بيانات المستخدم والترحيل (Posting) =====

        [StringLength(100)]
        [Display(Name = "أنشئ بواسطة")]
        public string? CreatedBy { get; set; }     // متغير: اسم المستخدم الذي أنشأ الإشعار

        [Display(Name = "مرحَّل؟")]
        public bool IsPosted { get; set; }         // متغير: هل الإشعار مرحَّل لدفتر الأستاذ أم لا

        [Display(Name = "تاريخ الترحيل")]
        public DateTime? PostedAt { get; set; }    // متغير: تاريخ ووقت الترحيل الفعلي

        [StringLength(100)]
        [Display(Name = "رحّله بواسطة")]
        public string? PostedBy { get; set; }      // متغير: اسم المستخدم الذي قام بالترحيل
    }
}
