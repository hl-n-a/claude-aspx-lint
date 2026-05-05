using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class Tag003UnbalancedTags : IRule
{
    public string Id => "TAG-003";
    public string Name => "Balises non equilibrees";
    public Severity Severity => Severity.Error;
    public string Description =>
        "Les balises ouvertes doivent etre fermees (sauf balises vides). Une pile non vide en fin de fichier signale une erreur de structure HTML.";
    public bool HasFix => false;

    private static readonly HashSet<string> VoidTags = new()
    {
        "br","hr","img","input","meta","link","area","base","col","embed","source","track","wbr"
    };

    private static readonly Regex TagRegex = new(
        @"<\/?([a-zA-Z][a-zA-Z0-9:_\-]*)\b[^>]*?(\/?)>",
        RegexOptions.Compiled);

    // On masque code serveur, commentaires, scripts, styles avant tokenisation
    // (meme strategie que la version JS).
    private static readonly Regex AspBlock = new(@"<%[\s\S]*?%>", RegexOptions.Compiled);
    private static readonly Regex HtmlComment = new(@"<!--[\s\S]*?-->", RegexOptions.Compiled);
    private static readonly Regex ScriptBlock = new(
        @"<script\b[^>]*>[\s\S]*?<\/script>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex StyleBlock = new(
        @"<style\b[^>]*>[\s\S]*?<\/style>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        // Pre-calcul : offset de debut de chaque ligne, pour mapper match.Index -> ligne 1-indexee.
        var lineStarts = new int[lines.Length];
        int pos = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            lineStarts[i] = pos;
            pos += lines[i].Length + 1; // +1 pour le \n
        }

        // Masque les zones a ignorer en gardant la longueur (on remplace par des espaces).
        var cleaned = AspBlock.Replace(content, m => new string(' ', m.Length));
        cleaned = HtmlComment.Replace(cleaned, m => new string(' ', m.Length));
        cleaned = ScriptBlock.Replace(cleaned, m => new string(' ', m.Length));
        cleaned = StyleBlock.Replace(cleaned, m => new string(' ', m.Length));

        var issues = new List<Issue>();
        var stack = new Stack<(string tag, int line)>();

        foreach (Match m in TagRegex.Matches(cleaned))
        {
            var tag = m.Groups[1].Value.ToLowerInvariant();
            var isClose = m.Value.StartsWith("</");
            var isSelfClose = m.Groups[2].Value == "/" || VoidTags.Contains(tag);
            if (isSelfClose && !isClose) continue;

            if (isClose)
            {
                if (stack.Count == 0)
                {
                    issues.Add(new Issue(Id, Name, Severity,
                        LineFromOffset(m.Index, lineStarts), 1, m.Value,
                        $"Balise fermante </{tag}> sans ouverture correspondante."));
                }
                else
                {
                    var top = stack.Peek();
                    if (top.tag == tag)
                    {
                        stack.Pop();
                    }
                    else
                    {
                        issues.Add(new Issue(Id, Name, Severity,
                            LineFromOffset(m.Index, lineStarts), 1, m.Value,
                            $"Imbrication incorrecte : </{tag}> ferme alors que <{top.tag}> est encore ouvert (ligne {top.line})."));
                        stack.Pop();
                    }
                }
            }
            else
            {
                stack.Push((tag, LineFromOffset(m.Index, lineStarts)));
            }
        }

        // Ce qui reste dans la pile = jamais ferme.
        foreach (var s in stack)
        {
            issues.Add(new Issue(Id, Name, Severity,
                s.line, 1, $"<{s.tag}>",
                $"Balise <{s.tag}> jamais fermee."));
        }

        return issues;
    }

    public string? Fix(string content, RuleContext ctx) => null;

    private static int LineFromOffset(int offset, int[] lineStarts)
    {
        int lo = 0, hi = lineStarts.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (lineStarts[mid] <= offset) lo = mid;
            else hi = mid - 1;
        }
        return lo + 1;
    }
}
