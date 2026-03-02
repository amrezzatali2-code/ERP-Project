namespace ERP.ViewModels
{
    /// <summary>
    /// DTO: بيانات تقرير أرباح العملاء
    /// </summary>
    public class CustomerProfitReportDto
    {
        public int CustomerId { get; set; }
        public string CustomerCode { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string? PartyCategory { get; set; } = "";
        public string? Phone1 { get; set; } = "";
        
        // الربح من البيع (من SalesInvoiceLines)
        public decimal SalesRevenue { get; set; }      // إجمالي الإيرادات من المبيعات
        public decimal SalesCost { get; set; }         // إجمالي التكلفة من المبيعات
        public decimal SalesProfit { get; set; }       // الربح من المبيعات = Revenue - Cost
        public decimal SalesProfitPercent { get; set; } // نسبة الربح من المبيعات

        // مرتجعات البيع (تُخصم من ربح البيع)
        public decimal ReturnProfit { get; set; }      // ربح المرتجعات = ReturnRevenue - ReturnCost

        // صافي الربح بعد المرتجعات (بدون الإشعارات)
        public decimal NetProfit { get; set; }         // صافي الربح = SalesProfit - ReturnProfit
        
        // الربح من الميزانية (من LedgerEntries)
        public decimal LedgerRevenue { get; set; }    // الإيرادات من الميزانية
        public decimal LedgerCost { get; set; }        // التكلفة من الميزانية
        public decimal LedgerProfit { get; set; }      // الربح من الميزانية = Revenue - Cost
        public decimal LedgerProfitPercent { get; set; } // نسبة الربح من الميزانية
        
        // الربح من أرصدة الحسابات (Account Balance)
        public decimal AccountBalanceRevenue { get; set; }  // رصيد حساب الإيرادات (Credit - Debit)
        public decimal AccountBalanceCost { get; set; }     // رصيد حساب COGS (Debit - Credit)
        public decimal AccountBalanceProfit { get; set; }   // الربح من الأرصدة = Revenue Balance - COGS Balance
        public decimal AccountBalanceProfitPercent { get; set; } // نسبة الربح من الأرصدة
        
        // إشعارات الخصم والإضافة (تأثير على الأرباح)
        public decimal DebitNotesAmount { get; set; }      // إجمالي إشعارات الخصم (يقلل الربح)
        public decimal CreditNotesAmount { get; set; }     // إجمالي إشعارات الإضافة (يزيد الربح)
        public decimal NetNotesAdjustment { get; set; }    // صافي الإشعارات = CreditNotes - DebitNotes
        public decimal AdjustedProfit { get; set; }        // الربح المعدل = SalesProfit + NetNotesAdjustment
        public decimal AdjustedProfitPercent { get; set; } // نسبة الربح المعدل
        
        // إحصائيات إضافية
        public int InvoiceCount { get; set; }          // عدد الفواتير
        public decimal AvgInvoiceValue { get; set; }   // متوسط قيمة الفاتورة
    }
}
