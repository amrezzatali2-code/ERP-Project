using System.ComponentModel.DataAnnotations;

namespace ERP.ViewModels
{
    public class ChangePasswordViewModel
    {
        [Required(ErrorMessage = "اسم المستخدم مطلوب.")]
        [Display(Name = "اسم المستخدم")]
        [StringLength(50)]
        public string UserName { get; set; } = string.Empty;

        [Required(ErrorMessage = "كلمة المرور الحالية مطلوبة.")]
        [DataType(DataType.Password)]
        [Display(Name = "كلمة المرور الحالية")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "كلمة المرور الجديدة مطلوبة.")]
        [StringLength(256, MinimumLength = 6, ErrorMessage = "كلمة المرور الجديدة يجب أن تكون 6 أحرف على الأقل.")]
        [DataType(DataType.Password)]
        [Display(Name = "كلمة المرور الجديدة")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "تأكيد كلمة المرور مطلوب.")]
        [DataType(DataType.Password)]
        [Display(Name = "تأكيد كلمة المرور الجديدة")]
        [Compare(nameof(NewPassword), ErrorMessage = "تأكيد كلمة المرور غير مطابق لكلمة المرور الجديدة.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
