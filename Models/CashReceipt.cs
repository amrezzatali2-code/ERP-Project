using System;
using System.ComponentModel.DataAnnotations;            // خصائص التحقق من صحة البيانات + Display + Range
using System.ComponentModel.DataAnnotations.Schema;     // خصائص الربط مع قاعدة البيانات
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation; // ✅ ValidateNever لمنع التحقق على Navigation Properties

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
        // ✅ ReceiptNumber سيتم توليده تلقائياً من CashReceiptId بعد الحفظ
        [StringLength(50)]                        // تعليق: أقصى طول لرقم المستند 50 حرفًا
        [Display(Name = "رقم المستند")]
        public string ReceiptNumber { get; set; } = string.Empty;   // متغير: رقم المستند الظاهر للمستخدم

        [Required]                                // تعليق: تاريخ الإذن مطلوب
        [Display(Name = "تاريخ الإذن")]
        public DateTime ReceiptDate { get; set; }   // متغير: تاريخ إذن الاستلام

        // ===== الطرف (عميل / مورد / غيره) =====
        [Display(Name = "الطرف")]
        public int? CustomerId { get; set; }        // متغير: رقم العميل/الطرف (حسب تصميمك الحالي: اختياري)

        [ForeignKey(nameof(CustomerId))]
        [ValidateNever]                             // ✅ تعليق: لا نتحقق من Navigation أثناء البوست (يتم تحميله من DB لاحقاً)
        public virtual Customer? Customer { get; set; }  // متغير: كائن العميل/الطرف المرتبط بالإذن

        // ===== الحسابات المحاسبية =====
        // ✅ مهم جداً:
        // - التحقق يجب أن يكون على الـ IDs (لأن اللي بييجي من الفورم هو Id)
        // - ومنع التحقق على الـ Navigation Properties (CashAccount / CounterAccount)

        [Display(Name = "حساب الصندوق / البنك")]
        [Range(1, int.MaxValue, ErrorMessage = "حساب الصندوق / البنك مطلوب.")] // ✅ يمنع 0 ويعطي رسالة واضحة
        public int CashAccountId { get; set; }      // متغير: حساب النقدية (صندوق/بنك) الذي يستلم الفلوس

        [ForeignKey(nameof(CashAccountId))]
        [ValidateNever]                             // ✅ يمنع ModelState من اعتبار CashAccount مطلوب
        public virtual Account? CashAccount { get; set; }  // متغير: كائن حساب الصندوق/البنك (يتجاب من DB)

        [Display(Name = "حساب الطرف")]
        [Range(1, int.MaxValue, ErrorMessage = "حساب الطرف مطلوب.")] // ✅ يمنع 0 ويعطي رسالة واضحة
        public int CounterAccountId { get; set; }   // متغير: الحساب المقابل

        [ForeignKey(nameof(CounterAccountId))]
        [ValidateNever]                             // ✅ يمنع ModelState من اعتبار CounterAccount مطلوب
        public virtual Account? CounterAccount { get; set; } // متغير: كائن حساب الطرف (يتجاب من DB)

        // ===== المبلغ والبيان =====

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "المبلغ")]
        [Range(0.01, 999999999, ErrorMessage = "المبلغ يجب أن يكون أكبر من صفر.")] // ✅ حماية إضافية
        public decimal Amount { get; set; }    // متغير: قيمة إذن الاستلام

        [StringLength(250)]
        [Display(Name = "البيان")]
        public string? Description { get; set; }  // متغير: بيان/شرح الإذن

        // ===== الترحيل والحالة =====

        [Display(Name = "الحالة")]
        [StringLength(20)]
        public string Status { get; set; } = "غير مرحلة";   // متغير: حالة الإذن

        [Display(Name = "مرحّل؟")]
        public bool IsPosted { get; set; } = false;      // متغير: هل تم ترحيله؟

        [Display(Name = "تاريخ الترحيل")]
        public DateTime? PostedAt { get; set; }          // متغير: تاريخ الترحيل

        [StringLength(100)]
        [Display(Name = "مرحّل بواسطة")]
        public string? PostedBy { get; set; }            // متغير: من رحّل

        // ===== التتبع =====

        [StringLength(100)]
        [Display(Name = "أنشئ بواسطة")]
        public string? CreatedBy { get; set; }           // متغير: من أنشأ

        [Display(Name = "تاريخ الإنشاء")]
        public DateTime CreatedAt { get; set; }          // متغير: وقت الإنشاء

        [Display(Name = "تاريخ آخر تعديل")]
        public DateTime? UpdatedAt { get; set; }         // متغير: آخر تعديل
    }
}
