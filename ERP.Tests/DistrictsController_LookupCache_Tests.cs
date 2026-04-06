using ERP.Controllers;
using ERP.Data;
using ERP.Infrastructure;
using ERP.Models;
using ERP.Services.Caching;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;

namespace ERP.Tests;

public class DistrictsController_LookupCache_Tests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static DistrictsController CreateController(AppDbContext db, ILookupCacheService lookupCache)
    {
        var activityLogger = new Mock<IUserActivityLogger>(MockBehavior.Strict);
        activityLogger
            .Setup(x => x.LogAsync(
                It.IsAny<UserActionType>(),
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        var controller = new DistrictsController(db, activityLogger.Object, lookupCache);
        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        return controller;
    }

    private static string[] ReadSelectTexts(object? items)
    {
        var list = Assert.IsAssignableFrom<IEnumerable<SelectListItem>>(items);
        return list.Select(x => x.Text ?? string.Empty).ToArray();
    }

    [Fact]
    public async Task Create_PopulatesGovernoratesFromLookupCacheUntilItIsCleared()
    {
        await using var db = CreateDbContext();
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());

        db.Governorates.Add(new Governorate { GovernorateName = "القاهرة" });
        await db.SaveChangesAsync();

        var lookupCache = new LookupCacheService(db, memoryCache);
        var controller = CreateController(db, lookupCache);

        var firstResult = Assert.IsType<ViewResult>(await controller.Create());
        Assert.IsType<District>(firstResult.Model);
        Assert.Single(ReadSelectTexts(controller.ViewBag.GovernorateId));

        db.Governorates.Add(new Governorate { GovernorateName = "الجيزة" });
        await db.SaveChangesAsync();

        await controller.Create();
        Assert.Single(ReadSelectTexts(controller.ViewBag.GovernorateId));

        lookupCache.ClearGovernoratesCache();

        await controller.Create();
        Assert.Equal(new[] { "الجيزة", "القاهرة" }, ReadSelectTexts(controller.ViewBag.GovernorateId));
    }
}
