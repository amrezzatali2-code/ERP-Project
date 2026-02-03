using ERP.Data;                                   // AppDbContext
using ERP.Infrastructure;                         // PagedResult
using ERP.Models;                                 // الموديلات UserExtraPermissions, User, Permission
using Microsoft.AspNetCore.Mvc;                   // أساس الكنترولر
using Microsoft.AspNetCore.Mvc.Rendering;         // SelectList
using Microsoft.EntityFrameworkCore;              // Include, AsNoTracking, ToListAsync
using System;                                     // متغيرات التاريخ DateTime
using System.Collections.Generic;                 // القوائم List
using System.Linq;                                // أوامر LINQ مثل Where و OrderBy
using System.Text;                                // StringBuilder لتصدير CSV
using System.Threading.Tasks;                     // Task و async
using ClosedXML.Excel;                      // لتصدير Excel
using System.IO;

namespace ERP.Controllers
{
    /// <summary>
    /// كنترولر إدارة جدول UserExtraPermissions
    /// (الصلاحيات الإضافية للمستخدمين، فوق الأدوار العادية).
    /// </summary>
    public class UserExtraPermissionsController : Controller
    {
        private readonly AppDbContext _context;   // متغير: الاتصال بقاعدة البيانات

        public UserExtraPermissionsController(AppDbContext context)
        {
            _context = context;
        }

        // ========= دوال مساعدة: الفلاتر + القوائم المنسدلة =========

        /// <summary>
        /// تطبيق البحث وفلترة الكود والتاريخ على استعلام الصلاحيات الإضافية.
        /// تُستخدم في Index و Export.
        /// </summary>
        private IQueryable<UserExtraPermissions> ApplyFilters(
            IQueryable<UserExtraPermissions> query,
            string? search,
            string? searchBy,
            bool useDateRange,
            DateTime? fromDate,
            DateTime? toDate,
            int? fromCode,
            int? toCode)
        {
            // فلتر Id من/إلى
            if (fromCode.HasValue)
            {
                query = query.Where(x => x.Id >= fromCode.Value);
            }

            if (toCode.HasValue)
            {
                query = query.Where(x => x.Id <= toCode.Value);
            }

            // فلتر التاريخ (CreatedAt)
            if (useDateRange && fromDate.HasValue && toDate.HasValue)
            {
                query = query.Where(x => x.CreatedAt >= fromDate.Value &&
                                         x.CreatedAt <= toDate.Value);
            }

            // بحث نصي
            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = search.Trim();

                switch ((searchBy ?? "all").ToLower())
                {
                    case "userid":
                        if (int.TryParse(term, out int userId))
                            query = query.Where(x => x.UserId == userId);
                        else
                            query = query.Where(x => false);
                        break;

                    case "permissionid":
                        if (int.TryParse(term, out int permId))
                            query = query.Where(x => x.PermissionId == permId);
                        else
                            query = query.Where(x => false);
                        break;

                    case "username":
                        query = query.Where(x =>
                            x.User.UserName.Contains(term) ||
                            (x.User.DisplayName ?? "").Contains(term));
                        break;

                    case "permission":
                        query = query.Where(x =>
                            x.Permission.Code.Contains(term) ||
                            x.Permission.NameAr.Contains(term));
                        break;

                    case "id":
                        if (int.TryParse(term, out int idVal))
                            query = query.Where(x => x.Id == idVal);
                        else
                            query = query.Where(x => false);
                        break;

                    default:    // all
                        query = query.Where(x =>
                            x.Id.ToString().Contains(term) ||
                            x.UserId.ToString().Contains(term) ||
                            x.PermissionId.ToString().Contains(term) ||
                            x.User.UserName.Contains(term) ||
                            (x.User.DisplayName ?? "").Contains(term) ||
                            x.Permission.Code.Contains(term) ||
                            x.Permission.NameAr.Contains(term));
                        break;
                }
            }

            return query;
        }








