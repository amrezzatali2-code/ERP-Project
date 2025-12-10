using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Models
{
    public class Governorate
    {
        [Display(Name = "كود المحافظة")]
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int GovernorateId { get; set; }   // رقم المحافظة (Identity)

        [Display(Name = "اسم المحافظة")]
        [Required, StringLength(100)]
        public string GovernorateName { get; set; } = default!; // اسم المحافظة

        // المدن التابعة للمحافظة
        public virtual ICollection<City> Cities { get; set; } = new List<City>();

        [Display(Name = "تاريخ الإنشاء")]
        public DateTime? CreatedAt { get; set; }            // تاريخ إنشاء السجل

        [Display(Name = "آخر تعديل")]
        public DateTime? UpdatedAt { get; set; }            // آخر تعديل على السجل

        // أحياء المحافظة
        public virtual ICollection<District> Districts { get; set; } = new List<District>();

        // المناطق التابعة للمحافظة
        public virtual ICollection<Area> Areas { get; set; } = new List<Area>();
    }
}
