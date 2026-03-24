using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// رأس استيراد الفاكس: كل استيراد Excel من عميل = رأس واحد (تاريخ ووقت الاستلام).
    /// </summary>
    [Table("VendorFaxUploads")]
    public class VendorFaxUpload
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public int CustomerId { get; set; }

        public DateTime ReceivedAt { get; set; }

        [StringLength(255)]
        public string? FileName { get; set; }

        [StringLength(100)]
        public string? ImportedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(CustomerId))]
        public virtual Customer? Customer { get; set; }

        [InverseProperty(nameof(VendorFaxLine.VendorFaxUpload))]
        public virtual ICollection<VendorFaxLine> Lines { get; set; } = new List<VendorFaxLine>();
    }
}
