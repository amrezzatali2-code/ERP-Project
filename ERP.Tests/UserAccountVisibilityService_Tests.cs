using System.Security.Claims;
using ERP.Data;
using ERP.Models;
using ERP.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace ERP.Tests;

public class UserAccountVisibilityService_Tests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static UserAccountVisibilityService CreateService(AppDbContext db, int userId)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        ], "TestAuth"));

        var httpAccessor = new HttpContextAccessor { HttpContext = httpContext };
        var permissionService = new Mock<IPermissionService>(MockBehavior.Strict);
        permissionService
            .Setup(x => x.HasPermissionAsync(userId, "UserAccountVisibility.SeeAll"))
            .ReturnsAsync(false);

        return new UserAccountVisibilityService(db, httpAccessor, permissionService.Object);
    }

    private static async Task<(int customerAccountId, int investorAccountId, int userId, int roleId)> SeedVisibilityGraphAsync(AppDbContext db)
    {
        var customerRoot = new Account
        {
            AccountCode = "1100",
            AccountName = "الأصول المتداولة",
            AccountType = AccountType.Asset,
            Level = 1,
            IsLeaf = false,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var investorRoot = new Account
        {
            AccountCode = "3100",
            AccountName = "رأس المال",
            AccountType = AccountType.Equity,
            Level = 1,
            IsLeaf = false,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Accounts.AddRange(customerRoot, investorRoot);
        await db.SaveChangesAsync();

        var customerAccount = new Account
        {
            AccountCode = "1103",
            AccountName = "حساب العملاء",
            AccountType = AccountType.Asset,
            ParentAccountId = customerRoot.AccountId,
            Level = 2,
            IsLeaf = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var investorAccount = new Account
        {
            AccountCode = "3101",
            AccountName = "حساب المستثمرين",
            AccountType = AccountType.Equity,
            ParentAccountId = investorRoot.AccountId,
            Level = 2,
            IsLeaf = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Accounts.AddRange(customerAccount, investorAccount);

        var user = new User
        {
            UserName = "tester",
            DisplayName = "Tester",
            PasswordHash = "hash",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var role = new Role
        {
            Name = "Sales",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        db.UserRoles.Add(new UserRole
        {
            UserId = user.UserId,
            RoleId = role.RoleId,
            IsPrimary = true,
            AssignedAt = DateTime.UtcNow
        });

        db.Customers.AddRange(
            new Customer
            {
                CustomerName = "عميل العملاء",
                PartyCategory = "Customer",
                AccountId = customerAccount.AccountId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Customer
            {
                CustomerName = "مستثمر رأس المال",
                PartyCategory = "Investor",
                AccountId = investorAccount.AccountId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

        await db.SaveChangesAsync();
        return (customerAccount.AccountId, investorAccount.AccountId, user.UserId, role.RoleId);
    }

    [Fact]
    public async Task ApplyCustomerVisibilityFilterAsync_HidesInvestorCustomers_WhenRoleAllowsOnlyCustomerAccount()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedVisibilityGraphAsync(db);

        db.RoleAccountVisibilityOverrides.Add(new RoleAccountVisibilityOverride
        {
            RoleId = seeded.roleId,
            AccountId = seeded.customerAccountId,
            IsAllowed = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, seeded.userId);
        var filtered = await service.ApplyCustomerVisibilityFilterAsync(db.Customers.AsNoTracking());
        var names = await filtered.OrderBy(c => c.CustomerName).Select(c => c.CustomerName).ToListAsync();

        Assert.Contains("عميل العملاء", names);
        Assert.DoesNotContain("مستثمر رأس المال", names);
    }

    [Fact]
    public async Task ApplyCustomerVisibilityFilterAsync_PrefersRoleVisibility_WhenLegacyUserOverrideStillAllowsInvestorAccount()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedVisibilityGraphAsync(db);

        db.RoleAccountVisibilityOverrides.Add(new RoleAccountVisibilityOverride
        {
            RoleId = seeded.roleId,
            AccountId = seeded.customerAccountId,
            IsAllowed = true,
            CreatedAt = DateTime.UtcNow
        });
        db.UserAccountVisibilityOverrides.Add(new UserAccountVisibilityOverride
        {
            UserId = seeded.userId,
            AccountId = seeded.investorAccountId,
            IsAllowed = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, seeded.userId);
        var filtered = await service.ApplyCustomerVisibilityFilterAsync(db.Customers.AsNoTracking());
        var names = await filtered.OrderBy(c => c.CustomerName).Select(c => c.CustomerName).ToListAsync();

        Assert.Contains("عميل العملاء", names);
        Assert.DoesNotContain("مستثمر رأس المال", names);
    }
}