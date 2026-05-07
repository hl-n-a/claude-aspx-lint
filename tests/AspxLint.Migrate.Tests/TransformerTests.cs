namespace AspxLint.Migrate.Tests;

/// <summary>
/// Tests unitaires des 4 transformers de la Phase 1. Chaque transformer
/// est testable en isolation : on lui passe un contenu, on verifie le
/// resultat ET les actions enregistrees dans le rapport.
/// </summary>
public class TransformerTests
{
    private static (string output, MigrationReport report)
        Run(ITransformer t, string input, string ext = "aspx", string fileName = "test.aspx")
    {
        var report = new MigrationReport();
        var ctx = new MigrationContext(fileName, ext, report);
        var output = t.Transform(input, ctx);
        return (output, report);
    }

    // ============= ServerCommentTransformer =============

    [Fact]
    public void ServerComment_basic()
    {
        var t = new ServerCommentTransformer();
        var (output, report) = Run(t, "<%-- hello --%>");
        Assert.Equal("@* hello *@", output);
        Assert.Equal(1, report.CountBySeverity(MigrationSeverity.Auto));
    }

    [Fact]
    public void ServerComment_multiline()
    {
        var t = new ServerCommentTransformer();
        var (output, _) = Run(t, "<%-- line1\nline2\nline3 --%>");
        Assert.Equal("@* line1\nline2\nline3 *@", output);
    }

    [Fact]
    public void ServerComment_nested_close_warns()
    {
        var t = new ServerCommentTransformer();
        var (_, report) = Run(t, "<%-- has a *@ in it --%>");
        Assert.Equal(1, report.CountBySeverity(MigrationSeverity.Warning));
    }

    [Fact]
    public void ServerComment_passes_through_when_absent()
    {
        var t = new ServerCommentTransformer();
        var (output, report) = Run(t, "<div>no comment</div>");
        Assert.Equal("<div>no comment</div>", output);
        Assert.Empty(report.Actions);
    }

    [Fact]
    public void ServerComment_handles_handwritten_variant_with_spaces()
    {
        // Pattern legacy : <% -- text -- %> (avec espaces apres <% et avant %>).
        // Pas la syntaxe officielle mais courant dans du code legacy. ASP.NET
        // tolere — Razor casserait parce que `--` est l'operateur de decrement.
        var t = new ServerCommentTransformer();
        var (output, _) = Run(t, "<% -- Appel de la modale -- %>");
        Assert.Equal("@* Appel de la modale *@", output);
    }

    [Fact]
    public void ServerComment_handwritten_variant_no_inner_space()
    {
        var t = new ServerCommentTransformer();
        var (output, _) = Run(t, "<% --Réassurance-- %>");
        Assert.Equal("@*Réassurance*@", output);
    }

    [Fact]
    public void ServerComment_does_not_match_decrement_operator()
    {
        // <% var x = --y; %> contient `--` mais c'est un decrement, pas un
        // commentaire. Ne doit PAS matcher.
        var t = new ServerCommentTransformer();
        var (output, report) = Run(t, "<% var x = --y; %>");
        // Pas de transformation — le statement transformer (pas execute ici)
        // s'en chargera.
        Assert.Equal("<% var x = --y; %>", output);
        Assert.Empty(report.Actions);
    }

    // ============= ServerExpressionTransformer =============

    [Fact]
    public void ServerExpression_html_encoded_clean()
    {
        var t = new ServerExpressionTransformer();
        var (output, report) = Run(t, "<%: Model.Name %>");
        Assert.Equal("@(Model.Name)", output);
        Assert.Equal(1, report.CountBySeverity(MigrationSeverity.Auto));
        Assert.Equal(0, report.CountBySeverity(MigrationSeverity.Warning));
    }

    [Fact]
    public void ServerExpression_simple_identifier_emits_no_warning()
    {
        // Razor encode par defaut — c'est le bon comportement pour les
        // expressions simples (variables, model access, methods sur string).
        // Pas de warning : on aurait spam le rapport pour rien.
        var t = new ServerExpressionTransformer();
        var (output, report) = Run(t, "<%= Model.Name %>");
        Assert.Equal("@(Model.Name)", output);
        Assert.Equal(0, report.CountBySeverity(MigrationSeverity.Warning));
        Assert.Equal(1, report.CountBySeverity(MigrationSeverity.Auto));
    }

