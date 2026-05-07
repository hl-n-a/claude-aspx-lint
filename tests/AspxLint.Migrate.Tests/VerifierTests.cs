namespace AspxLint.Migrate.Tests;

/// <summary>
/// Tests de <see cref="MigrationVerifier"/> : detection de residus ASPX
/// dans des fichiers .cshtml deja migres.
/// </summary>
public class VerifierTests
{
    [Fact]
    public void Verify_clean_razor_returns_no_issues()
    {
        var content = """
        @page
        @model App.Foo
        <h1>@Model.Name</h1>
        """;
        var issues = MigrationVerifier.Verify(content, "Foo.cshtml");
        Assert.Empty(issues);
    }

    [Fact]
    public void Verify_ignores_aspx_inside_razor_comment()
    {
        // PageDirectiveTransformer.HandleOutputCache emet volontairement le
        // code ASPX d'origine en commentaire TODO. Le verifier ne doit pas
        // signaler ces faux positifs.
        var content = "@*TODO[aspx-migrate] @OutputCache: <%@ OutputCache Duration=600 %> *@";
        var issues = MigrationVerifier.Verify(content, "x.cshtml");
        Assert.Empty(issues);
    }

    [Fact]
    public void Verify_detects_residual_server_directive_outside_comments()
    {
        var content = """
        @page
        <%@ OutputCache Duration=60 %>
        """;
        var issues = MigrationVerifier.Verify(content, "x.cshtml");
        Assert.Single(issues);
        Assert.Equal(VerifySeverity.Bug, issues[0].Severity);
        Assert.Equal("server-directive", issues[0].Pattern);
    }

    [Fact]
    public void Verify_detects_residual_server_expression()
    {
        var content = "<p><%= Model.Name %></p>";
        var issues = MigrationVerifier.Verify(content, "x.cshtml");
        Assert.True(issues.Any(i => i.Pattern == "server-expression" && i.Severity == VerifySeverity.Bug));
    }

    [Fact]
    public void Verify_detects_residual_server_statement()
    {
        var content = "<% var x = 1; %>";
        var issues = MigrationVerifier.Verify(content, "x.cshtml");
        Assert.True(issues.Any(i => i.Pattern == "server-statement" && i.Severity == VerifySeverity.Bug));
    }

    [Fact]
    public void Verify_detects_asp_label_as_pending()
    {
        var content = "<asp:Label ID=\"L1\" runat=\"server\" />";
        var issues = MigrationVerifier.Verify(content, "x.cshtml");
        Assert.True(issues.Any(i => i.Pattern == "asp:Label" && i.Severity == VerifySeverity.Pending));
    }

    [Fact]
    public void Verify_detects_asp_repeater_with_specific_suggestion()
    {
        var content = """
        <asp:Repeater ID="R1" runat="server">
          <ItemTemplate><li><%# Eval("Name") %></li></ItemTemplate>
        </asp:Repeater>
        """;
        var issues = MigrationVerifier.Verify(content, "x.cshtml");
        var repeater = issues.FirstOrDefault(i => i.Pattern == "asp:Repeater");
        Assert.NotNull(repeater);
        Assert.Contains("@foreach", repeater!.Suggestion);
    }

    [Fact]
    public void Verify_falls_back_to_generic_for_unknown_asp_control()
    {
        var content = "<asp:SomeFancyControl runat=\"server\" />";
        var issues = MigrationVerifier.Verify(content, "x.cshtml");
        Assert.True(issues.Any(i => i.Pattern == "asp:OtherControl"));
    }

    [Fact]
    public void Verify_does_not_double_count_specific_and_generic()
    {
        // <asp:Label> doit etre capture par la regle "asp:Label", pas aussi
        // par la regle generique "asp:OtherControl".
        var content = "<asp:Label runat=\"server\" />";
        var issues = MigrationVerifier.Verify(content, "x.cshtml");
        Assert.Single(issues);
        Assert.Equal("asp:Label", issues[0].Pattern);
    }

    [Fact]
    public void Verify_detects_residual_asp_content_as_bug()
    {
        // ChildPageContentTransformer aurait du convertir ce tag.
        var content = "<asp:Content ContentPlaceHolderID=\"X\" runat=\"server\">y</asp:Content>";
        var issues = MigrationVerifier.Verify(content, "x.cshtml");
        Assert.True(issues.Any(i => i.Pattern == "asp:Content" && i.Severity == VerifySeverity.Bug));
    }

    [Fact]
    public void Verify_detects_data_binding_methods()
    {
        var content = """
        <p>@(Eval("Name"))</p>
        <p>@(Bind("Status"))</p>
        <p>@Container.DataItem</p>
        """;
        var issues = MigrationVerifier.Verify(content, "x.cshtml");
        var patterns = issues.Select(i => i.Pattern).ToHashSet();
        Assert.Contains("Eval(", patterns);
        Assert.Contains("Bind(", patterns);
        Assert.Contains("Container.DataItem", patterns);
        Assert.All(issues, i => Assert.Equal(VerifySeverity.Pending, i.Severity));
    }

    [Fact]
    public void Verify_detects_runat_server_on_html_tag_as_manual()
    {
        var content = "<form runat=\"server\">";
        var issues = MigrationVerifier.Verify(content, "x.cshtml");
        Assert.True(issues.Any(i => i.Pattern == "runat-server-html" && i.Severity == VerifySeverity.Manual));
    }

    [Fact]
    public void Verify_directory_returns_per_file_issues()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.Path, "Clean.cshtml"), "@page\n<h1>ok</h1>\n");
        File.WriteAllText(Path.Combine(tmp.Path, "Dirty.cshtml"), "@page\n<asp:Label runat=\"server\" />\n");

        var issues = MigrationVerifier.VerifyDirectory(tmp.Path);
        Assert.Single(issues);
        Assert.Equal("Dirty.cshtml", issues[0].File);
    }

    [Fact]
    public void Verify_markdown_includes_summary_and_top_patterns()
    {
        var issues = new[]
        {
            new VerifyIssue(VerifySeverity.Pending, "f.cshtml", 1, "asp:Label", "<asp:Label", "..."),
            new VerifyIssue(VerifySeverity.Pending, "f.cshtml", 2, "asp:Label", "<asp:Label", "..."),
            new VerifyIssue(VerifySeverity.Bug,     "g.cshtml", 5, "server-directive", "<%@", "..."),
        };
        var md = MigrationVerifier.Markdown(issues);
        Assert.Contains("# aspx-lint migrate-verify", md);
        Assert.Contains("**1** residus syntaxiques", md);
        Assert.Contains("**2** controles serveur", md);
        Assert.Contains("Top patterns residuels", md);
        Assert.Contains("`asp:Label`", md);
    }

    [Fact]
    public void Verify_markdown_empty_when_no_issues()
    {
        var md = MigrationVerifier.Markdown(Array.Empty<VerifyIssue>());
        Assert.Contains("Aucun residu ASPX detecte", md);
    }
}

internal sealed class TempDir : IDisposable
{
    public string Path { get; }
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "aspxlint-migrate-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }
    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { }
    }
}
