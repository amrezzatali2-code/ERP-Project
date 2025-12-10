using ERP.Data;
using ERP.Infrastructure;
using ERP.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;               // لاستخدام StringBuilder في التصدير
using System.Threading.Tasks;

namespace ERP.Controllers
{
    public class SalesInvoicesController : Controller
    {
        private readonly AppDbContext _context;

        // مصفوفة طرق الدفع الثابتة لعرضها في الفورم
        private static readonly string[] PaymentMethods = new[] { "نقدي", "شبكة", "آجل", "مختلط" };

        public SalesInvoicesController(AppDbContext context)
        {
            _context = context;
        }




        // دالة خاصة لتحضير القوائم المنسدلة (العملاء + المخازن + طرق الدفع)
        private async Task PopulateDropDownsAsync()
        {
            // 1) طرق الدفع الثابتة (نفس المصفوفة اللي فوق)
            ViewBag.PaymentMethods = PaymentMethods; // نقدي / شبكة / آجل / مختلط

            // 2) قائمة العملاء: نعرض الاسم، والقيمة = الكود
            // ملاحظة: استبدل c.Name باسم عمود اسم العميل الحقيقي عندك (مثلاً NameAr أو CustomerName)
            ViewBag.Customers = await _context.Customers
                .AsNoTracking()
                .OrderBy(c => c.CustomerName) // اسم العميل
                .Select(c => new SelectListItem
                {
                    Value = c.CustomerId.ToString(),   // متغير: كود العميل
                    Text = c.CustomerName                     // متغير: اسم العميل
                })
                .ToListAsync();

            // 3) قائمة المخازن: نعرض اسم المخزن، والقيمة = الكود
            // استبدل w.Name باسم عمود اسم المخزن عندك لو مختلف
            ViewBag.Warehouses = await _context.Warehouses
                .AsNoTracking()
                .OrderBy(w => w.WarehouseName) // اسم المخزن
                .Select(w => new SelectListItem
                {
                    Value = w.WarehouseId.ToString(),  // متغير: كود المخزن
                    Text = w.WarehouseName                     // متغير: اسم المخزن
                })
                .ToListAsync();
        }


