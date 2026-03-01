namespace ERP.Services
{
    /// <summary>
    /// خدمة التحقق من صلاحيات المستخدم.
    /// الصلاحية النهائية = (من الأدوار) + (صلاحيات إضافية) - (صلاحيات ممنوعة)
    /// </summary>
    public interface IPermissionService
    {
        /// <summary>
        /// هل المستخدم الحالي يملك الصلاحية المحددة؟
        /// </summary>
        Task<bool> HasPermissionAsync(int userId, string permissionCode);

        /// <summary>
        /// هل المستخدم الحالي يملك الصلاحية؟ (يقرأ UserId من HttpContext)
        /// </summary>
        Task<bool> HasPermissionAsync(string permissionCode);

        /// <summary>
        /// يرجع مجموعة أكواد الصلاحيات الممنوحة للمستخدم (للعرض في القائمة وغيرها).
        /// </summary>
        Task<HashSet<string>> GetUserPermissionCodesAsync(int userId);

        /// <summary>
        /// هل للمستخدم الحالي أي صلاحية يبدأ كودها بالبادئة المعطاة؟ (مثل "Sales." لفتح المبيعات)
        /// </summary>
        Task<bool> HasAnyPermissionWithPrefixAsync(string codePrefix);
    }
}
