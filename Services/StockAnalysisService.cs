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
        // 2) الخصم المرجّح القديم (قبل البيع = على كل الكميات الداخلة)
        // يعتمد على StockLedger (حركات الدخول) بشرط أن PurchaseDiscount = نسبة خصم الشراء
        // ============================================================
        public async Task<decimal> GetWeightedPurchaseDiscountOldAsync(int prodId)
        {
            // تعليق: نجمع الدخلات فقط (QtyIn > 0)
            // ونحسب المتوسط الموزون على QtyIn
            var rows = await _context.StockLedger
                .AsNoTracking()
                .Where(x => x.ProdId == prodId && x.QtyIn > 0)
                .Select(x => new
                {
                    qty = x.QtyIn,                               // متغير: كمية الدخلة
                    discPct = (decimal?)(x.PurchaseDiscount) ?? 0m // متغير: نسبة الخصم للدخلة (لازم تكون محفوظة وقت الترحيل)
                })
                .ToListAsync();

            if (rows.Count == 0) return 0m;

            decimal totalQty = rows.Sum(r => (decimal)r.qty);
            if (totalQty <= 0m) return 0m;

            decimal weighted = rows.Sum(r => ((decimal)r.qty) * r.discPct);
            return weighted / totalQty;
        }


        // ============================================================
        // 3) الخصم المرجح الحالي بعد البيع
        // يعتمد فقط على الدُخلات المتبقية (RemainingQty > 0)
        // ============================================================
        public async Task<decimal> GetWeightedPurchaseDiscountCurrentAsync(int prodId)
        {
            // تعليق: RemainingQty تُملأ فقط لسطور الدخول
            var rows = await _context.StockLedger
                .AsNoTracking()
                .Where(x => x.ProdId == prodId && (x.RemainingQty ?? 0) > 0)
                .Select(x => new
                {
                    remaining = (decimal)(x.RemainingQty ?? 0),      // متغير: المتبقي من الدخلة
                    discPct = (decimal?)(x.PurchaseDiscount) ?? 0m   // متغير: نسبة الخصم لنفس الدخلة
                })
                .ToListAsync();

            if (rows.Count == 0) return 0m;

            decimal totalRemaining = rows.Sum(r => r.remaining);
            if (totalRemaining <= 0m) return 0m;

            decimal weighted = rows.Sum(r => r.remaining * r.discPct);
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


        // =========================================================
        // ✅ GetSaleDiscountAsync
        // حساب خصم البيع النهائي قبل إضافة سطر البيع
        // المنطق:
        // 1) نحدد PolicyId للعميل (لو null => 1)
        // 2) نبحث عن WarehousePolicyRule (WarehouseId + PolicyId) ونأخذ ProfitPercent
        // 3) لو مفيش Rule => نرجع لـ Policy.DefaultProfitPercent
        // 4) خصم البيع = الخصم المرجح - ProfitPercent
        // 5) نطبق حدود الأمان (0..100) + MaxDiscountToCustomer إن وُجد
        // =========================================================
        public async Task<decimal> GetSaleDiscountAsync(
            int prodId,
            int warehouseId,
            int customerId,
            decimal weightedPurchaseDiscount)
        {
            // =========================
            // (1) تحقق من المدخلات
            // =========================
            if (warehouseId <= 0)
                return ClampPercent(weightedPurchaseDiscount); // تعليق: بدون مخزن لا يوجد سياسة مخزن

            // =========================
            // (2) تحديد PolicyId للعميل
            // - لو العميل مش موجود أو PolicyId = null => نثبت 1
            // =========================
            int policyId = 1; // متغير: السياسة الافتراضية (حسب اتفاقنا)

            if (customerId > 0)
            {
                var cPolicyId = await _context.Customers
                    .AsNoTracking()
                    .Where(c => c.CustomerId == customerId)
                    .Select(c => c.PolicyId)
                    .FirstOrDefaultAsync();

                if (cPolicyId.HasValue && cPolicyId.Value > 0)
                    policyId = cPolicyId.Value;
            }

            // =========================
            // (3) جلب Rule للمخزن+السياسة (إن وجدت)
            // =========================
            var rule = await _context.WarehousePolicyRules
                .AsNoTracking()
                .Where(r =>
                    r.WarehouseId == warehouseId &&
                    r.PolicyId == policyId &&
                    r.IsActive)
                .Select(r => new
                {
                    r.ProfitPercent,          // متغير: مكسب المخزن من الخصم المرجح
                    r.MaxDiscountToCustomer   // متغير: أقصى خصم مسموح للعميل (اختياري)
                })
                .FirstOrDefaultAsync();

            // =========================
            // (4) تحديد profitPercent
            // - من الـ Rule لو موجود
            // - وإلا من Policy.DefaultProfitPercent
            // =========================
            decimal profitPercent = 0m; // متغير: مكسب السياسة الذي سنخصمه من الخصم المرجح

            if (rule != null)
            {
                profitPercent = rule.ProfitPercent;
            }
            else
            {
                // لو مفيش Rule نستخدم DefaultProfitPercent من جدول Policy
                var policyDefaultProfit = await _context.Policies
                    .AsNoTracking()
                    .Where(p => p.PolicyId == policyId && p.IsActive)
                    .Select(p => (decimal?)p.DefaultProfitPercent)
                    .FirstOrDefaultAsync();

                profitPercent = policyDefaultProfit ?? 0m;
            }

            // حماية: لو profitPercent سالب
            if (profitPercent < 0) profitPercent = 0;

            // =========================
            // (5) حساب خصم البيع النهائي
            // خصم البيع = الخصم المرجح - مكسب السياسة
            // =========================
            decimal saleDiscount = weightedPurchaseDiscount - profitPercent;

            // =========================
            // (6) تطبيق أقصى خصم للعميل إن وُجد
            // - لو MaxDiscountToCustomer موجود: لا نسمح بتجاوزه
            // =========================
            if (rule?.MaxDiscountToCustomer.HasValue == true)
            {
                var maxAllowed = rule.MaxDiscountToCustomer.Value;
                if (saleDiscount > maxAllowed)
                    saleDiscount = maxAllowed;
            }

            // =========================
            // (7) حماية نهائية 0..100
            // =========================
            return ClampPercent(saleDiscount);
        }

        // =========================================================
        // دالة مساعدة: تثبيت النسبة بين 0 و 100
        // =========================================================
        private static decimal ClampPercent(decimal v)
        {
            if (v < 0m) return 0m;
            if (v > 100m) return 100m;
            return v;
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
