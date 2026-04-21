using System;
using System.Collections.Generic;
using System.Linq;

namespace ERP.Data.Seed
{
    /// <summary>
    /// حزم صلاحيات افتراضية مقترحة لكل دور (تُضاف كروابط RolePermissions إن لم تكن موجودة).
    /// الصلاحيات العامة Global.* تُضاف لكل الأدوار عبر <see cref="GlobalPermissionSeeder"/> أولاً؛
    /// أي إجراء في النظام يمر عبر البوابة العامة ثم الصلاحية التفصيلية.
    /// </summary>
    public static class RoleDefaultPermissionCatalog
    {
        private static string[] M(params string[] codes) => codes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        private static readonly string[] Nav = { "Home.Index", "Dashboard.Sales" };
        private static readonly string[] GeneralList = M("Global.GeneralList");

        private static readonly string[] CustomersStd = M(
            "Customers.Index", "Customers.Show", "Customers.Create", "Customers.Edit", "Customers.Export",
            "Customers.GetDefaultAccountForParty", "Customers.GetDistrictsByGovernorate", "Customers.GetAreasByDistrict");

        private static readonly string[] ProductsRead = M(
            "Products.Index", "Products.Show", "Products.Details", "Products.Export", "Products.GetColumnValues",
            "Products.SearchProducts", "Products.SearchProductsByCode", "Products.SearchParties");

        private static readonly string[] WarehousesStd = M(
            "Warehouses.Index", "Warehouses.Show", "Warehouses.GetColumnValues");

        private static readonly string[] SalesInvoiceRep = M(
            "SalesInvoices.Index", "SalesInvoices.Show", "SalesInvoices.Create", "SalesInvoices.Edit",
            "SalesInvoices.Export", "SalesInvoices.SaveHeader", "SalesInvoices.GetProductsForDatalist", "SalesInvoices.GetAlternativeProducts",
            "SalesInvoices.GetSalesProductInfo", "SalesInvoices.AddLineJson", "SalesInvoices.RemoveLineJson", "SalesInvoices.ClearAllLinesJson",
            "SalesInvoices.SaveTaxJson", "SalesInvoices.PostInvoice", "SalesInvoices.OpenInvoice", "SalesInvoices.CreateFullReturn");

        private static readonly string[] SalesReturnLinesOnly = M("SalesReturnLines.Index", "SalesReturnLines.Export");

        private static readonly string[] CashVouchers = M(
            "CashReceipts.Index", "CashReceipts.Show", "CashReceipts.Details", "CashReceipts.Create", "CashReceipts.Edit",
            "CashReceipts.Export", "CashReceipts.Open", "CashReceipts.GetCustomerAccount",
            "CashPayments.Index", "CashPayments.Show", "CashPayments.Details", "CashPayments.Create", "CashPayments.Edit",
            "CashPayments.Export", "CashPayments.Open", "CashPayments.GetCustomerAccount");

        private static readonly string[] ReportsRead = M(
            "Reports.ProductBalances", "Reports.ProductDetailsReport", "Reports.BonusReport", "Reports.CustomerBalances",
            "Reports.ExportCustomerBalances", "Reports.ExportProductBalances", "Reports.ProductProfits", "Reports.CustomerProfits",
            "Reports.SalesPerformanceReport", "Reports.PurchasePerformanceReport", "Reports.RouteReport");

        private static readonly string[] AccountsCore = M(
            "Accounts.Index", "Accounts.Create", "Accounts.Edit", "Accounts.Export",
            "LedgerEntries.Index", "LedgerEntries.Show", "LedgerEntries.Export",
            "Treasury.Index");

        private static readonly string[] LedgerEntriesFull = M(
            "LedgerEntries.Index", "LedgerEntries.Show", "LedgerEntries.Export", "LedgerEntries.BulkDelete", "LedgerEntries.DeleteAll");

