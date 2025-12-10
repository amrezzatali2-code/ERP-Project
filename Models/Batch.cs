using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
// ✅ علشان نقدر نستخدم [Precision]
using Microsoft.EntityFrameworkCore;

namespace ERP.Models
{
    /// <summary>
    /// تشغيلات (تعريف تشغيلة لكل صنف مرة واحدة):
    /// - نستخدمها كمرجع ثابت لرقم التشغيلة + الصلاحية + السعر العام وقت ظهور التشغيلة (اختياري).
    /// - تفيد في الفاتورة لاختيار بيانات التشغيلة بسرعة.
    /// </summary>
    public class Batch
    {
        [Key]                                                   // المفتاح الأساسي للتشغيلة
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]   // رقم يزيد تلقائيًا
        public int BatchId { get; set; }                        // متغير: كود التشغيلة (PK)

        public int ProdId { get; set; }                         // متغير: كود الصنف (FK)

        public string BatchNo { get; set; } = default!;         // متغير: رقم التشغيلة

        public DateTime Expiry { get; set; }                    // متغير: تاريخ الصلاحية

        // 🔹 سعر الجمهور الخاص بهذه التشغيلة (نكتفي بـ خانتين عشريتين)
        [Precision(18, 2)]                                      // نوع العمود في SQL = decimal(18,2)
        public decimal? PriceRetailBatch { get; set; }          // متغير: سعر الجمهور للتشغيلة

        // 🔹 تكلفة العلبة الافتراضية (ممكن نحتاج 4 أرقام عشرية)
        [Precision(18, 4)]                                      // نوع العمود في SQL = decimal(18,4)
        public decimal? UnitCostDefault { get; set; }           // متغير: تكلفة العلبة الافتراضية

        public DateTime EntryDate { get; set; }                 // متغير: تاريخ إدخال التشغيلة

        public int? CustomerId { get; set; }                    // متغير: رقم العميل/المورد (اختياري)
    }
}