        // =========================
        // دالة مشتركة لبناء استعلام فواتير المبيعات
        // (بحث + فلتر رقم من/إلى + ترتيب)
        // =========================
        private IQueryable<SalesInvoice> BuildSalesInvoicesQuery(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            int? fromCode,
            int? toCode)
        {
            // 1) الاستعلام الأساسي (قراءة فقط لتحسين الأداء)
            IQueryable<SalesInvoice> q = _context.SalesInvoices.AsNoTracking();

            // 2) فلتر رقم الفاتورة من/إلى (SIId)
            if (fromCode.HasValue)
                q = q.Where(x => x.SIId >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(x => x.SIId <= toCode.Value);

            // 3) الحقول النصية للبحث
            var stringFields = new Dictionary<string, Expression<Func<SalesInvoice, string?>>>
            {
                ["status"] = x => x.Status,                     // حالة الفاتورة
                ["payment"] = x => x.PaymentMethod,              // طريقة الدفع
                ["date"] = x => x.SIDate.ToString("yyyy-MM-dd") // بحث بالتاريخ كنص (اختياري)
            };

            // 4) الحقول الرقمية للبحث
            var intFields = new Dictionary<string, Expression<Func<SalesInvoice, int>>>
            {
                ["id"] = x => x.SIId,         // رقم الفاتورة
                ["customer"] = x => x.CustomerId,   // كود العميل
                ["warehouse"] = x => x.WarehouseId   // كود المخزن
            };

            // 5) حقول الترتيب — نفس أسماء الأعمدة في الواجهة
            var orderFields = new Dictionary<string, Expression<Func<SalesInvoice, object>>>
            {
                ["date"] = x => x.SIDate,       // تاريخ الفاتورة
                ["id"] = x => x.SIId,         // رقم الفاتورة
                ["time"] = x => x.SITime!,      // وقت الفاتورة
                ["customer"] = x => x.CustomerId,
                ["warehouse"] = x => x.WarehouseId,
                ["net"] = x => x.NetTotal,
                ["status"] = x => x.Status!,
                ["posted"] = x => x.IsPosted      // مرحّل أم لا
            };

            // 6) تطبيق البحث + الترتيب عن طريق الإكستنشن الموحّد
            q = q.ApplySearchSort(
                search, searchBy,
                sort, dir,
                stringFields, intFields, orderFields,
                defaultSearchBy: "all",
                defaultSortBy: "date");

            return q;
        }

        // =========================
        // Index — عرض قائمة فواتير البيع
        // =========================
        public async Task<IActionResult> Index(
            string? search,              // نص البحث
            string? searchBy = "all",    // all | id | customer | warehouse | status | date
            string? sort = "date",       // date | id | time | customer | warehouse | net | status | posted
            string? dir = "desc",        // asc | desc
            int page = 1,
            int pageSize = 50,
            int? fromCode = null,        // فلتر رقم فاتورة من
            int? toCode = null)          // فلتر رقم فاتورة إلى
        {
            // بناء الاستعلام الموحّد
            var q = BuildSalesInvoicesQuery(search, searchBy, sort, dir, fromCode, toCode);

            // التقسيم إلى صفحات
            var model = await PagedResult<SalesInvoice>.CreateAsync(q, page, pageSize);

            // تجهيز قيم الـ ViewBag للفلاتر والواجهة
            ViewBag.Search = search ?? "";
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.Sort = sort ?? "date";
            ViewBag.Dir = (dir?.ToLower() == "asc") ? "asc" : "desc";

            // أرقام من/إلى (نستخدم أكثر من اسم لتوافق الواجهة)
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.CodeFrom = fromCode;
            ViewBag.CodeTo = toCode;

            // حقل التاريخ المستخدم في الفلترة (ثابت هنا SIDate)
            ViewBag.DateField = "SIDate";

            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalCount = model.TotalCount;

            return View(model);
        }





        // =========================================================
        // Create — GET: فتح شاشة إنشاء فاتورة مبيعات جديدة
        // =========================================================
      [HttpGet]
public IActionResult Create()
{
    // تجهيز موديل جديد بقيم افتراضية
    var model = new SalesInvoice
    {
        SIDate = DateTime.Today,           // تاريخ الفاتورة
        SITime = DateTime.Now.TimeOfDay,   // وقت الفاتورة
        IsPosted = false,                  // لسه مش مرحّلة
        Status = "مسودة"                  // حالة مبدئية مثلاً
        // تقدر تزود أي قيم افتراضية ثانية
    };

    // 👈 هنا المهم: نرجّع فيو "Show" بدل "Create"
    return View("Show", model);
}



        // =========================================================
        // Create — POST: حفظ بيانات الفاتورة الجديدة في قاعدة البيانات
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(SalesInvoice invoice)
        {
            // تحقق إضافي على نسبة خصم الهيدر (0..100)
            if (invoice.HeaderDiscountPercent < 0 || invoice.HeaderDiscountPercent > 100)
            {
                ModelState.AddModelError(
                    nameof(SalesInvoice.HeaderDiscountPercent),
                    "نسبة خصم الهيدر يجب أن تكون بين 0 و 100.");
            }

            // لو فيه أخطاء تحقق نرجع لنفس الشاشة
            if (!ModelState.IsValid)
            {
                await PopulateDropDownsAsync();   // إعادة تحميل القوائم
                return View(invoice);
            }

            // ضبط قيم التتبع (في حالة لم تُرسل من الواجهة)
            invoice.SIDate = invoice.SIDate == default ? DateTime.Today : invoice.SIDate;
            invoice.SITime = invoice.SITime == default ? DateTime.Now.TimeOfDay : invoice.SITime;
            invoice.CreatedAt = DateTime.Now;

            if (string.IsNullOrWhiteSpace(invoice.CreatedBy))
                invoice.CreatedBy = User?.Identity?.Name ?? "system";

            // إضافة الفاتورة إلى الـ DbContext
            _context.SalesInvoices.Add(invoice);

            // حفظ التغييرات في قاعدة البيانات
            await _context.SaveChangesAsync();

            TempData["Msg"] = "تم إنشاء الفاتورة بنجاح.";

            // بعد الحفظ نروح مباشرة لشاشة عرض/قراءة الفاتورة (Edit GET)
            return RedirectToAction(nameof(Edit), new { id = invoice.SIId });

        }






        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            // تحقق بسيط من رقم الفاتورة
            if (id <= 0)
                return BadRequest("رقم الفاتورة غير صالح.");

            // قراءة هيدر الفاتورة + بيانات العميل + سطور الفاتورة
            var model = await _context.SalesInvoices
                .Include(si => si.Customer)   // العميل
                .Include(si => si.Lines)      // سطور الفاتورة
                .AsNoTracking()
                .FirstOrDefaultAsync(si => si.SIId == id);

            if (model == null)
                return NotFound();            // لو الفاتورة مش موجودة

            // تعبئة القوائم المنسدلة (العملاء + المخازن + ... )
            await PopulateDropDownsAsync();

            // فتح شاشة الفاتورة (Edit = شاشة عرض + أزرار فتح/ترحيل/طباعة)
            return View(model);
        }






