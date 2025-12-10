using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// جدول ترقيم المستندات المستقل (بيع/مرتجع... لكل سلسلة SeriesCode)
    /// يستخدم أثناء إنشاء/ترحيل المستند لمنح رقم فريد سريع وآمن.
    /// </summary>
    public class DocumentSeries
    {
        // معرّف السلسلة (PK, Identity) — رقم داخلي للسجل
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int SeriesId { get; set; }

        // نوع المستند (SalesInvoice / SalesReturn ...)
        [Required, StringLength(30)]
        public string DocType { get; set; } = null!;

        // كود السلسلة (A, BR1, ...)
        [Required, StringLength(20)]
        public string SeriesCode { get; set; } = null!;

        // السنة المالية (مثال "2025")، أو null لو السياسة مستمرة
        [StringLength(4)]
        public string? FiscalYear { get; set; }

        // سياسة إعادة الترقيم: Continuous أو Yearly
        [Required, StringLength(15)]
        public string ResetPolicy { get; set; } = "Continuous";

        // العداد الحالي (آخر رقم مُستخدم). الرقم التالي = CurrentNo + 1
        public int CurrentNo { get; set; } = 0;

        // عرض الرقم (عدد الخانات – Padding)، مثال 6 ⇒ 000123
        public int NumberWidth { get; set; } = 6;

        // بادئة اختيارية لرقم المستند (مثال: "A-2025-")
        [StringLength(20)]
        public string? Prefix { get; set; }

        // تاريخ إنشاء السلسلة
        public DateTime CreatedAt { get; set; }

        // تاريخ آخر تعديل (لو حصل)
        public DateTime? UpdatedAt { get; set; }

        // حقل للمزامنة المتفائلة ومنع التعديل المتزامن
        [Timestamp]                                // تعليق: يخلي EF يتعامل معه كـ RowVersion
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }
}
