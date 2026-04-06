using System.Globalization;
using System.Linq;
using ERP.Models;

namespace ERP.Infrastructure
{
    /// <summary>
    /// فلتر عمود كود المستخدم الرقمي (filterCol_idExpr) — نفس منطق قوائم أخرى.
    /// </summary>
    public static class UserListNumericExpr
    {
        public static IQueryable<User> ApplyUserIdExpr(IQueryable<User> q, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return q;
            var expr = raw.Trim();
            if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                return q.Where(u => u.UserId <= smax);
            if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                return q.Where(u => u.UserId >= smin);
            if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                return q.Where(u => u.UserId < smax2);
            if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                return q.Where(u => u.UserId > smin2);
            if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
            {
                var separator = expr.Contains(':') ? ':' : '-';
                var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    if (a > b) (a, b) = (b, a);
                    return q.Where(u => u.UserId >= a && u.UserId <= b);
                }
            }
            if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                return q.Where(u => u.UserId == ex);
            return q;
        }
    }
}
