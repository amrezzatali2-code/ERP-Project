using ClosedXML.Excel;
using ERP.Data;                             // كائن الاتصال بقاعدة البيانات AppDbContext
using ERP.Filters;
using ERP.Infrastructure;                  // كلاس PagedResult لتقسيم الصفحات + الفلاتر
using ERP.Models;                          // الموديل WarehousePolicyRule
using ERP.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;   // علشان SelectList
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;          // القواميس Dictionary
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;             // التعبيرات Expressions
using System.Text;                         // StringBuilder للتصدير
using System.Threading.Tasks;




namespace ERP.Controllers
{
    /// <summary>
    /// إدارة جدول قواعد السياسات على مستوى المخزن (WarehousePolicyRule)
    /// كل صف = سياسة معيّنة (PolicyId) داخل مخزن معيّن (WarehouseId)
    /// وتحدد نسبة ربح المخزن وحدّ الخصم المسموح للعميل.
    /// </summary>
    [RequirePermission(PermissionCodes.Settings.WarehousePolicyRules_View)]
    public class WarehousePolicyRulesController : Controller
    {
        private readonly AppDbContext _context;   // متغير: اتصال بقاعدة البيانات

        public WarehousePolicyRulesController(AppDbContext context)
        {
            _context = context;
        }














        // =========================
        // دالة خاصة لبناء استعلام قواعد السياسات
        // (بحث + فلتر كود من/إلى + فلتر تاريخ اختياري + ترتيب)
        // =========================
        private IQueryable<WarehousePolicyRule> BuildRulesQuery(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            int? fromCode,
            int? toCode,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate)
        {
            // 1) الاستعلام الأساسي (قراءة فقط لتحسين الأداء)
            IQueryable<WarehousePolicyRule> q =
                _context.WarehousePolicyRules.AsNoTracking();

            // 2) فلتر الكود من/إلى (كود القاعدة نفسها Id)
            if (fromCode.HasValue)
                q = q.Where(x => x.Id >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(x => x.Id <= toCode.Value);

            // 3) فلتر التاريخ (على CreatedAt) لو مفعّل
            if (useDateRange)
            {
                if (fromDate.HasValue)
                    q = q.Where(x => x.CreatedAt >= fromDate.Value);

                if (toDate.HasValue)
                    q = q.Where(x => x.CreatedAt <= toDate.Value);
            }

            // 4) بحث مخصص لـ created و profit و maxdisc
            string? searchForSort = search;
            string? searchByForSort = searchBy;
            if (!string.IsNullOrWhiteSpace(search) && !string.IsNullOrWhiteSpace(searchBy))
            {
                var sb = searchBy.Trim().ToLowerInvariant();
                var text = search!.Trim();
                if (sb == "created" && DateTime.TryParse(text, out var dtCreated))
                {
                    q = q.Where(x => x.CreatedAt.Date == dtCreated.Date);
                    searchForSort = null;
                    searchByForSort = null;
                }
                else if (sb == "profit" && decimal.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var profitVal))
                {
                    q = q.Where(x => x.ProfitPercent == profitVal);
                    searchForSort = null;
                    searchByForSort = null;
                }
                else if (sb == "maxdisc" && decimal.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var maxVal))
                {
                    q = q.Where(x => x.MaxDiscountToCustomer.HasValue && x.MaxDiscountToCustomer.Value == maxVal);
                    searchForSort = null;
                    searchByForSort = null;
                }
            }

            // 5) الحقول النصية (فارغ - الموديل عددي فقط)
            var stringFields =
                new Dictionary<string, Expression<Func<WarehousePolicyRule, string?>>>();

            // 6) الحقول العددية للبحث
            var intFields =
                new Dictionary<string, Expression<Func<WarehousePolicyRule, int>>>()
                {
                    ["id"] = x => x.Id,
                    ["warehouse"] = x => x.WarehouseId,
                    ["policy"] = x => x.PolicyId
                };

            // 7) حقول الترتيب
            var orderFields =
                new Dictionary<string, Expression<Func<WarehousePolicyRule, object>>>()
                {
                    ["id"] = x => x.Id,
                    ["warehouse"] = x => x.WarehouseId,
                    ["policy"] = x => x.PolicyId,
                    ["profit"] = x => x.ProfitPercent,
                    ["maxdisc"] = x => x.MaxDiscountToCustomer ?? 0m,
                    ["created"] = x => x.CreatedAt
                };

            // 8) تطبيق البحث + الترتيب باستخدام الإكستنشن الموحد
            q = q.ApplySearchSort(
                searchForSort,
                searchByForSort,
                sort,                      // اسم العمود للترتيب
                dir,                       // asc / desc
                stringFields,
                intFields,
                orderFields,
                defaultSearchBy: "id",     // البحث الافتراضي بالكود
                defaultSortBy: "id"        // الترتيب الافتراضي بالكود
            );

