using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;          // لاستخدام Display و Key
using System.ComponentModel.DataAnnotations.Schema;  // لاستخدام DatabaseGenerated
using Microsoft.EntityFrameworkCore;                 // لاستخدام Precision للـ decimal

namespace ERP.Models
{
    /// <summary>
    /// رأس طلب الشراء: لا يُحدِّث المخزون. لاحقاً نحوله إلى فاتورة شراء.
    /// </summary>
    public class PurchaseRequest
    {
        // ========= الهوية / المفتاح الأساسي =========

        [Key]                                              // المفتاح الأساسي للجدول
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Display(Name = "رقم طلب الشراء")]
        public int PRId { get; set; }                      // رقم الطلب (ترقيم تلقائي من النظام)

        // ========= تواريخ الطلب =========

        [Display(Name = "تاريخ الطلب")]
        public DateTime PRDate { get; set; }               // تاريخ الطلب (من ناحية الشغل)

        [Display(Name = "مطلوب قبل تاريخ")]
        public DateTime? NeedByDate { get; set; }          // تاريخ مطلوب قبل (اختياري)

        // ========= ربط المخزن =========

        [Display(Name = "كود المخزن")]
        public int WarehouseId { get; set; }               // معرّف المخزن

        [Display(Name = "المخزن")]
        public virtual Warehouse Warehouse { get; set; } = null!;
        // ربط طلب الشراء بالمخزن الذي سيتم التوريد إليه

        // ========= ربط طلب الشراء بالعميل / المورد =========

        [Display(Name = "كود العميل")]
        public int CustomerId { get; set; }                // معرّف العميل من جدول Customer

        [Display(Name = "العميل")]
        public virtual Customer Customer { get; set; } = null!;
        // العميل / المورد المرتبط بالطلب

        // ========= إجماليات طلب الشراء =========

        [Display(Name = "إجمالي الكمية المطلوبة")]
        public int TotalQtyRequested { get; set; }         // مجموع الكميات فى كل السطور

        [Display(Name = "إجمالي التكلفة المتوقعة")]
        [Precision(18, 4)]
        public decimal ExpectedItemsTotal { get; set; }    // مجموع (الكمية × التكلفة المتوقعة للوحدة)

        [Display(Name = "قيمة الضريبة")]
        [Precision(18, 2)]
        public decimal TaxAmount { get; set; }             // ضريبة تقديرية على الطلب (مثل فاتورة المبيعات)

        // ========= بيانات إنشاء الطلب =========

        [Display(Name = "تم الطلب بواسطة")]
        public string RequestedBy { get; set; } = null!;
        // اسم / كود الشخص الذي طلب الأصناف (موظف المشتريات مثلاً)

        [Display(Name = "أنشأه المستخدم")]
        public string CreatedBy { get; set; } = null!;
        // كود مستخدم النظام الذي أدخل الطلب فعلياً على البرنامج

        [Display(Name = "تاريخ الإنشاء")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        // وقت إنشاء السجل في قاعدة البيانات

        [Display(Name = "آخر تعديل")]
        public DateTime? UpdatedAt { get; set; }
        // وقت آخر تعديل على الطلب (لو تم تعديله بعد الإنشاء)

        // ========= حالة الطلب وملاحظات =========

        [Display(Name = "الحالة")]
        public string Status { get; set; } = "Draft";
        // Draft     = مسودة
        // Converted = تم التحويل إلى فاتورة شراء
        // Cancelled = تم إلغاء الطلب

        [Display(Name = "تم التحويل إلى فاتورة شراء؟")]
        public bool IsConverted { get; set; }              // عمود صريح: محوَّل / غير محوَّل

        [Display(Name = "ملاحظات")]
        public string? Notes { get; set; }
        // أي ملاحظات إضافية على طلب الشراء

        // ========= سطور الطلب =========

        [Display(Name = "سطور طلب الشراء")]
        public virtual ICollection<PRLine> Lines { get; set; } = new List<PRLine>();
        // كل الأصناف والكميات المطلوبة في هذا الطلب

        // ========= فواتير الشراء الناتجة عن هذا الطلب =========

        [Display(Name = "فواتير الشراء الناتجة")]
        public virtual ICollection<PurchaseInvoice> PurchaseInvoices { get; set; } = new List<PurchaseInvoice>();
        // كل فواتير الشراء التي تم إنشاؤها من هذا الطلب (مربوطة بـ RefPRId في PurchaseInvoice)
    }
}
