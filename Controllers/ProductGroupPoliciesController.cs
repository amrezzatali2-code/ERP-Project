using ClosedXML.Excel;                      // متغيرات Excel (ClosedXML)
using ERP.Data;                             // كائن الاتصال بقاعدة البيانات AppDbContext
using ERP.Filters;
using ERP.Infrastructure;                   // كلاس PagedResult + ApplySearchSort
using ERP.Models;                           // الموديل ProductGroupPolicy
using ERP.Security;
using ERP.Services.Caching;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;   // SelectList للقوائم المنسدلة
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;           // Dictionary
using System.Globalization;                 // CultureInfo لتنسيق الأرقام في CSV
using System.IO;                            // MemoryStream
using System.Linq;
using System.Linq.Expressions;              // Expressions
using System.Text;                          // StringBuilder للتصدير
using System.Threading.Tasks;

namespace ERP.Controllers
{
    /// <summary>
    /// إدارة جدول سياسات مجموعات الأصناف (ProductGroupPolicy)
    /// كل صف = سياسة معينة تطبَّق على مجموعة أصناف محددة داخل مخزن معيّن،
    /// مع تحديد أقصى خصم للعميل وإمكانية تفعيل/إيقاف القاعدة.
    /// </summary>
    [RequirePermission("ProductGroupPolicies.Index")]
    public class ProductGroupPoliciesController : Controller
    {
        private readonly AppDbContext _context;   // متغير: اتصال بقاعدة البيانات
        private readonly ILookupCacheService _lookupCache;

        public ProductGroupPoliciesController(AppDbContext context, ILookupCacheService lookupCache)
        {
            _context = context;
            _lookupCache = lookupCache;
        }

        /// <summary>
        /// بناء خريطة نسبة الربح من سياسات المخازن لبنود ذات ProfitPercent = 0
        /// </summary>
        private async Task<Dictionary<string, decimal>> BuildWarehouseProfitMapAsync(IEnumerable<ProductGroupPolicy> items)
        {
            var zeroItems = items.Where(x => x.ProfitPercent == 0).ToList();
            if (!zeroItems.Any()) return new Dictionary<string, decimal>();

            var pairs = zeroItems.Select(x => (x.PolicyId, x.WarehouseId)).Distinct().ToList();
            var policyIds = pairs.Select(p => p.PolicyId).Distinct().ToList();
            var warehouseIds = pairs.Select(p => p.WarehouseId).Distinct().ToList();
            var pairSet = new HashSet<(int, int)>(pairs);

            // جلب القواعد ثم فلترة في الذاكرة (لتفادي خطأ ترجمة LINQ إلى SQL)
            var rules = await _context.WarehousePolicyRules
                .AsNoTracking()
                .Where(w => w.IsActive && policyIds.Contains(w.PolicyId) && warehouseIds.Contains(w.WarehouseId))
                .Select(w => new { w.PolicyId, w.WarehouseId, w.ProfitPercent })
                .ToListAsync();

            var map = new Dictionary<string, decimal>();
            foreach (var r in rules.Where(r => pairSet.Contains((r.PolicyId, r.WarehouseId))))
                map[$"{r.PolicyId}_{r.WarehouseId}"] = r.ProfitPercent;
            return map;
        }

        // =========================
        // دالة مساعدة لتحميل قوائم:
        // - مجموعات الأصناف
        // - السياسات
        // - المخازن
        // تُستخدم في Create و Edit
        // =========================
        private async Task LoadLookupsAsync(
            int? productGroupId = null,
            int? policyId = null,
            int? warehouseId = null)
        {
            // جلب مجموعات الأصناف بالاسم
            var groups = await _lookupCache.GetProductGroupsAsync();

            ViewBag.ProductGroupList = new SelectList(
                groups,
                "ProductGroupId",              // المفتاح في جدول المجموعات
                "Name",                        // اسم المجموعة الظاهر في القائمة
                productGroupId                 // المجموعة المختارة حاليًا (للـ Edit)
            );

            // جلب السياسات بالاسم
            var policies = await _lookupCache.GetPoliciesAsync();

            ViewBag.PolicyList = new SelectList(
                policies,
                "PolicyId",                    // كود السياسة
                "Name",                        // اسم السياسة
                policyId
            );

            // جلب المخازن بالاسم
            var warehouses = await _lookupCache.GetWarehousesAsync();

            ViewBag.WarehouseList = new SelectList(
                warehouses,
                "WarehouseId",                 // كود المخزن
                "WarehouseName",               // اسم المخزن الظاهر
                warehouseId
            );
        }

