using AspxLint.Core.Rules;

namespace AspxLint.Core.Tests;

/// <summary>
/// Tests des regles CFG-XXX qui s'appliquent uniquement aux fichiers .config.
/// Couvre detection (positive + negative) et auto-fix idempotence.
/// </summary>
public class WebConfigRulesTests
{
    private static IEnumerable<Issue> Run(IRule rule, string content, string ext = "config")
    {
        var ctx = new RuleContext(ext, $"Web.{ext}");
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        return rule.Detect(content, lines, ctx);
    }

    // ====== CFG-001 : compilation debug ======

    [Fact]
    public void Cfg001_fires_on_debug_true()
    {
        var rule = new Cfg001CompilationDebug();
        var issues = Run(rule, "<configuration><system.web><compilation debug=\"true\" targetFramework=\"4.8\" /></system.web></configuration>");
        Assert.NotEmpty(issues);
    }

    [Fact]
    public void Cfg001_does_not_fire_on_debug_false()
    {
        var rule = new Cfg001CompilationDebug();
        var issues = Run(rule, "<compilation debug=\"false\" />");
        Assert.Empty(issues);
    }

    [Fact]
    public void Cfg001_does_not_fire_on_aspx_ext()
    {
        var rule = new Cfg001CompilationDebug();
        var issues = Run(rule, "<compilation debug=\"true\" />", ext: "aspx");
        Assert.Empty(issues);
    }

    [Fact]
    public void Cfg001_fix_replaces_true_with_false()
    {
        var rule = new Cfg001CompilationDebug();
        var input = "<compilation debug=\"true\" targetFramework=\"4.8\" />";
        var ctx = new RuleContext("config", "Web.config");
        var fixedContent = rule.Fix(input, ctx);
        Assert.NotNull(fixedContent);
        Assert.Contains("debug=\"false\"", fixedContent);
        Assert.DoesNotContain("debug=\"true\"", fixedContent);
    }

    [Fact]
    public void Cfg001_fix_is_idempotent()
    {
        var rule = new Cfg001CompilationDebug();
        var input = "<compilation debug=\"true\" />";
        var ctx = new RuleContext("config", "Web.config");
        var first = rule.Fix(input, ctx)!;
        var second = rule.Fix(first, ctx)!;
        Assert.Equal(first, second);
    }

    // ====== CFG-002 : customErrors mode=Off ======

    [Fact]
    public void Cfg002_fires_on_customErrors_off()
    {
        var rule = new Cfg002CustomErrorsOff();
        var issues = Run(rule, "<customErrors mode=\"Off\" />");
        Assert.NotEmpty(issues);
    }

    [Fact]
    public void Cfg002_does_not_fire_on_customErrors_remoteOnly()
    {
        var rule = new Cfg002CustomErrorsOff();
        var issues = Run(rule, "<customErrors mode=\"RemoteOnly\" />");
        Assert.Empty(issues);
    }

    [Fact]
    public void Cfg002_fix_replaces_off_with_remoteOnly()
    {
        var rule = new Cfg002CustomErrorsOff();
        var ctx = new RuleContext("config", "Web.config");
        var fixedContent = rule.Fix("<customErrors mode=\"Off\" defaultRedirect=\"err.aspx\" />", ctx);
        Assert.Contains("mode=\"RemoteOnly\"", fixedContent);
        Assert.Contains("defaultRedirect=\"err.aspx\"", fixedContent);
    }

    // ====== CFG-003 : trace enabled ======

    [Fact]
    public void Cfg003_fires_on_trace_enabled_true()
    {
        var rule = new Cfg003TraceEnabled();
        var issues = Run(rule, "<trace enabled=\"true\" pageOutput=\"true\" />");
        Assert.NotEmpty(issues);
    }

    [Fact]
    public void Cfg003_does_not_fire_on_trace_enabled_false()
    {
        var rule = new Cfg003TraceEnabled();
        var issues = Run(rule, "<trace enabled=\"false\" />");
        Assert.Empty(issues);
    }

    [Fact]
    public void Cfg003_fix_replaces_true_with_false()
    {
        var rule = new Cfg003TraceEnabled();
        var ctx = new RuleContext("config", "Web.config");
        var fixedContent = rule.Fix("<trace enabled=\"true\" pageOutput=\"true\" />", ctx);
        Assert.Contains("enabled=\"false\"", fixedContent);
    }