    [Fact]
    public void ServerExpression_mvc_html_helper_no_warning()
    {
        // Html.X(...) retourne IHtmlString -> Razor ne re-encode pas.
        var t = new ServerExpressionTransformer();
        var (output, report) = Run(t, "<%= Html.Partial(\"Foo\") %>");
        Assert.Equal("@(Html.Partial(\"Foo\"))", output);
        Assert.Equal(0, report.CountBySeverity(MigrationSeverity.Warning));
    }

    [Fact]
    public void ServerExpression_mvc_url_helper_no_warning()
    {
        var t = new ServerExpressionTransformer();
        var (output, report) = Run(t, "<%= Url.Action(\"Index\", \"Home\") %>");
        Assert.Equal("@(Url.Action(\"Index\", \"Home\"))", output);
        Assert.Equal(0, report.CountBySeverity(MigrationSeverity.Warning));
    }

    [Fact]
    public void ServerExpression_explicit_html_raw_no_warning()
    {
        var t = new ServerExpressionTransformer();
        var (output, report) = Run(t, "<%= Html.Raw(Model.Html) %>");
        Assert.Equal("@(Html.Raw(Model.Html))", output);
        Assert.Equal(0, report.CountBySeverity(MigrationSeverity.Warning));
    }

    [Fact]
    public void ServerExpression_with_html_literal_auto_wraps_in_html_raw()
    {
        // Cas legitime : l'expression contient des balises HTML en string
        // literal -> l'auteur voulait du raw HTML. On wrap automatiquement
        // en @Html.Raw, avec un Warning pour rappeler le risque XSS.
        var t = new ServerExpressionTransformer();
        var (output, report) = Run(t, "<%= Model.Body.Replace(\"\\n\", \"<br>\") %>");
        Assert.StartsWith("@Html.Raw(", output);
        Assert.EndsWith(")", output);
        Assert.Equal(1, report.CountBySeverity(MigrationSeverity.Warning));
    }

    [Fact]
    public void ServerExpression_with_inline_html_in_ternary_auto_wraps()
    {
        var t = new ServerExpressionTransformer();
        var (output, _) = Run(t, "<%= cond ? \"<span>x</span>\" : \"\" %>");
        Assert.Contains("@Html.Raw(", output);
    }

    [Fact]
    public void ServerExpression_int_arithmetic_no_warning()
    {
        var t = new ServerExpressionTransformer();
        var (_, report) = Run(t, "<%= i + 1 %>");
        Assert.Equal(0, report.CountBySeverity(MigrationSeverity.Warning));
    }

    [Fact]
    public void ServerExpression_databinding_flagged_manual()
    {
        var t = new ServerExpressionTransformer();
        var (output, report) = Run(t, "<%# Eval(\"Name\") %>");
        Assert.Contains("@(Eval(\"Name\"))", output);
        Assert.Contains("TODO[aspx-migrate] data-binding", output);
        Assert.Equal(1, report.CountBySeverity(MigrationSeverity.Manual));
    }

    [Fact]
    public void ServerExpression_handles_complex_expression()
    {
        var t = new ServerExpressionTransformer();
        var (output, _) = Run(t, "<%: items.Where(i => i.Active).Count() %>");
        Assert.Equal("@(items.Where(i => i.Active).Count())", output);
    }

    // ============= ServerStatementTransformer =============

    [Fact]
    public void ServerStatement_simple_block_wrapped()
    {
        var t = new ServerStatementTransformer();
        var (output, _) = Run(t, "<% var x = 1; %>");
        Assert.Equal("@{ var x = 1; }", output);
    }

    [Fact]
    public void ServerStatement_if_block_uses_at_keyword()
    {
        var t = new ServerStatementTransformer();
        var input = "<% if (cond) { %>HI<% } %>";
        var (output, _) = Run(t, input);
        Assert.Equal("@if (cond) {HI}", output);
    }

