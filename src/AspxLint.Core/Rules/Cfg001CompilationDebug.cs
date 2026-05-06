using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

/// <summary>
/// CFG-001 : &lt;compilation debug="true"&gt; dans Web.config en production.
/// Mode debug active = fichiers PDB charges, code non optimise, scripts non
/// minifies, requests non timeoutees. Combine a customErrors=Off c'est une
/// surface d'attaque + perf catastrophique. Auto-fix : passe a "false".
/// </summary>
public sealed class Cfg001CompilationDebug : IRule
{
    public string Id => "CFG-001";
    public string Name => "compilation debug=\"true\" en Web.config";
    public Severity Severity => Severity.Warning;
    public string Description =>
        "Le mode debug=\"true\" desactive l'optimisation de code, charge les PDB, prolonge les timeouts et expose des stack traces. A laisser a false en production.";
    public bool HasFix => true;

    private static readonly Regex DetectRegex = new(
        @"<compilation\b[^>]*\bdebug\s*=\s*[""']true[""']",
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
                    "Passer debug=\"false\" pour la production (use Web.Debug.config pour debug local).");
            }
        }
    }

    public string? Fix(string content, RuleContext ctx)
    {
        if (ctx.Ext != "config") return content;
        return Regex.Replace(content,
            @"(<compilation\b[^>]*\bdebug\s*=\s*[""'])true([""'])",
            "$1false$2",
            RegexOptions.IgnoreCase);
    }
}
