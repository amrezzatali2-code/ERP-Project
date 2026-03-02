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
using ERP.Filters;
using ERP.Infrastructure;                         // PagedResult + ApplySearchSort + UserActivityLogger
using ERP.Models;                                 // DebitNote, UserActionType...
using ERP.Security;
using ERP.Services;                               // ILedgerPostingService

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إشعارات الخصم (DebitNotes)
    /// - CRUD (إنشاء/تعديل/تفاصيل/حذف).
    /// - زر حفظ وترحيل = حفظ + ترحيل محاسبي (LedgerEntries + تحديث حساب العميل + الأرباح).
    /// </summary>
    [RequirePermission("DebitNotes.Index")]
    public class DebitNotesController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ILedgerPostingService _ledgerPostingService;
        private readonly IUserActivityLogger _activityLogger;

        public DebitNotesController(AppDbContext context, ILedgerPostingService ledgerPostingService, IUserActivityLogger activityLogger)
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
            ViewData["CustomerId"] = new SelectList(
                _context.Customers.AsNoTracking().Where(c => c.IsActive == true).OrderBy(c => c.CustomerName),
                "CustomerId", "CustomerName", customerId);
            ViewData["AccountId"] = new SelectList(
                _context.Accounts.AsNoTracking().OrderBy(a => a.AccountName),
                "AccountId", "AccountName", accountId);
            ViewData["OffsetAccountId"] = new SelectList(
                _context.Accounts.AsNoTracking().OrderBy(a => a.AccountName),
                "AccountId", "AccountName", offsetAccountId);
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
                    ["DebitNoteId"] = d => d.DebitNoteId,                                         // رقم الإشعار / رقم المستند
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
            sb.AppendLine("DebitNoteId,NoteDate,CustomerId,CustomerName,AccountId,AccountName,OffsetAccountId,OffsetAccountName,Amount,Reason,Description,IsPosted,CreatedAt,UpdatedAt,CreatedBy,PostedAt,PostedBy");

            // كل إشعار في سطر CSV
            foreach (var d in list)
            {
                string customerName = d.Customer?.CustomerName ?? "";
                string accountName = d.Account?.AccountName ?? "";
                string offsetAccountName = d.OffsetAccount?.AccountName ?? "";

                string line = string.Join(",",
                    d.DebitNoteId,
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
                string? postedBy = User?.Identity?.Name ?? "System";
                foreach (var note in notes.Where(n => n.IsPosted))
                {
                    await _ledgerPostingService.ReverseForHeaderDeleteAsync(LedgerSourceType.DebitNote, note.DebitNoteId, postedBy, "حذف جماعي إشعار خصم");
                }
                _context.DebitNotes.RemoveRange(notes);
                await _context.SaveChangesAsync();

                TempData["Success"] = $"تم حذف {notes.Count} من إشعارات الخصم المحددة.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"لا يمكن حذف بعض الإشعارات: {ex.Message}";
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
                string? postedBy = User?.Identity?.Name ?? "System";
                foreach (var note in all.Where(n => n.IsPosted))
                {
                    await _ledgerPostingService.ReverseForHeaderDeleteAsync(LedgerSourceType.DebitNote, note.DebitNoteId, postedBy, "حذف جميع إشعارات الخصم");
                }
                _context.DebitNotes.RemoveRange(all);
                await _context.SaveChangesAsync();

                TempData["Success"] = "تم حذف جميع إشعارات الخصم.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"لا يمكن حذف جميع الإشعارات: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // جلب حساب الطرف تلقائياً عند اختيار العميل (للواجهة)
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
        public async Task<IActionResult> Create(int? customerId = null)
        {
            var model = new DebitNote
            {
                NoteDate = DateTime.Now,
                IsPosted = false
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

        // POST: DebitNotes/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(DebitNote debitNote)
        {
            // إن لم يُرسل حساب الطرف وتم اختيار عميل، نملأه من حساب العميل (نفس نمط إذن الاستلام)
            if (debitNote.AccountId <= 0 && debitNote.CustomerId.HasValue)
            {
                var cust = await _context.Customers
                    .AsNoTracking()
                    .Where(c => c.CustomerId == debitNote.CustomerId.Value)
                    .Select(c => new { c.AccountId })
                    .FirstOrDefaultAsync();
                if (cust?.AccountId != null)
                {
                    debitNote.AccountId = cust.AccountId.Value;
                    ModelState.Remove("AccountId");
                }
            }

            if (debitNote.AccountId <= 0)
                ModelState.AddModelError(nameof(DebitNote.AccountId), "حساب الطرف مطلوب.");

            if (ModelState.IsValid)
            {
                debitNote.CreatedAt = DateTime.Now;
                debitNote.UpdatedAt = null;
                debitNote.IsPosted = false;
                debitNote.PostedAt = null;
                debitNote.IsLocked = true; // غلق الإشعار بعد الحفظ
                if (string.IsNullOrEmpty(debitNote.CreatedBy))
                    debitNote.CreatedBy = User?.Identity?.Name ?? "System";

                _context.Add(debitNote);
                await _context.SaveChangesAsync();

                await _activityLogger.LogAsync(UserActionType.Create, "DebitNote", debitNote.DebitNoteId, $"إنشاء إشعار خصم رقم {debitNote.DebitNoteId}");

                try
                {
                    await _ledgerPostingService.PostDebitNoteAsync(debitNote.DebitNoteId, User?.Identity?.Name ?? "System");
                    TempData["Success"] = "تم حفظ وترحيل إشعار الخصم بنجاح.";
                }
                catch (Exception ex)
                {
                    TempData["Error"] = TempData["ErrorMessage"] = $"تم الحفظ، لكن فشل الترحيل: {ex.Message}";
                }
                return RedirectToAction(nameof(Edit), new { id = debitNote.DebitNoteId });
            }

            PopulateLookups(debitNote.CustomerId, debitNote.AccountId, debitNote.OffsetAccountId);
            return View(debitNote);
        }

        // GET: DebitNotes/Unlock/5 — فتح الإشعار للتعديل (سيُضاف التحقق من الصلاحية لاحقاً)
        public async Task<IActionResult> Unlock(int? id)
        {
            if (id == null)
                return NotFound();

            var debitNote = await _context.DebitNotes.FindAsync(id);
            if (debitNote == null)
                return NotFound();

            debitNote.IsLocked = false;
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Edit), new { id });
        }

        // GET: DebitNotes/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
                return NotFound();

            var debitNote = await _context.DebitNotes.FindAsync(id);
            if (debitNote == null)
                return NotFound();

            PopulateLookups(debitNote.CustomerId, debitNote.AccountId, debitNote.OffsetAccountId);
            return View(debitNote);
        }

        // POST: DebitNotes/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, DebitNote input)
        {
            if (id != input.DebitNoteId)
                return NotFound();

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
                ModelState.AddModelError(nameof(DebitNote.AccountId), "حساب الطرف مطلوب.");

            if (!ModelState.IsValid)
            {
                PopulateLookups(input.CustomerId, input.AccountId, null);
                return View(input);
            }

            var existing = await _context.DebitNotes.FindAsync(id);
            if (existing == null)
                return NotFound();

            var oldValues = System.Text.Json.JsonSerializer.Serialize(new { existing.NoteDate, existing.CustomerId, existing.AccountId, existing.Amount });
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
                    await _ledgerPostingService.ReverseForHeaderDeleteAsync(Models.LedgerSourceType.DebitNote, id, postedBy, "تعديل إشعار خصم وإعادة ترحيله");
                    existing.IsPosted = false;
                    existing.PostedAt = null;
                    existing.PostedBy = null;
                }

                await _context.SaveChangesAsync();

                var newValues = System.Text.Json.JsonSerializer.Serialize(new { existing.NoteDate, existing.CustomerId, existing.AccountId, existing.Amount });
                await _activityLogger.LogAsync(UserActionType.Edit, "DebitNote", id, $"تعديل إشعار خصم رقم {id}", oldValues, newValues);

                try
                {
                    await _ledgerPostingService.PostDebitNoteAsync(id, postedBy);
                    TempData["Success"] = "تم حفظ وترحيل إشعار الخصم بنجاح.";
                }
                catch (Exception ex)
                {
                    TempData["Error"] = $"تم الحفظ وعكس الترحيل القديم، لكن فشل الترحيل الجديد: {ex.Message}";
                }
                return RedirectToAction(nameof(Edit), new { id });
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!DebitNoteExists(id))
                    return NotFound();
                throw;
            }
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
            if (debitNote == null)
                return RedirectToAction(nameof(Index));

            try
            {
                var oldValues = System.Text.Json.JsonSerializer.Serialize(new { debitNote.NoteDate, debitNote.CustomerId, debitNote.AccountId, debitNote.Amount });
                if (debitNote.IsPosted)
                    await _ledgerPostingService.ReverseForHeaderDeleteAsync(Models.LedgerSourceType.DebitNote, id, User?.Identity?.Name ?? "System", "حذف إشعار خصم");

                _context.DebitNotes.Remove(debitNote);
                await _context.SaveChangesAsync();

                await _activityLogger.LogAsync(UserActionType.Delete, "DebitNote", id, $"حذف إشعار خصم رقم {id}", oldValues: oldValues);

                TempData["Success"] = "تم حذف إشعار الخصم.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = TempData["ErrorMessage"] = $"لا يمكن حذف الإشعار: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        private bool DebitNoteExists(int id)
        {
            return _context.DebitNotes.Any(e => e.DebitNoteId == id);
        }
    }
}
