using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// ربط عبوات التتبع بسطر فاتورة الشراء.
    /// </summary>
    public class PurchaseInvoiceLineUnit
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        public int PIId { get; set; }
        public int LineNo { get; set; }
        public long ItemUnitId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public PILine? PurchaseInvoiceLine { get; set; }
        public ItemUnit? ItemUnit { get; set; }
    }
}
