using System.Collections.Generic;

namespace ERP.Security
{
    /// <summary>
    /// كودات الصلاحيات الأساسية فى النظام.
    /// الهدف: عدم تكرار النصوص "Strings" فى الكود
    /// واستخدام نفس الكود فى كل مكان (Controller / View / Seeder).
    /// </summary>
    public static class PermissionCodes
    {
        // ===== المبيعات – فواتير البيع =====
        public static class SalesInvoices
        {
            public const string View = "Sales.Invoices.View";    // عرض قائمة الفواتير
            public const string Show = "Sales.Invoices.Show";    // فتح فاتورة واحدة
            public const string Create = "Sales.Invoices.Create";  // إنشاء فاتورة
            public const string Edit = "Sales.Invoices.Edit";    // تعديل فاتورة
            public const string Delete = "Sales.Invoices.Delete";  // حذف فاتورة
            public const string Export = "Sales.Invoices.Export";  // تصدير
            public const string Post = "Sales.Invoices.Post";    // ترحيل
            public const string UnPost = "Sales.Invoices.UnPost";  // إلغاء الترحيل
            public const string Print = "Sales.Invoices.Print";   // طباعة الفاتورة
        }

        // ===== العملاء (أكواد مطابقة لجدول Permissions من الـ Seeder) =====
        public static class Customers
        {
            public const string View = "Customers.Customers.View";
            public const string Show = "Customers.Customers.View";  // نفس العرض للقائمة والتفاصيل
            public const string Create = "Customers.Customers.Create";
            public const string Edit = "Customers.Customers.Edit";
            public const string Delete = "Customers.Customers.Delete";
            public const string Export = "Customers.Customers.Export";
        }

        // ===== إدارة المخازن – الأصناف (أكواد من Seeder: Inventory.Products.*) =====
        public static class Products
        {
            public const string View = "Inventory.Products.View";
            public const string Show = "Inventory.Products.View";   // نفس العرض للقائمة والتفاصيل
            public const string Create = "Inventory.Products.Create";
            public const string Edit = "Inventory.Products.Edit";
            public const string Delete = "Inventory.Products.Delete";
            public const string Export = "Inventory.Products.Export";
            public const string Import = "Inventory.Products.Import";
        }

        // ===== إدارة المخازن – حركة المخزون (أكواد من Seeder) =====
        public static class Stock
        {
            public const string StockAdjustments_View = "Inventory.StockAdjustments.View";
            public const string StockAdjustments_Show = "Inventory.StockAdjustments.Show";
            public const string StockAdjustments_Create = "Inventory.StockAdjustments.Create";
            public const string StockAdjustments_Edit = "Inventory.StockAdjustments.Edit";
            public const string StockAdjustments_Delete = "Inventory.StockAdjustments.Delete";
            public const string StockTransfers_View = "Inventory.StockTransfers.View";
            public const string StockTransfers_Show = "Inventory.StockTransfers.Show";
            public const string StockTransfers_Create = "Inventory.StockTransfers.Create";
            public const string StockTransfers_Edit = "Inventory.StockTransfers.Edit";
            public const string StockTransfers_Delete = "Inventory.StockTransfers.Delete";
        }

        // ===== الحسابات (أكواد من Seeder) =====
        public static class Accounts
        {
            public const string Chart_View = "Accounts.Chart.View";
            public const string Chart_Edit = "Accounts.Chart.Edit";
            public const string Ledger_View = "Accounts.Ledger.View";
            public const string Ledger_Export = "Accounts.Ledger.Export";
            public const string CashReceipt = "Accounts.CashReceipt.View";
            public const string CashPayment = "Accounts.CashPayment.View";
            public const string DebitNote = "Accounts.DebitNote.View";
            public const string CreditNote = "Accounts.CreditNote.View";
            public const string Treasury_View = "Accounts.Treasury.View";
        }

        // ===== المستخدمون والصلاحيات (أكواد من Seeder) =====
        public static class Security
        {
            public const string Users_View = "Security.Users.View";
            public const string Users_Create = "Security.Users.Create";
            public const string Users_Edit = "Security.Users.Edit";
            public const string Users_Delete = "Security.Users.Delete";
            public const string Users_Export = "Security.Users.Export";
            public const string Roles_View = "Security.Roles.View";
            public const string Roles_Create = "Security.Roles.Create";
            public const string Roles_Edit = "Security.Roles.Edit";
            public const string Roles_Delete = "Security.Roles.Delete";
            public const string Roles_Export = "Security.Roles.Export";
            public const string Permissions_View = "Security.Permissions.View";
            public const string Permissions_Create = "Security.Permissions.Create";
            public const string Permissions_Edit = "Security.Permissions.Edit";
            public const string Permissions_Delete = "Security.Permissions.Delete";
            public const string Permissions_Export = "Security.Permissions.Export";
            public const string RolePermissions_View = "Security.RolePermissions.View";
            public const string RolePermissions_Create = "Security.RolePermissions.Create";
            public const string RolePermissions_Edit = "Security.RolePermissions.Edit";
            public const string RolePermissions_Delete = "Security.RolePermissions.Delete";
            public const string RolePermissions_Export = "Security.RolePermissions.Export";
            public const string UserRoles_View = "Security.UserRoles.View";
            public const string UserRoles_Create = "Security.UserRoles.Create";
            public const string UserRoles_Edit = "Security.UserRoles.Edit";
            public const string UserRoles_Delete = "Security.UserRoles.Delete";
            public const string UserRoles_Export = "Security.UserRoles.Export";
            public const string UserExtraPermissions_View = "Security.UserExtraPermissions.View";
            public const string UserExtraPermissions_Create = "Security.UserExtraPermissions.Create";
            public const string UserExtraPermissions_Edit = "Security.UserExtraPermissions.Edit";
            public const string UserExtraPermissions_Delete = "Security.UserExtraPermissions.Delete";
            public const string UserExtraPermissions_Export = "Security.UserExtraPermissions.Export";
            public const string UserDeniedPermissions_View = "Security.UserDeniedPermissions.View";
            public const string UserDeniedPermissions_Create = "Security.UserDeniedPermissions.Create";
            public const string UserDeniedPermissions_Edit = "Security.UserDeniedPermissions.Edit";
            public const string UserDeniedPermissions_Delete = "Security.UserDeniedPermissions.Delete";
            public const string UserDeniedPermissions_Export = "Security.UserDeniedPermissions.Export";
        }

