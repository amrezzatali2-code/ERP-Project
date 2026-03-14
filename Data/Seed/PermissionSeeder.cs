using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Models;                      // جدول Permission
using Microsoft.EntityFrameworkCore;   // EF Core

namespace ERP.Data.Seed
{
    /// <summary>
    /// Seeder للصلاحيات الأساسية في النظام.
    /// - ينشئ الصلاحيات مرة واحدة فقط (Idempotent).
    /// - كود الصلاحية بالإنجليزي (Module.Screen.Action).
    /// - عمود الموديول في الجدول بالعربي علشان يظهر للمستخدم.
    /// </summary>
    public static class PermissionSeeder
    {
        public static async Task SeedAsync(AppDbContext db)
        {
            // ================== 0) توحيد الموديول القديم إلى العربي ==================
            // أي صلاحيات موجودة بالفعل هنحدّث عمود Module فيها بالعربي حسب "كود" الصلاحية.
            var allPerms = await db.Permissions.ToListAsync();   // كل الصلاحيات الحالية في الجدول

            bool anyModuleUpdated = false;   // متغير: هل تم تحديث أي موديول فعليًا؟

            if (allPerms.Count > 0)
            {
                foreach (var p in allPerms)
                {
                    // القيمة الحالية للموديول كما هي في قاعدة البيانات
                    var currentModule = (p.Module ?? string.Empty).Trim();

                    // لو الموديول الحالي "مخصص" يدويًا ومش من مفاتيحنا القياسية،
                    // نسيبه زي ما هو (علشان لو عدلت من البرنامج ما نبوظش شغلك).
                    if (!string.IsNullOrEmpty(currentModule) &&
                        currentModule != "Dashboard" &&
                        currentModule != "Settings" &&
                        currentModule != "Security" &&
                        currentModule != "Geo" &&
                        currentModule != "Customers" &&
                        currentModule != "Inventory" &&
                        currentModule != "Purchasing" &&
                        currentModule != "Sales" &&
                        currentModule != "Accounts" &&
                        currentModule != "Reports")
                    {
                        // مثال: "مخازن فرع فيصل" أو "حسابات داخلية" → سيبها زي ما هي
                        continue;
                    }

                    // أول جزء من كود الصلاحية قبل أول نقطة هو مفتاح الموديول (بالإنجليزي)
                    var parts = (p.Code ?? "")
                        .Split('.', StringSplitOptions.RemoveEmptyEntries); // أجزاء الكود Module.Screen.Action

                    var key = parts.Length > 0 ? parts[0] : "";              // المفتاح: Dashboard / Inventory / ...

                    // القيمة العربية الهدف للموديول بناءً على المفتاح الإنجليزي
                    string? targetModule = currentModule;   // نبدأ بالقيمة الحالية (يمكن تكون فاضية أو إنجليزي)

                    switch (key)
                    {
                        case "Dashboard":
                            targetModule = "لوحة التحكم"; break;
                        case "Settings":
                            targetModule = "الإعدادات"; break;
                        case "Security":
                            targetModule = "المستخدمون والصلاحيات"; break;
                        case "Geo":
                            targetModule = "المناطق الجغرافية"; break;
                        case "Customers":
                            targetModule = "العملاء"; break;
                        case "Inventory":
                            targetModule = "إدارة المخازن"; break;
                        case "Purchasing":
                            targetModule = "المشتريات"; break;
                        case "Sales":
                            targetModule = "المبيعات"; break;
                        case "Accounts":
                            targetModule = "الحسابات"; break;
                        case "Reports":
                            targetModule = "التقارير"; break;
                        case "ProductClassifications":
                        case "Routes":
                        case "SalesInvoiceRoutes":
                            targetModule = "خط السير"; break;
                        case "Departments":
                        case "Jobs":
                        case "Employees":
                            targetModule = "الموظفون"; break;
                        default:
                            // مفتاح غير معروف → ما نغيرش حاجة
                            break;
                    }

                    // لو فعلاً اتغيّرت القيمة (من فاضي أو إنجليزي إلى عربي)
                    if (!string.IsNullOrEmpty(targetModule) &&
                        targetModule != currentModule)
                    {
                        p.Module = targetModule;      // تحديث الموديول في الكائن
                        anyModuleUpdated = true;      // تعليم أن هناك تحديث حصل
                    }
                }
            }

            // ================== 1) منع تكرار الأكواد ==================
            // بناء قائمة بالأكواد الموجودة فعلاً (علشان ما نكررش نفس Permission Code)
            var existingCodes = allPerms
                .Select(p => p.Code)
                .Where(c => c != null)
                .Cast<string>()
                .ToList();

            var existing = new HashSet<string>(
                existingCodes,
                StringComparer.OrdinalIgnoreCase);   // تجاهل حالة الحروف في المقارنة

            var toInsert = new List<Permission>();   // الصلاحيات الجديدة التي ستضاف

            // دالة داخلية لإضافة صلاحية واحدة لو الكود غير موجود
            void Add(string moduleKey, string moduleNameAr, string code, string nameAr, string? description = null)
            {
                // لو الكود موجود بالفعل نتجاهله (علشان السييدر Idempotent)
                if (existing.Contains(code))
                    return;

                var now = DateTime.UtcNow; // وقت إنشاء السطر

                var p = new Permission
                {
                    Module = moduleNameAr,                 // اسم الموديول بالعربي للعرض في الواجهات
                    Code = code,                           // كود الصلاحية بالإنجليزي (Module.Screen.Action)
                    NameAr = nameAr,                       // اسم الصلاحية بالعربي
                    Description = description ?? nameAr,   // وصف مختصر (افتراضيًا نفس الاسم)
                    IsActive = true,                       // افتراضياً الصلاحية مفعّلة
                    CreatedAt = now,                       // تاريخ الإنشاء
                    UpdatedAt = null
                };

                toInsert.Add(p);       // إضافة الصلاحية لقائمة الإدخال
                existing.Add(code);    // تسجيل الكود في HashSet علشان ما يتكررش
            }

            // CRUD عادي (عرض / إضافة / تعديل / حذف / تصدير)
            void AddCrud(string moduleKey, string moduleNameAr, string screenKey, string screenNameAr)
            {
                Add(moduleKey, moduleNameAr, $"{moduleKey}.{screenKey}.View", $"عرض {screenNameAr}");
                Add(moduleKey, moduleNameAr, $"{moduleKey}.{screenKey}.Create", $"إضافة {screenNameAr}");
                Add(moduleKey, moduleNameAr, $"{moduleKey}.{screenKey}.Edit", $"تعديل {screenNameAr}");
                Add(moduleKey, moduleNameAr, $"{moduleKey}.{screenKey}.Delete", $"حذف {screenNameAr}");
                Add(moduleKey, moduleNameAr, $"{moduleKey}.{screenKey}.Export", $"تصدير بيانات {screenNameAr}");
            }

            // مستند (فاتورة / مرتجع / إذن ...) فيه ترحيل وطباعة
            void AddDocument(string moduleKey, string moduleNameAr, string screenKey, string docNameAr)
            {
                Add(moduleKey, moduleNameAr, $"{moduleKey}.{screenKey}.View", $"قائمة {docNameAr}");
                Add(moduleKey, moduleNameAr, $"{moduleKey}.{screenKey}.Show", $"عرض {docNameAr} مفردة");
                Add(moduleKey, moduleNameAr, $"{moduleKey}.{screenKey}.Create", $"إنشاء {docNameAr} جديدة");
                Add(moduleKey, moduleNameAr, $"{moduleKey}.{screenKey}.Edit", $"تعديل {docNameAr}");
                Add(moduleKey, moduleNameAr, $"{moduleKey}.{screenKey}.Delete", $"حذف {docNameAr}");
                Add(moduleKey, moduleNameAr, $"{moduleKey}.{screenKey}.Export", $"تصدير {docNameAr}");
                Add(moduleKey, moduleNameAr, $"{moduleKey}.{screenKey}.Post", $"ترحيل {docNameAr}");
                Add(moduleKey, moduleNameAr, $"{moduleKey}.{screenKey}.UnPost", $"فك ترحيل {docNameAr}");
                Add(moduleKey, moduleNameAr, $"{moduleKey}.{screenKey}.Print", $"طباعة {docNameAr}");
            }

            // ================== Dashboard ==================
            Add("Dashboard", "لوحة التحكم", "Dashboard.Dashboard.View", "عرض لوحة التحكم الرئيسية");

            // ================== Settings (الإعدادات) ==================
            AddCrud("Settings", "الإعدادات", "NumberSeries", "ترقيم المستندات");
            AddCrud("Settings", "الإعدادات", "MovementLog", "سجل الحركات");
            AddCrud("Settings", "الإعدادات", "Policies", "السياسات العامة");
            AddCrud("Settings", "الإعدادات", "ItemGroups", "مجموعات الأصناف");
            AddCrud("Settings", "الإعدادات", "ItemGroupPolicies", "سياسات مجموعات الأصناف");
            AddCrud("Settings", "الإعدادات", "WarehousePolicies", "سياسات المخازن");

            // ================== Security (المستخدمون والصلاحيات) ==================
            AddCrud("Security", "المستخدمون والصلاحيات", "Users", "المستخدمين");
            AddCrud("Security", "المستخدمون والصلاحيات", "Roles", "أدوار المستخدمين");
            AddCrud("Security", "المستخدمون والصلاحيات", "Permissions", "الصلاحيات الأساسية");  // قائمة الصلاحيات (مطابق لـ PermissionCodes.Security.Permissions_*)
            AddCrud("Security", "المستخدمون والصلاحيات", "RolePermissions", "صلاحيات الأدوار");
            AddCrud("Security", "المستخدمون والصلاحيات", "UserRoles", "ربط المستخدمين بالأدوار");
            AddCrud("Security", "المستخدمون والصلاحيات", "UserPermissions", "صلاحيات المستخدمين");
            AddCrud("Security", "المستخدمون والصلاحيات", "UserExtraPermissions", "صلاحيات إضافية للمستخدم");
            AddCrud("Security", "المستخدمون والصلاحيات", "UserDeniedPermissions", "استثناءات صلاحيات المستخدمين");

            // ================== Geo (المناطق الجغرافية) ==================
            AddCrud("Geo", "المناطق الجغرافية", "Governorates", "المحافظات");
            AddCrud("Geo", "المناطق الجغرافية", "Districts", "الأحياء / المراكز");
            AddCrud("Geo", "المناطق الجغرافية", "Areas", "المناطق");
            AddCrud("Geo", "المناطق الجغرافية", "Branches", "الفروع");

            // ================== Customers (العملاء) ==================
            AddCrud("Customers", "العملاء", "Customers", "قائمة العملاء");
            AddCrud("Customers", "العملاء", "CustomerVolume", "حجم تعامل عميل");

            // ================== Inventory (إدارة المخازن) ==================
            AddCrud("Inventory", "إدارة المخازن", "Products", "قائمة الأصناف");
            AddCrud("Inventory", "إدارة المخازن", "ProductMovements", "حركة صنف");
            AddCrud("Inventory", "إدارة المخازن", "PriceHistory", "سجل تغيرات الأسعار");

            AddCrud("Inventory", "إدارة المخازن", "Warehouses", "المخازن");
            AddCrud("Inventory", "إدارة المخازن", "Categories", "فئات الأصناف");
            AddCrud("Inventory", "إدارة المخازن", "StockLedger", "دفتر الحركة المخزنية");
            AddCrud("Inventory", "إدارة المخازن", "FifoMap", "ربط FIFO (الخروج ← الدخول)");

            AddDocument("Inventory", "إدارة المخازن", "StockAdjustments", "تسوية جردية");
            AddCrud("Inventory", "إدارة المخازن", "StockAdjustmentLines", "تفاصيل التسويات الجردية");

            AddDocument("Inventory", "إدارة المخازن", "StockTransfers", "تحويل بين المخازن");
            AddCrud("Inventory", "إدارة المخازن", "StockTransferLines", "تفاصيل التحويل بين المخازن");

            // ================== Purchasing (المشتريات) ==================
            AddDocument("Purchasing", "المشتريات", "Invoices", "فاتورة المشتريات");
            AddCrud("Purchasing", "المشتريات", "InvoiceLines", "أصناف فواتير المشتريات");

            AddDocument("Purchasing", "المشتريات", "Requests", "طلب الشراء");
            AddCrud("Purchasing", "المشتريات", "RequestLines", "أصناف طلبات الشراء");

            AddDocument("Purchasing", "المشتريات", "Returns", "مرتجع المشتريات");
            AddCrud("Purchasing", "المشتريات", "ReturnLines", "أصناف مرتجعات المشتريات");

            // ================== Sales (المبيعات) ==================
            AddDocument("Sales", "المبيعات", "Invoices", "فاتورة المبيعات");
            AddCrud("Sales", "المبيعات", "InvoiceLines", "أصناف فواتير المبيعات");

            AddDocument("Sales", "المبيعات", "Returns", "مرتجع المبيعات");
            AddCrud("Sales", "المبيعات", "ReturnLines", "أصناف مرتجعات المبيعات");

            AddDocument("Sales", "المبيعات", "Orders", "أمر البيع");
            AddCrud("Sales", "المبيعات", "OrderLines", "أصناف أوامر المبيعات");

            // ================== Accounts (الحسابات) ==================
            AddCrud("Accounts", "الحسابات", "Chart", "شجرة الحسابات");
            AddCrud("Accounts", "الحسابات", "Ledger", "دفتر الأستاذ (قيود اليومية)");

            AddDocument("Accounts", "الحسابات", "CashReceipt", "إذن استلام نقدية");
            AddDocument("Accounts", "الحسابات", "CashPayment", "إذن صرف نقدية");
            AddDocument("Accounts", "الحسابات", "DebitNote", "إشعار خصم");
            AddDocument("Accounts", "الحسابات", "CreditNote", "إشعار إضافة");

            // ================== التقارير ==================
            Add("Reports", "التقارير", "Reports.CustomerBalances.View", "تقرير أرصدة العملاء");
            Add("Reports", "التقارير", "Reports.ProductBalances.View", "تقرير أرصدة الأصناف");
            Add("Reports", "التقارير", "Reports.ProductDetailsReport", "تقرير أصناف مفصّلة");
            Add("Reports", "التقارير", "Reports.ProductProfits.View", "تقرير أرباح الأصناف");
            Add("Reports", "التقارير", "Reports.CustomerProfits.View", "تقرير أرباح العملاء");
            Add("Reports", "التقارير", "Reports.RouteReport", "تقرير خط السير");

            // ================== خط السير ==================
            Add("Route", "خط السير", "ProductClassifications.Index", "قائمة تصنيفات الأصناف");
            Add("Route", "خط السير", "ProductClassifications.Create", "إضافة تصنيف صنف");
            Add("Route", "خط السير", "ProductClassifications.Edit", "تعديل تصنيف صنف");
            Add("Route", "خط السير", "ProductClassifications.Delete", "حذف تصنيف صنف");
            Add("Route", "خط السير", "Routes.Index", "قائمة خطوط السير");
            Add("Route", "خط السير", "Routes.Create", "إضافة خط سير");
            Add("Route", "خط السير", "Routes.Edit", "تعديل خط سير");
            Add("Route", "خط السير", "Routes.Delete", "حذف خط سير");
            Add("Route", "خط السير", "ProductClassifications.DeleteConfirmed", "تنفيذ حذف تصنيف صنف");
            Add("Route", "خط السير", "ProductClassifications.BulkDelete", "مسح مجموعة تصنيفات");
            Add("Route", "خط السير", "ProductClassifications.DeleteAll", "مسح كل التصنيفات");
            Add("Route", "خط السير", "ProductClassifications.GetColumnValues", "قيم عمود للفلتر");
            Add("Route", "خط السير", "Routes.DeleteConfirmed", "تنفيذ حذف خط سير");
            Add("Route", "خط السير", "Routes.BulkDelete", "مسح مجموعة خطوط سير");
            Add("Route", "خط السير", "Routes.DeleteAll", "مسح كل خطوط السير");
            Add("Route", "خط السير", "Routes.GetColumnValues", "قيم عمود للفلتر");
            Add("Route", "خط السير", "SalesInvoiceRoutes.Index", "قائمة بيانات خط السير للفواتير");
            Add("Route", "خط السير", "SalesInvoiceRoutes.Edit", "تعديل بيانات خط السير لفاتورة");
            Add("Route", "خط السير", "SalesInvoiceRoutes.Entry", "إدخال بيانات خط السير (شاشة التسلسل)");
            Add("Route", "خط السير", "SalesInvoiceRoutes.GetInvoiceInfo", "جلب بيانات الفاتورة");
            Add("Route", "خط السير", "SalesInvoiceRoutes.GetEmployeesByJob", "جلب الموظفين حسب الوظيفة");
            Add("Route", "خط السير", "SalesInvoiceRoutes.GetFridgeProducts", "جلب أصناف الثلاجة");
            Add("Route", "خط السير", "SalesInvoiceRoutes.SaveRouteEntry", "تسجيل بيانات خط السير من شاشة الإدخال");
            Add("Route", "خط السير", "SalesInvoiceRoutes.SaveRouteJson", "حفظ بيانات خط السير (API)");

            Add("Departments", "الموظفون", "Departments.Index", "قائمة الأقسام");
            Add("Departments", "الموظفون", "Departments.Create", "إضافة قسم");
            Add("Departments", "الموظفون", "Departments.Edit", "تعديل قسم");
            Add("Departments", "الموظفون", "Departments.Delete", "حذف قسم");
            Add("Departments", "الموظفون", "Departments.DeleteConfirmed", "تنفيذ حذف قسم");
            Add("Departments", "الموظفون", "Departments.BulkDelete", "مسح مجموعة أقسام");
            Add("Departments", "الموظفون", "Departments.DeleteAll", "مسح كل الأقسام");
            Add("Jobs", "الموظفون", "Jobs.Index", "قائمة الوظائف");
            Add("Jobs", "الموظفون", "Jobs.Create", "إضافة وظيفة");
            Add("Jobs", "الموظفون", "Jobs.Edit", "تعديل وظيفة");
            Add("Jobs", "الموظفون", "Jobs.Delete", "حذف وظيفة");
            Add("Jobs", "الموظفون", "Jobs.DeleteConfirmed", "تنفيذ حذف وظيفة");
            Add("Jobs", "الموظفون", "Jobs.BulkDelete", "مسح مجموعة وظائف");
            Add("Jobs", "الموظفون", "Jobs.DeleteAll", "مسح كل الوظائف");
            Add("Employees", "الموظفون", "Employees.Index", "قائمة الموظفين");
            Add("Employees", "الموظفون", "Employees.Create", "إضافة موظف");
            Add("Employees", "الموظفون", "Employees.Edit", "تعديل موظف");
            Add("Employees", "الموظفون", "Employees.Delete", "حذف موظف");
            Add("Employees", "الموظفون", "Employees.DeleteConfirmed", "تنفيذ حذف موظف");
            Add("Employees", "الموظفون", "Employees.Show", "عرض تفاصيل موظف");
            Add("Employees", "الموظفون", "Employees.Export", "تصدير الموظفين");
            Add("Employees", "الموظفون", "Employees.BulkDelete", "مسح مجموعة موظفين");
            Add("Employees", "الموظفون", "Employees.DeleteAll", "مسح كل الموظفين");

            // ================== توحيد أسماء الصلاحيات المكررة (مثل لوحة التحكم) ==================
            var dashOld = await db.Permissions.FirstOrDefaultAsync(p => p.Code == "Dashboard.View");
            if (dashOld != null)
            {
                dashOld.NameAr = "عرض لوحة التحكم (كود قديم)";
                dashOld.Module = "لوحة التحكم";
                anyModuleUpdated = true;
            }

            // ================== حفظ أي تعديلات / إضافات ==================
            if (toInsert.Count > 0)
            {
                await db.Permissions.AddRangeAsync(toInsert);
            }

            if (toInsert.Count > 0 || anyModuleUpdated)
            {
                await db.SaveChangesAsync();
            }
        }
    }
}
