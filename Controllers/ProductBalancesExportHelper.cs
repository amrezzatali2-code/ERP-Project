using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ClosedXML.Excel;
using ERP.Infrastructure;
using ERP.ViewModels;

namespace ERP.Controllers;

/// <summary>
/// تصدير أرصدة الأصناف: أعمدة ديناميكية + ترتيب يطابق الأعمدة الظاهرة في الواجهة.
/// </summary>
internal static class ProductBalancesExportHelper
{
    internal static readonly string[] KnownColumnKeys =
        new[]
        {
            "code", "name", "batch", "date", "category", "productGroup", "bonusGroup",
            "company", "imported", "description", "qty", "discount", "manualDiscount",
            "effective", "sales", "priceRetail", "unitCost", "cost"
        }.Concat(Enumerable.Range(1, 10).Select(i => "policy" + i)).ToArray();

    private static readonly string[] DefaultColumnKeysWhenNoVisibleCols =
    {
        "code", "name", "category", "productGroup", "bonusGroup", "qty", "discount",
        "manualDiscount", "effective", "sales", "priceRetail", "unitCost", "cost"
    };

    public static List<int> ParseExportProductIds(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return new List<int>();
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();
    }

    public static List<int> FilterAndOrderProductIds(List<int> productIds, List<int> exportIds)
    {
        if (exportIds.Count == 0) return productIds;
        var exportSet = new HashSet<int>(exportIds);
        var rank = exportIds.Select((id, idx) => (id, idx)).ToDictionary(x => x.id, x => x.idx);
        return productIds.Where(id => exportSet.Contains(id)).OrderBy(id => rank[id]).ToList();
    }

    public static List<string> ResolveExportColumnKeys(string? visibleCols)
    {
        var parts = (visibleCols ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim().ToLowerInvariant())
            .Where(x => x.Length > 0)
            .ToList();
        var known = new HashSet<string>(KnownColumnKeys, StringComparer.OrdinalIgnoreCase);
        if (parts.Count == 0)
            return DefaultColumnKeysWhenNoVisibleCols.ToList();

        var list = new List<string>();
        foreach (var p in parts)
        {
            var key = KnownColumnKeys.FirstOrDefault(k => string.Equals(k, p, StringComparison.OrdinalIgnoreCase));
            if (key == null || list.Exists(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase))) continue;
            list.Add(key);
        }