        private static readonly string[] PurchasePack = M(
            "PurchaseInvoices.Index", "PurchaseInvoices.Show", "PurchaseInvoices.Create", "PurchaseInvoices.Edit",
            "PurchaseInvoices.Export", "PurchaseInvoices.SaveHeader", "PurchaseInvoices.AddLineJson", "PurchaseInvoices.RemoveLineJson",
            "PurchaseInvoices.ClearAllLinesJson", "PurchaseInvoices.SaveTaxJson", "PurchaseInvoices.PostInvoice", "PurchaseInvoices.OpenInvoice",
            "PurchaseInvoices.GetAlternativeProducts",
            "PurchaseRequests.Index", "PurchaseRequests.Show", "PurchaseRequests.Create", "PurchaseRequests.Edit",
            "PurchaseRequests.Export", "PurchaseRequests.SaveHeader", "PurchaseRequests.AddLineJson", "PurchaseRequests.RemoveLineJson",
            "PurchaseRequests.ClearAllLinesJson", "PurchaseRequests.SaveTaxJson", "PurchaseRequests.GetAlternativeProducts",
            "PurchaseRequests.GetProductDemandInfo", "PurchaseRequests.GetProductSalesInPeriod", "PurchaseRequests.ConvertToPurchaseInvoice",
            "PurchaseReturns.Index", "PurchaseReturns.Create", "PurchaseReturns.Edit", "PurchaseReturns.Export", "PurchaseReturns.SaveHeader",
            "PurchaseReturns.AddLineJson", "PurchaseReturns.RemoveLineJson", "PurchaseReturns.ClearLinesJson", "PurchaseReturns.OpenReturn", "PurchaseReturns.PostReturn",
            "PILines.Index", "PILines.Export",
            "PRLines.Index", "PRLines.Show", "PRLines.Create", "PRLines.Edit", "PRLines.Export", "PRLines.AddLineJson",
            "PurchaseReturnLines.Index", "PurchaseReturnLines.Show", "PurchaseReturnLines.Delete", "PurchaseReturnLines.Export");

        private static readonly string[] InventoryPack = M(
            "Products.Index", "Products.Details", "Products.Create", "Products.Edit", "Products.Show", "Products.Export", "Products.GetColumnValues",
            "Products.SearchProducts", "Products.SearchProductsByCode", "Products.SearchParties", "Products.Import",
            "Settings.ProductsExcelImport",
            "Categories.Index", "Categories.Show", "Categories.Details", "Categories.Create", "Categories.Edit", "Categories.Export",
            "Warehouses.Index", "Warehouses.Show", "Warehouses.Create", "Warehouses.Edit", "Warehouses.Export", "Warehouses.GetColumnValues",
            "StockAdjustments.Index", "StockAdjustments.Show", "StockAdjustments.Details", "StockAdjustments.Create", "StockAdjustments.Edit",
            "StockAdjustments.Export", "StockAdjustments.CreateHeaderJson", "StockAdjustments.UpdateHeaderJson", "StockAdjustments.GetStockAdjustmentProductInfo",
            "StockAdjustments.AddLineJson", "StockAdjustments.DeleteLineJson", "StockAdjustments.ClearLinesJson", "StockAdjustments.PostAdjustment", "StockAdjustments.OpenAdjustment",
            "StockTransfers.Index", "StockTransfers.Show", "StockTransfers.Details", "StockTransfers.Create", "StockTransfers.Edit",
            "StockTransfers.Export", "StockTransfers.CreateHeaderJson", "StockTransfers.UpdateHeaderJson", "StockTransfers.GetTransferProductInfo",
            "StockTransfers.AddLineJson", "StockTransfers.DeleteLineJson", "StockTransfers.ClearLinesJson", "StockTransfers.PostTransfer", "StockTransfers.OpenTransfer",
            "StockLedger.Index", "StockLedger.Export",
            "StockBatches.Index", "StockBatches.Export",
            "Batches.Index", "Batches.Show", "Batches.Create", "Batches.Edit", "Batches.Export",
            "StockFifoMap.Index", "StockFifoMap.Export",
            "StockAdjustmentLines.Index", "StockAdjustmentLines.Export",
            "StockTransferLines.Index", "StockTransferLines.Export",
            "ProductPriceHistory.Index", "ProductPriceHistory.Export");

