using System;
using System.Linq;
using ERP.Models;
using Microsoft.EntityFrameworkCore;

namespace ERP.Controllers
{
    public partial class ReportsController
    {
        private static string GetPdrCanonicalDocNameAr(string reportType) => reportType switch
        {
            "Sales" => "فاتورة مبيعات",
            "Purchases" => "فاتورة مشتريات",
            "SalesReturns" => "مرتجع بيع",
            "PurchaseReturns" => "مرتجع شراء",
            "Adjustments" => "تسوية جرد",
            "Transfers" => "تحويل مخزني",
            "PurchaseRequests" => "طلب شراء",
            "SalesOrders" => "أمر بيع",
            _ => ""
        };

        private static bool PdrDocNameArPasses(string reportType, string? filterCol_docNameAr)
        {
            var vals = ParseProductDetailsFilterStrings(filterCol_docNameAr);
            if (vals.Count == 0) return true;
            var expected = GetPdrCanonicalDocNameAr(reportType);
            return !string.IsNullOrEmpty(expected) && vals.Contains(expected);
        }

        private static IQueryable<SalesInvoiceLine> ApplyPdrSalesLineSort(IQueryable<SalesInvoiceLine> q, string? sort, string? dir)
        {
            var sortKey = (sort ?? "Date").Trim();
            var asc = string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase);
            return sortKey.ToLowerInvariant() switch
            {
                "docno" => asc
                    ? q.OrderBy(l => l.SalesInvoice!.SIId).ThenBy(l => l.LineNo)
                    : q.OrderByDescending(l => l.SalesInvoice!.SIId).ThenBy(l => l.LineNo),
                "author" => asc
                    ? q.OrderBy(l => l.SalesInvoice!.CreatedBy).ThenByDescending(l => l.SalesInvoice!.SIDate)
                    : q.OrderByDescending(l => l.SalesInvoice!.CreatedBy).ThenByDescending(l => l.SalesInvoice!.SIDate),
                "region" => asc
                    ? q.OrderBy(l => l.SalesInvoice!.Warehouse != null && l.SalesInvoice.Warehouse.Branch != null ? l.SalesInvoice.Warehouse.Branch.BranchName : "").ThenByDescending(l => l.SalesInvoice!.SIDate)
                    : q.OrderByDescending(l => l.SalesInvoice!.Warehouse != null && l.SalesInvoice.Warehouse.Branch != null ? l.SalesInvoice.Warehouse.Branch.BranchName : "").ThenByDescending(l => l.SalesInvoice!.SIDate),
                "documentnamear" => q.OrderByDescending(l => l.SalesInvoice!.SIDate).ThenBy(l => l.SalesInvoice!.SIId).ThenBy(l => l.LineNo),
                "productcode" => asc
                    ? q.OrderBy(l => l.Product!.ProdId).ThenByDescending(l => l.SalesInvoice!.SIDate)
                    : q.OrderByDescending(l => l.Product!.ProdId).ThenByDescending(l => l.SalesInvoice!.SIDate),
                "productname" => asc
                    ? q.OrderBy(l => l.Product!.ProdName).ThenByDescending(l => l.SalesInvoice!.SIDate)
                    : q.OrderByDescending(l => l.Product!.ProdName).ThenByDescending(l => l.SalesInvoice!.SIDate),
                "partyname" => asc
                    ? q.OrderBy(l => l.SalesInvoice!.Customer != null ? l.SalesInvoice.Customer.CustomerName : "").ThenByDescending(l => l.SalesInvoice!.SIDate)
                    : q.OrderByDescending(l => l.SalesInvoice!.Customer != null ? l.SalesInvoice.Customer.CustomerName : "").ThenByDescending(l => l.SalesInvoice!.SIDate),
                "warehousename" => asc
                    ? q.OrderBy(l => l.SalesInvoice!.Warehouse != null ? l.SalesInvoice.Warehouse.WarehouseName : "").ThenByDescending(l => l.SalesInvoice!.SIDate)
                    : q.OrderByDescending(l => l.SalesInvoice!.Warehouse != null ? l.SalesInvoice.Warehouse.WarehouseName : "").ThenByDescending(l => l.SalesInvoice!.SIDate),
                "date" => asc
                    ? q.OrderBy(l => l.SalesInvoice!.SIDate).ThenBy(l => l.SalesInvoice!.SIId).ThenBy(l => l.LineNo)
                    : q.OrderByDescending(l => l.SalesInvoice!.SIDate).ThenBy(l => l.SalesInvoice!.SIId).ThenBy(l => l.LineNo),
                "qty" => asc
                    ? q.OrderBy(l => l.Qty).ThenByDescending(l => l.SalesInvoice!.SIDate)
                    : q.OrderByDescending(l => l.Qty).ThenByDescending(l => l.SalesInvoice!.SIDate),
                "unitprice" => asc
                    ? q.OrderBy(l => l.PriceRetail).ThenByDescending(l => l.SalesInvoice!.SIDate)
                    : q.OrderByDescending(l => l.PriceRetail).ThenByDescending(l => l.SalesInvoice!.SIDate),
                "linetotal" => asc
                    ? q.OrderBy(l => l.LineNetTotal).ThenByDescending(l => l.SalesInvoice!.SIDate)
                    : q.OrderByDescending(l => l.LineNetTotal).ThenByDescending(l => l.SalesInvoice!.SIDate),
                "batchno" => asc
                    ? q.OrderBy(l => l.BatchNo ?? "").ThenByDescending(l => l.SalesInvoice!.SIDate)
                    : q.OrderByDescending(l => l.BatchNo ?? "").ThenByDescending(l => l.SalesInvoice!.SIDate),
                "expiry" => asc
                    ? q.OrderBy(l => l.Expiry ?? DateTime.MinValue).ThenByDescending(l => l.SalesInvoice!.SIDate)
                    : q.OrderByDescending(l => l.Expiry ?? DateTime.MinValue).ThenByDescending(l => l.SalesInvoice!.SIDate),
                "notes" => asc
                    ? q.OrderBy(l => l.Notes ?? "").ThenByDescending(l => l.SalesInvoice!.SIDate)
                    : q.OrderByDescending(l => l.Notes ?? "").ThenByDescending(l => l.SalesInvoice!.SIDate),
                _ => q.OrderByDescending(l => l.SalesInvoice!.SIDate).ThenBy(l => l.SalesInvoice!.SIId).ThenBy(l => l.LineNo)
            };
        }

