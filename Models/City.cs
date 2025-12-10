using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// جدول المدن/المراكز — يربط كل مدينة/مركز بمحافظتها
    /// يستخدم لاحقًا لربط الأحياء ثم المناطق وعناوين العملاء/العميلين
    /// </summary>
    [Table("Cities")] // التأكيد على اسم الجدول في قاعدة البيانات
    public class City
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int CityId { get; set; }  // معرّف المدينة/المركز (Identity)

        [Required, MaxLength(150)]
        public string CityName { get; set; } = string.Empty; // اسم المدينة/المركز

        [Required]
        public int GovernorateId { get; set; } // FK إلى المحافظات

        /// <summary>
        /// نوع الكيان (اختياري):
        /// 0 = مدينة، 1 = مركز، 2 = قسم، 3 = حي
        /// </summary>
        public byte? CityType { get; set; } // TinyInt اختياري

        /// <summary>
        /// هل المدينة/المركز نشط؟ القيمة الافتراضية = 1 (true)
        /// </summary>
        public bool IsActive { get; set; } = true;

        [MaxLength(250)]
        public string? Notes { get; set; } // ملاحظات اختيارية

        /// <summary>
        /// تاريخ ووقت الإنشاء — يملأ تلقائيًا من قاعدة البيانات
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// تاريخ ووقت آخر تعديل — يحدث عند كل تعديل
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        // ======= علاقات الملاحة =======
        public virtual Governorate? Governorate { get; set; } // المحافظة التابعة
    }
}
