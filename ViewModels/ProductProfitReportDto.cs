namespace ERP.ViewModels
{
    /// <summary>
    /// DTO: بيانات تقرير أرباح الأصناف
    /// </summary>
    public class ProductProfitReportDto
    {
        public int ProdId { get; set; }
        public string ProdCode { get; set; } = "";
        public string ProdName { get; set; } = "";
        public string CategoryName { get; set; } = "";
        
        // الربح من البيع (فواتير البيع فقط)
        public decimal SalesRevenue { get; set; }      // إجمالي الإيرادات من فواتير البيع
        public decimal SalesCost { get; set; }         // إجمالي التكلفة من فواتير البيع
        public decimal SalesProfit { get; set; }       // الربح من فواتير البيع = Revenue - Cost
        public decimal SalesProfitPercent { get; set; } // نسبة الربح من المبيعات

        // مرتجعات البيع (تُخصم من ربح البيع)
        public decimal ReturnProfit { get; set; }      // ربح المرتجعات = ReturnRevenue - ReturnCost
        
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
        
        // الربح من التسويات (من StockAdjustmentLines)
        public decimal AdjustmentProfit { get; set; }   // صافي الربح/الخسارة من التسويات (فائض - عجز)

        // الربح من التحويلات (من StockTransferLines — خصم أقل من المرجح)
        public decimal TransferProfit { get; set; }     // ربح التحويلات بين المخازن

        // صافي الربح النهائي
        public decimal NetProfit { get; set; }          // صافي الربح = (SalesProfit - ReturnProfit) + AdjustmentProfit + TransferProfit

        // إحصائيات إضافية
        public decimal SalesQty { get; set; }          // كمية صافي المبيعات (بعد المرتجعات)
    }
}
