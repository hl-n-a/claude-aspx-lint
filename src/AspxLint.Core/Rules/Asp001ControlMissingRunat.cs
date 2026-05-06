using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class Asp001ControlMissingRunat : IRule
{
    public string Id => "ASP-001";
    public string Name => "Controle serveur sans runat=\"server\"";
    public Severity Severity => Severity.Error;
    public string Description =>
        "Tout controle <asp:...> doit avoir l'attribut runat=\"server\", sinon il est traite comme du HTML litteral et n'a aucun comportement serveur.";
    public bool HasFix => true;

    // Detect sur contenu MASQUE : [^>] suffit.
    private static readonly Regex DetectRegex = new(
        @"<asp:([a-zA-Z]+)\b([^>]*?)/?>",
        RegexOptions.Compiled);

    // Fix sur contenu BRUT : la regex doit traverser les <%...%> en attribut.
    private static readonly Regex FixRegex = new(
        $@"<asp:([a-zA-Z]+)\b({RuleHelpers.TagInnerPattern})(\s*/?)>",
        RegexOptions.Compiled);

    private static readonly Regex RunatPresent = new(
        @"\brunat\s*=\s*[""']?server[""']?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        // Mask <%...%> globalement pour ne pas matcher des `<asp:...>` qui
        // seraient ecrits litteralement dans une chaine C# embarquee.
        var (_, maskedLines) = RuleHelpers.MaskAndSplit(content);

        for (int i = 0; i < maskedLines.Length; i++)
        {
            foreach (Match m in DetectRegex.Matches(maskedLines[i]))
            {
                if (RunatPresent.IsMatch(m.Groups[2].Value)) continue;
                yield return new Issue(Id, Name, Severity,
                    i + 1, m.Index + 1, m.Value,
                    $"Ajouter runat=\"server\" au controle <asp:{m.Groups[1].Value}>");
            }
        }
    }

    public string? Fix(string content, RuleContext ctx) =>
        RuleHelpers.FixOutsideAspBlocks(content, FixRegex, m =>
        {
            var attrs = m.Groups[2].Value;
            if (RunatPresent.IsMatch(attrs)) return m.Value;
            var cleanAttrs = attrs.TrimEnd();
            var tail = m.Groups[3].Value.Contains('/') ? " />" : ">";
            return $"<asp:{m.Groups[1].Value}{cleanAttrs} runat=\"server\"{tail}";
        });
}
