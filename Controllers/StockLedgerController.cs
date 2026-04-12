using System;                                       // متغيرات: DateTime
using System.Collections.Generic;                   // Dictionary
using System.IO;                                    // MemoryStream
using System.Linq;                                  // LINQ
using System.Linq.Expressions;                      // Expression<Func<>>
using System.Text;                                  // StringBuilder + Encoding (للـ CSV)
using System.Threading.Tasks;                       // Task/async
using ClosedXML.Excel;                              // مكتبة Excel
using ERP.Data;                                     // AppDbContext
using ERP.Filters;
using ERP.Infrastructure;                           // ApplySearchSort + PagedResult
using ERP.Models;                                   // StockLedger
using ERP.Security;
using Microsoft.AspNetCore.Mvc;                     // Controller
using Microsoft.AspNetCore.Mvc.Rendering;           // SelectListItem
using Microsoft.EntityFrameworkCore;                // AsNoTracking, ToListAsync

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر سجل الحركات:
    /// عرض + بحث + ترتيب + ترقيم + تصدير (Excel أو CSV) حسب اختيار المستخدم.
    /// </summary>
    [RequirePermission("StockLedger.Index")]
    public class StockLedgerController : Controller
    {
        private readonly AppDbContext context;   // متغير: سياق قاعدة البيانات

        public StockLedgerController(AppDbContext ctx) => context = ctx;

        // =========================
        // قواميس البحث/الترتيب المشتركة
        // =========================

        // حقول نصية للبحث (string)
        private static readonly Dictionary<string, Expression<Func<StockLedger, string?>>> StringFields
            = new(StringComparer.OrdinalIgnoreCase)
            {
                ["batch"] = x => x.BatchNo!,    // التشغيلة
                ["source"] = x => x.SourceType,  // نوع المصدر
            };

        // حقول رقمية للبحث (int)
        private static readonly Dictionary<string, Expression<Func<StockLedger, int>>> IntFields
            = new(StringComparer.OrdinalIgnoreCase)
            {
                ["sourceline"] = x => x.SourceLine,    // رقم سطر المصدر
                ["qtyin"] = x => x.QtyIn,         // كمية داخلة
                ["qtyout"] = x => x.QtyOut,        // كمية خارجة
                ["entry"] = x => x.EntryId,       // رقم القيد
                ["warehouse"] = x => x.WarehouseId,   // المخزن
                ["product"] = x => x.ProdId,        // الصنف
                ["sourceid"] = x => x.SourceId       // رقم المصدر
            };

        // حقول للترتيب (OrderBy)
        private static readonly Dictionary<string, Expression<Func<StockLedger, object>>> OrderFields
            = new(StringComparer.OrdinalIgnoreCase)
            {
                ["TranDate"] = x => x.TranDate,
                ["EntryId"] = x => x.EntryId,
                ["SourceId"] = x => x.SourceId,
                ["SourceLine"] = x => x.SourceLine,
                ["SourceType"] = x => x.SourceType ?? "",
                ["Warehouse"] = x => x.WarehouseId,
                ["WarehouseName"] = x => x.Warehouse != null ? x.Warehouse.WarehouseName : "",
                ["BranchName"] = x => x.Warehouse != null && x.Warehouse.Branch != null ? x.Warehouse.Branch.BranchName : "",
                ["CreatedBy"] = x => x.User != null ? x.User.DisplayName : "",
                ["Product"] = x => x.ProdId,
                ["Expiry"] = x => x.Expiry ?? DateTime.MaxValue,
                ["QtyIn"] = x => x.QtyIn,
                ["QtyOut"] = x => x.QtyOut,
                ["UnitCost"] = x => x.UnitCost,
                ["TotalCost"] = x => x.TotalCost
            };

        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        /// <summary>
        /// تطبيع الأرقام العربية ٠–٩ إلى لاتينية (مثل بحث الأصناف الموثّق ولوحة فلتر الأعمدة).
        /// </summary>
        private static string NormalizeArabicDigitsToLatin(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] >= '٠' && chars[i] <= '٩')
                    chars[i] = (char)('0' + (chars[i] - '٠'));
            }
            return new string(chars);
        }

        /// <summary>
        /// تطبيع وضع البحث (يبدأ / يحتوي / ينتهي) — آخر قيمة في الـ Query إن وُجدت (مثل pageSize).
        /// </summary>
        private string ResolveSearchMode(string? searchMode)
        {
            var smQuery = Request.Query["searchMode"].LastOrDefault();
            if (!string.IsNullOrEmpty(smQuery))
                searchMode = smQuery;
            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm == "startswith") sm = "starts";
            if (sm == "eq" || sm == "equals") sm = "contains";
            if (sm != "contains" && sm != "starts" && sm != "ends")
                sm = "contains";
            return sm;
        }

        private static string StockLedgerTextLikePattern(string w, string sm) =>
            sm == "starts" ? $"{w}%" : sm == "ends" ? $"%{w}" : $"%{w}%";

        /// <summary>
        /// بحث دفتر الحركة — حقول نصية بـ LIKE حسب الوضع (يحتوي / يبدأ / ينتهي)، كلمات متعددة AND،
        /// وأرقام كـ تطابق تام عند التحليل أو StartsWith/Contains/EndsWith على النص.
        /// </summary>
        private static IQueryable<StockLedger> ApplyStockLedgerSearchFilter(
            IQueryable<StockLedger> q,
            string? search,
            string? searchBy,
            string searchMode)
        {
            if (string.IsNullOrWhiteSpace(search))
                return q;

            var normalized = NormalizeArabicDigitsToLatin(search.Trim());
            var sb = (searchBy ?? "all").Trim().ToLowerInvariant();
            var sm = searchMode.Trim().ToLowerInvariant();
            if (sm != "starts" && sm != "ends") sm = "contains";

            var words = normalized.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
                return q;

            if (sb == "all")
            {
                foreach (var w in words)
                {
                    var like = StockLedgerTextLikePattern(w, sm);
                    q = q.Where(x =>
                        (x.BatchNo != null && EF.Functions.Like(x.BatchNo, like)) ||
                        (x.SourceType != null && EF.Functions.Like(x.SourceType, like)) ||
                        (x.Product != null && x.Product.ProdName != null && EF.Functions.Like(x.Product.ProdName, like)) ||
                        (x.Warehouse != null && x.Warehouse.WarehouseName != null && EF.Functions.Like(x.Warehouse.WarehouseName, like)) ||
                        (x.Warehouse != null && x.Warehouse.Branch != null && x.Warehouse.Branch.BranchName != null &&
                         EF.Functions.Like(x.Warehouse.Branch.BranchName, like)) ||
                        (x.User != null && x.User.DisplayName != null && EF.Functions.Like(x.User.DisplayName, like)) ||
                        (sm == "contains" && (x.ProdId.ToString().Contains(w) || x.SourceId.ToString().Contains(w) || x.EntryId.ToString().Contains(w) ||
                            x.WarehouseId.ToString().Contains(w) || x.SourceLine.ToString().Contains(w) || x.QtyIn.ToString().Contains(w) || x.QtyOut.ToString().Contains(w))) ||
                        (sm == "starts" && (x.ProdId.ToString().StartsWith(w) || x.SourceId.ToString().StartsWith(w) || x.EntryId.ToString().StartsWith(w) ||
                            x.WarehouseId.ToString().StartsWith(w) || x.SourceLine.ToString().StartsWith(w) || x.QtyIn.ToString().StartsWith(w) || x.QtyOut.ToString().StartsWith(w))) ||
                        (sm == "ends" && (x.ProdId.ToString().EndsWith(w) || x.SourceId.ToString().EndsWith(w) || x.EntryId.ToString().EndsWith(w) ||
                            x.WarehouseId.ToString().EndsWith(w) || x.SourceLine.ToString().EndsWith(w) || x.QtyIn.ToString().EndsWith(w) || x.QtyOut.ToString().EndsWith(w)))
                    );
                }

                return q;
            }

            if (sb == "productname")
            {
                foreach (var w in words)
                {
                    var like = StockLedgerTextLikePattern(w, sm);
                    q = q.Where(x => x.Product != null && x.Product.ProdName != null && EF.Functions.Like(x.Product.ProdName, like));
                }

                return q;
            }

            if (sb == "entry")
            {
                if (int.TryParse(normalized, out var ne))
                    return q.Where(x => x.EntryId == ne);
                var like = StockLedgerTextLikePattern(normalized, sm);
                return q.Where(x => EF.Functions.Like(x.EntryId.ToString(), like));
            }

            if (sb == "warehouse")
            {
                if (int.TryParse(normalized, out var nw))
                    return q.Where(x => x.WarehouseId == nw);
                var like = StockLedgerTextLikePattern(normalized, sm);
                return q.Where(x => EF.Functions.Like(x.WarehouseId.ToString(), like));
            }

            if (sb == "product")
            {
                if (int.TryParse(normalized, out var np))
                    return q.Where(x => x.ProdId == np);
                var like = StockLedgerTextLikePattern(normalized, sm);
                return q.Where(x => EF.Functions.Like(x.ProdId.ToString(), like));
            }

            if (sb == "sourceid")
            {
                if (int.TryParse(normalized, out var ns))
                    return q.Where(x => x.SourceId == ns);
                var like = StockLedgerTextLikePattern(normalized, sm);
                return q.Where(x => EF.Functions.Like(x.SourceId.ToString(), like));
            }

            if (sb == "sourceline")
            {
                if (int.TryParse(normalized, out var nl))
                    return q.Where(x => x.SourceLine == nl);
                var like = StockLedgerTextLikePattern(normalized, sm);
                return q.Where(x => EF.Functions.Like(x.SourceLine.ToString(), like));
            }

            if (sb == "batch")
            {
                foreach (var w in words)
                {
                    var like = StockLedgerTextLikePattern(w, sm);
                    q = q.Where(x => x.BatchNo != null && EF.Functions.Like(x.BatchNo, like));
                }

                return q;
            }

            if (sb == "source")
            {
                foreach (var w in words)
                {
                    var like = StockLedgerTextLikePattern(w, sm);
                    q = q.Where(x => x.SourceType != null && EF.Functions.Like(x.SourceType, like));
                }

                return q;
            }

            if (sb == "qtyin" && int.TryParse(normalized, out var qin))
                return q.Where(x => x.QtyIn == qin);
            if (sb == "qtyout" && int.TryParse(normalized, out var qout))
                return q.Where(x => x.QtyOut == qout);

            return q;
        }

        // =========================
        // Index — تقرير دفتر الحركة
        // =========================
        [HttpGet]
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",
            string? searchMode = null,
            string? sort = "TranDate",
            string? dir = "desc",
            int page = 1,
            int pageSize = 10,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromEntry = null,
            int? toEntry = null,
            string? filterCol_entry = null,
            string? filterCol_warehouse = null,
            string? filterCol_product = null,
            string? filterCol_source = null,
            string? filterCol_tran = null,
            string? filterCol_productname = null,
            string? filterCol_batch = null,
            string? filterCol_expiry = null,
            string? filterCol_sourceid = null,
            string? filterCol_sourceline = null,
            string? filterCol_qtyin = null,
            string? filterCol_qtyout = null,
            string? filterCol_unitcost = null,
            string? filterCol_rem = null,
            string? filterCol_priceretail = null,
            string? filterCol_PurchaseDiscount = null,
            string? filterCol_warehouse_name = null,
            string? filterCol_branch = null,
            string? filterCol_createdby = null,
            string? filterCol_entryExpr = null,
            string? filterCol_warehouseExpr = null,
            string? filterCol_productExpr = null,
            string? filterCol_sourceidExpr = null,
            string? filterCol_sourcelineExpr = null,
            string? filterCol_qtyinExpr = null,
            string? filterCol_qtyoutExpr = null,
            string? filterCol_unitcostExpr = null,
            string? filterCol_remExpr = null,
            string? filterCol_priceretailExpr = null,
            string? filterCol_purchasediscountExpr = null,
            string? filterCol_totalcostExpr = null)
        {
            // حجم الصفحة: آخر قيمة في الـ Query + القيم المسموحة (10،25،50،100،200،0=الكل)
            var pageSizeQuery = Request.Query["pageSize"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageSizeQuery) && int.TryParse(pageSizeQuery, out var psVal))
                pageSize = psVal;
            if (pageSize < 0) pageSize = 10;
            if (pageSize > 0 && pageSize != 10 && pageSize != 25 && pageSize != 50 && pageSize != 100 && pageSize != 200)
                pageSize = 10;

            var sm = ResolveSearchMode(searchMode);

            // الاستعلام الأساسي
            IQueryable<StockLedger> q = context.StockLedger
                .AsNoTracking()
                .Include(x => x.Product)
                .Include(x => x.Batch)
                .Include(x => x.Warehouse).ThenInclude(w => w.Branch)
                .Include(x => x.User);

            // بحث نصي/متعدد الكلمات (يشمل اسم الصنف) — ثم ترتيب فقط عبر ApplySearchSort بدون تكرار فلترة قديمة
            q = ApplyStockLedgerSearchFilter(q, search, searchBy, sm);
            q = q.ApplySearchSort(
                null, "all",
                sort, dir,
                StringFields, IntFields, OrderFields,
                defaultSearchBy: "all",
                defaultSortBy: "TranDate"
            );

            // فلترة بالتاريخ (اختيارية)
            if (useDateRange)
            {
                if (fromDate.HasValue)
                    q = q.Where(x => x.TranDate >= fromDate.Value);
                if (toDate.HasValue)
                    q = q.Where(x => x.TranDate <= toDate.Value);
            }

            // فلتر رقم القيد من/إلى
            if (fromEntry.HasValue)
                q = q.Where(x => x.EntryId >= fromEntry.Value);
            if (toEntry.HasValue)
                q = q.Where(x => x.EntryId <= toEntry.Value);

            q = ApplyStockLedgerColumnFilters(q,
                filterCol_entryExpr, filterCol_warehouseExpr, filterCol_productExpr,
                filterCol_sourceidExpr, filterCol_sourcelineExpr, filterCol_qtyinExpr, filterCol_qtyoutExpr,
                filterCol_unitcostExpr, filterCol_remExpr, filterCol_priceretailExpr, filterCol_purchasediscountExpr, filterCol_totalcostExpr,
                filterCol_entry, filterCol_warehouse, filterCol_product, filterCol_source, filterCol_tran,
                filterCol_productname, filterCol_batch, filterCol_expiry, filterCol_sourceid, filterCol_sourceline,
                filterCol_qtyin, filterCol_qtyout, filterCol_unitcost, filterCol_rem, filterCol_priceretail, filterCol_PurchaseDiscount,
                filterCol_warehouse_name, filterCol_branch, filterCol_createdby);

            var s = (search ?? "").Trim();
            var sb = (searchBy ?? "all").Trim();
            var so = (sort ?? "TranDate").Trim();
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            var totalFiltered = await q.CountAsync();
            var sumQtyIn = await q.SumAsync(x => (decimal)x.QtyIn);
            var sumQtyOut = await q.SumAsync(x => (decimal)x.QtyOut);
            var sumLineCost = await q.SumAsync(x => (x.TotalCost ?? 0) > 0 ? x.TotalCost!.Value : (x.QtyIn * x.UnitCost));

            ViewBag.FilteredQtyInSum = sumQtyIn;
            ViewBag.FilteredQtyOutSum = sumQtyOut;
            ViewBag.FilteredLineCostSum = sumLineCost;

            // نفس منطق التقارير (أرصدة الأصناف): صافي الكمية من دفتر الحركة + خصم مرجّح على المتبقي لدخول/فتح/تحويل/مزامنة
            var reportNetQty = await q.SumAsync(x => (decimal)(x.QtyIn - x.QtyOut));
            var qWeighted = q.Where(x =>
                (x.SourceType == "Purchase" || x.SourceType == "Opening" || x.SourceType == "TransferIn" || x.SourceType == "SyncToProductWarehouse") &&
                (x.RemainingQty ?? 0) > 0);
            var sumRem = await qWeighted.SumAsync(x => (decimal)(x.RemainingQty ?? 0));
            var sumRemDisc = await qWeighted.SumAsync(x => (decimal)(x.RemainingQty ?? 0) * ((decimal?)(x.PurchaseDiscount) ?? 0m));
            ViewBag.ReportNetQty = reportNetQty;
            ViewBag.ReportWeightedDiscountPct = sumRem > 0 ? sumRemDisc / sumRem : (decimal?)null;
            ViewBag.ReportWeightedRemTotal = sumRem;

            if (page < 1) page = 1;
            var effectivePageSize = pageSize;
            if (pageSize == 0)
            {
                effectivePageSize = totalFiltered == 0 ? 10 : Math.Min(totalFiltered, 100_000);
                page = 1;
            }

            var skip = (page - 1) * effectivePageSize;
            if (totalFiltered > 0 && skip >= totalFiltered)
            {
                page = Math.Max(1, (int)Math.Ceiling((double)totalFiltered / effectivePageSize));
                skip = (page - 1) * effectivePageSize;
            }

            var pageItems = await q.Skip(skip).Take(effectivePageSize).ToListAsync();

            var model = new PagedResult<StockLedger>(pageItems, page, pageSize, totalFiltered)
            {
                Search = s,
                SearchBy = sb,
                SortColumn = so,
                SortDescending = desc,
                UseDateRange = useDateRange,
                FromDate = fromDate,
                ToDate = toDate
            };

            // =========================================================
            // ✅ (جديد) تعبئة خصم الشراء من "سطور الشراء" PILines
            // الفكرة:
            // - StockLedger فيه: SourceType + SourceId + SourceLine
            // - الخصم موجود في جدول PILines داخل PurchaseDiscountPct
            // - نملأ PurchaseDiscount في Rows الخاصة بالشراء فقط (لصفحة النتائج الحالية)
            // =========================================================
            var pageRows = model.Items; // متغير: صفوف الصفحة الحالية (اسمها Items عندك)

            // متغير: صفوف الشراء الظاهرة في الصفحة (اللي ينفع نجيب خصمها)
            var purchaseRows = pageRows
                .Where(x =>
                    x.SourceType == "Purchase" &&   // تعليق: حركة شراء
                    x.SourceId > 0 &&               // تعليق: رقم فاتورة الشراء
                    x.SourceLine > 0)               // ✅ مهم: اسم الحقل عندك SourceLine وليس SourceLineNo
                .Select(x => new { x.EntryId, PIId = x.SourceId, LineNo = x.SourceLine })
                .ToList();

            if (purchaseRows.Any())
            {
                // متغير: كل فواتير الشراء الموجودة في الصفحة
                var piIds = purchaseRows.Select(x => x.PIId).Distinct().ToList();

                // متغير: نجلب خصومات سطور الشراء لهذه الفواتير فقط (أسرع)
                // ⚠️ أسماء الأعمدة من ملف PILine.cs عندك: PIId, LineNo, PurchaseDiscountPct
                var piLines = await context.PILines
                    .AsNoTracking()
                    .Where(l => piIds.Contains(l.PIId))
                    .Select(l => new
                    {
                        l.PIId,
                        l.LineNo,
                        Disc = (decimal?)l.PurchaseDiscountPct ?? 0m   // متغير: خصم الشراء %
                    })
                    .ToListAsync();

                // متغير: Lookup سريع (PIId + LineNo) => Disc
                var discLookup = piLines.ToDictionary(
                    x => (x.PIId, x.LineNo),
                    x => x.Disc
                );

                // تعليق: نملأ PurchaseDiscount داخل صفوف StockLedger في الذاكرة
                // بدون أي Update للـ DB (عرض فقط)
                foreach (var row in pageRows)
                {
                    if (row.SourceType == "Purchase" && row.SourceId > 0 && row.SourceLine > 0)
                    {
                        if (discLookup.TryGetValue((row.SourceId, row.SourceLine), out var disc))
                        {
                            // ✅ اكتبها في العمود الموجود بالفعل في StockLedger
                            // (حتى لو كانت Null هتظهر الآن في الجدول)
                            row.PurchaseDiscount = disc;
                        }
                        else
                        {
                            // لو السطر مش موجود لأي سبب
                            row.PurchaseDiscount = 0m;
                        }
                    }
                }
            }

            // خيارات البحث
            ViewBag.SearchOptions = new List<SelectListItem>
    {
        new("الكل",              "all")       { Selected = sb.Equals("all",        StringComparison.OrdinalIgnoreCase) },
        new("رقم القيد",         "entry")     { Selected = sb.Equals("entry",      StringComparison.OrdinalIgnoreCase) },
        new("المخزن",           "warehouse") { Selected = sb.Equals("warehouse",   StringComparison.OrdinalIgnoreCase) },
        new("الصنف",            "product")   { Selected = sb.Equals("product",     StringComparison.OrdinalIgnoreCase) },
        new("التشغيلة",         "batch")     { Selected = sb.Equals("batch",       StringComparison.OrdinalIgnoreCase) },
        new("نوع المصدر",       "source")    { Selected = sb.Equals("source",      StringComparison.OrdinalIgnoreCase) },
        new("رقم المصدر",       "sourceid")  { Selected = sb.Equals("sourceid",    StringComparison.OrdinalIgnoreCase) },
        new("سطر المصدر (رقم)", "sourceline"){ Selected = sb.Equals("sourceline",  StringComparison.OrdinalIgnoreCase) }
    };

            // خيارات الترتيب
            ViewBag.SortOptions = new List<SelectListItem>
    {
        new("التاريخ",        "TranDate")  { Selected = so.Equals("TranDate",  StringComparison.OrdinalIgnoreCase) },
        new("رقم القيد",      "EntryId")   { Selected = so.Equals("EntryId",   StringComparison.OrdinalIgnoreCase) },
        new("رقم الفاتورة",   "SourceId")  { Selected = so.Equals("SourceId",  StringComparison.OrdinalIgnoreCase) },
        new("سطر المصدر",     "SourceLine"){ Selected = so.Equals("SourceLine",StringComparison.OrdinalIgnoreCase) },
        new("اسم المخزن",    "WarehouseName") { Selected = so.Equals("WarehouseName", StringComparison.OrdinalIgnoreCase) },
        new("المنطقة (الفرع)", "BranchName") { Selected = so.Equals("BranchName", StringComparison.OrdinalIgnoreCase) },
        new("الكاتب",         "CreatedBy") { Selected = so.Equals("CreatedBy", StringComparison.OrdinalIgnoreCase) },
        new("المخزن",        "Warehouse") { Selected = so.Equals("Warehouse", StringComparison.OrdinalIgnoreCase) },
        new("الصنف",         "Product")   { Selected = so.Equals("Product",   StringComparison.OrdinalIgnoreCase) },
        new("الصلاحية",      "Expiry")    { Selected = so.Equals("Expiry",    StringComparison.OrdinalIgnoreCase) },
        new("كمية داخلة",    "QtyIn")     { Selected = so.Equals("QtyIn",     StringComparison.OrdinalIgnoreCase) },
        new("كمية خارجة",    "QtyOut")    { Selected = so.Equals("QtyOut",    StringComparison.OrdinalIgnoreCase) },
        new("تكلفة الوحدة",  "UnitCost")  { Selected = so.Equals("UnitCost",  StringComparison.OrdinalIgnoreCase) },
        new("إجمالي التكلفة", "TotalCost") { Selected = so.Equals("TotalCost", StringComparison.OrdinalIgnoreCase) }
    };

            ViewBag.Search = s;
            ViewBag.SearchBy = sb;
            ViewBag.SearchMode = sm;
            ViewBag.Sort = so;
            ViewBag.Dir = desc ? "desc" : "asc";
            ViewBag.PageSize = model.PageSize;
            ViewBag.UseDateRange = useDateRange;
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;
            ViewBag.FromEntry = fromEntry;
            ViewBag.ToEntry = toEntry;
            ViewBag.FilterCol_Entry = filterCol_entry;
            ViewBag.FilterCol_Warehouse = filterCol_warehouse;
            ViewBag.FilterCol_Product = filterCol_product;
            ViewBag.FilterCol_Source = filterCol_source;
            ViewBag.FilterCol_Tran = filterCol_tran;
            ViewBag.FilterCol_Productname = filterCol_productname;
            ViewBag.FilterCol_Batch = filterCol_batch;
            ViewBag.FilterCol_Expiry = filterCol_expiry;
            ViewBag.FilterCol_Sourceid = filterCol_sourceid;
            ViewBag.FilterCol_Sourceline = filterCol_sourceline;
            ViewBag.FilterCol_Qtyin = filterCol_qtyin;
            ViewBag.FilterCol_Qtyout = filterCol_qtyout;
            ViewBag.FilterCol_Unitcost = filterCol_unitcost;
            ViewBag.FilterCol_Rem = filterCol_rem;
            ViewBag.FilterCol_Priceretail = filterCol_priceretail;
            ViewBag.FilterCol_PurchaseDiscount = filterCol_PurchaseDiscount;
            ViewBag.FilterCol_EntryExpr = filterCol_entryExpr;
            ViewBag.FilterCol_WarehouseExpr = filterCol_warehouseExpr;
            ViewBag.FilterCol_ProductExpr = filterCol_productExpr;
            ViewBag.FilterCol_SourceidExpr = filterCol_sourceidExpr;
            ViewBag.FilterCol_SourcelineExpr = filterCol_sourcelineExpr;
            ViewBag.FilterCol_QtyinExpr = filterCol_qtyinExpr;
            ViewBag.FilterCol_QtyoutExpr = filterCol_qtyoutExpr;
            ViewBag.FilterCol_UnitcostExpr = filterCol_unitcostExpr;
            ViewBag.FilterCol_RemExpr = filterCol_remExpr;
            ViewBag.FilterCol_PriceretailExpr = filterCol_priceretailExpr;
            ViewBag.FilterCol_PurchasediscountExpr = filterCol_purchasediscountExpr;
            ViewBag.FilterCol_TotalcostExpr = filterCol_totalcostExpr;
            ViewBag.FilterCol_WarehouseName = filterCol_warehouse_name;
            ViewBag.FilterCol_Branch = filterCol_branch;
            ViewBag.FilterCol_Createdby = filterCol_createdby;

            return View(model);
        }

        /// <summary>
        /// فلاتر أعمدة دفتر الحركة (Expr + تشيك بوكس) — مشتركة بين العرض والتصدير.
        /// </summary>
        private IQueryable<StockLedger> ApplyStockLedgerColumnFilters(
            IQueryable<StockLedger> q,
            string? filterCol_entryExpr,
            string? filterCol_warehouseExpr,
            string? filterCol_productExpr,
            string? filterCol_sourceidExpr,
            string? filterCol_sourcelineExpr,
            string? filterCol_qtyinExpr,
            string? filterCol_qtyoutExpr,
            string? filterCol_unitcostExpr,
            string? filterCol_remExpr,
            string? filterCol_priceretailExpr,
            string? filterCol_purchasediscountExpr,
            string? filterCol_totalcostExpr,
            string? filterCol_entry,
            string? filterCol_warehouse,
            string? filterCol_product,
            string? filterCol_source,
            string? filterCol_tran,
            string? filterCol_productname,
            string? filterCol_batch,
            string? filterCol_expiry,
            string? filterCol_sourceid,
            string? filterCol_sourceline,
            string? filterCol_qtyin,
            string? filterCol_qtyout,
            string? filterCol_unitcost,
            string? filterCol_rem,
            string? filterCol_priceretail,
            string? filterCol_PurchaseDiscount,
            string? filterCol_warehouse_name,
            string? filterCol_branch,
            string? filterCol_createdby)
        {
            q = StockLedgerNumericExpr.ApplyEntryIdExpr(q, filterCol_entryExpr);
            q = StockLedgerNumericExpr.ApplyWarehouseIdExpr(q, filterCol_warehouseExpr);
            q = StockLedgerNumericExpr.ApplyProdIdExpr(q, filterCol_productExpr);
            q = StockLedgerNumericExpr.ApplySourceIdExpr(q, filterCol_sourceidExpr);
            q = StockLedgerNumericExpr.ApplySourceLineExpr(q, filterCol_sourcelineExpr);
            q = StockLedgerNumericExpr.ApplyQtyInExpr(q, filterCol_qtyinExpr);
            q = StockLedgerNumericExpr.ApplyQtyOutExpr(q, filterCol_qtyoutExpr);
            q = StockLedgerNumericExpr.ApplyUnitCostExpr(q, filterCol_unitcostExpr);
            q = StockLedgerNumericExpr.ApplyRemainingQtyExpr(q, filterCol_remExpr);
            q = StockLedgerNumericExpr.ApplyPriceRetailExpr(q, filterCol_priceretailExpr);
            q = StockLedgerNumericExpr.ApplyPurchaseDiscountExpr(q, filterCol_purchasediscountExpr);
            q = StockLedgerNumericExpr.ApplyLineTotalCostExpr(q, filterCol_totalcostExpr);

            if (string.IsNullOrWhiteSpace(filterCol_entryExpr) && !string.IsNullOrWhiteSpace(filterCol_entry))
            {
                var ids = filterCol_entry.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    q = q.Where(x => ids.Contains(x.EntryId));
            }
            if (string.IsNullOrWhiteSpace(filterCol_warehouseExpr) && !string.IsNullOrWhiteSpace(filterCol_warehouse))
            {
                var ids = filterCol_warehouse.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    q = q.Where(x => ids.Contains(x.WarehouseId));
            }
            if (string.IsNullOrWhiteSpace(filterCol_productExpr) && !string.IsNullOrWhiteSpace(filterCol_product))
            {
                var ids = filterCol_product.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    q = q.Where(x => ids.Contains(x.ProdId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_source))
            {
                var vals = filterCol_source.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    q = q.Where(x => vals.Contains(x.SourceType));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_tran))
            {
                var parts = filterCol_tran.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length >= 7).ToList();
                var dateFilters = new List<(int Year, int Month)>();
                foreach (var p in parts)
                {
                    if (p.Length == 7 && p[4] == '-' && int.TryParse(p.Substring(0, 4), out var y) && int.TryParse(p.Substring(5, 2), out var m) && m >= 1 && m <= 12)
                        dateFilters.Add((y, m));
                }
                if (dateFilters.Count > 0)
                    q = q.Where(x => dateFilters.Any(df => x.TranDate.Year == df.Year && x.TranDate.Month == df.Month));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_productname))
            {
                var vals = filterCol_productname.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    q = q.Where(x => x.Product != null && x.Product.ProdName != null && vals.Contains(x.Product.ProdName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_batch))
            {
                var vals = filterCol_batch.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    q = q.Where(x => vals.Contains(x.BatchNo ?? ""));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_expiry))
            {
                var parts = filterCol_expiry.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length >= 7).ToList();
                var dateFilters = new List<(int Year, int Month)>();
                foreach (var p in parts)
                {
                    if (p.Length == 7 && p[4] == '-' && int.TryParse(p.Substring(0, 4), out var y) && int.TryParse(p.Substring(5, 2), out var m) && m >= 1 && m <= 12)
                        dateFilters.Add((y, m));
                }
                if (dateFilters.Count > 0)
                    q = q.Where(x => x.Expiry.HasValue && dateFilters.Any(df => x.Expiry.Value.Year == df.Year && x.Expiry.Value.Month == df.Month));
            }
            if (string.IsNullOrWhiteSpace(filterCol_sourceidExpr) && !string.IsNullOrWhiteSpace(filterCol_sourceid))
            {
                var ids = filterCol_sourceid.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    q = q.Where(x => ids.Contains(x.SourceId));
            }
            if (string.IsNullOrWhiteSpace(filterCol_sourcelineExpr) && !string.IsNullOrWhiteSpace(filterCol_sourceline))
            {
                var ids = filterCol_sourceline.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    q = q.Where(x => ids.Contains(x.SourceLine));
            }
            if (string.IsNullOrWhiteSpace(filterCol_qtyinExpr) && !string.IsNullOrWhiteSpace(filterCol_qtyin))
            {
                var ids = filterCol_qtyin.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    q = q.Where(x => ids.Contains(x.QtyIn));
            }
            if (string.IsNullOrWhiteSpace(filterCol_qtyoutExpr) && !string.IsNullOrWhiteSpace(filterCol_qtyout))
            {
                var ids = filterCol_qtyout.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    q = q.Where(x => ids.Contains(x.QtyOut));
            }
            if (string.IsNullOrWhiteSpace(filterCol_unitcostExpr) && !string.IsNullOrWhiteSpace(filterCol_unitcost))
            {
                var vals = filterCol_unitcost.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0)
                    q = q.Where(x => vals.Contains(x.UnitCost));
            }
            if (string.IsNullOrWhiteSpace(filterCol_remExpr) && !string.IsNullOrWhiteSpace(filterCol_rem))
            {
                var ids = filterCol_rem.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    q = q.Where(x => x.RemainingQty.HasValue && ids.Contains(x.RemainingQty.Value));
            }
            if (string.IsNullOrWhiteSpace(filterCol_priceretailExpr) && !string.IsNullOrWhiteSpace(filterCol_priceretail))
            {
                var vals = filterCol_priceretail.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0)
                    q = q.Where(x => x.PriceRetailBatch.HasValue && vals.Contains(x.PriceRetailBatch.Value));
            }
            if (string.IsNullOrWhiteSpace(filterCol_purchasediscountExpr) && !string.IsNullOrWhiteSpace(filterCol_PurchaseDiscount))
            {
                var vals = filterCol_PurchaseDiscount.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count > 0)
                    q = q.Where(x => x.PurchaseDiscount.HasValue && vals.Contains(x.PurchaseDiscount.Value));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_warehouse_name))
            {
                var vals = filterCol_warehouse_name.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    q = q.Where(x => x.Warehouse != null && vals.Contains(x.Warehouse.WarehouseName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_branch))
            {
                var vals = filterCol_branch.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    q = q.Where(x => x.Warehouse != null && x.Warehouse.Branch != null && vals.Contains(x.Warehouse.Branch.BranchName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_createdby))
            {
                var vals = filterCol_createdby.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    q = q.Where(x => x.User != null && vals.Contains(x.User.DisplayName));
            }

            return q;
        }

        /// <summary>
        /// API: جلب القيم المميزة لعمود (للفلترة بنمط Excel).
        /// عند وجود نص بحث: نفلتر في قاعدة البيانات أولاً (مثل قائمة الأصناف/العملاء) ثم Take، ولا نعتمد على أول N قيمة ثم البحث.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var col = column?.Trim().ToLowerInvariant() ?? "";
            IQueryable<StockLedger> q = context.StockLedger.AsNoTracking();
            if (col == "productname")
                q = q.Include(x => x.Product);
            else if (col is "warehouse_name" or "branch" or "createdby")
                q = q.Include(x => x.Warehouse).ThenInclude(w => w.Branch).Include(x => x.User);

            List<(string Value, string Display)> items = col switch
            {
                "entry" => (await q.Select(x => x.EntryId).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "tran" => (await q.Select(x => new { x.TranDate.Year, x.TranDate.Month }).Distinct()
                    .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).Take(100).ToListAsync())
                    .Select(x => ($"{x.Year}-{x.Month:D2}", $"{x.Year}/{x.Month:D2}")).ToList(),
                "warehouse" => (await q.Select(x => x.WarehouseId).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "product" => (await q.Select(x => x.ProdId).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "source" => await GetStockLedgerSourceTypeColumnValuesAsync(q, searchTerm),
                "productname" => await GetStockLedgerProductNameColumnValuesAsync(q, searchTerm),
                "batch" => await GetStockLedgerBatchColumnValuesAsync(q, searchTerm),
                "expiry" => (await q.Where(x => x.Expiry.HasValue).Select(x => new { x.Expiry!.Value.Year, x.Expiry.Value.Month }).Distinct()
                    .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).Take(100).ToListAsync())
                    .Select(x => ($"{x.Year}-{x.Month:D2}", $"{x.Year}/{x.Month:D2}")).ToList(),
                "sourceid" => (await q.Select(x => x.SourceId).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "sourceline" => (await q.Select(x => x.SourceLine).Distinct().OrderBy(v => v).Take(500).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "qtyin" => (await q.Select(x => x.QtyIn).Distinct().OrderBy(v => v).Take(200).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "qtyout" => (await q.Select(x => x.QtyOut).Distinct().OrderBy(v => v).Take(200).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "unitcost" => (await q.Select(x => x.UnitCost).Distinct().OrderBy(v => v).Take(200).ToListAsync())
                    .Select(v => (v.ToString(System.Globalization.CultureInfo.InvariantCulture), v.ToString("0.00"))).ToList(),
                "rem" => (await q.Where(x => x.RemainingQty.HasValue).Select(x => x.RemainingQty!.Value).Distinct().OrderBy(v => v).Take(200).ToListAsync())
                    .Select(v => (v.ToString(), v.ToString())).ToList(),
                "priceretail" => (await q.Where(x => x.PriceRetailBatch.HasValue).Select(x => x.PriceRetailBatch!.Value).Distinct().OrderBy(v => v).Take(200).ToListAsync())
                    .Select(v => (v.ToString(System.Globalization.CultureInfo.InvariantCulture), v.ToString("0.00"))).ToList(),
                "purchasediscount" => (await q.Where(x => x.PurchaseDiscount.HasValue).Select(x => x.PurchaseDiscount!.Value).Distinct().OrderBy(v => v).Take(200).ToListAsync())
                    .Select(v => (v.ToString(System.Globalization.CultureInfo.InvariantCulture), v.ToString("0.00"))).ToList(),
                "totalcost" => (await q.Where(x => x.TotalCost.HasValue).Select(x => x.TotalCost!.Value).Distinct().OrderBy(v => v).Take(200).ToListAsync())
                    .Select(v => (v.ToString(System.Globalization.CultureInfo.InvariantCulture), v.ToString("0.00"))).ToList(),
                "warehouse_name" => await GetStockLedgerWarehouseNameColumnValuesAsync(q, searchTerm),
                "branch" => await GetStockLedgerBranchNameColumnValuesAsync(q, searchTerm),
                "createdby" => await GetStockLedgerCreatedByColumnValuesAsync(q, searchTerm),
                _ => new List<(string Value, string Display)>()
            };

            // تضييق إضافي: كل كلمات الاستعلام يجب أن تظهر في النص (AND) — يتماشى مع واجهة الفلتر
            if (!string.IsNullOrEmpty(searchTerm) && items.Count > 0)
            {
                var words = searchTerm.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length > 0)
                    items = items.Where(x =>
                    {
                        var text = (x.Display ?? x.Value ?? "").ToLowerInvariant();
                        return words.All(w => text.Contains(w));
                    }).ToList();
            }

            return Json(items.Select(x => new { value = x.Value, display = x.Display }));
        }

        private static string[] SplitSearchWords(string searchTerm) =>
            searchTerm.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        private static async Task<List<(string Value, string Display)>> GetStockLedgerProductNameColumnValuesAsync(IQueryable<StockLedger> q, string searchTerm)
        {
            var qn = q.Where(x => x.Product != null && x.Product.ProdName != null);
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                foreach (var w in SplitSearchWords(searchTerm))
                    qn = qn.Where(x => EF.Functions.Like(x.Product!.ProdName!, "%" + w + "%"));
                var list = await qn.Select(x => x.Product!.ProdName!).Distinct().OrderBy(c => c).Take(500).ToListAsync();
                return list.Select(c => (c, c)).ToList();
            }
            var list2 = await qn.Select(x => x.Product!.ProdName!).Distinct().OrderBy(c => c).Take(300).ToListAsync();
            return list2.Select(c => (c, c)).ToList();
        }

        private static async Task<List<(string Value, string Display)>> GetStockLedgerBatchColumnValuesAsync(IQueryable<StockLedger> q, string searchTerm)
        {
            var qn = q.Where(x => x.BatchNo != null);
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                foreach (var w in SplitSearchWords(searchTerm))
                    qn = qn.Where(x => EF.Functions.Like(x.BatchNo!, "%" + w + "%"));
                var list = await qn.Select(x => x.BatchNo!).Distinct().OrderBy(c => c).Take(500).ToListAsync();
                return list.Select(c => (c, c)).ToList();
            }
            var list2 = await qn.Select(x => x.BatchNo!).Distinct().OrderBy(c => c).Take(300).ToListAsync();
            return list2.Select(c => (c, c)).ToList();
        }

        private static async Task<List<(string Value, string Display)>> GetStockLedgerSourceTypeColumnValuesAsync(IQueryable<StockLedger> q, string searchTerm)
        {
            var qn = q.Where(x => x.SourceType != null);
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                foreach (var w in SplitSearchWords(searchTerm))
                    qn = qn.Where(x => EF.Functions.Like(x.SourceType!, "%" + w + "%"));
                var list = await qn.Select(x => x.SourceType!).Distinct().OrderBy(c => c).Take(200).ToListAsync();
                return list.Select(c => (c, c)).ToList();
            }
            var list2 = await qn.Select(x => x.SourceType!).Distinct().OrderBy(c => c).Take(200).ToListAsync();
            return list2.Select(c => (c, c)).ToList();
        }

        private static async Task<List<(string Value, string Display)>> GetStockLedgerWarehouseNameColumnValuesAsync(IQueryable<StockLedger> q, string searchTerm)
        {
            var qn = q.Where(x => x.Warehouse != null);
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                foreach (var w in SplitSearchWords(searchTerm))
                    qn = qn.Where(x => EF.Functions.Like(x.Warehouse!.WarehouseName, "%" + w + "%"));
                var list = await qn.Select(x => x.Warehouse!.WarehouseName).Distinct().OrderBy(c => c).Take(500).ToListAsync();
                return list.Select(c => (c, c)).ToList();
            }
            var list2 = await qn.Select(x => x.Warehouse!.WarehouseName).Distinct().OrderBy(c => c).Take(300).ToListAsync();
            return list2.Select(c => (c, c)).ToList();
        }

        private static async Task<List<(string Value, string Display)>> GetStockLedgerBranchNameColumnValuesAsync(IQueryable<StockLedger> q, string searchTerm)
        {
            var qn = q.Where(x => x.Warehouse != null && x.Warehouse.Branch != null);
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                foreach (var w in SplitSearchWords(searchTerm))
                    qn = qn.Where(x => EF.Functions.Like(x.Warehouse!.Branch!.BranchName, "%" + w + "%"));
                var list = await qn.Select(x => x.Warehouse!.Branch!.BranchName).Distinct().OrderBy(c => c).Take(500).ToListAsync();
                return list.Select(c => (c, c)).ToList();
            }
            var list2 = await qn.Select(x => x.Warehouse!.Branch!.BranchName).Distinct().OrderBy(c => c).Take(300).ToListAsync();
            return list2.Select(c => (c, c)).ToList();
        }

        private static async Task<List<(string Value, string Display)>> GetStockLedgerCreatedByColumnValuesAsync(IQueryable<StockLedger> q, string searchTerm)
        {
            var qn = q.Where(x => x.User != null);
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                foreach (var w in SplitSearchWords(searchTerm))
                    qn = qn.Where(x => EF.Functions.Like(x.User!.DisplayName, "%" + w + "%"));
                var list = await qn.Select(x => x.User!.DisplayName).Distinct().OrderBy(c => c).Take(500).ToListAsync();
                return list.Select(c => (c, c)).ToList();
            }
            var list2 = await qn.Select(x => x.User!.DisplayName).Distinct().OrderBy(c => c).Take(300).ToListAsync();
            return list2.Select(c => (c, c)).ToList();
        }










        // =========================
        // Export — تصدير حسب اختيار المستخدم (Excel أو CSV)
        // =========================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy = "all",
            string? searchMode = null,
            string? sort = "TranDate",
            string? dir = "desc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromEntry = null,
            int? toEntry = null,
            string? filterCol_entry = null,
            string? filterCol_warehouse = null,
            string? filterCol_product = null,
            string? filterCol_source = null,
            string? filterCol_tran = null,
            string? filterCol_productname = null,
            string? filterCol_batch = null,
            string? filterCol_expiry = null,
            string? filterCol_sourceid = null,
            string? filterCol_sourceline = null,
            string? filterCol_qtyin = null,
            string? filterCol_qtyout = null,
            string? filterCol_unitcost = null,
            string? filterCol_rem = null,
            string? filterCol_priceretail = null,
            string? filterCol_PurchaseDiscount = null,
            string? filterCol_warehouse_name = null,
            string? filterCol_branch = null,
            string? filterCol_createdby = null,
            string? filterCol_entryExpr = null,
            string? filterCol_warehouseExpr = null,
            string? filterCol_productExpr = null,
            string? filterCol_sourceidExpr = null,
            string? filterCol_sourcelineExpr = null,
            string? filterCol_qtyinExpr = null,
            string? filterCol_qtyoutExpr = null,
            string? filterCol_unitcostExpr = null,
            string? filterCol_remExpr = null,
            string? filterCol_priceretailExpr = null,
            string? filterCol_purchasediscountExpr = null,
            string? filterCol_totalcostExpr = null,
            string? format = "excel")
        {
            IQueryable<StockLedger> q = context.StockLedger.AsNoTracking()
                .Include(x => x.Product)
                .Include(x => x.Batch)
                .Include(x => x.Warehouse).ThenInclude(w => w.Branch)
                .Include(x => x.User);

            var sm = ResolveSearchMode(searchMode);
            q = ApplyStockLedgerSearchFilter(q, search, searchBy, sm);
            q = q.ApplySearchSort(
                null, "all",
                sort, dir,
                StringFields, IntFields, OrderFields,
                defaultSearchBy: "all",
                defaultSortBy: "TranDate"
            );

            if (useDateRange)
            {
                if (fromDate.HasValue)
                    q = q.Where(x => x.TranDate >= fromDate.Value);
                if (toDate.HasValue)
                    q = q.Where(x => x.TranDate <= toDate.Value);
            }

            if (fromEntry.HasValue)
                q = q.Where(x => x.EntryId >= fromEntry.Value);
            if (toEntry.HasValue)
                q = q.Where(x => x.EntryId <= toEntry.Value);

            q = ApplyStockLedgerColumnFilters(q,
                filterCol_entryExpr, filterCol_warehouseExpr, filterCol_productExpr,
                filterCol_sourceidExpr, filterCol_sourcelineExpr, filterCol_qtyinExpr, filterCol_qtyoutExpr,
                filterCol_unitcostExpr, filterCol_remExpr, filterCol_priceretailExpr, filterCol_purchasediscountExpr, filterCol_totalcostExpr,
                filterCol_entry, filterCol_warehouse, filterCol_product, filterCol_source, filterCol_tran,
                filterCol_productname, filterCol_batch, filterCol_expiry, filterCol_sourceid, filterCol_sourceline,
                filterCol_qtyin, filterCol_qtyout, filterCol_unitcost, filterCol_rem, filterCol_priceretail, filterCol_PurchaseDiscount,
                filterCol_warehouse_name, filterCol_branch, filterCol_createdby);

            var rows = await q
                .OrderBy(x => x.TranDate)
                .ThenBy(x => x.EntryId)
                .ToListAsync();

            format = (format ?? "excel").Trim().ToLowerInvariant();

            // ===== الفرع الأول: CSV =====
            if (format == "csv")
            {
                var sb = new StringBuilder();   // متغير: نص CSV فى الذاكرة

                // عناوين الأعمدة
                sb.AppendLine(string.Join(",",
                    Csv("رقم القيد"),
                    Csv("رقم الفاتورة"),
                    Csv("سطر المصدر"),
                    Csv("تاريخ الحركة"),
                    Csv("اسم المخزن"),
                    Csv("كود المخزن"),
                    Csv("المنطقة (الفرع)"),
                    Csv("الكاتب"),
                    Csv("اسم الصنف"),
                    Csv("كود الصنف"),
                    Csv("التشغيلة"),
                    Csv("تاريخ الصلاحية"),
                    Csv("سعر الجمهور"),
                    Csv("خصم الشراء"),
                    Csv("كمية داخلة"),
                    Csv("كمية خارجة"),
                    Csv("تكلفة/وحدة"),
                    Csv("إجمالي التكلفة"),
                    Csv("المتبقي (للدخول)"),
                    Csv("نوع المصدر")
                ));

                // البيانات
                foreach (var x in rows)
                {
                    var totalCostVal = x.TotalCost ?? (x.QtyIn * x.UnitCost);
                    var wn = x.Warehouse?.WarehouseName ?? "";
                    var bn = x.Warehouse?.Branch?.BranchName ?? "";
                    var un = x.User?.DisplayName ?? "";
                    var pn = x.Product?.ProdName ?? "";
                    sb.AppendLine(string.Join(",",
                        Csv(x.EntryId.ToString()),
                        Csv(x.SourceId.ToString()),
                        Csv(x.SourceLine.ToString()),
                        Csv(x.TranDate.ToString("yyyy-MM-dd HH:mm:ss")),
                        Csv(wn),
                        Csv(x.WarehouseId.ToString()),
                        Csv(bn),
                        Csv(un),
                        Csv(pn),
                        Csv(x.ProdId.ToString()),
                        Csv(x.BatchNo),
                        Csv(x.Expiry?.ToString("yyyy-MM-dd")),
                        Csv(x.PriceRetailBatch?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        Csv(x.PurchaseDiscount?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        Csv(x.QtyIn.ToString()),
                        Csv(x.QtyOut.ToString()),
                        Csv(x.UnitCost.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        Csv(totalCostVal.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        Csv(x.RemainingQty?.ToString()),
                        Csv(x.SourceType)
                    ));
                }

                // تحويل لـ UTF-8 مع BOM علشان Excel يقرأ عربى صح
                var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
                var bytes = utf8.GetBytes(sb.ToString());
                var fileNameCsv = ExcelExportNaming.ArabicTimestampedFileName("سجل الحركات", ".csv");

                return File(bytes, "text/csv; charset=utf-8", fileNameCsv);
            }

            // ===== الفرع الثانى: Excel (XLSX) =====
            using var workbook = new XLWorkbook();                // متغير: ملف Excel
            var ws = workbook.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("سجل الحركات"));

            int r = 1; // متغير: رقم الصف الحالى

            // عناوين الأعمدة
            ws.Cell(r, 1).Value = "رقم القيد";
            ws.Cell(r, 2).Value = "رقم الفاتورة";
            ws.Cell(r, 3).Value = "سطر المصدر";
            ws.Cell(r, 4).Value = "تاريخ الحركة";
            ws.Cell(r, 5).Value = "اسم المخزن";
            ws.Cell(r, 6).Value = "كود المخزن";
            ws.Cell(r, 7).Value = "المنطقة (الفرع)";
            ws.Cell(r, 8).Value = "الكاتب";
            ws.Cell(r, 9).Value = "اسم الصنف";
            ws.Cell(r, 10).Value = "كود الصنف";
            ws.Cell(r, 11).Value = "التشغيلة";
            ws.Cell(r, 12).Value = "تاريخ الصلاحية";
            ws.Cell(r, 13).Value = "سعر الجمهور";
            ws.Cell(r, 14).Value = "خصم الشراء";
            ws.Cell(r, 15).Value = "كمية داخلة";
            ws.Cell(r, 16).Value = "كمية خارجة";
            ws.Cell(r, 17).Value = "تكلفة/وحدة";
            ws.Cell(r, 18).Value = "إجمالي التكلفة";
            ws.Cell(r, 19).Value = "المتبقي (للدخول)";
            ws.Cell(r, 20).Value = "نوع المصدر";

            // تنسيق العناوين
            var headerRange = ws.Range(r, 1, r, 20);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // تعبئة البيانات
            foreach (var x in rows)
            {
                r++;
                var totalCostVal = x.TotalCost ?? (x.QtyIn * x.UnitCost);
                ws.Cell(r, 1).Value = x.EntryId;
                ws.Cell(r, 2).Value = x.SourceId;
                ws.Cell(r, 3).Value = x.SourceLine;
                ws.Cell(r, 4).Value = x.TranDate;
                ws.Cell(r, 5).Value = x.Warehouse?.WarehouseName ?? "";
                ws.Cell(r, 6).Value = x.WarehouseId;
                ws.Cell(r, 7).Value = x.Warehouse?.Branch?.BranchName ?? "";
                ws.Cell(r, 8).Value = x.User?.DisplayName ?? "";
                ws.Cell(r, 9).Value = x.Product?.ProdName ?? "";
                ws.Cell(r, 10).Value = x.ProdId;
                ws.Cell(r, 11).Value = x.BatchNo ?? "";
                ws.Cell(r, 12).Value = x.Expiry;
                ws.Cell(r, 13).Value = x.PriceRetailBatch.HasValue ? (double?)(double)x.PriceRetailBatch.Value : null;
                ws.Cell(r, 14).Value = x.PurchaseDiscount.HasValue ? (double?)(double)x.PurchaseDiscount.Value : null;
                ws.Cell(r, 15).Value = x.QtyIn;
                ws.Cell(r, 16).Value = x.QtyOut;
                ws.Cell(r, 17).Value = (double)x.UnitCost;
                ws.Cell(r, 18).Value = (double)totalCostVal;
                ws.Cell(r, 19).Value = x.RemainingQty ?? 0;
                ws.Cell(r, 20).Value = x.SourceType;
            }

            // ضبط عرض الأعمدة + تنسيق الأرقام
            ws.Columns().AdjustToContents();
            ws.Column(15).Style.NumberFormat.Format = "0";
            ws.Column(16).Style.NumberFormat.Format = "0";
            ws.Column(17).Style.NumberFormat.Format = "0.0000";
            ws.Column(18).Style.NumberFormat.Format = "0.00";
            ws.Column(19).Style.NumberFormat.Format = "0";

            using var stream = new MemoryStream();     // متغير: الذاكرة المؤقتة
            workbook.SaveAs(stream);
            stream.Position = 0;

            var fileNameXlsx = ExcelExportNaming.ArabicTimestampedFileName("سجل الحركات", ".xlsx");
            const string contentTypeXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

            return File(stream.ToArray(), contentTypeXlsx, fileNameXlsx);
        }

        // دالة مساعدة لتجهيز النص للـ CSV
        private static string Csv(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            var s = value.Replace("\"", "\"\"");   // استبدال " بـ ""

            if (s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
                return "\"" + s + "\"";           // لو فيه فواصل/سطور جديدة نحط النص بين ""

            return s;
        }
    }
}
