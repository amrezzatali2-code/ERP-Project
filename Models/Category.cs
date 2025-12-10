using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    // كلاس الفئة — يخزن تعريف فئات الأصناف (مثل أدوية بشرى، بيطرى، تجميل…)
    // أسماء أعمدة قاعدة البيانات إنجليزي، وعرض الحقول في الواجهات بالعربي.
    public class Category
    {
        [Key]                                                   // هذا هو المفتاح الأساسي للجدول
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]   // جعل كود الفئة Identity (يزيد تلقائيًا)
        [Display(Name = "كود الفئة")]                          // اسم الحقل في الشاشات
        public int CategoryId { get; set; }                     // متغير: رقم الفئة (PK)

        [Display(Name = "اسم الفئة")]                          // اسم الفئة للعرض في الشاشات
        public string CategoryName { get; set; } = null!;       // متغير: اسم الفئة (نص)

        // ===== حقول التاريخ لنظام القوائم الموحد =====

        [Display(Name = "تاريخ الإنشاء")]                      // يظهر في قائمة الفئات
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        // متغير: تاريخ ووقت إنشاء سجل الفئة
        // يتم إعطاؤه قيمة افتراضية عند إضافة الفئة لأول مرة

        [Display(Name = "تاريخ آخر تعديل")]                   // لعرض آخر تعديل تم على بيانات الفئة
        public DateTime? UpdatedAt { get; set; }
        // متغير: آخر مرة تم فيها تعديل بيانات هذه الفئة
        // يبقى null لو لم يحدث أي تعديل بعد الإنشاء

        // ===== العلاقة مع الأصناف =====

        public ICollection<Product> Products { get; set; } = new List<Product>();
        // متغير: قائمة الأصناف التي تنتمي لهذه الفئة (علاقة واحد إلى كثير)
    }
}
