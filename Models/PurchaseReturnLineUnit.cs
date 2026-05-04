using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// ربط عبوات التتبع بسطر مرتجع الشراء.
    /// </summary>
    public class PurchaseReturnLineUnit
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        public int PRetId { get; set; }
        public int LineNo { get; set; }
        public long ItemUnitId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public PurchaseReturnLine? PurchaseReturnLine { get; set; }
        public ItemUnit? ItemUnit { get; set; }
    }
}