        // =========================
        // Edit — POST: حفظ تعديل الهيدر مع RowVersion
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, SalesInvoice invoice)
        {
            // تأكد أن رقم الفاتورة في الرابط هو نفس الموجود في الموديل
            if (id != invoice.SIId)
                return NotFound();

            // تحقق إضافي على نسبة خصم الهيدر (0..100)
            if (invoice.HeaderDiscountPercent < 0 || invoice.HeaderDiscountPercent > 100)
            {
                ModelState.AddModelError(
                    nameof(SalesInvoice.HeaderDiscountPercent),
                    "النسبة يجب أن تكون بين 0 و 100");
            }

            // لو فيه أخطاء تحقق نرجع لنفس الفورم
            if (!ModelState.IsValid)
            {
                ViewBag.PaymentMethods = PaymentMethods;
                return View(invoice);
            }

            try
            {
                // تحديث وقت آخر تعديل
                invoice.UpdatedAt = DateTime.Now;

                // إعداد RowVersion الأصلي للتعامل مع التعارض
                _context.Entry(invoice)
                        .Property(x => x.RowVersion)
                        .OriginalValue = invoice.RowVersion;

                // تحديث الكيان في الـ DbContext
                _context.Update(invoice);

                // حفظ التغييرات فعلياً في SQL Server
                await _context.SaveChangesAsync();

                TempData["Msg"] = "تم تعديل الفاتورة بنجاح.";
            }
            catch (DbUpdateConcurrencyException)
            {
                // لو الفاتورة اختفت أثناء الحفظ (اتحذفت مثلاً)
                bool exists = await _context.SalesInvoices.AnyAsync(e => e.SIId == id);
                if (!exists)
                    return NotFound();

                // تعارض حقيقي: حد آخر عدّل نفس الفاتورة
                ModelState.AddModelError(
                    string.Empty,
                    "تعذر الحفظ بسبب تعديل متزامن. أعد تحميل الصفحة وحاول مجددًا.");

                ViewBag.PaymentMethods = PaymentMethods;
                return View(invoice);
            }

            return RedirectToAction(nameof(Index));
        }





