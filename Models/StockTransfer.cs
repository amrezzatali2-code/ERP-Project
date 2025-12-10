using System;
using System.Collections.Generic;                  // القوائم List و ICollection
using System.ComponentModel.DataAnnotations;       // خصائص العرض Display و Required
using System.ComponentModel.DataAnnotations.Schema;
using ERP.Models;

namespace ERP.Models
{
    /// <summary>
    /// جدول التحويلات بين المخازن (الهيدر).
    /// كل صف يمثل تحويل واحد من مخزن إلى مخزن آخر.
    /// </summary>
    public class StockTransfer
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Display(Name = "رقم التحويل")]
        public int Id { get; set; }                     // متغير: كود التحويل (PK)

        [Display(Name = "تاريخ التحويل")]
        [DataType(DataType.DateTime)]
        public DateTime TransferDate { get; set; } = DateTime.Now;
        // متغير: تاريخ ووقت التحويل بين المخازن

        [Display(Name = "من مخزن")]
        [Required]
        public int FromWarehouseId { get; set; }        // متغير: كود المخزن المحوَّل منه

        [Display(Name = "إلى مخزن")]
        [Required]
        public int ToWarehouseId { get; set; }          // متغير: كود المخزن المحوَّل إليه

        [Display(Name = "ملاحظات")]
        [StringLength(500)]
        public string? Note { get; set; }               // متغير: ملاحظات على التحويل

        [Display(Name = "المستخدم")]
        public int? UserId { get; set; }                // متغير: كود المستخدم الذي أنشأ التحويل

        [Display(Name = "تاريخ الإنشاء")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        // متغير: تاريخ إنشاء السجل

        [Display(Name = "آخر تعديل")]
        public DateTime? UpdatedAt { get; set; }
        // متغير: آخر وقت تعديل

        #region Navigation Properties  // خصائص الربط بين الجداول

        [Display(Name = "المخزن المصدر")]
        public Warehouse? FromWarehouse { get; set; }   // متغير: كائن المخزن المصدر

        [Display(Name = "المخزن الوجهة")]
        public Warehouse? ToWarehouse { get; set; }     // متغير: كائن المخزن الوجهة

        [Display(Name = "المستخدم")]
        public User? User { get; set; }                 // متغير: كائن المستخدم

        public ICollection<StockTransferLine> Lines { get; set; }
            = new List<StockTransferLine>();           // متغير: سطور الأصناف التابعة للتحويل

        #endregion
    }
}