        // ===== لوحات التحكم (مطابقة أسماء القائمة في الشريط) =====
        public static class Dashboard
        {
            public const string View = "Dashboard.Dashboard.View";
            /// <summary>مبيعاتي الشخصية — لوحة المندوب</summary>
            public const string Sales = "Dashboard.Dashboard.Sales";
            /// <summary>لوحة المدير</summary>
            public const string Manager = "Dashboard.Dashboard.Manager";
            /// <summary>لوحة الإدارة الكاملة</summary>
            public const string Owner = "Dashboard.Dashboard.Owner";
        }

        // ===== الإعدادات =====
        public static class Settings
        {
            public const string NumberSeries_View = "Settings.NumberSeries.View";
            public const string MovementLog_View = "Settings.MovementLog.View";
            public const string Policies_View = "Settings.Policies.View";
            public const string Policies_Create = "Settings.Policies.Create";
            public const string Policies_Edit = "Settings.Policies.Edit";
            public const string Policies_Delete = "Settings.Policies.Delete";
            public const string Policies_Export = "Settings.Policies.Export";
            public const string ItemGroups_View = "Settings.ItemGroups.View";
            public const string ItemGroupPolicies_View = "Settings.ItemGroupPolicies.View";
            public const string WarehousePolicies_View = "Settings.WarehousePolicies.View";
            public const string ProductGroups_View = "Settings.ProductGroups.View";
            public const string ProductBonusGroups_View = "Settings.ProductBonusGroups.View";
            public const string WarehousePolicyRules_View = "Settings.WarehousePolicyRules.View";
        }

        // ===== المناطق الجغرافية =====
        public static class Geo
        {
            public const string Governorates_View = "Geo.Governorates.View";
            public const string Governorates_Create = "Geo.Governorates.Create";
            public const string Governorates_Edit = "Geo.Governorates.Edit";
            public const string Governorates_Delete = "Geo.Governorates.Delete";
            public const string Governorates_Export = "Geo.Governorates.Export";
            public const string Districts_View = "Geo.Districts.View";
            public const string Districts_Create = "Geo.Districts.Create";
            public const string Districts_Edit = "Geo.Districts.Edit";
            public const string Districts_Delete = "Geo.Districts.Delete";
            public const string Districts_Export = "Geo.Districts.Export";
            public const string Areas_View = "Geo.Areas.View";
            public const string Areas_Create = "Geo.Areas.Create";
            public const string Areas_Edit = "Geo.Areas.Edit";
            public const string Areas_Delete = "Geo.Areas.Delete";
            public const string Areas_Export = "Geo.Areas.Export";
            public const string Branches_View = "Geo.Branches.View";
            public const string Branches_Create = "Geo.Branches.Create";
            public const string Branches_Edit = "Geo.Branches.Edit";
            public const string Branches_Delete = "Geo.Branches.Delete";
            public const string Branches_Export = "Geo.Branches.Export";
        }

        // ===== حجم تعامل عميل =====
        public static class CustomerVolume
        {
            public const string View = "Customers.CustomerVolume.View";
            public const string Create = "Customers.CustomerVolume.Create";
            public const string Edit = "Customers.CustomerVolume.Edit";
            public const string Delete = "Customers.CustomerVolume.Delete";
            public const string Export = "Customers.CustomerVolume.Export";
        }

        // ===== كشف حساب عميل =====
        public static class CustomerLedger
        {
            public const string View = "Customers.Ledger.View";
        }

        // ===== حركة صنف / سجل الأسعار =====
        public static class InventoryScreens
        {
            public const string ProductMovements_View = "Inventory.ProductMovements.View";
            public const string PriceHistory_View = "Inventory.PriceHistory.View";
            public const string Warehouses_View = "Inventory.Warehouses.View";
            public const string Warehouses_Create = "Inventory.Warehouses.Create";
            public const string Warehouses_Edit = "Inventory.Warehouses.Edit";
            public const string Warehouses_Delete = "Inventory.Warehouses.Delete";
            public const string Warehouses_Export = "Inventory.Warehouses.Export";
            public const string Categories_View = "Inventory.Categories.View";
            public const string Categories_Create = "Inventory.Categories.Create";
            public const string Categories_Edit = "Inventory.Categories.Edit";
            public const string Categories_Delete = "Inventory.Categories.Delete";
            public const string Categories_Export = "Inventory.Categories.Export";
            public const string StockLedger_View = "Inventory.StockLedger.View";
            public const string StockLedger_Export = "Inventory.StockLedger.Export";
            public const string FifoMap_View = "Inventory.FifoMap.View";
            public const string StockAdjustmentLines_View = "Inventory.StockAdjustmentLines.View";
            public const string StockTransferLines_View = "Inventory.StockTransferLines.View";
            public const string StockBatches_View = "Inventory.StockBatches.View";
            public const string Batches_View = "Inventory.Batches.View";
        }

