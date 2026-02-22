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
using ERP.Infrastructure;                         // PagedResult + ApplySearchSort + UserActivityLogger
using ERP.Models;                                 // CreditNote, UserActionType...
using ERP.Services;                               // ILedgerPostingService

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إشعارات الإضافة (CreditNotes)
    /// - زر حفظ = حفظ + ترحيل محاسبي (LedgerEntries + تحديث حساب العميل + الأرباح).
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
        private readonly AppDbContext _context;
        private readonly ILedgerPostingService _ledgerPostingService;
        private readonly IUserActivityLogger _activityLogger;

        public CreditNotesController(AppDbContext context, ILedgerPostingService ledgerPostingService, IUserActivityLogger activityLogger)
        {
            _context = context;
            _ledgerPostingService = ledgerPostingService;
            _activityLogger = activityLogger;
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
                        .Where(c => c.IsActive == true)
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
                    ["reason"] = c => c.Reason ?? "",                                   // سبب الإشعار
                    ["desc"] = c => c.Description ?? "",                              // البيان
                    ["customer"] = c => c.Customer != null ? c.Customer.CustomerName : "",// اسم الطرف
                    ["account"] = c => c.Account != null ? c.Account.AccountName : "",   // حساب الطرف
                    ["offset"] = c => c.OffsetAccount != null ? c.OffsetAccount.AccountName : "", // الحساب المقابل
                    ["createdBy"] = c => c.CreatedBy ?? "",                                // أنشئ بواسطة
                    ["postedBy"] = c => c.PostedBy ?? ""                                  // رحّله بواسطة
                };

            // الحقول الرقمية (int) التى يمكن البحث فيها (نفس إشعارات الخصم)
            var intFields =
                new Dictionary<string, Expression<Func<CreditNote, int>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = c => c.CreditNoteId,       // البحث برقم الإشعار
                    ["number"] = c => c.CreditNoteId   // رقم المستند
                };

            // الحقول المسموح الترتيب عليها
            var orderFields =
                new Dictionary<string, Expression<Func<CreditNote, object>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["CreditNoteId"] = c => c.CreditNoteId,                               // رقم الإشعار / رقم المستند
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

            // عناوين الأعمدة في ملف CSV (نفس إشعارات الخصم)
            sb.AppendLine("CreditNoteId,NoteDate,CustomerId,CustomerName,AccountId,AccountName,OffsetAccountId,OffsetAccountName,Amount,Reason,Description,IsPosted,CreatedAt,UpdatedAt,CreatedBy,PostedAt,PostedBy");

            // كل صف إشعار في سطر CSV
            foreach (var c in list)
            {
                string customerName = c.Customer?.CustomerName ?? "";
                string accountName = c.Account?.AccountName ?? "";
                string offsetName = c.OffsetAccount?.AccountName ?? "";

                string line = string.Join(",",
                    c.CreditNoteId,
                    c.NoteDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    c.CustomerId?.ToString() ?? "",
                    customerName.Replace(",", " "),
                    c.AccountId,
                    accountName.Replace(",", " "),
                    c.OffsetAccountId?.ToString() ?? "",
                    offsetName.Replace(",", " "),
                    c.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                    (c.Reason ?? "").Replace(",", " "),
                    (c.Description ?? "").Replace(",", " "),
                    c.IsPosted ? "1" : "0",
                    c.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    c.UpdatedAt.HasValue
                        ? c.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                        : "",
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
                string? postedBy = User?.Identity?.Name ?? "System";
                foreach (var note in notes.Where(n => n.IsPosted))
                {
                    await _ledgerPostingService.ReverseForHeaderDeleteAsync(Models.LedgerSourceType.CreditNote, note.CreditNoteId, postedBy, "حذف جماعي إشعار إضافة");
                }
                _context.CreditNotes.RemoveRange(notes);
                await _context.SaveChangesAsync();

                TempData["Success"] = $"تم حذف {notes.Count} من إشعارات الإضافة المحددة.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"لا يمكن حذف بعض الإشعارات: {ex.Message}";
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
                string? postedBy = User?.Identity?.Name ?? "System";
                foreach (var note in all.Where(n => n.IsPosted))
                {
                    await _ledgerPostingService.ReverseForHeaderDeleteAsync(Models.LedgerSourceType.CreditNote, note.CreditNoteId, postedBy, "حذف جميع إشعارات الإضافة");
                }
                _context.CreditNotes.RemoveRange(all);
                await _context.SaveChangesAsync();

                TempData["Success"] = "تم حذف جميع إشعارات الإضافة.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"لا يمكن حذف جميع الإشعارات: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // جلب حساب الطرف تلقائياً عند اختيار العميل (نفس نمط إشعار الخصم)
        // =========================================================
        public async Task<IActionResult> GetCustomerAccount(int customerId)
        {
            var customer = await _context.Customers
                .AsNoTracking()
                .Where(c => c.CustomerId == customerId)
                .Select(c => new { c.AccountId })
                .FirstOrDefaultAsync();

            if (customer == null || !customer.AccountId.HasValue)
            {
                return Json(new { success = false, message = "العميل غير موجود أو غير مربوط بحساب محاسبي." });
            }

            return Json(new { success = true, accountId = customer.AccountId.Value });
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
        public async Task<IActionResult> Create(int? customerId = null)
        {
            var model = new CreditNote
            {
                NoteDate = DateTime.Now,
                CreatedAt = DateTime.Now
            };
            if (customerId.HasValue && customerId.Value > 0)
            {
                var customer = await _context.Customers
                    .AsNoTracking()
                    .Where(c => c.CustomerId == customerId.Value)
                    .Select(c => new { c.CustomerId, c.AccountId })
                    .FirstOrDefaultAsync();
                if (customer != null)
                {
                    model.CustomerId = customer.CustomerId;
                    model.AccountId = customer.AccountId ?? 0;
                    ViewBag.LockCustomer = true;
                }
            }
            PopulateLookups(model.CustomerId, model.AccountId > 0 ? model.AccountId : null, null);
            return View(model);
        }

        // POST: CreditNotes/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreditNote creditNote)
        {
            // إن لم يُرسل حساب الطرف وتم اختيار عميل، نملأه من حساب العميل (نفس نمط إذن الاستلام)
            if (creditNote.AccountId <= 0 && creditNote.CustomerId.HasValue)
            {
                var cust = await _context.Customers
                    .AsNoTracking()
                    .Where(c => c.CustomerId == creditNote.CustomerId.Value)
                    .Select(c => new { c.AccountId })
                    .FirstOrDefaultAsync();
                if (cust?.AccountId != null)
                {
                    creditNote.AccountId = cust.AccountId.Value;
                    ModelState.Remove("AccountId");
                }
            }

            if (creditNote.AccountId <= 0)
                ModelState.AddModelError(nameof(CreditNote.AccountId), "حساب الطرف مطلوب.");

            if (ModelState.IsValid)
            {
                // تعبئة بيانات التتبع
                creditNote.CreatedAt = DateTime.Now;
                creditNote.UpdatedAt = creditNote.CreatedAt;
                creditNote.CreatedBy = User?.Identity?.Name ?? "System";
                creditNote.IsPosted = false;
                creditNote.PostedAt = null;
                creditNote.PostedBy = null;
                creditNote.IsLocked = true; // غلق الإشعار بعد الحفظ

                _context.Add(creditNote);
                await _context.SaveChangesAsync();

                await _activityLogger.LogAsync(UserActionType.Create, "CreditNote", creditNote.CreditNoteId, $"إنشاء إشعار إضافة رقم {creditNote.CreditNoteId}");

                try
                {
                    await _ledgerPostingService.PostCreditNoteAsync(creditNote.CreditNoteId, User?.Identity?.Name ?? "System");
                    TempData["Success"] = "تم حفظ وترحيل إشعار الإضافة بنجاح.";
                }
                catch (Exception ex)
                {
                    TempData["Error"] = TempData["ErrorMessage"] = $"تم الحفظ، لكن فشل الترحيل: {ex.Message}";
                }
                return RedirectToAction(nameof(Edit), new { id = creditNote.CreditNoteId });
            }

            PopulateLookups(creditNote.CustomerId, creditNote.AccountId, creditNote.OffsetAccountId);
            return View(creditNote);
        }

        // GET: CreditNotes/Unlock/5 — فتح الإشعار للتعديل (سيُضاف التحقق من الصلاحية لاحقاً)
        public async Task<IActionResult> Unlock(int? id)
        {
            if (id == null)
                return NotFound();

            var creditNote = await _context.CreditNotes.FindAsync(id);
            if (creditNote == null)
                return NotFound();

            creditNote.IsLocked = false;
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Edit), new { id });
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

            // ملء حساب الطرف من العميل إن كان فارغاً (نفس نمط Create)
            if (input.AccountId <= 0 && input.CustomerId.HasValue)
            {
                var cust = await _context.Customers
                    .AsNoTracking()
                    .Where(c => c.CustomerId == input.CustomerId.Value)
                    .Select(c => new { c.AccountId })
                    .FirstOrDefaultAsync();
                if (cust?.AccountId != null)
                {
                    input.AccountId = cust.AccountId.Value;
                    ModelState.Remove("AccountId");
                }
            }
            if (input.AccountId <= 0)
                ModelState.AddModelError(nameof(CreditNote.AccountId), "حساب الطرف مطلوب.");

            if (!ModelState.IsValid)
            {
                PopulateLookups(input.CustomerId, input.AccountId, null);
                return View(input);
            }

            var existing = await _context.CreditNotes.FindAsync(id);
            if (existing == null)
                return NotFound();

            existing.NoteDate = input.NoteDate;
            existing.CustomerId = input.CustomerId;
            existing.AccountId = input.AccountId;
            existing.Amount = input.Amount;
            existing.Reason = input.Reason;
            existing.Description = input.Description;
            existing.UpdatedAt = DateTime.Now;
            existing.IsLocked = true;

            try
            {
                string? postedBy = User?.Identity?.Name ?? "System";

                if (existing.IsPosted)
                {
                    // إشعار مرحّل: نعكس القيود القديمة ثم نرحّل بالمبلغ الجديد
                    await _ledgerPostingService.ReverseForHeaderDeleteAsync(Models.LedgerSourceType.CreditNote, id, postedBy, "تعديل إشعار إضافة وإعادة ترحيله");
                    existing.IsPosted = false;
                    existing.PostedAt = null;
                    existing.PostedBy = null;
                }

                await _context.SaveChangesAsync();

                await _activityLogger.LogAsync(UserActionType.Edit, "CreditNote", id, $"تعديل إشعار إضافة رقم {id}");

                try
                {
                    await _ledgerPostingService.PostCreditNoteAsync(id, postedBy);
                    TempData["Success"] = "تم حفظ وترحيل إشعار الإضافة بنجاح.";
                }
                catch (Exception ex)
                {
                    TempData["Error"] = $"تم الحفظ وعكس الترحيل القديم، لكن فشل الترحيل الجديد: {ex.Message}";
                }
                return RedirectToAction(nameof(Edit), new { id });
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
                if (creditNote.IsPosted)
                    await _ledgerPostingService.ReverseForHeaderDeleteAsync(Models.LedgerSourceType.CreditNote, id, User?.Identity?.Name ?? "System", "حذف إشعار إضافة");

                _context.CreditNotes.Remove(creditNote);
                await _context.SaveChangesAsync();

                await _activityLogger.LogAsync(UserActionType.Delete, "CreditNote", id, $"حذف إشعار إضافة رقم {id}");

                TempData["Success"] = "تم حذف إشعار الإضافة بنجاح.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = TempData["ErrorMessage"] = $"لا يمكن حذف الإشعار: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        private bool CreditNoteExists(int id)
        {
            return _context.CreditNotes.Any(e => e.CreditNoteId == id);
        }
    }
}
