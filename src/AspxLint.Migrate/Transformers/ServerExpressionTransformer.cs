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

    public string Transform(string content, MigrationContext ctx)
    {
        int eq = 0, colon = 0, hash = 0;
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
                    eq++;
                    ctx.Log(MigrationSeverity.Warning, line, Name,
                        $"`<%= {Trunc(expr)} %>` → `@({Trunc(expr)})`. ASPX `<%=` n'encode pas le HTML, Razor `@` si. Si la valeur DOIT rester non-encode, remplacer par `@Html.Raw({Trunc(expr)})`.");
                    return $"@({expr})";

                case "#":
                    hash++;
                    ctx.Log(MigrationSeverity.Manual, line, Name,
                        $"`<%# {Trunc(expr)} %>` → `@({Trunc(expr)})`. Le data-binding (`Eval(\"X\")`, `Bind(\"X\")`, `Container.DataItem`) n'a pas d'equivalent direct en Razor — verifier que l'expression marche dans le contexte du modele courant.");
                    // On laisse un commentaire TODO juste avant l'expression.
                    return $"@*TODO[aspx-migrate] data-binding: was <%# {expr} %>*@@({expr})";

                default:
                    return m.Value;
            }
        });

        if (eq + colon + hash > 0)
        {
            ctx.Log(MigrationSeverity.Auto, null, Name,
                $"{eq} `<%= %>`, {colon} `<%: %>`, {hash} `<%# %>` transformes vers `@(...)`.");
        }
        return result;
    }

    private static string Trunc(string s, int max = 40)
        => s.Length <= max ? s : s.Substring(0, max - 1) + "…";
}
