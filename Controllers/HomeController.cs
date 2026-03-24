using System.Diagnostics;
using ERP.Filters;
using ERP.Models;
using ERP.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;   // ✅ علشان نستخدم AllowAnonymous

namespace ERP.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        [RequirePermission("Home.Index")]
        public IActionResult Index()
        {
            return RedirectToAction("Sales", "Dashboard");
        }

        /// <summary>صفحة سياسة الخصوصية — بدون صلاحية منفصلة (غير مستخدمة في القوائم).</summary>
        [AllowAnonymous]
        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
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
