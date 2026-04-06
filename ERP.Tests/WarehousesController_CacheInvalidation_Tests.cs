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

public class WarehousesController_CacheInvalidation_Tests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static WarehousesController CreateController(
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

        var controller = new WarehousesController(
            db,
            activityLogger.Object,
            lookupCache.Object);

        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());

        return controller;
    }

    [Fact]
    public async Task Create_ClearsWarehouseCache_AfterSuccessfulSave()
    {
        await using var db = CreateDbContext();
        var lookupCache = new Mock<ILookupCacheService>(MockBehavior.Strict);
        lookupCache.Setup(x => x.ClearWarehousesCache());

        var controller = CreateController(db, lookupCache);

        var result = await controller.Create(new Warehouse
        {
            WarehouseName = "مخزن جديد",
            IsActive = true
        });

        Assert.IsType<RedirectToActionResult>(result);
        lookupCache.Verify(x => x.ClearWarehousesCache(), Times.Once);
    }

    [Fact]
    public async Task Edit_ClearsWarehouseCache_AfterSuccessfulSave()
    {
        await using var db = CreateDbContext();

        var warehouse = new Warehouse
        {
            WarehouseName = "مخزن قبل التعديل",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync();

        var lookupCache = new Mock<ILookupCacheService>(MockBehavior.Strict);
        lookupCache.Setup(x => x.ClearWarehousesCache());

        var controller = CreateController(db, lookupCache);

        var result = await controller.Edit(warehouse.WarehouseId, new Warehouse
        {
            WarehouseId = warehouse.WarehouseId,
            WarehouseName = "مخزن بعد التعديل",
            IsActive = false
        });

        Assert.IsType<RedirectToActionResult>(result);
        lookupCache.Verify(x => x.ClearWarehousesCache(), Times.Once);
    }

    [Fact]
    public async Task DeleteConfirmed_ClearsWarehouseCache_AfterSuccessfulDelete()
    {
        await using var db = CreateDbContext();

        var warehouse = new Warehouse
        {
            WarehouseName = "مخزن للحذف",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync();

        var lookupCache = new Mock<ILookupCacheService>(MockBehavior.Strict);
        lookupCache.Setup(x => x.ClearWarehousesCache());

        var controller = CreateController(db, lookupCache);

        var result = await controller.DeleteConfirmed(warehouse.WarehouseId);

        Assert.IsType<RedirectToActionResult>(result);
        lookupCache.Verify(x => x.ClearWarehousesCache(), Times.Once);
    }

    [Fact]
    public async Task BulkDelete_ClearsWarehouseCache_WhenAnyWarehousesAreDeleted()
    {
        await using var db = CreateDbContext();

        db.Warehouses.AddRange(
            new Warehouse
            {
                WarehouseName = "مخزن 1",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new Warehouse
            {
                WarehouseName = "مخزن 2",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var ids = await db.Warehouses.OrderBy(x => x.WarehouseId).Select(x => x.WarehouseId).ToListAsync();

        var lookupCache = new Mock<ILookupCacheService>(MockBehavior.Strict);
        lookupCache.Setup(x => x.ClearWarehousesCache());

        var controller = CreateController(db, lookupCache);

        var result = await controller.BulkDelete(string.Join(",", ids));

        Assert.IsType<RedirectToActionResult>(result);
        lookupCache.Verify(x => x.ClearWarehousesCache(), Times.Once);
    }

    [Fact]
    public async Task DeleteAll_ClearsWarehouseCache_WhenAnyWarehousesExist()
    {
        await using var db = CreateDbContext();

        db.Warehouses.Add(new Warehouse
        {
            WarehouseName = "مخزن موجود",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var lookupCache = new Mock<ILookupCacheService>(MockBehavior.Strict);
        lookupCache.Setup(x => x.ClearWarehousesCache());

        var controller = CreateController(db, lookupCache);

        var result = await controller.DeleteAll();

        Assert.IsType<RedirectToActionResult>(result);
        lookupCache.Verify(x => x.ClearWarehousesCache(), Times.Once);
    }
}
