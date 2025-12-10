using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ERP.Models
{
    /// <summary>
    /// جدول مجموعات الأصناف:
    /// مثال: مجموعة مضادات حيوية، مجموعة فيتامينات، مجموعة إكسسوار... إلخ.
    /// </summary>
    public class ProductGroup
    {
        [Key]                                      // المفتاح الأساسي للمجموعة
        public int ProductGroupId { get; set; }    // رقم مجموعة الأصناف

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty; // اسم المجموعة

        [MaxLength(500)]
        public string? Description { get; set; }   // وصف للمجموعة (اختياري)

        public bool IsActive { get; set; } = true; // تفعيل/إيقاف المجموعة

        // تواريخ الإنشاء والتعديل
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // تاريخ الإنشاء
        public DateTime? UpdatedAt { get; set; }                   // آخر تعديل

        // علاقات ملاحة
        public ICollection<Product> Products { get; set; }
            = new List<Product>();                // الأصناف التي تنتمي لهذه المجموعة

        public ICollection<ProductGroupPolicy> ProductGroupPolicies { get; set; }
            = new List<ProductGroupPolicy>();     // سياسات المجموعات المرتبطة بهذه المجموعة
    }
}
