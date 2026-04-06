using ERP.Controllers;
using ERP.Data;
using ERP.Models;
using ERP.Services.Caching;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ERP.Tests;

public class ProductGroupPoliciesController_LookupCache_Tests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static ProductGroupPoliciesController CreateController(AppDbContext db, ILookupCacheService lookupCache) =>
        new(db, lookupCache);

    private static string[] ReadSelectTexts(object? items)
    {
        var list = Assert.IsAssignableFrom<IEnumerable<SelectListItem>>(items);
        return list.Select(x => x.Text ?? string.Empty).ToArray();
    }

    [Fact]
    public async Task Create_PopulatesLookupsFromCacheUntilItIsCleared()
    {
        await using var db = CreateDbContext();
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());

        db.ProductGroups.Add(new ProductGroup
        {
            Name = "مجموعة أ",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        db.Policies.Add(new Policy
        {
            Name = "سياسة أ",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

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
        Assert.IsType<ProductGroupPolicy>(firstResult.Model);
        Assert.Single(ReadSelectTexts(controller.ViewBag.ProductGroupList));
        Assert.Single(ReadSelectTexts(controller.ViewBag.PolicyList));
        Assert.Single(ReadSelectTexts(controller.ViewBag.WarehouseList));

        db.ProductGroups.Add(new ProductGroup
        {
            Name = "مجموعة ب",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        db.Policies.Add(new Policy
        {
            Name = "سياسة ب",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        db.Warehouses.Add(new Warehouse
        {
            WarehouseName = "مخزن ب",
            BranchId = branch.BranchId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await controller.Create();
        Assert.Single(ReadSelectTexts(controller.ViewBag.ProductGroupList));
        Assert.Single(ReadSelectTexts(controller.ViewBag.PolicyList));
        Assert.Single(ReadSelectTexts(controller.ViewBag.WarehouseList));

        lookupCache.ClearProductGroupsCache();
        lookupCache.ClearPoliciesCache();
        lookupCache.ClearWarehousesCache();

        await controller.Create();
        Assert.Equal(2, ReadSelectTexts(controller.ViewBag.ProductGroupList).Length);
        Assert.Equal(2, ReadSelectTexts(controller.ViewBag.PolicyList).Length);
        Assert.Equal(2, ReadSelectTexts(controller.ViewBag.WarehouseList).Length);
    }

    [Fact]
    public async Task Edit_PopulatesLookupsFromCacheUntilItIsCleared()
    {
        await using var db = CreateDbContext();
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());

        var groupA = new ProductGroup
        {
            Name = "مجموعة 1",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var policyA = new Policy
        {
            Name = "سياسة 1",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var branch = new Branch { BranchName = "فرع الاختبار" };

        db.ProductGroups.Add(groupA);
        db.Policies.Add(policyA);
        db.Branches.Add(branch);
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

        var entity = new ProductGroupPolicy
        {
            ProductGroupId = groupA.ProductGroupId,
            PolicyId = policyA.PolicyId,
            WarehouseId = warehouseA.WarehouseId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.ProductGroupPolicies.Add(entity);
        await db.SaveChangesAsync();

        var lookupCache = new LookupCacheService(db, memoryCache);
        var controller = CreateController(db, lookupCache);

        var firstResult = Assert.IsType<ViewResult>(await controller.Edit(entity.Id));
        Assert.IsType<ProductGroupPolicy>(firstResult.Model);
        Assert.Single(ReadSelectTexts(controller.ViewBag.ProductGroupList));
        Assert.Single(ReadSelectTexts(controller.ViewBag.PolicyList));
        Assert.Single(ReadSelectTexts(controller.ViewBag.WarehouseList));

        db.ProductGroups.Add(new ProductGroup
        {
            Name = "مجموعة 2",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
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

        await controller.Edit(entity.Id);
        Assert.Single(ReadSelectTexts(controller.ViewBag.ProductGroupList));
        Assert.Single(ReadSelectTexts(controller.ViewBag.PolicyList));
        Assert.Single(ReadSelectTexts(controller.ViewBag.WarehouseList));

        lookupCache.ClearProductGroupsCache();
        lookupCache.ClearPoliciesCache();
        lookupCache.ClearWarehousesCache();

        await controller.Edit(entity.Id);
        Assert.Equal(2, ReadSelectTexts(controller.ViewBag.ProductGroupList).Length);
        Assert.Equal(2, ReadSelectTexts(controller.ViewBag.PolicyList).Length);
        Assert.Equal(2, ReadSelectTexts(controller.ViewBag.WarehouseList).Length);
    }
}