using System;
using System.ComponentModel.DataAnnotations;

namespace ERP.Models
{
    /// <summary>
    /// جدول سجل تغييرات "سعر الجمهور" لكل صنف
    /// هذا الجدول لا يتم إدخال بياناته يدويًا؛
    /// يتم إنشاء صف جديد تلقائيًا عند تعديل سعر الجمهور (PriceRetail) في جدول Product.
    /// </summary>
    public class ProductPriceHistory
    {
        [Key]
        [Display(Name = "كود التغيير")]
        public int PriceChangeId { get; set; }   // مفتاح أساسي للجدول (Identity)

        [Required]
        [Display(Name = "كود الصنف")]
        public int ProdId { get; set; }          // رقم الصنف المرتبط من جدول Product

        [Display(Name = "السعر القديم")]
        public decimal OldPrice { get; set; }    // سعر الجمهور قبل التعديل

        [Display(Name = "السعر الجديد")]
        public decimal NewPrice { get; set; }    // سعر الجمهور بعد التعديل

        [Display(Name = "تاريخ التغيير")]
        [DataType(DataType.DateTime)]           // نوع البيانات تاريخ/وقت لعرضه بشكل مناسب في الفورمات
        public DateTime ChangeDate { get; set; } = DateTime.UtcNow;
        // وقت حدوث التغيير نفسه (نستخدمه في الفلترة في شاشة Index)
        // القيمة الافتراضية الآن = التوقيت الحالي UTC، ويمكنك تغييرها لـ Now لو حابب تعتمد التوقيت المحلي

        [Display(Name = "بواسطة")]
        public string? ChangedBy { get; set; }   // اسم المستخدم الذي عدّل السعر (اختياري)

        [Display(Name = "سبب التغيير")]
        public string? Reason { get; set; }      // سبب تعديل السعر (اختياري)

        // ===== حقول التاريخ القياسية لنظام القوائم الموحد =====

        [Display(Name = "تاريخ الإنشاء")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        // تاريخ إنشاء سجل التغيير (عادة سيكون مساويًا لـ ChangeDate لكن نحتفظ به
        // لتوحيد النظام مع باقي الجداول، وللبحث/الترتيب الموحد)

        [Display(Name = "تاريخ آخر تعديل")]
        public DateTime? UpdatedAt { get; set; }
        // لو احتجنا نعدّل على سبب التغيير أو بيانات السجل نفسه بعد إنشائه
        // هنا نسجّل آخر وقت تعديل

        // ===== خاصية الملاحة للصنف =====

        [Display(Name = "الصنف")]
        public Product? Product { get; set; }    // للوصول لاسم الصنف وباقي بياناته من جدول Product
        // EF Core هيربط تلقائيًا بين ProdId و Product كـ Foreign Key
    }
}
