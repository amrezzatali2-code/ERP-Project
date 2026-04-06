using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using ERP.Data;
using ERP.Models;
using ERP.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ERP.Services
{
    /// <summary>
    /// يقرر الحسابات المخفية عن المستخدم (مصدر واحد لجميع الشاشات: القوائم، الفواتير، حجم التعامل، التقارير):
    /// - من لديه قائمة مسموح بها: اتحاد <c>UserAccountVisibilityOverride</c> (مستوى مستخدم — توافق مع قديم) + <c>RoleAccountVisibilityOverride</c> لكل أدواره → يرى المسموح فقط؛ المُخفى = الباقي (لها أولوية حتى مع SeeAll).
    /// - من لديه صلاحية UserAccountVisibility.SeeAll وبدون قائمة مسموح بها → يرى كل الحسابات.
    /// - من لم يختر أي حسابات صراحة وليس لديه SeeAll وليس مسؤول نظام: الافتراضي ذمم العملاء <c>1103</c> فقط (وما تحتها بعد التوسيع).
    /// <para><b>توسيع المسموح:</b> أبناء الشجرة، بادئة الكود، آباء الشجرة (لـ Asset/Liability/Equity فقط — لا صعود من مصروف/إيراد حتى لا يظهر 5200 عند اختيار 5202 فقط)، وأقران <b>Equity</b> تحت نفس الأب (3101 مع 3102/3103…).
    /// <b>قيود:</b> <c>LedgerEntries</c> لعميل/مورد؛ مستثمر بـ <c>AccountId == null</c> عبر القيود فقط.</para>
    /// </summary>
    public class UserAccountVisibilityService : IUserAccountVisibilityService
    {
        /// <summary>حساب التحكم بذمم العملاء — البذرة الافتراضية لصلاحيات الحسابات عند عدم اختيار أي حساب.</summary>
        private const string DefaultCustomerAccountsControlAccountCode = "1103";

        private readonly AppDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IPermissionService _permissionService;

        public UserAccountVisibilityService(
            AppDbContext context,
            IHttpContextAccessor httpContextAccessor,
            IPermissionService permissionService)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
            _permissionService = permissionService;
        }

        private int? GetCurrentUserId()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null || !user.Identity?.IsAuthenticated == true)
                return null;

            var idStr =
                user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user.FindFirst("UserId")?.Value
                ?? user.FindFirst("sub")?.Value;

            if (string.IsNullOrWhiteSpace(idStr))
                return null;

            idStr = idStr.Trim();
            if (int.TryParse(idStr, out var id))
                return id;

            return null;
        }

        private bool IsSystemAdmin()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null)
                return false;

            if (string.Equals(user.FindFirst("IsAdmin")?.Value, "true", StringComparison.OrdinalIgnoreCase))
                return true;

            var roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value?.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
            var adminRoleNames = new[] { "مسؤول النظام", "مالك النظام" };
            return roles.Any(r => adminRoleNames.Any(a => string.Equals(a, r, StringComparison.OrdinalIgnoreCase)));
        }

        public async Task<HashSet<int>> GetHiddenAccountIdsForCurrentUserAsync()
        {
            var userId = GetCurrentUserId();
            if (userId == null)
                return new HashSet<int>();

            // القائمة المسموح بها لها أولوية: إذا وُجدت نطبّقها (حتى مع صلاحية SeeAll) — إعداد «كله ما عدا مستثمر» يُحترم
            var hasExplicitAllowedList = await HasExplicitAllowedListForUserAsync(userId.Value);
            if (hasExplicitAllowedList)
                return await GetHiddenAccountIdsForUserAsync(userId.Value);

            // من لديه صلاحية «رؤية جميع الحسابات» وبدون قائمة مسموح بها → يرى كل الحسابات
            if (await _permissionService.HasPermissionAsync(userId.Value, "UserAccountVisibility.SeeAll"))
                return new HashSet<int>();

            // مسؤول النظام / مالك النظام: بدون قيد افتراضي (يرى الدليل كاملاً)
            if (IsSystemAdmin())
                return new HashSet<int>();

            // لم يُختر أي حساب صراحة: الافتراضي ذمم العملاء 1103 فقط (وما تحتها)
            return await GetHiddenAccountIdsForDefaultCustomerAccountsOnlyAsync();
        }

        public async Task<HashSet<int>> GetHiddenAccountIdsForUserAsync(int userId)
        {
            var allowedExpanded = await BuildExpandedAllowedAccountIdsForUserAsync(userId);
            return await HiddenFromExpandedAllowedAsync(allowedExpanded);
        }

        private async Task<HashSet<int>> HiddenFromExpandedAllowedAsync(HashSet<int> allowedExpanded)
        {
            if (allowedExpanded.Count == 0)
                return new HashSet<int>();

            var allAccountIds = await _context.Accounts.AsNoTracking()
                .Select(a => a.AccountId)
                .ToListAsync();
            return new HashSet<int>(allAccountIds.Where(id => !allowedExpanded.Contains(id)));
        }

        /// <summary>عند عدم اختيار حسابات: المخفي = كل الحسابات ما عدا شجرة 1103 (بعد التوسيع).</summary>
        private async Task<HashSet<int>> GetHiddenAccountIdsForDefaultCustomerAccountsOnlyAsync()
        {
            var seedId = await _context.Accounts.AsNoTracking()
                .Where(a => a.AccountCode == DefaultCustomerAccountsControlAccountCode)
                .Select(a => (int?)a.AccountId)
                .FirstOrDefaultAsync();
            if (!seedId.HasValue)
                return new HashSet<int>();

            var expanded = await BuildExpandedAllowedAccountIdsFromSeedsAsync(new HashSet<int> { seedId.Value });
            return await HiddenFromExpandedAllowedAsync(expanded);
        }

        /// <summary>هل يوجد أي حساب مسموح صراحة (مستخدم أو أحد أدواره)؟</summary>
        private async Task<bool> HasExplicitAllowedListForUserAsync(int userId)
        {
            if (await _context.UserAccountVisibilityOverrides
                    .AnyAsync(x => x.UserId == userId && x.IsAllowed))
                return true;

            var roleIds = await _context.UserRoles
                .AsNoTracking()
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.RoleId)
                .ToListAsync();
            if (roleIds.Count == 0)
                return false;

            return await _context.RoleAccountVisibilityOverrides
                .AsNoTracking()
                .AnyAsync(x => roleIds.Contains(x.RoleId) && x.IsAllowed);
        }

        /// <summary>
        /// معرّفات الحسابات المختارة كبذرة قبل التوسيع.
        /// عند وجود قائمة حسابات على أي دور للمستخدم تصبح هي المرجع الحالي،
        /// وتُستخدم سجلات المستخدم القديمة فقط كـ fallback إذا لم توجد أي إعدادات أدوار.
        /// </summary>
        private async Task<List<int>> GetRawAllowedAccountIdsForUserAsync(int userId)
        {
            var roleIds = await _context.UserRoles
                .AsNoTracking()
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.RoleId)
                .ToListAsync();

            if (roleIds.Count > 0)
            {
                var fromRoles = await _context.RoleAccountVisibilityOverrides
                    .AsNoTracking()
                    .Where(x => roleIds.Contains(x.RoleId) && x.IsAllowed)
                    .Select(x => x.AccountId)
                    .ToListAsync();

                if (fromRoles.Count > 0)
                    return fromRoles.Distinct().ToList();
            }

            return await _context.UserAccountVisibilityOverrides
                .AsNoTracking()
                .Where(x => x.UserId == userId && x.IsAllowed)
                .Select(x => x.AccountId)
                .Distinct()
                .ToListAsync();
        }

        /// <summary>
        /// الحسابات المسموح رؤيتها فعلياً: المختارة + أبناء الشجرة + بادئة الكود + آباء الشجرة (للأصول/الالتزامات/حقوق الملكية فقط) + أقران Equity.
        /// </summary>
        private async Task<HashSet<int>> BuildExpandedAllowedAccountIdsForUserAsync(int userId)
        {
            var allowedIds = await GetRawAllowedAccountIdsForUserAsync(userId);

            if (allowedIds.Count == 0)
                return new HashSet<int>();

            return await BuildExpandedAllowedAccountIdsFromSeedsAsync(new HashSet<int>(allowedIds));
        }

        private async Task<HashSet<int>> BuildExpandedAllowedAccountIdsFromSeedsAsync(HashSet<int> seedAllowed)
        {
            if (seedAllowed.Count == 0)
                return new HashSet<int>();

            var expanded = await ExpandAllowedAccountIdsWithDescendantsAsync(new HashSet<int>(seedAllowed));
            expanded = await ExpandAllowedByAccountCodePrefixesAsync(expanded);
            expanded = await ExpandAllowedWithAncestorsAsync(expanded);
            expanded = await ExpandEquityPeersUnderSameParentAsync(expanded);
            return expanded;
        }

        /// <summary>
        /// يضيف لمجموعة المسموح كل حسابات الأبناء (وأبناء الأبناء) في شجرة <see cref="Account"/>.
        /// </summary>
        private async Task<HashSet<int>> ExpandAllowedAccountIdsWithDescendantsAsync(HashSet<int> seedAllowed)
        {
            var nodes = await _context.Accounts.AsNoTracking()
                .Select(a => new { a.AccountId, a.ParentAccountId })
                .ToListAsync();

            var childrenByParent = nodes
                .Where(n => n.ParentAccountId.HasValue)
                .GroupBy(n => n.ParentAccountId!.Value)
                .ToDictionary(g => g.Key, g => g.Select(x => x.AccountId).ToList());

            var result = new HashSet<int>(seedAllowed);
            var queue = new Queue<int>(seedAllowed);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                if (!childrenByParent.TryGetValue(id, out var kids))
                    continue;
                foreach (var kid in kids)
                {
                    if (result.Add(kid))
                        queue.Enqueue(kid);
                }
            }

            return result;
        }

        /// <summary>
        /// يضيف حسابات تطابق كوداً مختاراً أو تفريعاً بصيغة <c>كود الأب-...</c> (حتى لو <c>ParentAccountId</c> غير مضبوط).
        /// </summary>
        private async Task<HashSet<int>> ExpandAllowedByAccountCodePrefixesAsync(HashSet<int> seedIds)
        {
            var accounts = await _context.Accounts.AsNoTracking()
                .Select(a => new { a.AccountId, a.AccountCode })
                .ToListAsync();

            var result = new HashSet<int>(seedIds);
            var codesFromSeed = accounts
                .Where(a => result.Contains(a.AccountId) && !string.IsNullOrWhiteSpace(a.AccountCode))
                .Select(a => a.AccountCode.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var acc in accounts)
            {
                if (string.IsNullOrWhiteSpace(acc.AccountCode))
                    continue;
                var code = acc.AccountCode.Trim();
                foreach (var root in codesFromSeed)
                {
                    if (code.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                        code.StartsWith(root + "-", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(acc.AccountId);
                        break;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// لكل حساب في المجموعة، يضيف سلسلة الآباء عبر <c>ParentAccountId</c> عندما الحساب الفرعي من نوع <see cref="AccountType.Asset"/> أو
        /// <see cref="AccountType.Liability"/> أو <see cref="AccountType.Equity"/> فقط.
        /// لا نصعد من حساب <b>مصروف أو إيراد</b> (مثل 5202 إيجار) لأن الأب التجميعي (5200) غالباً غير مختار ويجب ألا يُضاف للمسموح.
        /// </summary>
        private async Task<HashSet<int>> ExpandAllowedWithAncestorsAsync(HashSet<int> seedIds)
        {
            var nodes = await _context.Accounts.AsNoTracking()
                .Select(a => new { a.AccountId, a.ParentAccountId, a.AccountType })
                .ToListAsync();

            var result = new HashSet<int>(seedIds);
            bool added;
            do
            {
                added = false;
                foreach (var n in nodes)
                {
                    if (!n.ParentAccountId.HasValue)
                        continue;
                    if (n.AccountType != AccountType.Asset
                        && n.AccountType != AccountType.Liability
                        && n.AccountType != AccountType.Equity)
                        continue;
                    if (result.Contains(n.AccountId) && result.Add(n.ParentAccountId.Value))
                        added = true;
                }
            } while (added);

            return result;
        }

        /// <summary>
        /// إن وُجد في المسموح أي حساب من نوع حقوق ملكية (Equity)، يُضاف كل حساب Equity آخر يشترك معه في نفس الأب المباشر.
        /// يحل ارتباط بعض المستثمرين بـ 3102/3103 بدل 3101 مع بقاء المستخدم مختاراً «3101 — حساب المستثمرين» فقط.
        /// </summary>
        private async Task<HashSet<int>> ExpandEquityPeersUnderSameParentAsync(HashSet<int> seedIds)
        {
            var accounts = await _context.Accounts.AsNoTracking()
                .Select(a => new { a.AccountId, a.ParentAccountId, a.AccountType })
                .ToListAsync();

            var result = new HashSet<int>(seedIds);
            bool added;
            do
            {
                added = false;
                var parentIdsOfEquityInSet = accounts
                    .Where(a => result.Contains(a.AccountId) && a.ParentAccountId.HasValue && a.AccountType == AccountType.Equity)
                    .Select(a => a.ParentAccountId!.Value)
                    .ToHashSet();

                foreach (var a in accounts)
                {
                    if (a.AccountType != AccountType.Equity || !a.ParentAccountId.HasValue)
                        continue;
                    if (!parentIdsOfEquityInSet.Contains(a.ParentAccountId.Value))
                        continue;
                    if (result.Add(a.AccountId))
                        added = true;
                }
            } while (added);

            return result;
        }

        public async Task<bool> IsRestrictedToAllowedAccountsOnlyAsync()
        {
            var userId = GetCurrentUserId();
            if (userId == null) return false;
            // إذا وُجدت قائمة مسموح بها نعتبر المستخدم مقيداً (حتى مع SeeAll)
            if (await HasExplicitAllowedListForUserAsync(userId.Value))
                return true;
            if (await _permissionService.HasPermissionAsync(userId.Value, "UserAccountVisibility.SeeAll"))
                return false;
            if (IsSystemAdmin())
                return false;
            // افتراضي 1103: مقيد بقائمة مسموح مكافئة
            return true;
        }

        /// <inheritdoc />
        public async Task<(HashSet<int> hiddenAccountIds, bool restrictedOnly)> GetVisibilityStateForCurrentUserAsync()
        {
            var hidden = await GetHiddenAccountIdsForCurrentUserAsync();
            var restricted = await IsRestrictedToAllowedAccountsOnlyAsync();
            return (hidden, restricted);
        }

        /// <inheritdoc />
        public async Task<bool> ShouldShowAllAccountsInDropdownsAsync()
        {
            var hidden = await GetHiddenAccountIdsForCurrentUserAsync();
            return hidden.Count == 0;
        }

        /// <inheritdoc />
        /// <remarks>
        /// عميل/مورد: قيد على حساب مسموح. مستثمر بـ <c>AccountId</c> فارغ فقط: قيد على حساب مسموح (بيانات ناقصة).
        /// </remarks>
        public async Task<IQueryable<Customer>> ApplyCustomerVisibilityFilterAsync(IQueryable<Customer> query)
        {
            var (hiddenAccountIds, restrictedOnly) = await GetVisibilityStateForCurrentUserAsync();
            if (hiddenAccountIds.Count == 0)
                return query;

            if (restrictedOnly)
            {
                // فلترة بالـ allow-list (قائمة صغيرة) وليس NOT IN على آلاف المخفي — يتجنب فشل/قصّ SQL (حد المعاملات) ويوضّح المنطق.
                var userId = GetCurrentUserId();
                if (userId == null)
                    return query;

                var allowedExpanded = await BuildExpandedAllowedAccountIdsForUserAsync(userId.Value);
                var allowedList = allowedExpanded.ToList();
                if (allowedList.Count == 0)
                    return query.Where(_ => false);

                // (1) الحساب الرئيسي ضمن المسموح الموسَّع
                // (2) عميل/مورد + قيد على حساب مسموح
                // (3) مستثمر بدون AccountId + قيد على حساب مسموح (لا نفتح قيوداً لمستثمر له AccountId غير مسموح — منع تسرّب 3101 مع السماح بـ 1103 فقط)
                // PartyCategory: نستخدم Trim + مقارنة حالة-غير-حساسة حتى لا تُستبعد قيم مثل " Investor " أو اختلاف حالة
                return query.Where(c =>
                    (c.AccountId != null && allowedList.Contains(c.AccountId.Value))
                    || ((c.PartyCategory != null
                            && (c.PartyCategory.Trim() == "Customer" || c.PartyCategory.Trim() == "Supplier"))
                        && _context.LedgerEntries.Any(e => e.CustomerId == c.CustomerId && allowedList.Contains(e.AccountId)))
                    || (c.AccountId == null
                        && c.PartyCategory != null
                        && (c.PartyCategory.Trim().ToLower() == "investor" || c.PartyCategory.Trim() == "مستثمر")
                        && _context.LedgerEntries.Any(e => e.CustomerId == c.CustomerId && allowedList.Contains(e.AccountId))));
            }

            var hiddenList = hiddenAccountIds.ToList();
            return query.Where(c => c.AccountId == null || !hiddenList.Contains(c.AccountId.Value));
        }

        private const string InvestorAccountCode3101 = "3101";

        /// <inheritdoc />
        public async Task<IQueryable<LedgerEntry>> ApplyLedgerEntryListVisibilityFilterAsync(IQueryable<LedgerEntry> query, bool canViewInvestorAccount3101 = true)
        {
            if (!canViewInvestorAccount3101)
                query = query.Where(e => e.Account != null && e.Account.AccountCode != InvestorAccountCode3101);

            var (hiddenAccountIds, restrictedOnly) = await GetVisibilityStateForCurrentUserAsync();
            var hiddenList = hiddenAccountIds.ToList();
            if (hiddenList.Count == 0)
                return query;

            if (restrictedOnly)
            {
                // (أ) قيد على حساب مسموح — يشمل مستثمراً بدون AccountId على الطرف بينما القيد على 3101/غيره المسموح
                // (ب) حساب الطرف الرئيسي للعميل/المورد/المستثمر مسموح
                // (ج) عميل/مورد + أي قيد على حساب مسموح (نفس منطق ApplyCustomerVisibilityFilterAsync)
                return query.Where(e =>
                    !hiddenList.Contains(e.AccountId)
                    || (e.Customer != null && e.Customer.AccountId != null && !hiddenList.Contains(e.Customer.AccountId.Value))
                    || (e.Customer != null
                        && (e.Customer.PartyCategory == "Customer" || e.Customer.PartyCategory == "Supplier")
                        && e.CustomerId != null
                        && _context.LedgerEntries.Any(le => le.CustomerId == e.CustomerId && !hiddenList.Contains(le.AccountId))));
            }

            return query.Where(e =>
                e.Customer == null || e.Customer.AccountId == null || !hiddenList.Contains(e.Customer.AccountId.Value)
                || !hiddenList.Contains(e.AccountId)
                || (e.Customer != null && (e.Customer.PartyCategory == "Customer" || e.Customer.PartyCategory == "Supplier")
                    && e.CustomerId != null && _context.LedgerEntries.Any(le => le.CustomerId == e.CustomerId && !hiddenList.Contains(le.AccountId))));
        }
    }
}