        private static readonly string[] SalesManagerExtra = M(
            "SalesInvoices.Index", "SalesInvoices.Show", "SalesInvoices.Create", "SalesInvoices.Edit",
            "SalesInvoices.Export", "SalesInvoices.DeleteOneFromList", "SalesInvoices.DeleteOneFromShow",
            "SalesInvoices.SaveHeader", "SalesInvoices.GetProductsForDatalist", "SalesInvoices.GetAlternativeProducts", "SalesInvoices.GetSalesProductInfo",
            "SalesInvoices.AddLineJson", "SalesInvoices.RemoveLineJson", "SalesInvoices.ClearAllLinesJson", "SalesInvoices.SaveTaxJson",
            "SalesInvoices.PostInvoice", "SalesInvoices.OpenInvoice", "SalesInvoices.CreateFullReturn",
            "SalesReturns.Index", "SalesReturns.Show", "SalesReturns.Create", "SalesReturns.Edit", "SalesReturns.Export",
            "SalesReturns.SaveHeader", "SalesReturns.GetInvoiceItems", "SalesReturns.SearchProductsForReturn", "SalesReturns.GetProductsForReturnDatalist",
            "SalesReturns.AddLineJson", "SalesReturns.RemoveLineJson", "SalesReturns.ClearLinesJson", "SalesReturns.OpenReturn", "SalesReturns.PostReturn",
            "SalesOrders.Index", "SalesOrders.Show", "SalesOrders.Create", "SalesOrders.Edit", "SalesOrders.Export",
            "SalesInvoiceLines.Index", "SalesInvoiceLines.Details", "SalesInvoiceLines.Export",
            "SalesReturnLines.Index", "SalesReturnLines.Export",
            "SOLines.Index", "SOLines.Details", "SOLines.Export",
            "ProductDiscountOverrides.Index", "ProductDiscountOverrides.Edit", "ProductDiscountOverrides.Update",
            "CustomerLedger.Index",
            "SalesInvoiceRoutes.Index", "SalesInvoiceRoutes.Edit", "SalesInvoiceRoutes.Entry", "SalesInvoiceRoutes.GetInvoiceInfo",
            "SalesInvoiceRoutes.GetEmployeesByJob", "SalesInvoiceRoutes.GetFridgeProducts", "SalesInvoiceRoutes.SaveRouteEntry", "SalesInvoiceRoutes.SaveRouteJson");

        private static readonly string[] GmAdmin = M(
            "Users.Index", "Users.Details", "Users.Create", "Users.Edit", "Users.Export",
            "Roles.Index", "Roles.Details", "Roles.Create", "Roles.Edit", "Roles.Export",
            "Permissions.Index", "Permissions.Export",
            "RolePermissions.Index", "RolePermissions.Details", "RolePermissions.Edit", "RolePermissions.GetColumnValues",
            "UserRoles.Index", "UserRoles.Details", "UserRoles.Create", "UserRoles.Edit", "UserRoles.GetRolePermissionsPreview",
            "UserRoles.GetRolePermissionsEditable", "UserRoles.ResetToRoleDefaults",
            "UserExtraPermissions.Index", "UserDeniedPermissions.Index",
            "UserAccountVisibility.Index", "UserAccountVisibility.Edit", "UserAccountVisibility.Update", "UserAccountVisibility.SeeAll",
            "UserActivityLogs.Index", "UserActivityLogs.Details", "UserActivityLogs.Export");

        private static readonly string[] SettingsRead = M(
            "Settings.PrintHeader",
            "DocumentSeries.Index", "DocumentSeries.Details",
            "Policies.Index", "Policies.Details", "Policies.Export",
            "ProductGroups.Index", "ProductGroups.Details", "ProductGroups.Export",
            "ProductGroupPolicies.Index", "ProductGroupPolicies.Details", "ProductGroupPolicies.Export",
            "ProductBonusGroups.Index", "ProductBonusGroups.Details", "ProductBonusGroups.Export",
            "WarehousePolicyRules.Index", "WarehousePolicyRules.Details", "WarehousePolicyRules.Export", "WarehousePolicyRules.GetPoliciesNotUsedForWarehouse");

        private static readonly string[] GeoRead = M(
            "Governorates.Index", "Governorates.Details", "Governorates.Export",
            "Dstricts.Index", "Dstricts.Details", "Dstricts.Export",
            "Areas.Index", "Areas.Details", "Areas.Export",
            "Branches.Index", "Branches.Details", "Branches.Export",
            "Cities.Index", "Cities.Details");

        private static readonly string[] NotesPack = M(
            "DebitNotes.Index", "DebitNotes.Show", "DebitNotes.Details", "DebitNotes.Create", "DebitNotes.Edit",
            "DebitNotes.Export", "DebitNotes.Open", "DebitNotes.GetCustomerAccount",
            "CreditNotes.Index", "CreditNotes.Show", "CreditNotes.Details", "CreditNotes.Create", "CreditNotes.Edit",
            "CreditNotes.Export", "CreditNotes.Open", "CreditNotes.GetCustomerAccount");

