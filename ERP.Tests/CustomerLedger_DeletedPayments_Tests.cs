using ERP.Controllers;
using ERP.Data;
using ERP.Infrastructure;
using ERP.Models;
using ERP.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace ERP.Tests;

public class CustomerLedger_DeletedPayments_Tests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task Index_WhenPaymentHeaderWasDeleted_StillShowsReverseEntriesInDetailedLedger()
    {
        await using var db = CreateDbContext();

        var customer = new Customer
        {
            CustomerId = 1,
            CustomerName = "عميل اختبار",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        Assert.True(customer.CustomerId > 0, $"CustomerId={customer.CustomerId}");

        db.Accounts.AddRange(
            new Account
            {
                AccountId = 10,
                AccountCode = "1103",
                AccountName = "حساب العميل",
                AccountType = AccountType.Asset,
                Level = 2,
                IsLeaf = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new Account
            {
                AccountId = 20,
                AccountCode = "1201",
                AccountName = "الخزينة",
                AccountType = AccountType.Asset,
                Level = 2,
                IsLeaf = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        db.LedgerEntries.AddRange(
            new LedgerEntry
            {
                EntryDate = new DateTime(2026, 4, 6),
                SourceType = LedgerSourceType.Payment,
                VoucherNo = "55",
                SourceId = 55,
                LineNo = 1,
                PostVersion = 1,
                AccountId = 10,
                CustomerId = customer.CustomerId,
                Debit = 100m,
                Credit = 0m,
                Description = "ترحيل إذن دفع رقم 55 (مرحلة 1)",
                CreatedAt = DateTime.UtcNow
            },
            new LedgerEntry
            {
                EntryDate = new DateTime(2026, 4, 6),
                SourceType = LedgerSourceType.Payment,
                VoucherNo = "55",
                SourceId = 55,
                LineNo = 2,
                PostVersion = 1,
                AccountId = 20,
                CustomerId = null,
                Debit = 0m,
                Credit = 100m,
                Description = "ترحيل إذن دفع رقم 55 (مرحلة 1)",
                CreatedAt = DateTime.UtcNow
            },
            new LedgerEntry
            {
                EntryDate = new DateTime(2026, 4, 6),
                SourceType = LedgerSourceType.Payment,
                VoucherNo = "55",
                SourceId = 55,
                LineNo = 9001,
                PostVersion = 1,
                AccountId = 10,
                CustomerId = customer.CustomerId,
                Debit = 0m,
                Credit = 100m,
                Description = "عكس ترحيل إذن دفع رقم 55 بسبب الحذف",
                CreatedAt = DateTime.UtcNow
            },
            new LedgerEntry
            {
                EntryDate = new DateTime(2026, 4, 6),
                SourceType = LedgerSourceType.Payment,
                VoucherNo = "55",
                SourceId = 55,
                LineNo = 9002,
                PostVersion = 1,
                AccountId = 20,
                CustomerId = null,
                Debit = 100m,
                Credit = 0m,
                Description = "عكس ترحيل إذن دفع رقم 55 بسبب الحذف",
                CreatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.LedgerEntries.CountAsync(x => x.CustomerId == customer.CustomerId));
        Assert.Equal(1, await db.LedgerEntries.CountAsync(x => x.CustomerId == customer.CustomerId && x.Description != null && x.Description.Contains("عكس ترحيل")));

        var permission = new Mock<IPermissionService>();
        var visibility = new Mock<IUserAccountVisibilityService>();
        visibility.Setup(x => x.ApplyCustomerVisibilityFilterAsync(It.IsAny<IQueryable<Customer>>()))
            .ReturnsAsync((IQueryable<Customer> q) => q);

        var controller = new CustomerLedgerController(db, permission.Object, visibility.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.Index(customer.CustomerId, null, null, pageSize: 50);
        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<PagedResult<LedgerEntry>>(view.Model);

        var snapshot = string.Join(" | ", model.Items.Select(x => $"{x.LineNo}:{x.SourceType}:{x.Description}"));
        Assert.True(model.Items.Any(x => x.LineNo == 9001 && x.SourceType == LedgerSourceType.Payment), $"Items={snapshot}; TotalCount={model.TotalCount}");
        Assert.DoesNotContain(model.Items, x => x.LineNo == 1 && x.SourceType == LedgerSourceType.Payment);
    }
}
