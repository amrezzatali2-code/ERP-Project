using System;
using System.ComponentModel.DataAnnotations;

namespace ERP.Models
{
    /// <summary>
    /// إعدادات هيدر الطباعة العامة (اسم + لوجو).
    /// </summary>
    public class PrintHeaderSetting
    {
        public int Id { get; set; }

        [MaxLength(200)]
        public string CompanyName { get; set; } = "شركة الهدى";

        [MaxLength(500)]
        public string? LogoPath { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
