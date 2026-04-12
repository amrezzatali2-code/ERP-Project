using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Data;
using ERP.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace ERP.Controllers;

/// <summary>
/// خصم البيع % لسياسات 1..10 من <see cref="ERP.Models.WarehousePolicyRule"/> + الافتراضي من <see cref="ERP.Models.Policy"/>،
/// دون مسار مجموعة الأصناف (عرض تقريري لجدول سياسات المخزن).
/// الصيغة: خصم البيع = الخصم المرجّح − نسبة ربح المخزن، مع احترام أقصى خصم للعميل إن وُجد.
/// </summary>
internal static class ProductBalancePolicySaleHelper
{
    public static async Task<(Dictionary<int, (decimal Profit, decimal? Max)> Rules, Dictionary<int, decimal> DefaultProfit)> LoadWarehousePolicyContextAsync(
        AppDbContext context, int warehouseId)
    {
        Dictionary<int, (decimal Profit, decimal? Max)> rules;
        if (warehouseId > 0)
        {
            var rulesList = await context.WarehousePolicyRules
                .AsNoTracking()
                .Where(r => r.WarehouseId == warehouseId && r.PolicyId >= 1 && r.PolicyId <= 10 && r.IsActive)
                .Select(r => new { r.PolicyId, r.ProfitPercent, r.MaxDiscountToCustomer })
                .ToListAsync();
            rules = rulesList.ToDictionary(
                x => x.PolicyId,
                x => (x.ProfitPercent, x.MaxDiscountToCustomer));
        }
        else
            rules = new Dictionary<int, (decimal Profit, decimal? Max)>();

        // نسب الربط الافتراضية من جدول Policies لكل 1..10 (بما فيها غير المفعّلة) حتى لا تبقى أعمدة الخصم فارغة عند غياب قاعدة مخزن مفعّلة
        var defaults = await context.Policies
            .AsNoTracking()
            .Where(p => p.PolicyId >= 1 && p.PolicyId <= 10)
            .ToDictionaryAsync(p => p.PolicyId, p => p.DefaultProfitPercent);

        return (rules, defaults);
    }

    public static decimal? ComputePolicySaleDiscountPct(
        int policyId,
        decimal weightedPurchaseDiscount,
        Dictionary<int, (decimal Profit, decimal? Max)> rules,
        Dictionary<int, decimal> defaults)
    {
        if (policyId < 1 || policyId > 10)
            return null;

        decimal profit = 0m;
        if (rules.TryGetValue(policyId, out var ru))
            profit = ru.Profit;
        else if (defaults.TryGetValue(policyId, out var d))
            profit = d;

        var maxDc = rules.TryGetValue(policyId, out var r2) ? r2.Max : null;

        decimal sale = weightedPurchaseDiscount - profit;
        if (maxDc.HasValue && sale > maxDc.Value)
            sale = maxDc.Value;

        if (sale < 0m) sale = 0m;
        if (sale > 100m) sale = 100m;
        return Math.Round(sale, 2, MidpointRounding.AwayFromZero);
    }

    public static void ApplyToReport(
        IEnumerable<ProductBalanceReportDto> rows,
        Dictionary<int, (decimal Profit, decimal? Max)> rules,
        Dictionary<int, decimal> defaults)
    {
        foreach (var dto in rows)
        {
            for (var p = 1; p <= 10; p++)
                dto.PolicySaleDiscountPct[p - 1] = ComputePolicySaleDiscountPct(
                    p, dto.WeightedDiscount, rules, defaults);

            if (dto.Batches == null) continue;
            foreach (var b in dto.Batches)
            {
                for (var p = 1; p <= 10; p++)
                    b.PolicySaleDiscountPct[p - 1] = ComputePolicySaleDiscountPct(
                        p, b.WeightedDiscount, rules, defaults);
            }
        }
    }
}