        // =========================
        // دالة خاصة لبناء استعلام سياسات المجموعات
        // (بحث + فلتر كود من/إلى + فلتر تاريخ اختياري + ترتيب)
        // =========================
        private IQueryable<ProductGroupPolicy> BuildPoliciesQuery(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            int? fromCode,
            int? toCode,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate)
        {
            // 1) الاستعلام الأساسي مع تحميل الأسماء (مجموعة، سياسة، مخزن)
            IQueryable<ProductGroupPolicy> q =
                _context.ProductGroupPolicies
                    .Include(x => x.ProductGroup)
                    .Include(x => x.Policy)
                    .Include(x => x.Warehouse)
                    .AsNoTracking();

            // 2) فلتر الكود من/إلى (على Id = كود القاعدة)
            if (fromCode.HasValue)
                q = q.Where(x => x.Id >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(x => x.Id <= toCode.Value);

            // 3) فلتر التاريخ (على CreatedAt) لو مفعّل
            if (useDateRange)
            {
                if (fromDate.HasValue)
                    q = q.Where(x => x.CreatedAt >= fromDate.Value);

                if (toDate.HasValue)
                    q = q.Where(x => x.CreatedAt <= toDate.Value);
            }

            // 4) بحث مخصص لـ active و created
            string? searchForSort = search;
            string? searchByForSort = searchBy;
            if (!string.IsNullOrWhiteSpace(search) && !string.IsNullOrWhiteSpace(searchBy))
            {
                var sb = searchBy.Trim().ToLowerInvariant();
                var text = search!.Trim();

                if (sb == "active")
                {
                    if (text.Contains("نعم") || text.Contains("yes") || text.Equals("1", StringComparison.OrdinalIgnoreCase))
                        q = q.Where(x => x.IsActive);
                    else if (text.Contains("لا") || text.Contains("no") || text.Equals("0", StringComparison.OrdinalIgnoreCase))
                        q = q.Where(x => !x.IsActive);
                    searchForSort = null;
                    searchByForSort = null;
                }
                else if (sb == "created" && DateTime.TryParse(text, out var dtCreated))
                {
                    q = q.Where(x => x.CreatedAt.Date == dtCreated.Date);
                    searchForSort = null;
                    searchByForSort = null;
                }
            }

            // 5) الحقول النصية للبحث
            var stringFields =
                new Dictionary<string, Expression<Func<ProductGroupPolicy, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["groupname"] = x => x.ProductGroup != null ? x.ProductGroup.Name : null,
                    ["policyname"] = x => x.Policy != null ? x.Policy.Name : null,
                    ["warehousename"] = x => x.Warehouse != null ? x.Warehouse.WarehouseName : null,
                    ["status"] = x => x.IsActive ? "active" : "inactive",
                    ["active"] = x => x.IsActive ? "نعم" : "لا"
                };

            // 6) الحقول العددية للبحث (نص البحث رقم)
            var intFields =
                new Dictionary<string, Expression<Func<ProductGroupPolicy, int>>>()
                {
                    ["id"] = x => x.Id,             // كود القاعدة
                    ["group"] = x => x.ProductGroupId, // كود مجموعة الأصناف
                    ["policy"] = x => x.PolicyId,       // كود السياسة
                    ["warehouse"] = x => x.WarehouseId     // كود المخزن
                };

            // 7) حقول الترتيب في رأس الجدول
            var orderFields =
                new Dictionary<string, Expression<Func<ProductGroupPolicy, object>>>()
                {
                    ["id"] = x => x.Id,
                    ["group"] = x => x.ProductGroup != null ? x.ProductGroup.Name : "",
                    ["policy"] = x => x.Policy != null ? x.Policy.Name : "",
                    ["warehouse"] = x => x.Warehouse != null ? x.Warehouse.WarehouseName : "",
                    ["profit"] = x => x.ProfitPercent,
                    ["active"] = x => x.IsActive,
                    ["created"] = x => x.CreatedAt
                };

            // 8) تطبيق البحث + الترتيب باستخدام الإكستنشن الموحد
            q = q.ApplySearchSort(
                searchForSort,             // نص البحث
                searchByForSort,           // نوع البحث
                sort,                      // عمود الترتيب
                dir,                       // اتجاه الترتيب asc/desc
                stringFields,
                intFields,
                orderFields,
                defaultSearchBy: "id",     // افتراضياً نبحث بالكود
                defaultSortBy: "id"        // وافتراضياً نرتّب بالكود
            );

            return q;
        }

