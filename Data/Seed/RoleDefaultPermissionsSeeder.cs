using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Models;
using Microsoft.EntityFrameworkCore;

namespace ERP.Data.Seed
{
    /// <summary>
    /// يضيف روابط RolePermissions الناقصة لكل دور حسب <see cref="RoleDefaultPermissionCatalog"/> دون حذف ما عيّنه العميل.
    /// يُنفَّذ بعد مزامنة الكتالوج و <see cref="GlobalPermissionSeeder"/>؛ دور «مالك النظام» يُستثنى (يُمنح كل الصلاحيات في Program.cs).
    /// </summary>
    public static class RoleDefaultPermissionsSeeder
    {
        public static async Task EnsureRoleDefaultPermissionsAsync(AppDbContext db)
        {
            var codeToId = await db.Permissions.AsNoTracking()
                .Where(p => p.Code != null && p.IsActive)
                .ToDictionaryAsync(p => p.Code!, p => p.PermissionId, StringComparer.OrdinalIgnoreCase);

            var ownerRoleNames = RoleSeeder.OwnerRoleAliases;

            var rolePacks = new (string Name, Func<IReadOnlyList<string>> Codes)[]
            {
                ("المدير العام", () => RoleDefaultPermissionCatalog.GeneralManager()),
                ("مدير المبيعات", () => RoleDefaultPermissionCatalog.SalesManager()),
                ("مندوب مبيعات", () => RoleDefaultPermissionCatalog.SalesRepresentative()),
                ("مدير المشتريات", () => RoleDefaultPermissionCatalog.PurchaseManager()),
                ("مدير المخازن", () => RoleDefaultPermissionCatalog.WarehouseManager()),
                ("محاسب", () => RoleDefaultPermissionCatalog.Accountant()),
                ("مستخدم تقارير", () => RoleDefaultPermissionCatalog.ReportsOnly()),
                ("مستخدم عادي", () => RoleDefaultPermissionCatalog.BasicUser()),
            };

            var now = DateTime.UtcNow;

            foreach (var (name, getCodes) in rolePacks)
            {
                if (ownerRoleNames.Contains(name))
                    continue;

                var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == name);
                if (role == null)
                    continue;

                var existing = await db.RolePermissions
                    .Where(rp => rp.RoleId == role.RoleId && rp.IsAllowed)
                    .Select(rp => rp.PermissionId)
                    .ToListAsync();
                var have = existing.ToHashSet();

                foreach (var code in getCodes())
                {
                    if (!codeToId.TryGetValue(code, out var pid))
                        continue;
                    if (have.Contains(pid))
                        continue;
                    db.RolePermissions.Add(new RolePermission
                    {
                        RoleId = role.RoleId,
                        PermissionId = pid,
                        IsAllowed = true,
                        CreatedAt = now
                    });
                    have.Add(pid);
                }
            }

            await db.SaveChangesAsync();
        }
    }
}
