using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ERP.Models
{
    /// <summary>
    /// الحسابات المسموح للمستخدم رؤيتها فقط (قائمة بيضاء).
    /// إذا وُجدت للمستخدم سجلات بـ IsAllowed = true فإنه يرى تلك الحسابات فقط؛
    /// إذا لم توجد أي سجل فلا تقييد (يرى كل الحسابات).
    /// </summary>
    [Index(nameof(UserId), nameof(AccountId), IsUnique = true)]
    public class UserAccountVisibilityOverride
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public int UserId { get; set; }
        public int AccountId { get; set; }

        public bool IsAllowed { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(UserId))]
        public virtual User User { get; set; } = null!;

        [ForeignKey(nameof(AccountId))]
        public virtual Account Account { get; set; } = null!;
    }
}

