using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

/// <summary>
/// CFG-005 : &lt;sessionState mode="InProc"&gt; en Web.config.
/// InProc stocke la session dans la memoire du process IIS — perdu a chaque
/// recyclage du pool, ne scale pas en multi-instance, casse les load balancers
/// sans sticky sessions. Pour une vraie app multi-instance, utiliser StateServer
/// ou SQLServer.
/// Manuel : le bon mode depend de l'infra (Redis, SQL, StateServer...).
/// </summary>
public sealed class Cfg005SessionStateInProc : IRule
{
    public string Id => "CFG-005";
    public string Name => "sessionState mode=\"InProc\" ne scale pas";
    public Severity Severity => Severity.Info;
    public string Description =>
        "InProc stocke la session dans la memoire du process IIS : perdue au recyclage du pool, incompatible avec le multi-instance. Pour une vraie app multi-instance utiliser StateServer ou SQLServer (Redis via custom provider).";
    public bool HasFix => false;

    private static readonly Regex DetectRegex = new(
        @"<sessionState\b[^>]*\bmode\s*=\s*[""']InProc[""']",
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
                    "Choisir StateServer ou SQLServer pour le multi-instance.");
            }
        }
    }

    public string? Fix(string content, RuleContext ctx) => null;
}
