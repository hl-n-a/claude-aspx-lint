namespace AspxLint.Core.Rules;

public sealed class Ws004FinalNewline : IRule
{
    public string Id => "WS-004";
    public string Name => "Pas de saut de ligne final";
    public Severity Severity => Severity.Info;
    public string Description =>
        "POSIX recommande qu'un fichier texte se termine par un saut de ligne. Sinon, certains outils Unix l'affichent mal et Git signale 'No newline at end of file'.";
    public bool HasFix => true;

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        if (content.Length == 0) yield break;
        if (content.EndsWith('\n')) yield break;

        var lastLineLength = lines.Length == 0 ? 0 : lines[^1].Length;
        yield return new Issue(Id, Name, Severity,
            Math.Max(1, lines.Length), lastLineLength + 1,
            "— fin de fichier sans \\n",
            "Ajouter un saut de ligne a la fin du fichier.");
    }

    public string? Fix(string content, RuleContext ctx) =>
        content.EndsWith('\n') ? content : content + "\n";
}
