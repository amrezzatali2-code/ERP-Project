using ERP.Data;
using ERP.Filters;
using ERP.Infrastructure;
using ERP.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Controllers
{
    [RequirePermission("Settings.PrintHeader")]
    public class PrintHeaderSettingsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IUserActivityLogger _activityLogger;

        public PrintHeaderSettingsController(AppDbContext context, IWebHostEnvironment env, IUserActivityLogger activityLogger)
        {
            _context = context;
            _env = env;
            _activityLogger = activityLogger;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var setting = await GetOrCreateAsync();
            if (!string.IsNullOrWhiteSpace(TempData["Error"] as string))
            {
                ModelState.AddModelError(string.Empty, TempData["Error"]!.ToString()!);
            }
            return View(setting);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(string? companyName, IFormFile? logoFile, bool removeLogo = false)
        {
            var setting = await GetOrCreateAsync();
            if (setting.Id == 0 && IsMissingTableMode())
            {
                TempData["Error"] = "جدول إعداد هيدر الطباعة غير موجود حالياً. نفّذ الهجرة أولاً.";
                return RedirectToAction(nameof(Index));
            }

            var normalizedName = (companyName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                ModelState.AddModelError(nameof(companyName), "اسم هيدر الطباعة مطلوب.");
            }
            else if (normalizedName.Length > 200)
            {
                ModelState.AddModelError(nameof(companyName), "اسم هيدر الطباعة يجب ألا يزيد عن 200 حرف.");
            }

            if (logoFile != null && logoFile.Length > 0)
            {
                var ext = Path.GetExtension(logoFile.FileName)?.ToLowerInvariant();
                var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp", ".gif" };
                if (string.IsNullOrWhiteSpace(ext) || !allowed.Contains(ext))
                {
                    ModelState.AddModelError(nameof(logoFile), "صيغة اللوجو غير مدعومة. استخدم PNG/JPG/JPEG/WEBP/GIF.");
                }
            }

            if (!ModelState.IsValid)
            {
                setting.CompanyName = string.IsNullOrWhiteSpace(normalizedName) ? setting.CompanyName : normalizedName;
                return View(setting);
            }

            var previousLogo = setting.LogoPath;
            setting.CompanyName = normalizedName;

            if (removeLogo)
            {
                setting.LogoPath = null;
            }

            if (logoFile != null && logoFile.Length > 0)
            {
                var logosDir = Path.Combine(_env.WebRootPath, "uploads", "print-header");
                Directory.CreateDirectory(logosDir);

                var ext = Path.GetExtension(logoFile.FileName).ToLowerInvariant();
                var fileName = $"print-header-{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext}";
                var fullPath = Path.Combine(logosDir, fileName);

                await using (var fs = new FileStream(fullPath, FileMode.Create))
                {
                    await logoFile.CopyToAsync(fs);
                }

                setting.LogoPath = "/uploads/print-header/" + fileName;
            }

            setting.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            if (!string.Equals(previousLogo, setting.LogoPath, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteLocalLogo(previousLogo);
            }

            await _activityLogger.LogAsync(
                UserActionType.Edit,
                "PrintHeaderSetting",
                setting.Id,
                "تعديل هيدر الطباعة (اسم/لوجو)");

            TempData["Success"] = "تم حفظ إعدادات هيدر الطباعة بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        private async Task<PrintHeaderSetting> GetOrCreateAsync()
        {
            PrintHeaderSetting? setting;
            try
            {
                setting = await _context.PrintHeaderSettings
                    .OrderByDescending(x => x.Id)
                    .FirstOrDefaultAsync();
            }
            catch (Exception ex) when (IsMissingPrintHeaderSettingsTable(ex))
            {
                TempData["Error"] = "جدول إعداد هيدر الطباعة غير موجود حالياً. نفّذ الهجرة أولاً.";
                HttpContext.Items["PrintHeaderSettingsTableMissing"] = true;
                return new PrintHeaderSetting
                {
                    Id = 0,
                    CompanyName = "شركة الهدى",
                    UpdatedAt = DateTime.UtcNow
                };
            }

            if (setting != null)
            {
                return setting;
            }

            setting = new PrintHeaderSetting
            {
                CompanyName = "شركة الهدى",
                UpdatedAt = DateTime.UtcNow
            };
            _context.PrintHeaderSettings.Add(setting);
            await _context.SaveChangesAsync();
            return setting;
        }

        private bool IsMissingTableMode() =>
            HttpContext.Items.TryGetValue("PrintHeaderSettingsTableMissing", out var v) && v is bool b && b;

        private static bool IsMissingPrintHeaderSettingsTable(Exception ex)
        {
            Exception? current = ex;
            while (current != null)
            {
                var msg = current.Message ?? string.Empty;
                if (msg.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase) &&
                    msg.Contains("PrintHeaderSettings", StringComparison.OrdinalIgnoreCase))
                    return true;
                current = current.InnerException;
            }
            return false;
        }

        private void TryDeleteLocalLogo(string? logoPath)
        {
            if (string.IsNullOrWhiteSpace(logoPath)) return;
            if (!logoPath.StartsWith("/uploads/print-header/", StringComparison.OrdinalIgnoreCase)) return;
            var relative = logoPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(_env.WebRootPath, relative);
            if (System.IO.File.Exists(fullPath))
            {
                System.IO.File.Delete(fullPath);
            }
        }
    }
}
