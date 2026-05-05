using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class Ws002MixedIndent : IRule
{
    public string Id => "WS-002";
    public string Name => "Indentation mixte (tabulations + espaces)";
    public Severity Severity => Severity.Warning;
    public string Description =>
        "Melanger tabulations et espaces dans l'indentation d'un meme fichier rend l'affichage variable selon l'editeur. Choisir l'un ou l'autre.";
    public bool HasFix => true;

    private static readonly Regex IndentRegex = new(@"^[ \t]*", RegexOptions.Compiled);
    private static readonly Regex LeadingTabRegex = new(@"^\t", RegexOptions.Compiled);
    private static readonly Regex LeadingSpacesRegex = new(@"^ +", RegexOptions.Compiled);
    private static readonly Regex IndentReplaceRegex = new(@"^[ \t]+", RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        bool hasTab = false, hasSpace = false;
        foreach (var line in lines)
        {
            var indent = IndentRegex.Match(line).Value;
            if (indent.Contains('\t')) hasTab = true;
            if (LeadingSpacesRegex.IsMatch(line)) hasSpace = true;
        }
        if (!(hasTab && hasSpace)) yield break;

        var emitted = false;
        for (int i = 0; i < lines.Length; i++)
        {
            var indent = IndentRegex.Match(lines[i]).Value;
            if (indent.Contains('\t') && indent.Contains(' '))
            {
                emitted = true;
                var stripped = lines[i].TrimStart();
                yield return new Issue(Id, Name, Severity, i + 1, 1,
                    "⇥ + ⎵ " + (stripped.Length <= 50 ? stripped : stripped[..50]),
                    "Cette ligne melange tabulations et espaces dans son indentation.");
            }
        }
        if (!emitted)
        {
            // Aucun melange intra-ligne, mais le fichier melange des lignes-tab et des lignes-espace.
            int firstTab = -1, firstSpace = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (firstTab < 0 && LeadingTabRegex.IsMatch(lines[i])) firstTab = i;
                if (firstSpace < 0 && LeadingSpacesRegex.IsMatch(lines[i])) firstSpace = i;
                if (firstTab >= 0 && firstSpace >= 0) break;
            }
            if (firstTab >= 0)
            {
                var stripped = lines[firstTab].TrimStart();
                yield return new Issue(Id, Name, Severity, firstTab + 1, 1,
                    "⇥ " + (stripped.Length <= 50 ? stripped : stripped[..50]),
                    $"Le fichier melange indentation par tabulation (ici) et par espaces (ligne {firstSpace + 1}).");
            }
        }
    }

    public string? Fix(string content, RuleContext ctx)
    {
        // Convertit toute tab d'indentation en 4 espaces — ne touche pas aux tabs au milieu d'une ligne.
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        for (int i = 0; i < lines.Length; i++)
        {
            var m = IndentReplaceRegex.Match(lines[i]);
            if (!m.Success) continue;
            var indent = m.Value.Replace("\t", "    ");
            lines[i] = indent + lines[i][m.Length..];
        }
        return string.Join("\n", lines);
    }
}
