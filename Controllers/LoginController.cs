using System.Linq;
using System.Security.Claims;                        // الـ Claims الخاصة بالمستخدم
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ERP.Data;                                     // AppDbContext
using ERP.Models;                                   // User
using ERP.ViewModels;                               // LoginViewModel
using Microsoft.AspNetCore.Authentication;          // SignInAsync / SignOutAsync
using Microsoft.AspNetCore.Authentication.Cookies;  // CookieAuthenticationDefaults
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;                // FirstOrDefaultAsync
using Microsoft.AspNetCore.Authorization;           // AllowAnonymous / Authorize

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر مسئول عن شاشة تسجيل الدخول والخروج.
    /// </summary>
    [AllowAnonymous]   // تعليق: هذا الكنترولر مستثنى من سياسة التوثيق العامة
    public class LoginController : Controller
    {
        private readonly AppDbContext _db;  // متغير: سياق قاعدة البيانات

        public LoginController(AppDbContext db)
        {
            _db = db;
        }

        // ================================================================
        // دالة مساعدة: تحميل أسماء المستخدمين (لاستخدامها فى datalist)
        // - نفس فكرة العميل فى الفواتير
        // - نظهر فقط المستخدمين النشطين IsActive = true
        // ================================================================
        private async Task LoadUsersForLoginAsync()
        {
            // تحميل أسماء المستخدمين النشطين فقط (مرتبة)
            var users = await _db.Users
                .AsNoTracking()
                .Where(u => u.IsActive)                 // تعليق: نظهر المستخدمين النشطين فقط
                .OrderBy(u => u.UserName)               // تعليق: ترتيب أبجدي
                .Select(u => new
                {
                    Id = u.UserId,                      // متغير: كود المستخدم
                    Name = u.UserName                   // متغير: اسم المستخدم
                })
                .ToListAsync();

            // متغير: إرسال الأسماء للـ View (للاستخدام فى datalist)
            ViewBag.Users = users;
        }

        // ================================================================
        // GET: /Login
        // عرض شاشة تسجيل الدخول
        // ================================================================
        [HttpGet]
        public async Task<IActionResult> Index(string? returnUrl = null)
        {
            // متغير: موديل شاشة الدخول
            var model = new LoginViewModel
            {
                ReturnUrl = returnUrl     // تعليق: يتم حفظ الصفحة المطلوب الرجوع لها بعد الـ Login
            };

            // ✅ تجهيز قائمة أسماء المستخدمين لشاشة الدخول (مثل العميل)
            await LoadUsersForLoginAsync();

            return View("Index", model);   // تعليق: نرجع View باسم Index.cshtml
        }

        // ================================================================
        // POST: /Login
        // استقبال بيانات تسجيل الدخول والتحقق من المستخدم
        // ================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(LoginViewModel model)
        {
            // ✅ لازم نحمل الأسماء أيضًا عند أي رجوع للشاشة بسبب خطأ
            await LoadUsersForLoginAsync();

            // تحقق من صحة البيانات في الفورم
            if (!ModelState.IsValid)
            {
                return View("Index", model);   // تعليق: إعادة عرض الشاشة مع رسائل الخطأ
            }

            // متغير: اسم المستخدم بعد إزالة الفراغات الزائدة
            var userName = (model.UserName ?? string.Empty).Trim();

            // البحث عن المستخدم في قاعدة البيانات (يكون نشط)
            var user = await _db.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u =>
                    u.UserName == userName &&
                    u.IsActive);

            if (user == null)
            {
                // لو لم نجد مستخدم بهذا الاسم
                ModelState.AddModelError(string.Empty, "اسم المستخدم أو كلمة المرور غير صحيحة.");
                return View("Index", model);
            }

            // التحقق من كلمة المرور
            if (!VerifyPassword(model.Password, user.PasswordHash))
            {
                ModelState.AddModelError(string.Empty, "اسم المستخدم أو كلمة المرور غير صحيحة.");
                return View("Index", model);
            }

            // تحديث آخر وقت دخول (سجل النشاط لا يسجّل الدخول — فقط تعديل/حذف)
            user.LastLoginAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            // تجهيز الـ Claims الخاصة بالمستخدم
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),       // رقم المستخدم
                new Claim(ClaimTypes.Name, user.UserName),                          // اسم الدخول
                new Claim("DisplayName", user.DisplayName ?? user.UserName),        // الاسم المعروض
                new Claim("IsAdmin", user.IsAdmin ? "true" : "false"),              // هل أدمن؟
            }
            .Concat(
                // إضافة أسماء الأدوار فى الـ Claims
                user.UserRoles
                    .Where(ur => ur.Role != null)
                    .Select(ur => new Claim(ClaimTypes.Role, ur.Role!.Name))
            );

            // متغير: هوية المستخدم
            var identity = new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme
            );

            // متغير: كائن المستخدم الكامل
            var principal = new ClaimsPrincipal(identity);

            // إعدادات الكوكيز
            var authProps = new AuthenticationProperties
            {
                // متغير: الكوكي سيشن فقط ⇒ تنتهى عند غلق المتصفح
                IsPersistent = false
                // لا نحدد ExpiresUtc علشان ما تبقاش Persistent Cookie
            };

            // تنفيذ عملية تسجيل الدخول
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                authProps);

            // لو فيه ReturnUrl نرجع له، وإلا نذهب للصفحة الرئيسية
            if (!string.IsNullOrWhiteSpace(model.ReturnUrl) &&
                Url.IsLocalUrl(model.ReturnUrl))
            {
                return Redirect(model.ReturnUrl);
            }

            // هنا ممكن توجهه لقائمة فواتير المشتريات مثلاً (الآن إلى Home/Index)
            return RedirectToAction("Index", "Home");
        }

        // ================================================================
        // POST: /Login/Logout
        // تسجيل خروج المستخدم الحالى
        // ================================================================
        [Authorize]                         // تعليق: لازم يكون مسجّل دخول علشان يخرج
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Login");
        }

        // ================================================================
        // دالة مساعدة: التحقق من كلمة المرور
        // ================================================================
        private bool VerifyPassword(string inputPassword, string storedHash)
        {
            if (string.IsNullOrEmpty(storedHash))
                return false;

            // حساب SHA256 للنص الذي كتبه المستخدم
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(inputPassword);     // متغير: البايتات الداخلة للتشفير
            var hashBytes = sha.ComputeHash(bytes);                // متغير: نتيجة التشفير
            var inputHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();

            // مقارنة: لو مخزّن كهاش
            if (string.Equals(storedHash, inputHash, StringComparison.OrdinalIgnoreCase))
                return true;

            // مقارنة إضافية: لو كلمة المرور مخزنة كنص عادي (لبيانات قديمة)
            if (string.Equals(storedHash, inputPassword))
                return true;

            return false;
        }
    }
}
