using ERP.Data;
using ERP.Data.Seed;                 // كلاس Seed للصلاحيات
using ERP.Services;
using ERP.Seed;
using ERP.Seeders;


// ====================== التوطين للواجهة والثقافة العربية (ar-EG) ======================
using Microsoft.AspNetCore.Localization;      // خدمات التوطين
using Microsoft.EntityFrameworkCore;
using System.Globalization;                    // التحكم فى CultureInfo
using System.Threading.Tasks;

namespace ERP
{
    public class Program
    {
        // ✅ جعل Main متزامنة-غير متزامنة علشان نستخدم await
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddControllersWithViews();

            builder.Services.AddDbContext<AppDbContext>(option =>
                option.UseSqlServer(builder.Configuration.GetConnectionString("conString")));

            // يتيح الوصول لـ HttpContext داخل DbContext أو الخدمات الأخرى
            builder.Services.AddHttpContextAccessor();

            // تسجيل خدمة حساب الإجماليات فى الـ DI Container
            builder.Services.AddScoped<DocumentTotalsService>();

            var app = builder.Build();

            // ===== تشغيل الميجراشن + Seed الصلاحيات عند بدء التطبيق =====
            using (var scope = app.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var db = services.GetRequiredService<AppDbContext>();

                // نضمن أن كل الميجراشن اتطبقت
                db.Database.Migrate();

                // 1) صلاحيات النظام
                await PermissionSeeder.SeedAsync(db);

                // 2) الأدوار الأساسية
                await RoleSeeder.SeedAsync(db);

                // 3) ربط الأدوار بالصلاحيات
                await RolePermissionSeeder.SeedAsync(db);

                // 4) سياسات التسعير (Policy 1 .. Policy 30 مثلاً)
                await PolicySeeder.SeedAsync(db);

                // 5) مجموعات الأصناف Group A..Z  ✅
                await ProductGroupSeeder.SeedAsync(db);

                // 6) مجموعات الحافز للأصناف (إن وجدت)
                await ProductBonusGroupSeeder.SeedAsync(db);

                // 6) شجرة الحسابات (إن وجدت)
                await AccountsSeeder.SeedAsync(db);




            }
            // ===== نهاية بلوك الميجراشن + Seed =====

            // إعداد الثقافات (ar-EG)
            var supportedCultures = new[] { new CultureInfo("ar-EG") };

            var locOptions = new RequestLocalizationOptions
            {
                DefaultRequestCulture = new RequestCulture("ar-EG"),
                SupportedCultures = supportedCultures,
                SupportedUICultures = supportedCultures
            };

            app.UseRequestLocalization(locOptions);

            // تثبيت الثقافة على مستوى الثريدات
            CultureInfo.DefaultThreadCurrentCulture = supportedCultures[0];
            CultureInfo.DefaultThreadCurrentUICulture = supportedCultures[0];

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.Run();
        }
    }
}
