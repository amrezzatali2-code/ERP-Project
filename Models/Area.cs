using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// جدول المناطق/القرى — يتبع محافظة + يتبع حي/مركز
    /// </summary>
    public class Area
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AreaId { get; set; }                    // معرّف المنطقة (PK)

        public string AreaName { get; set; } = "";         // اسم المنطقة/القرية (إلزامي)

        public int GovernorateId { get; set; }             // FK → Governorates.GovernorateId (المحافظة)
        public int? DistrictId { get; set; }                // FK → Districts.DistrictId (الحي/المركز — اختياري: «لا يوجد حي/مركز»)

        public bool IsActive { get; set; } = true;         // الحالة (نشط/موقوف)
        public string? Notes { get; set; }                 // ملاحظات اختيارية
        public DateTime? CreatedAt { get; set; }           // تاريخ الإنشاء
        public DateTime? UpdatedAt { get; set; }           // تاريخ آخر تعديل

        // الملاحة
        public virtual Governorate? Governorate { get; set; } // المحافظة التابعة
        public virtual District? District { get; set; } // الحي/المركز التابع
    }
}
