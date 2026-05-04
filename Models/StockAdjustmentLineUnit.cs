using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// ربط عبوات التتبع بسطر تسوية المخزون.
    /// </summary>
    public class StockAdjustmentLineUnit
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        public int StockAdjustmentLineId { get; set; }
        public long ItemUnitId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public StockAdjustmentLine? StockAdjustmentLine { get; set; }
        public ItemUnit? ItemUnit { get; set; }
    }
}