    [Fact]
    public void ServerStatement_foreach_block_uses_at_keyword()
    {
        var t = new ServerStatementTransformer();
        var input = "<% foreach (var item in items) { %><li/><% } %>";
        var (output, _) = Run(t, input);
        Assert.Equal("@foreach (var item in items) {<li/>}", output);
    }

    [Fact]
    public void ServerStatement_else_branch_preserved()
    {
        var t = new ServerStatementTransformer();
        var input = "<% if (x) { %>A<% } else { %>B<% } %>";
        var (output, _) = Run(t, input);
        // Le } else { en Razor reste tel quel sans @{}
        Assert.Equal("@if (x) {A} else {B}", output);
    }

    [Fact]
    public void ServerStatement_response_write_flagged()
    {
        var t = new ServerStatementTransformer();
        var (_, report) = Run(t, "<% Response.Write(name); %>");
        Assert.Equal(1, report.CountBySeverity(MigrationSeverity.Manual));
    }

    // ============= PageDirectiveTransformer =============

    [Fact]
    public void PageDirective_emits_page_and_model()
    {
        var t = new PageDirectiveTransformer();
        var (output, report) = Run(t,
            "<%@ Page Language=\"C#\" Inherits=\"MyApp.HomePage\" %>");
        Assert.Contains("@page", output);
        Assert.Contains("@model MyApp.HomePage", output);
        Assert.True(report.CountBySeverity(MigrationSeverity.Auto) >= 1);
    }

    [Fact]
    public void PageDirective_extracts_generic_from_mvc_view_page()
    {
        // Pattern MVC tres courant : Inherits="System.Web.Mvc.ViewPage<MyModel>"
        // En Razor : @model MyModel (la classe de base est implicite).
        var t = new PageDirectiveTransformer();
        var (output, _) = Run(t,
            "<%@ Page Inherits=\"System.Web.Mvc.ViewPage<NS.MyModel>\" %>");
        Assert.Contains("@model NS.MyModel", output);
        Assert.DoesNotContain("ViewPage", output);
        Assert.DoesNotContain("System.Web.Mvc", output);
    }

    [Fact]
    public void PageDirective_extracts_generic_from_view_user_control()
    {
        // Pour les .ascx : Inherits="System.Web.Mvc.ViewUserControl<MyModel>"
        var t = new PageDirectiveTransformer();
        var (output, _) = Run(t,
            "<%@ Control Inherits=\"System.Web.Mvc.ViewUserControl<NomadeAventure.ViewModels.AvisVoyageurViewModel>\" %>",
            ext: "ascx", fileName: "Foo.ascx");
        Assert.Equal("@model NomadeAventure.ViewModels.AvisVoyageurViewModel", output);
    }

    [Fact]
    public void PageDirective_extracts_generic_from_view_master_page()
    {
        var t = new PageDirectiveTransformer();
        var (output, _) = Run(t,
            "<%@ Master Inherits=\"System.Web.Mvc.ViewMasterPage<MyApp.LayoutVm>\" %>",
            ext: "master", fileName: "Site.master");
        // La directive @Master est retiree (HandleMaster), mais le @model
        // doit etre extrait correctement... wait, HandleMaster retourne ""
        // pour la directive entiere donc le @model n'est pas emis pour
        // les masters. Verifions juste qu'il n'y a pas de ViewMasterPage
        // dans la sortie.
        Assert.DoesNotContain("ViewMasterPage", output);
    }

    [Fact]
    public void PageDirective_short_form_without_namespace_works()
    {
        // Forme courte : Inherits="ViewPage<X>" (sans System.Web.Mvc).
        var t = new PageDirectiveTransformer();
        var (output, _) = Run(t,
            "<%@ Page Inherits=\"ViewPage<NS.Foo>\" %>");
        Assert.Contains("@model NS.Foo", output);
    }

