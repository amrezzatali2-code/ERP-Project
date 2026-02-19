using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using ERP.Data;
using ERP.Infrastructure;                  // PagedResult + ApplySearchSort + UserActivityLogger
using ERP.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إدارة جدول ترقيم المستندات:
    /// - عرض (بحث + ترتيب + فلترة تاريخ/وقت + صفحات) بنظام القوائم الموحد.
    /// - إنشاء / تعديل / تفاصيل / حذف السلاسل.
    /// </summary>
    public class DocumentSeriesController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IUserActivityLogger _activityLogger;

        public DocumentSeriesController(AppDbContext context, IUserActivityLogger activityLogger)
        {
            _context = context;
            _activityLogger = activityLogger;
        }

        // ======================= Index =======================
        // قائمة ترقيم المستندات بنظام القوائم الموحد
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",           // البحث الافتراضي: في الكل
            string? sort = "DocType",           // الترتيب الافتراضي: نوع المستند
            string? dir = "asc",                // اتجاه الترتيب
            bool useDateRange = false,          // هل نستخدم فلترة بالتاريخ؟
            DateTime? fromDate = null,          // من تاريخ الإنشاء
            DateTime? toDate = null,            // إلى تاريخ الإنشاء
            int page = 1,
            int pageSize = 50)
        {
            // 1) الاستعلام الأساسي
            var q = _context.DocumentSeries.AsNoTracking();

            // 2) خرائط الحقول للبحث والفرز
            //    (نفس الفكرة المستخدمة في الجداول الأخرى)
            var stringFields =
                new Dictionary<string, Expression<Func<DocumentSeries, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["docType"] = x => x.DocType,
                    ["series"] = x => x.SeriesCode,
                    ["year"] = x => x.FiscalYear!,
                    ["policy"] = x => x.ResetPolicy,
                    ["prefix"] = x => x.Prefix!
                };

            var intFields =
                new Dictionary<string, Expression<Func<DocumentSeries, int>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["current"] = x => x.CurrentNo,
                    ["width"] = x => x.NumberWidth
                };

            var orderFields =
                new Dictionary<string, Expression<Func<DocumentSeries, object>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["DocType"] = x => x.DocType,
                    ["SeriesCode"] = x => x.SeriesCode,
                    ["FiscalYear"] = x => x.FiscalYear ?? "",
                    ["ResetPolicy"] = x => x.ResetPolicy,
                    ["CurrentNo"] = x => x.CurrentNo,
                    ["NumberWidth"] = x => x.NumberWidth,
                    ["Prefix"] = x => x.Prefix ?? "",
                    ["CreatedAt"] = x => x.CreatedAt
                };

            // 3) فلترة بالتاريخ/الوقت حسب CreatedAt
            if (useDateRange)
            {
                if (fromDate.HasValue)
                    q = q.Where(x => x.CreatedAt >= fromDate.Value);

                if (toDate.HasValue)
                    q = q.Where(x => x.CreatedAt <= toDate.Value);
            }

            // 4) البحث
            var sb = (searchBy ?? "all").Trim().ToLowerInvariant();

            // حالة "البحث في الكل"
            if (!string.IsNullOrWhiteSpace(search) &&
                string.Equals(sb, "all", StringComparison.OrdinalIgnoreCase))
            {
                var term = search.Trim();

                q = q.Where(x =>
                    x.DocType.Contains(term) ||
                    x.SeriesCode.Contains(term) ||
                    ((x.FiscalYear ?? "").Contains(term)) ||
                    x.ResetPolicy.Contains(term) ||
                    ((x.Prefix ?? "").Contains(term))
                );

                // نطبّق الترتيب فقط عن طريق الهيلبر الموحد
                q = q.ApplySearchSort(
                    search: null,
                    searchBy: "docType",
                    sort: sort,
                    dir: dir,
                    stringFields: stringFields,
                    intFields: intFields,
                    orderFields: orderFields,
                    defaultSearchBy: "docType",
                    defaultSortBy: "DocType"
                );
            }
            else
            {
                // بحث + ترتيب عن طريق الهيلبر ApplySearchSort
                q = q.ApplySearchSort(
                    search,
                    sb,
                    sort,
                    dir,
                    stringFields,
                    intFields,
                    orderFields,
                    defaultSearchBy: "docType",
                    defaultSortBy: "DocType"
                );
            }

            // 5) تجهيز قيم الاتجاه
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            // 6) إنشاء PagedResult بنفس النظام الموحد
            var model = await PagedResult<DocumentSeries>.CreateAsync(
                q,
                page,
                pageSize,
                sort,
                desc,
                search,
                sb
            );

            // حفظ حالة فلترة التاريخ داخل الموديل
            model.UseDateRange = useDateRange;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            // قيم احتياطية للـ ViewBag لو احتجناها في الفيو
            ViewBag.Search = search ?? "";
            ViewBag.SearchBy = sb;
            ViewBag.Sort = sort ?? "DocType";
            ViewBag.Dir = desc ? "desc" : "asc";

            // خيارات البحث (تظهر في _IndexFilters)
            ViewBag.SearchOptions = new List<SelectListItem>
            {
                new("البحث في الكل","all")     { Selected = sb == "all"      },
                new("نوع المستند","docType")   { Selected = sb == "docType"  },
                new("السلسلة","series")        { Selected = sb == "series"   },
                new("السنة","year")            { Selected = sb == "year"     },
                new("السياسة","policy")        { Selected = sb == "policy"   },
                new("البادئة","prefix")        { Selected = sb == "prefix"   }
            };

            // خيارات الترتيب (تظهر في _IndexFilters)
            ViewBag.SortOptions = new List<SelectListItem>
            {
                new("نوع المستند","DocType")       { Selected = model.SortColumn == "DocType"     },
                new("السلسلة","SeriesCode")        { Selected = model.SortColumn == "SeriesCode"  },
                new("السنة","FiscalYear")          { Selected = model.SortColumn == "FiscalYear"  },
                new("السياسة","ResetPolicy")       { Selected = model.SortColumn == "ResetPolicy" },
                new("العدد الحالي","CurrentNo")    { Selected = model.SortColumn == "CurrentNo"   },
                new("عرض الرقم","NumberWidth")     { Selected = model.SortColumn == "NumberWidth" },
                new("البادئة","Prefix")            { Selected = model.SortColumn == "Prefix"      },
                new("تاريخ الإنشاء","CreatedAt")   { Selected = model.SortColumn == "CreatedAt"   }
            };

            return View(model);
        }

        // ======================= Details =======================
        public async Task<IActionResult> Details(int id)
        {
            var m = await _context.DocumentSeries
                                  .AsNoTracking()
                                  .FirstOrDefaultAsync(x => x.SeriesId == id);

            if (m == null) return NotFound();
            return View(m);
        }

        // ======================= Create (GET) ==================
        public IActionResult Create()
        {
            // نرجّع موديل بقيم افتراضية
            return View(new DocumentSeries
            {
                ResetPolicy = "Continuous",
                NumberWidth = 6,
                CurrentNo = 0
            });
        }

        // ======================= Create (POST) =================
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(DocumentSeries m)
        {
            // تحقق إضافي: لو Yearly لازم السنة تتحدد
            if (string.Equals(m.ResetPolicy, "Yearly", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(m.FiscalYear))
            {
                ModelState.AddModelError(nameof(m.FiscalYear), "يجب تحديد السنة مع سياسة Yearly.");
            }

            if (!ModelState.IsValid)
                return View(m);

            // تعيين تاريخ الإنشاء
            m.CreatedAt = DateTime.Now;
            m.UpdatedAt = null;

            _context.DocumentSeries.Add(m);
            await _context.SaveChangesAsync();

            await _activityLogger.LogAsync(UserActionType.Create, "DocumentSeries", m.SeriesId, $"إنشاء سلسلة مستندات: {m.DocType}");

            TempData["ok"] = "تمت إضافة السلسلة بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // ======================= Edit (GET) ====================
        public async Task<IActionResult> Edit(int id)
        {
            var m = await _context.DocumentSeries.FirstOrDefaultAsync(x => x.SeriesId == id);
            if (m == null) return NotFound();
            return View(m);
        }

        // ======================= Edit (POST) ===================
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, DocumentSeries m)
        {
            if (id != m.SeriesId)
                return NotFound();

            if (string.Equals(m.ResetPolicy, "Yearly", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(m.FiscalYear))
            {
                ModelState.AddModelError(nameof(m.FiscalYear), "يجب تحديد السنة مع سياسة Yearly.");
            }

            if (!ModelState.IsValid)
                return View(m);

            try
            {
                // تحديث تاريخ التعديل
                m.UpdatedAt = DateTime.Now;

                // تعليق: EF سيستخدم RowVersion (Timestamp) لمنع الكتابة المتزامنة
                _context.Update(m);
                await _context.SaveChangesAsync();

                await _activityLogger.LogAsync(UserActionType.Edit, "DocumentSeries", id, $"تعديل سلسلة مستندات: {m.DocType}");

                TempData["ok"] = "تم تعديل السلسلة بنجاح.";
            }
            catch (DbUpdateConcurrencyException)
            {
                // في حالة حذف السجل من مستخدم آخر
                if (!await _context.DocumentSeries.AnyAsync(x => x.SeriesId == id))
                    return NotFound();

                throw; // نعيد الخطأ لو المشكلة غير متوقعة
            }

            return RedirectToAction(nameof(Index));
        }

        // ======================= Delete (GET) ==================
        public async Task<IActionResult> Delete(int id)
        {
            var m = await _context.DocumentSeries
                                  .AsNoTracking()
                                  .FirstOrDefaultAsync(x => x.SeriesId == id);
            if (m == null) return NotFound();
            return View(m);
        }

        // ======================= Delete (POST) =================
        [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var m = await _context.DocumentSeries.FirstOrDefaultAsync(x => x.SeriesId == id);
            if (m == null) return NotFound();

            _context.DocumentSeries.Remove(m);
            await _context.SaveChangesAsync();

            await _activityLogger.LogAsync(UserActionType.Delete, "DocumentSeries", id, $"حذف سلسلة مستندات: {m.DocType}");

            TempData["ok"] = "تم حذف السلسلة.";
            return RedirectToAction(nameof(Index));
        }
    }
}
