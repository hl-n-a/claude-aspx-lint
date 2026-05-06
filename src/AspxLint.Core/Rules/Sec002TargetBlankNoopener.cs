using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class Sec002TargetBlankNoopener : IRule
{
    public string Id => "SEC-002";
    public string Name => "Lien target=\"_blank\" sans rel=\"noopener\"";
    public Severity Severity => Severity.Warning;
    public string Description =>
        "Un lien <a target=\"_blank\"> permet a la page ouverte d'acceder a window.opener et de manipuler la page d'origine (faille tabnabbing). Le fix automatique ajoute rel=\"noopener noreferrer\" si l'attribut rel est absent ou ne contient pas deja un de ces tokens.";
    public bool HasFix => true;

    private static readonly Regex DetectRegex = new(
        $@"<a\b({RuleHelpers.TagInnerPattern})>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TargetBlank = new(
        @"\btarget\s*=\s*[""']?_blank[""']?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RelAttr = new(
        @"\brel\s*=\s*([""'])([^""']*)\1",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        var (_, maskedLines) = RuleHelpers.MaskAndSplit(content);

        for (int i = 0; i < maskedLines.Length; i++)
        {
            foreach (Match m in DetectRegex.Matches(maskedLines[i]))
            {
                var attrs = m.Groups[1].Value;
                if (!TargetBlank.IsMatch(attrs)) continue;
                if (HasNoopenerToken(attrs)) continue;

                yield return new Issue(Id, Name, Severity,
                    i + 1, m.Index + 1, m.Value,
                    "Ajouter rel=\"noopener noreferrer\" sur le lien target=\"_blank\".");
            }
        }
    }

    public string? Fix(string content, RuleContext ctx) =>
        RuleHelpers.FixOutsideAspBlocks(content, DetectRegex, m =>
        {
            var attrs = m.Groups[1].Value;
            if (!TargetBlank.IsMatch(attrs)) return m.Value;
            if (HasNoopenerToken(attrs)) return m.Value;

            // Si rel="..." existe deja sans noopener/noreferrer -> on enrichit.
            // Sinon on append rel="noopener noreferrer".
            var relMatch = RelAttr.Match(attrs);
            string newAttrs;
            if (relMatch.Success)
            {
                var existing = relMatch.Groups[2].Value.Trim();
                var merged = string.IsNullOrEmpty(existing)
                    ? "noopener noreferrer"
                    : $"{existing} noopener noreferrer";
                newAttrs = attrs.Substring(0, relMatch.Index)
                    + $"rel=\"{merged}\""
                    + attrs.Substring(relMatch.Index + relMatch.Length);
            }
            else
            {
                newAttrs = attrs.TrimEnd() + " rel=\"noopener noreferrer\"";
            }
            return $"<a{newAttrs}>";
        });

    private static bool HasNoopenerToken(string attrs)
    {
        var rel = RelAttr.Match(attrs);
        if (!rel.Success) return false;
        var tokens = rel.Groups[2].Value
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Any(t => t.Equals("noopener", StringComparison.OrdinalIgnoreCase)
                            || t.Equals("noreferrer", StringComparison.OrdinalIgnoreCase));
    }
}