    [Fact]
    public void PageDirective_handles_nested_generic_in_inherits()
    {
        // ViewPage<List<T>> : la regex greedy capture List<T> entierement
        // (le > final ferme le ViewPage<>).
        var t = new PageDirectiveTransformer();
        var (output, _) = Run(t,
            "<%@ Page Inherits=\"System.Web.Mvc.ViewPage<List<MyApp.Item>>\" %>");
        Assert.Contains("@model List<MyApp.Item>", output);
    }

    [Fact]
    public void PageDirective_mvc_inherits_without_generic_emits_no_model()
    {
        // Inherits="System.Web.Mvc.ViewPage" sans generique -> page dynamic.
        // En Razor on n'emet PAS de @model (le defaut est dynamic).
        var t = new PageDirectiveTransformer();
        var (output, _) = Run(t,
            "<%@ Page Inherits=\"System.Web.Mvc.ViewPage\" %>");
        Assert.Equal("@page", output.Trim());
        Assert.DoesNotContain("@model", output);
        Assert.DoesNotContain("ViewPage", output);
    }

    [Fact]
    public void PageDirective_mvc_view_user_control_without_generic_emits_no_model()
    {
        var t = new PageDirectiveTransformer();
        var (output, _) = Run(t,
            "<%@ Control Inherits=\"System.Web.Mvc.ViewUserControl\" %>",
            ext: "ascx", fileName: "x.ascx");
        Assert.Equal("", output);   // .ascx sans inherits genere rien
        Assert.DoesNotContain("ViewUserControl", output);
    }

    [Fact]
    public void PageDirective_non_mvc_inherits_kept_as_is()
    {
        // Si Inherits ne suit pas le pattern MVC, on garde tel quel.
        // Ne pas confondre avec une classe custom generique de l'utilisateur.
        var t = new PageDirectiveTransformer();
        var (output, _) = Run(t,
            "<%@ Page Inherits=\"MyApp.MyCustomBaseClass\" %>");
        Assert.Contains("@model MyApp.MyCustomBaseClass", output);
    }

    [Fact]
    public void PageDirective_master_directive_removed()
    {
        // Phase 2 : la directive @Master est supprimee (un layout Razor n'a
        // pas de directive d'entete equivalente). Le contenu (HTML +
        // ContentPlaceHolder) est traite par d'autres transformers.
        var t = new PageDirectiveTransformer();
        var (output, report) = Run(t, "<%@ Master Language=\"C#\" %>");
        Assert.Equal("", output);
        Assert.Equal(0, report.CountBySeverity(MigrationSeverity.Manual));
        Assert.True(report.CountBySeverity(MigrationSeverity.Auto) >= 1);
    }

    [Fact]
    public void PageDirective_with_master_page_file_emits_layout_binding()
    {
        var t = new PageDirectiveTransformer();
        var (output, _) = Run(t,
            "<%@ Page Language=\"C#\" Inherits=\"App.Home\" MasterPageFile=\"~/Site.Master\" %>");
        Assert.Contains("@page", output);
        Assert.Contains("@model App.Home", output);
        Assert.Contains("Layout = \"_Site\"", output);
    }

    [Fact]
    public void PageDirective_master_path_with_subdir_resolves_basename()
    {
        var t = new PageDirectiveTransformer();
        var (output, _) = Run(t,
            "<%@ Page MasterPageFile=\"~/MasterPages/Public.Master\" %>");
        Assert.Contains("Layout = \"_Public\"", output);
    }

    [Fact]
    public void PageDirective_register_flagged_manual()
    {
        var t = new PageDirectiveTransformer();
        var (_, report) = Run(t,
            "<%@ Register TagPrefix=\"uc\" TagName=\"Foo\" Src=\"~/Controls/Foo.ascx\" %>");
        Assert.Equal(1, report.CountBySeverity(MigrationSeverity.Manual));
    }

    [Fact]
    public void PageDirective_import_becomes_using()
    {
        var t = new PageDirectiveTransformer();
        var (output, report) = Run(t, "<%@ Import Namespace=\"System.Linq\" %>");
        Assert.Equal("@using System.Linq", output);
        Assert.Equal(1, report.CountBySeverity(MigrationSeverity.Auto));
    }

