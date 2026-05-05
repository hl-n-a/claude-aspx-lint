namespace AspxLint.Core.Rules;

public sealed class Ws005Bom : IRule
{
    public string Id => "WS-005";
    public string Name => "BOM en debut de fichier";
    public Severity Severity => Severity.Warning;
    public string Description =>
        "Un BOM UTF-8 (\\uFEFF) en debut de fichier ASPX peut perturber le moteur ASP.NET (espaces parasites avant <!DOCTYPE>) ainsi que certains outils de diff.";
    public bool HasFix => true;

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        if (content.Length > 0 && content[0] == '﻿')
            yield return new Issue(Id, Name, Severity,
                1, 1, "\\uFEFF (BOM)",
                "Enregistrer le fichier en UTF-8 sans BOM.");
    }

    public string? Fix(string content, RuleContext ctx) =>
        content.Length > 0 && content[0] == '﻿' ? content[1..] : content;
}
