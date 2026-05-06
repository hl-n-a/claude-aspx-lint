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

    [Fact]
    public async Task Dashboard_links_to_favicon()
    {
        var html = await GetDashboardHtml();
        Assert.Contains("rel=\"icon\"", html);
        Assert.Contains("/favicon.ico", html);
    }

    [Fact]
    public async Task Favicon_is_served_without_auth()
    {
        // /favicon.ico ne doit PAS demander de token : un browser charge le
        // favicon avant d'avoir traite le cookie/token de la requete /.
        var client = _fx.CreateClient();   // raw, sans cookie d'auth
        var r = await client.GetAsync("/favicon.ico");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("image/x-icon", r.Content.Headers.ContentType?.MediaType);
        var bytes = await r.Content.ReadAsByteArrayAsync();
        // ICO signature : 00 00 01 00 (reserved + type=ICO)
        Assert.True(bytes.Length > 100);
        Assert.Equal(0, bytes[0]);
        Assert.Equal(0, bytes[1]);
        Assert.Equal(1, bytes[2]);
        Assert.Equal(0, bytes[3]);
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
