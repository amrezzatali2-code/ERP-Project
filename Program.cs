using ERP.Data;
using ERP.Filters;                   // PopulateUserPermissionsFilter
using ERP.Models;                    // Permission (لضمان وجود صلاحيات فاتورة المبيعات)
using ERP.Services;                  // خدمة DocumentTotalsService
using ERP.Seed;
using ERP.Seeders;
using ERP.Infrastructure;           // IUserActivityLogger, UserActivityLogger
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

                // مزامنة الصلاحيات مع مكونات البرنامج: إضافة الناقصة وتصحيح الأسماء والموديولات
                var syncList = ERP.Security.PermissionCodes.GetAllForSync().ToList();
                var existingPerms = await db.Permissions.ToDictionaryAsync(p => p.Code, p => p, StringComparer.OrdinalIgnoreCase);
                var now = DateTime.UtcNow;
                bool anyChange = false;
                foreach (var (Code, NameAr, Module) in syncList)
                {
                    if (existingPerms.TryGetValue(Code, out var p))
                    {
                        if (p.NameAr != NameAr || (p.Module ?? "") != Module)
                        {
                            p.NameAr = NameAr;
                            p.Module = Module;
                            p.UpdatedAt = now;
                            anyChange = true;
                        }
                    }
                    else
                    {
                        db.Permissions.Add(new Permission { Code = Code, NameAr = NameAr, Module = Module, IsActive = true, CreatedAt = now });
                        anyChange = true;
                    }
                }
                if (anyChange) await db.SaveChangesAsync();

                // ضمان وجود صلاحيتي فاتورة المبيعات وقائمة المبيعات (نفس الأكواد المستخدمة في الكونترولر)
                const string codeView = "Sales.Invoices.View";
                const string codeCreate = "Sales.Invoices.Create";
                var codeViewLower = codeView.ToLower();
                var codeCreateLower = codeCreate.ToLower();
                var permView = await db.Permissions.FirstOrDefaultAsync(p => p.Code != null && p.Code.ToLower() == codeViewLower);
                if (permView == null)
                {
                    db.Permissions.Add(new Permission { Code = codeView, NameAr = "قائمة فواتير المبيعات", Module = "المبيعات", IsActive = true, CreatedAt = DateTime.UtcNow });
                    await db.SaveChangesAsync();
                    permView = await db.Permissions.FirstOrDefaultAsync(p => p.Code != null && p.Code.ToLower() == codeViewLower);
                }
                var permCreate = await db.Permissions.FirstOrDefaultAsync(p => p.Code != null && p.Code.ToLower() == codeCreateLower);
                if (permCreate == null)
                {
                    db.Permissions.Add(new Permission { Code = codeCreate, NameAr = "فاتورة مبيعات جديدة", Module = "المبيعات", IsActive = true, CreatedAt = DateTime.UtcNow });
                    await db.SaveChangesAsync();
                    permCreate = await db.Permissions.FirstOrDefaultAsync(p => p.Code != null && p.Code.ToLower() == codeCreateLower);
                }

                // ربط صلاحيات فاتورة المبيعات بأدوار المسؤول إذا لم تكونا مربوطة (حتى تعمل الصلاحيات فعلياً)
                if (permView != null || permCreate != null)
                {
                    var adminRoleNames = new[] { "مسؤول النظام", "مالك النظام" };
                    var adminRoles = await db.Roles.Where(r => adminRoleNames.Contains(r.Name ?? "")).ToListAsync();
                    var nowRp = DateTime.UtcNow;
                    foreach (var role in adminRoles)
                    {
                        if (permView != null)
                        {
                            var hasView = await db.RolePermissions.AnyAsync(rp => rp.RoleId == role.RoleId && rp.PermissionId == permView.PermissionId);
                            if (!hasView)
                            {
                                db.RolePermissions.Add(new RolePermission { RoleId = role.RoleId, PermissionId = permView.PermissionId, IsAllowed = true, CreatedAt = nowRp });
                            }
                        }
                        if (permCreate != null)
                        {
                            var hasCreate = await db.RolePermissions.AnyAsync(rp => rp.RoleId == role.RoleId && rp.PermissionId == permCreate.PermissionId);
                            if (!hasCreate)
                            {
                                db.RolePermissions.Add(new RolePermission { RoleId = role.RoleId, PermissionId = permCreate.PermissionId, IsAllowed = true, CreatedAt = nowRp });
                            }
                        }
                    }
                    await db.SaveChangesAsync();
                }

                // 4) سياسات التسعير (لو موجودة)
                await PolicySeeder.SeedAsync(db);

                // 5) مجموعات الأصناف Group A..Z
                await ProductGroupSeeder.SeedAsync(db);

                // 6) مجموعات البونص للأصناف
                await ProductBonusGroupSeeder.SeedAsync(db);

                // 7) شجرة الحسابات
                await AccountsSeeder.SeedAsync(db);
            }

            // =========================================================
            // 7) إعداد اللغة والثقافة (ar-EG)
            // =========================================================
            var supportedCultures = new[] { new CultureInfo("ar-EG") };

            var locOptions = new RequestLocalizationOptions
            {
                DefaultRequestCulture = new RequestCulture("ar-EG"),
                SupportedCultures = supportedCultures,
                SupportedUICultures = supportedCultures
            };

            app.UseRequestLocalization(locOptions);

            // تثبيت الثقافة الافتراضية على مستوى الثريدات
            CultureInfo.DefaultThreadCurrentCulture = supportedCultures[0];
            CultureInfo.DefaultThreadCurrentUICulture = supportedCultures[0];

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
