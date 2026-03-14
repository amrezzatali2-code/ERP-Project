using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// جدول الموظفين — بيانات الموظف الكاملة (اسم، قسم، وظيفة، تواريخ، راتب، اتصال، إلخ).
    /// </summary>
    public class Employee
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Display(Name = "كود الموظف")]
        public int Id { get; set; }

        [Required, StringLength(100)]
        [Display(Name = "الاسم بالكامل")]
        public string FullName { get; set; } = null!;

        [StringLength(20)]
        [Display(Name = "كود الموظف")]
        public string? Code { get; set; }

        [StringLength(20)]
        [Display(Name = "الرقم القومي")]
        public string? NationalId { get; set; }

        [Display(Name = "تاريخ الميلاد")]
        [DataType(DataType.Date)]
        public DateTime? BirthDate { get; set; }

        [Display(Name = "تاريخ التعيين")]
        [DataType(DataType.Date)]
        public DateTime? HireDate { get; set; }

        [Display(Name = "القسم")]
        public int? DepartmentId { get; set; }

        [ForeignKey(nameof(DepartmentId))]
        public virtual Department? Department { get; set; }

        [Display(Name = "الوظيفة / المسمى")]
        public int? JobId { get; set; }

        [ForeignKey(nameof(JobId))]
        public virtual Job? Job { get; set; }

        [StringLength(20)]
        [Display(Name = "هاتف 1")]
        public string? Phone1 { get; set; }

        [StringLength(20)]
        [Display(Name = "هاتف 2")]
        public string? Phone2 { get; set; }

        [StringLength(100)]
        [Display(Name = "البريد الإلكتروني")]
        [EmailAddress]
        public string? Email { get; set; }

        [StringLength(300)]
        [Display(Name = "العنوان")]
        public string? Address { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "الراتب الأساسي")]
        public decimal? BaseSalary { get; set; }

        [Display(Name = "نشط")]
        public bool IsActive { get; set; } = true;

        [StringLength(500)]
        [Display(Name = "ملاحظات")]
        public string? Notes { get; set; }

        /// <summary>ربط اختياري بحساب مستخدم (لتسجيل الدخول).</summary>
        [Display(Name = "حساب المستخدم")]
        public int? UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public virtual User? User { get; set; }

        [Display(Name = "تاريخ الإنشاء")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Display(Name = "تاريخ آخر تعديل")]
        public DateTime? UpdatedAt { get; set; }
    }
}
