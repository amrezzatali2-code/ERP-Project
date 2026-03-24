using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// سطر الفاكس: صنف كما أرسله العميل، سعر، خصم، وربط بالصنف عندنا بعد المطابقة.
    /// </summary>
    [Table("VendorFaxLines")]
    public class VendorFaxLine
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public int VendorFaxUploadId { get; set; }

        public int LineNo { get; set; }

        [Required]
        [StringLength(255)]
        public string ProductNameFromVendor { get; set; } = "";

        [StringLength(100)]
        public string? VendorProductCode { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal Price { get; set; }

        [Column(TypeName = "decimal(9,2)")]
        public decimal DiscountPct { get; set; }

        public int? MatchedProductId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(VendorFaxUploadId))]
        public virtual VendorFaxUpload? VendorFaxUpload { get; set; }

        [ForeignKey(nameof(MatchedProductId))]
        public virtual Product? MatchedProduct { get; set; }
    }
}
