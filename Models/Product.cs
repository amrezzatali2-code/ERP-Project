using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;        // خصائص التحقق من البيانات
using System.ComponentModel.DataAnnotations.Schema; // خصائص قاعدة البيانات

namespace ERP.Models
{
    /// <summary>
    /// جدول الأصناف (المنتجات الدوائية).
    /// </summary>
    public class Product
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Display(Name = "كود الصنف")]
        public int ProdId { get; set; }                 // متغير: رقم متسلسل للصنف

        [Display(Name = "اسم الصنف")]
        public string? ProdName { get; set; }           // متغير: الاسم التجاري للصنف

        [Display(Name = "الباركود")]
        public string? Barcode { get; set; }            // متغير: كود الباركود

        [Display(Name = "الاسم العلمي")]
        public string? GenericName { get; set; }        // متغير: الاسم العلمي / المادة الفعالة

        [Display(Name = "التركيز")]
        public string? Strength { get; set; }           // متغير: تركيز الدواء

        [Display(Name = "الفئة")]
        public int? CategoryId { get; set; }            // متغير: FK إلى جدول الفئات Categories

        [Display(Name = "سعر الجمهور")]
        public decimal PriceRetail { get; set; }        // متغير: سعر البيع للجمهور

        [Display(Name = "الوصف")]
        public string? Description { get; set; }        // متغير: وصف مختصر للصنف

        [Display(Name = "الشكل الدوائي")]
        public string? DosageForm { get; set; }         // متغير: أقراص / شراب / حقن ...

        // 🔹 عمود المنشأ (محلي / مستورد فقط)
        [Display(Name = "المنشأ (محلي / مستورد)")]
        [StringLength(10)]
        [RegularExpression(@"^(محلي|مستورد)$",
            ErrorMessage = "اختر قيمة صحيحة: محلي أو مستورد فقط")]
        public string? Imported { get; set; }           // متغير: نوع المنشأ (محلي أو مستورد)

        [Display(Name = "الشركة")]
        public string? Company { get; set; }            // متغير: اسم الشركة المنتجة

        [Display(Name = "الموقع")]
        [StringLength(50)]
        public string? Location { get; set; }           // متغير: الموقع (من استيراد إكسل أصناف الدواء/الإكسسوار)

        [Display(Name = "كود الإكسل")]
        [StringLength(50)]
        public string? ExternalCode { get; set; }       // متغير: الكود كما في الإكسل (ليس ProdId التلقائي)

        [Display(Name = "المخزن")]
        public int? WarehouseId { get; set; }             // متغير: المخزن الافتراضي للصنف

        [ForeignKey(nameof(WarehouseId))]
        [Display(Name = "المخزن")]
        public Warehouse? Warehouse { get; set; }

        [Display(Name = "فعال")]
        public bool IsActive { get; set; }              // متغير: هل الصنف مفعل في النظام؟

        [Display(Name = "تاريخ آخر تغيير سعر")]
        public DateTime? LastPriceChangeDate { get; set; } // متغير: آخر تاريخ تم فيه تعديل السعر

        // ================= الكوتة (Quota) للصنف =================

        [Display(Name = "له كوتة؟")]             // متغير: هل الصنف له كوتة محددة؟
        public bool HasQuota { get; set; } = false;

        [Display(Name = "كمية الكوتة")]          // متغير: عدد العلب المسموح بها كوتة
        [Range(0, int.MaxValue, ErrorMessage = "كمية الكوتة يجب أن تكون رقمًا موجبًا")]
        public int? QuotaQuantity { get; set; }

        [Display(Name = "كمية الكرتونة")]
        [Range(0, int.MaxValue, ErrorMessage = "كمية الكرتونة يجب أن تكون رقمًا موجبًا")]
        public int? CartonQuantity { get; set; }

        [Display(Name = "كمية الباكو")]
        [Range(0, int.MaxValue, ErrorMessage = "كمية الباكو يجب أن تكون رقمًا موجبًا")]
        public int? PackQuantity { get; set; }


        // ================= تواريخ الإنشاء والتعديل =================

        [Display(Name = "تاريخ الإنشاء")]
        public DateTime CreatedAt { get; set; }         // متغير: تاريخ إضافة الصنف لأول مرة

        [Display(Name = "آخر تعديل")]
        public DateTime UpdatedAt { get; set; }         // متغير: آخر تعديل على بيانات الصنف

        // ================= ربط مجموعات الأصناف والبونص =================

        [Display(Name = "مجموعة الصنف")]
        public int? ProductGroupId { get; set; }        // متغير: كود مجموعة الأصناف (اختياري)

        [Display(Name = "مجموعة الصنف")]
        public ProductGroup? ProductGroup { get; set; } // متغير: كائن مجموعة الأصناف المرتبط بالصنف

        [Display(Name = "مجموعة البونص")]
        public int? ProductBonusGroupId { get; set; }   // متغير: كود مجموعة البونص (اختياري)

        [Display(Name = "مجموعة البونص")]
        public ProductBonusGroup? ProductBonusGroup { get; set; } // متغير: مجموعة البونص المرتبطة

        [Display(Name = "التصنيف")]
        public int? ClassificationId { get; set; }      // متغير: تصنيف الصنف (عادي، ثلاجة، …) — لخط السير

        [ForeignKey(nameof(ClassificationId))]
        [Display(Name = "التصنيف")]
        public ProductClassification? Classification { get; set; }

        // ================= علاقات أخرى =================

        public Category? Category { get; set; }         // متغير: كائن الفئة المرتبط بالصنف

        public ICollection<StockAdjustmentLine> StockAdjustmentLines { get; set; }
            = new List<StockAdjustmentLine>();         // متغير: سطور تسويات المخزون المرتبطة بالصنف
    }
}
