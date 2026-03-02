using Microsoft.AspNetCore.Mvc;

namespace ERP.Filters
{
    /// <summary>
    /// سمة توضع على الـ Controller أو الـ Action لتشترط صلاحية معينة.
    /// مثال: [RequirePermission("SalesInvoices.Show")]
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public class RequirePermissionAttribute : TypeFilterAttribute
    {
        public RequirePermissionAttribute(string permissionCode)
            : base(typeof(RequirePermissionFilter))
        {
            PermissionCode = permissionCode ?? "";
            Arguments = new object[] { PermissionCode };
        }

        public string PermissionCode { get; }
    }
}
