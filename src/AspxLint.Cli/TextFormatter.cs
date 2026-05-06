using AspxLint.Core;

namespace AspxLint.Cli;

internal static class TextFormatter
{
    /// <summary>
    /// Sortie texte humaine. Si <paramref name="useColor"/> est vrai et que le
    /// terminal le supporte, on encadre la severite avec des codes ANSI ; sinon
    /// on emet le texte brut (pour les pipes / redirections / CI).
    /// </summary>
    public static void Write(
        IReadOnlyList<ScannedFile> files,
        int totalIssues,
        TextWriter o,
        bool useColor = true,
        bool quiet = false)
    {
        if (quiet)
        {
            o.WriteLine($"{files.Count} fichier(s) scannes, {totalIssues} probleme(s) trouve(s).");
            return;
        }

        foreach (var f in files)
        {
            if (f.Issues.Count == 0) continue;
            o.WriteLine();
            o.WriteLine(Bold($"{f.RelativePath}  ({f.Issues.Count} probleme(s))", useColor));
            foreach (var i in f.Issues)
            {
                var sev = ColorSeverity(i.Severity, useColor);
                o.WriteLine($"  {sev} {f.RelativePath}:{i.Line}:{i.Col}  {Dim($"[{i.RuleId}]", useColor)} {i.Hint}");
            }
        }

        o.WriteLine();
        o.WriteLine($"{files.Count} fichier(s) scannes, {totalIssues} probleme(s) trouve(s).");
    }

    // --- ANSI helpers ---
    // ESC est U+001B. On utilise (char)27 pour eviter tout probleme d'encodage
    // dans le source, et on construit les codes au runtime.
    private static readonly string Esc = ((char)27).ToString();
    private static string Reset  => Esc + "[0m";
    private static string BoldOn => Esc + "[1m";
    private static string DimOn  => Esc + "[2m";
    private static string Red    => Esc + "[31m";
    private static string Yellow => Esc + "[33m";
    private static string Blue   => Esc + "[34m";

    private static string ColorSeverity(Severity sev, bool useColor)
    {
        var label = sev.ToString().ToLowerInvariant().PadRight(7);
        if (!useColor) return label;
        var prefix = sev switch
        {
            Severity.Error   => Red + BoldOn,
            Severity.Warning => Yellow,
            Severity.Info    => Blue,
            _                => ""
        };
        return prefix + label + Reset;
    }

    private static string Bold(string s, bool useColor) =>
        useColor ? BoldOn + s + Reset : s;

    private static string Dim(string s, bool useColor) =>
        useColor ? DimOn + s + Reset : s;
}
