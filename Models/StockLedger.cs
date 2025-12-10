using System;
using System.ComponentModel.DataAnnotations;          // تعليقات العرض Display + Required
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
        [Key]                                                       // تعليق: هذا هو المفتاح الأساسي للجدول
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]       // تعليق: زيادة تلقائية Identity
        [Display(Name = "رقم القيد")]
        public int EntryId { get; set; }                            // متغير: رقم القيد فى دفتر الحركة

        // تاريخ/وقت الحركة
        [Display(Name = "تاريخ الحركة")]
        public DateTime TranDate { get; set; }                      // متغير: تاريخ/وقت الحركة

        // معرف المخزن (FK)
        [Display(Name = "كود المخزن")]
        public int WarehouseId { get; set; }                        // متغير: كود المخزن الذى حدثت به الحركة

        // معرف الصنف (FK) — اسم موحد ProdId
        [Display(Name = "كود الصنف")]
        public int ProdId { get; set; }                             // متغير: كود الصنف

        // رقم التشغيلة (كما هو مكتوب على العلبة)
        [Display(Name = "رقم التشغيلة")]
        public string? BatchNo { get; set; }                        // متغير: رقم التشغيلة (نحتفظ به نصيًا)

        // تاريخ الصلاحية (اختياري)
        [Display(Name = "تاريخ الصلاحية")]
        public DateTime? Expiry { get; set; }                       // متغير: تاريخ الصلاحية لو الصنف مُشغّل

        // 🔹 كود التشغيلة (اختياري) للربط مع جدول Batch
        [Display(Name = "كود التشغيلة (BatchId)")]
        public int? BatchId { get; set; }                           // متغير: مفتاح أجنبي اختيارى إلى Batch

        // كمية داخلة (عدد علب) — دائمًا >= 0
        [Display(Name = "كمية داخلة")]
        public int QtyIn { get; set; }                              // متغير: كمية دخول

        // كمية خارجة (عدد علب) — دائمًا >= 0
        [Display(Name = "كمية خارجة")]
        public int QtyOut { get; set; }                             // متغير: كمية خروج

        // تكلفة الوحدة: فى الدخول = تكلفة الشراء/التحويل، فى الخروج = تكلفة الدُفعات المستهلكة
        [Display(Name = "تكلفة الوحدة")]
        public decimal UnitCost { get; set; }                       // متغير: تكلفة الوحدة

        // المتبقى من الدخلة (يُملأ فقط فى سطور الدخول؛ null فى الخروج)
        [Display(Name = "الكمية المتبقية من الدخلة")]
        public int? RemainingQty { get; set; }                      // متغير: الكمية المتبقية من هذه الدخلة

        // نوع المصدر (Purchase / Sales / TransferIn / TransferOut / Adjustment / Opening ...)
        [Display(Name = "نوع الحركة")]
        public string SourceType { get; set; } = default!;          // متغير: نوع الحركة

        // رقم المستند المصدر (فاتورة/إذن...)
        [Display(Name = "رقم المستند")]
        public int SourceId { get; set; }                           // متغير: رقم المستند المصدر

        // رقم سطر المستند المصدر
        [Display(Name = "رقم سطر المستند")]
        public int SourceLine { get; set; }                         // متغير: رقم السطر فى المستند

        // ربط حركات نفس العملية (مثال: تحويل مخزنى خروج+دخول)
        [Display(Name = "رقم مجموعة الحركة")]
        public int? MovementGroupId { get; set; }                   // متغير: رقم مجموعة الحركة

        // المخزن المقابل فى التحويل (اختياري)
        [Display(Name = "المخزن المقابل")]
        public int? CounterWarehouseId { get; set; }                // متغير: المخزن المقابل (للتحويل)

        // سبب التسوية (اختياري)
        [Display(Name = "سبب التسوية")]
        public string? AdjustmentReason { get; set; }               // متغير: سبب التسوية

        // ملاحظة (اختياري)
        [Display(Name = "ملاحظات")]
        public string? Note { get; set; }                           // متغير: ملاحظات إضافية

        // من نفّذ الحركة (اختياري)
        [Display(Name = "المستخدم")]
        public int? UserId { get; set; }                            // متغير: رقم المستخدم الذى نفّذ الحركة

        // ===== حقول التاريخ لنظام القوائم الموحد =====

        [Display(Name = "تاريخ الإنشاء")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;  // متغير: وقت إضافة القيد لأول مرة

        [Display(Name = "تاريخ آخر تعديل")]
        public DateTime? UpdatedAt { get; set; }                   // متغير: آخر مرة تم فيها تعديل بيانات القيد

        // ===== علاقات (Navigation Properties) =====

        public Product? Product { get; set; }                       // تعليق: الصنف المرتبط بالحركة (FK → ProdId)
        public Warehouse? Warehouse { get; set; }                   // تعليق: المخزن المرتبط بالحركة (FK → WarehouseId)
        public Batch? Batch { get; set; }                           // تعليق: التشغيلة المرتبطة (اختياريًا) (FK → BatchId)
        // ممكن لاحقًا نضيف:
        // public User? User { get; set; } لو عندك موديل User واضح
    }
}
