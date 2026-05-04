// Controllers/ProductsController.cs
using ClosedXML.Excel;                  // مكتبة Excel
using OfficeOpenXml;
using DocumentFormat.OpenXml.InkML;
using ERP.Data;                         // سياق قاعدة البيانات الرئيسي
using ERP.Filters;                      // RequirePermission
using ERP.Infrastructure;               // PagedResult + UserActivityLogger
using ERP.Models;                       // Product, Category, UserActionType...
using ERP.Security;                     // PermissionCodes
using ERP.Services;                     // StockAnalysisService, IPermissionService, ILedgerPostingService
using ERP.Services.Caching;             // كاش البحث عن الأصناف/الأطراف (lookup آمن)
using ERP.ViewModels;                   // ViewModels الخاصة بحركة الصنف
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;                        // MemoryStream
using System.Linq;
using System.Text;                      // StringBuilder + Encoding للـ CSV
using System.Threading.Tasks;
using System.Linq.Expressions; // عشان Expression<Func<...>>


namespace ERP.Controllers
{
    /// <summary>
    /// الكنترولر الخاص بالأصناف: قائمة + تفاصيل + إضافة + تعديل + حذف + حركة صنف (Show)
    /// </summary>
    public class ProductsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly StockAnalysisService _stockAnalysisService;
        private readonly IUserActivityLogger _activityLogger;
        private readonly IPermissionService _permissionService;
        private readonly IUserAccountVisibilityService _accountVisibilityService;
        private readonly IProductCacheService _productCache;
        private readonly ICustomerCacheService _customerCache;
        private readonly ILookupCacheService _lookupCache;

        public ProductsController(
            AppDbContext db,
            StockAnalysisService stockAnalysisService,
            IUserActivityLogger activityLogger,
            IPermissionService permissionService,
            IUserAccountVisibilityService accountVisibilityService,
            IProductCacheService productCache,
            ICustomerCacheService customerCache,
            ILookupCacheService lookupCache)
        {
            _db = db;
            _stockAnalysisService = stockAnalysisService;
            _activityLogger = activityLogger;
            _permissionService = permissionService;
            _accountVisibilityService = accountVisibilityService;
            _productCache = productCache;
            _customerCache = customerCache;
            _lookupCache = lookupCache;
        }

        private async Task LoadWarehousesDDLAsync()
        {
            ViewBag.Warehouses = (await _lookupCache.GetWarehousesAsync())
                .Where(w => w.IsActive)
                .OrderBy(w => w.WarehouseName)
                .Select(w => new SelectListItem
                {
                    Value = w.WarehouseId.ToString(),
                    Text = w.WarehouseName
                })
                .ToList();
        }












        // =========================================================
        // 1) قائمة الأصناف  (نظام القوائم الموحد)
        // =========================================================
        [HttpGet]
        [RequirePermission("Products.Index")]
        public async Task<IActionResult> Index(
      string? search,                    // متغير: نص البحث العام
      string? searchBy = "all",          // متغير: عمود البحث (all|name|id|code|gen|category|comp|price|active|imported|updated)
      string? searchMode = "contains",   // ✅ متغير: طريقة البحث (contains | starts | equals)
      string? sort = "name",             // متغير: عمود الترتيب
      string? dir = "asc",               // متغير: اتجاه الترتيب asc | desc
      bool useDateRange = false,         // متغير: هل فلترة التاريخ مفعلة؟
      DateTime? fromDate = null,         // متغير: تاريخ/وقت من (CreatedAt)
      DateTime? toDate = null,           // متغير: تاريخ/وقت إلى (CreatedAt)

      // ✅ فلتر أرقام (من/إلى) — قابل للتوسع
      string? numField = null,           // متغير: حقل الرقم (id | price)
      decimal? fromNum = null,           // متغير: رقم من
      decimal? toNum = null,             // متغير: رقم إلى
      int? codeFrom = null,              // متغير: توافق مع الواجهة (يُستخدم كـ fromNum عند numField=id)
      int? codeTo = null,                // متغير: توافق مع الواجهة (يُستخدم كـ toNum عند numField=id)

      // ✅ فلاتر متراكبة جديدة
      int? filterProductGroupId = null,  // متغير: فلتر حسب مجموعة الصنف (ProductGroup)
      string? filterCompany = null,      // متغير: فلتر حسب الشركة (Company)
      string? filterImported = null,     // متغير: فلتر حسب المنشأ (محلي/مستورد)

      // ✅ فلاتر أعمدة بنمط Excel (قيم متعددة مفصولة بـ |)
      string? filterCol_productGroup = null,
      string? filterCol_bonusGroup = null,
      string? filterCol_company = null,
      string? filterCol_location = null,
      string? filterCol_warehouse = null,
      string? filterCol_imported = null,
      string? filterCol_isactive = null,
      // فلاتر باقي الأعمدة (+ _contains للبحث النصي "يحتوي")
      string? filterCol_id = null,
      string? filterCol_name = null,
      string? filterCol_name_contains = null,
      string? filterCol_barcode = null,
      string? filterCol_generic = null,
      string? filterCol_category = null,
      string? filterCol_classification = null,
      string? filterCol_price = null,
      string? filterCol_created = null,
      string? filterCol_modified = null,
      string? filterCol_lastprice = null,
      string? filterCol_hasQuota = null,
      string? filterCol_quotaQuantity = null,
      string? filterCol_cartonQuantity = null,
      string? filterCol_packQuantity = null,
      string? filterCol_descShort = null,
      // فلاتر رقمية متقدمة (صيغ < > من:إلى)
      string? filterCol_idExpr = null,      // مثل: <10 أو >10 أو 10:100
      string? filterCol_priceExpr = null,   // مثل: <100 أو >50 أو 50:200

      int page = 1,                      // متغير: رقم الصفحة الحالية
      int pageSize = 10,                 // متغير: عدد السطور في الصفحة (الافتراضي 10، 0 = الكل)
      bool print = false                 // عند true: طباعة كل النتائج المفلترة في صفحة واحدة (كل الأعمدة)
  )
        {
            // قراءة حجم الصفحة من الـ Query: نأخذ آخر قيمة لضمان تطابق القائمة المنسدلة مع الرقم المعروض (تجنب تكرار pageSize من فورم آخر)
            var pageSizeQuery = Request.Query["pageSize"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageSizeQuery) && int.TryParse(pageSizeQuery, out var psVal))
                pageSize = psVal;
            if (print) { pageSize = 0; page = 1; } // وضع الطباعة: كل الأصناف المفلترة، صفحة واحدة

            // عند تكرار searchMode في الـ Query (نماذج متعددة): نطبّع كل القيم؛ إن تعارضت نفضّل «contains» لأنها الافتراضية في الواجهة
            var smRaw = Request.Query["searchMode"];
            if (smRaw.Count > 0)
            {
                static string NormSm(string? v)
                {
                    if (string.IsNullOrWhiteSpace(v)) return "";
                    var t = v.Trim().ToLowerInvariant();
                    if (t == "startswith") t = "starts";
                    if (t == "eq" || t == "equals") t = "contains";
                    if (t != "contains" && t != "starts" && t != "ends") t = "contains";
                    return t;
                }
                if (smRaw.Count == 1 && !string.IsNullOrEmpty(smRaw[0]))
                    searchMode = smRaw[0];
                else
                {
                    var norms = smRaw.Select(NormSm).Where(x => x.Length > 0).ToList();
                    if (norms.Count == 0) { }
                    else if (norms.Distinct().Count() == 1)
                        searchMode = norms[0];
                    else if (norms.Contains("contains"))
                        searchMode = "contains";
                    else
                        searchMode = norms[^1];
                }
            }

            // =========================================================
            // (1) الاستعلام الأساسي (بدون تتبّع لتحسين الأداء)
            //      + تحميل مجموعة الصنف ومجموعة البونص لعرض أسمائهما في الجدول
            // =========================================================
            IQueryable<Product> q = _db.Products
                .Include(p => p.ProductGroup)        // متغير: تحميل مجموعة الصنف
                .Include(p => p.ProductBonusGroup)   // متغير: تحميل مجموعة البونص
                .Include(p => p.Classification)      // متغير: تحميل التصنيف (عادي، ثلاجة، …)
                .Include(p => p.Warehouse)           // متغير: تحميل المخزن للعرض في القائمة
                .AsNoTracking();

