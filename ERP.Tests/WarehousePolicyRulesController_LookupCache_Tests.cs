using ERP.Controllers;
using ERP.Data;
using ERP.Models;
using ERP.Services.Caching;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ERP.Tests;

public class WarehousePolicyRulesController_LookupCache_Tests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static WarehousePolicyRulesController CreateController(AppDbContext db, ILookupCacheService lookupCache) =>
        new(db, lookupCache);

    private static string[] ReadSelectTexts(object? items)
    {
        var list = Assert.IsAssignableFrom<IEnumerable<SelectListItem>>(items);
        return list.Select(x => x.Text ?? string.Empty).ToArray();
    }

    [Fact]
    public async Task Create_PopulatesWarehousesFromCacheUntilItIsCleared()
    {
        await using var db = CreateDbContext();
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());

        var branch = new Branch { BranchName = "الفرع الرئيسي" };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        db.Warehouses.Add(new Warehouse
        {
            WarehouseName = "مخزن أ",
            BranchId = branch.BranchId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var lookupCache = new LookupCacheService(db, memoryCache);
        var controller = CreateController(db, lookupCache);

        var firstResult = Assert.IsType<ViewResult>(await controller.Create());
        Assert.IsType<WarehousePolicyRule>(firstResult.Model);
        Assert.Single(ReadSelectTexts(controller.ViewBag.WarehouseList));
        Assert.Empty(ReadSelectTexts(controller.ViewBag.PolicyList));

        db.Warehouses.Add(new Warehouse
        {
            WarehouseName = "مخزن ب",
            BranchId = branch.BranchId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await controller.Create();
        Assert.Single(ReadSelectTexts(controller.ViewBag.WarehouseList));

        lookupCache.ClearWarehousesCache();

        await controller.Create();
        Assert.Equal(2, ReadSelectTexts(controller.ViewBag.WarehouseList).Length);
    }

    [Fact]
    public async Task Edit_PopulatesPoliciesAndWarehousesFromCacheUntilItIsCleared()
    {
        await using var db = CreateDbContext();
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());

        var branch = new Branch { BranchName = "فرع الاختبار" };
        var policyA = new Policy
        {
            Name = "سياسة 1",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Branches.Add(branch);
        db.Policies.Add(policyA);
        await db.SaveChangesAsync();

        var warehouseA = new Warehouse
        {
            WarehouseName = "مخزن 1",
            BranchId = branch.BranchId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Warehouses.Add(warehouseA);
        await db.SaveChangesAsync();

        var rule = new WarehousePolicyRule
        {
            WarehouseId = warehouseA.WarehouseId,
            PolicyId = policyA.PolicyId,
            ProfitPercent = 2m,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.WarehousePolicyRules.Add(rule);
        await db.SaveChangesAsync();

        var lookupCache = new LookupCacheService(db, memoryCache);
        var controller = CreateController(db, lookupCache);

        var firstResult = Assert.IsType<ViewResult>(await controller.Edit(rule.Id));
        Assert.IsType<WarehousePolicyRule>(firstResult.Model);
        Assert.Single(ReadSelectTexts(controller.ViewBag.WarehouseList));
        Assert.Single(ReadSelectTexts(controller.ViewBag.PolicyList));

        db.Policies.Add(new Policy
        {
            Name = "سياسة 2",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        db.Warehouses.Add(new Warehouse
        {
            WarehouseName = "مخزن 2",
            BranchId = branch.BranchId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await controller.Edit(rule.Id);
        Assert.Single(ReadSelectTexts(controller.ViewBag.WarehouseList));
        Assert.Single(ReadSelectTexts(controller.ViewBag.PolicyList));

        lookupCache.ClearPoliciesCache();
        lookupCache.ClearWarehousesCache();

        await controller.Edit(rule.Id);
        Assert.Equal(2, ReadSelectTexts(controller.ViewBag.WarehouseList).Length);
        Assert.Equal(2, ReadSelectTexts(controller.ViewBag.PolicyList).Length);
    }
}