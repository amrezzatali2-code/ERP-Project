using ERP.Data;
using ERP.Models;
using ERP.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ERP.Tests;

public class LoginRedirectService_Tests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static PermissionService CreatePermissionService(AppDbContext db)
    {
        return new PermissionService(db, new HttpContextAccessor());
    }

    private static async Task<(User User, Role Role)> SeedUserWithPermissionsAsync(AppDbContext db, params string[] permissionCodes)
    {
        var user = new User
        {
            UserName = "user." + Guid.NewGuid().ToString("N"),
            DisplayName = "اختبار",
            PasswordHash = "test",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var role = new Role
        {
            Name = "role-" + Guid.NewGuid().ToString("N"),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        db.Users.Add(user);
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        foreach (var code in permissionCodes)
        {
            var permission = new Permission
            {
                Code = code,
                NameAr = code,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            db.Permissions.Add(permission);
            await db.SaveChangesAsync();

            db.RolePermissions.Add(new RolePermission
            {
                RoleId = role.RoleId,
                PermissionId = permission.PermissionId,
                IsAllowed = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        db.UserRoles.Add(new UserRole
        {
            UserId = user.UserId,
            RoleId = role.RoleId,
            AssignedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        return (user, role);
    }

    [Fact]
    public async Task GetTargetAsync_WhenUserHasDashboardSales_ReturnsSalesDashboard()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedUserWithPermissionsAsync(db, "Dashboard.Sales");
        var service = new LoginRedirectService(CreatePermissionService(db));

        var target = await service.GetTargetAsync(seeded.User.UserId);

        Assert.Equal("Sales", target.Action);
        Assert.Equal("Dashboard", target.Controller);
    }

    [Fact]
    public async Task GetTargetAsync_WhenUserHasAccountsOnly_ReturnsAccountsIndex()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedUserWithPermissionsAsync(db, "Accounts.Index");
        var service = new LoginRedirectService(CreatePermissionService(db));

        var target = await service.GetTargetAsync(seeded.User.UserId);

        Assert.Equal("Index", target.Action);
        Assert.Equal("Accounts", target.Controller);
    }

    [Fact]
    public async Task GetTargetAsync_WhenNoSupportedPermissions_ReturnsAccessDenied()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedUserWithPermissionsAsync(db, "Roles.Index");
        var service = new LoginRedirectService(CreatePermissionService(db));

        var target = await service.GetTargetAsync(seeded.User.UserId);

        Assert.Equal("AccessDenied", target.Action);
        Assert.Equal("Home", target.Controller);
    }
}
