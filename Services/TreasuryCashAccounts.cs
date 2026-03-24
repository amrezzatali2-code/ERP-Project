using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ERP.Data;
using ERP.Models;
using Microsoft.EntityFrameworkCore;

namespace ERP.Services
{
    /// <summary>
    /// حسابات الخزينة/الصندوق/البنك: كل خزينة = حساب أصل (Asset) نشط في شجرة الحسابات.
    /// يُستخدم في أذون الاستلام والدفع وشاشة الخزينة — إضافة حسابات فرعية مثل 1101-2 يظهر كخزينة إضافية في القوائم.
    /// </summary>
    public static class TreasuryCashAccounts
    {
        /// <summary>استعلام حسابات تُعتبر «صندوق/خزينة/بنك» للنظام.</summary>
        /// <remarks>
        /// أي حساب نشط كوده يبدأ بـ 1101 أو 1102 يُعتبر خزينة/بنك حتى لو كان نوع الحساب في الدليل غير مضبوط كـ Asset (حالة شائعة في البيانات القديمة).
        /// أسماء عربية/إنجليزية تُستخدم مع نوع Asset فقط لتقليل الضجيج.
        /// </remarks>
        public static IQueryable<Account> QueryTreasuryCashBoxes(IQueryable<Account> accounts) =>
            accounts.Where(a => a.IsActive &&
                ((a.AccountCode != null &&
                  (a.AccountCode.Trim().StartsWith("1101") || a.AccountCode.Trim().StartsWith("1102"))) ||
                 (a.AccountType == AccountType.Asset && a.AccountName != null &&
                  (a.AccountName.Contains("خزينة") || a.AccountName.Contains("بنك") || a.AccountName.Contains("صندوق") ||
                   a.AccountName.Contains("Bank") || a.AccountName.Contains("bank") ||
                   a.AccountName.Contains("Cash") || a.AccountName.Contains("cash")))));

        public static async Task<List<int>> GetTreasuryCashBoxAccountIdsAsync(AppDbContext ctx, CancellationToken ct = default) =>
            await QueryTreasuryCashBoxes(ctx.Accounts.AsNoTracking()).Select(a => a.AccountId).ToListAsync(ct);

        /// <summary>الخزينة الافتراضية: أولوية لكود 1101 ثم باقي 1101* ثم الاسم.</summary>
        public static async Task<int?> GetDefaultTreasuryCashBoxAccountIdAsync(AppDbContext ctx, CancellationToken ct = default)
        {
            return await QueryTreasuryCashBoxes(ctx.Accounts.AsNoTracking())
                .OrderBy(a => a.AccountCode == "1101" ? 0 : 1)
                .ThenBy(a => a.AccountCode != null && a.AccountCode.StartsWith("1101") ? 0 : 1)
                .ThenBy(a => a.AccountCode)
                .ThenBy(a => a.AccountName)
                .Select(a => (int?)a.AccountId)
                .FirstOrDefaultAsync(ct);
        }

        public static async Task<bool> IsTreasuryCashBoxAsync(AppDbContext ctx, int accountId, CancellationToken ct = default) =>
            await QueryTreasuryCashBoxes(ctx.Accounts.AsNoTracking()).AnyAsync(a => a.AccountId == accountId, ct);

        /// <summary>مسموح اختيار الحساب كخزينة: معرّف كخزينة وليس ضمن الحسابات المخفية.</summary>
        public static async Task<bool> IsAllowedTreasuryCashBoxForUserAsync(AppDbContext ctx, int accountId, IReadOnlySet<int> hiddenAccountIds, CancellationToken ct = default)
        {
            if (hiddenAccountIds.Contains(accountId)) return false;
            return await IsTreasuryCashBoxAsync(ctx, accountId, ct);
        }
    }
}
