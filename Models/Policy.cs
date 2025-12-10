using System;                                     // متغيرات الوقت والتاريخ
using System.Collections.Generic;                 // القوائم List
using System.ComponentModel.DataAnnotations;      // خصائص التحقق [Required] [MaxLength]
using Microsoft.EntityFrameworkCore;              // Precision لاستخدام

namespace ERP.Models
{
    /// <summary>
    /// جدول السياسات التسعيرية للعملاء/المجموعات.
    /// مثال: سياسة رقم 1، سياسة كبار العملاء، سياسة صيدليات A.
    /// </summary>
    public class Policy
    {
        [Key]                                      // المفتاح الأساسي للسياسة
        public int PolicyId { get; set; }          // متغير: رقم السياسة الداخلي (Int Identity)

        [Required]                                 // اسم السياسة مطلوب
        [MaxLength(100)]                           // الحد الأقصى لطول الاسم
        public string Name { get; set; } = string.Empty;   // متغير: اسم السياسة (يظهر في الشاشات)

        [MaxLength(500)]
        public string? Description { get; set; }   // متغير: وصف مختصر للسياسة (اختياري)

        public bool IsActive { get; set; } = true; // متغير: هل السياسة مفعّلة أم لا

        // 🔹 نسبة ربح افتراضية لو لم توجد قواعد خاصة للمخزن أو مجموعة الأصناف
        // تستخدم كنسبة ربح من "الخصم المرجّح" (مثال: 3 = 3%)
        [Precision(5, 2)]
        public decimal DefaultProfitPercent { get; set; } = 0m; // متغير: نسبة ربح افتراضية %

        // تواريخ الإنشاء والتعديل (نظام القوائم الموحد)
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // متغير: تاريخ إنشاء السجل
        public DateTime? UpdatedAt { get; set; }                   // متغير: آخر تعديل على السجل

        // علاقات ملاحة (اختيارية للاستخدام في الـ EF)
        public ICollection<WarehousePolicyRule> WarehouseRules { get; set; }
            = new List<WarehousePolicyRule>();     // متغير: القواعد المرتبطة بالمخازن لهذه السياسة

        public ICollection<ProductGroupPolicy> ProductGroupPolicies { get; set; }
            = new List<ProductGroupPolicy>();      // متغير: سياسات مجموعات الأصناف المرتبطة بهذه السياسة
    }
}
