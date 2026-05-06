using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class A11y001ImgWithoutAlt : IRule
{
    public string Id => "A11Y-001";
    public string Name => "Balise <img> sans attribut alt";
    public Severity Severity => Severity.Warning;
    public string Description =>
        "Toute balise <img> doit avoir un attribut alt, meme vide (alt=\"\") pour les images decoratives. C'est requis par WCAG / WAI-ARIA et par les lecteurs d'ecran. Sans alt, l'image n'a aucune description pour les utilisateurs malvoyants. Pas d'auto-fix : choisir un alt approprie est une decision humaine — `alt=\"\"` n'est correct que si l'image est purement decorative.";
    public bool HasFix => false;

    private static readonly Regex DetectRegex = new(
        $@"<img\b({RuleHelpers.TagInnerPattern})/?>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AltAttr = new(
        @"\balt\s*=", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        var (_, maskedLines) = RuleHelpers.MaskAndSplit(content);
        for (int i = 0; i < maskedLines.Length; i++)
        {
            foreach (Match m in DetectRegex.Matches(maskedLines[i]))
            {
                var attrs = m.Groups[1].Value;
                if (AltAttr.IsMatch(attrs)) continue;
                yield return new Issue(Id, Name, Severity,
                    i + 1, m.Index + 1, m.Value,
                    "Ajouter alt=\"description\" (ou alt=\"\" si l'image est purement decorative).");
            }
        }
    }

    public string? Fix(string content, RuleContext ctx) => null;
}
