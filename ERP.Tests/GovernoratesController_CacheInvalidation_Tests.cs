using ERP.Controllers;
using ERP.Data;
using ERP.Infrastructure;
using ERP.Models;
using ERP.Services.Caching;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace ERP.Tests;

public class GovernoratesController_CacheInvalidation_Tests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static GovernoratesController CreateController(
        AppDbContext db,
        Mock<ILookupCacheService> lookupCache)
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

        var controller = new GovernoratesController(
            db,
            activityLogger.Object,
            lookupCache.Object);

        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        return controller;
    }

    [Fact]
    public async Task Create_ClearsAllGeographyCaches_AfterSuccessfulSave()
    {
        await using var db = CreateDbContext();
        var lookupCache = new Mock<ILookupCacheService>(MockBehavior.Strict);
        lookupCache.Setup(x => x.ClearAllGeographyCaches());

        var controller = CreateController(db, lookupCache);

        var result = await controller.Create(new Governorate { GovernorateName = "محافظة جديدة" });

        Assert.IsType<RedirectToActionResult>(result);
        lookupCache.Verify(x => x.ClearAllGeographyCaches(), Times.Once);
    }

    [Fact]
    public async Task Edit_ClearsAllGeographyCaches_AfterSuccessfulSave()
    {
        await using var db = CreateDbContext();
        var governorate = new Governorate { GovernorateName = "قبل التعديل" };
        db.Governorates.Add(governorate);
        await db.SaveChangesAsync();

        var lookupCache = new Mock<ILookupCacheService>(MockBehavior.Strict);
        lookupCache.Setup(x => x.ClearAllGeographyCaches());

        var controller = CreateController(db, lookupCache);

        var result = await controller.Edit(governorate.GovernorateId, new Governorate { GovernorateName = "بعد التعديل" });

        Assert.IsType<RedirectToActionResult>(result);
        lookupCache.Verify(x => x.ClearAllGeographyCaches(), Times.Once);
    }

    [Fact]
    public async Task DeleteConfirmed_ClearsAllGeographyCaches_AfterSuccessfulDelete()
    {
        await using var db = CreateDbContext();
        var governorate = new Governorate { GovernorateName = "للحذف" };
        db.Governorates.Add(governorate);
        await db.SaveChangesAsync();

        var lookupCache = new Mock<ILookupCacheService>(MockBehavior.Strict);
        lookupCache.Setup(x => x.ClearAllGeographyCaches());

        var controller = CreateController(db, lookupCache);

        var result = await controller.DeleteConfirmed(governorate.GovernorateId);

        Assert.IsType<RedirectToActionResult>(result);
        lookupCache.Verify(x => x.ClearAllGeographyCaches(), Times.Once);
    }

    [Fact]
    public async Task BulkDelete_ClearsAllGeographyCaches_WhenItemsAreDeleted()
    {
        await using var db = CreateDbContext();
        db.Governorates.AddRange(
            new Governorate { GovernorateName = "محافظة 1" },
            new Governorate { GovernorateName = "محافظة 2" });
        await db.SaveChangesAsync();

        var ids = await db.Governorates.OrderBy(x => x.GovernorateId).Select(x => x.GovernorateId).ToListAsync();

        var lookupCache = new Mock<ILookupCacheService>(MockBehavior.Strict);
        lookupCache.Setup(x => x.ClearAllGeographyCaches());

        var controller = CreateController(db, lookupCache);

        var result = await controller.BulkDelete(string.Join(",", ids));

        Assert.IsType<RedirectToActionResult>(result);
        lookupCache.Verify(x => x.ClearAllGeographyCaches(), Times.Once);
    }

    [Fact]
    public async Task DeleteAll_ClearsAllGeographyCaches_WhenItemsExist()
    {
        await using var db = CreateDbContext();
        db.Governorates.Add(new Governorate { GovernorateName = "محافظة موجودة" });
        await db.SaveChangesAsync();

        var lookupCache = new Mock<ILookupCacheService>(MockBehavior.Strict);
        lookupCache.Setup(x => x.ClearAllGeographyCaches());

        var controller = CreateController(db, lookupCache);

        var result = await controller.DeleteAll();

        Assert.IsType<RedirectToActionResult>(result);
        lookupCache.Verify(x => x.ClearAllGeographyCaches(), Times.Once);
    }
}
