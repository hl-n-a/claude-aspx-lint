using AspxLint.Core;

namespace AspxLint.Cli;

/// <summary>
/// Format TAP (Test Anything Protocol) version 14 — consomme par les test
/// runners (npm tap-spec, prove, jest-tap, etc.). Chaque issue est un test
/// "not ok" si error, sinon "ok" avec un commentaire indiquant la severite.
/// </summary>
internal static class TapFormatter
{
    public static async Task WriteAsync(IReadOnlyList<ScannedFile> files, TextWriter o)
    {
        var allIssues = files.SelectMany(f => f.Issues.Select(i => (file: f, issue: i))).ToList();
        await o.WriteLineAsync("TAP version 14");
        await o.WriteLineAsync($"1..{allIssues.Count}");

        int n = 0;
        foreach (var (f, i) in allIssues)
        {
            n++;
            var status = i.Severity == Severity.Error ? "not ok" : "ok";
            var sev = i.Severity.ToString().ToLowerInvariant();
            await o.WriteLineAsync($"{status} {n} - {f.RelativePath}:{i.Line}:{i.Col} [{i.RuleId}] {i.Hint}");
            if (i.Severity != Severity.Error)
            {
                await o.WriteLineAsync($"  ---");
                await o.WriteLineAsync($"  severity: {sev}");
                await o.WriteLineAsync($"  rule: {i.RuleId}");
                await o.WriteLineAsync($"  ...");
            }
            else
            {
                // YAML diagnostic block pour les "not ok" (TAP 14 standard).
                await o.WriteLineAsync($"  ---");
                await o.WriteLineAsync($"  severity: error");
                await o.WriteLineAsync($"  rule: {i.RuleId}");
                await o.WriteLineAsync($"  message: \"{Escape(i.Hint)}\"");
                await o.WriteLineAsync($"  snippet: \"{Escape(i.Snippet)}\"");
                await o.WriteLineAsync($"  at:");
                await o.WriteLineAsync($"    file: \"{Escape(f.RelativePath)}\"");
                await o.WriteLineAsync($"    line: {i.Line}");
                await o.WriteLineAsync($"    column: {i.Col}");
                await o.WriteLineAsync($"  ...");
            }
        }
    }

    private static string Escape(string s) =>
        (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
}
