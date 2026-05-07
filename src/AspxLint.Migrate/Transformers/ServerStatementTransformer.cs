using System.Text.RegularExpressions;

namespace AspxLint.Migrate.Transformers;

/// <summary>
/// Convertit les blocs d'instructions serveur `&lt;% stmt %&gt;` en Razor.
///
/// Le piege classique : `&lt;% if (x) { %&gt; ... &lt;% } %&gt;` ne peut PAS etre
/// converti en `@{ if (x) { }` ... `@{ } }` parce que Razor parserait
/// chaque `@{ }` comme un bloc independant (le `{` interne ouvre un sous-
/// scope C# mais le `}` du bloc Razor le ferme immediatement, et le HTML
/// suivant tombe en erreur de parse).
///
/// La conversion correcte :
///   `&lt;% if (cond) { %&gt;`     → `@if (cond) {`     (pas de @{ })
///   `&lt;% } %&gt;`                → `}`                (juste l'accolade)
///   `&lt;% } else { %&gt;`         → `} else {`
///   `&lt;% foreach (...) { %&gt;`  → `@foreach (...) {`
///   `&lt;% var x = 1; %&gt;`       → `@{ var x = 1; }`  (statement complet → @{ })
///
/// On detecte en regardant si le contenu commence par un mot-cle de
/// controle (if/for/foreach/while/do/switch/using/try/lock) ou est juste
/// un `}` / `} else { ` / `} else if (...) { `.
///
/// Tourne APRES <see cref="ServerCommentTransformer"/> et
/// <see cref="ServerExpressionTransformer"/> pour ne pas matcher leurs
/// blocs (qui commencent aussi par `&lt;%`). On exclut explicitement les
/// prefixes `=`, `:`, `#`, `--`, `@`.
/// </summary>
public sealed class ServerStatementTransformer : ITransformer
{
    public string Name => "ServerStatement";

    private static readonly Regex ServerStmt =
        new(@"<%(?![=:#@\-])([\s\S]*?)%>", RegexOptions.Compiled);

    // Mots-cles C# qui ouvrent un bloc.
    private static readonly Regex BlockKeyword =
        new(@"^\s*(if|else\s+if|for|foreach|while|do|switch|using|try|catch|finally|lock|fixed|unsafe|checked|unchecked)\b",
            RegexOptions.Compiled);

    // Match le contenu qui est juste `}` (avec eventuellement un `else { ` apres ou un `else if (...) { `).
    private static readonly Regex ClosingOnly =
        new(@"^\s*\}\s*((?:else(\s+if\s*\([^)]*\))?\s*\{)?)\s*$", RegexOptions.Compiled);

    // Match `else { ` au debut (sans le } qui le precede).
    private static readonly Regex ElseOpening =
        new(@"^\s*else(\s+if\s*\([^)]*\))?\s*\{?\s*$", RegexOptions.Compiled);

    public string Transform(string content, MigrationContext ctx)
    {
        int statementCount = 0;
        int controlCount = 0;
        int closingCount = 0;

        var result = ServerStmt.Replace(content, m =>
        {
            var raw = m.Groups[1].Value;
            var stmt = raw.Trim();
            int line = ServerCommentTransformer.LineOf(content, m.Index);

            if (Regex.IsMatch(stmt, @"\bResponse\s*\.\s*Write\s*\("))
            {
                ctx.Log(MigrationSeverity.Manual, line, Name,
                    "Bloc serveur contient `Response.Write(...)` qui est generalement remplacable par une expression `@(...)` directe en Razor.");
            }

            // 1. `<% } %>` ou `<% } else { %>` → juste les accolades, sans @{}.
            //    Indispensable pour les structures de controle multi-blocs.
            if (ClosingOnly.IsMatch(stmt))
            {
                closingCount++;
                return stmt;
            }

            // 2. `<% else ... %>` ou `<% else if (...) { %>` → idem.
            if (ElseOpening.IsMatch(stmt))
            {
                closingCount++;
                return stmt;
            }

            // 3. `<% if (cond) { %>` ou `<% foreach (...) { %>` → `@<kw> ... {`
            //    Le @ prefixe le mot-cle pour que Razor traite la suite comme du C#.
            if (BlockKeyword.IsMatch(stmt) && stmt.EndsWith("{"))
            {
                controlCount++;
                return "@" + stmt;
            }

            // 4. Tout le reste (declarations, assignations, methods calls
            //    autonomes) → bloc `@{ }` classique.
            statementCount++;
            return $"@{{ {stmt} }}";
        });

        if (statementCount + controlCount + closingCount > 0)
        {
            var parts = new List<string>();
            if (statementCount > 0) parts.Add($"{statementCount} `<% stmt %>` → `@{{ stmt }}`");
            if (controlCount > 0)   parts.Add($"{controlCount} structures de controle → `@if/@foreach/etc. {{`");
            if (closingCount > 0)   parts.Add($"{closingCount} accolades de fermeture → `}}`");
            ctx.Log(MigrationSeverity.Auto, null, Name, string.Join(" ; ", parts) + ".");
        }
        return result;
    }
}
