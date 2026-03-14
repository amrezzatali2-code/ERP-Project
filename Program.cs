using ERP.Data;
using ERP.Data.Seed;                 // RoleSeeder
using ERP.Filters;                   // PopulateUserPermissionsFilter
using ERP.Models;                    // Permission (لضمان وجود صلاحيات فاتورة المبيعات)
using ERP.Services;                  // خدمة DocumentTotalsService
using ERP.Seed;
using ERP.Seeders;
using ERP.Infrastructure;           // IUserActivityLogger, UserActivityLogger, DecimalModelBinderProvider
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;            // سياسة التوثيق
using Microsoft.AspNetCore.Mvc.Authorization;        // AuthorizeFilter العام
using Microsoft.AspNetCore.Localization;             // خدمات التوطين
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Threading.Tasks;

namespace ERP
{
    public class Program
    {
        // ✅ Main Async علشان نقدر نستعمل await فى الـ Seed
        public static async Task Main(string[] args)
        {
            // متغير: منشئ التطبيق
            var builder = WebApplication.CreateBuilder(args);

            // =========================================================
            // 1) إعداد MVC + فلتر Authorize عام (كل الصفحات تحتاج Login)
            // =========================================================
            builder.Services.AddControllersWithViews(options =>
            {
                // متغير: سياسة عامة تشترط وجود مستخدم مسجَّل دخول
                var policy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();

                // إضافة الفلتر على مستوى كل الكنترولرات
                options.Filters.Add(new AuthorizeFilter(policy));
                options.Filters.Add<PopulateUserPermissionsFilter>();

                // ربط القيم العشرية (الحد الائتماني، سعر الجمهور، إلخ) بقبول الفاصلة والنقطة لتفادي "must be a number"
                options.ModelBinderProviders.Insert(0, new DecimalModelBinderProvider());
            })
            .AddJsonOptions(options =>
            {
                // ✅ إعدادات JSON للتأكد من أن الـ property name matching يعمل بشكل صحيح
                options.JsonSerializerOptions.PropertyNameCaseInsensitive = true; // مهم: يجعل الـ binding case-insensitive
                options.JsonSerializerOptions.PropertyNamingPolicy = null; // استخدام أسماء الخصائص كما هي (PascalCase)
            });

            // =========================================================
            // 2) ربط قاعدة البيانات
            // =========================================================
            builder.Services.AddDbContext<AppDbContext>(option =>
                option.UseSqlServer(builder.Configuration.GetConnectionString("conString")));

            // متغير: إتاحة HttpContext داخل الخدمات والـ DbContext
            builder.Services.AddHttpContextAccessor();

            // متغير: تسجيل خدمة حساب إجماليات المستندات (الفواتير)
            builder.Services.AddScoped<DocumentTotalsService>();

            // =========================================================
            // 3) نظام التوثيق بالكوكيز (Login / Logout)
            // =========================================================
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    // ⚠️ مهم: مسار شاشة الدخول يطابق Login GET فى LoginController
                    options.LoginPath = "/Login";          // شاشة تسجيل الدخول
                    options.LogoutPath = "/Login/Logout";        // مسار تسجيل الخروج
                    options.AccessDeniedPath = "/Home/AccessDenied"; // صفحة "لا تملك صلاحية"
                });

            builder.Services.AddScoped<ILedgerPostingService, LedgerPostingService>();
            builder.Services.AddScoped<ERP.Services.StockAnalysisService>();
            builder.Services.AddScoped<IFullReturnService, FullReturnService>();
            builder.Services.AddScoped<ERP.Services.IPermissionService, ERP.Services.PermissionService>();

            // متغير: نظام الصلاحيات (PermissionService للتحقق من الصلاحيات)
            builder.Services.AddAuthorization();

            // =========================================================
            // 4) خدمة سجل النشاط (UserActivityLog)
            // =========================================================
            builder.Services.AddScoped<IUserActivityLogger, UserActivityLogger>();

            // =========================================================
            // 5) بناء التطبيق
            // =========================================================
            var app = builder.Build();

