using System;
using System.Collections.Generic;              // القوائم List
using System.Threading.Tasks;                  // async / Task
using Microsoft.EntityFrameworkCore;           // أوامر EF Core
using ERP.Data;                                // AppDbContext
using ERP.Models;                              // Account, AccountType

namespace ERP.Seeders
{
    /// <summary>
    /// سيدر شجرة الحسابات الأساسية للبرنامج (Chart of Accounts).
    /// يتم تشغيله مرة واحدة فقط إذا كان جدول الحسابات فارغ.
    /// </summary>
    public static class AccountsSeeder
    {
        /// <summary>
        /// دالة تشغيل السيدر:
        /// - لو جدول Accounts فيه بيانات → يخرج فوراً.
        /// - لو فاضي → يضيف شجرة حسابات جاهزة لشركة توزيع أدوية.
        /// </summary>
        /// <param name="db">سياق قاعدة البيانات AppDbContext</param>
        public static async Task SeedAsync(AppDbContext db)
        {
            // 1) لو فيه أي حسابات متسجلة بالفعل → لا نكرر الإدخال
            bool hasAny = await db.Accounts.AnyAsync();   // متغير: هل جدول الحسابات يحتوي بيانات؟
            if (hasAny)
                return;

            // تاريخ موحد لإنشاء كل الحسابات
            DateTime created = new DateTime(2025, 1, 1);  // متغير: تاريخ الإنشاء الافتراضي

            // ====================== الجذور مستوى 1 ======================

            // جذر الأصول
            var rootAssets = new Account
            {
                AccountCode = "1",                        // متغير: كود الحساب
                AccountName = "الأصول",                  // متغير: اسم الحساب
                AccountType = AccountType.Asset,         // نوع الحساب: أصل
                Level = 1,                               // مستوى أول
                IsLeaf = false,                          // ليس حساب تفصيلي
                IsActive = true,
                Notes = "جذر الأصول في شجرة الحسابات",
                CreatedAt = created
            };

            // جذر الالتزامات
            var rootLiabilities = new Account
            {
                AccountCode = "2",
                AccountName = "الالتزامات",
                AccountType = AccountType.Liability,
                Level = 1,
                IsLeaf = false,
                IsActive = true,
                Notes = "جذر الالتزامات في شجرة الحسابات",
                CreatedAt = created
            };

            // جذر حقوق الملكية
            var rootEquity = new Account
            {
                AccountCode = "3",
                AccountName = "حقوق الملكية",
                AccountType = AccountType.Equity,
                Level = 1,
                IsLeaf = false,
                IsActive = true,
                Notes = "جذر حقوق الملكية في شجرة الحسابات",
                CreatedAt = created
            };

            // جذر الإيرادات
            var rootRevenue = new Account
            {
                AccountCode = "4",
                AccountName = "الإيرادات",
                AccountType = AccountType.Revenue,
                Level = 1,
                IsLeaf = false,
                IsActive = true,
                Notes = "جذر الإيرادات في شجرة الحسابات",
                CreatedAt = created
            };

            // جذر المصروفات
            var rootExpenses = new Account
            {
                AccountCode = "5",
                AccountName = "المصروفات",
                AccountType = AccountType.Expense,
                Level = 1,
                IsLeaf = false,
                IsActive = true,
                Notes = "جذر المصروفات في شجرة الحسابات",
                CreatedAt = created
            };

            // ====================== الأصول (مستوى 2) ======================

            var currentAssets = new Account
            {
                AccountCode = "1100",
                AccountName = "الأصول المتداولة",
                AccountType = AccountType.Asset,
                ParentAccount = rootAssets,              // ربط بالحساب الأب
                Level = 2,
                IsLeaf = false,
                IsActive = true,
                CreatedAt = created
            };

            var nonCurrentAssets = new Account
            {
                AccountCode = "1200",
                AccountName = "الأصول غير المتداولة",
                AccountType = AccountType.Asset,
                ParentAccount = rootAssets,
                Level = 2,
                IsLeaf = false,
                IsActive = true,
                CreatedAt = created
            };

            // ====================== الأصول المتداولة (مستوى 3) ======================

            var cashAccount = new Account
            {
                AccountCode = "1101",
                AccountName = "الخزينة",
                AccountType = AccountType.Asset,
                ParentAccount = currentAssets,
                Level = 3,
                IsLeaf = true,                           // حساب تفصيلي يمكن التسجيل عليه
                IsActive = true,
                CreatedAt = created
            };

            var bankAccount = new Account
            {
                AccountCode = "1102",
                AccountName = "البنوك",
                AccountType = AccountType.Asset,
                ParentAccount = currentAssets,
                Level = 3,
                IsLeaf = true,
                IsActive = true,
                CreatedAt = created
            };

            var customersControl = new Account
            {
                AccountCode = "1103",
                AccountName = "حساب العملاء",
                AccountType = AccountType.Asset,
                ParentAccount = currentAssets,
                Level = 3,
                IsLeaf = false,                          // تحته عملاء تفصيليون
                IsActive = true,
                Notes = "أب لكل حسابات العملاء التفصيلية",
                CreatedAt = created
            };

            var otherReceivables = new Account
            {
                AccountCode = "1104",
                AccountName = "ذمم أخرى / سلف وعهد",
                AccountType = AccountType.Asset,
                ParentAccount = currentAssets,
                Level = 3,
                IsLeaf = true,
                IsActive = true,
                CreatedAt = created
            };

            var drugInventory = new Account
            {
                AccountCode = "1105",
                AccountName = "مخزون أدوية",
                AccountType = AccountType.Asset,
                ParentAccount = currentAssets,
                Level = 3,
                IsLeaf = true,
                IsActive = true,
                CreatedAt = created
            };

            var accessoryInventory = new Account
            {
                AccountCode = "1106",
                AccountName = "مخزون إكسسوار",
                AccountType = AccountType.Asset,
                ParentAccount = currentAssets,
                Level = 3,
                IsLeaf = true,
                IsActive = true,
                CreatedAt = created
            };

            // ====================== الأصول غير المتداولة (مستوى 3) ======================

            var fixedAssets = new Account
            {
                AccountCode = "1201",
                AccountName = "أثاث ومعدات",
                AccountType = AccountType.Asset,
                ParentAccount = nonCurrentAssets,
                Level = 3,
                IsLeaf = true,
                IsActive = true,
                CreatedAt = created
            };

            var cars = new Account
            {
                AccountCode = "1202",
                AccountName = "سيارات توزيع",
                AccountType = AccountType.Asset,
                ParentAccount = nonCurrentAssets,
                Level = 3,
                IsLeaf = true,
                IsActive = true,
                CreatedAt = created
            };

            // ====================== الالتزامات ======================

            var currentLiab = new Account
            {
                AccountCode = "2100",
                AccountName = "الالتزامات المتداولة",
                AccountType = AccountType.Liability,
                ParentAccount = rootLiabilities,
                Level = 2,
                IsLeaf = false,
                IsActive = true,
                CreatedAt = created
            };

            var longLiab = new Account
            {
                AccountCode = "2200",
                AccountName = "الالتزامات طويلة الأجل",
                AccountType = AccountType.Liability,
                ParentAccount = rootLiabilities,
                Level = 2,
                IsLeaf = false,
                IsActive = true,
                CreatedAt = created
            };

            var suppliers = new Account
            {
                AccountCode = "2101",
                AccountName = "الموردون",
                AccountType = AccountType.Liability,
                ParentAccount = currentLiab,
                Level = 3,
                IsLeaf = true,
                IsActive = true,
                CreatedAt = created
            };

            var notesPayable = new Account
            {
                AccountCode = "2102",
                AccountName = "أوراق دفع / شيكات صادرة",
                AccountType = AccountType.Liability,
                ParentAccount = currentLiab,
                Level = 3,
                IsLeaf = true,
                IsActive = true,
                CreatedAt = created
            };

            var taxPayable = new Account
            {
                AccountCode = "2103",
                AccountName = "ضرائب مستحقة",
                AccountType = AccountType.Liability,
                ParentAccount = currentLiab,
                Level = 3,
                IsLeaf = true,
                IsActive = true,
                CreatedAt = created
            };

            var longLoans = new Account
            {
                AccountCode = "2201",
                AccountName = "قروض بنكية طويلة الأجل",
                AccountType = AccountType.Liability,
                ParentAccount = longLiab,
                Level = 3,
                IsLeaf = true,
                IsActive = true,
                CreatedAt = created
            };

            // ====================== حقوق الملكية ======================

            var equityRootLevel2 = new Account
            {
                AccountCode = "3100",
                AccountName = "حقوق الملكية",
                AccountType = AccountType.Equity,
                ParentAccount = rootEquity,
                Level = 2,
                IsLeaf = false,
                IsActive = true,
                CreatedAt = created
            };

            var capital = new Account
            {
                AccountCode = "3101",
                AccountName = "رأس المال",
                AccountType = AccountType.Equity,
                ParentAccount = equityRootLevel2,
                Level = 3,
                IsLeaf = true,
                IsActive = true,
                CreatedAt = created
            };

            var retainedEarnings = new Account
            {
                AccountCode = "3102",
                AccountName = "أرباح وخسائر مرحلة",
                AccountType = AccountType.Equity,
                ParentAccount = equityRootLevel2,
                Level = 3,
                IsLeaf = true,
                IsActive = true,
                CreatedAt = created
            };

            var currentYearProfit = new Account
            {
                AccountCode = "3103",
                AccountName = "أرباح (خسائر) العام",
                AccountType = AccountType.Equity,
                ParentAccount = equityRootLevel2,
                Level = 3,
                IsLeaf = true,
                IsActive = true,
                CreatedAt = created
            };

            // ====================== الإيرادات ======================

            var salesRevenueRoot = new Account
            {
                AccountCode = "4100",
                AccountName = "إيرادات المبيعات",
                AccountType = AccountType.Revenue,
                ParentAccount = rootRevenue,
                Level = 2,
                IsLeaf = false,
                IsActive = true,
                CreatedAt = created
            };

            var drugSales = new Account
            {
                AccountCode = "4101",
                AccountName = "مبيعات أدوية",
                AccountType = AccountType.Revenue,
                ParentAccount = salesRevenueRoot,
                Level = 3,
                IsLeaf = true,
                IsActive = true,
                CreatedAt = created
            };

            var accessorySales = new Account
            {
                AccountCode = "4102",
                AccountName = "مبيعات إكسسوار",
                AccountType = AccountType.Revenue,
                ParentAccount = salesRevenueRoot,
                Level = 3,
                IsLeaf = true,
                IsActive = true,
                CreatedAt = created
            };

            var otherRevenueRoot = new Account
            {
                AccountCode = "4200",
                AccountName = "إيرادات أخرى",
                AccountType = AccountType.Revenue,
                ParentAccount = rootRevenue,
                Level = 2,
                IsLeaf = false,
                IsActive = true,
                CreatedAt = created
            };

            var supplierDiscounts = new Account
            {
                AccountCode = "4201",
                AccountName = "خصومات مكتسبة من الموردين",
                AccountType = AccountType.Revenue,
                ParentAccount = otherRevenueRoot,
                Level = 3,
                IsLeaf = true,
                IsActive = true,
                CreatedAt = created
            };

            // ====================== المصروفات ======================

            var purchaseRoot = new Account
            {
                AccountCode = "5000",
                AccountName = "المشتريات",
                AccountType = AccountType.Expense,
                ParentAccount = rootExpenses,
                Level = 2,
                IsLeaf = false,
                IsActive = true,
                Notes = "حساب المشتريات - يُستخدم في ترحيل مرتجعات الشراء",
                CreatedAt = created
            };

            var cogsRoot = new Account
            {
                AccountCode = "5100",
                AccountName = "تكلفة البضاعة المباعة",
                AccountType = AccountType.Expense,
                ParentAccount = rootExpenses,
                Level = 2,
                IsLeaf = false,
                IsActive = true,
                CreatedAt = created
            };

            var cogsDrugs = new Account
            {
                AccountCode = "5101",
                AccountName = "تكلفة الأدوية المباعة",
                AccountType = AccountType.Expense,
                ParentAccount = cogsRoot,
                Level = 3,
                IsLeaf = true,
                IsActive = true,
                CreatedAt = created
            };

            var operatingExpRoot = new Account
            {
                AccountCode = "5200",
                AccountName = "مصروفات تشغيلية",
                AccountType = AccountType.Expense,
                ParentAccount = rootExpenses,
                Level = 2,
                IsLeaf = false,
                IsActive = true,
                CreatedAt = created
            };

            var salaries = new Account
            {
                AccountCode = "5201",
                AccountName = "مرتبات وأجور",
                AccountType = AccountType.Expense,
                ParentAccount = operatingExpRoot,
                Level = 3,
                IsLeaf = true,
                IsActive = true,
                CreatedAt = created
            };

            var rent = new Account
            {
                AccountCode = "5202",
                AccountName = "إيجار",
                AccountType = AccountType.Expense,
                ParentAccount = operatingExpRoot,
                Level = 3,
                IsLeaf = true,
                IsActive = true,
                CreatedAt = created
            };

            var utilities = new Account
            {
                AccountCode = "5203",
                AccountName = "كهرباء ومياه",
                AccountType = AccountType.Expense,
                ParentAccount = operatingExpRoot,
                Level = 3,
                IsLeaf = true,
                IsActive = true,
                CreatedAt = created
            };

            var shippingExp = new Account
            {
                AccountCode = "5204",
                AccountName = "مصروفات نقل وتوزيع",
                AccountType = AccountType.Expense,
                ParentAccount = operatingExpRoot,
                Level = 3,
                IsLeaf = true,
                IsActive = true,
                CreatedAt = created
            };

            var otherGAndA = new Account
            {
                AccountCode = "5205",
                AccountName = "مصروفات عمومية وإدارية أخرى",
                AccountType = AccountType.Expense,
                ParentAccount = operatingExpRoot,
                Level = 3,
                IsLeaf = true,
                IsActive = true,
                CreatedAt = created
            };

            var customerDiscounts = new Account
            {
                AccountCode = "5300",
                AccountName = "خصومات مسموح بها للعملاء",
                AccountType = AccountType.Expense,
                ParentAccount = rootExpenses,
                Level = 2,
                IsLeaf = true,
                IsActive = true,
                CreatedAt = created
            };

            // ====================== تجميع كل الحسابات ======================

            var allAccounts = new List<Account>
            {
                // الجذور
                rootAssets, rootLiabilities, rootEquity, rootRevenue, rootExpenses,

                // الأصول
                currentAssets, nonCurrentAssets,
                cashAccount, bankAccount, customersControl, otherReceivables,
                drugInventory, accessoryInventory,
                fixedAssets, cars,

                // الالتزامات
                currentLiab, longLiab,
                suppliers, notesPayable, taxPayable, longLoans,

                // حقوق الملكية
                equityRootLevel2, capital, retainedEarnings, currentYearProfit,

                // الإيرادات
                salesRevenueRoot, drugSales, accessorySales,
                otherRevenueRoot, supplierDiscounts,

                // المصروفات
                purchaseRoot,
                cogsRoot, cogsDrugs,
                operatingExpRoot, salaries, rent, utilities,
                shippingExp, otherGAndA,
                customerDiscounts
            };

            // 2) إضافة كل الحسابات مرة واحدة
            await db.Accounts.AddRangeAsync(allAccounts);

            // 3) حفظ التغييرات في قاعدة البيانات
            await db.SaveChangesAsync();
        }
    }
}
