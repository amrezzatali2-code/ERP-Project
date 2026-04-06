using System;                                     // متغيرات التاريخ DateTime
using System.Collections.Generic;                 // القوائم List
using System.Linq;                                // أوامر LINQ
using System.Text;                                // StringBuilder للتصدير
using System.Threading.Tasks;                     // Task و async
using ERP.Data;                                   // AppDbContext
using ERP.Filters;
using ERP.Infrastructure;                         // PagedResult لتقسيم الصفحات
using ERP.Models;                                 // الموديلات UserRole, User, Role
using ERP.Security;
using ERP.ViewModels;                             // RolePermissionEditItem
using Microsoft.AspNetCore.Mvc;                   // أساس الكنترولر
using Microsoft.AspNetCore.Mvc.Rendering;         // SelectList
using Microsoft.EntityFrameworkCore;              // Include, AsNoTracking, ToListAsync
using ClosedXML.Excel;                      // لتصدير Excel
using System.IO;

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إدارة ربط المستخدمين بالأدوار (UserRoles).
    /// قائمة الفهرس: كل مستخدم مسجّل يظهر؛ من بلا أدوار صف واحد (عمود الدور: «ليس له دور»).
    /// كل سطر بربط فعلي = دور واحد لمستخدم معيّن.
    /// </summary>
    public class UserRolesController : Controller
    {
        private readonly AppDbContext _context;   // كائن الاتصال بقاعدة البيانات

        public UserRolesController(AppDbContext context)
        {
            _context = context;
        }







        // ========= دوال مساعدة: الفلاتر + القوائم المنسدلة =========

        /// <summary>
        /// كل المستخدمين مع تفريع على أدوارهم؛ من بلا أدوار يظهر صف واحد (UserRoleId = null).
        /// </summary>
        private IQueryable<UserRoleListRow> BuildListQuery()
        {
            const int roleJoinSentinel = -1;
            return from u in _context.Users.AsNoTracking()
                   from ur in _context.UserRoles.AsNoTracking().Where(r => r.UserId == u.UserId).DefaultIfEmpty()
                   join r in _context.Roles.AsNoTracking()
                       on (ur != null ? ur.RoleId : roleJoinSentinel) equals r.RoleId into rJoin
                   from r in rJoin.DefaultIfEmpty()
                   select new UserRoleListRow
                   {
                       UserRoleId = ur != null ? ur.Id : (int?)null,
                       UserId = u.UserId,
                       UserName = u.UserName,
                       DisplayName = u.DisplayName ?? string.Empty,
                       RoleId = ur != null ? ur.RoleId : (int?)null,
                       RoleName = r != null ? r.Name : null,
                       RoleDescription = r != null ? r.Description : null,
                       IsPrimary = ur != null && ur.IsPrimary,
                       AssignedAt = ur != null ? ur.AssignedAt : (DateTime?)null
                   };
        }

        /// <summary>
        /// بحث + فلتر كود السطر (UserRoleId) + تاريخ الإسناد على صفوف القائمة الموحّدة.
        /// searchMode: starts | contains | ends (للحقول النصية؛ الافتراضي contains).
        /// </summary>
        private static IQueryable<UserRoleListRow> ApplyListFilters(
            IQueryable<UserRoleListRow> query,
            string? search,
            string? searchBy,
            string? searchMode,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            int? fromCode,
            int? toCode)
        {
            var sm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (sm != "starts" && sm != "ends")
                sm = "contains";
            if (fromCode.HasValue)
                query = query.Where(x => x.UserRoleId.HasValue && x.UserRoleId.Value >= fromCode.Value);

            if (toCode.HasValue)
                query = query.Where(x => x.UserRoleId.HasValue && x.UserRoleId.Value <= toCode.Value);

            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                query = query.Where(x =>
                    x.AssignedAt.HasValue &&
                    x.AssignedAt.Value >= fromDate.Value &&
                    x.AssignedAt.Value <= toDate.Value);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = search.Trim();
                string mode = (searchBy ?? "all").ToLowerInvariant();

                switch (mode)
                {
                    case "userid":
                        if (int.TryParse(term, out int uid))
                            query = query.Where(x => x.UserId == uid);
                        else
                            query = query.Where(x => false);
                        break;

                    case "username":
                        query = sm == "starts"
                            ? query.Where(x => x.UserName.StartsWith(term))
                            : sm == "ends"
                                ? query.Where(x => x.UserName.EndsWith(term))
                                : query.Where(x => x.UserName.Contains(term));
                        break;

                    case "display":
                        query = sm == "starts"
                            ? query.Where(x => (x.DisplayName ?? "").StartsWith(term))
                            : sm == "ends"
                                ? query.Where(x => (x.DisplayName ?? "").EndsWith(term))
                                : query.Where(x => (x.DisplayName ?? "").Contains(term));
                        break;

                    case "roleid":
                        if (int.TryParse(term, out int rid))
                            query = query.Where(x => x.RoleId == rid);
                        else
                            query = query.Where(x => false);
                        break;

                    case "rolename":
                    case "role":
                        if (string.Equals(term, UserRoleListRow.NoRoleDisplay, StringComparison.Ordinal) ||
                            term == "ليس" || term == "بدون")
                            query = query.Where(x => x.RoleName == null);
                        else
                            query = query.Where(x =>
                                x.RoleName != null &&
                                (x.RoleName.Contains(term) ||
                                 (x.RoleDescription ?? "").Contains(term)));
                        break;

                    case "primary":
                        query = query.Where(x => x.IsPrimary);
                        break;

                    case "id":
                        if (int.TryParse(term, out int idVal))
                            query = query.Where(x => x.UserRoleId == idVal);
                        else
                            query = query.Where(x => false);
                        break;

                    default:
                        if (sm == "starts")
                            query = query.Where(x =>
                                (x.UserRoleId.HasValue && x.UserRoleId.Value.ToString().StartsWith(term)) ||
                                x.UserId.ToString().StartsWith(term) ||
                                (x.RoleId.HasValue && x.RoleId.Value.ToString().StartsWith(term)) ||
                                x.UserName.StartsWith(term) ||
                                (x.DisplayName ?? "").StartsWith(term) ||
                                (x.RoleName != null &&
                                 (x.RoleName.StartsWith(term) ||
                                  (x.RoleDescription ?? "").StartsWith(term))) ||
                                (x.RoleName == null && UserRoleListRow.NoRoleDisplay.StartsWith(term)));
                        else if (sm == "ends")
                            query = query.Where(x =>
                                (x.UserRoleId.HasValue && x.UserRoleId.Value.ToString().EndsWith(term)) ||
                                x.UserId.ToString().EndsWith(term) ||
                                (x.RoleId.HasValue && x.RoleId.Value.ToString().EndsWith(term)) ||
                                x.UserName.EndsWith(term) ||
                                (x.DisplayName ?? "").EndsWith(term) ||
                                (x.RoleName != null &&
                                 (x.RoleName.EndsWith(term) ||
                                  (x.RoleDescription ?? "").EndsWith(term))) ||
                                (x.RoleName == null && UserRoleListRow.NoRoleDisplay.EndsWith(term)));
                        else
                            query = query.Where(x =>
                                (x.UserRoleId.HasValue && x.UserRoleId.Value.ToString().Contains(term)) ||
                                x.UserId.ToString().Contains(term) ||
                                (x.RoleId.HasValue && x.RoleId.Value.ToString().Contains(term)) ||
                                x.UserName.Contains(term) ||
                                (x.DisplayName ?? "").Contains(term) ||
                                (x.RoleName != null &&
                                 (x.RoleName.Contains(term) ||
                                  (x.RoleDescription ?? "").Contains(term))) ||
                                (x.RoleName == null && UserRoleListRow.NoRoleDisplay.Contains(term)));
                        break;
                }
            }

            return query;
        }

        private static readonly char[] _filterSep = new[] { '|', ',', ';' };

        private static IQueryable<UserRoleListRow> ApplyListColumnFilters(
            IQueryable<UserRoleListRow> query,
            string? filterCol_id,
            string? filterCol_idExpr,
            string? filterCol_user,
            string? filterCol_role,
            string? filterCol_primary,
            string? filterCol_assigned)
        {
            if (!string.IsNullOrWhiteSpace(filterCol_id))
            {
                var ids = filterCol_id.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x.Trim(), out var v) ? v : (int?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (ids.Count > 0)
                    query = query.Where(x => x.UserRoleId.HasValue && ids.Contains(x.UserRoleId.Value));
            }
            else if (!string.IsNullOrWhiteSpace(filterCol_idExpr))
            {
                var expr = filterCol_idExpr.Trim();
                if (expr.StartsWith("<=") && expr.Length > 2 && int.TryParse(expr.Substring(2), out var max))
                    query = query.Where(x => x.UserRoleId.HasValue && x.UserRoleId.Value <= max);
                else if (expr.StartsWith(">=") && expr.Length > 2 && int.TryParse(expr.Substring(2), out var min))
                    query = query.Where(x => x.UserRoleId.HasValue && x.UserRoleId.Value >= min);
                else if (expr.StartsWith("<") && !expr.StartsWith("<=") && expr.Length > 1 && int.TryParse(expr.Substring(1), out var max2))
                    query = query.Where(x => x.UserRoleId.HasValue && x.UserRoleId.Value < max2);
                else if (expr.StartsWith(">") && !expr.StartsWith(">=") && expr.Length > 1 && int.TryParse(expr.Substring(1), out var min2))
                    query = query.Where(x => x.UserRoleId.HasValue && x.UserRoleId.Value > min2);
                else if ((expr.Contains(':') || expr.Contains('-')) && !expr.StartsWith("-"))
                {
                    var sep = expr.Contains(':') ? ':' : '-';
                    var parts = expr.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out var fromId) && int.TryParse(parts[1].Trim(), out var toId))
                    {
                        if (fromId > toId) (fromId, toId) = (toId, fromId);
                        query = query.Where(x => x.UserRoleId.HasValue && x.UserRoleId.Value >= fromId && x.UserRoleId.Value <= toId);
                    }
                }
                else if (int.TryParse(expr, out var exactId))
                    query = query.Where(x => x.UserRoleId.HasValue && x.UserRoleId.Value == exactId);
            }

            if (!string.IsNullOrWhiteSpace(filterCol_user))
            {
                var vals = filterCol_user.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                    query = query.Where(x => vals.Contains(x.UserName));
            }

            if (!string.IsNullOrWhiteSpace(filterCol_role))
            {
                var vals = filterCol_role.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (vals.Count > 0)
                {
                    var wantNoRole = vals.Any(v => v == UserRoleListRow.NoRoleDisplay);
                    var roleNames = vals.Where(v => v != UserRoleListRow.NoRoleDisplay).ToList();
                    if (wantNoRole && roleNames.Count > 0)
                    {
                        query = query.Where(x =>
                            x.RoleName == null ||
                            (x.RoleName != null && roleNames.Contains(x.RoleName)));
                    }
                    else if (wantNoRole)
                        query = query.Where(x => x.RoleName == null);
                    else
                        query = query.Where(x => x.RoleName != null && roleNames.Contains(x.RoleName));
                }
            }

            if (!string.IsNullOrWhiteSpace(filterCol_primary))
            {
                var vals = filterCol_primary.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim().ToLowerInvariant()).ToList();
                var wantTrue = vals.Any(v => v == "true" || v == "1" || v == "نعم");
                var wantFalse = vals.Any(v => v == "false" || v == "0" || v == "لا");
                if (wantTrue && !wantFalse) query = query.Where(x => x.IsPrimary);
                else if (wantFalse && !wantTrue) query = query.Where(x => !x.IsPrimary);
            }

            if (!string.IsNullOrWhiteSpace(filterCol_assigned))
            {
                var parts = filterCol_assigned.Split(_filterSep, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length >= 8).ToList();
                if (parts.Count > 0)
                {
                    var dates = new List<DateTime>();
                    foreach (var p in parts)
                        if (DateTime.TryParse(p, out var d)) dates.Add(d);
                    if (dates.Count > 0)
                        query = query.Where(x => x.AssignedAt.HasValue && dates.Contains(x.AssignedAt.Value));
                }
            }

            return query;
        }

        [HttpGet]
        public async Task<IActionResult> GetColumnValues(string column, string? search = null)
        {
            var searchTerm = (search ?? "").Trim().ToLowerInvariant();
            var columnLower = (column ?? "").Trim().ToLowerInvariant();
            var q = BuildListQuery();

            if (columnLower == "id")
            {
                var ids = await q.Where(x => x.UserRoleId.HasValue)
                    .Select(x => x.UserRoleId!.Value)
                    .Distinct()
                    .OrderBy(x => x)
                    .Take(500)
                    .ToListAsync();
                return Json(ids.Select(v => new { value = v.ToString(), display = v.ToString() }));
            }

            if (columnLower == "user" || columnLower == "username")
            {
                var list = await q.Select(x => x.UserName).Distinct().OrderBy(x => x).Take(500).ToListAsync();
                IEnumerable<string> filtered = list;
                if (!string.IsNullOrEmpty(searchTerm))
                    filtered = list.Where(s => s.ToLowerInvariant().Contains(searchTerm));
                return Json(filtered.Select(v => new { value = v, display = v }));
            }

            if (columnLower == "role" || columnLower == "rolename")
            {
                var hasNoRole = await q.AnyAsync(x => x.RoleName == null);
                var fromRolesTable = await _context.Roles.AsNoTracking()
                    .Where(r => r.Name != null)
                    .Select(r => r.Name!)
                    .Distinct()
                    .OrderBy(x => x)
                    .Take(500)
                    .ToListAsync();
                var list = fromRolesTable.ToList();
                if (hasNoRole)
                    list.Insert(0, UserRoleListRow.NoRoleDisplay);
                if (!string.IsNullOrEmpty(searchTerm))
                    list = list.Where(s => s != null && s.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)).ToList();
                return Json(list.Select(v => new { value = v, display = v }));
            }

            if (columnLower == "primary" || columnLower == "isprimary")
                return Json(new[] { new { value = "true", display = "\u0646\u0639\u0645" }, new { value = "false", display = "\u0644\u0627" } });

            if (columnLower == "assigned" || columnLower == "assignedat")
            {
                var list = await q.Where(x => x.AssignedAt.HasValue)
                    .Select(x => x.AssignedAt!.Value)
                    .Distinct()
                    .OrderByDescending(x => x)
                    .Take(300)
                    .ToListAsync();
                return Json(list.Select(d => new { value = d.ToString("yyyy-MM-dd HH:mm"), display = d.ToString("yyyy-MM-dd HH:mm") }));
            }

            return Json(Array.Empty<object>());
        }






        /// <summary>
        /// تجهيز القوائم المنسدلة للمستخدمين والأدوار للفورمات.
        /// </summary>
        private async Task PopulateLookupsAsync(int? selectedUserId = null, int? selectedRoleId = null)
        {
            // قائمة المستخدمين (اسم واحد بدون تكرار)
            var users = await _context.Users
                .OrderBy(u => u.UserName)
                .Select(u => new
                {
                    u.UserId,
                    Text = u.DisplayName ?? u.UserName
                })
                .ToListAsync();

            // قائمة الأدوار
            var roles = await _context.Roles
                .OrderBy(r => r.Name)
                .Select(r => new
                {
                    r.RoleId,
                    Text = r.Name + " (" + r.RoleId + ")"
                })
                .ToListAsync();

            ViewBag.UserId = new SelectList(users, "UserId", "Text", selectedUserId);
            ViewBag.RoleId = new SelectList(roles, "RoleId", "Text", selectedRoleId);
        }








        // ========= INDEX =========

        /// <summary>
        /// قائمة ربط المستخدمين بالأدوار مع:
        /// بحث + ترتيب + تقسيم صفحات + فلاتر، بنظام القوائم الموحد.
        /// </summary>
        [RequirePermission("UserRoles.Index")]
        public async Task<IActionResult> Index(
            string? search,
            string? searchBy,
            string? searchMode = "contains",
            string? sort = null,
            string? dir = null,
            int page = 1,
            int pageSize = 10,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? dateField = "AssignedAt",
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_user = null,
            string? filterCol_role = null,
            string? filterCol_primary = null,
            string? filterCol_assigned = null)
        {
            var pageSizeQuery = Request.Query["pageSize"].LastOrDefault();
            if (!string.IsNullOrEmpty(pageSizeQuery) && int.TryParse(pageSizeQuery, out var psVal))
                pageSize = psVal;

            const int defaultPageSize = 10;
            var allowedSizes = new HashSet<int> { 10, 25, 50, 100, 200 };
            if (pageSize < 0)
                pageSize = defaultPageSize;
            else if (pageSize > 0 && !allowedSizes.Contains(pageSize))
                pageSize = defaultPageSize;

            var smNorm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (smNorm != "starts" && smNorm != "ends")
                smNorm = "contains";

            IQueryable<UserRoleListRow> query = BuildListQuery();
            query = ApplyListFilters(query, search, searchBy, smNorm, useDateRange, fromDate, toDate, fromCode, toCode);
            query = ApplyListColumnFilters(query, filterCol_id, filterCol_idExpr, filterCol_user, filterCol_role, filterCol_primary, filterCol_assigned);

            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            sort ??= "UserName";

            query = sort switch
            {
                "UserId" => desc
                    ? query.OrderByDescending(x => x.UserId).ThenBy(x => x.UserRoleId)
                    : query.OrderBy(x => x.UserId).ThenBy(x => x.UserRoleId),

                "UserName" => desc
                    ? query.OrderByDescending(x => x.UserName).ThenBy(x => x.UserRoleId)
                    : query.OrderBy(x => x.UserName).ThenBy(x => x.UserRoleId),

                "RoleId" => desc
                    ? query.OrderByDescending(x => x.RoleId).ThenBy(x => x.UserRoleId)
                    : query.OrderBy(x => x.RoleId).ThenBy(x => x.UserRoleId),

                "RoleName" => desc
                    ? query.OrderBy(x => x.RoleName == null).ThenByDescending(x => x.RoleName)
                    : query.OrderBy(x => x.RoleName == null).ThenBy(x => x.RoleName),

                "IsPrimary" => desc
                    ? query.OrderByDescending(x => x.IsPrimary).ThenBy(x => x.UserName)
                    : query.OrderBy(x => x.IsPrimary).ThenBy(x => x.UserName),

                "AssignedAt" => desc
                    ? query.OrderBy(x => x.AssignedAt == null).ThenByDescending(x => x.AssignedAt)
                    : query.OrderBy(x => x.AssignedAt == null).ThenBy(x => x.AssignedAt),

                "Id" => desc
                    ? query.OrderByDescending(x => x.UserRoleId == null).ThenByDescending(x => x.UserRoleId)
                    : query.OrderBy(x => x.UserRoleId == null).ThenBy(x => x.UserRoleId),

                _ => desc
                    ? query.OrderByDescending(x => x.UserRoleId == null).ThenByDescending(x => x.UserRoleId)
                    : query.OrderBy(x => x.UserRoleId == null).ThenBy(x => x.UserRoleId)
            };

            int totalCount = await query.CountAsync();
            page = Math.Max(1, page);

            List<UserRoleListRow> items;
            int modelPageSize = pageSize;

            if (pageSize == 0)
            {
                int effectiveTake = totalCount == 0 ? defaultPageSize : Math.Min(totalCount, 100_000);
                page = 1;
                items = await query.Skip(0).Take(effectiveTake).ToListAsync();
                modelPageSize = 0;
            }
            else
            {
                int maxPage = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
                if (page > maxPage)
                    page = maxPage;
                int skip = (page - 1) * pageSize;
                items = await query.Skip(skip).Take(pageSize).ToListAsync();
            }

            var model = new PagedResult<UserRoleListRow>(items, page, modelPageSize, totalCount)
            {
                Search = search,
                SortColumn = sort,
                SortDescending = desc,
                UseDateRange = useDateRange,
                FromDate = fromDate,
                ToDate = toDate
            };

            int usersCount = await query.Select(x => x.UserId).Distinct().CountAsync();
            int assignedCount = await query.CountAsync(x => x.UserRoleId.HasValue);
            int primaryCount = await query.CountAsync(x => x.UserRoleId.HasValue && x.IsPrimary);

            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.SearchMode = smNorm;
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.DateField = dateField ?? "AssignedAt";
            ViewBag.FilterCol_Id = filterCol_id;
            ViewBag.FilterCol_IdExpr = filterCol_idExpr;
            ViewBag.FilterCol_User = filterCol_user;
            ViewBag.FilterCol_Role = filterCol_role;
            ViewBag.FilterCol_Primary = filterCol_primary;
            ViewBag.FilterCol_Assigned = filterCol_assigned;
            ViewBag.UsersCount = usersCount;
            ViewBag.AssignedCount = assignedCount;
            ViewBag.PrimaryCount = primaryCount;

            return View(model);
        }





        // ========= DETAILS =========

        /// <summary>
        /// عرض تفاصيل ربط مستخدم بدور معيّن.
        /// </summary>
        public async Task<IActionResult> Details(int id)
        {
            var item = await _context.UserRoles
                .AsNoTracking()
                .Include(x => x.User)
                .Include(x => x.Role)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (item == null)
                return NotFound();

            return View(item);
        }








        // ========= CREATE =========

        // GET: UserRoles/Create?userId=...
        public async Task<IActionResult> Create(int? userId = null)
        {
            await PopulateLookupsAsync(userId, null);
            return View();
        }

        // POST: UserRoles/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            [Bind("UserId,RoleId,IsPrimary")] UserRole item,
            int[]? selectedPermissionIds,
            List<int>? RoleAccountIds,
            string? SelectedRoleAccountIds = null)
        {
            ModelState.Remove(nameof(UserRole.User));
            ModelState.Remove(nameof(UserRole.Role));

            if (item.UserId <= 0)
                ModelState.AddModelError("UserId", "من فضلك اختر مستخدم.");
            if (item.RoleId <= 0)
                ModelState.AddModelError("RoleId", "من فضلك اختر دور.");

            bool exists = await _context.UserRoles
                .AnyAsync(x => x.UserId == item.UserId && x.RoleId == item.RoleId);
            if (exists)
                ModelState.AddModelError(string.Empty, "هذا المستخدم لديه بالفعل نفس الدور.");

            if (!ModelState.IsValid)
            {
                await PopulateLookupsAsync(item.UserId, item.RoleId);
                return View(item);
            }

            var rolePermIds = await _context.RolePermissions
                .Where(rp => rp.RoleId == item.RoleId && rp.IsAllowed)
                .Select(rp => rp.PermissionId)
                .ToListAsync();
            // لا تعتبر المصفوفة الفارغة «اختياراً صريحاً»: الـ model binder قد يربطها كـ [] بدل null،
            // فيُحسب allowed فارغاً ويُسجَّل رفض لكل صلاحيات الدور فيُمنع المستخدم رغم ربط الدور.
            var hasExplicitPermissionSelection = selectedPermissionIds != null && selectedPermissionIds.Length > 0;
            item.AssignedAt = DateTime.UtcNow;
            var allowed = hasExplicitPermissionSelection
                ? new HashSet<int>(selectedPermissionIds!)
                : new HashSet<int>(rolePermIds);
            // دور افتراضي = صح فقط عندما صلاحيات المستخدم (المحددة هنا) = صلاحيات الدور بالضبط
            item.IsPrimary = allowed.SetEquals(rolePermIds);

            _context.Add(item);
            await _context.SaveChangesAsync();

            // حفظ استثناءات الصلاحيات (تعديلات المستخدم)
            foreach (var permId in rolePermIds.Where(id => !allowed.Contains(id)))
            {
                if (!await _context.UserDeniedPermissions.AnyAsync(x => x.UserId == item.UserId && x.PermissionId == permId))
                {
                    _context.UserDeniedPermissions.Add(new UserDeniedPermission
                    {
                        UserId = item.UserId,
                        PermissionId = permId,
                        IsAllowed = false
                    });
                }
            }
            var toRemoveDenied = await _context.UserDeniedPermissions
                .Where(x => x.UserId == item.UserId && rolePermIds.Contains(x.PermissionId) && allowed.Contains(x.PermissionId))
                .ToListAsync();
            _context.UserDeniedPermissions.RemoveRange(toRemoveDenied);

            var allPermIds = await _context.Permissions.Where(p => p.IsActive).Select(p => p.PermissionId).ToListAsync();
            foreach (var permId in allPermIds.Where(id => allowed.Contains(id) && !rolePermIds.Contains(id)))
            {
                if (!await _context.UserExtraPermissions.AnyAsync(x => x.UserId == item.UserId && x.PermissionId == permId))
                {
                    _context.UserExtraPermissions.Add(new UserExtraPermissions
                    {
                        UserId = item.UserId,
                        PermissionId = permId
                    });
                }
            }
            var toRemoveExtra = await _context.UserExtraPermissions
                .Where(x => x.UserId == item.UserId && !allowed.Contains(x.PermissionId) && !rolePermIds.Contains(x.PermissionId))
                .ToListAsync();
            _context.UserExtraPermissions.RemoveRange(toRemoveExtra);

            var accSel = RoleAccountVisibilityPersistence.ParseSelectedAccountIds(Request, RoleAccountIds, SelectedRoleAccountIds);
            await RoleAccountVisibilityPersistence.ReplaceForRoleAsync(_context, item.RoleId, accSel);

            if (item.IsPrimary)
            {
                foreach (var ur in await _context.UserRoles.Where(ur => ur.UserId == item.UserId && ur.Id != item.Id).ToListAsync())
                {
                    ur.IsPrimary = false;
                    _context.Update(ur);
                }
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = "تم إسناد الدور للمستخدم بنجاح.";
            return RedirectToAction(nameof(Index));
        }








        /// <summary>
        /// إرجاع معاينة صلاحيات الدور لاستخدامها في شاشة إسناد دور للمستخدم.
        /// النتيجة تُعرض كـ Partial View داخل Create.cshtml عن طريق AJAX.
        /// </summary>
        /// <param name="roleId">رقم الدور المطلوب عرض صلاحياته</param>
        [HttpGet]
        public async Task<IActionResult> GetRolePermissionsPreview(int roleId)
        {
            // حماية بسيطة: لو roleId غير صحيح نرجّع بارشيال فاضي
            if (roleId <= 0)
            {
                return PartialView("_RolePermissionsPreview",
                    Enumerable.Empty<RolePermission>());
            }

            // متغير: تحميل صلاحيات هذا الدور مع بيانات الصلاحية المرتبطة
            var list = await _context.RolePermissions
                .Include(rp => rp.Permission)              // جلب بيانات Permission (الاسم / الموديول)
                .Where(rp => rp.RoleId == roleId)          // فلترة على الدور المطلوب
                .OrderBy(rp => rp.Permission.Module)       // ترتيب حسب الموديول
                .ThenBy(rp => rp.Permission.NameAr)        // ثم حسب اسم الصلاحية
                .AsNoTracking()                            // قراءة فقط لتحسين الأداء
                .ToListAsync();

            // إرجاع البارشيال مع البيانات
            return PartialView("_RolePermissionsPreview", list);
        }

        /// <summary>
        /// إرجاع كل الصلاحيات مع إمكانية التعديل (شيك بوكس).
        /// الحالة الابتدائية من الدور. إذا وُجد userId تُحمّل صلاحيات المستخدم الفعلية (دور + إضافية − ممنوعة).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetRolePermissionsEditable(int roleId, int userId = 0)
        {
            if (roleId <= 0)
            {
                return PartialView("_RolePermissionsEditable", Enumerable.Empty<RolePermissionEditItem>());
            }

            var rolePermIds = new HashSet<int>(await _context.RolePermissions
                .Where(rp => rp.RoleId == roleId && rp.IsAllowed)
                .Select(rp => rp.PermissionId)
                .ToListAsync());

            var effectiveAllowed = rolePermIds;
            if (userId > 0)
            {
                var userExtraIds = new HashSet<int>(await _context.UserExtraPermissions
                    .Where(x => x.UserId == userId)
                    .Select(x => x.PermissionId)
                    .ToListAsync());
                var userDeniedIds = new HashSet<int>(await _context.UserDeniedPermissions
                    .Where(x => x.UserId == userId)
                    .Select(x => x.PermissionId)
                    .ToListAsync());
                effectiveAllowed = new HashSet<int>(
                    rolePermIds.Where(pid => !userDeniedIds.Contains(pid)).Union(userExtraIds));
            }

            // استبعاد كود صلاحية قديم غير مستخدم
            var allPerms = await _context.Permissions
                .AsNoTracking()
                .Where(p => p.Code == null || p.Code != "Dashboard.Dashboard.View")
                .OrderBy(p => p.Module)
                .ThenBy(p => p.NameAr)
                .Select(p => new RolePermissionEditItem
                {
                    PermissionId = p.PermissionId,
                    Code = p.Code,
                    NameAr = p.NameAr,
                    Module = p.Module,
                    IsAllowed = false
                })
                .ToListAsync();

            foreach (var p in allPerms)
                p.IsAllowed = effectiveAllowed.Contains(p.PermissionId);

            var roleAccounts = await _context.Accounts
                .AsNoTracking()
                .Where(a => a.IsActive)
                .OrderBy(a => a.AccountCode)
                .Select(a => new AccountListItem
                {
                    AccountId = a.AccountId,
                    AccountCode = a.AccountCode,
                    AccountName = a.AccountName
                })
                .ToListAsync();

            var allowedAccIds = await _context.RoleAccountVisibilityOverrides
                .AsNoTracking()
                .Where(x => x.RoleId == roleId && x.IsAllowed)
                .Select(x => x.AccountId)
                .ToListAsync();

            ViewBag.RoleAccounts = roleAccounts;
            ViewBag.AllowedRoleAccountIds = new HashSet<int>(allowedAccIds);

            return PartialView("_RolePermissionsEditable", allPerms);
        }










        // ========= EDIT =========

        public async Task<IActionResult> Edit(int id)
        {
            var item = await _context.UserRoles
                .Include(x => x.User)
                .Include(x => x.Role)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (item == null)
                return NotFound();

            await PopulateLookupsAsync(item.UserId, item.RoleId);
            return View(item);
        }

        /// <summary>
        /// إعادة ضبط صلاحيات المستخدم لتطابق تعريف الدور في «صلاحيات الأدوار»:
        /// إزالة الممنوع والإضافي الناتجين عن تخصيص المستخدم (نفس منطق الحفظ عندما تكون الصلاحيات مطابقة للدور).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequirePermission("UserRoles.ResetToRoleDefaults")]
        public async Task<IActionResult> ResetToRoleDefaults(int id)
        {
            var ur = await _context.UserRoles.FirstOrDefaultAsync(x => x.Id == id);
            if (ur == null)
                return NotFound();

            await ResetUserPermissionsToRoleDefaultsAsync(ur.UserId, ur.RoleId);

            await _context.SaveChangesAsync();

            TempData["Success"] = "تم RESET: أُزيلت صلاحيات المستخدم الإضافية والممنوعة المتعلقة بهذا الدور؛ أصبحت الصلاحيات الفعلية مطابقة لما هو معرّف في «صلاحيات الأدوار» لهذا الدور.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        /// <summary>
        /// يزيل UserDenied لصلاحيات الدور المسموحة، ويزيل UserExtra لصلاحيات خارج تعريف الدور (كما في حفظ التعديل عند التطابق مع الدور).
        /// </summary>
        private async Task ResetUserPermissionsToRoleDefaultsAsync(int userId, int roleId)
        {
            var rolePermIds = await _context.RolePermissions
                .Where(rp => rp.RoleId == roleId && rp.IsAllowed)
                .Select(rp => rp.PermissionId)
                .ToListAsync();

            var deniedToRemove = await _context.UserDeniedPermissions
                .Where(x => x.UserId == userId && rolePermIds.Contains(x.PermissionId))
                .ToListAsync();
            _context.UserDeniedPermissions.RemoveRange(deniedToRemove);

            var extraToRemove = await _context.UserExtraPermissions
                .Where(x => x.UserId == userId && !rolePermIds.Contains(x.PermissionId))
                .ToListAsync();
            _context.UserExtraPermissions.RemoveRange(extraToRemove);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            int id,
            [Bind("Id,UserId,RoleId,IsPrimary,AssignedAt")] UserRole item,
            int[]? selectedPermissionIds,
            List<int>? RoleAccountIds,
            string? SelectedRoleAccountIds = null)
        {
            if (id != item.Id)
                return NotFound();

            // التأكد من عدم وجود سطر آخر بنفس (UserId, RoleId)
            bool exists = await _context.UserRoles
                .AnyAsync(x => x.Id != item.Id &&
                               x.UserId == item.UserId &&
                               x.RoleId == item.RoleId);

            if (exists)
            {
                ModelState.AddModelError(string.Empty,
                    "يوجد سطر آخر بنفس المستخدم ونفس الدور، لا يمكن التكرار.");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(item);
                    await _context.SaveChangesAsync();

                    var rolePermIds = await _context.RolePermissions
                        .Where(rp => rp.RoleId == item.RoleId && rp.IsAllowed)
                        .Select(rp => rp.PermissionId)
                        .ToListAsync();
                    var hasExplicitPermissionSelection = selectedPermissionIds != null && selectedPermissionIds.Length > 0;
                    // حفظ الصلاحيات (إضافية / ممنوعة) مثل Create — يقبل صلاحيات أكثر من الدور
                    var allowed = hasExplicitPermissionSelection
                        ? new HashSet<int>(selectedPermissionIds!)
                        : new HashSet<int>(rolePermIds);

                    foreach (var permId in rolePermIds.Where(pid => !allowed.Contains(pid)))
                    {
                        if (!await _context.UserDeniedPermissions.AnyAsync(x => x.UserId == item.UserId && x.PermissionId == permId))
                        {
                            _context.UserDeniedPermissions.Add(new UserDeniedPermission
                            {
                                UserId = item.UserId,
                                PermissionId = permId,
                                IsAllowed = false
                            });
                        }
                    }
                    var toRemoveDenied = await _context.UserDeniedPermissions
                        .Where(x => x.UserId == item.UserId && rolePermIds.Contains(x.PermissionId) && allowed.Contains(x.PermissionId))
                        .ToListAsync();
                    _context.UserDeniedPermissions.RemoveRange(toRemoveDenied);

                    var allPermIds = await _context.Permissions.Where(p => p.IsActive).Select(p => p.PermissionId).ToListAsync();
                    foreach (var permId in allPermIds.Where(pid => allowed.Contains(pid) && !rolePermIds.Contains(pid)))
                    {
                        if (!await _context.UserExtraPermissions.AnyAsync(x => x.UserId == item.UserId && x.PermissionId == permId))
                        {
                            _context.UserExtraPermissions.Add(new UserExtraPermissions
                            {
                                UserId = item.UserId,
                                PermissionId = permId
                            });
                        }
                    }
                    var toRemoveExtra = await _context.UserExtraPermissions
                        .Where(x => x.UserId == item.UserId && !allowed.Contains(x.PermissionId) && !rolePermIds.Contains(x.PermissionId))
                        .ToListAsync();
                    _context.UserExtraPermissions.RemoveRange(toRemoveExtra);

                    var accSel = RoleAccountVisibilityPersistence.ParseSelectedAccountIds(Request, RoleAccountIds, SelectedRoleAccountIds);
                    await RoleAccountVisibilityPersistence.ReplaceForRoleAsync(_context, item.RoleId, accSel);

                    // دور افتراضي = صح فقط عندما صلاحيات المستخدم = صلاحيات الدور بالضبط
                    item.IsPrimary = allowed.SetEquals(rolePermIds);
                    if (item.IsPrimary)
                    {
                        foreach (var ur in await _context.UserRoles.Where(ur => ur.UserId == item.UserId && ur.Id != item.Id).ToListAsync())
                        {
                            ur.IsPrimary = false;
                            _context.Update(ur);
                        }
                    }
                    _context.Update(item);

                    await _context.SaveChangesAsync();

                    TempData["Success"] = "تم تعديل دور المستخدم وصلاحياته بنجاح.";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!UserRoleExists(item.Id))
                        return NotFound();
                    else
                        throw;
                }

                return RedirectToAction(nameof(Index));
            }

            await PopulateLookupsAsync(item.UserId, item.RoleId);
            return View(item);
        }

        // ========= DELETE =========

        public async Task<IActionResult> Delete(int id)
        {
            var item = await _context.UserRoles
                .AsNoTracking()
                .Include(x => x.User)
                .Include(x => x.Role)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (item == null)
                return NotFound();

            return View(item);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var item = await _context.UserRoles.FindAsync(id);
            if (item != null)
            {
                _context.UserRoles.Remove(item);
                await _context.SaveChangesAsync();

                TempData["Success"] = "تم حذف ربط الدور بالمستخدم.";
            }

            return RedirectToAction(nameof(Index));
        }

        // ========= BULK DELETE =========

        /// <summary>
        /// حذف جماعي لعدة سطور UserRole دفعة واحدة.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete([FromForm] int[] ids)
        {
            if (ids == null || ids.Length == 0)
            {
                TempData["Error"] = "من فضلك اختر على الأقل سطر واحد للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var rows = await _context.UserRoles
                .Where(x => ids.Contains(x.Id))
                .ToListAsync();

            if (rows.Count == 0)
            {
                TempData["Error"] = "لم يتم العثور على السطور المحددة.";
                return RedirectToAction(nameof(Index));
            }

            _context.UserRoles.RemoveRange(rows);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم حذف {rows.Count} سطر من جدول ربط المستخدمين بالأدوار.";
            return RedirectToAction(nameof(Index));
        }

        // ========= DELETE ALL =========

        /// <summary>
        /// حذف كل سطور UserRoles (للبيئة التجريبية).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _context.UserRoles.ToListAsync();
            _context.UserRoles.RemoveRange(all);
            await _context.SaveChangesAsync();

            TempData["Success"] = "تم حذف جميع ربط المستخدمين بالأدوار.";
            return RedirectToAction(nameof(Index));
        }








        // ========= EXPORT =========

        /// <summary>
        /// تصدير ربط المستخدمين بالأدوار بصيغة CSV.
        /// يحترم نفس فلاتر شاشة الـ Index.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Export(
            string? search,
            string? searchBy,
            string? searchMode = "contains",
            string? sort = null,
            string? dir = null,
            bool useDateRange = false,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int? fromCode = null,
            int? toCode = null,
            string? filterCol_id = null,
            string? filterCol_idExpr = null,
            string? filterCol_user = null,
            string? filterCol_role = null,
            string? filterCol_primary = null,
            string? filterCol_assigned = null,
            string format = "excel")
        {
            var smNorm = (searchMode ?? "contains").Trim().ToLowerInvariant();
            if (smNorm != "starts" && smNorm != "ends")
                smNorm = "contains";

            IQueryable<UserRoleListRow> query = BuildListQuery();
            query = ApplyListFilters(query, search, searchBy, smNorm, useDateRange, fromDate, toDate, fromCode, toCode);
            query = ApplyListColumnFilters(query, filterCol_id, filterCol_idExpr, filterCol_user, filterCol_role, filterCol_primary, filterCol_assigned);

            query = query
                .OrderBy(x => x.UserName)
                .ThenBy(x => x.RoleName);

            var list = await query.ToListAsync();

            // نتأكد من قيمة format
            format = (format ?? "excel").ToLowerInvariant();

            // ================= فرع Excel =================
            if (format == "excel")
            {
                // تأكد إن عندك:
                // using ClosedXML.Excel;
                // using System.IO;
                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add(ExcelExportNaming.SafeWorksheetName("أدوار المستخدمين"));

                int row = 1;

                // عناوين الأعمدة بالعربي (بدون DisplayName)
                worksheet.Cell(row, 1).Value = "رقم السطر";
                worksheet.Cell(row, 2).Value = "رقم المستخدم";
                worksheet.Cell(row, 3).Value = "اسم الدخول";
                worksheet.Cell(row, 4).Value = "رقم الدور";
                worksheet.Cell(row, 5).Value = "اسم الدور";
                worksheet.Cell(row, 6).Value = "دور أساسي؟";
                worksheet.Cell(row, 7).Value = "تاريخ الإسناد";

                var headerRange = worksheet.Range(row, 1, row, 7);
                headerRange.Style.Font.Bold = true;

                // البيانات
                foreach (var x in list)
                {
                    row++;

                    string userName = x.UserName;
                    string roleName = x.RoleName ?? UserRoleListRow.NoRoleDisplay;
                    string primary = x.IsPrimary ? "نعم" : "";

                    if (x.UserRoleId.HasValue)
                        worksheet.Cell(row, 1).Value = x.UserRoleId.Value;
                    if (x.RoleId.HasValue)
                        worksheet.Cell(row, 4).Value = x.RoleId.Value;
                    worksheet.Cell(row, 2).Value = x.UserId;
                    worksheet.Cell(row, 3).Value = userName;
                    worksheet.Cell(row, 5).Value = roleName;
                    worksheet.Cell(row, 6).Value = primary;
                    if (x.AssignedAt.HasValue)
                        worksheet.Cell(row, 7).Value = x.AssignedAt.Value;
                }

                worksheet.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                stream.Position = 0;

                var fileNameExcel = ExcelExportNaming.ArabicTimestampedFileName("أدوار المستخدمين", ".xlsx");
                const string excelContentType =
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

                return File(stream.ToArray(), excelContentType, fileNameExcel);
            }

            // ================= فرع CSV =================
            var sb = new StringBuilder();

            // الهيدر (بدون DisplayName)
            sb.AppendLine("رقم السطر,رقم المستخدم,اسم الدخول,رقم الدور,اسم الدور,دور أساسي؟,تاريخ الإسناد");

            foreach (var x in list)
            {
                string userName = x.UserName.Replace("\"", "\"\"");
                string roleName = (x.RoleName ?? UserRoleListRow.NoRoleDisplay).Replace("\"", "\"\"");
                string primary = x.IsPrimary ? "نعم" : "";
                string assigned = x.AssignedAt?.ToString("yyyy-MM-dd HH:mm") ?? "";

                sb.AppendLine(
                    $"{x.UserRoleId?.ToString() ?? ""}," +
                    $"{x.UserId}," +
                    $"\"{userName}\"," +
                    $"{x.RoleId?.ToString() ?? ""}," +
                    $"\"{roleName}\"," +
                    $"{primary}," +
                    $"{assigned}");
            }

            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString());
            string fileNameCsv = ExcelExportNaming.ArabicTimestampedFileName("أدوار المستخدمين", ".csv");

            return File(bytes, "text/csv", fileNameCsv);
        }









        // ========= دالة مساعدة =========

        private bool UserRoleExists(int id)
        {
            return _context.UserRoles.Any(e => e.Id == id);
        }
    }
}
