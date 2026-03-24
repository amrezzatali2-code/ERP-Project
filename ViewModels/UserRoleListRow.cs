namespace ERP.ViewModels
{
    /// <summary>
    /// صف قائمة «أدوار المستخدمين»: مستخدم مع سطر ربط دور (أو صف واحد بدون دور).
    /// </summary>
    public class UserRoleListRow
    {
        /// <summary>النص المعروض في عمود الدور عندما لا يوجد ربط دور.</summary>
        public const string NoRoleDisplay = "ليس له دور";

        /// <summary>معرّف سطر UserRole؛ فارغ إن لم يُسند للمستخدم أي دور في هذا الصف.</summary>
        public int? UserRoleId { get; set; }

        public int UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;

        public int? RoleId { get; set; }
        public string? RoleName { get; set; }
        public string? RoleDescription { get; set; }

        public bool IsPrimary { get; set; }
        public DateTime? AssignedAt { get; set; }
    }
}
