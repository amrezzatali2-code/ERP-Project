using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using ERP.Services;

namespace ERP.Filters
{
    /// <summary>
    /// فلتر يتحقق من صلاحية المستخدم قبل تنفيذ الـ Action.
    /// إذا لم تكن الصلاحية ممنوحة يُعيد التوجيه للصفحة القادم منها مع رسالة تُعرض داخل الصفحة.
    /// </summary>
    public class RequirePermissionFilter : IAsyncAuthorizationFilter
    {
        private readonly string _permissionCode;
        private readonly IPermissionService _permissionService;
        private readonly ITempDataDictionaryFactory _tempDataFactory;

        public RequirePermissionFilter(string permissionCode, IPermissionService permissionService, ITempDataDictionaryFactory tempDataFactory)
        {
            _permissionCode = permissionCode?.Trim() ?? "";
            _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
            _tempDataFactory = tempDataFactory ?? throw new ArgumentNullException(nameof(tempDataFactory));
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            if (string.IsNullOrEmpty(_permissionCode))
                return;

            var hasPermission = await _permissionService.HasPermissionAsync(_permissionCode);
            if (hasPermission)
                return;

            const string message = "ليس لديك صلاحية لتنفيذ هذا الإجراء.";

            // طلبات fetch / AJAX: إرجاع JSON بدلاً من إعادة توجيه (تجنباً لتحليل HTML كـ JSON في الواجهة)
            var req = context.HttpContext.Request;
            if (string.Equals(req.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                || (req.Headers.TryGetValue("Accept", out var accept) && accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase)))
            {
                context.Result = new JsonResult(new { ok = false, message });
                return;
            }

            var tempData = _tempDataFactory.GetTempData(context.HttpContext);
            tempData["PermissionDeniedMessage"] = message;
            tempData.Save();

            var referer = context.HttpContext.Request.Headers["Referer"].FirstOrDefault();
            if (!string.IsNullOrEmpty(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var uri) && string.Equals(uri.Host, context.HttpContext.Request.Host.Host, StringComparison.OrdinalIgnoreCase))
            {
                context.Result = new RedirectResult(referer);
                return;
            }

            context.Result = new RedirectToActionResult("AccessDenied", "Home", null);
        }
    }
}
