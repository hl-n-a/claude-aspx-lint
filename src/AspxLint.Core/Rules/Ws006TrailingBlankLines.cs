using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class Ws006TrailingBlankLines : IRule
{
    public string Id => "WS-006";
    public string Name => "Lignes vides en fin de fichier";
    public Severity Severity => Severity.Info;
    public string Description =>
        "Un fichier doit se terminer par une seule ligne newline (POSIX), sans lignes vides surnumeraires. WS-006 cible les blank lines a la toute fin du fichier ; WS-003 vise les blocs de >2 lignes vides au milieu, et WS-004 garantit qu'il y a bien un \\n final.";
    public bool HasFix => true;

    /// <summary>
    /// 2+ newlines (avec eventuellement du whitespace entre) en fin de chaine.
    /// Ne se declenche pas pour 1 seul \n final (qui est le bon comportement).
    /// </summary>
    private static readonly Regex TrailingBlanks = new(
        @"(\r?\n[ \t]*){2,}\z", RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        var m = TrailingBlanks.Match(content);
        if (!m.Success) yield break;

        // Compte le nombre de "lignes vides surnumeraires" : chaque \r?\n
        // au-dela du premier represente une ligne vide.
        int newlineCount = 0;
        foreach (var c in m.Value) if (c == '\n') newlineCount++;
        if (newlineCount < 2) yield break;
        var blankCount = newlineCount - 1;

        // Position : derniere ligne non-vide + 1.
        int lastNonBlank = lines.Length - 1;
        while (lastNonBlank >= 0 && string.IsNullOrWhiteSpace(lines[lastNonBlank])) lastNonBlank--;

        yield return new Issue(Id, Name, Severity,
            Math.Max(1, lastNonBlank + 2), 1,
            $"{blankCount} ligne(s) vide(s) en fin de fichier",
            "Supprimer les lignes vides en fin de fichier (1 seul \\n final suffit).");
    }

    public string? Fix(string content, RuleContext ctx)
    {
        // Remplace les newlines surnumeraires en fin par un seul \n.
        // Si le contenu ne se terminait pas par \n du tout, WS-004 s'en charge.
        var fixedContent = TrailingBlanks.Replace(content, "\n");
        return fixedContent;
    }
}
