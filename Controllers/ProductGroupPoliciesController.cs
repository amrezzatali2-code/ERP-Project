using ClosedXML.Excel;                      // متغيرات Excel (ClosedXML)
using ERP.Data;                             // كائن الاتصال بقاعدة البيانات AppDbContext
using ERP.Infrastructure;                   // كلاس PagedResult + ApplySearchSort
using ERP.Models;                           // الموديل ProductGroupPolicy
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;   // SelectList للقوائم المنسدلة
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;           // Dictionary
using System.Globalization;                 // CultureInfo لتنسيق الأرقام في CSV
using System.IO;                            // MemoryStream
using System.Linq;
using System.Linq.Expressions;              // Expressions
using System.Text;                          // StringBuilder للتصدير
using System.Threading.Tasks;

namespace ERP.Controllers
{
    /// <summary>
    /// إدارة جدول سياسات مجموعات الأصناف (ProductGroupPolicy)
    /// كل صف = سياسة معينة تطبَّق على مجموعة أصناف محددة داخل مخزن معيّن،
    /// مع تحديد أقصى خصم للعميل وإمكانية تفعيل/إيقاف القاعدة.
    /// </summary>
    public class ProductGroupPoliciesController : Controller
    {
        private readonly AppDbContext _context;   // متغير: اتصال بقاعدة البيانات

        public ProductGroupPoliciesController(AppDbContext context)
        {
            _context = context;
        }

        // =========================
        // دالة مساعدة لتحميل قوائم:
        // - مجموعات الأصناف
        // - السياسات
        // - المخازن
        // تُستخدم في Create و Edit
        // =========================
        private async Task LoadLookupsAsync(
            int? productGroupId = null,
            int? policyId = null,
            int? warehouseId = null)
        {
            // جلب مجموعات الأصناف بالاسم
            var groups = await _context.ProductGroups
                .AsNoTracking()
                .OrderBy(g => g.Name)          // لو اسم العمود مختلف عدّله هنا
                .ToListAsync();

            ViewBag.ProductGroupList = new SelectList(
                groups,
                "ProductGroupId",              // المفتاح في جدول المجموعات
                "Name",                        // اسم المجموعة الظاهر في القائمة
                productGroupId                 // المجموعة المختارة حاليًا (للـ Edit)
            );

            // جلب السياسات بالاسم
            var policies = await _context.Policies
                .AsNoTracking()
                .OrderBy(p => p.Name)
                .ToListAsync();

            ViewBag.PolicyList = new SelectList(
                policies,
                "PolicyId",                    // كود السياسة
                "Name",                        // اسم السياسة
                policyId
            );

            // جلب المخازن بالاسم
            var warehouses = await _context.Warehouses
                .AsNoTracking()
                .OrderBy(w => w.WarehouseName) // لو عندك اسم مختلف عدّله (مثلاً Name)
                .ToListAsync();

            ViewBag.WarehouseList = new SelectList(
                warehouses,
                "WarehouseId",                 // كود المخزن
                "WarehouseName",               // اسم المخزن الظاهر
                warehouseId
            );
        }

        // =========================
        // دالة خاصة لبناء استعلام سياسات المجموعات
        // (بحث + فلتر كود من/إلى + فلتر تاريخ اختياري + ترتيب)
        // =========================
        private IQueryable<ProductGroupPolicy> BuildPoliciesQuery(
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
            IQueryable<ProductGroupPolicy> q =
                _context.ProductGroupPolicies.AsNoTracking();

            // 2) فلتر الكود من/إلى (على Id = كود القاعدة)
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

            // 4) الحقول النصية للبحث (مثلاً حالة القاعدة Active/Inactive)
            var stringFields =
                new Dictionary<string, Expression<Func<ProductGroupPolicy, string?>>>()
                {
                    ["status"] = x => x.IsActive ? "active" : "inactive"
                };

            // 5) الحقول العددية للبحث (نص البحث رقم)
            var intFields =
                new Dictionary<string, Expression<Func<ProductGroupPolicy, int>>>()
                {
                    ["id"] = x => x.Id,             // كود القاعدة
                    ["group"] = x => x.ProductGroupId, // كود مجموعة الأصناف
                    ["policy"] = x => x.PolicyId,       // كود السياسة
                    ["warehouse"] = x => x.WarehouseId     // كود المخزن
                };

            // 6) حقول الترتيب في رأس الجدول
            var orderFields =
                new Dictionary<string, Expression<Func<ProductGroupPolicy, object>>>()
                {
                    ["id"] = x => x.Id,
                    ["group"] = x => x.ProductGroupId,
                    ["policy"] = x => x.PolicyId,
                    ["warehouse"] = x => x.WarehouseId,
                    ["active"] = x => x.IsActive,
                    ["created"] = x => x.CreatedAt
                };

            // 7) تطبيق البحث + الترتيب باستخدام الإكستنشن الموحد
            q = q.ApplySearchSort(
                search,                    // نص البحث
                searchBy,                  // نوع البحث
                sort,                      // عمود الترتيب
                dir,                       // اتجاه الترتيب asc/desc
                stringFields,
                intFields,
                orderFields,
                defaultSearchBy: "id",     // افتراضياً نبحث بالكود
                defaultSortBy: "id"        // وافتراضياً نرتّب بالكود
            );

            return q;
        }

