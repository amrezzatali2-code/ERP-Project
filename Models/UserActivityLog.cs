using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// سجل نشاط المستخدمين:
    /// كل صف = عملية تمت في النظام (دخول، تعديل، حذف، ترحيل، ...).
    /// </summary>
    public class UserActivityLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Display(Name = "المعرّف")]
        public int Id { get; set; }                // رقم السطر في سجل النشاط

        [Display(Name = "المستخدم")]
        public int? UserId { get; set; }           // FK على Users (ممكن null في حالة فشل تسجيل الدخول مثلاً)

        [ForeignKey(nameof(UserId))]
        public virtual User? User { get; set; }    // كائن المستخدم الذي قام بالعملية

        [Required]
        [Display(Name = "نوع العملية")]
        public UserActionType ActionType { get; set; } // نوع الحدث (Create/Edit/Delete/Login/...)

        [StringLength(100)]
        [Display(Name = "اسم الكيان")]
        public string? EntityName { get; set; }    // اسم الجدول / الموديل (SalesInvoice / Product...)

        [Display(Name = "رقم السجل")]
        public int? EntityId { get; set; }         // رقم السجل داخل الكيان لو متاح

        [Display(Name = "وقت العملية")]
        public DateTime ActionTime { get; set; } = DateTime.UtcNow; // وقت تنفيذ العملية

        [StringLength(500)]
        [Display(Name = "وصف مختصر")]
        public string? Description { get; set; }   // شرح بالعربي لما حدث

        [Column(TypeName = "nvarchar(max)")]
        [Display(Name = "القيم القديمة (JSON)")]
        public string? OldValues { get; set; }     // القيم قبل التعديل (لو محتاج مستوى أعلى من التتبع)

        [Column(TypeName = "nvarchar(max)")]
        [Display(Name = "القيم الجديدة (JSON)")]
        public string? NewValues { get; set; }     // القيم بعد التعديل

        [StringLength(50)]
        [Display(Name = "عنوان الـ IP")]
        public string? IpAddress { get; set; }     // IP للجهاز

        [StringLength(200)]
        [Display(Name = "المتصفح / الجهاز")]
        public string? UserAgent { get; set; }     // نوع المتصفح/الجهاز المستخدم
    }

    /// <summary>
    /// أنواع العمليات المسجلة في سجل النشاط.
    /// </summary>
    public enum UserActionType
    {
        Login = 1,      // تسجيل دخول
        Logout = 2,     // تسجيل خروج
        Create = 3,     // إنشاء سجل جديد
        Edit = 4,       // تعديل
        Delete = 5,     // حذف
        Post = 6,       // ترحيل مستند
        Unpost = 7,     // إلغاء ترحيل
        Export = 8,     // تصدير (Excel / PDF)
        Import = 9,     // استيراد بيانات
        View = 10,      // عرض بيانات (مثلاً فتح فاتورة)
        Other = 99      // أى عملية أخرى عامة
    }
}