            return q;
        }








        // =========================
        // دالة مساعدة لتحميل قوائم المخازن والسياسات
        // forCreate: عند الإضافة نعرض فقط السياسات التي لم يُحدد ربح لها لهذا المخزن
        // =========================
        private async Task LoadLookupsAsync(int? warehouseId = null, int? policyId = null, bool forCreate = false)
        {
            var warehouses = await _context.Warehouses
                .AsNoTracking()
                .OrderBy(w => w.WarehouseName)
                .ToListAsync();

            ViewBag.WarehouseList = new SelectList(
                warehouses,
                "WarehouseId",
                "WarehouseName",
                warehouseId
            );

            List<Policy> policies;
            if (forCreate && warehouseId.HasValue)
            {
                var usedPolicyIds = await _context.WarehousePolicyRules
                    .AsNoTracking()
                    .Where(r => r.WarehouseId == warehouseId.Value)
                    .Select(r => r.PolicyId)
                    .ToListAsync();
                policies = await _context.Policies
                    .AsNoTracking()
                    .Where(p => !usedPolicyIds.Contains(p.PolicyId))
                    .OrderBy(p => p.Name)
                    .ToListAsync();
            }
            else if (forCreate)
            {
                policies = new List<Policy>();
            }
            else
            {
                policies = await _context.Policies
                    .AsNoTracking()
                    .OrderBy(p => p.Name)
                    .ToListAsync();
            }

            ViewBag.PolicyList = new SelectList(
                policies,
                "PolicyId",
                "Name",
                policyId
            );
        }

        /// <summary>جلب السياسات التي لم يُحدد لها ربح لهذا المخزن (للتعبئة في الإضافة)</summary>
        [HttpGet]
        public async Task<IActionResult> GetPoliciesNotUsedForWarehouse(int warehouseId)
        {
            var usedPolicyIds = await _context.WarehousePolicyRules
                .AsNoTracking()
                .Where(r => r.WarehouseId == warehouseId)
                .Select(r => r.PolicyId)
                .ToListAsync();

            var list = await _context.Policies
                .AsNoTracking()
                .Where(p => !usedPolicyIds.Contains(p.PolicyId))
                .OrderBy(p => p.Name)
                .Select(p => new { id = p.PolicyId, name = p.Name })
                .ToListAsync();

            return Json(list);
        }









