namespace ERP.ViewModels
{
    /// <summary>
    /// سطر تشغيلة واحدة ضمن تقرير أرصدة الأصناف (عند عرض الصنف بعدة تشغيلات).
    /// </summary>
    public class ProductBalanceBatchRow
    {
        public int? BatchId { get; set; }
        public string? BatchNo { get; set; }
        public DateTime? Expiry { get; set; }
        public int CurrentQty { get; set; }
        /// <summary>الخصم المرجّح المحسوب لهذه التشغيلة.</summary>
        public decimal WeightedDiscount { get; set; }
        /// <summary>الخصم اليدوي للبيع لهذه التشغيلة (من overrides).</summary>
        public decimal? ManualDiscountPct { get; set; }
        /// <summary>الخصم الفعّال = ManualDiscountPct ?? WeightedDiscount.</summary>
        public decimal EffectiveDiscountPct { get; set; }
        public decimal UnitCost { get; set; }
        public decimal TotalCost { get; set; }
        /// <summary>كمية المبيعات لهذه التشغيلة (في مدى التاريخ إن وُجد).</summary>
        public decimal SalesQty { get; set; }
        /// <summary>سعر الجمهور للتشغيلة (من Batches.PriceRetailBatch أو سعر الصنف).</summary>
        public decimal PriceRetail { get; set; }
    }
}
