using ERP.Controllers;
using ERP.Data;
using ERP.Infrastructure;
using ERP.Models;
using ERP.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace ERP.Tests;

/// <summary>
/// اختبارات رفض <see cref="SalesInvoicesController.PostInvoice"/> (مسار Ajax) بدون تعديل كود الإنتاج.
/// </summary>
public class SalesInvoicesController_PostInvoice_Tests
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
        var mockListVisibility = new Mock<IListVisibilityService>();
        var mockVis = new Mock<IUserAccountVisibilityService>();

        return new SalesInvoicesController(
            db,
            docTotals,
            mockActivity.Object,
            mockLedger.Object,
            stock,
            mockFullReturn.Object,
            mockPerm.Object,
            mockListVisibility.Object,
            mockVis.Object,
            fifo);
    }

    /// <summary>
    /// يضبط الطلب كـ Ajax كما يفعل المتصفح مع XMLHttpRequest حتى يعيد الأكشن NotFound/BadRequest JSON وليس Redirect.
    /// </summary>
    private static void SetAjaxRequestHeaders(SalesInvoicesController controller)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    [Fact]
    public async Task PostInvoice_ReturnsNotFound_WhenInvoiceDoesNotExist()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new AppDbContext(options);
        var controller = CreateController(db);
        SetAjaxRequestHeaders(controller);

        var result = await controller.PostInvoice(999_999);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task PostInvoice_ReturnsBadRequest_WhenInvoiceAlreadyPosted()
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
        SetAjaxRequestHeaders(controller);

        var result = await controller.PostInvoice(invoice.SIId);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task PostInvoice_ReturnsBadRequest_WhenCustomerIsMissing()
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
        await db.SaveChangesAsync();

        var invoice = new SalesInvoice
        {
            CustomerId = 0,
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
            IsPosted = false,
            RowVersion = new byte[8]
        };
        db.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        SetAjaxRequestHeaders(controller);

        var result = await controller.PostInvoice(invoice.SIId);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task PostInvoice_ReturnsBadRequest_WhenWarehouseIsMissing()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new AppDbContext(options);

        var customer = new Customer
        {
            CustomerName = "عميل اختبار",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var invoice = new SalesInvoice
        {
            CustomerId = customer.CustomerId,
            WarehouseId = 0,
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
            IsPosted = false,
            RowVersion = new byte[8]
        };
        db.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        SetAjaxRequestHeaders(controller);

        var result = await controller.PostInvoice(invoice.SIId);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task PostInvoice_ReturnsBadRequest_WhenCustomerIsInactive()
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
            CustomerName = "عميل غير نشط",
            IsActive = false,
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
            IsPosted = false,
            RowVersion = new byte[8]
        };
        db.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        SetAjaxRequestHeaders(controller);

        var result = await controller.PostInvoice(invoice.SIId);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task PostInvoice_ReturnsBadRequest_WhenCustomerDoesNotExist()
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
        await db.SaveChangesAsync();

        const int missingCustomerId = 9999;

        var invoice = new SalesInvoice
        {
            CustomerId = missingCustomerId,
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
            IsPosted = false,
            RowVersion = new byte[8]
        };
        db.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        SetAjaxRequestHeaders(controller);

        var result = await controller.PostInvoice(invoice.SIId);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task PostInvoice_ReturnsBadRequest_WhenWarehouseDoesNotExist()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new AppDbContext(options);

        var customer = new Customer
        {
            CustomerName = "عميل اختبار",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        const int missingWarehouseId = 9999;

        var invoice = new SalesInvoice
        {
            CustomerId = customer.CustomerId,
            WarehouseId = missingWarehouseId,
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
            IsPosted = false,
            RowVersion = new byte[8]
        };
        db.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var docTotals = new DocumentTotalsService(db, new Mock<ILogger<DocumentTotalsService>>().Object);
        var stock = new StockAnalysisService(db);
        var fifo = new SalesFifoCostRepairService(db);
        var mockActivity = new Mock<IUserActivityLogger>();
        var mockLedger = new Mock<ILedgerPostingService>();
        mockLedger
            .Setup(x => x.PostSalesInvoiceAsync(It.IsAny<int>(), It.IsAny<string>()))
            .Returns((int salesInvoiceId, string? _) =>
            {
                var inv = db.SalesInvoices.AsNoTracking().FirstOrDefault(x => x.SIId == salesInvoiceId);
                if (inv == null || !db.Warehouses.AsNoTracking().Any(w => w.WarehouseId == inv.WarehouseId))
                    throw new Exception("المخزن غير موجود أو غير صالح.");
                return Task.CompletedTask;
            });
        var mockFullReturn = new Mock<IFullReturnService>();
        var mockPerm = new Mock<IPermissionService>();
        var mockListVisibility = new Mock<IListVisibilityService>();
        var mockVis = new Mock<IUserAccountVisibilityService>();
        var controller = new SalesInvoicesController(
            db,
            docTotals,
            mockActivity.Object,
            mockLedger.Object,
            stock,
            mockFullReturn.Object,
            mockPerm.Object,
            mockListVisibility.Object,
            mockVis.Object,
            fifo);
        SetAjaxRequestHeaders(controller);

        var result = await controller.PostInvoice(invoice.SIId);

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
