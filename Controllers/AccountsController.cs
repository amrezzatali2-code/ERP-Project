using System;
using System.Collections.Generic;                    // القوائم List
using System.Globalization;                          // NumberStyles لفلتر الأعمدة
using System.Linq;                                   // استعلامات LINQ
using System.Linq.Expressions;                       // Expression لـ ApplySearchSort
using System.Text;                                   // بناء ملف CSV
using System.Threading.Tasks;                        // async / await
using Microsoft.AspNetCore.Mvc;                      // الكنترولر و IActionResult
using Microsoft.AspNetCore.Mvc.Rendering;            // SelectListItem
using Microsoft.EntityFrameworkCore;                 // AsNoTracking / ToListAsync
using ERP.Data;                                      // AppDbContext
using ERP.Filters;                                   // RequirePermission
using ERP.Infrastructure;                             // PagedResult + UserActivityLogger
using ERP.Models;                                    // Account, UserActionType
using ERP.Security;                                  // PermissionCodes
using ERP.Services;                                 // IPermissionService

namespace ERP.Controllers
{
    public class AccountsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IUserActivityLogger _activityLogger;
        private readonly IPermissionService _permissionService;

        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        private const string InvestorAccountCode = "3101"; // حساب المستثمرين

        public AccountsController(AppDbContext context, IUserActivityLogger activityLogger, IPermissionService permissionService)
        {
            _context = context;
            _activityLogger = activityLogger;
            _permissionService = permissionService;
        }

        private static Task<bool> CanViewInvestorsAsync() => Task.FromResult(true); // إظهار/إخفاء 3101 يعتمد على «الحسابات المسموح رؤيتها» فقط

