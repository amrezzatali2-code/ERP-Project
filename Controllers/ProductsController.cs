// Controllers/ProductsController.cs
using ClosedXML.Excel;                  // مكتبة Excel
using OfficeOpenXml;
using DocumentFormat.OpenXml.InkML;
using ERP.Data;                         // سياق قاعدة البيانات الرئيسي
using ERP.Infrastructure;               // كلاس PagedResult<T> لتقسيم الصفحات
using ERP.Models;                       // الموديلات Product, Category,...
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

namespace ERP.Controllers
{
    /// <summary>
    /// الكنترولر الخاص بالأصناف: قائمة + تفاصيل + إضافة + تعديل + حذف + حركة صنف (Show)
    /// </summary>
    public class ProductsController : Controller
    {
        // كائن الـ DbContext للتعامل مع قاعدة البيانات
        private readonly AppDbContext _db;

        public ProductsController(AppDbContext db) => _db = db;

        // =========================================================
        // 1) قائمة الأصناف  (نظام القوائم الموحد)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Index(
            string? search,                    // نص البحث
            string? searchBy = "all",          // all|name|id|code|gen|category|comp|price|active|imported|updated
            string? sort = "name",             // name|price|updated|category|comp|code|id|created|modified|isactive|imported
            string? dir = "asc",               // asc | desc
            bool useDateRange = false,         // هل فلترة التاريخ مفعلة؟
            DateTime? fromDate = null,         // تاريخ/وقت من (CreatedAt)
            DateTime? toDate = null,           // تاريخ/وقت إلى (CreatedAt)
            int page = 1,                      // رقم الصفحة الحالية
            int pageSize = 50                  // عدد السطور في الصفحة
        )
        {
            // (1) الاستعلام الأساسي (بدون تتبّع لتحسين الأداء)
            //      + تحميل مجموعة الصنف ومجموعة البونص لعرض أسمائهما في الجدول
            IQueryable<Product> q = _db.Products
                .Include(p => p.ProductGroup)        // متغير: تحميل مجموعة الصنف
                .Include(p => p.ProductBonusGroup)   // متغير: تحميل مجموعة البونص
                .AsNoTracking();

            // (2) تنظيف قيم الفلاتر
            var s = (search ?? string.Empty).Trim();                      // نص البحث
            var sb = (searchBy ?? "all").Trim().ToLowerInvariant();      // نوع البحث
            var so = (sort ?? "name").Trim().ToLowerInvariant();         // عمود الترتيب
            var desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase); // هل الترتيب تنازلي؟

            // (3) تطبيق البحث حسب نوعه
            if (!string.IsNullOrWhiteSpace(s))
            {
                switch (sb)
                {
                    case "id":   // البحث برقم الصنف (ProdId كـ int)
                        if (int.TryParse(s, out var prodIdVal))
                        {
                            q = q.Where(p => p.ProdId == prodIdVal);
                        }
                        else
                        {
                            // لو المستخدم كتب نص مش رقم نبحث داخل ProdId كنص
                            q = q.Where(p => p.ProdId.ToString().Contains(s));
                        }
                        break;

                    case "name": // البحث باسم الصنف
                        q = q.Where(p => p.ProdName != null && p.ProdName.Contains(s));
                        break;

                    case "code": // البحث بالباركود
                        q = q.Where(p => p.Barcode != null && p.Barcode.Contains(s));
                        break;

                    case "gen":  // البحث بالاسم العلمي
                        q = q.Where(p => p.GenericName != null && p.GenericName.Contains(s));
                        break;

                    case "category":   // البحث برقم الفئة (CategoryId كـ int?)
                        if (int.TryParse(s, out var catIdVal))
                        {
                            q = q.Where(p => p.CategoryId == catIdVal);
                        }
                        else
                        {
                            q = q.Where(p =>
                                p.CategoryId.HasValue &&
                                p.CategoryId.Value.ToString().Contains(s));
                        }
                        break;

                    case "comp": // البحث باسم الشركة
                        q = q.Where(p => p.Company != null && p.Company.Contains(s));
                        break;

                    case "price":   // البحث بسعر الجمهور
                        if (decimal.TryParse(s, out var priceVal))
                            q = q.Where(p => p.PriceRetail == priceVal);
                        else
                            q = q.Where(p => p.PriceRetail.ToString().Contains(s));
                        break;

                    case "active":  // البحث في حالة التفعيل (فعال/متوقف)
                        {
                            var yes = new[] { "1", "نعم", "yes", "true", "صح" };
                            var no = new[] { "0", "لا", "no", "false" };

                            if (yes.Contains(s, StringComparer.OrdinalIgnoreCase))
                                q = q.Where(p => p.IsActive);
                            else if (no.Contains(s, StringComparer.OrdinalIgnoreCase))
                                q = q.Where(p => !p.IsActive);

                            break;
                        }

                    case "imported": // البحث في حقل محلي/مستورد
                        q = q.Where(p => p.Imported != null && p.Imported.Contains(s));
                        break;

                    case "updated":  // البحث في تاريخ آخر تغيير سعر
                        if (DateTime.TryParse(s, out var d))
                        {
                            q = q.Where(p => p.LastPriceChangeDate.HasValue &&
                                             p.LastPriceChangeDate.Value.Date == d.Date);
                        }
                        else
                        {
                            q = q.Where(p => p.LastPriceChangeDate.HasValue &&
                                             p.LastPriceChangeDate.Value.ToString().Contains(s));
                        }
                        break;

                    case "all":
                    default:         // بحث شامل في أهم الحقول
                        q = q.Where(p =>
                            p.ProdId.ToString().Contains(s) ||                            // رقم الصنف
                            (p.ProdName != null && p.ProdName.Contains(s)) ||            // اسم الصنف
                            (p.Barcode != null && p.Barcode.Contains(s)) ||             // الباركود
                            (p.GenericName != null && p.GenericName.Contains(s)) ||     // الاسم العلمي
                            (p.Company != null && p.Company.Contains(s)) ||             // الشركة
                            (p.Description != null && p.Description.Contains(s)) ||     // الوصف
                            (p.CategoryId.HasValue &&
                             p.CategoryId.Value.ToString().Contains(s)) ||              // رقم الفئة
                            (p.Imported != null && p.Imported.Contains(s))              // محلي/مستورد
                        );
                        break;
                }
            }

