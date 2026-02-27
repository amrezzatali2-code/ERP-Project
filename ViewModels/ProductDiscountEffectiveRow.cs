namespace ERP.ViewModels
{
    /// <summary>
    /// صف في قائمة الخصم اليدوي للبيع: صنف له رصيد مع الخصم الفعّال (يدوي إن وُجد، وإلا المرجّح).
    /// </summary>
    public class ProductDiscountEffectiveRow
    {
        public int ProdId { get; set; }
        public string ProductName { get; set; } = "";
        public string ProdCode { get; set; } = "";
        public int? WarehouseId { get; set; }
        public string WarehouseName { get; set; } = "الكل";
        /// <summary>الخصم الفعّال: من override إن وُجد، وإلا المرجّح.</summary>
        public decimal EffectiveDiscountPct { get; set; }
        /// <summary>هل المصدر خصم يدوي (جدول Overrides)؟</summary>
        public bool IsManual { get; set; }
        public int? OverrideId { get; set; }
        public string? Reason { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? CreatedAt { get; set; }
    }
}