        private static readonly string[] SalesReturnsRep = M(
            "SalesReturns.Index", "SalesReturns.Show", "SalesReturns.Create", "SalesReturns.Edit", "SalesReturns.Export",
            "SalesReturns.SaveHeader", "SalesReturns.GetInvoiceItems", "SalesReturns.SearchProductsForReturn", "SalesReturns.GetProductsForReturnDatalist",
            "SalesReturns.AddLineJson", "SalesReturns.RemoveLineJson", "SalesReturns.ClearLinesJson", "SalesReturns.OpenReturn", "SalesReturns.PostReturn");

        /// <summary>المدير العام — وصول واسع للعمليات التشغيلية والإدارة (بدون صلاحيات صيانة خطرة مثل تصفير أرصمة جماعي إن لم تُذكر).</summary>
        public static IReadOnlyList<string> GeneralManager() => M(
            Nav.Concat(GeneralList).Concat(CustomersStd).Concat(SalesManagerExtra).Concat(PurchasePack)
                .Concat(InventoryPack).Concat(AccountsCore).Concat(LedgerEntriesFull).Concat(CashVouchers).Concat(NotesPack).Concat(ReportsRead)
                .Concat(GeoRead).Concat(SettingsRead).Concat(GmAdmin).Concat(new[]
                {
                    "Employees.Index", "Employees.Show", "Employees.Export",
                    "Routes.Index", "Routes.Export", "Routes.GetColumnValues",
                    "ProductClassifications.Index", "ProductClassifications.Export", "ProductClassifications.GetColumnValues"
                }).ToArray());

        public static IReadOnlyList<string> SalesManager() => M(
            Nav.Concat(GeneralList).Concat(CustomersStd).Concat(SalesManagerExtra).Concat(CashVouchers)
                .Concat(ReportsRead).Concat(ProductsRead).Concat(WarehousesStd).ToArray());

        public static IReadOnlyList<string> SalesRepresentative() => M(
            Nav.Concat(CustomersStd).Concat(SalesInvoiceRep).Concat(SalesReturnsRep).Concat(SalesReturnLinesOnly)
                .Concat(new[] { "CustomerLedger.Index" }).Concat(CashVouchers).Concat(ProductsRead).Concat(WarehousesStd).ToArray());

        public static IReadOnlyList<string> PurchaseManager() => M(
            Nav.Concat(GeneralList).Concat(PurchasePack).Concat(CustomersStd).Concat(ProductsRead).Concat(WarehousesStd)
                .Concat(new[] { "CustomerLedger.Index" }).Concat(ReportsRead).ToArray());

        public static IReadOnlyList<string> WarehouseManager() => M(Nav.Concat(GeneralList).Concat(InventoryPack).ToArray());

        public static IReadOnlyList<string> Accountant() => M(
            Nav.Concat(GeneralList).Concat(AccountsCore).Concat(LedgerEntriesFull).Concat(CashVouchers).Concat(NotesPack)
                .Concat(CustomersStd).Concat(new[] { "CustomerLedger.Index" }).Concat(ReportsRead)
                .Concat(new[] { "Treasury.ZeroTreasuryBalance" }).ToArray());

        public static IReadOnlyList<string> ReportsOnly() => M(
            Nav.Concat(GeneralList).Concat(ReportsRead).Concat(new[]
            {
                "CustomerLedger.Index", "LedgerEntries.Index", "LedgerEntries.Show", "LedgerEntries.Export",
                "Accounts.Index", "Accounts.Export"
            }).ToArray());

        public static IReadOnlyList<string> BasicUser() => M(
            Nav.Concat(new[]
            {
                "SalesInvoices.Index", "SalesInvoices.Show", "SalesInvoices.Create", "SalesInvoices.Edit",
                "SalesInvoices.SaveHeader", "SalesInvoices.GetProductsForDatalist", "SalesInvoices.AddLineJson", "SalesInvoices.RemoveLineJson",
                "SalesInvoices.SaveTaxJson", "SalesInvoices.PostInvoice", "SalesInvoices.OpenInvoice",
                "Customers.Index", "Customers.Show", "Customers.GetDefaultAccountForParty", "Customers.GetDistrictsByGovernorate", "Customers.GetAreasByDistrict",
                "Products.Index", "Products.Show", "Products.SearchProducts", "Products.SearchProductsByCode"
            }).ToArray());
    }
}
