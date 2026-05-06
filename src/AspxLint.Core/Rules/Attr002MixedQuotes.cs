using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class Attr002MixedQuotes : IRule
{
    public string Id => "ATTR-002";
    public string Name => "Melange guillemets simples / doubles";
    public Severity Severity => Severity.Info;
    public string Description =>
        "Pour la coherence du code, utilisez systematiquement les guillemets doubles (\") pour les attributs HTML/ASP.NET. Les guillemets simples sont valides mais inhabituels.";
    public bool HasFix => true;

    private static readonly Regex SingleQuotedAttr =
        new(@"\s([a-zA-Z][a-zA-Z0-9\-:_]*)='([^']*)'", RegexOptions.Compiled);

    private static readonly Regex TagWithAttrs =
        new(@"(<[a-zA-Z][a-zA-Z0-9:_\-]*\b[^>]*?)>", RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        // Mask <% ... %> globalement : les ' sont legitimes en C# pour
        // les chaines, donc on ignore tout ce qui est dans un bloc serveur.
        var (_, maskedLines) = RuleHelpers.MaskAndSplit(content);

        for (int i = 0; i < maskedLines.Length; i++)
        {
            var line = maskedLines[i];

            foreach (Match m in SingleQuotedAttr.Matches(line))
            {
                var before = line[..m.Index];
                var lastOpen = before.LastIndexOf('<');
                var lastClose = before.LastIndexOf('>');
                if (lastOpen <= lastClose) continue;

                yield return new Issue(Id, Name, Severity,
                    i + 1, m.Index + 1, m.Value.Trim(),
                    $"Preferer \"{m.Groups[1].Value}=\\\"{m.Groups[2].Value}\\\"\"");
            }
        }
    }

    public string? Fix(string content, RuleContext ctx) =>
        RuleHelpers.ApplyOutsideAspBlocks(content, segment =>
            TagWithAttrs.Replace(segment, m =>
            {
                var body = m.Groups[1].Value;
                var fixedBody = SingleQuotedAttr.Replace(body, mm =>
                    $" {mm.Groups[1].Value}=\"{mm.Groups[2].Value.Replace("\"", "&quot;")}\"");
                return fixedBody + ">";
            }));
}
