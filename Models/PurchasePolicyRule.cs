using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// سياسات الشراء: قواعد الشراء (مقارنة خصم، تقريب كمية، حد مخزون، إلخ).
    /// </summary>
    [Table("PurchasePolicyRules")]
    public class PurchasePolicyRule
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public bool Enabled { get; set; } = true;

        /// <summary>0=مقارنة خصم (Compare), 1=تقريب كمية بحد سعر (RoundQtyWithPriceCap)</summary>
        public byte RuleType { get; set; }

        /// <summary>عند Compare: 0=مساوي, 1=أعلى, 2=أدنى</summary>
        public byte CompareOp { get; set; }

        [Column(TypeName = "decimal(9,2)")]
        public decimal? DiffExact { get; set; }

        [Column(TypeName = "decimal(9,2)")]
        public decimal? TargetPercent { get; set; }

        [Column(TypeName = "decimal(9,2)")]
        public decimal? StockBelowPercent { get; set; }

        [Column(TypeName = "decimal(9,2)")]
        public decimal Tolerance { get; set; } = 0.10m;

        /// <summary>0=شراء (Buy), 1=مراجعة (Review)</summary>
        public byte Action { get; set; }

        public int SortOrder { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
