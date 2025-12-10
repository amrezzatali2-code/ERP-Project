using System;
using System.Collections.Generic;          // القوائم List
using System.Linq;                         // LINQ
using System.Threading.Tasks;              // Task / async
using ERP.Models;                          // موديل Role
using Microsoft.EntityFrameworkCore;       // FirstOrDefaultAsync / SaveChangesAsync

namespace ERP.Data.Seed
{
    /// <summary>
    /// Seeder للأدوار الأساسية فى النظام (كلها بأسماء عربية).
    /// - يحاول البحث عن الدور بالاسم العربي أو أي اسم قديم (إنجليزي).
    /// - لو لقيه → يعدّل الاسم والوصف وباقي الخصائص.
    /// - لو ما لقيهوش → يضيفه جديد.
    /// Idempotent = يشتغل كل مرة بدون تكرار بيانات.
    /// </summary>
    public static class RoleSeeder
    {
        public static async Task SeedAsync(AppDbContext db)
        {
            // 1) تعريف بيانات الأدوار الأساسية:
            var roles = new List<RoleSeedConfig>
            {
                // الإدارة العليا
                new RoleSeedConfig
                {
                    Name        = "مالك النظام",
                    Description = "مالك النظام / كل الصلاحيات",
                    IsSystemRole = true,
                    IsActive    = true,
                    OldNames    = new[] { "Owner" }
                },
                new RoleSeedConfig
                {
                    Name        = "المدير العام",
                    Description = "المدير العام للشركة",
                    IsSystemRole = true,
                    IsActive    = true,
                    OldNames    = new[] { "GeneralManager" }
                },

                // الحسابات والمالية
                new RoleSeedConfig
                {
                    Name        = "مدير الشئون المالية",
                    Description = "مسئول التخطيط المالي والتقارير المالية",
                    IsSystemRole = true,
                    IsActive    = true,
                    OldNames    = new[] { "FinanceManager" }
                },
                new RoleSeedConfig
                {
                    Name        = "محاسب عام",
                    Description = "محاسب عام / قيود / دفاتر",
                    IsActive    = true,
                    OldNames    = new[] { "Accountant" }
                },

                // المبيعات
                new RoleSeedConfig
                {
                    Name        = "مدير المبيعات",
                    Description = "إدارة فريق المبيعات ومتابعة الأهداف",
                    IsActive    = true,
                    OldNames    = new[] { "SalesManager" }
                },
                new RoleSeedConfig
                {
                    Name        = "مشرف مبيعات",
                    Description = "متابعة مندوبي المبيعات والمناطق",
                    IsActive    = true,
                    OldNames    = new[] { "SalesSupervisor" }
                },
                new RoleSeedConfig
                {
                    Name        = "مندوب مبيعات",
                    Description = "مندوب مبيعات (فاتورة + تحصيل)",
                    IsActive    = true,
                    OldNames    = new[] { "SalesRep" }
                },
                new RoleSeedConfig
                {
                    Name        = "خدمة عملاء المبيعات",
                    Description = "خدمة عملاء / إدخال فواتير المبيعات من المكتب",
                    IsActive    = true,
                    OldNames    = new[] { "SalesBackOffice" }
                },

                // الموزعين / التوزيع
                new RoleSeedConfig
                {
                    Name        = "مدير التوزيع",
                    Description = "مسئول خطوط السير وجداول التوزيع",
                    IsActive    = true,
                    OldNames    = new[] { "DistributorManager" }
                },
                new RoleSeedConfig
                {
                    Name        = "سائق / موزع",
                    Description = "سائق / موزع للأدوية على العملاء",
                    IsActive    = true,
                    OldNames    = new[] { "DriverDistributor" }
                },

                // المشتريات
                new RoleSeedConfig
                {
                    Name        = "مدير المشتريات",
                    Description = "مسئول سياسات الشراء والتفاوض مع الموردين",
                    IsActive    = true,
                    OldNames    = new[] { "PurchasingManager" }
                },
                new RoleSeedConfig
                {
                    Name        = "مسئول المشتريات",
                    Description = "متابعة أوامر الشراء وتنفيذها",
                    IsActive    = true,
                    OldNames    = new[] { "PurchasingOfficer" }
                },

                // المخازن والجرد
                new RoleSeedConfig
                {
                    Name        = "مدير المخازن",
                    Description = "المسئول الأول عن المخزون وحركة الأصناف",
                    IsActive    = true,
                    OldNames    = new[] { "WarehouseManager" }
                },
                new RoleSeedConfig
                {
                    Name        = "أمين مخزن",
                    Description = "استلام وصرف الأصناف من وإلى المخزن",
                    IsActive    = true,
                    OldNames    = new[] { "StoreKeeper" }
                },
                new RoleSeedConfig
                {
                    Name        = "مسئول الجرد",
                    Description = "مسئول الجرد وتسويات المخزون",
                    IsActive    = true,
                    OldNames    = new[] { "InventoryController" }
                },
                new RoleSeedConfig
                {
                    Name        = "مسئول المرتجعات",
                    Description = "مسئول مرتجعات العملاء والشركات",
                    IsActive    = true,
                    OldNames    = new[] { "ReturnsOfficer" }
                },

                // الموارد البشرية
                new RoleSeedConfig
                {
                    Name        = "مدير الموارد البشرية",
                    Description = "تخطيط ومتابعة سياسات الموارد البشرية",
                    IsActive    = true,
                    OldNames    = new[] { "HRManager" }
                },
                new RoleSeedConfig
                {
                    Name        = "مسئول شئون عاملين",
                    Description = "مسئول شئون عاملين / حضور وانصراف",
                    IsActive    = true,
                    OldNames    = new[] { "HROfficer" }
                },

                // تقنية المعلومات
                new RoleSeedConfig
                {
                    Name        = "مسئول تقنية المعلومات",
                    Description = "مسئول الشبكة / الأجهزة / النسخ الاحتياطي",
                    IsSystemRole = true,
                    IsActive    = true,
                    OldNames    = new[] { "ITAdmin" }
                },

                // التقارير فقط
                new RoleSeedConfig
                {
                    Name        = "مستخدم تقارير فقط",
                    Description = "قراءة التقارير بدون تعديل البيانات",
                    IsActive    = true,
                    OldNames    = new[] { "ReportsViewer" }
                },

                // مستخدم عادي
                new RoleSeedConfig
                {
                    Name        = "مستخدم عادي",
                    Description = "مستخدم عادي بصلاحيات محدودة",
                    IsActive    = true,
                    OldNames    = new[] { "BasicUser" }
                }
            };

            bool needSave = false;

            foreach (var cfg in roles)
            {
                // نبني قائمة الأسماء التي نبحث بها فى الجدول
                var namesToSearch = cfg.OldNames
                    .Concat(new[] { cfg.Name }) // العربي برضه
                    .ToList();

                // نسأل قاعدة البيانات مباشرة:
                var existing = await db.Roles
                    .FirstOrDefaultAsync(r => namesToSearch.Contains(r.Name));

                if (existing != null)
                {
                    // تحديث الدور الموجود
                    bool changed = false;

                    if (existing.Name != cfg.Name)
                    {
                        existing.Name = cfg.Name;
                        changed = true;
                    }

                    if (existing.Description != cfg.Description)
                    {
                        existing.Description = cfg.Description;
                        changed = true;
                    }

                    if (existing.IsSystemRole != cfg.IsSystemRole)
                    {
                        existing.IsSystemRole = cfg.IsSystemRole;
                        changed = true;
                    }

                    if (existing.IsActive != cfg.IsActive)
                    {
                        existing.IsActive = cfg.IsActive;
                        changed = true;
                    }

                    if (changed)
                    {
                        existing.UpdatedAt = DateTime.UtcNow;
                        needSave = true;
                    }
                }
                else
                {
                    // إضافة دور جديد
                    var role = new Role
                    {
                        Name = cfg.Name,
                        Description = cfg.Description,
                        IsSystemRole = cfg.IsSystemRole,
                        IsActive = cfg.IsActive,
                        CreatedAt = DateTime.UtcNow
                    };

                    await db.Roles.AddAsync(role);
                    needSave = true;
                }
            }

            if (needSave)
            {
                await db.SaveChangesAsync();
            }
        }

        /// <summary>
        /// كلاس مساعد لتعريف بيانات الدور فى الـ Seeder
        /// </summary>
        private class RoleSeedConfig
        {
            public string Name { get; set; } = string.Empty;         // اسم الدور (العربي النهائي)
            public string Description { get; set; } = string.Empty;  // الوصف
            public bool IsSystemRole { get; set; } = false;          // هل هو دور نظامي؟
            public bool IsActive { get; set; } = true;               // نشط؟
            public string[] OldNames { get; set; } = Array.Empty<string>(); // الأسماء القديمة (إنجليزي مثلاً)
        }
    }
}
