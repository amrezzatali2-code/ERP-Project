using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ERP.Data;
using ERP.Models;
using Microsoft.EntityFrameworkCore;

namespace ERP.Services
{
    /// <summary>نتيجة إصلاح فواتير مبيعات قديمة تم ترحيلها بدون إنشاء حركة مخزون بيع.</summary>
    public class SalesInvoiceMissingStockRepairResult
    {
        public int Fixed { get; set; }
        public int SkippedNotFound { get; set; }
        public int SkippedAlreadyHealthy { get; set; }
        public int SkippedPartialLedger { get; set; }
        public int Errors { get; set; }
        public List<string> Messages { get; } = new();
    }

    /// <summary>
    /// يعيد بناء حركات StockLedger و StockFifoMap وتحديث StockBatches
    /// لفواتير المبيعات القديمة التي لها سطور بيع ولا يوجد لها أي قيد مخزني من نوع Sales.
    /// </summary>
    public class SalesInvoiceMissingStockRepairService
    {
        private readonly AppDbContext _db;

        public SalesInvoiceMissingStockRepairService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<bool> HasMissingSalesOutLedgersAsync(int salesInvoiceId, CancellationToken ct = default)
        {
            var hasLines = await _db.SalesInvoiceLines
                .AsNoTracking()
                .AnyAsync(x => x.SIId == salesInvoiceId, ct);
            if (!hasLines)
                return false;

            var hasSalesLedgers = await _db.StockLedger
                .AsNoTracking()
                .AnyAsync(x => x.SourceType == "Sales" && x.SourceId == salesInvoiceId, ct);

            return !hasSalesLedgers;
        }

