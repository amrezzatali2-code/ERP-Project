using Microsoft.AspNetCore.Mvc.Filters;
using ERP.Security;
using ERP.Services;
using System.Security.Claims;

namespace ERP.Filters
{
    /// <summary>
    /// يملأ HttpContext.Items["UserPermissionCodes"] بمجموعة أكواد الصلاحيات للمستخدم الحالي
    /// لاستخدامها في _Layout لإخفاء عناصر القائمة.
    /// </summary>
    public class PopulateUserPermissionsFilter : IAsyncActionFilter
    {
        private readonly IPermissionService _permissionService;

        public PopulateUserPermissionsFilter(IPermissionService permissionService)
        {
            _permissionService = permissionService;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var userIdStr = context.HttpContext.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userIdStr) && int.TryParse(userIdStr, out int userId))
            {
                var codes = await _permissionService.GetUserPermissionCodesAsync(userId);
                context.HttpContext.Items["UserPermissionCodes"] = codes;
                context.HttpContext.Items["ShowListSummaries"] = await _permissionService.HasPermissionAsync(GlobalPermissionGates.ShowSummaries);
            }
            else
            {
                context.HttpContext.Items["UserPermissionCodes"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                context.HttpContext.Items["ShowListSummaries"] = false;
            }

            await next();
        }
    }
}
