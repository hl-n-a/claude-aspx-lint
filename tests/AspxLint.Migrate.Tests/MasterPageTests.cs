namespace AspxLint.Migrate.Tests;

/// <summary>
/// Tests Phase 2 : master pages → layouts + content pages → @section.
/// </summary>
public class MasterPageTests
{
    private static (string output, MigrationReport report)
        Run(ITransformer t, string input, string ext, string fileName)
    {
        var report = new MigrationReport();
        var ctx = new MigrationContext(fileName, ext, report);
        var output = t.Transform(input, ctx);
        return (output, report);
    }

    // ============= MasterContentPlaceHolderTransformer =============

    [Fact]
    public void Master_single_placeholder_becomes_render_body()
    {
        var t = new MasterContentPlaceHolderTransformer();
        var (output, _) = Run(t,
            "<asp:ContentPlaceHolder ID=\"X\" runat=\"server\" />",
            "master", "Site.master");
        Assert.Equal("@RenderBody()", output);
    }

    [Fact]
    public void Master_main_content_recognized_as_primary()
    {
        var t = new MasterContentPlaceHolderTransformer();
        var input = """
        <asp:ContentPlaceHolder ID="HeadContent" runat="server" />
        <asp:ContentPlaceHolder ID="MainContent" runat="server" />
        <asp:ContentPlaceHolder ID="FooterContent" runat="server" />
        """;
        var (output, _) = Run(t, input, "master", "Site.master");
        // MainContent → @RenderBody()
        Assert.Contains("@RenderBody()", output);
        // Les deux autres → @RenderSection
        Assert.Contains("@RenderSection(\"HeadContent\", required: false)", output);
        Assert.Contains("@RenderSection(\"FooterContent\", required: false)", output);
    }

    [Fact]
    public void Master_first_placeholder_primary_when_no_main_content_name()
    {
        var t = new MasterContentPlaceHolderTransformer();
        var input = """
        <asp:ContentPlaceHolder ID="Sidebar" runat="server" />
        <asp:ContentPlaceHolder ID="Footer" runat="server" />
        """;
        var (output, _) = Run(t, input, "master", "Site.master");
        // Pas de MainContent/Body/Content → premier (Sidebar) devient body.
        // Verifie via la position : @RenderBody() vient avant @RenderSection
        var iBody = output.IndexOf("@RenderBody()");
        var iSection = output.IndexOf("@RenderSection(\"Footer\"");
        Assert.True(iBody >= 0);
        Assert.True(iBody < iSection);
        Assert.DoesNotContain("@RenderSection(\"Sidebar\"", output);
    }

    [Fact]
    public void Master_placeholder_with_default_content_emits_todo()
    {
        var t = new MasterContentPlaceHolderTransformer();
        var input = """
        <asp:ContentPlaceHolder ID="Sidebar" runat="server">
          <h2>Default Sidebar</h2>
        </asp:ContentPlaceHolder>
        """;
        var (output, report) = Run(t, input, "master", "Site.master");
        Assert.Contains("@RenderBody()", output);   // Sidebar = primary (seul placeholder)
        Assert.Contains("TODO[aspx-migrate] default content", output);
        Assert.Contains("Default Sidebar", output);  // contenu original preserve dans le TODO
        Assert.Equal(1, report.CountBySeverity(MigrationSeverity.Manual));
    }

    [Fact]
    public void Master_mixed_self_closing_and_with_content_assigns_primary_correctly()
    {
        // Cas piege historique : si le regex `WithContent` n'exclut pas les
        // self-closing, il fait un match cross-tag qui absorbe le HeadContent
        // self-closing et le `</asp:ContentPlaceHolder>` de MainContent.
        // Du coup HeadContent etait flag comme primary au lieu de MainContent.
        var t = new MasterContentPlaceHolderTransformer();
        var input = """
        <head>
          <asp:ContentPlaceHolder ID="HeadContent" runat="server" />
        </head>
        <body>
          <asp:ContentPlaceHolder ID="MainContent" runat="server">
            <p>default</p>
          </asp:ContentPlaceHolder>
          <asp:ContentPlaceHolder ID="FooterContent" runat="server" />
        </body>
        """;
        var (output, _) = Run(t, input, "master", "Site.master");

        // HeadContent et FooterContent → @RenderSection
        Assert.Contains("@RenderSection(\"HeadContent\", required: false)", output);
        Assert.Contains("@RenderSection(\"FooterContent\", required: false)", output);
        // MainContent → @RenderBody (parce que reconnu par MasterPageHelpers
        // comme nom de primary, pas parce qu'il a un body)
        Assert.Contains("@RenderBody()", output);
        Assert.DoesNotContain("@RenderSection(\"MainContent\"", output);
    }

    [Fact]
    public void Master_does_not_fire_on_non_master_files()
    {
        var t = new MasterContentPlaceHolderTransformer();
        var input = "<asp:ContentPlaceHolder ID=\"X\" runat=\"server\" />";
        var (output, _) = Run(t, input, "aspx", "Foo.aspx");
        Assert.Equal(input, output);   // unchanged
    }

    // ============= ChildPageContentTransformer =============

