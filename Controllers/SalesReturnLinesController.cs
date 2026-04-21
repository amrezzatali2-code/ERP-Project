using System;
using System.Collections.Generic;                    // القوائم List / Dictionary
using System.Globalization;                          // CultureInfo لتنسيق الأرقام فى التصدير
using System.Linq;                                   // أوامر LINQ مثل Where / OrderBy
using System.Linq.Expressions;                       // Expression<Func<...>> لـ ApplySearchSort
using System.Text;                                   // StringBuilder + Encoding
using System.Threading.Tasks;                        // async / await
using ClosedXML.Excel;                               // علشان نصدر Excel فعلي
using ERP.Data;                                      // AppDbContext
using ERP.Filters;
using ERP.Infrastructure;                            // ApplySearchSort + PagedResult
using ERP.Models;                                    // SalesReturnLine , SalesReturn
using ERP.Security;
using ERP.Services;                                  // DocumentTotalsService لإعادة تجميع الهيدر
using Microsoft.AspNetCore.Mvc;                      // Controller / IActionResult
using Microsoft.EntityFrameworkCore;                 // Include / AsNoTracking

namespace ERP.Controllers
{
    /// <summary>
    /// شاشة "أصناف مرتجعات البيع" بنظام القوائم الموحد:
    /// - عرض سطور المرتجع مع بحث وترتيب وتقسيم صفحات.
    /// - فلترة حسب رقم المرتجع أو رقم السطر أو التاريخ.
    /// - حذف سطر/عدة أسطر بشرط أن يكون الهيدر في حالة Draft فقط.
    /// - تصدير CSV أو Excel.
    /// </summary>
    [RequirePermission("SalesReturnLines.Index")]
    public class SalesReturnLinesController : Controller
    {
        // متغير: سياق قاعدة البيانات للتعامل مع الجداول
        private readonly AppDbContext _context;

        // متغير: خدمة إعادة تجميع إجماليات الهيدر (SalesReturn)
        private readonly DocumentTotalsService _docTotals;

        // فاصل لقيم فلاتر الأعمدة (نفسه المستخدم فى القوائم الأخرى)
        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        public SalesReturnLinesController(AppDbContext context,
                                          DocumentTotalsService docTotals)
        {
            _context = context;        // كائن الاتصال بقاعدة البيانات
            _docTotals = docTotals;    // سيرفيس إعادة تجميع إجماليات مرتجع البيع
        }

        // ---------------------------------------------------------
        // دالة داخلية: بناء الاستعلام مع كل الفلاتر + البحث + الترتيب
        // نستخدمها فى Index و Export حتى لا نكرر الكود.
        // ---------------------------------------------------------
        private IQueryable<SalesReturnLine> BuildQuery(
            int? srId,
            string? search,
            string? searchBy,
            string? searchMode,
            string? sort,
            string? dir,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            int? fromCode,
            int? toCode)
        {
            // 1) الاستعلام الأساسي: سطور المرتجع + الهيدر + الصنف + عميل + منطقة (للعرض والبحث)
            IQueryable<SalesReturnLine> q = _context.SalesReturnLines
                .Include(l => l.Product)
                .Include(l => l.SalesReturn)
                    .ThenInclude(sr => sr.Customer)
                        .ThenInclude(c => c.Area)
                .AsNoTracking();

            // 2) فلترة برقم مرتجع معيّن (لو جاي من شاشة الهيدر)
            if (srId.HasValue)
            {
                q = q.Where(l => l.SRId == srId.Value);
            }

            // 3) فلتر من رقم سطر / إلى رقم سطر
            if (fromCode.HasValue)
            {
                q = q.Where(l => l.LineNo >= fromCode.Value);
            }
            if (toCode.HasValue)
            {
                q = q.Where(l => l.LineNo <= toCode.Value);
            }

            // 4) فلتر التاريخ/الوقت على مستوى الهيدر (SRDate)
            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                DateTime from = fromDate.Value;
                DateTime to = toDate.Value;

                q = q.Where(l =>
                    l.SalesReturn != null &&
                    l.SalesReturn.SRDate >= from &&
                    l.SalesReturn.SRDate <= to);
            }

            // 5) خرائط الحقول للبحث والفرز (نفس فكرة أوامر البيع)

            // الحقول النصية
            var stringFields =
                new Dictionary<string, Expression<Func<SalesReturnLine, string?>>>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["batch"] = x => x.BatchNo ?? "",
                    ["expiry"] = x => x.Expiry.HasValue
                                     ? x.Expiry.Value.ToString("yyyy-MM-dd")
                                     : "",
                    ["prodname"] = x => x.Product != null ? x.Product.ProdName : "",
                    ["customer"] = x => x.SalesReturn != null && x.SalesReturn.Customer != null
                        ? x.SalesReturn.Customer.CustomerName
                        : "",
                    ["createdby"] = x => x.SalesReturn != null ? x.SalesReturn.CreatedBy : "",
                    ["area"] = x => x.SalesReturn != null && x.SalesReturn.Customer != null && x.SalesReturn.Customer.Area != null
                        ? x.SalesReturn.Customer.Area.AreaName
                        : ""
                };

