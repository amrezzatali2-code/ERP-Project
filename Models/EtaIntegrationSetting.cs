using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// إعدادات الربط مع الفاتورة الإلكترونية المصرية.
    /// </summary>
    public class EtaIntegrationSetting
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public bool IsEnabled { get; set; }
        public bool UseSandbox { get; set; } = true;

        [StringLength(500)]
        public string? IdentityBaseUrl { get; set; }

        [StringLength(500)]
        public string? ApiBaseUrl { get; set; }

        [StringLength(200)]
        public string? ClientId { get; set; }

        [StringLength(500)]
        public string? ClientSecret { get; set; }

        [StringLength(200)]
        public string? TaxpayerRin { get; set; }

        [StringLength(500)]
        public string? CallbackBaseUrl { get; set; }

        [StringLength(500)]
        public string? CertificateThumbprint { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
