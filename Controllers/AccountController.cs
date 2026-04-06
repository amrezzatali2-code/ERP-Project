using System.Security.Claims;
using System.Threading.Tasks;
using ERP.Data;
using ERP.Infrastructure;
using ERP.Models;
using ERP.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Controllers
{
    /// <summary>
    /// إعدادات الحساب الشخصي (تغيير كلمة المرور) — أي مستخدم مسجّل دخول.
    /// </summary>
    [Authorize]
    public class AccountController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IUserActivityLogger _activityLogger;

        public AccountController(AppDbContext db, IUserActivityLogger activityLogger)
        {
            _db = db;
            _activityLogger = activityLogger;
        }

        [HttpGet]
        public IActionResult ChangePassword()
        {
            var name = User?.Identity?.Name?.Trim() ?? "";
            var model = new ChangePasswordViewModel { UserName = name };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdClaim, out var userId))
            {
                ModelState.AddModelError(string.Empty, "تعذر التحقق من هوية المستخدم.");
                return View(model);
            }

            var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "تعذر العثور على المستخدم.");
                return View(model);
            }

            if (!user.IsActive)
            {
                ModelState.AddModelError(string.Empty, "حسابك موقوف؛ لا يمكن تغيير كلمة المرور.");
                return View(model);
            }

            var enteredName = (model.UserName ?? string.Empty).Trim();
            if (!string.Equals(enteredName, user.UserName, StringComparison.Ordinal))
            {
                ModelState.AddModelError(nameof(model.UserName), "اسم المستخدم غير مطابق لحسابك الحالي.");
            }

            if (!PasswordHasher.VerifyPassword(model.CurrentPassword ?? "", user.PasswordHash))
            {
                ModelState.AddModelError(nameof(model.CurrentPassword), "كلمة المرور الحالية غير صحيحة.");
            }

            if (!ModelState.IsValid)
                return View(model);

            user.PasswordHash = PasswordHasher.HashPassword(model.NewPassword);
            user.UpdatedAt = DateTime.Now;
            await _db.SaveChangesAsync();

            await _activityLogger.LogAsync(
                UserActionType.Edit,
                "User",
                user.UserId,
                "تغيير كلمة المرور من شاشة «تغيير كلمة المرور» (الإعدادات)");

            TempData["SuccessMessage"] = "تم تغيير كلمة المرور بنجاح.";
            return RedirectToAction(nameof(ChangePassword));
        }
    }
}
