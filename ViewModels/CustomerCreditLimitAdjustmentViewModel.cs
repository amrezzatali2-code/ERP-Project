namespace ERP.ViewModels
{
    public class CustomerCreditLimitAdjustmentViewModel
    {
        public int? CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public string? PartyCategory { get; set; }
        public string? AccountName { get; set; }
        public string? AccountCode { get; set; }

        public decimal BaseCreditLimit { get; set; }
        public decimal CurrentBalance { get; set; }
        public decimal EffectiveCreditLimit { get; set; }
        public decimal RemainingCredit { get; set; }

        public decimal TemporaryIncreaseAmount { get; set; }
        public DateTime? TemporaryIncreaseUntil { get; set; }

        public decimal IncreaseAmount { get; set; }

        public List<CustomerCreditLimitAdjustmentLookupItem> Customers { get; set; } = new();
    }

    public class CustomerCreditLimitAdjustmentLookupItem
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; } = "";
        public string? PartyCategory { get; set; }
    }
}
