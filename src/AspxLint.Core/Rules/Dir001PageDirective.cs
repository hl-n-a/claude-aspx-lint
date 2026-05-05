using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class Dir001PageDirective : IRule
{
    public string Id => "DIR-001";
    public string Name => "Directive de page absente ou mal placee";
    public Severity Severity => Severity.Error;
    public string Description =>
        "Un fichier ASPX doit commencer par <%@ Page ... %>, un ASCX par <%@ Control ... %>, un MASTER par <%@ Master ... %>. La directive doit etre en premiere ligne non vide.";
    public bool HasFix => true;

    private static string? Expected(string ext) => ext switch
    {
        "aspx" => "Page",
        "ascx" => "Control",
        "master" => "Master",
        _ => null
    };

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        var expected = Expected(ctx.Ext);
        if (expected is null) yield break;

        int firstNonEmpty = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i])) { firstNonEmpty = i; break; }
        }
        if (firstNonEmpty < 0) yield break;

        var re = new Regex(@"<%@\s*" + expected + @"\b", RegexOptions.IgnoreCase);
        var hasDirective = lines.Any(l => re.IsMatch(l));

        if (!hasDirective)
        {
            yield return new Issue(Id, Name, Severity, 1, 1,
                lines[firstNonEmpty],
                $"Directive @{expected} manquante. Ajoutez-la en premiere ligne.");
        }
        else if (firstNonEmpty > 0 && !re.IsMatch(lines[firstNonEmpty]))
        {
            int dirLine = -1;
            for (int i = 0; i < lines.Length; i++)
                if (re.IsMatch(lines[i])) { dirLine = i; break; }

            if (dirLine != firstNonEmpty && dirLine >= 0)
                yield return new Issue(Id, Name, Severity, dirLine + 1, 1,
                    lines[dirLine],
                    $"La directive @{expected} doit etre la premiere ligne non vide du fichier.");
        }
    }

    public string? Fix(string content, RuleContext ctx)
    {
        var expected = Expected(ctx.Ext);
        if (expected is null) return content;

        var re = new Regex(@"<%@\s*" + expected + @"\b", RegexOptions.IgnoreCase);
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        int dirIndex = -1;
        for (int i = 0; i < lines.Count; i++)
            if (re.IsMatch(lines[i])) { dirIndex = i; break; }

        if (dirIndex < 0)
        {
            var stub = $"<%@ {expected} Language=\"C#\" AutoEventWireup=\"true\" %>";
            return stub + "\n" + content;
        }
        if (dirIndex > 0)
        {
            var dirLine = lines[dirIndex];
            lines.RemoveAt(dirIndex);
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0])) lines.RemoveAt(0);
            lines.Insert(0, dirLine);
            return string.Join("\n", lines);
        }
        return content;
    }
}
