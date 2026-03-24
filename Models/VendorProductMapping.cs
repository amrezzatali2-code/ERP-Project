using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// مطابقة أصناف العميل (قاعدة البيانات): اسم العميل، اسم الصنف، الكود، سعر الجمهور، كود العميل.
    /// الأصناف والعملاء من جداول الـ ERP. المطابقة عند الفاكس: بالكود أولاً إن وُجد ثم بالاسم.
    /// </summary>
    [Table("VendorProductMappings")]
    public class VendorProductMapping
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public int CustomerId { get; set; }

        [StringLength(255)]
        [Display(Name = "اسم الصنف")]
        public string? VendorProductName { get; set; }

        [Required]
        public int ProductId { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        [Display(Name = "سعر الجمهور")]
        public decimal? PriceRetail { get; set; }

        /// <summary>كود صنف العميل — يمكن تركه فارغاً ويُعبَّأ عند المطابقة (مع اسم الصنف).</summary>
        [StringLength(100)]
        [Display(Name = "كود صنف العميل")]
        public string? VendorProductCode { get; set; }

        [StringLength(50)]
        [Display(Name = "وسم")]
        public string? Tag { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        [ForeignKey(nameof(CustomerId))]
        public virtual Customer? Customer { get; set; }

        [ForeignKey(nameof(ProductId))]
        public virtual Product? Product { get; set; }
    }
}
