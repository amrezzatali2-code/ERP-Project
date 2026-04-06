using ERP.Controllers;
using ERP.Data;
using ERP.Infrastructure;
using ERP.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Tests;

public class UserActivityLogsController_Search_Tests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        var user = new User
        {
            UserName = "amr",
            DisplayName = "عمرو عزت",
            PasswordHash = "test",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserActivityLogs.AddRange(
            new UserActivityLog
            {
                Id = 12,
                UserId = user.UserId,
                ActionType = UserActionType.View,
                EntityName = "Customer",
                EntityId = 101,
                Description = "عرض بطاقة عميل",
                ActionTime = new DateTime(2026, 4, 4, 9, 0, 0, DateTimeKind.Utc),
                IpAddress = "127.0.0.1"
            },
            new UserActivityLog
            {
                Id = 123,
                UserId = user.UserId,
                ActionType = UserActionType.Edit,
                EntityName = "Product",
                EntityId = 202,
                Description = "تعديل بيانات صنف",
                ActionTime = new DateTime(2026, 4, 4, 10, 0, 0, DateTimeKind.Utc),
                IpAddress = "127.0.0.2"
            });

        await db.SaveChangesAsync();
    }

    private static UserActivityLogsController CreateController(AppDbContext db)
    {
        var controller = new UserActivityLogsController(db);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    [Fact]
    public async Task Index_SearchById_TreatsNumericSearchAsExactMatchEvenWithArabicDigits()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);

        var controller = CreateController(db);

        var result = Assert.IsType<ViewResult>(await controller.Index(search: "١٢", searchBy: "id", sort: null, dir: null));
        var model = Assert.IsType<PagedResult<UserActivityLog>>(result.Model);

        Assert.Single(model.Items);
        Assert.Equal(12, model.Items[0].Id);
    }

    [Fact]
    public async Task Index_SearchByDescription_TreatsTextSearchAsContainsMatch()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);

        var controller = CreateController(db);

        var result = Assert.IsType<ViewResult>(await controller.Index(search: "بطاقة", searchBy: "description", sort: null, dir: null));
        var model = Assert.IsType<PagedResult<UserActivityLog>>(result.Model);

        Assert.Single(model.Items);
        Assert.Equal(12, model.Items[0].Id);
        Assert.Contains("عرض بطاقة عميل", model.Items[0].Description);
    }
}
