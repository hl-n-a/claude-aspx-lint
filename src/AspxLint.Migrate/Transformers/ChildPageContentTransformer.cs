using System.Text.RegularExpressions;

namespace AspxLint.Migrate.Transformers;

/// <summary>
/// Transforme les `&lt;asp:Content&gt;` d'une page enfant ASPX :
///   - le `Content` "primary" → contenu inline (pas de wrapper)
///   - les autres              → `@section X { ... }`
///
/// Le primary est choisi via <see cref="MasterPageHelpers.PickPrimary"/>
/// applique aux ContentPlaceHolderID presents dans la page (cherche d'abord
/// "MainContent" / "Body" / "Content", sinon le premier dans l'ordre du
/// document).
///
/// Tourne sur tous les fichiers (la presence de `&lt;asp:Content&gt;` est
/// suffisante — ils n'apparaissent que dans des pages enfant de master,
/// par convention).
/// </summary>
public sealed class ChildPageContentTransformer : ITransformer
{
    public string Name => "ChildPageContent";

    // Le `(?<!/)` exclut les self-closing pour eviter les matchs cross-tag
    // (cf. note dans MasterContentPlaceHolderTransformer.WithContent).
    private static readonly Regex Content = new(
        @"<asp:Content\b([^>]*?)(?<!/)>([\s\S]*?)</asp:Content>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PlaceHolderIdAttr = new(
        @"\bContentPlaceHolderID\s*=\s*[""']([^""']+)[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Transform(string content, MigrationContext ctx)
    {
        // Phase 1 : collecte des IDs et detection des tags sans ID.
        var ids = new List<string>();
        bool hasAnyTag = false;
        foreach (Match m in Content.Matches(content))
        {
            hasAnyTag = true;
            var id = ExtractId(m.Groups[1].Value);
            if (id != null) ids.Add(id);
        }
        // Aucun <asp:Content> du tout : rien a faire.
        if (!hasAnyTag) return content;

        var primary = MasterPageHelpers.PickPrimary(ids);   // null si tous sans ID
        int inlineCount = 0, sectionCount = 0, emptyCount = 0;

        var result = Content.Replace(content, m =>
        {
            var id = ExtractId(m.Groups[1].Value);
            var inner = m.Groups[2].Value;
            int line = ServerCommentTransformer.LineOf(content, m.Index);

            if (id == null)
            {
                ctx.Log(MigrationSeverity.Warning, line, Name,
                    "<asp:Content> sans ContentPlaceHolderID — laisse en place pour revue manuelle.");
                return m.Value;
            }

            // <asp:Content> vide -> on retire purement (le master a
            // `@RenderSection(X, required: false)` donc la section optionnelle
            // peut etre absente sans probleme).
            if (string.IsNullOrWhiteSpace(inner))
            {
                emptyCount++;
                return "";
            }

            if (primary != null && id.Equals(primary, StringComparison.OrdinalIgnoreCase))
            {
                inlineCount++;
                // Inline : on retire les wrappers, on garde le contenu brut
                // (avec un trim leger pour eviter les blank lines parasites).
                return TrimOuterBlankLines(inner);
            }
            else
            {
                sectionCount++;
                return $"@section {id} {{{inner}}}";
            }
        });

        if (inlineCount + sectionCount + emptyCount > 0)
        {
            var parts = new List<string>();
            if (inlineCount > 0)  parts.Add($"{inlineCount} → inline (primary=`{primary}`)");
            if (sectionCount > 0) parts.Add($"{sectionCount} → @section");
            if (emptyCount > 0)   parts.Add($"{emptyCount} vide(s) supprime(s)");
            ctx.Log(MigrationSeverity.Auto, null, Name,
                "Page enfant : " + string.Join(", ", parts) + ".");
        }
        return result;
    }

    private static string? ExtractId(string attrs)
    {
        var m = PlaceHolderIdAttr.Match(attrs);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>
    /// Retire les eventuels \n / \r en debut et fin de ligne — le wrapper
    /// `&lt;asp:Content&gt;` cree typiquement des blank lines qu'on ne veut pas
    /// garder une fois desemballe.
    /// </summary>
    private static string TrimOuterBlankLines(string s)
    {
        // Trim seulement les newlines, pas tous les whitespaces (on veut
        // garder l'indentation du contenu lui-meme).
        var start = 0;
        var end = s.Length;
        while (start < end && (s[start] == '\n' || s[start] == '\r'))
            start++;
        while (end > start && (s[end - 1] == '\n' || s[end - 1] == '\r'))
            end--;
        return s.Substring(start, end - start);
    }
}
