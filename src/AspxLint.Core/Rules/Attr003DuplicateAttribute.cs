using System.Text;
using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class Attr003DuplicateAttribute : IRule
{
    public string Id => "ATTR-003";
    public string Name => "Attributs en double dans une balise";
    public Severity Severity => Severity.Error;
    public string Description =>
        "Une meme balise ne doit pas contenir deux fois le meme attribut. Le navigateur ne conserve generalement que le premier, ce qui cause des bugs subtils. Le fix automatique merge les valeurs de `class` (cas le plus frequent) et garde la premiere occurrence pour les autres attributs.";
    public bool HasFix => true;

    // Detect : on travaille sur le contenu masque pour eviter les faux positifs
    // sur du HTML embarque dans une chaine C#.
    private static readonly Regex DetectTagRegex =
        new(@"<([a-zA-Z][a-zA-Z0-9:_\-]*)\b([^>]*?)>", RegexOptions.Compiled);

    // Fix : sur contenu brut, doit traverser les <%...%> en attribut.
    private static readonly Regex FixTagRegex =
        new($@"<([a-zA-Z][a-zA-Z0-9:_\-]*)\b({RuleHelpers.TagInnerPattern})>",
            RegexOptions.Compiled);

    private static readonly Regex AttrRegex = new(
        @"(\s+)([a-zA-Z_][a-zA-Z0-9\-:_]*)(\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+))?",
        RegexOptions.Compiled);

    private static readonly Regex AttrNameRegex =
        new(@"\s([a-zA-Z][a-zA-Z0-9\-:_]*)\s*=", RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        var (_, maskedLines) = RuleHelpers.MaskAndSplit(content);

        for (int i = 0; i < maskedLines.Length; i++)
        {
            foreach (Match m in DetectTagRegex.Matches(maskedLines[i]))
            {
                var attrs = m.Groups[2].Value;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (Match am in AttrNameRegex.Matches(attrs))
                {
                    var name = am.Groups[1].Value;
                    if (!seen.Add(name))
                    {
                        var snippet = m.Value.Length > 80 ? m.Value[..80] + "…" : m.Value;
                        yield return new Issue(Id, Name, Severity,
                            i + 1, m.Index + 1, snippet,
                            $"L'attribut \"{name}\" apparait plusieurs fois dans cette balise.");
                        break;
                    }
                }
            }
        }
    }

    public string? Fix(string content, RuleContext ctx) =>
        RuleHelpers.FixOutsideAspBlocks(content, FixTagRegex, m =>
        {
            var tagName = m.Groups[1].Value;
            var attrs = m.Groups[2].Value;
            var dedup = DedupAttributes(attrs);
            return dedup == attrs ? m.Value : $"<{tagName}{dedup}>";
        });

    /// <summary>
    /// Pour chaque nom d'attribut duplique :
    ///   - "class" : merge les valeurs (split sur espaces, dedupe, rejoin)
    ///   - autres  : garde la premiere occurrence, supprime les autres
    /// Les attributs uniques et les whitespace originaux sont preserves.
    /// </summary>
    private static string DedupAttributes(string attrs)
    {
        var matches = AttrRegex.Matches(attrs).Cast<Match>().ToList();
        if (matches.Count == 0) return attrs;

        // Indexe par nom (case-insensitive) en preservant l'ordre d'apparition.
        var byName = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < matches.Count; i++)
        {
            var name = matches[i].Groups[2].Value;
            if (!byName.TryGetValue(name, out var list))
                byName[name] = list = new List<int>();
            list.Add(i);
        }

        // Decide quel match supprimer / remplacer.
        var skip = new HashSet<int>();
        var replace = new Dictionary<int, string>();   // index -> nouveau texte d'attribut entier

        foreach (var (name, indices) in byName)
        {
            if (indices.Count <= 1) continue;

            if (name.Equals("class", StringComparison.OrdinalIgnoreCase))
            {
                // Merge des tokens de toutes les valeurs.
                var tokens = new List<string>();
                var seenTokens = new HashSet<string>();
                foreach (var idx in indices)
                {
                    var val = ExtractValue(matches[idx]);
                    foreach (var t in val.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                        if (seenTokens.Add(t)) tokens.Add(t);
                }
                replace[indices[0]] = $"class=\"{string.Join(" ", tokens)}\"";
                for (int j = 1; j < indices.Count; j++) skip.Add(indices[j]);
            }
            else
            {
                // Standard HTML : on garde le 1er, on jette les suivants.
                for (int j = 1; j < indices.Count; j++) skip.Add(indices[j]);
            }
        }

        if (skip.Count == 0 && replace.Count == 0) return attrs;

        // Reconstruction avec preservation des espaces hors-attributs.
        var sb = new StringBuilder(attrs.Length);
        int last = 0;
        for (int i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            sb.Append(attrs, last, m.Index - last);
            if (skip.Contains(i))
            {
                // Saute ce match en entier (incluant son whitespace de tete).
            }
            else if (replace.TryGetValue(i, out var replacement))
            {
                sb.Append(m.Groups[1].Value);   // whitespace original
                sb.Append(replacement);          // nouveau attr
            }
            else
            {
                sb.Append(m.Value);
            }
            last = m.Index + m.Length;
        }
        sb.Append(attrs, last, attrs.Length - last);
        return sb.ToString();
    }

    private static string ExtractValue(Match m)
    {
        var eqGroup = m.Groups[3].Value;
        if (string.IsNullOrEmpty(eqGroup)) return "";
        var eqIdx = eqGroup.IndexOf('=');
        if (eqIdx < 0) return "";
        var raw = eqGroup[(eqIdx + 1)..].Trim();
        if (raw.Length >= 2 && (raw[0] == '"' || raw[0] == '\'') && raw[^1] == raw[0])
            return raw[1..^1];
        return raw;
    }
}
