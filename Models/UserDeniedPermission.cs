using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ERP.Models
{
    /// <summary>
    /// استثناء صلاحية لمستخدم معيّن:
    /// - ممكن نسمح له بصلاحية زيادة عن دوره (IsAllowed = true)
    /// - أو نمنع عنه صلاحية يمتلكها دوره (IsAllowed = false)
    /// </summary>
    [Index(nameof(UserId), nameof(PermissionId), IsUnique = true)]
    public class UserDeniedPermission
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Display(Name = "المعرّف")]
        public int Id { get; set; }           // معرّف السطر

        [Display(Name = "المستخدم")]
        public int UserId { get; set; }       // FK على Users

        [Display(Name = "الصلاحية")]
        public int PermissionId { get; set; } // FK على Permissions

        [Display(Name = "سماح/منع")]
        public bool IsAllowed { get; set; }   // true = سماح استثنائي، false = منع استثنائي

        [Display(Name = "تاريخ الإضافة")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // متى أُضيف الاستثناء

        [ForeignKey(nameof(UserId))]
        public virtual User User { get; set; } = null!;           // كائن المستخدم

        [ForeignKey(nameof(PermissionId))]
        public virtual Permission Permission { get; set; } = null!; // كائن الصلاحية
    }
}
