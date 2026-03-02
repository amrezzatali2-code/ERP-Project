using System;
using System.Linq;
using System.Threading.Tasks;
using ERP.Models;
using Microsoft.EntityFrameworkCore;

namespace ERP.Data.Seed
{
    /// <summary>
    /// أدوار النظام تُستخرج من الكود: قائمة مختصرة. لإضافة دور جديد أضفه هنا ثم أعد تشغيل التطبيق.
    /// </summary>
    public static class RoleSeeder
    {
        public static async Task SeedAsync(AppDbContext db)
        {
            var roles = new[]
            {
                new { Name = "مالك النظام", Description = "كل الصلاحيات", IsSystem = true },
                new { Name = "المدير العام", Description = "مدير الشركة", IsSystem = true },
                new { Name = "مدير المبيعات", Description = "إدارة المبيعات والفريق", IsSystem = false },
                new { Name = "مندوب مبيعات", Description = "فاتورة مبيعات وتحصيل", IsSystem = false },
                new { Name = "مدير المشتريات", Description = "طلبات وفواتير المشتريات", IsSystem = false },
                new { Name = "مدير المخازن", Description = "المخزون والتسويات والتحويلات", IsSystem = false },
                new { Name = "محاسب", Description = "الحسابات والقيود والتقارير", IsSystem = false },
                new { Name = "مستخدم تقارير", Description = "عرض التقارير فقط", IsSystem = false },
                new { Name = "مستخدم عادي", Description = "صلاحيات محدودة", IsSystem = false },
            };

            foreach (var r in roles)
            {
                var existing = await db.Roles.FirstOrDefaultAsync(x => x.Name == r.Name);
                if (existing != null)
                {
                    existing.Description = r.Description;
                    existing.IsSystemRole = r.IsSystem;
                    existing.IsActive = true;
                    existing.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    db.Roles.Add(new Role
                    {
                        Name = r.Name,
                        Description = r.Description,
                        IsSystemRole = r.IsSystem,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            await db.SaveChangesAsync();
        }
    }
}