        // =========================
        // Index — قائمة قواعد السياسات بالمخازن
        // =========================
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "id",        // id | warehouse | policy
            string? sort = "id",            // id | warehouse | policy | profit | maxdisc | created
            string? dir = "asc",            // asc | desc
            int page = 1,
            int pageSize = 25,
            int? fromCode = null,           // فلتر كود من
            int? toCode = null,             // فلتر كود إلى
            bool useDateRange = false,      // تفعيل فلتر التاريخ
            DateTime? fromDate = null,
            DateTime? toDate = null)
        {
            // بناء الاستعلام طبقاً للفلاتر
            var q = BuildRulesQuery(
                search,
                searchBy,
                sort,
                dir,
                fromCode,
                toCode,
                useDateRange,
                fromDate,
                toDate);

            // تقسيم الصفحات (النظام الثابت)
            var model = await PagedResult<WarehousePolicyRule>.CreateAsync(q, page, pageSize);

            // تعبئة خصائص البحث/الترتيب داخل الموديل (للاستخدام في الواجهة)
            model.Search = search ?? "";
            model.SearchBy = searchBy ?? "id";
            model.SortColumn = sort ?? "id";
            model.SortDescending = (dir?.ToLower() == "desc");
            model.UseDateRange = useDateRange;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            // تمرير فلتر الكود عن طريق ViewBag (مثل فواتير المبيعات)
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.CodeFrom = fromCode;
            ViewBag.CodeTo = toCode;

            // حقل التاريخ المستخدم في الفلترة (للنموذج الموحد)
            ViewBag.DateField = "CreatedAt";

            return View(model);
        }








        // =========================
        // Export — تصدير قواعد السياسات (CSV يفتح في Excel)
        // =========================
        [HttpGet]
        // تصدير قواعد سياسات المخازن
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            int? fromCode = null,
            int? toCode = null,
            int? codeFrom = null,
            int? codeTo = null,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string format = "excel")
        {
            if (!fromCode.HasValue && codeFrom.HasValue) fromCode = codeFrom;
            if (!toCode.HasValue && codeTo.HasValue) toCode = codeTo;

            // 1) بناء الاستعلام بنفس فلاتر الواجهة
            // BuildRulesQuery موجودة عندك بالفعل في الكنترولر
            var query = BuildRulesQuery(
                search,
                searchBy,
                sort,
                dir,
                fromCode,
                toCode,
                useDateRange,
                fromDate,
                toDate);

            // نضيف Include للمخزن والسياسة عشان نجيب الأسماء
            query = query
                .Include(r => r.Warehouse)   // 🔴 لو اسم الـ navigation مختلف عدّله
                .Include(r => r.Policy);     // 🔴 برضه هنا

            // 2) جلب كل النتائج (بدون Paging)
            var list = await query.ToListAsync();

            // 3) لو المطلوب Excel (افتراضي)
            if (string.Equals(format, "excel", StringComparison.OrdinalIgnoreCase))
            {
                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("WarehouseRules");

                int row = 1;

                // عناوين الأعمدة
                worksheet.Cell(row, 1).Value = "كود القاعدة";
                worksheet.Cell(row, 2).Value = "كود المخزن";
                worksheet.Cell(row, 3).Value = "اسم المخزن";
                worksheet.Cell(row, 4).Value = "كود السياسة";
                worksheet.Cell(row, 5).Value = "اسم السياسة";
                worksheet.Cell(row, 6).Value = "نسبة الربح %";
                worksheet.Cell(row, 7).Value = "أقصى خصم للعميل % (اختياري)";
                worksheet.Cell(row, 8).Value = "مفعّلة؟";
                worksheet.Cell(row, 9).Value = "تاريخ الإنشاء";
                worksheet.Cell(row, 10).Value = "آخر تعديل";

                // تنسيق الهيدر
                var header = worksheet.Range(row, 1, row, 10);
                header.Style.Font.Bold = true;

                // كتابة البيانات
                foreach (var r in list)
                {
                    row++;

                    worksheet.Cell(row, 1).Value = r.Id;
                    worksheet.Cell(row, 2).Value = r.WarehouseId;
                    worksheet.Cell(row, 3).Value = r.Warehouse?.WarehouseName ?? "";   // 🔴 عدّل Name لو مختلف
                    worksheet.Cell(row, 4).Value = r.PolicyId;
                    worksheet.Cell(row, 5).Value = r.Policy?.Name ?? "";
                    worksheet.Cell(row, 6).Value = r.ProfitPercent;
                    worksheet.Cell(row, 7).Value = r.MaxDiscountToCustomer;
                    worksheet.Cell(row, 8).Value = r.IsActive ? "مفعّلة" : "موقوفة";
                    worksheet.Cell(row, 9).Value = r.CreatedAt;
                    worksheet.Cell(row, 10).Value = r.UpdatedAt;
                }

                worksheet.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                stream.Position = 0;

                var fileName = $"WarehousePolicyRules_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                const string contentType =
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

                return File(stream.ToArray(), contentType, fileName);
            }
            else
            {
                // 4) حالة CSV
                var sb = new StringBuilder();

                sb.AppendLine("RuleId,WarehouseId,WarehouseName,PolicyId,PolicyName,ProfitPercent,MaxDiscountToCustomer,IsActive,CreatedAt,UpdatedAt");

                foreach (var r in list)
                {
                    string warehouseName = (r.Warehouse?.WarehouseName ?? string.Empty)
                        .Replace("\"", "\"\"");
                    string policyName = (r.Policy?.Name ?? string.Empty)
                        .Replace("\"", "\"\"");

                    string created = r.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                    string updated = r.UpdatedAt.HasValue
                        ? r.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm")
                        : string.Empty;

                    string profitText = r.ProfitPercent
                        .ToString("0.##", CultureInfo.InvariantCulture);

                    string maxDiscountText = r.MaxDiscountToCustomer.HasValue
                        ? r.MaxDiscountToCustomer.Value.ToString("0.##", CultureInfo.InvariantCulture)
                        : string.Empty;

                    sb.AppendLine(
                        $"{r.Id}," +
                        $"{r.WarehouseId}," +
                        $"\"{warehouseName}\"," +
                        $"{r.PolicyId}," +
                        $"\"{policyName}\"," +
                        $"{profitText}," +
                        $"{maxDiscountText}," +
                        $"{(r.IsActive ? 1 : 0)}," +
                        $"{created}," +
                        $"{updated}");
                }

                byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
                string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"WarehousePolicyRules_{timeStamp}.csv";

                return File(bytes, "text/csv", fileName);
            }
        }










        // =========================
        // Details — عرض القاعدة (قراءة فقط)
        // =========================
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
                return NotFound();

            var rule = await _context.WarehousePolicyRules
                                     .AsNoTracking()
                                     .FirstOrDefaultAsync(r => r.Id == id.Value);
            if (rule == null)
                return NotFound();

            return View(rule);
        }








        // =========================
        // Create — GET: شاشة إضافة قاعدة جديدة
        // =========================
        // GET: WarehousePolicyRules/Create
        // =========================
        // Create — GET: شاشة إضافة قاعدة جديدة
        // =========================
        public async Task<IActionResult> Create()
        {
            await LoadLookupsAsync(null, null, forCreate: true);

            var model = new WarehousePolicyRule
            {
                IsActive = true
            };

            return View(model);
        }










        // =========================
        // Create — POST
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(WarehousePolicyRule model)
        {
            // منع تكرار (نفس السياسة + نفس المخزن)
            bool duplicate = await _context.WarehousePolicyRules
                .AnyAsync(r => r.WarehouseId == model.WarehouseId && r.PolicyId == model.PolicyId);
            if (duplicate)
            {
                ModelState.AddModelError(string.Empty, "هذه السياسة تم عمل ربح لها بالفعل لهذا المخزن. اختر سياسة أخرى أو مخزنًا آخر.");
            }

            if (!ModelState.IsValid)
            {
                await LoadLookupsAsync(model.WarehouseId, model.PolicyId, forCreate: true);
                return View(model);
            }

            model.CreatedAt = DateTime.Now;

            _context.WarehousePolicyRules.Add(model);
            await _context.SaveChangesAsync();

            TempData["Msg"] = "تم إضافة قاعدة سياسة للمخزن بنجاح.";
            return RedirectToAction(nameof(Index));
        }












        // =========================
        // =========================
        // Edit — GET
        // =========================
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
                return NotFound();

            var rule = await _context.WarehousePolicyRules
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id.Value);

            if (rule == null)
                return NotFound();

            // تحميل القوائم مع تحديد القيم المختارة
            await LoadLookupsAsync(rule.WarehouseId, rule.PolicyId);

            return View(rule);
        }











        // =========================
        // Edit — POST
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, WarehousePolicyRule model)
        {
            if (id != model.Id)
                return NotFound();

            if (!ModelState.IsValid)
            {
                await LoadLookupsAsync(model.WarehouseId, model.PolicyId);
                return View(model);
            }

            try
            {
                model.UpdatedAt = DateTime.Now;
                _context.Update(model);
                await _context.SaveChangesAsync();

                TempData["Msg"] = "تم تعديل قاعدة سياسة المخزن بنجاح.";
            }
            catch (DbUpdateConcurrencyException)
            {
                bool exists = await _context.WarehousePolicyRules
                    .AnyAsync(r => r.Id == id);

                if (!exists)
                    return NotFound();

                ModelState.AddModelError(string.Empty,
                    "حدث تعارض في التعديل، من فضلك أعد تحميل الصفحة وحاول مرة أخرى.");
                await LoadLookupsAsync(model.WarehouseId, model.PolicyId);
                return View(model);
            }

            return RedirectToAction(nameof(Index));
        }
    









