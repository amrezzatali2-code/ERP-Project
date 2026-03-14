namespace ERP.ViewModels
{
    /// <summary>
    /// صف واحد في تقرير خط السير: فاتورة مبيعات + بيانات خط السير + العميل + خط السير من العميل + المخزن.
    /// </summary>
    public class RouteReportRowDto
    {
        public int Id { get; set; }
        public int SIId { get; set; }
        public DateTime SIDate { get; set; }
        public string CustomerName { get; set; } = "";
        public int? RouteId { get; set; }
        public string RouteName { get; set; } = "";
        public string WarehouseName { get; set; } = "";
        public string? ControlEmployeeName { get; set; }
        public string? PreparerEmployeeName { get; set; }
        public int BagsCount { get; set; }
        public int PacketsCount { get; set; }
        public int CartonsCount { get; set; }
        public int FridgeItemsCount { get; set; }
        public int FridgeBoxesCount { get; set; }
        public string? Notes { get; set; }
    }
}
