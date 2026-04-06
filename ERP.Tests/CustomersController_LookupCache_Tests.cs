using System.Text.Json;
using ERP.Controllers;
using ERP.Data;
using ERP.Infrastructure;
using ERP.Models;
using ERP.Services;
using ERP.Services.Caching;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;

namespace ERP.Tests;

public class CustomersController_LookupCache_Tests
{
    private sealed class LookupItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static CustomersController CreateController(AppDbContext db, ILookupCacheService lookupCache)
    {
        var accountVisibility = new Mock<IUserAccountVisibilityService>(MockBehavior.Strict);
        accountVisibility
            .Setup(x => x.GetHiddenAccountIdsForCurrentUserAsync())
            .ReturnsAsync(new HashSet<int>());

        return new CustomersController(
            db,
            new Mock<IUserActivityLogger>().Object,
            new Mock<IPermissionService>().Object,
            accountVisibility.Object,
            new Mock<ICustomerCacheService>().Object,
            lookupCache);
    }

    private static string[] ReadSelectTexts(object? selectListObject)
    {
        var selectList = Assert.IsType<SelectList>(selectListObject);
        return selectList
            .Cast<SelectListItem>()
            .Select(x => x.Text ?? string.Empty)
            .ToArray();
    }

    private static List<LookupItem> ReadLookupItems(IActionResult actionResult)
    {
        var json = Assert.IsType<JsonResult>(actionResult);
        var serialized = JsonSerializer.Serialize(json.Value);

        return JsonSerializer.Deserialize<List<LookupItem>>(
                   serialized,
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? new List<LookupItem>();
    }

    [Fact]
    public async Task GetDistrictsByGovernorate_ReturnsOnlyMatchingDistrictsOrderedByName()
    {
        await using var db = CreateDbContext();
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());

        var cairo = new Governorate { GovernorateName = "القاهرة" };
        var giza = new Governorate { GovernorateName = "الجيزة" };
        db.Governorates.AddRange(cairo, giza);
        await db.SaveChangesAsync();

        db.Districts.AddRange(
            new District
            {
                DistrictName = "مدينة نصر",
                GovernorateId = cairo.GovernorateId,
                IsActive = true
            },
            new District
            {
                DistrictName = "المعادي",
                GovernorateId = cairo.GovernorateId,
                IsActive = true
            },
            new District
            {
                DistrictName = "الدقي",
                GovernorateId = giza.GovernorateId,
                IsActive = true
            });
        await db.SaveChangesAsync();

        var lookupCache = new LookupCacheService(db, memoryCache);
        var controller = CreateController(db, lookupCache);

        var result = await controller.GetDistrictsByGovernorate(cairo.GovernorateId);
        var items = ReadLookupItems(result);

        Assert.Equal(2, items.Count);
        Assert.Equal(new[] { "المعادي", "مدينة نصر" }, items.Select(x => x.Name).ToArray());
        Assert.DoesNotContain(items, x => x.Name == "الدقي");
    }

    [Fact]
    public async Task GetAreasByDistrict_UsesLookupCacheUntilItIsCleared()
    {
        await using var db = CreateDbContext();
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());

        var governorate = new Governorate { GovernorateName = "الإسكندرية" };
        db.Governorates.Add(governorate);
        await db.SaveChangesAsync();

        var district = new District
        {
            DistrictName = "شرق",
            GovernorateId = governorate.GovernorateId,
            IsActive = true
        };
        db.Districts.Add(district);
        await db.SaveChangesAsync();

