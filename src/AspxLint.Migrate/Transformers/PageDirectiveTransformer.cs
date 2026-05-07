using System.Text.RegularExpressions;

namespace AspxLint.Migrate.Transformers;

/// <summary>
/// Convertit les directives de tete (toujours en debut de fichier en
/// pratique) :
///
///   `&lt;%@ Page Inherits="Foo.Bar" %&gt;`
///     → `@page` + `@model Foo.Bar` (.aspx -&gt; Razor Page)
///
///   `&lt;%@ Control Inherits="Foo.Bar" %&gt;`
///     → `@model Foo.Bar` (.ascx -&gt; partial view, pas de @page)
///
///   `&lt;%@ Master ... %&gt;`
///     → flag manual : on ne genere pas de Razor Layout en Phase 1
///       (Phase 2 le fera proprement avec @RenderBody / @RenderSection).
///
///   `&lt;%@ Register ... %&gt;`
///     → flag manual : Razor utilise @using ou @addTagHelper, pas
///       d'equivalent mecanique.
///
///   `&lt;%@ Import Namespace="X" %&gt;`
///     → `@using X` (auto)
///
///   `&lt;%@ Assembly Name="X" %&gt;`
///     → flag manual (les references projet sont differentes en Razor).
///
/// Tourne APRES <see cref="ServerCommentTransformer"/> et AVANT les autres
/// transformers d'expression — les directives ne ressemblent a rien
/// d'autre, donc l'ordre n'est pas critique.
/// </summary>
public sealed class PageDirectiveTransformer : ITransformer
{
    public string Name => "PageDirective";

    // Capture la directive : `<%@ Name attr1="val1" attr2="val2" %>`
    private static readonly Regex Directive =
        new(@"<%@\s*(\w+)([^%]*?)%>", RegexOptions.Compiled);

    // Sub-pattern pour les attributs : Name="value" ou Name='value'
    private static readonly Regex Attribute =
        new(@"(\w+)\s*=\s*[""']([^""']*)[""']", RegexOptions.Compiled);

    public string Transform(string content, MigrationContext ctx)
    {
        return Directive.Replace(content, m =>
        {
            var directiveName = m.Groups[1].Value.ToLowerInvariant();
            var attrs = ParseAttrs(m.Groups[2].Value);
            int line = ServerCommentTransformer.LineOf(content, m.Index);

            return directiveName switch
            {
                "page"     => HandlePage(attrs, ctx, line),
                "control"  => HandleControl(attrs, ctx, line),
                "master"   => HandleMaster(attrs, ctx, line, m.Value),
                "register" => HandleRegister(attrs, ctx, line, m.Value),
                "import"   => HandleImport(attrs, ctx, line),
                "assembly" => HandleAssembly(attrs, ctx, line, m.Value),
                "outputcache" => HandleOutputCache(attrs, ctx, line, m.Value),
                _ => HandleUnknown(directiveName, ctx, line, m.Value)
            };
        });
    }

