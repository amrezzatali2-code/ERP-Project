using ERP.Controllers;
using ERP.Data;
using ERP.Infrastructure;
using ERP.Models;
using ERP.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace ERP.Tests;

/// <summary>
/// اختبارات <see cref="SalesInvoicesController.SaveTaxJson"/> بدون تعديل كود الإنتاج.
/// </summary>
public class SalesInvoicesController_SaveTaxJson_Tests
{
    private static SalesInvoicesController CreateController(AppDbContext db)
    {
        var docTotals = new DocumentTotalsService(db, new Mock<ILogger<DocumentTotalsService>>().Object);
        var stock = new StockAnalysisService(db);
        var fifo = new SalesFifoCostRepairService(db);
        var mockActivity = new Mock<IUserActivityLogger>();
        var mockLedger = new Mock<ILedgerPostingService>();
        var mockFullReturn = new Mock<IFullReturnService>();
        var mockPerm = new Mock<IPermissionService>();
        var mockVis = new Mock<IUserAccountVisibilityService>();

        return new SalesInvoicesController(
            db,
            docTotals,
            mockActivity.Object,
            mockLedger.Object,
            stock,
            mockFullReturn.Object,
            mockPerm.Object,
            mockVis.Object,
            fifo);
    }

    [Fact]
    public async Task SaveTaxJson_ReturnsBadRequest_WhenDtoIsInvalid()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new AppDbContext(options);
        var controller = CreateController(db);

        var nullResult = await controller.SaveTaxJson(null!);
        Assert.IsType<BadRequestObjectResult>(nullResult);

        var zeroIdResult = await controller.SaveTaxJson(new SalesInvoicesController.SaveTaxJsonDto
        {
            SIId = 0,
            taxTotal = 0m
        });
        Assert.IsType<BadRequestObjectResult>(zeroIdResult);

        var negativeIdResult = await controller.SaveTaxJson(new SalesInvoicesController.SaveTaxJsonDto
        {
            SIId = -1,
            taxTotal = 0m
        });
        Assert.IsType<BadRequestObjectResult>(negativeIdResult);
    }

    [Fact]
    public async Task SaveTaxJson_ReturnsNotFound_WhenInvoiceDoesNotExist()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new AppDbContext(options);
        var controller = CreateController(db);

        var result = await controller.SaveTaxJson(new SalesInvoicesController.SaveTaxJsonDto
        {
            SIId = 999_999,
            taxTotal = 1m
        });

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task SaveTaxJson_ReturnsBadRequest_WhenInvoiceIsPosted()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new AppDbContext(options);

        var branch = new Branch { BranchName = "فرع اختبار" };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        var warehouse = new Warehouse
        {
            WarehouseName = "مخزن اختبار",
            BranchId = branch.BranchId
        };
        db.Warehouses.Add(warehouse);

        var customer = new Customer
        {
            CustomerName = "عميل اختبار",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var invoice = new SalesInvoice
        {
            CustomerId = customer.CustomerId,
            WarehouseId = warehouse.WarehouseId,
            SIDate = DateTime.UtcNow.Date,
            SITime = TimeSpan.Zero,
            Status = "مسودة",
            CreatedBy = "test",
            CreatedAt = DateTime.UtcNow,
            TotalBeforeDiscount = 0m,
            TotalAfterDiscountBeforeTax = 100m,
            TaxAmount = 0m,
            NetTotal = 100m,
            CustomerBalanceAtSave = 0m,
            IsPosted = true,
            PostedAt = DateTime.UtcNow,
            RowVersion = new byte[8]
        };
        db.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var controller = CreateController(db);

        var result = await controller.SaveTaxJson(new SalesInvoicesController.SaveTaxJsonDto
        {
            SIId = invoice.SIId,
            taxTotal = 5m
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SaveTaxJson_UpdatesTaxAndNetTotal_WhenInvoiceIsValid()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new AppDbContext(options);

        var branch = new Branch { BranchName = "فرع نجاح" };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        var warehouse = new Warehouse
        {
            WarehouseName = "مخزن نجاح",
            BranchId = branch.BranchId
        };
        db.Warehouses.Add(warehouse);

        var customer = new Customer
        {
            CustomerName = "عميل نجاح",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        const decimal totalAfterDiscountBeforeTax = 200m;
        var invoice = new SalesInvoice
        {
            CustomerId = customer.CustomerId,
            WarehouseId = warehouse.WarehouseId,
            SIDate = DateTime.UtcNow.Date,
            SITime = TimeSpan.Zero,
            Status = "مسودة",
            CreatedBy = "test",
            CreatedAt = DateTime.UtcNow,
            TotalBeforeDiscount = 0m,
            TotalAfterDiscountBeforeTax = totalAfterDiscountBeforeTax,
            TaxAmount = 0m,
            NetTotal = totalAfterDiscountBeforeTax,
            CustomerBalanceAtSave = 0m,
            IsPosted = false,
            UpdatedAt = null,
            RowVersion = new byte[8]
        };
        db.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var controller = CreateController(db);

        const decimal taxTotal = 50m;
        var beforeUtc = DateTime.UtcNow.AddSeconds(-2);

        var result = await controller.SaveTaxJson(new SalesInvoicesController.SaveTaxJsonDto
        {
            SIId = invoice.SIId,
            taxTotal = taxTotal
        });

        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json.Value);
        var payloadType = json.Value.GetType();
        Assert.True((bool)payloadType.GetProperty("ok")!.GetValue(json.Value)!);
        var totals = payloadType.GetProperty("totals")!.GetValue(json.Value)!;
        var totalsType = totals.GetType();
        Assert.Equal(taxTotal, (decimal)totalsType.GetProperty("taxAmount")!.GetValue(totals)!);
        var expectedNet = totalAfterDiscountBeforeTax + taxTotal;
        Assert.Equal(expectedNet, (decimal)totalsType.GetProperty("netTotal")!.GetValue(totals)!);

        var reloaded = await db.SalesInvoices.AsNoTracking().FirstAsync(x => x.SIId == invoice.SIId);
        Assert.Equal(Math.Round(taxTotal, 2), reloaded.TaxAmount);
        Assert.Equal(Math.Round(expectedNet, 2), reloaded.NetTotal);
        Assert.NotNull(reloaded.UpdatedAt);
        Assert.True(reloaded.UpdatedAt >= beforeUtc);
    }
}