        // ===== المشتريات =====
        public static class Purchasing
        {
            public const string Invoices_View = "Purchasing.Invoices.View";
            public const string Invoices_Show = "Purchasing.Invoices.Show";
            public const string Invoices_Create = "Purchasing.Invoices.Create";
            public const string Invoices_Edit = "Purchasing.Invoices.Edit";
            public const string Invoices_Delete = "Purchasing.Invoices.Delete";
            public const string Invoices_Export = "Purchasing.Invoices.Export";
            public const string Invoices_Post = "Purchasing.Invoices.Post";
            public const string Invoices_UnPost = "Purchasing.Invoices.UnPost";
            public const string Invoices_Print = "Purchasing.Invoices.Print";
            public const string Requests_View = "Purchasing.Requests.View";
            public const string Requests_Show = "Purchasing.Requests.Show";
            public const string Requests_Create = "Purchasing.Requests.Create";
            public const string Requests_Edit = "Purchasing.Requests.Edit";
            public const string Requests_Delete = "Purchasing.Requests.Delete";
            public const string Returns_View = "Purchasing.Returns.View";
            public const string Returns_Show = "Purchasing.Returns.Show";
            public const string Returns_Create = "Purchasing.Returns.Create";
            public const string Returns_Edit = "Purchasing.Returns.Edit";
            public const string Returns_Delete = "Purchasing.Returns.Delete";
            public const string InvoiceLines_View = "Purchasing.InvoiceLines.View";
            public const string RequestLines_View = "Purchasing.RequestLines.View";
            public const string ReturnLines_View = "Purchasing.ReturnLines.View";
        }

        // ===== قوائم أصناف المبيعات والمشتريات =====
        public static class SalesLines
        {
            public const string InvoiceLines_View = "Sales.InvoiceLines.View";
            public const string ReturnLines_View = "Sales.ReturnLines.LinesView";
            public const string OrderLines_View = "Sales.OrderLines.View";
        }

        public static class SalesDiscounts
        {
            public const string DiscountOverrides_View = "Sales.DiscountOverrides.View";
        }

        // ===== مرتجعات وأوامر المبيعات =====
        public static class SalesReturns
        {
            public const string View = "Sales.Returns.View";
            public const string Show = "Sales.Returns.Show";
            public const string Create = "Sales.Returns.Create";
            public const string Edit = "Sales.Returns.Edit";
            public const string Delete = "Sales.Returns.Delete";
            public const string Export = "Sales.Returns.Export";
            public const string Post = "Sales.Returns.Post";
            public const string UnPost = "Sales.Returns.UnPost";
            public const string Print = "Sales.Returns.Print";
        }

        public static class SalesOrders
        {
            public const string View = "Sales.Orders.View";
            public const string Show = "Sales.Orders.Show";
            public const string Create = "Sales.Orders.Create";
            public const string Edit = "Sales.Orders.Edit";
            public const string Delete = "Sales.Orders.Delete";
            public const string Export = "Sales.Orders.Export";
            public const string Post = "Sales.Orders.Post";
            public const string UnPost = "Sales.Orders.UnPost";
            public const string Print = "Sales.Orders.Print";
        }

        // ===== دفتر الأستاذ (قيود) =====
        public static class Ledger
        {
            public const string View = "Accounts.Ledger.View";
            public const string Create = "Accounts.Ledger.Create";
            public const string Edit = "Accounts.Ledger.Edit";
            public const string Delete = "Accounts.Ledger.Delete";
            public const string Export = "Accounts.Ledger.Export";
        }

        // ===== إذن قبض / صرف / إشعار خصم / إشعار إضافة =====
        public static class AccountsDocuments
        {
            public const string CashReceipt_View = "Accounts.CashReceipt.View";
            public const string CashReceipt_Show = "Accounts.CashReceipt.Show";
            public const string CashReceipt_Create = "Accounts.CashReceipt.Create";
            public const string CashPayment_View = "Accounts.CashPayment.View";
            public const string CashPayment_Show = "Accounts.CashPayment.Show";
            public const string CashPayment_Create = "Accounts.CashPayment.Create";
            public const string DebitNote_View = "Accounts.DebitNote.View";
            public const string DebitNote_Show = "Accounts.DebitNote.Show";
            public const string DebitNote_Create = "Accounts.DebitNote.Create";
            public const string CreditNote_View = "Accounts.CreditNote.View";
            public const string CreditNote_Show = "Accounts.CreditNote.Show";
            public const string CreditNote_Create = "Accounts.CreditNote.Create";
        }

        // ===== التقارير =====
        public static class Reports
        {
            public const string CustomerBalances_View = "Reports.CustomerBalances.View";
            public const string ProductBalances_View = "Reports.ProductBalances.View";
            public const string ProductProfits_View = "Reports.ProductProfits.View";
            public const string CustomerProfits_View = "Reports.CustomerProfits.View";
            public const string BonusSales_View = "Reports.BonusSales.View";
        }

