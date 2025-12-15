using ERP.Data;
using ERP.Data.Seed;                 // كلاس Seed للصلاحيات والأدوار
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

            // متغير: نظام الصلاحيات (حالياً الأساس فقط، بدون Policies خاصة)
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

                // 1) صلاحيات النظام
                await PermissionSeeder.SeedAsync(db);

                // 2) الأدوار الأساسية
                await RoleSeeder.SeedAsync(db);

                // 3) ربط الأدوار بالصلاحيات
                await RolePermissionSeeder.SeedAsync(db);

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
