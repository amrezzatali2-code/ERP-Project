using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ERP.Models
{
    /// <summary>
    /// جدول مجموعات الحوافز للأصناف
    /// مثال: Bonus Group 1 = 1 جنيه لكل علبة، Bonus Group 2 = 2 جنيه لكل علبة ...إلخ
    /// </summary>
    public class ProductBonusGroup
    {
        [Key]                                              // المفتاح الأساسي للمجموعة
        public int ProductBonusGroupId { get; set; }       // رقم مجموعة الحافز

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;   // اسم مجموعة الحافز (Bonus Group 1...)

        [MaxLength(500)]
        public string? Description { get; set; }           // وصف اختياري

        [Precision(18, 2)]
        public decimal BonusAmount { get; set; }           // قيمة الحافز لكل علبة (بالجنيه)

        public bool IsActive { get; set; } = true;         // هل مجموعة الحافز مفعّلة

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // تاريخ الإنشاء
        public DateTime? UpdatedAt { get; set; }                   // آخر تعديل

        // الأصناف التي تتبع هذه المجموعة
        public ICollection<Product> Products { get; set; }
            = new List<Product>();
    }
}
