using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// ربط عبوات التتبع بسطر التحويل المخزني.
    /// </summary>
    public class StockTransferLineUnit
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        public int StockTransferLineId { get; set; }
        public long ItemUnitId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public StockTransferLine? StockTransferLine { get; set; }
        public ItemUnit? ItemUnit { get; set; }
    }
}
