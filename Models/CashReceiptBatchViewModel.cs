using System;
using System.Collections.Generic;

namespace ERP.Models
{
    public class CashReceiptBatchViewModel
    {
        public int? AreaId { get; set; }
        public int? DistributorEmployeeId { get; set; }
        public int? DistributorUserId { get; set; }
        public DateTime BatchDate { get; set; } = DateTime.Today;
        public TimeSpan BatchTime { get; set; } = DateTime.Now.TimeOfDay;
        public string? AreaName { get; set; }
        public string? DistributorName { get; set; }
        public List<CashReceipt> Receipts { get; set; } = new();
    }
}
