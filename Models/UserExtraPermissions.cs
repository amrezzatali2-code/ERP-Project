using System;                                    // متغيرات التاريخ DateTime
using System.ComponentModel.DataAnnotations;     // خصائص Display و Key
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ERP.Models
{
    /// <summary>
    /// صلاحيات إضافية لمستخدم معيّن:
    /// تُستخدم لإضافة صلاحيات زيادة فوق الأدوار الأساسية للمستخدم.
    /// مثال: مستخدم سيلز عادي ونضيف له السماح بفتح شاشة تقارير خاصة.
    /// </summary>
    [Index(nameof(UserId), nameof(PermissionId), IsUnique = true)]
    public class UserExtraPermissions
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Display(Name = "المعرّف")]
        public int Id { get; set; }                 // متغير: رقم داخلي لكل سطر

        [Display(Name = "المستخدم")]
        public int UserId { get; set; }             // متغير: رقم المستخدم (FK على جدول Users)

        [Display(Name = "الصلاحية")]
        public int PermissionId { get; set; }       // متغير: رقم/كود الصلاحية (FK على جدول Permissions)

        [Display(Name = "تاريخ الإضافة")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        // متغير: وقت إضافة هذه الصلاحية الإضافية للمستخدم

        // ===== العلاقات (Navigation Properties) =====

        [ForeignKey(nameof(UserId))]
        public virtual User User { get; set; } = null!;
        // متغير: كائن المستخدم المرتبط بهذه الصلاحية الإضافية

        [ForeignKey(nameof(PermissionId))]
        public virtual Permission Permission { get; set; } = null!;
        // متغير: كائن الصلاحية التي تمت إضافتها للمستخدم
    }
}
