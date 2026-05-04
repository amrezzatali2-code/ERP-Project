using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// يمثل كل عبوة/وحدة دواء يمكن تتبعها بشكل مستقل عبر UID/Serial.
    /// هذه الطبقة لا تغيّر منطق المخزون الحالي، بل تضيف تتبعًا تفصيليًا فوقه.
    /// </summary>
    public class ItemUnit
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [Display(Name = "كود الصنف")]
        public int ProdId { get; set; }

        [Display(Name = "كود التشغيلة")]
        public int? BatchId { get; set; }

        [Required]
        [Display(Name = "المخزن الحالي")]
        public int WarehouseId { get; set; }

        [Required]
        [StringLength(100)]
        [Display(Name = "المعرف الفريد للعبوة")]
        public string Uid { get; set; } = string.Empty;

        [StringLength(30)]
        [Display(Name = "GTIN")]
        public string? Gtin { get; set; }

        [StringLength(100)]
        [Display(Name = "الرقم التسلسلي")]
        public string? SerialNo { get; set; }

        [StringLength(50)]
        [Display(Name = "رقم التشغيلة")]
        public string? BatchNo { get; set; }

        [Display(Name = "تاريخ الصلاحية")]
        public DateTime? Expiry { get; set; }

        [Required]
        [StringLength(30)]
        [Display(Name = "حالة العبوة")]
        public string Status { get; set; } = "InStock";

        [StringLength(50)]
        [Display(Name = "مصدر آخر حركة")]
        public string? CurrentSourceType { get; set; }

        [Display(Name = "رقم مستند آخر حركة")]
        public int? CurrentSourceId { get; set; }

        [Display(Name = "رقم سطر آخر حركة")]
        public int? CurrentSourceLineNo { get; set; }

        [Display(Name = "الصنف خاضع للتتبع")]
        public bool IsTracked { get; set; } = true;

        [Display(Name = "تاريخ الإنشاء")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Display(Name = "آخر تعديل")]
        public DateTime? UpdatedAt { get; set; }

        public Product? Product { get; set; }
        public Batch? Batch { get; set; }
        public Warehouse? Warehouse { get; set; }
    }
}
