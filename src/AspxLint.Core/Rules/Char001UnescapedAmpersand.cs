using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class Char001UnescapedAmpersand : IRule
{
    public string Id => "CHAR-001";
    public string Name => "& non echappe en dehors d'une entite";
    public Severity Severity => Severity.Warning;
    public string Description =>
        "Le caractere & doit etre encode en &amp; sauf s'il commence une entite (&lt;, &amp;, &#123;...) ou un parametre d'URL (?a=1&b=2). HTML5 tolere & dans les URL ; XHTML strict l'interdit. Cette regle suit le compromis HTML5 — un `&` est ignore s'il est suivi de `\\w+=` (forme typique d'un parametre).";
    public bool HasFix => false;

    // `&` non suivi d'une entite valide ET non suivi d'une forme `name=` (URL param).
    // Le nom du param accepte hyphen et underscore (cas `data-url`, `item-url`,
    // `Content-Type` qui apparaissent dans les URLs de tracking / API).
    // Double lookahead negatif : evite les faux positifs sur les URLs courantes
    // comme `?a=1&b=2&kaClkId=42&item-url=...` qui sont valides en HTML5.
    private static readonly Regex DetectRegex = new(
        @"&(?!(?:[a-zA-Z][a-zA-Z0-9]{1,8}|#\d+|#x[0-9a-fA-F]+);)(?![\w-]+\s*=)",
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
