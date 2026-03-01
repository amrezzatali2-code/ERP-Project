using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Models;                      // جدول RolePermission / Role / Permission
using Microsoft.EntityFrameworkCore;

namespace ERP.Data.Seed
{
    /// <summary>
    /// Seeder لجدول صلاحيات الأدوار.
    /// - يربط كل دور بكل صلاحية (أو حسب المنطق).
    /// - يمنع التكرار.
    /// - يملأ CreatedAt للسطور الجديدة و أي سطور قديمة فاضية.
    /// </summary>
    public static class RolePermissionSeeder
    {
        public static async Task SeedAsync(AppDbContext db)
        {
            // لو مفيش أدوار أو مفيش صلاحيات نخرج
            if (!await db.Roles.AnyAsync() || !await db.Permissions.AnyAsync())
                return;

            // تحميل الأدوار والصلاحيات
            var roles = await db.Roles
                .AsNoTracking()
                .ToListAsync();                       // كل الأدوار

            var permissions = await db.Permissions
                .AsNoTracking()
                .Where(p => p.IsActive)
                .ToListAsync();                       // كل الصلاحيات المفعّلة

            // الروابط الموجودة بالفعل (علشان ما نكررش)
            var existingLinks = await db.RolePermissions
                .AsNoTracking()
                .Select(rp => new { rp.RoleId, rp.PermissionId })
                .ToListAsync();

            var existingSet = new HashSet<(int RoleId, int PermissionId)>(
                existingLinks.Select(x => (x.RoleId, x.PermissionId)));

            var now = DateTime.UtcNow;               // وقت التنفيذ
            var toInsert = new List<RolePermission>();

            foreach (var role in roles)
            {
                foreach (var perm in permissions)
                {
                    var key = (role.RoleId, perm.PermissionId);

                    // لو الرابط موجود خلاص نتجاهله
                    if (existingSet.Contains(key))
                        continue;

                    var rp = new RolePermission
                    {
                        RoleId = role.RoleId,            // ربط بالدور
                        PermissionId = perm.PermissionId, // ربط بالصلاحية
                        IsAllowed = true,                // افتراضيًا مسموح لجميع الأدوار؛ التقييد من واجهة صلاحيات الأدوار
                        CreatedAt = now,                 // تاريخ الإنشاء
                        UpdatedAt = null
                    };

                    toInsert.Add(rp);
                }
            }

            if (toInsert.Count > 0)
            {
                await db.RolePermissions.AddRangeAsync(toInsert);
            }

            // تحديث أي سطور قديمة CreatedAt فيها بالقيمة الافتراضية (0001/01/01)
            var needDates = await db.RolePermissions
                .Where(rp => rp.CreatedAt == default)
                .ToListAsync();

            foreach (var rp in needDates)
            {
                rp.CreatedAt = now;
            }

            if (toInsert.Count > 0 || needDates.Count > 0)
            {
                await db.SaveChangesAsync();
            }
        }
    }
}
