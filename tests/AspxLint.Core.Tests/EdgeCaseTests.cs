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
    public void CHAR001_ignores_csharp_in_multiline_asp_block()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "CHAR-001");
        // Cas reel rencontre dans Demande/Index.aspx : un <% %> qui s'etend sur
        // ~18 lignes avec du C# qui contient && et &. Avant le fix de
        // MaskAspBlocks (preservation des newlines), les lignes apres la ligne 1
        // du block etaient decalees a l'index, et CHAR-001 detectait les `&&`
        // comme du HTML.
        var content =
            "<asp:Content runat=\"server\">\n" +
            "<%\n" +
            "    string a = \"x\";\n" +
            "    if (m != null && m.Foo == 1)\n" +
            "    {\n" +
            "        if (Model.Voyage.Form != null && (Model.IsDevis || (Model.ProductObj != null && !Model.ProductObj.aerienInclus)))\n" +
            "        {\n" +
            "            a = \"y\";\n" +
            "        }\n" +
            "    }\n" +
            "%>\n" +
            "</asp:Content>\n";
        var ctx = new RuleContext("aspx", "x.aspx");
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void CHAR001_ignores_ampersand_in_url_query_params()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "CHAR-001");
        // Cas reel rencontre dans Marker_Keyade.ascx : src d'un img tracker
        // avec plusieurs `&` separateurs de query string. HTML5-compatible.
        var content = "<img src=\"https://k.example.com/?a=1&b=2&kaClkId=42&kaEvSt=ok\" />\n";
        var ctx = new RuleContext("aspx", "x.aspx");
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void CHAR001_ignores_url_params_with_hyphens()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "CHAR-001");
        // Cas reel : `&item-url=...` dans un tracking pixel taboola.
        var content = "<img src=\"http://x.com/log?marking-type=retargeting&item-url=http://k.com\" />\n";
        var ctx = new RuleContext("aspx", "x.aspx");
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void CHAR001_still_fires_on_ampersand_in_text_content()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "CHAR-001");
        // Le cas legitime : du texte HTML avec un `&` non encode.
        var content = "<p>Tom & Jerry</p>\n";
        var ctx = new RuleContext("aspx", "x.aspx");
        var issues = rule.Detect(content, content.Split('\n'), ctx).ToList();
        Assert.Single(issues);
    }

    [Fact]
    public void CHAR001_ignores_ampersand_inside_script_block()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "CHAR-001");
        // Cas reel rencontre dans Demande/Index.aspx : du JS dans <script type="text/plain">
        // contient des `s && s !== "x"`. Ces `&&` sont du JS, pas du HTML.
        var content =
            "<div>\n" +
            "<script type=\"text/plain\" data-cookieconsent=\"marketing\">\n" +
            "  function f(s) { return s && s !== \"loaded\" && s !== \"complete\"; }\n" +
            "</script>\n" +
            "</div>\n";
        var ctx = new RuleContext("aspx", "x.aspx");
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void CHAR001_ignores_ampersand_inside_style_block()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "CHAR-001");
        var content =
            "<div>\n" +
            "<style>\n" +
            "  /* comment with & in it */\n" +
            "  a:hover & .x { color: red; }\n" +
            "</style>\n" +
            "</div>\n";
        var ctx = new RuleContext("aspx", "x.aspx");
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void TAG002_ignores_html_inside_multiline_asp_block()
    {
        // Sanity check supplementaire : avant le fix de MaskAspBlocks, les
        // newlines internes etaient ecrases en espaces, et tout l'indexage
        // de lignes apres un bloc multi-ligne devenait fausse.
        var rule = RuleRegistry.All.Single(r => r.Id == "TAG-002");
        var content =
            "<%\n" +
            "    string s = \"<DIV>some HTML in C# string</DIV>\";\n" +
            "    int x = 1 + 2;\n" +
            "%>\n" +
            "<div>real html</div>\n";
        var ctx = new RuleContext("aspx", "x.aspx");
        // Le <DIV> dans la chaine C# ne doit PAS etre detecte. Le <div> hors-bloc
        // est en minuscule donc ne tire pas non plus.
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void CustomRule_basic_pattern_match()
    {
        var rule = new AspxLint.Core.Rules.CustomRule(
            "CUSTOM-TEST", "Pas de TODO",
            Severity.Warning, "...", @"TODO[: ]", "Resoudre.");
        var content = "<%-- TODO: x --%>\n<p>nothing</p>\nTODO new\n";
        var ctx = new RuleContext("aspx", "x.aspx");
        // Le commentaire ASP est masque par defaut, donc seule la 3e ligne fire.
        var issues = rule.Detect(content, content.Split('\n'), ctx).ToList();
        Assert.Single(issues);
        Assert.Equal(3, issues[0].Line);
    }

    [Fact]
    public void CustomRule_ignoreCase_works()
    {
        var rule = new AspxLint.Core.Rules.CustomRule(
            "CUSTOM-CASE", "Mots-cles", Severity.Info, "...",
            @"WIP", "Finir.", ignoreCase: true);
        var content = "<p>wip</p>\n<p>WIP</p>\n<p>Wip</p>\n";
        var issues = rule.Detect(content, content.Split('\n'), new RuleContext("aspx", "x.aspx")).ToList();
        Assert.Equal(3, issues.Count);
    }

    [Fact]
    public void CustomRule_maskAspBlocks_false_scans_inside_blocks()
    {
        var rule = new AspxLint.Core.Rules.CustomRule(
            "CUSTOM-NOMASK", "Magic numbers", Severity.Info, "...",
            @"\b9999\b", "Constante.", maskAspBlocks: false);
        var content = "<%= 9999 %>\n";
        var issues = rule.Detect(content, content.Split('\n'), new RuleContext("aspx", "x.aspx")).ToList();
        Assert.Single(issues);
    }

    [Fact]
    public void Config_resolveRules_appends_custom_after_builtin()
    {
        var config = new AspxLintConfig
        {
            CustomRules =
            {
                new CustomRuleDefinition { Id = "CUSTOM-X", Pattern = "X", Severity = "info" },
                new CustomRuleDefinition { Id = "CUSTOM-Y", Pattern = "Y", Severity = "error" }
            }
        };
        var result = config.ResolveRules(RuleRegistry.All).ToList();
        Assert.Equal(RuleRegistry.All.Count + 2, result.Count);
        Assert.Equal("CUSTOM-X", result[^2].Id);
        Assert.Equal("CUSTOM-Y", result[^1].Id);
    }

    [Fact]
    public void Translations_returns_french_for_default_locale()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "TAG-001");
        var (name, desc) = Translations.Resolve(rule, null);
        Assert.Equal(rule.Name, name);
        Assert.Equal(rule.Description, desc);
    }

    [Fact]
    public void Translations_returns_english_for_en_locale()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "TAG-001");
        var (name, _) = Translations.Resolve(rule, "en");
        Assert.NotEqual(rule.Name, name);
        Assert.Contains("XHTML", name);
    }

    [Fact]
    public void Translations_falls_back_to_source_for_unknown_locale()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "WS-001");
        var (name, _) = Translations.Resolve(rule, "es");
        Assert.Equal(rule.Name, name);
    }

    [Fact]
    public void Translations_covers_all_29_rules_in_english()
    {
        // Garde-fou : si on ajoute une regle, on doit ajouter sa traduction EN
        // sinon les utilisateurs --lang en voient un mix FR/EN.
        var missing = RuleRegistry.All
            .Where(r => Translations.Resolve(r, "en").Name == r.Name)
            .Select(r => r.Id)
            .ToList();
        Assert.True(missing.Count == 0,
            "Traductions EN manquantes pour : " + string.Join(", ", missing));
    }

    [Fact]
    public void ScanIncremental_returns_cached_result_when_hash_unchanged()
    {
        using var tmp = new TempDir();
        tmp.WriteFile("a.ascx", "<p>x</p>\n");
        var cache = new ProjectScanner.IncrementalCache();

        var (run1, n1) = ProjectScanner.ScanIncremental(tmp.Path, RuleRegistry.All, cache);
        Assert.Equal(1, n1);   // 1 fichier re-analyse
        Assert.Single(run1);

        var (run2, n2) = ProjectScanner.ScanIncremental(tmp.Path, RuleRegistry.All, cache);
        Assert.Equal(0, n2);   // hash inchange -> 0 re-analyses
        Assert.Single(run2);
        Assert.Same(run1[0], run2[0]);   // meme reference, recuperee du cache
    }

    [Fact]
    public void ScanIncremental_reanalyzes_after_file_change()
    {
        using var tmp = new TempDir();
        tmp.WriteFile("a.ascx", "<p>x</p>\n");
        var cache = new ProjectScanner.IncrementalCache();

        ProjectScanner.ScanIncremental(tmp.Path, RuleRegistry.All, cache);
        // Modifie le fichier
        tmp.WriteFile("a.ascx", "<p>y</p>\n");
        var (run2, n2) = ProjectScanner.ScanIncremental(tmp.Path, RuleRegistry.All, cache);
        Assert.Equal(1, n2);
    }

    [Fact]
    public void ScanIncremental_drops_cache_for_deleted_files()
    {
        using var tmp = new TempDir();
        tmp.WriteFile("a.ascx", "<p>x</p>\n");
        tmp.WriteFile("b.ascx", "<p>y</p>\n");
        var cache = new ProjectScanner.IncrementalCache();

        var (run1, _) = ProjectScanner.ScanIncremental(tmp.Path, RuleRegistry.All, cache);
        Assert.Equal(2, run1.Count);
        Assert.Equal(2, cache.Count);

        File.Delete(Path.Combine(tmp.Path, "b.ascx"));
        var (run2, _) = ProjectScanner.ScanIncremental(tmp.Path, RuleRegistry.All, cache);
        Assert.Single(run2);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void IssueFilter_disable_line_ignores_next_line()
    {
        var content =
            "<%-- aspx-lint disable WS-001 --%>\n" +
            "<p>x   \n" +    // trailing whitespace -> WS-001 normally fires
            "<p>y   \n";     // this one still fires
        var issues = Analyzer.Analyze("x.aspx", content, RuleRegistry.All);
        var ws1 = issues.Where(i => i.RuleId == "WS-001").ToList();
        Assert.Single(ws1);
        Assert.Equal(3, ws1[0].Line);   // ligne 2 disabled, ligne 3 retenue
    }

    [Fact]
    public void IssueFilter_disable_file_ignores_whole_file()
    {
        var content =
            "<%-- aspx-lint disable-file WS-001 --%>\n" +
            "<p>x   \n" +
            "<p>y   \n";
        var issues = Analyzer.Analyze("x.aspx", content, RuleRegistry.All);
        Assert.Empty(issues.Where(i => i.RuleId == "WS-001"));
    }

    [Fact]
    public void IssueFilter_disable_file_without_rule_disables_all()
    {
        var content =
            "<%-- aspx-lint disable-file --%>\n" +
            "<p>x   \n" +
            "<DIV>caps</DIV>\n";
        var issues = Analyzer.Analyze("x.aspx", content, RuleRegistry.All);
        Assert.Empty(issues);
    }

    [Fact]
    public void IssueFilter_html_comment_syntax_works()
    {
        // Le marker peut etre dans un commentaire HTML <!-- --> aussi.
        var content =
            "<!-- aspx-lint disable-file WS-001 -->\n" +
            "<p>x   \n";
        var issues = Analyzer.Analyze("x.aspx", content, RuleRegistry.All);
        Assert.Empty(issues.Where(i => i.RuleId == "WS-001"));
    }

    [Fact]
    public void Config_off_disables_rule_completely()
    {
        var config = new AspxLintConfig { Rules = { ["WS-001"] = "off" } };
        var content = "<p>x   \n";
        var issues = Analyzer.Analyze("x.aspx", content, RuleRegistry.All, config);
        Assert.Empty(issues.Where(i => i.RuleId == "WS-001"));
    }

    [Fact]
    public void Config_severity_override_changes_reported_severity()
    {
        var config = new AspxLintConfig { Rules = { ["WS-001"] = "error" } };
        var content = "<p>x   \n";
        var issues = Analyzer.Analyze("x.aspx", content, RuleRegistry.All, config);
        var ws1 = issues.Where(i => i.RuleId == "WS-001").ToList();
        Assert.NotEmpty(ws1);
        Assert.Equal(Severity.Error, ws1[0].Severity);
    }

    [Fact]
    public void SEC002_detects_target_blank_without_rel()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "SEC-002");
        var content = "<a href=\"https://x.com\" target=\"_blank\">x</a>\n";
        var ctx = new RuleContext("ascx", "x.ascx");
        Assert.Single(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void SEC002_skips_when_noopener_present()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "SEC-002");
        var content = "<a href=\"https://x.com\" target=\"_blank\" rel=\"noopener\">x</a>\n";
        var ctx = new RuleContext("ascx", "x.ascx");
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void SEC002_fix_appends_rel_when_missing()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "SEC-002");
        var content = "<a href=\"https://x.com\" target=\"_blank\">x</a>\n";
        var ctx = new RuleContext("ascx", "x.ascx");
        var fixed1 = rule.Fix(content, ctx)!;
        Assert.Contains("rel=\"noopener noreferrer\"", fixed1);
    }

    [Fact]
    public void SEC002_fix_merges_into_existing_rel()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "SEC-002");
        var content = "<a href=\"x\" target=\"_blank\" rel=\"author\">x</a>\n";
        var ctx = new RuleContext("ascx", "x.ascx");
        var fixed1 = rule.Fix(content, ctx)!;
        Assert.Contains("rel=\"author noopener noreferrer\"", fixed1);
    }

    [Fact]
    public void A11Y001_detects_img_without_alt()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "A11Y-001");
        var content = "<img src=\"x.png\" />\n";
        var ctx = new RuleContext("ascx", "x.ascx");
        Assert.Single(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void A11Y001_skips_when_alt_present_even_empty()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "A11Y-001");
        var content = "<img src=\"x.png\" alt=\"\" />\n<img src=\"y.png\" alt=\"y\" />\n";
        var ctx = new RuleContext("ascx", "x.ascx");
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void SEC003_detects_localhost()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "SEC-003");
        var content = "<a href=\"http://localhost:5000/api\">x</a>\n";
        var ctx = new RuleContext("ascx", "x.ascx");
        Assert.Single(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void SEC003_detects_ip_loopback_and_local_tld()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "SEC-003");
        var c1 = "<img src=\"https://127.0.0.1/x\" />\n";
        var c2 = "<a href=\"https://api.local/v1\">x</a>\n";
        var ctx = new RuleContext("ascx", "x.ascx");
        Assert.Single(rule.Detect(c1, c1.Split('\n'), ctx));
        Assert.Single(rule.Detect(c2, c2.Split('\n'), ctx));
    }

    [Fact]
    public void SEC003_skips_production_urls()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "SEC-003");
        var content = "<a href=\"https://www.example.com/x\">x</a>\n";
        var ctx = new RuleContext("ascx", "x.ascx");
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void STYLE001_detects_inline_style()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "STYLE-001");
        var content = "<div style=\"color:red;display:none\">x</div>\n";
        var ctx = new RuleContext("ascx", "x.ascx");
        Assert.Single(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void STYLE001_does_not_match_data_style_attr()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "STYLE-001");
        var content = "<div data-style=\"x\">y</div>\n";
        var ctx = new RuleContext("ascx", "x.ascx");
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void SCRIPT001_detects_inline_event_handlers()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "SCRIPT-001");
        var content =
            "<button onclick=\"f()\">a</button>\n" +
            "<input onchange=\"g(this)\" />\n";
        var ctx = new RuleContext("ascx", "x.ascx");
        var issues = rule.Detect(content, content.Split('\n'), ctx).ToList();
        Assert.Equal(2, issues.Count);
    }

    [Fact]
    public void SCRIPT001_does_not_match_aspx_dataonly_attribute()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "SCRIPT-001");
        // Custom attr that contains "on" prefix mais qui n'est pas un handler standard.
        var content = "<input data-onclick-target=\"#x\" />\n";
        var ctx = new RuleContext("ascx", "x.ascx");
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void Config_glob_match_simple_patterns()
    {
        var c = new AspxLintConfig { Ignore = { "**/Generated/**", "*.bak" } };
        Assert.True(c.IsIgnored("Foo/Generated/x.aspx"));
        Assert.True(c.IsIgnored("a/b/Generated/c/d.aspx"));
        Assert.True(c.IsIgnored("file.bak"));
        Assert.False(c.IsIgnored("Foo/x.aspx"));
        Assert.False(c.IsIgnored("Generated.aspx"));   // pas de slash, pas un dossier
    }

    [Fact]
    public void WS006_detects_trailing_blank_lines()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "WS-006");
        // 3 newlines en fin = 2 lignes vides surnumeraires.
        var content = "<p>x</p>\n\n\n";
        var ctx = new RuleContext("ascx", "x.ascx");
        var issues = rule.Detect(content, content.Split('\n'), ctx).ToList();
        Assert.Single(issues);
    }

    [Fact]
    public void WS006_does_not_fire_on_single_final_newline()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "WS-006");
        var content = "<p>x</p>\n";
        var ctx = new RuleContext("ascx", "x.ascx");
        Assert.Empty(rule.Detect(content, content.Split('\n'), ctx));
    }

    [Fact]
    public void WS006_fix_collapses_trailing_blanks_to_one_newline()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "WS-006");
        var content = "<p>x</p>\n\n\n  \n";
        var ctx = new RuleContext("ascx", "x.ascx");
        var fixed1 = rule.Fix(content, ctx)!;
        Assert.Equal("<p>x</p>\n", fixed1);
    }

    [Fact]
    public void WS006_fix_is_idempotent()
    {
        var rule = RuleRegistry.All.Single(r => r.Id == "WS-006");
        var content = "<p>x</p>\n\n\n";
        var ctx = new RuleContext("ascx", "x.ascx");
        var first = rule.Fix(content, ctx)!;
        var second = rule.Fix(first, ctx)!;
        Assert.Equal(first, second);
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
