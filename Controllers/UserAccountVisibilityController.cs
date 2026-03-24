using ERP.Filters;
using ERP.Security;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Controllers
{
    /// <summary>
    /// إعداد الحسابات المسموح رؤيتها أصبح من <b>صلاحيات الأدوار</b> (جدول <c>RoleAccountVisibilityOverrides</c>).
    /// هذا الكنترولر يوجّه الروابط القديمة فقط.
    /// </summary>
    public class UserAccountVisibilityController : Controller
    {
        [RequirePermission("RolePermissions.Index")]
        public IActionResult Index()
        {
            TempData["Info"] = "تم دمج «الحسابات المسموح رؤيتها» مع شاشة صلاحيات الأدوار: من القائمة افتح «صلاحيات الأدوار» ثم «تعديل» للدور، وفي أسفل الصفحة حدّد الحسابات تحت عنوان «الحسابات المسموح رؤيتها لكل مستخدم (لهذا الدور)».";
            return RedirectToAction(nameof(RolePermissionsController.Index), "RolePermissions");
        }

        [RequirePermission("RolePermissions.Index")]
        public IActionResult Edit(int userId)
        {
            TempData["Info"] = "التعديل أصبح على مستوى الدور: استخدم «صلاحيات الأدوار» لكل دور، ثم اربط المستخدم بالأدوار من «أدوار المستخدمين». إن وُجد إعداد قديم على المستخدم فلا يزال يُحترم حتى تزيله يدوياً من قاعدة البيانات أو تنسخه للأدوار.";
            return RedirectToAction(nameof(RolePermissionsController.Index), "RolePermissions");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("RolePermissions.Index")]
        public IActionResult Update(int userId)
        {
            TempData["Info"] = "لم يُحفظ شيء من هذه الصفحة. استخدم «صلاحيات الأدوار» لتحديث الحسابات المسموح رؤيتها.";
            return RedirectToAction(nameof(RolePermissionsController.Index), "RolePermissions");
        }
    }
}
