namespace AspxLint.Core.Tests;

public class EdgeCaseTests
{
    [Theory]
    [MemberData(nameof(RegistryTests.AllRules), MemberType = typeof(RegistryTests))]
    public void All_rules_handle_empty_content_without_throwing(IRule rule)
    {
        var ctx = new RuleContext("aspx", "x.aspx");
        var lines = Array.Empty<string>();
        var ex = Record.Exception(() => rule.Detect("", lines, ctx).ToList());
        Assert.Null(ex);
    }

    [Theory]
    [MemberData(nameof(RegistryTests.AllRules), MemberType = typeof(RegistryTests))]
    public void All_rules_handle_whitespace_only_without_throwing(IRule rule)
    {
        var ctx = new RuleContext("aspx", "x.aspx");
        var content = "   \n\t\n  \n";
        var lines = content.Split('\n');
        var ex = Record.Exception(() => rule.Detect(content, lines, ctx).ToList());
        Assert.Null(ex);
    }

    [Theory]
    [MemberData(nameof(RegistryTests.AllRules), MemberType = typeof(RegistryTests))]
    public void All_rules_handle_single_newline_without_throwing(IRule rule)
    {
        var ctx = new RuleContext("aspx", "x.aspx");
        var content = "\n";
        var lines = content.Split('\n');
        var ex = Record.Exception(() => rule.Detect(content, lines, ctx).ToList());
        Assert.Null(ex);
    }

    [Fact]
    public void DOC001_does_not_fire_on_ascx()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "DOC-001");
        var content = "<html>\n<body></body>\n</html>\n";
        var ctx = new RuleContext("ascx", "x.ascx");
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void DOC001_does_not_fire_on_master()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "DOC-001");
        var content = "<html>\n<body></body>\n</html>\n";
        var ctx = new RuleContext("master", "x.master");
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void DOC001_does_not_fire_on_content_pages_with_MasterPageFile()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "DOC-001");
        var content = "<%@ Page MasterPageFile=\"~/Site.master\" %>\n<html>\n</html>\n";
        var ctx = new RuleContext("aspx", "x.aspx");
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void FORM001_does_not_fire_when_no_asp_controls_present()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "FORM-001");
        var content = "<%@ Page %>\n<form>\n</form>\n";   // pas d'<asp:...>
        var ctx = new RuleContext("aspx", "x.aspx");
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void FORM001_does_not_fire_on_ascx()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "FORM-001");
        var content = "<asp:Label runat=\"server\" />\n<form>\n</form>\n";
        var ctx = new RuleContext("ascx", "x.ascx");
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void DIR001_does_not_fire_on_unknown_extension()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "DIR-001");
        var content = "anything\n";
        var ctx = new RuleContext("asax", "Global.asax");
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void ASP003_only_fires_on_master_extension()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "ASP-003");
        var content = "<asp:ContentPlaceHolder runat=\"server\" />\n";

        Assert.Empty(rule.Detect(content, content.Split('\n'), new RuleContext("aspx", "x.aspx")));
        Assert.Empty(rule.Detect(content, content.Split('\n'), new RuleContext("ascx", "x.ascx")));
        Assert.Single(rule.Detect(content, content.Split('\n'), new RuleContext("master", "x.master")));
    }

    [Fact]
    public void ATTR002_skips_lines_containing_asp_blocks()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "ATTR-002");
        // La regle skip les lignes avec <% %> car les ' peuvent etre du C# ('hello').
        var content = "<a href='x' onclick=\"<%= GetSomething('arg') %>\">link</a>\n";
        var ctx = new RuleContext("aspx", "x.aspx");
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void ATTR001_fix_does_not_modify_asp_blocks()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "ATTR-001");
        var content = "<input value=<%= GetVal() %> />";
        var ctx = new RuleContext("aspx", "x.aspx");
        var fixed1 = rule.Fix(content, ctx)!;
        Assert.Contains("<%= GetVal() %>", fixed1); // bloc serveur intact
    }

    [Fact]
    public void CHAR001_skips_lines_with_asp_blocks()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "CHAR-001");
        var content = "if (a && b) { <%= x %> }\n";  // && est du C#
        var ctx = new RuleContext("aspx", "x.aspx");
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void CHAR001_does_not_fire_on_valid_entities()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "CHAR-001");
        var content = "Tom &amp; Jerry &#123; &#x1F; &lt;\n";
        var ctx = new RuleContext("aspx", "x.aspx");
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }
}
