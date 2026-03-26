using System;
using System.Collections.Generic;
using System.Linq;

namespace ERP.Security
{
    /// <summary>
    /// مصدر واحد لصلاحيات النظام: كل صلاحية = كونترولر.أكشن (مثل SalesInvoices.Index).
    /// تُستخرج من الكونترولرات ويُستخدم نفس الكود في [RequirePermission] وفي المزامنة مع قاعدة البيانات.
    /// </summary>
    public static class PermissionCodes
    {
        /// <summary>
        /// تعريف الصلاحيات: (اسم الكونترولر بدون Controller، مصفوفة الأكشنات، الموديول بالعربي).
        /// كل أكشن يُنتج كود صلاحية = ControllerName.ActionName
        /// حذف من القائمة vs من داخل الشاشة: DeleteOneFromList و DeleteOneFromShow لكل من فواتير المبيعات والمشتريات.
        /// </summary>
        private static readonly (string Controller, string[] Actions, string Module)[] Definitions =
        {
            ("Dashboard", new[] { "Sales" }, "لوحات التحكم"),
            ("Home", new[] { "Index" }, "لوحات التحكم"),
            ("SalesInvoices", new[] { "Index", "Show", "Create", "Edit", "Export", "SaveHeader", "GetProductsForDatalist", "GetAlternativeProducts", "GetSalesProductInfo", "DiagnosePolicy", "AddLineJson", "RemoveLineJson", "ClearAllLinesJson", "SaveTaxJson", "PostInvoice", "CreateFullReturn", "OpenInvoice", "Delete", "DeleteConfirmed", "DeleteOneFromList", "DeleteOneFromShow", "BulkDelete", "DeleteAll" }, "المبيعات"),
            ("SalesReturns", new[] { "Index", "Show", "Create", "Edit", "Delete", "Export", "SaveHeader", "DeleteConfirmed", "BulkDelete", "DeleteAll", "GetInvoiceItems", "SearchProductsForReturn", "GetProductsForReturnDatalist", "AddLineJson", "RemoveLineJson", "ClearLinesJson", "OpenReturn", "PostReturn" }, "المبيعات"),
            ("SalesOrders", new[] { "Index", "Show", "Create", "Edit", "Delete", "Export", "DeleteConfirmed", "BulkDelete", "DeleteAll" }, "المبيعات"),
            ("SalesInvoiceLines", new[] { "Index", "Details", "Delete", "BulkDelete", "DeleteAll", "Export" }, "المبيعات"),
            ("SalesReturnLines", new[] { "Index", "Delete", "BulkDelete", "DeleteAll", "Export" }, "المبيعات"),
            ("SOLines", new[] { "Index", "Details", "Delete", "BulkDelete", "DeleteAll", "Export" }, "المبيعات"),
            ("ProductDiscountOverrides", new[] { "Index", "Edit", "Delete", "DeleteConfirmed", "Update" }, "المبيعات"),
            ("Customers", new[] { "Index", "Show", "Create", "Edit", "Delete", "Export", "GetDefaultAccountForParty", "GetDistrictsByGovernorate", "GetAreasByDistrict", "DeleteConfirmed", "BulkDelete", "DeleteAll" }, "العملاء"),
            ("CustomerLedger", new[] { "Index" }, "العملاء"),
            ("Products", new[] { "Index", "Details", "Create", "Edit", "Delete", "Export", "Show", "Import", "GetColumnValues", "DeleteConfirmed", "BulkDelete", "DeleteAll", "SearchProducts", "SearchProductsByCode", "SearchParties" }, "إدارة المخازن"),
            ("Categories", new[] { "Index", "Show", "Details", "Create", "Edit", "Delete", "DeleteConfirmed", "BulkDelete", "DeleteAll", "Export" }, "إدارة المخازن"),
            ("Warehouses", new[] { "Index", "Show", "Create", "Edit", "Delete", "Export", "DeleteConfirmed", "BulkDelete", "DeleteAll", "GetColumnValues" }, "إدارة المخازن"),
            ("StockAdjustments", new[] { "Index", "Show", "Details", "Create", "Edit", "Delete", "Export", "DeleteConfirmed", "BulkDelete", "DeleteAll", "CreateHeaderJson", "UpdateHeaderJson", "GetStockAdjustmentProductInfo", "AddLineJson", "DeleteLineJson", "ClearLinesJson", "PostAdjustment", "OpenAdjustment" }, "إدارة المخازن"),
            ("StockTransfers", new[] { "Index", "Show", "Details", "Create", "Edit", "Delete", "Export", "DeleteConfirmed", "BulkDelete", "DeleteAll", "CreateHeaderJson", "UpdateHeaderJson", "GetTransferProductInfo", "AddLineJson", "DeleteLineJson", "ClearLinesJson", "PostTransfer", "OpenTransfer" }, "إدارة المخازن"),
            ("StockAdjustmentLines", new[] { "Index", "Details", "Create", "Edit", "Delete", "Export", "DeleteConfirmed", "BulkDelete", "DeleteAll" }, "إدارة المخازن"),
            ("StockTransferLines", new[] { "Index", "Details", "Create", "Edit", "Delete", "Export", "DeleteConfirmed", "BulkDelete", "DeleteAll" }, "إدارة المخازن"),
            ("StockLedger", new[] { "Index", "Export" }, "إدارة المخازن"),
            ("StockFifoMap", new[] { "Index", "Export" }, "إدارة المخازن"),
            ("StockBatches", new[] { "Index", "BulkDelete", "DeleteAll", "Export" }, "إدارة المخازن"),
            ("Batches", new[] { "Index", "Create", "Edit", "Show", "Delete", "Export", "DeleteConfirmed", "BulkDelete", "DeleteAll" }, "إدارة المخازن"),
            ("ProductPriceHistory", new[] { "Index", "Export", "BulkDelete", "DeleteAll" }, "إدارة المخازن"),
            ("PurchaseInvoices", new[] { "Index", "Show", "Create", "Edit", "Delete", "Export", "SaveHeader", "AddLineJson", "RemoveLineJson", "ClearAllLinesJson", "SaveTaxJson", "PostInvoice", "CreateFullReturn", "OpenInvoice", "GetAlternativeProducts", "DeleteConfirmed", "DeleteOneFromList", "DeleteOneFromShow", "BulkDelete", "DeleteAll" }, "المشتريات"),
            ("PurchaseRequests", new[] { "Index", "Show", "Create", "Edit", "Delete", "Export", "SaveHeader", "AddLineJson", "RemoveLineJson", "ClearAllLinesJson", "SaveTaxJson", "GetAlternativeProducts", "GetProductDemandInfo", "GetProductSalesInPeriod", "DeleteConfirmed", "BulkDelete", "DeleteAll", "RecalcTotals", "ConvertToPurchaseInvoice", "RecalcAllTotals" }, "المشتريات"),
            ("PurchaseReturns", new[] { "Index", "Create", "Edit", "Delete", "Export", "SaveHeader", "BulkDelete", "DeleteAll", "GetInvoiceItems", "AddLineJson", "RemoveLineJson", "ClearLinesJson", "OpenReturn", "PostReturn" }, "المشتريات"),
            ("PILines", new[] { "Index", "BulkDelete", "DeleteAll", "Export" }, "المشتريات"),
            ("PRLines", new[] { "Index", "Show", "Create", "Edit", "Delete", "Export", "AddLineJson", "BulkDelete" }, "المشتريات"),
            ("PurchaseReturnLines", new[] { "Index", "Show", "Delete", "Export", "BulkDelete", "DeleteAll" }, "المشتريات"),
            ("Accounts", new[] { "Index", "Create", "Edit", "Delete", "Export", "BulkDelete", "DeleteAll" }, "الحسابات"),
            ("LedgerEntries", new[] { "Index", "Show", "Export", "BulkDelete", "DeleteAll" }, "الحسابات"),
            ("CashReceipts", new[] { "Index", "Show", "Details", "Create", "Edit", "Delete", "Export", "Open", "DeleteConfirmed", "BulkDelete", "DeleteAll", "GetCustomerAccount" }, "الحسابات"),
            ("CashPayments", new[] { "Index", "Show", "Details", "Create", "Edit", "Delete", "Export", "Open", "DeleteConfirmed", "BulkDelete", "DeleteAll", "GetCustomerAccount" }, "الحسابات"),
            ("DebitNotes", new[] { "Index", "Show", "Details", "Create", "Edit", "Delete", "Export", "Unlock", "DeleteConfirmed", "BulkDelete", "DeleteAll", "GetCustomerAccount" }, "الحسابات"),
            ("CreditNotes", new[] { "Index", "Show", "Details", "Create", "Edit", "Delete", "Export", "Unlock", "DeleteConfirmed", "BulkDelete", "DeleteAll", "GetCustomerAccount" }, "الحسابات"),
            ("Treasury", new[] { "Index", "ZeroTreasuryBalance" }, "الحسابات"),
            ("Reports", new[] { "ProductBalances", "ProductDetailsReport", "BonusReport", "CustomerBalances", "ExportCustomerBalances", "ExportProductBalances", "ProductProfits", "CustomerProfits", "SalesPerformanceReport", "PurchasePerformanceReport", "RouteReport", "FixOrphanedPaymentEntries", "ZeroCustomerBalance", "SyncStockBatchesFromLedger", "ZeroPhantomSalesReturnRemaining", "CleanOrphanedStockLedger", "ZeroProductBalance", "SaveManualDiscount" }, "التقارير"),
            ("ProductClassifications", new[] { "Index", "Create", "Edit", "Delete", "DeleteConfirmed", "BulkDelete", "DeleteAll", "GetColumnValues" }, "خط السير"),
            ("Routes", new[] { "Index", "Create", "Edit", "Delete", "DeleteConfirmed", "BulkDelete", "DeleteAll", "GetColumnValues" }, "خط السير"),
            ("SalesInvoiceRoutes", new[] { "Index", "Edit", "Entry", "GetInvoiceInfo", "GetEmployeesByJob", "GetFridgeProducts", "SaveRouteEntry", "SaveRouteJson" }, "خط السير"),
            ("Governorates", new[] { "Index", "Details", "Create", "Edit", "Delete", "Export", "DeleteConfirmed", "BulkDelete", "DeleteAll" }, "المناطق الجغرافية"),
            ("Dstricts", new[] { "Index", "Details", "Create", "Edit", "Delete", "Export", "DeleteConfirmed", "BulkDelete", "DeleteAll" }, "المناطق الجغرافية"),
            ("Areas", new[] { "Index", "Details", "Create", "Edit", "Delete", "Export", "DeleteConfirmed", "BulkDelete", "DeleteAll" }, "المناطق الجغرافية"),
            ("Branches", new[] { "Index", "Details", "Create", "Edit", "Delete", "Export", "DeleteConfirmed", "BulkDelete", "DeleteAll" }, "المناطق الجغرافية"),
            ("Cities", new[] { "Index", "Details", "Create", "Edit", "Delete", "DeleteConfirmed" }, "المناطق الجغرافية"),
            ("DocumentSeries", new[] { "Index", "Details", "Create", "Edit", "Delete", "DeleteConfirmed" }, "الإعدادات"),
            ("Policies", new[] { "Index", "Details", "Create", "Edit", "Delete", "Export", "DeleteConfirmed", "BulkDelete", "DeleteAll" }, "الإعدادات"),
            ("ProductGroups", new[] { "Index", "Details", "Create", "Edit", "Delete", "Export", "DeleteConfirmed", "BulkDelete", "DeleteAll" }, "الإعدادات"),
            ("ProductGroupPolicies", new[] { "Index", "Details", "Create", "Edit", "Delete", "Export", "DeleteConfirmed", "BulkDelete", "DeleteAll" }, "الإعدادات"),
            ("ProductBonusGroups", new[] { "Index", "Details", "Create", "Edit", "Delete", "Export", "DeleteConfirmed", "BulkDelete", "DeleteAll" }, "الإعدادات"),
            ("WarehousePolicyRules", new[] { "Index", "Details", "Create", "Edit", "Delete", "Export", "DeleteConfirmed", "BulkDelete", "DeleteAll", "GetPoliciesNotUsedForWarehouse" }, "الإعدادات"),
            ("UserActivityLogs", new[] { "Index", "Details", "Delete", "DeleteConfirmed", "BulkDelete", "DeleteAll", "Export" }, "الإعدادات"),
            ("Users", new[] { "Index", "Details", "Create", "Edit", "Delete", "Export", "DeleteConfirmed", "BulkDelete", "DeleteAll" }, "المستخدمون والصلاحيات"),
            ("Roles", new[] { "Index", "Details", "Create", "Edit", "Delete", "Export", "DeleteConfirmed", "BulkDelete", "DeleteAll" }, "المستخدمون والصلاحيات"),
            ("Permissions", new[] { "Index", "Details", "Create", "Edit", "Delete", "Export", "DeleteConfirmed", "BulkDelete", "DeleteAll" }, "المستخدمون والصلاحيات"),
            ("RolePermissions", new[] { "Index", "Details", "Create", "Edit", "Delete", "Export", "DeleteConfirmed", "BulkDelete", "DeleteAll", "GetColumnValues" }, "المستخدمون والصلاحيات"),
            ("UserRoles", new[] { "Index", "Details", "Create", "Edit", "Delete", "Export", "DeleteConfirmed", "BulkDelete", "DeleteAll", "GetRolePermissionsPreview", "GetRolePermissionsEditable" }, "المستخدمون والصلاحيات"),
            ("UserExtraPermissions", new[] { "Index", "Details", "Create", "Edit", "Delete", "Export", "DeleteConfirmed", "BulkDelete", "DeleteAll" }, "المستخدمون والصلاحيات"),
            ("UserDeniedPermissions", new[] { "Index", "Details", "Create", "Edit", "Delete", "Export", "DeleteConfirmed", "BulkDelete", "DeleteAll" }, "المستخدمون والصلاحيات"),
            ("UserAccountVisibility", new[] { "Index", "Edit", "Update", "SeeAll" }, "المستخدمون والصلاحيات"),
            ("Departments", new[] { "Index", "Create", "Edit", "Delete", "DeleteConfirmed", "BulkDelete", "DeleteAll" }, "الموظفون"),
            ("Jobs", new[] { "Index", "Create", "Edit", "Delete", "DeleteConfirmed", "BulkDelete", "DeleteAll" }, "الموظفون"),
            ("Employees", new[] { "Index", "Create", "Edit", "Delete", "DeleteConfirmed", "Show", "Export", "BulkDelete", "DeleteAll" }, "الموظفون"),
        };

