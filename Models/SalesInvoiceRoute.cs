using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// بيانات خط السير لكل فاتورة مبيعات — سجل واحد لكل فاتورة (عدد الشنط، البواكي، الكراتين، أصناف الثلاجة، …).
    /// </summary>
    public class SalesInvoiceRoute
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        [Display(Name = "رقم الفاتورة")]
        public int SIId { get; set; }

        [ForeignKey(nameof(SIId))]
        public virtual SalesInvoice SalesInvoice { get; set; } = null!;

        [Display(Name = "الكونترول")]
        public int? ControlEmployeeId { get; set; }
        [ForeignKey(nameof(ControlEmployeeId))]
        public virtual Employee? ControlEmployee { get; set; }

        [Display(Name = "المحضر")]
        public int? PreparerEmployeeId { get; set; }
        [ForeignKey(nameof(PreparerEmployeeId))]
        public virtual Employee? PreparerEmployee { get; set; }

        [Display(Name = "الموزع")]
        public int? DistributorEmployeeId { get; set; }
        [ForeignKey(nameof(DistributorEmployeeId))]
        public virtual Employee? DistributorEmployee { get; set; }

        [Display(Name = "عدد الشنط")]
        public int BagsCount { get; set; }

        [Display(Name = "عدد البواكي")]
        public int PacketsCount { get; set; }

        [Display(Name = "عدد الكراتين")]
        public int CartonsCount { get; set; }

        [Display(Name = "عدد أصناف الثلاجة")]
        public int FridgeItemsCount { get; set; }

        [Display(Name = "عدد علب أصناف الثلاجة")]
        public int FridgeBoxesCount { get; set; }

        [StringLength(500)]
        [Display(Name = "ملاحظات")]
        public string? Notes { get; set; }

        [Display(Name = "تاريخ آخر تعديل")]
        public DateTime? UpdatedAt { get; set; }

        public virtual ICollection<SalesInvoiceRouteFridgeLine> FridgeLines { get; set; } = new List<SalesInvoiceRouteFridgeLine>();
    }
}
