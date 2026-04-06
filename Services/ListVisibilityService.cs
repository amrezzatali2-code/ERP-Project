using ERP.Data;
using ERP.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ERP.Services
{
    public class ListVisibilityService : IListVisibilityService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly AppDbContext _context;
        private readonly IPermissionService _permissionService;

        public ListVisibilityService(
            IHttpContextAccessor httpContextAccessor,
            AppDbContext context,
            IPermissionService permissionService)
        {
            _httpContextAccessor = httpContextAccessor;
            _context = context;
            _permissionService = permissionService;
        }

        public async Task<bool> CanViewAllOperationalListsAsync()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            var user = httpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
                return false;

            var permissionCode = PermissionCodes.Code("Global", "GeneralList");
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out var userId))
                return await _permissionService.HasPermissionAsync(userId, permissionCode);

            return await _permissionService.HasPermissionAsync(permissionCode);
        }

        public async Task<List<string>> GetCurrentUserCreatorNamesAsync()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            var set = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            void Add(string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    set.Add(value.Trim());
            }

            Add(user?.Identity?.Name);
            Add(user?.FindFirst("DisplayName")?.Value);
            Add(user?.FindFirst(ClaimTypes.Name)?.Value);

            var userIdClaim = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out var userId))
            {
                var row = await _context.Users.AsNoTracking()
                    .Where(u => u.UserId == userId)
                    .Select(u => new { u.UserName, u.DisplayName, u.Email })
                    .FirstOrDefaultAsync();

                if (row != null)
                {
                    Add(row.UserName);
                    Add(row.DisplayName);
                    Add(row.Email);
                }
            }

            return set.ToList();
        }
    }
}
