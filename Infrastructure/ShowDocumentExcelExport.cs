using System.IO;
using System.Linq;
using ClosedXML.Excel;
using ERP.Models;

namespace ERP.Infrastructure;

/// <summary>
/// تصدير مستند واحد (شاشة عرض) إلى Excel بعناوين عربية.
/// </summary>
public static class ShowDocumentExcelExport
{
    private static void BoldHeader(IXLRange range)
    {
        range.Style.Font.Bold = true;
    }

    public static byte[] SalesInvoice(SalesInvoice inv)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("فاتورة مبيعات"));
        ws.RightToLeft = true;
        int r = 1;
        void Kv(string k, string? v)
        {
            ws.Cell(r, 1).Value = k;
            ws.Cell(r, 2).Value = v ?? "";
            r++;
        }
        Kv("رقم الفاتورة", inv.SIId.ToString());
        Kv("تاريخ الفاتورة", inv.SIDate.ToString("yyyy-MM-dd"));
        Kv("العميل", inv.Customer?.CustomerName);
        Kv("كود العميل", inv.CustomerId.ToString());
        Kv("المخزن", inv.Warehouse?.WarehouseName);
        Kv("الحالة", inv.Status);
        Kv("مرحّلة", inv.IsPosted ? "نعم" : "لا");
        Kv("إجمالي قبل الخصم", inv.TotalBeforeDiscount.ToString("0.00"));
        Kv("بعد الخصم قبل الضريبة", inv.TotalAfterDiscountBeforeTax.ToString("0.00"));
        Kv("الضريبة", inv.TaxAmount.ToString("0.00"));
        Kv("الصافي", inv.NetTotal.ToString("0.00"));
        r++;
        string[] hdr =
        {
            "رقم السطر", "كود الصنف", "اسم الصنف", "الكمية", "سعر الجمهور", "خصم1 %", "خصم2 %", "خصم3 %",
            "قيمة الخصم", "سعر البيع للوحدة", "إجمالي بعد الخصم", "ضريبة %", "قيمة الضريبة", "صافي السطر",
            "التشغيلة", "تاريخ الانتهاء"
        };
        for (var i = 0; i < hdr.Length; i++)
            ws.Cell(r, i + 1).Value = hdr[i];
        BoldHeader(ws.Range(r, 1, r, hdr.Length));
        r++;
        foreach (var l in inv.Lines.OrderBy(x => x.LineNo))
        {
            var name = l.Product?.ProdName ?? "";
            ws.Cell(r, 1).Value = l.LineNo;
            ws.Cell(r, 2).Value = l.ProdId;
            ws.Cell(r, 3).Value = name;
            ws.Cell(r, 4).Value = l.Qty;
            ws.Cell(r, 5).Value = l.PriceRetail;
            ws.Cell(r, 6).Value = l.Disc1Percent;
            ws.Cell(r, 7).Value = l.Disc2Percent;
            ws.Cell(r, 8).Value = l.Disc3Percent;
            ws.Cell(r, 9).Value = l.DiscountValue;
            ws.Cell(r, 10).Value = l.UnitSalePrice;
            ws.Cell(r, 11).Value = l.LineTotalAfterDiscount;
            ws.Cell(r, 12).Value = l.TaxPercent;
            ws.Cell(r, 13).Value = l.TaxValue;
            ws.Cell(r, 14).Value = l.LineNetTotal;
            ws.Cell(r, 15).Value = l.BatchNo ?? "";
            ws.Cell(r, 16).Value = l.Expiry?.ToString("yyyy-MM-dd") ?? "";
            r++;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public static byte[] PurchaseInvoice(PurchaseInvoice inv)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("فاتورة مشتريات"));
        ws.RightToLeft = true;
        int r = 1;
        void Kv(string k, string? v)
        {
            ws.Cell(r, 1).Value = k;
            ws.Cell(r, 2).Value = v ?? "";
            r++;
        }
        Kv("رقم الفاتورة", inv.PIId.ToString());
        Kv("تاريخ الفاتورة", inv.PIDate.ToString("yyyy-MM-dd"));
        Kv("المورد", inv.Customer?.CustomerName);
        Kv("كود الجهة", inv.CustomerId.ToString());
        Kv("المخزن", inv.Warehouse?.WarehouseName ?? inv.WarehouseId.ToString());
        Kv("طلب شراء مرجعي", inv.RefPRId?.ToString() ?? "");
        Kv("الحالة", inv.Status);
        Kv("مرحّلة", inv.IsPosted ? "نعم" : "لا");
        Kv("إجمالي السطور", inv.ItemsTotal.ToString("0.00"));
        Kv("إجمالي الخصم", inv.DiscountTotal.ToString("0.00"));
        Kv("إجمالي الضريبة", inv.TaxTotal.ToString("0.00"));
        Kv("صافي الفاتورة", inv.NetTotal.ToString("0.00"));
        r++;
        string[] hdr =
        {
            "رقم السطر", "كود الصنف", "اسم الصنف", "الكمية", "تكلفة الوحدة", "خصم شراء %", "سعر الجمهور",
            "التشغيلة", "تاريخ الانتهاء", "إجمالي السطر (تقديري)"
        };
        for (var i = 0; i < hdr.Length; i++)
            ws.Cell(r, i + 1).Value = hdr[i];
        BoldHeader(ws.Range(r, 1, r, hdr.Length));
        r++;
        foreach (var l in inv.Lines.OrderBy(x => x.LineNo))
        {
            var lineTot = l.Qty * l.UnitCost * (1 - l.PurchaseDiscountPct / 100m);
            ws.Cell(r, 1).Value = l.LineNo;
            ws.Cell(r, 2).Value = l.ProdId;
            ws.Cell(r, 3).Value = l.Product?.ProdName ?? "";
            ws.Cell(r, 4).Value = l.Qty;
            ws.Cell(r, 5).Value = l.UnitCost;
            ws.Cell(r, 6).Value = l.PurchaseDiscountPct;
            ws.Cell(r, 7).Value = l.PriceRetail;
            ws.Cell(r, 8).Value = l.BatchNo ?? "";
            ws.Cell(r, 9).Value = l.Expiry?.ToString("yyyy-MM-dd") ?? "";
            ws.Cell(r, 10).Value = lineTot;
            r++;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public static byte[] SalesReturn(SalesReturn sr)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("مرتجع مبيعات"));
        ws.RightToLeft = true;
        int r = 1;
        void Kv(string k, string? v)
        {
            ws.Cell(r, 1).Value = k;
            ws.Cell(r, 2).Value = v ?? "";
            r++;
        }
        Kv("رقم المرتجع", sr.SRId.ToString());
        Kv("التاريخ", sr.SRDate.ToString("yyyy-MM-dd"));
        Kv("العميل", sr.Customer?.CustomerName);
        Kv("المخزن", sr.Warehouse?.WarehouseName ?? sr.WarehouseId.ToString());
        Kv("فاتورة أصلية", sr.SalesInvoiceId?.ToString() ?? "");
        Kv("الحالة", sr.Status);
        Kv("مرحّل", sr.IsPosted ? "نعم" : "لا");
        Kv("الصافي", sr.NetTotal.ToString("0.00"));
        r++;
        string[] hdr =
        {
            "رقم السطر", "كود الصنف", "اسم الصنف", "الكمية", "سعر الجمهور", "خصم1 %", "خصم2 %", "خصم3 %",
            "قيمة الخصم", "سعر البيع", "إجمالي بعد الخصم", "ضريبة %", "قيمة الضريبة", "صافي السطر", "التشغيلة", "الصلاحية"
        };
        for (var i = 0; i < hdr.Length; i++)
            ws.Cell(r, i + 1).Value = hdr[i];
        BoldHeader(ws.Range(r, 1, r, hdr.Length));
        r++;
        foreach (var l in sr.Lines.OrderBy(x => x.LineNo))
        {
            ws.Cell(r, 1).Value = l.LineNo;
            ws.Cell(r, 2).Value = l.ProdId;
            ws.Cell(r, 3).Value = l.Product?.ProdName ?? "";
            ws.Cell(r, 4).Value = l.Qty;
            ws.Cell(r, 5).Value = l.PriceRetail;
            ws.Cell(r, 6).Value = l.Disc1Percent;
            ws.Cell(r, 7).Value = l.Disc2Percent;
            ws.Cell(r, 8).Value = l.Disc3Percent;
            ws.Cell(r, 9).Value = l.DiscountValue;
            ws.Cell(r, 10).Value = l.UnitSalePrice;
            ws.Cell(r, 11).Value = l.LineTotalAfterDiscount;
            ws.Cell(r, 12).Value = l.TaxPercent;
            ws.Cell(r, 13).Value = l.TaxValue;
            ws.Cell(r, 14).Value = l.LineNetTotal;
            ws.Cell(r, 15).Value = l.BatchNo ?? "";
            ws.Cell(r, 16).Value = l.Expiry?.ToString("yyyy-MM-dd") ?? "";
            r++;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public static byte[] PurchaseReturn(PurchaseReturn pr)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("مرتجع مشتريات"));
        ws.RightToLeft = true;
        int r = 1;
        void Kv(string k, string? v)
        {
            ws.Cell(r, 1).Value = k;
            ws.Cell(r, 2).Value = v ?? "";
            r++;
        }
        Kv("رقم المرتجع", pr.PRetId.ToString());
        Kv("التاريخ", pr.PRetDate.ToString("yyyy-MM-dd"));
        Kv("الجهة", pr.Customer?.CustomerName);
        Kv("المخزن", pr.Warehouse?.WarehouseName ?? pr.WarehouseId.ToString());
        Kv("فاتورة شراء مرجعية", pr.RefPIId?.ToString() ?? "");
        Kv("الحالة", pr.Status);
        Kv("مرحّل", pr.IsPosted ? "نعم" : "لا");
        Kv("صافي المرتجع", pr.NetTotal.ToString("0.00"));
        r++;
        string[] hdr =
        {
            "رقم السطر", "كود الصنف", "اسم الصنف", "الكمية", "تكلفة الوحدة", "خصم شراء %", "سعر الجمهور",
            "التشغيلة", "الصلاحية", "إجمالي السطر (تقديري)"
        };
        for (var i = 0; i < hdr.Length; i++)
            ws.Cell(r, i + 1).Value = hdr[i];
        BoldHeader(ws.Range(r, 1, r, hdr.Length));
        r++;
        foreach (var l in pr.Lines.OrderBy(x => x.LineNo))
        {
            var lineTot = l.Qty * l.UnitCost * (1 - l.PurchaseDiscountPct / 100m);
            ws.Cell(r, 1).Value = l.LineNo;
            ws.Cell(r, 2).Value = l.ProdId;
            ws.Cell(r, 3).Value = l.Product?.ProdName ?? "";
            ws.Cell(r, 4).Value = l.Qty;
            ws.Cell(r, 5).Value = l.UnitCost;
            ws.Cell(r, 6).Value = l.PurchaseDiscountPct;
            ws.Cell(r, 7).Value = l.PriceRetail;
            ws.Cell(r, 8).Value = l.BatchNo ?? "";
            ws.Cell(r, 9).Value = l.Expiry?.ToString("yyyy-MM-dd") ?? "";
            ws.Cell(r, 10).Value = lineTot;
            r++;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public static byte[] PurchaseRequest(PurchaseRequest pr)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("طلب شراء"));
        ws.RightToLeft = true;
        int r = 1;
        void Kv(string k, string? v)
        {
            ws.Cell(r, 1).Value = k;
            ws.Cell(r, 2).Value = v ?? "";
            r++;
        }
        Kv("رقم الطلب", pr.PRId.ToString());
        Kv("تاريخ الطلب", pr.PRDate.ToString("yyyy-MM-dd"));
        Kv("مطلوب قبل", pr.NeedByDate?.ToString("yyyy-MM-dd") ?? "");
        Kv("المورد", pr.Customer?.CustomerName);
        Kv("المخزن", pr.Warehouse?.WarehouseName ?? pr.WarehouseId.ToString());
        Kv("الحالة", pr.Status);
        Kv("محوّل لفاتورة", pr.IsConverted ? "نعم" : "لا");
        Kv("إجمالي الكمية", pr.TotalQtyRequested.ToString());
        Kv("إجمالي التكلفة المتوقعة", pr.ExpectedItemsTotal.ToString("0.####"));
        Kv("الضريبة", pr.TaxAmount.ToString("0.00"));
        r++;
        string[] hdr =
        {
            "رقم السطر", "كود الصنف", "اسم الصنف", "الكمية المطلوبة", "مرجع السعر", "سعر الجمهور", "خصم شراء %",
            "التكلفة المتوقعة", "التشغيلة المفضلة", "الصلاحية المفضلة", "الكمية المحوّلة"
        };
        for (var i = 0; i < hdr.Length; i++)
            ws.Cell(r, i + 1).Value = hdr[i];
        BoldHeader(ws.Range(r, 1, r, hdr.Length));
        r++;
        foreach (var l in pr.Lines.OrderBy(x => x.LineNo))
        {
            ws.Cell(r, 1).Value = l.LineNo;
            ws.Cell(r, 2).Value = l.ProdId;
            ws.Cell(r, 3).Value = l.Product?.ProdName ?? "";
            ws.Cell(r, 4).Value = l.QtyRequested;
            ws.Cell(r, 5).Value = l.PriceBasis ?? "";
            ws.Cell(r, 6).Value = l.PriceRetail;
            ws.Cell(r, 7).Value = l.PurchaseDiscountPct;
            ws.Cell(r, 8).Value = l.ExpectedCost;
            ws.Cell(r, 9).Value = l.PreferredBatchNo ?? "";
            ws.Cell(r, 10).Value = l.PreferredExpiry?.ToString("yyyy-MM-dd") ?? "";
            ws.Cell(r, 11).Value = l.QtyConverted;
            r++;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public static byte[] SalesOrder(SalesOrder so)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("أمر بيع"));
        ws.RightToLeft = true;
        int r = 1;
        void Kv(string k, string? v)
        {
            ws.Cell(r, 1).Value = k;
            ws.Cell(r, 2).Value = v ?? "";
            r++;
        }
        Kv("رقم الأمر", so.SOId.ToString());
        Kv("تاريخ الأمر", so.SODate.ToString("yyyy-MM-dd"));
        Kv("العميل", so.Customer?.CustomerName);
        Kv("المخزن", so.Warehouse?.WarehouseName ?? so.WarehouseId.ToString());
        Kv("الحالة", so.Status);
        Kv("محوّل", so.IsConverted ? "نعم" : "لا");
        Kv("إجمالي الكمية", so.TotalQtyRequested.ToString());
        Kv("إجمالي القيمة المتوقعة", so.ExpectedItemsTotal.ToString("0.####"));
        r++;
        string[] hdr =
        {
            "رقم السطر", "كود الصنف", "اسم الصنف", "الكمية", "سعر الجمهور المطلوب", "خصم مبيعات %",
            "سعر/تكلفة متوقعة للوحدة", "التشغيلة المفضلة", "الصلاحية المفضلة", "الكمية المحوّلة", "قيمة السطر المتوقعة"
        };
        for (var i = 0; i < hdr.Length; i++)
            ws.Cell(r, i + 1).Value = hdr[i];
        BoldHeader(ws.Range(r, 1, r, hdr.Length));
        r++;
        foreach (var l in so.Lines.OrderBy(x => x.LineNo))
        {
            ws.Cell(r, 1).Value = l.LineNo;
            ws.Cell(r, 2).Value = l.ProdId;
            ws.Cell(r, 3).Value = l.Product?.ProdName ?? "";
            ws.Cell(r, 4).Value = l.QtyRequested;
            ws.Cell(r, 5).Value = l.RequestedRetailPrice;
            ws.Cell(r, 6).Value = l.SalesDiscountPct;
            ws.Cell(r, 7).Value = l.ExpectedUnitPrice;
            ws.Cell(r, 8).Value = l.PreferredBatchNo ?? "";
            ws.Cell(r, 9).Value = l.PreferredExpiry?.ToString("yyyy-MM-dd") ?? "";
            ws.Cell(r, 10).Value = l.QtyConverted;
            ws.Cell(r, 11).Value = l.ExpectedLineTotal;
            r++;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
