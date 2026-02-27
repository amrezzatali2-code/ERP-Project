using System;
using System.ComponentModel.DataAnnotations;          // تعليقات العرض Display + Required + MaxLength
using System.ComponentModel.DataAnnotations.Schema;   // DatabaseGenerated
using Microsoft.EntityFrameworkCore;                  // Precision

namespace ERP.Models
{
    /// <summary>
    /// جدول التشغيلات (Batch):
    /// - كل صف = تشغيلة لصنف معيّن (ProdId + BatchNo + Expiry).
    /// - نستخدمه كمرجع ثابت لرقم التشغيلة وتاريخ الصلاحية وسعر الجمهور والتكلفة الافتراضية.
    /// - StockLedger هو الذى يسجل الحركات الفعلية على مستوى المخزن.
    /// </summary>
    public class Batch
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Display(Name = "كود التشغيلة")]
        public int BatchId { get; set; }             // متغير: رقم داخلي للتشغيلة (PK)

        [Display(Name = "كود الصنف")]
        public int ProdId { get; set; }              // متغير: كود الصنف (FK → Product)

        [Required]
        [MaxLength(50)]
        [Display(Name = "رقم التشغيلة")]
        public string BatchNo { get; set; } = default!;  // متغير: رقم التشغيلة كما هو مكتوب على العلبة

        [Display(Name = "تاريخ الصلاحية")]
        public DateTime Expiry { get; set; }         // متغير: تاريخ الصلاحية

        // 🔹 سعر الجمهور لهذه التشغيلة (لو اختلف عن السعر العام)
        [Precision(18, 2)]
        [Display(Name = "سعر الجمهور للتشغيلة")]
        public decimal? PriceRetailBatch { get; set; }   // متغير: سعر الجمهور الخاص بالتشغيلة

        [Precision(18, 2)]
        [Display(Name = "خصم الشراء للتشغيلة %")]
        public decimal? PurchaseDiscountPct { get; set; }

        // 🔹 التكلفة الافتراضية للعلبة من هذه التشغيلة (4 أرقام عشرية تكفى للتكلفة)
        [Precision(18, 4)]
        [Display(Name = "التكلفة الافتراضية للعلبة")]
        public decimal? UnitCostDefault { get; set; }    // متغير: تكلفة العلبة (Cost) الافتراضية

        // 🔹 تاريخ إدخال هذه التشغيلة لأول مرة فى النظام (من فاتورة شراء أو تسوية افتتاحية)
        [Display(Name = "تاريخ إدخال التشغيلة")]
        public DateTime EntryDate { get; set; } = DateTime.UtcNow; // متغير: أول إدخال للتشغيلة

        // 🔹 العميل/المورد المرتبط بالتشغيلة (غالباً مورد فاتورة الشراء الأولى لهذه التشغيلة)
        [Display(Name = "المورد/العميل المرتبط")]
        public int? CustomerId { get; set; }              // متغير: رقم المورد أو العميل (اختياري)

        // ===== حقول النظام الموحد (تواريخ وإنفعال) =====

        [Display(Name = "تاريخ الإنشاء")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;  // متغير: وقت إنشاء السجل فى قاعدة البيانات

        [Display(Name = "تاريخ آخر تعديل")]
        public DateTime? UpdatedAt { get; set; }                    // متغير: آخر وقت تم فيه تعديل بيانات التشغيلة

        [Display(Name = "نشط؟")]
        public bool IsActive { get; set; } = true;                  // متغير: هل التشغيلة فعّالة (لم تُلغَ أو تُهمل)

        // ===== العلاقات (Navigation Properties) =====

        public Product? Product { get; set; }       // تعليق: الصنف المرتبط بالتشغيلة
        public Customer? Customer { get; set; }     // تعليق: المورد/العميل الذى جاءت منه التشغيلة (إن وجد)
    }
}
