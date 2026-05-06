using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

/// <summary>
/// CFG-002 : &lt;customErrors mode="Off"&gt; dans Web.config.
/// En "Off", les pages d'erreur YSOD (Yellow Screen Of Death) leak des
/// stack traces, des chemins disque, parfois des connection strings.
/// Auto-fix : passe a "RemoteOnly" (debug en local, page generique en remote).
/// </summary>
public sealed class Cfg002CustomErrorsOff : IRule
{
    public string Id => "CFG-002";
    public string Name => "customErrors mode=\"Off\" expose des stack traces";
    public Severity Severity => Severity.Warning;
    public string Description =>
        "Avec customErrors mode=\"Off\", les exceptions non-gerees affichent stack trace + paths disque + parfois connection strings au visiteur. Utiliser \"RemoteOnly\" (defaut) ou \"On\".";
    public bool HasFix => true;

    private static readonly Regex DetectRegex = new(
        @"<customErrors\b[^>]*\bmode\s*=\s*[""']Off[""']",
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
                    "Passer mode=\"RemoteOnly\" pour rester debuggable en local sans exposer en prod.");
            }
        }
    }

    public string? Fix(string content, RuleContext ctx)
    {
        if (ctx.Ext != "config") return content;
        return Regex.Replace(content,
            @"(<customErrors\b[^>]*\bmode\s*=\s*[""'])Off([""'])",
            "$1RemoteOnly$2",
            RegexOptions.IgnoreCase);
    }
}