            // الحقول الرقمية
            var intFields =
                new Dictionary<string, Expression<Func<SalesReturnLine, int>>>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["srid"] = x => x.SRId,    // رقم المرتجع
                    ["lineno"] = x => x.LineNo,  // رقم السطر
                    ["prod"] = x => x.ProdId   // كود الصنف
                };

            // مفاتيح الترتيب المسموحة
            var orderFields =
                new Dictionary<string, Expression<Func<SalesReturnLine, object>>>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["SRId"] = x => x.SRId,
                    ["SalesInvoiceId"] = x => x.SalesInvoiceId ?? 0,
                    ["LineNo"] = x => x.LineNo,
                    ["ProdId"] = x => x.ProdId,
                    ["Qty"] = x => x.Qty,
                    ["PriceRetail"] = x => x.PriceRetail,
                    ["UnitSalePrice"] = x => x.UnitSalePrice,
                    ["LineNetTotal"] = x => x.LineNetTotal,
                    ["BatchNo"] = x => x.BatchNo ?? "",
                    ["Expiry"] = x => x.Expiry ?? DateTime.MinValue,
                    ["Status"] = x => x.SalesReturn != null ? x.SalesReturn.Status ?? "" : "",
                    ["ProdName"] = x => x.Product != null ? x.Product.ProdName ?? "" : "",
                    ["CustomerName"] = x => x.SalesReturn != null && x.SalesReturn.Customer != null
                        ? x.SalesReturn.Customer.CustomerName ?? ""
                        : "",
                    ["CreatedBy"] = x => x.SalesReturn != null ? x.SalesReturn.CreatedBy : "",
                    ["AreaName"] = x => x.SalesReturn != null && x.SalesReturn.Customer != null && x.SalesReturn.Customer.Area != null
                        ? x.SalesReturn.Customer.Area.AreaName ?? ""
                        : ""
                };

            // 6) تطبيق دالة البحث/الترتيب الموحدة
            q = q.ApplySearchSort(
                    search: search,
                    searchBy: searchBy,
                    searchMode: searchMode,
                    sort: sort,
                    dir: dir,
                    stringFields: stringFields,
                    intFields: intFields,
                    orderFields: orderFields,
                    defaultSearchBy: "all",
                    defaultSortBy: "SRId");

            return q;
        }

        /// <summary>
        /// فلتر رقمي بنمط قائمة الأصناف (filterCol_*Expr): رقم مطابق أو &lt;= &gt;= &lt; &gt; أو نطاق min:max — نفس منطق ProductsController للـ id/price.
        /// </summary>
        private static IQueryable<SalesReturnLine> ApplyProductsStyleIntExpr(
            IQueryable<SalesReturnLine> q, string? raw, string column)
        {
            if (string.IsNullOrWhiteSpace(raw)) return q;
            var expr = raw.Trim();

            // —— رقم المرتجع ——
            if (string.Equals(column, "srid", StringComparison.OrdinalIgnoreCase))
            {
                if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                    return q.Where(l => l.SRId <= smax);
                if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                    return q.Where(l => l.SRId >= smin);
                if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                    return q.Where(l => l.SRId < smax2);
                if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                    return q.Where(l => l.SRId > smin2);
                if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
                {
                    var separator = expr.Contains(':') ? ':' : '-';
                    var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2
                        && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                        && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                    {
                        if (a > b) (a, b) = (b, a);
                        return q.Where(l => l.SRId >= a && l.SRId <= b);
                    }
                }
                if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                    return q.Where(l => l.SRId == ex);
                return q;
            }

            // —— رقم فاتورة الصنف (nullable) ——
            if (string.Equals(column, "siid", StringComparison.OrdinalIgnoreCase))
            {
                if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                    return q.Where(l => l.SalesInvoiceId.HasValue && l.SalesInvoiceId.Value <= smax);
                if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                    return q.Where(l => l.SalesInvoiceId.HasValue && l.SalesInvoiceId.Value >= smin);
                if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                    return q.Where(l => l.SalesInvoiceId.HasValue && l.SalesInvoiceId.Value < smax2);
                if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                    return q.Where(l => l.SalesInvoiceId.HasValue && l.SalesInvoiceId.Value > smin2);
                if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
                {
                    var separator = expr.Contains(':') ? ':' : '-';
                    var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2
                        && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                        && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                    {
                        if (a > b) (a, b) = (b, a);
                        return q.Where(l => l.SalesInvoiceId.HasValue && l.SalesInvoiceId.Value >= a && l.SalesInvoiceId.Value <= b);
                    }
                }
                if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                    return q.Where(l => l.SalesInvoiceId == ex);
                return q;
            }

            // —— رقم السطر، كود الصنف، الكمية ——
            if (string.Equals(column, "lineno", StringComparison.OrdinalIgnoreCase))
            {
                if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                    return q.Where(l => l.LineNo <= smax);
                if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                    return q.Where(l => l.LineNo >= smin);
                if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                    return q.Where(l => l.LineNo < smax2);
                if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                    return q.Where(l => l.LineNo > smin2);
                if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
                {
                    var separator = expr.Contains(':') ? ':' : '-';
                    var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2
                        && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                        && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                    {
                        if (a > b) (a, b) = (b, a);
                        return q.Where(l => l.LineNo >= a && l.LineNo <= b);
                    }
                }
                if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                    return q.Where(l => l.LineNo == ex);
                return q;
            }

            if (string.Equals(column, "prod", StringComparison.OrdinalIgnoreCase))
            {
                if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                    return q.Where(l => l.ProdId <= smax);
                if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                    return q.Where(l => l.ProdId >= smin);
                if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                    return q.Where(l => l.ProdId < smax2);
                if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                    return q.Where(l => l.ProdId > smin2);
                if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
                {
                    var separator = expr.Contains(':') ? ':' : '-';
                    var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2
                        && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                        && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                    {
                        if (a > b) (a, b) = (b, a);
                        return q.Where(l => l.ProdId >= a && l.ProdId <= b);
                    }
                }
                if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                    return q.Where(l => l.ProdId == ex);
                return q;
            }

            if (string.Equals(column, "qty", StringComparison.OrdinalIgnoreCase))
            {
                if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                    return q.Where(l => l.Qty <= smax);
                if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                    return q.Where(l => l.Qty >= smin);
                if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                    return q.Where(l => l.Qty < smax2);
                if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                    return q.Where(l => l.Qty > smin2);
                if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
                {
                    var separator = expr.Contains(':') ? ':' : '-';
                    var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2
                        && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                        && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                    {
                        if (a > b) (a, b) = (b, a);
                        return q.Where(l => l.Qty >= a && l.Qty <= b);
                    }
                }
                if (int.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                    return q.Where(l => l.Qty == ex);
                return q;
            }

            return q;
        }

        private static IQueryable<SalesReturnLine> ApplyProductsStyleDecimalExpr(
            IQueryable<SalesReturnLine> q, string? raw, string column)
        {
            if (string.IsNullOrWhiteSpace(raw)) return q;
            var expr = raw.Trim();

            if (string.Equals(column, "priceretail", StringComparison.OrdinalIgnoreCase))
            {
                if (expr.StartsWith("<=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                    return q.Where(l => l.PriceRetail <= smax);
                if (expr.StartsWith(">=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                    return q.Where(l => l.PriceRetail >= smin);
                if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                    return q.Where(l => l.PriceRetail < smax2);
                if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                    return q.Where(l => l.PriceRetail > smin2);
                if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
                {
                    var separator = expr.Contains(':') ? ':' : '-';
                    var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2
                        && decimal.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                        && decimal.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                    {
                        if (a > b) (a, b) = (b, a);
                        return q.Where(l => l.PriceRetail >= a && l.PriceRetail <= b);
                    }
                }
                if (decimal.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                    return q.Where(l => l.PriceRetail == ex);
                return q;
            }

            if (string.Equals(column, "unitprice", StringComparison.OrdinalIgnoreCase))
            {
                if (expr.StartsWith("<=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                    return q.Where(l => l.UnitSalePrice <= smax);
                if (expr.StartsWith(">=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                    return q.Where(l => l.UnitSalePrice >= smin);
                if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                    return q.Where(l => l.UnitSalePrice < smax2);
                if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                    return q.Where(l => l.UnitSalePrice > smin2);
                if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
                {
                    var separator = expr.Contains(':') ? ':' : '-';
                    var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2
                        && decimal.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                        && decimal.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                    {
                        if (a > b) (a, b) = (b, a);
                        return q.Where(l => l.UnitSalePrice >= a && l.UnitSalePrice <= b);
                    }
                }
                if (decimal.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                    return q.Where(l => l.UnitSalePrice == ex);
                return q;
            }

            if (string.Equals(column, "net", StringComparison.OrdinalIgnoreCase))
            {
                if (expr.StartsWith("<=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax))
                    return q.Where(l => l.LineNetTotal <= smax);
                if (expr.StartsWith(">=") && expr.Length > 2 && decimal.TryParse(expr.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin))
                    return q.Where(l => l.LineNetTotal >= smin);
                if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smax2))
                    return q.Where(l => l.LineNetTotal < smax2);
                if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && decimal.TryParse(expr.AsSpan(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var smin2))
                    return q.Where(l => l.LineNetTotal > smin2);
                if ((expr.Contains(':') || (expr.Contains('-') && expr.IndexOf('-', StringComparison.Ordinal) > 0)) && !expr.StartsWith("-"))
                {
                    var separator = expr.Contains(':') ? ':' : '-';
                    var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2
                        && decimal.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                        && decimal.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                    {
                        if (a > b) (a, b) = (b, a);
                        return q.Where(l => l.LineNetTotal >= a && l.LineNetTotal <= b);
                    }
                }
                if (decimal.TryParse(expr, NumberStyles.Any, CultureInfo.InvariantCulture, out var ex))
                    return q.Where(l => l.LineNetTotal == ex);
                return q;
            }

            return q;
        }

        /// <summary>
        /// فلاتر أعمدة: قيم متعددة بـ | أو صيغة رقمية G:≥ L:≤ R:من،إلى
        /// </summary>
        private IQueryable<SalesReturnLine> ApplyColumnFilters(
            IQueryable<SalesReturnLine> q,
            string? filterCol_srid,
            string? filterCol_siid,
            string? filterCol_lineno,
            string? filterCol_prod,
            string? filterCol_qty,
            string? filterCol_priceretail,
            string? filterCol_unitprice,
            string? filterCol_net,
            string? filterCol_batch,
            string? filterCol_expiry,
            string? filterCol_status,
            string? filterCol_prodname,
            string? filterCol_customer,
            string? filterCol_createdby,
            string? filterCol_area,
            string? filterCol_sridExpr = null,
            string? filterCol_siidExpr = null,
            string? filterCol_linenoExpr = null,
            string? filterCol_prodExpr = null,
            string? filterCol_qtyExpr = null,
            string? filterCol_priceretailExpr = null,
            string? filterCol_unitpriceExpr = null,
            string? filterCol_netExpr = null)
        {
            if (!string.IsNullOrWhiteSpace(filterCol_sridExpr))
                q = ApplyProductsStyleIntExpr(q, filterCol_sridExpr, "srid");
            else if (!string.IsNullOrWhiteSpace(filterCol_srid))
            {
                if (TryParseIntAdvanced(filterCol_srid, out var intVals, out var ig, out var il, out var ir1, out var ir2))
                {
                    if (ig.HasValue) q = q.Where(l => l.SRId >= ig.Value);
                    else if (il.HasValue) q = q.Where(l => l.SRId <= il.Value);
                    else if (ir1.HasValue && ir2.HasValue) q = q.Where(l => l.SRId >= ir1.Value && l.SRId <= ir2.Value);
                    else if (intVals.Count > 0) q = q.Where(l => intVals.Contains(l.SRId));
                }
            }

            if (!string.IsNullOrWhiteSpace(filterCol_siidExpr))
                q = ApplyProductsStyleIntExpr(q, filterCol_siidExpr, "siid");
            else if (!string.IsNullOrWhiteSpace(filterCol_siid))
            {
                if (TryParseIntAdvanced(filterCol_siid, out var intVals, out var ig, out var il, out var ir1, out var ir2))
                {
                    if (ig.HasValue) q = q.Where(l => l.SalesInvoiceId.HasValue && l.SalesInvoiceId.Value >= ig.Value);
                    else if (il.HasValue) q = q.Where(l => l.SalesInvoiceId.HasValue && l.SalesInvoiceId.Value <= il.Value);
                    else if (ir1.HasValue && ir2.HasValue) q = q.Where(l => l.SalesInvoiceId.HasValue && l.SalesInvoiceId.Value >= ir1.Value && l.SalesInvoiceId.Value <= ir2.Value);
                    else if (intVals.Count > 0) q = q.Where(l => l.SalesInvoiceId.HasValue && intVals.Contains(l.SalesInvoiceId.Value));
                }
            }

            if (!string.IsNullOrWhiteSpace(filterCol_linenoExpr))
                q = ApplyProductsStyleIntExpr(q, filterCol_linenoExpr, "lineno");
            else if (!string.IsNullOrWhiteSpace(filterCol_lineno))
            {
                if (TryParseIntAdvanced(filterCol_lineno, out var intVals, out var ig, out var il, out var ir1, out var ir2))
                {
                    if (ig.HasValue) q = q.Where(l => l.LineNo >= ig.Value);
                    else if (il.HasValue) q = q.Where(l => l.LineNo <= il.Value);
                    else if (ir1.HasValue && ir2.HasValue) q = q.Where(l => l.LineNo >= ir1.Value && l.LineNo <= ir2.Value);
                    else if (intVals.Count > 0) q = q.Where(l => intVals.Contains(l.LineNo));
                }
            }

            if (!string.IsNullOrWhiteSpace(filterCol_prodExpr))
                q = ApplyProductsStyleIntExpr(q, filterCol_prodExpr, "prod");
            else if (!string.IsNullOrWhiteSpace(filterCol_prod))
            {
                if (TryParseIntAdvanced(filterCol_prod, out var intVals, out var ig, out var il, out var ir1, out var ir2))
                {
                    if (ig.HasValue) q = q.Where(l => l.ProdId >= ig.Value);
                    else if (il.HasValue) q = q.Where(l => l.ProdId <= il.Value);
                    else if (ir1.HasValue && ir2.HasValue) q = q.Where(l => l.ProdId >= ir1.Value && l.ProdId <= ir2.Value);
                    else if (intVals.Count > 0) q = q.Where(l => intVals.Contains(l.ProdId));
                }
            }

            if (!string.IsNullOrWhiteSpace(filterCol_qtyExpr))
                q = ApplyProductsStyleIntExpr(q, filterCol_qtyExpr, "qty");
            else if (!string.IsNullOrWhiteSpace(filterCol_qty))
            {
                if (TryParseIntAdvanced(filterCol_qty, out var intVals, out var ig, out var il, out var ir1, out var ir2))
                {
                    if (ig.HasValue) q = q.Where(l => l.Qty >= ig.Value);
                    else if (il.HasValue) q = q.Where(l => l.Qty <= il.Value);
                    else if (ir1.HasValue && ir2.HasValue) q = q.Where(l => l.Qty >= ir1.Value && l.Qty <= ir2.Value);
                    else if (intVals.Count > 0) q = q.Where(l => intVals.Contains(l.Qty));
                }
            }

            if (!string.IsNullOrWhiteSpace(filterCol_priceretailExpr))
                q = ApplyProductsStyleDecimalExpr(q, filterCol_priceretailExpr, "priceretail");
            else if (!string.IsNullOrWhiteSpace(filterCol_priceretail))
            {
                if (TryParseDecimalAdvanced(filterCol_priceretail, out var decVals, out var dg, out var dl, out var dr1, out var dr2))
                {
                    if (dg.HasValue) q = q.Where(l => l.PriceRetail >= dg.Value);
                    else if (dl.HasValue) q = q.Where(l => l.PriceRetail <= dl.Value);
                    else if (dr1.HasValue && dr2.HasValue) q = q.Where(l => l.PriceRetail >= dr1.Value && l.PriceRetail <= dr2.Value);
                    else if (decVals.Count > 0) q = q.Where(l => decVals.Contains(l.PriceRetail));
                }
            }

            if (!string.IsNullOrWhiteSpace(filterCol_unitpriceExpr))
                q = ApplyProductsStyleDecimalExpr(q, filterCol_unitpriceExpr, "unitprice");
            else if (!string.IsNullOrWhiteSpace(filterCol_unitprice))
            {
                if (TryParseDecimalAdvanced(filterCol_unitprice, out var decVals, out var dg, out var dl, out var dr1, out var dr2))
                {
                    if (dg.HasValue) q = q.Where(l => l.UnitSalePrice >= dg.Value);
                    else if (dl.HasValue) q = q.Where(l => l.UnitSalePrice <= dl.Value);
                    else if (dr1.HasValue && dr2.HasValue) q = q.Where(l => l.UnitSalePrice >= dr1.Value && l.UnitSalePrice <= dr2.Value);
                    else if (decVals.Count > 0) q = q.Where(l => decVals.Contains(l.UnitSalePrice));
                }
            }

            if (!string.IsNullOrWhiteSpace(filterCol_netExpr))
                q = ApplyProductsStyleDecimalExpr(q, filterCol_netExpr, "net");
            else if (!string.IsNullOrWhiteSpace(filterCol_net))
            {
                if (TryParseDecimalAdvanced(filterCol_net, out var decVals, out var dg, out var dl, out var dr1, out var dr2))
                {
                    if (dg.HasValue) q = q.Where(l => l.LineNetTotal >= dg.Value);
                    else if (dl.HasValue) q = q.Where(l => l.LineNetTotal <= dl.Value);
                    else if (dr1.HasValue && dr2.HasValue) q = q.Where(l => l.LineNetTotal >= dr1.Value && l.LineNetTotal <= dr2.Value);
                    else if (decVals.Count > 0) q = q.Where(l => decVals.Contains(l.LineNetTotal));
                }
            }

            if (!string.IsNullOrWhiteSpace(filterCol_batch))
            {
                var vals = filterCol_batch.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(l => vals.Contains(l.BatchNo ?? ""));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_expiry))
            {
                var dates = filterCol_expiry.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => DateTime.TryParse(v.Trim(), out var d) ? d.Date : (DateTime?)null)
                    .Where(d => d.HasValue).Select(d => d!.Value)
                    .ToList();
                if (dates.Count > 0)
                    q = q.Where(l => l.Expiry.HasValue && dates.Contains(l.Expiry.Value.Date));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_status))
            {
                var vals = filterCol_status.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(l => l.SalesReturn != null && l.SalesReturn.Status != null && vals.Contains(l.SalesReturn.Status));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_prodname))
            {
                var vals = filterCol_prodname.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => v.Length > 0)
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(l => l.Product != null && vals.Contains(l.Product.ProdName));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_customer))
            {
                var vals = filterCol_customer.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => v.Length > 0)
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(l => l.SalesReturn != null && l.SalesReturn.Customer != null && vals.Contains(l.SalesReturn.Customer.CustomerName));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_createdby))
            {
                var vals = filterCol_createdby.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => v.Length > 0)
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(l => l.SalesReturn != null && vals.Contains(l.SalesReturn.CreatedBy));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_area))
            {
                var vals = filterCol_area.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => v.Length > 0)
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(l => l.SalesReturn != null && l.SalesReturn.Customer != null && l.SalesReturn.Customer.Area != null && vals.Contains(l.SalesReturn.Customer.Area.AreaName));
            }

            return q;
        }

        private static bool TryParseDecimalAdvanced(string raw, out List<decimal> inList, out decimal? gte, out decimal? lte, out decimal? r1, out decimal? r2)
        {
            inList = new List<decimal>();
            gte = lte = r1 = r2 = null;
            raw = (raw ?? "").Trim();
            if (raw.StartsWith("G:", StringComparison.OrdinalIgnoreCase) && decimal.TryParse(raw.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var gv))
            {
                gte = gv;
                return true;
            }
            if (raw.StartsWith("L:", StringComparison.OrdinalIgnoreCase) && decimal.TryParse(raw.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var lv))
            {
                lte = lv;
                return true;
            }
            if (raw.StartsWith("R:", StringComparison.OrdinalIgnoreCase))
            {
                var tail = raw.Substring(2);
                var parts = tail.Split(new[] { ',', ';' }, 2, StringSplitOptions.None);
                if (parts.Length == 2
                    && decimal.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && decimal.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    r1 = Math.Min(a, b);
                    r2 = Math.Max(a, b);
                    return true;
                }
            }
            foreach (var seg in raw.Split(new[] { '|', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (decimal.TryParse(seg.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                    inList.Add(d);
            }
            return true;
        }

        private static bool TryParseIntAdvanced(string raw, out List<int> inList, out int? gte, out int? lte, out int? r1, out int? r2)
        {
            inList = new List<int>();
            gte = lte = r1 = r2 = null;
            raw = (raw ?? "").Trim();
            if (raw.StartsWith("G:", StringComparison.OrdinalIgnoreCase) && int.TryParse(raw.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var gv))
            {
                gte = gv;
                return true;
            }
            if (raw.StartsWith("L:", StringComparison.OrdinalIgnoreCase) && int.TryParse(raw.AsSpan(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var lv))
            {
                lte = lv;
                return true;
            }
            if (raw.StartsWith("R:", StringComparison.OrdinalIgnoreCase))
            {
                var tail = raw.Substring(2);
                var parts = tail.Split(new[] { ',', ';' }, 2, StringSplitOptions.None);
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
                    && int.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    r1 = Math.Min(a, b);
                    r2 = Math.Max(a, b);
                    return true;
                }
            }
            foreach (var seg in raw.Split(new[] { '|', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(seg.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                    inList.Add(d);
            }
            return true;
        }

        // =========================
        // INDEX: قائمة سطور مرتجعات البيع
        // =========================
        public async Task<IActionResult> Index(
            int? srId,                      // رقم مرتجع معين (لو جاي من شاشة الهيدر)
            string? search,                 // نص البحث
            string? searchBy = "all",       // اسم الحقل الذي نبحث فيه
            string? searchMode = "contains", // starts | contains | ends
            string? sort = "SRId",          // عمود الترتيب
            string? dir = "asc",            // اتجاه الترتيب asc/desc
            bool useDateRange = false,      // هل نفعّل فلتر التاريخ؟
            DateTime? fromDate = null,      // من تاريخ (SRDate للهيدر)
            DateTime? toDate = null,        // إلى تاريخ
            int? fromCode = null,           // من رقم سطر
            int? toCode = null,             // إلى رقم سطر
            string? filterCol_srid = null,
            string? filterCol_siid = null,
            string? filterCol_lineno = null,
            string? filterCol_prod = null,
            string? filterCol_qty = null,
            string? filterCol_priceretail = null,
            string? filterCol_unitprice = null,
            string? filterCol_net = null,
            string? filterCol_batch = null,
            string? filterCol_expiry = null,
            string? filterCol_status = null,
            string? filterCol_prodname = null,
            string? filterCol_customer = null,
            string? filterCol_createdby = null,
            string? filterCol_area = null,
            string? filterCol_sridExpr = null,
            string? filterCol_siidExpr = null,
            string? filterCol_linenoExpr = null,
            string? filterCol_prodExpr = null,
            string? filterCol_qtyExpr = null,
            string? filterCol_priceretailExpr = null,
            string? filterCol_unitpriceExpr = null,
            string? filterCol_netExpr = null,
            int page = 1,
            int pageSize = 10
        )
        {
            var pageSizeQuery = Request.Query["pageSize"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageSizeQuery) && int.TryParse(pageSizeQuery, out var psVal))
                pageSize = psVal;
            if (pageSize < 0) pageSize = 10;
            if (pageSize > 0 && pageSize != 10 && pageSize != 25 && pageSize != 50 && pageSize != 100 && pageSize != 200)
                pageSize = 10;
            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm != "starts" && sm != "ends") sm = "contains";

            var q = BuildQuery(
                srId,
                search, searchBy, sm,
                sort, dir,
                useDateRange, fromDate, toDate,
                fromCode, toCode);

            q = ApplyColumnFilters(
                q,
                filterCol_srid,
                filterCol_siid,
                filterCol_lineno,
                filterCol_prod,
                filterCol_qty,
                filterCol_priceretail,
                filterCol_unitprice,
                filterCol_net,
                filterCol_batch,
                filterCol_expiry,
                filterCol_status,
                filterCol_prodname,
                filterCol_customer,
                filterCol_createdby,
                filterCol_area,
                filterCol_sridExpr,
                filterCol_siidExpr,
                filterCol_linenoExpr,
                filterCol_prodExpr,
                filterCol_qtyExpr,
                filterCol_priceretailExpr,
                filterCol_unitpriceExpr,
                filterCol_netExpr);

            var dirNorm = (dir?.ToLower() == "asc") ? "asc" : "desc";
            bool descending = dirNorm == "desc";

            var totalCount = await q.CountAsync();
            int totalQtyFiltered = 0;
            decimal totalRetailGrossFiltered = 0m;
            decimal totalNetFiltered = 0m;
            if (totalCount > 0)
            {
                totalQtyFiltered = await q.SumAsync(l => l.Qty);
                totalRetailGrossFiltered = await q.SumAsync(l => l.Qty * l.PriceRetail);
                totalNetFiltered = await q.SumAsync(l => l.LineNetTotal);
            }
            int effectivePageSize = pageSize;
            if (pageSize == 0)
            {
                effectivePageSize = totalCount == 0 ? 10 : Math.Min(totalCount, 100_000);
                page = 1;
            }
            if (page < 1) page = 1;
            var skip = (page - 1) * effectivePageSize;
            if (totalCount > 0 && effectivePageSize > 0 && skip >= totalCount)
            {
                page = Math.Max(1, (int)Math.Ceiling((double)totalCount / effectivePageSize));
                skip = (page - 1) * effectivePageSize;
            }

            var items = await q.Skip(skip).Take(effectivePageSize).ToListAsync();

            var model = new PagedResult<SalesReturnLine>(items, page, pageSize, totalCount)
            {
                Search = search,
                SearchBy = searchBy,
                SortColumn = sort,
                SortDescending = descending,
                UseDateRange = useDateRange,
                FromDate = fromDate,
                ToDate = toDate
            };

            ViewBag.FilterSRId = srId;
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.DateField = "SRDate";

            ViewBag.Search = search ?? "";
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.SearchMode = sm;
            ViewBag.Sort = sort ?? "SRId";
            ViewBag.Dir = dirNorm;

            ViewBag.FilterCol_srid = filterCol_srid ?? string.Empty;
            ViewBag.FilterCol_siid = filterCol_siid ?? string.Empty;
            ViewBag.FilterCol_lineno = filterCol_lineno ?? string.Empty;
            ViewBag.FilterCol_prod = filterCol_prod ?? string.Empty;
            ViewBag.FilterCol_qty = filterCol_qty ?? string.Empty;
            ViewBag.FilterCol_priceretail = filterCol_priceretail ?? string.Empty;
            ViewBag.FilterCol_unitprice = filterCol_unitprice ?? string.Empty;
            ViewBag.FilterCol_net = filterCol_net ?? string.Empty;
            ViewBag.FilterCol_batch = filterCol_batch ?? string.Empty;
            ViewBag.FilterCol_expiry = filterCol_expiry ?? string.Empty;
            ViewBag.FilterCol_status = filterCol_status ?? string.Empty;
            ViewBag.FilterCol_prodname = filterCol_prodname ?? string.Empty;
            ViewBag.FilterCol_customer = filterCol_customer ?? string.Empty;
            ViewBag.FilterCol_createdby = filterCol_createdby ?? string.Empty;
            ViewBag.FilterCol_area = filterCol_area ?? string.Empty;

            ViewBag.FilterCol_sridExpr = filterCol_sridExpr ?? string.Empty;
            ViewBag.FilterCol_siidExpr = filterCol_siidExpr ?? string.Empty;
            ViewBag.FilterCol_linenoExpr = filterCol_linenoExpr ?? string.Empty;
            ViewBag.FilterCol_prodExpr = filterCol_prodExpr ?? string.Empty;
            ViewBag.FilterCol_qtyExpr = filterCol_qtyExpr ?? string.Empty;
            ViewBag.FilterCol_priceretailExpr = filterCol_priceretailExpr ?? string.Empty;
            ViewBag.FilterCol_unitpriceExpr = filterCol_unitpriceExpr ?? string.Empty;
            ViewBag.FilterCol_netExpr = filterCol_netExpr ?? string.Empty;

            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalCount = model.TotalCount;
            ViewBag.TotalQtyFiltered = totalQtyFiltered;
            ViewBag.TotalRetailGrossFiltered = totalRetailGrossFiltered;
            ViewBag.TotalNetFiltered = totalNetFiltered;

            return View(model);
        }

        // =========================
        // DELETE: حذف سطر واحد
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int srId, int lineNo)
        {
            // نجيب السطر مع الهيدر للتأكد من حالة المرتجع
            var line = await _context.SalesReturnLines
                .Include(l => l.SalesReturn)
                .FirstOrDefaultAsync(l => l.SRId == srId && l.LineNo == lineNo);

            if (line == null)
            {
                TempData["error"] = "السطر المطلوب غير موجود.";
                return RedirectToAction(nameof(Index), new { srId });
            }

            // التحقق من أن حالة المرتجع "غير مرحلة" (مع دعم القيمة القديمة Draft)
            var status = line.SalesReturn?.Status ?? "";
            if (!string.Equals(status, "Draft", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(status, "غير مرحلة", StringComparison.OrdinalIgnoreCase))
            {
                TempData["error"] = "لا يمكن حذف سطر من مرتجع حالته ليست غير مرحلة.";
                return RedirectToAction(nameof(Index), new { srId });
            }

            try
            {
                _context.SalesReturnLines.Remove(line);
                await _context.SaveChangesAsync();

                // بعد الحذف: إعادة تجميع إجماليات هيدر مرتجع البيع
                await _docTotals.RecalcSalesReturnTotalsAsync(srId);

                TempData["ok"] = "تم حذف السطر بنجاح.";
            }
            catch (Exception ex)
            {
                TempData["error"] = "تعذر حذف السطر: " + ex.Message;
            }

            return RedirectToAction(nameof(Index), new { srId });
        }

        // =========================
        // BULK DELETE: حذف عدة أسطر معًا
        // selectedKeys: "SRId:LineNo,SRId:LineNo,..."
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedKeys)
        {
            if (string.IsNullOrWhiteSpace(selectedKeys))
            {
                TempData["error"] = "لم يتم اختيار أي أسطر للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var pairs = selectedKeys
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(k =>
                {
                    var parts = k.Split(':');
                    if (parts.Length != 2) return (srId: (int?)null, lineNo: (int?)null);

                    bool ok1 = int.TryParse(parts[0], out int s);
                    bool ok2 = int.TryParse(parts[1], out int l);
                    return (srId: ok1 ? s : (int?)null, lineNo: ok2 ? l : (int?)null);
                })
                .Where(p => p.srId.HasValue && p.lineNo.HasValue)
                .Select(p => new { SRId = p.srId!.Value, LineNo = p.lineNo!.Value })
                .ToList();

            if (!pairs.Any())
            {
                TempData["error"] = "صيغة مفاتيح الأسطر غير صحيحة.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                // نجيب كل الأسطر المطلوبة مرة واحدة
                var srIds = pairs.Select(p => p.SRId).Distinct().ToList();
                var lineNos = pairs.Select(p => p.LineNo).Distinct().ToList();

                var lines = await _context.SalesReturnLines
                    .Include(l => l.SalesReturn)
                    .Where(l => srIds.Contains(l.SRId) && lineNos.Contains(l.LineNo))
                    .ToListAsync();

                int deleted = 0;
                int blocked = 0;

                // قائمة المرتجعات التى ستحتاج إعادة تجميع
                var affectedSrIds = new HashSet<int>();

                foreach (var line in lines)
                {
                    var status = line.SalesReturn?.Status ?? "";
                    if (string.Equals(status, "Draft", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(status, "غير مرحلة", StringComparison.OrdinalIgnoreCase))
                    {
                        _context.SalesReturnLines.Remove(line);
                        deleted++;
                        affectedSrIds.Add(line.SRId);
                    }
                    else
                    {
                        blocked++;
                    }
                }

                if (deleted > 0)
                {
                    await _context.SaveChangesAsync();

                    // إعادة تجميع إجماليات كل هيدر متأثر
                    foreach (var id in affectedSrIds)
                    {
                        await _docTotals.RecalcSalesReturnTotalsAsync(id);
                    }
                }

                if (deleted > 0 && blocked == 0)
                    TempData["ok"] = $"تم حذف {deleted} سطر/أسطر بنجاح.";
                else if (deleted > 0 && blocked > 0)
                    TempData["ok"] = $"تم حذف {deleted} سطر، وتم منع حذف {blocked} سطر لأن حالة المرتجع ليست غير مرحلة.";
                else if (blocked > 0)
                    TempData["error"] = "تم منع الحذف لأن جميع الأسطر مرتبطة بمرتجعات ليست غير مرحلة.";
                else
                    TempData["error"] = "لم يتم العثور على الأسطر المطلوبة.";

            }
            catch (Exception ex)
            {
                TempData["error"] = "تعذر حذف الأسطر: " + ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================
        // DELETE ALL: حذف جميع الأسطر (للمرتجعات غير مرحلة فقط)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _context.SalesReturnLines
                .Include(l => l.SalesReturn)
                .ToListAsync();

            if (all.Count == 0)
            {
                TempData["error"] = "لا توجد سطور مرتجع لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            var deletable = all
                .Where(l => string.Equals(l.SalesReturn?.Status ?? "",
                                          "Draft",
                                          StringComparison.OrdinalIgnoreCase)
                    || string.Equals(l.SalesReturn?.Status ?? "",
                                     "غير مرحلة",
                                     StringComparison.OrdinalIgnoreCase))
                .ToList();

            var blocked = all.Count - deletable.Count;

            if (deletable.Count == 0)
            {
                TempData["error"] = "كل السطور مرتبطة بمرتجعات ليست غير مرحلة، لا يمكن حذفها.";
                return RedirectToAction(nameof(Index));
            }

            var affectedSrIds = deletable
                .Select(l => l.SRId)
                .Distinct()
                .ToList();

            _context.SalesReturnLines.RemoveRange(deletable);
            await _context.SaveChangesAsync();

            // إعادة تجميع إجماليات كل هيدر متأثر
            foreach (var id in affectedSrIds)
            {
                await _docTotals.RecalcSalesReturnTotalsAsync(id);
            }

            if (blocked > 0)
                TempData["ok"] = $"تم حذف {deletable.Count} سطر/أسطر، وتم منع حذف {blocked} سطر لأن حالة المرتجع ليست غير مرحلة.";
            else
                TempData["ok"] = "تم حذف جميع سطور مرتجعات البيع (الخاصة بمرتجعات غير مرحلة).";

            return RedirectToAction(nameof(Index));
        }

        // =========================
        // EXPORT: تصدير نفس البيانات المعروضة (بعد الفلاتر)
        //  - format = "csv"  ⇒ ملف CSV
        //  - غير ذلك         ⇒ ملف Excel حقيقي (.xlsx)
        // =========================
        public async Task<IActionResult> Export(
            int? srId,
            string? search,
            string? searchBy = "all",
            string? searchMode = "contains",
            string? sort = "SRId",
            string? dir = "asc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_srid = null,
            string? filterCol_siid = null,
            string? filterCol_lineno = null,
            string? filterCol_prod = null,
            string? filterCol_qty = null,
            string? filterCol_priceretail = null,
            string? filterCol_unitprice = null,
            string? filterCol_net = null,
            string? filterCol_batch = null,
            string? filterCol_expiry = null,
            string? filterCol_status = null,
            string? filterCol_prodname = null,
            string? filterCol_customer = null,
            string? filterCol_createdby = null,
            string? filterCol_area = null,
            string? filterCol_sridExpr = null,
            string? filterCol_siidExpr = null,
            string? filterCol_linenoExpr = null,
            string? filterCol_prodExpr = null,
            string? filterCol_qtyExpr = null,
            string? filterCol_priceretailExpr = null,
            string? filterCol_unitpriceExpr = null,
            string? filterCol_netExpr = null,
            string format = "excel"
        )
        {
            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm != "starts" && sm != "ends") sm = "contains";
            var q = BuildQuery(
                srId,
                search, searchBy, sm,
                sort, dir,
                useDateRange, fromDate, toDate,
                fromCode, toCode);

            q = ApplyColumnFilters(
                q,
                filterCol_srid,
                filterCol_siid,
                filterCol_lineno,
                filterCol_prod,
                filterCol_qty,
                filterCol_priceretail,
                filterCol_unitprice,
                filterCol_net,
                filterCol_batch,
                filterCol_expiry,
                filterCol_status,
                filterCol_prodname,
                filterCol_customer,
                filterCol_createdby,
                filterCol_area,
                filterCol_sridExpr,
                filterCol_siidExpr,
                filterCol_linenoExpr,
                filterCol_prodExpr,
                filterCol_qtyExpr,
                filterCol_priceretailExpr,
                filterCol_unitpriceExpr,
                filterCol_netExpr);

            var data = await q
                .OrderBy(l => l.SRId)
                .ThenBy(l => l.LineNo)
                .ToListAsync();

            format = (format ?? "excel").ToLowerInvariant();

            if (format == "csv")
            {
                // ====== تصدير CSV بسيط ======
                var lines = new List<string>
                {
                    "رقم المرتجع,رقم فاتورة البيع,رقم السطر,كود الصنف,اسم الصنف,العميل,الكاتب,المنطقة,الكمية,سعر الجمهور,سعر بيع الوحدة,صافي السطر,التشغيلة,الصلاحية,حالة المرتجع"
                };

                foreach (var l in data)
                {
                    string expiry = l.Expiry.HasValue
                        ? l.Expiry.Value.ToString("yyyy-MM-dd")
                        : "";
                    string status = l.SalesReturn?.Status ?? "";
                    var pn = l.Product?.ProdName ?? "";
                    var cn = l.SalesReturn?.Customer?.CustomerName ?? "";
                    var cb = l.SalesReturn?.CreatedBy ?? "";
                    var an = l.SalesReturn?.Customer?.Area?.AreaName ?? "";

                    lines.Add(string.Join(",",
                        l.SRId,
                        l.SalesInvoiceId.HasValue ? l.SalesInvoiceId.Value.ToString() : "",
                        l.LineNo,
                        l.ProdId,
                        EscapeCsv(pn),
                        EscapeCsv(cn),
                        EscapeCsv(cb),
                        EscapeCsv(an),
                        l.Qty,
                        l.PriceRetail.ToString("0.00", CultureInfo.InvariantCulture),
                        l.UnitSalePrice.ToString("0.00", CultureInfo.InvariantCulture),
                        l.LineNetTotal.ToString("0.00", CultureInfo.InvariantCulture),
                        EscapeCsv(l.BatchNo),
                        EscapeCsv(expiry),
                        EscapeCsv(status)
                    ));
                }

                var bytesCsv = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(string.Join(Environment.NewLine, lines));
                var fileNameCsv = ExcelExportNaming.ArabicTimestampedFileName("أصناف مرتجع البيع", ".csv");

                return File(bytesCsv, "text/csv", fileNameCsv);
            }
            else
            {
                // ====== تصدير Excel حقيقي باستخدام ClosedXML ======
                using var workbook = new XLWorkbook();
                var ws = workbook.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("أصناف مرتجع البيع"));

                int row = 1;

                // عناوين الأعمدة (عربي)
                ws.Cell(row, 1).Value = "رقم المرتجع";
                ws.Cell(row, 2).Value = "رقم فاتورة البيع";
                ws.Cell(row, 3).Value = "رقم السطر";
                ws.Cell(row, 4).Value = "كود الصنف";
                ws.Cell(row, 5).Value = "اسم الصنف";
                ws.Cell(row, 6).Value = "العميل";
                ws.Cell(row, 7).Value = "الكاتب";
                ws.Cell(row, 8).Value = "المنطقة";
                ws.Cell(row, 9).Value = "الكمية";
                ws.Cell(row, 10).Value = "سعر الجمهور";
                ws.Cell(row, 11).Value = "سعر بيع الوحدة";
                ws.Cell(row, 12).Value = "صافي السطر";
                ws.Cell(row, 13).Value = "التشغيلة";
                ws.Cell(row, 14).Value = "الصلاحية";
                ws.Cell(row, 15).Value = "حالة المرتجع";

                ws.Range(row, 1, row, 15).Style.Font.Bold = true;

                foreach (var l in data)
                {
                    row++;

                    ws.Cell(row, 1).Value = l.SRId;
                    ws.Cell(row, 2).Value = l.SalesInvoiceId.HasValue ? l.SalesInvoiceId.Value.ToString() : "";
                    ws.Cell(row, 3).Value = l.LineNo;
                    ws.Cell(row, 4).Value = l.ProdId;
                    ws.Cell(row, 5).Value = l.Product?.ProdName ?? "";
                    ws.Cell(row, 6).Value = l.SalesReturn?.Customer?.CustomerName ?? "";
                    ws.Cell(row, 7).Value = l.SalesReturn?.CreatedBy ?? "";
                    ws.Cell(row, 8).Value = l.SalesReturn?.Customer?.Area?.AreaName ?? "";
                    ws.Cell(row, 9).Value = l.Qty;
                    ws.Cell(row, 10).Value = l.PriceRetail;
                    ws.Cell(row, 11).Value = l.UnitSalePrice;
                    ws.Cell(row, 12).Value = l.LineNetTotal;
                    ws.Cell(row, 13).Value = l.BatchNo ?? "";
                    ws.Cell(row, 14).Value = l.Expiry?.ToString("yyyy-MM-dd") ?? "";
                    ws.Cell(row, 15).Value = l.SalesReturn?.Status ?? "";
                }

                ws.Columns().AdjustToContents();

                using var stream = new System.IO.MemoryStream();
                workbook.SaveAs(stream);
                var bytesXlsx = stream.ToArray();

                var fileNameXlsx = ExcelExportNaming.ArabicTimestampedFileName("أصناف مرتجع البيع", ".xlsx");
                const string contentTypeXlsx =
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

                return File(bytesXlsx, contentTypeXlsx, fileNameXlsx);
            }
        }

        // دالة مساعدة للهروب داخل CSV (لو في فاصلة / علامات تنصيص)
        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }

        // =========================
        // GetColumnValues — قيم مميزة لكل عمود لنمط فلترة الأعمدة (Excel-like)
        // =========================
        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            if (string.IsNullOrWhiteSpace(column))
                return Json(Array.Empty<string>());

            column = column.ToLowerInvariant();
            search = (search ?? string.Empty).Trim();

            IQueryable<SalesReturnLine> q = _context.SalesReturnLines
                .AsNoTracking()
                .Include(l => l.Product)
                .Include(l => l.SalesReturn)
                    .ThenInclude(sr => sr!.Customer)
                        .ThenInclude(c => c!.Area);

            if (column == "srid")
            {
                var query = q.Select(l => l.SRId.ToString());
                if (!string.IsNullOrEmpty(search))
                    query = query.Where(v => v.Contains(search));
                var list = await query.Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }

            if (column == "siid")
            {
                var query = q.Where(l => l.SalesInvoiceId.HasValue)
                             .Select(l => l.SalesInvoiceId!.Value.ToString());
                if (!string.IsNullOrEmpty(search))
                    query = query.Where(v => v.Contains(search));
                var list = await query.Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }

            if (column == "lineno")
            {
                var query = q.Select(l => l.LineNo.ToString());
                if (!string.IsNullOrEmpty(search))
                    query = query.Where(v => v.Contains(search));
                var list = await query.Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }

            if (column == "prod")
            {
                var query = q.Select(l => l.ProdId.ToString());
                if (!string.IsNullOrEmpty(search))
                    query = query.Where(v => v.Contains(search));
                var list = await query.Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }

            if (column == "qty")
            {
                var query = q.Select(l => l.Qty.ToString());
                if (!string.IsNullOrEmpty(search))
                    query = query.Where(v => v.Contains(search));
                var list = await query.Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }

            if (column == "priceretail")
            {
                var raw = await q.Select(l => l.PriceRetail).Distinct().OrderBy(v => v).Take(500).ToListAsync();
                var list = raw.Select(v => v.ToString("0.00", CultureInfo.InvariantCulture)).ToList();
                if (!string.IsNullOrEmpty(search))
                    list = list.Where(v => v.Contains(search, StringComparison.OrdinalIgnoreCase)).Take(200).ToList();
                else
                    list = list.Take(200).ToList();
                return Json(list);
            }

            if (column == "unitprice")
            {
                var raw = await q.Select(l => l.UnitSalePrice).Distinct().OrderBy(v => v).Take(500).ToListAsync();
                var list = raw.Select(v => v.ToString("0.00", CultureInfo.InvariantCulture)).ToList();
                if (!string.IsNullOrEmpty(search))
                    list = list.Where(v => v.Contains(search, StringComparison.OrdinalIgnoreCase)).Take(200).ToList();
                else
                    list = list.Take(200).ToList();
                return Json(list);
            }

            if (column == "net")
            {
                var raw = await q.Select(l => l.LineNetTotal).Distinct().OrderBy(v => v).Take(500).ToListAsync();
                var list = raw.Select(v => v.ToString("0.00", CultureInfo.InvariantCulture)).ToList();
                if (!string.IsNullOrEmpty(search))
                    list = list.Where(v => v.Contains(search, StringComparison.OrdinalIgnoreCase)).Take(200).ToList();
                else
                    list = list.Take(200).ToList();
                return Json(list);
            }

            if (column == "batch")
            {
                var query = q.Select(l => l.BatchNo ?? "");
                if (!string.IsNullOrEmpty(search))
                    query = query.Where(v => v.Contains(search));
                var list = await query.Where(v => v != "").Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }

            if (column == "expiry")
            {
                var query = q.Select(l => l.Expiry.HasValue ? l.Expiry.Value.ToString("yyyy-MM-dd") : "");
                if (!string.IsNullOrEmpty(search))
                    query = query.Where(v => v.Contains(search));
                var list = await query.Where(v => v != "").Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }

            if (column == "status")
            {
                var query = q.Select(l => l.SalesReturn != null ? (l.SalesReturn.Status ?? "") : "");
                if (!string.IsNullOrEmpty(search))
                    query = query.Where(v => v.Contains(search));
                var list = await query.Where(v => v != "").Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }

            if (column == "prodname")
            {
                var query = q.Select(l => l.Product != null ? (l.Product.ProdName ?? "") : "");
                if (!string.IsNullOrEmpty(search))
                    query = query.Where(v => v.Contains(search));
                var list = await query.Where(v => v != "").Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }

            if (column == "customer")
            {
                var query = q.Select(l => l.SalesReturn != null && l.SalesReturn.Customer != null
                    ? (l.SalesReturn.Customer.CustomerName ?? "")
                    : "");
                if (!string.IsNullOrEmpty(search))
                    query = query.Where(v => v.Contains(search));
                var list = await query.Where(v => v != "").Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }

            if (column == "createdby")
            {
                var query = q.Select(l => l.SalesReturn != null ? (l.SalesReturn.CreatedBy ?? "") : "");
                if (!string.IsNullOrEmpty(search))
                    query = query.Where(v => v.Contains(search));
                var list = await query.Where(v => v != "").Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }

            if (column == "area")
            {
                var query = q.Select(l => l.SalesReturn != null && l.SalesReturn.Customer != null && l.SalesReturn.Customer.Area != null
                    ? (l.SalesReturn.Customer.Area.AreaName ?? "")
                    : "");
                if (!string.IsNullOrEmpty(search))
                    query = query.Where(v => v.Contains(search));
                var list = await query.Where(v => v != "").Distinct().OrderBy(v => v).Take(200).ToListAsync();
                return Json(list);
            }

            return Json(Array.Empty<string>());
        }
    }
}
