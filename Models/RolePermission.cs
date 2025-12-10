using System;                                   // متغيرات الوقت DateTime
using System.ComponentModel.DataAnnotations;    // خواص العرض Display
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;            // خاصية Index

namespace ERP.Models
{
    /// <summary>
    /// ربط دور بصلاحية واحدة.
    /// كل سطر = الدور Role يملك صلاحية Permission معيّنة (مسموح أو لا).
    /// </summary>
    [Index(nameof(RoleId), nameof(PermissionId), IsUnique = true)]
    public class RolePermission
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Display(Name = "المعرّف")]
        public int Id { get; set; }                 // متغير: رقم داخلي للسطر (Primary Key)

        [Display(Name = "الدور")]
        public int RoleId { get; set; }             // متغير: رقم الدور (FK على جدول Roles)

        [Display(Name = "الصلاحية")]
        public int PermissionId { get; set; }       // متغير: رقم الصلاحية (FK على جدول Permissions)

        [Display(Name = "مسموح؟")]
        public bool IsAllowed { get; set; } = true; // متغير: هل هذه الصلاحية مسموحة لهذا الدور؟

        [Display(Name = "تاريخ الإنشاء")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        // متغير: تاريخ ووقت إنشاء الربط بين الدور والصلاحية

        [Display(Name = "آخر تعديل")]
        public DateTime? UpdatedAt { get; set; }
        // متغير: آخر تاريخ تم فيه تعديل الربط (إن وُجد تعديل)

        [ForeignKey(nameof(RoleId))]
        public virtual Role Role { get; set; } = null!;
        // متغير: الكائن الملازم للدور لسهولة العرض في الواجهات

        [ForeignKey(nameof(PermissionId))]
        public virtual Permission Permission { get; set; } = null!;
        // متغير: الكائن الملازم للصلاحية لسهولة العرض في الواجهات
    }
}
