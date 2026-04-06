namespace ERP.Services
{
    public class LoginRedirectService : ILoginRedirectService
    {
        private readonly IPermissionService _permissionService;

        private static readonly LoginRedirectTarget AccessDeniedTarget = new("AccessDenied", "Home");

        private static readonly (string PermissionCode, LoginRedirectTarget Target)[] Candidates =
        {
            ("Dashboard.Sales", new LoginRedirectTarget("Sales", "Dashboard")),
            ("Accounts.Index", new LoginRedirectTarget("Index", "Accounts")),
            ("LedgerEntries.Index", new LoginRedirectTarget("Index", "LedgerEntries")),
            ("CashReceipts.Index", new LoginRedirectTarget("Index", "CashReceipts")),
            ("CashPayments.Index", new LoginRedirectTarget("Index", "CashPayments")),
            ("SalesInvoices.Index", new LoginRedirectTarget("Index", "SalesInvoices")),
            ("PurchaseInvoices.Index", new LoginRedirectTarget("Index", "PurchaseInvoices")),
            ("Customers.Index", new LoginRedirectTarget("Index", "Customers")),
            ("Products.Index", new LoginRedirectTarget("Index", "Products")),
            ("StockLedger.Index", new LoginRedirectTarget("Index", "StockLedger")),
            ("UserActivityLogs.Index", new LoginRedirectTarget("Index", "UserActivityLogs"))
        };

        public LoginRedirectService(IPermissionService permissionService)
        {
            _permissionService = permissionService;
        }

        public async Task<LoginRedirectTarget> GetTargetAsync(int userId)
        {
            if (userId <= 0)
                return AccessDeniedTarget;

            foreach (var candidate in Candidates)
            {
                if (await _permissionService.HasPermissionAsync(userId, candidate.PermissionCode))
                    return candidate.Target;
            }

            return AccessDeniedTarget;
        }
    }
}
