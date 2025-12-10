using System;
using System.Collections.Generic;

namespace ERP.ViewModels
{
    // سطر حركة واحد (بيع أو مرتجع)
    public class MovementLine
    {
        public DateTime Date { get; set; }          // تاريخ الحركة
        public string DocNo { get; set; } = "";     // رقم المستند (فاتورة/مرتجع)
        public string Customer { get; set; } = "";  // اسم العميل
        public decimal Qty { get; set; }            // الكمية
        public decimal Amount { get; set; }         // المبلغ
        public string Kind { get; set; } = "بيع";   // "بيع" أو "مرتجع"
    }

    public class ProductMovementViewModel
    {
        public int? ProdId { get; set; }         // معرّف الصنف (int?)
        public DateTime? From { get; set; }         // من تاريخ
        public DateTime? To { get; set; }         // إلى تاريخ

        // إجماليات
        public decimal TotalQtySold { get; set; }
        public decimal TotalAmountSold { get; set; }
        public decimal TotalQtyReturned { get; set; }
        public decimal TotalAmountReturned { get; set; }

        public decimal NetQty => TotalQtySold - TotalQtyReturned;   // صافي الكمية
        public decimal NetAmount => TotalAmountSold - TotalAmountReturned; // صافي المبلغ

        // سطور الحركة للتفاصيل
        public List<MovementLine> Lines { get; set; } = new();
    }
}
