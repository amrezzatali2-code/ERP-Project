using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// إعداد مصدر البيانات للموديول (ORGA أو ERP).
    /// </summary>
    [Table("PurchasingDataSourceConfigs")]
    public class PurchasingDataSourceConfig
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [StringLength(20)]
        public string SourceType { get; set; } = ""; // "ORGA" أو "ERP"

        [StringLength(100)]
        public string? DisplayName { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
