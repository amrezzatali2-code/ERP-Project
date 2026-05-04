using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// سجل أرشيفي لمحاولات ونتائج تكامل التتبع مع هيئة الدواء.
    /// </summary>
    public class TrackTraceEventLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        public long? ItemUnitId { get; set; }

        [StringLength(50)]
        public string EventType { get; set; } = string.Empty;

        [StringLength(30)]
        public string Status { get; set; } = string.Empty;

        public string RequestJson { get; set; } = string.Empty;
        public string? ResponseJson { get; set; }

        [StringLength(1000)]
        public string? ErrorMessage { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ItemUnit? ItemUnit { get; set; }
    }
}