        private static IQueryable<PILine> ApplyPdrPiLineSort(IQueryable<PILine> q, string? sort, string? dir)
        {
            var sortKey = (sort ?? "Date").Trim();
            var asc = string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase);
            return sortKey.ToLowerInvariant() switch
            {
                "docno" => asc ? q.OrderBy(l => l.PurchaseInvoice!.PIId).ThenBy(l => l.LineNo) : q.OrderByDescending(l => l.PurchaseInvoice!.PIId).ThenBy(l => l.LineNo),
                "author" => asc ? q.OrderBy(l => l.PurchaseInvoice!.CreatedBy).ThenByDescending(l => l.PurchaseInvoice!.PIDate) : q.OrderByDescending(l => l.PurchaseInvoice!.CreatedBy).ThenByDescending(l => l.PurchaseInvoice!.PIDate),
                "documentnamear" => q.OrderByDescending(l => l.PurchaseInvoice!.PIDate).ThenBy(l => l.PurchaseInvoice!.PIId).ThenBy(l => l.LineNo),
                "productcode" => asc ? q.OrderBy(l => l.Product!.ProdId).ThenByDescending(l => l.PurchaseInvoice!.PIDate) : q.OrderByDescending(l => l.Product!.ProdId).ThenByDescending(l => l.PurchaseInvoice!.PIDate),
                "productname" => asc ? q.OrderBy(l => l.Product!.ProdName).ThenByDescending(l => l.PurchaseInvoice!.PIDate) : q.OrderByDescending(l => l.Product!.ProdName).ThenByDescending(l => l.PurchaseInvoice!.PIDate),
                "partyname" => asc ? q.OrderBy(l => l.PurchaseInvoice!.Customer != null ? l.PurchaseInvoice.Customer.CustomerName : "").ThenByDescending(l => l.PurchaseInvoice!.PIDate) : q.OrderByDescending(l => l.PurchaseInvoice!.Customer != null ? l.PurchaseInvoice.Customer.CustomerName : "").ThenByDescending(l => l.PurchaseInvoice!.PIDate),
                "region" => asc
                    ? q.OrderBy(l => l.PurchaseInvoice!.Warehouse != null && l.PurchaseInvoice.Warehouse.Branch != null ? l.PurchaseInvoice.Warehouse.Branch.BranchName : "").ThenByDescending(l => l.PurchaseInvoice!.PIDate)
                    : q.OrderByDescending(l => l.PurchaseInvoice!.Warehouse != null && l.PurchaseInvoice.Warehouse.Branch != null ? l.PurchaseInvoice.Warehouse.Branch.BranchName : "").ThenByDescending(l => l.PurchaseInvoice!.PIDate),
                "warehousename" => asc
                    ? q.OrderBy(l => l.PurchaseInvoice!.Warehouse != null ? l.PurchaseInvoice.Warehouse.WarehouseName : "").ThenByDescending(l => l.PurchaseInvoice!.PIDate)
                    : q.OrderByDescending(l => l.PurchaseInvoice!.Warehouse != null ? l.PurchaseInvoice.Warehouse.WarehouseName : "").ThenByDescending(l => l.PurchaseInvoice!.PIDate),
                "date" => asc ? q.OrderBy(l => l.PurchaseInvoice!.PIDate).ThenBy(l => l.PurchaseInvoice!.PIId).ThenBy(l => l.LineNo) : q.OrderByDescending(l => l.PurchaseInvoice!.PIDate).ThenBy(l => l.PurchaseInvoice!.PIId).ThenBy(l => l.LineNo),
                "qty" => asc ? q.OrderBy(l => l.Qty).ThenByDescending(l => l.PurchaseInvoice!.PIDate) : q.OrderByDescending(l => l.Qty).ThenByDescending(l => l.PurchaseInvoice!.PIDate),
                "unitprice" => asc ? q.OrderBy(l => l.UnitCost).ThenByDescending(l => l.PurchaseInvoice!.PIDate) : q.OrderByDescending(l => l.UnitCost).ThenByDescending(l => l.PurchaseInvoice!.PIDate),
                "linetotal" => asc ? q.OrderBy(l => l.Qty * l.UnitCost).ThenByDescending(l => l.PurchaseInvoice!.PIDate) : q.OrderByDescending(l => l.Qty * l.UnitCost).ThenByDescending(l => l.PurchaseInvoice!.PIDate),
                "batchno" => asc ? q.OrderBy(l => l.BatchNo ?? "").ThenByDescending(l => l.PurchaseInvoice!.PIDate) : q.OrderByDescending(l => l.BatchNo ?? "").ThenByDescending(l => l.PurchaseInvoice!.PIDate),
                "expiry" => asc ? q.OrderBy(l => l.Expiry ?? DateTime.MinValue).ThenByDescending(l => l.PurchaseInvoice!.PIDate) : q.OrderByDescending(l => l.Expiry ?? DateTime.MinValue).ThenByDescending(l => l.PurchaseInvoice!.PIDate),
                _ => q.OrderByDescending(l => l.PurchaseInvoice!.PIDate).ThenBy(l => l.PurchaseInvoice!.PIId).ThenBy(l => l.LineNo)
            };
        }

        private static IQueryable<PurchaseReturnLine> ApplyPdrPurchaseReturnLineSort(IQueryable<PurchaseReturnLine> q, string? sort, string? dir)
        {
            var sortKey = (sort ?? "Date").Trim();
            var asc = string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase);
            return sortKey.ToLowerInvariant() switch
            {
                "docno" => asc ? q.OrderBy(l => l.PurchaseReturn!.PRetId).ThenBy(l => l.LineNo) : q.OrderByDescending(l => l.PurchaseReturn!.PRetId).ThenBy(l => l.LineNo),
                "author" => asc ? q.OrderBy(l => l.PurchaseReturn!.CreatedBy).ThenByDescending(l => l.PurchaseReturn!.PRetDate) : q.OrderByDescending(l => l.PurchaseReturn!.CreatedBy).ThenByDescending(l => l.PurchaseReturn!.PRetDate),
                "documentnamear" => q.OrderByDescending(l => l.PurchaseReturn!.PRetDate).ThenBy(l => l.PurchaseReturn!.PRetId).ThenBy(l => l.LineNo),
                "productcode" => asc ? q.OrderBy(l => l.Product!.ProdId).ThenByDescending(l => l.PurchaseReturn!.PRetDate) : q.OrderByDescending(l => l.Product!.ProdId).ThenByDescending(l => l.PurchaseReturn!.PRetDate),
                "productname" => asc ? q.OrderBy(l => l.Product!.ProdName).ThenByDescending(l => l.PurchaseReturn!.PRetDate) : q.OrderByDescending(l => l.Product!.ProdName).ThenByDescending(l => l.PurchaseReturn!.PRetDate),
                "partyname" => asc ? q.OrderBy(l => l.PurchaseReturn!.Customer != null ? l.PurchaseReturn.Customer.CustomerName : "").ThenByDescending(l => l.PurchaseReturn!.PRetDate) : q.OrderByDescending(l => l.PurchaseReturn!.Customer != null ? l.PurchaseReturn.Customer.CustomerName : "").ThenByDescending(l => l.PurchaseReturn!.PRetDate),
                "date" => asc ? q.OrderBy(l => l.PurchaseReturn!.PRetDate).ThenBy(l => l.PurchaseReturn!.PRetId).ThenBy(l => l.LineNo) : q.OrderByDescending(l => l.PurchaseReturn!.PRetDate).ThenBy(l => l.PurchaseReturn!.PRetId).ThenBy(l => l.LineNo),
                "qty" => asc ? q.OrderBy(l => l.Qty).ThenByDescending(l => l.PurchaseReturn!.PRetDate) : q.OrderByDescending(l => l.Qty).ThenByDescending(l => l.PurchaseReturn!.PRetDate),
                "unitprice" => asc ? q.OrderBy(l => l.UnitCost).ThenByDescending(l => l.PurchaseReturn!.PRetDate) : q.OrderByDescending(l => l.UnitCost).ThenByDescending(l => l.PurchaseReturn!.PRetDate),
                "linetotal" => asc ? q.OrderBy(l => l.Qty * l.UnitCost).ThenByDescending(l => l.PurchaseReturn!.PRetDate) : q.OrderByDescending(l => l.Qty * l.UnitCost).ThenByDescending(l => l.PurchaseReturn!.PRetDate),
                "batchno" => asc ? q.OrderBy(l => l.BatchNo ?? "").ThenByDescending(l => l.PurchaseReturn!.PRetDate) : q.OrderByDescending(l => l.BatchNo ?? "").ThenByDescending(l => l.PurchaseReturn!.PRetDate),
                "expiry" => asc ? q.OrderBy(l => l.Expiry ?? DateTime.MinValue).ThenByDescending(l => l.PurchaseReturn!.PRetDate) : q.OrderByDescending(l => l.Expiry ?? DateTime.MinValue).ThenByDescending(l => l.PurchaseReturn!.PRetDate),
                _ => q.OrderByDescending(l => l.PurchaseReturn!.PRetDate).ThenBy(l => l.PurchaseReturn!.PRetId).ThenBy(l => l.LineNo)
            };
        }

        private static IQueryable<StockAdjustmentLine> ApplyPdrStockAdjustmentLineSort(IQueryable<StockAdjustmentLine> q, string? sort, string? dir)
        {
            var sortKey = (sort ?? "Date").Trim();
            var asc = string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase);
            return sortKey.ToLowerInvariant() switch
            {
                "docno" => asc ? q.OrderBy(l => l.StockAdjustment!.Id).ThenBy(l => l.Id) : q.OrderByDescending(l => l.StockAdjustment!.Id).ThenBy(l => l.Id),
                "author" => asc ? q.OrderBy(l => l.StockAdjustment!.PostedBy).ThenByDescending(l => l.StockAdjustment!.AdjustmentDate) : q.OrderByDescending(l => l.StockAdjustment!.PostedBy).ThenByDescending(l => l.StockAdjustment!.AdjustmentDate),
                "region" => asc ? q.OrderBy(l => l.StockAdjustment!.Warehouse != null && l.StockAdjustment.Warehouse.Branch != null ? l.StockAdjustment.Warehouse.Branch.BranchName : "").ThenByDescending(l => l.StockAdjustment!.AdjustmentDate) : q.OrderByDescending(l => l.StockAdjustment!.Warehouse != null && l.StockAdjustment.Warehouse.Branch != null ? l.StockAdjustment.Warehouse.Branch.BranchName : "").ThenByDescending(l => l.StockAdjustment!.AdjustmentDate),
                "warehousename" => asc ? q.OrderBy(l => l.StockAdjustment!.Warehouse != null ? l.StockAdjustment.Warehouse.WarehouseName : "").ThenByDescending(l => l.StockAdjustment!.AdjustmentDate) : q.OrderByDescending(l => l.StockAdjustment!.Warehouse != null ? l.StockAdjustment.Warehouse.WarehouseName : "").ThenByDescending(l => l.StockAdjustment!.AdjustmentDate),
                "documentnamear" => q.OrderByDescending(l => l.StockAdjustment!.AdjustmentDate).ThenBy(l => l.StockAdjustmentId).ThenBy(l => l.Id),
                "productcode" => asc ? q.OrderBy(l => l.Product!.ProdId).ThenByDescending(l => l.StockAdjustment!.AdjustmentDate) : q.OrderByDescending(l => l.Product!.ProdId).ThenByDescending(l => l.StockAdjustment!.AdjustmentDate),
                "productname" => asc ? q.OrderBy(l => l.Product!.ProdName).ThenByDescending(l => l.StockAdjustment!.AdjustmentDate) : q.OrderByDescending(l => l.Product!.ProdName).ThenByDescending(l => l.StockAdjustment!.AdjustmentDate),
                "date" => asc ? q.OrderBy(l => l.StockAdjustment!.AdjustmentDate).ThenBy(l => l.StockAdjustmentId).ThenBy(l => l.Id) : q.OrderByDescending(l => l.StockAdjustment!.AdjustmentDate).ThenBy(l => l.StockAdjustmentId).ThenBy(l => l.Id),
                "qty" => asc ? q.OrderBy(l => l.QtyDiff).ThenByDescending(l => l.StockAdjustment!.AdjustmentDate) : q.OrderByDescending(l => l.QtyDiff).ThenByDescending(l => l.StockAdjustment!.AdjustmentDate),
                "unitprice" => asc ? q.OrderBy(l => l.CostPerUnit ?? 0m).ThenByDescending(l => l.StockAdjustment!.AdjustmentDate) : q.OrderByDescending(l => l.CostPerUnit ?? 0m).ThenByDescending(l => l.StockAdjustment!.AdjustmentDate),
                "linetotal" => asc ? q.OrderBy(l => l.CostDiff ?? 0m).ThenByDescending(l => l.StockAdjustment!.AdjustmentDate) : q.OrderByDescending(l => l.CostDiff ?? 0m).ThenByDescending(l => l.StockAdjustment!.AdjustmentDate),
                "notes" => asc ? q.OrderBy(l => l.Note ?? "").ThenByDescending(l => l.StockAdjustment!.AdjustmentDate) : q.OrderByDescending(l => l.Note ?? "").ThenByDescending(l => l.StockAdjustment!.AdjustmentDate),
                _ => q.OrderByDescending(l => l.StockAdjustment!.AdjustmentDate).ThenBy(l => l.StockAdjustmentId).ThenBy(l => l.Id)
            };
        }

        private static IQueryable<StockTransferLine> ApplyPdrStockTransferLineSort(IQueryable<StockTransferLine> q, string? sort, string? dir)
        {
            var sortKey = (sort ?? "Date").Trim();
            var asc = string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase);
            return sortKey.ToLowerInvariant() switch
            {
                "docno" => asc ? q.OrderBy(l => l.StockTransfer!.Id).ThenBy(l => l.LineNo) : q.OrderByDescending(l => l.StockTransfer!.Id).ThenBy(l => l.LineNo),
                "author" => asc ? q.OrderBy(l => l.StockTransfer!.PostedBy).ThenByDescending(l => l.StockTransfer!.TransferDate) : q.OrderByDescending(l => l.StockTransfer!.PostedBy).ThenByDescending(l => l.StockTransfer!.TransferDate),
                "region" => asc ? q.OrderBy(l => l.StockTransfer!.FromWarehouse != null && l.StockTransfer.FromWarehouse.Branch != null ? l.StockTransfer.FromWarehouse.Branch.BranchName : "").ThenByDescending(l => l.StockTransfer!.TransferDate) : q.OrderByDescending(l => l.StockTransfer!.FromWarehouse != null && l.StockTransfer.FromWarehouse.Branch != null ? l.StockTransfer.FromWarehouse.Branch.BranchName : "").ThenByDescending(l => l.StockTransfer!.TransferDate),
                "documentnamear" => q.OrderByDescending(l => l.StockTransfer!.TransferDate).ThenBy(l => l.StockTransferId).ThenBy(l => l.LineNo),
                "productcode" => asc ? q.OrderBy(l => l.Product!.ProdId).ThenByDescending(l => l.StockTransfer!.TransferDate) : q.OrderByDescending(l => l.Product!.ProdId).ThenByDescending(l => l.StockTransfer!.TransferDate),
                "productname" => asc ? q.OrderBy(l => l.Product!.ProdName).ThenByDescending(l => l.StockTransfer!.TransferDate) : q.OrderByDescending(l => l.Product!.ProdName).ThenByDescending(l => l.StockTransfer!.TransferDate),
                "date" => asc ? q.OrderBy(l => l.StockTransfer!.TransferDate).ThenBy(l => l.StockTransferId).ThenBy(l => l.LineNo) : q.OrderByDescending(l => l.StockTransfer!.TransferDate).ThenBy(l => l.StockTransferId).ThenBy(l => l.LineNo),
                "qty" => asc ? q.OrderBy(l => l.Qty).ThenByDescending(l => l.StockTransfer!.TransferDate) : q.OrderByDescending(l => l.Qty).ThenByDescending(l => l.StockTransfer!.TransferDate),
                "unitprice" => asc ? q.OrderBy(l => l.UnitCost).ThenByDescending(l => l.StockTransfer!.TransferDate) : q.OrderByDescending(l => l.UnitCost).ThenByDescending(l => l.StockTransfer!.TransferDate),
                "linetotal" => asc ? q.OrderBy(l => l.Qty * l.UnitCost).ThenByDescending(l => l.StockTransfer!.TransferDate) : q.OrderByDescending(l => l.Qty * l.UnitCost).ThenByDescending(l => l.StockTransfer!.TransferDate),
                "notes" => asc ? q.OrderBy(l => l.Note ?? "").ThenByDescending(l => l.StockTransfer!.TransferDate) : q.OrderByDescending(l => l.Note ?? "").ThenByDescending(l => l.StockTransfer!.TransferDate),
                _ => q.OrderByDescending(l => l.StockTransfer!.TransferDate).ThenBy(l => l.StockTransferId).ThenBy(l => l.LineNo)
            };
        }

        private static IQueryable<PRLine> ApplyPdrPrLineSort(IQueryable<PRLine> q, string? sort, string? dir)
        {
            var sortKey = (sort ?? "Date").Trim();
            var asc = string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase);
            return sortKey.ToLowerInvariant() switch
            {
                "docno" => asc ? q.OrderBy(l => l.PurchaseRequest!.PRId).ThenBy(l => l.LineNo) : q.OrderByDescending(l => l.PurchaseRequest!.PRId).ThenBy(l => l.LineNo),
                "author" => asc ? q.OrderBy(l => l.PurchaseRequest!.CreatedBy).ThenByDescending(l => l.PurchaseRequest!.PRDate) : q.OrderByDescending(l => l.PurchaseRequest!.CreatedBy).ThenByDescending(l => l.PurchaseRequest!.PRDate),
                "region" => asc ? q.OrderBy(l => l.PurchaseRequest!.Warehouse != null && l.PurchaseRequest.Warehouse.Branch != null ? l.PurchaseRequest.Warehouse.Branch.BranchName : "").ThenByDescending(l => l.PurchaseRequest!.PRDate) : q.OrderByDescending(l => l.PurchaseRequest!.Warehouse != null && l.PurchaseRequest.Warehouse.Branch != null ? l.PurchaseRequest.Warehouse.Branch.BranchName : "").ThenByDescending(l => l.PurchaseRequest!.PRDate),
                "warehousename" => asc ? q.OrderBy(l => l.PurchaseRequest!.Warehouse != null ? l.PurchaseRequest.Warehouse.WarehouseName : "").ThenByDescending(l => l.PurchaseRequest!.PRDate) : q.OrderByDescending(l => l.PurchaseRequest!.Warehouse != null ? l.PurchaseRequest.Warehouse.WarehouseName : "").ThenByDescending(l => l.PurchaseRequest!.PRDate),
                "documentnamear" => q.OrderByDescending(l => l.PurchaseRequest!.PRDate).ThenBy(l => l.PRId).ThenBy(l => l.LineNo),
                "productcode" => asc ? q.OrderBy(l => l.Product!.ProdId).ThenByDescending(l => l.PurchaseRequest!.PRDate) : q.OrderByDescending(l => l.Product!.ProdId).ThenByDescending(l => l.PurchaseRequest!.PRDate),
                "productname" => asc ? q.OrderBy(l => l.Product!.ProdName).ThenByDescending(l => l.PurchaseRequest!.PRDate) : q.OrderByDescending(l => l.Product!.ProdName).ThenByDescending(l => l.PurchaseRequest!.PRDate),
                "partyname" => asc ? q.OrderBy(l => l.PurchaseRequest!.Customer != null ? l.PurchaseRequest.Customer.CustomerName : "").ThenByDescending(l => l.PurchaseRequest!.PRDate) : q.OrderByDescending(l => l.PurchaseRequest!.Customer != null ? l.PurchaseRequest.Customer.CustomerName : "").ThenByDescending(l => l.PurchaseRequest!.PRDate),
                "date" => asc ? q.OrderBy(l => l.PurchaseRequest!.PRDate).ThenBy(l => l.PRId).ThenBy(l => l.LineNo) : q.OrderByDescending(l => l.PurchaseRequest!.PRDate).ThenBy(l => l.PRId).ThenBy(l => l.LineNo),
                "qty" => asc ? q.OrderBy(l => l.QtyRequested).ThenByDescending(l => l.PurchaseRequest!.PRDate) : q.OrderByDescending(l => l.QtyRequested).ThenByDescending(l => l.PurchaseRequest!.PRDate),
                "unitprice" => asc ? q.OrderBy(l => l.ExpectedCost).ThenByDescending(l => l.PurchaseRequest!.PRDate) : q.OrderByDescending(l => l.ExpectedCost).ThenByDescending(l => l.PurchaseRequest!.PRDate),
                "linetotal" => asc ? q.OrderBy(l => l.QtyRequested * l.ExpectedCost).ThenByDescending(l => l.PurchaseRequest!.PRDate) : q.OrderByDescending(l => l.QtyRequested * l.ExpectedCost).ThenByDescending(l => l.PurchaseRequest!.PRDate),
                "batchno" => asc ? q.OrderBy(l => l.PreferredBatchNo ?? "").ThenByDescending(l => l.PurchaseRequest!.PRDate) : q.OrderByDescending(l => l.PreferredBatchNo ?? "").ThenByDescending(l => l.PurchaseRequest!.PRDate),
                "expiry" => asc ? q.OrderBy(l => l.PreferredExpiry ?? DateTime.MinValue).ThenByDescending(l => l.PurchaseRequest!.PRDate) : q.OrderByDescending(l => l.PreferredExpiry ?? DateTime.MinValue).ThenByDescending(l => l.PurchaseRequest!.PRDate),
                _ => q.OrderByDescending(l => l.PurchaseRequest!.PRDate).ThenBy(l => l.PRId).ThenBy(l => l.LineNo)
            };
        }

        private static IQueryable<SOLine> ApplyPdrSoLineSort(IQueryable<SOLine> q, string? sort, string? dir)
        {
            var sortKey = (sort ?? "Date").Trim();
            var asc = string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase);
            return sortKey.ToLowerInvariant() switch
            {
                "docno" => asc ? q.OrderBy(l => l.SalesOrder!.SOId).ThenBy(l => l.LineNo) : q.OrderByDescending(l => l.SalesOrder!.SOId).ThenBy(l => l.LineNo),
                "author" => asc ? q.OrderBy(l => l.SalesOrder!.CreatedBy).ThenByDescending(l => l.SalesOrder!.SODate) : q.OrderByDescending(l => l.SalesOrder!.CreatedBy).ThenByDescending(l => l.SalesOrder!.SODate),
                "documentnamear" => q.OrderByDescending(l => l.SalesOrder!.SODate).ThenBy(l => l.SOId).ThenBy(l => l.LineNo),
                "productcode" => asc ? q.OrderBy(l => l.Product!.ProdId).ThenByDescending(l => l.SalesOrder!.SODate) : q.OrderByDescending(l => l.Product!.ProdId).ThenByDescending(l => l.SalesOrder!.SODate),
                "productname" => asc ? q.OrderBy(l => l.Product!.ProdName).ThenByDescending(l => l.SalesOrder!.SODate) : q.OrderByDescending(l => l.Product!.ProdName).ThenByDescending(l => l.SalesOrder!.SODate),
                "partyname" => asc ? q.OrderBy(l => l.SalesOrder!.Customer != null ? l.SalesOrder.Customer.CustomerName : "").ThenByDescending(l => l.SalesOrder!.SODate) : q.OrderByDescending(l => l.SalesOrder!.Customer != null ? l.SalesOrder.Customer.CustomerName : "").ThenByDescending(l => l.SalesOrder!.SODate),
                "date" => asc ? q.OrderBy(l => l.SalesOrder!.SODate).ThenBy(l => l.SalesOrder!.SOId).ThenBy(l => l.LineNo) : q.OrderByDescending(l => l.SalesOrder!.SODate).ThenBy(l => l.SalesOrder!.SOId).ThenBy(l => l.LineNo),
                "qty" => asc ? q.OrderBy(l => l.QtyRequested).ThenByDescending(l => l.SalesOrder!.SODate) : q.OrderByDescending(l => l.QtyRequested).ThenByDescending(l => l.SalesOrder!.SODate),
                "unitprice" => asc ? q.OrderBy(l => l.RequestedRetailPrice).ThenByDescending(l => l.SalesOrder!.SODate) : q.OrderByDescending(l => l.RequestedRetailPrice).ThenByDescending(l => l.SalesOrder!.SODate),
                "linetotal" => asc ? q.OrderBy(l => l.QtyRequested * l.RequestedRetailPrice * (1 - l.SalesDiscountPct / 100m)).ThenByDescending(l => l.SalesOrder!.SODate) : q.OrderByDescending(l => l.QtyRequested * l.RequestedRetailPrice * (1 - l.SalesDiscountPct / 100m)).ThenByDescending(l => l.SalesOrder!.SODate),
                "batchno" => asc ? q.OrderBy(l => l.PreferredBatchNo ?? "").ThenByDescending(l => l.SalesOrder!.SODate) : q.OrderByDescending(l => l.PreferredBatchNo ?? "").ThenByDescending(l => l.SalesOrder!.SODate),
                "expiry" => asc ? q.OrderBy(l => l.PreferredExpiry ?? DateTime.MinValue).ThenByDescending(l => l.SalesOrder!.SODate) : q.OrderByDescending(l => l.PreferredExpiry ?? DateTime.MinValue).ThenByDescending(l => l.SalesOrder!.SODate),
                _ => q.OrderByDescending(l => l.SalesOrder!.SODate).ThenBy(l => l.SalesOrder!.SOId).ThenBy(l => l.LineNo)
            };
        }
    }
}
