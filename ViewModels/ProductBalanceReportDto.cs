namespace ERP.ViewModels
{
    /// <summary>
    /// DTO: بيانات تقرير أرصدة الأصناف
    /// </summary>
    public class ProductBalanceReportDto
    {
        public int ProdId { get; set; }
        public string ProdCode { get; set; } = "";
        public string ProdName { get; set; } = "";
        public string CategoryName { get; set; } = "";
        public string ProductGroupName { get; set; } = "";
        public string ProductBonusGroupName { get; set; } = "";
        public int CurrentQty { get; set; }
        /// <summary>الخصم المرجّح المحسوب من StockLedger (للعرض والمقارنة فقط).</summary>
        public decimal WeightedDiscount { get; set; }
        /// <summary>الخصم اليدوي للبيع من ProductDiscountOverrides (أحدث سجل).</summary>
        public decimal? ManualDiscountPct { get; set; }
        /// <summary>الخصم الفعّال = ManualDiscountPct ?? WeightedDiscount (المستخدم في البيع).</summary>
        public decimal EffectiveDiscountPct { get; set; }
        /// <summary>فرق ربح متوقع: (Effective - Computed) * PriceRetail * CurrentQty / 100 (للإرشاد فقط).</summary>
        public decimal ProfitDeltaExpected { get; set; }
        public decimal SalesQty { get; set; }
        public decimal PriceRetail { get; set; }
        public decimal UnitCost { get; set; }
        public decimal TotalCost { get; set; }

        /// <summary>عند وجود تشغيلتين أو أكثر: قائمة التشغيلات ببياناتها (رقم تشغيلة، صلاحية، كمية، خصم، تكلفة).</summary>
        public List<ProductBalanceBatchRow>? Batches { get; set; }

        /// <summary>عند وجود تشغيلة واحدة فقط (أو بيانات من دفتر الحركة فقط): رقم التشغيلة للصف الرئيسي.</summary>
        public string? FirstBatchNo { get; set; }
        /// <summary>عند وجود تشغيلة واحدة فقط: تاريخ الصلاحية للصف الرئيسي.</summary>
        public DateTime? FirstBatchExpiry { get; set; }
    }
}
