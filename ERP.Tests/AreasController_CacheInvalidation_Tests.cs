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

public class AreasController_CacheInvalidation_Tests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static AreasController CreateController(
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

        var controller = new AreasController(
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

        var district = new District
        {
            DistrictName = "المعادي",
            GovernorateId = governorate.GovernorateId,
            DistrictType = 0,
            IsActive = true
        };
        db.Districts.Add(district);
        await db.SaveChangesAsync();

        var lookupCache = new Mock<ILookupCacheService>(MockBehavior.Strict);
        lookupCache.Setup(x => x.ClearAllGeographyCaches());

        var controller = CreateController(db, lookupCache);

        var result = await controller.Create(new Area
        {
            AreaName = "زهراء المعادي",
            GovernorateId = governorate.GovernorateId,
            DistrictId = district.DistrictId,
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
            IsActive = true
        };
        db.Districts.Add(district);
        await db.SaveChangesAsync();

        var area = new Area
        {
            AreaName = "منطقة قبل التعديل",
            GovernorateId = governorate.GovernorateId,
            DistrictId = district.DistrictId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Areas.Add(area);
        await db.SaveChangesAsync();

        var lookupCache = new Mock<ILookupCacheService>(MockBehavior.Strict);
        lookupCache.Setup(x => x.ClearAllGeographyCaches());

        var controller = CreateController(db, lookupCache);

        var result = await controller.Edit(area.AreaId, new Area
        {
            AreaId = area.AreaId,
            AreaName = "منطقة بعد التعديل",
            GovernorateId = governorate.GovernorateId,
            DistrictId = district.DistrictId,
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
            IsActive = true
        };
        db.Districts.Add(district);
        await db.SaveChangesAsync();

        var area = new Area
        {
            AreaName = "لوران",
            GovernorateId = governorate.GovernorateId,
            DistrictId = district.DistrictId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Areas.Add(area);
        await db.SaveChangesAsync();

        var lookupCache = new Mock<ILookupCacheService>(MockBehavior.Strict);
        lookupCache.Setup(x => x.ClearAllGeographyCaches());

        var controller = CreateController(db, lookupCache);

        var result = await controller.DeleteConfirmed(area.AreaId);

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

        var district = new District
        {
            DistrictName = "دمنهور",
            GovernorateId = governorate.GovernorateId,
            DistrictType = 0,
            IsActive = true
        };
        db.Districts.Add(district);
        await db.SaveChangesAsync();

        db.Areas.Add(new Area
        {
            AreaName = "حي قائم",
            GovernorateId = governorate.GovernorateId,
            DistrictId = district.DistrictId,
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
        var governorate = new Governorate { GovernorateName = "أسيوط" };
        db.Governorates.Add(governorate);
        await db.SaveChangesAsync();

        var district = new District
        {
            DistrictName = "أول",
            GovernorateId = governorate.GovernorateId,
            DistrictType = 0,
            IsActive = true
        };
        db.Districts.Add(district);
        await db.SaveChangesAsync();

        db.Areas.AddRange(
            new Area
            {
                AreaName = "منطقة 1",
                GovernorateId = governorate.GovernorateId,
                DistrictId = district.DistrictId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Area
            {
                AreaName = "منطقة 2",
                GovernorateId = governorate.GovernorateId,
                DistrictId = district.DistrictId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var ids = await db.Areas.OrderBy(x => x.AreaId).Select(x => x.AreaId).ToListAsync();

        var lookupCache = new Mock<ILookupCacheService>(MockBehavior.Strict);
        lookupCache.Setup(x => x.ClearAllGeographyCaches());

        var controller = CreateController(db, lookupCache);

        var result = await controller.BulkDelete(string.Join(",", ids));

        Assert.IsType<RedirectToActionResult>(result);
        lookupCache.Verify(x => x.ClearAllGeographyCaches(), Times.Once);
    }
}
