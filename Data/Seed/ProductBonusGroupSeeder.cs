using System;
using System.Collections.Generic;              // القوائم List
using System.Threading.Tasks;                  // async / Task
using Microsoft.EntityFrameworkCore;           // أوامر EF Core
using ERP.Data;                                // AppDbContext
using ERP.Models;                              // ProductBonusGroup

namespace ERP.Seeders
{
    /// <summary>
    /// سيدر مجموعات الحافز للأصناف.
    /// مثال: Bonus Group 1 → 1 جنيه حافز لكل علبة، وهكذا.
    /// </summary>
    public static class ProductBonusGroupSeeder
    {
        /// <summary>
        /// دالة تشغيل السيدر:
        /// - لو جدول ProductBonusGroups فيه بيانات → لا يعمل شيء.
        /// - لو فاضي → يضيف مجموعات حافز جاهزة للاستخدام.
        /// </summary>
        /// <param name="db">كائن السياق AppDbContext للاتصال بقاعدة البيانات.</param>
        public static async Task SeedAsync(AppDbContext db)
        {
            // لو فيه أي بيانات في الجدول، نخرج فورًا (علشان ما نكررش الإدخال).
            bool hasAny = await db.ProductBonusGroups.AnyAsync();   // متغير: هل يوجد بيانات سابقة؟
            if (hasAny)
                return;

            // قائمة لتجهيز مجموعات الحافز
            var bonusGroups = new List<ProductBonusGroup>           // متغير: قائمة مجموعات الحافز
            {
                new ProductBonusGroup
                {
                    Name        = "Bonus Group 1",                  // اسم المجموعة
                    Description = "مجموعة حافز 1 جنيه لكل علبة",   // وصف بالعربي
                    BonusAmount = 1.00m,                            // 1 جنيه لكل علبة
                    IsActive    = true,
                    CreatedAt   = new DateTime(2025, 1, 1)
                },
                new ProductBonusGroup
                {
                    Name        = "Bonus Group 2",
                    Description = "مجموعة حافز 2 جنيه لكل علبة",
                    BonusAmount = 2.00m,
                    IsActive    = true,
                    CreatedAt   = new DateTime(2025, 1, 1)
                },
                new ProductBonusGroup
                {
                    Name        = "Bonus Group 3",
                    Description = "مجموعة حافز 3 جنيه لكل علبة",
                    BonusAmount = 3.00m,
                    IsActive    = true,
                    CreatedAt   = new DateTime(2025, 1, 1)
                },
                new ProductBonusGroup
                {
                    Name        = "Bonus Group 4",
                    Description = "مجموعة حافز 4 جنيه لكل علبة",
                    BonusAmount = 4.00m,
                    IsActive    = true,
                    CreatedAt   = new DateTime(2025, 1, 1)
                },
                new ProductBonusGroup
                {
                    Name        = "Bonus Group 5",
                    Description = "مجموعة حافز 5 جنيه لكل علبة",
                    BonusAmount = 5.00m,
                    IsActive    = true,
                    CreatedAt   = new DateTime(2025, 1, 1)
                }
            };

            // إضافة المجموعات مرة واحدة
            await db.ProductBonusGroups.AddRangeAsync(bonusGroups);

            // حفظ التغييرات في قاعدة البيانات
            await db.SaveChangesAsync();
        }
    }
}
