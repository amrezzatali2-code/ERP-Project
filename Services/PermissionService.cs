using System.Linq;
using System.Security.Claims;
using ERP.Data;
using Microsoft.EntityFrameworkCore;

namespace ERP.Services
{
    /// <summary>
    /// تطبيق خدمة التحقق من الصلاحيات.
    /// الصلاحية = من أدوار المستخدم (RolePermissions) + UserExtraPermissions - UserDeniedPermissions
    /// مسؤول النظام (IsAdmin) له كل الصلاحيات تلقائياً.
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

            var user = _httpContextAccessor.HttpContext?.User;
            if (user != null)
            {
                if (string.Equals(user.FindFirst("IsAdmin")?.Value, "true", StringComparison.OrdinalIgnoreCase))
                    return true;
                var roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value?.Trim()).Where(s => !string.IsNullOrEmpty(s));
                var adminRoleNames = new[] { "مسؤول النظام", "مالك النظام" };
                if (roles.Any(r => adminRoleNames.Any(admin => string.Equals(r, admin, StringComparison.OrdinalIgnoreCase))))
                    return true;
            }

            var code = permissionCode?.Trim() ?? "";
            if (string.IsNullOrEmpty(code)) return false;

            // البحث عن الصلاحية بأكواد مطابقة (بدون مراعاة حالة الحروف أو مسافات زائدة)
            var codeLower = code.ToLowerInvariant();
            var perm = await _context.Permissions
                .AsNoTracking()
                .Where(p => p.IsActive && p.Code != null && p.Code.ToLower() == codeLower)
                .FirstOrDefaultAsync();

            // في حال وجود أكواد قديمة في القاعدة (مثل Sales.Invoice بدل Sales.Invoices) نبحث عنها
            if (perm == null && (codeLower == "sales.invoices.view" || codeLower == "sales.invoices.create"))
            {
                var altCode = codeLower.Replace("sales.invoices.", "sales.invoice.");
                perm = await _context.Permissions
                    .AsNoTracking()
                    .Where(p => p.IsActive && p.Code != null && p.Code.ToLower() == altCode)
                    .FirstOrDefaultAsync();
            }
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
            var user = _httpContextAccessor.HttpContext?.User;
            if (user != null)
            {
                if (string.Equals(user.FindFirst("IsAdmin")?.Value, "true", StringComparison.OrdinalIgnoreCase))
                    return true;
                var roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value?.Trim()).Where(s => !string.IsNullOrEmpty(s));
                var adminRoleNames = new[] { "مسؤول النظام", "مالك النظام" };
                if (roles.Any(r => adminRoleNames.Any(admin => string.Equals(r, admin, StringComparison.OrdinalIgnoreCase))))
                    return true;
            }

            var userIdStr = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out int userId))
                return false;
            return await HasPermissionAsync(userId, permissionCode);
        }

        public async Task<HashSet<string>> GetUserPermissionCodesAsync(int userId)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (userId <= 0) return set;

            var roleIds = await _context.UserRoles
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.RoleId)
                .ToListAsync();

            var deniedPermIds = await _context.UserDeniedPermissions
                .Where(x => x.UserId == userId && !x.IsAllowed)
                .Select(x => x.PermissionId)
                .ToListAsync();
            var deniedSet = new HashSet<int>(deniedPermIds);

            var fromRoles = await _context.RolePermissions
                .Where(rp => roleIds.Contains(rp.RoleId) && rp.IsAllowed && !deniedSet.Contains(rp.PermissionId))
                .Select(rp => rp.PermissionId)
                .Distinct()
                .ToListAsync();

            var extraPermIds = await _context.UserExtraPermissions
                .Where(x => x.UserId == userId)
                .Select(x => x.PermissionId)
                .ToListAsync();

            var allPermIds = fromRoles.Union(extraPermIds).Distinct().ToList();
            if (allPermIds.Count == 0) return set;

            var codes = await _context.Permissions
                .Where(p => allPermIds.Contains(p.PermissionId) && p.IsActive && p.Code != null)
                .Select(p => p.Code!)
                .ToListAsync();

            foreach (var c in codes) set.Add(c);
            return set;
        }

        public async Task<bool> HasAnyPermissionWithPrefixAsync(string codePrefix)
        {
            var user = _httpContextAccessor.HttpContext?.User;
            var userIdStr = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out int userId) || userId <= 0)
                return false;
            if (string.IsNullOrWhiteSpace(codePrefix)) return false;
            var prefix = codePrefix.Trim();
            var codes = await GetUserPermissionCodesAsync(userId);
            return codes.Any(c => c != null && c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }
    }
}
