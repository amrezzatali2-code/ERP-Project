using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// سطر صنف ثلاجة في بيانات خط السير لفاتورة مبيعات — صنف + كمية.
    /// </summary>
    public class SalesInvoiceRouteFridgeLine
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Display(Name = "رقم الفاتورة")]
        public int SIId { get; set; }
        [ForeignKey(nameof(SIId))]
        public virtual SalesInvoiceRoute Route { get; set; } = null!;

        [Display(Name = "الصنف")]
        public int ProductId { get; set; }
        [ForeignKey(nameof(ProductId))]
        public virtual Product Product { get; set; } = null!;

        [Display(Name = "الكمية")]
        public int Qty { get; set; }
    }
}
