namespace AspxLint.Core.Tests;

public class RuleSpecificTests
{
    /// <summary>
    /// Regression : CLAUDE.md "Bugs résolus" #3 — sur &lt;asp:Label&gt;&lt;/asp:Label&gt;
    /// l'ancien fix produisait &lt;asp:Labelrunat="server"&gt; (espace manquant),
    /// que la regle re-detectait infinement. Le fix doit toujours laisser
    /// au moins une espace devant runat.
    /// </summary>
    [Fact]
    public void ASP001_fix_handles_open_tag_without_attrs_correctly()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "ASP-001");
        var ctx = new RuleContext("aspx", "x.aspx");

        var input = "<asp:Label></asp:Label>";
        var fixed1 = rule.Fix(input, ctx)!;

        Assert.DoesNotContain("Labelrunat", fixed1); // pas de collision
        Assert.Contains("<asp:Label runat=\"server\">", fixed1);
        // et idempotent
        Assert.Equal(fixed1, rule.Fix(fixed1, ctx));
    }

    [Fact]
    public void ASP001_fix_handles_self_closing_with_attrs()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "ASP-001");
        var ctx = new RuleContext("aspx", "x.aspx");

        var fixed1 = rule.Fix("<asp:Label Text=\"hi\" />", ctx)!;
        Assert.Contains("runat=\"server\"", fixed1);
        Assert.EndsWith(" />", fixed1);
    }

    [Fact]
    public void ASP001_skips_controls_already_having_runat()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "ASP-001");
        var ctx = new RuleContext("aspx", "x.aspx");
        var content = "<asp:Label runat=\"server\" />\n<asp:Button runat='server' />\n";
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void TAG002_does_not_lower_namespaced_controls()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "TAG-002");
        var ctx = new RuleContext("aspx", "x.aspx");
        var content = "<asp:Label /><uc:MyControl /><My:Tag />\n";
        // Ces tags ne sont PAS du HTML standard, donc TAG-002 doit les ignorer.
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void TAG002_fix_does_not_change_attribute_values()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "TAG-002");
        var ctx = new RuleContext("aspx", "x.aspx");
        // La valeur d'attribut "DIV" ne doit PAS etre touchee, seulement le tag.
        var fixed1 = rule.Fix("<DIV class=\"DIV\">x</DIV>", ctx)!;
        Assert.Equal("<div class=\"DIV\">x</div>", fixed1);
    }

    [Fact]
    public void TAG003_balanced_tags_yield_no_issue()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "TAG-003");
        var ctx = new RuleContext("aspx", "x.aspx");
        var content = "<html><body><p>ok</p></body></html>";
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void TAG003_detects_unclosed_tag()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "TAG-003");
        var ctx = new RuleContext("aspx", "x.aspx");
        var content = "<html>\n<body>\n<div>missing close\n</body>\n</html>\n";
        var issues = rule.Detect(content, content.Split('\n'), ctx).ToList();
        // <div> jamais ferme + mismatch sur </body> qui voit <div> au top
        Assert.NotEmpty(issues);
        Assert.Contains(issues, i => i.Hint != null && i.Hint.Contains("div"));
    }

    [Fact]
    public void TAG003_ignores_void_tags_in_balance()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "TAG-003");
        var ctx = new RuleContext("aspx", "x.aspx");
        var content = "<html><body><br><hr><img src=\"x\"></body></html>";
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void TAG003_ignores_tags_inside_asp_blocks()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "TAG-003");
        var ctx = new RuleContext("aspx", "x.aspx");
        // <p> dans le code serveur ne doit pas perturber l'analyse.
        var content = "<html><body><%= \"<p>fake</p>\" %></body></html>";
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void TAG003_ignores_tags_inside_html_comments()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "TAG-003");
        var ctx = new RuleContext("aspx", "x.aspx");
        var content = "<html><body><!-- <p>commented</p> --></body></html>";
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void ATTR001_skips_lines_containing_html_comments()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "ATTR-001");
        var ctx = new RuleContext("aspx", "x.aspx");
        var content = "<!-- <input type=text> -->\n";
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void ATTR001_skips_directive_lines()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "ATTR-001");
        var ctx = new RuleContext("aspx", "x.aspx");
        // Les directives <%@ ... %> ont une syntaxe a part, on les ignore en detection.
        var content = "<%@ Page Language=C# %>\n";
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Theory]
    [InlineData("aspx", "Page")]
    [InlineData("ascx", "Control")]
    [InlineData("master", "Master")]
    public void DIR001_fix_inserts_correct_directive_per_extension(string ext, string expectedKeyword)
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "DIR-001");
        var ctx = new RuleContext(ext, "x." + ext);
        var fixed1 = rule.Fix("<html></html>\n", ctx)!;
        Assert.StartsWith($"<%@ {expectedKeyword}", fixed1);
    }

    [Fact]
    public void DIR001_fix_moves_misplaced_directive_to_top()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "DIR-001");
        var ctx = new RuleContext("aspx", "x.aspx");
        var input = "<html>\n<body></body>\n</html>\n<%@ Page Language=\"C#\" %>\n";
        var fixed1 = rule.Fix(input, ctx)!;
        Assert.StartsWith("<%@ Page", fixed1);
    }

    [Fact]
    public void COM001_detects_dashes_inside_html_comment()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "COM-001");
        var ctx = new RuleContext("aspx", "x.aspx");
        var content = "<!-- so -- bad -->\n";
        Assert.Single(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void COM001_silent_on_clean_comment()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "COM-001");
        var ctx = new RuleContext("aspx", "x.aspx");
        var content = "<!-- normal comment -->\n";
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void SM001_silent_on_single_ScriptManager()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "SM-001");
        var ctx = new RuleContext("aspx", "x.aspx");
        var content = "<asp:ScriptManager runat=\"server\" />\n";
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void SM001_fires_only_for_extras_not_for_first()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "SM-001");
        var ctx = new RuleContext("aspx", "x.aspx");
        var content = "<asp:ScriptManager ID=\"sm1\" />\n<asp:ScriptManager ID=\"sm2\" />\n<asp:ScriptManager ID=\"sm3\" />\n";
        var issues = rule.Detect(content, content.Split('\n'), ctx).ToList();
        Assert.Equal(2, issues.Count); // sm2 et sm3, pas sm1
    }

    [Fact]
    public void ASP002_silent_when_all_ids_unique()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "ASP-002");
        var ctx = new RuleContext("aspx", "x.aspx");
        var content = "<asp:Label ID=\"a\" runat=\"server\" />\n<asp:Label ID=\"b\" runat=\"server\" />\n";
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void ASP002_detects_duplicate_id_pointing_to_first_line()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "ASP-002");
        var ctx = new RuleContext("aspx", "x.aspx");
        var content = "<asp:Label ID=\"a\" runat=\"server\" />\n<asp:Label ID=\"a\" runat=\"server\" />\n";
        var issues = rule.Detect(content, content.Split('\n'), ctx).ToList();
        Assert.Single(issues);
        Assert.Contains("ligne 1", issues[0].Hint!);
    }

    [Fact]
    public void TAG001_does_not_fire_on_already_self_closed_tags()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "TAG-001");
        var ctx = new RuleContext("aspx", "x.aspx");
        var content = "<br /><hr/><img src=\"x\" />";
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void TAG001_fix_preserves_existing_attributes()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "TAG-001");
        var ctx = new RuleContext("aspx", "x.aspx");
        var fixed1 = rule.Fix("<img src=\"a.png\" alt=\"a\">", ctx)!;
        Assert.Contains("src=\"a.png\"", fixed1);
        Assert.Contains("alt=\"a\"", fixed1);
        Assert.EndsWith(" />", fixed1);
    }

    [Fact]
    public void ASP005_skips_directives()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "ASP-005");
        var ctx = new RuleContext("aspx", "x.aspx");
        // <%@ Page %> est une directive, pas un bloc d'execution => skip.
        var content = "<%@ Page Language=\"C#\" %>\n";
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void WS002_silent_on_pure_space_indent()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "WS-002");
        var ctx = new RuleContext("aspx", "x.aspx");
        var content = "    a\n    b\n";
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void WS002_silent_on_pure_tab_indent()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "WS-002");
        var ctx = new RuleContext("aspx", "x.aspx");
        var content = "\ta\n\tb\n";
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }
}