        /// <summary>
        /// أسماء الصلاحيات بالعربي المعبرة (للعرض في واجهة الصلاحيات والأدوار).
        /// إذا لم يُذكر الكود يُستخدم الافتراضي: الموديول + الكود.
        /// </summary>
        private static readonly Dictionary<string, string> NameArByCode = new(StringComparer.OrdinalIgnoreCase)
        {
            // لوحات التحكم — لوحة واحدة فقط: مبيعاتي الشخصية (تفتح مع الدخول للبرنامج)
            ["Dashboard.Sales"] = "مبيعاتي الشخصية",
            ["Home.Index"] = "الصفحة الرئيسية",
            // فواتير المبيعات
            ["SalesInvoices.Index"] = "قائمة فواتير المبيعات",
            ["SalesInvoices.Show"] = "عرض فاتورة مبيعات",
            ["SalesInvoices.Create"] = "إنشاء فاتورة مبيعات",
            ["SalesInvoices.Edit"] = "تعديل فاتورة مبيعات",
            ["SalesInvoices.Delete"] = "حذف فاتورة مبيعات (صفحة التأكيد)",
            ["SalesInvoices.DeleteConfirmed"] = "تنفيذ حذف فاتورة مبيعات",
            ["SalesInvoices.DeleteOneFromList"] = "مسح فاتورة واحدة من قائمة البيع",
            ["SalesInvoices.DeleteOneFromShow"] = "مسح فاتورة من داخل شاشة الفاتورة",
            ["SalesInvoices.BulkDelete"] = "مسح مجموعة فواتير من قائمة البيع",
            ["SalesInvoices.DeleteAll"] = "مسح كل فواتير قائمة البيع",
            ["SalesInvoices.Export"] = "تصدير فواتير المبيعات",
            ["SalesInvoices.PostInvoice"] = "ترحيل فاتورة المبيعات",
            ["SalesInvoices.OpenInvoice"] = "فتح فاتورة المبيعات",
            ["SalesInvoices.CreateFullReturn"] = "إنشاء مرتجع كامل",
            // قائمة الخصم اليدوي للبيع
            ["ProductDiscountOverrides.Index"] = "قائمة الخصم اليدوي للبيع",
            ["ProductDiscountOverrides.Edit"] = "تعديل سجل الخصم اليدوي",
            ["ProductDiscountOverrides.Delete"] = "حذف سجل الخصم اليدوي",
            ["ProductDiscountOverrides.Update"] = "حفظ التعديل من داخل الجدول",
            // فواتير المشتريات
            ["PurchaseInvoices.Index"] = "قائمة فواتير المشتريات",
            ["PurchaseInvoices.Show"] = "عرض فاتورة مشتريات",
            ["PurchaseInvoices.Create"] = "إنشاء فاتورة مشتريات",
            ["PurchaseInvoices.Edit"] = "تعديل فاتورة مشتريات",
            ["PurchaseInvoices.Delete"] = "حذف فاتورة مشتريات (صفحة التأكيد)",
            ["PurchaseInvoices.DeleteConfirmed"] = "تنفيذ حذف فاتورة مشتريات",
            ["PurchaseInvoices.DeleteOneFromList"] = "مسح فاتورة واحدة من قائمة المشتريات",
            ["PurchaseInvoices.DeleteOneFromShow"] = "مسح فاتورة من داخل شاشة الفاتورة",
            ["PurchaseInvoices.BulkDelete"] = "مسح مجموعة فواتير من قائمة المشتريات",
            ["PurchaseInvoices.DeleteAll"] = "مسح كل فواتير قائمة المشتريات",
            ["PurchaseInvoices.Export"] = "تصدير فواتير المشتريات",
            ["PurchaseInvoices.PostInvoice"] = "ترحيل فاتورة المشتريات",
            ["PurchaseInvoices.OpenInvoice"] = "فتح فاتورة المشتريات",
            ["PurchaseInvoices.CreateFullReturn"] = "إنشاء مرتجع كامل للمشتريات",
            // العملاء
            ["Customers.Index"] = "قائمة العملاء",
            ["Customers.Show"] = "عرض عميل",
            ["Customers.Create"] = "إنشاء عميل",
            ["Customers.Edit"] = "تعديل عميل",
            ["Customers.Delete"] = "حذف عميل",
            ["Customers.DeleteOneFromList"] = "مسح عميل من القائمة",
            ["Customers.BulkDelete"] = "مسح مجموعة عملاء من القائمة",
            ["Customers.DeleteAll"] = "مسح كل العملاء من القائمة",
            ["Customers.Export"] = "تصدير العملاء",
            // المخازن والمنتجات
            ["Products.Index"] = "قائمة المنتجات",
            ["Products.Create"] = "إنشاء منتج",
            ["Products.Edit"] = "تعديل منتج",
            ["Products.Delete"] = "حذف منتج",
            ["Products.BulkDelete"] = "مسح مجموعة منتجات من القائمة",
            ["Products.DeleteAll"] = "مسح كل المنتجات من القائمة",
            ["Products.Export"] = "تصدير المنتجات",
            ["Categories.Index"] = "قائمة التصنيفات",
            ["Categories.BulkDelete"] = "مسح مجموعة تصنيفات من القائمة",
            ["Categories.DeleteAll"] = "مسح كل التصنيفات من القائمة",
            ["Warehouses.Index"] = "قائمة المخازن",
            ["Warehouses.BulkDelete"] = "مسح مجموعة مخازن من القائمة",
            ["Warehouses.DeleteAll"] = "مسح كل المخازن من القائمة",
            // المستخدمون والصلاحيات
            ["Users.Index"] = "قائمة المستخدمين",
            ["Users.Create"] = "إنشاء مستخدم",
            ["Users.Edit"] = "تعديل مستخدم",
            ["Users.Delete"] = "حذف مستخدم",
            ["Users.BulkDelete"] = "مسح مجموعة مستخدمين من القائمة",
            ["Users.DeleteAll"] = "مسح كل المستخدمين من القائمة",
            ["Roles.Index"] = "قائمة الأدوار",
            ["Roles.BulkDelete"] = "مسح مجموعة أدوار من القائمة",
            ["Roles.DeleteAll"] = "مسح كل الأدوار من القائمة",
            ["Permissions.Index"] = "قائمة الصلاحيات",
            ["RolePermissions.Index"] = "ربط الصلاحيات بالأدوار",
            ["UserRoles.Index"] = "ربط المستخدمين بالأدوار",
            // سطور أوامر البيع (SOLines)
            ["SOLines.Index"] = "قائمة سطور أوامر البيع",
            ["SOLines.Details"] = "تفاصيل سطر أمر بيع",
            ["SOLines.Delete"] = "حذف سطر أمر بيع",
            ["SOLines.BulkDelete"] = "مسح مجموعة سطور أوامر البيع",
            ["SOLines.DeleteAll"] = "مسح كل سطور أوامر البيع",
            ["SOLines.Export"] = "تصدير سطور أوامر البيع",
            // سطور فواتير المبيعات
            ["SalesInvoiceLines.Index"] = "قائمة سطور فواتير المبيعات",
            ["SalesInvoiceLines.Details"] = "تفاصيل سطر فاتورة مبيعات",
            ["SalesInvoiceLines.Delete"] = "حذف سطر فاتورة مبيعات",
            ["SalesInvoiceLines.BulkDelete"] = "مسح مجموعة سطور فواتير المبيعات",
            ["SalesInvoiceLines.DeleteAll"] = "مسح كل سطور فواتير المبيعات",
            ["SalesInvoiceLines.Export"] = "تصدير سطور فواتير المبيعات",
            // مرتجعات المبيعات
            ["SalesReturns.Index"] = "قائمة مرتجعات المبيعات",
            ["SalesReturns.Show"] = "عرض مرتجع مبيعات",
            ["SalesReturns.Create"] = "إنشاء مرتجع مبيعات",
            ["SalesReturns.Edit"] = "تعديل مرتجع مبيعات",
            ["SalesReturns.Delete"] = "حذف مرتجع مبيعات",
            ["SalesReturns.Export"] = "تصدير مرتجعات المبيعات",
            ["SalesReturns.BulkDelete"] = "مسح مجموعة مرتجعات من القائمة",
            ["SalesReturns.DeleteAll"] = "مسح كل مرتجعات المبيعات",
            ["SalesReturns.PostReturn"] = "ترحيل مرتجع المبيعات",
            ["SalesReturns.OpenReturn"] = "فتح مرتجع المبيعات",
            ["SalesReturns.SearchProductsForReturn"] = "بحث أصناف لمرتجع بدون فاتورة",
            ["SalesReturns.GetProductsForReturnDatalist"] = "قائمة أصناف المرتجع بدون فاتورة (داتاليست)",
            // أوامر البيع
            ["SalesOrders.Index"] = "قائمة أوامر البيع",
            ["SalesOrders.Show"] = "عرض أمر بيع",
            ["SalesOrders.Create"] = "إنشاء أمر بيع",
            ["SalesOrders.Edit"] = "تعديل أمر بيع",
            ["SalesOrders.Delete"] = "حذف أمر بيع",
            ["SalesOrders.Export"] = "تصدير أوامر البيع",
            ["SalesOrders.BulkDelete"] = "مسح مجموعة أوامر بيع من القائمة",
            ["SalesOrders.DeleteAll"] = "مسح كل أوامر البيع",
            // سطور مرتجعات المبيعات
            ["SalesReturnLines.Index"] = "قائمة سطور مرتجعات المبيعات",
            ["SalesReturnLines.Delete"] = "حذف سطر مرتجع مبيعات",
            ["SalesReturnLines.BulkDelete"] = "مسح مجموعة سطور مرتجعات",
            ["SalesReturnLines.DeleteAll"] = "مسح كل سطور مرتجعات المبيعات",
            ["SalesReturnLines.Export"] = "تصدير سطور مرتجعات المبيعات",
            // خط السير — تصنيفات الأصناف
            ["ProductClassifications.Index"] = "قائمة تصنيفات الأصناف",
            ["ProductClassifications.Create"] = "إضافة تصنيف صنف",
            ["ProductClassifications.Edit"] = "تعديل تصنيف صنف",
            ["ProductClassifications.Delete"] = "حذف تصنيف صنف",
            ["ProductClassifications.DeleteConfirmed"] = "تنفيذ حذف تصنيف",
            ["ProductClassifications.BulkDelete"] = "مسح مجموعة تصنيفات",
            ["ProductClassifications.DeleteAll"] = "مسح كل التصنيفات",
            ["ProductClassifications.GetColumnValues"] = "قيم عمود (فلتر)",
            // خط السير — خطوط السير
            ["Routes.Index"] = "قائمة خطوط السير",
            ["Routes.Create"] = "إضافة خط سير",
            ["Routes.Edit"] = "تعديل خط سير",
            ["Routes.Delete"] = "حذف خط سير",
            ["Routes.DeleteConfirmed"] = "تنفيذ حذف خط سير",
            ["Routes.BulkDelete"] = "مسح مجموعة خطوط",
            ["Routes.DeleteAll"] = "مسح كل خطوط السير",
            ["Routes.GetColumnValues"] = "قيم عمود (فلتر)",
            // خط السير — بيانات خط السير للفواتير
            ["SalesInvoiceRoutes.Index"] = "قائمة بيانات خط السير للفواتير",
            ["SalesInvoiceRoutes.Edit"] = "تعديل بيانات خط السير لفاتورة",
            ["SalesInvoiceRoutes.Entry"] = "إدخال بيانات خط السير (شاشة التسلسل)",
            ["SalesInvoiceRoutes.GetInvoiceInfo"] = "جلب بيانات الفاتورة (API)",
            ["SalesInvoiceRoutes.GetEmployeesByJob"] = "جلب الموظفين حسب الوظيفة (API)",
            ["SalesInvoiceRoutes.GetFridgeProducts"] = "جلب أصناف الثلاجة (API)",
            ["SalesInvoiceRoutes.SaveRouteEntry"] = "تسجيل بيانات خط السير من شاشة الإدخال",
            ["SalesInvoiceRoutes.SaveRouteJson"] = "حفظ بيانات خط السير (API)",
            // الموظفون — أقسام
            ["Departments.Index"] = "قائمة الأقسام",
            ["Departments.Create"] = "إضافة قسم",
            ["Departments.Edit"] = "تعديل قسم",
            ["Departments.Delete"] = "حذف قسم",
            ["Departments.DeleteConfirmed"] = "تنفيذ حذف قسم",
            ["Departments.BulkDelete"] = "مسح مجموعة أقسام",
            ["Departments.DeleteAll"] = "مسح كل الأقسام",
            // الموظفون — وظائف
            ["Jobs.Index"] = "قائمة الوظائف",
            ["Jobs.Create"] = "إضافة وظيفة",
            ["Jobs.Edit"] = "تعديل وظيفة",
            ["Jobs.Delete"] = "حذف وظيفة",
            ["Jobs.DeleteConfirmed"] = "تنفيذ حذف وظيفة",
            ["Jobs.BulkDelete"] = "مسح مجموعة وظائف",
            ["Jobs.DeleteAll"] = "مسح كل الوظائف",
            // الموظفون — موظفين
            ["Employees.Index"] = "قائمة الموظفين",
            ["Employees.Create"] = "إضافة موظف",
            ["Employees.Edit"] = "تعديل موظف",
            ["Employees.Delete"] = "حذف موظف",
            ["Employees.DeleteConfirmed"] = "تنفيذ حذف موظف",
            ["Employees.Show"] = "عرض تفاصيل موظف",
            ["Employees.Export"] = "تصدير الموظفين",
            ["Employees.BulkDelete"] = "مسح مجموعة موظفين",
            ["Employees.DeleteAll"] = "مسح كل الموظفين",

            // الحسابات المسموح رؤيتها لكل مستخدم
            ["UserAccountVisibility.Index"] = "الحسابات المسموح رؤيتها (موجّه — استخدم صلاحيات الأدوار)",
            ["UserAccountVisibility.Edit"] = "تعديل ظهور الحسابات (موجّه — استخدم صلاحيات الأدوار)",
            ["UserAccountVisibility.Update"] = "حفظ ظهور الحسابات (موجّه — استخدم صلاحيات الأدوار)",
            ["UserAccountVisibility.SeeAll"] = "رؤية جميع الحسابات (بما فيها المستثمر)",
        };

        /// <summary>
        /// يُنشئ كود صلاحية من اسم الكونترولر والأكشن (للاستخدام في HasPermissionAsync فقط، وليس في السمات).
        /// في [RequirePermission] استخدم النص الحرفي: [RequirePermission("SalesInvoices.Index")]
        /// </summary>
        public static string Code(string controllerName, string actionName)
        {
            if (string.IsNullOrWhiteSpace(controllerName) || string.IsNullOrWhiteSpace(actionName))
                return "";
            return $"{controllerName.Trim()}.{actionName.Trim()}";
        }

        /// <summary>
        /// ترجع كل الصلاحيات للمزامنة مع قاعدة البيانات: مسح القديم وإعادة البناء من هذا المصدر فقط.
        /// Code = ControllerName.ActionName بالضبط كما في الكونترولرات.
        /// </summary>
        public static IEnumerable<(string Code, string NameAr, string Module)> GetAllForSync()
        {
            foreach (var (controller, actions, module) in Definitions)
            {
                foreach (var action in actions.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var code = $"{controller}.{action}";
                    var nameAr = NameArByCode.TryGetValue(code, out var name) ? name : $"{module} - {code}";
                    yield return (code, nameAr, module);
                }
            }
        }
    }
}
