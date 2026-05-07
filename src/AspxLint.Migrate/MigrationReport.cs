using System.Text;

namespace AspxLint.Migrate;

/// <summary>
/// Severite d'une action de migration. Pour le rapport et pour decider si
/// la migration peut etre consideree "complete" ou si elle a besoin d'une
/// passe humaine.
/// </summary>
public enum MigrationSeverity
{
    /// <summary>Transformation automatique reussie, aucun avertissement.</summary>
    Auto,
    /// <summary>Transformation appliquee mais semantique potentiellement
    /// differente — valider apres coup (ex : `&lt;%= %&gt;` qui n'echappait
    /// pas devient `@` qui echappe par defaut).</summary>
    Warning,
    /// <summary>Pas de transformation automatique possible. Un commentaire
    /// `@* TODO[aspx-migrate] *@` est insere dans le fichier de sortie.</summary>
    Manual
}

/// <summary>
/// Une action enregistree pendant la migration. Genere une ligne du rapport
/// markdown final.
/// </summary>
public sealed record MigrationAction(
    MigrationSeverity Severity,
    string SourceFile,         // path relatif du .aspx d'origine
    int? Line,                 // ligne 1-based dans le source, null si global
    string Transformer,        // ex : "PageDirective"
    string Message             // description humaine
);

/// <summary>
/// Collecte des actions de migration. Genere un rapport markdown
/// agrege a la fin (un fichier .md ou la sortie console).
/// </summary>
public sealed class MigrationReport
{
    private readonly List<MigrationAction> _actions = new();

    public IReadOnlyList<MigrationAction> Actions => _actions;

    public void Add(MigrationAction action) => _actions.Add(action);

    public void Add(MigrationSeverity severity, string sourceFile, int? line, string transformer, string message)
        => _actions.Add(new MigrationAction(severity, sourceFile, line, transformer, message));

    public int CountBySeverity(MigrationSeverity s) => _actions.Count(a => a.Severity == s);

    /// <summary>
    /// Rapport markdown agrege : sommaire + tableau detaille groupe par
    /// fichier source. Concu pour etre lisible en console et copie-colle
    /// dans une PR description.
    /// </summary>
    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# aspx-lint migrate — rapport");
        sb.AppendLine();
        sb.AppendLine($"- **{CountBySeverity(MigrationSeverity.Auto)}** transformations automatiques");
        sb.AppendLine($"- **{CountBySeverity(MigrationSeverity.Warning)}** transformations avec avertissement");
        sb.AppendLine($"- **{CountBySeverity(MigrationSeverity.Manual)}** items necessitant une intervention manuelle");
        sb.AppendLine();

        var byFile = _actions.GroupBy(a => a.SourceFile).OrderBy(g => g.Key);
        foreach (var group in byFile)
        {
            sb.AppendLine($"## `{group.Key}`");
            sb.AppendLine();
            sb.AppendLine("| Ligne | Niveau | Transformer | Message |");
            sb.AppendLine("|------:|:-------|:------------|:--------|");
            foreach (var a in group.OrderBy(x => x.Line ?? 0))
            {
                var line = a.Line?.ToString() ?? "—";
                var sev = a.Severity switch
                {
                    MigrationSeverity.Auto    => "auto",
                    MigrationSeverity.Warning => "⚠ warning",
                    MigrationSeverity.Manual  => "✋ manual",
                    _ => a.Severity.ToString()
                };
                sb.AppendLine($"| {line} | {sev} | {a.Transformer} | {EscapeMd(a.Message)} |");
            }
            sb.AppendLine();
        }

        if (_actions.Count == 0)
        {
            sb.AppendLine("_(aucune transformation appliquee)_");
        }
        return sb.ToString();
    }

    private static string EscapeMd(string s)
        => s.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
}
