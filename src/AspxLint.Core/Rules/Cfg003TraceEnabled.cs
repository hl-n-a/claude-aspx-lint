using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

/// <summary>
/// CFG-003 : &lt;trace enabled="true"&gt; dans Web.config.
/// Trace.axd expose des informations sensibles (cookies, session, server vars,
/// stack frames) au public si pas restreint. Souvent oublie active en prod.
/// Auto-fix : passe a "false".
/// </summary>
public sealed class Cfg003TraceEnabled : IRule
{
    public string Id => "CFG-003";
    public string Name => "trace enabled=\"true\" en Web.config";
    public Severity Severity => Severity.Info;
    public string Description =>
        "trace enabled=\"true\" expose Trace.axd avec des donnees de session/cookies/server variables. A retirer en production sauf si explicitement protege par localOnly + restriction reseau.";
    public bool HasFix => true;

    private static readonly Regex DetectRegex = new(
        @"<trace\b[^>]*\benabled\s*=\s*[""']true[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        if (ctx.Ext != "config") yield break;
        for (int i = 0; i < lines.Length; i++)
        {
            foreach (Match m in DetectRegex.Matches(lines[i]))
            {
                yield return new Issue(Id, Name, Severity,
                    i + 1, m.Index + 1, m.Value,
                    "Passer enabled=\"false\" en production.");
            }
        }
    }

    public string? Fix(string content, RuleContext ctx)
    {
        if (ctx.Ext != "config") return content;
        return Regex.Replace(content,
            @"(<trace\b[^>]*\benabled\s*=\s*[""'])true([""'])",
            "$1false$2",
            RegexOptions.IgnoreCase);
    }
}
