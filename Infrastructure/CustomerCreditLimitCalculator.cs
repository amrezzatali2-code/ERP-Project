using ERP.Models;

namespace ERP.Infrastructure
{
    public static class CustomerCreditLimitCalculator
    {
        public static decimal GetEffectiveCreditLimit(Customer? customer, DateTime? now = null)
        {
            if (customer == null) return 0m;
            return GetEffectiveCreditLimit(
                customer.CreditLimit,
                customer.CreditLimitTemporaryIncrease,
                customer.CreditLimitTemporaryUntil,
                now);
        }

        public static decimal GetEffectiveCreditLimit(
            decimal baseCreditLimit,
            decimal? temporaryIncrease,
            DateTime? temporaryUntil,
            DateTime? now = null)
        {
            var current = now ?? DateTime.Now;
            var activeIncrease =
                temporaryIncrease.GetValueOrDefault() > 0m &&
                temporaryUntil.HasValue &&
                temporaryUntil.Value > current
                    ? temporaryIncrease!.Value
                    : 0m;

            return baseCreditLimit + activeIncrease;
        }

        public static decimal GetRemainingCredit(Customer? customer, DateTime? now = null)
        {
            if (customer == null) return 0m;
            var effective = GetEffectiveCreditLimit(customer, now);
            return Math.Max(0m, effective - customer.CurrentBalance);
        }
    }
}
