using System;                                     // متغيرات التاريخ DateTime
using System.Collections.Generic;                 // Dictionary, List
using System.Globalization;                       // تنسيق التواريخ عند التصدير
using System.Linq;                                // LINQ: Where / OrderBy
using System.Linq.Expressions;                    // Expression<Func<...>>
using System.Text;                                // StringBuilder للتصدير
using System.Threading.Tasks;                     // async / await
using Microsoft.AspNetCore.Mvc;                   // Controller, IActionResult
using Microsoft.AspNetCore.Mvc.Rendering;         // SelectList للقوائم المنسدلة
using Microsoft.EntityFrameworkCore;              // AsNoTracking, Include, ToListAsync
using ERP.Data;                                   // AppDbContext الاتصال بقاعدة البيانات
using ERP.Infrastructure;                         // PagedResult + ApplySearchSort
using ERP.Models;                                 // CreditNote + Account + Customer

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إشعارات الإضافة (CreditNotes)
    /// بالنظام الثابت:
    /// - Index: بحث + ترتيب + فلترة بالتاريخ والكود + اختيار أعمدة + طباعة + تصدير + حذف جماعي/حذف الكل.
    /// - Show: عرض إشعار واحد.
    /// - Export: تصدير إلى CSV/Excel.
    /// - BulkDelete: حذف المحدد.
    /// - DeleteAll: حذف كل الإشعارات (لبيئة TEST).
    /// - بالإضافة إلى CRUD الأساسي: Create / Edit / Details / Delete.
    /// </summary>
    public class CreditNotesController : Controller
    {
        // كائن الاتصال بقاعدة البيانات
        private readonly AppDbContext _context;   // متغير: السياق الأساسي للتعامل مع الـ DB

        public CreditNotesController(AppDbContext context)
        {
            _context = context;
        }

        // =========================================================
        // دالة مساعدة: تحميل القوائم المنسدلة (الطرف + الحسابات)
        // =========================================================
        private void PopulateLookups(int? customerId = null, int? accountId = null, int? offsetAccountId = null)
        {
            // قائمة العملاء/الأطراف
            ViewData["CustomerId"] = new SelectList(
                _context.Customers
                        .AsNoTracking()
                        .OrderBy(c => c.CustomerName),
                "CustomerId",
                "CustomerName",
                customerId
            );

            // قائمة الحسابات (حساب الطرف)
            ViewData["AccountId"] = new SelectList(
                _context.Accounts
                        .AsNoTracking()
                        .OrderBy(a => a.AccountName),
                "AccountId",
                "AccountName",
                accountId
            );

            // قائمة حساب مقابل (اختياري)
            ViewData["OffsetAccountId"] = new SelectList(
                _context.Accounts
                        .AsNoTracking()
                        .OrderBy(a => a.AccountName),
                "AccountId",
                "AccountName",
                offsetAccountId
            );
        }

        // =========================================================
        // دالة خاصة: تجهيز الاستعلام الأساسي + الفلاتر + البحث + الترتيب
        // نستخدمها في Index و Export حتى لا نكرر الكود.
        // =========================================================
        private IQueryable<CreditNote> BuildQuery(
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
            // (1) الاستعلام الأساسي من جدول إشعارات الإضافة مع تحميل الطرف والحسابات (بدون تتبّع لتحسين الأداء)
            IQueryable<CreditNote> q = _context.CreditNotes
                .AsNoTracking()
                .Include(c => c.Customer)
                .Include(c => c.Account)
                .Include(c => c.OffsetAccount);

            // (2) فلتر كود من/إلى (نعتمد هنا على CreditNoteId كرقم الإشعار)
            if (fromCode.HasValue)
                q = q.Where(c => c.CreditNoteId >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(c => c.CreditNoteId <= toCode.Value);

            // (3) فلتر التاريخ: نفلتر حسب تاريخ الإشعار NoteDate
            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                DateTime from = fromDate.Value;
                DateTime to = toDate.Value;

                q = q.Where(c => c.NoteDate >= from && c.NoteDate <= to);
            }

            // (4) خرائط البحث: نحدد الأعمدة النصية والرقمية للبحث الموحد

            // الحقول النصية (string) التى يمكن البحث فيها
            var stringFields =
                new Dictionary<string, Expression<Func<CreditNote, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["noteNumber"] = c => c.NoteNumber,                                     // رقم المستند
                    ["reason"] = c => c.Reason ?? "",                                   // سبب الإشعار
                    ["desc"] = c => c.Description ?? "",                              // البيان
                    ["customer"] = c => c.Customer != null ? c.Customer.CustomerName : "",// اسم الطرف
                    ["account"] = c => c.Account != null ? c.Account.AccountName : "",   // حساب الطرف
                    ["offset"] = c => c.OffsetAccount != null ? c.OffsetAccount.AccountName : "", // الحساب المقابل
                    ["createdBy"] = c => c.CreatedBy ?? "",                                // أنشئ بواسطة
                    ["postedBy"] = c => c.PostedBy ?? ""                                  // رحّله بواسطة
                };

            // الحقول الرقمية (int) التى يمكن البحث فيها
            var intFields =
                new Dictionary<string, Expression<Func<CreditNote, int>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = c => c.CreditNoteId        // البحث برقم الإشعار
                };

            // الحقول المسموح الترتيب عليها
            var orderFields =
                new Dictionary<string, Expression<Func<CreditNote, object>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["CreditNoteId"] = c => c.CreditNoteId,                               // رقم الإشعار
                    ["NoteNumber"] = c => c.NoteNumber,                                 // رقم المستند
                    ["NoteDate"] = c => c.NoteDate,                                   // تاريخ الإشعار
                    ["Amount"] = c => c.Amount,                                     // المبلغ
                    ["CustomerName"] = c => c.Customer != null ? c.Customer.CustomerName : "",
                    ["AccountName"] = c => c.Account != null ? c.Account.AccountName : "",
                    ["OffsetAccountName"] = c => c.OffsetAccount != null ? c.OffsetAccount.AccountName : "",
                    ["IsPosted"] = c => c.IsPosted,                                   // حالة الترحيل
                    ["CreatedAt"] = c => c.CreatedAt,                                  // تاريخ الإنشاء
                    ["UpdatedAt"] = c => c.UpdatedAt ?? DateTime.MinValue              // آخر تعديل
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
                defaultSearchBy: "all",         // لو المستخدم لم يحدد نوع البحث
                defaultSortBy: "NoteDate"     // الترتيب الافتراضي بتاريخ الإشعار
            );

            return q;
        }

        // =========================================================
        // Index — عرض قائمة إشعارات الإضافة (نظام القوائم الموحد)
        // =========================================================
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",
            string? sort = "NoteDate",
            string? dir = "desc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,   // من كود (CreditNoteId)
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
            var model = await PagedResult<CreditNote>.CreateAsync(q, page, pageSize);

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

            return View(model); // يعرض Views/CreditNotes/Index.cshtml
        }

        // =========================================================
        // Show — عرض تفاصيل إشعار إضافة واحد (قراءة فقط)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Show(int id)
        {
            if (id <= 0)
                return BadRequest(); // رقم غير صحيح

            // قراءة الإشعار مع الطرف والحسابات (للعرض فقط)
            var note = await _context.CreditNotes
                                     .AsNoTracking()
                                     .Include(c => c.Customer)
                                     .Include(c => c.Account)
                                     .Include(c => c.OffsetAccount)
                                     .FirstOrDefaultAsync(c => c.CreditNoteId == id);

            if (note == null)
                return NotFound();

            return View(note); // Views/CreditNotes/Show.cshtml (نعمله لاحقاً بنفس نمط Show الثابت)
        }

        // =========================================================
        // Export — تصدير قائمة الإشعارات إلى CSV (يفتح في Excel)
        // زر التصدير في الواجهة لونه أخضر (زر إكسل).
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
            string format = "excel")   // excel | csv (الاتنين حالياً يخرجوا CSV)
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
            sb.AppendLine("CreditNoteId,NoteNumber,NoteDate,CustomerName,AccountName,OffsetAccountName,Amount,Reason,Description,IsPosted,CreatedAt,CreatedBy,PostedAt,PostedBy");

            // كل صف إشعار في سطر CSV
            foreach (var c in list)
            {
                string customerName = c.Customer?.CustomerName ?? "";
                string accountName = c.Account?.AccountName ?? "";
                string offsetName = c.OffsetAccount?.AccountName ?? "";

                string line = string.Join(",",
                    c.CreditNoteId,
                    (c.NoteNumber ?? "").Replace(",", " "),
                    c.NoteDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    customerName.Replace(",", " "),
                    accountName.Replace(",", " "),
                    offsetName.Replace(",", " "),
                    c.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                    (c.Reason ?? "").Replace(",", " "),
                    (c.Description ?? "").Replace(",", " "),
                    c.IsPosted ? "1" : "0",
                    c.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    (c.CreatedBy ?? "").Replace(",", " "),
                    c.PostedAt.HasValue
                        ? c.PostedAt.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                        : "",
                    (c.PostedBy ?? "").Replace(",", " ")
                );

                sb.AppendLine(line);
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = "CreditNotes.csv";
            const string contentType = "text/csv";

            return File(bytes, contentType, fileName);
        }

        // =========================================================
        // BulkDelete — حذف مجموعة من إشعارات الإضافة المحددة
        // (يفضل استخدامها بحذر).
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
            var notes = await _context.CreditNotes
                                      .Where(c => ids.Contains(c.CreditNoteId))
                                      .ToListAsync();

            if (notes.Count == 0)
            {
                TempData["Error"] = "لم يتم العثور على الإشعارات المحددة.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _context.CreditNotes.RemoveRange(notes);
                await _context.SaveChangesAsync();

                TempData["Success"] = $"تم حذف {notes.Count} من إشعارات الإضافة المحددة.";
            }
            catch (DbUpdateException)
            {
                TempData["Error"] = "لا يمكن حذف بعض الإشعارات بسبب ارتباطها بحركات محاسبية أخرى.";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // DeleteAll — حذف جميع إشعارات الإضافة
        // تنبيه: يُفضّل استخدامه في بيئة TEST فقط.
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _context.CreditNotes.ToListAsync();

            if (all.Count == 0)
            {
                TempData["Error"] = "لا توجد إشعارات لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _context.CreditNotes.RemoveRange(all);
                await _context.SaveChangesAsync();

                TempData["Success"] = "تم حذف جميع إشعارات الإضافة.";
            }
            catch (DbUpdateException)
            {
                TempData["Error"] = "لا يمكن حذف جميع الإشعارات بسبب وجود ارتباطات محاسبية أخرى.";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // CRUD الأساسي — Create / Details / Edit / Delete
        // =========================================================

        // GET: CreditNotes/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
                return NotFound();

            var creditNote = await _context.CreditNotes
                .Include(c => c.Customer)
                .Include(c => c.Account)
                .Include(c => c.OffsetAccount)
                .FirstOrDefaultAsync(m => m.CreditNoteId == id);

            if (creditNote == null)
                return NotFound();

            return View(creditNote);
        }

        // GET: CreditNotes/Create
        public IActionResult Create()
        {
            PopulateLookups();
            // ممكن تهيئة تاريخ الإشعار والإنشاء افتراضياً
            var model = new CreditNote
            {
                NoteDate = DateTime.Now,
                CreatedAt = DateTime.Now
            };
            return View(model);
        }

        // POST: CreditNotes/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreditNote creditNote)
        {
            if (ModelState.IsValid)
            {
                // تعبئة بيانات التتبع
                creditNote.CreatedAt = DateTime.Now;
                creditNote.UpdatedAt = creditNote.CreatedAt;
                creditNote.CreatedBy = User?.Identity?.Name ?? "System";
                creditNote.IsPosted = false;
                creditNote.PostedAt = null;
                creditNote.PostedBy = null;

                _context.Add(creditNote);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            PopulateLookups(creditNote.CustomerId, creditNote.AccountId, creditNote.OffsetAccountId);
            return View(creditNote);
        }

        // GET: CreditNotes/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
                return NotFound();

            var creditNote = await _context.CreditNotes.FindAsync(id);
            if (creditNote == null)
                return NotFound();

            PopulateLookups(creditNote.CustomerId, creditNote.AccountId, creditNote.OffsetAccountId);
            return View(creditNote);
        }

        // POST: CreditNotes/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, CreditNote input)
        {
            if (id != input.CreditNoteId)
                return NotFound();

            if (!ModelState.IsValid)
            {
                PopulateLookups(input.CustomerId, input.AccountId, input.OffsetAccountId);
                return View(input);
            }

            var existing = await _context.CreditNotes.FindAsync(id);
            if (existing == null)
                return NotFound();

            // تحديث الحقول القابلة للتعديل من الشاشة
            existing.NoteNumber = input.NoteNumber;
            existing.NoteDate = input.NoteDate;
            existing.CustomerId = input.CustomerId;
            existing.AccountId = input.AccountId;
            existing.OffsetAccountId = input.OffsetAccountId;
            existing.Amount = input.Amount;
            existing.Reason = input.Reason;
            existing.Description = input.Description;

            // تحديث بيانات التتبع
            existing.UpdatedAt = DateTime.Now;

            try
            {
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!CreditNoteExists(existing.CreditNoteId))
                    return NotFound();

                throw;
            }
        }

        // GET: CreditNotes/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
                return NotFound();

            var creditNote = await _context.CreditNotes
                .Include(c => c.Customer)
                .Include(c => c.Account)
                .Include(c => c.OffsetAccount)
                .FirstOrDefaultAsync(m => m.CreditNoteId == id);

            if (creditNote == null)
                return NotFound();

            return View(creditNote);
        }

        // POST: CreditNotes/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var creditNote = await _context.CreditNotes.FindAsync(id);
            if (creditNote == null)
                return RedirectToAction(nameof(Index));

            try
            {
                _context.CreditNotes.Remove(creditNote);
                await _context.SaveChangesAsync();
                TempData["Success"] = "تم حذف إشعار الإضافة بنجاح.";
            }
            catch (DbUpdateException)
            {
                TempData["Error"] = "لا يمكن حذف الإشعار بسبب ارتباطه بحركات أخرى.";
            }

            return RedirectToAction(nameof(Index));
        }

        private bool CreditNoteExists(int id)
        {
            return _context.CreditNotes.Any(e => e.CreditNoteId == id);
        }
    }
}
