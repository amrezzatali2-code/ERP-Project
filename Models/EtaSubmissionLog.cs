using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// سجل نتائج إرسال المستندات للفاتورة الإلكترونية.
    /// </summary>
    public class EtaSubmissionLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [StringLength(50)]
        public string SourceType { get; set; } = string.Empty;

        public int SourceId { get; set; }

        [StringLength(50)]
        public string DocumentType { get; set; } = string.Empty;

        [StringLength(100)]
        public string? SubmissionUuid { get; set; }

        [StringLength(100)]
        public string? DocumentUuid { get; set; }

        [StringLength(30)]
        public string Status { get; set; } = string.Empty;

        public string RequestJson { get; set; } = string.Empty;
        public string? ResponseJson { get; set; }

        [StringLength(1000)]
        public string? ErrorMessage { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