            // (4) فلترة بالتاريخ (تاريخ إنشاء الصنف CreatedAt)
            DateTime? from = null;
            DateTime? to = null;

            if (useDateRange)
            {
                if (fromDate.HasValue)
                {
                    from = fromDate.Value;
                    q = q.Where(p => p.CreatedAt >= from.Value);
                }

                if (toDate.HasValue)
                {
                    to = toDate.Value;
                    q = q.Where(p => p.CreatedAt <= to.Value);
                }
            }

            // (5) الترتيب حسب العمود المطلوب
            q = so switch
            {
                "id" => (desc ? q.OrderByDescending(p => p.ProdId)
                              : q.OrderBy(p => p.ProdId)),

                "name" => (desc ? q.OrderByDescending(p => p.ProdName)
                                : q.OrderBy(p => p.ProdName)),

                "code" => (desc ? q.OrderByDescending(p => p.Barcode)
                                : q.OrderBy(p => p.Barcode)),

                "gen" => (desc ? q.OrderByDescending(p => p.GenericName)
                               : q.OrderBy(p => p.GenericName)),

                "category" => (desc ? q.OrderByDescending(p => p.CategoryId)
                                    : q.OrderBy(p => p.CategoryId)),

                "comp" => (desc ? q.OrderByDescending(p => p.Company)
                                : q.OrderBy(p => p.Company)),

                "price" => (desc ? q.OrderByDescending(p => p.PriceRetail)
                                 : q.OrderBy(p => p.PriceRetail)),

                "created" => (desc ? q.OrderByDescending(p => p.CreatedAt)
                                   : q.OrderBy(p => p.CreatedAt)),

                "modified" => (desc ? q.OrderByDescending(p => p.UpdatedAt)
                                    : q.OrderBy(p => p.UpdatedAt)),

                "updated" => (desc ? q.OrderByDescending(p => p.LastPriceChangeDate)
                                   : q.OrderBy(p => p.LastPriceChangeDate)),

                // 🔹 ترتيب حسب الحالة
                "isactive" => (desc ? q.OrderByDescending(p => p.IsActive)
                                    : q.OrderBy(p => p.IsActive)),

                // 🔹 ترتيب حسب محلي/مستورد
                "imported" => (desc ? q.OrderByDescending(p => p.Imported)
                                    : q.OrderBy(p => p.Imported)),

                _ => (desc ? q.OrderByDescending(p => p.ProdName)
                           : q.OrderBy(p => p.ProdName)),
            };

            // (6) الترقيم (Paging)
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50;

            int total = await q.CountAsync();                               // إجمالي عدد الأصناف
            int pages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
            if (page > pages) page = pages;

            var items = await q.Skip((page - 1) * pageSize)
                               .Take(pageSize)
                               .ToListAsync();                              // بيانات الصفحة الحالية

            var model = new PagedResult<Product>(items, page, pageSize, total)
            {
                // قيم الفلاتر لنظام القوائم الموحد
                Search = s,
                SortColumn = so,
                SortDescending = desc,
                UseDateRange = useDateRange,
                FromDate = from,
                ToDate = to
            };

