namespace AspxLint.Migrate.Tests;

/// <summary>
/// Tests d'integration sur le Migrator complet (pipeline des 4 transformers).
/// On verifie que les fichiers ASPX realistes sortent du Razor valide
/// (autant qu'on puisse l'affirmer sans lancer le compilateur Razor).
/// </summary>
public class MigratorTests
{
    [Fact]
    public void Migrate_simple_page_emits_well_formed_razor()
    {
        var input = """
        <%@ Page Language="C#" Inherits="MyApp.Index" %>
        <!DOCTYPE html>
        <html><body>
          <%-- comment --%>
          <h1><%: Title %></h1>
        </body></html>
        """;
        var result = Migrator.Migrate(input, "Index.aspx");
        Assert.Contains("@page", result.Content);
        Assert.Contains("@model MyApp.Index", result.Content);
        Assert.Contains("@* comment *@", result.Content);
        Assert.Contains("@(Title)", result.Content);
        // Aucune trace de syntaxe ASPX residuelle.
        Assert.DoesNotContain("<%", result.Content);
        Assert.DoesNotContain("%>", result.Content);
    }

    [Fact]
    public void Migrate_if_else_pattern_yields_clean_razor()
    {
        var input = """
        <% if (x > 0) { %>
          <p>positive</p>
        <% } else if (x < 0) { %>
          <p>negative</p>
        <% } else { %>
          <p>zero</p>
        <% } %>
        """;
        var result = Migrator.Migrate(input, "x.aspx");
        Assert.Contains("@if (x > 0) {", result.Content);
        Assert.Contains("else if (x < 0) {", result.Content);
        Assert.Contains("else {", result.Content);
        Assert.DoesNotContain("@{ if", result.Content);
        Assert.DoesNotContain("@{ }", result.Content);
    }

    [Fact]
    public void Migrate_foreach_pattern_yields_clean_razor()
    {
        var input = """
        <ul>
          <% foreach (var item in items) { %>
            <li><%: item.Name %></li>
          <% } %>
        </ul>
        """;
        var result = Migrator.Migrate(input, "list.aspx");
        Assert.Contains("@foreach (var item in items) {", result.Content);
        Assert.Contains("@(item.Name)", result.Content);
    }

    [Fact]
    public void Migrate_ascx_does_not_emit_page()
    {
        var input = "<%@ Control Language=\"C#\" Inherits=\"MyApp.PageHeader\" %>\n<div>x</div>";
        var result = Migrator.Migrate(input, "PageHeader.ascx");
        Assert.DoesNotContain("@page", result.Content);
        Assert.Contains("@model MyApp.PageHeader", result.Content);
    }

    [Fact]
    public void Migrate_master_flags_for_phase2()
    {
        var input = "<%@ Master Language=\"C#\" %>\n<asp:ContentPlaceHolder ID=\"Body\" runat=\"server\" />";
        var result = Migrator.Migrate(input, "Site.master");
        // En Phase 1 on ne traite pas les ContentPlaceHolder ; on insere
        // un TODO sur la directive @Master.
        Assert.Contains("TODO[aspx-migrate]", result.Content);
        Assert.True(result.Actions.Any(a => a.Severity == MigrationSeverity.Manual));
    }

    [Fact]
    public void Migrate_suggested_output_name_aspx_to_cshtml()
    {
        var name = Migrator.SuggestOutputName(Path.Combine("Views", "Home", "Index.aspx"));
        Assert.Equal(Path.Combine("Views", "Home", "Index.cshtml"), name);
    }

    [Fact]
    public void Migrate_suggested_output_name_ascx_gets_underscore_prefix()
    {
        var name = Migrator.SuggestOutputName(Path.Combine("Controls", "Header.ascx"));
        Assert.Equal(Path.Combine("Controls", "_Header.cshtml"), name);
    }

    [Fact]
    public void Migrate_suggested_output_name_master_gets_underscore_prefix()
    {
        var name = Migrator.SuggestOutputName("Site.master");
        Assert.Equal("_Site.cshtml", name);
    }

    [Fact]
    public void Migrate_returns_actions_for_this_file_only()
    {
        var report = new MigrationReport();
        var r1 = Migrator.Migrate("<%-- a --%>", "f1.aspx", report);
        var r2 = Migrator.Migrate("<%-- b --%>", "f2.aspx", report);

        // Le report cumule les actions des deux fichiers.
        Assert.Equal(2, report.Actions.Count);
        // Chaque MigrationResult ne contient que ses propres actions.
        Assert.Single(r1.Actions);
        Assert.Single(r2.Actions);
        Assert.Equal("f1.aspx", r1.Actions[0].SourceFile);
        Assert.Equal("f2.aspx", r2.Actions[0].SourceFile);
    }

    [Fact]
    public void Migrate_empty_content_returns_empty()
    {
        var result = Migrator.Migrate("", "empty.aspx");
        Assert.Equal("", result.Content);
        Assert.Empty(result.Actions);
    }

    [Fact]
    public void Migrate_databinding_inserts_todo_marker()
    {
        var input = "<li><%# Eval(\"Name\") %></li>";
        var result = Migrator.Migrate(input, "row.aspx");
        Assert.Contains("TODO[aspx-migrate] data-binding", result.Content);
        Assert.True(result.Actions.Any(a => a.Severity == MigrationSeverity.Manual));
    }

    [Fact]
    public void MigrationReport_markdown_contains_summary_and_actions()
    {
        var report = new MigrationReport();
        Migrator.Migrate("<%@ Page Inherits=\"X\" %>", "test.aspx", report);
        var md = report.ToMarkdown();
        Assert.Contains("# aspx-lint migrate — rapport", md);
        Assert.Contains("transformations automatiques", md);
        Assert.Contains("`test.aspx`", md);
    }
}