    // ====== CFG-004 : httpCookies ======

    [Fact]
    public void Cfg004_fires_when_httpOnlyCookies_missing()
    {
        var rule = new Cfg004HttpCookiesNotSecure();
        var issues = Run(rule, "<httpCookies requireSSL=\"true\" />");
        Assert.NotEmpty(issues);
        Assert.Contains("httpOnlyCookies", issues.First().Hint);
    }

    [Fact]
    public void Cfg004_fires_when_requireSSL_missing()
    {
        var rule = new Cfg004HttpCookiesNotSecure();
        var issues = Run(rule, "<httpCookies httpOnlyCookies=\"true\" />");
        Assert.NotEmpty(issues);
        Assert.Contains("requireSSL", issues.First().Hint);
    }

    [Fact]
    public void Cfg004_does_not_fire_when_both_present()
    {
        var rule = new Cfg004HttpCookiesNotSecure();
        var issues = Run(rule, "<httpCookies httpOnlyCookies=\"true\" requireSSL=\"true\" />");
        Assert.Empty(issues);
    }

    [Fact]
    public void Cfg004_has_no_fix()
    {
        var rule = new Cfg004HttpCookiesNotSecure();
        Assert.False(rule.HasFix);
        var ctx = new RuleContext("config", "Web.config");
        Assert.Null(rule.Fix("<httpCookies />", ctx));
    }

    // ====== CFG-005 : sessionState InProc ======

    [Fact]
    public void Cfg005_fires_on_InProc()
    {
        var rule = new Cfg005SessionStateInProc();
        var issues = Run(rule, "<sessionState mode=\"InProc\" timeout=\"20\" />");
        Assert.NotEmpty(issues);
    }

    [Fact]
    public void Cfg005_does_not_fire_on_StateServer()
    {
        var rule = new Cfg005SessionStateInProc();
        var issues = Run(rule, "<sessionState mode=\"StateServer\" stateConnectionString=\"...\" />");
        Assert.Empty(issues);
    }

    // ====== CFG-006 : connectionString password en clair ======

    [Fact]
    public void Cfg006_fires_on_password_in_connectionString()
    {
        var rule = new Cfg006ConnectionStringPlaintext();
        var issues = Run(rule, "<add name=\"Db\" connectionString=\"Server=.;Database=foo;User Id=sa;Password=Sup3rS3cret\" />");
        Assert.NotEmpty(issues);
    }

    [Fact]
    public void Cfg006_fires_on_pwd_alias()
    {
        var rule = new Cfg006ConnectionStringPlaintext();
        var issues = Run(rule, "<add connectionString=\"Server=x;User=y;Pwd=secret\" />");
        Assert.NotEmpty(issues);
    }

    [Fact]
    public void Cfg006_does_not_fire_on_integrated_security()
    {
        var rule = new Cfg006ConnectionStringPlaintext();
        var issues = Run(rule, "<add connectionString=\"Server=.;Database=foo;Integrated Security=true\" />");
        Assert.Empty(issues);
    }

    [Fact]
    public void Cfg006_does_not_fire_outside_connectionString_line()
    {
        var rule = new Cfg006ConnectionStringPlaintext();
        // password=... sur une ligne SEPARE de la connectionString ne doit pas
        // fire (le contexte est detecte par ligne).
        var issues = Run(rule,
            "<!-- password=foo -->\n" +
            "<add name=\"Db\" connectionString=\"Server=.;Integrated Security=true\" />");
        Assert.Empty(issues);
    }

    // ====== Integration : ProjectScanner accepte .config ======

    [Fact]
    public void ProjectScanner_default_extensions_include_config()
    {
        Assert.Contains(".config", ProjectScanner.DefaultExtensions);
    }

    [Fact]
    public void Analyzer_routes_config_file_to_cfg_rules()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("Web.config",
            "<configuration><system.web>" +
            "<compilation debug=\"true\" targetFramework=\"4.8\" />" +
            "<customErrors mode=\"Off\" />" +
            "</system.web></configuration>\n");

        var issues = Analyzer.Analyze(path, File.ReadAllText(path), RuleRegistry.All);
        var ids = issues.Select(i => i.RuleId).ToHashSet();

        Assert.Contains("CFG-001", ids);
        Assert.Contains("CFG-002", ids);
    }
}
