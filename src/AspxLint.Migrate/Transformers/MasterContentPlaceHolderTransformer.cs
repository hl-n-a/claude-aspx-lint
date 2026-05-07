using System.Text.RegularExpressions;

namespace AspxLint.Migrate.Transformers;

/// <summary>
/// Transforme les `&lt;asp:ContentPlaceHolder&gt;` d'un master ASPX en
/// directives Razor :
///   - placeholder "primary" → `@RenderBody()`
///   - autres placeholders   → `@RenderSection("ID", required: false)`
///
/// Le primary est choisi via <see cref="MasterPageHelpers.PickPrimary"/>
/// (preferences "MainContent" / "Body" / "Content", sinon le premier par
/// ordre du document).
///
/// Le contenu par defaut d'un placeholder (
/// `&lt;asp:ContentPlaceHolder ID="X"&gt;default markup&lt;/asp:ContentPlaceHolder&gt;`)
/// est garde en commentaire TODO — Razor n'a pas de mecanisme natif pour
/// un default-de-section sans wrapping `@if (!IsSectionDefined("X"))`.
///
/// Ne fire que sur les fichiers .master (ctx.Ext == "master").
/// </summary>
public sealed class MasterContentPlaceHolderTransformer : ITransformer
{
    public string Name => "MasterContentPlaceHolder";

    // Self-closing : <asp:ContentPlaceHolder ID="X" runat="server" />
    private static readonly Regex SelfClosing = new(
        @"<asp:ContentPlaceHolder\b([^>]*?)/\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Avec contenu : <asp:ContentPlaceHolder ID="X" runat="server"> ... </asp:ContentPlaceHolder>
    // Le `(?<!/)` (negative lookbehind) exclut les self-closing : sinon, ce
    // regex ferait un match cross-tag qui absorbe un self-closing precedent
    // et finit au `</asp:ContentPlaceHolder>` du tag suivant. Bug subtil
    // qui assignait le mauvais ID au primary.
    private static readonly Regex WithContent = new(
        @"<asp:ContentPlaceHolder\b([^>]*?)(?<!/)>([\s\S]*?)</asp:ContentPlaceHolder>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex IdAttr = new(
        @"\bID\s*=\s*[""']([^""']+)[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Transform(string content, MigrationContext ctx)
    {
        if (ctx.Ext != "master") return content;

        // Phase 1 : collecte des IDs pour decider du primary.
        var ids = new List<string>();
        foreach (Match m in SelfClosing.Matches(content))
        {
            var id = ExtractId(m.Groups[1].Value);
            if (id != null) ids.Add(id);
        }
        foreach (Match m in WithContent.Matches(content))
        {
            var id = ExtractId(m.Groups[1].Value);
            if (id != null) ids.Add(id);
        }

        var primary = MasterPageHelpers.PickPrimary(ids);
        int countBody = 0, countSection = 0;

        // Phase 2 : remplace.
        // Self-closing d'abord pour eviter qu'il soit absorbe par WithContent.
        var result = SelfClosing.Replace(content, m =>
            ReplacePlaceholder(m, content, primary, defaultContent: null,
                ref countBody, ref countSection, ctx));

        result = WithContent.Replace(result, m =>
        {
            var inner = m.Groups[2].Value;
            return ReplacePlaceholder(m, content, primary, defaultContent: inner,
                ref countBody, ref countSection, ctx);
        });

        if (countBody + countSection > 0)
        {
            ctx.Log(MigrationSeverity.Auto, null, Name,
                $"Master : {countBody} → @RenderBody() (primary=`{primary}`), {countSection} → @RenderSection(...).");
        }
        return result;
    }

    private static string? ExtractId(string attrs)
    {
        var m = IdAttr.Match(attrs);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string ReplacePlaceholder(
        Match m, string fullContent, string? primary,
        string? defaultContent,
        ref int countBody, ref int countSection,
        MigrationContext ctx)
    {
        var id = ExtractId(m.Groups[1].Value);
        int line = ServerCommentTransformer.LineOf(fullContent, m.Index);

        if (id == null)
        {
            ctx.Log(MigrationSeverity.Warning, line, "MasterContentPlaceHolder",
                "ContentPlaceHolder sans ID — convertir manuellement, le master n'est pas complet.");
            return m.Value;
        }

        var isPrimary = primary != null && id.Equals(primary, StringComparison.OrdinalIgnoreCase);

        var sb = new System.Text.StringBuilder();
        if (isPrimary)
        {
            sb.Append("@RenderBody()");
            countBody++;
        }
        else
        {
            sb.Append($"@RenderSection(\"{id}\", required: false)");
            countSection++;
        }

        // Default content : on l'emet en commentaire TODO juste apres.
        if (!string.IsNullOrWhiteSpace(defaultContent))
        {
            ctx.Log(MigrationSeverity.Manual, line, "MasterContentPlaceHolder",
                $"`<asp:ContentPlaceHolder ID=\"{id}\">` avait du contenu par defaut. Razor n'a pas de default natif — l'ai mis en commentaire TODO. Si tu veux le restaurer, wrap avec `@if (!IsSectionDefined(\"{id}\")) {{ ... }}`.");
            sb.Append("\n@*TODO[aspx-migrate] default content of placeholder \"")
              .Append(id)
              .Append("\":\n")
              .Append(defaultContent.Trim())
              .Append("\n*@");
        }

        return sb.ToString();
    }
}
