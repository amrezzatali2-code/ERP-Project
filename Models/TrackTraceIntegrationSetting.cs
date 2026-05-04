using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// إعدادات الربط مع نظام هيئة الدواء EPTTS.
    /// </summary>
    public class TrackTraceIntegrationSetting
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public bool IsEnabled { get; set; }
        public bool UseSandbox { get; set; } = true;

        [StringLength(500)]
        public string? BaseUrl { get; set; }

        [StringLength(200)]
        public string? UserName { get; set; }

        [StringLength(500)]
        public string? PasswordOrToken { get; set; }

        [StringLength(200)]
        public string? ClientId { get; set; }

        [StringLength(500)]
        public string? ClientSecret { get; set; }

        [StringLength(500)]
        public string? CallbackBaseUrl { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
