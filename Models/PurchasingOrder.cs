using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// طلب شراء الموديول (قبل التحويل إلى ERP). يُرسل واتساب ثم تأكيد/تعديل من العميل.
    /// </summary>
    [Table("PurchasingOrders")]
    public class PurchasingOrder
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public int CustomerId { get; set; }

        [StringLength(50)]
        public string? OrderNumber { get; set; }

        [DataType(DataType.Date)]
        public DateTime OrderDate { get; set; }

        [StringLength(30)]
        public string Status { get; set; } = "Draft"; // Draft, SentToWhatsApp, Confirmed, Modified, ConvertedToErp

        public DateTime? SentAt { get; set; }
        public DateTime? ConfirmedAt { get; set; }

        [StringLength(4000)]
        public string? AmendmentNotes { get; set; }

        /// <summary>عند التحويل إلى ERP: رقم طلب الشراء (PurchaseRequest.PRId)</summary>
        public int? ErpPurchaseRequestId { get; set; }

        [StringLength(100)]
        public string? CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        [ForeignKey(nameof(CustomerId))]
        public virtual Customer? Customer { get; set; }

        [ForeignKey(nameof(ErpPurchaseRequestId))]
        public virtual PurchaseRequest? ErpPurchaseRequest { get; set; }

        [InverseProperty(nameof(PurchasingOrderLine.PurchasingOrder))]
        public virtual ICollection<PurchasingOrderLine> Lines { get; set; } = new List<PurchasingOrderLine>();
    }
}