// =========================
// Delete — GET: صفحة تأكيد الحذف
// =========================
public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
                return NotFound();

            var rule = await _context.WarehousePolicyRules
                                     .AsNoTracking()
                                     .FirstOrDefaultAsync(r => r.Id == id.Value);
            if (rule == null)
                return NotFound();

            return View(rule);
        }








        // =========================
        // Delete — POST: حذف قاعدة واحدة
        // =========================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var rule = await _context.WarehousePolicyRules
                                     .FirstOrDefaultAsync(r => r.Id == id);
            if (rule == null)
                return NotFound();

            _context.WarehousePolicyRules.Remove(rule);
            await _context.SaveChangesAsync();

            TempData["Msg"] = "تم حذف القاعدة بنجاح.";
            return RedirectToAction(nameof(Index));
        }









        // =========================
        // BulkDelete — حذف مجموعة قواعد محددة
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["Msg"] = "لم يتم اختيار أي قواعد للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var ids = selectedIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var n) ? (int?)n : null)
                .Where(n => n.HasValue)
                .Select(n => n!.Value)
                .ToList();

            if (!ids.Any())
            {
                TempData["Msg"] = "لم يتم اختيار أكواد صحيحة للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var rules = await _context.WarehousePolicyRules
                                      .Where(r => ids.Contains(r.Id))
                                      .ToListAsync();

            if (!rules.Any())
            {
                TempData["Msg"] = "لم يتم العثور على القواعد المحددة.";
                return RedirectToAction(nameof(Index));
            }

            _context.WarehousePolicyRules.RemoveRange(rules);
            await _context.SaveChangesAsync();

            TempData["Msg"] = $"تم حذف {rules.Count} قاعدة سياسة.";
            return RedirectToAction(nameof(Index));
        }








        // =========================
        // DeleteAll — حذف جميع القواعد (خطير)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var rules = await _context.WarehousePolicyRules.ToListAsync();
            if (!rules.Any())
            {
                TempData["Msg"] = "لا توجد قواعد لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            _context.WarehousePolicyRules.RemoveRange(rules);
            await _context.SaveChangesAsync();

            TempData["Msg"] = $"تم حذف جميع قواعد السياسات ({rules.Count}).";
            return RedirectToAction(nameof(Index));
        }
    }
}
