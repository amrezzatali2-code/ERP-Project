using System;                             // متغيرات الوقت والتاريخ
using System.Collections.Generic;         // القوائم List
using System.Threading.Tasks;            // Task و async/await
using Microsoft.EntityFrameworkCore;      // AnyAsync
using ERP.Data;                           // AppDbContext
using ERP.Models;                         // Policy

namespace ERP.Seed   // لو النيم سبيس مختلف عدّله زي باقي السيدرز عندك
{
    /// <summary>
    /// سيدر سياسات التسعير:
    /// ينشئ 30 سياسة افتراضية Policy 1 .. Policy 30
    /// لو جدول السياسات فارغ.
    /// </summary>
    public static class PolicySeeder
    {
        /// <summary>
        /// تُستدعى من Program.cs هكذا:
        /// await PolicySeeder.SeedAsync(db);
        /// </summary>
        /// <param name="db">متغير: كونتكست قاعدة البيانات</param>
        public static async Task SeedAsync(AppDbContext db)
        {
            // 1) لو فيه أي سياسات بالفعل لا نكرر البيانات
            bool hasAny = await db.Policies.AnyAsync();   // متغير: هل يوجد سجلات في جدول السياسات؟
            if (hasAny)
                return;

            // 2) إنشاء قائمة بالسياسات الافتراضية
            var list = new List<Policy>();                // متغير: قائمة السياسات

            for (int i = 1; i <= 30; i++)
            {
                list.Add(new Policy
                {
                    // رقم السياسة PolicyId هيُولد أوتوماتيك من الـ Identity
                    Name = $"Policy {i}",                     // اسم السياسة
                    Description = $"سياسة افتراضية رقم {i}", // الوصف
                    IsActive = true,                         // مفعّلة
                    CreatedAt = new DateTime(2025, 1, 1),    // تاريخ ثابت للتجربة
                    DefaultProfitPercent = 0m                // نسبة ربح افتراضية = 0%
                });
            }

            // 3) إضافة السياسات للجدول وحفظها
            db.Policies.AddRange(list);      // إضافة كل السياسات للكونتكست
            await db.SaveChangesAsync();     // حفظ التغييرات في قاعدة البيانات
        }
    }
}
