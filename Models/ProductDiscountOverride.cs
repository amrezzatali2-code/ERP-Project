using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// جدول الخصم اليدوي للبيع: تخزين خصم يدوي معيّن من المستخدم للصنف/المخزن/التشغيلة.
    /// السجل الأحدث (CreatedAt DESC) هو الفعّال. لا نعدّل سجلات قديمة — نضيف سجل جديد عند كل تغيير.
    /// </summary>
    public class ProductDiscountOverride
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>كود الصنف (FK → Products).</summary>
        public int ProductId { get; set; }

        /// <summary>المخزن (اختياري). لو null = يطبق على كل المخازن.</summary>
        public int? WarehouseId { get; set; }

        /// <summary>التشغيلة (اختياري). لو null = يطبق على مستوى الصنف/المخزن فقط.</summary>
        public int? BatchId { get; set; }

        /// <summary>نسبة الخصم اليدوي للبيع (0..100).</summary>
        [Column(TypeName = "decimal(5,2)")]
        [Range(0, 100)]
        public decimal OverrideDiscountPct { get; set; }

        /// <summary>سبب أو ملاحظة (اختياري).</summary>
        [StringLength(200)]
        public string? Reason { get; set; }

        /// <summary>من أنشأ السجل (اسم المستخدم).</summary>
        [StringLength(100)]
        public string? CreatedBy { get; set; }

        /// <summary>وقت الإنشاء. السجل الأحدث هو المعتمد.</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // علاقات تنقلية (للاستعلامات)
        [ForeignKey(nameof(ProductId))]
        public virtual Product? Product { get; set; }

        [ForeignKey(nameof(WarehouseId))]
        public virtual Warehouse? Warehouse { get; set; }

        [ForeignKey(nameof(BatchId))]
        public virtual Batch? Batch { get; set; }
    }
}
