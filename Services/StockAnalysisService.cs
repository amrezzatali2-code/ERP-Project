using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ERP.Data;
using ERP.Models;

namespace ERP.Services
{
    /// <summary>
    /// خدمة تحليل المخزون Stock Ledger Analysis
    /// تعتمد على جدول StockLedger بالكامل لحساب:
    /// - الكمية الحالية
    /// - متوسط تكلفة الوحدة
    /// - الخصم المرجّح القديم والجديد
    /// - إجمالي قيمة المخزون
    /// </summary>
    public class StockAnalysisService
    {
        private readonly AppDbContext _context;

        public StockAnalysisService(AppDbContext context)
        {
            _context = context;
        }

        // ============================================================
        // 1) الكمية الحالية للصنف (Current Qty)
        // ============================================================
        public async Task<int> GetCurrentQtyAsync(int prodId, int? warehouseId = null)
        {
            var query = _context.StockLedger.Where(x => x.ProdId == prodId);

            if (warehouseId.HasValue)
                query = query.Where(x => x.WarehouseId == warehouseId);

            int totalIn = await query.SumAsync(x => x.QtyIn);
            int totalOut = await query.SumAsync(x => x.QtyOut);

            return totalIn - totalOut;
        }

        // ============================================================
        // 2) الخصم المرجح القديم (قبل البيع = على كل الكميات الداخلة)
        // ============================================================
        public async Task<decimal> GetWeightedPurchaseDiscountOldAsync(int prodId)
        {
            var purchases = await _context.StockLedger
                .Where(x => x.ProdId == prodId && x.QtyIn > 0)
                .Include(x => x.Batch)
                .ToListAsync();

            if (!purchases.Any())
                return 0;

            decimal totalQty = purchases.Sum(x => x.QtyIn);
            if (totalQty == 0) return 0;

            decimal weighted =
                purchases.Sum(x =>
                    x.QtyIn * (x.Batch?.PurchaseDiscountPct ?? 0m)
                );

            return weighted / totalQty;
        }

        // ============================================================
        // 3) الخصم المرجح الحالي بعد البيع
        // يعتمد فقط على الدُفعات المتبقية في المخزون
        // ============================================================
        public async Task<decimal> GetWeightedPurchaseDiscountCurrentAsync(int prodId)
        {
            var entries = await _context.StockLedger
                .Where(x => x.ProdId == prodId && x.RemainingQty > 0)
                .Include(x => x.Batch)
                .ToListAsync();

            if (!entries.Any())
                return 0;

            decimal totalRemaining = entries.Sum(x => x.RemainingQty ?? 0);
            if (totalRemaining == 0) return 0;

            decimal weighted =
                entries.Sum(x =>
                    (x.RemainingQty ?? 0) * (x.Batch?.PurchaseDiscountPct ?? 0m)
                );

            return weighted / totalRemaining;
        }

        // ============================================================
        // 4) متوسط تكلفة الوحدة (حسب FIFO)
        // ============================================================
        public async Task<decimal> GetAverageUnitCostAsync(int prodId)
        {
            var entries = await _context.StockLedger
                .Where(x => x.ProdId == prodId && x.RemainingQty > 0)
                .ToListAsync();

            if (!entries.Any()) return 0;

            decimal totalQty = entries.Sum(x => x.RemainingQty ?? 0);
            if (totalQty == 0) return 0;

            decimal totalValue =
                entries.Sum(x => (x.RemainingQty ?? 0) * x.UnitCost);

            return totalValue / totalQty;
        }

        // ============================================================
        // 5) إجمالي قيمة المخزون (Current Stock Value)
        // ============================================================
        public async Task<decimal> GetStockValueAsync(int prodId)
        {
            var entries = await _context.StockLedger
                .Where(x => x.ProdId == prodId && x.RemainingQty > 0)
                .ToListAsync();

            if (!entries.Any()) return 0;

            return entries.Sum(x =>
                (x.RemainingQty ?? 0) * x.UnitCost
            );
        }

        // ============================================================
        // 6) تقرير شامل للصنف Stock Summary Report
        // ============================================================
        public async Task<StockSummaryDto> GetProductStockSummaryAsync(int prodId)
        {
            int qty = await GetCurrentQtyAsync(prodId);
            decimal avgCost = await GetAverageUnitCostAsync(prodId);
            decimal oldDisc = await GetWeightedPurchaseDiscountOldAsync(prodId);
            decimal curDisc = await GetWeightedPurchaseDiscountCurrentAsync(prodId);
            decimal value = await GetStockValueAsync(prodId);

            return new StockSummaryDto
            {
                ProdId = prodId,
                CurrentQty = qty,
                AverageUnitCost = avgCost,
                WeightedOldDiscount = oldDisc,
                WeightedCurrentDiscount = curDisc,
                StockValue = value
            };
        }
    }

    // DTO لعرض التقرير
    public class StockSummaryDto
    {
        public int ProdId { get; set; }
        public int CurrentQty { get; set; }
        public decimal AverageUnitCost { get; set; }
        public decimal WeightedOldDiscount { get; set; }
        public decimal WeightedCurrentDiscount { get; set; }
        public decimal StockValue { get; set; }
    }
}
