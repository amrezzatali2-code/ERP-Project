using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    /// <summary>
    /// خطوط السير — لتجميع العملاء وتجهيز الفواتير حسب الخط.
    /// </summary>
    public class Route
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Display(Name = "كود الخط")]
        public int Id { get; set; }

        [Required, StringLength(100)]
        [Display(Name = "اسم خط السير")]
        public string Name { get; set; } = null!;

        [StringLength(20)]
        [Display(Name = "الكود")]
        public string? Code { get; set; }

        [Display(Name = "ترتيب العرض")]
        public int SortOrder { get; set; }

        [Display(Name = "فعال")]
        public bool IsActive { get; set; } = true;

        [Display(Name = "تاريخ الإنشاء")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Display(Name = "تاريخ آخر تعديل")]
        public DateTime? UpdatedAt { get; set; }

        public ICollection<Customer> Customers { get; set; } = new List<Customer>();
    }
}
