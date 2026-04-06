using System;                                // متغيرات التاريخ DateTime
using System.Collections.Generic;           // Dictionary
using System.Linq;                           // أوامر LINQ مثل Where و OrderBy
using System.Text;                           // StringBuilder لتجهيز ملف التصدير
using System.Text.RegularExpressions;        // Regex لتعريب الوصف
using ClosedXML.Excel;                       // تصدير Excel فعلي (.xlsx)
using System.Threading.Tasks;                // async / await
using Microsoft.AspNetCore.Mvc;              // أساس الكنترولر
using Microsoft.EntityFrameworkCore;         // Include / AsNoTracking
using ERP.Data;                              // AppDbContext
using ERP.Filters;
using ERP.Infrastructure;                    // PagedResult
using ERP.Models;                            // UserActivityLog, User, UserActionType
using ERP.Security;

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر سجل نشاط المستخدمين:
    /// عرض + تصفية + حذف + تصدير.
    /// لا يوجد إنشاء / تعديل يدوي للسجلات.
    /// </summary>
    [RequirePermission("UserActivityLogs.Index")]
    public class UserActivityLogsController : Controller
    {
        // كائن الاتصال بقاعدة البيانات
        private readonly AppDbContext _context;

        public UserActivityLogsController(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>تحويل اسم الكيان الإنجليزي إلى عربي للعرض.</summary>
        private static string EntityNameToArabic(string? en)
        {
            if (string.IsNullOrWhiteSpace(en)) return "";
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["CashReceipt"] = "إذن استلام نقدية",
                ["CashPayment"] = "إذن دفع",
                ["SalesInvoice"] = "فاتورة مبيعات",
                ["PurchaseInvoice"] = "فاتورة مشتريات",
                ["PurchaseRequest"] = "طلب شراء",
                ["PurchaseRequests"] = "طلبات شراء",
                ["Product"] = "صنف",
                ["User"] = "مستخدم",
                ["Customer"] = "عميل",
                ["Category"] = "فئة",
                ["Warehouse"] = "مخزن",
                ["Account"] = "حساب",
                ["LedgerEntry"] = "قيد محاسبي",
                ["StockLedger"] = "دفتر الحركة المخزنية",
                ["PILine"] = "سطر فاتورة مشتريات",
                ["SILine"] = "سطر فاتورة مبيعات",
                ["SalesInvoiceLine"] = "سطر فاتورة مبيعات",
                ["SalesInvoiceLines"] = "سطور فاتورة مبيعات",
                ["SalesInvoices"] = "فواتير مبيعات",
                ["PurchaseInvoices"] = "فواتير مشتريات",
                ["PurchaseInvoiceLine"] = "سطر فاتورة مشتريات",
                ["SalesOrder"] = "أمر بيع",
                ["SalesOrderLine"] = "سطر أمر بيع",
                ["SalesReturn"] = "مرتجع بيع",
                ["PurchaseReturn"] = "مرتجع شراء",
                ["DebitNote"] = "إشعار خصم",
                ["CreditNote"] = "إشعار إضافة",
                ["StockTransfer"] = "تحويل مخزني",
                ["ProductDiscountOverride"] = "خصم يدوي للصنف",
                ["DocumentSeries"] = "سلسلة مستندات",
                ["SalesReturnLine"] = "سطر مرتجع بيع",
                ["PurchaseReturnLine"] = "سطر مرتجع شراء",
                ["StockAdjustment"] = "تسوية مخزنية",
                ["Batch"] = "تشغيلة",
                ["Governorate"] = "محافظة",
                ["District"] = "مديرية",
                ["Area"] = "منطقة",
                ["NumberSeries"] = "سلاسل الترقيم",
                ["Policy"] = "سياسة",
                ["Invoice"] = "فاتورة",
                ["Permission"] = "صلاحية",
                ["Role"] = "دور",
                ["Branch"] = "فرع",
                ["City"] = "مدينة",
                ["ProductGroup"] = "مجموعة أصناف",
                ["StockAdjustmentLine"] = "سطر تسوية مخزنية",
                ["PRLine"] = "سطر طلب شراء",
                ["SOLine"] = "سطر أمر بيع"
            };
            return map.TryGetValue(en.Trim(), out var ar) ? ar : en;
        }

        /// <summary>تحويل مفاتيح ومصطلحات الوصف الإنجليزية إلى عربية (للعرض وللتصدير).</summary>
        private static string DescriptionToArabic(string? desc)
        {
            if (string.IsNullOrWhiteSpace(desc)) return "";
            var s = desc;
            // عبارات مركبة أولاً
            s = Regex.Replace(s, @"Bulk/DeleteAll", "حذف جماعي / حذف الكل", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bDeleteAll\b", "حذف الكل", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bBulkDelete\b", "حذف جماعي", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bBulk\b", "جماعي", RegexOptions.IgnoreCase);
            // مفاتيح بالصيغة Key=
            s = Regex.Replace(s, @"\bStockLedger\s*=", "قيود دفتر الحركة=", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bLines\s*=", "عدد السطور=", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bSIId\s*=", "رقم الفاتورة=", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bSlId\s*=", "رقم السطر=", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bProdId\s*=", "كود الصنف=", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bQty\s*=", "الكمية=", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bSegments\s*=", "الشرائح=", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bLineNo\s*=", "رقم السطر=", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bClearAllLines\b", "مسح كل السطور", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bLinesCount\s*=", "عدد السطور=", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bPIId\s*=", "رقم فاتورة المشتريات=", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bPRId\s*=", "رقم طلب الشراء=", RegexOptions.IgnoreCase);
            return s;
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
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_user = null,
            string? filterCol_action = null,
            string? filterCol_entity = null,
            string? filterCol_entityId = null,
            string? filterCol_time = null,
            string? filterCol_ip = null,
            string? filterCol_desc = null)
        {
            // تصحيح: إذا وُضع اسم عمود ترتيب (مثل ActionTime) بالخطأ في حقل البحث، نتجاهله
            var sortColumnNames = new[] { "Id", "UserName", "ActionType", "EntityName", "EntityId", "ActionTime", "IpAddress" };
            if (!string.IsNullOrWhiteSpace(search) && sortColumnNames.Contains(search.Trim(), StringComparer.OrdinalIgnoreCase))
                search = null;

            IQueryable<UserActivityLog> query = _context.UserActivityLogs
                .AsNoTracking()
                .Include(x => x.User);

            query = ApplyFilters(
                query,
                search,
                searchBy,
                useDateRange,
                fromDate,
                toDate,
                fromCode,
                toCode);

            // فلاتر الأعمدة (بنمط Excel)
            query = ApplyColumnFilters(query, filterCol_id, filterCol_user, filterCol_action, filterCol_entity, filterCol_entityId, filterCol_time, filterCol_ip, filterCol_desc);

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



            ViewBag.Search = search;
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.Sort = sort;
            ViewBag.Dir = desc ? "desc" : "asc";
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.DateField = "ActionTime";
            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_User = filterCol_user;
            ViewBag.FilterCol_Action = filterCol_action;
            ViewBag.FilterCol_Entity = filterCol_entity;
            ViewBag.FilterCol_EntityId = filterCol_entityId;
            ViewBag.FilterCol_Time = filterCol_time;
            ViewBag.FilterCol_Ip = filterCol_ip;
            ViewBag.FilterCol_Desc = filterCol_desc;

            return View(result);
        }

        private static readonly char[] _filterSep = { '|', ',', ';' };

        private IQueryable<UserActivityLog> ApplyColumnFilters(
            IQueryable<UserActivityLog> query,
            string? filterCol_id,
            string? filterCol_user,
            string? filterCol_action,
            string? filterCol_entity,
            string? filterCol_entityId,
            string? filterCol_time,
            string? filterCol_ip,
            string? filterCol_desc)
        {
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) query = query.Where(x => ids.Contains(x.Id));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_user))
            {
                var terms = filterCol_user.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
                if (terms.Count > 0)
                    query = query.Where(x => x.User != null && terms.Any(t => (x.User.UserName != null && x.User.UserName.Contains(t)) || (x.User.DisplayName != null && x.User.DisplayName.Contains(t))));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_action))
            {
                var actions = filterCol_action.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => Enum.TryParse<UserActionType>(p.Trim(), true, out var a) ? a : (UserActionType?)null)
                    .Where(a => a.HasValue).Select(a => a!.Value).Distinct().ToList();
                if (actions.Count > 0) query = query.Where(x => actions.Contains(x.ActionType));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_entity))
            {
                var names = filterCol_entity.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
                if (names.Count > 0) query = query.Where(x => x.EntityName != null && names.Contains(x.EntityName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_entityId))
            {
                var ids = filterCol_entityId.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) query = query.Where(x => x.EntityId.HasValue && ids.Contains(x.EntityId.Value));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_time))
            {
                var terms = filterCol_time.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
                if (terms.Count > 0)
                    query = query.Where(x => terms.Any(t => x.ActionTime.ToString("yyyy-MM-dd HH:mm").Contains(t)));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_ip))
            {
                var terms = filterCol_ip.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
                if (terms.Count > 0) query = query.Where(x => x.IpAddress != null && terms.Any(t => x.IpAddress.Contains(t)));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_desc))
            {
                var terms = filterCol_desc.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
                if (terms.Count > 0) query = query.Where(x => x.Description != null && terms.Any(t => x.Description.Contains(t)));
            }
            return query;
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
                string numericTerm = NormalizeArabicDigitsToLatin(term);
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
                    "id" => int.TryParse(numericTerm, out var idVal)
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

        private static string NormalizeArabicDigitsToLatin(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            var chars = value.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = chars[i] switch
                {
                    >= '\u0660' and <= '\u0669' => (char)('0' + (chars[i] - '\u0660')),
                    >= '\u06F0' and <= '\u06F9' => (char)('0' + (chars[i] - '\u06F0')),
                    _ => chars[i]
                };
            }

            return new string(chars);
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

        /// <summary>قيم مميزة للعمود (للوحة فلتر الأعمدة بنمط Excel).</summary>
        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var col = (column ?? "").Trim().ToLowerInvariant();
            var q = _context.UserActivityLogs.AsNoTracking().Include(x => x.User);

            if (col == "id")
            {
                var ids = await q.Select(x => x.Id).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (col == "user" || col == "username")
            {
                var list = await q.Where(x => x.User != null).Select(x => x.User!.DisplayName ?? x.User.UserName ?? "").Where(s => s != "").Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (col == "action" || col == "actiontype")
            {
                var list = Enum.GetNames<UserActionType>().ToList();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (col == "entity" || col == "entityname")
            {
                var list = await q.Where(x => x.EntityName != null).Select(x => x.EntityName!).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = EntityNameToArabic(v) }));
            }
            if (col == "entityid")
            {
                var ids = await q.Where(x => x.EntityId.HasValue).Select(x => x.EntityId!.Value).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (col == "time" || col == "actiontime")
            {
                var dates = await q.Select(x => x.ActionTime).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                var list = dates.Select(d => d.ToString("yyyy-MM-dd HH:mm")).Distinct().ToList();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (col == "ip" || col == "ipaddress")
            {
                var list = await q.Where(x => x.IpAddress != null).Select(x => x.IpAddress!).Distinct().OrderBy(x => x).Take(300).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (col == "desc" || col == "description")
            {
                var list = await q.Where(x => x.Description != null).Select(x => x.Description!).Distinct().OrderBy(x => x).Take(300).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v.Length > 100 ? v.Substring(0, 100) : v, display = v.Length > 80 ? v.Substring(0, 80) + "..." : v }));
            }
            return Json(new List<object>());
        }

        // GET: UserActivityLogs/Export
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
            string? filterCol_id = null,
            string? filterCol_user = null,
            string? filterCol_action = null,
            string? filterCol_entity = null,
            string? filterCol_entityId = null,
            string? filterCol_time = null,
            string? filterCol_ip = null,
            string? filterCol_desc = null,
            string format = "excel")
        {
            IQueryable<UserActivityLog> query = _context.UserActivityLogs
                .AsNoTracking()
                .Include(x => x.User);

            query = ApplyFilters(query, search, searchBy, useDateRange, fromDate, toDate, fromCode, toCode);
            query = ApplyColumnFilters(query, filterCol_id, filterCol_user, filterCol_action, filterCol_entity, filterCol_entityId, filterCol_time, filterCol_ip, filterCol_desc);

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
                using var workbook = new XLWorkbook();
                var ws = workbook.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("سجل النشاط"));
                ws.RightToLeft = true;

                string[] headers = { "المعرّف", "المستخدم", "نوع العملية", "اسم الكيان", "رقم السجل", "وقت العملية", "الوصف", "عنوان IP", "قبل", "بعد" };
                for (int i = 0; i < headers.Length; i++)
                    ws.Cell(1, i + 1).Value = headers[i];

                int row = 2;
                foreach (var r in data)
                {
                    ws.Cell(row, 1).Value = r.Id;
                    ws.Cell(row, 2).Value = r.User?.DisplayName ?? r.User?.UserName ?? "";
                    ws.Cell(row, 3).Value = r.ActionType.ToString();
                    ws.Cell(row, 4).Value = EntityNameToArabic(r.EntityName);
                    ws.Cell(row, 5).Value = r.EntityId?.ToString() ?? "";
                    ws.Cell(row, 6).Value = r.ActionTime.ToString("yyyy-MM-dd HH:mm:ss");
                    ws.Cell(row, 7).Value = DescriptionToArabic(r.Description);
                    ws.Cell(row, 8).Value = r.IpAddress ?? "";
                    ws.Cell(row, 9).Value = r.OldValues ?? "";
                    ws.Cell(row, 10).Value = r.NewValues ?? "";
                    row++;
                }

                using var stream = new System.IO.MemoryStream();
                workbook.SaveAs(stream);
                var fileName = ExcelExportNaming.ArabicTimestampedFileName("سجل نشاط المستخدمين", ".xlsx");
                return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }

            // CSV
            var sb = new StringBuilder();
            sb.AppendLine("المعرّف,المستخدم,نوع العملية,اسم الكيان,رقم السجل,وقت العملية,الوصف,عنوان IP,المتصفح,قبل,بعد");
            foreach (var r in data)
            {
                string Safe(string? s) => string.IsNullOrEmpty(s) ? "" : "\"" + s.Replace("\"", "\"\"") + "\"";
                sb.AppendLine(string.Join(",",
                    r.Id,
                    Safe(r.User?.UserName),
                    Safe(r.ActionType.ToString()),
                    Safe(EntityNameToArabic(r.EntityName)),
                    r.EntityId?.ToString() ?? "",
                    r.ActionTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    Safe(DescriptionToArabic(r.Description)),
                    Safe(r.IpAddress),
                    Safe(r.UserAgent),
                    Safe(r.OldValues),
                    Safe(r.NewValues)));
            }
            var utf8 = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            var bytes = utf8.GetBytes(sb.ToString());
            return File(bytes, "text/csv; charset=utf-8", ExcelExportNaming.ArabicTimestampedFileName("سجل نشاط المستخدمين", ".csv"));
        }
    }
}
