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
        // 2) الخصم المرجّح القديم (قبل البيع = على كل كميات الشراء الداخلة)
        // ✅ تعديل: يعتمد على كميات المشتريات فقط (SourceType == "Purchase")
        // ============================================================
        public async Task<decimal> GetWeightedPurchaseDiscountOldAsync(int prodId)
        {
            // تعليق: نجمع "مشتريات فقط" + الدخلات فقط (QtyIn > 0)
            // ونحسب المتوسط الموزون على QtyIn
            var rows = await _context.StockLedger
                .AsNoTracking()
                .Where(x =>
                    x.ProdId == prodId &&
                    x.SourceType == "Purchase" &&   // ✅ فلترة: مشتريات فقط
                    x.QtyIn > 0)
                .Select(x => new
                {
                    qty = x.QtyIn,                                 // متغير: كمية الشراء الداخلة
                    discPct = (decimal?)(x.PurchaseDiscount) ?? 0m // متغير: نسبة خصم الشراء لهذه الدخلة
                })
                .ToListAsync();

            if (rows.Count == 0) return 0m;

            decimal totalQty = rows.Sum(r => (decimal)r.qty);
            if (totalQty <= 0m) return 0m;

            decimal weighted = rows.Sum(r => ((decimal)r.qty) * r.discPct);
            return ClampPercent(weighted / totalQty);
        }


        // ============================================================
        // 3) الخصم المرجح الحالي بعد البيع
        // ✅ يعتمد على المتبقي من مشتريات أو افتتاحي أو دخول تحويل (مطابق لتقرير أرصدة الأصناف مع التحويلات)
        // ============================================================
        public async Task<decimal> GetWeightedPurchaseDiscountCurrentAsync(int prodId)
        {
            // تعليق: RemainingQty تُملأ فقط لسطور الدخول
            // ✅ Purchase أو Opening أو TransferIn (خصم محفوظ على سطر الدخول)
            var rows = await _context.StockLedger
                .AsNoTracking()
                .Where(x =>
                    x.ProdId == prodId &&
                    (x.SourceType == "Purchase" || x.SourceType == "Opening" || x.SourceType == "TransferIn") &&
                    (x.RemainingQty ?? 0) > 0)
                .Select(x => new
                {
                    remaining = (decimal)(x.RemainingQty ?? 0),      // متغير: المتبقي من دخلة الشراء
                    discPct = (decimal?)(x.PurchaseDiscount) ?? 0m   // متغير: نسبة خصم الشراء لنفس الدخلة
                })
                .ToListAsync();

            if (rows.Count == 0) return 0m;

            decimal totalRemaining = rows.Sum(r => r.remaining);
            if (totalRemaining <= 0m) return 0m;
            // تجنّب القسمة على أعداد صغيرة جداً (قد تنتج أرقاماً خاطئة)
            if (totalRemaining < 0.001m) return 0m;

            decimal weighted = rows.Sum(r => r.remaining * r.discPct);
            decimal result = weighted / totalRemaining;
            return ClampPercent(result);
        }

        /// <summary>
        /// الخصم المرجح للصنف في مخزن معين (للاستخدام في التحويل بين المخازن).
        /// يشمل Purchase و Opening و TransferIn (دخول من تحويل مخزني مع خصم شراء محفوظ على السطر).
        /// </summary>
        public async Task<decimal> GetWeightedPurchaseDiscountForWarehouseAsync(int prodId, int warehouseId)
        {
            var rows = await _context.StockLedger
                .AsNoTracking()
                .Where(x =>
                    x.ProdId == prodId &&
                    x.WarehouseId == warehouseId &&
                    (x.SourceType == "Purchase" || x.SourceType == "Opening" || x.SourceType == "TransferIn") &&
                    (x.RemainingQty ?? 0) > 0)
                .Select(x => new
                {
                    remaining = (decimal)(x.RemainingQty ?? 0),
                    discPct = (decimal?)(x.PurchaseDiscount) ?? 0m
                })
                .ToListAsync();

            if (rows.Count == 0) return 0m;
            decimal totalRemaining = rows.Sum(r => r.remaining);
            if (totalRemaining <= 0m) return 0m;
            if (totalRemaining < 0.001m) return 0m;
            decimal result = rows.Sum(r => r.remaining * r.discPct) / totalRemaining;
            return ClampPercent(result);
        }

        /// <summary>
        /// الخصم الفعّال للصنف: خصم يدوي (من ProductDiscountOverrides) إن وُجد، وإلا الخصم المرجّح المحسوب من StockLedger.
        /// أولوية الـ override: (ProductId + WarehouseId + BatchId) ثم (ProductId + WarehouseId) ثم (ProductId فقط).
        /// </summary>
        public async Task<decimal> GetEffectivePurchaseDiscountAsync(int productId, int? warehouseId, int? batchId)
        {
            // البحث عن أحدث override حسب الأولوية
            var overrideQuery = _context.ProductDiscountOverrides
                .AsNoTracking()
                .Where(x => x.ProductId == productId);

            // أولاً: تطابق تام (ProductId + WarehouseId + BatchId)
            if (warehouseId.HasValue && batchId.HasValue)
            {
                var exact = await overrideQuery
                    .Where(x => x.WarehouseId == warehouseId && x.BatchId == batchId)
                    .OrderByDescending(x => x.CreatedAt)
                    .Select(x => (decimal?)x.OverrideDiscountPct)
                    .FirstOrDefaultAsync();
                if (exact.HasValue) return ClampPercent(exact.Value);
                // تشغيلة: إن لم يُوجد خصم لمخزن معيّن، جرّب خصم محفوظ بدون مخزن (من تقرير الأرصدة عند عدم تحديد مخزن)
                var batchAnyWh = await overrideQuery
                    .Where(x => x.WarehouseId == null && x.BatchId == batchId)
                    .OrderByDescending(x => x.CreatedAt)
                    .Select(x => (decimal?)x.OverrideDiscountPct)
                    .FirstOrDefaultAsync();
                if (batchAnyWh.HasValue) return ClampPercent(batchAnyWh.Value);
            }
            // ثانياً: (ProductId + WarehouseId) و BatchId = null
            if (warehouseId.HasValue)
            {
                var byWh = await overrideQuery
                    .Where(x => x.WarehouseId == warehouseId && x.BatchId == null)
                    .OrderByDescending(x => x.CreatedAt)
                    .Select(x => (decimal?)x.OverrideDiscountPct)
                    .FirstOrDefaultAsync();
                if (byWh.HasValue) return ClampPercent(byWh.Value);
            }
            // ثالثاً: مستوى الصنف فقط (WarehouseId = null, BatchId = null)
            var byProduct = await overrideQuery
                .Where(x => x.WarehouseId == null && x.BatchId == null)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => (decimal?)x.OverrideDiscountPct)
                .FirstOrDefaultAsync();
            if (byProduct.HasValue) return ClampPercent(byProduct.Value);

            // لا يوجد override: استخدام الخصم المرجّح المحسوب
            if (warehouseId.HasValue)
                return await GetWeightedPurchaseDiscountForWarehouseAsync(productId, warehouseId.Value);
            return await GetWeightedPurchaseDiscountCurrentAsync(productId);
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
        // 2) لو الصنف له مجموعة: نبحث عن ProductGroupPolicy (مجموعة+سياسة+مخزن) — مقدم على سياسة المخزن
        // 3) لو مفيش قاعدة مجموعة: نبحث عن WarehousePolicyRule (WarehouseId + PolicyId)
        // 4) لو مفيش Rule => نرجع لـ Policy.DefaultProfitPercent
        // 5) خصم البيع = الخصم المرجح - ProfitPercent
        // 6) نطبق MaxDiscountToCustomer إن وُجد
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
                return ClampPercent(weightedPurchaseDiscount);

            // =========================
            // (2) تحديد PolicyId للعميل
            // =========================
            int policyId = 1;
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
            // (3) جلب ProductGroupId للصنف
            // =========================
            var productGroupId = await _context.Products
                .AsNoTracking()
                .Where(p => p.ProdId == prodId)
                .Select(p => (int?)p.ProductGroupId)
                .FirstOrDefaultAsync();

            decimal profitPercent = 0m;
            decimal? maxDiscountToCustomer = null;

            // =========================
            // (4) أولوية: سياسة المجموعة — ProductGroupPolicy يعطينا PolicyId، والربح من جدول سياسات المخازن
            // مثال: أوجمنتين Group A → سياسته رقم 10 → نذهب لسياسات المخازن (Policy 10 + المخزن) → نطبق الربح
            // لو الصنف ليس له مجموعة → نستخدم سياسة العميل (في الخطوة 5)
            // =========================
            if (productGroupId.HasValue && productGroupId.Value > 0)
            {
                // جلب PolicyId من ProductGroupPolicy للمجموعة + المخزن
                var groupPolicy = await _context.ProductGroupPolicies
                    .AsNoTracking()
                    .Where(gp =>
                        gp.ProductGroupId == productGroupId.Value &&
                        gp.WarehouseId == warehouseId &&
                        gp.IsActive)
                    .Select(gp => new { gp.PolicyId, gp.ProfitPercent, gp.MaxDiscountToCustomer })
                    .FirstOrDefaultAsync();

                // لو مفيش (مجموعة + مخزن): نجرب أي سياسة للمجموعة
                if (groupPolicy == null)
                {
                    groupPolicy = await _context.ProductGroupPolicies
                        .AsNoTracking()
                        .Where(gp =>
                            gp.ProductGroupId == productGroupId.Value &&
                            gp.IsActive)
                        .OrderByDescending(gp => gp.WarehouseId == warehouseId)
                        .Select(gp => new { gp.PolicyId, gp.ProfitPercent, gp.MaxDiscountToCustomer })
                        .FirstOrDefaultAsync();
                }

                if (groupPolicy != null)
                {
                    // الربح من جدول سياسات المخازن (WarehousePolicyRules) لهذه السياسة + المخزن
                    var whRule = await _context.WarehousePolicyRules
                        .AsNoTracking()
                        .Where(w => w.WarehouseId == warehouseId && w.PolicyId == groupPolicy.PolicyId && w.IsActive)
                        .Select(w => new { w.ProfitPercent, w.MaxDiscountToCustomer })
                        .FirstOrDefaultAsync();

                    if (whRule != null)
                    {
                        profitPercent = whRule.ProfitPercent;
                        maxDiscountToCustomer = whRule.MaxDiscountToCustomer ?? groupPolicy.MaxDiscountToCustomer;
                    }
                    else
                    {
                        // لو مفيش قاعدة في سياسات المخازن: نستخدم ProfitPercent من ProductGroupPolicy أو Policy.DefaultProfitPercent
                        if (groupPolicy.ProfitPercent > 0)
                            profitPercent = groupPolicy.ProfitPercent;
                        else
                        {
                            var defProfit = await _context.Policies
                                .AsNoTracking()
                                .Where(p => p.PolicyId == groupPolicy.PolicyId && p.IsActive)
                                .Select(p => (decimal?)p.DefaultProfitPercent)
                                .FirstOrDefaultAsync();
                            profitPercent = defProfit ?? 0m;
                        }
                        maxDiscountToCustomer = groupPolicy.MaxDiscountToCustomer;
                    }
                }
            }

            // =========================
            // (5) لو مفيش قاعدة مجموعة: نستخدم سياسة المخزن
            // =========================
            if (profitPercent == 0 && !maxDiscountToCustomer.HasValue)
            {
                var rule = await _context.WarehousePolicyRules
                    .AsNoTracking()
                    .Where(r =>
                        r.WarehouseId == warehouseId &&
                        r.PolicyId == policyId &&
                        r.IsActive)
                    .Select(r => new { r.ProfitPercent, r.MaxDiscountToCustomer })
                    .FirstOrDefaultAsync();

                if (rule != null)
                {
                    profitPercent = rule.ProfitPercent;
                    maxDiscountToCustomer = rule.MaxDiscountToCustomer;
                }
                else
                {
                    var policyDefaultProfit = await _context.Policies
                        .AsNoTracking()
                        .Where(p => p.PolicyId == policyId && p.IsActive)
                        .Select(p => (decimal?)p.DefaultProfitPercent)
                        .FirstOrDefaultAsync();

                    profitPercent = policyDefaultProfit ?? 0m;
                }
            }

            if (profitPercent < 0) profitPercent = 0;

            // =========================
            // (6) حساب خصم البيع وتطبيق أقصى خصم
            // =========================
            decimal saleDiscount = weightedPurchaseDiscount - profitPercent;

            if (maxDiscountToCustomer.HasValue && saleDiscount > maxDiscountToCustomer.Value)
                saleDiscount = maxDiscountToCustomer.Value;

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

        /// <summary>
        /// تفاصيل خصم البيع (للاستخدام في GetSalesProductInfo)
        /// </summary>
        public class SaleDiscountDetails
        {
            public decimal SaleDiscount { get; set; }
            public int? AppliedGroupPolicyId { get; set; }   // رقم السياسة المطبقة من المجموعة (إن وُجدت)
            public string? AppliedGroupPolicyName { get; set; } // اسم السياسة (مثل Policy 10)
            public string? PolicySource { get; set; }         // مصدر السياسة: Group / Warehouse / Policy
        }

        public async Task<SaleDiscountDetails> GetSaleDiscountDetailsAsync(
            int prodId,
            int warehouseId,
            int customerId,
            decimal weightedPurchaseDiscount)
        {
            var saleDiscount = await GetSaleDiscountAsync(prodId, warehouseId, customerId, weightedPurchaseDiscount);

            var productGroupId = await _context.Products
                .AsNoTracking()
                .Where(p => p.ProdId == prodId)
                .Select(p => (int?)p.ProductGroupId)
                .FirstOrDefaultAsync();

            int? appliedPolicyId = null;
            string? appliedPolicyName = null;

            if (productGroupId.HasValue && productGroupId.Value > 0)
            {
                // نفس منطق GetSaleDiscountAsync: ProductGroupPolicy يعطينا PolicyId
                var gpWithPolicy = await _context.ProductGroupPolicies
                    .AsNoTracking()
                    .Where(gp => gp.ProductGroupId == productGroupId.Value && gp.WarehouseId == warehouseId && gp.IsActive)
                    .Select(gp => new { gp.PolicyId, PolicyName = gp.Policy != null ? gp.Policy.Name : null })
                    .FirstOrDefaultAsync();

                if (gpWithPolicy == null)
                {
                    gpWithPolicy = await _context.ProductGroupPolicies
                        .AsNoTracking()
                        .Where(gp => gp.ProductGroupId == productGroupId.Value && gp.IsActive)
                        .OrderByDescending(gp => gp.WarehouseId == warehouseId)
                        .Select(gp => new { gp.PolicyId, PolicyName = gp.Policy != null ? gp.Policy.Name : null })
                        .FirstOrDefaultAsync();
                }

                if (gpWithPolicy != null)
                {
                    appliedPolicyId = gpWithPolicy.PolicyId;
                    appliedPolicyName = gpWithPolicy.PolicyName ?? $"Policy {gpWithPolicy.PolicyId}";
                }
            }

            return new SaleDiscountDetails
            {
                SaleDiscount = saleDiscount,
                AppliedGroupPolicyId = appliedPolicyId,
                AppliedGroupPolicyName = appliedPolicyName,
                PolicySource = appliedPolicyId != null ? "Group" : "Warehouse/Policy"
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
