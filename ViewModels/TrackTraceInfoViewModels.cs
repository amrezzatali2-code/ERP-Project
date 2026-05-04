using ERP.Infrastructure;
using System;

namespace ERP.ViewModels
{
    public class TrackTraceInfoRowViewModel
    {
        public long ItemUnitId { get; set; }
        public string SourceType { get; set; } = string.Empty;
        public string SourceTitle { get; set; } = string.Empty;
        public int DocumentId { get; set; }
        public int? SourceLineId { get; set; }
        public string Uid { get; set; } = string.Empty;
        public string? Gtin { get; set; }
        public string? SerialNo { get; set; }
        public int ProdId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string? BatchNo { get; set; }
        public DateTime? Expiry { get; set; }
        public string Status { get; set; } = string.Empty;
        public string WarehouseName { get; set; } = string.Empty;
        public int PIId { get; set; }
        public int LineNo { get; set; }
        public DateTime InvoiceDate { get; set; }
        public string SupplierName { get; set; } = string.Empty;
        public string? CurrentSourceType { get; set; }
        public int? CurrentSourceId { get; set; }
        public int? CurrentSourceLineNo { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class TrackTraceInfoIndexViewModel
    {
        public PagedResult<TrackTraceInfoRowViewModel> Result { get; set; } = new();
        public string? Search { get; set; }
        public string? Status { get; set; }
        public string? BatchNo { get; set; }
        public string? SourceType { get; set; }
        public string? SourceTitle { get; set; }
        public int? DocumentId { get; set; }
        public int? PIId { get; set; }
        public int? LineNo { get; set; }
        public int? SourceLineId { get; set; }
        public int TotalUnits { get; set; }
        public int InStockUnits { get; set; }
        public int SoldUnits { get; set; }
    }
}
