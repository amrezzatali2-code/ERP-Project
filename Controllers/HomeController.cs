using System.Diagnostics;
using ERP.Filters;
using ERP.Models;
using ERP.Security;
using ERP.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;   // ✅ علشان نستخدم AllowAnonymous
using Microsoft.Extensions.Logging;

namespace ERP.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILoginRedirectService _loginRedirectService;

        public HomeController(ILogger<HomeController> logger, ILoggerFactory loggerFactory, ILoginRedirectService loginRedirectService)
        {
            _logger = logger;
            _loggerFactory = loggerFactory;
            _loginRedirectService = loginRedirectService;
        }

        [Authorize]
        public async Task<IActionResult> Index()
        {
            var userIdClaim = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
                return RedirectToAction(nameof(AccessDenied));

            var target = await _loginRedirectService.GetTargetAsync(userId);
            return RedirectToAction(target.Action, target.Controller);
        }

        /// <summary>صفحة سياسة الخصوصية — بدون صلاحية منفصلة (غير مستخدمة في القوائم).</summary>
        [AllowAnonymous]
        public IActionResult Privacy()
        {
            return View();
        }

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            var feature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
            if (feature?.Error != null)
            {
                var technical = _loggerFactory.CreateLogger("ERP.Technical.Unhandled");
                var user = User?.Identity?.IsAuthenticated == true
                    ? (User.Identity?.Name ?? "?")
                    : "anonymous";
                technical.LogError(feature.Error,
                    "Unhandled exception (production path). Path={Path} User={User}",
                    feature.Path, user);
            }

            return View(new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }

        // ✅ صفحة "لا تملك صلاحية" – لازم نسمح للجميع يدخلها
        [AllowAnonymous]
        public IActionResult AccessDenied()
        {
            // هتقرأ View: Views/Home/AccessDenied.cshtml
            return View();
        }
    }
}
