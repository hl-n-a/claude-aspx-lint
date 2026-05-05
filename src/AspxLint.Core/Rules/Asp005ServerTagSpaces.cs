using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class Asp005ServerTagSpaces : IRule
{
    public string Id => "ASP-005";
    public string Name => "Espace dans la directive ASP <% %>";
    public Severity Severity => Severity.Warning;
    public string Description =>
        "Pour la lisibilite, un espace est recommande apres <% et avant %> dans les blocs de code serveur (<%= valeur %> plutot que <%=valeur%>).";
    public bool HasFix => true;

    private static readonly Regex DetectRegex =
        new(@"<%[=#:]?[^\s][^%]*?[^\s]%>", RegexOptions.Compiled);

    private static readonly Regex FixRegex =
        new(@"<%([=#:]?)([^\s%][^%]*?[^\s%])%>", RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            foreach (Match m in DetectRegex.Matches(lines[i]))
            {
                if (m.Value.Contains('@')) continue; // <%@ Page ... %> et autres directives
                yield return new Issue(Id, Name, Severity,
                    i + 1, m.Index + 1, m.Value,
                    "Espace recommande apres <% et avant %>.");
            }
        }
    }

    public string? Fix(string content, RuleContext ctx) =>
        FixRegex.Replace(content, m =>
            m.Value.Contains('@')
                ? m.Value
                : $"<%{m.Groups[1].Value} {m.Groups[2].Value} %>");
}
