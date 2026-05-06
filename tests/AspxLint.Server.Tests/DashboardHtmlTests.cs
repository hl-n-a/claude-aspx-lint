namespace AspxLint.Server.Tests;

/// <summary>
/// Smoke tests sur le rendu de la dashboard / : verifient que ExpandIncludes
/// resout bien tous les marqueurs {{include:...}}, et qu'une fonction-cle de
/// chacun des 17 modules JS est presente dans la sortie.
/// </summary>
public class DashboardHtmlTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fx;
    public DashboardHtmlTests(ApiFixture fx) => _fx = fx;

    private async Task<string> GetDashboardHtml()
    {
        var client = _fx.CreateAuthClient();
        var r = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await r.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Dashboard_resolves_all_include_markers()
    {
        var html = await GetDashboardHtml();
        // Tous les {{include:...}} doivent etre remplaces.
        Assert.DoesNotContain("{{include:", html);
    }

    [Fact]
    public async Task Dashboard_inlines_styles_and_partials()
    {
        var html = await GetDashboardHtml();
        // styles.css : selecteur arbitraire mais stable.
        Assert.Contains(".sidebar", html);
        // partials/modal-paste.html : id du textarea.
        Assert.Contains("id=\"pasteContent\"", html);
        // partials/modal-batch-report.html : id du modal.
        Assert.Contains("id=\"batchReportModal\"", html);
    }

    [Theory]
    // Une fonction-cle par module : si le module est absent, le test casse.
    [InlineData("01-state",            "loadRulesFromServer")]
    [InlineData("02-files-tree",       "buildFileTree")]
    [InlineData("03-analysis",         "analyzeFile")]
    [InlineData("04-highlight",        "highlightLine")]
    [InlineData("05-render-code",      "renderFileList")]
    [InlineData("06-minimap",          "renderMinimap")]
    [InlineData("07-diff",             "lineDiff")]
    [InlineData("08-edit-issues-stats", "renderIssues")]
    [InlineData("09-actions",          "selectFile")]
    [InlineData("10-fileio-server",    "saveCurrentToServer")]
    [InlineData("11-bulk",             "fixAllInProject")]
    [InlineData("12-modals-toast",     "showToast")]
    [InlineData("13-dragdrop",         "initDragDrop")]
    [InlineData("14-search",           "openSearchBar")]
    [InlineData("15-palette",          "openPalette")]
    [InlineData("16-keyboard",         "navigateFile")]
    [InlineData("17-desktop-sse",      "connectSse")]
    public async Task Dashboard_includes_module(string moduleName, string functionName)
    {
        var html = await GetDashboardHtml();
        Assert.True(
            html.Contains(functionName),
            $"Module '{moduleName}.js' semble absent : '{functionName}' introuvable dans la sortie.");
    }
}
