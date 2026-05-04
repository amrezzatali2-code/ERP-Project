using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// ربط عبوات التتبع بسطر مرتجع البيع.
    /// </summary>
    public class SalesReturnLineUnit
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        public int SRId { get; set; }
        public int LineNo { get; set; }
        public long ItemUnitId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public SalesReturnLine? SalesReturnLine { get; set; }
        public ItemUnit? ItemUnit { get; set; }
    }
}
