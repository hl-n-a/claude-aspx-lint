using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class Doc001MissingDoctype : IRule
{
    public string Id => "DOC-001";
    public string Name => "DOCTYPE manquant (ASPX seulement)";
    public Severity Severity => Severity.Warning;
    public string Description =>
        "Un fichier ASPX standalone (non-Content) devrait declarer un DOCTYPE pour activer le mode standards des navigateurs.";
    public bool HasFix => true;

    private static readonly Regex MasterPageFile = new(
        @"MasterPageFile\s*=", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DoctypePresent = new(
        @"<!DOCTYPE", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HtmlOpen = new(
        @"<html\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        if (ctx.Ext != "aspx") yield break;
        if (MasterPageFile.IsMatch(content)) yield break;       // content page : le master fournit le DOCTYPE
        if (DoctypePresent.IsMatch(content)) yield break;
        if (!HtmlOpen.IsMatch(content)) yield break;            // pas de <html> = sans doute un fragment

        yield return new Issue(Id, Name, Severity, 1, 1,
            "(absence de <!DOCTYPE>)",
            "Ajouter \"<!DOCTYPE html>\" avant la balise <html>.");
    }

    public string? Fix(string content, RuleContext ctx)
    {
        if (ctx.Ext != "aspx") return content;
        if (DoctypePresent.IsMatch(content) || MasterPageFile.IsMatch(content)) return content;
        var match = HtmlOpen.Match(content);
        if (!match.Success) return content;
        return content[..match.Index] + "<!DOCTYPE html>\n" + content[match.Index..];
    }
}