            // =========================================================
            // 6) تشغيل الميجراشن + Seed مرة واحدة عند بداية التشغيل
            // =========================================================
            using (var scope = app.Services.CreateScope())
            {
                // متغير: موفّر الخدمات داخل الـ scope
                var services = scope.ServiceProvider;
                var db = services.GetRequiredService<AppDbContext>();   // سياق قاعدة البيانات

                // تطبيق كل الـ Migrations لو ناقصة
                db.Database.Migrate();

                // ========== صلاحيات وأدوار من الكود فقط: مسح القديم وإعادة البناء ==========
                var syncList = ERP.Security.PermissionCodes.GetAllForSync()
                    .Where(x => !string.IsNullOrWhiteSpace(x.Code))
                    .DistinctBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // 1) حذف كل ربط دور-صلاحية (لأننا سنعيد بناء الصلاحيات)
                var allRolePerms = await db.RolePermissions.ToListAsync();
                db.RolePermissions.RemoveRange(allRolePerms);
                await db.SaveChangesAsync();

                // 2) حذف صلاحيات غير موجودة في القائمة الجديدة (أكواد قديمة)
                var syncCodes = new HashSet<string>(syncList.Select(x => x.Code), StringComparer.OrdinalIgnoreCase);
                var oldPerms = await db.Permissions.Where(p => p.Code != null && !syncCodes.Contains(p.Code)).ToListAsync();
                db.Permissions.RemoveRange(oldPerms);
                await db.SaveChangesAsync();

                // 3) إضافة/تحديث الصلاحيات من مصدر الكود فقط
                var existingPerms = await db.Permissions.ToDictionaryAsync(p => p.Code, p => p, StringComparer.OrdinalIgnoreCase);
                var now = DateTime.UtcNow;
                foreach (var (Code, NameAr, Module) in syncList)
                {
                    if (existingPerms.TryGetValue(Code, out var p))
                    {
                        p.NameAr = NameAr;
                        p.Module = Module;
                        p.UpdatedAt = now;
                    }
                    else
                    {
                        db.Permissions.Add(new Permission { Code = Code, NameAr = NameAr, Module = Module, IsActive = true, CreatedAt = now });
                    }
                }
                await db.SaveChangesAsync();

                // 4) إعادة بناء الأدوار من الـ Seeder (قائمة مختصرة من الكود)
                await RoleSeeder.SeedAsync(db);

                // 5) ربط دور "مالك النظام" بكل الصلاحيات
                var ownerRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == "مالك النظام");
                if (ownerRole != null)
                {
                    var allPerms = await db.Permissions.ToListAsync();
                    var existingOwnerLinks = (await db.RolePermissions.Where(rp => rp.RoleId == ownerRole.RoleId).Select(rp => rp.PermissionId).ToListAsync()).ToHashSet();
                    var nowRp = DateTime.UtcNow;
                    foreach (var perm in allPerms)
                    {
                        if (!existingOwnerLinks.Contains(perm.PermissionId))
                        {
                            db.RolePermissions.Add(new RolePermission { RoleId = ownerRole.RoleId, PermissionId = perm.PermissionId, IsAllowed = true, CreatedAt = nowRp });
                        }
                    }
                    await db.SaveChangesAsync();
                }

                // 6) سياسات التسعير (لو موجودة)
                await PolicySeeder.SeedAsync(db);

                // 5) مجموعات الأصناف Group A..Z
                await ProductGroupSeeder.SeedAsync(db);

                // 6) مجموعات البونص للأصناف
                await ProductBonusGroupSeeder.SeedAsync(db);

                // 7) شجرة الحسابات
                await AccountsSeeder.SeedAsync(db);
            }

            // =========================================================
            // 7) إعداد اللغة والثقافة (ar-EG) — صيغة التاريخ: يوم/شهر/سنة (مثل 12/3/2026)
            // =========================================================
            var arEg = (CultureInfo)CultureInfo.GetCultureInfo("ar-EG").Clone();
            arEg.DateTimeFormat.ShortDatePattern = "d/M/yyyy";
            arEg.DateTimeFormat.DateSeparator = "/";
            var supportedCultures = new[] { arEg };

            var locOptions = new RequestLocalizationOptions
            {
                DefaultRequestCulture = new RequestCulture(arEg),
                SupportedCultures = supportedCultures,
                SupportedUICultures = supportedCultures
            };

            app.UseRequestLocalization(locOptions);

            // تثبيت الثقافة الافتراضية على مستوى الثريدات
            CultureInfo.DefaultThreadCurrentCulture = arEg;
            CultureInfo.DefaultThreadCurrentUICulture = arEg;

            // =========================================================
            // 8) الـ Middleware المعتادة
            // =========================================================
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");  // صفحة الخطأ العامة
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            // ترتيب مهم: التوثيق ثم الصلاحيات
            app.UseAuthentication();
            app.UseAuthorization();

            // =========================================================
            // 9) خريطة الـ Routes (الراوت الافتراضى)
            // =========================================================
            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.Run();
        }
    }
}
