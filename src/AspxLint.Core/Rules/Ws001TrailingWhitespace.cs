using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class Ws001TrailingWhitespace : IRule
{
    public string Id => "WS-001";
    public string Name => "Espaces en fin de ligne";
    public Severity Severity => Severity.Info;
    public string Description =>
        "Les espaces ou tabulations en fin de ligne polluent les diffs Git et n'apportent rien de visuel. Ils doivent etre supprimes.";
    public bool HasFix => true;

    private static readonly Regex Trailing = new(@"[ \t]+$", RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var m = Trailing.Match(line);
            if (!m.Success) continue;

            var col = line.Length - m.Length + 1;
            var snippet = line.Length > 60
                ? "…" + line[^Math.Min(30, line.Length)..] + "⎵"
                : line + "⎵";

            yield return new Issue(
                Id, Name, Severity,
                i + 1, col, snippet,
                $"Supprimer les {m.Length} caractere(s) blanc(s) en fin de ligne."
            );
        }
    }

    public string? Fix(string content, RuleContext ctx)
    {
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        for (int i = 0; i < lines.Length; i++)
            lines[i] = Trailing.Replace(lines[i], "");
        return string.Join("\n", lines);
    }
}
