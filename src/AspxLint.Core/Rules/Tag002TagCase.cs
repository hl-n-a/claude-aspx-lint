using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class Tag002TagCase : IRule
{
    public string Id => "TAG-002";
    public string Name => "Casse incoherente des balises HTML";
    public Severity Severity => Severity.Warning;
    public string Description =>
        "Les balises HTML doivent etre en minuscules pour respecter HTML5/XHTML. Une casse mixte (<DIV>, <Div>) nuit a la lisibilite et peut poser des problemes avec certains parseurs.";
    public bool HasFix => true;

    // Detection : tags HTML standard avec une majuscule (hors controles namespaced asp:Foo, uc:Foo, ...).
    private static readonly Regex DetectRegex =
        new(@"<\/?([A-Z][a-zA-Z0-9]*)\b", RegexOptions.Compiled);

    private static readonly Regex NamespacedTag =
        new(@"^<\/?[a-zA-Z]+:", RegexOptions.Compiled);

    // Whitelist des tags HTML qu'on ose remettre en minuscules (le fix JS limite aussi a une whitelist).
    private static readonly string[] HtmlTags =
    {
        "html","head","body","div","span","p","a","img","br","hr",
        "ul","ol","li","table","tr","td","th","thead","tbody","tfoot",
        "form","input","button","select","option","textarea","label",
        "h1","h2","h3","h4","h5","h6","section","article","header","footer",
        "nav","main","aside","meta","link","title","script","style",
        "strong","em","b","i","u","small","code","pre","blockquote",
        "iframe","canvas","svg"
    };

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        // Mask <% ... %> blocks (including directives like <%@ Control Inherits="...<Generic>" %>)
        // so we don't false-positive on C# generics or HTML literals embedded in server code.
        var (_, maskedLines) = RuleHelpers.MaskAndSplit(content);

        for (int i = 0; i < maskedLines.Length; i++)
        {
            var line = maskedLines[i];
            foreach (Match m in DetectRegex.Matches(line))
            {
                // Skip controles namespaced : asp:Label, uc:Header, etc.
                if (NamespacedTag.IsMatch(line[m.Index..])) continue;
                yield return new Issue(Id, Name, Severity,
                    i + 1, m.Index + 1, m.Value,
                    $"Convertir \"{m.Groups[1].Value}\" en minuscules.");
            }
        }
    }

    public string? Fix(string content, RuleContext ctx)
    {
        // Pour chaque tag de la whitelist, remplace toutes les occurences case-insensitive
        // par sa forme minuscule, MAIS UNIQUEMENT en dehors des blocs <% %>. Sinon on
        // casse les types C# generics dans les directives (ex: ViewUserControl<MyType>).
        return RuleHelpers.ApplyOutsideAspBlocks(content, segment =>
        {
            var fixedSegment = segment;
            foreach (var tag in HtmlTags)
            {
                var re = new Regex(@"<(\/?)(" + tag + @")\b", RegexOptions.IgnoreCase);
                fixedSegment = re.Replace(fixedSegment, m => "<" + m.Groups[1].Value + tag);
            }
            return fixedSegment;
        });
    }
}
