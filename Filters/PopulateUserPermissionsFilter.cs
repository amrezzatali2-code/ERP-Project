using Microsoft.AspNetCore.Mvc.Filters;
using ERP.Security;
using ERP.Services;
using System;
using System.Security.Claims;
using System.Linq;

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

                // نحسم إظهار الملخصات من نفس بيانات الكاش بدل استدعاء صلاحية إضافي في كل طلب.
                var user = context.HttpContext.User;
                var isAdmin =
                    string.Equals(user?.FindFirst("IsAdmin")?.Value, "true", StringComparison.OrdinalIgnoreCase) ||
                    (user?.FindAll(ClaimTypes.Role).Any(r =>
                        string.Equals(r.Value, "مسؤول النظام", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(r.Value, "مالك النظام", StringComparison.OrdinalIgnoreCase)) ?? false);

                context.HttpContext.Items["ShowListSummaries"] =
                    isAdmin || codes.Contains(GlobalPermissionGates.ShowSummaries);
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
