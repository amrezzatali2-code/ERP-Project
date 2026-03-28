namespace ERP.ViewModels
{
    /// <summary>
    /// DTO: بيانات تقرير أرصدة العملاء
    /// </summary>
    public class CustomerBalanceReportDto
    {
        public int CustomerId { get; set; }
        public string CustomerCode { get; set; } = "";
        /// <summary>كود الإكسل (مسلسل/رقم من ملف الاستيراد) للمقارنة مع الإكسل بعد الاستيراد.</summary>
        public string? ExternalCode { get; set; }
        public string CustomerName { get; set; } = "";
        /// <summary>الحساب المحاسبي المرتبط بالعميل في شجرة الحسابات.</summary>
        public int? AccountId { get; set; }
        public string? AccountCode { get; set; }
        public string? AccountName { get; set; }
        /// <summary>نص العرض: كود الحساب — الاسم (فارغ إن لم يُربط حساب).</summary>
        public string AccountDisplay
        {
            get
            {
                if (!AccountId.HasValue) return "";
                var code = AccountCode ?? "";
                var name = AccountName ?? "";
                if (string.IsNullOrEmpty(code) && string.IsNullOrEmpty(name)) return "";
                if (string.IsNullOrEmpty(name)) return code;
                if (string.IsNullOrEmpty(code)) return name;
                return $"{code} — {name}";
            }
        }
        public string? PartyCategory { get; set; } = "";
        public string? Phone1 { get; set; } = "";
        public decimal CurrentBalance { get; set; }
        public decimal CreditLimit { get; set; }
        public decimal TotalSales { get; set; }
        public decimal TotalPurchases { get; set; }
        public decimal TotalReturns { get; set; }
        public decimal AvailableCredit { get; set; }
    }
}
