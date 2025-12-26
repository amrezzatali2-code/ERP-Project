using System;
using System.Collections.Generic;                    // القوائم List
using System.Linq;                                   // استعلامات LINQ
using System.Text;                                   // بناء ملف CSV
using System.Threading.Tasks;                        // async / await
using Microsoft.AspNetCore.Mvc;                      // الكنترولر و IActionResult
using Microsoft.AspNetCore.Mvc.Rendering;            // SelectListItem
using Microsoft.EntityFrameworkCore;                 // AsNoTracking / ToListAsync
using ERP.Data;                                      // AppDbContext
using ERP.Infrastructure;                            // PagedResult
using ERP.Models;                                    // الموديلات (Account)

namespace ERP.Controllers
{
    public class AccountsController : Controller
    {
        private readonly AppDbContext _context;      // متغير: كائن الداتا بيز

        public AccountsController(AppDbContext context)
        {
            _context = context;
        }

        // =========================================================
        // دالة مساعدة: ملء القوائم المنسدلة في فورم إضافة/تعديل حساب
        // =========================================================
        private void FillAccountDropdowns(Account? model = null)
        {
            // 1) قائمة نوع الحساب (Asset / Liability / ... من enum)
            var types = Enum.GetValues(typeof(AccountType));
            var typeItems = new List<SelectListItem>();    // متغير: عناصر نوع الحساب

            foreach (AccountType at in types)
            {
                typeItems.Add(new SelectListItem
                {
                    Value = ((int)at).ToString(),          // القيمة المخزنة في الداتابيز
                    Text = at.ToString(),                  // الاسم الظاهر (ممكن نعربه لاحقاً)
                    Selected = (model != null && model.AccountType == at)
                });
            }

            ViewBag.AccountTypes = typeItems;              // نرسلها للفيو

            // 2) قائمة الحسابات الأب (الحسابات غير التفصيلية فقط)
            var parents = _context.Accounts
                .AsNoTracking()
                .Where(a => !a.IsLeaf)                     // لا تُسجَّل عليه حركات
                .OrderBy(a => a.AccountCode)
                .Select(a => new
                {
                    a.AccountId,
                    Display = a.AccountCode + " - " + a.AccountName
                })
                .ToList();

            ViewBag.ParentAccounts = new SelectList(
                parents,
                "AccountId",
                "Display",
                model?.ParentAccountId
            );
        }






