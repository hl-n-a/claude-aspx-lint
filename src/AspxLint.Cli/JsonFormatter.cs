using System.Text.Json;
using AspxLint.Core;

namespace AspxLint.Cli;

internal static class JsonFormatter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task WriteAsync(IReadOnlyList<ScannedFile> files, int totalIssues, TextWriter o)
    {
        var payload = new
        {
            scannedAt = DateTime.UtcNow,
            fileCount = files.Count,
            issueCount = totalIssues,
            files = files.Select(f => new
            {
                path = f.AbsolutePath,
                relativePath = f.RelativePath,
                lineCount = f.LineCount,
                issues = f.Issues.Select(i => new
                {
                    ruleId = i.RuleId,
                    ruleName = i.RuleName,
                    severity = i.Severity.ToString().ToLowerInvariant(),
                    line = i.Line,
                    col = i.Col,
                    snippet = i.Snippet,
                    hint = i.Hint
                })
            })
        };
        await o.WriteLineAsync(JsonSerializer.Serialize(payload, Options));
    }
}
