using System;

namespace ERP.Models
{
    /// <summary>
    /// ربط FIFO: يحدد كل "خروج" أخذ قد إيه من أي "دخول".
    /// ضروري لحساب تكلفة المبيعات بدقة، ولإرجاع البيع لنفس الدُفعات، وتتبع التشغيلات.
    /// </summary>
    public class StockFifoMap
    {
        // مفتاح أساسي
        public int MapId { get; set; } 

        // حركة الخروج المرتبطة (FK -> StockLedger.EntryId)
        public int OutEntryId { get; set; } 

        // حركة الدخول المرتبطة (FK -> StockLedger.EntryId)
        public int InEntryId { get; set; } = default!;

        // كمية أُخذت من دخلة معينة (عدد علب)
        public int Qty { get; set; }

        // لقطة تكلفة الدخلة لحظة الاستهلاك (لأرشفة التكلفة)
        public decimal UnitCost { get; set; }
    }
}