        /// <summary>
        /// تجهيز القوائم المنسدلة للمستخدمين والصلاحيات للفورمات (Create/Edit).
        /// </summary>
        private async Task PopulateLookupsAsync(int? selectedUserId = null, int? selectedPermissionId = null)
        {
            // قائمة المستخدمين لعرضها في DropDown
            var userOptions = await _context.Users
                .OrderBy(u => u.DisplayName ?? u.UserName)
                .Select(u => new
                {
                    u.UserId,
                    Text = (u.DisplayName ?? u.UserName) + " (" + u.UserName + ")"
                })
                .ToListAsync();

            // قائمة الصلاحيات
            var permOptions = await _context.Permissions
                .OrderBy(p => p.Module)
                .ThenBy(p => p.NameAr)
                .Select(p => new
                {
                    p.PermissionId,
                    Text = (p.Module != null && p.Module != ""
                            ? "[" + p.Module + "] "
                            : "") + p.NameAr + " (" + p.Code + ")"
                })
                .ToListAsync();

            ViewBag.UserId = new SelectList(userOptions, "UserId", "Text", selectedUserId);
            ViewBag.PermissionId = new SelectList(permOptions, "PermissionId", "Text", selectedPermissionId);
        }








        // ===== دالة مساعدة: تحميل قائمة المستخدمين للكومبو بوكس =====
        private async Task PopulateUsersAsync(int? selectedUserId = null)
        {
            // جلب المستخدمين مع نص لطيف للعرض
            var users = await _context.Users
                .OrderBy(u => u.UserName)
                .Select(u => new
                {
                    u.UserId,                                      // رقم المستخدم
                    Text = u.UserName + " (" + u.UserName + ")"   // نص يظهر في الكومبو
                })
                .ToListAsync();

            // إرسال القائمة للفيو مع اختيار المستخدم الحالي لو موجود
            ViewBag.UserId = new SelectList(users, "UserId", "Text", selectedUserId);
        }








        // ===== دالة مساعدة: تحميل صلاحيات مستخدم معيّن (من الأدوار + الإضافية) =====
        private async Task LoadUserPermissionsForUserAsync(int userId, string? search = null)
        {
            // 1) أدوار المستخدم
            var userRoleIds = await _context.UserRoles
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.RoleId)
                .ToListAsync();

            var roleNames = await _context.Roles
                .Where(r => userRoleIds.Contains(r.RoleId))
                .Select(r => r.Name)
                .ToListAsync();

            ViewBag.UserRolesText = roleNames.Count == 0
                ? "لا يوجد أدوار مرتبطة بهذا المستخدم"
                : "أدوار المستخدم: " + string.Join(" ، ", roleNames);

            // 2) الصلاحيات القادمة من الأدوار (RolePermissions)
            var allowedFromRolesSet = (await _context.RolePermissions
                .Where(rp => userRoleIds.Contains(rp.RoleId) && rp.IsAllowed) // لو عندك عمود IsAllowed
                .Select(rp => rp.PermissionId)       // لازم تكون PermissionId زي اللي في جدول Permissions
                .ToListAsync()).ToHashSet();

            // 3) الصلاحيات الإضافية الحالية لهذا المستخدم (UserExtraPermissions)
            var extraPermsSet = (await _context.UserExtraPermissions
                .Where(x => x.UserId == userId)
                .Select(x => x.PermissionId)
                .ToListAsync()).ToHashSet();

