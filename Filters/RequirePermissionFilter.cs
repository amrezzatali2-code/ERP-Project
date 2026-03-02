using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ERP.Services;

namespace ERP.Filters
{
    /// <summary>
    /// فلتر يتحقق من صلاحية المستخدم قبل تنفيذ الـ Action.
    /// كود الصلاحية = ControllerName.ActionName (مثل SalesInvoices.Index).
    /// إذا لم تكن الصلاحية ممنوحة يُوجّه المستخدم لصفحة "لا تملك صلاحية".
    /// </summary>
    public class RequirePermissionFilter : IAsyncAuthorizationFilter
    {
        private readonly string _permissionCode;
        private readonly IPermissionService _permissionService;

        public RequirePermissionFilter(string permissionCode, IPermissionService permissionService)
        {
            _permissionCode = permissionCode?.Trim() ?? "";
            _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            if (string.IsNullOrEmpty(_permissionCode))
                return;

            var hasPermission = await _permissionService.HasPermissionAsync(_permissionCode);
            if (hasPermission)
                return;

            context.Result = new RedirectToActionResult("AccessDenied", "Home", null);
        }
    }
}
