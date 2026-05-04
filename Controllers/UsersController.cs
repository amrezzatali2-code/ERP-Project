using System;                                     // متغيرات التاريخ DateTime
using System.Collections.Generic;                 // Dictionary, List
using System.Globalization;                       // تنسيق التواريخ عند التصدير
using System.Linq;                                // LINQ: Where / OrderBy
using System.Linq.Expressions;                    // Expression<Func<...>>
using System.Text;                                // StringBuilder للتصدير
using System.Threading.Tasks;                     // async / await
using Microsoft.AspNetCore.Mvc;                   // Controller, IActionResult
using Microsoft.EntityFrameworkCore;              // AsNoTracking, ToListAsync
using ERP.Data;                                   // AppDbContext الاتصال بقاعدة البيانات
using ERP.Filters;
using ERP.Infrastructure;                         // PagedResult + ApplySearchSort + UserActivityLogger
using ERP.Models;                                 // User, UserActionType
using ERP.Security;
using ERP.Services;
using ClosedXML.Excel;                      // لتصدير Excel
using System.IO;






namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إدارة المستخدمين (Users)
    /// - شاشة قائمة بنظام القوائم الموحد (Index).
    /// - CRUD كامل: Create / Edit / Details / Delete.
    /// - Export + BulkDelete + DeleteAll.
    /// </summary>
    public class UsersController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IUserActivityLogger _activityLogger;
        private readonly IPermissionService _permissionService;

        public UsersController(AppDbContext context, IUserActivityLogger activityLogger, IPermissionService permissionService)
        {
            _context = context;
            _activityLogger = activityLogger;
            _permissionService = permissionService;
        }






        // =========================================================
        // دالة خاصة: تجهيز الاستعلام الأساسي + الفلاتر + البحث + الترتيب
        // نستخدمها في Index و Export حتى لا نكرر الكود.
        // =========================================================
        private IQueryable<User> BuildQuery(
            string? search,
            string? searchBy,
            string? searchMode,
            string? sort,
            string? dir,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            int? fromCode,
            int? toCode)
        {
            IQueryable<User> q = _context.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .AsNoTracking();

            if (fromCode.HasValue)
                q = q.Where(u => u.UserId >= fromCode.Value);

            if (toCode.HasValue)
                q = q.Where(u => u.UserId <= toCode.Value);

            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                DateTime from = fromDate.Value.Date;
                DateTime to = toDate.Value.Date.AddDays(1).AddTicks(-1);

                q = q.Where(u => u.CreatedAt >= from && u.CreatedAt <= to);
            }

            var stringFields =
                new Dictionary<string, Expression<Func<User, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["username"] = u => u.UserName,
                    ["display"] = u => u.DisplayName,
                };

            var intFields =
                new Dictionary<string, Expression<Func<User, int>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = u => u.UserId
                };

            var orderFields =
                new Dictionary<string, Expression<Func<User, object>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["UserId"] = u => u.UserId,
                    ["UserName"] = u => u.UserName,
                    ["DisplayName"] = u => u.DisplayName,
                    ["IsActive"] = u => u.IsActive,
                    ["CreatedAt"] = u => u.CreatedAt,
                    ["UpdatedAt"] = u => u.UpdatedAt ?? DateTime.MinValue
                };

            var mode = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (mode != "starts" && mode != "ends")
                mode = "contains";

            string? searchPass = search;
            string? searchByPass = searchBy;

            if (!string.IsNullOrWhiteSpace(search) &&
                string.Equals(searchBy, "all", StringComparison.OrdinalIgnoreCase))
            {
                var text = search.Trim();
                int? pidNum = int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid) ? pid : null;

                if (mode == "starts")
                {
                    q = q.Where(u =>
                        (u.UserName != null && u.UserName.StartsWith(text)) ||
                        (u.DisplayName != null && u.DisplayName.StartsWith(text)) ||
                        (pidNum.HasValue && u.UserId == pidNum.Value) ||
                        u.UserRoles.Any(ur => ur.Role != null && ur.Role.Name.StartsWith(text)));
                }
                else if (mode == "ends")
                {
                    q = q.Where(u =>
                        (u.UserName != null && u.UserName.EndsWith(text)) ||
                        (u.DisplayName != null && u.DisplayName.EndsWith(text)) ||
                        (pidNum.HasValue && u.UserId == pidNum.Value) ||
                        u.UserRoles.Any(ur => ur.Role != null && ur.Role.Name.EndsWith(text)));
                }
                else
                {
                    q = q.Where(u =>
                        (u.UserName != null && u.UserName.Contains(text)) ||
                        (u.DisplayName != null && u.DisplayName.Contains(text)) ||
                        (pidNum.HasValue && u.UserId == pidNum.Value) ||
                        u.UserRoles.Any(ur => ur.Role != null && ur.Role.Name.Contains(text)));
                }
                searchPass = null;
                searchByPass = "all";
            }
            else if (!string.IsNullOrWhiteSpace(search) &&
                     string.Equals(searchBy, "roles", StringComparison.OrdinalIgnoreCase))
            {
                var text = search.Trim();
                if (mode == "starts")
                    q = q.Where(u => u.UserRoles.Any(ur => ur.Role != null && ur.Role.Name.StartsWith(text)));
                else if (mode == "ends")
                    q = q.Where(u => u.UserRoles.Any(ur => ur.Role != null && ur.Role.Name.EndsWith(text)));
                else
                    q = q.Where(u => u.UserRoles.Any(ur => ur.Role != null && ur.Role.Name.Contains(text)));
                searchPass = null;
                searchByPass = "all";
            }

            q = q.ApplySearchSort(
                search: searchPass,
                searchBy: searchByPass ?? "all",
                sort: sort,
                dir: dir,
                stringFields: stringFields,
                intFields: intFields,
                orderFields: orderFields,
                defaultSearchBy: "all",
                defaultSortBy: "UserName",
                searchMode: searchMode);

            return q;
        }

        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        private static IQueryable<User> ApplyColumnFilters(
            IQueryable<User> query,
            string? filterCol_id,
            string? filterCol_idExpr,
            string? filterCol_username,
            string? filterCol_roles,
            IReadOnlyList<int> resolvedRoleIds,
            bool includeNoRoleFromRolesFilter,
            string? filterCol_active,
            string? filterCol_created,
            string? filterCol_updated)
        {
            var idsFromCheckboxes = new List<int>();
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                idsFromCheckboxes = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
            }
            if (idsFromCheckboxes.Count > 0)
                query = query.Where(u => idsFromCheckboxes.Contains(u.UserId));
            else if (!string.IsNullOrWhiteSpace(filterCol_idExpr))
                query = UserListNumericExpr.ApplyUserIdExpr(query, filterCol_idExpr);

            if (!string.IsNullOrWhiteSpace(filterCol_username))
            {
                var vals = filterCol_username.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0) query = query.Where(u => vals.Contains(u.UserName));
            }
            if (!string.IsNullOrWhiteSpace(filterCol_roles))
            {
                var vals = filterCol_roles.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                var roleNamesRequested = vals.Where(v => v != "لا يوجد دور").ToList();
                var includeNoRole = vals.Contains("لا يوجد دور");

                if (roleNamesRequested.Count > 0 && resolvedRoleIds.Count == 0)
                    query = query.Where(u => false);
                else if (roleNamesRequested.Count > 0 || includeNoRole)
                {
                    query = query.Where(u =>
                        (includeNoRole && u.UserRoles.Count == 0) ||
                        (resolvedRoleIds.Count > 0 && u.UserRoles.Any(ur => resolvedRoleIds.Contains(ur.RoleId))));
                }
            }
            if (!string.IsNullOrWhiteSpace(filterCol_active))
            {
                var vals = filterCol_active.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim().ToLowerInvariant()).ToList();
                var wantTrue = vals.Any(v => v == "true" || v == "1" || v == "نشط");
                var wantFalse = vals.Any(v => v == "false" || v == "0" || v == "موقوف");
                if (wantTrue && !wantFalse) query = query.Where(u => u.IsActive);
                else if (wantFalse && !wantTrue) query = query.Where(u => !u.IsActive);
            }
            if (!string.IsNullOrWhiteSpace(filterCol_created))
            {
                var parts = filterCol_created.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length >= 8).ToList();
                if (parts.Count > 0)
                {
                    var dates = new List<DateTime>();
                    foreach (var p in parts)
                        if (DateTime.TryParse(p, out var d)) dates.Add(d);
                    if (dates.Count > 0) query = query.Where(u => dates.Contains(u.CreatedAt));
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
                        if (DateTime.TryParse(p, out var d)) dates.Add(d);
                    if (dates.Count > 0) query = query.Where(u => u.UpdatedAt.HasValue && dates.Contains(u.UpdatedAt.Value));
                }
            }
            return query;
        }

        /// <summary>
        /// مطابقة أسماء الأدوار من الفلتر مع جدول الأدوار (OrdinalIgnoreCase) ثم إرجاع RoleId.
        /// </summary>
        private async Task<(List<int> RoleIds, bool IncludeNoRole)> ResolveRoleFilterIdsAsync(string? filterCol_roles)
        {
            var empty = new List<int>();
            if (string.IsNullOrWhiteSpace(filterCol_roles))
                return (empty, false);

            var vals = filterCol_roles.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
            var roleNames = vals.Where(v => v != "لا يوجد دور").ToList();
            var includeNoRole = vals.Contains("لا يوجد دور");
            if (roleNames.Count == 0)
                return (empty, includeNoRole);

            var allRoles = await _context.Roles.AsNoTracking()
                .Select(r => new { r.RoleId, r.Name })
                .ToListAsync();

            var roleIds = new List<int>();
            foreach (var rn in roleNames)
            {
                var m = allRoles.FirstOrDefault(r => string.Equals(r.Name, rn, StringComparison.OrdinalIgnoreCase));
                if (m != null) roleIds.Add(m.RoleId);
            }
            return (roleIds, includeNoRole);
        }

        /// <summary>
        /// تجهيز حذف المستخدمين بفك الارتباطات التي تمنع الحذف، ثم حذف الجداول التابعة مباشرة.
        /// </summary>
        private async Task PrepareUsersForDeletionAsync(IReadOnlyCollection<int> userIds)
        {
            if (userIds == null || userIds.Count == 0)
                return;

            var ids = userIds.Distinct().ToList();

            var userRoles = await _context.UserRoles
                .Where(x => ids.Contains(x.UserId))
                .ToListAsync();
            if (userRoles.Count > 0)
                _context.UserRoles.RemoveRange(userRoles);

            var deniedPermissions = await _context.UserDeniedPermissions
                .Where(x => ids.Contains(x.UserId))
                .ToListAsync();
            if (deniedPermissions.Count > 0)
                _context.UserDeniedPermissions.RemoveRange(deniedPermissions);

            var extraPermissions = await _context.UserExtraPermissions
                .Where(x => ids.Contains(x.UserId))
                .ToListAsync();
            if (extraPermissions.Count > 0)
                _context.UserExtraPermissions.RemoveRange(extraPermissions);

            var accountVisibilityOverrides = await _context.UserAccountVisibilityOverrides
                .Where(x => ids.Contains(x.UserId))
                .ToListAsync();
            if (accountVisibilityOverrides.Count > 0)
                _context.UserAccountVisibilityOverrides.RemoveRange(accountVisibilityOverrides);

            var activityLogs = await _context.UserActivityLogs
                .Where(x => x.UserId.HasValue && ids.Contains(x.UserId.Value))
                .ToListAsync();
            foreach (var log in activityLogs)
                log.UserId = null;

            var stockLedgerEntries = await _context.StockLedger
                .Where(x => x.UserId.HasValue && ids.Contains(x.UserId.Value))
                .ToListAsync();
            foreach (var entry in stockLedgerEntries)
                entry.UserId = null;

            var customers = await _context.Customers
                .Where(x => x.UserId.HasValue && ids.Contains(x.UserId.Value))
                .ToListAsync();
            foreach (var customer in customers)
                customer.UserId = null;

            var employees = await _context.Set<Employee>()
                .Where(x => x.UserId.HasValue && ids.Contains(x.UserId.Value))
                .ToListAsync();
            foreach (var employee in employees)
                employee.UserId = null;

            var stockTransfers = await _context.StockTransfers
                .Where(x => x.UserId.HasValue && ids.Contains(x.UserId.Value))
                .ToListAsync();
            foreach (var transfer in stockTransfers)
                transfer.UserId = null;
        }

        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var columnLower = (column ?? "").Trim().ToLowerInvariant();
            var q = _context.Users.AsNoTracking()
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role);

            if (columnLower == "id")
            {
                var ids = await q.Select(u => u.UserId).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }
            if (columnLower == "username")
            {
                var list = await q.Select(u => u.UserName).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                if (!string.IsNullOrEmpty(searchTerm)) list = list.Where(s => s != null && s.ToLower().Contains(searchTerm)).ToList();
                return Json(list.Select(v => new { value = v ?? "", display = v ?? "" }));
            }
            if (columnLower == "roles")
            {
                var roleNames = await _context.Roles.AsNoTracking().Select(r => r.Name).Where(n => n != null).Distinct().OrderBy(x => x).Take(200).ToListAsync();
                var list = new List<string>(roleNames!);
                list.Insert(0, "لا يوجد دور");
                if (!string.IsNullOrEmpty(searchTerm))
                    list = list.Where(s => s != null && s.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }
            if (columnLower == "active" || columnLower == "isactive")
            {
                return Json(new[] { new { value = "true", display = "نشط" }, new { value = "false", display = "موقوف" } });
            }
            if (columnLower == "created" || columnLower == "createdat")
            {
                var list = await q.Select(u => u.CreatedAt).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd HH:mm"), display = d.ToString("yyyy-MM-dd HH:mm") }));
            }
            if (columnLower == "updated" || columnLower == "updatedat")
            {
                var list = await q.Where(u => u.UpdatedAt.HasValue).Select(u => u.UpdatedAt!.Value).Distinct().OrderByDescending(x => x).Take(300).ToListAsync();
                return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd HH:mm"), display = d.ToString("yyyy-MM-dd HH:mm") }));
            }
            return Json(Array.Empty<object>());
        }







        // =========================================================
        // Index — عرض قائمة المستخدمين (نظام القوائم الموحد)
        // =========================================================
        [RequirePermission("Users.Index")]
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy = "all",
            string? searchMode = "contains",
            string? sort = "UserName",
            string? dir = "asc",
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,   // من كود (UserId)
            int? toCode = null,     // إلى كود
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_username = null,
            string? filterCol_roles = null,
            string? filterCol_active = null,
            string? filterCol_created = null,
            string? filterCol_updated = null,
            int page = 1,
            int pageSize = 10)
        {
            // تجنّب أخذ قيمة قديمة عند تكرار المعامل في الـ Query (مثل pageSize من فورم آخر)
            var pageSizeQuery = Request.Query["pageSize"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageSizeQuery) && int.TryParse(pageSizeQuery, out var psVal))
                pageSize = psVal;
            if (Request.Query.ContainsKey("search"))
                search = Request.Query["search"].LastOrDefault();
            if (Request.Query.ContainsKey("searchBy"))
                searchBy = Request.Query["searchBy"].LastOrDefault();
            if (Request.Query.ContainsKey("searchMode"))
                searchMode = Request.Query["searchMode"].LastOrDefault();

            if (page < 1) page = 1;
            if (pageSize < 0) pageSize = 10;
            if (pageSize > 0 && pageSize != 10 && pageSize != 25 && pageSize != 50 && pageSize != 100 && pageSize != 200)
                pageSize = 10;

            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm != "starts" && sm != "ends")
                sm = "contains";

            var (roleIds, includeNoRole) = await ResolveRoleFilterIdsAsync(filterCol_roles);

            var q = BuildQuery(
                search,
                searchBy,
                sm,
                sort,
                dir,
                useDateRange,
                fromDate,
                toDate,
                fromCode,
                toCode);

            q = ApplyColumnFilters(q, filterCol_id, filterCol_idExpr, filterCol_username, filterCol_roles, roleIds, includeNoRole, filterCol_active, filterCol_created, filterCol_updated);

            var totalCount = await q.CountAsync();
            var activeCount = await q.CountAsync(u => u.IsActive);
            var inactiveCount = totalCount - activeCount;

            var sortCol = sort ?? "UserName";
            var sortDesc = !string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase);
            var model = await PagedResult<User>.CreateAsync(
                q,
                page,
                pageSize,
                sortCol,
                sortDesc,
                search ?? "",
                searchBy ?? "all");

            model.UseDateRange = useDateRange;
            model.FromDate = fromDate;
            model.ToDate = toDate;

            ViewBag.Search = search ?? "";
            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.SearchMode = sm;
            ViewBag.Sort = sortCol;
            ViewBag.Dir = sortDesc ? "desc" : "asc";

            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;

            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_IdExpr = filterCol_idExpr;
            ViewBag.FilterCol_Username = filterCol_username;
            ViewBag.FilterCol_Roles = filterCol_roles;
            ViewBag.FilterCol_Active = filterCol_active;
            ViewBag.FilterCol_Created = filterCol_created;
            ViewBag.FilterCol_Updated = filterCol_updated;

            ViewBag.DateField = "CreatedAt";
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;

            ViewBag.TotalCount = totalCount;
            ViewBag.ActiveCount = activeCount;
            ViewBag.InactiveCount = inactiveCount;

            ViewBag.CanEdit = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Users", "Edit"));
            ViewBag.CanDelete = await _permissionService.HasPermissionAsync(PermissionCodes.Code("Users", "Delete"));

            return View(model);
        }







        // =========================================================
        // Details — عرض تفاصيل مستخدم واحد
        // =========================================================
        [RequirePermission("Users.Index")]
        [HttpGet]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
                return NotFound();

            var user = await _context.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == id);

            if (user == null)
                return NotFound();

            return View(user);
        }








        // =========================================================
        // Create — إضافة مستخدم جديد (GET)
        // =========================================================
        [RequirePermission("Users.Create")]
        [HttpGet]
        public IActionResult Create()
        {
            // فورم فارغ لإضافة مستخدم جديد
            var model = new User
            {
                IsActive = true,
                IsAdmin = false
            };

            return View(model); // Views/Users/Create.cshtml
        }








        // =========================================================
        // Create — إضافة مستخدم جديد (POST)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Users.Create")]
        public async Task<IActionResult> Create(User model)
        {
            // ======================================================
            // 1) تجهيز اسم الدخول + الاسم الظاهر
            // ======================================================

            if (!string.IsNullOrWhiteSpace(model.UserName))
            {
                // إزالة المسافات الزائدة من اسم الدخول
                model.UserName = model.UserName.Trim();        // متغير: اسم الدخول بعد التنظيف

                // جعل الاسم الظاهر = اسم الدخول دائماً
                // حتى لو الفورم لا يحتوي على حقل DisplayName
                model.DisplayName = model.UserName;            // متغير: الاسم الظاهر الداخلي
            }
            else
            {
                // لو اسم الدخول فاضي نرجّع خطأ في الموديل
                ModelState.AddModelError("UserName", "اسم الدخول مطلوب.");
            }

            // ======================================================
            // 2) التحقق من تكرار اسم الدخول (لو الموديل سليم حتى الآن)
            // ======================================================
            if (ModelState.IsValid)
            {
                bool userExists = await _context.Users
                    .AnyAsync(u => u.UserName == model.UserName);   // متغير: هل يوجد مستخدم بنفس اسم الدخول؟

                if (userExists)
                {
                    ModelState.AddModelError("UserName", "يوجد مستخدم آخر بنفس اسم الدخول.");
                }
            }

            // لو في أخطاء نرجع لنفس الفورم
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // ======================================================
            // 3) تعيين التواريخ الافتراضية
            // ======================================================
            model.CreatedAt = DateTime.Now;   // متغير: تاريخ إنشاء المستخدم
            model.UpdatedAt = null;          // متغير: لا يوجد تعديل بعد

            // عند الإنشاء: نحفظ كلمة السر بشكل مشفّر دائماً.
            if (string.IsNullOrWhiteSpace(model.PasswordHash))
            {
                ModelState.AddModelError("PasswordHash", "كلمة السر مطلوبة.");
                return View(model);
            }

            model.PasswordHash = PasswordHasher.HashPassword(model.PasswordHash.Trim());

            // ======================================================
            // 4) حفظ المستخدم في قاعدة البيانات
            // ======================================================
            _context.Users.Add(model);
            await _context.SaveChangesAsync();

            await ApplyNewUserDefaultsAsync(model.UserId);
            await _context.SaveChangesAsync();

            await _activityLogger.LogAsync(UserActionType.Create, "User", model.UserId, $"إنشاء مستخدم: {model.UserName}");

            TempData["Success"] = "تم إضافة المستخدم بنجاح.";
            return RedirectToAction(nameof(Index));
        }












        // =========================================================
        // Edit — تعديل مستخدم (GET)
        // =========================================================
        [RequirePermission("Users.Edit")]
        [HttpGet]
        public async Task<IActionResult> Edit(int id, string? returnUrl = null)
        {
            // التحقق من أن رقم المستخدم صحيح
            if (id <= 0)
                return BadRequest();                   // متغير: طلب غير صالح

            // جلب بيانات المستخدم للعرض في فورم التعديل
            var user = await _context.Users
                .AsNoTracking()                        // قراءة فقط (لا نحتاج تتبّع هنا)
                .FirstOrDefaultAsync(u => u.UserId == id);

            if (user == null)
                return NotFound();                     // متغير: المستخدم غير موجود

            ViewBag.ReturnUrl = returnUrl;
            return View(user); // Views/Users/Edit.cshtml
        }









        // =========================================================
        // Edit — تعديل مستخدم (POST)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Users.Edit")]
        public async Task<IActionResult> Edit(int id, User model, string? returnUrl = null)
        {
            // التأكد من تطابق المعرّف في الرابط مع الموديل
            if (id != model.UserId)
                return BadRequest();                  // متغير: طلب غير متطابق

            // عند التعديل: كلمة المرور اختيارية (فارغة = لا تغيير)، نزيل خطأ الـ Required إن وُجد
            if (string.IsNullOrWhiteSpace(model.PasswordHash))
                ModelState.Remove("PasswordHash");

            // ======================================================
            // 1) تجهيز اسم الدخول + الاسم الظاهر
            // ======================================================

            if (!string.IsNullOrWhiteSpace(model.UserName))
            {
                // إزالة المسافات الزائدة من اسم الدخول
                model.UserName = model.UserName.Trim();      // متغير: اسم الدخول بعد التنظيف

                // جعل الاسم الظاهر = اسم الدخول دائماً
                model.DisplayName = model.UserName;          // متغير: الاسم الظاهر الداخلي
            }
            else
            {
                ModelState.AddModelError("UserName", "اسم الدخول مطلوب.");
            }

            // ======================================================
            // 2) التحقق من عدم تكرار اسم الدخول مع مستخدم آخر
            // ======================================================
            if (ModelState.IsValid)
            {
                bool exists = await _context.Users
                    .AnyAsync(u =>
                        u.UserId != model.UserId &&          // متغير: نستبعد نفس المستخدم
                        u.UserName == model.UserName);       // متغير: نفس اسم الدخول الجديد؟

                if (exists)
                {
                    ModelState.AddModelError("UserName", "يوجد مستخدم آخر بنفس اسم الدخول.");
                }
            }

            // لو في أخطاء نرجع لنفس الفورم مع رسائل الخطأ
            if (!ModelState.IsValid)
            {
                ViewBag.ReturnUrl = returnUrl;
                return View(model);
            }

            // ======================================================
            // 3) جلب المستخدم الأصلي من قاعدة البيانات
            // ======================================================
            var dbUser = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == id);

            if (dbUser == null)
                return NotFound();                    // متغير: المستخدم لم يعد موجوداً

            // نسجل في سجل النشاط فقط القيم المهمة للمستخدم (اسم الدخول + أدمن؟ + نشط؟)
            var oldValues = System.Text.Json.JsonSerializer.Serialize(new
            {
                dbUser.UserName,
                dbUser.PortalRole,
                dbUser.IsAdmin,
                dbUser.IsActive
            });
            // ======================================================
            // 4) تحديث خصائص المستخدم
            // ======================================================

            dbUser.UserName = model.UserName;      // متغير: اسم الدخول
            dbUser.DisplayName = model.DisplayName;   // متغير: نحتفظ به مساويًا لاسم الدخول (للتقارير)
            dbUser.Email = model.Email;               // متغير: البريد الإلكتروني (اختياري)
            dbUser.PortalRole = model.PortalRole;
            dbUser.IsActive = model.IsActive;      // متغير: حالة التفعيل

            // لو تم إدخال كلمة مرور جديدة نحفظها مشفّرة.
            if (!string.IsNullOrWhiteSpace(model.PasswordHash))
            {
                dbUser.PasswordHash = PasswordHasher.HashPassword(model.PasswordHash.Trim());    // متغير: كلمة المرور الجديدة
            }

            dbUser.UpdatedAt = DateTime.Now;         // متغير: وقت آخر تعديل

            // ======================================================
            // 5) حفظ التغييرات في قاعدة البيانات
            // ======================================================
            await _context.SaveChangesAsync();

            var newValues = System.Text.Json.JsonSerializer.Serialize(new
            {
                dbUser.UserName,
                dbUser.PortalRole,
                dbUser.IsAdmin,
                dbUser.IsActive
            });
            await _activityLogger.LogAsync(UserActionType.Edit, "User", model.UserId, $"تعديل مستخدم: {model.UserName}", oldValues, newValues);

            TempData["Success"] = "تم حفظ تعديلات المستخدم بنجاح.";
            return RedirectToAction(nameof(Index));
        }















        // =========================================================
        // Delete — تأكيد الحذف (GET)
        // =========================================================
        [RequirePermission("Users.Delete")]
        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            if (id <= 0)
                return BadRequest();

            var user = await _context.Users
                                     .AsNoTracking()
                                     .FirstOrDefaultAsync(u => u.UserId == id);

            if (user == null)
                return NotFound();

            return View(user); // Views/Users/Delete.cshtml
        }












        // =========================================================
        // DeleteConfirmed — تنفيذ الحذف (POST)
        // =========================================================
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [RequirePermission("Users.Delete")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                TempData["Error"] = "المستخدم غير موجود.";
                return RedirectToAction(nameof(Index));
            }

            // ممكن لاحقاً نمنع حذف الـ Admin الرئيسي هنا لو حابب.
            try
            {
                var oldValues = System.Text.Json.JsonSerializer.Serialize(new { user.UserName, user.DisplayName, user.Email });
                await PrepareUsersForDeletionAsync(new[] { id });
                _context.Users.Remove(user);
                await _context.SaveChangesAsync();

                await _activityLogger.LogAsync(UserActionType.Delete, "User", id, $"حذف مستخدم: {user.UserName}", oldValues: oldValues);

                TempData["Success"] = "تم حذف المستخدم بنجاح.";
            }
            catch
            {
                TempData["Error"] = "تعذر حذف المستخدم، ربما لوجود ارتباطات أخرى.";
            }

            return RedirectToAction(nameof(Index));
        }











        // =========================================================
        // Export — تصدير قائمة المستخدمين إلى CSV (Excel)
        // =========================================================
        /// <summary>
        /// تصدير قائمة المستخدمين إلى ملف Excel
        /// بنفس فلاتر البحث الموجودة في شاشة القائمة.
        /// </summary>
        [RequirePermission("Users.Export")]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy,
            string? searchMode,
            string? sort,
            string? dir,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? dateField = "CreatedAt",
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_username = null,
            string? filterCol_roles = null,
            string? filterCol_active = null,
            string? filterCol_created = null,
            string? filterCol_updated = null)
        {
            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm != "starts" && sm != "ends")
                sm = "contains";

            var (roleIds, includeNoRole) = await ResolveRoleFilterIdsAsync(filterCol_roles);

            var q = BuildQuery(search ?? "", searchBy ?? "all", sm, sort ?? "UserName", dir ?? "asc",
                useDateRange, fromDate, toDate, fromCode, toCode);
            q = ApplyColumnFilters(q, filterCol_id, filterCol_idExpr, filterCol_username, filterCol_roles, roleIds, includeNoRole, filterCol_active, filterCol_created, filterCol_updated);
            var users = await q.ToListAsync();

            // 7) إنشاء ملف Excel في الذاكرة باستخدام ClosedXML
            using var workbook = new XLWorkbook();            // مصنف جديد
            var worksheet = workbook.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("المستخدمون"));

            int row = 1; // رقم الصف الحالي (نبدأ بالهيدر)

            // عناوين الأعمدة (الهيدر)
            worksheet.Cell(row, 1).Value = "رقم المستخدم";
            worksheet.Cell(row, 2).Value = "اسم الدخول";
            worksheet.Cell(row, 3).Value = "الأدوار";
            worksheet.Cell(row, 4).Value = "البريد الإلكتروني";
            worksheet.Cell(row, 5).Value = "نشط؟";
            worksheet.Cell(row, 6).Value = "تاريخ الإنشاء";
            worksheet.Cell(row, 7).Value = "آخر تعديل";
            worksheet.Cell(row, 8).Value = "آخر تسجيل دخول";

            // تنسيق الهيدر (خط عريض)
            var headerRange = worksheet.Range(row, 1, row, 8);
            headerRange.Style.Font.Bold = true;

            // 8) كتابة بيانات المستخدمين سطرًا بسطر
            foreach (var u in users)
            {
                row++;

                worksheet.Cell(row, 1).Value = u.UserId;                         // رقم المستخدم
                worksheet.Cell(row, 2).Value = u.UserName;                       // اسم الدخول

                // الأدوار (نستخدم RolesSummary من الموديل)
                var rolesText = !string.IsNullOrWhiteSpace(u.RolesSummary)
                    ? u.RolesSummary
                    : "لا يوجد دور";
                worksheet.Cell(row, 3).Value = rolesText;                        // الأدوار

                worksheet.Cell(row, 4).Value = u.Email;                          // البريد
                worksheet.Cell(row, 5).Value = u.IsActive ? "نشط" : "غير نشط";  // حالة التفعيل
                worksheet.Cell(row, 6).Value = u.CreatedAt;                      // تاريخ الإنشاء
                worksheet.Cell(row, 7).Value = u.UpdatedAt;                      // آخر تعديل
                worksheet.Cell(row, 8).Value = u.LastLoginAt;                    // آخر دخول
            }

            // 9) ضبط عرض الأعمدة تلقائيًا حسب المحتوى
            worksheet.Columns().AdjustToContents();

            // 10) حفظ الملف في MemoryStream وإرجاعه للمستخدم
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            stream.Position = 0;

            var fileName = ExcelExportNaming.ArabicTimestampedFileName("المستخدمون", ".xlsx");
            const string contentType =
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

            return File(stream.ToArray(), contentType, fileName);
        }

















        // =========================================================
        // BulkDelete — حذف مجموعة من المستخدمين المحددين
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Users.Delete")]
        public async Task<IActionResult> BulkDelete([FromForm] int[]? ids)
        {
            var idList = new List<int>();
            if (ids != null && ids.Length > 0)
                idList.AddRange(ids);
            else if (Request.HasFormContentType && Request.Form["ids"].Count > 0)
            {
                foreach (var s in Request.Form["ids"])
                {
                    if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
                        idList.Add(pid);
                }
            }
            idList = idList.Distinct().ToList();

            if (idList.Count == 0)
            {
                TempData["Error"] = "لم يتم اختيار أى مستخدم للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var users = await _context.Users
                                      .Where(u => idList.Contains(u.UserId))
                                      .ToListAsync();

            if (users.Count == 0)
            {
                TempData["Error"] = "لم يتم العثور على المستخدمين المحددين.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                await PrepareUsersForDeletionAsync(idList);
                _context.Users.RemoveRange(users);
                await _context.SaveChangesAsync();
                TempData["Success"] = $"تم حذف {users.Count} من المستخدمين المحددين.";
            }
            catch
            {
                TempData["Error"] = "لا يمكن حذف بعض المستخدمين بسبب ارتباطهم ببيانات أخرى.";
            }

            return RedirectToAction(nameof(Index));
        }














        // =========================================================
        // DeleteAll — حذف جميع المستخدمين (يفضل للبيئة التجريبية فقط)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("Users.Delete")]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _context.Users.ToListAsync();

            if (all.Count == 0)
            {
                TempData["Error"] = "لا توجد مستخدمين لحذفهم.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                await PrepareUsersForDeletionAsync(all.Select(x => x.UserId).ToList());
                _context.Users.RemoveRange(all);
                await _context.SaveChangesAsync();
                TempData["Success"] = "تم حذف جميع المستخدمين.";
            }
            catch
            {
                TempData["Error"] = "لا يمكن حذف جميع المستخدمين بسبب وجود ارتباطات أخرى.";
            }

            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// افتراضيات لكل مستخدم جديد: صلاحيات إضافية (بوابة Global.Open ثم الصفحة الرئيسية + مبيعاتي الشخصية).
        /// بدون Global.Open لا يمرّ التحقق من Dashboard.Sales / Home.Index (انظر GlobalPermissionGates + PermissionService).
        /// وقيد «الحسابات المسموح رؤيتها» على حساب العملاء 1103 إن وُجد في الدليل.
        /// </summary>
        private async Task ApplyNewUserDefaultsAsync(int userId)
        {
            if (userId <= 0) return;

            string[] extraPermissionCodes = { "Global.Open", "Home.Index", "Dashboard.Sales" };
            foreach (var code in extraPermissionCodes)
            {
                var perm = await _context.Permissions.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.IsActive && p.Code == code);
                if (perm == null) continue;

                var exists = await _context.UserExtraPermissions
                    .AnyAsync(x => x.UserId == userId && x.PermissionId == perm.PermissionId);
                if (!exists)
                {
                    _context.UserExtraPermissions.Add(new UserExtraPermissions
                    {
                        UserId = userId,
                        PermissionId = perm.PermissionId,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            const string defaultCustomerAccountCode = "1103";
            var accountId = await _context.Accounts.AsNoTracking()
                .Where(a => a.AccountCode == defaultCustomerAccountCode)
                .Select(a => a.AccountId)
                .FirstOrDefaultAsync();

            if (accountId > 0)
            {
                var hasOverride = await _context.UserAccountVisibilityOverrides
                    .AnyAsync(x => x.UserId == userId && x.AccountId == accountId);
                if (!hasOverride)
                {
                    _context.UserAccountVisibilityOverrides.Add(new UserAccountVisibilityOverride
                    {
                        UserId = userId,
                        AccountId = accountId,
                        IsAllowed = true,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
        }
    }
}