    private static Dictionary<string, string> ParseAttrs(string raw)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Attribute.Matches(raw))
            dict[m.Groups[1].Value] = m.Groups[2].Value;
        return dict;
    }

    private string HandlePage(Dictionary<string, string> attrs, MigrationContext ctx, int line)
    {
        var lines = new List<string> { "@page" };
        if (attrs.TryGetValue("Inherits", out var model))
            lines.Add($"@model {model}");

        // MasterPageFile : "Razor Page" peut declarer Layout = "..." dans
        // un bloc @{ }. On le signale mais on ne le branche pas
        // automatiquement (Phase 2 fera la migration master proprement).
        if (attrs.TryGetValue("MasterPageFile", out var master))
        {
            ctx.Log(MigrationSeverity.Manual, line, Name,
                $"@Page MasterPageFile=\"{master}\" → en Razor utiliser `@{{ Layout = \"_Layout\"; }}` (Phase 2 du migrate gerera ca proprement).");
            lines.Add($"@*TODO[aspx-migrate] master page: {master} *@");
        }

        // Title : pour info, on ne porte pas (Razor a ses propres conventions
        // via ViewData["Title"]).
        if (attrs.TryGetValue("Title", out var title))
            lines.Add($"@*TODO[aspx-migrate] Title=\"{title}\" — utiliser ViewData[\"Title\"] *@");

        ctx.Log(MigrationSeverity.Auto, line, Name,
            "Directive @Page → `@page` + `@model`.");
        return string.Join("\n", lines);
    }

    private string HandleControl(Dictionary<string, string> attrs, MigrationContext ctx, int line)
    {
        var lines = new List<string>();
        if (attrs.TryGetValue("Inherits", out var model))
            lines.Add($"@model {model}");
        ctx.Log(MigrationSeverity.Auto, line, Name,
            ".ascx → partial view : pas de directive specifique, juste `@model` si Inherits etait pose.");
        // Si pas de @model, on ne genere RIEN — les partials Razor
        // n'ont pas de directive d'entete.
        return lines.Count == 0 ? "" : string.Join("\n", lines);
    }

    private string HandleMaster(Dictionary<string, string> attrs, MigrationContext ctx, int line, string original)
    {
        ctx.Log(MigrationSeverity.Manual, line, Name,
            "Directive @Master → migration vers Razor Layout (`_Layout.cshtml`) deferree a la Phase 2 du migrate. Le contenu du master n'est pas traduit automatiquement en Phase 1.");
        return $"@*TODO[aspx-migrate] master page: {original} *@";
    }

    private string HandleRegister(Dictionary<string, string> attrs, MigrationContext ctx, int line, string original)
    {
        // <%@ Register Src="..." TagPrefix="X" TagName="Y" %>  → user control
        // <%@ Register Assembly="..." Namespace="X" TagPrefix="Y" %>  → custom server control
        ctx.Log(MigrationSeverity.Manual, line, Name,
            "@Register sans equivalent mecanique : en Razor utiliser `@addTagHelper` (pour les TagHelpers) ou `@using` + `<partial name=\"X\" />` (pour les anciens user controls).");
        return $"@*TODO[aspx-migrate] @Register: {original} *@";
    }

    private string HandleImport(Dictionary<string, string> attrs, MigrationContext ctx, int line)
    {
        if (!attrs.TryGetValue("Namespace", out var ns) || string.IsNullOrEmpty(ns))
        {
            ctx.Log(MigrationSeverity.Warning, line, Name,
                "@Import sans Namespace ignore.");
            return "";
        }
        ctx.Log(MigrationSeverity.Auto, line, Name,
            $"@Import Namespace=\"{ns}\" → `@using {ns}`.");
        return $"@using {ns}";
    }

    private string HandleAssembly(Dictionary<string, string> attrs, MigrationContext ctx, int line, string original)
    {
        ctx.Log(MigrationSeverity.Manual, line, Name,
            "@Assembly Name=... → en Razor les references projet sont gerees par le csproj, pas dans la vue.");
        return $"@*TODO[aspx-migrate] @Assembly: {original} *@";
    }

    private string HandleOutputCache(Dictionary<string, string> attrs, MigrationContext ctx, int line, string original)
    {
        ctx.Log(MigrationSeverity.Manual, line, Name,
            "@OutputCache → en Razor utiliser `[ResponseCache]` sur le PageModel ou `<cache>` tag helper.");
        return $"@*TODO[aspx-migrate] @OutputCache: {original} *@";
    }

    private string HandleUnknown(string name, MigrationContext ctx, int line, string original)
    {
        ctx.Log(MigrationSeverity.Manual, line, Name,
            $"Directive @{name} non reconnue — laissee comme commentaire TODO.");
        return $"@*TODO[aspx-migrate] @{name}: {original} *@";
    }
}
