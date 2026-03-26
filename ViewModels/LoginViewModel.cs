using System.Collections.Generic;
using System.ComponentModel.DataAnnotations; // خصائص التحقق من البيانات

namespace ERP.ViewModels
{
    /// <summary>
    /// عنصر قائمة المستخدمين في شاشة الدخول (لا نستخدم anonymous مع ViewBag لأن الرازور يرى العنصر كـ object).
    /// </summary>
    public class LoginUserListItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// موديل بسيط لبيانات شاشة تسجيل الدخول.
    /// </summary>
    public class LoginViewModel
    {
        [Required(ErrorMessage = "اسم المستخدم مطلوب")]
        [Display(Name = "اسم المستخدم")]
        public string UserName { get; set; } = string.Empty;   // متغير: اسم الدخول

        [Required(ErrorMessage = "كلمة المرور مطلوبة")]
        [DataType(DataType.Password)]
        [Display(Name = "كلمة المرور")]
        public string Password { get; set; } = string.Empty;   // متغير: كلمة المرور التي يكتبها المستخدم

      

        public string? ReturnUrl { get; set; }                 // متغير: عنوان الرجوع بعد نجاح الدخول (اختياري)
    }
}
