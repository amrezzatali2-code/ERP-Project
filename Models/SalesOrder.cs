using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;        // علشان [Key] و [Required] و [Timestamp]
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// هيدر أمر البيع:
    /// - أمر مبدئي من العميل لا يؤثر على المخزون.
    /// - لكل أمر عميل واحد ومخزن واحد وعدة سطور (SOLines).
    /// </summary>
    public class SalesOrder
    {
        // ===== المعرّف الأساسي لأمر البيع (الكود الذي يراه المستخدم) =====
        [Key]                                                   // مفتاح أساسي للجدول
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]   // رقم متزايد تلقائياً (Identity)
        [Display(Name = "رقم أمر البيع")]
        public int SOId { get; set; }                           // رقم أمر البيع

        // ===== بيانات عامة عن أمر البيع =====
        [Display(Name = "تاريخ أمر البيع")]
        public DateTime SODate { get; set; }                    // تاريخ إنشاء الأمر (تاريخ المستند نفسه)

        [Required]
        [Display(Name = "كود العميل")]
        public int CustomerId { get; set; }                     // كود العميل (FK على Customer)

        [Required]
        [Display(Name = "كود المخزن")]
        public int WarehouseId { get; set; }                    // كود المخزن الذي سيُحضَّر منه الطلب

        [Required]
        [StringLength(20)]
        [Display(Name = "حالة الأمر")]
        public string Status { get; set; } = "غير مرحلة";            // غير مرحلة / محول / ملغى (مثل طلب الشراء)

        [Display(Name = "محوّل إلى فاتورة مبيعات؟")]
        public bool IsConverted { get; set; }                     // true بعد التحويل إلى فاتورة مبيعات

        [StringLength(500)]
        [Display(Name = "ملاحظات")]
        public string? Notes { get; set; }                      // ملاحظات داخلية على أمر البيع

        [Required]
        [StringLength(50)]
        [Display(Name = "أنشأه")]
        public string CreatedBy { get; set; } = string.Empty;   // اسم/كود المستخدم الذي أنشأ الأمر

        // ===== تواريخ الإنشاء والتعديل (للتتبع) =====
        [Display(Name = "تاريخ الإنشاء")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;               // تاريخ ووقت إنشاء السجل في قاعدة البيانات

        [Display(Name = "آخر تحديث")]
        public DateTime? UpdatedAt { get; set; }                // آخر تعديل على السجل (إن وُجد)

        // ===== رقم المزامنة (Concurrency) =====
        [Timestamp]                                             // RowVersion لمنع تعارض التعديل المتوازي
        [Display(Name = "رقم المزامنة")]
        public byte[] RowVersion { get; set; } = default!;      // EF بيستخدمه عند الـ Update

        [Display(Name = "إجمالي الكمية المطلوبة")]
        public int TotalQtyRequested { get; set; }       // مجموع الكميات في كل السطور

        [Display(Name = "إجمالي القيمة المتوقعة")]
        [Precision(18, 4)]
        public decimal ExpectedItemsTotal { get; set; }  // مجموع (الكمية × السعر المتوقع)


        // ===== علاقات الملاحة (Navigation Properties) =====

        [Display(Name = "العميل")]
        public virtual Customer Customer { get; set; } = null!; // كيان العميل المرتبط بهذا الأمر

        [Display(Name = "المخزن")]
        public virtual Warehouse Warehouse { get; set; } = null!; // المخزن المرتبط بأمر البيع

        [Display(Name = "سطور أمر البيع")]
        public virtual ICollection<SOLine> Lines { get; set; }  // كل سطور أمر البيع
            = new List<SOLine>();
    }
}
