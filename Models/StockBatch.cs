using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// جدول: Stock_Batches
    /// الهدف: رصيد سريع جاهز (Cache) لكل (مخزن + صنف + تشغيلة + صلاحية).
    /// - لا يسجل "حركات" مثل StockLedger
    /// - فقط يحتفظ بالرصيد الحالي لتسريع شاشات البيع/التقارير
    /// </summary>
    public class StockBatch
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Display(Name = "رقم السجل")]
        public int Id { get; set; } // متغير: رقم السجل الأساسي

        // =========================
        // (1) مفتاح الرصيد (هوية الصف)
        // =========================

        [Display(Name = "المخزن")]
        public int WarehouseId { get; set; } // متغير: كود المخزن

        [Display(Name = "كود الصنف")]
        public int ProdId { get; set; } // متغير: كود الصنف

        [MaxLength(50)]
        [Display(Name = "رقم التشغيلة")]
        public string BatchNo { get; set; } = ""; // متغير: رقم التشغيلة (لازم يكون موحد Trim)

        [Display(Name = "الصلاحية")]
        public DateTime? Expiry { get; set; } // متغير: تاريخ الصلاحية (Date فقط)

        // =========================
        // (2) الرصيد
        // =========================

        [Display(Name = "المتاح")]
        public int QtyOnHand { get; set; } // متغير: الرصيد المتاح الآن

        [Display(Name = "محجوز")]
        public int QtyReserved { get; set; } // متغير: المحجوز (اختياري)

        // =========================
        // (3) معلومات مساعدة للمتابعة
        // =========================

        [Display(Name = "آخر تحديث")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow; // متغير: وقت آخر تحديث للرصيد

        [MaxLength(200)]
        [Display(Name = "ملاحظة")]
        public string? Note { get; set; } // متغير: ملاحظة بسيطة (اختياري)
    }
}
