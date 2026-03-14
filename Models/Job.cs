using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// جدول الوظائف (المسميات الوظيفية) — للاختيار من القائمة في بيانات الموظف.
    /// </summary>
    public class Job
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Display(Name = "كود الوظيفة")]
        public int Id { get; set; }

        [Required, StringLength(100)]
        [Display(Name = "الوظيفة / المسمى")]
        public string Name { get; set; } = null!;

        [StringLength(20)]
        [Display(Name = "الكود")]
        public string? Code { get; set; }

        [Display(Name = "ترتيب العرض")]
        public int SortOrder { get; set; }

        [Display(Name = "فعال")]
        public bool IsActive { get; set; } = true;

        [Display(Name = "تاريخ الإنشاء")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Display(Name = "تاريخ آخر تعديل")]
        public DateTime? UpdatedAt { get; set; }

        public ICollection<Employee> Employees { get; set; } = new List<Employee>();
    }
}
