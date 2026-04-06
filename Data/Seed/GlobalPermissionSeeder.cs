using System;
using System.Linq;
using System.Threading.Tasks;
using ERP.Models;
using ERP.Security;
using Microsoft.EntityFrameworkCore;

namespace ERP.Data.Seed
{
    /// <summary>
    /// يربط صلاحيات Global.* بكل الأدوار النشطة إن لم تكن مربوطة بعد،
    /// حتى لا تُقفل الشاشات بعد إضافة البوابة العامة للتثبيتات القديمة.
    /// يُستدعى قبل صلاحيات الأدوار التفصيلية: أي إجراء يتطلب أولاً Global.Open / Edit / … حسب نوع العملية (انظر PermissionService + GlobalPermissionGates).
    /// </summary>
    public static class GlobalPermissionSeeder
    {
        public static async Task EnsureAllRolesHaveGlobalPermissionsAsync(AppDbContext db)
        {
            var globalPerms = await db.Permissions
                .AsNoTracking()
                .Where(p =>
                    p.Code == GlobalPermissionGates.Open
                    || p.Code == GlobalPermissionGates.Edit
                    || p.Code == GlobalPermissionGates.Delete
                    || p.Code == GlobalPermissionGates.Export
                    || p.Code == GlobalPermissionGates.Print
                    || p.Code == GlobalPermissionGates.ShowSummaries)
                .Select(p => new { p.PermissionId, p.Code })
                .ToListAsync();
            if (globalPerms.Count == 0) return;

            var globalPermIds = globalPerms.Select(g => g.PermissionId).ToList();

            var roleIds = await db.Roles
                .Where(r => r.IsActive)
                .Select(r => r.RoleId)
                .ToListAsync();

            var existing = await db.RolePermissions
                .Where(rp => roleIds.Contains(rp.RoleId) && globalPermIds.Contains(rp.PermissionId))
                .Select(rp => new { rp.RoleId, rp.PermissionId })
                .ToListAsync();

            var existingSet = new System.Collections.Generic.HashSet<(int RoleId, int PermissionId)>(
                existing.Select(e => (e.RoleId, e.PermissionId)));

            var now = DateTime.UtcNow;
            foreach (var roleId in roleIds)
            {
                foreach (var gp in globalPerms)
                {
                    if (existingSet.Contains((roleId, gp.PermissionId))) continue;
                    db.RolePermissions.Add(new RolePermission
                    {
                        RoleId = roleId,
                        PermissionId = gp.PermissionId,
                        IsAllowed = true,
                        CreatedAt = now
                    });
                    existingSet.Add((roleId, gp.PermissionId));
                }
            }

            await db.SaveChangesAsync();
        }
    }
}
