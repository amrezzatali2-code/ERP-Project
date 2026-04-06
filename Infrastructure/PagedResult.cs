using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure
{
    /// <summary>
    /// نتيجة ترقيم عامة تصلح لأي جدول T
    /// - تحتوي على عناصر الصفحة + معلومات الترقيم
    /// - وتحمل معها حالة البحث والترتيب والتاريخ لعرضها في الـ View
    /// </summary>
    public class PagedResult<T>
    {
        // =============== البيانات الأساسية ===============

        /// <summary>
        /// العناصر المعروضة في الصفحة الحالية
        /// </summary>
        public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

        /// <summary>
        /// رقم الصفحة الحالية (1..N)
        /// </summary>
        public int PageNumber { get; init; }

        /// <summary>
        /// حجم الصفحة (عدد الصفوف في الصفحة الواحدة)
        /// </summary>
        public int PageSize { get; init; }

        /// <summary>
        /// إجمالي عدد السجلات بعد تطبيق كل الفلاتر
        /// </summary>
        public int TotalCount { get; init; }

        // =============== خصائص مساعدة لعرض "من / إلى" ===============

        /// <summary>
        /// أول رقم سجل ظاهر حاليًا فى الجدول
        /// مثال: صفحة 1 => 1 ، صفحة 2 (حجم 25) => 26
        /// لو مفيش بيانات => 0
        /// </summary>
        public int StartIndex
        {
            get
            {
                if (TotalCount == 0 || Items == null || Items.Count == 0)
                    return 0;

                // مثال: صفحة 2، PageSize = 25 => ((2 - 1) * 25) + 1 = 26
                return ((PageNumber < 1 ? 1 : PageNumber) - 1) * (PageSize < 1 ? 1 : PageSize) + 1;
            }
        }

        /// <summary>
        /// آخر رقم سجل ظاهر حاليًا فى الجدول
        /// مثال: صفحة 1 (حجم 25) => 25
        /// صفحة أخيرة بإجمالى 37 => 37
        /// لو مفيش بيانات => 0
        /// </summary>
        public int EndIndex
        {
            get
            {
                if (TotalCount == 0 || Items == null || Items.Count == 0)
                    return 0;

                // آخر رقم متوقع حسب عدد العناصر فى الصفحة الحالية
                var endByItems = StartIndex + Items.Count - 1;

                // ما نتعدّاش الإجمالى الفعلى
                return endByItems > TotalCount ? TotalCount : endByItems;
            }
        }

        // =============== خصائص محسوبة (مع إمكانية التعيين يدويًا) ===============

        private int? _totalPages;

        /// <summary>
        /// إجمالي عدد الصفحات (يُحسب تلقائيًا من TotalCount و PageSize)
        /// </summary>
        public int TotalPages
        {
            get => _totalPages ?? Math.Max(1, (int)Math.Ceiling(TotalCount / (double)(PageSize > 0 ? PageSize : 1)));
            init => _totalPages = value;
        }

        private bool? _hasPrevious;

        /// <summary>
        /// هل هناك صفحة سابقة؟
        /// </summary>
        public bool HasPrevious
        {
            get => _hasPrevious ?? (PageNumber > 1);
            init => _hasPrevious = value;
        }

        private bool? _hasNext;

        /// <summary>
        /// هل هناك صفحة تالية؟
        /// </summary>
        public bool HasNext
        {
            get => _hasNext ?? (PageSize == 0 ? false : (PageNumber * PageSize < TotalCount));
            init => _hasNext = value;
        }

        // =============== حالة البحث والترتيب / التاريخ ===============

        /// <summary>
        /// نص البحث العام (يُستخدم في الـ View لملء صندوق البحث)
        /// </summary>
        public string? Search { get; set; }          // متغير: نص البحث الحالي في الشاشة

        /// <summary>
        /// اسم الحقل المستخدم في البحث (مثلاً name / code / level)
        /// </summary>
        public string? SearchBy { get; set; }        // متغير: حقل البحث الحالي

        /// <summary>
        /// اسم العمود المستخدم في الترتيب الحالي (اختياري)
        /// </summary>
        public string? SortColumn { get; set; }      // متغير: عمود الترتيب الحالي

        /// <summary>
        /// هل الترتيب الحالي تنازلي؟
        /// </summary>
        public bool SortDescending { get; set; }     // متغير: هل الترتيب Desc

        /// <summary>
        /// هل نطبّق فلتر التاريخ؟
        /// (نربطه بالـ CheckBox في شاشة البحث)
        /// </summary>
        public bool UseDateRange { get; set; }       // متغير: هل تم تفعيل فلتر التاريخ

        /// <summary>
        /// بداية الفترة الزمنية (من تاريخ/وقت)
        /// </summary>
        public DateTime? FromDate { get; set; }      // متغير: تاريخ البداية

        /// <summary>
        /// نهاية الفترة الزمنية (إلى تاريخ/وقت)
        /// </summary>
        public DateTime? ToDate { get; set; }        // متغير: تاريخ النهاية

        // =============== دوال مساعدة للترتيب من الهيدر ===============

        /// <summary>
        /// هل العمود الحالي هو الذي يُرتَّب به فعلاً؟
        /// </summary>
        public bool IsSortedBy(string column)
        {
            if (string.IsNullOrWhiteSpace(column) || string.IsNullOrWhiteSpace(SortColumn))
                return false;

            return string.Equals(SortColumn, column, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// عند الضغط على عنوان عمود: ما هي اتجاه الترتيب القادم (asc/desc)؟
        /// </summary>
        public string GetNextSortDir(string column)
        {
            // لو كنت مرتّب بالفعل على نفس العمود تصاعدي ⇒ المرة الجاية يبقى تنازلي
            if (IsSortedBy(column) && !SortDescending)
                return "desc";

            // غير كده ⇒ نبدأ بتصاعدي
            return "asc";
        }

        /// <summary>
        /// رمز السهم الصغير الذي يظهر بجوار عنوان العمود
        /// </summary>
        public string GetSortIcon(string column)
        {
            if (!IsSortedBy(column))
                return string.Empty;

            return SortDescending ? "▼" : "▲";
        }

        // =============== البنّاءان ===============

        /// <summary>
        /// بنّاء فارغ مطلوب للـ Razor و الـ object initializer
        /// </summary>
        public PagedResult()
        {
        }

        /// <summary>
        /// بنّاء موحّد: يضبط الأساسيات ويحسب عدد الصفحات داخليًا
        /// </summary>
        /// <param name="items">عناصر الصفحة الحالية بعد Skip/Take</param>
        /// <param name="pageNumber">رقم الصفحة الحالية (1..N)</param>
        /// <param name="pageSize">حجم الصفحة</param>
        /// <param name="totalCount">إجمالى السجلات بعد الفلاتر (كل الجدول)</param>
        public PagedResult(IEnumerable<T>? items, int pageNumber, int pageSize, int totalCount)
        {
            // تجهيز عناصر الصفحة الحالية
            Items = (items ?? Enumerable.Empty<T>()).ToList();

            // ضبط القيم مع حماية من الأرقام غير المنطقية (pageSize=0 يعني «الكل» → صفحة واحدة)
            PageNumber = pageNumber < 1 ? 1 : pageNumber;
            PageSize = pageSize < 0 ? 10 : pageSize;  // السماح بـ 0 للعرض «الكل»
            TotalCount = totalCount < 0 ? 0 : totalCount;

            // حساب عدد الصفحات: عند 0 = صفحة واحدة، وإلا بالمعتاد
            _totalPages = PageSize == 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
        }

        // =============== الدالة الأساسية (3 بارامترات + ct) ===============

        /// <summary>
        /// إنشاء نتيجة ترقيم من استعلام LINQ:
        /// - يحسب إجمالى السجلات
        /// - يطبق Skip/Take
        /// - يرجع PagedResult فيه كل المعلومات
        /// </summary>
        public static async Task<PagedResult<T>> CreateAsync(
            IQueryable<T> q, int page, int pageSize, CancellationToken ct = default)
        {
            if (page < 1) page = 1;
            if (pageSize < 0) pageSize = 10;
            if (pageSize == 0)
            {
                var totalAll = await q.CountAsync(ct);
                var itemsAll = totalAll == 0
                    ? new List<T>()
                    : await q.Take(Math.Min(totalAll, 100_000)).ToListAsync(ct);

                return new PagedResult<T>(itemsAll, 1, 0, totalAll);
            }

            // إجمالى السجلات بعد الفلاتر
            var total = await q.CountAsync(ct);

            // لو مفيش بيانات
            if (total == 0)
            {
                return new PagedResult<T>(new List<T>(), 1, pageSize, 0);
                //                           ↑ صفحة 1      ↑ حجم الصفحة  ↑ إجمالي = 0
            }

            var skip = (page - 1) * pageSize;

            // لو المستخدم طلب صفحة أكبر من عدد الصفحات الموجودة
            if (skip >= total)
            {
                page = Math.Max(1, (int)Math.Ceiling((double)total / pageSize));
                skip = (page - 1) * pageSize;
            }

            // عناصر الصفحة الحالية
            var items = await q.Skip(skip).Take(pageSize).ToListAsync(ct);

            // انتبه لترتيب البراميترز: (items, pageNumber, pageSize, totalCount)
            return new PagedResult<T>(items, page, pageSize, total);
        }

        // =============== أوفرلود جديد (7 بارامترات) ===============

        /// <summary>
        /// إنشاء نتيجة ترقيم مع حفظ حالة الترتيب والبحث
        /// (يُستخدم في الشاشات اللى فيها فلاتر: search / sort / searchBy).
        /// </summary>
        public static async Task<PagedResult<T>> CreateAsync(
            IQueryable<T> q,          // الاستعلام بعد الفلاتر
            int page,                 // رقم الصفحة
            int pageSize,             // حجم الصفحة
            string? sortColumn,       // عمود الترتيب الحالي
            bool sortDescending,      // هل الترتيب Desc؟
            string? search,           // نص البحث الحالي
            string? searchBy,         // حقل البحث الحالي
            CancellationToken ct = default)
        {
            // نستخدم الدالة الأساسية علشان نحسب Items + PageNumber + PageSize + TotalCount
            var result = await CreateAsync(q, page, pageSize, ct);

            // نخزن حالة الفلاتر علشان نعرضها في الـ View
            result.SortColumn = sortColumn;
            result.SortDescending = sortDescending;
            result.Search = search;
            result.SearchBy = searchBy;

            return result;
        }
    }
}
