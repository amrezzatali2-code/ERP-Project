using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;              // لاستخدام Precision

namespace ERP.Models
{
    /// <summary>
    /// قاعدة سياسة للمخزن:
    /// لكل (مخزن + سياسة) نحدد نسبة ربح المخزن وربما أقصى خصم للعميل.
    /// مثال: مخزن الأدوية + سياسة 1 = ربح 1% من الخصم المرجح.
    /// </summary>
    public class WarehousePolicyRule
    {
        [Key]                                      // المفتاح الأساسي
        public int Id { get; set; }                // رقم داخلي للقاعدة

        [Required]
        public int WarehouseId { get; set; }       // كود المخزن (FK على جدول Warehouses)

        [Required]
        public int PolicyId { get; set; }          // كود السياسة (FK على جدول Policies)

        [Precision(5, 2)]                          // نسبة مئوية بحد أقصى 999.99
        public decimal ProfitPercent { get; set; } // نسبة ربح المخزن من الخصم المرجح (مثال: 1 = 1%)

        [Precision(5, 2)]
        public decimal? MaxDiscountToCustomer { get; set; }
        // أقصى خصم مسموح للعميل كنسبة مئوية (اختياري)

        // تواريخ الإنشاء والتعديل
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // تاريخ إنشاء السجل
        public DateTime? UpdatedAt { get; set; }                   // آخر تعديل

        // علاقات ملاحة (اختيارية)
        public Warehouse? Warehouse { get; set; }   // المخزن المرتبط بهذه القاعدة
        public Policy? Policy { get; set; }         // السياسة المرتبطة بهذه القاعدة

        public bool IsActive { get; set; } = true;                 // متغير: القاعدة مفعلة افتراضيًا

    }
}
