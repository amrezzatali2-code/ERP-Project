using ERP.Controllers;
using ERP.Data;
using ERP.Infrastructure;
using ERP.Models;
using ERP.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace ERP.Tests;

public class CashPayments_Delete_Tests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task DeleteConfirmed_WhenPosted_ReversesAndLogsBeforeRemovingHeader()
    {
        await using var db = CreateDbContext();

        db.CashPayments.Add(new CashPayment
        {
            CashPaymentId = 15,
            PaymentNumber = "15",
            PaymentDate = new DateTime(2026, 4, 6),
            Amount = 125m,
            IsPosted = true,
            Status = "مغلق",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var ledger = new Mock<ILedgerPostingService>();
        var logger = new Mock<IUserActivityLogger>();
        var permission = new Mock<IPermissionService>();
        var visibility = new Mock<IUserAccountVisibilityService>();

        var http = new DefaultHttpContext();
        var controller = new CashPaymentsController(db, ledger.Object, logger.Object, permission.Object, visibility.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>())
        };

        var result = await controller.DeleteConfirmed(15);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.False(await db.CashPayments.AnyAsync(x => x.CashPaymentId == 15));
        ledger.Verify(x => x.ReverseForHeaderDeleteAsync(LedgerSourceType.Payment, 15, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        logger.Verify(x => x.LogAsync(UserActionType.Delete, "CashPayment", 15, It.Is<string>(s => s.Contains("حذف إذن دفع رقم 15")), It.IsAny<string>(), null), Times.Once);
    }
}
