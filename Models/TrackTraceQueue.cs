using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// صف انتظار إرسال أحداث التتبع إلى هيئة الدواء.
    /// </summary>
    public class TrackTraceQueue
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        public long? ItemUnitId { get; set; }

        [Required]
        [StringLength(50)]
        public string EventType { get; set; } = string.Empty;

        [Required]
        [StringLength(30)]
        public string Status { get; set; } = "Pending";

        [Required]
        public string JsonData { get; set; } = string.Empty;

        public int RetryCount { get; set; }

        [StringLength(1000)]
        public string? LastError { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? NextRetryAt { get; set; }
        public DateTime? SentAt { get; set; }

        public ItemUnit? ItemUnit { get; set; }
    }
}
