// ViewModels/PurchaseRequestHeaderDto.cs
using System;

namespace ERP.ViewModels
{
    /// <summary>
    /// DTO لحفظ رأس طلب الشراء
    /// - يستقبل البيانات من JavaScript (camelCase) ويحولها إلى C# (PascalCase)
    /// - PropertyNameCaseInsensitive = true في Program.cs يضمن عمل الـ binding
    /// </summary>
    public class PurchaseRequestHeaderDto
    {
        // متغير: رقم الطلب (0 = طلب جديد، >0 = طلب موجود)
        public int PRId { get; set; }

        // متغير: تاريخ الطلب
        public DateTime? PRDate { get; set; }

        // متغير: تاريخ مطلوب قبل
        public DateTime? NeedByDate { get; set; }

        // متغير: كود المورد المختار من شاشة الأوتوكمبليت
        public int CustomerId { get; set; }

        // متغير: كود المخزن المختار من الكومبو
        public int WarehouseId { get; set; }

        // متغير: تم الطلب بواسطة
        public string? RequestedBy { get; set; }

        // متغير: ملاحظات
        public string? Notes { get; set; }
    }
}
