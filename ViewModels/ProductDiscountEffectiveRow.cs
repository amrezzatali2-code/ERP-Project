namespace ERP.ViewModels
{
    /// <summary>
    /// صف في قائمة الخصم اليدوي للبيع: صنف له رصيد مع الخصم الفعّال (يدوي إن وُجد، وإلا المرجّح).
    /// قد يكون الصف على مستوى الصنف/المخزن (BatchId = null) أو على مستوى التشغيلة (BatchId معبّأ).
    /// </summary>
    public class ProductDiscountEffectiveRow
    {
        public int ProdId { get; set; }
        public string ProductName { get; set; } = "";
        public string ProdCode { get; set; } = "";
        public int? WarehouseId { get; set; }
        public string WarehouseName { get; set; } = "الكل";
        /// <summary>معرف التشغيلة (null = صف على مستوى الصنف/المخزن).</summary>
        public int? BatchId { get; set; }
        /// <summary>رقم التشغيلة (للعرض في صفوف التشغيلات).</summary>
        public string? BatchNo { get; set; }
        /// <summary>تاريخ الصلاحية (للعرض في صفوف التشغيلات).</summary>
        public DateTime? Expiry { get; set; }
        /// <summary>الخصم الفعّال: من override إن وُجد، وإلا المرجّح.</summary>
        public decimal EffectiveDiscountPct { get; set; }
        /// <summary>هل المصدر خصم يدوي (جدول Overrides)؟</summary>
        public bool IsManual { get; set; }
        public int? OverrideId { get; set; }
        public string? Reason { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    /// <summary>
    /// مجموعة صف واحد (صنف/مخزن) مع صفوف التشغيلات عند وجود أكثر من تشغيلة.
    /// </summary>
    public class ProductDiscountGroupRow
    {
        public ProductDiscountEffectiveRow MainRow { get; set; } = null!;
        public List<ProductDiscountEffectiveRow> BatchRows { get; set; } = new();
    }
}
