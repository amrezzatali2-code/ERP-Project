using System.Text;
using Microsoft.VisualBasic.FileIO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ERP.Infrastructure.Export;

public static class CsvPdfExportHelper
{
    public static byte[] GeneratePdf(string csvContent, string title)
    {
        var rows = ParseCsv(csvContent);
        var headers = rows.Count > 0 ? rows[0] : Array.Empty<string>();
        var body = rows.Count > 1 ? rows.Skip(1).ToList() : new List<string[]>();

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(20);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(9));

                page.Header().Column(column =>
                {
                    column.Item().AlignCenter().Text(title).SemiBold().FontSize(16);
                    column.Item().PaddingTop(4).AlignCenter().Text($"تاريخ التصدير: {DateTime.Now:yyyy-MM-dd HH:mm}");
                });

                page.Content().PaddingTop(12).Element(x => ComposeTable(x, headers, body));
            });
        }).GeneratePdf();
    }

    private static void ComposeTable(IContainer container, string[] headers, IReadOnlyList<string[]> rows)
    {
        container.Table(table =>
        {
            var columnCount = Math.Max(headers.Length, rows.Count == 0 ? 0 : rows.Max(x => x.Length));
            if (columnCount == 0)
            {
                table.ColumnsDefinition(columns => columns.RelativeColumn());
                table.Cell().Element(CellStyle).AlignCenter().Text("لا توجد بيانات");
                return;
            }

            table.ColumnsDefinition(columns =>
            {
                for (var i = 0; i < columnCount; i++)
                    columns.RelativeColumn();
            });

            if (headers.Length > 0)
            {
                table.Header(header =>
                {
                    foreach (var cell in headers)
                    {
                        header.Cell().Element(HeaderCellStyle).AlignCenter().Text(Sanitize(cell));
                    }
                });
            }

            foreach (var row in rows)
            {
                for (var i = 0; i < columnCount; i++)
                {
                    var value = i < row.Length ? row[i] : string.Empty;
                    table.Cell().Element(CellStyle).AlignRight().Text(Sanitize(value));
                }
            }
        });
    }

    private static IContainer HeaderCellStyle(IContainer container)
    {
        return CellStyle(container)
            .Background(Colors.Grey.Lighten3)
            .DefaultTextStyle(x => x.SemiBold());
    }

    private static IContainer CellStyle(IContainer container)
    {
        return container
            .Border(1)
            .BorderColor(Colors.Grey.Lighten1)
            .PaddingVertical(4)
            .PaddingHorizontal(6);
    }

    private static List<string[]> ParseCsv(string csvContent)
    {
        var rows = new List<string[]>();
        using var reader = new StringReader(csvContent);
        using var parser = new TextFieldParser(reader)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");

        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields() ?? Array.Empty<string>();
            rows.Add(fields);
        }

        return rows;
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Replace("\uFEFF", string.Empty).Trim();
    }
}
