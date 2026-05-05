using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class Asp003ContentPlaceHolderMissingId : IRule
{
    public string Id => "ASP-003";
    public string Name => "ContentPlaceHolder sans ID (MASTER)";
    public Severity Severity => Severity.Error;
    public string Description =>
        "Dans un fichier MASTER, chaque <asp:ContentPlaceHolder> doit avoir un attribut ID unique pour que les pages enfants puissent y injecter du contenu.";
    public bool HasFix => false;

    private static readonly Regex CphRegex = new(
        @"<asp:ContentPlaceHolder\b([^>]*?)\/?>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex IdAttrRegex = new(
        @"\bID\s*=\s*[""'][^""']+[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        if (ctx.Ext != "master") yield break;

        for (int i = 0; i < lines.Length; i++)
        {
            foreach (Match m in CphRegex.Matches(lines[i]))
            {
                if (IdAttrRegex.IsMatch(m.Groups[1].Value)) continue;
                yield return new Issue(Id, Name, Severity,
                    i + 1, m.Index + 1, m.Value,
                    "Ajouter un attribut ID unique a ce ContentPlaceHolder.");
            }
        }
    }

    public string? Fix(string content, RuleContext ctx) => null;
}
