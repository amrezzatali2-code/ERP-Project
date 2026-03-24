using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Data;
using ERP.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure
{
    /// <summary>قراءة الحسابات المختارة من الفورم وحفظها لجدول <see cref="RoleAccountVisibilityOverride"/>.</summary>
    public static class RoleAccountVisibilityPersistence
    {
        public static List<int> ParseSelectedAccountIds(
            HttpRequest? request,
            List<int>? roleAccountIds,
            string? selectedCsv)
        {
            var selected = new List<int>();
            if (!string.IsNullOrWhiteSpace(selectedCsv))
            {
                foreach (var part in selectedCsv.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    if (int.TryParse(part.Trim(), out var id) && id > 0 && !selected.Contains(id))
                        selected.Add(id);
            }
            if (selected.Count == 0 && roleAccountIds != null && roleAccountIds.Count > 0)
            {
                foreach (var id in roleAccountIds.Where(id => id > 0))
                    if (!selected.Contains(id)) selected.Add(id);
            }
            if (selected.Count == 0 && request?.HasFormContentType == true)
            {
                foreach (var key in request.Form.Keys)
                {
                    if (key == null) continue;
                    if (!key.Equals("RoleAccountIds", StringComparison.OrdinalIgnoreCase) &&
                        !key.StartsWith("RoleAccountIds[", StringComparison.OrdinalIgnoreCase))
                        continue;
                    foreach (var v in request.Form[key])
                        if (int.TryParse(v, out var id) && id > 0 && !selected.Contains(id))
                            selected.Add(id);
                }
            }
            return selected;
        }

        public static async Task ReplaceForRoleAsync(AppDbContext context, int roleId, IEnumerable<int> accountIds)
        {
            var existing = await context.RoleAccountVisibilityOverrides
                .Where(x => x.RoleId == roleId)
                .ToListAsync();
            if (existing.Count > 0)
                context.RoleAccountVisibilityOverrides.RemoveRange(existing);

            var now = DateTime.UtcNow;
            foreach (var accountId in accountIds.Distinct())
            {
                if (accountId <= 0) continue;
                if (!await context.Accounts.AnyAsync(a => a.AccountId == accountId))
                    continue;
                context.RoleAccountVisibilityOverrides.Add(new RoleAccountVisibilityOverride
                {
                    RoleId = roleId,
                    AccountId = accountId,
                    IsAllowed = true,
                    CreatedAt = now
                });
            }
        }
    }
}
