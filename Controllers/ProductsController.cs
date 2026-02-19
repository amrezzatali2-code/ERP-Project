// Controllers/ProductsController.cs
using ClosedXML.Excel;                  // مكتبة Excel
using OfficeOpenXml;
using DocumentFormat.OpenXml.InkML;
using ERP.Data;                         // سياق قاعدة البيانات الرئيسي
using ERP.Infrastructure;               // PagedResult + UserActivityLogger
using ERP.Models;                       // Product, Category, UserActionType...
using ERP.Services;                     // StockAnalysisService
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

        public ProductsController(AppDbContext db, StockAnalysisService stockAnalysisService, IUserActivityLogger activityLogger)
        {
            _db = db;
            _stockAnalysisService = stockAnalysisService;
            _activityLogger = activityLogger;
        }












        // =========================================================
        // 1) قائمة الأصناف  (نظام القوائم الموحد)
        // =========================================================
        [HttpGet]
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

      // ✅ فلاتر متراكبة جديدة
      int? filterProductGroupId = null,  // متغير: فلتر حسب مجموعة الصنف (ProductGroup)
      string? filterCompany = null,      // متغير: فلتر حسب الشركة (Company)
      string? filterImported = null,     // متغير: فلتر حسب المنشأ (محلي/مستورد)

      int page = 1,                      // متغير: رقم الصفحة الحالية
      int pageSize = 50                  // متغير: عدد السطور في الصفحة
  )
        {
            // =========================================================
            // (1) الاستعلام الأساسي (بدون تتبّع لتحسين الأداء)
            //      + تحميل مجموعة الصنف ومجموعة البونص لعرض أسمائهما في الجدول
            // =========================================================
            IQueryable<Product> q = _db.Products
                .Include(p => p.ProductGroup)        // متغير: تحميل مجموعة الصنف
                .Include(p => p.ProductBonusGroup)   // متغير: تحميل مجموعة البونص
                .AsNoTracking();

            // =========================================================
            // (2) تنظيف قيم الفلاتر
            // =========================================================
            var s = (search ?? string.Empty).Trim();                        // متغير: نص البحث بعد التنظيف
            var sb = (searchBy ?? "all").Trim().ToLowerInvariant();         // متغير: عمود البحث
            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();  // متغير: طريقة البحث
            var so = (sort ?? "name").Trim().ToLowerInvariant();            // متغير: عمود الترتيب
            var desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase); // متغير: هل الترتيب تنازلي؟

            // ضبط page/pageSize
            page = page <= 0 ? 1 : page;                     // متغير: حماية رقم الصفحة
            pageSize = pageSize <= 0 ? 50 : pageSize;         // متغير: حماية حجم الصفحة

            // توحيد searchMode
            if (sm == "startswith") sm = "starts";            // متغير: قبول startswith
            if (sm == "eq") sm = "equals";                    // متغير: قبول eq
            if (sm != "contains" && sm != "starts" && sm != "equals")
                sm = "contains";                              // متغير: fallback

            bool modeStarts = sm == "starts";                 // متغير: وضع يبدأ بـ
            bool modeEquals = sm == "equals";                 // متغير: وضع يساوي

            // =========================================================
            // (3) تطبيق البحث (يدعم: يحتوي/يبدأ بـ/يساوي)
            // =========================================================
            if (!string.IsNullOrWhiteSpace(s))
            {
                bool isInt = int.TryParse(s, out int intVal);        // متغير: هل النص رقم صحيح؟
                bool isDec = decimal.TryParse(s, out decimal decVal);// متغير: هل النص رقم عشري؟

                string likeContains = $"%{s}%";   // متغير: LIKE يحتوي
                string likeStarts = $"{s}%";      // متغير: LIKE يبدأ بـ

                switch (sb)
                {
                    case "id":
                        if (isInt)
                            q = q.Where(p => p.ProdId == intVal);
                        else
                            q = q.Where(p => p.ProdId.ToString().Contains(s));
                        break;

                    case "code":
                        if (modeEquals)
                            q = q.Where(p => p.Barcode != null && p.Barcode == s);
                        else if (modeStarts)
                            q = q.Where(p => p.Barcode != null && EF.Functions.Like(p.Barcode, likeStarts));
                        else
                            q = q.Where(p => p.Barcode != null && EF.Functions.Like(p.Barcode, likeContains));
                        break;

                    case "name":
                        if (modeEquals)
                            q = q.Where(p => p.ProdName != null && p.ProdName == s);
                        else if (modeStarts)
                            q = q.Where(p => p.ProdName != null && EF.Functions.Like(p.ProdName, likeStarts));
                        else
                            q = q.Where(p => p.ProdName != null && EF.Functions.Like(p.ProdName, likeContains));
                        break;

                    case "gen":
                        if (modeEquals)
                            q = q.Where(p => p.GenericName != null && p.GenericName == s);
                        else if (modeStarts)
                            q = q.Where(p => p.GenericName != null && EF.Functions.Like(p.GenericName, likeStarts));
                        else
                            q = q.Where(p => p.GenericName != null && EF.Functions.Like(p.GenericName, likeContains));
                        break;

                    case "category":
                        if (modeEquals)
                            q = q.Where(p => p.ProductGroup != null && p.ProductGroup.Name == s);
                        else if (modeStarts)
                            q = q.Where(p => p.ProductGroup != null && p.ProductGroup.Name != null && EF.Functions.Like(p.ProductGroup.Name, likeStarts));
                        else
                            q = q.Where(p => p.ProductGroup != null && p.ProductGroup.Name != null && EF.Functions.Like(p.ProductGroup.Name, likeContains));
                        break;

                    case "comp":
                        if (modeEquals)
                            q = q.Where(p => p.Company != null && p.Company == s);
                        else if (modeStarts)
                            q = q.Where(p => p.Company != null && EF.Functions.Like(p.Company, likeStarts));
                        else
                            q = q.Where(p => p.Company != null && EF.Functions.Like(p.Company, likeContains));
                        break;

                    case "price":
                        if (isDec)
                            q = q.Where(p => p.PriceRetail == decVal);
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
            // (5) ✅ فلاتر متراكبة (Layered Filters)
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
                "comp" => desc ? q.OrderByDescending(p => p.Company) : q.OrderBy(p => p.Company),
                "created" => desc ? q.OrderByDescending(p => p.CreatedAt) : q.OrderBy(p => p.CreatedAt),
                "updated" => desc ? q.OrderByDescending(p => p.UpdatedAt) : q.OrderBy(p => p.UpdatedAt),
                "imported" => desc ? q.OrderByDescending(p => p.Imported) : q.OrderBy(p => p.Imported),
                "active" => desc ? q.OrderByDescending(p => p.IsActive) : q.OrderBy(p => p.IsActive),
                _ => desc ? q.OrderByDescending(p => p.ProdName) : q.OrderBy(p => p.ProdName)
            };

            // =========================================================
            // (7) الترقيم (Paging)
            // =========================================================
            int total = await q.CountAsync(); // متغير: إجمالي النتائج بعد الفلاتر

            var items = await q
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // =========================================================
            // (8) تجهيز PagedResult + تثبيت حالة البحث/الترتيب/التاريخ
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
            ViewBag.SearchMode = sm;
            ViewBag.NumField = nf;
            ViewBag.FromNum = fromNum;
            ViewBag.ToNum = toNum;

            // ✅ فلاتر متراكبة: تحميل قوائم للـ dropdowns
            var productGroups = await _db.ProductGroups
                .AsNoTracking()
                .Where(pg => pg.IsActive)
                .OrderBy(pg => pg.Name)
                .Select(pg => new { pg.ProductGroupId, pg.Name })
                .ToListAsync();

            ViewBag.ProductGroups = new SelectList(productGroups, "ProductGroupId", "Name", filterProductGroupId);

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

            return View(model);
        }







        // =========================================================
        // 2) تفاصيل صنف واحد
        // =========================================================
        public async Task<IActionResult> Details(int id)   // يستقبل رقم الصنف (ProdId)
        {
            if (id <= 0) return NotFound();

            var product = await _db.Products.FindAsync(id);
            if (product == null) return NotFound();

            return View(product);
        }







        // =========================================================
        // 3) إضافة صنف جديد
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            // تحميل قائمة الفئات + مجموعات الأصناف + مجموعات البونص
            await LoadCategoriesDDLAsync(null);
            await LoadProductGroupsDDLAsync(null);          // جديد: مجموعات الأصناف
            await LoadBonusGroupsDDLAsync(null);            // جديد: مجموعات البونص

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
        public async Task<IActionResult> Create(Product model)
        {
            if (!ModelState.IsValid)
            {
                // إعادة تحميل القوائم لو حصل خطأ تحقق
                await LoadCategoriesDDLAsync(model.CategoryId);
                await LoadProductGroupsDDLAsync(model.ProductGroupId);
                await LoadBonusGroupsDDLAsync(model.ProductBonusGroupId);
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
        public async Task<IActionResult> Edit(int id)
        {
            if (id <= 0) return NotFound();

            var m = await _db.Products.FindAsync(id);
            if (m == null) return NotFound();

            await LoadCategoriesDDLAsync(m.CategoryId);
            await LoadProductGroupsDDLAsync(m.ProductGroupId);
            await LoadBonusGroupsDDLAsync(m.ProductBonusGroupId);
            ViewBag.ImportedOptions = GetImportedOptions(m.Imported);

            return View(m);
        }







        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Product model)
        {
            // حماية: لو حد غيّر الـ Id في الفورم
            if (id != model.ProdId) return BadRequest();

            if (!ModelState.IsValid)
            {
                await LoadCategoriesDDLAsync(model.CategoryId);
                await LoadProductGroupsDDLAsync(model.ProductGroupId);
                await LoadBonusGroupsDDLAsync(model.ProductBonusGroupId);
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
            await _db.SaveChangesAsync();

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

            TempData["Msg"] = "تم تعديل الصنف بنجاح.";
            return RedirectToAction(nameof(Index));
        }







        // =========================================================
        // 5) حذف صنف واحد (النموذج التقليدي)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            if (id <= 0) return NotFound();

            var m = await _db.Products.FindAsync(id);
            if (m == null) return NotFound();

            return View(m);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
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

            TempData["Msg"] = $"تم حذف {products.Count} صنف/أصناف بنجاح.";
            return RedirectToAction(nameof(Index));
        }










        // =========================================================
        // 7) حذف جميع الأصناف (DeleteAll) — استخدمه بحذر وبصلاحيات
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
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

            TempData["Msg"] = $"تم حذف جميع الأصناف ({allProducts.Count}) بنجاح.";
            return RedirectToAction(nameof(Index));
        }









        // =========================
        // Export — تصدير قائمة الأصناف (Excel أو CSV)
        // =========================
        [HttpGet]
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
            string? format = "excel")    // excel | csv
        {
            // متغير: الاستعلام الأساسي بدون تتبّع لزيادة السرعة
            IQueryable<Product> q = _db.Products
                .Include(p => p.ProductGroup)
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

                "comp" or "company" =>
                    (desc ? q.OrderByDescending(p => p.Company)
                          : q.OrderBy(p => p.Company)),

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
                    Csv("الشركة"),
                    Csv("محلي/مستورد"),
                    Csv("فعال؟"),
                    Csv("سعر الجمهور"),
                    Csv("تاريخ الإنشاء"),
                    Csv("آخر تعديل"),
                    Csv("آخر تغيير سعر"),
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
                        Csv(p.Company),
                        Csv(p.Imported?.ToString()),
                        Csv(p.IsActive ? "نشط" : "موقوف"),
                        Csv(p.PriceRetail.ToString(CultureInfo.InvariantCulture)),
                        Csv(p.CreatedAt.ToString("yyyy-MM-dd HH:mm")),
                        Csv(p.UpdatedAt.ToString("yyyy-MM-dd HH:mm")),
                        Csv(p.LastPriceChangeDate?.ToString("yyyy-MM-dd")),
                        Csv(p.Description)
                    ));
                }

                // تحويل إلى UTF-8 مع BOM علشان Excel يقرا عربى صح
                var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
                var bytes = utf8.GetBytes(sbCsv.ToString());
                var fileNameCsv = $"Products_{DateTime.Now:yyyyMMdd_HHmm}_csv.csv";

                return File(bytes, "text/csv; charset=utf-8", fileNameCsv);
            }

            // ========= الفرع الثاني: Excel (XLSX) =========
            using var workbook = new XLWorkbook();             // متغير: ملف Excel
            var ws = workbook.Worksheets.Add("Products");      // متغير: ورقة العمل

            int r = 1; // متغير: رقم الصف الحالى

            // عناوين الأعمدة
            ws.Cell(r, 1).Value = "كود الصنف";
            ws.Cell(r, 2).Value = "اسم الصنف";
            ws.Cell(r, 3).Value = "الباركود";
            ws.Cell(r, 4).Value = "الاسم العلمي";
            ws.Cell(r, 5).Value = "كود الفئة";
            ws.Cell(r, 6).Value = "الشركة";
            ws.Cell(r, 7).Value = "محلي/مستورد";
            ws.Cell(r, 8).Value = "فعال؟";
            ws.Cell(r, 9).Value = "سعر الجمهور";
            ws.Cell(r, 10).Value = "تاريخ الإنشاء";
            ws.Cell(r, 11).Value = "آخر تعديل";
            ws.Cell(r, 12).Value = "آخر تغيير سعر";
            ws.Cell(r, 13).Value = "الوصف";

            // تنسيق صف العناوين
            var headerRange = ws.Range(r, 1, r, 13);
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
                ws.Cell(r, 6).Value = p.Company ?? "";
                ws.Cell(r, 7).Value = p.Imported?.ToString() ?? "";
                ws.Cell(r, 8).Value = p.IsActive ? "نشط" : "موقوف";
                ws.Cell(r, 9).Value = p.PriceRetail;
                ws.Cell(r, 10).Value = p.CreatedAt;
                ws.Cell(r, 11).Value = p.UpdatedAt;
                ws.Cell(r, 12).Value = p.LastPriceChangeDate;
                ws.Cell(r, 13).Value = p.Description ?? "";
            }

            // ضبط عرض الأعمدة تلقائيًا
            ws.Columns().AdjustToContents();

            // تنسيق سعر الجمهور
            ws.Column(9).Style.NumberFormat.Format = "0.00";

            using var stream = new MemoryStream();     // متغير: ذاكرة مؤقتة
            workbook.SaveAs(stream);
            stream.Position = 0;

            var fileNameXlsx = $"Products_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
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

            // نجيب أول 20 صنف فيهم النص المكتوب في الاسم
            var results = await _db.Products
                .AsNoTracking()
                .Where(p => p.ProdName.Contains(term))
                .OrderBy(p => p.ProdName)
                .Select(p => new
                {
                    id = p.ProdId,          // كود الصنف
                    name = p.ProdName,      // اسم الصنف
                    company = p.Company,    // الشركة (اختيارى للعرض)
                    barcode = p.Barcode     // الباركود (اختيارى للعرض)
                })
                .Take(20)
                .ToListAsync();

            // نرجّع النتيجة كـ JSON علشان الجافاسكربت تستخدمها
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

            // بحث بالكود (ProdId) أو الباركود كنص
            var query = _db.Products
                .AsNoTracking()
                .Where(p =>
                    p.ProdId.ToString().Contains(term) ||
                    (p.Barcode != null && p.Barcode.Contains(term)));

            var results = await query
                .OrderBy(p => p.ProdId)
                .Select(p => new
                {
                    id = p.ProdId,
                    name = p.ProdName,
                    company = p.Company,
                    barcode = p.Barcode
                })
                .Take(20)
                .ToListAsync();

            return Json(results);
        }









        // =========================================================
        // بحث Ajax عن العملاء / الأطراف بالاسم (فلتر اختيارى فى حركة الصنف)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> SearchParties(string term)
        {
            if (string.IsNullOrWhiteSpace(term))
                return Json(Array.Empty<object>());

            term = term.Trim();

            // هنا نفترض أن كل العملاء والموردين فى جدول Customers
            var results = await _db.Customers
                .AsNoTracking()
                .Where(c => c.CustomerName.Contains(term))
                .OrderBy(c => c.CustomerName)
                .Select(c => new
                {
                    id = c.CustomerId,
                    name = c.CustomerName
                })
                .Take(20)
                .ToListAsync();

            return Json(results);
        }

        /// <summary>
        /// شاشة حركة صنف.
        /// - يمكن فتحها من المينيو بدون كود → يتم اختيار الصنف من الشاشة.
        /// - يمكن فتحها من زر "حركة الصنف" فى قائمة الأصناف مع id.
        /// تستقبل أيضاً كود مخزن + فترة من/إلى + عميل اختيارى للتصفية.
        /// </summary>
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

            // ===== 5) تحميل قائمة الأصناف للأوتوكومبليت (datalist) - دائماً حتى يعمل datalist =====
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
                .FirstOrDefaultAsync(p => p.ProdId == finalProdId.Value);

            if (product == null)
                return NotFound();

            ViewBag.SelectedProdName = product.ProdName;      // اسم الصنف فى بوكس البحث
            ViewBag.PriceRetail = product.PriceRetail;        // سعر الجمهور من جدول الأصناف

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
                    DiscountPercent = sil.PriceRetail > 0 
                        ? ((sil.PriceRetail - sil.UnitSalePrice) / sil.PriceRetail) * 100m
                        : 0m,
                    NetAmount = sil.LineNetTotal
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

            // حساب صافي الكمية = Sum(QtyIn) - Sum(QtyOut)
            int totalQtyIn = await stockLedgerQuery.SumAsync(sl => sl.QtyIn);
            int totalQtyOut = await stockLedgerQuery.SumAsync(sl => sl.QtyOut);
            int calculatedNetQty = totalQtyIn - totalQtyOut;
            ViewBag.NetQty = calculatedNetQty;

            // حساب صافي المبلغ من StockLedger (إجمالي التكلفة)
            decimal calculatedNetAmount = await stockLedgerQuery
                .Where(sl => sl.QtyIn > 0)
                .SumAsync(sl => (sl.TotalCost ?? (sl.QtyIn * sl.UnitCost))) 
                - await stockLedgerQuery
                    .Where(sl => sl.QtyOut > 0)
                    .SumAsync(sl => (sl.TotalCost ?? (sl.QtyOut * sl.UnitCost)));
            ViewBag.NetAmount = calculatedNetAmount;

            // ===== 10) حساب الخصم المرجح من StockAnalysisService =====
            // الخصم المرجح = متوسط الخصم الموزون للمشتريات المتبقية من StockLedger
            // يعتمد على: SourceType == "Purchase" و RemainingQty > 0
            // الصيغة: Sum(RemainingQty × PurchaseDiscount) / Sum(RemainingQty)
            decimal calculatedWeightedDiscount = await _stockAnalysisService.GetWeightedPurchaseDiscountCurrentAsync(finalProdId.Value);
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
                    Amount = srl.LineNetTotal
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
        public IActionResult Import()
        {
            // ممكن نستخدم TempData لرسائل النجاح/الخطأ بين الريدايركتات
            ViewBag.Success = TempData["Success"];
            ViewBag.Error = TempData["Error"];

            return View();
        }

        // =====================
        // POST: Products/Import
        // استيراد الأصناف من ملف إكسل
        // =====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Import(IFormFile excelFile)   // متغيّر: ملف الإكسل المرفوع من الفيو
        {
            // ===== 1) التأكد من اختيار ملف =====
            if (excelFile == null || excelFile.Length == 0)
            {
                TempData["Error"] = "من فضلك اختر ملف إكسل أولاً.";
                return RedirectToAction(nameof(Import));   // رجوع لنفس الصفحة مع رسالة خطأ
            }

            try
            {
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

                // ===== 3) خريطة الهيدر (اسم العمود -> رقم العمود) =====
                var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);  // متغيّر: يحتفظ بأرقام الأعمدة حسب أسمائها

                for (int col = 1; col <= lastCol; col++)
                {
                    var headerText = sheet.Cells[1, col].Text?.Trim(); // متغيّر: نص الهيدر فى الصف الأول
                    if (!string.IsNullOrWhiteSpace(headerText) && !headers.ContainsKey(headerText))
                    {
                        headers[headerText] = col;                     // حفظ اسم العمود مع رقمه
                    }
                }

                // ===== 4) الأعمدة المطلوبة فى ملف الإكسل (من غير units) =====
                string[] requiredHeaders =
                {
                    "ProdName",
                    "PriceRetail",
                    "GenericName",
                    "Company",
                    "Description",
                    "DosageForm",
                    "Barcode",
                    "Imported"
                };

                var missing = requiredHeaders
                    .Where(h => !headers.ContainsKey(h))
                    .ToList();

                if (missing.Any())
                {
                    TempData["Error"] = "بعض الأعمدة غير موجودة في الملف: " +
                                        string.Join(", ", missing);
                    return RedirectToAction(nameof(Import));
                }

                // ===== 5) تجهيز قائمة الأصناف التى سيتم حفظها =====
                var productsToInsert = new List<Product>(); // متغيّر: قائمة الأصناف الجديدة

                for (int row = 2; row <= lastRow; row++)    // نبدأ من الصف الثانى بعد الهيدر
                {
                    // ---- اسم الصنف (لو فاضى نتخطى الصف) ----
                    string prodName = sheet.Cells[row, headers["ProdName"]].Text?.Trim();
                    if (string.IsNullOrWhiteSpace(prodName))
                        continue;

                    // ---- سعر الجمهور ----
                    decimal priceRetail = 0m;
                    var priceText = sheet.Cells[row, headers["PriceRetail"]].Text?.Trim();

                    if (!string.IsNullOrWhiteSpace(priceText))
                    {
                        decimal tmp;
                        // نحاول نقرأ الرقم بأسلوبين (عشان الفاصلة أو النقطة)
                        if (decimal.TryParse(priceText, NumberStyles.Any, CultureInfo.InvariantCulture, out tmp) ||
                            decimal.TryParse(priceText, NumberStyles.Any, CultureInfo.GetCultureInfo("ar-EG"), out tmp))
                        {
                            priceRetail = tmp;
                        }
                    }

                    // ---- إنشاء كائن Product جديد ----
                    var product = new Product
                    {
                        // Id: تلقائي Identity من قاعدة البيانات
                        ProdName = prodName,                                                   // اسم الصنف
                        PriceRetail = priceRetail,                                             // سعر الجمهور
                        GenericName = sheet.Cells[row, headers["GenericName"]].Text?.Trim(),   // الاسم العلمي
                        Company = sheet.Cells[row, headers["Company"]].Text?.Trim(),           // الشركة
                        Description = sheet.Cells[row, headers["Description"]].Text?.Trim(),   // الوصف
                        DosageForm = sheet.Cells[row, headers["DosageForm"]].Text?.Trim(),     // الشكل الدوائي
                        Barcode = sheet.Cells[row, headers["Barcode"]].Text?.Trim(),           // الباركود
                        Imported = sheet.Cells[row, headers["Imported"]].Text?.Trim(),         // محلى / مستورد

                        IsActive = true,                                                       // جعل الصنف نشط افتراضياً
                        CreatedAt = DateTime.Now,                                              // تاريخ الإنشاء
                        UpdatedAt = DateTime.Now                                               // آخر تعديل
                    };

                    productsToInsert.Add(product);
                }

                if (productsToInsert.Count == 0)
                {
                    TempData["Error"] = "لم يتم العثور على أي صف صالح للاستيراد.";
                    return RedirectToAction(nameof(Import));
                }

                // ===== 6) الحفظ في قاعدة البيانات مرة واحدة =====
                _db.Products.AddRange(productsToInsert);  // إضافة جميع الأصناف إلى الكونتكست
                await _db.SaveChangesAsync();             // حفظ فعلي فى قاعدة البيانات

                TempData["Success"] = $"تم استيراد {productsToInsert.Count} صنفاً بنجاح.";
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
    }
}
