using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class Style001InlineStyle : IRule
{
    public string Id => "STYLE-001";
    public string Name => "Attribut style=\"...\" inline";
    public Severity Severity => Severity.Info;
    public string Description =>
        "L'utilisation d'un attribut style=\"...\" inline melange presentation et structure, contourne le CSS du projet et complique le theming / la maintenance. Pas d'auto-fix : extraire les regles dans une feuille de style demande une decision humaine sur le nommage de la classe.";
    public bool HasFix => false;

    /// <summary>
    /// Match `style="..."` ou `style='...'` dans une balise. On exige `\s` ou `<`
    /// avant pour eviter de matcher `data-style=`, `text-style=`, etc.
    /// </summary>
    private static readonly Regex DetectRegex = new(
        @"(?<=[\s])style\s*=\s*([""'])(?<value>[^""']+)\1",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        var (_, maskedLines) = RuleHelpers.MaskAndSplit(content);
        for (int i = 0; i < maskedLines.Length; i++)
        {
            foreach (Match m in DetectRegex.Matches(maskedLines[i]))
            {
                var val = m.Groups["value"].Value;
                yield return new Issue(Id, Name, Severity,
                    i + 1, m.Index + 1,
                    m.Value.Length > 80 ? m.Value[..80] + "…" : m.Value,
                    $"Extraire ces declarations CSS dans une classe : {Snip(val, 40)}");
            }
        }
    }

    private static string Snip(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen] + "…";

    public string? Fix(string content, RuleContext ctx) => null;
}
