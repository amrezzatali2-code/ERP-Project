namespace ERP.Tests;

public class ProductBalancesViewLayout_Tests
{
    private const string ViewPath = @"D:\Courses\Courses to study\ASP.NET\ERP\Views\Reports\ProductBalances.cshtml";

    [Fact]
    public void ProductBalancesView_UsesTopGenerateButtonAndNoLegacyDateHints()
    {
        var text = File.ReadAllText(ViewPath);

        Assert.Contains("erp-pb-top-generate-wrap", text);
        Assert.Contains("erp-pb-top-actions-group", text);
        Assert.Contains("erp-pb-right-fields-group", text);
        Assert.Contains("erp-pb-row2-main", text);
        Assert.Contains("erp-pb-date-icon-svg", text);
        Assert.DoesNotContain("إن تركت «إلى» فارغاً", text);
        Assert.DoesNotContain("placeholder=\"يوم/شهر/سنة 00:00\"", text);
        Assert.Contains("autocomplete=\"off\"", text);
        Assert.Contains("erp-pb-date-input-group", text);
    }
}
