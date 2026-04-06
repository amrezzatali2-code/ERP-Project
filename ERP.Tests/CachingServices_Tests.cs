using ERP.Data;
using ERP.Models;
using ERP.Services;
using ERP.Services.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;

namespace ERP.Tests;

public class CachingServices_Tests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static IMemoryCache CreateMemoryCache() =>
        new MemoryCache(new MemoryCacheOptions());

    [Fact]
    public async Task ProductCacheService_ReturnsCachedSnapshot_UntilCacheIsCleared()
    {
        await using var db = CreateDbContext();
        using var cache = CreateMemoryCache();

        db.Products.Add(new Product
        {
            ProdName = "صنف 1",
            Company = "شركة 1",
            Barcode = "111",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var service = new ProductCacheService(db, cache);

        var firstRead = await service.GetProductsLookupAsync();
        Assert.Single(firstRead);

        db.Products.Add(new Product
        {
            ProdName = "صنف 2",
            Company = "شركة 2",
            Barcode = "222",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var secondRead = await service.GetProductsLookupAsync();
        Assert.Single(secondRead);

        service.ClearProductsCache();

        var thirdRead = await service.GetProductsLookupAsync();
        Assert.Equal(2, thirdRead.Count);
    }

    [Fact]
    public async Task CustomerCacheService_GetActiveCustomersLookupAsync_ReturnsCachedSnapshot_UntilCacheIsCleared()
    {
        await using var db = CreateDbContext();
        using var cache = CreateMemoryCache();

        db.Customers.Add(new Customer
        {
            CustomerName = "عميل نشط",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.Customers.Add(new Customer
        {
            CustomerName = "عميل غير نشط",
            IsActive = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var visibility = new Mock<IUserAccountVisibilityService>(MockBehavior.Strict);
        var service = new CustomerCacheService(db, cache, visibility.Object);

        var firstRead = await service.GetActiveCustomersLookupAsync();
        Assert.Single(firstRead);
        Assert.Equal("عميل نشط", firstRead[0].CustomerName);

        db.Customers.Add(new Customer
        {
            CustomerName = "عميل نشط 2",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var secondRead = await service.GetActiveCustomersLookupAsync();
        Assert.Single(secondRead);

        service.ClearCustomersCache();

        var thirdRead = await service.GetActiveCustomersLookupAsync();
        Assert.Equal(2, thirdRead.Count);
    }

    [Fact]
    public async Task CustomerCacheService_SearchPartiesAutocompleteAsync_UsesCachedSnapshot_WhenNotRestricted()
    {
        await using var db = CreateDbContext();
        using var cache = CreateMemoryCache();

        db.Customers.AddRange(
            new Customer
            {
                CustomerName = "مؤسسة ألف",
                AccountId = 10,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Customer
            {
                CustomerName = "مؤسسة باء",
                AccountId = 20,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Customer
            {
                CustomerName = "مؤسسة جيم",
                AccountId = null,
                IsActive = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var visibility = new Mock<IUserAccountVisibilityService>(MockBehavior.Strict);
        visibility
            .Setup(x => x.GetVisibilityStateForCurrentUserAsync())
            .ReturnsAsync((new HashSet<int> { 20 }, false));

        var service = new CustomerCacheService(db, cache, visibility.Object);

        var result = await service.SearchPartiesAutocompleteAsync("مؤسسة");

        Assert.Equal(2, result.Count);
        Assert.Contains(result, x => x.Name == "مؤسسة ألف");
        Assert.Contains(result, x => x.Name == "مؤسسة جيم");
        Assert.DoesNotContain(result, x => x.Name == "مؤسسة باء");
    }

    [Fact]
    public async Task CustomerCacheService_SearchPartiesAutocompleteAsync_UsesVisibilityFilteredQuery_WhenRestrictedOnly()
    {
        await using var db = CreateDbContext();
        using var cache = CreateMemoryCache();

        var visible = new Customer
        {
            CustomerName = "طرف ظاهر",
            AccountId = 10,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var hidden = new Customer
        {
            CustomerName = "طرف مخفي",
            AccountId = 20,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Customers.AddRange(visible, hidden);
        await db.SaveChangesAsync();

        var visibility = new Mock<IUserAccountVisibilityService>(MockBehavior.Strict);
        visibility
            .Setup(x => x.GetVisibilityStateForCurrentUserAsync())
            .ReturnsAsync((new HashSet<int>(), true));
        visibility
            .Setup(x => x.ApplyCustomerVisibilityFilterAsync(It.IsAny<IQueryable<Customer>>()))
            .Returns<IQueryable<Customer>>(q => Task.FromResult(q.Where(c => c.CustomerId == visible.CustomerId)));

        var service = new CustomerCacheService(db, cache, visibility.Object);

        var result = await service.SearchPartiesAutocompleteAsync("طرف");

        Assert.Single(result);
        Assert.Equal("طرف ظاهر", result[0].Name);
    }

    [Fact]
    public async Task LookupCacheService_ReturnsCachedGovernorates_UntilCacheIsCleared()
    {
        await using var db = CreateDbContext();
        using var cache = CreateMemoryCache();

        db.Governorates.Add(new Governorate { GovernorateName = "القاهرة" });
        await db.SaveChangesAsync();

        var service = new LookupCacheService(db, cache);

        var firstRead = await service.GetGovernoratesAsync();
        Assert.Single(firstRead);

        db.Governorates.Add(new Governorate { GovernorateName = "الجيزة" });
        await db.SaveChangesAsync();

        var secondRead = await service.GetGovernoratesAsync();
        Assert.Single(secondRead);

        service.ClearGovernoratesCache();

        var thirdRead = await service.GetGovernoratesAsync();
        Assert.Equal(2, thirdRead.Count);
    }

    [Fact]
    public async Task LookupCacheService_ClearAllGeographyCaches_ClearsGovernoratesDistrictsAndAreas()
    {
        await using var db = CreateDbContext();
        using var cache = CreateMemoryCache();

        var governorate = new Governorate { GovernorateName = "الإسكندرية" };
        db.Governorates.Add(governorate);
        await db.SaveChangesAsync();

        db.Districts.Add(new District
        {
            DistrictName = "شرق",
            GovernorateId = governorate.GovernorateId,
            IsActive = true
        });
        db.Areas.Add(new Area
        {
            AreaName = "سموحة",
            GovernorateId = governorate.GovernorateId,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var service = new LookupCacheService(db, cache);

        Assert.Single(await service.GetGovernoratesAsync());
        Assert.Single(await service.GetDistrictsAsync());
        Assert.Single(await service.GetAreasAsync());

        db.Governorates.Add(new Governorate { GovernorateName = "البحيرة" });
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
            IsActive = true
        });
        await db.SaveChangesAsync();

        Assert.Single(await service.GetGovernoratesAsync());
        Assert.Single(await service.GetDistrictsAsync());
        Assert.Single(await service.GetAreasAsync());

        service.ClearAllGeographyCaches();

        Assert.Equal(2, (await service.GetGovernoratesAsync()).Count);
        Assert.Equal(2, (await service.GetDistrictsAsync()).Count);
        Assert.Equal(2, (await service.GetAreasAsync()).Count);
    }

    [Fact]
    public async Task LookupCacheService_ReturnsCachedWarehousesWithBranch_UntilCacheIsCleared()
    {
        await using var db = CreateDbContext();
        using var cache = CreateMemoryCache();

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

        var service = new LookupCacheService(db, cache);

        var firstRead = await service.GetWarehousesAsync();
        Assert.Single(firstRead);
        Assert.NotNull(firstRead[0].Branch);

        db.Warehouses.Add(new Warehouse
        {
            WarehouseName = "المخزن ب",
            BranchId = branch.BranchId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var secondRead = await service.GetWarehousesAsync();
        Assert.Single(secondRead);

        service.ClearWarehousesCache();

        var thirdRead = await service.GetWarehousesAsync();
        Assert.Equal(2, thirdRead.Count);
    }
    [Fact]
    public async Task LookupCacheService_ReturnsCachedPolicies_UntilCacheIsCleared()
    {
        await using var db = CreateDbContext();
        using var cache = CreateMemoryCache();

        db.Policies.Add(new Policy
        {
            Name = "سياسة أ",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new LookupCacheService(db, cache);

        var firstRead = await service.GetPoliciesAsync();
        Assert.Single(firstRead);

        db.Policies.Add(new Policy
        {
            Name = "سياسة ب",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var secondRead = await service.GetPoliciesAsync();
        Assert.Single(secondRead);

        service.ClearPoliciesCache();

        var thirdRead = await service.GetPoliciesAsync();
        Assert.Equal(2, thirdRead.Count);
    }

    [Fact]
    public async Task LookupCacheService_ReturnsCachedProductGroups_UntilCacheIsCleared()
    {
        await using var db = CreateDbContext();
        using var cache = CreateMemoryCache();

        db.ProductGroups.Add(new ProductGroup
        {
            Name = "مجموعة أ",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new LookupCacheService(db, cache);

        var firstRead = await service.GetProductGroupsAsync();
        Assert.Single(firstRead);

        db.ProductGroups.Add(new ProductGroup
        {
            Name = "مجموعة ب",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var secondRead = await service.GetProductGroupsAsync();
        Assert.Single(secondRead);

        service.ClearProductGroupsCache();

        var thirdRead = await service.GetProductGroupsAsync();
        Assert.Equal(2, thirdRead.Count);
    }
}