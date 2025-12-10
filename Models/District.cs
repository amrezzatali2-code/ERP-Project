using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// جدول الأحياء/المراكز — تابع لمحافظة فقط
    /// </summary>
    public class District
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int DistrictId { get; set; }                 // معرّف الحي/المركز (PK, Identity)

        public string DistrictName { get; set; } = null!;   // اسم الحي/المركز (إجباري)

        public int GovernorateId { get; set; }              // FK: المحافظة التابعة
        public virtual Governorate? Governorate { get; set; } // ملاحة للمحافظة

        public byte? DistrictType { get; set; }             // النوع (اختياري): 0=حي، 1=مركز

        public bool IsActive { get; set; } = true;          // نشط/موقوف (افتراضي = نشط)

        public string? Notes { get; set; }                  // ملاحظات (اختياري)

        public DateTime? CreatedAt { get; set; }            // تاريخ الإنشاء (اختياري)
        public DateTime? UpdatedAt { get; set; }            // تاريخ التعديل (اختياري)

        public virtual ICollection<Area> Areas { get; set; } = new List<Area>(); // المناطق التابعة للحي/المركز

    }
}
