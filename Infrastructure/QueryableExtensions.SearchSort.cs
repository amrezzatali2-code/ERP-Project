using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace ERP.Infrastructure
{
    /// <summary>
    /// دوال البحث/الترتيب العامة القابلة لإعادة الاستخدام لكل الجداول
    /// </summary>
    public static partial class QueryableExtensions
    {
        private static string NormalizeSearchText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var chars = value.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = chars[i] switch
                {
                    >= '\u0660' and <= '\u0669' => (char)('0' + (chars[i] - '\u0660')), // Arabic-Indic
                    >= '\u06F0' and <= '\u06F9' => (char)('0' + (chars[i] - '\u06F0')), // Eastern Arabic-Indic
                    _ => chars[i]
                };
            }

            // Normalize input in .NET only (safe for EF) using invariant rules.
            // The database-side expression uses ToLower() for translation.
            return new string(chars).ToLowerInvariant();
        }

        /// <summary>
        /// يطبّق "بحث + ترتيب" على أي استعلام IQueryable بشكل موحّد.
        /// - stringFields: مفاتيح الحقول النصية (يتم البحث فيها بـ Contains).
        /// - intFields   : مفاتيح الحقول الرقمية (يتم البحث فيها بالمساواة ==).
        /// - orderFields : مفاتيح الحقول المسموح الترتيب بها (تدعم أي نوع).
        /// - defaultSearchBy/defaultSortBy: مفاتيح افتراضية عند غياب أو خطأ المدخل.
        /// ملاحظة: تم دعم "all" للبحث في كل الحقول النصية + الحقول الرقمية (عند إمكان تحويل المصطلح إلى رقم).
        /// </summary>
        public static IQueryable<T> ApplySearchSort<T>(
            this IQueryable<T> q,
            string? search, string? searchBy,
            string? sort, string? dir,
            IDictionary<string, Expression<Func<T, string?>>>? stringFields = null,
            IDictionary<string, Expression<Func<T, int>>>? intFields = null,
            IDictionary<string, Expression<Func<T, object>>>? orderFields = null,
            string defaultSearchBy = "all",
            string defaultSortBy = "name",
            string? searchMode = null,
            bool applyOrdering = true)
        {
            // تجهيز القواميس (بحساسية أحرف غير مهمة)
            stringFields ??= new Dictionary<string, Expression<Func<T, string?>>>(StringComparer.OrdinalIgnoreCase);
            intFields ??= new Dictionary<string, Expression<Func<T, int>>>(StringComparer.OrdinalIgnoreCase);
            orderFields ??= new Dictionary<string, Expression<Func<T, object>>>(StringComparer.OrdinalIgnoreCase);

            var mode = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (mode != "starts" && mode != "ends")
                mode = "contains";

            // أسماء المفاتيح المختارة
            var sb = (searchBy ?? defaultSearchBy).Trim().ToLowerInvariant(); // مفتاح البحث
            var srt = (sort ?? defaultSortBy).Trim().ToLowerInvariant();       // مفتاح الترتيب
            var isDesc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            // ===== البحث =====
            if (!string.IsNullOrWhiteSpace(search))
            {
                var rawText = search.Trim();
                var text = NormalizeSearchText(rawText);

                // --- بحث "الكل": OR على كل stringFields (+ كل intFields لو النص رقم)
                if (sb == "all")
                {
                    // x => (Field1 match(text) || ... || IntField == n)
                    var param = Expression.Parameter(typeof(T), "x");
                    Expression? orExpr = null;

                    // نصوص: Field != null && مطابقة حسب searchMode (starts/contains/ends)
                    foreach (var sel in stringFields.Values)
                    {
                        var body = new ReplaceParam(sel.Parameters[0], param).Visit(sel.Body)!; // string?
                        var notNull = Expression.NotEqual(body, Expression.Constant(null, typeof(string)));
                        var match = BuildStringSearchMatch(body, text, mode);
                        var expr = Expression.AndAlso(notNull, match);
                        orExpr = orExpr == null ? expr : Expression.OrElse(orExpr, expr);
                    }

                    // أرقام: Field == n (لو أمكن تحويل النص إلى رقم)
                    if (intFields.Count > 0 && int.TryParse(text, out var n))
                    {
                        foreach (var sel in intFields.Values)
                        {
                            var body = new ReplaceParam(sel.Parameters[0], param).Visit(sel.Body)!; // int
                            var eq = Expression.Equal(body, Expression.Constant(n));
                            orExpr = orExpr == null ? eq : Expression.OrElse(orExpr, eq);
                        }
                    }

                    if (orExpr != null)
                    {
                        var lambda = Expression.Lambda<Func<T, bool>>(orExpr, param);
                        q = q.Where(lambda);
                    }
                }
                // --- بحث بمفتاح محدّد (نصي/رقمي)
                else if (intFields.TryGetValue(sb, out var intSel))
                {
                    var param = intSel.Parameters[0];
                    var bodyInt = intSel.Body;
                    var toString = Expression.Call(bodyInt, typeof(int).GetMethod(nameof(int.ToString), Type.EmptyTypes)!);
                    var matchStr = BuildStringSearchMatch(toString, text, mode);
                    Expression pred = matchStr;
                    if (mode == "contains" && int.TryParse(text, out var nExact))
                    {
                        var eq = Expression.Equal(bodyInt, Expression.Constant(nExact));
                        pred = Expression.OrElse(matchStr, eq);
                    }
                    var lambda = Expression.Lambda<Func<T, bool>>(pred, param);
                    q = q.Where(lambda);
                }
                else if (stringFields.TryGetValue(sb, out var strExpr))
                {
                    var param = strExpr.Parameters[0];
                    var member = strExpr.Body; // string?
                    var notNull = Expression.NotEqual(member, Expression.Constant(null, typeof(string)));
                    var match = BuildStringSearchMatch(member, text, mode);
                    var andAlso = Expression.AndAlso(notNull, match);
                    var lambda = Expression.Lambda<Func<T, bool>>(andAlso, param);
                    q = q.Where(lambda);
                }
                // لو مفتاح غير معرّف → لا شيء
            }

            // ===== الترتيب =====
            if (applyOrdering)
            {
                if (!orderFields.TryGetValue(srt, out var orderExpr))
                {
                    // جرّب الافتراضي، ثم أول متاح
                    if (!orderFields.TryGetValue(defaultSortBy, out orderExpr))
                    {
                        orderExpr =
                            orderFields.Values.FirstOrDefault()
                            ?? stringFields.Values.Select(e =>
                                Expression.Lambda<Func<T, object>>(Expression.Convert(e.Body, typeof(object)), e.Parameters))
                               .FirstOrDefault()
                            ?? intFields.Values.Select(e =>
                                Expression.Lambda<Func<T, object>>(Expression.Convert(e.Body, typeof(object)), e.Parameters))
                               .FirstOrDefault()
                            ?? throw new InvalidOperationException("لا يوجد أي حقل مهيأ للترتيب.");
                    }
                }

                q = isDesc ? q.OrderByDescending(orderExpr) : q.OrderBy(orderExpr);
            }

            return q;
        }

        private static Expression BuildStringSearchMatch(Expression stringMember, string text, string mode)
        {
            var constText = Expression.Constant(text);

            // Case-insensitive (and digit-normalized) match:
            // Apply ToLower() to the member; text already normalized.
            var toLower = typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;
            var loweredMember = Expression.Call(stringMember, toLower);

            return mode switch
            {
                "starts" => Expression.Call(
                    loweredMember,
                    typeof(string).GetMethod(nameof(string.StartsWith), new[] { typeof(string) })!,
                    constText),
                "ends" => Expression.Call(
                    loweredMember,
                    typeof(string).GetMethod(nameof(string.EndsWith), new[] { typeof(string) })!,
                    constText),
                _ => Expression.Call(
                    loweredMember,
                    typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!,
                    constText)
            };
        }

        /// <summary>مبدّل باراميتر للتعبيرات حتى تتوحّد في Where</summary>
        private sealed class ReplaceParam : ExpressionVisitor
        {
            private readonly ParameterExpression _oldParam;
            private readonly ParameterExpression _newParam;
            public ReplaceParam(ParameterExpression oldParam, ParameterExpression newParam)
            {
                _oldParam = oldParam; _newParam = newParam;
            }
            protected override Expression VisitParameter(ParameterExpression node)
                => node == _oldParam ? _newParam : base.VisitParameter(node);
        }
    }
}
