using System;                                     // متغيرات التاريخ DateTime
using System.Collections.Generic;                 // Dictionary, List
using System.Globalization;                       // تنسيق التواريخ عند التصدير
using System.Linq;                                // LINQ: Where / OrderBy
using System.Linq.Expressions;                    // Expression<Func<...>>
using System.Text;                                // StringBuilder للتصدير
using System.Threading.Tasks;                     // async / await
using Microsoft.AspNetCore.Mvc;                   // Controller, IActionResult
using Microsoft.EntityFrameworkCore;              // AsNoTracking, Include, ToListAsync
using ERP.Data;                                   // AppDbContext الاتصال بقاعدة البيانات
using ERP.Infrastructure;                         // PagedResult + ApplySearchSort
using ERP.Models;                                 // DebitNote + Customer + Account

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إشعارات الخصم (DebitNotes)
    /// - CRUD (إنشاء/تعديل/تفاصيل/حذف).
    /// - شاشة قائمة موحّدة (بحث + فلترة بالكود + فلترة بالتاريخ + تصدير + حذف جماعي + حذف الكل).
    /// </summary>
    public class DebitNotesController : Controller
    {
        // كائن الاتصال بقاعدة البيانات
        private readonly AppDbContext _context;   // السياق الأساسي للتعامل مع الـ DB

        public DebitNotesController(AppDbContext context)
        {
            _context = context;
        }

        // =========================================================
        // دالة خاصة: تجهيز الاستعلام الأساسي + الفلاتر + البحث + الترتيب
        // تُستخدم في Index و Export حتى لا نكرر الكود.
        // =========================================================
        private IQueryable<DebitNote> BuildQuery(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            int? fromCode,
            int? toCode)
        {
            // (1) الاستعلام الأساسي من جدول إشعارات الخصم مع تحميل العميل والحسابات
            IQueryable<DebitNote> q = _context.DebitNotes
                .AsNoTracking()
                .Include(d => d.Customer)
                .Include(d => d.Account)
                .Include(d => d.OffsetAccount);

            // (2) فلتر كود من/إلى (نعتمد هنا على DebitNoteId كرقم الإشعار)
            if (fromCode.HasValue)
                q = q.Where(d => d.DebitNoteId >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(d => d.DebitNoteId <= toCode.Value);

            // (3) فلتر التاريخ: نفلتر حسب تاريخ الإشعار NoteDate
            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                DateTime from = fromDate.Value;
                DateTime to = toDate.Value;

                q = q.Where(d => d.NoteDate >= from && d.NoteDate <= to);
            }

            // (4) خرائط البحث: نحدد الأعمدة النصية والرقمية للبحث الموحد

            // الحقول النصية (string) التي يمكن البحث فيها
            var stringFields =
                new Dictionary<string, Expression<Func<DebitNote, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["number"] = d => d.NoteNumber,                                    // رقم المستند
                    ["reason"] = d => d.Reason ?? "",                                  // سبب الإشعار
                    ["desc"] = d => d.Description ?? "",                             // البيان
                    ["customer"] = d => d.Customer != null ? d.Customer.CustomerName : "", // اسم العميل/الطرف
                    ["account"] = d => d.Account != null ? d.Account.AccountName : "",   // اسم حساب الطرف
                    ["offset"] = d => d.OffsetAccount != null ? d.OffsetAccount.AccountName : "" // الحساب المقابل
                };

            // الحقول الرقمية (int) التي يمكن البحث فيها
            var intFields =
                new Dictionary<string, Expression<Func<DebitNote, int>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = d => d.DebitNoteId      // البحث برقم الإشعار
                };

            // الحقول المسموح الترتيب عليها
            var orderFields =
                new Dictionary<string, Expression<Func<DebitNote, object>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["DebitNoteId"] = d => d.DebitNoteId,                                         // رقم الإشعار
                    ["NoteNumber"] = d => d.NoteNumber,                                          // رقم المستند
                    ["NoteDate"] = d => d.NoteDate,                                            // تاريخ الإشعار
                    ["Amount"] = d => d.Amount,                                              // المبلغ
                    ["CustomerName"] = d => d.Customer != null ? d.Customer.CustomerName : "",     // اسم العميل/الطرف
                    ["AccountName"] = d => d.Account != null ? d.Account.AccountName : "",        // اسم حساب الطرف
                    ["OffsetAccountName"] = d => d.OffsetAccount != null ? d.OffsetAccount.AccountName : "", // اسم الحساب المقابل
                    ["IsPosted"] = d => d.IsPosted,                                            // حالة الترحيل
                    ["CreatedAt"] = d => d.CreatedAt,                                           // تاريخ الإنشاء
                    ["UpdatedAt"] = d => d.UpdatedAt ?? DateTime.MinValue                       // آخر تعديل
                };

            // (5) تطبيق منظومة البحث/الترتيب الموحدة
            q = q.ApplySearchSort(
                search: search,
                searchBy: searchBy,
                sort: sort,
                dir: dir,
                stringFields: stringFields,
                intFields: intFields,
                orderFields: orderFields,
                defaultSearchBy: "all",          // لو المستخدم لم يحدد نوع البحث
                defaultSortBy: "NoteDate"      // الترتيب الافتراضي بتاريخ الإشعار (الأحدث أولاً)
            );

            return q;
        }

        // =========================================================
        // Index — عرض قائمة إشعارات الخصم (نظام القوائم الموحد)
        // =========================================================
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",
            string? sort = "NoteDate",
            string? dir = "desc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,   // من كود (DebitNoteId)
            int? toCode = null,     // إلى كود
            int page = 1,
            int pageSize = 50)
        {
            // تجهيز الاستعلام مع كل الفلاتر
            var q = BuildQuery(
                search,
                searchBy,
                sort,
                dir,
                useDateRange,
                fromDate,
                toDate,
                fromCode,
                toCode);

            // إنشاء موديل التقسيم PagedResult
            var model = await PagedResult<DebitNote>.CreateAsync(q, page, pageSize);

            // حفظ قيم الفلترة الزمنية داخل الموديل (لنظام القوائم الموحد)
            model.UseDateRange = useDateRange;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            // تمرير القيم للـ ViewBag لاستخدامها في الواجهة
            ViewBag.Search = search ?? "";
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.Sort = sort ?? "NoteDate";
            ViewBag.Dir = (dir?.ToLower() == "asc") ? "asc" : "desc";

            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;

            ViewBag.DateField = "NoteDate";       // نستخدم تاريخ الإشعار للفلترة
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;

            ViewBag.TotalCount = model.TotalCount; // إجمالي عدد الإشعارات

            return View(model); // يعرض Views/DebitNotes/Index.cshtml
        }

        // =========================================================
        // Show — عرض تفاصيل إشعار خصم واحد (قراءة فقط)
        // نستخدمه في زر "عرض" في الجدول، ونعتمد على نفس View بتاع Details لو حابب.
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Show(int id)
        {
            if (id <= 0)
                return BadRequest(); // رقم غير صحيح

            var debitNote = await _context.DebitNotes
                                          .AsNoTracking()
                                          .Include(d => d.Customer)
                                          .Include(d => d.Account)
                                          .Include(d => d.OffsetAccount)
                                          .FirstOrDefaultAsync(d => d.DebitNoteId == id);

            if (debitNote == null)
                return NotFound();

            return View(debitNote); // Views/DebitNotes/Show.cshtml (نعمله لاحقاً لو حابب)
        }

        // =========================================================
        // Export — تصدير قائمة إشعارات الخصم إلى CSV (يفتح في Excel)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy = "all",
            string? sort = "NoteDate",
            string? dir = "desc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string format = "excel")   // excel | csv (الاثنين حالياً يخرجوا CSV
        {
            // نبني نفس الاستعلام المستخدم في Index لضمان نفس النتائج
            var q = BuildQuery(
                search,
                searchBy,
                sort,
                dir,
                useDateRange,
                fromDate,
                toDate,
                fromCode,
                toCode);

            var list = await q.ToListAsync();

            var sb = new StringBuilder();

            // عناوين الأعمدة في ملف CSV
            sb.AppendLine("DebitNoteId,NoteNumber,NoteDate,CustomerId,CustomerName,AccountId,AccountName,OffsetAccountId,OffsetAccountName,Amount,Reason,Description,IsPosted,CreatedAt,UpdatedAt,CreatedBy,PostedAt,PostedBy");

            // كل إشعار في سطر CSV
            foreach (var d in list)
            {
                string customerName = d.Customer?.CustomerName ?? "";
                string accountName = d.Account?.AccountName ?? "";
                string offsetAccountName = d.OffsetAccount?.AccountName ?? "";

                string line = string.Join(",",
                    d.DebitNoteId,
                    (d.NoteNumber ?? "").Replace(",", " "),
                    d.NoteDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    d.CustomerId?.ToString() ?? "",
                    customerName.Replace(",", " "),
                    d.AccountId,
                    accountName.Replace(",", " "),
                    d.OffsetAccountId?.ToString() ?? "",
                    offsetAccountName.Replace(",", " "),
                    d.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                    (d.Reason ?? "").Replace(",", " "),
                    (d.Description ?? "").Replace(",", " "),
                    d.IsPosted ? "1" : "0",
                    d.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    d.UpdatedAt.HasValue
                        ? d.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                        : "",
                    (d.CreatedBy ?? "").Replace(",", " "),
                    d.PostedAt.HasValue
                        ? d.PostedAt.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                        : "",
                    (d.PostedBy ?? "").Replace(",", " ")
                );

                sb.AppendLine(line);
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = "DebitNotes.csv";
            const string contentType = "text/csv";

            return File(bytes, contentType, fileName);
        }

        // =========================================================
        // BulkDelete — حذف مجموعة من إشعارات الخصم المحددة
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(int[] ids)
        {
            // لو المستخدم لم يحدد أى إشعار
            if (ids == null || ids.Length == 0)
            {
                TempData["Error"] = "لم يتم اختيار أى إشعار للحذف.";
                return RedirectToAction(nameof(Index));
            }

            // نحضر الإشعارات المطابقة للأرقام المختارة
            var notes = await _context.DebitNotes
                                      .Where(d => ids.Contains(d.DebitNoteId))
                                      .ToListAsync();

            if (notes.Count == 0)
            {
                TempData["Error"] = "لم يتم العثور على الإشعارات المحددة.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _context.DebitNotes.RemoveRange(notes);
                await _context.SaveChangesAsync();

                TempData["Success"] = $"تم حذف {notes.Count} من إشعارات الخصم المحددة.";
            }
            catch (DbUpdateException)
            {
                TempData["Error"] = "لا يمكن حذف بعض الإشعارات بسبب ارتباطها بقيود محاسبية أو جداول أخرى.";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // DeleteAll — حذف جميع إشعارات الخصم
        // يُفضل استخدامه في بيئة تجريبية فقط.
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _context.DebitNotes.ToListAsync();

            if (all.Count == 0)
            {
                TempData["Error"] = "لا توجد إشعارات خصم لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _context.DebitNotes.RemoveRange(all);
                await _context.SaveChangesAsync();

                TempData["Success"] = "تم حذف جميع إشعارات الخصم.";
            }
            catch (DbUpdateException)
            {
                TempData["Error"] = "لا يمكن حذف جميع الإشعارات بسبب وجود ارتباطات محاسبية أخرى.";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // ===== CRUD القياسي: Details / Create / Edit / Delete =====
        // =========================================================

        // GET: DebitNotes/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
                return NotFound();

            var debitNote = await _context.DebitNotes
                                          .Include(d => d.Customer)
                                          .Include(d => d.Account)
                                          .Include(d => d.OffsetAccount)
                                          .FirstOrDefaultAsync(m => m.DebitNoteId == id);
            if (debitNote == null)
                return NotFound();

            return View(debitNote);
        }

        // GET: DebitNotes/Create
        public IActionResult Create()
        {
            // ممكن هنا لاحقاً نجهز DropDowns للـ Customer / Account / OffsetAccount
            return View();
        }

        // POST: DebitNotes/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(DebitNote debitNote)
        {
            if (ModelState.IsValid)
            {
                debitNote.CreatedAt = DateTime.Now;
                debitNote.UpdatedAt = null;
                debitNote.IsPosted = false;
                debitNote.PostedAt = null;

                if (string.IsNullOrEmpty(debitNote.CreatedBy))
                    debitNote.CreatedBy = User?.Identity?.Name ?? "System";

                _context.Add(debitNote);
                await _context.SaveChangesAsync();
                TempData["Success"] = "تم إنشاء إشعار الخصم بنجاح.";
                return RedirectToAction(nameof(Index));
            }

            return View(debitNote);
        }

        // GET: DebitNotes/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
                return NotFound();

            var debitNote = await _context.DebitNotes.FindAsync(id);
            if (debitNote == null)
                return NotFound();

            return View(debitNote);
        }

        // POST: DebitNotes/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, DebitNote debitNote)
        {
            if (id != debitNote.DebitNoteId)
                return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    debitNote.UpdatedAt = DateTime.Now;
                    _context.Update(debitNote);
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "تم تعديل إشعار الخصم بنجاح.";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!DebitNoteExists(debitNote.DebitNoteId))
                        return NotFound();
                    else
                        throw;
                }
                return RedirectToAction(nameof(Index));
            }

            return View(debitNote);
        }

        // GET: DebitNotes/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
                return NotFound();

            var debitNote = await _context.DebitNotes
                                          .Include(d => d.Customer)
                                          .Include(d => d.Account)
                                          .Include(d => d.OffsetAccount)
                                          .FirstOrDefaultAsync(m => m.DebitNoteId == id);
            if (debitNote == null)
                return NotFound();

            return View(debitNote);
        }

        // POST: DebitNotes/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var debitNote = await _context.DebitNotes.FindAsync(id);
            if (debitNote != null)
            {
                _context.DebitNotes.Remove(debitNote);
                await _context.SaveChangesAsync();
                TempData["Success"] = "تم حذف إشعار الخصم.";
            }

            return RedirectToAction(nameof(Index));
        }

        private bool DebitNoteExists(int id)
        {
            return _context.DebitNotes.Any(e => e.DebitNoteId == id);
        }
    }
}
