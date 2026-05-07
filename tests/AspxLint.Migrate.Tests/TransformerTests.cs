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
    public void ServerExpression_raw_warns()
    {
        var t = new ServerExpressionTransformer();
        var (output, report) = Run(t, "<%= Model.Html %>");
        Assert.Equal("@(Model.Html)", output);
        Assert.True(report.CountBySeverity(MigrationSeverity.Warning) >= 1);
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
    public void PageDirective_master_flagged_manual()
    {
        var t = new PageDirectiveTransformer();
        var (output, report) = Run(t, "<%@ Master Language=\"C#\" %>");
        Assert.Contains("TODO[aspx-migrate]", output);
        Assert.Equal(1, report.CountBySeverity(MigrationSeverity.Manual));
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

    // ============= Robustesse / edge cases =============

    [Fact]
    public void ServerStatement_empty_block_does_not_crash()
    {
        var t = new ServerStatementTransformer();
        var (output, _) = Run(t, "<% %>");
        Assert.Equal("@{  }", output);
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
