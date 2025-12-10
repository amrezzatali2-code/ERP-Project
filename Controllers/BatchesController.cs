using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using ERP.Data;
using ERP.Infrastructure;                 // ApplySearchSort
using ERP.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ERP.Controllers
{
    /// <summary>
    /// تقرير تشغيلات (تعريف التشغيلة) — عرض فقط (قراءة).
    /// </summary>
    public class BatchesController : Controller
    {
        private readonly AppDbContext context;   // متغير: سياق قاعدة البيانات الرئيسي

        public BatchesController(AppDbContext ctx) => context = ctx;

        // GET: /Batches
        public async Task<IActionResult> Index(
            string? search,                  // متغير: نص البحث الذي يكتبه المستخدم
            string? searchBy = "all",        // متغير: العمود الذي سيتم البحث فيه
            string? sort = "Product",        // متغير: اسم عمود الترتيب الافتراضي
            string? dir = "asc",             // متغير: اتجاه الترتيب (تصاعدي/تنازلي)
            int page = 1,                    // متغير: رقم الصفحة الحالية
            int pageSize = 50)               // متغير: عدد السطور في كل صفحة
        {
            // 1) الاستعلام الأساسي من جدول التشغيلات بدون تتبّع (لزيادة سرعة القراءة في التقارير)
            IQueryable<Batch> q = context.Batches.AsNoTracking();

            // 2) مفاتيح البحث النصي (string) — هنا التشغيلة فقط
            var stringFields = new Dictionary<string, Expression<Func<Batch, string?>>>()
            {
                ["batch"] = x => x.BatchNo          // تعليق: البحث بالنص داخل رقم/كود التشغيلة
            };

            // 3) مفاتيح البحث الرقمي (int) — الصنف والعميل
            var intFields = new Dictionary<string, Expression<Func<Batch, int>>>()
            {
                ["product"] = x => x.ProdId,        // تعليق: البحث برقم الصنف (ProductId)
                // لو CustomerId عندك int? (nullable) نستخدم ?? 0 لتحويلها لـ int
                ["customer"] = x => x.CustomerId ?? 0   // تعليق: البحث برقم العميل (CustomerId كمرجع لمن اشتريت منه/له)
            };

            // 4) مفاتيح الترتيب المسموحة (Sorting)
            var orderFields = new Dictionary<string, Expression<Func<Batch, object>>>()
            {
                ["Product"] = x => x.ProdId,                           // ترتيب حسب رقم الصنف
                ["Batch"] = x => x.BatchNo!,                            // ترتيب حسب كود التشغيلة
                ["Expiry"] = x => x.Expiry,                              // ترتيب حسب تاريخ الصلاحية
                ["Retail"] = x => x.PriceRetailBatch ?? 0m,              // ترتيب حسب سعر الجمهور لهذه التشغيلة
                ["Cost"] = x => x.UnitCostDefault ?? 0m,               // ترتيب حسب التكلفة الافتراضية
                ["EntryDate"] = x => x.EntryDate                            // ترتيب حسب تاريخ الإدخال للنظام
            };

            // 5) تطبيق منظومة البحث/الترتيب الموحّدة
            q = q.ApplySearchSort(
                    search, searchBy,
                    sort, dir,
                    stringFields, intFields, orderFields,
                    defaultSearchBy: "all",   // تعليق: لو المستخدم لم يحدد عمود بحث، نبحث في الكل
                    defaultSortBy: "Product"  // تعليق: الترتيب الافتراضي حسب الصنف
                );

            // 6) ترقيم بسيط (Paging)
            var totalRows = await q.CountAsync();                              // إجمالي عدد السطور
            var totalPages = (int)Math.Ceiling(totalRows / (double)pageSize);  // إجمالي عدد الصفحات
            page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));       // ضمان أن رقم الصفحة داخل الحدود

            var rows = await q
                .Skip((page - 1) * pageSize)   // تخطي السطور السابقة
                .Take(pageSize)                // أخذ عدد السطور المطلوبة للصفحة الحالية
                .ToListAsync();

            // 7) خيارات البحث لعرضها في الكمبو بوكس في البارشال
            ViewBag.SearchOptions = new List<SelectListItem>
            {
                new("الكل",      "all"){ Selected = (searchBy ?? "all").Equals("all", StringComparison.OrdinalIgnoreCase) },
                new("الصنف",     "product"),
                new("التشغيلة",  "batch"),
                new("العميل",    "customer")
            };

            // 8) خيارات الترتيب
            ViewBag.SortOptions = new List<SelectListItem>
            {
                new("الصنف",                 "Product"),
                new("التشغيلة",             "Batch"),
                new("الصلاحية",             "Expiry"),
                new("سعر الجمهور (تشغيلة)", "Retail"),
                new("التكلفة الافتراضية",   "Cost"),
                new("تاريخ الإدخال",        "EntryDate"),
            };

            // 9) تمرير حالة الفلاتر والصفحات للـ View (الفوتر والبارشال)
            ViewBag.Search = search ?? "";
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.Sort = sort ?? "Product";
            ViewBag.Dir = (dir?.ToLower() == "asc") ? "asc" : "desc";

            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalPages = totalPages;
            ViewBag.RangeStart = totalRows == 0 ? 0 : ((page - 1) * pageSize) + 1;
            ViewBag.RangeEnd = Math.Min(page * pageSize, totalRows);
            ViewBag.TotalRows = totalRows;

            // 10) إرجاع النتيجة للـ View على شكل قائمة تشغيلات
            return View(rows);
        }
    }
}
