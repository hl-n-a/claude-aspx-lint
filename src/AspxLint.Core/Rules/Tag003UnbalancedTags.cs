using System.Text;
using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class Tag003UnbalancedTags : IRule
{
    public string Id => "TAG-003";
    public string Name => "Balises non equilibrees";
    public Severity Severity => Severity.Error;
    public string Description =>
        "Les balises ouvertes doivent etre fermees (sauf balises vides). Le fix automatique gere deux cas : (a) imbrication incorrecte ou un parent doit etre ferme avant un autre (insertion des </tag> manquants), (b) balise jamais fermee a la fin du fichier (append du </tag>). Les cas de fermeture orpheline (</tag> sans ouverture) sont laisses tels quels — ils necessitent une revue humaine.";
    public bool HasFix => true;

    private static readonly HashSet<string> VoidTags = new()
    {
        "br","hr","img","input","meta","link","area","base","col","embed","source","track","wbr"
    };

    private static readonly Regex TagRegex = new(
        @"<\/?([a-zA-Z][a-zA-Z0-9:_\-]*)\b[^>]*?(\/?)>",
        RegexOptions.Compiled);

    private static readonly Regex HtmlComment = new(@"<!--[\s\S]*?-->", RegexOptions.Compiled);
    private static readonly Regex ScriptBlock = new(
        @"<script\b[^>]*>[\s\S]*?<\/script>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex StyleBlock = new(
        @"<style\b[^>]*>[\s\S]*?<\/style>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        var lineStarts = ComputeLineStarts(lines);
        var cleaned = MaskOutNoise(content);

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

        foreach (var s in stack)
        {
            issues.Add(new Issue(Id, Name, Severity,
                s.line, 1, $"<{s.tag}>",
                $"Balise <{s.tag}> jamais fermee."));
        }

        return issues;
    }

    /// <summary>
    /// Fix : on rejoue la stack walk. A chaque fermeture mal imbriquee,
    /// si la balise correspondante est plus loin dans la stack, on insere
    /// les </intermediaire> manquantes juste avant cette fermeture (avec
    /// l'indentation du contexte) puis on pop. A la fin, pour les balises
    /// jamais fermees, on append les </tag> a la fin du fichier. Les cas
    /// "fermante sans ouverture" sont laisses tels quels.
    /// </summary>
    public string? Fix(string content, RuleContext ctx)
    {
        var cleaned = MaskOutNoise(content);
        var stack = new Stack<(string tag, int openIndex)>();
        var insertions = new List<(int offset, string text)>();

        foreach (Match m in TagRegex.Matches(cleaned))
        {
            var tag = m.Groups[1].Value.ToLowerInvariant();
            var isClose = m.Value.StartsWith("</");
            var isSelfClose = m.Groups[2].Value == "/" || VoidTags.Contains(tag);
            if (isSelfClose && !isClose) continue;

            if (!isClose)
            {
                stack.Push((tag, m.Index));
                continue;
            }

            if (stack.Count == 0) continue;   // fermeture orpheline : on touche pas

            if (stack.Peek().tag == tag)
            {
                stack.Pop();
                continue;
            }

            // Imbrication incorrecte : cherche `tag` plus profond dans la stack.
            int depth = -1;
            int i = 0;
            foreach (var item in stack)
            {
                if (item.tag == tag) { depth = i; break; }
                i++;
            }

            if (depth < 0) continue;   // pas trouve : fermeture etrangere, manuel

            // Insertion : les balises au-dessus de `tag` dans la stack doivent etre
            // fermees AVANT cette fermeture. On les recupere (top en premier).
            var toClose = new List<string>();
            for (int k = 0; k < depth; k++) toClose.Add(stack.Pop().tag);
            stack.Pop();   // pop la balise qui correspond a `tag`

            // Indentation : on prend celle de la ligne qui contient la fermeture.
            int lineStart = content.LastIndexOf('\n', m.Index) + 1;
            int indentEnd = lineStart;
            while (indentEnd < m.Index && (content[indentEnd] == ' ' || content[indentEnd] == '\t'))
                indentEnd++;
            var indent = content[lineStart..indentEnd];

            var sb = new StringBuilder();
            foreach (var t in toClose) sb.Append($"</{t}>\n").Append(indent);
            insertions.Add((m.Index, sb.ToString()));
        }

        // Fin de fichier : balises jamais fermees -> append en bout.
        if (stack.Count > 0)
        {
            var sb = new StringBuilder();
            foreach (var s in stack) sb.Append($"</{s.tag}>\n");
            // On insere avant le dernier \n si le fichier termine par un newline,
            // sinon a la fin pure.
            int insertAt = content.Length;
            if (insertAt > 0 && content[insertAt - 1] == '\n') insertAt--;
            insertions.Add((insertAt, sb.ToString()));
        }

        if (insertions.Count == 0) return content;

        // Applique les insertions de la fin vers le debut pour preserver les offsets.
        insertions.Sort((a, b) => b.offset.CompareTo(a.offset));
        var result = new StringBuilder(content);
        foreach (var (offset, text) in insertions)
            result.Insert(offset, text);

        return result.ToString();
    }

    private static string MaskOutNoise(string content)
    {
        var cleaned = RuleHelpers.MaskAspBlocks(content);
        cleaned = HtmlComment.Replace(cleaned, m => new string(' ', m.Length));
        cleaned = ScriptBlock.Replace(cleaned, m => new string(' ', m.Length));
        cleaned = StyleBlock.Replace(cleaned, m => new string(' ', m.Length));
        return cleaned;
    }

    private static int[] ComputeLineStarts(string[] lines)
    {
        var starts = new int[lines.Length];
        int pos = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            starts[i] = pos;
            pos += lines[i].Length + 1;
        }
        return starts;
    }

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