        /// <summary>
        /// ترجع كل الصلاحيات فى شكل List علشان نستخدمها فى Seeder.
        /// Code = الكود الثابت
        /// NameAr = اسم الصلاحية بالعربي
        /// Module = اسم الموديول (اللى ظاهر فى الـ Navbar بالعربي)
        /// Description = وصف مختصر
        /// </summary>
        public static IEnumerable<(string Code, string NameAr, string Module, string Description)> GetAll()
        {
            // ===================== المبيعات =====================

            // فواتير المبيعات
            yield return (SalesInvoices.View, "عرض فواتير المبيعات", "المبيعات", "فتح شاشة قائمة فواتير المبيعات");
            yield return (SalesInvoices.Show, "عرض تفاصيل فاتورة مبيعات", "المبيعات", "فتح شاشة فاتورة واحدة للعرض");
            yield return (SalesInvoices.Create, "إنشاء فاتورة مبيعات جديدة", "المبيعات", "إضافة فاتورة جديدة");
            yield return (SalesInvoices.Edit, "تعديل فاتورة مبيعات", "المبيعات", "تعديل بيانات فاتورة موجودة");
            yield return (SalesInvoices.Delete, "حذف فاتورة مبيعات", "المبيعات", "حذف فاتورة مع السطور التابعة لها");
            yield return (SalesInvoices.Export, "تصدير فواتير المبيعات", "المبيعات", "تصدير قائمة الفواتير إلى Excel/CSV");
            yield return (SalesInvoices.Post, "ترحيل فاتورة مبيعات", "المبيعات", "ترحيل الفاتورة للمخزون والحسابات");
            yield return (SalesInvoices.UnPost, "إلغاء ترحيل فاتورة مبيعات", "المبيعات", "إلغاء ترحيل فاتورة سبق ترحيلها");
            yield return (SalesInvoices.Print, "طباعة فاتورة المبيعات", "المبيعات", "طباعة الفاتورة بتصميم الطباعة");

            // العملاء (منيو العملاء)
            yield return (Customers.View, "عرض العملاء", "العملاء", "قائمة العملاء");
            yield return (Customers.Create, "إضافة عميل جديد", "العملاء", "تسجيل عميل جديد");
            yield return (Customers.Edit, "تعديل بيانات عميل", "العملاء", "تعديل بيانات عميل موجود");
            yield return (Customers.Delete, "حذف عميل", "العملاء", "حذف عميل (مع مراعاة القيود)");
            yield return (Customers.Export, "تصدير العملاء", "العملاء", "تصدير قائمة العملاء إلى ملف");

            // ===================== إدارة المخازن =====================

            // الأصناف
            yield return (Products.View, "عرض الأصناف", "إدارة المخازن", "قائمة الأصناف");
            yield return (Products.Create, "إضافة صنف جديد", "إدارة المخازن", "تسجيل صنف جديد");
            yield return (Products.Edit, "تعديل صنف", "إدارة المخازن", "تعديل بيانات صنف");
            yield return (Products.Delete, "حذف صنف", "إدارة المخازن", "حذف صنف (مع مراعاة القيود)");
            yield return (Products.Export, "تصدير الأصناف", "إدارة المخازن", "تصدير قائمة الأصناف");

            // حركة المخزون
            yield return (Stock.StockAdjustments_View, "عرض تسويات الجرد", "إدارة المخازن", "قائمة تسويات المخزون");
            yield return (Stock.StockAdjustments_Edit, "إنشاء/تعديل تسوية جرد", "إدارة المخازن", "إدخال تسوية جرد جديدة أو تعديلها");
            yield return (Stock.StockTransfers_View, "عرض تحويلات المخزون", "إدارة المخازن", "قائمة تحويلات بين المخازن");
            yield return (Stock.StockTransfers_Edit, "إنشاء/تعديل تحويل مخزون", "إدارة المخازن", "تحويل كمية من مخزن لمخزن آخر");

            // ===================== الحسابات =====================

            yield return (Accounts.Chart_View, "عرض شجرة الحسابات", "الحسابات", "قائمة شجرة الحسابات");
            yield return (Accounts.Chart_Edit, "تعديل شجرة الحسابات", "الحسابات", "إضافة/تعديل/حذف حساب فى الشجرة");
            yield return (Accounts.Ledger_View, "عرض دفتر الأستاذ", "الحسابات", "استعلام حركات حساب (دفتر الأستاذ)");
            yield return (Accounts.Ledger_Export, "تصدير دفتر الأستاذ", "الحسابات", "تصدير حركات حساب إلى Excel");
            yield return (Accounts.CashReceipt, "إذن قبض نقدي", "الحسابات", "إنشاء إيصال قبض نقدي");
            yield return (Accounts.CashPayment, "إذن صرف نقدي", "الحسابات", "إنشاء إيصال صرف نقدي");
            yield return (Accounts.DebitNote, "إشعار خصم", "الحسابات", "تسجيل إشعار خصم لعميل أو مورد");
            yield return (Accounts.CreditNote, "إشعار إضافة", "الحسابات", "تسجيل إشعار إضافة لعميل أو مورد");

            // ===================== المستخدمون والصلاحيات =====================

            yield return (Security.Users_View, "عرض المستخدمين", "المستخدمون والصلاحيات", "قائمة المستخدمين");
            yield return (Security.Users_Edit, "إدارة المستخدمين", "المستخدمون والصلاحيات", "إضافة وتعديل وحذف المستخدمين");
            yield return (Security.Roles_View, "عرض الأدوار", "المستخدمون والصلاحيات", "قائمة الأدوار");
            yield return (Security.Roles_Edit, "إدارة الأدوار", "المستخدمون والصلاحيات", "إضافة/تعديل/حذف دور");
            yield return (Security.Permissions_View, "عرض الصلاحيات", "المستخدمون والصلاحيات", "قائمة الصلاحيات الأساسية");
            yield return (Security.Permissions_Edit, "إدارة الصلاحيات والأدوار", "المستخدمون والصلاحيات", "تعديل الصلاحيات وربطها بالأدوار والمستخدمين");
        }