    [Fact]
    public void Child_main_content_becomes_inline()
    {
        var t = new ChildPageContentTransformer();
        var input = """
        <asp:Content ContentPlaceHolderID="MainContent" runat="server">
          <h1>Hello</h1>
        </asp:Content>
        """;
        var (output, _) = Run(t, input, "aspx", "Index.aspx");
        // Pas de wrapper @section, juste le contenu inline.
        Assert.DoesNotContain("@section", output);
        Assert.DoesNotContain("<asp:Content", output);
        Assert.Contains("<h1>Hello</h1>", output);
    }

    [Fact]
    public void Child_secondary_content_becomes_section()
    {
        var t = new ChildPageContentTransformer();
        var input = """
        <asp:Content ContentPlaceHolderID="MainContent" runat="server">
          body
        </asp:Content>
        <asp:Content ContentPlaceHolderID="HeadContent" runat="server">
          <link rel="stylesheet" href="x.css" />
        </asp:Content>
        """;
        var (output, _) = Run(t, input, "aspx", "Index.aspx");
        Assert.Contains("body", output);                          // inline (MainContent = primary)
        Assert.Contains("@section HeadContent {", output);        // @section pour le reste
        Assert.Contains("<link rel=\"stylesheet\" href=\"x.css\" />", output);
    }

    [Fact]
    public void Child_first_content_inline_when_no_main_content_name()
    {
        var t = new ChildPageContentTransformer();
        var input = """
        <asp:Content ContentPlaceHolderID="Foo" runat="server">first</asp:Content>
        <asp:Content ContentPlaceHolderID="Bar" runat="server">second</asp:Content>
        """;
        var (output, _) = Run(t, input, "aspx", "x.aspx");
        // Foo (premier) devient inline ; Bar devient section
        Assert.Contains("first", output);
        Assert.Contains("@section Bar {", output);
        Assert.DoesNotContain("@section Foo {", output);
    }

    [Fact]
    public void Child_no_asp_content_unchanged()
    {
        var t = new ChildPageContentTransformer();
        var input = "<div>plain html</div>";
        var (output, report) = Run(t, input, "aspx", "Plain.aspx");
        Assert.Equal(input, output);
        Assert.Empty(report.Actions);
    }

    [Fact]
    public void Child_asp_content_without_id_warns()
    {
        var t = new ChildPageContentTransformer();
        var input = "<asp:Content runat=\"server\">x</asp:Content>";
        var (_, report) = Run(t, input, "aspx", "x.aspx");
        Assert.True(report.CountBySeverity(MigrationSeverity.Warning) >= 1);
    }

    // ============= MasterPageHelpers =============

    [Fact]
    public void Helpers_master_path_to_layout_resolves_tilde_and_extension()
    {
        Assert.Equal("_Site",   Transformers.MasterPageHelpers.MasterPathToLayoutName("~/Site.Master"));
        Assert.Equal("_Public", Transformers.MasterPageHelpers.MasterPathToLayoutName("~/MasterPages/Public.Master"));
        Assert.Equal("_Site",   Transformers.MasterPageHelpers.MasterPathToLayoutName("Site.master"));
    }

    [Fact]
    public void Helpers_pick_primary_prefers_main_content_name()
    {
        var ids = new[] { "HeadContent", "MainContent", "Footer" };
        Assert.Equal("MainContent", Transformers.MasterPageHelpers.PickPrimary(ids));
    }

    [Fact]
    public void Helpers_pick_primary_falls_back_to_first()
    {
        var ids = new[] { "Foo", "Bar" };
        Assert.Equal("Foo", Transformers.MasterPageHelpers.PickPrimary(ids));
    }

    // ============= Migrator end-to-end (Phase 2) =============

    [Fact]
    public void Migrate_master_emits_clean_layout()
    {
        var input = """
        <%@ Master Language="C#" %>
        <!DOCTYPE html>
        <html>
        <head>
          <asp:ContentPlaceHolder ID="HeadContent" runat="server" />
        </head>
        <body>
          <asp:ContentPlaceHolder ID="MainContent" runat="server" />
        </body>
        </html>
        """;
        var result = Migrator.Migrate(input, "Site.master");
        Assert.Contains("@RenderBody()", result.Content);
        Assert.Contains("@RenderSection(\"HeadContent\", required: false)", result.Content);
        Assert.DoesNotContain("<%@ Master", result.Content);
        Assert.DoesNotContain("<asp:ContentPlaceHolder", result.Content);
    }

    [Fact]
    public void Migrate_child_page_emits_layout_binding_and_sections()
    {
        var input = """
        <%@ Page Language="C#" Inherits="App.Index" MasterPageFile="~/Site.Master" %>
        <asp:Content ContentPlaceHolderID="HeadContent" runat="server">
          <link rel="stylesheet" href="x.css" />
        </asp:Content>
        <asp:Content ContentPlaceHolderID="MainContent" runat="server">
          <h1>Hello</h1>
        </asp:Content>
        """;
        var result = Migrator.Migrate(input, "Index.aspx");
        Assert.Contains("@page", result.Content);
        Assert.Contains("@model App.Index", result.Content);
        Assert.Contains("Layout = \"_Site\"", result.Content);
        Assert.Contains("@section HeadContent {", result.Content);
        Assert.Contains("<h1>Hello</h1>", result.Content);
        Assert.DoesNotContain("@section MainContent", result.Content);   // primary → inline
        Assert.DoesNotContain("<asp:Content", result.Content);
    }
}
