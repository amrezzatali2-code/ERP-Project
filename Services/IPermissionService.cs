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
    }
}
