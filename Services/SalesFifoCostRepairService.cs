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
    /// <summary>نتيجة تشغيل إصلاح تكلفة FIFO لحركات مبيعات قديمة كانت بتكلفة صفر.</summary>
    public class SalesFifoCostRepairResult
    {
        public int Fixed { get; set; }
        public int SkippedHadFifoMap { get; set; }
        public int SkippedStillZero { get; set; }
        public int Errors { get; set; }
        public List<string> Messages { get; } = new();
    }

    /// <summary>
    /// يعيد تطبيق منطق FIFO (الصارم ثم الاحتياطي) على حركات خروج مبيعات مسجّلة بتكلفة صفر
    /// ولم تُنشئ StockFifoMap — شائع بعد ترحيل أول مدة بدون تشغيلة/صلاحية تطابق سطر البيع.
    /// </summary>
    public class SalesFifoCostRepairService
    {
        private readonly AppDbContext _db;

        public SalesFifoCostRepairService(AppDbContext db)
        {
            _db = db;
        }

        /// <summary>نفس منطق SalesInvoicesController: تكلفة وحدة الدخلة من TotalCost عند غياب UnitCost.</summary>
        public static decimal InflowUnitCostForFifo(StockLedger inL)
        {
            if (inL.QtyIn <= 0) return inL.UnitCost;
            if (inL.UnitCost != 0m) return inL.UnitCost;
            if (inL.TotalCost.HasValue && inL.TotalCost.Value != 0m)
                return Math.Round(inL.TotalCost.Value / inL.QtyIn, 4, MidpointRounding.AwayFromZero);
            return 0m;
        }

        /// <param name="maxRows">حد أقصى لعدد الحركات المعالجة (null = بدون حد).</param>
        public async Task<SalesFifoCostRepairResult> RepairZeroCostSalesOutLedgersAsync(int? maxRows, CancellationToken ct = default)
        {
            var result = new SalesFifoCostRepairResult();

            var withMapIds = await _db.Set<StockFifoMap>().AsNoTracking()
                .Select(f => f.OutEntryId)
                .Distinct()
                .ToListAsync(ct);
            var withMap = withMapIds.ToHashSet();

            IQueryable<StockLedger> query = _db.StockLedger
                .Where(sl => sl.SourceType == "Sales" && sl.QtyOut > 0
                    && sl.UnitCost == 0m
                    && (sl.TotalCost == null || sl.TotalCost == 0m))
                .OrderBy(sl => sl.EntryId);

            if (maxRows.HasValue && maxRows.Value > 0)
                query = query.Take(maxRows.Value);

            var candidates = await query.ToListAsync(ct);

            foreach (var outLedger in candidates)
            {
                if (withMap.Contains(outLedger.EntryId))
                {
                    result.SkippedHadFifoMap++;
                    continue;
                }

                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                try
                {
                    var outRow = await _db.StockLedger.FirstAsync(x => x.EntryId == outLedger.EntryId, ct);

                    int qtyDelta = outRow.QtyOut;
                    int need = qtyDelta;
                    decimal costTotal = 0m;

                    string batchNo = outRow.BatchNo ?? "";
                    DateTime expiry = outRow.Expiry?.Date ?? DateTime.UtcNow.Date;

                    int warehouseId = outRow.WarehouseId;
                    int prodId = outRow.ProdId;

                    var inLedgers = await _db.StockLedger
                        .Where(x =>
                            x.WarehouseId == warehouseId &&
                            x.ProdId == prodId &&
                            x.QtyIn > 0 &&
                            (x.RemainingQty ?? 0) > 0 &&
                            (x.BatchNo ?? "").Trim() == (batchNo ?? "").Trim() &&
                            ((x.Expiry.HasValue ? x.Expiry.Value.Date : (DateTime?)null) == expiry.Date))
                        .OrderBy(x => x.Expiry)
                        .ThenBy(x => x.EntryId)
                        .ToListAsync(ct);

                    foreach (var inL in inLedgers)
                    {
                        if (need <= 0) break;
                        int avail = inL.RemainingQty ?? 0;
                        if (avail <= 0) continue;
                        int take = Math.Min(need, avail);
                        inL.RemainingQty = avail - take;
                        var inUc = InflowUnitCostForFifo(inL);
                        _db.Set<StockFifoMap>().Add(new StockFifoMap
                        {
                            OutEntryId = outRow.EntryId,
                            InEntryId = inL.EntryId,
                            Qty = take,
                            UnitCost = inUc
                        });
                        costTotal += take * inUc;
                        need -= take;
                    }

                    if (need > 0)
                    {
                        var fallbackInLedgers = await _db.StockLedger
                            .Where(x =>
                                x.WarehouseId == warehouseId &&
                                x.ProdId == prodId &&
                                x.QtyIn > 0 &&
                                (x.RemainingQty ?? 0) > 0)
                            .OrderBy(x => x.Expiry)
                            .ThenBy(x => x.EntryId)
                            .ToListAsync(ct);

                        foreach (var inL in fallbackInLedgers)
                        {
                            if (need <= 0) break;
                            int avail = inL.RemainingQty ?? 0;
                            if (avail <= 0) continue;
                            int take = Math.Min(need, avail);
                            inL.RemainingQty = avail - take;
                            var inUcFb = InflowUnitCostForFifo(inL);
                            _db.Set<StockFifoMap>().Add(new StockFifoMap
                            {
                                OutEntryId = outRow.EntryId,
                                InEntryId = inL.EntryId,
                                Qty = take,
                                UnitCost = inUcFb
                            });
                            costTotal += take * inUcFb;
                            need -= take;
                        }
                    }

                    if (need > 0)
                    {
                        await tx.RollbackAsync(ct);
                        result.SkippedStillZero++;
                        result.Messages.Add($"قيد خروج {outRow.EntryId}: لا يوجد رصيد كافٍ في الدخلات بعد FIFO (متبقي {need} علبة) — تحقق من تطابق المخزن والصنف.");
                        continue;
                    }

                    if (costTotal <= 0m)
                    {
                        await tx.RollbackAsync(ct);
                        result.SkippedStillZero++;
                        continue;
                    }

                    decimal costPerUnit = qtyDelta > 0 ? Math.Round(costTotal / qtyDelta, 2, MidpointRounding.AwayFromZero) : 0m;
                    outRow.UnitCost = costPerUnit;
                    outRow.TotalCost = Math.Round(costTotal, 2, MidpointRounding.AwayFromZero);

                    await _db.SaveChangesAsync(ct);

                    int siId = outRow.SourceId;
                    int lineNo = outRow.SourceLine;
                    var outsForLine = await _db.StockLedger
                        .Where(s => s.SourceType == "Sales" && s.SourceId == siId && s.SourceLine == lineNo && s.QtyOut > 0)
                        .ToListAsync(ct);
                    decimal lineCostTotal = outsForLine.Sum(o => o.TotalCost ?? (o.UnitCost * o.QtyOut));

                    var line = await _db.SalesInvoiceLines.FirstOrDefaultAsync(l => l.SIId == siId && l.LineNo == lineNo, ct);
                    if (line != null)
                    {
                        line.CostTotal = Math.Round(lineCostTotal, 2);
                        line.CostPerUnit = line.Qty > 0 ? Math.Round(lineCostTotal / line.Qty, 2) : 0m;
                        line.ProfitValue = Math.Round(line.LineNetTotal - line.CostTotal, 2);
                        line.ProfitPercent = line.LineNetTotal > 0 ? Math.Round(line.ProfitValue / line.LineNetTotal * 100m, 2) : 0m;
                    }

                    await _db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    result.Fixed++;
                    withMap.Add(outRow.EntryId);
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync(ct);
                    result.Errors++;
                    result.Messages.Add($"EntryId={outLedger.EntryId}: {ex.Message}");
                }
            }

            return result;
        }
    }
}
