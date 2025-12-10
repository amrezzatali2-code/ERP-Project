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
            string defaultSortBy = "name")
        {
            // تجهيز القواميس (بحساسية أحرف غير مهمة)
            stringFields ??= new Dictionary<string, Expression<Func<T, string?>>>(StringComparer.OrdinalIgnoreCase);
            intFields ??= new Dictionary<string, Expression<Func<T, int>>>(StringComparer.OrdinalIgnoreCase);
            orderFields ??= new Dictionary<string, Expression<Func<T, object>>>(StringComparer.OrdinalIgnoreCase);

            // أسماء المفاتيح المختارة
            var sb = (searchBy ?? defaultSearchBy).Trim().ToLowerInvariant(); // مفتاح البحث
            var srt = (sort ?? defaultSortBy).Trim().ToLowerInvariant();       // مفتاح الترتيب
            var isDesc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            // ===== البحث =====
            if (!string.IsNullOrWhiteSpace(search))
            {
                var text = search.Trim();

                // --- بحث "الكل": OR على كل stringFields (+ كل intFields لو النص رقم)
                if (sb == "all")
                {
                    // x => (x.Field1.Contains(text) || x.Field2.Contains(text) || ... || x.IntField == n)
                    var param = Expression.Parameter(typeof(T), "x");
                    Expression? orExpr = null;

                    // نصوص: Field != null && Field.Contains(text)
                    foreach (var sel in stringFields.Values)
                    {
                        var body = new ReplaceParam(sel.Parameters[0], param).Visit(sel.Body)!; // string?
                        var notNull = Expression.NotEqual(body, Expression.Constant(null, typeof(string)));
                        var contains = Expression.Call(
                            body,
                            typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!,
                            Expression.Constant(text)
                        );
                        var expr = Expression.AndAlso(notNull, contains);
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
                else if (intFields.TryGetValue(sb, out var intExpr))
                {
                    if (int.TryParse(text, out var n))
                    {
                        var param = intExpr.Parameters[0];
                        var bodyEq = Expression.Equal(intExpr.Body, Expression.Constant(n));
                        var lambda = Expression.Lambda<Func<T, bool>>(bodyEq, param);
                        q = q.Where(lambda);
                    }
                    // لو النص ليس رقمًا نتجاهل فلترة الرقم
                }
                else if (stringFields.TryGetValue(sb, out var strExpr))
                {
                    var param = strExpr.Parameters[0];
                    var member = strExpr.Body; // string?
                    var notNull = Expression.NotEqual(member, Expression.Constant(null, typeof(string)));
                    var contains = Expression.Call(
                        member,
                        typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!,
                        Expression.Constant(text)
                    );
                    var andAlso = Expression.AndAlso(notNull, contains);
                    var lambda = Expression.Lambda<Func<T, bool>>(andAlso, param);
                    q = q.Where(lambda);
                }
                // لو مفتاح غير معرّف → لا شيء
            }

            // ===== الترتيب =====
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
            return q;
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
