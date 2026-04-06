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

public class ProductGroupsController_CacheInvalidation_Tests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static ProductGroupsController CreateController(AppDbContext db, Mock<ILookupCacheService> lookupCache)
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

        var controller = new ProductGroupsController(db, activityLogger.Object, lookupCache.Object);
        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        return controller;
    }

    [Fact]
    public async Task Create_ClearsProductGroupsCache_AfterSuccessfulSave()
    {
        await using var db = CreateDbContext();
        var lookupCache = new Mock<ILookupCacheService>(MockBehavior.Strict);
        lookupCache.Setup(x => x.ClearProductGroupsCache());

        var controller = CreateController(db, lookupCache);

        var result = await controller.Create(new ProductGroup { Name = "مجموعة جديدة", IsActive = true });

        Assert.IsType<RedirectToActionResult>(result);
        lookupCache.Verify(x => x.ClearProductGroupsCache(), Times.Once);
    }

    [Fact]
    public async Task Edit_ClearsProductGroupsCache_AfterSuccessfulSave()
    {
        await using var db = CreateDbContext();
        var group = new ProductGroup { Name = "قبل التعديل", IsActive = true, CreatedAt = DateTime.UtcNow };
        db.ProductGroups.Add(group);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var lookupCache = new Mock<ILookupCacheService>(MockBehavior.Strict);
        lookupCache.Setup(x => x.ClearProductGroupsCache());

        var controller = CreateController(db, lookupCache);

        var result = await controller.Edit(group.ProductGroupId, new ProductGroup
        {
            ProductGroupId = group.ProductGroupId,
            Name = "بعد التعديل",
            IsActive = false,
            CreatedAt = group.CreatedAt
        });

        Assert.IsType<RedirectToActionResult>(result);
        lookupCache.Verify(x => x.ClearProductGroupsCache(), Times.Once);
    }

    [Fact]
    public async Task DeleteConfirmed_ClearsProductGroupsCache_AfterSuccessfulDelete()
    {
        await using var db = CreateDbContext();
        var group = new ProductGroup { Name = "للحذف", IsActive = true, CreatedAt = DateTime.UtcNow };
        db.ProductGroups.Add(group);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var lookupCache = new Mock<ILookupCacheService>(MockBehavior.Strict);
        lookupCache.Setup(x => x.ClearProductGroupsCache());

        var controller = CreateController(db, lookupCache);

        var result = await controller.DeleteConfirmed(group.ProductGroupId);

        Assert.IsType<RedirectToActionResult>(result);
        lookupCache.Verify(x => x.ClearProductGroupsCache(), Times.Once);
    }

    [Fact]
    public async Task BulkDelete_ClearsProductGroupsCache_WhenAnyGroupsAreDeleted()
    {
        await using var db = CreateDbContext();
        db.ProductGroups.AddRange(
            new ProductGroup { Name = "مجموعة 1", IsActive = true, CreatedAt = DateTime.UtcNow },
            new ProductGroup { Name = "مجموعة 2", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var ids = await db.ProductGroups.OrderBy(x => x.ProductGroupId).Select(x => x.ProductGroupId).ToListAsync();

        var lookupCache = new Mock<ILookupCacheService>(MockBehavior.Strict);
        lookupCache.Setup(x => x.ClearProductGroupsCache());

        var controller = CreateController(db, lookupCache);

        var result = await controller.BulkDelete(string.Join(',', ids));

        Assert.IsType<RedirectToActionResult>(result);
        lookupCache.Verify(x => x.ClearProductGroupsCache(), Times.Once);
    }

    [Fact]
    public async Task DeleteAll_ClearsProductGroupsCache_WhenAnyGroupsExist()
    {
        await using var db = CreateDbContext();
        db.ProductGroups.Add(new ProductGroup { Name = "مجموعة موجودة", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var lookupCache = new Mock<ILookupCacheService>(MockBehavior.Strict);
        lookupCache.Setup(x => x.ClearProductGroupsCache());

        var controller = CreateController(db, lookupCache);

        var result = await controller.DeleteAll();

        Assert.IsType<RedirectToActionResult>(result);
        lookupCache.Verify(x => x.ClearProductGroupsCache(), Times.Once);
    }
}