        // =========================================================
        // GET: قائمة الحسابات (نظام القوائم الموحد)
        // =========================================================
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",          // all|name|code|id|type|notes|level
            string? sort = "name",             // name|code|id|type|level|created|updated
            string? dir = "asc",               // asc | desc
            bool useDateRange = false,         // فلترة بالتاريخ؟
            DateTime? fromDate = null,         // من تاريخ
            DateTime? toDate = null,           // إلى تاريخ
            string? dateField = "CreatedAt",   // CreatedAt أو UpdatedAt
            int? fromCode = null,              // من رقم حساب (AccountId)
            int? toCode = null,                // إلى رقم حساب
            int page = 1,                      // رقم الصفحة
            int pageSize = 50                  // حجم الصفحة
        )
        {
            // استعلام أساسي بدون تتبع لتحسين الأداء
            IQueryable<Account> q = _context.Accounts.AsNoTracking();

            // تنظيف قيم البحث والترتيب
            var s = (search ?? string.Empty).Trim();                 // نص البحث
            var sb = (searchBy ?? "all").Trim().ToLowerInvariant(); // نوع البحث
            var so = (sort ?? "name").Trim().ToLowerInvariant();    // عمود الترتيب
            bool descending = string.Equals(dir, "desc",
                StringComparison.OrdinalIgnoreCase);                // ترتيب تنازلي؟

            // ============= 1) فلترة من/إلى رقم الحساب (AccountId) =============
            if (fromCode.HasValue)
            {
                int fc = fromCode.Value;                            // متغير: من كود
                q = q.Where(a => a.AccountId >= fc);
            }

            if (toCode.HasValue)
            {
                int tc = toCode.Value;                              // متغير: إلى كود
                q = q.Where(a => a.AccountId <= tc);
            }

            // ============= 2) فلترة بالتاريخ (CreatedAt / UpdatedAt) ===========
            if (useDateRange)
            {
                if (fromDate.HasValue)
                {
                    DateTime f = fromDate.Value;                    // متغير: من تاريخ
                    if (string.Equals(dateField, "UpdatedAt",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        q = q.Where(a => a.UpdatedAt >= f);
                    }
                    else
                    {
                        q = q.Where(a => a.CreatedAt >= f);
                    }
                }

                if (toDate.HasValue)
                {
                    DateTime t = toDate.Value;                      // متغير: إلى تاريخ
                    if (string.Equals(dateField, "UpdatedAt",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        q = q.Where(a => a.UpdatedAt <= t);
                    }
                    else
                    {
                        q = q.Where(a => a.CreatedAt <= t);
                    }
                }
            }

            // ============= 3) البحث حسب نوع الحقل =============================
            if (!string.IsNullOrWhiteSpace(s))
            {
                switch (sb)
                {
                    case "name":    // اسم الحساب
                        q = q.Where(a => a.AccountName.Contains(s));
                        break;

                    case "code":    // كود الحساب
                        q = q.Where(a => a.AccountCode.Contains(s));
                        break;

                    case "id":      // رقم الحساب (AccountId)
                        if (int.TryParse(s, out int idVal))
                        {
                            q = q.Where(a => a.AccountId == idVal);
                        }
                        else
                        {
                            q = q.Where(a => a.AccountId.ToString().Contains(s));
                        }
                        break;

                    case "type":    // نوع الحساب (Asset / Liability ...)
                        q = q.Where(a => a.AccountType.ToString().Contains(s));
                        break;

                    case "notes":   // الملاحظات
                        q = q.Where(a => a.Notes != null && a.Notes.Contains(s));
                        break;

                    case "level":   // مستوى الحساب
                        if (int.TryParse(s, out int lvlVal))
                        {
                            q = q.Where(a => a.Level == lvlVal);
                        }
                        else
                        {
                            q = q.Where(a => a.Level.ToString().Contains(s));
                        }
                        break;

                    case "all":
                    default:        // البحث في الكل
                        q = q.Where(a =>
                            a.AccountName.Contains(s) ||
                            a.AccountCode.Contains(s) ||
                            a.AccountId.ToString().Contains(s) ||
                            a.AccountType.ToString().Contains(s) ||
                            (a.Notes != null && a.Notes.Contains(s)) ||
                            a.Level.ToString().Contains(s));
                        break;
                }
            }

            // ============= 4) الترتيب حسب العمود ===============================
            q = so switch
            {
                "code" => (descending
                    ? q.OrderByDescending(a => a.AccountCode)
                    : q.OrderBy(a => a.AccountCode)),

                "id" => (descending
                    ? q.OrderByDescending(a => a.AccountId)
                    : q.OrderBy(a => a.AccountId)),

                "type" => (descending
                    ? q.OrderByDescending(a => a.AccountType)
                    : q.OrderBy(a => a.AccountType)),

                "level" => (descending
                    ? q.OrderByDescending(a => a.Level)
                    : q.OrderBy(a => a.Level)),

                "created" => (descending
                    ? q.OrderByDescending(a => a.CreatedAt)
                    : q.OrderBy(a => a.CreatedAt)),

                "updated" => (descending
                    ? q.OrderByDescending(a => a.UpdatedAt)
                    : q.OrderBy(a => a.UpdatedAt)),

                "name" or _ => (descending
                    ? q.OrderByDescending(a => a.AccountName)
                    : q.OrderBy(a => a.AccountName)),
            };

            // ============= 5) الترقيم (Paging) ================================
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50;

            int totalCount = await q.CountAsync();                  // إجمالي السجلات
            int totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
            if (page > totalPages) page = totalPages;

            var items = await q
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();                                     // بيانات الصفحة الحالية

            var model = new PagedResult<Account>(items, page, pageSize, totalCount)
            {
                Search = s,            // نص البحث
                SearchBy = sb,           // نوع البحث
                SortColumn = so,           // عمود الترتيب
                SortDescending = descending,   // ترتيب تنازلي؟
                UseDateRange = useDateRange, // استخدام فلتر التاريخ؟
                FromDate = fromDate,     // من تاريخ
                ToDate = toDate        // إلى تاريخ
                                       // مفيش FromCode/ToCode هنا، هنمررهم في ViewBag فقط
            };

            // تمرير القيم للـ ViewBag علشان البارشال _IndexFilters
            ViewBag.Search = s;
            ViewBag.SearchBy = sb;
            ViewBag.Sort = so;
            ViewBag.Dir = descending ? "desc" : "asc";
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.Total = totalCount;
            ViewBag.DateField = dateField;

            // ========= خيارات البحث في الدروب داون (نفس أسماء أعمدة الجدول) =======
            ViewBag.SearchOptions = new[]
            {
                new SelectListItem { Text = "البحث في الكل", Value = "all",   Selected = sb == "all"   },
                new SelectListItem { Text = "اسم الحساب",   Value = "name",  Selected = sb == "name"  },
                new SelectListItem { Text = "كود الحساب",   Value = "code",  Selected = sb == "code"  },
                new SelectListItem { Text = "رقم الحساب",   Value = "id",    Selected = sb == "id"    },
                new SelectListItem { Text = "نوع الحساب",   Value = "type",  Selected = sb == "type"  },
                new SelectListItem { Text = "الملاحظات",    Value = "notes", Selected = sb == "notes" },
                new SelectListItem { Text = "مستوى الحساب", Value = "level", Selected = sb == "level" }
            };

            // ========= خيارات الترتيب في البارشال (لو مستخدم) ======================
            ViewBag.SortOptions = new[]
            {
                new SelectListItem { Text = "اسم الحساب",       Value = "name",    Selected = so == "name"    },
                new SelectListItem { Text = "كود الحساب",       Value = "code",    Selected = so == "code"    },
                new SelectListItem { Text = "رقم الحساب",       Value = "id",      Selected = so == "id"      },
                new SelectListItem { Text = "نوع الحساب",       Value = "type",    Selected = so == "type"    },
                new SelectListItem { Text = "مستوى الحساب",     Value = "level",   Selected = so == "level"   },
                new SelectListItem { Text = "تاريخ الإنشاء",    Value = "created", Selected = so == "created" },
                new SelectListItem { Text = "تاريخ آخر تعديل",  Value = "updated", Selected = so == "updated" }
            };

            return View(model);
        }








        // =========================================================
        // GET: Accounts/Create
        // فتح شاشة إضافة حساب جديد
        // =========================================================
        public IActionResult Create(int? parentId)
        {
            var account = new Account();                // متغير: كائن حساب جديد

            if (parentId.HasValue)
            {
                var parent = _context.Accounts.FirstOrDefault(a => a.AccountId == parentId.Value);
                if (parent != null)
                {
                    account.ParentAccountId = parent.AccountId;
                    account.AccountType = parent.AccountType;   // نفس نوع الحساب الأب
                    account.Level = parent.Level + 1;           // مستوى = مستوى الأب + 1
                }
            }
            else
            {
                account.Level = 1;
                account.AccountType = AccountType.Asset;
            }

            account.IsLeaf = true;
            account.IsActive = true;
            account.CreatedAt = DateTime.Now;

            FillAccountDropdowns(account);
            return View(account);
        }

        // =========================================================
        // POST: Accounts/Create
        // حفظ حساب جديد
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Account account)
        {
            if (!ModelState.IsValid)
            {
                FillAccountDropdowns(account);
                return View(account);
            }

            // ضبط النوع والمستوى من الحساب الأب (لو موجود)
            if (account.ParentAccountId.HasValue)
            {
                var parent = await _context.Accounts
                    .FirstOrDefaultAsync(a => a.AccountId == account.ParentAccountId.Value);

                if (parent != null)
                {
                    account.AccountType = parent.AccountType;
                    account.Level = parent.Level + 1;
                }
            }

            account.CreatedAt = DateTime.Now;
            account.UpdatedAt = null;

            _context.Accounts.Add(account);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "تم إضافة الحساب بنجاح.";
            return RedirectToAction(nameof(Index));
        }









        // =========================================================
        // GET: Accounts/Edit/5
        // فتح شاشة تعديل حساب
        // =========================================================
        public async Task<IActionResult> Edit(int id)
        {
            var account = await _context.Accounts.FindAsync(id);
            if (account == null)
                return NotFound();

            FillAccountDropdowns(account);
            return View(account);
        }

        // =========================================================
        // POST: Accounts/Edit/5
        // حفظ تعديل حساب
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Account account)
        {
            if (id != account.AccountId)
                return NotFound();

            if (!ModelState.IsValid)
            {
                FillAccountDropdowns(account);
                return View(account);
            }

            // ضبط النوع والمستوى من الحساب الأب مرة أخرى لو تغيّر
            if (account.ParentAccountId.HasValue)
            {
                var parent = await _context.Accounts
                    .FirstOrDefaultAsync(a => a.AccountId == account.ParentAccountId.Value);

                if (parent != null)
                {
                    account.AccountType = parent.AccountType;
                    account.Level = parent.Level + 1;
                }
            }

            try
            {
                account.UpdatedAt = DateTime.Now;
                _context.Update(account);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم تحديث الحساب بنجاح.";
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!AccountExists(account.AccountId))
                    return NotFound();

                throw;
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // حذف حساب مفرد (من جدول القوائم الموحد)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var account = await _context.Accounts.FindAsync(id);
            if (account == null)
                return NotFound();

            _context.Accounts.Remove(account);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "تم حذف الحساب بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // BulkDelete: حذف مجموعة حسابات مختارة
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(int[] ids)
        {
            if (ids == null || ids.Length == 0)
            {
                TempData["ErrorMessage"] = "لم يتم اختيار أي حسابات للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var accounts = await _context.Accounts
                .Where(a => ids.Contains(a.AccountId))
                .ToListAsync();

            _context.Accounts.RemoveRange(accounts);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"تم حذف {accounts.Count} حساب/حسابات.";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // DeleteAll: حذف كل الحسابات (بحذر شديد)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _context.Accounts.ToListAsync();
            _context.Accounts.RemoveRange(all);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "تم حذف جميع الحسابات.";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // Export: تصدير الحسابات إلى ملف CSV يفتح في Excel
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy = "all",
            string? sort = "name",
            string? dir = "asc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? dateField = "CreatedAt",
            int? fromCode = null,
            int? toCode = null)
        {
            // نعيد استخدام نفس منطق Index بدون الترقيم
            // (نسخ مبسط: لو حابب نعمل دالة مشتركة للفلاتر نقدر لاحقاً)

            IQueryable<Account> q = _context.Accounts.AsNoTracking();

            var s = (search ?? string.Empty).Trim();
            var sb = (searchBy ?? "all").Trim().ToLowerInvariant();
            var so = (sort ?? "name").Trim().ToLowerInvariant();
            bool descending = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            // فلترة من/إلى كود
            if (fromCode.HasValue)
                q = q.Where(a => a.AccountId >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(a => a.AccountId <= toCode.Value);

            // فلترة بالتاريخ
            if (useDateRange)
            {
                if (fromDate.HasValue)
                {
                    var f = fromDate.Value;
                    if (string.Equals(dateField, "UpdatedAt", StringComparison.OrdinalIgnoreCase))
                        q = q.Where(a => a.UpdatedAt >= f);
                    else
                        q = q.Where(a => a.CreatedAt >= f);
                }
                if (toDate.HasValue)
                {
                    var t = toDate.Value;
                    if (string.Equals(dateField, "UpdatedAt", StringComparison.OrdinalIgnoreCase))
                        q = q.Where(a => a.UpdatedAt <= t);
                    else
                        q = q.Where(a => a.CreatedAt <= t);
                }
            }

            // البحث
            if (!string.IsNullOrWhiteSpace(s))
            {
                switch (sb)
                {
                    case "name":
                        q = q.Where(a => a.AccountName.Contains(s));
                        break;
                    case "code":
                        q = q.Where(a => a.AccountCode.Contains(s));
                        break;
                    case "id":
                        if (int.TryParse(s, out int idVal))
                            q = q.Where(a => a.AccountId == idVal);
                        else
                            q = q.Where(a => a.AccountId.ToString().Contains(s));
                        break;
                    case "type":
                        q = q.Where(a => a.AccountType.ToString().Contains(s));
                        break;
                    case "notes":
                        q = q.Where(a => a.Notes != null && a.Notes.Contains(s));
                        break;
                    case "level":
                        if (int.TryParse(s, out int lvlVal))
                            q = q.Where(a => a.Level == lvlVal);
                        else
                            q = q.Where(a => a.Level.ToString().Contains(s));
                        break;
                    case "all":
                    default:
                        q = q.Where(a =>
                            a.AccountName.Contains(s) ||
                            a.AccountCode.Contains(s) ||
                            a.AccountId.ToString().Contains(s) ||
                            a.AccountType.ToString().Contains(s) ||
                            (a.Notes != null && a.Notes.Contains(s)) ||
                            a.Level.ToString().Contains(s));
                        break;
                }
            }

            // الترتيب
            q = so switch
            {
                "code" => (descending ? q.OrderByDescending(a => a.AccountCode) : q.OrderBy(a => a.AccountCode)),
                "id" => (descending ? q.OrderByDescending(a => a.AccountId) : q.OrderBy(a => a.AccountId)),
                "type" => (descending ? q.OrderByDescending(a => a.AccountType) : q.OrderBy(a => a.AccountType)),
                "level" => (descending ? q.OrderByDescending(a => a.Level) : q.OrderBy(a => a.Level)),
                "created" => (descending ? q.OrderByDescending(a => a.CreatedAt) : q.OrderBy(a => a.CreatedAt)),
                "updated" => (descending ? q.OrderByDescending(a => a.UpdatedAt) : q.OrderBy(a => a.UpdatedAt)),
                "name" or _ => (descending ? q.OrderByDescending(a => a.AccountName) : q.OrderBy(a => a.AccountName)),
            };

            var list = await q.ToListAsync();    // متغير: قائمة النهائية للتصدير

            // بناء ملف CSV (UTF-8 مع BOM علشان العربي)
            var sbCsv = new StringBuilder();

            // رأس الأعمدة (نفس أسماء الأعمدة في الجدول)
            sbCsv.AppendLine("رقم الحساب,كود الحساب,اسم الحساب,نوع الحساب,الحساب الأب,مستوى الحساب,حساب تفصيلي,نشط,ملاحظات,تاريخ الإنشاء,تاريخ آخر تعديل");

            foreach (var a in list)
            {
                var parentCode = a.ParentAccountId.HasValue
                    ? _context.Accounts.AsNoTracking()
                        .Where(p => p.AccountId == a.ParentAccountId.Value)
                        .Select(p => p.AccountCode)
                        .FirstOrDefault()
                    : "";

                // سطر واحد من القيم
                sbCsv.AppendLine(string.Join(",",
                    a.AccountId,
                    EscapeCsv(a.AccountCode),
                    EscapeCsv(a.AccountName),
                    a.AccountType,
                    EscapeCsv(parentCode),
                    a.Level,
                    a.IsLeaf ? "نعم" : "لا",
                    a.IsActive ? "نعم" : "لا",
                    EscapeCsv(a.Notes),
                    a.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                    a.UpdatedAt?.ToString("yyyy-MM-dd HH:mm") ?? ""
                ));
            }

            var csvString = sbCsv.ToString();
            var preamble = Encoding.UTF8.GetPreamble();
            var body = Encoding.UTF8.GetBytes(csvString);
            var bytes = new byte[preamble.Length + body.Length];
            Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
            Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);

            return File(bytes, "text/csv; charset=utf-8", "Accounts.csv");
        }

        // دالة صغيرة للهروب من الفواصل في CSV
        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }

        private bool AccountExists(int id)
        {
            return _context.Accounts.Any(e => e.AccountId == id);
        }
    }
}
