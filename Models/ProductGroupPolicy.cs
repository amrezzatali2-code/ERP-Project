using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;  // علشان Precision

namespace ERP.Models
{
    /// <summary>
    /// سياسة لمجموعة أصناف حسب(سياسة + مخزن):
    /// لو الصنف ينتمي لهذه المجموعة نستخدم هذه النسبة بدلاً من نسبة سياسة المخزن.
    /// </summary>
    public class ProductGroupPolicy
    {
        [Key]                                      // المفتاح الأساسي
        public int Id { get; set; }                // رقم داخلي للسجل

        [Required]
        public int ProductGroupId { get; set; }    // كود مجموعة الأصناف (FK على ProductGroup)

        [Required]
        public int PolicyId { get; set; }          // كود السياسة (FK على Policy)

        [Required]
        public int WarehouseId { get; set; }       // كود المخزن (FK على Warehouse)

        [Precision(5, 2)]
        public decimal ProfitPercent { get; set; } // نسبة ربح المجموعة من الخصم المرجح

        [Precision(5, 2)]
        public decimal? MaxDiscountToCustomer { get; set; }
        // أقصى خصم مسموح من الخصم المرجح للعميل (اختياري)

        public bool IsActive { get; set; } = true; // تفعيل/إلغاء هذه القاعدة

        // تواريخ الإنشاء والتعديل
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // علاقات ملاحة
        public ProductGroup? ProductGroup { get; set; }  // المجموعة المرتبطة
        public Policy? Policy { get; set; }              // السياسة المرتبطة
        public Warehouse? Warehouse { get; set; }        // المخزن المرتبط
    }
}
