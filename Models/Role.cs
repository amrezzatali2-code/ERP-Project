using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ERP.Models
{
    /// <summary>
    /// دور / مجموعة صلاحيات (سيلز، مشتريات، مدير مبيعات...).
    /// </summary>
    [Index(nameof(Name), IsUnique = true)] // كل دور باسمه يكون فريد
    public class Role
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Display(Name = "رقم الدور")]
        public int RoleId { get; set; }              // PK للدور

        [Required]
        [StringLength(50)]
        [Display(Name = "اسم الدور")]
        public string Name { get; set; } = string.Empty; // مثل: "Sales", "Admin"

        [StringLength(150)]
        [Display(Name = "الوصف")]
        public string? Description { get; set; }     // وصف مختصر لصلاحيات الدور

        [Display(Name = "دور نظامي؟")]
        public bool IsSystemRole { get; set; } = false; // أدوار أساسية لا نغيّرها بسهولة

        [Display(Name = "نشط؟")]
        public bool IsActive { get; set; } = true;   // تفعيل/إيقاف الدور بالكامل

        [Display(Name = "تاريخ الإنشاء")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Display(Name = "آخر تعديل")]
        public DateTime? UpdatedAt { get; set; }

        // ===== العلاقات =====

        [Display(Name = "المستخدمون داخل الدور")]
        public virtual ICollection<UserRole> UserRoles { get; set; }
            = new List<UserRole>();                 // المستخدمين الذين لديهم هذا الدور

        [Display(Name = "صلاحيات الدور")]
        public virtual ICollection<RolePermission> RolePermissions { get; set; }
            = new List<RolePermission>();           // الصلاحيات الأساسية المرتبطة بالدور
    }
}
