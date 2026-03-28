using ERP.Models;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Infrastructure
{
    /// <summary>
    /// روابط فتح مستند المصدر من دفتر الحركة المخزنية (مع frame=1 للتابات).
    /// </summary>
    public static class StockLedgerSourceDocumentHelper
    {
        public static (string? Url, string TabId, string Title) GetOpenDocument(IUrlHelper url, StockLedger row)
        {
            var id = row.SourceId;
            if (id <= 0)
                return (null, "", "");

            var st = (row.SourceType ?? "").Trim();
            const int frame = 1;

            return st switch
            {
                "Sales" => (
                    url.Action("Show", "SalesInvoices", new { id, frame }),
                    "si-show-tab",
                    "فاتورة مبيعات"),

                "Purchase" => (
                    url.Action("Show", "PurchaseInvoices", new { id, frame }),
                    "pi-show-tab",
                    "فاتورة مشتريات"),

                "SalesReturn" => (
                    url.Action("Edit", "SalesReturns", new { id, frame }),
                    "sr-show-tab",
                    "مرتجع مبيعات"),

                "PurchaseReturn" => (
                    url.Action("Edit", "PurchaseReturns", new { id, frame }),
                    "pret-show-tab",
                    "مرتجع مشتريات"),

                "Adjustment" => (
                    url.Action("Show", "StockAdjustments", new { id, frame }),
                    "stock-adj-show-tab",
                    "تسوية مخزون"),

                "TransferIn" or "TransferOut" => (
                    url.Action("Show", "StockTransfers", new { id, frame }),
                    "st-transfer-show-tab",
                    "تحويل مخزني"),

                "SyncToProductWarehouse" => (
                    url.Action("Show", "Products", new { id = row.ProdId, frame }),
                    "product-edit-tab",
                    "صنف"),

                "Opening" => (null, "", ""),

                _ => (null, "", "")
            };
        }
    }
}
