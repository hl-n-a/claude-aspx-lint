using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class Asp002DuplicateControlId : IRule
{
    public string Id => "ASP-002";
    public string Name => "ID de controle serveur duplique";
    public Severity Severity => Severity.Error;
    public string Description =>
        "Deux controles serveur dans la meme page (hors templates repetes) ne peuvent pas avoir le meme ID — cela provoque une erreur de compilation ASP.NET.";
    public bool HasFix => false;

    // Capture <... ID="xxx" ... runat="server" ...>
    private static readonly Regex ControlWithIdRegex = new(
        @"<(asp:[a-zA-Z]+|[a-zA-Z]+)\b[^>]*\bID\s*=\s*[""']([^""']+)[""'][^>]*\brunat\s*=\s*[""']?server[""']?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int i = 0; i < lines.Length; i++)
        {
            foreach (Match m in ControlWithIdRegex.Matches(lines[i]))
            {
                var id = m.Groups[2].Value;
                if (seen.TryGetValue(id, out var firstLine))
                {
                    var snippet = m.Value.Length > 80 ? m.Value[..80] + "…" : m.Value;
                    yield return new Issue(Id, Name, Severity,
                        i + 1, m.Index + 1, snippet,
                        $"ID \"{id}\" deja utilise ligne {firstLine}.");
                }
                else
                {
                    seen[id] = i + 1;
                }
            }
        }
    }

    public string? Fix(string content, RuleContext ctx) => null;
}
