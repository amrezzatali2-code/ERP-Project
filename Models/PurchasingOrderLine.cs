using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// سطر طلب شراء الموديول.
    /// </summary>
    [Table("PurchasingOrderLines")]
    public class PurchasingOrderLine
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public int PurchasingOrderId { get; set; }

        public int LineNo { get; set; }

        [Required]
        public int ProductId { get; set; }

        [StringLength(100)]
        public string? VendorProductCode { get; set; }

        [StringLength(255)]
        public string? ProductName { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal Qty { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal? UnitPrice { get; set; }

        [Column(TypeName = "decimal(9,2)")]
        public decimal? DiscountPct { get; set; }

        [StringLength(500)]
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(PurchasingOrderId))]
        public virtual PurchasingOrder? PurchasingOrder { get; set; }

        [ForeignKey(nameof(ProductId))]
        public virtual Product? Product { get; set; }
    }
}
