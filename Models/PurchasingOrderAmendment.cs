using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// سجل تأكيد/تعديل طلب الشراء من العميل.
    /// </summary>
    [Table("PurchasingOrderAmendments")]
    public class PurchasingOrderAmendment
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public int PurchasingOrderId { get; set; }

        [StringLength(20)]
        public string AmendmentType { get; set; } = ""; // Confirmed, Modified

        public DateTime AmendmentDate { get; set; }

        [StringLength(4000)]
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(PurchasingOrderId))]
        public virtual PurchasingOrder? PurchasingOrder { get; set; }
    }
}