        /// <summary>تطبيق فلاتر الأعمدة (بنمط Excel) على استعلام الحسابات.</summary>
        private static IQueryable<Account> ApplyColumnFilters(
            IQueryable<Account> query,
            string? filterCol_id,
            string? filterCol_idExpr,
            string? filterCol_code,
            string? filterCol_name,
            string? filterCol_type,
            string? filterCol_level,
            string? filterCol_levelExpr,
            string? filterCol_leaf,
            string? filterCol_active,
            string? filterCol_created,
            string? filterCol_updated,
            string? filterCol_notes)
        {
            if (!string.IsNullOrWhiteSpace(filterCol_idExpr))
                query = AccountListNumericExpr.ApplyAccountIdExpr(query, filterCol_idExpr);
            else if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) query = query.Where(a => ids.Contains(a.AccountId));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_code))
            {
                var vals = filterCol_code.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) query = query.Where(a => vals.Contains(a.AccountCode));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_name))
            {
                var vals = filterCol_name.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) query = query.Where(a => vals.Contains(a.AccountName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_type))
            {
                var vals = filterCol_type.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) query = query.Where(a => vals.Contains(a.AccountType.ToString()));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_levelExpr))
                query = AccountListNumericExpr.ApplyLevelExpr(query, filterCol_levelExpr);
            else if (!string.IsNullOrWhiteSpace(filterCol_level))
            {
                var ids = filterCol_level.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0) query = query.Where(a => ids.Contains(a.Level));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_leaf))
            {
                var parts = filterCol_leaf.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim().ToLowerInvariant())
                    .Where(x => x == "true" || x == "false" || x == "1" || x == "0" || x == "نعم" || x == "لا" || x == "تفصيلي" || x == "تجميعي").ToList();
                var includeTrue = parts.Any(x => x == "true" || x == "1" || x == "نعم" || x == "تفصيلي");
                var includeFalse = parts.Any(x => x == "false" || x == "0" || x == "لا" || x == "تجميعي");
                if (includeTrue && !includeFalse) query = query.Where(a => a.IsLeaf);
                else if (includeFalse && !includeTrue) query = query.Where(a => !a.IsLeaf);
            }
            if (!string.IsNullOrWhiteSpace(filterCol_active))
            {
                var parts = filterCol_active.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim().ToLowerInvariant())
                    .Where(x => x == "true" || x == "false" || x == "1" || x == "0" || x == "نعم" || x == "لا" || x == "نشط" || x == "موقوف").ToList();
                var includeTrue = parts.Any(x => x == "true" || x == "1" || x == "نعم" || x == "نشط");
                var includeFalse = parts.Any(x => x == "false" || x == "0" || x == "لا" || x == "موقوف");
                if (includeTrue && !includeFalse) query = query.Where(a => a.IsActive);
                else if (includeFalse && !includeTrue) query = query.Where(a => !a.IsActive);
            }
            if (!string.IsNullOrWhiteSpace(filterCol_created))
            {
                var parts = filterCol_created.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length >= 8).ToList();
                if (parts.Count > 0)
                {
                    var dates = new List<DateTime>();
                    foreach (var p in parts)
                        if (DateTime.TryParse(p, out var d)) dates.Add(d.Date);
                    if (dates.Count > 0) query = query.Where(a => dates.Contains(a.CreatedAt.Date));
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_updated))
            {
                var parts = filterCol_updated.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length >= 8).ToList();
                if (parts.Count > 0)
                {
                    var dates = new List<DateTime>();
                    foreach (var p in parts)
                        if (DateTime.TryParse(p, out var d)) dates.Add(d.Date);
                    if (dates.Count > 0) query = query.Where(a => a.UpdatedAt.HasValue && dates.Contains(a.UpdatedAt.Value.Date));
                }
            }
            return query;
        }

        /// <summary>استعلام الحسابات: بحث موحّد + فلاتر أساسية (بدون ترتيب نهائي).</summary>
        private IQueryable<Account> BuildAccountsQuery(
            bool hideInvestorAccount,
            string? search,
            string? searchBy,
            string? searchMode,
            string? sort,
            string? dir,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            string? dateField,
            int? fromCode,
            int? toCode)
        {
            IQueryable<Account> q = _context.Accounts.AsNoTracking();
            if (hideInvestorAccount)
                q = q.Where(a => a.AccountCode != InvestorAccountCode);

            if (fromCode.HasValue)
                q = q.Where(a => a.AccountId >= fromCode.Value);
            if (toCode.HasValue)
                q = q.Where(a => a.AccountId <= toCode.Value);

            if (useDateRange)
            {
                if (fromDate.HasValue)
                {
                    if (string.Equals(dateField, "UpdatedAt", StringComparison.OrdinalIgnoreCase))
                        q = q.Where(a => a.UpdatedAt >= fromDate.Value);
                    else
                        q = q.Where(a => a.CreatedAt >= fromDate.Value);
                }
                if (toDate.HasValue)
                {
                    if (string.Equals(dateField, "UpdatedAt", StringComparison.OrdinalIgnoreCase))
                        q = q.Where(a => a.UpdatedAt <= toDate.Value);
                    else
                        q = q.Where(a => a.CreatedAt <= toDate.Value);
                }
            }

            var stringFields =
                new Dictionary<string, Expression<Func<Account, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["name"] = a => a.AccountName,
                    ["code"] = a => a.AccountCode,
                    ["type"] = a => a.AccountType.ToString(),
                    ["notes"] = a => a.Notes
                };
            var intFields =
                new Dictionary<string, Expression<Func<Account, int>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = a => a.AccountId,
                    ["level"] = a => a.Level
                };
            var orderFields =
                new Dictionary<string, Expression<Func<Account, object>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["name"] = a => a.AccountName,
                    ["code"] = a => a.AccountCode,
                    ["id"] = a => a.AccountId,
                    ["type"] = a => a.AccountType,
                    ["level"] = a => a.Level,
                    ["leaf"] = a => a.IsLeaf,
                    ["active"] = a => a.IsActive,
                    ["created"] = a => a.CreatedAt,
                    ["updated"] = a => a.UpdatedAt ?? a.CreatedAt,
                    ["notes"] = a => a.Notes ?? ""
                };

            q = q.ApplySearchSort(
                search,
                searchBy,
                sort,
                dir,
                stringFields,
                intFields,
                orderFields,
                defaultSearchBy: "all",
                defaultSortBy: "code",
                searchMode: searchMode,
                applyOrdering: false);

            return q;
        }

        private static IQueryable<Account> ApplyAccountSort(IQueryable<Account> query, string? sort, string? dir)
        {
            var key = (sort ?? "code").Trim();
            var desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            return key.ToLowerInvariant() switch
            {
                "code" => desc ? query.OrderByDescending(a => a.AccountCode) : query.OrderBy(a => a.AccountCode),
                "id" => desc ? query.OrderByDescending(a => a.AccountId) : query.OrderBy(a => a.AccountId),
                "name" => desc ? query.OrderByDescending(a => a.AccountName) : query.OrderBy(a => a.AccountName),
                "type" => desc ? query.OrderByDescending(a => a.AccountType) : query.OrderBy(a => a.AccountType),
                "level" => desc ? query.OrderByDescending(a => a.Level) : query.OrderBy(a => a.Level),
                "leaf" => desc ? query.OrderByDescending(a => a.IsLeaf) : query.OrderBy(a => a.IsLeaf),
                "active" => desc ? query.OrderByDescending(a => a.IsActive) : query.OrderBy(a => a.IsActive),
                "created" => desc ? query.OrderByDescending(a => a.CreatedAt) : query.OrderBy(a => a.CreatedAt),
                "updated" => desc ? query.OrderByDescending(a => a.UpdatedAt) : query.OrderBy(a => a.UpdatedAt),
                "notes" => desc ? query.OrderByDescending(a => a.Notes ?? "") : query.OrderBy(a => a.Notes ?? ""),
                _ => desc ? query.OrderByDescending(a => a.AccountCode) : query.OrderBy(a => a.AccountCode),
            };
        }

        /// <summary>API: جلب القيم المميزة لعمود (للفلترة بنمط Excel).</summary>
        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var columnLower = (column ?? "").Trim().ToLowerInvariant();
            var canViewInvestors = await CanViewInvestorsAsync();
            IQueryable<Account> accountsQ = _context.Accounts.AsNoTracking();
            if (!canViewInvestors)
                accountsQ = accountsQ.Where(a => a.AccountCode != InvestorAccountCode);

            if (columnLower == "id")
            {
                var ids = await accountsQ
                    .Select(a => a.AccountId).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "code")
            {
                var q = accountsQ.Select(a => a.AccountCode);
                if (!string.IsNullOrEmpty(searchTerm)) q = q.Where(s => s.ToLower().Contains(searchTerm));
                var list = await q.Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "name")
            {
                var q = accountsQ.Select(a => a.AccountName);
                if (!string.IsNullOrEmpty(searchTerm)) q = q.Where(s => s.ToLower().Contains(searchTerm));
                var list = await q.Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "type")
            {
                // جلب القيم كـ enum ثم تحويلها لنص في الذاكرة (تفادي فشل ترجمة ToString في EF)
                var list = await accountsQ
                    .Select(a => a.AccountType).Distinct().OrderBy(x => x).Take(100).ToListAsync();
                return Json(list.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "level")
            {
                var ids = await accountsQ
                    .Select(a => a.Level).Distinct().OrderBy(x => x).Take(50).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "leaf")
            {
                var items = new[] { new { value = "true", display = "تفصيلي" }, new { value = "false", display = "تجميعي" } };
                return Json(items);
            }
            if (columnLower == "active")
            {
                var items = new[] { new { value = "true", display = "نشط" }, new { value = "false", display = "موقوف" } };
                return Json(items);
            }
            if (columnLower == "created")
            {
                var dates = await accountsQ
                    .Select(a => a.CreatedAt.Date).Distinct().OrderByDescending(x => x).Take(500).ToListAsync();
                return Json(dates.Select(d => new { value = d.ToString("yyyy-MM-dd"), display = d.ToString("yyyy-MM-dd") }));
            }
            if (columnLower == "updated")
            {
                var dates = await accountsQ
                    .Where(a => a.UpdatedAt.HasValue).Select(a => a.UpdatedAt!.Value.Date).Distinct().OrderByDescending(x => x).Take(500).ToListAsync();
                return Json(dates.Select(d => new { value = d.ToString("yyyy-MM-dd"), display = d.ToString("yyyy-MM-dd") }));
            }
            if (columnLower == "notes")
            {
                var q = accountsQ.Where(a => a.Notes != null && a.Notes != "").Select(a => a.Notes!);
                if (!string.IsNullOrEmpty(searchTerm)) q = q.Where(s => s.ToLower().Contains(searchTerm));
                var list = await q.Distinct().OrderBy(x => x).Take(300).ToListAsync();
                return Json(list.Select(v => new { value = v, display = v.Length > 60 ? v.Substring(0, 60) + "…" : v }));
            }
            return Json(Array.Empty<object>());
        }

        // =========================================================
        // GET: تفاصيل حساب
        // =========================================================
        [HttpGet]
        [RequirePermission("Accounts.Index")]
        public async Task<IActionResult> Details(int id)
        {
            var account = await _context.Accounts
                .AsNoTracking()
                .Include(a => a.ParentAccount)
                .FirstOrDefaultAsync(a => a.AccountId == id);
            if (account == null)
                return NotFound();
            if (account.AccountCode == InvestorAccountCode && !await CanViewInvestorsAsync())
                return NotFound();
            return View(account);
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
        [RequirePermission("Accounts.Index")]
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",
            string? searchMode = "contains",
            string? sort = "code",
            string? dir = "asc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? dateField = "CreatedAt",
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_code = null,
            string? filterCol_name = null,
            string? filterCol_type = null,
            string? filterCol_level = null,
            string? filterCol_levelExpr = null,
            string? filterCol_leaf = null,
            string? filterCol_active = null,
            string? filterCol_created = null,
            string? filterCol_updated = null,
            string? filterCol_notes = null,
            int page = 1,
            int pageSize = 10)
        {
            var pageSizeQuery = Request.Query["pageSize"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageSizeQuery) && int.TryParse(pageSizeQuery.Trim(), out var psVal))
                pageSize = psVal;

            var pageQuery = Request.Query["page"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageQuery) && int.TryParse(pageQuery.Trim(), out var pVal))
                page = pVal;

            searchBy ??= "all";
            sort ??= "code";
            dir ??= "asc";
            searchMode ??= "contains";

            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm != "starts" && sm != "ends" && sm != "contains")
                sm = "contains";

            var s = (search ?? string.Empty).Trim();
            var sb = (searchBy ?? "all").Trim().ToLowerInvariant();
            var so = (sort ?? "code").Trim().ToLowerInvariant();
            bool descending = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            if (page < 1) page = 1;

            var hideInvestor = !await CanViewInvestorsAsync();

            IQueryable<Account> q = BuildAccountsQuery(
                hideInvestor, s, sb, sm, so, dir,
                useDateRange, fromDate, toDate, dateField, fromCode, toCode);

            q = ApplyColumnFilters(
                q,
                filterCol_id,
                filterCol_idExpr,
                filterCol_code,
                filterCol_name,
                filterCol_type,
                filterCol_level,
                filterCol_levelExpr,
                filterCol_leaf,
                filterCol_active,
                filterCol_created,
                filterCol_updated,
                filterCol_notes);

            int totalCount = await q.CountAsync();
            int leafCountFiltered = await q.CountAsync(a => a.IsLeaf);
            int activeCountFiltered = await q.CountAsync(a => a.IsActive);

            q = ApplyAccountSort(q, so, dir);

            if (pageSize < 0) pageSize = 10;
            var allowedSizes = new[] { 10, 25, 50, 100, 200, 0 };
            if (pageSize != 0 && !allowedSizes.Contains(pageSize))
                pageSize = 10;

            int totalPages;
            List<Account> items;

            if (pageSize == 0)
            {
                var effectiveTake = totalCount == 0 ? 10 : Math.Min(totalCount, 100_000);
                page = 1;
                items = await q.Skip(0).Take(effectiveTake).ToListAsync();
                totalPages = 1;
            }
            else
            {
                totalPages = totalCount == 0 ? 1 : Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
                if (page > totalPages) page = totalPages;
                items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            }

            var model = new PagedResult<Account>(items, page, pageSize, totalCount)
            {
                Search = s,
                SearchBy = sb,
                SortColumn = so,
                SortDescending = descending,
                UseDateRange = useDateRange,
                FromDate = fromDate,
                ToDate = toDate,
                TotalPages = totalPages
            };

            ViewBag.Search = s;
            ViewBag.SearchBy = sb;
            ViewBag.SearchMode = sm;
            ViewBag.Sort = so;
            ViewBag.Dir = descending ? "desc" : "asc";
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.Total = totalCount;
            ViewBag.TotalCount = totalCount;
            ViewBag.LeafCountFiltered = leafCountFiltered;
            ViewBag.ActiveCountFiltered = activeCountFiltered;
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.DateField = dateField;
            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_IdExpr = filterCol_idExpr;
            ViewBag.FilterCol_Code = filterCol_code;
            ViewBag.FilterCol_Name = filterCol_name;
            ViewBag.FilterCol_Type = filterCol_type;
            ViewBag.FilterCol_Level = filterCol_level;
            ViewBag.FilterCol_LevelExpr = filterCol_levelExpr;
            ViewBag.FilterCol_Leaf = filterCol_leaf;
            ViewBag.FilterCol_Active = filterCol_active;
            ViewBag.FilterCol_Created = filterCol_created;
            ViewBag.FilterCol_Updated = filterCol_updated;
            ViewBag.FilterCol_Notes = filterCol_notes;

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
        [RequirePermission("Accounts.Edit")]
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
        [RequirePermission("Accounts.Edit")]
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

            await _activityLogger.LogAsync(
                UserActionType.Create,
                "Account",
                account.AccountId,
                $"إنشاء حساب جديد: {account.AccountCode} - {account.AccountName}");

            TempData["SuccessMessage"] = "تم إضافة الحساب بنجاح.";
            return RedirectToAction(nameof(Index));
        }









        // =========================================================
        // GET: Accounts/Edit/5
        // فتح شاشة تعديل حساب
        // =========================================================
        [RequirePermission("Accounts.Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            var account = await _context.Accounts.FindAsync(id);
            if (account == null)
                return NotFound();
            if (account.AccountCode == InvestorAccountCode && !await CanViewInvestorsAsync())
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
        [RequirePermission("Accounts.Edit")]
        public async Task<IActionResult> Edit(int id, Account account)
        {
            if (id != account.AccountId)
                return NotFound();
            if (account.AccountCode == InvestorAccountCode && !await CanViewInvestorsAsync())
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

            var existing = await _context.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.AccountId == id);
            var oldValues = existing != null
                ? System.Text.Json.JsonSerializer.Serialize(new { existing.AccountCode, existing.AccountName, existing.AccountType })
                : null;

            try
            {
                account.UpdatedAt = DateTime.Now;
                _context.Update(account);
                await _context.SaveChangesAsync();

                var newValues = System.Text.Json.JsonSerializer.Serialize(new { account.AccountCode, account.AccountName, account.AccountType });
                await _activityLogger.LogAsync(
                    UserActionType.Edit,
                    "Account",
                    account.AccountId,
                    $"تعديل حساب: {account.AccountCode} - {account.AccountName}",
                    oldValues,
                    newValues);

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
        // GET: تأكيد حذف حساب
        // =========================================================
        [HttpGet]
        [RequirePermission("Accounts.Edit")]
        public async Task<IActionResult> Delete(int id)
        {
            var account = await _context.Accounts
                .Include(a => a.ParentAccount)
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.AccountId == id);
            if (account == null)
                return NotFound();
            if (account.AccountCode == InvestorAccountCode && !await CanViewInvestorsAsync())
                return NotFound();
            return View(account);
        }

        // =========================================================
        // POST: تنفيذ حذف حساب مفرد
        // =========================================================
        [HttpPost]
        [ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [RequirePermission("Accounts.Edit")]
        public async Task<IActionResult> DeletePost(int id)
        {
            var account = await _context.Accounts.FindAsync(id);
            if (account == null)
                return NotFound();
            if (account.AccountCode == InvestorAccountCode && !await CanViewInvestorsAsync())
                return NotFound();

            var oldValues = System.Text.Json.JsonSerializer.Serialize(new { account.AccountCode, account.AccountName });
            _context.Accounts.Remove(account);
            await _context.SaveChangesAsync();

            await _activityLogger.LogAsync(
                UserActionType.Delete,
                "Account",
                id,
                $"حذف حساب: {account.AccountCode} - {account.AccountName}",
                oldValues: oldValues);

            TempData["SuccessMessage"] = "تم حذف الحساب بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // BulkDelete: حذف مجموعة حسابات مختارة
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Accounts.Edit")]
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

            if (!await CanViewInvestorsAsync())
                accounts = accounts.Where(a => a.AccountCode != InvestorAccountCode).ToList();

            if (accounts.Count == 0)
            {
                TempData["ErrorMessage"] = "لا توجد حسابات مسموح حذفها (قد تكون حسابات المستثمرين محجوبة).";
                return RedirectToAction(nameof(Index));
            }

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
        [RequirePermission("Accounts.Edit")]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _context.Accounts.ToListAsync();
            if (!await CanViewInvestorsAsync())
                all = all.Where(a => a.AccountCode != InvestorAccountCode).ToList();

            if (all.Count == 0)
            {
                TempData["ErrorMessage"] = "لا توجد حسابات مسموح حذفها (قد تكون حسابات المستثمرين محجوبة).";
                return RedirectToAction(nameof(Index));
            }

            _context.Accounts.RemoveRange(all);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "تم حذف جميع الحسابات.";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // Export: تصدير الحسابات إلى ملف CSV يفتح في Excel
        // =========================================================
        [HttpGet]
        [RequirePermission("Accounts.Index")]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy = "all",
            string? searchMode = "contains",
            string? sort = "code",
            string? dir = "asc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? dateField = "CreatedAt",
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_code = null,
            string? filterCol_name = null,
            string? filterCol_type = null,
            string? filterCol_level = null,
            string? filterCol_levelExpr = null,
            string? filterCol_leaf = null,
            string? filterCol_active = null,
            string? filterCol_created = null,
            string? filterCol_updated = null,
            string? filterCol_notes = null)
        {
            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm != "starts" && sm != "ends" && sm != "contains")
                sm = "contains";

            var s = (search ?? string.Empty).Trim();
            var sb = (searchBy ?? "all").Trim().ToLowerInvariant();
            var so = (sort ?? "code").Trim().ToLowerInvariant();
            var dirNorm = (dir ?? "asc").Trim().ToLowerInvariant();

            var hideInvestor = !await CanViewInvestorsAsync();

            IQueryable<Account> q = BuildAccountsQuery(
                hideInvestor, s, sb, sm, so, dirNorm,
                useDateRange, fromDate, toDate, dateField, fromCode, toCode);

            q = ApplyColumnFilters(
                q,
                filterCol_id,
                filterCol_idExpr,
                filterCol_code,
                filterCol_name,
                filterCol_type,
                filterCol_level,
                filterCol_levelExpr,
                filterCol_leaf,
                filterCol_active,
                filterCol_created,
                filterCol_updated,
                filterCol_notes);

            q = ApplyAccountSort(q, so, dirNorm);

            var list = await q.ToListAsync();

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

            return File(bytes, "text/csv; charset=utf-8", ExcelExportNaming.ArabicTimestampedFileName("دليل الحسابات", ".csv"));
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
