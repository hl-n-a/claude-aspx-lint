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
        int emptyCount = 0;

        var result = ServerStmt.Replace(content, m =>
        {
            var raw = m.Groups[1].Value;
            var stmt = raw.Trim();
            int line = ServerCommentTransformer.LineOf(content, m.Index);

            // 0. `<% %>` vide -> on retire purement (sinon @{ } visuel inutile).
            if (stmt.Length == 0)
            {
                emptyCount++;
                return "";
            }

            if (Regex.IsMatch(stmt, @"\bResponse\s*\.\s*Write\s*\("))
            {
                ctx.Log(MigrationSeverity.Manual, line, Name,
                    "Bloc serveur contient `Response.Write(...)` qui est generalement remplacable par une expression `@(...)` directe en Razor.");
            }

            // 1. `<% } %>` ou `<% } else { %>` → juste les accolades, sans @{}.
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

            // 3a. `<% if (cond) { ... %>` (ouvre un bloc qui ne ferme pas
            //     dans ce stmt — le `}` arrivera plus tard dans un autre
            //     `<% } %>`). Detection via comptage de braces : si depth
            //     finale > 0, le stmt est un opener.
            //     Output : `@stmt` brut. Razor parse `@if (cond) { ...`
            //     comme un block dont le HTML qui suit fait partie du body.
            if (BlockKeyword.IsMatch(stmt) && IsBlockOpener(stmt))
            {
                controlCount++;
                return "@" + stmt;
            }

            // 3b. `<% if (cond) { body; } %>` ou variante multi-line ou le
            //     `<% %>` contient EXACTEMENT UNE structure de controle
            //     complete (pas plusieurs statements). Conversion direct
            //     en `@if/@foreach/etc.` sans wrapper `@{}`.
            if (IsSingleControlFlowStatement(stmt))
            {
                controlCount++;
                return "@" + stmt;
            }

            // 3c. `<% if (cond) body; %>` (sans accolades) → `@if (cond) { body; }`
            //     C# permet l'inline if sans accolades, Razor aussi mais c'est
            //     plus sur de wrapper le body. Heuristique : on force des
            //     accolades autour du body.
            if (TryWrapInlineControl(stmt, out var wrapped))
            {
                controlCount++;
                return wrapped;
            }

            // 4. Tout le reste (declarations, assignations, methods calls
            //    autonomes) → bloc `@{ }` classique.
            statementCount++;
            return $"@{{ {stmt} }}";
        });

        if (statementCount + controlCount + closingCount + emptyCount > 0)
        {
            var parts = new List<string>();
            if (statementCount > 0) parts.Add($"{statementCount} `<% stmt %>` → `@{{ stmt }}`");
            if (controlCount > 0)   parts.Add($"{controlCount} structures de controle → `@if/@foreach/etc.`");
            if (closingCount > 0)   parts.Add($"{closingCount} accolades de fermeture → `}}`");
            if (emptyCount > 0)     parts.Add($"{emptyCount} bloc(s) vide(s) supprime(s)");
            ctx.Log(MigrationSeverity.Auto, null, Name, string.Join(" ; ", parts) + ".");
        }
        return result;
    }

    /// <summary>
    /// Vrai si <paramref name="stmt"/> ouvre un block C# qui ne se ferme pas
    /// dans le stmt lui-meme (depth de braces > 0 a la fin). Signe que le
    /// `}` correspondant viendra dans un autre `&lt;% } %&gt;` plus loin —
    /// auquel cas il faut emettre `@stmt` brut (sans `@{ }` qui changerait
    /// la structure du block).
    ///
    /// Exemple : `if (cond)\n{\n    stmt1;\n    stmt2;` (ouvre `{`, ne
    /// ferme pas — le `}` est dans un `&lt;% } %&gt;` plus loin).
    /// </summary>
    internal static bool IsBlockOpener(string stmt)
    {
        int depth = 0;
        bool inStr = false, inChar = false, escaped = false;
        for (int i = 0; i < stmt.Length; i++)
        {
            char c = stmt[i];
            if (escaped) { escaped = false; continue; }
            if (c == '\\') { escaped = true; continue; }
            if (inStr)  { if (c == '"')  inStr = false; continue; }
            if (inChar) { if (c == '\'') inChar = false; continue; }
            if (c == '"')  { inStr = true; continue; }
            if (c == '\'') { inChar = true; continue; }
            if (c == '{') depth++;
            else if (c == '}') depth--;
        }
        return depth > 0;
    }

    /// <summary>
    /// Vrai si <paramref name="stmt"/> contient EXACTEMENT une seule
    /// structure de controle complete (pas plusieurs statements consecutifs).
    /// Utilise pour decider si on peut emettre `@if/@foreach/etc.` direct
    /// au lieu de `@{ stmt }`.
    ///
    /// Algorithme : parse le stmt en track-ant string/char literals, trouve
    /// le `{` initial puis son `}` correspondant. Si tout ce qui suit le
    /// `}` est du whitespace, le stmt est UNE seule structure.
    /// </summary>
    internal static bool IsSingleControlFlowStatement(string stmt)
    {
        if (!BlockKeyword.IsMatch(stmt)) return false;

        int parenDepth = 0;
        int startBrace = -1;
        bool inStr = false, inChar = false, escaped = false;
        for (int i = 0; i < stmt.Length; i++)
        {
            char c = stmt[i];
            if (escaped) { escaped = false; continue; }
            if (c == '\\') { escaped = true; continue; }
            if (inStr)  { if (c == '"')  inStr = false; continue; }
            if (inChar) { if (c == '\'') inChar = false; continue; }
            if (c == '"')  { inStr = true; continue; }
            if (c == '\'') { inChar = true; continue; }
            if (c == '(') parenDepth++;
            else if (c == ')') parenDepth--;
            else if (c == '{' && parenDepth == 0) { startBrace = i; break; }
        }
        if (startBrace < 0) return false;

        int depth = 1;
        int endBrace = -1;
        inStr = inChar = escaped = false;
        for (int i = startBrace + 1; i < stmt.Length; i++)
        {
            char c = stmt[i];
            if (escaped) { escaped = false; continue; }
            if (c == '\\') { escaped = true; continue; }
            if (inStr)  { if (c == '"')  inStr = false; continue; }
            if (inChar) { if (c == '\'') inChar = false; continue; }
            if (c == '"')  { inStr = true; continue; }
            if (c == '\'') { inChar = true; continue; }
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) { endBrace = i; break; }
            }
        }
        if (endBrace < 0) return false;

        // Apres la `}` finale, il ne doit y avoir que du whitespace.
        for (int i = endBrace + 1; i < stmt.Length; i++)
        {
            if (!char.IsWhiteSpace(stmt[i])) return false;
        }
        return true;
    }

    /// <summary>
    /// Essaie de detecter `<% if (cond) body; %>` (sans accolades, single-line)
    /// et le transformer en `@if (cond) { body; }`. Renvoie true si le pattern
    /// a matche.
    ///
    /// Restreint au cas single-line ET sans `{` du tout dans le body — sinon
    /// on risque de matcher un block multi-line ou seul le `{` ouvrant est
    /// dans le `<% %>` initial (le `}` fermant arrive plus loin via `<% } %>`).
    /// Pour ce cas multi-line, on laisse le bloc tomber dans `@{ stmt }`
    /// (comportement par defaut, valide).
    /// </summary>
    private static bool TryWrapInlineControl(string stmt, out string output)
    {
        output = "";
        // Garde-fous : single-line seulement, et pas de `{` dans le stmt.
        if (stmt.Contains('\n') || stmt.Contains('{')) return false;

        var m = Regex.Match(stmt,
            @"^\s*(if|else\s+if|for|foreach|while|using|lock)\s*\(([^)]*(?:\([^)]*\)[^)]*)*)\)\s*(.+?);?\s*$");
        if (!m.Success) return false;

        var keyword = m.Groups[1].Value;
        var cond    = m.Groups[2].Value;
        var body    = m.Groups[3].Value.TrimEnd(';').TrimEnd();
        if (string.IsNullOrWhiteSpace(body)) return false;
        output = $"@{keyword} ({cond}) {{ {body}; }}";
        return true;
    }
}