        db.Areas.Add(new Area
        {
            AreaName = "سموحة",
            GovernorateId = governorate.GovernorateId,
            DistrictId = district.DistrictId,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var lookupCache = new LookupCacheService(db, memoryCache);
        var controller = CreateController(db, lookupCache);

        var firstItems = ReadLookupItems(await controller.GetAreasByDistrict(district.DistrictId));
        Assert.Single(firstItems);
        Assert.Equal("سموحة", firstItems[0].Name);

        db.Areas.Add(new Area
        {
            AreaName = "لوران",
            GovernorateId = governorate.GovernorateId,
            DistrictId = district.DistrictId,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var secondItems = ReadLookupItems(await controller.GetAreasByDistrict(district.DistrictId));
        Assert.Single(secondItems);

        lookupCache.ClearAreasCache();

        var thirdItems = ReadLookupItems(await controller.GetAreasByDistrict(district.DistrictId));
        Assert.Equal(2, thirdItems.Count);
        Assert.Equal(new[] { "سموحة", "لوران" }, thirdItems.Select(x => x.Name).OrderBy(x => x).ToArray());
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
        var firstGovernorates = ReadSelectTexts(controller.ViewBag.GovernorateId);
        Assert.Single(firstGovernorates);
        Assert.Equal("القاهرة", firstGovernorates[0]);
        Assert.IsType<Customer>(firstResult.Model);

        db.Governorates.Add(new Governorate { GovernorateName = "الجيزة" });
        await db.SaveChangesAsync();

        await controller.Create();
        var secondGovernorates = ReadSelectTexts(controller.ViewBag.GovernorateId);
        Assert.Single(secondGovernorates);

        lookupCache.ClearGovernoratesCache();

        await controller.Create();
        var thirdGovernorates = ReadSelectTexts(controller.ViewBag.GovernorateId);
        Assert.Equal(new[] { "الجيزة", "القاهرة" }, thirdGovernorates);
    }

    [Fact]
    public async Task Edit_PopulatesDistrictsAndAreasFromLookupCacheUntilTheyAreCleared()
    {
        await using var db = CreateDbContext();
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());

        var governorate = new Governorate { GovernorateName = "الإسكندرية" };
        db.Governorates.Add(governorate);
        await db.SaveChangesAsync();

        var district = new District
        {
            DistrictName = "شرق",
            GovernorateId = governorate.GovernorateId,
            IsActive = true
        };
        db.Districts.Add(district);
        await db.SaveChangesAsync();

        var area = new Area
        {
            AreaName = "سموحة",
            GovernorateId = governorate.GovernorateId,
            DistrictId = district.DistrictId,
            IsActive = true
        };
        db.Areas.Add(area);

        var customer = new Customer
        {
            CustomerName = "عميل جغرافي",
            GovernorateId = governorate.GovernorateId,
            DistrictId = district.DistrictId,
            AreaId = area.AreaId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var lookupCache = new LookupCacheService(db, memoryCache);
        var controller = CreateController(db, lookupCache);

        var firstResult = Assert.IsType<ViewResult>(await controller.Edit(customer.CustomerId));
        Assert.IsType<Customer>(firstResult.Model);
        Assert.Single(ReadSelectTexts(controller.ViewBag.DistrictId));
        Assert.Single(ReadSelectTexts(controller.ViewBag.AreaId));

        db.Districts.Add(new District
        {
            DistrictName = "وسط",
            GovernorateId = governorate.GovernorateId,
            IsActive = true
        });
        db.Areas.Add(new Area
        {
            AreaName = "لوران",
            GovernorateId = governorate.GovernorateId,
            DistrictId = district.DistrictId,
            IsActive = true
        });
        await db.SaveChangesAsync();

        await controller.Edit(customer.CustomerId);
        Assert.Single(ReadSelectTexts(controller.ViewBag.DistrictId));
        Assert.Single(ReadSelectTexts(controller.ViewBag.AreaId));

        lookupCache.ClearDistrictsCache();
        lookupCache.ClearAreasCache();

        await controller.Edit(customer.CustomerId);
        Assert.Equal(2, ReadSelectTexts(controller.ViewBag.DistrictId).Length);
        Assert.Equal(2, ReadSelectTexts(controller.ViewBag.AreaId).Length);
    }
}