        private static readonly char[] _filterSep = { '|', ',', ';' };

        /// <summary>تطبيق فلاتر الأعمدة (نظام البحث الشبيه بـ Excel).</summary>
        private IQueryable<ProductGroupPolicy> ApplyColumnFilters(
            IQueryable<ProductGroupPolicy> q,
            string? filterCol_id,
            string? filterCol_group,
            string? filterCol_policy,
            string? filterCol_warehouse,
            string? filterCol_profit,
            string? filterCol_active,
            string? filterCol_created)
        {
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    q = q.Where(x => ids.Contains(x.Id));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_group))
            {
                var terms = filterCol_group.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
                if (terms.Count > 0)
                    q = q.Where(x => x.ProductGroup != null && terms.Contains(x.ProductGroup.Name));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_policy))
            {
                var terms = filterCol_policy.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
                if (terms.Count > 0)
                    q = q.Where(x => x.Policy != null && terms.Contains(x.Policy.Name));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_warehouse))
            {
                var terms = filterCol_warehouse.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
                if (terms.Count > 0)
                    q = q.Where(x => x.Warehouse != null && terms.Contains(x.Warehouse.WarehouseName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_profit))
            {
                var terms = filterCol_profit.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .Select(t => decimal.TryParse(t, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (terms.Count > 0)
                    q = q.Where(x => terms.Contains(x.ProfitPercent));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_active))
            {
                var parts = filterCol_active.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim().ToLowerInvariant()).ToHashSet();
                if (parts.Contains("true") && !parts.Contains("false"))
                    q = q.Where(x => x.IsActive);
                else if (parts.Contains("false") && !parts.Contains("true"))
                    q = q.Where(x => !x.IsActive);
            }
            if (!string.IsNullOrWhiteSpace(filterCol_created))
            {
                var terms = filterCol_created.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
                if (terms.Count > 0)
                    q = q.Where(x => terms.Any(t => x.CreatedAt.ToString("yyyy-MM-dd HH:mm").Contains(t)));
            }
            return q;
        }

        /// <summary>قيم مميزة للعمود (للوحة فلتر الأعمدة بنمط Excel).</summary>
        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var col = (column ?? "").Trim().ToLowerInvariant();
            var q = _context.ProductGroupPolicies
                .Include(x => x.ProductGroup)
                .Include(x => x.Policy)
                .Include(x => x.Warehouse)
                .AsNoTracking();

            if (col == "id")
            {
                var ids = await q.Select(x => x.Id).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (col == "group")
            {
                var list = await q.Where(x => x.ProductGroup != null).Select(x => x.ProductGroup!.Name!).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm))
                    list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v ?? "", display = v ?? "" }));
            }
            if (col == "policy")
            {
                var list = await q.Where(x => x.Policy != null).Select(x => x.Policy!.Name!).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm))
                    list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v ?? "", display = v ?? "" }));
            }
            if (col == "warehouse")
            {
                var list = await q.Where(x => x.Warehouse != null).Select(x => x.Warehouse!.WarehouseName!).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm))
                    list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v ?? "", display = v ?? "" }));
            }
            if (col == "profit")
            {
                var values = await q.Select(x => x.ProfitPercent).Distinct().OrderBy(x => x).Take(300).ToListAsync();
                var list = values.Select(v => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Distinct().ToList();
                if (!string.IsNullOrEmpty(searchTerm))
                    list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (col == "active")
            {
                return Json(new[] { new { value = "true", display = "نعم" }, new { value = "false", display = "لا" } });
            }
            if (col == "created")
            {
                var dates = await q.Select(x => x.CreatedAt).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                var list = dates.Select(d => d.ToString("yyyy-MM-dd HH:mm")).Distinct().ToList();
                if (!string.IsNullOrEmpty(searchTerm))
                    list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            return Json(new List<object>());
        }

        // =========================
        // Index — قائمة سياسات مجموعات الأصناف
        // =========================
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "id",        // id | group | policy | warehouse | status
            string? sort = "id",            // id | group | policy | warehouse | active | created
            string? dir = "asc",            // asc | desc
            int page = 1,
            int pageSize = 25,
            int? fromCode = null,           // فلتر كود من
            int? toCode = null,             // فلتر كود إلى
            bool useDateRange = false,      // تفعيل فلتر التاريخ
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? filterCol_id = null,
            string? filterCol_group = null,
            string? filterCol_policy = null,
            string? filterCol_warehouse = null,
            string? filterCol_profit = null,
            string? filterCol_active = null,
            string? filterCol_created = null)
        {
            // بناء الاستعلام طبقاً للفلاتر
            var q = BuildPoliciesQuery(
                search,
                searchBy,
                sort,
                dir,
                fromCode,
                toCode,
                useDateRange,
                fromDate,
                toDate);

            q = ApplyColumnFilters(q, filterCol_id, filterCol_group, filterCol_policy, filterCol_warehouse, filterCol_profit, filterCol_active, filterCol_created);

            // تقسيم الصفحات
            var model = await PagedResult<ProductGroupPolicy>.CreateAsync(q, page, pageSize);

            // تعبئة خصائص البحث/الترتيب داخل الموديل (للاستخدام في الواجهة)
            model.Search = search ?? "";
            model.SearchBy = searchBy ?? "id";
            model.SortColumn = sort ?? "id";
            model.SortDescending = (dir?.ToLower() == "desc");
            model.UseDateRange = useDateRange;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            // تمرير فلتر الكود عن طريق ViewBag (مثل الجداول السابقة)
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.CodeFrom = fromCode;
            ViewBag.CodeTo = toCode;

            // فلتر الأعمدة (نظام البحث Excel)
            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_Group = filterCol_group;
            ViewBag.FilterCol_Policy = filterCol_policy;
            ViewBag.FilterCol_Warehouse = filterCol_warehouse;
            ViewBag.FilterCol_Profit = filterCol_profit;
            ViewBag.FilterCol_Active = filterCol_active;
            ViewBag.FilterCol_Created = filterCol_created;

            // حقل التاريخ المستخدم في الفلترة (للنموذج الموحد)
            ViewBag.DateField = "CreatedAt";

            // نسبة الربح من سياسات المخازن عندما تكون صفر في ProductGroupPolicy
            ViewBag.WarehouseProfitMap = await BuildWarehouseProfitMapAsync(model.Items);

            return View(model);
        }

        // =========================
        // Export — تصدير سياسات المجموعات
        // يدعم:
        // - Excel (ملف .xlsx)
        // - CSV
        // مع إظهار أسماء المجموعة/السياسة/المخزن
        // =========================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            int? fromCode = null,           // fromCode للاسم الجديد
            int? toCode = null,
            int? codeFrom = null,           // دعم الأسماء القديمة codeFrom/codeTo لو موجودة في الواجهة
            int? codeTo = null,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? filterCol_id = null,
            string? filterCol_group = null,
            string? filterCol_policy = null,
            string? filterCol_warehouse = null,
            string? filterCol_profit = null,
            string? filterCol_active = null,
            string? filterCol_created = null,
            string format = "excel")        // excel | csv
        {
            // توحيد الأسماء (لو من الواجهة القديمة)
            if (!fromCode.HasValue && codeFrom.HasValue)
                fromCode = codeFrom;

            if (!toCode.HasValue && codeTo.HasValue)
                toCode = codeTo;

            // 1) بناء الاستعلام بنفس فلاتر الواجهة
            var query = BuildPoliciesQuery(
                search,
                searchBy,
                sort,
                dir,
                fromCode,
                toCode,
                useDateRange,
                fromDate,
                toDate);

            query = ApplyColumnFilters(query, filterCol_id, filterCol_group, filterCol_policy, filterCol_warehouse, filterCol_profit, filterCol_active, filterCol_created);

            // 2) جلب كل النتائج (بدون Paging) — الـ Include موجود في BuildPoliciesQuery
            var list = await query.ToListAsync();

            // خريطة نسبة الربح من سياسات المخازن (عندما تكون صفر في ProductGroupPolicy)
            var profitMap = await BuildWarehouseProfitMapAsync(list);

            decimal GetEffectiveProfit(ProductGroupPolicy r)
            {
                if (r.ProfitPercent > 0) return r.ProfitPercent;
                return profitMap.TryGetValue($"{r.PolicyId}_{r.WarehouseId}", out var p) ? p : 0m;
            }

            // 3) لو المطلوب Excel (افتراضي)
            if (string.Equals(format, "excel", StringComparison.OrdinalIgnoreCase))
            {
                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("سياسات مجموعات الأصناف"));

                int row = 1;

                // عناوين الأعمدة
                worksheet.Cell(row, 1).Value = "كود القاعدة";
                worksheet.Cell(row, 2).Value = "اسم المجموعة";
                worksheet.Cell(row, 3).Value = "اسم السياسة";
                worksheet.Cell(row, 4).Value = "اسم المخزن";
                worksheet.Cell(row, 5).Value = "نسبة الربح %";
                worksheet.Cell(row, 6).Value = "أقصى خصم للعميل %";
                worksheet.Cell(row, 7).Value = "مفعّلة؟";
                worksheet.Cell(row, 8).Value = "تاريخ الإنشاء";
                worksheet.Cell(row, 9).Value = "آخر تعديل";

                // تنسيق الهيدر
                var header = worksheet.Range(row, 1, row, 9);
                header.Style.Font.Bold = true;

                // كتابة البيانات
                foreach (var r in list)
                {
                    row++;

                    worksheet.Cell(row, 1).Value = r.Id;
                    worksheet.Cell(row, 2).Value = r.ProductGroup?.Name ?? "";
                    worksheet.Cell(row, 3).Value = r.Policy?.Name ?? "";
                    worksheet.Cell(row, 4).Value = r.Warehouse?.WarehouseName ?? "";
                    worksheet.Cell(row, 5).Value = GetEffectiveProfit(r);
                    worksheet.Cell(row, 6).Value = r.MaxDiscountToCustomer;
                    worksheet.Cell(row, 7).Value = r.IsActive ? "مفعّلة" : "موقوفة";
                    worksheet.Cell(row, 8).Value = r.CreatedAt;
                    worksheet.Cell(row, 9).Value = r.UpdatedAt;
                }

                worksheet.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                stream.Position = 0;

                var fileName = ExcelExportNaming.ArabicTimestampedFileName("سياسات مجموعات الأصناف", ".xlsx");
                const string contentType =
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

                return File(stream.ToArray(), contentType, fileName);
            }
            else
            {
                // 4) حالة CSV
                var sb = new StringBuilder();

                sb.AppendLine("كود القاعدة,اسم المجموعة,اسم السياسة,اسم المخزن,نسبة الربح %,أقصى خصم للعميل %,مفعّلة؟,تاريخ الإنشاء,آخر تعديل");

                foreach (var r in list)
                {
                    string groupName = (r.ProductGroup?.Name ?? string.Empty).Replace("\"", "\"\"");
                    string policyName = (r.Policy?.Name ?? string.Empty).Replace("\"", "\"\"");
                    string warehouseName = (r.Warehouse?.WarehouseName ?? string.Empty).Replace("\"", "\"\"");

                    string created = r.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                    string updated = r.UpdatedAt.HasValue ? r.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm") : string.Empty;
                    string maxDiscountText = r.MaxDiscountToCustomer.HasValue ? r.MaxDiscountToCustomer.Value.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;

                    sb.AppendLine(
                        $"{r.Id},\"{groupName}\",\"{policyName}\",\"{warehouseName}\"," +
                        $"{GetEffectiveProfit(r).ToString("0.##", CultureInfo.InvariantCulture)}," +
                        $"{maxDiscountText}," +
                        $"\"{(r.IsActive ? "مفعّلة" : "موقوفة")}\"," +
                        $"{created}," +
                        $"{updated}");
                }

                byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString());
                string fileName = ExcelExportNaming.ArabicTimestampedFileName("سياسات مجموعات الأصناف", ".csv");

                return File(bytes, "text/csv; charset=utf-8", fileName);
            }
        }

        // =========================
        // Details — عرض القاعدة (قراءة فقط)
        // =========================
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
                return NotFound();

            var policy = await _context.ProductGroupPolicies
                                       .Include(p => p.ProductGroup)
                                       .Include(p => p.Policy)
                                       .Include(p => p.Warehouse)
                                       .AsNoTracking()
                                       .FirstOrDefaultAsync(p => p.Id == id.Value);
            if (policy == null)
                return NotFound();

            // نسبة الربح الفعلية (من سياسات المخازن إن كانت صفر)
            var profitMap = await BuildWarehouseProfitMapAsync(new[] { policy });
            ViewBag.EffectiveProfit = policy.ProfitPercent > 0
                ? policy.ProfitPercent
                : (profitMap.TryGetValue($"{policy.PolicyId}_{policy.WarehouseId}", out var p) ? p : 0m);

            return View(policy);
        }

        // =========================
        // Create — GET: شاشة إضافة قاعدة جديدة
        // =========================
        public async Task<IActionResult> Create()
        {
            // تحميل القوائم (مجموعات + سياسات + مخازن)
            await LoadLookupsAsync();

            // القاعدة مفعّلة افتراضيًا
            var model = new ProductGroupPolicy
            {
                IsActive = true
            };

            return View(model);
        }

        // =========================
        // Create — POST: حفظ القاعدة الجديدة
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ProductGroupPolicy policy)
        {
            // تحقق بسيط على الأكواد
            if (policy.ProductGroupId <= 0)
            {
                ModelState.AddModelError(nameof(ProductGroupPolicy.ProductGroupId),
                    "يجب اختيار مجموعة أصناف صحيحة.");
            }

            if (policy.PolicyId <= 0)
            {
                ModelState.AddModelError(nameof(ProductGroupPolicy.PolicyId),
                    "يجب اختيار سياسة صحيحة.");
            }

            if (policy.WarehouseId <= 0)
            {
                ModelState.AddModelError(nameof(ProductGroupPolicy.WarehouseId),
                    "يجب اختيار مخزن صحيح.");
            }

            if (!ModelState.IsValid)
            {
                // إعادة تحميل القوائم عند وجود أخطاء
                await LoadLookupsAsync(policy.ProductGroupId, policy.PolicyId, policy.WarehouseId);
                return View(policy);
            }

            policy.CreatedAt = DateTime.Now;   // تثبيت تاريخ الإنشاء

            _context.ProductGroupPolicies.Add(policy);
            await _context.SaveChangesAsync();

            TempData["Msg"] = "تم إضافة سياسة لمجموعة الأصناف بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // Edit — GET: فتح القاعدة للتعديل
        // =========================
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
                return NotFound();

            var policy = await _context.ProductGroupPolicies
                                       .AsNoTracking()
                                       .FirstOrDefaultAsync(p => p.Id == id.Value);
            if (policy == null)
                return NotFound();

            // تحميل القوائم مع تحديد القيم المختارة
            await LoadLookupsAsync(policy.ProductGroupId, policy.PolicyId, policy.WarehouseId);

            return View(policy);
        }

        // =========================
        // Edit — POST: حفظ التعديل
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ProductGroupPolicy policy)
        {
            if (id != policy.Id)
                return NotFound();

            if (policy.ProductGroupId <= 0)
            {
                ModelState.AddModelError(nameof(ProductGroupPolicy.ProductGroupId),
                    "يجب اختيار مجموعة أصناف صحيحة.");
            }

            if (policy.PolicyId <= 0)
            {
                ModelState.AddModelError(nameof(ProductGroupPolicy.PolicyId),
                    "يجب اختيار سياسة صحيحة.");
            }

            if (policy.WarehouseId <= 0)
            {
                ModelState.AddModelError(nameof(ProductGroupPolicy.WarehouseId),
                    "يجب اختيار مخزن صحيح.");
            }

            if (!ModelState.IsValid)
            {
                await LoadLookupsAsync(policy.ProductGroupId, policy.PolicyId, policy.WarehouseId);
                return View(policy);
            }

            try
            {
                policy.UpdatedAt = DateTime.Now;    // آخر تعديل
                _context.Update(policy);
                await _context.SaveChangesAsync();

                TempData["Msg"] = "تم تعديل سياسة مجموعة الأصناف بنجاح.";
            }
            catch (DbUpdateConcurrencyException)
            {
                bool exists = await _context.ProductGroupPolicies
                                            .AnyAsync(e => e.Id == id);
                if (!exists)
                    return NotFound();

                ModelState.AddModelError(
                    string.Empty,
                    "تعذّر الحفظ بسبب تعارض في التعديل. أعد تحميل الصفحة وحاول مرة أخرى.");

                await LoadLookupsAsync(policy.ProductGroupId, policy.PolicyId, policy.WarehouseId);
                return View(policy);
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================
        // Delete — GET: صفحة تأكيد الحذف
        // =========================
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
                return NotFound();

            var policy = await _context.ProductGroupPolicies
                                       .AsNoTracking()
                                       .FirstOrDefaultAsync(p => p.Id == id.Value);
            if (policy == null)
                return NotFound();

            return View(policy);
        }

        // =========================
        // Delete — POST: حذف قاعدة واحدة
        // =========================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var policy = await _context.ProductGroupPolicies
                                       .FirstOrDefaultAsync(p => p.Id == id);
            if (policy == null)
                return NotFound();

            _context.ProductGroupPolicies.Remove(policy);
            await _context.SaveChangesAsync();

            TempData["Msg"] = "تم حذف سياسة مجموعة الأصناف.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // BulkDelete — حذف مجموعة سياسات محددة
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["Msg"] = "لم يتم اختيار أي سياسات للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var ids = selectedIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var n) ? (int?)n : null)
                .Where(n => n.HasValue)
                .Select(n => n!.Value)
                .ToList();

            if (!ids.Any())
            {
                TempData["Msg"] = "لم يتم اختيار أكواد صحيحة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var policies = await _context.ProductGroupPolicies
                                         .Where(p => ids.Contains(p.Id))
                                         .ToListAsync();

            if (!policies.Any())
            {
                TempData["Msg"] = "لم يتم العثور على السياسات المحددة.";
                return RedirectToAction(nameof(Index));
            }

            _context.ProductGroupPolicies.RemoveRange(policies);
            await _context.SaveChangesAsync();

            TempData["Msg"] = $"تم حذف {policies.Count} سياسة لمجموعات الأصناف.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // DeleteAll — حذف جميع السياسات (خطير)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var policies = await _context.ProductGroupPolicies.ToListAsync();
            if (!policies.Any())
            {
                TempData["Msg"] = "لا توجد سياسات لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            _context.ProductGroupPolicies.RemoveRange(policies);
            await _context.SaveChangesAsync();

            TempData["Msg"] = $"تم حذف جميع سياسات مجموعات الأصناف ({policies.Count}).";
            return RedirectToAction(nameof(Index));
        }
    }
}
