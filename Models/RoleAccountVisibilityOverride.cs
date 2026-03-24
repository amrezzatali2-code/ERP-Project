using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ERP.Models
{
    /// <summary>
    /// الحسابات المسموح رؤيتها لمن يحمل هذا الدور (قائمة بيضاء لكل دور).
    /// للمستخدم الفعلي: اتحاد المسموح من كل أدواره + أي إعداد قديم على مستوى المستخدم (<see cref="UserAccountVisibilityOverride"/>).
    /// </summary>
    [Index(nameof(RoleId), nameof(AccountId), IsUnique = true)]
    public class RoleAccountVisibilityOverride
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public int RoleId { get; set; }
        public int AccountId { get; set; }

        public bool IsAllowed { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(RoleId))]
        public virtual Role Role { get; set; } = null!;

        [ForeignKey(nameof(AccountId))]
        public virtual Account Account { get; set; } = null!;
    }
}
