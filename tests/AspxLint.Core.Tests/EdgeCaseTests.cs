using System.Text.RegularExpressions;

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
    public void ATTR002_ignores_single_quotes_inside_asp_blocks()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "ATTR-002");
        // Les ' a l'interieur de <% %> sont du C# (chaines), pas des attributs HTML.
        // En revanche, href='x' (HORS du bloc serveur) reste un attribut single-quote
        // que la regle doit signaler.
        var content = "<a onclick=\"<%= GetSomething('arg') %>\">link</a>\n";
        var ctx = new RuleContext("aspx", "x.aspx");
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void ATTR002_handles_multiline_asp_block()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "ATTR-002");
        // Bloc serveur multi-ligne avec ' du C# : ne doit pas declencher.
        var content = "<% if (foo == 'bar'\n   && baz == 'qux') { %>\n<div>ok</div>\n<% } %>\n";
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
    public void CHAR001_ignores_ampersand_inside_asp_blocks()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "CHAR-001");
        // && est du C# a l'interieur d'un bloc serveur : ne doit pas declencher.
        var content = "<% if (a && b) { %>ok<% } %>\n";
        var ctx = new RuleContext("aspx", "x.aspx");
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void CHAR001_ignores_ampersand_inside_multiline_asp_block()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "CHAR-001");
        // Bloc serveur multi-ligne (cas reel rencontre dans InfoPrix.ascx).
        var content = "<% if (Model.Foo != null\n   && Model.Bar != null\n   && Model.Baz) { %>\n<div>ok</div>\n<% } %>\n";
        var ctx = new RuleContext("aspx", "x.aspx");
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void TAG001_fix_does_not_break_input_with_asp_block_in_value()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "TAG-001");
        // Pattern reel rencontre dans Views/ : la regex naive prenait le `>`
        // de `%>` pour la fin du tag et inserait ` /` au milieu du bloc serveur.
        var content = "<input type=\"text\" value=\"<%= Model.Foo %>\">\n";
        var ctx = new RuleContext("ascx", "x.ascx");
        var fixed1 = rule.Fix(content, ctx)!;
        Assert.Contains("<%= Model.Foo %>", fixed1);   // bloc serveur intact
        Assert.Contains("<input ", fixed1);
        Assert.Contains(" />", fixed1);                // bien transforme en self-close
        Assert.DoesNotContain("% />", fixed1);         // PAS l'ancien bug
    }

    [Fact]
    public void TAG001_fix_is_idempotent_with_asp_blocks()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "TAG-001");
        var content = "<img src=\"<%= Model.Url %>\" alt=\"<%= Model.Alt %>\">\n";
        var ctx = new RuleContext("ascx", "x.ascx");
        var first = rule.Fix(content, ctx)!;
        var second = rule.Fix(first, ctx)!;
        Assert.Equal(first, second);
    }

    [Fact]
    public void FORM001_fix_does_not_break_form_with_asp_block_in_action()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "FORM-001");
        // Cas reel : action="/<%= X %>/sub" — sans masquage, runat="server"
        // etait insere AU MILIEU du bloc serveur.
        var content =
            "<%@ Page Language=\"C#\" %>\n" +
            "<asp:Label ID=\"x\" runat=\"server\" />\n" +
            "<form action=\"/<%= Model.Type %>/sub\" method=\"post\">x</form>\n";
        var ctx = new RuleContext("aspx", "x.aspx");
        var fixed1 = rule.Fix(content, ctx)!;
        Assert.Contains("<%= Model.Type %>", fixed1);                 // bloc serveur intact
        Assert.Contains("runat=\"server\"", fixed1);
        Assert.DoesNotContain("% runat=", fixed1);                    // PAS l'ancien bug
        Assert.Contains("action=\"/<%= Model.Type %>/sub\"", fixed1); // attr intact
    }

    [Fact]
    public void ASP001_fix_does_not_break_asp_control_with_asp_block_in_attr()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "ASP-001");
        // Bien que rare, un controle asp:* peut avoir un attribut interpole.
        var content = "<asp:Label Text=\"<%= Model.Lbl %>\" />\n";
        var ctx = new RuleContext("aspx", "x.aspx");
        var fixed1 = rule.Fix(content, ctx)!;
        Assert.Contains("<%= Model.Lbl %>", fixed1);
        Assert.Contains("runat=\"server\"", fixed1);
        Assert.DoesNotContain("% runat=", fixed1);
    }

    [Fact]
    public void TAG001_does_not_fix_input_inside_asp_block()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "TAG-001");
        // <input> dans une chaine C# d'un bloc serveur : doit etre laisse tranquille.
        var content = "<% Response.Write(\"<input type=text>\"); %>\n";
        var ctx = new RuleContext("aspx", "x.aspx");
        var fixed1 = rule.Fix(content, ctx)!;
        Assert.Equal(content, fixed1);
    }

    [Fact]
    public void ATTR003_fix_merges_duplicate_class()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "ATTR-003");
        // Cas reel : <a class="A" href="x" class="B"> doit devenir
        // <a class="A B" href="x"> avec dedupe des tokens.
        var content = "<a class=\"text-over\"  href=\"/x\" class=\"thumbnail-subhead\" data-x=\"y\">link</a>\n";
        var ctx = new RuleContext("ascx", "x.ascx");
        var fixed1 = rule.Fix(content, ctx)!;
        Assert.Contains("class=\"text-over thumbnail-subhead\"", fixed1);
        Assert.Single(Regex.Matches(fixed1, @"\bclass="));
        Assert.Contains("href=\"/x\"", fixed1);              // attr non touche
        Assert.Contains("data-x=\"y\"", fixed1);             // attr non touche
    }

    [Fact]
    public void ATTR003_fix_dedupes_class_tokens()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "ATTR-003");
        var content = "<div class=\"foo bar\" class=\"bar baz\">x</div>\n";
        var ctx = new RuleContext("ascx", "x.ascx");
        var fixed1 = rule.Fix(content, ctx)!;
        Assert.Contains("class=\"foo bar baz\"", fixed1);
    }

    [Fact]
    public void ATTR003_fix_keeps_first_for_non_class_attributes()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "ATTR-003");
        // Pour id, data-x, etc., on garde le premier (comportement HTML standard).
        var content = "<div id=\"a\" id=\"b\">x</div>\n";
        var ctx = new RuleContext("ascx", "x.ascx");
        var fixed1 = rule.Fix(content, ctx)!;
        Assert.Contains("id=\"a\"", fixed1);
        Assert.DoesNotContain("id=\"b\"", fixed1);
    }

    [Fact]
    public void ATTR003_fix_is_idempotent()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "ATTR-003");
        var content = "<a class=\"A\" class=\"B\">x</a>\n";
        var ctx = new RuleContext("ascx", "x.ascx");
        var first = rule.Fix(content, ctx)!;
        var second = rule.Fix(first, ctx)!;
        Assert.Equal(first, second);
    }

    [Fact]
    public void TAG003_fix_inserts_missing_closes_before_mismatched_close()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "TAG-003");
        // Cas Monitoring.Master simplifie : <body> <div> <div> ... </body> </html>
        // Le fix doit inserer </div></div> avant </body>.
        var content =
            "<html>\n" +
            "<body>\n" +
            "    <div class=\"container\">\n" +
            "    <div class=\"row\">\n" +
            "        <p>contenu</p>\n" +
            "    </body>\n" +
            "</html>\n";
        var ctx = new RuleContext("master", "x.master");
        var fixed1 = rule.Fix(content, ctx)!;
        Assert.Contains("</div>", fixed1);

        // Apres fix, les issues TAG-003 doivent disparaitre.
        var afterIssues = rule.Detect(fixed1, fixed1.Split('\n'), ctx).ToList();
        Assert.Empty(afterIssues);
    }

    [Fact]
    public void TAG003_fix_appends_closes_for_unclosed_tags_at_eof()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "TAG-003");
        var content = "<div><span>texte\n";
        var ctx = new RuleContext("ascx", "x.ascx");
        var fixed1 = rule.Fix(content, ctx)!;
        var afterIssues = rule.Detect(fixed1, fixed1.Split('\n'), ctx).ToList();
        Assert.Empty(afterIssues);
        Assert.Contains("</span>", fixed1);
        Assert.Contains("</div>", fixed1);
    }

    [Fact]
    public void TAG003_fix_does_not_touch_orphan_close()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "TAG-003");
        // </div> sans <div> ouvert : fix risque, on laisse manuel.
        var content = "<span>texte</span></div>\n";
        var ctx = new RuleContext("ascx", "x.ascx");
        var fixed1 = rule.Fix(content, ctx)!;
        Assert.Equal(content, fixed1);   // pas modifie
    }

    [Fact]
    public void TAG003_ignores_html_inside_server_comment_with_interpolation()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "TAG-003");
        // Cas reel : un commentaire <%-- --%> qui contient un <%= %> interpole.
        // L'ancien masking matchait jusqu'au premier `%>` du `<%=`, exposant le
        // reste du commentaire (HTML) qui generait des faux TAG-003.
        var content =
            "<div>\n" +
            "<%-- <span class=\"x\"><%= Model.Foo %></span></div> --%>\n" +
            "</div>\n";
        var ctx = new RuleContext("ascx", "x.ascx");
        var issues = rule.Detect(content, content.Split('\n'), ctx).ToList();
        Assert.Empty(issues);
    }

    [Fact]
    public void TAG002_ignores_csharp_generic_in_directive()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "TAG-002");
        // Directive @Control avec un generic C# : <Generic> ne doit pas etre traite
        // comme une balise HTML <Generic> en mauvaise casse.
        var content = "<%@ Control Inherits=\"System.Web.Mvc.ViewUserControl<MyType>\" %>\n";
        var ctx = new RuleContext("ascx", "x.ascx");
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
