using System;
using System.ComponentModel.DataAnnotations;          // تعليقات العرض Display + Key
using System.ComponentModel.DataAnnotations.Schema;   // DatabaseGenerated

namespace ERP.Models
{
    /// <summary>
    /// دفتر الحركة المخزنية (مصدر الحقيقة للمخزون).
    /// يسجل كل حركة دخول/خروج على مستوى: المخزن + الصنف + التشغيلة + الصلاحية.
    /// لا نخزن الرصيد الجاري في كل صف؛ بنحسبه وقت الطلب.
    /// RemainingQty يُملأ لسطور "الدخول" فقط لتسريع FIFO.
    /// </summary>
    public class StockLedger
    {
        // رقم القيد (مفتاح أساسي Identity)
        [Key]                                                       // تعليق: هذا هو المفتاح الأساسي للجدول
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]       // تعليق: زيادة تلقائية Identity
        [Display(Name = "رقم القيد")]
        public int EntryId { get; set; }

        // تاريخ/وقت الحركة
        [Display(Name = "تاريخ الحركة")]
        public DateTime TranDate { get; set; }

        // معرف المخزن (FK)
        [Display(Name = "كود المخزن")]
        public int WarehouseId { get; set; }

        // معرف الصنف (FK) — حافظنا على اسم موحد ProdId
        [Display(Name = "كود الصنف")]
        public int ProdId { get; set; }

        // رقم التشغيلة (قد يكون فارغًا لو الصنف غير مُشغّل)
        [Display(Name = "رقم التشغيلة")]
        public string? BatchNo { get; set; }

        // تاريخ الصلاحية (اختياري)
        [Display(Name = "تاريخ الصلاحية")]
        public DateTime? Expiry { get; set; }

        // كمية داخلة (عدد علب) — دائمًا >= 0
        [Display(Name = "كمية داخلة")]
        public int QtyIn { get; set; }

        // كمية خارجة (عدد علب) — دائمًا >= 0
        [Display(Name = "كمية خارجة")]
        public int QtyOut { get; set; }

        // تكلفة الوحدة: في الدخول = تكلفة الشراء/التحويل، وفي الخروج = تكلفة الدُفعات المستهلكة
        [Display(Name = "تكلفة الوحدة")]
        public decimal UnitCost { get; set; }

        // المتبقي من الدخلة (يُملأ فقط في سطور الدخول؛ null في الخروج)
        [Display(Name = "الكمية المتبقية من الدخلة")]
        public int? RemainingQty { get; set; }

        // نوع المصدر (Purchase / Sales / TransferIn / TransferOut / Adjustment / Opening ...)
        [Display(Name = "نوع الحركة")]
        public string SourceType { get; set; } = default!;

        // رقم المستند المصدر (فاتورة/إذن...)
        [Display(Name = "رقم المستند")]
        public int SourceId { get; set; } = default!;

        // رقم سطر المستند المصدر
        [Display(Name = "رقم سطر المستند")]
        public int SourceLine { get; set; }

        // ربط حركات نفس العملية (مثال: تحويل مخزني خروج+دخول)
        [Display(Name = "رقم مجموعة الحركة")]
        public int? MovementGroupId { get; set; }

        // المخزن المقابل في التحويل (اختياري)
        [Display(Name = "المخزن المقابل")]
        public int? CounterWarehouseId { get; set; }

        // سبب التسوية (اختياري)
        [Display(Name = "سبب التسوية")]
        public string? AdjustmentReason { get; set; }

        // ملاحظة (اختياري)
        [Display(Name = "ملاحظات")]
        public string? Note { get; set; }

        // من نفّذ الحركة (اختياري)
        [Display(Name = "المستخدم")]
        public int? UserId { get; set; }

        // ===== حقول التاريخ لنظام القوائم الموحد =====

        [Display(Name = "تاريخ الإنشاء")]              // تعليق: وقت إضافة القيد لأول مرة
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Display(Name = "تاريخ آخر تعديل")]           // تعليق: آخر مرة تم فيها تعديل بيانات القيد
        public DateTime? UpdatedAt { get; set; }
    }
}