            // تمرير القيم للفيو (للمحافظة على التكامل مع البارشال)
            ViewBag.Search = s;
            ViewBag.SearchBy = sb;
            ViewBag.Sort = so;
            ViewBag.Dir = desc ? "desc" : "asc";
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.Total = total;

            // (7) خيارات البحث للبارشال _IndexFilters
            ViewBag.SearchOptions = new[]
            {
                new SelectListItem{ Text="الكل",        Value="all",      Selected = sb=="all"      },
                new SelectListItem{ Text="الاسم",       Value="name",     Selected = sb=="name"     },
                new SelectListItem{ Text="المعرّف",     Value="id",       Selected = sb=="id"       },
                new SelectListItem{ Text="الباركود",    Value="code",     Selected = sb=="code"     },
                new SelectListItem{ Text="الاسم العلمي",Value="gen",      Selected = sb=="gen"      },
                new SelectListItem{ Text="الفئة",       Value="category", Selected = sb=="category" },
                new SelectListItem{ Text="الشركة",      Value="comp",     Selected = sb=="comp"     },
                new SelectListItem{ Text="السعر",       Value="price",    Selected = sb=="price"    },
                new SelectListItem{ Text="فعال",        Value="active",   Selected = sb=="active"   },
                new SelectListItem{ Text="مستورد",      Value="imported", Selected = sb=="imported" },
                new SelectListItem{ Text="تاريخ السعر", Value="updated",  Selected = sb=="updated"  },
            };

            // (8) خيارات الترتيب
            ViewBag.SortOptions = new[]
            {
                new SelectListItem{ Text="الاسم",             Value="name",      Selected = so=="name"      },
                new SelectListItem{ Text="السعر",             Value="price",     Selected = so=="price"     },
                new SelectListItem{ Text="تاريخ الإنشاء",     Value="created",   Selected = so=="created"   },
                new SelectListItem{ Text="آخر تعديل",         Value="modified",  Selected = so=="modified"  },
                new SelectListItem{ Text="آخر تغيير سعر",     Value="updated",   Selected = so=="updated"   },
                new SelectListItem{ Text="الفئة",             Value="category",  Selected = so=="category"  },
                new SelectListItem{ Text="الشركة",            Value="comp",      Selected = so=="comp"      },
                new SelectListItem{ Text="الباركود",          Value="code",      Selected = so=="code"      },
                new SelectListItem{ Text="المعرّف",           Value="id",        Selected = so=="id"        },
                new SelectListItem{ Text="الحالة",            Value="isactive",  Selected = so=="isactive"  },
                new SelectListItem{ Text="محلي/مستورد",      Value="imported",  Selected = so=="imported"  },
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
                _db.Products.Remove(m);
                await _db.SaveChangesAsync();
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
            string? format = "excel")    // excel | csv
        {
            // متغير: الاستعلام الأساسي بدون تتبّع لزيادة السرعة
            IQueryable<Product> q = _db.Products.AsNoTracking();

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
            ViewBag.Lines = Array.Empty<object>();             // حركة مجمعة
            ViewBag.SalesLines = Array.Empty<object>();        // فواتير المبيعات
            ViewBag.SalesReturnLines = Array.Empty<object>();  // مرتجعات المبيعات
            ViewBag.PurchaseLines = Array.Empty<object>();     // فواتير المشتريات
            ViewBag.PurchaseReturnLines = Array.Empty<object>(); // مرتجعات المشتريات

            ViewBag.YearlySummary = Array.Empty<object>();     // ملخص حسب السنوات
            ViewBag.WarehouseSummary = Array.Empty<object>();  // ملخص حسب المخازن

            // ===== 5) لو لسه ما اخترناش صنف → رجّع الشاشة من غير بيانات =====
            if (finalProdId == null)
            {
                ViewBag.SelectedProdName = "";
                return View(model: null);   // @model Product? فى الفيو
            }

            // ===== 6) تحميل بيانات بطاقة الصنف من جدول Products =====
            var product = await _db.Products
                .Include(p => p.Category)
                .FirstOrDefaultAsync(p => p.ProdId == finalProdId.Value);

            if (product == null)
                return NotFound();

            ViewBag.SelectedProdName = product.ProdName;      // اسم الصنف فى بوكس البحث
            ViewBag.PriceRetail = product.PriceRetail;        // سعر الجمهور من جدول الأصناف

            // ===== 7) لاحقاً: هنا هنضيف الربط الفعلى مع StockLedger / StockBatch =====
            // - تجميع المشتريات/المبيعات/المرتجعات من القيود المخزنية
            // - حساب الخصم المرجح
            // - ملء ViewBag.* بالأرقام الحقيقية + القوائم لكل تاب

            return View(product);   // الملف: Views/Products/Show.cshtml
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
