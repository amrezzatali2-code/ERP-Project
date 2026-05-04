using ERP.Infrastructure;
using System;

namespace ERP.ViewModels
{
    public class TrackTraceQueueRowViewModel
    {
        public long Id { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public long? ItemUnitId { get; set; }
        public string? Uid { get; set; }
        public string? ProductName { get; set; }
        public int RetryCount { get; set; }
        public string? LastError { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? NextRetryAt { get; set; }
        public DateTime? SentAt { get; set; }
    }

    public class TrackTraceQueueIndexViewModel
    {
        public PagedResult<TrackTraceQueueRowViewModel> Result { get; set; } = new();
        public string? Search { get; set; }
        public string? Status { get; set; }
        public string? EventType { get; set; }
        public int PendingCount { get; set; }
        public int FailedCount { get; set; }
        public int SentCount { get; set; }
    }
}
