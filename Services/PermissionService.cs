using System;
using System.Linq;
using System.Security.Claims;
using ERP.Data;
using ERP.Security;
using Microsoft.Extensions.Caching.Memory;
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
        private readonly IMemoryCache _memoryCache;

        private const string PermissionCacheVersionKey = "perm-cache-version";
        private static readonly TimeSpan PermissionCacheAbsoluteExpiration = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan PermissionCacheSlidingExpiration = TimeSpan.FromMinutes(5);

        public PermissionService(AppDbContext context, IHttpContextAccessor httpContextAccessor, IMemoryCache memoryCache)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
            _memoryCache = memoryCache;
        }

        /// <summary>
        /// صلاحيات خطيرة: لا نمنحها تلقائياً لمالك/مسؤول النظام، بل نتحقق من الربط الفعلي في الأدوار.
        /// </summary>
        private static readonly HashSet<string> AlwaysCheckPermissions = new(StringComparer.OrdinalIgnoreCase)
        {
            "SalesInvoices.DeleteAll",
            "SalesInvoices.BulkDelete",
            "PurchaseInvoices.DeleteAll",
            "PurchaseInvoices.BulkDelete"
        };

        public async Task<bool> HasPermissionAsync(int userId, string permissionCode)
        {
            if (userId <= 0 || string.IsNullOrWhiteSpace(permissionCode)) return false;

            var code = permissionCode?.Trim() ?? "";
            var bypassAdmin = !AlwaysCheckPermissions.Contains(code);

            var user = _httpContextAccessor.HttpContext?.User;
            if (bypassAdmin && user != null)
            {
                if (string.Equals(user.FindFirst("IsAdmin")?.Value, "true", StringComparison.OrdinalIgnoreCase))
                    return true;
                var roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value?.Trim()).Where(s => !string.IsNullOrEmpty(s));
                var adminRoleNames = new[] { "مسؤول النظام", "مالك النظام" };
                if (roles.Any(r => adminRoleNames.Any(admin => string.Equals(r, admin, StringComparison.OrdinalIgnoreCase))))
                    return true;
            }

            if (string.IsNullOrEmpty(code)) return false;

            // مسار سريع جداً: فحص الصلاحية من كاش الصلاحيات الفعّالة للمستخدم
            // (يشمل Role + Extra - Denied) مع دعم الأكواد القديمة المتوافقة.
            var cachedCodes = await GetUserPermissionCodesAsync(userId);
            if (HasPermissionFromCachedCodes(cachedCodes, code))
                return true;

            // صلاحيات Global.* لا تمتلك خرائط توافق قديمة؛ إذا لم تُوجد في الكاش نعتبرها غير متاحة فوراً
            // لتجنّب استعلامات DB إضافية في كل طلب (خصوصاً Global.GeneralList و Global.ShowSummaries).
            if (code.StartsWith("Global.", StringComparison.OrdinalIgnoreCase))
                return false;

            // البحث عن الصلاحية بأكواد مطابقة (بدون مراعاة حالة الحروف أو مسافات زائدة)
            var codeLower = code.ToLowerInvariant();
            var perm = await _context.Permissions
                .AsNoTracking()
                .Where(p => p.IsActive && p.Code != null && p.Code.ToLower() == codeLower)
                .FirstOrDefaultAsync();

            // في حال وجود أكواد قديمة في القاعدة نبحث عنها
            if (perm == null && codeLower == "sales.invoices.view.index")
            {
                perm = await _context.Permissions
                    .AsNoTracking()
                    .Where(p => p.IsActive && p.Code != null && p.Code.ToLower() == "sales.invoices.view")
                    .FirstOrDefaultAsync();
            }
            if (perm == null && codeLower.StartsWith("sales.invoices."))
            {
                var altCode = codeLower.Replace("sales.invoices.", "sales.invoice.");
                perm = await _context.Permissions
                    .AsNoTracking()
                    .Where(p => p.IsActive && p.Code != null && p.Code.ToLower() == altCode)
                    .FirstOrDefaultAsync();
            }
            if (perm == null && (codeLower == "customers.view.index" || codeLower == "customers.view.show"))
            {
                perm = await _context.Permissions
                    .AsNoTracking()
                    .Where(p => p.IsActive && p.Code != null && p.Code.ToLower() == "customers.customers.view")
                    .FirstOrDefaultAsync();
            }
            if (perm == null && codeLower.StartsWith("customers.") && !codeLower.StartsWith("customers.customers.") && !codeLower.StartsWith("customers.view.") && !codeLower.StartsWith("customers.ledger.") && !codeLower.StartsWith("customers.customervolume."))
            {
                var altCode = "customers.customers." + codeLower.Substring("customers.".Length);
                perm = await _context.Permissions
                    .AsNoTracking()
                    .Where(p => p.IsActive && p.Code != null && p.Code.ToLower() == altCode)
                    .FirstOrDefaultAsync();
            }
            // المشتريات: كود جديد بصيغة underscore (Purchasing.Invoices_View) — إن لم يُوجد نبحث عن الأكواد القديمة
            if (perm == null && codeLower.StartsWith("purchasing."))
            {
                var oldCodes = new List<string>();
                if (codeLower == "purchasing.invoices_view")
                    oldCodes.AddRange(new[] { "purchasing.invoices.view.index", "purchasing.invoices.view" });
                else if (codeLower == "purchasing.requests_view")
                    oldCodes.AddRange(new[] { "purchasing.requests.view.index", "purchasing.requests.view" });
                else if (codeLower == "purchasing.returns_view")
                    oldCodes.AddRange(new[] { "purchasing.returns.view.index", "purchasing.returns.view" });
                else
                    oldCodes.Add(codeLower
                        .Replace("purchasing.invoices_", "purchasing.invoices.")
                        .Replace("purchasing.requests_", "purchasing.requests.")
                        .Replace("purchasing.returns_", "purchasing.returns.")
                        .Replace("purchasing.invoicelines_", "purchasing.invoicelines.")
                        .Replace("purchasing.requestlines_", "purchasing.requestlines.")
                        .Replace("purchasing.returnlines_", "purchasing.returnlines."));
                foreach (var old in oldCodes)
                {
                    perm = await _context.Permissions
                        .AsNoTracking()
                        .Where(p => p.IsActive && p.Code != null && p.Code.ToLower() == old)
                        .FirstOrDefaultAsync();
                    if (perm != null) break;
                }
                if (perm == null)
                {
                    var altCode = codeLower
                        .Replace("purchasing.invoices.", "purchasing.invoice.")
                        .Replace("purchasing.requests.", "purchasing.request.")
                        .Replace("purchasing.returns.", "purchasing.return.")
                        .Replace("purchasing.invoicelines.", "purchasing.invoiceline.")
                        .Replace("purchasing.requestlines.", "purchasing.requestline.")
                        .Replace("purchasing.returnlines.", "purchasing.returnline.");
                    if (altCode != codeLower)
                    {
                        perm = await _context.Permissions
                            .AsNoTracking()
                            .Where(p => p.IsActive && p.Code != null && p.Code.ToLower() == altCode)
                            .FirstOrDefaultAsync();
                    }
                }
            }
            if (perm == null) return false;

            var globalGate = GlobalPermissionGates.TryGetRequiredGlobalCode(code);
            if (!string.IsNullOrEmpty(globalGate) && !string.Equals(globalGate, code, StringComparison.OrdinalIgnoreCase))
            {
                if (!await HasPermissionAsync(userId, globalGate))
                    return false;
            }

            // التحقق: ممنوع؟ من الدور؟ إضافي؟
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
            if (extra) return true;

            // المشتريات: لو الصلاحية بالكود الجديد (underscore) والدور مربوط بالكود القديم (نقطة) نتحقق من القديم
            if (codeLower.StartsWith("purchasing.") && codeLower.Contains("_"))
            {
                var legacyCode = codeLower
                    .Replace("purchasing.invoices_", "purchasing.invoices.")
                    .Replace("purchasing.requests_", "purchasing.requests.")
                    .Replace("purchasing.returns_", "purchasing.returns.")
                    .Replace("purchasing.invoicelines_", "purchasing.invoicelines.")
                    .Replace("purchasing.requestlines_", "purchasing.requestlines.")
                    .Replace("purchasing.returnlines_", "purchasing.returnlines.");
                if (legacyCode != codeLower)
                {
                    var permLegacy = await _context.Permissions
                        .AsNoTracking()
                        .Where(p => p.IsActive && p.Code != null && p.Code.ToLower() == legacyCode)
                        .FirstOrDefaultAsync();
                    if (permLegacy != null && permLegacy.PermissionId != perm.PermissionId)
                    {
                        var deniedLegacy = await _context.UserDeniedPermissions
                            .AnyAsync(x => x.UserId == userId && x.PermissionId == permLegacy.PermissionId && !x.IsAllowed);
                        if (!deniedLegacy)
                        {
                            var fromRoleLegacy = await _context.UserRoles
                                .Where(ur => ur.UserId == userId)
                                .Join(_context.RolePermissions.Where(rp => rp.PermissionId == permLegacy.PermissionId && rp.IsAllowed),
                                    ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => 1)
                                .AnyAsync();
                            if (fromRoleLegacy) return true;
                            var extraLegacy = await _context.UserExtraPermissions
                                .AnyAsync(x => x.UserId == userId && x.PermissionId == permLegacy.PermissionId);
                            if (extraLegacy) return true;
                        }
                    }
                }
            }

            // لو الصلاحية بالكود الجديد والدور مربوط بكود قديم نتحقق من القديم أيضاً
            if (codeLower == "sales.invoices.view.index")
            {
                var permLegacy = await _context.Permissions
                    .AsNoTracking()
                    .Where(p => p.IsActive && p.Code != null && p.Code.ToLower() == "sales.invoices.view")
                    .FirstOrDefaultAsync();
                if (permLegacy != null && permLegacy.PermissionId != perm.PermissionId)
                {
                    var deniedLegacy = await _context.UserDeniedPermissions
                        .AnyAsync(x => x.UserId == userId && x.PermissionId == permLegacy.PermissionId && !x.IsAllowed);
                    if (!deniedLegacy)
                    {
                        var fromRoleLegacy = await _context.UserRoles
                            .Where(ur => ur.UserId == userId)
                            .Join(_context.RolePermissions.Where(rp => rp.PermissionId == permLegacy.PermissionId && rp.IsAllowed),
                                ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => 1)
                            .AnyAsync();
                        if (fromRoleLegacy) return true;
                        var extraLegacy = await _context.UserExtraPermissions
                            .AnyAsync(x => x.UserId == userId && x.PermissionId == permLegacy.PermissionId);
                        if (extraLegacy) return true;
                    }
                }
            }
            if (codeLower == "customers.view.index" || codeLower == "customers.view.show")
            {
                var permLegacy = await _context.Permissions
                    .AsNoTracking()
                    .Where(p => p.IsActive && p.Code != null && p.Code.ToLower() == "customers.customers.view")
                    .FirstOrDefaultAsync();
                if (permLegacy != null && permLegacy.PermissionId != perm.PermissionId)
                {
                    var deniedLegacy = await _context.UserDeniedPermissions
                        .AnyAsync(x => x.UserId == userId && x.PermissionId == permLegacy.PermissionId && !x.IsAllowed);
                    if (!deniedLegacy)
                    {
                        var fromRoleLegacy = await _context.UserRoles
                            .Where(ur => ur.UserId == userId)
                            .Join(_context.RolePermissions.Where(rp => rp.PermissionId == permLegacy.PermissionId && rp.IsAllowed),
                                ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => 1)
                            .AnyAsync();
                        if (fromRoleLegacy) return true;
                        var extraLegacy = await _context.UserExtraPermissions
                            .AnyAsync(x => x.UserId == userId && x.PermissionId == permLegacy.PermissionId);
                        if (extraLegacy) return true;
                    }
                }
            }
            if (codeLower.StartsWith("customers.") && !codeLower.StartsWith("customers.customers.") && !codeLower.StartsWith("customers.view.") && !codeLower.StartsWith("customers.ledger.") && !codeLower.StartsWith("customers.customervolume."))
            {
                var altCode = "customers.customers." + codeLower.Substring("customers.".Length);
                var permLegacy = await _context.Permissions
                    .AsNoTracking()
                    .Where(p => p.IsActive && p.Code != null && p.Code.ToLower() == altCode)
                    .FirstOrDefaultAsync();
                if (permLegacy != null && permLegacy.PermissionId != perm.PermissionId)
                {
                    var deniedLegacy = await _context.UserDeniedPermissions
                        .AnyAsync(x => x.UserId == userId && x.PermissionId == permLegacy.PermissionId && !x.IsAllowed);
                    if (!deniedLegacy)
                    {
                        var fromRoleLegacy = await _context.UserRoles
                            .Where(ur => ur.UserId == userId)
                            .Join(_context.RolePermissions.Where(rp => rp.PermissionId == permLegacy.PermissionId && rp.IsAllowed),
                                ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => 1)
                            .AnyAsync();
                        if (fromRoleLegacy) return true;
                        var extraLegacy = await _context.UserExtraPermissions
                            .AnyAsync(x => x.UserId == userId && x.PermissionId == permLegacy.PermissionId);
                        if (extraLegacy) return true;
                    }
                }
            }
            if (codeLower.StartsWith("sales.invoices."))
            {
                var altCode = codeLower.Replace("sales.invoices.", "sales.invoice.");
                var permLegacy = await _context.Permissions
                    .AsNoTracking()
                    .Where(p => p.IsActive && p.Code != null && p.Code.ToLower() == altCode)
                    .FirstOrDefaultAsync();
                if (permLegacy != null && permLegacy.PermissionId != perm.PermissionId)
                {
                    var deniedLegacy = await _context.UserDeniedPermissions
                        .AnyAsync(x => x.UserId == userId && x.PermissionId == permLegacy.PermissionId && !x.IsAllowed);
                    if (!deniedLegacy)
                    {
                        var fromRoleLegacy = await _context.UserRoles
                            .Where(ur => ur.UserId == userId)
                            .Join(_context.RolePermissions.Where(rp => rp.PermissionId == permLegacy.PermissionId && rp.IsAllowed),
                                ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => 1)
                            .AnyAsync();
                        if (fromRoleLegacy) return true;
                        var extraLegacy = await _context.UserExtraPermissions
                            .AnyAsync(x => x.UserId == userId && x.PermissionId == permLegacy.PermissionId);
                        if (extraLegacy) return true;
                    }
                }
            }
            // قوائم المشتريات: لو الدور مربوط بـ View (قديم) بدون .Index نعتبره صالحاً
            if (codeLower == "purchasing.invoices.view.index" || codeLower == "purchasing.requests.view.index" || codeLower == "purchasing.returns.view.index")
            {
                var legacyCode = codeLower == "purchasing.invoices.view.index" ? "purchasing.invoices.view"
                    : codeLower == "purchasing.requests.view.index" ? "purchasing.requests.view"
                    : "purchasing.returns.view";
                var permLegacy = await _context.Permissions
                    .AsNoTracking()
                    .Where(p => p.IsActive && p.Code != null && p.Code.ToLower() == legacyCode)
                    .FirstOrDefaultAsync();
                if (permLegacy != null && permLegacy.PermissionId != perm.PermissionId)
                {
                    var deniedLegacy = await _context.UserDeniedPermissions
                        .AnyAsync(x => x.UserId == userId && x.PermissionId == permLegacy.PermissionId && !x.IsAllowed);
                    if (!deniedLegacy)
                    {
                        var fromRoleLegacy = await _context.UserRoles
                            .Where(ur => ur.UserId == userId)
                            .Join(_context.RolePermissions.Where(rp => rp.PermissionId == permLegacy.PermissionId && rp.IsAllowed),
                                ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => 1)
                            .AnyAsync();
                        if (fromRoleLegacy) return true;
                        var extraLegacy = await _context.UserExtraPermissions
                            .AnyAsync(x => x.UserId == userId && x.PermissionId == permLegacy.PermissionId);
                        if (extraLegacy) return true;
                    }
                }
            }
            // المشتريات: لو الدور مربوط بصلاحية قديمة (مفرد) نعتبرها صالحة
            if (codeLower.StartsWith("purchasing."))
            {
                var altCode = codeLower
                    .Replace("purchasing.invoices.", "purchasing.invoice.")
                    .Replace("purchasing.requests.", "purchasing.request.")
                    .Replace("purchasing.returns.", "purchasing.return.")
                    .Replace("purchasing.invoicelines.", "purchasing.invoiceline.")
                    .Replace("purchasing.requestlines.", "purchasing.requestline.")
                    .Replace("purchasing.returnlines.", "purchasing.returnline.");
                if (altCode != codeLower)
                {
                    var permLegacy = await _context.Permissions
                        .AsNoTracking()
                        .Where(p => p.IsActive && p.Code != null && p.Code.ToLower() == altCode)
                        .FirstOrDefaultAsync();
                    if (permLegacy != null && permLegacy.PermissionId != perm.PermissionId)
                    {
                        var deniedLegacy = await _context.UserDeniedPermissions
                            .AnyAsync(x => x.UserId == userId && x.PermissionId == permLegacy.PermissionId && !x.IsAllowed);
                        if (!deniedLegacy)
                        {
                            var fromRoleLegacy = await _context.UserRoles
                                .Where(ur => ur.UserId == userId)
                                .Join(_context.RolePermissions.Where(rp => rp.PermissionId == permLegacy.PermissionId && rp.IsAllowed),
                                    ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => 1)
                                .AnyAsync();
                            if (fromRoleLegacy) return true;
                            var extraLegacy = await _context.UserExtraPermissions
                                .AnyAsync(x => x.UserId == userId && x.PermissionId == permLegacy.PermissionId);
                            if (extraLegacy) return true;
                        }
                    }
                }
            }

            return false;
        }

        public async Task<bool> HasPermissionAsync(string permissionCode)
        {
            var code = permissionCode?.Trim() ?? "";
            var bypassAdmin = !AlwaysCheckPermissions.Contains(code);

            var user = _httpContextAccessor.HttpContext?.User;
            if (bypassAdmin && user != null)
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

            // مسار سريع: قراءة الصلاحيات من الكاش أولاً لتقليل استعلامات كل طلب.
            var cachedCodes = await GetUserPermissionCodesAsync(userId);
            if (cachedCodes.Contains(code))
                return true;

            return await HasPermissionAsync(userId, permissionCode);
        }

        public async Task<HashSet<string>> GetUserPermissionCodesAsync(int userId)
        {
            var empty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (userId <= 0) return empty;

            var cacheKey = BuildUserPermissionCacheKey(userId);
            if (_memoryCache.TryGetValue(cacheKey, out HashSet<string>? cached) && cached != null)
            {
                // نرجع نسخة حتى لا يعدّل أي مستهلك الكاش الأصلي بالخطأ.
                return new HashSet<string>(cached, StringComparer.OrdinalIgnoreCase);
            }

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

            _memoryCache.Set(
                cacheKey,
                new HashSet<string>(set, StringComparer.OrdinalIgnoreCase),
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = PermissionCacheAbsoluteExpiration,
                    SlidingExpiration = PermissionCacheSlidingExpiration
                });

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

        public void InvalidateUserPermissionCache(int userId)
        {
            if (userId <= 0) return;
            _memoryCache.Remove(BuildUserPermissionCacheKey(userId));
        }

        public void InvalidateAllPermissionCaches()
        {
            var version = GetPermissionCacheVersion();
            _memoryCache.Set(PermissionCacheVersionKey, version + 1);
        }

        private string BuildUserPermissionCacheKey(int userId)
        {
            var version = GetPermissionCacheVersion();
            return $"user-permissions:{version}:{userId}";
        }

        private int GetPermissionCacheVersion()
        {
            if (_memoryCache.TryGetValue(PermissionCacheVersionKey, out int version))
                return version;

            _memoryCache.Set(PermissionCacheVersionKey, 1);
            return 1;
        }

        private static bool HasPermissionFromCachedCodes(HashSet<string> cachedCodes, string permissionCode)
        {
            if (cachedCodes.Count == 0 || string.IsNullOrWhiteSpace(permissionCode))
                return false;

            var code = permissionCode.Trim();
            if (cachedCodes.Contains(code))
                return true;

            var codeLower = code.ToLowerInvariant();

            // Sales legacy: sales.invoices.* -> sales.invoice.*
            if (codeLower == "sales.invoices.view.index" && cachedCodes.Contains("sales.invoices.view"))
                return true;
            if (codeLower.StartsWith("sales.invoices."))
            {
                var altCode = codeLower.Replace("sales.invoices.", "sales.invoice.");
                if (cachedCodes.Contains(altCode))
                    return true;
            }

            // Customers legacy
            if ((codeLower == "customers.view.index" || codeLower == "customers.view.show")
                && cachedCodes.Contains("customers.customers.view"))
                return true;
            if (codeLower.StartsWith("customers.")
                && !codeLower.StartsWith("customers.customers.")
                && !codeLower.StartsWith("customers.view.")
                && !codeLower.StartsWith("customers.ledger.")
                && !codeLower.StartsWith("customers.customervolume."))
            {
                var altCode = "customers.customers." + codeLower.Substring("customers.".Length);
                if (cachedCodes.Contains(altCode))
                    return true;
            }

            // Purchasing legacy: underscore <-> dot + plural <-> singular + view.index <-> view
            if (codeLower.StartsWith("purchasing."))
            {
                var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (codeLower == "purchasing.invoices_view")
                    candidates.UnionWith(new[] { "purchasing.invoices.view.index", "purchasing.invoices.view" });
                else if (codeLower == "purchasing.requests_view")
                    candidates.UnionWith(new[] { "purchasing.requests.view.index", "purchasing.requests.view" });
                else if (codeLower == "purchasing.returns_view")
                    candidates.UnionWith(new[] { "purchasing.returns.view.index", "purchasing.returns.view" });
                else
                    candidates.Add(codeLower
                        .Replace("purchasing.invoices_", "purchasing.invoices.")
                        .Replace("purchasing.requests_", "purchasing.requests.")
                        .Replace("purchasing.returns_", "purchasing.returns.")
                        .Replace("purchasing.invoicelines_", "purchasing.invoicelines.")
                        .Replace("purchasing.requestlines_", "purchasing.requestlines.")
                        .Replace("purchasing.returnlines_", "purchasing.returnlines."));

                candidates.Add(codeLower
                    .Replace("purchasing.invoices.", "purchasing.invoice.")
                    .Replace("purchasing.requests.", "purchasing.request.")
                    .Replace("purchasing.returns.", "purchasing.return.")
                    .Replace("purchasing.invoicelines.", "purchasing.invoiceline.")
                    .Replace("purchasing.requestlines.", "purchasing.requestline.")
                    .Replace("purchasing.returnlines.", "purchasing.returnline."));

                if (codeLower == "purchasing.invoices.view.index")
                    candidates.Add("purchasing.invoices.view");
                else if (codeLower == "purchasing.requests.view.index")
                    candidates.Add("purchasing.requests.view");
                else if (codeLower == "purchasing.returns.view.index")
                    candidates.Add("purchasing.returns.view");

                if (candidates.Any(cachedCodes.Contains))
                    return true;
            }

            return false;
        }
    }
}
