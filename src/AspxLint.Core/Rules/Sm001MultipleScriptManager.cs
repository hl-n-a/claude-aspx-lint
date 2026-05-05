using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class Sm001MultipleScriptManager : IRule
{
    public string Id => "SM-001";
    public string Name => "Plusieurs ScriptManager dans la page";
    public Severity Severity => Severity.Error;
    public string Description =>
        "Une page ASP.NET ne peut contenir qu'un seul <asp:ScriptManager>. Pour les content pages avec un master ayant deja un ScriptManager, utiliser <asp:ScriptManagerProxy>.";
    public bool HasFix => false;

    private static readonly Regex DetectRegex =
        new(@"<asp:ScriptManager\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        var matches = new List<(int line, int col, string snippet)>();
        for (int i = 0; i < lines.Length; i++)
        {
            foreach (Match m in DetectRegex.Matches(lines[i]))
            {
                var rest = lines[i][m.Index..];
                var endTag = rest.IndexOf('>');
                var snippet = endTag >= 0 ? rest[..(endTag + 1)] : rest;
                matches.Add((i + 1, m.Index + 1, snippet));
            }
        }

        if (matches.Count <= 1) yield break;

        var firstLine = matches[0].line;
        for (int k = 1; k < matches.Count; k++)
        {
            var mm = matches[k];
            yield return new Issue(Id, Name, Severity,
                mm.line, mm.col, mm.snippet,
                $"Un ScriptManager existe deja ligne {firstLine}. Utiliser ScriptManagerProxy ici.");
        }
    }

    public string? Fix(string content, RuleContext ctx) => null;
}
