using System;                                  // متغيرات التاريخ والوقت DateTime
using System.Collections.Generic;              // القوائم List
using System.ComponentModel.DataAnnotations;   // خصائص التحقق من صحة البيانات

namespace ERP.Models
{
    /// <summary>
    /// رأس تسوية الجرد:
    /// كل سجل = حركة تسوية واحدة على مخزن معيّن في تاريخ معيّن
    /// مثال: جرد مخزن 1 يوم 2025-01-01 واكتشاف فروقات.
    /// </summary>
    public class StockAdjustment
    {
        [Key]
        [Display(Name = "رقم التسوية")]               // يظهر في الشاشات كـ رقم التسوية
        public int Id { get; set; }                    // كود التسوية (PK)

        [Required]
        [Display(Name = "تاريخ التسوية")]             // تاريخ الجرد / التسوية
        public DateTime AdjustmentDate { get; set; }   // تاريخ التسوية

        [Required]
        [Display(Name = "كود المخزن")]                // المخزن الذي نعمل عليه الجرد
        public int WarehouseId { get; set; }           // FK على جدول Warehouses

        [MaxLength(50)]
        [Display(Name = "رقم مرجعي")]                 // رقم محضر الجرد أو مستند خارجي
        public string? ReferenceNo { get; set; }       // اختياري

        [MaxLength(200)]
        [Display(Name = "سبب التسوية / ملاحظات")]
        public string? Reason { get; set; }            // سبب عام أو ملاحظات

        // ========= حقول التاريخ =========

        [Display(Name = "تاريخ الإنشاء")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        // يُملأ مرة واحدة عند إنشاء التسوية

        [Display(Name = "آخر تعديل")]
        public DateTime? UpdatedAt { get; set; }
        // يُحدث عند تعديل التسوية

        // ========= علاقات الملاحة =========

        [Display(Name = "المخزن")]
        public virtual Warehouse? Warehouse { get; set; } // المخزن المرتبط بالتسوية

        // سطور التسوية (أصناف + كميات)
        public virtual ICollection<StockAdjustmentLine> Lines { get; set; }
            = new List<StockAdjustmentLine>();           // كل سطر = صنف + باتش + فروق كميات
    }
}
