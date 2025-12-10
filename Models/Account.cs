using System;                                   // متغيرات التاريخ DateTime
using System.Collections.Generic;              // القوائم Collections
using System.ComponentModel.DataAnnotations;   // خصائص التحقق
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// جدول الحسابات المالية (شجرة الحسابات)
    /// كل صف = حساب مالي له رصيد (عميل، مورد، خزينة، بنك، مبيعات، رأس مال، ...).
    /// </summary>
    public class Account
    {
        [Key]   // تعليق: المفتاح الأساسي للجدول
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Display(Name = "رقم الحساب")]
        public int AccountId { get; set; }          // متغير: رقم داخلي للحساب

        // كود الحساب الظاهر للمستخدم (مثال 1103-001)
        [Required]                                  // تعليق: الكود مطلوب
        [StringLength(50)]                          // تعليق: طول الكود الأقصى 50 حرف
        [Display(Name = "كود الحساب")]
        public string AccountCode { get; set; } = string.Empty;

        // اسم الحساب (مثال: صيدلية الرحمن)
        [Required]                                  // تعليق: الاسم مطلوب
        [StringLength(200)]                         // تعليق: طول الاسم الأقصى 200 حرف
        [Display(Name = "اسم الحساب")]
        public string AccountName { get; set; } = string.Empty;

        [Display(Name = "نوع الحساب")]
        public AccountType AccountType { get; set; }             // نوع الحساب (أصل، التزام، ...)

        [Display(Name = "الحساب الأب")]
        public int? ParentAccountId { get; set; }                // رقم الأب (اختياري)

        [ForeignKey(nameof(ParentAccountId))]
        [Display(Name = "الحساب الأب")]
        public Account? ParentAccount { get; set; }              // كائن الحساب الأب

        [InverseProperty(nameof(ParentAccount))]
        public ICollection<Account> Children { get; set; }
            = new List<Account>();                               // قائمة حسابات الأبناء

        [Display(Name = "مستوى الحساب")]
        public int Level { get; set; }                           // مستوى الحساب في الشجرة

        [Display(Name = "حساب تفصيلي؟")]
        public bool IsLeaf { get; set; }                         // هل يمكن التسجيل عليه؟

        [Display(Name = "نشط؟")]
        public bool IsActive { get; set; } = true;               // هل الحساب مفعّل

        [Display(Name = "ملاحظات")]
        public string? Notes { get; set; }                       // ملاحظات عن الحساب

        [Display(Name = "تاريخ الإنشاء")]
        public DateTime CreatedAt { get; set; }                  // تاريخ إنشاء الحساب

        [Display(Name = "تاريخ آخر تعديل")]
        public DateTime? UpdatedAt { get; set; }                 // تاريخ آخر تعديل

        // ربط الحسابات بالعملاء (لو الحساب مرتبط بعميل/مورد/موظف... الخ)
        public ICollection<Customer> Customers { get; set; }
            = new List<Customer>();                              // العملاء المرتبطين بهذا الحساب
    }

    /// <summary>
    /// أنواع الحسابات المحاسبية الأساسية
    /// </summary>
    public enum AccountType
    {
        Asset = 1,   // أصل
        Liability = 2,   // التزام
        Equity = 3,   // حقوق ملكية
        Revenue = 4,   // إيراد
        Expense = 5    // مصروف
    }
}
