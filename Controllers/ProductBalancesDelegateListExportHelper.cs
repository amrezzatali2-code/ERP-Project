using System.Collections.Generic;
using System.IO;
using System.Text;
using ClosedXML.Excel;
using ERP.Infrastructure;

namespace ERP.Controllers;

/// <summary>تصدير قائمة مختصرة للصيدلية: اسم الصنف + سعر الجمهور + خصم بيع حسب سياسة.</summary>
internal static class ProductBalancesDelegateListExportHelper
{
    public static byte[] BuildExcel(IReadOnlyList<(string Name, decimal Price, decimal Discount)> rows, int columnGroups)
    {
        if (columnGroups < 1) columnGroups = 1;
        if (columnGroups > 3) columnGroups = 3;

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("قائمة صيدلية"));
        ws.RightToLeft = false;

        var totalCols = columnGroups * 3;
        for (var g = 0; g < columnGroups; g++)
        {
            var baseCol = totalCols - (g + 1) * 3 + 1; // ابدأ من يمين الصفحة
            ws.Cell(1, baseCol).Value = "الخصم";
            ws.Cell(1, baseCol + 1).Value = "سعر الجمهور";
            ws.Cell(1, baseCol + 2).Value = "اسم الصنف";
            ws.Range(1, baseCol, 1, baseCol + 2).Style.Font.Bold = true;
            ws.Range(1, baseCol, 1, baseCol + 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var g = i % columnGroups;
            var row = (i / columnGroups) + 2;
            var baseCol = totalCols - (g + 1) * 3 + 1; // نفس ترتيب اليمين
            var r = rows[i];
            ws.Cell(row, baseCol).Value = r.Discount;
            ws.Cell(row, baseCol + 1).Value = r.Price;
            ws.Cell(row, baseCol + 2).Value = r.Name;
        }

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public static byte[] BuildCsv(IReadOnlyList<(string Name, decimal Price, decimal Discount)> rows, int columnGroups)
    {
        if (columnGroups < 1) columnGroups = 1;
        if (columnGroups > 3) columnGroups = 3;

        var sb = new StringBuilder();
        var header = new string[columnGroups * 3];
        for (var g = 0; g < columnGroups; g++)
        {
            var baseIdx = (columnGroups - 1 - g) * 3; // ابدأ من اليمين
            header[baseIdx] = ProductBalancesExportHelper.CsvEscape("الخصم");
            header[baseIdx + 1] = ProductBalancesExportHelper.CsvEscape("سعر الجمهور");
            header[baseIdx + 2] = ProductBalancesExportHelper.CsvEscape("اسم الصنف");
        }
        sb.AppendLine(string.Join(",", header));

        var rowCount = rows.Count == 0 ? 0 : (int)Math.Ceiling(rows.Count / (double)columnGroups);
        for (var r = 0; r < rowCount; r++)
        {
            var line = new string[columnGroups * 3];
            for (var g = 0; g < columnGroups; g++)
            {
                var idx = r * columnGroups + g;
                var baseIdx = (columnGroups - 1 - g) * 3; // نفس ترتيب اليمين
                if (idx < rows.Count)
                {
                    var item = rows[idx];
                    line[baseIdx] = item.Discount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                    line[baseIdx + 1] = item.Price.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                    line[baseIdx + 2] = ProductBalancesExportHelper.CsvEscape(item.Name);
                }
                else
                {
                    line[baseIdx] = "";
                    line[baseIdx + 1] = "";
                    line[baseIdx + 2] = ProductBalancesExportHelper.CsvEscape("");
                }
            }
            sb.AppendLine(string.Join(",", line));
        }

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }
}
