using ERP.Filters;
using ERP.Infrastructure;
using ERP.Models;
using ERP.Services;
using ERP.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Controllers
{
    [RequirePermission("Settings.PrintHeader")]
    public class MobileAppSettingsController : Controller
    {
        private readonly MobileAppProgramCodeService _mobileSettingsService;
        private readonly IUserActivityLogger _activityLogger;

        public MobileAppSettingsController(
            MobileAppProgramCodeService mobileSettingsService,
            IUserActivityLogger activityLogger)
        {
            _mobileSettingsService = mobileSettingsService;
            _activityLogger = activityLogger;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var settings = await _mobileSettingsService.GetSettingsAsync();
            var vm = new MobileAppSettingsViewModel
            {
                CompanyName = settings.CompanyName,
                ProgramCode = settings.ProgramCode
            };
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(MobileAppSettingsViewModel model)
        {
            model.CompanyName = MobileAppProgramCodeService.NormalizeCompanyName(model.CompanyName);
            model.ProgramCode = MobileAppProgramCodeService.NormalizeProgramCode(model.ProgramCode);

            if (string.IsNullOrWhiteSpace(model.CompanyName))
                ModelState.AddModelError(nameof(model.CompanyName), "اسم الشركة مطلوب.");
            if (string.IsNullOrWhiteSpace(model.ProgramCode))
                ModelState.AddModelError(nameof(model.ProgramCode), "كود الشركة مطلوب.");

            if (!ModelState.IsValid)
                return View(model);

            await _mobileSettingsService.SaveSettingsAsync(model.CompanyName, model.ProgramCode);

            await _activityLogger.LogAsync(
                UserActionType.Edit,
                "MobileAppSettings",
                1,
                "تعديل إعدادات تطبيق الصيدلي (اسم الشركة + كود الشركة)");

            TempData["Success"] = "تم حفظ إعدادات تطبيق الصيدلي بنجاح.";
            return RedirectToAction(nameof(Index));
        }
    }
}