            // =========================================================
            // (2) تنظيف قيم الفلاتر
            // =========================================================
            var s = (search ?? string.Empty).Trim();                        // متغير: نص البحث بعد التنظيف
            var sb = (searchBy ?? "all").Trim().ToLowerInvariant();         // متغير: عمود البحث
            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();  // متغير: طريقة البحث
            var so = (sort ?? "name").Trim().ToLowerInvariant();            // متغير: عمود الترتيب
            var desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase); // متغير: هل الترتيب تنازلي؟

            // ضبط page/pageSize (0 = عرض الكل، والقيم المسموحة: 10، 25، 50، 100، 200)
            page = page <= 0 ? 1 : page;
            if (pageSize < 0) pageSize = 10;
            if (pageSize > 0 && pageSize != 10 && pageSize != 25 && pageSize != 50 && pageSize != 100 && pageSize != 200)
                pageSize = 10;

            // توحيد searchMode — «يساوي» أُزيل من الواجهة؛ روابط قديمة تُعامل كـ «يحتوي»
            if (sm == "startswith") sm = "starts";
            if (sm == "eq" || sm == "equals") sm = "contains";
            if (sm != "contains" && sm != "starts" && sm != "ends")
                sm = "contains";

            bool modeStarts = sm == "starts";
            bool modeEquals = false;
            bool modeEnds = sm == "ends";

            // =========================================================
            // (3) تطبيق البحث (يدعم: يحتوي/يبدأ بـ/ينتهي بـ)
            // =========================================================
            if (!string.IsNullOrWhiteSpace(s))
            {
                bool isInt = int.TryParse(s, out int intVal);        // متغير: هل النص رقم صحيح؟
                bool isDec = decimal.TryParse(s, out decimal decVal);// متغير: هل النص رقم عشري؟

                string likeContains = $"%{s}%";   // متغير: LIKE يحتوي
                string likeStarts = $"{s}%";      // متغير: LIKE يبدأ بـ
                string likeEnds = $"%{s}";        // متغير: LIKE ينتهي بـ

                switch (sb)
                {
                    case "id":
                        if (isInt)
                            q = q.Where(p => p.ProdId == intVal);
                        else if (modeEnds)
                            q = q.Where(p => p.ProdId.ToString().EndsWith(s));
                        else
                            q = q.Where(p => p.ProdId.ToString().Contains(s));
                        break;

                    case "code":
                        if (modeEquals)
                            q = q.Where(p => p.Barcode != null && p.Barcode == s);
                        else if (modeStarts)
                            q = q.Where(p => p.Barcode != null && EF.Functions.Like(p.Barcode, likeStarts));
                        else if (modeEnds)
                            q = q.Where(p => p.Barcode != null && EF.Functions.Like(p.Barcode, likeEnds));
                        else
                            q = q.Where(p => p.Barcode != null && EF.Functions.Like(p.Barcode, likeContains));
                        break;

                    case "name":
                        if (modeEquals)
                            q = q.Where(p => p.ProdName != null && p.ProdName == s);
                        else if (modeStarts)
                            q = q.Where(p => p.ProdName != null && EF.Functions.Like(p.ProdName, likeStarts));
                        else if (modeEnds)
                            q = q.Where(p => p.ProdName != null && EF.Functions.Like(p.ProdName, likeEnds));
                        else
                            q = q.Where(p => p.ProdName != null && EF.Functions.Like(p.ProdName, likeContains));
                        break;

                    case "gen":
                        if (modeEquals)
                            q = q.Where(p => p.GenericName != null && p.GenericName == s);
                        else if (modeStarts)
                            q = q.Where(p => p.GenericName != null && EF.Functions.Like(p.GenericName, likeStarts));
                        else if (modeEnds)
                            q = q.Where(p => p.GenericName != null && EF.Functions.Like(p.GenericName, likeEnds));
                        else
                            q = q.Where(p => p.GenericName != null && EF.Functions.Like(p.GenericName, likeContains));
                        break;

                    case "category":
                    case "productgroup":
                        if (modeEquals)
                            q = q.Where(p => p.ProductGroup != null && p.ProductGroup.Name == s);
                        else if (modeStarts)
                            q = q.Where(p => p.ProductGroup != null && p.ProductGroup.Name != null && EF.Functions.Like(p.ProductGroup.Name, likeStarts));
                        else if (modeEnds)
                            q = q.Where(p => p.ProductGroup != null && p.ProductGroup.Name != null && EF.Functions.Like(p.ProductGroup.Name, likeEnds));
                        else
                            q = q.Where(p => p.ProductGroup != null && p.ProductGroup.Name != null && EF.Functions.Like(p.ProductGroup.Name, likeContains));
                        break;

                    case "bonusgroup":
                        if (modeEquals)
                            q = q.Where(p => p.ProductBonusGroup != null && p.ProductBonusGroup.Name == s);
                        else if (modeStarts)
                            q = q.Where(p => p.ProductBonusGroup != null && p.ProductBonusGroup.Name != null && EF.Functions.Like(p.ProductBonusGroup.Name, likeStarts));
                        else if (modeEnds)
                            q = q.Where(p => p.ProductBonusGroup != null && p.ProductBonusGroup.Name != null && EF.Functions.Like(p.ProductBonusGroup.Name, likeEnds));
                        else
                            q = q.Where(p => p.ProductBonusGroup != null && p.ProductBonusGroup.Name != null && EF.Functions.Like(p.ProductBonusGroup.Name, likeContains));
                        break;

                    case "comp":
                        if (modeEquals)
                            q = q.Where(p => p.Company != null && p.Company == s);
                        else if (modeStarts)
                            q = q.Where(p => p.Company != null && EF.Functions.Like(p.Company, likeStarts));
                        else if (modeEnds)
                            q = q.Where(p => p.Company != null && EF.Functions.Like(p.Company, likeEnds));
                        else
                            q = q.Where(p => p.Company != null && EF.Functions.Like(p.Company, likeContains));
                        break;

                    case "price":
                        if (isDec)
                            q = q.Where(p => p.PriceRetail == decVal);
                        else if (modeEnds)
                            q = q.Where(p => p.PriceRetail.ToString().EndsWith(s));
                        else
                            q = q.Where(p => p.PriceRetail.ToString().Contains(s));
                        break;

                    case "active":
                        if (s == "1" || s == "true" || s == "نعم") q = q.Where(p => p.IsActive);
                        else if (s == "0" || s == "false" || s == "لا") q = q.Where(p => !p.IsActive);
                        break;
                    case "imported":
                        {
                            // =========================
                            // فلتر "محلي/مستورد"
                            // - Product.Imported نوعه string? وليس bool
                            // - نقبل إدخال المستخدم: (1/true/نعم) = مستورد
                            // - و (0/false/لا) = محلي
                            // - أو يكتب النص مباشرة: "محلي" / "مستورد"
                            // =========================

                            var val = (s ?? "").Trim().ToLower(); // متغير: قيمة البحث بعد تنظيفها

                            // لو المستخدم كتب "1" أو "true" أو "نعم" => نعتبره "مستورد"
                            if (val == "1" || val == "true" || val == "نعم" || val == "yes")
                            {
                                q = q.Where(p => (p.Imported ?? "").Contains("مستورد"));
                            }
                            // لو المستخدم كتب "0" أو "false" أو "لا" => نعتبره "محلي"
                            else if (val == "0" || val == "false" || val == "لا" || val == "no")
                            {
                                q = q.Where(p => (p.Imported ?? "").Contains("محلي"));
                            }
                            else
                            {
                                // لو المستخدم كتب نص مباشر (مثلاً: محلي / مستورد / imported / local)
                                // نفلتر بـ Contains
                                q = q.Where(p => (p.Imported ?? "").ToLower().Contains(val));
                            }

                            break;
                        }


                    case "updated":
                        if (modeEnds)
                            q = q.Where(p => p.UpdatedAt.ToString().EndsWith(s));
                        else
                            q = q.Where(p => p.UpdatedAt.ToString().Contains(s));
                        break;

                    default:
                        // all: بحث عام في الأعمدة المهمة
                        if (modeEquals)
                        {
                            q = q.Where(p =>
                                (isInt && p.ProdId == intVal) ||
                                (p.Barcode != null && p.Barcode == s) ||
                                (p.ProdName != null && p.ProdName == s) ||
                                (p.GenericName != null && p.GenericName == s) ||
                                (p.Company != null && p.Company == s) ||
                                (p.ProductGroup != null && p.ProductGroup.Name != null && p.ProductGroup.Name == s) ||
                                (p.ProductBonusGroup != null && p.ProductBonusGroup.Name != null && p.ProductBonusGroup.Name == s) ||
                                (isDec && p.PriceRetail == decVal)
                            );
                        }
                        else if (modeStarts)
                        {
                            q = q.Where(p =>
                                p.ProdId.ToString().StartsWith(s) ||
                                (p.Barcode != null && EF.Functions.Like(p.Barcode, likeStarts)) ||
                                (p.ProdName != null && EF.Functions.Like(p.ProdName, likeStarts)) ||
                                (p.GenericName != null && EF.Functions.Like(p.GenericName, likeStarts)) ||
                                (p.Company != null && EF.Functions.Like(p.Company, likeStarts)) ||
                                (p.ProductGroup != null && p.ProductGroup.Name != null && EF.Functions.Like(p.ProductGroup.Name, likeStarts)) ||
                                (p.ProductBonusGroup != null && p.ProductBonusGroup.Name != null && EF.Functions.Like(p.ProductBonusGroup.Name, likeStarts)) ||
                                p.PriceRetail.ToString().StartsWith(s)
                            );
                        }
                        else if (modeEnds)
                        {
                            q = q.Where(p =>
                                (!isInt && p.ProdId.ToString().EndsWith(s)) ||
                                (isInt && p.ProdId == intVal) ||
                                (p.Barcode != null && EF.Functions.Like(p.Barcode, likeEnds)) ||
                                (p.ProdName != null && EF.Functions.Like(p.ProdName, likeEnds)) ||
                                (p.GenericName != null && EF.Functions.Like(p.GenericName, likeEnds)) ||
                                (p.Company != null && EF.Functions.Like(p.Company, likeEnds)) ||
                                (p.ProductGroup != null && p.ProductGroup.Name != null && EF.Functions.Like(p.ProductGroup.Name, likeEnds)) ||
                                (p.ProductBonusGroup != null && p.ProductBonusGroup.Name != null && EF.Functions.Like(p.ProductBonusGroup.Name, likeEnds)) ||
                                p.PriceRetail.ToString().EndsWith(s)
                            );
                        }
                        else
                        {
                            q = q.Where(p =>
                                p.ProdId.ToString().Contains(s) ||
                                (p.Barcode != null && EF.Functions.Like(p.Barcode, likeContains)) ||
                                (p.ProdName != null && EF.Functions.Like(p.ProdName, likeContains)) ||
                                (p.GenericName != null && EF.Functions.Like(p.GenericName, likeContains)) ||
                                (p.Company != null && EF.Functions.Like(p.Company, likeContains)) ||
                                (p.ProductGroup != null && p.ProductGroup.Name != null && EF.Functions.Like(p.ProductGroup.Name, likeContains)) ||
                                (p.ProductBonusGroup != null && p.ProductBonusGroup.Name != null && EF.Functions.Like(p.ProductBonusGroup.Name, likeContains)) ||
                                p.PriceRetail.ToString().Contains(s)
                            );
                        }
                        break;
                }
            }

            // =========================================================
            // (4) ✅ فلتر الأرقام (من/إلى) — يدعم id/price حالياً
            // =========================================================
            var nf = (numField ?? "").Trim().ToLowerInvariant(); // متغير: حقل الرقم

            // توافق مع codeFrom/codeTo من الواجهة
            if (!fromNum.HasValue && codeFrom.HasValue) { fromNum = codeFrom.Value; nf = string.IsNullOrEmpty(nf) ? "id" : nf; }
            if (!toNum.HasValue && codeTo.HasValue) { toNum = codeTo.Value; nf = string.IsNullOrEmpty(nf) ? "id" : nf; }

            // لو من > إلى نبدّلهم
            if (fromNum.HasValue && toNum.HasValue && fromNum.Value > toNum.Value)
            {
                var tmp = fromNum;
                fromNum = toNum;
                toNum = tmp;
            }

            if (nf == "id")
            {
                if (fromNum.HasValue) q = q.Where(p => p.ProdId >= (int)fromNum.Value);
                if (toNum.HasValue) q = q.Where(p => p.ProdId <= (int)toNum.Value);
            }
            else if (nf == "price")
            {
                if (fromNum.HasValue) q = q.Where(p => p.PriceRetail >= fromNum.Value);
                if (toNum.HasValue) q = q.Where(p => p.PriceRetail <= toNum.Value);
            }

            // =========================================================
            // (5) ✅ فلاتر متراكبة (Layered Filters) — تُطبَّق بنظام AND (كل الفلاتر معاً)
            // =========================================================
            
            // فلتر حسب مجموعة الصنف (ProductGroup)
            if (filterProductGroupId.HasValue && filterProductGroupId.Value > 0)
            {
                q = q.Where(p => p.ProductGroupId == filterProductGroupId.Value);
            }

            // فلتر حسب الشركة (Company)
            if (!string.IsNullOrWhiteSpace(filterCompany))
            {
                var companyFilter = filterCompany.Trim();
                q = q.Where(p => p.Company != null && p.Company.Contains(companyFilter));
            }

            // فلتر حسب المنشأ (Imported: محلي/مستورد)
            if (!string.IsNullOrWhiteSpace(filterImported))
            {
                var importedFilter = filterImported.Trim().ToLower();
                if (importedFilter == "مستورد" || importedFilter == "imported")
                {
                    q = q.Where(p => (p.Imported ?? "").Contains("مستورد"));
                }
                else if (importedFilter == "محلي" || importedFilter == "local")
                {
                    q = q.Where(p => (p.Imported ?? "").Contains("محلي"));
                }
            }

            // =========================================================
            // (5b) فلاتر أعمدة بنمط Excel (قيم متعددة مفصولة بـ |) — تُضاف أيضاً بنظام AND
            // =========================================================
            var sep = new[] { '|' };
            if (!string.IsNullOrWhiteSpace(filterCol_productGroup))
            {
                var ids = filterCol_productGroup.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToList();
                if (ids.Count > 0)
                    q = q.Where(p => p.ProductGroupId != null && ids.Contains(p.ProductGroupId.Value));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_bonusGroup))
            {
                var ids = filterCol_bonusGroup.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToList();
                if (ids.Count > 0)
                    q = q.Where(p => p.ProductBonusGroupId != null && ids.Contains(p.ProductBonusGroupId.Value));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_company))
            {
                var vals = filterCol_company.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(p => p.Company != null && vals.Contains(p.Company));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_location))
            {
                var vals = filterCol_location.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(p => p.Location != null && vals.Contains(p.Location));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_warehouse))
            {
                var ids = filterCol_warehouse.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x) && int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    .Select(x => int.Parse(x, CultureInfo.InvariantCulture))
                    .ToList();
                if (ids.Count > 0)
                    q = q.Where(p => p.WarehouseId != null && ids.Contains(p.WarehouseId.Value));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_imported))
            {
                var vals = filterCol_imported.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(p => p.Imported != null && vals.Contains(p.Imported));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_isactive))
            {
                var vals = filterCol_isactive.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();
                if (vals.Count > 0)
                {
                    var activeVals = new List<bool>();
                    if (vals.Contains("نشط")) activeVals.Add(true);
                    if (vals.Contains("موقوف")) activeVals.Add(false);
                    if (activeVals.Count > 0)
                        q = q.Where(p => activeVals.Contains(p.IsActive));
                }
            }

            // فلاتر باقي الأعمدة النصية (كود الصنف: من filterCol_id أو رقم واحد من filterCol_idExpr)
            var filterByIds = new List<int>();
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                filterByIds = filterCol_id.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToList();
            }
            if (filterByIds.Count == 0 && !string.IsNullOrWhiteSpace(filterCol_idExpr))
            {
                var expr = filterCol_idExpr.Trim();
                if (int.TryParse(expr, out var singleId))
                    filterByIds.Add(singleId);
            }
            if (filterByIds.Count > 0)
                q = q.Where(p => filterByIds.Contains(p.ProdId));
            // أولوية فلتر القيم المحددة (تشيك بوكسات لوحة العمود) على "يحتوي" — عند اختيار صنف واحد أو أكثر نطبّق مطابقة تامة
            if (!string.IsNullOrWhiteSpace(filterCol_name))
            {
                var vals = filterCol_name.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(p => p.ProdName != null && vals.Contains(p.ProdName));
            }
            else if (!string.IsNullOrWhiteSpace(filterCol_name_contains))
            {
                var term = filterCol_name_contains.Trim();
                q = q.Where(p => p.ProdName != null && EF.Functions.Like(p.ProdName, "%" + term + "%"));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_barcode))
            {
                var vals = filterCol_barcode.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(p => p.Barcode != null && vals.Contains(p.Barcode));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_generic))
            {
                var vals = filterCol_generic.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(p => p.GenericName != null && vals.Contains(p.GenericName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_category))
            {
                var ids = filterCol_category.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToList();
                if (ids.Count > 0)
                    q = q.Where(p => p.CategoryId != null && ids.Contains(p.CategoryId.Value));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_classification))
            {
                var ids = filterCol_classification.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToList();
                if (ids.Count > 0)
                    q = q.Where(p => p.ClassificationId != null && ids.Contains(p.ClassificationId.Value));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_price))
            {
                var prices = filterCol_price.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToList();
                if (prices.Count > 0)
                    q = q.Where(p => prices.Contains(p.PriceRetail));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_hasQuota))
            {
                var vals = filterCol_hasQuota.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();
                if (vals.Count > 0)
                {
                    var quotaVals = new List<bool>();
                    if (vals.Contains("نعم")) quotaVals.Add(true);
                    if (vals.Contains("لا")) quotaVals.Add(false);
                    if (quotaVals.Count > 0)
                        q = q.Where(p => quotaVals.Contains(p.HasQuota));
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_quotaQuantity))
            {
                var qty = filterCol_quotaQuantity.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToList();
                if (qty.Count > 0)
                    q = q.Where(p => p.QuotaQuantity != null && qty.Contains(p.QuotaQuantity.Value));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_cartonQuantity))
            {
                var qty = filterCol_cartonQuantity.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToList();
                if (qty.Count > 0)
                    q = q.Where(p => p.CartonQuantity != null && qty.Contains(p.CartonQuantity.Value));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_packQuantity))
            {
                var qty = filterCol_packQuantity.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToList();
                if (qty.Count > 0)
                    q = q.Where(p => p.PackQuantity != null && qty.Contains(p.PackQuantity.Value));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_descShort))
            {
                var vals = filterCol_descShort.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();
                if (vals.Count > 0)
                    q = q.Where(p => p.Description != null && vals.Any(v => p.Description.Contains(v)));
            }

            // فلاتر التاريخ (نطاق سنة/شهر)
            if (!string.IsNullOrWhiteSpace(filterCol_created))
            {
                var dateParts = filterCol_created.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();
                if (dateParts.Count > 0)
                {
                    var dateFilters = dateParts.Select(dp =>
                    {
                        var parts = dp.Split('-');
                        if (parts.Length == 2 && int.TryParse(parts[0], out var y) && int.TryParse(parts[1], out var m))
                            return new { Year = y, Month = m };
                        return null;
                    }).Where(x => x != null).ToList();
                    if (dateFilters.Count > 0)
                    {
                        q = q.Where(p => dateFilters.Any(df =>
                            p.CreatedAt.Year == df.Year && p.CreatedAt.Month == df.Month));
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_modified))
            {
                var dateParts = filterCol_modified.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();
                if (dateParts.Count > 0)
                {
                    var dateFilters = dateParts.Select(dp =>
                    {
                        var parts = dp.Split('-');
                        if (parts.Length == 2 && int.TryParse(parts[0], out var y) && int.TryParse(parts[1], out var m))
                            return new { Year = y, Month = m };
                        return null;
                    }).Where(x => x != null).ToList();
                    if (dateFilters.Count > 0)
                    {
                        q = q.Where(p => dateFilters.Any(df =>
                            p.UpdatedAt.Year == df.Year && p.UpdatedAt.Month == df.Month));
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_lastprice))
            {
                var dateParts = filterCol_lastprice.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();
                if (dateParts.Count > 0)
                {
                    var dateFilters = dateParts.Select(dp =>
                    {
                        var parts = dp.Split('-');
                        if (parts.Length == 2 && int.TryParse(parts[0], out var y) && int.TryParse(parts[1], out var m))
                            return new { Year = y, Month = m };
                        return null;
                    }).Where(x => x != null).ToList();
                    if (dateFilters.Count > 0)
                    {
                        q = q.Where(p => p.LastPriceChangeDate.HasValue && dateFilters.Any(df =>
                            p.LastPriceChangeDate!.Value.Year == df.Year && p.LastPriceChangeDate.Value.Month == df.Month));
                    }
                }
            }

            // =========================================================
            // (5c) فلاتر رقمية متقدمة (صيغ < > من:إلى)
            // =========================================================
            if (!string.IsNullOrWhiteSpace(filterCol_idExpr))
            {
                var expr = filterCol_idExpr.Trim();
                if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.Substring(2), out var max))
                {
                    q = q.Where(p => p.ProdId <= max);
                }
                else if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.Substring(2), out var min))
                {
                    q = q.Where(p => p.ProdId >= min);
                }
                else if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.Substring(1), out var max2))
                {
                    q = q.Where(p => p.ProdId < max2);
                }
                else if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.Substring(1), out var min2))
                {
                    q = q.Where(p => p.ProdId > min2);
                }
                else if ((expr.Contains(':') || expr.Contains('-')) && !expr.StartsWith("-"))
                {
                    var separator = expr.Contains(':') ? ':' : '-';
                    var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2 &&
                        int.TryParse(parts[0].Trim(), out var from) &&
                        int.TryParse(parts[1].Trim(), out var to))
                    {
                        if (from > to) (from, to) = (to, from);
                        q = q.Where(p => p.ProdId >= from && p.ProdId <= to);
                    }
                }
                else if (int.TryParse(expr, out var exactId))
                {
                    // رقم واحد فقط = بحث عن كود صنف مطابق (مثل 64444)
                    q = q.Where(p => p.ProdId == exactId);
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_priceExpr))
            {
                var expr = filterCol_priceExpr.Trim();
                if (expr.StartsWith("<=") && decimal.TryParse(expr.Substring(2), out var max))
                {
                    q = q.Where(p => p.PriceRetail <= max);
                }
                else if (expr.StartsWith(">=") && decimal.TryParse(expr.Substring(2), out var min))
                {
                    q = q.Where(p => p.PriceRetail >= min);
                }
                else if (expr.StartsWith("<") && decimal.TryParse(expr.Substring(1), out var max2))
                {
                    q = q.Where(p => p.PriceRetail < max2);
                }
                else if (expr.StartsWith(">") && decimal.TryParse(expr.Substring(1), out var min2))
                {
                    q = q.Where(p => p.PriceRetail > min2);
                }
                else if ((expr.Contains(':') || expr.Contains('-')) && !expr.StartsWith("-"))
                {
                    var separator = expr.Contains(':') ? ':' : '-';
                    var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2 &&
                        decimal.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var from) &&
                        decimal.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var to))
                    {
                        if (from > to) (from, to) = (to, from);
                        q = q.Where(p => p.PriceRetail >= from && p.PriceRetail <= to);
                    }
                }
                else if (decimal.TryParse(expr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var exactPrice))
                {
                    // رقم واحد = سعر يساوي بالضبط (مثل 100 أو 99.50)
                    q = q.Where(p => p.PriceRetail == exactPrice);
                }
            }

            // =========================================================
            // (6) فلتر التاريخ/الوقت (CreatedAt)
            // =========================================================
            if (useDateRange)
            {
                if (fromDate.HasValue) q = q.Where(p => p.CreatedAt >= fromDate.Value);
                if (toDate.HasValue) q = q.Where(p => p.CreatedAt <= toDate.Value);
            }

            // =========================================================
            // (7) الترتيب
            // =========================================================
            q = so switch
            {
                "id" => desc ? q.OrderByDescending(p => p.ProdId) : q.OrderBy(p => p.ProdId),
                "code" => desc ? q.OrderByDescending(p => p.Barcode) : q.OrderBy(p => p.Barcode),
                "name" => desc ? q.OrderByDescending(p => p.ProdName) : q.OrderBy(p => p.ProdName),
                "price" => desc ? q.OrderByDescending(p => p.PriceRetail) : q.OrderBy(p => p.PriceRetail),
                "category" => desc ? q.OrderByDescending(p => p.ProductGroup!.Name) : q.OrderBy(p => p.ProductGroup!.Name),
                "classification" => desc ? q.OrderByDescending(p => p.Classification != null ? (p.Classification.Name ?? "") : "") : q.OrderBy(p => p.Classification != null ? (p.Classification.Name ?? "") : ""),
                "productgroup" => desc ? q.OrderByDescending(p => p.ProductGroup != null ? (p.ProductGroup.Name ?? "") : "") : q.OrderBy(p => p.ProductGroup != null ? (p.ProductGroup.Name ?? "") : ""),
                "bonusgroup" => desc ? q.OrderByDescending(p => p.ProductBonusGroup != null ? (p.ProductBonusGroup.Name ?? "") : "") : q.OrderBy(p => p.ProductBonusGroup != null ? (p.ProductBonusGroup.Name ?? "") : ""),
                "comp" => desc ? q.OrderByDescending(p => p.Company) : q.OrderBy(p => p.Company),
                "location" => desc ? q.OrderByDescending(p => p.Location) : q.OrderBy(p => p.Location),
                "created" => desc ? q.OrderByDescending(p => p.CreatedAt) : q.OrderBy(p => p.CreatedAt),
                "updated" or "modified" => desc ? q.OrderByDescending(p => p.UpdatedAt) : q.OrderBy(p => p.UpdatedAt),
                "imported" => desc ? q.OrderByDescending(p => p.Imported) : q.OrderBy(p => p.Imported),
                "active" or "isactive" => desc ? q.OrderByDescending(p => p.IsActive) : q.OrderBy(p => p.IsActive),
                "hasquota" => desc ? q.OrderByDescending(p => p.HasQuota) : q.OrderBy(p => p.HasQuota),
                "quotaquantity" => desc ? q.OrderByDescending(p => p.QuotaQuantity ?? 0) : q.OrderBy(p => p.QuotaQuantity ?? 0),
                "cartonquantity" => desc ? q.OrderByDescending(p => p.CartonQuantity ?? 0) : q.OrderBy(p => p.CartonQuantity ?? 0),
                "packquantity" => desc ? q.OrderByDescending(p => p.PackQuantity ?? 0) : q.OrderBy(p => p.PackQuantity ?? 0),
                "lastprice" => desc ? q.OrderByDescending(p => p.LastPriceChangeDate) : q.OrderBy(p => p.LastPriceChangeDate),
                "descshort" => desc ? q.OrderByDescending(p => p.Description) : q.OrderBy(p => p.Description),
                _ => desc ? q.OrderByDescending(p => p.ProdName) : q.OrderBy(p => p.ProdName)
            };

            // =========================================================
            // (7) الترقيم (Paging) — دعم pageSize=0 لعرض «الكل»
            // =========================================================
            int total = await q.CountAsync(); // متغير: إجمالي النتائج بعد الفلاتر

            int effectivePageSize = pageSize;
            if (pageSize == 0)
            {
                effectivePageSize = total == 0 ? 10 : Math.Min(total, 100_000); // حد أقصى للذاكرة
                page = 1;
            }

            var items = await q
                .Skip((page - 1) * effectivePageSize)
                .Take(effectivePageSize)
                .ToListAsync();

            // =========================================================
            // (8) تجهيز PagedResult + تثبيت حالة البحث/الترتيب/التاريخ (نمرّر pageSize الأصلي لظهور «الكل» في الواجهة)
            // =========================================================
            var model = new PagedResult<Product>(items, page, pageSize, total)
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
            // (9) ViewBag: تثبيت قيم الواجهة
            // =========================================================
            ViewBag.PageSize = pageSize; // لضمان تطابق القائمة المنسدلة «حجم الصفحة» مع الرقم المعروض
            // إجمالي الأصناف بدون أي فلتر (للعرض في كارت الإجمالي)
            ViewBag.TotalProductsUnfiltered = await _db.Products.CountAsync();
            ViewBag.SearchMode = sm;
            ViewBag.SearchBy = sb;
            ViewBag.NumField = nf;
            ViewBag.FromNum = fromNum;
            ViewBag.ToNum = toNum;
            ViewBag.CodeFrom = fromNum.HasValue && nf == "id" ? (int)fromNum.Value : (int?)null;
            ViewBag.CodeTo = toNum.HasValue && nf == "id" ? (int)toNum.Value : (int?)null;

            // ✅ فلاتر متراكبة: تحميل قوائم للـ dropdowns
            var productGroups = await _db.ProductGroups
                .AsNoTracking()
                .Where(pg => pg.IsActive)
                .OrderBy(pg => pg.Name)
                .Select(pg => new { pg.ProductGroupId, pg.Name })
                .ToListAsync();

            ViewBag.ProductGroups = new SelectList(productGroups, "ProductGroupId", "Name", filterProductGroupId);

            // فلاتر أعمدة Excel
            ViewBag.FilterCol_ProductGroup = filterCol_productGroup;
            ViewBag.FilterCol_BonusGroup = filterCol_bonusGroup;
            ViewBag.FilterCol_Company = filterCol_company;
            ViewBag.FilterCol_Location = filterCol_location;
            ViewBag.FilterCol_Warehouse = filterCol_warehouse;
            ViewBag.FilterCol_Imported = filterCol_imported;
            ViewBag.FilterCol_IsActive = filterCol_isactive;
            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_Name = filterCol_name;
            ViewBag.FilterCol_NameContains = filterCol_name_contains;
            ViewBag.FilterCol_Barcode = filterCol_barcode;
            ViewBag.FilterCol_Generic = filterCol_generic;
            ViewBag.FilterCol_Category = filterCol_category;
            ViewBag.FilterCol_Classification = filterCol_classification;
            ViewBag.FilterCol_Price = filterCol_price;
            ViewBag.FilterCol_Created = filterCol_created;
            ViewBag.FilterCol_Modified = filterCol_modified;
            ViewBag.FilterCol_LastPrice = filterCol_lastprice;
            ViewBag.FilterCol_HasQuota = filterCol_hasQuota;
            ViewBag.FilterCol_QuotaQuantity = filterCol_quotaQuantity;
            ViewBag.FilterCol_CartonQuantity = filterCol_cartonQuantity;
            ViewBag.FilterCol_PackQuantity = filterCol_packQuantity;
            ViewBag.FilterCol_DescShort = filterCol_descShort;
            ViewBag.FilterCol_IdExpr = filterCol_idExpr;
            ViewBag.FilterCol_PriceExpr = filterCol_priceExpr;

            var companies = await _db.Products
                .AsNoTracking()
                .Where(p => !string.IsNullOrWhiteSpace(p.Company))
                .Select(p => p.Company!)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();

            ViewBag.Companies = companies;
            ViewBag.FilterProductGroupId = filterProductGroupId;
            ViewBag.FilterCompany = filterCompany;
            ViewBag.FilterImported = filterImported;

            ViewBag.SearchByOptions = new List<SelectListItem>
            {
                new SelectListItem("الكل", "all"),
                new SelectListItem("كود الصنف (ID)", "id"),
                new SelectListItem("باركود", "code"),
                new SelectListItem("اسم الصنف", "name"),
                new SelectListItem("المادة الفعالة", "gen"),
                new SelectListItem("المجموعة", "category"),
                new SelectListItem("الشركة", "comp"),
                new SelectListItem("سعر الجمهور", "price"),
                new SelectListItem("نشط؟", "active"),
                new SelectListItem("مستورَد؟", "imported"),
                new SelectListItem("تاريخ التعديل", "updated")
            };

            ViewBag.SortOptions = new List<SelectListItem>
            {
                new SelectListItem("الاسم", "name"),
                new SelectListItem("الكود", "id"),
                new SelectListItem("الباركود", "code"),
                new SelectListItem("السعر", "price"),
                new SelectListItem("المجموعة", "category"),
                new SelectListItem("الشركة", "comp"),
                new SelectListItem("تاريخ الإنشاء", "created"),
                new SelectListItem("آخر تعديل", "updated"),
                new SelectListItem("نشط؟", "active"),
                new SelectListItem("مستورَد؟", "imported")
            };

            ViewBag.PageSizeOptions = new List<SelectListItem>
            {
                new SelectListItem("25", "25"),
                new SelectListItem("50", "50"),
                new SelectListItem("100", "100"),
                new SelectListItem("200", "200")
            };

            ViewBag.CanCreate = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Products", "Create"));
            ViewBag.CanEdit = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Products", "Edit"));
            ViewBag.CanShow = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Products", "Show"));
            ViewBag.CanDelete = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Products", "Delete"));
            ViewBag.CanExport = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Products", "Export"));

            if (print)
            {
                ViewBag.Print = true;
                return View("Index", model); // نفس شكل جدول القائمة مع كل البيانات المفلترة
            }
            return View(model);
        }

        // =========================================================
        // API: جلب القيم المميزة لعمود (للفلترة بنمط Excel)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var q = _db.Products
                .Include(p => p.ProductGroup)
                .Include(p => p.ProductBonusGroup)
                .Include(p => p.Warehouse)
                .AsNoTracking();

            List<(string Value, string Display)> items = column?.ToLowerInvariant() switch
            {
                // أعمدة رقمية (ID)
                "id" => (await q
                    .Select(p => p.ProdId)
                    .Distinct()
                    .OrderBy(v => v)
                    .Take(500) // حد أقصى 500 قيمة لتجنب التحميل البطيء
                    .ToListAsync())
                    .Select(v => (v.ToString(), v.ToString()))
                    .ToList(),
                
                // أعمدة نصية — عند وجود نص بحث نفلتر في DB أولاً حتى تظهر كل النتائج (مثل ديمرا)
                "name" => string.IsNullOrEmpty(searchTerm)
                    ? (await q.Where(p => !string.IsNullOrWhiteSpace(p.ProdName)).Select(p => p.ProdName!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v, v)).ToList()
                    : (await q.Where(p => p.ProdName != null && EF.Functions.Like(p.ProdName, "%" + searchTerm + "%")).Select(p => p.ProdName!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v!, v)).ToList(),
                "barcode" => string.IsNullOrEmpty(searchTerm)
                    ? (await q.Where(p => !string.IsNullOrWhiteSpace(p.Barcode)).Select(p => p.Barcode!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v, v)).ToList()
                    : (await q.Where(p => p.Barcode != null && EF.Functions.Like(p.Barcode, "%" + searchTerm + "%")).Select(p => p.Barcode!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v!, v)).ToList(),
                "generic" => string.IsNullOrEmpty(searchTerm)
                    ? (await q.Where(p => !string.IsNullOrWhiteSpace(p.GenericName)).Select(p => p.GenericName!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v, v)).ToList()
                    : (await q.Where(p => p.GenericName != null && EF.Functions.Like(p.GenericName, "%" + searchTerm + "%")).Select(p => p.GenericName!).Distinct().OrderBy(v => v).Take(500).ToListAsync()).Select(v => (v!, v)).ToList(),
                "category" => (await q
                    .Where(p => p.CategoryId.HasValue)
                    .Select(p => p.CategoryId!.Value)
                    .Distinct()
                    .OrderBy(v => v)
                    .Take(500)
                    .ToListAsync())
                    .Select(v => (v.ToString(), v.ToString()))
                    .ToList(),
                "classification" => (await _db.ProductClassifications
                    .AsNoTracking()
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.Name)
                    .Select(c => new { c.Id, c.Name })
                    .ToListAsync())
                    .Select(c => (c.Id.ToString(), c.Name ?? c.Id.ToString()))
                    .ToList(),
                "productgroup" => (await q
                    .Where(p => p.ProductGroup != null)
                    .Select(p => new { p.ProductGroup!.ProductGroupId, p.ProductGroup.Name })
                    .Distinct()
                    .OrderBy(x => x.Name)
                    .ToListAsync())
                    .Select(x => (x.ProductGroupId.ToString(), x.Name ?? ""))
                    .ToList(),
                "bonusgroup" => (await q
                    .Where(p => p.ProductBonusGroup != null)
                    .Select(p => new { p.ProductBonusGroup!.ProductBonusGroupId, p.ProductBonusGroup.Name })
                    .Distinct()
                    .OrderBy(x => x.Name)
                    .ToListAsync())
                    .Select(x => (x.ProductBonusGroupId.ToString(), x.Name ?? ""))
                    .ToList(),
                "company" => string.IsNullOrEmpty(searchTerm)
                    ? (await q.Where(p => !string.IsNullOrWhiteSpace(p.Company)).Select(p => p.Company!).Distinct().OrderBy(c => c).Take(500).ToListAsync()).Select(c => (c, c)).ToList()
                    : (await q.Where(p => p.Company != null && EF.Functions.Like(p.Company, "%" + searchTerm + "%")).Select(p => p.Company!).Distinct().OrderBy(c => c).Take(500).ToListAsync()).Select(c => (c!, c)).ToList(),
                "location" => string.IsNullOrEmpty(searchTerm)
                    ? (await q.Where(p => !string.IsNullOrWhiteSpace(p.Location)).Select(p => p.Location!).Distinct().OrderBy(c => c).Take(500).ToListAsync()).Select(c => (c, c)).ToList()
                    : (await q.Where(p => p.Location != null && EF.Functions.Like(p.Location, "%" + searchTerm + "%")).Select(p => p.Location!).Distinct().OrderBy(c => c).Take(500).ToListAsync()).Select(c => (c!, c)).ToList(),
                "warehouse" => (await q
                    .Where(p => p.Warehouse != null)
                    .Select(p => new { p.Warehouse!.WarehouseId, p.Warehouse.WarehouseName })
                    .Distinct()
                    .OrderBy(x => x.WarehouseName)
                    .Take(500)
                    .ToListAsync())
                    .Select(x => (x.WarehouseId.ToString(), x.WarehouseName ?? "")).ToList(),

                // أعمدة رقمية (السعر)
                "price" => (await q
                    .Where(p => p.PriceRetail > 0)
                    .Select(p => p.PriceRetail)
                    .Distinct()
                    .OrderBy(v => v)
                    .Take(500)
                    .ToListAsync())
                    .Select(v => (v.ToString("N2"), v.ToString("N2")))
                    .ToList(),
                
                // أعمدة نصية محدودة القيم
                "imported" => (await q
                    .Where(p => !string.IsNullOrWhiteSpace(p.Imported))
                    .Select(p => p.Imported!)
                    .Distinct()
                    .OrderBy(i => i)
                    .ToListAsync())
                    .Select(i => (i, i))
                    .ToList(),
                "isactive" => new List<(string, string)>
                {
                    ("نشط", "نشط"),
                    ("موقوف", "موقوف")
                },
                "hasquota" => new List<(string, string)>
                {
                    ("نعم", "نعم"),
                    ("لا", "لا")
                },
                
                // أعمدة رقمية (الكمية)
                "quotaquantity" => (await q
                    .Where(p => p.QuotaQuantity.HasValue && p.QuotaQuantity.Value > 0)
                    .Select(p => p.QuotaQuantity!.Value)
                    .Distinct()
                    .OrderBy(v => v)
                    .Take(500)
                    .ToListAsync())
                    .Select(v => (v.ToString(), v.ToString()))
                    .ToList(),
                "cartonquantity" => (await q
                    .Where(p => p.CartonQuantity.HasValue && p.CartonQuantity.Value > 0)
                    .Select(p => p.CartonQuantity!.Value)
                    .Distinct()
                    .OrderBy(v => v)
                    .Take(500)
                    .ToListAsync())
                    .Select(v => (v.ToString(), v.ToString()))
                    .ToList(),
                "packquantity" => (await q
                    .Where(p => p.PackQuantity.HasValue && p.PackQuantity.Value > 0)
                    .Select(p => p.PackQuantity!.Value)
                    .Distinct()
                    .OrderBy(v => v)
                    .Take(500)
                    .ToListAsync())
                    .Select(v => (v.ToString(), v.ToString()))
                    .ToList(),
                
                // أعمدة التاريخ (نعرض فقط السنوات والأشهر المميزة)
                "created" => (await q
                    .Select(p => new { Year = p.CreatedAt.Year, Month = p.CreatedAt.Month })
                    .Distinct()
                    .OrderByDescending(x => x.Year)
                    .ThenByDescending(x => x.Month)
                    .Take(100)
                    .ToListAsync())
                    .Select(x => ($"{x.Year}-{x.Month:D2}", $"{x.Year}/{x.Month:D2}"))
                    .ToList(),
                "modified" => (await q
                    .Select(p => new { Year = p.UpdatedAt.Year, Month = p.UpdatedAt.Month })
                    .Distinct()
                    .OrderByDescending(x => x.Year)
                    .ThenByDescending(x => x.Month)
                    .Take(100)
                    .ToListAsync())
                    .Select(x => ($"{x.Year}-{x.Month:D2}", $"{x.Year}/{x.Month:D2}"))
                    .ToList(),
                "lastprice" => (await q
                    .Where(p => p.LastPriceChangeDate.HasValue)
                    .Select(p => new { Year = p.LastPriceChangeDate!.Value.Year, Month = p.LastPriceChangeDate.Value.Month })
                    .Distinct()
                    .OrderByDescending(x => x.Year)
                    .ThenByDescending(x => x.Month)
                    .Take(100)
                    .ToListAsync())
                    .Select(x => ($"{x.Year}-{x.Month:D2}", $"{x.Year}/{x.Month:D2}"))
                    .ToList(),
                
                // الوصف المختصر (نص)
                "descshort" => (await q
                    .Where(p => !string.IsNullOrWhiteSpace(p.Description))
                    .Select(p => p.Description!.Substring(0, Math.Min(50, p.Description.Length)))
                    .Distinct()
                    .OrderBy(v => v)
                    .Take(200)
                    .ToListAsync())
                    .Select(v => (v, v))
                    .ToList(),
                
                _ => new List<(string Value, string Display)>()
            };

            // تطبيق البحث داخل القائمة
            if (!string.IsNullOrEmpty(searchTerm))
            {
                items = items
                    .Where(x => x.Display.ToLowerInvariant().Contains(searchTerm))
                    .ToList();
            }

            return Json(items.Select(x => new { value = x.Value, display = x.Display }));
        }





        // =========================================================
        // 2) تفاصيل صنف واحد
        // =========================================================
        public async Task<IActionResult> Details(int id)   // يستقبل رقم الصنف (ProdId)
        {
            if (id <= 0) return NotFound();

            var product = await _db.Products.Include(p => p.Warehouse).FirstOrDefaultAsync(p => p.ProdId == id);
            if (product == null) return NotFound();

            return View(product);
        }







        // =========================================================
        // 3) إضافة صنف جديد
        // =========================================================
        [HttpGet]
        [RequirePermission("Products.Create")]
        public async Task<IActionResult> Create()
        {
            // تحميل قائمة الفئات + مجموعات الأصناف + مجموعات البونص + التصنيف + المخازن
            await LoadCategoriesDDLAsync(null);
            await LoadProductGroupsDDLAsync(null);
            await LoadBonusGroupsDDLAsync(null);
            await LoadClassificationDDLAsync(null);
            await LoadWarehousesDDLAsync();

            ViewBag.ImportedOptions = GetImportedOptions(null);

            // قيم افتراضية للصنف الجديد
            var m = new Product
            {
                IsActive = true,
                LastPriceChangeDate = DateTime.Now,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            return View(m);
        }







        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Products.Create")]
        public async Task<IActionResult> Create(Product model)
        {
            if (!ModelState.IsValid)
            {
                await LoadCategoriesDDLAsync(model.CategoryId);
                await LoadProductGroupsDDLAsync(model.ProductGroupId);
                await LoadBonusGroupsDDLAsync(model.ProductBonusGroupId);
                await LoadClassificationDDLAsync(model.ClassificationId);
                await LoadWarehousesDDLAsync();
                ViewBag.ImportedOptions = GetImportedOptions(model.Imported);
                return View(model);
            }

            // تواريخ الإنشاء والتعديل لأول مرة
            model.CreatedAt = DateTime.Now;
            model.UpdatedAt = DateTime.Now;

            // لو السعر موجود ولم يتم تحديد تاريخ آخر تغيير نضع تاريخ اليوم
            model.LastPriceChangeDate ??= DateTime.Now;

            _db.Products.Add(model);
            await _db.SaveChangesAsync();
            // إبطال كاش بحث الأصناف — لا إعادة بناء هنا؛ أول طلب بحث يحمّل من قاعدة البيانات.
            _productCache.ClearProductsCache();

            await _activityLogger.LogAsync(
                UserActionType.Create,
                "Product",
                model.ProdId,
                $"إنشاء صنف جديد: {model.ProdName}");

            TempData["Msg"] = "تم إضافة الصنف بنجاح.";
            return RedirectToAction(nameof(Index));
        }







        // =========================================================
        // 4) تعديل صنف
        // =========================================================
        [HttpGet]
        [RequirePermission("Products.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            if (id <= 0) return NotFound();

            var m = await _db.Products.FindAsync(id);
            if (m == null) return NotFound();

            await LoadCategoriesDDLAsync(m.CategoryId);
            await LoadProductGroupsDDLAsync(m.ProductGroupId);
            await LoadBonusGroupsDDLAsync(m.ProductBonusGroupId);
            await LoadClassificationDDLAsync(m.ClassificationId);
            await LoadWarehousesDDLAsync();
            ViewBag.ImportedOptions = GetImportedOptions(m.Imported);

            return View(m);
        }







        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Products.Edit")]
        public async Task<IActionResult> Edit(int id, Product model)
        {
            // حماية: لو حد غيّر الـ Id في الفورم
            if (id != model.ProdId) return BadRequest();

            if (!ModelState.IsValid)
            {
                await LoadCategoriesDDLAsync(model.CategoryId);
                await LoadProductGroupsDDLAsync(model.ProductGroupId);
                await LoadBonusGroupsDDLAsync(model.ProductBonusGroupId);
                await LoadClassificationDDLAsync(model.ClassificationId);
                await LoadWarehousesDDLAsync();
                ViewBag.ImportedOptions = GetImportedOptions(model.Imported);
                return View(model);
            }

            // جلب النسخة الأصلية من قاعدة البيانات للمقارنة
            var original = await _db.Products
                                    .AsNoTracking()
                                    .FirstOrDefaultAsync(p => p.ProdId == id);
            if (original == null) return NotFound();

            var oldValues = System.Text.Json.JsonSerializer.Serialize(new
            {
                original.ProdName,
                original.Barcode,
                original.PriceRetail,
                original.CategoryId
            });

            // المحافظة على تاريخ الإنشاء القديم
            model.CreatedAt = original.CreatedAt;

            // تحديث تاريخ التعديل الآن
            model.UpdatedAt = DateTime.Now;

            // مقارنة السعر لتحديث تاريخ آخر تغيير سعر
            if (original.PriceRetail != model.PriceRetail)
            {
                model.LastPriceChangeDate = DateTime.Now;
            }

            _db.Products.Update(model);

            // عند وجود مخزن معيّن للصنف: نقل أي رصيد في مخازن أخرى إلى هذا المخزن (حتى يظهر الصنف في المخزن الصحيح ويُزال من الاكسسوار مثلاً)
            int stockMoved = 0;
            if (model.WarehouseId.HasValue)
            {
                int targetWh = model.WarehouseId.Value;
                var tranDate = DateTime.UtcNow;
                const string sourceType = "SyncToProductWarehouse";
                const int sourceId = 0;
                // تضمين صفوف لها رصيد إما من RemainingQty أو من (QtyIn - QtyOut) للقيود القديمة التي RemainingQty فيها null
                var sourceRows = await _db.StockLedger
                    .Where(sl => sl.ProdId == model.ProdId && sl.WarehouseId != targetWh && sl.QtyIn > 0 &&
                        ((sl.RemainingQty ?? 0) > 0 || (sl.RemainingQty == null && sl.QtyIn > sl.QtyOut)))
                    .OrderBy(sl => sl.TranDate)
                    .ToListAsync();
                int lineNo = 0;
                foreach (var row in sourceRows)
                {
                    int take = (row.RemainingQty ?? 0) > 0 ? (row.RemainingQty ?? 0) : Math.Max(0, row.QtyIn - row.QtyOut);
                    if (take <= 0) continue;
                    _db.StockLedger.Add(new StockLedger
                    {
                        TranDate = tranDate,
                        WarehouseId = row.WarehouseId,
                        ProdId = row.ProdId,
                        BatchNo = row.BatchNo,
                        BatchId = row.BatchId,
                        Expiry = row.Expiry,
                        QtyIn = 0,
                        QtyOut = take,
                        UnitCost = row.UnitCost,
                        RemainingQty = null,
                        SourceType = sourceType,
                        SourceId = sourceId,
                        SourceLine = lineNo,
                        CounterWarehouseId = targetWh,
                        Note = "تحويل رصيد إلى المخزن المعيّن للصنف (تعديل صنف)",
                        CreatedAt = tranDate
                    });
                    row.RemainingQty = 0;
                    _db.StockLedger.Update(row);
                    var lineTotalCost = (decimal)take * row.UnitCost;
                    var lineTotalCostNullable = row.TotalCost.HasValue && row.TotalCost.Value != 0 ? (decimal?)lineTotalCost : null;
                    _db.StockLedger.Add(new StockLedger
                    {
                        TranDate = tranDate,
                        WarehouseId = targetWh,
                        ProdId = row.ProdId,
                        BatchNo = row.BatchNo,
                        BatchId = row.BatchId,
                        Expiry = row.Expiry,
                        QtyIn = take,
                        QtyOut = 0,
                        UnitCost = row.UnitCost,
                        TotalCost = lineTotalCostNullable,
                        PurchaseDiscount = row.PurchaseDiscount,
                        PriceRetailBatch = row.PriceRetailBatch,
                        RemainingQty = take,
                        SourceType = sourceType,
                        SourceId = sourceId,
                        SourceLine = lineNo,
                        CounterWarehouseId = row.WarehouseId,
                        Note = "تحويل رصيد إلى المخزن المعيّن للصنف (تعديل صنف)",
                        CreatedAt = tranDate
                    });
                    stockMoved += take;
                    lineNo++;
                }
            }

            await _db.SaveChangesAsync();
            _productCache.ClearProductsCache();

            var newValues = System.Text.Json.JsonSerializer.Serialize(new
            {
                model.ProdName,
                model.Barcode,
                model.PriceRetail,
                model.CategoryId
            });
            await _activityLogger.LogAsync(
                UserActionType.Edit,
                "Product",
                model.ProdId,
                $"تعديل صنف: {model.ProdName}",
                oldValues,
                newValues);

            if (stockMoved > 0)
                TempData["Msg"] = "تم تعديل الصنف بنجاح. تم نقل " + stockMoved + " وحدة إلى المخزن الجديد.";
            else
                TempData["Msg"] = "تم تعديل الصنف بنجاح.";
            return RedirectToAction(nameof(Edit), new { id = model.ProdId });
        }







        // =========================================================
        // 5) حذف صنف واحد (النموذج التقليدي)
        // =========================================================
        [HttpGet]
        [RequirePermission("Products.Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            if (id <= 0) return NotFound();

            var m = await _db.Products.FindAsync(id);
            if (m == null) return NotFound();

            return View(m);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [RequirePermission("Products.Delete")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var m = await _db.Products.FindAsync(id);
            if (m != null)
            {
                var oldValues = System.Text.Json.JsonSerializer.Serialize(new
                {
                    m.ProdName,
                    m.Barcode,
                    m.PriceRetail
                });
                _db.Products.Remove(m);
                await _db.SaveChangesAsync();
                _productCache.ClearProductsCache();

                await _activityLogger.LogAsync(
                    UserActionType.Delete,
                    "Product",
                    id,
                    $"حذف صنف: {m.ProdName}",
                    oldValues: oldValues);

                TempData["Msg"] = "تم حذف الصنف.";
            }

            return RedirectToAction(nameof(Index));
        }








        // =========================================================
        // 6) حذف متعدد من شاشة القائمة (BulkDelete)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Products.Delete")]
        public async Task<IActionResult> BulkDelete(string selectedIds)
        {
            // selectedIds: سلسلة IDs مفصولة بفاصلة  "1,5,7"
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["Msg"] = "لم يتم اختيار أي صنف للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var ids = selectedIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x, out var v) ? (int?)v : null)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();

            if (!ids.Any())
            {
                TempData["Msg"] = "لم يتم اختيار أي صنف صحيح للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var products = await _db.Products
                .Where(p => ids.Contains(p.ProdId))
                .ToListAsync();

            if (!products.Any())
            {
                TempData["Msg"] = "لم يتم العثور على الأصناف المحددة.";
                return RedirectToAction(nameof(Index));
            }

            _db.Products.RemoveRange(products);
            await _db.SaveChangesAsync();
            _productCache.ClearProductsCache();

            TempData["Msg"] = $"تم حذف {products.Count} صنف/أصناف بنجاح.";
            return RedirectToAction(nameof(Index));
        }










        // =========================================================
        // 7) حذف جميع الأصناف (DeleteAll) — استخدمه بحذر وبصلاحيات
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Products.Delete")]
        public async Task<IActionResult> DeleteAll()
        {
            var allProducts = await _db.Products.ToListAsync();

            if (!allProducts.Any())
            {
                TempData["Msg"] = "لا توجد أصناف لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            _db.Products.RemoveRange(allProducts);
            await _db.SaveChangesAsync();
            _productCache.ClearProductsCache();

            TempData["Msg"] = $"تم حذف جميع الأصناف ({allProducts.Count}) بنجاح.";
            return RedirectToAction(nameof(Index));
        }









        // =========================
        // Export — تصدير قائمة الأصناف (Excel أو CSV)
        // =========================
        [HttpGet]
        [RequirePermission("Products.Export")]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy = "all",      // all | name | id | barcode | gen | category | company
            string? sort = "name",     // name | id | code | barcode | gen | category | comp | price | created | modified | lastprice
            string? dir = "asc",      // asc | desc
            bool useDateRange = false,      // فلترة بتاريخ الإنشاء
            DateTime? fromDate = null,       // من تاريخ
            DateTime? toDate = null,       // إلى تاريخ
            // ✅ فلاتر متراكبة
            int? filterProductGroupId = null,
            string? filterCompany = null,
            string? filterImported = null,
            // ✅ فلاتر أعمدة Excel
            string? filterCol_productGroup = null,
            string? filterCol_bonusGroup = null,
            string? filterCol_company = null,
            string? filterCol_location = null,
            string? filterCol_warehouse = null,
            string? filterCol_imported = null,
            string? filterCol_isactive = null,
            // فلاتر باقي الأعمدة (+ _contains للبحث "يحتوي")
            string? filterCol_id = null,
            string? filterCol_name = null,
            string? filterCol_name_contains = null,
            string? filterCol_barcode = null,
            string? filterCol_generic = null,
            string? filterCol_category = null,
            string? filterCol_classification = null,
            string? filterCol_price = null,
            string? filterCol_created = null,
            string? filterCol_modified = null,
            string? filterCol_lastprice = null,
            string? filterCol_hasQuota = null,
            string? filterCol_quotaQuantity = null,
            string? filterCol_cartonQuantity = null,
            string? filterCol_packQuantity = null,
            string? filterCol_descShort = null,
            // فلاتر رقمية متقدمة
            string? filterCol_idExpr = null,
            string? filterCol_priceExpr = null,
            string? format = "excel")    // excel | csv
        {
            // متغير: الاستعلام الأساسي بدون تتبّع لزيادة السرعة
            IQueryable<Product> q = _db.Products
                .Include(p => p.ProductGroup)
                .Include(p => p.ProductBonusGroup)
                .Include(p => p.Classification)
                .Include(p => p.Warehouse)
                .AsNoTracking();

            // ========= البحث =========
            var term = (search ?? string.Empty).Trim();                 // متغير: نص البحث بعد التنظيف
            var sb = (searchBy ?? "all").Trim().ToLowerInvariant();  // متغير: نوع البحث

            if (!string.IsNullOrEmpty(term))
            {
                switch (sb)
                {
                    case "name":
                        // بحث باسم الصنف
                        q = q.Where(p => p.ProdName.Contains(term));
                        break;

                    case "id":
                    case "code":
                        // بحث برقم الصنف
                        if (int.TryParse(term, out var idVal))
                        {
                            q = q.Where(p => p.ProdId == idVal);
                        }
                        else
                        {
                            q = q.Where(p => p.ProdId.ToString().Contains(term));
                        }
                        break;

                    case "barcode":
                    case "bar":
                        q = q.Where(p => p.Barcode != null && p.Barcode.Contains(term));
                        break;

                    case "gen":
                    case "generic":
                        q = q.Where(p => p.GenericName != null && p.GenericName.Contains(term));
                        break;

                    case "category":
                    case "cat":
                        if (int.TryParse(term, out var catId))
                        {
                            q = q.Where(p => p.CategoryId == catId);
                        }
                        else
                        {
                            q = q.Where(p => p.CategoryId.ToString().Contains(term));
                        }
                        break;

                    case "company":
                    case "comp":
                        q = q.Where(p => p.Company != null && p.Company.Contains(term));
                        break;

                    case "all":
                    default:
                        // بحث شامل فى أكثر من عمود
                        q = q.Where(p =>
                               p.ProdName.Contains(term)
                            || p.ProdId.ToString().Contains(term)
                            || (p.Barcode != null && p.Barcode.Contains(term))
                            || (p.GenericName != null && p.GenericName.Contains(term))
                            || (p.Company != null && p.Company.Contains(term)));
                        break;
                }
            }

            // ========= ✅ فلاتر متراكبة =========
            
            // فلتر حسب مجموعة الصنف (ProductGroup)
            if (filterProductGroupId.HasValue && filterProductGroupId.Value > 0)
            {
                q = q.Where(p => p.ProductGroupId == filterProductGroupId.Value);
            }

            // فلتر حسب الشركة (Company)
            if (!string.IsNullOrWhiteSpace(filterCompany))
            {
                var companyFilter = filterCompany.Trim();
                q = q.Where(p => p.Company != null && p.Company.Contains(companyFilter));
            }

            // فلتر حسب المنشأ (Imported)
            if (!string.IsNullOrWhiteSpace(filterImported))
            {
                var importedFilter = filterImported.Trim().ToLower();
                if (importedFilter == "مستورد" || importedFilter == "imported")
                {
                    q = q.Where(p => (p.Imported ?? "").Contains("مستورد"));
                }
                else if (importedFilter == "محلي" || importedFilter == "local")
                {
                    q = q.Where(p => (p.Imported ?? "").Contains("محلي"));
                }
            }

            // ========= فلاتر أعمدة Excel =========
            var sep = new[] { '|' };
            if (!string.IsNullOrWhiteSpace(filterCol_productGroup))
            {
                var ids = filterCol_productGroup.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    q = q.Where(p => p.ProductGroupId != null && ids.Contains(p.ProductGroupId.Value));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_bonusGroup))
            {
                var ids = filterCol_bonusGroup.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    q = q.Where(p => p.ProductBonusGroupId != null && ids.Contains(p.ProductBonusGroupId.Value));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_company))
            {
                var vals = filterCol_company.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    q = q.Where(p => p.Company != null && vals.Contains(p.Company));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_location))
            {
                var vals = filterCol_location.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    q = q.Where(p => p.Location != null && vals.Contains(p.Location));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_warehouse))
            {
                var ids = filterCol_warehouse.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x) && int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    .Select(x => int.Parse(x, CultureInfo.InvariantCulture))
                    .ToList();
                if (ids.Count > 0)
                    q = q.Where(p => p.WarehouseId != null && ids.Contains(p.WarehouseId.Value));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_imported))
            {
                var vals = filterCol_imported.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    q = q.Where(p => p.Imported != null && vals.Contains(p.Imported));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_isactive))
            {
                var vals = filterCol_isactive.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                {
                    var activeVals = new List<bool>();
                    if (vals.Contains("نشط")) activeVals.Add(true);
                    if (vals.Contains("موقوف")) activeVals.Add(false);
                    if (activeVals.Count > 0)
                        q = q.Where(p => activeVals.Contains(p.IsActive));
                }
            }

            // فلاتر باقي الأعمدة (نفس منطق Index)
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) q = q.Where(p => ids.Contains(p.ProdId));
            }
            // أولوية فلتر القيم المحددة (تشيك بوكسات) على "يحتوي" — مطابقة تامة عند اختيار أسماء من لوحة العمود
            if (!string.IsNullOrWhiteSpace(filterCol_name))
            {
                var vals = filterCol_name.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) q = q.Where(p => p.ProdName != null && vals.Contains(p.ProdName));
            }
            else if (!string.IsNullOrWhiteSpace(filterCol_name_contains))
            {
                var nameContains = filterCol_name_contains.Trim();
                q = q.Where(p => p.ProdName != null && EF.Functions.Like(p.ProdName, "%" + nameContains + "%"));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_barcode))
            {
                var vals = filterCol_barcode.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) q = q.Where(p => p.Barcode != null && vals.Contains(p.Barcode));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_generic))
            {
                var vals = filterCol_generic.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) q = q.Where(p => p.GenericName != null && vals.Contains(p.GenericName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_category))
            {
                var ids = filterCol_category.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) q = q.Where(p => p.CategoryId != null && ids.Contains(p.CategoryId.Value));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_classification))
            {
                var ids = filterCol_classification.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) q = q.Where(p => p.ClassificationId != null && ids.Contains(p.ClassificationId.Value));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_price))
            {
                var prices = filterCol_price.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => decimal.TryParse(x.Trim(), out var v) ? v : (decimal?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (prices.Count > 0) q = q.Where(p => prices.Contains(p.PriceRetail));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_hasQuota))
            {
                var vals = filterCol_hasQuota.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                {
                    var quotaVals = new List<bool>();
                    if (vals.Contains("نعم")) quotaVals.Add(true);
                    if (vals.Contains("لا")) quotaVals.Add(false);
                    if (quotaVals.Count > 0) q = q.Where(p => quotaVals.Contains(p.HasQuota));
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_quotaQuantity))
            {
                var qty = filterCol_quotaQuantity.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (qty.Count > 0) q = q.Where(p => p.QuotaQuantity != null && qty.Contains(p.QuotaQuantity.Value));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_cartonQuantity))
            {
                var qty = filterCol_cartonQuantity.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (qty.Count > 0) q = q.Where(p => p.CartonQuantity != null && qty.Contains(p.CartonQuantity.Value));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_packQuantity))
            {
                var qty = filterCol_packQuantity.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (qty.Count > 0) q = q.Where(p => p.PackQuantity != null && qty.Contains(p.PackQuantity.Value));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_descShort))
            {
                var vals = filterCol_descShort.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) q = q.Where(p => p.Description != null && vals.Any(v => p.Description.Contains(v)));
            }

            // فلاتر التاريخ
            if (!string.IsNullOrWhiteSpace(filterCol_created))
            {
                var dateParts = filterCol_created.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (dateParts.Count > 0)
                {
                    var dateFilters = dateParts.Select(dp =>
                    {
                        var parts = dp.Split('-');
                        if (parts.Length == 2 && int.TryParse(parts[0], out var y) && int.TryParse(parts[1], out var m))
                            return new { Year = y, Month = m };
                        return null;
                    }).Where(x => x != null).ToList();
                    if (dateFilters.Count > 0)
                        q = q.Where(p => dateFilters.Any(df => p.CreatedAt.Year == df.Year && p.CreatedAt.Month == df.Month));
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_modified))
            {
                var dateParts = filterCol_modified.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (dateParts.Count > 0)
                {
                    var dateFilters = dateParts.Select(dp =>
                    {
                        var parts = dp.Split('-');
                        if (parts.Length == 2 && int.TryParse(parts[0], out var y) && int.TryParse(parts[1], out var m))
                            return new { Year = y, Month = m };
                        return null;
                    }).Where(x => x != null).ToList();
                    if (dateFilters.Count > 0)
                        q = q.Where(p => dateFilters.Any(df => p.UpdatedAt.Year == df.Year && p.UpdatedAt.Month == df.Month));
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_lastprice))
            {
                var dateParts = filterCol_lastprice.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (dateParts.Count > 0)
                {
                    var dateFilters = dateParts.Select(dp =>
                    {
                        var parts = dp.Split('-');
                        if (parts.Length == 2 && int.TryParse(parts[0], out var y) && int.TryParse(parts[1], out var m))
                            return new { Year = y, Month = m };
                        return null;
                    }).Where(x => x != null).ToList();
                    if (dateFilters.Count > 0)
                        q = q.Where(p => p.LastPriceChangeDate.HasValue && dateFilters.Any(df =>
                            p.LastPriceChangeDate!.Value.Year == df.Year && p.LastPriceChangeDate.Value.Month == df.Month));
                }
            }

            // فلاتر رقمية متقدمة (بما فيها رقم واحد = كود صنف مطابق)
            if (!string.IsNullOrWhiteSpace(filterCol_idExpr))
            {
                var expr = filterCol_idExpr.Trim();
                if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.Substring(2), out var max))
                    q = q.Where(p => p.ProdId <= max);
                else if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.Substring(2), out var min))
                    q = q.Where(p => p.ProdId >= min);
                else if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.Substring(1), out var max2))
                    q = q.Where(p => p.ProdId < max2);
                else if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.Substring(1), out var min2))
                    q = q.Where(p => p.ProdId > min2);
                else if ((expr.Contains(':') || expr.Contains('-')) && !expr.StartsWith("-"))
                {
                    var separator = expr.Contains(':') ? ':' : '-';
                    var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out var from) && int.TryParse(parts[1].Trim(), out var to))
                    {
                        if (from > to) (from, to) = (to, from);
                        q = q.Where(p => p.ProdId >= from && p.ProdId <= to);
                    }
                }
                else if (int.TryParse(expr, out var exactId))
                    q = q.Where(p => p.ProdId == exactId);
            }
            if (!string.IsNullOrWhiteSpace(filterCol_priceExpr))
            {
                var expr = filterCol_priceExpr.Trim();
                if (expr.StartsWith("<=") && decimal.TryParse(expr.Substring(2), out var max))
                    q = q.Where(p => p.PriceRetail <= max);
                else if (expr.StartsWith(">=") && decimal.TryParse(expr.Substring(2), out var min))
                    q = q.Where(p => p.PriceRetail >= min);
                else if (expr.StartsWith("<") && decimal.TryParse(expr.Substring(1), out var max2))
                    q = q.Where(p => p.PriceRetail < max2);
                else if (expr.StartsWith(">") && decimal.TryParse(expr.Substring(1), out var min2))
                    q = q.Where(p => p.PriceRetail > min2);
                else if ((expr.Contains(':') || expr.Contains('-')) && !expr.StartsWith("-"))
                {
                    var separator = expr.Contains(':') ? ':' : '-';
                    var parts = expr.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2 && decimal.TryParse(parts[0].Trim(), out var from) && decimal.TryParse(parts[1].Trim(), out var to))
                    {
                        if (from > to) (from, to) = (to, from);
                        q = q.Where(p => p.PriceRetail >= from && p.PriceRetail <= to);
                    }
                }
                else if (decimal.TryParse(expr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var exactPrice))
                    q = q.Where(p => p.PriceRetail == exactPrice);
            }

            // ========= فلترة بالتاريخ (تاريخ الإنشاء) =========
            if (useDateRange)
            {
                if (fromDate.HasValue)
                {
                    q = q.Where(p => p.CreatedAt >= fromDate.Value);
                }

                if (toDate.HasValue)
                {
                    q = q.Where(p => p.CreatedAt <= toDate.Value);
                }
            }

            // ========= الترتيب =========
            var so = (sort ?? "name").Trim().ToLowerInvariant();      // متغير: عمود الترتيب
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);  // متغير: هل الترتيب تنازلى؟

            q = so switch
            {
                "id" =>
                    (desc ? q.OrderByDescending(p => p.ProdId)
                          : q.OrderBy(p => p.ProdId)),

                "code" or "barcode" =>
                    (desc ? q.OrderByDescending(p => p.Barcode)
                          : q.OrderBy(p => p.Barcode)),

                "gen" or "generic" =>
                    (desc ? q.OrderByDescending(p => p.GenericName)
                          : q.OrderBy(p => p.GenericName)),

                "category" or "cat" =>
                    (desc ? q.OrderByDescending(p => p.CategoryId)
                          : q.OrderBy(p => p.CategoryId)),

                "classification" =>
                    (desc ? q.OrderByDescending(p => p.Classification != null ? (p.Classification.Name ?? "") : "")
                          : q.OrderBy(p => p.Classification != null ? (p.Classification.Name ?? "") : "")),

                "comp" or "company" =>
                    (desc ? q.OrderByDescending(p => p.Company)
                          : q.OrderBy(p => p.Company)),

                "location" =>
                    (desc ? q.OrderByDescending(p => p.Location)
                          : q.OrderBy(p => p.Location)),

                "price" =>
                    (desc ? q.OrderByDescending(p => p.PriceRetail)
                          : q.OrderBy(p => p.PriceRetail)),

                "created" =>
                    (desc ? q.OrderByDescending(p => p.CreatedAt)
                          : q.OrderBy(p => p.CreatedAt)),

                "modified" =>
                    (desc ? q.OrderByDescending(p => p.UpdatedAt)
                          : q.OrderBy(p => p.UpdatedAt)),

                "lastprice" or "updated" =>
                    (desc ? q.OrderByDescending(p => p.LastPriceChangeDate)
                          : q.OrderBy(p => p.LastPriceChangeDate)),

                "cartonquantity" =>
                    (desc ? q.OrderByDescending(p => p.CartonQuantity ?? 0)
                          : q.OrderBy(p => p.CartonQuantity ?? 0)),

                "packquantity" =>
                    (desc ? q.OrderByDescending(p => p.PackQuantity ?? 0)
                          : q.OrderBy(p => p.PackQuantity ?? 0)),

                "name" or _ =>
                    (desc ? q.OrderByDescending(p => p.ProdName)
                          : q.OrderBy(p => p.ProdName)),
            };

            // ========= جلب كل البيانات (بدون Paging) =========
            var products = await q.ToListAsync();    // متغير: قائمة الأصناف للتصدير

            // توحيد قيمة format
            format = (format ?? "excel").Trim().ToLowerInvariant();

            // ========= الفرع الأول: CSV =========
            if (format == "csv")
            {
                var sbCsv = new StringBuilder();     // متغير: نص CSV

                // عناوين الأعمدة
                sbCsv.AppendLine(string.Join(",",
                    Csv("كود الصنف"),
                    Csv("اسم الصنف"),
                    Csv("الباركود"),
                    Csv("الاسم العلمي"),
                    Csv("كود الفئة"),
                    Csv("التصنيف"),
                    Csv("الشركة"),
                    Csv("الموقع"),
                    Csv("المخزن"),
                    Csv("محلي/مستورد"),
                    Csv("فعال؟"),
                    Csv("سعر الجمهور"),
                    Csv("تاريخ الإنشاء"),
                    Csv("آخر تعديل"),
                    Csv("آخر تغيير سعر"),
                    Csv("كمية الكرتونة"),
                    Csv("كمية الباكو"),
                    Csv("الوصف")
                ));

                // البيانات
                foreach (var p in products)
                {
                    sbCsv.AppendLine(string.Join(",",
                        Csv(p.ProdId.ToString()),
                        Csv(p.ProdName),
                        Csv(p.Barcode),
                        Csv(p.GenericName),
                        Csv(p.CategoryId.ToString()),
                        Csv(p.Classification?.Name),
                        Csv(p.Company),
                        Csv(p.Location),
                        Csv(p.Warehouse?.WarehouseName),
                        Csv(p.Imported?.ToString()),
                        Csv(p.IsActive ? "نشط" : "موقوف"),
                        Csv(p.PriceRetail.ToString(CultureInfo.InvariantCulture)),
                        Csv(p.CreatedAt.ToString("yyyy-MM-dd HH:mm")),
                        Csv(p.UpdatedAt.ToString("yyyy-MM-dd HH:mm")),
                        Csv(p.LastPriceChangeDate?.ToString("yyyy-MM-dd")),
                        Csv(p.CartonQuantity?.ToString(CultureInfo.InvariantCulture)),
                        Csv(p.PackQuantity?.ToString(CultureInfo.InvariantCulture)),
                        Csv(p.Description)
                    ));
                }

                // تحويل إلى UTF-8 مع BOM علشان Excel يقرا عربى صح
                var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
                var bytes = utf8.GetBytes(sbCsv.ToString());
                var fileNameCsv = ExcelExportNaming.ArabicTimestampedFileName("الأصناف", ".csv");

                return File(bytes, "text/csv; charset=utf-8", fileNameCsv);
            }

            // ========= الفرع الثاني: Excel (XLSX) =========
            using var workbook = new XLWorkbook();             // متغير: ملف Excel
            var ws = workbook.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("الأصناف"));

            int r = 1; // متغير: رقم الصف الحالى

            // عناوين الأعمدة
            ws.Cell(r, 1).Value = "كود الصنف";
            ws.Cell(r, 2).Value = "اسم الصنف";
            ws.Cell(r, 3).Value = "الباركود";
            ws.Cell(r, 4).Value = "الاسم العلمي";
            ws.Cell(r, 5).Value = "كود الفئة";
            ws.Cell(r, 6).Value = "التصنيف";
            ws.Cell(r, 7).Value = "الشركة";
            ws.Cell(r, 8).Value = "الموقع";
            ws.Cell(r, 9).Value = "المخزن";
            ws.Cell(r, 10).Value = "محلي/مستورد";
            ws.Cell(r, 11).Value = "فعال؟";
            ws.Cell(r, 12).Value = "سعر الجمهور";
            ws.Cell(r, 13).Value = "تاريخ الإنشاء";
            ws.Cell(r, 14).Value = "آخر تعديل";
            ws.Cell(r, 15).Value = "آخر تغيير سعر";
            ws.Cell(r, 16).Value = "كمية الكرتونة";
            ws.Cell(r, 17).Value = "كمية الباكو";
            ws.Cell(r, 18).Value = "الوصف";

            // تنسيق صف العناوين
            var headerRange = ws.Range(r, 1, r, 18);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // تعبئة البيانات
            foreach (var p in products)
            {
                r++;

                ws.Cell(r, 1).Value = p.ProdId;
                ws.Cell(r, 2).Value = p.ProdName;
                ws.Cell(r, 3).Value = p.Barcode ?? "";
                ws.Cell(r, 4).Value = p.GenericName ?? "";
                ws.Cell(r, 5).Value = p.CategoryId;
                ws.Cell(r, 6).Value = p.Classification?.Name ?? "";
                ws.Cell(r, 7).Value = p.Company ?? "";
                ws.Cell(r, 8).Value = p.Location ?? "";
                ws.Cell(r, 9).Value = p.Warehouse?.WarehouseName ?? "";
                ws.Cell(r, 10).Value = p.Imported?.ToString() ?? "";
                ws.Cell(r, 11).Value = p.IsActive ? "نشط" : "موقوف";
                ws.Cell(r, 12).Value = p.PriceRetail;
                ws.Cell(r, 13).Value = p.CreatedAt;
                ws.Cell(r, 14).Value = p.UpdatedAt;
                ws.Cell(r, 15).Value = p.LastPriceChangeDate;
                ws.Cell(r, 16).Value = p.CartonQuantity;
                ws.Cell(r, 17).Value = p.PackQuantity;
                ws.Cell(r, 18).Value = p.Description ?? "";
            }

            // ضبط عرض الأعمدة تلقائيًا
            ws.Columns().AdjustToContents();

            // تنسيق سعر الجمهور
            ws.Column(12).Style.NumberFormat.Format = "0.00";

            using var stream = new MemoryStream();     // متغير: ذاكرة مؤقتة
            workbook.SaveAs(stream);
            stream.Position = 0;

            var fileNameXlsx = ExcelExportNaming.ArabicTimestampedFileName("الأصناف", ".xlsx");
            const string contentTypeXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

            return File(stream.ToArray(), contentTypeXlsx, fileNameXlsx);
        }

        // دالة مساعدة لتجهيز النص للـ CSV
        private static string Csv(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            // استبدال " بـ ""
            var s = value.Replace("\"", "\"\"");

            // لو فيه فاصلة أو سطر جديد → نحوط النص بين ""
            if (s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
                return "\"" + s + "\"";

            return s;
        }









        // =========================================================
        // بحث Ajax عن الأصناف بالاسم (يُستخدم فى أوتوكمبليت حركة الصنف)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> SearchProducts(string term)
        {
            // لو مفيش نص مكتوب → رجّع ليست فاضية
            if (string.IsNullOrWhiteSpace(term))
                return Json(Array.Empty<object>());

            term = term.Trim();

            // نفس منطق البحث السابق: لكن القائمة من كاش lookup خفيف (يُبنى من DB بعد المسح أو انتهاء المدة).
            var all = await _productCache.GetProductsLookupAsync(HttpContext.RequestAborted);
            var results = all
                .Where(p => p.ProdName != null && p.ProdName.Contains(term))
                .OrderBy(p => p.ProdName)
                .Take(20)
                .Select(p => new
                {
                    id = p.ProdId,
                    name = p.ProdName,
                    company = p.Company,
                    barcode = p.Barcode
                })
                .ToList();

            return Json(results);
        }









        // =========================================================
        // بحث Ajax عن الأصناف بالكود أو الباركود
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> SearchProductsByCode(string term)
        {
            if (string.IsNullOrWhiteSpace(term))
                return Json(Array.Empty<object>());

            term = term.Trim();

            var all = await _productCache.GetProductsLookupAsync(HttpContext.RequestAborted);
            var results = all
                .Where(p =>
                    p.ProdId.ToString().Contains(term) ||
                    (p.Barcode != null && p.Barcode.Contains(term)))
                .OrderBy(p => p.ProdId)
                .Take(20)
                .Select(p => new
                {
                    id = p.ProdId,
                    name = p.ProdName,
                    company = p.Company,
                    barcode = p.Barcode
                })
                .ToList();

            return Json(results);
        }









        // =========================================================
        // بحث Ajax عن العملاء / الأطراف بالاسم (فلتر اختيارى فى حركة الصنف)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> SearchParties(string term)
        {
            // البحث في خدمة الكاش (مع استبعاد كاش القائمة عند التقييد بقائمة مسموح — انظر CustomerCacheService).
            // نفس شكل SearchProducts: خصائص id/name صريحة حتى يتطابق JSON مع الواجهة (AddJsonOptions بدون camelCase).
            var results = await _customerCache.SearchPartiesAutocompleteAsync(term, HttpContext.RequestAborted);
            return Json(results.Select(r => new { id = r.Id, name = r.Name }).ToList());
        }

        /// <summary>
        /// شاشة حركة صنف.
        /// - يمكن فتحها من المينيو بدون كود → يتم اختيار الصنف من الشاشة.
        /// - يمكن فتحها من زر "حركة الصنف" فى قائمة الأصناف مع id.
        /// تستقبل أيضاً كود مخزن + فترة من/إلى + عميل اختيارى للتصفية.
        /// </summary>
        [RequirePermission("Products.Show")]
        public async Task<IActionResult> Show(
      int? id,                 // كود الصنف لو جاي من asp-route-id
      int? prodId,             // كود الصنف لو جاي من الفورم (الهيدن)
      int? warehouseId,        // كود المخزن
      DateTime? from,          // من تاريخ
      DateTime? to,            // إلى تاريخ
      int? customerId,         // كود الطرف (عميل/مورد) للفلترة
      bool filterByCustomer = false, // هل نطبق فلتر الطرف؟
      string? partyKind = "all"      // نوع الطرف: all | customer | supplier
  )
        {
            // ===== 1) توحيد كود الصنف =====
            int? finalProdId = prodId ?? id;   // متغير: الكود الفعلى للصنف

            // ===== 2) تحميل قائمة المخازن للكومبو =====
            ViewBag.Warehouses = await _db.Warehouses
                .OrderBy(w => w.WarehouseName)
                .Select(w => new SelectListItem
                {
                    Value = w.WarehouseId.ToString(),   // كود المخزن
                    Text = w.WarehouseName              // اسم المخزن
                })
                .ToListAsync();

            // ===== 3) تخزين اختيارات البحث فى ViewBag =====
            ViewBag.SelectedProdId = finalProdId;           // كود الصنف
            ViewBag.SelectedWarehouseId = warehouseId;      // كود المخزن
            ViewBag.From = from;                            // من تاريخ
            ViewBag.To = to;                                // إلى تاريخ

            int? finalCustomerId = filterByCustomer ? customerId : null;   // متغير: كود الطرف الفعلى
            ViewBag.SelectedCustomerId = finalCustomerId;
            ViewBag.FilterByCustomer = filterByCustomer;

            // نوع الطرف: all | customer | supplier
            string partyKindNorm = (partyKind ?? "all").Trim().ToLowerInvariant();
            if (partyKindNorm != "customer" && partyKindNorm != "supplier")
                partyKindNorm = "all";
            ViewBag.PartyKind = partyKindNorm;

            // تحميل اسم الطرف لو موجود
            string customerName = string.Empty;
            if (finalCustomerId.HasValue)
            {
                var customer = await _db.Customers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.CustomerId == finalCustomerId.Value);

                if (customer != null)
                    customerName = customer.CustomerName;
            }
            ViewBag.SelectedCustomerName = customerName;

            // ===== 4) تهيئة جميع أرقام الملخص والقوائم بقيم افتراضية =====

            // سعر الجمهور + الخصم المرجح
            ViewBag.PriceRetail = 0m;          // متغير: سعر الجمهور للصنف
            ViewBag.WeightedDiscount = 0m;     // متغير: الخصم المرجح (Placeholder)

            // مشتريات
            ViewBag.TotalQtyPurchased = 0;         // متغير: كمية المشتريات
            ViewBag.TotalAmountPurchased = 0m;     // متغير: مبلغ المشتريات

            // مرتجعات مشتريات
            ViewBag.TotalQtyPurchaseReturned = 0;      // متغير: كمية مردودات المشتريات
            ViewBag.TotalAmountPurchaseReturned = 0m;  // متغير: مبلغ مردودات المشتريات

            // مبيعات
            ViewBag.TotalQtySold = 0;              // متغير: كمية المبيعات
            ViewBag.TotalAmountSold = 0m;          // متغير: مبلغ المبيعات

            // مردودات مبيعات
            ViewBag.TotalQtyReturned = 0;          // متغير: كمية مردودات المبيعات
            ViewBag.TotalAmountReturned = 0m;      // متغير: مبلغ مردودات المبيعات

            // إجمالى وصافى
            ViewBag.TotalQtyAll = 0;               // متغير: إجمالى الكمية (Placeholder)
            ViewBag.NetQty = 0;                    // متغير: صافى الكمية
            ViewBag.NetAmount = 0m;                // متغير: صافى المبلغ
            ViewBag.OpeningQty = 0;                // كمية رصيد أول المدة (Opening) — عرض فقط

            // تفاصيل الحركة والملخصات
            ViewBag.Lines = Array.Empty<object>();             // حركة مجمعة (لن يُستخدم)
            ViewBag.SalesLines = Array.Empty<object>();        // فواتير المبيعات
            ViewBag.SalesReturnLines = Array.Empty<object>();  // مرتجعات المبيعات
            ViewBag.PurchaseLines = Array.Empty<object>();     // فواتير المشتريات
            ViewBag.PurchaseReturnLines = Array.Empty<object>(); // مرتجعات المشتريات
            ViewBag.AdjustmentLines = Array.Empty<object>();   // تسويات جردية
            ViewBag.PurchaseRequestLines = Array.Empty<object>(); // طلبات الشراء
            ViewBag.TransferLines = Array.Empty<object>();     // تحويلات بين المخازن

            ViewBag.YearlySummary = Array.Empty<object>();     // ملخص حسب السنوات
            ViewBag.WarehouseSummary = Array.Empty<object>();  // ملخص حسب المخازن

            // ===== 5) تحميل قائمة الأصناف للأوتوكومبليت (datalist — نفس نمط فاتورة المبيعات) =====
            await LoadProductsForAutoCompleteAsync();

            // ===== 6) لو لسه ما اخترناش صنف → رجّع الشاشة من غير بيانات =====
            if (finalProdId == null)
            {
                ViewBag.SelectedProdName = "";
                return View(model: null);   // @model Product? فى الفيو
            }

            // ===== 7) تحميل بيانات بطاقة الصنف من جدول Products =====
            var product = await _db.Products
                .Include(p => p.Category)
                .Include(p => p.Classification)
                .FirstOrDefaultAsync(p => p.ProdId == finalProdId.Value);

            if (product == null)
                return NotFound();

            ViewBag.SelectedProdName = product.ProdName;      // اسم الصنف فى بوكس البحث
            ViewBag.PriceRetail = product.PriceRetail;        // سعر الجمهور من جدول الأصناف

            // كمية افتتاحية (عرض فقط — من StockLedger حيث SourceType = Opening)
            var openingSlQ = _db.StockLedger.AsNoTracking()
                .Where(sl => sl.ProdId == finalProdId.Value && sl.SourceType == "Opening");
            if (warehouseId.HasValue)
                openingSlQ = openingSlQ.Where(sl => sl.WarehouseId == warehouseId.Value);
            ViewBag.OpeningQty = await openingSlQ.SumAsync(sl => (int?)sl.QtyIn) ?? 0;

            // ===== 8) جلب بيانات المبيعات من SalesInvoiceLines =====
            // تجميعات الملخص: كل الفترات، كل الفواتير (مرحّل + غير مرحّل) — مثل قائمة أصناف المبيعات
            var salesAllTimeQuery = _db.SalesInvoiceLines
                .AsNoTracking()
                .Where(sil => sil.ProdId == finalProdId.Value);
            ViewBag.TotalQtySold = await salesAllTimeQuery.SumAsync(sil => sil.Qty);
            ViewBag.TotalAmountSold = await salesAllTimeQuery.SumAsync(sil => sil.LineNetTotal);

            var salesQuery = _db.SalesInvoiceLines
                .AsNoTracking()
                .Include(sil => sil.SalesInvoice)
                    .ThenInclude(si => si.Customer)
                .Where(sil => sil.ProdId == finalProdId.Value);

            if (warehouseId.HasValue)
                salesQuery = salesQuery.Where(sil => sil.SalesInvoice.WarehouseId == warehouseId.Value);
            if (from.HasValue)
                salesQuery = salesQuery.Where(sil => sil.SalesInvoice.SIDate >= from.Value);
            if (to.HasValue)
                salesQuery = salesQuery.Where(sil => sil.SalesInvoice.SIDate <= to.Value);
            if (finalCustomerId.HasValue)
                salesQuery = salesQuery.Where(sil => sil.SalesInvoice.CustomerId == finalCustomerId.Value);

            // خصم السطر: نسبة فعّالة من إجمالي قبل الضريبة مقابل سعر الجمهور × الكمية؛
            // إن تعادل الأرقام (قديم/تقريب) نستخدم الخصم المركّب من Disc1/2/3.
            var salesList = await salesQuery
                .OrderByDescending(sil => sil.SalesInvoice.SIDate)
                .ThenByDescending(sil => sil.SalesInvoice.SIId)
                .Select(sil => new
                {
                    Date = sil.SalesInvoice.SIDate,
                    InvoiceNo = sil.SIId,
                    Customer = sil.SalesInvoice.Customer != null ? sil.SalesInvoice.Customer.CustomerName : "",
                    Qty = sil.Qty,
                    UnitPrice = sil.UnitSalePrice,
                    DiscountPercent =
                        sil.Qty > 0 && sil.PriceRetail > 0
                            ? (
                                sil.LineTotalAfterDiscount < sil.Qty * sil.PriceRetail - 0.0001m
                                    ? 100m * (1m - sil.LineTotalAfterDiscount / (sil.Qty * sil.PriceRetail))
                                    : 100m * (1m
                                        - (1m - sil.Disc1Percent / 100m)
                                        * (1m - sil.Disc2Percent / 100m)
                                        * (1m - sil.Disc3Percent / 100m))
                              )
                            : 0m,
                    NetAmount = sil.LineNetTotal,
                    InvoiceNetTotal = sil.SalesInvoice.NetTotal
                })
                .ToListAsync();

            ViewBag.SalesLines = salesList;

            // ===== 9) حساب صافي الكمية من StockLedger =====
            var stockLedgerQuery = _db.StockLedger
                .AsNoTracking()
                .Where(sl => sl.ProdId == finalProdId.Value);

            // فلترة بالمخزن إذا تم تحديده
            if (warehouseId.HasValue)
            {
                stockLedgerQuery = stockLedgerQuery.Where(sl => sl.WarehouseId == warehouseId.Value);
            }

            // فلترة بالتاريخ إذا تم تحديده
            if (from.HasValue)
            {
                stockLedgerQuery = stockLedgerQuery.Where(sl => sl.TranDate >= from.Value);
            }
            if (to.HasValue)
            {
                stockLedgerQuery = stockLedgerQuery.Where(sl => sl.TranDate <= to.Value);
            }

            // حساب صافي الكمية = Sum(QtyIn) - Sum(QtyOut) — ولا يُعرض سالب أبداً (الحد الأدنى 0)
            int totalQtyIn = await stockLedgerQuery.SumAsync(sl => sl.QtyIn);
            int totalQtyOut = await stockLedgerQuery.SumAsync(sl => sl.QtyOut);
            int calculatedNetQty = totalQtyIn - totalQtyOut;
            ViewBag.NetQty = calculatedNetQty < 0 ? 0 : calculatedNetQty;

            // حساب صافي المبلغ من StockLedger (إجمالي التكلفة)
            decimal calculatedNetAmount = await stockLedgerQuery
                .Where(sl => sl.QtyIn > 0)
                .SumAsync(sl => (sl.TotalCost ?? (sl.QtyIn * sl.UnitCost))) 
                - await stockLedgerQuery
                    .Where(sl => sl.QtyOut > 0)
                    .SumAsync(sl => (sl.TotalCost ?? (sl.QtyOut * sl.UnitCost)));
            ViewBag.NetAmount = calculatedNetAmount;

            // ===== 10) الخصم الفعّال (يدوي من ProductDiscountOverrides إن وُجد، وإلا المرجّح من StockLedger) =====
            decimal calculatedWeightedDiscount = await _stockAnalysisService.GetEffectivePurchaseDiscountAsync(finalProdId.Value, warehouseId, null);
            ViewBag.WeightedDiscount = calculatedWeightedDiscount;

            // ===== 11) جلب بيانات المشتريات من PILines =====
            // تجميعات الملخص: كل الفترات، كل الفواتير (مرحّل + غير مرحّل) — مثل قائمة أصناف فواتير المشتريات
            var purchasesAllTimeQuery = _db.PILines
                .AsNoTracking()
                .Where(pil => pil.ProdId == finalProdId.Value);
            ViewBag.TotalQtyPurchased = await purchasesAllTimeQuery.SumAsync(pil => pil.Qty);
            ViewBag.TotalAmountPurchased = await purchasesAllTimeQuery.SumAsync(pil => pil.Qty * pil.UnitCost);

            var purchasesQuery = _db.PILines
                .AsNoTracking()
                .Include(pil => pil.PurchaseInvoice)
                    .ThenInclude(pi => pi.Customer)
                .Where(pil => pil.ProdId == finalProdId.Value);

            if (warehouseId.HasValue)
                purchasesQuery = purchasesQuery.Where(pil => pil.PurchaseInvoice.WarehouseId == warehouseId.Value);
            if (from.HasValue)
                purchasesQuery = purchasesQuery.Where(pil => pil.PurchaseInvoice.PIDate >= from.Value);
            if (to.HasValue)
                purchasesQuery = purchasesQuery.Where(pil => pil.PurchaseInvoice.PIDate <= to.Value);

            var purchasesList = await purchasesQuery
                .OrderByDescending(pil => pil.PurchaseInvoice.PIDate)
                .ThenByDescending(pil => pil.PurchaseInvoice.PIId)
                .Select(pil => new
                {
                    Date = pil.PurchaseInvoice.PIDate,
                    InvoiceNo = pil.PurchaseInvoice.PIId,
                    Supplier = pil.PurchaseInvoice.Customer != null ? pil.PurchaseInvoice.Customer.CustomerName : "",
                    Qty = pil.Qty,
                    UnitCost = pil.UnitCost,
                    Amount = pil.Qty * pil.UnitCost
                })
                .ToListAsync();

            ViewBag.PurchaseLines = purchasesList;

            // ===== 12) مرتجعات المبيعات من SalesReturnLines =====
            var salesReturnsAllTime = _db.SalesReturnLines
                .AsNoTracking()
                .Where(srl => srl.ProdId == finalProdId.Value);
            ViewBag.TotalQtyReturned = await salesReturnsAllTime.SumAsync(srl => srl.Qty);
            ViewBag.TotalAmountReturned = await salesReturnsAllTime.SumAsync(srl => srl.LineNetTotal);

            var salesReturnsQuery = _db.SalesReturnLines
                .AsNoTracking()
                .Include(srl => srl.SalesReturn)
                    .ThenInclude(sr => sr!.Customer)
                .Where(srl => srl.ProdId == finalProdId.Value);
            if (warehouseId.HasValue)
                salesReturnsQuery = salesReturnsQuery.Where(srl => srl.SalesReturn != null && srl.SalesReturn.WarehouseId == warehouseId.Value);
            if (from.HasValue)
                salesReturnsQuery = salesReturnsQuery.Where(srl => srl.SalesReturn != null && srl.SalesReturn.SRDate >= from.Value);
            if (to.HasValue)
                salesReturnsQuery = salesReturnsQuery.Where(srl => srl.SalesReturn != null && srl.SalesReturn.SRDate <= to.Value);
            if (finalCustomerId.HasValue)
                salesReturnsQuery = salesReturnsQuery.Where(srl => srl.SalesReturn != null && srl.SalesReturn.CustomerId == finalCustomerId.Value);

            var salesReturnLinesList = await salesReturnsQuery
                .OrderByDescending(srl => srl.SalesReturn!.SRDate)
                .ThenByDescending(srl => srl.SalesReturn!.SRId)
                .ThenBy(srl => srl.LineNo)
                .Select(srl => new
                {
                    Date = srl.SalesReturn!.SRDate,
                    DocNo = srl.SalesReturn.SRId,
                    Customer = srl.SalesReturn.Customer != null ? srl.SalesReturn.Customer.CustomerName : "",
                    Qty = srl.Qty,
                    UnitPrice = srl.UnitSalePrice,
                    Amount = srl.LineNetTotal,
                    InvoiceNetTotal = srl.SalesReturn.NetTotal
                })
                .ToListAsync();
            ViewBag.SalesReturnLines = salesReturnLinesList;

            // ===== 13) مرتجعات المشتريات من PurchaseReturnLines =====
            var purchaseReturnsAllTime = _db.PurchaseReturnLines
                .AsNoTracking()
                .Where(prl => prl.ProdId == finalProdId.Value);
            ViewBag.TotalQtyPurchaseReturned = await purchaseReturnsAllTime.SumAsync(prl => prl.Qty);
            ViewBag.TotalAmountPurchaseReturned = await purchaseReturnsAllTime.SumAsync(prl => prl.Qty * prl.UnitCost);

            var purchaseReturnsQuery = _db.PurchaseReturnLines
                .AsNoTracking()
                .Include(prl => prl.PurchaseReturn)
                    .ThenInclude(pr => pr!.Customer)
                .Where(prl => prl.ProdId == finalProdId.Value);
            if (warehouseId.HasValue)
                purchaseReturnsQuery = purchaseReturnsQuery.Where(prl => prl.PurchaseReturn != null && prl.PurchaseReturn.WarehouseId == warehouseId.Value);
            if (from.HasValue)
                purchaseReturnsQuery = purchaseReturnsQuery.Where(prl => prl.PurchaseReturn != null && prl.PurchaseReturn.PRetDate >= from.Value);
            if (to.HasValue)
                purchaseReturnsQuery = purchaseReturnsQuery.Where(prl => prl.PurchaseReturn != null && prl.PurchaseReturn.PRetDate <= to.Value);
            if (finalCustomerId.HasValue)
                purchaseReturnsQuery = purchaseReturnsQuery.Where(prl => prl.PurchaseReturn != null && prl.PurchaseReturn.CustomerId == finalCustomerId.Value);

            var purchaseReturnLinesList = await purchaseReturnsQuery
                .OrderByDescending(prl => prl.PurchaseReturn!.PRetDate)
                .ThenByDescending(prl => prl.PurchaseReturn!.PRetId)
                .ThenBy(prl => prl.LineNo)
                .Select(prl => new
                {
                    Date = prl.PurchaseReturn!.PRetDate,
                    DocNo = prl.PurchaseReturn.PRetId,
                    Supplier = prl.PurchaseReturn.Customer != null ? prl.PurchaseReturn.Customer.CustomerName : "",
                    Qty = prl.Qty,
                    UnitCost = prl.UnitCost,
                    Amount = prl.Qty * prl.UnitCost
                })
                .ToListAsync();
            ViewBag.PurchaseReturnLines = purchaseReturnLinesList;

            return View(product);   // الملف: Views/Products/Show.cshtml
        }

        // =========================================================
        // دالة مساعدة: تحميل قائمة الأصناف للأوتوكومبليت (datalist)
        // =========================================================
        private async Task LoadProductsForAutoCompleteAsync()
        {
            var products = await _db.Products
                .AsNoTracking()
                .OrderBy(p => p.ProdName)
                .Select(p => new
                {
                    Id = p.ProdId,
                    Name = p.ProdName ?? string.Empty,
                    GenericName = p.GenericName ?? string.Empty,
                    Company = p.Company ?? string.Empty,
                    HasQuota = p.HasQuota,
                    PriceRetail = p.PriceRetail
                })
                .ToListAsync();

            ViewBag.ProductsAuto = products;
        }





        // =========================================================
        // 9) دوال مساعدة لتحميل القوائم (الفئات + محلي/مستورد + مجموعات)
        // =========================================================

        // تحميل قائمة الفئات للفورم (Create/Edit)
        private async Task LoadCategoriesDDLAsync(int? selectedCategoryId)
        {
            // جدول Categories يجب أن يحتوي على CategoryId (int) و CategoryName
            var cats = await _db.Categories
                .AsNoTracking()
                .OrderBy(c => c.CategoryName)
                .Select(c => new { c.CategoryId, c.CategoryName })
                .ToListAsync();

            ViewBag.Categories = new SelectList(cats, "CategoryId", "CategoryName", selectedCategoryId);
        }

        // جديد: تحميل مجموعات الأصناف (ProductGroup)
        private async Task LoadProductGroupsDDLAsync(int? selectedGroupId)
        {
            var groups = await _db.ProductGroups
                .AsNoTracking()
                .Where(g => g.IsActive)
                .OrderBy(g => g.Name)
                .Select(g => new { g.ProductGroupId, g.Name })
                .ToListAsync();

            ViewBag.ProductGroups = new SelectList(groups, "ProductGroupId", "Name", selectedGroupId);
        }

        // جديد: تحميل مجموعات البونص (ProductBonusGroup)
        private async Task LoadBonusGroupsDDLAsync(int? selectedBonusGroupId)
        {
            var bonusGroups = await _db.ProductBonusGroups
                .AsNoTracking()
                .Where(g => g.IsActive)
                .OrderBy(g => g.Name)
                .Select(g => new { g.ProductBonusGroupId, g.Name })
                .ToListAsync();

            ViewBag.ProductBonusGroups = new SelectList(bonusGroups, "ProductBonusGroupId", "Name", selectedBonusGroupId);
        }

        // تصنيف الصنف (لخط السير: عادي، ثلاجة، …)
        private async Task LoadClassificationDDLAsync(int? selectedClassificationId)
        {
            var list = await _db.ProductClassifications
                .AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
                .Select(c => new { c.Id, c.Name })
                .ToListAsync();
            ViewBag.ClassificationId = new SelectList(list, "Id", "Name", selectedClassificationId);
        }

        // قائمة ثابتة لقيم Imported (محلي/مستورد فقط)
        private static readonly List<SelectListItem> ImportedOptions = new()
        {
            new SelectListItem { Text = "محلي",   Value = "محلي"   },
            new SelectListItem { Text = "مستورد", Value = "مستورد" },
        };

        // تجهيز قائمة Imported مع تحديد القيمة الحالية
        private List<SelectListItem> GetImportedOptions(string? selected)
        {
            return ImportedOptions
                .Select(o => new SelectListItem
                {
                    Text = o.Text,
                    Value = o.Value,
                    Selected = (o.Value == selected)
                })
                .ToList();
        }

        // =====================
        // GET: Products/Import
        // شاشة اختيار ملف الإكسل
        // =====================
        [HttpGet]
        public async Task<IActionResult> Import()
        {
            var canExcelProducts = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Settings", "ProductsExcelImport"));
            var canOtherBulk = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Products", "Import"));
            if (!canExcelProducts && !canOtherBulk)
                return RedirectToAction("AccessDenied", "Home");
            ViewBag.CanImportProductsExcel = canExcelProducts || canOtherBulk;
            ViewBag.CanImportBulkOther = canOtherBulk;

            ViewBag.Success = TempData["Success"];
            ViewBag.Error = TempData["Error"];
            var governorates = await _db.Governorates.OrderBy(g => g.GovernorateName).Select(g => new { g.GovernorateId, g.GovernorateName }).ToListAsync();
            var districts = await _db.Districts.OrderBy(d => d.DistrictName).Select(d => new { d.DistrictId, d.DistrictName, d.GovernorateId }).ToListAsync();
            ViewBag.Governorates = governorates.Select(g => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem { Value = g.GovernorateId.ToString(), Text = g.GovernorateName }).ToList();
            ViewBag.Districts = districts.Select(d => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem { Value = d.DistrictId.ToString(), Text = d.DistrictName }).ToList();
            var warehouses = await _db.Warehouses.Where(w => w.IsActive).OrderBy(w => w.WarehouseName).Select(w => new { w.WarehouseId, w.WarehouseName }).ToListAsync();
            ViewBag.Warehouses = warehouses.Select(w => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem { Value = w.WarehouseId.ToString(), Text = w.WarehouseName }).ToList();
            return View();
        }

        // =====================
        // POST: Products/Import
        // استيراد الأصناف من ملف إكسل
        // =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Import(IFormFile excelFile, string? importType = null)   // importType: Medicine | Accessory (لتعيين الفئة)
        {
            var canExcelProducts = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Settings", "ProductsExcelImport"));
            var canLegacyImport = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Products", "Import"));
            if (!canExcelProducts && !canLegacyImport)
                return RedirectToAction("AccessDenied", "Home");

            // ===== 1) التأكد من اختيار ملف =====
            if (excelFile == null || excelFile.Length == 0)
            {
                TempData["Error"] = "من فضلك اختر ملف إكسل أولاً.";
                return RedirectToAction(nameof(Import));   // رجوع لنفس الصفحة مع رسالة خطأ
            }

            try
            {
                // ملفات كبيرة (أكثر من 24 ألف صنف): نزيد مهلة أوامر قاعدة البيانات لتجنّب انقطاع الطلب
                try { _db.Database.SetCommandTimeout(300); } catch { /* 300 ثانية = 5 دقائق */ }

                // ===== استيراد آمن (إضافة/تحديث فقط) بدون حذف البيانات القديمة =====
                // ملاحظة: كان النظام سابقاً يحذف الأصناف قبل الاستيراد، وهذا يسبب
                // تعارضات FK مع جداول الحركة (مثل PRLines). لذلك ألغينا الحذف التلقائي.

                // ===== 2) قراءة ملف الإكسل فى الذاكرة =====
                using var stream = new MemoryStream();                 // متغيّر: stream يحمل بيانات الملف
                await excelFile.CopyToAsync(stream);                   // نسخ الملف إلى الـ stream
                stream.Position = 0;                                   // إعادة المؤشّر للبداية

                // إعداد ترخيص EPPlus للاستخدام الشخصى / التجريبى (غير تجارى)
                ExcelPackage.License.SetNonCommercialPersonal("Amr ERP Dev");  // اكتب أى اسم يمثلك هنا

                using var package = new ExcelPackage(stream);          // متغيّر: package يمثل ملف الإكسل
                var sheet = package.Workbook.Worksheets[0];            // متغيّر: أول شيت فى الملف

                if (sheet.Dimension == null)
                {
                    TempData["Error"] = "الملف لا يحتوي على بيانات.";
                    return RedirectToAction(nameof(Import));
                }

                int lastRow = sheet.Dimension.End.Row;    // متغيّر: رقم آخر صف
                int lastCol = sheet.Dimension.End.Column; // متغيّر: رقم آخر عمود

                // ===== 3) خريطة الهيدر — أعمدة ثابتة فقط (أي عمود إضافي في الإكسل يُتجاهل) =====
                var fixedProductCols = new HashSet<string>(ExcelImportColumns.Products, StringComparer.OrdinalIgnoreCase);
                var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int col = 1; col <= lastCol; col++)
                {
                    var headerText = sheet.Cells[1, col].Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(headerText) && fixedProductCols.Contains(headerText) && !headers.ContainsKey(headerText))
                        headers[headerText] = col;
                }

                // ===== 4) عمود اسم الصنف مطلوب (بالإنجليزية أو العربية) =====
                int nameCol = -1;
                foreach (var key in new[] { "ProdName", "الصنف", "اسم الصنف" })
                {
                    if (headers.TryGetValue(key, out var col)) { nameCol = col; break; }
                }
                if (nameCol <= 0)
                {
                    TempData["Error"] = "الملف يجب أن يحتوي على عمود اسم الصنف (ProdName أو الصنف أو اسم الصنف).";
                    return RedirectToAction(nameof(Import));
                }
                // عمود الكود (اختياري): إن وُجد وكان رقماً موجباً يُستخدم كـ ProdId خلال الاستيراد فقط — لا نعبّئ كود الإكسل
                int? codeCol = GetCol(headers, "اسم الكود", "كود الصنف", "ProdId", "Code", "الكود");

                // دالة مساعدة: قراءة نص من عمود بالاسم الإنجليزي أو العربي
                string? GetCell(string en, string ar, int r)
                {
                    if (headers.TryGetValue(en, out var c)) return sheet.Cells[r, c].Text?.Trim();
                    if (headers.TryGetValue(ar, out c)) return sheet.Cells[r, c].Text?.Trim();
                    return null;
                }
                // قراءة نص من عمود بأي من الأسماء الممكنة (مثلاً الشركة/الشركه، التصنيف/التصنيه)
                string? GetCellByKeys(int r, params string[] keys)
                {
                    foreach (var k in keys)
                        if (!string.IsNullOrEmpty(k) && headers.TryGetValue(k, out var c))
                            return sheet.Cells[r, c].Text?.Trim();
                    return null;
                }
                decimal GetCellDecimal(string en, string ar, int r)
                {
                    var t = GetCell(en, ar, r);
                    if (string.IsNullOrWhiteSpace(t)) return 0m;
                    if (decimal.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return v;
                    if (decimal.TryParse(t, NumberStyles.Any, CultureInfo.GetCultureInfo("ar-EG"), out v)) return v;
                    return 0m;
                }
                // سعر الجمهور: عمود الإكسل قد يكون "سعر الجمهور" أو "PriceRetail" أو "سعر ج" أو "سعرج"
                decimal GetPriceRetail(int r)
                {
                    var v = GetCellDecimal("سعر الجمهور", "PriceRetail", r);
                    if (v != 0m) return v;
                    v = GetCellDecimal("PriceRetail", "سعر ج", r);
                    if (v != 0m) return v;
                    return GetCellDecimal("PriceRetail", "سعرج", r);
                }

                int? GetCellIntOptional(int r, params string[] keys)
                {
                    foreach (var k in keys)
                    {
                        if (string.IsNullOrEmpty(k) || !headers.TryGetValue(k, out var c)) continue;
                        var t = sheet.Cells[r, c].Text?.Trim();
                        if (string.IsNullOrWhiteSpace(t)) continue;
                        if (int.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) && v >= 0) return v;
                        if (int.TryParse(t, NumberStyles.Any, CultureInfo.GetCultureInfo("ar-EG"), out v) && v >= 0) return v;
                    }
                    return null;
                }

                // ===== 5) قائمة المخازن للربط بالاسم من الإكسل =====
                var warehousesByName = await _db.Warehouses.AsNoTracking()
                    .Where(w => w.IsActive)
                    .ToDictionaryAsync(w => w.WarehouseName.Trim(), w => w.WarehouseId, StringComparer.OrdinalIgnoreCase);

                // قائمة التصنيفات للربط بالاسم (مع قبول تاء مربوطة أو هاء في الاسم)
                var classificationsByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in await _db.ProductClassifications.AsNoTracking().Where(x => x.IsActive).ToListAsync())
                {
                    var name = c.Name?.Trim() ?? "";
                    if (string.IsNullOrEmpty(name)) continue;
                    classificationsByName[name] = c.Id;
                    var nameNorm = name.Replace('\u0629', '\u0647'); // تاء مربوطة → هاء
                    if (nameNorm != name) classificationsByName[nameNorm] = c.Id;
                }

                // تعيين الفئة مرة واحدة إن وُجد (دواء / اكسسوار)
                int? categoryIdForImport = null;
                if (!string.IsNullOrWhiteSpace(importType))
                {
                    var categoryName = importType.Equals("Medicine", StringComparison.OrdinalIgnoreCase) ? "دواء"
                        : importType.Equals("Accessory", StringComparison.OrdinalIgnoreCase) ? "اكسسوار" : null;
                    if (!string.IsNullOrEmpty(categoryName))
                    {
                        var cat = await _db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.CategoryName == categoryName);
                        if (cat != null) categoryIdForImport = cat.CategoryId;
                    }
                }

                // ===== 6) استيراد على دفعات (Batch) لدعم ملفات كبيرة (أكثر من 24 ألف صنف) =====
                const int batchSize = 2000;   // حجم الدفعة: تقليل استهلاك الذاكرة وتجنّب مهلة الطلب
                int totalInserted = 0, totalUpdated = 0;
                int totalRowsWithName = 0;   // عدد الصفوط التي تحتوي على اسم (غير فارغ) — للمقارنة مع العدد النهائي

                for (int startRow = 2; startRow <= lastRow; startRow += batchSize)
                {
                    int endRow = Math.Min(startRow + batchSize - 1, lastRow);
                    var namesInBatch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int r = startRow; r <= endRow; r++)
                    {
                        var n = sheet.Cells[r, nameCol].Text?.Trim();
                        if (!string.IsNullOrWhiteSpace(n)) namesInBatch.Add(n);
                    }
                    if (namesInBatch.Count == 0) continue;

                    var existingProducts = await _db.Products
                        .Where(p => p.ProdName != null && namesInBatch.Contains(p.ProdName))
                        .ToListAsync();
                    var existingByName = existingProducts.ToDictionary(p => p.ProdName!.Trim(), p => p, StringComparer.OrdinalIgnoreCase);
                    var usedProdIds = existingProducts.Select(p => p.ProdId).ToHashSet();
                    var productsToInsert = new List<Product>();

                    for (int row = startRow; row <= endRow; row++)
                    {
                        string prodName = sheet.Cells[row, nameCol].Text?.Trim() ?? "";
                        if (string.IsNullOrWhiteSpace(prodName)) continue;
                        totalRowsWithName++;

                        int? warehouseId = null;
                        var whIdText = GetCell("WarehouseId", "كود المخزن", row);
                        if (!string.IsNullOrWhiteSpace(whIdText) && int.TryParse(whIdText, NumberStyles.Any, CultureInfo.InvariantCulture, out var whId))
                            warehouseId = whId;
                        else
                        {
                            var whName = GetCell("WarehouseName", "اسم المخزن", row) ?? GetCell("المخزن", "", row);
                            if (!string.IsNullOrWhiteSpace(whName) && warehousesByName.TryGetValue(whName.Trim(), out var whIdByName))
                                warehouseId = whIdByName;
                        }

                        // حل التصنيف من اسم العمود (مع قبول تاء/هاء)
                        int? classificationId = null;
                        var classificationName = GetCellByKeys(row, "Classification", "التصنيف", "التصنيه");
                        if (!string.IsNullOrWhiteSpace(classificationName))
                        {
                            var cn = classificationName.Trim();
                            if (classificationsByName.TryGetValue(cn, out var cid))
                                classificationId = cid;
                            else if (classificationsByName.TryGetValue(cn.Replace('\u0629', '\u0647'), out cid))
                                classificationId = cid;
                        }

                        if (existingByName.TryGetValue(prodName, out var existing))
                        {
                            existing.PriceRetail = GetPriceRetail(row);
                            existing.GenericName = GetCell("GenericName", "الاسم العلمي", row);
                            existing.Company = GetCellByKeys(row, "Company", "الشركة", "الشركه");
                            existing.ClassificationId = classificationId;
                            existing.Location = GetCell("Location", "الموقع", row);
                            existing.WarehouseId = warehouseId;
                            existing.Description = GetCell("Description", "الوصف", row);
                            existing.DosageForm = GetCell("DosageForm", "الشكل الدوائي", row);
                            existing.Barcode = GetCell("Barcode", "Barcode", row);
                            existing.Imported = GetCell("Imported", "المنشأ", row);
                            existing.CartonQuantity = GetCellIntOptional(row, "CartonQuantity", "كمية الكرتونة", "الكرتونة");
                            existing.PackQuantity = GetCellIntOptional(row, "PackQuantity", "كمية الباكو", "الباكو", "باكو");
                            existing.UpdatedAt = DateTime.UtcNow;
                            if (categoryIdForImport.HasValue) existing.CategoryId = categoryIdForImport.Value;
                            if (existing.ProdId != 0) totalUpdated++;
                        }
                        else
                        {
                            var product = new Product
                            {
                                ProdName = prodName,
                                PriceRetail = GetPriceRetail(row),
                                GenericName = GetCell("GenericName", "الاسم العلمي", row),
                                Company = GetCellByKeys(row, "Company", "الشركة", "الشركه"),
                                ClassificationId = classificationId,
                                Location = GetCell("Location", "الموقع", row),
                                WarehouseId = warehouseId,
                                Description = GetCell("Description", "الوصف", row),
                                DosageForm = GetCell("DosageForm", "الشكل الدوائي", row),
                                Barcode = GetCell("Barcode", "Barcode", row),
                                Imported = GetCell("Imported", "المنشأ", row),
                                CartonQuantity = GetCellIntOptional(row, "CartonQuantity", "كمية الكرتونة", "الكرتونة"),
                                PackQuantity = GetCellIntOptional(row, "PackQuantity", "كمية الباكو", "الباكو", "باكو"),
                                IsActive = true,
                                CreatedAt = DateTime.UtcNow,
                                UpdatedAt = DateTime.UtcNow
                            };
                            if (categoryIdForImport.HasValue) product.CategoryId = categoryIdForImport.Value;
                            // خلال الاستيراد فقط: إن وُجد عمود الكود (اسم الكود / كود الصنف...) رقماً موجباً وغير مكرر يُستخدم كـ ProdId — لا نعبّئ كود الإكسل
                            if (codeCol.HasValue && codeCol.Value > 0)
                            {
                                var codeStr = sheet.Cells[row, codeCol.Value].Text?.Trim();
                                if (!string.IsNullOrWhiteSpace(codeStr) && int.TryParse(codeStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var codeInt) && codeInt > 0 && !usedProdIds.Contains(codeInt))
                                {
                                    product.ProdId = codeInt;
                                    usedProdIds.Add(codeInt);
                                }
                            }
                            productsToInsert.Add(product);
                            existingByName[prodName.Trim()] = product;
                        }
                    }

                    var withIdentity = productsToInsert.Where(p => p.ProdId == 0).ToList();
                    var withExplicitId = productsToInsert.Where(p => p.ProdId > 0).ToList();
                    if (withIdentity.Count > 0)
                        _db.Products.AddRange(withIdentity);
                    if (withExplicitId.Count > 0)
                    {
                        // تشغيل SET ON والـ INSERT وSET OFF على نفس الاتصال (Transaction) حتى لا يفقد الإعداد
                        using var tran = await _db.Database.BeginTransactionAsync();
                        try
                        {
                            await _db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [Products] ON");
                            foreach (var p in withExplicitId)
                            {
                                await _db.Database.ExecuteSqlRawAsync(
                                    "INSERT INTO [Products] (ProdId, ProdName, PriceRetail, ExternalCode, GenericName, Company, ClassificationId, Location, WarehouseId, Description, DosageForm, Barcode, Imported, IsActive, CreatedAt, UpdatedAt, CategoryId, CartonQuantity, PackQuantity) VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12}, {13}, {14}, {15}, {16}, {17}, {18})",
                                    p.ProdId, p.ProdName ?? "", p.PriceRetail, p.ExternalCode, p.GenericName, p.Company, p.ClassificationId, p.Location, p.WarehouseId, p.Description, p.DosageForm, p.Barcode, p.Imported, p.IsActive, p.CreatedAt, p.UpdatedAt, p.CategoryId, p.CartonQuantity, p.PackQuantity);
                            }
                            await _db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [Products] OFF");
                            await tran.CommitAsync();
                        }
                        catch
                        {
                            await tran.RollbackAsync();
                            throw;
                        }
                    }
                    await _db.SaveChangesAsync();
                    totalInserted += productsToInsert.Count;
                }

                if (totalInserted == 0 && totalUpdated == 0)
                {
                    TempData["Error"] = "لم يتم العثور على أي صف صالح للاستيراد.";
                    return RedirectToAction(nameof(Import));
                }

                int totalRowsInFile = lastRow - 1;  // من صف 2 إلى lastRow
                int skippedEmpty = totalRowsInFile - totalRowsWithName;

                var typeLabel = importType == "Medicine" ? " (أصناف دواء)" : importType == "Accessory" ? " (أصناف إكسسوار)" : "";
                var msg = totalUpdated > 0 && totalInserted > 0
                    ? $"تم استيراد {totalInserted} صنفاً جديداً وتحديث {totalUpdated} صنفاً موجوداً{typeLabel}."
                    : totalUpdated > 0
                        ? $"تم تحديث {totalUpdated} صنفاً موجوداً (بدون إضافة جديدة){typeLabel}."
                        : $"تم استيراد {totalInserted} صنفاً بنجاح{typeLabel}.";
                if (skippedEmpty > 0)
                    msg += $" صفوط مُتخطّاة (اسم فارغ): {skippedEmpty}.";
                // عندما يكون عدد الصفوط (ذات الاسم) أكبر من ما نُحصيه (إضافة + تحديث من القاعدة) فالفارق بسبب تكرار الاسم في الملف
                if (totalRowsWithName > totalInserted + totalUpdated)
                    msg += " ملاحظة: عدد الصفوط المعالجة في الملف أكبر من العدد الظاهر لأن نفس اسم الصنف قد تكرر في أكثر من صف — البرنامج يحتفظ بسجل واحد لكل اسم (آخر ظهور في الملف).";
                _productCache.ClearProductsCache(); // إبطال كاش البحث بعد الاستيراد/التحديث الجماعي
                TempData["Success"] = msg;
                return RedirectToAction(nameof(Import));       // رجوع لنفس الصفحة مع رسالة نجاح
            }
            catch (Exception ex)
            {
                // أثناء التجربة هنعرض الرسالة الداخلية علشان نفهم الخطأ
                TempData["Error"] = "حدث خطأ أثناء استيراد الأصناف: " +
                                    (ex.InnerException?.Message ?? ex.Message);

                return RedirectToAction(nameof(Import));
            }
        }

        // =====================
        // استيراد أرصدة الأصناف (رصيد أول المدة) — أعمدة ثابتة فقط؛ الزيادة تُتجاهل
        // =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Products.Import")]
        public async Task<IActionResult> ImportProductOpeningBalance(IFormFile excelFile)
        {
            if (excelFile == null || excelFile.Length == 0)
            {
                TempData["Error"] = "من فضلك اختر ملف أرصدة الأصناف (Excel) أولاً.";
                return RedirectToAction(nameof(Import));
            }

            try
            {
                using var stream = new MemoryStream();
                await excelFile.CopyToAsync(stream);
                stream.Position = 0;
                ExcelPackage.License.SetNonCommercialPersonal("Amr ERP Dev");

                using var package = new ExcelPackage(stream);
                var sheet = package.Workbook.Worksheets[0];
                if (sheet.Dimension == null)
                {
                    TempData["Error"] = "الملف لا يحتوي على بيانات.";
                    return RedirectToAction(nameof(Import));
                }

                int lastRow = sheet.Dimension.End.Row;
                int lastCol = sheet.Dimension.End.Column;

                // بناء خريطة الهيدر للأعمدة الثابتة فقط — أي عمود غير معرّف يُتجاهل
                var fixedNames = new HashSet<string>(ExcelImportColumns.ProductOpeningBalance, StringComparer.OrdinalIgnoreCase);
                var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int col = 1; col <= lastCol; col++)
                {
                    var headerText = sheet.Cells[1, col].Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(headerText) && fixedNames.Contains(headerText) && !headers.ContainsKey(headerText))
                        headers[headerText] = col;
                }

                // مطلوب: الصنف (كود أو اسم) + الكمية — عمود الكود: اسم الكود / كود الصنف...؛ عمود الاسم: ProdName ثم الصنف ثم اسم الصنف
                int? colProdId = GetCol(headers, "اسم الكود", "كود الصنف", "ProdId");
                int? colProdName = GetCol(headers, "ProdName", "الصنف", "اسم الصنف");
                int prodCol = colProdId ?? colProdName ?? -1;
                if (prodCol <= 0)
                {
                    TempData["Error"] = "الملف يجب أن يحتوي على عمود الصنف (كود الصنف أو ProdId أو الصنف أو اسم الصنف).";
                    return RedirectToAction(nameof(Import));
                }

                int? colQty = GetCol(headers, "الكمية", "QtyIn", "Quantity");
                if (!colQty.HasValue || colQty <= 0)
                {
                    TempData["Error"] = "الملف يجب أن يحتوي على عمود الكمية (الكمية أو QtyIn أو Quantity).";
                    return RedirectToAction(nameof(Import));
                }

                int? colDiscount = GetCol(headers, "الخصم المرجح", "PurchaseDiscount", "المرجح");
                int? colTotalCost = GetCol(headers, "إجمالي التكلفة", "اجمالي التكلفة", "إجمالي تكلفة", "TotalCost");
                int? colUnitCost = GetCol(headers, "تكلفة العلبة", "تكلفة الوحدة", "UnitCost");
                int? colPriceRetail = GetCol(headers, "سعر الجمهور", "PriceRetail", "PriceRetailBatch");
                int? colWarehouse = GetCol(headers, "كود المخزن", "WarehouseId", "المخزن", "اسم المخزن", "WarehouseName");
                int? colBatchNo = GetCol(headers, "التشغيلة", "التشغيله", "رقم التشغيلة", "رقم التشغيله", "BatchNo", "Batch");
                int? colExpiry = GetCol(headers, "الصلاحية", "الصلاحيه", "Expiry", "تاريخ الصلاحية", "تاريخ الصلاحيه", "Expire");

                var defaultWarehouse = await _db.Warehouses.Where(w => w.IsActive).OrderBy(w => w.WarehouseId).Select(w => w.WarehouseId).FirstOrDefaultAsync();
                if (defaultWarehouse == 0 && !colWarehouse.HasValue)
                {
                    TempData["Error"] = "لا يوجد مخزن فعّال. أضف مخزناً أو ضع عمود كود المخزن في الملف.";
                    return RedirectToAction(nameof(Import));
                }

                var warehousesByName = await _db.Warehouses.AsNoTracking()
                    .Where(w => w.IsActive)
                    .ToDictionaryAsync(w => w.WarehouseName.Trim(), w => w.WarehouseId, StringComparer.OrdinalIgnoreCase);

                var productById = await _db.Products.AsNoTracking().ToDictionaryAsync(p => p.ProdId, p => p);
                var productByName = await _db.Products.AsNoTracking().Where(p => p.ProdName != null).ToDictionaryAsync(p => p.ProdName!.Trim(), p => p, StringComparer.OrdinalIgnoreCase);

                var entries = new List<StockLedger>();
                var batchCache = new Dictionary<string, Batch>();
                var tranDate = DateTime.UtcNow.Date;
                var skippedNoProduct = 0;
                var skippedNoQty = 0;
                var skippedBatchIncomplete = 0;

                for (int row = 2; row <= lastRow; row++)
                {
                    var prodVal = sheet.Cells[row, prodCol].Text?.Trim();
                    if (string.IsNullOrWhiteSpace(prodVal)) continue;

                    int prodId;
                    if (int.TryParse(prodVal, out var id) && productById.TryGetValue(id, out var pById))
                        prodId = pById.ProdId;
                    else if (productByName.TryGetValue(prodVal, out var pByName))
                        prodId = pByName.ProdId;
                    else
                    {
                        skippedNoProduct++;
                        continue;
                    }

                    int qty = 0;
                    var qtyCell = sheet.Cells[row, colQty!.Value];
                    var qtyDecimal = GetDecimalFromCell(qtyCell);
                    if (qtyDecimal > 0m) qty = (int)Math.Round(qtyDecimal);
                    if (qty <= 0)
                    {
                        skippedNoQty++;
                        continue;
                    }

                    decimal totalCost = 0m;
                    if (colTotalCost.HasValue)
                        totalCost = GetDecimalFromCell(sheet.Cells[row, colTotalCost.Value]);

                    decimal purchaseDiscount = 0m;
                    if (colDiscount.HasValue)
                        purchaseDiscount = GetDecimalFromCell(sheet.Cells[row, colDiscount.Value]);

                    int warehouseId = defaultWarehouse;
                    if (colWarehouse.HasValue)
                    {
                        var whText = sheet.Cells[row, colWarehouse.Value].Text?.Trim();
                        if (!string.IsNullOrWhiteSpace(whText))
                        {
                            if (int.TryParse(whText, NumberStyles.Any, CultureInfo.InvariantCulture, out var whId))
                                warehouseId = whId;
                            else if (warehousesByName.TryGetValue(whText, out var whIdByName))
                                warehouseId = whIdByName;
                        }
                    }

                    decimal unitCost = 0m;
                    if (colUnitCost.HasValue)
                        unitCost = GetDecimalFromCell(sheet.Cells[row, colUnitCost.Value]);
                    if (unitCost == 0m && qty > 0 && totalCost != 0m)
                        unitCost = totalCost / qty;

                    decimal? priceRetailBatch = null;
                    if (colPriceRetail.HasValue)
                    {
                        var pr = GetDecimalFromCell(sheet.Cells[row, colPriceRetail.Value]);
                        if (pr != 0m) priceRetailBatch = pr;
                    }
                    if (!priceRetailBatch.HasValue && productById.TryGetValue(prodId, out var prodRef) && prodRef.PriceRetail > 0)
                        priceRetailBatch = prodRef.PriceRetail;

                    string? batchNoText = null;
                    DateTime? expiryDate = null;
                    if (colBatchNo.HasValue) batchNoText = sheet.Cells[row, colBatchNo.Value].Text?.Trim();
                    if (colExpiry.HasValue) expiryDate = GetDateFromExcelCell(sheet.Cells[row, colExpiry.Value]);

                    Batch? batchEntity = null;
                    string? batchNoForLedger = null;
                    DateTime? expiryForLedger = null;
                    if (!string.IsNullOrWhiteSpace(batchNoText) && expiryDate.HasValue)
                    {
                        batchEntity = await GetOrCreateBatchForOpeningAsync(
                            batchCache, prodId, batchNoText, expiryDate.Value, unitCost, priceRetailBatch, purchaseDiscount);
                        batchNoForLedger = batchNoText.Length > 50 ? batchNoText.Substring(0, 50) : batchNoText;
                        expiryForLedger = expiryDate.Value.Date;
                    }
                    else if (!string.IsNullOrWhiteSpace(batchNoText) && !expiryDate.HasValue ||
                             string.IsNullOrWhiteSpace(batchNoText) && expiryDate.HasValue)
                    {
                        skippedBatchIncomplete++;
                    }

                    entries.Add(new StockLedger
                    {
                        TranDate = tranDate,
                        WarehouseId = warehouseId,
                        ProdId = prodId,
                        BatchNo = batchNoForLedger,
                        Expiry = expiryForLedger,
                        Batch = batchEntity,
                        QtyIn = qty,
                        QtyOut = 0,
                        UnitCost = unitCost,
                        TotalCost = totalCost,
                        PurchaseDiscount = purchaseDiscount,
                        PriceRetailBatch = priceRetailBatch,
                        RemainingQty = qty,
                        SourceType = "Opening",
                        SourceId = 0,
                        SourceLine = row - 1,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                // توحيد تكلفة الوحدة من إجمالي التكلفة إن وُجد إجمالي وكمية (ملفات منقولة قد تترك تكلفة العلبة صفراً)
                foreach (var e in entries)
                {
                    if (e.QtyIn > 0 && e.UnitCost == 0m && e.TotalCost.HasValue && e.TotalCost.Value != 0m)
                        e.UnitCost = Math.Round(e.TotalCost.Value / e.QtyIn, 4, MidpointRounding.AwayFromZero);
                }

                if (entries.Count == 0)
                {
                    TempData["Error"] = "لم يتم العثور على أي صف صالح (صنف موجود + كمية > 0).";
                    return RedirectToAction(nameof(Import));
                }

                // مسح القديم كله ثم استيراد الجديد: حذف كل قيود رصيد أول المدة (Opening) من دفتر الحركة
                var allOpening = await _db.StockLedger.Where(sl => sl.SourceType == "Opening").ToListAsync();
                if (allOpening.Count > 0)
                {
                    _db.StockLedger.RemoveRange(allOpening);
                    await _db.SaveChangesAsync();
                }

                await SyncStockBatchesFromLedgerAsync();

                _db.StockLedger.AddRange(entries);
                await _db.SaveChangesAsync();

                await SyncStockBatchesFromLedgerAsync();

                var msg = allOpening.Count > 0
                    ? $"تم مسح {allOpening.Count} قيد رصيد قديم واستيراد رصيد أول المدة لـ {entries.Count} صفاً."
                    : $"تم استيراد رصيد أول المدة لـ {entries.Count} صفاً بنجاح.";
                msg += " تم تحديث أرصدة التشغيلات (Stock_Batches) لتطابق دفتر الحركة.";
                if (skippedNoProduct > 0 || skippedNoQty > 0 || skippedBatchIncomplete > 0)
                {
                    var parts = new List<string>();
                    if (skippedNoProduct > 0) parts.Add($"{skippedNoProduct} صنف غير موجود في القائمة");
                    if (skippedNoQty > 0) parts.Add($"{skippedNoQty} كمية صفر أو فارغة");
                    if (skippedBatchIncomplete > 0) parts.Add($"{skippedBatchIncomplete} صفاً بها تشغيلة أو صلاحية فقط (بدون الآخر) — تُجاهل ربط التشغيلة");
                    msg += " (تم تخطي: " + string.Join("، ", parts) + ").";
                }
                TempData["Success"] = msg;
                return RedirectToAction(nameof(Import));
            }
            catch (Exception ex)
            {
                TempData["Error"] = "خطأ أثناء استيراد أرصدة الأصناف: " + (ex.InnerException?.Message ?? ex.Message);
                return RedirectToAction(nameof(Import));
            }
        }

        /// <summary>
        /// تحويل أرصدة الأصناف إلى المخزن المعيّن لكل صنف (Product.WarehouseId).
        /// للأصناف التي معها مخزن معيّن (مثل الاكسسوار) لكن رصيدها الفعلي في مخزن آخر — ينقل الرصيد إلى المخزن المعيّن دون إعادة استيراد.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Products.Import")]
        public async Task<IActionResult> SyncStockToProductWarehouse(int? warehouseId)
        {
            var productsQuery = _db.Products.AsNoTracking()
                .Where(p => p.WarehouseId != null);
            if (warehouseId.HasValue && warehouseId.Value > 0)
                productsQuery = productsQuery.Where(p => p.WarehouseId == warehouseId.Value);

            var products = await productsQuery
                .Select(p => new { p.ProdId, p.WarehouseId })
                .ToListAsync();

            if (products.Count == 0)
            {
                TempData["Error"] = warehouseId.HasValue
                    ? "لا توجد أصناف معيّن لها هذا المخزن (المخزن الافتراضي للصنف)."
                    : "لا توجد أصناف لها مخزن معيّن (Product.WarehouseId).";
                return RedirectToAction(nameof(Import));
            }

            int totalMoved = 0;
            var tranDate = DateTime.UtcNow;
            const string sourceType = "SyncToProductWarehouse";
            const int sourceId = 0;

            foreach (var prod in products)
            {
                int targetWh = prod.WarehouseId!.Value;
                var sourceRows = await _db.StockLedger
                    .Where(sl => sl.ProdId == prod.ProdId && sl.WarehouseId != targetWh && sl.QtyIn > 0 && (sl.RemainingQty ?? 0) > 0)
                    .OrderBy(sl => sl.TranDate)
                    .ToListAsync();

                int lineNo = 0;
                foreach (var row in sourceRows)
                {
                    int take = row.RemainingQty ?? 0;
                    if (take <= 0) continue;

                    _db.StockLedger.Add(new StockLedger
                    {
                        TranDate = tranDate,
                        WarehouseId = row.WarehouseId,
                        ProdId = row.ProdId,
                        BatchNo = row.BatchNo,
                        BatchId = row.BatchId,
                        Expiry = row.Expiry,
                        QtyIn = 0,
                        QtyOut = take,
                        UnitCost = row.UnitCost,
                        RemainingQty = null,
                        SourceType = sourceType,
                        SourceId = sourceId,
                        SourceLine = lineNo,
                        CounterWarehouseId = targetWh,
                        Note = "تحويل رصيد إلى المخزن المعيّن للصنف",
                        CreatedAt = tranDate
                    });

                    row.RemainingQty = 0;
                    _db.StockLedger.Update(row);

                    // نسخ تكلفة الوحدة والخصم وسعر الجمهور حتى يظهر الخصم المرجح وتكلفة العلبة في تقرير أرصدة الأصناف
                    var lineTotalCost = (decimal)take * row.UnitCost;
                    var lineTotalCostNullable = row.TotalCost.HasValue && row.TotalCost.Value != 0 ? (decimal?)lineTotalCost : null;
                    _db.StockLedger.Add(new StockLedger
                    {
                        TranDate = tranDate,
                        WarehouseId = targetWh,
                        ProdId = row.ProdId,
                        BatchNo = row.BatchNo,
                        BatchId = row.BatchId,
                        Expiry = row.Expiry,
                        QtyIn = take,
                        QtyOut = 0,
                        UnitCost = row.UnitCost,
                        TotalCost = lineTotalCostNullable,
                        PurchaseDiscount = row.PurchaseDiscount,
                        PriceRetailBatch = row.PriceRetailBatch,
                        RemainingQty = take,
                        SourceType = sourceType,
                        SourceId = sourceId,
                        SourceLine = lineNo,
                        CounterWarehouseId = row.WarehouseId,
                        Note = "تحويل رصيد إلى المخزن المعيّن للصنف",
                        CreatedAt = tranDate
                    });

                    totalMoved += take;
                    lineNo++;
                }
            }

            await _db.SaveChangesAsync();

            var whName = warehouseId.HasValue && warehouseId.Value > 0
                ? (await _db.Warehouses.FindAsync(warehouseId.Value))?.WarehouseName ?? warehouseId.ToString()
                : "المخازن المعيّنة";
            TempData["Success"] = $"تم تحويل الرصيد إلى المخزن المعيّن للصنف: {totalMoved} وحدة من {products.Count} صنفاً (باتجاه {whName}).";
            return RedirectToAction(nameof(Import));
        }

        private static int? GetCol(Dictionary<string, int> headers, params string[] names)
        {
            foreach (var n in names)
                if (headers.TryGetValue(n, out var c)) return c;
            return null;
        }

        /// <summary>قراءة قيمة رقمية من خلية إكسل — نفضّل القيمة المخزنة (Value) لتجنّب خطأ التنسيق (مثل 163 ألف تُستورد 72 مليون).</summary>
        private static decimal GetDecimalFromCell(ExcelRangeBase cell)
        {
            if (cell?.Value == null) return 0m;
            var v = cell.Value;
            if (v is double d) return (decimal)d;
            if (v is decimal dec) return dec;
            if (v is int i) return (decimal)i;
            if (v is long l) return (decimal)l;
            if (v is float f) return (decimal)f;
            var text = cell.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return 0m;
            text = text.Replace(" ", "");
            int lastComma = text.LastIndexOf(',');
            if (lastComma >= 0)
            {
                string after = text.Substring(lastComma + 1);
                if (after.Length >= 2 && after.Length <= 3 && after.All(char.IsDigit))
                    text = text.Substring(0, lastComma) + "." + after;
                else
                    text = text.Replace(",", "");
            }
            else
                text = text.Replace(",", "");
            decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var result);
            return result;
        }

        /// <summary>قراءة تاريخ من خلية إكسل (قيمة تاريخ أو رقم سيريال أو نص).</summary>
        private static DateTime? GetDateFromExcelCell(ExcelRangeBase cell)
        {
            if (cell?.Value == null) return null;
            var v = cell.Value;
            if (v is DateTime dt) return dt.Date;
            if (v is double d) { try { return DateTime.FromOADate(d).Date; } catch { /* ignore */ } }
            var text = cell.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt2)) return dt2.Date;
            if (DateTime.TryParse(text, CultureInfo.GetCultureInfo("ar-EG"), DateTimeStyles.None, out dt2)) return dt2.Date;
            return null;
        }

        /// <summary>مزامنة Stock_Batches من StockLedger (نفس منطق تقارير أرصدة الأصناف).</summary>
        private async Task SyncStockBatchesFromLedgerAsync()
        {
            var ledgerBalances = await _db.StockLedger
                .AsNoTracking()
                .GroupBy(sl => new { sl.WarehouseId, sl.ProdId, BatchNo = sl.BatchNo ?? "", Expiry = sl.Expiry.HasValue ? sl.Expiry.Value.Date : (DateTime?)null })
                .Select(g => new
                {
                    g.Key.WarehouseId,
                    g.Key.ProdId,
                    g.Key.BatchNo,
                    g.Key.Expiry,
                    Qty = g.Sum(sl => sl.QtyIn - sl.QtyOut)
                })
                .ToListAsync();

            foreach (var lb in ledgerBalances)
            {
                int qtyToSet = Math.Max(0, lb.Qty);
                var sb = await _db.StockBatches.FirstOrDefaultAsync(x =>
                    x.WarehouseId == lb.WarehouseId && x.ProdId == lb.ProdId &&
                    (x.BatchNo ?? "").Trim() == (lb.BatchNo ?? "").Trim() &&
                    ((x.Expiry.HasValue ? x.Expiry.Value.Date : (DateTime?)null) == lb.Expiry));
                if (sb != null)
                {
                    if (sb.QtyOnHand != qtyToSet)
                    {
                        sb.QtyOnHand = qtyToSet;
                        sb.UpdatedAt = DateTime.UtcNow;
                        sb.Note = $"مزامنة من StockLedger {DateTime.UtcNow:yyyy-MM-dd HH:mm}";
                    }
                }
                else if (qtyToSet > 0)
                {
                    _db.StockBatches.Add(new StockBatch
                    {
                        WarehouseId = lb.WarehouseId,
                        ProdId = lb.ProdId,
                        BatchNo = lb.BatchNo ?? "",
                        Expiry = lb.Expiry,
                        QtyOnHand = qtyToSet,
                        UpdatedAt = DateTime.UtcNow,
                        Note = $"مزامنة من StockLedger {DateTime.UtcNow:yyyy-MM-dd HH:mm}"
                    });
                }
            }

            var allBatches = await _db.StockBatches.Where(sb => sb.QtyOnHand > 0).ToListAsync();
            foreach (var sb in allBatches)
            {
                var key = (sb.WarehouseId, sb.ProdId, BatchNo: (sb.BatchNo ?? "").Trim(), Expiry: sb.Expiry.HasValue ? sb.Expiry.Value.Date : (DateTime?)null);
                if (!ledgerBalances.Any(lb => lb.WarehouseId == key.WarehouseId && lb.ProdId == key.ProdId &&
                    (lb.BatchNo ?? "").Trim() == key.BatchNo && ((lb.Expiry.HasValue ? lb.Expiry.Value.Date : (DateTime?)null) == key.Expiry)))
                {
                    sb.QtyOnHand = 0;
                    sb.UpdatedAt = DateTime.UtcNow;
                    sb.Note = $"مزامنة: تصفير (لا رصيد في Ledger) {DateTime.UtcNow:yyyy-MM-dd HH:mm}";
                }
            }

            await _db.SaveChangesAsync();
        }

        /// <summary>تشغيلة افتتاحية: جدول Batch + ربط السطر في StockLedger.</summary>
        private async Task<Batch> GetOrCreateBatchForOpeningAsync(
            Dictionary<string, Batch> cache,
            int prodId,
            string batchNo,
            DateTime expiry,
            decimal unitCost,
            decimal? priceRetailBatch,
            decimal purchaseDiscountPct)
        {
            var bn = batchNo.Trim();
            if (bn.Length > 50) bn = bn.Substring(0, 50);
            var exp = expiry.Date;
            var key = $"{prodId}\u001f{bn}\u001f{exp:yyyy-MM-dd}";
            if (cache.TryGetValue(key, out var cached))
                return cached;

            var existing = await _db.Batches.FirstOrDefaultAsync(b =>
                b.ProdId == prodId && b.BatchNo == bn && b.Expiry.Date == exp);
            if (existing != null)
            {
                cache[key] = existing;
                return existing;
            }

            var batch = new Batch
            {
                ProdId = prodId,
                BatchNo = bn,
                Expiry = exp,
                PriceRetailBatch = priceRetailBatch,
                UnitCostDefault = unitCost > 0 ? unitCost : null,
                PurchaseDiscountPct = purchaseDiscountPct == 0 ? null : purchaseDiscountPct,
                EntryDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };
            _db.Batches.Add(batch);
            cache[key] = batch;
            return batch;
        }

        // =====================
        // استيراد العملاء من إكسل — مسلسل → كود الإكسل (ExternalCode)، والاسم مطلوب (Upsert بالاسم)
        // =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Products.Import")]
        public async Task<IActionResult> ImportCustomers(IFormFile? excelFile, int? governorateId, int? districtId)
        {
            if (excelFile == null || excelFile.Length == 0)
            {
                TempData["Error"] = "من فضلك اختر ملف قائمة العملاء (Excel) أولاً.";
                return RedirectToAction(nameof(Import));
            }

            try
            {
                // استيراد آمن للعملاء: إضافة/تحديث فقط بدون حذف أي بيانات حركة أو أرصدة.

                using var stream = new MemoryStream();
                await excelFile.CopyToAsync(stream);
                stream.Position = 0;
                ExcelPackage.License.SetNonCommercialPersonal("Amr ERP Dev");
                using var package = new ExcelPackage(stream);
                var sheet = package.Workbook.Worksheets[0];
                if (sheet.Dimension == null)
                {
                    TempData["Error"] = "الملف لا يحتوي على بيانات.";
                    return RedirectToAction(nameof(Import));
                }

                int lastRow = sheet.Dimension.End.Row;
                int lastCol = sheet.Dimension.End.Column;
                var fixedNames = new HashSet<string>(ExcelImportColumns.Customers, StringComparer.OrdinalIgnoreCase);
                var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int col = 1; col <= lastCol; col++)
                {
                    var headerText = sheet.Cells[1, col].Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(headerText) && fixedNames.Contains(headerText) && !headers.ContainsKey(headerText))
                        headers[headerText] = col;
                }

                int? colName = GetCol(headers, "الاسم", "اسم العميل", "CustomerName");
                if (!colName.HasValue || colName <= 0)
                {
                    TempData["Error"] = "الملف يجب أن يحتوي على عمود الاسم (الاسم أو اسم العميل أو CustomerName).";
                    return RedirectToAction(nameof(Import));
                }

                // عمود الكود: نفس ترتيب استيراد الأصناف — إن كان رقماً يُستخدم كـ CustomerId
                int? colSerial = GetCol(headers, "اسم الكود", "كود العميل", "CustomerId", "مسلسل", "الرقم", "كود", "Code", "الكود");
                int? colPhone = GetCol(headers, "التليفون", "الهاتف", "Phone", "Phone1");
                int? colDate = GetCol(headers, "التاريخ", "Date", "EntryDate");
                int? colResponsible = GetCol(headers, "اسم المسؤول", "اسم المسئول", "OrderContactName");
                int? colNotes = GetCol(headers, "ملاحظات", "Notes");
                int? colRegion = GetCol(headers, "المنطقة", "Region", "RegionName", "Area", "AreaName");
                int? colAddress = GetCol(headers, "العنوان", "Address");
                int? colSegment = GetCol(headers, "الشريحه", "الشريحة", "Segment");
                int? colBalance = GetCol(headers, "الأرصدة", "Balance", "CurrentBalance");
                int? colRep = GetCol(headers, "المندوب", "UserId", "SalesRep");
                int? colTaxId = GetCol(headers, "رقم البطاقة الضريبية", "الرقم القومى", "رقم البطاقة الضريبية / الرقم القومى", "TaxId", "NationalId");
                int? colRecordNum = GetCol(headers, "رقم السجل", "RecordNumber");
                int? colLicense = GetCol(headers, "رقم الرخصة", "LicenseNumber");

                string GetCell(int row, int? col)
                {
                    if (!col.HasValue || col <= 0) return "";
                    var v = sheet.Cells[row, col.Value].Value;
                    if (v is double d) return d.ToString(CultureInfo.InvariantCulture);
                    return sheet.Cells[row, col.Value].Text?.Trim() ?? "";
                }

                decimal GetCellDecimal(int row, int? col)
                {
                    var t = GetCell(row, col);
                    if (string.IsNullOrWhiteSpace(t)) return 0m;
                    if (decimal.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return v;
                    if (decimal.TryParse(t, NumberStyles.Any, CultureInfo.GetCultureInfo("ar-EG"), out v)) return v;
                    return 0m;
                }

                DateTime? GetCellDate(int row, int? col)
                {
                    var t = GetCell(row, col);
                    if (string.IsNullOrWhiteSpace(t)) return null;
                    if (DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) return dt;
                    if (DateTime.TryParse(t, CultureInfo.GetCultureInfo("ar-EG"), DateTimeStyles.None, out dt)) return dt;
                    return null;
                }

                var areasByName = await _db.Areas.AsNoTracking().Where(a => a.AreaName != null).ToDictionaryAsync(a => a.AreaName!.Trim(), a => a.AreaId, StringComparer.OrdinalIgnoreCase);
                int regionsCreated = 0;
                if (colRegion.HasValue && colRegion.Value > 0)
                {
                    var namesToCreate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int scanRow = 2; scanRow <= lastRow; scanRow++)
                    {
                        var rn = GetCell(scanRow, colRegion).Trim();
                        if (string.IsNullOrWhiteSpace(rn)) continue;
                        if (!areasByName.ContainsKey(rn))
                            namesToCreate.Add(rn);
                    }
                    if (namesToCreate.Count > 0)
                    {
                        var (ok, gid, did, err) = await ResolveGovernorateDistrictForImportAsync(governorateId, districtId);
                        if (!ok)
                        {
                            TempData["Error"] = err;
                            return RedirectToAction(nameof(Import));
                        }
                        var nowUtc = DateTime.UtcNow;
                        var createdAreas = new List<Area>();
                        foreach (var n in namesToCreate)
                        {
                            var a = new Area
                            {
                                AreaName = n,
                                GovernorateId = gid,
                                DistrictId = did,
                                IsActive = true,
                                CreatedAt = nowUtc,
                                UpdatedAt = nowUtc
                            };
                            _db.Areas.Add(a);
                            createdAreas.Add(a);
                        }
                        await _db.SaveChangesAsync();
                        foreach (var a in createdAreas)
                            areasByName[a.AreaName.Trim()] = a.AreaId;
                        regionsCreated = createdAreas.Count;
                    }
                }

                var usersByName = await _db.Users.AsNoTracking()
                    .Where(u => u.DisplayName != null || u.UserName != null)
                    .ToListAsync();
                var userByDisplayName = usersByName
                    .Where(u => !string.IsNullOrWhiteSpace(u.DisplayName))
                    .ToDictionary(u => u.DisplayName!.Trim(), u => u.UserId, StringComparer.OrdinalIgnoreCase);
                var userByUserName = usersByName
                    .Where(u => !string.IsNullOrWhiteSpace(u.UserName))
                    .ToDictionary(u => u.UserName!.Trim(), u => u.UserId, StringComparer.OrdinalIgnoreCase);

                int inserted = 0, updated = 0;
                var existingByName = await _db.Customers.Where(c => c.CustomerName != null).ToDictionaryAsync(c => c.CustomerName!.Trim(), c => c, StringComparer.OrdinalIgnoreCase);
                var usedCustomerIds = existingByName.Values.Select(c => c.CustomerId).ToHashSet();
                var newCustomersList = new List<Customer>();

                for (int row = 2; row <= lastRow; row++)
                {
                    var name = GetCell(row, colName).Trim();
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var externalCode = colSerial.HasValue ? GetCell(row, colSerial).Trim() : null;
                    var phone1 = colPhone.HasValue ? GetCell(row, colPhone).Trim() : null;
                    var dateVal = GetCellDate(row, colDate);
                    var orderContactName = colResponsible.HasValue ? GetCell(row, colResponsible).Trim() : null;
                    var notes = colNotes.HasValue ? GetCell(row, colNotes).Trim() : null;
                    var regionName = colRegion.HasValue ? GetCell(row, colRegion).Trim() : null;
                    var address = colAddress.HasValue ? GetCell(row, colAddress).Trim() : null;
                    var segment = colSegment.HasValue ? GetCell(row, colSegment).Trim() : null;
                    var balance = GetCellDecimal(row, colBalance);
                    var taxId = colTaxId.HasValue ? GetCell(row, colTaxId).Trim() : null;
                    var recordNumber = colRecordNum.HasValue ? GetCell(row, colRecordNum).Trim() : null;
                    var licenseNumber = colLicense.HasValue ? GetCell(row, colLicense).Trim() : null;

                    int? userId = null;
                    if (colRep.HasValue)
                    {
                        var repText = GetCell(row, colRep).Trim();
                        if (!string.IsNullOrWhiteSpace(repText) && (userByDisplayName.TryGetValue(repText, out var uid) || userByUserName.TryGetValue(repText, out uid)))
                            userId = uid;
                    }

                    int? areaId = null;
                    string? regionNameOnly = null;
                    if (!string.IsNullOrWhiteSpace(regionName))
                    {
                        if (areasByName.TryGetValue(regionName, out var aid))
                            areaId = aid;
                        else
                            regionNameOnly = regionName;
                    }

                    if (existingByName.TryGetValue(name, out var existing))
                    {
                        existing.ExternalCode = string.IsNullOrWhiteSpace(externalCode) ? existing.ExternalCode : externalCode;
                        existing.Phone1 = string.IsNullOrWhiteSpace(phone1) ? existing.Phone1 : phone1;
                        existing.OrderContactName = string.IsNullOrWhiteSpace(orderContactName) ? existing.OrderContactName : orderContactName;
                        existing.Notes = string.IsNullOrWhiteSpace(notes) ? existing.Notes : notes;
                        if (areaId.HasValue) { existing.AreaId = areaId; existing.RegionName = null; }
                        else { existing.RegionName = regionNameOnly ?? existing.RegionName; if (regionNameOnly != null) existing.AreaId = null; }
                        existing.Address = string.IsNullOrWhiteSpace(address) ? existing.Address : address;
                        existing.Segment = string.IsNullOrWhiteSpace(segment) ? existing.Segment : segment;
                        existing.CurrentBalance = balance;
                        existing.UserId = userId ?? existing.UserId;
                        existing.TaxIdOrNationalId = string.IsNullOrWhiteSpace(taxId) ? existing.TaxIdOrNationalId : taxId;
                        existing.RecordNumber = string.IsNullOrWhiteSpace(recordNumber) ? existing.RecordNumber : recordNumber;
                        existing.LicenseNumber = string.IsNullOrWhiteSpace(licenseNumber) ? existing.LicenseNumber : licenseNumber;
                        existing.UpdatedAt = DateTime.UtcNow;
                        updated++;
                    }
                    else
                    {
                        var created = dateVal ?? DateTime.UtcNow;
                        var newCust = new Customer
                        {
                            CustomerName = name,
                            Phone1 = string.IsNullOrWhiteSpace(phone1) ? null : phone1,
                            OrderContactName = string.IsNullOrWhiteSpace(orderContactName) ? null : orderContactName,
                            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes,
                            AreaId = areaId,
                            RegionName = regionNameOnly,
                            Address = string.IsNullOrWhiteSpace(address) ? null : address,
                            Segment = string.IsNullOrWhiteSpace(segment) ? null : segment,
                            CurrentBalance = balance,
                            UserId = userId,
                            TaxIdOrNationalId = string.IsNullOrWhiteSpace(taxId) ? null : taxId,
                            RecordNumber = string.IsNullOrWhiteSpace(recordNumber) ? null : recordNumber,
                            LicenseNumber = string.IsNullOrWhiteSpace(licenseNumber) ? null : licenseNumber,
                            CreatedAt = created,
                            UpdatedAt = DateTime.UtcNow,
                            IsActive = true
                        };
                        // خلال الاستيراد فقط: إن وُجد عمود الكود رقماً موجباً وغير مكرر يُستخدم كـ CustomerId (مثل الأصناف)
                        int codeInt = 0;
                        if (colSerial.HasValue && colSerial.Value > 0)
                        {
                            var codeVal = GetDecimalFromCell(sheet.Cells[row, colSerial.Value]);
                            if (codeVal >= 1m && codeVal <= 2147483647m) codeInt = (int)Math.Round(codeVal);
                        }
                        if (codeInt > 0 && !usedCustomerIds.Contains(codeInt))
                        {
                            newCust.CustomerId = codeInt;
                            newCust.ExternalCode = null;
                            usedCustomerIds.Add(codeInt);
                        }
                        else
                        {
                            var codeStr = colSerial.HasValue ? GetCell(row, colSerial).Trim() : null;
                            if (!string.IsNullOrWhiteSpace(codeStr))
                                newCust.ExternalCode = codeStr.Length <= 50 ? codeStr : codeStr.Substring(0, 50);
                        }
                        newCustomersList.Add(newCust);
                        inserted++;
                        existingByName[name] = newCust;
                    }
                }

                var withIdentityCust = newCustomersList.Where(c => c.CustomerId == 0).ToList();
                var withExplicitIdCust = newCustomersList.Where(c => c.CustomerId > 0).ToList();
                if (withIdentityCust.Count > 0)
                    _db.Customers.AddRange(withIdentityCust);
                if (withExplicitIdCust.Count > 0)
                {
                    using var tran = await _db.Database.BeginTransactionAsync();
                    try
                    {
                        await _db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [Customers] ON");
                        foreach (var c in withExplicitIdCust)
                        {
                            await _db.Database.ExecuteSqlRawAsync(
                                "INSERT INTO [Customers] (CustomerId, CustomerName, ExternalCode, Phone1, OrderContactName, Notes, AreaId, RegionName, Address, Segment, CreditLimit, CurrentBalance, UserId, TaxIdOrNationalId, RecordNumber, LicenseNumber, CreatedAt, UpdatedAt, IsActive) VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12}, {13}, {14}, {15}, {16}, {17}, {18})",
                                c.CustomerId, c.CustomerName ?? "", c.ExternalCode, c.Phone1, c.OrderContactName, c.Notes, c.AreaId, c.RegionName, c.Address, c.Segment, c.CreditLimit, c.CurrentBalance, c.UserId, c.TaxIdOrNationalId, c.RecordNumber, c.LicenseNumber, c.CreatedAt, c.UpdatedAt, c.IsActive);
                        }
                        await _db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [Customers] OFF");
                        await tran.CommitAsync();
                    }
                    catch
                    {
                        await tran.RollbackAsync();
                        throw;
                    }
                }
                await _db.SaveChangesAsync();
                var msg = $"تم استيراد قائمة العملاء: {inserted} جديد، {updated} محدّث.";
                if (regionsCreated > 0)
                    msg += $" وتم إنشاء {regionsCreated} منطقة جديدة في جدول المناطق وربط العملاء بها حسب عمود «المنطقة».";
                TempData["Success"] = msg;
                return RedirectToAction(nameof(Import));
            }
            catch (Exception ex)
            {
                TempData["Error"] = "خطأ أثناء استيراد العملاء: " + (ex.InnerException?.Message ?? ex.Message);
                return RedirectToAction(nameof(Import));
            }
        }

        /// <summary>محافظة + حي لربط المناطق الجديدة عند استيراد العملاء (أو أول محافظة/حي إن لم يُحدَّد).</summary>
        private async Task<(bool ok, int gid, int did, string? err)> ResolveGovernorateDistrictForImportAsync(int? governorateId, int? districtId)
        {
            if (governorateId.HasValue && districtId.HasValue)
            {
                var district = await _db.Districts.FindAsync(districtId.Value);
                if (district == null || district.GovernorateId != governorateId.Value)
                    return (false, 0, 0, "الحي المختار لا يتبع المحافظة المختارة.");
                return (true, governorateId.Value, districtId.Value, null);
            }
            var firstGov = await _db.Governorates.OrderBy(g => g.GovernorateId).FirstOrDefaultAsync();
            if (firstGov == null)
                return (false, 0, 0, "لا توجد محافظات في النظام. أضف محافظة وحيّاً أولاً.");
            var firstDist = await _db.Districts.Where(d => d.GovernorateId == firstGov.GovernorateId).OrderBy(d => d.DistrictId).FirstOrDefaultAsync();
            if (firstDist == null)
                return (false, 0, 0, "لا يوجد حي/مركز في المحافظة الأولى. أضف حيّاً أولاً.");
            return (true, firstGov.GovernorateId, firstDist.DistrictId, null);
        }

        // =====================
        // استيراد أرصدة العملاء (رصيد أول المدة) — دفتر الأستاذ LedgerEntry، SourceType = Opening
        // =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Products.Import")]
        public async Task<IActionResult> ImportCustomerBalances(IFormFile? excelFile)
        {
            if (excelFile == null || excelFile.Length == 0)
            {
                TempData["Error"] = "من فضلك اختر ملف أرصدة العملاء (Excel) أولاً.";
                return RedirectToAction(nameof(Import));
            }

            try
            {
                using var stream = new MemoryStream();
                await excelFile.CopyToAsync(stream);
                stream.Position = 0;
                ExcelPackage.License.SetNonCommercialPersonal("Amr ERP Dev");
                using var package = new ExcelPackage(stream);
                var sheet = package.Workbook.Worksheets[0];
                if (sheet.Dimension == null)
                {
                    TempData["Error"] = "الملف لا يحتوي على بيانات.";
                    return RedirectToAction(nameof(Import));
                }

                int lastRow = sheet.Dimension.End.Row;
                int lastCol = sheet.Dimension.End.Column;
                var fixedNames = new HashSet<string>(ExcelImportColumns.CustomerOpeningBalance, StringComparer.OrdinalIgnoreCase);
                var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int col = 1; col <= lastCol; col++)
                {
                    var headerText = sheet.Cells[1, col].Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(headerText) && fixedNames.Contains(headerText) && !headers.ContainsKey(headerText))
                        headers[headerText] = col;
                }

                int? colCustomerCode = GetCol(headers, "اسم الكود", "كود العميل", "CustomerId", "رقم الحساب");
                int? colCustomerName = GetCol(headers, "اسم العميل", "العميل", "الاسم");
                if ((!colCustomerCode.HasValue || colCustomerCode <= 0) && (!colCustomerName.HasValue || colCustomerName <= 0))
                {
                    TempData["Error"] = "الملف يجب أن يحتوي على عمود للعميل (كود العميل أو رقم الحساب أو الاسم).";
                    return RedirectToAction(nameof(Import));
                }

                int? colDebit = GetCol(headers, "مدين", "Debit", "مدین");
                int? colCredit = GetCol(headers, "دائن", "Credit");
                if ((!colDebit.HasValue || colDebit <= 0) && (!colCredit.HasValue || colCredit <= 0))
                {
                    TempData["Error"] = "الملف يجب أن يحتوي على عمود مدين و/أو عمود دائن.";
                    return RedirectToAction(nameof(Import));
                }

                int? colEntryDate = GetCol(headers, "تاريخ", "EntryDate");
                int? colAccountId = GetCol(headers, "AccountId");

                var entryDateDefault = DateTime.Today;
                var customers = await _db.Customers.ToListAsync();
                var customerById = customers.ToDictionary(c => c.CustomerId);
                // تجنّب تكرار المفتاح: إذا تكرر كود الإكسل أو الاسم نأخذ أول عميل فقط
                var customerByExternalCode = customers
                    .Where(c => !string.IsNullOrWhiteSpace(c.ExternalCode))
                    .GroupBy(c => c.ExternalCode!.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                var customerByName = customers
                    .Where(c => !string.IsNullOrWhiteSpace(c.CustomerName))
                    .GroupBy(c => c.CustomerName!.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                int defaultAccountId = await _db.Accounts
                    .Where(a => a.AccountCode == "1103")
                    .Select(a => a.AccountId)
                    .FirstOrDefaultAsync();
                if (defaultAccountId == 0)
                    defaultAccountId = await _db.Accounts.OrderBy(a => a.AccountId).Select(a => a.AccountId).FirstOrDefaultAsync();

                var toAdd = new List<LedgerEntry>();
                var errors = new List<string>();
                var lineNo = 0;
                var newCustomersCount = 0;

                for (int row = 2; row <= lastRow; row++)
                {
                    string? codeVal = null;
                    if (colCustomerCode.HasValue && colCustomerCode.Value > 0)
                        codeVal = sheet.Cells[row, colCustomerCode.Value].Text?.Trim();
                    string? nameVal = null;
                    if (colCustomerName.HasValue && colCustomerName.Value > 0)
                        nameVal = sheet.Cells[row, colCustomerName.Value].Text?.Trim();

                    decimal debit = 0m;
                    if (colDebit.HasValue && colDebit.Value > 0)
                        debit = GetDecimalFromCell(sheet.Cells[row, colDebit.Value]);
                    decimal credit = 0m;
                    if (colCredit.HasValue && colCredit.Value > 0)
                        credit = GetDecimalFromCell(sheet.Cells[row, colCredit.Value]);

                    if (debit == 0m && credit == 0m)
                        continue;

                    // مثل دفتر الأستاذ: كل قيد له جانب واحد فقط — إما مدين أو دائن (الآخر صفر)
                    if (debit > 0m && credit > 0m)
                    {
                        var net = debit - credit;
                        if (net >= 0) { debit = net; credit = 0m; }
                        else { credit = -net; debit = 0m; }
                    }

                    Customer? customer = null;
                    if (!string.IsNullOrWhiteSpace(codeVal))
                    {
                        if (int.TryParse(codeVal, out var id) && customerById.TryGetValue(id, out var byId))
                            customer = byId;
                        else if (customerByExternalCode.TryGetValue(codeVal, out var byCode))
                            customer = byCode;
                    }
                    if (customer == null && !string.IsNullOrWhiteSpace(nameVal) && customerByName.TryGetValue(nameVal, out var cn))
                        customer = cn;
                    // ربط رقم الحساب من الإكسل بكود الإكسل في البرنامج (للمقارنة والربط)
                    if (customer != null && !string.IsNullOrWhiteSpace(codeVal))
                    {
                        customer.ExternalCode = codeVal.Length <= 50 ? codeVal : codeVal.Substring(0, 50);
                        customer.UpdatedAt = DateTime.Now;
                    }
                    if (customer == null)
                    {
                        var newName = !string.IsNullOrWhiteSpace(nameVal) ? nameVal : ("عميل " + (codeVal ?? "بدون كود"));
                        if (newName.Length > 200) newName = newName.Substring(0, 200);
                        var newCustomer = new Customer
                        {
                            CustomerName = newName,
                            ExternalCode = !string.IsNullOrWhiteSpace(codeVal) && codeVal.Length <= 50 ? codeVal : (codeVal?.Length > 50 ? codeVal.Substring(0, 50) : null),
                            PartyCategory = "عميل",
                            AccountId = defaultAccountId > 0 ? defaultAccountId : null,
                            IsActive = true,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now
                        };
                        _db.Customers.Add(newCustomer);
                        await _db.SaveChangesAsync();
                        newCustomersCount++;
                        customerById[newCustomer.CustomerId] = newCustomer;
                        if (!string.IsNullOrWhiteSpace(newCustomer.ExternalCode))
                            customerByExternalCode[newCustomer.ExternalCode.Trim()] = newCustomer;
                        customerByName[newCustomer.CustomerName.Trim()] = newCustomer;
                        customer = newCustomer;
                    }

                    int accountId = defaultAccountId;
                    if (colAccountId.HasValue && colAccountId.Value > 0)
                    {
                        var accText = sheet.Cells[row, colAccountId.Value].Text?.Trim();
                        if (!string.IsNullOrWhiteSpace(accText) && int.TryParse(accText, out var fileAccId) && fileAccId > 0)
                            accountId = fileAccId;
                        else if (customer.AccountId.HasValue && customer.AccountId.Value > 0)
                            accountId = customer.AccountId.Value;
                    }
                    else if (customer.AccountId.HasValue && customer.AccountId.Value > 0)
                        accountId = customer.AccountId.Value;

                    if (accountId == 0)
                    {
                        errors.Add($"صف {row}: لا يوجد حساب محاسبي للعميل «{customer.CustomerName}».");
                        continue;
                    }

                    DateTime entryDate = entryDateDefault;
                    if (colEntryDate.HasValue && colEntryDate.Value > 0)
                    {
                        var dateText = sheet.Cells[row, colEntryDate.Value].Text?.Trim();
                        if (!string.IsNullOrWhiteSpace(dateText) && DateTime.TryParse(dateText, CultureInfo.GetCultureInfo("ar-EG"), DateTimeStyles.None, out var parsed))
                            entryDate = parsed.Date;
                    }

                    lineNo++;
                    toAdd.Add(new LedgerEntry
                    {
                        EntryDate = entryDate,
                        SourceType = LedgerSourceType.Opening,
                        VoucherNo = "رصيد افتتاحي",
                        SourceId = null,
                        LineNo = lineNo,
                        PostVersion = 1,
                        AccountId = accountId,
                        CustomerId = customer.CustomerId,
                        Debit = debit,
                        Credit = credit,
                        Description = "رصيد افتتاحي - " + customer.CustomerName,
                        CreatedAt = DateTime.Now
                    });
                }

                var existingOpening = await _db.LedgerEntries
                    .Where(e => e.SourceType == LedgerSourceType.Opening && e.CustomerId != null)
                    .ToListAsync();
                _db.LedgerEntries.RemoveRange(existingOpening);
                await _db.LedgerEntries.AddRangeAsync(toAdd);
                await _db.SaveChangesAsync();

                // إعادة حساب أرصدة العملاء (Customer.CurrentBalance) من دفتر الأستاذ لضبط إجمالي المدين/الدائن في القوائم والتقارير
                var ledgerPosting = HttpContext.RequestServices.GetRequiredService<ILedgerPostingService>();
                await ledgerPosting.RecalcAllCustomerBalancesAsync();

                var msg = $"تم استيراد أرصدة افتتاحية لـ {toAdd.Count} عميل.";
                if (newCustomersCount > 0)
                    msg += $" تمت إضافة {newCustomersCount} عميل جديد من الملف إلى قائمة العملاء.";
                if (errors.Count > 0)
                    msg += " تحذيرات: " + string.Join(" ", errors.Take(5)) + (errors.Count > 5 ? " ..." : "");
                TempData["Success"] = msg;
                return RedirectToAction(nameof(Import));
            }
            catch (Exception ex)
            {
                TempData["Error"] = "خطأ أثناء استيراد أرصدة العملاء: " + (ex.InnerException?.Message ?? ex.Message);
                return RedirectToAction(nameof(Import));
            }
        }

        // =====================
        // مسح كل العملاء المستوردين + أرصدتهم/حركاتهم المرتبطة
        // معيار "المستورد": العميل الذي لديه ExternalCode غير فارغ
        // =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Products.Import")]
        public async Task<IActionResult> DeleteImportedCustomersAndBalances()
        {
            var importedCustomers = _db.Customers
                .Where(c => c.ExternalCode != null && c.ExternalCode.Trim() != "");

            var importedCount = await importedCustomers.CountAsync();
            if (importedCount == 0)
            {
                TempData["Error"] = "لا توجد بيانات عملاء مستوردة للمسح.";
                return RedirectToAction(nameof(Import));
            }

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var importedCustomerIds = importedCustomers.Select(c => c.CustomerId);

                // فك الربط الاختياري قبل الحذف
                await _db.Users
                    .Where(u => u.CustomerId.HasValue && importedCustomerIds.Contains(u.CustomerId.Value))
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.CustomerId, (int?)null));

                await _db.Batches
                    .Where(b => b.CustomerId.HasValue && importedCustomerIds.Contains(b.CustomerId.Value))
                    .ExecuteUpdateAsync(s => s.SetProperty(b => b.CustomerId, (int?)null));

                // حذف المستندات المرتبطة بالعملاء المستوردين (الهيدر؛ السطور تُحذف Cascade حسب الإعداد)
                var deletedCashReceipts = await _db.CashReceipts
                    .Where(x => x.CustomerId.HasValue && importedCustomerIds.Contains(x.CustomerId.Value))
                    .ExecuteDeleteAsync();

                var deletedCashPayments = await _db.CashPayments
                    .Where(x => x.CustomerId.HasValue && importedCustomerIds.Contains(x.CustomerId.Value))
                    .ExecuteDeleteAsync();

                var deletedDebitNotes = await _db.DebitNotes
                    .Where(x => x.CustomerId.HasValue && importedCustomerIds.Contains(x.CustomerId.Value))
                    .ExecuteDeleteAsync();

                var deletedCreditNotes = await _db.CreditNotes
                    .Where(x => x.CustomerId.HasValue && importedCustomerIds.Contains(x.CustomerId.Value))
                    .ExecuteDeleteAsync();

                var deletedSalesOrders = await _db.SalesOrders
                    .Where(x => importedCustomerIds.Contains(x.CustomerId))
                    .ExecuteDeleteAsync();

                var deletedPurchaseRequests = await _db.PurchaseRequests
                    .Where(x => importedCustomerIds.Contains(x.CustomerId))
                    .ExecuteDeleteAsync();

                var deletedSalesReturns = await _db.SalesReturns
                    .Where(x => importedCustomerIds.Contains(x.CustomerId))
                    .ExecuteDeleteAsync();

                var deletedSalesInvoices = await _db.SalesInvoices
                    .Where(x => importedCustomerIds.Contains(x.CustomerId))
                    .ExecuteDeleteAsync();

                var deletedPurchaseReturns = await _db.PurchaseReturns
                    .Where(x => importedCustomerIds.Contains(x.CustomerId))
                    .ExecuteDeleteAsync();

                var deletedPurchaseInvoices = await _db.PurchaseInvoices
                    .Where(x => importedCustomerIds.Contains(x.CustomerId))
                    .ExecuteDeleteAsync();

                // أرصدة وقيود مرتبطة بالعميل
                var deletedLedgerEntries = await _db.LedgerEntries
                    .Where(x => x.CustomerId.HasValue && importedCustomerIds.Contains(x.CustomerId.Value))
                    .ExecuteDeleteAsync();

                // أخيراً حذف العملاء أنفسهم
                var deletedCustomers = await _db.Customers
                    .Where(c => c.ExternalCode != null && c.ExternalCode.Trim() != "")
                    .ExecuteDeleteAsync();

                await tx.CommitAsync();

                var ledgerPosting = HttpContext.RequestServices.GetRequiredService<ILedgerPostingService>();
                await ledgerPosting.RecalcAllCustomerBalancesAsync();
                _customerCache.ClearCustomersCache();

                TempData["Success"] =
                    $"تم مسح العملاء المستوردين بنجاح. " +
                    $"العملاء: {deletedCustomers}، القيود: {deletedLedgerEntries}، " +
                    $"المبيعات: {deletedSalesInvoices}، مرتجعات المبيعات: {deletedSalesReturns}، " +
                    $"المشتريات: {deletedPurchaseInvoices}، مرتجعات المشتريات: {deletedPurchaseReturns}، " +
                    $"القبض: {deletedCashReceipts}، الدفع: {deletedCashPayments}، " +
                    $"إشعارات الخصم/الإضافة: {deletedDebitNotes + deletedCreditNotes}.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "تعذّر مسح العملاء المستوردين: " + (ex.InnerException?.Message ?? ex.Message);
            }

            return RedirectToAction(nameof(Import));
        }

        // =====================
        // مسح كل العملاء + الأرصدة/الحركات المرتبطة
        // =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Products.Import")]
        public async Task<IActionResult> DeleteAllCustomersAndBalances()
        {
            var allCustomersCount = await _db.Customers.CountAsync();
            if (allCustomersCount == 0)
            {
                TempData["Error"] = "لا توجد بيانات عملاء للمسح.";
                return RedirectToAction(nameof(Import));
            }

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                // فك الروابط الاختيارية أولاً
                await _db.Users
                    .Where(u => u.CustomerId.HasValue)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.CustomerId, (int?)null));

                await _db.Batches
                    .Where(b => b.CustomerId.HasValue)
                    .ExecuteUpdateAsync(s => s.SetProperty(b => b.CustomerId, (int?)null));

                // جداول الموديولات المرتبطة مباشرة بالعميل
                await _db.VendorProductMappings.ExecuteDeleteAsync();
                await _db.VendorFaxUploads.ExecuteDeleteAsync(); // VendorFaxLines تُحذف Cascade
                await _db.PurchasingOrders.ExecuteDeleteAsync(); // PurchasingOrderLines تُحذف Cascade

                // مستندات العملاء الرئيسية
                await _db.CashReceipts.ExecuteDeleteAsync();
                await _db.CashPayments.ExecuteDeleteAsync();
                await _db.DebitNotes.ExecuteDeleteAsync();
                await _db.CreditNotes.ExecuteDeleteAsync();
                await _db.SalesOrders.ExecuteDeleteAsync();      // SOLines تُحذف Cascade
                await _db.PurchaseRequests.ExecuteDeleteAsync(); // PRLines تُحذف Cascade
                await _db.SalesReturns.ExecuteDeleteAsync();     // SalesReturnLines تُحذف Cascade
                await _db.SalesInvoices.ExecuteDeleteAsync();    // SalesInvoiceLines + Route/FridgeLines تُحذف Cascade
                await _db.PurchaseReturns.ExecuteDeleteAsync();  // PurchaseReturnLines تُحذف Cascade
                await _db.PurchaseInvoices.ExecuteDeleteAsync(); // PILines تُحذف Cascade

                // قيود العملاء
                await _db.LedgerEntries
                    .Where(x => x.CustomerId.HasValue)
                    .ExecuteDeleteAsync();

                // أخيراً حذف العملاء
                var deletedCustomers = await _db.Customers.ExecuteDeleteAsync();
                await tx.CommitAsync();

                var ledgerPosting = HttpContext.RequestServices.GetRequiredService<ILedgerPostingService>();
                await ledgerPosting.RecalcAllCustomerBalancesAsync();
                _customerCache.ClearCustomersCache();

                TempData["Success"] = $"تم مسح جميع العملاء وكل الأرصدة/الحركات المرتبطة بهم بنجاح. عدد العملاء المحذوفة: {deletedCustomers}.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "تعذّر مسح كل العملاء: " + (ex.InnerException?.Message ?? ex.Message);
            }

            return RedirectToAction(nameof(Import));
        }

        // =====================
        // مسح كل الأصناف المستوردة + أرصدتها/حركاتها المرتبطة
        // معيار "المستورد": Product.Imported يحتوي "مستورد" أو "imported"
        // =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Products.Import")]
        public async Task<IActionResult> DeleteImportedProductsAndBalances()
        {
            var importedProducts = _db.Products.Where(p =>
                EF.Functions.Like((p.Imported ?? ""), "%مستورد%") ||
                EF.Functions.Like((p.Imported ?? "").ToLower(), "%imported%"));

            var importedCount = await importedProducts.CountAsync();
            if (importedCount == 0)
            {
                TempData["Error"] = "لا توجد أصناف مستوردة للمسح.";
                return RedirectToAction(nameof(Import));
            }

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var importedProductIds = importedProducts.Select(p => p.ProdId);
                var importedStockEntryIds = _db.StockLedger
                    .Where(sl => importedProductIds.Contains(sl.ProdId))
                    .Select(sl => sl.EntryId);

                // حذف كل ما يرتبط بالصنف المستورد قبل حذف الصنف نفسه
                await _db.VendorProductMappings.Where(x => importedProductIds.Contains(x.ProductId)).ExecuteDeleteAsync();
                await _db.PurchasingOrderLines.Where(x => importedProductIds.Contains(x.ProductId)).ExecuteDeleteAsync();
                await _db.VendorFaxLines.Where(x => x.MatchedProductId.HasValue && importedProductIds.Contains(x.MatchedProductId.Value)).ExecuteDeleteAsync();
                await _db.SalesInvoiceRouteFridgeLines.Where(x => importedProductIds.Contains(x.ProductId)).ExecuteDeleteAsync();
                await _db.StockAdjustmentLines.Where(x => importedProductIds.Contains(x.ProductId)).ExecuteDeleteAsync();
                await _db.StockTransferLines.Where(x => importedProductIds.Contains(x.ProductId)).ExecuteDeleteAsync();
                await _db.PRLines.Where(x => importedProductIds.Contains(x.ProdId)).ExecuteDeleteAsync();
                await _db.SOLines.Where(x => importedProductIds.Contains(x.ProdId)).ExecuteDeleteAsync();
                await _db.PurchaseReturnLines.Where(x => importedProductIds.Contains(x.ProdId)).ExecuteDeleteAsync();
                await _db.PILines.Where(x => importedProductIds.Contains(x.ProdId)).ExecuteDeleteAsync();
                await _db.SalesReturnLines.Where(x => importedProductIds.Contains(x.ProdId)).ExecuteDeleteAsync();
                await _db.SalesInvoiceLines.Where(x => importedProductIds.Contains(x.ProdId)).ExecuteDeleteAsync();
                await _db.ProductDiscountOverrides.Where(x => importedProductIds.Contains(x.ProductId)).ExecuteDeleteAsync();
                await _db.ProductPriceHistories.Where(x => importedProductIds.Contains(x.ProdId)).ExecuteDeleteAsync();
                await _db.StockFifoMap.Where(x => importedStockEntryIds.Contains(x.InEntryId) || importedStockEntryIds.Contains(x.OutEntryId)).ExecuteDeleteAsync();
                await _db.StockLedger.Where(x => importedProductIds.Contains(x.ProdId)).ExecuteDeleteAsync();
                await _db.StockBatches.Where(x => importedProductIds.Contains(x.ProdId)).ExecuteDeleteAsync();
                await _db.Batches.Where(x => importedProductIds.Contains(x.ProdId)).ExecuteDeleteAsync();

                var deletedProducts = await _db.Products
                    .Where(p =>
                        EF.Functions.Like((p.Imported ?? ""), "%مستورد%") ||
                        EF.Functions.Like((p.Imported ?? "").ToLower(), "%imported%"))
                    .ExecuteDeleteAsync();

                await tx.CommitAsync();

                _productCache.ClearProductsCache();
                TempData["Success"] = $"تم مسح الأصناف المستوردة وأرصدتها بنجاح. عدد الأصناف المحذوفة: {deletedProducts}.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "تعذّر مسح الأصناف المستوردة: " + (ex.InnerException?.Message ?? ex.Message);
            }

            return RedirectToAction(nameof(Import));
        }

        // =====================
        // مسح كل الأصناف + أرصدتها/حركاتها المرتبطة
        // =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Products.Import")]
        public async Task<IActionResult> DeleteAllProductsAndBalances()
        {
            var allProductsCount = await _db.Products.CountAsync();
            if (allProductsCount == 0)
            {
                TempData["Error"] = "لا توجد أصناف للمسح.";
                return RedirectToAction(nameof(Import));
            }

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var allProductIds = _db.Products.Select(p => p.ProdId);
                var allStockEntryIds = _db.StockLedger.Select(sl => sl.EntryId);

                // جداول مرتبطة بالأصناف
                await _db.VendorProductMappings.ExecuteDeleteAsync();
                await _db.PurchasingOrderLines.ExecuteDeleteAsync();
                await _db.VendorFaxLines.ExecuteDeleteAsync();
                await _db.SalesInvoiceRouteFridgeLines.ExecuteDeleteAsync();
                await _db.StockAdjustmentLines.ExecuteDeleteAsync();
                await _db.StockTransferLines.ExecuteDeleteAsync();
                await _db.PRLines.ExecuteDeleteAsync();
                await _db.SOLines.ExecuteDeleteAsync();
                await _db.PurchaseReturnLines.ExecuteDeleteAsync();
                await _db.PILines.ExecuteDeleteAsync();
                await _db.SalesReturnLines.ExecuteDeleteAsync();
                await _db.SalesInvoiceLines.ExecuteDeleteAsync();
                await _db.ProductDiscountOverrides.ExecuteDeleteAsync();
                await _db.ProductPriceHistories.ExecuteDeleteAsync();
                await _db.StockFifoMap
                    .Where(x => allStockEntryIds.Contains(x.InEntryId) || allStockEntryIds.Contains(x.OutEntryId))
                    .ExecuteDeleteAsync();
                await _db.StockLedger.Where(x => allProductIds.Contains(x.ProdId)).ExecuteDeleteAsync();
                await _db.StockBatches.Where(x => allProductIds.Contains(x.ProdId)).ExecuteDeleteAsync();
                await _db.Batches.Where(x => allProductIds.Contains(x.ProdId)).ExecuteDeleteAsync();

                var deletedProducts = await _db.Products.ExecuteDeleteAsync();
                await tx.CommitAsync();

                _productCache.ClearProductsCache();
                TempData["Success"] = $"تم مسح جميع الأصناف وكل الأرصدة/الحركات المرتبطة بها بنجاح. عدد الأصناف المحذوفة: {deletedProducts}.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "تعذّر مسح كل الأصناف: " + (ex.InnerException?.Message ?? ex.Message);
            }

            return RedirectToAction(nameof(Import));
        }

        // =====================
        // استيراد أسماء المستخدمين من إكسل
        // =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Products.Import")]
        public IActionResult ImportUsers(IFormFile? excelFile)
        {
            if (excelFile == null || excelFile.Length == 0)
            {
                TempData["Error"] = "من فضلك اختر ملف أسماء المستخدمين (Excel) أولاً.";
                return RedirectToAction(nameof(Import));
            }
            TempData["Error"] = "استيراد المستخدمين من إكسل قيد الإعداد. سيتم الربط عند توفير نموذج الملف.";
            return RedirectToAction(nameof(Import));
        }

        // =====================
        // استيراد رصيد الخزينة (رصيد أول المدة) — من شيت: خلية "رصيد الخزينة" والرقم تحتها أو بجانبها
        // =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Products.Import")]
        public async Task<IActionResult> ImportTreasuryBalance(IFormFile? excelFile, string? accountId)
        {
            if (excelFile == null || excelFile.Length == 0)
            {
                TempData["Error"] = "ارفع ملف إكسل يحتوي على «رصيد الخزينة» والرقم تحتها (مثال: 425000).";
                return RedirectToAction(nameof(Import));
            }

            try
            {
                int treasuryAccountId = 0;
                if (!string.IsNullOrWhiteSpace(accountId) && int.TryParse(accountId.Trim(), out var parsed) && parsed > 0)
                    treasuryAccountId = parsed;
                if (treasuryAccountId == 0)
                {
                    treasuryAccountId = await _db.Accounts
                        .Where(a => a.AccountCode == "1101" || a.AccountCode.StartsWith("1101"))
                        .OrderBy(a => a.AccountCode)
                        .Select(a => a.AccountId)
                        .FirstOrDefaultAsync();
                    if (treasuryAccountId == 0)
                        treasuryAccountId = await _db.Accounts.OrderBy(a => a.AccountId).Select(a => a.AccountId).FirstOrDefaultAsync();
                }
                if (treasuryAccountId == 0)
                {
                    TempData["Error"] = "لم يُعثر على حساب خزينة. أدخل رقم الحساب يدوياً أو أضف حساباً بكود 1101.";
                    return RedirectToAction(nameof(Import));
                }

                using var stream = new MemoryStream();
                await excelFile.CopyToAsync(stream);
                stream.Position = 0;
                ExcelPackage.License.SetNonCommercialPersonal("Amr ERP Dev");
                using var package = new ExcelPackage(stream);
                var sheet = package.Workbook.Worksheets[0];
                if (sheet.Dimension == null)
                {
                    TempData["Error"] = "الملف لا يحتوي على بيانات.";
                    return RedirectToAction(nameof(Import));
                }

                int lastRow = sheet.Dimension.End.Row;
                int lastCol = sheet.Dimension.End.Column;
                const string label = "رصيد الخزينة";
                decimal amount = 0m;
                bool found = false;

                for (int r = 1; r <= lastRow && !found; r++)
                {
                    for (int c = 1; c <= lastCol; c++)
                    {
                        var cellText = sheet.Cells[r, c].Text?.Trim() ?? "";
                        if (string.IsNullOrEmpty(cellText) || !cellText.Contains(label, StringComparison.OrdinalIgnoreCase))
                            continue;
                        amount = GetDecimalFromCell(sheet.Cells[r + 1, c]);
                        if (c < lastCol && amount == 0m) amount = GetDecimalFromCell(sheet.Cells[r, c + 1]);
                        found = true;
                        break;
                    }
                    if (found) break;
                }

                if (!found)
                {
                    TempData["Error"] = "لم يُعثر في الملف على خلية «رصيد الخزينة» مع رقم صالح تحتها أو بجانبها.";
                    return RedirectToAction(nameof(Import));
                }

                decimal debit = amount >= 0 ? amount : 0m;
                decimal credit = amount < 0 ? Math.Abs(amount) : 0m;

                var existingOpening = await _db.LedgerEntries
                    .Where(e => e.SourceType == LedgerSourceType.Opening && e.AccountId == treasuryAccountId && e.CustomerId == null)
                    .ToListAsync();
                _db.LedgerEntries.RemoveRange(existingOpening);
                _db.LedgerEntries.Add(new LedgerEntry
                {
                    EntryDate = DateTime.Today,
                    SourceType = LedgerSourceType.Opening,
                    VoucherNo = "رصيد افتتاحي خزينة",
                    SourceId = null,
                    LineNo = 1,
                    PostVersion = 1,
                    AccountId = treasuryAccountId,
                    CustomerId = null,
                    Debit = debit,
                    Credit = credit,
                    Description = "رصيد افتتاحي خزينة - مستورد من إكسل",
                    CreatedAt = DateTime.Now
                });
                await _db.SaveChangesAsync();

                var balanceText = amount >= 0 ? $"{amount:N0} مدين" : $"{Math.Abs(amount):N0} دائن";
                TempData["Success"] = $"تم استيراد رصيد الخزينة: {balanceText} كرصيد افتتاحي لحساب رقم {treasuryAccountId}.";
                return RedirectToAction(nameof(Import));
            }
            catch (Exception ex)
            {
                TempData["Error"] = "خطأ أثناء استيراد رصيد الخزينة: " + (ex.InnerException?.Message ?? ex.Message);
                return RedirectToAction(nameof(Import));
            }
        }
    }
}
