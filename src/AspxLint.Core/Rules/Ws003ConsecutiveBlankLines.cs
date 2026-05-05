using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class Ws003ConsecutiveBlankLines : IRule
{
    public string Id => "WS-003";
    public string Name => "Lignes vides multiples (>2)";
    public Severity Severity => Severity.Info;
    public string Description =>
        "Plus de 2 lignes vides consecutives degradent la lisibilite sans apporter de separation visuelle utile.";
    public bool HasFix => true;

    // (\r?\n[ \t]*){3,}  — au moins 3 sauts de ligne entoures de blancs.
    private static readonly Regex FixRegex = new(@"(\r?\n[ \t]*){3,}", RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        int consec = 0;
        int startLine = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                if (consec == 0) startLine = i + 1;
                consec++;
            }
            else
            {
                if (consec > 2)
                    yield return new Issue(Id, Name, Severity,
                        startLine, 1,
                        $"{consec} lignes vides consecutives",
                        "Reduire a au maximum 2 lignes vides.");
                consec = 0;
            }
        }
        if (consec > 2)
            yield return new Issue(Id, Name, Severity,
                startLine, 1,
                $"{consec} lignes vides en fin de fichier",
                "Reduire a au maximum 2 lignes vides.");
    }

    public string? Fix(string content, RuleContext ctx) => FixRegex.Replace(content, "\n\n\n");
}
