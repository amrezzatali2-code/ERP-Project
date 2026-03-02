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
        /// </summary>
        private static readonly (string Controller, string[] Actions, string Module)[] Definitions =
        {
            ("Dashboard", new[] { "Index", "Sales", "Manager", "Owner" }, "لوحات التحكم"),
            ("Home", new[] { "Index", "Privacy" }, "لوحات التحكم"),
            ("SalesInvoices", new[] { "Index", "Show", "Create", "Edit", "Delete", "Export", "SaveHeader", "GetProductsForDatalist", "GetAlternativeProducts", "GetSalesProductInfo", "DiagnosePolicy", "AddLineJson", "RemoveLineJson", "ClearAllLinesJson", "SaveTaxJson", "PostInvoice", "CreateFullReturn", "OpenInvoice", "DeleteConfirmed", "BulkDelete", "DeleteAll" }, "المبيعات"),
            ("SalesReturns", new[] { "Index", "Show", "Create", "Edit", "Delete", "Export", "SaveHeader", "DeleteConfirmed", "BulkDelete", "DeleteAll", "GetInvoiceItems", "AddLineJson", "RemoveLineJson", "ClearLinesJson", "OpenReturn", "PostReturn" }, "المبيعات"),
            ("SalesOrders", new[] { "Index", "Show", "Create", "Edit", "Delete", "Export", "DeleteConfirmed", "BulkDelete", "DeleteAll" }, "المبيعات"),
            ("SalesInvoiceLines", new[] { "Index", "Details", "Delete", "BulkDelete", "DeleteAll", "Export" }, "المبيعات"),
            ("SalesReturnLines", new[] { "Index", "Delete", "BulkDelete", "DeleteAll", "Export" }, "المبيعات"),
            ("SOLines", new[] { "Index", "Details", "Delete", "BulkDelete", "DeleteAll", "Export" }, "المبيعات"),
            ("ProductDiscountOverrides", new[] { "Index" }, "المبيعات"),
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
            ("PurchaseInvoices", new[] { "Index", "Show", "Create", "Edit", "Delete", "Export", "SaveHeader", "AddLineJson", "RemoveLineJson", "ClearAllLinesJson", "SaveTaxJson", "PostInvoice", "CreateFullReturn", "OpenInvoice", "GetAlternativeProducts", "DeleteConfirmed", "BulkDelete", "DeleteAll" }, "المشتريات"),
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
            ("Reports", new[] { "ProductBalances", "BonusReport", "CustomerBalances", "ExportCustomerBalances", "ExportProductBalances", "ProductProfits", "CustomerProfits", "FixOrphanedPaymentEntries", "ZeroCustomerBalance", "SyncStockBatchesFromLedger", "CleanOrphanedStockLedger", "ZeroProductBalance", "SaveManualDiscount" }, "التقارير"),
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
                    yield return (code, $"{module} - {code}", module);
                }
            }
        }
    }
}
