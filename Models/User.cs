using System;
using System.Collections.Generic;                    // القوائم List
using System.ComponentModel.DataAnnotations;        // الخصائص مثل Required, StringLength
using System.ComponentModel.DataAnnotations.Schema; // DatabaseGenerated / ForeignKey
using Microsoft.EntityFrameworkCore;                // Index


namespace ERP.Models
{
    /// <summary>
    /// مستخدم النظام (يوزر يدخل على البرنامج).
    /// </summary>
    [Index(nameof(UserName), IsUnique = true)]  // فهرس على اسم الدخول (يكون فريد)
    public class User
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Display(Name = "رقم المستخدم")]
        public int UserId { get; set; }                // متغير: PK للمستخدم

        [Required]
        [StringLength(50)]
        [Display(Name = "اسم الدخول")]
        public string UserName { get; set; } = string.Empty; // متغير: اسم الدخول (Login)

        
        [StringLength(150)]
        [Display(Name = "الاسم المعروض")]
        public string DisplayName { get; set; } = string.Empty; // متغير: اسم الموظف كما يظهر في التقارير

        [Required]
        [StringLength(256)]
        [Display(Name = "كلمة المرور (مشفرة)")]
        public string PasswordHash { get; set; } = string.Empty; // متغير: تخزين الباسورد كمشفّر فقط

        [StringLength(150)]
        [Display(Name = "البريد الإلكتروني")]
        public string? Email { get; set; }                     // متغير: بريد اختياري

        [Display(Name = "أدمن؟")]
        public bool IsAdmin { get; set; } = false;             // متغير: هل المستخدم مدير نظام؟

        [Display(Name = "نشط؟")]
        public bool IsActive { get; set; } = true;             // متغير: هل المستخدم مفعل أم لا

        [Display(Name = "تاريخ الإنشاء")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // متغير: وقت إنشاء المستخدم

        [Display(Name = "آخر تعديل")]
        public DateTime? UpdatedAt { get; set; }              // متغير: آخر تعديل على بياناته

        [Display(Name = "آخر تسجيل دخول")]
        public DateTime? LastLoginAt { get; set; }            // متغير: آخر مرة دخل فيها على السيستم

        [StringLength(50)]
        [Display(Name = "أنشئ بواسطة")]
        public string? CreatedBy { get; set; }                // متغير: مين عمل اليوزر ده (اسم مستخدم)

        // ===== ربط المستخدم بجدول العملاء (طرف عام: عميل / موظف / مورد / مستثمر) =====

        [Display(Name = "الطرف المرتبط")]
        public int? CustomerId { get; set; }                  // متغير: كود العميل/الطرف المرتبط بهذا المستخدم (اختياري)

        [ForeignKey(nameof(CustomerId))]
        [Display(Name = "بيانات الطرف (عميل / موظف / مورد / مستثمر)")]
        public virtual Customer? Customer { get; set; }       // متغير: كائن العميل/الطرف المرتبط (لو موجود)

        // ===== العلاقات (Navigation Properties) =====

        [Display(Name = "الأدوار المرتبطة")]
        public virtual ICollection<UserRole> UserRoles { get; set; }
            = new List<UserRole>();                          // متغير: الأدوار التي ينتمي لها هذا المستخدم

        [Display(Name = "استثناءات الصلاحيات")]
        public virtual ICollection<UserDeniedPermission> PermissionOverrides { get; set; }
            = new List<UserDeniedPermission>();              // متغير: صلاحيات منقوصة من هذا المستخدم

        [Display(Name = "سجل النشاط")]
        public virtual ICollection<UserActivityLog> ActivityLogs { get; set; }
            = new List<UserActivityLog>();                   // متغير: كل الحركات التي قام بها المستخدم

        [Display(Name = "صلاحيات إضافية")]
        public virtual ICollection<UserExtraPermissions> ExtraPermissions { get; set; }
            = new List<UserExtraPermissions>();              // متغير: كل الصلاحيات الزيادة عن الأدوار


        // ===== خصائص للعرض فقط (لا تُخزَّن في قاعدة البيانات) =====

        [NotMapped]
        [Display(Name = "الأدوار")]
        public string RolesSummary
        {
            get
            {
                // لو المستخدم ملوش أدوار
                if (UserRoles == null || UserRoles.Count == 0)
                    return "لا يوجد دور";

                // تجميع أسماء الأدوار بدون تكرار
                var names = new HashSet<string>();    // متغير: مجموعة لمنع التكرار
                foreach (var ur in UserRoles)
                {
                    var roleName = ur.Role?.Name;     // متغير: اسم الدور المرتبط
                    if (!string.IsNullOrWhiteSpace(roleName))
                        names.Add(roleName);
                }

                // ضم الأسماء في نص واحد مفصول بفاصلة عربية
                return string.Join("، ", names);
            }
        }

    }
}
