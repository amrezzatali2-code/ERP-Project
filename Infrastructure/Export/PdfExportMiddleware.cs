using System.Text;
using Microsoft.AspNetCore.Http.Extensions;

namespace ERP.Infrastructure.Export;

public class PdfExportMiddleware
{
    private const string PdfFlagKey = "__erp_pdf_export_requested";
    private const string PdfInlineFlagKey = "__erp_pdf_export_inline";
    private readonly RequestDelegate _next;

    public PdfExportMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var wantsPdf = string.Equals(context.Request.Query["format"], "pdf", StringComparison.OrdinalIgnoreCase);
        if (!wantsPdf)
        {
            await _next(context);
            return;
        }
        var preferInline = string.Equals(context.Request.Query["pdfInline"], "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.Request.Query["pdfInline"], "true", StringComparison.OrdinalIgnoreCase);

        var originalQuery = context.Request.QueryString;
        context.Items[PdfFlagKey] = true;
        context.Items[PdfInlineFlagKey] = preferInline;
        context.Request.QueryString = ReplaceFormatQueryString(context.Request.Query, "csv");

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context);

            buffer.Position = 0;
            if (!ShouldConvert(context.Response))
            {
                await buffer.CopyToAsync(originalBody);
                return;
            }

            var csvBytes = buffer.ToArray();
            var csvText = DecodeCsv(csvBytes);
            var fileName = ResolvePdfFileName(context.Response.Headers.ContentDisposition);
            var title = Path.GetFileNameWithoutExtension(fileName);
            var pdfBytes = CsvPdfExportHelper.GeneratePdf(csvText, title);
            var inline = context.Items.TryGetValue(PdfInlineFlagKey, out var inlineObj) && inlineObj is bool b && b;

            context.Response.Body = originalBody;
            context.Response.ContentType = "application/pdf";
            context.Response.ContentLength = pdfBytes.Length;
            context.Response.Headers.ContentDisposition = BuildContentDispositionHeader(fileName, inline);
            await context.Response.Body.WriteAsync(pdfBytes, 0, pdfBytes.Length);
        }
        finally
        {
            context.Response.Body = originalBody;
            context.Request.QueryString = originalQuery;
            context.Items.Remove(PdfFlagKey);
            context.Items.Remove(PdfInlineFlagKey);
        }
    }

    private static bool ShouldConvert(HttpResponse response)
    {
        if (response.StatusCode < 200 || response.StatusCode >= 300)
            return false;

        return response.ContentType?.StartsWith("text/csv", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static QueryString ReplaceFormatQueryString(IQueryCollection query, string formatValue)
    {
        var builder = new QueryBuilder();
        foreach (var pair in query)
        {
            if (string.Equals(pair.Key, "format", StringComparison.OrdinalIgnoreCase))
            {
                builder.Add(pair.Key, formatValue);
                continue;
            }

            foreach (var value in pair.Value)
                builder.Add(pair.Key, value ?? string.Empty);
        }

        if (!query.ContainsKey("format"))
            builder.Add("format", formatValue);

        return builder.ToQueryString();
    }

    private static string DecodeCsv(byte[] csvBytes)
    {
        if (csvBytes.Length >= 3 &&
            csvBytes[0] == 0xEF &&
            csvBytes[1] == 0xBB &&
            csvBytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(csvBytes, 3, csvBytes.Length - 3);
        }

        return Encoding.UTF8.GetString(csvBytes);
    }

    private static string ResolvePdfFileName(string? contentDisposition)
    {
        var fallback = $"تصدير_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        if (string.IsNullOrWhiteSpace(contentDisposition))
            return fallback;

        string? fileName = null;

        var parts = contentDisposition.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (part.StartsWith("filename*=", StringComparison.OrdinalIgnoreCase))
            {
                var raw = part.Substring("filename*=".Length);
                var idx = raw.IndexOf("''", StringComparison.Ordinal);
                fileName = idx >= 0 ? Uri.UnescapeDataString(raw[(idx + 2)..].Trim('"')) : raw.Trim('"');
                break;
            }

            if (part.StartsWith("filename=", StringComparison.OrdinalIgnoreCase))
            {
                fileName = part.Substring("filename=".Length).Trim('"');
            }
        }

        if (string.IsNullOrWhiteSpace(fileName))
            return fallback;

        if (fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return fileName[..^4] + ".pdf";

        return Path.ChangeExtension(fileName, ".pdf");
    }

    private static string BuildContentDispositionHeader(string fileName, bool inline = false)
    {
        var safeFileName = string.IsNullOrWhiteSpace(fileName)
            ? "export.pdf"
            : fileName;

        var asciiFileName = BuildAsciiFallbackFileName(safeFileName);
        var encodedFileName = Uri.EscapeDataString(safeFileName);
        var mode = inline ? "inline" : "attachment";
        return $"{mode}; filename=\"{asciiFileName}\"; filename*=UTF-8''{encodedFileName}";
    }

    private static string BuildAsciiFallbackFileName(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".pdf";

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var normalized = baseName.Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            if (ch <= 127 && (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.'))
            {
                sb.Append(ch);
            }
            else if (ch <= 127 && char.IsWhiteSpace(ch))
            {
                sb.Append('_');
            }
        }

        var asciiBase = sb.ToString().Trim('_', '.', ' ');
        if (string.IsNullOrWhiteSpace(asciiBase))
            asciiBase = "export";

        return asciiBase + extension;
    }
}
