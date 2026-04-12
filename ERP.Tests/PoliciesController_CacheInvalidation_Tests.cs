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

public class PoliciesController_CacheInvalidation_Tests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static PoliciesController CreateController(AppDbContext db, Mock<ILookupCacheService> lookupCache)
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

        var controller = new PoliciesController(db, activityLogger.Object, lookupCache.Object);
        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        return controller;
    }

    [Fact]
    public async Task Create_ClearsPoliciesCache_AfterSuccessfulSave()
    {
        await using var db = CreateDbContext();
        var lookupCache = new Mock<ILookupCacheService>(MockBehavior.Strict);
        lookupCache.Setup(x => x.ClearPoliciesCache());

        var controller = CreateController(db, lookupCache);

        var result = await controller.Create(new Policy { Name = "سياسة جديدة", IsActive = true });

        Assert.IsType<RedirectToActionResult>(result);
        lookupCache.Verify(x => x.ClearPoliciesCache(), Times.Once);
    }

    [Fact]
    public async Task Edit_ClearsPoliciesCache_AfterSuccessfulSave()
    {
        await using var db = CreateDbContext();
        var policy = new Policy { Name = "قبل التعديل", IsActive = true, CreatedAt = DateTime.UtcNow };
        db.Policies.Add(policy);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var lookupCache = new Mock<ILookupCacheService>(MockBehavior.Strict);
        lookupCache.Setup(x => x.ClearPoliciesCache());

        var controller = CreateController(db, lookupCache);

        var result = await controller.Edit(policy.PolicyId, new Policy
        {
            PolicyId = policy.PolicyId,
            Name = "بعد التعديل",
            IsActive = false,
            CreatedAt = policy.CreatedAt
        });

        Assert.IsType<RedirectToActionResult>(result);
        lookupCache.Verify(x => x.ClearPoliciesCache(), Times.Once);
    }

    [Fact]
    public async Task DeleteConfirmed_ClearsPoliciesCache_AfterSuccessfulDelete()
    {
        await using var db = CreateDbContext();
        var policy = new Policy { Name = "للحذف", IsActive = true, CreatedAt = DateTime.UtcNow };
        db.Policies.Add(policy);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var lookupCache = new Mock<ILookupCacheService>(MockBehavior.Strict);
        lookupCache.Setup(x => x.ClearPoliciesCache());

        var controller = CreateController(db, lookupCache);

        var result = await controller.DeleteConfirmed(policy.PolicyId);

        Assert.IsType<RedirectToActionResult>(result);
        lookupCache.Verify(x => x.ClearPoliciesCache(), Times.Once);
    }

    [Fact]
    public async Task BulkDelete_ClearsPoliciesCache_WhenAnyPoliciesAreDeleted()
    {
        await using var db = CreateDbContext();
        db.Policies.AddRange(
            new Policy { Name = "سياسة 1", IsActive = true, CreatedAt = DateTime.UtcNow },
            new Policy { Name = "سياسة 2", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var ids = await db.Policies.OrderBy(x => x.PolicyId).Select(x => x.PolicyId).ToListAsync();

        var lookupCache = new Mock<ILookupCacheService>(MockBehavior.Strict);
        lookupCache.Setup(x => x.ClearPoliciesCache());

        var controller = CreateController(db, lookupCache);

        var result = await controller.BulkDelete(string.Join(',', ids));

        Assert.IsType<RedirectToActionResult>(result);
        lookupCache.Verify(x => x.ClearPoliciesCache(), Times.Once);
    }

    [Fact]
    public async Task DeleteAll_ClearsPoliciesCache_WhenAnyPoliciesExist()
    {
        await using var db = CreateDbContext();
        db.Policies.Add(new Policy { Name = "سياسة موجودة", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var lookupCache = new Mock<ILookupCacheService>(MockBehavior.Strict);
        lookupCache.Setup(x => x.ClearPoliciesCache());

        var controller = CreateController(db, lookupCache);

        var result = await controller.DeleteAll(search: null, searchBy: null, searchMode: null, sort: null, dir: null, fromCode: null, toCode: null);

        Assert.IsType<RedirectToActionResult>(result);
        lookupCache.Verify(x => x.ClearPoliciesCache(), Times.Once);
    }
}