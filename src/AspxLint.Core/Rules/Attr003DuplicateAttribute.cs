using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class Attr003DuplicateAttribute : IRule
{
    public string Id => "ATTR-003";
    public string Name => "Attributs en double dans une balise";
    public Severity Severity => Severity.Error;
    public string Description =>
        "Une meme balise ne doit pas contenir deux fois le meme attribut. Le navigateur ne conserve generalement que le premier, ce qui cause des bugs subtils.";
    public bool HasFix => false;

    private static readonly Regex TagRegex =
        new(@"<([a-zA-Z][a-zA-Z0-9:_\-]*)\b([^>]*?)>", RegexOptions.Compiled);

    private static readonly Regex AttrNameRegex =
        new(@"\s([a-zA-Z][a-zA-Z0-9\-:_]*)\s*=", RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            foreach (Match m in TagRegex.Matches(lines[i]))
            {
                var attrs = m.Groups[2].Value;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (Match am in AttrNameRegex.Matches(attrs))
                {
                    var name = am.Groups[1].Value;
                    if (!seen.Add(name))
                    {
                        var snippet = m.Value.Length > 80 ? m.Value[..80] + "…" : m.Value;
                        yield return new Issue(Id, Name, Severity,
                            i + 1, m.Index + 1, snippet,
                            $"L'attribut \"{name}\" apparait plusieurs fois dans cette balise.");
                        break; // un par balise suffit
                    }
                }
            }
        }
    }

    public string? Fix(string content, RuleContext ctx) => null;
}
