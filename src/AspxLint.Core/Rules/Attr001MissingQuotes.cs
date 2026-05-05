using System.Text;
using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class Attr001MissingQuotes : IRule
{
    public string Id => "ATTR-001";
    public string Name => "Attribut sans guillemets";
    public Severity Severity => Severity.Warning;
    public string Description =>
        "Les valeurs d'attributs doivent toujours etre entre guillemets doubles pour respecter XHTML. Sinon les caracteres speciaux et espaces posent probleme.";
    public bool HasFix => true;

    // attr=value — value est tout sauf espace, guillemet, < > = / (donc une vraie valeur "nue").
    private static readonly Regex AttrUnquoted = new(
        @"\s([a-zA-Z][a-zA-Z0-9\-:_]*)=([^\s""'<>=/]+)",
        RegexOptions.Compiled);

    private static readonly Regex Comments = new(@"<!--|-->", RegexOptions.Compiled);
    private static readonly Regex Directive = new(@"<%@", RegexOptions.Compiled);

    private static readonly Regex AspBlock = new(@"<%[\s\S]*?%>", RegexOptions.Compiled);
    private static readonly Regex TagWithAttrs = new(
        @"(<[a-zA-Z][a-zA-Z0-9:_\-]*\b)([^>]*)>",
        RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (Comments.IsMatch(line)) continue;
            if (Directive.IsMatch(line)) continue;

            foreach (Match m in AttrUnquoted.Matches(line))
            {
                // Verifie qu'on est bien a l'interieur d'une balise non encore fermee.
                var before = line[..m.Index];
                var lastOpen = before.LastIndexOf('<');
                var lastClose = before.LastIndexOf('>');
                if (lastOpen <= lastClose) continue;

                yield return new Issue(Id, Name, Severity,
                    i + 1, m.Index + 1, m.Value.Trim(),
                    $"Entourer la valeur de guillemets : {m.Groups[1].Value}=\"{m.Groups[2].Value}\"");
            }
        }
    }

    public string? Fix(string content, RuleContext ctx)
    {
        // Decoupe par bloc <% %> pour ne JAMAIS toucher au code serveur.
        var sb = new StringBuilder(content.Length + 64);
        int last = 0;
        foreach (Match m in AspBlock.Matches(content))
        {
            sb.Append(FixSegment(content[last..m.Index]));
            sb.Append(m.Value);
            last = m.Index + m.Length;
        }
        sb.Append(FixSegment(content[last..]));
        return sb.ToString();
    }

    private static string FixSegment(string text) =>
        TagWithAttrs.Replace(text, m =>
            m.Groups[1].Value
            + AttrUnquoted.Replace(m.Groups[2].Value,
                mm => $" {mm.Groups[1].Value}=\"{mm.Groups[2].Value}\"")
            + ">");
}
