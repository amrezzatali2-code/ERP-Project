using System;
using System.Collections.Generic;                 // القوائم List
using System.Threading.Tasks;                    // Task / async
using Microsoft.EntityFrameworkCore;             // AnyAsync
using ERP.Data;                                  // AppDbContext
using ERP.Models;                                // ProductGroup

namespace ERP.Seed
{
    /// <summary>
    /// سيدر لمجموعات الأصناف:
    /// ينشئ Group A .. Group Z لو جدول المجموعات فاضي.
    /// </summary>
    public static class ProductGroupSeeder
    {
        public static async Task SeedAsync(AppDbContext db)   // متغير: كونتكست قاعدة البيانات
        {
            // 1) لو فيه أي مجموعة فعليًا، ما نكرّرش البيانات
            bool hasAny = await db.ProductGroups.AnyAsync();  // متغير: هل يوجد سجلات حالية؟
            if (hasAny)
                return;

            var groups = new List<ProductGroup>();            // متغير: قائمة المجموعات

            // 2) إنشاء Group A .. Group Z
            for (char c = 'A'; c <= 'Z'; c++)
            {
                groups.Add(new ProductGroup
                {
                    // لا نحدد ProductGroupId علشان Identity يشتغل لوحده
                    Name = $"Group {c}",                // اسم المجموعة
                    Description = $"مجموعة أصناف {c}",        // وصف بالعربي
                    IsActive = true,                        // المجموعة مفعّلة
                    CreatedAt = new DateTime(2025, 1, 1)     // تاريخ ثابت للتجربة
                });
            }

            // 3) إضافة وحفظ
            await db.ProductGroups.AddRangeAsync(groups);     // إضافة جميع المجموعات
            await db.SaveChangesAsync();                      // حفظ في قاعدة البيانات
        }
    }
}
