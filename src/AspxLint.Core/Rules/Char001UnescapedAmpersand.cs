using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class Char001UnescapedAmpersand : IRule
{
    public string Id => "CHAR-001";
    public string Name => "& non echappe en dehors d'une entite";
    public Severity Severity => Severity.Warning;
    public string Description =>
        "Le caractere & doit etre encode en &amp; sauf s'il commence une entite (&lt;, &amp;, &#123;...). Sinon, validation XHTML invalide.";
    public bool HasFix => false;

    // & non suivi d'une entite valide (lookahead negatif).
    private static readonly Regex DetectRegex = new(
        @"&(?!(?:[a-zA-Z][a-zA-Z0-9]{1,8}|#\d+|#x[0-9a-fA-F]+);)",
        RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        // Mask les blocs <% ... %>, <script>, <style>, et commentaires HTML
        // <!-- -->. Pour CHAR-001 ces zones ne sont pas du contenu HTML : `&&`
        // dans du JS ou du C# n'a aucune raison d'etre encode en `&amp;`.
        var (_, maskedLines) = RuleHelpers.MaskAndSplitFull(content);

        for (int i = 0; i < maskedLines.Length; i++)
        {
            var line = maskedLines[i];
            foreach (Match m in DetectRegex.Matches(line))
            {
                var from = Math.Max(0, m.Index - 5);
                var to = Math.Min(line.Length, m.Index + 10);
                yield return new Issue(Id, Name, Severity,
                    i + 1, m.Index + 1, line[from..to],
                    "Encoder ce \"&\" en \"&amp;\" si dans du contenu HTML.");
            }
        }
    }

    public string? Fix(string content, RuleContext ctx) => null;
}
