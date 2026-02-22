namespace ERP.ViewModels
{
    /// <summary>
    /// عنصر صلاحية لعرضه في شاشة إسناد الدور مع إمكانية التعديل.
    /// </summary>
    public class RolePermissionEditItem
    {
        public int PermissionId { get; set; }
        public string? Code { get; set; }
        public string? NameAr { get; set; }
        public string? Module { get; set; }
        /// <summary>
        /// هل الصلاحية مسموحة في الدور (الحالة الابتدائية للشيك بوكس).
        /// </summary>
        public bool IsAllowed { get; set; }
    }
}
