using System;                                // متغيرات التاريخ DateTime
using System.Linq;                           // أوامر LINQ مثل Where و OrderBy
using System.Text;                           // StringBuilder لتجهيز ملف التصدير
using System.Threading.Tasks;                // async / await
using Microsoft.AspNetCore.Mvc;              // أساس الكنترولر
using Microsoft.EntityFrameworkCore;         // Include / AsNoTracking
using ERP.Data;                              // AppDbContext
using ERP.Infrastructure;                    // PagedResult
using ERP.Models;                            // UserActivityLog, User

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

                "ActionTime" => desc
                    ? query.OrderByDescending(x => x.ActionTime)
                    : query.OrderBy(x => x.ActionTime),

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

                query = mode switch
                {
                    // البحث باسم المستخدم أو الاسم الظاهر
                    "user" => query.Where(x =>
                        x.User != null &&
                        (
                            x.User.UserName.Contains(term) ||
                            (x.User.DisplayName != null && x.User.DisplayName.Contains(term))
                        )),

                    // البحث بنوع العملية
                    "action" => query.Where(x =>
                        x.ActionType.ToString().Contains(term)),

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

                    // البحث في الكل
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
                        x.ActionType.ToString().Contains(term))
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
                "ActionTime" => desc
                    ? query.OrderByDescending(x => x.ActionTime)
                    : query.OrderBy(x => x.ActionTime),
                _ => desc
                    ? query.OrderByDescending(x => x.ActionTime)
                    : query.OrderBy(x => x.ActionTime)
            };

            var data = await query.ToListAsync();

            // تجهيز ملف CSV يمكن فتحه في Excel
            var sb = new StringBuilder();

            // عنوان الأعمدة
            sb.AppendLine("Id,UserName,ActionType,EntityName,EntityId,ActionTime,Description,IpAddress,UserAgent");

            foreach (var r in data)
            {
                string userName = r.User?.UserName ?? "";
                string entity = r.EntityName ?? "";
                string descText = r.Description ?? "";
                string ip = r.IpAddress ?? "";
                string agent = r.UserAgent ?? "";

                // دالة مساعدة لتأمين النص (تجنب كسر CSV)
                string Safe(string? s) =>
                    string.IsNullOrEmpty(s)
                        ? ""
                        : "\"" + s.Replace("\"", "\"\"") + "\"";

                string line = string.Join(",",
                    r.Id,
                    Safe(userName),
                    r.ActionType.ToString(),
                    Safe(entity),
                    r.EntityId?.ToString() ?? "",
                    r.ActionTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    Safe(descText),
                    Safe(ip),
                    Safe(agent));

                sb.AppendLine(line);
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = $"UserActivityLog_{DateTime.Now:yyyyMMdd_HHmmss}.csv";

            return File(bytes, "text/csv; charset=utf-8", fileName);
        }
    }
}
