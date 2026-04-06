using System.Reflection;
using ERP.Controllers;
using ERP.Filters;
using ERP.Security;

namespace ERP.Tests;

public class GeneralPermissions_Tests
{
    [Theory]
    [InlineData("Accounts.Index")]
    [InlineData("Home.Index")]
    [InlineData("Dashboard.Sales")]
    [InlineData("Reports.CustomerBalances")]
    public void GlobalOpen_DoesNotGateNormalScreensAndReports(string code)
    {
        Assert.Null(GlobalPermissionGates.TryGetRequiredGlobalCode(code));
    }

    [Theory]
    [InlineData("SalesInvoices.OpenInvoice")]
    [InlineData("PurchaseInvoices.OpenInvoice")]
    [InlineData("CashPayments.Open")]
    [InlineData("CashReceipts.Open")]
    [InlineData("DebitNotes.Unlock")]
    [InlineData("CreditNotes.Unlock")]
    public void GlobalOpen_GatesOnlyOpenClosedDocuments(string code)
    {
        Assert.Equal(GlobalPermissionGates.Open, GlobalPermissionGates.TryGetRequiredGlobalCode(code));
    }

    [Theory]
    [InlineData(typeof(CashPaymentsController), "Delete", "CashPayments.Delete")]
    [InlineData(typeof(CashPaymentsController), "DeleteConfirmed", "CashPayments.Delete")]
    [InlineData(typeof(CashPaymentsController), "BulkDelete", "CashPayments.BulkDelete")]
    [InlineData(typeof(CashPaymentsController), "DeleteAll", "CashPayments.DeleteAll")]
    [InlineData(typeof(CashReceiptsController), "Delete", "CashReceipts.Delete")]
    [InlineData(typeof(CashReceiptsController), "DeleteConfirmed", "CashReceipts.Delete")]
    [InlineData(typeof(CashReceiptsController), "BulkDelete", "CashReceipts.BulkDelete")]
    [InlineData(typeof(CashReceiptsController), "DeleteAll", "CashReceipts.DeleteAll")]
    [InlineData(typeof(DebitNotesController), "Delete", "DebitNotes.Delete")]
    [InlineData(typeof(DebitNotesController), "DeleteConfirmed", "DebitNotes.Delete")]
    [InlineData(typeof(DebitNotesController), "BulkDelete", "DebitNotes.BulkDelete")]
    [InlineData(typeof(DebitNotesController), "DeleteAll", "DebitNotes.DeleteAll")]
    [InlineData(typeof(CreditNotesController), "Delete", "CreditNotes.Delete")]
    [InlineData(typeof(CreditNotesController), "DeleteConfirmed", "CreditNotes.Delete")]
    [InlineData(typeof(CreditNotesController), "BulkDelete", "CreditNotes.BulkDelete")]
    [InlineData(typeof(CreditNotesController), "DeleteAll", "CreditNotes.DeleteAll")]
    public void DeleteActions_HaveExplicitPermissionAttributes(Type controllerType, string methodName, string expectedPermission)
    {
        var method = controllerType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);

        var attr = method!.GetCustomAttributes(typeof(RequirePermissionAttribute), true)
            .OfType<RequirePermissionAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attr);
        Assert.Equal(expectedPermission, attr!.PermissionCode);
    }
}
