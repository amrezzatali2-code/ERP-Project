using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Models;

namespace ERP.Services
{
    /// <summary>
    /// صلاحيات ظهور الحسابات للمستخدم — مصدر واحد. السياسة الموثّقة: <c>.cursor/rules/user-account-visibility-pattern.mdc</c>.
    /// </summary>
    public interface IUserAccountVisibilityService
    {
        /// <summary>معرّفات الحسابات المخفية عن المستخدم الحالي (فارغ = لا إخفاء).</summary>
        Task<HashSet<int>> GetHiddenAccountIdsForCurrentUserAsync();

        /// <summary>معرّفات الحسابات المخفية لمستخدم عند وجود قائمة مسموح؛ المسموح يُوسَّع تلقائياً ليشمل الحسابات الفرعية في الشجرة (انظر <c>UserAccountVisibilityService</c>).</summary>
        Task<HashSet<int>> GetHiddenAccountIdsForUserAsync(int userId);

        /// <summary>صحيح عندما وُجدت قائمة حسابات مسموح بها على المستخدم و/أو أدواره (<c>UserAccountVisibilityOverrides</c> + <c>RoleAccountVisibilityOverrides</c>) — أي تقييد «المسموح فقط».</summary>
        Task<bool> IsRestrictedToAllowedAccountsOnlyAsync();

        /// <summary>يجمع <see cref="GetHiddenAccountIdsForCurrentUserAsync"/> و <see cref="IsRestrictedToAllowedAccountsOnlyAsync"/> لاستدعاء واحد.</summary>
        Task<(HashSet<int> hiddenAccountIds, bool restrictedOnly)> GetVisibilityStateForCurrentUserAsync();

        /// <summary>صحيح عندما لا توجد حسابات مخفية (<c>hidden.Count == 0</c>) — تُعرض القوائم المنسدلة دون تصفية حسابات.</summary>
        Task<bool> ShouldShowAllAccountsInDropdownsAsync();

        /// <summary>
        /// فلتر <see cref="Customer"/> حسب صلاحيات الحسابات.
        /// عند التقييد بقائمة مسموح: يظهر الطرف إذا <c>AccountId</c> مسموح، أو (عميل/مورد فقط) له قيد في <c>LedgerEntries</c> على حساب مسموح.
        /// المستثمر والموظف: <b>حساب رئيسي فقط</b> — بدون توسيع عبر القيود.
        /// </summary>
        Task<IQueryable<Customer>> ApplyCustomerVisibilityFilterAsync(IQueryable<Customer> query);
    }
}