        return list.Count > 0 ? list : DefaultColumnKeysWhenNoVisibleCols.ToList();
    }

    internal static bool TryParsePolicyColumnKey(string keyLower, out int indexZeroBased)
    {
        indexZeroBased = -1;
        if (string.IsNullOrEmpty(keyLower) || !keyLower.StartsWith("policy", StringComparison.Ordinal))
            return false;
        var suffix = keyLower["policy".Length..];
        if (!int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid) || pid < 1 || pid > 10)
            return false;
        indexZeroBased = pid - 1;
        return true;
    }

    private static decimal? GetPolicyPct(ProductBalanceReportDto item, int indexZeroBased) =>
        item.PolicySaleDiscountPct != null && indexZeroBased >= 0 && indexZeroBased < item.PolicySaleDiscountPct.Length
            ? item.PolicySaleDiscountPct[indexZeroBased]
            : null;

    private static decimal? GetPolicyPctBatch(ProductBalanceBatchRow batch, int indexZeroBased) =>
        batch.PolicySaleDiscountPct != null && indexZeroBased >= 0 && indexZeroBased < batch.PolicySaleDiscountPct.Length
            ? batch.PolicySaleDiscountPct[indexZeroBased]
            : null;

    public static string HeaderArabic(string key)
    {
        var kl = key.ToLowerInvariant();
        if (TryParsePolicyColumnKey(kl, out var pi))
            return "خصم بيع سياسة " + (pi + 1) + " %";
        return kl switch
    {
        "code" => "الكود",
        "name" => "اسم الصنف",
        "batch" => "التشغيلة",
        "date" => "التاريخ",
        "category" => "الفئة",
        "productgroup" => "مجموعة الصنف",
        "bonusgroup" => "مجموعة البونص",
        "company" => "الشركة",
        "imported" => "محلي/مستورد",
        "description" => "الوصف",
        "qty" => "الكمية الحالية",
        "discount" => "الخصم المرجح %",
        "manualdiscount" => "الخصم اليدوي للبيع %",
        "effective" => "الخصم الفعّال %",
        "sales" => "المبيعات",
        "priceretail" => "سعر الجمهور",
        "unitcost" => "تكلفة العلبة",
        "cost" => "التكلفة الإجمالية",
        _ => key
    };
    }

    private static bool HasMultiBatches(ProductBalanceReportDto item) =>
        item.Batches != null && item.Batches.Count >= 2;

    public static void WriteHeaderRow(IXLWorksheet ws, int row, List<string> keys)
    {
        for (var i = 0; i < keys.Count; i++)
            ws.Cell(row, i + 1).Value = HeaderArabic(keys[i]);
        if (keys.Count > 0)
            ws.Range(row, 1, row, keys.Count).Style.Font.Bold = true;
    }

    public static void WriteMainRow(IXLWorksheet ws, int row, List<string> keys, ProductBalanceReportDto item)
    {
        var hb = HasMultiBatches(item);
        for (var i = 0; i < keys.Count; i++)
        {
            var k = keys[i].ToLowerInvariant();
            var cell = ws.Cell(row, i + 1);
            switch (k)
            {
                case "code":
                    cell.Value = item.ProdCode;
                    break;
                case "name":
                    cell.Value = item.ProdName ?? "";
                    break;
                case "batch":
                    cell.Value = hb ? "—" : (string.IsNullOrEmpty(item.FirstBatchNo) && !item.FirstBatchExpiry.HasValue ? "—" : (item.FirstBatchNo ?? "—"));
                    break;
                case "date":
                    cell.Value = hb ? "—" : (item.FirstBatchExpiry.HasValue ? item.FirstBatchExpiry.Value.ToString("d/M/yyyy", CultureInfo.InvariantCulture) : "—");
                    break;
                case "category":
                    cell.Value = item.CategoryName ?? "";
                    break;
                case "productgroup":
                    cell.Value = item.ProductGroupName ?? "";
                    break;
                case "bonusgroup":
                    cell.Value = item.ProductBonusGroupName ?? "";
                    break;
                case "company":
                    cell.Value = item.Company ?? "";
                    break;
                case "imported":
                    cell.Value = item.Imported ?? "";
                    break;
                case "description":
                    cell.Value = item.Description ?? "";
                    break;
                case "qty":
                    cell.Value = item.CurrentQty;
                    break;
                case "discount":
                    cell.Value = item.WeightedDiscount;
                    break;
                case "manualdiscount":
                    if (hb) cell.Value = "";
                    else if (item.ManualDiscountPct.HasValue) cell.Value = item.ManualDiscountPct.Value;
                    else cell.Value = "";
                    break;
                case "effective":
                    cell.Value = item.EffectiveDiscountPct;
                    break;
                case "sales":
                    cell.Value = item.SalesQty;
                    break;
                case "priceretail":
                    cell.Value = item.PriceRetail;
                    break;
                case "unitcost":
                    cell.Value = item.UnitCost;
                    break;
                case "cost":
                    cell.Value = item.TotalCost;
                    break;
                default:
                    if (TryParsePolicyColumnKey(k, out var pIdx))
                    {
                        var pv = GetPolicyPct(item, pIdx);
                        if (pv.HasValue) cell.Value = pv.Value;
                        else cell.Value = "";
                    }
                    else cell.Value = "";
                    break;
            }
        }
    }

    public static void WriteBatchRow(IXLWorksheet ws, int row, List<string> keys, ProductBalanceReportDto item, ProductBalanceBatchRow batch)
    {
        for (var i = 0; i < keys.Count; i++)
        {
            var k = keys[i].ToLowerInvariant();
            var cell = ws.Cell(row, i + 1);
            switch (k)
            {
                case "code":
                    cell.Value = "";
                    break;
                case "name":
                    cell.Value = "  └ تشغيلة: " + (batch.BatchNo ?? "-") + (batch.Expiry.HasValue ? " | " + batch.Expiry.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "");
                    break;
                case "batch":
                    cell.Value = batch.BatchNo ?? "—";
                    break;
                case "date":
                    cell.Value = batch.Expiry.HasValue ? batch.Expiry.Value.ToString("d/M/yyyy", CultureInfo.InvariantCulture) : "—";
                    break;
                case "category":
                case "productgroup":
                case "bonusgroup":
                case "company":
                case "imported":
                case "description":
                    cell.Value = "";
                    break;
                case "qty":
                    cell.Value = batch.CurrentQty;
                    break;
                case "discount":
                    cell.Value = batch.WeightedDiscount;
                    break;
                case "manualdiscount":
                    if (batch.ManualDiscountPct.HasValue) cell.Value = batch.ManualDiscountPct.Value;
                    else cell.Value = "";
                    break;
                case "effective":
                    cell.Value = batch.EffectiveDiscountPct;
                    break;
                case "sales":
                    cell.Value = batch.SalesQty;
                    break;
                case "priceretail":
                    cell.Value = batch.PriceRetail;
                    break;
                case "unitcost":
                    cell.Value = batch.UnitCost;
                    break;
                case "cost":
                    cell.Value = batch.TotalCost;
                    break;
                default:
                    if (TryParsePolicyColumnKey(k, out var pIdxB))
                    {
                        var pvB = GetPolicyPctBatch(batch, pIdxB);
                        if (pvB.HasValue) cell.Value = pvB.Value;
                        else cell.Value = "";
                    }
                    else cell.Value = "";
                    break;
            }
        }
    }

    public static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        value = value.Replace("\"", "\"\"");
        return $"\"{value}\"";
    }

    private static string CsvNum(decimal d) => d.ToString("0.00", CultureInfo.InvariantCulture);

    public static string MainCsvCell(string key, ProductBalanceReportDto item)
    {
        var kl = key.ToLowerInvariant();
        if (TryParsePolicyColumnKey(kl, out var pix))
        {
            var v = GetPolicyPct(item, pix);
            return v.HasValue ? CsvNum(v.Value) : CsvEscape("");
        }
        var hb = HasMultiBatches(item);
        return kl switch
        {
            "code" => CsvEscape(item.ProdCode),
            "name" => CsvEscape(item.ProdName ?? ""),
            "batch" => CsvEscape(hb ? "—" : (string.IsNullOrEmpty(item.FirstBatchNo) && !item.FirstBatchExpiry.HasValue ? "—" : (item.FirstBatchNo ?? "—"))),
            "date" => CsvEscape(hb ? "—" : (item.FirstBatchExpiry.HasValue ? item.FirstBatchExpiry.Value.ToString("d/M/yyyy", CultureInfo.InvariantCulture) : "—")),
            "category" => CsvEscape(item.CategoryName ?? ""),
            "productgroup" => CsvEscape(item.ProductGroupName ?? ""),
            "bonusgroup" => CsvEscape(item.ProductBonusGroupName ?? ""),
            "company" => CsvEscape(item.Company ?? ""),
            "imported" => CsvEscape(item.Imported ?? ""),
            "description" => CsvEscape(item.Description ?? ""),
            "qty" => item.CurrentQty.ToString(CultureInfo.InvariantCulture),
            "discount" => CsvNum(item.WeightedDiscount),
            "manualdiscount" => hb ? CsvEscape("") : (item.ManualDiscountPct.HasValue ? CsvNum(item.ManualDiscountPct.Value) : CsvEscape("")),
            "effective" => CsvNum(item.EffectiveDiscountPct),
            "sales" => CsvNum(item.SalesQty),
            "priceretail" => CsvNum(item.PriceRetail),
            "unitcost" => CsvNum(item.UnitCost),
            "cost" => CsvNum(item.TotalCost),
            _ => CsvEscape("")
        };
    }

    public static string BatchCsvCell(string key, ProductBalanceReportDto item, ProductBalanceBatchRow batch)
    {
        var kl = key.ToLowerInvariant();
        if (TryParsePolicyColumnKey(kl, out var pixB))
        {
            var v = GetPolicyPctBatch(batch, pixB);
            return v.HasValue ? CsvNum(v.Value) : CsvEscape("");
        }
        return kl switch
        {
            "code" => CsvEscape(""),
            "name" => CsvEscape("  └ تشغيلة: " + (batch.BatchNo ?? "-") + (batch.Expiry.HasValue ? " | " + batch.Expiry.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "")),
            "batch" => CsvEscape(batch.BatchNo ?? "—"),
            "date" => CsvEscape(batch.Expiry.HasValue ? batch.Expiry.Value.ToString("d/M/yyyy", CultureInfo.InvariantCulture) : "—"),
            "category" or "productgroup" or "bonusgroup" or "company" or "imported" or "description" => CsvEscape(""),
            "qty" => batch.CurrentQty.ToString(CultureInfo.InvariantCulture),
            "discount" => CsvNum(batch.WeightedDiscount),
            "manualdiscount" => batch.ManualDiscountPct.HasValue ? CsvNum(batch.ManualDiscountPct.Value) : CsvEscape(""),
            "effective" => CsvNum(batch.EffectiveDiscountPct),
            "sales" => CsvNum(batch.SalesQty),
            "priceretail" => CsvNum(batch.PriceRetail),
            "unitcost" => CsvNum(batch.UnitCost),
            "cost" => CsvNum(batch.TotalCost),
            _ => CsvEscape("")
        };
    }

    public static byte[] BuildExcelBytes(List<ProductBalanceReportDto> reportData, List<string> keys)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("أرصدة الأصناف"));
        var row = 1;
        WriteHeaderRow(worksheet, row, keys);
        row = 2;
        foreach (var item in reportData)
        {
            WriteMainRow(worksheet, row, keys, item);
            row++;
            if (item.Batches != null && item.Batches.Count >= 2)
            {
                foreach (var batch in item.Batches)
                {
                    WriteBatchRow(worksheet, row, keys, item, batch);
                    row++;
                }
            }
        }

        worksheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static byte[] BuildCsvBytes(List<ProductBalanceReportDto> reportData, List<string> keys)
    {
        var csvB = new StringBuilder();
        csvB.AppendLine(string.Join(",", keys.Select(k => CsvEscape(HeaderArabic(k)))));
        foreach (var item in reportData)
        {
            csvB.AppendLine(string.Join(",", keys.Select(k => MainCsvCell(k, item))));
            if (item.Batches != null && item.Batches.Count >= 2)
            {
                foreach (var batch in item.Batches)
                    csvB.AppendLine(string.Join(",", keys.Select(k => BatchCsvCell(k, item, batch))));
            }
        }

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csvB.ToString())).ToArray();
    }
}
