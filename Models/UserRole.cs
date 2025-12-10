using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;  // علشان [ValidateNever]


namespace ERP.Models
{
    /// <summary>
    /// ربط مستخدم بدور (مستخدم واحد يمكن أن يكون له أكثر من دور).
    /// </summary>
    [Index(nameof(UserId), nameof(RoleId), IsUnique = true)]
    public class UserRole
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Display(Name = "المعرّف")]
        public int Id { get; set; }          // معرّف السطر

        [Display(Name = "المستخدم")]
        public int UserId { get; set; }      // FK على Users

        [Display(Name = "الدور")]
        public int RoleId { get; set; }      // FK على Roles

        [Display(Name = "دور افتراضي؟")]
        public bool IsPrimary { get; set; } = false; // هل هذا الدور الرئيسي للمستخدم

        [Display(Name = "تاريخ الإسناد")]
        public DateTime AssignedAt { get; set; } = DateTime.UtcNow; // متى أُسند الدور للمستخدم

        [ForeignKey(nameof(UserId))]
        [ValidateNever]                            // ✅ تجاهل التحقق على الخاصية User
        public virtual User User { get; set; } = null!;    // متغير: كائن المستخدم

        [ForeignKey(nameof(RoleId))]
        [ValidateNever]                            // ✅ تجاهل التحقق على الخاصية Role
        public virtual Role Role { get; set; } = null!;    // متغير: كائن الدور

    }
}
