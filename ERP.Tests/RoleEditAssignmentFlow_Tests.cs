using ERP.Controllers;
using ERP.Data;
using ERP.Infrastructure;
using ERP.Models;
using ERP.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;

namespace ERP.Tests;

public class RoleEditAssignmentFlow_Tests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static TempDataDictionary CreateTempData(HttpContext httpContext)
    {
        return new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
    }

    private static RolesController CreateRolesController(AppDbContext db)
    {
        var httpContext = new DefaultHttpContext();
        return new RolesController(db, Mock.Of<IUserActivityLogger>())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = CreateTempData(httpContext)
        };
    }

    private static RolePermissionsController CreateRolePermissionsController(AppDbContext db)
    {
        var httpContext = new DefaultHttpContext();
        return new RolePermissionsController(db)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = CreateTempData(httpContext)
        };
    }

    private static UserRolesController CreateUserRolesController(AppDbContext db)
    {
        var httpContext = new DefaultHttpContext();
        return new UserRolesController(db)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = CreateTempData(httpContext)
        };
    }

    [Fact]
    public async Task EditedRolePermissions_ThenAssignedToNewUser_AllowsLoginRedirectToAccounts()
    {
        await using var db = CreateDbContext();

        var role = new Role
        {
            Name = "حسابات",
            Description = "قديم",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var user = new User
        {
            UserName = "accounts.user",
            DisplayName = "حسابات",
            PasswordHash = "test",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var globalOpen = new Permission
        {
            Code = "Global.Open",
            NameAr = "عام — فتح",
            Module = "صلاحيات عامة",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var accountsIndex = new Permission
        {
            Code = "Accounts.Index",
            NameAr = "قائمة الحسابات",
            Module = "الحسابات",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        db.Roles.Add(role);
        db.Users.Add(user);
        db.Permissions.AddRange(globalOpen, accountsIndex);
        await db.SaveChangesAsync();

        var rolesController = CreateRolesController(db);
        var rolePermissionsController = CreateRolePermissionsController(db);
        var userRolesController = CreateUserRolesController(db);

        var editRoleResult = await rolesController.Edit(role.RoleId, new Role
        {
            RoleId = role.RoleId,
            Name = "حسابات عامة",
            Description = "معدل",
            IsActive = true,
            IsSystemRole = false
        });

        Assert.IsType<RedirectToActionResult>(editRoleResult);

        var editPermissionsResult = await rolePermissionsController.Edit(
            roleId: role.RoleId,
            selectedPermissionIds: new[] { globalOpen.PermissionId, accountsIndex.PermissionId },
            RoleAccountIds: null,
            SelectedRoleAccountIds: null,
            frame: false);

        Assert.IsType<RedirectToActionResult>(editPermissionsResult);

        var assignResult = await userRolesController.Create(
            new UserRole
            {
                UserId = user.UserId,
                RoleId = role.RoleId
            },
            selectedPermissionIds: new[] { globalOpen.PermissionId, accountsIndex.PermissionId },
            RoleAccountIds: null,
            SelectedRoleAccountIds: null);

        Assert.IsType<RedirectToActionResult>(assignResult);

        var redirectService = new LoginRedirectService(
            new PermissionService(db, new HttpContextAccessor(), new MemoryCache(new MemoryCacheOptions())));
        var target = await redirectService.GetTargetAsync(user.UserId);

        Assert.Equal("Index", target.Action);
        Assert.Equal("Accounts", target.Controller);
        Assert.False(await db.UserDeniedPermissions.AnyAsync(x => x.UserId == user.UserId && !x.IsAllowed));
    }
}
