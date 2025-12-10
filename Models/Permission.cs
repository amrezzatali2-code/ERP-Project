using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ERP.Models
{
    /// <summary>
    /// صلاحية واحدة (مثال: SalesInvoices_View أو CashReceipts_Create).
    /// </summary>
    [Index(nameof(Code), IsUnique = true)]   // كود الصلاحية يكون فريد
    public class Permission
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Display(Name = "رقم الصلاحية")]
        public int PermissionId { get; set; }       // PK للصلاحية

        [Required]
        [StringLength(100)]
        [Display(Name = "كود الصلاحية")]
        public string Code { get; set; } = string.Empty;
        // مثال: "Sales.Invoice.View", "Sales.Invoice.Edit"

        [Required]
        [StringLength(150)]
        [Display(Name = "اسم الصلاحية")]
        public string NameAr { get; set; } = string.Empty;  // اسم بالعربي لعرضه في شاشة الصلاحيات

        [Display(Name = "نشطة؟")]
        public bool IsActive { get; set; } = true;         // هل الصلاحية مفعّلة أم معطّلة


        [StringLength(100)]
        [Display(Name = "الموديول")]
        public string? Module { get; set; }        // مثال: Sales / Purchasing / Inventory / HR

        [StringLength(250)]
        [Display(Name = "الوصف")]
        public string? Description { get; set; }   // شرح مختصر لما تعنية الصلاحية

        [Display(Name = "تاريخ الإنشاء")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Display(Name = "آخر تعديل")]
        public DateTime? UpdatedAt { get; set; }

        // ===== العلاقات =====

        [Display(Name = "الأدوار المرتبطة")]
        public virtual ICollection<RolePermission> RolePermissions { get; set; }
            = new List<RolePermission>();         // الأدوار التي تمتلك هذه الصلاحية

    

        // ===== العلاقات (Navigation Properties) =====

        [Display(Name = "استثناءات الصلاحية للمستخدمين")]
        public virtual ICollection<UserDeniedPermission> UserDeniedPermissions { get; set; }
            = new List<UserDeniedPermission>();   // تجميعة استثناءات هذه الصلاحية

        [Display(Name = "مستخدمون لديهم الصلاحية كإضافة")]
        public virtual ICollection<UserExtraPermissions> UserExtraPermissions { get; set; }
    = new List<UserExtraPermissions>();


    }
}