        // =========================
        // Export — تصدير فواتير المبيعات (CSV يفتح في Excel)
        // =========================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            int? codeFrom,
            int? codeTo,
            bool useDateRange = false,       // موجود للتماشي مع الفورم، حالياً لا نستخدمه
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? format = "excel")        // excel | csv (حاليًا الاثنين CSV)
        {
            // نعيد استخدام نفس منطق الفلترة والترتيب
            int? fromCode = codeFrom;
            int? toCode = codeTo;

            var q = BuildSalesInvoicesQuery(search, searchBy, sort, dir, fromCode, toCode);

            var list = await q.ToListAsync();

            // تجهيز CSV بسيط — Excel يفتحه بدون مشكلة
            var sb = new StringBuilder();

            // العناوين
            sb.AppendLine("InvoiceId,Date,Time,CustomerId,WarehouseId,NetTotal,Status,IsPosted");

            foreach (var x in list)
            {
                // تحويل الوقت من TimeSpan إلى نص بصيغة hh:mm
                string timeText = x.SITime.ToString(@"hh\:mm");   // مثال: 14:35

                // سطر واحد في CSV لكل فاتورة
                var line = string.Join(",",
                    x.SIId,                                      // رقم الفاتورة
                    x.SIDate.ToString("yyyy-MM-dd"),             // التاريخ
                    timeText,                                    // الوقت كنص
                    x.CustomerId,                                // كود العميل
                    x.WarehouseId,                               // كود المخزن
                    x.NetTotal.ToString("0.00"),                 // الصافي (منسّق)
                    (x.Status ?? "").Replace(",", " "),          // الحالة (نزيل الفاصلة عشان CSV)
                    x.IsPosted ? "Yes" : "No"                    // مرحل أم لا
                );

                sb.AppendLine(line);
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var ext = (format ?? "excel").ToLower() == "csv" ? "csv" : "csv"; // الاثنين CSV حاليًا
            var fileName = $"SalesInvoices_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}";

            return File(bytes, "text/csv", fileName);
        }










        // =========================
        // Delete — GET: صفحة تأكيد الحذف
        // =========================
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
                return NotFound();

            var invoice = await _context.SalesInvoices
                                        .AsNoTracking()
                                        .FirstOrDefaultAsync(m => m.SIId == id.Value);
            if (invoice == null)
                return NotFound();

            return View(invoice);
        }

        // =========================
        // Delete — POST: تنفيذ الحذف لفاتورة واحدة
        // (معتمد على Cascade لحذف السطور)
        // =========================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var invoice = await _context.SalesInvoices
                                        .FirstOrDefaultAsync(e => e.SIId == id);
            if (invoice == null)
                return NotFound();

            try
            {
                _context.SalesInvoices.Remove(invoice);
                await _context.SaveChangesAsync();
                TempData["Msg"] = "تم حذف الفاتورة (مع السطور التابعة لها إن وُجدت).";
            }
            catch (DbUpdateConcurrencyException)
            {
                ModelState.AddModelError(string.Empty, "تعذر الحذف بسبب تعارض متزامن.");
                return View(invoice);
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================
        // BulkDelete — حذف مجموعة فواتير مختارة
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["Msg"] = "لم يتم اختيار أي فواتير للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var ids = selectedIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                 .Select(s => int.TryParse(s, out var n) ? (int?)n : null)
                                 .Where(n => n.HasValue)
                                 .Select(n => n!.Value)
                                 .ToList();

            if (!ids.Any())
            {
                TempData["Msg"] = "لم يتم اختيار أي فواتير صحيحة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var invoices = await _context.SalesInvoices
                                         .Where(x => ids.Contains(x.SIId))
                                         .ToListAsync();

            if (!invoices.Any())
            {
                TempData["Msg"] = "لم يتم العثور على الفواتير المحددة.";
                return RedirectToAction(nameof(Index));
            }

            // نفترض أن العلاقة مع السطور عليها Cascade Delete
            _context.SalesInvoices.RemoveRange(invoices);
            await _context.SaveChangesAsync();

            TempData["Msg"] = $"تم حذف {invoices.Count} فاتورة (مع السطور التابعة لها).";
            return RedirectToAction(nameof(Index));
        }







        // =========================
        // DeleteAll — حذف جميع فواتير المبيعات
        // (استخدمه بحذر شديد)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var invoices = await _context.SalesInvoices.ToListAsync();

            if (!invoices.Any())
            {
                TempData["Msg"] = "لا توجد فواتير لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            _context.SalesInvoices.RemoveRange(invoices);
            await _context.SaveChangesAsync();

            TempData["Msg"] = $"تم حذف جميع فواتير المبيعات ({invoices.Count}) مع السطور التابعة لها.";
            return RedirectToAction(nameof(Index));
        }



    }
}
