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

public class DistrictsController_CacheInvalidation_Tests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static DistrictsController CreateController(
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

        var controller = new DistrictsController(
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
        var governorate = new Governorate { GovernorateName = "القاهرة" };
        db.Governorates.Add(governorate);
        await db.SaveChangesAsync();

        var lookupCache = new Mock<ILookupCacheService>(MockBehavior.Strict);
        lookupCache.Setup(x => x.ClearAllGeographyCaches());

        var controller = CreateController(db, lookupCache);

        var result = await controller.Create(new District
        {
            DistrictName = "مدينة نصر",
            GovernorateId = governorate.GovernorateId,
            DistrictType = 0,
            IsActive = true
        });

        Assert.IsType<RedirectToActionResult>(result);
        lookupCache.Verify(x => x.ClearAllGeographyCaches(), Times.Once);
    }

    [Fact]
    public async Task Edit_ClearsAllGeographyCaches_AfterSuccessfulSave()
    {
        await using var db = CreateDbContext();
        var governorate = new Governorate { GovernorateName = "الجيزة" };
        db.Governorates.Add(governorate);
        await db.SaveChangesAsync();

        var district = new District
        {
            DistrictName = "الدقي",
            GovernorateId = governorate.GovernorateId,
            DistrictType = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Districts.Add(district);
        await db.SaveChangesAsync();

        var lookupCache = new Mock<ILookupCacheService>(MockBehavior.Strict);
        lookupCache.Setup(x => x.ClearAllGeographyCaches());

        var controller = CreateController(db, lookupCache);

        var result = await controller.Edit(district.DistrictId, new District
        {
            DistrictId = district.DistrictId,
            DistrictName = "العجوزة",
            GovernorateId = governorate.GovernorateId,
            DistrictType = 1,
            IsActive = false
        });

        Assert.IsType<RedirectToActionResult>(result);
        lookupCache.Verify(x => x.ClearAllGeographyCaches(), Times.Once);
    }

    [Fact]
    public async Task DeleteConfirmed_ClearsAllGeographyCaches_AfterSuccessfulDelete()
    {
        await using var db = CreateDbContext();
        var governorate = new Governorate { GovernorateName = "الإسكندرية" };
        db.Governorates.Add(governorate);
        await db.SaveChangesAsync();

        var district = new District
        {
            DistrictName = "شرق",
            GovernorateId = governorate.GovernorateId,
            DistrictType = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Districts.Add(district);
        await db.SaveChangesAsync();

        var lookupCache = new Mock<ILookupCacheService>(MockBehavior.Strict);
        lookupCache.Setup(x => x.ClearAllGeographyCaches());

        var controller = CreateController(db, lookupCache);

        var result = await controller.DeleteConfirmed(district.DistrictId);

        Assert.IsType<RedirectToActionResult>(result);
        lookupCache.Verify(x => x.ClearAllGeographyCaches(), Times.Once);
    }

    [Fact]
    public async Task DeleteAll_ClearsAllGeographyCaches_WhenItemsExist()
    {
        await using var db = CreateDbContext();
        var governorate = new Governorate { GovernorateName = "البحيرة" };
        db.Governorates.Add(governorate);
        await db.SaveChangesAsync();

        db.Districts.Add(new District
        {
            DistrictName = "دمنهور",
            GovernorateId = governorate.GovernorateId,
            DistrictType = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var lookupCache = new Mock<ILookupCacheService>(MockBehavior.Strict);
        lookupCache.Setup(x => x.ClearAllGeographyCaches());

        var controller = CreateController(db, lookupCache);

        var result = await controller.DeleteAll();

        Assert.IsType<RedirectToActionResult>(result);
        lookupCache.Verify(x => x.ClearAllGeographyCaches(), Times.Once);
    }

    [Fact]
    public async Task BulkDelete_ClearsAllGeographyCaches_WhenItemsAreDeleted()
    {
        await using var db = CreateDbContext();
        var governorate = new Governorate { GovernorateName = "سوهاج" };
        db.Governorates.Add(governorate);
        await db.SaveChangesAsync();

        db.Districts.AddRange(
            new District
            {
                DistrictName = "أول",
                GovernorateId = governorate.GovernorateId,
                DistrictType = 0,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new District
            {
                DistrictName = "ثان",
                GovernorateId = governorate.GovernorateId,
                DistrictType = 0,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var ids = await db.Districts.OrderBy(x => x.DistrictId).Select(x => x.DistrictId).ToListAsync();

        var lookupCache = new Mock<ILookupCacheService>(MockBehavior.Strict);
        lookupCache.Setup(x => x.ClearAllGeographyCaches());

        var controller = CreateController(db, lookupCache);

        var result = await controller.BulkDelete(string.Join(",", ids));

        Assert.IsType<RedirectToActionResult>(result);
        lookupCache.Verify(x => x.ClearAllGeographyCaches(), Times.Once);
    }
}
