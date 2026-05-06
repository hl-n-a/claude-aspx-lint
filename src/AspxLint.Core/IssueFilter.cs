using System.Text.RegularExpressions;

namespace AspxLint.Core;

/// <summary>
/// Filtre les issues d'apres des directives "disable" inline dans le source.
///
/// Syntaxe (dans un commentaire serveur &lt;%-- ... --%&gt; ou commentaire HTML) :
///   &lt;%-- aspx-lint disable TAG-003 --%&gt;        : ignore TAG-003 sur la ligne SUIVANTE
///   &lt;%-- aspx-lint disable TAG-003,ATTR-002 --%&gt; : plusieurs regles
///   &lt;%-- aspx-lint disable-line TAG-003 --%&gt;   : alias explicite (= meme effet)
///   &lt;%-- aspx-lint disable-file TAG-003 --%&gt;   : ignore TAG-003 dans tout le fichier
///   &lt;%-- aspx-lint disable-file --%&gt;            : sans liste = TOUTES les regles ignorees
///
/// Les commentaires HTML &lt;!-- aspx-lint disable ... --&gt; sont aussi reconnus.
/// </summary>
public static class IssueFilter
{
    private static readonly Regex DisableMarker = new(
        @"(?:<%--|<!--)\s*aspx-lint\s+(disable|disable-line|disable-file)(?:\s+([\w\-,\s]+?))?\s*(?:--%>|-->)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IEnumerable<Issue> ApplyDisableComments(string content, IEnumerable<Issue> issues)
    {
        var (lineDisables, fileDisables) = ParseDirectives(content);
        if (lineDisables.Count == 0 && fileDisables.Count == 0 && !fileDisables.Contains("*"))
            return issues;

        return issues.Where(i =>
        {
            // disable-file applies anywhere
            if (fileDisables.Contains("*")) return false;
            if (fileDisables.Contains(i.RuleId)) return false;
            // disable-line applies to the line just after the marker
            if (lineDisables.TryGetValue(i.Line, out var ruleSet))
            {
                if (ruleSet.Contains("*") || ruleSet.Contains(i.RuleId)) return false;
            }
            return true;
        });
    }

    private static (Dictionary<int, HashSet<string>> ByLine, HashSet<string> File) ParseDirectives(string content)
    {
        var byLine = new Dictionary<int, HashSet<string>>();
        var fileWide = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Pre-calcule offset -> ligne (1-indexed)
        var lineStarts = new List<int> { 0 };
        for (int i = 0; i < content.Length; i++)
            if (content[i] == '\n') lineStarts.Add(i + 1);

        int LineFromOffset(int off)
        {
            // BinarySearch ; renvoie l'index OU le complement
            int lo = 0, hi = lineStarts.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (lineStarts[mid] <= off) lo = mid;
                else hi = mid - 1;
            }
            return lo + 1;
        }

        foreach (Match m in DisableMarker.Matches(content))
        {
            var kind = m.Groups[1].Value.ToLowerInvariant();
            var rulesText = m.Groups[2].Value;
            var rules = string.IsNullOrWhiteSpace(rulesText)
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "*" }
                : new HashSet<string>(
                    rulesText.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                             .Select(s => s.Trim().ToUpperInvariant()),
                    StringComparer.OrdinalIgnoreCase);

            if (kind == "disable-file")
            {
                foreach (var r in rules) fileWide.Add(r);
                continue;
            }

            // disable et disable-line : ligne suivante
            var commentLine = LineFromOffset(m.Index);
            var targetLine = commentLine + 1;
            if (!byLine.TryGetValue(targetLine, out var set))
                byLine[targetLine] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rules) set.Add(r);
        }

        return (byLine, fileWide);
    }
}
