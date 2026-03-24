using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Controllers
{
    /// <summary>
    /// موديول برنامج المشتريات: لوحة التحكم والشاشات المستقبلية.
    /// الأصناف والعملاء والمستخدمون والصلاحيات من جداول الـ ERP.
    /// </summary>
    public class PurchasingProgramController : Controller
    {
        public IActionResult Index()
        {
            ViewData["Title"] = "برنامج المشتريات";
            return View();
        }
    }
}
