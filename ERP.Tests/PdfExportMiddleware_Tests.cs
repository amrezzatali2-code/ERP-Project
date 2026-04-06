using ERP.Infrastructure.Export;
using Microsoft.AspNetCore.Http;
using QuestPDF.Infrastructure;

namespace ERP.Tests;

public class PdfExportMiddleware_Tests
{
    [Fact]
    public async Task InvokeAsync_WhenCsvResponseHasArabicFileName_WritesAsciiSafeContentDispositionWithUtf8FileName()
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var middleware = new PdfExportMiddleware(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/csv; charset=utf-8";
            context.Response.Headers.ContentDisposition =
                "attachment; filename*=UTF-8''%D8%B3%D8%AC%D9%84_%D8%A7%D9%84%D8%AD%D8%B1%D9%83%D8%A7%D8%AA_20260404_120000.csv";

            await context.Response.WriteAsync("رقم القيد,الصنف\n1,باراسيتامول");
        });

        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString("?format=pdf");

        await using var output = new MemoryStream();
        httpContext.Response.Body = output;

        await middleware.InvokeAsync(httpContext);

        Assert.Equal("application/pdf", httpContext.Response.ContentType);

        var contentDisposition = httpContext.Response.Headers.ContentDisposition.ToString();
        Assert.Contains("attachment;", contentDisposition);
        Assert.Contains("filename=\"20260404_120000.pdf\"", contentDisposition);
        Assert.Contains("filename*=UTF-8''", contentDisposition);
        Assert.Contains("%D8%B3%D8%AC%D9%84_%D8%A7%D9%84%D8%AD%D8%B1%D9%83%D8%A7%D8%AA_20260404_120000.pdf", contentDisposition);
        Assert.Contains(".pdf", contentDisposition);

        output.Position = 0;
        Assert.True(output.Length > 0);
    }
}
