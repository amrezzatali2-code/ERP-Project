using System.Text;
using ERP.Controllers;
using ERP.Data;
using ERP.Infrastructure;
using ERP.Models;
using ERP.Services.Caching;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace ERP.Tests;

public class GovernoratesExport_Tests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static GovernoratesController CreateController(AppDbContext db)
    {
        var controller = new GovernoratesController(
            db,
            Mock.Of<IUserActivityLogger>(),
            Mock.Of<ILookupCacheService>());

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    [Fact]
    public async Task Export_AsCsv_WhenNameContainsQuotes_IncludesEscapedDataRow()
    {
        await using var db = CreateDbContext();
        db.Governorates.Add(new Governorate
        {
            GovernorateName = "محافظة \"اختبار\"",
            CreatedAt = new DateTime(2026, 4, 4, 9, 30, 0),
            UpdatedAt = new DateTime(2026, 4, 4, 10, 0, 0)
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db);

        var result = await controller.Export(
            search: null,
            searchBy: null,
            searchMode: null,
            sort: null,
            dir: null,
            format: "csv");

        var file = Assert.IsType<FileContentResult>(result);
        var csv = Encoding.UTF8.GetString(file.FileContents);

        Assert.Contains("كود المحافظة,اسم المحافظة,تاريخ الإنشاء,آخر تعديل", csv);
        Assert.Contains("\"محافظة \"\"اختبار\"\"\"", csv);
    }
}
