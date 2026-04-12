using ERP.Controllers;
using ERP.Data;
using ERP.Infrastructure;
using ERP.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Tests;

public class StockLedgerController_Search_Tests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        var branch = new Branch
        {
            BranchName = "القاهرة",
            CreatedAt = DateTime.UtcNow
        };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        var warehouse = new Warehouse
        {
            WarehouseName = "المخزن الرئيسي",
            BranchId = branch.BranchId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Warehouses.Add(warehouse);

        var productA = new Product
        {
            ProdName = "باراسيتامول 500",
            WarehouseId = warehouse.WarehouseId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var productB = new Product
        {
            ProdName = "أموكسيسيلين",
            WarehouseId = warehouse.WarehouseId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Products.AddRange(productA, productB);
        await db.SaveChangesAsync();

        db.StockLedger.AddRange(
            new StockLedger
            {
                EntryId = 12,
                TranDate = new DateTime(2026, 4, 4, 9, 0, 0),
                WarehouseId = warehouse.WarehouseId,
                ProdId = productA.ProdId,
                QtyIn = 5,
                QtyOut = 0,
                UnitCost = 10,
                SourceType = "Purchase",
                SourceId = 100,
                SourceLine = 1,
                CreatedAt = DateTime.UtcNow
            },
            new StockLedger
            {
                EntryId = 123,
                TranDate = new DateTime(2026, 4, 4, 10, 0, 0),
                WarehouseId = warehouse.WarehouseId,
                ProdId = productB.ProdId,
                QtyIn = 7,
                QtyOut = 0,
                UnitCost = 11,
                SourceType = "Purchase",
                SourceId = 101,
                SourceLine = 1,
                CreatedAt = DateTime.UtcNow
            });

        await db.SaveChangesAsync();
    }

    private static StockLedgerController CreateController(AppDbContext db)
    {
        var controller = new StockLedgerController(db);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    [Fact]
    public async Task Index_SearchByEntry_TreatsNumericSearchAsExactMatchEvenWithArabicDigits()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);

        var controller = CreateController(db);

        var result = Assert.IsType<ViewResult>(await controller.Index(search: "١٢", searchBy: "entry"));
        var model = Assert.IsType<PagedResult<StockLedger>>(result.Model);

        Assert.Single(model.Items);
        Assert.Equal(12, model.Items[0].EntryId);
    }

    [Fact]
    public async Task Index_SearchByProductName_TreatsTextSearchAsContainsMatch()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);

        var controller = CreateController(db);

        var result = Assert.IsType<ViewResult>(await controller.Index(search: "سيتامول", searchBy: "productname"));
        var model = Assert.IsType<PagedResult<StockLedger>>(result.Model);

        Assert.Single(model.Items);
        Assert.Equal(12, model.Items[0].EntryId);
        Assert.Equal("باراسيتامول 500", model.Items[0].Product?.ProdName);
    }

    [Fact]
    public async Task Index_SearchByProductName_StartsWith_DoesNotMatchMiddleSubstring()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);

        var controller = CreateController(db);

        var result = Assert.IsType<ViewResult>(await controller.Index(search: "سيتامول", searchBy: "productname", searchMode: "starts"));
        var model = Assert.IsType<PagedResult<StockLedger>>(result.Model);

        Assert.Empty(model.Items);
    }

    [Fact]
    public async Task Index_SearchByProductName_StartsWith_MatchesPrefix()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);

        var controller = CreateController(db);

        var result = Assert.IsType<ViewResult>(await controller.Index(search: "بار", searchBy: "productname", searchMode: "starts"));
        var model = Assert.IsType<PagedResult<StockLedger>>(result.Model);

        Assert.Single(model.Items);
        Assert.Equal(12, model.Items[0].EntryId);
    }

    [Fact]
    public async Task Index_SearchByProductName_EndsWith_MatchesSuffix()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);

        var controller = CreateController(db);

        var result = Assert.IsType<ViewResult>(await controller.Index(search: "سيلين", searchBy: "productname", searchMode: "ends"));
        var model = Assert.IsType<PagedResult<StockLedger>>(result.Model);

        Assert.Single(model.Items);
        Assert.Equal(123, model.Items[0].EntryId);
        Assert.Equal("أموكسيسيلين", model.Items[0].Product?.ProdName);
    }

    [Fact]
    public async Task Export_Csv_UsesUnifiedStockLedgerFileName()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);

        var controller = CreateController(db);

        var result = Assert.IsType<FileContentResult>(await controller.Export(search: null, format: "csv"));

        Assert.Equal("text/csv; charset=utf-8", result.ContentType);
        Assert.StartsWith("سجل الحركات_", result.FileDownloadName);
        Assert.EndsWith(".csv", result.FileDownloadName);
    }
}
