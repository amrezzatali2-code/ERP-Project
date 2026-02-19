using System;
using System.ComponentModel.DataAnnotations;       // Display و Required
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;              // Precision
using ERP.Models;

namespace ERP.Models
{
    /// <summary>
    /// سطر تحويل مخزني: صنف واحد وكميته وتكلفته في تحويل معين.
    /// </summary>
    public class StockTransferLine
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Display(Name = "رقم السطر")]
        public int Id { get; set; }                  // متغير: كود السطر (PK)

        [Display(Name = "رقم التحويل")]
        [Required]
        public int StockTransferId { get; set; }     // متغير: كود رأس التحويل (FK إلى StockTransfer)

        [Display(Name = "ترتيب السطر")]
        public int LineNo { get; set; }              // متغير: رقم السطر داخل التحويل (1،2،3...)

        [Display(Name = "كود الصنف")]
        [Required]
        public int ProductId { get; set; }           // متغير: كود الصنف (FK إلى Products)

        [Display(Name = "كود التشغيلة")]
        public int? BatchId { get; set; }            // متغير: كود التشغيلة/الباتش (FK إلى Batches) اختياري

        [Display(Name = "الكمية المحولة")]
        [Required]
        public int Qty { get; set; }                 // متغير: الكمية المحولة من هذا الصنف

        [Display(Name = "تكلفة الوحدة")]
        [Precision(18, 4)]
        public decimal UnitCost { get; set; }        // متغير: تكلفة الوحدة وقت التحويل (لأغراض التقارير)

        [Precision(18, 2)]
        [Display(Name = "سعر الجمهور")]
        public decimal? PriceRetail { get; set; }    // سعر الجمهور (من الصنف أو التشغيلة)

        [Precision(5, 2)]
        [Display(Name = "الخصم المرجح %")]
        public decimal? WeightedDiscountPct { get; set; }  // الخصم المرجح من المشتريات في المخزن المصدر

        [Precision(5, 2)]
        [Display(Name = "الخصم %")]
        public decimal? DiscountPct { get; set; }    // خصم التحويل (مختلف عن المرجح — للربح من التحويل)

        [Display(Name = "ملاحظات")]
        [StringLength(500)]
        public string? Note { get; set; }            // متغير: ملاحظات خاصة بالسطر (اختياري)

        #region Navigation Properties  // خصائص الربط بين الجداول

        [Display(Name = "رأس التحويل")]
        public StockTransfer? StockTransfer { get; set; } // متغير: رأس التحويل الذي ينتمي له هذا السطر

        [Display(Name = "الصنف")]
        public Product? Product { get; set; }             // متغير: كائن الصنف

        [Display(Name = "التشغيلة")]
        public Batch? Batch { get; set; }                 // متغير: كائن التشغيلة (إن وجد)

        #endregion
    }
}