        /// <summary>
        /// ترجع كل الصلاحيات للمزامنة مع قاعدة البيانات: إضافة الناقصة وتصحيح أسماء والموديولات لتطابق مكونات البرنامج.
        /// </summary>
        public static IEnumerable<(string Code, string NameAr, string Module)> GetAllForSync()
        {
            // ===================== لوحات التحكم (مطابقة عناصر القائمة في الشريط) =====================
            yield return (Dashboard.View, "لوحة التحكم", "لوحات التحكم");
            yield return (Dashboard.Sales, "مبيعاتي الشخصية", "لوحات التحكم");
            yield return (Dashboard.Manager, "لوحة المدير", "لوحات التحكم");
            yield return (Dashboard.Owner, "لوحة الإدارة الكاملة", "لوحات التحكم");

            // ===================== الإعدادات =====================
            yield return (Settings.NumberSeries_View, "الترقيم", "الإعدادات");
            yield return (Settings.MovementLog_View, "سجل الحركات", "الإعدادات");
            yield return (Settings.Policies_View, "السياسات", "الإعدادات");
            yield return (Settings.Policies_Create, "إضافة سياسة", "الإعدادات");
            yield return (Settings.Policies_Edit, "تعديل سياسة", "الإعدادات");
            yield return (Settings.Policies_Delete, "حذف سياسة", "الإعدادات");
            yield return (Settings.Policies_Export, "تصدير السياسات", "الإعدادات");
            yield return (Settings.ItemGroups_View, "مجموعات الأصناف", "الإعدادات");
            yield return (Settings.ProductGroups_View, "مجموعات الأصناف الخاصة بالسياسة", "الإعدادات");
            yield return (Settings.ProductBonusGroups_View, "مجموعات الأصناف الخاصة بالحافز", "الإعدادات");
            yield return (Settings.ItemGroupPolicies_View, "سياسة مجموعات الأصناف", "الإعدادات");
            yield return (Settings.WarehousePolicies_View, "سياسات المخازن", "الإعدادات");
            yield return (Settings.WarehousePolicyRules_View, "سياسة المخازن", "الإعدادات");

            // ===================== المستخدمون والصلاحيات =====================
            yield return (Security.Users_View, "قائمة المستخدمين", "المستخدمون والصلاحيات");
            yield return (Security.Users_Create, "إضافة مستخدم", "المستخدمون والصلاحيات");
            yield return (Security.Users_Edit, "تعديل مستخدم", "المستخدمون والصلاحيات");
            yield return (Security.Users_Delete, "حذف مستخدم", "المستخدمون والصلاحيات");
            yield return (Security.Users_Export, "تصدير المستخدمين", "المستخدمون والصلاحيات");
            yield return (Security.Roles_View, "قائمة الأدوار", "المستخدمون والصلاحيات");
            yield return (Security.Roles_Create, "إضافة دور", "المستخدمون والصلاحيات");
            yield return (Security.Roles_Edit, "تعديل صلاحيات الأدوار", "المستخدمون والصلاحيات");
            yield return (Security.Roles_Delete, "حذف دور", "المستخدمون والصلاحيات");
            yield return (Security.Roles_Export, "تصدير الأدوار", "المستخدمون والصلاحيات");
            yield return (Security.Permissions_View, "قائمة الصلاحيات", "المستخدمون والصلاحيات");
            yield return (Security.Permissions_Create, "إضافة صلاحية", "المستخدمون والصلاحيات");
            yield return (Security.Permissions_Edit, "تعديل صلاحية", "المستخدمون والصلاحيات");
            yield return (Security.Permissions_Delete, "حذف صلاحية", "المستخدمون والصلاحيات");
            yield return (Security.Permissions_Export, "تصدير الصلاحيات", "المستخدمون والصلاحيات");
            yield return (Security.RolePermissions_View, "صلاحيات الأدوار", "المستخدمون والصلاحيات");
            yield return (Security.RolePermissions_Create, "إضافة صلاحيات أدوار", "المستخدمون والصلاحيات");
            yield return (Security.RolePermissions_Edit, "تعديل صلاحيات الأدوار", "المستخدمون والصلاحيات");
            yield return (Security.RolePermissions_Delete, "حذف صلاحيات أدوار", "المستخدمون والصلاحيات");
            yield return (Security.RolePermissions_Export, "تصدير صلاحيات الأدوار", "المستخدمون والصلاحيات");
            yield return (Security.UserRoles_View, "أدوار المستخدمين", "المستخدمون والصلاحيات");
            yield return (Security.UserRoles_Create, "ربط مستخدم بدور", "المستخدمون والصلاحيات");
            yield return (Security.UserRoles_Edit, "تعديل أدوار المستخدم", "المستخدمون والصلاحيات");
            yield return (Security.UserRoles_Delete, "إلغاء ربط دور", "المستخدمون والصلاحيات");
            yield return (Security.UserRoles_Export, "تصدير أدوار المستخدمين", "المستخدمون والصلاحيات");
            yield return (Security.UserExtraPermissions_View, "صلاحيات إضافية للمستخدم", "المستخدمون والصلاحيات");
            yield return (Security.UserExtraPermissions_Create, "إضافة صلاحية إضافية", "المستخدمون والصلاحيات");
            yield return (Security.UserExtraPermissions_Edit, "تعديل صلاحية إضافية", "المستخدمون والصلاحيات");
            yield return (Security.UserExtraPermissions_Delete, "حذف صلاحية إضافية", "المستخدمون والصلاحيات");
            yield return (Security.UserExtraPermissions_Export, "تصدير الصلاحيات الإضافية", "المستخدمون والصلاحيات");
            yield return (Security.UserDeniedPermissions_View, "استثناءات صلاحيات المستخدمين", "المستخدمون والصلاحيات");
            yield return (Security.UserDeniedPermissions_Create, "إضافة استثناء صلاحية", "المستخدمون والصلاحيات");
            yield return (Security.UserDeniedPermissions_Edit, "تعديل استثناء صلاحية", "المستخدمون والصلاحيات");
            yield return (Security.UserDeniedPermissions_Delete, "حذف استثناء صلاحية", "المستخدمون والصلاحيات");
            yield return (Security.UserDeniedPermissions_Export, "تصدير الاستثناءات", "المستخدمون والصلاحيات");

            // ===================== المناطق الجغرافية =====================
            yield return (Geo.Governorates_View, "المحافظات", "المناطق الجغرافية");
            yield return (Geo.Governorates_Create, "إضافة محافظة", "المناطق الجغرافية");
            yield return (Geo.Governorates_Edit, "تعديل محافظة", "المناطق الجغرافية");
            yield return (Geo.Governorates_Delete, "حذف محافظة", "المناطق الجغرافية");
            yield return (Geo.Governorates_Export, "تصدير المحافظات", "المناطق الجغرافية");
            yield return (Geo.Districts_View, "الأحياء/المراكز", "المناطق الجغرافية");
            yield return (Geo.Districts_Create, "إضافة حي/مركز", "المناطق الجغرافية");
            yield return (Geo.Districts_Edit, "تعديل حي/مركز", "المناطق الجغرافية");
            yield return (Geo.Districts_Delete, "حذف حي/مركز", "المناطق الجغرافية");
            yield return (Geo.Districts_Export, "تصدير الأحياء", "المناطق الجغرافية");
            yield return (Geo.Areas_View, "المناطق", "المناطق الجغرافية");
            yield return (Geo.Areas_Create, "إضافة منطقة", "المناطق الجغرافية");
            yield return (Geo.Areas_Edit, "تعديل منطقة", "المناطق الجغرافية");
            yield return (Geo.Areas_Delete, "حذف منطقة", "المناطق الجغرافية");
            yield return (Geo.Areas_Export, "تصدير المناطق", "المناطق الجغرافية");
            yield return (Geo.Branches_View, "الفروع", "المناطق الجغرافية");
            yield return (Geo.Branches_Create, "إضافة فرع", "المناطق الجغرافية");
            yield return (Geo.Branches_Edit, "تعديل فرع", "المناطق الجغرافية");
            yield return (Geo.Branches_Delete, "حذف فرع", "المناطق الجغرافية");
            yield return (Geo.Branches_Export, "تصدير الفروع", "المناطق الجغرافية");

            // ===================== العملاء =====================
            yield return (Customers.View, "قائمة العملاء", "العملاء");
            yield return (Customers.Create, "إضافة عميل جديد", "العملاء");
            yield return (Customers.Edit, "تعديل بيانات عميل", "العملاء");
            yield return (Customers.Delete, "حذف عميل", "العملاء");
            yield return (Customers.Export, "تصدير العملاء", "العملاء");

            // حجم تعامل عميل
            yield return (CustomerVolume.View, "حجم تعامل عميل", "العملاء");
            yield return (CustomerVolume.Create, "إضافة حجم تعامل", "العملاء");
            yield return (CustomerVolume.Edit, "تعديل حجم تعامل", "العملاء");
            yield return (CustomerVolume.Delete, "حذف حجم تعامل", "العملاء");
            yield return (CustomerVolume.Export, "تصدير حجم تعامل", "العملاء");
            yield return (CustomerLedger.View, "كشف حساب عميل", "العملاء");

            // ===================== إدارة المخازن =====================
            yield return (Products.View, "قائمة الأصناف", "إدارة المخازن");
            yield return (Products.Create, "إضافة صنف جديد", "إدارة المخازن");
            yield return (Products.Edit, "تعديل صنف", "إدارة المخازن");
            yield return (Products.Delete, "حذف صنف", "إدارة المخازن");
            yield return (Products.Export, "تصدير الأصناف", "إدارة المخازن");
            yield return (Products.Import, "استيراد أصناف من الإكسل", "إدارة المخازن");

            yield return (InventoryScreens.ProductMovements_View, "حركة الصنف", "إدارة المخازن");
            yield return (InventoryScreens.PriceHistory_View, "قائمة سجل تغير الأسعار", "إدارة المخازن");
            yield return (InventoryScreens.Warehouses_View, "قائمة المخازن", "إدارة المخازن");
            yield return (InventoryScreens.Warehouses_Create, "إضافة مخزن", "إدارة المخازن");
            yield return (InventoryScreens.Warehouses_Edit, "تعديل مخزن", "إدارة المخازن");
            yield return (InventoryScreens.Warehouses_Delete, "حذف مخزن", "إدارة المخازن");
            yield return (InventoryScreens.Warehouses_Export, "تصدير المخازن", "إدارة المخازن");
            yield return (InventoryScreens.Categories_View, "فئات الأصناف", "إدارة المخازن");
            yield return (InventoryScreens.Categories_Create, "إضافة فئة", "إدارة المخازن");
            yield return (InventoryScreens.Categories_Edit, "تعديل فئة", "إدارة المخازن");
            yield return (InventoryScreens.Categories_Delete, "حذف فئة", "إدارة المخازن");
            yield return (InventoryScreens.Categories_Export, "تصدير الفئات", "إدارة المخازن");
            yield return (InventoryScreens.StockLedger_View, "دفتر الحركة المخزنية", "إدارة المخازن");
            yield return (InventoryScreens.StockLedger_Export, "تصدير دفتر الحركة المخزنية", "إدارة المخازن");
            yield return (InventoryScreens.FifoMap_View, "ربط FIFO", "إدارة المخازن");
            yield return (InventoryScreens.StockAdjustmentLines_View, "قائمة أصناف التسويات الجردية", "إدارة المخازن");
            yield return (InventoryScreens.StockTransferLines_View, "تفاصيل التحويل بين المخازن", "إدارة المخازن");
            yield return (InventoryScreens.StockBatches_View, "مخزون التشغيلات", "إدارة المخازن");
            yield return (InventoryScreens.Batches_View, "قائمة التشغيلات", "إدارة المخازن");

            yield return (Stock.StockAdjustments_View, "قائمة التسويات الجردية", "إدارة المخازن");
            yield return (Stock.StockAdjustments_Show, "فاتورة تسوية جردية", "إدارة المخازن");
            yield return (Stock.StockAdjustments_Create, "إنشاء تسوية جرد", "إدارة المخازن");
            yield return (Stock.StockAdjustments_Edit, "تعديل تسوية جرد", "إدارة المخازن");
            yield return (Stock.StockAdjustments_Delete, "حذف تسوية جرد", "إدارة المخازن");
            yield return (Stock.StockTransfers_View, "قائمة التحويلات بين المخازن", "إدارة المخازن");
            yield return (Stock.StockTransfers_Show, "تحويلات بين المخازن", "إدارة المخازن");
            yield return (Stock.StockTransfers_Create, "إنشاء تحويل مخزون", "إدارة المخازن");
            yield return (Stock.StockTransfers_Edit, "تعديل تحويل مخزون", "إدارة المخازن");
            yield return (Stock.StockTransfers_Delete, "حذف تحويل مخزون", "إدارة المخازن");

            // ===================== المبيعات =====================
            yield return (SalesInvoices.View, "قائمة فواتير المبيعات", "المبيعات");
            yield return (SalesInvoices.Show, "عرض فاتورة مبيعات", "المبيعات");
            yield return (SalesInvoices.Create, "فاتورة مبيعات جديدة", "المبيعات");
            yield return (SalesInvoices.Edit, "تعديل فاتورة مبيعات", "المبيعات");
            yield return (SalesInvoices.Delete, "حذف فاتورة مبيعات", "المبيعات");
            yield return (SalesInvoices.Export, "تصدير فواتير المبيعات", "المبيعات");
            yield return (SalesInvoices.Post, "ترحيل فاتورة مبيعات", "المبيعات");
            yield return (SalesInvoices.UnPost, "إلغاء ترحيل فاتورة مبيعات", "المبيعات");
            yield return (SalesInvoices.Print, "طباعة فاتورة المبيعات", "المبيعات");

            yield return (SalesReturns.View, "قائمة مرتجعات المبيعات", "المبيعات");
            yield return (SalesReturns.Show, "عرض مرتجع مبيعات", "المبيعات");
            yield return (SalesReturns.Create, "مرتجع مبيعات جديد", "المبيعات");
            yield return (SalesReturns.Edit, "تعديل مرتجع مبيعات", "المبيعات");
            yield return (SalesReturns.Delete, "حذف مرتجع مبيعات", "المبيعات");
            yield return (SalesReturns.Export, "تصدير مرتجعات المبيعات", "المبيعات");
            yield return (SalesReturns.Post, "ترحيل مرتجع مبيعات", "المبيعات");
            yield return (SalesReturns.UnPost, "إلغاء ترحيل مرتجع مبيعات", "المبيعات");
            yield return (SalesReturns.Print, "طباعة مرتجع المبيعات", "المبيعات");

            yield return (SalesOrders.View, "قائمة أوامر المبيعات", "المبيعات");
            yield return (SalesOrders.Show, "عرض أمر بيع", "المبيعات");
            yield return (SalesOrders.Create, "أمر بيع جديد", "المبيعات");
            yield return (SalesOrders.Edit, "تعديل أمر بيع", "المبيعات");
            yield return (SalesOrders.Delete, "حذف أمر بيع", "المبيعات");
            yield return (SalesOrders.Export, "تصدير أوامر المبيعات", "المبيعات");
            yield return (SalesOrders.Post, "ترحيل أمر بيع", "المبيعات");
            yield return (SalesOrders.UnPost, "إلغاء ترحيل أمر بيع", "المبيعات");
            yield return (SalesOrders.Print, "طباعة أمر البيع", "المبيعات");
            yield return (SalesLines.InvoiceLines_View, "قائمة أصناف المبيعات", "المبيعات");
            yield return (SalesLines.ReturnLines_View, "قائمة أصناف مرتجعات المبيعات", "المبيعات");
            yield return (SalesLines.OrderLines_View, "قائمة أصناف أوامر المبيعات", "المبيعات");
            yield return (SalesDiscounts.DiscountOverrides_View, "قائمة الخصم اليدوي للبيع", "المبيعات");

            // ===================== المشتريات =====================
            yield return (Purchasing.Invoices_View, "قائمة فواتير المشتريات", "المشتريات");
            yield return (Purchasing.Invoices_Show, "عرض فاتورة مشتريات", "المشتريات");
            yield return (Purchasing.Invoices_Create, "فاتورة مشتريات جديدة", "المشتريات");
            yield return (Purchasing.Invoices_Edit, "تعديل فاتورة مشتريات", "المشتريات");
            yield return (Purchasing.Invoices_Delete, "حذف فاتورة مشتريات", "المشتريات");
            yield return (Purchasing.Invoices_Export, "تصدير فواتير المشتريات", "المشتريات");
            yield return (Purchasing.Invoices_Post, "ترحيل فاتورة مشتريات", "المشتريات");
            yield return (Purchasing.Invoices_UnPost, "إلغاء ترحيل فاتورة مشتريات", "المشتريات");
            yield return (Purchasing.Invoices_Print, "طباعة فاتورة المشتريات", "المشتريات");
            yield return (Purchasing.Requests_View, "قائمة طلبات الشراء", "المشتريات");
            yield return (Purchasing.Requests_Show, "عرض طلب شراء", "المشتريات");
            yield return (Purchasing.Requests_Create, "طلب شراء جديد", "المشتريات");
            yield return (Purchasing.Requests_Edit, "تعديل طلب شراء", "المشتريات");
            yield return (Purchasing.Requests_Delete, "حذف طلب شراء", "المشتريات");
            yield return (Purchasing.Returns_View, "قائمة مرتجعات المشتريات", "المشتريات");
            yield return (Purchasing.Returns_Show, "عرض مرتجع مشتريات", "المشتريات");
            yield return (Purchasing.Returns_Create, "مرتجع مشتريات جديد", "المشتريات");
            yield return (Purchasing.Returns_Edit, "تعديل مرتجع مشتريات", "المشتريات");
            yield return (Purchasing.Returns_Delete, "حذف مرتجع مشتريات", "المشتريات");
            yield return (Purchasing.InvoiceLines_View, "قائمة أصناف فواتير المشتريات", "المشتريات");
            yield return (Purchasing.RequestLines_View, "قائمة أصناف طلبات الشراء", "المشتريات");
            yield return (Purchasing.ReturnLines_View, "قائمة أصناف مرتجعات الشراء", "المشتريات");

            // ===================== الحسابات =====================
            yield return (Accounts.Chart_View, "شجرة الحسابات", "الحسابات");
            yield return (Accounts.Chart_Edit, "تعديل شجرة الحسابات", "الحسابات");
            yield return (Accounts.Ledger_View, "دفتر الأستاذ", "الحسابات");
            yield return (Accounts.Ledger_Export, "تصدير دفتر الأستاذ", "الحسابات");
            yield return (Accounts.CashReceipt, "إذن قبض نقدي", "الحسابات");
            yield return (Accounts.CashPayment, "إذن صرف نقدي", "الحسابات");
            yield return (Accounts.DebitNote, "إشعار خصم", "الحسابات");
            yield return (Accounts.CreditNote, "إشعار إضافة", "الحسابات");
            yield return (Accounts.Treasury_View, "الخزينة الرئيسية", "الحسابات");

            yield return (Ledger.View, "دفتر الأستاذ (قيود)", "الحسابات");
            yield return (Ledger.Create, "إنشاء قيد", "الحسابات");
            yield return (Ledger.Edit, "تعديل قيد", "الحسابات");
            yield return (Ledger.Delete, "حذف قيد", "الحسابات");
            yield return (Ledger.Export, "تصدير القيود", "الحسابات");

            yield return (AccountsDocuments.CashReceipt_View, "قائمة أذون القبض", "الحسابات");
            yield return (AccountsDocuments.CashReceipt_Show, "عرض إذن قبض", "الحسابات");
            yield return (AccountsDocuments.CashReceipt_Create, "إنشاء إذن قبض", "الحسابات");
            yield return (AccountsDocuments.CashPayment_View, "قائمة أذون الصرف", "الحسابات");
            yield return (AccountsDocuments.CashPayment_Show, "عرض إذن صرف", "الحسابات");
            yield return (AccountsDocuments.CashPayment_Create, "إنشاء إذن صرف", "الحسابات");
            yield return (AccountsDocuments.DebitNote_View, "قائمة إشعارات الخصم", "الحسابات");
            yield return (AccountsDocuments.DebitNote_Show, "عرض إشعار خصم", "الحسابات");
            yield return (AccountsDocuments.DebitNote_Create, "إنشاء إشعار خصم", "الحسابات");
            yield return (AccountsDocuments.CreditNote_View, "قائمة إشعارات الإضافة", "الحسابات");
            yield return (AccountsDocuments.CreditNote_Show, "عرض إشعار إضافة", "الحسابات");
            yield return (AccountsDocuments.CreditNote_Create, "إنشاء إشعار إضافة", "الحسابات");

            // ===================== التقارير =====================
            yield return (Reports.CustomerBalances_View, "تقرير أرصدة العملاء", "التقارير");
            yield return (Reports.ProductBalances_View, "تقرير أرصدة الأصناف", "التقارير");
            yield return (Reports.ProductProfits_View, "تقرير أرباح الأصناف", "التقارير");
            yield return (Reports.CustomerProfits_View, "تقرير أرباح العملاء", "التقارير");
            yield return (Reports.BonusSales_View, "تقرير مبيعات البونص لكل مستخدم", "التقارير");
        }
    }
}
