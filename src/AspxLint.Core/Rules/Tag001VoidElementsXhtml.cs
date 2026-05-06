using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class Tag001VoidElementsXhtml : IRule
{
    public string Id => "TAG-001";
    public string Name => "Balise auto-fermante non conforme XHTML";
    public Severity Severity => Severity.Warning;
    public string Description =>
        "Les balises vides comme <br>, <hr>, <img>, <input>, <meta>, <link> doivent s'ecrire en auto-fermantes (ex: <br />) pour respecter la conformite XHTML utilisee par les pages ASP.NET Web Forms.";
    public bool HasFix => true;

    private const string VoidTagAlternation =
        "br|hr|img|input|meta|link|area|base|col|embed|source|track|wbr";

    // Pour Detect : on travaille sur un contenu MASQUE (les blocs <% %> sont
    // remplaces par des espaces), donc une regex simple [^>] suffit.
    private static readonly Regex DetectRegex = new(
        $@"<({VoidTagAlternation})(\s[^>]*?)?(?<!/)>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Pour Fix : on opere sur le contenu BRUT, donc le pattern doit savoir
    // traverser les blocs <%...%> en attribut (ex: <input value="<%= x %>">),
    // sinon le `>` du `%>` est pris pour la fin du tag.
    private static readonly Regex FixRegex = new(
        $@"<({VoidTagAlternation})((?:\s{RuleHelpers.TagInnerPattern})?)(?<!/)\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        // Mask <%...%> globalement pour ne pas detecter de tags HTML
        // qui seraient incorpores dans une chaine C# (rare mais possible).
        var (_, maskedLines) = RuleHelpers.MaskAndSplit(content);

        for (int i = 0; i < maskedLines.Length; i++)
        {
            foreach (Match m in DetectRegex.Matches(maskedLines[i]))
            {
                if (m.Value.EndsWith("/>")) continue;
                var fixedTag = m.Value[..^1].TrimEnd() + " />";
                yield return new Issue(Id, Name, Severity,
                    i + 1, m.Index + 1,
                    m.Value,
                    $"Remplacer par \"{fixedTag}\".");
            }
        }
    }

    public string? Fix(string content, RuleContext ctx) =>
        RuleHelpers.FixOutsideAspBlocks(content, FixRegex,
            m => $"<{m.Groups[1].Value}{m.Groups[2].Value} />");
}
