using System;                                // متغيرات التاريخ DateTime
using System.Linq;                           // أوامر LINQ مثل Where و OrderBy
using System.Text;                           // StringBuilder لتجهيز ملف التصدير
using ClosedXML.Excel;                       // تصدير Excel فعلي (.xlsx)
using System.Threading.Tasks;                // async / await
using Microsoft.AspNetCore.Mvc;              // أساس الكنترولر
using Microsoft.EntityFrameworkCore;         // Include / AsNoTracking
using ERP.Data;                              // AppDbContext
using ERP.Infrastructure;                    // PagedResult
using ERP.Models;                            // UserActivityLog, User, UserActionType

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر سجل نشاط المستخدمين:
    /// عرض + تصفية + حذف + تصدير.
    /// لا يوجد إنشاء / تعديل يدوي للسجلات.
    /// </summary>
    public class UserActivityLogsController : Controller
    {
        // كائن الاتصال بقاعدة البيانات
        private readonly AppDbContext _context;

        public UserActivityLogsController(AppDbContext context)
        {
            _context = context;
        }

        // GET: UserActivityLogs
        // عرض قائمة سجل النشاط بالنظام الثابت (بحث + فلترة + ترتيب + تقسيم صفحات)
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            int page = 1,
            int pageSize = 25,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null)
        {
            // تصحيح: إذا وُضع اسم عمود ترتيب (مثل ActionTime) بالخطأ في حقل البحث، نتجاهله
            var sortColumnNames = new[] { "Id", "UserName", "ActionType", "EntityName", "EntityId", "ActionTime", "IpAddress" };
            if (!string.IsNullOrWhiteSpace(search) && sortColumnNames.Contains(search.Trim(), StringComparer.OrdinalIgnoreCase))
                search = null;

            // استعلام أساسي مع Include للمستخدم
            // مهم: نثبّت النوع كـ IQueryable لحل مشكلة CS0266
            IQueryable<UserActivityLog> query = _context.UserActivityLogs
                .AsNoTracking()
                .Include(x => x.User);

            // تطبيق الفلاتر (بحث + كود من/إلى + تاريخ)
            query = ApplyFilters(
                query,
                search,
                searchBy,
                useDateRange,
                fromDate,
                toDate,
                fromCode,
                toCode);

            // الترتيب
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            sort ??= "ActionTime";   // الترتيب الافتراضي: من الأحدث إلى الأقدم

            query = sort switch
            {
                "Id" => desc
                    ? query.OrderByDescending(x => x.Id)
                    : query.OrderBy(x => x.Id),
                "UserName" => desc
                    ? query.OrderByDescending(x => x.User!.UserName)
                    : query.OrderBy(x => x.User!.UserName),
                "ActionType" => desc
                    ? query.OrderByDescending(x => x.ActionType)
                    : query.OrderBy(x => x.ActionType),
                "EntityName" => desc
                    ? query.OrderByDescending(x => x.EntityName)
                    : query.OrderBy(x => x.EntityName),
                "EntityId" => desc
                    ? query.OrderByDescending(x => x.EntityId)
                    : query.OrderBy(x => x.EntityId),
                "ActionTime" => desc
                    ? query.OrderByDescending(x => x.ActionTime)
                    : query.OrderBy(x => x.ActionTime),
                "IpAddress" => desc
                    ? query.OrderByDescending(x => x.IpAddress)
                    : query.OrderBy(x => x.IpAddress),

                _ => desc
                    ? query.OrderByDescending(x => x.ActionTime)
                    : query.OrderBy(x => x.ActionTime)
            };

            // تجهيز نتيجة الصفحات بالنظام الثابت

            // تجهيز نتيجة الصفحات بالنظام الثابت
            var result = await PagedResult<UserActivityLog>.CreateAsync(
                query,
                page,
                pageSize,
                search,     // نص البحث
                desc,       // ترتيب تنازلي؟
                sort,       // اسم عمود الترتيب
                searchBy);  // طريقة البحث



            // تمرير قيم البحث/الفلترة للواجهة
            ViewBag.Search = search;
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.Sort = sort;
            ViewBag.Dir = desc ? "desc" : "asc";
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.DateField = "ActionTime";

            return View(result);
        }

        /// <summary>
        /// دالة خاصة لتطبيق الفلاتر على استعلام سجل النشاط.
        /// </summary>
        private IQueryable<UserActivityLog> ApplyFilters(
            IQueryable<UserActivityLog> query,
            string? search,
            string? searchBy,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            int? fromCode,
            int? toCode)
        {
            // فلتر البحث النصّي
            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = search.Trim();
                string mode = (searchBy ?? "all").ToLower();

                // نوع العملية enum: لا نستخدم ToString() في الـ query لأنه لا يترجم لـ SQL
                var matchingActionTypes = Enum.GetValues<UserActionType>()
                    .Where(e => e.ToString().Contains(term, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                query = mode switch
                {
                    // البحث باسم المستخدم أو الاسم الظاهر
                    "user" => query.Where(x =>
                        x.User != null &&
                        (
                            x.User.UserName.Contains(term) ||
                            (x.User.DisplayName != null && x.User.DisplayName.Contains(term))
                        )),

                    // البحث بنوع العملية (مقارنة enum بدون ToString في SQL)
                    "action" => matchingActionTypes.Length > 0
                        ? query.Where(x => matchingActionTypes.Contains(x.ActionType))
                        : query.Where(x => false),

                    // البحث باسم الكيان
                    "entity" => query.Where(x =>
                        x.EntityName != null && x.EntityName.Contains(term)),

                    // البحث بالوصف
                    "description" => query.Where(x =>
                        x.Description != null && x.Description.Contains(term)),

                    // البحث برقم السجل
                    "id" => int.TryParse(term, out var idVal)
                        ? query.Where(x => x.Id == idVal)
                        : query.Where(x => false),

                    // البحث في الكل (ActionType عبر enum values بدل ToString)
                    _ => query.Where(x =>
                        (x.User != null &&
                            (
                                x.User.UserName.Contains(term) ||
                                (x.User.DisplayName != null && x.User.DisplayName.Contains(term))
                            ))
                        ||
                        (x.EntityName != null && x.EntityName.Contains(term))
                        ||
                        (x.Description != null && x.Description.Contains(term))
                        ||
                        (x.OldValues != null && x.OldValues.Contains(term))
                        ||
                        (x.NewValues != null && x.NewValues.Contains(term))
                        ||
                        (matchingActionTypes.Length > 0 && matchingActionTypes.Contains(x.ActionType)))
                };
            }

            // فلتر كود من/إلى (على Id)
            if (fromCode.HasValue)
                query = query.Where(x => x.Id >= fromCode.Value);

            if (toCode.HasValue)
                query = query.Where(x => x.Id <= toCode.Value);

            // فلتر التاريخ (على ActionTime)
            if (useDateRange)
            {
                if (fromDate.HasValue)
                    query = query.Where(x => x.ActionTime >= fromDate.Value);

                if (toDate.HasValue)
                    query = query.Where(x => x.ActionTime <= toDate.Value);
            }

            return query;
        }

        // GET: UserActivityLogs/Details/5
        // عرض تفاصيل سجل نشاط واحد
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
                return NotFound();

            var log = await _context.UserActivityLogs
                .Include(x => x.User)
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == id);

            if (log == null)
                return NotFound();

            return View(log);
        }

        // GET: UserActivityLogs/Delete/5
        // صفحة تأكيد حذف سجل واحد
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
                return NotFound();

            var log = await _context.UserActivityLogs
                .Include(x => x.User)
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == id);

            if (log == null)
                return NotFound();

            return View(log);
        }

        // POST: UserActivityLogs/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var log = await _context.UserActivityLogs.FindAsync(id);
            if (log != null)
            {
                _context.UserActivityLogs.Remove(log);
                await _context.SaveChangesAsync();
                TempData["Success"] = "تم حذف سجل النشاط بنجاح.";
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: UserActivityLogs/BulkDelete
        // حذف مجموعة سجلات تم اختيارها من الجدول
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(int[] ids)
        {
            if (ids == null || ids.Length == 0)
            {
                TempData["Error"] = "من فضلك اختر على الأقل سجل نشاط واحد للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var logs = await _context.UserActivityLogs
                .Where(x => ids.Contains(x.Id))
                .ToListAsync();

            if (logs.Count == 0)
            {
                TempData["Error"] = "لم يتم العثور على السجلات المحددة.";
                return RedirectToAction(nameof(Index));
            }

            _context.UserActivityLogs.RemoveRange(logs);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم حذف {logs.Count} سجل نشاط.";
            return RedirectToAction(nameof(Index));
        }

        // POST: UserActivityLogs/DeleteAll
        // حذف كل سجل النشاط (لبيئة تجريبية فقط)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var allLogs = await _context.UserActivityLogs.ToListAsync();
            if (allLogs.Count == 0)
            {
                TempData["Info"] = "لا توجد سجلات نشاط للحذف.";
                return RedirectToAction(nameof(Index));
            }

            _context.UserActivityLogs.RemoveRange(allLogs);
            await _context.SaveChangesAsync();

            TempData["Success"] = "تم حذف جميع سجل النشاط.";
            return RedirectToAction(nameof(Index));
        }

        // GET: UserActivityLogs/Export
        // تصدير سجل النشاط (Excel/CSV) بعد تطبيق نفس الفلاتر
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
            string format = "excel")
        {
            // استعلام أساسي للتصدير مع Include للمستخدم
            IQueryable<UserActivityLog> query = _context.UserActivityLogs
                .AsNoTracking()
                .Include(x => x.User);

            // نفس الفلاتر المستخدمة في Index
            query = ApplyFilters(
                query,
                search,
                searchBy,
                useDateRange,
                fromDate,
                toDate,
                fromCode,
                toCode);

            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            sort ??= "ActionTime";

            query = sort switch
            {
                "Id" => desc ? query.OrderByDescending(x => x.Id) : query.OrderBy(x => x.Id),
                "UserName" => desc ? query.OrderByDescending(x => x.User!.UserName) : query.OrderBy(x => x.User!.UserName),
                "ActionType" => desc ? query.OrderByDescending(x => x.ActionType) : query.OrderBy(x => x.ActionType),
                "EntityName" => desc ? query.OrderByDescending(x => x.EntityName) : query.OrderBy(x => x.EntityName),
                "EntityId" => desc ? query.OrderByDescending(x => x.EntityId) : query.OrderBy(x => x.EntityId),
                "ActionTime" => desc ? query.OrderByDescending(x => x.ActionTime) : query.OrderBy(x => x.ActionTime),
                "IpAddress" => desc ? query.OrderByDescending(x => x.IpAddress) : query.OrderBy(x => x.IpAddress),
                _ => desc ? query.OrderByDescending(x => x.ActionTime) : query.OrderBy(x => x.ActionTime)
            };

            var data = await query.ToListAsync();

            if (string.Equals(format, "excel", StringComparison.OrdinalIgnoreCase))
            {
                // تصدير Excel فعلي (.xlsx) باستخدام ClosedXML
                using var workbook = new XLWorkbook();
                var ws = workbook.Worksheets.Add("سجل النشاط");
                ws.RightToLeft = true; // دعم اتجاه RTL للعربية

                string[] headers = { "المعرّف", "المستخدم", "نوع العملية", "اسم الكيان", "رقم السجل", "وقت العملية", "الوصف", "عنوان IP", "قبل", "بعد" };
                for (int i = 0; i < headers.Length; i++)
                    ws.Cell(1, i + 1).Value = headers[i];

                int row = 2;
                foreach (var r in data)
                {
                    ws.Cell(row, 1).Value = r.Id;
                    ws.Cell(row, 2).Value = r.User?.UserName ?? "";
                    ws.Cell(row, 3).Value = r.ActionType.ToString();
                    ws.Cell(row, 4).Value = r.EntityName ?? "";
                    ws.Cell(row, 5).Value = r.EntityId?.ToString() ?? "";
                    ws.Cell(row, 6).Value = r.ActionTime.ToString("yyyy-MM-dd HH:mm:ss");
                    ws.Cell(row, 7).Value = r.Description ?? "";
                    ws.Cell(row, 8).Value = r.IpAddress ?? "";
                    ws.Cell(row, 9).Value = r.OldValues ?? "";
                    ws.Cell(row, 10).Value = r.NewValues ?? "";
                    row++;
                }

                using var stream = new System.IO.MemoryStream();
                workbook.SaveAs(stream);
                var fileName = $"UserActivityLog_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }

            // CSV
            var sb = new StringBuilder();
            sb.AppendLine("Id,UserName,ActionType,EntityName,EntityId,ActionTime,Description,IpAddress,UserAgent,OldValues,NewValues");
            foreach (var r in data)
            {
                string Safe(string? s) => string.IsNullOrEmpty(s) ? "" : "\"" + s.Replace("\"", "\"\"") + "\"";
                sb.AppendLine(string.Join(",",
                    r.Id,
                    Safe(r.User?.UserName),
                    r.ActionType.ToString(),
                    Safe(r.EntityName),
                    r.EntityId?.ToString() ?? "",
                    r.ActionTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    Safe(r.Description),
                    Safe(r.IpAddress),
                    Safe(r.UserAgent),
                    Safe(r.OldValues),
                    Safe(r.NewValues)));
            }
            var utf8 = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            var bytes = utf8.GetBytes(sb.ToString());
            return File(bytes, "text/csv; charset=utf-8", $"UserActivityLog_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        }
    }
}
