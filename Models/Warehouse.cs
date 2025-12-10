using System;
using System.Collections.Generic;                 // ICollection, List
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// جدول المخازن
    /// يخزن بيانات كل مخزن (اسم المخزن، الفرع، حالة التفعيل، الملاحظات، تواريخ الإنشاء والتعديل)
    /// </summary>
    public class Warehouse
    {
        [Key]                                                   // المفتاح الأساسي للجدول
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]    // يجعل WarehouseId عمود Identity (يزيد تلقائيًا)
        [Display(Name = "معرّف المخزن")]
        public int WarehouseId { get; set; }                    // رقم المخزن (PK)

        [Required]
        [StringLength(200)]
        [Display(Name = "اسم المخزن")]
        public string WarehouseName { get; set; } = null!;      // اسم المخزن

        [Required]
        [Display(Name = "معرّف الفرع")]
        public int BranchId { get; set; }                       // FK على جدول الفروع

        [Display(Name = "فعّال")]
        public bool IsActive { get; set; } = true;              // حالة التفعيل

        [StringLength(500)]
        [Display(Name = "ملاحظات")]
        public string? Notes { get; set; }                      // ملاحظات اختيارية

        // ===================== حقول التاريخ =====================

        [Display(Name = "تاريخ الإنشاء")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Display(Name = "تاريخ آخر تعديل")]
        public DateTime? UpdatedAt { get; set; }

        // ===================== العلاقات =====================

        [Display(Name = "الفرع")]
        public virtual Branch? Branch { get; set; }             // المخزن يتبع فرع واحد

        // كل تسويات الجرد التي تمت على هذا المخزن
        public virtual ICollection<StockAdjustment> StockAdjustments { get; set; }
            = new List<StockAdjustment>();                      // قائمة تسويات مرتبطة بالمخزن

        // التحويلات اللي المخزن ده هو المصدر فيها
        public ICollection<StockTransfer> TransfersFrom { get; set; }
            = new List<StockTransfer>();   // متغير: قائمة التحويلات الخارجة من هذا المخزن

        // التحويلات اللي المخزن ده هو الوجهة فيها
        public ICollection<StockTransfer> TransfersTo { get; set; }
            = new List<StockTransfer>();   // متغير: قائمة التحويلات الداخلة لهذا المخزن

    }
}
