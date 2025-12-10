using System;
using System.Collections.Generic;                 // القوائم (List)
using System.Linq;                                // أوامر LINQ: Where / OrderBy / Skip / Take
using System.Threading.Tasks;                     // async / Task
using ERP.Data;                                   // AppDbContext
using ERP.Infrastructure;                         // كلاس PagedResult لتقسيم الصفحات
using ERP.Models;                                 // الموديلات (PurchaseRequest, PRLine, Customer, Warehouse)
using Microsoft.AspNetCore.Mvc;                   // Controller / IActionResult
using Microsoft.EntityFrameworkCore;              // Include / AsNoTracking
using ERP.Services;                               // استخدام DocumentTotalsService (سيرفيس إجماليات)

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إدارة "طلبات الشراء" (PurchaseRequests)
    /// - قائمة طلبات الشراء بنظام القوائم الموحّد
    /// - فتح طلب جديد / عرض طلب قديم
    /// - حذف مجموعة / حذف الكل
    /// - تصدير النتائج إلى CSV
    /// </summary>
    public class PurchaseRequestsController : Controller
    {
        private readonly AppDbContext _context;              // متغير: اتصال قاعدة البيانات
        private readonly DocumentTotalsService _docTotals;   // متغير: خدمة حساب إجماليات المستندات

        public PurchaseRequestsController(AppDbContext context,
                                           DocumentTotalsService docTotals)
        {
            _context = context;     // تخزين سياق قاعدة البيانات
            _docTotals = docTotals;   // تخزين سيرفيس الإجماليات لإعادة حساب إجماليات الطلب
        }


        /// <summary>
        /// تجهيز القوائم المنسدلة (الموردين + المخازن) لشاشة فاتورة المشتريات.
        /// هنا بنعرض الموردين فقط: PartyCategory = "Supplier".
        /// </summary>
        private async Task PopulateDropDownsAsync(int? selectedSupplierId = null,
                                                  int? selectedWarehouseId = null)
        {
            // ===== الموردون فقط (نوع الطرف = Supplier) =====
            var suppliers = await _context.Customers
                .AsNoTracking()
                .OrderBy(c => c.CustomerName)
                .Select(c => new
                {
                    Id = c.CustomerId,                    // متغير: كود المورد
                    Name = c.CustomerName,                  // متغير: اسم المورد
                    UserName = "",                              // حالياً مفيش مستخدم مربوط – نخليها فاضية
                    Phone = c.Phone1,
                    Address = c.Address
                })
                .ToListAsync();

            // دى اللى بتتقرى في الـ datalist فى Show.cshtml
            ViewBag.Customers = suppliers;

            // ===== المخازن =====
            var warehouses = await _context.Warehouses
                .AsNoTracking()
                .OrderBy(w => w.WarehouseName)
                .ToListAsync();

            ViewBag.Warehouses = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                warehouses,
                "WarehouseId",
                "WarehouseName",
                selectedWarehouseId
            );
        }





        // ========================================================
        // GET: PurchaseRequests/Index
        // شاشة "قائمة طلبات الشراء" بنظام القوائم الموحّد
        // ========================================================
        public async Task<IActionResult> Index(
            string? search,                      // نص البحث الحر
            string? searchBy,                    // نوع البحث (id / customer / warehouse / status / date / all)
            string? sort,                        // عمود الترتيب (id / date / needby / customer / warehouse / status / created)
            string? dir,                         // اتجاه الترتيب asc / desc
            int page = 1,                        // رقم الصفحة
            int pageSize = 25,                   // عدد السطور في الصفحة
            bool useDateRange = false,           // تفعيل فلتر التاريخ؟
            DateTime? fromDate = null,           // تاريخ من
            DateTime? toDate = null,             // تاريخ إلى
            int? fromCode = null,                // من رقم طلب
            int? toCode = null,                  // إلى رقم طلب
            string? dateField = null             // اسم حقل التاريخ المستخدم (هنا PRDate)
        )
        {
            // 1) قيم افتراضية للباراميترات لو مش جاية من الكويري
            searchBy ??= "id";          // البحث الافتراضي برقم الطلب
            sort ??= "date";         // الترتيب الافتراضي بتاريخ الطلب
            dir = (dir == "asc") ? "asc" : "desc";
            dateField ??= "PRDate";

            // 2) استعلام الأساس مع Include للعميل والمخزن
            var query = _context.PurchaseRequests
                .Include(pr => pr.Customer)      // بيانات العميل
                .Include(pr => pr.Warehouse)     // بيانات المخزن
                .AsNoTracking()                  // للقراءة فقط
                .AsQueryable();

            // 3) تطبيق البحث حسب نوعه
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();

                switch (searchBy)
                {
                    case "id":      // البحث برقم الطلب
                        if (int.TryParse(s, out var idVal))
                        {
                            query = query.Where(pr => pr.PRId == idVal);
                        }
                        else
                        {
                            query = query.Where(pr => pr.PRId.ToString().Contains(s));
                        }
                        break;

                    case "customer": // البحث باسم العميل أو كوده
                        query = query.Where(pr =>
                            pr.Customer.CustomerName.Contains(s) ||
                            pr.CustomerId.ToString().Contains(s));
                        break;

                    case "warehouse": // البحث باسم المخزن أو كوده
                        query = query.Where(pr =>
                            pr.Warehouse.WarehouseName.Contains(s) ||
                            pr.WarehouseId.ToString().Contains(s));
                        break;

                    case "status":   // البحث بالحالة
                        query = query.Where(pr => pr.Status.Contains(s));
                        break;

                    case "date":     // البحث بتاريخ محدد مكتوب في صندوق البحث
                        if (DateTime.TryParse(s, out var d))
                        {
                            var dateOnly = d.Date;
                            query = query.Where(pr => pr.PRDate.Date == dateOnly);
                        }
                        break;

                    default:         // بحث عام في أكثر من حقل
                        query = query.Where(pr =>
                            pr.PRId.ToString().Contains(s) ||
                            pr.Customer.CustomerName.Contains(s) ||
                            pr.Status.Contains(s));
                        break;
                }
            }

            // 4) فلتر التاريخ من/إلى على PRDate
            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                var from = fromDate.Value.Date;
                var to = toDate.Value.Date;
                query = query.Where(pr => pr.PRDate.Date >= from && pr.PRDate.Date <= to);
            }

            // 5) فلتر "من رقم / إلى رقم"
            if (fromCode.HasValue)
                query = query.Where(pr => pr.PRId >= fromCode.Value);

            if (toCode.HasValue)
                query = query.Where(pr => pr.PRId <= toCode.Value);

            // 6) الترتيب
            bool descending = (dir == "desc");

            query = sort switch
            {
                "id" => descending
                    ? query.OrderByDescending(pr => pr.PRId)
                    : query.OrderBy(pr => pr.PRId),

                "date" => descending
                    ? query.OrderByDescending(pr => pr.PRDate).ThenByDescending(pr => pr.PRId)
                    : query.OrderBy(pr => pr.PRDate).ThenBy(pr => pr.PRId),

                "needby" => descending
                    ? query.OrderByDescending(pr => pr.NeedByDate).ThenByDescending(pr => pr.PRId)
                    : query.OrderBy(pr => pr.NeedByDate).ThenBy(pr => pr.PRId),

                "customer" => descending
                    ? query.OrderByDescending(pr => pr.Customer.CustomerName)
                    : query.OrderBy(pr => pr.Customer.CustomerName),

                "warehouse" => descending
                    ? query.OrderByDescending(pr => pr.Warehouse.WarehouseName)
                    : query.OrderBy(pr => pr.Warehouse.WarehouseName),

                "status" => descending
                    ? query.OrderByDescending(pr => pr.Status)
                    : query.OrderBy(pr => pr.Status),

                "created" => descending
                    ? query.OrderByDescending(pr => pr.CreatedAt).ThenByDescending(pr => pr.PRId)
                    : query.OrderBy(pr => pr.CreatedAt).ThenBy(pr => pr.PRId),

                _ => descending
                    ? query.OrderByDescending(pr => pr.PRDate).ThenByDescending(pr => pr.PRId)
                    : query.OrderBy(pr => pr.PRDate).ThenBy(pr => pr.PRId),
            };

            // 7) إعداد الترقيم (Paging)
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 25;

            var total = await query.CountAsync(); // إجمالي السطور في الفلتر الحالي

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // متغير: موديل نتيجة الترقيم لنظام القوائم الموّحد
            var model = new PagedResult<PurchaseRequest>
            {
                Items = items,
                TotalCount = total,
                PageNumber = page,
                PageSize = pageSize,
                Search = search,
                SearchBy = searchBy,
                SortColumn = sort,
                SortDescending = descending,
                UseDateRange = useDateRange,
                FromDate = fromDate,
                ToDate = toDate
                // من الممكن لاحقاً نضيف FromCode / ToCode لو حبيت توسّع PagedResult
            };

            // 8) قيم إضافية للواجهة (فلتر الكود من/إلى)
            ViewBag.Search = search;
            ViewBag.SearchBy = searchBy;
            ViewBag.Sort = sort;
            ViewBag.Dir = dir;
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.CodeFrom = fromCode;
            ViewBag.CodeTo = toCode;
            ViewBag.DateField = dateField;

            return View(model);
        }






        // ========================================================
        // GET: PurchaseRequests/Create
        // فتح شاشة طلب جديد باستخدام View: Show
        // ========================================================
        public async Task<IActionResult> Create()
        {
            // متغير: موديل طلب شراء جديد بالقيم الافتراضية
            var model = new PurchaseRequest
            {
                PRDate = DateTime.Today,     // تاريخ اليوم كقيمة افتراضية
                NeedByDate = null,               // المستخدم يحددها من الشاشة
                WarehouseId = 0,                  // يختار المخزن من الشاشة
                CustomerId = 0,                  // يختار المورد من الشاشة

                // إجماليات الطلب (هتتحسب من السطور عن طريق السيرفيس)
                TotalQtyRequested = 0,                 // إجمالي الكمية المطلوبة = 0 مبدئياً
                ExpectedItemsTotal = 0m,                // إجمالي التكلفة المتوقعة = 0 مبدئياً

                Status = "Draft",                  // الحالة الافتراضية: مسودة
                IsConverted = false,                    // الطلب لسه متحوّلش لفاتورة شراء

                CreatedAt = DateTime.UtcNow,            // وقت إنشاء السجل
                Lines = new List<PRLine>()          // قائمة سطور فاضية
            };

            // تحميل قائمة العملاء للمساعدة في الاختيار من الـ datalist
            await PopulateDropDownsAsync();

            // نستخدم نفس فيو Show لعرض الطلب الجديد
            return View("Show", model);
        }





        // =======================
        // GET: PurchaseRequests/Edit/5
        // شاشة تعديل رأس طلب الشراء
        // =======================
        public async Task<IActionResult> Edit(int? id)
        {
            // لو مفيش رقم طلب → نرجع 404
            if (id == null)
                return NotFound();

            // متغير: نجيب رأس طلب الشراء من قاعدة البيانات
            // مع ربط المخزن والعميل للعرض فقط
            var purchaseRequest = await _context.PurchaseRequests
                .Include(p => p.Warehouse)   // بيانات المخزن
                .Include(p => p.Customer)    // بيانات العميل / المورد
                .FirstOrDefaultAsync(p => p.PRId == id.Value);

            // لو الطلب مش موجود → نرجع 404
            if (purchaseRequest == null)
                return NotFound();

            // نرجع الموديل للفيو Edit.cshtml
            return View(purchaseRequest);
        }

        // =========================
        // Edit — GET: فتح طلب شراء قديم للعرض/التعديل
        // =========================
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            // تحقق بسيط من رقم الطلب
            if (id <= 0)
                return BadRequest("رقم طلب الشراء غير صالح.");

            // قراءة هيدر طلب الشراء من قاعدة البيانات (بدون تتبّع)
            var model = await _context.PurchaseRequests
                .AsNoTracking()
                .FirstOrDefaultAsync(pr => pr.PRId == id);

            // لو الطلب مش موجود
            if (model == null)
                return NotFound();

            // تعبئة القوائم المنسدلة (موردين، مخازن، مستخدمين... إلخ) لو عندك
            await PopulateDropDownsAsync();

            // فتح شاشة طلب الشراء (Edit = شاشة عرض + إمكانية التعديل حسب الصلاحيات)
            return View(model);
        }



        // =========================
        // Edit — POST: حفظ تعديل هيدر طلب الشراء
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, PurchaseRequest request)
        {
            // تأكد أن رقم الطلب في الرابط هو نفس الموجود في الموديل
            if (id != request.PRId)
                return NotFound();

            // لو فيه أخطاء تحقق (Validation) في الموديل
            if (!ModelState.IsValid)
            {
                // نرجّع القوائم المنسدلة قبل الرجوع للفيو
                await PopulateDropDownsAsync();
                return View(request);
            }

            try
            {
                // هنا لو الموديل فيه حقول زي UpdatedAt تقدر تحدثها يدويًا:
                // request.UpdatedAt = DateTime.Now;

                // تحديث الكيان في الـ DbContext
                _context.Update(request);

                // حفظ التغييرات فعلياً في قاعدة البيانات
                await _context.SaveChangesAsync();

                TempData["Msg"] = "تم تعديل طلب الشراء بنجاح.";
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateException ex)
            {
                // أي خطأ في التحديث/الحفظ
                ModelState.AddModelError(string.Empty, "تعذّر حفظ التعديلات: " + ex.Message);

                await PopulateDropDownsAsync();
                return View(request);
            }
        }








        // ========================================================
        // POST: PurchaseRequests/BulkDelete
        // حذف مجموعة طلبات مختارة
        // ========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["Error"] = "من فضلك اختر على الأقل طلب واحد للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var ids = selectedIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var id) ? id : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();

            if (!ids.Any())
            {
                TempData["Error"] = "لم يتم العثور على أرقام صالحة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var requests = await _context.PurchaseRequests
                .Where(pr => ids.Contains(pr.PRId))
                .ToListAsync();

            if (!requests.Any())
            {
                TempData["Error"] = "لا توجد طلبات مطابقة للأرقام المختارة.";
                return RedirectToAction(nameof(Index));
            }

            _context.PurchaseRequests.RemoveRange(requests);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم حذف {requests.Count} من طلبات الشراء.";
            return RedirectToAction(nameof(Index));
        }

        // ========================================================
        // POST: PurchaseRequests/DeleteAll
        // حذف جميع طلبات الشراء
        // ========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var allRequests = await _context.PurchaseRequests.ToListAsync();

            if (!allRequests.Any())
            {
                TempData["Info"] = "لا توجد طلبات شراء لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            _context.PurchaseRequests.RemoveRange(allRequests);
            await _context.SaveChangesAsync();

            TempData["Success"] = "تم حذف جميع طلبات الشراء.";
            return RedirectToAction(nameof(Index));
        }

        // ========================================================
        // GET: PurchaseRequests/Export
        // تصدير نفس نتائج الفلترة إلى CSV
        // ========================================================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string format = "csv"
        )
        {
            dir = (dir == "asc") ? "asc" : "desc";

            var query = _context.PurchaseRequests
                .Include(pr => pr.Customer)
                .Include(pr => pr.Warehouse)
                .AsNoTracking()
                .AsQueryable();

            // نفس منطق البحث الموجود في Index
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();

                switch (searchBy)
                {
                    case "id":
                        if (int.TryParse(s, out var idVal))
                            query = query.Where(pr => pr.PRId == idVal);
                        else
                            query = query.Where(pr => pr.PRId.ToString().Contains(s));
                        break;

                    case "customer":
                        query = query.Where(pr =>
                            pr.Customer.CustomerName.Contains(s) ||
                            pr.CustomerId.ToString().Contains(s));
                        break;

                    case "warehouse":
                        query = query.Where(pr =>
                            pr.Warehouse.WarehouseName.Contains(s) ||
                            pr.WarehouseId.ToString().Contains(s));
                        break;

                    case "status":
                        query = query.Where(pr => pr.Status.Contains(s));
                        break;
                }
            }

            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                var from = fromDate.Value.Date;
                var to = toDate.Value.Date;
                query = query.Where(pr => pr.PRDate.Date >= from && pr.PRDate.Date <= to);
            }

            if (fromCode.HasValue)
                query = query.Where(pr => pr.PRId >= fromCode.Value);

            if (toCode.HasValue)
                query = query.Where(pr => pr.PRId <= toCode.Value);

            bool descending = (dir == "desc");
            query = descending
                ? query.OrderByDescending(pr => pr.PRDate).ThenByDescending(pr => pr.PRId)
                : query.OrderBy(pr => pr.PRDate).ThenBy(pr => pr.PRId);

            var data = await query.ToListAsync();

            // تجهيز CSV بسيط مع الأعمدة الجديدة (الإجماليات + التحويل)
            var lines = new List<string>
            {
                "PRId,PRDate,NeedByDate,Customer,Warehouse,Status,TotalQtyRequested,ExpectedItemsTotal,IsConverted"
            };

            foreach (var pr in data)
            {
                var line = string.Join(",",
                    pr.PRId,
                    pr.PRDate.ToString("yyyy-MM-dd"),
                    pr.NeedByDate?.ToString("yyyy-MM-dd") ?? "",
                    "\"" + (pr.Customer?.CustomerName ?? "") + "\"",
                    "\"" + (pr.Warehouse?.WarehouseName ?? "") + "\"",
                    "\"" + pr.Status + "\"",
                    pr.TotalQtyRequested,          // إجمالي الكمية المطلوبة
                    pr.ExpectedItemsTotal,         // إجمالي التكلفة المتوقعة
                    pr.IsConverted ? "1" : "0"     // تم التحويل؟ 1=نعم, 0=لا
                );

                lines.Add(line);
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, lines));
            var fileName = $"PurchaseRequests_{DateTime.Now:yyyyMMdd_HHmmss}.csv";

            return File(bytes, "text/csv", fileName);
        }
    }
}
