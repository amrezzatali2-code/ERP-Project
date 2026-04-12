using System.Collections.Generic;
using System.IO;
using System.Text;
using ClosedXML.Excel;
using ERP.Infrastructure;

namespace ERP.Controllers;

/// <summary>تصدير قائمة مختصرة للصيدلية: اسم الصنف + سعر الجمهور + خصم بيع حسب سياسة.</summary>
internal static class ProductBalancesDelegateListExportHelper
{
    public static byte[] BuildExcel(IReadOnlyList<(string Name, decimal Price, decimal Discount)> rows, string policyTitle)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("قائمة صيدلية"));
        var row = 1;
        ws.Cell(row, 1).Value = "اسم الصنف";
        ws.Cell(row, 2).Value = "سعر الجمهور";
        ws.Cell(row, 3).Value = "خصم بيع (" + policyTitle + ") %";
        ws.Range(row, 1, row, 3).Style.Font.Bold = true;
        row = 2;
        foreach (var r in rows)
        {
            ws.Cell(row, 1).Value = r.Name;
            ws.Cell(row, 2).Value = r.Price;
            ws.Cell(row, 3).Value = r.Discount;
            row++;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public static byte[] BuildCsv(IReadOnlyList<(string Name, decimal Price, decimal Discount)> rows, string policyTitle)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",",
            ProductBalancesExportHelper.CsvEscape("اسم الصنف"),
            ProductBalancesExportHelper.CsvEscape("سعر الجمهور"),
            ProductBalancesExportHelper.CsvEscape("خصم بيع (" + policyTitle + ") %")));
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",",
                ProductBalancesExportHelper.CsvEscape(r.Name),
                r.Price.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                r.Discount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)));
        }
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }
}
