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

public class ProductsController_LookupCache_Tests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static ProductsController CreateController(AppDbContext db, ILookupCacheService lookupCache)
    {
        var accountVisibility = new Mock<IUserAccountVisibilityService>(MockBehavior.Strict);
        accountVisibility
            .Setup(x => x.GetHiddenAccountIdsForCurrentUserAsync())
            .ReturnsAsync(new HashSet<int>());

        return new ProductsController(
            db,
            new StockAnalysisService(db),
            new Mock<IUserActivityLogger>().Object,
            new Mock<IPermissionService>().Object,
            accountVisibility.Object,
            new Mock<IProductCacheService>().Object,
            new Mock<ICustomerCacheService>().Object,
            lookupCache);
    }

    private static string[] ReadSelectTexts(object? items)
    {
        var list = Assert.IsAssignableFrom<IEnumerable<SelectListItem>>(items);
        return list.Select(x => x.Text ?? string.Empty).ToArray();
    }

    [Fact]
    public async Task Create_PopulatesWarehousesFromLookupCacheUntilItIsCleared()
    {
        await using var db = CreateDbContext();
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());

        var branch = new Branch { BranchName = "الفرع الرئيسي" };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        db.Warehouses.Add(new Warehouse
        {
            WarehouseName = "المخزن أ",
            BranchId = branch.BranchId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var lookupCache = new LookupCacheService(db, memoryCache);
        var controller = CreateController(db, lookupCache);

        var firstResult = Assert.IsType<ViewResult>(await controller.Create());
        Assert.IsType<Product>(firstResult.Model);
        var firstWarehouses = ReadSelectTexts(controller.ViewBag.Warehouses);
        Assert.Single(firstWarehouses);
        Assert.Equal("المخزن أ", firstWarehouses[0]);

        db.Warehouses.Add(new Warehouse
        {
            WarehouseName = "المخزن ب",
            BranchId = branch.BranchId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await controller.Create();
        var secondWarehouses = ReadSelectTexts(controller.ViewBag.Warehouses);
        Assert.Single(secondWarehouses);

        lookupCache.ClearWarehousesCache();

        await controller.Create();
        var thirdWarehouses = ReadSelectTexts(controller.ViewBag.Warehouses);
        Assert.Equal(new[] { "المخزن أ", "المخزن ب" }, thirdWarehouses);
    }

    [Fact]
    public async Task Edit_PopulatesWarehousesFromLookupCacheUntilItIsCleared()
    {
        await using var db = CreateDbContext();
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());

        var branch = new Branch { BranchName = "فرع الاختبار" };
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

        var product = new Product
        {
            ProdName = "صنف اختبار",
            WarehouseId = warehouseA.WarehouseId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var lookupCache = new LookupCacheService(db, memoryCache);
        var controller = CreateController(db, lookupCache);

        var firstResult = Assert.IsType<ViewResult>(await controller.Edit(product.ProdId));
        Assert.IsType<Product>(firstResult.Model);
        Assert.Single(ReadSelectTexts(controller.ViewBag.Warehouses));

        db.Warehouses.Add(new Warehouse
        {
            WarehouseName = "مخزن 2",
            BranchId = branch.BranchId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await controller.Edit(product.ProdId);
        Assert.Single(ReadSelectTexts(controller.ViewBag.Warehouses));

        lookupCache.ClearWarehousesCache();

        await controller.Edit(product.ProdId);
        Assert.Equal(2, ReadSelectTexts(controller.ViewBag.Warehouses).Length);
    }
}
