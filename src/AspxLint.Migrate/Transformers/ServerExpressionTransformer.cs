using System.Text.RegularExpressions;

namespace AspxLint.Migrate.Transformers;

/// <summary>
/// Convertit les expressions serveur :
///   `&lt;%= expr %&gt;`  → `@(expr)` + warning (Razor encode par defaut, ASPX non)
///   `&lt;%: expr %&gt;`  → `@(expr)`            (semantique identique)
///   `&lt;%# expr %&gt;`  → `@(expr)` + manual (data-binding context different)
///
/// Tourne APRES <see cref="ServerCommentTransformer"/> pour ne pas matcher
/// dans des commentaires.
///
/// Pour memoire :
///   - ASPX `&lt;%= %&gt;` ne fait PAS de HTML-encode (Response.Write brut).
///   - ASPX `&lt;%: %&gt;` (.NET 4+) fait HTML-encode.
///   - Razor `@expr` HTML-encode par defaut. Pour le brut : `@Html.Raw(expr)`.
///   - ASPX `&lt;%# %&gt;` ne fire qu'apres DataBind() — vit dans des
///     templates de Repeater/GridView. En Razor, on passe par @Model.
/// </summary>
public sealed class ServerExpressionTransformer : ITransformer
{
    public string Name => "ServerExpression";

    // Capture le type (=, :, #) et le contenu. [\s\S] pour multi-ligne,
    // *? non-greedy pour ne pas avaler plusieurs blocs.
    private static readonly Regex ServerExpr =
        new(@"<%([=:#])([\s\S]*?)%>", RegexOptions.Compiled);

    // Classifie une expression `<%= expr %>` pour decider :
    //   - Si elle appelle un helper MVC (Html.X / Url.X / Ajax.X) -> safe,
    //     pas de probleme d'encoding (retourne IHtmlString).
    //   - Si elle est deja explicitement Html.Raw(...) -> preserve.
    //   - Si elle contient du HTML literal en string ("<br>", "<div>") ->
    //     l'utilisateur voulait du HTML brut, on auto-wrap en Html.Raw
    //     (avec un Warning : XSS si le contenu n'est pas sur).
    //   - Sinon : expression simple, Razor encode par defaut, c'est le bon
    //     comportement. Auto, pas de warning.

    private static readonly Regex MvcHelperCall = new(
        @"^\s*(?:Html|Url|Ajax)\s*\.\s*\w+\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex AlreadyRaw = new(
        @"^\s*Html\s*\.\s*Raw\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex HtmlLiteralInString = new(
        @"""\s*</?\w+",     // "<tag" ou "</tag" dans une string literal
        RegexOptions.Compiled);

    public string Transform(string content, MigrationContext ctx)
    {
        int eqMvc = 0, eqRaw = 0, eqHtmlLit = 0, eqSimple = 0;
        int colon = 0, hash = 0;

        var result = ServerExpr.Replace(content, m =>
        {
            var type = m.Groups[1].Value;
            var expr = m.Groups[2].Value.Trim();
            int line = ServerCommentTransformer.LineOf(content, m.Index);

            switch (type)
            {
                case ":":
                    colon++;
                    return $"@({expr})";

                case "=":
                    return HandleEqualsExpression(expr, line, ctx,
                        ref eqMvc, ref eqRaw, ref eqHtmlLit, ref eqSimple);

                case "#":
                    hash++;
                    ctx.Log(MigrationSeverity.Manual, line, Name,
                        $"`<%# {Trunc(expr)} %>` → `@({Trunc(expr)})`. Le data-binding (`Eval(\"X\")`, `Bind(\"X\")`, `Container.DataItem`) n'a pas d'equivalent direct en Razor — verifier que l'expression marche dans le contexte du modele courant.");
                    return $"@*TODO[aspx-migrate] data-binding: was <%# {expr} %>*@@({expr})";

                default:
                    return m.Value;
            }
        });

        // Resume agrege par categorie (1 log par categorie au lieu de N
        // warnings par occurrence -> rapport beaucoup plus lisible).
        var parts = new List<string>();
        if (eqMvc > 0)      parts.Add($"{eqMvc} appel(s) MVC helper (Html/Url/Ajax) → `@(...)`, IHtmlString safe");
        if (eqRaw > 0)      parts.Add($"{eqRaw} `Html.Raw(...)` preserve(s)");
        if (eqHtmlLit > 0)  parts.Add($"{eqHtmlLit} expression(s) avec HTML literal → wrap auto en `@Html.Raw(...)` (verifier XSS)");
        if (eqSimple > 0)   parts.Add($"{eqSimple} expression(s) simple(s) → `@(...)` (Razor encode par defaut)");
        if (colon > 0)      parts.Add($"{colon} `<%: %>` → `@(...)`");
        if (hash > 0)       parts.Add($"{hash} `<%# %>` → `@(...)` + TODO data-binding");
        if (parts.Count > 0)
            ctx.Log(MigrationSeverity.Auto, null, Name, string.Join(" ; ", parts) + ".");

        return result;
    }

    private string HandleEqualsExpression(
        string expr, int line, MigrationContext ctx,
        ref int eqMvc, ref int eqRaw, ref int eqHtmlLit, ref int eqSimple)
    {
        // 1. Html.Raw(...) deja la — translation 1-1, pas de souci.
        if (AlreadyRaw.IsMatch(expr))
        {
            eqRaw++;
            return $"@({expr})";
        }

        // 2. Helper MVC (Html.X / Url.X / Ajax.X) — retourne IHtmlString
        //    en MVC ; Razor sait que IHtmlString ne doit pas etre re-encode.
        if (MvcHelperCall.IsMatch(expr))
        {
            eqMvc++;
            return $"@({expr})";
        }

        // 3. Contient du HTML literal (`"<br>"`, `"</div>"`, etc.) — l'auteur
        //    voulait sortir du HTML brut. Auto-wrap en Html.Raw, avec un
        //    Warning sur le risque XSS si le contenu venait d'une source
        //    non-fiable (l'utilisateur doit verifier).
        if (HtmlLiteralInString.IsMatch(expr))
        {
            eqHtmlLit++;
            ctx.Log(MigrationSeverity.Warning, line, Name,
                $"`<%= {Trunc(expr)} %>` contient du HTML literal — wrap en `@Html.Raw(...)` pour preserver le rendu. Verifier que la donnee est sure (sinon XSS).");
            return $"@Html.Raw({expr})";
        }

        // 4. Cas par defaut : expression simple. Razor encode → c'est ce
        //    qu'on veut dans 99% des cas. Pas de warning.
        eqSimple++;
        return $"@({expr})";
    }

    private static string Trunc(string s, int max = 40)
        => s.Length <= max ? s : s.Substring(0, max - 1) + "…";
}