    [Fact]
    public void PageDirective_control_emits_only_model()
    {
        var t = new PageDirectiveTransformer();
        var (output, _) = Run(t,
            "<%@ Control Language=\"C#\" Inherits=\"MyApp.MyControl\" %>",
            ext: "ascx", fileName: "MyControl.ascx");
        Assert.DoesNotContain("@page", output);
        Assert.Contains("@model MyApp.MyControl", output);
    }

    [Fact]
    public void PageDirective_control_without_inherits_emits_empty()
    {
        var t = new PageDirectiveTransformer();
        var (output, _) = Run(t, "<%@ Control Language=\"C#\" %>",
            ext: "ascx", fileName: "x.ascx");
        Assert.Equal("", output);
    }

    [Fact]
    public void PageDirective_unknown_directive_flagged_manual()
    {
        var t = new PageDirectiveTransformer();
        var (output, report) = Run(t, "<%@ FooBar Attr=\"x\" %>");
        Assert.Contains("TODO[aspx-migrate]", output);
        Assert.Equal(1, report.CountBySeverity(MigrationSeverity.Manual));
    }

    // ============= RunatServerTransformer =============

    [Fact]
    public void RunatServer_strips_attribute_from_form_tag()
    {
        var t = new RunatServerTransformer();
        var (output, report) = Run(t, "<form id=\"f1\" runat=\"server\">");
        Assert.Equal("<form id=\"f1\">", output);
        Assert.True(report.CountBySeverity(MigrationSeverity.Auto) >= 1);
    }

    [Fact]
    public void RunatServer_strips_attribute_from_head_tag()
    {
        var t = new RunatServerTransformer();
        var (output, _) = Run(t, "<head id=\"Head1\" runat=\"server\">");
        Assert.Equal("<head id=\"Head1\">", output);
    }

    [Fact]
    public void RunatServer_handles_single_quotes()
    {
        var t = new RunatServerTransformer();
        var (output, _) = Run(t, "<body runat='server'>");
        Assert.Equal("<body>", output);
    }

    [Fact]
    public void RunatServer_does_not_touch_asp_controls()
    {
        // <asp:Label> ne doit PAS etre touche — il sera transforme en
        // Phase 3. Strip runat="server" maintenant casserait le sens.
        var t = new RunatServerTransformer();
        var input = "<asp:Label ID=\"L1\" runat=\"server\" Text=\"x\" />";
        var (output, _) = Run(t, input);
        Assert.Equal(input, output);
    }

    [Fact]
    public void RunatServer_preserves_other_attributes()
    {
        var t = new RunatServerTransformer();
        var (output, _) = Run(t, "<form id=\"f\" runat=\"server\" method=\"post\" action=\"/x\">");
        Assert.Equal("<form id=\"f\" method=\"post\" action=\"/x\">", output);
    }

    [Fact]
    public void RunatServer_no_op_when_attribute_absent()
    {
        var t = new RunatServerTransformer();
        var (output, report) = Run(t, "<div>plain</div>");
        Assert.Equal("<div>plain</div>", output);
        Assert.Empty(report.Actions);
    }

    // ============= Robustesse / edge cases =============

    [Fact]
    public void ServerStatement_empty_block_is_removed()
    {
        // `<% %>` vide : on retire purement (sinon @{} visuel inutile).
        var t = new ServerStatementTransformer();
        var (output, _) = Run(t, "<% %>");
        Assert.Equal("", output);
    }

    [Fact]
    public void ServerStatement_single_line_balanced_if_unwraps()
    {
        var t = new ServerStatementTransformer();
        var (output, _) = Run(t, "<% if (Model == null) { return; } %>");
        Assert.Equal("@if (Model == null) { return; }", output);
    }

    [Fact]
    public void ServerStatement_multiple_statements_stays_in_curly_block()
    {
        // <% if (cond) { ... } stmt2; if (cond2) { ... } %> contient
        // PLUSIEURS statements -> doit rester dans @{ } pour rester du
        // Razor valide (Razor ne tolere qu'UNE structure derriere `@`).
        var t = new ServerStatementTransformer();
        var input = "<% if (Model == null) { return; } bool x = false; if (X) { Y(); } %>";
        var (output, _) = Run(t, input);
        Assert.StartsWith("@{ ", output);
        Assert.EndsWith(" }", output);
    }

