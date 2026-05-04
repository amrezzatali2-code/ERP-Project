using ERP.Data;
using ERP.Infrastructure;
using ERP.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ERP.Controllers
{
    /// <summary>
    /// شاشات عرض معلومات التتبع وقائمة انتظار الإرسال.
    /// الإضافة هنا للعرض فقط ولا تغيّر أي منطق تشغيلي قائم.
    /// </summary>
    public class TrackTraceController : Controller
    {
        private readonly AppDbContext _context;

        public TrackTraceController(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(
            string? search,
            string? status,
            string? batchNo,
            string? sourceType,
            int? documentId,
            int? piId,
            int? lineNo,
            int? lineId,
            int page = 1,
            int pageSize = 50)
        {
            if (documentId == null && piId.HasValue && piId.Value > 0)
            {
                sourceType = "purchase-invoice";
                documentId = piId.Value;
            }

            var purchaseInvoices =
                from link in _context.PurchaseInvoiceLineUnits.AsNoTracking()
                join unit in _context.ItemUnits.AsNoTracking() on link.ItemUnitId equals unit.Id
                join line in _context.PILines.AsNoTracking() on new { link.PIId, link.LineNo } equals new { line.PIId, line.LineNo }
                join invoice in _context.PurchaseInvoices.AsNoTracking() on link.PIId equals invoice.PIId
                join product in _context.Products.AsNoTracking() on unit.ProdId equals product.ProdId into productJoin
                from product in productJoin.DefaultIfEmpty()
                join customer in _context.Customers.AsNoTracking() on invoice.CustomerId equals customer.CustomerId into customerJoin
                from customer in customerJoin.DefaultIfEmpty()
                join warehouse in _context.Warehouses.AsNoTracking() on unit.WarehouseId equals warehouse.WarehouseId into warehouseJoin
                from warehouse in warehouseJoin.DefaultIfEmpty()
                select new TrackTraceInfoRowViewModel
                {
                    ItemUnitId = unit.Id,
                    SourceType = "purchase-invoice",
                    SourceTitle = "فاتورة مشتريات",
                    DocumentId = link.PIId,
                    SourceLineId = null,
                    Uid = unit.Uid,
                    Gtin = unit.Gtin,
                    SerialNo = unit.SerialNo,
                    ProdId = unit.ProdId,
                    ProductName = product != null ? (product.ProdName ?? string.Empty) : string.Empty,
                    BatchNo = !string.IsNullOrWhiteSpace(unit.BatchNo) ? unit.BatchNo : line.BatchNo,
                    Expiry = unit.Expiry ?? line.Expiry,
                    Status = unit.Status,
                    WarehouseName = warehouse != null ? warehouse.WarehouseName : string.Empty,
                    PIId = link.PIId,
                    LineNo = link.LineNo,
                    InvoiceDate = invoice.PIDate,
                    SupplierName = customer != null ? customer.CustomerName : string.Empty,
                    CurrentSourceType = unit.CurrentSourceType,
                    CurrentSourceId = unit.CurrentSourceId,
                    CurrentSourceLineNo = unit.CurrentSourceLineNo,
                    CreatedAt = unit.CreatedAt
                };

            var salesInvoices =
                from link in _context.SalesInvoiceLineUnits.AsNoTracking()
                join unit in _context.ItemUnits.AsNoTracking() on link.ItemUnitId equals unit.Id
                join line in _context.SalesInvoiceLines.AsNoTracking() on new { link.SIId, link.LineNo } equals new { line.SIId, line.LineNo }
                join invoice in _context.SalesInvoices.AsNoTracking() on link.SIId equals invoice.SIId
                join product in _context.Products.AsNoTracking() on unit.ProdId equals product.ProdId into productJoin
                from product in productJoin.DefaultIfEmpty()
                join customer in _context.Customers.AsNoTracking() on invoice.CustomerId equals customer.CustomerId into customerJoin
                from customer in customerJoin.DefaultIfEmpty()
                join warehouse in _context.Warehouses.AsNoTracking() on unit.WarehouseId equals warehouse.WarehouseId into warehouseJoin
                from warehouse in warehouseJoin.DefaultIfEmpty()
                select new TrackTraceInfoRowViewModel
                {
                    ItemUnitId = unit.Id,
                    SourceType = "sales-invoice",
                    SourceTitle = "فاتورة مبيعات",
                    DocumentId = link.SIId,
                    SourceLineId = null,
                    Uid = unit.Uid,
                    Gtin = unit.Gtin,
                    SerialNo = unit.SerialNo,
                    ProdId = unit.ProdId,
                    ProductName = product != null ? (product.ProdName ?? string.Empty) : string.Empty,
                    BatchNo = !string.IsNullOrWhiteSpace(unit.BatchNo) ? unit.BatchNo : line.BatchNo,
                    Expiry = unit.Expiry ?? line.Expiry,
                    Status = unit.Status,
                    WarehouseName = warehouse != null ? warehouse.WarehouseName : string.Empty,
                    PIId = link.SIId,
                    LineNo = link.LineNo,
                    InvoiceDate = invoice.SIDate,
                    SupplierName = customer != null ? customer.CustomerName : string.Empty,
                    CurrentSourceType = unit.CurrentSourceType,
                    CurrentSourceId = unit.CurrentSourceId,
                    CurrentSourceLineNo = unit.CurrentSourceLineNo,
                    CreatedAt = unit.CreatedAt
                };

            var purchaseReturns =
                from link in _context.PurchaseReturnLineUnits.AsNoTracking()
                join unit in _context.ItemUnits.AsNoTracking() on link.ItemUnitId equals unit.Id
                join line in _context.PurchaseReturnLines.AsNoTracking() on new { link.PRetId, link.LineNo } equals new { line.PRetId, line.LineNo }
                join header in _context.PurchaseReturns.AsNoTracking() on link.PRetId equals header.PRetId
                join product in _context.Products.AsNoTracking() on unit.ProdId equals product.ProdId into productJoin
                from product in productJoin.DefaultIfEmpty()
                join customer in _context.Customers.AsNoTracking() on header.CustomerId equals customer.CustomerId into customerJoin
                from customer in customerJoin.DefaultIfEmpty()
                join warehouse in _context.Warehouses.AsNoTracking() on unit.WarehouseId equals warehouse.WarehouseId into warehouseJoin
                from warehouse in warehouseJoin.DefaultIfEmpty()
                select new TrackTraceInfoRowViewModel
                {
                    ItemUnitId = unit.Id,
                    SourceType = "purchase-return",
                    SourceTitle = "مرتجع مشتريات",
                    DocumentId = link.PRetId,
                    SourceLineId = null,
                    Uid = unit.Uid,
                    Gtin = unit.Gtin,
                    SerialNo = unit.SerialNo,
                    ProdId = unit.ProdId,
                    ProductName = product != null ? (product.ProdName ?? string.Empty) : string.Empty,
                    BatchNo = !string.IsNullOrWhiteSpace(unit.BatchNo) ? unit.BatchNo : line.BatchNo,
                    Expiry = unit.Expiry ?? line.Expiry,
                    Status = unit.Status,
                    WarehouseName = warehouse != null ? warehouse.WarehouseName : string.Empty,
                    PIId = link.PRetId,
                    LineNo = link.LineNo,
                    InvoiceDate = header.PRetDate,
                    SupplierName = customer != null ? customer.CustomerName : string.Empty,
                    CurrentSourceType = unit.CurrentSourceType,
                    CurrentSourceId = unit.CurrentSourceId,
                    CurrentSourceLineNo = unit.CurrentSourceLineNo,
                    CreatedAt = unit.CreatedAt
                };

            var salesReturns =
                from link in _context.SalesReturnLineUnits.AsNoTracking()
                join unit in _context.ItemUnits.AsNoTracking() on link.ItemUnitId equals unit.Id
                join line in _context.SalesReturnLines.AsNoTracking() on new { link.SRId, link.LineNo } equals new { line.SRId, line.LineNo }
                join header in _context.SalesReturns.AsNoTracking() on link.SRId equals header.SRId
                join product in _context.Products.AsNoTracking() on unit.ProdId equals product.ProdId into productJoin
                from product in productJoin.DefaultIfEmpty()
                join customer in _context.Customers.AsNoTracking() on header.CustomerId equals customer.CustomerId into customerJoin
                from customer in customerJoin.DefaultIfEmpty()
                join warehouse in _context.Warehouses.AsNoTracking() on unit.WarehouseId equals warehouse.WarehouseId into warehouseJoin
                from warehouse in warehouseJoin.DefaultIfEmpty()
                select new TrackTraceInfoRowViewModel
                {
                    ItemUnitId = unit.Id,
                    SourceType = "sales-return",
                    SourceTitle = "مرتجع مبيعات",
                    DocumentId = link.SRId,
                    SourceLineId = null,
                    Uid = unit.Uid,
                    Gtin = unit.Gtin,
                    SerialNo = unit.SerialNo,
                    ProdId = unit.ProdId,
                    ProductName = product != null ? (product.ProdName ?? string.Empty) : string.Empty,
                    BatchNo = !string.IsNullOrWhiteSpace(unit.BatchNo) ? unit.BatchNo : line.BatchNo,
                    Expiry = unit.Expiry ?? line.Expiry,
                    Status = unit.Status,
                    WarehouseName = warehouse != null ? warehouse.WarehouseName : string.Empty,
                    PIId = link.SRId,
                    LineNo = link.LineNo,
                    InvoiceDate = header.SRDate,
                    SupplierName = customer != null ? customer.CustomerName : string.Empty,
                    CurrentSourceType = unit.CurrentSourceType,
                    CurrentSourceId = unit.CurrentSourceId,
                    CurrentSourceLineNo = unit.CurrentSourceLineNo,
                    CreatedAt = unit.CreatedAt
                };

            var stockTransfers =
                from link in _context.StockTransferLineUnits.AsNoTracking()
                join unit in _context.ItemUnits.AsNoTracking() on link.ItemUnitId equals unit.Id
                join line in _context.StockTransferLines.AsNoTracking() on link.StockTransferLineId equals line.Id
                join header in _context.StockTransfers.AsNoTracking() on line.StockTransferId equals header.Id
                join product in _context.Products.AsNoTracking() on unit.ProdId equals product.ProdId into productJoin
                from product in productJoin.DefaultIfEmpty()
                join warehouse in _context.Warehouses.AsNoTracking() on unit.WarehouseId equals warehouse.WarehouseId into warehouseJoin
                from warehouse in warehouseJoin.DefaultIfEmpty()
                join fromWarehouse in _context.Warehouses.AsNoTracking() on header.FromWarehouseId equals fromWarehouse.WarehouseId into fromJoin
                from fromWarehouse in fromJoin.DefaultIfEmpty()
                join toWarehouse in _context.Warehouses.AsNoTracking() on header.ToWarehouseId equals toWarehouse.WarehouseId into toJoin
                from toWarehouse in toJoin.DefaultIfEmpty()
                select new TrackTraceInfoRowViewModel
                {
                    ItemUnitId = unit.Id,
                    SourceType = "stock-transfer",
                    SourceTitle = "تحويل مخزني",
                    DocumentId = header.Id,
                    SourceLineId = link.StockTransferLineId,
                    Uid = unit.Uid,
                    Gtin = unit.Gtin,
                    SerialNo = unit.SerialNo,
                    ProdId = unit.ProdId,
                    ProductName = product != null ? (product.ProdName ?? string.Empty) : string.Empty,
                    BatchNo = !string.IsNullOrWhiteSpace(unit.BatchNo) ? unit.BatchNo : (line.Batch != null ? line.Batch.BatchNo : null),
                    Expiry = unit.Expiry ?? (line.Batch != null ? line.Batch.Expiry : null),
                    Status = unit.Status,
                    WarehouseName = warehouse != null ? warehouse.WarehouseName : string.Empty,
                    PIId = header.Id,
                    LineNo = line.LineNo,
                    InvoiceDate = header.TransferDate,
                    SupplierName = $"{(fromWarehouse != null ? fromWarehouse.WarehouseName : "—")} ← {(toWarehouse != null ? toWarehouse.WarehouseName : "—")}",
                    CurrentSourceType = unit.CurrentSourceType,
                    CurrentSourceId = unit.CurrentSourceId,
                    CurrentSourceLineNo = unit.CurrentSourceLineNo,
                    CreatedAt = unit.CreatedAt
                };

            var stockAdjustments =
                from link in _context.StockAdjustmentLineUnits.AsNoTracking()
                join unit in _context.ItemUnits.AsNoTracking() on link.ItemUnitId equals unit.Id
                join line in _context.StockAdjustmentLines.AsNoTracking() on link.StockAdjustmentLineId equals line.Id
                join header in _context.StockAdjustments.AsNoTracking() on line.StockAdjustmentId equals header.Id
                join product in _context.Products.AsNoTracking() on unit.ProdId equals product.ProdId into productJoin
                from product in productJoin.DefaultIfEmpty()
                join warehouse in _context.Warehouses.AsNoTracking() on unit.WarehouseId equals warehouse.WarehouseId into warehouseJoin
                from warehouse in warehouseJoin.DefaultIfEmpty()
                select new TrackTraceInfoRowViewModel
                {
                    ItemUnitId = unit.Id,
                    SourceType = "stock-adjustment",
                    SourceTitle = "تسوية مخزنية",
                    DocumentId = header.Id,
                    SourceLineId = link.StockAdjustmentLineId,
                    Uid = unit.Uid,
                    Gtin = unit.Gtin,
                    SerialNo = unit.SerialNo,
                    ProdId = unit.ProdId,
                    ProductName = product != null ? (product.ProdName ?? string.Empty) : string.Empty,
                    BatchNo = !string.IsNullOrWhiteSpace(unit.BatchNo) ? unit.BatchNo : (line.Batch != null ? line.Batch.BatchNo : null),
                    Expiry = unit.Expiry ?? (line.Batch != null ? line.Batch.Expiry : null),
                    Status = unit.Status,
                    WarehouseName = warehouse != null ? warehouse.WarehouseName : string.Empty,
                    PIId = header.Id,
                    LineNo = line.Id,
                    InvoiceDate = header.AdjustmentDate,
                    SupplierName = warehouse != null ? warehouse.WarehouseName : string.Empty,
                    CurrentSourceType = unit.CurrentSourceType,
                    CurrentSourceId = unit.CurrentSourceId,
                    CurrentSourceLineNo = unit.CurrentSourceLineNo,
                    CreatedAt = unit.CreatedAt
                };

            var query = purchaseInvoices
                .Concat(salesInvoices)
                .Concat(purchaseReturns)
                .Concat(salesReturns)
                .Concat(stockTransfers)
                .Concat(stockAdjustments);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var trimmed = search.Trim();
                query = query.Where(x =>
                    x.Uid.Contains(trimmed) ||
                    (x.SerialNo != null && x.SerialNo.Contains(trimmed)) ||
                    (x.Gtin != null && x.Gtin.Contains(trimmed)) ||
                    x.ProductName.Contains(trimmed) ||
                    (x.SupplierName != null && x.SupplierName.Contains(trimmed)));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(x => x.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(batchNo))
            {
                var batch = batchNo.Trim();
                query = query.Where(x => x.BatchNo != null && x.BatchNo.Contains(batch));
            }

            if (!string.IsNullOrWhiteSpace(sourceType))
            {
                query = query.Where(x => x.SourceType == sourceType);
            }

            if (documentId.HasValue && documentId.Value > 0)
            {
                query = query.Where(x => x.DocumentId == documentId.Value);
            }

            if (lineNo.HasValue && lineNo.Value > 0)
            {
                query = query.Where(x => x.LineNo == lineNo.Value);
            }

            if (lineId.HasValue && lineId.Value > 0)
            {
                query = query.Where(x => x.SourceLineId == lineId.Value);
            }

            var totalUnits = await query.CountAsync();
            var inStockUnits = await query.CountAsync(x => x.Status == "InStock");
            var soldUnits = await query.CountAsync(x => x.Status == "Sold");

            query = query.OrderByDescending(x => x.PIId)
                         .ThenBy(x => x.LineNo)
                         .ThenByDescending(x => x.ItemUnitId);

            var result = await PagedResult<TrackTraceInfoRowViewModel>.CreateAsync(
                query,
                page,
                pageSize,
                "ItemUnitId",
                true,
                search,
                "all");

            var vm = new TrackTraceInfoIndexViewModel
            {
                Result = result,
                Search = search,
                Status = status,
                BatchNo = batchNo,
                SourceType = sourceType,
                SourceTitle = ResolveSourceTitle(sourceType),
                DocumentId = documentId,
                PIId = piId,
                LineNo = lineNo,
                SourceLineId = lineId,
                TotalUnits = totalUnits,
                InStockUnits = inStockUnits,
                SoldUnits = soldUnits
            };

            return View(vm);
        }

        private static string? ResolveSourceTitle(string? sourceType)
        {
            return sourceType switch
            {
                "purchase-invoice" => "فاتورة مشتريات",
                "sales-invoice" => "فاتورة مبيعات",
                "purchase-return" => "مرتجع مشتريات",
                "sales-return" => "مرتجع مبيعات",
                "stock-transfer" => "تحويل مخزني",
                "stock-adjustment" => "تسوية مخزنية",
                _ => null
            };
        }

        public async Task<IActionResult> Queue(
            string? search,
            string? status,
            string? eventType,
            int page = 1,
            int pageSize = 50)
        {
            var query =
                from q in _context.TrackTraceQueues.AsNoTracking()
                join unit in _context.ItemUnits.AsNoTracking() on q.ItemUnitId equals unit.Id into unitJoin
                from unit in unitJoin.DefaultIfEmpty()
                join product in _context.Products.AsNoTracking() on unit.ProdId equals product.ProdId into productJoin
                from product in productJoin.DefaultIfEmpty()
                select new TrackTraceQueueRowViewModel
                {
                    Id = q.Id,
                    EventType = q.EventType,
                    Status = q.Status,
                    ItemUnitId = q.ItemUnitId,
                    Uid = unit != null ? unit.Uid : null,
                    ProductName = product != null ? product.ProdName : null,
                    RetryCount = q.RetryCount,
                    LastError = q.LastError,
                    CreatedAt = q.CreatedAt,
                    NextRetryAt = q.NextRetryAt,
                    SentAt = q.SentAt
                };

            if (!string.IsNullOrWhiteSpace(search))
            {
                var trimmed = search.Trim();
                query = query.Where(x =>
                    (x.Uid != null && x.Uid.Contains(trimmed)) ||
                    (x.ProductName != null && x.ProductName.Contains(trimmed)) ||
                    x.Id.ToString().Contains(trimmed));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(x => x.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(eventType))
            {
                query = query.Where(x => x.EventType == eventType);
            }

            var pendingCount = await query.CountAsync(x => x.Status == "Pending");
            var failedCount = await query.CountAsync(x => x.Status == "Failed");
            var sentCount = await query.CountAsync(x => x.Status == "Sent");

            query = query.OrderByDescending(x => x.Id);

            var result = await PagedResult<TrackTraceQueueRowViewModel>.CreateAsync(
                query,
                page,
                pageSize,
                "Id",
                true,
                search,
                "all");

            var vm = new TrackTraceQueueIndexViewModel
            {
                Result = result,
                Search = search,
                Status = status,
                EventType = eventType,
                PendingCount = pendingCount,
                FailedCount = failedCount,
                SentCount = sentCount
            };

            return View(vm);
        }
    }
}
