using System;
using System.Collections.Generic;

namespace ERP.Infrastructure
{
    /// <summary>
    /// عرض نوع الطرف بالعربي في الواجهات؛ القيم المخزنة في DB تبقى بالإنجليزية (Customer، …).
    /// </summary>
    public static class PartyCategoryDisplay
    {
        public static readonly IReadOnlyDictionary<string, string> ArabicByKey =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Customer", "عميل" },
                { "Supplier", "مورد" },
                { "Employee", "موظف" },
                { "Investor", "مستثمر" },
                { "Bank", "بنك" },
                { "Expense", "مصروف" },
                { "Owner", "مالك / شريك" }
            };

        public static string ToArabic(string? partyCategory)
        {
            if (string.IsNullOrWhiteSpace(partyCategory)) return "";
            return ArabicByKey.TryGetValue(partyCategory.Trim(), out var name) ? name : partyCategory.Trim();
        }
    }
}