        // =========================
        // Index — قائمة سياسات مجموعات الأصناف
        // =========================
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "id",        // id | group | policy | warehouse | status
            string? sort = "id",            // id | group | policy | warehouse | active | created
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
            var q = BuildPoliciesQuery(
                search,
                searchBy,
                sort,
                dir,
                fromCode,
                toCode,
                useDateRange,
                fromDate,
                toDate);

            // تقسيم الصفحات
            var model = await PagedResult<ProductGroupPolicy>.CreateAsync(q, page, pageSize);

            // تعبئة خصائص البحث/الترتيب داخل الموديل (للاستخدام في الواجهة)
            model.Search = search ?? "";
            model.SearchBy = searchBy ?? "id";
            model.SortColumn = sort ?? "id";
            model.SortDescending = (dir?.ToLower() == "desc");
            model.UseDateRange = useDateRange;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            // تمرير فلتر الكود عن طريق ViewBag (مثل الجداول السابقة)
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.CodeFrom = fromCode;
            ViewBag.CodeTo = toCode;

            // حقل التاريخ المستخدم في الفلترة (للنموذج الموحد)
            ViewBag.DateField = "CreatedAt";

            return View(model);
        }

        // =========================
        // Export — تصدير سياسات المجموعات
        // يدعم:
        // - Excel (ملف .xlsx)
        // - CSV
        // مع إظهار أسماء المجموعة/السياسة/المخزن
        // =========================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy,
            string? sort,
            string? dir,
            int? fromCode = null,           // fromCode للاسم الجديد
            int? toCode = null,
            int? codeFrom = null,           // دعم الأسماء القديمة codeFrom/codeTo لو موجودة في الواجهة
            int? codeTo = null,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string format = "excel")        // excel | csv
        {
            // توحيد الأسماء (لو من الواجهة القديمة)
            if (!fromCode.HasValue && codeFrom.HasValue)
                fromCode = codeFrom;

            if (!toCode.HasValue && codeTo.HasValue)
                toCode = codeTo;

            // 1) بناء الاستعلام بنفس فلاتر الواجهة
            var query = BuildPoliciesQuery(
                search,
                searchBy,
                sort,
                dir,
                fromCode,
                toCode,
                useDateRange,
                fromDate,
                toDate);

            // Include للمجموعة والسياسة والمخزن عشان نجيب الأسماء
            query = query
                .Include(r => r.ProductGroup)
                .Include(r => r.Policy)
                .Include(r => r.Warehouse);

            // 2) جلب كل النتائج (بدون Paging)
            var list = await query.ToListAsync();

            // 3) لو المطلوب Excel (افتراضي)
            if (string.Equals(format, "excel", StringComparison.OrdinalIgnoreCase))
            {
                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("GroupPolicies");

                int row = 1;

                // عناوين الأعمدة
                worksheet.Cell(row, 1).Value = "كود القاعدة";
                worksheet.Cell(row, 2).Value = "كود مجموعة الأصناف";
                worksheet.Cell(row, 3).Value = "اسم مجموعة الأصناف";
                worksheet.Cell(row, 4).Value = "كود السياسة";
                worksheet.Cell(row, 5).Value = "اسم السياسة";
                worksheet.Cell(row, 6).Value = "كود المخزن";
                worksheet.Cell(row, 7).Value = "اسم المخزن";
                worksheet.Cell(row, 8).Value = "أقصى خصم للعميل %";
                worksheet.Cell(row, 9).Value = "مفعّلة؟";
                worksheet.Cell(row, 10).Value = "تاريخ الإنشاء";
                worksheet.Cell(row, 11).Value = "آخر تعديل";

                // تنسيق الهيدر
                var header = worksheet.Range(row, 1, row, 11);
                header.Style.Font.Bold = true;

                // كتابة البيانات
                foreach (var r in list)
                {
                    row++;

                    worksheet.Cell(row, 1).Value = r.Id;
                    worksheet.Cell(row, 2).Value = r.ProductGroupId;
                    worksheet.Cell(row, 3).Value = r.ProductGroup?.Name ?? "";          // اسم المجموعة
                    worksheet.Cell(row, 4).Value = r.PolicyId;
                    worksheet.Cell(row, 5).Value = r.Policy?.Name ?? "";                 // اسم السياسة
                    worksheet.Cell(row, 6).Value = r.WarehouseId;
                    worksheet.Cell(row, 7).Value = r.Warehouse?.WarehouseName ?? "";     // اسم المخزن
                    worksheet.Cell(row, 8).Value = r.MaxDiscountToCustomer;              // أقصى خصم
                    worksheet.Cell(row, 9).Value = r.IsActive ? "مفعّلة" : "موقوفة";
                    worksheet.Cell(row, 10).Value = r.CreatedAt;
                    worksheet.Cell(row, 11).Value = r.UpdatedAt;
                }

                worksheet.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                stream.Position = 0;

                var fileName = $"ProductGroupPolicies_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                const string contentType =
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

                return File(stream.ToArray(), contentType, fileName);
            }
            else
            {
                // 4) حالة CSV
                var sb = new StringBuilder();

                sb.AppendLine("Id,ProductGroupId,ProductGroupName,PolicyId,PolicyName,WarehouseId,WarehouseName,MaxDiscountToCustomer,IsActive,CreatedAt,UpdatedAt");

                foreach (var r in list)
                {
                    string groupName = (r.ProductGroup?.Name ?? string.Empty)
                        .Replace("\"", "\"\"");
                    string policyName = (r.Policy?.Name ?? string.Empty)
                        .Replace("\"", "\"\"");
                    string warehouseName = (r.Warehouse?.WarehouseName ?? string.Empty)
                        .Replace("\"", "\"\"");

                    string created = r.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                    string updated = r.UpdatedAt.HasValue
                        ? r.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm")
                        : string.Empty;

                    string maxDiscountText = r.MaxDiscountToCustomer.HasValue
                        ? r.MaxDiscountToCustomer.Value.ToString("0.##", CultureInfo.InvariantCulture)
                        : string.Empty;

                    sb.AppendLine(
                        $"{r.Id}," +
                        $"{r.ProductGroupId}," +
                        $"\"{groupName}\"," +
                        $"{r.PolicyId}," +
                        $"\"{policyName}\"," +
                        $"{r.WarehouseId}," +
                        $"\"{warehouseName}\"," +
                        $"{maxDiscountText}," +
                        $"{(r.IsActive ? 1 : 0)}," +
                        $"{created}," +
                        $"{updated}");
                }

                byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
                string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"ProductGroupPolicies_{timeStamp}.csv";

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

            var policy = await _context.ProductGroupPolicies
                                       .AsNoTracking()
                                       .FirstOrDefaultAsync(p => p.Id == id.Value);
            if (policy == null)
                return NotFound();

            return View(policy);
        }

        // =========================
        // Create — GET: شاشة إضافة قاعدة جديدة
        // =========================
        public async Task<IActionResult> Create()
        {
            // تحميل القوائم (مجموعات + سياسات + مخازن)
            await LoadLookupsAsync();

            // القاعدة مفعّلة افتراضيًا
            var model = new ProductGroupPolicy
            {
                IsActive = true
            };

            return View(model);
        }

        // =========================
        // Create — POST: حفظ القاعدة الجديدة
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ProductGroupPolicy policy)
        {
            // تحقق بسيط على الأكواد
            if (policy.ProductGroupId <= 0)
            {
                ModelState.AddModelError(nameof(ProductGroupPolicy.ProductGroupId),
                    "يجب اختيار مجموعة أصناف صحيحة.");
            }

            if (policy.PolicyId <= 0)
            {
                ModelState.AddModelError(nameof(ProductGroupPolicy.PolicyId),
                    "يجب اختيار سياسة صحيحة.");
            }

            if (policy.WarehouseId <= 0)
            {
                ModelState.AddModelError(nameof(ProductGroupPolicy.WarehouseId),
                    "يجب اختيار مخزن صحيح.");
            }

            if (!ModelState.IsValid)
            {
                // إعادة تحميل القوائم عند وجود أخطاء
                await LoadLookupsAsync(policy.ProductGroupId, policy.PolicyId, policy.WarehouseId);
                return View(policy);
            }

            policy.CreatedAt = DateTime.Now;   // تثبيت تاريخ الإنشاء

            _context.ProductGroupPolicies.Add(policy);
            await _context.SaveChangesAsync();

            TempData["Msg"] = "تم إضافة سياسة لمجموعة الأصناف بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // Edit — GET: فتح القاعدة للتعديل
        // =========================
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
                return NotFound();

            var policy = await _context.ProductGroupPolicies
                                       .AsNoTracking()
                                       .FirstOrDefaultAsync(p => p.Id == id.Value);
            if (policy == null)
                return NotFound();

            // تحميل القوائم مع تحديد القيم المختارة
            await LoadLookupsAsync(policy.ProductGroupId, policy.PolicyId, policy.WarehouseId);

            return View(policy);
        }

        // =========================
        // Edit — POST: حفظ التعديل
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ProductGroupPolicy policy)
        {
            if (id != policy.Id)
                return NotFound();

            if (policy.ProductGroupId <= 0)
            {
                ModelState.AddModelError(nameof(ProductGroupPolicy.ProductGroupId),
                    "يجب اختيار مجموعة أصناف صحيحة.");
            }

            if (policy.PolicyId <= 0)
            {
                ModelState.AddModelError(nameof(ProductGroupPolicy.PolicyId),
                    "يجب اختيار سياسة صحيحة.");
            }

            if (policy.WarehouseId <= 0)
            {
                ModelState.AddModelError(nameof(ProductGroupPolicy.WarehouseId),
                    "يجب اختيار مخزن صحيح.");
            }

            if (!ModelState.IsValid)
            {
                await LoadLookupsAsync(policy.ProductGroupId, policy.PolicyId, policy.WarehouseId);
                return View(policy);
            }

            try
            {
                policy.UpdatedAt = DateTime.Now;    // آخر تعديل
                _context.Update(policy);
                await _context.SaveChangesAsync();

                TempData["Msg"] = "تم تعديل سياسة مجموعة الأصناف بنجاح.";
            }
            catch (DbUpdateConcurrencyException)
            {
                bool exists = await _context.ProductGroupPolicies
                                            .AnyAsync(e => e.Id == id);
                if (!exists)
                    return NotFound();

                ModelState.AddModelError(
                    string.Empty,
                    "تعذّر الحفظ بسبب تعارض في التعديل. أعد تحميل الصفحة وحاول مرة أخرى.");

                await LoadLookupsAsync(policy.ProductGroupId, policy.PolicyId, policy.WarehouseId);
                return View(policy);
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

            var policy = await _context.ProductGroupPolicies
                                       .AsNoTracking()
                                       .FirstOrDefaultAsync(p => p.Id == id.Value);
            if (policy == null)
                return NotFound();

            return View(policy);
        }

        // =========================
        // Delete — POST: حذف قاعدة واحدة
        // =========================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var policy = await _context.ProductGroupPolicies
                                       .FirstOrDefaultAsync(p => p.Id == id);
            if (policy == null)
                return NotFound();

            _context.ProductGroupPolicies.Remove(policy);
            await _context.SaveChangesAsync();

            TempData["Msg"] = "تم حذف سياسة مجموعة الأصناف.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // BulkDelete — حذف مجموعة سياسات محددة
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string? selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
            {
                TempData["Msg"] = "لم يتم اختيار أي سياسات للحذف.";
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

            var policies = await _context.ProductGroupPolicies
                                         .Where(p => ids.Contains(p.Id))
                                         .ToListAsync();

            if (!policies.Any())
            {
                TempData["Msg"] = "لم يتم العثور على السياسات المحددة.";
                return RedirectToAction(nameof(Index));
            }

            _context.ProductGroupPolicies.RemoveRange(policies);
            await _context.SaveChangesAsync();

            TempData["Msg"] = $"تم حذف {policies.Count} سياسة لمجموعات الأصناف.";
            return RedirectToAction(nameof(Index));
        }

        // =========================
        // DeleteAll — حذف جميع السياسات (خطير)
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var policies = await _context.ProductGroupPolicies.ToListAsync();
            if (!policies.Any())
            {
                TempData["Msg"] = "لا توجد سياسات لحذفها.";
                return RedirectToAction(nameof(Index));
            }

            _context.ProductGroupPolicies.RemoveRange(policies);
            await _context.SaveChangesAsync();

            TempData["Msg"] = $"تم حذف جميع سياسات مجموعات الأصناف ({policies.Count}).";
            return RedirectToAction(nameof(Index));
        }
    }
}