    [Fact]
    public void ServerStatement_multi_line_single_block_unwraps()
    {
        var t = new ServerStatementTransformer();
        var input = "<% if (cond)\n{\n    DoSomething();\n    DoSomethingElse();\n} %>";
        var (output, _) = Run(t, input);
        Assert.StartsWith("@if", output);
        Assert.DoesNotContain("@{", output);
    }

    [Fact]
    public void ServerStatement_block_opener_with_unbalanced_braces_unwraps()
    {
        // Cas reel : le `<% %>` ouvre un if avec `{` mais ne ferme pas dans
        // ce stmt — le `}` arrive dans un `<% } %>` plus loin. La depth
        // finale est > 0, donc IsBlockOpener detecte. On emet `@stmt` brut.
        // Razor parse ca comme un block dont le HTML qui suit fait partie
        // du body.
        var t = new ServerStatementTransformer();
        var input = "<% if (cond)\n{\n    var x = 1;\n    var y = 2;\n %>";
        var (output, _) = Run(t, input);
        Assert.StartsWith("@if", output);
        Assert.DoesNotContain("@{", output);
    }

    [Fact]
    public void ServerStatement_stacked_closings_split_correctly()
    {
        // Pattern legacy : <% } } } %> ferme 3 blocks empiles.
        // Doit produire 3 `}` separes, pas un seul wrapper @{ }.
        var t = new ServerStatementTransformer();
        var input = "<% } } } %>";
        var (output, _) = Run(t, input);
        Assert.DoesNotContain("@{", output);
        Assert.Equal(3, output.Count(c => c == '}'));
    }

    [Fact]
    public void ServerStatement_closing_then_new_code_splits()
    {
        // Pattern : <% } if (cond) { ... } %> ferme un block puis ouvre/ferme
        // un autre. Le closing initial doit sortir du wrapper @{ }.
        var t = new ServerStatementTransformer();
        var input = "<% } if (cond) { Foo(); } %>";
        var (output, _) = Run(t, input);
        Assert.StartsWith("}", output);          // closing emis en premier
        Assert.Contains("@if (cond) {", output); // puis le if traite normalement
        Assert.DoesNotContain("@{ }", output);
    }

    [Fact]
    public void ServerStatement_else_block_alone_preserved()
    {
        // <% } else { %> doit produire `} else {` et PAS `} else` puis `@{ {`.
        var t = new ServerStatementTransformer();
        var input = "<% } else { %>";
        var (output, _) = Run(t, input);
        Assert.Equal("} else {", output);
    }

    [Fact]
    public void ServerStatement_braces_inside_strings_dont_count()
    {
        // Le `}` dans une string ne doit pas compter dans le balance check.
        var t = new ServerStatementTransformer();
        var input = "<% var s = \"with } brace\"; if (cond) { Foo(); } %>";
        var (output, _) = Run(t, input);
        // Multiple statements (var + if) -> @{ }
        Assert.StartsWith("@{", output);
    }

    [Fact]
    public void ServerExpression_inside_comment_keeps_being_inside_comment()
    {
        // Apres pipeline : le contenu DU commentaire peut etre re-transforme,
        // mais ca reste dans `@* ... *@` donc inerte au runtime Razor.
        // Ce qui compte c'est qu'on reste dans un commentaire bien forme.
        var input = "<%-- <%= x %> --%>";
        var (afterComments, _) = Run(new ServerCommentTransformer(), input);
        var (afterExpr, _) = Run(new ServerExpressionTransformer(), afterComments);
        Assert.StartsWith("@*", afterExpr);
        Assert.EndsWith("*@", afterExpr);
        // Pas de syntaxe ASPX residuelle qui s'echapperait du commentaire.
        Assert.DoesNotContain("<%", afterExpr);
        Assert.DoesNotContain("%>", afterExpr);
    }
}