        public async Task<SalesInvoiceMissingStockRepairResult> RepairMissingSalesOutLedgersAsync(
            int? salesInvoiceId,
            CancellationToken ct = default)
        {
            var result = new SalesInvoiceMissingStockRepairResult();

            var candidateIds = salesInvoiceId.HasValue
                ? new List<int> { salesInvoiceId.Value }
                : await _db.SalesInvoices
                    .AsNoTracking()
                    .Where(si => _db.SalesInvoiceLines.Any(l => l.SIId == si.SIId))
                    .Where(si => !_db.StockLedger.Any(sl => sl.SourceType == "Sales" && sl.SourceId == si.SIId))
                    .OrderBy(si => si.SIId)
                    .Select(si => si.SIId)
                    .ToListAsync(ct);

            foreach (var id in candidateIds.Distinct())
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                try
                {
                    var invoice = await _db.SalesInvoices.FirstOrDefaultAsync(x => x.SIId == id, ct);
                    if (invoice == null)
                    {
                        result.SkippedNotFound++;
                        result.Messages.Add($"الفاتورة {id}: غير موجودة.");
                        await tx.RollbackAsync(ct);
                        continue;
                    }

                    var lines = await _db.SalesInvoiceLines
                        .Where(x => x.SIId == id)
                        .OrderBy(x => x.LineNo)
                        .ToListAsync(ct);

                    if (lines.Count == 0)
                    {
                        result.SkippedAlreadyHealthy++;
                        await tx.RollbackAsync(ct);
                        continue;
                    }

                    var existingSalesLedgers = await _db.StockLedger
                        .Where(x => x.SourceType == "Sales" && x.SourceId == id)
                        .ToListAsync(ct);

                    if (existingSalesLedgers.Count > 0)
                    {
                        bool hasAllLines = lines.All(line => existingSalesLedgers.Any(lg => lg.SourceLine == line.LineNo && lg.QtyOut > 0));
                        if (hasAllLines)
                        {
                            result.SkippedAlreadyHealthy++;
                        }
                        else
                        {
                            result.SkippedPartialLedger++;
                            result.Messages.Add($"الفاتورة {id}: بها حركة مخزون جزئية، تحتاج مراجعة يدوية.");
                        }

                        await tx.RollbackAsync(ct);
                        continue;
                    }

                    foreach (var line in lines)
                    {
                        if (line.Qty <= 0)
                            throw new InvalidOperationException($"الفاتورة {id} السطر {line.LineNo}: الكمية غير صالحة.");

                        if (!line.Expiry.HasValue)
                            throw new InvalidOperationException($"الفاتورة {id} السطر {line.LineNo}: تاريخ الصلاحية غير موجود.");

                        var normalizedBatchNo = (line.BatchNo ?? string.Empty).Trim();
                        var normalizedExpiry = line.Expiry.Value.Date;

                        var outLedger = new StockLedger
                        {
                            TranDate = invoice.PostedAt ?? invoice.SIDate,
                            WarehouseId = invoice.WarehouseId,
                            ProdId = line.ProdId,
                            BatchNo = normalizedBatchNo,
                            Expiry = normalizedExpiry,
                            BatchId = null,
                            QtyIn = 0,
                            QtyOut = line.Qty,
                            UnitCost = 0m,
                            TotalCost = 0m,
                            RemainingQty = null,
                            SourceType = "Sales",
                            SourceId = invoice.SIId,
                            SourceLine = line.LineNo,
                            Note = $"Legacy sales repair for SI:{invoice.SIId} Line:{line.LineNo}"
                        };

                        _db.StockLedger.Add(outLedger);
                        await _db.SaveChangesAsync(ct);

                        int need = line.Qty;
                        decimal costTotal = 0m;

                        var matchingInflows = await _db.StockLedger
                            .Where(x =>
                                x.WarehouseId == invoice.WarehouseId &&
                                x.ProdId == line.ProdId &&
                                x.QtyIn > 0 &&
                                (x.RemainingQty ?? 0) > 0 &&
                                (x.BatchNo ?? "").Trim() == normalizedBatchNo &&
                                ((x.Expiry.HasValue ? x.Expiry.Value.Date : (DateTime?)null) == normalizedExpiry))
                            .OrderBy(x => x.Expiry)
                            .ThenBy(x => x.EntryId)
                            .ToListAsync(ct);

                        foreach (var inLedger in matchingInflows)
                        {
                            if (need <= 0)
                                break;

                            int available = inLedger.RemainingQty ?? 0;
                            if (available <= 0)
                                continue;

                            int take = Math.Min(need, available);
                            inLedger.RemainingQty = available - take;

                            var inflowUnitCost = SalesFifoCostRepairService.InflowUnitCostForFifo(inLedger);
                            _db.Set<StockFifoMap>().Add(new StockFifoMap
                            {
                                OutEntryId = outLedger.EntryId,
                                InEntryId = inLedger.EntryId,
                                Qty = take,
                                UnitCost = inflowUnitCost
                            });

                            costTotal += take * inflowUnitCost;
                            need -= take;
                        }

                        if (need > 0)
                        {
                            var fallbackInflows = await _db.StockLedger
                                .Where(x =>
                                    x.WarehouseId == invoice.WarehouseId &&
                                    x.ProdId == line.ProdId &&
                                    x.QtyIn > 0 &&
                                    (x.RemainingQty ?? 0) > 0)
                                .OrderBy(x => x.Expiry)
                                .ThenBy(x => x.EntryId)
                                .ToListAsync(ct);

                            foreach (var inLedger in fallbackInflows)
                            {
                                if (need <= 0)
                                    break;

                                int available = inLedger.RemainingQty ?? 0;
                                if (available <= 0)
                                    continue;

                                int take = Math.Min(need, available);
                                inLedger.RemainingQty = available - take;

                                var inflowUnitCost = SalesFifoCostRepairService.InflowUnitCostForFifo(inLedger);
                                _db.Set<StockFifoMap>().Add(new StockFifoMap
                                {
                                    OutEntryId = outLedger.EntryId,
                                    InEntryId = inLedger.EntryId,
                                    Qty = take,
                                    UnitCost = inflowUnitCost
                                });

                                costTotal += take * inflowUnitCost;
                                need -= take;
                            }
                        }

                        if (need > 0)
                            throw new InvalidOperationException($"الفاتورة {id} السطر {line.LineNo}: لا يوجد رصيد كافٍ لإعادة بناء حركة المخزون. المتبقي {need}.");

                        var batchRow = await _db.StockBatches.FirstOrDefaultAsync(x =>
                            x.WarehouseId == invoice.WarehouseId &&
                            x.ProdId == line.ProdId &&
                            x.BatchNo == normalizedBatchNo &&
                            x.Expiry.HasValue &&
                            x.Expiry.Value.Date == normalizedExpiry, ct);

                        if (batchRow == null)
                            throw new InvalidOperationException($"الفاتورة {id} السطر {line.LineNo}: صف التشغيلة غير موجود في StockBatches.");

                        if (batchRow.QtyOnHand < line.Qty)
                            throw new InvalidOperationException($"الفاتورة {id} السطر {line.LineNo}: الرصيد الحالي في StockBatches أقل من كمية الفاتورة.");

                        batchRow.QtyOnHand -= line.Qty;
                        batchRow.UpdatedAt = DateTime.UtcNow;
                        batchRow.Note = $"Legacy SI:{invoice.SIId} Line:{line.LineNo} (-{line.Qty})";

                        var costPerUnit = line.Qty > 0
                            ? Math.Round(costTotal / line.Qty, 2, MidpointRounding.AwayFromZero)
                            : 0m;

                        outLedger.UnitCost = costPerUnit;
                        outLedger.TotalCost = Math.Round(costTotal, 2, MidpointRounding.AwayFromZero);

                        line.CostPerUnit = costPerUnit;
                        line.CostTotal = Math.Round(costTotal, 2, MidpointRounding.AwayFromZero);
                        line.ProfitValue = Math.Round(line.LineNetTotal - line.CostTotal, 2, MidpointRounding.AwayFromZero);
                        line.ProfitPercent = line.LineNetTotal > 0m
                            ? Math.Round((line.ProfitValue / line.LineNetTotal) * 100m, 2, MidpointRounding.AwayFromZero)
                            : 0m;

                        await _db.SaveChangesAsync(ct);
                    }

                    await tx.CommitAsync(ct);
                    result.Fixed++;
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync(ct);
                    result.Errors++;
                    result.Messages.Add($"الفاتورة {id}: {ex.Message}");
                }
            }

            return result;
        }
    }
}