            // 4) كل الصلاحيات مع فلتر اختياري بالبحث
            var permsQuery = _context.Permissions.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                // فلترة حسب الكود أو الموديول أو الاسم بالعربي
                permsQuery = permsQuery.Where(p =>
                    p.Code.Contains(search) ||
                    p.Module.Contains(search) ||
                    p.NameAr.Contains(search));
            }

            var allPerms = await permsQuery
                .OrderBy(p => p.Module)
                .ThenBy(p => p.Code)
                .ToListAsync();

            // 5) تجهيز صفوف الجدول للفيو
            var rows = allPerms.Select(p => new
            {
                Id = p.PermissionId,                            // رقم الصلاحية
                Code = p.Code,                                    // الكود بالإنجليزي
                Module = p.Module,                                  // الموديول بالعربي
                NameAr = p.NameAr,                                  // اسم الصلاحية بالعربي
                IsFromRole = allowedFromRolesSet.Contains(p.PermissionId), // ✔/✖ في الأدوار؟
                IsExtra = extraPermsSet.Contains(p.PermissionId)        // هل موجودة كصلاحية إضافية؟
            }).ToList();

            ViewBag.Permissions = rows;   // إرسال البيانات للفيو
        }







        /// <summary>
        /// حفظ الصلاحيات الإضافية لمستخدم معيّن:
        /// - selectedPermissionIds = كل الصلاحيات المتعلّمة في الفورم (دور + إضافية)
        /// - نحسب صلاحيات الدور من الجداول
        /// - الصلاحيات الإضافية = المختارة - صلاحيات الدور
        /// - نضيف الجديد ونحذف الإضافيات التي اتشالت.
        /// </summary>
        // ===== دالة مساعدة: حفظ الصلاحيات الإضافية للمستخدم =====
        private async Task SaveUserExtrasAsync(int userId, int[]? selectedPermissionIds)
        {
            // لو الفورم رجّع null نعوّضها بقائمة فاضية
            var selectedIds = (selectedPermissionIds ?? Array.Empty<int>())
                .Distinct()
                .ToList();                          // الصلاحيات التى نريدها كإضافية بعد الحفظ

            // الصلاحيات الإضافية الحالية من القاعدة
            var existingExtras = await _context.UserExtraPermissions
                .Where(x => x.UserId == userId)
                .ToListAsync();

            var existingIds = existingExtras
                .Select(x => x.PermissionId)
                .ToHashSet();

            // 1) الصلاحيات التى كانت إضافية وتم إلغاء علامة الصح ⇒ نحذفها
            var toDelete = existingExtras
                .Where(x => !selectedIds.Contains(x.PermissionId))
                .ToList();

            if (toDelete.Count > 0)
                _context.UserExtraPermissions.RemoveRange(toDelete);

            // 2) الصلاحيات الجديدة التى تم اختيارها الآن ولم تكن موجودة قبل كإضافية ⇒ نضيفها
            var now = DateTime.UtcNow;

            var toInsert = selectedIds
                .Where(pid => !existingIds.Contains(pid))
                .Select(pid => new UserExtraPermissions
                {
                    UserId = userId,          // المستخدم
                    PermissionId = pid,             // رقم الصلاحية
                    CreatedAt = now              // تاريخ الإضافة
                })
                .ToList();

            if (toInsert.Count > 0)
                await _context.UserExtraPermissions.AddRangeAsync(toInsert);

            await _context.SaveChangesAsync();
        }









        // دالة مساعدة: ترجع كل الصلاحيات + علامة هل هي صلاحية إضافية للمستخدم ولا لا
        private async Task PopulatePermissionsForUserAsync(int userId)
        {
            // كل الصلاحيات
            var allPermissions = await _context.Permissions
                .OrderBy(p => p.Module)
                .ThenBy(p => p.NameAr)
                .ToListAsync();

            // الصلاحيات الإضافية الحالية للمستخدم
            var extraIds = await _context.UserExtraPermissions
                .Where(x => x.UserId == userId)
                .Select(x => x.PermissionId)
                .ToListAsync();

            var extraSet = new HashSet<int>(extraIds);

            // تجهيز الداتا للعرض في الجدول
            var rows = allPermissions.Select(p => new
            {
                PermissionId = p.PermissionId,            // معرّف الصلاحية (PK)
                p.Code,                         // الكود النصي مثل Sales.Invoices.View
                NameAr = p.NameAr ?? "",
                Module = p.Module ?? "",
                IsSelected = extraSet.Contains(p.PermissionId)  // true لو صلاحية إضافية للمستخدم
            }).ToList();

            ViewBag.PermissionsTable = rows;
        }









        // ========= INDEX =========

        /// <summary>
        /// قائمة الصلاحيات الإضافية للمستخدمين
        /// مع بحث/ترتيب/فلترة وتجزئة صفحات بنفس النظام الثابت.
        /// </summary>
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
            string? dateField = "CreatedAt",
            int? fromCode = null,
            int? toCode = null)
        {
            // استعلام أساسي للمستخدم والصلاحية مع Include
            IQueryable<UserExtraPermissions> query = _context.UserExtraPermissions
                .AsNoTracking()
                .Include(x => x.User)
                .Include(x => x.Permission);

            // تطبيق الفلاتر
            query = ApplyFilters(query, search, searchBy, useDateRange, fromDate, toDate, fromCode, toCode);

            // الترتيب
            bool desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
            sort ??= "Id";

            query = sort switch
            {
                "UserId" => desc
                    ? query.OrderByDescending(x => x.UserId)
                    : query.OrderBy(x => x.UserId),

                "PermissionId" => desc
                    ? query.OrderByDescending(x => x.PermissionId)
                    : query.OrderBy(x => x.PermissionId),

                "UserName" => desc
                    ? query.OrderByDescending(x => x.User.DisplayName ?? x.User.UserName)
                    : query.OrderBy(x => x.User.DisplayName ?? x.User.UserName),

                "PermissionCode" => desc
                    ? query.OrderByDescending(x => x.Permission.Code)
                    : query.OrderBy(x => x.Permission.Code),

                "CreatedAt" => desc
                    ? query.OrderByDescending(x => x.CreatedAt)
                    : query.OrderBy(x => x.CreatedAt),

                _ => desc
                    ? query.OrderByDescending(x => x.Id)
                    : query.OrderBy(x => x.Id)
            };

            // الترقيم
            int totalCount = await query.CountAsync();
            pageSize = Math.Max(1, pageSize);
            page = Math.Max(1, page);

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var model = new PagedResult<UserExtraPermissions>(items, totalCount, page, pageSize)
            {
                Search = search,
                SortColumn = sort,
                SortDescending = desc,
                UseDateRange = useDateRange,
                FromDate = fromDate,
                ToDate = toDate
            };

            ViewBag.SearchBy = searchBy ?? "all";
            ViewBag.FromCode = fromCode;
            ViewBag.ToCode = toCode;
            ViewBag.DateField = dateField ?? "CreatedAt";

            return View(model);
        }








        // ========= DETAILS =========

        /// <summary>
        /// عرض تفاصيل صلاحية إضافية واحدة لمستخدم.
        /// </summary>
        public async Task<IActionResult> Details(int id)
        {
            var extra = await _context.UserExtraPermissions
                .AsNoTracking()
                .Include(x => x.User)
                .Include(x => x.Permission)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (extra == null)
                return NotFound();

            return View(extra);
        }









        // ========= CREATE =========

        // ======================= GET: UserExtraPermissions/Create =======================
        [HttpGet]
        public async Task<IActionResult> Create(int? userId, string? search)
        {
            // تجهيز كومبو المستخدمين
            await PopulateUsersAsync(userId);

            if (userId.HasValue)
            {
                // تحميل صلاحيات المستخدم المختار
                await LoadUserPermissionsForUserAsync(userId.Value, search);
                ViewBag.SelectedUserId = userId.Value;
            }
            else
            {
                // لو لسه ما اختارش مستخدم
                ViewBag.UserRolesText = "اختر مستخدمًا لعرض صلاحياته.";
                ViewBag.Permissions = new List<object>();
            }

            return View();
        }









        // ======================= POST: UserExtraPermissions/Create ======================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(int userId, int[] selectedPermissionIds)
        {
            // 1) التحقق من اختيار مستخدم
            if (userId <= 0)
            {
                TempData["Error"] = "من فضلك اختر المستخدم أولاً.";
                await PopulateUsersAsync(null);
                ViewBag.Permissions = new List<object>();
                return View();
            }

            // 2) حذف الصلاحيات الإضافية القديمة لهذا المستخدم
            var oldExtras = _context.UserExtraPermissions
                .Where(x => x.UserId == userId);

            _context.UserExtraPermissions.RemoveRange(oldExtras);

            // 3) إضافة الصلاحيات الجديدة المختارة
            if (selectedPermissionIds != null && selectedPermissionIds.Length > 0)
            {
                var now = DateTime.UtcNow;

                var newExtras = selectedPermissionIds
                    .Distinct() // لو نفس الصلاحية متكررة
                    .Select(pid => new UserExtraPermissions
                    {
                        UserId = userId,   // المستخدم
                        PermissionId = pid,      // الصلاحية
                        CreatedAt = now      // وقت الإضافة
                    });

                await _context.UserExtraPermissions.AddRangeAsync(newExtras);
            }

            // 4) حفظ في قاعدة البيانات
            await _context.SaveChangesAsync();

            // 5) رسالة نجاح + رجوع للقائمة
            TempData["Success"] = "تم حفظ الصلاحيات الإضافية للمستخدم.";
            return RedirectToAction(nameof(Index));   // أو RedirectToAction("Index", new { userId })
        }

        // ===









        /// <summary>
        /// فتح شاشة تعديل الصلاحيات الإضافية لمستخدم.
        /// زر Edit في Index يبعث Id لسطر واحد، هنا نجيب UserId ونفتح كل صلاحياته.
        /// </summary>
        // GET: UserExtraPermissions/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
                return NotFound();

            var row = await _context.UserExtraPermissions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id.Value);

            if (row == null)
                return NotFound();

            int userId = row.UserId;    // المستخدم صاحب هذه الصلاحية الإضافية

            await PopulateUsersAsync(userId);
            await LoadUserPermissionsForUserAsync(userId);

            return View(row);
        }









        // POST: UserExtraPermissions/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, UserExtraPermissions model, int[]? selectedPermissionIds)
        {
            if (id != model.Id)
                return NotFound();

            if (model.UserId <= 0)
            {
                ModelState.AddModelError(nameof(model.UserId), "يجب اختيار مستخدم.");
            }

            if (!ModelState.IsValid)
            {
                await PopulateUsersAsync(model.UserId);
                await LoadUserPermissionsForUserAsync(model.UserId);
                return View(model);
            }

            await SaveUserExtrasAsync(model.UserId, selectedPermissionIds);

            TempData["Success"] = "تم حفظ الصلاحيات الإضافية للمستخدم.";
            return RedirectToAction(nameof(Index), new { userId = model.UserId });
        }











        // ========= DELETE =========

        public async Task<IActionResult> Delete(int id)
        {
            var extra = await _context.UserExtraPermissions
                .AsNoTracking()
                .Include(x => x.User)
                .Include(x => x.Permission)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (extra == null)
                return NotFound();

            return View(extra);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var extra = await _context.UserExtraPermissions.FindAsync(id);
            if (extra != null)
            {
                _context.UserExtraPermissions.Remove(extra);
                await _context.SaveChangesAsync();
                TempData["Success"] = "تم حذف الصلاحية الإضافية.";
            }

            return RedirectToAction(nameof(Index));
        }

        // ========= BULK DELETE =========

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete([FromForm] int[] ids)
        {
            if (ids == null || ids.Length == 0)
            {
                TempData["Error"] = "من فضلك اختر على الأقل سطر واحد للحذف.";
                return RedirectToAction(nameof(Index));
            }

            var rows = await _context.UserExtraPermissions
                .Where(x => ids.Contains(x.Id))
                .ToListAsync();

            if (rows.Count == 0)
            {
                TempData["Error"] = "لم يتم العثور على السطور المحددة.";
                return RedirectToAction(nameof(Index));
            }

            _context.UserExtraPermissions.RemoveRange(rows);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم حذف {rows.Count} سطر من الصلاحيات الإضافية.";
            return RedirectToAction(nameof(Index));
        }

        // ========= DELETE ALL =========

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var all = await _context.UserExtraPermissions.ToListAsync();
            _context.UserExtraPermissions.RemoveRange(all);
            await _context.SaveChangesAsync();

            TempData["Success"] = "تم حذف جميع الصلاحيات الإضافية للمستخدمين.";
            return RedirectToAction(nameof(Index));
        }








        // ========= EXPORT =========

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
            // 1) الاستعلام الأساسي: صلاحيات إضافية + المستخدم + الصلاحية
            IQueryable<UserExtraPermissions> query = _context.UserExtraPermissions
                .AsNoTracking()
                .Include(x => x.User)
                .Include(x => x.Permission);

            // 2) تطبيق نفس الفلاتر المستخدمة في Index
            query = ApplyFilters(
                query,
                search,
                searchBy,
                useDateRange,
                fromDate,
                toDate,
                fromCode,
                toCode);

            // 3) ترتيب ثابت حسب Id
            var list = await query
                .OrderBy(x => x.Id)
                .ToListAsync();

            // نتأكد من قيمة format
            format = (format ?? "excel").ToLowerInvariant();

            // ================= فرع Excel =================
            if (format == "excel")
            {
                // تأكد من وجود using ClosedXML.Excel; و using System.IO; أعلى الملف
                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("UserExtraPermissions");

                int row = 1;

                // عناوين الأعمدة بالعربي
                worksheet.Cell(row, 1).Value = "رقم السطر";
                worksheet.Cell(row, 2).Value = "رقم المستخدم";
                worksheet.Cell(row, 3).Value = "اسم الدخول";
                worksheet.Cell(row, 4).Value = "رقم الصلاحية";
                worksheet.Cell(row, 5).Value = "كود الصلاحية";
                worksheet.Cell(row, 6).Value = "اسم الصلاحية";
                worksheet.Cell(row, 7).Value = "تاريخ الإضافة";

                var headerRange = worksheet.Range(row, 1, row, 7);
                headerRange.Style.Font.Bold = true;

                // البيانات
                foreach (var x in list)
                {
                    row++;

                    string userName = x.User?.UserName ?? string.Empty;        // اسم الدخول فقط
                    string permCode = x.Permission?.Code ?? string.Empty;
                    string permName = x.Permission?.NameAr ?? string.Empty;

                    worksheet.Cell(row, 1).Value = x.Id;
                    worksheet.Cell(row, 2).Value = x.UserId;
                    worksheet.Cell(row, 3).Value = userName;
                    worksheet.Cell(row, 4).Value = x.PermissionId;
                    worksheet.Cell(row, 5).Value = permCode;
                    worksheet.Cell(row, 6).Value = permName;
                    worksheet.Cell(row, 7).Value = x.CreatedAt;
                }

                worksheet.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                stream.Position = 0;

                var fileNameExcel = $"UserExtraPermissions_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                const string excelContentType =
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

                return File(stream.ToArray(), excelContentType, fileNameExcel);
            }

            // ================= فرع CSV =================
            var sb = new StringBuilder();
            sb.AppendLine("Id,UserId,UserName,PermissionId,PermissionCode,PermissionName,CreatedAt");

            foreach (var x in list)
            {
                string userName = (x.User?.UserName ?? string.Empty)
                    .Replace("\"", "\"\"");                   // اسم الدخول
                string permCode = (x.Permission?.Code ?? string.Empty)
                    .Replace("\"", "\"\"");
                string permName = (x.Permission?.NameAr ?? string.Empty)
                    .Replace("\"", "\"\"");
                string created = x.CreatedAt.ToString("yyyy-MM-dd HH:mm");

                sb.AppendLine(
                    $"{x.Id}," +
                    $"{x.UserId}," +
                    $"\"{userName}\"," +
                    $"{x.PermissionId}," +
                    $"\"{permCode}\"," +
                    $"\"{permName}\"," +
                    $"{created}");
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileNameCsv = $"UserExtraPermissions_{timeStamp}.csv";

            return File(bytes, "text/csv", fileNameCsv);
        }








        // ========= دالة مساعدة =========

        private bool UserExtraExists(int id)
        {
            return _context.UserExtraPermissions.Any(e => e.Id == id);
        }
    }
}
