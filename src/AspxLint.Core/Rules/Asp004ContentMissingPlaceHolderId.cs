using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class Asp004ContentMissingPlaceHolderId : IRule
{
    public string Id => "ASP-004";
    public string Name => "Content sans ContentPlaceHolderID (page enfant)";
    public Severity Severity => Severity.Error;
    public string Description =>
        "Une balise <asp:Content> doit referencer un placeholder du master via ContentPlaceHolderID, sinon la page ne saura pas ou injecter le contenu.";
    public bool HasFix => false;

    private static readonly Regex ContentRegex = new(
        @"<asp:Content\b([^>]*?)>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CphIdAttr = new(
        @"ContentPlaceHolderID\s*=\s*[""'][^""']+[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            foreach (Match m in ContentRegex.Matches(lines[i]))
            {
                if (CphIdAttr.IsMatch(m.Groups[1].Value)) continue;
                yield return new Issue(Id, Name, Severity,
                    i + 1, m.Index + 1, m.Value,
                    "Ajouter ContentPlaceHolderID=\"...\" pointant vers un placeholder du master.");
            }
        }
    }

    public string? Fix(string content, RuleContext ctx) => null;
}
