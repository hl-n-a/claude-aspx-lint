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

    // Detection : balises void sans / avant le >.
    private static readonly Regex DetectRegex = new(
        $@"<({VoidTagAlternation})(\s[^>]*?)?(?<!/)>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Fix : meme chose mais on capture aussi un eventuel whitespace avant > pour le re-emettre proprement.
    private static readonly Regex FixRegex = new(
        $@"<({VoidTagAlternation})((?:\s[^>]*?)?)(?<!/)\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            foreach (Match m in DetectRegex.Matches(lines[i]))
            {
                if (m.Value.EndsWith("/>")) continue; // garde-fou
                var fixedTag = m.Value[..^1].TrimEnd() + " />";
                yield return new Issue(Id, Name, Severity,
                    i + 1, m.Index + 1,
                    m.Value,
                    $"Remplacer par \"{fixedTag}\".");
            }
        }
    }

    public string? Fix(string content, RuleContext ctx) =>
        FixRegex.Replace(content, m => $"<{m.Groups[1].Value}{m.Groups[2].Value} />");
}
