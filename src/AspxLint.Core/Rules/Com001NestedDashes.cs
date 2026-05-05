using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class Com001NestedDashes : IRule
{
    public string Id => "COM-001";
    public string Name => "Commentaire HTML imbrique (-- dans <!-- -->)";
    public Severity Severity => Severity.Warning;
    public string Description =>
        "La sequence -- ne doit pas apparaitre a l'interieur d'un commentaire HTML <!-- -->. Cela invalide le commentaire selon la spec XHTML.";
    public bool HasFix => false;

    private static readonly Regex HtmlComment =
        new(@"<!--([\s\S]*?)-->", RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        // Pre-calcul des offsets de debut de ligne.
        var lineStarts = new int[lines.Length];
        int pos = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            lineStarts[i] = pos;
            pos += lines[i].Length + 1;
        }

        foreach (Match m in HtmlComment.Matches(content))
        {
            if (!m.Groups[1].Value.Contains("--")) continue;
            var snippet = m.Value.Length > 60 ? m.Value[..60] + "…" : m.Value;
            yield return new Issue(Id, Name, Severity,
                LineFromOffset(m.Index, lineStarts), 1, snippet,
                "Le commentaire contient \"--\", ce qui est invalide en XHTML strict.");
        }
    }

    public string? Fix(string content, RuleContext ctx) => null;

    private static int LineFromOffset(int offset, int[] lineStarts)
    {
        int lo = 0, hi = lineStarts.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (lineStarts[mid] <= offset) lo = mid;
            else hi = mid - 1;
        }
        return lo + 1;
    }
}
