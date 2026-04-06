using ERP.Controllers;
using ERP.Data;
using ERP.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace ERP.Tests;

public class UserRolesController_Create_Tests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static async Task<(User User, Role Role, Permission Permission)> SeedAsync(AppDbContext db)
    {
        var user = new User
        {
            UserName = "sales.manager",
            DisplayName = "مدير المبيعات",
            PasswordHash = "test",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var role = new Role
        {
            Name = "مدير المبيعات",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var permission = new Permission
        {
            Code = "Dashboard.Sales",
            NameAr = "مبيعاتي الشخصية",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        db.Users.Add(user);
        db.Roles.Add(role);
        db.Permissions.Add(permission);
        await db.SaveChangesAsync();

        db.RolePermissions.Add(new RolePermission
        {
            RoleId = role.RoleId,
            PermissionId = permission.PermissionId,
            IsAllowed = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        return (user, role, permission);
    }

    private static UserRolesController CreateController(AppDbContext db)
    {
        var httpContext = new DefaultHttpContext();
        var controller = new UserRolesController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            },
            TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>())
        };

        return controller;
    }

    [Fact]
    public async Task Create_WhenNoCustomPermissionSelectionIsPosted_DoesNotCreateDeniedPermissions()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedAsync(db);
        var controller = CreateController(db);

        var item = new UserRole
        {
            UserId = seeded.User.UserId,
            RoleId = seeded.Role.RoleId
        };

        var result = await controller.Create(item, selectedPermissionIds: null, RoleAccountIds: null, SelectedRoleAccountIds: null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(await db.UserRoles.AnyAsync(x => x.UserId == seeded.User.UserId && x.RoleId == seeded.Role.RoleId));
        Assert.False(await db.UserDeniedPermissions.AnyAsync(x => x.UserId == seeded.User.UserId && x.PermissionId == seeded.Permission.PermissionId && !x.IsAllowed));
    }

    [Fact]
    public async Task Create_WhenSelectedPermissionIdsIsEmptyArray_DoesNotDenyRolePermissions()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedAsync(db);
        var controller = CreateController(db);

        var item = new UserRole
        {
            UserId = seeded.User.UserId,
            RoleId = seeded.Role.RoleId
        };

        var result = await controller.Create(item, selectedPermissionIds: Array.Empty<int>(), RoleAccountIds: null, SelectedRoleAccountIds: null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.False(await db.UserDeniedPermissions.AnyAsync(x => x.UserId == seeded.User.UserId && x.PermissionId == seeded.Permission.PermissionId && !x.IsAllowed));
    }
}
