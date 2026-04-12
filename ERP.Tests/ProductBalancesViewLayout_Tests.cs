namespace ERP.Tests;

public class ProductBalancesViewLayout_Tests
{
    private static string GetProductBalancesViewPath()
    {
        var dir = Path.GetDirectoryName(typeof(ProductBalancesViewLayout_Tests).Assembly.Location) ?? "";
        for (var i = 0; i < 12; i++)
        {
            var candidate = Path.Combine(dir, "Views", "Reports", "ProductBalances.cshtml");
            if (File.Exists(candidate))
                return candidate;
            var parent = Directory.GetParent(dir);
            if (parent == null)
                break;
            dir = parent.FullName;
        }

        throw new FileNotFoundException("Views/Reports/ProductBalances.cshtml not found walking up from test output.");
    }

    [Fact]
    public void ProductBalancesView_UsesTopGenerateButtonAndDateInputsLikeVolume()
    {
        var path = GetProductBalancesViewPath();
        var text = File.ReadAllText(path);

        Assert.Contains("erp-pb-top-generate-wrap", text);
        Assert.Contains("erp-pb-top-actions-group", text);
        Assert.Contains("erp-pb-right-fields-group", text);
        Assert.Contains("erp-pb-row2-main", text);
        Assert.Contains("erp-pb-filter-row1", text);
        Assert.Contains("erp-pb-col-apply-cancel", text);
        Assert.Contains("erp-pb-dates-inline-group", text);
        Assert.Contains("<span>📊</span> تجميع", text);
        Assert.DoesNotContain("تجميع التقرير", text);
        Assert.Contains("name=\"fromDate\"", text);
        Assert.Contains("name=\"toDate\"", text);
        Assert.Contains("type=\"date\"", text);
        Assert.Contains("erpPbCenterToast", text);
        Assert.Contains("erpPbSearchCancel", text);
        Assert.Contains("تطبيق</button>", text);
        Assert.Contains("erp-export-form", text);
        Assert.Contains("name=\"visibleCols\"", text);
        Assert.Contains("erpPbExportVisibleCols", text);
        Assert.Contains("ExportProductBalances", text);
        Assert.Contains("name=\"format\"", text);
        Assert.Contains("<option value=\"pdf\">PDF</option>", text);
        Assert.Contains("btn-erp-success", text);
        Assert.Contains("تصدير", text);
        Assert.Contains("erp-columns-modal", text);
        Assert.Contains("erp-pb-columns-modal-list", text);
        Assert.Contains("erpPbColumnsMeta", text);
        Assert.Contains("erp-pb-export-btn", text);
        Assert.Contains("erp-pb-export-format-select", text);
        Assert.True(
            text.IndexOf("erp-pb-export-btn", StringComparison.Ordinal) < text.IndexOf("erp-pb-export-format-select", StringComparison.Ordinal),
            "Expected export button markup before format select (تصدير left of Excel in LTR group).");
        Assert.Contains("formaction=", text);
        Assert.DoesNotContain("name=\"categoryId\"", text);
        Assert.DoesNotContain("name=\"productGroupId\"", text);
        Assert.DoesNotContain("إن تركت «إلى» فارغاً", text);
        Assert.DoesNotContain("placeholder=\"يوم/شهر/سنة 00:00\"", text);
        Assert.Contains("autocomplete=\"off\"", text);
    }
}
