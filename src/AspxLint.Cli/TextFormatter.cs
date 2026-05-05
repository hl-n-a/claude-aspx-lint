using AspxLint.Core;

namespace AspxLint.Cli;

internal static class TextFormatter
{
    public static void Write(IReadOnlyList<ScannedFile> files, int totalIssues, TextWriter o)
    {
        foreach (var f in files)
        {
            if (f.Issues.Count == 0) continue;
            o.WriteLine();
            o.WriteLine($"{f.RelativePath}  ({f.Issues.Count} probleme(s))");
            foreach (var i in f.Issues)
            {
                var sev = i.Severity.ToString().ToLowerInvariant().PadRight(7);
                o.WriteLine($"  {sev} {f.RelativePath}:{i.Line}:{i.Col}  [{i.RuleId}] {i.Hint}");
            }
        }

        o.WriteLine();
        o.WriteLine($"{files.Count} fichier(s) scannes, {totalIssues} probleme(s) trouve(s).");
    }
}
