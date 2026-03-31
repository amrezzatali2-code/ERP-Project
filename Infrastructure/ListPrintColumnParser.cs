using System;
using System.Collections.Generic;
using System.Linq;

namespace ERP.Infrastructure
{
    /// <summary>
    /// تحليل معامل <c>printCols</c> لصفحات طباعة القوائم (مفصولة بفواصل، مطابقة لـ <c>data-col</c> في الجدول).
    /// يُعاد ترتيب المفاتيح حسب <paramref name="allowedColumnOrder"/> فقط.
    /// </summary>
    public static class ListPrintColumnParser
    {
        /// <param name="printCols">من الطلب؛ فارغ = كل الأعمدة المعتمدة.</param>
        /// <param name="allowedColumnOrder">الترتيب المعتمد (قائمة بيضاء).</param>
        /// <param name="keyAliases">اختياري: مثل policy → PolicyId عند اختلاف الاسم بين الرأس والرابط.</param>
        public static List<string> ParsePrintColumns(
            string? printCols,
            IReadOnlyList<string> allowedColumnOrder,
            IReadOnlyDictionary<string, string>? keyAliases = null)
        {
            if (allowedColumnOrder == null || allowedColumnOrder.Count == 0)
                return new List<string>();

            if (string.IsNullOrWhiteSpace(printCols))
                return allowedColumnOrder.ToList();

            string? ResolveCanonical(string raw)
            {
                if (keyAliases != null)
                {
                    foreach (var kv in keyAliases)
                    {
                        if (string.Equals(kv.Key, raw, StringComparison.OrdinalIgnoreCase))
                        {
                            var target = kv.Value;
                            var match = allowedColumnOrder.FirstOrDefault(k =>
                                string.Equals(k, target, StringComparison.Ordinal));
                            return match ?? target;
                        }
                    }
                }

                return allowedColumnOrder.FirstOrDefault(k =>
                    string.Equals(k, raw, StringComparison.OrdinalIgnoreCase));
            }

            var wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var part in printCols.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var c = ResolveCanonical(part.Trim());
                if (c != null)
                    wanted.Add(c);
            }

            var result = new List<string>();
            foreach (var key in allowedColumnOrder)
            {
                if (wanted.Contains(key))
                    result.Add(key);
            }

            return result.Count > 0 ? result : allowedColumnOrder.ToList();
        }
    }
}
