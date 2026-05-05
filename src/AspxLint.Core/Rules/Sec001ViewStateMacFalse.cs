using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class Sec001ViewStateMacFalse : IRule
{
    public string Id => "SEC-001";
    public string Name => "EnableViewStateMac=\"false\" — risque de securite";
    public Severity Severity => Severity.Error;
    public string Description =>
        "Desactiver EnableViewStateMac expose a des attaques par injection de ViewState. Cette option ne doit jamais etre a false en production.";
    public bool HasFix => true;

    private static readonly Regex DetectRegex = new(
        @"EnableViewStateMac\s*=\s*[""']?false[""']?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            foreach (Match m in DetectRegex.Matches(lines[i]))
            {
                yield return new Issue(Id, Name, Severity,
                    i + 1, m.Index + 1, m.Value,
                    "Retirer ou repasser EnableViewStateMac a true.");
            }
        }
    }

    public string? Fix(string content, RuleContext ctx) =>
        DetectRegex.Replace(content, "EnableViewStateMac=\"true\"");
}
