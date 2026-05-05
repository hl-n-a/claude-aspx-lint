using System.Text.Json;
using AspxLint.Core;

namespace AspxLint.Cli;

/// <summary>
/// Formatter SARIF v2.1.0 — schema standard que GitHub Code Scanning sait ingerer
/// via l'action `github/codeql-action/upload-sarif`. Reference :
/// https://docs.oasis-open.org/sarif/sarif/v2.1.0/sarif-v2.1.0.html
/// </summary>
internal static class SarifFormatter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static async Task WriteAsync(
        IReadOnlyList<ScannedFile> files,
        IReadOnlyList<IRule> registry,
        TextWriter o)
    {
        var rulesByLevel = registry.Select(r => new
        {
            id = r.Id,
            name = r.Name,
            shortDescription = new { text = r.Name },
            fullDescription = new { text = r.Description },
            defaultConfiguration = new { level = SarifLevel(r.Severity) },
            properties = new { hasFix = r.HasFix }
        });

        var results = files.SelectMany(f => f.Issues.Select(i => new
        {
            ruleId = i.RuleId,
            level = SarifLevel(i.Severity),
            message = new { text = i.Hint ?? i.RuleName },
            locations = new[]
            {
                new
                {
                    physicalLocation = new
                    {
                        artifactLocation = new { uri = ToUri(f.RelativePath) },
                        region = new { startLine = i.Line, startColumn = i.Col }
                    }
                }
            }
        }));

        var sarif = new
        {
            version = "2.1.0",
            schema = "https://raw.githubusercontent.com/oasis-tcs/sarif-spec/master/Schemata/sarif-schema-2.1.0.json",
            runs = new[]
            {
                new
                {
                    tool = new
                    {
                        driver = new
                        {
                            name = "aspx-lint",
                            version = "0.1.0",
                            informationUri = "https://github.com/anthropics/aspx-lint",
                            rules = rulesByLevel
                        }
                    },
                    results
                }
            }
        };

        // System.Text.Json ne supporte pas les noms qui commencent par $ sans contournement ;
        // on serialise avec "schema" puis on patch en post pour respecter la spec SARIF.
        var json = JsonSerializer.Serialize(sarif, Options);
        json = json.Replace("\"schema\":", "\"$schema\":");
        await o.WriteLineAsync(json);
    }

    private static string SarifLevel(Severity sev) => sev switch
    {
        Severity.Error => "error",
        Severity.Warning => "warning",
        Severity.Info => "note",
        _ => "none"
    };

    private static string ToUri(string relativePath) =>
        relativePath.Replace('\\', '/');
}
