using ERP.Data;
using Microsoft.EntityFrameworkCore;

namespace ERP.Services
{
    /// <summary>
    /// تطبيق خدمة التحقق من الصلاحيات.
    /// الصلاحية = من أدوار المستخدم (RolePermissions) + UserExtraPermissions - UserDeniedPermissions
    /// </summary>
    public class PermissionService : IPermissionService
    {
        private readonly AppDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public PermissionService(AppDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<bool> HasPermissionAsync(int userId, string permissionCode)
        {
            if (userId <= 0 || string.IsNullOrWhiteSpace(permissionCode)) return false;

            var code = permissionCode.Trim();

            var perm = await _context.Permissions
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Code == code && p.IsActive);
            if (perm == null) return false;

            var denied = await _context.UserDeniedPermissions
                .AnyAsync(x => x.UserId == userId && x.PermissionId == perm.PermissionId && !x.IsAllowed);
            if (denied) return false;

            var fromRole = await _context.UserRoles
                .Where(ur => ur.UserId == userId)
                .Join(_context.RolePermissions.Where(rp => rp.PermissionId == perm.PermissionId && rp.IsAllowed),
                    ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => 1)
                .AnyAsync();
            if (fromRole) return true;

            var extra = await _context.UserExtraPermissions
                .AnyAsync(x => x.UserId == userId && x.PermissionId == perm.PermissionId);
            return extra;
        }

        public async Task<bool> HasPermissionAsync(string permissionCode)
        {
            var userIdStr = _httpContextAccessor.HttpContext?.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out int userId))
                return false;
            return await HasPermissionAsync(userId, permissionCode);
        }
    }
}
