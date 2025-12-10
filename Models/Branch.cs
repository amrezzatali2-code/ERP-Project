using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// جدول الفروع Branches
    /// </summary>
    public class Branch
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Display(Name = "كود الفرع")]             // رقم الفرع (مفتاح أساسي)
        public int BranchId { get; set; }         // متغير: رقم الفرع

        // اسم الفرع
        [Required, StringLength(200)]
        [Display(Name = "اسم الفرع")]
        public string BranchName { get; set; } = null!;  // متغير: اسم الفرع

        // تاريخ الإنشاء
        [Display(Name = "تاريخ الإنشاء")]
        public DateTime? CreatedAt { get; set; }        // متغير: وقت إنشاء السجل

        // آخر تعديل
        [Display(Name = "آخر تعديل")]
        public DateTime? UpdatedAt { get; set; }        // متغير: وقت آخر تعديل

        // علاقة 1 → متعدد مع المخازن
        public virtual ICollection<Warehouse> Warehouses { get; set; } = new List<Warehouse>();
    }
}
