using System;

namespace ERP.Models
{
    /// <summary>
    /// رصيد سريع لكل (مخزن + صنف + تشغيلة + صلاحية):
    /// - يُحدّث مع كل حركة (شراء/بيع/تحويل/مرتجع/تسوية) داخل نفس المعاملة.
    /// - الغرض: فحص الرصيد اللحظي ومنع البيع بالسالب بسرعة.
    /// - مصدر الحقيقة يظل دفتر الحركات (StockLedger).
    /// </summary>
    public class StockBatch
    {
        // معرّف المخزن
        public int WarehouseId { get; set; } 

        // معرّف الصنف
        public int ProdId { get; set; } 

        // رقم التشغيلة
        public string BatchNo { get; set; } = default!; // التشغيلة

        // تاريخ الصلاحية
        public DateTime Expiry { get; set; } // الصلاحية

        // الرصيد الحالي (عدد العِلب)
        public int QtyOnHand { get; set; } // الرصيد (int)
    }
}
